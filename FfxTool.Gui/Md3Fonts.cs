using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;

namespace FfxTool.Gui
{
    /// <summary>
    /// Loads the Inter font family your design spec specifies for every
    /// text role, with a graceful fallback to Segoe UI if the font files
    /// aren't present.
    ///
    /// IMPORTANT — why this needs manual setup: I could not bundle the
    /// actual Inter .ttf files from the sandbox this project is built in.
    /// Its network access is locked to a handful of package registries
    /// (npm, PyPI, crates.io, GitHub) — not fonts.google.com or any font
    /// CDN — so there was no way to fetch the real font binaries here.
    ///
    /// Inter is free/open-source (SIL Open Font License) — download it
    /// yourself from https://rsms.me/inter/ or
    /// https://fonts.google.com/specimen/Inter and drop these three files
    /// into an `assets/fonts/` folder next to the built .exe (add them to
    /// FfxTool.Gui.csproj as CopyToOutputDirectory items, the same way
    /// data/plugin_table.json is already handled):
    ///   assets/fonts/Inter-Regular.ttf
    ///   assets/fonts/Inter-Medium.ttf
    ///   assets/fonts/Inter-SemiBold.ttf
    ///
    /// Until those files exist, every Md3Tokens font falls back to Segoe
    /// UI automatically — the app still works and looks reasonable, just
    /// not pixel-matched to your spec's typography.
    ///
    /// Separate weight FILES (not just a bold flag) are used deliberately:
    /// GDI+'s FontStyle only supports Regular/Bold/Italic, not arbitrary
    /// named weights — Inter's spec calls for weight 500 (Medium) and 600
    /// (SemiBold) specifically, which aren't the same as CSS "bold" (700).
    /// Loading them as distinct font-family names is the only way to get
    /// the actual weights GDI+ can render accurately.
    /// </summary>
    public static class Md3Fonts
    {
        static readonly PrivateFontCollection _collection = new PrivateFontCollection();
        static bool _loaded;
        static bool _hasRegular, _hasMedium, _hasSemiBold;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts");
            _hasRegular = TryAdd(Path.Combine(dir, "Inter-Regular.ttf"));
            _hasMedium = TryAdd(Path.Combine(dir, "Inter-Medium.ttf"));
            _hasSemiBold = TryAdd(Path.Combine(dir, "Inter-SemiBold.ttf"));
        }

        static bool TryAdd(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                _collection.AddFontFile(path);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Get an Inter font at the given size/weight, or a Segoe UI fallback.</summary>
        public static Font Get(float sizePt, bool medium = false, bool semiBold = false)
        {
            EnsureLoaded();

            if (semiBold && _hasSemiBold)
                return new Font(_collection.Families[FamilyIndex("Inter SemiBold")], sizePt, FontStyle.Regular);
            if (medium && _hasMedium)
                return new Font(_collection.Families[FamilyIndex("Inter Medium")], sizePt, FontStyle.Regular);
            if (_hasRegular)
                return new Font(_collection.Families[FamilyIndex("Inter")], sizePt, FontStyle.Regular);

            // Fallback: Segoe UI, approximating Medium/SemiBold with Bold
            // since Segoe UI's GDI+ FontStyle can't hit those exact weights either.
            return new Font("Segoe UI", sizePt, (medium || semiBold) ? FontStyle.Bold : FontStyle.Regular);
        }

        static int FamilyIndex(string name)
        {
            for (int i = 0; i < _collection.Families.Length; i++)
                if (_collection.Families[i].Name == name) return i;
            return 0;
        }
    }
}
