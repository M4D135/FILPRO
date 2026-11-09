namespace Filpro.Core;

/// <summary>Limites partagées par le moteur d'exécution et le pont qui demande la confirmation Windows.</summary>
public static class ExecutionLimits
{
    /// <summary>
    /// Âge maximal d'un plan au démarrage d'une opération native : au-delà, le moteur refuse le plan (execution.expired).
    /// Le pont refuse avant la confirmation Windows un plan qui atteindrait cet âge pendant que la fenêtre reste ouverte.
    /// </summary>
    public static readonly TimeSpan PlanFreshness = TimeSpan.FromMinutes(10);
}
