using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>Commandes fictives séparées des Write natives ; le mode n'est jamais choisi par un argument de la page.</summary>
public sealed class DemonstrationModule : IOperationModule
{
    public string Screen => "demonstration";
    private static void RequireMode(EngineContext context) => _ = ExecutionBridge.Require(context, demonstration: true);
    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("demonstration.catalog", OperationClass.Local, (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args); RequireMode(context);
            return Task.FromResult<object?>(context.Require<IFilproDemonstrationSession>().ReadCatalog(ct));
        }, "Catalogue fermé de données fictives, sans accès au poste.");
        yield return new("demonstration.catalogStore", OperationClass.Local, async (args, _, ct) =>
        {
            var (request, options) = RecoveryModule.ReadCatalogConfirmation(args);
            return await ExecutionBridge.Require(context, demonstration: true).StartCatalogAsync(request, options, ct).ConfigureAwait(false);
        }, "Préparation fictive du catalogue d’un magasin du monde isolé ; aucun accès réel.");
        yield return new("demonstration.scenario", OperationClass.Local, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["outcome"]); RequireMode(context);
            return Task.FromResult<object?>(context.Require<IFilproDemonstrationSession>().SetScenario(BridgeArguments.GetString(values, "outcome"), ct));
        }, "Issue fictive de la prochaine opération de démonstration.");
        foreach (var kind in new[] { "backup", "repair", "recover" })
        {
            yield return new("demonstration." + kind, OperationClass.Local, async (args, _, ct) =>
            {
                var (id, options) = RepairOperationsModule.ReadConfirmation(args);
                var service = ExecutionBridge.Require(context, demonstration: true);
                return kind switch
                {
                    "backup" => await service.StartBackupAsync(ExecutionBridge.Plans(context).Take<BackupPlanReview>(id), options, ct).ConfigureAwait(false),
                    "repair" => await service.StartRepairAsync(ExecutionBridge.Plans(context).Take<RepairPlanReview>(id), options, ct).ConfigureAwait(false),
                    _ => await service.StartRecoveryAsync(ExecutionBridge.Plans(context).Take<RecoveryPlanReview>(id), options, ct).ConfigureAwait(false),
                };
            }, "Exécution fictive du plan confirmé, sur le seul fournisseur de démonstration.");
        }
        yield return new("demonstration.restore", OperationClass.Local, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["planId", "confirmed", "acknowledgeCapacity", "acknowledgePriorBackup", "acknowledgeIdentityReview"]);
            foreach (var key in new[] { "confirmed", "acknowledgeCapacity", "acknowledgePriorBackup", "acknowledgeIdentityReview" })
                if (!BridgeArguments.GetBoolean(values, key)) throw BridgeArguments.Invalid("confirmer les protections de la restauration fictive.");
            var service = ExecutionBridge.Require(context, demonstration: true);
            return await service.StartRestoreAsync(ExecutionBridge.Plans(context).Take<RestorePlanReview>(BridgeArguments.GetString(values, "planId")),
                ExecutionBridge.ConfirmedOptions(), ct).ConfigureAwait(false);
        }, "Restauration fictive avec étapes et résultats, sans appel au moteur Windows.");
        yield return new("demonstration.repairRollback", OperationClass.Local, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["receiptPath", "confirmed"]);
            if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer le retour arrière fictif.");
            return await ExecutionBridge.Require(context, demonstration: true).StartRepairRollbackAsync(
                new(BridgeArguments.GetString(values, "receiptPath")), ExecutionBridge.ConfirmedOptions(), ct).ConfigureAwait(false);
        }, "Retour arrière fictif à partir d’un reçu du monde de démonstration.");
        foreach (var rollback in new[] { false, true })
        {
            yield return new(rollback ? "demonstration.updateRollback" : "demonstration.updateInstall", OperationClass.Local, async (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["transactionId", "confirmed"]);
                RequireMode(context); UpdatesModule.RequireIdle(context);
                var service = UpdatesModule.Service(context, simulation: true);
                return rollback
                    ? await service.RollbackAsync(BridgeArguments.GetString(values, "transactionId"), BridgeArguments.GetBoolean(values, "confirmed"), ct).ConfigureAwait(false)
                    : await service.InstallAsync(BridgeArguments.GetString(values, "transactionId"), BridgeArguments.GetBoolean(values, "confirmed"), ct).ConfigureAwait(false);
            }, "Mise à jour ou retour arrière simulé exclusivement en mémoire.");
        }
    }
}
