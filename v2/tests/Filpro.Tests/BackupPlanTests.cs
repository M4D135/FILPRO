using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Contrat et fonction pure du plan de sauvegarde multi-profils : sélection vide refusée, compte explicite exigé, profil
/// Entra refusé, profil chargé sans VSS refusé, destination et clé bornées, besoin d'espace explicitement inconnu et
/// exclusions annoncées. Aucune E/S, aucun moteur : rien n'est exécuté sur le poste.
/// </summary>
internal sealed class BackupPlanTests
{
    private const string PlanId = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Created = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    private static SoftwareItem Folder(string rel, bool selected = true) =>
        new(SoftwareScope.Roaming, rel, rel, SoftwareCategory.Donnees, selected, false, false, null, false, false);

    private static SoftwareItem Key(string rel, bool selected = true) =>
        new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Preferences, selected, false, false, null, false, false);

    private static BackupProfileSelection Profile(
        string sid = "S-1-5-21-1-2-3-1001",
        string account = "LAB\\alice",
        string path = "C:\\Users\\alice",
        bool? loaded = false) =>
        new(sid, ProfileIdentityKind.CompteLocalOuDomaine, account, path, loaded, [Folder("Contoso\\App")], [Key("Software\\Contoso")]);

    private static BackupPlanRequest Request(
        IReadOnlyList<BackupProfileSelection>? profiles = null,
        string destination = "\\\\serveur\\partage\\filpro",
        bool encrypted = false,
        string? keyPath = null,
        bool useVss = false,
        bool noCompress = false,
        bool continueOnError = false,
        bool demonstration = false,
        string? purpose = null,
        IReadOnlyList<BackupExclusion>? exclusions = null) =>
        new(profiles ?? [Profile()], destination, encrypted, keyPath, useVss, noCompress, continueOnError, demonstration, purpose, exclusions);

    private static BackupPlanReview Create(BackupPlanRequest request) => BackupPlan.Create(request, PlanId, Created);

    [Test]
    public void MigAppHorsProfilEstRefuseAvantPreparation()
    {
        foreach (var demonstration in new[] { false, true })
        {
            var error = Check.Throws<FilproOperationException>(() => Create(Request(demonstration: demonstration) with { MigApp = true }), "MigApp sort du profil");
            Check.Equal(BackupPlan.ProfileScopeCode, error.Code, "refus explicite de portée");
        }
    }

    [Test]
    public void SelectionVideEstRefusee()
    {
        var empty = Check.Throws<FilproOperationException>(() => Create(Request(profiles: [])), "aucun profil n'est accepté");
        Check.Equal(BackupPlan.SelectionCode, empty.Code, "code backup.selection");
        Check.Contains("au moins un profil", empty.Message, "le refus annonce la sélection vide");
        var missing = Check.Throws<FilproOperationException>(() => Create(Request() with { Profiles = null! }), "une liste absente est refusée");
        Check.Equal(BackupPlan.SelectionCode, missing.Code, "même code pour une liste absente");
        var nullProfile = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [null!])), "un profil null est refusé sans être retiré en silence");
        Check.Contains("Profil sélectionné invalide", nullProfile.Message, "le profil invalide est nommé");
    }

    [Test]
    public void CompteDoitEtreExpliciteEtUnique()
    {
        var bare = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(account: "alice")])), "un compte sans domaine est refusé");
        Check.Contains("Compte non explicite", bare.Message, "le compte non résolu est nommé");
        var sidAsAccount = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(account: "S-1-5-21-1-2-3-1001")])), "un SID ne tient pas lieu de compte");
        Check.Contains("Compte non explicite", sidAsAccount.Message, "le SID substitué est refusé");
        var duplicate = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles:
            [
                Profile(sid: "S-1-5-21-1-2-3-1001", account: "LAB\\alice", path: "C:\\Users\\alice"),
                Profile(sid: "S-1-5-21-1-2-3-1002", account: "lab\\ALICE", path: "C:\\Users\\alice2"),
            ])), "le même compte deux fois est refusé");
        Check.Contains("Compte en double", duplicate.Message, "le doublon est nommé");
    }

    [Test]
    public void ProfilEntraEstRefuse()
    {
        var entraSid = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(sid: "S-1-12-1-42")])), "un profil Entra est hors périmètre USMT");
        Check.Contains("Entra", entraSid.Message, "le motif est annoncé");
        var entraKind = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [new BackupProfileSelection(
                "S-1-5-21-1-2-3-1001", ProfileIdentityKind.Entra, "LAB\\alice", "C:\\Users\\alice", false,
                [Folder("Contoso\\App")], [Key("Software\\Contoso")])])), "l'identité Entra est refusée même avec un SID ordinaire");
        Check.Contains("Entra", entraKind.Message, "le motif est annoncé");
        var malformed = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(sid: "S-1-5")])), "un SID mal formé est refusé");
        Check.Contains("SID", malformed.Message, "le SID est nommé");
    }

    [Test]
    public void ProfilChargeExigeVss()
    {
        var blocked = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(loaded: true)])), "profil ouvert sans VSS refusé");
        Check.Contains("VSS", blocked.Message, "la raison nomme VSS");
        var plan = Create(Request(profiles: [Profile(loaded: true)], useVss: true));
        Check.True(plan.Warnings.Any(w => w.Contains("VSS", StringComparison.Ordinal)), "l'avertissement annonce le contrôle du moteur");
    }

    [Test]
    public void ChargementInconnuResteInconnu()
    {
        var blocked = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile(loaded: null)])), "chargement inconnu refusé sur le poste réel");
        Check.Contains("inconnu", blocked.Message, "le refus dit que la vérification est impossible");
        var plan = Create(Request(profiles: [Profile(loaded: null)], demonstration: true));
        Check.True(plan.Demonstration, "le plan reste marqué démonstration");
        Check.True(plan.Warnings.Any(w => w.Contains("non établi", StringComparison.Ordinal)), "le chargement non établi est annoncé, pas comblé");
    }

    [Test]
    public void DestinationInvalideEstRefusee()
    {
        foreach (var destination in new[] { "", "   ", "backup\\magasin", "C:backup\\magasin", "C:\\Users\\alice\\magasin", "\\\\serveur\\partage\\..\\autre" })
        {
            var ex = Check.Throws<FilproOperationException>(
                () => Create(Request(destination: destination)), $"destination refusée : « {destination} »");
            Check.Equal(BackupPlan.DestinationCode, ex.Code, "code backup.destination");
        }
        var plan = Create(Request(destination: "\\\\serveur\\partage\\filpro\\2026-10-25"));
        Check.Equal("\\\\serveur\\partage\\filpro\\2026-10-25", plan.Destination, "une destination UNC valide est acceptée et normalisée");
    }

    [Test]
    public void CleDeChiffrementEstBorneeEtJamaisContenue()
    {
        var missing = Check.Throws<FilproOperationException>(
            () => Create(Request(encrypted: true)), "chiffrement sans clé refusé");
        Check.Equal(BackupPlan.DestinationCode, missing.Code, "le refus porte sur l'emplacement");
        var inProfile = Check.Throws<FilproOperationException>(
            () => Create(Request(encrypted: true, keyPath: "C:\\Users\\alice\\cle.key")), "clé dans un profil source refusée");
        Check.Contains("hors des profils sources", inProfile.Message, "la raison est celle de la V1");
        var inStore = Check.Throws<FilproOperationException>(
            () => Create(Request(destination: "\\\\serveur\\partage\\filpro", encrypted: true, keyPath: "\\\\serveur\\partage\\filpro\\cle.key")),
            "clé dans le magasin refusée");
        Check.Contains("hors du dossier de sauvegarde", inStore.Message, "la clé reste hors du magasin");
        var compressed = Check.Throws<FilproOperationException>(
            () => Create(Request(encrypted: true, keyPath: "D:\\cles\\cle.key", noCompress: true)), "chiffrement et non-compression incompatibles");
        Check.Equal(BackupPlan.PlanCode, compressed.Code, "code backup.plan");
        var plan = Create(Request(encrypted: true, keyPath: "D:\\cles\\cle.key"));
        Check.Equal("D:\\cles\\cle.key", plan.KeyPath, "le plan ne porte que le chemin de la clé");
        Check.True(plan.Warnings.Any(w => w.Contains("jamais affichée ni journalisée", StringComparison.Ordinal)), "la clé n'est jamais journalisée");
        var silent = Check.Throws<FilproOperationException>(
            () => Create(Request(keyPath: "D:\\cles\\cle.key")), "un chemin de clé sans chiffrement est refusé");
        Check.Contains("clé", silent.Message, "l'incohérence est annoncée");
    }

    [Test]
    public void TailleResteExplicitementInconnue()
    {
        var plan = Create(Request());
        Check.Equal(BackupSpaceState.Inconnu, plan.Space.State, "le besoin d'espace n'est jamais inventé");
        Check.Null(plan.Space.StoreBytes, "aucun octet de magasin annoncé");
        Check.Null(plan.Space.TempBytes, "aucun octet temporaire annoncé");
        Check.Null(plan.Space.MeasuredAt, "aucune date de mesure sans mesure");
        Check.True(plan.HasUnknownSpace, "le plan déclare sa taille inconnue");
        Check.True(plan.RequiresManualSpaceAcknowledgement, "la confirmation devra porter l'accusé de capacité");
        Check.Contains("Aucun octet n'a été mesuré", plan.Space.Notice, "l'honnêteté de la mesure est annoncée");
        Check.Contains("bloquée", plan.Space.Notice, "la copie reste bloquée sans estimation");
    }

    [Test]
    public void ExclusionsSontRepresenteesEtAnnoncees()
    {
        var declared = new BackupExclusion(BackupExclusionKind.Dossier, "Local\\Temp", "cache temporaire sans valeur", BackupExclusionOrigin.Operateur);
        var plan = Create(Request(exclusions: [declared]));
        Check.True(plan.Exclusions.Any(e => e.Kind == BackupExclusionKind.Dossier && e.Target == "Local\\Temp"), "l'exclusion de l'opérateur est conservée");
        Check.True(plan.Exclusions.Any(e => e.Kind == BackupExclusionKind.FluxNomme && e.Origin == BackupExclusionOrigin.Regle), "les flux nommés sont annoncés");
        Check.True(plan.Exclusions.Any(e => e.Kind == BackupExclusionKind.Application), "les applications non migrées sont annoncées");
        Check.True(plan.Exclusions.Any(e => e.Kind == BackupExclusionKind.Ruche), "les ruches du registre sont annoncées");
        Check.True(plan.Exclusions.Any(e => e.Kind == BackupExclusionKind.DossierVide), "les dossiers vides sont annoncés");
        Check.Contains("flux nommés", plan.Scope, "le périmètre du plan le dit aussi");

        var asRule = Check.Throws<FilproOperationException>(
            () => Create(Request(exclusions: [declared with { Origin = BackupExclusionOrigin.Regle }])), "une demande ne s'invente pas règle");
        Check.Contains("règle", asRule.Message, "le refus est explicite");
        var unjustified = Check.Throws<FilproOperationException>(
            () => Create(Request(exclusions: [declared with { Reason = "  " }])), "une exclusion sans motif est refusée");
        Check.Contains("justifiée", unjustified.Message, "le motif est exigé");
        var unnamed = Check.Throws<FilproOperationException>(
            () => Create(Request(exclusions: [declared with { Target = " " }])), "une exclusion nommée sans cible est refusée");
        Check.Contains("nommée", unnamed.Message, "la cible est exigée");
        var duplicate = Check.Throws<FilproOperationException>(
            () => Create(Request(exclusions: [declared, declared])), "exclusion dupliquée refusée");
        Check.Contains("double", duplicate.Message, "le doublon est nommé");
        var excludedProfile = Check.Throws<FilproOperationException>(
            () => Create(Request(exclusions: [new BackupExclusion(BackupExclusionKind.Profil, "LAB\\alice", "compte d'administration", BackupExclusionOrigin.Operateur)])),
            "un profil exclu ne peut pas être sauvegardé");
        Check.Contains("exclu", excludedProfile.Message, "la contradiction est annoncée");
    }

    [Test]
    public void SelectionEstCanoniseeEtReproductible()
    {
        var first = Create(Request(profiles:
        [
            Profile(sid: "S-1-5-21-1-2-3-1002", account: "LAB\\bob", path: "C:\\Users\\bob"),
            Profile(sid: "S-1-5-21-1-2-3-1001", account: "LAB\\alice", path: "C:\\Users\\alice"),
        ]));
        Check.Equal("LAB\\alice", first.Profiles[0].Account, "les comptes sont ordonnés");
        Check.Equal("LAB\\bob", first.Profiles[1].Account, "l'ordre ne dépend pas de la demande");
        var again = Create(Request(profiles:
        [
            Profile(sid: "S-1-5-21-1-2-3-1001", account: "LAB\\alice", path: "C:\\Users\\alice"),
            Profile(sid: "S-1-5-21-1-2-3-1002", account: "LAB\\bob", path: "C:\\Users\\bob"),
        ]));
        Check.Equal(
            string.Join("|", first.Profiles.Select(p => p.Sid + ";" + string.Join(",", p.Files.Select(f => f.Rel)) + ";" + string.Join(",", p.Registry.Select(r => r.Rel)))),
            string.Join("|", again.Profiles.Select(p => p.Sid + ";" + string.Join(",", p.Files.Select(f => f.Rel)) + ";" + string.Join(",", p.Registry.Select(r => r.Rel)))),
            "la même demande produit le même plan canonisé");
        Check.Equal(BackupPlan.CurrentSchemaVersion, first.SchemaVersion, "la version du contrat est portée par le plan");
        Check.Equal(ProductInfo.Version, first.ProductVersion, "la version du produit est portée par le plan");
        Check.Equal(Created, first.CreatedUtc, "l'horodatage vient de l'appelant");
        Check.Equal(PlanId, first.Id, "l'identifiant vient de l'appelant");
        Check.Contains("aucune écriture", first.Notice, "le plan annonce qu'il n'a rien exécuté");
        Check.Contains("arrêt demandé", first.Notice, "un arrêt demandé n'est pas un arrêt achevé");
    }

    [Test]
    public void ElementsDeSelectionSontBornes()
    {
        var unselected = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile() with { Files = [Folder("Contoso\\App", selected: false)] }])),
            "un élément décoché ne peut pas être sauvegardé");
        Check.Contains("non sélectionné", unselected.Message, "le refus est explicite");
        var escape = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile() with { Files = [Folder("..\\Documents")] }])), "la sortie du profil est refusée");
        Check.Contains("non pris en charge", escape.Message, "le chemin relatif est nommé");
        var absolute = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile() with { Registry = [Key("C:\\Windows")] }])), "une clé absolue est refusée");
        Check.Contains("non pris en charge", absolute.Message, "le chemin relatif est nommé");
        var wrongScope = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile() with { Registry = [new SoftwareItem(SoftwareScope.Roaming, "Software\\Contoso", "x", SoftwareCategory.Autre, true, false, false, null, false, false)] }])),
            "un dossier placé parmi les clés est refusé");
        Check.Contains("Portée inattendue", wrongScope.Message, "la portée est vérifiée");
        var twice = Check.Throws<FilproOperationException>(
            () => Create(Request(profiles: [Profile() with { Files = [Folder("Contoso\\App"), Folder("Contoso\\App/")] }])),
            "le même dossier retenu deux fois est refusé");
        Check.Contains("deux fois", twice.Message, "le doublon est nommé");
        var emptySelectionIsAllowed = Create(Request(profiles: [Profile() with { Files = [], Registry = [] }]));
        Check.Equal(0, emptySelectionIsAllowed.Profiles[0].Files.Count, "sauvegarder un profil sans réglage reste possible");
        Check.Contains("dossiers AppData 0", emptySelectionIsAllowed.Scope, "le périmètre l'annonce");
    }

    [Test]
    public void IdentifiantEtDateDuPlanSontExiges()
    {
        foreach (var id in new[] { "", "   ", "0123", "0123456789abcdef0123456789abcdeZ", "0123456789ABCDEF0123456789ABCDEF" })
        {
            Check.Throws<FilproOperationException>(() => BackupPlan.Create(Request(), id, Created), $"identifiant refusé : « {id} »");
        }
        Check.Throws<FilproOperationException>(
            () => BackupPlan.Create(Request(), PlanId, default), "une date absente est refusée");
    }
}
