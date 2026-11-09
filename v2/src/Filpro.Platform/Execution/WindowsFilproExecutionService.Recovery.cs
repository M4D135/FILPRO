using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class WindowsFilproExecutionService
{
    public async Task<RecoveryPlanReview> CreateRecoveryPlanAsync(RecoveryPlanRequest request, CancellationToken cancellationToken)
    {
        RequireMode(false); cancellationToken.ThrowIfCancellationRequested();
        if (request.EntryIds is null || request.EntryIds.Count is < 1 or > 10000
            || request.EntryIds.Distinct(StringComparer.Ordinal).Count() != request.EntryIds.Count)
            throw new FilproOperationException("recovery.selection", "Choisir de 1 à 10 000 entrées distinctes du catalogue.");
        var store = await ReadStoreAsync(request.Bundle, cancellationToken).ConfigureAwait(false);
        if (!store.Inspection.Store.Ready || store.Manifest is null && store.V1?.Payload is null)
            throw new FilproOperationException("recovery.store", "Un magasin exploitable est requis ; préparer le catalogue V1 avant la sélection.");
        var compressed = store.Manifest?.Compressed ?? store.V1!.Compressed;
        var available = store.Inspection.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var entries = new List<ExecutionStoreEntry>();
        foreach (var id in request.EntryIds)
        {
            if (!available.TryGetValue(id, out var entry) || entry.Corrupted)
                throw new FilproOperationException("recovery.selection", "Entrée absente du catalogue vérifié ou corrompue.");
            entries.Add(entry);
        }
        var destination = StoragePathPolicy.NormalizeWindows(request.Destination);
        RequireIndependentDestination(destination, request.Bundle, cancellationToken);
        var key = request.KeyPath is null ? null : StoragePathPolicy.NormalizeWindows(request.KeyPath);
        if (store.Inspection.Store.Encrypted && key is null) throw new FilproOperationException("usmt.crypto", "Fichier clé requis pour ce magasin.");
        var planId = Guid.NewGuid().ToString("N");
        destination = StoragePathPolicy.NormalizeWindows(Path.Combine(destination, "FILPRO-recuperation-" + planId[..8]));
        return new(planId, clock.GetUtcNow(), false, store.Inspection.Bundle, store.Inspection.Id,
            store.Inspection.ManifestSha256, destination, key, entries, store.Inspection.Store.Encrypted,
            ["L'extraction crée un nouveau dossier ; aucun fichier existant n'est remplacé.",
                compressed ? "Les empreintes des fichiers extraits seront mesurées ; USMT ne fournit pas leur empreinte individuelle d'origine."
                    : "Les fichiers non compressés seront comparés aux SHA-256 de l'index avant et après copie.",
                store.V1 is null ? "Le contenu est lié au manifeste V2." : "La preuve du contenu V1 a été mesurée lors de la préparation ; aucune empreinte historique absente n'est inventée.",
                "Cette récupération de fichiers ne restaure ni compte, ni registre, ni application."],
            "Confirmer avant le contrôle complet du magasin puis l'extraction des fichiers sélectionnés.", store.Inspection.SourcePayloadSha256);
    }

    public async Task<ExecutionJobSnapshot> StartRecoveryAsync(RecoveryPlanReview plan, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        RequireStart(plan.Demonstration, plan.Id, plan.CreatedUtc, start, cancellationToken);
        if (!Capabilities.Recover) throw new FilproOperationException("execution.usmt", "USMT vérifié indisponible.");
        var store = await ReadStoreAsync(plan.Bundle, cancellationToken).ConfigureAwait(false);
        RequireRecoveryStore(plan, store);
        RequireIndependentDestination(plan.Destination, plan.Bundle, cancellationToken);
        return jobs.Start(ExecutionKind.Recover, "Récupération de " + plan.Entries.Count + " fichier(s)", start,
            context => WithOperationAsync(context, () => RecoverAsync(plan, context)), cancellationToken);
    }

    private async Task<ExecutionOutcome> RecoverAsync(RecoveryPlanReview plan, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var logs = NewLogDirectory(context.Id);
        var receipt = Path.Combine(logs, "recovery-receipt.json");
        var recovered = new List<object>();
        var destinationCreated = false;
        try
        {
            context.Report(ExecutionState.Preparing, "Contrôle du magasin et de la destination");
            var store = await ReadStoreAsync(plan.Bundle, token).ConfigureAwait(false); RequireRecoveryStore(plan, store);
            RequireIndependentDestination(plan.Destination, plan.Bundle, token);
            using var bundleLease = files.HoldDirectory(plan.Bundle);
            using var storeInputs = new ExecutionInputLease(files);
            await storeInputs.HoldAsync(Path.Combine(plan.Bundle, "filpro-backup.json"), plan.ManifestSha256, null, token).ConfigureAwait(false);
            using var keyLease = OpenKeyLease(plan.Encrypted, plan.KeyPath, plan.Bundle);
            var requestedIds = plan.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var requested = store.Catalog.Where(e => requestedIds.Contains(e.Id)).ToArray();
            if (requested.Length != plan.Entries.Count) throw new FilproOperationException("recovery.changed", "Le catalogue ne contient plus exactement la sélection confirmée.");
            files.CreateDirectories(Path.GetDirectoryName(plan.Destination)!);
            using var destinationLease = files.CreateNewDirectory(plan.Destination);
            destinationCreated = true;
            var outputRoot = Path.Combine(plan.Destination, "Fichiers"); files.CreateDirectories(outputRoot);
            foreach (var group in requested.GroupBy(e => e.ProfileId))
            {
                var profile = RecoverySourceFor(store, group.Key);
                if (!profile.Compressed)
                {
                    context.Report(ExecutionState.Preparing, "Vérification SHA-256 du magasin non compressé", profile.Sid);
                    using var plain = await new PlainStoreVerifier(files).VerifyAndHoldAsync(plan.Bundle,
                        new PlainStoreDescriptor(profile.Id, profile.Store, profile.Payload), token, profile.IndexRoot).ConfigureAwait(false);
                    var sourceStore = ExecutionFileSystem.Resolve(plan.Bundle, profile.Store);
                    var selectedBytes = group.Aggregate(0L, (total, entry) => checked(total + (entry.Bytes ?? 0)));
                    RequireSpace(plan.Destination, selectedBytes, 0, options.StateDirectory);
                    foreach (var entry in group)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.StoredPath is null || !plain.Entries.TryGetValue(entry.StoredPath, out var expected)
                            || entry.Bytes != expected.Bytes)
                            throw new FilproOperationException("recovery.changed", "Le catalogue ne correspond plus à l'index vérifié.");
                        var relative = profile.Id + "/" + entry.Name;
                        var target = ExecutionFileSystem.Resolve(outputRoot, relative);
                        files.CreateDirectories(Path.GetDirectoryName(target)!);
                        context.Report(ExecutionState.Running, "Copie des fichiers vérifiés", profile.Sid, recovered.Count, requested.Length);
                        await files.CopyNewAsync(ExecutionFileSystem.Resolve(sourceStore, expected.Path), target, token).ConfigureAwait(false);
                        var after = await files.DescribeAsync(outputRoot, relative, token).ConfigureAwait(false);
                        if (after.Bytes != expected.Bytes || after.Sha256 != expected.Sha256)
                            throw new FilproOperationException("recovery.integrity", "La copie ne correspond pas à l'index de sauvegarde.");
                        recovered.Add(new { entry.Id, entry.Name, target, after.Sha256, after.Bytes, sourceHashVerified = true });
                        WriteReceipt(receipt, new { kind = "Recovery", stage = "Running", plan.Bundle, plan.Destination, requested = requested.Length, recovered });
                    }
                    continue;
                }
                var payload = ExecutionFileSystem.Resolve(plan.Bundle, profile.Payload.Path);
                using var payloadInputs = new ExecutionInputLease(files);
                await payloadInputs.HoldAsync(payload, profile.Payload.Sha256, profile.Payload.Bytes, token).ConfigureAwait(false);
                var profileLogs = Path.Combine(logs, profile.Id); files.CreateDirectories(profileLogs);
                var verify = new List<string> { "/verify", payload, "/l:" + Path.Combine(profileLogs, "verify.log") };
                verify.AddRange(Crypto(plan.Encrypted, plan.KeyPath));
                context.Report(ExecutionState.Preparing, "Vérification native du magasin avant extraction", profile.Sid);
                RequireSuccess(await RunAsync("usmtutils.exe", verify, profileLogs, "verify", token).ConfigureAwait(false), "USMT /verify");
                var number = 0;
                foreach (var batch in group.Chunk(20))
                {
                    token.ThrowIfCancellationRequested();
                    var batchName = "extraction-" + profile.Id[..8] + "-" + (++number).ToString("D5");
                    var batchDirectory = Path.Combine(plan.Destination, batchName);
                    using var batchLease = files.CreateNewDirectory(batchDirectory);
                    var args = new List<string> { "/extract", payload, batchDirectory,
                        "/i:" + string.Join(';', batch.Select(e => ExecutionCatalogCodec.NormalizeName(e.Name))),
                        "/l:" + Path.Combine(profileLogs, batchName + ".log") };
                    args.AddRange(Crypto(plan.Encrypted, plan.KeyPath));
                    context.Report(ExecutionState.Running, "Extraction des fichiers sélectionnés", profile.Sid, recovered.Count, requested.Length);
                    RequireSuccess(await RunAsync("usmtutils.exe", args, profileLogs, batchName, token).ConfigureAwait(false), "USMT /extract");
                    var actual = EnumerateSafeFiles(batchDirectory, token).ToDictionary(
                        path => Path.GetRelativePath(batchDirectory, path).Replace('\\', '/'), path => path, StringComparer.OrdinalIgnoreCase);
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in batch)
                    {
                        var candidates = ExtractedCandidates(entry.Name).Where(actual.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        if (candidates.Length != 1 || !used.Add(candidates[0]))
                            throw new FilproOperationException("recovery.ambiguous", "Extraction absente ou ambiguë ; le lot est conservé sans écrasement.");
                        var relative = profile.Id + "/" + entry.Name[0] + "/" + entry.Name[3..].Replace('\\', '/');
                        var target = ExecutionFileSystem.Resolve(outputRoot, relative);
                        files.CreateDirectories(Path.GetDirectoryName(target)!);
                        var sourcePath = actual[candidates[0]];
                        var before = await files.DescribeAsync(batchDirectory, candidates[0], token).ConfigureAwait(false);
                        await files.CopyNewAsync(sourcePath, target, token).ConfigureAwait(false);
                        var after = await files.DescribeAsync(outputRoot, relative, token).ConfigureAwait(false);
                        if (before.Sha256 != after.Sha256 || before.Bytes != after.Bytes)
                            throw new FilproOperationException("recovery.integrity", "Le fichier recopié ne correspond pas à l'extraction ; données conservées pour examen.");
                        recovered.Add(new { entry.Id, entry.Name, target, after.Sha256, after.Bytes });
                    }
                    if (used.Count != actual.Count) throw new FilproOperationException("recovery.unexpected", "USMT a extrait des fichiers supplémentaires ; aucune réussite globale.");
                    WriteReceipt(receipt, new { kind = "Recovery", stage = "Running", plan.Bundle, plan.Destination, requested = requested.Length, recovered });
                }
            }
            WriteReceipt(receipt, new { kind = "Recovery", stage = "Completed", plan.Bundle, plan.Destination, requested = requested.Length, recovered });
            return new(ExecutionState.Succeeded, plan.Warnings, plan.Destination);
        }
        catch (Exception ex)
        {
            var evidenceFailure = TryWriteReceipt(receipt, new { kind = "Recovery", stage = destinationCreated ? "Partial" : "Stopped", plan.Bundle,
                plan.Destination, recovered, cancelled = ex is OperationCanceledException });
            if (destinationCreated) throw new ExecutionPartialException("recovery.partial", "Extraction interrompue ; les fichiers obtenus sont conservés séparément.", plan.Destination,
                new[] { "Aucune destination existante n'a été écrasée.", "Reçu de contrôle : " + receipt, evidenceFailure }.Where(s => s is not null).Cast<string>().ToArray());
            throw;
        }
    }

    private static void RequireRecoveryStore(RecoveryPlanReview plan, NativeBackupStore store)
    {
        if (!store.Inspection.Store.Ready
            || store.Inspection.ManifestSha256 != plan.ManifestSha256 || store.Inspection.Id != plan.StoreId)
            throw new FilproOperationException("store.changed", "Le magasin a changé après la revue.");
        if (store.V1 is { } legacy && (legacy.Payload is null || plan.SourcePayloadSha256 is null || legacy.Payload.Sha256 != plan.SourcePayloadSha256))
            throw new FilproOperationException("store.catalogRequired", "Le contenu V1 préparé n'est plus celui du plan ; préparer le catalogue et confirmer à nouveau.");
        var available = store.Inspection.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        if (plan.Entries.Count == 0 || plan.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != plan.Entries.Count
            || plan.Entries.Any(e => !available.TryGetValue(e.Id, out var original) || original != e || original.Corrupted))
            throw new FilproOperationException("recovery.selection", "La sélection ne correspond pas au catalogue du magasin.");
    }

    private sealed record RecoverySource(string Id, string? Sid, string Store, ManifestFileReference Payload,
        bool Compressed, string? IndexRoot = null);
    private static RecoverySource RecoverySourceFor(NativeBackupStore store, string profileId)
    {
        if (store.V1 is { } legacy && profileId == legacy.ProfileId && legacy.Payload is not null)
            return new(profileId, null, "Store", legacy.Payload, legacy.Compressed, legacy.PayloadRoot);
        var profile = store.Manifest?.Profiles.SingleOrDefault(p => p.Id == profileId && p.Status is "Ready" or "Partial");
        if (profile?.Payload is null) throw new FilproOperationException("recovery.selection", "Le profil du catalogue n'est pas exploitable.");
        return new(profile.Id, profile.Selection.Sid, profile.Store, profile.Payload, store.Manifest!.Compressed);
    }
    private void RequireIndependentDestination(string destination, string bundle, CancellationToken token)
    {
        if (StoragePathPolicy.IsWithin(destination, bundle) || StoragePathPolicy.IsWithin(bundle, destination))
            throw new FilproOperationException("recovery.destination", "La destination doit être indépendante du magasin source.");
        var inventory = source.ReadProfiles(token);
        if (inventory.SessionReadStatus != SessionReadStatus.Complete || !string.IsNullOrWhiteSpace(inventory.SessionError))
            throw new FilproOperationException("recovery.destination", "L'inventaire global des profils ou des sessions est incomplet ; une destination indépendante ne peut pas être prouvée.");
        foreach (var profile in inventory.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.LocalPath)))
            if (StoragePathPolicy.IsWithin(destination, profile.LocalPath!) || StoragePathPolicy.IsWithin(profile.LocalPath!, destination))
                throw new FilproOperationException("recovery.destination", "Choisir une destination indépendante des profils Windows.");
    }
    private IEnumerable<string> EnumerateSafeFiles(string root, CancellationToken token)
    {
        var pending = new Stack<string>(); pending.Push(root);
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            using var lease = files.HoldDirectory(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (++count > 100000) throw new FilproOperationException("recovery.bounds", "Lot d'extraction hors limites.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0) { using var checkedDirectory = files.HoldDirectory(entry); pending.Push(entry); }
                else { files.RequireFile(entry); yield return entry; }
            }
        }
    }
    private static IReadOnlyList<string> ExtractedCandidates(string name)
    {
        name = ExecutionCatalogCodec.NormalizeName(name);
        if (name.Length < 4 || name[1] != ':') throw new FilproOperationException("recovery.name", "Nom de fichier USMT hors lecteur local.");
        var drive = char.ToUpperInvariant(name[0]); var rest = name[3..].Replace('\\', '/');
        return [drive + "/" + rest, drive + "$/" + rest, "USMT/File/" + drive + "$/" + rest, rest];
    }
}
