using System;
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

        // Header icon-button states: filter cycles All → missing-only →
        // compatible-only; sort toggles effect-name direction. Both were
        // previously decorative buttons with hover cursors but no handlers.
        int _filterMode; // 0 all, 1 missing only, 2 compatible only
        bool _sortDesc;
        Label _statusBarRight;

        // M3 Expressive Bold compact adaptive — whole section rebuilt 12-col
        public ListerTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = ThemeManager.Current.Surface;
            AllowDrop = true;
            DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0 && files[0].EndsWith(".ffx", StringComparison.OrdinalIgnoreCase))
                        e.Effect = DragDropEffects.Copy;
                }
            };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadFile(files[0]);
            };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- expressive heading: stitch h1 36px bold ---
            var heading = new Label { Text = "Effect Lister", Font = Md3Tokens.HeadlineLarge, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            ThemeManager.ThemeChanged += () => heading.ForeColor = ThemeManager.Current.OnSurface;

            // --- header row: FlowLayout wrap (M3 adaptive) so pill/icons never clip at 960px ---
            var headerRow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var openBtn = new Md3Button { Text = "Open .ffx file…", Icon = Md3Icons.Icon.FolderOpen, Width = 180, Margin = new Padding(0, 0, Md3Tokens.Space4, Md3Tokens.Space2) };
            openBtn.Click += (s, e) => OpenFile();

            // real design: an "info" icon + text inside a bordered pill,
            // not plain text next to the button
            var fileChip = new BufferedPanel { Height = 36, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
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
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Info, new Rectangle(Md3Tokens.Space3, 8, 18, 18), ThemeManager.Current.OnSurfaceVariant, 1.5f);
            };
            _fileChipLabel = new Label
            {
                Text = "No file loaded", ForeColor = ThemeManager.Current.OnSurfaceVariant, Font = Md3Tokens.BodyMedium,
                AutoSize = true, Location = new Point(38, 9),
            };
            fileChip.Controls.Add(_fileChipLabel);
            // Same formula everywhere (initial + every reload) so the chip
            // doesn't jump wider after a load.
            void UpdateChipWidth()
            {
                fileChip.Width = 38 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space3;
                fileChip.Invalidate();
            }
            UpdateChipWidth();
            ThemeManager.ThemeChanged += UpdateChipWidth;

            var iconButtonsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(Md3Tokens.Space4, 0, 0, Md3Tokens.Space2) };
            // Active state is shown by tinting the icon Primary (and the
            // status bar right label spells out the current mode).
            iconButtonsRow.Controls.Add(MakeIconButton(Md3Icons.Icon.Palette, "Cycle filter: all / missing only / compatible only", () =>
            {
                _filterMode = (_filterMode + 1) % 3;
                Refresh_();
            }, () => _filterMode != 0));
            iconButtonsRow.Controls.Add(MakeIconButton(Md3Icons.Icon.Convert, "Toggle sort direction (by effect name)", () =>
            {
                _sortDesc = !_sortDesc;
                Refresh_();
            }, () => _sortDesc));

            headerRow.Controls.Add(openBtn);
            headerRow.Controls.Add(fileChip);
            headerRow.Controls.Add(iconButtonsRow);

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
            // Row colors are baked per-item at Refresh_ time, so a theme
            // switch must REBUILD the rows (not just repaint) or they'd
            // keep the old theme's container colors.
            ThemeManager.ThemeChanged += () => Refresh_();
            // Effect Name / Plugin Vendor / Compatibility — matches the
            // real design's column set for this screen. No "Action" column:
            // that's specific to Convert's table (where rows can be
            // removed) — Effect Lister is read-only, intentionally
            // different from Convert's table despite the visual similarity.
            _list.Columns.Add("Effect Name", 260);
            _list.Columns.Add("Plugin Vendor", 320);
            _list.Columns.Add("Compatibility", 220);
            // M3 adaptive: responsive column weights (32/42/26) instead of fixed overflow at 800px
            _list.Resize += (s, e) =>
            {
                if (_list.Width > 100)
                {
                    int w = _list.Width - 24; // scrollbar
                    _list.Columns[0].Width = Math.Max(140, w * 32 / 100);
                    _list.Columns[1].Width = Math.Max(180, w * 42 / 100);
                    _list.Columns[2].Width = Math.Max(140, w * 26 / 100);
                }
            };

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
            _statusBarRight = statusBarRight;

            statusBar.Controls.Add(_statusBarLeft, 0, 0);
            statusBar.Controls.Add(statusBarRight, 1, 0);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(headerRow, 0, 1);
            root.Controls.Add(_listHost, 0, 2);
            root.Controls.Add(statusBar, 0, 3);
            Controls.Add(root);
        }

        Control MakeIconButton(Md3Icons.Icon icon, string tooltip, Action onClick = null, Func<bool> isActive = null)
        {
            var btn = new BufferedPanel { Width = 36, Height = 36, Margin = new Padding(Md3Tokens.Space1), Cursor = onClick != null ? Cursors.Hand : Cursors.Default };
            var tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            if (onClick != null) btn.Click += (s, e) => onClick();
            bool _hover = false;
            btn.MouseEnter += (s, e) => { if (onClick != null) { _hover = true; btn.Invalidate(); } };
            btn.MouseLeave += (s, e) => { _hover = false; btn.Invalidate(); };
            btn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool active = isActive != null && isActive();
                if (_hover)
                {
                    using (var path = RoundedRect(new Rectangle(0, 0, 35, 35), Md3Tokens.CornerSmall))
                    using (var brush = new SolidBrush(Color.FromArgb(Md3Tokens.HoverStateAlpha, ThemeManager.Current.OnSurfaceVariant)))
                        e.Graphics.FillPath(brush, path);
                }
                var color = active ? ThemeManager.Current.Primary : ThemeManager.Current.OnSurfaceVariant;
                Md3Icons.Draw(e.Graphics, icon, new Rectangle(8, 8, 20, 20), color, active ? 1.9f : 1.6f);
            };
            return btn;
        }

        Panel BuildEmptyState()
        {
            // stitch spec: bg-surface-container-low/80 border-2 dashed rounded-[32px] p-16 min-h 500 hover border-primary/50
            var container = new Panel { Dock = DockStyle.Fill, Visible = true, AutoScroll = false };
            container.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var margin = 32;
                var bounds = new Rectangle(margin, margin, container.Width - margin * 2, Math.Min(420, Math.Max(240, container.Height - margin * 2)));
                using (var pen = new Pen(Color.FromArgb(120, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B), 1.8f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var path = RoundedRect(bounds, Md3Tokens.CornerExtraLarge)) // 24 => 32px visual
                {
                    using (var brush = new SolidBrush(Color.FromArgb(20, ThemeManager.Current.SurfaceContainer.R, ThemeManager.Current.SurfaceContainer.G, ThemeManager.Current.SurfaceContainer.B)))
                        e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                int cy = bounds.Top + bounds.Height / 2 - 80;
                // circular icon bg like stitch w-24 h-24 rounded-full
                var iconBg = new Rectangle(bounds.X + bounds.Width / 2 - 48, cy - 36, 96, 96);
                using (var path = RoundedRect(iconBg, 48))
                using (var brush = new SolidBrush(Color.FromArgb(40, ThemeManager.Current.SurfaceContainerHigh.R, ThemeManager.Current.SurfaceContainerHigh.G, ThemeManager.Current.SurfaceContainerHigh.B)))
                    e.Graphics.FillPath(brush, path);
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.FolderOpen, new Rectangle(bounds.X + bounds.Width / 2 - 28, cy - 20, 56, 56), ThemeManager.Current.OnSurfaceVariant, 1.6f);

                TextRenderer.DrawText(e.Graphics, "No preset loaded", Md3Tokens.HeadlineSmall,
                    new Rectangle(bounds.X, cy + 68, bounds.Width, 30), ThemeManager.Current.OnSurface, TextFormatFlags.HorizontalCenter);

                var msgRect = new Rectangle(bounds.X + bounds.Width / 2 - 240, cy + 102, 480, 50);
                TextRenderer.DrawText(e.Graphics,
                    "Open a .ffx file to see its effects and check them against your plugin profile. You can also drag and drop a file directly into this workspace.",
                    Md3Tokens.BodyMedium, msgRect, ThemeManager.Current.OnSurfaceVariant,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };

            // compact CTA for adaptive density
            var ctaBtn = new Md3Button { Text = "Open .ffx file", Icon = Md3Icons.Icon.FolderOpen, Width = 160, Height = 40 };
            ctaBtn.Click += (s, e) => OpenFile();
            container.Controls.Add(ctaBtn);

            var cardsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.None };
            cardsRow.Controls.Add(MakeDisabledActionCard(Md3Icons.Icon.Check, "Auto-Analyze", "Verify compatibility on load", "Not yet implemented"));
            var recent = HistoryStore.Load().FirstOrDefault();
            bool hasRecent = recent != null;
            var recentCard = new BufferedPanel { Width = 220, Height = 64, Margin = new Padding(Md3Tokens.Space2), Enabled = hasRecent, Cursor = hasRecent ? Cursors.Hand : Cursors.Default };
            if (!hasRecent) new ToolTip().SetToolTip(recentCard, "No recent files yet");
            else new ToolTip().SetToolTip(recentCard, $"Open {recent.fileName}");
            recentCard.Paint += (s2, e2) =>
            {
                e2.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 0, recentCard.Width - 1, recentCard.Height - 1);
                using (var path = RoundedRect(bounds, Md3Tokens.CornerMedium))
                using (var brush = new SolidBrush(hasRecent ? ThemeManager.Current.SurfaceContainer : Color.FromArgb(180, ThemeManager.Current.SurfaceContainer.R, ThemeManager.Current.SurfaceContainer.G, ThemeManager.Current.SurfaceContainer.B)))
                using (var pen = new Pen(hasRecent ? ThemeManager.Current.OutlineVariant : Color.FromArgb(120, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                {
                    e2.Graphics.FillPath(brush, path);
                    e2.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e2.Graphics, Md3Icons.Icon.History, new Rectangle(16, 20, 22, 22), hasRecent ? ThemeManager.Current.Primary : ThemeManager.Current.Outline, 1.6f);
                TextRenderer.DrawText(e2.Graphics, "Recent Files", Md3Tokens.LabelMedium, new Rectangle(50, 12, recentCard.Width - 60, 18), hasRecent ? ThemeManager.Current.OnSurface : ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.Left);
                string sub = hasRecent ? recent.fileName : "View last 5 analyzed presets";
                TextRenderer.DrawText(e2.Graphics, sub, Md3Tokens.BodySmall, new Rectangle(50, 32, recentCard.Width - 60, 18), ThemeManager.Current.OutlineVariant, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            if (hasRecent) recentCard.Click += (s2, e2) => LoadFile(recent.path); // LoadFile handles its own errors
            cardsRow.Controls.Add(recentCard);
            container.Controls.Add(cardsRow);
            container.Resize += (s, e) =>
            {
                ctaBtn.Location = new Point((container.Width - ctaBtn.Width) / 2, container.Height / 2 + 90);
                cardsRow.Location = new Point((container.Width - cardsRow.Width) / 2, container.Height / 2 + 150);
            };

            return container;
        }

        Control MakeDisabledActionCard(Md3Icons.Icon icon, string title, string subtitle, string tooltip)
        {
            var card = new BufferedPanel { Width = 180, Height = 56, Margin = new Padding(Md3Tokens.Space2), Enabled = false };
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
            try
            {
                _fileChipLabel.Text = Path.GetFileName(path);
                SetChipWidth();
                var data = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(data);
                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));
                Refresh_();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to read '{Path.GetFileName(path)}':\n{ex.Message}", "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _fileChipLabel.Text = "No file loaded";
                SetChipWidth();
                _currentEffects = new System.Collections.Generic.List<Pipeline.EffectInfo>();
                Refresh_();
            }
        }

        void SetChipWidth()
        {
            // Same formula as the initial layout — the chip previously used
            // a different trailing gap after loads, so it visibly jumped.
            if (_fileChipLabel.Parent is Panel chip)
            {
                chip.Width = 38 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space3;
                chip.Invalidate();
            }
        }

        public void Refresh_()
        {
            var table = PluginLookup.LoadTable();
            var realEffects = _currentEffects.Where(e => !e.IsSentinel).ToList();

            // Sort by effect name (direction toggled from the header button)
            System.Collections.Generic.IEnumerable<Pipeline.EffectInfo> ordered = _sortDesc
                ? realEffects.OrderByDescending(e => e.MatchName, StringComparer.OrdinalIgnoreCase)
                : realEffects.OrderBy(e => e.MatchName, StringComparer.OrdinalIgnoreCase);

            // Filter (cycled from the header button): 0 all, 1 missing
            // plugins only, 2 compatible only (owned or native/unknown vendor).
            if (_filterMode == 1)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) == false);
            else if (_filterMode == 2)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) != false);

            _list.Items.Clear();
            foreach (var eff in ordered)
            {
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);

                string status;
                if (match.Vendor == null) status = "Unknown plugin";
                else if (owned == false) status = "Likely to fail";
                else if (owned == true) status = "Compatible";
                else status = "Native";

                var item = new ListViewItem(new[] { eff.MatchName, $"{match.Vendor ?? "?"} — {match.Suite ?? "?"}", status });
                // Row colors are resolved FRESH here on every rebuild — the
                // old code baked theme colors into items and only reset them
                // on the next file load, leaving stale-theme rows after a
                // theme switch.
                if (owned == false) item.BackColor = ThemeManager.Current.ErrorContainer;
                else if (match.Vendor == null) item.BackColor = ThemeManager.Current.TertiaryContainer;
                _list.Items.Add(item);
            }

            bool hasContent = realEffects.Count > 0;
            _list.Visible = hasContent;
            _emptyState.Visible = !hasContent;

            // Status bar right label reflects the active filter/sort mode so
            // the header buttons' state is always readable at a glance.
            string filterText = _filterMode == 0 ? "All" : _filterMode == 1 ? "Missing only" : "Compatible only";
            string sortText = _sortDesc ? "Z→A" : "A→Z";
            _statusBarRight.Text = hasContent ? $"{filterText} · {sortText}" : "Ready";

            RefreshStatusBar();
        }
    }
}
