using System.Drawing;

namespace Filpro.App;

/// <summary>
/// Couleurs FILPRO de la fenêtre « FILPRO — Confirmation Windows », reprises des jetons de la page
/// (wad\web\css\tokens.css). Toutes opaques.
/// </summary>
internal sealed record ConfirmationPalette(
    string Name,
    bool Dark,
    // Surface de la fenêtre et valeurs en lecture seule (--bg-elevated, --bg).
    Color Window,
    Color Field,
    // Contour des valeurs et du bouton secondaire (--border), contour des cases à cocher (--border-strong).
    Color Border,
    Color Control,
    Color Text,
    Color Muted,
    // Case cochée (--accent) et sa coche ; anneau du focus ; survol du bouton secondaire (--bg-hover).
    Color Accent,
    Color OnAccent,
    Color Focus,
    Color Hover,
    // Bouton d'accord armé : partie visible au repos du dégradé de marque des boutons principaux de la page.
    Color PrimaryStart,
    Color PrimaryEnd,
    Color OnPrimary,
    // Points graves (--alert) et motif d'un bouton d'accord désactivé (--warn).
    Color Alert,
    Color Warn,
    // Filet de marque en haut de la fenêtre (--brand-gradient).
    Color BrandStart,
    Color BrandMiddle,
    Color BrandEnd);

/// <summary>
/// Choix de la palette de la fenêtre de confirmation. Table fermée compilée dans FILPRO : aucune couleur ne vient de la
/// page, seul le nom du thème est lu dans les réglages de FILPRO. Le contraste élevé de Windows garde les couleurs système.
/// </summary>
internal static class ConfirmationTheme
{
    internal static readonly ConfirmationPalette Light = new(
        "light", false,
        Window: Rgb(0xFFFFFF), Field: Rgb(0xF3F6FB), Border: Rgb(0xD7DEE9), Control: Rgb(0x8392A8),
        Text: Rgb(0x17243A), Muted: Rgb(0x52627B),
        Accent: Rgb(0x066DDB), OnAccent: Rgb(0xFFFFFF), Focus: Rgb(0x066DDB), Hover: Rgb(0xF0F6FF),
        PrimaryStart: Rgb(0x066DDB), PrimaryEnd: Rgb(0x6A5CFF), OnPrimary: Rgb(0xFFFFFF),
        Alert: Rgb(0xB91C1C), Warn: Rgb(0x8A5300),
        BrandStart: Rgb(0x066DDB), BrandMiddle: Rgb(0x6A5CFF), BrandEnd: Rgb(0x19C3B2));

    internal static readonly ConfirmationPalette Dark = new(
        "dark", true,
        Window: Rgb(0x172437), Field: Rgb(0x0D1625), Border: Rgb(0x2B3D57), Control: Rgb(0x64809F),
        Text: Rgb(0xEDF4FF), Muted: Rgb(0xBFCEE2),
        Accent: Rgb(0x2470D6), OnAccent: Rgb(0xFFFFFF), Focus: Rgb(0x8BBEFF), Hover: Rgb(0x1D3050),
        PrimaryStart: Rgb(0x066DDB), PrimaryEnd: Rgb(0x6A5CFF), OnPrimary: Rgb(0xFFFFFF),
        Alert: Rgb(0xFCA5A5), Warn: Rgb(0xFCD34D),
        BrandStart: Rgb(0x066DDB), BrandMiddle: Rgb(0x6A5CFF), BrandEnd: Rgb(0x19C3B2));

    /// <summary>Thème « Contraste élevé » de FILPRO, distinct du contraste élevé de Windows.</summary>
    internal static readonly ConfirmationPalette Contrast = new(
        "contrast", true,
        Window: Rgb(0x000000), Field: Rgb(0x000000), Border: Rgb(0xFFFFFF), Control: Rgb(0xFFFFFF),
        Text: Rgb(0xFFFFFF), Muted: Rgb(0xFFFFFF),
        Accent: Rgb(0x1AEBFF), OnAccent: Rgb(0x000000), Focus: Rgb(0xFFFF00), Hover: Rgb(0x1A1A1A),
        PrimaryStart: Rgb(0x1AEBFF), PrimaryEnd: Rgb(0x1AEBFF), OnPrimary: Rgb(0x000000),
        Alert: Rgb(0xFF6B6B), Warn: Rgb(0xFFFF00),
        BrandStart: Rgb(0x1AEBFF), BrandMiddle: Rgb(0x1AEBFF), BrandEnd: Rgb(0x1AEBFF));

    internal static IReadOnlyList<ConfirmationPalette> All { get; } = [Light, Dark, Contrast];

    /// <summary>
    /// Palette du thème des réglages de FILPRO (« light », « dark », « contrast », « system ») ; « system » ou une valeur
    /// inconnue suivent le mode des applications de Windows. Nulle en contraste élevé de Windows : couleurs système.
    /// </summary>
    internal static ConfirmationPalette? Resolve(string? setting, bool windowsHighContrast, bool windowsAppsDark) =>
        windowsHighContrast ? null : setting switch
        {
            "light" => Light,
            "dark" => Dark,
            "contrast" => Contrast,
            _ => windowsAppsDark ? Dark : Light,
        };

    /// <summary>Mélange de deux couleurs opaques : 0 rend la première, 1 la seconde.</summary>
    internal static Color Blend(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        int Mix(int a, int b) => (int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero);
        return Color.FromArgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    /// <summary>Rapport de contraste WCAG 2 entre deux couleurs opaques, de 1 à 21.</summary>
    internal static double ContrastRatio(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static Color Rgb(int value) => Color.FromArgb(255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
}
