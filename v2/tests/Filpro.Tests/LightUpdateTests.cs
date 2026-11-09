using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class LightUpdateTests
{
    private const string Light = PackManifestCodec.LightFlavor;
    private const string Full = PackManifestCodec.ProductionFlavor;

    [Test] public static async Task LightConserveWebEtUsmtEtRefuseLeRuntimeEmbarque()
    {
        using var fixture = new PackTestFixture();
        var root = fixture.CreatePack("light", flavor: Light);
        var validated = await PackIntegrityVerifier.ValidateAsync(root, Light);
        var manifest = validated.Manifest;
        var required = new[] { "FILPRO.exe", "wad/web/index.html", "wad/USMT/scanstate.exe", "wad/USMT/loadstate.exe",
            "wad/USMT/usmtutils.exe", "wad/USMT/MigApp.xml" };
        foreach (var path in required)
        {
            Check.Throws<FilproOperationException>(() => Decode(manifest with { Files = manifest.Files.Where(file => file.Path != path).ToArray() }), path);
        }
        foreach (var path in new[] { "wad/RuNtImE/anything.bin", "wad/dev/notes", "wad/web/MSedgeWebView2.exe",
            "wad/USMT/WebView2Loader.dll", "wad/packages/MicrosoftEdgeWebView2Setup.exe", "wad/packages/Microsoft.WebView2.FixedVersionRuntime.140.x64.cab" })
        {
            var extra = new PackFile(path, 4, new string('a', 64));
            Check.Throws<FilproOperationException>(() => Decode(manifest with { Files = [.. manifest.Files, extra] }), path);
        }
        foreach (var flavor in new[] { Full, "DEV" })
        {
            Check.Throws<FilproOperationException>(() => Decode(manifest with { Flavor = flavor }), "runtime fixe toujours obligatoire en " + flavor);
            var complete = fixture.CreatePack(flavor, flavor: flavor);
            Check.Equal(flavor, (await PackIntegrityVerifier.ValidateAsync(complete, flavor)).Manifest.Flavor, flavor);
        }
    }

    [Test] public static async Task PartageChoisitLEditionInstalleeEtPermetCommitPuisRetourArriereLight()
    {
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", "2.0.12", "OLD-LIGHT", flavor: Light);
        var lightZip = Archive(fixture, Light, "2.1.0", Light);
        var fullZip = Archive(fixture, Full, "2.9.0", Full);
        File.WriteAllText(Path.Combine(fixture.Root, "FILPRO-9.9.9-win-x64-DEV.zip"), "archive historique non distribuée");
        File.WriteAllLines(Path.Combine(fixture.Root, "SHA256SUMS"), new[] { SumLine(lightZip), SumLine(fullZip) });
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        var offer = await service.InspectAsync(new(UpdateSourceKind.Folder, fixture.Root), default);
        Check.Equal(Path.GetFileName(lightZip), offer.FileName, "le complet plus récent reste hors de la sélection Light");
        var prepared = await service.PrepareAsync(offer.Id, true, default);
        var tx = UpdateTransaction.Read(fixture.Data, prepared.TransactionId!, installed, Light);
        Check.Equal(Light, tx.ExpectedFlavor, "édition relue du journal durable");
        Check.Equal(2, tx.SchemaVersion, "journal Light identifié explicitement");
        Check.Equal(Path.Combine(fixture.Data, "updates-light", tx.Id), tx.DirectoryPath, "journal Light dans son historique");
        Check.False(Directory.Exists(Path.Combine(fixture.Data, "updates")), "le client PROD historique ne voit aucun journal Light");
        await PackIntegrityVerifier.CopyAsync(installed, tx.WorkerRoot, await PackIntegrityVerifier.ValidateAsync(installed, Light), default);
        await WindowsUpdateWorker.VerifyWorkerAsync(tx, default);
        var committed = await UpdateDirectoryTransaction.InstallAsync(tx, () => Task.FromResult(true), Lease, default);
        Check.Equal(UpdateStage.Committed, committed.Stage, "commit Light après santé simulée");
        Check.Equal("2.1.0", (await PackIntegrityVerifier.ValidateAsync(installed, Light)).Manifest.Version, "cible Light");
        await WindowsUpdateWorker.VerifyWorkerAsync(committed, default);
        var restored = await UpdateDirectoryTransaction.RollbackAsync(UpdateTransaction.Read(fixture.Data, tx.Id, expectedFlavor: Light), Lease, default);
        Check.Equal(UpdateStage.RolledBack, restored.Stage, "retour arrière Light");
        Check.Equal("OLD-LIGHT", File.ReadAllText(Path.Combine(installed, "FILPRO.exe")), "ancien exécutable restauré");
        Check.Equal("2.1.0", (await PackIntegrityVerifier.ValidateAsync(tx.FailedRoot, Light)).Manifest.Version, "cible conservée");
        var full = fixture.CreatePack("installed-full", flavor: Full);
        using var fullService = new LocalUpdateService(full, fixture.Data, () => true);
        Check.Equal(Path.GetFileName(fullZip), (await fullService.InspectAsync(new(UpdateSourceKind.Folder, fixture.Root), default)).FileName,
            "le pack complet sélectionne sa variante dans le même dépôt");
        var dev = fixture.CreatePack("installed-dev", flavor: "DEV");
        using var devService = new LocalUpdateService(dev, fixture.Data, () => true);
        await Check.ThrowsAsync<FilproOperationException>(() => devService.InspectAsync(new(UpdateSourceKind.Folder, fixture.Root), default),
            "un DEV historique ne peut pas demander d’offre");
    }

    [Test] public static async Task RenommerLeZipNePermetJamaisDeChangerDEdition()
    {
        foreach (var (installedFlavor, foreign) in new[] { (Light, Full), (Full, Light) })
        {
            using var fixture = new PackTestFixture();
            var installed = fixture.CreatePack("installed", flavor: installedFlavor);
            var renamed = Archive(fixture, foreign, "2.1.0", installedFlavor);
            using var service = new LocalUpdateService(installed, fixture.Data, () => true);
            var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, renamed, Hash(renamed)), default);
            var refusal = await Check.ThrowsAsync<FilproOperationException>(() => service.PrepareAsync(offer.Id, true, default), "ZIP renommé refusé");
            Check.Equal("update.archive", refusal.Code, "refus au contrôle du manifeste");
            Check.Equal(0, Directory.GetFiles(fixture.Data, "transaction.json", SearchOption.AllDirectories).Length, "aucune transaction préparée");
            Check.Equal("2.0.0", (await PackIntegrityVerifier.ValidateAsync(installed, installedFlavor)).Manifest.Version, "installation préservée");
        }
    }

    [Test] public static async Task StageWorkerEtRepriseRefusentUneAutreEditionMemeAvecSonEmpreinte()
    {
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", flavor: Light);
        var archive = Archive(fixture, Light, "2.1.0", Light);
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, archive, Hash(archive)), default);
        var prepared = await service.PrepareAsync(offer.Id, true, default);
        var tx = UpdateTransaction.Read(fixture.Data, prepared.TransactionId!, installed, Light);
        var full = fixture.CreatePack("foreign", "2.1.0", flavor: Full);
        var fullPack = await PackIntegrityVerifier.ValidateAsync(full, Full);
        var wrong = tx with { TargetManifestSha256 = fullPack.ManifestSha256 };
        Directory.Move(tx.StageRoot, Path.Combine(fixture.Root, "light-stage"));
        Directory.Move(full, tx.StageRoot);
        var refused = await UpdateDirectoryTransaction.InstallAsync(wrong, () => throw new TestFailureException("Aucun lancement attendu."), Lease, default);
        Check.Equal(UpdateStage.Failed, refused.Stage, "refus avant déplacement de l’installation");
        Check.False(Directory.Exists(tx.PreviousRoot), "ancienne installation toujours en place");
        Directory.Move(tx.StageRoot, tx.WorkerRoot);
        var workerError = await Check.ThrowsAsync<FilproOperationException>(() => WindowsUpdateWorker.VerifyWorkerAsync(wrong, default), "worker complet étranger");
        Check.Equal("pack.flavor", workerError.Code, "édition du worker relue sur disque");
        Directory.Move(tx.WorkerRoot, tx.PreviousRoot);
        var recovery = wrong with { Stage = UpdateStage.RollingBack, PreviousManifestSha256 = fullPack.ManifestSha256 };
        var rollbackError = await Check.ThrowsAsync<FilproOperationException>(() => UpdateDirectoryTransaction.RollbackAsync(recovery, Lease, default), "previous étranger");
        Check.Equal("pack.flavor", rollbackError.Code, "édition de la copie précédente relue avant restauration");
        Check.True(Directory.Exists(tx.PreviousRoot), "copie étrangère conservée pour diagnostic");
        Check.Equal(Light, (await PackIntegrityVerifier.ValidateAsync(installed, Light)).Manifest.Flavor, "installation Light préservée");
    }

    [Test] public static async Task LeSocleInstalleSuitLaMiseAJourEtLeRetourArriere()
    {
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", "2.0.12", "OLD-LIGHT", flavor: Light);
        using var socle = WebViewSocle.UseForTests(WebViewSocleTests.Socle(installed, "9.0.0.1"));
        var archive = Archive(fixture, Light, "2.1.0", Light);
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, archive, Hash(archive)), default);
        var prepared = await service.PrepareAsync(offer.Id, true, default);
        var tx = UpdateTransaction.Read(fixture.Data, prepared.TransactionId!, installed, Light);
        var stage = await PackIntegrityVerifier.ValidateAsync(tx.StageRoot, Light);
        Check.Equal("9.0.0.1", stage.WebViewSocleVersion, "Socle recopié et vérifié dans la préparation");
        Check.Equal(tx.TargetManifestSha256, stage.ManifestSha256, "manifeste de l’offre inchangé");
        await PackIntegrityVerifier.CopyAsync(installed, tx.WorkerRoot, await PackIntegrityVerifier.ValidateAsync(installed, Light), default);
        Check.False(Directory.Exists(Path.Combine(tx.WorkerRoot, "wad", "runtime")), "le worker ne copie que le pack");
        var committed = await UpdateDirectoryTransaction.InstallAsync(tx, () => Task.FromResult(true), Lease, default);
        Check.Equal(UpdateStage.Committed, committed.Stage, "commit avec Socle");
        var after = await PackIntegrityVerifier.ValidateAsync(installed, Light);
        Check.Equal(("2.1.0", "9.0.0.1"), (after.Manifest.Version, after.WebViewSocleVersion), "nouvelle version avec le Socle préservé");
        var restored = await UpdateDirectoryTransaction.RollbackAsync(UpdateTransaction.Read(fixture.Data, tx.Id, expectedFlavor: Light), Lease, default);
        Check.Equal(UpdateStage.RolledBack, restored.Stage, "retour arrière");
        var back = await PackIntegrityVerifier.ValidateAsync(installed, Light);
        Check.Equal(("2.0.12", "9.0.0.1"), (back.Manifest.Version, back.WebViewSocleVersion), "ancienne version avec sa propre copie du Socle");
    }

    [Test] public static async Task UnSocleChangeApresLaPreparationEstRefuseSansBloquerUneNouvellePreparation()
    {
        if (!OperatingSystem.IsWindows()) Check.Skip("La remise au moteur de remplacement exige Windows.");
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", "2.0.12", flavor: Light);
        using var socle = WebViewSocle.UseForTests(WebViewSocleTests.Socle(installed, "9.0.0.1"));
        var archive = Archive(fixture, Light, "2.1.0", Light);
        using var lease = WindowsUpdateWorker.AcquireInstallationLease(installed);
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, archive, Hash(archive)), default);
        var prepared = await service.PrepareAsync(offer.Id, true, default);
        Directory.Delete(Path.Combine(installed, "wad", "runtime"), recursive: true);
        var refused = await Check.ThrowsAsync<FilproOperationException>(() => service.InstallAsync(prepared.TransactionId!, true, default), "Socle retiré après préparation");
        Check.Equal("update.changed", refused.Code, "refus avant tout remplacement");
        var tx = UpdateTransaction.Read(fixture.Data, prepared.TransactionId!, installed, Light);
        Check.Equal(UpdateStage.Failed, tx.Stage, "journal clos en échec");
        Check.False(Directory.Exists(tx.WorkerRoot) || Directory.Exists(tx.PreviousRoot), "aucun worker lancé ni installation déplacée");
        var again = await service.InspectAsync(new(UpdateSourceKind.ManualZip, archive, Hash(archive)), default);
        Check.Equal(UpdateStage.Prepared, (await service.PrepareAsync(again.Id, true, default)).Stage, "nouvelle préparation possible");
    }

    [Test] public static async Task UnSocleAltereEmpecheLaPreparation()
    {
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", "2.0.12", flavor: Light);
        using var socle = WebViewSocle.UseForTests(WebViewSocleTests.Socle(installed, "9.0.0.1"));
        var archive = Archive(fixture, Light, "2.1.0", Light);
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, archive, Hash(archive)), default);
        File.AppendAllText(Path.Combine(installed, "wad/runtime/webview2/msedge.dll"), "x");
        var refused = await Check.ThrowsAsync<FilproOperationException>(() => service.PrepareAsync(offer.Id, true, default), "Socle altéré");
        Check.Equal("pack.socle", refused.Code, "refus avant toute préparation");
        Check.Equal(0, Directory.GetFiles(fixture.Data, "transaction.json", SearchOption.AllDirectories).Length, "aucune transaction préparée");
    }

    private static PackManifest Decode(PackManifest manifest) => PackManifestCodec.Decode(
        JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    private static string Archive(PackTestFixture fixture, string contentFlavor, string version, string nameFlavor) =>
        fixture.Zip(fixture.CreatePack("target-" + contentFlavor, version, flavor: contentFlavor), $"FILPRO-{version}-win-x64-{nameFlavor}.zip");
    private static string Hash(string path) => PackTestFixture.Hash(File.ReadAllBytes(path));
    private static string SumLine(string path) => Hash(path) + "  " + Path.GetFileName(path);
    private static Task<IDisposable> Lease() => Task.FromResult<IDisposable>(new MemoryStream());
}
