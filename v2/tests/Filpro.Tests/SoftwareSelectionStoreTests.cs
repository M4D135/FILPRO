using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Persistance atomique de la sélection : aller-retour, conservation des préférences, remplacement et honnêteté.</summary>
internal sealed class SoftwareSelectionStoreTests
{
    private const string Sid = "S-1-5-21-111222333-444555666-777888999-1000";
    private static readonly DateTimeOffset Stamp = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static SoftwareItem FileItem(SoftwareScope scope, string rel, SoftwareCategory category = SoftwareCategory.Donnees) =>
        new(scope, rel, rel, category, true, false, false, null, false, false);

    private static SoftwareItem Reg(string rel) =>
        new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, true, false, false, null, false, false);

    private static LastSelection Selection(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry) =>
        new(Sid, files, registry, Stamp);

    [Test]
    public void SauvegardePuisLectureConserveLaSelection()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            var written = SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "Notepad++"), FileItem(SoftwareScope.Local, @"Google\Chrome\User Data", SoftwareCategory.Modele)], [Reg(@"Software\Notepad++")]));
            Check.True(File.Exists(written), "le document est écrit au chemin absolu rendu");

            var doc = SoftwareSelectionStore.Read(path);
            Check.NotNull(doc, "relecture réussie");
            Check.Equal(2, doc!.Files.Count, "deux dossiers relus");
            Check.Equal(SoftwareCategory.Modele, doc.Files.Single(f => f.Scope == SoftwareScope.Local).Category, "le modèle commun survit à l'aller-retour");
            Check.Equal(1, doc.Registry.Count, "une clé relue");
            var last = doc.ToLastSelection(Sid);
            Check.Equal(Sid, last.Sid, "le SID vient de l'appelant");
            Check.Equal(Stamp, last.CapturedAt, "horodatage conservé");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void LesPreferencesExistantesSontPreservees()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            var prefs = new SelectionPreferences(true, true, false, true);
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), prefs);

            // Seconde sauvegarde de la sélection, sans préférences explicites : les réglages existants sont conservés.
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "Notepad++")], [Reg(@"Software\Notepad++")]));

            var doc = SoftwareSelectionStore.Read(path);
            Check.NotNull(doc, "relecture réussie");
            Check.Equal(prefs, doc!.Preferences, "les préférences déjà enregistrées ne sont pas réinitialisées");
            Check.Equal("Notepad++", doc.Files.Single().Rel, "la sélection, elle, est bien remplacée");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void PreferencesExplicitesOuReinitialisationSelonDemande()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            var a = new SelectionPreferences(true, false, false, false);
            var b = new SelectionPreferences(false, true, true, false);
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), a);

            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), b);
            Check.Equal(b, SoftwareSelectionStore.Read(path)!.Preferences, "des préférences explicites écrasent les anciennes");

            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), preferences: null, preserveExistingPreferences: false);
            Check.Equal(SoftwareSelectionCodec.DefaultPreferences, SoftwareSelectionStore.Read(path)!.Preferences, "sans conservation : retour aux préférences par défaut");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void UnEchecDeSerialisationLaisseLeDocumentPrecedent()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], [Reg(@"Software\VideoLAN\VLC")]));

            Check.Throws<FilproOperationException>(
                () => SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, @"foo\..\bar")], [])),
                "une sélection invalide est refusée avant toute écriture");

            var doc = SoftwareSelectionStore.Read(path);
            Check.NotNull(doc, "le document précédent est intact");
            Check.Equal("vlc", doc!.Files.Single().Rel, "aucune écriture partielle n'a eu lieu");
            Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "aucun fichier temporaire résiduel");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void LectureAbsenteOuInvalideRendNullSansException()
    {
        var dir = NewDir();
        try
        {
            Check.Null(SoftwareSelectionStore.Read(Path.Combine(dir, "inexistant.json")), "document absent : null, jamais inventé");

            var corrupt = Path.Combine(dir, "corrompu.json");
            File.WriteAllText(corrupt, "{ ceci n'est pas du json ");
            Check.Null(SoftwareSelectionStore.Read(corrupt), "document illisible : null, aucune exception vers l'appelant");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void LeDossierParentEstCreeEtLeCheminTropLongEstRefuse()
    {
        var dir = NewDir();
        try
        {
            var nested = Path.Combine(dir, "Selections", "derniere.json");
            var written = SoftwareSelectionStore.Save(nested, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []));
            Check.True(File.Exists(written), "le dossier parent manquant est créé");

            Check.Throws<FilproOperationException>(
                () => SoftwareSelectionStore.Save(Path.Combine(dir, new string('n', 300) + ".json"), Selection([FileItem(SoftwareScope.Roaming, "vlc")], [])),
                "un chemin dépassant la limite Windows est refusé avant écriture");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void CheminsParProfilBornesEtIndependantsDeLaSaisie()
    {
        var dir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "filpro-selection-path"));
        var first = SoftwareSelectionStore.GetProfilePath(dir, Sid);
        var second = SoftwareSelectionStore.GetProfilePath(dir, "S-1-5-21-111222333-444555666-777888999-1001");
        Check.Equal(dir, Path.GetDirectoryName(first), "le fichier reste directement dans le dossier de préférences");
        Check.Equal(first, SoftwareSelectionStore.GetProfilePath(dir, Sid), "même SID : même fichier");
        Check.False(first == second, "deux profils n'utilisent pas le même document");
        Check.False(Path.GetFileName(first).Contains(Sid, StringComparison.Ordinal), "le SID n'est pas utilisé comme nom visible");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionStore.GetProfilePath(dir, "../ailleurs"), "un chemin ne peut pas remplacer un SID");
        Check.Throws<FilproOperationException>(() => SoftwareSelectionStore.GetProfilePath("relatif", Sid), "dossier relatif refusé");
    }

    [Test]
    public void DocumentTropGrandRefuseAvantAllocationDuContenu()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "trop-grand.json");
            using (var stream = File.Create(path)) stream.SetLength(SoftwareSelectionCodec.MaxBytes + 1);
            Check.Null(SoftwareSelectionStore.Read(path), "un document au-delà de la borne ne devient pas une sélection");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void AnnulationAvantEnregistrementConserveLeDocumentPrecedent()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []));
            var before = File.ReadAllBytes(path);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Check.Throws<OperationCanceledException>(() => SoftwareSelectionStore.Save(path, Selection([], []), cancellationToken: cancelled.Token),
                "une demande annulée ne remplace pas le document");
            Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "les octets précédents restent intacts");
            Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "aucun temporaire créé par une demande déjà annulée");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void DocumentInvalideOuFuturConserveAvantTouteEcriture()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            string[] documents =
            [
                "{ document interrompu ",
                """{"schemaVersion":2,"kind":"SelectionLogiciels","savedUtc":"2026-10-25T10:00:00Z","files":[],"registry":[],"preferences":{"migApp":true,"vss":true,"continueOnError":false,"encrypted":true}}""",
            ];
            foreach (var document in documents)
            {
                var before = Encoding.UTF8.GetBytes(document);
                File.WriteAllBytes(path, before);
                Check.Null(SoftwareSelectionStore.Read(path), "le lecteur public garde son contrat tolérant");
                var error = Check.Throws<FilproOperationException>(() => SoftwareSelectionStore.Save(path,
                    Selection([FileItem(SoftwareScope.Roaming, "vlc")], [])),
                    "une relecture invalide ne remplace pas les préférences à préserver");
                Check.Equal("selection.store", error.Code, "refus de conservation explicite");
                Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "le document original est conservé octet pour octet");
                Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "le refus précède la création d'un temporaire");
            }
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void DocumentVerrouilleRefuseSansPublicationNiReinitialisation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Check.Skip("Contrôle du verrou exclusif FileShare.None réservé à Windows.");
        }

        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            var preferences = new SelectionPreferences(true, true, false, true);
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), preferences);
            var before = File.ReadAllBytes(path);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var sharingEnforced = false;
                try
                {
                    using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                catch (IOException)
                {
                    sharingEnforced = true;
                }
                Check.True(sharingEnforced, "Windows doit appliquer le verrou exclusif FileShare.None");

                var error = Check.Throws<FilproOperationException>(() => SoftwareSelectionStore.Save(path,
                    Selection([FileItem(SoftwareScope.Roaming, "Notepad++")], [])),
                    "la lecture refusée doit interrompre l'enregistrement avant la publication");
                Check.Equal("selection.store", error.Code, "le partage refusé est un refus de préservation");
                Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "aucun temporaire après la lecture refusée");
            }
            Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "le document reste intact une fois le verrou libéré");
            Check.Equal(preferences, SoftwareSelectionStore.Read(path)!.Preferences, "aucune préférence n'a été réinitialisée");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Test]
    public void RemplacementExpliciteNeDependPasDesAnciennesPreferences()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "selection.json");
            File.WriteAllText(path, "{ document invalide ");
            var requested = new SelectionPreferences(false, true, false, true);
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "vlc")], []), requested);
            Check.Equal(requested, SoftwareSelectionStore.Read(path)!.Preferences, "les préférences explicitement fournies reprennent la main");

            File.WriteAllText(path, "{ document invalide ");
            SoftwareSelectionStore.Save(path, Selection([FileItem(SoftwareScope.Roaming, "Notepad++")], []), preserveExistingPreferences: false);
            Check.Equal(SoftwareSelectionCodec.DefaultPreferences, SoftwareSelectionStore.Read(path)!.Preferences,
                "la réinitialisation explicite conserve son comportement");
        }
        finally
        {
            Clean(dir);
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "filpro-sel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Clean(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
