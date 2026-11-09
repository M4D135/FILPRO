using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class OwnedCopiedHiveMountTests
{
    [Test]
    public static void CollisionRefuseeSansToucherLaRucheEtrangere()
    {
        var backend = new Backend { Mounted = true };
        var owner = new OwnedCopiedHiveMounts(backend);
        var error = Check.Throws<FilproOperationException>(() => owner.Open("unused"), "Collision refusée");
        Check.Equal("hive.ownership", error.Code, "Cause explicite");
        Check.Equal(0, backend.Loads, "Aucun chargement"); Check.Equal(0, backend.Unloads, "Aucun unload étranger");
    }

    [Test]
    public static void CopiePossedeeLibereeApresFermetureEtRetries()
    {
        var backend = new Backend { UnloadFailures = 1 };
        var owner = new OwnedCopiedHiveMounts(backend);
        var result = owner.Open("copy");
        Check.True(backend.LastTag!.StartsWith("FILPRO_V2_", StringComparison.Ordinal), "Tag distinct des SID Windows");
        result.Key!.Dispose();
        owner.Release("copy");
        Check.False(backend.Mounted, "Copie déchargée"); Check.Equal(2, backend.Unloads, "Échec transitoire repris");
        owner.Release("copy"); Check.Equal(2, backend.Unloads, "Le nom libéré n'est jamais réutilisé");
    }

    [Test]
    public static void MontageVivantDeCeProcessusNEstPasUnResidu()
    {
        var backend = new Backend();
        var owner = new OwnedCopiedHiveMounts(backend);
        var result = owner.Open("copy");
        var tag = backend.LastTag!;
        Check.True(owner.Owns(tag), "Montage suivi tant qu'il est vivant");
        Check.False(WindowsNativeExecutionSafety.IsResidualMount(tag, owner.Owns, backend.Claimed), "La lecture en cours n'est pas un résidu");
        Check.True(WindowsNativeExecutionSafety.IsResidualMount("FILPRO_V2_" + Guid.NewGuid().ToString("N"), owner.Owns, backend.Claimed), "Tag inconnu de ce processus : résidu");
        Check.False(WindowsNativeExecutionSafety.IsResidualMount("S-1-5-21-1-2-3-1001", owner.Owns, backend.Claimed), "Ruche Windows ignorée");
        result.Key!.Dispose();
        owner.Release("copy");
        Check.False(owner.Owns(tag), "Plus suivi après déchargement confirmé");
        Check.False(backend.Claimed(tag), "Marqueur rendu après déchargement confirmé");
        Check.True(WindowsNativeExecutionSafety.IsResidualMount(tag, owner.Owns, backend.Claimed), "Le même tag resté monté serait un résidu");
    }

    [Test]
    public static void MontageVivantDUnAutreProcessusNEstPasUnResidu()
    {
        var foreign = "FILPRO_V2_" + Guid.NewGuid().ToString("N");
        Check.False(WindowsNativeExecutionSafety.IsResidualMount(foreign, _ => false, tag => tag == foreign), "Le marqueur d'un processus vivant protège sa lecture");
        Check.True(WindowsNativeExecutionSafety.IsResidualMount(foreign, _ => false, _ => false), "Sans marqueur, le montage d'un processus disparu reste un résidu");
    }

    [Test]
    public static void MontageDechargePendantLeControleNEstPasUnResidu()
    {
        var tag = "FILPRO_V2_" + Guid.NewGuid().ToString("N");
        string[][] unloaded = [[tag, "S-1-5-18"], ["S-1-5-18"]];
        var calls = 0;
        Check.False(WindowsNativeExecutionSafety.HasResidualMount(() => unloaded[calls++], _ => false, _ => false), "Nom disparu avant la seconde liste : montage vivant libéré");
        Check.Equal(2, calls, "Seconde liste relue après le contrôle du marqueur");
        Check.True(WindowsNativeExecutionSafety.HasResidualMount(() => [tag], _ => false, _ => false), "Nom toujours présent sans suivi ni marqueur : résidu");
        Check.False(WindowsNativeExecutionSafety.HasResidualMount(() => [tag], _ => false, name => name == tag), "Marqueur vivant : aucun résidu");
        calls = 0;
        Check.False(WindowsNativeExecutionSafety.HasResidualMount(() => { calls++; return ["S-1-5-18"]; }, _ => false, _ => false), "Aucun candidat");
        Check.Equal(1, calls, "Aucune seconde liste sans candidat");
    }

    [Test]
    public static void MarqueurPrisAvantLeChargementEtGardeSiLeDechargementEchoue()
    {
        var refused = new Backend { ClaimRefused = true };
        Check.Equal("hive.ownership", Check.Throws<FilproOperationException>(() => new OwnedCopiedHiveMounts(refused).Open("copy"), "Marqueur déjà pris").Code, "Collision refusée");
        Check.Equal(0, refused.Loads, "Aucun chargement sans marqueur");
        var collision = new Backend { Mounted = true };
        Check.Throws<FilproOperationException>(() => new OwnedCopiedHiveMounts(collision).Open("copy"), "Nom déjà monté");
        Check.False(collision.Claimed(collision.LastClaim!), "Marqueur rendu après une collision");
        var failed = new Backend { OpenError = 5, UnloadFailures = 3 };
        var owner = new OwnedCopiedHiveMounts(failed);
        Check.Throws<FilproOperationException>(() => owner.Open("copy"), "Le premier nettoyage échoue");
        Check.True(failed.Claimed(failed.LastTag!), "Montage non confirmé : marqueur conservé");
        owner.CleanupUntransferred();
        Check.False(failed.Claimed(failed.LastTag!), "Marqueur rendu avec le déchargement confirmé");
    }

    [Test]
    public static void SondeAccesRefuseVautMontagePresent()
    {
        Check.True(WindowsCopiedHiveMountBackend.Probe(0, opened: true), "Clé ouverte");
        Check.True(WindowsCopiedHiveMountBackend.Probe(5, opened: false), "Racine illisible : le nom existe et reste à décharger");
        Check.False(WindowsCopiedHiveMountBackend.Probe(2, opened: false), "Nom absent");
        Check.False(WindowsCopiedHiveMountBackend.Probe(3, opened: false), "Chemin absent");
        foreach (var (code, opened) in new[] { (0, false), (1450, false), (6, false) })
            Check.Equal("hive.probe", Check.Throws<FilproOperationException>(() => WindowsCopiedHiveMountBackend.Probe(code, opened), "État inconnu " + code).Code, "Code conservé");
        var tag = "FILPRO_V2_" + Guid.NewGuid().ToString("N");
        Check.Equal(@"Global\" + tag, WindowsCopiedHiveMountBackend.ClaimName(tag), "Marqueur dans le namespace Global, visible des autres sessions");
        if (!OperatingSystem.IsWindows())
        {
            Check.False(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess(tag), "Aucun marqueur hors Windows");
            return;
        }
        var backend = new WindowsCopiedHiveMountBackend();
        Check.False(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess(tag), "Tag libre");
        using (backend.Claim(tag))
        {
            Check.True(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess(tag), "Marqueur visible par son nom");
            Check.Equal("hive.ownership", Check.Throws<FilproOperationException>(() => backend.Claim(tag), "Second marqueur").Code, "Tag déjà marqué refusé");
        }
        Check.False(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess(tag), "Marqueur détruit avec son handle");
        Check.False(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess("FILPRO_V2_pas-un-guid"), "Tag hors format jamais vivant");
        // Homonyme dont le propriétaire n'est pas Administrateurs, comme celui qu'un compte standard pourrait créer.
        var squatted = "FILPRO_V2_" + Guid.NewGuid().ToString("N");
        using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
        var security = new System.Security.AccessControl.EventWaitHandleSecurity();
        security.SetOwner(current.User!);
        security.AddAccessRule(new(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
            System.Security.AccessControl.EventWaitHandleRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        using (EventWaitHandleAcl.Create(false, EventResetMode.ManualReset, WindowsCopiedHiveMountBackend.ClaimName(squatted), out var created, security))
        {
            Check.True(created, "Homonyme créé");
            Check.False(WindowsCopiedHiveMountBackend.ClaimedByLiveProcess(squatted), "Un homonyme d'un autre propriétaire ne masque pas un résidu");
        }
    }

    [Test]
    public static void EchecOuvertureApresLoadNettoieLeMontage()
    {
        var backend = new Backend { OpenError = 5 };
        var owner = new OwnedCopiedHiveMounts(backend);
        Check.Throws<FilproOperationException>(() => owner.Open("copy"), "Ouverture refusée");
        Check.Equal(1, backend.Unloads, "Montage réussi malgré ouverture refusée déchargé");
        Check.False(backend.Mounted, "Plus de montage");
    }

    [Test]
    public static void EchecLoadAvecNomApparuNeDechargePasSansPreuve()
    {
        var backend = new Backend { LoadError = 183, AppearsDespiteLoadError = true };
        var blocked = false;
        var owner = new OwnedCopiedHiveMounts(backend, _ => blocked = true);
        var error = Check.Throws<FilproOperationException>(() => owner.Open("copy"), "Propriété inconnue");
        Check.Equal("hive.cleanup", error.Code, "Blocage de sécurité conservé");
        Check.Equal(0, backend.Unloads, "Jamais d'unload sans Load réussi"); Check.True(blocked, "Nouvelles mutations bloquées");
    }

    [Test]
    public static void NettoyageFinalReprendUnMontageNonTransfereSansToucherAuxHandlesActifs()
    {
        var failed = new Backend { OpenError = 5, UnloadFailures = 3 };
        var owner = new OwnedCopiedHiveMounts(failed);
        Check.Throws<FilproOperationException>(() => owner.Open("copy"), "Le premier nettoyage échoue");
        Check.True(failed.Mounted, "Preuve conservée pour reprise");
        owner.CleanupUntransferred();
        Check.False(failed.Mounted, "Le nettoyage final reprend le seul montage possédé sans handles transmis");
        var active = new Backend();
        var other = new OwnedCopiedHiveMounts(active);
        var opened = other.Open("active");
        other.CleanupUntransferred();
        Check.Equal(0, active.Unloads, "Jamais de nettoyage global pendant une lecture active");
        opened.Key!.Dispose(); other.Release("active");
    }

    [Test]
    public static void RefusAppKeyBadDbMonteSeulementLaCopie()
    {
        var backend = new Backend();
        var owner = new OwnedCopiedHiveMounts(backend);
        var refused = new Key(() => { });
        var opened = WindowsOfflineHiveNative.OpenOrMountCopy(new(1009, refused), @"C:\State\Hives\id\NTUSER.DAT", owner);
        Check.Equal(0, opened.ErrorCode, "Copie lue par le montage possédé");
        Check.Equal(@"C:\State\Hives\id\NTUSER.DAT", backend.LastPath, "Seule la copie reçue est chargée");
        opened.Key!.Dispose(); owner.Release(@"C:\State\Hives\id\NTUSER.DAT");
        Check.False(backend.Mounted, "Montage libéré");
        var corrupt = WindowsOfflineHiveNative.OpenOrMountCopy(new(1015, null), "copy", owner);
        Check.Equal(1015, corrupt.ErrorCode, "Ruche corrompue : code conservé");
        var denied = WindowsOfflineHiveNative.OpenOrMountCopy(new(5, null), "copy", owner);
        Check.Equal(5, denied.ErrorCode, "Racine refusée : code conservé, le montage serait aussi illisible");
        Check.Equal(1, backend.Loads, "Aucun montage pour 1015 ni 5");
    }

    private sealed class Backend : ICopiedHiveMountBackend
    {
        public bool Mounted; public bool AppearsDespiteLoadError; public int Loads; public int Unloads; public bool ClaimRefused;
        public int LoadError; public int OpenError; public int UnloadFailures; public string? LastTag; public string? LastPath; public string? LastClaim; private bool handleOpen;
        private readonly HashSet<string> claims = [];
        public bool Claimed(string tag) => claims.Contains(tag);
        public bool IsMounted(string tag) => Mounted;
        public IDisposable Claim(string tag)
        {
            if (ClaimRefused) throw new FilproOperationException("hive.ownership", "Marqueur déjà pris.");
            Check.True(claims.Add(LastClaim = tag), "Un seul marqueur par tag");
            return new Released(() => claims.Remove(tag));
        }
        public int Load(string tag, string path)
        {
            Check.True(Claimed(tag), "Marqueur pris avant le chargement");
            LastTag = tag; LastPath = path; Loads++; Mounted = LoadError == 0 || AppearsDespiteLoadError; return LoadError;
        }
        public RegistryKeyReadResult Open(string tag)
        { if (OpenError != 0) return new(OpenError, null); handleOpen = true; return new(0, new Key(() => handleOpen = false)); }
        public int Unload(string tag)
        {
            Check.False(handleOpen, "Root fermé avant déchargement"); Check.Equal(LastTag, tag, "Seul le tag possédé est traité");
            Unloads++; if (Unloads <= UnloadFailures) return 170; Mounted = false; return 0;
        }
    }
    private sealed class Released(Action onDispose) : IDisposable { public void Dispose() => onDispose(); }
    private sealed class Key(Action onClose) : IReadOnlyUserRegistryKey
    {
        public RegistryKeyReadResult OpenSubKey(string relativePath) => new(2, null);
        public RegistryNameReadResult ReadSubKeyName(int index) => new(259, null);
        public void Dispose() => onClose();
    }
}
