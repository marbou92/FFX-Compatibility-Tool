using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings hub with sub-settings: Appearance, Storage (delete the
    /// plugin-scan cache and the recent-files history), Plugin Profiles
    /// (the existing ProfilePage embedded verbatim — its logic is
    /// untouched) and About (with real project links). Theme changes apply
    /// instantly via ThemeService.
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private const string RepoUrl = "https://github.com/marbou92/FFX-Compatibility-Tool";

        private static readonly (Md3Palette palette, Color swatch)[] Palettes =
        {
            (Md3Palette.Teal,   Color.FromRgb(0x00, 0x6B, 0x5F)),
            (Md3Palette.Blue,   Color.FromRgb(0x00, 0x5B, 0xBF)),
            (Md3Palette.Purple, Color.FromRgb(0x7A, 0x4F, 0xE0)),
            (Md3Palette.Orange, Color.FromRgb(0xB4, 0x54, 0x0A)),
        };

        private bool _syncing;

        public SettingsPage(ProfilePage profilePage)
        {
            InitializeComponent();
            VersionText.Text = "Version " + AppInfo.Version;

            ProfileHost.Content = profilePage ?? throw new ArgumentNullException(nameof(profilePage));

            DarkSwitch.Checked += (s, e) => ApplyMode(Md3Mode.Dark);
            DarkSwitch.Unchecked += (s, e) => ApplyMode(Md3Mode.Light);

            BuildPaletteSwatches();
            SyncFromTheme();

            // keep the switch in sync when the mode changes elsewhere
            // (Restore Defaults) — previously a stale-switch desync bug
            ThemeService.Changed += SyncFromTheme;
        }

        private void SubNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppearanceView == null) return; // XAML not loaded yet
            AppearanceView.Visibility = SubNav.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            StorageView.Visibility = SubNav.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            ProfileHost.Visibility = SubNav.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            AboutView.Visibility = SubNav.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (SubNav.SelectedIndex == 1) RefreshStorageInfo();
        }

        // ---------- storage ----------

        /// <summary>Human size for the storage readouts.</summary>
        private static string FmtBytes(long bytes)
        {
            if (bytes >= 1024 * 1024) return ((double)bytes / (1024 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024) return ((double)bytes / 1024).ToString("0.#") + " KB";
            return bytes + " B";
        }

        /// <summary>Live readouts for the two delete rows: the plugin scan
        /// catalog's size on disk (or "not built yet") and the history's
        /// entry count. Runs when the Storage tab opens and after each
        /// delete, so the numbers can never lie about what's on disk.</summary>
        private void RefreshStorageInfo()
        {
            long cacheBytes = 0;
            bool cacheExists = false;
            try
            {
                var cat = new System.IO.FileInfo(PluginCatalog.CatalogPath);
                cacheExists = cat.Exists;
                if (cat.Exists) cacheBytes = cat.Length;
            }
            catch { /* unreadable profile folder — fall through to the not-built wording */ }
            CacheInfo.Text = cacheExists
                ? "plugin_catalog.txt · " + FmtBytes(cacheBytes)
                : "plugin_catalog.txt · not built yet";

            long historyBytes = 0;
            try
            {
                var f = new System.IO.FileInfo(HistoryStore.StorePath);
                if (f.Exists) historyBytes = f.Length;
            }
            catch { /* same probe failure — the entry count still shows */ }
            int entries = HistoryStore.Load().Count;
            HistoryInfo.Text = entries > 0
                ? "recent_files.json · " + entries + " of 5 · " + FmtBytes(historyBytes)
                : "recent_files.json · empty";
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            var choice = MessageBox.Show(
                "Delete the plugin scan catalog?\n\nPlugin recognition falls back to the built-in reference tables until the next system scan rebuilds it.",
                "Delete Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;
            bool deleted = false;
            try
            {
                if (System.IO.File.Exists(PluginCatalog.CatalogPath))
                {
                    System.IO.File.Delete(PluginCatalog.CatalogPath);
                    deleted = true;
                }
            }
            catch { /* a locked file must not take Settings down */ }
            // drop the in-memory copy too, so the next lookup re-reads
            // the (now absent) file instead of trusting the old scan
            PluginRecognition.ResetCatalog();
            LogService.Append("storage: plugin scan catalog " +
                              (deleted ? "deleted" : "already absent"));
            RefreshStorageInfo();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var choice = MessageBox.Show(
                "Delete the recently-opened history?\n\nThe list of analyzed presets empties; it fills up again as new presets are analyzed.",
                "Delete History", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;
            bool deleted = HistoryStore.Clear();
            LogService.Append("storage: recent-files history " +
                              (deleted ? "deleted" : "already empty"));
            RefreshStorageInfo();
        }

        private void OpenFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // CatalogPath's getter creates the folder if it never
                // existed, so Explorer always has something to open.
                // Process lives in System.Diagnostics, not System.IO —
                // the round-27 build died on exactly that (CS0234).
                System.Diagnostics.Process.Start("explorer.exe",
                    "\"" + System.IO.Path.GetDirectoryName(PluginCatalog.CatalogPath) + "\"");
            }
            catch { /* Explorer refused — nothing sensible to do */ }
        }

        // ---------- project links ----------
        private void RepoLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl);

        private void IssueLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/issues/new");

        private void Logs_Click(object sender, RoutedEventArgs e) =>
            LogService.RevealLatest();

        // ---------- update check ----------

        /// <summary>
        /// One GET of the repository's one-line VERSION.txt on a worker
        /// thread; the answer lands back on the UI dispatcher. The button
        /// disables itself for the flight so a slow network can't stack
        /// two checks.
        /// </summary>
        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            CheckUpdatesButton.Content = "Checking…";
            UpdateStatusText.Text = "Contacting the version file…";
            UpdateLink.Visibility = Visibility.Collapsed;

            UpdateChecker.CheckAsync(result => Dispatcher.BeginInvoke(new Action(() =>
            {
                CheckUpdatesButton.IsEnabled = true;
                CheckUpdatesButton.Content = "Check for Updates";
                UpdateStatusText.Text = result.Message;
                if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    UpdateLink.Visibility = Visibility.Visible;
                LogService.Append("update check: " + result.Message);
            })));
        }

        private void UpdateLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/releases");

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { /* browser launch refused — nothing sensible to do */ }
        }

        private void SyncFromTheme()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                DarkSwitch.IsChecked = ThemeService.Mode == Md3Mode.Dark;
                RefreshSwatchSelection();
            }
            finally { _syncing = false; }
        }

        private void ApplyMode(Md3Mode mode)
        {
            if (_syncing) return;
            _syncing = true;
            try { ThemeService.Apply(mode, ThemeService.Palette); }
            finally { _syncing = false; }
        }

        private void BuildPaletteSwatches()
        {
            foreach (var (palette, swatch) in Palettes)
            {
                var p = palette; // capture
                // Swatch = circle + selection ring layered on a shared 54px hit
                // Grid. NEVER nest the circle inside a rounded Border: WPF
                // Border clips its child to the rounded interior, and
                // 54 − 2×2.5 border − 2×4 padding = 41px of interior vs a 44px
                // circle sheared the bottom arc clean off — the lop-sided
                // "blob" swatches from the round-2 screenshot review.
                var circle = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(swatch),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var ring = new System.Windows.Shapes.Ellipse
                {
                    Width = 54,
                    Height = 54,
                    StrokeThickness = 2.5,
                    Stroke = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var hit = new Grid
                {
                    Width = 54,
                    Height = 54,
                    Background = Brushes.Transparent, // full-surface click target
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                hit.Children.Add(circle);
                hit.Children.Add(ring);
                var name = new TextBlock
                {
                    Text = p.ToString(),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var stack = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
                stack.Children.Add(hit);
                stack.Children.Add(name);
                var tag = new StackPanel { Tag = (ring, name, p) };
                tag.Children.Add(stack);
                hit.MouseLeftButtonUp += (s, e) =>
                {
                    if (_syncing) return;
                    _syncing = true;
                    try { ThemeService.Apply(ThemeService.Mode, p); }
                    finally { _syncing = false; }
                    RefreshSwatchSelection();
                };
                PaletteRow.Children.Add(tag);
            }
            RefreshSwatchSelection();
        }

        private void RefreshSwatchSelection()
        {
            foreach (StackPanel tag in PaletteRow.Children)
            {
                var (ring, name, p) = ((System.Windows.Shapes.Ellipse, TextBlock, Md3Palette))tag.Tag;
                bool selected = ThemeService.Palette == p;
                ring.Stroke = selected ? (Brush)FindResource("B.Primary") : Brushes.Transparent;
                name.Foreground = selected ? (Brush)FindResource("B.Primary") : (Brush)FindResource("B.OnSurfaceVariant");
            }
        }

        private void Restore_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ThemeService.Apply(Md3Mode.Light, Md3Palette.Teal);
        }
    }
}
