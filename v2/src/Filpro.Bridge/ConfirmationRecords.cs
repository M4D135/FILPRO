using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Objets que l'hôte a renvoyés à la page et que la confirmation Windows reprendra : offres de mise à jour avec la demande
/// qui les a produites, aperçus des défauts du pack et provenance des préparations. La fenêtre est construite à partir de
/// ces objets, jamais à partir des arguments de la page. Une composition en possède un seul ; chaque sorte est bornée à
/// <see cref="Capacity"/> entrées. Offres et aperçus restent valables <see cref="Lifetime"/>, comme chez leurs services ; une
/// provenance est conservée sans limite de durée, car une transaction préparée reste installable sans limite de durée
/// (une seule à la fois) : seule la capacité borne ces entrées.
/// </summary>
public sealed class ConfirmationRecords
{
    public const int Capacity = 16;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Offre renvoyée par updates.inspect et la demande qui l'a produite : sorte de source, chemin normalisé et SHA-256 saisi
    /// dans la page (qui ne prouve jamais l'origine du pack). <see cref="Deadline"/> borne la confirmation de la
    /// préparation : comptée depuis la demande d'inspection, elle ne dépasse jamais l'expiration de l'offre chez le service ;
    /// sans échéance, l'offre est tenue pour expirée.
    /// </summary>
    public sealed record OfferRecord(UpdateOffer Offer, UpdateSourceKind Kind, string? Path, bool HashTyped, DateTimeOffset Deadline = default);

    /// <summary>Aperçu renvoyé par configuration.previewDefaults et l'échéance de sa confirmation, comptée depuis la demande.</summary>
    public sealed record PreviewRecord(ConfigurationPreview Preview, DateTimeOffset Deadline);

    private readonly Table<OfferRecord> offers;
    private readonly Table<PreviewRecord> previews;
    private readonly Table<OfferRecord> provenances;

    public ConfirmationRecords(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        offers = new(clock, Lifetime);
        previews = new(clock, Lifetime);
        provenances = new(clock, null);
    }

    public static ConfirmationRecords For(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Shared(() => new ConfirmationRecords(context.Clock));
    }

    /// <summary><paramref name="requested"/> : instant pris avant la demande d'inspection au service.</summary>
    public void RecordOffer(UpdateOffer offer, UpdateSourceRequest request, DateTimeOffset requested)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(request);
        offers.Add(offer.Id, new(offer, request.Kind,
            request.Path is null ? null : StoragePathPolicy.NormalizeNative(request.Path), request.Sha256 is not null, requested + Lifetime));
    }

    public OfferRecord? Offer(string id) => offers.Find(id);

    /// <summary><paramref name="requested"/> : instant pris avant la demande d'aperçu au service.</summary>
    public void RecordPreview(ConfigurationPreview preview, DateTimeOffset requested)
    {
        ArgumentNullException.ThrowIfNull(preview);
        previews.Add(preview.Id, new(preview, requested + Lifetime));
    }

    public PreviewRecord? Preview(string id) => previews.Find(id);

    public void RecordProvenance(string transactionId, OfferRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        provenances.Add(transactionId, record);
    }

    public OfferRecord? Provenance(string transactionId) => provenances.Find(transactionId);

    /// <summary>
    /// Table bornée : la plus ancienne entrée cède sa place quand la table est pleine. Avec une durée de vie, les entrées
    /// expirées sont oubliées ; sans durée de vie (<c>null</c>), aucune échéance n'est calculée.
    /// </summary>
    private sealed class Table<T>(TimeProvider clock, TimeSpan? lifetime) where T : class
    {
        private readonly Dictionary<string, (T Value, long Order, DateTimeOffset? ExpiresAt)> entries = new(StringComparer.Ordinal);
        private readonly object gate = new();
        private long added;

        public void Add(string key, T value)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock (gate)
            {
                var now = clock.GetUtcNow();
                foreach (var expired in entries.Where(entry => !Live(entry.Value.ExpiresAt, now)).Select(entry => entry.Key).ToArray())
                    entries.Remove(expired);
                entries.Remove(key);
                if (entries.Count >= Capacity) entries.Remove(entries.MinBy(entry => entry.Value.Order).Key);
                DateTimeOffset? expiresAt = lifetime is { } span ? now + span : null;
                entries.Add(key, (value, ++added, expiresAt));
            }
        }

        public T? Find(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock (gate)
                return entries.TryGetValue(key, out var entry) && Live(entry.ExpiresAt, clock.GetUtcNow()) ? entry.Value : null;
        }

        private static bool Live(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt is not { } limit || limit > now;
    }
}
