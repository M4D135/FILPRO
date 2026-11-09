using Filpro.App;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class WebViewRuntimePolicyTests
{
    [Test]
    public static void LesSourcesPeuventUtiliserEvergreenMaisLesPacksCompletsExigentLeurMoteur()
    {
        using var fixture = new PackTestFixture();
        var folder = Path.Combine(fixture.Root, "wad", "runtime", "webview2");
        Func<bool> unused = () => throw new TestFailureException("Evergreen n’est sondé que pour un pack LIGHT.");
        Check.Null(WebViewRuntimePolicy.SelectFolder(fixture.Root, null, unused), "sources sans moteur embarqué : utiliser Evergreen");
        foreach (var flavor in new[] { "PROD", "DEV" })
            Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.SelectFolder(fixture.Root, Result(flavor), unused), "pack complet sans moteur embarqué refusé");
        Check.Null(WebViewRuntimePolicy.SelectFolder(fixture.Root, Result("PROD-LIGHT"), () => true), "pack LIGHT vérifié : utiliser Evergreen");
        PackTestFixture.Write(fixture.Root, "wad/runtime/webview2/msedgewebview2.exe", "fixture");
        Check.Equal(folder, WebViewRuntimePolicy.SelectFolder(fixture.Root, null, unused), "sources avec moteur embarqué : le préférer");
        foreach (var flavor in new[] { "PROD", "DEV" })
            Check.Equal(folder, WebViewRuntimePolicy.SelectFolder(fixture.Root, Result(flavor), unused), "dossier embarqué imposé");
    }

    [Test]
    public static void LightPrefereEvergreenPuisLeSocleVerifie()
    {
        using var fixture = new PackTestFixture();
        var folder = Path.Combine(fixture.Root, "wad", "runtime", "webview2");
        PackTestFixture.Write(fixture.Root, "wad/runtime/webview2/msedgewebview2.exe", "fixture");
        var withSocle = Result("PROD-LIGHT") with { WebViewSocleVersion = WebViewSocle.CurrentVersion };
        Check.Null(WebViewRuntimePolicy.SelectFolder(fixture.Root, withSocle, () => true), "Evergreen utilisable : choisi avant le Socle");
        Check.Equal(folder, WebViewRuntimePolicy.SelectFolder(fixture.Root, withSocle, () => false), "Evergreen absent : Socle vérifié");
        var refused = Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.SelectFolder(fixture.Root, Result("PROD-LIGHT"), () => false),
            "un moteur présent mais non vérifié n’est jamais lancé");
        Check.Equal("pack.evergreen", refused.Code, "Evergreen ou Socle requis");
        Check.Contains("Evergreen", refused.Message, "installation Evergreen proposée");
        Check.Contains(WebViewSocle.ReleaseUrl, refused.Message, "lien vers le pack WebView2 du Socle");
        Check.False(refused.Message.Contains("PROD", StringComparison.Ordinal), "aucun renvoi vers un pack PROD retiré");
        Check.Contains("Evergreen", WebViewRuntimePolicy.FixedFailed, "échec du moteur fixe : Evergreen proposé aussi");
        Check.Equal(WebViewRuntimePolicy.FixedFailed, WebViewRuntimePolicy.FixedFailureFor(withSocle), "LIGHT : réextraire le Socle");
        foreach (var verified in new[] { Result("PROD"), Result("DEV"), null })
            Check.Equal(WebViewRuntimePolicy.MissingBundled, WebViewRuntimePolicy.FixedFailureFor(verified), "moteur embarqué : réextraire le pack");
    }

    [Test]
    public static void EvergreenExigeLaVersionStableEnregistreePourLaMachine()
    {
        using var fixture = new PackTestFixture();
        const string installed = "150.0.1.2";
        PackTestFixture.Write(fixture.Root, "Microsoft/EdgeWebView/Application/150.0.1.2/msedgewebview2.exe", "fixture");
        var runtime = WebViewRuntimePolicy.InstalledBrowsers([null, "149.0.0.1", installed], [fixture.Root]).Single();
        Check.Equal(Path.Combine(fixture.Root, "Microsoft", "EdgeWebView", "Application", installed, "msedgewebview2.exe"), runtime.Browser,
            "seul le dossier machine de la version enregistrée est proposé au SDK");
        Check.Equal(runtime.Browser, WebViewRuntimePolicy.ApprovedForVersion([runtime], installed).Single(), "moteur Evergreen effectivement choisi par le SDK");
        foreach (var selectedBySdk in new[] { installed + " beta", "149.0.0.1" })
            Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.ApprovedForVersion([runtime], selectedBySdk), "canal ou version SDK hors de l’installation machine refusé");
        foreach (var candidate in new[] { "150.0.1.2 beta", "150.0.1.3", "../../Users/admin", "0.0.0.0" })
            Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.InstalledBrowsers([candidate], [fixture.Root]), "canal/chemin/version absente refusé");
        Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.InstalledBrowsers([], [fixture.Root]), "installation par utilisateur seule refusée");
        Check.Throws<FilproOperationException>(() => WebViewRuntimePolicy.InstalledBrowsers([installed], [fixture.Data]), "inscription sans moteur refusée");
    }

    private static PackValidationResult Result(string flavor) => new(new PackManifest(1, "FILPRO", ProductInfo.Version, "win-x64", flavor, new string('a', 40), []),
        new string('b', 64), null, 0, 0, "fixture", null);
}
