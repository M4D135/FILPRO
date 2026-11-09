using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class WindowsFilproExecutionService
{
    /// <summary>Compatibilité bornée des magasins V1 avec preuve source ; aucun XML inconnu ni composant machine n'est appliqué.</summary>
    private async Task<ExecutionOutcome> RestoreV1Async(RestorePlanReview plan, ExecutionJobContext context)
    {
        var token = context.CancellationToken;
        var logs = NewLogDirectory(context.Id);
        var receipt = Path.Combine(logs, "restore-v1-receipt.json");
        var applied = new List<object>();
        string? safetyBundle = null;
        var mutated = false;
        try
        {
            var store = await ReadStoreAsync(plan.Bundle, token).ConfigureAwait(false); MatchStore(plan, store);
            var legacy = store.V1!;
            if (store.Inspection.Store.UsmtVersion != UsmtVersion())
                throw new FilproOperationException("usmt.version", "La version USMT V1 diffère du pack courant ; compatibilité non prouvée.");
            var scopes = LegacyRestoreScope.Resolve(store.Inspection.SourceProfiles, legacy.Selection, new(store.Inspection.CatalogComplete, store.Catalog));
            using var inputs = new ExecutionInputLease(files);
            await inputs.HoldAsync(Path.Combine(plan.Bundle, "filpro-backup.json"), plan.SourceManifestSha256!, null, token).ConfigureAwait(false);
            using var key = OpenKeyLease(plan.Encrypted, plan.SourceKeyPath, plan.Bundle);
            using var plain = !legacy.Compressed
                ? await new PlainStoreVerifier(files).VerifyAndHoldAsync(plan.Bundle,
                    new PlainStoreDescriptor(legacy.ProfileId, "Store", legacy.Payload), token, legacy.PayloadRoot).ConfigureAwait(false) : null;
            if (legacy.Compressed)
            {
                var payload = await inputs.HoldAsync(ExecutionFileSystem.Resolve(plan.Bundle, legacy.Payload!.Path),
                    legacy.Payload.Sha256, legacy.Payload.Bytes, token).ConfigureAwait(false);
                var verify = new List<string> { "/verify", payload, "/l:" + Path.Combine(logs, "v1-verify.log") };
                verify.AddRange(Crypto(plan.Encrypted, plan.SourceKeyPath));
                context.Report(ExecutionState.Preparing, "Vérification du contenu V1 lié au plan confirmé");
                RequireSuccess(await RunAsync("usmtutils.exe", verify, logs, "v1-verify", token).ConfigureAwait(false), "USMT /verify");
            }
            safetyBundle = await CapturePriorAsync(plan, scopes.Select(s => s.Selection).ToArray(), context).ConfigureAwait(false);
            WriteReceipt(receipt, new { kind = "RestoreV1", stage = "PriorBackupReady", plan.Bundle, safetyBundle, expected = plan.Mappings, applied });
            foreach (var mapping in plan.Mappings)
            {
                token.ThrowIfCancellationRequested();
                MatchStore(plan, await ReadStoreAsync(plan.Bundle, token).ConfigureAwait(false));
                var target = plan.Targets.Single(t => t.Sid == mapping.TargetSid);
                profiles.Match(target.Sid, target.Account, target.ProfilePath, true, token);
                var scope = scopes.Single(s => s.Selection.Account.Equals(mapping.Source, StringComparison.OrdinalIgnoreCase));
                var profileId = BackupManifestCodec.Hash(System.Text.Encoding.UTF8.GetBytes(scope.Selection.Sid))[..32].ToLowerInvariant();
                var profileLogs = Path.Combine(logs, "v1-" + profileId); files.CreateDirectories(profileLogs);
                var privateRules = Path.Combine(profileLogs, "Inputs");
                using var privateDirectory = files.CreateNewDirectory(privateRules);
                using var rulesLease = new ExecutionInputLease(files);
                var configuration = await rulesLease.CopyConfigurationAsync(plan.Bundle, legacy.ProfileConfig!, privateRules, token).ConfigureAwait(false);
                var rules = new List<string>();
                foreach (var rule in legacy.Rules)
                {
                    if (rule.Path == "Rules/filpro-config.xml")
                    {
                        var original = await rulesLease.HoldAsync(ExecutionFileSystem.Resolve(plan.Bundle, rule.Path), rule.Sha256, rule.Bytes, token).ConfigureAwait(false);
                        var bytes = LegacyUsmtRules.Restore(files.ReadBounded(original, BackupManifestCodec.MaximumBytes), plan.ConflictPolicies);
                        var derived = Path.Combine(profileLogs, "filpro-config.xml"); files.WriteAtomic(derived, bytes, false);
                        await rulesLease.HoldAsync(derived, BackupManifestCodec.Hash(bytes), bytes.Length, token).ConfigureAwait(false);
                        rules.Add(derived);
                    }
                    else rules.Add(await rulesLease.CopyRuleAsync(plan.Bundle, rule, privateRules, token).ConfigureAwait(false));
                }
                var saved = new BackupManifestProfile(profileId, scope.Selection, "Store", "Ready", legacy.Rules,
                    legacy.Payload, null, 0, legacy.Compressed ? "Verified" : "FileHashesVerified", scope.PersonalFolders, [], legacy.ProfileConfig);
                using var targetScope = new RestoreTargetScopeGuard(files).Acquire(target.ProfilePath, saved, token);
                await RequireRegistrySelectionAsync(new(target.Sid, target.IdentityKind, target.Account, target.ProfilePath, false, [], []), token).ConfigureAwait(false);
                profiles.Match(target.Sid, target.Account, target.ProfilePath, true, token);
                var single = plan with { Users = [mapping.Source], Mappings = [mapping], Targets = [target] };
                var invocation = UsmtArguments.Build(UsmtArgumentRequest.ForRestore(single, ExecutionFileSystem.Resolve(plan.Bundle, "Store"),
                    profileLogs, rules, configuration, noCompress: !legacy.Compressed, continueOnError: legacy.ContinueOnError));
                mutated = true;
                context.Report(ExecutionState.Running, "Application du profil V1 reconnu", target.Sid, applied.Count, plan.Mappings.Count);
                var loaded = await RunAsync("loadstate.exe", invocation.Arguments, profileLogs, "v1-loadstate", token, context, target.Sid).ConfigureAwait(false);
                RequireSuccess(loaded, "LoadState", legacy.ContinueOnError);
                applied.Add(new { mapping.Source, mapping.Target, target.Sid, loaded.ExitCode, loaded.IgnoredErrors });
                if (loaded.ExitCode != 0 || loaded.IgnoredErrors is > 0)
                    throw new FilproOperationException("restore.partial", "LoadState V1 a signalé des erreurs ; revue et reprise nécessaires.");
                WriteReceipt(receipt, new { kind = "RestoreV1", stage = "ProfileApplied", plan.Bundle, safetyBundle, expected = plan.Mappings, applied });
            }
            WriteReceipt(receipt, new { kind = "RestoreV1", stage = "AppliedAwaitingUserValidation", plan.Bundle, safetyBundle, expected = plan.Mappings, applied });
            return new(ExecutionState.Succeeded,
                ["LoadState V1 a terminé pour les profils confirmés. Vérifier leur ouverture de session et leurs données.",
                    "La preuve du contenu V1 décrit son état préparé ; aucune empreinte historique de capture n'a été inventée.",
                    "Sauvegarde préalable conservée : " + safetyBundle], receipt);
        }
        catch (Exception exception)
        {
            var evidenceFailure = TryWriteReceipt(receipt, new { kind = "RestoreV1", stage = mutated ? "PartialRequiresReview" : "StoppedBeforeTargetWrite",
                plan.Bundle, safetyBundle, expected = plan.Mappings, applied, cancelled = exception is OperationCanceledException });
            if (mutated) throw new ExecutionPartialException("restore.partial", "La restauration V1 s'est interrompue après une écriture possible.", receipt,
                new[] { "USMT ne permet pas un retour arrière transactionnel ; aucune donnée n'est supprimée automatiquement.",
                    "Sauvegarde préalable conservée : " + safetyBundle, "Reçu : " + receipt, evidenceFailure }.Where(s => s is not null).Cast<string>().ToArray());
            throw;
        }
    }
}
