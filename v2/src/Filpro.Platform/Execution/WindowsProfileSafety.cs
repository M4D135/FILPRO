using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Filpro.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Filpro.Platform;

public sealed record ProfileContainerEvidence(bool Fslogix, string? Error);

/// <summary>Lectures préalables fermées : conteneurs/itinérance et chemins de la ruche du compte effectivement visé.</summary>
public static class WindowsProfileSafety
{
    public static ProfileContainerEvidence ReadContainers()
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Contrôle des conteneurs indisponible hors Windows.");
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var configured = Enabled(machine, @"SOFTWARE\FSLogix\Profiles")
                || Enabled(machine, @"SOFTWARE\Policies\FSLogix\Profiles")
                || Enabled(machine, @"SOFTWARE\Policies\FSLogix\ODFC");
            return new(configured || ServiceNotStopped(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or Win32Exception or FilproOperationException)
        { return new(false, "La configuration FSLogix ou son service n'a pas pu être contrôlé ; opération native bloquée."); }
    }

    public static bool ReadCentralProfile(string sid)
    {
        SoftwareDiscoveryInputs.ValidateSid(sid);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var profile = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid, writable: false)
            ?? throw new FilproOperationException("profile.identity", "Clé de profil absente pendant le contrôle d'itinérance.");
        var value = profile.GetValue("CentralProfile", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null and not string) throw new FilproOperationException("profile.unsupported", "Type de profil itinérant inconnu.");
        return value is string text && text.Length != 0;
    }

    public static void RequireUserFolders(IReadOnlyUserRegistryKey hive, string profilePath, ExecutionFileSystem files, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var opened = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        using var key = opened.Key;
        if (opened.ErrorCode != 0 || key is not IReadOnlyUserRegistryValues values)
            throw new FilproOperationException("profile.redirected", "Les emplacements personnels du compte ne sont pas lisibles ; aucune migration automatique.");
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var complete = false;
        for (var index = 0; index <= 512; index++)
        {
            token.ThrowIfCancellationRequested();
            var name = values.ReadValueName(index);
            if (name.ErrorCode == 259) { complete = true; break; }
            if (name.ErrorCode != 0 || string.IsNullOrEmpty(name.Name) || index == 512)
                throw new FilproOperationException("profile.redirected", "Inventaire des emplacements personnels incomplet.");
            var value = values.ReadValue(name.Name, 65536);
            if (value.ErrorCode != 0 || value.Kind is not (1 or 2) || value.Bytes is null || value.Bytes.Length % 2 != 0)
                throw new FilproOperationException("profile.redirected", "Valeur d'emplacement personnelle illisible ou de type inattendu.");
            string text;
            try { text = new UnicodeEncoding(false, false, true).GetString(value.Bytes).TrimEnd('\0'); }
            catch (DecoderFallbackException) { throw new FilproOperationException("profile.redirected", "Chemin utilisateur mal encodé."); }
            if (text.Contains('\0') || !rows.TryAdd(name.Name, text))
                throw new FilproOperationException("profile.redirected", "Emplacement personnel ambigu.");
        }
        if (!complete) throw new FilproOperationException("profile.redirected", "Lecture des emplacements personnels incomplète.");
        foreach (var path in ProfileFolderPolicy.Validate(profilePath, rows))
        {
            token.ThrowIfCancellationRequested();
            var parentChecked = false;
            for (var current = path; current is not null && StoragePathPolicy.IsWithin(current, profilePath); current = Path.GetDirectoryName(current))
            {
                try { using var lease = files.HoldDirectory(current); parentChecked = true; break; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            if (!parentChecked) throw new FilproOperationException("profile.redirected", "Le profil a disparu pendant le contrôle de ses emplacements.");
        }
        token.ThrowIfCancellationRequested();
    }

    private static bool Enabled(RegistryKey machine, string path)
    {
        using var key = machine.OpenSubKey(path, writable: false);
        var value = key?.GetValue("Enabled", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return false; // Valeur FSLogix par défaut documentée : 0.
        if (value is not int number || number is not (0 or 1)) throw new FilproOperationException("profile.unsupported", "Configuration de conteneur inconnue.");
        return number == 1;
    }

    private static bool ServiceNotStopped()
    {
        using var manager = Native.OpenSCManagerW(null, null, 0x1); // SC_MANAGER_CONNECT, aucune commande de service.
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = Native.OpenServiceW(manager, "frxsvc", 0x4); // SERVICE_QUERY_STATUS uniquement.
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1060) return false; // ERROR_SERVICE_DOES_NOT_EXIST
            throw new Win32Exception(error);
        }
        if (!Native.QueryServiceStatusEx(service, 0, out var status, (uint)Marshal.SizeOf<Native.ServiceStatus>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return status.CurrentState != 1; // SERVICE_STOPPED ; transitions et état inconnu restent bloquants.
    }

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => Native.CloseServiceHandle(handle);
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct ServiceStatus
        { internal uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceStatus status, uint bytes, out uint needed);
        [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool CloseServiceHandle(IntPtr handle);
    }
}
