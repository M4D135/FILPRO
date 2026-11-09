using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Pont fermé : validation des noms, admission par mode, répartiteur (inconnu, malformé, trop long, échec attendu).</summary>
internal sealed class BridgeTests
{
    private static JsonElement Args(string json = "{}") => JsonDocument.Parse(json).RootElement.Clone();

    private static OperationDefinition Echo => new("test.echo", OperationClass.LocalRead, (a, c, t) => Task.FromResult<object?>("ok"), "écho");
    private static OperationDefinition Fail => new("test.fail", OperationClass.LocalRead, async (a, c, t) => { await Task.Yield(); throw new FilproOperationException("test.fail", "échec attendu"); }, "échec");
    private static OperationDefinition Write => new("test.write", OperationClass.Write, (a, c, t) => Task.FromResult<object?>("jamais"), "écriture");
    private static OperationDefinition Network => new("test.net", OperationClass.NetworkRead, (a, c, t) => Task.FromResult<object?>("jamais"), "lecture réseau");
    private static OperationDefinition NetworkServed => new("test.netserved", OperationClass.NetworkRead, (a, c, t) => Task.FromResult<object?>("lu"), "servi", ServedInSimulation: true);

    private static (BridgeDispatcher Dispatcher, List<BridgeResponse> Published) Harness(OperationRegistry registry, Func<OperationDefinition, BridgeError?>? admission = null)
    {
        var published = new List<BridgeResponse>();
        var dispatcher = new BridgeDispatcher(registry, published.Add, admission);
        return (dispatcher, published);
    }

    [Test]
    public void NomsValidesEtInvalides()
    {
        Check.True(OperationRegistry.IsValidName("accueil.summary"), "accueil.summary est valide");
        Check.True(OperationRegistry.IsValidName("a.b"), "a.b est valide");
        Check.False(OperationRegistry.IsValidName("Session.Info"), "majuscule initiale refusée");
        Check.False(OperationRegistry.IsValidName("single"), "un seul segment refusé");
        Check.False(OperationRegistry.IsValidName(""), "vide refusé");
        Check.False(OperationRegistry.IsValidName(null), "nul refusé");
    }

    [Test]
    public void NomDupliqueArreteLeRegistre()
    {
        Check.Throws<ArgumentException>(
            () => new OperationRegistry([Echo, Echo]),
            "un nom dupliqué doit arrêter la construction du registre");
    }

    [Test]
    public async Task OperationInconnueEstRefusee()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Echo]));
        await dispatcher.HandleAsync("""{"id":"r1","op":"test.missing","args":{}}""", CancellationToken.None);
        Check.Equal(1, published.Count, "une réponse est renvoyée");
        Check.Equal("error", published[0].Kind, "réponse d’erreur");
        Check.Equal("operation.unknown", published[0].Error?.Code, "code operation.unknown");
    }

    [Test]
    public async Task OperationConnueRenvoieUnResultat()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Echo]));
        await dispatcher.HandleAsync("""{"id":"r1","op":"test.echo","args":{}}""", CancellationToken.None);
        Check.Equal("result", published[0].Kind, "réponse de résultat");
        Check.Equal("ok", published[0].Data, "données renvoyées");
    }

    [Test]
    public async Task MessageMalformeEstRefuseSansFuire()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Echo]));
        await dispatcher.HandleAsync("{ ceci n'est pas du json ", CancellationToken.None);
        Check.Equal("message.json", published[0].Error?.Code, "code message.json");
    }

    [Test]
    public async Task MessageTropLongEstRefuse()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Echo]));
        var huge = new string('x', BridgeDispatcher.MaxMessageLength + 1);
        await dispatcher.HandleAsync(huge, CancellationToken.None);
        Check.Equal("message.size", published[0].Error?.Code, "code message.size");
    }

    [Test]
    public async Task EchecAttenduDeLOperationEstRenvoye()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Fail]));
        await dispatcher.HandleAsync("""{"id":"r1","op":"test.fail","args":{}}""", CancellationToken.None);
        Check.Equal("error", published[0].Kind, "réponse d’erreur");
        Check.Equal("test.fail", published[0].Error?.Code, "code de l’échec attendu conservé");
    }

    [Test]
    public void AdmissionDemonstrationRefuseEcritureEtReseauNonServi()
    {
        Check.Null(AdmissionGuards.ReadOnlySimulation(Echo), "LocalRead admise en démonstration");
        Check.Null(AdmissionGuards.ReadOnlySimulation(NetworkServed), "NetworkRead servie admise en démonstration");
        Check.Equal(FilproErrors.SimulationUnavailable, AdmissionGuards.ReadOnlySimulation(Network)?.Code, "NetworkRead non servie refusée en démonstration");
        Check.Equal(FilproErrors.SimulationReadOnly, AdmissionGuards.ReadOnlySimulation(Write)?.Code, "Write refusée en démonstration");
    }

    [Test]
    public void AdmissionPosteRefuseEcriture()
    {
        Check.Null(AdmissionGuards.ReadOnlyPoste(Network), "NetworkRead admise en poste réel");
        Check.Equal(FilproErrors.WriteUnavailable, AdmissionGuards.ReadOnlyPoste(Write)?.Code, "Write refusée en poste réel (moteur non câblé)");
    }

    [Test]
    public async Task AdmissionRefuseAvantLeGestionnaire()
    {
        var (dispatcher, published) = Harness(new OperationRegistry([Write]), AdmissionGuards.For(AppMode.Simulation));
        await dispatcher.HandleAsync("""{"id":"r1","op":"test.write","args":{}}""", CancellationToken.None);
        Check.Equal("error", published[0].Kind, "la Write est refusée");
        Check.Equal(FilproErrors.SimulationReadOnly, published[0].Error?.Code, "refusée par la garde d’admission, jamais exécutée");
    }

    [Test]
    public void NomDEcranValide()
    {
        Check.True(OperationModules.IsValidScreen("accueil"), "accueil est un écran valide");
        Check.False(OperationModules.IsValidScreen("Accueil"), "majuscule initiale refusée pour un écran");
        Check.False(OperationModules.IsValidScreen(""), "écran vide refusé");
    }
}
