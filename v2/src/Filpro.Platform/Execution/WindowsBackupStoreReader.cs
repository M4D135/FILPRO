using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

public sealed record NativeBackupStore(BackupManifest? Manifest, ExecutionStoreInspection Inspection,
    IReadOnlyList<BackupCatalogEntry> Catalog, NativeV1Store? V1 = null);
public sealed record NativeV1Store(string ProfileId, bool Compressed, bool ContinueOnError, bool MigApp,
    IReadOnlyList<ManifestFileReference> Rules, ManifestFileReference? ProfileConfig,
    LegacySoftwareSelection Selection, IReadOnlyList<string> RestoreBlockers, ManifestFileReference? Payload = null,
    string? PayloadRoot = null);

/// <summary>Lecture bornée. Les empreintes de règles/catalogues sont contrôlées ; le lourd /verify appartient au démarrage confirmé.</summary>
public sealed class WindowsBackupStoreReader(ExecutionFileSystem files, string? trustedUsmtRoot = null)
{
    public async Task<NativeBackupStore> ReadAsync(string bundle, CancellationToken token)
    {
        bundle = StoragePathPolicy.NormalizeNative(bundle);
        using var lease = files.HoldDirectory(bundle);
        token.ThrowIfCancellationRequested();
        var bytes = files.ReadBounded(Path.Combine(bundle, "filpro-backup.json"), BackupManifestCodec.MaximumBytes);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        BackupManifestCodec.RejectDuplicateProperties(json.RootElement);
        if (json.RootElement.TryGetProperty("SchemaVersion", out var legacySchema) && legacySchema.GetInt32() == 1)
            return await ReadV1Async(bundle, bytes, json.RootElement, token).ConfigureAwait(false);
        var manifest = BackupManifestCodec.Decode(bytes);
        if (manifest.Simulated) throw new FilproOperationException("store.mode", "Un magasin de démonstration ne peut pas être ouvert par le moteur réel.");
        var entries = new List<BackupCatalogEntry>();
        var warnings = manifest.Warnings.ToList();
        var catalogComplete = true;
        var catalogBytes = 256;
        var available = new List<BackupManifestProfile>();
        foreach (var profile in manifest.Profiles)
        {
            token.ThrowIfCancellationRequested();
            foreach (var rule in profile.Rules)
            {
                if (rule.Bytes > BackupManifestCodec.MaximumBytes) throw new FilproOperationException("store.rules", "Règle trop volumineuse.");
                await files.VerifyAsync(bundle, rule, token).ConfigureAwait(false);
            }
            if (profile.Rules.Count != (manifest.MigApp ? 2 : 1) || profile.Rules[0].Path != "Rules/" + profile.Id + "/filpro-config.xml"
                || profile.Rules[0].Sha256 != BackupManifestCodec.Hash(UsmtMigrationRules.Capture(profile)))
                throw new FilproOperationException("store.rules", "Les règles ne correspondent pas à la sélection fermée de ce profil.");
            if (manifest.MigApp && profile.Rules[1].Path != "Rules/" + profile.Id + "/MigApp.xml")
                throw new FilproOperationException("store.rules", "Règle MigApp inattendue.");
            if (profile.ProfileConfig is not null)
            {
                if (profile.ProfileConfig.Bytes > BackupManifestCodec.MaximumBytes) throw new FilproOperationException("store.rules", "Configuration trop volumineuse.");
                await files.VerifyAsync(bundle, profile.ProfileConfig, token).ConfigureAwait(false);
                RequireClosedProfileConfig(files.ReadBounded(ExecutionFileSystem.Resolve(bundle, profile.ProfileConfig.Path), BackupManifestCodec.MaximumBytes));
            }
            if (profile.Status is "Ready" or "Partial")
            {
                files.RequireFile(ExecutionFileSystem.Resolve(bundle, profile.Payload!.Path));
                available.Add(profile);
            }
            if (profile.Catalog is not null)
            {
                if (profile.Catalog.Bytes > ExecutionCatalogCodec.MaximumBytes) throw new FilproOperationException("store.catalog", "Catalogue trop volumineux.");
                await files.VerifyAsync(bundle, profile.Catalog, token).ConfigureAwait(false);
                var catalog = ExecutionCatalogCodec.Decode(files.ReadBounded(ExecutionFileSystem.Resolve(bundle, profile.Catalog.Path), ExecutionCatalogCodec.MaximumBytes));
                if (catalog.Entries.Any(e => e.ProfileId != profile.Id)) throw new FilproOperationException("store.catalog", "Catalogue lié à un autre profil.");
                if (catalog.Entries.Any(e => manifest.Compressed == (e.StoredPath is not null)))
                    throw new FilproOperationException("store.catalog", "Type du catalogue incompatible avec le magasin.");
                if (profile.Status is "Ready" or "Partial")
                    foreach (var entry in catalog.Entries)
                    {
                        var cost = ExecutionCatalogCodec.EntryByteCost(entry);
                        if (entries.Count == ExecutionCatalogCodec.MaximumEntries || catalogBytes + cost > ExecutionCatalogCodec.MaximumBytes)
                        { catalogComplete = false; continue; }
                        entries.Add(entry); catalogBytes += cost;
                    }
                catalogComplete &= catalog.Complete;
            }
            else catalogComplete = false;
        }
        var partial = manifest.Status == "Partial";
        var ready = (manifest.Status is "Ready" or "Partial") && available.Count > 0;
        warnings.Add("L'intégrité du contenu USMT sera contrôlée après confirmation, avant toute restauration ou extraction.");
        if (!manifest.Compressed) warnings.Add("Magasin non compressé : FileHashesVerified est une preuve SHA-256 des fichiers, pas un contrôle /verify du format USMT. Tous les fichiers seront relus avant application.");
        if (!catalogComplete) warnings.Add("Le catalogue de fichiers est absent ou incomplet ; aucune entrée manquante n'est inventée.");
        var filesSelection = available.SelectMany(p => p.Selection.Files).DistinctBy(i => (i.Scope, i.Rel.ToUpperInvariant())).ToArray();
        var registrySelection = available.SelectMany(p => p.Selection.Registry).DistinctBy(i => i.Rel.ToUpperInvariant()).ToArray();
        var inspection = new ExecutionStoreInspection(manifest.Id, bundle, BackupManifestCodec.Hash(bytes),
            new(bundle, ready, false, partial, false, manifest.Encrypted, manifest.Status,
                available.Select(p => p.Selection.Account).ToArray(), manifest.Computer, manifest.CreatedUtc.ToString("O"), manifest.UsmtVersion),
            available.Select(p => new RestoreSourceProfile(p.Selection.Account, p.Selection.Sid, p.Selection.ProfilePath)).ToArray(),
            entries.Select(e => new ExecutionStoreEntry(e.Id, e.Name, e.Bytes, e.Corrupted)).ToArray(),
            catalogComplete, false, warnings, SoftwareCatalog.Group(filesSelection, registrySelection), StoreWarnings: [.. manifest.Warnings]);
        token.ThrowIfCancellationRequested();
        return new(manifest, inspection, entries);
    }

    private async Task<NativeBackupStore> ReadV1Async(string bundle, byte[] bytes, JsonElement root, CancellationToken token)
    {
        string RequiredString(string name) => root.GetProperty(name).GetString() is { Length: > 0 } value ? value
            : throw new FilproOperationException("store.manifest", "Champ V1 absent : " + name);
        var simulated = root.GetProperty("Simulated").GetBoolean();
        if (simulated) throw new FilproOperationException("store.mode", "Magasin V1 fictif refusé par le moteur réel.");
        var compressed = root.GetProperty("Compressed").GetBoolean();
        var encrypted = root.TryGetProperty("Encrypted", out var encryption) && encryption.GetBoolean();
        if (!compressed && encrypted) throw new FilproOperationException("store.manifest", "Un magasin USMT non compressé ne peut pas être chiffré.");
        var status = RequiredString("Status");
        var users = root.GetProperty("Users").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        // La construction pure des arguments revalide les comptes explicites, leur nombre et les doublons.
        _ = UsmtArguments.Build(new(UsmtAction.Scan, @"C:\FILPRO\validation\Store", @"C:\FILPRO\validation\Logs", [], users));
        var rules = root.GetProperty("Rules").EnumerateArray().ToArray();
        if (rules.Length is < 1 or > 200) throw new FilproOperationException("store.rules", "Règles V1 absentes ou hors limites.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<ManifestFileReference>();
        var blockers = new List<string>
        {
            "LoadState V1 est désactivé : aucune référence exhaustive vérifiable des composants Windows capturés n'est conservée par ce format. Un XML hashé et un catalogue de fichiers ne prouvent pas l'absence de paramètres machine. La récupération indépendante de fichiers reste disponible.",
        };
        var selection = new LegacySoftwareSelection([], []);
        var hasDocuments = false;
        var hasMigApp = root.TryGetProperty("IncludeMigApp", out var migAppFlag) && migAppFlag.GetBoolean();
        if (hasMigApp) blockers.Add("MigApp peut écrire des paramètres machine non couverts par la sauvegarde préalable de profils.");
        foreach (var rule in rules)
        {
            var relative = BackupManifestCodec.RequireRelative(rule.GetProperty("Path").GetString() ?? "");
            if (!relative.StartsWith("Rules/", StringComparison.Ordinal) || !relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || !paths.Add(relative)) throw new FilproOperationException("store.rules", "Chemin de règle V1 invalide.");
            var ruleBytes = files.ReadBounded(ExecutionFileSystem.Resolve(bundle, relative), BackupManifestCodec.MaximumBytes);
            var hash = rule.GetProperty("SHA256").GetString();
            if (!BackupManifestCodec.Hash(ruleBytes).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new FilproOperationException("store.integrity", "Règle V1 modifiée.");
            _ = UsmtMigrationRules.Read(ruleBytes);
            var reference = new ManifestFileReference(relative, hash!.ToUpperInvariant(), ruleBytes.Length);
            BackupManifestCodec.ValidateReference(reference); references.Add(reference);
            try
            {
                switch (relative)
                {
                    case "Rules/filpro-config.xml": selection = LegacyUsmtRules.ReadSelection(ruleBytes); break;
                    case "Rules/MigDocs.xml":
                        if (trustedUsmtRoot is null) throw new FilproOperationException("restore.compatibility", "La ressource MigDocs de confiance est absente.");
                        LegacyUsmtRules.RequireProfileMigDocs(ruleBytes, files.ReadBounded(Path.Combine(trustedUsmtRoot, "MigDocs.xml"), BackupManifestCodec.MaximumBytes));
                        hasDocuments = true; break;
                    case "Rules/MigApp.xml":
                        hasMigApp = true;
                        blockers.Add("MigApp peut écrire des paramètres machine non couverts par la sauvegarde préalable de profils.");
                        break;
                    case "Rules/filpro-store-exclusion.xml": LegacyUsmtRules.RequireStoreExclusion(ruleBytes); break;
                    case "Rules/filpro-key-exclusion.xml": LegacyUsmtRules.RequireKeyExclusion(ruleBytes); break;
                    default: blockers.Add("Règle V1 non reconnue pour une restauration de profils : " + relative); break;
                }
            }
            catch (Exception ex) when (ex is FilproOperationException or IOException or UnauthorizedAccessException)
            { blockers.Add("Compatibilité non établie pour " + relative + " : " + ex.Message); }
            token.ThrowIfCancellationRequested();
        }
        if (!hasDocuments) blockers.Add("La règle MigDocs limitée aux profils n'est pas reconnue exactement.");
        ManifestFileReference? profileConfig = null;
        if (root.TryGetProperty("ProfileConfig", out var configValue) && configValue.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(configValue.GetString()))
        {
            var relative = BackupManifestCodec.RequireRelative(configValue.GetString()!);
            if (relative != "Rules/filpro-profile.config.xml" || !root.TryGetProperty("ProfileConfigSHA256", out var configHash))
                blockers.Add("Configuration V1 de profil absente ou non reconnue.");
            else
            {
                var configBytes = files.ReadBounded(ExecutionFileSystem.Resolve(bundle, relative), BackupManifestCodec.MaximumBytes);
                if (!BackupManifestCodec.Hash(configBytes).Equals(configHash.GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new FilproOperationException("store.integrity", "Configuration V1 modifiée.");
                profileConfig = new(relative, BackupManifestCodec.Hash(configBytes), configBytes.Length);
                try
                {
                    RequireClosedProfileConfig(configBytes);
                    var config = UsmtMigrationRules.Read(configBytes);
                    if (config.Root!.Element("Documents")!.Descendants("component").Any(c => (string?)c.Attribute("migrate") != "yes"))
                        blockers.Add("Le périmètre Documents V1 comporte des exclusions de composants non représentées dans la sélection.");
                }
                catch (FilproOperationException ex) { blockers.Add(ex.Message); }
            }
        }
        else blockers.Add("La V1 n'a pas conservé de configuration source excluant les composants Windows ; LoadState reste refusé.");
        var payload = compressed ? "Store/USMT/USMT.MIG" : "Store/USMT/File";
        var exists = compressed ? files.FilePresent(ExecutionFileSystem.Resolve(bundle, payload))
            : Directory.Exists(ExecutionFileSystem.Resolve(bundle, payload));
        if (!compressed && exists) { using var plainLease = files.HoldDirectory(ExecutionFileSystem.Resolve(bundle, payload)); }
        var sources = new List<RestoreSourceProfile>();
        if (root.TryGetProperty("SourceProfiles", out var sourceRows) && sourceRows.ValueKind == JsonValueKind.Array)
            foreach (var source in sourceRows.EnumerateArray())
            {
                var account = source.TryGetProperty("Account", out var accountValue) ? accountValue.GetString() : null;
                var sid = source.TryGetProperty("SID", out var sidValue) || source.TryGetProperty("Sid", out sidValue) ? sidValue.GetString() : null;
                var path = source.TryGetProperty("Path", out var pathValue) || source.TryGetProperty("ProfilePath", out pathValue) ? pathValue.GetString() : null;
                if (account is not null && users.Contains(account, StringComparer.OrdinalIgnoreCase))
                {
                    if (sid is not null) SoftwareDiscoveryInputs.ValidateSid(sid);
                    if (path is not null) _ = StoragePathPolicy.NormalizeWindows(path);
                    sources.Add(new(account, sid, path));
                }
            }
        if (sources.Count != users.Length || sources.Any(s => s.Sid is null || s.ProfilePath is null)
            || sources.Select(s => s.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            blockers.Add("Identités source V1 incomplètes ou ambiguës ; aucun SID ni chemin n'est emprunté à la cible.");
        var ready = exists && status is "Completed" or "CompletedWithWarnings" or "Partial";
        var info = new RestoreStoreInfo(bundle, ready, false, status is "CompletedWithWarnings" or "Partial", false, encrypted,
            status, users, root.TryGetProperty("Computer", out var computer) ? computer.GetString() : null,
            root.TryGetProperty("CreatedUtc", out var created) ? created.GetString() : null,
            root.TryGetProperty("UsmtVersion", out var version) ? version.GetString() : null);
        if (string.IsNullOrWhiteSpace(info.UsmtVersion)) blockers.Add("Version USMT de capture absente.");
        var id = RequiredString("Id");
        var inspection = new ExecutionStoreInspection(id, bundle, BackupManifestCodec.Hash(bytes), info,
            sources, [], false, false,
            ["Manifeste V1 reconnu : seuls les champs réellement présents sont repris. Le contenu nécessite une préparation confirmée du catalogue avant restauration ou récupération.",
                "La V1 ne conserve pas de SHA-256 de capture du contenu ; le prochain contrôle établit son état actuel, pas une authenticité historique.",
                .. blockers], SoftwareCatalog.Group(selection.Files, selection.Registry));
        await Task.CompletedTask.ConfigureAwait(false);
        return new(null, inspection, [], new NativeV1Store(BackupManifestCodec.Hash(System.Text.Encoding.UTF8.GetBytes(id))[..32].ToLowerInvariant(),
            compressed, root.TryGetProperty("ContinueOnError", out var partialFlag) && partialFlag.GetBoolean(), hasMigApp,
            references, profileConfig, selection, blockers));
    }

    internal static void RequireClosedProfileConfig(ReadOnlyMemory<byte> bytes)
    {
        _ = UsmtProfileConfiguration.Restrict(bytes, requireWindowsAlreadyExcluded: true);
    }
}
