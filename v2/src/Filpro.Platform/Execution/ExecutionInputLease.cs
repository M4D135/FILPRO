using System.Security.Cryptography;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>
/// Garde les fichiers effectivement hashés ouverts sans partage d'écriture jusqu'à la fin de l'outil.
/// Les règles venant d'un magasin sont copiées dans le répertoire privé de l'opération, puis relues et gardées.
/// </summary>
public sealed class ExecutionInputLease(ExecutionFileSystem files) : IDisposable
{
    private readonly List<IDisposable> held = [];
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public async Task<string> HoldAsync(string path, string expectedSha256, long? expectedBytes, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        path = StoragePathPolicy.NormalizeNative(path);
        var parentPath = Path.GetDirectoryName(path)!;
        var parent = directories.Contains(parentPath) ? null : files.HoldDirectory(parentPath);
        FileStream? stream = null;
        try
        {
            files.RequireFile(path);
            stream = StorageFileIdentity.OpenRead(path, asynchronous: true);
            var size = stream.Length;
            if (expectedBytes is not null && size != expectedBytes)
                throw new FilproOperationException("store.integrity", "La taille d'une entrée USMT a changé.");
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            if (stream.Length != size || !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new FilproOperationException("store.integrity", "Une entrée USMT a changé après sa revue.");
            token.ThrowIfCancellationRequested();
            if (parent is not null) { directories.Add(parentPath); held.Add(parent); }
            held.Add(stream);
            stream = null;
            return path;
        }
        catch { stream?.Dispose(); parent?.Dispose(); throw; }
    }

    public async Task<ManifestFileReference> MeasureAndHoldAsync(string root, string relative, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var path = ExecutionFileSystem.Resolve(root, relative);
        var parentPath = Path.GetDirectoryName(path)!;
        var parent = directories.Contains(parentPath) ? null : files.HoldDirectory(parentPath);
        FileStream? stream = null;
        try
        {
            files.RequireFile(path);
            stream = StorageFileIdentity.OpenRead(path, asynchronous: true);
            var size = stream.Length;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            if (stream.Length != size) throw new FilproOperationException("store.integrity", "Un fichier du magasin a changé pendant son indexation.");
            token.ThrowIfCancellationRequested();
            if (parent is not null) { directories.Add(parentPath); held.Add(parent); }
            held.Add(stream); stream = null;
            return new(BackupManifestCodec.RequireRelative(relative), hash, size);
        }
        catch { stream?.Dispose(); parent?.Dispose(); throw; }
    }

    public async Task<string> CopyRuleAsync(string bundle, ManifestFileReference reference, string privateDirectory,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BackupManifestCodec.ValidateReference(reference);
        if (reference.Bytes > BackupManifestCodec.MaximumBytes || !reference.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("store.rules", "Entrée de règle USMT hors limites.");
        var source = ExecutionFileSystem.Resolve(bundle, reference.Path);
        using var input = new ExecutionInputLease(files);
        await input.HoldAsync(source, reference.Sha256, reference.Bytes, token).ConfigureAwait(false);
        var destination = Path.Combine(StoragePathPolicy.NormalizeNative(privateDirectory), Path.GetFileName(source));
        files.CopyNew(source, destination);
        return await HoldAsync(destination, reference.Sha256, reference.Bytes, token).ConfigureAwait(false);
    }

    public async Task<string> CopyConfigurationAsync(string bundle, ManifestFileReference reference, string privateDirectory,
        CancellationToken token)
    {
        if (reference.Bytes > BackupManifestCodec.MaximumBytes) throw new FilproOperationException("store.rules", "Configuration USMT hors limites.");
        var source = ExecutionFileSystem.Resolve(bundle, reference.Path);
        using var input = new ExecutionInputLease(files);
        await input.HoldAsync(source, reference.Sha256, reference.Bytes, token).ConfigureAwait(false);
        var canonical = UsmtProfileConfiguration.Restrict(files.ReadBounded(source, BackupManifestCodec.MaximumBytes), requireWindowsAlreadyExcluded: true);
        var target = Path.Combine(StoragePathPolicy.NormalizeNative(privateDirectory), Path.GetFileName(source));
        files.WriteAtomic(target, canonical, false);
        return await HoldAsync(target, BackupManifestCodec.Hash(canonical), canonical.Length, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (var index = held.Count - 1; index >= 0; index--) held[index].Dispose();
        held.Clear();
        directories.Clear();
    }
}
