using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Preuve portable de l'index de fichiers ; aucun test n'exécute USMT.</summary>
internal static class PlainStoreTests
{
    private const string Id = "11111111111111111111111111111111";

    [Test]
    public static void IndexExigePiedOrdreEtFinNonTronquee()
    {
        var first = Entry("USMT/catalog.dat", "metadata");
        var second = Entry("USMT/File/C/Users/Alice/Documents/a.txt", "A");
        var valid = Index(first, second);
        Check.Equal(2, PlainStoreIndexCodec.Read(new StringReader(valid), Id, default).Count(), "Index complet lu");
        foreach (var broken in new[] { valid.TrimEnd('\n', '\r'), string.Join('\n', valid.Split('\n').Take(3)) + "\n", Index(second, first), Index(first, first) })
            Check.Throws<FilproOperationException>(() => PlainStoreIndexCodec.Read(new StringReader(broken), Id, default).ToArray(), "Fin, ordre ou doublon refusé");
        var duplicate = valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        Check.Throws<FilproOperationException>(() => PlainStoreIndexCodec.Read(new StringReader(duplicate), Id, default).ToArray(), "Champ dupliqué refusé");
    }

    [Test]
    public static void IndexRefuseCheminSortantEtAnnulation()
    {
        Check.Throws<FilproOperationException>(() => Index(Entry("USMT/File/../../ailleurs.txt", "x")), "Traversée interdite");
        var valid = Index(Entry("USMT/File/C/Users/Alice/a.txt", "a"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Check.Throws<OperationCanceledException>(() => PlainStoreIndexCodec.Read(new StringReader(valid), Id, cancel.Token).ToArray(), "Annulation propagée");
        var longLine = new string('x', PlainStoreIndexCodec.MaximumLineCharacters + 1) + "\n";
        Check.Throws<FilproOperationException>(() => PlainStoreIndexCodec.Read(new StringReader(longLine), Id, default).ToArray(), "Lecture bornée");
    }

    [Test]
    public static async Task CaptureEtVerificationPortentFichiersEtMetadonnees()
    {
        using var fixture = new Fixture(); fixture.Populate();
        var verifier = new PlainStoreVerifier(new ExecutionFileSystem());
        var capture = await verifier.CaptureAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store"), default);
        Check.True(capture.Catalog.Complete, "Deux fichiers catalogue complets");
        Check.Equal(2, capture.Catalog.Entries.Count, "Les métadonnées USMT ne sont pas présentées comme documents");
        Check.True(capture.Catalog.Entries.All(e => e.StoredPath == "USMT/File/" + e.Name), "Lien exact vers le fichier natif");
        using var verified = await verifier.VerifyAndHoldAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store", capture.Index), default);
        Check.Equal(3, verified.Entries.Count, "Les métadonnées sont également hashées et retenues");
        Check.True(verified.Entries.ContainsKey("USMT/catalog.dat"), "Métadonnées indexées");
    }

    [Test]
    public static async Task SubstitutionSuppressionEtAjoutRefusesSansMutation()
    {
        using var fixture = new Fixture(); fixture.Populate();
        var verifier = new PlainStoreVerifier(new ExecutionFileSystem());
        var capture = await verifier.CaptureAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store"), default);
        var descriptor = new PlainStoreDescriptor(Id, "Store", capture.Index);
        var target = fixture.PathOf("Store/USMT/catalog.dat");
        File.WriteAllText(target, "changed");
        await Check.ThrowsAsync<FilproOperationException>(() => verifier.VerifyAndHoldAsync(fixture.Root, descriptor, default), "Métadonnées substituées");
        Check.Equal("changed", File.ReadAllText(target), "Le vérificateur ne corrige pas le contenu");
        File.WriteAllText(target, "metadata");
        File.Delete(fixture.PathOf("Store/USMT/File/C/Users/Alice/Documents/b.txt"));
        await Check.ThrowsAsync<FilproOperationException>(() => verifier.VerifyAndHoldAsync(fixture.Root, descriptor, default), "Fichier manquant");
        fixture.Write("Store/USMT/File/C/Users/Alice/Documents/b.txt", "B");
        fixture.Write("Store/USMT/File/C/Users/Alice/Documents/c.txt", "C");
        await Check.ThrowsAsync<FilproOperationException>(() => verifier.VerifyAndHoldAsync(fixture.Root, descriptor, default), "Fichier non indexé");
    }

    [Test]
    public static async Task IndexPriveLaisseV1InchangeEtAnnulationNePublieRien()
    {
        using var fixture = new Fixture(); fixture.Populate();
        var before = Directory.GetFiles(fixture.PathOf("Store"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes, StringComparer.Ordinal);
        var privateRoot = fixture.PathOf("Private"); Directory.CreateDirectory(privateRoot);
        var verifier = new PlainStoreVerifier(new ExecutionFileSystem());
        var capture = await verifier.CaptureAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store"), default, privateRoot);
        Check.False(Directory.Exists(fixture.PathOf("Indexes")), "Aucun index ajouté au magasin source");
        using (await verifier.VerifyAndHoldAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store", capture.Index), default, privateRoot)) { }
        foreach (var (path, bytes) in before) Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)), "Source conservée octet pour octet");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var otherId = new string('2', 32);
        await Check.ThrowsAsync<OperationCanceledException>(() => verifier.CaptureAsync(fixture.Root, new PlainStoreDescriptor(otherId, "Store"), cancel.Token, privateRoot), "Annulation avant publication");
        Check.False(File.Exists(Path.Combine(privateRoot, "Indexes", otherId + ".jsonl")), "Aucun index annulé publié");
        Check.Equal(0, Directory.GetFiles(privateRoot, "*.tmp-*", SearchOption.AllDirectories).Length, "Temporaires libérés");
    }

    [Test]
    public static async Task IndexAltereeRefuseeEtRefusLibereLesHandles()
    {
        using var fixture = new Fixture(); fixture.Populate();
        var verifier = new PlainStoreVerifier(new ExecutionFileSystem());
        var capture = await verifier.CaptureAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store"), default);
        var index = fixture.PathOf(capture.Index.Path); File.AppendAllText(index, "{}\n");
        await Check.ThrowsAsync<FilproOperationException>(() => verifier.VerifyAndHoldAsync(fixture.Root, new PlainStoreDescriptor(Id, "Store", capture.Index), default), "Index substitué");
        using var exclusive = new FileStream(index, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check.True(exclusive.CanWrite, "Aucun handle du vérificateur conservé après le refus");
    }

    private static ManifestFileReference Entry(string path, string value)
    { var bytes = Encoding.UTF8.GetBytes(value); return new(path, BackupManifestCodec.Hash(bytes), bytes.Length); }
    private static string Index(params ManifestFileReference[] entries)
    {
        using var writer = new StringWriter(); PlainStoreIndexCodec.WriteHeader(writer, Id);
        foreach (var entry in entries) PlainStoreIndexCodec.WriteEntry(writer, entry);
        PlainStoreIndexCodec.WriteFooter(writer, entries.Length, entries.Sum(e => e.Bytes)); return writer.ToString();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filpro-plain-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        public void Write(string relative, string content)
        { var path = PathOf(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        public void Populate()
        { Write("Store/USMT/catalog.dat", "metadata"); Write("Store/USMT/File/C/Users/Alice/Documents/a.txt", "A"); Write("Store/USMT/File/C/Users/Alice/Documents/b.txt", "B"); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
