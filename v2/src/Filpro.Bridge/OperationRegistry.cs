using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Filpro.Bridge;

/// <summary>Gestionnaire d'une opération déclarée. S'exécute hors du fil de l'interface ; renvoie les données renvoyées à la page.</summary>
public delegate Task<object?> OperationHandler(JsonElement arguments, BridgeContext context, CancellationToken cancellationToken);

/// <summary>
/// Une opération que la page est autorisée à appeler. <see cref="ServedInSimulation"/> marque une opération NetworkRead
/// répondue par le fournisseur de démonstration ; toute autre NetworkRead est refusée en démonstration (simulation.unavailable).
/// </summary>
public sealed record OperationDefinition(string Name, OperationClass Class, OperationHandler Handler, string Description, bool ServedInSimulation = false);

/// <summary>Liste fermée des opérations : la page n'appelle que ce qui est enregistré ici, rien d'autre.</summary>
public sealed class OperationRegistry
{
    private readonly FrozenDictionary<string, OperationDefinition> operations;

    public OperationRegistry(IEnumerable<OperationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var map = new Dictionary<string, OperationDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!IsValidName(definition.Name))
            {
                throw new ArgumentException($"Invalid operation name: {definition.Name}", nameof(definitions));
            }
            if (!map.TryAdd(definition.Name, definition))
            {
                throw new ArgumentException($"Duplicate operation: {definition.Name}", nameof(definitions));
            }
        }
        operations = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Names => operations.Keys;

    public bool TryGet(string name, [NotNullWhen(true)] out OperationDefinition? definition) => operations.TryGetValue(name, out definition);

    /// <summary>Les noms d'opération sont en pointillés lower camel case, par ex. « accueil.summary » ou « sauvegarde.lister ».</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
        {
            return false;
        }
        var segments = name.Split('.');
        return segments.Length >= 2 && segments.All(s => s.Length > 0 && char.IsAsciiLetterLower(s[0]) && s.All(char.IsAsciiLetterOrDigit));
    }
}
