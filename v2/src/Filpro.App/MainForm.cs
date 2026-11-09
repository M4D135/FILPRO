using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Filpro.App;

/// <summary>Hôte Windows du kit DOMENIA : contenu local, pont fermé et cycle de vie possédé par la session.</summary>
internal sealed partial class MainForm : Form, IApplicationSessionControl
{
    private Icon? ownedWindowIcon;
    private const string StartFailedMessage = "L’affichage de FILPRO n’a pas pu démarrer. Le détail technique est dans le journal de l’application.";

    // Élevé avec un autre compte que celui de la session, l'affichage tourne sous le compte de la session : il doit pouvoir
    // lire le dossier de FILPRO (moteur et interface), ce qui n'est pas le cas d'un dossier rangé dans le profil administrateur.
    // Ce compte ne doit pas pouvoir le modifier : FILPRO s'exécute toujours élevé.
    private string StartFailureText() => webViewSessionAccount is { } account
        ? StartFailedMessage + " L’affichage s’exécute sous le compte ouvert sur ce poste (" + account + ") : " + PlacementAdvice
        : StartFailedMessage;
    private const string PlacementAdvice = "placez FILPRO dans un dossier que ce compte peut lire sans pouvoir le modifier, par exemple C:\\Program Files\\FILPRO, puis relancez-le.";
    // Au-delà, un démarrage encore en cours est signalé sans être interrompu : il peut toujours aboutir.
    private static readonly TimeSpan SlowStart = TimeSpan.FromSeconds(25);
    // Un refus établi n'est affiché que si le moteur n'a pas démarré entre-temps : Authz reste une estimation.
    private static readonly TimeSpan UnreadableGrace = TimeSpan.FromSeconds(5);
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Color.FromArgb(245, 246, 249) };
    private readonly Label loading = new() { Dock = DockStyle.Fill, UseMnemonic = false, Text = "Ouverture de FILPRO…", TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(36) };
    private readonly ConcurrentDictionary<int, Task> inFlight = new();
    private readonly BridgeDispatcher dispatcher;
    private readonly Composition composition;
    private readonly string[] startupArguments;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource pageLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int exitRequested;
    private int shuttingDown;
    private bool allowClose;
    private volatile bool pageAvailable;
    private bool healthAcknowledged;
    private readonly SemaphoreSlim healthGate = new(1, 1);
    private readonly object sessionChange = new();
    private Task initialization = Task.CompletedTask;
    private string? demoDataDirectory;
    // Compte de la session quand le profil d'affichage de cette fenêtre a été préparé pour lui ; nul sinon.
    private string? webViewSessionAccount;
    private IFilproExecutionService? execution;
    private IFilproUpdateService? updates;
    // Présence du processus élevé, partagée par toutes les compositions ; relevée toutes les 10 s une fois armée.
    private readonly IOperatorPresence presence;
    private readonly System.Windows.Forms.Timer presenceTimer = new() { Interval = 10_000 };

    public MainForm(Composition composition, string[] startupArguments, IOperatorPresence presence)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(presence);
        this.composition = composition; this.startupArguments = startupArguments; this.presence = presence;
        Text = CurrentTitle;
        presence.Changed += OnPresenceChanged;
        presenceTimer.Tick += (_, _) => PollPresence();
        presenceTimer.Start();
        ownedWindowIcon = BrandIcon.CreateOwned();
        Icon = ownedWindowIcon;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        MinimumSize = new Size(Math.Min(820, workArea.Width), Math.Min(540, workArea.Height));
        Size = new Size(Math.Min(1440, (int)(workArea.Width * 0.94)), Math.Min(900, (int)(workArea.Height * 0.94)));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 246, 249);
        Controls.Add(webView); Controls.Add(loading);
        composition.Context.Register<IApplicationSessionControl>(this);
        // Seule autorité des opérations privilégiées du poste : la fenêtre Windows du processus élevé, jamais la page.
        if (composition.Decision.Mode == AppMode.Poste) composition.Context.Register<IOperatorConfirmation>(this);
        dispatcher = new BridgeDispatcher(composition.Registry, PostToPage, composition.Admission);
        dispatcher.UnhandledError += (operation, exception) => TechnicalLog.Instance.Error($"Échec de l’opération {operation}.", exception);
        dispatcher.Refused += (code, operation) => TechnicalLog.Instance.Warning($"Message refusé : {code}" + (operation is null ? "." : $" ({operation})."));
        Load += async (_, _) =>
        {
            initialization = InitializeWebViewAsync();
            await initialization.ConfigureAwait(true);
        };
        FormClosing += OnFormClosing;
    }

    public AppMode? RequestedMode { get; private set; }

    private async Task InitializeWebViewAsync()
    {
        if (AppPaths.UserData is null)
        {
            ShowFallback("Aucun dossier de données utilisable pour ce compte Windows : FILPRO ne peut pas initialiser son interface."); return;
        }
        // Une réinitialisation de la démonstration recrée aussi le stockage web ; aucun choix fictif ne survit au nouveau monde.
        var profile = AppPaths.WebViewProfile(composition.Decision.Mode,
            composition.Decision.Mode == AppMode.Simulation ? composition.Context.Generation : null);
        CoreWebView2Environment environment;
        WebViewDataLocation.ProfileFolder folder;
        try
        {
            var progress = new Progress<PackValidationProgress>(value =>
            {
                if (!IsDisposed) loading.Text = "Vérification du pack… " + value.CompletedFiles + " / " + value.TotalFiles;
            });
            await Task.Run(() => composition.InitializeAsync(progress, lifetime.Token), lifetime.Token).ConfigureAwait(true);
            if (composition.Context.Service<IFilproConfigurationService>() is { } configuration)
            {
                var settings = await configuration.ReadAsync(lifetime.Token).ConfigureAwait(true);
                if (settings.Settings.Theme == "dark")
                {
                    BackColor = loading.BackColor = webView.DefaultBackgroundColor = Color.FromArgb(18, 22, 30);
                    loading.ForeColor = Color.FromArgb(235, 239, 247);
                }
            }
            if (!File.Exists(Path.Combine(AppPaths.WebRoot, "index.html")))
                throw new FilproOperationException("pack.web", "L’interface wad\\web est absente. Relancez FILPRO depuis le pack complet.");
            execution = composition.Context.Service<IFilproExecutionService>();
            updates = composition.Context.Service<IFilproUpdateService>();
            if (execution is not null) execution.Changed += OnExecutionChanged;
            if (updates is not null) updates.Changed += OnUpdateChanged;
            loading.Text = "Initialisation de l’interface…";
            folder = AppPaths.PrepareWebView(profile);
            webViewSessionAccount = folder.SessionAccount;
            var userData = folder.Path;
            if (composition.Decision.Mode == AppMode.Simulation) demoDataDirectory = userData;
            var runtimeFolder = WebViewRuntimePolicy.SelectFolder(AppPaths.AppDirectory, composition.VerifiedPack, WebViewRuntime.EvergreenUsable);
            var start = WebViewRuntime.StartAsync(webView, runtimeFolder, userData, WebViewRuntimePolicy.FixedFailureFor(composition.VerifiedPack));
            if (folder is { SessionAccount: { } account, SessionSid: { } sessionSid }) _ = ExplainSlowStartAsync(start, account, sessionSid, runtimeFolder);
            // Un moteur que Windows ne parvient pas à lancer ne rend jamais la main : la fermeture ne doit pas l'attendre.
            environment = await start.WaitAsync(lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { return; }
        catch (FilproOperationException ex)
        { TechnicalLog.Instance.Error("Pack non disponible.", ex); ShowFallback(ex.Message); return; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException)
        { TechnicalLog.Instance.Error("Démarrage de WebView2 impossible.", ex); ShowFallback(StartFailureText()); return; }
        if (lifetime.IsCancellationRequested) return;
        var core = webView.CoreWebView2;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsReputationCheckingRequired = false;
        core.SetVirtualHostNameToFolderMapping(ProductInfo.WebHost, AppPaths.WebRoot, CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!IsAllowedResource(e.Request.Uri)) e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
        core.NavigationStarting += (_, e) => { if (!IsInterfaceAddress(e.Uri)) e.Cancel = true; };
        core.FrameNavigationStarting += (_, e) => { if (!IsInterfaceAddress(e.Uri) && e.Uri is not ("about:blank" or "about:srcdoc")) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ProcessFailed += (_, _) => { pageAvailable = false; RefusePendingConfirmation(); ShowFallback(StartFailureText()); };
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess && IsInterfaceAddress(core.Source))
            {
                pageAvailable = true; pageLoaded.TrySetResult(); loading.Visible = false; webView.Visible = true;
            }
            else { pageAvailable = false; ShowFallback(StartFailureText()); }
        };
        // Situation établie avant le premier message de la page : la fenêtre de confirmation et le pont la lisent ensuite.
        if (composition.Decision.Mode == AppMode.Poste) RegisterSituation(core, folder.SessionSid);
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate($"https://{ProductInfo.WebHost}/index.html");
    }

    private static bool IsInterfaceAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, ProductInfo.WebHost, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);
    private static bool IsAllowedResource(string? address) => IsInterfaceAddress(address)
        || (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "data" or "blob" or "about");

    private void ShowFallback(string message)
    {
        webView.Visible = false; loading.Text = message; loading.Visible = true;
    }

    /// <summary>
    /// Élevé avec un autre compte que celui de la session, WebView2 lance son moteur sous ce compte : s'il ne peut pas lire
    /// le pack, Windows le signale et le démarrage reste en attente. Le motif est affiché dès qu'il est établi, sinon un
    /// démarrage qui tarde est signalé ; rien n'est interrompu ni refusé, l'affichage peut encore aboutir.
    /// </summary>
    private async Task ExplainSlowStartAsync(Task start, string account, string sessionSid, string? runtimeFolder)
    {
        try
        {
            var check = Task.Run(() => SessionAccess.UnreadableEngine(sessionSid, runtimeFolder, AppPaths.WebRoot));
            if (await SessionAccess.WatchAsync(start, check, UnreadableGrace, SlowStart, lifetime.Token).ConfigureAwait(true) is not { } notice) return;
            if (notice.UnreadablePath is { } unreadable)
            {
                TechnicalLog.Instance.Warning("Le compte de la session " + account + " ne peut pas lire " + unreadable
                    + " : l’affichage, lancé sous ce compte, ne démarre pas.");
                ShowStartNotice(start, "L’affichage de FILPRO ne peut pas démarrer : le compte ouvert sur ce poste (" + account
                    + ") ne peut pas lire " + unreadable + " ; " + PlacementAdvice);
            }
            else
            {
                TechnicalLog.Instance.Warning("Affichage toujours en démarrage après " + SlowStart.TotalSeconds + " s sous le compte de la session " + account + ".");
                ShowStartNotice(start, "L’affichage de FILPRO tarde à démarrer. Il s’exécute sous le compte ouvert sur ce poste (" + account
                    + ") : si ce compte ne peut pas lire le dossier de FILPRO, " + PlacementAdvice);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { TechnicalLog.Instance.Warning("Diagnostic du démarrage de l’affichage indisponible : " + ex.Message); }
    }

    // Seulement tant que le démarrage est en cours : un échec ou une réussite a déjà son propre affichage.
    private void ShowStartNotice(Task start, string message)
    {
        if (!start.IsCompleted && !IsDisposed && !lifetime.IsCancellationRequested) loading.Text = message;
    }
    private void OnExecutionChanged(ExecutionJobSnapshot snapshot) => PostEvent("operations.changed", snapshot);
    private void OnUpdateChanged(UpdateStatus status) => PostEvent("updates.changed", status);
    private void PostEvent(string name, object data) => PostToPage(new BridgeResponse(null, "event", name, data));

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsInterfaceAddress(e.Source) || composition.Context.IsClosing) return;
        var json = e.WebMessageAsJson;
        var task = Task.Run(async () =>
        {
            try { await dispatcher.HandleAsync(json, lifetime.Token).ConfigureAwait(false); }
#pragma warning disable CA1031 // Une tâche de pont ne doit jamais devenir une exception non observée.
            catch (Exception ex) { TechnicalLog.Instance.Error("Message de l’interface non traité.", ex); }
#pragma warning restore CA1031
        }, CancellationToken.None);
        inFlight[task.Id] = task;
        _ = task.ContinueWith(t =>
        {
            inFlight.TryRemove(t.Id, out _);
            if (Volatile.Read(ref exitRequested) != 0 && !IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(async () => await BeginShutdownAsync().ConfigureAwait(true)); }
                catch (InvalidOperationException) { /* La fermeture a déjà achevé le cycle de vie. */ }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void PostToPage(BridgeResponse response)
    {
        var json = JsonSerializer.Serialize(response, BridgeJson.Options);
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && webView.CoreWebView2 is not null)
                {
                    try { webView.CoreWebView2.PostWebMessageAsJson(json); }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException)
                    { TechnicalLog.Instance.Warning("Réponse non transmise : surface web fermée."); }
                }
            });
        }
        catch (InvalidOperationException) { TechnicalLog.Instance.Warning("Réponse non transmise : fenêtre fermée."); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (ownedWindowIcon is { } ownedIcon)
            {
                Icon = null;
                ownedWindowIcon = null;
                ownedIcon.Dispose();
            }
            if (execution is not null) execution.Changed -= OnExecutionChanged;
            if (updates is not null) updates.Changed -= OnUpdateChanged;
            presence.Changed -= OnPresenceChanged;
            presenceTimer.Dispose();
            lifetime.Dispose(); healthGate.Dispose(); webView.Dispose();
            // Un cache encore possédé par WebView2 reste sur disque ; aucune suppression forcée ni suivi de jonctions.
            if (demoDataDirectory is not null)
                TechnicalLog.Instance.Info("Fin de la session de démonstration ; son stockage web reste séparé du poste réel.");
        }
        base.Dispose(disposing);
    }
}
