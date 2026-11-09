using System.Collections.Immutable;
using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>Préférences via le pont fermé : cohérence fichiers/registre, périmètre du profil et refus sans mutation.</summary>
internal sealed class SelectionBridgeTests
{
    private const string Lea = "S-1-5-21-111222333-444555666-777888999-1001";
    private const string Sophie = "S-1-5-21-111222333-444555666-777888999-1003";
    private static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task MemoriserUnGroupeConserveSesFichiersEtSesClesEnsemble()
    {
        var harness = new Harness();
        var result = Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:vlc" } }));
        var vlc = result.Groups.Single(group => group.Id == "known:vlc");
        Check.True(vlc.Selected == true, "le groupe choisi est coché entièrement");
        Check.True(vlc.Files.Count > 0 && vlc.Registry.Count > 0, "le groupe contient bien les deux portées");
        Check.True(vlc.Files.Concat(vlc.Registry).All(item => item.Selected), "fichiers et clés portent le même choix");
        Check.True(result.Groups.Where(group => group.Id != "known:vlc").All(group => group.Selected == false),
            "les recommandations ne réactivent pas d'autres groupes");

        var stored = Result<LastSelection>(await harness.Call("sauvegarder.lastSelection", new { sid = Lea }));
        Check.Equal("vlc", stored.Files.Single().Rel, "le chemin est reconstruit par le fournisseur");
        Check.Equal(@"Software\VideoLAN\VLC", stored.Registry.Single().Rel, "la clé associée est enregistrée avec le fichier");
        Check.Equal(1, harness.Source.SaveCalls, "une seule mémorisation explicite");
    }

    [Test]
    public async Task LePlanMultiProfilsRelitLesChoixDistinctsDeChaqueSid()
    {
        var harness = new Harness();
        Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:notepadpp" } }));
        Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Sophie, groupIds = new[] { "known:vlc" } }));
        var plan = Result<BackupPlanReview>(await harness.Call("sauvegarder.plan", new { sids = new[] { Lea, Sophie }, destination = @"D:\Sauvegardes" }));
        Check.Equal(2, plan.Profiles.Count, "les deux profils sont retenus");
        var lea = plan.Profiles.Single(profile => profile.Sid == Lea);
        var sophie = plan.Profiles.Single(profile => profile.Sid == Sophie);
        Check.Equal("Notepad++", lea.Files.Single().Rel, "Léa garde son choix propre");
        Check.Equal(@"Software\Notepad++", lea.Registry.Single().Rel, "Léa garde la clé liée à son logiciel");
        Check.Equal("vlc", sophie.Files.Single().Rel, "Sophie n'emprunte pas le choix de Léa");
        Check.Equal(@"Software\VideoLAN\VLC", sophie.Registry.Single().Rel, "Sophie garde sa propre clé");
        Check.Equal(2, harness.Source.SaveCalls, "la préparation du plan n'enregistre pas de nouveaux choix");
    }

    [Test]
    public async Task UneSelectionVideResteVideDansLaLectureEtLePlan()
    {
        var harness = new Harness();
        var inventory = Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = Array.Empty<string>() }));
        Check.True(inventory.Groups.All(group => group.Selected == false), "tout décocher ne relance pas les recommandations");
        var last = Result<LastSelection>(await harness.Call("sauvegarder.lastSelection", new { sid = Lea }));
        Check.Equal(0, last.Files.Count + last.Registry.Count, "document vide présent, distinct d'une absence de mémoire");
        var plan = Result<BackupPlanReview>(await harness.Call("sauvegarder.plan", new { sids = new[] { Lea }, destination = @"D:\Sauvegardes" }));
        Check.Equal(0, plan.Profiles.Single().Files.Count + plan.Profiles.Single().Registry.Count,
            "la préparation ne substitue pas les recommandations au choix vide");
    }

    [Test]
    public async Task LesArgumentsRefusesNeModifientPasLesChoixExistants()
    {
        var harness = new Harness();
        Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:notepadpp" } }));
        var before = await LastDocument(harness);
        (object Args, string Code)[] refusals =
        [
            (new { sid = Lea }, FilproErrors.ArgumentsInvalid),
            (new { sid = Lea, groupIds = "known:vlc" }, FilproErrors.ArgumentsInvalid),
            (new { sid = Lea, groupIds = new[] { "known:inconnu" } }, FilproErrors.ArgumentsInvalid),
            (new { sid = Lea, groupIds = new[] { "known:vlc", "known:vlc" } }, FilproErrors.ArgumentsInvalid),
            (new { sid = Lea, groupIds = new[] { "known:vlc" }, path = @"C:\interdit.json" }, FilproErrors.ArgumentsInvalid),
            (new { sid = "S-1-5-21-0-0-0-9999", groupIds = new[] { "known:vlc" } }, BackupPlan.SelectionCode),
        ];
        foreach (var refusal in refusals)
        {
            var response = await harness.Call("sauvegarder.saveSelection", refusal.Args);
            Check.Equal("error", response.Kind, "la demande invalide est refusée");
            Check.Equal(refusal.Code, response.Error?.Code, "le refus attendu ne devient pas un échec technique masqué");
            Check.Equal(1, harness.Source.SaveCalls, "aucune seconde sauvegarde de préférences après le refus");
            Check.Equal(before, await LastDocument(harness), "les choix antérieurs restent identiques");
        }
    }

    [Test]
    public async Task UnInventaireIncompletNePeutPasEffacerLaSelectionPrecedente()
    {
        var harness = new Harness();
        Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:notepadpp" } }));
        var before = await LastDocument(harness);
        foreach (var filesIncomplete in new[] { true, false })
        {
            harness.Source.IncompleteFiles = filesIncomplete;
            harness.Source.IncompleteRegistry = !filesIncomplete;
            var response = await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = Array.Empty<string>() });
            Check.Equal(FilproErrors.SourceUnavailable, response.Error?.Code, "une lecture partielle interdit le remplacement des choix");
            Check.Equal(1, harness.Source.SaveCalls, "le fournisseur ne reçoit aucune écriture sur inventaire incomplet");
            Check.Equal(before, await LastDocument(harness), "le document complet précédent n'est pas remplacé par une sélection tronquée");
        }
    }

    [Test]
    public async Task UnInventaireIncompletNePeutPasProduireUnPlanAmpute()
    {
        var harness = new Harness();
        Result<SoftwareInventory>(await harness.Call("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:vlc" } }));
        var before = await LastDocument(harness);
        foreach (var filesIncomplete in new[] { true, false })
        {
            harness.Source.IncompleteFiles = filesIncomplete;
            harness.Source.IncompleteRegistry = !filesIncomplete;
            var response = await harness.Call("sauvegarder.plan", new { sids = new[] { Lea }, destination = @"D:\Sauvegardes" });
            Check.Equal(FilproErrors.SourceUnavailable, response.Error?.Code, "pas de plan donnant une fausse preuve de sélection complète");
            Check.Equal(before, await LastDocument(harness), "les derniers choix restent intacts");
            Check.Equal(1, harness.Source.SaveCalls, "aucune réécriture après découverte partielle");
        }
    }

    [Test]
    public async Task UnFournisseurDunAutreModeEstRefuseAvantTouteLecture()
    {
        foreach (var mode in new[] { AppMode.Simulation, AppMode.Poste })
        {
            var harness = new Harness(mode, demonstration: mode != AppMode.Simulation);
            (string Operation, object Args)[] calls =
            [
                ("sauvegarder.profiles", new { }),
                ("sauvegarder.software", new { sid = Lea }),
                ("sauvegarder.lastSelection", new { sid = Lea }),
                ("sauvegarder.saveSelection", new { sid = Lea, groupIds = new[] { "known:vlc" } }),
                ("sauvegarder.plan", new { sids = new[] { Lea }, destination = @"D:\Sauvegardes" }),
                ("sauvegarder.restorePlan", RestoreArguments([Lea], Lea)),
            ];
            foreach (var call in calls)
            {
                var response = await harness.Call(call.Operation, call.Args);
                Check.Equal(FilproErrors.SourceUnavailable, response.Error?.Code, "le fournisseur incompatible est refusé");
                Check.Equal(0, harness.Source.ReadCalls, "le refus précède toute lecture de données et d'identité");
                Check.Equal(0, harness.Source.SaveCalls, "le refus précède toute écriture de préférences");
            }
        }
    }

    [Test]
    public async Task LeCompteExecutantReluEstRefuseMemeSiSonProfilNestPasCharge()
    {
        var args = RestoreArguments([Lea], Lea);
        Result<RestorePlanPreview>(await new Harness().Call("sauvegarder.restorePlan", args));
        var harness = new Harness(executingSid: Lea);
        Check.True(harness.Source.ReadProfiles(CancellationToken.None).Profiles.Single(profile => profile.Sid == Lea).Loaded == false,
            "la fixture est un profil non chargé : le refus doit provenir de l'identité exécutante");
        var response = await harness.Call("sauvegarder.restorePlan", args);
        Check.Equal(RestorePlan.TargetCode, response.Error?.Code, "la fonction pure refuse le compte exécutant fourni par le pont");
        Check.Contains("exécute FILPRO", response.Error?.Message, "le motif n'est ni le chargement ni une erreur de correspondance");
        Check.Equal(1, harness.Source.IdentityReads, "l'identité a réellement été demandée au fournisseur");
        Check.Equal(0, harness.Source.SaveCalls, "la préparation refusée ne modifie aucun choix");
    }

    [Test]
    public async Task LeProfilDesConflitsDoitAppartenirAuxDestinations()
    {
        foreach (var targets in new[] { new[] { Lea }, Array.Empty<string>() })
        {
            var harness = new Harness();
            var response = await harness.Call("sauvegarder.restorePlan", RestoreArguments(targets, Sophie));
            Check.Equal(RestorePlan.TargetCode, response.Error?.Code, "un profil existant mais hors cibles est refusé");
            Check.Contains("destinations retenues", response.Error?.Message, "l'appartenance aux cibles est la cause explicite");
            Check.Equal(0, harness.Source.SoftwareReads, "les logiciels du profil hors cible ne sont pas lus");
            Check.Equal(0, harness.Source.IdentityReads, "le plan s'arrête avant d'interroger le compte exécutant");
            Check.Equal(0, harness.Source.SaveCalls, "le refus ne modifie rien");
        }
    }

    private static object RestoreArguments(string[] targets, string conflictSid) => new
    {
        bundle = @"\\serveur\partage\magasin",
        storeReady = true,
        manifestUsers = new[] { @"DEMO\lea" },
        users = new[] { @"DEMO\lea" },
        targetSids = targets,
        conflictSid,
        priorRoot = @"D:\prealable",
        priorKeyPath = @"D:\cles\prealable.key",
    };

    private static async Task<string> LastDocument(Harness harness) =>
        JsonSerializer.Serialize(Result<LastSelection>(await harness.Call("sauvegarder.lastSelection", new { sid = Lea })));

    private static T Result<T>(BridgeResponse response) where T : class
    {
        Check.Equal("result", response.Kind, "résultat attendu, reçu : " + response.Error?.Code + " / " + response.Error?.Message);
        var result = response.Data as T;
        Check.NotNull(result, "le contrat de résultat attendu est renvoyé");
        return result!;
    }

    private sealed class Harness
    {
        private readonly AppMode mode;
        private readonly OperationRegistry registry;

        public Harness(AppMode mode = AppMode.Simulation, bool? demonstration = null, string? executingSid = null)
        {
            this.mode = mode;
            Source = new ObservedPoste(demonstration ?? true, executingSid);
            var decision = new ModeDecision(mode, ModeReason.Argument, "Contrôle du pont", null, ImmutableArray<string>.Empty);
            var context = new EngineContext(decision, null, Source, Clock, SilentTechnicalJournal.Instance);
            registry = new OperationRegistry(new SauvegarderModule().CreateOperations(context));
        }

        public ObservedPoste Source { get; }

        public async Task<BridgeResponse> Call(string operation, object args)
        {
            var published = new List<BridgeResponse>();
            var dispatcher = new BridgeDispatcher(registry, published.Add, AdmissionGuards.For(mode));
            await dispatcher.HandleAsync(JsonSerializer.Serialize(new { id = "selection-test", op = operation, args }), CancellationToken.None);
            return published.Single();
        }
    }

    private sealed class ObservedPoste(bool demonstration, string? executingSid) : IFilproPosteSource
    {
        private readonly SimulationPosteSource inner = new(SimulationOptions.Default, Clock);
        public bool Demonstration => demonstration;
        public bool IncompleteFiles { get; set; }
        public bool IncompleteRegistry { get; set; }
        public int ProfileReads { get; private set; }
        public int SoftwareReads { get; private set; }
        public int SelectionReads { get; private set; }
        public int IdentityReads { get; private set; }
        public int SaveCalls { get; private set; }
        public int ReadCalls => ProfileReads + SoftwareReads + SelectionReads + IdentityReads;

        public string ReadCurrentAccountSid(CancellationToken cancellationToken)
        {
            IdentityReads++;
            cancellationToken.ThrowIfCancellationRequested();
            return executingSid ?? inner.ReadCurrentAccountSid(cancellationToken);
        }

        public ProfileInventory ReadProfiles(CancellationToken cancellationToken)
        {
            ProfileReads++;
            return inner.ReadProfiles(cancellationToken);
        }

        public SoftwareInventory ReadSoftware(string sid, SoftwareQuery query, CancellationToken cancellationToken)
        {
            SoftwareReads++;
            var inventory = inner.ReadSoftware(sid, query, cancellationToken);
            return inventory with
            {
                Discovery = inventory.Discovery with { FilesComplete = !IncompleteFiles, RegistryComplete = !IncompleteRegistry },
            };
        }

        public LastSelection? ReadLastSelection(string sid, CancellationToken cancellationToken)
        {
            SelectionReads++;
            return inner.ReadLastSelection(sid, cancellationToken);
        }

        public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return inner.SaveLastSelection(selection, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
