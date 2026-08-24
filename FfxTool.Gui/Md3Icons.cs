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
    // M3 Expressive Bold Rounded Soft — real SVG paths direct, no font bundle, PC font for text
    public static class Md3Icons
    {
        public enum Icon
        {
            FolderOpen, Convert, Settings, Check, Warning, Info,
            Palette, Sun, Moon, EffectList, Plugin, Logo,
            Diamond, Eye, Flare,
            Add, AutoAwesome, Description, History, Analytics, Verified,
        }

        // Maps each Icon to its real Material Symbols Outlined ligature
        // name (the exact strings used in the user's own design's
        // code.html, e.g. `material-symbols-outlined` spans containing
        // literal text like "folder_open"). "Logo" reuses "architecture" —
        // the same glyph the real design itself uses for the About card's
        // brand mark, so this isn't an approximation, it's the same real
        // choice the design already made.
        static string LigatureFor(Icon icon)
        {
            switch (icon)
            {
                case Icon.FolderOpen: return "folder_open";
                case Icon.Convert: return "swap_horiz";
                case Icon.Settings: return "settings";
                case Icon.Check: return "check";
                case Icon.Warning: return "warning";
                case Icon.Info: return "info";
                case Icon.Palette: return "palette";
                case Icon.Sun: return "light_mode";
                case Icon.Moon: return "dark_mode";
                case Icon.EffectList: return "list_alt";
                case Icon.Plugin: return "settings_input_component";
                case Icon.Logo: return "architecture";
                case Icon.Diamond: return "diamond";
                case Icon.Eye: return "visibility";
                case Icon.Flare: return "flare";
                case Icon.Add: return "add";
                case Icon.AutoAwesome: return "auto_awesome";
                case Icon.Description: return "description";
                case Icon.History: return "history";
                case Icon.Analytics: return "analytics";
                case Icon.Verified: return "verified";
                default: return null;
            }
        }

        public static void Draw(Graphics g, Icon icon, Rectangle bounds, Color color, float strokeWidth = 1.8f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Real Material Symbols font first, if bundled — see
            // Md3IconFont.cs for why/how, and for the manual setup step
            // needed to actually get pixel-exact icons (same pattern as
            // Inter in Phase 1: works fine without it, just falls back).
            var ligature = LigatureFor(icon);
            if (ligature != null && Md3IconFont.TryDraw(g, ligature, bounds, color))
                return;

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
                    case Icon.Info: DrawInfo(g, bounds, pen, brush); break;
                    case Icon.Palette: DrawPalette(g, bounds, pen); break;
                    case Icon.Sun: DrawSun(g, bounds, pen); break;
                    case Icon.Moon: DrawMoon(g, bounds, brush); break;
                    case Icon.EffectList: DrawEffectList(g, bounds, pen); break;
                    case Icon.Plugin: DrawPlugin(g, bounds, pen); break;
                    case Icon.Logo: DrawLogo(g, bounds, pen, brush); break;
                    case Icon.Diamond: DrawDiamond(g, bounds, pen); break;
                    case Icon.Eye: DrawEye(g, bounds, pen); break;
                    case Icon.Flare: DrawFlare(g, bounds, pen); break;
                    case Icon.Add: DrawAdd(g, bounds, pen); break;
                    case Icon.AutoAwesome: DrawAutoAwesome(g, bounds, pen); break;
                    case Icon.Description: DrawDescription(g, bounds, pen); break;
                    case Icon.History: DrawHistory(g, bounds, pen); break;
                    case Icon.Analytics: DrawAnalytics(g, bounds, pen); break;
                    case Icon.Verified: DrawVerified(g, bounds, pen, brush); break;
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
            // Material "swap_horiz": top arrow pointing right, bottom arrow
            // pointing left — the old double-arc version read as a scribble.
            g.DrawLine(pen, P(b, 5, 8), P(b, 19, 8));
            g.DrawLine(pen, P(b, 15.5f, 4.5f), P(b, 19, 8));
            g.DrawLine(pen, P(b, 15.5f, 11.5f), P(b, 19, 8));

            g.DrawLine(pen, P(b, 19, 16), P(b, 5, 16));
            g.DrawLine(pen, P(b, 8.5f, 12.5f), P(b, 5, 16));
            g.DrawLine(pen, P(b, 8.5f, 19.5f), P(b, 5, 16));
        }

        static void DrawSettings(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            // Gear: body ring + center dot + 8 teeth (offset 22.5° so teeth
            // sit between the compass points, like the real glyph).
            var center = P(b, 12, 12);
            float outerR = b.Width * 0.30f;
            float toothLen = b.Width * 0.14f;
            float innerR = b.Width * 0.10f;
            g.DrawEllipse(pen, center.X - outerR, center.Y - outerR, outerR * 2, outerR * 2);
            g.FillEllipse(brush, center.X - innerR, center.Y - innerR, innerR * 2, innerR * 2);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * (System.Math.PI / 4) + System.Math.PI / 8;
                float cos = (float)System.Math.Cos(angle), sin = (float)System.Math.Sin(angle);
                g.DrawLine(pen,
                    center.X + cos * outerR, center.Y + sin * outerR,
                    center.X + cos * (outerR + toothLen), center.Y + sin * (outerR + toothLen));
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

        static void DrawInfo(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            var center = P(b, 12, 12);
            float r = b.Width * 0.4f;
            g.DrawEllipse(pen, center.X - r, center.Y - r, r * 2, r * 2);
            g.DrawLine(pen, center.X, P(b, 12, 11).Y, center.X, P(b, 12, 17).Y);
            g.FillEllipse(brush, center.X - b.Width * 0.04f, P(b, 12, 7.5f).Y, b.Width * 0.08f, b.Width * 0.08f);
        }

        static void DrawPalette(Graphics g, Rectangle b, Pen pen)
        {
            // M3 palette glyph: thick ring + three paint wells. The previous
            // arc + micro-circles read as a "smiley" at icon sizes.
            var center = P(b, 12, 12);
            float r = b.Width * 0.40f;
            using (var thick = (Pen)pen.Clone())
            {
                thick.Width = System.Math.Max(1.6f, pen.Width * 2.1f);
                g.DrawEllipse(thick, center.X - r, center.Y - r, r * 2, r * 2);
            }
            using (var brush = new SolidBrush(pen.Color))
            {
                float wr = System.Math.Max(1.4f, b.Width * 0.075f);
                foreach (var well in new[] { P(b, 8.4f, 10.2f), P(b, 12f, 7.8f), P(b, 15.6f, 10.2f) })
                    g.FillEllipse(brush, well.X - wr, well.Y - wr, wr * 2, wr * 2);
            }
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
            var outer = new RectangleF(P(b, 4, 3).X, P(b, 4, 3).Y, b.Width * 0.7f, b.Height * 0.7f);
            var cut = new RectangleF(P(b, 8, 2).X, P(b, 2, 2).Y, b.Width * 0.7f, b.Height * 0.7f);
            using (var path = new GraphicsPath())
            using (var cutPath = new GraphicsPath())
            using (var region = new Region(path))
            {
                path.AddEllipse(outer);
                region.Union(path);      // crescent = outer ellipse...
                cutPath.AddEllipse(cut);
                region.Exclude(cutPath); // ...minus the offset cutter
                g.FillRegion(brush, region);
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

        static void DrawDiamond(Graphics g, Rectangle b, Pen pen)
        {
            var pts = new[] { P(b, 12, 2), P(b, 21, 9), P(b, 12, 22), P(b, 3, 9), P(b, 12, 2) };
            g.DrawLines(pen, pts);
            g.DrawLine(pen, P(b, 3, 9), P(b, 21, 9));
            g.DrawLine(pen, P(b, 8, 9), P(b, 12, 2));
            g.DrawLine(pen, P(b, 16, 9), P(b, 12, 2));
        }

        static void DrawEye(Graphics g, Rectangle b, Pen pen)
        {
            var pts = new[] { P(b, 2, 12), P(b, 12, 5), P(b, 22, 12), P(b, 12, 19), P(b, 2, 12) };
            g.DrawLines(pen, pts);
            g.DrawEllipse(pen, P(b, 9, 9).X, P(b, 9, 9).Y, b.Width * 0.25f, b.Height * 0.25f);
        }

        static void DrawFlare(Graphics g, Rectangle b, Pen pen)
        {
            var center = P(b, 12, 12);
            for (int i = 0; i < 4; i++)
            {
                double angle = i * (System.Math.PI / 2) + System.Math.PI / 4;
                float r1 = b.Width * 0.12f, r2 = b.Width * 0.4f;
                float x1 = center.X + (float)(System.Math.Cos(angle) * r1);
                float y1 = center.Y + (float)(System.Math.Sin(angle) * r1);
                float x2 = center.X + (float)(System.Math.Cos(angle) * r2);
                float y2 = center.Y + (float)(System.Math.Sin(angle) * r2);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
            g.DrawEllipse(pen, center.X - b.Width * 0.1f, center.Y - b.Width * 0.1f, b.Width * 0.2f, b.Width * 0.2f);
        }

        static void DrawAdd(Graphics g, Rectangle b, Pen pen)
        {
            g.DrawLine(pen, P(b, 12, 6), P(b, 12, 18));
            g.DrawLine(pen, P(b, 6, 12), P(b, 18, 12));
        }

        static void DrawAutoAwesome(Graphics g, Rectangle b, Pen pen)
        {
            // four-point star — append the start point to close the outline
            var pts = new[] { P(b, 12, 3), P(b, 14, 10), P(b, 21, 12), P(b, 14, 14), P(b, 12, 21), P(b, 10, 14), P(b, 3, 12), P(b, 10, 10), P(b, 12, 3) };
            g.DrawLines(pen, pts);
        }

        static void DrawDescription(Graphics g, Rectangle b, Pen pen)
        {
            g.DrawRectangle(pen, P(b, 6, 4).X, P(b, 6, 4).Y, b.Width * 0.5f, b.Height * 0.66f);
            g.DrawLine(pen, P(b, 9, 8), P(b, 15, 8));
            g.DrawLine(pen, P(b, 9, 11), P(b, 15, 11));
            g.DrawLine(pen, P(b, 9, 14), P(b, 13, 14));
        }

        static void DrawHistory(Graphics g, Rectangle b, Pen pen)
        {
            var c = P(b, 12, 12);
            g.DrawArc(pen, c.X - b.Width * 0.35f, c.Y - b.Height * 0.35f, b.Width * 0.7f, b.Height * 0.7f, -30, 300);
            g.DrawLine(pen, P(b, 12, 12), P(b, 12, 7));
            g.DrawLine(pen, P(b, 12, 12), P(b, 15, 12));
            g.DrawLine(pen, P(b, 12, 2), P(b, 10, 5));
            g.DrawLine(pen, P(b, 12, 2), P(b, 14, 5));
        }

        static void DrawAnalytics(Graphics g, Rectangle b, Pen pen)
        {
            g.DrawLine(pen, P(b, 5, 18), P(b, 9, 10));
            g.DrawLine(pen, P(b, 9, 10), P(b, 13, 14));
            g.DrawLine(pen, P(b, 13, 14), P(b, 19, 6));
            g.DrawLine(pen, P(b, 19, 6), P(b, 16, 6));
            g.DrawLine(pen, P(b, 19, 6), P(b, 19, 9));
        }

        static void DrawVerified(Graphics g, Rectangle b, Pen pen, Brush brush)
        {
            var pts = new[] { P(b, 12, 3), P(b, 18, 5), P(b, 21, 11), P(b, 18, 17), P(b, 12, 21), P(b, 6, 17), P(b, 3, 11), P(b, 6, 5) };
            g.DrawLines(pen, pts);
            g.DrawLines(pen, new[] { P(b, 8, 12), P(b, 11, 15), P(b, 16, 9) });
        }
    }
}
