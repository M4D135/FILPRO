using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Filpro.Core;

namespace Filpro.App;

/// <summary>
/// Dossier de données de WebView2. WebView2 lance ses processus sous le compte de la session Windows (propriétaire du
/// bureau), jamais sous le compte élevé : quand un utilisateur standard ouvre FILPRO avec un autre compte administrateur
/// saisi dans l'UAC, l'affichage tourne donc sous le compte standard (comportement documenté, WebView2Feedback #932),
/// qui ne peut pas entrer dans le profil de l'administrateur. Dans ce seul cas, le profil WebView2 va sous ProgramData,
/// dans un dossier réservé à SYSTEM, aux Administrateurs et au compte de la session. Les données de FILPRO (journal,
/// réglages, opérations) restent dans le dossier de l'administrateur. Sans élévation, ou quand la session et FILPRO
/// ont le même compte, rien ne change.
/// </summary>
internal static class WebViewDataLocation
{
    internal const string SharedFolder = "WebView2";
    // Écriture complète des fichiers et sous-dossiers, sans droit de changer les autorisations ni le propriétaire.
    private const FileSystemRights SessionRights = FileSystemRights.Modify | FileSystemRights.Synchronize;
    // Plus haut que le compte de la session peut tenir : tout FILE_ALL_ACCESS sauf autorisations et propriétaire.
    private const FileSystemRights SessionMaximum = FileSystemRights.FullControl & ~(FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership);
    private const InheritanceFlags AllChildren = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    /// <summary>Dossier prêt pour un profil d'affichage ; compte (nom et SID) de la session quand ce dossier a été placé pour lui.</summary>
    internal readonly record struct ProfileFolder(string Path, string? SessionAccount, string? SessionSid = null);

    /// <summary>Le profil WebView2 doit-il quitter le dossier de FILPRO pour un dossier accessible au compte de la session ?</summary>
    internal static bool NeedsSessionFolder(bool elevated, string processSid, string? sessionSid)
        => elevated && sessionSid is not null && !string.Equals(sessionSid, processSid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prépare le dossier d'un profil d'affichage : sous ProgramData quand WebView2 tournera sous un autre compte que
    /// FILPRO, sinon sous le dossier de données de FILPRO. Le nom du profil est vérifié dans les deux cas. Le compte de la
    /// session n'est rendu qu'une fois son dossier créé ou vérifié : après un échec, aucun message ne le met en cause.
    /// </summary>
    internal static ProfileFolder PrepareProfile(string profile, bool elevated, SecurityIdentifier processUser, string? sessionSid,
        string programData, string? localData)
    {
        var segments = ProfileSegments(profile);
        if (NeedsSessionFolder(elevated, processUser.Value, sessionSid))
            return new ProfileFolder(Prepare(programData, sessionSid!, processUser, segments), AccountName(sessionSid!), sessionSid);
        if (localData is null)
            throw new FilproOperationException("webview.data",
                "Aucun dossier de données utilisable pour ce compte Windows : FILPRO ne peut pas initialiser son interface.");
        var path = Path.Combine([localData, .. segments]);
        Directory.CreateDirectory(path);
        return new ProfileFolder(path, null);
    }

    /// <summary>Segments d'un nom de profil relatif : aucun segment vide, « . », « .. » ni caractère interdit.</summary>
    internal static string[] ProfileSegments(string profile)
    {
        var segments = profile.Split(['\\', '/'], StringSplitOptions.None);
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Nom de profil d’affichage invalide : " + profile);
        return segments;
    }

    /// <summary>
    /// Compte sous lequel WebView2 lancera ses processus : propriétaire du bureau de la session (l'Explorateur), sinon
    /// l'utilisateur de la session. Nul s'il est introuvable : le comportement historique est alors conservé.
    /// </summary>
    internal static string? SessionAccountSid()
    {
        try
        {
            var shell = Native.GetShellWindow();
            if (shell != IntPtr.Zero && Native.GetWindowThreadProcessId(shell, out var pid) != 0 && pid != 0)
            {
                using var self = Process.GetCurrentProcess();
                if (SessionProcessOwners((uint)self.SessionId).TryGetValue(pid, out var owner)) return owner;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or SystemException) { }
        try { return SessionUser(); }
        catch (Exception ex) when (ex is Win32Exception or SystemException) { return null; }
    }

    /// <summary>Nom lisible d'un compte pour les messages ; le SID sinon.</summary>
    internal static string AccountName(string sid)
    {
        try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return sid; }
    }

    /// <summary>
    /// Crée ou vérifie &lt;ProgramData&gt;\FILPRO-V2\WebView2\&lt;compte de la session&gt;\&lt;compte de FILPRO&gt;\&lt;profil&gt;.
    /// Chaque niveau est créé par ce processus élevé sans suivre de lien et reste réservé à SYSTEM et aux Administrateurs ;
    /// seul le dossier final donne au compte de la session le droit d'écrire, sans pouvoir en changer les autorisations.
    /// Un dossier existant n'est jamais réparé : une protection différente est refusée.
    /// </summary>
    internal static string Prepare(string programData, string sessionSid, SecurityIdentifier processUser, string profile)
        => Prepare(programData, sessionSid, processUser, ProfileSegments(profile));

    private static string Prepare(string programData, string sessionSid, SecurityIdentifier processUser, string[] segments)
    {
        var session = new SecurityIdentifier(sessionSid);
        if (!Path.IsPathFullyQualified(programData) || !Directory.Exists(programData))
            throw new IOException("ProgramData indisponible pour le profil d’affichage.");
        var current = programData;
        foreach (var segment in new[] { UserDataLocation.FallbackFolder, SharedFolder, session.Value, processUser.Value }.Concat(segments.SkipLast(1)))
        {
            current = Path.Combine(current, segment);
            UserDataLocation.EnsureProtected(current, processUser);
        }
        var leaf = Path.Combine(current, segments[^1]);
        EnsureSessionWritable(leaf, session);
        return leaf;
    }

    private static void EnsureSessionWritable(string path, SecurityIdentifier session)
    {
        UserDataLocation.RefuseReparse(path);
        if (!Directory.Exists(path))
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { UserDataLocation.SystemSid, UserDataLocation.AdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, AllChildren, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(session, SessionRights, AllChildren, PropagationFlags.None, AccessControlType.Allow));
            security.SetOwner(new SecurityIdentifier(UserDataLocation.AdministratorsSid));
            new DirectoryInfo(path).Create(security);
        }
        var descriptor = UserDataLocation.ReadDescriptor(UserDataLocation.Pin(path), path);
        var problem = UserDataLocation.ReadEntries(descriptor, out var entries)
            ?? SessionFolderProblem(descriptor.Owner?.Value, session.Value, descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), entries);
        if (problem is not null) throw new UnauthorizedAccessException(problem + " : " + path);
    }

    /// <summary>
    /// Règle pure du dossier final, celle que FILPRO pose et rien d'autre : propriétaire SYSTEM ou Administrateurs,
    /// héritage coupé, aucune entrée héritée ni refus explicite ; hors SYSTEM et Administrateurs, un autre compte ne peut
    /// que lire, et le compte de la session tient Modification et Synchronize sur le dossier et tout son contenu, sans
    /// droit sur les autorisations ni le propriétaire. Ses droits peuvent être répartis sur plusieurs entrées ; les bits
    /// génériques sont jugés selon leur sens pour un fichier.
    /// </summary>
    internal static string? SessionFolderProblem(string? owner, string sessionSid, bool inheritanceProtected,
        IEnumerable<UserDataLocation.AccessEntry> rules)
    {
        if (owner is not (UserDataLocation.SystemSid or UserDataLocation.AdministratorsSid)) return "Propriétaire non approuvé";
        if (!inheritanceProtected) return "Droits hérités non coupés";
        var sessionRights = (FileSystemRights)0;
        foreach (var rule in rules)
        {
            if (rule.Inherited) return "Droit hérité présent (" + rule.Sid + ")";
            if (!rule.Allow) return "Refus explicite présent (" + rule.Sid + ")";
            if (rule.Sid is UserDataLocation.SystemSid or UserDataLocation.AdministratorsSid) continue;
            if (!string.Equals(rule.Sid, sessionSid, StringComparison.OrdinalIgnoreCase))
            {
                if (UserDataLocation.GrantsMoreThanRead(rule.Rights)) return "Écriture accordée à un compte non autorisé (" + rule.Sid + ")";
                continue;
            }
            var rights = UserDataLocation.Specific(rule.Rights);
            if ((rights & ~SessionMaximum) != 0) return "Le compte de la session pourrait changer les autorisations";
            if (rule.Inheritance != AllChildren || rule.Propagation != PropagationFlags.None)
                return "Droits du compte de la session non transmis à tout le contenu";
            sessionRights |= rights;
        }
        return (sessionRights & SessionRights) == SessionRights ? null : "Le compte de la session ne peut pas écrire";
    }

    /// <summary>Compte de chaque processus d'une session (PID → SID), lu en une seule énumération WTS.</summary>
    internal static Dictionary<uint, string> SessionProcessOwners(uint sessionId)
    {
        var level = 1u;
        if (!Native.WTSEnumerateProcessesExW(IntPtr.Zero, ref level, sessionId, out var buffer, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var owners = new Dictionary<uint, string>();
            var size = Marshal.SizeOf<Native.ProcessInfoEx>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.ProcessInfoEx>(buffer + i * size);
                if (info.UserSid != IntPtr.Zero) owners[info.ProcessId] = new SecurityIdentifier(info.UserSid).Value;
            }
            return owners;
        }
        finally { Native.WTSFreeMemoryExW(Native.ProcessInfoLevel1, buffer, count); }
    }

    private static string? SessionUser()
    {
        var user = SessionText(Native.UserName);
        if (string.IsNullOrEmpty(user)) return null;
        var domain = SessionText(Native.DomainName);
        try { return ((SecurityIdentifier)new NTAccount(domain ?? "", user).Translate(typeof(SecurityIdentifier))).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return null; }
    }

    private static string? SessionText(int kind)
    {
        if (!Native.WTSQuerySessionInformationW(IntPtr.Zero, Native.CurrentSession, kind, out var buffer, out _)) return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { Native.WTSFreeMemory(buffer); }
    }

    private static class Native
    {
        internal const int ProcessInfoLevel1 = 1;
        internal const int CurrentSession = -1;
        internal const int UserName = 5;
        internal const int DomainName = 7;

        // WTS_PROCESS_INFO_EXW : tous les compteurs sont des DWORD, 64 octets en x64 (relu sur Windows 11 x64).
        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInfoEx
        {
            public uint SessionId, ProcessId;
            public IntPtr ProcessName, UserSid;
            public uint Threads, Handles, PagefileUsage, PeakPagefileUsage, WorkingSetSize, PeakWorkingSetSize;
            public long UserTime, KernelTime;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSEnumerateProcessesExW(IntPtr server, ref uint level, uint sessionId, out IntPtr processes, out int count);

        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSFreeMemoryExW(int type, IntPtr memory, int count);

        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr memory);
    }
}
