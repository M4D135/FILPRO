namespace Filpro.Core;

// ---------- Construction sûre des arguments USMT à partir du plan typé (micro-lot 004A) ----------

/// <summary>
/// Action USMT demandée. <see cref="Scan"/> et <see cref="Estimate"/> pilotent <c>scanstate.exe</c> (magasin puis
/// estimation <c>/p:</c>) ; <see cref="Load"/> pilote <c>loadstate.exe</c>. Aucune de ces actions n'est exécutée par la
/// construction des arguments : le moteur seul lance le processus, avec la liste d'arguments produite.
/// </summary>
public enum UsmtAction { Scan, Estimate, Load }

/// <summary>Correspondance source → cible retenue pour la restauration, réduite à ce que <c>/mu:</c> exige.</summary>
public sealed record UsmtAccountMapping(string Source, string Target);

/// <summary>
/// Invocation USMT produite par <see cref="UsmtArguments.Build"/> : le nom de l'exécutable attendu (résolu ensuite par le
/// moteur depuis le dossier USMT vérifié de la distribution, jamais depuis une variable d'environnement), la <b>liste
/// fermée des arguments</b> à passer telle quelle à <c>ProcessStartInfo.ArgumentList</c> (jamais une chaîne de commande
/// libre, jamais un shell), les chemins de journaux que le moteur devra créer et protéger, et une mention d'honnêteté.
/// Rien n'est exécuté ici.
/// </summary>
public sealed record UsmtInvocation(
    UsmtAction Action,
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> LogPaths,
    string Notice);

/// <summary>
/// Demande fermée soumise à <see cref="UsmtArguments.Build"/>. Les valeurs qui viennent du plan validé (comptes, options,
/// clé, correspondances) sont injectées par les adaptateurs <see cref="ForBackup"/> et <see cref="ForRestore"/> ; les
/// chemins d'exécution (magasin, dossier des journaux, règles, configuration, fichier d'estimation) viennent du moteur.
/// La demande ne porte ni horloge ni identifiant aléatoire : la construction reste pure et déterministe.
/// </summary>
public sealed record UsmtArgumentRequest(
    UsmtAction Action,
    string Store,
    string LogDirectory,
    IReadOnlyList<string> Manifests,
    IReadOnlyList<string> Users,
    IReadOnlyList<string>? ExcludeUsers = null,
    IReadOnlyList<UsmtAccountMapping>? AccountMappings = null,
    string? Config = null,
    string? EstimateFile = null,
    string? Bundle = null,
    string? KeyFile = null,
    bool NoCompress = false,
    bool ContinueOnError = false,
    bool UseVss = false,
    bool Encrypted = false)
{
    /// <summary>
    /// Adapte un plan de sauvegarde validé en demande d'arguments <see cref="UsmtAction.Scan"/> ou
    /// <see cref="UsmtAction.Estimate"/>. Les comptes viennent des profils du plan, les options (non-compression,
    /// poursuite après erreur, VSS, chiffrement) et le chemin de la clé viennent du plan ; le magasin, le dossier des
    /// journaux, les règles et le fichier d'estimation sont fournis par le moteur. Un plan de sauvegarde ne construit
    /// jamais d'arguments LoadState.
    /// </summary>
    public static UsmtArgumentRequest ForBackup(
        BackupPlanReview plan,
        UsmtAction action,
        string store,
        string logDirectory,
        IReadOnlyList<string> manifests,
        string? estimateFile = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (action == UsmtAction.Load)
        {
            throw new FilproOperationException(UsmtArguments.PlanCode, "Un plan de sauvegarde ne construit pas d'arguments LoadState.");
        }

        return new UsmtArgumentRequest(
            action,
            store,
            logDirectory,
            manifests,
            [.. plan.Profiles.Select(p => p.Account)],
            ExcludeUsers: null,
            AccountMappings: null,
            Config: null,
            EstimateFile: estimateFile,
            Bundle: plan.Destination,
            KeyFile: plan.KeyPath,
            NoCompress: plan.NoCompress,
            ContinueOnError: plan.ContinueOnError,
            UseVss: plan.UseVss,
            Encrypted: plan.Encrypted);
    }

    /// <summary>
    /// Adapte un plan de restauration validé en demande d'arguments <see cref="UsmtAction.Load"/>. Les comptes source, les
    /// correspondances, le magasin et le chemin de la clé viennent du plan ; le magasin physique (dossier du magasin USMT
    /// à l'intérieur du lot), le dossier des journaux, les règles, la configuration et les comptes exclus viennent du
    /// moteur. La non-compression et la poursuite après erreur dépendent du manifeste du magasin : le moteur les renseigne
    /// (elles ne sont pas portées par le plan).
    /// </summary>
    public static UsmtArgumentRequest ForRestore(
        RestorePlanReview plan,
        string store,
        string logDirectory,
        IReadOnlyList<string> manifests,
        string? config = null,
        IReadOnlyList<string>? excludeUsers = null,
        bool noCompress = false,
        bool continueOnError = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new UsmtArgumentRequest(
            UsmtAction.Load,
            store,
            logDirectory,
            manifests,
            plan.Users,
            ExcludeUsers: excludeUsers,
            AccountMappings: [.. plan.Mappings.Select(m => new UsmtAccountMapping(m.Source, m.Target))],
            Config: config,
            EstimateFile: null,
            Bundle: plan.Bundle,
            KeyFile: plan.SourceKeyPath,
            NoCompress: noCompress,
            ContinueOnError: continueOnError,
            UseVss: false,
            Encrypted: plan.Encrypted);
    }
}
