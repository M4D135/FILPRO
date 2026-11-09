using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Contrôles d'intégrité et de forme portables ; les partages de handles Windows ont une suite distincte.</summary>
internal static class ExecutionInputSafetyTests
{
    [Test]
    public static async Task RegleSubstitueeRefuseeSansCopieNiMutation()
    {
        using var fixture = new Fixture();
        var original = Encoding.UTF8.GetBytes("<migration/>");
        var changed = Encoding.UTF8.GetBytes("<migration changed=\"true\"/>");
        var source = fixture.Write("Rules/source.xml", changed);
        var target = fixture.Directory("Inputs");
        using var lease = new ExecutionInputLease(new ExecutionFileSystem());
        var error = await Check.ThrowsAsync<FilproOperationException>(() => lease.CopyRuleAsync(fixture.Root,
            new("Rules/source.xml", BackupManifestCodec.Hash(original), original.Length), target, default), "Empreinte/longueur source différentes");
        Check.Equal("store.integrity", error.Code, "Validation avant copie");
        Check.Equal(0, System.IO.Directory.GetFiles(target).Length, "Aucune règle publiée");
        Check.True(File.ReadAllBytes(source).SequenceEqual(changed), "Source conservée octet pour octet");
    }

    [Test]
    public static async Task ReglePriveeExacteEtAnnulationAvantPublication()
    {
        using var fixture = new Fixture();
        var bytes = Encoding.UTF8.GetBytes("<migration/>");
        var source = fixture.Write("Rules/source.xml", bytes);
        var target = fixture.Directory("Inputs");
        using (var lease = new ExecutionInputLease(new ExecutionFileSystem()))
        {
            var copy = await lease.CopyRuleAsync(fixture.Root, new("Rules/source.xml", BackupManifestCodec.Hash(bytes), bytes.Length), target, default);
            Check.False(copy == source, "L'outil reçoit une copie privée");
            Check.True(File.ReadAllBytes(copy).SequenceEqual(bytes), "Copie exacte");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var secondLease = new ExecutionInputLease(new ExecutionFileSystem());
        var otherTarget = fixture.Directory("Cancelled");
        await Check.ThrowsAsync<OperationCanceledException>(() => secondLease.CopyRuleAsync(fixture.Root,
            new("Rules/source.xml", BackupManifestCodec.Hash(bytes), bytes.Length), otherTarget, cancelled.Token), "Annulation avant publication");
        Check.Equal(0, System.IO.Directory.GetFiles(otherTarget).Length, "Aucune copie après annulation");
    }

    [Test]
    public static void PorteeCibleAbsenteAdmiseMaisFormeEtCloudRefuses()
    {
        using var fixture = new Fixture();
        var profile = fixture.Directory("Profile");
        var guard = new RestoreTargetScopeGuard(new ExecutionFileSystem());
        using (guard.Acquire(profile, Saved(), default)) { }
        File.WriteAllText(Path.Combine(profile, "Documents"), "ne pas remplacer");
        Check.Throws<FilproOperationException>(() => guard.Acquire(profile, Saved(), default), "Un dossier cible devenu fichier est refusé");
        Check.Equal("ne pas remplacer", File.ReadAllText(Path.Combine(profile, "Documents")), "Aucune mutation du conflit");
        File.Delete(Path.Combine(profile, "Documents"));
        System.IO.Directory.CreateDirectory(Path.Combine(profile, "Documents", "OneDrive"));
        Check.Throws<FilproOperationException>(() => guard.Acquire(profile, Saved(), default), "Un sous-dossier cloud non exclu est refusé");
        using (guard.Acquire(profile, Saved() with { ExcludedPaths = ["Documents/OneDrive"] }, default)) { }
    }

    [Test]
    public static void PorteeCibleBorneeEtAnnulableSansEcriture()
    {
        using var fixture = new Fixture();
        var profile = fixture.Directory("Profile");
        var documents = Path.Combine(profile, "Documents"); System.IO.Directory.CreateDirectory(documents);
        File.WriteAllText(Path.Combine(documents, "existant.txt"), "conservé");
        var guard = new RestoreTargetScopeGuard(new ExecutionFileSystem());
        Check.Throws<FilproOperationException>(() => guard.Acquire(profile, Saved(), default, maximumEntries: 1), "Borne de parcours ferme");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Check.Throws<OperationCanceledException>(() => guard.Acquire(profile, Saved(), cancellation.Token), "Annulation conservée");
        Check.Equal("conservé", File.ReadAllText(Path.Combine(documents, "existant.txt")), "Données cibles intactes");
        using (guard.Acquire(profile, Saved(), default)) { }
    }

    [Test]
    public static void PorteeProfilAdmetCrochetsEtPourcentEtNommeCeQuiNePeutPasEtreEcarte()
    {
        using var fixture = new Fixture();
        var profile = fixture.Directory("Profile");
        fixture.Write("Profile/Documents/photo [1].jpg", [1]);
        fixture.Write("Profile/Documents/100%.txt", [2]);
        fixture.Write("Profile/Documents/Projets [2024]/plan.txt", [3]);
        fixture.Write("Profile/notes [racine].txt", [4]);
        fixture.Write("Profile/Documents/Ancien/filpro-backup.json", [5]);
        var reader = new ProfileFileScopeReader(new ExecutionFileSystem());
        var scope = reader.Read(profile, [], null, default);
        Check.True(scope.PersonalFolders.SequenceEqual(["Documents"]), "Dossier personnel inclus malgré les noms de ses fichiers");
        Check.True(scope.ExcludedPaths.SequenceEqual(["Documents/Ancien"]), "Seul le magasin FILPRO est écarté");
        Check.Equal(1, scope.Warnings.Count, "Une seule exclusion annoncée");
        fixture.Write("Profile/Documents/Copie [B]/filpro-backup.json", [6]);
        var error = Check.Throws<FilproOperationException>(() => reader.Read(profile, [], null, default), "Exclusion impossible à écrire");
        Check.Equal("usmt.rules", error.Code, "Refus avant toute copie");
        Check.True(error.Message.Contains("Copie [B]", StringComparison.Ordinal), "Le chemin à renommer est nommé");
        System.IO.Directory.Delete(Path.Combine(profile, "Documents", "Copie [B]"), true);
        fixture.Directory("Profile/Archives 100%");
        error = Check.Throws<FilproOperationException>(() => reader.Read(profile, [], null, default), "Dossier du profil impossible à inclure");
        Check.True(error.Message.Contains("Archives 100%", StringComparison.Ordinal), "Le dossier à renommer est nommé");
    }

    [Test]
    public static void MarqueurDeMagasinIrrepresentableNInterromptPasLaPortee()
    {
        using var fixture = new Fixture();
        var profile = fixture.Directory("Profile");
        var limit = OperatingSystem.IsWindows() ? 259 : 4096;
        // Dossier admis dont le marqueur filpro-backup.json dépasserait la limite de chemin : il ne peut pas être un magasin FILPRO.
        var directory = Path.Combine(profile, "Documents");
        for (var remaining = limit - 10 - directory.Length; remaining > 0; remaining = limit - 10 - directory.Length)
        {
            Check.True(remaining > 1, "Base de test assez courte pour viser la longueur");
            directory = Path.Combine(directory, new string('a', remaining > 255 ? 100 : remaining - 1));
        }
        Check.Equal(limit - 10, directory.Length, "Longueur visée");
        Check.True(ProfileFileScopeReader.StoreMarker(directory) is null, "Marqueur hors limite : aucun magasin possible");
        Check.True(ProfileFileScopeReader.StoreMarker(profile) is not null, "Marqueur ordinaire vérifié");
        if (!OperatingSystem.IsWindows()) return;
        fixture.Write(Path.GetRelativePath(fixture.Root, Path.Combine(directory, "x.txt")), [1]);
        // 249 + 1 + 10 = 260 caractères : seul un parcours effectif du dossier long l'écarte.
        var beyond = Path.Combine(directory, "0123456789");
        fixture.Write(Path.GetRelativePath(fixture.Root, beyond), [2]);
        var scope = new ProfileFileScopeReader(new ExecutionFileSystem()).Read(profile, [], null, default);
        Check.True(scope.PersonalFolders.SequenceEqual(["Documents"]), "Dossier long inclus sans refus");
        Check.True(scope.ExcludedPaths.SequenceEqual([Path.GetRelativePath(profile, beyond).Replace('\\', '/')]), "Son contenu est parcouru : seul l'enfant hors limite est écarté");
        Check.Equal(1, scope.Warnings.Count, "Une seule exclusion annoncée");
    }

    internal static BackupManifestProfile Saved() => new(new string('a', 32),
        new("S-1-5-21-1-2-3-1000", ProfileIdentityKind.CompteLocalOuDomaine, @"LAB\Alice", @"C:\Users\Alice", false, [], []),
        "Store/" + new string('a', 32), "Ready", [], null, null, 0, "Verified", ["Documents"], []);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filpro-input-" + Guid.NewGuid().ToString("N"));
        public Fixture() => System.IO.Directory.CreateDirectory(Root);
        public string Directory(string relative)
        { var path = Path.Combine(Root, relative); System.IO.Directory.CreateDirectory(path); return path; }
        public string Write(string relative, byte[] bytes)
        { var path = Path.Combine(Root, relative); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path; }
        public void Dispose() => System.IO.Directory.Delete(Root, true);
    }
}
