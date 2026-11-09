namespace Filpro.Core;

/// <summary>États d'une opération possédée par le moteur. Cancelled n'est publié qu'après son nettoyage.</summary>
public enum ExecutionState { Pending, Preparing, Running, Succeeded, Partial, Cancelled, Failed }
public enum ExecutionKind { Backup, Restore, Repair, Recover, Rollback, Catalog }

public sealed record ExecutionError(string Code, string Message);

/// <summary>Les compteurs et l'estimation restent nuls quand le moteur ne possède pas de mesure.</summary>
public sealed record ExecutionJobSnapshot(
    string Id,
    ExecutionKind Kind,
    string Title,
    ExecutionState State,
    string CurrentAction,
    double? ProgressPercent,
    double? EstimatedRemainingSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool Demonstration,
    ExecutionError? Error,
    string? ProfileSid,
    long? CompletedUnits,
    long? TotalUnits,
    IReadOnlyList<string> Warnings,
    string? ResultPath,
    bool CancellationRequested = false);

/// <summary>Accords recueillis avant toute préparation longue. Ils ne remplacent ni le plan serveur ni les gardes.</summary>
public sealed record ExecutionStartOptions(bool PreparationConfirmed, bool ManualSpaceAcknowledged = false);

public sealed record ExecutionCapabilities(bool Backup, bool Restore, bool Repair, bool Recover,
    IReadOnlyList<string> UnavailableReasons);

/// <summary>L'identifiant ne contient pas de chemin fourni par le navigateur. Le catalogue est lié à un manifeste.</summary>
public sealed record ExecutionStoreEntry(string Id, string RelativePath, long? Bytes, bool Corrupted = false);

/// <summary>
/// Lecture bornée du manifeste et de ses règles. PayloadVerified décrit un contrôle natif récent, jamais la seule
/// présence d'un fichier MIG. Inspecter un magasin ne lance pas une restauration. <see cref="StoreWarnings"/> reprend les
/// avertissements déclarés par le manifeste (texte libre du magasin), aussi placés en tête de <see cref="Warnings"/>.
/// </summary>
public sealed record ExecutionStoreInspection(
    string Id,
    string Bundle,
    string ManifestSha256,
    RestoreStoreInfo Store,
    IReadOnlyList<RestoreSourceProfile> SourceProfiles,
    IReadOnlyList<ExecutionStoreEntry> Entries,
    bool CatalogComplete,
    bool PayloadVerified,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SoftwareGroup>? ConflictChoices = null,
    string? SourcePayloadSha256 = null,
    IReadOnlyList<string>? StoreWarnings = null);

/// <summary>
/// La lecture native longue est toujours une opération confirmée, distincte de l'inspection du manifeste.
/// <see cref="ExpectedManifestSha256"/> lie le démarrage au manifeste montré dans la confirmation Windows.
/// </summary>
public sealed record StoreCatalogRequest(string Bundle, string? KeyPath = null, string? ExpectedManifestSha256 = null);

public sealed record RecoveryPlanRequest(string Bundle, string Destination, IReadOnlyList<string> EntryIds,
    string? KeyPath = null);

public sealed record RecoveryPlanReview(string Id, DateTimeOffset CreatedUtc, bool Demonstration,
    string Bundle, string StoreId, string ManifestSha256, string Destination, string? KeyPath,
    IReadOnlyList<ExecutionStoreEntry> Entries, bool Encrypted, IReadOnlyList<string> Warnings, string Notice,
    string? SourcePayloadSha256 = null);

public sealed record RepairPlanRequest(string Sid, string SafetyBackupRoot);

/// <summary>Le dossier original est conservé ; aucune suppression, ouverture de session ou relance de Windows implicite.</summary>
public sealed record RepairPlanReview(string Id, DateTimeOffset CreatedUtc, bool Demonstration,
    string Sid, string Account, string ProfilePath, string PreservedProfilePath, string SafetyBackupRoot,
    IReadOnlyList<string> Actions, IReadOnlyList<string> Warnings, string Notice);

/// <summary>
/// Retour arrière d'une réparation : reçu protégé émis par ce moteur, jamais une commande arbitraire.
/// <see cref="ExpectedReceiptSha256"/> lie le démarrage au reçu montré dans la confirmation Windows.
/// </summary>
public sealed record RepairRollbackRequest(string ReceiptPath, string? ExpectedReceiptSha256 = null);

/// <summary>
/// Reçu de réparation relu et vérifié comme le retour arrière le fera (sceau, structure, emplacement), sans rien lancer.
/// <see cref="ReceiptSha256"/> est l'empreinte des octets vérifiés du reçu.
/// </summary>
public sealed record RepairRollbackReview(string ReceiptPath, string ReceiptSha256, bool Demonstration,
    string Sid, string Account, string Stage, string OriginalPath, string PreservedPath,
    string BackupDirectory, string SafetyBundle, string? NewProfilePreservedPath);
