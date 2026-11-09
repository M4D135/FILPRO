using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Filpro.Core;
using Microsoft.Web.WebView2.Core;

namespace Filpro.App;

/// <summary>
/// Présentation de la fenêtre « FILPRO — Confirmation Windows », possédée par cette fenêtre élevée. Une seule fenêtre native
/// modale à la fois, sélecteur de fichiers ou confirmation, sans file d'attente. La page reste désactivée tant que la
/// fenêtre est ouverte ; toute erreur fait échouer la demande et rien n'est lancé.
/// </summary>
internal sealed partial class MainForm : IOperatorConfirmation
{
    // Sélecteur de fichiers ou confirmation ouverte ; comme la fenêtre affichée, uniquement sur le fil de l'interface.
    private bool nativeModalOpen;
    private NativeConfirmationForm? pendingConfirmation;

    public Task<ConfirmationResult> ConfirmAsync(ConfirmationSheet sheet, CancellationToken cancellationToken)
    {
        if (sheet is null) return Task.FromException<ConfirmationResult>(new ArgumentNullException(nameof(sheet)));
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<ConfirmationResult>(cancellationToken);
        // Constaté à l'arrivée de la demande : FILPRO en arrière-plan rend la demande suspecte et arme plus lentement l'accord.
        var requestedInForeground = FilproIsForeground();
        return PresentInThemeAsync(sheet, requestedInForeground, cancellationToken);
    }

    /// <summary>Lit le thème choisi dans FILPRO, puis présente la fenêtre sur le fil de l'interface.</summary>
    private async Task<ConfirmationResult> PresentInThemeAsync(ConfirmationSheet sheet, bool requestedInForeground,
        CancellationToken cancellationToken)
    {
        var theme = await ConfiguredThemeAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<ConfirmationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(() => Present(sheet, requestedInForeground, theme, completion, cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Thème des réglages de FILPRO, tenus par FILPRO élevé (liste fermée « system », « light », « dark », « contrast »), lu
    /// sans effet sur les réglages ni sur leurs aperçus ; « system » s'il n'est pas lisible après une seconde d'attente. Seul
    /// l'aspect de la fenêtre en dépend ; une demande annulée reste annulée.
    /// </summary>
    private async Task<string> ConfiguredThemeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (composition.Context.Service<IFilproConfigurationService>() is not { } configuration) return "system";
            return await configuration.ReadThemeAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false) ?? "system";
        }
#pragma warning disable CA1031 // Réglages illisibles : la fenêtre garde le thème du système, la demande continue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            cancellationToken.ThrowIfCancellationRequested();
            TechnicalLog.Instance.Warning("Thème de FILPRO illisible pour la fenêtre de confirmation (" + ex.GetType().Name + ") : thème du système.");
            return "system";
        }
    }

    /// <summary>Applications de Windows en mode sombre pour le compte de FILPRO élevé ; faux si le réglage est illisible.</summary>
    private static bool WindowsAppsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
#pragma warning disable CA1031 // Réglage de Windows illisible : thème clair, la demande continue.
        catch (Exception) { return false; }
#pragma warning restore CA1031
    }

    private void Present(ConfirmationSheet sheet, bool requestedInForeground, string theme, TaskCompletionSource<ConfirmationResult> completion,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }
        // Affichage tombé pendant la lecture du thème : la page n'existe plus, aucune fenêtre.
        if (!pageAvailable)
        {
            completion.TrySetException(new FilproOperationException(FilproErrors.ConfirmationUnavailable, FilproErrors.ConfirmationUnavailableMessage));
            return;
        }
        // Verrouillé pendant le trajet de la demande : aucune fenêtre.
        if (presence.Latched)
        {
            completion.TrySetException(new FilproOperationException(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage));
            return;
        }
        if (!TryEnterNativeModal())
        {
            completion.TrySetException(new FilproOperationException(FilproErrors.ConfirmationBusy, FilproErrors.ConfirmationBusyMessage));
            return;
        }
        var result = new ConfirmationResult(ConfirmationOutcome.Refused);
        Exception? failure = null;
        var shown = false;
        try
        {
            // Une fenêtre possédée par une fenêtre réduite resterait cachée.
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            var situation = composition.Context.Service<HostSituation>();
            var palette = ConfirmationTheme.Resolve(theme, SystemInformation.HighContrast, WindowsAppsDark());
            using var dialog = new NativeConfirmationForm(sheet,
                NativeWindowPolicy.SituationLine(situation, situation?.HostAccount ?? WindowsElevation.AccountName()),
                requestedInForeground, Screen.FromControl(this).WorkingArea, palette);
            dialog.Activated += (_, _) => Flash(false);
            pendingConfirmation = dialog;
            shown = true;
            webView.Enabled = false;
            PostEvent("confirmation.changed", new { Pending = true });
            if (!requestedInForeground) Flash(true);
            using (cancellationToken.Register(() => RefuseLater(dialog)))
            {
                dialog.ShowDialog(this);
            }
            cancellationToken.ThrowIfCancellationRequested();
            result = new ConfirmationResult(dialog.Outcome, dialog.Outcome == ConfirmationOutcome.Approved && dialog.LockAfterStart);
        }
#pragma warning disable CA1031 // Toute erreur de présentation fait échouer la demande : rien n'est lancé.
        catch (Exception ex) { failure = ex; }
#pragma warning restore CA1031
        finally
        {
            pendingConfirmation = null;
            // La place est toujours rendue : sinon toute demande suivante serait refusée comme occupée jusqu'à la relance.
            try
            {
                if (shown)
                {
                    Flash(false);
                    if (!webView.IsDisposed) webView.Enabled = true;
                    PostEvent("confirmation.changed", new { Pending = false });
                }
            }
#pragma warning disable CA1031 // Comme une erreur de présentation, elle fait échouer la demande : rien n'est lancé.
            catch (Exception ex) { failure ??= ex; }
#pragma warning restore CA1031
            finally { LeaveNativeModal(); }
        }
        // Une fermeture de FILPRO l'emporte sur toute décision : l'opération n'est pas lancée.
        if (failure is OperationCanceledException || cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
        else if (failure is not null) completion.TrySetException(failure);
        else completion.TrySetResult(result);
    }

    /// <summary>Le jeton peut être annulé depuis n'importe quel fil : la fenêtre se ferme sur le fil de l'interface.</summary>
    private void RefuseLater(NativeConfirmationForm dialog)
    {
        try { BeginInvoke(dialog.Refuse); }
        catch (InvalidOperationException) { /* Fenêtre principale détruite : la fenêtre qu'elle possédait l'est aussi. */ }
    }

    /// <summary>Un moteur d'affichage en échec ferme la fenêtre en attente : refus, rien n'est lancé.</summary>
    private void RefusePendingConfirmation() => pendingConfirmation?.Refuse();

    // Les fenêtres natives sont modales sur le fil de l'interface : une demande arrivée pendant l'une d'elles y est traitée
    // par sa boucle de messages et trouve la place prise.
    private bool TryEnterNativeModal()
    {
        if (nativeModalOpen) return false;
        nativeModalOpen = true;
        return true;
    }

    private void LeaveNativeModal() => nativeModalOpen = false;

    private static bool FilproIsForeground()
    {
        var window = Native.GetForegroundWindow();
        return window != IntPtr.Zero && Native.GetWindowThreadProcessId(window, out var process) != 0 && process == (uint)Environment.ProcessId;
    }

    /// <summary>Bouton de FILPRO dans la barre des tâches : clignote jusqu'au premier plan, arrêté dès l'activation de la fenêtre.</summary>
    private void Flash(bool start)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var info = new Native.FlashInfo
        {
            Size = (uint)Marshal.SizeOf<Native.FlashInfo>(),
            Window = Handle,
            Flags = start ? Native.FlashAll | Native.FlashUntilForeground : Native.FlashStop,
        };
        _ = Native.FlashWindowEx(ref info);
    }

    /// <summary>
    /// Compare le compte du moteur d'affichage réellement lancé (propriétaire du processus navigateur de WebView2) au compte
    /// élevé de FILPRO et au compte de la session retenu pour le profil d'affichage. Un compte introuvable donne une
    /// situation indéterminée ; sans compte élevé lisible, rien n'est enregistré et la situation reste indéterminée.
    /// </summary>
    private void RegisterSituation(CoreWebView2 core, string? shellSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value is not { } hostSid)
        {
            TechnicalLog.Instance.Warning("Compte élevé sans SID : situation de l’affichage indéterminée.");
            return;
        }
        string? browserSid = null;
        try
        {
            var browser = core.BrowserProcessId;
            using var self = Process.GetCurrentProcess();
            if (WebViewDataLocation.SessionProcessOwners((uint)self.SessionId).TryGetValue(browser, out var owner)) browserSid = owner;
        }
        catch (Exception ex) when (ex is Win32Exception or SystemException)
        {
            TechnicalLog.Instance.Warning("Compte du moteur d’affichage illisible (" + ex.GetType().Name + ") : situation indéterminée.");
        }
        var situation = new HostSituation(NativeWindowPolicy.Classify(hostSid, browserSid, shellSid), hostSid, WebViewDataLocation.AccountName(hostSid),
            browserSid, browserSid is null ? null : WebViewDataLocation.AccountName(browserSid));
        composition.Context.Register(situation);
        TechnicalLog.Instance.Info(NativeWindowPolicy.SituationLog(situation));
        if (situation.Kind == SessionSituation.SameAccount) return;
        // Autre compte ou compte indéterminé : verrou de présence actif jusqu'à la fin du processus élevé. Un verrouillage de
        // session reçu pendant le démarrage s'applique à l'armement ; l'état courant de la session est relu aussitôt.
        presence.Arm();
        CheckSessionState();
    }

    private static class Native
    {
        // FLASHW_ALL (légende et bouton de la barre des tâches), FLASHW_TIMERNOFG (jusqu'au premier plan), FLASHW_STOP.
        internal const uint FlashAll = 0x3;
        internal const uint FlashUntilForeground = 0xC;
        internal const uint FlashStop = 0;

        // FLASHWINFO.
        [StructLayout(LayoutKind.Sequential)]
        internal struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags, Count, Timeout;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlashWindowEx(ref FlashInfo info);
    }
}
