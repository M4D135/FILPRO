using System.IO.Enumeration;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Entrée de premier niveau : attributs lus dans l'enregistrement du dossier parent, sans ouvrir l'enfant.</summary>
public readonly record struct SoftwareDirectoryEntry(string Path, FileAttributes Attributes);

/// <summary>Façade de lecture seule. Les erreurs de GetAttributes doivent rester distinctes d'une absence.</summary>
public interface ISoftwareFileSystem
{
    FileAttributes GetAttributes(string path);
    IEnumerable<SoftwareDirectoryEntry> EnumerateDirectories(string path);
}

/// <summary>Découvre les dossiers précis du catalogue puis le premier niveau AppData, sans suivre les liens observés.</summary>
public sealed class WindowsSoftwareFileReader
{
    private readonly ISoftwareFileSystem fileSystem;
    private readonly TimeProvider clock;
    private readonly SoftwareDiscoveryLimits limits;

    public WindowsSoftwareFileReader(ISoftwareFileSystem? fileSystem = null, TimeProvider? clock = null,
        SoftwareDiscoveryLimits? limits = null)
    {
        this.fileSystem = fileSystem ?? new PhysicalSoftwareFileSystem();
        this.clock = clock ?? TimeProvider.System;
        this.limits = limits ?? new SoftwareDiscoveryLimits();
        this.limits.Validate();
    }

    public SoftwareDiscoveryResult Read(string? profileRoot, IReadOnlyList<SoftwareItem> additional, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extra = SoftwareDiscoveryInputs.Normalize(additional, files: true);
        var items = new Dictionary<string, SoftwareItem>(StringComparer.OrdinalIgnoreCase);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        string root;
        try
        {
            if (string.IsNullOrWhiteSpace(profileRoot) || !Path.IsPathFullyQualified(profileRoot))
                return new([], false, "Racine du profil absente ou non absolue.");
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileRoot));
            if (string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
                return new([], false, "Une racine de volume ou de partage ne peut pas servir de profil.");
            if (!ProbeDirectory(root, cancellationToken)) return new([], false, "Dossier du profil absent.");
        }
        catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
        {
            return new([], false, "Racine du profil illisible ou contenant un lien ; aucune découverte effectuée.");
        }

        foreach (var item in SoftwareDiscoveryInputs.Merge(SoftwareDiscoveryCatalog.Files, extra))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var baseDirectory = BaseDirectory(root, item.Scope);
                var path = Path.GetFullPath(Path.Combine(baseDirectory, item.Rel.Replace('\\', Path.DirectorySeparatorChar)));
                if (!IsDescendant(baseDirectory, path)) throw new IOException("Dossier hors du périmètre AppData.");
                if (ProbeDirectory(path, cancellationToken)) items[item.Scope + "|" + item.Rel] = item;
            }
            catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
            {
                reasons.Add("Un dossier explicite est illisible ou lié ; la sélection précédente doit être conservée.");
            }
        }

        foreach (var scope in new[] { SoftwareScope.Roaming, SoftwareScope.Local })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseDirectory = BaseDirectory(root, scope);
            try
            {
                if (!ProbeDirectory(baseDirectory, cancellationToken)) continue;
                var start = clock.GetTimestamp();
                using var iterator = fileSystem.EnumerateDirectories(baseDirectory).GetEnumerator();
                var count = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count >= limits.MaximumGenericEntries || clock.GetElapsedTime(start) >= limits.Duration)
                    {
                        reasons.Add("Découverte AppData limitée en nombre ou en durée ; inventaire partiel.");
                        break;
                    }
                    if (!iterator.MoveNext()) break;
                    cancellationToken.ThrowIfCancellationRequested();
                    count++;
                    if (clock.GetElapsedTime(start) >= limits.Duration)
                    {
                        reasons.Add("Découverte AppData limitée en durée ; inventaire partiel.");
                        break;
                    }
                    var entry = iterator.Current;
                    // Nom et premier niveau lus sur le chemin brut de l'énumération, avant GetFullPath : sous Windows, celui-ci
                    // retire le point ou l'espace final d'un nom et, avant Windows 11, change un nom d'appareil (NUL, COM1.bak…)
                    // en \\.\NUL. Ces noms doivent être tus, pas contrôlés sous un autre nom ni pris pour une sortie du premier niveau.
                    var relative = Path.GetFileName(entry.Path);
                    if (relative is "" or "." or ".." || !string.Equals(Path.GetDirectoryName(entry.Path), baseDirectory, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Résultat d'énumération hors du premier niveau AppData.");
                    if (SoftwareDiscoveryCatalog.IsGenericFileExcluded(scope, relative)
                        || extra.Any(item => item.Scope == scope && SoftwareDiscoveryInputs.Overlaps(item.Rel, relative))) continue;
                    SoftwareItem item;
                    try
                    {
                        item = SoftwareDiscoveryInputs.Normalize([
                            new SoftwareItem(scope, relative, relative, SoftwareCatalog.Classify(relative), false, false, false, null, false, false),
                        ], files: true).Single();
                    }
                    catch (FilproOperationException)
                    {
                        // Nom qu'aucune règle USMT ne peut écrire (nom d'appareil réservé, caractère interdit, fin par une espace
                        // ou un point) : ce dossier, lien compris, ne peut jamais être retenu, donc le taire ne fait rien perdre.
                        // Il ne rend pas l'inventaire partiel, ce qui bloquerait les choix et la sauvegarde du profil.
                        continue;
                    }
                    // Un nom que le codec réécrit (demi-paire UTF-16 isolée devenue U+FFFD) désignerait un autre dossier : tu de même.
                    if (!string.Equals(item.Rel, relative, StringComparison.Ordinal)) continue;
                    var path = Path.GetFullPath(entry.Path);
                    // Avant Windows 11, un nom d'appareil que le codec laisse passer (« NUL .txt ») devient aussi \\.\NUL : ce
                    // dossier n'a pas de chemin Win32 et se tait de même. Une racine déjà en \\.\ n'est pas concernée.
                    if (path.StartsWith(@"\\.\", StringComparison.Ordinal) && !entry.Path.StartsWith(@"\\.\", StringComparison.Ordinal)) continue;
                    if (!IsDescendant(baseDirectory, path) || !string.Equals(Path.GetDirectoryName(path), baseDirectory, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Résultat d'énumération hors du premier niveau AppData.");
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Un lien générique ne peut pas être sélectionné : l'exclure sans bloquer les autres dossiers.
                        // Ne jamais l'interroger ni le suivre. Les chemins explicites ou mémorisés et ceux du catalogue
                        // ont déjà été contrôlés séparément par ProbeDirectory et restent bloquants s'ils sont liés.
                        continue;
                    }
                    if (ProbeDirectory(path, cancellationToken)) items.TryAdd(scope + "|" + relative, item);
                    else reasons.Add("Un dossier énuméré est devenu absent ; inventaire partiel.");
                }
            }
            catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
            {
                reasons.Add("Découverte AppData interrompue par un accès refusé, un lien ou une erreur de lecture.");
            }
        }
        try
        {
            if (!ProbeDirectory(root, cancellationToken)) reasons.Add("Le dossier du profil a disparu pendant la lecture ; inventaire partiel.");
        }
        catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
        {
            reasons.Add("Le dossier du profil n'est plus accessible sans lien ; inventaire partiel.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new([.. items.Values], reasons.Count == 0, reasons.Count == 0 ? null : string.Join(" ", reasons));
    }

    private static string BaseDirectory(string root, SoftwareScope scope) => Path.Combine(root, "AppData", scope == SoftwareScope.Roaming ? "Roaming" : "Local");

    private static bool IsDescendant(string parent, string path) => path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private bool ProbeDirectory(string path, CancellationToken cancellationToken)
    {
        // Vérifier les ancêtres du haut vers le bas : ne pas résoudre un enfant sous une jonction déjà observée.
        var prefix = Path.GetPathRoot(path) ?? throw new IOException("Racine de chemin indisponible.");
        var current = prefix;
        var components = path[prefix.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var index = -1; index < components.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index >= 0) current = Path.Combine(current, components[index]);
            FileAttributes attributes;
            try { attributes = fileSystem.GetAttributes(current); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Lien ou jonction refusé.");
            if ((attributes & FileAttributes.Directory) == 0) throw new IOException("Un composant n'est pas un dossier.");
        }
        return true;
    }

    private sealed class PhysicalSoftwareFileSystem : ISoftwareFileSystem
    {
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        // Même périmètre que Directory.EnumerateDirectories (options Compatible) ; les attributs viennent de la même lecture.
        public IEnumerable<SoftwareDirectoryEntry> EnumerateDirectories(string path) =>
            new FileSystemEnumerable<SoftwareDirectoryEntry>(path,
                static (ref FileSystemEntry entry) => new SoftwareDirectoryEntry(entry.ToFullPath(), entry.Attributes),
                new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, MatchType = MatchType.Win32 })
            { ShouldIncludePredicate = static (ref FileSystemEntry entry) => entry.IsDirectory };
    }
}
