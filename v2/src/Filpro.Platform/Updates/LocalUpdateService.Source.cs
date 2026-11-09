using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class LocalUpdateService
{
    private string SourcePreferencePath => Path.Combine(_dataRoot, "update-source.json");

    public Task<UpdateSourcePreference> ReadSourceAsync(CancellationToken cancellationToken) => LockedAsync(() =>
    {
        ConfigurationFileIO.NoLinks(SourcePreferencePath);
        if (!File.Exists(SourcePreferencePath)) return Task.FromResult(NoFolder);
        using var document = JsonDocument.Parse(ConfigurationFileIO.Read(SourcePreferencePath, 16 * 1024), new JsonDocumentOptions { MaxDepth = 3 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw InvalidSourcePreference();
        var names = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(new[] { "application", "folder", "kind", "schemaVersion" })
            || root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("application").GetString() != "FILPRO") throw InvalidSourcePreference();
        var kind = root.GetProperty("kind").GetInt32(); var folder = root.GetProperty("folder").GetString();
        // Choix GitHub Releases enregistré par une version antérieure : cette source n'existe plus, un dossier reste à renseigner.
        if (kind == RetiredGitHubReleases && folder is null) return Task.FromResult(NoFolder);
        return Task.FromResult(ValidateSource((UpdateSourceKind)kind, folder));
    }, cancellationToken);

    public Task<UpdateSourcePreference> SaveSourceAsync(UpdateSourceKind kind, string? folder, CancellationToken cancellationToken) => LockedAsync(() =>
    {
        var preference = ValidateSource(kind, folder);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, application = "FILPRO", kind = (int)preference.Kind, folder = preference.Folder });
        ConfigurationFileIO.NoLinks(SourcePreferencePath);
        var before = File.Exists(SourcePreferencePath) ? ConfigurationFileIO.Hash(ConfigurationFileIO.Read(SourcePreferencePath, 16 * 1024)) : null;
        _ = ConfigurationFileIO.AtomicWrite(SourcePreferencePath, bytes, Path.Combine(_dataRoot, "update-source-backups"), before, before is null);
        return Task.FromResult(preference);
    }, cancellationToken);

    private const int RetiredGitHubReleases = 0;
    private static readonly UpdateSourcePreference NoFolder = new(false, UpdateSourceKind.Folder, null);

    private static UpdateSourcePreference ValidateSource(UpdateSourceKind kind, string? folder)
    {
        if (!Enum.IsDefined(kind) || kind != UpdateSourceKind.Folder && folder is not null) throw InvalidSourcePreference();
        if (kind == UpdateSourceKind.Folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder.Length > 2048) throw InvalidSourcePreference();
            // Validation lexicale seulement : mémoriser un partage indisponible ne l'ouvre jamais.
            folder = StoragePathPolicy.NormalizeNative(folder);
        }
        return new(false, kind, folder);
    }
    private static FilproOperationException InvalidSourcePreference() => new("update.source", "Préférence de mise à jour invalide. Choisir un dossier local/UNC ou un ZIP manuel ; aucun identifiant réseau n'est conservé.");
}
