using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Contrôle des copies et de la propriété des handles sur une façade ; aucune API de ruche Windows exécutée.</summary>
internal static class OfflineUserHiveTests
{
    [Test]
    public static async Task OriginalJamaisMonteEtEnfantsOubliesFermes()
    {
        using var fixture = new Fixture();
        var native = new FakeNative();
        var manager = fixture.Manager(native);
        var lease = await manager.OpenCopyAsync(fixture.Original, default);
        Check.False(native.Path == fixture.Original, "Le chemin natif est une copie");
        Check.Equal("original", File.ReadAllText(native.Path!), "Copie exacte");
        _ = lease.Root.OpenSubKey(@"Software\Test"); // Le caller omet volontairement Dispose.
        Check.Equal(2, native.Active, "Racine et enfant possédés");
        await lease.DisposeAsync();
        Check.Equal(0, native.Active, "Tous les handles fermés");
        Check.Equal(1, native.Verifications, "Libération vérifiée après fermeture");
        Check.Equal("original", File.ReadAllText(fixture.Original), "L'original est intact");
        Check.False(File.Exists(native.Path), "La copie privée a été nettoyée");
    }

    [Test]
    public static async Task AnnulationApresOuvertureNettoieAvantDeRendreLaMain()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNative { OnOpen = cancellation.Cancel };
        var manager = fixture.Manager(native);
        await Check.ThrowsAsync<OperationCanceledException>(() => manager.OpenCopyAsync(fixture.Original, cancellation.Token), "Annulation rendue après nettoyage");
        Check.Equal(0, native.Active, "Aucun handle après exception");
        Check.Equal(1, native.Verifications, "Copie libérée");
        Check.Equal("original", File.ReadAllText(fixture.Original), "Original conservé");
    }

    [Test]
    public static async Task EchecDeLiberationResteBloquantEtPeutEtreRecontrole()
    {
        using var fixture = new Fixture();
        var native = new FakeNative { FailVerification = true };
        var manager = fixture.Manager(native);
        var lease = await manager.OpenCopyAsync(fixture.Original, default);
        _ = lease.Root.OpenSubKey("Software");
        var error = await Check.ThrowsAsync<FilproOperationException>(() => lease.DisposeAsync().AsTask(), "Une libération inconnue est un échec");
        Check.Equal("hive.cleanup", error.Code, "Erreur de cleanup conservée");
        Check.Equal(0, native.Active, "Handles néanmoins fermés");
        Check.True(File.Exists(native.Path), "Copie gardée pour examen");
        native.FailVerification = false;
        await manager.CleanupAsync();
        Check.False(File.Exists(native.Path), "Nettoyage achevé après preuve de libération");
    }

    [Test]
    public static async Task EchecNatifNeConservePasUnHandleRetourneParErreur()
    {
        using var fixture = new Fixture();
        var native = new FakeNative { ErrorCode = 5 };
        var manager = fixture.Manager(native);
        var error = await Check.ThrowsAsync<FilproOperationException>(() => manager.OpenCopyAsync(fixture.Original, default), "Accès natif refusé");
        Check.Equal("hive.load", error.Code, "Erreur explicite");
        Check.Equal(0, native.Active, "Handle erroné fermé");
        Check.Equal("original", File.ReadAllText(fixture.Original), "Jamais de modification originale");
        Check.False(File.Exists(native.Path), "Échec avant transfert : la copie libérée est nettoyée");
    }

    [Test]
    public static async Task NouvelEssaiApresNettoyagePartielNeReprouvePasUneCopieLiberee()
    {
        using var fixture = new Fixture();
        string? blocker = null;
        // Comme WindowsOfflineHiveNative : une copie absente ne peut plus être ouverte en exclusif.
        var native = new FakeNative { RequireCopy = true };
        native.OnOpen = () => Directory.CreateDirectory(blocker = Path.Combine(System.IO.Path.GetDirectoryName(native.Path!)!, "tenu"));
        var manager = fixture.Manager(native);
        var lease = await manager.OpenCopyAsync(fixture.Original, default);
        await Check.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask(), "Le dossier privé encore tenu fait échouer le premier nettoyage");
        Check.Equal(1, native.Verifications, "Libération prouvée une fois");
        Check.False(File.Exists(native.Path), "La copie est déjà effacée");
        Directory.Delete(blocker!);
        await manager.CleanupAsync();
        Check.Equal(1, native.Verifications, "Le nouvel essai ne reprouve pas une copie déjà libérée");
        Check.False(Directory.Exists(System.IO.Path.GetDirectoryName(native.Path!)), "Nettoyage achevé");
    }

    [Test]
    public static void ReplisPossedesLimitesAuxRefusAppKeyConnus()
    {
        foreach (var code in new[] { 87, 1009 })
            Check.True(WindowsOfflineHiveNative.UsesOwnedCopyMount(code), "Repli possédé sur la copie pour Win32 " + code);
        foreach (var code in new[] { 0, 2, 3, 5, 32, 33, 1015, 1016, 1017, 1314, 1450 })
            Check.False(WindowsOfflineHiveNative.UsesOwnedCopyMount(code), "Aucun montage pour Win32 " + code);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "filpro-hive-test-" + Guid.NewGuid().ToString("N"));
        public string Original { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root); var profile = Path.Combine(root, "Profile"); Directory.CreateDirectory(profile);
            Original = Path.Combine(profile, "NTUSER.DAT"); File.WriteAllText(Original, "original");
            File.WriteAllText(Original + ".LOG1", "journal");
        }
        public OfflineUserHiveManager Manager(FakeNative native) => new(new ExecutionFileSystem(), native, Path.Combine(root, "Private"));
        public void Dispose() => Directory.Delete(root, true);
    }
    private sealed class FakeNative : IOfflineHiveNative
    {
        public int Active;
        public int Verifications;
        public bool FailVerification;
        public bool RequireCopy;
        public int ErrorCode;
        public string? Path;
        public Action? OnOpen;
        public RegistryKeyReadResult OpenPrivateCopy(string path)
        { Path = path; var key = new FakeKey(this); OnOpen?.Invoke(); return new(ErrorCode, key); }
        public void VerifyReleased(string path)
        {
            Verifications++;
            Check.Equal(0, Active, "La vérification arrive après toutes les fermetures");
            if (FailVerification) throw new FilproOperationException("hive.cleanup", "Toujours occupée.");
            if (RequireCopy && !File.Exists(path)) throw new FilproOperationException("hive.cleanup", "Copie absente : libération non prouvable.");
        }
    }
    private sealed class FakeKey : IReadOnlyUserRegistryKey
    {
        private readonly FakeNative owner;
        private bool closed;
        public FakeKey(FakeNative owner) { this.owner = owner; owner.Active++; }
        public RegistryKeyReadResult OpenSubKey(string relativePath)
        { ObjectDisposedException.ThrowIf(closed, this); return new(0, new FakeKey(owner)); }
        public RegistryNameReadResult ReadSubKeyName(int index) => new(259, null);
        public void Dispose() { if (closed) throw new InvalidOperationException("Double fermeture native."); closed = true; owner.Active--; }
    }
}
