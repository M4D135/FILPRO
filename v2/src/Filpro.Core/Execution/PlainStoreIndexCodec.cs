using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filpro.Core;

/// <summary>Index JSONL de fichiers USMT ordinaires. Chaque fichier reste dans le magasin natif, sans archive custom.</summary>
public static class PlainStoreIndexCodec
{
    public const int MaximumFiles = 100_000;
    public const long MaximumBytes = 128L * 1024 * 1024;
    public const int MaximumLineCharacters = 8192;
    public const string Kind = "FILPRO.USMT.FileIndex";
    private sealed record Header(string Kind, int SchemaVersion, string ProfileId);
    private sealed record Footer(bool End, int Files, long TotalBytes);
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4 };

    public static void WriteHeader(TextWriter writer, string profileId)
    {
        if (!Guid.TryParseExact(profileId, "N", out _)) throw Invalid();
        writer.WriteLine(JsonSerializer.Serialize(new Header(Kind, 1, profileId), Options));
    }
    public static void WriteEntry(TextWriter writer, ManifestFileReference entry)
    { ValidateEntry(entry); writer.WriteLine(JsonSerializer.Serialize(entry, Options)); }
    public static void WriteFooter(TextWriter writer, int files, long totalBytes)
    {
        if (files is < 1 or > MaximumFiles || totalBytes < 0) throw Invalid();
        writer.WriteLine(JsonSerializer.Serialize(new Footer(true, files, totalBytes), Options));
    }

    /// <summary>L'appelant doit consommer tout l'énumérateur : le pied vérifie aussi qu'aucune fin tronquée n'est admise.</summary>
    public static IEnumerable<ManifestFileReference> Read(TextReader reader, string profileId, CancellationToken token)
    {
        var header = Decode<Header>(ReadLine(reader, token) ?? throw Invalid());
        if (header.Kind != Kind || header.SchemaVersion != 1 || header.ProfileId != profileId
            || !Guid.TryParseExact(header.ProfileId, "N", out _)) throw Invalid();
        string? previous = null;
        var count = 0;
        long bytes = 0;
        while (true)
        {
            var line = ReadLine(reader, token) ?? throw Invalid();
            using var document = Parse(line);
            if (document.RootElement.TryGetProperty("end", out _))
            {
                var footer = Decode<Footer>(line);
                if (!footer.End || count == 0 || footer.Files != count || footer.TotalBytes != bytes
                    || ReadLine(reader, token) is not null) throw Invalid();
                token.ThrowIfCancellationRequested(); yield break;
            }
            var entry = Decode<ManifestFileReference>(line); ValidateEntry(entry);
            if (++count > MaximumFiles || (previous is not null && ComparePaths(previous, entry.Path) >= 0)) throw Invalid();
            try { bytes = checked(bytes + entry.Bytes); } catch (OverflowException) { throw Invalid(); }
            previous = entry.Path;
            yield return entry;
        }
    }

    public static int ComparePaths(string first, string second)
    {
        var a = first.Split('/'); var b = second.Split('/');
        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            var difference = StringComparer.OrdinalIgnoreCase.Compare(a[index], b[index]);
            if (difference != 0) return difference;
        }
        return a.Length.CompareTo(b.Length);
    }
    private static void ValidateEntry(ManifestFileReference entry)
    {
        BackupManifestCodec.ValidateReference(entry);
        if (entry.Path != BackupManifestCodec.RequireRelative(entry.Path) || !entry.Path.StartsWith("USMT/", StringComparison.Ordinal)) throw Invalid();
    }
    private static T Decode<T>(string line)
    {
        using var document = Parse(line);
        try { return JsonSerializer.Deserialize<T>(line, Options) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }
    private static JsonDocument Parse(string line)
    {
        try
        {
            var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
            try { BackupManifestCodec.RejectDuplicateProperties(document.RootElement); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static string? ReadLine(TextReader reader, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var line = new System.Text.StringBuilder();
        while (true)
        {
            var value = reader.Read();
            if (value < 0) return line.Length == 0 ? null : throw Invalid(); // Chaque enregistrement possède sa fin de ligne.
            if (value == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length == MaximumLineCharacters) throw Invalid();
            line.Append((char)value);
        }
    }
    private static FilproOperationException Invalid() => new("store.index", "Index de fichiers USMT invalide, tronqué, dupliqué ou hors limites.");
}
