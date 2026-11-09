using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filpro.Bridge;

/// <summary>Requête envoyée par la page : un identifiant de corrélation, un nom d'opération déclarée et ses arguments JSON.</summary>
public sealed record BridgeRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("op")] string Operation,
    [property: JsonPropertyName("args")] JsonElement Arguments);

/// <summary>Réponse ou événement poussé à la page. <see cref="Kind"/> vaut "result", "error" ou "event" ; « error » n'est écrit que s'il est renseigné.</summary>
public sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("op")] string Operation,
    [property: JsonPropertyName("data")] object? Data,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeError? Error = null);

/// <summary>Erreur renvoyée à la page : un code stable pour l'interface et un message français pour l'opérateur.</summary>
public sealed record BridgeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Classe d'une opération, qui décide comment le moteur l'admet et la budgète.</summary>
public enum OperationClass
{
    /// <summary>Calcul pur ou état en mémoire ; ne touche jamais le disque ni le réseau.</summary>
    Local,
    /// <summary>Lecture du magasin local de l'application ou de fichiers locaux.</summary>
    LocalRead,
    /// <summary>Lecture du poste (fichiers, registre, modèles) ; seulement après un geste explicite de l'opérateur.</summary>
    NetworkRead,
    /// <summary>
    /// Écriture des seules préférences locales de l'application (dossier de données utilisateur, document séparé en
    /// démonstration) ; jamais le système réel. Admise en démonstration.
    /// </summary>
    LocalWrite,
    /// <summary>Écriture système réelle (sauvegarde, restauration, régénération) ; plan, confirmation, reçu et relecture requis.</summary>
    Write,
}
