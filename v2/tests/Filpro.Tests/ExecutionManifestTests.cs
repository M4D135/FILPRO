using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class ExecutionManifestTests
{
    [Test]
    public static void ManifesteNePrometPasUneReussiteSansMagasinVerifie()
    {
        var manifest = FixtureManifest();
        _ = BackupManifestCodec.Decode(BackupManifestCodec.Encode(manifest));
        var missing = manifest with { Profiles = [manifest.Profiles[0] with { Payload = null }] };
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Encode(missing), "Une empreinte manque");
        var running = manifest with { Profiles = [manifest.Profiles[0] with { Status = "Running", Payload = null }] };
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Encode(running), "Un profil en cours ne devient pas ready");
        var future = manifest with { SchemaVersion = 500 };
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Encode(future), "Schéma futur refusé");
    }

    [Test]
    public static void ManifestesDupliquesEtCheminsSortantsRefuses()
    {
        var manifest = FixtureManifest();
        var json = Encoding.UTF8.GetString(BackupManifestCodec.Encode(manifest));
        json = json.Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"SchemaVersion\":2", StringComparison.Ordinal);
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Decode(Encoding.UTF8.GetBytes(json)), "Champ dupliqué refusé");
        var profile = manifest.Profiles[0];
        var changed = manifest with { Profiles = [profile with { Rules = [profile.Rules[0] with { Path = "Rules/../ailleurs.xml" }] }] };
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Encode(changed), "Sortie du lot refusée");
        changed = manifest with { Profiles = [profile, profile with { Id = Guid.NewGuid().ToString("N") }] };
        Check.Throws<FilproOperationException>(() => BackupManifestCodec.Encode(changed), "Même SID deux fois refusé");
    }

    [Test]
    public static void EstimationNonCompresseeChoisitLeClusterDuVolume()
    {
        var many = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <PreMigration>
              <storeSize>
                <size clusterSize="4096">413696</size>
                <size clusterSize="0">191523</size>
                <size clusterSize="4096">413696</size>
              </storeSize>
              <temporarySpace><size>1249592</size></temporarySpace>
            </PreMigration>
            """);
        var read = UsmtMigrationRules.ReadEstimate(many, 4096);
        Check.Equal(413696L, read.Store, "taille du cluster 4096");
        Check.Equal(1249592L, read.Temporary, "espace temporaire");
        var single = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <PreMigration>
              <storeSize><size clusterSize="4096">16384</size></storeSize>
              <temporarySpace><size>1208632</size></temporarySpace>
            </PreMigration>
            """);
        var old = UsmtMigrationRules.ReadEstimate(single);
        Check.Equal(16384L, old.Store, "une seule taille reste acceptée");
        Check.Throws<FilproOperationException>(() => UsmtMigrationRules.ReadEstimate(many), "plusieurs tailles sans cluster");
        Check.Throws<FilproOperationException>(() => UsmtMigrationRules.ReadEstimate(many, 512), "cluster absent");
    }

    [Test]
    public static void UnePolitiqueUniqueCouvreFichiersEtRegistreDuGroupe()
    {
        var profile = FixtureManifest().Profiles[0];
        var groups = SoftwareCatalog.Group(profile.Selection.Files, profile.Selection.Registry);
        Check.Equal(1, groups.Count, "Un groupe contient fichier et clé");
        foreach (var mode in new[] { RestorePolicyMode.KeepCurrent, RestorePolicyMode.UseSaved, RestorePolicyMode.KeepBoth })
        {
            var xml = UsmtMigrationRules.Read(UsmtMigrationRules.Restore(profile, [new(groups[0].Id, mode)]));
            foreach (var rule in xml.Descendants("rules")) Check.Equal(1, rule.Elements("merge").Count(), "Un seul merge par portée au chargement");
            var registry = xml.Descendants("rules").Single(r => r.Element("include")!.Descendants("pattern").Any(p => (string?)p.Attribute("type") == "Registry"));
            var expected = mode == RestorePolicyMode.UseSaved ? "MigXmlHelper.SourcePriority()" : "MigXmlHelper.DestinationPriority()";
            Check.Equal(expected, (string?)registry.Element("merge")!.Attribute("script"), "Politique registre réelle");
            if (mode == RestorePolicyMode.KeepBoth)
                Check.True(xml.Descendants("rules").Where(r => r != registry).All(r => ((string?)r.Element("merge")!.Attribute("script"))!.Contains("FindFilePlaceByPattern", StringComparison.Ordinal)), "Coexistence des fichiers");
        }
        var invalid = profile with { Selection = profile.Selection with { Files = [profile.Selection.Files[0] with { Rel = "%TEMP%" }] } };
        Check.Throws<FilproOperationException>(() => UsmtMigrationRules.Capture(invalid), "Aucune expansion d'une sélection");
    }

    [Test]
    public static async Task LectureControleLesReglesSansInventerUneVerificationDuPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-execution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifest = FixtureManifest(); var profile = manifest.Profiles[0];
            void Write(string relative, byte[] bytes)
            { var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
            Write(profile.Rules[0].Path, UsmtMigrationRules.Capture(profile));
            Write(profile.ProfileConfig!.Path, ConfigBytes);
            Write(profile.Payload!.Path, Encoding.UTF8.GetBytes("payload"));
            Write("filpro-backup.json", BackupManifestCodec.Encode(manifest));
            var reader = new WindowsBackupStoreReader(new ExecutionFileSystem());
            var read = await reader.ReadAsync(root, default);
            Check.True(read.Inspection.Store.Ready, "Métadonnées ready reconnues");
            Check.False(read.Inspection.PayloadVerified, "Aucun /verify natif exécuté par une lecture courte");
            Check.Equal(1, read.Inspection.ConflictChoices!.Count, "Choix issus du magasin, pas d'une cible");
            Write(profile.Rules[0].Path, Encoding.UTF8.GetBytes("<migration/>"));
            var error = await Check.ThrowsAsync<FilproOperationException>(() => reader.ReadAsync(root, default), "Règle modifiée refusée");
            Check.Equal("store.integrity", error.Code, "Empreinte contrôlée");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public static void CatalogueExigeMarqueurUniqueEtFiltresLitteraux()
    {
        var id = new string('a', 32);
        var row = @"C:\Users\A\Documents\fiche.txt" + "\tOK";
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [row], default).Complete, "Sans catalog.mig OK, aucune complétude");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, ["catalog.mig\tCORRUPTED", row], default).Complete, "Corruption du catalogue");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, ["catalog.mig\tOK", "catalog.mig\tOK", row], default).Complete, "Marqueur dupliqué");
        var good = ExecutionCatalogCodec.ReadUsmtOutput(id, [row, "catalog.mig\tOK"], default);
        Check.True(good.Complete, "Catalogue interprété"); Check.Equal(1, good.Entries.Count, "Une entrée réelle");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, ["catalog.mig\tOK", @"C:\Users\A\x;*.txt" + "\tOK"], default).Complete, "Pas de motif arbitraire à extraire");
        Check.True(good.NativeIntegrityValid == true, "Rapport natif complet avec catalogue vérifié");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [row, "catalog.mig\tCORRUPTED"], default).NativeIntegrityValid == true, "Catalogue natif corrompu refuse la preuve");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [row, "ligne illisible", "catalog.mig\tOK"], default).NativeIntegrityValid == true, "Ligne illisible refuse la preuve");
        Check.True(ExecutionCatalogCodec.ReadUsmtOutput(id, ["catalog.mig\tOK", row], default).NativeIntegrityValid == true, "Le catalogue peut précéder les autres enregistrements");
    }

    [Test]
    public static void RapportNatif26100DistingueFichiersEtMetadonnees()
    {
        var id = new string('b', 32);
        // Forme observée dans FILPRO-W11-TEST, USMT 10.0.26100 /verify:all.
        string[] lines = [
            @"C:\Users\F2QA_68b870d\AppData\Roaming\vlc\filpro-fixture.txt" + "\tOK",
            @"C:\Users\F2QA_68b870d\Documents\FILPRO-qualification.txt" + "\tOK",
            @"C:\Users\F2QA_68b870d\Desktop\FILPRO-qualification.txt" + "\tOK",
            "MIGSTATE.DAT\tOK", "catalog.mig\tOK", "CACHE.IMG\tOK",
        ];
        var result = ExecutionCatalogCodec.ReadUsmtOutput(id, lines, default);
        Check.True(result.Complete && result.NativeIntegrityValid == true, "rapport natif reconnu");
        Check.Equal(3, result.Entries.Count, "les métadonnées ne sont pas des fichiers récupérables");
        Check.Equal(3, ExecutionCatalogCodec.Decode(ExecutionCatalogCodec.Encode(result)).Entries.Count, "catalogue publiable et relisible");
        foreach (var internalName in new[] { "MIGSTATE.DAT", "catalog.mig", "CACHE.IMG" })
        {
            var corrupt = lines.Select(line => line == internalName + "\tOK" ? internalName + "\tCORRUPTED" : line);
            Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, corrupt, default).NativeIntegrityValid == true, "corruption interne refusée : " + internalName);
            Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [.. lines, internalName + "\tOK"], default).NativeIntegrityValid == true, "métadonnée dupliquée refusée");
        }
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [.. lines, "inconnu.dat\tOK"], default).NativeIntegrityValid == true, "métadonnée non reconnue refusée");
        Check.False(ExecutionCatalogCodec.ReadUsmtOutput(id, [.. lines, lines[0]], default).NativeIntegrityValid == true, "fichier dupliqué refusé");
    }

    [Test]
    public static void ProgressionNouvellePhaseEffaceLesAnciennesEstimations()
    {
        var progress = UsmtProgressReader.Parse("PHASE,Collecting\ntotalPercentageCompleted,25\ntotalMinutesRemaining,6\nPHASE,Saving\n");
        Check.Equal("Saving", progress.Phase, "Dernière phase"); Check.Null(progress.Percent, "Pourcentage ancien effacé"); Check.Null(progress.MinutesRemaining, "ETA ancienne effacée");
        progress = UsmtProgressReader.Parse("PHASE,Applying\ntotalPercentageCompleted,8\ntotalMinutesRemaining,3\nnumberOfIgnoredErrors,2\n");
        Check.Equal<int?>(8, progress.Percent, "Pourcentage observé"); Check.Equal<int?>(3, progress.MinutesRemaining, "Durée native observée");
        Check.Equal<int?>(2, progress.IgnoredErrors, "Erreurs ignorées gardées");
    }

    [Test]
    public static void ProfileListSauvegardeTypesEtRefuseUneIdentiteFausse()
    {
        var snapshot = new ProfileRegistrySnapshot(1, "S-1-5-21-1-2-3-1000", @"C:\Users\Alice",
            [new("ProfileImagePath", 2, Text: @"C:\Users\Alice"), new("State", 4, Number: uint.MaxValue), new("Sid", 3, Binary: "AQID")]);
        var actual = ProfileRegistrySnapshotCodec.Decode(ProfileRegistrySnapshotCodec.Encode(snapshot));
        Check.Equal<ulong?>(uint.MaxValue, actual.Values.Single(v => v.Name == "State").Number, "Bits DWORD conservés");
        Check.Throws<FilproOperationException>(() => ProfileRegistrySnapshotCodec.Encode(snapshot with { Sid = snapshot.Sid + "\n" }), "LF terminal refusé");
        Check.Throws<FilproOperationException>(() => ProfileRegistrySnapshotCodec.Encode(snapshot with { Values = [.. snapshot.Values, new("state", 4, Number: 0)] }), "Valeurs ambiguës refusées");
        Check.Throws<FilproOperationException>(() => ProfileRegistrySnapshotCodec.Encode(snapshot with { Values = [new("ProfileImagePath", 6, Text: "lien")] }), "Lien registre non restaurable");
    }

    private static readonly byte[] ConfigBytes = Encoding.UTF8.GetBytes("<Configuration><WindowsComponents><component ID=\"windows\" migrate=\"no\"/></WindowsComponents><Documents><component ID=\"docs\" migrate=\"yes\"/></Documents></Configuration>");
    private static BackupManifest FixtureManifest()
    {
        var id = new string('1', 32);
        var selection = new BackupProfileSelection("S-1-5-21-1-2-3-1000", ProfileIdentityKind.CompteLocalOuDomaine,
            @"LAB\Alice", @"C:\Users\Alice", false,
            [new(SoftwareScope.Local, @"Google\Chrome\User Data", "Chrome", SoftwareCategory.Preferences, true, false, true, null, false, false)],
            [new(SoftwareScope.Hkcu, @"Software\Google\Chrome", "Chrome", SoftwareCategory.Preferences, true, false, true, null, false, false)]);
        var profile = new BackupManifestProfile(id, selection, "Store/" + id, "Ready", [],
            new("Store/" + id + "/USMT/USMT.MIG", BackupManifestCodec.Hash(Encoding.UTF8.GetBytes("payload")), 7), null, 0, "Verified", ["Documents"], []);
        var rules = UsmtMigrationRules.Capture(profile);
        profile = profile with { Rules = [new("Rules/" + id + "/filpro-config.xml", BackupManifestCodec.Hash(rules), rules.Length)],
            ProfileConfig = new("Rules/" + id + "/profile-config.xml", BackupManifestCodec.Hash(ConfigBytes), ConfigBytes.Length) };
        return new(2, BackupManifestCodec.Kind, new string('2', 32), "2.0.0", DateTimeOffset.UnixEpoch, "LAB", "10.0.1.0",
            "Ready", false, true, false, false, false, [profile], []);
    }
}
