using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filpro.Core;

[assembly: InternalsVisibleTo("Filpro.Tests")]

namespace Filpro.Platform;

internal sealed record UpdateTransaction(int SchemaVersion, string Id, string InstallRoot, string DataRoot,
    string PreviousVersion, string PreviousManifestSha256, string? PreviousDefaultsSha256,
    string TargetVersion, string TargetManifestSha256, string? TargetDefaultsSha256,
    UpdateStage Stage, string HealthToken, DateTimeOffset CreatedUtc, string? Error)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Flavor { get; init; }
    internal string ExpectedFlavor => Flavor ?? PackManifestCodec.ProductionFlavor;
    // Les anciens clients PROD lisent tous les journaux de "updates", même ceux d’une autre installation.
    internal static string HistoryRoot(string dataRoot, string flavor) => Path.Combine(dataRoot, flavor switch
    {
        PackManifestCodec.ProductionFlavor => "updates",
        PackManifestCodec.LightFlavor => "updates-light",
        _ => throw Invalid(),
    });
    internal string DirectoryPath => Path.Combine(HistoryRoot(DataRoot, ExpectedFlavor), Id);
    internal string JournalPath => Path.Combine(DirectoryPath, "transaction.json");
    internal string WorkRoot => Path.Combine(Path.GetDirectoryName(InstallRoot)!, ".filpro-update-" + InstallationKey(InstallRoot) + "-" + Id);
    internal string StageRoot => Path.Combine(WorkRoot, "stage");
    internal string PreviousRoot => Path.Combine(WorkRoot, "previous");
    internal string WorkerRoot => Path.Combine(WorkRoot, "worker");
    internal string FailedRoot => Path.Combine(WorkRoot, "failed");
    internal string HealthPath => Path.Combine(DirectoryPath, "health.json");
    internal static string InstallationKey(string root) => ConfigurationFileIO.Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root))[..16];
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<UpdateStage>() }, WriteIndented = true,
    };
    internal void Save(bool create = false)
    {
        Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Json);
        var previous = File.Exists(JournalPath) ? ConfigurationFileIO.Hash(ConfigurationFileIO.Read(JournalPath, 64 * 1024)) : null;
        if (create && previous is not null) throw new FilproOperationException("update.transaction", "La transaction existe déjà.");
        ConfigurationFileIO.AtomicWrite(JournalPath, bytes, Path.Combine(DirectoryPath, "journal-backups"), previous, expectAbsent: previous is null);
    }
    internal static UpdateTransaction Read(string dataRoot, string id, string? expectedInstallRoot = null,
        string expectedFlavor = PackManifestCodec.ProductionFlavor)
    {
        RequireId(id); var root = StoragePathPolicy.NormalizeNative(dataRoot);
        var bytes = ConfigurationFileIO.Read(Path.Combine(HistoryRoot(root, expectedFlavor), id, "transaction.json"), 64 * 1024);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (fields.Length is not (14 or 15) || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length) throw Invalid();
            var transaction = JsonSerializer.Deserialize<UpdateTransaction>(bytes, Json) ?? throw Invalid();
            transaction.Validate();
            if (fields.Length != (transaction.SchemaVersion == 1 ? 14 : 15)
                || fields.Contains("flavor", StringComparer.Ordinal) != (transaction.SchemaVersion == 2)) throw Invalid();
            if (transaction.Id != id || transaction.DataRoot != root || transaction.ExpectedFlavor != expectedFlavor || (expectedInstallRoot is not null && transaction.InstallRoot != StoragePathPolicy.NormalizeNative(expectedInstallRoot))) throw Invalid();
            return transaction;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { throw Invalid(); }
    }
    internal void Validate()
    {
        RequireId(Id);
        var validSchema = (SchemaVersion == 1 && Flavor is null) || (SchemaVersion == 2 && Flavor == PackManifestCodec.LightFlavor);
        if (!validSchema || InstallRoot != StoragePathPolicy.NormalizeNative(InstallRoot) || DataRoot != StoragePathPolicy.NormalizeNative(DataRoot)
            || !PackManifestCodec.IsSha256(PreviousManifestSha256) || !PackManifestCodec.IsSha256(TargetManifestSha256)
            || (PreviousDefaultsSha256 is not null && !PackManifestCodec.IsSha256(PreviousDefaultsSha256))
            || (TargetDefaultsSha256 is not null && !PackManifestCodec.IsSha256(TargetDefaultsSha256))
            || !PackManifestCodec.IsSha256(HealthToken) || !Enum.IsDefined(Stage) || Error?.Length > 2048
            || CreatedUtc == default || StoragePathPolicy.IsWithin(DataRoot, InstallRoot) || StoragePathPolicy.IsWithin(InstallRoot, DataRoot)
            || StoragePathPolicy.IsWithin(DataRoot, WorkRoot) || StoragePathPolicy.IsWithin(WorkRoot, DataRoot)) throw Invalid();
        if (PackageVersion.Parse(TargetVersion).CompareTo(PackageVersion.Parse(PreviousVersion)) <= 0)
            throw new FilproOperationException("update.version", "Le journal ne décrit pas une mise à jour vers une version supérieure.");
        ConfigurationFileIO.NoLinks(WorkRoot); ConfigurationFileIO.NoLinks(DirectoryPath);
        RequireSafeInstallation(InstallRoot);
    }
    internal static void RequireId(string id)
    { if (id is null || !Guid.TryParseExact(id, "N", out var parsed) || parsed.ToString("N") != id) throw Invalid(); }
    internal static void RequireSafeInstallation(string root)
    {
        var normalized = StoragePathPolicy.NormalizeNative(root); ConfigurationFileIO.NoLinks(normalized);
        if (OperatingSystem.IsWindows())
        {
            if (normalized.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(normalized)!).DriveType == DriveType.Network)
                throw new FilproOperationException("update.installation", "Installer la mise à jour depuis une copie locale ou USB du pack ; la source peut rester sur un partage UNC.");
            foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData })
            {
                var path = Environment.GetFolderPath(special);
                if (!string.IsNullOrEmpty(path) && (normalized.Equals(path, StringComparison.OrdinalIgnoreCase)
                    || (special is Environment.SpecialFolder.Windows or Environment.SpecialFolder.System && StoragePathPolicy.IsWithin(normalized, path))))
                    throw new FilproOperationException("update.installation", "Ce répertoire système ou personnel ne peut pas être remplacé comme un pack.");
            }
        }
    }
    private static FilproOperationException Invalid() => new("update.transaction", "Journal de mise à jour invalide ou étranger à cette installation. Aucun dossier remplacé.");
}

/// <summary>Échanges de répertoires uniquement ; toutes les copies précédentes restent disponibles.</summary>
internal static class UpdateDirectoryTransaction
{
    // Une coupure peut laisser le journal en retard sur le dernier déplacement.
    // Ceci est une possibilité de reprise, pas une preuve : les octets sont relus sous verrou.
    internal static bool HasRollbackCandidate(UpdateTransaction transaction)
        => transaction.Stage != UpdateStage.RolledBack && (Directory.Exists(transaction.PreviousRoot)
            || (transaction.Stage is UpdateStage.RollingBack or UpdateStage.Failed && Directory.Exists(transaction.InstallRoot)));

    internal static Task VerifyRollbackSourceAsync(UpdateTransaction transaction, CancellationToken token)
    {
        if (Directory.Exists(transaction.PreviousRoot))
            return VerifyAsync(transaction.PreviousRoot, transaction.PreviousManifestSha256, transaction.PreviousDefaultsSha256, token, transaction.PreviousVersion, transaction.ExpectedFlavor);
        if (File.Exists(transaction.PreviousRoot) || transaction.Stage is not (UpdateStage.RollingBack or UpdateStage.RolledBack or UpdateStage.Failed))
            throw new FilproOperationException("update.rollback", "Aucune copie précédente ni fin de retour arrière à vérifier. Les dossiers sont conservés.");
        return VerifyAsync(transaction.InstallRoot, transaction.PreviousManifestSha256, transaction.PreviousDefaultsSha256, token, transaction.PreviousVersion, transaction.ExpectedFlavor);
    }

    internal static async Task<UpdateTransaction> InstallAsync(UpdateTransaction transaction, Func<Task<bool>> launchAndCheckHealth,
        Func<Task<IDisposable>> acquireExclusiveLease, CancellationToken token)
    {
        if (transaction.Stage is not (UpdateStage.Prepared or UpdateStage.WaitingForExit)) throw new FilproOperationException("update.state", "Transaction non préparée.");
        transaction.Validate();
        try
        {
            using (await acquireExclusiveLease().ConfigureAwait(false))
            {
                using var installParent = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(transaction.InstallRoot)!);
                using var workParent = ConfigurationFileIO.FileSystem.HoldDirectory(transaction.WorkRoot);
                await VerifyAsync(transaction.InstallRoot, transaction.PreviousManifestSha256, transaction.PreviousDefaultsSha256, token, transaction.PreviousVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
                await VerifyAsync(transaction.StageRoot, transaction.TargetManifestSha256, transaction.TargetDefaultsSha256, token, transaction.TargetVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
                if (Directory.Exists(transaction.PreviousRoot) || File.Exists(transaction.PreviousRoot) || Directory.Exists(transaction.FailedRoot)) throw new IOException("Dossier de retour arrière déjà présent.");
                transaction = transaction with { Stage = UpdateStage.WaitingForExit, Error = null }; transaction.Save();
                ConfigurationFileIO.NoLinks(transaction.InstallRoot); ConfigurationFileIO.NoLinks(transaction.StageRoot); ConfigurationFileIO.NoLinks(transaction.PreviousRoot);
                Directory.Move(transaction.InstallRoot, transaction.PreviousRoot);
                transaction = transaction with { Stage = UpdateStage.OldMoved }; transaction.Save();
                Directory.Move(transaction.StageRoot, transaction.InstallRoot);
                transaction = transaction with { Stage = UpdateStage.NewInstalled }; transaction.Save();
            }
            transaction = transaction with { Stage = UpdateStage.AwaitingHealth }; transaction.Save();
            if (!await launchAndCheckHealth().ConfigureAwait(false)) throw new IOException("La nouvelle application n'a pas confirmé son démarrage complet.");
            await VerifyAsync(transaction.InstallRoot, transaction.TargetManifestSha256, transaction.TargetDefaultsSha256, token, transaction.TargetVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
            transaction = transaction with { Stage = UpdateStage.Committed }; transaction.Save();
            return transaction;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FilproOperationException or OperationCanceledException
            or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        {
            transaction = transaction with { Error = Limit(ex.Message) };
            if (Directory.Exists(transaction.PreviousRoot))
            {
                try { return await RollbackAsync(transaction, acquireExclusiveLease, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or FilproOperationException)
                { transaction = transaction with { Stage = UpdateStage.Failed, Error = Limit(ex.Message + " Retour arrière en attente : " + rollback.Message) }; }
            }
            else transaction = transaction with { Stage = UpdateStage.Failed };
            transaction.Save(); return transaction;
        }
    }
    internal static async Task<UpdateTransaction> RollbackAsync(UpdateTransaction transaction, Func<Task<IDisposable>> acquireExclusiveLease, CancellationToken token)
    {
        transaction.Validate();
        using (await acquireExclusiveLease().ConfigureAwait(false))
        {
            using var installParent = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(transaction.InstallRoot)!);
            using var workParent = ConfigurationFileIO.FileSystem.HoldDirectory(transaction.WorkRoot);
            await VerifyRollbackSourceAsync(transaction, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(transaction.PreviousRoot))
            {
                // Ancien pack déjà remis en place : réconcilier le journal sous le même verrou
                // que les déplacements, sans déplacer ni écraser à nouveau l'installation.
                if (transaction.Stage != UpdateStage.RolledBack)
                { transaction = transaction with { Stage = UpdateStage.RolledBack }; transaction.Save(); }
                return transaction;
            }
            if (Directory.Exists(transaction.InstallRoot))
            {
                await VerifyAsync(transaction.InstallRoot, transaction.TargetManifestSha256, transaction.TargetDefaultsSha256, token, transaction.TargetVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
                if (Directory.Exists(transaction.FailedRoot) || File.Exists(transaction.FailedRoot)) throw new IOException("Le dossier de conservation de la version retirée existe déjà.");
            }
            transaction = transaction with { Stage = UpdateStage.RollingBack }; transaction.Save();
            ConfigurationFileIO.NoLinks(transaction.InstallRoot); ConfigurationFileIO.NoLinks(transaction.PreviousRoot); ConfigurationFileIO.NoLinks(transaction.FailedRoot);
            if (Directory.Exists(transaction.InstallRoot)) Directory.Move(transaction.InstallRoot, transaction.FailedRoot);
            Directory.Move(transaction.PreviousRoot, transaction.InstallRoot);
            transaction = transaction with { Stage = UpdateStage.RolledBack }; transaction.Save();
        }
        return transaction;
    }
    internal static async Task<PackValidationResult> VerifyAsync(string path, string manifest, string? defaults, CancellationToken token, string expectedVersion, string expectedFlavor)
    {
        var pack = await PackIntegrityVerifier.ValidateAsync(path, expectedFlavor, cancellationToken: token).ConfigureAwait(false);
        if (pack.ManifestSha256 != manifest || pack.DefaultsSha256 != defaults || pack.Manifest.Version != expectedVersion)
            throw new FilproOperationException("update.changed", "Une copie du pack a changé depuis la préparation. Intervention explicite requise.");
        return pack;
    }
    private static string Limit(string text) => text.Length <= 2048 ? text : text[..2048];
}
