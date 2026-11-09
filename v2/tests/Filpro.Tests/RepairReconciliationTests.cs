using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Décisions sur preuves fictives et diagnostic du coordinateur ; aucune mutation de profil ou de registre Windows.</summary>
internal static class RepairReconciliationTests
{
    private static readonly string Original = new('a', 64);
    private static readonly string Replacement = new('b', 64);
    private static readonly string Foreign = new('c', 64);
    private static RepairObservedState Pending => new("RollbackPending", Original, null, Original, null, null, RepairRegistryIdentity.Original);

    [Test] public static void RenamePendingAvantRenommageNeDeplaceRien()
    {
        var state = Pending with { Stage = "RenamePending", CurrentHash = Original, PreservedOriginalHash = null };
        Check.Equal(RepairRollbackStep.Complete, RepairReconciliation.Next(state), "Le journal seul ne prouve pas un renommage");
    }

    [Test] public static void RenamePendingApresRenommageRendLeDossierSansSupprimerLaCle()
    {
        Check.Equal(RepairRollbackStep.RestoreOriginalDirectory, RepairReconciliation.Next(Pending with { Stage = "RenamePending" }),
            "L'entrée originale exacte est conservée pendant le retour du dossier");
    }

    [Test] public static void DetachPendingApresSuppressionReconstitueDossierPuisCle()
    {
        var state = Pending with { Stage = "DetachPending", Registry = RepairRegistryIdentity.Absent };
        Check.Equal(RepairRollbackStep.RestoreOriginalDirectory, RepairReconciliation.Next(state), "Dossier d'abord");
        state = state with { CurrentHash = Original, PreservedOriginalHash = null };
        Check.Equal(RepairRollbackStep.RestoreOriginalKey, RepairReconciliation.Next(state), "Clé seulement après dossier identifié");
        Check.Equal(RepairRollbackStep.Complete, RepairReconciliation.Next(state with { Registry = RepairRegistryIdentity.Original }), "Preuves de fin réelles");
    }

    [Test] public static void NouveauProfilExigeSauvegardesAvantPremierRenommage()
    {
        var state = Pending with { Stage = "AwaitingNewProfile", CurrentHash = Replacement, Registry = RepairRegistryIdentity.Unrecognized };
        Check.Equal(RepairRollbackStep.CaptureReplacement, RepairReconciliation.Next(state), "Capturer ruche et ProfileList, puis sceller leurs références");
    }

    [Test] public static void RollbackPendingArchiveLeNouveauProfilAvantSaCle()
    {
        var state = Pending with { CurrentHash = Replacement, ReplacementExpectedHash = Replacement, Registry = RepairRegistryIdentity.Replacement };
        Check.Equal(RepairRollbackStep.PreserveReplacement, RepairReconciliation.Next(state), "Conserver le dossier avant tout détachement");
        state = state with { CurrentHash = null, PreservedReplacementHash = Replacement };
        Check.Equal(RepairRollbackStep.RemoveReplacementKey, RepairReconciliation.Next(state), "Le dossier conservé est identifié");
    }

    [Test] public static void CoupureApresArchivageEtSuppressionReprendLeProfilOriginal()
    {
        var state = Pending with { ReplacementExpectedHash = Replacement, PreservedReplacementHash = Replacement, Registry = RepairRegistryIdentity.Absent };
        Check.Equal(RepairRollbackStep.RestoreOriginalDirectory, RepairReconciliation.Next(state), "Reprise après conservation du nouveau profil");
        state = state with { CurrentHash = Original, PreservedOriginalHash = null };
        Check.Equal(RepairRollbackStep.RestoreOriginalKey, RepairReconciliation.Next(state), "Le .old déjà revenu n'est plus exigé à son ancien chemin");
    }

    [Test] public static void DossierDejaRevenuEtCleDuNouveauProfilRestanteSontReconcilies()
    {
        var state = Pending with { CurrentHash = Original, PreservedOriginalHash = null, ReplacementExpectedHash = Replacement,
            PreservedReplacementHash = Replacement, Registry = RepairRegistryIdentity.Replacement };
        Check.Equal(RepairRollbackStep.RemoveReplacementKey, RepairReconciliation.Next(state), "Seule la clé sauvegardée peut être retirée");
        Check.Equal(RepairRollbackStep.RestoreOriginalKey, RepairReconciliation.Next(state with { Registry = RepairRegistryIdentity.Absent }), "Pas de nouveau renommage");
    }

    [Test] public static void RetourAcheveEstIdempotentUniquementAvecLesPreuvesAttendues()
    {
        foreach (var stage in new[] { "RollbackPending", "RolledBack", "PreparationRolledBack" })
        {
            var state = Pending with { Stage = stage, CurrentHash = Original, PreservedOriginalHash = null,
                ReplacementExpectedHash = Replacement, PreservedReplacementHash = Replacement };
            Check.Equal(RepairRollbackStep.Complete, RepairReconciliation.Next(state), "Aucune répétition de suppression ou renommage");
        }
    }

    [Test] public static void RecuTermineIncoherentNeRelanceAucuneMutation()
    {
        foreach (var stage in new[] { "RolledBack", "PreparationRolledBack" })
        {
            Refused(Pending with { Stage = stage }, "Dossier encore déplacé malgré le reçu terminé");
            Refused(Pending with { Stage = stage, CurrentHash = Original, PreservedOriginalHash = null, Registry = RepairRegistryIdentity.Absent }, "Clé absente malgré le reçu terminé");
        }
    }

    [Test] public static void OriginalModifieDupliqueOuAbsentRefuseLeRetourAutomatique()
    {
        Refused(Pending with { PreservedOriginalHash = Foreign }, "Ruche du .old modifiée");
        Refused(Pending with { CurrentHash = Original }, "Deux copies revendiquent l'original");
        Refused(Pending with { PreservedOriginalHash = null }, "Original absent des deux chemins");
        Refused(Pending with { PreservedOriginalHash = null, CurrentHash = Foreign }, "Dossier courant étranger");
    }

    [Test] public static void NouveauProfilModifieOuDupliqueNeDevientPasUneSourceConnue()
    {
        var state = Pending with { ReplacementExpectedHash = Replacement, Registry = RepairRegistryIdentity.Replacement };
        Refused(state with { CurrentHash = Foreign }, "Le nouveau profil a changé après sa sauvegarde");
        Refused(state with { CurrentHash = Replacement, PreservedReplacementHash = Replacement }, "Deux nouveaux profils identiques : aucune destination choisie implicitement");
        Refused(state with { PreservedReplacementHash = Foreign }, "Nouveau dossier conservé modifié");
    }

    [Test] public static void NouveauProfilEnregistreNePeutPasDisparaitreSilencieusement()
    {
        Refused(Pending with { ReplacementExpectedHash = Replacement }, "Nouveau profil absent des deux emplacements");
        Refused(Pending with { CurrentHash = Original, PreservedOriginalHash = null, ReplacementExpectedHash = Replacement }, "Un original revenu ne masque pas la disparition du nouveau profil");
    }

    [Test] public static void CleEtrangereNePeutPasEtreSupprimee()
    {
        Refused(Pending with { Registry = RepairRegistryIdentity.Unrecognized }, "Clé inconnue, cible libre");
        Refused(Pending with { CurrentHash = Original, PreservedOriginalHash = null, Registry = RepairRegistryIdentity.Unrecognized }, "Clé inconnue, original revenu");
        Refused(Pending with { CurrentHash = Replacement, ReplacementExpectedHash = Replacement, Registry = RepairRegistryIdentity.Unrecognized }, "Clé changée après sauvegarde du nouveau profil");
    }

    [Test] public static void DossierOccupeSansProfileListNePermetPasUneCaptureImplicite()
    {
        Refused(Pending with { CurrentHash = Replacement, Registry = RepairRegistryIdentity.Absent }, "Dossier sans identité de compte prouvée");
    }

    [Test] public static void EtatOuMetadonneesIncoherentsSontRefuses()
    {
        Refused(Pending with { Stage = "SuccesSuppose" }, "État inconnu");
        Refused(Pending with { PreservedReplacementHash = Replacement }, "Dossier nouveau sans empreinte scellée");
        Refused(Pending with { Registry = RepairRegistryIdentity.Replacement }, "Clé nouvelle sans snapshot scellé");
        Refused(Pending with { OriginalExpectedHash = "abc" }, "Empreinte mal formée");
        Refused(Pending with { ReplacementExpectedHash = Original, CurrentHash = Replacement }, "Identités de ruche non distinctes");
    }

    [Test] public static void CasseDesEmpreintesNeChangePasIdentiteEtatsMutants()
    {
        Check.Equal(RepairRollbackStep.RestoreOriginalDirectory,
            RepairReconciliation.Next(Pending with { PreservedOriginalHash = Original.ToUpperInvariant() }), "Empreinte hexadécimale équivalente");
        foreach (var stage in new[] { "RenamePending", "DetachPending", "RollbackPending", "RollbackPartial" })
            Check.True(RepairReconciliation.MayHaveChanged(stage), "Une coupure dans un état intentionnel garde la possibilité de mutation");
        Check.False(RepairReconciliation.MayHaveChanged("BackupsReady"), "Une sauvegarde prête ne prouve pas un renommage");
    }

    [Test] public static async Task JournalPleinConserveStatutPartielEtCheminsDansLeCoordinateur()
    {
        var coordinator = new ExecutionJobCoordinator(false, TimeProvider.System);
        var finished = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Changed += state => { if (ExecutionJobCoordinator.IsTerminal(state.State)) finished.TrySetResult(state); };
        var attempts = 0;
        _ = coordinator.Start(ExecutionKind.Rollback, "Reprise fictive", new(true), _ =>
            throw RepairReconciliation.Partial("repair.rollbackPartial", "Dossier déjà remis, reçu non confirmé.", "recu-fictif",
                ["Original : dossier-original-fictif", "Nouveau conservé : dossier-nouveau-fictif", "Sauvegarde : sauvegarde-fictive"],
                () => { attempts++; throw new IOException("Support fictif plein."); }), default);
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check.Equal(1, attempts, "Une tentative de journal, aucun rejeu infini");
        Check.Equal(ExecutionState.Partial, result.State, "La panne du journal n'efface pas les écritures possibles");
        Check.Equal("recu-fictif", result.ResultPath, "Chemin du reçu restant, sans affirmer sa mise à jour");
        Check.Equal("repair.rollbackPartial", result.Error!.Code, "Cause métier partielle conservée");
        Check.True(result.Warnings.Any(w => w.Contains("dossier-nouveau-fictif", StringComparison.Ordinal)), "Chemin de secours conservé");
        Check.True(result.Warnings.Any(w => w.Contains("n'a pas pu être actualisé", StringComparison.Ordinal)), "Échec d'écriture explicite");
    }

    [Test] public static void RecuEcritNeTransformePasUneMutationPartielleEnSucces()
    {
        var recorded = false;
        var error = RepairReconciliation.Partial("repair.partial", "Écriture possible.", "recu-fictif", ["Dossiers conservés"], () => recorded = true);
        Check.True(recorded, "Reçu effectivement tenté"); Check.Equal("repair.partial", error.Code, "Diagnostic toujours partiel");
        Check.Equal(1, error.Warnings.Count, "Aucun échec de journal inventé");
    }

    private static void Refused(RepairObservedState state, string message)
        => Check.Equal("repair.reconcile", Check.Throws<FilproOperationException>(() => RepairReconciliation.Next(state), message).Code, "Refus fermé");
}
