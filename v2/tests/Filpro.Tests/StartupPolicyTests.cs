using Filpro.App;
using Filpro.Core;

namespace Filpro.Tests;

internal sealed class StartupPolicyTests
{
    [Test]
    public void SeulUnJetonAdministrateurOuvreDirectement()
    {
        Check.False(StartupPolicy.CanEnterDirectly(false), "sans droits actifs, l’UAC est demandée avant toute ouverture");
        Check.True(StartupPolicy.CanEnterDirectly(true), "l’UAC accordée tient lieu de connexion, sans écran d’entrée");
    }

    [Test]
    public void LeRelaisNeTransporteNiCommandeNiModeFictif()
    {
        Check.True(StartupPolicy.IsElevatedRelay(["--poste", "--filpro-elevated"]), "relance fixe acceptée");
        Check.False(StartupPolicy.IsElevatedRelay(["--simulation"]), "aucune élévation induite par la démonstration");
        foreach (var args in new[] { new[] { "--filpro-elevated" }, new[] { "--simulation", "--filpro-elevated" }, new[] { "--poste", "--filpro-elevated", "commande" } })
            Check.Throws<FilproOperationException>(() => StartupPolicy.IsElevatedRelay(args), "arguments étrangers refusés");
    }

    [Test]
    public void LaConnexionNExposeAucunPontMetierNiSecret()
    {
        foreach (var action in new[] { "info", "open", "close", "layout" })
            Check.Equal(action, StartupPolicy.ReadAction("{\"type\":\"filpro.startup\",\"action\":\"" + action + "\"}"), "action fermée");
        foreach (var json in new[] { "null", "[]", "{", "{\"op\":\"sauvegarder.confirmPlan\",\"args\":{}}",
            "{\"type\":\"filpro.startup\",\"action\":\"run\"}", "{\"type\":\"filpro.startup\",\"action\":\"open\",\"password\":\"x\"}",
            "{\"type\":\"filpro.startup\",\"action\":\"open\",\"action\":\"close\"}", "{\"type\":\"filpro.startup\",\"action\":true}", "{\"type\":\"filpro.startup\",\"action\":\"layout\",\"height\":1000}", new string('x', 257) })
            Check.Null(StartupPolicy.ReadAction(json), "entrée étrangère ou ambiguë refusée");
    }
}
