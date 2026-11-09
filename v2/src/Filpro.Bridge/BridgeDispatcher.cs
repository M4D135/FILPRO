using System.Text.Json;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Valide chaque message venant de la page et exécute l'opération déclarée correspondante. La garde d'admission optionnelle
/// est évaluée avant le gestionnaire (démonstration : Write refusée, NetworkRead refusée sauf servie par le fournisseur
/// fictif). Chaque refus est signalé par <see cref="Refused"/> pour que l'hôte le trace.
/// </summary>
public sealed class BridgeDispatcher(OperationRegistry registry, Action<BridgeResponse> publish, Func<OperationDefinition, BridgeError?>? admission = null)
{
    /// <summary>Plus long message accepté venant de la page ; au-delà, refusé avant analyse.</summary>
    public const int MaxMessageLength = 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>Traite un message JSON brut. Ne lève jamais : une requête mal formée ou inconnue reçoit une erreur.</summary>
    public async Task HandleAsync(string json, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxMessageLength)
        {
            Refuse(null, "bridge", null, new BridgeError("message.size", "Message de l’interface refusé : taille non admise."));
            return;
        }
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Le message de l'analyseur peut citer la saisie de la page : seul le code est signalé.
            Refuse(null, "bridge", null, new BridgeError("message.json", "Message de l’interface illisible."));
            return;
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 64)
        {
            Refuse(null, "bridge", null, new BridgeError("message.id", "Message de l’interface sans identifiant valide."));
            return;
        }
        if (!OperationRegistry.IsValidName(request.Operation) || !registry.TryGet(request.Operation, out var definition))
        {
            // Un nom bien formé (ASCII pointillé, 64 caractères au plus) est signalé ; tout le reste ne l'est pas.
            var traced = OperationRegistry.IsValidName(request.Operation) ? request.Operation : null;
            Refuse(request.Id, request.Operation ?? "", traced, new BridgeError("operation.unknown", "Opération inconnue : rien n’a été exécuté."));
            return;
        }
        if (admission?.Invoke(definition) is BridgeError refusal)
        {
            Refuse(request.Id, definition.Name, definition.Name, refusal);
            return;
        }
        var context = new BridgeContext(publish, request.Id, definition.Name);
        try
        {
            var data = await definition.Handler(request.Arguments, context, cancellationToken).ConfigureAwait(false);
            publish(new BridgeResponse(request.Id, "result", definition.Name, data));
        }
        catch (OperationCanceledException)
        {
            publish(new BridgeResponse(request.Id, "error", definition.Name, null, new BridgeError("operation.cancelled", "Opération annulée.")));
        }
        catch (BridgeOperationException ex)
        {
            // Arguments refusés par le schéma de l'opération (arguments.invalid).
            Refuse(request.Id, definition.Name, definition.Name, new BridgeError(ex.Code, ex.Message));
        }
        catch (FilproOperationException ex)
        {
            publish(new BridgeResponse(request.Id, "error", definition.Name, null, new BridgeError(ex.Code, ex.Message)));
        }
#pragma warning disable CA1031 // La page doit toujours recevoir une réponse ; l'exception est tracée par l'hôte.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UnhandledError?.Invoke(definition.Name, ex);
            publish(new BridgeResponse(request.Id, "error", definition.Name, null, new BridgeError("operation.failed", "L’opération a échoué. Le détail technique est dans le journal.")));
        }
    }

    /// <summary>Levé pour une échec imprévu d'un gestionnaire, afin que l'hôte l'écrive au journal technique.</summary>
    public event Action<string, Exception>? UnhandledError;

    /// <summary>
    /// Levé pour chaque message refusé, avec le code d'erreur et le nom de l'opération quand elle est déclarée ou bien formée
    /// (sinon null). Ne porte jamais le contenu du message.
    /// </summary>
    public event Action<string, string?>? Refused;

    private void Refuse(string? id, string operation, string? tracedOperation, BridgeError error)
    {
        Refused?.Invoke(error.Code, tracedOperation);
        publish(new BridgeResponse(id, "error", operation, null, error));
    }
}

/// <summary>Échec attendu d'une opération, montré à l'opérateur tel quel (message français, code stable).</summary>
public sealed class BridgeOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
