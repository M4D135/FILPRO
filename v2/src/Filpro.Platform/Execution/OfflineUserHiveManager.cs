using System.Collections.Concurrent;
using Filpro.Core;

namespace Filpro.Platform;

public interface IOfflineHiveNative
{
    RegistryKeyReadResult OpenPrivateCopy(string path);
    void VerifyReleased(string path);
}

/// <summary>
/// Lecture d'une copie privée NTUSER.DAT par RegLoadAppKeyW, avec repli possédé sur cette copie uniquement quand
/// RegLoadAppKeyW la refuse pour ses descripteurs (Win32 87, 1009). Aucun montage de l'original. La racine ET tous ses handles enfants sont fermés avant le déchargement.
/// </summary>
public sealed class OfflineUserHiveManager(ExecutionFileSystem files, IOfflineHiveNative native, string privateRoot)
{
    private readonly ConcurrentDictionary<string, OfflineUserHiveLease> owned = new(StringComparer.Ordinal);
    public async Task<OfflineUserHiveLease> OpenCopyAsync(string ntUserPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ntUserPath = StoragePathPolicy.NormalizeNative(ntUserPath);
        if (!Path.GetFileName(ntUserPath).Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("hive.path", "Seule la ruche NTUSER.DAT du profil est admise.");
        files.RequireFile(ntUserPath);
        files.CreatePrivateDirectory(privateRoot);
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(privateRoot, id);
        var directoryLease = files.CreateNewDirectory(directory);
        var path = Path.Combine(directory, "NTUSER.DAT");
        var transferred = false;
        var nativeAttempted = false;
        try
        {
            using (files.HoldDirectory(Path.GetDirectoryName(ntUserPath)!))
            {
                var streams = new List<(string Name, FileStream Stream)>();
                try
                {
                    // Tous les fichiers présents restent ouverts sans partage d'écriture jusqu'à la fin des copies.
                    foreach (var suffix in new[] { "", ".LOG1", ".LOG2" })
                    {
                        var source = ntUserPath + suffix;
                        if (!files.FilePresent(source)) { if (suffix.Length == 0) throw new FileNotFoundException(); continue; }
                        streams.Add(("NTUSER.DAT" + suffix, StorageFileIdentity.OpenRead(source)));
                    }
                    foreach (var (name, stream) in streams)
                    {
                        token.ThrowIfCancellationRequested();
                        await using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            65536, FileOptions.Asynchronous);
                        await stream.CopyToAsync(output, token).ConfigureAwait(false);
                        await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                    }
                }
                finally { foreach (var (_, stream) in streams) stream.Dispose(); }
            }
            token.ThrowIfCancellationRequested();
            nativeAttempted = true;
            var result = native.OpenPrivateCopy(path);
            if (result.ErrorCode != 0 || result.Key is null)
            {
                result.Key?.Dispose();
                throw new FilproOperationException("hive.load", "La copie privée de ruche n'est pas lisible (Win32 " + result.ErrorCode + "). Aucun montage de l'original.");
            }
            var releaseProven = false;
            var lease = new OfflineUserHiveLease(result.Key, () =>
            {
                try
                {
                    // Une libération prouvée ne se reprouve pas : un nouvel essai après un nettoyage partiel (fichier TxR
                    // ou dossier encore tenu) finit seulement la suppression, même si NTUSER.DAT est déjà effacée.
                    if (!releaseProven) { native.VerifyReleased(path); releaseProven = true; }
                    foreach (var file in Directory.EnumerateFiles(directory)) { files.RequireFile(file); File.Delete(file); }
                }
                finally { directoryLease.Dispose(); }
                Directory.Delete(directory, recursive: false);
                owned.TryRemove(id, out _);
            });
            if (!owned.TryAdd(id, lease)) { lease.DisposeAsync().GetAwaiter().GetResult(); throw new InvalidOperationException(); }
            transferred = true;
            if (token.IsCancellationRequested) { await lease.DisposeAsync().ConfigureAwait(false); token.ThrowIfCancellationRequested(); }
            return lease;
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    if (nativeAttempted && files.FilePresent(path)) native.VerifyReleased(path);
                    foreach (var file in Directory.EnumerateFiles(directory)) { files.RequireFile(file); File.Delete(file); }
                    directoryLease.Dispose();
                    Directory.Delete(directory, recursive: false);
                }
                finally { directoryLease.Dispose(); }
            }
        }
    }

    public async Task CleanupAsync()
    {
        List<string> failures = [];
        foreach (var lease in owned.Values)
        {
            try { await lease.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex.Message); }
        }
        if (native is WindowsOfflineHiveNative windows)
            try { windows.CleanupUntransferred(); } catch (Exception ex) { failures.Add(ex.Message); }
        if (failures.Count > 0) throw new FilproOperationException("hive.cleanup", string.Join(" ", failures));
    }
}

public sealed class OfflineUserHiveLease : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly List<OwnedKey> handles = [];
    private readonly Action released;
    private bool closed;
    private bool verified;
    internal OfflineUserHiveLease(IReadOnlyUserRegistryKey root, Action released)
    { this.released = released; Root = Own(root); }
    public IReadOnlyUserRegistryKey Root { get; }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (!closed)
            {
                closed = true;
                for (var i = handles.Count - 1; i >= 0; i--) handles[i].CloseNative();
                handles.Clear();
            }
            if (!verified) { released(); verified = true; }
        }
        return ValueTask.CompletedTask;
    }
    private OwnedKey Own(IReadOnlyUserRegistryKey key)
    { var wrapped = new OwnedKey(this, key); handles.Add(wrapped); return wrapped; }
    private sealed class OwnedKey(OfflineUserHiveLease owner, IReadOnlyUserRegistryKey native) : IReadOnlyUserRegistryValues
    {
        private bool closed;
        public RegistryKeyReadResult OpenSubKey(string relativePath)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed || owner.closed, this);
                var result = native.OpenSubKey(relativePath);
                if (result.ErrorCode != 0 || result.Key is null) { result.Key?.Dispose(); return new(result.ErrorCode == 0 ? 6 : result.ErrorCode, null); }
                return new(0, owner.Own(result.Key));
            }
        }
        public RegistryNameReadResult ReadSubKeyName(int index)
        {
            lock (owner.gate) { ObjectDisposedException.ThrowIf(closed || owner.closed, this); return native.ReadSubKeyName(index); }
        }
        public RegistryNameReadResult ReadValueName(int index)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed || owner.closed, this);
                return native is IReadOnlyUserRegistryValues values ? values.ReadValueName(index) : new(50, null);
            }
        }
        public RegistryValueReadResult ReadValue(string name, int maximumBytes)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed || owner.closed, this);
                return native is IReadOnlyUserRegistryValues values ? values.ReadValue(name, maximumBytes) : new(50, 0, null);
            }
        }
        internal void CloseNative() { if (closed) return; closed = true; native.Dispose(); }
        public void Dispose() { lock (owner.gate) CloseNative(); }
    }
}

public sealed class WindowsOfflineHiveNative : IOfflineHiveNative
{
    private static readonly OwnedCopiedHiveMounts Fallback = new(new WindowsCopiedHiveMountBackend(), WindowsNativeExecutionSafety.Block);
    public RegistryKeyReadResult OpenPrivateCopy(string path)
    {
        if (WindowsNativeExecutionSafety.BlockReason is { } reason) throw new FilproOperationException("hive.cleanup", reason);
        return OpenOrMountCopy(WindowsLoadedUserRegistry.OpenPrivateCopiedHive(path), path, Fallback);
    }

    /// <summary>Le repli ne reçoit que le chemin de la copie déjà refusé par RegLoadAppKeyW ; jamais l'original.</summary>
    internal static RegistryKeyReadResult OpenOrMountCopy(RegistryKeyReadResult appKey, string copiedPath, OwnedCopiedHiveMounts fallback)
    {
        if (appKey.ErrorCode == 0 && appKey.Key is not null) return appKey;
        appKey.Key?.Dispose();
        return UsesOwnedCopyMount(appKey.ErrorCode) ? fallback.Open(copiedPath) : new(appKey.ErrorCode, null);
    }

    /// <summary>
    /// Codes RegLoadAppKeyW pour lesquels la même copie privée reste lisible par RegLoadKeyW possédé : 87 (paramètre refusé)
    /// et 1009 (ERROR_BADDB). Windows 11 26100 rend 1009 pour tout NTUSER.DAT ouvert une fois en session, à cause du
    /// descripteur de SystemCertificates\Root\ProtectedRoots, alors que RegLoadKeyW charge la copie intacte.
    /// 5 reste un échec : RegLoadAppKeyW demande KEY_READ comme l'ouverture du montage, et une racine qui le refuse
    /// laisserait un montage illisible. 1015/1016/1017 (ruche corrompue, E/S, fichier non ruche) : RegLoadKeyW n'y changerait rien.
    /// </summary>
    internal static bool UsesOwnedCopyMount(int appKeyError) => appKeyError is 87 or 1009;
    /// <summary>Vrai tant qu'un montage possédé de ce processus porte ce tag, du chargement au déchargement confirmé.</summary>
    internal static bool OwnsLiveMount(string tag) => Fallback.Owns(tag);
    public void VerifyReleased(string path)
    {
        try
        {
            Fallback.Release(path);
            using var exclusive = StorageFileIdentity.OpenExclusiveRead(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FilproOperationException)
        {
            WindowsNativeExecutionSafety.Block("La libération d'une ruche privée reste non confirmée ; relancer après examen du nettoyage.");
            throw new FilproOperationException("hive.cleanup", "Les handles de la ruche privée sont fermés, mais sa libération exclusive n'a pas pu être confirmée. Aucune mutation supplémentaire.");
        }
    }
    public void CleanupUntransferred() => Fallback.CleanupUntransferred();
}
