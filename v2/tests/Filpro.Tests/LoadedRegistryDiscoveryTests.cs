using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Preuves portables de décisions et de propriété des ressources sur façade ; aucune API Windows exécutée.</summary>
internal sealed class LoadedRegistryDiscoveryTests
{
    private const string Sid = "S-1-5-21-111222333-444555666-777888999-1001";
    private static SoftwareItem Item(string rel) => new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Modele, true, true, true, 42, true, true);

    [Test]
    public void FichiersEtClesConnusSeRegroupentApresLectureDePresence()
    {
        var source = new FakeRegistry();
        source.Add(@"Software\Google\Chrome");
        source.Add(@"Software\VideoLAN\VLC");
        source.Add(@"Software\Acme\Preferences");
        var result = new WindowsLoadedRegistryReader(source).Read(Sid,
            [Item(@"Software\Acme\Preferences"), Item(@"Software\Absent")], CancellationToken.None);
        Check.True(result.Complete, "lecture du périmètre déclarée complète");
        Check.Equal(3, result.Items.Count, "clés présentes seulement, sans parent générique trop large");
        Check.True(result.Items.Single(item => item.Rel == @"Software\Google\Chrome").Recommend, "recommandation Chrome V1");
        Check.False(result.Items.Single(item => item.Rel == @"Software\VideoLAN\VLC").Recommend, "reconnaissance VLC sans recommandation");
        Check.Equal(SoftwareCategory.Modele, result.Items.Single(item => item.Rel == @"Software\Acme\Preferences").Category, "modèle précis conservé");
        Check.True(result.Items.All(item => item.Size is null && !item.Selected && !item.SizeMeasured), "aucune ancienne mesure réutilisée");
        var file = new SoftwareItem(SoftwareScope.Local, @"Google\Chrome\User Data", "Chrome", SoftwareCategory.Donnees, false, true, true, null, false, false);
        var group = LastSelectionRules.Apply(Sid, [file], result.Items, null).Single(item => item.Id == "known:chrome");
        Check.True(group.Files.Count == 1 && group.Registry.Count == 1 && group.Selected == true, "fichiers et clés rejoignent réellement le même groupe recommandé");
        AssertClosed(source);
    }

    [Test]
    public void RucheAbsenteEtAccesRefuseRestentIndisponibles()
    {
        foreach (var code in new[] { 2, 3, 5 })
        {
            var source = new FakeRegistry { HiveError = code };
            var result = new WindowsLoadedRegistryReader(source).Read(Sid, [], CancellationToken.None);
            Check.False(result.Complete, "ni absence de ruche chargée ni refus ne prouvent un registre vide");
            Check.Equal(0, result.Items.Count, "aucune présence fictive");
            Check.Contains(code is 2 or 3 ? "non chargée" : "inaccessible", result.Error, "cause distincte conservée");
            Check.Equal(0, source.Opened, "aucun handle fabriqué après refus");
        }
    }

    [Test]
    public void CleExpliciteAbsenteEstDistincteDUneCleIllisible()
    {
        var source = new FakeRegistry();
        Check.True(new WindowsLoadedRegistryReader(source).Read(Sid, [Item(@"Software\Prive")], CancellationToken.None).Complete,
            "absence de clé prouvée dans une ruche réellement ouverte");
        source.Errors[@"Software\Prive"] = 5;
        var result = new WindowsLoadedRegistryReader(source).Read(Sid, [Item(@"Software\Prive")], CancellationToken.None);
        Check.False(result.Complete, "accès refusé empêche une sauvegarde de sélection amputée");
        Check.Contains("Win32 5", result.Error, "LSTATUS conservé");
        AssertClosed(source);
    }

    [Test]
    public void BornesEtErreurDEnumerationNeDeviennentPasUnSucces()
    {
        var source = new FakeRegistry();
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" }) source.Add("Software\\" + name);
        var limited = new WindowsLoadedRegistryReader(source, limits: new(2)).Read(Sid, [], CancellationToken.None);
        Check.False(limited.Complete, "la limite d'énumération est explicite");
        Check.Equal(2, limited.Items.Count, "deux entrées seulement retenues");
        AssertClosed(source);

        source.EnumerationError = 5;
        var failed = new WindowsLoadedRegistryReader(source).Read(Sid, [], CancellationToken.None);
        Check.False(failed.Complete, "erreur native d'énumération conservée");
        AssertClosed(source);

        source.EnumerationError = 0;
        var clock = new ManualClock();
        source.OnReadName = () => clock.Advance(TimeSpan.FromSeconds(2));
        var timed = new WindowsLoadedRegistryReader(source, clock, new(1000, TimeSpan.FromSeconds(1))).Read(Sid, [], CancellationToken.None);
        Check.False(timed.Complete, "limite de temps entre lectures conservée");
        AssertClosed(source);
    }

    [Test]
    public void AnnulationApresOuvertureOuEnumerationFermeTousLesHandles()
    {
        foreach (var phase in new[] { "clé", "énumération", "dernière ouverture" })
        {
            var source = new FakeRegistry();
            source.Add(@"Software\Google\Chrome");
            source.Add(@"Software\Alpha");
            using var cancellation = new CancellationTokenSource();
            if (phase == "énumération") source.OnReadName = cancellation.Cancel;
            else source.AfterOpen = path =>
            {
                if (path == (phase == "clé" ? @"Software\Google\Chrome" : "Software")) cancellation.Cancel();
            };
            Check.Throws<OperationCanceledException>(() => new WindowsLoadedRegistryReader(source).Read(Sid, [], cancellation.Token),
                "annulation propagée après " + phase);
            AssertClosed(source);
        }
    }

    [Test]
    public void ExceptionDuLecteurEtDisparitionGeneriqueFermentLesHandles()
    {
        var source = new FakeRegistry();
        source.ThrowOnOpen = @"Software\Google\Chrome";
        Check.False(new WindowsLoadedRegistryReader(source).Read(Sid, [], CancellationToken.None).Complete, "exception attendue transforme la lecture en résultat partiel");
        AssertClosed(source);
        source.ThrowOnOpen = null;
        source.Names = ["Efface"];
        var result = new WindowsLoadedRegistryReader(source).Read(Sid, [], CancellationToken.None);
        Check.False(result.Complete, "une clé énumérée puis disparue interdit la mémorisation");
        AssertClosed(source);
    }

    [Test]
    public void NomGeneriqueNonRepresentableNeDevientPasUneExclusionConnue()
    {
        var source = new FakeRegistry { Names = ["Microsoft/Acme"] };
        var result = new WindowsLoadedRegistryReader(source).Read(Sid, [], CancellationToken.None);
        Check.False(result.Complete, "le slash littéral d'un nom natif ne devient pas un séparateur de chemin");
        Check.Contains("ne peut pas être représenté", result.Error, "défaut de forme explicite");
        Check.False(source.PathsOpened.Contains(@"Software\Microsoft/Acme"), "aucune ouverture du nom refusé");
        AssertClosed(source);
    }

    [Test]
    public void SidEtCheminInvalidesSontRefusesAvantLesLectures()
    {
        var source = new FakeRegistry();
        var reader = new WindowsLoadedRegistryReader(source);
        foreach (var sid in new[] { Sid + "\n", "S-1-5-21-1-2-4294967296-4", "S-1-5-21-1-2-3-04", Sid + @"\Software" })
        {
            Check.Throws<FilproOperationException>(() => reader.Read(sid, [], CancellationToken.None), "SID non canonique refusé");
            Check.Throws<FilproOperationException>(() => new WindowsPosteSource(TimeProvider.System).ReadSoftware(sid, SoftwareQuery.Empty, CancellationToken.None),
                "validation avant préférence ou profil dans le fournisseur");
        }
        Check.Throws<FilproOperationException>(() => reader.Read(Sid, [Item(@"Software\..\Autre")], CancellationToken.None), "chemin complémentaire refusé");
        Check.Equal(0, source.HiveCalls, "aucun appel de façade avant validation");
    }

    private static void AssertClosed(FakeRegistry source)
    {
        Check.Equal(0, source.Active, "aucun handle détenu après lecture");
        Check.Equal(source.Opened, source.Closed, "chaque handle détenu a été fermé");
    }

    private sealed class FakeRegistry : ILoadedUserRegistry
    {
        private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase) { "Software" };
        public Dictionary<string, int> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> PathsOpened { get; } = [];
        public int HiveError { get; set; }
        public int EnumerationError { get; set; }
        public int HiveCalls { get; private set; }
        public int Opened { get; private set; }
        public int Closed { get; private set; }
        public int Active => Opened - Closed;
        public string? ThrowOnOpen { get; set; }
        public string[]? Names { get; set; }
        public Action<string>? AfterOpen { get; set; }
        public Action? OnReadName { get; set; }

        public void Add(string path)
        {
            var segments = path.Split('\\');
            for (var count = 1; count <= segments.Length; count++) keys.Add(string.Join("\\", segments.Take(count)));
        }

        public RegistryKeyReadResult OpenUserHive(string sid)
        {
            HiveCalls++;
            return HiveError == 0 ? NewKey("") : new(HiveError, null);
        }

        private RegistryKeyReadResult NewKey(string path)
        {
            Opened++;
            return new(0, new Key(this, path));
        }

        private sealed class Key(FakeRegistry owner, string path) : IReadOnlyUserRegistryKey
        {
            private bool disposed;

            public RegistryKeyReadResult OpenSubKey(string relativePath)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var full = path.Length == 0 ? relativePath : path + "\\" + relativePath;
                owner.PathsOpened.Add(full);
                if (full == owner.ThrowOnOpen) throw new IOException("lecture interrompue");
                var opened = owner.Errors.TryGetValue(full, out var error) ? new RegistryKeyReadResult(error, null)
                    : owner.keys.Contains(full) ? owner.NewKey(full) : new RegistryKeyReadResult(2, null);
                owner.AfterOpen?.Invoke(full);
                return opened;
            }

            public RegistryNameReadResult ReadSubKeyName(int index)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                owner.OnReadName?.Invoke();
                if (owner.EnumerationError != 0) return new(owner.EnumerationError, null);
                var names = owner.Names ?? owner.keys.Where(key => key.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)
                        && !key[(path.Length + 1)..].Contains('\\'))
                    .Select(key => key[(path.Length + 1)..]).Order(StringComparer.Ordinal).ToArray();
                return index < names.Length ? new(0, names[index]) : new(259, null);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.Closed++;
            }
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
