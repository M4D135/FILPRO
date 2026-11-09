using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>
/// Monde de démonstration cohérent, en mémoire. Catalogue, historique, sélection, exécution et réglages partagent cette
/// instance par session. Ni API Windows, ni fichier, ni réseau ; les chemins sont des libellés fictifs fermés.
/// </summary>
public sealed class SimulationWorld : IFilproSimulationSource, IFilproExecutionService, IFilproDemonstrationSession
{
    private sealed record MemoryStore(DemonstrationStore Summary, ExecutionStoreInspection Inspection,
        IReadOnlyList<ExecutionStoreEntry>? CatalogSource = null);
    private const string MissingCatalogNotice = "Ancien magasin fictif sans catalogue préparé : confirmer la lecture longue avant de choisir des fichiers.";
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly ExecutionJobCoordinator coordinator;
    private readonly Dictionary<string, MemoryStore> stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExecutionJobSnapshot> history = [];
    private readonly Dictionary<string, DemonstrationProfileState> profileStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DemonstrationProfileState> repairReceipts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryPlanReview> recoveryPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RepairPlanReview> repairPlans = new(StringComparer.Ordinal);
    private readonly string generation = Guid.NewGuid().ToString("N");
    private string scenario = "succeeded";
    private int sequence;
    private int activeResources;

    public SimulationWorld(SimulationOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
        Poste = new SimulationPosteSource(options.Validate(), clock);
        coordinator = new ExecutionJobCoordinator(true, clock);
        coordinator.Changed += snapshot => Changed?.Invoke(snapshot);
        Configuration = new SimulationConfigurationService(clock);
        Updates = new SimulationUpdateService();
        Seed();
    }

    public bool Demonstration => true;
    public IFilproPosteSource Poste { get; }
    public IFilproExecutionService Execution => this;
    public IFilproConfigurationService Configuration { get; }
    public IFilproUpdateService Updates { get; }
    public int ActiveResources => Volatile.Read(ref activeResources);
    public ExecutionCapabilities Capabilities { get; } = new(true, true, true, true, []);
    public event Action<ExecutionJobSnapshot>? Changed;

    private static readonly DemonstrationDestination[] Destinations =
    [
        new("local", "Atelier — disque local fictif", @"D:\FILPRO-DEMO\Local", "local"),
        new("usb", "Intervention — clé USB fictive", @"E:\FILPRO-DEMO\USB", "usb"),
        new("lan", "Équipe — partage fictif", @"\\partage.example.invalid\FILPRO-DEMO", "unc"),
        new("reserve", "Réserve — second poste fictif", @"D:\FILPRO-DEMO\Reserve", "local"),
    ];
    private static readonly DemonstrationAssets Assets = new(
        @"D:\FILPRO-DEMO\Local\configuration-exemple.json", @"D:\FILPRO-DEMO\Local\export-demonstration.json",
        @"D:\FILPRO-DEMO\Local\MisesAJour", @"D:\FILPRO-DEMO\Local\MisesAJour\FILPRO_DEMO_PROD.zip",
        @"D:\FILPRO-DEMO\Local\Recuperation", @"D:\FILPRO-DEMO\Reserve\Prealable");
    private static readonly DemonstrationScenario[] Scenarios =
    [
        new("succeeded", "Parcours complet", "Préparation, exécution et bilan réussis sur les données fictives."),
        new("partial", "Résultat partiel", "Une omission fictive est annoncée dans le bilan ; aucun succès complet."),
        new("failed", "Erreur contrôlée", "Une erreur fictive interrompt l'action et préserve l'état avant validation."),
    ];

    public DemonstrationCatalog ReadCatalog(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return new(true, generation, Destinations.ToArray(), stores.Values.Select(store => store.Summary)
            .OrderByDescending(store => store.CreatedUtc).ToArray(), Scenarios.ToArray(), scenario, Assets, profileStates.Values.ToArray(),
            repairReceipts.Select(receipt => new DemonstrationRepairReceipt(receipt.Key, receipt.Value.Account, receipt.Value.Sid)).ToArray());
    }

    public DemonstrationCatalog SetScenario(string outcome, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Scenarios.Any(choice => string.Equals(choice.Id, outcome, StringComparison.Ordinal)))
            throw Refused("Scénario de démonstration inconnu.");
        lock (gate)
        {
            if (coordinator.List().Any(job => !ExecutionJobCoordinator.IsTerminal(job.State)))
                throw Refused("Terminer ou annuler l'opération fictive avant de changer son scénario.");
            scenario = outcome;
        }
        return ReadCatalog(cancellationToken);
    }

    public IReadOnlyList<ExecutionJobSnapshot> List()
    {
        lock (gate) return coordinator.List().Concat(history).OrderByDescending(job => job.StartedAt).ToArray();
    }
    public ExecutionJobSnapshot? Get(string id) => List().FirstOrDefault(job => job.Id == id);
    public Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var known = Get(id) ?? throw Refused("Opération fictive inconnue.");
        return ExecutionJobCoordinator.IsTerminal(known.State) ? Task.FromResult(known) : coordinator.CancelAsync(id, cancellationToken);
    }
    public Task ShutdownAsync(CancellationToken cancellationToken) => coordinator.ShutdownAsync(cancellationToken);

    public Task<ExecutionStoreInspection> InspectStoreAsync(string bundle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(RequireStore(bundle).Inspection);
    }

    public Task<ExecutionJobSnapshot> StartCatalogAsync(StoreCatalogRequest request, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.KeyPath is not null) RequirePath(request.KeyPath);
        lock (gate)
        {
            var store = RequireStore(request.Bundle);
            if (request.ExpectedManifestSha256 is { } expected
                && !string.Equals(expected, store.Inspection.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw Refused("Le magasin fictif a changé depuis sa confirmation.");
            if (store.Inspection.Store.Encrypted && request.KeyPath is null)
                throw Refused("Le catalogue du magasin fictif chiffré exige un chemin de clé fictif.");
            var entries = store.CatalogSource ?? store.Inspection.Entries;
            return Start(ExecutionKind.Catalog, "Préparation du catalogue fictif", [], options, cancellationToken, (partial, _) =>
            {
                var complete = !partial && (store.CatalogSource is not null || store.Inspection.CatalogComplete);
                var exposed = partial ? entries.Take(Math.Max(1, entries.Count / 2)).ToArray() : entries.ToArray();
                var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("payload-fictif|" + store.Inspection.Id)));
                var warnings = store.Inspection.Warnings.Where(warning => warning != MissingCatalogNotice).Append(
                    complete ? "Catalogue fictif préparé en mémoire ; aucune archive Windows n'a été ouverte."
                        : "Catalogue fictif partiel : aucune absence de fichier n'est déduite.").ToArray();
                var inspection = store.Inspection with { Entries = exposed, CatalogComplete = complete,
                    PayloadVerified = !partial, SourcePayloadSha256 = digest, Warnings = warnings };
                var summary = store.CatalogSource is null ? store.Summary : store.Summary with
                    { Label = complete ? "Ancien magasin fictif — catalogue préparé" : "Ancien magasin fictif — catalogue partiel" };
                stores[request.Bundle] = store with { Summary = summary, Inspection = inspection };
                return store.Inspection.Bundle;
            }, steps: ["Examen du magasin fictif", "Préparation du contrôle fictif", "Vérification de l'archive fictive",
                "Lecture des entrées fictives", "Construction du catalogue fictif", "Libération des ressources fictives"]);
        }
    }

    public Task<RecoveryPlanReview> CreateRecoveryPlanAsync(RecoveryPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequirePath(request.Destination);
        if (request.KeyPath is not null) RequirePath(request.KeyPath);
        lock (gate)
        {
            var store = RequireStore(request.Bundle).Inspection;
            if (store.Store.Encrypted && request.KeyPath is null) throw Refused("Le magasin fictif chiffré exige un chemin de clé fictif.");
            var ids = request.EntryIds?.ToHashSet(StringComparer.Ordinal) ?? throw Refused("Choisir les fichiers fictifs à récupérer.");
            if (ids.Count == 0 || ids.Count != request.EntryIds!.Count || ids.Count > 500
                || ids.Any(id => !store.Entries.Any(entry => entry.Id == id))) throw Refused("Sélection de fichiers fictifs invalide.");
            if (SameOrChild(request.Destination, store.Bundle)) throw Refused("La récupération doit utiliser un autre dossier fictif.");
            var plan = new RecoveryPlanReview(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), true, store.Bundle,
                store.Id, store.ManifestSha256, request.Destination, request.KeyPath,
                store.Entries.Where(entry => ids.Contains(entry.Id)).ToArray(), store.Store.Encrypted, [],
                "Extraction fictive vers un nouveau dossier en mémoire ; aucun fichier du poste n'est ouvert.", store.SourcePayloadSha256);
            recoveryPlans[plan.Id] = plan;
            return Task.FromResult(plan);
        }
    }

    public Task<RepairPlanReview> CreateRepairPlanAsync(RepairPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequirePath(request.SafetyBackupRoot);
        var profile = RequireTarget(request.Sid);
        var plan = new RepairPlanReview(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), true, profile.Sid, profile.Account,
            profile.LocalPath!, profile.LocalPath + ".preserve-demo", request.SafetyBackupRoot,
            ["Simuler la sauvegarde préalable du profil", "Préserver l'ancien dossier fictif", "Simuler la réparation des références du profil", "Vérifier le reçu fictif et la possibilité de retour arrière"],
            [], "Démonstration : l'ancien profil reste dans le monde fictif ; aucun compte ou registre Windows n'est modifié.");
        lock (gate) repairPlans[plan.Id] = plan;
        return Task.FromResult(plan);
    }

    public Task<ExecutionJobSnapshot> StartBackupAsync(BackupPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireDemonstration(plan.Demonstration);
        RequirePath(plan.Destination);
        if (plan.KeyPath is not null) RequirePath(plan.KeyPath);
        foreach (var profile in plan.Profiles)
        {
            var known = RequireProfile(profile.Sid);
            if (!known.MigrationSupported || !string.Equals(known.LocalPath, profile.ProfilePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(known.Account, profile.Account, StringComparison.OrdinalIgnoreCase)) throw Refused("Profil extérieur au monde fictif.");
            if (known.Loaded is null || (known.Loaded == true && !plan.UseVss))
                throw Refused("Le chargement doit être connu ; un profil chargé exige un accord VSS explicite, même en démonstration.");
        }
        if (plan.Profiles.Count == 0) throw Refused("Aucun profil fictif retenu.");
        return Start(ExecutionKind.Backup, "Sauvegarde de " + plan.Profiles.Count.ToString(CultureInfo.InvariantCulture) + " profil(s) fictif(s)",
            plan.Profiles.Select(profile => profile.Sid).ToArray(), options, cancellationToken, (partial, jobId) =>
            {
                var source = plan.Profiles.Select(profile => new RestoreSourceProfile(profile.Account, profile.Sid, profile.ProfilePath)).ToArray();
                var choices = SoftwareCatalog.Group(plan.Profiles.SelectMany(profile => profile.Files).DistinctBy(item => item.Scope + "|" + item.Rel).ToArray(),
                    plan.Profiles.SelectMany(profile => profile.Registry).DistinctBy(item => item.Rel).ToArray());
                return AddStore(source, plan.Destination, partial, choices, encrypted: plan.Encrypted,
                    selectedFiles: plan.Profiles.ToDictionary(profile => profile.Sid, profile => profile.Files, StringComparer.Ordinal), migApp: plan.MigApp).Inspection.Bundle;
            });
    }

    public Task<ExecutionJobSnapshot> StartRestoreAsync(RestorePlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireDemonstration(plan.Demonstration);
        ExecutionStoreInspection inspection;
        lock (gate)
        {
            var store = inspection = RequireStore(plan.Bundle).Inspection;
            if (plan.Users.Count == 0 || plan.Users.Any(account => !store.SourceProfiles.Any(source => source.Account.Equals(account, StringComparison.OrdinalIgnoreCase))))
                throw Refused("Compte source absent du magasin fictif.");
            if (store.Store.Encrypted && plan.SourceKeyPath is null) throw Refused("Le magasin fictif chiffré exige une clé fictive.");
        }
        foreach (var target in plan.Targets) RequireTarget(target.Sid);
        if (plan.PriorBackup.Root is not null) RequirePath(plan.PriorBackup.Root);
        if (plan.SourceKeyPath is not null) RequirePath(plan.SourceKeyPath);
        return Start(ExecutionKind.Restore, "Restauration multi-profils fictive", plan.Targets.Select(target => target.Sid).ToArray(),
            options, cancellationToken, (partial, jobId) =>
            {
                var receipt = Assets.SafetyBackupRoot + "\\restauration-" + jobId + ".json";
                foreach (var target in plan.Targets)
                    profileStates[target.Sid] = new(target.Sid, target.Account, partial ? "restorePartial" : "restored", receipt, inspection.Id);
                return receipt;
            }, forcePartial: inspection.Store.Partial);
    }

    public Task<ExecutionJobSnapshot> StartRecoveryAsync(RecoveryPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireDemonstration(plan.Demonstration);
        lock (gate)
        {
            if (!recoveryPlans.Remove(plan.Id, out var kept) || kept != plan) throw Refused("Plan de récupération fictif absent ou modifié.");
            var store = RequireStore(plan.Bundle).Inspection;
            if (store.Id != plan.StoreId || store.ManifestSha256 != plan.ManifestSha256 || store.SourcePayloadSha256 != plan.SourcePayloadSha256)
                throw Refused("Le catalogue fictif a changé depuis la revue du plan de récupération.");
        }
        RequirePath(plan.Destination);
        return Start(ExecutionKind.Recover, "Récupération de fichiers fictifs", [], options, cancellationToken,
            (_, jobId) => plan.Destination + "\\extraction-" + jobId);
    }

    public Task<ExecutionJobSnapshot> StartRepairAsync(RepairPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireDemonstration(plan.Demonstration);
        RequireTarget(plan.Sid);
        lock (gate)
        {
            if (!repairPlans.Remove(plan.Id, out var kept) || kept != plan) throw Refused("Plan de réparation fictif absent ou modifié.");
        }
        return Start(ExecutionKind.Repair, "Réparation fictive de " + plan.Account, [plan.Sid], options, cancellationToken, (partial, jobId) =>
        {
            var receipt = Assets.SafetyBackupRoot + "\\reparation-" + jobId + ".json";
            repairReceipts[receipt] = profileStates[plan.Sid];
            profileStates[plan.Sid] = new(plan.Sid, plan.Account, partial ? "repairPartial" : "repairPrepared", receipt, null);
            return receipt;
        });
    }

    public Task<RepairRollbackReview> ReviewRepairRollbackAsync(RepairRollbackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        DemonstrationProfileState previous;
        lock (gate) if (!repairReceipts.TryGetValue(request.ReceiptPath, out previous!)) throw Refused("Reçu absent du monde fictif ; aucun fichier réel n'est ouvert.");
        var profile = RequireTarget(previous.Sid);
        return Task.FromResult(new RepairRollbackReview(request.ReceiptPath, DemonstrationReceiptSha256(request.ReceiptPath, previous), true,
            previous.Sid, previous.Account, "demonstration", profile.LocalPath!, profile.LocalPath + ".preserve-demo",
            Assets.SafetyBackupRoot, request.ReceiptPath, null));
    }

    // Empreinte stable d'un reçu fictif : aucun fichier n'est lu en démonstration.
    private static string DemonstrationReceiptSha256(string receiptPath, DemonstrationProfileState state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receiptPath + "\n" + state.Sid + "\n" + state.State)));

    public Task<ExecutionJobSnapshot> StartRepairRollbackAsync(RepairRollbackRequest request, ExecutionStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DemonstrationProfileState previous;
        lock (gate) if (!repairReceipts.TryGetValue(request.ReceiptPath, out previous!)) throw Refused("Reçu absent du monde fictif ; aucun fichier réel n'est ouvert.");
        if (request.ExpectedReceiptSha256 is { } expected
            && !string.Equals(expected, DemonstrationReceiptSha256(request.ReceiptPath, previous), StringComparison.OrdinalIgnoreCase))
            throw Refused("Le reçu fictif a changé depuis sa confirmation.");
        RequireTarget(previous.Sid);
        return Start(ExecutionKind.Rollback, "Retour arrière fictif de réparation", [previous.Sid], options, cancellationToken,
            (partial, _) =>
            {
                if (!partial) { profileStates[previous.Sid] = previous; repairReceipts.Remove(request.ReceiptPath); }
                return request.ReceiptPath;
            });
    }

    private Task<ExecutionJobSnapshot> Start(ExecutionKind kind, string title, IReadOnlyList<string> sids,
        ExecutionStartOptions options, CancellationToken cancellationToken, Func<bool, string, string> commit, bool forcePartial = false,
        IReadOnlyList<string>? steps = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.ManualSpaceAcknowledged) throw Refused("Accusé de capacité requis avant la préparation fictive.");
        lock (gate)
        {
            var outcome = forcePartial && scenario == "succeeded" ? "partial" : scenario;
            var started = coordinator.Start(kind, title, options, async job =>
            {
                Interlocked.Increment(ref activeResources);
                try
                {
                    string[] actions = steps?.ToArray() ?? ["Vérification du plan fictif", "Préparation des données fictives", "Lecture des fichiers fictifs", "Lecture des clés fictives", "Traitement du premier profil", "Traitement des profils suivants", "Contrôle des conflits fictifs", "Vérification du résultat fictif", "Préparation du bilan", "Libération des ressources fictives"];
                    for (var step = 0; step < actions.Length; step++)
                    {
                        job.CancellationToken.ThrowIfCancellationRequested();
                        job.Report(step < 2 ? ExecutionState.Preparing : ExecutionState.Running, actions[step],
                            sids.Count == 0 ? null : sids[Math.Min(sids.Count - 1, step * sids.Count / actions.Length)], step, actions.Length,
                            (actions.Length - step) * .35);
                        await Task.Delay(TimeSpan.FromMilliseconds(350), clock, job.CancellationToken).ConfigureAwait(false);
                        if (outcome == "failed" && step == 5) throw new FilproOperationException("demonstration.failure", "Erreur fictive demandée : le résultat n'a pas été validé et les données fictives précédentes sont conservées.");
                    }
                    job.CancellationToken.ThrowIfCancellationRequested();
                    string result;
                    lock (gate) result = commit(outcome == "partial", job.Id);
                    if (outcome != "partial") job.Report(ExecutionState.Running, "Bilan fictif terminé", null, actions.Length, actions.Length, 0);
                    return new ExecutionOutcome(outcome == "partial" ? ExecutionState.Partial : ExecutionState.Succeeded,
                        outcome == "partial" ? ["Démonstration : un fichier fictif verrouillé a été omis ; résultat partiel à examiner."] :
                        ["Démonstration — aucune donnée réelle lue ou écrite."], result);
                }
                finally { Interlocked.Decrement(ref activeResources); }
            }, cancellationToken);
            scenario = "succeeded";
            return Task.FromResult(started);
        }
    }

    public AccueilSnapshot BuildAccueil()
    {
        var inventory = Poste.ReadProfiles(CancellationToken.None);
        var jobs = List();
        var profiles = inventory.Profiles.Select(profile => new FilproProfile(profile.Account + " · fictif", "Poste de démonstration",
            profile.Session.State == SessionState.Active ? FilproProfileState.Actif : profile.Session.State == SessionState.Unknown ? FilproProfileState.Inconnu : FilproProfileState.Inactif,
            profile.Session.Count, profile.LastUse)).ToArray();
        var sessions = inventory.Profiles.Select(profile => new FilproSession(profile.Account + " · fictif",
            profile.Session.State switch { SessionState.Active => FilproSessionState.Active, SessionState.Disconnected => FilproSessionState.Deconnectee, SessionState.None => FilproSessionState.Fermee, _ => FilproSessionState.Inconnue }, profile.LastUse)).ToArray();
        FilproBackup[] backups;
        lock (gate) backups = stores.Values.Select(store => new FilproBackup(store.Summary.Id, store.Summary.CreatedUtc,
            store.Inspection.Entries.Sum(entry => entry.Bytes ?? 0), string.Join(", ", store.Summary.Accounts),
            store.Summary.Partial ? FilproBackupState.Partielle : FilproBackupState.Reussie)).ToArray();
        var tasks = jobs.Select(job => new FilproTask(job.Id, job.Title, job.CancellationRequested && !ExecutionJobCoordinator.IsTerminal(job.State)
            ? FilproTaskState.ArretDemande : job.State switch
            {
                ExecutionState.Pending => FilproTaskState.EnAttente,
                ExecutionState.Preparing or ExecutionState.Running => FilproTaskState.EnCours,
                ExecutionState.Cancelled => FilproTaskState.Annulee,
                ExecutionState.Failed => FilproTaskState.Echouee,
                ExecutionState.Partial => FilproTaskState.Partielle,
                _ => FilproTaskState.Terminee,
            }, job.ProgressPercent is null ? null : (int)job.ProgressPercent.Value)).ToArray();
        var journal = jobs.Take(60).Select(job => new FilproJournalEntry(job.FinishedAt ?? job.StartedAt,
            job.State == ExecutionState.Failed ? FilproJournalLevel.Erreur : job.State == ExecutionState.Partial ? FilproJournalLevel.Avertissement : FilproJournalLevel.Info,
            job.Title + " — " + (job.Error?.Message ?? job.CurrentAction))).ToArray();
        return new(clock.GetUtcNow(), ModeTexts.SimulationLabel, true, profiles, sessions, backups, tasks, journal,
            new AccueilAvailability(true, true, true, true, false));
    }

    private void Seed()
    {
        var profiles = Poste.ReadProfiles(CancellationToken.None).Profiles.Where(profile => profile.MigrationSupported).ToArray();
        foreach (var profile in Poste.ReadProfiles(CancellationToken.None).Profiles)
            profileStates[profile.Sid] = new(profile.Sid, profile.Account, "original", null, null);
        for (var index = 0; index < 60; index++)
        {
            var available = profiles.Where(profile => profile.Loaded == false && profile.Session.State == SessionState.None).ToArray();
            var selected = index == 0 && available.Length > 1 ? available.Take(2).ToArray() : index % 3 == 0 && profiles.Length > 1
                ? new[] { profiles[(index + 1) % profiles.Length], profiles[(index + 2) % profiles.Length] }
                : [profiles[index % profiles.Length]];
            var source = selected.Select(profile => new RestoreSourceProfile(profile.Account, profile.Sid, profile.LocalPath)).ToArray();
            var inventories = selected.ToDictionary(profile => profile.Sid,
                profile => Poste.ReadSoftware(profile.Sid, SoftwareQuery.Empty, CancellationToken.None), StringComparer.Ordinal);
            var choices = SoftwareCatalog.Group(inventories.Values.SelectMany(inventory => inventory.Groups).SelectMany(group => group.Files)
                .Where(item => item.Selected).DistinctBy(item => item.Scope + "|" + item.Rel).ToArray(),
                inventories.Values.SelectMany(inventory => inventory.Groups).SelectMany(group => group.Registry)
                .Where(item => item.Selected).DistinctBy(item => item.Rel).ToArray());
            var at = clock.GetUtcNow().AddHours(-index * 7 - 1);
            var selectedFiles = inventories.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<SoftwareItem>)pair.Value.Groups.SelectMany(group => group.Files).Where(item => item.Selected).ToArray(), StringComparer.Ordinal);
            var store = AddStore(source, Destinations[index % Destinations.Length].Path, index % 11 == 10, choices, at, selectedFiles: selectedFiles);
            if (index == 59)
            {
                // Un exemple V1 sans catalogue : les comptes sont connus, aucun SID ni chemin source n'est inventé.
                store = store with
                {
                    Summary = store.Summary with { Label = "Ancien magasin fictif — catalogue à préparer" },
                    CatalogSource = store.Inspection.Entries,
                    Inspection = store.Inspection with
                    {
                        Store = store.Inspection.Store with { Legacy = true, Status = "Ancien magasin fictif" },
                        SourceProfiles = source.Select(profile => new RestoreSourceProfile(profile.Account, null, null)).ToArray(),
                        Entries = [], CatalogComplete = false, PayloadVerified = false, SourcePayloadSha256 = null,
                        Warnings = [MissingCatalogNotice],
                    },
                };
                stores[store.Summary.Bundle] = store;
            }
            var state = index % 17 == 16 ? ExecutionState.Failed : index % 13 == 12 ? ExecutionState.Cancelled
                : store.Summary.Partial ? ExecutionState.Partial : ExecutionState.Succeeded;
            var kind = state is ExecutionState.Failed or ExecutionState.Cancelled ? ExecutionKind.Restore : ExecutionKind.Backup;
            history.Add(new ExecutionJobSnapshot("demo-history-" + (index + 1).ToString("000", CultureInfo.InvariantCulture),
                kind, (kind == ExecutionKind.Backup ? "Sauvegarde fictive · " : "Restauration fictive · ") + string.Join(", ", source.Select(profile => profile.Account)), state,
                state == ExecutionState.Succeeded ? "Sauvegarde fictive terminée" : "Scénario historique fictif à examiner",
                state == ExecutionState.Succeeded ? 100 : 64, null, at.AddMinutes(-4), at, true,
                state == ExecutionState.Failed ? new ExecutionError("demonstration.history", "Exemple fictif de support indisponible.") : null,
                source[0].Sid, 8, 8, store.Inspection.Warnings, store.Inspection.Bundle));
        }
    }

    private MemoryStore AddStore(IReadOnlyList<RestoreSourceProfile> source, string destination, bool partial,
        IReadOnlyList<SoftwareGroup> choices, DateTimeOffset? created = null, bool encrypted = false,
        IReadOnlyDictionary<string, IReadOnlyList<SoftwareItem>>? selectedFiles = null, bool migApp = false)
    {
        var id = "DEMO-SAV-" + (++sequence).ToString("000", CultureInfo.InvariantCulture);
        var at = created ?? clock.GetUtcNow();
        var bundle = destination + "\\" + id;
        var entries = source.SelectMany((profile, i) =>
        {
            IReadOnlyList<SoftwareItem> files = [];
            if (profile.Sid is not null && selectedFiles is not null && selectedFiles.TryGetValue(profile.Sid, out var values)) files = values;
            return new[]
            {
                new ExecutionStoreEntry(id + "-" + i + "-doc", "profiles/" + i + "/Documents/Présentation fictive.txt", 640),
                new ExecutionStoreEntry(id + "-" + i + "-desk", "profiles/" + i + "/Desktop/Lisez-moi-démo.txt", 320),
            }.Concat(files.Select((file, n) => new ExecutionStoreEntry(id + "-" + i + "-app-" + n,
                "profiles/" + i + "/AppData/" + file.Scope + "/" + file.Rel.Replace('\\', '/') + "/exemple-fictif.txt", 128)));
        }).ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "|" + string.Join('|', source.Select(profile => profile.Account)))));
        var summary = new DemonstrationStore(id, bundle, "Magasin fictif " + id, at, source.Select(profile => profile.Account).ToArray(), partial ? "partial" : "succeeded", partial, encrypted);
        var store = new RestoreStoreInfo(bundle, true, false, partial, true, encrypted, partial ? "Partiel fictif" : "Prêt fictif",
            summary.Accounts, "POSTE-DEMO.example.invalid", at.ToString("O", CultureInfo.InvariantCulture), null);
        var warnings = new List<string>();
        warnings.Add(partial ? "Magasin fictif partiel : un fichier verrouillé est absent." : "Manifeste et contrôle d'intégrité fictifs, conservés uniquement en mémoire.");
        if (migApp) warnings.Add("MigApp fictif inclus : son périmètre USMT s'ajoute aux groupes FILPRO. Le catalogue de cet exemple ne prétend pas énumérer ce contenu supplémentaire.");
        var inspection = new ExecutionStoreInspection(id, bundle, sha, store, source, entries, !migApp, true, warnings, choices);
        var item = new MemoryStore(summary, inspection);
        stores.Add(bundle, item);
        return item;
    }

    private MemoryStore RequireStore(string bundle) => stores.TryGetValue(bundle, out var store) ? store
        : throw Refused("Magasin absent du catalogue fictif. Aucun chemin réel n'est ouvert en démonstration.");
    private ProfileCandidate RequireProfile(string sid) => Poste.ReadProfiles(CancellationToken.None).Profiles.FirstOrDefault(profile => profile.Sid == sid)
        ?? throw Refused("Profil absent du monde fictif.");
    private ProfileCandidate RequireTarget(string sid)
    {
        var profile = RequireProfile(sid);
        if (!profile.MigrationSupported || profile.Loaded != false || sid == Poste.ReadCurrentAccountSid(CancellationToken.None)
            || profile.Session.State != SessionState.None || profile.Session.Count != 0 || profile.Session.ReadStatus != SessionReadStatus.Complete)
            throw Refused("Le scénario conserve les gardes de profil courant, chargé ou non pris en charge.");
        return profile;
    }
    internal static void RequirePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('/') || path.Contains('\0')
            || path.Split('\\').Any(segment => segment is "." or "..") || !Destinations.Any(destination => SameOrChild(path, destination.Path)))
            throw Refused("Choisir un emplacement du catalogue fictif. Aucun chemin réel n'est utilisé depuis la démonstration.");
    }
    private static bool SameOrChild(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase);
    private static void RequireDemonstration(bool demonstration)
    { if (!demonstration) throw Refused("Un plan réel ne peut pas être exécuté par le monde fictif."); }
    internal static FilproOperationException Refused(string message) => new("demonstration.boundary", message);
}
