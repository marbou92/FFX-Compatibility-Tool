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
            // Both Md3Switch and Md3Checkbox live inside an Md3Card in every
            // real usage in this app, and Md3Card no longer keeps its
            // BackColor property in sync with the live theme (it paints
            // SurfaceContainer directly in its own OnPaint instead) —
            // reading Parent.BackColor here was picking up a stale/wrong
            // color, which caused the switch track to visibly mismatch
            // its card and overlap/obscure the label text next to it.
            // Reading the theme's SurfaceContainer directly instead is
            // correct for this app's actual layout, not just a workaround.
            g.Clear(ThemeManager.Current.SurfaceContainer);

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
            // Both Md3Switch and Md3Checkbox live inside an Md3Card in every
            // real usage in this app, and Md3Card no longer keeps its
            // BackColor property in sync with the live theme (it paints
            // SurfaceContainer directly in its own OnPaint instead) —
            // reading Parent.BackColor here was picking up a stale/wrong
            // color, which caused the switch track to visibly mismatch
            // its card and overlap/obscure the label text next to it.
            // Reading the theme's SurfaceContainer directly instead is
            // correct for this app's actual layout, not just a workaround.
            g.Clear(ThemeManager.Current.SurfaceContainer);

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

}
