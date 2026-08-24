using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    // M3 Expressive Bold 12-col bento — whole section rebuilt compact adaptive
    public class ProfileTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Action _onChange;
        readonly Dictionary<string, Md3Switch> _switches = new Dictionary<string, Md3Switch>();

        static readonly Dictionary<string, (Md3Icons.Icon icon, string suites)> VendorMeta = new Dictionary<string, (Md3Icons.Icon, string)>
        {
            { "Boris FX", (Md3Icons.Icon.Diamond, "Sapphire, Continuum, Mocha") },
            { "Plugin Everything", (Md3Icons.Icon.Plugin, "Deep Glow, AutoFill") },
            { "RE:Vision Effects", (Md3Icons.Icon.Eye, "Twixtor, ReelSmart Motion Blur") },
            { "Red Giant / Maxon", (Md3Icons.Icon.AutoAwesome, "Trapcode, Magic Bullet, VFX") },
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
            Dock = DockStyle.Fill;

            // M3 Expressive 12-col scaffold: outer Flow TopDown with centered max 1440
            var outer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0) };
            var centered = new Panel { AutoSize = true, MaximumSize = new Size(Md3Tokens.ContentMaxWidth, 0), Dock = DockStyle.Top };
            centered.Width = Md3Tokens.ContentMaxWidth;
            centered.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Resize += (s,e) => centered.Width = Math.Min(Md3Tokens.ContentMaxWidth, ClientSize.Width - 32);

            var heading = new Label { Text = "Plugin Profiles", Font = Md3Tokens.DisplayLarge, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(Md3Tokens.Space6, Md3Tokens.Space6, 0, Md3Tokens.Space1) };
            var intro = new Label
            {
                Text = "Check off every plugin vendor you have installed in your target After Effects version. This ensures converted FFX files only reference available effects.",
                Font = Md3Tokens.BodyLarge, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(Md3Tokens.Space6, 0, 0, Md3Tokens.Space8),
            };
            ThemeManager.ThemeChanged += () => { heading.ForeColor = ThemeManager.Current.OnSurface; intro.ForeColor = ThemeManager.Current.OnSurfaceVariant; };

            var table = PluginLookup.LoadTable();
            var vendors = _profile.AllKnownVendors(table);

            // Bento grid: 3 cols expanded / 2 medium / 1 compact. Width math
            // accounts for the grid's left margin AND each card's right margin
            // (the old formula clamped card width to 320, which made the
            // 3rd column not fit and left ~40% dead space).
            var grid = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Top, Margin = new Padding(Md3Tokens.Space6, 0, Md3Tokens.Space2, Md3Tokens.Space4) };
            const int baseW = 280;
            foreach (var vendor in vendors)
                grid.Controls.Add(BuildVendorCard(vendor, baseW, 120));
            grid.Controls.Add(BuildAddCustomCard(baseW, 120));
            var discovery = BuildDiscoveryCard(baseW, 120);
            grid.Controls.Add(discovery);

            void RelayoutGrid()
            {
                int gridW = centered.Width - Md3Tokens.Space6 - Md3Tokens.Space2; // grid margins
                int cols = gridW >= 900 ? 3 : gridW >= 600 ? 2 : 1;
                int gutter = Md3Tokens.Space4;
                int cardW = cols == 1
                    ? Math.Max(280, gridW - gutter)
                    : (gridW - (cols - 1) * gutter - cols * gutter) / cols;
                cardW = Math.Max(240, cardW);
                foreach (Control c in grid.Controls)
                {
                    c.Width = c == discovery ? Math.Max(280, gridW - gutter) : cardW;
                }
            }
            Resize += (s, e) => { RelayoutGrid(); };
            centered.Resize += (s, e) => { RelayoutGrid(); };

            centered.Controls.Add(heading);
            centered.Controls.Add(intro);
            centered.Controls.Add(grid);
            // Dock order inside the plain centered panel: last-added docks
            // first, so reverse the visual order via SetChildIndex.
            centered.Controls.SetChildIndex(grid, 0);
            centered.Controls.SetChildIndex(intro, 1);
            centered.Controls.SetChildIndex(heading, 2);

            outer.Controls.Add(centered);
            Controls.Add(outer);
            ThemeManager.ThemeChanged += () => BackColor = ThemeManager.Current.Surface;
        }

        Control BuildDiscoveryCard(int w, int h)
        {
            // Full-width bento card for Automatic Plugin Discovery — replaces
            // the old divider + bare footer row where the Scan System button
            // ended up below the visible area.
            var card = new Md3Card { Width = w, Height = h, Margin = new Padding(0, 0, Md3Tokens.Space4, Md3Tokens.Space4), Variant = Md3CardVariant.Filled };

            var title = new Label { Text = "Automatic Plugin Discovery", Font = Md3Tokens.TitleMedium, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, BackColor = Color.Transparent, Location = new Point(Md3Tokens.Space4, Md3Tokens.Space4) };
            var desc = new Label
            {
                Text = "Select your After Effects 'Plug-ins' directory and we'll automatically check matching vendors.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, BackColor = Color.Transparent, MaximumSize = new Size(440, 0),
                Location = new Point(Md3Tokens.Space4, Md3Tokens.Space4 + 26),
            };
            var scanBtn = new Md3Button { Text = "Scan System", Icon = Md3Icons.Icon.FolderOpen, Variant = Md3ButtonVariant.Tonal, Width = 180, Height = Md3Tokens.ButtonHeight };
            scanBtn.Click += (s, e) => ScanFolder();
            card.Controls.Add(title);
            card.Controls.Add(desc);
            card.Controls.Add(scanBtn);

            card.Resize += (s, e) =>
            {
                scanBtn.Location = new Point(card.ClientSize.Width - scanBtn.Width - Md3Tokens.Space4, (card.ClientSize.Height - scanBtn.Height) / 2);
                desc.MaximumSize = new Size(Math.Max(240, card.ClientSize.Width - scanBtn.Width - Md3Tokens.Space8 * 3), 0);
            };
            return card;
        }

        Control BuildVendorCard(string vendor, int w, int h)
        {
            var card = new Md3Card { Width = w, Height = h, Margin = new Padding(0,0,Md3Tokens.Space4,Md3Tokens.Space4), Variant = Md3CardVariant.Filled };
            var (icon, suites) = VendorMeta.TryGetValue(vendor, out var meta) ? meta : (Md3Icons.Icon.Plugin, "");
            var iconBox = new BufferedPanel { Size = new Size(48,48), Location = new Point(Md3Tokens.Space4, Md3Tokens.Space4), BackColor = Color.Transparent };
            iconBox.Paint += (s,e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var path = RoundedRect(new Rectangle(0,0,47,47), Md3Tokens.CornerMedium))
                using(var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerHigh))
                    e.Graphics.FillPath(brush, path);
                Md3Icons.Draw(e.Graphics, icon, new Rectangle(12,12,24,24), ThemeManager.Current.TertiaryContainer, 1.8f);
            };
            var title = new Label { Text = vendor, Font = Md3Tokens.TitleSmall, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, BackColor = Color.Transparent, Location = new Point(iconBox.Right+Md3Tokens.Space3, iconBox.Top+2) };
            var subtitle = new Label { Text = suites, Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, BackColor = Color.Transparent, MaximumSize = new Size(w - iconBox.Right - 70, 32), Location = new Point(iconBox.Right+Md3Tokens.Space3, title.Bottom+1) };
            var sw = new Md3Switch { Checked = _profile.OwnedVendors.Contains(vendor), Width = 52, Height = 32, Location = new Point(w - 56, Md3Tokens.Space4) };
            sw.CheckedChanged += (s,e) => { _profile.SetOwned(vendor, sw.Checked); _profile.Save(); _onChange(); };
            _switches[vendor]=sw;
            var badge = new BufferedPanel { Size = new Size(w-32,28), Location = new Point(Md3Tokens.Space4, h-36), BackColor = Color.Transparent };
            // Badge reflects the live switch state — previously it always
            // said "Profile linked" whether the vendor was on or off.
            badge.Paint += (s,e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool owned = sw.Checked;
                var b = new Rectangle(0,0,badge.Width-1,badge.Height-1);
                using(var path = RoundedRect(b, Md3Tokens.CornerMedium))
                using(var brush = new SolidBrush(owned
                    ? Color.FromArgb(60, ThemeManager.Current.SurfaceContainerHigh.R, ThemeManager.Current.SurfaceContainerHigh.G, ThemeManager.Current.SurfaceContainerHigh.B)
                    : Color.FromArgb(30, ThemeManager.Current.SurfaceContainerHigh.R, ThemeManager.Current.SurfaceContainerHigh.G, ThemeManager.Current.SurfaceContainerHigh.B)))
                    e.Graphics.FillPath(brush, path);
                if (owned)
                    Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Verified, new Rectangle(8,6,16,16), ThemeManager.Current.Primary, 1.6f);
                else
                    Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Info, new Rectangle(8,6,16,16), ThemeManager.Current.OnSurfaceVariant, 1.6f);
            };
            var badgeLabel = new Label { Text = sw.Checked ? "Profile linked" : "Not in profile", Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, BackColor = Color.Transparent, Location = new Point(28,7) };
            badge.Controls.Add(badgeLabel);
            sw.CheckedChanged += (s,e) =>
            {
                badgeLabel.Text = sw.Checked ? "Profile linked" : "Not in profile";
                badgeLabel.ForeColor = sw.Checked ? ThemeManager.Current.OnSurface : ThemeManager.Current.OnSurfaceVariant;
                badge.Invalidate();
            };
            ThemeManager.ThemeChanged += () => badge.Invalidate();
            card.Controls.Add(iconBox); card.Controls.Add(title); card.Controls.Add(subtitle); card.Controls.Add(sw); card.Controls.Add(badge);
            // keep switch/badge aligned on resize
            card.Resize += (s,e) => { sw.Location = new Point(card.ClientSize.Width - 56, Md3Tokens.Space4); subtitle.MaximumSize = new Size(card.ClientSize.Width - iconBox.Right - 70, 32); badge.Width = card.ClientSize.Width - 32; badge.Location = new Point(Md3Tokens.Space4, card.ClientSize.Height-36); };
            return card;
        }

        Control BuildAddCustomCard(int w, int h)
        {
            var card = new BufferedPanel { Width = w, Height = h, Margin = new Padding(0,0,Md3Tokens.Space4,Md3Tokens.Space4), Enabled = false, BackColor = Color.Transparent };
            var tip = new ToolTip(); tip.SetToolTip(card, "Not yet implemented");
            card.Paint += (s,e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var b = new Rectangle(0,0,card.Width-1,card.Height-1);
                using(var path = RoundedRect(b, Md3Tokens.Corner3XL))
                using(var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerLow))
                using(var pen = new Pen(ThemeManager.Current.OutlineVariant, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Add, new Rectangle(w/2-10, h/2-22, 20,20), ThemeManager.Current.OnSurfaceVariant, 1.6f);
                TextRenderer.DrawText(e.Graphics, "Add custom vendor", Md3Tokens.LabelLarge, new Rectangle(0,h/2+2,w,20), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.HorizontalCenter);
            };
            return card;
        }

        void ScanFolder()
        {
            using(var dlg = new FolderBrowserDialog { Description = "Select your AE plugins folder" })
            {
                if(dlg.ShowDialog()!=DialogResult.OK) return;
                string[] f; try{ f=Directory.GetFiles(dlg.SelectedPath).Select(fn=>Path.GetFileName(fn).ToLowerInvariant()).ToArray(); } catch { /* unreadable folder (locked/no access) — scan is best-effort, bail quietly */ return; }
                foreach(var kv in VendorFileHints)
                {
                    if(!_switches.ContainsKey(kv.Key)) continue;
                    bool found = f.Any(fn=>kv.Value.Any(h=>fn.Contains(h)));
                    if(found) _switches[kv.Key].Checked = true;
                }
            }
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle b,int r)
        {
            var p=new System.Drawing.Drawing2D.GraphicsPath(); int d=r*2; p.AddArc(b.X,b.Y,d,d,180,90); p.AddArc(b.Right-d,b.Y,d,d,270,90); p.AddArc(b.Right-d,b.Bottom-d,d,d,0,90); p.AddArc(b.X,b.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
    }
}
