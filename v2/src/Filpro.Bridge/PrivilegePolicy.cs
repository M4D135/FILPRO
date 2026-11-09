using System.Collections.Frozen;

namespace Filpro.Bridge;

/// <summary>Effet d'une opération privilégiée : sur le poste (profils, magasins, pack) ou sur le code de FILPRO.</summary>
public enum PrivilegeTier { System, Code }

/// <summary>Opération qui ne s'exécute qu'après accord dans la fenêtre de confirmation Windows de l'hôte élevé.</summary>
public sealed record PrivilegedOperation(string Name, OperationClass Class, PrivilegeTier Tier);

/// <summary>
/// Liste fermée des opérations privilégiées, fixée dans le code : aucun réglage, aucune variable et aucun argument de la
/// page ne la modifie. Toute écriture système (Write) doit y figurer et chaque nom doit exister dans le registre avec la
/// classe déclarée ; sinon <see cref="Validate"/> arrête le démarrage.
/// </summary>
public static class PrivilegePolicy
{
    public static IReadOnlyList<PrivilegedOperation> Operations { get; } =
    [
        new("sauvegarder.confirmPlan", OperationClass.Write, PrivilegeTier.System),
        new("sauvegarder.confirmRestorePlan", OperationClass.Write, PrivilegeTier.System),
        new("analyser.confirmRepair", OperationClass.Write, PrivilegeTier.System),
        new("analyser.rollbackRepair", OperationClass.Write, PrivilegeTier.System),
        new("recuperer.catalog", OperationClass.Write, PrivilegeTier.System),
        new("recuperer.confirmPlan", OperationClass.Write, PrivilegeTier.System),
        new("configuration.applyDefaults", OperationClass.LocalWrite, PrivilegeTier.System),
        new("updates.prepare", OperationClass.LocalWrite, PrivilegeTier.Code),
        new("updates.install", OperationClass.Write, PrivilegeTier.Code),
        new("updates.rollback", OperationClass.Write, PrivilegeTier.Code),
    ];

    private static readonly FrozenDictionary<string, PrivilegedOperation> ByName =
        Operations.ToFrozenDictionary(operation => operation.Name, StringComparer.Ordinal);

    // Recherche d'une offre et source mémorisée : elles ne servent qu'à mettre à jour le code de FILPRO, et la source
    // mémorisée sert ensuite au compte élevé dans sa propre session.
    private static readonly FrozenSet<string> CodeAdjacent =
        new[] { "updates.inspect", "updates.saveSource" }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsPrivileged(string name) => name is not null && ByName.ContainsKey(name);

    /// <summary>Palier d'une opération privilégiée ; nul pour toute autre opération.</summary>
    public static PrivilegeTier? TierOf(string name) => name is not null && ByName.TryGetValue(name, out var operation) ? operation.Tier : null;

    /// <summary>
    /// Opérations réservées à une session du compte élevé : mise à jour du code de FILPRO, recherche de l'offre et source
    /// mémorisée qui la servent, pour que le compte de la session ne choisisse pas la source des prochaines mises à jour.
    /// </summary>
    public static bool RefusedAcrossAccounts(string name) =>
        name is not null && (CodeAdjacent.Contains(name) || TierOf(name) == PrivilegeTier.Code);

    /// <summary>Registre et liste doivent concorder : une écriture oubliée ou un nom périmé est une erreur de programmation.</summary>
    public static void Validate(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var name in registry.Names)
        {
            if (registry.TryGet(name, out var definition) && definition.Class == OperationClass.Write && !ByName.ContainsKey(name))
                throw new InvalidOperationException($"Write operation {name} is missing from the privilege policy.");
        }
        foreach (var operation in Operations)
        {
            if (!registry.TryGet(operation.Name, out var definition))
                throw new InvalidOperationException($"Privileged operation {operation.Name} is not registered.");
            if (definition.Class != operation.Class)
                throw new InvalidOperationException($"Privileged operation {operation.Name} is registered as {definition.Class}, not {operation.Class}.");
        }
        foreach (var name in CodeAdjacent)
        {
            if (!registry.TryGet(name, out _)) throw new InvalidOperationException($"Update operation {name} is not registered.");
        }
    }
}
