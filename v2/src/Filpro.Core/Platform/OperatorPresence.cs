namespace Filpro.Core;

/// <summary>Motif du verrouillage de FILPRO dans une session d'un autre compte.</summary>
public enum PresenceReason
{
    /// <summary>Session Windows verrouillée (Win+L, écran de veille).</summary>
    SessionLocked,
    /// <summary>Session déconnectée de la console ou du bureau à distance.</summary>
    SessionDisconnected,
    /// <summary>Fermeture de la session Windows.</summary>
    SessionLogoff,
    /// <summary>
    /// Accord donné avec la case « Verrouiller FILPRO après le lancement » cochée : verrou posé dès l'accord, avant le
    /// lancement, et maintenu même si le lancement échoue.
    /// </summary>
    LockAfterStart,
    /// <summary>Aucune saisie au clavier ni à la souris pendant <see cref="OperatorPresence.IdleLimit"/>.</summary>
    Idle,
    /// <summary>
    /// Plus de <see cref="OperatorPresence.PollGapLimit"/> sans relevé (veille, fil de l'interface bloqué) : l'activité
    /// de l'opérateur n'est plus établie.
    /// </summary>
    PollGap,
    /// <summary><see cref="OperatorPresence.AgeLimit"/> écoulées depuis le lancement élevé de FILPRO.</summary>
    ProcessAge,
    /// <summary><see cref="OperatorPresence.RefusalLimit"/> refus ou absences de réponse en <see cref="OperatorPresence.RefusalWindow"/>.</summary>
    Refusals,
}

/// <summary>
/// Présence de l'opérateur qui a élevé FILPRO quand l'affichage tourne sous un autre compte, ou sous un compte
/// indéterminé. Armée une fois cette situation établie, elle ne se verrouille que dans un sens : seuls la lecture locale et
/// le suivi des opérations restent admis. Une relance de FILPRO, avec une nouvelle UAC, repart d'un objet neuf.
/// </summary>
public interface IOperatorPresence
{
    bool Armed { get; }
    bool Latched { get; }
    PresenceReason? Reason { get; }

    /// <summary>Levé une seule fois, au verrouillage, hors du verrou interne.</summary>
    event EventHandler? Changed;

    /// <summary>Active la surveillance ; sans retour. Un changement de session reçu avant s'applique alors.</summary>
    void Arm();

    /// <summary>Verrouille si la présence est armée et pas encore verrouillée ; vrai pour l'appel qui a verrouillé.</summary>
    bool Latch(PresenceReason reason);

    /// <summary>
    /// Session Windows verrouillée, déconnectée ou fermée : verrouille une présence armée ; avant l'armement, le premier
    /// motif est conservé et appliqué par <see cref="Arm"/>.
    /// </summary>
    void SessionChanged(PresenceReason reason);

    /// <summary>Compte un refus ou une absence de réponse dans la fenêtre de confirmation Windows.</summary>
    void RecordRefusal();

    /// <summary>
    /// Relevé périodique : <paramref name="lastInputTick"/> est la dernière saisie de la session (GetLastInputInfo),
    /// <paramref name="nowTick"/> le compteur courant sur 32 bits, lu après elle.
    /// </summary>
    void Poll(uint lastInputTick, uint nowTick);
}

/// <summary>
/// Règles du verrou de présence, sans appel natif : inactivité, âge du processus élevé, relevés interrompus et refus
/// répétés. Les durées se mesurent sur un compteur monotone qui avance aussi pendant la veille (millisecondes depuis le
/// démarrage de Windows) : un changement de l'heure du poste ne verrouille pas FILPRO et n'allonge pas ses délais.
/// </summary>
public sealed class OperatorPresence : IOperatorPresence
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AgeLimit = TimeSpan.FromMinutes(30);
    public const int RefusalLimit = 3;
    public static readonly TimeSpan RefusalWindow = TimeSpan.FromMinutes(10);

    /// <summary>Écart maximal entre deux relevés : au-delà (veille, fil bloqué), l'activité n'est plus établie.</summary>
    public static readonly TimeSpan PollGapLimit = TimeSpan.FromMinutes(2);

    private readonly object gate = new();
    private readonly Func<long> uptime;
    private readonly long started;
    private readonly Queue<long> refusals = new();
    private long? lastPoll;
    private bool armed;
    private PresenceReason? pending;
    private PresenceReason? reason;

    /// <param name="uptimeMilliseconds">Compteur monotone en millisecondes, veille comprise (Environment.TickCount64).</param>
    /// <param name="ageAtStart">Âge du processus élevé à la création de cet objet ; une valeur négative compte pour zéro.</param>
    public OperatorPresence(Func<long> uptimeMilliseconds, TimeSpan ageAtStart)
    {
        ArgumentNullException.ThrowIfNull(uptimeMilliseconds);
        uptime = uptimeMilliseconds;
        started = uptime() - (long)Math.Max(0, ageAtStart.TotalMilliseconds);
    }

    public event EventHandler? Changed;

    public bool Armed { get { lock (gate) return armed; } }

    public bool Latched { get { lock (gate) return reason is not null; } }

    public PresenceReason? Reason { get { lock (gate) return reason; } }

    public void Arm()
    {
        PresenceReason? due;
        lock (gate)
        {
            if (armed) return;
            armed = true;
            lastPoll = uptime();
            due = pending;
        }
        if (due is { } value) Latch(value);
    }

    public bool Latch(PresenceReason value)
    {
        lock (gate)
        {
            if (!armed || reason is not null) return false;
            reason = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SessionChanged(PresenceReason value)
    {
        lock (gate)
        {
            if (!armed)
            {
                pending ??= value;
                return;
            }
        }
        Latch(value);
    }

    public void RecordRefusal()
    {
        bool due;
        lock (gate)
        {
            if (!armed || reason is not null) return;
            var now = uptime();
            refusals.Enqueue(now);
            while (now - refusals.Peek() >= (long)RefusalWindow.TotalMilliseconds) refusals.Dequeue();
            due = refusals.Count >= RefusalLimit;
        }
        if (due) Latch(PresenceReason.Refusals);
    }

    public void Poll(uint lastInputTick, uint nowTick)
    {
        PresenceReason? due = null;
        lock (gate)
        {
            if (!armed || reason is not null) return;
            var now = uptime();
            // Compteur sur 32 bits, qui repart de zéro tous les 49,7 jours : seule la différence a un sens. Une saisie
            // postérieure au compteur lu (écart au-delà de la moitié de la plage) est une saisie récente.
            var idle = unchecked(nowTick - lastInputTick);
            if (idle > int.MaxValue) idle = 0;
            if (now - started >= (long)AgeLimit.TotalMilliseconds) due = PresenceReason.ProcessAge;
            else if (lastPoll is { } previous && now - previous > (long)PollGapLimit.TotalMilliseconds) due = PresenceReason.PollGap;
            else if (idle >= (long)IdleLimit.TotalMilliseconds) due = PresenceReason.Idle;
            lastPoll = now;
        }
        if (due is { } value) Latch(value);
    }
}
