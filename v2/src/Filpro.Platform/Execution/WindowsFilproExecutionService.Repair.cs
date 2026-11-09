using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class WindowsFilproExecutionService
{
    private sealed record RepairReceipt(int SchemaVersion, string Id, string Sid, string Account, string OriginalPath,
        string PreservedPath, string BackupDirectory, string SafetyBundle, ManifestFileReference RegistryBackup,
        ManifestFileReference HiveBackup, string Stage, string? NewProfilePreservedPath = null,
        ManifestFileReference? NewRegistryBackup = null, ManifestFileReference? NewHiveBackup = null);
    private sealed record SealedRepairReceipt(string Payload, string Signature);
    private static readonly JsonSerializerOptions RepairJsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };

    public Task<RepairPlanReview> CreateRepairPlanAsync(RepairPlanRequest request, CancellationToken cancellationToken)
    {
        RequireMode(false); cancellationToken.ThrowIfCancellationRequested();
        var profile = profiles.Read(request.Sid, true, cancellationToken);
        RequireRepairLocation(profile.LocalPath!);
        files.RequireFile(Path.Combine(profile.LocalPath!, "NTUSER.DAT"));
        var safety = StoragePathPolicy.NormalizeWindows(request.SafetyBackupRoot);
        if (StoragePathPolicy.IsWithin(safety, profile.LocalPath!) || StoragePathPolicy.IsWithin(profile.LocalPath!, safety))
            throw new FilproOperationException("repair.backup", "Choisir une sauvegarde préalable indépendante du profil.");
        var id = Guid.NewGuid().ToString("N");
        var preserved = StoragePathPolicy.NormalizeWindows(profile.LocalPath! + ".old-filpro-" + id[..8]);
        if (Directory.Exists(preserved) || File.Exists(preserved)) throw new FilproOperationException("repair.destination", "Le dossier de conservation existe déjà.");
        var plan = new RepairPlanReview(id, clock.GetUtcNow(), false, profile.Sid, profile.Account, profile.LocalPath!, preserved, safety,
            ["Créer et vérifier une sauvegarde USMT du profil.", "Archiver NTUSER.DAT et toutes les valeurs ProfileList prises en charge.",
                "Conserver le dossier du profil sous un nom .old-filpro distinct.", "Détacher uniquement l'entrée ProfileList de ce SID."],
            ["Le compte doit rester déconnecté pendant l'intervention.", "L'ancien profil et les sauvegardes restent conservés.",
                "La création du nouveau profil exige ensuite une connexion volontaire de l'utilisateur ; aucun redémarrage n'est lancé."],
            "Confirmer avant toute préparation longue. Cette étape prépare la recréation du profil ; sa validation utilisateur reste distincte.");
        return Task.FromResult(plan);
    }

    public Task<ExecutionJobSnapshot> StartRepairAsync(RepairPlanReview plan, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        RequireStart(plan.Demonstration, plan.Id, plan.CreatedUtc, start, cancellationToken);
        if (!Capabilities.Repair) throw new FilproOperationException("execution.usmt", "La réparation exige USMT vérifié pour sa sauvegarde préalable.");
        profiles.Match(plan.Sid, plan.Account, plan.ProfilePath, true, cancellationToken);
        RequireRepairLocation(plan.ProfilePath);
        if (plan.PreservedProfilePath != plan.ProfilePath + ".old-filpro-" + plan.Id[..8])
            throw new FilproOperationException("repair.plan", "Le chemin de conservation ne correspond pas au plan.");
        return Task.FromResult(jobs.Start(ExecutionKind.Repair, "Préparation de la réparation de " + plan.Account, start,
            context => WithOperationAsync(context, () => RepairAsync(plan, context)), cancellationToken));
    }

    private async Task<ExecutionOutcome> RepairAsync(RepairPlanReview plan, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var logs = NewLogDirectory(context.Id);
        var receiptPath = Path.Combine(logs, "repair-receipt.json");
        RepairReceipt? receipt = null;
        var moved = false;
        var registry = new WindowsProfileRegistry();
        ProfileRegistrySnapshot? registrySnapshot = null;
        try
        {
            context.Report(ExecutionState.Preparing, "Sauvegarde préalable avant réparation", plan.Sid);
            var profile = profiles.Match(plan.Sid, plan.Account, plan.ProfilePath, true, token);
            var inventory = source.ReadSoftware(plan.Sid, SoftwareQuery.Empty, token);
            if (!inventory.Discovery.FilesComplete || !inventory.Discovery.RegistryComplete)
                throw new FilproOperationException("repair.backup", "Inventaire incomplet : la préparation destructive reste bloquée.");
            var selection = new BackupProfileSelection(plan.Sid, profile.IdentityKind, profile.Account, profile.LocalPath!, false,
                inventory.Groups.SelectMany(g => g.Files).Select(f => f with { Selected = true }).ToArray(),
                inventory.Groups.SelectMany(g => g.Registry).Select(r => r with { Selected = true }).ToArray());
            var backupPlan = BackupPlan.Create(new([selection], plan.SafetyBackupRoot, Purpose: "Sauvegarde préalable à la réparation"),
                Guid.NewGuid().ToString("N"), clock.GetUtcNow());
            var safety = await BackupAsync(backupPlan, context).ConfigureAwait(false);
            if (safety.State != ExecutionState.Succeeded || safety.ResultPath is null)
                throw new FilproOperationException("repair.backup", "La sauvegarde préalable n'est pas complète ; le profil reste attaché.");
            var safetyStore = await stores.ReadAsync(safety.ResultPath, token).ConfigureAwait(false);
            foreach (var captured in safetyStore.Manifest!.Profiles)
                await files.VerifyAsync(safety.ResultPath, captured.Payload!, token).ConfigureAwait(false);
            profiles.Match(plan.Sid, plan.Account, plan.ProfilePath, true, token);
            var backupDirectory = Path.Combine(plan.SafetyBackupRoot, "FILPRO-repair-" + plan.Id);
            using var backupLease = files.CreateNewDirectory(backupDirectory);
            using var parentLease = files.HoldDirectory(Path.GetDirectoryName(plan.ProfilePath)!);
            var hivePath = Path.Combine(plan.ProfilePath, "NTUSER.DAT");
            files.RequireFile(hivePath);
            // FILE_SHARE_DELETE empêche une session d'ouvrir la ruche en écriture pendant la copie.
            // Windows refuse de renommer le dossier parent tant que ce handle existe : le fermer avant Directory.Move.
            using (var hiveLease = StorageFileIdentity.OpenReadForRename(hivePath))
            {
                StorageFileIdentity.RequireSingleLink(hiveLease);
                registrySnapshot = registry.Capture(plan.Sid, plan.ProfilePath);
                files.WriteAtomic(Path.Combine(backupDirectory, "profile-list.json"), ProfileRegistrySnapshotCodec.Encode(registrySnapshot), false);
                using (var savedHive = new FileStream(Path.Combine(backupDirectory, "NTUSER.DAT"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { await hiveLease.CopyToAsync(savedHive, token).ConfigureAwait(false); savedHive.Flush(true); }
                receipt = new(1, plan.Id, plan.Sid, plan.Account, plan.ProfilePath, plan.PreservedProfilePath, backupDirectory, safety.ResultPath,
                    await files.DescribeAsync(backupDirectory, "profile-list.json", token).ConfigureAwait(false),
                    await files.DescribeAsync(backupDirectory, "NTUSER.DAT", token).ConfigureAwait(false), "BackupsReady");
                WriteRepairReceipt(receiptPath, receipt);
            }
            token.ThrowIfCancellationRequested();
            profiles.Match(plan.Sid, plan.Account, plan.ProfilePath, true, token);
            RequireNoSession(plan.Sid, token);
            context.Report(ExecutionState.Running, "Conservation du profil original", plan.Sid);
            receipt = receipt with { Stage = "RenamePending" }; WriteRepairReceipt(receiptPath, receipt);
            Directory.Move(plan.ProfilePath, plan.PreservedProfilePath); moved = true;
            receipt = receipt with { Stage = "FolderPreserved" }; WriteRepairReceipt(receiptPath, receipt);
            RequireNoSession(plan.Sid, token);
            context.Report(ExecutionState.Running, "Détachement de l'entrée ProfileList sauvegardée", plan.Sid);
            receipt = receipt with { Stage = "DetachPending" }; WriteRepairReceipt(receiptPath, receipt);
            registry.RemoveExact(registrySnapshot);
            receipt = receipt with { Stage = "AwaitingNewProfile" }; WriteRepairReceipt(receiptPath, receipt);
            return new(ExecutionState.Succeeded,
                ["Ancien profil conservé : " + plan.PreservedProfilePath,
                    "Reconnecter volontairement l'utilisateur pour créer son nouveau profil, puis vérifier la session avant restauration des données.",
                    "Aucun redémarrage ni fermeture de session n'a été déclenché.", "La préparation est terminée ; la réparation n'est pas encore validée par l'utilisateur."], receiptPath);
        }
        catch (Exception ex)
        {
            if (receipt is not null && moved)
            {
                // Retour local immédiat uniquement si aucun nouveau dossier n'a pris la place de l'original.
                try
                {
                    RequireNoSession(plan.Sid, CancellationToken.None);
                    using var parentLease = files.HoldDirectory(Path.GetDirectoryName(plan.ProfilePath)!);
                    using var current = await ObserveRepairHiveAsync(plan.ProfilePath, CancellationToken.None).ConfigureAwait(false);
                    if (current is not null) throw new IOException("Le chemin original est réoccupé.");
                    using var preserved = await ObserveRepairHiveAsync(plan.PreservedProfilePath, CancellationToken.None).ConfigureAwait(false)
                        ?? throw new IOException("Le profil conservé est absent.");
                    RequireRepairHash(preserved, receipt.HiveBackup);
                    var attached = registry.Exists(plan.Sid) ? registry.Capture(plan.Sid, plan.ProfilePath) : null;
                    if (attached is not null && !SameProfileSnapshot(attached, registrySnapshot!))
                        throw new IOException("L'entrée ProfileList n'est pas celle sauvegardée.");
                    current?.Dispose();
                    preserved.Dispose();
                    Directory.Move(plan.PreservedProfilePath, plan.ProfilePath);
                    if (attached is null) registry.RestoreAbsent(registrySnapshot!);
                    if (!SameProfileSnapshot(registry.Capture(plan.Sid, plan.ProfilePath), registrySnapshot!))
                        throw new IOException("L'entrée remise en place n'est pas confirmée.");
                    receipt = receipt with { Stage = "PreparationRolledBack" };
                    throw RepairReconciliation.Partial("repair.reverted", "La préparation a échoué ; le profil original a été remis en place.", receiptPath,
                        RepairRecoveryPaths(receipt), () => WriteRepairReceipt(receiptPath, receipt));
                }
                catch (ExecutionPartialException) { throw; }
                catch (Exception)
                {
                    receipt = receipt with { Stage = "PreparePartial" };
                    throw RepairReconciliation.Partial("repair.partial", "Préparation interrompue après un renommage ; les dossiers et sauvegardes sont conservés.", receiptPath,
                        RepairRecoveryPaths(receipt), () => WriteRepairReceipt(receiptPath, receipt));
                }
            }
            if (receipt is not null)
            {
                receipt = receipt with { Stage = ex is OperationCanceledException ? "CancelledBeforeRename" : "FailedBeforeRename" };
                try { WriteRepairReceipt(receiptPath, receipt); } catch { /* Aucun profil n'a été renommé ; conserver l'échec initial. */ }
            }
            throw;
        }
    }

    public Task<RepairRollbackReview> ReviewRepairRollbackAsync(RepairRollbackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMode(false); cancellationToken.ThrowIfCancellationRequested();
        var receipt = ReadRepairReceipt(request.ReceiptPath, out var receiptSha256);
        RequireRepairLocation(receipt.OriginalPath);
        RequireNoSession(receipt.Sid, cancellationToken);
        return Task.FromResult(new RepairRollbackReview(StoragePathPolicy.NormalizeNative(request.ReceiptPath), receiptSha256, false,
            receipt.Sid, receipt.Account, receipt.Stage, receipt.OriginalPath, receipt.PreservedPath, receipt.BackupDirectory,
            receipt.SafetyBundle, receipt.NewProfilePreservedPath));
    }

    public Task<ExecutionJobSnapshot> StartRepairRollbackAsync(RepairRollbackRequest request, ExecutionStartOptions start, CancellationToken cancellationToken)
    {
        RequireStart(false, Guid.NewGuid().ToString("N"), clock.GetUtcNow(), start, cancellationToken);
        var receipt = ReadRepairReceipt(request.ReceiptPath, out var receiptSha256);
        if (request.ExpectedReceiptSha256 is { } expected && !string.Equals(expected, receiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("repair.receipt", "Le reçu de réparation a changé depuis sa confirmation : relisez-le avant de recommencer.");
        RequireNoSession(receipt.Sid, cancellationToken);
        return Task.FromResult(jobs.Start(ExecutionKind.Rollback, "Retour arrière de la réparation de " + receipt.Account, start,
            context => WithOperationAsync(context, () => RollbackRepairAsync(request.ReceiptPath, receiptSha256, context)), cancellationToken));
    }

    // Le travail exécute le reçu contrôlé au démarrage : un reçu remplacé entre-temps, même scellé, est refusé.
    private async Task<ExecutionOutcome> RollbackRepairAsync(string receiptPath, string startedReceiptSha256, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var receipt = ReadRepairReceipt(receiptPath, out var receiptSha256);
        if (!string.Equals(receiptSha256, startedReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("repair.receipt", "Le reçu de réparation a changé depuis le démarrage du retour arrière : rien n'a été modifié.");
        var changed = false;
        var priorChangesPossible = RepairReconciliation.MayHaveChanged(receipt.Stage);
        try
        {
            context.Report(ExecutionState.Preparing, "Vérification des sauvegardes et des dossiers conservés", receipt.Sid);
            RequireNoSession(receipt.Sid, token);
            RequireRepairLocation(receipt.OriginalPath);
            using var parentLease = files.HoldDirectory(Path.GetDirectoryName(receipt.OriginalPath)!);
            using var backupInputs = new ExecutionInputLease(files);
            var snapshot = await HoldRepairSnapshotAsync(receipt, receipt.RegistryBackup, receipt.HiveBackup, backupInputs, token).ConfigureAwait(false);
            var registry = new WindowsProfileRegistry();
            ProfileRegistrySnapshot? replacementSnapshot = receipt.NewRegistryBackup is null ? null
                : await HoldRepairSnapshotAsync(receipt, receipt.NewRegistryBackup, receipt.NewHiveBackup!, backupInputs, token).ConfigureAwait(false);

            // Au plus six décisions dans le parcours normal. Chaque tour relit ce qui existe effectivement après une coupure.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                token.ThrowIfCancellationRequested(); RequireNoSession(receipt.Sid, token);
                using var current = await ObserveRepairHiveAsync(receipt.OriginalPath, token).ConfigureAwait(false);
                using var old = await ObserveRepairHiveAsync(receipt.PreservedPath, token).ConfigureAwait(false);
                using var replacement = receipt.NewProfilePreservedPath is null ? null
                    : await ObserveRepairHiveAsync(receipt.NewProfilePreservedPath, token).ConfigureAwait(false);
                var attached = registry.Exists(receipt.Sid) ? registry.Capture(receipt.Sid, receipt.OriginalPath) : null;
                var identity = attached is null ? RepairRegistryIdentity.Absent
                    : SameProfileSnapshot(attached, snapshot) ? RepairRegistryIdentity.Original
                    : replacementSnapshot is not null && SameProfileSnapshot(attached, replacementSnapshot)
                        ? RepairRegistryIdentity.Replacement : RepairRegistryIdentity.Unrecognized;
                var step = RepairReconciliation.Next(new(receipt.Stage, receipt.HiveBackup.Sha256, current?.Sha256,
                    old?.Sha256, receipt.NewHiveBackup?.Sha256, replacement?.Sha256, identity));
                if (step == RepairRollbackStep.Complete)
                {
                    if (receipt.Stage is not ("RolledBack" or "PreparationRolledBack"))
                    { receipt = receipt with { Stage = "RolledBack" }; WriteRepairReceipt(receiptPath, receipt); }
                    return new(ExecutionState.Succeeded,
                        ["Le dossier original et son entrée ProfileList correspondent aux sauvegardes vérifiées.",
                            .. RepairRecoveryPaths(receipt), "Vérifier la connexion avec l'utilisateur ; aucun redémarrage automatique."], receiptPath);
                }
                if (step == RepairRollbackStep.CaptureReplacement)
                {
                    profiles.Match(receipt.Sid, receipt.Account, receipt.OriginalPath, true, token);
                    receipt = await CaptureRepairReplacementAsync(receipt, current!, attached!, token).ConfigureAwait(false);
                    // Les chemins ET les empreintes du nouveau profil deviennent durables avant son premier renommage.
                    WriteRepairReceipt(receiptPath, receipt);
                    replacementSnapshot = await HoldRepairSnapshotAsync(receipt, receipt.NewRegistryBackup!, receipt.NewHiveBackup!, backupInputs, token).ConfigureAwait(false);
                    continue;
                }

                receipt = receipt with { Stage = "RollbackPending" }; WriteRepairReceipt(receiptPath, receipt);
                token.ThrowIfCancellationRequested(); RequireNoSession(receipt.Sid, token);
                context.Report(ExecutionState.Running, step switch
                {
                    RepairRollbackStep.PreserveReplacement => "Conservation du nouveau profil",
                    RepairRollbackStep.RemoveReplacementKey => "Détachement de l'entrée du nouveau profil sauvegardée",
                    RepairRollbackStep.RestoreOriginalDirectory => "Remise en place du dossier original",
                    RepairRollbackStep.RestoreOriginalKey => "Rétablissement de l'entrée ProfileList originale",
                    _ => "Contrôle du retour arrière",
                }, receipt.Sid);
                // Même contrainte : un NTUSER.DAT ouvert bloque le renommage de son dossier.
                current?.Dispose();
                old?.Dispose();
                replacement?.Dispose();
                switch (step)
                {
                    case RepairRollbackStep.PreserveReplacement:
                        Directory.Move(receipt.OriginalPath, receipt.NewProfilePreservedPath!); changed = true; break;
                    case RepairRollbackStep.RemoveReplacementKey:
                        registry.RemoveExact(replacementSnapshot!); changed = true; break;
                    case RepairRollbackStep.RestoreOriginalDirectory:
                        Directory.Move(receipt.PreservedPath, receipt.OriginalPath); changed = true; break;
                    case RepairRollbackStep.RestoreOriginalKey:
                        registry.RestoreAbsent(snapshot); changed = true; break;
                    default: throw new FilproOperationException("repair.reconcile", "Étape de reprise inconnue.");
                }
            }
            throw new FilproOperationException("repair.reconcile", "La reprise n'a pas convergé ; aucune nouvelle tentative automatique.");
        }
        catch (Exception ex)
        {
            if (changed || priorChangesPossible || RepairReconciliation.MayHaveChanged(receipt.Stage))
            {
                receipt = receipt with { Stage = "RollbackPartial" };
                throw RepairReconciliation.Partial("repair.rollbackPartial", "Le retour arrière n'est pas entièrement confirmé ; aucun dossier n'a été supprimé.", receiptPath,
                    [ex is FilproOperationException known ? known.Message : ex is OperationCanceledException
                        ? "Interruption demandée ; le retour arrière doit être repris après contrôle."
                        : "Une lecture ou une écriture nécessaire à la reprise a échoué.", .. RepairRecoveryPaths(receipt)],
                    () => WriteRepairReceipt(receiptPath, receipt));
            }
            throw;
        }
    }

    private async Task<ProfileRegistrySnapshot> HoldRepairSnapshotAsync(RepairReceipt receipt, ManifestFileReference registryReference,
        ManifestFileReference hiveReference, ExecutionInputLease inputs, CancellationToken token)
    {
        var registryPath = ExecutionFileSystem.Resolve(receipt.BackupDirectory, registryReference.Path);
        await inputs.HoldAsync(registryPath, registryReference.Sha256, registryReference.Bytes, token).ConfigureAwait(false);
        await inputs.HoldAsync(ExecutionFileSystem.Resolve(receipt.BackupDirectory, hiveReference.Path), hiveReference.Sha256, hiveReference.Bytes, token).ConfigureAwait(false);
        var snapshot = ProfileRegistrySnapshotCodec.Decode(files.ReadBounded(registryPath, BackupManifestCodec.MaximumBytes));
        if (snapshot.Sid != receipt.Sid || !snapshot.ProfilePath.Equals(receipt.OriginalPath, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("repair.receipt", "Une sauvegarde ProfileList ne correspond pas au reçu.");
        return snapshot;
    }

    private async Task<RepairReceipt> CaptureRepairReplacementAsync(RepairReceipt receipt, RepairHiveEvidence current,
        ProfileRegistrySnapshot snapshot, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var preserved = receipt.OriginalPath + ".new-filpro-" + id[..8];
        _ = StoragePathPolicy.NormalizeWindows(preserved);
        // Toute occupation, y compris un fichier ou un lien, fait échouer la préparation avant de déplacer le profil.
        using (var existing = await ObserveRepairHiveAsync(preserved, token).ConfigureAwait(false))
            if (existing is not null) throw new FilproOperationException("repair.destination", "La destination de conservation du nouveau profil existe déjà.");
        using var backupLease = files.HoldDirectory(receipt.BackupDirectory);
        var registryName = "new-profile-list-" + id + ".json";
        var hiveName = "new-NTUSER-" + id + ".DAT";
        files.WriteAtomic(Path.Combine(receipt.BackupDirectory, registryName), ProfileRegistrySnapshotCodec.Encode(snapshot), false);
        current.Stream.Position = 0;
        await using (var target = new FileStream(Path.Combine(receipt.BackupDirectory, hiveName), FileMode.CreateNew, FileAccess.Write, FileShare.None,
            65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
        { await current.Stream.CopyToAsync(target, token).ConfigureAwait(false); target.Flush(true); }
        var hive = await files.DescribeAsync(receipt.BackupDirectory, hiveName, token).ConfigureAwait(false);
        RequireRepairHash(current, hive);
        return receipt with { Stage = "RollbackPending", NewProfilePreservedPath = preserved,
            NewRegistryBackup = await files.DescribeAsync(receipt.BackupDirectory, registryName, token).ConfigureAwait(false), NewHiveBackup = hive };
    }

    private async Task<RepairHiveEvidence?> ObserveRepairHiveAsync(string directory, CancellationToken token)
    {
        try { _ = File.GetAttributes(directory); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using var directoryCheck = files.HoldDirectory(directory);
        var path = Path.Combine(directory, "NTUSER.DAT"); files.RequireFile(path);
        var stream = StorageFileIdentity.OpenReadForRename(path);
        try
        {
            var size = stream.Length;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            if (stream.Length != size) throw new FilproOperationException("repair.changed", "La ruche a changé pendant son contrôle.");
            return new(stream, hash, size);
        }
        catch { stream.Dispose(); throw; }
    }
    private sealed class RepairHiveEvidence(FileStream stream, string sha256, long bytes) : IDisposable
    {
        public FileStream Stream { get; } = stream;
        public string Sha256 { get; } = sha256;
        public long Bytes { get; } = bytes;
        public void Dispose() => Stream.Dispose();
    }
    private static bool SameProfileSnapshot(ProfileRegistrySnapshot left, ProfileRegistrySnapshot right)
        => ProfileRegistrySnapshotCodec.Encode(left).AsSpan().SequenceEqual(ProfileRegistrySnapshotCodec.Encode(right));
    private static void RequireRepairHash(RepairHiveEvidence actual, ManifestFileReference expected)
    {
        if (actual.Bytes != expected.Bytes || !actual.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("repair.changed", "Une ruche ne correspond pas à sa sauvegarde vérifiée.");
    }
    private static IReadOnlyList<string> RepairRecoveryPaths(RepairReceipt receipt) =>
        ["Emplacement original : " + receipt.OriginalPath, "Ancien profil conservé si non encore remis : " + receipt.PreservedPath,
            "Sauvegardes de ruches et ProfileList : " + receipt.BackupDirectory, "Sauvegarde préalable : " + receipt.SafetyBundle,
            .. (receipt.NewProfilePreservedPath is null ? Array.Empty<string>() : new[] { "Nouveau profil conservé si déjà renommé : " + receipt.NewProfilePreservedPath }),
            "Ne pas supprimer ces dossiers. Contrôler les sessions avant toute reprise."];

    private void RequireNoSession(string sid, CancellationToken token)
    {
        if (source.ReadCurrentAccountSid(token) == sid) throw new FilproOperationException("profile.current", "Le compte exécutant ne peut pas être modifié.");
        var evidence = WindowsPosteSource.ReadNativeSessionEvidence(sid, token);
        if (evidence.ReadStatus != SessionReadStatus.Complete || evidence.State != SessionState.None || evidence.Count != 0)
            throw new FilproOperationException("profile.session", "Une session existe ou son état n'est pas prouvé.");
        var probe = new WindowsLoadedUserRegistry().OpenUserHive(sid);
        using var key = probe.Key;
        if (probe.ErrorCode is not (2 or 3) || key is not null)
            throw new FilproOperationException("profile.loaded", "La ruche de la session est chargée ou son état est inconnu.");
    }
    private static void RequireRepairLocation(string path)
    {
        path = StoragePathPolicy.NormalizeWindows(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || new[] { "Default", "Public", "All Users", "Default User" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            throw new FilproOperationException("repair.path", "La réparation exige un profil local ordinaire.");
        foreach (var system in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }.Where(s => !string.IsNullOrEmpty(s)))
            if (StoragePathPolicy.IsWithin(path, system) || StoragePathPolicy.IsWithin(system, path))
                throw new FilproOperationException("repair.path", "Un emplacement système ne peut pas être renommé comme profil.");
    }
    private void WriteRepairReceipt(string path, RepairReceipt receipt)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(receipt, RepairJsonOptions);
        var key = ReadRepairAuthenticationKey(create: true);
        try
        {
            var envelope = new SealedRepairReceipt(Convert.ToBase64String(payload), Convert.ToHexString(HMACSHA256.HashData(key, payload)));
            files.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(envelope, RepairJsonOptions), true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private RepairReceipt ReadRepairReceipt(string path) => ReadRepairReceipt(path, out _);

    // L'empreinte porte sur les octets vérifiés : elle lie la confirmation Windows au reçu relu au démarrage.
    private RepairReceipt ReadRepairReceipt(string path, out string receiptSha256)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        if (!StoragePathPolicy.IsWithin(path, options.StateDirectory, false) || Path.GetFileName(path) != "repair-receipt.json"
            || !Guid.TryParseExact(Path.GetFileName(Path.GetDirectoryName(path)), "N", out _))
            throw new FilproOperationException("repair.receipt", "Choisir un reçu émis dans le dossier privé des opérations FILPRO.");
        var bytes = files.ReadBounded(path, BackupManifestCodec.MaximumBytes);
        receiptSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        using var document = JsonDocument.Parse(bytes); BackupManifestCodec.RejectDuplicateProperties(document.RootElement);
        var envelope = JsonSerializer.Deserialize<SealedRepairReceipt>(bytes, RepairJsonOptions)
            ?? throw new FilproOperationException("repair.receipt", "Reçu vide.");
        var payload = Convert.FromBase64String(envelope.Payload);
        var key = ReadRepairAuthenticationKey(create: false);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), Convert.FromHexString(envelope.Signature)))
                throw new FilproOperationException("repair.receipt", "Le reçu de réparation a été modifié.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        using var decoded = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
        BackupManifestCodec.RejectDuplicateProperties(decoded.RootElement);
        var receipt = JsonSerializer.Deserialize<RepairReceipt>(payload, RepairJsonOptions) ?? throw new FilproOperationException("repair.receipt", "Reçu illisible.");
        SoftwareDiscoveryInputs.ValidateSid(receipt.Sid);
        if (receipt.SchemaVersion != 1 || !Guid.TryParseExact(receipt.Id, "N", out _)
            || receipt.PreservedPath != receipt.OriginalPath + ".old-filpro-" + receipt.Id[..8]
            || receipt.RegistryBackup is null || receipt.HiveBackup is null
            || receipt.RegistryBackup.Path != "profile-list.json" || receipt.HiveBackup.Path != "NTUSER.DAT"
            || !RepairReconciliation.IsKnownStage(receipt.Stage))
            throw new FilproOperationException("repair.receipt", "Structure du reçu refusée.");
        _ = StoragePathPolicy.NormalizeWindows(receipt.OriginalPath); _ = StoragePathPolicy.NormalizeWindows(receipt.PreservedPath);
        _ = StoragePathPolicy.NormalizeWindows(receipt.BackupDirectory); _ = StoragePathPolicy.NormalizeWindows(receipt.SafetyBundle);
        BackupManifestCodec.ValidateReference(receipt.RegistryBackup); BackupManifestCodec.ValidateReference(receipt.HiveBackup);
        if (receipt.RegistryBackup.Bytes > BackupManifestCodec.MaximumBytes || receipt.HiveBackup.Bytes <= 0)
            throw new FilproOperationException("repair.receipt", "Sauvegardes du reçu hors limites.");
        if (receipt.NewProfilePreservedPath is not null || receipt.NewRegistryBackup is not null || receipt.NewHiveBackup is not null)
        {
            if (receipt.NewProfilePreservedPath is null || receipt.NewRegistryBackup is null || receipt.NewHiveBackup is null)
                throw new FilproOperationException("repair.receipt", "L'ancien reçu ne lie pas toutes les sauvegardes du nouveau profil ; examen manuel requis.");
            const string prefix = "new-profile-list-";
            var name = receipt.NewRegistryBackup.Path;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
                throw new FilproOperationException("repair.receipt", "Nom de sauvegarde du nouveau profil invalide.");
            var id = name[prefix.Length..^5];
            if (!Guid.TryParseExact(id, "N", out _) || receipt.NewHiveBackup.Path != "new-NTUSER-" + id + ".DAT"
                || receipt.NewProfilePreservedPath != receipt.OriginalPath + ".new-filpro-" + id[..8])
                throw new FilproOperationException("repair.receipt", "Les chemins du nouveau profil ne correspondent pas aux sauvegardes scellées.");
            _ = StoragePathPolicy.NormalizeWindows(receipt.NewProfilePreservedPath);
            BackupManifestCodec.ValidateReference(receipt.NewRegistryBackup); BackupManifestCodec.ValidateReference(receipt.NewHiveBackup);
            if (receipt.NewRegistryBackup.Bytes > BackupManifestCodec.MaximumBytes || receipt.NewHiveBackup.Bytes <= 0)
                throw new FilproOperationException("repair.receipt", "Sauvegardes du nouveau profil hors limites.");
        }
        return receipt;
    }
    private byte[] ReadRepairAuthenticationKey(bool create)
    {
        var path = Path.Combine(options.StateDirectory, "repair-authentication.key");
        if (!files.FilePresent(path))
        {
            if (!create) throw new FilproOperationException("repair.receipt", "Clé locale d'authentification des reçus absente.");
            files.WriteAtomic(path, RandomNumberGenerator.GetBytes(32), false);
        }
        var key = files.ReadBounded(path, 32);
        if (key.Length != 32) throw new FilproOperationException("repair.receipt", "Clé locale des reçus invalide.");
        return key;
    }
}
