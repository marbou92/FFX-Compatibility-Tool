using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// A small set of custom-painted WinForms controls approximating MD3
    /// components (filled button, outlined card) using Md3Tokens. WinForms
    /// has no native theming hooks for this, so these override OnPaint
    /// directly rather than relying on any external MD3 library — there
    /// isn't one for WinForms.
    /// </summary>
    /// <summary>
    /// MD3's button variants (m3.material.io/components/buttons) — this
    /// was previously a single "filled" style used for every button in
    /// the app, including secondary actions like "Scan a plugins folder"
    /// that MD3 would style differently to establish visual hierarchy
    /// (a screen full of identical filled buttons has no hierarchy at all).
    /// Elevated is intentionally omitted — its whole visual identity is a
    /// drop shadow, and WinForms shadows are unreliable across OS versions
    /// including Win7 (same reasoning Md3Card already used to skip it).
    /// </summary>
    public enum Md3ButtonVariant { Filled, Tonal, Outlined, Text }

    /// <summary>
    /// Plain Panel with double buffering enabled — drop-in replacement for
    /// "new Panel" wherever a custom Paint handler exists, so resize/theme
    /// repaints stop flickering (WinForms Panel doesn't buffer by default).
    /// </summary>
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }

    /// <summary>
    /// The color custom-painted controls must clear their background with so
    /// their rounded corners blend invisibly into whatever surface they sit
    /// on. WinForms has no real transparency for UserPaint controls — the
    /// corners are painted with this color instead. Resolves the actual
    /// painted fill of an Md3Card parent (its ambient BackColor property is
    /// NOT kept in sync with what its OnPaint draws), or the app Surface for
    /// anything sitting directly on a tab/form.
    /// </summary>
    public static class Md3Surface
    {
        public static Color BackingFor(Control parent)
        {
            var card = parent as Md3Card;
            if (card != null)
            {
                switch (card.Variant)
                {
                    case Md3CardVariant.Elevated: return ThemeManager.Current.SurfaceContainerHigh;
                    case Md3CardVariant.Outlined: return ThemeManager.Current.Surface;
                    default: return ThemeManager.Current.SurfaceContainer; // Filled
                }
            }
            return ThemeManager.Current.Surface;
        }

        /// <summary>Blend a color toward the current Surface — used for disabled-state muting.</summary>
        public static Color Mute(Color c, float t)
        {
            var s = ThemeManager.Current.Surface;
            return Color.FromArgb(
                c.A,
                (int)(c.R + (s.R - c.R) * t),
                (int)(c.G + (s.G - c.G) * t),
                (int)(c.B + (s.B - c.B) * t));
        }
    }

    public class Md3Button : Button
    {
        public Md3ButtonVariant Variant = Md3ButtonVariant.Filled;
        public Md3Icons.Icon? Icon = null;

        bool _hovering;
        bool _pressed;

        public Md3Button()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Md3Tokens.LabelLarge;
            Height = 36;
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { _hovering = true; Invalidate(); };
            MouseLeave += (s, e) => { _hovering = false; Invalidate(); };
            // Matches the design spec's "active:scale-95" press feedback —
            // approximated here as a small inset of the drawn pill rather
            // than a true GDI+ scale transform (which would need to scale
            // around the button's center and complicates hit-testing);
            // visually reads the same for a control this size.
            MouseDown += (s, e) => { _pressed = true; Invalidate(); };
            MouseUp += (s, e) => { _pressed = false; Invalidate(); };
            EnabledChanged += (s, e) => Invalidate();
            ThemeManager.ThemeChanged += Invalidate_;
        }

        void Invalidate_() => Invalidate();

        (Color fill, Color content, bool outlined) Colors()
        {
            // Disabled: MD3 mutes both fill and content toward the surface.
            if (!Enabled)
            {
                switch (Variant)
                {
                    case Md3ButtonVariant.Tonal: return (Md3Surface.Mute(ThemeManager.Current.SurfaceContainerHigh, 0.5f), Md3Surface.Mute(ThemeManager.Current.OnSurfaceVariant, 0.5f), false);
                    case Md3ButtonVariant.Outlined:
                    case Md3ButtonVariant.Text: return (Color.Transparent, Md3Surface.Mute(ThemeManager.Current.OnSurfaceVariant, 0.4f), Variant == Md3ButtonVariant.Outlined);
                    default: return (Md3Surface.Mute(ThemeManager.Current.PrimaryContainer, 0.35f), Md3Surface.Mute(ThemeManager.Current.OnPrimaryContainer, 0.45f), false);
                }
            }

            switch (Variant)
            {
                case Md3ButtonVariant.Filled: return (ThemeManager.Current.Primary, ThemeManager.Current.OnPrimary, false);
                case Md3ButtonVariant.Tonal: return (ThemeManager.Current.PrimaryContainer, ThemeManager.Current.OnPrimaryContainer, false);
                case Md3ButtonVariant.Outlined: return (Color.Transparent, ThemeManager.Current.Primary, true);
                case Md3ButtonVariant.Text: return (Color.Transparent, ThemeManager.Current.Primary, false);
                default: return (ThemeManager.Current.Primary, ThemeManager.Current.OnPrimary, false);
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Without ControlStyles.UserPaint, WinForms still paints the
            // native button background underneath our custom pill, which
            // showed through as jagged black corner artifacts in a real
            // screenshot — clearing to the actual parent surface first
            // fixes that properly. The parent's *painted* fill is resolved
            // via Md3Surface.BackingFor (its ambient BackColor property
            // doesn't match what an Md3Card draws), so corners blend into
            // cards of any variant instead of leaving light squares.
            g.Clear(Md3Surface.BackingFor(Parent));

            var (fill, content, outlined) = Colors();
            // ~5% inset on press, approximating the design's scale-95 press feedback
            var drawBounds = _pressed
                ? Rectangle.Inflate(ClientRectangle, -(int)(ClientRectangle.Width * 0.025f), -(int)(ClientRectangle.Height * 0.025f))
                : ClientRectangle;
            using (var path = PillPath(drawBounds))
            {
                if (fill != Color.Transparent)
                {
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                }
                if (outlined)
                {
                    using (var pen = new Pen(ThemeManager.Current.Outline, 1f))
                        g.DrawPath(pen, path);
                }
                if (_hovering)
                {
                    // MD3's real hover mechanism: a semi-transparent overlay
                    // of the content color, not a flat color swap.
                    Md3StateLayer.Paint(g, path, content, Md3Tokens.HoverStateAlpha);
                }
            }

            int textX = ClientRectangle.X;
            int textWidth = ClientRectangle.Width;
            if (Icon.HasValue)
            {
                int iconSize = 18;
                var iconBounds = new Rectangle(ClientRectangle.X + Md3Tokens.Space4, (Height - iconSize) / 2, iconSize, iconSize);
                Md3Icons.Draw(g, Icon.Value, iconBounds, content, 1.8f);
                textX = iconBounds.Right + Md3Tokens.Space2;
                textWidth = ClientRectangle.Width - (textX - ClientRectangle.X) - Md3Tokens.Space4;
            }
            var textRect = new Rectangle(textX, ClientRectangle.Y, textWidth, ClientRectangle.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, content,
                Icon.HasValue
                    ? TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    : TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static GraphicsPath PillPath(Rectangle bounds)
        {
            var path = new GraphicsPath();
            int radius = bounds.Height / 2;
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>An MD3-style "card" surface — rounded panel with a subtle border, no drop shadow (WinForms shadows are unreliable across OS versions including Win7).</summary>
    /// <summary>
    /// MD3's card variants (m3.material.io/components/cards). Previously
    /// every card in the app (Plugin Profile vendor rows, Settings'
    /// Appearance/About) looked identical — one undifferentiated style.
    /// Elevated uses a slightly higher surface tone instead of a drop
    /// shadow (WinForms shadows are unreliable across OS versions
    /// including Win7, so this substitutes MD3's own tonal-elevation
    /// concept rather than skipping elevation differentiation entirely).
    /// </summary>
    public enum Md3CardVariant { Elevated, Filled, Outlined }

    public class Md3Card : Panel
    {
        public Md3CardVariant Variant = Md3CardVariant.Filled;
        bool _hovering;

        public Md3Card()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Padding = new Padding(Md3Tokens.Space4);
            ThemeManager.ThemeChanged += Invalidate_;
            // Matches the design spec's card hover pattern: "border
            // border-transparent hover:border-outline-variant" — a border
            // that's invisible at rest and fades in on hover, rather than
            // always-visible (Outlined variant already always shows a
            // border; this is a distinct interaction on top of Filled/
            // Elevated variants, which otherwise have no border at all).
            MouseEnter += (s, e) => { _hovering = true; Invalidate(); };
            MouseLeave += (s, e) => { _hovering = false; Invalidate(); };
        }

        void Invalidate_() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill;
            bool outline;
            switch (Variant)
            {
                case Md3CardVariant.Elevated: fill = ThemeManager.Current.SurfaceContainerHigh; outline = false; break;
                case Md3CardVariant.Outlined: fill = ThemeManager.Current.Surface; outline = true; break;
                default: fill = ThemeManager.Current.SurfaceContainer; outline = false; break; // Filled
            }

            // expressive Bold: LargeIncreased 20 for cards, XL-inc 32 for heroes (M3 2026)
            using (var path = RoundedRect(bounds, Md3Tokens.CornerLargeIncreased))
            using (var fillBrush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(fillBrush, path);
                if (outline)
                {
                    using (var pen = new Pen(Color.FromArgb(77, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B), 1))
                        e.Graphics.DrawPath(pen, path);
                }
                else if (_hovering)
                {
                    using (var pen = new Pen(Color.FromArgb(120, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B), 1))
                        e.Graphics.DrawPath(pen, path);
                }
                // subtle expressive elevation hint — 1px shadow at 5% (Win7 safe, no DWM)
                if (Variant == Md3CardVariant.Elevated && !_hovering)
                {
                    using (var pen = new Pen(Color.FromArgb(20, 0, 0, 0), 1))
                        e.Graphics.DrawPath(pen, path);
                }
            }
        }

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
