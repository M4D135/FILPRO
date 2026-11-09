using Filpro.Core;

namespace Filpro.Platform;

internal enum RepairRegistryIdentity { Absent, Original, Replacement, Unrecognized }
internal enum RepairRollbackStep
{ CaptureReplacement, PreserveReplacement, RemoveReplacementKey, RestoreOriginalDirectory, RestoreOriginalKey, Complete }

/// <summary>Empreintes des ruches effectivement ouvertes ; null prouve l'absence du dossier, jamais un accès refusé.</summary>
internal sealed record RepairObservedState(string Stage, string OriginalExpectedHash, string? CurrentHash,
    string? PreservedOriginalHash, string? ReplacementExpectedHash, string? PreservedReplacementHash,
    RepairRegistryIdentity Registry);

/// <summary>
/// Décide une seule étape à partir des preuves réelles. Le libellé du journal ne prouve jamais qu'un renommage a eu lieu.
/// Une ruche sert à identifier le dossier conservé ; ceci ne prétend pas hasher tous les fichiers personnels.
/// </summary>
internal static class RepairReconciliation
{
    internal static bool IsKnownStage(string stage) => stage is "BackupsReady" or "RenamePending" or "FolderPreserved"
        or "DetachPending" or "AwaitingNewProfile" or "PreparePartial" or "RollbackPending" or "RollbackPartial"
        or "RolledBack" or "PreparationRolledBack" or "CancelledBeforeRename" or "FailedBeforeRename";

    internal static bool MayHaveChanged(string stage) => stage is "RenamePending" or "FolderPreserved" or "DetachPending"
        or "AwaitingNewProfile" or "PreparePartial" or "RollbackPending" or "RollbackPartial";

    internal static RepairRollbackStep Next(RepairObservedState state)
    {
        if (!IsKnownStage(state.Stage) || !Enum.IsDefined(state.Registry)) throw Invalid("État de réparation inconnu.");
        RequireHash(state.OriginalExpectedHash);
        foreach (var hash in new[] { state.CurrentHash, state.PreservedOriginalHash, state.ReplacementExpectedHash, state.PreservedReplacementHash })
            if (hash is not null) RequireHash(hash);
        var oldPresent = state.PreservedOriginalHash is not null;
        var currentIsOriginal = Same(state.CurrentHash, state.OriginalExpectedHash);
        if (oldPresent)
        {
            if (!Same(state.PreservedOriginalHash, state.OriginalExpectedHash))
                throw Invalid("La ruche du profil original conservé a changé.");
            if (currentIsOriginal) throw Invalid("Deux dossiers revendiquent le profil original ; aucune copie ne sera choisie implicitement.");
        }
        else if (!currentIsOriginal)
            throw Invalid("Le profil original n'est identifié ni à son emplacement ni dans le dossier conservé.");

        if (state.ReplacementExpectedHash is null)
        {
            if (state.PreservedReplacementHash is not null || state.Registry == RepairRegistryIdentity.Replacement)
                throw Invalid("La conservation du nouveau profil n'est pas liée à des sauvegardes vérifiées.");
        }
        else
        {
            if (Same(state.ReplacementExpectedHash, state.OriginalExpectedHash))
                throw Invalid("Les deux identités de ruche sont identiques ; association des dossiers ambiguë.");
            if (state.PreservedReplacementHash is not null && !Same(state.PreservedReplacementHash, state.ReplacementExpectedHash))
                throw Invalid("Le nouveau profil conservé a changé ; aucun remplacement automatique.");
            if (oldPresent && state.CurrentHash is not null)
            {
                if (!Same(state.CurrentHash, state.ReplacementExpectedHash) || state.PreservedReplacementHash is not null)
                    throw Invalid("Le nouveau profil a changé ou existe à deux emplacements.");
            }
            else if (state.PreservedReplacementHash is null)
                throw Invalid("Le nouveau profil à préserver n'est plus présent à l'emplacement enregistré.");
        }

        if (state.Stage is "RolledBack" or "PreparationRolledBack")
        {
            if (oldPresent || !currentIsOriginal || state.Registry != RepairRegistryIdentity.Original)
                throw Invalid("Le reçu terminé ne correspond plus à l'état réel ; aucune nouvelle mutation.");
            return RepairRollbackStep.Complete;
        }

        if (oldPresent && state.CurrentHash is not null)
        {
            if (state.ReplacementExpectedHash is null)
            {
                if (state.Registry == RepairRegistryIdentity.Absent)
                    throw Invalid("Un dossier occupe la cible sans entrée ProfileList identifiable.");
                return RepairRollbackStep.CaptureReplacement;
            }
            if (state.Registry == RepairRegistryIdentity.Unrecognized)
                throw Invalid("L'entrée du nouveau profil a changé depuis sa sauvegarde.");
            return RepairRollbackStep.PreserveReplacement;
        }
        if (state.Registry == RepairRegistryIdentity.Unrecognized)
            throw Invalid("Une entrée ProfileList étrangère occupe le SID ; aucune suppression.");
        if (state.Registry == RepairRegistryIdentity.Replacement)
        {
            if (state.PreservedReplacementHash is null)
                throw Invalid("L'entrée du nouveau profil ne peut pas être détachée avant conservation de son dossier.");
            return RepairRollbackStep.RemoveReplacementKey;
        }
        if (oldPresent) return RepairRollbackStep.RestoreOriginalDirectory;
        return state.Registry == RepairRegistryIdentity.Absent ? RepairRollbackStep.RestoreOriginalKey : RepairRollbackStep.Complete;
    }

    /// <summary>Le diagnostic secondaire ne doit jamais remplacer la connaissance d'une mutation déjà possible.</summary>
    internal static ExecutionPartialException Partial(string code, string message, string receiptPath,
        IReadOnlyList<string> recoveryPaths, Action record)
    {
        var warnings = recoveryPaths.ToList();
        try { record(); }
        catch (Exception)
        { warnings.Add("Le reçu n'a pas pu être actualisé : " + receiptPath + ". Conserver les chemins affichés ; aucun succès de journalisation n'est déclaré."); }
        return new(code, message, receiptPath, warnings);
    }

    private static bool Same(string? actual, string expected) => actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static void RequireHash(string value)
    {
        if (value is not { Length: 64 } || value.Any(character => !char.IsAsciiHexDigit(character)))
            throw Invalid("Empreinte de ruche invalide.");
    }
    private static FilproOperationException Invalid(string message) => new("repair.reconcile", message);
}
