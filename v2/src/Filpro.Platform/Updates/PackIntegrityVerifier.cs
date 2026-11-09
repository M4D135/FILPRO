using System.Security.Cryptography;
using Filpro.Core;

namespace Filpro.Platform;

public static class PackIntegrityVerifier
{
    /// <summary>Métadonnées bornées sans suivre de liens ; ne remplace pas ValidateAsync pour l'intégrité des contenus.</summary>
    public static PackManifest ReadManifest(string packRoot)
    {
        var root = StoragePathPolicy.NormalizeNative(packRoot);
        return PackManifestCodec.Decode(ConfigurationFileIO.Read(Path.Combine(root, PackManifestCodec.ManifestPath), PackManifestCodec.MaximumManifestBytes));
    }

    public static Task<PackValidationResult> ValidateAsync(string packRoot, string flavor,
        IProgress<PackValidationProgress>? progress = null, CancellationToken cancellationToken = default)
        => ValidateCoreAsync(packRoot, flavor, false, progress, cancellationToken);

    /// <summary>Seule l'option de configuration peut être ignorée. Ne qualifie jamais une installation ou une mise à jour.</summary>
    public static Task<PackValidationResult> ValidateForStartupAsync(string packRoot, string flavor,
        IProgress<PackValidationProgress>? progress = null, CancellationToken cancellationToken = default)
        => ValidateCoreAsync(packRoot, flavor, true, progress, cancellationToken);

    private static async Task<PackValidationResult> ValidateCoreAsync(string packRoot, string flavor, bool allowInvalidDefaults,
        IProgress<PackValidationProgress>? progress, CancellationToken cancellationToken)
    {
        var root = StoragePathPolicy.NormalizeNative(packRoot);
        using var rootLease = ConfigurationFileIO.FileSystem.HoldDirectory(root);
        ConfigurationFileIO.NoLinks(root);
        var manifestBytes = ConfigurationFileIO.Read(Path.Combine(root, PackManifestCodec.ManifestPath), PackManifestCodec.MaximumManifestBytes);
        var manifest = PackManifestCodec.Decode(manifestBytes);
        if (manifest.Flavor != flavor) throw new FilproOperationException("pack.flavor", "Le type du pack ne correspond pas à la distribution attendue.");
        var expected = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.Ordinal);
        expected.Add(PackManifestCodec.ManifestPath);
        var defaults = Path.Combine(root, PackManifestCodec.DefaultsPath);
        byte[]? defaultsBytes = null; string? defaultsWarning = null;
        if (File.Exists(defaults))
        {
            expected.Add(PackManifestCodec.DefaultsPath);
            // Les refus de liens, de droits et d'identité ne sont jamais des erreurs d'option tolérables.
            try { defaultsBytes = ConfigurationFileIO.Read(defaults, ConfigurationCodec.MaximumBytes); }
            catch (FilproOperationException ex) when (allowInvalidDefaults && ex.Code == "storage.size")
            { defaultsWarning = "Configuration fournie vide ou trop volumineuse : ignorée. Les réglages locaux sont conservés."; }
            if (defaultsBytes is not null)
            {
                try { _ = ConfigurationCodec.Decode(defaultsBytes); }
                catch (FilproOperationException) when (allowInvalidDefaults)
                { defaultsWarning = "Configuration fournie invalide : ignorée. Les réglages locaux sont conservés."; }
            }
        }
        var allowedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in expected)
        {
            var path = file;
            while (path.Contains('/')) { path = path[..path.LastIndexOf('/')]; allowedDirectories.Add(path); }
        }
        // L'unique dossier d'option peut être vide avant la première définition des défauts.
        allowedDirectories.Add("wad/config");
        // Un pack LIGHT n'a jamais de moteur dans son manifeste ; seul le Socle extrait dans wad, connu fichier par fichier, y est admis.
        var socles = manifest.Flavor == PackManifestCodec.LightFlavor ? WebViewSocle.Known : [];
        var socleFiles = socles.SelectMany(index => index.Files).Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var socleDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in socleFiles)
        {
            var path = file;
            while (path.Contains('/')) { path = path[..path.LastIndexOf('/')]; socleDirectories.Add(path); }
        }
        var socleFound = new HashSet<string>(StringComparer.Ordinal); var socleSeen = false;
        var found = new HashSet<string>(StringComparer.Ordinal); var caseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>(); directories.Push(root); var entries = 0;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            using var directoryLease = ConfigurationFileIO.FileSystem.HoldDirectory(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > PackManifestCodec.MaximumFiles * 4) throw new FilproOperationException("pack.size", "Inventaire trop volumineux.");
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!caseNames.Add(relative)) throw new FilproOperationException("pack.duplicate", "Chemins ambigus dans le pack.");
                var isDirectory = Directory.Exists(path);
                // Une entrée étrangère (desktop.ini, raccourci, zip d'origine, fichier OneDrive à la demande…) est refusée
                // en la nommant, avant les règles de lien et de chemin : le refus doit dire quoi retirer, pas accuser le manifeste.
                var socle = socles.Count != 0 && WebViewSocle.IsSoclePath(relative);
                if (socle)
                {
                    socleSeen = true;
                    if (isDirectory ? !socleDirectories.Contains(relative) : !socleFiles.Contains(relative))
                        throw SocleRefused((isDirectory ? "dossier non prévu " : "fichier non prévu ") + relative);
                }
                else if (isDirectory ? !allowedDirectories.Contains(relative) : !expected.Contains(relative))
                    throw Extra(isDirectory ? "Dossier" : "Fichier", relative);
                ConfigurationFileIO.NoLinks(path);
                if (isDirectory) directories.Push(path);
                else
                {
                    PackManifestCodec.ValidateRelativePath(relative);
                    (socle ? socleFound : found).Add(relative);
                }
            }
        }
        if (!found.SetEquals(expected)) throw new FilproOperationException("pack.missing", "Le pack est incomplet.");
        // Deux versions du Socle peuvent porter les mêmes chemins : leur contenu, lu une seule fois, départage les candidates.
        var candidates = socleSeen ? MatchSocle(socles, socleFound) : [];
        var socleBounds = candidates.Count == 0 ? [] : candidates[0].Files
            .Select(file => file with { Size = candidates.Max(index => index.Files.Single(other => other.Path == file.Path).Size) }).ToArray();
        var checkedFiles = manifest.Files.Select(file => (File: file, Socle: false))
            .Concat(socleBounds.Select(file => (File: file, Socle: true))).ToArray();
        // VerifiedFiles et VerifiedBytes restent ceux du manifeste : la preuve de démarrage les compare au manifeste seul.
        long completed = 0; var count = 0; long verifiedBytes = 0; var verifiedFiles = 0; var total = checkedFiles.Sum(item => item.File.Size);
        progress?.Report(new("Vérification du pack", 0, checkedFiles.Length, 0, total));
        foreach (var (file, socle) in checkedFiles)
        {
            var path = Path.Combine(root, file.Path); ConfigurationFileIO.NoLinks(path);
            var mismatch = socle ? SocleRefused("fichier modifié " + file.Path) : Mismatch(file.Path);
            using var fileParentLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(path)!);
            await using var stream = StorageFileIdentity.OpenRead(path, asynchronous: true);
            if (socle ? stream.Length > file.Size : stream.Length != file.Size) throw mismatch;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024]; long read = 0; int bytes;
            while ((bytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                read += bytes; if (read > file.Size) throw mismatch;
                hash.AppendData(buffer, 0, bytes);
                progress?.Report(new(file.Path, count, checkedFiles.Length, completed + read, total));
            }
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (socle)
            {
                candidates = candidates.Where(index => index.Files.Any(other => other.Path == file.Path && other.Size == read && other.Sha256 == digest)).ToList();
                if (candidates.Count == 0) throw mismatch;
            }
            else if (read != file.Size || digest != file.Sha256) throw mismatch;
            completed += read; count++; progress?.Report(new(file.Path, count, checkedFiles.Length, completed, total));
            if (!socle) { verifiedBytes += read; verifiedFiles++; }
        }
        if (ConfigurationFileIO.Hash(ConfigurationFileIO.Read(Path.Combine(root, PackManifestCodec.ManifestPath), PackManifestCodec.MaximumManifestBytes)) != ConfigurationFileIO.Hash(manifestBytes))
            throw Mismatch(PackManifestCodec.ManifestPath);
        if (defaultsBytes is not null && ConfigurationFileIO.Hash(ConfigurationFileIO.Read(defaults, ConfigurationCodec.MaximumBytes)) != ConfigurationFileIO.Hash(defaultsBytes))
            throw Mismatch(PackManifestCodec.DefaultsPath);
        return new(manifest, ConfigurationFileIO.Hash(manifestBytes), defaultsBytes is null ? null : ConfigurationFileIO.Hash(defaultsBytes), verifiedFiles, verifiedBytes, PackManifestCodec.IntegrityNotice, defaultsWarning)
            { WebViewSocleVersion = candidates.FirstOrDefault()?.Version };
    }

    /// <summary>
    /// Reporte dans la préparation d'une mise à jour LIGHT le Socle de l'installation courante, octet par octet contre son index.
    /// La version retirée garde sa propre copie : le retour arrière la restitue sans dépendre de la nouvelle.
    /// </summary>
    internal static async Task CopyWebViewSocleAsync(string source, string destination, string version,
        IProgress<PackValidationProgress>? progress, CancellationToken cancellationToken)
    {
        var index = WebViewSocle.Known.SingleOrDefault(known => known.Version == version)
            ?? throw new FilproOperationException("pack.socle", "Version du moteur WebView2 du Socle inconnue de cette application : " + version);
        ConfigurationFileIO.NoLinks(source); ConfigurationFileIO.NoLinks(destination);
        if (Directory.Exists(Path.Combine(destination, WebViewSocle.Parent)) || File.Exists(Path.Combine(destination, WebViewSocle.Parent)))
            throw new IOException("La préparation contient déjà un dossier wad\\runtime.");
        long completed = 0; var count = 0; var total = index.Files.Sum(file => file.Size);
        foreach (var file in index.Files)
        {
            var reached = completed;
            await CopyFileAsync(source, destination, file.Path, file.Size, file.Sha256, SocleRefused("fichier modifié " + file.Path),
                progress is null ? null : written => progress.Report(new(file.Path, count, index.Files.Count, reached + written, total)), cancellationToken).ConfigureAwait(false);
            completed += file.Size; count++;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, string relative, long maximum, string? expectedSha256,
        FilproOperationException mismatch, Action<long>? copied, CancellationToken cancellationToken)
    {
        var input = Path.Combine(source, relative); var output = Path.Combine(destination, relative);
        ConfigurationFileIO.NoLinks(input); ConfigurationFileIO.NoLinks(output); ConfigurationFileIO.FileSystem.CreateDirectories(Path.GetDirectoryName(output)!);
        using var sourceLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(input)!);
        using var targetLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(output)!);
        await using var reader = StorageFileIdentity.OpenRead(input, asynchronous: true);
        await using var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        if (reader.Length > maximum) throw mismatch;
        using var hash = expectedSha256 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; long written = 0; int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            written += count; if (written > maximum) throw mismatch;
            hash?.AppendData(buffer, 0, count);
            await writer.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied?.Invoke(written);
        }
        if (hash is not null && (written != maximum || Convert.ToHexStringLower(hash.GetHashAndReset()) != expectedSha256)) throw mismatch;
        writer.Flush(true);
    }

    /// <summary>Un Socle partiel ou mêlant deux versions est refusé en nommant le premier fichier qui manque ou qui est en trop.</summary>
    private static List<WebViewSocleIndex> MatchSocle(IReadOnlyList<WebViewSocleIndex> socles, HashSet<string> found)
    {
        var exact = socles.Where(index => found.SetEquals(index.Files.Select(file => file.Path))).ToList();
        if (exact.Count != 0) return exact;
        var closest = socles.OrderByDescending(index => index.Files.Count(file => found.Contains(file.Path))).First();
        var missing = closest.Files.FirstOrDefault(file => !found.Contains(file.Path))?.Path;
        throw SocleRefused(missing is not null ? "fichier manquant " + missing
            : "fichier d'une autre version " + found.Order(StringComparer.Ordinal).First(path => closest.Files.All(file => file.Path != path)));
    }

    internal static async Task CopyAsync(string source, string destination, PackValidationResult expected, CancellationToken cancellationToken)
    {
        if (expected.DefaultsWarning is not null) throw new FilproOperationException("pack.defaults", "Corriger la configuration fournie avant de distribuer ce pack.");
        ConfigurationFileIO.NoLinks(source); ConfigurationFileIO.NoLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Le dossier de préparation existe déjà.");
        using var newRootLease = ConfigurationFileIO.FileSystem.CreateNewDirectory(destination);
        foreach (var relative in expected.Manifest.Files.Select(f => f.Path).Append(PackManifestCodec.ManifestPath)
            .Concat(expected.DefaultsSha256 is null ? [] : new[] { PackManifestCodec.DefaultsPath }))
        {
            var maximum = relative == PackManifestCodec.ManifestPath ? PackManifestCodec.MaximumManifestBytes
                : relative == PackManifestCodec.DefaultsPath ? ConfigurationCodec.MaximumBytes : expected.Manifest.Files.Single(f => f.Path == relative).Size;
            await CopyFileAsync(source, destination, relative, maximum, null, Mismatch(relative), null, cancellationToken).ConfigureAwait(false);
        }
        var actual = await ValidateAsync(destination, expected.Manifest.Flavor, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (actual.ManifestSha256 != expected.ManifestSha256 || actual.DefaultsSha256 != expected.DefaultsSha256)
            throw new FilproOperationException("pack.changed", "La source a changé pendant la copie. Aucune installation n'a été remplacée.");
    }
    private static FilproOperationException Extra(string kind, string relative) => new("pack.extra",
        kind + " non prévu dans le dossier de FILPRO : " + relative + ". Extraire le zip complet dans un dossier vide réservé à FILPRO.");
    private static FilproOperationException Mismatch(string path) => new("pack.hash", "Contrôle d'intégrité échoué : " + path);
    private static FilproOperationException SocleRefused(string detail) => new("pack.socle",
        "Le moteur WebView2 extrait dans wad\\runtime\\webview2 ne correspond pas au pack du Socle Windows (" + detail
        + "). Supprimez le dossier wad\\runtime ou extrayez de nouveau le pack WebView2 du Socle dans le dossier wad de FILPRO : " + WebViewSocle.ReleaseUrl);
}
