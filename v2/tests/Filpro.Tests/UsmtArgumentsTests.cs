using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Construction sûre des arguments USMT à partir du plan typé : ordre et jetons fidèles à la V1
/// (<c>Get-FilproUsmtArguments</c>), comptes bornés, journaux hors du magasin, estimation exigée, chiffrement borné,
/// jamais de <c>/o</c>, jamais de shell (un chemin avec espace reste un seul jeton). Une fonction pure : aucune E/S,
/// aucun processus, aucune exécution.
/// </summary>
internal sealed class UsmtArgumentsTests
{
    private const string Store = @"C:\magasins\lot\Store";
    private const string Logs = @"C:\filpro\UsmtLogs\run1";
    private static readonly string[] Manifests = [@"C:\usmt\MigDocs.xml", @"C:\rules\filpro-config.xml"];
    private static readonly string[] Users = ["LAB\\alice", "LAB\\bob"];

    private static UsmtArgumentRequest Request(
        UsmtAction action = UsmtAction.Scan,
        string store = Store,
        string logDirectory = Logs,
        IReadOnlyList<string>? manifests = null,
        IReadOnlyList<string>? users = null,
        IReadOnlyList<string>? excludeUsers = null,
        IReadOnlyList<UsmtAccountMapping>? mappings = null,
        string? config = null,
        string? estimateFile = null,
        string? bundle = null,
        string? keyFile = null,
        bool noCompress = false,
        bool continueOnError = false,
        bool useVss = false,
        bool encrypted = false) =>
        new(action, store, logDirectory, manifests ?? Manifests, users ?? Users,
            excludeUsers, mappings, config, estimateFile, bundle, keyFile,
            noCompress, continueOnError, useVss, encrypted);

    private static UsmtInvocation Build(UsmtArgumentRequest request) => UsmtArguments.Build(request);

    private static bool Has(UsmtInvocation inv, string token) => inv.Arguments.Contains(token, StringComparer.Ordinal);

    private static bool HasPrefix(UsmtInvocation inv, string prefix) => inv.Arguments.Any(a => a.StartsWith(prefix, StringComparison.Ordinal));

    private static int Index(UsmtInvocation inv, string token) => inv.Arguments.ToList().IndexOf(token);

    // ---------- Scan ----------

    [Test]
    public void ScanReproduitLesJetonsDeLaV1()
    {
        var inv = Build(Request());
        Check.Equal("scanstate.exe", inv.Executable, "Scan pilote scanstate.exe");
        Check.Equal(Store, inv.Arguments[0], "le magasin est le premier jeton");
        Check.True(Has(inv, "/i:" + Manifests[0]) && Has(inv, "/i:" + Manifests[1]), "chaque règle est passée en /i:");
        Check.True(Has(inv, "/v:5"), "verbosité comme la V1");
        Check.True(Has(inv, "/l:" + Logs + "\\scanstate.log"), "journal scanstate");
        Check.True(Has(inv, "/progress:" + Logs + "\\scanstate-progress.log"), "journal de progression scanstate");
        Check.True(Has(inv, "/localonly") && Has(inv, "/ue:*") && Has(inv, "/efs:abort"), "garde-fous Scan de la V1");
        Check.True(Has(inv, "/ui:LAB\\alice") && Has(inv, "/ui:LAB\\bob"), "un /ui: par compte");
        Check.True(Has(inv, "/listfiles:" + Logs + "\\captured-files.txt"), "liste des fichiers capturés");
        Check.False(Has(inv, "/vsc"), "VSS absent sans demande");
        Check.False(Has(inv, "/nocompress"), "compression par défaut");
        Check.False(Has(inv, "/c"), "poursuite après erreur non demandée");
        Check.False(HasPrefix(inv, "/o"), "jamais de /o : un magasin n'écrase jamais un autre");
        Check.False(HasPrefix(inv, "/mu:"), "pas de correspondance en sauvegarde");
    }

    [Test]
    public void OrdreDesJetonsestCeluiDeLaV1()
    {
        var inv = Build(Request(useVss: true, continueOnError: true));
        Check.Equal(0, Index(inv, Store), "le magasin ouvre la ligne");
        Check.True(Index(inv, "/i:" + Manifests[0]) < Index(inv, "/v:5"), "les règles précèdent le niveau de verbesité");
        Check.True(Index(inv, "/v:5") < Index(inv, "/l:" + Logs + "\\scanstate.log"), "/v:5 avant /l:");
        Check.True(Index(inv, "/l:" + Logs + "\\scanstate.log") < Index(inv, "/progress:" + Logs + "\\scanstate-progress.log"), "/l: avant /progress:");
        Check.True(Index(inv, "/localonly") < Index(inv, "/ue:*"), "/localonly avant /ue:*");
        Check.True(Index(inv, "/efs:abort") < Index(inv, "/ui:LAB\\alice"), "/efs:abort avant les comptes");
        Check.True(Index(inv, "/ui:LAB\\bob") < Index(inv, "/vsc"), "VSS après les comptes");
        Check.True(Has(inv, "/c"), "poursuite après erreur = /c");
    }

    [Test]
    public void OptionsScanSontFideles()
    {
        var vss = Build(Request(useVss: true));
        Check.True(Has(vss, "/vsc"), "VSS demandé ajoute /vsc");
        var nocompress = Build(Request(noCompress: true));
        Check.True(Has(nocompress, "/nocompress"), "non-compression = /nocompress");
        var config = Build(Request(config: @"C:\usmt\config.xml"));
        Check.True(Has(config, "/config:" + @"C:\usmt\config.xml"), "config passée en /config:");
    }

    [Test]
    public void ReglesVidesSontIgnorees()
    {
        var inv = Build(Request(manifests: [@"C:\usmt\MigDocs.xml", "", "   ", null!]));
        Check.True(Has(inv, "/i:" + @"C:\usmt\MigDocs.xml"), "la règle valable est conservée");
        Check.Equal(1, inv.Arguments.Count(a => a.StartsWith("/i:", StringComparison.Ordinal)), "les entrées vides sont ignorées, comme la V1");
    }

    // ---------- Estimate ----------

    [Test]
    public void EstimateExigeLeFichierDEstimation()
    {
        var inv = Build(Request(UsmtAction.Estimate, estimateFile: Logs + "\\estimate.xml"));
        Check.Equal("scanstate.exe", inv.Executable, "l'estimation passe par scanstate.exe");
        Check.True(Has(inv, "/p:" + Logs + "\\estimate.xml"), "le fichier d'estimation est passé en /p:");
        Check.False(HasPrefix(inv, "/listfiles:"), "pas de liste de fichiers pour une estimation");
        var missing = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Estimate)), "estimation sans fichier refusée");
        Check.Equal(UsmtArguments.PlanCode, missing.Code, "code usmt.plan");
        Check.Contains("estimation", missing.Message, "le motif est annoncé");
    }

    [Test]
    public void EstimationNeChiffreJamais()
    {
        var inv = Build(Request(UsmtAction.Estimate, estimateFile: Logs + "\\estimate.xml", encrypted: true, keyFile: @"D:\cles\cle.key"));
        Check.False(HasPrefix(inv, "/encrypt:"), "l'estimation n'émet jamais /encrypt: (règle V1)");
        Check.False(HasPrefix(inv, "/decrypt:"), "l'estimation n'émet jamais /decrypt:");
        Check.False(HasPrefix(inv, "/keyfile:"), "aucune clé pour une estimation");
    }

    // ---------- Load ----------

    [Test]
    public void LoadReproduitLesJetonsDeLaV1()
    {
        var inv = Build(Request(
            UsmtAction.Load,
            excludeUsers: ["LAB\\carol"],
            mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\alice2")]));
        Check.Equal("loadstate.exe", inv.Executable, "Load pilote loadstate.exe");
        Check.True(Has(inv, "/ue:*"), "exclusion globale d'abord");
        Check.True(Has(inv, "/ui:LAB\\alice") && Has(inv, "/ui:LAB\\bob"), "un /ui: par compte restauré");
        Check.True(Has(inv, "/ue:LAB\\carol"), "compte exclu passé en /ue:");
        Check.True(Has(inv, "/mu:LAB\\alice:LAB\\alice2"), "correspondance modifiée passée en /mu:");
        Check.False(HasPrefix(inv, "/mu:LAB\\bob"), "cible implicite (bob→bob) n'émet pas de /mu:");
        Check.False(Has(inv, "/localonly"), "/localonly réservé à Scan/Estimate");
        Check.False(Has(inv, "/efs:abort"), "/efs:abort réservé à Scan/Estimate");
        Check.False(HasPrefix(inv, "/vsc"), "pas de VSS en restauration");
        Check.False(HasPrefix(inv, "/listfiles:"), "pas de liste de fichiers en restauration");
        Check.False(HasPrefix(inv, "/p:"), "pas d'estimation en restauration");
        Check.False(HasPrefix(inv, "/o"), "jamais de /o");
    }

    [Test]
    public void CorrespondanceIdentiqueNaPasDeMu()
    {
        var inv = Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\alice"), new UsmtAccountMapping("LAB\\bob", "LAB\\bob")]));
        Check.False(HasPrefix(inv, "/mu:"), "source égale cible : aucun /mu:");
    }

    // ---------- Journaux hors du magasin ----------

    [Test]
    public void JournauxDoiventEtreHorsDuMagasin()
    {
        var inside = Check.Throws<FilproOperationException>(
            () => Build(Request(logDirectory: Store + "\\logs")), "journaux dans le magasin refusés");
        Check.Equal(UsmtArguments.PlanCode, inside.Code, "code usmt.plan");
        Check.Contains("hors du magasin", inside.Message, "la raison est celle de la V1");
        Check.Throws<FilproOperationException>(
            () => Build(Request(logDirectory: Store)), "journaux à la racine du magasin refusés");
    }

    // ---------- Comptes ----------

    [Test]
    public void SelectionDeComptesEstObligatoireEtExplicite()
    {
        var empty = Check.Throws<FilproOperationException>(() => Build(Request(users: [])), "aucun compte refusé");
        Check.Equal(UsmtArguments.SelectionCode, empty.Code, "code usmt.selection");
        Check.Contains("Sélection explicite", empty.Message, "le refus annonce la sélection obligatoire");
        var bare = Check.Throws<FilproOperationException>(() => Build(Request(users: ["alice"])), "compte sans domaine refusé");
        Check.Contains("non explicite", bare.Message, "le compte non résolu est nommé");
        var sid = Check.Throws<FilproOperationException>(() => Build(Request(users: ["S-1-5-21-1-2-3-1001"])), "un SID ne tient pas lieu de compte");
        Check.Contains("non explicite", sid.Message, "le SID substitué est refusé");
        var dup = Check.Throws<FilproOperationException>(() => Build(Request(users: ["LAB\\alice", "lab\\ALICE"])), "doublon refusé");
        Check.Contains("Compte en double", dup.Message, "le doublon est nommé");
    }

    [Test]
    public void LoadRefuseUnCompteInclusEtExclu()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Load, excludeUsers: ["LAB\\alice"])), "un compte ne peut être des deux côtés");
        Check.Equal(UsmtArguments.SelectionCode, ex.Code, "code usmt.selection");
        Check.Contains("simultanément", ex.Message, "la raison est celle de la V1");
    }

    [Test]
    public void CompteExcluDoitEtreExplicite()
    {
        var ex = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Load, excludeUsers: ["carol"])), "compte exclu sans domaine refusé");
        Check.Contains("Compte à exclure non explicite", ex.Message, "le motif est annoncé");
        var blank = Build(Request(UsmtAction.Load, excludeUsers: ["", "   "]));
        Check.False(HasPrefix(blank, "/ue:LAB"), "les exclusions vides sont ignorées, comme la V1");
    }

    [Test]
    public void CorrespondancesDeMigrationSontGardees()
    {
        var unselected = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\carol", "LAB\\carol2")])),
            "correspondance sur un compte non sélectionné refusée");
        Check.Contains("non sélectionné", unselected.Message, "le motif est annoncé");
        var twice = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\x"), new UsmtAccountMapping("LAB\\alice", "LAB\\y")])),
            "double correspondance refusée");
        Check.Contains("en double", twice.Message, "le doublon est nommé");
        var merge = Check.Throws<FilproOperationException>(
            () => Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\bob")])),
            "fusion détectée même si bob n'a pas de correspondance explicite (cible implicite bob)");
        Check.Contains("Fusion", merge.Message, "la fusion est refusée sur l'ensemble des comptes");
    }

    // ---------- Chiffrement ----------

    [Test]
    public void ChiffrementScanEtLoad()
    {
        var scan = Build(Request(encrypted: true, keyFile: @"D:\cles\cle.key", bundle: @"C:\magasins\lot"));
        Check.True(Has(scan, "/encrypt:AES_256"), "Scan chiffre en AES_256");
        Check.True(Has(scan, "/keyfile:" + @"D:\cles\cle.key"), "le chemin de la clé est passé, jamais son contenu");
        var load = Build(Request(UsmtAction.Load, encrypted: true, keyFile: @"D:\cles\cle.key", bundle: @"C:\magasins\lot"));
        Check.True(Has(load, "/decrypt:AES_256"), "Load déchiffre en AES_256");
    }

    [Test]
    public void ChiffrementEstBorne()
    {
        var noKey = Check.Throws<FilproOperationException>(() => Build(Request(encrypted: true)), "chiffrement sans clé refusé");
        Check.Equal(UsmtArguments.CryptoCode, noKey.Code, "code usmt.crypto");
        Check.Contains("clé", noKey.Message, "la clé est exigée");
        var withNoCompress = Check.Throws<FilproOperationException>(
            () => Build(Request(encrypted: true, keyFile: @"D:\cles\cle.key", noCompress: true)), "chiffrement et non-compression incompatibles");
        Check.Contains("non compressé", withNoCompress.Message, "l'incompatibilité est annoncée");
        var insideBundle = Check.Throws<FilproOperationException>(
            () => Build(Request(encrypted: true, keyFile: @"C:\magasins\lot\cle.key", bundle: @"C:\magasins\lot")), "clé dans le lot refusée");
        Check.Contains("hors du dossier de sauvegarde", insideBundle.Message, "la clé reste hors du lot");
    }

    // ---------- Sûreté (pas de shell) et déterminisme ----------

    [Test]
    public void UnCheminAvecEspaceResteUnSeulJeton()
    {
        const string spaced = @"C:\Mes Magasins\lot\Store";
        var inv = Build(Request(store: spaced));
        Check.Equal(spaced, inv.Arguments[0], "le magasin avec espace est un jeton unique, non découpé");
        Check.False(inv.Arguments.Contains("Mes", StringComparer.Ordinal), "aucune découpe sur les espaces : aucune commande shell");
        Check.True(inv.Arguments.All(a => a.Length > 0), "aucun jeton vide");
    }

    [Test]
    public void ConstructionEstDeterministe()
    {
        var first = Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\alice2")], excludeUsers: ["LAB\\carol"]));
        var again = Build(Request(UsmtAction.Load, mappings: [new UsmtAccountMapping("LAB\\alice", "LAB\\alice2")], excludeUsers: ["LAB\\carol"]));
        Check.Equal(string.Join("\n", first.Arguments), string.Join("\n", again.Arguments), "la même demande produit exactement les mêmes jetons");
        Check.Contains("ArgumentList", first.Notice, "la mention renvoie à ArgumentList, jamais au shell");
        Check.Contains("arrêt demandé", first.Notice, "un arrêt demandé n'est pas un arrêt achevé");
        Check.Contains("variable d'environnement", first.Notice, "l'exécutable n'est jamais résolu depuis l'environnement");
    }

    [Test]
    public void JournalsetExecutableSontAnnonces()
    {
        var scan = Build(Request());
        Check.True(scan.LogPaths.Contains(Logs + "\\scanstate.log", StringComparer.Ordinal), "le journal principal est annoncé");
        Check.True(scan.LogPaths.Contains(Logs + "\\captured-files.txt", StringComparer.Ordinal), "la liste des fichiers est annoncée pour Scan");
        var load = Build(Request(UsmtAction.Load));
        Check.True(load.LogPaths.Contains(Logs + "\\loadstate.log", StringComparer.Ordinal), "le journal loadstate est annoncé");
        Check.False(load.LogPaths.Any(p => p.EndsWith("captured-files.txt", StringComparison.Ordinal)), "aucune liste de fichiers annoncée en Load");
    }

    // ---------- Adaptateurs depuis le plan typé ----------

    [Test]
    public void AdaptateurDepuisLePlanDeSauvegarde()
    {
        var profile = new BackupProfileSelection(
            "S-1-5-21-1-2-3-1001", ProfileIdentityKind.CompteLocalOuDomaine, "LAB\\alice", "C:\\Users\\alice", false,
            [new SoftwareItem(SoftwareScope.Roaming, "Contoso\\App", "App", SoftwareCategory.Donnees, true, false, false, null, false, false)],
            [new SoftwareItem(SoftwareScope.Hkcu, "Software\\Contoso", "Contoso", SoftwareCategory.Preferences, true, false, false, null, false, false)]);
        var plan = BackupPlan.Create(
            new BackupPlanRequest([profile], @"\\serveur\partage\filpro", Encrypted: true, KeyPath: @"D:\cles\cle.key", UseVss: true),
            "0123456789abcdef0123456789abcdef", new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero));

        var request = UsmtArgumentRequest.ForBackup(plan, UsmtAction.Scan, Store, Logs, Manifests);
        var inv = Build(request);
        Check.True(Has(inv, "/ui:LAB\\alice"), "le compte vient du plan");
        Check.True(Has(inv, "/vsc"), "VSS vient du plan");
        Check.True(Has(inv, "/encrypt:AES_256") && Has(inv, "/keyfile:" + @"D:\cles\cle.key"), "le chiffrement et la clé viennent du plan");

        var wrong = Check.Throws<FilproOperationException>(
            () => UsmtArgumentRequest.ForBackup(plan, UsmtAction.Load, Store, Logs, Manifests), "un plan de sauvegarde ne fait pas de LoadState");
        Check.Equal(UsmtArguments.PlanCode, wrong.Code, "code usmt.plan");
    }

    [Test]
    public void AdaptateurDepuisLePlanDeRestauration()
    {
        var store = new RestoreStoreInfo(@"\\serveur\partage\filpro\magasin", true, false, false, false, true, "Completed",
            ["LAB\\alice", "LAB\\bob"], "POSTE-SOURCE", "2026-10-20T08:00:00Z", "10.0.22621");
        var plan = RestorePlan.Validate(
            new RestorePlanRequest(
                store,
                ["LAB\\alice", "LAB\\bob"],
                [new RestoreAccountMapping("LAB\\alice", "LAB\\alice2")],
                [
                    new RestoreTargetProfile("S-1-5-21-1-2-3-2002", ProfileIdentityKind.CompteLocalOuDomaine, "LAB\\alice2", "C:\\Users\\alice2", false),
                    new RestoreTargetProfile("S-1-5-21-1-2-3-2001", ProfileIdentityKind.CompteLocalOuDomaine, "LAB\\bob", "C:\\Users\\bob", false),
                ],
                null, null, null,
                new PriorBackupRequirement(PriorBackupState.Identifiee, @"D:\prealable", @"D:\cles\prealable.key", false, ""),
                null, @"D:\cles\source.key", false, false, false, false, null),
            "0123456789abcdef0123456789abcdef", new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

        var request = UsmtArgumentRequest.ForRestore(plan, @"\\serveur\partage\filpro\magasin\Store", Logs, Manifests);
        var inv = Build(request);
        Check.Equal("loadstate.exe", inv.Executable, "le plan de restauration pilote loadstate.exe");
        Check.True(Has(inv, "/ui:LAB\\alice") && Has(inv, "/ui:LAB\\bob"), "les comptes viennent du plan");
        Check.True(Has(inv, "/mu:LAB\\alice:LAB\\alice2"), "la correspondance vient du plan");
        Check.True(Has(inv, "/decrypt:AES_256") && Has(inv, "/keyfile:" + @"D:\cles\source.key"), "le déchiffrement et la clé viennent du plan");
    }
}
