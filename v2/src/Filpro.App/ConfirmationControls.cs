using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Filpro.App;

/// <summary>
/// Bouton de la fenêtre de confirmation aux couleurs de FILPRO : dégradé de marque pour l'accord armé, gris lisible tant
/// qu'il est désactivé, contour pour Annuler, anneau visible dès qu'il a le focus (Entrée et Espace agissent sur lui).
/// Sans palette (contraste élevé de Windows), rendu système. Reste un bouton Windows : clavier, UI Automation et UIPI
/// inchangés.
/// </summary>
internal sealed class ConfirmationButton : Button
{
    // Marge de l'anneau du focus autour du corps du bouton, en pixels logiques.
    internal const int RingMargin = 3;
    private const int Radius = 8;
    private bool hovered;
    private bool pressed;

    internal ConfirmationButton(bool primary)
    {
        Primary = primary;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    internal bool Primary { get; }

    // Posée par la fenêtre à sa construction ; jamais sérialisée par le concepteur.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ConfirmationPalette? Palette { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left) pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) hovered = pressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (Palette is not { } palette)
        {
            base.OnPaint(pevent);
            return;
        }
        var g = pevent.Graphics;
        g.Clear(palette.Window);
        var ring = LogicalToDeviceUnits(RingMargin);
        var body = new Rectangle(ring, ring, Width - 2 * ring - 1, Height - 2 * ring - 1);
        if (body.Width < 2 || body.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = LogicalToDeviceUnits(Radius);
        using (var shape = ConfirmationPaint.Rounded(body, radius))
        {
            Color text;
            if (Primary && Enabled)
            {
                var darken = pressed ? 0.18 : hovered ? 0.08 : 0;
                var start = ConfirmationTheme.Blend(palette.PrimaryStart, Color.Black, darken);
                var end = ConfirmationTheme.Blend(palette.PrimaryEnd, Color.Black, darken);
                using var fill = new LinearGradientBrush(body, start, end, LinearGradientMode.Horizontal);
                g.FillPath(fill, shape);
                text = palette.OnPrimary;
            }
            else
            {
                // Accord désactivé : gris lisible, pour qu'on lise ce qui s'armera ; Annuler : contour.
                var back = Primary ? palette.Field : pressed ? ConfirmationTheme.Blend(palette.Hover, palette.Text, 0.06)
                    : hovered && Enabled ? palette.Hover : palette.Window;
                using var fill = new SolidBrush(back);
                // Contour d'Annuler au contraste des contrôles (3:1), visible même quand le focus est ailleurs.
                using var border = new Pen(Primary ? palette.Border : palette.Control, Math.Max(1, LogicalToDeviceUnits(1)));
                g.FillPath(fill, shape);
                g.DrawPath(border, shape);
                text = Enabled && !Primary ? palette.Text : palette.Muted;
            }
            TextRenderer.DrawText(g, Text, Font, body, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (Focused)
        {
            var width = Math.Max(2, LogicalToDeviceUnits(2));
            var outline = Rectangle.Inflate(body, ring - width / 2 - 1, ring - width / 2 - 1);
            using var focus = ConfirmationPaint.Rounded(outline, radius + ring - 1);
            using var pen = new Pen(palette.Focus, width);
            g.DrawPath(pen, focus);
        }
    }
}

/// <summary>
/// Case à cocher de la fenêtre de confirmation aux couleurs de FILPRO (case arrondie, coche sur l'accent). Sans palette,
/// rendu système. Reste une case Windows : Espace, UI Automation et UIPI inchangés.
/// </summary>
internal sealed class ConfirmationCheckBox : CheckBox
{
    // Largeur réservée à la case et à son espacement avant le texte, en pixels logiques.
    internal const int TextIndent = 28;
    private const int Box = 16;
    private const int BoxLeft = 3;

    internal ConfirmationCheckBox() =>
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

    // Posée par la fenêtre à sa construction ; jamais sérialisée par le concepteur.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ConfirmationPalette? Palette { get; set; }

    /// <summary>Taille de la case et de son texte sur la largeur donnée (texte coupé en lignes).</summary>
    internal Size Measure(int width)
    {
        var indent = LogicalToDeviceUnits(TextIndent);
        var text = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - indent), int.MaxValue), TextFlags);
        return new Size(Math.Min(width, indent + text.Width + LogicalToDeviceUnits(2)),
            Math.Max(text.Height, LogicalToDeviceUnits(Box + 2 * BoxLeft)));
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (Palette is not { } palette)
        {
            base.OnPaint(pevent);
            return;
        }
        var g = pevent.Graphics;
        g.Clear(palette.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = LogicalToDeviceUnits(Box);
        var left = LogicalToDeviceUnits(BoxLeft);
        // Case alignée sur la première ligne du texte.
        var line = TextRenderer.MeasureText("Ag", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
        var box = new Rectangle(left, Math.Max(left, (line - size) / 2), size, size);
        var radius = LogicalToDeviceUnits(4);
        using (var shape = ConfirmationPaint.Rounded(box, radius))
        {
            if (Checked)
            {
                using var fill = new SolidBrush(Enabled ? palette.Accent : ConfirmationTheme.Blend(palette.Accent, palette.Window, 0.5));
                g.FillPath(fill, shape);
                using var mark = new Pen(palette.OnAccent, Math.Max(2, LogicalToDeviceUnits(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(mark,
                [
                    new PointF(box.Left + box.Width * 0.24f, box.Top + box.Height * 0.52f),
                    new PointF(box.Left + box.Width * 0.43f, box.Top + box.Height * 0.71f),
                    new PointF(box.Left + box.Width * 0.77f, box.Top + box.Height * 0.31f),
                ]);
            }
            else
            {
                using var fill = new SolidBrush(palette.Window);
                using var border = new Pen(palette.Control, Math.Max(1, LogicalToDeviceUnits(1)));
                g.FillPath(fill, shape);
                g.DrawPath(border, shape);
            }
        }
        if (Focused && ShowFocusCues)
        {
            var width = Math.Max(2, LogicalToDeviceUnits(2));
            using var ring = ConfirmationPaint.Rounded(Rectangle.Inflate(box, left - width / 2 - 1, left - width / 2 - 1), radius + left - 1);
            using var pen = new Pen(palette.Focus, width);
            g.DrawPath(pen, ring);
        }
        var indent = LogicalToDeviceUnits(TextIndent);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(indent, 0, Math.Max(1, Width - indent), Height),
            Enabled ? palette.Text : palette.Muted, TextFlags);
    }

    private const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
}

/// <summary>Formes communes de la fenêtre de confirmation.</summary>
internal static class ConfirmationPaint
{
    /// <summary>Rectangle aux coins arrondis, rayon borné à la moitié du plus petit côté.</summary>
    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        var r = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        if (r == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var d = 2 * r;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
