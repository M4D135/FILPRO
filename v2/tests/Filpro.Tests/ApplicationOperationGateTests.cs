using Filpro.Core;

namespace Filpro.Tests;

internal sealed class ApplicationOperationGateTests
{
    [Test]
    public void LeDemarrageEnVolEmpecheUneInstallationMemeSansJobPublie()
    {
        var gate = new ApplicationOperationGate();
        using (gate.EnterNativeStart())
            Check.Equal("update.busy", Check.Throws<FilproOperationException>(() => gate.EnterUpdateHandoff(() => true), "démarrage réservé").Code, "pas de fenêtre entre admission et création du job");
        using var handoff = gate.EnterUpdateHandoff(() => true);
        Check.True(gate.UpdateReserved, "l'installation possède la transition");
        Check.Equal("update.busy", Check.Throws<FilproOperationException>(() => gate.EnterNativeStart(), "aucun nouveau job pendant les await du worker").Code, "exclusion mutuelle");
    }
    [Test]
    public void LeJobPublieEmpecheLaRemiseAuWorkerApresLaFinDuDemarrage()
    {
        var gate = new ApplicationOperationGate();
        using (gate.EnterNativeStart()) { }
        Check.Throws<FilproOperationException>(() => gate.EnterUpdateHandoff(() => false), "le callback lit les jobs sous réservation");
        using var next = gate.EnterNativeStart();
    }
    [Test]
    public void UnEchecLibereLaReservationMaisUneFermetureResteDefinitive()
    {
        var gate = new ApplicationOperationGate();
        var lease = gate.EnterUpdateHandoff(() => true); lease.Dispose(); lease.Dispose();
        using (gate.EnterNativeStart()) { }
        using (gate.EnterUpdateHandoff(() => true)) gate.Close();
        Check.Throws<FilproOperationException>(() => gate.EnterNativeStart(), "la libération de la réservation ne rouvre pas la session");
        Check.Throws<FilproOperationException>(() => gate.EnterUpdateHandoff(() => true), "aucune nouvelle transition après fermeture");
    }
}
