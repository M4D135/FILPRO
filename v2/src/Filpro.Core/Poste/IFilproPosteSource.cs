namespace Filpro.Core;

/// <summary>
/// Demande d'inventaire logiciel d'un profil. <see cref="AdditionalFiles"/> porte les éléments déclarés à inclure s'ils
/// existent (modèles communs, éléments d'une dernière sélection) ; <see cref="MeasureSizes"/> déclenche une mesure lourde
/// (fausse par défaut dans ce lot : les tailles restent honnêtement inconnues).
/// </summary>
public sealed record SoftwareQuery(
    IReadOnlyList<SoftwareItem> AdditionalFiles,
    IReadOnlyList<string> AdditionalRegistryKeys,
    bool MeasureSizes = false)
{
    public static SoftwareQuery Empty { get; } = new([], [], MeasureSizes: false);
}

/// <summary>
/// Contrat du fournisseur « poste » pour les écrans Analyser/réparer et Sauvegarder : lecture seule des profils, des
/// sessions WTS, de la sélection groupée fichiers+registre et de la dernière sélection. L'enregistrement des choix ne
/// modifie que les préférences FILPRO de l'opérateur, jamais les profils inventoriés. Le fournisseur natif
/// (Filpro.Platform) lit le poste ; le fournisseur de démonstration (Filpro.Simulation) fabrique un monde fictif séparé
/// et conserve ses choix uniquement en mémoire. Les deux signalent honnêtement ce qu'ils n'ont pas pu lire.
/// </summary>
public interface IFilproPosteSource
{
    /// <summary>Vrai si les données viennent du monde fictif de démonstration (jamais présentées comme réelles).</summary>
    bool Demonstration { get; }

    /// <summary>
    /// SID du compte qui exécute FILPRO, lu depuis le jeton courant (identité fictive en démonstration).
    /// Une identité inconnue provoque un refus : elle ne peut jamais devenir une absence de garde de restauration.
    /// </summary>
    string ReadCurrentAccountSid(CancellationToken cancellationToken);

    /// <summary>Profils et sessions WTS. Ne doit jamais écrire ni monter de ruche.</summary>
    ProfileInventory ReadProfiles(CancellationToken cancellationToken);

    /// <summary>Sélection groupée fichiers+registre d'un profil, avec recommandations et modèles communs demandés.</summary>
    SoftwareInventory ReadSoftware(string sid, SoftwareQuery query, CancellationToken cancellationToken);

    /// <summary>Dernière sélection enregistrée pour ce compte, ou <c>null</c> s'il n'y en a pas (jamais inventée).</summary>
    LastSelection? ReadLastSelection(string sid, CancellationToken cancellationToken);

    /// <summary>
    /// Mémorise explicitement les fichiers et clés choisis pour ce SID. L'horodatage vient du fournisseur ; aucun chemin
    /// de stockage ne vient de l'interface. Une sélection vide est un choix conservé, distinct d'une absence de choix.
    /// </summary>
    LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken);
}
