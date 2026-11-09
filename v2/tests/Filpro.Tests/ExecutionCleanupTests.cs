using Filpro.Core;

namespace Filpro.Tests;

internal static class ExecutionCleanupTests
{
    [Test]
    public static async Task NettoyageEnEchecNeMasquePasPartielNiSauvegardePrealable()
    {
        var error = await Check.ThrowsAsync<ExecutionPartialException>(() => ExecutionCleanupGuard.RunAsync(
            () => Task.FromException<ExecutionOutcome>(new ExecutionPartialException("restore.partial", "Premier profil appliqué", @"D:\Receipts\reprise.json", [@"Sauvegarde préalable : E:\Prior"])),
            () => Task.FromException(new FilproOperationException("hive.cleanup", "Ruche privée encore possédée"))), "Le statut partiel subsiste");
        Check.Equal("restore.partial", error.Code, "Cause de l'écriture partielle conservée");
        Check.Equal(@"D:\Receipts\reprise.json", error.ResultPath, "Reçu de reprise conservé");
        Check.True(error.Warnings.Contains(@"Sauvegarde préalable : E:\Prior"), "Chemin de sauvegarde préalable conservé");
        Check.True(error.Warnings.Any(w => w.Contains("Ruche privée encore possédée", StringComparison.Ordinal)), "Diagnostic de nettoyage ajouté");
    }

    [Test]
    public static async Task SuccesAttendNettoyageEtDevientPartielSiCeluiCiEchoue()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = ExecutionCleanupGuard.RunAsync(() => Task.FromResult(new ExecutionOutcome(ExecutionState.Succeeded, ["Données conservées"], @"E:\Backup")), () => cleanup.Task);
        Check.False(run.IsCompleted, "Aucun état terminal avant la fin du nettoyage");
        cleanup.SetException(new IOException("unload indisponible"));
        var error = await Check.ThrowsAsync<ExecutionPartialException>(() => run, "Aucune fausse réussite");
        Check.Equal(@"E:\Backup", error.ResultPath, "Résultat réel conservé");
        Check.Equal("hive.cleanup", error.Code, "Nettoyage visible");
    }

    [Test]
    public static async Task AnnulationResteAnnulationSeulementApresNettoyageReussi()
    {
        var cleaned = false;
        await Check.ThrowsAsync<OperationCanceledException>(() => ExecutionCleanupGuard.RunAsync(
            () => Task.FromException<ExecutionOutcome>(new OperationCanceledException()), () => { cleaned = true; return Task.CompletedTask; }), "Annulation propagée après nettoyage");
        Check.True(cleaned, "Nettoyage exécuté malgré l'annulation");
        var error = await Check.ThrowsAsync<FilproOperationException>(() => ExecutionCleanupGuard.RunAsync(
            () => Task.FromException<ExecutionOutcome>(new OperationCanceledException()), () => Task.FromException(new IOException("unload refusé"))), "Un nettoyage raté ne donne pas une annulation achevée");
        Check.Equal("hive.cleanup", error.Code, "Échec honnête du nettoyage");
    }
}
