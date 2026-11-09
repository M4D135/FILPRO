using Filpro.Bridge;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Fiches de la confirmation Windows, fonctions pures : aucun texte libre de la page, caractères invisibles rendus visibles
/// et signalés, valeurs longues coupées en fin seulement, correspondances complètes, points d'attention, origines, titres
/// et libellés exacts.
/// </summary>
internal sealed class ConfirmationSheetsTests
{
    private static string AllText(ConfirmationSheet sheet) => string.Join("\n",
        new[] { sheet.Title, sheet.ApproveLabel }
            .Concat(sheet.Facts.SelectMany(fact => new[] { fact.Label, fact.Value }))
            .Concat(sheet.Attention.Select(item => item.Text)));

    private static ConfirmationFact Fact(ConfirmationSheet sheet, string label) =>
        sheet.Facts.SingleOrDefault(fact => fact.Label == label) ?? throw new TestFailureException("fait absent : " + label);

    private static ConfirmationAttention? Attention(ConfirmationSheet sheet, string start) =>
        sheet.Attention.SingleOrDefault(item => item.Text.StartsWith(start, StringComparison.Ordinal));

    private static DriveType? Fixed(string root) => DriveType.Fixed;

    private static SoftwareGroup Group(string id, string name) => new(id, name, false, [], [], null, 0, "", ConfirmationSamples.NoSize);

    private static ConfirmationSheet[] AllSheets() =>
    [
        ConfirmationSheets.Backup(ConfirmationSamples.Backup("0123456789abcdef0123456789abcdef"), null, Fixed),
        ConfirmationSheets.Restore(ConfirmationSamples.Restore("0123456789abcdef0123456789abcdef"), Fixed),
        ConfirmationSheets.Repair(ConfirmationSamples.Repair("0123456789abcdef0123456789abcdef"), Fixed),
        ConfirmationSheets.RepairRollback(ConfirmationSamples.Rollback(@"C:\ProgramData\FILPRO\Operations\0123\repair-receipt.json")),
        ConfirmationSheets.Catalog(ConfirmationSamples.Inspection(@"\\nas\sauvegardes\poste-01"), @"E:\cles\magasin.key"),
        ConfirmationSheets.Recovery(ConfirmationSamples.Recovery("0123456789abcdef0123456789abcdef"), Fixed),
        ConfirmationSheets.Defaults(ConfirmationSamples.Defaults("0123456789abcdef0123456789abcdef")),
        ConfirmationSheets.UpdatePrepare(new(ConfirmationSamples.Offer("0123456789abcdef0123456789abcdef"), UpdateSourceKind.Folder, @"\\nas\maj", false), "2.0.8"),
        ConfirmationSheets.UpdateInstall(ConfirmationSamples.Prepared("fedcba9876543210fedcba9876543210"), null),
        ConfirmationSheets.UpdateRollback(ConfirmationSamples.Committed("fedcba9876543210fedcba9876543210")),
    ];

    [Test]
    public void TitresLibellesEtOperationsSontExacts()
    {
        var sheets = AllSheets();
        var expected = new (string Operation, string Title, string Approve)[]
        {
            ("sauvegarder.confirmPlan", "Lancer la sauvegarde de 1 profil(s)", "Lancer la sauvegarde"),
            ("sauvegarder.confirmRestorePlan", "Restaurer 1 profil(s) sur ce poste", "Lancer la restauration"),
            ("analyser.confirmRepair", @"Réparer le profil de POSTE\jdupont", "Lancer la réparation"),
            ("analyser.rollbackRepair", @"Annuler la réparation du profil de POSTE\jdupont", "Lancer le retour arrière"),
            ("recuperer.catalog", "Préparer le catalogue de ce magasin", "Préparer le catalogue"),
            ("recuperer.confirmPlan", "Récupérer 3 fichier(s) dans un nouveau dossier", "Lancer la récupération"),
            ("configuration.applyDefaults", "Écrire les réglages par défaut du pack", "Écrire dans le pack"),
            ("updates.prepare", "Préparer FILPRO 2.0.9", "Préparer cette version"),
            ("updates.install", "Installer FILPRO 2.0.9 puis fermer FILPRO", "Installer et fermer FILPRO"),
            ("updates.rollback", "Revenir à la version précédente puis fermer FILPRO", "Revenir et fermer FILPRO"),
        };
        Check.Equal(expected.Length, sheets.Length, "une fiche par opération privilégiée");
        for (var index = 0; index < expected.Length; index++)
        {
            Check.Equal(expected[index].Operation, sheets[index].Operation, "opération de la fiche");
            Check.Equal(expected[index].Title, sheets[index].Title, expected[index].Operation + " : titre exact");
            Check.Equal(expected[index].Approve, sheets[index].ApproveLabel, expected[index].Operation + " : libellé d'accord exact");
            Check.True(sheets[index].Facts.Count > 0, expected[index].Operation + " : des faits détenus par l'hôte");
            Check.False(sheets[index].Attention.Any(item => item.Text == ConfirmationSheets.HiddenCharactersAttention), expected[index].Operation + " : aucune alerte sans raison");
        }
        Check.Equal(string.Join(",", PrivilegePolicy.Operations.Select(operation => operation.Name).Order(StringComparer.Ordinal)),
            string.Join(",", sheets.Select(sheet => sheet.Operation).Order(StringComparer.Ordinal)), "les fiches couvrent la liste fermée");
        Check.Equal(4, ConfirmationSheets.Backup(ConfirmationSamples.Backup("0123456789abcdef0123456789abcdef") with
        {
            Profiles = [.. Enumerable.Range(1, 4).Select(index => ConfirmationSamples.Profile("S-1-5-21-1-2-3-100" + index, @"POSTE\agent" + index))],
        }, null, _ => DriveType.Fixed).Facts.Count(fact => fact.Label == "Profil"), "un fait par profil");
    }

    [Test]
    public void LEmpreinteLieLaFicheALObjetExecute()
    {
        var plan = ConfirmationSamples.Backup("0123456789abcdef0123456789abcdef");
        Check.Equal(ConfirmedPlanStore.Fingerprint(plan), ConfirmationSheets.Backup(plan, null, _ => DriveType.Fixed).Digest, "empreinte du plan");
        var status = ConfirmationSamples.Prepared("fedcba9876543210fedcba9876543210");
        Check.Equal(ConfirmedPlanStore.Fingerprint(status), ConfirmationSheets.UpdateInstall(status, null).Digest, "empreinte de l'état relu");
        var offer = ConfirmationSamples.Offer("0123456789abcdef0123456789abcdef");
        Check.Equal(ConfirmedPlanStore.Fingerprint(offer), ConfirmationSheets.UpdatePrepare(new(offer, UpdateSourceKind.Folder, @"\\nas\maj", false), "2.0.8").Digest,
            "empreinte de l'offre");
        var inspection = ConfirmationSamples.Inspection(@"\\nas\sauvegardes\poste-01");
        Check.Equal(ConfirmedPlanStore.Fingerprint(inspection), ConfirmationSheets.Catalog(inspection, null).Digest, "empreinte du magasin inspecté");
        Check.False(ConfirmationSheets.Backup(plan with { Destination = @"D:\Autre" }, null, _ => DriveType.Fixed).Digest
            == ConfirmationSheets.Backup(plan, null, _ => DriveType.Fixed).Digest, "un autre plan donne une autre empreinte");
    }

    [Test]
    public void ObjetEtDeclarationDeLaPageNApparaissentJamais()
    {
        const string Purpose = "OBJET-LIBRE-DE-LA-PAGE";
        const string Statement = "DECLARATION-LIBRE-DE-LA-PAGE";
        var backup = ConfirmationSheets.Backup(ConfirmationSamples.Backup("0123456789abcdef0123456789abcdef") with { Purpose = Purpose }, null, _ => DriveType.Fixed);
        var restorePlan = ConfirmationSamples.Restore("0123456789abcdef0123456789abcdef");
        var restore = ConfirmationSheets.Restore(restorePlan with { Purpose = Purpose, PriorBackup = restorePlan.PriorBackup with { Statement = Statement } }, Fixed);
        foreach (var sheet in new[] { backup, restore })
        {
            Check.False(AllText(sheet).Contains(Purpose, StringComparison.Ordinal), sheet.Operation + " : l'objet saisi dans la page n'est jamais montré");
            Check.False(AllText(sheet).Contains(Statement, StringComparison.Ordinal), sheet.Operation + " : la déclaration saisie n'est jamais montrée");
        }
        Check.Equal(@"C:\Prealable (disque local)", Fact(restore, "Sauvegarde préalable").Value, "seul l'emplacement reçu est montré, avec sa nature");
        Check.Equal(FactOrigin.Interface, Fact(restore, "Sauvegarde préalable").Origin, "emplacement reçu de la page");
    }

    [Test]
    public void CaracteresInvisiblesRendusVisiblesEtSignales()
    {
        var (text, flagged) = NativeText.Display("a\u202Eb\u200Bc\r\nd\u0085e");
        Check.Equal("a⟨U+202E⟩b⟨U+200B⟩c⟨U+000D⟩⟨U+000A⟩d⟨U+0085⟩e", text, "direction, largeur nulle, CR, LF et C1 visibles");
        Check.True(flagged, "valeur signalée");
        Check.Equal("⟨U+2066⟩⟨U+FEFF⟩⟨U+00AD⟩⟨U+061C⟩⟨U+2028⟩⟨U+007F⟩⟨U+E0001⟩⟨U+D800⟩x", NativeText.Display("\u2066\uFEFF\u00AD\u061C\u2028\u007F\U000E0001\uD800x").Text,
            "isolat, BOM, format, séparateur de ligne, DEL, étiquette hors BMP et demi-paire isolée visibles");
        Check.Equal((@"C:\Users\Zoé — 😀 dossier", false), NativeText.Display(@"C:\Users\Zoé — 😀 dossier"), "texte ordinaire inchangé, non signalé");
        Check.Equal((string.Empty, false), NativeText.Display(null), "valeur absente");
        var plan = ConfirmationSamples.Repair("0123456789abcdef0123456789abcdef") with { Account = "POSTE\\jdupont\u202Etxt.exe", Notice = "ligne 1\nligne 2" };
        var sheet = ConfirmationSheets.Repair(plan, Fixed);
        Check.Contains("⟨U+202E⟩", sheet.Title, "le titre ne cache aucun caractère");
        Check.Equal("ligne 1⟨U+000A⟩ligne 2", Fact(sheet, "Remarque").Value, "une valeur n'impose jamais ses propres lignes");
        var hidden = sheet.Attention.Where(item => item.Text == ConfirmationSheets.HiddenCharactersAttention).ToArray();
        Check.Equal(1, hidden.Length, "signalé une seule fois pour toute la fiche");
        Check.True(hidden[0].Severe, "signalement grave");
        Check.Equal("Caractères invisibles ou de contrôle signalés dans une valeur.", ConfirmationSheets.HiddenCharactersAttention, "texte exact");
        var offer = ConfirmationSamples.Offer("0123456789abcdef0123456789abcdef") with { FileName = "FILPRO-2.0.9\u200B-win-x64-PROD.zip" };
        Check.True(ConfirmationSheets.UpdatePrepare(new(offer, UpdateSourceKind.Folder, @"\\nas\maj", false), "2.0.8").Attention
            .Any(item => item.Severe && item.Text == ConfirmationSheets.HiddenCharactersAttention), "toute valeur est contrôlée, pas seulement les comptes");
    }

    [Test]
    public void ValeurLongueCoupeeEnFinSeulement()
    {
        var exact = new string('x', NativeText.MaximumLength);
        Check.Equal((exact, false), NativeText.Display(exact), "2 000 caractères ne sont pas coupés");
        var (text, flagged) = NativeText.Display(new string('a', NativeText.MaximumLength - 1) + "😀" + new string('b', 3000));
        Check.True(text.StartsWith(new string('a', NativeText.MaximumLength - 1) + "… (", StringComparison.Ordinal), "le début est conservé, la paire n'est pas coupée");
        Check.True(text.EndsWith("… (5000 caractères)", StringComparison.Ordinal), "la longueur d'origine est annoncée");
        Check.False(text.Any(char.IsSurrogate), "aucune demi-paire de substitution");
        Check.False(flagged, "une coupe n'est pas un caractère caché");
        var control = NativeText.Display(new string('x', NativeText.MaximumLength - 4) + "\u0007yyyy");
        Check.Equal(new string('x', NativeText.MaximumLength - 4) + "… (2001 caractères)", control.Text, "jamais au milieu d'un ⟨U+XXXX⟩");
        Check.True(control.Flagged, "le caractère coupé reste signalé");
        Check.True(NativeText.Display(new string('x', 2500) + "\u202E").Flagged, "un caractère caché après la coupe est signalé");
        var fact = Fact(ConfirmationSheets.Repair(ConfirmationSamples.Repair("0123456789abcdef0123456789abcdef") with { ProfilePath = @"C:\" + new string('p', 5000) }, Fixed),
            "Dossier du profil");
        Check.True(fact.Value.StartsWith(@"C:\ppp", StringComparison.Ordinal) && fact.Value.EndsWith("… (5003 caractères)", StringComparison.Ordinal),
            "un chemin long est coupé à la fin, son début reste lisible");
    }

    [Test]
    public void ToutesLesCorrespondancesSontListees()
    {
        var plan = ConfirmationSamples.Restore("0123456789abcdef0123456789abcdef");
        plan = plan with
        {
            IdentityPairs =
            [
                new(@"ANCIEN\alice", null, null, @"POSTE\alice", "S-1-5-21-1-2-3-1101"),
                new(@"ANCIEN\bruno", null, null, @"POSTE\bruno", "S-1-5-21-1-2-3-1102"),
                new(@"ANCIEN\chloe", null, null, @"POSTE\chloe", null),
            ],
            Targets =
            [
                new("S-1-5-21-1-2-3-1101", ProfileIdentityKind.CompteLocalOuDomaine, @"POSTE\alice", @"C:\Users\alice", false),
                new("S-1-5-21-1-2-3-1102", ProfileIdentityKind.CompteLocalOuDomaine, @"POSTE\bruno", @"D:\Profils\bruno", false),
            ],
        };
        var sheet = ConfirmationSheets.Restore(plan, Fixed);
        var mappings = sheet.Facts.Where(fact => fact.Label == "Correspondance").ToArray();
        Check.Equal(3, mappings.Length, "une ligne par correspondance");
        Check.Equal("ANCIEN\\alice → POSTE\\alice\nC:\\Users\\alice", mappings[0].Value, "compte source, compte cible et dossier du profil cible");
        Check.Equal("ANCIEN\\bruno → POSTE\\bruno\nD:\\Profils\\bruno", mappings[1].Value, "dossier relu dans les cibles par SID");
        Check.Equal("ANCIEN\\chloe → POSTE\\chloe\nnouveau profil", mappings[2].Value, "cible sans profil existant");
        Check.True(mappings.All(fact => fact.Origin == FactOrigin.Filpro), "correspondances retenues par le plan relu");
        Check.Equal("Restaurer 3 profil(s) sur ce poste", sheet.Title, "titre du nombre de profils");
        Check.Equal("Écrit des fichiers et des clés HKCU dans le profil de POSTE\\alice, POSTE\\bruno, POSTE\\chloe : ils seront utilisés à leur prochaine ouverture de session.",
            Attention(sheet, "Écrit des fichiers")?.Text, "chaque compte cible est annoncé");
        Check.Equal("ancien format, chiffré", Fact(ConfirmationSheets.Restore(plan with { Legacy = true, Encrypted = true, SourceKeyPath = @"E:\cle.key" }, Fixed),
            "État du magasin").Value, "état du magasin");
        Check.Equal(FactOrigin.Store, Fact(sheet, "Ordinateur source").Origin, "déclaré par le manifeste");
        Check.Equal(FactOrigin.Interface, Fact(sheet, "Magasin").Origin, "chemin reçu de la page");
    }

    [Test]
    public void PointsDAttentionDeLaSauvegarde()
    {
        var plan = ConfirmationSamples.Backup("0123456789abcdef0123456789abcdef");
        var network = ConfirmationSheets.Backup(plan with { Destination = @"\\nas\sauvegardes" }, null, _ => throw new InvalidOperationException("aucun lecteur pour un UNC"));
        Check.True(Attention(network, "Destination sur un partage réseau : les données quittent ce poste.")?.Severe == true, "partage UNC : grave");
        Check.Equal(@"\\nas\sauvegardes (partage réseau)", Fact(network, "Destination").Value, "nature de l'emplacement");
        Check.Equal(FactOrigin.Interface, Fact(network, "Destination").Origin, "destination reçue de la page");
        var mapped = ConfirmationSheets.Backup(plan with { Destination = @"Z:\Sauvegardes" }, null, _ => DriveType.Network);
        Check.True(Attention(mapped, "Destination sur un partage réseau")?.Severe == true, "lecteur réseau : grave");
        var removable = ConfirmationSheets.Backup(plan with { Destination = @"E:\Sauvegardes" }, null, _ => DriveType.Removable);
        Check.True(Attention(removable, "Destination sur un support amovible.") is { Severe: false }, "support amovible signalé");
        Check.Null(Attention(removable, "Destination sur un partage réseau"), "pas de réseau pour un support amovible");
        var local = ConfirmationSheets.Backup(plan, null, _ => DriveType.Fixed);
        Check.Null(Attention(local, "Destination sur"), "disque local : aucune alerte d'emplacement");
        Check.Equal(@"C:\Sauvegardes (disque local)", Fact(local, "Destination").Value, "disque local nommé");
        var unknown = ConfirmationSheets.Backup(plan, null, _ => DriveType.CDRom);
        Check.Equal(@"C:\Sauvegardes (lecteur de type inconnu)", Fact(unknown, "Destination").Value, "jamais « disque local » sans le savoir");
        Check.True(Attention(unknown, "Destination sur un lecteur de type inconnu : FILPRO ne peut pas dire s’il est local, amovible ou réseau.")?.Severe == true,
            "type de lecteur inconnu : grave");
        var absent = Check.Throws<FilproOperationException>(() => ConfirmationSheets.Backup(plan with { Destination = @"z:\Sauvegardes" }, null,
            _ => DriveType.NoRootDirectory), "lettre de lecteur absente pour FILPRO élevé");
        Check.Equal("destination.absent", absent.Code, "refusée avant toute fenêtre");
        Check.Equal(@"Lecteur Z: introuvable pour FILPRO élevé : un lecteur réseau connecté dans la session n’est pas visible ici ; indiquer le chemin UNC \\serveur\partage.",
            absent.Message, "message français exact");
        Check.True(Attention(local, "Sauvegarde non chiffrée.") is not null, "absence de chiffrement signalée");
        Check.Equal("non", Fact(local, "Chiffrement").Value, "chiffrement non");
        var encrypted = ConfirmationSheets.Backup(plan with { Encrypted = true, KeyPath = @"E:\cles\poste.key", ContinueOnError = true }, null, _ => DriveType.Fixed);
        Check.Null(Attention(encrypted, "Sauvegarde non chiffrée."), "sauvegarde chiffrée");
        Check.Equal(@"oui — clé E:\cles\poste.key", Fact(encrypted, "Chiffrement").Value, "chemin de la clé, jamais son contenu");
        Check.Equal(FactOrigin.Interface, Fact(encrypted, "Chiffrement").Origin, "clé choisie dans la page");
        Check.True(Attention(encrypted, "Poursuite sur erreur : des éléments peuvent manquer sans arrêter la sauvegarde.") is not null, "poursuite sur erreur signalée");
        Check.Equal("oui", Fact(encrypted, "Poursuite sur erreur").Value, "poursuite sur erreur");
        var host = new HostSituation(SessionSituation.SameAccount, ConfirmationSamples.UserSid.ToLowerInvariant(), @"DOMAINE\tech", null, null);
        Check.Equal(@"Le profil du compte élevé (DOMAINE\tech) fait partie de la sauvegarde.",
            Attention(ConfirmationSheets.Backup(plan, host, _ => DriveType.Fixed), "Le profil du compte élevé")?.Text, "profil du compte élevé signalé");
        Check.Null(Attention(ConfirmationSheets.Backup(plan, host with { HostSid = ConfirmationSamples.HostSid }, _ => DriveType.Fixed), "Le profil du compte élevé"),
            "autre compte élevé : rien à signaler");
        Check.Equal("POSTE\\jdupont — 2 élément(s) AppData, 1 clé(s) HKCU\nC:\\Users\\jdupont", Fact(local, "Profil").Value, "profil, réglages retenus et dossier");
        Check.Equal("0123456789abcdef0123456789abcdef — créé le 2026-10-29 08:30 UTC", Fact(local, "Plan").Value, "identifiant et date du plan");
        Check.Equal("oui", Fact(local, "Compression").Value, "compression active");
    }

    [Test]
    public void PointsDAttentionDeLaRestauration()
    {
        var plan = ConfirmationSamples.Restore("0123456789abcdef0123456789abcdef");
        var current = ConfirmationSheets.Restore(plan, Fixed);
        Check.Null(Attention(current, "Manifeste du magasin non relu"), "manifeste relu");
        Check.Equal(ConfirmationSamples.Manifest, Fact(current, "Empreinte du manifeste").Value, "empreinte du manifeste relu");
        Check.Null(Attention(current, "Magasin d’un ancien format"), "magasin récent");
        Check.Null(Attention(current, "Magasin partiel"), "magasin complet");
        Check.Equal("format FILPRO, non chiffré", Fact(current, "État du magasin").Value, "état d'un magasin complet");
        Check.False(current.Attention.Any(item => item.Text.Contains("utiliser la sauvegarde", StringComparison.Ordinal)), "aucune décision « utiliser la sauvegarde »");
        var partial = ConfirmationSheets.Restore(plan with { SourcePartial = true }, Fixed);
        Check.Equal("format FILPRO, partiel, non chiffré", Fact(partial, "État du magasin").Value, "magasin partiel lu par FILPRO");
        Check.True(Attention(partial, "Magasin partiel : des éléments n’ont pas été capturés et ne seront pas restaurés ; examiner ses journaux.") is not null,
            "omissions du magasin annoncées, quelle que soit la case cochée dans la page");
        var risky = ConfirmationSheets.Restore(plan with
        {
            SourceManifestSha256 = null,
            Legacy = true,
            ConflictPolicies = [new("known:vlc", RestorePolicyMode.UseSaved), new("known:office", RestorePolicyMode.UseSaved), new("known:chrome", RestorePolicyMode.KeepCurrent)],
        }, Fixed);
        Check.True(Attention(risky, "Manifeste du magasin non relu par FILPRO.")?.Severe == true, "manifeste non relu : grave");
        Check.Equal("2 décision(s) « utiliser la sauvegarde » : les données courantes concernées seront remplacées.",
            Attention(risky, "2 décision(s)")?.Text, "nombre de décisions « utiliser la sauvegarde »");
        Check.True(Attention(risky, "Magasin d’un ancien format : contrôles du contenu plus limités.") is not null, "ancien format signalé");
        Check.False(risky.Facts.Any(fact => fact.Label == "Empreinte du manifeste"), "aucune empreinte inventée");
        var key = ConfirmationSheets.Restore(plan with { Encrypted = true, SourceKeyPath = @"E:\cles\magasin.key" }, Fixed);
        Check.Equal(@"E:\cles\magasin.key", Fact(key, "Clé du magasin").Value, "clé du magasin chiffré");
        Check.Equal(FactOrigin.Interface, Fact(key, "Clé du magasin").Origin, "clé choisie dans la page");
    }

    [Test]
    public void LesConflitsSontMontresLigneALigne()
    {
        var plan = ConfirmationSamples.Restore("0123456789abcdef0123456789abcdef");
        IReadOnlyList<SoftwareGroup> groups = [ConfirmationSamples.Vlc, Group(@"registry|Software\Alpha", "Alpha")];
        IReadOnlyList<RestorePolicy> policies = [new("known:vlc", RestorePolicyMode.KeepBoth), new(@"registry|Software\Alpha", RestorePolicyMode.UseSaved)];
        var lines = RestoreConflictPolicies.SummaryLines(groups, policies);
        Check.Equal(RestoreConflictPolicies.Summarize(groups, policies), string.Join("\r\n", lines), "le texte de la page joint les mêmes lignes par CR LF");
        var sheet = ConfirmationSheets.Restore(plan with
        {
            ConflictPolicies = policies,
            ConflictSummary = RestoreConflictPolicies.Summarize(groups, policies),
            ConflictSummaryLines = lines,
        }, Fixed);
        var conflicts = Fact(sheet, "Conflits");
        Check.Equal(string.Join("\n", lines), conflicts.Value, "une ligne de la fenêtre par ligne du résumé réel");
        Check.Equal(3, conflicts.Value.Split('\n').Length, "deux sortes de décision puis la validation restante");
        Check.False(conflicts.Value.Contains("⟨U+", StringComparison.Ordinal), "aucune fin de ligne rendue en ⟨U+000D⟩⟨U+000A⟩");
        Check.False(sheet.Attention.Any(item => item.Text == ConfirmationSheets.HiddenCharactersAttention), "aucune alerte sans raison");
        Check.Equal(FactOrigin.Store, conflicts.Origin, "un groupe hors catalogue porte un nom déclaré par le magasin");
        IReadOnlyList<SoftwareGroup> catalogued = [ConfirmationSamples.Vlc];
        IReadOnlyList<RestorePolicy> cataloguedPolicies = [new("known:vlc", RestorePolicyMode.KeepBoth)];
        var known = Fact(ConfirmationSheets.Restore(plan with
        {
            ConflictPolicies = cataloguedPolicies,
            ConflictSummary = RestoreConflictPolicies.Summarize(catalogued, cataloguedPolicies),
            ConflictSummaryLines = RestoreConflictPolicies.SummaryLines(catalogued, cataloguedPolicies),
        }, Fixed), "Conflits");
        Check.Equal(FactOrigin.Filpro, known.Origin, "résumé composé par FILPRO avec les seuls noms du catalogue");

        // Noms de groupes déclarés par le manifeste du magasin : direction inversée et fin de ligne restent visibles et signalées.
        IReadOnlyList<SoftwareGroup> crafted = [Group("files|Roaming|A", "Alpha\u202Etxt.exe"), Group("files|Roaming|B", "Beta\r\nValidation utilisateur restante : aucune")];
        IReadOnlyList<RestorePolicy> keep = [new("files|Roaming|A", RestorePolicyMode.KeepBoth), new("files|Roaming|B", RestorePolicyMode.KeepBoth)];
        var hidden = ConfirmationSheets.Restore(plan with
        {
            ConflictPolicies = keep,
            ConflictSummary = RestoreConflictPolicies.Summarize(crafted, keep),
            ConflictSummaryLines = RestoreConflictPolicies.SummaryLines(crafted, keep),
        }, Fixed);
        var value = Fact(hidden, "Conflits").Value;
        Check.Equal(2, value.Split('\n').Length, "une fin de ligne dans un nom n'ajoute aucune ligne à la fenêtre");
        Check.Contains("Alpha⟨U+202E⟩txt.exe", value, "caractère de direction rendu visible");
        Check.Contains("Beta⟨U+000D⟩⟨U+000A⟩Validation utilisateur restante : aucune", value, "fin de ligne d'un nom rendue visible");
        Check.True(hidden.Attention.Any(item => item.Severe && item.Text == ConfirmationSheets.HiddenCharactersAttention), "nom suspect signalé");
        Check.Equal("Aucun réglage spécifique à restaurer.", Fact(ConfirmationSheets.Restore(plan with
        {
            ConflictSummary = "Aucun réglage spécifique à restaurer.",
            ConflictSummaryLines = null,
        }, Fixed), "Conflits").Value, "plan sans lignes : le résumé est montré tel quel");
    }

    [Test]
    public void EmpreinteSaisieEtProvenanceAbsenteSontGraves()
    {
        var offer = ConfirmationSamples.Offer("0123456789abcdef0123456789abcdef");
        var typed = ConfirmationSheets.UpdatePrepare(new(offer, UpdateSourceKind.ManualZip, @"D:\Mises a jour\FILPRO-2.0.9-win-x64-PROD.zip", true), "2.0.8");
        Check.True(Attention(typed, "SHA-256 saisi dans l’interface : il ne prouve pas l’origine du pack.")?.Severe == true, "empreinte saisie : grave");
        Check.Equal(FactOrigin.Interface, Fact(typed, "SHA-256").Origin, "empreinte reçue de la page");
        Check.Equal("saisie dans l’interface", Fact(typed, "Origine de l’empreinte").Value, "origine de l'empreinte");
        Check.Equal(@"ZIP D:\Mises a jour\FILPRO-2.0.9-win-x64-PROD.zip", Fact(typed, "Source").Value, "ZIP choisi");
        Check.True(Attention(typed, "Aucune signature d’éditeur : seule l’empreinte SHA-256 est contrôlée.") is not null, "aucune signature d'éditeur");
        Check.Equal("01234567 89abcdef 01234567 89abcdef 01234567 89abcdef 01234567 89abcdef", Fact(typed, "SHA-256").Value, "empreinte en 8 groupes de 8");
        Check.Equal("2.0.8 → 2.0.9", Fact(typed, "Versions").Value, "versions");
        Check.Equal("FILPRO-2.0.9-win-x64-PROD.zip — 117,7 Mo", Fact(typed, "Fichier").Value, "nom et taille en français");
        var folder = ConfirmationSheets.UpdatePrepare(new(offer, UpdateSourceKind.Folder, @"\\nas\maj", false), "2.0.8");
        Check.Null(Attention(folder, "SHA-256 saisi"), "empreinte lue dans le dossier");
        Check.Equal(@"Dossier \\nas\maj", Fact(folder, "Source").Value, "dossier choisi");
        Check.Equal(FactOrigin.Interface, Fact(folder, "Source").Origin, "dossier reçu de la page");
        Check.Equal("SHA256SUMS du dossier", Fact(folder, "Origine de l’empreinte").Value, "somme du dossier");
        Check.False(folder.Attention.Concat(typed.Attention).Any(item => item.Text.Contains("GitHub", StringComparison.Ordinal)), "aucune source GitHub évoquée");
        var status = ConfirmationSamples.Prepared("fedcba9876543210fedcba9876543210");
        var unknown = ConfirmationSheets.UpdateInstall(status, null);
        Check.True(Attention(unknown, "Provenance non conservée par cette session de FILPRO : non vérifiable.")?.Severe == true, "provenance absente : grave");
        Check.False(unknown.Facts.Any(fact => fact.Label == "Provenance"), "aucune provenance inventée");
        Check.True(Attention(unknown, "FILPRO se ferme ; la version précédente est conservée ; le nouveau démarrage est contrôlé avant validation.") is not null,
            "fermeture annoncée");
        var known = ConfirmationSheets.UpdateInstall(status, new(offer, UpdateSourceKind.Folder, @"\\nas\maj", false));
        Check.Null(Attention(known, "Provenance non conservée"), "provenance connue");
        Check.Equal(@"Dossier \\nas\maj · empreinte : SHA256SUMS du dossier", Fact(known, "Provenance").Value, "provenance de la préparation");
        Check.Equal(FactOrigin.Interface, Fact(known, "Provenance").Origin, "dossier reçu de la page");
        Check.Equal("fedcba9876543210fedcba9876543210", Fact(known, "Transaction").Value, "transaction");
        var rollback = ConfirmationSheets.UpdateRollback(ConfirmationSamples.Committed("fedcba9876543210fedcba9876543210"));
        Check.Equal("2.0.9 → 2.0.8", Fact(rollback, "Versions").Value, "version conservée par la transaction");
        Check.Equal("2.0.9 → version conservée", Fact(ConfirmationSheets.UpdateRollback(ConfirmationSamples.Committed("fedcba9876543210fedcba9876543210")
            with { PreviousVersion = null }), "Versions").Value, "version précédente inconnue");
        Check.True(Attention(rollback, "FILPRO se ferme ; le retour arrière est contrôlé au nouveau démarrage.") is not null, "fermeture annoncée");
    }

    [Test]
    public void ReglagesParDefautDuPack()
    {
        var sheet = ConfirmationSheets.Defaults(ConfirmationSamples.Defaults("0123456789abcdef0123456789abcdef"));
        Check.Equal(@"wad\config\deployment-defaults.json de ce pack", Fact(sheet, "Fichier").Value, "fichier écrit dans le pack");
        Check.Equal("encrypted : False,conflictPolicy : useSaved,continueOnError : True",
            string.Join(",", sheet.Facts.Where(fact => fact.Label == "Réglage").Select(fact => fact.Value)), "un réglage par valeur écrite");
        Check.True(Attention(sheet, "Tous les futurs utilisateurs de ce pack recevront ces réglages.") is not null, "diffusion annoncée");
        Check.True(Attention(sheet, "Le chiffrement ne sera plus proposé par défaut.")?.Severe == true, "chiffrement retiré : grave");
        Check.True(Attention(sheet, "Les conflits seront résolus par défaut avec la version sauvegardée.") is not null, "useSaved signalé");
        Check.True(Attention(sheet, "La poursuite sur erreur sera activée par défaut.") is not null, "poursuite sur erreur signalée");
        var safe = ConfirmationSheets.Defaults(ConfirmationSamples.Defaults("0123456789abcdef0123456789abcdef") with
        {
            Changes = [new("encrypted", "False", "True", "replace"), new("conflictPolicy", "useSaved", "keepBoth", "replace"), new("continueOnError", "True", "False", "replace")],
            PackIdentity = "pack-2.0.8",
        });
        Check.Equal(1, safe.Attention.Count, "seule la diffusion est annoncée");
        Check.Equal("encrypted : False → True", safe.Facts.First(fact => fact.Label == "Réglage").Value, "un changement réel garde l'avant et l'après");
        Check.Equal("wad\\config\\deployment-defaults.json de ce pack\nIdentité du pack : pack-2.0.8", Fact(safe, "Fichier").Value, "identité du pack");
    }

    [Test]
    public void RecuperationTaillesEtPremiersFichiers()
    {
        var plan = ConfirmationSamples.Recovery("0123456789abcdef0123456789abcdef", 12);
        var sized = plan with { Entries = [.. plan.Entries.Select((entry, index) => entry with { Bytes = index < 10 ? 1_300_234 : null })] };
        var sheet = ConfirmationSheets.Recovery(sized, Fixed);
        Check.Equal("12 fichier(s), au moins 12,4 Mo (taille inconnue pour 2 fichier(s))", Fact(sheet, "Fichiers").Value, "taille française, tailles inconnues dites");
        var first = Fact(sheet, "Premiers fichiers").Value.Split('\n');
        Check.Equal(11, first.Length, "dix chemins puis le reste");
        Check.Equal(@"Users\jdupont\Documents\fichier-1.docx", first[0], "chemin relatif du magasin");
        Check.Equal("… et 2 autres", first[10], "nombre des autres fichiers");
        Check.Equal(FactOrigin.Store, Fact(sheet, "Premiers fichiers").Origin, "chemins déclarés par le magasin");
        Check.Equal("12 fichier(s), 12,4 Mo", Fact(ConfirmationSheets.Recovery(sized with
        {
            Entries = [.. sized.Entries.Select(entry => entry with { Bytes = 1_083_528 })],
        }, Fixed), "Fichiers").Value, "toutes les tailles connues");
        Check.Equal("3 fichier(s), taille inconnue", Fact(ConfirmationSheets.Recovery(ConfirmationSamples.Recovery("0123456789abcdef0123456789abcdef") with
        {
            Entries = [.. ConfirmationSamples.Recovery("0123456789abcdef0123456789abcdef").Entries.Select(entry => entry with { Bytes = null })],
        }, Fixed), "Fichiers").Value, "aucune taille connue");
        Check.Equal(@"C:\Recuperation\FILPRO-recuperation-01234567 (disque local)", Fact(sheet, "Dossier de destination").Value, "nouveau dossier complet et sa nature");
        Check.Equal(FactOrigin.Interface, Fact(sheet, "Dossier de destination").Origin, "emplacement reçu de la page");
        Check.Equal("non", Fact(sheet, "Chiffrement").Value, "magasin non chiffré");
        Check.Null(Attention(sheet, "Les fichiers récupérés sont écrits en clair."), "rien à déchiffrer");
        Check.Equal(FactOrigin.Filpro, Fact(sheet, "Avertissements").Origin, "phrases fixes du moteur, sans texte du manifeste");
        var encrypted = ConfirmationSheets.Recovery(plan with { Encrypted = true, KeyPath = @"E:\cles\magasin.key" }, Fixed);
        Check.Equal(@"E:\cles\magasin.key", Fact(encrypted, "Clé de déchiffrement du magasin").Value, "clé du magasin chiffré");
        Check.Equal(FactOrigin.Interface, Fact(encrypted, "Clé de déchiffrement du magasin").Origin, "clé choisie dans la page");
        Check.False(encrypted.Facts.Any(fact => fact.Label == "Chiffrement"), "la clé n'est pas présentée comme un chiffrement des fichiers récupérés");
        Check.True(Attention(encrypted, "Les fichiers récupérés sont écrits en clair.") is not null, "fichiers déchiffrés à l'écriture");
    }

    [Test]
    public void EmplacementsEcritsParLaRestaurationLaReparationEtLaRecuperation()
    {
        // Sous-dossier d'un partage : la racine nue d'un partage est refusée par StoragePathPolicy.NormalizeWindows.
        const string Share = @"\\nas\partage\x";
        static DriveType? NoDrive(string root) => throw new InvalidOperationException("aucun lecteur pour un UNC : " + root);
        const string Id = "0123456789abcdef0123456789abcdef";

        var restorePlan = ConfirmationSamples.Restore(Id);
        var restore = ConfirmationSheets.Restore(restorePlan with { PriorBackup = restorePlan.PriorBackup with { Root = Share } }, NoDrive);
        Check.Equal(Share + " (partage réseau)", Fact(restore, "Sauvegarde préalable").Value, "sauvegarde préalable et sa nature");
        Check.Equal(FactOrigin.Interface, Fact(restore, "Sauvegarde préalable").Origin, "emplacement reçu de la page");
        Check.True(Attention(restore, "Sauvegarde préalable sur un partage réseau : les données des profils cibles quittent ce poste.")?.Severe == true,
            "partage réseau : grave");
        Check.Equal(@"C:\Cles\prealable.key", Fact(restore, "Clé de la sauvegarde préalable").Value, "clé de la sauvegarde préalable affichée");
        Check.Equal(FactOrigin.Interface, Fact(restore, "Clé de la sauvegarde préalable").Origin, "clé choisie dans la page");
        Check.True(Attention(restore, "FILPRO écrit d’abord dans ce dossier une sauvegarde des profils cibles, chiffrée avec cette clé.") is { Severe: false },
            "ce que le moteur écrit d'abord est dit");
        Check.Null(Attention(restore, "Sauvegarde préalable non chiffrée"), "sauvegarde préalable chiffrée");
        Check.False(AllText(restore).Contains("contrôlée par le moteur", StringComparison.Ordinal), "aucune affirmation de contrôle de l'emplacement");
        var plain = ConfirmationSheets.Restore(restorePlan with { PriorBackup = restorePlan.PriorBackup with { KeyPath = null } }, Fixed);
        Check.Equal("aucune", Fact(plain, "Clé de la sauvegarde préalable").Value, "aucune clé");
        Check.Equal(FactOrigin.Filpro, Fact(plain, "Clé de la sauvegarde préalable").Origin, "absence constatée par FILPRO");
        Check.True(Attention(plain, "Sauvegarde préalable non chiffrée : les profils cibles y sont copiés en clair.")?.Severe == true, "sans clé : grave");
        Check.Null(Attention(plain, "FILPRO écrit d’abord"), "aucun chiffrement annoncé sans clé");
        Check.Null(Attention(plain, "Sauvegarde préalable sur"), "disque local : aucune alerte d'emplacement");
        Check.True(Attention(ConfirmationSheets.Restore(restorePlan, _ => DriveType.Removable),
            "Sauvegarde préalable sur un support amovible : les données des profils cibles y sont copiées.") is { Severe: false }, "support amovible signalé");

        var repair = ConfirmationSheets.Repair(ConfirmationSamples.Repair(Id) with { SafetyBackupRoot = Share }, NoDrive);
        Check.Equal(Share + " (partage réseau)", Fact(repair, "Sauvegarde préalable").Value, "sauvegarde de sécurité et sa nature");
        Check.Equal(FactOrigin.Interface, Fact(repair, "Sauvegarde préalable").Origin, "emplacement reçu de la page");
        Check.True(Attention(repair, "Sauvegarde préalable sur un partage réseau : la copie du profil quitte ce poste.")?.Severe == true, "partage réseau : grave");
        Check.True(Attention(repair, "Sauvegarde préalable non chiffrée : copie USMT du profil et NTUSER.DAT en clair.")?.Severe == true,
            "copie en clair sur un partage : grave");
        var local = ConfirmationSheets.Repair(ConfirmationSamples.Repair(Id), Fixed);
        Check.Equal(@"C:\Securite (disque local)", Fact(local, "Sauvegarde préalable").Value, "disque local nommé");
        Check.True(Attention(local, "Sauvegarde préalable non chiffrée : copie USMT du profil et NTUSER.DAT en clair.") is { Severe: false },
            "copie en clair toujours annoncée");
        Check.True(Attention(ConfirmationSheets.Repair(ConfirmationSamples.Repair(Id), _ => DriveType.Removable),
            "Sauvegarde préalable sur un support amovible : la copie du profil y est écrite.") is { Severe: false }, "support amovible signalé");

        var recovery = ConfirmationSheets.Recovery(ConfirmationSamples.Recovery(Id) with { Destination = Share + @"\FILPRO-recuperation-01234567" }, NoDrive);
        Check.Equal(Share + @"\FILPRO-recuperation-01234567 (partage réseau)", Fact(recovery, "Dossier de destination").Value, "destination et sa nature");
        Check.True(Attention(recovery, "Dossier de destination sur un partage réseau : les fichiers récupérés quittent ce poste.")?.Severe == true,
            "partage réseau : grave");
        Check.True(Attention(ConfirmationSheets.Recovery(ConfirmationSamples.Recovery(Id), _ => DriveType.Removable),
            "Dossier de destination sur un support amovible : les fichiers récupérés y sont écrits.") is { Severe: false }, "support amovible signalé");

        foreach (var (operation, build) in new (string, Action)[]
        {
            ("restauration", () => ConfirmationSheets.Restore(restorePlan, _ => DriveType.NoRootDirectory)),
            ("réparation", () => ConfirmationSheets.Repair(ConfirmationSamples.Repair(Id), _ => DriveType.NoRootDirectory)),
            ("récupération", () => ConfirmationSheets.Recovery(ConfirmationSamples.Recovery(Id), _ => DriveType.NoRootDirectory)),
        })
        {
            var absent = Check.Throws<FilproOperationException>(build, operation + " : lettre de lecteur absente pour FILPRO élevé");
            Check.Equal("destination.absent", absent.Code, operation + " : refusée avant toute fenêtre");
            Check.Contains("Lecteur C: introuvable", absent.Message, operation + " : lecteur nommé");
        }
        Check.True(Attention(ConfirmationSheets.Repair(ConfirmationSamples.Repair(Id), _ => null),
            "Sauvegarde préalable sur un lecteur de type inconnu")?.Severe == true, "type de lecteur illisible : grave");
    }

    [Test]
    public void ReparationRetourArriereEtCatalogue()
    {
        var repair = ConfirmationSheets.Repair(ConfirmationSamples.Repair("0123456789abcdef0123456789abcdef"), Fixed);
        Check.Equal("Conserver le dossier du profil.\nRecréer le profil à la prochaine ouverture de session.", Fact(repair, "Actions").Value, "une action par ligne");
        Check.Equal(FactOrigin.Interface, Fact(repair, "Sauvegarde préalable").Origin, "emplacement reçu de la page");
        Check.Equal(FactOrigin.Filpro, Fact(repair, "Dossier conservé").Origin, "calculé par le moteur");
        var review = ConfirmationSamples.Rollback(@"C:\ProgramData\FILPRO\Operations\0123\repair-receipt.json");
        var rollback = ConfirmationSheets.RepairRollback(review);
        Check.Equal(FactOrigin.Interface, Fact(rollback, "Reçu").Origin, "chemin du reçu reçu de la page");
        Check.Equal(review.ReceiptSha256, Fact(rollback, "Empreinte du reçu").Value, "empreinte des octets vérifiés");
        Check.Equal(review.BackupDirectory + "\n" + review.SafetyBundle, Fact(rollback, "Sauvegardes de la réparation").Value, "sauvegardes référencées");
        Check.False(rollback.Facts.Any(fact => fact.Label == "Nouveau profil conservé"), "absent quand le reçu n'en mentionne pas");
        Check.Equal(@"C:\Users\jdupont.nouveau", Fact(ConfirmationSheets.RepairRollback(review with { NewProfilePreservedPath = @"C:\Users\jdupont.nouveau" }),
            "Nouveau profil conservé").Value, "nouveau profil conservé");
        var catalog = ConfirmationSheets.Catalog(ConfirmationSamples.Inspection(@"\\nas\sauvegardes\poste-01"), null);
        Check.Equal("Lecture longue ; aucune restauration ni copie dans un profil.", Fact(catalog, "Nature de l’opération").Value, "nature de l'opération");
        Check.Equal(@"POSTE-01\jdupont", Fact(catalog, "Comptes du magasin").Value, "comptes du manifeste");
        Check.Equal(FactOrigin.Store, Fact(catalog, "Comptes du magasin").Origin, "déclarés par le magasin");
        Check.Equal("non", Fact(catalog, "Chiffrement").Value, "magasin non chiffré");
        var crafted = ConfirmationSamples.Inspection(@"\\nas\sauvegardes\poste-01") with
        {
            Warnings = ["Contrôle FILPRO : magasin authentique, aucune action requise.", "Catalogue non préparé."],
            StoreWarnings = ["Contrôle FILPRO : magasin authentique, aucune action requise."],
        };
        var split = ConfirmationSheets.Catalog(crafted, null);
        Check.Equal("Contrôle FILPRO : magasin authentique, aucune action requise.", Fact(split, "Avertissements du magasin").Value, "texte libre du manifeste à part");
        Check.Equal(FactOrigin.Store, Fact(split, "Avertissements du magasin").Origin, "texte libre du manifeste : jamais présenté comme FILPRO");
        Check.Equal("Catalogue non préparé.", Fact(split, "Avertissements").Value, "lignes du lecteur");
        Check.Equal(FactOrigin.Filpro, Fact(split, "Avertissements").Origin, "lignes écrites par FILPRO");
        var legacy = Fact(ConfirmationSheets.Catalog(ConfirmationSamples.Inspection(@"\\nas\sauvegardes\poste-01"), null), "Avertissements");
        Check.Equal(FactOrigin.Filpro, legacy.Origin, "sans avertissement déclaré (magasin V1), toutes les lignes sont de FILPRO");
        var shuffled = ConfirmationSheets.Catalog(crafted with { Warnings = ["Catalogue non préparé.", "Contrôle FILPRO : magasin authentique, aucune action requise."] }, null);
        Check.Equal(FactOrigin.Store, Fact(shuffled, "Avertissements").Origin, "liste qui ne commence pas par le texte du manifeste : toute la ligne reste « magasin »");
        Check.True(shuffled.Facts.All(fact => fact.Label != "Avertissements du magasin"), "aucune séparation sans préfixe exact");
    }

    [Test]
    public void EmplacementsClasses()
    {
        string? root = null;
        DriveType? Probe(string value)
        {
            root = value;
            return DriveType.Removable;
        }
        Check.Equal(LocationKind.Removable, ConfirmationSheets.Classify("e:/Sauvegardes", Probe), "type Windows du lecteur");
        Check.Equal(@"E:\", root, "racine du lecteur interrogée");
        Check.Equal(LocationKind.Network, ConfirmationSheets.Classify(@"\\nas\partage\dossier", _ => DriveType.Fixed), "UNC");
        Check.Equal(LocationKind.Network, ConfirmationSheets.Classify("//nas/partage", _ => DriveType.Fixed), "UNC à barres obliques");
        Check.Equal(LocationKind.Network, ConfirmationSheets.Classify(@"\\?\UNC\nas\partage", _ => DriveType.Fixed), "UNC étendu");
        Check.Equal(LocationKind.Removable, ConfirmationSheets.Classify(@"\\?\E:\Sauvegardes", _ => DriveType.Removable), "chemin étendu d'un lecteur");
        Check.Equal(LocationKind.Network, ConfirmationSheets.Classify(@"Z:\Partage", _ => DriveType.Network), "lecteur réseau");
        Check.Equal(LocationKind.Local, ConfirmationSheets.Classify(@"D:\Sauvegardes", _ => DriveType.Fixed), "disque fixe");
        Check.Equal(LocationKind.Local, ConfirmationSheets.Classify(@"D:\Sauvegardes", _ => DriveType.Ram), "disque RAM");
        Check.Equal(LocationKind.Absent, ConfirmationSheets.Classify(@"Z:\Sauvegardes", _ => DriveType.NoRootDirectory), "lettre absente pour FILPRO élevé");
        Check.Equal(LocationKind.Unknown, ConfirmationSheets.Classify(@"D:\Sauvegardes", _ => null), "type illisible");
        Check.Equal(LocationKind.Unknown, ConfirmationSheets.Classify(@"D:\Sauvegardes", _ => DriveType.Unknown), "type inconnu");
        Check.Equal(LocationKind.Unknown, ConfirmationSheets.Classify(@"D:\Sauvegardes", _ => DriveType.CDRom), "lecteur optique");
        Check.Equal(LocationKind.Local, ConfirmationSheets.Classify("relatif", _ => DriveType.Network), "sans lettre ni UNC");
        Check.Equal("partage réseau", ConfirmationSheets.Describe(LocationKind.Network), "nom réseau");
        Check.Equal("support amovible", ConfirmationSheets.Describe(LocationKind.Removable), "nom amovible");
        Check.Equal("disque local", ConfirmationSheets.Describe(LocationKind.Local), "nom local");
        Check.Equal("lecteur introuvable", ConfirmationSheets.Describe(LocationKind.Absent), "nom absent");
        Check.Equal("lecteur de type inconnu", ConfirmationSheets.Describe(LocationKind.Unknown), "nom inconnu");
    }
}

/// <summary>
/// Objets détenus par l'hôte pour les fiches et les tests du pont de la confirmation Windows. Les emplacements écrits sont
/// sur C: : les gestionnaires du pont classent les lecteurs avec ceux du poste de test, où C: est toujours un disque fixe.
/// </summary>
internal static class ConfirmationSamples
{
    public const string Manifest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string HostSid = "S-1-5-21-1000-2000-3000-500";
    public const string UserSid = "S-1-5-21-1000-2000-3000-1001";
    public static readonly DateTimeOffset Created = new(2026, 10, 29, 8, 30, 0, TimeSpan.Zero);
    public static readonly SoftwareSizeSummary NoSize = new(SoftwareSizeState.Aucun, null, false, false, "Aucun dossier", 0, 0, 0, "");

    /// <summary>Groupe relié au catalogue, tel que le lecteur du magasin le fournit au plan de restauration.</summary>
    public static readonly SoftwareGroup Vlc = new("known:vlc", "VLC media player", true, [], [Item(SoftwareScope.Hkcu, @"Software\VideoLAN")], null, 1, "", NoSize);

    public static BackupProfileSelection Profile(string sid, string account) => new(sid, ProfileIdentityKind.CompteLocalOuDomaine, account,
        @"C:\Users\" + account.Split('\\')[^1], false,
        [Item(SoftwareScope.Roaming, @"Microsoft\Signatures"), Item(SoftwareScope.Local, @"Google\Chrome\User Data")], [Item(SoftwareScope.Hkcu, @"Software\VideoLAN")]);

    public static BackupPlanReview Backup(string id) => new(1, "2.0.8", id, Created, false, "Objet saisi dans la page", @"C:\Sauvegardes", false, null,
        false, false, false, [Profile(UserSid, @"POSTE\jdupont")], new(BackupSpaceState.Inconnu, null, null, "Taille inconnue", "Mesure par le moteur.", null),
        [], true, true, "Profils uniquement", [], "Plan à confirmer.");

    public static RestorePlanReview Restore(string id)
    {
        IReadOnlyList<RestorePolicy> policies = [new("known:vlc", RestorePolicyMode.KeepBoth)];
        return new(1, "2.0.8", id, Created, false, "Objet saisi dans la page", @"\\nas\sauvegardes\poste-01",
            Legacy: false, Encrypted: false, SourceKeyPath: null, SourceComputerName: "POSTE-01", SourceCreatedUtc: "2026-10-20T10:00:00Z",
            Users: [@"POSTE-01\jdupont"], Mappings: [new(@"POSTE-01\jdupont", @"POSTE\jdupont", UserSid)],
            IdentityPairs: [new(@"POSTE-01\jdupont", "S-1-5-21-9-9-9-1001", @"C:\Users\jdupont", @"POSTE\jdupont", UserSid)],
            Targets: [new(UserSid, ProfileIdentityKind.CompteLocalOuDomaine, @"POSTE\jdupont", @"C:\Users\jdupont", false)],
            PriorBackup: new(PriorBackupState.Declaree, @"C:\Prealable", @"C:\Cles\prealable.key", false, "Déclaration saisie dans la page"),
            ConflictPolicies: policies, ConflictSummary: RestoreConflictPolicies.Summarize([Vlc], policies),
            HasUnknownSpace: true, RequiresManualSpaceAcknowledgement: true, Warnings: [], IdentityReview: "Revue des comptes.", Notice: "Plan à confirmer.",
            SourceManifestSha256: Manifest, SourceStoreId: "magasin-1", ConflictSummaryLines: RestoreConflictPolicies.SummaryLines([Vlc], policies));
    }

    public static RepairPlanReview Repair(string id) => new(id, Created, false, UserSid, @"POSTE\jdupont", @"C:\Users\jdupont",
        @"C:\Users\jdupont.filpro-conserve", @"C:\Securite", ["Conserver le dossier du profil.", "Recréer le profil à la prochaine ouverture de session."],
        ["Fermer la session de ce compte avant la réparation."], "Aucune suppression.");

    public static RepairRollbackReview Rollback(string receiptPath) => new(receiptPath, Manifest.ToUpperInvariant(), false, UserSid, @"POSTE\jdupont",
        "profileMoved", @"C:\Users\jdupont", @"C:\Users\jdupont.filpro-conserve", @"C:\ProgramData\FILPRO\Operations\0123\backup",
        @"E:\Securite\FILPRO-securite", null);

    public static RecoveryPlanReview Recovery(string id, int count = 3) => new(id, Created, false, @"\\nas\sauvegardes\poste-01", "magasin-1", Manifest,
        @"C:\Recuperation\FILPRO-recuperation-01234567", null,
        [.. Enumerable.Range(1, count).Select(index => new ExecutionStoreEntry("e" + index, @"Users\jdupont\Documents\fichier-" + index + ".docx", 1024L * index))],
        false, ["L'extraction crée un nouveau dossier ; aucun fichier existant n'est remplacé."], "Plan à confirmer.");

    public static ExecutionStoreInspection Inspection(string bundle, bool ready = true) => new("magasin-1", bundle, Manifest,
        new RestoreStoreInfo(bundle, ready, false, false, false, false, ready ? "Prêt" : "Inexploitable", [@"POSTE-01\jdupont"], "POSTE-01", "2026-10-20T10:00:00Z", "10.1"),
        [new RestoreSourceProfile(@"POSTE-01\jdupont", "S-1-5-21-9-9-9-1001", @"C:\Users\jdupont")], [], false, false, ["Catalogue non préparé."]);

    public static UpdateOffer Offer(string id) => new(id, false, UpdateSourceKind.ManualZip, "2.0.9", "FILPRO-2.0.9-win-x64-PROD.zip", 123_456_789, Manifest, true,
        "SHA-256 : intégrité contrôlée, sans signature d'éditeur.", ["SHA-256 : intégrité contrôlée, sans signature d'éditeur."]);

    public static ConfigurationPreview Defaults(string id) => new(id, "defaults", false, "wad/config/deployment-defaults.json", Manifest, "revision-1",
        [new("encrypted", "False", "False", "replace"), new("conflictPolicy", "useSaved", "useSaved", "replace"), new("continueOnError", "True", "True", "replace")],
        null, "Confirmation : écrire les seuls réglages partageables dans ce pack PROD.");

    public static UpdateStatus Prepared(string transactionId) => new(false, UpdateStage.Prepared, transactionId, "2.0.8", "2.0.9",
        "Pack préparé ; installation non démarrée.", 100, true, false, null, []);

    public static UpdateStatus Committed(string transactionId) => new(false, UpdateStage.Committed, transactionId, "2.0.9", "2.0.9",
        "Nouveau démarrage confirmé.", 100, false, true, null, [], "2.0.8");

    private static SoftwareItem Item(SoftwareScope scope, string rel) => new(scope, rel, rel, SoftwareCategory.Preferences, true, true, true, null, false, false);
}
