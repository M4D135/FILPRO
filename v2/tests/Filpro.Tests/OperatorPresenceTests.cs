using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Verrou de présence d'une session d'un autre compte : armé une fois la situation établie, verrouillé dans un seul sens par
/// la session Windows (même avant l'armement), l'inactivité, l'âge du processus élevé, un relevé interrompu ou des refus
/// répétés ; une seule notification. Les durées suivent un compteur monotone, jamais l'heure du poste.
/// </summary>
internal sealed class OperatorPresenceTests
{
    private const uint Tick = 1_000_000;

    private static (OperatorPresence Presence, Uptime Clock, List<PresenceReason?> Changes) Armed(TimeSpan? age = null)
    {
        var clock = new Uptime();
        var presence = new OperatorPresence(clock.Read, age ?? TimeSpan.Zero);
        var changes = new List<PresenceReason?>();
        presence.Changed += (_, _) => changes.Add(presence.Reason);
        presence.Arm();
        return (presence, clock, changes);
    }

    [Test]
    public void SansArmementRienNeVerrouille()
    {
        var clock = new Uptime();
        var presence = new OperatorPresence(clock.Read, OperatorPresence.AgeLimit);
        var changes = 0;
        presence.Changed += (_, _) => changes++;
        Check.False(presence.Latch(PresenceReason.LockAfterStart), "même compte : aucune présence à verrouiller");
        presence.SessionChanged(PresenceReason.SessionLocked);
        presence.Poll(0, Tick);
        for (var i = 0; i < OperatorPresence.RefusalLimit; i++) presence.RecordRefusal();
        Check.False(presence.Armed, "jamais armée");
        Check.False(presence.Latched, "jamais verrouillée sans armement");
        Check.Null(presence.Reason, "aucun motif");
        Check.Equal(0, changes, "aucune notification");
        Check.Throws<ArgumentNullException>(() => new OperatorPresence(null!, TimeSpan.Zero), "compteur absent");
    }

    [Test]
    public void LeVerrouVaDansUnSeulSensAvecUneSeuleNotification()
    {
        var (presence, _, changes) = Armed();
        Check.True(presence.Armed, "armée");
        Check.True(presence.Latch(PresenceReason.LockAfterStart), "premier verrouillage");
        Check.False(presence.Latch(PresenceReason.Idle), "déjà verrouillée");
        presence.Arm();
        presence.SessionChanged(PresenceReason.SessionLocked);
        presence.Poll(Tick, Tick);
        presence.RecordRefusal();
        Check.True(presence.Latched, "toujours verrouillée");
        Check.Equal<PresenceReason?>(PresenceReason.LockAfterStart, presence.Reason, "le premier motif est conservé");
        Check.Equal(1, changes.Count, "une seule notification");
        Check.Equal<PresenceReason?>(PresenceReason.LockAfterStart, changes[0], "état lisible depuis la notification, hors verrou interne");
    }

    [Test]
    public void UnChangementDeSessionAvantLArmementSAppliqueALArmement()
    {
        var clock = new Uptime();
        var presence = new OperatorPresence(clock.Read, TimeSpan.Zero);
        var changes = new List<PresenceReason?>();
        presence.Changed += (_, _) => changes.Add(presence.Reason);
        presence.SessionChanged(PresenceReason.SessionLocked);
        presence.SessionChanged(PresenceReason.SessionDisconnected);
        Check.False(presence.Latched, "situation encore inconnue : rien n'est verrouillé");
        Check.Equal(0, changes.Count, "aucune notification avant l'armement");
        presence.Arm();
        Check.Equal<PresenceReason?>(PresenceReason.SessionLocked, presence.Reason, "le premier motif reçu au démarrage verrouille dès l'armement");
        Check.Equal(1, changes.Count, "une seule notification");
        var (armed, _, _) = Armed();
        armed.SessionChanged(PresenceReason.SessionLogoff);
        Check.Equal<PresenceReason?>(PresenceReason.SessionLogoff, armed.Reason, "armée : le changement de session verrouille aussitôt");
    }

    [Test]
    public void QuinzeMinutesSansSaisieVerrouillent()
    {
        var (presence, clock, _) = Armed();
        var limit = (uint)OperatorPresence.IdleLimit.TotalMilliseconds;
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(Tick - limit + 1, Tick);
        Check.False(presence.Latched, "une milliseconde avant le seuil");
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(Tick - limit, Tick);
        Check.Equal<PresenceReason?>(PresenceReason.Idle, presence.Reason, "15 minutes sans saisie");
    }

    [Test]
    public void LeCompteurSur32BitsRepartDeZero()
    {
        var (presence, clock, _) = Armed();
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(uint.MaxValue - 1000, 2000);
        Check.False(presence.Latched, "saisie juste avant le retour à zéro du compteur : 3 s d'inactivité");
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(3000, 2000);
        Check.False(presence.Latched, "saisie postérieure au compteur lu : saisie récente, jamais 49 jours d'inactivité");
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(uint.MaxValue - (uint)OperatorPresence.IdleLimit.TotalMilliseconds + 2001, 2000);
        Check.Equal<PresenceReason?>(PresenceReason.Idle, presence.Reason, "inactivité à cheval sur le retour à zéro");
    }

    [Test]
    public void DesRelevesReguliersNeVerrouillentPas()
    {
        var (presence, clock, _) = Armed();
        for (var poll = 0; poll < 120; poll++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            presence.Poll(Tick + (uint)poll * 10_000, Tick + (uint)poll * 10_000 + 500);
        }
        Check.False(presence.Latched, "20 minutes de relevés toutes les 10 s avec des saisies récentes");
    }

    [Test]
    public void UnReleveInterrompuVerrouille()
    {
        var (presence, clock, _) = Armed();
        clock.Advance(OperatorPresence.PollGapLimit);
        presence.Poll(Tick, Tick);
        Check.False(presence.Latched, "écart égal à la limite depuis l'armement");
        clock.Advance(OperatorPresence.PollGapLimit - TimeSpan.FromSeconds(1));
        presence.Poll(Tick, Tick);
        Check.False(presence.Latched, "écart mesuré depuis le relevé précédent, pas depuis l'armement");
        clock.Advance(OperatorPresence.PollGapLimit + TimeSpan.FromMilliseconds(1));
        presence.Poll(Tick, Tick);
        Check.Equal<PresenceReason?>(PresenceReason.PollGap, presence.Reason, "relevés interrompus (veille, fil bloqué) : activité non établie, motif propre");
    }

    [Test]
    public void TrenteMinutesApresLeLancementEleveVerrouillent()
    {
        var (presence, clock, _) = Armed(OperatorPresence.AgeLimit - TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(10));
        presence.Poll(Tick, Tick);
        Check.False(presence.Latched, "29 min 40 s après le lancement");
        clock.Advance(TimeSpan.FromSeconds(20));
        presence.Poll(Tick, Tick);
        Check.Equal<PresenceReason?>(PresenceReason.ProcessAge, presence.Reason, "30 minutes, même avec une saisie récente");
        var (young, youngClock, _) = Armed(TimeSpan.FromMinutes(-5));
        for (var poll = 1; poll <= 180; poll++)
        {
            youngClock.Advance(TimeSpan.FromSeconds(10));
            young.Poll(Tick + (uint)poll * 10_000, Tick + (uint)poll * 10_000);
            Check.Equal(poll == 180, young.Latched, "âge négatif compté pour zéro : verrou à 30 minutes pile (relevé " + poll + ")");
        }
        Check.Equal<PresenceReason?>(PresenceReason.ProcessAge, young.Reason, "âge du processus");
    }

    [Test]
    public void TroisRefusEnDixMinutesVerrouillent()
    {
        var (presence, clock, _) = Armed();
        presence.RecordRefusal();
        clock.Advance(TimeSpan.FromMinutes(5));
        presence.RecordRefusal();
        clock.Advance(TimeSpan.FromMinutes(5));
        presence.RecordRefusal();
        Check.False(presence.Latched, "le premier refus sort de la fenêtre glissante à 10 minutes pile");
        clock.Advance(TimeSpan.FromMinutes(4));
        presence.RecordRefusal();
        Check.Equal<PresenceReason?>(PresenceReason.Refusals, presence.Reason, "3 refus en moins de 10 minutes");
    }

    [Test]
    public void UnSeulAppelVerrouilleMemeEnParallele()
    {
        var (presence, _, changes) = Armed();
        var reasons = Enum.GetValues<PresenceReason>();
        var winners = 0;
        Parallel.For(0, 64, index => { if (presence.Latch(reasons[index % reasons.Length])) Interlocked.Increment(ref winners); });
        Check.Equal(1, winners, "un seul gagnant");
        Check.Equal(1, changes.Count, "une seule notification");
    }

    /// <summary>Compteur monotone en millisecondes, avancé par le test.</summary>
    private sealed class Uptime
    {
        private long now = 3_600_000;
        public long Read() => Interlocked.Read(ref now);
        public void Advance(TimeSpan by) => Interlocked.Add(ref now, (long)by.TotalMilliseconds);
    }
}
