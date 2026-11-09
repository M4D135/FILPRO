using Filpro.Core;

namespace Filpro.Bridge;

public sealed class OperationsModule : IOperationModule
{
    public string Screen => "operations";
    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("operations.list", OperationClass.LocalRead, (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args); ct.ThrowIfCancellationRequested();
            return Task.FromResult<object?>(ExecutionBridge.Require(context).List());
        }, "Opérations de la session courante, sans lecture d’autres profils.");
        yield return new("operations.get", OperationClass.LocalRead, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["id"]); ct.ThrowIfCancellationRequested();
            return Task.FromResult<object?>(ExecutionBridge.Require(context).Get(BridgeArguments.GetString(values, "id"))
                ?? throw new FilproOperationException("operation.unknown", "Opération inconnue dans cette session."));
        }, "État actuel d’une opération connue.");
        yield return new("operations.cancel", OperationClass.Local, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["id", "confirmed"]);
            if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer l’annulation.");
            return await ExecutionBridge.Require(context).CancelAsync(BridgeArguments.GetString(values, "id"), ct).ConfigureAwait(false);
        }, "Demande l’arrêt puis attend la fermeture des ressources ; une demande d’arrêt n’est pas une annulation achevée.");
    }
}
