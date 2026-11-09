using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Filpro.App;

/// <summary>
/// Dossier de données de l'identité qui exécute FILPRO. %LOCALAPPDATA%\FILPRO reste le choix normal ; il est d'abord
/// éprouvé par une écriture réelle. Quand un administrateur saisi dans l'UAC n'a pas de dossier local utilisable,
/// le processus élevé emploie, comme FILPRO 1, un dossier de ProgramData réservé à SYSTEM et aux Administrateurs.
/// Un dossier de secours préexistant n'est jamais repris s'il n'a pas déjà exactement cette protection.
/// </summary>
internal static class UserDataLocation
{
    internal const string FallbackFolder = "FILPRO-V2";
    internal const string SystemSid = "S-1-5-18";
    internal const string AdministratorsSid = "S-1-5-32-544";
    // Seuls droits qu'un autre compte peut garder : lire et parcourir. Tout autre bit (écriture, suppression, autorisations,
    // propriétaire, ACCESS_SYSTEM_SECURITY, MAXIMUM_ALLOWED) est refusé, une fois les bits génériques convertis.
    private const FileSystemRights ReadOnly = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;

    /// <summary>Une entrée de la liste d'accès d'un dossier, telle qu'elle est écrite sur le disque.</summary>
    internal readonly record struct AccessEntry(string Sid, bool Allow, FileSystemRights Rights, InheritanceFlags Inheritance,
        PropagationFlags Propagation, bool Inherited);

    private static readonly List<string> notes = [];
    // Handles gardés jusqu'à la fin du processus, sans partage de suppression : chaque niveau vérifié ne peut plus être
    // renommé ni remplacé par un lien pendant que FILPRO l'utilise.
    private static readonly List<SafeFileHandle> pinned = [];

    /// <summary>Constats de la résolution, journalisés après coup : le journal technique dépend lui-même de ce dossier.</summary>
    internal static IReadOnlyList<string> Notes { get { lock (notes) return notes.ToArray(); } }

    internal static string? Resolve(bool elevated)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (Path.IsPathFullyQualified(local))
        {
            var candidate = Path.Combine(local, "FILPRO");
            if (TryProbe(candidate, refuseLinks: false, out var reason)) return candidate;
            Note("Dossier local de la session inutilisable (" + reason + ").");
        }
        else Note("Windows ne fournit pas de dossier local absolu pour cette session.");
        if (!elevated) return null;
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (!Path.IsPathFullyQualified(programData) || !Directory.Exists(programData)) { Note("ProgramData indisponible."); return null; }
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new UnauthorizedAccessException("Identité Windows sans SID.");
            var root = Path.Combine(programData, FallbackFolder);
            var users = Path.Combine(root, "Users");
            var own = Path.Combine(users, user.Value);
            foreach (var folder in new[] { root, users, own }) EnsureProtected(folder, user);
            if (!TryProbe(own, refuseLinks: true, out var reason)) { Note("Dossier de secours inutilisable (" + reason + ")."); return null; }
            Note("Dossier de secours protégé utilisé : " + own + ".");
            return own;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException)
        {
            Note("Dossier de secours refusé : " + ex.Message);
            return null;
        }
    }

    /// <summary>Crée le dossier avec sa protection, ou vérifie celle d'un dossier déjà présent ; ne la répare jamais.</summary>
    internal static void EnsureProtected(string path, SecurityIdentifier user)
    {
        RefuseReparse(path);
        if (!Directory.Exists(path))
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { SystemSid, AdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.SetOwner(new SecurityIdentifier(AdministratorsSid));
            new DirectoryInfo(path).Create(security);
        }
        var descriptor = ReadDescriptor(Pin(path), path);
        var problem = ReadEntries(descriptor, out var entries)
            ?? ProtectionProblem(descriptor.Owner?.Value, user.Value, descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected),
                entries.Select(e => (e.Sid, e.Allow, e.Rights)));
        if (problem is not null) throw new UnauthorizedAccessException(problem + " : " + path);
    }

    /// <summary>Règle pure : propriétaire de confiance, héritage coupé, aucun droit au-delà de la lecture hors SYSTEM et Administrateurs.</summary>
    internal static string? ProtectionProblem(string? owner, string currentUser, bool inheritanceProtected,
        IEnumerable<(string Sid, bool Allow, FileSystemRights Rights)> rules)
    {
        if (owner is not (SystemSid or AdministratorsSid) && owner != currentUser) return "Propriétaire non approuvé";
        if (!inheritanceProtected) return "Droits hérités non coupés";
        foreach (var rule in rules)
            if (rule.Allow && GrantsMoreThanRead(rule.Rights) && rule.Sid is not (SystemSid or AdministratorsSid))
                return "Écriture accordée à un compte non autorisé (" + rule.Sid + ")";
        return null;
    }

    /// <summary>Le masque donne-t-il plus que lire et parcourir, une fois ses bits génériques convertis ?</summary>
    internal static bool GrantsMoreThanRead(FileSystemRights rights) => (Specific(rights) & ~ReadOnly) != 0;

    /// <summary>
    /// Convertit les bits génériques en droits de fichier (FILE_GENERIC_READ, _WRITE, _EXECUTE, FILE_ALL_ACCESS). Windows les
    /// garde tels quels dans une entrée seulement héritable, que les fichiers créés ensuite recevront avec leur sens complet.
    /// </summary>
    internal static FileSystemRights Specific(FileSystemRights rights)
    {
        var mask = unchecked((uint)rights);
        var specific = mask & 0x0FFFFFFFu;
        if ((mask & 0x80000000u) != 0) specific |= 0x120089u;
        if ((mask & 0x40000000u) != 0) specific |= 0x120116u;
        if ((mask & 0x20000000u) != 0) specific |= 0x1200A0u;
        if ((mask & 0x10000000u) != 0) specific |= 0x1F01FFu;
        return unchecked((FileSystemRights)specific);
    }

    /// <summary>
    /// Entrées de la liste d'accès brute. Refuse ce que les règles de .NET ne montreraient pas : liste absente (tous les
    /// droits à tous) ou entrée d'un autre type qu'autorisation ou refus simples (objet, composée, inconnue). Une entrée
    /// conditionnelle compte comme accordée ou refusée sans condition.
    /// </summary>
    internal static string? ReadEntries(RawSecurityDescriptor descriptor, out AccessEntry[] entries)
    {
        entries = [];
        if (descriptor.DiscretionaryAcl is not { } dacl) return "Liste d’accès absente";
        var list = new List<AccessEntry>(dacl.Count);
        foreach (GenericAce ace in dacl)
        {
            if (ace is not CommonAce { AceQualifier: AceQualifier.AccessAllowed or AceQualifier.AccessDenied } common)
                return "Entrée d’autorisation non standard (" + ace.AceType + ")";
            list.Add(new AccessEntry(common.SecurityIdentifier.Value, common.AceQualifier == AceQualifier.AccessAllowed,
                unchecked((FileSystemRights)common.AccessMask), common.InheritanceFlags, common.PropagationFlags, common.IsInherited));
        }
        entries = [.. list];
        return null;
    }

    /// <summary>Propriétaire et liste d'accès lus sur le handle épinglé : jamais par un chemin qui pourrait désigner un autre objet.</summary>
    internal static RawSecurityDescriptor ReadDescriptor(SafeFileHandle handle, string path)
    {
        var error = Native.GetSecurityInfo(handle, Native.FileObject, Native.OwnerInformation | Native.DaclInformation,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
        if (error != 0) throw new IOException("Sécurité du dossier illisible : " + path, unchecked((int)(0x80070000u | error)));
        try
        {
            var bytes = new byte[Native.GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally { Native.LocalFree(descriptor); }
    }

    /// <summary>
    /// Ouvre le dossier lui-même (jamais la cible d'un lien), vérifie sur ce handle qu'il s'agit d'un vrai dossier, puis le
    /// garde ouvert jusqu'à la fin du processus et le rend pour lire sa sécurité.
    /// </summary>
    internal static SafeFileHandle Pin(string path)
    {
        var handle = Native.CreateFileW(path, Native.ReadControl | Native.FileReadAttributes, FileShare.Read | FileShare.Write,
            IntPtr.Zero, FileMode.Open, Native.BackupSemantics | Native.OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Dossier de secours inaccessible : " + path, Marshal.GetHRForLastWin32Error()); }
        if (!Native.GetFileInformationByHandle(handle, out var information))
        { handle.Dispose(); throw new IOException("Attributs du dossier de secours illisibles : " + path, Marshal.GetHRForLastWin32Error()); }
        var attributes = (FileAttributes)information.Attributes;
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
        { handle.Dispose(); throw new UnauthorizedAccessException("Lien ou point de montage refusé : " + path); }
        lock (pinned) pinned.Add(handle);
        return handle;
    }

    internal static void RefuseReparse(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException("Lien ou point de montage refusé : " + current);
        }
    }

    /// <summary>Création d'un sous-dossier, écriture, relecture puis suppression réelles, comme le fera WebView2.</summary>
    internal static bool TryProbe(string folder, bool refuseLinks, out string reason)
    {
        var probeFolder = Path.Combine(folder, ".filpro-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            if (refuseLinks) RefuseReparse(folder);
            Directory.CreateDirectory(probeFolder);
            var probe = Path.Combine(probeFolder, "essai.bin");
            byte[] expected = [0x46, 0x49, 0x4C, 0x50, 0x52, 0x4F];
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
            {
                stream.Write(expected);
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                var actual = new byte[expected.Length];
                stream.ReadExactly(actual);
                if (!actual.AsSpan().SequenceEqual(expected)) { reason = "relecture différente"; return false; }
            }
            Directory.Delete(probeFolder);
            if (Directory.Exists(probeFolder)) { reason = "dossier d’essai non supprimé"; return false; }
            reason = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            reason = ex.GetType().Name + " : " + ex.Message;
            return false;
        }
    }

    private static void Note(string text) { lock (notes) notes.Add(text); }

    private static class Native
    {
        internal const uint ReadControl = 0x00020000;
        internal const uint FileReadAttributes = 0x0080;
        internal const uint BackupSemantics = 0x02000000;
        internal const uint OpenReparsePoint = 0x00200000;
        internal const int FileObject = 1;
        internal const uint OwnerInformation = 0x1;
        internal const uint DaclInformation = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security,
            FileMode disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

        [DllImport("advapi32.dll")]
        internal static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation, IntPtr owner, IntPtr group,
            IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

        [DllImport("advapi32.dll")]
        internal static extern uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
