using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

internal static class UpdateSourcePreferenceTests
{
    [Test] public static async Task DossierAbsentMemoriseSansAccesPuisReluApresRedemarrage()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        var absent = Path.Combine(fixture.Root, "partage-indisponible");
        using (var first = new LocalUpdateService(pack, fixture.Data, () => true))
        {
            var saved = await first.SaveSourceAsync(UpdateSourceKind.Folder, absent, default);
            Check.Equal(absent, saved.Folder, "choix conservé");
            Check.False(Directory.Exists(absent), "aucun accès ni création de la source");
        }
        using var reopened = new LocalUpdateService(pack, fixture.Data, () => true);
        var read = await reopened.ReadSourceAsync(default);
        Check.Equal(UpdateSourceKind.Folder, read.Kind, "type relu");
        Check.Equal(absent, read.Folder, "chemin relu");
        using var configuration = new LocalConfigurationService(pack, fixture.Data);
        var export = Path.Combine(fixture.Root, "partageable.json");
        await configuration.ExportAsync(export, true, default);
        Check.False(File.ReadAllText(export).Contains("partage-indisponible", StringComparison.Ordinal), "chemin privé exclu de l'export");
        await reopened.SaveSourceAsync(UpdateSourceKind.ManualZip, null, default);
        Check.True((await reopened.ReadSourceAsync(default)).Folder is null, "ZIP manuel sans chemin mémorisé");
    }

    [Test] public static async Task AncienChoixGitHubDevientUnDossierARenseigner()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        using var service = new LocalUpdateService(pack, fixture.Data, () => true);
        var none = await service.ReadSourceAsync(default);
        Check.True(none.Kind == UpdateSourceKind.Folder && none.Folder is null, "sans choix : dossier à renseigner");
        Directory.CreateDirectory(fixture.Data); var file = Path.Combine(fixture.Data, "update-source.json");
        File.WriteAllText(file, """{"schemaVersion":1,"application":"FILPRO","kind":0,"folder":null}""");
        var legacy = await service.ReadSourceAsync(default);
        Check.True(legacy.Kind == UpdateSourceKind.Folder && legacy.Folder is null, "ancien choix GitHub : dossier à renseigner");
        File.WriteAllText(file, """{"schemaVersion":1,"application":"FILPRO","kind":0,"folder":"D:\\Maj"}""");
        await Check.ThrowsAsync<FilproOperationException>(() => service.ReadSourceAsync(default), "type 0 avec un chemin : préférence invalide");
        await Check.ThrowsAsync<FilproOperationException>(() => service.SaveSourceAsync((UpdateSourceKind)0, null, default), "type 0 non enregistrable");
        var inspect = await Check.ThrowsAsync<FilproOperationException>(() => service.InspectAsync(new((UpdateSourceKind)0), default), "aucune recherche GitHub");
        Check.Equal("update.source", inspect.Code, "source inconnue");
    }

    [Test] public static async Task SourceFictiveResteEnMemoireEtRefuseUnCheminReel()
    {
        var simulation = new SimulationUpdateService();
        await Check.ThrowsAsync<FilproOperationException>(() => simulation.SaveSourceAsync(UpdateSourceKind.Folder,
            @"C:\Users\PersonneReelle", default), "source réelle refusée en démo");
        var state = await simulation.SaveSourceAsync(UpdateSourceKind.ManualZip, null, default);
        Check.True(state.Demonstration, "état fictif explicite");
        Check.Equal(UpdateSourceKind.ManualZip, (await simulation.ReadSourceAsync(default)).Kind, "choix en mémoire");
        Check.Equal(UpdateSourceKind.Folder, (await new SimulationUpdateService().ReadSourceAsync(default)).Kind, "nouvelle démo isolée");
    }
}
