using System.Collections.Concurrent;

namespace Filpro.Core;

/// <summary>
/// Services donnés à chaque module d'opérations : mode, fournisseur de simulation (nul en poste réel non câblé),
/// horloge, journal technique. Les services partagés du moteur sont créés à la première utilisation par
/// <see cref="Shared{T}"/>. Volontairement minimal dans le lot 001 : rien n'est lu ici.
/// </summary>
public sealed class EngineContext
{
    private readonly ConcurrentDictionary<Type, object> shared = new();
    private int closing;

    public bool IsClosing => Volatile.Read(ref closing) != 0;

    /// <summary>Barrière irréversible pour cette composition avant annulation et nettoyage.</summary>
    public void BeginClosing()
    {
        Operations.Close();
        Interlocked.Exchange(ref closing, 1);
    }

    public ApplicationOperationGate Operations { get; } = new();

    /// <summary>Identité opaque d'une composition ; une bascule de mode crée une autre génération et d'autres services.</summary>
    public string Generation { get; }

    public EngineContext(
        ModeDecision decision,
        IFilproSimulationSource? simulation,
        IFilproPosteSource? poste,
        TimeProvider clock,
        ITechnicalJournal log,
        string? generation = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        Decision = decision;
        Simulation = simulation;
        Poste = poste;
        Clock = clock;
        Log = log;
        Generation = generation ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>Décision de démarrage (mode, raison, bandeau).</summary>
    public ModeDecision Decision { get; }

    public AppMode Mode => Decision.Mode;

    /// <summary>Fournisseur de démonstration de l'Accueil, ou nul quand le mode poste réel (non câblé) est actif.</summary>
    public IFilproSimulationSource? Simulation { get; }

    /// <summary>
    /// Fournisseur « poste » des écrans Analyser/réparer et Sauvegarder : natif (Filpro.Platform) en mode poste réel,
    /// fictif (Filpro.Simulation) en démonstration. Nul si aucun n'est câblé : les opérations répondent indisponibles.
    /// </summary>
    public IFilproPosteSource? Poste { get; }

    public TimeProvider Clock { get; }

    /// <summary>Journal technique : codes, compteurs et échecs uniquement, jamais la saisie de la page ni de secret.</summary>
    public ITechnicalJournal Log { get; }

    /// <summary>Enregistre explicitement un service de cette seule session, avant d'exposer le pont.</summary>
    public void Register<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!shared.TryAdd(typeof(T), service))
        {
            throw new InvalidOperationException("Service déjà enregistré pour cette session : " + typeof(T).Name);
        }
    }

    public T? Service<T>() where T : class => shared.TryGetValue(typeof(T), out var service) ? (T)service : null;

    public T Require<T>() where T : class => Service<T>()
        ?? throw new FilproOperationException(FilproErrors.FeatureUnavailable, "Ce service n’est pas disponible dans cette session.");

    /// <summary>Service partagé créé une seule fois à la première demande (dashboard, périmètre de travail…).</summary>
    public T Shared<T>(Func<T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return (T)shared.GetOrAdd(typeof(T), _ => factory());
    }
}
