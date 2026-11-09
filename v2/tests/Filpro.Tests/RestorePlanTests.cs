using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Contrat et fonction pure du plan de restauration : correspondances de comptes (doublon, fusion, compte non
/// sélectionné), profils cibles (SID, chargement, compte exécutant, hors correspondance), sauvegarde préalable exigée et
/// bornée, anciens magasins reconnus, magasin partiel et chiffré, politiques de conflits réutilisées depuis
/// <see cref="RestoreConflictPolicies"/> et besoin d'espace explicitement inconnu. Aucune E/S, aucun moteur.
/// </summary>
internal sealed class RestorePlanTests
{
    private const string PlanId = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Created = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly SoftwareSizeSummary NoSize = new(SoftwareSizeState.Aucun, null, false, false, "Aucun dossier", 0, 0, 0, "");

    private static RestoreStoreInfo Store(
        bool ready = true,
        bool legacy = false,
        bool partial = false,
        bool simulated = false,
        bool encrypted = false,
        IReadOnlyList<string>? manifest = null,
        string? usmt = "10.0.22621") =>
        new(@"\\serveur\partage\filpro\magasin", ready, legacy, partial, simulated, encrypted,
            legacy ? "Ancien format - à vérifier" : "Completed",
            manifest ?? ["LAB\\alice"], "POSTE-SOURCE", "2026-10-20T08:00:00Z", usmt);

    private static RestoreTargetProfile Target(
        string sid = "S-1-5-21-1-2-3-2001",
        string account = "LAB\\alice",
        string path = "C:\\Users\\alice",
        bool? loaded = false) =>
        new(sid, ProfileIdentityKind.CompteLocalOuDomaine, account, path, loaded);

    private static PriorBackupRequirement Prior(
        PriorBackupState state = PriorBackupState.Identifiee,
        string? root = "D:\\prealable",
        string? key = "D:\\cles\\prealable.key",
        bool verified = false,
        string statement = "") =>
        new(state, root, key, verified, statement);

    private static RestorePlanRequest Request(
        RestoreStoreInfo? store = null,
        IReadOnlyList<string>? users = null,
        IReadOnlyList<RestoreAccountMapping>? mappings = null,
        IReadOnlyList<RestoreTargetProfile>? targets = null,
        IReadOnlyList<RestoreSourceProfile>? sourceProfiles = null,
        IReadOnlyList<SoftwareGroup>? choices = null,
        IReadOnlyList<RestorePolicy>? policies = null,
        PriorBackupRequirement? prior = null,
        string? currentSid = null,
        string? sourceKey = null,
        bool legacyAck = false,
        bool partialAck = false,
        bool assumeEncrypted = false,
        bool demo = false,
        string? purpose = null) =>
        new(store ?? Store(), users ?? ["LAB\\alice"], mappings, targets, sourceProfiles, choices, policies,
            prior, currentSid, sourceKey, legacyAck, partialAck, assumeEncrypted, demo, purpose);

    private static RestorePlanReview Validate(RestorePlanRequest request) => RestorePlan.Validate(request, PlanId, Created);

    private static SoftwareItem Reg(string rel) => new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, false, false, false, null, false, false);
    private static SoftwareGroup Group(string id, string name, string[] registry) =>
        new(id, name, true, [], [.. registry.Select(Reg)], null, 0, "", NoSize);

    [Test]
    public void SelectionVideEstRefusee()
    {
        var empty = Check.Throws<FilproOperationException>(() => Validate(Request(users: [])), "aucun compte n'est accepté");
        Check.Equal(RestorePlan.SelectionCode, empty.Code, "code restore.selection");
        Check.Contains("au moins un compte", empty.Message, "le refus annonce la sélection vide");
        var missing = Check.Throws<FilproOperationException>(() => Validate(Request() with { Users = null! }), "une liste absente est refusée");
        Check.Equal(RestorePlan.SelectionCode, missing.Code, "même code pour une liste absente");
    }

    [Test]
    public void CompteSourceDoitEtreExpliciteEtUnique()
    {
        var bare = Check.Throws<FilproOperationException>(
            () => Validate(Request(users: ["alice"])), "un compte sans domaine est refusé");
        Check.Contains("Compte non explicite", bare.Message, "le compte non résolu est nommé");
        var sidAsAccount = Check.Throws<FilproOperationException>(
            () => Validate(Request(users: ["S-1-5-21-1-2-3-1001"])), "un SID ne tient pas lieu de compte");
        Check.Contains("Compte non explicite", sidAsAccount.Message, "le SID substitué est refusé");
        var duplicate = Check.Throws<FilproOperationException>(
            () => Validate(Request(users: ["LAB\\alice", "lab\\ALICE"], store: Store(manifest: ["LAB\\alice", "lab\\ALICE"]))),
            "le même compte deux fois est refusé");
        Check.Contains("Compte source en double", duplicate.Message, "le doublon est nommé");
    }

    [Test]
    public void CompteSourceAbsentDuManifesteEstRefuse()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(users: ["LAB\\bob"])), "un compte absent du manifeste ne peut pas être restauré");
        Check.Equal(RestorePlan.StoreCode, ex.Code, "code restore.store");
        Check.Contains("absent du manifeste", ex.Message, "le motif est annoncé");
    }

    [Test]
    public void MagasinNonPretEstRefuse()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(ready: false))), "un magasin non prêt est refusé");
        Check.Equal(RestorePlan.StoreCode, ex.Code, "code restore.store");
        Check.Contains("Source absente", ex.Message, "l'état de la source est annoncé");
    }

    [Test]
    public void MelangeSimulationEtReelEstRefuse()
    {
        var realWithFakeStore = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(simulated: true), demo: false)), "magasin de démonstration sur le poste réel");
        Check.Contains("Mélange", realWithFakeStore.Message, "le mélange est refusé");
        var demoWithRealStore = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(simulated: false), demo: true)), "magasin réel en démonstration");
        Check.Contains("Mélange", demoWithRealStore.Message, "le mélange est refusé");
    }

    [Test]
    public void CorrespondanceParDefautEstLeCompteSource()
    {
        var plan = Validate(Request());
        Check.Equal(1, plan.Mappings.Count, "une correspondance par compte sélectionné");
        Check.Equal("LAB\\alice", plan.Mappings[0].Target, "sans correspondance, la cible est le compte source");
        Check.Null(plan.Mappings[0].TargetSid, "aucun profil cible : pas de SID de destination");
        Check.Contains("nouveau profil", plan.IdentityReview, "la revue annonce un nouveau profil");
    }

    [Test]
    public void CorrespondanceVersUnAutreCompteEstAcceptee()
    {
        var plan = Validate(Request(mappings: [new RestoreAccountMapping("LAB\\alice", "LAB\\alice2")]));
        Check.Equal("LAB\\alice2", plan.Mappings[0].Target, "la cible explicite est retenue");
        Check.Equal("LAB\\alice", plan.Mappings[0].Source, "la source est conservée");
    }

    [Test]
    public void CorrespondanceDoubleEstRefusee()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(mappings:
            [
                new RestoreAccountMapping("LAB\\alice", "LAB\\x"),
                new RestoreAccountMapping("LAB\\alice", "LAB\\y"),
            ])), "deux correspondances pour le même source");
        Check.Equal(RestorePlan.MappingCode, ex.Code, "code restore.mapping");
        Check.Contains("en double", ex.Message, "le doublon est nommé");
    }

    [Test]
    public void FusionDeComptesVersUneDestinationEstRefusee()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(
                users: ["LAB\\alice", "LAB\\bob"],
                store: Store(manifest: ["LAB\\alice", "LAB\\bob"]),
                mappings:
                [
                    new RestoreAccountMapping("LAB\\alice", "LAB\\fusion"),
                    new RestoreAccountMapping("LAB\\bob", "LAB\\fusion"),
                ])), "deux comptes ne peuvent pas fusionner vers une même destination");
        Check.Equal(RestorePlan.MappingCode, ex.Code, "code restore.mapping");
        Check.Contains("Fusion", ex.Message, "la fusion est refusée");
    }

    [Test]
    public void CorrespondanceSurCompteNonSelectionneEstRefusee()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(mappings: [new RestoreAccountMapping("LAB\\carol", "LAB\\x")])),
            "une correspondance doit porter sur un compte sélectionné");
        Check.Contains("non sélectionné", ex.Message, "le compte non sélectionné est nommé");
    }

    [Test]
    public void ProfilCibleChargeEstRefuse()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(loaded: true)], prior: Prior())), "profil cible ouvert refusé");
        Check.Equal(RestorePlan.TargetCode, ex.Code, "code restore.target");
        Check.Contains("encore chargé", ex.Message, "la raison est annoncée");
    }

    [Test]
    public void ChargementCibleInconnuResteInconnu()
    {
        var blocked = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(loaded: null)], prior: Prior())), "chargement inconnu refusé sur le poste réel");
        Check.Contains("inconnu", blocked.Message, "le refus dit que la vérification est impossible");
        var plan = Validate(Request(store: Store(simulated: true), targets: [Target(loaded: null)], prior: Prior(key: null), demo: true));
        Check.True(plan.Warnings.Any(w => w.Contains("non établi", StringComparison.Ordinal)), "le chargement non établi est annoncé, pas comblé");
    }

    [Test]
    public void CompteExecutantNePeutPasEtreCible()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(sid: "S-1-5-21-1-2-3-2001")], prior: Prior(), currentSid: "S-1-5-21-1-2-3-2001")),
            "le compte qui exécute FILPRO ne peut pas être une destination");
        Check.Contains("exécute FILPRO", ex.Message, "la raison est annoncée");
    }

    [Test]
    public void IdentiteDeDestinationInvalideEstRefusee()
    {
        var malformed = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(sid: "S-1-5")], prior: Prior())), "un SID de cible mal formé est refusé");
        Check.Contains("Identité de destination", malformed.Message, "le SID est nommé");
        var entra = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [new RestoreTargetProfile("S-1-5-21-1-2-3-2001", ProfileIdentityKind.Entra, "LAB\\alice", "C:\\Users\\alice", false)], prior: Prior())),
            "une cible Entra est refusée");
        Check.Contains("Entra", entra.Message, "le motif est annoncé");
    }

    [Test]
    public void ProfilCibleHorsCorrespondanceEstRefuse()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(account: "LAB\\dave")], prior: Prior())), "une cible doit correspondre à un compte sélectionné");
        Check.Contains("hors correspondance", ex.Message, "la cible inattendue est nommée");
    }

    [Test]
    public void SauvegardePrealableEstExigeeAvantRestauration()
    {
        var missing = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target()], prior: null)), "aucune sauvegarde préalable fournie");
        Check.Equal(RestorePlan.PriorCode, missing.Code, "code restore.prior");
        Check.Contains("sauvegarde préalable", missing.Message, "l'exigence est annoncée");
        var absent = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target()], prior: Prior(state: PriorBackupState.Absente))), "état absent refusé");
        Check.Contains("sauvegarde préalable", absent.Message, "l'absence est refusée");
    }

    [Test]
    public void SauvegardePrealableDoitEtreHorsDuMagasin()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target()], prior: Prior(root: @"\\serveur\partage\filpro\magasin\prealable"))),
            "la sauvegarde préalable ne peut pas être dans le magasin à restaurer");
        Check.Contains("hors du magasin", ex.Message, "la raison est annoncée");
    }

    [Test]
    public void SauvegardePrealableDoitEtreHorsDesProfilsCibles()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target(path: "C:\\Users\\alice")], prior: Prior(root: "C:\\Users\\alice\\prealable"))),
            "la sauvegarde préalable ne peut pas être dans un profil de destination");
        Check.Contains("dans un profil de destination", ex.Message, "la raison est annoncée");
    }

    [Test]
    public void SauvegardePrealableJamaisDeclareeVerifiee()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target()], prior: Prior(verified: true))),
            "le plan ne peut pas affirmer que la sauvegarde préalable est vérifiée");
        Check.Contains("vérifiée", ex.Message, "la fausse vérification est refusée");
        var plan = Validate(Request(targets: [Target()], prior: Prior(state: PriorBackupState.Declaree)));
        Check.False(plan.PriorBackup.Verified, "le plan laisse la vérification au moteur");
        Check.True(plan.Warnings.Any(w => w.Contains("non contrôlée par FILPRO", StringComparison.Ordinal)), "une sauvegarde déclarée est annoncée comme non contrôlée");
    }

    [Test]
    public void ClePrealableEstExigeeHorsDemonstration()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(targets: [Target()], prior: Prior(key: null))), "clé indépendante exigée sur le poste réel");
        Check.Contains("clé indépendante", ex.Message, "la clé est exigée");
        var plan = Validate(Request(store: Store(simulated: true), targets: [Target()], prior: Prior(key: null), demo: true));
        Check.Null(plan.PriorBackup.KeyPath, "en démonstration, la clé peut rester absente");
    }

    [Test]
    public void AncienMagasinExigeReconnaissanceExplicite()
    {
        var blocked = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(legacy: true), legacyAck: false)), "ancien magasin non reconnu");
        Check.Equal(RestorePlan.StoreCode, blocked.Code, "code restore.store");
        Check.Contains("Ancien magasin", blocked.Message, "la reconnaissance est exigée");
        var plan = Validate(Request(store: Store(legacy: true), legacyAck: true));
        Check.True(plan.Legacy, "le plan marque l'ancien magasin");
        Check.True(plan.Warnings.Any(w => w.Contains("Ancien magasin", StringComparison.Ordinal)), "les limites de l'ancien magasin sont annoncées");
    }

    [Test]
    public void AncienMagasinNeFournitPasDeSourceSid()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Validate(Request(
                store: Store(legacy: true),
                legacyAck: true,
                sourceProfiles: [new RestoreSourceProfile("LAB\\alice", "S-1-5-21-1-2-3-1001", "C:\\Users\\alice")])),
            "un ancien magasin n'a pas de SID/chemin source : ne pas emprunter ceux de la cible");
        Check.Contains("ne fournit pas de SID", ex.Message, "l'emprunt est refusé");
    }

    [Test]
    public void MagasinPartielExigeReconnaissance()
    {
        var blocked = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(partial: true), partialAck: false)), "magasin partiel non reconnu");
        Check.Contains("omissions", blocked.Message, "les omissions sont annoncées");
        var plan = Validate(Request(store: Store(partial: true), partialAck: true));
        Check.True(plan.Warnings.Any(w => w.Contains("partiel", StringComparison.Ordinal)), "l'état partiel reconnu est annoncé");
        Check.True(plan.SourcePartial, "l'état partiel lu dans le magasin est porté par le plan, pour la confirmation Windows");
        Check.False(Validate(Request()).SourcePartial, "magasin complet");
    }

    [Test]
    public void MagasinChiffreExigeSaCle()
    {
        var missing = Check.Throws<FilproOperationException>(
            () => Validate(Request(store: Store(encrypted: true))), "chiffrement sans clé source");
        Check.Equal(RestorePlan.StoreCode, missing.Code, "code restore.store");
        Check.Contains("clé", missing.Message, "la clé est exigée");
        var plan = Validate(Request(store: Store(encrypted: true), sourceKey: "D:\\cles\\source.key"));
        Check.Equal("D:\\cles\\source.key", plan.SourceKeyPath, "le plan ne porte que le chemin de la clé");
        Check.True(plan.Warnings.Any(w => w.Contains("jamais affichée ni journalisée", StringComparison.Ordinal)), "la clé n'est jamais journalisée");
        var silent = Check.Throws<FilproOperationException>(
            () => Validate(Request(sourceKey: "D:\\cles\\source.key")), "chemin de clé sans magasin chiffré");
        Check.Contains("sans magasin chiffré", silent.Message, "l'incohérence est annoncée");
    }

    [Test]
    public void PolitiquesDeConflitSontReutilisees()
    {
        var choices = new[] { Group("a", "Alpha", ["Software\\Shared"]), Group("b", "Beta", ["Software\\Shared"]) };
        var conflict = Check.Throws<FilproOperationException>(
            () => Validate(Request(choices: choices, policies:
            [
                new RestorePolicy("a", RestorePolicyMode.UseSaved),
                new RestorePolicy("b", RestorePolicyMode.KeepCurrent),
            ])), "deux logiciels ne peuvent pas imposer des politiques différentes à la même clé");
        Check.Equal(RestoreConflictPolicies.ConflictCode, conflict.Code, "le code conflict.policy vient de RestoreConflictPolicies");

        var defaults = Validate(Request(choices: [Group("a", "Alpha", ["Software\\A"])]));
        Check.Equal(1, defaults.ConflictPolicies.Count, "une décision par groupe affiché");
        Check.True(defaults.ConflictPolicies.All(p => p.Mode == RestorePolicyMode.KeepBoth), "coexistence par défaut");
        Check.Contains("Coexistence", defaults.ConflictSummary, "le résumé vient de RestoreConflictPolicies");
        var lines = defaults.ConflictSummaryLines ?? throw new TestFailureException("lignes du résumé absentes du plan");
        Check.Equal(string.Join("|", RestoreConflictPolicies.SummaryLines([Group("a", "Alpha", ["Software\\A"])], defaults.ConflictPolicies)), string.Join("|", lines),
            "les lignes du plan sont celles de RestoreConflictPolicies");
        Check.Equal(defaults.ConflictSummary, string.Join("\r\n", lines), "le texte de la page joint les mêmes lignes");
    }

    [Test]
    public void TailleResteExplicitementInconnue()
    {
        var plan = Validate(Request());
        Check.True(plan.HasUnknownSpace, "le besoin d'espace n'est jamais inventé");
        Check.True(plan.RequiresManualSpaceAcknowledgement, "la confirmation devra porter l'accusé de capacité");
    }

    [Test]
    public void PlanEstCanoniseEtHonnete()
    {
        var plan = Validate(Request(
            users: ["LAB\\bob", "LAB\\alice"],
            store: Store(manifest: ["LAB\\bob", "LAB\\alice"]),
            mappings: [new RestoreAccountMapping("LAB\\alice", "LAB\\alice2")]));
        Check.Equal("LAB\\alice", plan.Users[0], "les comptes sont ordonnés");
        Check.Equal("LAB\\bob", plan.Users[1], "l'ordre ne dépend pas de la demande");
        Check.Equal("LAB\\alice", plan.Mappings[0].Source, "les correspondances sont ordonnées par source");
        Check.Equal("LAB\\alice2", plan.Mappings[0].Target, "la cible explicite est canonisée");
        Check.Equal("LAB\\bob", plan.Mappings[1].Target, "la cible par défaut est le compte source");
        Check.Contains("→", plan.IdentityReview, "la revue présente les paires source → cible");
        Check.Contains("POSTE-SOURCE", plan.IdentityReview, "le poste source est rappelé");
        Check.Equal(RestorePlan.CurrentSchemaVersion, plan.SchemaVersion, "la version du contrat est portée par le plan");
        Check.Equal(ProductInfo.Version, plan.ProductVersion, "la version du produit est portée par le plan");
        Check.Equal(Created, plan.CreatedUtc, "l'horodatage vient de l'appelant");
        Check.Equal(PlanId, plan.Id, "l'identifiant vient de l'appelant");
        Check.Contains("aucune écriture", plan.Notice, "le plan annonce qu'il n'a rien exécuté");
        Check.Contains("arrêt demandé", plan.Notice, "un arrêt demandé n'est pas un arrêt achevé");
    }

    [Test]
    public void CibleExistantePorteSonSidDansLaRevue()
    {
        var plan = Validate(Request(targets: [Target(sid: "S-1-5-21-1-2-3-2001", account: "LAB\\alice")], prior: Prior()));
        Check.Equal("S-1-5-21-1-2-3-2001", plan.Mappings[0].TargetSid, "le SID de la cible existante est rattaché à la correspondance");
        Check.Contains("S-1-5-21-1-2-3-2001", plan.IdentityReview, "la revue affiche la cible existante");
        Check.Equal(PriorBackupState.Identifiee, plan.PriorBackup.State, "la sauvegarde préalable identifiée est conservée");
    }

    [Test]
    public void IdentifiantEtDateDuPlanSontExiges()
    {
        foreach (var id in new[] { "", "   ", "0123", "0123456789abcdef0123456789abcdeZ", "0123456789ABCDEF0123456789ABCDEF" })
        {
            Check.Throws<FilproOperationException>(() => RestorePlan.Validate(Request(), id, Created), $"identifiant refusé : « {id} »");
        }
        Check.Throws<FilproOperationException>(
            () => RestorePlan.Validate(Request(), PlanId, default), "une date absente est refusée");
    }
}
