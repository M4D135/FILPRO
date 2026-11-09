namespace Filpro.Core;

// ---------- Plan de restauration (contrat typé du micro-lot 003B) ----------

/// <summary>
/// État de la sauvegarde préalable exigée avant de restaurer sur un profil déjà présent. Le plan ne vérifie rien : il
/// refuse une absence, un emplacement dans le magasin à restaurer ou dans un profil cible, et ne promeut jamais un état
/// en « vérifié ». <see cref="Declaree"/> reste non contrôlée par FILPRO ; <see cref="Identifiee"/> signifie magasin
/// FILPRO identifié et règles contrôlées, le dernier contrôle natif n'étant qu'une information enregistrée.
/// </summary>
public enum PriorBackupState { NonApplicable, Absente, Declaree, Identifiee }

/// <summary>
/// Sauvegarde préalable des destinations. <see cref="Root"/> et <see cref="KeyPath"/> sont des chemins, jamais du contenu.
/// <see cref="Verified"/> n'est jamais posé à <c>true</c> par le plan : seule l'exécution du moteur peut l'établir.
/// </summary>
public sealed record PriorBackupRequirement(
    PriorBackupState State,
    string? Root,
    string? KeyPath,
    bool Verified,
    string Statement);

/// <summary>
/// Magasin source tel que lu par le moteur, transmis à la validation pure. <see cref="Legacy"/> marque un ancien magasin
/// sans manifeste FILPRO (pas de SID ni de chemin source : ne jamais emprunter ceux de la cible). <see cref="Ready"/> est
/// l'état d'exploitabilité constaté ; le plan ne le fabrique pas.
/// </summary>
public sealed record RestoreStoreInfo(
    string Bundle,
    bool Ready,
    bool Legacy,
    bool Partial,
    bool Simulated,
    bool Encrypted,
    string Status,
    IReadOnlyList<string>? ManifestUsers,
    string? SourceComputerName,
    string? SourceCreatedUtc,
    string? UsmtVersion);

/// <summary>Correspondance d'un compte source vers un compte cible. Sans <see cref="Target"/>, la cible est le source.</summary>
public sealed record RestoreAccountMapping(string Source, string? Target);

/// <summary>
/// Profil source porté par le manifeste (SID et chemin connus pour un magasin récent). Pour un ancien magasin, ces deux
/// champs doivent rester <c>null</c> : le plan refuse qu'on emprunte le SID ou le chemin de la cible.
/// </summary>
public sealed record RestoreSourceProfile(string Account, string? Sid, string? ProfilePath);

/// <summary>
/// Profil cible résolu par le moteur (compte existant sur ce poste). <see cref="Loaded"/> est tri-état : <c>null</c>
/// quand le chargement n'est pas prouvé, jamais supposé. Un profil cible chargé bloque la restauration.
/// </summary>
public sealed record RestoreTargetProfile(
    string Sid,
    ProfileIdentityKind IdentityKind,
    string Account,
    string ProfilePath,
    bool? Loaded);

/// <summary>
/// Demande de plan de restauration soumise à <see cref="RestorePlan.Validate"/>. Elle ne contient ni horloge ni
/// identifiant aléatoire : le moteur les fournit, ce qui garde la validation pure, déterministe et testable sans poste.
/// </summary>
public sealed record RestorePlanRequest(
    RestoreStoreInfo Store,
    IReadOnlyList<string> Users,
    IReadOnlyList<RestoreAccountMapping>? Mappings,
    IReadOnlyList<RestoreTargetProfile>? Targets,
    IReadOnlyList<RestoreSourceProfile>? SourceProfiles,
    IReadOnlyList<SoftwareGroup>? ConflictChoices,
    IReadOnlyList<RestorePolicy>? ConflictPolicies,
    PriorBackupRequirement? PriorBackup,
    string? CurrentAccountSid,
    string? SourceKeyPath,
    bool LegacyAcknowledged = false,
    bool PartialAcknowledged = false,
    bool AssumeEncrypted = false,
    bool Demonstration = false,
    string? Purpose = null);

/// <summary>Correspondance canonisée retenue par le plan (cible explicite, SID de la cible si le profil existe déjà).</summary>
public sealed record RestoreMappingResolution(string Source, string Target, string? TargetSid);

/// <summary>
/// Paire d'identités source → cible pour la revue avant accord. Pour un ancien magasin, le source n'a ni SID ni chemin :
/// ils restent <c>null</c> plutôt que d'être empruntés à la cible.
/// </summary>
public sealed record RestoreIdentityPair(
    string SourceAccount,
    string? SourceSid,
    string? SourcePath,
    string TargetAccount,
    string? TargetSid);

/// <summary>
/// Plan de restauration validé, destiné à la revue puis à la confirmation par le moteur. Sa fabrication n'a rien lu ni
/// écrit : le besoin d'espace y est explicitement inconnu, la sauvegarde préalable n'y est jamais déclarée vérifiée, la
/// clé de chiffrement n'y figure que par son chemin, et les politiques de conflits viennent de
/// <see cref="RestoreConflictPolicies"/> (mêmes règles de portées et de chevauchements). <see cref="ConflictSummaryLines"/>
/// porte les lignes de <see cref="ConflictSummary"/>, que la confirmation Windows montre une à une ;
/// <see cref="SourcePartial"/> reprend l'état partiel du magasin lu (des éléments n'ont pas été capturés).
/// </summary>
public sealed record RestorePlanReview(
    int SchemaVersion,
    string ProductVersion,
    string Id,
    DateTimeOffset CreatedUtc,
    bool Demonstration,
    string Purpose,
    string Bundle,
    bool Legacy,
    bool Encrypted,
    string? SourceKeyPath,
    string? SourceComputerName,
    string? SourceCreatedUtc,
    IReadOnlyList<string> Users,
    IReadOnlyList<RestoreMappingResolution> Mappings,
    IReadOnlyList<RestoreIdentityPair> IdentityPairs,
    IReadOnlyList<RestoreTargetProfile> Targets,
    PriorBackupRequirement PriorBackup,
    IReadOnlyList<RestorePolicy> ConflictPolicies,
    string ConflictSummary,
    bool HasUnknownSpace,
    bool RequiresManualSpaceAcknowledgement,
    IReadOnlyList<string> Warnings,
    string IdentityReview,
    string Notice,
    string? SourceManifestSha256 = null,
    string? SourceStoreId = null,
    string? SourcePayloadSha256 = null,
    IReadOnlyList<string>? ConflictSummaryLines = null,
    bool SourcePartial = false);
