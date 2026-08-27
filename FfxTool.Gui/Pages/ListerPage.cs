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
    /// its decoded parameters, the raw keyframe stream of any animated
    /// parameter, and a value-vs-time graph drawn from the same data
    /// (PresetsInspector reads, never writes — the pipeline's keyframes are
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

        public class ParamRowVm
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public string Chip { get; set; }
            public Visibility ChipVisible { get; set; }
            public string MatchName { get; set; }

            public ParamRowVm(PresetParameter p)
            {
                Name = p.Name;
                MatchName = p.MatchName ?? p.Name;
                string range = p.Min.HasValue && p.Max.HasValue
                    ? $" · range {Fmt(p.Min)} … {Fmt(p.Max)}" : "";
                Detail = (p.IsAnimated ? "animated" : "static") + range;
                if (p.IsAnimated)
                {
                    Chip = $"{p.Keyframes.Count} key{(p.Keyframes.Count == 1 ? "" : "s")}";
                    ChipVisible = Visibility.Visible;
                }
                else if (p.StaticValue.HasValue)
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

            static string Fmt(double? v) => v?.ToString("0.###") ?? "—";
        }

        public class KfRowVm
        {
            public string Index { get; set; }
            public string Time { get; set; }
            public string Value { get; set; }
            public string Interp { get; set; }

            public KfRowVm(int index, PresetKeyframe kf)
            {
                Index = index.ToString();
                // raw preset ticks — AE keeps these in the comp's own
                // timebase, so they are shown as-is, never faked as seconds
                Time = kf.Time.ToString();
                Value = kf.Value.ToString("0.###");
                Interp = kf.InterpLabel;
            }
        }

        private readonly PluginProfile _profile;
        private List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();
        private List<PresetEffectDetails> _details = new List<PresetEffectDetails>();
        private readonly ObservableCollection<EffectRowVm> _rows = new ObservableCollection<EffectRowVm>();
        private int _filterMode; // 0 all, 1 missing only, 2 compatible only
        private bool _sortDesc;

        // inspector state: selected effect (by stable effect index), selected
        // animated parameter (for the keyframes/graph tabs), active tab
        private int _inspEffectIndex = -1;
        private int _animParamIndex = -1;
        private int _tab;
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
            KfList.ItemsSource = p.Keyframes.Select((k, i) => new KfRowVm(i + 1, k)).ToList();
        }

        /// <summary>
        /// Value-vs-time curve for the selected animated parameter, drawn
        /// straight from the decoded keyframe stream: one dot per keyframe,
        /// straight lines for linear segments, a step for Hold, and cubic
        /// segments whose handles sit at each side's influence fraction for
        /// Bezier easing (the fixture's Easy-Ease streams render as the
        /// familiar S-curve). X axis = preset ticks (AE comp timebase, shown
        /// as-is), Y axis = value. Pure WPF shapes — Win7-safe, no effects.
        /// </summary>
        private void DrawGraph()
        {
            if (GraphCanvas == null) return;
            GraphCanvas.Children.Clear();
            var p = CurrentAnimParam();
            if (p == null || p.Keyframes.Count == 0)
            {
                GraphHint.Visibility = Visibility.Visible;
                return;
            }
            GraphHint.Visibility = Visibility.Collapsed;

            double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
            if (w < 60 || h < 60) return; // not laid out yet — SizeChanged redraws

            var kfs = p.Keyframes;
            const double L = 48, R = 14, T = 14, B = 28;
            double tMin = kfs[0].Time, tMax = kfs[kfs.Count - 1].Time;
            if (tMax <= tMin) tMax = tMin + 1;
            double vMin = kfs.Min(k => k.Value), vMax = kfs.Max(k => k.Value);
            if (vMax - vMin < 1e-9) { vMin -= 1; vMax += 1; }
            else { double pad = (vMax - vMin) * 0.12; vMin -= pad; vMax += pad; }

            Func<double, double> xOf = t => L + (t - tMin) / (tMax - tMin) * (w - L - R);
            Func<double, double> yOf = v => T + (vMax - v) / (vMax - vMin) * (h - T - B);

            var gridBrush = (Brush)FindResource("B.OutlineVariant");
            var labelBrush = (Brush)FindResource("B.OnSurfaceVariant");
            var primary = (Brush)FindResource("B.Primary");
            var surface = (Brush)FindResource("B.Surface");

            for (int i = 0; i <= 3; i++)
            {
                double v = vMin + (vMax - vMin) * i / 3.0;
                double y = Math.Round(yOf(v)) + 0.5;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = L, X2 = w - R, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
                var lbl = new TextBlock { Text = v.ToString("0.##"), FontSize = 10, Foreground = labelBrush };
                Canvas.SetLeft(lbl, 2);
                Canvas.SetTop(lbl, y - 6);
                GraphCanvas.Children.Add(lbl);
            }

            AddTimeLabel(xOf(tMin), h - B + 8, tMin.ToString());
            AddTimeLabel((xOf(tMin) + xOf(tMax)) / 2 - 12, h - B + 8, ((tMin + tMax) / 2).ToString());
            AddTimeLabel(xOf(tMax) - 24, h - B + 8, tMax.ToString());

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(Math.Round(xOf(kfs[0].Time)), Math.Round(yOf(kfs[0].Value))), false, false);
                for (int i = 0; i + 1 < kfs.Count; i++)
                {
                    var a = kfs[i];
                    var b = kfs[i + 1];
                    double dt = Math.Max(1, b.Time - a.Time);
                    var end = new Point(Math.Round(xOf(b.Time)), Math.Round(yOf(b.Value)));
                    if (a.InterpOut == 3) // hold: value steps at the next keyframe
                    {
                        ctx.LineTo(new Point(end.X, Math.Round(yOf(a.Value))), true, false);
                        ctx.LineTo(end, true, false);
                    }
                    else if (a.InterpOut == 1) // linear
                    {
                        ctx.LineTo(end, true, false);
                    }
                    else // bezier: handles at influence fractions of the segment
                    {
                        double oi = Math.Max(0, Math.Min(1, a.OutInfluence));
                        double ii = Math.Max(0, Math.Min(1, b.InInfluence));
                        var c1 = new Point(Math.Round(xOf(a.Time + oi * dt)), Math.Round(yOf(a.Value)));
                        var c2 = new Point(Math.Round(xOf(b.Time - ii * dt)), Math.Round(yOf(b.Value)));
                        ctx.BezierTo(c1, c2, end, true, false);
                    }
                }
            }
            geo.Freeze();
            GraphCanvas.Children.Add(new Path
            {
                Data = geo,
                Stroke = primary,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            });

            for (int i = 0; i < kfs.Count; i++)
            {
                var k = kfs[i];
                GraphCanvas.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = primary,
                    Stroke = surface,
                    StrokeThickness = 1.5,
                    ToolTip = $"#{i + 1}    t = {k.Time}    value = {k.Value.ToString("0.###")}"
                });
                var dot = (Ellipse)GraphCanvas.Children[GraphCanvas.Children.Count - 1];
                Canvas.SetLeft(dot, xOf(k.Time) - 4);
                Canvas.SetTop(dot, yOf(k.Value) - 4);
            }
        }

        private void AddTimeLabel(double x, double y, string text)
        {
            var lbl = new TextBlock { Text = text, FontSize = 10, Foreground = (Brush)FindResource("B.OnSurfaceVariant") };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y);
            GraphCanvas.Children.Add(lbl);
        }
    }
}
