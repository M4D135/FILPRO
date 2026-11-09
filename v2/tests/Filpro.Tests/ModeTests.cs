using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Décision de mode : options de ligne de commande, variable d'environnement, repli honnête en démonstration.</summary>
internal sealed class ModeTests
{
    [Test]
    public void ArgumentSimulationChoisitDemonstration()
    {
        var d = ModeResolver.Resolve(["--simulation"], null);
        Check.Equal(AppMode.Simulation, d.Mode, "--simulation doit donner la démonstration");
        Check.Equal(ModeReason.Argument, d.Reason, "raison = argument");
        Check.NotNull(d.Banner, "la démonstration affiche un bandeau");
    }

    [Test]
    public void Variable1ChoisitDemonstration()
    {
        var d = ModeResolver.Resolve([], "1");
        Check.Equal(AppMode.Simulation, d.Mode, "FILPRO_SIMULATION=1 doit donner la démonstration");
        Check.Equal(ModeReason.Environment, d.Reason, "raison = environnement");
    }

    [Test]
    public void Variable0DemandePosteReel()
    {
        var d = ModeResolver.Resolve([], "0");
        Check.Equal(AppMode.Poste, d.Mode, "FILPRO_SIMULATION=0 doit demander le poste réel");
        Check.Equal(ModeReason.EnvironmentOptOut, d.Reason, "raison = opt-out");
        Check.Null(d.Banner, "le poste réel n’a pas de bandeau de démonstration");
    }

    [Test]
    public void SansChoixRetombeHonnetementEnDemonstration()
    {
        var d = ModeResolver.Resolve([], null);
        Check.Equal(AppMode.Simulation, d.Mode, "sans choix : démonstration par défaut (moteur réel non câblé)");
        Check.Equal(ModeReason.DefaultDemonstration, d.Reason, "raison = démonstration par défaut");
    }

    [Test]
    public void VariableInvalideAjouteUnAvertissement()
    {
        var d = ModeResolver.Resolve([], "oui");
        Check.Equal(AppMode.Simulation, d.Mode, "valeur non reconnue : démonstration");
        Check.True(d.Warnings.Length == 1, "une valeur invalide de la variable produit exactement un avertissement");
    }

    [Test]
    public void ArgumentsInconnusSontComptesSansEtreRecopies()
    {
        var d = ModeResolver.Resolve(["--secret=motdepasse", "--autre"], "1");
        Check.Equal(AppMode.Simulation, d.Mode, "la variable décide malgré des arguments inconnus");
        Check.True(d.Warnings.Length == 1, "les arguments ignorés sont comptés en un avertissement");
        Check.False(string.Join("|", d.Warnings).Contains("motdepasse", StringComparison.Ordinal), "aucun argument n’est recopié dans le journal");
    }

    [Test]
    public void PackProductionDemarreSurPosteMaisGardeLeChoixExpliciteDeDemonstration()
    {
        Check.Equal(AppMode.Poste, ModeResolver.Resolve([], null, productionDefault: true).Mode, "bootstrap réel au premier lancement PROD");
        Check.Equal(AppMode.Simulation, ModeResolver.Resolve(["--simulation"], "0", productionDefault: true).Mode, "argument démonstration prioritaire");
        Check.Equal(AppMode.Simulation, ModeResolver.Resolve([], "1", productionDefault: true).Mode, "variable démonstration explicite conservée");
        Check.Equal(AppMode.Poste, ModeResolver.Resolve(["--poste"], "1").Mode, "contexte explicite du poste");
        Check.Equal(AppMode.Simulation, ModeResolver.Resolve(["--poste", "--simulation"], null, productionDefault: true).Mode, "ambiguïté résolue vers la démonstration");
    }
}
