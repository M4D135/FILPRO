namespace Filpro.Core;

/// <summary>
/// Résolution des politiques de conflit de restauration des réglages, fidèle au correctif V1
/// (Resolve-FilproRestoreConflictPolicies, Get-FilproRestoreConflictSummary). Une décision doit couvrir tous les groupes
/// affichés (aucune valeur implicite cachée) ; deux logiciels qui chevauchent la même portée avec des politiques
/// différentes sont refusés. La coexistence (<see cref="RestorePolicyMode.KeepBoth"/>) est le choix par défaut : le
/// registre garde la valeur actuelle, les fichiers sont conservés côte à côte.
/// </summary>
public static class RestoreConflictPolicies
{
    public const string ConflictCode = "conflict.policy";

    /// <summary>Aperçu de préparation d'une restauration : groupes concernés, décisions canonisées et résumé pour l'opérateur.</summary>
    public sealed record ConflictPreview(
        IReadOnlyList<SoftwareGroup> Choices,
        IReadOnlyList<RestorePolicy> Policies,
        string Summary);

    /// <summary>Valide et canonise les décisions. Lève <see cref="FilproOperationException"/> (code conflict.policy) si ambigu.</summary>
    public static IReadOnlyList<RestorePolicy> Resolve(IReadOnlyList<SoftwareGroup> choices, IReadOnlyList<RestorePolicy>? policies)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count > 500)
        {
            throw Conflict("Trop de groupes de réglages.");
        }

        IReadOnlyList<RestorePolicy> effective;
        if (policies is { Count: > 0 })
        {
            effective = policies;
        }
        else if (choices.Count == 0)
        {
            effective = [];
        }
        else
        {
            effective = [.. choices.Select(c => new RestorePolicy(c.Id, RestorePolicyMode.KeepBoth))];
        }

        if (effective.Count > 500)
        {
            throw Conflict("Trop de politiques de restauration.");
        }

        var available = new Dictionary<string, SoftwareGroup>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (!available.TryAdd(choice.Id, choice))
            {
                throw Conflict("Groupe de réglages ambigu.");
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RestorePolicy>(effective.Count);
        foreach (var policy in effective)
        {
            if (string.IsNullOrEmpty(policy.Id) || !available.ContainsKey(policy.Id) || !seen.Add(policy.Id))
            {
                throw Conflict("Politique sans groupe sauvegardé ou en double.");
            }
            result.Add(new RestorePolicy(policy.Id, policy.Mode));
        }

        if (result.Count != choices.Count)
        {
            throw Conflict("Décision manquante pour un groupe de réglages.");
        }

        var scopes = new List<RestoreScope>();
        var patterns = new Dictionary<string, RestorePolicyMode>(StringComparer.Ordinal);
        foreach (var row in result)
        {
            var choice = available[row.Id];
            foreach (var scope in Scopes(choice, row.Mode))
            {
                foreach (var previous in scopes)
                {
                    var overlaps = string.Equals(previous.Path, scope.Path, StringComparison.OrdinalIgnoreCase)
                        || scope.Path.StartsWith(previous.Path + '\\', StringComparison.OrdinalIgnoreCase)
                        || previous.Path.StartsWith(scope.Path + '\\', StringComparison.OrdinalIgnoreCase);
                    if (previous.Type == scope.Type && previous.Mode != scope.Mode && overlaps)
                    {
                        throw Conflict("Deux logiciels chevauchent les mêmes réglages avec des politiques différentes. Choisir une politique commune.");
                    }
                }
                scopes.Add(scope);
                var key = scope.Type + "|" + scope.Path;
                if (patterns.TryGetValue(key, out var existing) && existing != scope.Mode)
                {
                    throw Conflict("Deux logiciels imposent des politiques différentes au même réglage.");
                }
                patterns[key] = scope.Mode;
            }
        }

        return [.. result.OrderBy(p => p.Id, StringComparer.Ordinal)];
    }

    /// <summary>Résumé lisible pour l'opérateur, avant le plan (résumé du correctif V1) : les lignes de <see cref="SummaryLines"/> jointes par CR LF.</summary>
    public static string Summarize(IReadOnlyList<SoftwareGroup> choices, IReadOnlyList<RestorePolicy> policies) =>
        string.Join("\r\n", SummaryLines(choices, policies));

    /// <summary>
    /// Lignes du résumé : une par sorte de décision, puis la validation restante. Un nom de groupe peut venir du manifeste du
    /// magasin ; les lignes ne sont jamais retrouvées en découpant le texte, de sorte qu'une fin de ligne contenue dans un
    /// nom reste à l'intérieur de sa ligne.
    /// </summary>
    public static IReadOnlyList<string> SummaryLines(IReadOnlyList<SoftwareGroup> choices, IReadOnlyList<RestorePolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(policies);
        if (choices.Count == 0)
        {
            return ["Aucun réglage spécifique à restaurer."];
        }
        var map = new Dictionary<string, RestorePolicyMode>(StringComparer.Ordinal);
        foreach (var policy in policies)
        {
            map[policy.Id] = policy.Mode;
        }
        var restored = new List<string>();
        var kept = new List<string>();
        var mixed = new List<string>();
        var unconfigured = new List<string>();
        foreach (var choice in choices)
        {
            if (!map.TryGetValue(choice.Id, out var mode))
            {
                unconfigured.Add(choice.Name);
                continue;
            }
            switch (mode)
            {
                case RestorePolicyMode.UseSaved: restored.Add(choice.Name); break;
                case RestorePolicyMode.KeepCurrent: kept.Add(choice.Name); break;
                default: mixed.Add(choice.Name); break;
            }
        }
        var lines = new List<string>();
        if (restored.Count > 0)
        {
            lines.Add("Rétablissement demandé du contenu sauvegardé : " + string.Join(", ", restored));
        }
        if (kept.Count > 0)
        {
            lines.Add("Conservation demandée des réglages actuels en cas de conflit : " + string.Join(", ", kept));
        }
        if (mixed.Count > 0)
        {
            lines.Add("Coexistence demandée des fichiers ; registre actuel conservé : " + string.Join(", ", mixed));
        }
        if (unconfigured.Count > 0)
        {
            lines.Add("Politique de conflit non définie : " + string.Join(", ", unconfigured));
        }
        if (lines.Count == 0)
        {
            lines.Add("Aucune action spécifique sur les conflits de réglages.");
        }
        lines.Add("Validation utilisateur restante : l’activation effective des réglages (registre, services, sessions) doit être contrôlée dans la session cible après restauration.");
        return lines;
    }

    private static IEnumerable<RestoreScope> Scopes(SoftwareGroup choice, RestorePolicyMode mode)
    {
        foreach (var file in choice.Files)
        {
            var rel = (file.Rel ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
            var path = (file.Scope == SoftwareScope.Roaming ? "Roaming" : "Local") + "|" + rel;
            yield return new RestoreScope("File", path, mode);
        }
        foreach (var key in choice.Registry)
        {
            var rel = (key.Rel ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
            yield return new RestoreScope("Registry", rel, mode);
        }
    }

    private static FilproOperationException Conflict(string message) => new(ConflictCode, message);
}
