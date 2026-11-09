using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>Fournisseur de démonstration : déterminisme, honnêteté (aucune action réelle), compteurs conformes.</summary>
internal sealed class SimulationTests
{
    private static TimeProvider FixedClock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public void MemeGraineProduitLeMemeAccueil()
    {
        var a = new FilproSimulationSource(new SimulationOptions(Seed: 4242), FixedClock).BuildAccueil();
        var b = new FilproSimulationSource(new SimulationOptions(Seed: 4242), FixedClock).BuildAccueil();
        Check.Equal(a.Profiles.Count, b.Profiles.Count, "même nombre de profils");
        Check.Equal(a.Profiles[0].Name, b.Profiles[0].Name, "mêmes noms de profils");
        Check.Equal(a.Backups[0].Id, b.Backups[0].Id, "mêmes identifiants de sauvegarde");
        Check.Equal(a.Journal[0].Message, b.Journal[0].Message, "mêmes lignes de journal");
    }

    [Test]
    public void LeSnapshotDeclareLaDemonstrationEtAucuneActionReelle()
    {
        var s = new FilproSimulationSource(SimulationOptions.Default, FixedClock).BuildAccueil();
        Check.True(s.Demonstration, "le snapshot est marqué démonstration");
        Check.Equal(ModeTexts.SimulationLabel, s.Mode, "le mode annoncé est Démonstration");
        Check.False(s.Availability.RealSystemActions, "aucune action système réelle n’est revendiquée");
    }

    [Test]
    public void LesCompteursSuiventLesReglages()
    {
        var options = new SimulationOptions(Seed: 7, ProfileCount: 3, BackupCount: 2, TaskCount: 4, JournalCount: 5);
        var s = new FilproSimulationSource(options, FixedClock).BuildAccueil();
        Check.Equal(3, s.Profiles.Count, "trois profils demandés");
        Check.Equal(2, s.Backups.Count, "deux sauvegardes demandées");
        Check.Equal(4, s.Tasks.Count, "quatre tâches demandées");
        Check.Equal(5, s.Journal.Count, "cinq lignes de journal demandées");
    }

    [Test]
    public void LesCompteursHorsBornesSontRefuses()
    {
        Check.Throws<ArgumentOutOfRangeException>(
            () => new FilproSimulationSource(new SimulationOptions(ProfileCount: 500), FixedClock).BuildAccueil(),
            "un compteur déraisonnable doit être refusé");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
