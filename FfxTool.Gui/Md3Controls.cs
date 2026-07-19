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

    public class Md3Button : Button
    {
        public Md3ButtonVariant Variant = Md3ButtonVariant.Filled;
        public Md3Icons.Icon? Icon = null;

        bool _hovering;

        public Md3Button()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Md3Tokens.LabelLarge;
            Height = 36;
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { _hovering = true; Invalidate(); };
            MouseLeave += (s, e) => { _hovering = false; Invalidate(); };
            ThemeManager.ThemeChanged += Invalidate_;
        }

        void Invalidate_() => Invalidate();

        (Color fill, Color content, bool outlined) Colors()
        {
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
            // fixes that properly.
            g.Clear(Parent?.BackColor ?? ThemeManager.Current.Surface);

            var (fill, content, outlined) = Colors();
            using (var path = PillPath(ClientRectangle))
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

        public Md3Card()
        {
            Padding = new Padding(Md3Tokens.Space4);
            ThemeManager.ThemeChanged += Invalidate_;
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

            using (var path = RoundedRect(bounds, Md3Tokens.CornerMedium))
            using (var fillBrush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(fillBrush, path);
                if (outline)
                {
                    using (var pen = new Pen(ThemeManager.Current.OutlineVariant, 1))
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

    /// <summary>A small colored "chip" used for status labels (e.g. "You have this" / "NOT in your profile") — MD3's assist-chip color logic without a real chip control.</summary>
    public class Md3StatusChip : Label
    {
        public enum Status { Ok, Warning, Error, Neutral }

        public void SetStatus(string text, Status status)
        {
            Text = "  " + text + "  ";
            Font = Md3Tokens.LabelLarge;
            AutoSize = true;
            TextAlign = ContentAlignment.MiddleCenter;
            Padding = new Padding(Md3Tokens.Space2, Md3Tokens.Space1, Md3Tokens.Space2, Md3Tokens.Space1);

            switch (status)
            {
                case Status.Ok:
                    BackColor = ThemeManager.Current.PrimaryContainer;
                    ForeColor = ThemeManager.Current.OnPrimaryContainer;
                    break;
                case Status.Warning:
                    BackColor = ThemeManager.Current.TertiaryContainer;
                    ForeColor = ThemeManager.Current.OnTertiaryContainer;
                    break;
                case Status.Error:
                    BackColor = ThemeManager.Current.ErrorContainer;
                    ForeColor = ThemeManager.Current.OnErrorContainer;
                    break;
                default:
                    BackColor = ThemeManager.Current.SurfaceContainerHigh;
                    ForeColor = ThemeManager.Current.OnSurfaceVariant;
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            // MD3 spec (component-shape mapping): "Chips: small" (8px
            // corner), NOT a full pill — chips and buttons look similar
            // but are spec'd differently. This was previously using a
            // full/pill shape, which is the button shape, not the chip
            // shape. BackColor/ForeColor here are still set once per
            // SetStatus() call rather than read live from ThemeManager on
            // every paint — acceptable for now since SetStatus() is meant
            // to be called again on any state change (including a theme
            // change, if the caller wires that up), but flagging the same
            // staleness pattern found and fixed in Md3Button/Md3Card.
            using (var path = RoundedRect(bounds, Md3Tokens.CornerSmall))
            using (var brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
