using System.Drawing;

namespace FfxTool.Gui
{
    /// <summary>
    /// Non-color MD3 tokens: spacing scale, type scale, shape (corner
    /// radius). These don't change between light/dark or between
    /// palettes, so they stay static — colors live in Md3Theme/ThemeManager
    /// since those DO change at runtime.
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

        // --- Type scale — the FULL MD3 scale (m3.material.io/styles/typography/type-scale-tokens):
        // Display / Headline / Title / Body / Label, each Large/Medium/Small.
        // Previous versions of this file only had 6 of these 15 — headers,
        // captions, and body text were all sharing 2-3 sizes with no real
        // differentiation. Point sizes below are a desktop-scaled-down
        // approximation of MD3's sp values (which target mobile density),
        // not a literal 1:1 conversion.
        public static Font DisplayLarge => new Font("Segoe UI", 32f, FontStyle.Regular);
        public static Font DisplayMedium => new Font("Segoe UI", 26f, FontStyle.Regular);
        public static Font DisplaySmall => new Font("Segoe UI", 21f, FontStyle.Regular);

        public static Font HeadlineLarge => new Font("Segoe UI", 19f, FontStyle.Regular);
        public static Font HeadlineMedium => new Font("Segoe UI", 17f, FontStyle.Regular);
        public static Font HeadlineSmall => new Font("Segoe UI", 15f, FontStyle.Regular);

        public static Font TitleLarge => new Font("Segoe UI", 16f, FontStyle.Regular);
        public static Font TitleMedium => new Font("Segoe UI", 12f, FontStyle.Bold);
        public static Font TitleSmall => new Font("Segoe UI", 10.5f, FontStyle.Bold);

        public static Font BodyLarge => new Font("Segoe UI", 10f, FontStyle.Regular);
        public static Font BodyMedium => new Font("Segoe UI", 9f, FontStyle.Regular);
        public static Font BodySmall => new Font("Segoe UI", 8f, FontStyle.Regular);

        public static Font LabelLarge => new Font("Segoe UI", 9f, FontStyle.Bold);
        public static Font LabelMedium => new Font("Segoe UI", 8f, FontStyle.Bold);
        public static Font LabelSmall => new Font("Segoe UI", 7f, FontStyle.Bold);

        // --- Layout tokens — added during layout audit: cards across
        // different tabs were using inconsistent hardcoded widths (420 in
        // ProfileTab, 520 in SettingsTab) with no shared source of truth.
        // Every card-style content block in the app should reference this
        // instead of picking its own number.
        public const int ContentMaxWidth = 520;

        // --- Shape (MD3 corner radius scale) ---
        public const int CornerExtraSmall = 4;
        public const int CornerSmall = 8;
        public const int CornerMedium = 12;
        public const int CornerLarge = 16;
        // "Full" (pill/stadium) has no fixed value — it's computed as
        // bounds.Height / 2 wherever it's used (Md3Button, Md3Switch).

        // --- State-layer opacities (m3.material.io/foundations/interaction/states) —
        // MD3's real hover/press/focus mechanism is a semi-transparent
        // overlay of the state color, not a flat color swap. Previous
        // versions relied entirely on WinForms' free FlatAppearance hover
        // color, which doesn't follow this model. Used by the new
        // Md3StateLayer helper below.
        public const int HoverStateAlpha = 20;   // ~8% per spec, nudged up for screen visibility at small sizes
        public const int PressStateAlpha = 32;   // ~12%
        public const int FocusStateAlpha = 28;   // ~10%
    }

    /// <summary>
    /// Paints a semi-transparent state-layer overlay (hover/press/focus)
    /// on top of a control's existing fill — the actual MD3 mechanism,
    /// rather than swapping the base color outright.
    /// </summary>
    public static class Md3StateLayer
    {
        public static void Paint(Graphics g, System.Drawing.Drawing2D.GraphicsPath path, Color stateColor, int alpha)
        {
            using (var brush = new SolidBrush(Color.FromArgb(alpha, stateColor)))
                g.FillPath(brush, path);
        }
    }
}
