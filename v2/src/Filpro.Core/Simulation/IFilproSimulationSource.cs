namespace Filpro.Core;

/// <summary>
/// Contrat du fournisseur de données de l'Accueil. Le fournisseur de démonstration (Filpro.Simulation) l'implémente en
/// mémoire, sans réseau ni disque. Le mode poste réel n'est pas câblé dans le lot 001 : il reste absent et l'interface
/// indique alors honnêtement l'indisponibilité.
/// </summary>
public interface IFilproSimulationSource
{
    /// <summary>Construit la vue agrégée de l'Accueil. Ne doit jamais toucher le système réel.</summary>
    AccueilSnapshot BuildAccueil();
}
