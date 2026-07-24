using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Rebuilt to match the user's real design (found in Effect_Lister.zip,
    /// which — despite the filename — actually contains the Plugin
    /// Profile screen; confirmed by opening screen.png directly, same
    /// filename/content mismatch documented in Phase 3).
    ///
    /// Real design: a 3-column bento-style card grid (icon + name +
    /// suite list + switch per vendor), a dashed "Add custom vendor"
    /// card, and a footer with the folder-scan action — matching the
    /// exact copy and layout from code.html.
    /// </summary>
    public class ProfileTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Action _onChange;
        readonly Dictionary<string, Md3Switch> _switches = new Dictionary<string, Md3Switch>();

        // Vendor -> (icon, suite list) taken directly from the real
        // design's card copy ("Sapphire, Continuum, Mocha" etc.)
        static readonly Dictionary<string, (Md3Icons.Icon icon, string suites)> VendorMeta = new Dictionary<string, (Md3Icons.Icon, string)>
        {
            { "Boris FX", (Md3Icons.Icon.Diamond, "Sapphire, Continuum, Mocha") },
            { "Plugin Everything", (Md3Icons.Icon.Plugin, "Deep Glow, AutoFill") },
            { "RE:Vision Effects", (Md3Icons.Icon.Eye, "Twixtor, ReelSmart Motion Blur") },
            { "Red Giant / Maxon", (Md3Icons.Icon.Convert, "Trapcode, Magic Bullet, VFX") }, // "Convert" reused as a motion-ish placeholder for the real design's motion_photos_on icon, pending Phase 7's real icon font
            { "Video Copilot", (Md3Icons.Icon.Flare, "Optical Flares, Element 3D, Saber") },
        };

        static readonly Dictionary<string, string[]> VendorFileHints = new Dictionary<string, string[]>
        {
            { "Boris FX", new[] { "sapphire", "continuum", "bcc" } },
            { "Red Giant / Maxon", new[] { "magic bullet", "trapcode", "red giant" } },
            { "Video Copilot", new[] { "element", "optical flares", "saber", "twitch" } },
            { "Plugin Everything", new[] { "deep glow", "shadow studio" } },
            { "RE:Vision Effects", new[] { "twixtor", "reelsmart" } },
        };

        public ProfileTab(PluginProfile profile, Action onChange)
        {
            _profile = profile;
            _onChange = onChange;
            BackColor = ThemeManager.Current.Surface;
            AutoScroll = true;

            var root = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

            var intro = new Label
            {
                // exact copy from the real design
                Text = "Check off every plugin vendor you have installed in your target After Effects\n" +
                       "version. This ensures converted FFX files only reference available effects.",
                Font = Md3Tokens.BodyLarge, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space6),
            };
            root.Controls.Add(intro);

            var table = PluginLookup.LoadTable();
            var vendors = _profile.AllKnownVendors(table);

            // 3-column bento grid, matching the real design's "grid-cols-3" —
            // vendors plus one trailing "Add custom vendor" card
            const int cols = 3;
            const int cardW = 280, cardH = 96;
            var grid = new TableLayoutPanel
            {
                AutoSize = true, ColumnCount = cols,
                RowCount = (int)Math.Ceiling((vendors.Count + 1) / (float)cols),
                Margin = new Padding(0, 0, 0, Md3Tokens.Space8),
            };
            for (int c = 0; c < cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, cardW + Md3Tokens.Space6));

            int col = 0, row = 0;
            foreach (var vendor in vendors)
            {
                grid.Controls.Add(BuildVendorCard(vendor, cardW, cardH), col, row);
                col++; if (col >= cols) { col = 0; row++; }
            }
            grid.Controls.Add(BuildAddCustomCard(cardW, cardH), col, row);
            root.Controls.Add(grid);

            // --- footer: Automatic Plugin Discovery ---
            var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            var footerLeft = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            footerLeft.Controls.Add(new Label
            {
                Text = "Automatic Plugin Discovery", Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface,
                AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1),
            });
            footerLeft.Controls.Add(new Label
            {
                Text = "Select your After Effects 'Plug-ins' directory and we'll automatically check matching vendors.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(480, 0),
            });

            var footerRight = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Anchor = AnchorStyles.Right, WrapContents = false };
            var scanBtn = new Md3Button { Text = "Scan a plugins folder…", Icon = Md3Icons.Icon.FolderOpen, Variant = Md3ButtonVariant.Outlined, Width = 220 };
            scanBtn.Click += (s, e) => ScanFolder();
            footerRight.Controls.Add(scanBtn);
            // Real design's caption claims specific AE version support
            // ("Supports CC 2022, 2023, 2024") that isn't actually true of
            // this feature — the folder scan is a generic filename-match
            // heuristic, not something that's version-gated at all.
            // Replaced with an honest description of what it actually does.
            footerRight.Controls.Add(new Label
            {
                Text = "Looks for known vendor files in the folder you select.",
                Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, Margin = new Padding(0, Md3Tokens.Space1, 0, 0),
            });

            footer.Controls.Add(footerLeft, 0, 0);
            footer.Controls.Add(footerRight, 1, 0);

            var divider = new Panel { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            divider.Paint += (s, e) => { using (var pen = new Pen(ThemeManager.Current.OutlineVariant)) e.Graphics.DrawLine(pen, 0, 0, divider.Width, 0); };
            divider.Resize += (s, e) => divider.Invalidate();

            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false };
            content.Controls.Add(root);
            content.Controls.Add(divider);
            content.Controls.Add(footer);
            Controls.Add(content);
        }

        Control BuildVendorCard(string vendor, int w, int h)
        {
            var card = new Md3Card { Width = w, Height = h, Margin = new Padding(0, 0, Md3Tokens.Space6, Md3Tokens.Space6), Variant = Md3CardVariant.Filled };
            var (icon, suites) = VendorMeta.TryGetValue(vendor, out var meta) ? meta : (Md3Icons.Icon.Plugin, "");

            var iconBox = new Panel { Size = new Size(48, 48), Location = new Point(Md3Tokens.Space6, (h - 48) / 2) };
            iconBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = RoundedRect(new Rectangle(0, 0, 47, 47), Md3Tokens.CornerMedium))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerHigh))
                    e.Graphics.FillPath(brush, path);
                Md3Icons.Draw(e.Graphics, icon, new Rectangle(12, 12, 24, 24), ThemeManager.Current.TertiaryContainer, 1.8f);
            };

            var title = new Label
            {
                Text = vendor, Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface,
                AutoSize = true, Location = new Point(iconBox.Right + Md3Tokens.Space4, iconBox.Top + 2),
            };
            var subtitle = new Label
            {
                Text = suites, Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(w - iconBox.Right - Md3Tokens.Space6 - 60, 0),
                Location = new Point(iconBox.Right + Md3Tokens.Space4, title.Bottom + 2),
            };

            var sw = new Md3Switch { Checked = _profile.OwnedVendors.Contains(vendor), Width = 52, Height = 32 };
            sw.Location = new Point(w - sw.Width - Md3Tokens.Space4, (h - sw.Height) / 2);
            sw.CheckedChanged += (s, e) =>
            {
                _profile.SetOwned(vendor, sw.Checked);
                _profile.Save();
                _onChange();
            };
            _switches[vendor] = sw;

            card.Controls.Add(iconBox);
            card.Controls.Add(title);
            card.Controls.Add(subtitle);
            card.Controls.Add(sw);
            return card;
        }

        Control BuildAddCustomCard(int w, int h)
        {
            // Real design shows a dashed "Add custom vendor" card. Not
            // wired to real functionality — this app's plugin lookup is a
            // fixed table (data/plugin_table.json), it doesn't currently
            // support arbitrary user-added vendors at runtime. Shown
            // disabled with a tooltip rather than implying it works.
            var card = new Panel { Width = w, Height = h, Margin = new Padding(0, 0, Md3Tokens.Space6, Md3Tokens.Space6), Enabled = false, Cursor = Cursors.Default };
            var tip = new ToolTip();
            tip.SetToolTip(card, "Not yet implemented");
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var path = RoundedRect(bounds, Md3Tokens.CornerExtraLarge))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerLow))
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Check, new Rectangle(w / 2 - 10, h / 2 - 22, 20, 20), ThemeManager.Current.OnSurfaceVariant, 1.6f);
                TextRenderer.DrawText(e.Graphics, "Add custom vendor", Md3Tokens.LabelLarge,
                    new Rectangle(0, h / 2 + 2, w, 20), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.HorizontalCenter);
            };
            return card;
        }

        void ScanFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select your AE plugins folder" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string[] filenames;
                try { filenames = Directory.GetFiles(dlg.SelectedPath).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToArray(); }
                catch (IOException) { return; }

                foreach (var kv in VendorFileHints)
                {
                    if (!_switches.ContainsKey(kv.Key)) continue;
                    bool found = filenames.Any(fname => kv.Value.Any(hint => fname.Contains(hint)));
                    if (found) _switches[kv.Key].Checked = true;
                }
            }
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
