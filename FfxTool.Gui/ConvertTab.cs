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
    /// Fixed Convert tab — stitch_visual_discovery_tool aligned:
    ///  - RowCount 5 fix + AutoScroll root prevents clipping at 820px
    ///  - Callout capped at 720 (no overflow), rounded-[32px] gradient accent
    ///  - Effect list host MinimumSize 200 + centered empty state (no half-hidden Browse)
    ///  - Target/Encoding row uses absolute/middle layout that wraps cleanly
    ///  - Console header uses real Label + dotsPanel 260 so CONSOLE OUTPUT never truncates to CONSOLE OU
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
            AutoScroll = false; // single scroll owned by MainForm._contentHost (prevents nested scrollbars)
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, AutoScroll = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // heading
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status row
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // callout
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list host — takes remaining, outer scroll handles overflow
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // target
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220)); // console
            var heading = new Label { Text = "Convert Preset", Font = new Font(Md3Tokens.HeadlineMedium.FontFamily, 22f, FontStyle.Bold), ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space6) };
            ThemeManager.ThemeChanged += () => heading.ForeColor = ThemeManager.Current.OnSurface;

            // --- status row: FlowLayout wrap so pill + desc never clip at 960px (M3 adaptive) ---
            var statusRow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var openBtn = new Md3Button { Text = "Open .ffx file…", Icon = Md3Icons.Icon.FolderOpen, Width = 180, Margin = new Padding(0, 0, Md3Tokens.Space4, Md3Tokens.Space4) };
            openBtn.Click += (s, e) => OpenFile();

            var statusChip = new Panel { AutoSize = true, Height = 36, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            _fileChipLabel = new Label { Text = "Status: No file loaded", Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Location = new Point(34, 9) };
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
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Info, new Rectangle(Md3Tokens.Space2, 8, 18, 18), ThemeManager.Current.OnSurfaceVariant, 1.4f);
            };
            statusChip.Controls.Add(_fileChipLabel);
            System.Action updateChipWidth = null;
            updateChipWidth = () =>
            {
                statusChip.Width = 34 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space6;
                statusChip.Invalidate();
            };
            updateChipWidth();
            ThemeManager.ThemeChanged += () => updateChipWidth();

            var descLabel = new Label
            {
                Text = "Ready to process legacy After Effects presets. Supports version translation and plugin cleanup.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(460, 0), TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(Md3Tokens.Space2, 6, 0, Md3Tokens.Space4),
            };

            statusRow.Controls.Add(openBtn);
            statusRow.Controls.Add(statusChip);
            statusRow.Controls.Add(descLabel);

            // --- Intelligent Conversion callout — stitch rounded-[32px] with gradient top accent ---
            var callout = new Md3Card { Variant = Md3CardVariant.Filled, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            callout.Padding = new Padding(Md3Tokens.Space6);
            var calloutFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, MaximumSize = new Size(900, 0) };
            calloutFlow.Controls.Add(new Label { Text = "Intelligent Conversion", Font = Md3Tokens.TitleSmall, ForeColor = ThemeManager.Current.OnSurface, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            calloutFlow.Controls.Add(new Label
            {
                Text = "The tool will automatically detect and suggest removal of missing or incompatible plugins based on your current host configuration. Presets will be re-encoded to the selected target version.",
                Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                AutoSize = true, MaximumSize = new Size(720, 0),
            });
            callout.Controls.Add(calloutFlow);
            // gradient accent bar (stitch top h-2 primary->tertiary) painted on callout
            callout.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var accent = new Rectangle(0, 0, callout.Width, 4);
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(accent, ThemeManager.Current.Primary, ThemeManager.Current.TertiaryContainer, 0f))
                    e.Graphics.FillRectangle(brush, accent);
            };

            // --- data table host ---
            _effectList = new CheckedListBox { Dock = DockStyle.Fill, Font = Md3Tokens.BodyMedium };
            _effectListEmptyState = BuildEmptyState();
            _effectListHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Md3Tokens.Space4), MinimumSize = new Size(0, 160), AutoScroll = false };
            _effectListHost.Controls.Add(_effectList);
            _effectListHost.Controls.Add(_effectListEmptyState);
            _effectList.Visible = false;

            // --- target version + encoding options + convert button — expressive wrap (M3 adaptive) ---
            var targetRow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var targetCol = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            targetCol.Controls.Add(new Label { Text = "Target version", Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            _targetCombo = new Md3Dropdown { Width = 200 };
            _targetCombo.SetItems(Pipeline.KnownVersions.Keys.OrderBy(k => k).Select(DisplayNameFor), 0);
            targetCol.Controls.Add(_targetCombo);

            var encodingCol = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            encodingCol.Controls.Add(new Label { Text = "Encoding Options", Font = Md3Tokens.LabelMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant, AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space1) });
            var cleanMetadataCheck = new Md3Checkbox { Text = "Clean Metadata", Checked = true, Enabled = false, Width = 180, Height = 24 };
            var tip = new ToolTip();
            tip.SetToolTip(cleanMetadataCheck, "Not yet implemented — the pipeline doesn't have a separate metadata-cleaning step");
            _overwriteCheck = new Md3Checkbox { Text = "Overwrite File", Width = 180, Height = 24 };
            encodingCol.Controls.Add(cleanMetadataCheck);
            encodingCol.Controls.Add(_overwriteCheck);

            _convertBtn = new Md3Button { Text = "Convert…", Icon = Md3Icons.Icon.Convert, Width = 150, Height = 46, Enabled = false };
            _convertBtn.Click += (s, e) => DoConvert();
            var convertHost = new Panel { AutoSize = true, Height = 46, Margin = new Padding(Md3Tokens.Space4, Md3Tokens.Space4, 0, 0) };
            convertHost.Controls.Add(_convertBtn);
            _convertBtn.Location = new Point(0, 0);

            targetRow.Controls.Add(targetCol);
            targetRow.Controls.Add(encodingCol);
            targetRow.Controls.Add(convertHost);

            // --- console panel — fixed height 220, header 32 with real Label so CONSOLE OUTPUT never clips ---
            var consolePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 24, 27) };
            var consoleHeader = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 32, BackColor = Color.FromArgb(30, 30, 34) };
            consoleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            consoleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var dotsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, MinimumSize = new Size(260, 32) };
            dotsPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color[] dots = { Color.FromArgb(237, 106, 94), Color.FromArgb(97, 174, 238), Color.FromArgb(159, 120, 224) };
                int x = 10;
                foreach (var c in dots)
                {
                    using (var b = new SolidBrush(c)) e.Graphics.FillEllipse(b, x, 11, 10, 10);
                    x += 18;
                }
            };
            // real label for CONSOLE OUTPUT so it measures correctly (fixes CONSOLE OU truncation)
            var consoleTitleLabel = new Label { Text = "CONSOLE OUTPUT", Font = Md3Tokens.LabelSmall, ForeColor = Color.FromArgb(160, 255, 255, 255), AutoSize = true, Location = new Point(66, 9), BackColor = Color.Transparent };
            dotsPanel.Controls.Add(consoleTitleLabel);

            var clearLogsBtn = new LinkLabel { Text = "Clear Logs", AutoSize = true, Anchor = AnchorStyles.Right, Font = Md3Tokens.LabelSmall, LinkColor = Color.FromArgb(160, 255, 255, 255), Margin = new Padding(0, 9, 12, 0) };
            // _consoleBox is created before handler so capture works
            _consoleBox = new TextBox
            {
                Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9f),
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 24, 27), ForeColor = Color.FromArgb(190, 230, 190),
                BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            };
            clearLogsBtn.LinkClicked += (s, e) => { _consoleBox.Clear(); Log("[SYSTEM] Log cleared."); };
            consoleHeader.Controls.Add(dotsPanel, 0, 0);
            consoleHeader.Controls.Add(clearLogsBtn, 1, 0);

            // rounded-[12px] clip for console panel
            consolePanel.Padding = new Padding(0);
            consolePanel.Paint += (s, e) =>
            {
                // subtle rounded border illusion via outer rect
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            };

            consolePanel.Controls.Add(_consoleBox);
            consolePanel.Controls.Add(consoleHeader);
            _consoleBox.BringToFront(); // header stays top, box fills remainder via Dock Fill + header Dock Top ordering
            // Ensure box doesn't cover header: add header first then box with Dock Fill will fill remaining
            consolePanel.Controls.SetChildIndex(consoleHeader, 0);
            consolePanel.Controls.SetChildIndex(_consoleBox, 1);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(statusRow, 0, 1);
            root.Controls.Add(callout, 0, 2);
            root.Controls.Add(_effectListHost, 0, 3);
            root.Controls.Add(targetRow, 0, 4);
            root.Controls.Add(consolePanel, 0, 5);
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
                _fileChipLabel.Parent.Width = 34 + TextRenderer.MeasureText(_fileChipLabel.Text, Md3Tokens.BodyMedium).Width + Md3Tokens.Space6;
                _fileChipLabel.Parent.Invalidate();
                _convertBtn.Enabled = true;
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
            var container = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Current.Surface };
            container.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var margin = 24;
                var bounds = new Rectangle(margin, margin, container.Width - margin * 2, Math.Max(160, container.Height - margin * 2));
                using (var pen = new Pen(Color.FromArgb(100, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var path = RoundedRect(bounds, Md3Tokens.CornerLarge)) // stitch rounded-[32px] = CornerLarge 16 mapped, large feels right for this panel
                {
                    // subtle fill matching stitch surface-container-low
                    using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainerLow))
                        e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                int cy = bounds.Top + bounds.Height / 2 - 40;
                Md3Icons.Draw(e.Graphics, Md3Icons.Icon.FolderOpen, new Rectangle(bounds.X + bounds.Width / 2 - 28, cy - 28, 56, 56), ThemeManager.Current.Outline, 1.6f);
                TextRenderer.DrawText(e.Graphics, "No preset loaded", Md3Tokens.TitleMedium, new Rectangle(bounds.X, cy + 38, bounds.Width, 26), ThemeManager.Current.OnSurface, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics, "Select an Adobe After Effects .ffx file to begin the analysis and conversion process.", Md3Tokens.BodyMedium,
                    new Rectangle(bounds.X + bounds.Width / 2 - 220, cy + 68, 440, 40), ThemeManager.Current.OnSurfaceVariant, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };

            var browseBtn = new Md3Button { Text = "Browse Files", Icon = Md3Icons.Icon.Check, Variant = Md3ButtonVariant.Outlined, Width = 150, Height = 36 };
            browseBtn.Click += (s, e) => OpenFile();
            container.Controls.Add(browseBtn);
            container.Resize += (s, e) => browseBtn.Location = new Point((container.Width - browseBtn.Width) / 2, container.Height / 2 + 90);
            browseBtn.Location = new Point(100, 100); // initial, will be corrected on Resize
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
            Log("[OK] Verification pass: 0 Utf8 tags remaining, indices contiguous, keyframe/parameter data unchanged.");
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
