using System.Drawing;
using System.Drawing.Drawing2D;

namespace FfxTool.Gui
{
    /// <summary>
    /// A small hand-drawn icon set, since WinForms has no icon-font
    /// support and MD3 leans heavily on iconography (nav items, buttons,
    /// status indicators) that the app had none of before this pass.
    ///
    /// Each icon draws into a given bounds rectangle at whatever size is
    /// requested — these aren't bitmaps, they're vector line/shape
    /// primitives on a conceptual 24x24 grid (MD3's standard icon grid),
    /// scaled to fit. Kept intentionally simple/geometric rather than
    /// detailed, matching MD3's own icon style rather than skeuomorphic
    /// detail.
    /// </summary>
    public static class Md3Icons
    {
        public enum Icon
        {
            FolderOpen, Convert, Settings, Check, Warning,
            Palette, Sun, Moon, EffectList, Plugin, Logo,
        }

        public static void Draw(Graphics g, Icon icon, Rectangle bounds, Color color, float strokeWidth = 1.8f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (icon)
                {
                    case Icon.FolderOpen: DrawFolderOpen(g, bounds, pen, brush); break;
                    case Icon.Convert: DrawConvert(g, bounds, pen); break;
                    case Icon.Settings: DrawSettings(g, bounds, pen, brush); break;
                    case Icon.Check: DrawCheck(g, bounds, pen); break;
                    case Icon.Warning: DrawWarning(g, bounds, pen, brush); break;
                    case Icon.Palette: DrawPalette(g, bounds, pen); break;
                    case Icon.Sun: DrawSun(g, bounds, pen); break;
                    case Icon.Moon: DrawMoon(g, bounds, brush); break;
                    case Icon.EffectList: DrawEffectList(g, bounds, pen); break;
                    case Icon.Plugin: DrawPlugin(g, bounds, pen); break;
                    case Icon.Logo: DrawLogo(g, bounds, pen, brush); break;
                }
            }
        }

        // Maps a 0-24 conceptual grid coordinate into the actual bounds rectangle.
        static PointF P(Rectangle b, float x, float y) => new PointF(b.X + b.Width * (x / 24f), b.Y + b.Height * (y / 24f));

        static void DrawFolderOpen(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            var pts = new[] { P(b, 3, 6), P(b, 9, 6), P(b, 11, 8), P(b, 21, 8), P(b, 21, 18), P(b, 3, 18), P(b, 3, 6) };
            g.DrawLines(pen, pts);
            g.DrawLine(pen, P(b, 3, 18), P(b, 6, 11));
            g.DrawLine(pen, P(b, 6, 11), P(b, 21, 11));
        }

        static void DrawConvert(Graphics g, Rectangle b, Pen pen)
        {
            // two opposing curved arrows — a simple "transform/convert" glyph
            g.DrawArc(pen, P(b, 4, 4).X, P(b, 4, 4).Y, b.Width * 0.55f, b.Height * 0.55f, 90, 200);
            g.DrawLine(pen, P(b, 4, 13), P(b, 7, 16));
            g.DrawLine(pen, P(b, 4, 13), P(b, 8, 11));

            g.DrawArc(pen, P(b, 8, 15).X, P(b, 15, 15).Y, b.Width * 0.55f, b.Height * 0.55f, 270, 200);
            g.DrawLine(pen, P(b, 20, 11), P(b, 17, 8));
            g.DrawLine(pen, P(b, 20, 11), P(b, 16, 13));
        }

        static void DrawSettings(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            var center = P(b, 12, 12);
            float outerR = b.Width * 0.38f;
            float innerR = b.Width * 0.15f;
            g.DrawEllipse(pen, center.X - outerR, center.Y - outerR, outerR * 2, outerR * 2);
            g.FillEllipse(brush, center.X - innerR, center.Y - innerR, innerR * 2, innerR * 2);
            // 6 simple teeth
            for (int i = 0; i < 6; i++)
            {
                double angle = i * (System.Math.PI / 3);
                float x1 = center.X + (float)(System.Math.Cos(angle) * outerR);
                float y1 = center.Y + (float)(System.Math.Sin(angle) * outerR);
                float x2 = center.X + (float)(System.Math.Cos(angle) * (outerR + b.Width * 0.09f));
                float y2 = center.Y + (float)(System.Math.Sin(angle) * (outerR + b.Width * 0.09f));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        static void DrawCheck(Graphics g, Rectangle b, Pen pen)
        {
            g.DrawLines(pen, new[] { P(b, 4, 12), P(b, 10, 18), P(b, 20, 6) });
        }

        static void DrawWarning(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            var pts = new[] { P(b, 12, 3), P(b, 21, 19), P(b, 3, 19), P(b, 12, 3) };
            g.DrawLines(pen, pts);
            g.DrawLine(pen, P(b, 12, 9), P(b, 12, 14));
            g.FillEllipse(brush, P(b, 11.3f, 15.5f).X, P(b, 11.3f, 15.5f).Y, b.Width * 0.06f, b.Height * 0.06f);
        }

        static void DrawPalette(Graphics g, Rectangle b, Pen pen)
        {
            g.DrawArc(pen, P(b, 3, 3).X, P(b, 3, 3).Y, b.Width * 0.75f, b.Height * 0.75f, 20, 320);
            g.DrawEllipse(pen, P(b, 8, 8).X, P(b, 8, 8).Y, b.Width * 0.1f, b.Height * 0.1f);
            g.DrawEllipse(pen, P(b, 14, 8).X, P(b, 14, 8).Y, b.Width * 0.1f, b.Height * 0.1f);
            g.DrawEllipse(pen, P(b, 11, 14).X, P(b, 11, 14).Y, b.Width * 0.1f, b.Height * 0.1f);
        }

        static void DrawSun(Graphics g, Rectangle b, Pen pen)
        {
            var center = P(b, 12, 12);
            float r = b.Width * 0.2f;
            g.DrawEllipse(pen, center.X - r, center.Y - r, r * 2, r * 2);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * (System.Math.PI / 4);
                float x1 = center.X + (float)(System.Math.Cos(angle) * (r + 2));
                float y1 = center.Y + (float)(System.Math.Sin(angle) * (r + 2));
                float x2 = center.X + (float)(System.Math.Cos(angle) * (r + 6));
                float y2 = center.Y + (float)(System.Math.Sin(angle) * (r + 6));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        static void DrawMoon(Graphics g, Rectangle b, Brush brush)
        {
            using (var path = new GraphicsPath())
            {
                var outer = new RectangleF(P(b, 4, 3).X, P(b, 4, 3).Y, b.Width * 0.7f, b.Height * 0.7f);
                var cut = new RectangleF(P(b, 8, 2).X, P(b, 2, 2).Y, b.Width * 0.7f, b.Height * 0.7f);
                path.AddEllipse(outer);
                using (var cutPath = new GraphicsPath())
                {
                    cutPath.AddEllipse(cut);
                    var region = new Region(path);
                    region.Exclude(cutPath);
                    g.FillRegion(brush, region);
                }
            }
        }

        static void DrawEffectList(Graphics g, Rectangle b, Pen pen)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = 6 + i * 6;
                g.DrawLine(pen, P(b, 3, y), P(b, 8, y));
                g.DrawLine(pen, P(b, 11, y), P(b, 21, y));
            }
        }

        static void DrawPlugin(Graphics g, Rectangle b, Pen pen)
        {
            // simple puzzle-piece-ish plug glyph: rectangle with two prongs
            g.DrawRectangle(pen, P(b, 6, 9).X, P(b, 6, 9).Y, b.Width * 0.5f, b.Height * 0.3f);
            g.DrawLine(pen, P(b, 9, 9), P(b, 9, 5));
            g.DrawLine(pen, P(b, 15, 9), P(b, 15, 5));
            g.DrawLine(pen, P(b, 12, 16.2f), P(b, 12, 20));
        }

        static void DrawLogo(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            // Abstract angular mark echoing the design spec's brand
            // personality ("technical precision") — deliberately simple
            // rather than a literal icon-font glyph, since this is the
            // one piece with no Material Symbols equivalent to approximate
            // (it's meant to be a distinct app mark, not a system icon).
            var pts = new[] { P(b, 12, 2), P(b, 21, 8), P(b, 21, 16), P(b, 12, 22), P(b, 3, 16), P(b, 3, 8), P(b, 12, 2) };
            g.DrawLines(pen, pts);
            g.DrawLine(pen, P(b, 12, 2), P(b, 12, 22));
            using (var lightPen = new Pen(((SolidBrush)brush).Color, 1.2f))
                g.DrawLine(lightPen, P(b, 3, 8), P(b, 21, 16));
        }
    }
}
