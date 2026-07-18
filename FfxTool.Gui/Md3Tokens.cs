using System.Drawing;

namespace FfxTool.Gui
{
    /// <summary>
    /// Non-color MD3 tokens: spacing scale, type scale, shape (corner
    /// radius). These don't change between light/dark or between
    /// palettes, so they stay static — unlike colors, which moved to
    /// Md3Theme/ThemeManager so they can change at runtime.
    /// </summary>
    public static class Md3Tokens
    {
        // --- Spacing scale (MD3's 8dp baseline grid + 4dp half-step) ---
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space6 = 24;
        public const int Space8 = 32;

        // --- Type scale (subset — WinForms doesn't do variable font
        // weights well without custom rendering, so this stays close to
        // system-safe sizes rather than the full MD3 scale) ---
        public static Font TitleLarge => new Font("Segoe UI", 16f, FontStyle.Regular);
        public static Font TitleMedium => new Font("Segoe UI", 12f, FontStyle.Bold);
        public static Font BodyLarge => new Font("Segoe UI", 10f, FontStyle.Regular);
        public static Font BodyMedium => new Font("Segoe UI", 9f, FontStyle.Regular);
        public static Font LabelLarge => new Font("Segoe UI", 9f, FontStyle.Bold);

        // --- Shape (MD3 corner radius scale) ---
        public const int CornerSmall = 8;
        public const int CornerMedium = 12;
        public const int CornerLarge = 16;
    }
}
