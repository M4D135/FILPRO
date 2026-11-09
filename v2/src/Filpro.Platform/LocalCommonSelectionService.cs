using Filpro.Core;
using System.Text.Json;

namespace Filpro.Platform;

/// <summary>Document distinct des sélections V1 et des réglages exportables. Sauvegarde avant remplacement atomique.</summary>
public sealed partial class LocalCommonSelectionService : IFilproCommonSelectionService
{
    private readonly string path;
    private readonly string backupRoot;
    private readonly string dataRoot;
    private readonly string? packRoot;
    private readonly TimeProvider clock;
    private const int MaximumBytes = 1024 * 1024;

    public LocalCommonSelectionService(string dataRoot, TimeProvider clock, string? packRoot = null)
    {
        this.clock = clock;
        var root = StoragePathPolicy.NormalizeNative(dataRoot);
        this.dataRoot = root;
        this.packRoot = packRoot is null ? null : StoragePathPolicy.NormalizeNative(packRoot);
        path = Path.Combine(root, "V2", "Selections", "common.json");
        backupRoot = Path.Combine(root, "V2", "Selections", "Backups");
    }
    public bool Demonstration => false;
    public CommonSelectionState Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var document = DecodeSelection(ConfigurationFileIO.Read(path, MaximumBytes)).Document;
            return new(false, true, document.Files, document.Registry);
        }
        catch (FileNotFoundException) { return new(false, false, [], []); }
        catch (DirectoryNotFoundException) { return new(false, false, [], []); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new FilproOperationException("selection.store", "Le modèle commun existant est invalide. Il est conservé ; corriger ou restaurer son document avant de réessayer.");
        }
    }
    public CommonSelectionState Save(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = SoftwareSelectionCodec.Encode(new(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind,
            clock.GetUtcNow(), files, registry, SoftwareSelectionCodec.DefaultPreferences));
        if (bytes.Length > MaximumBytes) throw new FilproOperationException("selection.size", "Modèle commun trop volumineux.");
        using var lease = ConfigurationFileIO.Lock(path + ".lock");
        _ = Read(cancellationToken); // Refuse un document existant illisible au lieu de l'écraser.
        var previous = File.Exists(path) ? ConfigurationFileIO.Hash(ConfigurationFileIO.Read(path, MaximumBytes)) : null;
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationFileIO.AtomicWrite(path, bytes, backupRoot, previous, expectAbsent: previous is null);
        return Read(cancellationToken);
    }
    public CommonSelectionState Clear(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = ConfigurationFileIO.Lock(path + ".lock");
        var current = Read(cancellationToken);
        if (!current.HasSelection) return current;
        ConfigurationFileIO.NoLinks(backupRoot); Directory.CreateDirectory(backupRoot); ConfigurationFileIO.NoLinks(path);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(path, Path.Combine(backupRoot, "common-" + Guid.NewGuid().ToString("N") + ".json"), overwrite: false);
        return new(false, false, [], []);
    }
}
