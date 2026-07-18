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
            BackColor = Md3Tokens.Primary;
            ForeColor = Md3Tokens.OnPrimary;
            Font = Md3Tokens.LabelLarge;
            Height = 36;
            Cursor = Cursors.Hand;
            FlatAppearance.MouseOverBackColor = ControlPaint.Light(Md3Tokens.Primary, 0.1f);
            FlatAppearance.MouseDownBackColor = ControlPaint.Dark(Md3Tokens.Primary, 0.05f);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(ClientRectangle, Md3Tokens.CornerLarge))
            using (var brush = new SolidBrush(BackColor))
            {
                pevent.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, ForeColor,
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

    /// <summary>An MD3-style "card" surface — rounded panel with a subtle border, no drop shadow (WinForms shadows are unreliable across OS versions including Win7).</summary>
    public class Md3Card : Panel
    {
        public Md3Card()
        {
            BackColor = Md3Tokens.SurfaceContainer;
            Padding = new Padding(Md3Tokens.Space4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(bounds, Md3Tokens.CornerMedium))
            using (var fillBrush = new SolidBrush(BackColor))
            using (var pen = new Pen(Md3Tokens.OutlineVariant, 1))
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
                    BackColor = Md3Tokens.PrimaryContainer;
                    ForeColor = Md3Tokens.OnPrimaryContainer;
                    break;
                case Status.Warning:
                    BackColor = Md3Tokens.TertiaryContainer;
                    ForeColor = Md3Tokens.OnTertiaryContainer;
                    break;
                case Status.Error:
                    BackColor = Md3Tokens.ErrorContainer;
                    ForeColor = Md3Tokens.OnErrorContainer;
                    break;
                default:
                    BackColor = Md3Tokens.SurfaceContainerHigh;
                    ForeColor = Md3Tokens.OnSurfaceVariant;
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRectFull(bounds))
            using (var brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static GraphicsPath RoundedRectFull(Rectangle bounds)
        {
            // fully rounded ("pill") shape — MD3 chips use full corner radius
            var path = new GraphicsPath();
            int radius = bounds.Height / 2;
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
