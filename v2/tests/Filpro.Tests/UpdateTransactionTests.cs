using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class UpdateTransactionTests
{
    [Test] public static async Task CommitSeulementApresSanteConfirmee()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture);
        var observed = false;
        var result = await UpdateDirectoryTransaction.InstallAsync(transaction, () =>
        {
            observed = true; Check.Equal("NEW-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "nouveaux octets présents avant santé");
            Check.True(Directory.Exists(transaction.PreviousRoot), "ancien pack conservé avant santé"); return Task.FromResult(true);
        }, Lease, default);
        Check.True(observed, "contrôle réellement appelé"); Check.Equal(UpdateStage.Committed, result.Stage, "commit après santé");
        Check.Equal(UpdateStage.Committed, UpdateTransaction.Read(fixture.Data, transaction.Id, transaction.InstallRoot).Stage, "journal durable");
        using var journal = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(transaction.JournalPath));
        Check.Equal(14, journal.RootElement.EnumerateObject().Count(), "format lisible par les workers PROD déjà distribués");
        Check.Equal(1, journal.RootElement.GetProperty("schemaVersion").GetInt32(), "schéma historique PROD");
        Check.False(journal.RootElement.TryGetProperty("flavor", out _), "aucun champ que le worker historique refuserait");
    }
    [Test] public static async Task EchecDemarrageRestaureAncienPackSansEffacerNouveau()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture); var personal = Path.Combine(fixture.Data, "personnel.json"); File.WriteAllText(personal, "CONSERVER");
        var result = await UpdateDirectoryTransaction.InstallAsync(transaction, () => throw new System.ComponentModel.Win32Exception("Échec fictif de lancement"), Lease, default);
        Check.Equal(UpdateStage.RolledBack, result.Stage, "rollback automatique"); Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "ancien rétabli");
        Check.Equal("NEW-FAKE", File.ReadAllText(Path.Combine(transaction.FailedRoot, "FILPRO.exe")), "nouveau conservé"); Check.Equal("CONSERVER", File.ReadAllText(personal), "AppData inchangé");
    }
    [Test] public static async Task AbsenceAccuseDeSanteNestPasSucces()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture);
        var result = await UpdateDirectoryTransaction.InstallAsync(transaction, () => Task.FromResult(false), Lease, default);
        Check.Equal(UpdateStage.RolledBack, result.Stage, "santé refusée"); Check.NotNull(result.Error, "cause conservée");
    }
    [Test] public static async Task StageAltereNeDeplacePasInstallation()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture); File.AppendAllText(Path.Combine(transaction.StageRoot, "FILPRO.exe"), "ALTERE"); var launched = false;
        var result = await UpdateDirectoryTransaction.InstallAsync(transaction, () => { launched = true; return Task.FromResult(true); }, Lease, default);
        Check.Equal(UpdateStage.Failed, result.Stage, "stage refusé"); Check.False(launched, "aucun lancement"); Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "ancien intact");
    }
    [Test] public static async Task RepriseApresDeplacementAncienAvantPublicationNouveau()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture); Directory.Move(transaction.InstallRoot, transaction.PreviousRoot);
        transaction = transaction with { Stage = UpdateStage.OldMoved }; transaction.Save();
        var result = await UpdateDirectoryTransaction.RollbackAsync(UpdateTransaction.Read(fixture.Data, transaction.Id, transaction.InstallRoot), Lease, default);
        Check.Equal(UpdateStage.RolledBack, result.Stage, "reprise"); Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "ancien revenu"); Check.True(Directory.Exists(transaction.StageRoot), "préparation conservée");
    }
    [Test] public static async Task EchecRetourArriereConserveLesDeuxCopiesEtJournal()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture); var calls = 0;
        Task<IDisposable> FailingSecondLease() => ++calls == 1 ? Lease() : throw new IOException("Instance encore ouverte fictive");
        var result = await UpdateDirectoryTransaction.InstallAsync(transaction, () => Task.FromResult(false), FailingSecondLease, default);
        Check.Equal(UpdateStage.Failed, result.Stage, "échec honnête"); Check.True(Directory.Exists(transaction.PreviousRoot), "ancienne copie conservée"); Check.True(Directory.Exists(transaction.InstallRoot), "nouvelle copie conservée");
        Check.Contains("Retour arrière en attente", result.Error, "action récupérable signalée");
    }
    [Test] public static void SemverOrdonneDixApresNeufEtStableApresPrerelease()
    { Check.True(PackageVersion.Parse("2.10.0").CompareTo(PackageVersion.Parse("2.9.0")) > 0, "numérique"); Check.True(PackageVersion.Parse("2.0.0").CompareTo(PackageVersion.Parse("2.0.0-rc.10")) > 0, "stable"); Check.True(PackageVersion.Parse("2.0.0-rc.10").CompareTo(PackageVersion.Parse("2.0.0-rc.9")) > 0, "préversion numérique"); }
    [Test] public static async Task JournalLieALinstallationEtVersionDuPack()
    {
        using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture);
        Check.Throws<FilproOperationException>(() => UpdateTransaction.Read(fixture.Data, transaction.Id, Path.Combine(fixture.Root, "autre-installation")), "journal d'une autre installation");
        Check.Throws<FilproOperationException>(() => (transaction with { TargetVersion = "1.0.0" }).Save(), "rétrogradation falsifiée");
        var changed = transaction with { TargetVersion = "2.9.0" }; changed.Save();
        var launched = false; var result = await UpdateDirectoryTransaction.InstallAsync(changed, () => { launched = true; return Task.FromResult(true); }, Lease, default);
        Check.Equal(UpdateStage.Failed, result.Stage, "version falsifiée différente du manifeste"); Check.False(launched, "aucun lancement");
        Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "installation préservée");
    }
    [Test] public static void ChecksumsExigentUneEntreeExacteSansCheminNiDoublon()
    {
        var hash = new string('a', 64); Check.Equal(hash, UpdateSources.ParseChecksums(Encoding.UTF8.GetBytes(hash + "  FILPRO.zip\n"), "FILPRO.zip"), "somme exacte");
        foreach (var text in new[] { hash + "  ../FILPRO.zip", hash + "  FILPRO.zip\n" + hash + "  FILPRO.zip", hash + "  AUTRE.zip" })
            Check.Throws<FilproOperationException>(() => UpdateSources.ParseChecksums(Encoding.UTF8.GetBytes(text), "FILPRO.zip"), "checksum ambigu ou absent");
        var invalid = Encoding.UTF8.GetBytes(hash + "  FILPRO.zip\n").Concat(new byte[] { 0xC3, 0x28, 0x0A }).ToArray();
        Check.Throws<FilproOperationException>(() => UpdateSources.ParseChecksums(invalid, "FILPRO.zip"), "octets UTF-8 invalides : erreur de source, pas d'état bloqué");
    }
    [Test] public static async Task RepriseAuxFrontieresDesDeplacementsEtDuJournal()
    {
        // Coupures observables : journal avant/après déplacement de l'ancien, du nouveau,
        // puis de la version retirée ; aucun EXE fictif n'est exécuté.
        foreach (var (stage, published, retired) in new[]
        {
            (UpdateStage.WaitingForExit, false, false), (UpdateStage.OldMoved, false, false),
            (UpdateStage.OldMoved, true, false), (UpdateStage.NewInstalled, true, false),
            (UpdateStage.AwaitingHealth, true, false), (UpdateStage.RollingBack, true, false),
            (UpdateStage.RollingBack, true, true), (UpdateStage.Failed, true, true),
        })
        {
            using var fixture = new PackTestFixture(); var transaction = await Prepare(fixture);
            Directory.Move(transaction.InstallRoot, transaction.PreviousRoot);
            if (published) Directory.Move(transaction.StageRoot, transaction.InstallRoot);
            if (retired) Directory.Move(transaction.InstallRoot, transaction.FailedRoot);
            transaction = transaction with { Stage = stage }; transaction.Save();
            var result = await UpdateDirectoryTransaction.RollbackAsync(UpdateTransaction.Read(fixture.Data, transaction.Id), Lease, default);
            Check.Equal(UpdateStage.RolledBack, result.Stage, $"reprise depuis {stage}/{published}/{retired}");
            Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "ancienne version rétablie");
            Check.Equal("NEW-FAKE", File.ReadAllText(Path.Combine(published ? transaction.FailedRoot : transaction.StageRoot, "FILPRO.exe")), "nouveaux octets conservés");
        }
    }
    [Test] public static async Task RetourArriereDejaAppliqueFinaliseJournalEtResteIdempotent()
    {
        foreach (var stage in new[] { UpdateStage.RollingBack, UpdateStage.Failed })
        {
            using var fixture = new PackTestFixture(); var transaction = await RestoredBeforeFinalJournal(fixture, stage);
            var result = await UpdateDirectoryTransaction.RollbackAsync(UpdateTransaction.Read(fixture.Data, transaction.Id), Lease, default);
            Check.Equal(UpdateStage.RolledBack, result.Stage, "journal finalisé après coupure");
            var journal = File.ReadAllText(transaction.JournalPath);
            await UpdateDirectoryTransaction.RollbackAsync(result, Lease, default);
            Check.Equal(journal, File.ReadAllText(transaction.JournalPath), "réessai sans réécriture");
            Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "installation conservée");
            Check.Equal("NEW-FAKE", File.ReadAllText(Path.Combine(transaction.FailedRoot, "FILPRO.exe")), "version retirée conservée");
        }
    }
    [Test] public static async Task FinalisationRetourArriereAttendVerrouEtRespecteAnnulation()
    {
        using var fixture = new PackTestFixture(); var transaction = await RestoredBeforeFinalJournal(fixture);
        var journal = File.ReadAllText(transaction.JournalPath);
        await Check.ThrowsAsync<IOException>(() => UpdateDirectoryTransaction.RollbackAsync(transaction,
            () => throw new IOException("Installation encore utilisée"), default), "verrou obligatoire même sans déplacement restant");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Check.ThrowsAsync<OperationCanceledException>(() => UpdateDirectoryTransaction.RollbackAsync(transaction, Lease, cancelled.Token), "annulation respectée");
        Check.Equal(journal, File.ReadAllText(transaction.JournalPath), "aucun succès journalisé sans contrôle");
        Check.Equal("OLD-FAKE", File.ReadAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe")), "octets préservés");
    }
    [Test] public static async Task ReconciliationRefuseInstallationOuDefautsModifies()
    {
        foreach (var changeDefaults in new[] { false, true })
        {
            using var fixture = new PackTestFixture(); var transaction = await RestoredBeforeFinalJournal(fixture);
            if (changeDefaults) PackTestFixture.SetDefaults(transaction.InstallRoot, new SharedConfiguration());
            else File.AppendAllText(Path.Combine(transaction.InstallRoot, "FILPRO.exe"), "MODIFIE");
            var journal = File.ReadAllText(transaction.JournalPath);
            await Check.ThrowsAsync<FilproOperationException>(() => UpdateDirectoryTransaction.RollbackAsync(transaction, Lease, default), "aucune confiance dans le seul état du journal");
            Check.Equal(journal, File.ReadAllText(transaction.JournalPath), "journal non finalisé");
            Check.True(Directory.Exists(transaction.FailedRoot), "copie retirée préservée");
        }
    }
    [Test] public static async Task ReconciliationRefuseVersionDifferenteEtDestinationAmbigue()
    {
        using var fixture = new PackTestFixture(); var transaction = await RestoredBeforeFinalJournal(fixture);
        var wrongVersion = transaction with { PreviousVersion = "1.9.0" };
        await Check.ThrowsAsync<FilproOperationException>(() => UpdateDirectoryTransaction.RollbackAsync(wrongVersion, Lease, default), "version précédente relue");
        File.WriteAllText(transaction.PreviousRoot, "NE-PAS-ECRASER");
        await Check.ThrowsAsync<FilproOperationException>(() => UpdateDirectoryTransaction.RollbackAsync(transaction, Lease, default), "fichier à la place du répertoire refusé");
        Check.Equal("NE-PAS-ECRASER", File.ReadAllText(transaction.PreviousRoot), "entrée ambiguë conservée");
        Check.Equal(UpdateStage.RollingBack, UpdateTransaction.Read(fixture.Data, transaction.Id).Stage, "aucun succès enregistré");
    }
    [Test] public static async Task EtatProposeRepriseSansDossierPreviousPuisRetireActionApresSucces()
    {
        using var fixture = new PackTestFixture(); var transaction = await RestoredBeforeFinalJournal(fixture);
        using var service = new LocalUpdateService(transaction.InstallRoot, fixture.Data, () => true);
        var pending = await service.ReadAsync(default);
        Check.True(pending.CanRollback, "reprise proposée dans les paramètres malgré previous déjà déplacé");
        Check.Equal(UpdateStage.RollingBack, pending.Stage, "lecture seule ne proclame pas le succès");
        await UpdateDirectoryTransaction.RollbackAsync(transaction, Lease, default);
        var completed = await service.ReadAsync(default);
        Check.False(completed.CanRollback, "action retirée après finalisation");
        Check.Equal(UpdateStage.RolledBack, completed.Stage, "état final relu");
    }
    private static async Task<UpdateTransaction> RestoredBeforeFinalJournal(PackTestFixture fixture, UpdateStage stage = UpdateStage.RollingBack)
    {
        var transaction = await Prepare(fixture);
        Directory.Move(transaction.InstallRoot, transaction.PreviousRoot);
        Directory.Move(transaction.StageRoot, transaction.InstallRoot);
        transaction = transaction with { Stage = stage }; transaction.Save();
        Directory.Move(transaction.InstallRoot, transaction.FailedRoot);
        Directory.Move(transaction.PreviousRoot, transaction.InstallRoot);
        return transaction;
    }
    private static async Task<UpdateTransaction> Prepare(PackTestFixture fixture)
    {
        var installed = fixture.CreatePack("installed", "2.0.0", "OLD-FAKE"); var target = fixture.CreatePack("target", "2.1.0", "NEW-FAKE");
        var oldPack = await PackIntegrityVerifier.ValidateAsync(installed, "PROD"); var newPack = await PackIntegrityVerifier.ValidateAsync(target, "PROD");
        var transaction = new UpdateTransaction(1, Guid.NewGuid().ToString("N"), installed, fixture.Data, "2.0.0", oldPack.ManifestSha256, null,
            "2.1.0", newPack.ManifestSha256, null, UpdateStage.Prepared, new string('b', 64), DateTimeOffset.UtcNow, null);
        Directory.CreateDirectory(transaction.WorkRoot); Directory.Move(target, transaction.StageRoot); transaction.Save(create: true); return transaction;
    }
    private static Task<IDisposable> Lease() => Task.FromResult<IDisposable>(new NoopLease());
    private sealed class NoopLease : IDisposable { public void Dispose() { } }
}
