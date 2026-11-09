using System.Runtime.ExceptionServices;

namespace Filpro.Core;

/// <summary>Attend le nettoyage, sans effacer un résultat partiel ni ses chemins quand le nettoyage échoue à son tour.</summary>
public static class ExecutionCleanupGuard
{
    public static async Task<ExecutionOutcome> RunAsync(Func<Task<ExecutionOutcome>> action, Func<Task> cleanup)
    {
        ExecutionOutcome? outcome = null;
        Exception? failure = null;
        try { outcome = await action().ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        try { await cleanup().ConfigureAwait(false); }
        catch (Exception cleanupError)
        {
            var warning = "Le nettoyage des ruches n'est pas terminé ; les opérations natives restent bloquées. " + cleanupError.Message;
            if (failure is ExecutionPartialException partial)
                throw new ExecutionPartialException(partial.Code, partial.Message + " Le nettoyage a également échoué.",
                    partial.ResultPath, [.. partial.Warnings, warning]);
            if (outcome is not null)
                throw new ExecutionPartialException("hive.cleanup", "L'opération a produit un résultat, mais son nettoyage n'est pas terminé.",
                    outcome.ResultPath, [.. outcome.Warnings, warning]);
            throw new FilproOperationException("hive.cleanup", warning
                + (failure is null ? "" : " L'opération était déjà interrompue : " + failure.Message));
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return outcome!;
    }
}
