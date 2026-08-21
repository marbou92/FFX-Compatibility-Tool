using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    public class MainForm : Form
    {
        readonly PluginProfile _profile;
        readonly ListerTab _listerTab;
        readonly ProfileTab _profileTab;
        readonly ConvertTab _convertTab;
        readonly SettingsTab _settingsTab;
        readonly Panel _contentHost;
        readonly NavRail _navRail;
        readonly TableLayoutPanel _root;
        readonly TableLayoutPanel _body;
        readonly Md3TitleBar _titleBar;

        // resize-border thickness for the WM_NCHITTEST override below —
        // wide enough to grab comfortably with a mouse, matching roughly
        // what native Windows borders feel like.
        const int ResizeBorder = 6;

        public MainForm()
        {
            ThemeManager.Load(); // must happen before any control reads ThemeManager.Current

            // Custom chrome: no native title bar/border at all — Md3TitleBar
            // (below) replaces minimize/maximize/close/drag, and the
            // WndProc override replaces resize-by-edge-drag. See
            // Md3TitleBar.cs for why these specific techniques were chosen
            // over anything deeper/riskier.
            FormBorderStyle = FormBorderStyle.None;

            Text = "FFX Compatibility Tool";
            MinimumSize = new Size(960, 620);
            Size = new Size(1180, 740); // expressive default — shows 3-col grid + supporting pane on xl
            BackColor = ThemeManager.Current.Surface;
            Font = Md3Tokens.BodyLarge;

            _profile = PluginProfile.Load();

            _listerTab = new ListerTab(_profile) { Dock = DockStyle.Fill, Visible = false };
            _profileTab = new ProfileTab(_profile, OnProfileChanged) { Dock = DockStyle.Fill, Visible = false };
            _convertTab = new ConvertTab(_profile) { Dock = DockStyle.Fill, Visible = false };
            _settingsTab = new SettingsTab { Dock = DockStyle.Fill, Visible = false };

            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Current.Surface, Padding = new Padding(Md3Tokens.Space8), AutoScroll = true };
            // M3 12-col: content max 1440 centered — keep 32 margin, 24 gutter via Space8/Space6
            _contentHost.Resize += (s, e) =>
            {
                int maxW = Md3Tokens.ContentMaxWidth; // 1440
                int avail = _contentHost.ClientSize.Width;
                int pad = avail > maxW ? (avail - maxW) / 2 : Md3Tokens.Space8;
                pad = Math.Max(Md3Tokens.Space6, pad);
                _contentHost.Padding = new Padding(pad, Md3Tokens.Space8, pad, Md3Tokens.Space8);
            };
            _contentHost.Controls.Add(_settingsTab);
            _contentHost.Controls.Add(_convertTab);
            _contentHost.Controls.Add(_profileTab);
            _contentHost.Controls.Add(_listerTab);

            var supportPane = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(217, ThemeManager.Current.Surface.R, ThemeManager.Current.Surface.G, ThemeManager.Current.Surface.B), Visible = false, Width = 360 };
            supportPane.Padding = new Padding(Md3Tokens.Space6);
            supportPane.Paint += (s, e) =>
            {
                if (!supportPane.Visible) return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(51, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                    e.Graphics.DrawLine(pen, 0, 0, 0, supportPane.Height);
                // M3 Expressive supporting pane — real history wired to HistoryStore
                TextRenderer.DrawText(e.Graphics, "Recent Files", Md3Tokens.TitleMedium, new Rectangle(16, 16, 328, 24), ThemeManager.Current.OnSurface, TextFormatFlags.Left);
                var recents = HistoryStore.Load();
                if (recents.Count == 0)
                {
                    TextRenderer.DrawText(e.Graphics, "No recent files", Md3Tokens.BodySmall, new Rectangle(16, 44, 328, 20), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.Left);
                }
                else
                {
                    int y = 44;
                    foreach (var r in recents.Take(3))
                    {
                        string line = $"{r.fileName} • {HistoryStore.TimeAgo(r.timestamp)} • {r.bytes / 1024} KB";
                        TextRenderer.DrawText(e.Graphics, line, Md3Tokens.BodySmall, new Rectangle(16, y, 328, 20), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                        y += 22;
                    }
                }
                int statsY = 44 + Math.Max(1, Math.Min(3, HistoryStore.Load().Count)) * 22 + 24;
                TextRenderer.DrawText(e.Graphics, "Stats", Md3Tokens.TitleSmall, new Rectangle(16, statsY, 200, 20), ThemeManager.Current.OnSurface, TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, $"{HistoryStore.Load().Count} Presets Scanned", Md3Tokens.LabelLarge, new Rectangle(16, statsY + 24, 328, 20), ThemeManager.Current.TertiaryContainer, TextFormatFlags.HorizontalCenter);
            };
            ThemeManager.ThemeChanged += () => { supportPane.BackColor = Color.FromArgb(217, ThemeManager.Current.Surface.R, ThemeManager.Current.Surface.G, ThemeManager.Current.Surface.B); supportPane.Invalidate(); };

            // Global drag-drop: allow dropping .ffx anywhere on the window; delegate to the active tab that supports file loading
            AllowDrop = true;
            DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0 && files[0].EndsWith(".ffx", System.StringComparison.OrdinalIgnoreCase))
                        e.Effect = DragDropEffects.Copy;
                }
            };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 0) return;
                var path = files[0];
                // Route to the visible tab that can handle it and record history (M3 Expressive real wiring)
                if (_listerTab.Visible) _listerTab.LoadFile(path);
                else if (_convertTab.Visible) _convertTab.LoadFileForConvert(path);
                else { _listerTab.LoadFile(path); ShowTab(0); }
                try { HistoryStore.Push(path); supportPane.Invalidate(); } catch { }
            };

            _navRail = new NavRail();
            _navRail.AddItem("Effect Lister", _listerTab, Md3Icons.Icon.EffectList);
            _navRail.AddItem("Plugin Profile", _profileTab, Md3Icons.Icon.Plugin);
            _navRail.AddItem("Convert", _convertTab, Md3Icons.Icon.Convert);
            _navRail.AddItem("Settings", _settingsTab, Md3Icons.Icon.Settings, pinned: true);
            _navRail.SelectionChanged += OnNavSelectionChanged;
            _navRail.FabClicked += () =>
            {
                using (var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    var path = dlg.FileName;
                    if (_listerTab.Visible) _listerTab.LoadFile(path);
                    else if (_convertTab.Visible) _convertTab.LoadFileForConvert(path);
                    else { _listerTab.LoadFile(path); ShowTab(0); }
                    try { HistoryStore.Push(path); } catch { }
                }
            };

            _titleBar = new Md3TitleBar(this, "FFX Compatibility Tool");

            _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // stitch secondary pane: hidden xl:block — show only on wide windows (1280+ shows 360 pane)
            void UpdateSupportPane()
            {
                bool show = Width >= 1280;
                supportPane.Visible = show;
                _body.ColumnStyles[2].Width = show ? 360 : 0;
            }
            // initial
            Load += (s, e) => UpdateSupportPane();
            Resize += (s, e) => UpdateSupportPane();

            _body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavRail.RailWidth));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            _body.Controls.Add(_navRail, 0, 0);
            _body.Controls.Add(_contentHost, 1, 0);
            _body.Controls.Add(supportPane, 2, 0);
            _navRail.Dock = DockStyle.Fill;

            // expressive footer: h-12 glass with Profile + DB version (stitch footer) — Dock.Fill inside TableLayout cell, not Dock.Bottom
            var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, ThemeManager.Current.SurfaceContainerLow.R, ThemeManager.Current.SurfaceContainerLow.G, ThemeManager.Current.SurfaceContainerLow.B) };
            footer.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(77, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                // dot + Profile
                e.Graphics.FillEllipse(new SolidBrush(Color.FromArgb(34, 197, 94)), 16, 19, 10, 10);
                TextRenderer.DrawText(e.Graphics, "Profile: Default", Md3Tokens.LabelMedium, new Rectangle(32, 0, 200, 48), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, "DB Version: v1.2.4", Md3Tokens.LabelMedium, new Rectangle(footer.Width - 180, 0, 160, 48), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            };
            ThemeManager.ThemeChanged += () => { footer.BackColor = Color.FromArgb(230, ThemeManager.Current.SurfaceContainerLow.R, ThemeManager.Current.SurfaceContainerLow.G, ThemeManager.Current.SurfaceContainerLow.B); footer.Invalidate(); };
            footer.Resize += (s, e) => footer.Invalidate();

            _root.Controls.Add(_titleBar, 0, 0);
            _root.Controls.Add(_body, 0, 1);
            _root.Controls.Add(footer, 0, 2);
            // need 3 rows now
            _root.RowCount = 3;
            _root.RowStyles.Clear();
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            Controls.Add(_root);

            ShowTab(0);

            // Re-theme the entire open window the moment the user changes
            // mode/palette in Settings — no restart required.
            ThemeManager.ThemeChanged += () =>
            {
                BackColor = ThemeManager.Current.Surface;
                _contentHost.BackColor = ThemeManager.Current.Surface;
                ThemeManager.ApplyToTree(this);
            };
        }

        void OnNavSelectionChanged(int index) => ShowTab(index);

        void ShowTab(int index)
        {
            _listerTab.Visible = index == 0;
            _profileTab.Visible = index == 1;
            _convertTab.Visible = index == 2;
            _settingsTab.Visible = index == 3;
        }

        void OnProfileChanged()
        {
            _listerTab.Refresh_();
            _convertTab.Refresh_();
        }

        // --- resize-by-edge-drag, the other half of the custom-chrome
        // implementation (drag-to-move lives in Md3TitleBar). Standard
        // WM_NCHITTEST override: tell Windows which edge/corner the
        // cursor is over so it can handle the actual resize natively —
        // same "let the OS do the real work" approach as the title bar's
        // drag handling, not a from-scratch resize implementation.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                var screenPoint = new Point(m.LParam.ToInt32());
                var clientPoint = PointToClient(screenPoint);

                bool left = clientPoint.X <= ResizeBorder;
                bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
                bool top = clientPoint.Y <= ResizeBorder;
                bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;

                if (top && left) m.Result = (System.IntPtr)13;      // HTTOPLEFT
                else if (top && right) m.Result = (System.IntPtr)14; // HTTOPRIGHT
                else if (bottom && left) m.Result = (System.IntPtr)16; // HTBOTTOMLEFT
                else if (bottom && right) m.Result = (System.IntPtr)17; // HTBOTTOMRIGHT
                else if (left) m.Result = (System.IntPtr)10;   // HTLEFT
                else if (right) m.Result = (System.IntPtr)11;  // HTRIGHT
                else if (top) m.Result = (System.IntPtr)12;    // HTTOP
                else if (bottom) m.Result = (System.IntPtr)15; // HTBOTTOM
                return;
            }
            base.WndProc(ref m);
        }
    }
}
