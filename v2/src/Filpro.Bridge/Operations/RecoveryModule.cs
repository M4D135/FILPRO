using Filpro.Core;
using System.Text.Json;

namespace Filpro.Bridge;

public sealed class RecoveryModule : IOperationModule
{
    public string Screen => "recuperer";
    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("recuperer.catalog", OperationClass.Write, async (args, _, ct) =>
        {
            var (request, options) = ReadCatalogConfirmation(args);
            var service = ExecutionBridge.Require(context, demonstration: false);
            using var turn = NativeConfirmation.Begin(context);
            using var admission = context.Operations.EnterNativeStart();
            // Le manifeste montré dans la fenêtre est celui que le démarrage exige : un magasin changé depuis est refusé.
            var inspection = await service.InspectStoreAsync(request.Bundle, ct).ConfigureAwait(false);
            if (!inspection.Store.Ready) throw new FilproOperationException("store.state", "Le magasin n'est pas exploitable.");
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.Catalog(inspection, request.KeyPath),
                () => service.StartCatalogAsync(request with { ExpectedManifestSha256 = inspection.ManifestSha256 }, options, ct), ct).ConfigureAwait(false);
        }, "Après confirmation, vérifie le magasin et prépare son catalogue ; aucune restauration ni copie dans un profil.");
        yield return new("recuperer.plan", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["bundle", "destination", "entryIds", "keyPath"]);
            var plan = await ExecutionBridge.Require(context).CreateRecoveryPlanAsync(new(
                BridgeArguments.GetString(values, "bundle"), BridgeArguments.GetString(values, "destination"),
                BridgeArguments.GetStringArray(values, "entryIds", 500), BridgeArguments.GetOptionalString(values, "keyPath")), ct).ConfigureAwait(false);
            ExecutionBridge.Plans(context).Add(plan.Id, plan);
            return plan;
        }, "Aperçu des fichiers à récupérer et de leur destination, lié au manifeste relu.", ServedInSimulation: true);
        yield return new("recuperer.confirmPlan", OperationClass.Write, async (args, _, ct) =>
        {
            var (id, options) = RepairOperationsModule.ReadConfirmation(args);
            var service = ExecutionBridge.Require(context, demonstration: false);
            using var turn = NativeConfirmation.Begin(context);
            using var admission = context.Operations.EnterNativeStart();
            var plan = ExecutionBridge.Plans(context).Take<RecoveryPlanReview>(id);
            ExecutionBridge.RequirePlanTimeLeft(turn, plan.CreatedUtc);
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.Recovery(plan),
                () => service.StartRecoveryAsync(plan, options, ct), ct).ConfigureAwait(false);
        }, "Récupère uniquement les entrées revues et confirmées, sans écraser les données courantes.");
    }

    internal static (StoreCatalogRequest Request, ExecutionStartOptions Options) ReadCatalogConfirmation(JsonElement args)
    {
        var values = BridgeArguments.ReadObject(args, ["bundle", "keyPath", "confirmed", "spaceAcknowledged"]);
        if (!BridgeArguments.GetBoolean(values, "confirmed") || !BridgeArguments.GetBoolean(values, "spaceAcknowledged"))
            throw BridgeArguments.Invalid("confirmer la préparation longue du catalogue et sa capacité temporaire.");
        return (new(BridgeArguments.GetString(values, "bundle"), BridgeArguments.GetOptionalString(values, "keyPath")),
            ExecutionBridge.ConfirmedOptions());
    }
}
