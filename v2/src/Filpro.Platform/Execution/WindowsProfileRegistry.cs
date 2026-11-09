using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Mutations limitées à une entrée ProfileList préalablement capturée ; aucun chemin de registre fourni par le navigateur.</summary>
public sealed class WindowsProfileRegistry
{
    private const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    public ProfileRegistrySnapshot Capture(string sid, string expectedPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        RequireWindowsSid(sid);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var parent = machine.OpenSubKey(ProfileList, writable: false) ?? throw Invalid("ProfileList est inaccessible.");
        using var backupVariant = parent.OpenSubKey(sid + ".bak", writable: false);
        if (backupVariant is not null) throw Invalid("Une entrée ProfileList .bak existe ; ce conflit doit être examiné avant la réparation.");
        var code = Native.RegOpenKeyExW(parent.Handle, sid, 0x8, 0x20019 | 0x100, out var handle);
        using var keyHandle = handle;
        if (code != 0 || handle.IsInvalid) throw Invalid("Entrée ProfileList inaccessible (Win32 " + code + ").");
        using var key = RegistryKey.FromHandle(handle, RegistryView.Registry64);
        if (key.GetSubKeyNames().Length > 0 || key.GetValueNames().Contains("SymbolicLinkValue", StringComparer.OrdinalIgnoreCase))
            throw Invalid("Structure ProfileList non prise en charge ; aucune suppression de sous-clés inconnues.");
        var rawPath = key.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (rawPath is null || !StoragePathPolicy.NormalizeWindows(Environment.ExpandEnvironmentVariables(rawPath))
                .Equals(StoragePathPolicy.NormalizeWindows(expectedPath), StringComparison.OrdinalIgnoreCase))
            throw Invalid("ProfileImagePath ne correspond plus au profil confirmé.");
        var values = new List<ProfileRegistryValue>();
        foreach (var name in key.GetValueNames())
        {
            if (values.Count >= 512) throw Invalid("Trop de valeurs ProfileList.");
            var kind = key.GetValueKind(name);
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            values.Add(kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString when value is string text => new(name, (int)kind, Text: text),
                RegistryValueKind.MultiString when value is string[] strings => new(name, (int)kind, Strings: strings),
                RegistryValueKind.Binary or RegistryValueKind.None when value is byte[] binary => new(name, (int)kind, Binary: Convert.ToBase64String(binary)),
                RegistryValueKind.DWord when value is int number => new(name, (int)kind, Number: unchecked((uint)number)),
                RegistryValueKind.QWord when value is long number => new(name, (int)kind, Number: unchecked((ulong)number)),
                _ => throw Invalid("Type de valeur ProfileList non pris en charge."),
            });
        }
        var snapshot = new ProfileRegistrySnapshot(1, sid, StoragePathPolicy.NormalizeWindows(expectedPath), values.OrderBy(v => v.Name, StringComparer.Ordinal).ToArray());
        _ = ProfileRegistrySnapshotCodec.Encode(snapshot); return snapshot;
    }
    public bool Exists(string sid)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        RequireWindowsSid(sid);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(ProfileList + "\\" + sid, writable: false);
        return key is not null;
    }
    public void RemoveExact(ProfileRegistrySnapshot expected)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var current = Capture(expected.Sid, expected.ProfilePath);
        if (!ProfileRegistrySnapshotCodec.Encode(current).AsSpan().SequenceEqual(ProfileRegistrySnapshotCodec.Encode(expected)))
            throw Invalid("L'entrée ProfileList a changé ; aucune suppression.");
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var parent = machine.OpenSubKey(ProfileList, writable: true) ?? throw Invalid("ProfileList n'est pas accessible en écriture.");
        parent.DeleteSubKey(expected.Sid, throwOnMissingSubKey: true); parent.Flush(); // Pas de DeleteSubKeyTree.
        if (Exists(expected.Sid)) throw Invalid("La suppression de l'entrée ProfileList n'est pas confirmée.");
    }
    public void RestoreAbsent(ProfileRegistrySnapshot snapshot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        RequireWindowsSid(snapshot.Sid); ProfileRegistrySnapshotCodec.Validate(snapshot);
        if (Exists(snapshot.Sid)) throw Invalid("Une entrée ProfileList existe déjà ; aucun remplacement implicite.");
        var image = snapshot.Values.Single(v => v.Name.Equals("ProfileImagePath", StringComparison.OrdinalIgnoreCase));
        if (!StoragePathPolicy.NormalizeWindows(Environment.ExpandEnvironmentVariables(image.Text!)).Equals(snapshot.ProfilePath, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Image du profil non conforme au reçu.");
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var parent = machine.OpenSubKey(ProfileList, writable: true) ?? throw Invalid("ProfileList n'est pas accessible en écriture.");
        using var backupVariant = parent.OpenSubKey(snapshot.Sid + ".bak", writable: false);
        if (backupVariant is not null) throw Invalid("Une entrée .bak empêche le retour arrière automatique.");
        var temporary = "FILPRO_PENDING_" + Guid.NewGuid().ToString("N");
        var created = Native.RegCreateKeyExW(parent.Handle, temporary, 0, null, 0, 0xF003F | 0x100,
            IntPtr.Zero, out var handle, out var disposition);
        using var keyHandle = handle;
        if (created != 0 || handle.IsInvalid || disposition != 1) throw Invalid("Création exclusive de la clé préparatoire refusée.");
        var published = false;
        try
        {
            using (var key = RegistryKey.FromHandle(handle, RegistryView.Registry64))
            {
                foreach (var value in snapshot.Values)
                {
                    object data = value.Kind switch
                    {
                        1 or 2 => value.Text!, 7 => value.Strings!.ToArray(), -1 or 3 => Convert.FromBase64String(value.Binary!),
                        4 => unchecked((int)value.Number!.Value), 11 => unchecked((long)value.Number!.Value),
                        _ => throw Invalid("Type refusé."),
                    };
                    key.SetValue(value.Name, data, (RegistryValueKind)value.Kind);
                }
                key.Flush();
            }
            // RegRenameKey refuse un nom déjà existant : aucune fusion avec une entrée créée concurremment.
            var renamed = Native.RegRenameKey(parent.Handle, temporary, snapshot.Sid);
            if (renamed != 0) throw Invalid("Publication de l'entrée ProfileList refusée (Win32 " + renamed + ").");
            published = true; parent.Flush();
        }
        finally { if (!published) parent.DeleteSubKey(temporary, throwOnMissingSubKey: false); }
        var restored = Capture(snapshot.Sid, snapshot.ProfilePath);
        if (!ProfileRegistrySnapshotCodec.Encode(restored).AsSpan().SequenceEqual(ProfileRegistrySnapshotCodec.Encode(snapshot)))
            throw Invalid("L'entrée restaurée n'a pas pu être vérifiée.");
    }
    private static void RequireWindowsSid(string sid)
    {
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("platform.windows", "ProfileList nécessite Windows.");
        SoftwareDiscoveryInputs.ValidateSid(sid);
        if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) throw Invalid("Compte non pris en charge pour une réparation.");
    }
    private static FilproOperationException Invalid(string message) => new("repair.registry", message);
    private static class Native
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegOpenKeyExW(SafeRegistryHandle parent, string name, int options, int access, out SafeRegistryHandle result);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegCreateKeyExW(SafeRegistryHandle parent, string name, int reserved, string? keyClass,
            int options, int access, IntPtr security, out SafeRegistryHandle result, out int disposition);
        [DllImport("advapi32.dll", EntryPoint = "RegRenameKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RegRenameKey(SafeRegistryHandle parent, string oldName, string newName);
    }
}
