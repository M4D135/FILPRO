using Filpro.Core;

namespace Filpro.Bridge;

public sealed class RepairOperationsModule : IOperationModule
{
    public string Screen => "analyser";
    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("analyser.repairPlan", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["sid", "safetyBackupRoot"]);
            var plan = await ExecutionBridge.Require(context).CreateRepairPlanAsync(new(
                BridgeArguments.GetString(values, "sid"), BridgeArguments.GetString(values, "safetyBackupRoot")), ct).ConfigureAwait(false);
            ExecutionBridge.Plans(context).Add(plan.Id, plan);
            return plan;
        }, "Aperçu de réparation avec conservation du profil et sauvegarde préalable, sans préparation longue.", ServedInSimulation: true);
        yield return new("analyser.confirmRepair", OperationClass.Write, async (args, _, ct) =>
        {
            var (id, options) = ReadConfirmation(args);
            var service = ExecutionBridge.Require(context, demonstration: false);
            using var turn = NativeConfirmation.Begin(context);
            using var admission = context.Operations.EnterNativeStart();
            var plan = ExecutionBridge.Plans(context).Take<RepairPlanReview>(id);
            ExecutionBridge.RequirePlanTimeLeft(turn, plan.CreatedUtc);
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.Repair(plan),
                () => service.StartRepairAsync(plan, options, ct), ct).ConfigureAwait(false);
        }, "Répare uniquement le profil revu et confirmé, après les contrôles et la sauvegarde préalable du moteur.");
        yield return new("analyser.rollbackRepair", OperationClass.Write, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["receiptPath", "confirmed"]);
            if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer le retour arrière de la réparation.");
            var receiptPath = BridgeArguments.GetString(values, "receiptPath");
            var service = ExecutionBridge.Require(context, demonstration: false);
            using var turn = NativeConfirmation.Begin(context);
            using var admission = context.Operations.EnterNativeStart();
            // Le reçu est relu et vérifié avant toute fenêtre ; le démarrage refuse un reçu dont l'empreinte a changé depuis.
            var review = await service.ReviewRepairRollbackAsync(new(receiptPath), ct).ConfigureAwait(false);
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.RepairRollback(review),
                () => service.StartRepairRollbackAsync(new(receiptPath, review.ReceiptSha256), ExecutionBridge.ConfirmedOptions(), ct), ct).ConfigureAwait(false);
        }, "Retour arrière demandé explicitement depuis un reçu de réparation validé.");
    }

    internal static (string Id, ExecutionStartOptions Options) ReadConfirmation(System.Text.Json.JsonElement args)
    {
        var values = BridgeArguments.ReadObject(args, ["planId", "confirmed", "acknowledgeCapacity"]);
        if (!BridgeArguments.GetBoolean(values, "confirmed") || !BridgeArguments.GetBoolean(values, "acknowledgeCapacity"))
            throw BridgeArguments.Invalid("confirmer la préparation et le contrôle de capacité avant l’opération.");
        return (BridgeArguments.GetString(values, "planId"), ExecutionBridge.ConfirmedOptions());
    }
}
