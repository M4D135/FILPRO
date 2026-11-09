using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>Fournisseurs poste : le fictif est déterministe et honnête ; le natif refuse d'inventer hors Windows.</summary>
internal sealed class PosteSourceTests
{
    private static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public void DemonstrationTrieEtSignaleLesEtatsInconnus()
    {
        var src = new SimulationPosteSource(SimulationOptions.Default, Clock);
        var inv = src.ReadProfiles(CancellationToken.None);
        Check.True(src.Demonstration, "le fournisseur se déclare démonstration");
        Check.Equal(SessionState.Active, inv.Profiles[0].Session.State, "la session active est en tête");
        var karim = inv.Profiles.Single(p => p.Sid.EndsWith("-1002", StringComparison.Ordinal));
        Check.Null(karim.Loaded, "chargement non prouvé rendu null, jamais false");
        var entra = inv.Profiles.Single(p => p.IdentityKind == ProfileIdentityKind.Entra);
        Check.False(entra.MigrationSupported, "profil Entra hors périmètre USMT");
    }

    [Test]
    public void DemonstrationRecommandeFichiersEtClesEnsemble()
    {
        var src = new SimulationPosteSource(SimulationOptions.Default, Clock);
        var inv = src.ReadSoftware("S-1-5-21-111222333-444555666-777888999-1001", SoftwareQuery.Empty, CancellationToken.None);
        Check.True(inv.Groups.Any(g => g.Id == "known:chrome" && g.Selected == true), "Chrome recommandé coché");
        Check.True(inv.Groups.Any(g => g.Id == "known:office" && g.Selected == false), "Office non recommandé décoché");
    }

    [Test]
    public void DemonstrationEstDeterministe()
    {
        const string sid = "S-1-5-21-111222333-444555666-777888999-1001";
        var a = new SimulationPosteSource(SimulationOptions.Default, Clock).ReadSoftware(sid, SoftwareQuery.Empty, CancellationToken.None);
        var b = new SimulationPosteSource(SimulationOptions.Default, Clock).ReadSoftware(sid, SoftwareQuery.Empty, CancellationToken.None);
        Check.Equal(string.Join(",", a.Groups.Select(g => g.Id)), string.Join(",", b.Groups.Select(g => g.Id)), "mêmes groupes pour une même graine");
        Check.Equal(string.Join(",", a.Groups.SelectMany(g => g.Files).Select(f => f.Size)),
            string.Join(",", b.Groups.SelectMany(g => g.Files).Select(f => f.Size)), "mêmes tailles fictives pour une même graine");
    }

    [Test]
    public void DerniereSelectionUniquementPourLeCompteEnregistre()
    {
        var src = new SimulationPosteSource(SimulationOptions.Default, Clock);
        Check.NotNull(src.ReadLastSelection("S-1-5-21-111222333-444555666-777888999-1000", CancellationToken.None), "le premier compte a une dernière sélection");
        Check.Null(src.ReadLastSelection("S-1-5-21-111222333-444555666-777888999-1001", CancellationToken.None), "les autres n’en ont aucune, jamais inventée");
    }

    [Test]
    public void LecteurNatifHorsWindowsAnnonceIndisponible()
    {
        if (OperatingSystem.IsWindows())
        {
            Check.Skip("Contrôle du refus hors Windows ; non applicable sur Windows.");
        }
        var inv = new WindowsPosteSource(Clock).ReadProfiles(CancellationToken.None);
        Check.Equal(SessionReadStatus.Unavailable, inv.SessionReadStatus, "hors Windows : sessions indisponibles");
        Check.Equal(0, inv.Profiles.Count, "aucun profil inventé");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
