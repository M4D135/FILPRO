using Filpro.Bridge;
using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.App;

/// <summary>Une composition immuable par mode. Aucun service natif de profils n'est construit en démonstration.</summary>
internal sealed class Composition : IDisposable
{
    private readonly List<IDisposable> owned = [];
    private bool initialized;

    private Composition(EngineContext context)
    {
        Context = context;
        var modules = OperationModules.Discover(OperationModules.LoadFilproAssemblies());
        Registry = OperationModules.CreateRegistry(context, modules);
        // Une écriture absente de la liste des opérations privilégiées, ou un nom périmé, arrête le démarrage.
        PrivilegePolicy.Validate(Registry);
        Admission = AdmissionGuards.For(context);
    }
    public EngineContext Context { get; }
    public ModeDecision Decision => Context.Decision;
    public OperationRegistry Registry { get; }
    public Func<OperationDefinition, BridgeError?> Admission { get; }
    public bool PackVerified { get; private set; }
    public PackValidationResult? VerifiedPack { get; private set; }
    public string WindowTitle => ProductInfo.Name + (Decision.Mode == AppMode.Simulation ? ModeTexts.WindowTitleSuffix : "");

    public static Composition Create(ModeDecision decision, IOperatorPresence presence)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(presence);
        var clock = TimeProvider.System;
        EngineContext context;
        if (decision.Mode == AppMode.Simulation)
        {
            var world = new SimulationWorld(SimulationOptions.Default, clock);
            var common = new SimulationCommonSelectionService(clock);
            var poste = new ConfiguredPosteSource(world.Poste, world.Configuration, common);
            context = new EngineContext(decision, world, poste, clock, TechnicalLog.Instance, world.ReadCatalog(CancellationToken.None).Generation);
            context.Register<IFilproExecutionService>(world);
            context.Register<IFilproConfigurationService>(world.Configuration);
            context.Register<IFilproUpdateService>(world.Updates);
            context.Register<IFilproCommonSelectionService>(common);
            context.Register<IFilproDemonstrationSession>(world);
            return new Composition(context);
        }
        var data = AppPaths.UserData ?? throw new FilproOperationException("application.data", "Aucun dossier de données utilisable pour ce compte Windows.");
        var configuration = new LocalConfigurationService(AppPaths.AppDirectory, data);
        var templates = new LocalCommonSelectionService(data, clock, AppPaths.AppDirectory);
        // Sélections et copies privées de ruches suivent le même dossier de données que la configuration et les opérations.
        var native = new ConfiguredPosteSource(new WindowsPosteSource(clock, Path.Combine(data, "Selections"), Path.Combine(data, "PrivateHives")),
            configuration, templates);
        context = new EngineContext(decision, null, native, clock, TechnicalLog.Instance);
        var updates = new LocalUpdateService(AppPaths.AppDirectory, data, () => !context.IsClosing
            && context.Service<IFilproExecutionService>()?.Active().Any() != true);
        context.Register<IFilproConfigurationService>(configuration);
        context.Register<IFilproCommonSelectionService>(templates);
        context.Register<IFilproUpdateService>(updates);
        // Lue par l'admission du pont quand l'affichage tourne sous un autre compte.
        context.Register(presence);
        var result = new Composition(context);
        result.owned.Add(configuration); result.owned.Add(updates);
        return result;
    }

    /// <summary>Le pont n'est exposé qu'après cette vérification. Sans manifeste, les sources restent consultables en DEV.</summary>
    public async Task InitializeAsync(IProgress<PackValidationProgress> progress, CancellationToken cancellationToken)
    {
        if (initialized) return;
        var manifestPath = Path.Combine(AppPaths.AppDirectory, PackManifestCodec.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            Context.Log.Warning("Exécution depuis les sources : manifeste de pack absent, opérations natives d’écriture indisponibles.");
            initialized = true; return;
        }
        var manifest = PackIntegrityVerifier.ReadManifest(AppPaths.AppDirectory);
        if (!string.Equals(manifest.Version, ProductInfo.Version, StringComparison.Ordinal))
            throw new FilproOperationException("pack.version", "Le manifeste et l’application n’annoncent pas la même version.");
        var verified = await PackIntegrityVerifier.ValidateForStartupAsync(AppPaths.AppDirectory, manifest.Flavor, progress, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(verified.Manifest.Version, ProductInfo.Version, StringComparison.Ordinal))
            throw new FilproOperationException("pack.version", "Le manifeste vérifié et l’application n’annoncent pas la même version.");
        if (Context.Service<IFilproConfigurationService>() is LocalConfigurationService configuration)
            await configuration.UseStartupValidationAsync(verified, cancellationToken).ConfigureAwait(false);
        // Un démarrage dégradé ne confirme pas la santé d'une mise à jour.
        VerifiedPack = verified;
        PackVerified = verified.DefaultsWarning is null;
        if (verified.DefaultsWarning is { } warning) Context.Log.Warning(warning);
        if (Decision.Mode == AppMode.Poste)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[] { "scanstate.exe", "loadstate.exe", "usmtutils.exe" })
            {
                var entry = verified.Manifest.Files.SingleOrDefault(file => file.Path == "wad/USMT/" + name);
                if (entry is not null) hashes.Add(name, entry.Sha256);
            }
            async Task Verify(CancellationToken token)
            {
                var current = await PackIntegrityVerifier.ValidateForStartupAsync(AppPaths.AppDirectory, manifest.Flavor, cancellationToken: token).ConfigureAwait(false);
                if (current.ManifestSha256 != verified.ManifestSha256)
                    throw new FilproOperationException("pack.changed", "Le pack a changé depuis le démarrage. Fermer puis relancer FILPRO.");
            }
            Context.Register<IFilproExecutionService>(new WindowsFilproExecutionService(Context.Poste!,
                new WindowsExecutionOptions(Path.Combine(AppPaths.AppDirectory, "wad", "USMT"),
                    Path.Combine(AppPaths.UserData!, "Operations"), hashes, Verify), Context.Clock, journal: TechnicalLog.Instance));
        }
        initialized = true;
    }

    public void Dispose()
    {
        for (var index = owned.Count - 1; index >= 0; index--) owned[index].Dispose();
        owned.Clear();
    }
}
