namespace Filpro.Bridge;

/// <summary>Tout ce qu'un gestionnaire peut utiliser en plus de ses arguments : publication d'événements vers la page.</summary>
public sealed class BridgeContext(Action<BridgeResponse> publish, string requestId, string operation)
{
    public string RequestId { get; } = requestId;
    public string Operation { get; } = operation;

    /// <summary>Pousse un événement (progression, résultats partiels) à la page pour cette requête.</summary>
    public void Publish(string eventName, object? data) =>
        publish(new BridgeResponse(RequestId, "event", Operation + ":" + eventName, data));
}
