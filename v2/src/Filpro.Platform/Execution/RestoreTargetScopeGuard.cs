using Filpro.Core;

namespace Filpro.Platform;

/// <summary>
/// Vérifie uniquement les portées fermées qui vont être réappliquées. Une portée absente peut être créée par USMT,
/// mais un lien, un fichier EFS ou un accès illisible ne devient jamais une destination supposée sûre.
/// Les dossiers existants restent tenus sans partage de suppression pendant LoadState sous Windows.
/// </summary>
public sealed class RestoreTargetScopeGuard(ExecutionFileSystem files)
{
    public IDisposable Acquire(string root, BackupManifestProfile saved, CancellationToken token,
        int maximumEntries = 2_000_000, int maximumDirectories = 16_384)
    {
        root = StoragePathPolicy.NormalizeNative(root);
        var lease = new ScopeLease();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = (saved.ExcludedPaths ?? []).Select(UsmtMigrationRules.RequireExcludedRelative).ToArray();
        var count = 0;
        try
        {
            Hold(root);
            foreach (var entry in Directory.EnumerateFiles(root))
            {
                if (Path.GetFileName(entry).StartsWith("NTUSER", StringComparison.OrdinalIgnoreCase)) continue;
                Inspect(entry, recursive: false);
            }
            foreach (var folder in saved.PersonalFolders ?? []) Inspect(ExecutionFileSystem.Resolve(root, folder), recursive: true, directoryScope: true);
            foreach (var item in saved.Selection.Files)
            {
                if (item.Scope is not (SoftwareScope.Local or SoftwareScope.Roaming))
                    throw Invalid("Portée de fichiers du magasin invalide.");
                var relative = "AppData/" + (item.Scope == SoftwareScope.Local ? "Local/" : "Roaming/") + item.Rel.Replace('\\', '/');
                Inspect(ExecutionFileSystem.Resolve(root, relative), recursive: true, directoryScope: true);
            }
            token.ThrowIfCancellationRequested();
            return lease;
        }
        catch { lease.Dispose(); throw; }

        void Inspect(string path, bool recursive, bool directoryScope = false, int depth = 0)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 128) throw Invalid("La profondeur de la cible dépasse 128 niveaux ; aucune application USMT.");
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (excluded.Any(e => relative.Equals(e, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(e + "/", StringComparison.OrdinalIgnoreCase))) return;
            if (++count > maximumEntries) throw Invalid("Le contrôle complet de la cible dépasse sa borne ; aucune application USMT.");
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { HoldNearestParent(path); return; }
            catch (DirectoryNotFoundException) { HoldNearestParent(path); return; }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!Hold(path)) return;
                if (recursive)
                    foreach (var child in Directory.EnumerateFileSystemEntries(path)) Inspect(child, recursive: true, depth: depth + 1);
            }
            else
            {
                if (directoryScope) throw Invalid("Un dossier de destination a été remplacé par un fichier.");
                files.RequireFile(path);
                if ((attributes & FileAttributes.Encrypted) != 0) throw Invalid("Un fichier EFS existe dans une portée cible.");
                using var checkedFile = StorageFileIdentity.OpenRead(path);
            }
        }
        bool Hold(string directory)
        {
            if (visited.Contains(directory)) return false;
            if (visited.Count == maximumDirectories) throw Invalid("Trop de dossiers à maintenir stables pendant la restauration.");
            var held = files.HoldDirectory(directory);
            visited.Add(directory); lease.Add(held);
            return true;
        }
        void HoldNearestParent(string path)
        {
            for (var parent = Path.GetDirectoryName(path); parent is not null && StoragePathPolicy.IsWithin(parent, root); parent = Path.GetDirectoryName(parent))
            {
                try { Hold(parent); return; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            throw Invalid("La racine cible a disparu pendant le contrôle.");
        }
    }

    private static FilproOperationException Invalid(string message) => new("restore.path", message);
    private sealed class ScopeLease : IDisposable
    {
        private readonly List<IDisposable> held = [];
        public void Add(IDisposable lease) => held.Add(lease);
        public void Dispose() { for (var index = held.Count - 1; index >= 0; index--) held[index].Dispose(); held.Clear(); }
    }
}
