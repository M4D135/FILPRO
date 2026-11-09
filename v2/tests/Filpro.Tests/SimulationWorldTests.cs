using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>Contrats du monde en mémoire. Ces tests ne valident ni Windows, ni WebView2, ni une migration native.</summary>
internal sealed class SimulationWorldTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Test]
    public async Task CatalogueEtMagasinsPartagentLesSixIdentitesFictives()
    {
        var world = World();
        var catalog = world.ReadCatalog(Ct);
        var profiles = world.Poste.ReadProfiles(Ct).Profiles;
        Check.Equal(6, profiles.Count, "six profils fictifs");
        Check.Equal(4, catalog.Destinations.Count, "quatre supports fictifs");
        Check.Equal(60, catalog.Stores.Count, "soixante magasins liés au même monde");
        Check.Equal(6, catalog.ProfileStates!.Count, "état initial de chaque profil");
        foreach (var summary in catalog.Stores)
        {
            var store = await world.InspectStoreAsync(summary.Bundle, Ct);
            Check.True(store.Store.Simulated, "le magasin est explicitement fictif");
            foreach (var source in store.SourceProfiles)
            {
                Check.True(profiles.Any(profile => profile.Account == source.Account), "aucune identité empruntée à un poste réel");
                if (store.Store.Legacy)
                    Check.True(source.Sid is null && source.ProfilePath is null, "un ancien magasin ne reçoit aucun SID ni chemin source inventé");
                else Check.True(profiles.Any(profile => profile.Sid == source.Sid && profile.Account == source.Account), "identité du magasin récent cohérente");
            }
        }
        Check.False(world.BuildAccueil().Availability.RealSystemActions, "aucune action Windows revendiquée");
        Check.False(typeof(SimulationWorld).Assembly.GetReferencedAssemblies().Any(name => name.Name == "Filpro.Platform"), "aucune dépendance au fournisseur Windows");
    }

    [Test]
    public async Task PlansReelsCheminsExterieursEtScenariosForcesSontRefuses()
    {
        var world = World();
        var plan = Backup(world);
        var count = world.List().Count;
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartBackupAsync(plan with { Demonstration = false }, new(true, true), Ct), "plan réel refusé");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartBackupAsync(plan with { Destination = @"C:\VraisProfils" }, new(true, true), Ct), "chemin extérieur refusé");
        await Check.ThrowsAsync<FilproOperationException>(() => world.InspectStoreAsync(@"C:\VraieSauvegarde", Ct), "aucun magasin réel inspecté");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartRepairRollbackAsync(new(@"C:\Reel\recu.json"), new(true, true), Ct), "aucun reçu réel ouvert");
        Check.Throws<FilproOperationException>(() => world.SetScenario("executeNative", Ct), "scénario fermé");
        Check.Equal(count, world.List().Count, "aucune opération engagée sur refus");
    }

    [Test]
    public async Task ConfirmationPuisAnnulationAttendentLeNettoyageSansAjouterDeMagasin()
    {
        var world = new SimulationWorld(SimulationOptions.Default, TimeProvider.System);
        var plan = Backup(world);
        var initial = world.ReadCatalog(Ct).Stores.Count;
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartBackupAsync(plan, new(false, true), Ct), "confirmation avant préparation");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartBackupAsync(plan, new(true, false), Ct), "capacité explicitement reconnue");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Changed += job => { if (job.State == ExecutionState.Preparing) entered.TrySetResult(); };
        var started = await world.StartBackupAsync(plan, new(true, true), Ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check.True(world.ActiveResources > 0, "le délégué a acquis sa ressource fictive");
        var stopped = await world.CancelAsync(started.Id, Ct);
        Check.Equal(ExecutionState.Cancelled, stopped.State, "annulation seulement après la fin du délégué");
        Check.Equal(0, world.ActiveResources, "finally achevé avant le bilan d'annulation");
        Check.Equal(initial, world.ReadCatalog(Ct).Stores.Count, "aucun magasin réussi inventé après annulation");
    }

    [Test]
    public async Task SauvegardeMultiProfilsGardeLeursChoixEtLeChiffrementFictif()
    {
        var world = World();
        var plan = Backup(world) with { Encrypted = true, KeyPath = @"E:\FILPRO-DEMO\USB\cle-fictive.key", MigApp = true };
        var done = await Complete(world, () => world.StartBackupAsync(plan, new(true, true), Ct));
        Check.Equal(ExecutionState.Succeeded, done.State, "résultat fictif terminé");
        Check.Equal(done.TotalUnits, done.CompletedUnits, "toutes les étapes sont comptées à la fin de la démonstration");
        var store = await world.InspectStoreAsync(done.ResultPath!, Ct);
        Check.Equal(2, store.SourceProfiles.Count, "aucun compte abandonné");
        Check.True(store.Store.Encrypted, "le magasin garde le choix de chiffrement");
        Check.False(store.CatalogComplete, "le catalogue fictif ne prétend pas énumérer le périmètre MigApp supplémentaire");
        Check.Equal(1, store.ConflictChoices!.Count, "seuls les groupes réellement retenus sont au catalogue");
        Check.Equal("known:chrome", store.ConflictChoices[0].Id, "fichiers et registre Chrome conservés ensemble");
        Check.True(store.ConflictChoices[0].Files.Count > 0 && store.ConflictChoices[0].Registry.Count > 0, "deux portées présentes");
        var emptySid = plan.Profiles.Single(profile => profile.Files.Count == 0 && profile.Registry.Count == 0).Sid;
        var emptyIndex = store.SourceProfiles.Select((profile, index) => (profile.Sid, Index: index)).Single(profile => profile.Sid == emptySid).Index;
        Check.False(store.Entries.Any(entry => entry.RelativePath.StartsWith($"profiles/{emptyIndex}/AppData/", StringComparison.Ordinal)), "la sélection vide ne reçoit pas les fichiers de l'autre profil après canonisation de l'ordre");
        Check.Equal(0, world.ActiveResources, "aucune ressource fictive retenue après réussite");
    }

    [Test]
    public async Task ErreurFictivePreserveLEtatPuisReparationEtRetourArriereLeRetablissent()
    {
        var world = World();
        var original = world.ReadCatalog(Ct);
        world.SetScenario("failed", Ct);
        var failed = await Complete(world, () => world.StartBackupAsync(Backup(world), new(true, true), Ct));
        Check.Equal(ExecutionState.Failed, failed.State, "échec explicite");
        Check.Equal(original.Stores.Count, world.ReadCatalog(Ct).Stores.Count, "échec avant validation sans nouveau magasin");
        var profile = world.Poste.ReadProfiles(Ct).Profiles.First(profile => profile.Account == @"DEMO\sophie");
        var plan = await world.CreateRepairPlanAsync(new(profile.Sid, original.Assets.SafetyBackupRoot), Ct);
        var repaired = await Complete(world, () => world.StartRepairAsync(plan, new(true, true), Ct));
        Check.Equal("repairPrepared", world.ReadCatalog(Ct).ProfileStates!.Single(item => item.Sid == profile.Sid).State, "préparation fictive visible, aucune connexion réelle prétendue");
        var receipt = world.ReadCatalog(Ct).RepairReceipts!.Single();
        Check.Equal(repaired.ResultPath!, receipt.ReceiptPath, "seul le reçu réellement émis dans ce monde est proposé à la reprise");
        Check.Equal(profile.Sid, receipt.Sid, "reçu lié au bon profil fictif");
        var rollback = await Complete(world, () => world.StartRepairRollbackAsync(new(repaired.ResultPath!), new(true, true), Ct));
        Check.Equal(ExecutionState.Succeeded, rollback.State, "retour arrière suivi");
        Check.Equal("original", world.ReadCatalog(Ct).ProfileStates!.Single(item => item.Sid == profile.Sid).State, "état fictif initial restauré");
        Check.Equal(0, world.ReadCatalog(Ct).RepairReceipts!.Count, "le reçu consommé ne reste pas proposé");
    }

    [Test]
    public async Task CatalogueFictifExigeAccordEtResteLieAuMagasinConnu()
    {
        var world = World();
        var before = await LegacyStore(world);
        Check.Equal(0, before.Entries.Count, "pas de catalogue inventé par l'inspection courte");
        Check.False(before.CatalogComplete, "absence de catalogue explicite");
        var count = world.List().Count;
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartCatalogAsync(new(@"C:\VraiMagasin"), new(true, true), Ct), "magasin réel refusé");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartCatalogAsync(new(before.Bundle, @"C:\Reel\cle.key"), new(true, true), Ct), "clé réelle refusée");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartCatalogAsync(new(before.Bundle), new(false, true), Ct), "confirmation avant lecture longue");
        await Check.ThrowsAsync<FilproOperationException>(() => world.StartCatalogAsync(new(before.Bundle), new(true, false), Ct), "espace reconnu avant préparation");
        Check.Equal(count, world.List().Count, "aucun travail admis sur refus");
        var done = await Complete(world, () => world.StartCatalogAsync(new(before.Bundle), new(true, true), Ct));
        Check.Equal(ExecutionKind.Catalog, done.Kind, "travail de catalogage distinct d'une récupération");
        Check.Equal(ExecutionState.Succeeded, done.State, "catalogue fictif terminé");
        Check.Equal(before.Bundle, done.ResultPath!, "retour vers le magasin, pas vers un fichier privé");
        var after = await world.InspectStoreAsync(before.Bundle, Ct);
        Check.True(after.CatalogComplete && after.Entries.Count > 0, "entrées publiées après le travail fictif");
        Check.Equal(64, after.SourcePayloadSha256!.Length, "empreinte fictive explicite liée à la même archive");
        var plan = await world.CreateRecoveryPlanAsync(new(after.Bundle, world.ReadCatalog(Ct).Assets.RecoveryDestination, [after.Entries[0].Id]), Ct);
        Check.Equal(after.SourcePayloadSha256, plan.SourcePayloadSha256, "le plan garde le lien avec le catalogue relu");
        Check.Equal(0, world.ActiveResources, "nettoyage terminé avant le bilan");
    }

    [Test]
    public async Task AnnulationEtEchecDuCatalogueNePublientPasDEntreesPuisLePartielResteExplicite()
    {
        var world = new SimulationWorld(SimulationOptions.Default, TimeProvider.System);
        var before = await LegacyStore(world);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Changed += job => { if (job.Kind == ExecutionKind.Catalog && job.State == ExecutionState.Preparing) entered.TrySetResult(); };
        var running = await world.StartCatalogAsync(new(before.Bundle), new(true, true), Ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await world.CancelAsync(running.Id, Ct);
        Check.Equal(ExecutionState.Cancelled, stopped.State, "arrêt après nettoyage");
        Check.Equal(0, (await world.InspectStoreAsync(before.Bundle, Ct)).Entries.Count, "annulation sans publication de catalogue");
        Check.Equal(0, world.ActiveResources, "aucune ressource retenue après l'arrêt");

        var accelerated = World();
        var source = await LegacyStore(accelerated);
        accelerated.SetScenario("failed", Ct);
        var failed = await Complete(accelerated, () => accelerated.StartCatalogAsync(new(source.Bundle), new(true, true), Ct));
        Check.Equal(ExecutionState.Failed, failed.State, "erreur contrôlée explicite");
        Check.Equal(0, (await accelerated.InspectStoreAsync(source.Bundle, Ct)).Entries.Count, "échec sans publication de catalogue");
        accelerated.SetScenario("partial", Ct);
        var partial = await Complete(accelerated, () => accelerated.StartCatalogAsync(new(source.Bundle), new(true, true), Ct));
        var result = await accelerated.InspectStoreAsync(source.Bundle, Ct);
        Check.Equal(ExecutionState.Partial, partial.State, "bilan partiel distinct d'une réussite");
        Check.True(result.Entries.Count > 0, "entrées fictives disponibles annoncées");
        Check.False(result.CatalogComplete, "catalogue incomplet non promu complet");
        Check.False(result.PayloadVerified, "contrôle partiel non promu intégrité complète");
    }

    [Test]
    public async Task RestaurerUnMagasinPartielNeDevientPasUneReussiteComplete()
    {
        var world = World();
        var catalog = world.ReadCatalog(Ct);
        var summary = catalog.Stores.First(store => store.Partial && store.Accounts.Count == 1);
        var inspection = await world.InspectStoreAsync(summary.Bundle, Ct);
        var target = world.Poste.ReadProfiles(Ct).Profiles.Single(profile => profile.Account == @"DEMO\sophie");
        var source = inspection.SourceProfiles[0];
        var request = new RestorePlanRequest(inspection.Store, [source.Account], [new(source.Account, target.Account)],
            [new(target.Sid, target.IdentityKind, target.Account, target.LocalPath!, target.Loaded)], inspection.SourceProfiles,
            inspection.ConflictChoices, null, new(PriorBackupState.Declaree, catalog.Assets.SafetyBackupRoot, null, false, "Sauvegarde préalable fictive"),
            world.Poste.ReadCurrentAccountSid(Ct), null, PartialAcknowledged: true, Demonstration: true);
        var plan = RestorePlan.Validate(request, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        var done = await Complete(world, () => world.StartRestoreAsync(plan, new(true, true), Ct));
        Check.Equal(ExecutionState.Partial, done.State, "la source partielle ne produit pas une réussite complète");
        Check.Equal("restorePartial", world.ReadCatalog(Ct).ProfileStates!.Single(profile => profile.Sid == target.Sid).State, "le bilan partiel reste visible dans le monde fictif");
    }

    [Test]
    public async Task ConfigurationEtMiseAJourRestentDansLaSessionFictive()
    {
        var world = World();
        var catalog = world.ReadCatalog(Ct);
        var initial = await world.Configuration.ReadAsync(Ct);
        var saved = await world.Configuration.SaveAsync(initial.Settings with { Theme = "dark", MigApp = true }, initial.Revision, Ct);
        Check.NotNull(saved.BackupPath, "copie préalable fictive");
        await Check.ThrowsAsync<FilproOperationException>(() => world.Configuration.SaveAsync(initial.Settings, initial.Revision, Ct), "ancienne révision refusée");
        var preview = await world.Configuration.PreviewDefaultsAsync(Ct);
        var defaults = await world.Configuration.ApplyDefaultsAsync(preview.Id, true, Ct);
        Check.True(defaults.Demonstration, "défauts uniquement fictifs");
        Check.NotNull(defaults.WrittenPath, "document fictif identifiable");
        await Check.ThrowsAsync<FilproOperationException>(() => world.Configuration.ExportAsync(@"C:\Reel\config.json", true, Ct), "aucune écriture réelle via export");
        var offer = await world.Updates.InspectAsync(new(UpdateSourceKind.Folder, catalog.Assets.UpdateFolderPath), Ct);
        await Check.ThrowsAsync<FilproOperationException>(() => world.Updates.PrepareAsync(offer.Id, false, Ct), "préparation explicite");
        var prepared = await world.Updates.PrepareAsync(offer.Id, true, Ct);
        await world.Updates.InstallAsync(prepared.TransactionId!, true, Ct);
        Check.Equal("2.1.0-demo", (await world.Updates.ReadAsync(Ct)).CurrentVersion, "version changée en mémoire");
        await world.Updates.RollbackAsync(prepared.TransactionId!, true, Ct);
        Check.Equal(ProductInfo.Version + "-demo", (await world.Updates.ReadAsync(Ct)).CurrentVersion, "retour arrière en mémoire");
        var fresh = World();
        Check.Equal("system", (await fresh.Configuration.ReadAsync(Ct)).Settings.Theme, "une autre session ne reprend pas les réglages fictifs");
    }

    private static SimulationWorld World() => new(SimulationOptions.Default, new AcceleratedClock());
    private static async Task<ExecutionStoreInspection> LegacyStore(SimulationWorld world)
    {
        foreach (var summary in world.ReadCatalog(Ct).Stores)
        {
            var store = await world.InspectStoreAsync(summary.Bundle, Ct);
            if (store.Store.Legacy) return store;
        }
        throw new InvalidOperationException("Le monde de démonstration doit proposer un ancien magasin sans catalogue.");
    }
    private static BackupPlanReview Backup(SimulationWorld world)
    {
        var profiles = world.Poste.ReadProfiles(Ct).Profiles.Where(profile => profile.MigrationSupported
            && profile.Loaded == false && profile.Session.State == SessionState.None).Take(2).ToArray();
        var chrome = world.Poste.ReadSoftware(profiles[0].Sid, SoftwareQuery.Empty, Ct).Groups.Single(group => group.Id == "known:chrome");
        var selections = profiles.Select((profile, index) => new BackupProfileSelection(profile.Sid, profile.IdentityKind,
            profile.Account, profile.LocalPath!, profile.Loaded,
            index == 0 ? chrome.Files : [], index == 0 ? chrome.Registry : [])).ToArray();
        return BackupPlan.Create(new(selections, world.ReadCatalog(Ct).Destinations[0].Path, Demonstration: true),
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
    }
    private static async Task<ExecutionJobSnapshot> Complete(SimulationWorld world, Func<Task<ExecutionJobSnapshot>> start)
    {
        var completion = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(ExecutionJobSnapshot job) { if (ExecutionJobCoordinator.IsTerminal(job.State)) completion.TrySetResult(job); }
        world.Changed += Observe;
        try { await start(); return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { world.Changed -= Observe; }
    }
    private sealed class AcceleratedClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.FromMilliseconds(1), period);
    }
}
