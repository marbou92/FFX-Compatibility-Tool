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
    /// The right-hand inspector adds a deep dive into the selected effect:
    /// its decoded parameters, the timed keyframe stream of any animated
    /// parameter (seconds + 30 fps frame numbers, converted by
    /// PresetCurve), and an AE-style graph editor with Value and Speed
    /// modes, a frame-based time grid and a hover probe (PresetInspector
    /// reads, never writes — the pipeline's keyframes are untouched).
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

        public class ParamRowVm
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public string Chip { get; set; }
            public Visibility ChipVisible { get; set; }
            public Visibility AccentVisible { get; set; }
            public string MatchName { get; set; }

            public ParamRowVm(PresetParameter p)
            {
                Name = p.Name;
                MatchName = p.MatchName ?? p.Name;

                if (p.IsAnimated)
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
                    Chip = $"{p.Keyframes.Count} key{(p.Keyframes.Count == 1 ? "" : "s")}";
                    ChipVisible = Visibility.Visible;
                    AccentVisible = Visibility.Visible;
                }
                else
                {
                    string range = p.Min.HasValue && p.Max.HasValue
                        ? $" · range {Fmt(p.Min)} … {Fmt(p.Max)}" : "";
                    Detail = "static value" + range;
                    AccentVisible = Visibility.Collapsed;
                    if (p.StaticValue.HasValue)
                    {
                        Chip = Fmt(p.StaticValue);
                        ChipVisible = Visibility.Visible;
                    }
                    else
                    {
                        Chip = "";
                        ChipVisible = Visibility.Collapsed;
                    }
                }
            }

            static string Fmt(double? v) => v?.ToString("0.###") ?? "—";
        }

        public class KfRowVm
        {
            public string Index { get; set; }
            public string TimeSec { get; set; }
            public string Sub { get; set; }
            public string Value { get; set; }
            public string Interp { get; set; }
            public string Tip { get; set; }

            public KfRowVm(int index, PresetKeyframe kf, PresetKeyframe prev)
            {
                Index = index.ToString();
                // ticks → seconds via PresetCurve's empirically derived
                // timebase (1 tick = 1/1024 s); raw ticks stay in the tooltip
                double sec = PresetCurve.Seconds(kf.Time);
                TimeSec = sec.ToString("0.##") + " s";
                int frame = (int)Math.Round(sec * Fps);
                Sub = prev == null
                    ? $"frame {frame}"
                    : $"frame {frame} · +{(sec - PresetCurve.Seconds(prev.Time)).ToString("0.##")} s";
                Value = kf.Value.ToString("0.###");
                Interp = kf.InterpLabel;
                Tip = $"raw time {kf.Time} ticks · in influence {kf.InInfluence.ToString("0.##")}" +
                      $" · out influence {kf.OutInfluence.ToString("0.##")}";
            }
        }

        private readonly PluginProfile _profile;
        private List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();
        private List<PresetEffectDetails> _details = new List<PresetEffectDetails>();
        private readonly ObservableCollection<EffectRowVm> _rows = new ObservableCollection<EffectRowVm>();
        private int _filterMode; // 0 all, 1 missing only, 2 compatible only
        private bool _sortDesc;

        // inspector state: selected effect (by stable effect index), selected
        // animated parameter (for the keyframes/graph tabs), active tab,
        // graph mode (0 = value like AE's value graph, 1 = speed graph)
        private int _inspEffectIndex = -1;
        private int _animParamIndex = -1;
        private int _tab;
        private int _graphMode;
        private bool _syncingCombo;

        // DragEnter/DragLeave fire on every child boundary crossing; a depth
        // counter is the only flicker-free way to know the drag truly left.
        private int _dragDepth;

        public ListerPage(PluginProfile profile)
        {
            InitializeComponent();
            _profile = profile;
            EffectList.ItemsSource = _rows;
            StatusBarVersion.Text = $"FFX Compatibility Tool {AppInfo.DisplayVersion}";
            UpdateRecentCard();
            GraphCanvas.SizeChanged += (s, e) => DrawGraph();
            SetTab(0);
            // row container brushes are captured per Refresh — re-run when the
            // theme changes so status tints match the new palette/mode; the
            // graph bakes brush colors too, so it redraws as well
            ThemeService.Changed += () => { Refresh(); DrawGraph(); };
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
                FileChipText.Text = System.IO.Path.GetFileName(path);
                byte[] bytes = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(bytes);

                // deep inspection is additive — if a preset carries a
                // structure the inspector can't decode, the list above still
                // works and the inspector explains itself
                try { _details = PresetInspector.Inspect(bytes); }
                catch { _details = new List<PresetEffectDetails>(); }

                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));
                Refresh();
                if (_rows.Count > 0) EffectList.SelectedIndex = 0; // open the inspector right away
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"Failed to read '{System.IO.Path.GetFileName(path)}':\n{ex.Message}",
                    "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                FileChipText.Text = "No file loaded";
                _currentEffects = new List<Pipeline.EffectInfo>();
                _details = new List<PresetEffectDetails>();
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
            }

            bool hasContent = _currentEffects.Any(e => !e.IsSentinel);
            EmptyState.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
            SplitHost.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;

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

            _inspEffectIndex = row.EffectIndex;
            var d = CurrentDetails();

            if (d == null)
            {
                InspTitle.Text = row.Name;
                InspSub.Text = "No parameter data available";
                InspEmpty.Visibility = Visibility.Collapsed;
                InspUnavailable.Visibility = Visibility.Visible;
                ParamList.ItemsSource = null;
                KfList.ItemsSource = null;
                SetComboSource(null);
                StatusBarLeft.Text = "";
                StatusSep.Visibility = Visibility.Collapsed;
                DrawGraph();
                return;
            }

            InspTitle.Text = string.IsNullOrEmpty(d.ShortName) ? row.Name : d.ShortName;
            InspSub.Text = $"{d.MatchName}  ·  {d.Parameters.Count} parameter{(d.Parameters.Count == 1 ? "" : "s")}" +
                           $"  ·  {d.AnimatedCount} animated";
            InspEmpty.Visibility = Visibility.Collapsed;
            InspUnavailable.Visibility = Visibility.Collapsed;

            ParamList.ItemsSource = d.Parameters.Select(p => new ParamRowVm(p)).ToList();
            ParamEmpty.Visibility = d.Parameters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var animParams = d.Parameters.Where(p => p.IsAnimated).ToList();
            SetComboSource(animParams.Select(p => p.Name).ToList());
            _animParamIndex = animParams.Count > 0 ? 0 : -1;
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
            _syncingCombo = true;
            KfParamSelect.SelectedIndex = idx;
            GraphParamSelect.SelectedIndex = idx;
            _syncingCombo = false;
            _animParamIndex = idx;
            BuildKeyframes();
            DrawGraph();
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
                ? "value over time · 30 fps grid"
                : "speed = |Δvalue / Δt| · 30 fps grid";
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
        }

        private void BuildKeyframes()
        {
            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0)
            {
                KfList.ItemsSource = null;
                KfEmpty.Visibility = Visibility.Visible;
                return;
            }
            KfEmpty.Visibility = Visibility.Collapsed;
            var kfs = p.Keyframes;
            KfList.ItemsSource = kfs.Select((k, i) =>
                new KfRowVm(i + 1, k, i > 0 ? kfs[i - 1] : null)).ToList();
        }

        // ---------- AE-style graph ----------
        private const double Fps = 30.0; // frame numbers + grid assume 30 fps

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
        /// = bezier, left triangle = hold. Speed mode: |Δvalue/Δt| sampled
        /// from the same curve, drawn as AE's filled speed area.
        /// Both modes share a frame-based time grid (30 fps) with second
        /// labels and a hover probe (dashed cursor + floating readout).
        /// Pure WPF shapes — Win7-safe, no bitmap effects.
        /// </summary>
        private void DrawGraph()
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
                GraphHint.Visibility = Visibility.Visible;
                return;
            }
            GraphHint.Visibility = Visibility.Collapsed;

            double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
            if (w < 60 || h < 60) return; // not laid out yet — SizeChanged redraws

            var segs = PresetCurve.BuildSegments(p.Keyframes);
            if (segs.Count == 0)
            {
                GraphHint.Visibility = Visibility.Visible;
                return;
            }

            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");
            var accent = (Brush)FindResource("B.Primary");
            var surface = (Brush)FindResource("B.Surface");
            var outline = (Brush)FindResource("B.Outline");

            const double L = 46, R = 12, T = 14, Bot = 24;
            double t0 = segs[0].T0, t1 = segs[segs.Count - 1].T1;
            if (t1 - t0 < 1e-6) t1 = t0 + 0.5; // single-instant stream still gets an axis
            Func<double, double> xOf = t => L + (t - t0) / (t1 - t0) * (w - L - R);

            DrawTimeGrid(t0, t1, xOf, w, h, T, Bot, L, R, gridBrush, labelBrush);

            var plot = new PlotState
            {
                Segs = segs, T0 = t0, T1 = t1,
                W = w, H = h, L = L, R = R, T = T, Bot = Bot, Mode = _graphMode
            };

            if (_graphMode == 0) DrawValueCurve(plot, accent, surface);
            else DrawSpeedCurve(plot, accent, surface);

            _plot = plot;
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
                AddTimeLabel(x + 4, h - bot + 6, t.ToString("0.##") + "s", labelBrush);
            }
        }

        private void DrawValueCurve(PlotState plot, Brush accent, Brush surface)
        {
            var segs = plot.Segs;
            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");

            double vMin = segs.Min(s => Math.Min(s.V0, s.V1));
            double vMax = segs.Max(s => Math.Max(s.V0, s.V1));
            // include handle extremes so bezier overshoot stays inside the plot
            vMin = Math.Min(vMin, segs.Min(s => Math.Min(s.C1V, s.C2V)));
            vMax = Math.Max(vMax, segs.Max(s => Math.Max(s.C1V, s.C2V)));
            if (vMax - vMin < 1e-9) { vMin -= 1; vMax += 1; }
            else { double pad = (vMax - vMin) * 0.12; vMin -= pad; vMax += pad; }
            plot.VMin = vMin; plot.VMax = vMax;

            Func<double, double> yOf = v => plot.T + (vMax - v) / (vMax - vMin) * (plot.H - plot.T - plot.Bot);

            // horizontal value grid at round steps
            double vStep = NiceStep((vMax - vMin) / 3.2);
            for (double v = Math.Ceiling(vMin / vStep) * vStep; v <= vMax + 1e-9; v += vStep)
            {
                double y = Math.Round(yOf(v)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = plot.L, X2 = plot.W - plot.R, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                var lbl = new TextBlock { Text = v.ToString("0.##"), FontSize = 10, Foreground = labelBrush };
                Canvas.SetLeft(lbl, 2);
                Canvas.SetTop(lbl, y - 6);
                GraphCanvas.Children.Add(lbl);
            }

            // the curve itself
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(Math.Round(XOf(plot, segs[0].T0)), Math.Round(yOf(segs[0].V0))), false, false);
                foreach (var s in segs)
                {
                    var end = new Point(Math.Round(XOf(plot, s.T1)), Math.Round(yOf(s.V1)));
                    if (s.Mode == PresetCurve.InterpHold)
                    {
                        ctx.LineTo(new Point(end.X, Math.Round(yOf(s.V0))), true, false);
                        ctx.LineTo(end, true, false);
                    }
                    else if (s.Mode == PresetCurve.InterpLinear)
                    {
                        ctx.LineTo(end, true, false);
                    }
                    else
                    {
                        var c1 = new Point(Math.Round(XOf(plot, s.C1T)), Math.Round(yOf(s.C1V)));
                        var c2 = new Point(Math.Round(XOf(plot, s.C2T)), Math.Round(yOf(s.C2V)));
                        ctx.BezierTo(c1, c2, end, true, false);
                    }
                }
            }
            geo.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = geo,
                Stroke = accent,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            });

            // keyframe markers, shaped by interpolation like AE's icons
            var stream = CurrentAnimParam();
            if (stream == null) return;
            for (int i = 0; i < stream.Keyframes.Count; i++)
            {
                var k = stream.Keyframes[i];
                int shape = i + 1 < stream.Keyframes.Count ? k.InterpOut : k.InterpIn;
                double x = XOf(plot, PresetCurve.Seconds(k.Time));
                double y = yOf(k.Value);
                string tip = $"#{i + 1}  t {PresetCurve.Seconds(k.Time).ToString("0.##")} s" +
                             $"  ·  value {k.Value.ToString("0.###")}  ·  {k.InterpLabel}";
                var marker = MakeMarker(shape, x, y, accent, surface, tip);
                GraphCanvas.Children.Add(marker);
            }
        }

        private void DrawSpeedCurve(PlotState plot, Brush accent, Brush surface)
        {
            var segs = plot.Segs;
            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");

            // sample the value curve, difference it into speed
            const int N = 260;
            double[] ts, vs;
            PresetCurve.SampleValues(segs, N, out ts, out vs);
            var sp = new double[N];
            for (int i = 1; i < N; i++)
                sp[i] = Math.Abs(vs[i] - vs[i - 1]) / Math.Max(ts[i] - ts[i - 1], 1e-9);
            sp[0] = sp[1];

            double spMax = sp.Max();
            if (spMax < 1e-9) spMax = 1.0;
            spMax *= 1.1; // headroom so the peak never kisses the top edge
            plot.SpeedMax = spMax;

            Func<double, double> yOf = v => plot.T + (spMax - v) / spMax * (plot.H - plot.T - plot.Bot);
            double baseY = Math.Round(plot.H - plot.Bot) + 0.5;

            // horizontal speed grid
            double vStep = NiceStep(spMax / 3.0);
            for (double v = vStep; v <= spMax + 1e-9; v += vStep)
            {
                double y = Math.Round(yOf(v)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = plot.L, X2 = plot.W - plot.R, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                var lbl = new TextBlock { Text = v.ToString("0.##") + "/s", FontSize = 10, Foreground = labelBrush };
                Canvas.SetLeft(lbl, 2);
                Canvas.SetTop(lbl, y - 6);
                GraphCanvas.Children.Add(lbl);
            }

            // AE-style: the speed graph reads as a filled area over the axis
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(Math.Round(XOf(plot, ts[0])), baseY), true, false);
                for (int i = 0; i < N; i++)
                    ctx.LineTo(new Point(Math.Round(XOf(plot, ts[i])), Math.Round(yOf(sp[i]))), true, false);
                ctx.LineTo(new Point(Math.Round(XOf(plot, ts[N - 1])), baseY), true, false);
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
                ctx.BeginFigure(new Point(Math.Round(XOf(plot, ts[0])), Math.Round(yOf(sp[0]))), false, false);
                for (int i = 1; i < N; i++)
                    ctx.LineTo(new Point(Math.Round(XOf(plot, ts[i])), Math.Round(yOf(sp[i]))), true, false);
            }
            line.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = line,
                Stroke = accent,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round
            });

            // keyframe markers on the speed curve: numeric slope around each kf
            var stream = CurrentAnimParam();
            if (stream == null) return;
            double eps = Math.Max((plot.T1 - plot.T0) / 2000.0, 1e-6);
            foreach (var k in stream.Keyframes)
            {
                double t = PresetCurve.Seconds(k.Time);
                double before = PresetCurve.ValueAt(segs, t - eps);
                double after = PresetCurve.ValueAt(segs, t + eps);
                if (double.IsNaN(before) || double.IsNaN(after)) continue;
                double s = Math.Abs(after - before) / (2 * eps);
                if (s > spMax) s = spMax;
                string tip = $"t {t.ToString("0.##")} s  ·  speed {s.ToString("0.###")} /s";
                GraphCanvas.Children.Add(MakeMarker(PresetCurve.InterpBezier,
                    XOf(plot, t), yOf(s), accent, surface, tip));
            }
        }

        /// <summary>Linear keyframes → square, bezier → diamond, hold → left triangle.</summary>
        private UIElement MakeMarker(int interp, double x, double y, Brush fill, Brush stroke, string tip)
        {
            UIElement el;
            if (interp == PresetCurve.InterpLinear)
            {
                el = new Rectangle
                {
                    Width = 7.5,
                    Height = 7.5,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.2,
                    ToolTip = tip
                };
                Canvas.SetLeft(el, x - 3.75);
                Canvas.SetTop(el, y - 3.75);
            }
            else if (interp == PresetCurve.InterpHold)
            {
                el = new Polygon
                {
                    Points = new PointCollection { new Point(11, 0), new Point(11, 10), new Point(1, 5) },
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.2,
                    StrokeLineJoin = PenLineJoin.Round,
                    ToolTip = tip
                };
                Canvas.SetLeft(el, x - 6);
                Canvas.SetTop(el, y - 5);
            }
            else
            {
                el = new Polygon
                {
                    Points = new PointCollection { new Point(5.5, 0), new Point(11, 5.5), new Point(5.5, 11), new Point(0, 5.5) },
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.2,
                    StrokeLineJoin = PenLineJoin.Round,
                    ToolTip = tip
                };
                Canvas.SetLeft(el, x - 5.5);
                Canvas.SetTop(el, y - 5.5);
            }
            return el;
        }

        /// <summary>
        /// Hover probe: dashed vertical cursor + floating readout with the
        /// exact time and the interpolated value (value mode) or numeric
        /// speed (speed mode) under the mouse.
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

            EnsureCursor();
            double x = Math.Round(pt.X) + 0.5;
            _cursorLine.Visibility = Visibility.Visible; // re-shown after ClearCursor
            _cursorLine.X1 = x;
            _cursorLine.X2 = x;
            _cursorLine.Y1 = _plot.T;
            _cursorLine.Y2 = _plot.H - _plot.Bot;

            string readout;
            double plotH = _plot.H - _plot.T - _plot.Bot;
            if (_plot.Mode == 0)
            {
                double v = PresetCurve.ValueAt(_plot.Segs, t);
                double y = _plot.T + (_plot.VMax - v) / Math.Max(_plot.VMax - _plot.VMin, 1e-9) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = $"{t.ToString("0.##")} s · value {v.ToString("0.###")}";
            }
            else
            {
                double eps = Math.Max((_plot.T1 - _plot.T0) / 2000.0, 1e-6);
                double s = Math.Abs(PresetCurve.ValueAt(_plot.Segs, t + eps) -
                                    PresetCurve.ValueAt(_plot.Segs, t - eps)) / (2 * eps);
                double y = _plot.T + (1 - Math.Min(s / Math.Max(_plot.SpeedMax, 1e-9), 1)) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = $"{t.ToString("0.##")} s · speed {s.ToString("0.###")} /s";
            }

            GraphReadoutText.Text = readout;
            GraphReadout.Visibility = Visibility.Visible;
        }

        private void GraphCanvas_MouseLeave(object sender, MouseEventArgs e) => ClearCursor();

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
}
