using System.Collections;

namespace Filpro.App;

/// <summary>
/// Variables d'environnement héritées que FILPRO, toujours élevé, ne laisse pas décider à sa place. Les variables
/// WEBVIEW2_* choisissent le moteur WebView2, son dossier de données, son canal ou ses arguments de lancement, et
/// n'importe quel programme du compte de la session peut les poser dans HKCU\Environment. Elles sont retirées du processus
/// avant tout appel à WebView2, donc aussi des processus qu'il lance. Sans WinForms, liée aux tests.
/// </summary>
internal static class EnvironmentGuard
{
    internal const string WebView2Prefix = "WEBVIEW2_";

    private const int NameLimit = 64;

    /// <summary>Retire chaque variable dont le nom commence par WEBVIEW2_, sans casse ; rend les noms retirés, jamais les valeurs.</summary>
    internal static IReadOnlyList<string> RemoveWebView2Overrides()
    {
        var removed = new List<string>();
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is not string name || !name.StartsWith(WebView2Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            Environment.SetEnvironmentVariable(name, null);
            removed.Add(name);
        }
        removed.Sort(StringComparer.OrdinalIgnoreCase);
        return removed;
    }

    /// <summary>Noms pour le journal : posés par un autre programme, ils ne peuvent ni couper la ligne ni l'allonger sans fin.</summary>
    internal static string Describe(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return string.Join(", ", names.Select(name => NativeWindowPolicy.Visible(name, NameLimit)));
    }
}
