using Filpro.Core;

namespace Filpro.Tests;

internal static class ExecutionCoordinatorTests
{
    [Test]
    public static void ConfirmationRefuseAvantToutTravail()
    {
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System);
        var called = false;
        var error = Check.Throws<FilproOperationException>(() => coordinator.Start(ExecutionKind.Backup, "Test", new(false),
            _ => { called = true; return Task.FromResult(new ExecutionOutcome(ExecutionState.Succeeded, [])); }, default), "Confirmation exigée");
        Check.Equal("execution.confirmation", error.Code, "Code de refus");
        Check.False(called, "Le travail n'a jamais commencé");
        Check.Equal(0, coordinator.List().Count, "Aucune opération inventée");
    }

    [Test]
    public static async Task AnnulationAttendVraimentLeFinally()
    {
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = coordinator.Start(ExecutionKind.Backup, "Annulation", new(true), async context =>
        {
            context.Report(ExecutionState.Running, "Travail", progressPercent: 27, estimatedRemainingSeconds: 12);
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
            finally { cleanupStarted.SetResult(); await releaseCleanup.Task; }
            return new(ExecutionState.Succeeded, []);
        }, default);
        await started.Task;
        var cancelled = coordinator.CancelAsync(job.Id, default);
        await cleanupStarted.Task;
        Check.False(cancelled.IsCompleted, "L'arrêt ne finit pas avant le nettoyage");
        var pending = coordinator.Get(job.Id)!;
        Check.True(pending.CancellationRequested, "Demande visible");
        Check.False(ExecutionJobCoordinator.IsTerminal(pending.State), "Pas d'état terminal prématuré");
        releaseCleanup.SetResult();
        Check.Equal(ExecutionState.Cancelled, (await cancelled).State, "Annulation seulement après finally");
    }

    [Test]
    public static async Task EchecDuNettoyageNeDevientPasUneAnnulationReussie()
    {
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = coordinator.Start(ExecutionKind.Restore, "Nettoyage", new(true), async context =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
            finally { throw new FilproOperationException("hive.cleanup", "Libération non confirmée."); }
        }, default);
        await started.Task;
        var result = await coordinator.CancelAsync(job.Id, default);
        Check.Equal(ExecutionState.Failed, result.State, "Échec de nettoyage conservé");
        Check.Equal("hive.cleanup", result.Error!.Code, "Cause conservée");
    }

    [Test]
    public static async Task OperationConcurrenteRefuseeEtRecuPartielConserve()
    {
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Changed += state => { if (ExecutionJobCoordinator.IsTerminal(state.State)) finished.TrySetResult(state); };
        _ = coordinator.Start(ExecutionKind.Restore, "Partiel", new(true), async _ =>
        { await release.Task; throw new ExecutionPartialException("restore.partial", "Déjà écrit.", "recu-local", ["Sauvegarde conservée"]); }, default);
        Check.Throws<FilproOperationException>(() => coordinator.Start(ExecutionKind.Backup, "Concurrent", new(true),
            _ => Task.FromResult(new ExecutionOutcome(ExecutionState.Succeeded, [])), default), "Concurrence refusée");
        release.SetResult();
        var result = await finished.Task;
        Check.Equal(ExecutionState.Partial, result.State, "Écritures possibles = partiel");
        Check.Equal("recu-local", result.ResultPath, "Reçu de reprise accessible");
        Check.Equal("restore.partial", result.Error!.Code, "Cause détaillée");
    }

    [Test]
    public static async Task EchecInattenduResteStableEtPasseAuJournal()
    {
        var journal = new JournalCapture();
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System, journal: journal);
        var finished = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Changed += state => { if (ExecutionJobCoordinator.IsTerminal(state.State)) finished.TrySetResult(state); };
        coordinator.Start(ExecutionKind.Backup, "Inattendu", new(true),
            _ => throw new IOException("detail technique"), default);
        var result = await finished.Task;
        Check.Equal(ExecutionState.Failed, result.State, "Échec conservé");
        Check.Equal("execution.failed", result.Error!.Code, "Code stable");
        Check.Equal("L’opération a échoué ; les journaux locaux conservent le diagnostic.", result.Error.Message, "Message opérateur stable");
        Check.Equal(1, journal.Errors.Count, "Un seul enregistrement");
        Check.Equal("execution.failed", journal.Errors[0].Message, "Code journalisé");
        Check.True(journal.Errors[0].Exception is IOException, "Exception d'origine journalisée");
    }

    private sealed class JournalCapture : ITechnicalJournal
    {
        public List<(string Message, Exception Exception)> Errors { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception exception) => Errors.Add((message, exception));
    }
}
