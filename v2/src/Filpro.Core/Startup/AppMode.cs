using System.Collections.Immutable;

namespace Filpro.Core;

/// <summary>Mode de fonctionnement de l'application, décidé une fois au démarrage.</summary>
public enum AppMode
{
    /// <summary>Démonstration : fournisseur fictif en mémoire, aucune action système réelle, aucune écriture disque/registre.</summary>
    Simulation,

    /// <summary>Poste réel : analyses, sauvegardes et restaurations sur la machine courante. Non câblé dans le lot 001.</summary>
    Poste,
}

/// <summary>Raison du choix du mode ; sérialisée sous son nom camelCase.</summary>
public enum ModeReason
{
    Argument,
    Environment,
    EnvironmentOptOut,
    /// <summary>Aucun choix explicite : démonstration par défaut tant que le moteur réel n'est pas câblé.</summary>
    DefaultDemonstration,
    DefaultProduction,
}

/// <summary>Résultat de la décision de démarrage. Les <see cref="Warnings"/> vont uniquement au journal technique.</summary>
public sealed record ModeDecision(
    AppMode Mode,
    ModeReason Reason,
    string Detail,
    string? Banner,
    ImmutableArray<string> Warnings);

/// <summary>Textes opérateur du mode.</summary>
public static class ModeTexts
{
    public const string SimulationLabel = "Démonstration";
    public const string PosteLabel = "Poste réel";
    public const string Banner = "Démonstration : données fictives d'un fournisseur séparé, aucune analyse, sauvegarde ni restauration réelle sur ce poste.";
    public const string WindowTitleSuffix = " — Démonstration";

    public const string DetailArgument = "Lancé avec l’option --simulation.";
    public const string DetailEnvironment = "Variable FILPRO_SIMULATION=1 présente.";
    public const string DetailEnvironmentOptOut = "Variable FILPRO_SIMULATION=0 : mode poste réel demandé.";
    public const string DetailDefault = "Aucun mode demandé : démonstration par défaut. Le passage au poste réel se confirme dans l’interface.";

    public const string WarningVariable = "Valeur de FILPRO_SIMULATION ignorée : seules 1 et 0 sont admises.";

    public static string WarningArguments(int count) => count == 1
        ? "1 argument de ligne de commande ignoré : seuls --simulation et --poste sont reconnus."
        : $"{count} arguments de ligne de commande ignorés : seuls --simulation et --poste sont reconnus.";
}
