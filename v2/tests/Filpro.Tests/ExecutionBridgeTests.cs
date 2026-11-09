using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

internal sealed class ExecutionBridgeTests
{
    private const string Hugo = "S-1-5-21-111222333-444555666-777888999-1004";
    private sealed class Harness
    {
        public SimulationWorld World { get; } = new(SimulationOptions.Default, TimeProvider.System);
        public EngineContext Context { get; }
        private readonly BridgeDispatcher dispatcher;
        private readonly List<BridgeResponse> responses = [];
        public Harness()
        {
            var common = new SimulationCommonSelectionService(TimeProvider.System);
            Context = new(ModeResolver.Resolve(["--simulation"], null), World,
                new ConfiguredPosteSource(World.Poste, World.Configuration, common), TimeProvider.System,
                SilentTechnicalJournal.Instance, World.ReadCatalog(CancellationToken.None).Generation);
            Context.Register<IFilproExecutionService>(World); Context.Register<IFilproDemonstrationSession>(World);
            Context.Register<IFilproConfigurationService>(World.Configuration); Context.Register<IFilproUpdateService>(World.Updates);
            Context.Register<IFilproCommonSelectionService>(common);
            dispatcher = new(OperationModules.CreateRegistry(Context, [typeof(SessionModule).Assembly]), responses.Add, AdmissionGuards.For(Context));
        }
        public async Task<BridgeResponse> Call(string operation, object args)
        {
            responses.Clear();
            await dispatcher.HandleAsync(JsonSerializer.Serialize(new { id = "r1", op = operation, args }), CancellationToken.None);
            return responses.Single(response => response.Kind is "result" or "error");
        }
    }
    private static T Result<T>(BridgeResponse response)
    {
        Check.Equal("result", response.Kind, response.Error?.Message ?? "réponse attendue");
        return (T)response.Data!;
    }
    [Test]
    public async Task LePontCompletDemonstrationRefuseTouteEcritureNativeAvantService()
    {
        var harness = new Harness();
        foreach (var operation in new[] { "sauvegarder.confirmPlan", "sauvegarder.confirmRestorePlan", "analyser.confirmRepair", "analyser.rollbackRepair", "recuperer.confirmPlan", "recuperer.catalog", "updates.install", "updates.rollback" })
        {
            var response = await harness.Call(operation, new { confirmed = true });
            Check.Equal(FilproErrors.SimulationReadOnly, response.Error?.Code, "refus avant même les arguments du gestionnaire");
        }
        Check.Equal(FilproErrors.SimulationUnavailable, (await harness.Call("session.choosePath", new { kind = "folder" })).Error?.Code, "pas de dialogue réel");
        Check.False(harness.World.List().Any(job => !job.Demonstration), "aucune tâche réelle");
    }
    [Test]
    public async Task SeulLePlanDuServeurPeutDemarrerUneSauvegardeEtUneSeuleFois()
    {
        var harness = new Harness();
        var catalog = harness.World.ReadCatalog(CancellationToken.None);
        var refused = await harness.Call("sauvegarder.plan", new { sids = new[] { Hugo }, destination = catalog.Destinations[0].Path, migApp = true });
        Check.Equal(BackupPlan.ProfileScopeCode, refused.Error?.Code, "la page ne peut pas élargir la capture aux réglages machine");
        var plan = Result<BackupPlanReview>(await harness.Call("sauvegarder.plan", new { sids = new[] { Hugo }, destination = catalog.Destinations[0].Path }));
        Check.False(plan.MigApp, "périmètre limité aux profils");
        Check.Equal("arguments.invalid", (await harness.Call("demonstration.backup", new { planId = plan.Id, confirmed = true, acknowledgeCapacity = true, destination = "C:\\interdit" })).Error?.Code, "aucun plan remplaçable dans la page");
        var job = Result<ExecutionJobSnapshot>(await harness.Call("demonstration.backup", new { planId = plan.Id, confirmed = true, acknowledgeCapacity = true }));
        Check.True(job.Demonstration, "la tâche créée est fictive");
        Check.Equal("plan.expired", (await harness.Call("demonstration.backup", new { planId = plan.Id, confirmed = true, acknowledgeCapacity = true })).Error?.Code, "rejeu refusé");
        await harness.World.ShutdownAsync(CancellationToken.None);
        Check.Equal(0, harness.World.ActiveResources, "nettoyage réellement terminé");
    }
    [Test]
    public async Task GitHubNEstPlusUneSourceDeMiseAJour()
    {
        var harness = new Harness();
        foreach (var operation in new[] { "updates.githubSession", "updates.setGitHubToken" })
            Check.Equal("operation.unknown", (await harness.Call(operation, new { })).Error?.Code, operation + " retirée");
        foreach (var operation in new[] { "updates.inspect", "updates.saveSource" })
            Check.Equal("arguments.invalid", (await harness.Call(operation, new { kind = "gitHubReleases" })).Error?.Code, operation + " : source GitHub inconnue");
    }
    [Test]
    public async Task UneSessionEnFermetureRefuseMemeLesNouvellesLectures()
    {
        var harness = new Harness(); harness.Context.BeginClosing();
        Check.Equal("session.closing", (await harness.Call("session.info", new { })).Error?.Code, "barrière de fin de session");
    }
    [Test]
    public async Task ModeleCommunParLePontNeRecoitNiCheminNiCleArbitraire()
    {
        var harness = new Harness();
        var saved = Result<CommonSelectionState>(await harness.Call("sauvegarder.setCommonSelection", new { sid = Hugo, groupIds = new[] { "known:vlc" } }));
        Check.True(saved.Demonstration && saved.AppData.Count > 0 && saved.RegistryKeys.Count > 0, "les portées sont reconstruites ensemble");
        var refused = await harness.Call("sauvegarder.setCommonSelection", new { sid = Hugo, groupIds = new[] { "known:vlc" }, path = "C:\\Windows" });
        Check.Equal("arguments.invalid", refused.Error?.Code, "champ supplémentaire refusé");
        var state = Result<CommonSelectionState>(await harness.Call("sauvegarder.readCommonSelection", new { }));
        Check.Equal(saved.AppData.Count, state.AppData.Count, "modèle conservé");
    }
    [Test]
    public void LesCapacitesNePermettentJamaisUneNouvelleCommandeWriteImplicite()
    {
        var context = new EngineContext(ModeResolver.Resolve(["--poste"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance)
            .WithSameAccount();
        context.Register<IFilproExecutionService>(new CapabilityOnlyExecutor(false));
        var guard = AdmissionGuards.For(context);
        OperationDefinition Write(string name) => new(name, OperationClass.Write, (_, _, _) => throw new InvalidOperationException("jamais appelé"), "test");
        Check.Null(guard(Write("sauvegarder.confirmPlan")), "route native connue et capacité disponible");
        Check.Equal(FilproErrors.WriteUnavailable, guard(Write("autre.ecrire"))?.Code, "aucune ouverture générale de Write");
        var crossed = new EngineContext(ModeResolver.Resolve(["--poste"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance);
        crossed.Register<IFilproExecutionService>(new CapabilityOnlyExecutor(true));
        Check.Equal(FilproErrors.WriteUnavailable, AdmissionGuards.For(crossed)(Write("sauvegarder.confirmPlan"))?.Code, "fournisseur d'un autre mode refusé");
    }
    private sealed class CapabilityOnlyExecutor(bool demonstration) : IFilproExecutionService
    {
        public bool Demonstration => demonstration;
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
}
