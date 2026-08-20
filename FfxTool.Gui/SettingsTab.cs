using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// Rebuilt to match the real Settings design (found in
    /// Plugin_profil.zip — same filename/content mismatch pattern
    /// documented in Phases 3-4; confirmed by opening screen.png).
    ///
    /// Matches: card titles/icons, the "Dark Mode" sub-panel row, the
    /// circular palette swatches with a check-icon overlay on the
    /// selected one, the About card's logo box + description copy
    /// (including the bold colored phrase), and "Restore Defaults".
    /// </summary>
    public class SettingsTab : UserControl
    {
        public SettingsTab()
        {
            BackColor = ThemeManager.Current.Surface;

            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

            var title = new Label { Text = "Settings", Font = Md3Tokens.HeadlineMedium, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) };
            var subtitle = new Label { Text = "Manage your preferences and system configuration.", Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            root.Controls.Add(title);
            root.Controls.Add(subtitle);

            var cardsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(980, 0) };
            cardsRow.Controls.Add(BuildAppearanceCard());
            cardsRow.Controls.Add(BuildAboutCard());
            root.Controls.Add(cardsRow);

            var restoreLink = new LinkLabel { Text = "Restore Defaults", Font = Md3Tokens.LabelLarge, AutoSize = true, Margin = new Padding(0, Md3Tokens.Space6, 0, 0) };
            restoreLink.LinkClicked += (s, e) => ThemeManager.Apply(Md3Mode.Light, Md3Palette.Blue);
            root.Controls.Add(restoreLink);

            Controls.Add(root);
            ThemeManager.ThemeChanged += () => BackColor = ThemeManager.Current.Surface;
        }

        Control BuildAppearanceCard()
        {
            var card = new Md3Card { Variant = Md3CardVariant.Filled, Width = 460, AutoSize = true, Padding = new Padding(Md3Tokens.Space6), Margin = new Padding(0, 0, Md3Tokens.Space6, 0) };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

            var headerRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            var iconLbl = new Label { AutoSize = true, Width = 24, Height = 24 };
            iconLbl.Paint += (s, e) => Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Palette, new Rectangle(0, 0, 22, 22), ThemeManager.Current.Primary, 1.8f);
            headerRow.Controls.Add(iconLbl);
            headerRow.Controls.Add(new Label { Text = "Appearance", Font = Md3Tokens.TitleLarge, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(Md3Tokens.Space2, 2, 0, 0) });
            flow.Controls.Add(headerRow);

            // "Dark Mode" sub-panel — a rounded surface-container row
            // inside the card, matching the real design's nested-surface
            // treatment (not just a bare label + switch).
            var darkRow = new Panel { Width = 400, Height = 64, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            darkRow.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = RoundedRect(new Rectangle(0, 0, darkRow.Width - 1, darkRow.Height - 1), Md3Tokens.CornerMedium))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
                    e.Graphics.FillPath(brush, path);
            };
            darkRow.Controls.Add(new Label { Text = "Dark Mode", Font = Md3Tokens.TitleSmall, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Location = new Point(Md3Tokens.Space4, 12) });
            darkRow.Controls.Add(new Label { Text = "Switch between light and dark UI themes", Font = Md3Tokens.BodySmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Location = new Point(Md3Tokens.Space4, 34) });
            var darkSwitch = new Md3Switch { Checked = ThemeManager.Mode == Md3Mode.Dark, Width = 60, Height = 32, Location = new Point(darkRow.Width - 68, 16) };
            darkSwitch.CheckedChanged += (s, e) => ThemeManager.Apply(darkSwitch.Checked ? Md3Mode.Dark : Md3Mode.Light, ThemeManager.Palette);
            darkRow.Controls.Add(darkSwitch);
            flow.Controls.Add(darkRow);

            flow.Controls.Add(new Label { Text = "Color palette", Font = Md3Tokens.TitleSmall, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) });
            var paletteRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
            foreach (Md3Palette p in System.Enum.GetValues(typeof(Md3Palette)))
                paletteRow.Controls.Add(BuildPaletteSwatch(p));
            flow.Controls.Add(paletteRow);

            card.Controls.Add(flow);
            return card;
        }

        Control BuildPaletteSwatch(Md3Palette palette)
        {
            // Real design: a filled circular swatch, "check" icon shown
            // in white on the selected one, a subtle ring/border on
            // selection, and the palette name below.
            var container = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            var swatch = new Panel { Width = 48, Height = 48, Cursor = Cursors.Hand };
            var nameLbl = new Label { Text = PaletteName(palette), Font = Md3Tokens.LabelSmall, AutoSize = true, Anchor = AnchorStyles.Top };

            void Repaint()
            {
                bool selected = ThemeManager.Palette == palette;
                nameLbl.ForeColor = selected ? ThemeManager.Current.Primary : ThemeManager.Current.OnSurfaceVariant;
                swatch.Invalidate();
            }

            swatch.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool selected = ThemeManager.Palette == palette;
                var color = PrimaryColorFor(palette);
                if (selected)
                {
                    using (var ringPen = new Pen(ThemeManager.Current.Primary, 3f))
                        e.Graphics.DrawEllipse(ringPen, 2, 2, 43, 43);
                }
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillEllipse(brush, 6, 6, 36, 36);
                if (selected)
                    Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Check, new Rectangle(15, 15, 18, 18), Color.White, 2.2f);
            };
            swatch.Click += (s, e) => { ThemeManager.Apply(ThemeManager.Mode, palette); };
            ThemeManager.ThemeChanged += Repaint;

            container.Controls.Add(swatch);
            container.Controls.Add(nameLbl);
            Repaint();
            return container;
        }

        static string PaletteName(Md3Palette p) => p.ToString();

        static Color PrimaryColorFor(Md3Palette p)
        {
            // Real exact hex values from the design spec (not
            // approximated) — Blue matches ThemeManager's own exact
            // BlueLight primary; Green/Purple/Orange are the spec's real
            // swatch colors even though the rest of those 3 palettes'
            // tokens are still approximated (see Phase 1's README).
            switch (p)
            {
                case Md3Palette.Blue: return ColorTranslator.FromHtml("#005BBF");
                case Md3Palette.Green: return ColorTranslator.FromHtml("#2E6C4A");
                case Md3Palette.Purple: return ColorTranslator.FromHtml("#6341D5");
                case Md3Palette.Orange: return ColorTranslator.FromHtml("#9C4500");
                default: return ColorTranslator.FromHtml("#005BBF");
            }
        }

        Control BuildAboutCard()
        {
            var card = new Md3Card { Variant = Md3CardVariant.Elevated, Width = 400, AutoSize = true, Padding = new Padding(Md3Tokens.Space6) };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

            var headerRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            var infoIcon = new Label { AutoSize = true, Width = 24, Height = 24 };
            infoIcon.Paint += (s, e) => Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Info, new Rectangle(0, 0, 22, 22), ThemeManager.Current.OnSurfaceVariant, 1.8f);
            headerRow.Controls.Add(infoIcon);
            headerRow.Controls.Add(new Label { Text = "About", Font = Md3Tokens.TitleLarge, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(Md3Tokens.Space2, 2, 0, 0) });
            flow.Controls.Add(headerRow);

            var logoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            var logoBox = new Panel { Width = 64, Height = 64, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            logoBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = RoundedRect(new Rectangle(0, 0, 63, 63), Md3Tokens.CornerLarge))
                using (var brush = new SolidBrush(ThemeManager.Current.Surface))
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Logo, new Rectangle(16, 16, 32, 32), ThemeManager.Current.Primary, 1.8f);
            };
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var nameCol = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            nameCol.Controls.Add(new Label { Text = "FFX Compatibility Tool", Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true });
            nameCol.Controls.Add(new Label { Text = $"Version {version}", Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true });
            logoRow.Controls.Add(logoBox);
            logoRow.Controls.Add(nameCol);
            flow.Controls.Add(logoRow);

            var divider = new Panel { Width = 340, Height = 1, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            divider.Paint += (s, e) => { using (var pen = new Pen(ThemeManager.Current.OutlineVariant)) e.Graphics.DrawLine(pen, 0, 0, divider.Width, 0); };
            flow.Controls.Add(divider);

            // Real design bolds/colors one phrase inline within the
            // paragraph — approximated here as two separate labels rather
            // than true inline rich-text (WinForms Label doesn't support
            // mixed-style runs without a much heavier custom text-layout
            // control), which reads close enough at this text length.
            flow.Controls.Add(new Label
            {
                Text = "A professional-grade utility designed for motion designers and video editors.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, MaximumSize = new Size(340, 0),
            });
            flow.Controls.Add(new Label
            {
                Text = "This tool specializes in downgrading After Effects presets (.ffx) to ensure cross-version compatibility.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.Primary, AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, Md3Tokens.Space1, 0, Md3Tokens.Space2),
            });
            flow.Controls.Add(new Label
            {
                Text = "Designed with precision to maintain keyframe data and expression integrity across different software generations.",
                Font = Md3Tokens.BodySmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 0, 0, Md3Tokens.Space6),
            });

            var buttonsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var updatesBtn = new Md3Button { Text = "Check for Updates", Width = 200, Enabled = false, Margin = new Padding(0, 0, Md3Tokens.Space2, 0) };
            var updatesTip = new ToolTip(); updatesTip.SetToolTip(updatesBtn, "Not yet implemented — no update server exists for this app yet");
            var logsBtn = new Md3Button { Text = "Logs", Variant = Md3ButtonVariant.Outlined, Width = 100, Enabled = false };
            var logsTip = new ToolTip(); logsTip.SetToolTip(logsBtn, "Not yet implemented — Convert's console output isn't persisted to a log file yet");
            buttonsRow.Controls.Add(updatesBtn);
            buttonsRow.Controls.Add(logsBtn);
            flow.Controls.Add(buttonsRow);

            card.Controls.Add(flow);
            return card;
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
