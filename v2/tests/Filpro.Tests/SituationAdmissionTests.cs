using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Admission selon la situation de l'affichage : sous un autre compte ou un compte indéterminé, les mises à jour du code de
/// FILPRO sont refusées et, tant que la présence n'est pas armée et libre, seuls la lecture locale et le suivi restent
/// admis, sans changement de session. Même compte et démonstration : rien ne change.
/// </summary>
internal sealed class SituationAdmissionTests
{
    private static readonly HashSet<string> CodeOperations =
        ["updates.inspect", "updates.install", "updates.prepare", "updates.rollback", "updates.saveSource"];

    private static EngineContext Poste(HostSituation? situation, IOperatorPresence? presence = null)
    {
        var context = new EngineContext(ModeResolver.Resolve(["--poste"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance);
        context.Register<IFilproExecutionService>(new PrivilegePolicyTests.CapableExecutor());
        context.Register<IFilproUpdateService>(new PrivilegePolicyTests.NativeUpdates());
        if (situation is not null) context.Register(situation);
        if (presence is not null) context.Register(presence);
        return context;
    }

    private static OperationRegistry Registry(EngineContext context) =>
        OperationModules.CreateRegistry(context, OperationModules.Discover(OperationModules.LoadFilproAssemblies()));

    private static List<OperationDefinition> Definitions()
    {
        var registry = Registry(Poste(TestSituations.SameAccount));
        return [.. registry.Names.Order(StringComparer.Ordinal)
            .Select(name => registry.TryGet(name, out var definition) ? definition : throw new InvalidOperationException(name))];
    }

    private static OperatorPresence Presence(bool armed, bool latched)
    {
        var presence = new OperatorPresence(() => Environment.TickCount64, TimeSpan.Zero);
        if (armed) presence.Arm();
        if (latched) presence.Latch(PresenceReason.SessionLocked);
        return presence;
    }

    private static string? Code(EngineContext context, OperationDefinition definition) => AdmissionGuards.For(context)(definition)?.Code;

    private static bool Following(OperationDefinition definition) =>
        definition.Class is OperationClass.Local or OperationClass.LocalRead && !definition.Name.StartsWith("session.demo", StringComparison.Ordinal);

    [Test]
    public void LesMisesAJourSontRefuseesHorsDuCompteEleve()
    {
        var definitions = Definitions();
        Check.Equal(CodeOperations.Count, definitions.Count(definition => CodeOperations.Contains(definition.Name)), "toutes déclarées");
        foreach (var (situation, label) in new[] { (TestSituations.CrossAccount, "autre compte"), (TestSituations.Unknown, "indéterminé"), ((HostSituation?)null, "absente") })
        {
            var context = Poste(situation, Presence(armed: true, latched: false));
            foreach (var definition in definitions.Where(definition => CodeOperations.Contains(definition.Name)))
                Check.Equal(FilproErrors.SessionCrossAccount, Code(context, definition), label + " : " + definition.Name);
        }
        var same = Poste(TestSituations.SameAccount);
        foreach (var definition in definitions.Where(definition => CodeOperations.Contains(definition.Name)))
            Check.Null(Code(same, definition), "même compte : " + definition.Name);
    }

    [Test]
    public void LesGardesExistantesGardentLaPriorite()
    {
        var definitions = Definitions();
        var closing = Poste(TestSituations.CrossAccount);
        closing.BeginClosing();
        foreach (var definition in definitions)
            Check.Equal("session.closing", Code(closing, definition), "fermeture d'abord : " + definition.Name);
        var demo = new EngineContext(ModeResolver.Resolve(["--simulation"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance);
        demo.Register(TestSituations.CrossAccount);
        foreach (var definition in definitions)
            Check.Equal(AdmissionGuards.ReadOnlySimulation(definition)?.Code, Code(demo, definition), "démonstration inchangée : " + definition.Name);
        var incapable = new EngineContext(ModeResolver.Resolve(["--poste"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance);
        incapable.Register(TestSituations.CrossAccount);
        incapable.Register<IOperatorPresence>(Presence(armed: true, latched: true));
        foreach (var definition in definitions.Where(definition => definition.Class == OperationClass.Write))
            Check.Equal(FilproErrors.WriteUnavailable, Code(incapable, definition), "capacité d'abord : " + definition.Name);
    }

    [Test]
    public void VerrouilleSeulsLaLectureLocaleEtLeSuiviSontAdmis()
    {
        var definitions = Definitions();
        foreach (var (presence, label) in new[] { ((IOperatorPresence?)null, "présence absente"), (Presence(armed: false, latched: false), "non armée"),
                     (Presence(armed: true, latched: true), "verrouillée") })
        {
            foreach (var situation in new[] { TestSituations.CrossAccount, TestSituations.Unknown })
            {
                var context = Poste(situation, presence);
                foreach (var definition in definitions)
                {
                    var expected = CodeOperations.Contains(definition.Name) ? FilproErrors.SessionCrossAccount
                        : Following(definition) ? null : FilproErrors.PresenceLocked;
                    Check.Equal(expected, Code(context, definition), label + ", " + situation.Kind + " : " + definition.Name);
                }
            }
        }
        var locked = Poste(TestSituations.CrossAccount, Presence(armed: true, latched: true));
        foreach (var name in new[] { "session.info", "session.ready", "session.confirmClose", "operations.list", "operations.get", "operations.cancel" })
            Check.Null(Code(locked, definitions.Single(definition => definition.Name == name)), "suivi et fermeture : " + name);
        foreach (var name in new[] { "session.demoEnter", "session.demoExit", "session.demoReset", "session.choosePath", "sauvegarder.plan", "configuration.save" })
            Check.Equal(FilproErrors.PresenceLocked, Code(locked, definitions.Single(definition => definition.Name == name)), "refusé une fois verrouillé : " + name);
    }

    [Test]
    public void ArmeeEtLibreSeulesLesMisesAJourSontRefusees()
    {
        var context = Poste(TestSituations.CrossAccount, Presence(armed: true, latched: false));
        foreach (var definition in Definitions())
            Check.Equal(CodeOperations.Contains(definition.Name) ? FilproErrors.SessionCrossAccount : null, Code(context, definition), definition.Name);
    }

    [Test]
    public void LeMemeCompteIgnoreLaPresence()
    {
        foreach (var presence in new IOperatorPresence?[] { null, Presence(armed: true, latched: true) })
        {
            var context = Poste(TestSituations.SameAccount, presence);
            foreach (var definition in Definitions())
                Check.Null(Code(context, definition), "même compte : " + definition.Name);
        }
    }

    [Test]
    public async Task LaPageRecoitLesIndicationsDeLaSituation()
    {
        static async Task<IReadOnlyDictionary<string, bool>> Capabilities(EngineContext context)
        {
            Check.True(Registry(context).TryGet("session.info", out var definition), "session.info déclarée");
            var info = (SessionInfo)(await definition!.Handler(JsonSerializer.SerializeToElement(new { }), new BridgeContext(_ => { }, "r1", "session.info"),
                CancellationToken.None))!;
            return info.Capabilities!;
        }
        var same = await Capabilities(Poste(TestSituations.SameAccount));
        Check.False(same["crossAccount"], "même compte");
        Check.True(same["codeUpdates"], "mises à jour proposées");
        Check.False(same["presenceLocked"], "jamais verrouillé");
        Check.False(same.ContainsKey("packModifiable"), "aucun avertissement de dossier modifiable");
        var presence = Presence(armed: true, latched: false);
        var across = Poste(TestSituations.CrossAccount, presence);
        var free = await Capabilities(across);
        Check.True(free["crossAccount"], "autre compte");
        Check.False(free["codeUpdates"], "mises à jour indisponibles");
        Check.False(free["presenceLocked"], "présence armée et libre");
        presence.Latch(PresenceReason.Idle);
        var locked = await Capabilities(across);
        Check.True(locked["presenceLocked"], "verrouillé");
        var demo = new EngineContext(ModeResolver.Resolve(["--simulation"], null), null, null, TimeProvider.System, SilentTechnicalJournal.Instance);
        demo.Register(TestSituations.CrossAccount);
        var simulated = await Capabilities(demo);
        Check.False(simulated["crossAccount"], "démonstration : aucune situation");
        Check.True(simulated["codeUpdates"], "démonstration : mises à jour fictives");
    }
}
