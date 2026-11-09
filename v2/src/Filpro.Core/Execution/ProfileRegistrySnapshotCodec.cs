using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filpro.Core;

public sealed record ProfileRegistryValue(string Name, int Kind, string? Text = null,
    IReadOnlyList<string>? Strings = null, string? Binary = null, ulong? Number = null);
public sealed record ProfileRegistrySnapshot(int SchemaVersion, string Sid, string ProfilePath,
    IReadOnlyList<ProfileRegistryValue> Values);

/// <summary>Sauvegarde fermée des valeurs ProfileList. Aucun fichier .reg ni commande libre provenant d'une page.</summary>
public static class ProfileRegistrySnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public static byte[] Encode(ProfileRegistrySnapshot snapshot)
    {
        Validate(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
        if (bytes.Length > BackupManifestCodec.MaximumBytes) throw Invalid();
        return bytes;
    }
    public static ProfileRegistrySnapshot Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > BackupManifestCodec.MaximumBytes) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            BackupManifestCodec.RejectDuplicateProperties(document.RootElement);
            var snapshot = JsonSerializer.Deserialize<ProfileRegistrySnapshot>(bytes.Span, Options) ?? throw Invalid();
            Validate(snapshot); return snapshot;
        }
        catch (JsonException) { throw Invalid(); }
    }
    public static void Validate(ProfileRegistrySnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1 || !snapshot.Sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) throw Invalid();
        var parts = snapshot.Sid[9..].Split('-');
        if (parts.Length != 4 || parts.Any(p => !uint.TryParse(p, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || p != number.ToString(System.Globalization.CultureInfo.InvariantCulture))) throw Invalid();
        _ = StoragePathPolicy.NormalizeWindows(snapshot.ProfilePath);
        if (snapshot.Values is null || snapshot.Values.Count is < 1 or > 512) throw Invalid();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in snapshot.Values)
        {
            if (value is null || value.Name is null || value.Name.Length > 16383 || value.Name.Any(char.IsControl) || !names.Add(value.Name)) throw Invalid();
            var fields = (value.Text is null ? 0 : 1) + (value.Strings is null ? 0 : 1) + (value.Binary is null ? 0 : 1) + (value.Number.HasValue ? 1 : 0);
            if (fields != 1) throw Invalid();
            switch (value.Kind)
            {
                case 1 or 2 when value.Text is not null && value.Text.Length <= 262144 && !value.Text.Contains('\0'): break;
                case 7 when value.Strings is not null && value.Strings.Count <= 4096
                    && value.Strings.All(s => s is not null && s.Length <= 262144 && !s.Contains('\0')): break;
                case -1 or 3 when value.Binary is not null:
                    try { if (Convert.FromBase64String(value.Binary).Length > 262144) throw Invalid(); }
                    catch (FormatException) { throw Invalid(); } break;
                case 4 when value.Number is <= uint.MaxValue: break;
                case 11 when value.Number.HasValue: break;
                default: throw Invalid();
            }
        }
        var path = snapshot.Values.SingleOrDefault(v => v.Name.Equals("ProfileImagePath", StringComparison.OrdinalIgnoreCase));
        if (path is null || path.Kind is not (1 or 2) || string.IsNullOrWhiteSpace(path.Text)) throw Invalid();
    }
    private static FilproOperationException Invalid() => new("repair.registry", "Sauvegarde ProfileList invalide ; aucune écriture autorisée.");
}
