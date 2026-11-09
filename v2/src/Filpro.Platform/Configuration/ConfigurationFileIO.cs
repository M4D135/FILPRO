using System.Security.Cryptography;

namespace Filpro.Platform;

/// <summary>E/S bornées, sans suivi de liens. Les chemins sont revalidés immédiatement avant publication.</summary>
internal static class ConfigurationFileIO
{
    internal static ExecutionFileSystem FileSystem { get; } = new();
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void NoLinks(string path)
    {
        var full = Filpro.Core.StoragePathPolicy.NormalizeNative(path, true);
        for (var cursor = full; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                var attributes = File.GetAttributes(cursor);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Device | (FileAttributes)0x40000 | (FileAttributes)0x400000)) != 0)
                    throw new Filpro.Core.FilproOperationException("storage.link", "Lien, fichier distant à la demande ou répertoire spécial refusé.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var cloud = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(cloud) && (full.Equals(cloud.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(cloud.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase)))
                    throw new Filpro.Core.FilproOperationException("storage.cloud", "Le stockage OneDrive est exclu.");
            }
        }
    }

    internal static byte[] Read(string path, int maximum)
    {
        using var lease = FileSystem.HoldDirectory(Path.GetDirectoryName(Filpro.Core.StoragePathPolicy.NormalizeNative(path))!);
        NoLinks(path);
        using var stream = StorageFileIdentity.OpenRead(path);
        if (stream.Length < 1 || stream.Length > maximum) throw new Filpro.Core.FilproOperationException("storage.size", "Fichier vide ou trop volumineux.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Le fichier a changé pendant la lecture.");
        return bytes;
    }

    internal static string? AtomicWrite(string path, byte[] bytes, string backupRoot, string? expectedHash, bool expectAbsent = false)
    {
        NoLinks(path); NoLinks(backupRoot);
        FileSystem.CreateDirectories(Path.GetDirectoryName(path)!); FileSystem.CreateDirectories(backupRoot);
        using var targetLease = FileSystem.HoldDirectory(Path.GetDirectoryName(path)!);
        using var backupLease = FileSystem.HoldDirectory(backupRoot);
        NoLinks(path); NoLinks(backupRoot);
        if (expectAbsent && File.Exists(path)) throw Stale();
        var exists = File.Exists(path);
        if (expectedHash is not null && (!exists || Hash(Read(path, 1024 * 1024)) != expectedHash)) throw Stale();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = exists ? Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json") : null;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            if (Hash(Read(temporary, 1024 * 1024)) != Hash(bytes)) throw new IOException("Écriture temporaire non conforme.");
            NoLinks(path);
            if (expectedHash is not null && Hash(Read(path, 1024 * 1024)) != expectedHash) throw Stale();
            if (exists)
            {
                var original = Read(path, 1024 * 1024);
                if (expectedHash is not null && Hash(original) != expectedHash) throw Stale();
                using (var saved = new FileStream(backup!, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { saved.Write(original); saved.Flush(flushToDisk: true); }
                if (Hash(Read(backup!, 1024 * 1024)) != Hash(original)) throw new IOException("La sauvegarde préalable n'est pas conforme.");
                if (Hash(Read(path, 1024 * 1024)) != Hash(original)) throw Stale();
                File.Replace(temporary, path, null, ignoreMetadataErrors: false);
            }
            else File.Move(temporary, path, overwrite: false);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static IDisposable Lock(string path)
    {
        NoLinks(path); FileSystem.CreateDirectories(Path.GetDirectoryName(path)!); NoLinks(path);
        var lease = FileSystem.HoldDirectory(Path.GetDirectoryName(path)!);
        try { return new PinnedLock(StorageFileIdentity.OpenLock(path), lease); }
        catch (IOException) { lease.Dispose(); throw new Filpro.Core.FilproOperationException("storage.busy", "Une autre instance utilise ce document. Réessayer après la fin de son opération."); }
        catch { lease.Dispose(); throw; }
    }
    private sealed class PinnedLock(FileStream stream, IDisposable lease) : IDisposable
    { public void Dispose() { stream.Dispose(); lease.Dispose(); } }
    internal static Filpro.Core.FilproOperationException Stale() => new("configuration.stale", "La configuration ou le fichier a changé. Refaire l'aperçu avant de confirmer.");
}
