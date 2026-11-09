using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Opérations de l'Accueil. Le gestionnaire ne fait que valider les arguments et déléguer au fournisseur actif. En
/// démonstration, les chiffres viennent du fournisseur fictif ; en poste réel non câblé, l'opération répond honnêtement
/// l'indisponibilité (source.unavailable) plutôt que des données inventées.
/// </summary>
public sealed class AccueilModule : IOperationModule
{
    public string Screen => "accueil";

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            // LocalRead : vue agrégée depuis le fournisseur actif ; aucune action système réelle.
            new OperationDefinition("accueil.summary", OperationClass.NetworkRead, (args, _, cancellationToken) =>
            {
                BridgeArguments.RequireNone(args);
                cancellationToken.ThrowIfCancellationRequested();
                if (context.Mode == AppMode.Simulation)
                {
                    var source = context.Simulation ?? throw new FilproOperationException(FilproErrors.SourceUnavailable, FilproErrors.SourceUnavailableMessage);
                    return Task.FromResult<object?>(source.BuildAccueil());
                }
                var poste = context.Poste;
                if (poste is null || poste.Demonstration) throw new FilproOperationException(FilproErrors.SourceUnavailable, "Fournisseur natif indisponible.");
                var inventory = poste.ReadProfiles(cancellationToken);
                var profiles = inventory.Profiles.Select(profile => new FilproProfile(profile.Account,
                    profile.IdentityKind == ProfileIdentityKind.Entra ? "Entra" : "Local ou domaine",
                    profile.Session.State switch
                    {
                        SessionState.Active => FilproProfileState.Actif,
                        SessionState.None => FilproProfileState.Inactif,
                        SessionState.Disconnected => FilproProfileState.Deconnecte,
                        _ => FilproProfileState.Inconnu,
                    }, profile.Session.Count, profile.LastUse)).ToArray();
                var sessions = inventory.Profiles.Where(profile => profile.Session.State != SessionState.None)
                    .SelectMany(profile => Enumerable.Range(0, Math.Max(1, profile.Session.Count ?? 1)).Select(_ => new FilproSession(
                        profile.Account, profile.Session.State switch
                        {
                            SessionState.Active => FilproSessionState.Active,
                            SessionState.Disconnected => FilproSessionState.Deconnectee,
                            _ => FilproSessionState.Inconnue,
                        }, null))).ToArray();
                var execution = context.Service<IFilproExecutionService>();
                var jobs = execution is { Demonstration: false } ? execution.List() : [];
                var tasks = jobs.Select(job => new FilproTask(job.Id, job.Title,
                    job.CancellationRequested && !ExecutionJobCoordinator.IsTerminal(job.State) ? FilproTaskState.ArretDemande : job.State switch
                    {
                        ExecutionState.Pending => FilproTaskState.EnAttente,
                        ExecutionState.Preparing or ExecutionState.Running => FilproTaskState.EnCours,
                        ExecutionState.Succeeded => FilproTaskState.Terminee,
                        ExecutionState.Partial => FilproTaskState.Partielle,
                        ExecutionState.Cancelled => FilproTaskState.Annulee,
                        _ => FilproTaskState.Echouee,
                    }, job.ProgressPercent is { } progress ? (int)Math.Clamp(progress, 0, 100) : null)).ToArray();
                var journal = jobs.Select(job => new FilproJournalEntry(job.FinishedAt ?? job.StartedAt,
                    job.State == ExecutionState.Failed ? FilproJournalLevel.Erreur : job.State == ExecutionState.Partial ? FilproJournalLevel.Avertissement : FilproJournalLevel.Info,
                    job.Title + " — " + job.CurrentAction)).ToList();
                if (inventory.SessionReadStatus != SessionReadStatus.Complete)
                    journal.Add(new(context.Clock.GetUtcNow(), FilproJournalLevel.Avertissement, "Les sessions n’ont pas pu être toutes lues ; aucune absence de session n’est supposée."));
                return Task.FromResult<object?>(new AccueilSnapshot(context.Clock.GetUtcNow(), ModeTexts.PosteLabel, false,
                    profiles, sessions, [], tasks, journal.OrderByDescending(entry => entry.At).ToArray(),
                    new AccueilAvailability(inventory.SessionReadStatus != SessionReadStatus.Unavailable, false, execution is not null, true,
                        execution is { Demonstration: false } && execution.Capabilities.Backup)));
            }, "Profils et sessions relus, tâches courantes et journal local persistant ; les magasins non choisis restent non inventoriés.", ServedInSimulation: true),
        ];
    }
}
