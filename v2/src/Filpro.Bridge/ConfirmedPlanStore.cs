using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>Plans détenus par le serveur, bornés et à usage unique. La page ne peut jamais soumettre un plan à exécuter.</summary>
public sealed class ConfirmedPlanStore(TimeProvider clock)
{
    private sealed record Entry(object Plan, string Fingerprint, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Entry> plans = new(StringComparer.Ordinal);
    private readonly object sync = new();
    public static readonly TimeSpan Lifetime = ExecutionLimits.PlanFreshness;
    public const int MaximumPlans = 64;

    public void Add<T>(string id, T plan) where T : class
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(id);
        lock (sync)
        {
            var now = clock.GetUtcNow();
            foreach (var entry in plans.Where(pair => pair.Value.ExpiresAt <= now)) plans.TryRemove(entry.Key, out _);
            if (plans.Count >= MaximumPlans)
                throw new FilproOperationException("plan.capacity", "Trop d’aperçus en attente : fermez les anciens aperçus ou attendez leur expiration.");
            if (!plans.TryAdd(id, new(plan, Fingerprint(plan), now + Lifetime)))
                throw new FilproOperationException("plan.id", "Identifiant de plan déjà utilisé.");
        }
    }

    public T Take<T>(string id) where T : class
    {
        ValidateId(id);
        // Retrait atomique : une double confirmation ne peut lancer deux opérations.
        if (!plans.TryRemove(id, out var entry) || entry.ExpiresAt <= clock.GetUtcNow()
            || entry.Plan is not T plan || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(entry.Fingerprint), Convert.FromHexString(Fingerprint(plan))))
            throw new FilproOperationException("plan.expired", "Cet aperçu a expiré, a changé ou a déjà été confirmé. Recalculez-le avant de continuer.");
        return plan;
    }

    public static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, BridgeJson.Options)));

    private static void ValidateId(string id)
    {
        if (id is null || id.Length != 32 || id.Any(character => !char.IsAsciiHexDigit(character))
            || !string.Equals(id, id.ToLowerInvariant(), StringComparison.Ordinal))
            throw new FilproOperationException("plan.id", "Identifiant de plan invalide.");
    }
}
