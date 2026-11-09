using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Aperçu du plan de restauration renvoyé à l'écran avant toute confirmation : le plan canonisé produit par la fonction
/// pure <see cref="RestorePlan.Validate"/>, augmenté de ce que le pont a dû <b>déclarer</b> à la place d'un lecteur de
/// magasin (aucun manifeste n'est relu dans ce lot) et des limites honnêtes à afficher. Aucun champ n'affirme une
/// vérification : l'exploitabilité du magasin est déclarée par l'opérateur, les groupes de conflits viennent du
/// fournisseur poste, et rien n'a été lu, écrit, monté ni restauré.
/// </summary>
public sealed record RestorePlanPreview(
    RestorePlanReview Plan,
    RestoreStoreDeclaration Store,
    string ConflictNotice,
    IReadOnlyList<string> Limitations);

/// <summary>
/// État du magasin source <b>tel que déclaré par l'opérateur</b> et transmis à la validation pure.
/// <see cref="Ready"/> est la déclaration d'exploitabilité : le pont ne la pose jamais de lui-même (elle vient d'une case
/// que l'opérateur coche) et <see cref="RestorePlan.Validate"/> refuse un magasin non déclaré prêt. Le contenu du manifeste
/// n'est jamais lu ni représenté : seuls des comptes et des chemins sont portés, jamais une clé.
/// </summary>
public sealed record RestoreStoreDeclaration(
    string Bundle,
    string Status,
    bool Ready,
    bool DeclaredByOperator,
    bool Legacy,
    bool Partial,
    bool Encrypted,
    int ManifestUserCount,
    string? UsmtVersion,
    string? SourceComputerName,
    string? SourceCreatedUtc);
