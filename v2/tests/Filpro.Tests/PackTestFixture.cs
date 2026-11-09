using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Octets fictifs : aucun EXE créé par ces tests n'est lancé.</summary>
internal sealed class PackTestFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "filpro-pack-test-" + Guid.NewGuid().ToString("N"));
    internal string Data => Path.Combine(Root, "appdata");
    internal PackTestFixture() => Directory.CreateDirectory(Root);
    internal string CreatePack(string name, string version = "2.0.0", string marker = "FAKE-NEVER-EXECUTE", SharedConfiguration? defaults = null,
        string flavor = PackManifestCodec.ProductionFlavor)
    {
        var root = Path.Combine(Root, name); Directory.CreateDirectory(root);
        Write(root, "FILPRO.exe", marker);
        Write(root, "wad/web/index.html", "<p>Fictif</p>");
        if (flavor != PackManifestCodec.LightFlavor) Write(root, "wad/runtime/webview2/msedgewebview2.exe", "FAKE-WEBVIEW-NEVER-EXECUTE");
        Write(root, "wad/USMT/scanstate.exe", "FAKE-SCANSTATE-NEVER-EXECUTE");
        Write(root, "wad/USMT/loadstate.exe", "FAKE-LOADSTATE-NEVER-EXECUTE");
        Write(root, "wad/USMT/usmtutils.exe", "FAKE-USMTUTILS-NEVER-EXECUTE");
        Write(root, "wad/USMT/MigApp.xml", "<migration />");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => new PackFile(
            Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, Hash(File.ReadAllBytes(path)))).ToArray();
        var manifest = new PackManifest(1, "FILPRO", version, "win-x64", flavor, new string('a', 40), files);
        File.WriteAllBytes(Path.Combine(root, PackManifestCodec.ManifestPath), JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        if (defaults is not null) SetDefaults(root, defaults);
        return root;
    }
    internal static void SetDefaults(string pack, SharedConfiguration settings)
    { var path = Path.Combine(pack, PackManifestCodec.DefaultsPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, ConfigurationCodec.Encode(settings)); }
    internal static void Write(string root, string relative, string text)
    { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    internal string Zip(string pack, string name)
    { var output = Path.Combine(Root, name); ZipFile.CreateFromDirectory(pack, output); return output; }
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}
