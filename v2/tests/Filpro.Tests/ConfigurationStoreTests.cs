using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class ConfigurationStoreTests
{
    [Test] public static async Task NouvelUtilisateurInitialiseDepuisDefautsValides()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack", defaults: new SharedConfiguration(Theme: "dark"));
        using var service = new LocalConfigurationService(pack, fixture.Data); var state = await service.ReadAsync(default);
        Check.Equal("dark", state.Settings.Theme, "initialisation"); Check.True(state.Bootstrap is null, "pas de question pour un nouvel utilisateur");
        Check.True(File.Exists(Path.Combine(fixture.Data, "configuration-v2.json")), "état local créé");
    }
    [Test] public static async Task ChoixGarderMemoriseUneFoisParEmpreinteDuPack()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        using var service = new LocalConfigurationService(pack, fixture.Data); _ = await service.ReadAsync(default);
        PackTestFixture.SetDefaults(pack, new SharedConfiguration(Theme: "dark"));
        var proposed = await service.ReadAsync(default); Check.NotNull(proposed.Bootstrap, "nouveaux défauts proposés");
        _ = await service.DecideBootstrapAsync(proposed.Bootstrap!.Id, false, true, default);
        var kept = await service.ReadAsync(default); Check.Equal("system", kept.Settings.Theme, "réglages conservés"); Check.True(kept.Bootstrap is null, "proposition mémorisée");
        PackTestFixture.SetDefaults(pack, new SharedConfiguration(Theme: "light"));
        Check.NotNull((await service.ReadAsync(default)).Bootstrap, "même version mais défauts différents = nouveau choix");
    }
    [Test] public static async Task DefautsIdentiquesAuxReglagesNeDemandentRienEtRestentVus()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        var path = Path.Combine(fixture.Data, "configuration-v2.json");
        using (var service = new LocalConfigurationService(pack, fixture.Data))
        {
            var state = await service.ReadAsync(default);
            _ = await service.SaveAsync(state.Settings with { Theme = "dark" }, state.Revision, default);
            PackTestFixture.SetDefaults(pack, new SharedConfiguration(Theme: "dark")); var before = File.ReadAllBytes(path);
            var seen = await service.ReadAsync(default); Check.Null(seen.Bootstrap, "aucun aperçu quand rien ne changerait");
            Check.Equal(seen.Revision, (await service.ReadAsync(default)).Revision, "révision stable entre deux lectures");
            Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "la lecture n'écrit rien");
            _ = await service.SaveAsync(seen.Settings with { Theme = "light" }, seen.Revision, default);
            Check.Null((await service.ReadAsync(default)).Bootstrap, "défauts déjà vus : pas reproposés après un changement de réglage");
        }
        using (var restarted = new LocalConfigurationService(pack, fixture.Data))
            Check.Null((await restarted.ReadAsync(default)).Bootstrap, "défauts vus mémorisés par l'enregistrement suivant");
        PackTestFixture.SetDefaults(pack, new SharedConfiguration(Theme: "contrast"));
        using var changed = new LocalConfigurationService(pack, fixture.Data);
        Check.NotNull((await changed.ReadAsync(default)).Bootstrap, "défauts différents : aperçu proposé");
    }
    [Test] public static async Task MiseAJourAvecLesMemesDefautsNeReposeRien()
    {
        using var fixture = new PackTestFixture(); var old = fixture.CreatePack("ancien", version: "0.0.1");
        using (var service = new LocalConfigurationService(old, fixture.Data))
        {
            var state = await service.ReadAsync(default);
            _ = await service.SaveAsync(state.Settings with { Theme = "contrast", Vss = true }, state.Revision, default);
            var preview = await service.PreviewDefaultsAsync(default); var written = await service.ApplyDefaultsAsync(preview.Id, true, default);
            Check.Null(written.State.Bootstrap, "défauts définis depuis les réglages actuels : rien à examiner");
        }
        var updated = fixture.CreatePack("nouveau", version: "2.0.18", marker: "NOUVELLE-VERSION-FICTIVE",
            defaults: new SharedConfiguration(Theme: "contrast", Vss: true));
        var path = Path.Combine(fixture.Data, "configuration-v2.json"); var before = File.ReadAllBytes(path);
        using var next = new LocalConfigurationService(updated, fixture.Data); var after = await next.ReadAsync(default);
        Check.Null(after.Bootstrap, "nouvelle version aux mêmes défauts : aucun aperçu");
        Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "premier démarrage de la nouvelle version : rien d'écrit");
        Check.Equal("contrast", after.Settings.Theme, "réglages inchangés"); Check.True(after.Settings.Vss, "réglages inchangés");
    }
    [Test] public static async Task LeThemeSeLitSansCreerNiProposerQuoiQueCeSoit()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        using var service = new LocalConfigurationService(pack, fixture.Data); var path = Path.Combine(fixture.Data, "configuration-v2.json");
        Check.Null(await service.ReadThemeAsync(default), "rien d'enregistré : aucun thème"); Check.False(File.Exists(path), "document jamais créé par la lecture du thème");
        var state = await service.ReadAsync(default); _ = await service.SaveAsync(state.Settings with { Theme = "dark" }, state.Revision, default);
        PackTestFixture.SetDefaults(pack, new SharedConfiguration(Theme: "light"));
        var proposed = await service.ReadAsync(default); Check.NotNull(proposed.Bootstrap, "défauts du pack proposés"); var before = File.ReadAllBytes(path);
        // 40 confirmations : au-delà de 32 aperçus, une lecture complète évincerait l'aperçu en attente.
        for (var i = 0; i < 40; i++) Check.Equal("dark", await service.ReadThemeAsync(default), "thème enregistré");
        Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "document inchangé");
        _ = await service.DecideBootstrapAsync(proposed.Bootstrap!.Id, false, true, default);
        Check.Equal("dark", (await service.ReadAsync(default)).Settings.Theme, "aperçu d'origine toujours valable et décidé");
    }
    [Test] public static async Task AnnulationNeMemorisePasLeChoixEtApplicationSauvegarde()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack");
        using var service = new LocalConfigurationService(pack, fixture.Data); _ = await service.ReadAsync(default);
        PackTestFixture.SetDefaults(pack, new SharedConfiguration(Vss: true)); var preview = (await service.ReadAsync(default)).Bootstrap!;
        await Check.ThrowsAsync<FilproOperationException>(() => service.DecideBootstrapAsync(preview.Id, true, false, default), "pas de confirmation");
        Check.NotNull((await service.ReadAsync(default)).Bootstrap, "choix non marqué");
        var applied = await service.DecideBootstrapAsync(preview.Id, true, true, default);
        Check.True(applied.State.Settings.Vss, "appliqué"); Check.True(File.Exists(applied.BackupPath), "sauvegarde préalable réelle");
    }
    [Test] public static async Task ImportLieAuxOctetsEtRevisionNecrasePasUneModification()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); using var service = new LocalConfigurationService(pack, fixture.Data);
        var state = await service.ReadAsync(default); var source = Path.Combine(fixture.Root, "portable.json");
        File.WriteAllBytes(source, ConfigurationCodec.Encode(new SharedConfiguration(Theme: "dark"))); var preview = await service.PreviewImportAsync(source, default);
        File.WriteAllBytes(source, ConfigurationCodec.Encode(new SharedConfiguration(Theme: "light")));
        await Check.ThrowsAsync<FilproOperationException>(() => service.ApplyImportAsync(preview.Id, true, default), "source changée");
        preview = await service.PreviewImportAsync(source, default); _ = await service.SaveAsync(state.Settings with { Vss = true }, state.Revision, default);
        await Check.ThrowsAsync<FilproOperationException>(() => service.ApplyImportAsync(preview.Id, true, default), "état concurrent changé");
        Check.True((await service.ReadAsync(default)).Settings.Vss, "modification concurrente conservée");
    }
    [Test] public static async Task EtatInvalideConserveSansReinitialisation()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); Directory.CreateDirectory(fixture.Data);
        var path = Path.Combine(fixture.Data, "configuration-v2.json"); File.WriteAllText(path, "{document corrompu");
        using var service = new LocalConfigurationService(pack, fixture.Data);
        await Check.ThrowsAsync<FilproOperationException>(() => service.ReadAsync(default), "état invalide"); Check.Equal("{document corrompu", File.ReadAllText(path), "octets inchangés");
    }
    [Test] public static async Task DefautsProdSansModificationDuManifesteBinaire()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack", defaults: new SharedConfiguration());
        var initial = await PackIntegrityVerifier.ValidateAsync(pack, "PROD"); using var service = new LocalConfigurationService(pack, fixture.Data);
        var state = await service.ReadAsync(default); _ = await service.SaveAsync(state.Settings with { Theme = "contrast" }, state.Revision, default);
        var preview = await service.PreviewDefaultsAsync(default); var result = await service.ApplyDefaultsAsync(preview.Id, true, default);
        var updated = await PackIntegrityVerifier.ValidateAsync(pack, "PROD");
        Check.Equal(initial.ManifestSha256, updated.ManifestSha256, "manifeste binaire identique"); Check.True(initial.DefaultsSha256 != updated.DefaultsSha256, "option changée");
        Check.True(File.Exists(result.BackupPath), "anciens défauts sauvegardés hors pack"); Check.False(StoragePathPolicy.IsWithin(result.BackupPath!, pack), "sauvegarde hors pack");
    }

    [Test] public static async Task LesDefautsLightSontDistribuablesSansOuvrirCeDroitAuDev()
    {
        using var fixture = new PackTestFixture();
        var light = fixture.CreatePack("light", flavor: PackManifestCodec.LightFlavor);
        using var service = new LocalConfigurationService(light, fixture.Data);
        var previous = await PackIntegrityVerifier.ValidateAsync(light, PackManifestCodec.LightFlavor);
        var preview = await service.PreviewDefaultsAsync(default);
        await service.ApplyDefaultsAsync(preview.Id, true, default);
        var updated = await PackIntegrityVerifier.ValidateAsync(light, PackManifestCodec.LightFlavor);
        Check.Equal(previous.ManifestSha256, updated.ManifestSha256, "inventaire Light inchangé");
        Check.NotNull(updated.DefaultsSha256, "défauts distribuables enregistrés");
        var dev = fixture.CreatePack("dev", flavor: "DEV");
        using var devService = new LocalConfigurationService(dev, fixture.Data);
        await Check.ThrowsAsync<FilproOperationException>(() => devService.PreviewDefaultsAsync(default), "DEV reste exclu de la distribution");
    }
    [Test] public static async Task ExportExclutPreferencesPersonnellesEtSecret()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); Directory.CreateDirectory(fixture.Data);
        var old = Path.Combine(fixture.Data, "filpro.prefs.json"); File.WriteAllText(old, "SECRET-FICTIF-COMPTE-SID");
        using var service = new LocalConfigurationService(pack, fixture.Data); _ = await service.ReadAsync(default);
        var path = Path.Combine(fixture.Root, "export.json"); _ = await service.ExportAsync(path, true, default);
        Check.False(File.ReadAllText(path).Contains("SECRET", StringComparison.Ordinal), "aucune préférence personnelle exportée");
        Check.Equal("SECRET-FICTIF-COMPTE-SID", File.ReadAllText(old), "V1 conservée"); _ = ConfigurationCodec.Decode(File.ReadAllBytes(path));
    }
    [Test] public static async Task DefautsInvalidesNaffectentPasLesReglagesExistants()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); using var service = new LocalConfigurationService(pack, fixture.Data);
        var initial = await service.ReadAsync(default); var path = Path.Combine(pack, PackManifestCodec.DefaultsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "{\"token\":\"fictif\"}");
        var after = await service.ReadAsync(default); Check.Equal(initial.Revision, after.Revision, "réglages inchangés"); Check.True(after.Warnings.Count > 0, "avertissement explicite"); Check.True(after.Bootstrap is null, "aucun aperçu trompeur");
    }
    [Test] public static async Task ExportNeRemplaceNiJsonEtrangerNiAutreExtension()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack"); using var service = new LocalConfigurationService(pack, fixture.Data);
        foreach (var name in new[] { "document.json", "document.txt" })
        {
            var path = Path.Combine(fixture.Root, name); const string original = "{\"application\":\"AUTRE\",\"valeur\":42}"; File.WriteAllText(path, original);
            await Check.ThrowsAsync<FilproOperationException>(() => service.ExportAsync(path, true, default), "fichier étranger refusé");
            Check.Equal(original, File.ReadAllText(path), "aucun octet étranger écrasé");
        }
    }
    [Test] public static async Task CacheStartupLieAuManifesteEtInapteAAutoriserUneEcritureSurPackAltere()
    {
        using var fixture = new PackTestFixture(); var pack = fixture.CreatePack("pack", defaults: new SharedConfiguration(Theme: "dark"));
        var other = fixture.CreatePack("autre", marker: "AUTRES-OCTETS-FICTIFS");
        var verified = await PackIntegrityVerifier.ValidateAsync(pack, "PROD"); var foreign = await PackIntegrityVerifier.ValidateAsync(other, "PROD");
        using var service = new LocalConfigurationService(pack, fixture.Data);
        await Check.ThrowsAsync<FilproOperationException>(() => service.UseStartupValidationAsync(foreign, default), "cache d'un autre pack refusé");
        await Check.ThrowsAsync<FilproOperationException>(() => service.UseStartupValidationAsync(verified with { VerifiedFiles = 0 }, default), "vérification incomplète refusée");
        await service.UseStartupValidationAsync(verified, default);
        File.AppendAllText(Path.Combine(pack, "FILPRO.exe"), "ALTERE-APRES-DEMARRAGE");
        Check.Equal("dark", (await service.ReadAsync(default)).Settings.Theme, "le cache sert uniquement à lire les options fermées");
        await Check.ThrowsAsync<FilproOperationException>(() => service.PreviewDefaultsAsync(default), "une nouvelle écriture du pack exige une nouvelle vérification complète");
    }
}
