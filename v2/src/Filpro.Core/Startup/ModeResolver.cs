using System.Collections.Immutable;

namespace Filpro.Core;

/// <summary>
/// Choisit le mode de fonctionnement depuis la ligne de commande et l'environnement. Le mode poste réel n'étant pas
/// câblé dans le lot 001, l'absence de choix explicite retombe honnêtement en démonstration.
/// </summary>
public static class ModeResolver
{
    public const string SimulationArgument = "--simulation";
    public const string PosteArgument = "--poste";
    public const string SimulationVariable = "FILPRO_SIMULATION";

    /// <summary>
    /// Pur. Ordre : --simulation, FILPRO_SIMULATION=1, FILPRO_SIMULATION=0, sinon démonstration par défaut.
    /// </summary>
    public static ModeDecision Resolve(IReadOnlyList<string> arguments, string? variableValue, bool productionDefault = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var warnings = ImmutableArray.CreateBuilder<string>();

        var simulationArgument = false;
        var posteArgument = false;
        var ignoredArguments = 0;
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, SimulationArgument, StringComparison.OrdinalIgnoreCase))
            {
                simulationArgument = true;
            }
            else if (string.Equals(argument, PosteArgument, StringComparison.OrdinalIgnoreCase))
            {
                posteArgument = true;
            }
            else
            {
                ignoredArguments++;
            }
        }
        // Les arguments ignorés sont comptés, jamais recopiés : ils pourraient porter n'importe quoi.
        if (ignoredArguments > 0)
        {
            warnings.Add(ModeTexts.WarningArguments(ignoredArguments));
        }

        var variable = variableValue?.Trim();
        var variableOn = string.Equals(variable, "1", StringComparison.Ordinal);
        var variableOff = string.Equals(variable, "0", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(variable) && !variableOn && !variableOff)
        {
            warnings.Add(ModeTexts.WarningVariable);
        }

        if (simulationArgument)
        {
            return Simulation(ModeReason.Argument, ModeTexts.DetailArgument, warnings);
        }
        if (posteArgument)
        {
            return new ModeDecision(AppMode.Poste, ModeReason.Argument, "Lancé avec l’option --poste.", null, warnings.ToImmutable());
        }
        if (variableOn)
        {
            return Simulation(ModeReason.Environment, ModeTexts.DetailEnvironment, warnings);
        }
        if (variableOff)
        {
            return new ModeDecision(AppMode.Poste, ModeReason.EnvironmentOptOut, ModeTexts.DetailEnvironmentOptOut, null, warnings.ToImmutable());
        }

        return productionDefault
            ? new ModeDecision(AppMode.Poste, ModeReason.DefaultProduction, "Pack PROD : contexte du poste par défaut.", null, warnings.ToImmutable())
            : Simulation(ModeReason.DefaultDemonstration, ModeTexts.DetailDefault, warnings);
    }

    private static ModeDecision Simulation(ModeReason reason, string detail, ImmutableArray<string>.Builder warnings) =>
        new(AppMode.Simulation, reason, detail, ModeTexts.Banner, warnings.ToImmutable());
}
