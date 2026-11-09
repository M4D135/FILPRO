using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Filpro.Core;

public sealed record BackupCatalogEntry(string Id, string ProfileId, string Name, long? Bytes, bool Corrupted, string? StoredPath = null);
public sealed record BackupCatalog(bool Complete, IReadOnlyList<BackupCatalogEntry> Entries, bool? NativeIntegrityValid = null);

public static partial class ExecutionCatalogCodec
{
    public const int MaximumEntries = 20000;
    public const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public static BackupCatalog ReadUsmtOutput(string profileId, IEnumerable<string> lines, CancellationToken token)
    {
        var entries = new List<BackupCatalogEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var complete = true;
        var catalogMarkers = 0;
        var nativeValid = true;
        var byteBudget = 256;
        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();
            var match = CatalogLine().Match(line);
            if (!match.Success) { if (!string.IsNullOrWhiteSpace(line)) { complete = false; nativeValid = false; } continue; }
            var name = match.Groups[1].Value;
            if (!names.Add(name)) { complete = false; nativeValid = false; continue; }
            // /verify:all énumère aussi les fichiers internes du magasin. Le catalogue peut
            // précéder CACHE.IMG ; ce n'est pas un marqueur de fin de sortie.
            if (name.Equals("catalog.mig", StringComparison.OrdinalIgnoreCase))
            {
                catalogMarkers++;
                if (match.Groups[2].Value != "OK") { complete = false; nativeValid = false; }
                continue;
            }
            if (match.Groups[2].Value == "CORRUPTED") nativeValid = false;
            if (name.Equals("MIGSTATE.DAT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CACHE.IMG", StringComparison.OrdinalIgnoreCase))
            {
                if (match.Groups[2].Value != "OK") complete = false;
                continue;
            }
            if (!(name.Length >= 3 && char.IsAsciiLetter(name[0]) && name[1] == ':' && name[2] == '\\')
                && !name.StartsWith(@"\\", StringComparison.Ordinal))
            { complete = false; nativeValid = false; continue; }
            try { _ = NormalizeName(name); } catch (FilproOperationException) { complete = false; continue; }
            if (entries.Count == MaximumEntries) { complete = false; continue; }
            var entry = new BackupCatalogEntry(EntryId(profileId, name), profileId, name, null, match.Groups[2].Value == "CORRUPTED");
            var cost = EntryByteCost(entry);
            if (byteBudget + cost > MaximumBytes) { complete = false; continue; }
            byteBudget += cost; entries.Add(entry);
        }
        return new(complete && catalogMarkers == 1, entries, nativeValid && catalogMarkers == 1);
    }
    public static byte[] Encode(BackupCatalog catalog)
    {
        Validate(catalog);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Options);
        if (bytes.Length > MaximumBytes) throw Invalid("Catalogue trop volumineux.");
        return bytes;
    }
    public static BackupCatalog Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("Taille de catalogue invalide.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            BackupManifestCodec.RejectDuplicateProperties(document.RootElement);
            var result = JsonSerializer.Deserialize<BackupCatalog>(bytes.Span, Options) ?? throw Invalid("Catalogue vide.");
            Validate(result); return result;
        }
        catch (JsonException) { throw Invalid("Catalogue JSON invalide."); }
    }
    public static string NormalizeName(string name)
    {
        // Les noms viennent du catalogue natif, jamais d'un motif libre. Les caractères de filtre sont refusés.
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['*', '?', ';', ',', '[', ']', '%']) >= 0)
            throw Invalid("Nom non représentable par un filtre exact USMT.");
        return StoragePathPolicy.NormalizeWindows(name);
    }
    public static string EntryId(string profileId, string name) => BackupManifestCodec.Hash(Encoding.UTF8.GetBytes(profileId + "|" + name.ToUpperInvariant()));
    public static int EntryByteCost(BackupCatalogEntry entry) => JsonSerializer.SerializeToUtf8Bytes(entry, Options).Length + 1;
    private static void Validate(BackupCatalog catalog)
    {
        if (catalog.Entries is null || catalog.Entries.Count > MaximumEntries) throw Invalid("Catalogue hors limites.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            if (entry is null || !Guid.TryParseExact(entry.ProfileId, "N", out _) || !ids.Add(entry.Id) || entry.Bytes < 0)
                throw Invalid("Entrée de catalogue invalide.");
            if (entry.StoredPath is null) _ = NormalizeName(entry.Name);
            else if (entry.StoredPath != "USMT/File/" + BackupManifestCodec.RequireRelative(entry.Name)
                || entry.StoredPath != BackupManifestCodec.RequireRelative(entry.StoredPath) || entry.Bytes is null)
                throw Invalid("Chemin de fichier non compressé incohérent.");
            var expected = EntryId(entry.ProfileId, entry.Name);
            if (entry.Id != expected) throw Invalid("Identifiant du catalogue incohérent.");
        }
    }
    private static FilproOperationException Invalid(string message) => new("store.catalog", message);
    [GeneratedRegex(@"\A(.+?)\t+(OK|CORRUPTED)\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogLine();
}
