using System.Text;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class LocalCommonSelectionService
{
    private sealed record PendingPatch(CommonPatchPreview Preview, string Source, DateTimeOffset Expires);
    private readonly object patchGate = new();
    private readonly Dictionary<string, PendingPatch> patches = new(StringComparer.Ordinal);

    public CommonPatchResult Export(string destination, bool confirmed, CancellationToken cancellationToken)
    {
        ConfirmPatch(confirmed); cancellationToken.ThrowIfCancellationRequested();
        destination = StoragePathPolicy.NormalizeNative(destination);
        if (!destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || StoragePathPolicy.IsWithin(destination, dataRoot) || packRoot is not null && StoragePathPolicy.IsWithin(destination, packRoot))
            throw InvalidPatch("Choisir un nouveau JSON hors du pack et des données internes de FILPRO.");
        var current = Read(cancellationToken); var bytes = CommonSelectionPatch.Encode(current); var hash = CommonSelectionPatch.Hash(bytes);
        filesForPatch.CreateDirectories(Path.GetDirectoryName(destination)!);
        using var lease = filesForPatch.HoldDirectory(Path.GetDirectoryName(destination)!);
        ConfigurationFileIO.NoLinks(destination); ConfigurationFileIO.NoLinks(destination + ".sha256");
        if (File.Exists(destination) || File.Exists(destination + ".sha256")) throw InvalidPatch("Choisir un nouveau nom ; aucun export existant n'est écrasé.");
        filesForPatch.WriteAtomic(destination, bytes, false);
        filesForPatch.WriteAtomic(destination + ".sha256", Encoding.UTF8.GetBytes(hash + "\n"), false);
        return new(false, current, hash, destination, null, false, "Modèle exporté avec sa somme .sha256. Aucun profil, compte, clé ou réglage de chiffrement n'est inclus.");
    }
    private readonly ExecutionFileSystem filesForPatch = new();

    public CommonPatchPreview PreviewPatch(string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); source = StoragePathPolicy.NormalizeNative(source);
        var (incoming, hash) = ReadPatch(source); var current = Read(cancellationToken);
        var preview = new CommonPatchPreview(Guid.NewGuid().ToString("N"), false, hash, CommonSelectionPatch.Revision(current),
            CommonSelectionPatch.Describe(current, incoming), "Les entrées absentes du patch restent conservées. Un conflit conserve le choix local sauf décision explicite. Aucun profil n'est modifié.");
        lock (patchGate)
        {
            foreach (var id in patches.Where(entry => entry.Value.Expires < clock.GetUtcNow()).Select(entry => entry.Key).ToArray()) patches.Remove(id);
            if (patches.Count >= 16) patches.Remove(patches.Keys.First());
            patches.Add(preview.Id, new(preview, source, clock.GetUtcNow().AddMinutes(15)));
        }
        return preview;
    }

    public CommonPatchResult ApplyPatch(string previewId, bool replaceConflicts, bool confirmed, CancellationToken cancellationToken)
    {
        ConfirmPatch(confirmed); cancellationToken.ThrowIfCancellationRequested();
        lock (patchGate)
        {
            if (!patches.TryGetValue(previewId, out var pending) || pending.Expires < clock.GetUtcNow()) throw InvalidPatch("Aperçu absent ou expiré ; relire le patch.");
            using var lease = ConfigurationFileIO.Lock(path + ".lock");
            var (incoming, hash) = ReadPatch(pending.Source);
            var original = File.Exists(path) ? ConfigurationFileIO.Read(path, MaximumBytes) : null;
            var decoded = original is null ? ((SoftwareSelectionDocument?)null, Array.Empty<string>()) : DecodeSelection(original);
            var current = decoded.Item1 is { } selectionDocument ? new CommonSelectionState(false, true, selectionDocument.Files, selectionDocument.Registry)
                : new CommonSelectionState(false, false, [], []);
            if (hash != pending.Preview.Sha256 || CommonSelectionPatch.Revision(current) != pending.Preview.ExpectedRevision)
                throw InvalidPatch("Le patch ou les choix locaux ont changé ; refaire l'aperçu avant toute écriture.");
            var applied = decoded.Item2;
            if (incoming.AppData.Count + incoming.RegistryKeys.Count == 0)
            { patches.Remove(previewId); return new(false, current, hash, null, null, false, "Patch vide : choix actuels conservés, aucune écriture."); }
            if (applied.Contains(hash, StringComparer.Ordinal))
            { patches.Remove(previewId); return new(false, current, hash, null, null, true, "Ce patch a déjà été appliqué ; aucune nouvelle écriture."); }
            var merged = CommonSelectionPatch.Merge(current, incoming, replaceConflicts);
            var selection = SoftwareSelectionCodec.Encode(new(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind,
                clock.GetUtcNow(), merged.AppData, merged.RegistryKeys, SoftwareSelectionCodec.DefaultPreferences));
            using var parsed = JsonDocument.Parse(selection);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, application = "FILPRO", kind = "common-selection",
                selection = parsed.RootElement, appliedHashes = applied.Append(hash).TakeLast(128).ToArray() });
            if (bytes.Length > MaximumBytes) throw InvalidPatch("Modèle fusionné trop volumineux.");
            _ = DecodeSelection(bytes); cancellationToken.ThrowIfCancellationRequested();
            var backup = ConfigurationFileIO.AtomicWrite(path, bytes, backupRoot, original is null ? null : ConfigurationFileIO.Hash(original), original is null);
            patches.Remove(previewId);
            return new(false, merged, hash, path, backup, false, "Patch appliqué après sauvegarde. Choix locaux absents du patch conservés ; sélections personnelles et profils inchangés.");
        }
    }

    private static (CommonSelectionState State, string Hash) ReadPatch(string source)
    {
        var bytes = ConfigurationFileIO.Read(source, CommonSelectionPatch.MaximumBytes);
        var expected = Encoding.UTF8.GetString(ConfigurationFileIO.Read(source + ".sha256", 256)).Trim();
        var hash = CommonSelectionPatch.Hash(bytes);
        if (!PackManifestCodec.IsSha256(expected) || hash != expected) throw InvalidPatch("Le patch et son fichier .sha256 sont absents, modifiés ou incohérents.");
        return (CommonSelectionPatch.Decode(bytes, false), hash);
    }

    private static (SoftwareSelectionDocument Document, string[] Applied) DecodeSelection(byte[] bytes)
    {
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 10 });
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw InvalidPatch("Modèle commun invalide.");
        if (root.TryGetProperty("kind", out var kind) && kind.GetString() == SoftwareSelectionCodec.Kind)
            return (SoftwareSelectionCodec.Decode(bytes), []);
        if (!root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(new[] { "application", "appliedHashes", "kind", "schemaVersion", "selection" })
            || root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("application").GetString() != "FILPRO"
            || root.GetProperty("kind").GetString() != "common-selection") throw InvalidPatch("Version du modèle commun inconnue.");
        var entries = root.GetProperty("appliedHashes");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 128) throw InvalidPatch("Historique de patches invalide.");
        var hashes = entries.EnumerateArray().Select(value => value.GetString()!).ToArray();
        if (hashes.Any(hash => !PackManifestCodec.IsSha256(hash)) || hashes.Distinct(StringComparer.Ordinal).Count() != hashes.Length) throw InvalidPatch("Empreinte de patch invalide.");
        return (SoftwareSelectionCodec.Decode(Encoding.UTF8.GetBytes(root.GetProperty("selection").GetRawText())), hashes);
    }
    private static void ConfirmPatch(bool confirmed) { if (!confirmed) throw InvalidPatch("Confirmer avant l'export ou l'application du patch."); }
    private static FilproOperationException InvalidPatch(string message) => new("templates.patch", message);
}
