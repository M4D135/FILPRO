using Filpro.Bridge;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Liste fermée des opérations privilégiées : le registre réel, composé comme l'application le compose, concorde avec elle ;
/// une écriture oubliée, un nom périmé ou une classe changée arrête la composition ; la garde d'admission d'un poste
/// pleinement capable n'admet que les huit écritures de la liste.
/// </summary>
internal sealed class PrivilegePolicyTests
{
    private static EngineContext Poste() =>
        new EngineContext(ModeResolver.Resolve(["--poste"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance).WithSameAccount();

    // Même découverte que la composition de l'application : modules publics des assemblages Filpro approuvés.
    private static OperationRegistry RealRegistry() =>
        OperationModules.CreateRegistry(Poste(), OperationModules.Discover(OperationModules.LoadFilproAssemblies()));

    private static List<OperationDefinition> Definitions(OperationRegistry registry) =>
        [.. registry.Names.Order(StringComparer.Ordinal).Select(name => registry.TryGet(name, out var definition) ? definition : throw new InvalidOperationException(name))];

    private static OperationDefinition Definition(string name, OperationClass operationClass) =>
        new(name, operationClass, (_, _, _) => throw new InvalidOperationException("jamais appelé"), "test");

    [Test]
    public void LeRegistreReelConcordeAvecLaListeFermee()
    {
        var registry = RealRegistry();
        PrivilegePolicy.Validate(registry);
        foreach (var definition in Definitions(registry).Where(definition => definition.Class == OperationClass.Write))
            Check.True(PrivilegePolicy.IsPrivileged(definition.Name), definition.Name + " : toute écriture système est soumise à la confirmation Windows");
        Check.Equal(10, PrivilegePolicy.Operations.Count, "dix opérations privilégiées");
        Check.Equal(10, PrivilegePolicy.Operations.Select(operation => operation.Name).Distinct(StringComparer.Ordinal).Count(), "noms uniques");
        Check.Equal(8, PrivilegePolicy.Operations.Count(operation => operation.Class == OperationClass.Write), "huit écritures système");
        foreach (var name in new[] { "updates.prepare", "configuration.applyDefaults" })
        {
            Check.True(PrivilegePolicy.IsPrivileged(name), name + " : écriture locale privilégiée");
            Check.True(registry.TryGet(name, out var definition) && definition.Class == OperationClass.LocalWrite, name + " reste une écriture locale");
        }
        Check.Equal(3, PrivilegePolicy.Operations.Count(operation => operation.Tier == PrivilegeTier.Code), "trois opérations touchent le code de FILPRO");
        Check.False(PrivilegePolicy.IsPrivileged("session.info"), "une lecture n'est pas privilégiée");
        Check.False(PrivilegePolicy.IsPrivileged(null!), "un nom absent n'est pas privilégié");
    }

    [Test]
    public void UneEcritureOublieeArreteLaComposition()
    {
        var definitions = Definitions(RealRegistry());
        definitions.Add(Definition("analyser.nouvelleEcriture", OperationClass.Write));
        Check.Throws<InvalidOperationException>(() => PrivilegePolicy.Validate(new OperationRegistry(definitions)), "écriture absente de la liste");
    }

    [Test]
    public void UnNomPerimeOuUneClasseChangeeArreteLaComposition()
    {
        var definitions = Definitions(RealRegistry());
        var missing = definitions.Where(definition => definition.Name != "updates.prepare").ToList();
        Check.Throws<InvalidOperationException>(() => PrivilegePolicy.Validate(new OperationRegistry(missing)), "nom de la liste absent du registre");
        var downgraded = definitions.Select(definition => definition.Name == "configuration.applyDefaults" ? definition with { Class = OperationClass.LocalRead } : definition).ToList();
        Check.Throws<InvalidOperationException>(() => PrivilegePolicy.Validate(new OperationRegistry(downgraded)), "classe déclarée différente");
        var promoted = definitions.Select(definition => definition.Name == "updates.prepare" ? definition with { Class = OperationClass.Write } : definition).ToList();
        Check.Throws<InvalidOperationException>(() => PrivilegePolicy.Validate(new OperationRegistry(promoted)), "une écriture locale devenue Write est refusée");
        Check.Throws<ArgumentNullException>(() => PrivilegePolicy.Validate(null!), "registre absent");
    }

    [Test]
    public void MisesAJourEtSourceSontReserveesAuCompteEleve()
    {
        string[] reserved = ["updates.inspect", "updates.install", "updates.prepare", "updates.rollback", "updates.saveSource"];
        var names = Definitions(RealRegistry()).Select(definition => definition.Name).ToArray();
        var refused = names.Where(PrivilegePolicy.RefusedAcrossAccounts).Order(StringComparer.Ordinal).ToArray();
        Check.Equal(string.Join(",", reserved), string.Join(",", refused), "exactement le code de FILPRO, la recherche de l'offre et la source");
        foreach (var operation in PrivilegePolicy.Operations)
            Check.Equal<PrivilegeTier?>(operation.Tier, PrivilegePolicy.TierOf(operation.Name), operation.Name + " : palier de la liste");
        Check.Null(PrivilegePolicy.TierOf("updates.inspect"), "la recherche de l'offre n'est pas une opération privilégiée");
        Check.Null(PrivilegePolicy.TierOf(null!), "nom absent");
        Check.False(PrivilegePolicy.RefusedAcrossAccounts(null!), "nom absent");
        var withoutInspect = Definitions(RealRegistry()).Where(definition => definition.Name != "updates.inspect").ToList();
        Check.Throws<InvalidOperationException>(() => PrivilegePolicy.Validate(new OperationRegistry(withoutInspect)), "recherche de l'offre absente du registre");
    }

    [Test]
    public void LaGardeDUnPostePleinementCapableNAdmetQueLesHuitEcrituresDeLaListe()
    {
        var context = Poste();
        context.Register<IFilproExecutionService>(new CapableExecutor());
        context.Register<IFilproUpdateService>(new NativeUpdates());
        var guard = AdmissionGuards.For(context);
        var admitted = Definitions(RealRegistry())
            .Where(definition => definition.Class == OperationClass.Write && guard(definition) is null)
            .Select(definition => definition.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = PrivilegePolicy.Operations.Where(operation => operation.Class == OperationClass.Write)
            .Select(operation => operation.Name).Order(StringComparer.Ordinal).ToArray();
        Check.Equal(string.Join(",", expected), string.Join(",", admitted), "exactement les écritures de la liste fermée");
        Check.Equal(FilproErrors.WriteUnavailable, guard(Definition("analyser.nouvelleEcriture", OperationClass.Write))?.Code, "aucune autre écriture admise");
        foreach (var operation in PrivilegePolicy.Operations.Where(operation => operation.Class == OperationClass.LocalWrite))
            Check.Null(guard(Definition(operation.Name, OperationClass.LocalWrite)), operation.Name + " : écriture locale admise puis confirmée par son gestionnaire");
    }

    /// <summary>Exécutant natif déclaré pleinement capable ; aucune méthode n'est appelée par la garde.</summary>
    internal sealed class CapableExecutor : IFilproExecutionService
    {
        public bool Demonstration => false;
        public ExecutionCapabilities Capabilities => new(true, true, true, true, []);
        public event Action<ExecutionJobSnapshot>? Changed { add { } remove { } }
        public IReadOnlyList<ExecutionJobSnapshot> List() => [];
        public ExecutionJobSnapshot? Get(string id) => null;
        public Task<ExecutionStoreInspection> InspectStoreAsync(string bundle, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartCatalogAsync(StoreCatalogRequest request, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RecoveryPlanReview> CreateRecoveryPlanAsync(RecoveryPlanRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepairPlanReview> CreateRepairPlanAsync(RepairPlanRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartBackupAsync(BackupPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartRestoreAsync(RestorePlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartRecoveryAsync(RecoveryPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartRepairAsync(RepairPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepairRollbackReview> ReviewRepairRollbackAsync(RepairRollbackRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartRepairRollbackAsync(RepairRollbackRequest request, ExecutionStartOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Fournisseur de mise à jour natif déclaré ; la garde ne lit que <see cref="Demonstration"/>.</summary>
    internal sealed class NativeUpdates : IFilproUpdateService
    {
        public bool Demonstration => false;
        public event Action<UpdateStatus>? Changed { add { } remove { } }
        public Task<UpdateSourcePreference> ReadSourceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateSourcePreference> SaveSourceAsync(UpdateSourceKind kind, string? folder, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateStatus> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateOffer> InspectAsync(UpdateSourceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateStatus> PrepareAsync(string offerId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateHandoff> InstallAsync(string transactionId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateHandoff> RollbackAsync(string transactionId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
