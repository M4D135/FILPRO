namespace Filpro.Core;

public sealed record LegacyProfileScope(BackupProfileSelection Selection, IReadOnlyList<string> PersonalFolders);

/// <summary>La compatibilité V1 ne déduit aucune identité ni portée externe : tout fichier doit appartenir à un profil attesté.</summary>
public static class LegacyRestoreScope
{
    public static IReadOnlyList<LegacyProfileScope> Resolve(IReadOnlyList<RestoreSourceProfile> sources,
        LegacySoftwareSelection selection, BackupCatalog catalog)
    {
        if (!catalog.Complete || sources.Count == 0 || sources.Any(s => s.Sid is null || s.ProfilePath is null)
            || sources.Select(s => s.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count
            || sources.Select(s => s.ProfilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw Invalid("Un catalogue complet et des identités source distinctes sont requis.");
        var roots = sources.Select(s => StoragePathPolicy.NormalizeWindows(s.ProfilePath!)).ToArray();
        if (roots.Any(r => r.StartsWith(@"\\", StringComparison.Ordinal))) throw Invalid("Un profil source réseau n'est pas pris en charge.");
        var folders = sources.Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (var entry in catalog.Entries)
        {
            if (entry.Corrupted) throw Invalid("Un fichier corrompu ne permet pas une restauration V1.");
            var name = OriginalName(entry);
            var owners = Enumerable.Range(0, roots.Length).Where(i => StoragePathPolicy.IsWithin(name, roots[i], allowRoot: false)).ToArray();
            if (owners.Length != 1) throw Invalid("Un fichier V1 se trouve hors des profils source attestés ou leur portée est ambiguë.");
            var owner = owners[0];
            var relative = name[(roots[owner].Length + 1)..];
            var segments = relative.Split('\\');
            if (segments[0].Equals("AppData", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length < 4) throw Invalid("Une portée AppData V1 n'est pas représentable.");
                var scope = segments[1].Equals("Local", StringComparison.OrdinalIgnoreCase) ? SoftwareScope.Local
                    : segments[1].Equals("Roaming", StringComparison.OrdinalIgnoreCase) ? SoftwareScope.Roaming : (SoftwareScope?)null;
                var applicationPath = string.Join('\\', segments.Skip(2));
                if (scope is null || !selection.Files.Any(f => f.Scope == scope && applicationPath.StartsWith(f.Rel.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw Invalid("Un fichier AppData V1 n'est pas couvert par les sélections de fichiers reconnues.");
            }
            else if (segments.Length > 1)
            {
                StoragePathPolicy.RequireSegment(segments[0]);
                if (segments[0].IndexOfAny(['%', '*', '?', '[', ']']) >= 0) throw Invalid("Dossier personnel V1 non représentable.");
                folders[owner].Add(segments[0]);
            }
            else if (segments[0].StartsWith("NTUSER", StringComparison.OrdinalIgnoreCase))
                throw Invalid("Une ruche brute n'est pas une donnée personnelle à réappliquer comme fichier.");
        }
        return sources.Select((s, index) => new LegacyProfileScope(new(s.Sid!, ProfileIdentityKind.CompteLocalOuDomaine,
            s.Account, roots[index], false, selection.Files, selection.Registry), folders[index].Order(StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
    }

    public static string OriginalName(BackupCatalogEntry entry)
    {
        if (entry.StoredPath is null) return ExecutionCatalogCodec.NormalizeName(entry.Name);
        if (entry.StoredPath != "USMT/File/" + BackupManifestCodec.RequireRelative(entry.Name)) throw Invalid("Index V1 non cohérent.");
        var split = entry.Name.IndexOf('/');
        var drive = split > 0 ? entry.Name[..split] : "";
        if ((drive.Length != 1 && (drive.Length != 2 || drive[1] != '$')) || !char.IsAsciiLetter(drive[0]))
            throw Invalid("L'arborescence native V1 ne permet pas de prouver le chemin d'origine ; récupération indépendante uniquement.");
        return ExecutionCatalogCodec.NormalizeName(char.ToUpperInvariant(drive[0]) + @":\" + entry.Name[(split + 1)..].Replace('/', '\\'));
    }

    private static FilproOperationException Invalid(string reason) => new("restore.compatibility", reason);
}
