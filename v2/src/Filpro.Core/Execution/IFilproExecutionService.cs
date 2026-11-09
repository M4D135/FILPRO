namespace Filpro.Core;

/// <summary>
/// Contrat fermé de l'exécutant. La composition choisit l'instance native ou la simulation ; aucun argument navigateur
/// ne choisit ce mode. Les opérations de démarrage reçoivent uniquement les plans relus et conservés côté moteur.
/// </summary>
public interface IFilproExecutionService
{
    bool Demonstration { get; }
    ExecutionCapabilities Capabilities { get; }
    event Action<ExecutionJobSnapshot>? Changed;
    IReadOnlyList<ExecutionJobSnapshot> List();
    /// <summary>Travaux possédés par cette instance ; indépendant de la lecture de l'historique sur disque.</summary>
    IReadOnlyList<ExecutionJobSnapshot> Active() => List().Where(job => !ExecutionJobCoordinator.IsTerminal(job.State)).ToArray();
    ExecutionJobSnapshot? Get(string id);
    Task<ExecutionStoreInspection> InspectStoreAsync(string bundle, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartCatalogAsync(StoreCatalogRequest request, ExecutionStartOptions options, CancellationToken cancellationToken);
    Task<RecoveryPlanReview> CreateRecoveryPlanAsync(RecoveryPlanRequest request, CancellationToken cancellationToken);
    Task<RepairPlanReview> CreateRepairPlanAsync(RepairPlanRequest request, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartBackupAsync(BackupPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartRestoreAsync(RestorePlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartRecoveryAsync(RecoveryPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartRepairAsync(RepairPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken);
    /// <summary>Relit et vérifie le reçu comme le retour arrière le fera, pour la confirmation Windows ; rien n'est lancé.</summary>
    Task<RepairRollbackReview> ReviewRepairRollbackAsync(RepairRollbackRequest request, CancellationToken cancellationToken);
    Task<ExecutionJobSnapshot> StartRepairRollbackAsync(RepairRollbackRequest request, ExecutionStartOptions options, CancellationToken cancellationToken);
    /// <summary>Attend le travail et ses finally. L'arrêt demandé n'est pas présenté comme un arrêt achevé.</summary>
    Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken);
    /// <summary>Interdit tout nouveau travail, annule les opérations en cours puis attend leurs nettoyages.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
