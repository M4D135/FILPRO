using System.Collections.Immutable;
using Filpro.Bridge;
using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>
/// Parcours de confirmation du plan de sauvegarde branché sur le pont : <c>sauvegarder.plan</c> (NetworkRead servie)
/// renvoie l'aperçu canonisé à taille inconnue et refusent une sélection invalide ; <c>sauvegarder.confirmPlan</c>
/// (Write) est refusé par la garde d'admission avant toute exécution, et son gestionnaire exige l'accusé de capacité.
/// Aucune écriture, aucune mesure, aucun montage de ruche : le fournisseur est le monde fictif de démonstration.
/// </summary>
internal sealed class SauvegarderPlanTests
{
    private const string Lea = "S-1-5-21-111222333-444555666-777888999-1001";   // non chargé
    private const string Camille = "S-1-5-21-111222333-444555666-777888999-1000"; // chargé (session active)
    private const string PlanId = "0123456789abcdef0123456789abcdef";
    private const string Destination = """\\\\serveur\\partage\\filpro""";

    private static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    private static EngineContext Context()
    {
        var decision = new ModeDecision(AppMode.Simulation, ModeReason.DefaultDemonstration, ModeTexts.DetailDefault, ModeTexts.Banner, ImmutableArray<string>.Empty);
        var poste = new SimulationPosteSource(SimulationOptions.Default, Clock);
        return new EngineContext(decision, simulation: null, poste, Clock, SilentTechnicalJournal.Instance);
    }

    private static OperationRegistry Registry() => new(new SauvegarderModule().CreateOperations(Context()));

    private static async Task<BridgeResponse> Call(OperationRegistry registry, Func<OperationDefinition, BridgeError?>? admission, string op, string argsJson)
    {
        var published = new List<BridgeResponse>();
        var dispatcher = new BridgeDispatcher(registry, published.Add, admission);
        await dispatcher.HandleAsync($$"""{"id":"r1","op":"{{op}}","args":{{argsJson}}}""", CancellationToken.None);
        return published.Single();
    }

    [Test]
    public void OperationPlanEstServieEnDemonstrationEtConfirmationEstUneEcriture()
    {
        var registry = Registry();
        Check.True(registry.TryGet("sauvegarder.plan", out var plan), "sauvegarder.plan est déclarée");
        Check.Equal(OperationClass.NetworkRead, plan!.Class, "le plan est une lecture poste");
        Check.True(plan.ServedInSimulation, "le plan est servi en démonstration");
        Check.True(registry.TryGet("sauvegarder.confirmPlan", out var confirm), "sauvegarder.confirmPlan est déclarée");
        Check.Equal(OperationClass.Write, confirm!.Class, "la confirmation est une écriture système");
        Check.False(confirm.ServedInSimulation, "la confirmation n'est jamais servie en démonstration");
    }

    [Test]
    public async Task PlanRenvoieUnApercuCanoniseATailleInconnue()
    {
        var response = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":["{{Lea}}"],"destination":"{{Destination}}"}""");
        Check.Equal("result", response.Kind, "le plan est renvoyé");
        var plan = response.Data as BackupPlanReview;
        Check.NotNull(plan, "le pont renvoie le plan canonisé");
        Check.True(plan!.Demonstration, "le plan porte la marque démonstration du fournisseur");
        Check.Equal(1, plan.Profiles.Count, "un profil retenu");
        Check.Equal("DEMO\\lea", plan.Profiles[0].Account, "le compte vient du fournisseur, pas de la page");
        Check.Equal(BackupSpaceState.Inconnu, plan.Space.State, "le besoin d'espace reste inconnu");
        Check.Null(plan.Space.StoreBytes, "aucun octet inventé");
        Check.True(plan.HasUnknownSpace, "la taille inconnue est déclarée");
        Check.True(plan.RequiresManualSpaceAcknowledgement, "l'accusé de capacité est exigé à la confirmation");
        Check.True(plan.Exclusions.Any(e => e.Origin == BackupExclusionOrigin.Regle && e.Kind == BackupExclusionKind.Ruche), "les ruches sont annoncées");
        Check.Contains("serveur", plan.Destination, "la destination est reprise et normalisée");
    }

    [Test]
    public async Task PlanRefuseProfilChargeSansVss()
    {
        var blocked = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":["{{Camille}}"],"destination":"{{Destination}}"}""");
        Check.Equal("error", blocked.Kind, "profil chargé sans VSS refusé");
        Check.Equal(BackupPlan.SelectionCode, blocked.Error?.Code, "code backup.selection");

        var ok = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":["{{Camille}}"],"destination":"{{Destination}}","useVss":true}""");
        Check.Equal("result", ok.Kind, "avec VSS le plan est établi");
        var plan = ok.Data as BackupPlanReview;
        Check.True(plan!.Warnings.Any(w => w.Contains("VSS", StringComparison.Ordinal)), "l'avertissement annonce le contrôle du moteur");
    }

    [Test]
    public async Task PlanRefuseProfilInconnuEtArgumentsInvalides()
    {
        var unknown = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":["S-1-5-21-0-0-0-9999"],"destination":"{{Destination}}"}""");
        Check.Equal(BackupPlan.SelectionCode, unknown.Error?.Code, "un SID hors fournisseur est refusé");

        var empty = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":[],"destination":"{{Destination}}"}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, empty.Error?.Code, "sélection vide refusée par le schéma d'arguments");

        var noDest = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":["{{Lea}}"]}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, noDest.Error?.Code, "destination obligatoire");

        var badType = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.plan", $$"""{"sids":"{{Lea}}","destination":"{{Destination}}"}""");
        Check.Equal(FilproErrors.ArgumentsInvalid, badType.Error?.Code, "sids doit être un tableau");
    }

    [Test]
    public async Task ConfirmationRefuseeParLaGardeAvantLeGestionnaire()
    {
        var demo = await Call(Registry(), AdmissionGuards.For(AppMode.Simulation), "sauvegarder.confirmPlan", $$"""{"planId":"{{PlanId}}","acknowledgeCapacity":true}""");
        Check.Equal("error", demo.Kind, "la confirmation est refusée en démonstration");
        Check.Equal(FilproErrors.SimulationReadOnly, demo.Error?.Code, "refusée par la garde, jamais exécutée");
        Check.Null(demo.Data, "aucune donnée, aucune préparation");

        var poste = await Call(Registry(), AdmissionGuards.For(AppMode.Poste), "sauvegarder.confirmPlan", $$"""{"planId":"{{PlanId}}","acknowledgeCapacity":true}""");
        Check.Equal(FilproErrors.WriteUnavailable, poste.Error?.Code, "refusée en poste réel (moteur absent)");
    }

    [Test]
    public async Task GestionnaireDeConfirmationExigeLAccuseDeCapacite()
    {
        // Sans garde d'admission, on atteint le gestionnaire : il refuse d'abord l'absence d'accusé, puis l'exécution elle-même.
        var noAck = await Call(Registry(), admission: null, "sauvegarder.confirmPlan", $$"""{"planId":"{{PlanId}}","acknowledgeCapacity":false}""");
        Check.Equal(BackupPlan.PlanCode, noAck.Error?.Code, "accusé de capacité obligatoire");

        var acknowledged = await Call(Registry(), admission: null, "sauvegarder.confirmPlan", $$"""{"planId":"{{PlanId}}","acknowledgeCapacity":true}""");
        Check.Equal(FilproErrors.FeatureUnavailable, acknowledged.Error?.Code, "moteur absent : rien n'est exécuté même confirmé");
        Check.Null(acknowledged.Data, "aucune donnée renvoyée par la confirmation");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
