namespace Filpro.Core;

public sealed record ExecutionOutcome(ExecutionState State, IReadOnlyList<string> Warnings, string? ResultPath = null);

/// <summary>Un moteur a pu écrire : son reçu et les données de reprise restent accessibles, sans succès global fictif.</summary>
public sealed class ExecutionPartialException(string code, string message, string? resultPath, IReadOnlyList<string> warnings) : Exception(message)
{
    public string Code { get; } = code;
    public string? ResultPath { get; } = resultPath;
    public IReadOnlyList<string> Warnings { get; } = warnings;
}

/// <summary>Canal d'une seule opération. Les états terminaux sont publiés par le coordinateur après le travail.</summary>
public sealed class ExecutionJobContext
{
    private readonly Action<ExecutionState, string, string?, long?, long?, double?, double?> report;
    internal ExecutionJobContext(string id, CancellationToken token,
        Action<ExecutionState, string, string?, long?, long?, double?, double?> report)
    { Id = id; CancellationToken = token; this.report = report; }
    public string Id { get; }
    public CancellationToken CancellationToken { get; }
    public void Report(ExecutionState state, string action, string? profileSid = null,
        long? completedUnits = null, long? totalUnits = null, double? estimatedRemainingSeconds = null, double? progressPercent = null)
    {
        if (state is not (ExecutionState.Preparing or ExecutionState.Running))
            throw new ArgumentOutOfRangeException(nameof(state));
        report(state, action, profileSid, completedUnits, totalUnits, estimatedRemainingSeconds, progressPercent);
    }
}

/// <summary>
/// Coordinateur en mémoire, sans dépendance Windows et sans E/S. Sérialise les opérations dans une instance ; la
/// plateforme ajoute un verrou interprocessus. Une annulation attend le délégué, donc ses finally et ses déchargements.
/// </summary>
public sealed class ExecutionJobCoordinator(bool demonstration, TimeProvider clock, Action<ExecutionJobSnapshot>? checkpoint = null, ITechnicalJournal? journal = null)
{
    private sealed class Job(ExecutionJobSnapshot snapshot)
    {
        public ExecutionJobSnapshot Snapshot = snapshot;
        public readonly CancellationTokenSource Cancellation = new();
        public Task Completion = Task.CompletedTask;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private bool closing;
    public event Action<ExecutionJobSnapshot>? Changed;

    public IReadOnlyList<ExecutionJobSnapshot> List()
    { lock (gate) return jobs.Values.Select(j => j.Snapshot).OrderByDescending(j => j.StartedAt).ToArray(); }
    public ExecutionJobSnapshot? Get(string id)
    { lock (gate) return jobs.TryGetValue(id, out var job) ? job.Snapshot : null; }

    public ExecutionJobSnapshot Start(ExecutionKind kind, string title, ExecutionStartOptions options,
        Func<ExecutionJobContext, Task<ExecutionOutcome>> execute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(execute);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.PreparationConfirmed)
            throw new FilproOperationException("execution.confirmation", "Confirmer avant la préparation longue.");
        Job job;
        var launch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (closing) throw new FilproOperationException("execution.closing", "FILPRO termine ses opérations.");
            if (jobs.Values.Any(j => !IsTerminal(j.Snapshot.State)))
                throw new FilproOperationException("execution.busy", "Une opération est déjà en cours ; attendre son nettoyage.");
            foreach (var old in jobs.Where(p => IsTerminal(p.Value.Snapshot.State)).OrderBy(p => p.Value.Snapshot.StartedAt)
                         .Take(Math.Max(0, jobs.Count - 99)).ToArray())
            { old.Value.Cancellation.Dispose(); jobs.Remove(old.Key); }
            var id = Guid.NewGuid().ToString("N");
            job = new Job(new(id, kind, title, ExecutionState.Pending, "En attente de préparation", null, null,
                clock.GetUtcNow(), null, demonstration, null, null, null, null, [], null));
            // Échec de journal initial : aucun délégué métier n'est lancé.
            try { checkpoint?.Invoke(job.Snapshot); }
            catch { job.Cancellation.Dispose(); throw; }
            jobs.Add(id, job);
            job.Completion = Task.Run(async () =>
            {
                await launch.Task.ConfigureAwait(false);
                await RunAsync(job, execute).ConfigureAwait(false);
            }, CancellationToken.None);
        }
        Publish(job.Snapshot);
        launch.SetResult();
        return job.Snapshot;
    }

    public async Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken)
    {
        Job job;
        ExecutionJobSnapshot pending;
        lock (gate)
        {
            if (!jobs.TryGetValue(id, out job!))
                throw new FilproOperationException("execution.unknown", "Opération inconnue.");
            if (IsTerminal(job.Snapshot.State)) return job.Snapshot;
            pending = job.Snapshot = job.Snapshot with { CancellationRequested = true, CurrentAction = "Arrêt demandé ; nettoyage en cours" };
        }
        Publish(pending);
        job.Cancellation.Cancel();
        await job.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate) return job.Snapshot;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Job[] active;
        lock (gate) { closing = true; active = jobs.Values.Where(j => !IsTerminal(j.Snapshot.State)).ToArray(); }
        foreach (var job in active) job.Cancellation.Cancel();
        await Task.WhenAll(active.Select(j => j.Completion)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static bool IsTerminal(ExecutionState state) => state is ExecutionState.Succeeded or ExecutionState.Partial
        or ExecutionState.Cancelled or ExecutionState.Failed;

    private async Task RunAsync(Job job, Func<ExecutionJobContext, Task<ExecutionOutcome>> execute)
    {
        ExecutionOutcome outcome;
        ExecutionError? error = null;
        try
        {
            var context = new ExecutionJobContext(job.Snapshot.Id, job.Cancellation.Token,
                (state, action, sid, done, total, eta, percent) => Update(job, state, action, sid, done, total, eta, percent));
            outcome = await execute(context).ConfigureAwait(false);
            if (outcome.State is not (ExecutionState.Succeeded or ExecutionState.Partial))
                throw new InvalidOperationException("État de fin métier non valide.");
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        { outcome = new(ExecutionState.Cancelled, ["Opération interrompue ; consulter son reçu avant toute reprise."]); }
        catch (ExecutionPartialException ex)
        {
            error = new(ex.Code, ex.Message);
            outcome = new(ExecutionState.Partial, ex.Warnings, ex.ResultPath);
        }
        catch (Exception ex)
        {
            if (ex is FilproOperationException operation)
                error = new(operation.Code, operation.Message);
            else
            {
                // Le journal ne doit pas remplacer le résultat de l'opération.
                try { journal?.Error("execution.failed", ex); } catch (Exception) { }
                error = new("execution.failed", "L’opération a échoué ; les journaux locaux conservent le diagnostic.");
            }
            outcome = new(ExecutionState.Failed, []);
        }
        ExecutionJobSnapshot final;
        lock (gate)
        {
            final = job.Snapshot with
            {
                State = outcome.State,
                CurrentAction = outcome.State switch
                {
                    ExecutionState.Succeeded => "Opération terminée", ExecutionState.Partial => "Opération partielle ; revue nécessaire",
                    ExecutionState.Cancelled => "Opération arrêtée après nettoyage", _ => "Opération en échec",
                },
                ProgressPercent = outcome.State == ExecutionState.Succeeded ? 100 : job.Snapshot.ProgressPercent,
                EstimatedRemainingSeconds = null, FinishedAt = clock.GetUtcNow(), Error = error,
                Warnings = outcome.Warnings.ToArray(), ResultPath = outcome.ResultPath,
            };
        }
        try { checkpoint?.Invoke(final); }
        catch
        {
            final = final with
            {
                State = final.State == ExecutionState.Succeeded ? ExecutionState.Partial : final.State,
                CurrentAction = "Bilan final non enregistré ; conserver les journaux locaux",
                Warnings = final.Warnings.Append("Le bilan durable n'a pas pu être écrit. Après fermeture, l'issue de cette opération restera à vérifier.").ToArray(),
            };
        }
        lock (gate) job.Snapshot = final;
        Publish(final);
    }

    private void Update(Job job, ExecutionState state, string action, string? sid, long? done, long? total, double? eta, double? percent)
    {
        if (done < 0 || total <= 0 || done > total || eta < 0 || (eta.HasValue && !double.IsFinite(eta.Value))
            || percent < 0 || percent > 100 || (percent.HasValue && !double.IsFinite(percent.Value)))
            throw new ArgumentOutOfRangeException(nameof(done), "Mesure de progression invalide.");
        ExecutionJobSnapshot snapshot;
        lock (gate)
        {
            snapshot = job.Snapshot = job.Snapshot with
            {
                State = state, CurrentAction = action, ProfileSid = sid, CompletedUnits = done, TotalUnits = total,
                ProgressPercent = percent ?? (done.HasValue && total.HasValue ? 100d * done.Value / total.Value : null),
                EstimatedRemainingSeconds = eta,
            };
        }
        Publish(snapshot);
    }
    private void Publish(ExecutionJobSnapshot snapshot)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        { try { ((Action<ExecutionJobSnapshot>)handler)(snapshot); } catch { /* Un observateur ne dirige pas le moteur. */ } }
    }
}
