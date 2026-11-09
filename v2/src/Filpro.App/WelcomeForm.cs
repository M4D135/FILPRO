using System.Runtime.InteropServices;
using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Filpro.App;

/// <summary>Entrée locale sans composition métier ni pont vers les profils, avant l'UAC.</summary>
internal sealed class WelcomeForm : Form
{
    private Icon? ownedWindowIcon;
    private readonly WebView2 view = new() { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Color.FromArgb(243, 246, 251) };
    private readonly Label loading = new() { Dock = DockStyle.Fill, Text = "Préparation de votre espace…", TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(32) };
    private readonly CancellationTokenSource lifetime = new();
    private Task initialization = Task.CompletedTask;
    private bool busy;
    private bool closing;
    private bool fitting;
    private bool fitRequested;
    private bool resourcesDisposed;

    private ElevationRefusal? pendingRefusal;

    internal WelcomeForm(ElevationRefusal refusal)
    {
        pendingRefusal = refusal;
        Text = "FILPRO · Connexion";
        ownedWindowIcon = BrandIcon.CreateOwned();
        Icon = ownedWindowIcon;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(1040, area.Width), Math.Min(700, area.Height));
        MinimumSize = new Size(Math.Min(640, area.Width), Math.Min(480, area.Height));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(243, 246, 251);
        Controls.Add(view); Controls.Add(loading);
        Load += async (_, _) => { initialization = InitializeAsync(); await initialization.ConfigureAwait(true); };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            if (busy) { e.Cancel = true; return; }
            if (!initialization.IsCompleted)
            {
                e.Cancel = true; lifetime.Cancel();
                await initialization.ConfigureAwait(true);
                closing = true; Close();
            }
        };
    }

    internal bool ContinueHere { get; private set; }

    private async Task InitializeAsync()
    {
        try
        {
            // Aucun HTML du pack n'est chargé avant la vérification. Les sources sans manifeste restent sans capacité d'écriture.
            PackValidationResult? verified = null;
            if (File.Exists(Path.Combine(AppPaths.AppDirectory, PackManifestCodec.ManifestPath)))
            {
                var manifest = PackIntegrityVerifier.ReadManifest(AppPaths.AppDirectory);
                if (manifest.Version != ProductInfo.Version) throw new FilproOperationException("pack.version", "Le pack et l’application annoncent des versions différentes.");
                var progress = new Progress<PackValidationProgress>(p => { if (!IsDisposed) loading.Text = $"Vérification de FILPRO… {p.CompletedFiles} / {p.TotalFiles}"; });
                verified = await Task.Run(() => PackIntegrityVerifier.ValidateForStartupAsync(AppPaths.AppDirectory, manifest.Flavor, progress, lifetime.Token), lifetime.Token).ConfigureAwait(true);
            }
            if (verified is not null && verified.Manifest.Version != ProductInfo.Version)
                throw new FilproOperationException("pack.version", "Le manifeste vérifié et l’application n’annoncent pas la même version.");
            var folder = WebViewRuntimePolicy.SelectFolder(AppPaths.AppDirectory, verified, WebViewRuntime.EvergreenUsable);
            var environment = await WebViewRuntime.StartAsync(view, folder, AppPaths.PrepareWebView("WebView2-Login").Path, WebViewRuntimePolicy.FixedFailureFor(verified)).ConfigureAwait(true);
            lifetime.Token.ThrowIfCancellationRequested();
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsReputationCheckingRequired = false;
            core.SetVirtualHostNameToFolderMapping(ProductInfo.WebHost, AppPaths.WebRoot, CoreWebView2HostResourceAccessKind.Deny);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) => { if (!IsLocal(e.Request.Uri)) e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", ""); };
            core.NavigationStarting += (_, e) => e.Cancel = !IsLogin(e.Uri);
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.ProcessFailed += (_, _) => Fail("L’écran de connexion n’a pas pu rester ouvert. Fermez puis relancez FILPRO.");
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess || !IsLogin(core.Source)) { Fail("L’écran de connexion est indisponible. Relancez le pack complet."); return; }
                loading.Visible = false; view.Visible = true;
            };
            core.WebMessageReceived += async (_, e) =>
            {
                if (!IsLogin(e.Source) || closing) return;
                var action = StartupPolicy.ReadAction(e.WebMessageAsJson);
                if (action == "layout") { await FitContentAsync().ConfigureAwait(true); return; }
                if (busy) return;
                if (action == "info")
                {
                    Reply("ready", WindowsElevation.AccountName(), WindowsElevation.IsElevated());
                    // L'UAC demandée au lancement a déjà été annulée ou refusée : la page l'explique, boutons actifs.
                    if (pendingRefusal is { } refusal) { pendingRefusal = null; Reply(refusal.State, message: refusal.Message); }
                }
                else if (action == "close") Close();
                else if (action == "open") await OpenAsync().ConfigureAwait(true);
            };
            core.Navigate($"https://{ProductInfo.WebHost}/login.html");
        }
        catch (OperationCanceledException) { }
        catch (FilproOperationException ex)
        {
            TechnicalLog.Instance.Error("Écran d’entrée indisponible.", ex);
            Fail(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException)
        {
            TechnicalLog.Instance.Error("Écran d’entrée indisponible.", ex);
            Fail("FILPRO n’a pas pu préparer la connexion. Vérifiez le pack complet puis relancez l’application.");
        }
    }

    private async Task OpenAsync()
    {
        busy = true;
        Reply("opening");
        try
        {
            if (WindowsElevation.IsElevated()) { ContinueHere = true; closing = true; Close(); return; }
            var refusal = await Task.Run(WindowsElevation.TryRelaunch).ConfigureAwait(true);
            if (refusal is null) { closing = true; Close(); }
            else Reply(refusal.State, message: refusal.Message);
        }
        finally { busy = false; }
    }

    private void Reply(string state, string? account = null, bool? elevated = null, string? message = null)
        => view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "filpro.startup", state, account, elevated, message, version = ProductInfo.Version }));

    private async Task FitContentAsync()
    {
        fitRequested = true;
        if (fitting) return;
        fitting = true;
        try
        {
            while (fitRequested && !closing && !IsDisposed)
            {
                fitRequested = false;
                // Mesure locale fixe : la page ne fournit ni taille native ni commande à exécuter.
                var json = await view.CoreWebView2.ExecuteScriptAsync("""
                    (() => {
                      const access = document.querySelector('.entry-access');
                      const inner = document.querySelector('.entry-access-inner');
                      const footer = document.querySelector('.entry-access-footer');
                      if (!access || !inner || !footer) return null;
                      const style = getComputedStyle(access);
                      return { width: innerWidth, height: Math.ceil(access.offsetTop + inner.scrollHeight + footer.scrollHeight
                        + parseFloat(style.paddingTop) + parseFloat(style.paddingBottom) + 2) };
                    })()
                    """).ConfigureAwait(true);
                if (closing || IsDisposed || WindowState != FormWindowState.Normal) continue;
                using var measurement = JsonDocument.Parse(json);
                if (measurement.RootElement.ValueKind != JsonValueKind.Object) continue;
                var width = measurement.RootElement.GetProperty("width").GetDouble();
                var height = measurement.RootElement.GetProperty("height").GetDouble();
                if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || height > 16384) continue;
                var area = Screen.FromControl(this).WorkingArea;
                var frame = Height - ClientSize.Height;
                var required = Math.Min(area.Height - frame, (int)Math.Ceiling(height * view.ClientSize.Width / width));
                if (required <= ClientSize.Height) continue;
                ClientSize = new Size(ClientSize.Width, required);
                Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException)
        { TechnicalLog.Instance.Warning("La taille de l’écran d’entrée n’a pas pu être actualisée."); }
        finally { fitting = false; }
    }
    private void Fail(string text) { if (!IsDisposed) { view.Visible = false; loading.Visible = true; loading.Text = text; } }
    private static bool IsLocal(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == ProductInfo.WebHost && uri.IsDefaultPort && uri.UserInfo.Length == 0;
    private static bool IsLogin(string address) => IsLocal(address) && new Uri(address).AbsolutePath == "/login.html"
        && new Uri(address).Query.Length == 0;
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            // Close() libère la Form ; le using de Program peut ensuite appeler Dispose() à nouveau.
            resourcesDisposed = true;
            Icon = null;
            ownedWindowIcon?.Dispose();
            ownedWindowIcon = null;
            lifetime.Cancel(); view.Dispose(); lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
