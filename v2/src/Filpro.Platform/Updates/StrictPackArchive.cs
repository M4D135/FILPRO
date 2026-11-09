using System.IO.Compression;
using System.Security.Cryptography;
using Filpro.Core;

namespace Filpro.Platform;

public static class StrictPackArchive
{
    public static async Task<PackValidationResult> ExtractAsync(string archivePath, string destination,
        string expectedSha256, IProgress<PackValidationProgress>? progress = null, CancellationToken cancellationToken = default,
        string expectedFlavor = PackManifestCodec.ProductionFlavor)
    {
        if (!PackManifestCodec.IsProductionFlavor(expectedFlavor)) throw Invalid("Seul un pack de production peut être installé.");
        if (!PackManifestCodec.IsSha256(expectedSha256)) throw Invalid("Somme SHA-256 complète requise.");
        var archive = StoragePathPolicy.NormalizeNative(archivePath); var output = StoragePathPolicy.NormalizeNative(destination);
        ConfigurationFileIO.NoLinks(archive); ConfigurationFileIO.NoLinks(output);
        using var sourceLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(archive)!);
        if (File.Exists(output) || Directory.Exists(output)) throw Invalid("La destination temporaire doit être nouvelle.");
        await using var stream = StorageFileIdentity.OpenRead(archive, asynchronous: true);
        if (stream.Length is < 1 or > PackManifestCodec.MaximumPackBytes) throw Invalid("Taille d'archive refusée.");
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) != expectedSha256)
            throw Invalid("La somme SHA-256 de l'archive ne correspond pas.");
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count is < 4 or > PackManifestCodec.MaximumFiles * 3) throw Invalid("Nombre d'entrées refusé.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var directoryNames = new List<string>(); long total = 0;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = entry.FullName.EndsWith('/'); var path = directory ? entry.FullName.TrimEnd('/') : entry.FullName;
            if (path == "wad" && directory) { }
            else PackManifestCodec.ValidateRelativePath(path);
            if (entry.FullName.Contains('\\') || !names.Add(path) || (directory && entry.FullName != path + "/")) throw Invalid("Entrée ambiguë ou en double.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if (unixType is not (0 or 0x8000 or 0x4000) || ((entry.ExternalAttributes & 0x400) != 0)
                || ((entry.ExternalAttributes & 0x10) != 0 && !directory)
                || (unixType == 0x4000 && !directory) || (unixType == 0x8000 && directory)) throw Invalid("Liens et entrées spéciales interdits.");
            if (directory)
            {
                if (entry.Length != 0) throw Invalid("Dossier ZIP porteur de contenu.");
                directoryNames.Add(path); continue;
            }
            if (entry.Length < 0 || entry.Length > PackManifestCodec.MaximumFileBytes || total > PackManifestCodec.MaximumPackBytes - entry.Length
                || (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 250)) throw Invalid("Archive trop volumineuse ou taux de compression excessif.");
            total += entry.Length; files.Add(path, entry);
        }
        if (!files.TryGetValue(PackManifestCodec.ManifestPath, out var manifestEntry) || manifestEntry.Length > PackManifestCodec.MaximumManifestBytes) throw Invalid("Manifeste absent ou trop volumineux.");
        var manifest = PackManifestCodec.Decode(await ReadEntryAsync(manifestEntry, PackManifestCodec.MaximumManifestBytes, cancellationToken).ConfigureAwait(false));
        if (manifest.Flavor != expectedFlavor || PackageVersion.Parse(manifest.Version).Prerelease is not null)
            throw Invalid($"Le manifeste doit identifier un pack {expectedFlavor} stable, de la même édition que l’installation.");
        var expected = manifest.Files.Select(file => file.Path).Append(PackManifestCodec.ManifestPath).ToHashSet(StringComparer.Ordinal);
        if (files.ContainsKey(PackManifestCodec.DefaultsPath))
        {
            _ = ConfigurationCodec.Decode(await ReadEntryAsync(files[PackManifestCodec.DefaultsPath], ConfigurationCodec.MaximumBytes, cancellationToken).ConfigureAwait(false));
            expected.Add(PackManifestCodec.DefaultsPath);
        }
        if (!expected.SetEquals(files.Keys)) throw Invalid("Inventaire ZIP différent du manifeste.");
        foreach (var directory in directoryNames)
            if (!expected.Any(path => path.StartsWith(directory + "/", StringComparison.Ordinal)) && directory != "wad/config") throw Invalid("Dossier ZIP non prévu.");
        foreach (var entry in manifest.Files) if (files[entry.Path].Length != entry.Size) throw Invalid("Taille différente du manifeste.");
        using var stageLease = ConfigurationFileIO.FileSystem.CreateNewDirectory(output);
        var complete = 0; long bytesDone = 0;
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(output, entry.Key); ConfigurationFileIO.NoLinks(path);
            if (!StoragePathPolicy.IsWithin(path, output, false)) throw Invalid("Traversée de répertoire.");
            ConfigurationFileIO.FileSystem.CreateDirectories(Path.GetDirectoryName(path)!); ConfigurationFileIO.NoLinks(path);
            using var parentLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(path)!);
            using var reader = entry.Value.Open();
            await using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = new byte[128 * 1024]; long written = 0; int size;
            while ((size = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                written += size; if (written > entry.Value.Length) throw Invalid("Longueur décompressée non conforme.");
                await writer.WriteAsync(buffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
                progress?.Report(new(entry.Key, complete, files.Count, bytesDone + written, total));
            }
            if (written != entry.Value.Length) throw Invalid("Entrée ZIP tronquée.");
            writer.Flush(true); bytesDone += written; complete++; progress?.Report(new(entry.Key, complete, files.Count, bytesDone, total));
        }
        return await PackIntegrityVerifier.ValidateAsync(output, expectedFlavor, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, int maximum, CancellationToken token)
    {
        if (entry.Length is < 1 || entry.Length > maximum) throw Invalid("Entrée JSON trop volumineuse.");
        using var reader = entry.Open(); var bytes = new byte[checked((int)entry.Length)]; await reader.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (reader.ReadByte() != -1) throw Invalid("Entrée JSON plus longue qu'annoncé.");
        return bytes;
    }
    private static FilproOperationException Invalid(string message) => new("update.archive", message);
}
