using Filpro.Core;
using Filpro.Platform;

namespace Filpro.App;

/// <summary>
/// Le dossier nul sélectionne Evergreen. PROD-LIGHT préfère Evergreen puis, à défaut, le moteur du Socle Windows extrait dans wad,
/// que PackIntegrityVerifier a déjà vérifié fichier par fichier. Les sources Git et les packs PROD/DEV gardent leur moteur embarqué.
/// </summary>
internal static class WebViewRuntimePolicy
{
    internal const string MissingBundled = "Le moteur WebView2 de ce pack est absent ou incomplet. Extrayez de nouveau le pack FILPRO, avec son dossier wad\\runtime\\webview2.";
    internal const string MissingEvergreen = "Pour ouvrir FILPRO, Microsoft Edge WebView2 Runtime Evergreen doit être installé pour tous les utilisateurs de ce poste. "
        + "Sinon, extrayez le pack WebView2 du Socle Windows dans le dossier wad de FILPRO : " + WebViewSocle.ReleaseUrl;
    internal const string FixedFailed = "Le moteur WebView2 de wad\\runtime\\webview2 n’a pas pu démarrer. Extrayez de nouveau FILPRO et, s’il y a lieu, "
        + "le pack WebView2 du Socle Windows (" + WebViewSocle.ReleaseUrl + "), ou faites installer Microsoft Edge WebView2 Runtime Evergreen pour tous les utilisateurs du poste.";
    internal const string BrowserExe = "msedgewebview2.exe";

    /// <summary>Texte d'un moteur fixe qui ne démarre pas : le Socle pour un pack LIGHT, le pack lui-même pour PROD, DEV et les sources.</summary>
    internal static string FixedFailureFor(PackValidationResult? verified)
        => verified?.Manifest.Flavor == PackManifestCodec.LightFlavor ? FixedFailed : MissingBundled;

    internal static string? SelectFolder(string packRoot, PackValidationResult? verified, Func<bool> evergreenUsable)
    {
        var folder = Path.Combine(packRoot, "wad", "runtime", "webview2");
        if (verified?.Manifest.Flavor == PackManifestCodec.LightFlavor)
        {
            if (evergreenUsable()) return null;
            return verified.WebViewSocleVersion is not null ? folder : throw new FilproOperationException("pack.evergreen", MissingEvergreen);
        }
        if (File.Exists(Path.Combine(folder, BrowserExe))) return folder;
        if (verified is null) return null; // Exécution depuis les sources Git, sans manifeste de distribution.
        throw new FilproOperationException("pack.runtime", MissingBundled);
    }

    internal static string[] ApprovedForVersion(IEnumerable<(string Browser, string Version)> installed, string sdkVersion)
    {
        var matches = installed.Where(runtime => runtime.Version == sdkVersion).Select(runtime => runtime.Browser).ToArray();
        return matches.Length != 0 ? matches : throw new FilproOperationException("pack.evergreen", MissingEvergreen);
    }

    /// <summary>Versions Evergreen enregistrées par EdgeUpdate dans HKLM. Aucun chemin ne provient du registre ou du profil utilisateur.</summary>
    internal static (string Browser, string Version)[] InstalledBrowsers(IEnumerable<string?> machineVersions, IReadOnlyList<string> programFolders)
    {
        var versions = machineVersions.Select(text => Version.TryParse(text, out var version) && version.Major > 0 && version.Revision >= 0
            && version.ToString() == text ? version : null).OfType<Version>().Distinct().OrderDescending();
        var installed = new List<(string Browser, string Version)>();
        foreach (var version in versions)
        foreach (var root in programFolders.Where(Path.IsPathFullyQualified))
        {
            var browser = Path.Combine(root, "Microsoft", "EdgeWebView", "Application", version.ToString(), BrowserExe);
            if (!File.Exists(browser)) continue;
            UserDataLocation.RefuseReparse(browser);
            installed.Add((browser, version.ToString()));
        }
        if (installed.Count == 0) throw new FilproOperationException("pack.evergreen", MissingEvergreen);
        return installed.ToArray();
    }
}
