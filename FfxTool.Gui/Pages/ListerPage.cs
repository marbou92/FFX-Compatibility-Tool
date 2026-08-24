using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Effect Lister: read-only compatibility view of a preset's effects â€”
    /// filter/sort toolbar, status-colored rows, friendly empty state.
    /// </summary>
    public partial class ListerPage : UserControl, ISection
    {
        public class EffectRowVm
        {
            public string Name { get; set; }
            public string VendorLabel { get; set; }
            public string Status { get; set; }
            public Brush RowBrush { get; set; }
        }

        private readonly PluginProfile _profile;
        private List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();
        private readonly ObservableCollection<EffectRowVm> _rows = new ObservableCollection<EffectRowVm>();
        private int _filterMode; // 0 all, 1 missing only, 2 compatible only
        private bool _sortDesc;

        public ListerPage(PluginProfile profile)
        {
            InitializeComponent();
            _profile = profile;
            EffectList.ItemsSource = _rows;
            StatusBarVersion.Text = $"FFX Compatibility Tool v{Version}";
            UpdateRecentCard();
        }

        private static string Version =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

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
                RecentCard.ToolTip = $"Open {recent.FileName}";
            }
            else
            {
                RecentText.Text = "View last 5 analyzed presets";
                RecentCard.Opacity = 0.55;
                RecentCard.Cursor = Cursors.Arrow;
                RecentCard.ToolTip = "No recent files yet";
            }
        }

        // ---------- loading ----------
        public void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" };
            if (dlg.ShowDialog() == true) LoadFile(dlg.FileName);
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

        private void Recent_Click(object sender, MouseButtonEventArgs e)
        {
            var recent = HistoryStore.Load().FirstOrDefault();
            if (recent != null) LoadFile(recent.Path);
        }

        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length > 0 && files[0].EndsWith(".ffx", StringComparison.OrdinalIgnoreCase))
                e.Effects = DragDropEffects.Copy;
        }

        private void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                LoadFile(files[0]);
        }

        private void LoadFile(string path)
        {
            try
            {
                FileChipText.Text = System.IO.Path.GetFileName(path);
                _currentEffects = Pipeline.ListEffects(File.ReadAllBytes(path));
                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"Failed to read '{System.IO.Path.GetFileName(path)}':\n{ex.Message}",
                    "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                FileChipText.Text = "No file loaded";
                _currentEffects = new List<Pipeline.EffectInfo>();
                Refresh();
            }
        }

        // ---------- filter / sort / refresh ----------
        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = (_filterMode + 1) % 3;
            // active filter gets the primary tint
            var primary = (Brush)FindResource("B.Primary");
            FilterIcon.Foreground = _filterMode != 0 ? primary : (Brush)FindResource("B.OnSurfaceVariant");
            Refresh();
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _sortDesc = !_sortDesc;
            var primary = (Brush)FindResource("B.Primary");
            SortIcon.Foreground = _sortDesc ? primary : (Brush)FindResource("B.OnSurfaceVariant");
            Refresh();
        }

        public void Refresh()
        {
            var table = PluginLookup.LoadTable();
            var real = _currentEffects.Where(e => !e.IsSentinel);

            IEnumerable<Pipeline.EffectInfo> ordered = _sortDesc
                ? real.OrderByDescending(e => e.MatchName, StringComparer.OrdinalIgnoreCase)
                : real.OrderBy(e => e.MatchName, StringComparer.OrdinalIgnoreCase);

            if (_filterMode == 1)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) == false);
            else if (_filterMode == 2)
                ordered = ordered.Where(e => _profile.Owns(PluginLookup.Resolve(e.MatchName, table).Vendor) != false);

            _rows.Clear();
            foreach (var eff in ordered)
            {
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
                    VendorLabel = $"{match.Vendor ?? "?"} â€” {match.Suite ?? "?"}",
                    Status = status,
                    RowBrush = row
                });
            }

            bool hasContent = _currentEffects.Any(e => !e.IsSentinel);
            EmptyState.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
            ListHost.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;

            string filterText = _filterMode == 0 ? "All" : _filterMode == 1 ? "Missing only" : "Compatible only";
            string sortText = _sortDesc ? "Zâ†’A" : "Aâ†’Z";
            StatusBarMode.Text = hasContent ? $"{filterText} Â· {sortText}" : "Ready";
        }
    }
}
