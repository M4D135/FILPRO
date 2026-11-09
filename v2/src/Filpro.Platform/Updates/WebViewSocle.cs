using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Moteur WebView2 fixe d'une version du pack commun du Socle Windows : chemins relatifs à la racine du pack FILPRO.</summary>
public sealed record WebViewSocleIndex(string Version, IReadOnlyList<PackFile> Files);

/// <summary>
/// Le pack LIGHT ne contient jamais de moteur. Le pack WebView2 du Socle Windows, publié une seule fois pour tous les outils,
/// peut être extrait dans <c>wad</c> : il n'est accepté que s'il reproduit exactement un index compilé dans FILPRO.
/// La liste ne fait que s'allonger : une version plus récente doit relire le Socle préservé par la précédente.
/// </summary>
public static class WebViewSocle
{
    public const string Parent = "wad/runtime";
    public const string Root = Parent + "/webview2";
    public const string CurrentVersion = "156.0.4314.42";
    public const string ReleaseUrl = "https://github.com/M4D135/socle-windows/releases/tag/webview2-" + CurrentVersion;
    private const string IndexRoot = "runtime/webview2";
    private static readonly string[] EmbeddedIndexes = ["webview2-156.0.4314.42.files.json"];
    private static readonly Lazy<IReadOnlyList<WebViewSocleIndex>> Embedded = new(() => EmbeddedIndexes.Select(Load).ToArray());
    private static readonly AsyncLocal<IReadOnlyList<WebViewSocleIndex>?> Replacement = new();

    public static IReadOnlyList<WebViewSocleIndex> Known => Replacement.Value ?? Embedded.Value;

    /// <summary>Remplace les index connus pour le flux asynchrone courant seulement ; les tests évitent ainsi 700 Mo d'octets réels.</summary>
    internal static IDisposable UseForTests(params WebViewSocleIndex[] indexes)
    {
        var previous = Replacement.Value; Replacement.Value = indexes;
        return new Restore(() => Replacement.Value = previous);
    }

    internal static bool IsSoclePath(string relative) => relative == Parent || relative.StartsWith(Parent + "/", StringComparison.Ordinal);

    internal static byte[] ReadEmbedded(string name)
    {
        using var stream = typeof(WebViewSocle).Assembly.GetManifestResourceStream("Filpro.Platform.Socle." + name)
            ?? throw new InvalidOperationException("Index du Socle absent de l'application : " + name);
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }

    private static WebViewSocleIndex Load(string name) => Decode(ReadEmbedded(name));

    internal static WebViewSocleIndex Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            Fields(root, ["component", "version", "root", "file_count", "files"]);
            var version = root.GetProperty("version").GetString();
            if (root.GetProperty("component").GetString() != "webview2-fixed" || root.GetProperty("root").GetString() != IndexRoot
                || !System.Version.TryParse(version, out var parsed) || parsed.Revision < 0 || parsed.ToString() != version) throw Invalid();
            var entries = root.GetProperty("files");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 1 or > PackManifestCodec.MaximumFiles
                || root.GetProperty("file_count").GetInt32() != entries.GetArrayLength()) throw Invalid();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var files = new List<PackFile>();
            foreach (var entry in entries.EnumerateArray())
            {
                Fields(entry, ["path", "bytes", "sha256"]);
                var path = entry.GetProperty("path").GetString(); var size = entry.GetProperty("bytes").GetInt64(); var sha = entry.GetProperty("sha256").GetString();
                if (path is null || !path.StartsWith(IndexRoot + "/", StringComparison.Ordinal) || size is < 0 or > PackManifestCodec.MaximumFileBytes
                    || !PackManifestCodec.IsSha256(sha)) throw Invalid();
                var relative = PackManifestCodec.ValidateRelativePath("wad/" + path);
                if (!names.Add(relative)) throw Invalid();
                files.Add(new(relative, size, sha!));
            }
            if (!files.Any(file => file.Path == Root + "/msedgewebview2.exe" && file.Size > 0)) throw Invalid();
            return new(version!, files.AsReadOnly());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or FilproOperationException)
        { throw Invalid(); }
    }

    private static void Fields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != fields.Length) throw Invalid();
    }

    private static FilproOperationException Invalid() => new("pack.socle", "Index du moteur WebView2 du Socle refusé : identité, chemins ou empreintes non conformes.");

    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
}
