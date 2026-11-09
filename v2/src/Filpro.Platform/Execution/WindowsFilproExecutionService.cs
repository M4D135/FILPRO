using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Exécutant réel. L'instance simulée ne référence jamais cette classe ni ses E/S.</summary>
public sealed partial class WindowsFilproExecutionService : IFilproExecutionService
{
    private readonly IFilproPosteSource source;
    private readonly WindowsExecutionOptions options;
    private readonly TimeProvider clock;
    private readonly ExecutionFileSystem files = new();
    private readonly ExecutionJobCoordinator jobs;
    private readonly ExecutionHistoryStore history;
    private readonly ExecutionProfileGuard profiles;
    private readonly WindowsBackupStoreReader stores;
    private readonly IExecutionProcessRunner processes;
    private readonly OfflineUserHiveManager hives;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public WindowsFilproExecutionService(IFilproPosteSource source, WindowsExecutionOptions options, TimeProvider clock,
        IExecutionProcessRunner? processRunner = null, ITechnicalJournal? journal = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(clock);
        if (source.Demonstration) throw new FilproOperationException("execution.mode", "Un fournisseur fictif ne peut pas alimenter le moteur réel.");
        ArgumentNullException.ThrowIfNull(options.VerifyPackAsync); ArgumentNullException.ThrowIfNull(options.ToolSha256);
        this.source = source; this.options = options; this.clock = clock;
        history = new(options.StateDirectory);
        jobs = new(false, clock, history.Save, journal ?? SilentTechnicalJournal.Instance); profiles = new(source); stores = new(files, options.UsmtRoot);
        processes = processRunner ?? new WindowsExecutionProcess(options, files);
        hives = new(files, new WindowsOfflineHiveNative(), Path.Combine(options.StateDirectory, "Hives"));
        jobs.Changed += snapshot => Changed?.Invoke(snapshot);
    }
    public bool Demonstration => false;
    public ExecutionCapabilities Capabilities
    {
        get
        {
            var windows = OperatingSystem.IsWindows();
            var administrator = IsAdministrator();
            var usmt = new[] { "scanstate.exe", "loadstate.exe", "usmtutils.exe" }.All(name => options.ToolSha256.ContainsKey(name));
            var blocked = WindowsNativeExecutionSafety.BlockReason;
            var available = windows && administrator && usmt && blocked is null;
            return new(available, available, available, available,
                !windows ? ["L'exécution native nécessite Windows."] : !administrator
                    ? ["Relancer explicitement FILPRO en administrateur pour une opération réelle ; aucune élévation automatique."]
                    : !usmt ? ["Le pack ne fournit pas un jeu USMT vérifié complet."] : blocked is not null ? [blocked] : []);
        }
    }
    public event Action<ExecutionJobSnapshot>? Changed;
    public IReadOnlyList<ExecutionJobSnapshot> List() => history.Merge(jobs.List());
    public IReadOnlyList<ExecutionJobSnapshot> Active() => jobs.List().Where(job => !ExecutionJobCoordinator.IsTerminal(job.State)).ToArray();
    public ExecutionJobSnapshot? Get(string id) => jobs.Get(id) ?? List().FirstOrDefault(job => job.Id == id);
    public Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken) => jobs.CancelAsync(id, cancellationToken);
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await jobs.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        await hives.CleanupAsync().ConfigureAwait(false);
    }
    public async Task<ExecutionStoreInspection> InspectStoreAsync(string bundle, CancellationToken cancellationToken)
    { RequireMode(false); return (await ReadStoreAsync(bundle, cancellationToken).ConfigureAwait(false)).Inspection; }

    public Task<ExecutionJobSnapshot> StartBackupAsync(BackupPlanReview plan, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        if (plan.MigApp) throw new FilproOperationException(BackupPlan.ProfileScopeCode, BackupPlan.ProfileScopeNotice);
        RequireStart(plan.Demonstration, plan.Id, plan.CreatedUtc, start, cancellationToken);
        if (!Capabilities.Backup) throw new FilproOperationException("execution.usmt", "USMT vérifié indisponible.");
        foreach (var profile in plan.Profiles)
        {
            var fresh = profiles.Match(profile.Sid, profile.Account, profile.ProfilePath, false, cancellationToken);
            if (fresh.Loaded != false && !plan.UseVss) throw new FilproOperationException("backup.vss", "Un profil chargé exige la capture VSS explicite.");
        }
        _ = StoragePathPolicy.NormalizeWindows(plan.Destination);
        return Task.FromResult(jobs.Start(ExecutionKind.Backup, "Sauvegarde de " + plan.Profiles.Count + " profil(s)", start,
            context => WithOperationAsync(context, () => BackupAsync(plan, context)), cancellationToken));
    }

    public async Task<ExecutionJobSnapshot> StartRestoreAsync(RestorePlanReview plan, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        RequireStart(plan.Demonstration, plan.Id, plan.CreatedUtc, start, cancellationToken);
        if (!Capabilities.Restore) throw new FilproOperationException("execution.usmt", "USMT vérifié indisponible.");
        var store = await ReadStoreAsync(plan.Bundle, cancellationToken).ConfigureAwait(false);
        MatchStore(plan, store);
        foreach (var target in plan.Targets) profiles.Match(target.Sid, target.Account, target.ProfilePath, true, cancellationToken);
        if (plan.Targets.Count != plan.Users.Count || plan.Mappings.Any(m => m.TargetSid is null))
            throw new FilproOperationException("restore.target", "Chaque cible doit déjà posséder un profil local identifié, déconnecté et sauvegardable.");
        return jobs.Start(ExecutionKind.Restore, "Restauration de " + plan.Users.Count + " profil(s)", start,
            context => WithOperationAsync(context, () => RestoreAsync(plan, context)), cancellationToken);
    }

    private async Task<ExecutionOutcome> WithOperationAsync(ExecutionJobContext context, Func<Task<ExecutionOutcome>> action)
    {
        files.CreatePrivateDirectory(options.StateDirectory);
        using var stateLease = files.HoldDirectory(options.StateDirectory);
        using var operationLock = StorageFileIdentity.OpenLock(Path.Combine(options.StateDirectory, "operation.lock"));
        using var machineLock = WindowsNativeExecutionSafety.AcquireMachineLease(files);
        WindowsNativeExecutionSafety.RequireNoResidualMounts();
        await options.VerifyPackAsync(context.CancellationToken).ConfigureAwait(false);
        return await ExecutionCleanupGuard.RunAsync(action, hives.CleanupAsync).ConfigureAwait(false);
    }

    private void RequireMode(bool demonstration)
    {
        if (demonstration || source.Demonstration)
            throw new FilproOperationException("execution.mode", "Une opération fictive ne peut pas utiliser l'exécutant réel.");
    }
    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        catch (System.Security.SecurityException) { return false; }
    }
    private void RequireStart(bool demonstration, string id, DateTimeOffset created,
        ExecutionStartOptions start, CancellationToken token)
    {
        RequireMode(demonstration); token.ThrowIfCancellationRequested();
        if (WindowsNativeExecutionSafety.BlockReason is { } reason) throw new FilproOperationException("hive.cleanup", reason);
        if (!start.PreparationConfirmed || !start.ManualSpaceAcknowledged)
            throw new FilproOperationException("execution.confirmation", "Confirmer la préparation et la revue de capacité avant de continuer.");
        if (!Guid.TryParseExact(id, "N", out _) || created > clock.GetUtcNow() || clock.GetUtcNow() - created > ExecutionLimits.PlanFreshness)
            throw new FilproOperationException("execution.expired", "Le plan a expiré ; le relire et le confirmer à nouveau.");
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("platform.windows", "L'opération réelle nécessite Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new FilproOperationException("execution.privilege", "Relancer explicitement FILPRO en administrateur avant une opération réelle.");
    }
    private static void MatchStore(RestorePlanReview plan, NativeBackupStore store)
    {
        if (store.Manifest?.MigApp == true || store.V1?.MigApp == true) throw new FilproOperationException("restore.migapp", "MigApp peut modifier des paramètres machine dont le préalable n'est pas couvert. Restauration automatique bloquée ; récupération indépendante de fichiers disponible.");
        if (store.V1 is { } legacy)
        {
            if (legacy.RestoreBlockers.Count > 0) throw new FilproOperationException("restore.compatibility", string.Join(" ", legacy.RestoreBlockers));
            if (legacy.Payload is null || plan.SourcePayloadSha256 is null || legacy.Payload.Sha256 != plan.SourcePayloadSha256)
                throw new FilproOperationException("store.catalogRequired", "Préparer et confirmer le catalogue V1, puis relire le plan : le contenu doit être lié à un SHA-256 mesuré.");
            _ = LegacyRestoreScope.Resolve(store.Inspection.SourceProfiles, legacy.Selection, new(store.Inspection.CatalogComplete, store.Catalog));
        }
        else if (store.Manifest is null) throw new FilproOperationException("restore.compatibility", "Type de magasin non pris en charge.");
        if (!store.Inspection.Store.Ready || store.Inspection.Store.Simulated
            || string.IsNullOrWhiteSpace(plan.SourceManifestSha256) || string.IsNullOrWhiteSpace(plan.SourceStoreId)
            || !store.Inspection.ManifestSha256.Equals(plan.SourceManifestSha256, StringComparison.OrdinalIgnoreCase)
            || store.Inspection.Id != plan.SourceStoreId)
            throw new FilproOperationException("store.changed", "Le magasin n'est pas celui relu et confirmé ; aucune restauration.");
        if (plan.Users.Any(user => !store.Inspection.Store.ManifestUsers!.Contains(user, StringComparer.OrdinalIgnoreCase)))
            throw new FilproOperationException("restore.selection", "Un compte sélectionné est absent des profils exploitables du magasin.");
    }
    private string NewLogDirectory(string id)
    {
        var path = Path.Combine(options.StateDirectory, id);
        files.CreateDirectories(path);
        return path;
    }
    private void WriteReceipt(string path, object receipt) => files.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions), overwrite: true);
    private string? TryWriteReceipt(string path, object receipt)
    {
        try { WriteReceipt(path, receipt); return null; }
        catch (Exception) { return "Le reçu n'a pas pu être publié : " + path + ". L'état partiel et les chemins de secours restent affichés dans cette session."; }
    }
    private async Task<ExecutionProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string logs,
        string name, CancellationToken token, ExecutionJobContext? context = null, string? profileSid = null)
    {
        var progressPath = arguments.FirstOrDefault(a => a.StartsWith("/progress:", StringComparison.Ordinal))?[10..];
        Action<UsmtProgressSnapshot>? report = context is null ? null : progress =>
        {
            var action = progress.Phase switch
            {
                "Initializing" => "Initialisation USMT", "Scanning" => "Analyse USMT", "Collecting" => "Collecte des données",
                "Saving" => "Écriture du magasin", "Estimating" => "Estimation USMT", "Applying" => "Application des données",
                _ => "Opération USMT en cours",
            };
            context.Report(name == "estimate" ? ExecutionState.Preparing : ExecutionState.Running, action, profileSid,
                estimatedRemainingSeconds: progress.MinutesRemaining * 60d, progressPercent: progress.Percent);
        };
        var result = await processes.RunAsync(new(executable, arguments, logs, name, progressPath, report), token).ConfigureAwait(false);
        if (result.OutputTruncated) throw new FilproOperationException("execution.output", "Sortie native incomplète ; aucune réussite n'est déclarée.");
        return result;
    }
    private static void RequireSuccess(ExecutionProcessResult result, string tool, bool allowPartial = false)
    {
        if (result.ExitCode == 0 || (allowPartial && result.ExitCode == 3)) return;
        throw new FilproOperationException("usmt.exit", tool + " a échoué (code " + result.ExitCode + "). Consulter le journal local.");
    }
    private static IReadOnlyList<string> Crypto(bool encrypted, string? keyPath)
    {
        if (!encrypted) return [];
        if (string.IsNullOrWhiteSpace(keyPath)) throw new FilproOperationException("usmt.crypto", "Fichier clé requis.");
        return ["/decrypt:AES_256", "/keyfile:" + StoragePathPolicy.NormalizeWindows(keyPath)];
    }
    private string UsmtVersion()
    {
        files.RequireFile(Path.Combine(options.UsmtRoot, "scanstate.exe"));
        return FileVersionInfo.GetVersionInfo(Path.Combine(options.UsmtRoot, "scanstate.exe")).FileVersion
            ?? throw new FilproOperationException("usmt.version", "Version USMT absente.");
    }
    private static int ClusterBytes(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || !Native.GetDiskFreeSpaceW(root, out var sectors, out var bytes, out _, out _))
            throw new FilproOperationException("storage.space", "Taille de cluster du volume illisible.");
        var cluster = (long)sectors * bytes;
        if (cluster is <= 0 or > int.MaxValue) throw new FilproOperationException("storage.space", "Taille de cluster du volume illisible.");
        return (int)cluster;
    }
    private static void RequireSpace(string destination, long storeBytes, long temporaryBytes, string temporaryRoot)
    {
        if (storeBytes < 0 || temporaryBytes < 0) throw new FilproOperationException("storage.space", "Mesure d'espace invalide.");
        var required = checked(storeBytes + storeBytes / 10 + 64L * 1024 * 1024);
        if (!Native.GetDiskFreeSpaceExW(destination, out var free, out _, out _) || free < (ulong)required)
            throw new FilproOperationException("storage.space", "Espace disponible insuffisant ou illisible sur la destination.");
        var tempRequired = checked(temporaryBytes + 64L * 1024 * 1024);
        if (!Native.GetDiskFreeSpaceExW(temporaryRoot, out var tempFree, out _, out _) || tempFree < (ulong)tempRequired)
            throw new FilproOperationException("storage.space", "Espace temporaire insuffisant ou illisible.");
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceExW(string path, out ulong available, out ulong total, out ulong free);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
    }
}
