using System.Drawing;

namespace FfxTool.Gui
{
    /// <summary>
    /// Material Design 3 tokens, hand-applied to WinForms controls.
    ///
    /// WinForms has no native MD3 support (it predates Material Design
    /// entirely), and no skill/library ports MD3 to it — the closest
    /// available skill (material-3-skill) targets Compose/Flutter/Web
    /// only. Rather than skip styling entirely, this file borrows MD3's
    /// actual published design tokens (color roles, spacing scale, type
    /// scale) from m3.material.io and applies them manually via
    /// OwnerDraw / custom-painted controls elsewhere in this project.
    ///
    /// Baseline scheme: a blue seed color (#1A73E8-adjacent), light theme
    /// only for now — dark theme would double the token set and isn't
    /// needed for a first pass.
    /// </summary>
    public static class Md3Tokens
    {
        // --- Color roles (MD3 baseline light scheme) ---
        public static readonly Color Primary = ColorTranslator.FromHtml("#1A73E8");
        public static readonly Color OnPrimary = ColorTranslator.FromHtml("#FFFFFF");
        public static readonly Color PrimaryContainer = ColorTranslator.FromHtml("#D3E3FD");
        public static readonly Color OnPrimaryContainer = ColorTranslator.FromHtml("#001C3B");

        public static readonly Color Surface = ColorTranslator.FromHtml("#FDFBFF");
        public static readonly Color SurfaceContainer = ColorTranslator.FromHtml("#F2F2F7");
        public static readonly Color SurfaceContainerHigh = ColorTranslator.FromHtml("#E9E9EE");
        public static readonly Color OnSurface = ColorTranslator.FromHtml("#1A1B20");
        public static readonly Color OnSurfaceVariant = ColorTranslator.FromHtml("#44474E");

        public static readonly Color Outline = ColorTranslator.FromHtml("#74777F");
        public static readonly Color OutlineVariant = ColorTranslator.FromHtml("#C4C6D0");

        public static readonly Color Error = ColorTranslator.FromHtml("#BA1A1A");
        public static readonly Color ErrorContainer = ColorTranslator.FromHtml("#FFDAD6");
        public static readonly Color OnErrorContainer = ColorTranslator.FromHtml("#410002");

        // A warning tone MD3 doesn't define a role for by default —
        // borrowed from the tertiary role instead of inventing an
        // off-spec color, per the skill's "distillation" approach.
        public static readonly Color TertiaryContainer = ColorTranslator.FromHtml("#FFDDBA");
        public static readonly Color OnTertiaryContainer = ColorTranslator.FromHtml("#2B1700");

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

        // --- Shape (MD3 corner radius scale — used by the rounded-panel
        // and rounded-button helpers in Md3Controls.cs) ---
        public const int CornerSmall = 8;
        public const int CornerMedium = 12;
        public const int CornerLarge = 16;
    }
}
