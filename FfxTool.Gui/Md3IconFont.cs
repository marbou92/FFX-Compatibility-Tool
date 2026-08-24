using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// Loads Google's real Material Symbols Outlined font — the exact
    /// icon font your design's `code.html` uses (`material-symbols-outlined`
    /// spans with ligature text like "folder_open", "settings", etc.) —
    /// with a graceful fallback to the hand-drawn vector icons in
    /// Md3Icons.cs if the font file isn't bundled.
    ///
    /// SAME SANDBOX LIMITATION AS Md3Fonts.cs (Inter, Phase 1): this
    /// environment's network access is locked to package registries
    /// (npm, PyPI, GitHub), not font CDNs — fonts.google.com and
    /// fonts.gstatic.com aren't reachable from here, so the actual font
    /// binary couldn't be fetched automatically.
    ///
    /// Material Symbols is free/open-source (Apache 2.0). Get it
    /// yourself from https://fonts.google.com/icons (download the
    /// "Outlined" static .ttf) and drop it at:
    ///   assets/fonts/MaterialSymbolsOutlined.ttf
    /// (add it to FfxTool.Gui.csproj as a CopyToOutputDirectory item,
    /// same as the Inter files from Phase 1).
    ///
    /// HOW THIS FONT ACTUALLY WORKS, for anyone maintaining this later:
    /// Material Symbols isn't a normal icon font with one glyph per
    /// Unicode codepoint — it uses OpenType ligature substitution, so
    /// drawing the literal ASCII string "settings" renders as the gear
    /// icon glyph, not the 8 letters s-e-t-t-i-n-g-s. That's why
    /// DrawLigature() below just calls TextRenderer.DrawText with the
    /// icon's name as the string — that IS the correct usage, not a
    /// placeholder/bug.
    /// </summary>
    public static class Md3IconFont
    {
        static readonly PrivateFontCollection _collection = new PrivateFontCollection();
        static bool _loaded, _available;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts", "MaterialSymbolsOutlined.ttf");
                if (File.Exists(path))
                {
                    _collection.AddFontFile(path);
                    _available = _collection.Families.Length > 0;
                }
            }
            catch (Exception) { _available = false; }
        }

        public static bool IsAvailable { get { EnsureLoaded(); return _available; } }

        // Icon sizes repeat heavily (24px nav icons, 18/20/22px buttons...),
        // but bounds vary slightly; cache one Font per rounded pixel size
        // instead of allocating a new GDI+ Font on every icon draw.
        static readonly System.Collections.Generic.Dictionary<int, Font> _fontCache =
            new System.Collections.Generic.Dictionary<int, Font>();

        /// <summary>
        /// Draw a Material Symbols glyph by its real ligature name (e.g.
        /// "folder_open", "settings", "swap_horiz"). Returns false if the
        /// font isn't bundled, so callers can fall back to a hand-drawn
        /// icon — see Md3Icons.Draw() for that fallback wiring.
        /// </summary>
        public static bool TryDraw(Graphics g, string ligatureName, Rectangle bounds, Color color)
        {
            EnsureLoaded();
            if (!_available) return false;

            int px = Math.Max(6, (int)Math.Round(bounds.Height * 0.85f));
            Font font;
            lock (_fontCache)
            {
                if (!_fontCache.TryGetValue(px, out font))
                {
                    font = new Font(_collection.Families[0], px, FontStyle.Regular, GraphicsUnit.Pixel);
                    _fontCache[px] = font;
                }
            }

            TextRenderer.DrawText(g, ligatureName, font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return true;
        }
    }
}
