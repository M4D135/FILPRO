using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

internal sealed class CommonSelectionTests
{
    private const string Lea = "S-1-5-21-111222333-444555666-777888999-1001";
    private const string Hugo = "S-1-5-21-111222333-444555666-777888999-1004";

    [Test]
    public async Task RecommandationsPeuventEtreDesactiveesSansEffacerLesDerniersChoix()
    {
        var clock = TimeProvider.System; var source = new SimulationPosteSource(SimulationOptions.Default, clock);
        var configuration = new SimulationConfigurationService(clock); var common = new SimulationCommonSelectionService(clock);
        var poste = new ConfiguredPosteSource(source, configuration, common);
        var initial = await configuration.ReadAsync(CancellationToken.None);
        await configuration.SaveAsync(initial.Settings with { RecommendedSelection = false }, initial.Revision, CancellationToken.None);
        var inventory = poste.ReadSoftware(Hugo, SoftwareQuery.Empty, CancellationToken.None);
        Check.Null(source.ReadLastSelection(Hugo, CancellationToken.None), "le profil choisi n'a pas d'historique");
        Check.True(inventory.Groups.Count > 0 && inventory.Groups.All(group => group.Selected == false), "aucune recommandation quand désactivée");
        var selected = source.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.Single(group => group.Id == "known:vlc");
        source.SaveLastSelection(new(Lea, selected.Files, selected.Registry, clock.GetUtcNow()), CancellationToken.None);
        Check.True(poste.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.Single(group => group.Id == selected.Id).Selected == true,
            "les derniers choix restent prioritaires");
    }
    [Test]
    public void ModeleCommunConserveFichiersEtClesEtNeReactivePasUnChoixVide()
    {
        var clock = TimeProvider.System; var source = new SimulationPosteSource(SimulationOptions.Default, clock);
        var common = new SimulationCommonSelectionService(clock);
        var poste = new ConfiguredPosteSource(source, new SimulationConfigurationService(clock), common);
        var vlc = source.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.Single(group => group.Id == "known:vlc");
        var state = common.Save(vlc.Files, vlc.Registry, CancellationToken.None);
        Check.True(state.HasSelection && state.AppData.Count > 0 && state.RegistryKeys.Count > 0, "les deux portées restent ensemble");
        var inherited = poste.ReadSoftware(Hugo, SoftwareQuery.Empty, CancellationToken.None);
        Check.True(inherited.Groups.Single(group => group.Id == vlc.Id).Selected == true, "le profil sans historique reprend les deux portées");
        Check.True(inherited.Groups.Where(group => group.Id != vlc.Id).All(group => group.Selected == false), "aucune sélection supplémentaire implicite");
        source.SaveLastSelection(new(Lea, [], [], clock.GetUtcNow()), CancellationToken.None);
        Check.True(poste.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.All(group => group.Selected == false), "le choix vide l'emporte");
        common.Clear(CancellationToken.None);
        Check.False(common.Read(CancellationToken.None).HasSelection, "retour aux recommandations pour profils sans historique");
    }
    [Test]
    public void DocumentCommunInvalideNestNiEcraseNiInterpreteCommeAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-common-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LocalCommonSelectionService(root, TimeProvider.System);
            service.Save([], [], CancellationToken.None);
            var path = Path.Combine(root, "V2", "Selections", "common.json");
            File.WriteAllText(path, "{document-invalide}");
            Check.Throws<FilproOperationException>(() => service.Save([], [], CancellationToken.None), "conserver le document endommagé");
            Check.Equal("{document-invalide}", File.ReadAllText(path), "aucun écrasement");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Test]
    public void RemplacementEtRetraitConserventUneCopiePrealableSansSid()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-common-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LocalCommonSelectionService(root, TimeProvider.System);
            service.Save([], [], CancellationToken.None); service.Save([], [], CancellationToken.None); service.Clear(CancellationToken.None);
            Check.False(service.Read(CancellationToken.None).HasSelection, "document retiré");
            var backups = Directory.GetFiles(Path.Combine(root, "V2", "Selections", "Backups"));
            Check.Equal(2, backups.Length, "une sauvegarde pour chaque changement destructeur");
            Check.True(backups.All(path => !File.ReadAllText(path).Contains("sid", StringComparison.OrdinalIgnoreCase)), "aucun SID dans le modèle commun");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
