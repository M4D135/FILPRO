using System.Globalization;
using System.Text;
using Filpro.Core;

namespace Filpro.App;

/// <summary>
/// Règles pures de la fenêtre « FILPRO — Confirmation Windows », sans WinForms ni appel natif (liées aux tests comme
/// <see cref="StartupPolicy"/>) : compte de l'affichage, recouvrement, délai d'armement du bouton d'accord et textes.
/// </summary>
internal static class NativeWindowPolicy
{
    /// <summary>Sans décision dans ce délai, la fenêtre se ferme et rien n'est lancé.</summary>
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Affichage sous un autre compte : un sélecteur de fichiers resté ouvert se ferme seul après ce délai.</summary>
    internal static readonly TimeSpan PickerTimeout = TimeSpan.FromSeconds(120);

    internal const string BackgroundRequest = "Demande reçue alors que FILPRO n’était pas la fenêtre active.";

    private const int ProgramNameLimit = 64;

    /// <summary>
    /// Compte de l'affichage comparé au compte élevé de FILPRO. Le propriétaire du processus navigateur de WebView2 fait
    /// foi ; le compte de la session retenu pour le profil d'affichage, quand il est connu, doit le confirmer. Un compte
    /// introuvable ou un désaccord donne <see cref="SessionSituation.Unknown"/>.
    /// </summary>
    internal static SessionSituation Classify(string? hostSid, string? browserSid, string? shellSid)
    {
        if (string.IsNullOrEmpty(hostSid) || string.IsNullOrEmpty(browserSid)) return SessionSituation.Unknown;
        if (!SameSid(browserSid, hostSid)) return SessionSituation.CrossAccount;
        return string.IsNullOrEmpty(shellSid) || SameSid(shellSid, hostSid) ? SessionSituation.SameAccount : SessionSituation.Unknown;
    }

    /// <summary>Recouvrement de deux rectangles d'écran, bords exclus ; un rectangle vide ne recouvre rien.</summary>
    internal static bool Intersects(ScreenRect first, ScreenRect second) =>
        !first.IsEmpty && !second.IsEmpty
        && first.Left < second.Right && second.Left < first.Right && first.Top < second.Bottom && second.Top < first.Bottom;

    /// <summary>Délai avant que le bouton d'accord ne s'arme, repris à chaque activation de la fenêtre.</summary>
    internal static TimeSpan ArmingDelay(bool requestedInForeground) => TimeSpan.FromSeconds(requestedInForeground ? 1.5 : 3);

    /// <summary>
    /// L'accord n'est possible qu'armé et avant l'échéance : un clic arrivé après le délai de réponse, avant que la minuterie
    /// n'annule la fenêtre, est refusé.
    /// </summary>
    internal static bool CanApprove(TimeSpan elapsed, TimeSpan armedAt, bool active, bool obscured, bool decided) =>
        !decided && active && !obscured && elapsed >= armedAt && elapsed < ResponseTimeout;

    /// <summary>« Annulation automatique dans 1:58 » : secondes entamées, jamais négatives.</summary>
    internal static string CountdownText(TimeSpan remaining)
    {
        var seconds = remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"Annulation automatique dans {seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>Ligne de situation de la fenêtre ; sans situation établie, l'affichage reste indéterminé.</summary>
    internal static string SituationLine(HostSituation? situation, string hostAccount)
    {
        var display = situation is { DisplayAccount: { } account } ? situation.Kind switch
        {
            SessionSituation.SameAccount => account + " (même compte)",
            SessionSituation.CrossAccount => account + " (autre compte)",
            _ => null,
        } : null;
        return "Compte élevé : " + (situation?.HostAccount ?? hostAccount) + " · Affichage : " + (display ?? "compte indéterminé");
    }

    /// <summary>Ligne unique du journal technique : comptes, verdict et protections actives, rien de la page.</summary>
    internal static string SituationLog(HostSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        var verdict = situation.Kind switch
        {
            SessionSituation.SameAccount => "même compte",
            SessionSituation.CrossAccount => "autre compte",
            _ => "situation indéterminée",
        };
        return "Affichage sous " + (situation.DisplayAccount ?? "un compte indéterminé") + ", FILPRO élevé sous " + situation.HostAccount
            + " : " + verdict + (situation.Kind == SessionSituation.SameAccount ? "." : " ; mises à jour indisponibles, verrouillage de présence actif.");
    }

    /// <summary>
    /// Compte de la session dont les droits sur le dossier de FILPRO comptent : le propriétaire du moteur d'affichage s'il
    /// diffère du compte élevé, sinon le compte retenu pour le profil d'affichage s'il en diffère ; nul sinon.
    /// </summary>
    internal static string? SessionSid(string hostSid, string? browserSid, string? shellSid)
    {
        if (!string.IsNullOrEmpty(browserSid) && !SameSid(browserSid, hostSid)) return browserSid;
        return !string.IsNullOrEmpty(shellSid) && !SameSid(shellSid, hostSid) ? shellSid : null;
    }

    /// <summary>Motif de verrouillage d'une notification de session Windows (WM_WTSSESSION_CHANGE) ; nul pour les autres.</summary>
    internal static PresenceReason? PresenceReasonFor(int sessionChange) => sessionChange switch
    {
        // WTS_SESSION_LOCK, WTS_CONSOLE_DISCONNECT, WTS_REMOTE_DISCONNECT, WTS_SESSION_LOGOFF.
        7 => PresenceReason.SessionLocked,
        2 or 4 => PresenceReason.SessionDisconnected,
        6 => PresenceReason.SessionLogoff,
        _ => null,
    };

    /// <summary>
    /// Motif de verrouillage d'après l'état courant de la session (WTSINFOEX_LEVEL1) : session déconnectée
    /// (WTSDisconnected = 4) ou verrouillée (SessionFlags = WTS_SESSIONSTATE_LOCK = 0) ; nul pour une session active et
    /// ouverte, ou un état inconnu (-1).
    /// </summary>
    internal static PresenceReason? SessionStateReason(int connectState, int sessionFlags) => connectState switch
    {
        4 => PresenceReason.SessionDisconnected,
        _ when sessionFlags == 0 => PresenceReason.SessionLocked,
        _ => null,
    };

    /// <summary>Motif du verrouillage, en français, pour le journal technique.</summary>
    internal static string PresenceReasonText(PresenceReason? reason) => reason switch
    {
        PresenceReason.SessionLocked => "session Windows verrouillée",
        PresenceReason.SessionDisconnected => "session Windows déconnectée",
        PresenceReason.SessionLogoff => "fermeture de la session Windows",
        PresenceReason.LockAfterStart => "verrouillage demandé au lancement d’une opération",
        PresenceReason.Idle => "15 minutes sans saisie",
        PresenceReason.PollGap => "veille ou relevés interrompus plus de 2 minutes",
        PresenceReason.ProcessAge => "30 minutes depuis le lancement élevé",
        PresenceReason.Refusals => "3 refus ou absences de réponse en 10 minutes",
        _ => "motif inconnu",
    };

    /// <summary>Étiquette d'origine d'une valeur : lue par FILPRO, déclarée par le magasin ou reçue de la page.</summary>
    internal static string OriginTag(FactOrigin origin) => origin switch
    {
        FactOrigin.Filpro => "FILPRO",
        FactOrigin.Store => "magasin",
        FactOrigin.Interface => "interface",
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    /// <summary>Points d'attention de la fiche, plus une ligne grave quand FILPRO n'était pas au premier plan à la demande.</summary>
    internal static IReadOnlyList<ConfirmationAttention> Attention(IReadOnlyList<ConfirmationAttention> attention, bool requestedInForeground)
    {
        ArgumentNullException.ThrowIfNull(attention);
        return requestedInForeground ? attention : [.. attention, new ConfirmationAttention(BackgroundRequest, Severe: true)];
    }

    /// <summary>Motif d'un bouton d'accord désactivé par la fenêtre d'un autre programme posée par-dessus.</summary>
    internal static string ObscuredText(string? program) =>
        "Bouton désactivé : la fenêtre « " + ProgramName(program) + " » recouvre cette confirmation. Déplacez-la pour continuer.";

    /// <summary>Nom de processus d'un autre programme, donc choisi par lui : rendu visible et borné.</summary>
    internal static string ProgramName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "programme inconnu" : Visible(name, ProgramNameLimit);

    /// <summary>
    /// Texte dont FILPRO ne choisit pas les caractères : contrôle, mise en forme (bidi, largeur nulle) et séparateurs de
    /// lignes rendus visibles (⟨U+XXXX⟩) ; au-delà de <paramref name="limit"/> caractères, la fin est remplacée par « … ».
    /// </summary>
    internal static string Visible(string text, int limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        var visible = new StringBuilder();
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ == limit) { visible.Append('…'); break; }
            if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator)
                visible.Append(CultureInfo.InvariantCulture, $"⟨U+{rune.Value:X4}⟩");
            else visible.Append(rune.ToString());
        }
        return visible.ToString();
    }

    private static bool SameSid(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Rectangle d'écran en pixels physiques, bords droit et bas exclus, comme le RECT de Windows.</summary>
internal readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    internal bool IsEmpty => Right <= Left || Bottom <= Top;
}
