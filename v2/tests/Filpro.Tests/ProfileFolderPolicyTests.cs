using Filpro.Core;

namespace Filpro.Tests;

internal static class ProfileFolderPolicyTests
{
    [Test]
    public static void VariablesAppartiennentAuProfilCible()
    {
        var values = Values(); values["Personal"] = @"%HOMEDRIVE%%HOMEPATH%\Documents";
        values["Templates"] = @"%APPDATA%\Microsoft\Templates";
        var result = ProfileFolderPolicy.Validate(@"D:\Profiles\Alice", values);
        Check.True(result.Contains(@"D:\Profiles\Alice\Documents"), "Identité cible utilisée");
        Check.True(result.Contains(@"D:\Profiles\Alice\AppData\Roaming\Microsoft\Templates"), "AppData cible utilisé");
    }

    [Test]
    public static void RedirectionsCloudEtVariablesInconnuesRefusees()
    {
        foreach (var path in new[] { @"\\server\share\Documents", @"C:\Users\Bob\Documents", @"%USERPROFILE%\OneDrive\Documents", @"%USERNAME%\Documents", @"%USERPROFILE%\..\Bob" })
        {
            var values = Values(); values["Personal"] = path;
            Check.Throws<FilproOperationException>(() => ProfileFolderPolicy.Validate(@"C:\Users\Alice", values), "Aucune portée extérieure supposée sûre");
        }
    }

    [Test]
    public static void AppDataDoitEtrePresentEtStandard()
    {
        var values = Values(); values["AppData"] = @"%USERPROFILE%\RoamingAutre";
        Check.Throws<FilproOperationException>(() => ProfileFolderPolicy.Validate(@"C:\Users\Alice", values), "AppData déplacé");
        values = Values(); values.Remove("Local AppData"); values["Personal"] = @"%USERPROFILE%\Documents";
        Check.Throws<FilproOperationException>(() => ProfileFolderPolicy.Validate(@"C:\Users\Alice", values), "AppData incomplet");
        values = Values(); values["appdata"] = @"%USERPROFILE%\AppData\Roaming";
        Check.Throws<FilproOperationException>(() => ProfileFolderPolicy.Validate(@"C:\Users\Alice", values), "Casse ambiguë");
    }

    [Test]
    public static void PrealableInclutUniquementLesPorteesDesSourcesVersCetteCible()
    {
        var sourceA = Selection("Alice", "1000", @"Microsoft\MonModele", @"Control Panel\Desktop");
        var sourceB = Selection("Bob", "1001", @"Autre\Modele", @"Software\Autre");
        RestoreMappingResolution[] mappings = [new(@"LAB\Alice", @"LAB\CibleA", "S-1-5-21-1-2-3-2000"), new(@"LAB\Bob", @"LAB\CibleB", "S-1-5-21-1-2-3-2001")];
        var query = RestorePriorSelection.Query("S-1-5-21-1-2-3-2000", mappings, [sourceA, sourceB]);
        Check.Equal(@"Microsoft\MonModele", query.AdditionalFiles.Single().Rel, "Modèle absent du catalogue local transmis");
        Check.Equal(@"Control Panel\Desktop", query.AdditionalRegistryKeys.Single(), "Clé hors Software transmise");
        Check.Throws<FilproOperationException>(() => RestorePriorSelection.Query("S-1-5-21-1-2-3-2000", mappings, [sourceB]), "Source manquante refusée");
    }

    private static Dictionary<string, string> Values() => new(StringComparer.Ordinal)
    { ["AppData"] = @"%USERPROFILE%\AppData\Roaming", ["Local AppData"] = @"%USERPROFILE%\AppData\Local" };
    private static BackupProfileSelection Selection(string name, string rid, string file, string registry) => new("S-1-5-21-1-2-3-" + rid,
        ProfileIdentityKind.CompteLocalOuDomaine, @"LAB\" + name, @"C:\Users\" + name, false,
        [new(SoftwareScope.Roaming, file, file, SoftwareCategory.Preferences, true, false, false, null, false, false)],
        [new(SoftwareScope.Hkcu, registry, registry, SoftwareCategory.Preferences, true, false, false, null, false, false)]);
}
