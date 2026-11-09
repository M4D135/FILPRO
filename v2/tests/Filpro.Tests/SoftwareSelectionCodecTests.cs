using System.Text;
using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Codec pur de la sélection logiciels : aller-retour, canonisation, forme fermée, bornes et honnêteté.</summary>
internal sealed class SoftwareSelectionCodecTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static SoftwareItem File(SoftwareScope scope, string rel, SoftwareCategory category = SoftwareCategory.Donnees) =>
        new(scope, rel, rel, category, true, false, false, null, false, false);

    private static SoftwareItem Reg(string rel) =>
        new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, true, false, false, null, false, false);

    private static SoftwareSelectionDocument Doc(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry, SelectionPreferences? prefs = null) =>
        new(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind, Stamp, files, registry, prefs ?? new SelectionPreferences(true, false, true, false));

    [Test]
    public void AllerRetourConservePorteeCheminLibelleEtCategorie()
    {
        var files = new[] { File(SoftwareScope.Roaming, "Notepad++"), File(SoftwareScope.Local, @"Google\Chrome\User Data", SoftwareCategory.Modele) };
        var registry = new[] { Reg(@"Software\Notepad++") };
        var decoded = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(Doc(files, registry)));

        Check.Equal(2, decoded.Files.Count, "deux dossiers");
        Check.Equal(SoftwareScope.Local, decoded.Files[0].Scope, "tri par portée : local avant roaming");
        Check.Equal(SoftwareCategory.Modele, decoded.Files[0].Category, "le modèle commun garde sa catégorie");
        Check.Equal(@"Google\Chrome\User Data", decoded.Files[0].Rel, "chemin conservé");
        Check.Equal(SoftwareScope.Roaming, decoded.Files[1].Scope, "roaming");
        Check.True(decoded.Files[1].Selected, "un élément enregistré est retenu");
        Check.Null(decoded.Files[1].Size, "aucune taille inventée");
        Check.False(decoded.Files[1].SizeMeasured, "jamais présentée comme mesurée");
        Check.Equal(1, decoded.Registry.Count, "une clé");
        Check.Equal(SoftwareScope.Hkcu, decoded.Registry[0].Scope, "portée du registre");
    }

    [Test]
    public void CanonisationDeterministeEtDedoublonnage()
    {
        var a = Doc(
            [File(SoftwareScope.Roaming, "vlc"), File(SoftwareScope.Roaming, "Notepad++"), File(SoftwareScope.Roaming, "Notepad++")],
            [Reg(@"Software\VideoLAN\VLC")]);
        var b = Doc(
            [File(SoftwareScope.Roaming, "Notepad++"), File(SoftwareScope.Roaming, "vlc")],
            [Reg(@"Software\VideoLAN\VLC")]);

        var bytesA = SoftwareSelectionCodec.Encode(a);
        var bytesB = SoftwareSelectionCodec.Encode(b);
        Check.True(bytesA.AsSpan().SequenceEqual(bytesB), "la même sélection canonise à l'identique (doublon retiré, ordre figé)");
        Check.Equal(2, SoftwareSelectionCodec.Decode(bytesA).Files.Count, "le doublon n'est pas écrit deux fois");
    }

    [Test]
    public void PreferencesFontPartieDuDocumentEtReviennent()
    {
        var prefs = new SelectionPreferences(true, true, false, true);
        var decoded = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, "vlc")], [], prefs)));
        Check.Equal(prefs, decoded.Preferences, "les préférences mémorisées reviennent à l'identique");
    }

    [Test]
    public void ProprieteInattendueOuAbsenteEstRefusee()
    {
        var good = @"{""schemaVersion"":1,""kind"":""SelectionLogiciels"",""savedUtc"":""2026-10-25T10:00:00Z"",""files"":[],""registry"":[],""preferences"":{""migApp"":false,""vss"":false,""continueOnError"":false,""encrypted"":false}}";
        Check.NotNull(SoftwareSelectionCodec.Decode(Bytes(good)), "document minimal valide accepté");

        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(good.Replace("\"files\":[]", "\"files\":[],\"compte\":\"x\""))), "propriété interdite refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(good.Replace("\"kind\":\"SelectionLogiciels\",", ""))), "propriété absente refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(good.Replace("SelectionLogiciels", "AutreChose"))), "nature inconnue refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(good.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"))), "version non prise en charge refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(good.Replace("2026-10-25T10:00:00Z", "25/10/2026"))), "horodatage invalide refusé");
    }

    [Test]
    public void CheminsEtLibellesInvalidesSontRefuses()
    {
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, @"foo\..\bar")], [])), "traversée .. refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, @"\absolu")], [])), "chemin absolu refusé");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, "C:\\Windows")], [])), "lettre de lecteur refusée");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, "CON")], [])), "nom d'appareil réservé refusé");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([], [Reg(@"Software\Bad:Key")])), "deux-points refusés dans une clé");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Hkcu, "MauvaisePortee")], [])), "portée fichier invalide refusée");
    }

    [Test]
    public void LibelleAssainiEtTronque()
    {
        var control = ((char)1).ToString();
        var item = new SoftwareItem(SoftwareScope.Roaming, "vlc", "vlc" + control + "bad", SoftwareCategory.Donnees, true, false, false, null, false, false);
        var decoded = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(Doc([item], [])));
        Check.False(decoded.Files[0].Name.Contains((char)1), "caractère de contrôle retiré");
        Check.Equal("vlc bad", decoded.Files[0].Name, "caractère de contrôle remplacé par une espace");

        var longName = new string('x', 400);
        var longItem = new SoftwareItem(SoftwareScope.Roaming, "vlc", longName, SoftwareCategory.Donnees, true, false, false, null, false, false);
        var back = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(Doc([longItem], [])));
        Check.Equal(SoftwareSelectionCodec.MaxNameLength, back.Files[0].Name.Length, "libellé tronqué à la borne");
    }

    [Test]
    public void DoubleEtDepassementDeBorneRefuses()
    {
        var dup = @"{""schemaVersion"":1,""kind"":""SelectionLogiciels"",""savedUtc"":""2026-10-25T10:00:00Z"",""files"":[{""scope"":""roaming"",""rel"":""vlc"",""name"":""vlc"",""category"":""donnees""},{""scope"":""roaming"",""rel"":""vlc"",""name"":""vlc"",""category"":""donnees""}],""registry"":[],""preferences"":{""migApp"":false,""vss"":false,""continueOnError"":false,""encrypted"":false}}";
        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Decode(Bytes(dup)), "élément en double refusé à la relecture");

        var many = new SoftwareItem[SoftwareSelectionCodec.MaxItems + 1];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = File(SoftwareScope.Roaming, "dossier" + i);
        }

        Check.Throws<FilproOperationException>(() => SoftwareSelectionCodec.Encode(Doc(many, [])), "au-delà de la borne d'éléments refusé");
    }

    [Test]
    public void DocumentSeReconvertEnDerniereSelection()
    {
        var decoded = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(Doc([File(SoftwareScope.Roaming, "vlc")], [Reg(@"Software\VideoLAN\VLC")])));
        var last = decoded.ToLastSelection("S-1-5-21-111222333-444555666-1000");
        Check.Equal("S-1-5-21-111222333-444555666-1000", last.Sid, "le SID vient de la clé de stockage, jamais du document");
        Check.Equal(1, last.Files.Count, "un dossier");
        Check.Equal(1, last.Registry.Count, "une clé");
        Check.Equal(Stamp, last.CapturedAt, "horodatage conservé");
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);
}
