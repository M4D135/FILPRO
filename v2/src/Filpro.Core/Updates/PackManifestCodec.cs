using System.Text.Json;

namespace Filpro.Core;

public static class PackManifestCodec
{
    public const string ProductionFlavor = "PROD";
    public const string LightFlavor = "PROD-LIGHT";
    public static bool IsProductionFlavor(string? flavor) => flavor is ProductionFlavor or LightFlavor;
    public const string ManifestPath = "wad/pack-manifest.json";
    public const string DefaultsPath = "wad/config/deployment-defaults.json";
    public const int MaximumManifestBytes = 8 * 1024 * 1024;
    public const int MaximumFiles = 20000;
    public const long MaximumFileBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumPackBytes = 8L * 1024 * 1024 * 1024;
    public const string IntegrityNotice = "Les empreintes SHA-256 vérifient l'intégrité du pack. Elles ne constituent pas une signature ni une authentification cryptographique de l'éditeur.";

    public static PackManifest Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumManifestBytes) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            Fields(root, ["schemaVersion", "application", "version", "architecture", "flavor", "sourceCommit", "files"]);
            var flavor = root.GetProperty("flavor").GetString();
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("application").GetString() != "FILPRO"
                || root.GetProperty("architecture").GetString() != "win-x64"
                || flavor is not (ProductionFlavor or LightFlavor or "DEV")) throw Invalid();
            var version = root.GetProperty("version").GetString()!; _ = PackageVersion.Parse(version);
            var source = root.GetProperty("sourceCommit").GetString()!;
            if (source is null || source.Length != 40 || !source.All(char.IsAsciiHexDigit)) throw Invalid();
            var entries = root.GetProperty("files");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 3 or > MaximumFiles) throw Invalid();
            var files = new List<PackFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                Fields(entry, ["path", "size", "sha256"]);
                var path = ValidateRelativePath(entry.GetProperty("path").GetString()!);
                if (path.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase) || path.Equals(DefaultsPath, StringComparison.OrdinalIgnoreCase)
                    || !names.Add(path) || (flavor == LightFlavor && ForbiddenInLight(path))) throw Invalid();
                var size = entry.GetProperty("size").GetInt64();
                var hash = entry.GetProperty("sha256").GetString()!;
                if (size < 0 || size > MaximumFileBytes || !IsSha256(hash) || total > MaximumPackBytes - size) throw Invalid();
                total += size; files.Add(new(path, size, hash));
            }
            foreach (var name in names)
            {
                var segments = name.Split('/'); var parent = "";
                foreach (var segment in segments.SkipLast(1))
                { parent = parent.Length == 0 ? segment : parent + "/" + segment; if (names.Contains(parent)) throw Invalid(); }
            }
            foreach (var required in new[] { "FILPRO.exe", "wad/web/index.html",
                "wad/USMT/scanstate.exe", "wad/USMT/loadstate.exe", "wad/USMT/usmtutils.exe", "wad/USMT/MigApp.xml" })
                if (!files.Any(file => file.Path == required && file.Size > 0)) throw Invalid();
            if (flavor != LightFlavor && !files.Any(file => file.Path == "wad/runtime/webview2/msedgewebview2.exe" && file.Size > 0)) throw Invalid();
            return new(1, "FILPRO", version, "win-x64", flavor, source.ToLowerInvariant(), files.AsReadOnly());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Invalid(); }
    }

    private static bool ForbiddenInLight(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower is "wad/runtime" or "wad/dev" || lower.StartsWith("wad/runtime/", StringComparison.Ordinal)
            || lower.StartsWith("wad/dev/", StringComparison.Ordinal)) return true;
        var name = lower[(lower.LastIndexOf('/') + 1)..];
        return name is "msedgewebview2.exe" or "msedge.exe" or "msedge.dll" or "msedge_elf.dll" or "webview2loader.dll"
            or "icudtl.dat" or "resources.pak" or "v8_context_snapshot.bin"
            || ((name.StartsWith("microsoftedgewebview2", StringComparison.Ordinal)
                || name.StartsWith("microsoft.webview2.fixedversionruntime", StringComparison.Ordinal))
                && Path.GetExtension(name) is ".exe" or ".cab" or ".msi" or ".zip");
    }

    public static string ValidateRelativePath(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 220 || value.Contains('\\') || value.StartsWith('/')
            || value.EndsWith('/') || (value != "FILPRO.exe" && !value.StartsWith("wad/", StringComparison.Ordinal))) throw Invalid();
        foreach (var part in value.Split('/')) StoragePathPolicy.RequireSegment(part);
        return value;
    }
    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static void Fields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != fields.Length) throw Invalid();
    }
    private static FilproOperationException Invalid() => new("pack.manifest", "Manifeste de pack refusé : identité, chemins, inventaire ou empreintes non conformes.");
}
