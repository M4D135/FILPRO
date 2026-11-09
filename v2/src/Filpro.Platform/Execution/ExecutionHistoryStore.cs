using System.Text.Json;
using System.Text.Json.Serialization;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Bilans privés, sans reprise automatique ni autorité d'exécution. Aucun état de démonstration n'est accepté.</summary>
public sealed class ExecutionHistoryStore
{
    public const int MaximumEntries = 1000;
    private const int MaximumBytes = 128 * 1024;
    private sealed record Document(int SchemaVersion, ExecutionJobSnapshot Snapshot);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12,
    };
    private readonly string root;
    private readonly string operations;
    private readonly ExecutionFileSystem files = new();
    private readonly object gate = new();
    private Dictionary<string, ExecutionJobSnapshot>? cached;
    private DateTimeOffset cachedUntil;

    public ExecutionHistoryStore(string stateDirectory)
    {
        operations = StoragePathPolicy.NormalizeNative(stateDirectory);
        root = Path.Combine(operations, "History");
    }

    public void Save(ExecutionJobSnapshot snapshot)
    {
        Validate(snapshot);
        lock (gate)
        {
            files.CreatePrivateDirectory(operations);
            using var parent = files.HoldDirectory(operations);
            files.CreatePrivateDirectory(root);
            using var lease = ConfigurationFileIO.Lock(Path.Combine(root, "history.lock"));
            var path = Path.Combine(root, snapshot.Id + ".json");
            if (!File.Exists(path) && Paths().Count >= MaximumEntries)
                throw new FilproOperationException("history.full", "Le journal contient 1 000 bilans. Archiver manuellement le dossier Operations/History avant de lancer une nouvelle opération ; aucun bilan n'a été supprimé.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, WithLogPath(snapshot)), JsonOptions);
            if (bytes.Length > MaximumBytes) throw Invalid();
            files.WriteAtomic(path, bytes, overwrite: true);
            cached = null;
        }
    }

    public IReadOnlyList<ExecutionJobSnapshot> Merge(IReadOnlyList<ExecutionJobSnapshot> current)
    {
        lock (gate)
        {
            var result = cached is not null && DateTimeOffset.UtcNow < cachedUntil
                ? new Dictionary<string, ExecutionJobSnapshot>(cached, StringComparer.Ordinal) : null;
            if (result is null)
            {
                result = new Dictionary<string, ExecutionJobSnapshot>(StringComparer.Ordinal);
                ConfigurationFileIO.NoLinks(root);
                if (Directory.Exists(root))
                {
                    using var lease = files.HoldDirectory(root);
                    foreach (var path in Paths())
                    {
                        Document document;
                        try { document = JsonSerializer.Deserialize<Document>(ReadSnapshot(path), JsonOptions) ?? throw Invalid(); }
                        catch (JsonException) { throw Invalid(); }
                        if (document.SchemaVersion != 1 || document.Snapshot is null) throw Invalid();
                        var snapshot = document.Snapshot; Validate(snapshot);
                        if (Path.GetFileName(path) != snapshot.Id + ".json") throw Invalid();
                        if (!ExecutionJobCoordinator.IsTerminal(snapshot.State))
                            snapshot = snapshot with
                            {
                                State = ExecutionState.Partial, FinishedAt = null, EstimatedRemainingSeconds = null,
                                CurrentAction = "Issue non confirmée — consulter les journaux de la session d'origine",
                                Error = new("history.unconfirmed", "Aucun bilan final enregistré. La session d'origine peut être encore active ou avoir été interrompue. Aucune reprise automatique."),
                                CancellationRequested = false,
                            };
                        result.Add(snapshot.Id, snapshot);
                    }
                }
                cached = new(result, StringComparer.Ordinal);
                cachedUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            foreach (var snapshot in current) result[snapshot.Id] = WithLogPath(snapshot);
            return result.Values.OrderByDescending(snapshot => snapshot.StartedAt).ToArray();
        }
    }

    private List<string> Paths()
    {
        var paths = new List<string>(); var entries = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            if (++entries > MaximumEntries * 2 + 1) throw Invalid();
            ConfigurationFileIO.NoLinks(path);
            var name = Path.GetFileName(path);
            if (name == "history.lock") continue;
            // Un fichier temporaire conservé après coupure n'est jamais pris pour un bilan.
            if (name.Length == 74 && name.Substring(32, 10) == ".json.tmp-"
                && Guid.TryParseExact(name[..32], "N", out _) && Guid.TryParseExact(name[42..], "N", out _) && !Directory.Exists(path)) continue;
            if (!name.EndsWith(".json", StringComparison.Ordinal) || !Guid.TryParseExact(name[..^5], "N", out _) || Directory.Exists(path)) throw Invalid();
            if (paths.Count == MaximumEntries) throw Invalid();
            paths.Add(path);
        }
        return paths;
    }

    private ExecutionJobSnapshot WithLogPath(ExecutionJobSnapshot snapshot)
        => snapshot with { ResultPath = snapshot.ResultPath ?? Path.Combine(operations, snapshot.Id) };

    private static byte[] ReadSnapshot(string path)
    {
        ConfigurationFileIO.NoLinks(path);
        // Les bilans sont remplacés atomiquement. Un lecteur ne doit pas empêcher la publication du bilan final.
        using var stream = StorageFileIdentity.OpenReadSnapshot(path);
        if (stream.Length is < 1 or > MaximumBytes) throw Invalid();
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw Invalid();
        return bytes;
    }

    private static void Validate(ExecutionJobSnapshot value)
    {
        if (!Guid.TryParseExact(value.Id, "N", out _) || value.Demonstration || !Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.State)
            || string.IsNullOrWhiteSpace(value.Title) || value.Title.Length > 2048 || value.CurrentAction is null || value.CurrentAction.Length > 8192
            || value.Warnings is null || value.Warnings.Count > 1024 || value.Warnings.Any(w => w is null || w.Length > 8192)
            || value.StartedAt == default || value.FinishedAt < value.StartedAt
            || value.ProgressPercent is { } p && (!double.IsFinite(p) || p < 0 || p > 100)
            || value.EstimatedRemainingSeconds is { } e && (!double.IsFinite(e) || e < 0)) throw Invalid();
    }
    private static FilproOperationException Invalid() => new("history.invalid", "Journal local invalide ou trop volumineux. Conserver le dossier Operations/History pour examen ; aucun bilan n'a été remplacé.");
}
