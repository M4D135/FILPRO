namespace Filpro.Core;

/// <summary>Échec attendu d'une opération du moteur : un code stable pour la page et un message français pour l'opérateur.</summary>
public sealed class FilproOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Codes et messages d'erreur du contrat du pont.</summary>
public static class FilproErrors
{
    public const string ArgumentsInvalid = "arguments.invalid";

    public const string SimulationUnavailable = "simulation.unavailable";
    public const string SimulationUnavailableMessage = "Non disponible en démonstration : aucune analyse, sauvegarde ni restauration réelle sur ce poste.";

    public const string SimulationReadOnly = "simulation.readOnly";
    public const string SimulationReadOnlyMessage = "Écritures désactivées en démonstration : aucune action réelle tentée.";

    public const string WriteUnavailable = "write.unavailable";
    public const string WriteUnavailableMessage = "Écritures non disponibles dans cette version : aucune action réelle tentée.";

    public const string SourceUnavailable = "source.unavailable";
    public const string SourceUnavailableMessage = "Fournisseur de données indisponible dans cette version : lancez FILPRO avec --simulation pour l’essayer sans toucher au poste.";

    public const string FeatureUnavailable = "feature.unavailable";
    public const string FeatureUnavailableMessage = "Fonction non implémentée dans ce lot : rien n’a été exécuté.";

    public const string ConfirmationRefused = "confirmation.refused";
    public const string ConfirmationRefusedMessage = "Opération refusée dans la fenêtre « FILPRO — Confirmation Windows » : rien n’a été lancé.";

    public const string ConfirmationTimeout = "confirmation.timeout";
    public const string ConfirmationTimeoutMessage = "Aucune réponse dans la fenêtre « FILPRO — Confirmation Windows » depuis 2 minutes : rien n’a été lancé.";

    public const string ConfirmationBusy = "confirmation.busy";
    public const string ConfirmationBusyMessage = "Une fenêtre Windows de FILPRO est déjà ouverte ou une confirmation vient d’être refusée : répondez-y ou réessayez dans quelques secondes.";

    public const string ConfirmationUnavailable = "confirmation.unavailable";
    public const string ConfirmationUnavailableMessage = "La confirmation Windows de FILPRO n’est pas disponible : aucune opération n’a été lancée.";

    public const string SessionCrossAccount = "session.crossAccount";
    public const string SessionCrossAccountMessage = "Les mises à jour de FILPRO ne sont pas proposées quand FILPRO est élevé avec un autre compte que celui de la session. Mettez FILPRO à jour depuis une session du compte administrateur.";

    public const string PresenceLocked = "presence.locked";
    public const string PresenceLockedMessage = "FILPRO est verrouillé dans cette session : les opérations en cours continuent. Fermez puis relancez FILPRO avec vos identifiants pour en lancer d’autres.";
}
