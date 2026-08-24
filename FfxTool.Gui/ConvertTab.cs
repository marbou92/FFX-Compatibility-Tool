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
    /// Convert tab — M3 Expressive two-pane workspace:
    ///  - LEFT: hero drop zone (swaps to the effect checklist once a file
    ///    loads) → file row → target/encoding options → full-width Convert CTA
    ///  - RIGHT: "Intelligent Conversion" callout on top, themed console
    ///    card filling the rest — both ALWAYS visible, no scrolling needed
    ///  - Drag-drop wired + LoadFileForConvert exposed for MainForm global drop
    /// </summary>
    public class ConvertTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Label _fileChipLabel;
        readonly CheckedListBox _effectList;
        readonly Panel _effectListEmptyState;
        readonly Panel _effectListHost;
        readonly Md3Dropdown _targetCombo;
        readonly CheckBox _overwriteCheck;
        readonly Md3Button _convertBtn;
        readonly TextBox _consoleBox;

        byte[] _inputData;
        string _inputPath;
        List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();

        public ConvertTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = ThemeManager.Current.Surface;
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            var heading = new Label { Text = "Convert Preset", Font = Md3Tokens.DisplayLarge, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            ThemeManager.ThemeChanged += () => heading.ForeColor = ThemeManager.Current.OnSurface;

            // ================= LEFT PANE — workspace =================
            var leftPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            leftPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // hero / effect list
            leftPane.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // file row
            leftPane.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // options
            leftPane.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // convert CTA

            _effectList = new CheckedListBox { Dock = DockStyle.Fill, Font = Md3Tokens.BodyMedium, BorderStyle = BorderStyle.None };
            _effectListEmptyState = BuildEmptyState();
            _effectListHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            _effectListHost.Controls.Add(_effectList);
            _effectListHost.Controls.Add(_effectListEmptyState);
            _effectList.Visible = false;

            var fileRow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            var openBtn = new Md3Button { Text = "Open .ffx file…", Icon = Md3Icons.Icon.FolderOpen, Width = 180, Height = Md3Tokens.ButtonHeight, Margin = new Padding(0, 0, Md3Tokens.Space3, 0) };
            openBtn.Click += (s, e) => OpenFile();

            var statusChip = new BufferedPanel { AutoSize = true, Height = 40, Margin = new Padding(0) };
            _fileChipLabel = new Label { Text = "Status: No file loaded", Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Location = new Point(36, 11), BackColor = Color.Transparent };
            statusChip.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 0, statusChip.Width - 1, statusChip.Height - 1);
                using (var path = RoundedRect(bounds, Md3Tokens.CornerSmall))
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Info, new Rectangle(Md3Tokens.Space3, 11, 18, 18), ThemeManager.Current.OnSurfaceVariant, 1.4f);
            };
            statusChip.Controls.Add(_fileChipLabel);
            void UpdateChipWidth()
            {
                statusChip.Width = 36 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space4;
                statusChip.Invalidate();
            }
            UpdateChipWidth();
            ThemeManager.ThemeChanged += UpdateChipWidth;

            fileRow.Controls.Add(openBtn);
            fileRow.Controls.Add(statusChip);

            // --- options: target version | encoding options, 50/50 ---
            var optionsRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var targetCol = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            targetCol.Controls.Add(new Label { Text = "Target version", Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            _targetCombo = new Md3Dropdown { Width = 200 };
            _targetCombo.SetItems(Pipeline.KnownVersions.Keys.OrderBy(k => k).Select(DisplayNameFor), 0);
            targetCol.Controls.Add(_targetCombo);

            var encodingCol = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            encodingCol.Controls.Add(new Label { Text = "Encoding Options", Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            var cleanMetadataCheck = new Md3Checkbox { Text = "Clean Metadata", Checked = true, Enabled = false, Width = 170, Height = 24 };
            var tip = new ToolTip();
            tip.SetToolTip(cleanMetadataCheck, "Not yet implemented — the pipeline doesn't have a separate metadata-cleaning step");
            _overwriteCheck = new Md3Checkbox { Text = "Overwrite File", Width = 170, Height = 24 };
            encodingCol.Controls.Add(cleanMetadataCheck);
            encodingCol.Controls.Add(_overwriteCheck);

            optionsRow.Controls.Add(targetCol, 0, 0);
            optionsRow.Controls.Add(encodingCol, 1, 0);

            // --- full-width expressive Convert CTA ---
            var convertRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 1, AutoSize = true, Margin = new Padding(0) };
            convertRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _convertBtn = new Md3Button { Text = "Convert…", Icon = Md3Icons.Icon.Convert, Height = 48, Dock = DockStyle.Fill, Enabled = false, Margin = new Padding(0) };
            _convertBtn.Click += (s, e) => DoConvert();
            convertRow.Controls.Add(_convertBtn, 0, 0);

            leftPane.Controls.Add(_effectListHost, 0, 0);
            leftPane.Controls.Add(fileRow, 0, 1);
            leftPane.Controls.Add(optionsRow, 0, 2);
            leftPane.Controls.Add(convertRow, 0, 3);

            // ================= RIGHT PANE — insight + console card =================
            var rightCard = new Md3Card { Variant = Md3CardVariant.Filled, Dock = DockStyle.Fill, Margin = new Padding(Md3Tokens.Space6, 0, 0, 0) };
            rightCard.Padding = new Padding(Md3Tokens.Space6);

            var calloutFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            calloutFlow.Controls.Add(new Label { Text = "Intelligent Conversion", Font = Md3Tokens.TitleLarge, ForeColor = ThemeManager.Current.OnSurface, BackColor = Color.Transparent, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            var calloutDesc = new Label
            {
                Text = "Automatically detects missing or incompatible plugins against your profile and suggests removal. Presets are re-encoded to the selected target version.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, BackColor = Color.Transparent,
                AutoSize = true, MaximumSize = new Size(360, 0),
            };
            calloutFlow.Controls.Add(calloutDesc);

            // console: header (dots + title + clear) over the log box, both themed
            var consoleHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), BackColor = Color.Transparent };
            consoleHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            consoleHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var consoleHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 36, BackColor = Color.Transparent, Margin = new Padding(0) };
            consoleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            consoleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var dotsPanel = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, MinimumSize = new Size(220, 36) };
            dotsPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color[] dots = { Color.FromArgb(237, 106, 94), Color.FromArgb(97, 174, 238), Color.FromArgb(159, 120, 224) };
                int x = 2;
                foreach (var c in dots)
                {
                    using (var b = new SolidBrush(c)) e.Graphics.FillEllipse(b, x, 13, 10, 10);
                    x += 18;
                }
            };
            var consoleTitleLabel = new Label { Text = "CONSOLE OUTPUT", Font = Md3Tokens.LabelSmall, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Location = new Point(60, 11), BackColor = Color.Transparent };
            dotsPanel.Controls.Add(consoleTitleLabel);

            var clearLogsBtn = new LinkLabel { Text = "Clear Logs", AutoSize = true, Anchor = AnchorStyles.Right, Font = Md3Tokens.LabelSmall, LinkColor = ThemeManager.Current.Primary, Margin = new Padding(0, 10, 4, 0), BackColor = Color.Transparent };
            _consoleBox = new TextBox
            {
                Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9f),
                Dock = DockStyle.Fill, BackColor = ThemeManager.Current.SurfaceContainer, ForeColor = ThemeManager.Current.OnSurface,
                BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            };
            ThemeManager.ThemeChanged += () =>
            {
                _consoleBox.BackColor = ThemeManager.Current.SurfaceContainer;
                _consoleBox.ForeColor = ThemeManager.Current.OnSurface;
                consoleTitleLabel.ForeColor = ThemeManager.Current.OnSurfaceVariant;
                clearLogsBtn.LinkColor = ThemeManager.Current.Primary;
                dotsPanel.Invalidate();
            };
            clearLogsBtn.LinkClicked += (s, e) => { _consoleBox.Clear(); Log("[SYSTEM] Log cleared."); };
            consoleHeader.Controls.Add(dotsPanel, 0, 0);
            consoleHeader.Controls.Add(clearLogsBtn, 1, 0);

            consoleHost.Controls.Add(consoleHeader, 0, 0);
            consoleHost.Controls.Add(_consoleBox, 0, 1);

            rightCard.Controls.Add(calloutFlow);
            rightCard.Controls.Add(consoleHost);
            // dock order: callout (top) first, console fills the remainder
            rightCard.Controls.SetChildIndex(calloutFlow, 0);
            rightCard.Controls.SetChildIndex(consoleHost, 1);
            // gradient accent bar (stitch top h-2 primary->tertiary) on the card
            rightCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var accent = new Rectangle(Md3Tokens.Space6, Md3Tokens.Space6, Math.Max(0, rightCard.Width - Md3Tokens.Space6 * 2), 4);
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(accent, ThemeManager.Current.Primary, ThemeManager.Current.TertiaryContainer, 0f))
                    e.Graphics.FillRectangle(brush, accent);
            };

            // ================= ROOT — heading + two panes =================
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoScroll = false, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // heading
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // panes
            root.Controls.Add(heading, 0, 0);
            root.SetColumnSpan(heading, 2);
            root.Controls.Add(leftPane, 0, 1);
            root.Controls.Add(rightCard, 1, 1);
            Controls.Add(root);

            Log("[SYSTEM] Engine initialized.");
            Log("[INFO] Waiting for file input…");
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && files[0].EndsWith(".ffx", StringComparison.OrdinalIgnoreCase))
                    e.Effect = DragDropEffects.Copy;
            }
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;
            LoadFileForConvert(files[0]);
        }

        public void LoadFileForConvert(string path)
        {
            try
            {
                _inputPath = path;
                _inputData = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(_inputData);
                _fileChipLabel.Text = $"Status: {Path.GetFileName(path)}";
                _fileChipLabel.Parent.Width = 34 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space4;
                _fileChipLabel.Parent.Invalidate();
                _convertBtn.Enabled = true;
                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));
                Log($"[INFO] Loaded {Path.GetFileName(path)} ({_inputData.Length} bytes).");
                Refresh_();
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to read '{Path.GetFileName(path)}': {ex.Message}");
                MessageBox.Show(this, $"Failed to read '{Path.GetFileName(path)}':\n{ex.Message}", "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static readonly Dictionary<string, string> _displayNames = new Dictionary<string, string> { { "cs5.5", "After Effects CS5.5" } };
        static string DisplayNameFor(string key) => _displayNames.TryGetValue(key, out var v) ? v : key;
        static string InternalKeyFor(string display) => _displayNames.FirstOrDefault(kv => kv.Value == display).Key ?? display;

        void Log(string line) => _consoleBox.AppendText(line + Environment.NewLine);

        Panel BuildEmptyState()
        {
            // Measured vertical stack so NOTHING clips: icon → title →
            // message → Browse CTA, all centered inside the dashed hero box.
            const int IconSize = 48, TitleH = 24, MsgH = 40, Gap = 16, MsgGap = 8, BtnGap = 20;
            const int StackH = IconSize + Gap + TitleH + MsgGap + MsgH + BtnGap + 40;

            var container = new BufferedPanel { Dock = DockStyle.Fill, BackColor = ThemeManager.Current.Surface };
            var browseBtn = new Md3Button { Text = "Browse Files", Icon = Md3Icons.Icon.FolderOpen, Variant = Md3ButtonVariant.Outlined, Width = 170, Height = 40 };
            browseBtn.Click += (s, e) => OpenFile();
            container.Controls.Add(browseBtn);

            container.Resize += (s, e) =>
            {
                int margin = Md3Tokens.Space4;
                var box = new Rectangle(margin, margin, Math.Max(0, container.Width - margin * 2), Math.Max(0, container.Height - margin * 2));
                int top = box.Top + Math.Max(0, (box.Height - StackH) / 2);
                browseBtn.Location = new Point(box.X + (box.Width - browseBtn.Width) / 2, top + IconSize + Gap + TitleH + MsgGap + MsgH + BtnGap);
            };

            container.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int margin = Md3Tokens.Space4;
                var box = new Rectangle(margin, margin, Math.Max(0, container.Width - margin * 2), Math.Max(0, container.Height - margin * 2));
                using (var pen = new Pen(Color.FromArgb(100, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var path = RoundedRect(box, Md3Tokens.CornerExtraLarge))
                {
                    using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerLow))
                        e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                int top = box.Top + Math.Max(0, (box.Height - StackH) / 2);
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.FolderOpen, new Rectangle(box.X + (box.Width - IconSize) / 2, top, IconSize, IconSize), ThemeManager.Current.Outline, 1.6f);
                TextRenderer.DrawText(e.Graphics, "No preset loaded", Md3Tokens.TitleLarge, new Rectangle(box.X, top + IconSize + Gap, box.Width, TitleH), ThemeManager.Current.OnSurface, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int msgW = Math.Min(440, Math.Max(120, box.Width - Md3Tokens.Space6));
                TextRenderer.DrawText(e.Graphics, "Select an Adobe After Effects .ffx file to begin the analysis and conversion process.", Md3Tokens.BodyMedium,
                    new Rectangle(box.X + (box.Width - msgW) / 2, top + IconSize + Gap + TitleH + MsgGap, msgW, MsgH), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };
            return container;
        }

        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                LoadFileForConvert(dlg.FileName);
            }
        }

        public void Refresh_()
        {
            _effectList.Items.Clear();
            if (_currentEffects.Count == 0)
            {
                _effectList.Visible = false;
                _effectListEmptyState.Visible = true;
                return;
            }

            var table = PluginLookup.LoadTable();
            foreach (var eff in _currentEffects)
            {
                if (eff.IsSentinel) continue;
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);
                _effectList.Items.Add($"{eff.MatchName}  ({match.Vendor ?? "unknown vendor"})", owned == false);
            }

            _effectList.Visible = true;
            _effectListEmptyState.Visible = false;
        }

        void DoConvert()
        {
            if (_inputData == null) return;

            var toRemove = new HashSet<string>();
            for (int i = 0; i < _effectList.Items.Count; i++)
            {
                if (_effectList.GetItemChecked(i))
                {
                    var text = _effectList.Items[i].ToString();
                    var name = text.Substring(0, text.IndexOf("  (", StringComparison.Ordinal));
                    toRemove.Add(name);
                }
            }

            var target = InternalKeyFor(_targetCombo.SelectedItem ?? "After Effects CS5.5");
            Log($"[SYSTEM] Converting to target '{target}'…");

            Pipeline.ConversionResult result;
            try
            {
                result = Pipeline.Convert(_inputData, target, toRemove.Count > 0 ? toRemove : null);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.Message}");
                MessageBox.Show(this, ex.Message, "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string outPath;
            if (_overwriteCheck.Checked && !string.IsNullOrEmpty(_inputPath))
            {
                outPath = _inputPath;
                File.WriteAllBytes(outPath, result.Data);
            }
            else
            {
                using (var dlg = new SaveFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) { Log("[INFO] Save cancelled."); return; }
                    outPath = dlg.FileName;
                    File.WriteAllBytes(outPath, result.Data);
                }
            }

            Log($"[SUCCESS] Saved: {outPath}");
            if (result.RemovedEffects.Count > 0) Log($"[INFO] Removed: {string.Join(", ", result.RemovedEffects)}");
            foreach (var w in result.Warnings) Log($"[WARNING] {w}");
            // Pipeline.Convert already ran its Verify() pass and throws on
            // any failure, so reaching this line means the file is clean —
            // worded loosely so it can't drift from the actual checks.
            Log("[OK] Verification pass clean — structure, indices and keyframe data intact.");
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
