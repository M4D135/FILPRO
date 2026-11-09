using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Filpro.Core;
using Microsoft.Win32.SafeHandles;

namespace Filpro.Platform;

/// <summary>Résultat LSTATUS : seuls 2/3 prouvent l'absence. Un code non nul ne contient aucune clé utilisable.</summary>
public sealed record RegistryKeyReadResult(int ErrorCode, IReadOnlyUserRegistryKey? Key);

/// <summary>Résultat d'une énumération : 259 signifie fin de liste ; tout autre code non nul est une lecture partielle.</summary>
public sealed record RegistryNameReadResult(int ErrorCode, string? Name);
public sealed record RegistryValueReadResult(int ErrorCode, int Kind, byte[]? Bytes);

/// <summary>Handle détenu par l'appelant, ouvrable uniquement en lecture, relativement à sa racine.</summary>
public interface IReadOnlyUserRegistryKey : IDisposable
{
    RegistryKeyReadResult OpenSubKey(string relativePath);
    RegistryNameReadResult ReadSubKeyName(int index);
}

/// <summary>Valeurs bornées requises pour vérifier les dossiers du compte ; aucune méthode d'écriture.</summary>
public interface IReadOnlyUserRegistryValues : IReadOnlyUserRegistryKey
{
    RegistryNameReadResult ReadValueName(int index);
    RegistryValueReadResult ReadValue(string name, int maximumBytes);
}

/// <summary>Accès aux seules ruches déjà chargées par Windows ; aucune méthode de chargement ou d'écriture.</summary>
public interface ILoadedUserRegistry
{
    RegistryKeyReadResult OpenUserHive(string sid);
}

/// <summary>Catalogue HKCU et premier niveau Software d'une ruche HKU\SID déjà chargée. Aucun montage temporaire.</summary>
public sealed class WindowsLoadedRegistryReader
{
    private readonly ILoadedUserRegistry source;
    private readonly TimeProvider clock;
    private readonly SoftwareDiscoveryLimits limits;

    public WindowsLoadedRegistryReader(ILoadedUserRegistry? source = null, TimeProvider? clock = null,
        SoftwareDiscoveryLimits? limits = null)
    {
        this.source = source ?? new WindowsLoadedUserRegistry();
        this.clock = clock ?? TimeProvider.System;
        this.limits = limits ?? new SoftwareDiscoveryLimits();
        this.limits.Validate();
    }

    public SoftwareDiscoveryResult Read(string sid, IReadOnlyList<SoftwareItem> additional, CancellationToken cancellationToken)
    {
        SoftwareDiscoveryInputs.ValidateSid(sid);
        cancellationToken.ThrowIfCancellationRequested();
        var extra = SoftwareDiscoveryInputs.Normalize(additional, files: false);
        var items = new Dictionary<string, SoftwareItem>(StringComparer.OrdinalIgnoreCase);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var opened = source.OpenUserHive(sid);
            using var hive = opened.Key;
            cancellationToken.ThrowIfCancellationRequested();
            if (opened.ErrorCode is 2 or 3)
                return new([], false, "Ruche du profil non chargée : lecture hors ligne indisponible, aucun montage effectué.");
            if (opened.ErrorCode != 0 || hive is null)
                return new([], false, ReadError("Ruche du profil inaccessible", opened.ErrorCode));

            foreach (var item in SoftwareDiscoveryInputs.Merge(SoftwareDiscoveryCatalog.Registry, extra))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var probe = hive.OpenSubKey(item.Rel);
                using var key = probe.Key;
                cancellationToken.ThrowIfCancellationRequested();
                if (probe.ErrorCode == 0 && key is not null) items[item.Rel] = item;
                else if (probe.ErrorCode is not (2 or 3)) reasons.Add(ReadError("Une clé explicite est inaccessible", probe.ErrorCode));
            }

            var softwareProbe = hive.OpenSubKey("Software");
            using var software = softwareProbe.Key;
            if (softwareProbe.ErrorCode == 0 && software is not null)
            {
                var start = clock.GetTimestamp();
                for (var index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (index >= limits.MaximumGenericEntries || clock.GetElapsedTime(start) >= limits.Duration)
                    {
                        reasons.Add("Découverte Software limitée en nombre ou en durée ; inventaire partiel.");
                        break;
                    }
                    var entry = software.ReadSubKeyName(index);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.ErrorCode == 259) break;
                    if (entry.ErrorCode != 0 || string.IsNullOrEmpty(entry.Name))
                    {
                        reasons.Add(ReadError("Énumération Software interrompue", entry.ErrorCode));
                        break;
                    }
                    if (clock.GetElapsedTime(start) >= limits.Duration)
                    {
                        reasons.Add("Découverte Software limitée en durée ; inventaire partiel.");
                        break;
                    }
                    var relative = "Software\\" + entry.Name;
                    SoftwareItem item;
                    try
                    {
                        if (entry.Name.Contains('\\') || entry.Name.Contains('/') || entry.Name.Any(char.IsControl))
                            throw new FilproOperationException("selection.store", "Nom de clé non représentable.");
                        item = SoftwareDiscoveryInputs.Normalize([
                            new SoftwareItem(SoftwareScope.Hkcu, relative, entry.Name, SoftwareCategory.Autre, false, false, false, null, false, false),
                        ], files: false).Single();
                    }
                    catch (FilproOperationException)
                    {
                        // Nom qu'aucune règle USMT ne peut écrire ([, ], /, *, ?, ", <, >, |, :, caractère de contrôle, fin par
                        // une espace ou un point) : cette clé ne peut jamais être retenue, donc la taire ne fait rien perdre.
                        // Elle ne rend pas l'inventaire partiel, ce qui bloquerait les choix et la sauvegarde du profil.
                        continue;
                    }
                    if (SoftwareDiscoveryCatalog.IsGenericRegistryExcluded(relative)
                        || extra.Any(known => SoftwareDiscoveryInputs.Overlaps(known.Rel, relative))) continue;
                    var probe = software.OpenSubKey(entry.Name);
                    using var key = probe.Key;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (probe.ErrorCode == 0 && key is not null) items.TryAdd(relative, item);
                    else reasons.Add(ReadError("Une clé énumérée est devenue absente ou inaccessible", probe.ErrorCode));
                }
            }
            else if (softwareProbe.ErrorCode is not (2 or 3))
            {
                reasons.Add(ReadError("Racine Software inaccessible", softwareProbe.ErrorCode));
            }
        }
        catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
        {
            reasons.Add("Découverte du registre interrompue par un accès refusé ou une erreur de lecture.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new([.. items.Values], reasons.Count == 0, reasons.Count == 0 ? null : string.Join(" ", reasons));
    }

    private static string ReadError(string text, int code) => text + " (Win32 " + code.ToString(CultureInfo.InvariantCulture) + ").";
}

/// <summary>Adaptateur Win32 : LSTATUS conservé, handles SafeRegistryHandle privés, aucune référence HKCU implicite.</summary>
public sealed class WindowsLoadedUserRegistry : ILoadedUserRegistry
{
    internal static RegistryKeyReadResult OpenOwnedCopiedHive(string tag)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!tag.StartsWith("FILPRO_V2_", StringComparison.Ordinal) || !Guid.TryParseExact(tag[10..], "N", out _))
            throw new FilproOperationException("hive.ownership", "Tag privé invalide.");
        var code = NativeMethods.OpenHive(NativeMethods.HkeyUsers, tag, NativeMethods.OpenLink, NativeMethods.KeyRead, out var handle);
        return FinishOpen(code, handle);
    }

    /// <summary>Copie privée fournie uniquement par le gestionnaire de ruches ; aucun point de montage global HKU.</summary>
    internal static RegistryKeyReadResult OpenPrivateCopiedHive(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Lecture de ruche réservée à Windows.");
        path = StoragePathPolicy.NormalizeWindows(path);
        var code = NativeMethods.LoadAppKey(path, out var handle, NativeMethods.KeyRead, 0x1, 0); // REG_PROCESS_APPKEY
        return FinishOpen(code, handle);
    }

    public RegistryKeyReadResult OpenUserHive(string sid)
    {
        SoftwareDiscoveryInputs.ValidateSid(sid);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Lecture HKU réservée à Windows.");
        var result = NativeMethods.OpenHive(NativeMethods.HkeyUsers, sid,
            NativeMethods.OpenLink, NativeMethods.KeyRead, out var handle);
        return FinishOpen(result, handle);
    }

    private static RegistryKeyReadResult FinishOpen(int code, SafeRegistryHandle handle)
    {
        var transferred = false;
        try
        {
            if (code != 0 || handle.IsInvalid) return new(code == 0 ? 6 : code, null);
            // Ouvrir le lien lui-même, jamais sa destination. La donnée du lien n'est pas lue ni suivie.
            var check = NativeMethods.QueryLink(handle, "SymbolicLinkValue", IntPtr.Zero, out var kind, IntPtr.Zero, out _);
            if (check is not (0 or 2) || (check == 0 && kind == 6)) return new(check is 0 or 2 ? 50 : check, null);
            var result = new RegistryKeyReadResult(0, new ReadKey(handle));
            transferred = true;
            return result;
        }
        finally { if (!transferred) handle.Dispose(); }
    }

    private sealed class ReadKey(SafeRegistryHandle handle) : IReadOnlyUserRegistryValues
    {
        public RegistryKeyReadResult OpenSubKey(string relativePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(relativePath);
            var components = relativePath.Split('\\');
            if (relativePath.Length > SoftwareSelectionCodec.MaxRegistryRelLength
                || relativePath.Any(char.IsControl)
                || components.Any(part => part.Length == 0 || part is "." or ".." || part.Contains('/')))
                throw new ArgumentException("Chemin de clé relatif invalide.", nameof(relativePath));
            ReadKey? owned = null;
            try
            {
                foreach (var component in components)
                {
                    var code = NativeMethods.OpenSubKey(owned?.Handle ?? handle, component,
                        NativeMethods.OpenLink, NativeMethods.KeyRead, out var child);
                    var opened = FinishOpen(code, child);
                    owned?.Dispose();
                    owned = opened.Key as ReadKey;
                    if (opened.ErrorCode != 0 || owned is null) return opened;
                }
                var result = new RegistryKeyReadResult(0, owned);
                owned = null; // Le caller possède uniquement le dernier handle ; les parents intermédiaires sont fermés.
                return result;
            }
            finally { owned?.Dispose(); }
        }

        private SafeRegistryHandle Handle => handle;

        public RegistryNameReadResult ReadSubKeyName(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var name = new StringBuilder(256); // Nom de sous-clé Win32 : 255 caractères au maximum.
            var size = name.Capacity;
            var code = NativeMethods.Enumerate(handle, index, name, ref size, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new(code, code == 0 ? name.ToString(0, size) : null);
        }

        public RegistryNameReadResult ReadValueName(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var name = new StringBuilder(512);
            var length = name.Capacity;
            var code = NativeMethods.EnumerateValue(handle, index, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new(code, code == 0 ? name.ToString(0, length) : null);
        }

        public RegistryValueReadResult ReadValue(string name, int maximumBytes)
        {
            if (name.Length > 511 || name.Any(char.IsControl) || maximumBytes is < 1 or > 131072) throw new ArgumentException("Lecture de valeur hors limites.");
            var length = 0;
            var code = NativeMethods.QueryValue(handle, name, IntPtr.Zero, out var kind, null, ref length);
            if (code != 0 || length < 0 || length > maximumBytes) return new(code == 0 ? 234 : code, kind, null);
            var bytes = new byte[length];
            code = NativeMethods.QueryValue(handle, name, IntPtr.Zero, out kind, bytes, ref length);
            if (code != 0 || length < 0 || length > bytes.Length) return new(code == 0 ? 234 : code, kind, null);
            if (length != bytes.Length) Array.Resize(ref bytes, length);
            return new(0, kind, bytes);
        }

        public void Dispose() => handle.Dispose();
    }

    private static class NativeMethods
    {
        internal const int KeyRead = 0x20019;
        internal const int OpenLink = 0x0008;
        // Pseudo-handle Win32 HKEY_USERS ; 0x80000005 désigne HKEY_CURRENT_CONFIG.
        internal static readonly IntPtr HkeyUsers = new(unchecked((int)0x80000003));

        [DllImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", CharSet = CharSet.Unicode)]
        internal static extern int LoadAppKey(string path, out SafeRegistryHandle result, int desired, int options, int reserved);

        [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
        internal static extern int OpenHive(IntPtr root, string subKey, int options, int desired, out SafeRegistryHandle result);

        [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
        internal static extern int OpenSubKey(SafeRegistryHandle root, string subKey, int options, int desired, out SafeRegistryHandle result);

        [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode)]
        internal static extern int Enumerate(SafeRegistryHandle key, int index, StringBuilder name, ref int length,
            IntPtr reserved, IntPtr keyClass, IntPtr classLength, IntPtr lastWrite);

        [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
        internal static extern int QueryLink(SafeRegistryHandle key, string name, IntPtr reserved, out int type,
            IntPtr data, out int length);

        [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
        internal static extern int QueryValue(SafeRegistryHandle key, string name, IntPtr reserved, out int type,
            [Out] byte[]? data, ref int length);

        [DllImport("advapi32.dll", EntryPoint = "RegEnumValueW", CharSet = CharSet.Unicode)]
        internal static extern int EnumerateValue(SafeRegistryHandle key, int index, StringBuilder name, ref int length,
            IntPtr reserved, IntPtr type, IntPtr data, IntPtr dataLength);
    }
}
