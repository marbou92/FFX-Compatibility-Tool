using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// Additional MD3 components beyond Md3Controls.cs's button/card/chip,
    /// modeled on the actual component specs at m3.material.io/components
    /// (Switch, Checkbox, and the outlined Dropdown menu shape) — hand-
    /// painted the same way, since WinForms has no MD3 support natively
    /// and no library provides it.
    /// </summary>

    /// <summary>MD3 "Switch" (m3.material.io/components/switch) — a track
    /// + thumb toggle, distinct from a checkbox both visually and in what
    /// it communicates (an immediate on/off state, not a list selection).
    /// Used in ProfileTab in place of a plain CheckBox.</summary>
    public class Md3Switch : CheckBox
    {
        const int TrackWidth = 52;
        const int TrackHeight = 32;
        const int ThumbMargin = 4;

        public Md3Switch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Appearance = Appearance.Button; // suppress the default checkbox glyph, we paint our own
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Md3Tokens.BodyLarge;
            AutoSize = false;
            Height = TrackHeight + 4;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? ThemeManager.Current.Surface);

            var trackRect = new Rectangle(0, 2, TrackWidth, TrackHeight);
            var trackColor = Checked ? ThemeManager.Current.Primary : ThemeManager.Current.SurfaceContainerHigh;
            var outlineColor = Checked ? ThemeManager.Current.Primary : ThemeManager.Current.Outline;

            using (var path = PillPath(trackRect))
            using (var fillBrush = new SolidBrush(trackColor))
            using (var pen = new Pen(outlineColor, 1.5f))
            {
                g.FillPath(fillBrush, path);
                if (!Checked) g.DrawPath(pen, path); // MD3 only outlines the unchecked track
            }

            // thumb: bigger + primary-colored when checked, matching MD3's
            // "thumb grows on check" micro-interaction (static here, no
            // animation, but the size difference alone reads clearly)
            int thumbDiameter = Checked ? TrackHeight - ThumbMargin : (TrackHeight - ThumbMargin) - 8;
            int thumbY = trackRect.Y + (TrackHeight - thumbDiameter) / 2;
            int thumbX = Checked
                ? trackRect.Right - thumbDiameter - (ThumbMargin / 2)
                : trackRect.X + (ThumbMargin / 2) + 4;

            var thumbColor = Checked ? ThemeManager.Current.OnPrimary : ThemeManager.Current.Outline;
            using (var thumbBrush = new SolidBrush(thumbColor))
                g.FillEllipse(thumbBrush, thumbX, thumbY, thumbDiameter, thumbDiameter);

            // label, to the right of the track
            var labelRect = new Rectangle(TrackWidth + Md3Tokens.Space3, 0, Width - TrackWidth - Md3Tokens.Space3, Height);
            TextRenderer.DrawText(g, Text, Font, labelRect, ThemeManager.Current.OnSurface,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
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

    /// <summary>MD3 "Checkbox" (m3.material.io/components/checkbox) — a
    /// rounded-square container with a check glyph, replacing the native
    /// Windows checkbox square. Kept separate from Md3Switch since MD3
    /// itself treats them as distinct components for distinct purposes
    /// (checkbox = multi-select in a list; switch = single on/off state).</summary>
    public class Md3Checkbox : CheckBox
    {
        const int BoxSize = 20;

        public Md3Checkbox()
        {
            SetStyle(ControlStyles.UserPaint, true);
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Md3Tokens.BodyLarge;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? ThemeManager.Current.Surface);

            var boxRect = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);

            using (var path = RoundedRect(boxRect, 4))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(ThemeManager.Current.Primary))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(ThemeManager.Current.Outline, 1.5f))
                        g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(ThemeManager.Current.OnPrimary, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    var p1 = new Point(boxRect.X + 4, boxRect.Y + 10);
                    var p2 = new Point(boxRect.X + 8, boxRect.Y + 14);
                    var p3 = new Point(boxRect.X + 16, boxRect.Y + 5);
                    g.DrawLines(pen, new[] { p1, p2, p3 });
                }
            }

            var labelRect = new Rectangle(BoxSize + Md3Tokens.Space2, 0, Width - BoxSize - Md3Tokens.Space2, Height);
            TextRenderer.DrawText(g, Text, Font, labelRect, ThemeManager.Current.OnSurface,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
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

    /// <summary>MD3 "Outlined Dropdown menu" shape
    /// (m3.material.io/components/menus#dropdown-menus) applied to a
    /// standard WinForms ComboBox via owner-draw: rounded outline, MD3
    /// color tokens, no native Windows combobox chrome.</summary>
    public class Md3ComboBox : ComboBox
    {
        public Md3ComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            Font = Md3Tokens.BodyLarge;
            ItemHeight = 24;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var bg = selected ? ThemeManager.Current.PrimaryContainer : ThemeManager.Current.Surface;
            var fg = selected ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurface;

            using (var brush = new SolidBrush(bg))
                e.Graphics.FillRectangle(brush, e.Bounds);

            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Owner-drawing the closed-state box too (not just the
            // dropdown list) — otherwise the box itself stays native
            // Windows chrome while only the opened list gets MD3 styling,
            // which looks inconsistent.
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = RoundedRect(bounds, Md3Tokens.CornerSmall))
            using (var fillBrush = new SolidBrush(ThemeManager.Current.Surface))
            using (var pen = new Pen(ThemeManager.Current.Outline, 1f))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(pen, path);
            }

            if (SelectedIndex >= 0)
            {
                var textRect = new Rectangle(Md3Tokens.Space3, 0, Width - Md3Tokens.Space6 - 16, Height);
                TextRenderer.DrawText(g, Items[SelectedIndex].ToString(), Font, textRect, ThemeManager.Current.OnSurface,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }

            // simple downward-caret glyph instead of the native combo arrow
            var caretCenter = new Point(Width - 16, Height / 2);
            using (var pen = new Pen(ThemeManager.Current.OnSurfaceVariant, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, caretCenter.X - 4, caretCenter.Y - 2, caretCenter.X, caretCenter.Y + 2);
                g.DrawLine(pen, caretCenter.X, caretCenter.Y + 2, caretCenter.X + 4, caretCenter.Y - 2);
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
