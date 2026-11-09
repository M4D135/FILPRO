using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class WindowsFilproExecutionService
{
    private async Task<ExecutionOutcome> BackupAsync(BackupPlanReview plan, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        context.Report(ExecutionState.Preparing, "Contrôle des profils et du support");
        var destination = StoragePathPolicy.NormalizeWindows(plan.Destination);
        foreach (var profile in plan.Profiles)
            if (StoragePathPolicy.IsWithin(destination, profile.ProfilePath) || StoragePathPolicy.IsWithin(profile.ProfilePath, destination))
                throw new FilproOperationException("backup.destination", "Destination et profil source ne peuvent pas être imbriqués.");
        files.CreateDirectories(destination);
        using var destinationLease = files.HoldDirectory(destination);
        var id = Guid.NewGuid().ToString("N");
        var bundle = Path.Combine(destination, "FILPRO-" + clock.GetUtcNow().ToString("yyyyMMdd-HHmmss") + "-" + id[..8]);
        using var bundleLease = files.CreateNewDirectory(bundle);
        files.CreateDirectories(Path.Combine(bundle, "Store"));
        files.CreateDirectories(Path.Combine(bundle, "Rules"));
        files.CreateDirectories(Path.Combine(bundle, "Catalog"));
        var logs = Path.Combine(NewLogDirectory(context.Id), "backup-" + id[..8]);
        files.CreateDirectories(logs);
        var manifestPath = Path.Combine(bundle, "filpro-backup.json");
        var warnings = new List<string>
        {
            "USMT conserve des données et réglages ; les applications devront être installées et vérifiées séparément.",
            "Le succès VSS ne prouve pas la cohérence transactionnelle de chaque application ouverte.",
        };
        if (plan.MigApp) warnings.Add("MigApp ajoute son propre périmètre applicatif Microsoft à la sélection FILPRO.");
        if (plan.NoCompress) warnings.Add("Magasin non compressé : contrôle des fichiers par SHA-256, distinct de /verify USMT. Au plus 100 000 fichiers et 16 384 dossiers par profil ; Microsoft recommande la compression pour les migrations de production.");
        var rows = new List<BackupManifestProfile>();
        BackupManifest? manifest = null;
        using var keyLease = OpenKeyLease(plan.Encrypted, plan.KeyPath, bundle);
        try
        {
            foreach (var profile in plan.Profiles)
            {
                token.ThrowIfCancellationRequested();
                context.Report(ExecutionState.Preparing, "Préparation des fichiers et clés sélectionnés", profile.Sid);
                profiles.Match(profile.Sid, profile.Account, profile.ProfilePath, false, token);
                var scope = new ProfileFileScopeReader(files).Read(profile.ProfilePath, profile.Files, plan.KeyPath, token);
                await RequireRegistrySelectionAsync(profile, token).ConfigureAwait(false);
                warnings.AddRange(scope.Warnings.Take(Math.Max(0, 900 - warnings.Count)));
                var profileId = Guid.NewGuid().ToString("N");
                var ruleDirectory = Path.Combine(bundle, "Rules", profileId);
                files.CreateDirectories(ruleDirectory);
                var row = new BackupManifestProfile(profileId, profile, "Store/" + profileId, "Preparing", [], null, null, null,
                    "NotRun", scope.PersonalFolders, scope.ExcludedPaths);
                var ruleRelative = "Rules/" + profileId + "/filpro-config.xml";
                var rulePath = ExecutionFileSystem.Resolve(bundle, ruleRelative);
                files.WriteAtomic(rulePath, UsmtMigrationRules.Capture(row), false);
                var rules = new List<ManifestFileReference> { await files.DescribeAsync(bundle, ruleRelative, token).ConfigureAwait(false) };
                if (plan.MigApp)
                {
                    var relative = "Rules/" + profileId + "/MigApp.xml";
                    files.CopyNew(Path.Combine(options.UsmtRoot, "MigApp.xml"), ExecutionFileSystem.Resolve(bundle, relative));
                    rules.Add(await files.DescribeAsync(bundle, relative, token).ConfigureAwait(false));
                }
                row = row with { Rules = rules };
                var profileLogs = Path.Combine(logs, profileId); files.CreateDirectories(profileLogs);
                var generated = Path.Combine(profileLogs, "generated-config.xml");
                using var preparationInputs = new ExecutionInputLease(files);
                foreach (var rule in rules)
                    await preparationInputs.HoldAsync(ExecutionFileSystem.Resolve(bundle, rule.Path), rule.Sha256, rule.Bytes, token).ConfigureAwait(false);
                var arguments = rules.Select(r => "/i:" + ExecutionFileSystem.Resolve(bundle, r.Path)).ToList();
                arguments.AddRange(["/genconfig:" + generated, "/v:5", "/l:" + Path.Combine(profileLogs, "genconfig.log")]);
                context.Report(ExecutionState.Preparing, "Préparation de la configuration USMT du profil", profile.Sid);
                RequireSuccess(await RunAsync("scanstate.exe", arguments, profileLogs, "genconfig", token).ConfigureAwait(false), "ScanState /genconfig");
                var generatedBytes = files.ReadBounded(generated, BackupManifestCodec.MaximumBytes);
                var configuration = UsmtMigrationRules.DisableWindowsComponents(generatedBytes, generatedBytes);
                var configRelative = "Rules/" + profileId + "/profile-config.xml";
                files.WriteAtomic(ExecutionFileSystem.Resolve(bundle, configRelative), configuration, false);
                row = row with { ProfileConfig = await files.DescribeAsync(bundle, configRelative, token).ConfigureAwait(false) };
                rows.Add(row);
            }
            manifest = new(BackupManifestCodec.Schema, BackupManifestCodec.Kind, id, ProductInfo.Version, clock.GetUtcNow(),
                Environment.MachineName, UsmtVersion(), "Preparing", false, !plan.NoCompress, plan.Encrypted, plan.ContinueOnError, plan.UseVss,
                rows, warnings, plan.MigApp);
            SaveManifest();
            for (var index = 0; index < rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var row = rows[index];
                var profile = profiles.Match(row.Selection.Sid, row.Selection.Account, row.Selection.ProfilePath, false, token);
                if (profile.Loaded != false && !plan.UseVss) throw new FilproOperationException("backup.vss", "Le profil s'est chargé avant la copie ; VSS non confirmé.");
                using var sourceRootLease = files.HoldDirectory(profile.LocalPath!);
                var profileLogs = Path.Combine(logs, row.Id);
                var store = ExecutionFileSystem.Resolve(bundle, row.Store);
                var rulePaths = row.Rules.Select(r => ExecutionFileSystem.Resolve(bundle, r.Path)).ToArray();
                var config = ExecutionFileSystem.Resolve(bundle, row.ProfileConfig!.Path);
                using var inputs = new ExecutionInputLease(files);
                foreach (var reference in row.Rules.Append(row.ProfileConfig!))
                    await inputs.HoldAsync(ExecutionFileSystem.Resolve(bundle, reference.Path), reference.Sha256, reference.Bytes, token).ConfigureAwait(false);
                var single = plan with { Profiles = [row.Selection], Destination = bundle };
                var estimateFile = Path.Combine(profileLogs, "estimate.xml");
                var request = UsmtArgumentRequest.ForBackup(single, UsmtAction.Estimate, store, profileLogs, rulePaths, estimateFile) with { Config = config };
                var estimateInvocation = UsmtArguments.Build(request);
                context.Report(ExecutionState.Preparing, "Estimation USMT de l'espace nécessaire", profile.Sid);
                var estimateResult = await RunAsync(estimateInvocation.Executable, estimateInvocation.Arguments, profileLogs, "estimate", token, context, profile.Sid).ConfigureAwait(false);
                RequireSuccess(estimateResult, "Estimation USMT", plan.ContinueOnError);
                var estimate = UsmtMigrationRules.ReadEstimate(files.ReadBounded(estimateFile, BackupManifestCodec.MaximumBytes), ClusterBytes(destination));
                if (estimate.Store <= 0) throw new FilproOperationException("storage.space", "L'estimation du magasin n'est pas exploitable ; aucune capture.");
                RequireSpace(destination, estimate.Store, estimate.Temporary, options.StateDirectory);
                rows[index] = row with { Status = "Running" }; manifest = manifest with { Status = "Running" }; SaveManifest();
                context.Report(ExecutionState.Running, "Capture USMT des fichiers et réglages", profile.Sid, index, rows.Count);
                var invocation = UsmtArguments.Build(UsmtArgumentRequest.ForBackup(single, UsmtAction.Scan, store, profileLogs, rulePaths) with { Config = config });
                var result = await RunAsync(invocation.Executable, invocation.Arguments, profileLogs, "scanstate", token, context, profile.Sid).ConfigureAwait(false);
                RequireSuccess(result, "ScanState", plan.ContinueOnError);
                BackupCatalog catalog;
                ManifestFileReference payloadReference;
                if (plan.NoCompress)
                {
                    context.Report(ExecutionState.Running, "Indexation SHA-256 du magasin non compressé", profile.Sid);
                    var indexed = await new PlainStoreVerifier(files).CaptureAsync(bundle, row, token).ConfigureAwait(false);
                    catalog = indexed.Catalog; payloadReference = indexed.Index;
                }
                else
                {
                    var payloadRelative = row.Store + "/USMT/USMT.MIG";
                    var payload = ExecutionFileSystem.Resolve(bundle, payloadRelative);
                    files.RequireFile(payload);
                    context.Report(ExecutionState.Running, "Contrôle d'intégrité du magasin USMT", profile.Sid);
                    var verifyArguments = new List<string> { "/verify:all", payload, "/l:" + Path.Combine(profileLogs, "verify.log") };
                    verifyArguments.AddRange(Crypto(plan.Encrypted, plan.KeyPath));
                    var verification = await RunAsync("usmtutils.exe", verifyArguments, profileLogs, "verify", token).ConfigureAwait(false);
                    RequireSuccess(verification, "USMT /verify");
                    catalog = ExecutionCatalogCodec.ReadUsmtOutput(row.Id, WindowsExecutionProcess.ReadCatalogOutput(verification.StandardOutputPath), token);
                    if (catalog.NativeIntegrityValid != true || catalog.Entries.Any(e => e.Corrupted))
                        throw new FilproOperationException("store.integrity", "Le rapport USMT est incomplet, non reconnu ou signale un contenu corrompu.");
                    context.Report(ExecutionState.Running, "Calcul de l'empreinte du magasin", profile.Sid);
                    payloadReference = await files.DescribeAsync(bundle, payloadRelative, token).ConfigureAwait(false);
                }
                var catalogRelative = "Catalog/" + row.Id + ".json";
                files.WriteAtomic(ExecutionFileSystem.Resolve(bundle, catalogRelative), ExecutionCatalogCodec.Encode(catalog), false);
                if (!catalog.Complete) warnings.Add("Le catalogue visible de " + profile.Account + " est incomplet ou borné ; il ne remplace pas la preuve d'intégrité du magasin.");
                rows[index] = row with
                {
                    Status = result.ExitCode == 3 || estimateResult.ExitCode == 3 || result.IgnoredErrors > 0 || estimateResult.IgnoredErrors > 0 ? "Partial" : "Ready",
                    Payload = payloadReference, Catalog = await files.DescribeAsync(bundle, catalogRelative, token).ConfigureAwait(false),
                    ExitCode = result.ExitCode, Verification = plan.NoCompress ? "FileHashesVerified" : "Verified",
                };
                SaveManifest();
            }
            manifest = manifest with { Status = rows.All(p => p.Status == "Ready") ? "Ready" : "Partial" };
            SaveManifest();
            return new(manifest.Status == "Ready" ? ExecutionState.Succeeded : ExecutionState.Partial, warnings, bundle);
        }
        catch (Exception ex)
        {
            if (manifest is not null)
            {
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i].Status is "Preparing" or "Running") rows[i] = rows[i] with { Status = ex is OperationCanceledException ? "Cancelled" : "Failed" };
                var partial = rows.Any(p => p.Status is "Ready" or "Partial");
                manifest = manifest with { Status = partial ? "Partial" : ex is OperationCanceledException ? "Cancelled" : "Failed" };
                string? evidenceFailure = null;
                try { SaveManifest(); }
                catch (Exception) { evidenceFailure = "Le dernier état du manifeste n'a pas pu être publié ; conserver le dossier pour examen : " + bundle; }
                if (partial) throw new ExecutionPartialException("backup.partial", "Certains profils seulement ont un magasin vérifié.", bundle,
                    new[] { "Les magasins terminés et les données partielles sont conservés ; aucune sauvegarde antérieure n'a été remplacée.", evidenceFailure }.Where(s => s is not null).Cast<string>().ToArray());
            }
            throw;
        }
        void SaveManifest()
        {
            manifest = manifest! with { Profiles = rows.ToArray(), Warnings = warnings.Take(1000).ToArray() };
            files.WriteAtomic(manifestPath, BackupManifestCodec.Encode(manifest), true);
        }
    }

    private async Task RequireRegistrySelectionAsync(BackupProfileSelection profile, CancellationToken token)
    {
        var loaded = new WindowsLoadedUserRegistry().OpenUserHive(profile.Sid);
        if (loaded.ErrorCode == 0 && loaded.Key is not null)
        {
            using (loaded.Key) RequireKeys(loaded.Key);
            return;
        }
        loaded.Key?.Dispose();
        if (loaded.ErrorCode is not (2 or 3)) throw new FilproOperationException("backup.registry", "Le registre du profil est inaccessible.");
        await using var privateHive = await hives.OpenCopyAsync(Path.Combine(profile.ProfilePath, "NTUSER.DAT"), token).ConfigureAwait(false);
        RequireKeys(privateHive.Root);
        void RequireKeys(IReadOnlyUserRegistryKey root)
        {
            WindowsProfileSafety.RequireUserFolders(root, profile.ProfilePath, files, token);
            foreach (var selected in profile.Registry)
            {
                token.ThrowIfCancellationRequested();
                var opened = root.OpenSubKey(selected.Rel);
                using var key = opened.Key;
                if (opened.ErrorCode != 0 || key is null) throw new FilproOperationException("backup.registry", "Une clé sélectionnée est absente ou inaccessible : " + selected.Rel);
            }
            token.ThrowIfCancellationRequested();
        }
    }

    private async Task<ExecutionOutcome> RestoreAsync(RestorePlanReview plan, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var store = await ReadStoreAsync(plan.Bundle, token).ConfigureAwait(false); MatchStore(plan, store);
        if (store.V1 is not null) return await RestoreV1Async(plan, context).ConfigureAwait(false);
        var logs = NewLogDirectory(context.Id);
        var receipt = Path.Combine(logs, "restore-receipt.json");
        var results = new List<object>();
        var mutated = false;
        string? safetyBundle = null;
        try
        {
            context.Report(ExecutionState.Preparing, "Contrôle du magasin confirmé");
            var manifest = store.Manifest!;
            if (manifest.UsmtVersion != UsmtVersion()) throw new FilproOperationException("usmt.version", "La version USMT diffère de celle du magasin ; compatibilité non prouvée.");
            using var bundleLease = files.HoldDirectory(plan.Bundle);
            using var storeInputs = new ExecutionInputLease(files);
            await storeInputs.HoldAsync(Path.Combine(plan.Bundle, "filpro-backup.json"), plan.SourceManifestSha256!, null, token).ConfigureAwait(false);
            using var keyLease = OpenKeyLease(manifest.Encrypted, plan.SourceKeyPath, plan.Bundle);
            safetyBundle = await CapturePriorAsync(plan, manifest.Profiles.Select(p => p.Selection).ToArray(), context).ConfigureAwait(false);
            WriteReceipt(receipt, new { kind = "Restore", stage = "PriorBackupReady", bundle = plan.Bundle, safetyBundle, results });
            foreach (var mapping in plan.Mappings)
            {
                token.ThrowIfCancellationRequested();
                var target = plan.Targets.Single(t => t.Sid == mapping.TargetSid);
                profiles.Match(target.Sid, target.Account, target.ProfilePath, true, token);
                var current = await ReadStoreAsync(plan.Bundle, token).ConfigureAwait(false); MatchStore(plan, current);
                var row = manifest.Profiles.Single(p => p.Selection.Account.Equals(mapping.Source, StringComparison.OrdinalIgnoreCase));
                using var targetRootLease = files.HoldDirectory(target.ProfilePath);
                var profileLogs = Path.Combine(logs, row.Id); files.CreateDirectories(profileLogs);
                var payload = ExecutionFileSystem.Resolve(plan.Bundle, row.Payload!.Path);
                using var payloadInputs = new ExecutionInputLease(files);
                using var plainInputs = !manifest.Compressed
                    ? await new PlainStoreVerifier(files).VerifyAndHoldAsync(plan.Bundle, row, token).ConfigureAwait(false) : null;
                if (manifest.Compressed)
                {
                    await payloadInputs.HoldAsync(payload, row.Payload.Sha256, row.Payload.Bytes, token).ConfigureAwait(false);
                    var verify = new List<string> { "/verify", payload, "/l:" + Path.Combine(profileLogs, "verify.log") };
                    verify.AddRange(Crypto(manifest.Encrypted, plan.SourceKeyPath));
                    context.Report(ExecutionState.Preparing, "Vérification USMT avant écriture du profil", target.Sid);
                    RequireSuccess(await RunAsync("usmtutils.exe", verify, profileLogs, "verify", token).ConfigureAwait(false), "USMT /verify");
                }
                var groups = SoftwareCatalog.Group(row.Selection.Files, row.Selection.Registry);
                var groupIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
                var policies = RestoreConflictPolicies.Resolve(groups, plan.ConflictPolicies.Where(p => groupIds.Contains(p.Id)).ToArray());
                var derivedRule = Path.Combine(profileLogs, "filpro-config.xml");
                var derivedBytes = UsmtMigrationRules.Restore(row, policies);
                files.WriteAtomic(derivedRule, derivedBytes, false);
                using var ruleInputs = new ExecutionInputLease(files);
                await ruleInputs.HoldAsync(derivedRule, BackupManifestCodec.Hash(derivedBytes), derivedBytes.Length, token).ConfigureAwait(false);
                var rules = new List<string> { derivedRule };
                var privateRules = Path.Combine(profileLogs, "Inputs");
                using var privateRulesLease = files.CreateNewDirectory(privateRules);
                var configuration = await ruleInputs.CopyConfigurationAsync(plan.Bundle, row.ProfileConfig!, privateRules, token).ConfigureAwait(false);
                if (manifest.MigApp)
                {
                    var local = await files.DescribeAsync(options.UsmtRoot, "MigApp.xml", token).ConfigureAwait(false);
                    if (local.Sha256 != row.Rules[1].Sha256) throw new FilproOperationException("usmt.rules", "MigApp ne correspond pas au pack vérifié courant.");
                    rules.Add(await ruleInputs.CopyRuleAsync(plan.Bundle, row.Rules[1], privateRules, token).ConfigureAwait(false));
                }
                var single = plan with { Users = [mapping.Source], Mappings = [mapping], Targets = [target] };
                var request = UsmtArgumentRequest.ForRestore(single, ExecutionFileSystem.Resolve(plan.Bundle, row.Store), profileLogs, rules,
                    configuration, noCompress: !manifest.Compressed, continueOnError: manifest.ContinueOnError);
                var invocation = UsmtArguments.Build(request);
                using var targetScopes = new RestoreTargetScopeGuard(files).Acquire(target.ProfilePath, row, token);
                await RequireRegistrySelectionAsync(new(target.Sid, target.IdentityKind, target.Account, target.ProfilePath, false, [], []), token).ConfigureAwait(false);
                profiles.Match(target.Sid, target.Account, target.ProfilePath, true, token);
                WriteReceipt(receipt, new { kind = "Restore", stage = "Applying", target.Sid, bundle = plan.Bundle, safetyBundle, results });
                mutated = true;
                context.Report(ExecutionState.Running, "Restauration des fichiers et réglages USMT", target.Sid);
                var loaded = await RunAsync(invocation.Executable, invocation.Arguments, profileLogs, "loadstate", token, context, target.Sid).ConfigureAwait(false);
                RequireSuccess(loaded, "LoadState", manifest.ContinueOnError);
                results.Add(new { mapping.Source, mapping.Target, target.Sid, loaded.ExitCode, loaded.IgnoredErrors,
                    state = loaded.ExitCode == 0 && loaded.IgnoredErrors is not > 0 ? "Applied" : "Partial" });
                if (loaded.ExitCode != 0 || loaded.IgnoredErrors > 0) throw new ExecutionPartialException("restore.partial", "LoadState a signalé des erreurs ; aucune réussite globale.", receipt,
                    ["La sauvegarde préalable est conservée : " + safetyBundle, "USMT n'offre pas d'annulation transactionnelle de ses ajouts ; revue et reprise nécessaires."]);
                WriteReceipt(receipt, new { kind = "Restore", stage = "ProfileApplied", bundle = plan.Bundle, safetyBundle, results });
            }
            WriteReceipt(receipt, new { kind = "Restore", stage = "AppliedAwaitingUserValidation", bundle = plan.Bundle, safetyBundle, results });
            return new(ExecutionState.Succeeded,
                ["LoadState a terminé. Vérifier l'ouverture de session, les fichiers et les applications avec l'utilisateur.", "Sauvegarde préalable conservée : " + safetyBundle], receipt);
        }
        catch (Exception ex)
        {
            var evidenceFailure = TryWriteReceipt(receipt, new { kind = "Restore", stage = mutated ? "PartialRequiresReview" : "StoppedBeforeTargetWrite", bundle = plan.Bundle,
                safetyBundle, expected = plan.Mappings, results, cancelled = ex is OperationCanceledException });
            if (mutated) throw new ExecutionPartialException("restore.partial", "La restauration s'est interrompue après une écriture possible.", receipt,
                new[] { "Aucune suppression de données pour tenter un faux retour arrière.", "Sauvegarde préalable conservée : " + safetyBundle,
                    "Correspondances attendues : " + string.Join(" ; ", plan.Mappings.Select(m => m.Source + " → " + m.Target)),
                    evidenceFailure }.Where(s => s is not null).Cast<string>().ToArray());
            throw;
        }
    }

    private IDisposable? OpenKeyLease(bool encrypted, string? keyPath, string bundle)
    {
        if (!encrypted) return null;
        if (string.IsNullOrWhiteSpace(keyPath) || StoragePathPolicy.IsWithin(keyPath, bundle))
            throw new FilproOperationException("usmt.crypto", "Le fichier clé doit exister hors du magasin.");
        var parents = files.HoldDirectory(Path.GetDirectoryName(keyPath)!);
        FileStream? stream = null;
        try
        {
            files.RequireFile(keyPath); stream = StorageFileIdentity.OpenRead(keyPath);
            if (stream.Length is <= 0 or > 1024 * 1024) throw new FilproOperationException("usmt.crypto", "Taille de fichier clé invalide.");
            return new KeyInputLease(stream, parents);
        }
        catch { stream?.Dispose(); parents.Dispose(); throw; }
    }

    private sealed class KeyInputLease(FileStream stream, IDisposable parents) : IDisposable
    { public void Dispose() { stream.Dispose(); parents.Dispose(); } }

    private async Task<string> CapturePriorAsync(RestorePlanReview plan, IReadOnlyList<BackupProfileSelection> selections,
        ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var priorProfiles = new List<BackupProfileSelection>();
        foreach (var target in plan.Targets)
        {
            profiles.Match(target.Sid, target.Account, target.ProfilePath, true, token);
            var inventory = source.ReadSoftware(target.Sid, RestorePriorSelection.Query(target.Sid, plan.Mappings, selections), token);
            if (!inventory.Discovery.FilesComplete || !inventory.Discovery.RegistryComplete)
                throw new FilproOperationException("restore.prior", "Inventaire incomplet : la sauvegarde préalable ne peut pas couvrir la cible et les portées à appliquer.");
            priorProfiles.Add(new(target.Sid, target.IdentityKind, target.Account, target.ProfilePath, false,
                inventory.Groups.SelectMany(g => g.Files).Select(f => f with { Selected = true }).ToArray(),
                inventory.Groups.SelectMany(g => g.Registry).Select(r => r with { Selected = true }).ToArray()));
        }
        if (string.IsNullOrWhiteSpace(plan.PriorBackup.Root)) throw new FilproOperationException("restore.prior", "Emplacement de sauvegarde préalable requis.");
        var prior = BackupPlan.Create(new(priorProfiles, plan.PriorBackup.Root, Encrypted: plan.PriorBackup.KeyPath is not null,
            KeyPath: plan.PriorBackup.KeyPath, Purpose: "Sauvegarde préalable à une restauration"), Guid.NewGuid().ToString("N"), clock.GetUtcNow());
        context.Report(ExecutionState.Preparing, "Sauvegarde préalable vérifiée des profils cibles et des portées du magasin");
        var safety = await BackupAsync(prior, context).ConfigureAwait(false);
        if (safety.State != ExecutionState.Succeeded || safety.ResultPath is null)
            throw new FilproOperationException("restore.prior", "La sauvegarde préalable est partielle ; aucune cible ne sera modifiée.");
        return safety.ResultPath;
    }
}
