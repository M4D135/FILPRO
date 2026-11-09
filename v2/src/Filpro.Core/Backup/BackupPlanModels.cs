namespace Filpro.Core;

// ---------- Plan de sauvegarde multi-profils (contrat typé du lot 003A) ----------

/// <summary>
/// État du besoin d'espace d'un plan de sauvegarde. <see cref="Inconnu"/> est l'état honnête d'un plan non mesuré : il
/// bloque la copie, il ne l'autorise pas. Aucun état mesuré ne peut être fabriqué par la création du plan.
/// </summary>
public enum BackupSpaceState { Inconnu, Estime, Partiel }

/// <summary>
/// Besoin d'espace annoncé par le plan (magasin, temporaire, marges). Les octets ne viennent que du moteur après mesure :
/// tant qu'aucune mesure n'a été faite, <see cref="StoreBytes"/>, <see cref="TempBytes"/> et <see cref="MeasuredAt"/>
/// restent <c>null</c> et <see cref="State"/> reste <see cref="BackupSpaceState.Inconnu"/>.
/// </summary>
public sealed record BackupSpaceRequirement(
    BackupSpaceState State,
    long? StoreBytes,
    long? TempBytes,
    string Text,
    string Notice,
    DateTimeOffset? MeasuredAt);

/// <summary>Nature de ce qui est écarté d'un plan de sauvegarde.</summary>
public enum BackupExclusionKind { Profil, Dossier, Cle, FluxNomme, Application, Ruche, DossierVide, Magasin }

/// <summary>
/// Origine d'une exclusion. <see cref="Operateur"/> est déclarée par la demande (compte écarté, dossier retiré) ;
/// <see cref="Regle"/> est annoncée par le plan lui-même (flux nommés, applications, ruches, dossiers vides, magasins
/// voisins). Une demande ne peut pas se déclarer elle-même « règle ».
/// </summary>
public enum BackupExclusionOrigin { Operateur, Regle }

/// <summary>Une exclusion du plan, toujours justifiée. <see cref="Target"/> est obligatoire pour ce qui est nommé.</summary>
public sealed record BackupExclusion(
    BackupExclusionKind Kind,
    string? Target,
    string Reason,
    BackupExclusionOrigin Origin);

/// <summary>
/// Profil retenu pour la sauvegarde, avec les réglages qui l'accompagnent (dossiers AppData et clés HKCU déjà retenus par
/// le fournisseur). <see cref="Loaded"/> est tri-état : <c>null</c> quand le chargement n'est pas prouvé, jamais supposé.
/// Le plan ne lit rien : il reçoit la sélection et la refuse si elle est invraisemblable.
/// </summary>
public sealed record BackupProfileSelection(
    string Sid,
    ProfileIdentityKind IdentityKind,
    string Account,
    string ProfilePath,
    bool? Loaded,
    IReadOnlyList<SoftwareItem> Files,
    IReadOnlyList<SoftwareItem> Registry);

/// <summary>
/// Demande de plan multi-profils soumise à <see cref="BackupPlan.Create"/>. Elle ne contient ni horloge ni identifiant
/// aléatoire : le moteur les fournit, ce qui garde la création pure, déterministe et testable sans poste.
/// </summary>
public sealed record BackupPlanRequest(
    IReadOnlyList<BackupProfileSelection> Profiles,
    string Destination,
    bool Encrypted = false,
    string? KeyPath = null,
    bool UseVss = false,
    bool NoCompress = false,
    bool ContinueOnError = false,
    bool Demonstration = false,
    string? Purpose = null,
    IReadOnlyList<BackupExclusion>? Exclusions = null,
    bool MigApp = false);

/// <summary>
/// Plan de sauvegarde multi-profils validé, destiné à la revue puis à la confirmation par le moteur. Sa fabrication n'a
/// rien lu ni écrit sur le poste : le besoin d'espace y est explicitement inconnu, les exclusions y sont annoncées et la
/// clé de chiffrement n'y figure que par son chemin, jamais par son contenu. Nommé <c>…Review</c> comme
/// <see cref="RestorePlanReview"/> pour éviter le doublon de nom avec la fabrique pure <see cref="BackupPlan"/>.
/// </summary>
public sealed record BackupPlanReview(
    int SchemaVersion,
    string ProductVersion,
    string Id,
    DateTimeOffset CreatedUtc,
    bool Demonstration,
    string Purpose,
    string Destination,
    bool Encrypted,
    string? KeyPath,
    bool UseVss,
    bool NoCompress,
    bool ContinueOnError,
    IReadOnlyList<BackupProfileSelection> Profiles,
    BackupSpaceRequirement Space,
    IReadOnlyList<BackupExclusion> Exclusions,
    bool HasUnknownSpace,
    bool RequiresManualSpaceAcknowledgement,
    string Scope,
    IReadOnlyList<string> Warnings,
    string Notice,
    bool MigApp = false);
