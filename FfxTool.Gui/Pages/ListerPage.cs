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
        /// One row of the Parameters tab, shaped like an AE property line:
        /// stopwatch state, name + stream summary, the value slot, and the
        /// keyframe navigator targets. Brushes stay in the template (via
        /// DynamicResource triggers keyed on IsAnimated) so theme swaps keep
        /// working — the VM only carries flags, text and counts.
        /// </summary>
        public class ParamRowVm
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public string MatchName { get; set; }
            public Visibility AccentVisible { get; set; }
            public bool IsAnimated { get; set; }
            public double StopwatchOpacity { get; set; }
            public string StopwatchTip { get; set; }
            public string ValueText { get; set; }
            public Visibility ValueVisible { get; set; }
            public string ValueTip { get; set; }
            public Visibility NavVisible { get; set; }
            public string KeyCountTip { get; set; }
            public Cursor RowCursor { get; set; }
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
                    AccentVisible = Visibility.Visible;
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
                    AccentVisible = Visibility.Collapsed;
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
                    Sub = $"frame {frame} · +{frame - prevFrame}f";
                }
                Value = kf.Value.ToString("0.###");
                Interp = kf.InterpLabel;
                Tip = $"t = {sec.ToString("0.###")} s · raw time {kf.Time} ticks" +
                      $" · in influence {kf.InInfluence.ToString("0.##")}" +
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
                _selKf = -1;
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

        /// <summary>Position of p among the effect's animated parameters.</summary>
        private int AnimatedIndexOf(PresetParameter p)
        {
            var d = CurrentDetails();
            if (d == null || p == null) return -1;
            var anim = d.Parameters.Where(x => x.IsAnimated).ToList();
            return anim.IndexOf(p);
        }

        // ---------- AE parameter rows: click + keyframe navigator ----------
        private void ParamRow_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ParamRowVm vm && vm.IsAnimated)
                SelectAnimatedParam(AnimatedIndexOf(vm.ParamRef));
        }

        private void KfPrev_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ParamRowVm vm && vm.IsAnimated)
            {
                SelectAnimatedParam(AnimatedIndexOf(vm.ParamRef));
                SelectKeyframe(_selKf < 0 ? 0 : _selKf - 1);
            }
        }

        private void KfNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ParamRowVm vm && vm.IsAnimated)
            {
                SelectAnimatedParam(AnimatedIndexOf(vm.ParamRef));
                SelectKeyframe(_selKf < 0 ? 0 : _selKf + 1);
            }
        }

        /// <summary>The diamond button: jump to the Keyframes tab for that stream.</summary>
        private void KfShow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ParamRowVm vm && vm.IsAnimated)
            {
                SelectAnimatedParam(AnimatedIndexOf(vm.ParamRef));
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
            if (_tab == 2) DrawGraph();
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
                ? "value over time · click a keyframe to inspect it"
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
                KfDetail.Visibility = Visibility.Collapsed;
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
                KfDetail.Text = $"#{_selKf + 1} easing — in:  slope {k.InSlope.ToString("0.###")} · influence {k.InInfluence.ToString("0.##")}" +
                                $"   ·   out:  slope {k.OutSlope.ToString("0.###")} · influence {k.OutInfluence.ToString("0.##")}";
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
        }

        // ---------- AE-style graph ----------
        private const double Fps = 30.0; // frame numbers + grid assume 30 fps

        /// <summary>AE-style timecode h:mm:ss:ff (hours only when needed).</summary>
        private static string Timecode(double sec)
        {
            int total = (int)Math.Round(sec * Fps);
            int f = total % 30, s = (total / 30) % 60, m = (total / 1800) % 60, h = total / 108000;
            return h > 0
                ? $"{h}:{m.ToString("00")}:{s.ToString("00")}:{f.ToString("00")}"
                : $"0:{m.ToString("00")}:{s.ToString("00")}:{f.ToString("00")}";
        }

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
        /// from the same curve, drawn as AE's filled speed area. A selected
        /// keyframe gets AE's accent ring plus its tangent handles, in both
        /// tabs (graph markers, keyframe rows and the navigator stay in sync).
        /// Curve geometry is deliberately NOT pixel-snapped: rounding every
        /// coordinate quantized the beziers into visible kinks.
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

            var plot = new PlotState
            {
                Segs = segs, T0 = t0, T1 = t1,
                W = w, H = h, L = L, R = R, T = T, Bot = Bot, Mode = _graphMode
            };

            double plotH = h - T - Bot;
            if (_graphMode == 0)
            {
                Func<double, double> yOf = v => T + (plot.VMax - v) / Math.Max(plot.VMax - plot.VMin, 1e-9) * plotH;
                DrawValueCurve(plot, accent, surface, yOf);
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

        private void DrawValueCurve(PlotState plot, Brush accent, Brush surface, Func<double, double> yOf)
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

            // horizontal value grid at round steps; labels right-aligned in
            // the left gutter (AE's value ruler) and skipped when they would
            // collide — 13px is the minimum pitch for the 10px label font
            double vStep = NiceStep((vMax - vMin) / 3.2);
            double lastLabelY = -1000;
            for (double v = Math.Ceiling(vMin / vStep) * vStep; v <= vMax + 1e-9; v += vStep)
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
                        Text = v.ToString("0.##"),
                        FontSize = 10,
                        Foreground = labelBrush,
                        Width = plot.L - 10,
                        TextAlignment = TextAlignment.Right
                    };
                    Canvas.SetLeft(lbl, 2);
                    Canvas.SetTop(lbl, y - 6);
                    GraphCanvas.Children.Add(lbl);
                    lastLabelY = y;
                }
            }

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
            var stream = CurrentAnimParam();
            if (stream == null) return;
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
                        Text = v.ToString("0.##") + "/s",
                        FontSize = 10,
                        Foreground = labelBrush,
                        Width = plot.L - 10,
                        TextAlignment = TextAlignment.Right
                    };
                    Canvas.SetLeft(lbl, 2);
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

            // keyframe markers on the speed curve: numeric slope around each
            // kf, clickable like their value-graph twins
            var stream = CurrentAnimParam();
            if (stream == null) return;
            for (int i = 0; i < stream.Keyframes.Count; i++)
            {
                var k = stream.Keyframes[i];
                double t = PresetCurve.Seconds(k.Time);
                double s = SpeedAt(segs, t);
                if (double.IsNaN(s)) continue;
                string tip = $"#{i + 1}  {Timecode(t)}  ·  speed {s.ToString("0.###")} /s  ·  click to select";
                int idx = i;
                GraphCanvas.Children.Add(MakeMarker(PresetCurve.InterpBezier,
                    XOf(plot, t), yOf(Math.Min(s, spMax)), accent, surface, tip, idx));
            }
        }

        /// <summary>Numeric |Δvalue/Δt| at t via a tiny centered difference.</summary>
        private static double SpeedAt(List<PresetCurve.Segment> segs, double t)
        {
            if (segs == null || segs.Count == 0) return double.NaN;
            double span = Math.Max((segs[segs.Count - 1].T1 - segs[0].T0) / 2000.0, 1e-6);
            double before = PresetCurve.ValueAt(segs, t - span);
            double after = PresetCurve.ValueAt(segs, t + span);
            if (double.IsNaN(before) || double.IsNaN(after)) return double.NaN;
            return Math.Abs(after - before) / (2 * span);
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

            double y = plot.Mode == 0 ? yOf(k.Value) : yOf(Math.Min(SpeedAt(plot.Segs, t), plot.SpeedMax));
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
        /// Linear keyframes → square, bezier → diamond, hold → left triangle.
        /// kfIndex &gt;= 0 makes the marker clickable for selection.
        /// </summary>
        private UIElement MakeMarker(int interp, double x, double y, Brush fill, Brush stroke, string tip, int kfIndex)
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

            if (kfIndex >= 0)
            {
                var fe = (FrameworkElement)el;
                fe.Cursor = Cursors.Hand;
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
            double plotH = _plot.H - _plot.T - _plot.Bot;
            if (_plot.Mode == 0)
            {
                double v = PresetCurve.ValueAt(_plot.Segs, t);
                double y = _plot.T + (_plot.VMax - v) / Math.Max(_plot.VMax - _plot.VMin, 1e-9) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = $"{t.ToString("0.##")} s · f{frame} · value {v.ToString("0.###")}";
            }
            else
            {
                double s = SpeedAt(_plot.Segs, t);
                double y = _plot.T + (1 - Math.Min(Math.Max(s, 0) / Math.Max(_plot.SpeedMax, 1e-9), 1)) * plotH;
                Canvas.SetLeft(_cursorDot, x - 3.5);
                Canvas.SetTop(_cursorDot, Math.Round(y) - 3.5);
                _cursorDot.Visibility = Visibility.Visible;
                readout = double.IsNaN(s)
                    ? $"{t.ToString("0.##")} s · f{frame}"
                    : $"{t.ToString("0.##")} s · f{frame} · speed {s.ToString("0.###")} /s";
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
