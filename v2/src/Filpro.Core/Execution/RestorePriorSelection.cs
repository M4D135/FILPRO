namespace Filpro.Core;

/// <summary>Le préalable couvre aussi les portées du magasin qui ne figurent pas dans les recettes locales de la cible.</summary>
public static class RestorePriorSelection
{
    public static SoftwareQuery Query(string targetSid, IReadOnlyList<RestoreMappingResolution> mappings,
        IReadOnlyList<BackupProfileSelection> sources)
    {
        var accounts = mappings.Where(m => m.TargetSid == targetSid).Select(m => m.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = sources.Where(s => accounts.Contains(s.Account)).ToArray();
        if (accounts.Count == 0 || selected.Length != accounts.Count)
            throw new FilproOperationException("restore.prior", "La portée source du préalable n'est pas identifiée sans ambiguïté.");
        return new(selected.SelectMany(s => s.Files).DistinctBy(f => (f.Scope, f.Rel.ToUpperInvariant())).ToArray(),
            selected.SelectMany(s => s.Registry).Select(r => r.Rel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
