using Filpro.App;
using Filpro.Core;

namespace Filpro.Tests;

internal sealed class NativeWindowPolicyTests
{
    private const string Host = "S-1-5-21-1-2-3-1002";
    private const string Session = "S-1-5-21-1-2-3-1001";
    private const string HostAccount = @"DOMAINE\tech";
    private const string SessionAccount = @"POSTE\jdupont";

    [Test]
    public void AffichageClasseDApresLeProcessusNavigateur()
    {
        Check.Equal(SessionSituation.SameAccount, NativeWindowPolicy.Classify(Host, Host, null), "navigateur sous le compte élevé, sans compte de session distinct");
        Check.Equal(SessionSituation.SameAccount, NativeWindowPolicy.Classify(Host, Host.ToLowerInvariant(), Host), "SID comparés sans casse, session concordante");
        Check.Equal(SessionSituation.CrossAccount, NativeWindowPolicy.Classify(Host, Session, Session), "navigateur sous le compte de la session");
        Check.Equal(SessionSituation.CrossAccount, NativeWindowPolicy.Classify(Host, Session, null), "le navigateur fait foi sans compte de session");
        Check.Equal(SessionSituation.Unknown, NativeWindowPolicy.Classify(Host, Host, Session),
            "navigateur sous le compte élevé mais profil préparé pour un autre compte : désaccord indéterminé");
        Check.Equal(SessionSituation.Unknown, NativeWindowPolicy.Classify(Host, null, Session), "processus navigateur introuvable");
        Check.Equal(SessionSituation.Unknown, NativeWindowPolicy.Classify(Host, "", null), "propriétaire illisible");
        Check.Equal(SessionSituation.Unknown, NativeWindowPolicy.Classify(null, Host, null), "compte élevé illisible");
    }

    [Test]
    public void RecouvrementStrictDesFenetres()
    {
        var dialog = new ScreenRect(100, 100, 500, 400);
        Check.True(NativeWindowPolicy.Intersects(dialog, new ScreenRect(450, 350, 900, 900)), "coin recouvert");
        Check.True(NativeWindowPolicy.Intersects(dialog, new ScreenRect(0, 0, 2000, 2000)), "fenêtre qui couvre tout l'écran");
        Check.True(NativeWindowPolicy.Intersects(dialog, new ScreenRect(200, 200, 300, 300)), "petite fenêtre posée dessus");
        Check.True(NativeWindowPolicy.Intersects(new ScreenRect(-1920, 0, -10, 1080), new ScreenRect(-100, 500, 50, 600)), "écran à gauche, coordonnées négatives");
        Check.False(NativeWindowPolicy.Intersects(dialog, new ScreenRect(500, 100, 800, 400)), "bord droit commun : aucun pixel partagé");
        Check.False(NativeWindowPolicy.Intersects(dialog, new ScreenRect(100, 400, 500, 700)), "bord bas commun : aucun pixel partagé");
        Check.False(NativeWindowPolicy.Intersects(dialog, new ScreenRect(600, 0, 900, 99)), "fenêtre éloignée");
        Check.False(NativeWindowPolicy.Intersects(dialog, new ScreenRect(200, 200, 200, 300)), "rectangle vide");
        Check.False(NativeWindowPolicy.Intersects(default, dialog), "cadre illisible");
    }

    [Test]
    public void LAccordExigeLArmementAvantLEcheance()
    {
        var armed = TimeSpan.FromSeconds(1.5);
        var timeout = NativeWindowPolicy.ResponseTimeout;
        Check.True(NativeWindowPolicy.CanApprove(armed, armed, true, false, false), "armée, active, découverte, avant l'échéance");
        Check.False(NativeWindowPolicy.CanApprove(armed - TimeSpan.FromMilliseconds(1), armed, true, false, false), "délai d'armement non écoulé");
        Check.False(NativeWindowPolicy.CanApprove(armed, armed, false, false, false), "fenêtre inactive");
        Check.False(NativeWindowPolicy.CanApprove(armed, armed, true, true, false), "fenêtre recouverte");
        Check.False(NativeWindowPolicy.CanApprove(armed, armed, true, false, true), "décision déjà prise");
        Check.True(NativeWindowPolicy.CanApprove(timeout - TimeSpan.FromMilliseconds(1), armed, true, false, false), "juste avant l'échéance");
        Check.False(NativeWindowPolicy.CanApprove(timeout, armed, true, false, false), "échéance atteinte, minuterie pas encore passée");
        Check.False(NativeWindowPolicy.CanApprove(timeout + TimeSpan.FromSeconds(1), armed, true, false, false), "après l'échéance");
    }

    [Test]
    public void DelaiDArmementEtCompteARebours()
    {
        Check.Equal(TimeSpan.FromSeconds(1.5), NativeWindowPolicy.ArmingDelay(requestedInForeground: true), "demande reçue au premier plan");
        Check.Equal(TimeSpan.FromSeconds(3), NativeWindowPolicy.ArmingDelay(requestedInForeground: false), "demande reçue en arrière-plan");
        Check.Equal(TimeSpan.FromSeconds(120), NativeWindowPolicy.ResponseTimeout, "délai de réponse annoncé par confirmation.timeout");
        Check.Equal("Annulation automatique dans 2:00", NativeWindowPolicy.CountdownText(NativeWindowPolicy.ResponseTimeout), "ouverture");
        Check.Equal("Annulation automatique dans 1:58", NativeWindowPolicy.CountdownText(TimeSpan.FromSeconds(118)), "secondes sur deux chiffres");
        Check.Equal("Annulation automatique dans 1:58", NativeWindowPolicy.CountdownText(TimeSpan.FromSeconds(117.2)), "seconde entamée comptée");
        Check.Equal("Annulation automatique dans 0:09", NativeWindowPolicy.CountdownText(TimeSpan.FromSeconds(9)), "moins d'une minute");
        Check.Equal("Annulation automatique dans 0:01", NativeWindowPolicy.CountdownText(TimeSpan.FromMilliseconds(1)), "dernière seconde");
        Check.Equal("Annulation automatique dans 0:00", NativeWindowPolicy.CountdownText(TimeSpan.Zero), "échéance");
        Check.Equal("Annulation automatique dans 0:00", NativeWindowPolicy.CountdownText(TimeSpan.FromSeconds(-3)), "jamais négatif");
    }

    [Test]
    public void LigneDeSituationEtJournal()
    {
        var same = new HostSituation(SessionSituation.SameAccount, Host, HostAccount, Host, HostAccount);
        var cross = new HostSituation(SessionSituation.CrossAccount, Host, HostAccount, Session, SessionAccount);
        var unknown = new HostSituation(SessionSituation.Unknown, Host, HostAccount, Host, HostAccount);
        Check.Equal(@"Compte élevé : DOMAINE\tech · Affichage : DOMAINE\tech (même compte)", NativeWindowPolicy.SituationLine(same, "ignoré"), "même compte");
        Check.Equal(@"Compte élevé : DOMAINE\tech · Affichage : POSTE\jdupont (autre compte)", NativeWindowPolicy.SituationLine(cross, "ignoré"), "autre compte");
        Check.Equal(@"Compte élevé : DOMAINE\tech · Affichage : compte indéterminé", NativeWindowPolicy.SituationLine(unknown, "ignoré"),
            "désaccord jamais présenté comme le même compte");
        Check.Equal(@"Compte élevé : DOMAINE\tech · Affichage : compte indéterminé",
            NativeWindowPolicy.SituationLine(same with { DisplayAccount = null }, "ignoré"), "compte d'affichage sans nom");
        Check.Equal(@"Compte élevé : DOMAINE\admin · Affichage : compte indéterminé", NativeWindowPolicy.SituationLine(null, @"DOMAINE\admin"),
            "situation absente : compte élevé lu ailleurs");
        Check.Equal(@"Affichage sous POSTE\jdupont, FILPRO élevé sous DOMAINE\tech : autre compte ; mises à jour indisponibles, verrouillage de présence actif.",
            NativeWindowPolicy.SituationLog(cross), "journal autre compte");
        Check.Equal(@"Affichage sous DOMAINE\tech, FILPRO élevé sous DOMAINE\tech : même compte.", NativeWindowPolicy.SituationLog(same), "journal même compte");
        Check.Equal(@"Affichage sous un compte indéterminé, FILPRO élevé sous DOMAINE\tech : situation indéterminée ; mises à jour indisponibles, verrouillage de présence actif.",
            NativeWindowPolicy.SituationLog(unknown with { DisplaySid = null, DisplayAccount = null }), "journal indéterminé");
    }

    [Test]
    public void CompteDeLaSessionEtMotifsDuVerrou()
    {
        Check.Equal(Session, NativeWindowPolicy.SessionSid(Host, Session, null), "moteur d'affichage sous un autre compte");
        Check.Equal(Session, NativeWindowPolicy.SessionSid(Host, Session, Host), "le moteur fait foi");
        Check.Equal(Session, NativeWindowPolicy.SessionSid(Host, Host, Session), "désaccord : compte retenu pour le profil d'affichage");
        Check.Equal(Session, NativeWindowPolicy.SessionSid(Host, null, Session), "moteur illisible : compte du profil d'affichage");
        Check.Null(NativeWindowPolicy.SessionSid(Host, Host.ToLowerInvariant(), null), "même compte, casse comprise");
        Check.Null(NativeWindowPolicy.SessionSid(Host, null, null), "aucun compte connu");
        Check.Equal<PresenceReason?>(PresenceReason.SessionLocked, NativeWindowPolicy.PresenceReasonFor(7), "WTS_SESSION_LOCK");
        Check.Equal<PresenceReason?>(PresenceReason.SessionDisconnected, NativeWindowPolicy.PresenceReasonFor(2), "WTS_CONSOLE_DISCONNECT");
        Check.Equal<PresenceReason?>(PresenceReason.SessionDisconnected, NativeWindowPolicy.PresenceReasonFor(4), "WTS_REMOTE_DISCONNECT");
        Check.Equal<PresenceReason?>(PresenceReason.SessionLogoff, NativeWindowPolicy.PresenceReasonFor(6), "WTS_SESSION_LOGOFF");
        foreach (var other in new[] { 0, 1, 3, 5, 8, 9, 10, 11 })
            Check.Null(NativeWindowPolicy.PresenceReasonFor(other), "connexion, ouverture ou déverrouillage : aucun verrou (" + other + ")");
        foreach (var reason in Enum.GetValues<PresenceReason>())
            Check.False(NativeWindowPolicy.PresenceReasonText(reason) == NativeWindowPolicy.PresenceReasonText(null), reason + " : motif nommé");
        Check.Equal("15 minutes sans saisie", NativeWindowPolicy.PresenceReasonText(PresenceReason.Idle), "inactivité");
        Check.Equal("veille ou relevés interrompus plus de 2 minutes", NativeWindowPolicy.PresenceReasonText(PresenceReason.PollGap),
            "veille distinguée de l'inactivité");
        Check.Equal(TimeSpan.FromMinutes(2), NativeWindowPolicy.PickerTimeout, "sélecteur fermé après 2 minutes");
        Check.Equal<PresenceReason?>(PresenceReason.SessionLocked, NativeWindowPolicy.SessionStateReason(0, 0), "active et verrouillée");
        Check.Null(NativeWindowPolicy.SessionStateReason(0, 1), "active et ouverte");
        Check.Null(NativeWindowPolicy.SessionStateReason(0, -1), "état de verrouillage inconnu : rien n'est conclu");
        Check.Equal<PresenceReason?>(PresenceReason.SessionDisconnected, NativeWindowPolicy.SessionStateReason(4, 1), "déconnectée");
        Check.Equal<PresenceReason?>(PresenceReason.SessionDisconnected, NativeWindowPolicy.SessionStateReason(4, -1), "déconnectée, verrouillage inconnu");
        foreach (var state in new[] { 1, 2, 3, 5, 6, 7, 8, 9 })
            Check.Null(NativeWindowPolicy.SessionStateReason(state, 1), "état de connexion " + state + " sans verrouillage : rien n'est conclu");
    }

    [Test]
    public void OriginesEtPointsDAttention()
    {
        Check.Equal("FILPRO", NativeWindowPolicy.OriginTag(FactOrigin.Filpro), "valeur lue par FILPRO");
        Check.Equal("magasin", NativeWindowPolicy.OriginTag(FactOrigin.Store), "valeur déclarée par le magasin");
        Check.Equal("interface", NativeWindowPolicy.OriginTag(FactOrigin.Interface), "valeur reçue de la page");
        Check.Throws<ArgumentOutOfRangeException>(() => NativeWindowPolicy.OriginTag((FactOrigin)42), "origine inconnue refusée");
        ConfirmationAttention[] sheet = [new("Sauvegarde non chiffrée."), new("Destination sur un partage réseau : les données quittent ce poste.", Severe: true)];
        Check.True(sheet.SequenceEqual(NativeWindowPolicy.Attention(sheet, requestedInForeground: true)), "premier plan : points de la fiche seuls");
        var background = NativeWindowPolicy.Attention(sheet, requestedInForeground: false);
        Check.True(background.Take(2).SequenceEqual(sheet), "arrière-plan : points de la fiche conservés dans l'ordre");
        Check.Equal(3, background.Count, "arrière-plan : une seule ligne ajoutée");
        Check.Equal(new ConfirmationAttention("Demande reçue alors que FILPRO n’était pas la fenêtre active.", Severe: true), background[2],
            "arrière-plan : ligne grave ajoutée");
    }

    [Test]
    public void ProgrammeQuiRecouvreNommeSansCaractereTrompeur()
    {
        Check.Equal("Bouton désactivé : la fenêtre « Teams » recouvre cette confirmation. Déplacez-la pour continuer.", NativeWindowPolicy.ObscuredText("Teams"),
            "programme nommé");
        Check.Contains("« programme inconnu »", NativeWindowPolicy.ObscuredText(""), "nom illisible");
        Check.Contains("« programme inconnu »", NativeWindowPolicy.ObscuredText(null), "processus disparu");
        Check.Equal("evil⟨U+202E⟩gpj.exe", NativeWindowPolicy.ProgramName("evil\u202Egpj.exe"), "inversion bidi rendue visible");
        Check.Equal("a⟨U+000A⟩b⟨U+200B⟩c⟨U+2028⟩d⟨U+0085⟩e⟨U+FEFF⟩", NativeWindowPolicy.ProgramName("a\nb\u200Bc\u2028d\u0085e\uFEFF"),
            "saut de ligne, largeur nulle, séparateur de ligne, C1 et BOM rendus visibles");
        Check.Equal(new string('x', 64) + "…", NativeWindowPolicy.ProgramName(new string('x', 100)), "nom borné");
        Check.Equal("😀😀…", NativeWindowPolicy.Visible("😀😀😀", 2), "coupure entre caractères, jamais dans une paire de substitution");
        Check.Equal(@"C:\Données\é ⟨U+0009⟩", NativeWindowPolicy.Visible("C:\\Données\\é \t", int.MaxValue), "texte ordinaire conservé, tabulation rendue visible");
    }

    [Test]
    public void VariablesWebView2RetireesEtNommeesSeulement()
    {
        const string Variable = "WEBVIEW2_FILPRO_TEST";
        const string MixedCase = "WebView2_Filpro_Casse_Test";
        const string Unrelated = "FILPRO_WEBVIEW2_TEST";
        const string Value = "--remote-debugging-port=9222";
        try
        {
            Environment.SetEnvironmentVariable(Variable, Value);
            Environment.SetEnvironmentVariable(MixedCase, Value);
            Environment.SetEnvironmentVariable(Unrelated, "conservée");
            var removed = EnvironmentGuard.RemoveWebView2Overrides();
            Check.True(removed.Contains(Variable), "variable WEBVIEW2_ retirée et nommée");
            Check.True(removed.Contains(MixedCase), "préfixe reconnu sans casse");
            Check.False(removed.Contains(Unrelated), "préfixe ailleurs qu'au début : variable conservée");
            Check.False(removed.Any(name => name.Contains(Value, StringComparison.Ordinal)), "aucune valeur rapportée");
            Check.Null(Environment.GetEnvironmentVariable(Variable), "variable retirée du processus");
            Check.Null(Environment.GetEnvironmentVariable(MixedCase), "variable de casse différente retirée du processus");
            Check.Equal("conservée", Environment.GetEnvironmentVariable(Unrelated), "autre variable intacte");
            Check.Equal(0, EnvironmentGuard.RemoveWebView2Overrides().Count, "plus rien à retirer");
            var described = EnvironmentGuard.Describe(removed);
            Check.Contains(Variable, described, "nom écrit au journal");
            Check.False(described.Contains(Value, StringComparison.Ordinal), "valeur jamais écrite au journal");
            Check.Equal("WEBVIEW2_A⟨U+000A⟩B, " + "WEBVIEW2_" + new string('X', 55) + "…",
                EnvironmentGuard.Describe(["WEBVIEW2_A\nB", "WEBVIEW2_" + new string('X', 80)]), "nom qui ne coupe ni n'allonge sans fin la ligne du journal");
        }
        finally
        {
            foreach (var name in new[] { Variable, MixedCase, Unrelated }) Environment.SetEnvironmentVariable(name, null);
        }
    }
}
