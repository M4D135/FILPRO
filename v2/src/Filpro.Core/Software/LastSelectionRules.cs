namespace Filpro.Core;

/// <summary>Réapplique les choix précis d'un compte aux éléments relus, sans inventer d'élément absent de l'inventaire.</summary>
public static class LastSelectionRules
{
    /// <summary>
    /// Sans document mémorisé, applique les recommandations. Avec un document, même vide, conserve exactement le choix
    /// enregistré et ne réactive aucun logiciel recommandé. Les tailles et constats restent ceux de la nouvelle lecture.
    /// </summary>
    public static IReadOnlyList<SoftwareGroup> Apply(
        string sid, IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry, LastSelection? last)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(registry);
        if (last is null)
        {
            return SoftwareCatalog.ApplyRecommendations(SoftwareCatalog.Group(files, registry));
        }
        if (!string.Equals(last.Sid, sid, StringComparison.Ordinal))
        {
            throw new FilproOperationException("selection.profile", "La sélection mémorisée appartient à un autre profil.");
        }

        var selectedFiles = new HashSet<string>(last.Files.Select(Key), StringComparer.OrdinalIgnoreCase);
        var selectedRegistry = new HashSet<string>(last.Registry.Select(Key), StringComparer.OrdinalIgnoreCase);
        return SoftwareCatalog.Group(
            [.. files.Select(item => item with { Selected = selectedFiles.Contains(Key(item)) })],
            [.. registry.Select(item => item with { Selected = selectedRegistry.Contains(Key(item)) })]);
    }

    private static string Key(SoftwareItem item) => item.Scope + "|" + item.Rel.Replace('/', '\\').TrimEnd('\\');
}
