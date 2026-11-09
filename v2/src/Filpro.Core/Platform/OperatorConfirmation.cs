namespace Filpro.Core;

/// <summary>Origine d'une information montrée dans la fenêtre de confirmation Windows.</summary>
public enum FactOrigin
{
    /// <summary>Lue ou calculée par FILPRO : poste, pack, plan relu et conservé par l'hôte.</summary>
    Filpro,
    /// <summary>Déclarée par le manifeste du magasin relu par FILPRO.</summary>
    Store,
    /// <summary>Chaîne reçue de la page (chemin saisi ou choisi, SHA-256 saisi) : jamais une preuve.</summary>
    Interface,
}

/// <summary>Une ligne de la fenêtre de confirmation : libellé fixe, valeur détenue par l'hôte et son origine.</summary>
public sealed record ConfirmationFact(string Label, string Value, FactOrigin Origin);

/// <summary>Point d'attention calculé par l'hôte ; <see cref="Severe"/> s'affiche en rouge.</summary>
public sealed record ConfirmationAttention(string Text, bool Severe = false);

/// <summary>
/// Contenu de la fenêtre de confirmation Windows. Il est construit uniquement à partir d'objets détenus par l'hôte, jamais à
/// partir d'un texte libre de la page. <see cref="Digest"/> lie la fenêtre à l'objet qui sera exécuté.
/// <see cref="OfferLockAfterStart"/> ajoute la case « Verrouiller FILPRO après le lancement » (autre compte seulement).
/// </summary>
public sealed record ConfirmationSheet(
    string Operation,
    string Title,
    string ApproveLabel,
    string Digest,
    IReadOnlyList<ConfirmationFact> Facts,
    IReadOnlyList<ConfirmationAttention> Attention,
    bool OfferLockAfterStart = false);

/// <summary>Décision prise dans la fenêtre de confirmation Windows.</summary>
public enum ConfirmationOutcome { Approved, Refused, TimedOut }

/// <summary>
/// Décision de la fenêtre et choix de la case « Verrouiller FILPRO après le lancement » ; la case n'a d'effet qu'avec un
/// accord sur une fiche qui la proposait.
/// </summary>
public sealed record ConfirmationResult(ConfirmationOutcome Outcome, bool LockAfterStart = false);

/// <summary>
/// Fenêtre de confirmation de l'hôte élevé, hors de la page. Seule une décision <see cref="ConfirmationOutcome.Approved"/>
/// autorise une opération privilégiée ; l'annulation du jeton ferme la fenêtre et lève <see cref="OperationCanceledException"/>.
/// </summary>
public interface IOperatorConfirmation
{
    Task<ConfirmationResult> ConfirmAsync(ConfirmationSheet sheet, CancellationToken cancellationToken);
}

/// <summary>Compte sous lequel tourne l'affichage, comparé au compte élevé de FILPRO.</summary>
public enum SessionSituation { SameAccount, CrossAccount, Unknown }

/// <summary>
/// Situation de la fenêtre, établie par l'hôte une fois l'affichage démarré. Une situation absente ou indéterminée est
/// traitée comme <see cref="SessionSituation.Unknown"/>.
/// </summary>
public sealed record HostSituation(SessionSituation Kind, string HostSid, string HostAccount, string? DisplaySid, string? DisplayAccount);
