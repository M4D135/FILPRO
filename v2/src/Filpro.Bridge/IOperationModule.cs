using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Les opérations d'un écran (un module par écran). La composition trouve chaque module par réflexion
/// (<see cref="OperationModules.Discover"/>), donc ajouter un écran ne modifie jamais une liste centrale. Un module est une
/// classe publique, non abstraite, avec un constructeur public sans paramètre ; chaque opération qu'il déclare est nommée
/// « &lt;écran&gt;.&lt;verbe&gt; » (« accueil.summary », « sauvegarde.lister »).
/// </summary>
public interface IOperationModule
{
    /// <summary>Premier segment de chaque opération du module, lower camel case (« accueil », « journal »).</summary>
    string Screen { get; }

    /// <summary>
    /// Déclare les opérations. S'exécute une fois au démarrage, sur le fil de l'interface : rien n'est lu ici, les services
    /// ne sont que conservés pour les gestionnaires (qui s'exécutent hors du fil de l'interface).
    /// </summary>
    IEnumerable<OperationDefinition> CreateOperations(EngineContext context);
}
