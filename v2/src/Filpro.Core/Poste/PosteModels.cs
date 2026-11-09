namespace Filpro.Core;

// ---------- Profils, SID et sessions WTS ----------

/// <summary>État de lecture de l'inventaire des sessions WTS. « Partial » et « Unavailable » interdisent toute absence affirmée.</summary>
public enum SessionReadStatus { Complete, Partial, Unavailable }

/// <summary>État de session d'un profil, dérivé de WTS. « Unknown » n'est jamais converti en « aucune session ».</summary>
public enum SessionState { Active, Disconnected, None, Unknown }

/// <summary>Origine de l'identité d'un profil. Les profils Entra (S-1-12-1-*) sont hors du périmètre USMT.</summary>
public enum ProfileIdentityKind { CompteLocalOuDomaine, Entra }

/// <summary>
/// Une session WTS lue en lecture seule. <see cref="ConnectState"/> garde la valeur brute WTS (0 = active, 4 = déconnectée,
/// 6 = écoute) ; l'état exploitable est dérivé par <see cref="SessionEvidenceReader"/>. Aucun jeton n'est exposé.
/// </summary>
public sealed record WindowsSession(int SessionId, int ConnectState, string? Sid, string? Account, string IdentitySource);

/// <summary>Inventaire WTS d'un instantané frais. « Unavailable » rend toutes les sessions inconnues, jamais fermées.</summary>
public sealed record WindowsSessionInventory(SessionReadStatus ReadStatus, IReadOnlyList<WindowsSession> Sessions, string? Error);

/// <summary>Preuve de session rattachée à un SID précis. Un compte homonyme ne se substitue jamais au SID exact.</summary>
public sealed record SessionEvidence(
    SessionState State,
    int? Count,
    IReadOnlyList<int> Ids,
    string Source,
    SessionReadStatus ReadStatus,
    string? Error);

/// <summary>
/// Profil suivi, lu en lecture seule. <see cref="Loaded"/> est <c>null</c> quand le chargement n'est pas prouvé (jamais
/// supposé). Les avertissements signalent itinérance, FSLogix ou redirections ; ils n'empêchent pas la lecture.
/// </summary>
public sealed record ProfileCandidate(
    string Sid,
    ProfileIdentityKind IdentityKind,
    bool MigrationSupported,
    string Account,
    string? LocalPath,
    bool? Loaded,
    SessionEvidence Session,
    bool Exists,
    IReadOnlyList<string> Warnings,
    bool IsFslogix,
    bool IsCentralProfile,
    bool HasRedirected,
    bool Excluded,
    DateTimeOffset? LastUse);

/// <summary>Liste des profils avec l'état de lecture des sessions qui l'a accompagnée.</summary>
public sealed record ProfileInventory(SessionReadStatus SessionReadStatus, string? SessionError, IReadOnlyList<ProfileCandidate> Profiles);

// ---------- Sélection groupée des logiciels (fichiers + registre) ----------

/// <summary>Portée d'un élément de logiciel. Fichiers : Local ou Roaming (AppData). Registre : HKCU.</summary>
public enum SoftwareScope { Local, Roaming, Hkcu }

/// <summary>Catégorie d'un élément, reprise de la capture V1 (données, préférences, caches, modèles communs).</summary>
public enum SoftwareCategory { Donnees, Preferences, Caches, Modele, Autre }

/// <summary>État de la mesure d'une taille de fichiers. « Inconnue » et « Partielle » ne sont jamais présentées comme mesurées.</summary>
public enum SoftwareSizeState { Aucun, Inconnue, Mesuree, Partielle }

/// <summary>
/// Un élément sélectionnable : un dossier AppData (scope Local/Roaming) ou une clé du registre (scope Hkcu). Les tailles
/// viennent uniquement du worker ; ici elles sont absentes tant que la mesure n'a pas été faite.
/// </summary>
public sealed record SoftwareItem(
    SoftwareScope Scope,
    string Rel,
    string Name,
    SoftwareCategory Category,
    bool Selected,
    bool Recommend,
    bool Known,
    long? Size,
    bool SizeMeasured,
    bool SizeComplete);

/// <summary>Résultat de la mesure groupée des fichiers d'un logiciel (jamais la taille des clés HKCU).</summary>
public sealed record SoftwareSizeSummary(
    SoftwareSizeState State,
    long? Bytes,
    bool Measured,
    bool Complete,
    string Text,
    int UnknownPaths,
    int OverlappingPaths,
    int DuplicatePaths,
    string Notice);

/// <summary>
/// Groupe de logiciels : fichiers et clés d'un même logiciel, sélectionnés ensemble. <see cref="Selected"/> est
/// <c>null</c> quand la sélection est partielle (tri-état), comme dans la V1.
/// </summary>
public sealed record SoftwareGroup(
    string Id,
    string Name,
    bool Linked,
    IReadOnlyList<SoftwareItem> Files,
    IReadOnlyList<SoftwareItem> Registry,
    bool? Selected,
    int SelectedCount,
    string Summary,
    SoftwareSizeSummary Size);

/// <summary>Constat de découverte : une liste incomplète ne prouve pas l'absence d'un logiciel.</summary>
public sealed record DiscoveryNotice(bool FilesComplete, bool RegistryComplete, string? FilesReason, string? RegistryReason);

/// <summary>Inventaire logiciel d'un profil : groupes fichiers+registre, erreur de registre éventuelle et constat de découverte.</summary>
public sealed record SoftwareInventory(
    string Sid,
    IReadOnlyList<SoftwareGroup> Groups,
    string? RegistryError,
    DiscoveryNotice Discovery,
    DateTimeOffset CapturedAt);

/// <summary>Dernière sélection enregistrée pour un compte (fichiers et clés précis, valables pour ce SID).</summary>
public sealed record LastSelection(
    string Sid,
    IReadOnlyList<SoftwareItem> Files,
    IReadOnlyList<SoftwareItem> Registry,
    DateTimeOffset CapturedAt);

// ---------- Politiques de conflit (correctif restauration des réglages) ----------

/// <summary>
/// Décision de restauration d'une portée de réglages. <see cref="KeepBoth"/> est la coexistence par défaut : un nouveau
/// fichier contenant les octets sauvegardés est ajouté et l'actuel est conservé ; le registre garde la valeur actuelle.
/// </summary>
public enum RestorePolicyMode { KeepBoth, KeepCurrent, UseSaved }

/// <summary>Décision de restauration pour un groupe de réglages identifié.</summary>
public sealed record RestorePolicy(string Id, RestorePolicyMode Mode);

/// <summary>Portée d'un réglage (fichier ou clé) utilisée pour détecter les chevauchements entre logiciels.</summary>
public sealed record RestoreScope(string Type, string Path, RestorePolicyMode Mode);
