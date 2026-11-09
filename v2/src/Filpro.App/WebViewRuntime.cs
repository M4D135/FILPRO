using System.ComponentModel;
using System.Runtime.InteropServices;
using Filpro.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Filpro.App;

internal static class WebViewRuntime
{
    private const string RuntimeClient = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    internal static async Task<CoreWebView2Environment> StartAsync(WebView2 view, string? folder, string data, string fixedFailure)
    {
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
        string[] approved;
        string? evergreenVersion = null;
        try
        {
            RefuseLoaderRedirects();
            if (folder is null) (approved, evergreenVersion) = ApproveEvergreen(options);
            else approved = [Path.Combine(folder, WebViewRuntimePolicy.BrowserExe)];
            var environment = await CoreWebView2Environment.CreateAsync(folder, data, options).ConfigureAwait(true);
            if (evergreenVersion is not null && environment.BrowserVersionString != evergreenVersion)
                throw new FilproOperationException("pack.evergreen", WebViewRuntimePolicy.MissingEvergreen);
            await view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            // Un moteur déjà partagé ou sélectionné par une stratégie doit toujours correspondre au moteur approuvé,
            // avant de naviguer ou de raccorder le pont. Windows reste chargé de lancer le navigateur sous le compte de session.
            var browserPid = view.CoreWebView2.BrowserProcessId;
            if (!approved.Contains(BrowserImagePath(browserPid), StringComparer.OrdinalIgnoreCase))
                throw new FilproOperationException("pack.runtime", "Windows a lancé un moteur WebView2 différent de celui autorisé par ce pack. Vérifiez la stratégie WebView2 du poste.");
            TechnicalLog.Instance.Info($"WebView2 initialisé (FILPRO {Environment.ProcessId}, navigateur {browserPid}).");
            return environment;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or FileNotFoundException)
        {
            TechnicalLog.Instance.Error("Moteur WebView2 indisponible.", ex);
            throw new FilproOperationException("pack.runtime", folder is null ? WebViewRuntimePolicy.MissingEvergreen : fixedFailure);
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or System.Security.SecurityException)
        {
            TechnicalLog.Instance.Error("Identité du moteur WebView2 illisible.", ex);
            throw new FilproOperationException("pack.runtime", UnverifiableEngine);
        }
    }

    private const string UnverifiableEngine = "Windows ne permet pas de vérifier le moteur WebView2 lancé pour cette session. Demandez à l’administrateur de vérifier son installation et les droits du compte.";

    /// <summary>
    /// Sonde Evergreen sans créer d'environnement, pour choisir le Socle avant tout lancement du navigateur.
    /// Une stratégie de redirection reste une erreur : elle n'est jamais contournée par le moteur du Socle.
    /// </summary>
    internal static bool EvergreenUsable()
    {
        try
        {
            RefuseLoaderRedirects();
            _ = ApproveEvergreen(new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false });
            return true;
        }
        catch (FilproOperationException ex) when (ex.Code == "pack.evergreen") { return false; }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or FileNotFoundException)
        { TechnicalLog.Instance.Info("WebView2 Evergreen indisponible : " + ex.GetType().Name + "."); return false; }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or System.Security.SecurityException)
        {
            TechnicalLog.Instance.Error("Moteur WebView2 Evergreen illisible.", ex);
            throw new FilproOperationException("pack.runtime", UnverifiableEngine);
        }
    }

    private static (string[] Approved, string Version) ApproveEvergreen(CoreWebView2EnvironmentOptions options)
    {
        options.ReleaseChannels = CoreWebView2ReleaseChannels.Stable;
        var installed = WebViewRuntimePolicy.InstalledBrowsers(ReadMachineVersions(),
            [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)]);
        var version = CoreWebView2Environment.GetAvailableBrowserVersionString(null, options);
        return (WebViewRuntimePolicy.ApprovedForVersion(installed, version), version);
    }

    // Lire le nom de l'image ne nécessite pas d'ouvrir la mémoire du navigateur, même si son propriétaire diffère.
    private static string BrowserImagePath(uint pid)
    {
        using var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new char[32768];
        var size = (uint)buffer.Length;
        if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new string(buffer, 0, checked((int)size));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle handle, uint flags, [Out] char[] path, ref uint size);

    private static string?[] ReadMachineVersions()
    {
        static string? InstalledVersion(RegistryView view)
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var client = machine.OpenSubKey(RuntimeClient);
            return client?.GetValue("pv", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        return [InstalledVersion(RegistryView.Registry32), InstalledVersion(RegistryView.Registry64)];
    }

    private static void RefuseLoaderRedirects()
    {
        // Le SDK donne priorité aux stratégies sur le dossier du moteur, les données et les arguments de l'application.
        // L'écran d'accueil peut aussi tourner sans élévation, où Windows prend en compte les stratégies HKCU.
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            foreach (var setting in new[] { "BrowserExecutableFolder", "UserDataFolder", "AdditionalBrowserArguments" })
            {
                using var policy = registry.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge\WebView2\" + setting);
                if (policy is null) continue;
                foreach (var name in new[] { Path.GetFileName(Environment.ProcessPath), "*" })
                    if (policy.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string { Length: > 0 })
                        throw new FilproOperationException("pack.runtimePolicy", "Une stratégie Windows redirige WebView2 pour FILPRO. Demandez à l’administrateur de vérifier la stratégie « " + setting + " ».");
            }
        }
    }
}
