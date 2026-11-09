using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>RegLoadKey/RegUnLoadKey confinés aux copies et tags FILPRO, privilèges rétablis après chaque appel.</summary>
public sealed class WindowsCopiedHiveMountBackend : ICopiedHiveMountBackend
{
    private static readonly IntPtr HkeyUsers = new(unchecked((int)0x80000003));
    private static readonly object PrivilegeGate = new();
    public int Load(string tag, string copiedPath)
    { RequireTag(tag); return InvokePrivileged(() => Native.RegLoadKeyW(HkeyUsers, tag, copiedPath)); }
    public int Unload(string tag)
    { RequireTag(tag); return InvokePrivileged(() => Native.RegUnLoadKeyW(HkeyUsers, tag)); }
    public RegistryKeyReadResult Open(string tag)
    { RequireTag(tag); return WindowsLoadedUserRegistry.OpenOwnedCopiedHive(tag); }

    /// <summary>
    /// Événement nommé du namespace Global, visible de toutes les sessions. Windows le détruit avec le dernier handle,
    /// donc à la fin du processus : un montage resté sans marqueur appartient à un processus disparu. Son propriétaire
    /// est Administrateurs, qu'un compte standard ne peut pas attribuer : un homonyme créé par lui ne passe pas pour vivant.
    /// </summary>
    public IDisposable Claim(string tag)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        RequireTag(tag);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new EventWaitHandleSecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var identity in new[] { new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), administrators })
            security.AddAccessRule(new EventWaitHandleAccessRule(identity, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        EventWaitHandle marker;
        bool created;
        try { marker = EventWaitHandleAcl.Create(false, EventResetMode.ManualReset, ClaimName(tag), out created, security); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        { throw new FilproOperationException("hive.ownership", "Le marqueur de la ruche privée ne peut pas être créé ; aucun chargement."); }
        if (created) return marker;
        marker.Dispose();
        throw new FilproOperationException("hive.ownership", "Le marqueur de la ruche privée existe déjà ; aucun chargement ni déchargement.");
    }

    /// <summary>
    /// Vivant seulement si le marqueur existe et appartient à Administrateurs ; tout administrateur élevé peut lire celui de FILPRO.
    /// Un homonyme illisible ou d'un autre propriétaire laisse le montage compté comme résidu. Un tag hors format n'est jamais vivant.
    /// </summary>
    internal static bool ClaimedByLiveProcess(string tag)
    {
        if (!OperatingSystem.IsWindows() || !IsTag(tag)) return false;
        try
        {
            if (!EventWaitHandleAcl.TryOpenExisting(ClaimName(tag), EventWaitHandleRights.ReadPermissions | EventWaitHandleRights.Synchronize, out var marker))
                return false;
            using (marker)
                return marker.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
                    && owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException) { return false; }
    }

    internal static string ClaimName(string tag) => @"Global\" + tag;
    public bool IsMounted(string tag)
    {
        RequireTag(tag);
        var code = Native.RegOpenKeyExW(HkeyUsers, tag, 0x8, 0x20019, out var handle);
        using (handle) return Probe(code, !handle.IsInvalid);
    }

    /// <summary>
    /// 5 : le nom existe sous HKU mais sa racine refuse KEY_READ. Un montage possédé reste alors déchargeable ;
    /// seule la réponse 2 ou 3 prouve sa disparition.
    /// </summary>
    internal static bool Probe(int code, bool opened) => code switch
    {
        0 when opened => true,
        5 => true,
        2 or 3 => false,
        _ => throw new FilproOperationException("hive.probe", "État de la ruche privée inconnu (Win32 " + code + ")."),
    };
    private static void RequireTag(string tag)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!IsTag(tag)) throw new FilproOperationException("hive.ownership", "Le tag n'appartient pas au format privé FILPRO.");
    }
    private static bool IsTag(string tag) => tag.StartsWith("FILPRO_V2_", StringComparison.Ordinal) && Guid.TryParseExact(tag[10..], "N", out _);
    private static int InvokePrivileged(Func<int> action)
    {
        lock (PrivilegeGate)
        {
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x20 | 0x8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            {
                var previous = new List<Native.TokenPrivileges>();
                int? result = null;
                Exception? workFailure = null;
                var cleanupFailed = false;
                try
                {
                    foreach (var name in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
                    {
                        if (!Native.LookupPrivilegeValueW(null, name, out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        var desired = new Native.TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                        var adjusted = Native.AdjustTokenPrivileges(token, false, ref desired, Marshal.SizeOf<Native.TokenPrivileges>(), out var old, out _);
                        var code = Marshal.GetLastWin32Error();
                        if (adjusted) previous.Add(old);
                        if (!adjusted || code != 0) throw new FilproOperationException("hive.privilege", "La lecture de cette copie exige les privilèges de sauvegarde/restauration ; relancer explicitement en administrateur.");
                    }
                    result = action();
                }
                catch (Exception ex) { workFailure = ex; }
                finally
                {
                    for (var i = previous.Count - 1; i >= 0; i--)
                    {
                        var old = previous[i];
                        if (!Native.RestoreTokenPrivileges(token, false, ref old, 0, IntPtr.Zero, IntPtr.Zero)
                            || Marshal.GetLastWin32Error() != 0) cleanupFailed = true;
                    }
                }
                if (cleanupFailed) throw new HiveNativeInvocationException("Rétablissement des privilèges non confirmé.", result);
                if (workFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(workFailure).Throw();
                return result ?? throw new InvalidOperationException("Appel natif absent.");
            }
        }
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] internal struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
        [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref TokenPrivileges desired, int size,
            out TokenPrivileges previous, out int length);
        [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RestoreTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref TokenPrivileges desired, int size,
            IntPtr previous, IntPtr length);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] internal static extern int RegLoadKeyW(IntPtr root, string key, string path);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] internal static extern int RegUnLoadKeyW(IntPtr root, string key);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegOpenKeyExW(IntPtr root, string key, int options, int access, out SafeRegistryHandle result);
    }
}
