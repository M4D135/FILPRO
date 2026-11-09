using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Filpro.Core;

namespace Filpro.App;

/// <summary>
/// Fenêtre « FILPRO — Confirmation Windows » : seule autorité d'une opération privilégiée demandée par la page. Elle
/// appartient au processus élevé de FILPRO (UIPI : un programme non élevé de la session ne peut ni la cliquer, ni lui
/// envoyer de touches, ni l'automatiser). Contrôles WinForms aux couleurs de FILPRO (palette fermée compilée, thème lu dans
/// les réglages de FILPRO), ou aux couleurs système en contraste élevé de Windows ; aucun HTML ni lien, rien qui lance un
/// processus ; chaque texte vient de la fiche construite par l'hôte. Le bouton d'accord ne s'arme qu'après un délai à
/// chaque activation et reste désactivé tant qu'une fenêtre d'un autre programme la recouvre ; toute autre sortie (Annuler,
/// Échap, Entrée ou Espace sur Annuler, Alt+F4, croix, délai écoulé, erreur) vaut refus.
/// </summary>
internal sealed class NativeConfirmationForm : Form
{
    private const string TrustStatement = "Seule cette fenêtre Windows autorise l’opération. Elle ne s’affiche jamais dans la page de FILPRO et ne demande jamais de mot de passe.";
    // Dimensions en pixels logiques (96 ppp), converties à la résolution de la fenêtre à chaque mise en page.
    private const int ContentWidth = 620;
    private const int MinimumContentWidth = 320;
    private const int LabelColumn = 170;
    private const int Edge = 16;
    private const int Spacing = 12;
    private const int RowSpacing = 6;
    private const int Tight = 4;
    // En-tête : logo FILPRO à gauche du titre ; filet de marque en haut (palette FILPRO).
    private const int Logo = 32;
    private const int LogoGap = 12;
    private const int Strip = 3;
    // Cadre arrondi des valeurs (palette FILPRO) : marges intérieures et rayon.
    private const int FieldPadX = 8;
    private const int FieldPadY = 5;
    private const int FieldRadius = 6;
    // Corps du texte en points, comme le corps de la page (14 px) ; titres 20 % plus grands.
    private const float BodyPoints = 10F;
    // Au-delà de cette part de la zone de travail, les faits défilent.
    private const double FactsShare = 0.7;
    // Fenêtres examinées au-dessus de celle-ci ; au-delà, le bouton reste désactivé.
    private const int WindowsAboveLimit = 1024;

    private readonly TimeSpan armingDelay;
    private readonly Rectangle workingArea;
    private readonly Label title = NewLabel("ConfirmationTitle");
    private readonly Label situation = NewLabel("ConfirmationSituation");
    private readonly Panel facts = new() { Name = "ConfirmationFacts", AutoScroll = true };
    private readonly List<(Label Label, TextBox Value, Label Origin)> rows = [];
    private readonly Label attentionTitle = NewLabel("ConfirmationAttentionTitle");
    private readonly Panel attention = new() { Name = "ConfirmationAttention" };
    private readonly List<(Label Line, bool Severe)> attentionLines = [];
    // Autre compte seulement : cochée par défaut, elle verrouille FILPRO dès l'accord, avant le lancement, même s'il échoue.
    private readonly ConfirmationCheckBox lockAfterStart = new()
    {
        Name = "ConfirmationLockAfterStart", Text = "Verrouiller FILPRO après le lancement (suivi seulement)", UseMnemonic = false, Checked = true,
    };
    private readonly bool offerLock;
    private readonly ConfirmationCheckBox detailsToggle = new() { Name = "ConfirmationDetails", Text = "Détails techniques", UseMnemonic = false };
    private readonly TextBox details = NewValue("ConfirmationDetailsText");
    private readonly Label footer = NewLabel("ConfirmationFooter");
    private readonly Label blocked = NewLabel("ConfirmationBlocked");
    private readonly Label countdown = NewLabel("ConfirmationCountdown");
    private readonly ConfirmationButton approve = new(primary: true) { Name = "ConfirmationApprove", UseMnemonic = false, Enabled = false };
    private readonly ConfirmationButton cancel = new(primary: false) { Name = "ConfirmationCancel", Text = "Annuler", UseMnemonic = false, DialogResult = DialogResult.Cancel };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Stopwatch clock = new();
    // Palette FILPRO ; nulle en contraste élevé de Windows (couleurs et rendu système).
    private readonly ConfirmationPalette? palette;
    private readonly Font body;
    private readonly Icon brandIcon = BrandIcon.CreateOwned();
    private Icon? headerIcon;
    private int headerIconSize;
    private Rectangle logoBounds;
    private Font? strong;
    private Font? heading;
    private Font? mono;
    // Police de la fenêtre dont dérivent les polices ci-dessus (recréées après un changement de résolution).
    private (string Name, float Size)? fontsFrom;
    private TimeSpan armedAt;
    private bool active;
    private bool decided;
    // WM_SHOWWINDOW de la fenêtre propriétaire réduite ou restaurée en cours de traitement (voir WndProc).
    private bool followingOwner;
    private (uint Process, string Name)? lastProgram;

    internal NativeConfirmationForm(ConfirmationSheet sheet, string situationLine, bool requestedInForeground, Rectangle workingArea,
        ConfirmationPalette? palette)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(situationLine);
        // Mise à l'échelle avant tout contrôle : la mise en page explicite se fait ensuite en pixels, à la résolution réelle.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        this.workingArea = workingArea;
        this.palette = palette;
        body = new Font(Typefaces.Value.Body, BodyPoints);
        Font = body;
        armingDelay = NativeWindowPolicy.ArmingDelay(requestedInForeground);
        Name = "FilproNativeConfirmation";
        Text = "FILPRO — Confirmation Windows";
        Icon = brandIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // Créée sur l'écran de la fenêtre propriétaire, donc à sa résolution, puis centrée sur elle au chargement.
        Location = workingArea.Location;
        title.Text = Printable(sheet.Title);
        situation.Text = Printable(situationLine);
        foreach (var fact in sheet.Facts)
        {
            var number = rows.Count + 1;
            var label = NewLabel("FactLabel_" + number);
            label.Text = Printable(fact.Label);
            // Une valeur sur plusieurs lignes arrive en lignes déjà rendues visibles, jointes par « \n ».
            var value = NewValue("Fact_" + number);
            value.Text = string.Join(Environment.NewLine, fact.Value.Split('\n').Select(Printable));
            var origin = NewLabel("FactOrigin_" + number);
            origin.Text = NativeWindowPolicy.OriginTag(fact.Origin);
            rows.Add((label, value, origin));
            facts.Controls.AddRange([label, value, origin]);
        }
        foreach (var item in NativeWindowPolicy.Attention(sheet.Attention, requestedInForeground))
        {
            var line = NewLabel("Attention_" + (attentionLines.Count + 1));
            line.Text = Printable(item.Text);
            attentionLines.Add((line, item.Severe));
            attention.Controls.Add(line);
        }
        offerLock = sheet.OfferLockAfterStart;
        lockAfterStart.Visible = offerLock;
        attentionTitle.Text = "Points d’attention";
        details.Text = "Opération : " + Printable(sheet.Operation) + Environment.NewLine + "Empreinte : " + Printable(sheet.Digest);
        details.Visible = false;
        footer.Text = TrustStatement;
        countdown.Text = NativeWindowPolicy.CountdownText(NativeWindowPolicy.ResponseTimeout);
        approve.Text = Printable(sheet.ApproveLabel);
        approve.Click += (_, _) => Guarded(() =>
        {
            // Revérifié au clic : fenêtre active, délai écoulé, aucune fenêtre d'un autre programme par-dessus.
            if (RefreshApproval()) Finish(ConfirmationOutcome.Approved);
        });
        detailsToggle.CheckedChanged += (_, _) => Guarded(() =>
        {
            details.Visible = detailsToggle.Checked;
            ArrangeLayout();
        });
        timer.Tick += (_, _) => Guarded(Tick);
        facts.Paint += (_, e) => Guarded(() => PaintFields(e.Graphics, rows.Select(row => row.Value)));
        // Barres de défilement sombres avec une palette sombre (Windows 10 1809 et suivants ; ignoré ailleurs).
        facts.HandleCreated += (_, _) =>
        {
            if (this.palette is { Dark: true }) _ = Native.SetWindowTheme(facts.Handle, "DarkMode_Explorer", null);
        };
        // Aucun bouton d'acceptation : Entrée et Espace n'agissent que sur le bouton qui a le focus, Annuler au départ.
        CancelButton = cancel;
        Controls.AddRange([title, situation, facts, attentionTitle, attention, lockAfterStart, detailsToggle, details, footer, blocked, countdown, approve, cancel]);
        ApplyPalette();
        ActiveControl = cancel;
    }

    /// <summary>Décision prise ; refus tant que rien d'autre n'a été décidé.</summary>
    internal ConfirmationOutcome Outcome { get; private set; } = ConfirmationOutcome.Refused;

    /// <summary>Case « Verrouiller FILPRO après le lancement » proposée par la fiche et cochée au moment de la décision.</summary>
    internal bool LockAfterStart => offerLock && lockAfterStart.Checked;

    /// <summary>Refus venu de FILPRO (fermeture, moteur d'affichage en échec) : la fenêtre se ferme sans accord.</summary>
    internal void Refuse()
    {
        if (IsDisposed) return;
        if (!decided) Finish(ConfirmationOutcome.Refused);
        // Décision figée sans fermeture (fenêtre propriétaire fermée) : le résultat termine la boucle modale.
        else if (Outcome != ConfirmationOutcome.Approved && Visible && DialogResult == DialogResult.None) DialogResult = DialogResult.Cancel;
    }

    protected override void OnLoad(EventArgs e)
    {
        // Avant Form.OnLoad, qui centre la fenêtre sur sa propriétaire d'après sa taille.
        Guarded(ArrangeLayout);
        // Refus décidé avant l'affichage : ShowDialog a remis le résultat à zéro.
        if (decided) DialogResult = Outcome == ConfirmationOutcome.Approved ? DialogResult.OK : DialogResult.Cancel;
        clock.Start();
        timer.Start();
        base.OnLoad(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // Chaque activation relance le délai : le clic qui active la fenêtre n'accorde jamais.
        active = true;
        armedAt = clock.Elapsed + armingDelay;
        Guarded(() => RefreshApproval());
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        active = false;
        approve.Enabled = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        // Fermeture annulée, ou demandée à la fenêtre propriétaire (WM_CLOSE, fin de session), qui peut encore l'annuler : la
        // fenêtre reste ouverte et la décision en attente, avec son délai et les refus venus de FILPRO.
        if (e.Cancel || e.CloseReason == CloseReason.FormOwnerClosing) return;
        // Annuler, Échap, Alt+F4, croix ou fermeture de session : refus.
        if (!decided)
        {
            decided = true;
            Outcome = ConfirmationOutcome.Refused;
        }
        timer.Stop();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Fermée par n'importe quel chemin, fenêtre propriétaire comprise : plus aucune décision.
        decided = true;
        timer.Stop();
        approve.Enabled = false;
        base.OnFormClosed(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Après la mise à l'échelle de WinForms : textes remesurés à la nouvelle résolution.
        if (IsHandleCreated) BeginInvoke(() => Guarded(ArrangeLayout));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (palette is not { } colors) return;
        // Barre de titre accordée à la palette : mode sombre (Windows 10 20H1 et suivants), couleurs de la légende, du texte et
        // du contour (Windows 11). Un attribut refusé laisse la barre système.
        var dark = colors.Dark ? 1 : 0;
        _ = Native.DwmSetWindowAttribute(Handle, Native.ImmersiveDarkMode, ref dark, sizeof(int));
        var caption = ColorRef(colors.Window);
        _ = Native.DwmSetWindowAttribute(Handle, Native.CaptionColor, ref caption, sizeof(int));
        var text = ColorRef(colors.Text);
        _ = Native.DwmSetWindowAttribute(Handle, Native.TextColor, ref text, sizeof(int));
        var border = ColorRef(colors.Border);
        _ = Native.DwmSetWindowAttribute(Handle, Native.BorderColor, ref border, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Guarded(() =>
        {
            var g = e.Graphics;
            var stripHeight = Px(Strip);
            if (palette is { } colors && ClientSize.Width >= 2 && stripHeight > 0)
            {
                var band = new Rectangle(0, 0, ClientSize.Width, stripHeight);
                using var brand = new LinearGradientBrush(band, colors.BrandStart, colors.BrandEnd, LinearGradientMode.Horizontal)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = [colors.BrandStart, colors.BrandMiddle, colors.BrandEnd],
                        Positions = [0F, 0.55F, 1F],
                    },
                };
                g.FillRectangle(brand, band);
            }
            if (!logoBounds.IsEmpty) g.DrawIcon(HeaderIcon(), logoBounds);
            // Visible lit l'état réel ici : la fenêtre est affichée quand elle se peint.
            if (details.Visible) PaintFields(g, [details]);
        });
    }

    protected override void WndProc(ref Message m)
    {
        // Fenêtre propriétaire réduite ou restaurée : Windows cache ou remontre cette fenêtre (SW_PARENTCLOSING /
        // SW_PARENTOPENING, puis le WM_SHOWWINDOW(…, 0) imbriqué envoyé par ShowWindow). WinForms n'en est pas informé : il la
        // croirait fermée et ShowDialog rendrait un refus. La boucle modale et la décision restent en attente ; le délai de
        // 2 minutes s'applique toujours. Cachée, la fenêtre perd l'activation, donc son bouton d'accord ; la prochaine
        // activation relance le délai d'armement.
        if (m.Msg == Native.ShowWindowMessage && (followingOwner || m.LParam != IntPtr.Zero))
        {
            var outer = followingOwner;
            followingOwner = true;
            try { DefWndProc(ref m); }
            finally { followingOwner = outer; }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            timer.Dispose();
            strong?.Dispose();
            heading?.Dispose();
            mono?.Dispose();
            strong = heading = mono = null;
            body.Dispose();
            headerIcon?.Dispose();
            headerIcon = null;
            brandIcon.Dispose();
        }
    }

    private void Tick()
    {
        var remaining = NativeWindowPolicy.ResponseTimeout - clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            Finish(ConfirmationOutcome.TimedOut);
            return;
        }
        SetText(countdown, NativeWindowPolicy.CountdownText(remaining));
        RefreshApproval();
    }

    /// <summary>Arme ou désarme le bouton d'accord ; vrai seulement s'il peut accorder maintenant.</summary>
    private bool RefreshApproval()
    {
        var program = ObscuringProgram();
        // Une fenêtre étrangère par-dessus désarme le bouton ; après son départ, le délai d'armement reprend.
        if (program is not null) armedAt = clock.Elapsed + armingDelay;
        SetText(blocked, program is null ? "" : NativeWindowPolicy.ObscuredText(program));
        var armed = NativeWindowPolicy.CanApprove(clock.Elapsed, armedAt, active, program is not null, decided);
        if (approve.Enabled != armed) approve.Enabled = armed;
        return armed;
    }

    private void Finish(ConfirmationOutcome outcome)
    {
        if (decided) return;
        decided = true;
        Outcome = outcome;
        timer.Stop();
        approve.Enabled = false;
        DialogResult = outcome == ConfirmationOutcome.Approved ? DialogResult.OK : DialogResult.Cancel;
    }

    /// <summary>Toute erreur de la fenêtre vaut refus : elle est tracée et rien n'est lancé.</summary>
    private void Guarded(Action action)
    {
        try { action(); }
#pragma warning disable CA1031 // Une exception sur le fil de l'interface fermerait FILPRO ; ici elle refuse l'opération.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TechnicalLog.Instance.Error("Fenêtre de confirmation Windows en échec : opération refusée.", ex);
            Finish(ConfirmationOutcome.Refused);
        }
    }

    /// <summary>
    /// Nom du programme dont une fenêtre visible, non masquée par DWM et non réduite recouvre celle-ci depuis plus haut dans
    /// l'ordre Z ; nul si aucune. Les fenêtres de FILPRO sont ignorées ; un parcours trop long compte comme recouvrement.
    /// </summary>
    private string? ObscuringProgram()
    {
        if (!IsHandleCreated) return null;
        var own = FrameOf(Handle);
        var self = (uint)Environment.ProcessId;
        var window = Handle;
        for (var examined = 0; examined < WindowsAboveLimit; examined++)
        {
            window = Native.GetWindow(window, Native.PreviousWindow);
            if (window == IntPtr.Zero) return null;
            if (!Native.IsWindowVisible(window) || Native.IsIconic(window) || IsCloaked(window)) continue;
            if (Native.GetWindowThreadProcessId(window, out var process) == 0 || process == self) continue;
            if (NativeWindowPolicy.Intersects(own, FrameOf(window))) return ProcessNameOf(process);
        }
        return "";
    }

    private string ProcessNameOf(uint process)
    {
        if (lastProgram is { } known && known.Process == process) return known.Name;
        string name;
        try
        {
            using var other = Process.GetProcessById((int)process);
            name = other.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { name = ""; }
        lastProgram = (process, name);
        return name;
    }

    /// <summary>Cadre visible d'une fenêtre (sans les bordures invisibles de redimensionnement), en pixels physiques.</summary>
    private static ScreenRect FrameOf(IntPtr window)
    {
        if (Native.DwmGetWindowAttribute(window, Native.ExtendedFrameBounds, out Native.Rect frame, Marshal.SizeOf<Native.Rect>()) != 0
            && !Native.GetWindowRect(window, out frame)) return default;
        return new ScreenRect(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    private static bool IsCloaked(IntPtr window) =>
        Native.DwmGetWindowAttribute(window, Native.Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>
    /// Mise en page explicite à la résolution de la fenêtre : largeur fixe, hauteurs mesurées sur les textes, faits qui
    /// défilent au-delà de la part de la zone de travail qui leur est réservée.
    /// </summary>
    private void ArrangeLayout()
    {
        ApplyFonts();
        SuspendLayout();
        try
        {
            // Affichée (détails techniques, changement de résolution après un déplacement) : zone de travail de l'écran où elle
            // se trouve. Au chargement, encore cachée : celle de la fenêtre propriétaire, sur laquelle Form.OnLoad la centre.
            var shown = Visible;
            var area = shown ? Screen.FromHandle(Handle).WorkingArea : workingArea;
            var frame = Size - ClientSize;
            var width = Math.Clamp(area.Width - frame.Width - 2 * Px(Edge), Px(MinimumContentWidth), Px(ContentWidth));
            var content = ArrangeFacts(width);
            var limit = Math.Max(Px(96), Math.Min((int)(area.Height * FactsShare), area.Height - frame.Height - Stack(width, 0)));
            if (content > limit)
            {
                ArrangeFacts(width - SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi));
                content = limit;
            }
            ClientSize = new Size(width + 2 * Px(Edge), Stack(width, content));
            if (shown)
            {
                // Agrandie sur place, elle pourrait passer sous la barre des tâches, qui désactiverait le bouton d'accord : elle
                // est ramenée entière dans la zone de travail. Max et Min plutôt que Math.Clamp, qui lèverait une exception (donc
                // un refus) si elle était plus grande que la zone.
                Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
                Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            }
        }
        finally { ResumeLayout(); }
        // Logo, filet et cadres des valeurs sont peints par la fenêtre et par le panneau des faits.
        Invalidate();
        facts.Invalidate();
    }

    /// <summary>Empile les blocs de haut en bas ; rend la hauteur cliente nécessaire.</summary>
    private int Stack(int width, int factsHeight)
    {
        var edge = Px(Edge);
        var y = edge;
        void Place(Control control, int height, int after)
        {
            control.SetBounds(edge, y, width, height);
            y += height + after;
        }
        // En-tête : logo FILPRO, titre et situation à sa droite.
        var logo = Px(Logo);
        var textLeft = edge + logo + Px(LogoGap);
        var textWidth = Math.Max(1, width - logo - Px(LogoGap));
        title.SetBounds(textLeft, y, textWidth, HeightOf(title, textWidth));
        situation.SetBounds(textLeft, title.Bottom + Px(Tight), textWidth, HeightOf(situation, textWidth));
        logoBounds = new Rectangle(edge, y, logo, logo);
        y = Math.Max(situation.Bottom, logoBounds.Bottom) + Px(Spacing);
        facts.Visible = rows.Count > 0;
        if (rows.Count > 0) Place(facts, factsHeight, Px(Spacing));
        attentionTitle.Visible = attention.Visible = attentionLines.Count > 0;
        if (attentionLines.Count > 0)
        {
            Place(attentionTitle, HeightOf(attentionTitle, width), Px(Tight));
            Place(attention, ArrangeAttention(width), Px(Spacing));
        }
        // La fiche fait foi : Visible lit aussi l'état de la fenêtre, encore cachée au chargement.
        if (offerLock)
        {
            int height;
            if (palette is null)
            {
                var text = TextRenderer.MeasureText(lockAfterStart.Text, lockAfterStart.Font, new Size(Math.Max(1, width - Px(24)), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
                height = Math.Max(lockAfterStart.GetPreferredSize(Size.Empty).Height, text + Px(Tight));
            }
            else height = lockAfterStart.Measure(width).Height + Px(Tight);
            Place(lockAfterStart, height, Px(Spacing));
        }
        var toggle = palette is null ? detailsToggle.GetPreferredSize(Size.Empty) : detailsToggle.Measure(width);
        detailsToggle.SetBounds(edge, y, Math.Min(width, toggle.Width), toggle.Height);
        y += toggle.Height + Px(Tight);
        // Visible lit aussi l'état de la fenêtre, encore cachée au chargement : la case fait foi.
        if (detailsToggle.Checked)
        {
            var pad = FieldPadding;
            details.SetBounds(edge + pad.Width, y + pad.Height, Math.Max(1, width - 2 * pad.Width), details.Height);
            details.Height = FittedHeight(details);
            y += details.Height + 2 * pad.Height + Px(Spacing);
        }
        else y += Px(Spacing - Tight);
        Place(footer, HeightOf(footer, width), Px(Tight));
        // Place réservée au motif d'un bouton désactivé : la fenêtre ne change pas de taille quand il apparaît.
        var reserved = TextRenderer.MeasureText(NativeWindowPolicy.ObscuredText(new string('W', 64)), blocked.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
        Place(blocked, reserved, Px(Spacing));
        // Annuler à droite, l'accord à sa gauche, le compte à rebours dans la place restante ou au-dessus.
        var approveSize = ButtonSizeOf(approve);
        var cancelSize = ButtonSizeOf(cancel);
        var buttonHeight = Math.Max(approveSize.Height, cancelSize.Height);
        var countdownHeight = countdown.GetPreferredSize(Size.Empty).Height;
        var room = width - cancelSize.Width - approveSize.Width - Px(8) - Px(Spacing);
        if (countdown.GetPreferredSize(Size.Empty).Width > room) Place(countdown, countdownHeight, Px(Tight));
        else countdown.SetBounds(edge, y + (buttonHeight - countdownHeight) / 2, room, countdownHeight);
        cancel.SetBounds(edge + width - cancelSize.Width, y, cancelSize.Width, buttonHeight);
        approve.SetBounds(cancel.Left - Px(8) - approveSize.Width, y, approveSize.Width, buttonHeight);
        return y + buttonHeight + edge;
    }

    /// <summary>Place les faits sur la largeur donnée (libellé, valeur, origine) ; rend la hauteur de leur contenu.</summary>
    private int ArrangeFacts(int width)
    {
        facts.AutoScrollPosition = Point.Empty;
        var spacing = Px(RowSpacing);
        // Avec la palette FILPRO, chaque valeur est dans un cadre arrondi : libellé et origine alignés sur son texte.
        var pad = FieldPadding;
        var labelWidth = Math.Min(Px(LabelColumn), width / 3);
        var originWidth = rows.Count == 0 ? 0 : rows.Max(row => row.Origin.GetPreferredSize(Size.Empty).Width);
        var valueLeft = labelWidth + spacing;
        var valueWidth = Math.Max(Px(80), width - valueLeft - spacing - originWidth);
        var y = 0;
        foreach (var (label, value, origin) in rows)
        {
            var top = y + pad.Height;
            label.SetBounds(0, top, labelWidth, HeightOf(label, labelWidth));
            value.SetBounds(valueLeft + pad.Width, top, Math.Max(1, valueWidth - 2 * pad.Width), value.Height);
            value.Height = FittedHeight(value);
            origin.SetBounds(valueLeft + valueWidth + spacing, top, originWidth, origin.GetPreferredSize(Size.Empty).Height);
            y += Math.Max(label.Height, Math.Max(value.Height, origin.Height)) + 2 * pad.Height + spacing;
        }
        return Math.Max(0, y - spacing);
    }

    private int ArrangeAttention(int width)
    {
        var y = 0;
        foreach (var (line, _) in attentionLines)
        {
            var height = HeightOf(line, width);
            line.SetBounds(0, y, width, height);
            y += height + Px(RowSpacing);
        }
        return Math.Max(0, y - Px(RowSpacing));
    }

    /// <summary>
    /// Polices dérivées de celle de la fenêtre : titre plus grand et demi-gras, boutons et points d'attention demi-gras,
    /// empreinte à chasse fixe. Points graves à la couleur d'alerte de la palette ; sans palette, en rouge, ou en gras en
    /// contraste élevé (couleurs système seulement).
    /// </summary>
    private void ApplyFonts()
    {
        if (fontsFrom is { } done && done.Size == Font.Size && done.Name == Font.Name) return;
        var previous = (strong, heading, mono);
        var faces = Typefaces.Value;
        strong = faces.Strong is { } semibold ? new Font(semibold, Font.Size) : new Font(Font, FontStyle.Bold);
        heading = faces.Heading is { } display ? new Font(display, Font.Size * 1.2F) : new Font(Font.FontFamily, Font.Size * 1.2F, FontStyle.Bold);
        mono = new Font(faces.Mono, Font.Size * 0.95F);
        fontsFrom = (Font.Name, Font.Size);
        title.Font = heading;
        attentionTitle.Font = strong;
        approve.Font = strong;
        cancel.Font = strong;
        details.Font = mono;
        var highContrast = SystemInformation.HighContrast;
        foreach (var (line, severe) in attentionLines)
        {
            if (!severe) continue;
            if (palette is not null || highContrast) line.Font = strong;
            else line.ForeColor = Color.Firebrick;
        }
        previous.strong?.Dispose();
        previous.heading?.Dispose();
        previous.mono?.Dispose();
    }

    /// <summary>Couleurs FILPRO sur chaque contrôle ; sans palette, rien n'est changé (couleurs système).</summary>
    private void ApplyPalette()
    {
        approve.Palette = cancel.Palette = palette;
        lockAfterStart.Palette = detailsToggle.Palette = palette;
        if (palette is not { } colors) return;
        BackColor = facts.BackColor = attention.BackColor = colors.Window;
        ForeColor = colors.Text;
        situation.ForeColor = footer.ForeColor = countdown.ForeColor = colors.Muted;
        blocked.ForeColor = colors.Warn;
        foreach (var (label, value, origin) in rows)
        {
            label.ForeColor = origin.ForeColor = colors.Muted;
            InField(value, colors);
        }
        InField(details, colors);
        foreach (var (line, severe) in attentionLines) line.ForeColor = severe ? colors.Alert : colors.Text;

        // Le cadre arrondi est peint par le parent autour de la valeur, sans bordure propre.
        static void InField(TextBox box, ConfirmationPalette colors)
        {
            box.BorderStyle = BorderStyle.None;
            box.BackColor = colors.Field;
            box.ForeColor = colors.Text;
        }
    }

    /// <summary>Cadres arrondis des valeurs (palette FILPRO seulement), autour de chaque zone de texte affichée.</summary>
    private void PaintFields(Graphics g, IEnumerable<TextBox> boxes)
    {
        if (palette is not { } colors) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pad = FieldPadding;
        using var fill = new SolidBrush(colors.Field);
        using var border = new Pen(colors.Border, Math.Max(1, Px(1)));
        foreach (var box in boxes)
        {
            var frame = Rectangle.Inflate(box.Bounds, pad.Width, pad.Height);
            frame.Width -= 1;
            frame.Height -= 1;
            if (frame.Width < 2 || frame.Height < 2) continue;
            using var shape = ConfirmationPaint.Rounded(frame, Px(FieldRadius));
            g.FillPath(fill, shape);
            g.DrawPath(border, shape);
        }
    }

    /// <summary>Logo FILPRO de l'en-tête, à la taille de la résolution courante.</summary>
    private Icon HeaderIcon()
    {
        var size = Px(Logo);
        if (headerIcon is { } current && headerIconSize == size) return current;
        // Créée avant de libérer l'ancienne : un échec ne laisse jamais une icône libérée dans le champ.
        var next = new Icon(brandIcon, size, size);
        headerIcon?.Dispose();
        headerIcon = next;
        headerIconSize = size;
        return next;
    }

    private Size FieldPadding => palette is null ? Size.Empty : new Size(Px(FieldPadX), Px(FieldPadY));

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    /// <summary>Hauteur d'une valeur d'après les lignes réellement coupées par le contrôle à sa largeur actuelle.</summary>
    private int FittedHeight(TextBox box)
    {
        var lines = box.TextLength == 0 ? 1 : box.GetLineFromCharIndex(box.TextLength - 1) + 1;
        var line = TextRenderer.MeasureText("Ag", box.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
        return lines * line + (box.Height - box.ClientSize.Height) + Px(Tight);
    }

    private Size ButtonSizeOf(Button button)
    {
        if (palette is null)
        {
            var preferred = button.GetPreferredSize(Size.Empty);
            return new Size(Math.Max(preferred.Width + Px(16), Px(96)), Math.Max(preferred.Height, Px(28)));
        }
        // Corps du bouton comme ceux de la page, plus la marge de l'anneau du focus de chaque côté.
        var text = TextRenderer.MeasureText(button.Text, button.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var ring = 2 * Px(ConfirmationButton.RingMargin);
        return new Size(Math.Max(text.Width + Px(32), Px(104)) + ring, Math.Max(text.Height + Px(14), Px(34)) + ring);
    }

    private int Px(int logical) => LogicalToDeviceUnits(logical);

    private static int HeightOf(Label label, int width) => label.GetPreferredSize(new Size(width, 0)).Height;

    private static void SetText(Label label, string text)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
    }

    /// <summary>Dernière garde avant l'écran : tout caractère invisible ou de contrôle resté dans un texte est montré.</summary>
    private static string Printable(string text) => NativeWindowPolicy.Visible(text, int.MaxValue);

    private static Label NewLabel(string name) => new() { Name = name, AutoSize = false, UseMnemonic = false };

    private static TextBox NewValue(string name) => new()
    {
        Name = name,
        ReadOnly = true,
        Multiline = true,
        WordWrap = true,
        ScrollBars = ScrollBars.None,
        BorderStyle = BorderStyle.FixedSingle,
    };

    // Polices de la page (Segoe UI Variable) si Windows les fournit, sinon Segoe UI ; Cascadia Mono, sinon Consolas.
    private static readonly Lazy<Typeface> Typefaces = new(Typeface.Find);

    private sealed record Typeface(string Body, string? Strong, string? Heading, string Mono)
    {
        internal static Typeface Find()
        {
            HashSet<string> names;
            try
            {
                using var installed = new InstalledFontCollection();
                var families = installed.Families;
                names = families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var family in families) family.Dispose();
            }
#pragma warning disable CA1031 // Polices illisibles (GDI+ épuisé, police abîmée) : police système, la fenêtre s'ouvre quand même.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TechnicalLog.Instance.Warning("Polices de la fenêtre de confirmation illisibles (" + ex.GetType().Name + ") : police système.");
                names = [];
            }
            string? First(params string[] candidates) => candidates.FirstOrDefault(names.Contains);
            return new Typeface(
                First("Segoe UI Variable Text", "Segoe UI") ?? Control.DefaultFont.Name,
                First("Segoe UI Variable Text Semibold", "Segoe UI Semibold"),
                First("Segoe UI Variable Display Semibold", "Segoe UI Semibold"),
                First("Cascadia Mono", "Consolas", "Courier New") ?? Control.DefaultFont.Name);
        }
    }

    private static class Native
    {
        // WM_SHOWWINDOW : fenêtre sur le point d'être cachée ou montrée.
        internal const int ShowWindowMessage = 0x0018;
        // GW_HWNDPREV : fenêtre juste au-dessus dans l'ordre Z.
        internal const uint PreviousWindow = 3;
        // DWMWA_EXTENDED_FRAME_BOUNDS : cadre visible ; DWMWA_CLOAKED : fenêtre masquée par DWM (autre bureau virtuel…).
        internal const int ExtendedFrameBounds = 9;
        internal const int Cloaked = 14;
        // DWMWA_USE_IMMERSIVE_DARK_MODE, DWMWA_BORDER_COLOR, DWMWA_CAPTION_COLOR, DWMWA_TEXT_COLOR (COLORREF 0x00BBGGRR).
        internal const int ImmersiveDarkMode = 20;
        internal const int BorderColor = 34;
        internal const int CaptionColor = 35;
        internal const int TextColor = 36;

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

        // RECT.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    }
}
