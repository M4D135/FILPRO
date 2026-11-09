using System.Globalization;
using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>
/// Fournisseur de démonstration de l'Accueil : monde fictif en mémoire, déterministe pour une graine donnée. Il ne lit ni
/// n'écrit jamais le système réel (aucun fichier, registre, réseau ni WMI) et ne prétend pas refléter l'état de la machine :
/// <see cref="AccueilAvailability.RealSystemActions"/> est toujours faux et le snapshot est marqué démonstration.
/// </summary>
public sealed class FilproSimulationSource(SimulationOptions options, TimeProvider clock) : IFilproSimulationSource
{
    private static readonly string[] ProfileNames =
    [
        "Camille Martin", "Léa Dubois", "Karim Benali", "Sophie Nguyen", "Hugo Lefèvre", "Nadia Cherif",
        "Thomas Moreau", "Yasmine Haddad", "Julien Petit", "Émilie Roux", "Marc Fontaine", "Inès Girard",
    ];

    private static readonly string[] ProfileKinds = ["Poste", "Poste partagé", "Nomade", "Atelier"];

    private static readonly string[] BackupScopes = ["Profil complet", "Documents et Bureau", "Registre des logiciels", "Modèles communs", "Profil + paramètres"];

    private static readonly (string Title, FilproTaskState State)[] TaskKinds =
    [
        ("Analyser et réparer le profil", FilproTaskState.Terminee),
        ("Sauvegarder le profil courant", FilproTaskState.EnCours),
        ("Restaurer depuis une sauvegarde", FilproTaskState.EnAttente),
        ("Récupérer des fichiers supprimés", FilproTaskState.EnAttente),
        ("Synchroniser les modèles communs", FilproTaskState.Terminee),
        ("Vérifier l’intégrité du magasin", FilproTaskState.ArretDemande),
    ];

    private static readonly string[] JournalMessages =
    [
        "Analyse terminée sans erreur bloquante.",
        "Sauvegarde du profil enregistrée dans le magasin local.",
        "Registre des logiciels comparé au modèle commun.",
        "Un fichier était verrouillé et a été ignoré.",
        "Clé de chiffrement du magasin vérifiée.",
        "Restauration préparée ; confirmation de l’opérateur en attente.",
        "Hives du registre déchargées en fin d’opération.",
        "Arrêt demandé : l’opération n’est pas encore achevée.",
    ];

    public AccueilSnapshot BuildAccueil()
    {
        var validated = options.Validate();
        var rng = SeededRandom.FromSeed(validated.Seed);
        var now = clock.GetUtcNow();

        var profiles = new List<FilproProfile>(validated.ProfileCount);
        var sessions = new List<FilproSession>();
        for (var i = 0; i < validated.ProfileCount; i++)
        {
            var name = ProfileNames[i % ProfileNames.Length];
            var state = i == 0 ? FilproProfileState.Actif : (FilproProfileState)rng.Next((int)FilproProfileState.Actif, (int)FilproProfileState.Verrouille + 1);
            var sessionCount = rng.Next(0, 4);
            var lastActivity = state == FilproProfileState.Actif ? now.AddMinutes(-rng.Next(1, 240)) : now.AddDays(-rng.Next(1, 30));
            profiles.Add(new FilproProfile(name, ProfileKinds[rng.Next(ProfileKinds.Length)], state, sessionCount, lastActivity));
            for (var s = 0; s < sessionCount; s++)
            {
                var sessionState = s == 0 && state == FilproProfileState.Actif ? FilproSessionState.Active : (FilproSessionState)rng.Next(0, 3);
                sessions.Add(new FilproSession(name, sessionState, now.AddMinutes(-rng.Next(1, 5000))));
            }
        }

        var backups = new List<FilproBackup>(validated.BackupCount);
        for (var i = 0; i < validated.BackupCount; i++)
        {
            var createdAt = now.AddHours(-24 * i - rng.Next(0, 12));
            var state = i == 0 ? FilproBackupState.Reussie : (FilproBackupState)rng.Next(0, 4);
            var sizeMb = rng.Next(120, 4800);
            var stamp = createdAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var seq = (i + 1).ToString("00", CultureInfo.InvariantCulture);
            var id = $"SAV-{stamp}-{seq}";
            backups.Add(new FilproBackup(id, createdAt, (long)sizeMb * 1024 * 1024, BackupScopes[rng.Next(BackupScopes.Length)], state));
        }

        var tasks = new List<FilproTask>(validated.TaskCount);
        for (var i = 0; i < validated.TaskCount; i++)
        {
            var (title, state) = TaskKinds[i % TaskKinds.Length];
            var progress = state switch
            {
                FilproTaskState.Terminee => 100,
                FilproTaskState.EnAttente => 0,
                FilproTaskState.Annulee => 0,
                FilproTaskState.ArretDemande => rng.Next(20, 80),
                _ => rng.Next(5, 95),
            };
            tasks.Add(new FilproTask("TCH-" + (i + 1).ToString("00", CultureInfo.InvariantCulture), title, state, progress));
        }

        var journal = new List<FilproJournalEntry>(validated.JournalCount);
        for (var i = 0; i < validated.JournalCount; i++)
        {
            var level = i % 5 == 3 ? FilproJournalLevel.Avertissement : i % 7 == 6 ? FilproJournalLevel.Erreur : FilproJournalLevel.Info;
            journal.Add(new FilproJournalEntry(now.AddMinutes(-13 * i - rng.Next(0, 6)), level, JournalMessages[rng.Next(JournalMessages.Length)]));
        }

        // Démonstration : aucune section ne reflète l'état réel du poste ; rien n'a été lu ni écrit sur le système.
        var availability = new AccueilAvailability(Profiles: true, Backups: true, Tasks: true, Journal: true, RealSystemActions: false);
        return new AccueilSnapshot(now, ModeTexts.SimulationLabel, Demonstration: true, profiles, sessions, backups, tasks, journal, availability);
    }
}
