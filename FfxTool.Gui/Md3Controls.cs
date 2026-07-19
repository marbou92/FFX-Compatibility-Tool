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
    public class Md3Button : Button
    {
        public Md3Button()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = ThemeManager.Current.Primary;
            ForeColor = ThemeManager.Current.OnPrimary;
            Font = Md3Tokens.LabelLarge;
            Height = 36;
            Cursor = Cursors.Hand;
            FlatAppearance.MouseOverBackColor = ControlPaint.Light(ThemeManager.Current.Primary, 0.1f);
            FlatAppearance.MouseDownBackColor = ControlPaint.Dark(ThemeManager.Current.Primary, 0.05f);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // MD3 spec (component-shape mapping): "Buttons (all types): full"
            // — a true pill/stadium shape, not a fixed corner radius. This
            // was previously using Md3Tokens.CornerLarge (16px fixed),
            // which is spec-incorrect (that value is for FABs/nav drawers).
            using (var path = PillPath(ClientRectangle))
            using (var brush = new SolidBrush(ThemeManager.Current.Primary))
            {
                pevent.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, ThemeManager.Current.OnPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
    public class Md3Card : Panel
    {
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
            using (var path = RoundedRect(bounds, Md3Tokens.CornerMedium))
            using (var fillBrush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
            using (var pen = new Pen(ThemeManager.Current.OutlineVariant, 1))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(pen, path);
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
