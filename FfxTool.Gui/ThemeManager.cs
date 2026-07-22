using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FfxTool.Gui
{
    /// <summary>
    /// A full MD3 color-role set (m3.material.io/styles/color/roles) for
    /// one theme+palette combination. Everything that was previously a
    /// static readonly Color on Md3Tokens now lives here as an *instance*
    /// field, so it can change at runtime.
    /// </summary>
    public class Md3Theme
    {
        public Color Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer;
        public Color Surface, SurfaceContainerLowest, SurfaceContainerLow, SurfaceContainer, SurfaceContainerHigh, OnSurface, OnSurfaceVariant;
        public Color Outline, OutlineVariant;
        public Color Error, ErrorContainer, OnErrorContainer;
        public Color TertiaryContainer, OnTertiaryContainer;
        // Your spec ("Material Technical Desktop") calls out Side
        // Navigation as its own distinct token (#F6F6FA) — close to but
        // NOT the same as surface-container-low (#f5f3f7). Previously
        // NavRail just reused SurfaceContainerLow; now it uses this
        // dedicated token, matching your spec's actual intent
        // ("visually separate the application structure from the workspace").
        public Color NavigationSurface;

        static Color H(string hex) => ColorTranslator.FromHtml(hex);

        static Color Blend(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

        // Fills in any tier a specific palette definition didn't set
        // explicitly, by interpolating between Surface and SurfaceContainer.
        // BlueLight() now sets SurfaceContainerLowest/Low/NavigationSurface
        // directly from the user's exact design spec (real hex values, not
        // approximations) — this only fills the gap for the other 7
        // palette/mode combinations that don't have a full spec yet.
        static Md3Theme FillDerived(Md3Theme t)
        {
            if (t.SurfaceContainerLowest.IsEmpty) t.SurfaceContainerLowest = Blend(t.Surface, t.SurfaceContainer, 0.25f);
            if (t.SurfaceContainerLow.IsEmpty) t.SurfaceContainerLow = Blend(t.Surface, t.SurfaceContainer, 0.6f);
            if (t.NavigationSurface.IsEmpty) t.NavigationSurface = t.SurfaceContainerLow;
            return t;
        }

        // --- Palette seed hues. Each palette below defines its own full
        // light+dark role set rather than deriving one algorithmically —
        // MD3's real tonal-palette generation (m3.material.io/styles/color/system)
        // is a whole HCT color-space algorithm; hand-picking role colors
        // per seed is a pragmatic approximation of it, not a reimplementation.

        // Exact values from the user's own design spec ("Material Technical
        // Desktop"), not an approximation — every field here traces to a
        // literal hex value in that document, including the two surface
        // tiers and the dedicated navigation-rail color it calls out
        // specifically ("Side Navigation: a slightly cooler, muted gray").
        public static Md3Theme BlueLight() => FillDerived(new Md3Theme {
            Primary = H("#005BBF"), OnPrimary = H("#FFFFFF"), PrimaryContainer = H("#1A73E8"), OnPrimaryContainer = H("#FFFFFF"),
            Surface = H("#FAF9FC"),
            SurfaceContainerLowest = H("#FFFFFF"), SurfaceContainerLow = H("#F5F3F7"),
            SurfaceContainer = H("#EFEDF1"), SurfaceContainerHigh = H("#E9E7EB"),
            OnSurface = H("#1A1B20"), OnSurfaceVariant = H("#44474E"),
            Outline = H("#C4C6D0"), OutlineVariant = H("#C1C6D6"),
            Error = H("#BA1A1A"), ErrorContainer = H("#FFDAD6"), OnErrorContainer = H("#93000A"),
            TertiaryContainer = H("#7C5DF0"), OnTertiaryContainer = H("#050021"),
            NavigationSurface = H("#F6F6FA"),
        });

        public static Md3Theme BlueDark() => FillDerived(new Md3Theme {
            Primary = H("#A9C7FF"), OnPrimary = H("#00315C"), PrimaryContainer = H("#00478A"), OnPrimaryContainer = H("#D3E3FD"),
            Surface = H("#111318"), SurfaceContainer = H("#1D2024"), SurfaceContainerHigh = H("#282A2F"),
            OnSurface = H("#E2E2E9"), OnSurfaceVariant = H("#C4C6D0"),
            Outline = H("#8E9099"), OutlineVariant = H("#44474E"),
            Error = H("#FFB4AB"), ErrorContainer = H("#93000A"), OnErrorContainer = H("#FFDAD6"),
            TertiaryContainer = H("#5C4200"), OnTertiaryContainer = H("#FFDDBA"),
        });

        public static Md3Theme GreenLight() => FillDerived(new Md3Theme {
            Primary = H("#2E7D4F"), OnPrimary = H("#FFFFFF"), PrimaryContainer = H("#B2F2C9"), OnPrimaryContainer = H("#00210F"),
            Surface = H("#FBFDF8"), SurfaceContainer = H("#EEF2EB"), SurfaceContainerHigh = H("#E3E8E0"),
            OnSurface = H("#191C19"), OnSurfaceVariant = H("#414942"),
            Outline = H("#717971"), OutlineVariant = H("#C1C9BF"),
            Error = H("#BA1A1A"), ErrorContainer = H("#FFDAD6"), OnErrorContainer = H("#410002"),
            TertiaryContainer = H("#D2E4FF"), OnTertiaryContainer = H("#001C38"),
        });

        public static Md3Theme GreenDark() => FillDerived(new Md3Theme {
            Primary = H("#8FD6A6"), OnPrimary = H("#00391C"), PrimaryContainer = H("#00522C"), OnPrimaryContainer = H("#B2F2C9"),
            Surface = H("#101411"), SurfaceContainer = H("#1B1F1B"), SurfaceContainerHigh = H("#252A25"),
            OnSurface = H("#E1E3DD"), OnSurfaceVariant = H("#C1C9BF"),
            Outline = H("#8B938A"), OutlineVariant = H("#414942"),
            Error = H("#FFB4AB"), ErrorContainer = H("#93000A"), OnErrorContainer = H("#FFDAD6"),
            TertiaryContainer = H("#00447C"), OnTertiaryContainer = H("#D2E4FF"),
        });

        public static Md3Theme PurpleLight() => FillDerived(new Md3Theme {
            Primary = H("#7A4FE0"), OnPrimary = H("#FFFFFF"), PrimaryContainer = H("#E8DDFF"), OnPrimaryContainer = H("#26005A"),
            Surface = H("#FDFBFF"), SurfaceContainer = H("#F1EDF7"), SurfaceContainerHigh = H("#E6E1EC"),
            OnSurface = H("#1C1B20"), OnSurfaceVariant = H("#48454E"),
            Outline = H("#79747E"), OutlineVariant = H("#C9C4CF"),
            Error = H("#BA1A1A"), ErrorContainer = H("#FFDAD6"), OnErrorContainer = H("#410002"),
            TertiaryContainer = H("#FFD9E1"), OnTertiaryContainer = H("#3E001D"),
        });

        public static Md3Theme PurpleDark() => FillDerived(new Md3Theme {
            Primary = H("#CFBCFF"), OnPrimary = H("#3F1F94"), PrimaryContainer = H("#5A38AC"), OnPrimaryContainer = H("#E8DDFF"),
            Surface = H("#141317"), SurfaceContainer = H("#201F24"), SurfaceContainerHigh = H("#2B292F"),
            OnSurface = H("#E6E1E9"), OnSurfaceVariant = H("#C9C4CF"),
            Outline = H("#928F99"), OutlineVariant = H("#48454E"),
            Error = H("#FFB4AB"), ErrorContainer = H("#93000A"), OnErrorContainer = H("#FFDAD6"),
            TertiaryContainer = H("#5E1133"), OnTertiaryContainer = H("#FFD9E1"),
        });

        public static Md3Theme OrangeLight() => FillDerived(new Md3Theme {
            Primary = H("#B4540A"), OnPrimary = H("#FFFFFF"), PrimaryContainer = H("#FFDBC6"), OnPrimaryContainer = H("#391300"),
            Surface = H("#FFFBFF"), SurfaceContainer = H("#F6EEE9"), SurfaceContainerHigh = H("#EBE2DD"),
            OnSurface = H("#201A17"), OnSurfaceVariant = H("#52443D"),
            Outline = H("#85736C"), OutlineVariant = H("#D8C2B9"),
            Error = H("#BA1A1A"), ErrorContainer = H("#FFDAD6"), OnErrorContainer = H("#410002"),
            TertiaryContainer = H("#D8E6BB"), OnTertiaryContainer = H("#141F00"),
        });

        public static Md3Theme OrangeDark() => FillDerived(new Md3Theme {
            Primary = H("#FFB68C"), OnPrimary = H("#5D2600"), PrimaryContainer = H("#853800"), OnPrimaryContainer = H("#FFDBC6"),
            Surface = H("#18120F"), SurfaceContainer = H("#241E1A"), SurfaceContainerHigh = H("#2F2824"),
            OnSurface = H("#EDE0DA"), OnSurfaceVariant = H("#D8C2B9"),
            Outline = H("#A08D85"), OutlineVariant = H("#52443D"),
            Error = H("#FFB4AB"), ErrorContainer = H("#93000A"), OnErrorContainer = H("#FFDAD6"),
            TertiaryContainer = H("#3B4A1A"), OnTertiaryContainer = H("#D8E6BB"),
        });
    }

    public enum Md3Palette { Blue, Green, Purple, Orange }
    public enum Md3Mode { Light, Dark }

    /// <summary>
    /// Holds the app-wide current theme, persists the user's mode/palette
    /// choice, and notifies subscribers when it changes so open windows
    /// can re-theme themselves without a restart.
    /// </summary>
    public static class ThemeManager
    {
        public static Md3Theme Current { get; private set; } = Md3Theme.BlueLight();
        public static Md3Mode Mode { get; private set; } = Md3Mode.Light;
        public static Md3Palette Palette { get; private set; } = Md3Palette.Blue;

        public static event Action ThemeChanged;

        class StoredSettings
        {
            public string mode { get; set; }
            public string palette { get; set; }
        }

        static string SettingsPath()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(baseDir, "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "appearance.json");
        }

        public static void Load()
        {
            try
            {
                var path = SettingsPath();
                if (File.Exists(path))
                {
                    var data = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path));
                    if (Enum.TryParse<Md3Mode>(data?.mode, out var mode)) Mode = mode;
                    if (Enum.TryParse<Md3Palette>(data?.palette, out var palette)) Palette = palette;
                }
            }
            catch (Exception) { /* fall back to defaults silently — appearance prefs aren't critical */ }

            Apply(Mode, Palette, save: false);
        }

        public static void Apply(Md3Mode mode, Md3Palette palette, bool save = true)
        {
            Mode = mode;
            Palette = palette;

            Current = (palette, mode) switch
            {
                (Md3Palette.Blue, Md3Mode.Light) => Md3Theme.BlueLight(),
                (Md3Palette.Blue, Md3Mode.Dark) => Md3Theme.BlueDark(),
                (Md3Palette.Green, Md3Mode.Light) => Md3Theme.GreenLight(),
                (Md3Palette.Green, Md3Mode.Dark) => Md3Theme.GreenDark(),
                (Md3Palette.Purple, Md3Mode.Light) => Md3Theme.PurpleLight(),
                (Md3Palette.Purple, Md3Mode.Dark) => Md3Theme.PurpleDark(),
                (Md3Palette.Orange, Md3Mode.Light) => Md3Theme.OrangeLight(),
                (Md3Palette.Orange, Md3Mode.Dark) => Md3Theme.OrangeDark(),
                _ => Md3Theme.BlueLight(),
            };

            if (save)
            {
                try
                {
                    var data = new StoredSettings { mode = Mode.ToString(), palette = Palette.ToString() };
                    File.WriteAllText(SettingsPath(), JsonSerializer.Serialize(data));
                }
                catch (Exception) { /* best-effort — don't block a theme switch on a failed save */ }
            }

            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Recursively re-applies Surface/OnSurface colors to every child
        /// control and forces a repaint. Custom-painted controls (Md3Button,
        /// Md3Card, Md3Switch, etc.) already read ThemeManager.Current
        /// fresh inside their own OnPaint, so Invalidate(true) alone is
        /// enough for those — this walk exists for the *native* WinForms
        /// controls (ListView, TextBox, Panel, Form) that hold a static
        /// BackColor/ForeColor property rather than computing it per-paint.
        /// </summary>
        public static void ApplyToTree(System.Windows.Forms.Control root)
        {
            if (root is System.Windows.Forms.Form form)
                form.BackColor = Current.Surface;
            else if (root is System.Windows.Forms.UserControl uc)
                uc.BackColor = Current.Surface;
            else if (root is System.Windows.Forms.Panel panel && panel.GetType() == typeof(System.Windows.Forms.Panel))
                panel.BackColor = Current.Surface;
            else if (root is System.Windows.Forms.ListView lv)
            {
                lv.BackColor = Current.Surface;
                lv.ForeColor = Current.OnSurface;
            }
            else if (root is System.Windows.Forms.TextBox tb)
            {
                tb.BackColor = Current.SurfaceContainer;
                tb.ForeColor = Current.OnSurface;
            }
            else if (root is System.Windows.Forms.CheckedListBox clb)
            {
                clb.BackColor = Current.Surface;
                clb.ForeColor = Current.OnSurface;
            }
            else if (root is System.Windows.Forms.Label lbl && lbl.GetType() == typeof(System.Windows.Forms.Label))
            {
                lbl.ForeColor = Current.OnSurface;
            }

            root.Invalidate(true);

            foreach (System.Windows.Forms.Control child in root.Controls)
                ApplyToTree(child);
        }
    }
}
