using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings tab: Appearance (light/dark mode + color palette) and
    /// About. Applying a change here calls ThemeManager.Apply(), which
    /// fires ThemeChanged — MainForm listens for that and re-themes the
    /// whole open window immediately, no restart needed.
    /// </summary>
    public class SettingsTab : UserControl
    {
        public SettingsTab()
        {
            BackColor = ThemeManager.Current.Surface;

            var root = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true,
            };

            root.Controls.Add(BuildAppearanceSection());
            root.Controls.Add(BuildAboutSection());

            Controls.Add(root);
            ThemeManager.ThemeChanged += () => BackColor = ThemeManager.Current.Surface;
        }

        Control BuildAppearanceSection()
        {
            var card = new Md3Card { Width = 520, AutoSize = true, Padding = new Padding(Md3Tokens.Space6), Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

            var title = new Label { Text = "Appearance", Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var modeLabel = new Label { Text = "Theme", Font = Md3Tokens.BodyLarge, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space2) };
            var darkSwitch = new Md3Switch
            {
                Text = "Dark mode", Width = 220,
                Checked = ThemeManager.Mode == Md3Mode.Dark,
                Margin = new Padding(0, 0, 0, Md3Tokens.Space6),
            };
            darkSwitch.CheckedChanged += (s, e) =>
                ThemeManager.Apply(darkSwitch.Checked ? Md3Mode.Dark : Md3Mode.Light, ThemeManager.Palette);

            var paletteLabel = new Label { Text = "Color palette", Font = Md3Tokens.BodyLarge, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space2) };
            var paletteRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };

            foreach (Md3Palette p in System.Enum.GetValues(typeof(Md3Palette)))
            {
                paletteRow.Controls.Add(BuildPaletteSwatch(p));
            }

            flow.Controls.Add(title);
            flow.Controls.Add(modeLabel);
            flow.Controls.Add(darkSwitch);
            flow.Controls.Add(paletteLabel);
            flow.Controls.Add(paletteRow);
            card.Controls.Add(flow);
            return card;
        }

        Control BuildPaletteSwatch(Md3Palette palette)
        {
            // a small clickable circular swatch showing that palette's
            // primary color, with a ring around the currently-selected one
            var swatch = new Panel { Width = 40, Height = 40, Margin = new Padding(0, 0, Md3Tokens.Space3, 0), Cursor = Cursors.Hand };
            var color = PrimaryColorFor(palette);
            bool isSelected() => ThemeManager.Palette == palette;

            swatch.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (isSelected())
                {
                    using (var ringPen = new Pen(ThemeManager.Current.OnSurface, 2f))
                        e.Graphics.DrawEllipse(ringPen, 1, 1, 37, 37);
                }
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillEllipse(brush, 6, 6, 28, 28);
            };
            swatch.Click += (s, e) =>
            {
                ThemeManager.Apply(ThemeManager.Mode, palette);
                swatch.Invalidate();
            };
            ThemeManager.ThemeChanged += () => swatch.Invalidate();
            return swatch;
        }

        static Color PrimaryColorFor(Md3Palette p)
        {
            switch (p)
            {
                case Md3Palette.Blue: return ColorTranslator.FromHtml("#1A73E8");
                case Md3Palette.Green: return ColorTranslator.FromHtml("#2E7D4F");
                case Md3Palette.Purple: return ColorTranslator.FromHtml("#7A4FE0");
                case Md3Palette.Orange: return ColorTranslator.FromHtml("#B4540A");
                default: return ColorTranslator.FromHtml("#1A73E8");
            }
        }

        Control BuildAboutSection()
        {
            var card = new Md3Card { Width = 520, AutoSize = true, Padding = new Padding(Md3Tokens.Space6) };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

            var title = new Label { Text = "About", Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var body = new Label
            {
                Text = $"FFX Compatibility Tool\nVersion {version}\n\n" +
                       "Downgrades Adobe After Effects .ffx presets to older AE versions, " +
                       "with plugin-dependency detection.",
                Font = Md3Tokens.BodyLarge, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(460, 0),
            };

            flow.Controls.Add(title);
            flow.Controls.Add(body);
            card.Controls.Add(flow);
            return card;
        }
    }
}
