using System.Drawing;
using Filpro.App;

namespace Filpro.Tests;

/// <summary>
/// Palette FILPRO de la fenêtre « FILPRO — Confirmation Windows » : choisie par le thème des réglages de FILPRO, jamais
/// par une couleur venue de la page ; couleurs système en contraste élevé de Windows ; textes lisibles (WCAG AA) dans
/// chaque thème.
/// </summary>
internal sealed class ConfirmationThemeTests
{
    [Test]
    public void LeThemeDesReglagesChoisitLaPalette()
    {
        Check.Equal(ConfirmationTheme.Light, ConfirmationTheme.Resolve("light", false, true), "clair, même si Windows est sombre");
        Check.Equal(ConfirmationTheme.Dark, ConfirmationTheme.Resolve("dark", false, false), "sombre, même si Windows est clair");
        Check.Equal(ConfirmationTheme.Contrast, ConfirmationTheme.Resolve("contrast", false, false), "contraste élevé de FILPRO");
        Check.Equal(ConfirmationTheme.Dark, ConfirmationTheme.Resolve("system", false, true), "système : applications Windows sombres");
        Check.Equal(ConfirmationTheme.Light, ConfirmationTheme.Resolve("system", false, false), "système : applications Windows claires");
        Check.Equal(ConfirmationTheme.Light, ConfirmationTheme.Resolve("#ff0000", false, false), "valeur inconnue : thème du système");
        Check.Equal(ConfirmationTheme.Dark, ConfirmationTheme.Resolve(null, false, true), "réglage absent : thème du système");
        Check.Equal(ConfirmationTheme.Light, ConfirmationTheme.Resolve("Dark", false, false), "nom exact seulement");
    }

    [Test]
    public void LeContrasteEleveDeWindowsGardeLesCouleursSysteme()
    {
        foreach (var setting in new[] { "light", "dark", "contrast", "system", null })
        {
            Check.Null(ConfirmationTheme.Resolve(setting, true, false), "contraste élevé de Windows, thème " + (setting ?? "absent"));
            Check.Null(ConfirmationTheme.Resolve(setting, true, true), "contraste élevé de Windows sombre, thème " + (setting ?? "absent"));
        }
    }

    [Test]
    public void LesPalettesSontFermeesEtOpaques()
    {
        Check.Equal(3, ConfirmationTheme.All.Count, "trois palettes compilées");
        Check.Equal(3, ConfirmationTheme.All.Select(palette => palette.Name).Distinct(StringComparer.Ordinal).Count(), "noms distincts");
        foreach (var palette in ConfirmationTheme.All)
        {
            var colors = typeof(ConfirmationPalette).GetProperties().Where(property => property.PropertyType == typeof(Color)).ToList();
            Check.Equal(18, colors.Count, "toutes les couleurs de la palette sont examinées (" + palette.Name + ")");
            foreach (var property in colors)
                Check.Equal(255, ((Color)property.GetValue(palette)!).A, palette.Name + "." + property.Name + " opaque");
        }
        Check.False(ConfirmationTheme.Light.Dark, "clair");
        Check.True(ConfirmationTheme.Dark.Dark, "sombre : barre de titre sombre");
        Check.True(ConfirmationTheme.Contrast.Dark, "contraste : barre de titre sombre");
    }

    [Test]
    public void LesTextesRestentLisiblesDansChaqueTheme()
    {
        foreach (var palette in ConfirmationTheme.All)
        {
            // Texte : 4,5:1 (WCAG AA) ; contours et anneau du focus : 3:1 (composants d'interface).
            Text(palette, palette.Text, palette.Window, "texte");
            Text(palette, palette.Text, palette.Field, "valeur");
            Text(palette, palette.Muted, palette.Window, "texte secondaire");
            Text(palette, palette.Muted, palette.Field, "accord désactivé");
            Text(palette, palette.Alert, palette.Window, "point grave");
            Text(palette, palette.Warn, palette.Window, "motif du bouton désactivé");
            Text(palette, palette.OnPrimary, palette.PrimaryStart, "accord armé, début du dégradé");
            Text(palette, palette.OnPrimary, palette.PrimaryEnd, "accord armé, fin du dégradé");
            Text(palette, palette.Text, palette.Hover, "Annuler survolé");
            Part(palette, palette.OnAccent, palette.Accent, "coche");
            Part(palette, palette.Focus, palette.Window, "anneau du focus");
            Part(palette, palette.Control, palette.Window, "contour de case et d'Annuler");
            Part(palette, palette.Accent, palette.Window, "case cochée");
        }
    }

    [Test]
    public void LeMelangeEtLeContrasteSontExacts()
    {
        Check.Equal(Color.FromArgb(255, 255, 255).ToArgb(), ConfirmationTheme.Blend(Color.White, Color.Black, 0).ToArgb(), "0 : première couleur");
        Check.Equal(Color.FromArgb(0, 0, 0).ToArgb(), ConfirmationTheme.Blend(Color.White, Color.Black, 1).ToArgb(), "1 : seconde couleur");
        Check.Equal(Color.FromArgb(128, 128, 128).ToArgb(), ConfirmationTheme.Blend(Color.White, Color.Black, 0.5).ToArgb(), "moitié");
        Check.Equal(Color.FromArgb(0, 0, 0).ToArgb(), ConfirmationTheme.Blend(Color.White, Color.Black, 7).ToArgb(), "au-delà de 1 : borné");
        Check.Equal(21.0, Math.Round(ConfirmationTheme.ContrastRatio(Color.White, Color.Black), 6), "blanc sur noir : 21:1");
        Check.Equal(1.0, ConfirmationTheme.ContrastRatio(Color.Red, Color.Red), "même couleur : 1:1");
        Check.Equal(ConfirmationTheme.ContrastRatio(Color.Navy, Color.Gold), ConfirmationTheme.ContrastRatio(Color.Gold, Color.Navy), "symétrique");
    }

    private static void Text(ConfirmationPalette palette, Color fore, Color back, string what) =>
        Check.True(ConfirmationTheme.ContrastRatio(fore, back) >= 4.5,
            palette.Name + " : " + what + " lisible (" + ConfirmationTheme.ContrastRatio(fore, back).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1)");

    private static void Part(ConfirmationPalette palette, Color fore, Color back, string what) =>
        Check.True(ConfirmationTheme.ContrastRatio(fore, back) >= 3,
            palette.Name + " : " + what + " visible (" + ConfirmationTheme.ContrastRatio(fore, back).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1)");
}
