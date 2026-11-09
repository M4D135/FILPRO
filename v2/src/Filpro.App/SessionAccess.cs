using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Filpro.App;

/// <summary>
/// Droits qu'un compte obtiendrait sur un fichier ou un dossier, évalués par Windows (Authz) à partir de son SID, comme
/// l'onglet « Accès effectif » des propriétés de sécurité, avec les groupes d'une ouverture de session interactive. Ne sert
/// qu'à expliquer un affichage qui ne démarre pas, quand FILPRO est élevé avec un autre compte que celui de la session :
/// un résultat indéterminable (API indisponible, compte de domaine injoignable, partage réseau) n'explique rien et
/// n'empêche jamais le démarrage.
/// </summary>
internal static class SessionAccess
{
    // FILE_READ_DATA (FILE_LIST_DIRECTORY pour un dossier) et FILE_EXECUTE (FILE_TRAVERSE pour un dossier).
    internal const uint ReadData = 0x1;
    internal const uint Execute = 0x20;
    private const uint MaximumAllowed = 0x02000000;
    private const int AccessDenied = 5;
    // Groupes d'une session ouverte, absents d'un contexte construit depuis le seul SID. Tout le monde, INTERACTIVE et les
    // utilisateurs authentifiés sont toujours dans le jeton de la session ; LOCAL, ouverture console, bureau à distance et
    // cette organisation en dépendent. Ajoutés à tort, ceux-ci ne font que masquer un refus (l'avertissement de démarrage
    // lent prend alors le relais).
    private static readonly string[] InteractiveGroups = ["S-1-1-0", "S-1-2-0", "S-1-2-1", "S-1-5-4", "S-1-5-11", "S-1-5-14", "S-1-5-15"];
    // Une entrée de refus qui nomme un groupe de type d'ouverture ou d'authentification, autre que les trois toujours
    // présents, rend le résultat indéterminé : le contexte Authz peut l'avoir à tort (réseau d'une ouverture S4U) ou
    // ne pas l'avoir alors que la session l'a.
    private static readonly HashSet<string> CertainGroups = ["S-1-1-0", "S-1-5-4", "S-1-5-11"];

    /// <summary>Constat sur un démarrage encore en cours : élément illisible nommé, ou simple lenteur.</summary>
    internal readonly record struct StartNotice(string? UnreadablePath);

    /// <summary>
    /// Premier élément de l'affichage que le compte de la session ne pourrait pas lire : dossier du moteur WebView2 du pack,
    /// son exécutable, dossier de l'interface, sa page d'accueil. Nul s'il peut tout lire, si le pack est sur un partage
    /// réseau (sa sécurité dépend alors du serveur) ou si ce n'est pas déterminable.
    /// </summary>
    internal static string? UnreadableEngine(string sessionSid, string? runtimeFolder, string webRoot)
    {
        var targets = new List<(string Path, uint Required)>();
        if (runtimeFolder is not null)
        {
            targets.Add((runtimeFolder, ReadData | Execute));
            targets.Add((Path.Combine(runtimeFolder, "msedgewebview2.exe"), ReadData | Execute));
        }
        targets.Add((webRoot, ReadData | Execute));
        targets.Add((Path.Combine(webRoot, "index.html"), ReadData));
        if (targets.Any(t => IsNetworkPath(t.Path))) return null;
        var granted = Granted(sessionSid, targets.Select(t => t.Path).ToArray());
        for (var i = 0; i < targets.Count; i++)
            if (granted[i] is { } rights && (rights & targets[i].Required) != targets[i].Required) return targets[i].Path;
        return null;
    }

    /// <summary>
    /// Attend, sans jamais l'interrompre, ce que l'écran d'attente doit dire d'un démarrage : un élément illisible établi
    /// par <paramref name="check"/> et toujours d'actualité après <paramref name="grace"/>, sinon un démarrage encore en
    /// cours au bout de <paramref name="slow"/>, compté dès le départ. Nul si le démarrage aboutit ou échoue avant, ou si
    /// l'attente est annulée. Un contrôle en échec ou bloqué ne retarde rien.
    /// </summary>
    internal static async Task<StartNotice?> WatchAsync(Task start, Task<string?> check, TimeSpan grace, TimeSpan slow, CancellationToken token)
    {
        var slowDelay = Task.Delay(slow, token);
        var first = await Task.WhenAny(start, check, slowDelay).ConfigureAwait(false);
        if (first == check && check.IsCompletedSuccessfully && check.Result is { } unreadable)
        {
            var graceDelay = Task.Delay(grace, token);
            return await Task.WhenAny(start, graceDelay).ConfigureAwait(false) == graceDelay && Pending(start, token)
                ? new StartNotice(unreadable) : null;
        }
        if (first != slowDelay) first = await Task.WhenAny(start, slowDelay).ConfigureAwait(false);
        return first == slowDelay && Pending(start, token) ? new StartNotice(null) : null;
    }

    private static bool Pending(Task start, CancellationToken token) => !start.IsCompleted && !token.IsCancellationRequested;

    /// <summary>Masque des droits que Windows accorderait au compte sur cet objet (0 sans aucun droit) ; nul si indéterminable.</summary>
    internal static uint? Granted(string sid, string path) => Granted(sid, [path])[0];

    /// <summary>Même évaluation pour plusieurs objets, avec un seul contexte Authz (une seule résolution des groupes).</summary>
    internal static uint?[] Granted(string sid, IReadOnlyList<string> paths)
    {
        var results = new uint?[paths.Count];
        IntPtr manager = IntPtr.Zero, baseContext = IntPtr.Zero, context = IntPtr.Zero, groups = IntPtr.Zero;
        var groupSids = new List<IntPtr>();
        try
        {
            var identifier = new SecurityIdentifier(sid);
            if (!Native.AuthzInitializeResourceManager(Native.NoAudit, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, null, out manager)) return results;
            if (!Native.AuthzInitializeContextFromSid(0, Binary(identifier), manager, IntPtr.Zero, default, IntPtr.Zero, out baseContext)) return results;
            var entrySize = Marshal.SizeOf<Native.SidAndAttributes>();
            groups = Marshal.AllocHGlobal(entrySize * InteractiveGroups.Length);
            for (var i = 0; i < InteractiveGroups.Length; i++)
            {
                var bytes = Binary(new SecurityIdentifier(InteractiveGroups[i]));
                var pointer = Marshal.AllocHGlobal(bytes.Length);
                groupSids.Add(pointer);
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                Marshal.StructureToPtr(new Native.SidAndAttributes { Sid = pointer, Attributes = Native.GroupEnabled }, groups + i * entrySize, false);
            }
            if (!Native.AuthzAddSidsToContext(baseContext, groups, (uint)InteractiveGroups.Length, IntPtr.Zero, 0, out context)) return results;
            for (var i = 0; i < paths.Count; i++) results[i] = Check(context, paths[i]);
            return results;
        }
        catch (Exception ex) when (ex is ArgumentException or SystemException) { return new uint?[paths.Count]; }
        finally
        {
            if (context != IntPtr.Zero) Native.AuthzFreeContext(context);
            if (baseContext != IntPtr.Zero) Native.AuthzFreeContext(baseContext);
            if (manager != IntPtr.Zero) Native.AuthzFreeResourceManager(manager);
            foreach (var pointer in groupSids) Marshal.FreeHGlobal(pointer);
            if (groups != IntPtr.Zero) Marshal.FreeHGlobal(groups);
        }
    }

    private static uint? Check(IntPtr context, string path)
    {
        IntPtr descriptor = IntPtr.Zero, granted = IntPtr.Zero, error = IntPtr.Zero;
        try
        {
            if (Native.GetNamedSecurityInfoW(path, Native.FileObject, Native.OwnerInformation | Native.GroupInformation | Native.DaclInformation,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out descriptor) != 0) return null;
            if (DeniesUncertainGroup(descriptor)) return null;
            granted = Marshal.AllocHGlobal(sizeof(uint));
            error = Marshal.AllocHGlobal(sizeof(uint));
            var request = new Native.AccessRequest { DesiredAccess = MaximumAllowed };
            var reply = new Native.AccessReply { ResultListLength = 1, GrantedAccessMask = granted, Error = error };
            if (!Native.AuthzAccessCheck(0, context, ref request, IntPtr.Zero, descriptor, IntPtr.Zero, 0, ref reply, IntPtr.Zero)) return null;
            return Marshal.ReadInt32(error) switch
            {
                0 => unchecked((uint)Marshal.ReadInt32(granted)),
                AccessDenied => 0u,
                _ => null,
            };
        }
        finally
        {
            if (granted != IntPtr.Zero) Marshal.FreeHGlobal(granted);
            if (error != IntPtr.Zero) Marshal.FreeHGlobal(error);
            if (descriptor != IntPtr.Zero) Native.LocalFree(descriptor);
        }
    }

    private static bool DeniesUncertainGroup(IntPtr descriptor)
    {
        var bytes = new byte[Native.GetSecurityDescriptorLength(descriptor)];
        Marshal.Copy(descriptor, bytes, 0, bytes.Length);
        return new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl is { } dacl && dacl.Cast<GenericAce>().Any(ace =>
            ace is KnownAce { AceType: AceType.AccessDenied or AceType.AccessDeniedCallback or AceType.AccessDeniedObject or AceType.AccessDeniedCallbackObject } denied && IsLogonGroup(denied.SecurityIdentifier.Value));
    }

    // S-1-2-x (local, console) et les SID d'autorité NT liés à l'ouverture : réseau, traitement par lots, service, anonyme,
    // utilisateur Terminal Server, bureau à distance, cette organisation, compte local, paquets d'authentification.
    private static bool IsLogonGroup(string sid)
    {
        if (CertainGroups.Contains(sid)) return false;
        if (sid.StartsWith("S-1-2-", StringComparison.Ordinal) || sid.StartsWith("S-1-5-64-", StringComparison.Ordinal)
            || sid.StartsWith("S-1-5-65-", StringComparison.Ordinal)) return true;
        return sid is "S-1-5-1" or "S-1-5-2" or "S-1-5-3" or "S-1-5-6" or "S-1-5-7" or "S-1-5-13" or "S-1-5-14" or "S-1-5-15"
            or "S-1-5-1000" or "S-1-5-113" or "S-1-5-114";
    }

    private static byte[] Binary(SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try { return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return true; }
    }

    private static class Native
    {
        internal const int FileObject = 1;
        internal const uint OwnerInformation = 0x1;
        internal const uint GroupInformation = 0x2;
        internal const uint DaclInformation = 0x4;
        internal const uint NoAudit = 0x1;
        // SE_GROUP_MANDATORY | SE_GROUP_ENABLED_BY_DEFAULT | SE_GROUP_ENABLED.
        internal const uint GroupEnabled = 0x7;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Luid { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccessRequest
        {
            public uint DesiredAccess;
            public IntPtr PrincipalSelfSid, ObjectTypeList;
            public uint ObjectTypeListLength;
            public IntPtr OptionalArguments;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccessReply
        {
            public uint ResultListLength;
            public IntPtr GrantedAccessMask, SaclEvaluationResults, Error;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetNamedSecurityInfoW(string objectName, int objectType, uint securityInformation, IntPtr owner, IntPtr group,
            IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

        [DllImport("authz.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzInitializeResourceManager(uint flags, IntPtr dynamicAccessCheck, IntPtr computeDynamicGroups,
            IntPtr freeDynamicGroups, string? name, out IntPtr resourceManager);

        [DllImport("authz.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzInitializeContextFromSid(uint flags, byte[] userSid, IntPtr resourceManager, IntPtr expirationTime,
            Luid identifier, IntPtr dynamicGroupArgs, out IntPtr clientContext);

        [DllImport("authz.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzAddSidsToContext(IntPtr clientContext, IntPtr sids, uint sidCount, IntPtr restrictedSids,
            uint restrictedSidCount, out IntPtr newClientContext);

        [DllImport("authz.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzAccessCheck(uint flags, IntPtr clientContext, ref AccessRequest request, IntPtr auditEvent,
            IntPtr securityDescriptor, IntPtr optionalSecurityDescriptors, uint optionalSecurityDescriptorCount, ref AccessReply reply,
            IntPtr accessCheckResults);

        [DllImport("authz.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzFreeContext(IntPtr clientContext);

        [DllImport("authz.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AuthzFreeResourceManager(IntPtr resourceManager);

        [DllImport("advapi32.dll")]
        internal static extern uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
