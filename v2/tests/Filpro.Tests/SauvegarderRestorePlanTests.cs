using System.Collections.Immutable;
using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>
/// Parcours de confirmation du plan de restauration branché sur le pont : <c>sauvegarder.restorePlan</c> (NetworkRead
/// servie) relit les profils cibles et les portées de conflits chez le fournisseur poste, réutilise
/// <see cref="RestoreConflictPolicies"/> pour les conflits par portée et renvoie l'aperçu canonisé à taille inconnue ;
/// <c>sauvegarder.confirmRestorePlan</c> (Write) est refusée par la garde d'admission avant toute exécution, et son
/// gestionnaire exige les trois accusés (capacité, sauvegarde préalable, revue des identités). Aucune écriture, aucune
/// mesure, aucun montage de ruche : le fournisseur est le monde fictif de démonstration et le magasin n'est que déclaré.
/// </summary>
internal sealed class SauvegarderRestorePlanTests
{
    private const string Lea = "S-1-5-21-111222333-444555666-777888999-1001";      // non chargé
    private const string Camille = "S-1-5-21-111222333-444555666-777888999-1000";  // chargé (session active)
    private const string LeaAccount = @"DEMO\lea";
    private const string PlanId = "0123456789abcdef0123456789abcdef";

    /// <summary>Saisie opérateur par défaut : un magasin déclaré prêt, un compte au manifeste et à restaurer.</summary>
    private const string DefaultProperties = """
        "bundle":"\\\\serveur\\partage\\filpro\\magasin","storeReady":true,"manifestUsers":["DEMO\\lea"],"users":["DEMO\\lea"]
        """;

    private static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    private static EngineContext Context(string? executingSid = null)
    {
        var decision = new ModeDecision(AppMode.Simulation, ModeReason.DefaultDemonstration, ModeTexts.DetailDefault, ModeTexts.Banner, ImmutableArray<string>.Empty);
        IFilproPosteSource poste = new SimulationPosteSource(SimulationOptions.Default, Clock);
        if (executingSid is not null) poste = new PosteWithExecutingAccount(poste, executingSid);
        return new EngineContext(decision, simulation: null, poste, Clock, SilentTechnicalJournal.Instance);
    }

    private static OperationRegistry Registry(string? executingSid = null) => new(new SauvegarderModule().CreateOperations(Context(executingSid)));

    /// <summary>
    /// Arguments de la page : les propriétés par défaut, remplacées par celles de <paramref name="extra"/>. Le pont refuse
    /// une propriété donnée deux fois, d'où la fusion plutôt que la concaténation.
    /// </summary>
    private static string Args(string extra)
    {
        using var document = JsonDocument.Parse("{" + DefaultProperties + "}");
        var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            kept[property.Name] = property.Value.Clone();
        }
        if (extra.Length > 0)
        {
            var text = extra.Trim();
            using var overrides = JsonDocument.Parse(text.StartsWith("{", StringComparison.Ordinal) ? text : "{" + text + "}");
            foreach (var property in overrides.RootElement.EnumerateObject())
            {
                kept[property.Name] = property.Value.Clone();
            }
        }
        return "{" + string.Join(",", kept.Select(p => "\"" + p.Key + "\":" + p.Value.GetRawText())) + "}";
    }

    private static async Task<BridgeResponse> Call(OperationRegistry registry, Func<OperationDefinition, BridgeError?>? admission, string op, string argsJson)
    {
        var published = new List<BridgeResponse>();
        var dispatcher = new BridgeDispatcher(registry, published.Add, admission);
        await dispatcher.HandleAsync($$"""{"id":"r1","op":"{{op}}","args":{{argsJson}}}""", CancellationToken.None);
        return published.Single();
    }

    private static Task<BridgeResponse> Plan(string extra, string? executingSid = null) =>
        Call(Registry(executingSid), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.restorePlan", Args(extra));

    [Test]
    public void OperationRestaurationEstServieEtSaConfirmationEstUneEcriture()
    {
        var registry = Registry();
        Check.True(registry.TryGet("sauvegarder.restorePlan", out var plan), "sauvegarder.restorePlan est déclarée");
        Check.Equal(OperationClass.NetworkRead, plan!.Class, "le plan est une lecture poste");
        Check.True(plan.ServedInSimulation, "le plan est servi en démonstration");
        Check.True(registry.TryGet("sauvegarder.confirmRestorePlan", out var confirm), "sauvegarder.confirmRestorePlan est déclarée");
        Check.Equal(OperationClass.Write, confirm!.Class, "la confirmation est une écriture système");
        Check.False(confirm.ServedInSimulation, "la confirmation n'est jamais servie en démonstration");
    }

    [Test]
    public async Task PlanRenvoieUnApercuCanoniseATailleInconnue()
    {
        var response = await Plan(string.Empty);
        Check.Equal("result", response.Kind, "le plan est renvoyé");
        var preview = response.Data as RestorePlanPreview;
        Check.NotNull(preview, "le pont renvoie l'aperçu du plan de restauration");
        var plan = preview!.Plan;
        Check.True(plan.Demonstration, "le plan porte la marque démonstration du fournisseur");
        Check.Equal(1, plan.Users.Count, "un compte retenu");
        Check.Equal(LeaAccount, plan.Users[0], "le compte vient du fournisseur, pas de la page");
        Check.Equal(LeaAccount, plan.Mappings[0].Target, "sans correspondance, la cible est le compte source");
        Check.Null(plan.Mappings[0].TargetSid, "aucun profil cible : pas de SID de destination");
        Check.Equal(PriorBackupState.NonApplicable, plan.PriorBackup.State, "sans destination existante, pas de sauvegarde préalable exigée");
        Check.False(plan.PriorBackup.Verified, "le plan ne déclare jamais la sauvegarde préalable vérifiée");
        Check.True(plan.HasUnknownSpace, "le besoin d'espace reste inconnu");
        Check.True(plan.RequiresManualSpaceAcknowledgement, "la confirmation devra porter l'accusé de capacité");
        Check.Contains("Aucun réglage spécifique", plan.ConflictSummary, "sans profil cible, aucune portée n'est examinée");
        Check.Contains("aucun LoadState", plan.Notice, "le plan annonce qu'il n'a rien exécuté");
        Check.True(preview.Store.DeclaredByOperator, "l'état du magasin reste une déclaration de l'opérateur");
        Check.Contains("déclaré", preview.Store.Status, "le statut dit qu'aucun manifeste n'a été relu");
        Check.True(preview.Limitations.Any(l => l.Contains("manifeste", StringComparison.Ordinal)), "la limite du manifeste non relu est annoncée");
    }

    [Test]
    public async Task PlanRelitLeProfilCibleChezLeFournisseurEtSaSauvegardePrealable()
    {
        var response = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"],"priorRoot":"D:\\prealable","priorKeyPath":"D:\\cles\\prealable.key"}""");
        Check.Equal("result", response.Kind, "le plan est établi avec une destination existante");
        var preview = response.Data as RestorePlanPreview;
        Check.NotNull(preview, "aperçu renvoyé");
        var plan = preview!.Plan;
        Check.Equal(1, plan.Targets.Count, "un profil cible");
        Check.Equal(Lea, plan.Targets[0].Sid, "le SID cible est celui demandé");
        Check.Equal(LeaAccount, plan.Targets[0].Account, "le compte cible vient du fournisseur, pas de la page");
        Check.Equal(@"C:\Users\lea", plan.Targets[0].ProfilePath, "le chemin cible est relu, pas saisi");
        Check.True(plan.Targets[0].Loaded == false, "le chargement est relu en tri-état");
        Check.Equal(Lea, plan.Mappings[0].TargetSid, "le SID de la cible existante est rattaché à la correspondance");
        Check.Equal(PriorBackupState.Declaree, plan.PriorBackup.State, "sauvegarde préalable déclarée par l'opérateur");
        Check.False(plan.PriorBackup.Verified, "elle n'est jamais déclarée vérifiée par le plan");
        Check.Equal(@"D:\prealable", plan.PriorBackup.Root, "le dossier préalable est porté (un chemin, jamais un contenu)");
        Check.True(plan.Warnings.Any(w => w.Contains("non contrôlée par FILPRO", StringComparison.Ordinal)), "le doute sur la préalable est annoncé");
        Check.True(plan.ConflictPolicies.Count > 0, "une décision par portée affichée");
        Check.True(plan.ConflictPolicies.All(p => p.Mode == RestorePolicyMode.KeepBoth), "coexistence par défaut");
        Check.Contains("Coexistence", plan.ConflictSummary, "le résumé vient de RestoreConflictPolicies");
    }

    [Test]
    public async Task PlanRefuseProfilCibleCharge()
    {
        // Opérateur différent et correspondance valide : ce test doit atteindre la garde de chargement,
        // sans être satisfait auparavant par le refus du compte exécutant ou d'une cible hors correspondance.
        var response = await Plan($$"""{"targetSids":["{{Camille}}"],"manifestUsers":["DEMO\\camille"],"users":["DEMO\\camille"],"priorRoot":"D:\\prealable"}""",
            executingSid: "S-1-5-21-999-888-777-500");
        Check.Equal("error", response.Kind, "profil cible chargé refusé");
        Check.Equal(RestorePlan.TargetCode, response.Error?.Code, "code restore.target");
        Check.Contains("chargé", response.Error?.Message, "la raison est annoncée");
    }

    [Test]
    public async Task PlanExigeLaSauvegardePrealableSurDestinationExistante()
    {
        var response = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"]}""");
        Check.Equal("error", response.Kind, "aucune sauvegarde préalable fournie");
        Check.Equal(RestorePlan.PriorCode, response.Error?.Code, "code restore.prior");
        Check.Contains("sauvegarde préalable", response.Error?.Message, "l'exigence est annoncée");
    }

    [Test]
    public async Task PlanRefuseMagasinNonDeclarePretEtCompteAbsentDuManifeste()
    {
        var notReady = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.restorePlan",
            """{"bundle":"\\\\serveur\\partage\\filpro\\magasin","storeReady":false,"manifestUsers":["DEMO\\lea"],"users":["DEMO\\lea"]}""");
        Check.Equal(RestorePlan.StoreCode, notReady.Error?.Code, "le pont ne pose jamais Ready à la place de l'opérateur");
        Check.Contains("Source absente", notReady.Error?.Message, "le refus vient de la fonction pure");

        var outside = await Plan("""{"manifestUsers":["DEMO\\lea"],"users":["DEMO\\bob"]}""");
        Check.Equal(RestorePlan.StoreCode, outside.Error?.Code, "compte absent du manifeste refusé");
    }

    [Test]
    public async Task PlanRefuseProfilCibleInconnu()
    {
        var response = await Plan("""{"targetSids":["S-1-5-21-0-0-0-9999"]}""");
        Check.Equal(RestorePlan.TargetCode, response.Error?.Code, "un SID hors fournisseur est refusé avant le plan");
    }

    [Test]
    public async Task PlanRefuseLaFusionDeComptesVersUneDestination()
    {
        var response = await Plan("""
            {"manifestUsers":["DEMO\\lea","DEMO\\camille"],"users":["DEMO\\lea","DEMO\\camille"],"mappings":[{"source":"DEMO\\lea","target":"DEMO\\fusion"},{"source":"DEMO\\camille","target":"DEMO\\fusion"}]}
            """);
        Check.Equal(RestorePlan.MappingCode, response.Error?.Code, "code restore.mapping");
        Check.Contains("Fusion", response.Error?.Message, "la fusion est refusée par la fonction pure");
    }

    [Test]
    public async Task PlanAppliqueLesDecisionsDeConflitParPortee()
    {
        var response = await Plan("""
            {"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"],"priorRoot":"D:\\prealable","conflictPolicies":[{"id":"known:chrome","mode":"useSaved"},{"id":"known:notepadpp","mode":"keepBoth"},{"id":"known:office","mode":"keepBoth"},{"id":"known:vlc","mode":"keepCurrent"}]}
            """);
        Check.Equal("result", response.Kind, "les décisions sont admises quand elles couvrent toutes les portées");
        var plan = (response.Data as RestorePlanPreview)!.Plan;
        Check.Equal(RestorePolicyMode.UseSaved, plan.ConflictPolicies.Single(p => p.Id == "known:chrome").Mode, "la décision de la portée est reprise");
        Check.Equal(RestorePolicyMode.KeepCurrent, plan.ConflictPolicies.Single(p => p.Id == "known:vlc").Mode, "une autre portée garde sa décision");
        Check.Contains("Rétablissement demandé", plan.ConflictSummary, "le résumé reflète la première décision");
        Check.Contains("Conservation demandée", plan.ConflictSummary, "le résumé reflète la seconde décision");
    }

    [Test]
    public async Task PlanRefuseDecisionManquanteOuPorteeInconnue()
    {
        var missing = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"],"priorRoot":"D:\\prealable","conflictPolicies":[{"id":"known:chrome","mode":"keepBoth"}]}""");
        Check.Equal(RestoreConflictPolicies.ConflictCode, missing.Error?.Code, "une décision doit couvrir toutes les portées affichées");
        Check.Contains("manquante", missing.Error?.Message, "le motif vient de RestoreConflictPolicies");

        var unknown = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"],"priorRoot":"D:\\prealable","conflictPolicies":[{"id":"known:absent","mode":"keepBoth"}]}""");
        Check.Equal(RestoreConflictPolicies.ConflictCode, unknown.Error?.Code, "une politique sans groupe sauvegardé est refusée");
    }

    [Test]
    public async Task PlanRefuseArgumentsInvalides()
    {
        var noBundle = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.restorePlan",
            """{"storeReady":true,"manifestUsers":["DEMO\\lea"],"users":["DEMO\\lea"]}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, noBundle.Error?.Code, "bundle obligatoire");

        var noReady = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.restorePlan",
            """{"bundle":"\\\\serveur\\partage\\filpro\\magasin","manifestUsers":["DEMO\\lea"],"users":["DEMO\\lea"]}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, noReady.Error?.Code, "la déclaration d'exploitabilité est explicite");

        var emptyUsers = await Plan("""{"users":[]}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, emptyUsers.Error?.Code, "sélection vide refusée par le schéma d'arguments");

        var badMode = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001"],"priorRoot":"D:\\prealable","conflictPolicies":[{"id":"known:chrome","mode":"ecraseTout"}]}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, badMode.Error?.Code, "mode de politique inconnu refusé");

        var manyTargets = await Plan("""{"targetSids":["S-1-5-21-111222333-444555666-777888999-1001","S-1-5-21-111222333-444555666-777888999-1000"],"priorRoot":"D:\\prealable"}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, manyTargets.Error?.Code, "la préparation ne lit pas les portées de tous les profils sans les nommer");
    }

    [Test]
    public async Task ConfirmationRefuseeParLaGardeAvantLeGestionnaire()
    {
        var demo = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.confirmRestorePlan", Confirmation(true, true, true));
        Check.Equal("error", demo.Kind, "la confirmation est refusée en démonstration");
        Check.Equal(FilproErrors.SimulationReadOnly, demo.Error?.Code, "refusée par la garde, jamais exécutée");
        Check.Null(demo.Data, "aucune donnée, aucune préparation");

        var poste = await Call(Registry(), AdmissionGuards.For(AppMode.Poste), "sauvegarder.confirmRestorePlan", Confirmation(true, true, true));
        Check.Equal(FilproErrors.WriteUnavailable, poste.Error?.Code, "refusée en poste réel (moteur absent)");
        Check.Null(poste.Data, "aucune donnée, aucune restauration");
    }

    [Test]
    public async Task GestionnaireDeConfirmationExigeLesTroisAccuses()
    {
        // Sans garde d'admission, on atteint le gestionnaire : il recontrôle l'identifiant, les trois accusés, puis refuse l'exécution.
        var badId = await Call(Registry(), admission: null, "sauvegarder.confirmRestorePlan",
            """{"planId":"0123","acknowledgeCapacity":true,"acknowledgePriorBackup":true,"acknowledgeIdentityReview":true}""");
        Check.Equal(RestorePlan.PlanCode, badId.Error?.Code, "identifiant de plan exigé");

        var noCapacity = await Call(Registry(), admission: null, "sauvegarder.confirmRestorePlan", Confirmation(false, true, true));
        Check.Equal(RestorePlan.PlanCode, noCapacity.Error?.Code, "accusé de capacité obligatoire");

        var noPrior = await Call(Registry(), admission: null, "sauvegarder.confirmRestorePlan", Confirmation(true, false, true));
        Check.Equal(RestorePlan.PriorCode, noPrior.Error?.Code, "sauvegarde préalable à confirmer par l'opérateur");

        var noIdentity = await Call(Registry(), admission: null, "sauvegarder.confirmRestorePlan", Confirmation(true, true, false));
        Check.Equal(RestorePlan.TargetCode, noIdentity.Error?.Code, "revue des correspondances à confirmer");

        var acknowledged = await Call(Registry(), admission: null, "sauvegarder.confirmRestorePlan", Confirmation(true, true, true));
        Check.Equal(FilproErrors.FeatureUnavailable, acknowledged.Error?.Code, "moteur absent : rien n'est exécuté même confirmé");
        Check.Null(acknowledged.Data, "aucune donnée renvoyée par la confirmation");
    }

    private static string Confirmation(bool capacity, bool prior, bool identity) =>
        $$"""{"planId":"{{PlanId}}","acknowledgeCapacity":{{(capacity ? "true" : "false")}},"acknowledgePriorBackup":{{(prior ? "true" : "false")}},"acknowledgeIdentityReview":{{(identity ? "true" : "false")}}}""";

    private sealed class PosteWithExecutingAccount(IFilproPosteSource inner, string sid) : IFilproPosteSource
    {
        public bool Demonstration => inner.Demonstration;
        public string ReadCurrentAccountSid(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sid;
        }
        public ProfileInventory ReadProfiles(CancellationToken cancellationToken) => inner.ReadProfiles(cancellationToken);
        public SoftwareInventory ReadSoftware(string profileSid, SoftwareQuery query, CancellationToken cancellationToken) =>
            inner.ReadSoftware(profileSid, query, cancellationToken);
        public LastSelection? ReadLastSelection(string profileSid, CancellationToken cancellationToken) =>
            inner.ReadLastSelection(profileSid, cancellationToken);
        public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken) =>
            inner.SaveLastSelection(selection, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
