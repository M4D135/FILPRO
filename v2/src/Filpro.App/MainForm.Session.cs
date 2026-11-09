using Filpro.Core;
using Filpro.Platform;

namespace Filpro.App;

internal sealed partial class MainForm
{
    public Task RequestRestartAsync(AppMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode is not (AppMode.Poste or AppMode.Simulation)) throw new ArgumentOutOfRangeException(nameof(mode));
        lock (sessionChange)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref exitRequested) != 0 || composition.Context.IsClosing || composition.Context.Operations.UpdateReserved)
                throw new FilproOperationException("session.closing", "Une fermeture ou une mise à jour est déjà engagée.");
            using var transition = composition.Context.Operations.EnterUpdateHandoff(() =>
                execution?.Active().Any() != true);
            RequestedMode = mode;
            composition.Context.BeginClosing();
            Interlocked.Exchange(ref exitRequested, 1);
        }
        return Task.CompletedTask;
    }
    public Task RequestExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sessionChange)
        {
            cancellationToken.ThrowIfCancellationRequested();
            composition.Context.BeginClosing();
            Interlocked.Exchange(ref exitRequested, 1);
        }
        // Le gestionnaire du pont publie d'abord son résultat ; sa continuation déclenche ensuite la fermeture.
        return Task.CompletedTask;
    }

    public async Task AcknowledgeReadyAsync(string generation, CancellationToken cancellationToken)
    {
        if (generation != composition.Context.Generation) throw new FilproOperationException("session.stale", "Page d’une autre session.");
        await pageLoaded.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        // La page vient de s'abonner : l'état de l'hôte annoncé avant ce moment (verrou, dossier modifiable) lui est redit.
        if (composition.Decision.Mode == AppMode.Poste)
        {
            if (presence.Latched) PostEvent("presence.changed", new { Locked = true });
            if (composition.Context.Service<PackExposure>() is not null) PostEvent("pack.exposure", new { Modifiable = true });
        }
        if (!startupArguments.Contains("--filpro-update-health", StringComparer.Ordinal)) return;
        if (composition.Decision.Mode != AppMode.Poste || !composition.PackVerified || !pageAvailable)
            throw new FilproOperationException("update.health", "Le démarrage du pack natif n’est pas confirmé.");
        await healthGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (healthAcknowledged) return;
            healthAcknowledged = await WindowsUpdateWorker.AcknowledgeStartupAsync(AppPaths.AppDirectory, AppPaths.UserData!,
                startupArguments, () => pageAvailable && !IsDisposed && !composition.Context.IsClosing && generation == composition.Context.Generation,
                cancellationToken).ConfigureAwait(false);
        }
        finally { healthGate.Release(); }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (Volatile.Read(ref shuttingDown) != 0) return;
        var active = execution?.Active().ToArray() ?? [];
        if (active.Length > 0 && Volatile.Read(ref exitRequested) == 0)
        {
            if (pageAvailable) { PostEvent("session.closeRequested", new { ActiveOperations = active }); return; }
            if (MessageBox.Show(this, "Une opération est en cours. Demander son arrêt et attendre son nettoyage avant de fermer ?",
                ProductInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        lock (sessionChange) { composition.Context.BeginClosing(); Interlocked.Exchange(ref exitRequested, 1); }
        _ = BeginShutdownAsync();
    }

    private async Task BeginShutdownAsync()
    {
        if (Interlocked.Exchange(ref shuttingDown, 1) != 0) return;
        composition.Context.BeginClosing();
        Text = composition.WindowTitle + " — fermeture en cours";
        try
        {
            lifetime.Cancel();
            await initialization.ConfigureAwait(true);
            if (execution is not null) await execution.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);
            // Pas de délai qui abandonnerait des ruches ou des écritures : toutes les réponses en vol sont drainées.
            await Task.WhenAll(inFlight.Values.ToArray()).ConfigureAwait(true);
            allowClose = true; Close();
        }
#pragma warning disable CA1031 // Un échec du nettoyage doit garder la fenêtre ouverte et annoncer l'échec.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TechnicalLog.Instance.Error("Nettoyage non terminé ; fermeture suspendue.", ex);
            RequestedMode = null;
            Interlocked.Exchange(ref shuttingDown, 0);
            ShowFallback("Le nettoyage n’a pas pu être confirmé. FILPRO reste ouvert. Le détail est dans le journal ; réessayer de fermer après résolution du problème.");
        }
    }

    public Task<string?> PickPathAsync(ApplicationPathKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (composition.Decision.Mode != AppMode.Poste) throw new FilproOperationException("simulation.readOnly", "Aucun sélecteur de fichiers réels en démonstration.");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(() =>
        {
            // Verrouillé : le sélecteur, ouvert par FILPRO élevé, parcourrait le poste avec les droits administrateur.
            if (presence.Latched)
            {
                completion.TrySetException(new FilproOperationException(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage));
                return;
            }
            // Une seule fenêtre native à la fois : aucun sélecteur pendant une confirmation Windows, ni deux sélecteurs empilés.
            if (!TryEnterNativeModal())
            {
                completion.TrySetException(new FilproOperationException(FilproErrors.ConfirmationBusy, FilproErrors.ConfirmationBusyMessage));
                return;
            }
            // Autre compte : un sélecteur oublié se ferme seul au bout de 2 minutes, comme Annuler.
            System.Windows.Forms.Timer? expiry = null;
            if (presence.Armed)
            {
                expiry = new System.Windows.Forms.Timer { Interval = (int)NativeWindowPolicy.PickerTimeout.TotalMilliseconds };
                expiry.Tick += (_, _) =>
                {
                    expiry.Stop();
                    if (CancelOpenPicker()) TechnicalLog.Instance.Info("Sélecteur de fichiers fermé automatiquement après 2 minutes (affichage sous un autre compte).");
                };
                expiry.Start();
            }
            pickerOpen = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? selected;
                if (kind == ApplicationPathKind.Folder)
                {
                    using var dialog = new FolderBrowserDialog { Description = "Choisir un dossier local, USB ou un partage réseau", UseDescriptionForTitle = true, ShowNewFolderButton = true };
                    selected = dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
                }
                else if (kind == ApplicationPathKind.SaveConfiguration)
                {
                    using var dialog = new SaveFileDialog { Title = "Exporter la configuration partageable", Filter = "Configuration JSON (*.json)|*.json", DefaultExt = "json", FileName = "FILPRO-configuration.json", OverwritePrompt = true, AddExtension = true };
                    selected = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
                }
                else
                {
                    using var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = false, RestoreDirectory = true,
                        Filter = kind switch
                        {
                            ApplicationPathKind.OpenConfiguration => "Configuration JSON (*.json)|*.json",
                            ApplicationPathKind.UpdateZip => "Pack ZIP (*.zip)|*.zip",
                            ApplicationPathKind.EncryptionKey => "Fichier de clé (*.*)|*.*",
                            ApplicationPathKind.RepairReceipt => "Reçu de réparation FILPRO (*.json)|*.json",
                            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                        } };
                    selected = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
                }
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(selected);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
#pragma warning disable CA1031 // Toute erreur de dialogue devient une réponse du pont.
            catch (Exception ex) { completion.TrySetException(ex); }
#pragma warning restore CA1031
            finally
            {
                pickerOpen = false;
                expiry?.Dispose();
                LeaveNativeModal();
            }
        });
        return completion.Task;
    }
}
