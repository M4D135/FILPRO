namespace Filpro.Core;

/// <summary>État d'un profil utilisateur suivi par FILPRO.</summary>
public enum FilproProfileState { Actif, Inactif, Verrouille, Inconnu, Deconnecte }

/// <summary>État d'une session rattachée à un profil.</summary>
public enum FilproSessionState { Active, Fermee, Abandonnee, Deconnectee, Inconnue }

/// <summary>État d'une sauvegarde enregistrée dans le magasin.</summary>
public enum FilproBackupState { Reussie, EnCours, Echouee, Annulee, Partielle }

/// <summary>État d'une tâche de suivi. « ArretDemande » n'est jamais un arrêt achevé.</summary>
public enum FilproTaskState { EnAttente, EnCours, Terminee, Annulee, ArretDemande, Echouee, Partielle }

/// <summary>Niveau d'une ligne du journal.</summary>
public enum FilproJournalLevel { Info, Avertissement, Erreur }

/// <summary>Profil suivi (nom d'affichage, type, état, nombre de sessions, dernière activité connue).</summary>
public sealed record FilproProfile(string Name, string Kind, FilproProfileState State, int? SessionCount, DateTimeOffset? LastActivity);

/// <summary>Session rattachée à un profil.</summary>
public sealed record FilproSession(string Profile, FilproSessionState State, DateTimeOffset? LastSeen);

/// <summary>Sauvegarde du magasin FILPRO.</summary>
public sealed record FilproBackup(string Id, DateTimeOffset CreatedAt, long SizeBytes, string Scope, FilproBackupState State);

/// <summary>Tâche de suivi (analyse, sauvegarde, restauration, récupération…).</summary>
public sealed record FilproTask(string Id, string Title, FilproTaskState State, int? ProgressPercent);

/// <summary>Ligne du journal de la session.</summary>
public sealed record FilproJournalEntry(DateTimeOffset At, FilproJournalLevel Level, string Message);

/// <summary>
/// Disponibilité de chaque section de l'Accueil. En démonstration, <see cref="RealSystemActions"/> est faux et aucune
/// section ne prétend refléter l'état réel du poste : les chiffres viennent du fournisseur fictif.
/// </summary>
public sealed record AccueilAvailability(
    bool Profiles,
    bool Backups,
    bool Tasks,
    bool Journal,
    bool RealSystemActions);

/// <summary>Informations de session affichées dans la barre supérieure (mode, démonstration, bandeau éventuel).</summary>
public sealed record SessionInfo(string Mode, bool Demonstration, string? Banner, DateTimeOffset GeneratedAt,
    string? Generation = null, IReadOnlyDictionary<string, bool>? Capabilities = null,
    IReadOnlyList<string>? CapabilityWarnings = null);

/// <summary>Vue agrégée servie à l'Accueil par le fournisseur actif (simulation ou, plus tard, poste réel).</summary>
public sealed record AccueilSnapshot(
    DateTimeOffset GeneratedAt,
    string Mode,
    bool Demonstration,
    IReadOnlyList<FilproProfile> Profiles,
    IReadOnlyList<FilproSession> Sessions,
    IReadOnlyList<FilproBackup> Backups,
    IReadOnlyList<FilproTask> Tasks,
    IReadOnlyList<FilproJournalEntry> Journal,
    AccueilAvailability Availability);
