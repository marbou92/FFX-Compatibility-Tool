using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
// System.IO and System.Windows.Shapes both define `Path` - bind the bare
// name to the shape once so the graph code stays unambiguous
using Path = System.Windows.Shapes.Path;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Effect Lister: read-only compatibility view of a preset's effects —
    /// filter/sort toolbar, status-colored rows in a unified list card,
    /// friendly empty state, drag-overlay language shared with Convert,
    /// and a recent-files flyout over the full 5-entry history.
    ///
    /// The workspace opens into AE's Effect Controls panel — every effect
    /// as a collapsible block of real AE property lines (keyframe-navigator
    /// gutter, stopwatch, fixed name column, hover-underlined value, nested
    /// parameter groups, the About link) — with a
    /// switcher to the split compatibility list + tabbed inspector, whose
    /// parameter rows are the simple read (stopwatch, name, value under a
    /// quiet group caption). Both read the same decoded data and share
    /// keyframe selection state.
    ///
    /// The right-hand inspector reads like AE's timeline: parameter rows
    /// carry the stopwatch mark (lit = time-varying) and a keyframe
    /// navigator, the Keyframes tab shows AE-style timecodes with slope
    /// and influence numbers for the selected keyframe, and the Graph tab
    /// draws the Graph Editor pair — a value graph with tangent handles
    /// and a speed graph — with a hover probe. Everything is read-only
    /// (PresetInspector reads, never writes; the pipeline's keyframes are
    /// untouched).
    /// </summary>
    public partial class ListerPage : UserControl, ISection
    {
        public class EffectRowVm
        {
            public string Name { get; set; }
            public string VendorLabel { get; set; }
            public string Status { get; set; }
            public Brush RowBrush { get; set; }
            // position of this effect among the file's non-sentinel effects —
            // the stable key that ties a (sorted/filtered) row back to its
            // PresetEffectDetails entry
            public int EffectIndex { get; set; }
        }

        public class RecentRow
        {
            public string FileName { get; set; }
            public string Meta { get; set; }
            public string Path { get; set; }
            public bool Exists { get; set; }
        }

        /// <summary>
        /// One row of the Parameters tab / Effect Controls body, shaped
        /// like an AE property line: stopwatch state, name + stream
        /// summary, the value slot, and the keyframe navigator targets.
        /// Brushes stay in the templates (via DynamicResource triggers
        /// keyed on IsAnimated) so theme swaps keep working — the VM only
        /// carries flags, text and counts. The Effect Controls template
        /// renders the navigator gutter; the inspector's simple row drops
        /// it in favor of a click-through to the Keyframes tab.
        /// </summary>
        public class ParamRowVm
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public string MatchName { get; set; }
            public bool IsAnimated { get; set; }
            public double StopwatchOpacity { get; set; }
            public string StopwatchTip { get; set; }
            public string ValueText { get; set; }
            public Visibility ValueVisible { get; set; }
            public string ValueTip { get; set; }
            public Visibility NavVisible { get; set; }
            public string KeyCountTip { get; set; }
            public Cursor RowCursor { get; set; }
            public double RowOpacity { get; set; }
            // the decoded parameter behind the row — what the navigator
            // buttons and the row click resolve to
            public PresetParameter ParamRef { get; set; }

            public ParamRowVm(PresetParameter p)
            {
                ParamRef = p;
                Name = p.Name;
                MatchName = p.MatchName ?? p.Name;
                IsAnimated = p.IsAnimated;
                RowCursor = p.IsAnimated ? Cursors.Hand : Cursors.Arrow;

                // the Count check is belt-and-braces: the decoder only sets
                // IsAnimated after storing at least one keyframe, but this
                // row builder must never be able to throw on any input
                if (p.IsAnimated && p.Keyframes.Count > 0)
                {
                    // stream summary: keys · value travel · time span
                    double vMin = p.Keyframes.Min(k => k.Value);
                    double vMax = p.Keyframes.Max(k => k.Value);
                    double span = PresetCurve.Seconds(
                        p.Keyframes[p.Keyframes.Count - 1].Time - p.Keyframes[0].Time);
                    string travel = Math.Abs(vMax - vMin) < 1e-9
                        ? $"flat at {Fmt(vMin)}"
                        : $"{Fmt(vMin)} → {Fmt(vMax)}";
                    Detail = $"animated · {travel} · {span.ToString("0.##")} s span";
                    StopwatchOpacity = 1.0;
                    StopwatchTip = "Time-varying: ON — this property carries a keyframe stream (read-only inspector)";

                    // the value slot shows the value at the first keyframe,
                    // the way AE shows the value at the playhead
                    ValueText = Fmt(p.Keyframes[0].Value);
                    ValueVisible = Visibility.Visible;
                    ValueTip = $"value at the first keyframe · range {Fmt(vMin)} … {Fmt(vMax)} · {p.Keyframes.Count} keyframes";
                    NavVisible = Visibility.Visible;
                    KeyCountTip = $"{p.Keyframes.Count} keyframe{(p.Keyframes.Count == 1 ? "" : "s")} · click to open the Keyframes tab";
                }
                else
                {
                    string range = p.Min.HasValue && p.Max.HasValue
                        ? $" · range {Fmt(p.Min)} … {Fmt(p.Max)}" : "";
                    Detail = "static value" + range;
                    StopwatchOpacity = 0.4;
                    StopwatchTip = "Time-varying: OFF — static value (read-only inspector)";

                    if (p.StaticValue.HasValue)
                    {
                        ValueText = Fmt(p.StaticValue);
                        ValueVisible = Visibility.Visible;
                        ValueTip = p.Min.HasValue && p.Max.HasValue
                            ? $"static value · range {Fmt(p.Min)} … {Fmt(p.Max)}"
                            : "static value";
                    }
                    else
                    {
                        ValueText = "";
                        ValueVisible = Visibility.Collapsed;
                        ValueTip = "";
                    }
                    NavVisible = Visibility.Collapsed;
                    KeyCountTip = "";
                }
            }

            static string Fmt(double? v) => v?.ToString("0.###") ?? "—";
        }

        /// <summary>
        /// One row of the EFFECT CONTROLS panel — deliberately a separate
        /// type from the inspector's ParamRowVm so the two parameter UIs
        /// stay independent: this one carries the control kind decoded from
        /// the preset's parT tree (checkbox, popup, color, angle, point,
        /// layer, button...) and the value column renders the matching
        /// AE-style visual per kind. Read-only, like AE's panel.
        /// </summary>
        public class EcParamVm
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public string MatchName { get; set; }
            public bool IsAnimated { get; set; }
            public double StopwatchOpacity { get; set; }
            public string StopwatchTip { get; set; }
            public string ValueText { get; set; }
            public Visibility ValueVisible { get; set; }
            public string ValueTip { get; set; }
            public Visibility NavVisible { get; set; }
            public string KeyCountTip { get; set; }
            public Cursor RowCursor { get; set; }

            // --- kind-driven view state (Effect Controls value visuals) ---
            public bool IsCheckbox { get; set; }
            public bool IsPopup { get; set; }
            public bool IsColor { get; set; }
            public bool Checked { get; set; }
            public string PopupText { get; set; }
            public Brush ColorBrush { get; set; }
            // which value control this row renders — computed here so the
            // templates bind Visibility straight to the VM (style triggers
            // could never override a local Visibility binding)
            public Visibility TextVisible { get; set; } = Visibility.Collapsed;
            public Visibility CheckboxVisible { get; set; } = Visibility.Collapsed;
            public Visibility PopupVisible { get; set; } = Visibility.Collapsed;
            public Visibility SwatchVisible { get; set; } = Visibility.Collapsed;
            // the decoded parameter behind the row — what the navigator
            // buttons and the row click resolve to
            public PresetParameter ParamRef { get; set; }

            public EcParamVm(PresetParameter p)
            {
                ParamRef = p;
                Name = p.Name;
                MatchName = p.MatchName ?? p.Name;
                IsAnimated = p.IsAnimated;
                RowCursor = p.IsAnimated ? Cursors.Hand : Cursors.Arrow;
                int kind = p.Kind;

                IsCheckbox = kind == PresetParamKind.Checkbox;
                IsPopup = kind == PresetParamKind.Popup;
                IsColor = kind == PresetParamKind.Color && !p.IsAnimated;

                // row-level state: the stream summary lives in tooltips, the
                // stopwatch mark reads ON/OFF, the navigator gutter is
                // reserved on every row so the NAME columns align like AE's
                if (p.IsAnimated && p.Keyframes.Count > 0)
                {
                    double vMin = p.Keyframes.Min(k => k.Value);
                    double vMax = p.Keyframes.Max(k => k.Value);
                    double span = PresetCurve.Seconds(
                        p.Keyframes[p.Keyframes.Count - 1].Time - p.Keyframes[0].Time);
                    string travel = Math.Abs(vMax - vMin) < 1e-9
                        ? $"flat at {Fmt(vMin)}"
                        : $"{Fmt(vMin)} → {Fmt(vMax)}";
                    Detail = $"animated · {travel} · {span.ToString("0.##")} s span";
                    StopwatchOpacity = 1.0;
                    StopwatchTip = "Time-varying: ON — this property carries a keyframe stream (read-only panel)";
                    NavVisible = Visibility.Visible;
                    KeyCountTip = $"{p.Keyframes.Count} keyframe{(p.Keyframes.Count == 1 ? "" : "s")} · click to open the Keyframes tab";
                }
                else
                {
                    string range = p.Min.HasValue && p.Max.HasValue
                        ? $" · range {Fmt(p.Min)} … {Fmt(p.Max)}" : "";
                    Detail = "static value" + range;
                    StopwatchOpacity = 0.4;
                    StopwatchTip = "Time-varying: OFF — static value (read-only panel)";
                    NavVisible = Visibility.Collapsed;
                    KeyCountTip = "";
                }

                // value slot: the control AE draws for this kind. The value
                // read is the static cdat value, or the first keyframe's the
                // way AE shows the value at the playhead.
                double v = 0;
                bool hasV = (p.IsAnimated && p.Keyframes.Count > 0) || p.StaticValue.HasValue;
                if (hasV)
                    v = p.IsAnimated && p.Keyframes.Count > 0
                        ? p.Keyframes[0].Value : p.StaticValue.Value;

                if (IsCheckbox)
                {
                    // AE draws a real checkbox in the value column
                    Checked = hasV && v >= 0.5;
                    CheckboxVisible = Visibility.Visible;
                    ValueText = "";
                    ValueVisible = Visibility.Collapsed;
                    ValueTip = (Checked ? "On" : "Off") + " — from the preset (read-only panel)";
                }
                else if (IsPopup)
                {
                    // AE draws a popup whose label is the selected entry
                    PopupText = PopupLabel(p, hasV ? v : 0);
                    PopupVisible = Visibility.Visible;
                    ValueText = "";
                    ValueVisible = Visibility.Collapsed;
                    var menu = p.MenuItems;
                    ValueTip = menu != null
                        ? $"menu selection {Math.Max(1, (int)Math.Round(hasV ? v : 1))} of {menu.Length} · read-only panel"
                        : "menu selection · read-only panel";
                }
                else if (IsColor)
                {
                    // AE draws a color swatch in the value column
                    ColorBrush = ColorOf(p);
                    SwatchVisible = Visibility.Visible;
                    ValueText = "";
                    ValueVisible = Visibility.Collapsed;
                    ValueTip = "color from the preset (read-only panel)";
                }
                else if (kind == PresetParamKind.Button || kind == PresetParamKind.FloatSlider ||
                         kind == PresetParamKind.ArbitraryData)
                {
                    // command rows and plugin data blobs: AE shows no value
                    ValueText = "";
                    ValueVisible = Visibility.Collapsed;
                    ValueTip = "";
                }
                else if (kind == PresetParamKind.Point && !p.IsAnimated && p.StaticValue2.HasValue)
                {
                    ValueText = $"({Num(p.StaticValue ?? 0)}, {Num(p.StaticValue2 ?? 0)})";
                    ValueVisible = Visibility.Visible;
                    TextVisible = Visibility.Visible;
                    ValueTip = "point (X, Y) from the preset · " + Detail;
                }
                else if ((kind == PresetParamKind.Layer || kind == PresetParamKind.Path))
                {
                    ValueText = !hasV || v == 0
                        ? "None"
                        : (kind == PresetParamKind.Layer ? $"Layer {v.ToString("0")}" : $"Mask {v.ToString("0")}");
                    ValueVisible = Visibility.Visible;
                    TextVisible = Visibility.Visible;
                    ValueTip = "selection from the preset (read-only panel)";
                }
                else if (hasV)
                {
                    // sliders, angles, percents — AE's right-aligned number
                    ValueText = kind == PresetParamKind.Angle ? Num(v) + "°" : Num(v);
                    ValueVisible = Visibility.Visible;
                    TextVisible = Visibility.Visible;
                    if (p.IsAnimated)
                    {
                        double vMin = p.Keyframes.Min(k => k.Value);
                        double vMax = p.Keyframes.Max(k => k.Value);
                        ValueTip = $"value at the first keyframe · range {Fmt(vMin)} … {Fmt(vMax)} · {p.Keyframes.Count} keyframes";
                    }
                    else
                    {
                        ValueTip = p.Min.HasValue && p.Max.HasValue
                            ? $"static value · range {Fmt(p.Min)} … {Fmt(p.Max)}"
                            : "static value";
                    }
                }
                else
                {
                    ValueText = "";
                    ValueVisible = Visibility.Collapsed;
                    ValueTip = "";
                }
            }

            /// <summary>AE formats values with a fixed decimal read.</summary>
            static string Num(double v) => v.ToString("0.0##");

            static string Fmt(double? v) => v?.ToString("0.###") ?? "—";

            /// <summary>
            /// Popup label for a 1-based stored index ("No|Tile|Reflect" with
            /// 3.0 → "Reflect"); out-of-range presets fall back to Option N.
            /// </summary>
            static string PopupLabel(PresetParameter p, double idx)
            {
                var menu = p.MenuItems;
                int i = (int)Math.Round(idx) - 1;
                if (menu != null && i >= 0 && i < menu.Length && menu[i].Length > 0)
                    return menu[i];
                return "Option " + Math.Max(1, i + 1);
            }

            /// <summary>
            /// Swatch brush from the stored RGB(A) doubles; presets store
            /// either the 0-1 or the 0-255 scale, told apart per channel.
            /// </summary>
            static Brush ColorOf(PresetParameter p)
            {
                double r = p.StaticValue ?? 0, g = p.StaticValue2 ?? 0, b = p.StaticValue3 ?? 0;
                if (r > 1 || g > 1 || b > 1) { r /= 255.0; g /= 255.0; b /= 255.0; }
                byte R = (byte)Math.Round(Math.Max(0, Math.Min(1, r)) * 255);
                byte G = (byte)Math.Round(Math.Max(0, Math.Min(1, g)) * 255);
                byte B = (byte)Math.Round(Math.Max(0, Math.Min(1, b)) * 255);
                var br = new SolidColorBrush(Color.FromRgb(R, G, B));
                br.Freeze();
                return br;
            }
        }

        /// <summary>One keyframe row: AE timecode, frame math, easing chip.</summary>
        public class KfRowVm
        {
            public string Index { get; set; }
            public int KfIndex { get; set; }
            public string TimeSec { get; set; }
            public string Sub { get; set; }
            public string Value { get; set; }
            public string Interp { get; set; }
            public string Tip { get; set; }
            public bool Selected { get; set; }

            public KfRowVm(int index, PresetKeyframe kf, PresetKeyframe prev)
            {
                KfIndex = index - 1;
                Index = index.ToString();
                // ticks → seconds via PresetCurve's empirically derived
                // timebase (1 tick = 1/1024 s); raw ticks stay in the tooltip
                double sec = PresetCurve.Seconds(kf.Time);
                TimeSec = Timecode(sec);
                int frame = (int)Math.Round(sec * Fps);
                if (prev == null)
                {
                    Sub = $"frame {frame} · {sec.ToString("0.##")} s";
                }
                else
                {
                    int prevFrame = (int)Math.Round(PresetCurve.Seconds(prev.Time) * Fps);
                    double ds = sec - PresetCurve.Seconds(prev.Time);
                    Sub = $"frame {frame} · +{frame - prevFrame}f · +{ds.ToString("0.##")}s";
                }
                Value = kf.Value.ToString("0.###");
                Interp = kf.InterpLabel;
                Tip = $"t = {sec.ToString("0.###")} s · raw time {kf.Time} ticks" +
                      $" · in influence {kf.InInfluence.ToString("0.##")}" +
                      $" · out influence {kf.OutInfluence.ToString("0.##")}";
            }
        }

        /// <summary>
        /// One effect block of the Effect Controls view: header data plus
        /// the AE property rows (the same ParamRowVm anatomy the inspector
        /// uses, so stopwatch/navigator behavior is identical in both views).
        /// </summary>
        public class EcGroupVm
        {
            public string Title { get; set; }
            public string Sub { get; set; }
            public bool Open { get; set; }
            public Visibility BodyVisible => Open ? Visibility.Visible : Visibility.Collapsed;
            // body tree: EcParamVm property lines and EcSubGroupVm group
            // nodes, in the preset's document order
            public List<object> Items { get; set; }
            public int EffectIndex { get; set; }
        }

        private readonly PluginProfile _profile;
        private List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();
        private List<PresetEffectDetails> _details = new List<PresetEffectDetails>();
        // human-readable decode problems from the last load ("effect #2 ..."):
        // surfaced on the panel and in the log, so a preset that half-decodes
        // never fails in silence
        private List<string> _inspectErrors = new List<string>();
        // last plugin-table failure reason already logged (null = none) —
        // so a persistent failure is logged once, not again on every Refresh
        private string _lastTableError;
        private readonly ObservableCollection<EffectRowVm> _rows = new ObservableCollection<EffectRowVm>();
        private int _filterMode; // 0 all, 1 missing only, 2 compatible only
        private bool _sortDesc;

        // inspector state: selected effect (by stable effect index), selected
        // animated parameter (for the keyframes/graph tabs), active tab,
        // graph mode (0 = value like AE's value graph, 1 = speed graph),
        // selected keyframe (drives the graph's ring + handles and the
        // Keyframes tab's highlight + easing numbers)
        private int _inspEffectIndex = -1;
        private int _animParamIndex = -1;
        private int _tab;
        private int _graphMode;
        private int _selKf = -1;
        private bool _syncingCombo;

        // DragEnter/DragLeave fire on every child boundary crossing; a depth
        // counter is the only flicker-free way to know the drag truly left.
        private int _dragDepth;

        // ---------- view modes: AE Effect Controls panel vs. split inspector ----------
        // 0 = Effect Controls (the AE-style panel, the default), 1 = split
        // compatibility list + tabbed inspector. Group open/closed state and
        // the per-effect status/vendor header lines survive every rebuild;
        // the dictionaries are keyed by stable effect index.
        // the Inspector is the default section: a freshly loaded preset
        // opens the split workspace (compatibility list + inspector), the
        // way AE opens its Inspector rather than Effect Controls
        private int _viewMode = 1;
        private readonly Dictionary<int, bool> _ecOpen = new Dictionary<int, bool>();
        // AE anatomy state: each named parameter group's disclosure state
        // (keyed "effectIndex|groupPath"); survives every rebuild
        private readonly Dictionary<string, bool> _ecGroupOpen = new Dictionary<string, bool>();
        private readonly Dictionary<int, string> _ecStatus = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _ecVendor = new Dictionary<int, string>();

        // live-resize redraw coalescing: while the window is being dragged
        // by its border, SizeChanged fires per pixel and a full graph
        // rebuild (geometry + labels + 260 samples) per pixel is exactly
        // the kind of work that makes Win7 resize feel rough. The timer
        // merges the storm into ~1 redraw per 70 ms plus a final one.
        private DispatcherTimer _graphRedrawTimer;

        public ListerPage(PluginProfile profile)
        {
            InitializeComponent();
            _profile = profile;
            EffectList.ItemsSource = _rows;
            StatusBarVersion.Text = $"FFX Compatibility Tool {AppInfo.DisplayVersion}";
            UpdateRecentCard();
            GraphCanvas.SizeChanged += (s, e) => ScheduleGraphRedraw();
            KfTimeline.SizeChanged += (s, e) => DrawKfTimeline();
            SetTab(0);
            SetView(0); // the AE Effect Controls panel is the default workspace view
            // row container brushes are captured per Refresh — re-run when the
            // theme changes so status tints match the new palette/mode; the
            // graph and the keyframe strip bake brush colors too
            ThemeService.Changed += () => { Refresh(); BuildKeyframes(); DrawGraph(); };
        }

        public void OnShown() => UpdateRecentCard();

        public void OnProfileChanged() => Refresh();

        private void UpdateRecentCard()
        {
            var recent = HistoryStore.Load().FirstOrDefault();
            if (recent != null)
            {
                RecentText.Text = recent.FileName;
                RecentCard.Opacity = 1;
                RecentCard.Cursor = Cursors.Hand;
                RecentCard.ToolTip = "Open the recent-files picker";
            }
            else
            {
                RecentText.Text = "View last 5 analyzed presets";
                RecentCard.Opacity = 0.55;
                RecentCard.Cursor = Cursors.Hand;
                RecentCard.ToolTip = "No recent files yet";
            }
        }

        // ---------- recent-files flyout ----------
        private void Recent_Click(object sender, MouseButtonEventArgs e)
        {
            var entries = HistoryStore.Load();
            var rows = new List<RecentRow>();
            foreach (var entry in entries.Take(5))
            {
                bool exists = File.Exists(entry.Path);
                rows.Add(new RecentRow
                {
                    FileName = entry.FileName,
                    // HistoryStore.TimeAgo finally gets its call site: "12 mins ago"
                    Meta = exists
                        ? $"{entry.EffectCount} effect{(entry.EffectCount == 1 ? "" : "s")} · {HistoryStore.TimeAgo(entry.Timestamp)}"
                        : "File moved or deleted",
                    Path = entry.Path,
                    Exists = exists
                });
            }

            if (rows.Count > 0)
            {
                RecentList.ItemsSource = rows;
                RecentEmptyHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                RecentList.ItemsSource = null;
                RecentEmptyHint.Visibility = Visibility.Visible;
            }

            // a previously picked item stays selected in its list box; reset so
            // re-opening shows no stale highlight and re-picking still fires
            RecentList.SelectedIndex = -1;
            RecentFlyout.IsOpen = true;
        }

        private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecentList.SelectedItem is RecentRow row && row.Exists && File.Exists(row.Path))
            {
                RecentFlyout.IsOpen = false;
                LoadFile(row.Path);
            }
        }

        // ---------- loading ----------
        public void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" };
            if (dlg.ShowDialog() == true) LoadFile(dlg.FileName);
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

        // ---------- drag feedback ----------
        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (!HasFfx(e.Data)) return;
            _dragDepth++;
            DragOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            if (_dragDepth > 0) _dragDepth--;
            if (_dragDepth == 0) DragOverlay.Visibility = Visibility.Collapsed;
        }

        private void Page_Drop(object sender, DragEventArgs e)
        {
            _dragDepth = 0;
            DragOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var ffx = files.FirstOrDefault(f => f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));
                if (ffx != null) { LoadFile(ffx); return; }
            }
            MessageBox.Show(Window.GetWindow(this),
                "No .ffx preset was found in the dropped items.",
                "Unsupported file", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool HasFfx(IDataObject data) =>
            data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));

        private void LoadFile(string path)
        {
            try
            {
                // a fresh file invalidates every index the inspector holds —
                // effect, animated-parameter and keyframe selections are all
                // positions in the PREVIOUS file's decoded data
                _inspEffectIndex = -1;
                _animParamIndex = -1;
                _selKf = -1;
                // ...and the panel's own state is per-file too: disclosure
                // states keyed by effect index belong to the OLD preset and
                // must not leak into the new one
                _ecOpen.Clear();
                _ecGroupOpen.Clear();
                _ecStatus.Clear();
                _ecVendor.Clear();
                // a fresh preset opens the Inspector section (the split
                // workspace), regardless of the view the old file was in
                _viewMode = 1;

                FileChipText.Text = System.IO.Path.GetFileName(path);
                byte[] bytes = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(bytes);

                // deep inspection is additive — if a preset carries a
                // structure the inspector can't decode, the list above still
                // works and the panel SAYS what couldn't be read (never
                // silently)
                _inspectErrors = new List<string>();
                try { _details = PresetInspector.Inspect(bytes, _inspectErrors); }
                catch (Exception ipx)
                {
                    _details = new List<PresetEffectDetails>();
                    _inspectErrors.Add("the preset structure couldn't be read: " +
                                       ipx.GetType().Name + " — " + ipx.Message);
                }
                foreach (var w in _inspectErrors)
                    LogService.Append("inspect: " + System.IO.Path.GetFileName(path) + " — " + w);

                int ecParams = _details.Sum(x => x.Parameters.Count);
                int ecAnim = _details.Sum(x => x.AnimatedCount);
                EcSub.Text = $"{_currentEffects.Count(x => !x.IsSentinel)} effects · " +
                             $"{ecParams} parameter{(ecParams == 1 ? "" : "s")} · " +
                             $"{ecAnim} animated — read-only, like AE's panel";
                if (_inspectErrors.Count > 0)
                {
                    // one-line heads-up on the panel; the full list rides the
                    // tooltip and the log
                    EcSub.Text += $"  ⚠ {_inspectErrors.Count} decode " +
                                  $"warning{(_inspectErrors.Count == 1 ? "" : "s")}";
                    EcSub.ToolTip = string.Join("\n", _inspectErrors);
                }
                else
                {
                    EcSub.ToolTip = null;
                }

                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));
                Refresh();
                if (_rows.Count > 0) EffectList.SelectedIndex = 0; // open the inspector right away
            }
            catch (Exception ex)
            {
                // the full problem report instead of a bare message box:
                // it names the failing method, is copyable for a bug
                // report, and lands in both logs (crash.log + session log)
                App.Report($"reading '{System.IO.Path.GetFileName(path)}' into the Effect Controls panel", ex);
                FileChipText.Text = "No file loaded";
                _currentEffects = new List<Pipeline.EffectInfo>();
                _details = new List<PresetEffectDetails>();
                EcSub.Text = "Open a preset to see its effect controls";
                Refresh();
            }
        }

        // ---------- filter / sort / refresh ----------
        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = (_filterMode + 1) % 3;
            // active filter gets the primary tint — resource KEY, tracks theme swaps
            FilterIcon.SetResourceReference(IconGlyph.ForegroundProperty,
                _filterMode != 0 ? "B.Primary" : "B.OnSurfaceVariant");
            Refresh();
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _sortDesc = !_sortDesc;
            SortIcon.SetResourceReference(IconGlyph.ForegroundProperty,
                _sortDesc ? "B.Primary" : "B.OnSurfaceVariant");
            Refresh();
        }

        public void Refresh()
        {
            var table = PluginLookup.LoadTable();
            if (PluginLookup.TableLoadError != _lastTableError)
            {
                // each NEW table failure is logged once — statuses degrade
                // to Unknown plugin, the list itself keeps working
                _lastTableError = PluginLookup.TableLoadError;
                if (_lastTableError != null)
                    LogService.Append("plugin table: " + _lastTableError +
                                      " \u2014 every effect shows as Unknown plugin until it's fixed");
            }
            var real = _currentEffects.Where(e => !e.IsSentinel);

            // stable position of every effect within the file — rows are
            // sorted/filtered, but the inspector needs the original index
            var fileOrder = real.Select((eff, i) => new { eff, i })
                                .ToDictionary(x => x.eff, x => x.i);

            IEnumerable<Pipeline.EffectInfo> ordered = _sortDesc
                ? real.OrderByDescending(e => e.MatchName, StringComparer.OrdinalIgnoreCase)
                : real.OrderBy(e => e.MatchName, StringComparer.OrdinalIgnoreCase);

            if (_filterMode == 1)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) == false);
            else if (_filterMode == 2)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) != false);

            int shown = 0, missing = 0;
            _rows.Clear();
            foreach (var eff in ordered)
            {
                shown++;
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);

                string status;
                Brush row = Brushes.Transparent;
                if (match.Vendor == null)
                {
                    status = "Unknown plugin";
                    row = (Brush)FindResource("B.TertiaryContainer");
                }
                else if (owned == false)
                {
                    status = "Likely to fail";
                    missing++;
                    row = (Brush)FindResource("B.ErrorContainer");
                }
                else if (owned == true)
                {
                    status = "Compatible";
                }
                else
                {
                    status = "Native";
                }

                _rows.Add(new EffectRowVm
                {
                    Name = eff.MatchName,
                    VendorLabel = $"{match.Vendor ?? "?"} — {match.Suite ?? "?"}",
                    Status = status,
                    RowBrush = row,
                    EffectIndex = fileOrder[eff]
                });

                // header data for the Effect Controls block, keyed by the
                // same stable effect index the rows carry
                _ecStatus[fileOrder[eff]] = status;
                _ecVendor[fileOrder[eff]] = $"{match.Vendor ?? "?"} — {match.Suite ?? "?"}";
            }

            bool hasContent = _currentEffects.Any(e => !e.IsSentinel);
            EmptyState.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
            SetView(_viewMode); // shows/hides EcHost + SplitHost for the active view

            if (hasContent && shown > 0)
            {
                string filterText = _filterMode == 0 ? "All" : _filterMode == 1 ? "Missing only" : "Compatible only";
                string sortText = _sortDesc ? "Z→A" : "A→Z";
                StatusBarMode.Text = $"{shown} shown · {missing} likely to fail · {filterText} · {sortText}";
            }
            else
            {
                StatusBarMode.Text = hasContent ? "No effects match this filter" : "Ready";
            }

            // the list was rebuilt, so the ListBox selection is gone — restore
            // the inspector's effect if its row survived the filter/sort
            if (EffectList.SelectedIndex < 0 && _inspEffectIndex >= 0)
            {
                int idx = _rows.ToList().FindIndex(r => r.EffectIndex == _inspEffectIndex);
                if (idx >= 0) EffectList.SelectedIndex = idx;
            }

            if (_inspEffectIndex < 0) SetInspectorEmpty();

            // the left status slot is only populated by future features — keep
            // its separator hidden while empty so no orphan "·" ever shows
            StatusSep.Visibility = string.IsNullOrEmpty(StatusBarLeft.Text)
                ? Visibility.Collapsed : Visibility.Visible;

            RefreshEcRows();
        }

        // ---------- view modes: Effect Controls ↔ split inspector ----------
        private void ViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SetView(sender == ViewInspectorBtn ? 1 : 0);
        }

        /// <summary>
        /// Switches the workspace between the AE Effect Controls panel
        /// (mode 0) and the split compatibility list + inspector
        /// (mode 1, the default). Hidden while no preset is loaded.
        /// </summary>
        private void SetView(int mode)
        {
            _viewMode = mode;
            if (ViewEcBtn == null) return; // XAML not loaded yet (design-time)
            bool has = _currentEffects.Any(x => !x.IsSentinel);
            ViewEcBtn.IsChecked = mode == 0;
            ViewInspectorBtn.IsChecked = mode == 1;
            ViewSwitcher.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            EcHost.Visibility = has && mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            SplitHost.Visibility = has && mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (mode == 0) RefreshEcRows();
            else
            {
                // the graph pane may be getting its first real size this
                // pass — repaint after layout instead of 70ms later
                Dispatcher.BeginInvoke(new Action(DrawGraph), DispatcherPriority.Loaded);
            }
        }

        /// <summary>Vendor/status line under one Effect Controls block title.</summary>
        private string EcSubFor(int effectIndex, PresetEffectDetails d)
        {
            string vendor = _ecVendor.TryGetValue(effectIndex, out string v) ? v : "unknown plugin";
            string status = _ecStatus.TryGetValue(effectIndex, out string s) ? s : "";
            return $"{vendor}  ·  {d.Parameters.Count} parameter{(d.Parameters.Count == 1 ? "" : "s")}" +
                   $"  ·  {d.AnimatedCount} animated  ·  {status}";
        }

        /// <summary>
        /// Rebuilds the Effect Controls groups from the decoded preset. The
        /// rows share the inspector's ParamRowVm/template, so the property
        /// lines read identically in both views. Cheap — runs only on load,
        /// theme change, or a toggle.
        /// </summary>
        private void RefreshEcRows()
        {
            if (EcList == null) return;
            var groups = new List<EcGroupVm>();
            for (int i = 0; i < _details.Count; i++)
            {
                var d = _details[i];
                if (d.Error != null)
                {
                    // the decoder kept this effect's slot but couldn't decode
                    // its parameter tree — show WHY, never an empty gap
                    LogService.Append($"effect controls: effect #{i + 1} ({d.MatchName}) " +
                                      $"couldn't be decoded \u2014 {d.Error}");
                    groups.Add(new EcGroupVm
                    {
                        Title = string.IsNullOrEmpty(d.ShortName) ? d.MatchName : d.ShortName,
                        Sub = "\u26a0 couldn't be decoded \u2014 " + d.Error,
                        Open = false,
                        Items = new List<object>(),
                        EffectIndex = i
                    });
                    continue;
                }
                if (d.Parameters.Count == 0)
                {
                    // a real effect whose every row was hidden (housekeeping
                    // markers, no parT tree...) still gets its header line —
                    // an honest "nothing decoded" beats an invisible effect
                    groups.Add(new EcGroupVm
                    {
                        Title = string.IsNullOrEmpty(d.ShortName) ? d.MatchName : d.ShortName,
                        Sub = EcSubFor(i, d),
                        Open = false,
                        Items = new List<object>(),
                        EffectIndex = i
                    });
                    continue;
                }
                try
                {
                    bool open = !_ecOpen.TryGetValue(i, out bool o) || o;
                    var root = new List<object>();
                    var created = new Dictionary<string, EcSubGroupVm>();
                    foreach (var p in d.Parameters)
                    {
                        // the Effect Controls' own row VM — kind-aware values,
                        // separate from the inspector's simpler ParamRowVm
                        var row = new EcParamVm(p);
                        AddEcRow(root, created, i, p.Group, row);
                    }
                    groups.Add(new EcGroupVm
                    {
                        Title = string.IsNullOrEmpty(d.ShortName) ? d.MatchName : d.ShortName,
                        Sub = EcSubFor(i, d),
                        Open = open,
                        Items = root,
                        EffectIndex = i
                    });
                }
                catch (Exception ex)
                {
                    // one malformed effect must never take the panel down —
                    // AE never dies on a preset either: degrade to a closed
                    // block that still names the effect AND the failure
                    LogService.Append($"effect controls: effect #{i + 1} ({d.MatchName}) " +
                                      $"couldn't be displayed \u2014 {Describe(ex)}");
                    groups.Add(new EcGroupVm
                    {
                        Title = string.IsNullOrEmpty(d.ShortName) ? d.MatchName : d.ShortName,
                        Sub = "\u26a0 parameters couldn't be displayed \u2014 " + Describe(ex),
                        Open = false,
                        Items = new List<object>(),
                        EffectIndex = i
                    });
                }
            }
            try
            {
                EcList.ItemsSource = groups;
                // realize the row templates HERE, inside the guard: without
                // this the realization happens in the layout pass — past
                // every try/catch in the load path — where one bad row
                // crashed the whole window (the reported preset-load crash)
                EcList.UpdateLayout();
                if (groups.Count == 0)
                {
                    EcEmpty.Text = _inspectErrors.Count > 0
                        ? "Parameter data for this preset couldn't be decoded:\n" +
                          string.Join("\n", _inspectErrors) +
                          "\nThe compatibility list still works."
                        : "Parameter data for this preset couldn't be decoded - the compatibility list still works, and the inspector explains what could be read.";
                    EcEmpty.Visibility = Visibility.Visible;
                }
                else
                {
                    EcEmpty.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                string chain = Describe(ex);
                LogService.Append($"effect controls: the panel couldn't be rendered \u2014 {chain}");
                EcList.ItemsSource = null;
                EcEmpty.Text = "This preset's parameter panel couldn't be rendered \u2014 " + chain +
                               " - the compatibility list still works. Details in About \u2192 Logs.";
                EcEmpty.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Places one property row into the EC body tree: at the root, or
        /// inside its parameter group — intermediate group nodes are
        /// created on first encounter, so the preset's document order of
        /// groups and rows (AE's panel order) is preserved.
        /// </summary>
        private void AddEcRow(List<object> root, Dictionary<string, EcSubGroupVm> created,
                              int effectIndex, string path, object row)
        {
            if (string.IsNullOrEmpty(path)) { root.Add(row); return; }
            AddEcNode(root, created, effectIndex, path).Items.Add(row);
        }

        /// <summary>Gets or creates the group node for a group path.</summary>
        private EcSubGroupVm AddEcNode(List<object> root, Dictionary<string, EcSubGroupVm> created,
                                       int effectIndex, string path)
        {
            if (created.TryGetValue(path, out var g)) return g;
            string name = path, parent = null;
            int sep = path.LastIndexOf('\u0001');
            if (sep >= 0) { name = path.Substring(sep + 1); parent = path.Substring(0, sep); }
            g = new EcSubGroupVm
            {
                Title = name,
                GroupKey = path,
                EffectIndex = effectIndex,
                // AE's Effect Controls starts named sub-settings collapsed —
                // only the user's explicit disclosure (persisted in
                // _ecGroupOpen) opens them
                Open = _ecGroupOpen.TryGetValue(effectIndex + "|" + path, out bool stored) && stored
            };
            created[path] = g;
            if (parent == null) root.Add(g);
            else AddEcNode(root, created, effectIndex, parent).Items.Add(g);
            return g;
        }

        // ---------- Effect Controls group toggles ----------
        private void FlipEcGroup(int effectIndex)
        {
            bool open = !_ecOpen.TryGetValue(effectIndex, out bool cur) || cur;
            _ecOpen[effectIndex] = !open;
            RefreshEcRows();
        }

        private void EcToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EcGroupVm g)
            {
                FlipEcGroup(g.EffectIndex);
                e.Handled = true;
            }
        }

        private void EcHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EcGroupVm g)
            {
                FlipEcGroup(g.EffectIndex);
                e.Handled = true;
            }
        }

        // named parameter group disclosure rows
        private void EcSubToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EcSubGroupVm g)
            {
                FlipEcSub(g);
                e.Handled = true;
            }
        }

        private void EcSub_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EcSubGroupVm g)
            {
                FlipEcSub(g);
                e.Handled = true;
            }
        }

        private void FlipEcSub(EcSubGroupVm g)
        {
            string key = g.EffectIndex + "|" + g.GroupKey;
            // the default here must mirror AddEcNode's (collapsed): the row
            // the user clicked renders from THAT default, so the flip has
            // to read the same base state or the first click does nothing
            bool open = _ecGroupOpen.TryGetValue(key, out bool cur) && cur;
            _ecGroupOpen[key] = !open;
            RefreshEcRows();
        }

        // ---------- inspector ----------
        private PresetEffectDetails CurrentDetails() =>
            _inspEffectIndex >= 0 && _inspEffectIndex < _details.Count ? _details[_inspEffectIndex] : null;

        private PresetParameter CurrentAnimParam()
        {
            var d = CurrentDetails();
            if (d == null || _animParamIndex < 0) return null;
            var anim = d.Parameters.Where(p => p.IsAnimated).ToList();
            return _animParamIndex < anim.Count ? anim[_animParamIndex] : null;
        }

        private void EffectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateInspector();
        }

        private void UpdateInspector()
        {
            if (!(EffectList.SelectedItem is EffectRowVm row))
            {
                _inspEffectIndex = -1;
                SetInspectorEmpty();
                return;
            }
            ShowEffect(row.EffectIndex, row.Name);
        }

        /// <summary>
        /// Populates the inspector for a stable effect index. Normally the
        /// (visible) list row drives this; Effect Controls clicks resolve
        /// their owning effect directly, even when that row is filtered
        /// out of the compatibility list.
        /// </summary>
        private void ShowEffect(int effectIndex, string fallbackName)
        {
            try
            {
                ShowEffectCore(effectIndex, fallbackName);
            }
            catch (Exception ex)
            {
                // the inspector degrades with the reason, like the Effect
                // Controls panel already does per block — a decode or
                // drawing surprise never takes the workspace down
                LogService.Append($"inspector: effect #{effectIndex + 1} couldn't be shown \u2014 {ex.GetType().Name}: {ex.Message}");
                _inspEffectIndex = effectIndex;
                InspTitle.Text = fallbackName;
                InspSub.Text = "\u26a0 couldn't be shown \u2014 " + ex.GetType().Name + ": " + ex.Message;
                InspEmpty.Visibility = Visibility.Collapsed;
                InspUnavailable.Visibility = Visibility.Visible;
                ParamList.ItemsSource = null;
                KfList.ItemsSource = null;
                SetComboSource(null);
                _animParamIndex = -1;
                _selKf = -1;
                StatusBarLeft.Text = "";
                StatusSep.Visibility = Visibility.Collapsed;
                DrawGraph();
            }
        }

        private void ShowEffectCore(int effectIndex, string fallbackName)
        {
            _inspEffectIndex = effectIndex;
            var d = CurrentDetails();

            if (d == null)
            {
                InspTitle.Text = fallbackName;
                InspSub.Text = "No parameter data available";
                InspEmpty.Visibility = Visibility.Collapsed;
                InspUnavailable.Visibility = Visibility.Visible;
                ParamList.ItemsSource = null;
                KfList.ItemsSource = null;
                SetComboSource(null);
                _selKf = -1;
                StatusBarLeft.Text = "";
                StatusSep.Visibility = Visibility.Collapsed;
                DrawGraph();
                return;
            }

            InspTitle.Text = string.IsNullOrEmpty(d.ShortName) ? fallbackName : d.ShortName;
            InspSub.Text = $"{d.MatchName}  ·  {d.Parameters.Count} parameter{(d.Parameters.Count == 1 ? "" : "s")}" +
                           $"  ·  {d.AnimatedCount} animated";
            InspEmpty.Visibility = Visibility.Collapsed;
            InspUnavailable.Visibility = Visibility.Collapsed;

            RefreshParamRows();

            var animParams = d.Parameters.Where(p => p.IsAnimated).ToList();
            SetComboSource(animParams.Select(p => p.Name).ToList());
            _animParamIndex = animParams.Count > 0 ? 0 : -1;
            _selKf = -1;
            _syncingCombo = true;
            KfParamSelect.SelectedIndex = _animParamIndex;
            GraphParamSelect.SelectedIndex = _animParamIndex;
            _syncingCombo = false;

            StatusBarLeft.Text = $"{d.Parameters.Count} parameters · {d.AnimatedCount} animated";
            StatusSep.Visibility = Visibility.Visible;

            BuildKeyframes();
            DrawGraph();
        }

        private void SetInspectorEmpty()
        {
            InspTitle.Text = "Inspector";
            InspSub.Text = "Select an effect to inspect it";
            InspEmpty.Visibility = Visibility.Visible;
            InspUnavailable.Visibility = Visibility.Collapsed;
            ParamList.ItemsSource = null;
            KfList.ItemsSource = null;
            SetComboSource(null);
            _animParamIndex = -1;
            _selKf = -1;
            StatusBarLeft.Text = "";
            StatusSep.Visibility = Visibility.Collapsed;
            BuildKeyframes();
            DrawGraph();
        }

        private void SetComboSource(List<string> names)
        {
            _syncingCombo = true;
            KfParamSelect.ItemsSource = names;
            GraphParamSelect.ItemsSource = names;
            KfParamSelect.SelectedIndex = -1;
            GraphParamSelect.SelectedIndex = -1;
            _syncingCombo = false;
        }

        private void AnimParam_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingCombo) return;
            int idx = sender == KfParamSelect ? KfParamSelect.SelectedIndex : GraphParamSelect.SelectedIndex;
            if (idx < 0) return;
            SelectAnimatedParam(idx);
        }

        /// <summary>
        /// Selects an animated parameter (combo, param row and navigator all
        /// funnel here), resets the keyframe selection and refreshes both tabs.
        /// </summary>
        private void SelectAnimatedParam(int animIndex)
        {
            if (animIndex < 0) return;
            _syncingCombo = true;
            KfParamSelect.SelectedIndex = animIndex;
            GraphParamSelect.SelectedIndex = animIndex;
            _syncingCombo = false;
            _animParamIndex = animIndex;
            _selKf = -1;
            BuildKeyframes();
            DrawGraph();
        }

        /// <summary>
        /// Position of p among the effect's animated parameters. Effect
        /// Controls clicks arrive without a (visible) list selection, so the
        /// owning effect is resolved from the decoded details on demand —
        /// which also makes the hidden inspector's state correct.
        /// </summary>
        private int AnimatedIndexOf(PresetParameter p)
        {
            if (p == null) return -1;
            var d = CurrentDetails();
            if (d != null)
            {
                var anim = d.Parameters.Where(x => x.IsAnimated).ToList();
                int idx = anim.IndexOf(p);
                if (idx >= 0) return idx;
            }
            for (int e = 0; e < _details.Count; e++)
            {
                if (_details[e].Parameters.Contains(p))
                {
                    var det = _details[e];
                    ShowEffect(e, string.IsNullOrEmpty(det.ShortName) ? det.MatchName : det.ShortName);
                    var anim = det.Parameters.Where(x => x.IsAnimated).ToList();
                    return anim.IndexOf(p);
                }
            }
            return -1;
        }

        /// <summary>
        /// Flattens an exception chain into one line — "Type: message ←
        /// caused by Type: message". The inner exception usually names the
        /// real fault (a XamlParseException's cause hides in its
        /// InnerException), so panel messages and log lines must carry the
        /// whole chain, not just the wrapper.
        /// </summary>
        private static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (var e = ex; e != null; e = e.InnerException)
                parts.Add(e.GetType().Name + ": " + e.Message);
            return string.Join(" \u2190 caused by ", parts);
        }

        /// <summary>
        /// Rebuilds the Parameters tab as one calm read: plain property
        /// lines under quiet, always-expanded group captions. No toggles
        /// and no chips — the Keyframes and Graph tabs carry the motion
        /// data. Cheap — the list is at most a few dozen rows.
        /// </summary>
        private void RefreshParamRows()
        {
            var d = CurrentDetails();
            if (d == null)
            {
                ParamList.ItemsSource = null;
                return;
            }
            var root = new List<object>();
            var created = new Dictionary<string, EcSubGroupVm>();
            foreach (var p in d.Parameters)
            {
                var row = new ParamRowVm(p) { RowOpacity = 1.0 };
                if (string.IsNullOrEmpty(p.Group)) root.Add(row);
                else AddInspNode(root, created, p.Group).Items.Add(row);
            }
            try
            {
                ParamList.ItemsSource = root;
                // realize the row templates HERE, inside the guard: a
                // template fault would otherwise surface in the layout
                // pass, past every catch in this path, as an app-level
                // crash dialog instead of a named inline reason
                ParamList.UpdateLayout();
                ParamEmpty.Text = "No decodable parameters in this effect block.";
                ParamEmpty.Visibility = root.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogService.Append($"inspector: the parameter panel couldn't be rendered \u2014 {Describe(ex)}");
                ParamList.ItemsSource = null;
                ParamEmpty.Text = "Parameter data for this effect couldn't be rendered \u2014 " + Describe(ex);
                ParamEmpty.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Inspector group caption for a group path, created on first
        /// encounter. Always expanded — the inspector's simple read never
        /// hides rows; the Effect Controls panel owns disclosure behavior.
        /// </summary>
        private static EcSubGroupVm AddInspNode(List<object> root,
            Dictionary<string, EcSubGroupVm> created, string path)
        {
            if (created.TryGetValue(path, out var g)) return g;
            string name = path, parent = null;
            int sep = path.LastIndexOf('\u0001');
            if (sep >= 0) { name = path.Substring(sep + 1); parent = path.Substring(0, sep); }
            g = new EcSubGroupVm { Title = name, GroupKey = path, EffectIndex = -1, Open = true };
            created[path] = g;
            if (parent == null) root.Add(g);
            else AddInspNode(root, created, parent).Items.Add(g);
            return g;
        }

        // ---------- AE parameter rows: click + keyframe navigator ----------
        // Both parameter UIs funnel here: the inspector's simple ParamRowVm
        // and the Effect Controls' kind-aware EcParamVm share the decoded
        // parameter behind the row.
        private static PresetParameter ParamRefOf(object dc) =>
            (dc as ParamRowVm)?.ParamRef ?? (dc as EcParamVm)?.ParamRef;

        private static bool IsAnimatedRow(object dc) =>
            (dc as ParamRowVm)?.IsAnimated ?? ((dc as EcParamVm)?.IsAnimated ?? false);

        private void ParamRow_Click(object sender, MouseButtonEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;
            if (IsAnimatedRow(dc))
                SelectAnimatedParam(AnimatedIndexOf(ParamRefOf(dc)));
        }

        private void KfPrev_Click(object sender, RoutedEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;
            if (IsAnimatedRow(dc))
            {
                SelectAnimatedParam(AnimatedIndexOf(ParamRefOf(dc)));
                SelectKeyframe(_selKf < 0 ? 0 : _selKf - 1);
            }
        }

        private void KfNext_Click(object sender, RoutedEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;
            if (IsAnimatedRow(dc))
            {
                SelectAnimatedParam(AnimatedIndexOf(ParamRefOf(dc)));
                SelectKeyframe(_selKf < 0 ? 0 : _selKf + 1);
            }
        }

        /// <summary>The diamond button: jump to the Keyframes tab for that stream.</summary>
        private void KfShow_Click(object sender, RoutedEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;
            if (IsAnimatedRow(dc))
            {
                SelectAnimatedParam(AnimatedIndexOf(ParamRefOf(dc)));
                if (_viewMode == 0) SetView(1); // the Keyframes tab lives in the inspector
                SetTab(1);
            }
        }

        private void KfRow_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is KfRowVm vm)
                SelectKeyframe(vm.KfIndex);
        }

        /// <summary>
        /// Selects a keyframe (clamped): highlights its row, prints its
        /// slope/influence numbers and draws the graph's selection ring.
        /// </summary>
        private void SelectKeyframe(int i)
        {
            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0)
            {
                _selKf = -1;
            }
            else
            {
                _selKf = Math.Max(0, Math.Min(p.Keyframes.Count - 1, i));
            }
            BuildKeyframes();
            if (_tab == 2) DrawGraph(); // ring + handles follow the pick
        }

        private void TabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender == TabParamsBtn) SetTab(0);
            else if (sender == TabKeyframesBtn) SetTab(1);
            else SetTab(2);
        }

        private void GraphMode_Click(object sender, RoutedEventArgs e)
        {
            _graphMode = sender == GraphSpeedBtn ? 1 : 0;
            GraphValueBtn.IsChecked = _graphMode == 0;
            GraphSpeedBtn.IsChecked = _graphMode == 1;
            GraphLegend.Text = _graphMode == 0
                ? "value over time · right axis = value"
                : "speed = |Δvalue / Δt| · right axis = speed";
            DrawGraph();
        }

        private void SetTab(int tab)
        {
            _tab = tab;
            if (TabParamsBtn == null) return; // XAML not loaded yet (design-time)
            TabParamsBtn.IsChecked = tab == 0;
            TabKeyframesBtn.IsChecked = tab == 1;
            TabGraphBtn.IsChecked = tab == 2;
            ParamPane.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
            KfPane.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
            GraphPane.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
            BuildKeyframes();
            DrawGraph();
            if (tab == 2)
            {
                // first paint after the pane was collapsed: ActualWidth is
                // still 0 in this layout pass — repaint once layout is done
                // instead of leaving a blank plot until the SizeChanged timer
                Dispatcher.BeginInvoke(new Action(DrawGraph), DispatcherPriority.Loaded);
            }
        }

        private void BuildKeyframes()
        {
            try
            {
                BuildKeyframesCore();
            }
            catch (Exception ex)
            {
                LogService.Append("keyframes: the list couldn't be built \u2014 " + ex.GetType().Name + ": " + ex.Message);
                KfList.ItemsSource = null;
                KfDetail.Visibility = Visibility.Collapsed;
                KfEmpty.Text = "The keyframe list couldn't be built \u2014 " + ex.GetType().Name + ": " + ex.Message + " (details in About \u2192 Logs)";
                KfEmpty.Visibility = Visibility.Visible;
                DrawKfTimeline();
            }
        }

        private void BuildKeyframesCore()
        {
            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0)
            {
                KfList.ItemsSource = null;
                KfEmpty.Text = KfEmptyDefault; // an earlier failure may have left a reason here
                KfEmpty.Visibility = Visibility.Visible;
                KfDetail.Visibility = Visibility.Collapsed;
                DrawKfTimeline();
                return;
            }
            KfEmpty.Visibility = Visibility.Collapsed;
            var kfs = p.Keyframes;
            KfList.ItemsSource = kfs.Select((k, i) =>
                new KfRowVm(i + 1, k, i > 0 ? kfs[i - 1] : null)
                {
                    Selected = i == _selKf
                }).ToList();

            // easing numbers for the selected keyframe — the AE Graph
            // Editor's numeric readout of speed and influence per side
            if (_selKf >= 0 && _selKf < kfs.Count)
            {
                var k = kfs[_selKf];
                KfDetail.Text = $"#{_selKf + 1} {k.InterpLabel} — in:  speed {k.InSlope.ToString("0.###")} /s · influence {Pct(k.InInfluence)}" +
                                $"   ·   out:  speed {k.OutSlope.ToString("0.###")} /s · influence {Pct(k.OutInfluence)}";
                KfDetail.Visibility = Visibility.Visible;

                // keep the picked row visible (the list is small and plain
                // StackPanel-hosted, so the container exists after layout)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (KfList.ItemContainerGenerator.ContainerFromIndex(_selKf) is FrameworkElement fe)
                            fe.BringIntoView();
                    }
                    catch { /* cosmetic */ }
                }), DispatcherPriority.Loaded);
            }
            else
            {
                KfDetail.Visibility = Visibility.Collapsed;
            }
            DrawKfTimeline();
        }

        // ---------- AE-style graph ----------
        private const double Fps = 30.0; // frame numbers + grid assume 30 fps

        /// <summary>The hint / empty texts the XAML ships with — restored
        /// whenever a pane falls back to its hint state, so a failure
        /// reason never sticks around after a successful redraw.</summary>
        private const string GraphHintDefault = "Pick an animated parameter to plot its curve \u2014 then click any keyframe to inspect its easing.";
        private const string KfEmptyDefault = "This effect has no animated parameters \u2014 its values are static. Static values are listed under Parameters.";

        /// <summary>AE-style timecode h:mm:ss:ff (hours only when needed).</summary>
        private static string Timecode(double sec)
        {
            int total = (int)Math.Round(sec * Fps);
            int f = total % 30, s = (total / 30) % 60, m = (total / 1800) % 60, h = total / 108000;
            return h > 0
                ? $"{h}:{m.ToString("00")}:{s.ToString("00")}:{f.ToString("00")}"
                : $"0:{m.ToString("00")}:{s.ToString("00")}:{f.ToString("00")}";
        }

        /// <summary>AE shows influences as percentages ("Influence: 33.3%").</summary>
        private static string Pct(double v) => (v * 100).ToString("0.#") + "%";

        private sealed class PlotState
        {
            public List<PresetCurve.Segment> Segs;
            public double T0, T1;      // seconds span
            public double W, H;        // canvas size
            public double L, R, T, Bot; // margins
            public int Mode;           // 0 value, 1 speed
            public double VMin, VMax;  // value mode y-range
            public double SpeedMax;    // speed mode y-range
        }

        private PlotState _plot;
        private Line _cursorLine;
        private Ellipse _cursorDot;

        /// <summary>
        /// AE-style graph editor for the selected animated parameter.
        /// Value mode: the value curve (linear segments straight, Hold steps,
        /// Bezier segments with AE tangent handles from PresetCurve) with
        /// interpolation-shaped keyframe markers — square = linear, diamond
        /// = bezier, left triangle = hold. Speed mode: the analytic |dv/dt|
        /// — the derivative of the very same cubic — drawn as AE's filled
        /// speed area. A selected
        /// keyframe gets AE's accent ring plus its tangent handles, in both
        /// tabs (graph markers, keyframe rows and the navigator stay in sync).
        /// Curve geometry is deliberately NOT pixel-snapped: rounding every
        /// coordinate quantized the beziers into visible kinks.
        /// Pure WPF shapes — Win7-safe, no bitmap effects.
        /// </summary>
        private void DrawGraph()
        {
            if (GraphCanvas == null) return;
            try
            {
                DrawGraphCore();
            }
            catch (Exception ex)
            {
                // a drawing surprise degrades the pane with the reason
                // instead of riding the global crash dialog; the SizeChanged
                // redraws keep calling in, so the next pass repaints normal
                LogService.Append("graph: couldn't be drawn \u2014 " + ex.GetType().Name + ": " + ex.Message);
                GraphCanvas.Children.Clear();
                _cursorLine = null;
                _cursorDot = null;
                _plot = null;
                if (GraphReadout != null) GraphReadout.Visibility = Visibility.Collapsed;
                GraphHint.Text = "The graph couldn't be drawn \u2014 " + ex.GetType().Name + ": " + ex.Message;
                GraphHint.Visibility = Visibility.Visible;
            }
        }

        private void DrawGraphCore()
        {
            if (GraphCanvas == null) return;
            GraphCanvas.Children.Clear();
            _cursorLine = null;
            _cursorDot = null;
            if (GraphReadout != null) GraphReadout.Visibility = Visibility.Collapsed;
            _plot = null;

            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0)
            {
                GraphHint.Text = GraphHintDefault; // an earlier failure may have left a reason here
                GraphHint.Visibility = Visibility.Visible;
                return;
            }
            GraphHint.Visibility = Visibility.Collapsed;

            double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
            if (w < 60 || h < 60) return; // not laid out yet — SizeChanged redraws

            var segs = PresetCurve.BuildSegments(p.Keyframes);
            // a one-keyframe stream has no spans but still plots — the value
            // branch draws it as AE does: a constant. No hint fallback here.

            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");
            var accent = (Brush)FindResource("B.Primary");
            var surface = (Brush)FindResource("B.Surface");
            var outline = (Brush)FindResource("B.Outline");

            const double L = 14, R = 48, T = 14, Bot = 24;
            // span from the KEYFRAMES, not the segments — a one-keyframe
            // stream has zero segments yet still plots as a constant
            double t0 = PresetCurve.Seconds(p.Keyframes[0].Time);
            double t1 = PresetCurve.Seconds(p.Keyframes[p.Keyframes.Count - 1].Time);
            if (t1 - t0 < 1e-6) t1 = t0 + 0.5; // single-instant stream still gets an axis
            Func<double, double> xOf = t => L + (t - t0) / (t1 - t0) * (w - L - R);

            DrawTimeGrid(t0, t1, xOf, w, h, T, Bot, L, R, gridBrush, labelBrush);

            // AE's plot axes: a solid left (value) and bottom (time) line so
            // the plot reads as a chart, not a floating cloud
            double axisX = Math.Round(L) + 0.5;
            GraphCanvas.Children.Add(new Line
            {
                X1 = axisX, Y1 = T, X2 = axisX, Y2 = h - Bot,
                Stroke = outline, StrokeThickness = 1
            });
            GraphCanvas.Children.Add(new Line
            {
                X1 = axisX, Y1 = Math.Round(h - Bot) + 0.5, X2 = w - R, Y2 = Math.Round(h - Bot) + 0.5,
                Stroke = outline, StrokeThickness = 1
            });
            // AE's value ruler lives on the RIGHT edge of the plot — the
            // labels sit in the right gutter, outside the plot area
            double rulerX = Math.Round(w - R) - 0.5;
            GraphCanvas.Children.Add(new Line
            {
                X1 = rulerX, Y1 = T, X2 = rulerX, Y2 = h - Bot,
                Stroke = outline, StrokeThickness = 1
            });

            var plot = new PlotState
            {
                Segs = segs, T0 = t0, T1 = t1,
                W = w, H = h, L = L, R = R, T = T, Bot = Bot, Mode = _graphMode
            };

            double plotH = h - T - Bot;
            if (_graphMode == 0)
            {
                // AE scales the plot to the CURVE: keyframe values plus the
                // path's own samples set the range BEFORE the y mapping is
                // built, so the scale can never depend on draw order
                double vMin = p.Keyframes.Min(k => k.Value);
                double vMax = p.Keyframes.Max(k => k.Value);
                PresetCurve.SampleValues(segs, 240, out _, out var sampled);
                foreach (double v in sampled)
                {
                    if (v < vMin) vMin = v;
                    if (v > vMax) vMax = v;
                }
                if (vMax - vMin < 1e-9) { vMin -= 1; vMax += 1; }
                else { double pad = (vMax - vMin) * 0.12; vMin -= pad; vMax += pad; }
                plot.VMin = vMin; plot.VMax = vMax;

                Func<double, double> yOf = v => T + (plot.VMax - v) / Math.Max(plot.VMax - plot.VMin, 1e-9) * plotH;
                DrawValueGrid(plot, yOf);
                if (segs.Count == 0)
                    DrawSingleKeyframePlot(plot, p.Keyframes[0], accent, surface, yOf);
                else
                    DrawValueCurve(plot, p, accent, surface, yOf);
                DrawSelection(plot, yOf, accent, surface);
            }
            else
            {
                Func<double, double> yOf = v => T + (plot.SpeedMax - v) / Math.Max(plot.SpeedMax, 1e-9) * plotH;
                DrawSpeedCurve(plot, accent, surface, yOf);
                DrawSelection(plot, yOf, accent, surface);
            }

            _plot = plot;
        }

        /// <summary>
        /// Coalesces redraw storms while the window is resized: each
        /// SizeChanged restarts a 70 ms one-shot; the graph rebuilds once
        /// the storm pauses, then once more when it ends.
        /// </summary>
        private void ScheduleGraphRedraw()
        {
            if (_graphRedrawTimer == null)
            {
                _graphRedrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                _graphRedrawTimer.Tick += (s, e) =>
                {
                    _graphRedrawTimer.Stop();
                    DrawGraph();
                };
            }
            _graphRedrawTimer.Stop();
            _graphRedrawTimer.Start();
        }

        /// <summary>Vertical grid at whole-frame multiples of 30 fps.</summary>
        private void DrawTimeGrid(double t0, double t1, Func<double, double> xOf,
            double w, double h, double top, double bot, double L, double R,
            Brush gridBrush, Brush labelBrush)
        {
            // pick the coarsest whole-frame step that keeps labels ~78px apart
            double targetLines = Math.Max(2.0, (w - L - R) / 78.0);
            double frameSpan = (t1 - t0) * Fps;
            double needStep = frameSpan / targetLines;
            double[] steps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
            double step = steps.FirstOrDefault(s => s >= needStep);
            if (step == 0) step = steps[steps.Length - 1];

            double stepSec = step / Fps;
            int first = (int)Math.Ceiling(t0 * Fps / step - 1e-9);
            double lastLabelX = -1000;
            for (int f = first; ; f += (int)step)
            {
                double t = f / Fps;
                if (t > t1 + 1e-9) break;
                double x = Math.Round(xOf(t)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = top, Y2 = h - bot,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                if (x - lastLabelX >= 44)
                {
                    AddTimeLabel(x + 4, h - bot + 6, t.ToString("0.##") + "s", labelBrush);
                    lastLabelX = x;
                }
            }
        }

        /// <summary>
        /// Horizontal value grid + right-gutter labels — AE's value ruler.
        /// Labels format to their step (AxisNum): the old "0.##" collapsed
        /// every small-magnitude gridline to "0", which read as a broken
        /// axis on presets whose values live below 0.01.
        /// </summary>
        private void DrawValueGrid(PlotState plot, Func<double, double> yOf)
        {
            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");
            double vStep = NiceStep((plot.VMax - plot.VMin) / 3.2);
            double lastLabelY = -1000;
            for (double v = Math.Ceiling(plot.VMin / vStep) * vStep; v <= plot.VMax + 1e-9; v += vStep)
            {
                double y = Math.Round(yOf(v)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = plot.L, X2 = plot.W - plot.R, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                if (Math.Abs(y - lastLabelY) >= 13)
                {
                    var lbl = new TextBlock
                    {
                        Text = AxisNum(v, vStep),
                        FontSize = 10,
                        Foreground = labelBrush,
                        Width = plot.R - 8,
                        TextAlignment = TextAlignment.Right
                    };
                    Canvas.SetLeft(lbl, plot.W - plot.R + 6);
                    Canvas.SetTop(lbl, y - 6);
                    GraphCanvas.Children.Add(lbl);
                    lastLabelY = y;
                }
            }
        }

        /// <summary>
        /// A one-keyframe stream is animated but has no spans: AE shows a
        /// constant. Draw the flat value line plus its keyframe marker —
        /// the old code fell back to the "pick a parameter" hint here,
        /// which read as a broken graph.
        /// </summary>
        private void DrawSingleKeyframePlot(PlotState plot, PresetKeyframe kf,
            Brush accent, Brush surface, Func<double, double> yOf)
        {
            double y = yOf(kf.Value);
            GraphCanvas.Children.Add(new Line
            {
                X1 = plot.L, Y1 = y, X2 = plot.W - plot.R, Y2 = y,
                Stroke = accent, StrokeThickness = 2, Opacity = 0.9
            });
            string tip = $"{Timecode(PresetCurve.Seconds(kf.Time))}  ·  constant value {kf.Value.ToString("0.###")}  ·  click to select";
            GraphCanvas.Children.Add(MakeMarker(PresetCurve.InterpLinear,
                XOf(plot, plot.T0), y, accent, surface, tip, 0));
        }

        /// <summary>
        /// Axis number format that adapts to the step size: whole steps get
        /// "0.#", fractional steps get exactly as many decimals as the step
        /// needs, and extreme magnitudes fall back to significant digits.
        /// </summary>
        private static string AxisNum(double v, double step)
        {
            if (Math.Abs(v) < 1e-12) return "0";
            if (Math.Abs(v) >= 100000 || Math.Abs(v) < 0.0005) return v.ToString("G4");
            int dec = 6;
            for (int d = 0; d <= 6; d++)
            {
                double scaled = step * Math.Pow(10, d);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6) { dec = d; break; }
            }
            string s = v.ToString("F" + dec);
            if (dec > 0) s = s.TrimEnd('0').TrimEnd('.');
            return s;
        }

        private void DrawValueCurve(PlotState plot, PresetParameter stream, Brush accent, Brush surface, Func<double, double> yOf)
        {
            var segs = plot.Segs;

            // the curve itself — smooth coordinates, no pixel snapping
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(XOf(plot, segs[0].T0), yOf(segs[0].V0)), false, false);
                foreach (var s in segs)
                {
                    var end = new Point(XOf(plot, s.T1), yOf(s.V1));
                    if (s.Mode == PresetCurve.InterpHold)
                    {
                        ctx.LineTo(new Point(end.X, yOf(s.V0)), true, false);
                        ctx.LineTo(end, true, false);
                    }
                    else if (s.Mode == PresetCurve.InterpLinear)
                    {
                        ctx.LineTo(end, true, false);
                    }
                    else
                    {
                        var c1 = new Point(XOf(plot, s.C1T), yOf(s.C1V));
                        var c2 = new Point(XOf(plot, s.C2T), yOf(s.C2V));
                        ctx.BezierTo(c1, c2, end, true, false);
                    }
                }
            }
            geo.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = geo,
                Stroke = accent,
                StrokeThickness = 2.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            // keyframe markers, shaped by interpolation like AE's icons,
            // clickable to select (ring + handles + easing numbers)
            for (int i = 0; i < stream.Keyframes.Count; i++)
            {
                var k = stream.Keyframes[i];
                int shape = i + 1 < stream.Keyframes.Count ? k.InterpOut : k.InterpIn;
                double x = XOf(plot, PresetCurve.Seconds(k.Time));
                double y = yOf(k.Value);
                string tip = $"#{i + 1}  {Timecode(PresetCurve.Seconds(k.Time))}" +
                             $"  ·  value {k.Value.ToString("0.###")}  ·  {k.InterpLabel}  ·  click to select";
                int idx = i;
                var marker = MakeMarker(shape, x, y, accent, surface, tip, idx);
                GraphCanvas.Children.Add(marker);
            }
        }

        private void DrawSpeedCurve(PlotState plot, Brush accent, Brush surface, Func<double, double> yOf)
        {
            var segs = plot.Segs;
            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");

            // analytic speed |dv/dt| — the derivative of the SAME cubic the
            // value graph draws, so the two editors always agree (finite
            // differences of samples used to trace a different curve)
            const int N = 320;
            double[] ts = new double[N], sp = new double[N];
            // span from the plot (keyframe-derived), so a one-keyframe
            // stream — zero segments, constant value, zero speed — still
            // draws its flat baseline instead of crashing on segs[0]
            double st0 = plot.T0, st1 = plot.T1;
            for (int i = 0; i < N; i++)
            {
                ts[i] = st0 + (st1 - st0) * i / (N - 1);
                double s = PresetCurve.SpeedAt(segs, ts[i]);
                sp[i] = double.IsNaN(s) ? 0 : s;
            }

            double spMax = sp.Max();
            if (spMax < 1e-9) spMax = 1.0;
            spMax *= 1.1; // headroom so the peak never kisses the top edge
            plot.SpeedMax = spMax;

            double baseY = Math.Round(plot.H - plot.Bot) + 0.5;

            // horizontal speed grid; labels right-aligned in the gutter
            double vStep = NiceStep(spMax / 3.0);
            double lastLabelY = -1000;
            for (double v = vStep; v <= spMax + 1e-9; v += vStep)
            {
                double y = Math.Round(yOf(v)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = plot.L, X2 = plot.W - plot.R, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                if (Math.Abs(y - lastLabelY) >= 13)
                {
                    var lbl = new TextBlock
                    {
                        Text = AxisNum(v, vStep) + "/s",
                        FontSize = 10,
                        Foreground = labelBrush,
                        Width = plot.R - 8,
                        TextAlignment = TextAlignment.Right
                    };
                    Canvas.SetLeft(lbl, plot.W - plot.R + 6);
                    Canvas.SetTop(lbl, y - 6);
                    GraphCanvas.Children.Add(lbl);
                    lastLabelY = y;
                }
            }

            // AE-style: the speed graph reads as a filled area over the axis
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(XOf(plot, ts[0]), baseY), true, false);
                for (int i = 0; i < N; i++)
                    ctx.LineTo(new Point(XOf(plot, ts[i]), yOf(sp[i])), true, false);
                ctx.LineTo(new Point(XOf(plot, ts[N - 1]), baseY), true, false);
            }
            area.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = area,
                Fill = accent,
                Opacity = 0.30,
                StrokeThickness = 0
            });

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(new Point(XOf(plot, ts[0]), yOf(sp[0])), false, false);
                for (int i = 1; i < N; i++)
                    ctx.LineTo(new Point(XOf(plot, ts[i]), yOf(sp[i])), true, false);
            }
            line.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = line,
                Stroke = accent,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            // keyframe markers on the speed curve: exact analytic speed at
            // each kf, clickable like their value-graph twins
            var stream = CurrentAnimParam();
            if (stream == null) return;
            for (int i = 0; i < stream.Keyframes.Count; i++)
            {
                var k = stream.Keyframes[i];
                double t = PresetCurve.Seconds(k.Time);
                double s = PresetCurve.SpeedAt(segs, t);
                if (double.IsNaN(s)) s = 0; // one-keyframe stream: constant = zero speed
                string tip = $"#{i + 1}  {Timecode(t)}  ·  speed {s.ToString("0.###")} /s  ·  click to select";
                int idx = i;
                GraphCanvas.Children.Add(MakeMarker(PresetCurve.InterpBezier,
                    XOf(plot, t), yOf(Math.Min(s, spMax)), accent, surface, tip, idx));
            }
        }

        /// <summary>
        /// AE-timeline strip for the Keyframes tab: a frame-grid baseline
        /// with the stream's keyframes as clickable markers and the selected
        /// one ringed — the "when do the keys land" view; the table below
        /// carries the exact numbers. Pure shapes, redrawn on size and
        /// theme changes.
        /// </summary>
        private void DrawKfTimeline()
        {
            if (KfTimeline == null) return;
            try
            {
                DrawKfTimelineCore();
            }
            catch (Exception ex)
            {
                // the strip degrades to empty; the table and the log
                // carry the reason
                LogService.Append("keyframe timeline: couldn't be drawn \u2014 " + ex.GetType().Name + ": " + ex.Message);
                KfTimeline.Children.Clear();
            }
        }

        private void DrawKfTimelineCore()
        {
            if (KfTimeline == null) return;
            KfTimeline.Children.Clear();
            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0) return;
            double w = KfTimeline.ActualWidth;
            if (w < 60) return; // not laid out yet — SizeChanged redraws

            var accent = (Brush)FindResource("B.Primary");
            var surface = (Brush)FindResource("B.Surface");
            var grid = (Brush)FindResource("B.OutlineVariant");
            var label = (Brush)FindResource("B.OnSurfaceVariant");

            const double y = 16, pad = 18;
            var kfs = p.Keyframes;
            double t0 = PresetCurve.Seconds(kfs[0].Time);
            double t1 = PresetCurve.Seconds(kfs[kfs.Count - 1].Time);
            if (t1 - t0 < 1e-6) t1 = t0 + 0.5;
            Func<double, double> xOf = t => pad + (t - t0) / (t1 - t0) * (w - 2 * pad);

            // baseline + round time ticks, coarsest step that keeps ~5 of them
            double midY = y + 0.5;
            KfTimeline.Children.Add(new Line
            {
                X1 = pad, Y1 = midY, X2 = w - pad, Y2 = midY,
                Stroke = grid, StrokeThickness = 1
            });
            double stepSec = NiceStep((t1 - t0) / 5.0);
            for (double t = Math.Ceiling(t0 / stepSec) * stepSec; t <= t1 + 1e-9; t += stepSec)
            {
                double x = Math.Round(xOf(t)) + 0.5;
                KfTimeline.Children.Add(new Line
                {
                    X1 = x, Y1 = y - 3, X2 = x, Y2 = y + 3,
                    Stroke = grid, StrokeThickness = 1
                });
            }

            // end timecodes (the table carries every exact per-key time)
            var first = new TextBlock { Text = Timecode(t0), FontSize = 9.5, Foreground = label };
            Canvas.SetLeft(first, pad - 2);
            Canvas.SetTop(first, y + 9);
            KfTimeline.Children.Add(first);
            double x1 = xOf(t1);
            if (kfs.Count > 1 && x1 > 128) // both timecodes need ~72px each
            {
                var last = new TextBlock { Text = Timecode(t1), FontSize = 9.5, Foreground = label, Opacity = 0.85 };
                Canvas.SetLeft(last, x1 - 56);
                Canvas.SetTop(last, y + 9);
                KfTimeline.Children.Add(last);
            }

            // keyframe markers — the same AE squares as the graph, clickable
            for (int i = 0; i < kfs.Count; i++)
            {
                var k = kfs[i];
                int shape = i + 1 < kfs.Count ? k.InterpOut : k.InterpIn;
                double t = PresetCurve.Seconds(k.Time);
                string tip = $"#{i + 1}  {Timecode(t)}  ·  value {k.Value.ToString("0.###")}  ·  click to select";
                KfTimeline.Children.Add(MakeMarker(shape, xOf(t), y, accent, surface, tip, i));
            }

            // selection ring over its marker, in sync with the table + graph
            if (_selKf >= 0 && _selKf < kfs.Count)
            {
                double x = xOf(PresetCurve.Seconds(kfs[_selKf].Time));
                var ring = new Ellipse
                {
                    Width = 15, Height = 15, Stroke = accent, StrokeThickness = 1.6,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, x - 7.5);
                Canvas.SetTop(ring, y - 7.5);
                KfTimeline.Children.Add(ring);
            }
        }

        /// <summary>
        /// The selected keyframe, AE Graph Editor style: an accent ring on
        /// its marker (value or speed position) plus its tangent handles in
        /// value mode — thin lines to the two control points with hollow
        /// handle dots, only for bezier segments, exactly when AE shows them.
        /// </summary>
        private void DrawSelection(PlotState plot, Func<double, double> yOf, Brush accent, Brush surface)
        {
            var stream = CurrentAnimParam();
            if (stream == null || _selKf < 0 || _selKf >= stream.Keyframes.Count) return;

            var k = stream.Keyframes[_selKf];
            double t = PresetCurve.Seconds(k.Time);
            double kx = XOf(plot, t);

            // AE time marker: a dashed accent playhead at the selected
            // keyframe's time, in both modes, under the ring
            GraphCanvas.Children.Add(new Line
            {
                X1 = kx, X2 = kx, Y1 = plot.T, Y2 = plot.H - plot.Bot,
                Stroke = accent, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Opacity = 0.7, IsHitTestVisible = false
            });

            if (plot.Mode == 0)
            {
                double ky = yOf(k.Value);
                var handleBrush = (Brush)FindResource("B.Outline");
                // outgoing handle: control point 1 of the segment that starts here
                if (_selKf < plot.Segs.Count && plot.Segs[_selKf].Mode == PresetCurve.InterpBezier)
                {
                    var s = plot.Segs[_selKf];
                    AddHandle(kx, ky, XOf(plot, s.C1T), yOf(s.C1V), handleBrush, surface);
                }
                // incoming handle: control point 2 of the segment that ends here
                if (_selKf > 0 && plot.Segs[_selKf - 1].Mode == PresetCurve.InterpBezier)
                {
                    var s = plot.Segs[_selKf - 1];
                    AddHandle(kx, ky, XOf(plot, s.C2T), yOf(s.C2V), handleBrush, surface);
                }
            }

            double y = plot.Mode == 0 ? yOf(k.Value) : yOf(Math.Min(PresetCurve.SpeedAt(plot.Segs, t), plot.SpeedMax));
            if (double.IsNaN(y)) return;
            var ring = new Ellipse
            {
                Width = 15,
                Height = 15,
                Stroke = accent,
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, kx - 7.5);
            Canvas.SetTop(ring, y - 7.5);
            GraphCanvas.Children.Add(ring);
        }

        /// <summary>One tangent handle: a thin line plus a hollow handle dot.</summary>
        private void AddHandle(double x0, double y0, double x1, double y1, Brush lineBrush, Brush fill)
        {
            GraphCanvas.Children.Add(new Line
            {
                X1 = x0, Y1 = y0, X2 = x1, Y2 = y1,
                Stroke = lineBrush, StrokeThickness = 1
            });
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = fill,
                Stroke = lineBrush,
                StrokeThickness = 1.2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x1 - 3.5);
            Canvas.SetTop(dot, y1 - 3.5);
            GraphCanvas.Children.Add(dot);
        }

        /// <summary>
        /// AE Graph Editor keyframes are small SQUARES; the interpolation
        /// reads through the fill — hollow = linear, filled = bezier,
        /// left-half-filled = hold. kfIndex &gt;= 0 makes the marker
        /// clickable for selection.
        /// </summary>
        private UIElement MakeMarker(int interp, double x, double y, Brush fill, Brush stroke, string tip, int kfIndex)
        {
            const double S = 9, Half = 4.5;
            UIElement el;
            if (interp == PresetCurve.InterpHold)
            {
                var host = new Canvas { Width = S, Height = S };
                host.Children.Add(new Rectangle
                {
                    Width = Half, Height = S, RadiusX = 1.5, RadiusY = 1.5, Fill = fill
                });
                host.Children.Add(new Rectangle
                {
                    Width = S, Height = S, RadiusX = 1.5, RadiusY = 1.5,
                    Fill = Brushes.Transparent, // full-rect hit target
                    Stroke = fill, StrokeThickness = 1.2
                });
                el = host;
            }
            else if (interp == PresetCurve.InterpLinear)
            {
                el = new Rectangle
                {
                    Width = S, Height = S, RadiusX = 1.5, RadiusY = 1.5,
                    // transparent fill keeps the whole square clickable
                    // while the marker still reads as hollow
                    Fill = Brushes.Transparent,
                    Stroke = fill, StrokeThickness = 1.4
                };
            }
            else
            {
                el = new Rectangle
                {
                    Width = S, Height = S, RadiusX = 1.5, RadiusY = 1.5,
                    Fill = fill, Stroke = stroke, StrokeThickness = 1.2
                };
            }

            Canvas.SetLeft(el, x - Half);
            Canvas.SetTop(el, y - Half);

            if (kfIndex >= 0)
            {
                var fe = (FrameworkElement)el;
                fe.Cursor = Cursors.Hand;
                fe.ToolTip = tip;
                fe.MouseLeftButtonUp += (s, e) =>
                {
                    SelectKeyframe(kfIndex);
                    e.Handled = true;
                };
            }
            return el;
        }

        /// <summary>
        /// Hover probe: dashed vertical cursor + floating readout with the
        /// exact time (and its frame number), the interpolated value (value
        /// mode) or numeric speed (speed mode) under the mouse.
        /// </summary>
        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_plot == null || GraphCanvas == null) return;
            var pt = e.GetPosition(GraphCanvas);
            double innerW = _plot.W - _plot.L - _plot.R;
            if (pt.X < _plot.L || pt.X > _plot.W - _plot.R || pt.Y > _plot.H - _plot.Bot)
            {
                ClearCursor();
                return;
            }
            double t = _plot.T0 + (pt.X - _plot.L) / innerW * (_plot.T1 - _plot.T0);
            int frame = (int)Math.Round(t * Fps);

            EnsureCursor();
            double x = Math.Round(pt.X) + 0.5;
            _cursorLine.Visibility = Visibility.Visible; // re-shown after ClearCursor
            _cursorLine.X1 = x;
            _cursorLine.X2 = x;
            _cursorLine.Y1 = _plot.T;
            _cursorLine.Y2 = _plot.H - _plot.Bot;

            string readout;
            string easing = SegmentEasingAt(t);
            double plotH = _plot.H - _plot.T - _plot.Bot;
            if (_plot.Mode == 0)
            {
                double v = PresetCurve.ValueAt(_plot.Segs, t);
                if (double.IsNaN(v))
                {
                    // one-keyframe stream: no spans, the constant holds
                    var pp = CurrentAnimParam();
                    if (pp != null && pp.Keyframes.Count > 0) v = pp.Keyframes[0].Value;
                }
                double y = _plot.T + (_plot.VMax - v) / Math.Max(_plot.VMax - _plot.VMin, 1e-9) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = $"{t.ToString("0.##")} s · f{frame} · value {v.ToString("0.###")}{easing}";
            }
            else
            {
                double s = PresetCurve.SpeedAt(_plot.Segs, t);
                double y = _plot.T + (1 - Math.Min(Math.Max(s, 0) / Math.Max(_plot.SpeedMax, 1e-9), 1)) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = double.IsNaN(s)
                    ? $"{t.ToString("0.##")} s · f{frame}"
                    : $"{t.ToString("0.##")} s · f{frame} · speed {s.ToString("0.###")} /s{easing}";
            }

            GraphReadoutText.Text = readout;
            GraphReadout.Visibility = Visibility.Visible;
        }

        private void GraphCanvas_MouseLeave(object sender, MouseEventArgs e) => ClearCursor();

        /// <summary>The interpolation of the span under time t ("" outside).</summary>
        private string SegmentEasingAt(double t)
        {
            if (_plot == null) return "";
            foreach (var s in _plot.Segs)
            {
                if (t < s.T0 || t > s.T1) continue;
                return s.Mode == PresetCurve.InterpLinear ? " · linear"
                     : s.Mode == PresetCurve.InterpHold ? " · hold"
                     : " · bezier";
            }
            return "";
        }

        private void EnsureCursor()
        {
            if (_cursorLine != null) return;
            var outline = (Brush)FindResource("B.Outline");
            var accent = (Brush)FindResource("B.Primary");
            var surface = (Brush)FindResource("B.Surface");
            _cursorLine = new Line
            {
                Stroke = outline,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                IsHitTestVisible = false
            };
            _cursorDot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = accent,
                Stroke = surface,
                StrokeThickness = 1.2,
                IsHitTestVisible = false
            };
            GraphCanvas.Children.Add(_cursorLine);
            GraphCanvas.Children.Add(_cursorDot);
        }

        private void ClearCursor()
        {
            if (_cursorLine != null) _cursorLine.Visibility = Visibility.Collapsed;
            if (_cursorDot != null) _cursorDot.Visibility = Visibility.Collapsed;
            if (GraphReadout != null) GraphReadout.Visibility = Visibility.Collapsed;
        }

        private double XOf(PlotState plot, double t) =>
            plot.L + (t - plot.T0) / Math.Max(plot.T1 - plot.T0, 1e-9) * (plot.W - plot.L - plot.R);

        /// <summary>Round axis step from a raw division: 1/2/2.5/5 × 10^n.</summary>
        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double step = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
            return step * mag;
        }

        private void AddTimeLabel(double x, double y, string text, Brush labelBrush)
        {
            var lbl = new TextBlock { Text = text, FontSize = 10, Foreground = labelBrush };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y);
            GraphCanvas.Children.Add(lbl);
        }
    }

    /// <summary>
    /// One named parameter group inside an Effect Controls block — AE's
    /// collapsible sub-groups ("Compositing Options" and friends). A
    /// top-level class because the XAML template selector references it.
    /// </summary>
    public class EcSubGroupVm
    {
        public string Title { get; set; }
        public string GroupKey { get; set; }
        public int EffectIndex { get; set; }
        public bool Open { get; set; }
        public Visibility BodyVisible => Open ? Visibility.Visible : Visibility.Collapsed;
        public List<object> Items { get; set; } = new List<object>();
    }

    /// <summary>
    /// Picks the Effect Controls body template: a group disclosure row for
    /// EcSubGroupVm nodes, the AE property line for everything else.
    /// </summary>
    public sealed class EcBodySelector : DataTemplateSelector
    {
        public DataTemplate GroupTemplate { get; set; }
        public string GroupTemplateKey { get; set; }
        public DataTemplate RowTemplate { get; set; }
        private DataTemplate _groupByKey;

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (!(item is EcSubGroupVm)) return RowTemplate;
            if (GroupTemplate != null) return GroupTemplate;
            if (_groupByKey != null) return _groupByKey;
            // Recursive group templates cannot be wired with
            // {StaticResource Key} inside their own content: WPF expands
            // template content off-tree, where the template's own key is
            // unreachable ("Cannot find resource named ..."). Resolving
            // the same key from the live container is the supported
            // recursion path — the container is in the tree, so the page
            // resource lookup succeeds.
            if (!string.IsNullOrEmpty(GroupTemplateKey) && container is FrameworkElement fe)
                _groupByKey = fe.TryFindResource(GroupTemplateKey) as DataTemplate;
            return _groupByKey;
        }
    }
}
