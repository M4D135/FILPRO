using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Recettes V1 et frontières de découverte pures ; aucune preuve de présence ou de sauvegarde sous Windows.</summary>
internal sealed class SoftwareDiscoveryCatalogTests
{
    private static SoftwareItem File(SoftwareScope scope, string rel) =>
        SoftwareDiscoveryCatalog.Files.Single(item => item.Scope == scope && item.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));

    private static SoftwareItem Reg(string rel) =>
        SoftwareDiscoveryCatalog.Registry.Single(item => item.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void RecettesNePretendentNiPresenceNiSelectionNiMesure()
    {
        Check.Equal(29, SoftwareDiscoveryCatalog.Files.Count, "29 recettes AppCatalog V1");
        Check.Equal(24, SoftwareDiscoveryCatalog.Registry.Count, "12 clés explicites et 12 ajouts des associations V1");
        var all = SoftwareDiscoveryCatalog.Files.Concat(SoftwareDiscoveryCatalog.Registry).ToList();
        Check.Equal(all.Count, all.Select(item => $"{item.Scope}|{item.Rel}").Distinct(StringComparer.OrdinalIgnoreCase).Count(), "aucune recette exacte dupliquée");
        foreach (var item in all)
        {
            Check.True(item.Known, item.Rel + " est une recette connue, pas un constat de présence");
            Check.False(item.Selected, item.Rel + " n'est pas sélectionné avant découverte");
            Check.Null(item.Size, item.Rel + " n'a pas de taille inventée");
            Check.False(item.SizeMeasured, item.Rel + " n'est pas mesuré");
            Check.False(item.SizeComplete, item.Rel + " n'est pas prétendu complet");
        }
    }

    [Test]
    public void RecommandationsV1RestentIndependantesDesAssociations()
    {
        Check.True(File(SoftwareScope.Roaming, @"Microsoft\Signatures").Recommend, "signatures recommandées");
        Check.True(Reg(@"Software\Microsoft\Office").Recommend, "réglages Office recommandés");
        Check.True(File(SoftwareScope.Local, @"Google\Chrome\User Data").Recommend, "profil Chrome recommandé");
        Check.True(Reg(@"Software\Google\Chrome").Recommend, "clé Chrome recommandée");

        foreach (var rel in new[] { "vlc", @"SAP\SAP GUI", @"Citrix\ICA Client" })
        {
            var item = File(SoftwareScope.Roaming, rel);
            Check.True(SoftwareCatalog.ResolveIdentity(item).Linked, rel + " est associé à un logiciel");
            Check.False(item.Recommend, rel + " ne devient pas recommandé parce qu'il est associé");
        }
        foreach (var recipe in SoftwareDiscoveryCatalog.RegistryRecipes.Where(recipe => recipe.SourceCategory == "Logiciel"))
        {
            Check.False(recipe.Item.Recommend, recipe.Item.Rel + " ajouté par une association, jamais recommandé implicitement");
        }
        Check.True(File(SoftwareScope.Roaming, "Notepad++").Recommend, "Notepad++ recommandé par les fichiers");
        Check.False(Reg(@"Software\Notepad++").Recommend, "sa clé ajoutée conserve Recommend=false");
    }

    [Test]
    public void DomaineV1EtRecettesNonQualifieesRestentExplicites()
    {
        var examples = SoftwareDiscoveryCatalog.FileRecipes.Where(recipe => recipe.RequiresQualification).ToList();
        Check.Equal(3, examples.Count, "les trois marqueurs NotTested sont conservés");
        foreach (var rel in new[] { @"SAP\SAP GUI", @"Citrix\ICA Client", @"Microsoft\Outlook\AutoComplete" })
        {
            var recipe = examples.Single(recipe => recipe.Item.Rel == rel);
            Check.False(recipe.Item.Recommend, rel + " n'a pas de recommandation propre");
            Check.Equal("Bureautique", recipe.SourceCategory, rel + " conserve le domaine V1");
        }
        var officeTemplates = SoftwareDiscoveryCatalog.FileRecipes.Single(recipe => recipe.Item.Rel == @"Microsoft\Templates");
        Check.Equal("Modèles Office", officeTemplates.Item.Name, "nom lisible V1 conservé");
        Check.Equal("Bureautique", officeTemplates.SourceCategory, "le domaine applicatif est séparé du type de données");
        Check.Equal(SoftwareCatalog.Classify(officeTemplates.Item.Rel), officeTemplates.Item.Category, "classement de données V2 conservé");
        Check.False(officeTemplates.Item.Category == SoftwareCategory.Modele, "un dossier Office n'est pas un modèle commun utilisateur");
        Check.True(SoftwareDiscoveryCatalog.Registry.All(item => item.Category == SoftwareCategory.Preferences), "les clés gardent un type de données V2 valide");
        Check.False(SoftwareDiscoveryCatalog.RegistryRecipes.Any(recipe => recipe.RequiresQualification), "RegCatalog V1 n'avait pas de marqueur NotTested ; cela ne prouve pas une qualification Windows");
    }

    [Test]
    public void RegroupementSelectionneFichiersEtRegistreEnsembleSansRecommanderVlcSapCitrix()
    {
        var groups = SoftwareCatalog.ApplyRecommendations(SoftwareCatalog.Group(SoftwareDiscoveryCatalog.Files, SoftwareDiscoveryCatalog.Registry));
        foreach (var id in new[] { "known:office", "known:chrome", "known:notepadpp" })
        {
            var group = groups.Single(group => group.Id == id);
            Check.True(group.Files.Count > 0 && group.Registry.Count > 0, id + " contient fichiers et clés");
            Check.True(group.Files.Concat(group.Registry).All(item => item.Selected), id + " sélectionne tous ses membres");
        }
        foreach (var id in new[] { "known:vlc", "known:sap", "known:citrix" })
        {
            var group = groups.Single(group => group.Id == id);
            Check.True(group.Files.Count > 0 && group.Registry.Count > 0, id + " conserve fichiers et clés");
            Check.False(group.Files.Concat(group.Registry).Any(item => item.Selected), id + " n'est pas recommandé");
        }
        // Comportement hérité documenté : Recommend=false n'est pas un veto sur les autres membres du groupe Office.
        var autoComplete = groups.Single(group => group.Id == "known:office").Files.Single(item => item.Rel == @"Microsoft\Outlook\AutoComplete");
        Check.False(autoComplete.Recommend, "la recette d'exemple n'a pas changé de recommandation");
        Check.True(autoComplete.Selected, "le groupement Office existant reste inchangé ; ceci ne qualifie pas cette recette");
    }

    [Test]
    public void RacinesPrecisesNeSontPasRemplaceesParDesVendeursEntiers()
    {
        Check.Equal("Chrome", File(SoftwareScope.Local, @"Google\Chrome\User Data").Name, "racine Chrome exacte");
        Check.Equal("Firefox (profil)", File(SoftwareScope.Roaming, @"Mozilla\Firefox").Name, "racine Firefox exacte");
        Check.False(SoftwareDiscoveryCatalog.Files.Any(item => item.Rel is "Microsoft" or "Google" or "Mozilla" or "SAP" or "Citrix"), "aucun dossier vendeur en bloc");
        Check.False(SoftwareDiscoveryCatalog.Files.Any(item => item.Rel == "Sublime Text"), "un binding n'ajoute pas une recette fichiers absente d'AppCatalog");
        Check.Equal("Sublime Text (registre)", Reg(@"Software\Sublime Text").Name, "la clé manquante est en revanche ajoutée comme en V1");
        Check.False(SoftwareDiscoveryCatalog.Registry.Any(item => item.Rel is "Software\\Microsoft" or "Software\\Google" or "Software\\VideoLAN" or "Software\\SAP"), "aucune clé vendeur en bloc");
        Check.NotNull(Reg(@"Software\Microsoft\Office\Outlook\Addins"), "le sous-périmètre explicite Office est conservé");
        Check.NotNull(Reg(@"Software\Microsoft\Office\16.0\Common\Identity"), "le sous-périmètre explicite d'identité est conservé");
    }

    [Test]
    public void GeneriquesFichiersExcluentBruitVendeursEtChevauchementsAvecLaBonnePortee()
    {
        foreach (var scope in new[] { SoftwareScope.Local, SoftwareScope.Roaming })
        {
            foreach (var rel in new[] { "Microsoft", "Google", "Adobe", "Packages", "TempState", "CrashDumps", "D3DSCache", "History", "VirtualStore" })
            {
                Check.True(SoftwareDiscoveryCatalog.IsGenericFileExcluded(scope, rel), scope + " exclut " + rel);
            }
            Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(scope, "Microsoft Tools"), "la frontière du nom de vendeur est exacte");
            Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(scope, "AcmeApp"), "un générique inconnu reste découvrable");
        }
        foreach (var rel in new[] { "SAP", @"SAP\SAP GUI", @"SAP\SAP GUI\Config", "Citrix", "Notepad++", "notepad++/plugins/" })
        {
            Check.True(SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Roaming, rel), rel + " ne double pas une recette explicite");
        }
        Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Roaming, "SAPTools"), "un préfixe partiel n'est pas le parent de SAP GUI");
        Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Roaming, "Notepad++ Backup"), "un voisin n'est pas un descendant");
        Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Local, "SAP"), "SAP n'a une recette explicite que sous Roaming");
        Check.False(SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Local, "Notepad++"), "un catalogue Roaming ne masque pas Local");
    }

    [Test]
    public void GeneriquesRegistreExcluentParentsConnusSansAssimilerDesVoisins()
    {
        foreach (var rel in new[] { @"Software\Microsoft", @"Software\Google", @"Software\Classes", @"Software\Wow6432Node", @"Software\RegisteredApplications", @"Software\VideoLAN", @"Software\SAP", @"Software\Citrix", @"Software\Notepad++", @"software/videolan/vlc/Settings/" })
        {
            Check.True(SoftwareDiscoveryCatalog.IsGenericRegistryExcluded(rel), rel + " ne doit pas élargir ou doubler une recette");
        }
        foreach (var rel in new[] { @"Software\AcmeApp", @"Software\VideoLAN Tools", @"Software\SAPTools", @"Software\Microsoft Tools", @"Software\Notepad++ Backup" })
        {
            Check.False(SoftwareDiscoveryCatalog.IsGenericRegistryExcluded(rel), rel + " est un générique distinct");
        }
        Check.True(SoftwareDiscoveryCatalog.IsGenericRegistryExcluded("Software"), "la racine Software entière n'est jamais un générique");
        Check.True(SoftwareDiscoveryCatalog.IsGenericRegistryExcluded("Network"), "Network est une recette explicite, hors scan Software");
        Check.True(SoftwareDiscoveryCatalog.IsGenericRegistryExcluded(@"OtherRoot\App"), "aucune nouvelle racine de scan HKCU implicite");
    }

    [Test]
    public void FiltrageRefuseUnePorteeFichiersDeRegistre()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => SoftwareDiscoveryCatalog.IsGenericFileExcluded(SoftwareScope.Hkcu, "Microsoft"), "HKCU n'est pas AppData");
        Check.Throws<ArgumentOutOfRangeException>(() => SoftwareDiscoveryCatalog.IsGenericFileExcluded((SoftwareScope)99, "Microsoft"), "une portée inconnue n'est pas assimilée à Roaming");
    }
}
