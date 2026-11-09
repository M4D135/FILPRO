using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>Règles d'admission évaluées par le répartiteur avant tout gestionnaire.</summary>
public static class AdmissionGuards
{
    /// <summary>
    /// Démonstration : lecture seule par défaut. Toute écriture système (Write) est refusée (simulation.readOnly) quelle que
    /// soit sa déclaration. Une NetworkRead est refusée (simulation.unavailable) sauf si l'opération est marquée servie par le
    /// fournisseur fictif : refusée par défaut, donc un futur outil réel ne peut jamais s'exécuter pour de vrai en démonstration.
    /// Local, LocalRead et LocalWrite (préférences locales, jamais le système) sont admis.
    /// </summary>
    public static BridgeError? ReadOnlySimulation(OperationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Class switch
        {
            OperationClass.Local or OperationClass.LocalRead or OperationClass.LocalWrite => null,
            OperationClass.NetworkRead when definition.ServedInSimulation => null,
            OperationClass.NetworkRead => new BridgeError(FilproErrors.SimulationUnavailable, FilproErrors.SimulationUnavailableMessage),
            // Toute Write, et toute classe ajoutée plus tard, est refusée tant que cette garde ne dit pas le contraire.
            _ => new BridgeError(FilproErrors.SimulationReadOnly, FilproErrors.SimulationReadOnlyMessage),
        };
    }

    /// <summary>
    /// Poste réel : le moteur d'écriture n'est pas câblé dans le lot 001. Local, LocalRead, LocalWrite et NetworkRead (lectures
    /// bornées après geste de l'opérateur) sont admis ; toute Write est refusée (write.unavailable) avant son gestionnaire,
    /// quelle que soit la classe ajoutée plus tard.
    /// </summary>
    public static BridgeError? ReadOnlyPoste(OperationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Class switch
        {
            OperationClass.Local or OperationClass.LocalRead or OperationClass.LocalWrite or OperationClass.NetworkRead => null,
            _ => new BridgeError(FilproErrors.WriteUnavailable, FilproErrors.WriteUnavailableMessage),
        };
    }

    /// <summary>Garde d'un mode : chaque mode a une garde, donc aucune opération n'est jamais admise sans règle.</summary>
    public static Func<OperationDefinition, BridgeError?> For(AppMode mode) => mode == AppMode.Simulation ? ReadOnlySimulation : ReadOnlyPoste;

    /// <summary>
    /// Seules les écritures explicitement raccordées à un fournisseur natif sont admises ; en poste, la situation de
    /// l'affichage s'applique ensuite (<see cref="Situation"/>).
    /// </summary>
    public static Func<OperationDefinition, BridgeError?> For(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return definition => Capabilities(context, definition) ?? (context.Mode == AppMode.Poste ? Situation(context, definition) : null);
    }

    /// <summary>
    /// Affichage sous un autre compte que FILPRO élevé, ou sous un compte indéterminé (situation absente comprise) : les
    /// mises à jour du code de FILPRO sont refusées (session.crossAccount) ; tant que la présence n'est pas armée et libre,
    /// seules la lecture locale et le suivi sont admis, sans changement de session (presence.locked). Même compte :
    /// rien ne change.
    /// </summary>
    public static BridgeError? Situation(EngineContext context, OperationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        if (!NativeConfirmation.AcrossAccounts(context)) return null;
        if (PrivilegePolicy.RefusedAcrossAccounts(definition.Name))
            return new BridgeError(FilproErrors.SessionCrossAccount, FilproErrors.SessionCrossAccountMessage);
        if (!NativeConfirmation.Locked(context)) return null;
        return definition.Class is OperationClass.Local or OperationClass.LocalRead
            && !definition.Name.StartsWith("session.demo", StringComparison.Ordinal)
            ? null : new BridgeError(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage);
    }

    private static BridgeError? Capabilities(EngineContext context, OperationDefinition definition)
    {
        if (context.IsClosing) return new BridgeError("session.closing", "La session se ferme et attend le nettoyage des opérations.");
        if (context.Mode == AppMode.Simulation) return ReadOnlySimulation(definition);
        if (definition.Class != OperationClass.Write) return ReadOnlyPoste(definition);
        var execution = context.Service<IFilproExecutionService>();
        var capabilities = execution is { Demonstration: false } ? execution.Capabilities : null;
        var admitted = definition.Name switch
        {
            "sauvegarder.confirmPlan" => capabilities?.Backup == true,
            "sauvegarder.confirmRestorePlan" => capabilities?.Restore == true,
            "analyser.confirmRepair" or "analyser.rollbackRepair" => capabilities?.Repair == true,
            "recuperer.confirmPlan" or "recuperer.catalog" => capabilities?.Recover == true,
            "updates.install" or "updates.rollback" => context.Service<IFilproUpdateService>() is { Demonstration: false },
            _ => false,
        };
        return admitted ? null : new BridgeError(FilproErrors.WriteUnavailable,
            "Cette opération native n’est pas disponible dans cette session. Vérifier les capacités et l’intégrité du pack.");
    }
}
