using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Mode du même EXE, exécuté depuis une copie vérifiée hors du dossier remplacé.</summary>
public static class WindowsUpdateWorker
{
    private static readonly ConcurrentDictionary<string, int> HeldLeases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan StartupHealthTimeout = TimeSpan.FromMinutes(10);

    public static IDisposable AcquireInstallationLease(string packRoot)
    {
        var root = StoragePathPolicy.NormalizeNative(packRoot); UpdateTransaction.RequireSafeInstallation(root);
        var path = LeasePath(root); ConfigurationFileIO.NoLinks(path);
        var parentLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(path)!);
        try
        {
        if (!File.Exists(path))
        {
            try { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite); created.Flush(true); }
            catch (IOException) when (File.Exists(path)) { }
        }
        ConfigurationFileIO.NoLinks(path);
        var stream = StorageFileIdentity.OpenRead(path);
        HeldLeases.AddOrUpdate(root, 1, (_, value) => value + 1);
        return new HeldLease(root, stream, parentLease);
        }
        catch { parentLease.Dispose(); throw; }
    }
    internal static bool HasInstallationLease(string root) => HeldLeases.TryGetValue(root, out var count) && count > 0;

    public static async Task<int?> TryRunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args[0] is not ("--filpro-update-worker" or "--filpro-update-rollback")) return null;
        if (!OperatingSystem.IsWindows() || args.Length != 4) return 2;
        UpdateTransaction? transaction = null;
        try
        {
            var runningRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var flavor = PackIntegrityVerifier.ReadManifest(runningRoot).Flavor;
            transaction = UpdateTransaction.Read(args[3], args[1], args[2], flavor);
            if (!runningRoot.Equals(transaction.WorkerRoot, StringComparison.OrdinalIgnoreCase)) throw new FilproOperationException("update.worker", "Le remplacement doit être exécuté depuis sa copie isolée.");
            using var workerLock = await WaitForLockAsync(Path.Combine(transaction.DirectoryPath, "worker.lock"), TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            transaction = UpdateTransaction.Read(args[3], args[1], args[2], flavor);
            await VerifyWorkerAsync(transaction, cancellationToken).ConfigureAwait(false);
            var leaseRoot = transaction.InstallRoot;
            Task<IDisposable> Exclusive() => AcquireExclusiveAsync(leaseRoot, CancellationToken.None);
            if (transaction.Stage == UpdateStage.Committed && args[0] == "--filpro-update-worker") return 0;
            if (transaction.Stage == UpdateStage.RolledBack) return 0;
            if (args[0] == "--filpro-update-rollback" || transaction.Stage is UpdateStage.OldMoved or UpdateStage.NewInstalled or UpdateStage.AwaitingHealth or UpdateStage.RollingBack or UpdateStage.Failed)
            {
                transaction = await UpdateDirectoryTransaction.RollbackAsync(transaction, Exclusive, cancellationToken).ConfigureAwait(false);
                StartRestored(transaction); return 0;
            }
            var captured = transaction;
            transaction = await UpdateDirectoryTransaction.InstallAsync(transaction,
                () => StartAndWaitForHealthAsync(captured, cancellationToken), Exclusive, cancellationToken).ConfigureAwait(false);
            if (transaction.Stage == UpdateStage.RolledBack) StartRestored(transaction);
            return transaction.Stage == UpdateStage.Committed ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FilproOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (transaction is not null)
            {
                try { (transaction with { Stage = transaction.Stage == UpdateStage.RolledBack ? UpdateStage.RolledBack : UpdateStage.Failed, Error = ex.Message.Length > 2048 ? ex.Message[..2048] : ex.Message }).Save(); }
                catch (Exception save) when (save is IOException or UnauthorizedAccessException or FilproOperationException) { /* Les copies restent présentes même si le support ne permet plus de journaliser. */ }
            }
            return 1;
        }
    }

    internal static async Task VerifyWorkerAsync(UpdateTransaction transaction, CancellationToken token)
    {
        var worker = await PackIntegrityVerifier.ValidateAsync(transaction.WorkerRoot, transaction.ExpectedFlavor, cancellationToken: token).ConfigureAwait(false);
        if (worker.ManifestSha256 != transaction.PreviousManifestSha256 && worker.ManifestSha256 != transaction.TargetManifestSha256)
            throw new FilproOperationException("update.worker", "Le moteur ne correspond pas à cette transaction.");
    }

    /// <summary>À appeler après intégrité du pack et disponibilité réelle de l'interface, jamais à la seule création de processus.</summary>
    public static async Task<bool> AcknowledgeStartupAsync(string packRoot, string dataRoot, string[] args,
        Func<bool> stillReady, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stillReady);
        var index = Array.IndexOf(args, "--filpro-update-health"); if (index < 0) return false;
        if (!OperatingSystem.IsWindows() || index != 0 || args.Length != 3) throw new FilproOperationException("update.health", "Accusé de démarrage invalide.");
        var flavor = PackIntegrityVerifier.ReadManifest(packRoot).Flavor;
        var transaction = UpdateTransaction.Read(dataRoot, args[1], packRoot, flavor);
        if (transaction.Stage != UpdateStage.AwaitingHealth || args[2] != transaction.HealthToken || !HasInstallationLease(transaction.InstallRoot))
            throw new FilproOperationException("update.health", "La demande de santé ne correspond pas à la transaction active.");
        await UpdateDirectoryTransaction.VerifyAsync(packRoot, transaction.TargetManifestSha256, transaction.TargetDefaultsSha256, cancellationToken, transaction.TargetVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, transactionId = transaction.Id,
            token = transaction.HealthToken, manifestSha256 = transaction.TargetManifestSha256 });
        cancellationToken.ThrowIfCancellationRequested();
        if (!stillReady()) throw new FilproOperationException("update.health", "L'interface n'est plus prête ; aucun succès de démarrage n'est accusé.");
        ConfigurationFileIO.AtomicWrite(transaction.HealthPath, bytes, Path.Combine(transaction.DirectoryPath, "health-backups"), null, expectAbsent: true);
        return true;
    }

    private static async Task<bool> StartAndWaitForHealthAsync(UpdateTransaction transaction, CancellationToken token)
    {
        if (File.Exists(transaction.HealthPath)) throw new FilproOperationException("update.health", "Un accusé de santé existe avant le nouveau lancement.");
        var start = new ProcessStartInfo(Path.Combine(transaction.InstallRoot, "FILPRO.exe")) { UseShellExecute = false, WorkingDirectory = transaction.InstallRoot };
        start.Environment[ModeResolver.SimulationVariable] = "0";
        start.ArgumentList.Add("--filpro-update-health"); start.ArgumentList.Add(transaction.Id); start.ArgumentList.Add(transaction.HealthToken);
        using var process = Process.Start(start) ?? throw new IOException("La nouvelle application n'a pas démarré.");
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StartupHealthTimeout)
        {
            token.ThrowIfCancellationRequested();
            // Sortie relevée avant de chercher l'accusé : un accusé écrit juste avant une fermeture rapide est encore lu.
            var exited = process.HasExited;
            if (File.Exists(transaction.HealthPath))
            {
                using var health = JsonDocument.Parse(ConfigurationFileIO.Read(transaction.HealthPath, 4096));
                var root = health.RootElement; var names = root.EnumerateObject().Select(p => p.Name).ToArray();
                if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4
                    || !names.Order(StringComparer.Ordinal).SequenceEqual(new[] { "manifestSha256", "schemaVersion", "token", "transactionId" })
                    || root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("transactionId").GetString() != transaction.Id
                    || root.GetProperty("token").GetString() != transaction.HealthToken || root.GetProperty("manifestSha256").GetString() != transaction.TargetManifestSha256)
                    throw new FilproOperationException("update.health", "Accusé de santé incorrect.");
                return true;
            }
            if (exited) return false;
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        // Seul le processus lancé par cette transaction reçoit une demande de fermeture normale. Aucun Kill.
        if (!process.HasExited) { process.CloseMainWindow(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); } catch (OperationCanceledException) { } }
        throw new IOException("Le délai maximal de 10 minutes pour confirmer le nouveau démarrage est dépassé ; retour arrière demandé.");
    }

    private static void StartRestored(UpdateTransaction transaction)
    {
        var start = new ProcessStartInfo(Path.Combine(transaction.InstallRoot, "FILPRO.exe")) { UseShellExecute = false, WorkingDirectory = transaction.InstallRoot };
        start.Environment[ModeResolver.SimulationVariable] = "0"; using var process = Process.Start(start);
    }
    private static async Task<IDisposable> AcquireExclusiveAsync(string root, CancellationToken token)
    {
        var path = LeasePath(root); ConfigurationFileIO.NoLinks(path);
        if (!File.Exists(path)) throw new FilproOperationException("update.lease", "Verrou d'installation absent. Aucun remplacement effectué.");
        return await WaitForLockAsync(path, TimeSpan.FromMinutes(2), token).ConfigureAwait(false);
    }
    private static async Task<IDisposable> WaitForLockAsync(string path, TimeSpan timeout, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested(); ConfigurationFileIO.NoLinks(path);
            try { return ConfigurationFileIO.Lock(path); }
            catch (FilproOperationException ex) when (ex.Code == "storage.busy" && watch.Elapsed < timeout)
            { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }
    private static string LeasePath(string root) => Path.Combine(Path.GetDirectoryName(root)!, ".filpro-install-" + UpdateTransaction.InstallationKey(root) + ".lock");
    private sealed class HeldLease(string root, FileStream stream, IDisposable parentLease) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            stream.Dispose(); parentLease.Dispose(); HeldLeases.AddOrUpdate(root, 0, (_, count) => Math.Max(0, count - 1));
        }
    }
}
