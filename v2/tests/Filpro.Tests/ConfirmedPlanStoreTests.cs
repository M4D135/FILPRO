using Filpro.Bridge;
using Filpro.Core;

namespace Filpro.Tests;

internal sealed class ConfirmedPlanStoreTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed record MutablePlan(List<string> Profiles);
    private const string Id = "00112233445566778899aabbccddeeff";

    [Test]
    public void UnApercuExpireNePeutPasEtreExecute()
    {
        var clock = new Clock(); var store = new ConfirmedPlanStore(clock);
        store.Add(Id, new MutablePlan(["profil-a"])); clock.Now += ConfirmedPlanStore.Lifetime;
        Check.Equal("plan.expired", Check.Throws<FilproOperationException>(() => store.Take<MutablePlan>(Id), "le délai est exact").Code, "expiration");
    }
    [Test]
    public async Task UneDoubleConfirmationConcurrenteNeConsommeLePlanQuUneFois()
    {
        var store = new ConfirmedPlanStore(new Clock()); store.Add(Id, new MutablePlan(["profil-a"]));
        var starts = 0;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            try { store.Take<MutablePlan>(Id); Interlocked.Increment(ref starts); }
            catch (FilproOperationException ex) { Check.Equal("plan.expired", ex.Code, "rejeu refusé"); }
        })));
        Check.Equal(1, starts, "un seul démarrage possible");
    }
    [Test]
    public void UneListeModifieeApresApercuInvalideLaConfirmation()
    {
        var store = new ConfirmedPlanStore(new Clock()); var plan = new MutablePlan(["profil-a"]);
        store.Add(Id, plan); plan.Profiles.Add("profil-non-confirme");
        Check.Equal("plan.expired", Check.Throws<FilproOperationException>(() => store.Take<MutablePlan>(Id), "empreinte relue").Code, "plan modifié");
    }
    [Test]
    public void TypeIdentifiantEtCapaciteSontFermes()
    {
        var clock = new Clock(); var store = new ConfirmedPlanStore(clock);
        Check.Throws<FilproOperationException>(() => store.Add("../profil", new MutablePlan([])), "aucun chemin comme id");
        for (var i = 0; i < ConfirmedPlanStore.MaximumPlans; i++) store.Add(i.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), new MutablePlan([]));
        Check.Equal("plan.capacity", Check.Throws<FilproOperationException>(() => store.Add(Id, new MutablePlan([])), "borne mémoire").Code, "limite");
        clock.Now += ConfirmedPlanStore.Lifetime;
        store.Add(Id, new MutablePlan([]));
        Check.Throws<FilproOperationException>(() => store.Take<BackupPlanReview>(Id), "une confirmation ne change pas le type du plan");
        Check.Throws<FilproOperationException>(() => store.Take<MutablePlan>(Id), "le mauvais type ne permet pas le rejeu");
    }
}
