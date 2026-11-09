using System.Diagnostics;
using System.Security.Cryptography;
using Filpro.Core;

namespace Filpro.Platform;

public sealed partial class LocalUpdateService : IFilproUpdateService, IDisposable
{
    private readonly string _packRoot;
    private readonly string _dataRoot;
    private readonly string? _installedFlavor;
    private readonly Func<bool> _applicationIsIdle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (UpdateCandidate Candidate, DateTimeOffset Expires)> _offers = new(StringComparer.Ordinal);
    private UpdateStatus _status;
    public bool Demonstration => false;
    public event Action<UpdateStatus>? Changed;

    public LocalUpdateService(string packRoot, string dataRoot, Func<bool> applicationIsIdle)
    {
        _packRoot = StoragePathPolicy.NormalizeNative(packRoot); _dataRoot = StoragePathPolicy.NormalizeNative(dataRoot);
        _applicationIsIdle = applicationIsIdle ?? throw new ArgumentNullException(nameof(applicationIsIdle));
        if (StoragePathPolicy.IsWithin(_packRoot, _dataRoot) || StoragePathPolicy.IsWithin(_dataRoot, _packRoot))
            throw new FilproOperationException("update.location", "Le pack et les données personnelles doivent être distincts.");
        var version = ProductInfo.Version;
        var manifest = Path.Combine(_packRoot, PackManifestCodec.ManifestPath);
        if (File.Exists(manifest))
        {
            var installed = PackIntegrityVerifier.ReadManifest(_packRoot);
            version = installed.Version;
            _installedFlavor = installed.Flavor;
        }
        _status = new(false, UpdateStage.Idle, null, version, null, "Aucune recherche effectuée", null, false, false, null, [PackManifestCodec.IntegrityNotice]);
    }

    private string InstalledProductionFlavor() => PackManifestCodec.IsProductionFlavor(_installedFlavor) ? _installedFlavor!
        : throw new FilproOperationException("update.flavor", "Les mises à jour exigent une installation PROD ou PROD-LIGHT.");

    public Task<UpdateStatus> ReadAsync(CancellationToken cancellationToken) => LockedAsync(() =>
    {
        var latest = LatestTransaction(); if (latest is not null) SetStatus(latest, Notice(latest.Stage));
        return Task.FromResult(_status);
    }, cancellationToken);

    public Task<UpdateOffer> InspectAsync(UpdateSourceRequest request, CancellationToken cancellationToken)
    {
        // Chemin vide : rien n'est lu, et l'état de la mise à jour ne passe pas en échec.
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind is UpdateSourceKind.Folder or UpdateSourceKind.ManualZip && string.IsNullOrWhiteSpace(request.Path))
            return Task.FromException<UpdateOffer>(MissingSourcePath());
        return InspectLockedAsync(request, cancellationToken);
    }

    private Task<UpdateOffer> InspectLockedAsync(UpdateSourceRequest request, CancellationToken cancellationToken) => LockedAsync(() =>
    {
        RequireNoPendingTransaction();
        Publish(_status with { Stage = UpdateStage.Inspecting, Action = "Lecture explicite de la source de mise à jour", ProgressPercent = null, Error = null });
        var candidate = UpdateSources.Inspect(request, _status.CurrentVersion, InstalledProductionFlavor());
        foreach (var id in _offers.Where(p => p.Value.Expires < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _offers.Remove(id);
        if (_offers.Count >= 16) _offers.Remove(_offers.Keys.First());
        _offers[candidate.Offer.Id] = (candidate, DateTimeOffset.UtcNow.AddMinutes(15));
        Publish(_status with { Stage = UpdateStage.Available, TargetVersion = candidate.Offer.Version, Action = candidate.Offer.Newer ? "Version disponible ; confirmer avant copie" : "Aucune version plus récente", CanInstall = false });
        return Task.FromResult(candidate.Offer);
    }, cancellationToken);

    public Task<UpdateStatus> PrepareAsync(string offerId, bool confirmed, CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        Confirm(confirmed); RequireNoPendingTransaction(); UpdateTransaction.RequireSafeInstallation(_packRoot);
        if (!_offers.TryGetValue(offerId, out var entry) || entry.Expires < DateTimeOffset.UtcNow) throw new FilproOperationException("update.offer", "Offre absente ou expirée ; refaire l'inspection.");
        var candidate = entry.Candidate;
        if (!candidate.Offer.Newer) throw new FilproOperationException("update.version", "Installer une version identique ou inférieure est refusé. Utiliser le retour arrière d'une transaction connue.");
        var progress = new InlineProgress(value => Publish(_status with
        {
            Action = value.Path, ProgressPercent = value.TotalBytes > 0 ? 100d * value.CompletedBytes / value.TotalBytes : null,
        }));
        Publish(_status with { Stage = UpdateStage.Preparing, Action = "Contrôle de l'installation avant préparation", ProgressPercent = 0, CanInstall = false, Error = null });
        var current = await PackIntegrityVerifier.ValidateAsync(_packRoot, InstalledProductionFlavor(), progress, cancellationToken).ConfigureAwait(false);
        if (PackageVersion.Parse(candidate.Offer.Version).CompareTo(PackageVersion.Parse(current.Manifest.Version)) <= 0) throw new FilproOperationException("update.version", "La version installée a changé depuis l'inspection.");
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(UpdateTransaction.HistoryRoot(_dataRoot, current.Manifest.Flavor), id); ConfigurationFileIO.NoLinks(directory); ConfigurationFileIO.FileSystem.CreatePrivateDirectory(directory);
        var archive = Path.Combine(directory, "package.zip");
        await UpdateSources.CopyAsync(candidate, archive, progress, cancellationToken).ConfigureAwait(false);
        var work = Path.Combine(Path.GetDirectoryName(_packRoot)!, ".filpro-update-" + UpdateTransaction.InstallationKey(_packRoot) + "-" + id);
        ConfigurationFileIO.NoLinks(work); if (Directory.Exists(work) || File.Exists(work)) throw new IOException("La préparation existe déjà.");
        ConfigurationFileIO.FileSystem.CreatePrivateDirectory(work);
        Publish(_status with { TransactionId = id, Action = "Extraction bornée et vérification complète du ZIP" });
        var stage = Path.Combine(work, "stage");
        var prepared = await StrictPackArchive.ExtractAsync(archive, stage, candidate.Offer.Sha256, progress, cancellationToken, current.Manifest.Flavor).ConfigureAwait(false);
        if (prepared.Manifest.Version != candidate.Offer.Version) throw new FilproOperationException("update.version", "Version du manifeste différente de l'offre inspectée.");
        if (current.WebViewSocleVersion is { } socle)
        {
            // L'offre LIGHT ne transporte jamais de moteur : la nouvelle version reçoit une copie vérifiée du Socle déjà installé.
            Publish(_status with { Action = "Copie du moteur WebView2 du Socle dans la nouvelle version" });
            await PackIntegrityVerifier.CopyWebViewSocleAsync(_packRoot, stage, socle, progress, cancellationToken).ConfigureAwait(false);
            var carried = await PackIntegrityVerifier.ValidateAsync(stage, current.Manifest.Flavor, progress, cancellationToken).ConfigureAwait(false);
            if (carried.ManifestSha256 != prepared.ManifestSha256 || carried.DefaultsSha256 != prepared.DefaultsSha256 || carried.WebViewSocleVersion != socle)
                throw new FilproOperationException("update.changed", "La préparation a changé pendant la copie du moteur WebView2 du Socle. Aucune installation n'a été remplacée.");
        }
        // Les anciens workers PROD doivent toujours pouvoir relire leurs journaux à 14 champs.
        var light = current.Manifest.Flavor == PackManifestCodec.LightFlavor;
        var transaction = new UpdateTransaction(light ? 2 : 1, id, _packRoot, _dataRoot, current.Manifest.Version, current.ManifestSha256, current.DefaultsSha256,
            prepared.Manifest.Version, prepared.ManifestSha256, prepared.DefaultsSha256, UpdateStage.Prepared,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow, null)
            { Flavor = light ? current.Manifest.Flavor : null };
        transaction.Save(create: true); _offers.Remove(offerId);
        SetStatus(transaction, "Pack validé et préparé. Confirmer l'installation puis fermer FILPRO proprement.");
        return _status;
    }, cancellationToken);

    public Task<UpdateHandoff> InstallAsync(string transactionId, bool confirmed, CancellationToken cancellationToken) =>
        HandoffAsync(transactionId, rollback: false, confirmed, cancellationToken);
    public Task<UpdateHandoff> RollbackAsync(string transactionId, bool confirmed, CancellationToken cancellationToken) =>
        HandoffAsync(transactionId, rollback: true, confirmed, cancellationToken);

    private Task<UpdateHandoff> HandoffAsync(string transactionId, bool rollback, bool confirmed, CancellationToken cancellationToken) => LockedAsync<UpdateHandoff>(async () =>
    {
        Confirm(confirmed);
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("update.windows", "Le remplacement de l'application exige Windows ; aucun exécutable n'a été lancé.");
        if (!_applicationIsIdle()) throw new FilproOperationException("update.busy", "Attendre la fin des sauvegardes, restaurations et réparations avant de fermer l'application.");
        if (!WindowsUpdateWorker.HasInstallationLease(_packRoot)) throw new FilproOperationException("update.lease", "Le verrou d'installation n'est pas détenu. Mise à jour désactivée pour cette session.");
        UpdateTransaction.RequireId(transactionId);
        var flavor = InstalledProductionFlavor();
        var transactionDirectory = Path.Combine(UpdateTransaction.HistoryRoot(_dataRoot, flavor), transactionId);
        using var transactionLock = ConfigurationFileIO.Lock(Path.Combine(transactionDirectory, "worker.lock"));
        var transaction = UpdateTransaction.Read(_dataRoot, transactionId, _packRoot, flavor);
        if (!rollback && transaction.Stage != UpdateStage.Prepared) throw new FilproOperationException("update.state", "La transaction n'est pas préparée pour installation.");
        if (rollback && !UpdateDirectoryTransaction.HasRollbackCandidate(transaction)) throw new FilproOperationException("update.rollback", "Aucune version précédente ni reprise disponible pour cette transaction.");
        var current = await PackIntegrityVerifier.ValidateAsync(_packRoot, flavor, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (current.ManifestSha256 != transaction.PreviousManifestSha256 && current.ManifestSha256 != transaction.TargetManifestSha256)
            throw new FilproOperationException("update.changed", "L’installation active ne correspond pas à cette transaction.");
        if (!rollback)
        {
            var stage = await UpdateDirectoryTransaction.VerifyAsync(transaction.StageRoot, transaction.TargetManifestSha256, transaction.TargetDefaultsSha256, cancellationToken, transaction.TargetVersion, transaction.ExpectedFlavor).ConfigureAwait(false);
            // La préparation a recopié le Socle présent à ce moment-là ; un Socle ajouté, retiré ou changé depuis serait perdu au remplacement.
            // Le journal passe en échec sans dossier précédent : une nouvelle préparation reste possible.
            if (current.ManifestSha256 == transaction.PreviousManifestSha256 && stage.WebViewSocleVersion != current.WebViewSocleVersion)
            {
                const string changed = "Le moteur WebView2 du Socle de l’installation a changé depuis la préparation. Préparer de nouveau la mise à jour.";
                transaction = transaction with { Stage = UpdateStage.Failed, Error = changed }; transaction.Save();
                SetStatus(transaction, changed);
                throw new FilproOperationException("update.changed", changed);
            }
        }
        if (!Directory.Exists(transaction.WorkerRoot))
            await PackIntegrityVerifier.CopyAsync(_packRoot, transaction.WorkerRoot, current, cancellationToken).ConfigureAwait(false);
        else
        {
            var worker = await PackIntegrityVerifier.ValidateAsync(transaction.WorkerRoot, transaction.ExpectedFlavor, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (worker.ManifestSha256 != transaction.PreviousManifestSha256 && worker.ManifestSha256 != current.ManifestSha256) throw new FilproOperationException("update.worker", "Copie du moteur de mise à jour inconnue.");
        }
        if (rollback) await UpdateDirectoryTransaction.VerifyRollbackSourceAsync(transaction, cancellationToken).ConfigureAwait(false);
        transaction = transaction with { Stage = rollback ? UpdateStage.RollingBack : UpdateStage.WaitingForExit, Error = null }; transaction.Save();
        var start = new ProcessStartInfo(Path.Combine(transaction.WorkerRoot, "FILPRO.exe")) { UseShellExecute = false, WorkingDirectory = transaction.WorkerRoot };
        start.ArgumentList.Add(rollback ? "--filpro-update-rollback" : "--filpro-update-worker");
        start.ArgumentList.Add(transaction.Id); start.ArgumentList.Add(transaction.InstallRoot); start.ArgumentList.Add(transaction.DataRoot);
        try { using var process = Process.Start(start) ?? throw new IOException("Le moteur de remplacement n'a pas démarré."); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            (transaction with { Stage = UpdateStage.Failed, Error = "Le moteur de remplacement n'a pas démarré." }).Save();
            throw new FilproOperationException("update.worker", "Le moteur de remplacement n'a pas démarré ; l'installation est inchangée.");
        }
        SetStatus(transaction, "Le moteur attend la fermeture de toutes les instances de FILPRO. Aucun processus ne sera tué.");
        return new(transaction.Id, false, true, "Fermer FILPRO après la fin des opérations. Le remplacement sera confirmé uniquement après le contrôle du nouveau démarrage.");
    }, cancellationToken);

    private UpdateTransaction? LatestTransaction()
        => Transactions().OrderByDescending(transaction => transaction.CreatedUtc).FirstOrDefault();

    private IReadOnlyList<UpdateTransaction> Transactions()
    {
        if (!PackManifestCodec.IsProductionFlavor(_installedFlavor)) return [];
        var root = UpdateTransaction.HistoryRoot(_dataRoot, _installedFlavor!); ConfigurationFileIO.NoLinks(root);
        if (!Directory.Exists(root)) return [];
        using var rootLease = ConfigurationFileIO.FileSystem.HoldDirectory(root);
        var entries = Directory.EnumerateDirectories(root).Take(201).ToArray(); if (entries.Length > 200) throw new FilproOperationException("update.history", "Historique des mises à jour trop volumineux ; conserver les reçus avant nettoyage explicite.");
        var result = new List<UpdateTransaction>();
        foreach (var path in entries)
        {
            ConfigurationFileIO.NoLinks(path); var id = Path.GetFileName(path); UpdateTransaction.RequireId(id);
            if (!File.Exists(Path.Combine(path, "transaction.json"))) continue; // Préparation interrompue : aucune installation touchée.
            var transaction = UpdateTransaction.Read(_dataRoot, id, expectedFlavor: _installedFlavor!);
            if (transaction.InstallRoot != _packRoot) continue;
            result.Add(transaction);
        }
        return result;
    }
    private void RequireNoPendingTransaction()
    {
        if (Transactions().Any(transaction => transaction.Stage is not (UpdateStage.Committed or UpdateStage.RolledBack)
            && (transaction.Stage != UpdateStage.Failed || Directory.Exists(transaction.PreviousRoot))))
            throw new FilproOperationException("update.pending", "Une transaction existe déjà. Consulter son état ou effectuer son retour arrière avant une autre mise à jour.");
    }
    private void SetStatus(UpdateTransaction transaction, string action) => Publish(new(false, transaction.Stage, transaction.Id,
        _status.CurrentVersion, transaction.TargetVersion, action, transaction.Stage is UpdateStage.Prepared or UpdateStage.Committed or UpdateStage.RolledBack ? 100 : null,
        transaction.Stage == UpdateStage.Prepared, UpdateDirectoryTransaction.HasRollbackCandidate(transaction), transaction.Error, [PackManifestCodec.IntegrityNotice],
        transaction.PreviousVersion));
    private void Publish(UpdateStatus status) { _status = status; Changed?.Invoke(status); }
    private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { using var locked = ConfigurationFileIO.Lock(Path.Combine(_dataRoot, "update.lock")); return await action().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FilproOperationException or HttpRequestException
            or System.Text.Json.JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Publish(_status with { Stage = UpdateStage.Failed, CanInstall = false, Action = "Action arrêtée ; consulter le journal avant de recommencer", Error = ex.Message, ProgressPercent = null });
            if (ex is FilproOperationException or OperationCanceledException) throw;
            throw new FilproOperationException("update.failed", ex.Message);
        }
        finally { _gate.Release(); }
    }
    private static string Notice(UpdateStage stage) => stage switch
    {
        UpdateStage.Prepared => "Pack préparé ; installation non démarrée.",
        UpdateStage.Committed => "Nouveau démarrage confirmé. Version précédente conservée pour retour arrière.",
        UpdateStage.RolledBack => "Version précédente rétablie. Les réglages personnels sont conservés.",
        UpdateStage.Failed => "Échec enregistré. Les copies et journaux sont conservés.",
        _ => "Transaction en cours ou interrompue. Ne pas supprimer les copies ; utiliser le retour arrière si le moteur ne tourne plus.",
    };
    private static void Confirm(bool confirmed) { if (!confirmed) throw new FilproOperationException("update.confirm", "Confirmation explicite requise avant préparation, installation ou retour arrière."); }
    public void Dispose() => _gate.Dispose();
    private sealed class InlineProgress(Action<PackValidationProgress> callback) : IProgress<PackValidationProgress>
    { public void Report(PackValidationProgress value) => callback(value); }
}
