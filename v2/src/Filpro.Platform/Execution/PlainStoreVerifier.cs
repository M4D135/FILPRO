using System.Text;
using Filpro.Core;

namespace Filpro.Platform;

public sealed record PlainStoreCapture(ManifestFileReference Index, BackupCatalog Catalog, long Bytes);
public sealed record PlainStoreDescriptor(string Id, string Store, ManifestFileReference? Payload = null,
    string Verification = "FileHashesVerified");

/// <summary>Preuve SHA-256 distincte de /verify (qui n'existe que pour un MIG compressé).</summary>
public sealed class PlainStoreVerifier(ExecutionFileSystem files)
{
    public Task<PlainStoreCapture> CaptureAsync(string bundle, BackupManifestProfile profile, CancellationToken token, string? indexBundle = null)
        => CaptureAsync(bundle, new PlainStoreDescriptor(profile.Id, profile.Store, profile.Payload, profile.Verification), token, indexBundle);

    public async Task<PlainStoreCapture> CaptureAsync(string bundle, PlainStoreDescriptor profile, CancellationToken token, string? indexBundle = null)
    {
        RequireDescriptor(profile);
        var store = ExecutionFileSystem.Resolve(bundle, profile.Store);
        using var fileDirectory = files.HoldDirectory(Path.Combine(store, "USMT", "File"));
        var indexRelative = "Indexes/" + profile.Id + ".jsonl";
        indexBundle ??= bundle;
        files.CreateDirectories(Path.Combine(indexBundle, "Indexes"));
        var path = ExecutionFileSystem.Resolve(indexBundle, indexRelative);
        using var indexDirectory = files.HoldDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using var inputs = new ExecutionInputLease(files);
        var entries = new List<BackupCatalogEntry>();
        var catalogBytes = 256;
        var complete = true;
        long totalBytes = 0;
        var count = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await using (var writer = new StreamWriter(output, new UTF8Encoding(false, true), 65536, leaveOpen: true))
                {
                    PlainStoreIndexCodec.WriteHeader(writer, profile.Id);
                    foreach (var relative in EnumerateFiles(store, token))
                    {
                        if (++count > PlainStoreIndexCodec.MaximumFiles) throw Bounds();
                        var entry = await inputs.MeasureAndHoldAsync(store, relative, token).ConfigureAwait(false);
                        totalBytes = checked(totalBytes + entry.Bytes);
                        PlainStoreIndexCodec.WriteEntry(writer, entry);
                        if (entry.Path.StartsWith("USMT/File/", StringComparison.Ordinal))
                        {
                            var name = entry.Path[10..];
                            var item = new BackupCatalogEntry(ExecutionCatalogCodec.EntryId(profile.Id, name), profile.Id,
                                name, entry.Bytes, false, entry.Path);
                            var cost = ExecutionCatalogCodec.EntryByteCost(item);
                            if (entries.Count == ExecutionCatalogCodec.MaximumEntries || catalogBytes + cost > ExecutionCatalogCodec.MaximumBytes) complete = false;
                            else { entries.Add(item); catalogBytes += cost; }
                        }
                    }
                    PlainStoreIndexCodec.WriteFooter(writer, count, totalBytes);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                if (output.Length > PlainStoreIndexCodec.MaximumBytes) throw Bounds();
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
            return new(await files.DescribeAsync(indexBundle, indexRelative, token).ConfigureAwait(false), new(complete, entries), totalBytes);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task<PlainStoreLease> VerifyAndHoldAsync(string bundle, BackupManifestProfile profile, CancellationToken token, string? indexBundle = null)
        => VerifyAndHoldAsync(bundle, new PlainStoreDescriptor(profile.Id, profile.Store, profile.Payload, profile.Verification), token, indexBundle);

    public async Task<PlainStoreLease> VerifyAndHoldAsync(string bundle, PlainStoreDescriptor profile, CancellationToken token, string? indexBundle = null)
    {
        RequireDescriptor(profile);
        if (profile.Payload is null || profile.Payload.Path != "Indexes/" + profile.Id + ".jsonl"
            || profile.Payload.Bytes > PlainStoreIndexCodec.MaximumBytes || profile.Verification != "FileHashesVerified")
            throw new FilproOperationException("store.index", "Preuve non compressée absente.");
        var held = new ExecutionInputLease(files);
        try
        {
            var index = ExecutionFileSystem.Resolve(indexBundle ?? bundle, profile.Payload.Path);
            await held.HoldAsync(index, profile.Payload.Sha256, profile.Payload.Bytes, token).ConfigureAwait(false);
            var store = ExecutionFileSystem.Resolve(bundle, profile.Store);
            using var fileDirectory = files.HoldDirectory(Path.Combine(store, "USMT", "File"));
            using var stream = StorageFileIdentity.OpenRead(index);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, 65536);
            using var actual = EnumerateFiles(store, token).GetEnumerator();
            var entries = new Dictionary<string, ManifestFileReference>(StringComparer.OrdinalIgnoreCase);
            foreach (var expected in PlainStoreIndexCodec.Read(reader, profile.Id, token))
            {
                token.ThrowIfCancellationRequested();
                if (!actual.MoveNext() || !actual.Current.Equals(expected.Path, StringComparison.Ordinal))
                    throw new FilproOperationException("store.integrity", "Le jeu de fichiers du magasin a changé ; aucun LoadState.");
                await held.HoldAsync(ExecutionFileSystem.Resolve(store, expected.Path), expected.Sha256, expected.Bytes, token).ConfigureAwait(false);
                entries.Add(expected.Path, expected);
            }
            if (actual.MoveNext()) throw new FilproOperationException("store.integrity", "Le magasin contient un fichier non indexé.");
            token.ThrowIfCancellationRequested();
            return new(held, entries);
        }
        catch { held.Dispose(); throw; }
    }

    private static void RequireDescriptor(PlainStoreDescriptor profile)
    {
        if (!Guid.TryParseExact(profile.Id, "N", out _) || BackupManifestCodec.RequireRelative(profile.Store) != profile.Store)
            throw new FilproOperationException("store.index", "Identifiant ou emplacement du magasin non compressé invalide.");
    }

    private IEnumerable<string> EnumerateFiles(string store, CancellationToken token)
    {
        var entries = 0;
        var directories = 0;
        return Visit(store, 0);

        IEnumerable<string> Visit(string directory, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++directories > 16_384 || depth > 128) throw Bounds();
            using var lease = files.HoldDirectory(directory);
            var children = Directory.EnumerateFileSystemEntries(directory).Take(PlainStoreIndexCodec.MaximumFiles + 16_385).ToArray();
            if (children.Length > PlainStoreIndexCodec.MaximumFiles + 16_384) throw Bounds();
            Array.Sort(children, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)));
            string? last = null;
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(child);
                if (name.Equals(last, StringComparison.OrdinalIgnoreCase)) throw new FilproOperationException("store.index", "Deux noms du magasin sont ambigus sous Windows.");
                last = name;
                var relative = BackupManifestCodec.RequireRelative(Path.GetRelativePath(store, child).Replace(Path.DirectorySeparatorChar, '/'));
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (var path in Visit(child, depth + 1)) yield return path;
                }
                else
                {
                    if (++entries > PlainStoreIndexCodec.MaximumFiles || !relative.StartsWith("USMT/", StringComparison.Ordinal)) throw Bounds();
                    files.RequireFile(child); yield return relative;
                }
            }
        }
    }
    private static FilproOperationException Bounds() => new("store.index", "Magasin non compressé hors des bornes de vérification (100 000 fichiers, 16 384 dossiers, profondeur 128) ; choisir un magasin compressé.");
}

public sealed class PlainStoreLease(ExecutionInputLease inputs, IReadOnlyDictionary<string, ManifestFileReference> entries) : IDisposable
{
    public IReadOnlyDictionary<string, ManifestFileReference> Entries { get; } = entries;
    public void Dispose() => inputs.Dispose();
}
