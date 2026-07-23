using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Rebuilt to match the user's real design (found in Settings.zip,
    /// which — despite the filename — actually contains the Effect
    /// Lister screen; the 4 uploaded zips' names don't match their
    /// contents 1:1, confirmed by opening each screen.png directly
    /// rather than trusting the filenames).
    ///
    /// Matches the real code.html precisely for: the "info + No file
    /// loaded" pill chip (not plain text), filter/sort icon buttons,
    /// the dashed-border empty-state workspace with its exact copy, and
    /// the bottom status bar layout. Two things adapted rather than
    /// copied verbatim — see comments at each: the empty state's two
    /// quick-action cards (Auto-Analyze / Recent Files) aren't real
    /// features yet, so they're shown disabled rather than faked as
    /// working; and the status bar shows real app data instead of the
    /// mockup's placeholder text ("Plugin DB: v2.4.1").
    /// </summary>
    public class ListerTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Label _fileChipLabel;
        readonly ListView _list;
        readonly Panel _emptyState;
        readonly Panel _listHost;
        readonly Label _statusBarLeft;
        System.Collections.Generic.List<Pipeline.EffectInfo> _currentEffects = new System.Collections.Generic.List<Pipeline.EffectInfo>();

        public ListerTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = ThemeManager.Current.Surface;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- header row: open button, "info + no file loaded" pill, filter/sort icons ---
            var headerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var openBtn = new Md3Button { Text = "Open .ffx file…", Icon = Md3Icons.Icon.FolderOpen, Width = 180, Margin = new Padding(0, 0, Md3Tokens.Space4, Md3Tokens.Space4) };
            openBtn.Click += (s, e) => OpenFile();

            // real design: an "info" icon + text inside a bordered pill,
            // not plain text next to the button
            var fileChip = new Panel { Height = 36, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            fileChip.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 0, fileChip.Width - 1, fileChip.Height - 1);
                using (var path = RoundedRect(bounds, Md3Tokens.CornerSmall))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Warning, new Rectangle(Md3Tokens.Space3, 8, 18, 18), ThemeManager.Current.OnSurfaceVariant, 1.5f);
            };
            _fileChipLabel = new Label
            {
                Text = "No file loaded", ForeColor = ThemeManager.Current.OnSurfaceVariant, Font = Md3Tokens.BodyMedium,
                AutoSize = true, Location = new Point(38, 9),
            };
            fileChip.Controls.Add(_fileChipLabel);
            fileChip.Width = 38 + TextRenderer.MeasureText("No file loaded", Md3Tokens.BodyMedium).Width + Md3Tokens.Space3;

            var iconButtonsRow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            iconButtonsRow.Controls.Add(MakeIconButton(Md3Icons.Icon.Convert, "Sort"));  // closest existing glyph to "sort" — real Material Symbols "sort" comes in Phase 7
            iconButtonsRow.Controls.Add(MakeIconButton(Md3Icons.Icon.Palette, "Filter")); // placeholder glyph until Phase 7's real icon font

            headerRow.Controls.Add(openBtn, 0, 0);
            headerRow.Controls.Add(fileChip, 1, 0);
            headerRow.Controls.Add(iconButtonsRow, 2, 0);

            // --- data table ---
            _list = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = false,
                Dock = DockStyle.Fill, Font = Md3Tokens.BodyMedium, BackColor = ThemeManager.Current.Surface,
                BorderStyle = BorderStyle.FixedSingle, OwnerDraw = true,
            };
            _list.DrawColumnHeader += (s, e) =>
            {
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerHigh))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var textRect = new Rectangle(e.Bounds.X + Md3Tokens.Space2, e.Bounds.Y, e.Bounds.Width - Md3Tokens.Space4, e.Bounds.Height);
                // spec's table headers: label-md, uppercase
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), Md3Tokens.LabelMedium, textRect, ThemeManager.Current.OnSurfaceVariant,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _list.DrawSubItem += (s, e) =>
            {
                var rowBg = e.Item.Selected
                    ? ThemeManager.Current.PrimaryContainer
                    : e.Item.BackColor != _list.BackColor ? e.Item.BackColor : ThemeManager.Current.Surface;
                using (var brush = new SolidBrush(rowBg))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                var textColor = e.Item.Selected ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurface;
                var textRect = new Rectangle(e.Bounds.X + Md3Tokens.Space2, e.Bounds.Y, e.Bounds.Width - Md3Tokens.Space4, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Md3Tokens.BodyMedium, textRect, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            _list.DrawItem += (s, e) => { };
            ThemeManager.ThemeChanged += () => { _list.BackColor = ThemeManager.Current.Surface; _list.Invalidate(true); };
            // Effect Name / Plugin Vendor / Compatibility — matches the
            // real design's column set for this screen. No "Action" column:
            // that's specific to Convert's table (where rows can be
            // removed) — Effect Lister is read-only, intentionally
            // different from Convert's table despite the visual similarity.
            _list.Columns.Add("Effect Name", 260);
            _list.Columns.Add("Plugin Vendor", 320);
            _list.Columns.Add("Compatibility", 220);

            _emptyState = BuildEmptyState();
            _listHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, Md3Tokens.Space2, 0, Md3Tokens.Space2) };
            _listHost.Controls.Add(_list);
            _listHost.Controls.Add(_emptyState);
            _list.Dock = DockStyle.Fill;
            _list.Visible = false;

            // --- bottom status bar ---
            var statusBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, Md3Tokens.Space4, 0, 0) };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _statusBarLeft = new Label { Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true };
            RefreshStatusBar();

            var statusBarRight = new Label
            {
                Text = "Ready", Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.Outline,
                AutoSize = true, Anchor = AnchorStyles.Right, Dock = DockStyle.Right,
            };

            statusBar.Controls.Add(_statusBarLeft, 0, 0);
            statusBar.Controls.Add(statusBarRight, 1, 0);

            root.Controls.Add(headerRow, 0, 0);
            root.Controls.Add(_listHost, 0, 1);
            root.Controls.Add(statusBar, 0, 2);
            Controls.Add(root);
        }

        Control MakeIconButton(Md3Icons.Icon icon, string tooltip)
        {
            var btn = new Panel { Width = 36, Height = 36, Margin = new Padding(Md3Tokens.Space1), Cursor = Cursors.Hand };
            var tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            btn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Md3Icons.Draw(e.Graphics, icon, new Rectangle(8, 8, 20, 20), ThemeManager.Current.OnSurfaceVariant, 1.6f);
            };
            return btn;
        }

        Panel BuildEmptyState()
        {
            var container = new Panel { Dock = DockStyle.Fill, Visible = true };
            container.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var margin = 40;
                var bounds = new Rectangle(margin, margin, container.Width - margin * 2, container.Height - margin * 2);
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var path = RoundedRect(bounds, Md3Tokens.CornerLarge))
                    e.Graphics.DrawPath(pen, path);

                int cy = bounds.Top + 90;
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.FolderOpen, new Rectangle(bounds.X + bounds.Width / 2 - 32, cy - 32, 64, 64), ThemeManager.Current.Outline, 2.0f);

                TextRenderer.DrawText(e.Graphics, "No preset loaded", Md3Tokens.HeadlineSmall,
                    new Rectangle(bounds.X, cy + 48, bounds.Width, 30), ThemeManager.Current.OnSurface, TextFormatFlags.HorizontalCenter);

                var msgRect = new Rectangle(bounds.X + bounds.Width / 2 - 220, cy + 82, 440, 50);
                TextRenderer.DrawText(e.Graphics,
                    "Open a .ffx file to see its effects and check them against your plugin profile. You can also drag and drop a file directly into this workspace.",
                    Md3Tokens.BodyMedium, msgRect, ThemeManager.Current.OnSurfaceVariant,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };

            // Two quick-action cards from the real design (Auto-Analyze,
            // Recent Files) — shown disabled with a tooltip rather than
            // wired up, since neither is an implemented feature yet.
            // Showing them as working would misrepresent what the app
            // actually does.
            var cardsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.None };
            cardsRow.Controls.Add(MakeDisabledActionCard(Md3Icons.Icon.Check, "Auto-Analyze", "Verify compatibility on load", "Not yet implemented"));
            cardsRow.Controls.Add(MakeDisabledActionCard(Md3Icons.Icon.Warning, "Recent Files", "View last 5 analyzed presets", "Not yet implemented"));
            container.Controls.Add(cardsRow);
            container.Resize += (s, e) =>
            {
                cardsRow.Location = new Point((container.Width - cardsRow.Width) / 2, container.Height / 2 + 90);
            };

            return container;
        }

        Control MakeDisabledActionCard(Md3Icons.Icon icon, string title, string subtitle, string tooltip)
        {
            var card = new Panel { Width = 220, Height = 64, Margin = new Padding(Md3Tokens.Space2), Enabled = false };
            var tip = new ToolTip();
            tip.SetToolTip(card, tooltip);
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var path = RoundedRect(bounds, Md3Tokens.CornerMedium))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, icon, new Rectangle(16, 20, 22, 22), ThemeManager.Current.Outline, 1.6f);
                TextRenderer.DrawText(e.Graphics, title, Md3Tokens.LabelMedium, new Rectangle(50, 12, card.Width - 60, 18), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, subtitle, Md3Tokens.BodySmall, new Rectangle(50, 32, card.Width - 60, 18), ThemeManager.Current.OutlineVariant, TextFormatFlags.Left);
            };
            return card;
        }

        void RefreshStatusBar()
        {
            int vendorCount = _profile.OwnedVendors.Count;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            // Real data instead of the mockup's placeholder text
            // ("Plugin DB: v2.4.1 (Stable)") — this app doesn't have a
            // versioned plugin database, so showing a fabricated one
            // would be misleading. Shows the actual plugin profile state
            // and real app version instead.
            _statusBarLeft.Text = $"Plugin Profile: {vendorCount} vendor(s) selected     ·     FFX Compatibility Tool v{version}";
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

        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                LoadFile(dlg.FileName);
            }
        }

        public void LoadFile(string path)
        {
            _fileChipLabel.Text = Path.GetFileName(path);
            var data = File.ReadAllBytes(path);
            _currentEffects = Pipeline.ListEffects(data);
            Refresh_();
        }

        public void Refresh_()
        {
            var table = PluginLookup.LoadTable();
            var realEffects = _currentEffects.Where(e => !e.IsSentinel).ToList();

            _list.Items.Clear();
            foreach (var eff in realEffects)
            {
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);

                string status;
                if (match.Vendor == null) status = "Unknown plugin";
                else if (owned == false) status = "Likely to fail";
                else if (owned == true) status = "Compatible";
                else status = "Native";

                var item = new ListViewItem(new[] { eff.MatchName, $"{match.Vendor ?? "?"} — {match.Suite ?? "?"}", status });
                if (owned == false) item.BackColor = ThemeManager.Current.ErrorContainer;
                else if (match.Vendor == null) item.BackColor = ThemeManager.Current.TertiaryContainer;
                _list.Items.Add(item);
            }

            bool hasContent = realEffects.Count > 0;
            _list.Visible = hasContent;
            _emptyState.Visible = !hasContent;
            RefreshStatusBar();
        }
    }
}
