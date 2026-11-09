using System.Text;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Configuration V2 distincte des préférences V1 et des sélections par profil.</summary>
public sealed class LocalConfigurationService : IFilproConfigurationService, IDisposable
{
    private sealed record Document(SharedConfiguration Settings, IReadOnlyList<string> Decisions, string Revision);
    private sealed record Pending(ConfigurationPreview View, SharedConfiguration Settings, string? SourcePath,
        string? SourceHash, string? ManifestHash, bool SourceAbsent);
    private readonly string _packRoot;
    private readonly string _dataRoot;
    private readonly string _statePath;
    private readonly string _backupRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (Pending Pending, DateTimeOffset Expires)> _previews = new(StringComparer.Ordinal);
    private PackValidationResult? _startupPack;
    public bool Demonstration => false;

    public LocalConfigurationService(string packRoot, string dataRoot)
    {
        _packRoot = StoragePathPolicy.NormalizeNative(packRoot); _dataRoot = StoragePathPolicy.NormalizeNative(dataRoot);
        if (StoragePathPolicy.IsWithin(_dataRoot, _packRoot) || StoragePathPolicy.IsWithin(_packRoot, _dataRoot))
            throw new FilproOperationException("configuration.location", "Le stockage des réglages doit être distinct du pack.");
        _statePath = Path.Combine(_dataRoot, "configuration-v2.json"); _backupRoot = Path.Combine(_dataRoot, "configuration-backups");
    }

    /// <summary>Composition native seulement, après son hash complet et avant exposition du pont. Le manifeste reste lié aux mêmes octets.</summary>
    public async Task UseStartupValidationAsync(PackValidationResult verified, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verified);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = ConfigurationFileIO.Read(Path.Combine(_packRoot, PackManifestCodec.ManifestPath), PackManifestCodec.MaximumManifestBytes);
            var manifest = PackManifestCodec.Decode(bytes);
            if (ConfigurationFileIO.Hash(bytes) != verified.ManifestSha256 || verified.VerifiedFiles != manifest.Files.Count
                || verified.VerifiedBytes != manifest.Files.Sum(file => file.Size))
                throw new FilproOperationException("pack.proof", "La vérification de démarrage ne correspond pas au manifeste complet de ce pack.");
            _startupPack = verified with { Manifest = manifest };
        }
        finally { _gate.Release(); }
    }

    /// <summary>Lecture seule du document existant : jamais de création, d'aperçu ni de validation du pack.</summary>
    public async Task<string?> ReadThemeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ConfigurationFileIO.NoLinks(_statePath);
            return File.Exists(_statePath) ? DecodeState(ConfigurationFileIO.Read(_statePath, 1024 * 1024)).Settings.Theme : null;
        }
        finally { _gate.Release(); }
    }

    public Task<ConfigurationState> ReadAsync(CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        var warnings = new List<string>(); var document = await EnsureDocumentAsync(warnings, cancellationToken).ConfigureAwait(false);
        return await StateAsync(document, warnings, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<ConfigurationResult> SaveAsync(SharedConfiguration settings, string expectedRevision, CancellationToken cancellationToken) => LockedAsync<ConfigurationResult>(async () =>
    {
        ConfigurationCodec.Validate(settings); var warnings = new List<string>();
        var document = await EnsureDocumentAsync(warnings, cancellationToken).ConfigureAwait(false); RequireRevision(document, expectedRevision);
        var (saved, backup) = Write(document with { Settings = settings });
        return new(false, await StateAsync(saved, warnings, cancellationToken).ConfigureAwait(false), _statePath, backup, "Réglages enregistrés. Les sélections et magasins de profils sont conservés.");
    }, cancellationToken);

    public Task<ConfigurationResult> ExportAsync(string destinationPath, bool confirmed, CancellationToken cancellationToken) => LockedAsync<ConfigurationResult>(async () =>
    {
        Confirm(confirmed); var path = StoragePathPolicy.NormalizeNative(destinationPath);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("configuration.export", "Choisir un nouveau fichier avec l'extension .json.");
        if (StoragePathPolicy.IsWithin(path, _packRoot) || StoragePathPolicy.IsWithin(path, _dataRoot))
            throw new FilproOperationException("configuration.export", "Choisir un fichier d'export distinct du pack et des réglages internes.");
        var warnings = new List<string>(); var document = await EnsureDocumentAsync(warnings, cancellationToken).ConfigureAwait(false);
        var bytes = ConfigurationCodec.Encode(document.Settings); _ = ConfigurationCodec.Decode(bytes);
        string? before = null;
        if (File.Exists(path))
        {
            var existing = ConfigurationFileIO.Read(path, ConfigurationCodec.MaximumBytes);
            try { _ = ConfigurationCodec.Decode(existing); }
            catch (FilproOperationException) { throw new FilproOperationException("configuration.export", "Ce fichier existant n'est pas un export FILPRO valide. Choisir un nouveau fichier ; aucun remplacement effectué."); }
            before = ConfigurationFileIO.Hash(existing);
        }
        var backup = ConfigurationFileIO.AtomicWrite(path, bytes, _backupRoot, before, expectAbsent: before is null);
        return new(false, await StateAsync(document, warnings, cancellationToken).ConfigureAwait(false), path, backup, "Export des seuls réglages partageables, sans chemin, compte, sélection personnelle ni secret.");
    }, cancellationToken);

    public Task<ConfigurationPreview> PreviewImportAsync(string sourcePath, CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        var document = await EnsureDocumentAsync([], cancellationToken).ConfigureAwait(false);
        var path = StoragePathPolicy.NormalizeNative(sourcePath);
        var bytes = ConfigurationFileIO.Read(path, ConfigurationCodec.MaximumBytes);
        var decoded = ConfigurationCodec.Decode(bytes, document.Settings);
        return AddPreview("import", Path.GetFileName(path), document, decoded, path, ConfigurationFileIO.Hash(bytes), null, null, false);
    }, cancellationToken);

    public Task<ConfigurationResult> ApplyImportAsync(string previewId, bool confirmed, CancellationToken cancellationToken) =>
        ApplyAsync(previewId, "import", apply: true, confirmed, cancellationToken);

    private Task<PackValidationResult> ValidateDefaultsPackAsync(CancellationToken cancellationToken)
    {
        var flavor = PackIntegrityVerifier.ReadManifest(_packRoot).Flavor;
        if (!PackManifestCodec.IsProductionFlavor(flavor))
            throw new FilproOperationException("pack.flavor", "Les défauts de distribution exigent un pack de production.");
        return PackIntegrityVerifier.ValidateAsync(_packRoot, flavor, cancellationToken: cancellationToken);
    }

    public Task<ConfigurationPreview> PreviewDefaultsAsync(CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        var document = await EnsureDocumentAsync([], cancellationToken).ConfigureAwait(false);
        var pack = await ValidateDefaultsPackAsync(cancellationToken).ConfigureAwait(false);
        var bytes = ConfigurationCodec.Encode(document.Settings);
        var path = Path.Combine(_packRoot, PackManifestCodec.DefaultsPath);
        return AddPreview("defaults", PackManifestCodec.DefaultsPath, document, ConfigurationCodec.Decode(bytes), path,
            pack.DefaultsSha256, pack.ManifestSha256, null, pack.DefaultsSha256 is null);
    }, cancellationToken);

    public Task<ConfigurationResult> ApplyDefaultsAsync(string previewId, bool confirmed, CancellationToken cancellationToken) => LockedAsync<ConfigurationResult>(async () =>
    {
        Confirm(confirmed); var pending = GetPreview(previewId, "defaults");
        var document = await EnsureDocumentAsync([], cancellationToken).ConfigureAwait(false); RequireRevision(document, pending.View.ExpectedRevision);
        var lockName = ".filpro-defaults-" + ConfigurationFileIO.Hash(Encoding.UTF8.GetBytes(_packRoot))[..16] + ".lock";
        using var packLock = ConfigurationFileIO.Lock(Path.Combine(Path.GetDirectoryName(_packRoot)!, lockName));
        var pack = await ValidateDefaultsPackAsync(cancellationToken).ConfigureAwait(false);
        if (pack.ManifestSha256 != pending.ManifestHash || pack.DefaultsSha256 != pending.SourceHash) throw ConfigurationFileIO.Stale();
        var bytes = ConfigurationCodec.Encode(pending.Settings); _ = ConfigurationCodec.Decode(bytes);
        var backup = ConfigurationFileIO.AtomicWrite(pending.SourcePath!, bytes, _backupRoot, pending.SourceHash, pending.SourceAbsent);
        try
        {
            var validated = await PackIntegrityVerifier.ValidateAsync(_packRoot, pack.Manifest.Flavor, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (validated.ManifestSha256 != pack.ManifestSha256 || validated.DefaultsSha256 != ConfigurationFileIO.Hash(bytes)) throw ConfigurationFileIO.Stale();
            _startupPack = validated;
        }
        catch
        {
            // La seule option écrite est remise à son état précédent ; les binaires ne sont jamais réécrits ici.
            using var optionParent = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(pending.SourcePath!)!);
            if (ConfigurationFileIO.Hash(ConfigurationFileIO.Read(pending.SourcePath!, ConfigurationCodec.MaximumBytes)) == ConfigurationFileIO.Hash(bytes))
            {
                if (backup is null) File.Delete(pending.SourcePath!);
                else ConfigurationFileIO.AtomicWrite(pending.SourcePath!, ConfigurationFileIO.Read(backup, ConfigurationCodec.MaximumBytes), _backupRoot, ConfigurationFileIO.Hash(bytes));
            }
            throw;
        }
        _previews.Remove(previewId);
        return new(false, await StateAsync(document, [], cancellationToken).ConfigureAwait(false), pending.SourcePath, backup,
            $"Défauts partageables écrits dans le pack {pack.Manifest.Flavor}. Le manifeste des binaires reste intact ; le ZIP distribué doit être régénéré avec sa somme SHA-256.");
    }, cancellationToken);

    public Task<ConfigurationResult> DecideBootstrapAsync(string previewId, bool apply, bool confirmed, CancellationToken cancellationToken) =>
        ApplyAsync(previewId, "bootstrap", apply, confirmed, cancellationToken);

    private Task<ConfigurationResult> ApplyAsync(string id, string kind, bool apply, bool confirmed, CancellationToken cancellationToken) => LockedAsync<ConfigurationResult>(async () =>
    {
        Confirm(confirmed); var pending = GetPreview(id, kind);
        var document = await EnsureDocumentAsync([], cancellationToken).ConfigureAwait(false); RequireRevision(document, pending.View.ExpectedRevision);
        var bytes = ConfigurationFileIO.Read(pending.SourcePath!, ConfigurationCodec.MaximumBytes);
        if (ConfigurationFileIO.Hash(bytes) != pending.SourceHash) throw ConfigurationFileIO.Stale();
        var after = ConfigurationCodec.Decode(bytes, document.Settings);
        if (kind == "bootstrap")
        {
            var pack = await PackIntegrityVerifier.ValidateAsync(_packRoot, _startupPack!.Manifest.Flavor, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (pack.ManifestSha256 != pending.ManifestHash || pack.DefaultsSha256 != pending.SourceHash) throw ConfigurationFileIO.Stale();
        }
        var decisions = kind == "bootstrap" ? document.Decisions.Append(pending.View.PackIdentity!).Distinct(StringComparer.Ordinal).TakeLast(128).ToArray() : document.Decisions;
        var (saved, backup) = Write(document with { Settings = apply ? after.Settings : document.Settings, Decisions = decisions });
        _previews.Remove(id);
        return new(false, await StateAsync(saved, [], cancellationToken).ConfigureAwait(false), _statePath, backup,
            apply ? "Configuration appliquée après sauvegarde des réglages précédents." : "Réglages actuels conservés. Ce pack ne proposera plus les mêmes défauts.");
    }, cancellationToken);

    private async Task<Document> EnsureDocumentAsync(List<string> warnings, CancellationToken cancellationToken)
    {
        ConfigurationFileIO.NoLinks(_statePath);
        if (File.Exists(_statePath)) return DecodeState(ConfigurationFileIO.Read(_statePath, 1024 * 1024));
        var settings = new SharedConfiguration(); string[] decisions = [];
        var legacyExists = File.Exists(Path.Combine(_dataRoot, "filpro.prefs.json"));
        if (legacyExists) warnings.Add("Des préférences V1 existent et sont conservées. Les réglages V2 sont stockés séparément ; aucune migration implicite.");
        var supplied = await SuppliedAsync(warnings, cancellationToken).ConfigureAwait(false);
        if (supplied is not null && !legacyExists) { settings = ConfigurationCodec.Decode(supplied.Value.Bytes).Settings; decisions = [supplied.Value.Identity]; }
        var bytes = EncodeState(settings, decisions);
        ConfigurationFileIO.AtomicWrite(_statePath, bytes, _backupRoot, null, expectAbsent: true);
        return DecodeState(bytes);
    }

    private async Task<ConfigurationState> StateAsync(Document document, List<string> warnings, CancellationToken cancellationToken)
    {
        var supplied = await SuppliedAsync(warnings, cancellationToken).ConfigureAwait(false); ConfigurationPreview? preview = null;
        if (supplied is not null && !document.Decisions.Contains(supplied.Value.Identity, StringComparer.Ordinal))
            preview = AddPreview("bootstrap", "Défauts du pack " + _startupPack!.Manifest.Version, document,
                ConfigurationCodec.Decode(supplied.Value.Bytes, document.Settings), Path.Combine(_packRoot, PackManifestCodec.DefaultsPath),
                ConfigurationFileIO.Hash(supplied.Value.Bytes), _startupPack.ManifestSha256, supplied.Value.Identity, false);
        return new(false, document.Revision, document.Settings, preview, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<(byte[] Bytes, string Identity)?> SuppliedAsync(List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var manifestPath = Path.Combine(_packRoot, PackManifestCodec.ManifestPath);
            if (!File.Exists(manifestPath)) { warnings.Add("Défauts du pack indisponibles : manifeste de distribution absent."); return null; }
            var manifestBytes = ConfigurationFileIO.Read(manifestPath, PackManifestCodec.MaximumManifestBytes);
            if (_startupPack is null || _startupPack.ManifestSha256 != ConfigurationFileIO.Hash(manifestBytes))
            {
                var manifest = PackManifestCodec.Decode(manifestBytes);
                _startupPack = await PackIntegrityVerifier.ValidateAsync(_packRoot, manifest.Flavor, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            var path = Path.Combine(_packRoot, PackManifestCodec.DefaultsPath);
            if (!File.Exists(path)) return null;
            var bytes = ConfigurationFileIO.Read(path, ConfigurationCodec.MaximumBytes); _ = ConfigurationCodec.Decode(bytes);
            return (bytes, ConfigurationFileIO.Hash(Encoding.UTF8.GetBytes(_startupPack.ManifestSha256 + ":" + ConfigurationFileIO.Hash(bytes))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FilproOperationException)
        { warnings.Add("Défauts du pack non appliqués : " + ex.Message); return null; }
    }

    private ConfigurationPreview AddPreview(string kind, string label, Document document, PortableConfiguration after,
        string? path, string? sourceHash, string? manifestHash, string? identity, bool absent)
    {
        foreach (var key in _previews.Where(p => p.Value.Expires < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _previews.Remove(key);
        if (_previews.Count >= 32) _previews.Remove(_previews.Keys.First());
        var view = new ConfigurationPreview(Guid.NewGuid().ToString("N"), kind, false, label,
            ConfigurationFileIO.Hash(ConfigurationCodec.Encode(after.Settings)), document.Revision,
            ConfigurationCodec.Changes(document.Settings, after), identity, kind == "defaults"
                ? "Confirmation : écrire les seuls réglages partageables dans ce pack de production. Si le pack est en lecture seule, créer explicitement une copie complète modifiable, lancer cette copie puis définir ses défauts. Aucun repli silencieux dans AppData."
                : "Aucun secret ni donnée personnelle. Les champs absents de l'import restent inchangés. Confirmation requise.");
        _previews.Add(view.Id, (new(view, after.Settings, path, sourceHash, manifestHash, absent), DateTimeOffset.UtcNow.AddMinutes(15)));
        return view;
    }
    private Pending GetPreview(string id, string kind) => _previews.TryGetValue(id, out var value) && value.Expires >= DateTimeOffset.UtcNow && value.Pending.View.Kind == kind
        ? value.Pending : throw new FilproOperationException("configuration.preview", "Aperçu absent ou expiré. Refaire l'aperçu.");
    private (Document Document, string? Backup) Write(Document document)
    {
        var bytes = EncodeState(document.Settings, document.Decisions);
        var backup = ConfigurationFileIO.AtomicWrite(_statePath, bytes, _backupRoot, document.Revision);
        return (DecodeState(bytes), backup);
    }
    private static byte[] EncodeState(SharedConfiguration settings, IReadOnlyList<string> decisions)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WritePropertyName("configuration");
            writer.WriteRawValue(ConfigurationCodec.Encode(settings)); writer.WriteStartArray("decisions");
            foreach (var decision in decisions) writer.WriteStringValue(decision); writer.WriteEndArray(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
    private static Document DecodeState(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 }); var root = json.RootElement;
            var fields = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (fields.Length != 3 || fields.Distinct(StringComparer.Ordinal).Count() != 3 || !fields.Order().SequenceEqual(new[] { "configuration", "decisions", "schemaVersion" })) throw ConfigurationFileIO.Stale();
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw ConfigurationFileIO.Stale();
            var settings = ConfigurationCodec.Decode(Encoding.UTF8.GetBytes(root.GetProperty("configuration").GetRawText()));
            if (settings.PresentFields.Count != ConfigurationCodec.Fields.Count) throw ConfigurationFileIO.Stale();
            var decisions = root.GetProperty("decisions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            if (decisions.Length > 128 || decisions.Any(p => !PackManifestCodec.IsSha256(p)) || decisions.Distinct(StringComparer.Ordinal).Count() != decisions.Length) throw ConfigurationFileIO.Stale();
            return new(settings.Settings, decisions, ConfigurationFileIO.Hash(bytes));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { throw new FilproOperationException("configuration.state", "Réglages existants illisibles ou version inconnue. Ils sont conservés ; aucune réinitialisation automatique."); }
    }
    private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { using var locked = ConfigurationFileIO.Lock(Path.Combine(_dataRoot, "configuration-v2.lock")); return await action().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new FilproOperationException("configuration.storage", "Écriture ou lecture de configuration impossible ; vérifier les droits et le support. " + ex.Message); }
        finally { _gate.Release(); }
    }
    private static void RequireRevision(Document document, string expected) { if (document.Revision != expected) throw ConfigurationFileIO.Stale(); }
    private static void Confirm(bool confirmed) { if (!confirmed) throw new FilproOperationException("configuration.confirm", "Confirmation explicite requise avant cette action."); }
    public void Dispose() => _gate.Dispose();
}
