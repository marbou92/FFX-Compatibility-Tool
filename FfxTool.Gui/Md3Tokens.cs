using System.Drawing;

namespace FfxTool.Gui
{
    /// <summary>
    /// Non-color MD3 tokens: spacing scale, type scale, shape (corner
    /// radius). These don't change between light/dark or between
    /// palettes, so they stay static — colors live in Md3Theme/ThemeManager
    /// since those DO change at runtime.
    ///
    /// Type scale below is the EXACT scale from the user's own design spec
    /// ("Material Technical Desktop"), not an approximation — px values
    /// converted to points at the standard 96dpi web / 72dpi print ratio
    /// (pt = px * 0.75). Font family/weight is resolved through
    /// Md3Fonts.Get(), which loads real Inter font files if present and
    /// falls back to Segoe UI otherwise — see Md3Fonts.cs for why the
    /// actual font files couldn't be bundled automatically.
    /// </summary>
    public static class Md3Tokens
    {
        // --- Spacing scale — matches the spec's own tokens directly
        // (stack-gap=8, component-gap=16, gutter=24) plus a couple of
        // in-between half-steps this app already used.
        public const int Space1 = 4;
        public const int Space2 = 8;   // spec: stack-gap
        public const int Space3 = 12;
        public const int Space4 = 16;  // spec: component-gap
        public const int Space6 = 24;  // spec: gutter
        public const int Space8 = 32;  // spec: margin-desktop

        // --- Type scale (spec px -> pt at 0.75 ratio) ---
        public static Font DisplayLarge => Md3Fonts.Get(24f);              // spec has no display tier; reusing headline-lg's weight/style at a larger size
        public static Font DisplayMedium => Md3Fonts.Get(21f);
        public static Font DisplaySmall => Md3Fonts.Get(18f);

        public static Font HeadlineLarge => Md3Fonts.Get(24f);   // spec: headline-lg, 32px/400
        public static Font HeadlineMedium => Md3Fonts.Get(21f);  // spec: headline-md, 28px/400
        public static Font HeadlineSmall => Md3Fonts.Get(18f);   // spec: headline-sm, 24px/400

        public static Font TitleLarge => Md3Fonts.Get(16.5f, medium: true);  // spec: title-lg, 22px/500
        public static Font TitleMedium => Md3Fonts.Get(12f, medium: true);   // spec: title-md, 16px/500
        public static Font TitleSmall => Md3Fonts.Get(10.5f, medium: true);  // spec: title-sm, 14px/500

        public static Font BodyLarge => Md3Fonts.Get(12f);    // spec: body-lg, 16px/400
        public static Font BodyMedium => Md3Fonts.Get(10.5f);  // spec: body-md, 14px/400
        public static Font BodySmall => Md3Fonts.Get(9f);      // spec: body-sm, 12px/400

        public static Font LabelLarge => Md3Fonts.Get(10.5f, medium: true);  // spec: label-lg, 14px/500
        public static Font LabelMedium => Md3Fonts.Get(9f, medium: true);    // spec: label-md, 12px/500
        public static Font LabelSmall => Md3Fonts.Get(8.25f, medium: true);  // spec: label-sm, 11px/500

        // Known gap: the spec also specifies letter-spacing per role (e.g.
        // body-lg: 0.5px) — GDI+'s TextRenderer/Graphics.DrawString has no
        // built-in letter-spacing control. Not implemented; would require
        // manually drawing character-by-character with adjusted advance
        // widths, which is a meaningfully bigger, riskier change than
        // anything else in this token pass. Flagging rather than
        // approximating it badly.

        // --- Shape (spec: sm=4px(0.25rem), DEFAULT=8px(0.5rem),
        // md=12px(0.75rem), lg=16px(1rem), xl=24px(1.5rem), full=pill) ---
        public const int CornerExtraSmall = 4;
        public const int CornerSmall = 8;
        public const int CornerMedium = 12;
        public const int CornerLarge = 16;
        public const int CornerExtraLarge = 24; // spec's "xl" — not previously in this app's scale

        public const int ContentMaxWidth = 520;

        // --- State-layer opacities (m3.material.io/foundations/interaction/states) ---
        public const int HoverStateAlpha = 20;
        public const int PressStateAlpha = 32;
        public const int FocusStateAlpha = 28;
    }

    public static class Md3StateLayer
    {
        public static void Paint(Graphics g, System.Drawing.Drawing2D.GraphicsPath path, Color stateColor, int alpha)
        {
            using (var brush = new SolidBrush(Color.FromArgb(alpha, stateColor)))
                g.FillPath(brush, path);
        }
    }
}
