using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class PackIntegrityTests
{
    [Test] public static async Task ConfigurationInvalideIgnoreeSeulementAuDemarrage()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack", defaults: new SharedConfiguration());
        foreach (var content in new[] { "{invalide", "", new string('x', ConfigurationCodec.MaximumBytes + 1) })
        {
            File.WriteAllText(Path.Combine(pack, PackManifestCodec.DefaultsPath), content);
            var result = await PackIntegrityVerifier.ValidateForStartupAsync(pack, "PROD");
            Check.True(result.DefaultsWarning is not null, "option ignorée annoncée");
            Check.Equal(7, result.VerifiedFiles, "tous les binaires restent vérifiés");
            await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "distribution stricte");
            using var configuration = new LocalConfigurationService(pack, fixture.Data);
            await configuration.UseStartupValidationAsync(result, default);
            var state = await configuration.ReadAsync(default);
            Check.True(state.Warnings.Count > 0, "configuration locale lisible avec avertissement");
        }
        File.AppendAllText(Path.Combine(pack, "FILPRO.exe"), "ALTERATION");
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateForStartupAsync(pack, "PROD"), "aucune tolérance sur les binaires");
    }

    [Test] public static async Task InventaireExactEtProgressionMesuree()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); var values = new List<PackValidationProgress>();
        var result = await PackIntegrityVerifier.ValidateAsync(pack, "PROD", new ProgressCapture(values));
        Check.Equal(7, result.VerifiedFiles, "sept fichiers fictifs gérés"); Check.True(result.VerifiedBytes > 0, "octets vérifiés");
        Check.Equal(7, values[^1].CompletedFiles, "progression finale réelle"); Check.Contains("ne constituent pas une signature", result.IntegrityNotice, "authenticité non revendiquée");
    }
    [Test] public static async Task FichierAltereOuManquantRefuse()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); File.AppendAllText(Path.Combine(pack, "FILPRO.exe"), "ALTERATION");
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "hash/taille"); File.Delete(Path.Combine(pack, "FILPRO.exe"));
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "absent");
    }
    [Test] public static async Task FichierEtDossierSupplementairesRefuses()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); var extra = Path.Combine(pack, "wad", "inconnu.dll"); File.WriteAllText(extra, "fictif");
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "extra fichier"); File.Delete(extra); Directory.CreateDirectory(Path.Combine(pack, "autre"));
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "extra dossier");
    }
    [Test] public static async Task PackExtraitParmiDAutresFichiersNommeLeFichierEnTrop()
    {
        // Zip extrait directement sur le Bureau : desktop.ini, raccourcis et zip d'origine côtoient FILPRO.exe.
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        foreach (var name in new[] { "desktop.ini", "Raccourci FILPRO.lnk", "FILPRO-2.0.4-win-x64-PROD.zip" })
        {
            var extra = Path.Combine(pack, name); File.WriteAllText(extra, "fictif");
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateForStartupAsync(pack, "PROD"), "fichier étranger refusé");
            Check.Equal("pack.extra", refused.Code, "refus d'inventaire, pas de manifeste");
            Check.Contains(name, refused.Message, "fichier en trop nommé");
            Check.Contains("dossier vide réservé à FILPRO", refused.Message, "consigne d'extraction");
            File.Delete(extra);
        }
        var folder = Path.Combine(pack, "Nouveau dossier"); Directory.CreateDirectory(folder);
        var directory = await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "dossier étranger refusé");
        Check.Equal("pack.extra", directory.Code, "refus d'inventaire du dossier"); Check.Contains("Nouveau dossier", directory.Message, "dossier en trop nommé");
        Directory.Delete(folder);
        Check.Equal(7, (await PackIntegrityVerifier.ValidateAsync(pack, "PROD")).VerifiedFiles, "pack accepté une fois le dossier nettoyé");
    }
    [Test] public static async Task ExceptionDefautsFermeeNePermetPasDeCodeOuSecret()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack", defaults: new SharedConfiguration()); _ = await PackIntegrityVerifier.ValidateAsync(pack, "PROD");
        File.WriteAllText(Path.Combine(pack, PackManifestCodec.DefaultsPath), "{\"schemaVersion\":1,\"application\":\"FILPRO\",\"kind\":\"shared-configuration\",\"settings\":{\"command\":\"fictif\"}}");
        await Check.ThrowsAsync<FilproOperationException>(() => PackIntegrityVerifier.ValidateAsync(pack, "PROD"), "exception fermée");
    }
    [Test] public static void TraverseesDoublonsEtRacineInterditsDansManifeste()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); var path = Path.Combine(pack, PackManifestCodec.ManifestPath);
        var manifest = PackManifestCodec.Decode(File.ReadAllBytes(path));
        foreach (var bad in new[] { "../FILPRO.exe", "wad/../file", "wad/file:ads", "wad/CON.txt", "C:/FILPRO.exe", "autre/fichier", "wad\\file" })
            Check.Throws<FilproOperationException>(() => PackManifestCodec.ValidateRelativePath(bad), "chemin hostile");
        var duplicate = manifest with { Files = manifest.Files.Append(manifest.Files[0] with { Path = "filpro.exe" }).ToArray() };
        Check.Throws<FilproOperationException>(() => PackManifestCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(duplicate, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })), "doublon Windows");
    }
    [Test] public static async Task ZipValideCopieEtRevalideAvantInstallation()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("source"); var zip = fixture.Zip(pack, "package.zip"); var target = Path.Combine(fixture.Root, "staged");
        var validated = await StrictPackArchive.ExtractAsync(zip, target, PackTestFixture.Hash(File.ReadAllBytes(zip)));
        Check.Equal("2.0.0", validated.Manifest.Version, "manifest vérifié"); Check.Equal(File.ReadAllText(Path.Combine(pack, "FILPRO.exe")), File.ReadAllText(Path.Combine(target, "FILPRO.exe")), "copie réelle fictive");
    }
    [Test] public static async Task ZipTraverseEtLienRefusesAvantExtraction()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("source");
        foreach (var link in new[] { false, true })
        {
            var zip = fixture.Zip(pack, link ? "link.zip" : "traversal.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry(link ? "wad/lien" : "../sortie"); if (link) entry.ExternalAttributes = 0xa1ff << 16;
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8); writer.Write("fictif");
            }
            var output = Path.Combine(fixture.Root, link ? "link-output" : "traversal-output");
            await Check.ThrowsAsync<FilproOperationException>(() => StrictPackArchive.ExtractAsync(zip, output, PackTestFixture.Hash(File.ReadAllBytes(zip))), "zip hostile");
            Check.False(Directory.Exists(output), "aucun dossier extrait");
        }
    }
    [Test] public static async Task MauvaisChecksumEtPackDevRefuses()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("source"); var zip = fixture.Zip(pack, "package.zip");
        await Check.ThrowsAsync<FilproOperationException>(() => StrictPackArchive.ExtractAsync(zip, Path.Combine(fixture.Root, "wrong"), new string('0', 64)), "checksum");
        var path = Path.Combine(pack, PackManifestCodec.ManifestPath); File.WriteAllText(path, File.ReadAllText(path).Replace("\"PROD\"", "\"DEV\"", StringComparison.Ordinal));
        var dev = fixture.Zip(pack, "dev.zip"); await Check.ThrowsAsync<FilproOperationException>(() => StrictPackArchive.ExtractAsync(dev, Path.Combine(fixture.Root, "dev-output"), PackTestFixture.Hash(File.ReadAllBytes(dev))), "DEV");
    }
    private sealed class ProgressCapture(List<PackValidationProgress> values) : IProgress<PackValidationProgress> { public void Report(PackValidationProgress value) => values.Add(value); }
}
