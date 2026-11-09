using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class WindowsFilproExecutionService
{
    private sealed record PreparedCatalog(string Bundle, string ManifestSha256, string StoreId, BackupCatalog Catalog,
        ManifestFileReference? V1Payload, string? V1PayloadRoot);
    private readonly object catalogGate = new();
    private readonly Dictionary<string, PreparedCatalog> preparedCatalogs = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ExecutionJobSnapshot> StartCatalogAsync(StoreCatalogRequest request, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        RequireStart(false, Guid.NewGuid().ToString("N"), clock.GetUtcNow(), start, cancellationToken);
        if (!Capabilities.Recover) throw new FilproOperationException("execution.usmt", "Un pack USMT vérifié est requis pour préparer le catalogue.");
        var store = await stores.ReadAsync(request.Bundle, cancellationToken).ConfigureAwait(false);
        if (!store.Inspection.Store.Ready) throw new FilproOperationException("store.state", "Le magasin n'est pas exploitable.");
        if (request.ExpectedManifestSha256 is { } expected
            && !string.Equals(expected, store.Inspection.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("store.changed", "Le magasin a changé depuis sa confirmation : relisez-le avant de recommencer.");
        var key = request.KeyPath is null ? null : StoragePathPolicy.NormalizeWindows(request.KeyPath);
        if (store.Inspection.Store.Encrypted && key is null) throw new FilproOperationException("usmt.crypto", "Fichier clé requis.");
        return jobs.Start(ExecutionKind.Catalog, "Préparation du catalogue de fichiers", start,
            context => WithOperationAsync(context, () => PrepareCatalogAsync(store.Inspection.Bundle, store.Inspection.ManifestSha256, key, context)), cancellationToken);
    }

    private async Task<NativeBackupStore> ReadStoreAsync(string bundle, CancellationToken token)
    {
        var store = await stores.ReadAsync(bundle, token).ConfigureAwait(false);
        PreparedCatalog? prepared;
        lock (catalogGate) preparedCatalogs.TryGetValue(store.Inspection.Bundle, out prepared);
        if (prepared is null || prepared.ManifestSha256 != store.Inspection.ManifestSha256 || prepared.StoreId != store.Inspection.Id) return store;
        return store with
        {
            Catalog = prepared.Catalog.Entries,
            V1 = store.V1 is null ? null : store.V1 with { Payload = prepared.V1Payload, PayloadRoot = prepared.V1PayloadRoot },
            Inspection = store.Inspection with
            {
                Entries = prepared.Catalog.Entries.Select(e => new ExecutionStoreEntry(e.Id, e.Name, e.Bytes, e.Corrupted)).ToArray(),
                CatalogComplete = prepared.Catalog.Complete,
                SourcePayloadSha256 = prepared.V1Payload?.Sha256,
                PayloadVerified = false, // Le contrôle est refait avant écriture ; une lecture courte ne prouve pas le contenu actuel.
            },
        };
    }

    private async Task<ExecutionOutcome> PrepareCatalogAsync(string bundle, string manifestHash, string? keyPath, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var store = await stores.ReadAsync(bundle, token).ConfigureAwait(false);
        if (!store.Inspection.Store.Ready || store.Inspection.ManifestSha256 != manifestHash)
            throw new FilproOperationException("store.changed", "Le magasin a changé avant la préparation.");
        var logs = NewLogDirectory(context.Id);
        using var inputs = new ExecutionInputLease(files);
        await inputs.HoldAsync(Path.Combine(bundle, "filpro-backup.json"), manifestHash, null, token).ConfigureAwait(false);
        using var key = OpenKeyLease(store.Inspection.Store.Encrypted, keyPath, bundle);
        var entries = new List<BackupCatalogEntry>();
        var complete = true;
        var catalogBytes = 256;
        ManifestFileReference? v1Payload = null;
        string? v1PayloadRoot = null;
        if (store.V1 is { } legacy)
        {
            if (legacy.Compressed)
            {
                context.Report(ExecutionState.Preparing, "Mesure du contenu V1 avant lecture du catalogue");
                v1Payload = await inputs.MeasureAndHoldAsync(bundle, "Store/USMT/USMT.MIG", token).ConfigureAwait(false);
                v1PayloadRoot = bundle;
                var catalog = await NativeCatalog(legacy.ProfileId, ExecutionFileSystem.Resolve(bundle, v1Payload.Path), logs).ConfigureAwait(false);
                Append(catalog);
            }
            else
            {
                context.Report(ExecutionState.Preparing, "Indexation SHA-256 actuelle du magasin V1 non compressé");
                var row = new PlainStoreDescriptor(legacy.ProfileId, "Store");
                var captured = await new PlainStoreVerifier(files).CaptureAsync(bundle, row, token, logs).ConfigureAwait(false);
                Append(captured.Catalog);
                v1Payload = captured.Index; v1PayloadRoot = logs;
            }
        }
        else
        {
            foreach (var profile in store.Manifest!.Profiles.Where(p => p.Status is "Ready" or "Partial"))
            {
                var profileLogs = Path.Combine(logs, profile.Id); files.CreateDirectories(profileLogs);
                if (store.Manifest.Compressed)
                {
                    using var payload = new ExecutionInputLease(files);
                    var path = await payload.HoldAsync(ExecutionFileSystem.Resolve(bundle, profile.Payload!.Path), profile.Payload.Sha256, profile.Payload.Bytes, token).ConfigureAwait(false);
                    var catalog = await NativeCatalog(profile.Id, path, profileLogs).ConfigureAwait(false);
                    Append(catalog);
                }
                else
                {
                    context.Report(ExecutionState.Preparing, "Vérification SHA-256 avant lecture des fichiers", profile.Selection.Sid);
                    using var payload = await new PlainStoreVerifier(files).VerifyAndHoldAsync(bundle, profile, token).ConfigureAwait(false);
                    foreach (var entry in payload.Entries.Values.Where(e => e.Path.StartsWith("USMT/File/", StringComparison.Ordinal)))
                    {
                        var name = entry.Path[10..];
                        var item = new BackupCatalogEntry(ExecutionCatalogCodec.EntryId(profile.Id, name), profile.Id, name, entry.Bytes, false, entry.Path);
                        AppendEntry(item);
                    }
                }
            }
        }
        token.ThrowIfCancellationRequested();
        var result = new BackupCatalog(complete, entries);
        lock (catalogGate)
        {
            if (preparedCatalogs.Count >= 100 && !preparedCatalogs.ContainsKey(bundle)) preparedCatalogs.Remove(preparedCatalogs.Keys.First());
            preparedCatalogs[bundle] = new(bundle, manifestHash, store.Inspection.Id, result, v1Payload, v1PayloadRoot);
        }
        return new(complete ? ExecutionState.Succeeded : ExecutionState.Partial,
            ["Catalogue établi après confirmation ; les fichiers seront recontrôlés avant récupération ou restauration.",
                store.V1 is null ? "Le manifeste V2 reste inchangé." : "Le magasin V1 reste inchangé. L'index mesuré décrit son état actuel ; aucune empreinte historique absente n'est inventée."], bundle);

        void Append(BackupCatalog catalog)
        {
            complete &= catalog.Complete;
            foreach (var entry in catalog.Entries) AppendEntry(entry);
        }
        void AppendEntry(BackupCatalogEntry entry)
        {
            var cost = ExecutionCatalogCodec.EntryByteCost(entry);
            if (entries.Count == ExecutionCatalogCodec.MaximumEntries || catalogBytes + cost > ExecutionCatalogCodec.MaximumBytes)
            { complete = false; return; }
            entries.Add(entry); catalogBytes += cost;
        }

        async Task<BackupCatalog> NativeCatalog(string profileId, string payload, string directory)
        {
            var arguments = new List<string> { "/verify:all", payload, "/l:" + Path.Combine(directory, "catalog-verify.log") };
            arguments.AddRange(Crypto(store.Inspection.Store.Encrypted, keyPath));
            context.Report(ExecutionState.Running, "Vérification native et lecture du catalogue USMT");
            var read = await RunAsync("usmtutils.exe", arguments, directory, "catalog", token).ConfigureAwait(false);
            RequireSuccess(read, "USMT /verify:all");
            var catalog = ExecutionCatalogCodec.ReadUsmtOutput(profileId, WindowsExecutionProcess.ReadCatalogOutput(read.StandardOutputPath), token);
            if (catalog.NativeIntegrityValid != true || catalog.Entries.Any(e => e.Corrupted))
                throw new FilproOperationException("store.integrity", "Le rapport USMT est incomplet, non reconnu ou signale un contenu corrompu.");
            return catalog;
        }
    }

}
