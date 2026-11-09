using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class WebViewSocleTests
{
    private const string Light = PackManifestCodec.LightFlavor;
    // Empreinte publiée par la release webview2-156.0.4314.42 de M4D135/socle-windows (SHA256SUMS.txt).
    private const string PublishedIndexSha256 = "a846d3785426fabe872f7e53188e3ba998d4a0ebde34f6af66ac4fe04acf77d3";

    [Test] public static void IndexCompileEstCeluiPublieParLeSocle()
    {
        Check.Equal(PublishedIndexSha256, PackTestFixture.Hash(WebViewSocle.ReadEmbedded("webview2-156.0.4314.42.files.json")), "octets de l’index publié");
        var index = WebViewSocle.Known.Single();
        Check.Equal(WebViewSocle.CurrentVersion, index.Version, "version courante du Socle");
        Check.Equal(257, index.Files.Count, "inventaire complet");
        Check.Equal(706_167_729L, index.Files.Sum(file => file.Size), "taille décompressée");
        Check.True(index.Files.All(file => file.Path.StartsWith(WebViewSocle.Root + "/", StringComparison.Ordinal)), "tout sous wad/runtime/webview2");
        Check.True(index.Files.Any(file => file.Path == WebViewSocle.Root + "/msedgewebview2.exe"), "navigateur présent");
        Check.Equal("https://github.com/M4D135/socle-windows/releases/tag/webview2-156.0.4314.42", WebViewSocle.ReleaseUrl, "lien publié vers la release du Socle");
    }

    [Test] public static void IndexHostileRefuse()
    {
        var valid = IndexJson("1.2.3.4", ("runtime/webview2/msedgewebview2.exe", "moteur"));
        Check.Equal("1.2.3.4", WebViewSocle.Decode(Encoding.UTF8.GetBytes(valid)).Version, "index minimal accepté");
        // Chaque cas garde le navigateur valide : seul le garde visé peut refuser l'index.
        var browser = ("runtime/webview2/msedgewebview2.exe", "moteur");
        Check.Equal(2, WebViewSocle.Decode(Encoding.UTF8.GetBytes(IndexJson("1.2.3.4", browser, ("runtime/webview2/Locales/fr.pak", "x")))).Files.Count, "témoin à deux fichiers accepté");
        var hostile = new[]
        {
            valid.Replace("webview2-fixed", "webview2-evergreen", StringComparison.Ordinal),
            valid.Replace("\"root\":\"runtime/webview2\"", "\"root\":\"runtime\"", StringComparison.Ordinal),
            valid.Replace("1.2.3.4", "1.2.3", StringComparison.Ordinal),
            valid.Replace("\"file_count\":1", "\"file_count\":2", StringComparison.Ordinal),
            valid.Replace("\"files\"", "\"extra\":1,\"files\"", StringComparison.Ordinal),
            IndexJson("1.2.3.4", browser, ("runtime/webview2/../../FILPRO.exe", "x")),
            IndexJson("1.2.3.4", browser, ("runtime/other/msedgewebview2.exe", "x")),
            IndexJson("1.2.3.4", browser, ("runtime/webview2/a:b", "x")),
            IndexJson("1.2.3.4", ("runtime/webview2/lib.dll", "x")),
            IndexJson("1.2.3.4", browser, ("runtime/webview2/MSEDGEWEBVIEW2.EXE", "y")),
            IndexJson("1.2.3.4", browser, ("runtime/webview2/lib.dll", "x")).Replace("\"sha256\":\"", "\"sha256\":\"A", StringComparison.Ordinal),
            "[]", "{",
        };
        foreach (var json in hostile)
        {
            var refused = Check.Throws<FilproOperationException>(() => WebViewSocle.Decode(Encoding.UTF8.GetBytes(json)), "index hostile : " + json[..Math.Min(json.Length, 60)]);
            Check.Equal("pack.socle", refused.Code, "refus propre à l’index du Socle");
        }
    }

    [Test] public static async Task SocleExactAccepteACoteDUnPackLight()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("light", flavor: Light);
        using var socle = WebViewSocle.UseForTests(Socle(pack, "9.0.0.1"));
        var values = new List<PackValidationProgress>();
        var result = await PackIntegrityVerifier.ValidateAsync(pack, Light, new ProgressCapture(values));
        Check.Equal("9.0.0.1", result.WebViewSocleVersion, "Socle vérifié et annoncé");
        Check.Equal(6, result.VerifiedFiles, "preuve du manifeste seule, comparée au démarrage");
        Check.Equal(6 + 3, values[^1].CompletedFiles, "binaires du pack et du Socle vérifiés");
        Check.Equal(9, values[^1].TotalFiles, "progression sur le pack et le Socle");
        using (var configuration = new LocalConfigurationService(pack, fixture.Data))
            await configuration.UseStartupValidationAsync(await PackIntegrityVerifier.ValidateForStartupAsync(pack, Light), default);
        Check.Equal("9.0.0.1", (await PackIntegrityVerifier.ValidateForStartupAsync(pack, Light)).WebViewSocleVersion, "accepté au démarrage");
        var bare = fixture.CreatePack("bare", flavor: Light);
        Check.Null((await PackIntegrityVerifier.ValidateAsync(bare, Light)).WebViewSocleVersion, "sans Socle : Evergreen seul");
    }

    [Test] public static async Task SocleAltereIncompletOuEnrichiRefuseEnNommantLeFichier()
    {
        using var fixture = new PackTestFixture();
        var cases = new (string Label, Action<string> Damage, string Named)[]
        {
            ("fichier modifié", pack => File.AppendAllText(Path.Combine(pack, "wad/runtime/webview2/msedge.dll"), "x"), "wad/runtime/webview2/msedge.dll"),
            ("fichier manquant", pack => File.Delete(Path.Combine(pack, "wad/runtime/webview2/Locales/fr.pak")), "wad/runtime/webview2/Locales/fr.pak"),
            ("fichier en trop", pack => File.WriteAllText(Path.Combine(pack, "wad/runtime/webview2/desktop.ini"), "x"), "wad/runtime/webview2/desktop.ini"),
            ("dossier en trop", pack => Directory.CreateDirectory(Path.Combine(pack, "wad/runtime/webview2/Nouveau dossier")), "wad/runtime/webview2/Nouveau dossier"),
            ("autre dossier runtime", pack => Directory.CreateDirectory(Path.Combine(pack, "wad/runtime/webview3")), "wad/runtime/webview3"),
            ("dossier vidé", pack => Directory.Delete(Path.Combine(pack, "wad/runtime/webview2"), recursive: true), "wad/runtime/webview2/"),
        };
        for (var i = 0; i < cases.Length; i++)
        {
            var (label, damage, named) = cases[i];
            var pack = fixture.CreatePack("light-" + i, flavor: Light);
            using var socle = WebViewSocle.UseForTests(Socle(pack, "9.0.0.1"));
            damage(pack);
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateForStartupAsync(pack, Light), label);
            Check.Equal("pack.socle", refused.Code, label + " : refus propre au Socle");
            Check.Contains(named, refused.Message, label + " : élément nommé");
            Check.Contains(WebViewSocle.ReleaseUrl, refused.Message, label + " : lien vers le Socle");
        }
    }

    [Test] public static async Task DeuxVersionsMeleesRefuseesEtChaqueVersionConnueAcceptee()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("light", flavor: Light);
        var older = Socle(pack, "9.0.0.1");
        var newer = Socle(Path.Combine(fixture.Root, "newer"), "9.0.0.2", "msedgewebview2.exe", "msedge.dll", "Locales/de.pak");
        using (WebViewSocle.UseForTests(older, newer))
            Check.Equal("9.0.0.1", (await PackIntegrityVerifier.ValidateAsync(pack, Light)).WebViewSocleVersion, "ancienne version encore reconnue");
        File.Copy(Path.Combine(fixture.Root, "newer", "wad/runtime/webview2/Locales/de.pak"), Path.Combine(pack, "wad/runtime/webview2/Locales/de.pak"));
        using (WebViewSocle.UseForTests(older, newer))
        {
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, Light), "deux versions mêlées");
            Check.Equal("pack.socle", refused.Code, "refus propre au Socle");
            Check.Contains("autre version wad/runtime/webview2/Locales/de.pak", refused.Message, "fichier de l’autre version nommé");
        }
    }

    [Test] public static async Task UnSocleModifieAvantSaCopieEstRefuse()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("light", flavor: Light);
        using var socle = WebViewSocle.UseForTests(Socle(pack, "9.0.0.1"));
        Check.Equal("9.0.0.1", (await PackIntegrityVerifier.ValidateAsync(pack, Light)).WebViewSocleVersion, "Socle vérifié avant la copie");
        File.WriteAllText(Path.Combine(pack, "wad/runtime/webview2/msedge.dll"), "FAKE-SOCLE-NEVER-EXECUTE msedge.dll 9.0.0.X");
        var stage = Path.Combine(fixture.Root, "stage"); Directory.CreateDirectory(stage);
        var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.CopyWebViewSocleAsync(pack, stage, "9.0.0.1", null, default), "copie contrôlée octet par octet");
        Check.Equal("pack.socle", refused.Code, "refus propre au Socle"); Check.Contains("wad/runtime/webview2/msedge.dll", refused.Message, "fichier nommé");
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.CopyWebViewSocleAsync(pack, Path.Combine(fixture.Root, "other"), "1.0.0.0", null, default), "version inconnue");
    }

    [Test] public static async Task DeuxVersionsAuxMemesCheminsSontDepartageesParLeContenu()
    {
        using var fixture = new PackTestFixture();
        var older = fixture.CreatePack("older", flavor: Light); var newer = fixture.CreatePack("newer", flavor: Light);
        var first = Socle(older, "9.0.0.1"); var second = Socle(newer, "9.0.0.2");
        foreach (var order in new[] { new[] { first, second }, new[] { second, first } })
        {
            using var known = WebViewSocle.UseForTests(order);
            Check.Equal("9.0.0.1", (await PackIntegrityVerifier.ValidateAsync(older, Light)).WebViewSocleVersion, "ancienne version reconnue quel que soit l’ordre");
            Check.Equal("9.0.0.2", (await PackIntegrityVerifier.ValidateAsync(newer, Light)).WebViewSocleVersion, "nouvelle version reconnue quel que soit l’ordre");
        }
        File.Copy(Path.Combine(newer, "wad/runtime/webview2/msedge.dll"), Path.Combine(older, "wad/runtime/webview2/msedge.dll"), overwrite: true);
        using (WebViewSocle.UseForTests(first, second))
        {
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(older, Light), "fichiers de deux versions mêlés");
            Check.Equal("pack.socle", refused.Code, "refus propre au Socle");
        }
    }

    [Test] public static async Task LesPacksAvecMoteurEmbarqueNeLisentPasLeSocle()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("prod");
        using var socle = WebViewSocle.UseForTests(Socle(Path.Combine(fixture.Root, "elsewhere"), "9.0.0.1"));
        Check.Null((await PackIntegrityVerifier.ValidateAsync(pack, PackManifestCodec.ProductionFlavor)).WebViewSocleVersion, "le moteur PROD vient de son manifeste");
        File.WriteAllText(Path.Combine(pack, "wad/runtime/webview2/msedge.dll"), "socle");
        var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, PackManifestCodec.ProductionFlavor), "fichier hors manifeste PROD");
        Check.Equal("pack.extra", refused.Code, "règle d’inventaire inchangée hors LIGHT");
    }

    /// <summary>Octets fictifs : le faux moteur n'est jamais lancé.</summary>
    internal static WebViewSocleIndex Socle(string pack, string version, params string[] names)
    {
        var contents = (names.Length != 0 ? names : ["msedgewebview2.exe", "msedge.dll", "Locales/fr.pak"])
            .ToDictionary(name => WebViewSocle.Root + "/" + name, name => "FAKE-SOCLE-NEVER-EXECUTE " + name + " " + version, StringComparer.Ordinal);
        foreach (var (path, text) in contents) PackTestFixture.Write(pack, path, text);
        return new(version, contents.Select(item => new PackFile(item.Key, Encoding.UTF8.GetByteCount(item.Value), PackTestFixture.Hash(Encoding.UTF8.GetBytes(item.Value)))).ToArray());
    }

    private static string IndexJson(string version, params (string Path, string Text)[] files) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["component"] = "webview2-fixed", ["version"] = version, ["root"] = "runtime/webview2", ["file_count"] = files.Length,
        ["files"] = files.Select(file => new Dictionary<string, object> { ["path"] = file.Path, ["bytes"] = Encoding.UTF8.GetByteCount(file.Text), ["sha256"] = PackTestFixture.Hash(Encoding.UTF8.GetBytes(file.Text)) }).ToArray(),
    });

    private sealed class ProgressCapture(List<PackValidationProgress> values) : IProgress<PackValidationProgress> { public void Report(PackValidationProgress value) => values.Add(value); }
}

/// <summary>
/// Hors des suites de la CI Windows : un contrôle ignoré y invalide la suite. À lancer explicitement avec FILPRO_SOCLE_ZIP,
/// copie locale de Socle_WebView2-156.0.4314.42_Windows-x64.zip ; aucun téléchargement ici.
/// </summary>
internal static class WebViewSocleRealZipTests
{
    [Test] public static async Task SocleReelExtraitACoteDUnPackLight()
    {
        var zip = Environment.GetEnvironmentVariable("FILPRO_SOCLE_ZIP");
        if (string.IsNullOrEmpty(zip) || !File.Exists(zip)) Check.Skip("FILPRO_SOCLE_ZIP absent : Socle réel non disponible localement.");
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("light", flavor: PackManifestCodec.LightFlavor);
        ZipFile.ExtractToDirectory(zip!, Path.Combine(pack, "wad"));
        var values = new List<PackValidationProgress>();
        var result = await PackIntegrityVerifier.ValidateForStartupAsync(pack, PackManifestCodec.LightFlavor, new Capture(values));
        Check.Equal(WebViewSocle.CurrentVersion, result.WebViewSocleVersion, "Socle publié reconnu");
        Check.Equal(6 + 257, values[^1].CompletedFiles, "257 fichiers du Socle vérifiés");
        using var configuration = new LocalConfigurationService(pack, fixture.Data);
        await configuration.UseStartupValidationAsync(result, default);
    }

    private sealed class Capture(List<PackValidationProgress> values) : IProgress<PackValidationProgress> { public void Report(PackValidationProgress value) => values.Add(value); }
}
