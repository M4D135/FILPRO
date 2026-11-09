using Filpro.Core;

namespace Filpro.Bridge;

internal static class ExecutionBridge
{
    internal static IFilproExecutionService Require(EngineContext context, bool? demonstration = null)
    {
        var expected = context.Mode == AppMode.Simulation;
        var service = context.Require<IFilproExecutionService>();
        if (service.Demonstration != expected || (demonstration.HasValue && demonstration.Value != expected))
            throw new FilproOperationException("execution.mode", "Le fournisseur d’opérations ne correspond pas au mode de cette session.");
        return service;
    }

    internal static ConfirmedPlanStore Plans(EngineContext context) => context.Shared(() => new ConfirmedPlanStore(context.Clock));
    internal static ExecutionStartOptions ConfirmedOptions(bool capacity = true) => new(true, capacity);

    /// <summary>
    /// Refuse avant la confirmation Windows un plan qui atteindrait, la fenêtre encore ouverte, l'âge que le moteur refuse au
    /// démarrage (<see cref="ExecutionLimits.PlanFreshness"/>). Le plan déjà pris est perdu, comme après un refus.
    /// </summary>
    internal static void RequirePlanTimeLeft(NativeConfirmationTurn turn, DateTimeOffset createdUtc) =>
        turn.RequireTimeLeft(createdUtc + ExecutionLimits.PlanFreshness, "execution.expired",
            "Le plan arrive à expiration : le relire et le confirmer à nouveau.");
}
