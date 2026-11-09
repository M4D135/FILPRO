using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Logique pure de la sélection groupée : identité logiciel, regroupement fichiers+registre, recommandations, tailles.</summary>
internal sealed class SoftwareCatalogTests
{
    private static SoftwareItem File(SoftwareScope scope, string rel, bool selected = false, bool recommend = false, long? size = null, bool measured = false, bool complete = false) =>
        new(scope, rel, rel, SoftwareCategory.Donnees, selected, recommend, false, size, measured, complete);
    private static SoftwareItem Reg(string rel, bool selected = false, bool recommend = false) =>
        new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, selected, recommend, false, null, false, false);

    [Test]
    public void IdentiteReconnaitLesLogicielsConus()
    {
        var (id, name, linked) = SoftwareCatalog.ResolveIdentity(File(SoftwareScope.Roaming, "Notepad++"));
        Check.Equal("known:notepadpp", id, "Notepad++ reconnu");
        Check.True(linked, "lié");
        Check.Equal("Notepad++", name, "nom du logiciel");
        var office = SoftwareCatalog.ResolveIdentity(File(SoftwareScope.Roaming, @"Microsoft\Word"));
        Check.Equal("known:office", office.Id, "Word rattaché à Office");
        var unknown = SoftwareCatalog.ResolveIdentity(File(SoftwareScope.Roaming, "ZzzInconnu"));
        Check.False(unknown.Linked, "chemin inconnu non lié");
        Check.True(unknown.Id.StartsWith("files|", StringComparison.Ordinal), "périmètre propre fichiers");
    }

    [Test]
    public void RegroupementFusionneFichiersEtCleDunMemeLogiciel()
    {
        var groups = SoftwareCatalog.Group(
            [File(SoftwareScope.Roaming, "Notepad++", selected: true), File(SoftwareScope.Roaming, "vlc")],
            [Reg(@"Software\Notepad++", selected: true), Reg(@"Software\VideoLAN\VLC")]);
        var notepad = groups.Single(g => g.Id == "known:notepadpp");
        Check.Equal(1, notepad.Files.Count, "un dossier");
        Check.Equal(1, notepad.Registry.Count, "une clé");
        Check.Equal(true, notepad.Selected, "tout retenu");
        Check.Equal(2, notepad.SelectedCount, "deux membres retenus");
        var vlc = groups.Single(g => g.Id == "known:vlc");
        Check.Equal(false, vlc.Selected, "rien retenu");
    }

    [Test]
    public void SelectionPartielleDonneLeTriEtat()
    {
        var groups = SoftwareCatalog.Group([File(SoftwareScope.Roaming, "Notepad++", selected: true)], [Reg(@"Software\Notepad++", selected: false)]);
        Check.Null(groups.Single().Selected, "sélection partielle = null");
        Check.Equal(1, groups.Single().SelectedCount, "un seul membre retenu");
    }

    [Test]
    public void RecommandationCocheFichiersEtClesEnsemble()
    {
        var groups = SoftwareCatalog.Group(
            [File(SoftwareScope.Roaming, "Notepad++", recommend: true), File(SoftwareScope.Roaming, "vlc")],
            [Reg(@"Software\Notepad++", recommend: true), Reg(@"Software\VideoLAN\VLC")]);
        var applied = SoftwareCatalog.ApplyRecommendations(groups);
        Check.Equal(true, applied.Single(g => g.Id == "known:notepadpp").Selected, "logiciel recommandé : tout coché");
        Check.Equal(false, applied.Single(g => g.Id == "known:vlc").Selected, "non recommandé : rien");
    }

    [Test]
    public void TailleMesureeCompleteEtChevauchementCompteUneFois()
    {
        var files = new[]
        {
            File(SoftwareScope.Local, @"Google\Chrome\User Data", size: 1000, measured: true, complete: true),
            File(SoftwareScope.Local, @"Google\Chrome\User Data\Default\Cache", size: 500, measured: true, complete: true),
        };
        var s = SoftwareCatalog.SummarizeSizes(files);
        Check.Equal(SoftwareSizeState.Mesuree, s.State, "mesure complète");
        Check.Equal(1000L, s.Bytes!.Value, "dossier imbriqué compté une seule fois");
        Check.Equal(1, s.OverlappingPaths, "un chevauchement");
    }

    [Test]
    public void TailleNonMesureeResteInconnue()
    {
        var s = SoftwareCatalog.SummarizeSizes([File(SoftwareScope.Roaming, "Notepad++")]);
        Check.Equal(SoftwareSizeState.Inconnue, s.State, "sans mesure : inconnue, jamais inventée");
        Check.Null(s.Bytes, "aucun octet fictif");
    }
}
