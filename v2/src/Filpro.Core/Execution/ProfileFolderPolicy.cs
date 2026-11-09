using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>Développe les chemins du compte visé, jamais ceux de l'administrateur qui exécute FILPRO.</summary>
public static partial class ProfileFolderPolicy
{
    public static IReadOnlyList<string> Validate(string profilePath, IReadOnlyDictionary<string, string> values)
    {
        var root = StoragePathPolicy.NormalizeWindows(profilePath);
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || values.Count is < 2 or > 512) throw Invalid();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || !names.TryAdd(name, Expand(value, root))) throw Invalid();
        }
        if (!names.TryGetValue("AppData", out var roaming) || !names.TryGetValue("Local AppData", out var local)
            || !roaming.Equals(root + @"\AppData\Roaming", StringComparison.OrdinalIgnoreCase)
            || !local.Equals(root + @"\AppData\Local", StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("profile.redirected", "AppData est absent, redirigé ou non résolu ; les sélections ne peuvent pas être appliquées au chemin standard.");
        return names.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Expand(string value, string root)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) throw Invalid();
        var expanded = Variables().Replace(value, match => match.Groups[1].Value.ToUpperInvariant() switch
        {
            "USERPROFILE" => root,
            "HOMEDRIVE" => root[..2],
            "HOMEPATH" => root[2..],
            "APPDATA" => root + @"\AppData\Roaming",
            "LOCALAPPDATA" => root + @"\AppData\Local",
            _ => throw Invalid(),
        });
        if (expanded.Contains('%')) throw Invalid();
        var path = StoragePathPolicy.NormalizeWindows(expanded);
        if (!StoragePathPolicy.IsWithin(path, root, false))
            throw new FilproOperationException("profile.redirected", "Un dossier personnel sort du profil local ; la migration automatique est refusée.");
        return path;
    }

    private static FilproOperationException Invalid() => new("profile.redirected", "Emplacement utilisateur inconnu ou variable non résolue dans le compte cible.");
    [GeneratedRegex("%([^%]+)%", RegexOptions.CultureInvariant)] private static partial Regex Variables();
}
