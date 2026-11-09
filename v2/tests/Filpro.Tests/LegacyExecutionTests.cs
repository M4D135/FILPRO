using System.Text;
using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Contrôles de compatibilité fermée. Ils ne valident ni ScanState ni LoadState sur Windows.</summary>
internal static class LegacyExecutionTests
{
    private const string Id = "11111111111111111111111111111111";
    private static readonly RestoreSourceProfile[] Sources = [new(@"LAB\Alice", "S-1-5-21-1-2-3-1000", @"C:\Users\Alice")];

    [Test]
    public static void ReglesV1ConnuesGardentFichiersEtRegistreAvecUneSeulePolitique()
    {
        var selection = Selection(); var bytes = LegacyUsmtRules.Capture(selection);
        var read = LegacyUsmtRules.ReadSelection(bytes);
        Check.Equal(selection.Files[0].Rel, read.Files[0].Rel, "Chemin AppData exact");
        Check.Equal(selection.Registry[0].Rel, read.Registry[0].Rel, "Clé exacte");
        var group = SoftwareCatalog.Group(read.Files, read.Registry).Single();
        var restore = UsmtMigrationRules.Read(LegacyUsmtRules.Restore(bytes, [new(group.Id, RestorePolicyMode.KeepCurrent)]));
        Check.True(restore.Descendants("rules").All(r => r.Elements("merge").Count() == 1), "Une seule politique à l'application");
        Check.True(restore.Descendants("merge").All(m => (string?)m.Attribute("script") == "MigXmlHelper.DestinationPriority()"), "Fichier et registre cohérents");
        Check.Equal(0, LegacyUsmtRules.ReadSelection(LegacyUsmtRules.Capture(new([], []))).Files.Count, "Sélection vide exacte admise");
    }

    [Test]
    public static void AucuneRegleExterneOuMachineNePassePourUneRegleConnue()
    {
        var bytes = LegacyUsmtRules.Capture(Selection());
        var text = Encoding.UTF8.GetString(bytes);
        foreach (var changed in new[] { text.Replace("context=\"User\"", "context=\"System\"", StringComparison.Ordinal),
            text.Replace("MigXmlHelper.SourcePriority()", "Custom.Execute()", StringComparison.Ordinal),
            text.Replace(@"HKCU\Software", @"HKLM\Software", StringComparison.Ordinal),
            text.Replace("%CSIDL_APPDATA%", " %CSIDL_APPDATA%", StringComparison.Ordinal) })
            Check.Throws<FilproOperationException>(() => LegacyUsmtRules.ReadSelection(Encoding.UTF8.GetBytes(changed)), "XML modifié refusé");
    }

    [Test]
    public static void ConfigurationExigeExclusionSourceEtRetireLesPolitiquesExternes()
    {
        var valid = "<Configuration><Policies><ErrorControl><fileError errorCode=\"any\"/></ErrorControl></Policies><WindowsComponents><component ID=\"win\" migrate=\"no\"/></WindowsComponents><Documents><component ID=\"doc\" migrate=\"yes\"/></Documents></Configuration>";
        var restricted = UsmtMigrationRules.Read(UsmtProfileConfiguration.Restrict(Encoding.UTF8.GetBytes(valid), true));
        Check.Null(restricted.Root!.Element("Policies"), "Aucune règle externe nonFatal conservée");
        Check.Throws<FilproOperationException>(() => UsmtProfileConfiguration.Restrict(Encoding.UTF8.GetBytes(valid.Replace("ID=\"win\" migrate=\"no\"", "ID=\"win\" migrate=\"yes\"", StringComparison.Ordinal)), true), "La config cible ne remplace pas la preuve source");
        Check.Throws<FilproOperationException>(() => UsmtProfileConfiguration.Restrict(Encoding.UTF8.GetBytes(valid.Replace("</Configuration>", "<ProfileControl><localGroups/></ProfileControl></Configuration>", StringComparison.Ordinal)), true), "Mutation de groupes refusée");
    }

    [Test]
    public static void CatalogueCompletRelieLesFichiersAuxIdentitesReelles()
    {
        var entries = new[] { Compressed(@"C:\Users\Alice\Documents\fiche.txt"), Compressed(@"C:\Users\Alice\AppData\Roaming\vlc\vlcrc") };
        var actual = LegacyRestoreScope.Resolve(Sources, Selection(), new(true, entries)).Single();
        Check.Equal(Sources[0].Sid, actual.Selection.Sid, "SID source conservé");
        Check.Equal("Documents", actual.PersonalFolders.Single(), "Dossier source réel");
        Check.Equal(Sources[0].ProfilePath, actual.Selection.ProfilePath, "Aucun emprunt à une cible");
        var plain = new BackupCatalogEntry(ExecutionCatalogCodec.EntryId(Id, "C$/Users/Alice/Documents/fiche.txt"), Id,
            "C$/Users/Alice/Documents/fiche.txt", 8, false, "USMT/File/C$/Users/Alice/Documents/fiche.txt");
        Check.Equal(@"C:\Users\Alice\Documents\fiche.txt", LegacyRestoreScope.OriginalName(plain), "Arborescence drive$ reconnue exactement");
    }

    [Test]
    public static void CataloguePartielExterneOuAppDataNonCouvertBloqueLoadState()
    {
        Check.Throws<FilproOperationException>(() => LegacyRestoreScope.Resolve(Sources, Selection(), new(false, [Compressed(@"C:\Users\Alice\Documents\fiche.txt")])), "Catalogue partiel");
        foreach (var path in new[] { @"C:\Windows\system.ini", @"C:\Users\Bob\Documents\fiche.txt", @"C:\Users\Alice\AppData\Roaming\Microsoft\HorsSelection\data", @"C:\Users\Alice\NTUSER.DAT" })
            Check.Throws<FilproOperationException>(() => LegacyRestoreScope.Resolve(Sources, Selection(), new(true, [Compressed(path)])), "Portée non couverte refusée");
        var unknown = new BackupCatalogEntry("x", Id, "opaque/layout.txt", 1, false, "USMT/File/opaque/layout.txt");
        Check.Throws<FilproOperationException>(() => LegacyRestoreScope.OriginalName(unknown), "Disposition native non prouvée");
        Check.Throws<FilproOperationException>(() => LegacyRestoreScope.Resolve([Sources[0] with { Sid = null }], Selection(), new(true, [])), "Identité absente");
    }

    [Test]
    public static async Task LectureV1ReprendSIDEtPathExactesSansPretendreVerifierLePayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-v1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Rules")); Directory.CreateDirectory(Path.Combine(root, "Store", "USMT"));
        try
        {
            var rules = LegacyUsmtRules.Capture(Selection()); File.WriteAllBytes(Path.Combine(root, "Rules", "filpro-config.xml"), rules);
            File.WriteAllText(Path.Combine(root, "Store", "USMT", "USMT.MIG"), "native-not-verified");
            var manifest = new { SchemaVersion = 1, Simulated = false, Compressed = true, Encrypted = false, Status = "Completed", Users = new[] { @"LAB\Alice" },
                Rules = new[] { new { Path = @"Rules\filpro-config.xml", SHA256 = BackupManifestCodec.Hash(rules) } },
                SourceProfiles = new[] { new { Account = @"LAB\Alice", SID = "S-1-5-21-1-2-3-1000", Path = @"C:\Users\Alice" } },
                Id = "V1-actual-manifest", UsmtVersion = "10.0.1.0" };
            File.WriteAllBytes(Path.Combine(root, "filpro-backup.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
            var read = await new WindowsBackupStoreReader(new ExecutionFileSystem()).ReadAsync(root, default);
            Check.Equal(Sources[0].Sid, read.Inspection.SourceProfiles.Single().Sid, "Champ V1 SID en majuscules");
            Check.Equal(Sources[0].ProfilePath, read.Inspection.SourceProfiles.Single().ProfilePath, "Champ V1 Path");
            Check.True(read.Inspection.Store.Ready, "Métadonnées de magasin reconnues");
            Check.False(read.Inspection.PayloadVerified, "Aucun contrôle USMT inventé");
            Check.True(read.V1!.RestoreBlockers.Count > 0, "Sans preuve de config source, LoadState reste bloqué");
            Check.True(read.V1.RestoreBlockers.Any(b => b.Contains("référence exhaustive", StringComparison.Ordinal)), "Le catalogue de fichiers ne prouve pas l'absence de paramètres machine");
            Check.Null(read.V1.Payload, "Aucun SHA historique fabriqué");
        }
        finally { Directory.Delete(root, true); }
    }

    private static BackupCatalogEntry Compressed(string name) => new(ExecutionCatalogCodec.EntryId(Id, name), Id, name, null, false);
    private static LegacySoftwareSelection Selection() => new(
        [new(SoftwareScope.Roaming, @"vlc", "VLC", SoftwareCategory.Preferences, true, false, false, null, false, false)],
        [new(SoftwareScope.Hkcu, @"Software\VideoLAN\VLC", "VLC", SoftwareCategory.Preferences, true, false, false, null, false, false)]);
}
