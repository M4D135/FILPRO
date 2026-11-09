using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Contrats portables sur une façade en mémoire ; aucune validation du système de fichiers Windows.</summary>
internal sealed class SoftwareFileDiscoveryTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "filpro-discovery-fake", "profil"));
    private static string AppData(SoftwareScope scope) => Path.Combine(Root, "AppData", scope == SoftwareScope.Roaming ? "Roaming" : "Local");
    private static string At(SoftwareScope scope, string rel) => Path.Combine(AppData(scope), rel.Replace('\\', Path.DirectorySeparatorChar));
    private static SoftwareItem Item(SoftwareScope scope, string rel) => new(scope, rel, rel, SoftwareCategory.Modele, true, true, true, 100, true, true);

    [Test]
    public void CatalogueFinEtChoixMemorisesSontRelusSansTaillesInventees()
    {
        var fs = NewFileSystem();
        fs.Add(At(SoftwareScope.Local, @"Google\Chrome\User Data"));
        fs.Add(At(SoftwareScope.Roaming, "vlc"));
        fs.Add(At(SoftwareScope.Roaming, @"SAP\SAP GUI"));
        fs.Add(At(SoftwareScope.Roaming, @"MonLogiciel\Preferences"));
        fs.Add(At(SoftwareScope.Local, "Preferences"));
        var reading = new WindowsSoftwareFileReader(fs).Read(Root,
            [Item(SoftwareScope.Roaming, @"MonLogiciel\Preferences"), Item(SoftwareScope.Local, "Disparu")], CancellationToken.None);
        Check.True(reading.Complete, "le périmètre explicitement déclaré a été relu");
        Check.True(reading.Items.Single(item => item.Rel == @"Google\Chrome\User Data").Recommend, "Chrome garde la recommandation V1");
        Check.False(reading.Items.Single(item => item.Rel == "vlc").Recommend, "logiciel connu n'implique pas recommandé");
        Check.False(reading.Items.Single(item => item.Rel == @"SAP\SAP GUI").Recommend, "recette SAP non recommandée");
        Check.Equal(SoftwareCategory.Modele, reading.Items.Single(item => item.Rel == @"MonLogiciel\Preferences").Category, "modèle mémorisé conservé");
        Check.Equal(SoftwareCategory.Preferences, reading.Items.Single(item => item.Rel == "Preferences").Category, "classification des dossiers génériques conservée");
        Check.False(reading.Items.Any(item => item.Rel is "Google" or "SAP" or "MonLogiciel" or "Disparu"), "aucun parent trop large ni dossier absent inventé");
        Check.True(reading.Items.All(item => item.Size is null && !item.SizeMeasured && !item.Selected), "preuves fraîches et choix restent distincts");
        Check.Equal(2, fs.EnumeratorsDisposed, "les deux énumérateurs sont fermés");
    }

    [Test]
    public void AliasWindowsLocauxExclusSansSuivreLesLiensEtChoixExplicitesToujoursControles()
    {
        var fs = NewFileSystem();
        var aliases = new[] { "Application Data", "History", "Temporary Internet Files" };
        foreach (var name in aliases)
        {
            var path = At(SoftwareScope.Local, name);
            fs.Add(path);
            fs.Attributes[path] |= FileAttributes.ReparsePoint;
        }
        fs.Add(At(SoftwareScope.Roaming, "vlc"));
        var reader = new WindowsSoftwareFileReader(fs);
        var result = reader.Read(Root, [], CancellationToken.None);
        Check.True(result.Complete, "les alias standard Windows ne rendent pas tout profil incomplet");
        Check.True(result.Items.Any(item => item.Rel == "vlc"), "les réglages logiciels restent découverts");
        Check.False(fs.Probed.Any(path => aliases.Any(name => path == At(SoftwareScope.Local, name))), "aucun alias générique n'est suivi");
        Check.False(reader.Read(Root, [Item(SoftwareScope.Local, "Application Data")], CancellationToken.None).Complete,
            "un alias choisi explicitement reste refusé comme lien");
        var unknown = At(SoftwareScope.Local, "AutreLien");
        fs.Add(unknown);
        fs.Attributes[unknown] |= FileAttributes.ReparsePoint;
        Check.False(reader.Read(Root, [], CancellationToken.None).Complete, "un lien inconnu reste bloquant");
    }

    [Test]
    public void AbsenceEtAccesRefuseDunChoixNeSontPasConfondus()
    {
        var fs = NewFileSystem();
        var selected = Item(SoftwareScope.Roaming, "ChoixPrive");
        var reader = new WindowsSoftwareFileReader(fs);
        Check.True(reader.Read(Root, [selected], CancellationToken.None).Complete, "absence effective prouvée");
        fs.Errors[At(SoftwareScope.Roaming, "ChoixPrive")] = new UnauthorizedAccessException();
        var refused = reader.Read(Root, [selected], CancellationToken.None);
        Check.False(refused.Complete, "un accès refusé empêche toute mémorisation de remplacement");
        Check.Equal(0, refused.Items.Count, "aucune présence n'est inventée à partir de la mémoire");
        Check.NotNull(refused.Error, "l'incomplétude a une explication");
    }

    [Test]
    public void JonctionEtRacineDeVolumeSontRefuseesAvantLaDescente()
    {
        var fs = NewFileSystem();
        var appData = Path.Combine(Root, "AppData");
        fs.Attributes[appData] = FileAttributes.Directory | FileAttributes.ReparsePoint;
        var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.False(result.Complete, "jonction refusée");
        Check.False(fs.Probed.Any(path => path.StartsWith(appData + Path.DirectorySeparatorChar, StringComparison.Ordinal)), "aucun enfant sous la jonction n'est interrogé");
        Check.Equal(0, fs.Enumerated.Count, "aucune énumération au-delà du lien");
        fs.Probed.Clear();
        Check.False(new WindowsSoftwareFileReader(fs).Read(Path.GetPathRoot(Root), [], CancellationToken.None).Complete, "racine de volume refusée comme profil");
        Check.Equal(0, fs.Probed.Count, "racine refusée avant E/S");
    }

    [Test]
    public void DossierGeneriqueDisparuOuHorsRacineRendLaLecturePartielle()
    {
        var fs = NewFileSystem();
        fs.Entries[AppData(SoftwareScope.Roaming)] = [At(SoftwareScope.Roaming, "DisparuPendantLecture")];
        var reading = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.False(reading.Complete, "un résultat énuméré puis absent n'est pas un inventaire complet");
        Check.Contains("devenu absent", reading.Error, "disparition annoncée");
        var outside = Path.GetFullPath(Path.Combine(Root, "..", "hors-profil"));
        fs.Entries[AppData(SoftwareScope.Roaming)] = [outside];
        fs.Probed.Clear();
        Check.False(new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None).Complete, "résultat hors profil refusé");
        Check.False(fs.Probed.Contains(outside), "le chemin extérieur n'est jamais interrogé");
    }

    [Test]
    public void NomDeDossierNonRepresentableEstTuSansRendreLInventairePartiel()
    {
        // Noms qu'aucune règle USMT ne peut écrire et que la normalisation des chemins laisse intacts sur Windows comme ailleurs.
        string[] refused = ["Nom|alt", "Etoile*", "Question?", "Guillemet\"", "Chevron<", "Ctl\u0001"];
        var fs = NewFileSystem();
        foreach (var name in refused) fs.Add(At(SoftwareScope.Roaming, name));
        fs.Add(At(SoftwareScope.Roaming, "vlc"));
        var reading = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.True(reading.Complete, "un dossier qu'aucune règle USMT ne peut écrire ne bloque ni les choix ni la sauvegarde");
        Check.Null(reading.Error, "aucun motif d'inventaire partiel");
        Check.Equal("vlc", reading.Items.Single().Rel, "la découverte continue après les noms tus");
        Check.False(fs.Probed.Any(path => refused.Any(name => path == At(SoftwareScope.Roaming, name))), "aucun nom tu n'est interrogé");
    }

    [Test]
    public void BornesDeNombreEtDeTempsConserventUnResultatPartiel()
    {
        var fs = NewFileSystem();
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" }) fs.Add(At(SoftwareScope.Roaming, name));
        var result = new WindowsSoftwareFileReader(fs, limits: new(2)).Read(Root, [], CancellationToken.None);
        Check.False(result.Complete, "borne de nombre annoncée");
        Check.Equal(2, result.Items.Count, "seuls deux dossiers génériques retenus");
        Check.Equal(2, fs.EnumeratorsDisposed, "énumérateurs fermés même après limite");

        var clock = new ManualClock();
        fs.OnNext = () => clock.Advance(TimeSpan.FromSeconds(2));
        var timed = new WindowsSoftwareFileReader(fs, clock, new(1000, TimeSpan.FromSeconds(1))).Read(Root, [], CancellationToken.None);
        Check.False(timed.Complete, "dépassement du temps entre lectures annoncé");
        Check.Equal(0, timed.Items.Count, "aucune présence publiée après dépassement");
    }

    [Test]
    public void AnnulationEtErreurFermentLEnumerateur()
    {
        var fs = NewFileSystem();
        fs.Add(At(SoftwareScope.Roaming, "Alpha"));
        using var cancellation = new CancellationTokenSource();
        fs.OnNext = cancellation.Cancel;
        Check.Throws<OperationCanceledException>(() => new WindowsSoftwareFileReader(fs).Read(Root, [], cancellation.Token), "annulation propagée");
        Check.Equal(1, fs.EnumeratorsDisposed, "l'énumérateur actif est fermé à l'annulation");
        fs.OnNext = () => throw new IOException("lecture interrompue");
        var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.False(result.Complete, "erreur d'énumération ne devient pas absence");
        Check.Equal(3, fs.EnumeratorsDisposed, "les ressources des deux nouvelles portées ont aussi été fermées");
    }

    [Test]
    public void CheminSupplementaireInvalideRefuseAvantTouteLecture()
    {
        var fs = NewFileSystem();
        Check.Throws<FilproOperationException>(() => new WindowsSoftwareFileReader(fs).Read(Root,
            [Item(SoftwareScope.Roaming, @"..\ailleurs")], CancellationToken.None), "validation du codec avant E/S");
        Check.Equal(0, fs.Probed.Count, "aucun attribut lu");
    }

    [Test]
    public void ProfilDisparuPendantLaLectureNeDevientPasUnInventaireVideComplet()
    {
        var fs = NewFileSystem();
        var rootReads = 0;
        fs.OnProbe = path =>
        {
            if (path == Root && ++rootReads == 2) fs.Attributes.Remove(Root);
        };
        var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.False(result.Complete, "disparition du profil détectée avant publication");
        Check.Contains("profil a disparu", result.Error, "la cause de l'incomplétude est explicite");
    }

    private const FileAttributes WindowsAlias = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden
        | FileAttributes.System | FileAttributes.NotContentIndexed;

    [Test]
    public void AliasWindowsTraduitIgnoreSansSuivreEtLaPorteeContinue()
    {
        // Premier niveau Local relevé sur Windows 11 fr-FR (VM FILPRO-FIX), plus deux logiciels fictifs autour de « Historique ».
        var fs = NewFileSystem();
        foreach (var name in new[] { "AcmeAvant", "ConnectedDevicesPlatform", "D3DSCache", "JetBrains", "Microsoft", "Packages", "Publishers", "Temp", "VirtualStore" })
            fs.Add(At(SoftwareScope.Local, name));
        var aliases = new[] { "Application Data", "Historique", "Temporary Internet Files", "Verlauf" };
        foreach (var name in aliases)
        {
            var path = At(SoftwareScope.Local, name);
            fs.Add(path);
            fs.Attributes[path] = WindowsAlias;
        }
        fs.Add(At(SoftwareScope.Roaming, "vlc"));
        var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.True(result.Complete, "un alias Windows traduit ne rend pas le profil incomplet");
        Check.Null(result.Error, "aucune raison d'incomplétude");
        Check.True(result.Items.Any(item => item.Scope == SoftwareScope.Local && item.Rel == "AcmeAvant"), "entrée avant l'alias retenue");
        Check.True(result.Items.Any(item => item.Scope == SoftwareScope.Local && item.Rel == "JetBrains"), "entrée après l'alias retenue : la portée continue");
        Check.False(result.Items.Any(item => aliases.Contains(item.Rel)), "aucun alias proposé comme logiciel");
        Check.False(fs.Probed.Any(path => aliases.Any(name => path.StartsWith(At(SoftwareScope.Local, name), StringComparison.OrdinalIgnoreCase))),
            "aucun alias ni descendant n'est interrogé");
    }

    [Test]
    public void LienUtilisateurSignaleSansInterrompreLaPorteeNiEtreSuivi()
    {
        var fs = NewFileSystem();
        fs.Add(At(SoftwareScope.Local, "AcmeAvant"));
        fs.Add(At(SoftwareScope.Local, "ZoomApp"));
        var links = new Dictionary<string, FileAttributes>
        {
            ["MonLien"] = FileAttributes.Directory | FileAttributes.ReparsePoint,
            ["LienCache"] = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden,
            ["LienSysteme"] = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.System,
        };
        foreach (var (name, attributes) in links)
        {
            var path = At(SoftwareScope.Local, name);
            fs.Add(path);
            fs.Attributes[path] = attributes;
        }
        var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
        Check.False(result.Complete, "un lien utilisateur reste une découverte partielle");
        Check.Contains("lien ou une jonction non suivi", result.Error, "la cause est le lien, pas une interruption");
        Check.False(result.Error!.Contains("interrompue", StringComparison.Ordinal), "la portée n'est plus interrompue");
        Check.True(result.Items.Any(item => item.Rel == "AcmeAvant") && result.Items.Any(item => item.Rel == "ZoomApp"), "les voisins restent découverts");
        Check.False(result.Items.Any(item => links.ContainsKey(item.Rel)), "aucun lien proposé");
        Check.False(fs.Probed.Any(path => links.Keys.Any(name => path.StartsWith(At(SoftwareScope.Local, name), StringComparison.OrdinalIgnoreCase))),
            "aucun lien n'est interrogé ni suivi");
    }

    [Test]
    public void LienSeulementCacheOuSeulementSystemeNEstPasUnAlias()
    {
        // Chaque lien seul dans sa lecture : la raison dédupliquée ne masque plus un saut silencieux.
        foreach (var attributes in new[] { FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden,
                     FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.System })
        {
            var fs = NewFileSystem();
            fs.Add(At(SoftwareScope.Local, "AcmeAvant"));
            var link = At(SoftwareScope.Local, "Lien");
            fs.Add(link);
            fs.Attributes[link] = attributes;
            var result = new WindowsSoftwareFileReader(fs).Read(Root, [], CancellationToken.None);
            Check.False(result.Complete, attributes + " : seul le couple caché + système désigne un alias Windows");
            Check.Contains("lien ou une jonction non suivi", result.Error, attributes + " : le lien est signalé");
        }
    }

    [Test]
    public void LienReelSignaleParLEnumerationPhysique()
    {
        for (var parent = new DirectoryInfo(Path.GetTempPath()); parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null) Check.Skip("Dossier temporaire sous un lien (" + parent.FullName + ") : profil réel non représentable.");
        var root = Path.Combine(Path.GetTempPath(), "filpro-discovery-real-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "AppData", "Local");
        var link = Path.Combine(local, "MonLien");
        Directory.CreateDirectory(Path.Combine(local, "AcmeAvant"));
        Directory.CreateDirectory(Path.Combine(root, "AppData", "Roaming"));
        Directory.CreateDirectory(Path.Combine(root, "cible"));
        try
        {
            try { Directory.CreateSymbolicLink(link, Path.Combine(root, "cible")); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { Check.Skip("Création de lien refusée sur cet hôte : " + ex.Message); }
            // Les attributs viennent de l'enregistrement du dossier parent : le lien n'est ni interrogé ni suivi.
            var result = new WindowsSoftwareFileReader().Read(root, [], CancellationToken.None);
            Check.False(result.Complete, "un lien réel reste une découverte partielle");
            Check.Contains("lien ou une jonction non suivi", result.Error, "l'énumération physique rapporte le point d'analyse");
            Check.False(result.Error!.Contains("interrompue", StringComparison.Ordinal), "la portée n'est pas interrompue");
            Check.True(result.Items.Any(item => item.Rel == "AcmeAvant"), "le voisin réel reste découvert");
            Check.False(result.Items.Any(item => item.Rel == "MonLien"), "aucun lien proposé");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void AliasWindowsTraduitChoisiExplicitementResteRefuse()
    {
        var fs = NewFileSystem();
        var alias = At(SoftwareScope.Local, "Historique");
        fs.Add(alias);
        fs.Attributes[alias] = WindowsAlias;
        foreach (var rel in new[] { "Historique", @"Historique\Sous" })
        {
            var result = new WindowsSoftwareFileReader(fs).Read(Root, [Item(SoftwareScope.Local, rel)], CancellationToken.None);
            Check.False(result.Complete, rel + " : un choix explicite sous un alias reste refusé comme lien");
            Check.Contains("dossier explicite est illisible ou lié", result.Error, rel + " : la sélection précédente est conservée");
            Check.False(result.Items.Any(item => item.Rel.StartsWith("Historique", StringComparison.OrdinalIgnoreCase)), rel + " : aucune présence inventée");
        }
    }

    private static FakeFileSystem NewFileSystem()
    {
        var fs = new FakeFileSystem();
        fs.Add(AppData(SoftwareScope.Local));
        fs.Add(AppData(SoftwareScope.Roaming));
        return fs;
    }

    private sealed class FakeFileSystem : ISoftwareFileSystem
    {
        public Dictionary<string, FileAttributes> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Exception> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Probed { get; } = [];
        public List<string> Enumerated { get; } = [];
        public int EnumeratorsDisposed { get; private set; }
        public Action? OnNext { get; set; }
        public Action<string>? OnProbe { get; set; }

        public void Add(string path)
        {
            string? current = path;
            while (!string.IsNullOrEmpty(current))
            {
                Attributes[current] = FileAttributes.Directory;
                current = Path.GetDirectoryName(current);
            }
        }

        public FileAttributes GetAttributes(string path)
        {
            Probed.Add(path);
            OnProbe?.Invoke(path);
            if (Errors.TryGetValue(path, out var error)) throw error;
            return Attributes.TryGetValue(path, out var value) ? value : throw new DirectoryNotFoundException();
        }

        public IEnumerable<SoftwareDirectoryEntry> EnumerateDirectories(string path)
        {
            Enumerated.Add(path);
            try
            {
                var values = Entries.TryGetValue(path, out var provided) ? provided
                    : Attributes.Keys.Where(item => string.Equals(Path.GetDirectoryName(item), path, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
                foreach (var value in values)
                {
                    OnNext?.Invoke();
                    // Comme l'enregistrement NTFS du parent : attributs de l'entrée au moment de l'énumération, sans GetAttributes.
                    yield return new(value, Attributes.TryGetValue(value, out var attributes) ? attributes : FileAttributes.Directory);
                }
            }
            finally { EnumeratorsDisposed++; }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan span) => ticks += span.Ticks;
    }
}
