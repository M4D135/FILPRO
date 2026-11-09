namespace Filpro.Core;

/// <summary>Valeurs enregistrées dans update-source.json. 0, l'ancienne source GitHub Releases, n'est plus attribuée.</summary>
public enum UpdateSourceKind { Folder = 1, ManualZip = 2 }
public enum UpdateStage { Idle, Inspecting, Available, Preparing, Prepared, WaitingForExit, OldMoved,
    NewInstalled, AwaitingHealth, Committed, RollingBack, RolledBack, Failed }
public sealed record UpdateSourceRequest(UpdateSourceKind Kind, string? Path = null, string? Sha256 = null);
public sealed record UpdateSourcePreference(bool Demonstration, UpdateSourceKind Kind, string? Folder);
public sealed record UpdateOffer(string Id, bool Demonstration, UpdateSourceKind Source, string Version,
    string FileName, long Bytes, string Sha256, bool Newer, string IntegrityNotice, IReadOnlyList<string> Warnings);
/// <summary><see cref="PreviousVersion"/> est la version conservée par la transaction, montrée avant un retour arrière.</summary>
public sealed record UpdateStatus(bool Demonstration, UpdateStage Stage, string? TransactionId, string CurrentVersion,
    string? TargetVersion, string Action, double? ProgressPercent, bool CanInstall, bool CanRollback,
    string? Error, IReadOnlyList<string> Warnings, string? PreviousVersion = null);
public sealed record UpdateHandoff(string TransactionId, bool Demonstration, bool CloseApplicationRequired, string Notice);

/// <summary>Aucune recherche ni installation au démarrage. Chaque lecture de source et chaque préparation sont explicites.</summary>
public interface IFilproUpdateService
{
    bool Demonstration { get; }
    event Action<UpdateStatus>? Changed;
    Task<UpdateSourcePreference> ReadSourceAsync(CancellationToken cancellationToken);
    Task<UpdateSourcePreference> SaveSourceAsync(UpdateSourceKind kind, string? folder, CancellationToken cancellationToken);
    Task<UpdateStatus> ReadAsync(CancellationToken cancellationToken);
    Task<UpdateOffer> InspectAsync(UpdateSourceRequest request, CancellationToken cancellationToken);
    Task<UpdateStatus> PrepareAsync(string offerId, bool confirmed, CancellationToken cancellationToken);
    Task<UpdateHandoff> InstallAsync(string transactionId, bool confirmed, CancellationToken cancellationToken);
    Task<UpdateHandoff> RollbackAsync(string transactionId, bool confirmed, CancellationToken cancellationToken);
}

public sealed record PackFile(string Path, long Size, string Sha256);
public sealed record PackManifest(int SchemaVersion, string Application, string Version, string Architecture,
    string Flavor, string SourceCommit, IReadOnlyList<PackFile> Files);
public sealed record PackValidationProgress(string Path, int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes);
public sealed record PackValidationResult(PackManifest Manifest, string ManifestSha256, string? DefaultsSha256,
    int VerifiedFiles, long VerifiedBytes, string IntegrityNotice, string? DefaultsWarning = null)
{
    /// <summary>Version du moteur WebView2 du Socle extrait dans wad, vérifié fichier par fichier ; nul s'il est absent.</summary>
    public string? WebViewSocleVersion { get; init; }
}
