namespace Filpro.Core;

/// <summary>Liste fermée de réglages partageables. Aucun chemin, compte, SID, secret ou historique.</summary>
public sealed record SharedConfiguration(
    string Theme = "system",
    string Motion = "system",
    bool NavigationCollapsed = false,
    bool AutoSelectCurrentTask = true,
    bool NotifyOnCompletion = true,
    bool RecommendedSelection = true,
    bool MigApp = false,
    bool Vss = false,
    bool ContinueOnError = false,
    bool Encrypted = false,
    string ConflictPolicy = "keepBoth");

public sealed record ConfigurationChange(string Key, string Before, string After, string Disposition);
public sealed record ConfigurationPreview(string Id, string Kind, bool Demonstration, string SourceLabel,
    string Sha256, string ExpectedRevision, IReadOnlyList<ConfigurationChange> Changes, string? PackIdentity,
    string Notice);
public sealed record ConfigurationState(bool Demonstration, string Revision, SharedConfiguration Settings,
    ConfigurationPreview? Bootstrap, IReadOnlyList<string> Warnings);
public sealed record ConfigurationResult(bool Demonstration, ConfigurationState State, string? WrittenPath,
    string? BackupPath, string Notice);

/// <summary>Le fournisseur de démonstration implémente ce contrat sans ouvrir de fichier réel.</summary>
public interface IFilproConfigurationService
{
    bool Demonstration { get; }
    Task<ConfigurationState> ReadAsync(CancellationToken cancellationToken);
    /// <summary>
    /// Thème enregistré, sans aucun effet (ni création du document, ni aperçu) ; nul si rien n'est encore enregistré. Sert
    /// seulement à l'aspect de la fenêtre de confirmation Windows.
    /// </summary>
    Task<string?> ReadThemeAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    Task<ConfigurationResult> SaveAsync(SharedConfiguration settings, string expectedRevision, CancellationToken cancellationToken);
    Task<ConfigurationResult> ExportAsync(string destinationPath, bool confirmed, CancellationToken cancellationToken);
    Task<ConfigurationPreview> PreviewImportAsync(string sourcePath, CancellationToken cancellationToken);
    Task<ConfigurationResult> ApplyImportAsync(string previewId, bool confirmed, CancellationToken cancellationToken);
    Task<ConfigurationPreview> PreviewDefaultsAsync(CancellationToken cancellationToken);
    Task<ConfigurationResult> ApplyDefaultsAsync(string previewId, bool confirmed, CancellationToken cancellationToken);
    Task<ConfigurationResult> DecideBootstrapAsync(string previewId, bool apply, bool confirmed, CancellationToken cancellationToken);
}
