using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class ExecutionHistoryTests
{
    private static ExecutionJobSnapshot Snapshot(ExecutionState state) => new(Guid.NewGuid().ToString("N"),
        ExecutionKind.Backup, "Sauvegarde fictive", state, "Action fictive", null, null,
        DateTimeOffset.UtcNow, null, false, null, null, null, null, [], null);

    [Test] public static void BilanPersistantEtIssueNonConfirmeeApresReouverture()
    {
        using var fixture = new PackTestFixture(); var directory = Path.Combine(fixture.Root, "Operations");
        var store = new ExecutionHistoryStore(directory); var pending = Snapshot(ExecutionState.Pending);
        store.Save(pending);
        var reopened = new ExecutionHistoryStore(directory);
        var unknown = reopened.Merge([]).Single();
        Check.Equal(ExecutionState.Partial, unknown.State, "aucune réussite ou annulation supposée après coupure");
        Check.Equal("history.unconfirmed", unknown.Error!.Code, "issue inconnue explicite");
        Check.True(unknown.FinishedAt is null, "aucune date de fin inventée");
        Check.Equal(ExecutionState.Pending, reopened.Merge([pending]).Single().State, "l'opération possédée garde son vrai état");
        var final = pending with { State = ExecutionState.Succeeded, FinishedAt = DateTimeOffset.UtcNow, ResultPath = "resultat-fictif" };
        store.Save(final);
        var saved = new ExecutionHistoryStore(directory).Merge([]).Single();
        Check.Equal(ExecutionState.Succeeded, saved.State, "bilan final relu");
        Check.Equal("resultat-fictif", saved.ResultPath, "résultat conservé");
    }

    [Test] public static void DemonstrationEtJournalCorrompuRefusesSansEffacement()
    {
        using var fixture = new PackTestFixture(); var directory = Path.Combine(fixture.Root, "Operations");
        var store = new ExecutionHistoryStore(directory); var snapshot = Snapshot(ExecutionState.Pending);
        Check.Throws<FilproOperationException>(() => store.Save(snapshot with { Demonstration = true }), "isolation démo");
        Check.False(Directory.Exists(directory), "aucune écriture en démo");
        store.Save(snapshot);
        var path = Path.Combine(directory, "History", snapshot.Id + ".json"); File.WriteAllText(path, "{corrompu");
        Check.Throws<FilproOperationException>(() => new ExecutionHistoryStore(directory).Merge([]), "corruption visible");
        Check.Equal("{corrompu", File.ReadAllText(path), "preuve conservée");
    }

    [Test] public static void EchecDuJournalInitialInterditLeTravail()
    {
        var called = false;
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System, _ => throw new IOException("disque fictif plein"));
        Check.Throws<IOException>(() => coordinator.Start(ExecutionKind.Backup, "Fictif", new(true), _ =>
        { called = true; return Task.FromResult(new ExecutionOutcome(ExecutionState.Succeeded, [])); }, default), "refus initial");
        Check.False(called, "aucune mutation après échec de journal");
        Check.Equal(0, coordinator.List().Count, "aucune tâche démarrée");
    }

    [Test] public static async Task EchecDuBilanFinalNeDevientPasUnSuccesDurable()
    {
        var finished = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System, _ =>
        { if (++writes == 2) throw new IOException("disque fictif plein"); });
        coordinator.Changed += snapshot => { if (ExecutionJobCoordinator.IsTerminal(snapshot.State)) finished.TrySetResult(snapshot); };
        _ = coordinator.Start(ExecutionKind.Backup, "Fictif", new(true), _ =>
            Task.FromResult(new ExecutionOutcome(ExecutionState.Succeeded, [], "resultat-fictif")), default);
        var final = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check.Equal(ExecutionState.Partial, final.State, "échec de bilan visible");
        Check.Equal("resultat-fictif", final.ResultPath, "résultat métier conservé");
        Check.True(final.Warnings.Count > 0, "avertissement durable");
    }
}
