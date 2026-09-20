using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings hub, 0.2.2 second pass. Five sub-pages on a shared M3
    /// system: Appearance (Light/Dark/System segmented control, palette
    /// swatches badged with mini-pills, a live theme preview, a two-step
    /// restore), Storage (disk-usage meter, two-step delete confirms,
    /// file-location rows), Plugin Profiles (embedded verbatim in matching
    /// chrome), Updates (the vivi-music "Update Settings" shape: a status
    /// row with the check folded in, app version + flavour, three behavior
    /// switches, a clear-downloads row and a history group whose Changelog
    /// row drills into a version-pill viewer fed from GitHub's release
    /// list) and About (identity, specs chips, project links — links and
    /// details only). Every boolean is a real Md3Switch; the former
    /// checkmarks on selectors read as mini-pills. Above it all: a search
    /// box that jumps to any row, a sub-nav whose pill slides between
    /// five items, Alt+1..5 shortcuts and a remembered last tab (UiPrefs).
    /// Theme changes apply instantly via ThemeService.
    /// </summary>
    public partial class SettingsPage : UserControl, ISection
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
        private bool _manualChecking;
        private Md3Palette? _lastSelectedPalette;

        // two-step confirm state per Delete button
        private sealed class ConfirmState
        {
            public bool Armed;
            public object Original;
            public DispatcherTimer Timer;
        }

        // search index
        private sealed class SearchEntry
        {
            public int PageIndex;
            public string Page;
            public string Title;
            public string Keywords;
            public FrameworkElement Row;
        }
        private readonly List<SearchEntry> _searchIndex = new List<SearchEntry>();

        // changelog drill-in (the Updates page's version-pill viewer)
        private List<ChangelogFeed.ReleaseSummary> _releases;
        private bool _releasesLoading;
        private bool _changelogOpen;

        public SettingsPage(ProfilePage profilePage)
        {
            InitializeComponent();
            VersionText.Text = "Version " + AppInfo.DisplayVersion;

            ProfileHost.Content = profilePage ?? throw new ArgumentNullException(nameof(profilePage));

            // ---- segmented theme control ----
            SegLight.Checked += (s, e) => ApplyMode(Md3Mode.Light);
            SegDark.Checked += (s, e) => ApplyMode(Md3Mode.Dark);
            SegSystem.Checked += (s, e) => ApplyFollow(true);

            // ---- storage: the switch + the whole row toggles it ----
            VerboseCheck.Checked += (s, e) => LogService.Verbose = true;
            VerboseCheck.Unchecked += (s, e) => LogService.Verbose = false;
            VerboseCheck.IsChecked = LogService.Verbose;

            // ---- updates: the three switches bind straight to the
            //      service's persisted settings ----
            AutoCheckSwitch.IsChecked = UpdateService.AutoCheckEnabled;
            AutoCheckSwitch.Checked += (s, e) => UpdateService.SetAutoCheck(true);
            AutoCheckSwitch.Unchecked += (s, e) => UpdateService.SetAutoCheck(false);

            BetaCheckSwitch.IsChecked = UpdateService.BetaCheckEnabled;
            BetaCheckSwitch.Checked += (s, e) => UpdateService.SetBetaCheck(true);
            BetaCheckSwitch.Unchecked += (s, e) => UpdateService.SetBetaCheck(false);

            NotifySwitch.IsChecked = UpdateService.NotifyEnabled;
            NotifySwitch.Checked += (s, e) => UpdateService.SetNotify(true);
            NotifySwitch.Unchecked += (s, e) => UpdateService.SetNotify(false);

            AppVersionValue.Text = "v" + AppInfo.DisplayVersion;
            FlavourValue.Text = UpdateService.IsNightlyBuild ? "Nightly" : "Stable";
            VersionChannelText.Text = UpdateService.IsNightlyBuild ? "NIGHTLY" : "STABLE";

            // checks run on worker threads (the startup auto-check
            // included) — refresh the status row on the dispatcher
            // whatever thread the answer arrives on
            UpdateService.CheckFinished += result => Dispatcher.BeginInvoke(new Action(() =>
            {
                _manualChecking = false;
                RefreshUpdateStatus();
            }));
            // a silent find lights the Updates tab's dot (the toast is
            // MainWindow's, gated by the notify switch)
            UpdateService.UpdateFound += entry => Dispatcher.BeginInvoke(new Action(() =>
            {
                NavUpdatesDot.Visibility = Visibility.Visible;
                RefreshUpdateStatus();
            }));
            UpdateService.UpdateSeen += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                NavUpdatesDot.Visibility = Visibility.Collapsed;
            }));

            BuildSearchIndex();
            BuildPaletteSwatches();
            SyncFromTheme();
            RefreshUpdateStatus();
            RefreshClearDownloadsRow();

            // remember the last-visited sub-tab (suggestion 1): restoring
            // fires SelectionChanged, which applies the views and saves the
            // value back; equal to the default index 0 it simply no-ops and
            // the XAML's own initial visibility carries the day.
            int saved = UiPrefs.SettingsTab;
            if (saved < 0 || saved > 4) saved = 0;
            SubNav.SelectedIndex = saved;

            // the sliding pill needs real layout — align it as soon as the
            // sub-nav is measured, and again whenever its size changes
            SubNav.Loaded += (s, e) => MoveSubNavPill(false);
            SubNav.SizeChanged += (s, e) => MoveSubNavPill(false);

            // keep the segments in sync when the mode changes elsewhere
            // (Restore Defaults, a system flip while following)
            ThemeService.Changed += SyncFromTheme;
        }

        // MainWindow drives the active page through ISection (Ctrl+O and
        // the per-section OnShown).
        public void OpenFile() { }

        public void OnShown()
        {
            // the pill can drift if the page was laid out while hidden —
            // re-align, and freshen the status row's "last checked" line
            MoveSubNavPill(false);
            RefreshUpdateStatus();
            RefreshClearDownloadsRow();
        }

        /// <summary>Keyboard sub-tab switching (Alt+1..5, wired from
        /// MainWindow) — also used by the search results to navigate.</summary>
        public void SelectSubTab(int index)
        {
            if (index < 0 || index > 4) return;
            SubNav.SelectedIndex = index;
        }

        // ---------- sub-nav: switching, sliding pill, cross-fade ----------

        private void SubNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // NavAppearance is IsSelected="True" in the XAML, so this fires
            // while the BAML is still being applied. IsInitialized only
            // turns true after the whole tree exists — the initial views
            // carry their visibility in the XAML itself, so skipping the
            // parse-time firing changes nothing and removes the reliance
            // on declaration order (the BatchPage startup-crash lesson).
            if (!IsInitialized) return;

            int idx = SubNav.SelectedIndex;
            bool appearance = idx == 0, storage = idx == 1, profile = idx == 2,
                 updates = idx == 3, about = idx == 4;
            // any nav switch leaves the changelog drill-in
            CloseChangelog();
            AppearanceView.Visibility = appearance ? Visibility.Visible : Visibility.Collapsed;
            StorageView.Visibility = storage ? Visibility.Visible : Visibility.Collapsed;
            ProfileView.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
            UpdatesView.Visibility = updates ? Visibility.Visible : Visibility.Collapsed;
            AboutView.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

            if (storage) RefreshStorageInfo();
            if (updates)
            {
                RefreshUpdateStatus();
                RefreshClearDownloadsRow();
            }

            AnimateViewIn(appearance ? (UIElement)AppearanceView
                           : storage ? (UIElement)StorageView
                           : profile ? (UIElement)ProfileView
                           : updates ? (UIElement)UpdatesView
                           : (UIElement)AboutView);
            MoveSubNavPill(true);
            UiPrefs.SettingsTab = idx;
        }

        /// <summary>Slides the active-indicator pill under the selected
        /// sub-nav item — the NavRail's spring, horizontally tiny edition.
        /// No-ops quietly before layout has real bounds (Loaded/SizeChanged
        /// re-align it).</summary>
        private void MoveSubNavPill(bool animate)
        {
            if (SubNav.SelectedIndex < 0) return;
            var item = SubNav.ItemContainerGenerator.ContainerFromIndex(SubNav.SelectedIndex) as ListBoxItem;
            if (item == null || item.ActualHeight <= 0 || SubNavHost.ActualWidth <= 0) return;

            var pos = item.TranslatePoint(new Point(0, 0), SubNavHost);
            SubNavPill.Height = item.ActualHeight;
            SubNavPill.Width = Math.Max(140, SubNavHost.ActualWidth - 16);

            if (animate)
            {
                var anim = new DoubleAnimation(pos.Y, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut }
                };
                SubNavPillMove.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            else
            {
                SubNavPillMove.BeginAnimation(TranslateTransform.YProperty, null);
                SubNavPillMove.Y = pos.Y;
            }
        }

        /// <summary>Quiet fade + 9px rise when a sub-page switches — the
        /// same material feel as MainWindow's section animation.</summary>
        private void AnimateViewIn(UIElement view)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            var slide = view.RenderTransform as TranslateTransform;
            if (slide == null)
            {
                slide = new TranslateTransform();
                view.RenderTransform = slide;
            }
            view.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(9, 0, dur) { EasingFunction = ease });
        }

        // ---------- search (suggestion 4) ----------

        private void BuildSearchIndex()
        {
            AddSearch(0, "Appearance", "Theme mode", "light dark system windows follow segmented", null);
            AddSearch(0, "Appearance", "Color palette", "accent teal blue purple orange swatch", null);
            AddSearch(0, "Appearance", "Preview", "live theme example sample", PreviewCard);
            AddSearch(0, "Appearance", "Restore defaults", "reset theme light teal", RestoreButton);
            AddSearch(1, "Storage", "Disk usage", "size meter space cache history logs", MeterRow);
            AddSearch(1, "Storage", "Plugin scan catalog", "cache delete clear recognition plugin_catalog", CacheRow);
            AddSearch(1, "Storage", "Verbose Logging", "debug bug report log steps", VerboseRow);
            AddSearch(1, "Storage", "Recent presets", "history delete recent files recent_files", HistoryRow);
            AddSearch(1, "Storage", "Open storage folder", "explorer appdata folder files data", OpenFolderRow);
            AddSearch(1, "Storage", "Open session logs", "console log files verbose reveal", LogsRow);
            AddSearch(2, "Plugin Profiles", "Vendor switches", "plugins profile vendors after effects linked available", null);
            AddSearch(3, "Updates", "System update", "check update status available up to date latest", UpdateStatusRow);
            AddSearch(3, "Updates", "App version", "copy version number build", AppVersionRow);
            AddSearch(3, "Updates", "Flavour", "stable nightly build kind", FlavourRow);
            AddSearch(3, "Updates", "Automatic update check", "auto silent startup throttle schedule", AutoCheckRow);
            AddSearch(3, "Updates", "Include nightlies", "beta prerelease nightly rolling build", BetaCheckRow);
            AddSearch(3, "Updates", "Notify when an update is found", "toast notification badge bell", NotifyRow);
            AddSearch(3, "Updates", "Clear downloaded updates", "staged download delete disk space", ClearDownloadsRow);
            AddSearch(3, "Updates", "Changelog", "what's new release notes version history", ChangelogRow);
            AddSearch(3, "Updates", "Commits", "main branch history github changes", CommitsRow);
            AddSearch(4, "About", "Version", "copy about identity made by", VersionRow);
            AddSearch(4, "About", "SHA-256", "hash verify checksum copy", ShaRow);
            AddSearch(4, "About", "GitHub repository", "source code repo open", RepoRow);
            AddSearch(4, "About", "Report an issue", "bug feedback problem support", IssueRow);
        }

        private void AddSearch(int pageIndex, string page, string title, string keywords, FrameworkElement row)
        {
            _searchIndex.Add(new SearchEntry
            {
                PageIndex = pageIndex,
                Page = page,
                Title = title,
                Keywords = keywords ?? "",
                Row = row
            });
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = (SearchBox.Text ?? "").Trim();
            SearchPlaceholder.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchResultsList.Children.Clear();
            if (q.Length == 0)
            {
                SearchResults.Visibility = Visibility.Collapsed;
                return;
            }

            var matches = _searchIndex.Where(m =>
                    m.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Page.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Keywords.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(8)
                .ToList();

            if (matches.Count == 0)
            {
                var none = new TextBlock
                {
                    Text = "No matches in settings.",
                    Margin = new Thickness(10, 8, 10, 8),
                    FontSize = 12.5
                };
                none.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                SearchResultsList.Children.Add(none);
            }
            else
            {
                foreach (var m in matches) SearchResultsList.Children.Add(BuildSearchResultRow(m, q));
            }
            SearchResults.Visibility = Visibility.Visible;
        }

        private Border BuildSearchResultRow(SearchEntry entry, string query)
        {
            var title = new TextBlock
            {
                Text = entry.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");
            var page = new TextBlock
            {
                Text = entry.Page,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            page.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(title);
            Grid.SetColumn(page, 1);
            grid.Children.Add(page);

            var border = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            border.MouseEnter += (s, e) => border.SetResourceReference(Border.BackgroundProperty, "B.SCHigh");
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (s, e) =>
            {
                SearchBox.Text = "";
                SelectSubTab(entry.PageIndex);
                if (entry.Row != null)
                {
                    // let the view finish its cross-fade, then flash the row
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        entry.Row.BeginAnimation(UIElement.OpacityProperty,
                            new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(450))
                            {
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            });
                    }));
                }
            };
            return border;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = "";
                SearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SearchResults.Visibility == Visibility.Visible)
            {
                // jump straight to the first match
                var first = SearchResultsList.Children.OfType<Border>().FirstOrDefault();
                first?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent
                });
                e.Handled = true;
            }
        }

        // ---------- storage ----------

        /// <summary>Human size for the storage readouts.</summary>
        private static string FmtBytes(long bytes)
        {
            if (bytes >= 1024 * 1024) return ((double)bytes / (1024 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024) return ((double)bytes / 1024).ToString("0.#") + " KB";
            return bytes + " B";
        }

        /// <summary>Live readouts: the meter's three segments (plugin scan
        /// catalog, recent-files history, session logs) plus the per-row
        /// captions. Runs when the Storage tab opens and after each delete,
        /// so the numbers can never lie about what's on disk.</summary>
        private void RefreshStorageInfo()
        {
            long cacheBytes = 0;
            bool cacheExists = false;
            try
            {
                var cat = new FileInfo(PluginCatalog.CatalogPath);
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
                var f = new FileInfo(HistoryStore.StorePath);
                if (f.Exists) historyBytes = f.Length;
            }
            catch { /* same probe failure — the entry count still shows */ }
            int entries = HistoryStore.Load().Count;
            HistoryInfo.Text = entries > 0
                ? "recent_files.json · " + entries + " of 5 · " + FmtBytes(historyBytes)
                : "recent_files.json · empty";

            long logsBytes = 0;
            try
            {
                var dir = new DirectoryInfo(LogService.LogsDirectory);
                if (dir.Exists)
                    foreach (var log in dir.GetFiles("*.log"))
                        logsBytes += log.Length;
            }
            catch { /* an unreadable logs folder just reads as zero */ }

            long total = cacheBytes + historyBytes + logsBytes;
            if (total <= 0)
            {
                MeterGrid.Visibility = Visibility.Collapsed;
                MeterEmpty.Visibility = Visibility.Visible;
                MeterLegendCache.Text = "Cache —";
                MeterLegendHistory.Text = "History —";
                MeterLegendLogs.Text = "Logs —";
                return;
            }
            MeterGrid.Visibility = Visibility.Visible;
            MeterEmpty.Visibility = Visibility.Collapsed;
            MeterCacheCol.Width = new GridLength(cacheBytes * 100.0 / total, GridUnitType.Star);
            MeterHistoryCol.Width = new GridLength(historyBytes * 100.0 / total, GridUnitType.Star);
            MeterLogsCol.Width = new GridLength(logsBytes * 100.0 / total, GridUnitType.Star);
            MeterLegendCache.Text = "Cache " + FmtBytes(cacheBytes);
            MeterLegendHistory.Text = "History " + FmtBytes(historyBytes);
            MeterLegendLogs.Text = "Logs " + FmtBytes(logsBytes);
        }

        // ---------- two-step delete confirms (suggestion 13) ----------

        /// <summary>First click arms the button (it turns into a red-tinted
        /// "Confirm delete?" for three seconds); a second click inside the
        /// window executes. Replaces the old modal MessageBox for these
        /// low-stakes deletes. restBgKey null = back to plain transparent
        /// (the OutlinedButton's own look).</summary>
        private void ArmConfirm(Button btn, string armedText, Action confirm,
                                string armedBgKey, string armedFgKey,
                                string restBgKey, string restFgKey)
        {
            var st = btn.Tag as ConfirmState;
            if (st == null)
            {
                st = new ConfirmState();
                btn.Tag = st;
            }
            if (!st.Armed)
            {
                st.Armed = true;
                st.Original = btn.Content;
                btn.Content = armedText;
                btn.SetResourceReference(Control.BackgroundProperty, armedBgKey);
                btn.SetResourceReference(Control.ForegroundProperty, armedFgKey);
                st.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += (s, e) => DisarmConfirm(btn, st, restBgKey, restFgKey);
                st.Timer.Start();
            }
            else
            {
                DisarmConfirm(btn, st, restBgKey, restFgKey);
                confirm();
            }
        }

        private static void DisarmConfirm(Button btn, ConfirmState st, string restBgKey, string restFgKey)
        {
            if (st.Timer != null)
            {
                st.Timer.Stop();
                st.Timer = null;
            }
            st.Armed = false;
            btn.Content = st.Original;
            if (restBgKey == null) btn.Background = Brushes.Transparent;
            else btn.SetResourceReference(Control.BackgroundProperty, restBgKey);
            btn.SetResourceReference(Control.ForegroundProperty, restFgKey);
        }

        private void CacheDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            ArmConfirm((Button)sender, "Confirm delete?", () =>
            {
                bool deleted = false;
                try
                {
                    if (File.Exists(PluginCatalog.CatalogPath))
                    {
                        File.Delete(PluginCatalog.CatalogPath);
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
                InfoToast("Cache deleted", "The catalog rebuilds on the next system scan.");
            }, "B.ErrorContainer", "B.OnErrorContainer", "B.Primary", "B.OnPrimary");
        }

        private void HistoryDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            ArmConfirm((Button)sender, "Confirm delete?", () =>
            {
                bool deleted = HistoryStore.Clear();
                LogService.Append("storage: recent-files history " +
                                  (deleted ? "deleted" : "already empty"));
                RefreshStorageInfo();
                InfoToast("History cleared", "Recent presets refills as new files are analyzed.");
            }, "B.ErrorContainer", "B.OnErrorContainer", "B.Primary", "B.OnPrimary");
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ArmConfirm((Button)sender, "Confirm reset?", () =>
            {
                ThemeService.ApplyWithSystem(false, Md3Mode.Light, Md3Palette.Teal);
                InfoToast("Defaults restored", "Light · Teal · system-follow off.");
            }, "B.ErrorContainer", "B.OnErrorContainer", null, "B.Primary");
        }

        /// <summary>The generic feedback pill in the window's corner
        /// (suggestion 14) — MainWindow owns it; no owner, no toast.</summary>
        private void InfoToast(string title, string sub)
        {
            (Window.GetWindow(this) as MainWindow)?.ShowInfoToast(title, sub);
        }

        private void OpenFolder_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // CatalogPath's getter creates the folder if it never
                // existed, so Explorer always has something to open.
                Process.Start("explorer.exe",
                    "\"" + Path.GetDirectoryName(PluginCatalog.CatalogPath) + "\"");
            }
            catch { /* Explorer refused — nothing sensible to do */ }
        }

        private void LogsRow_Click(object sender, MouseButtonEventArgs e) =>
            LogService.RevealLatest();

        // ---------- appearance: segmented control + swatches ----------

        private void SyncFromTheme()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                SegLight.IsChecked = !ThemeService.FollowSystem && ThemeService.Mode == Md3Mode.Light;
                SegDark.IsChecked = !ThemeService.FollowSystem && ThemeService.Mode == Md3Mode.Dark;
                SegSystem.IsChecked = ThemeService.FollowSystem;
                RefreshSwatchSelection();
            }
            finally { _syncing = false; }
        }

        private void ApplyFollow(bool follow)
        {
            if (_syncing) return;
            _syncing = true;
            try { ThemeService.ApplyWithSystem(follow, ThemeService.Mode, ThemeService.Palette); }
            finally { _syncing = false; }
        }

        private void ApplyMode(Md3Mode mode)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                // picking Light or Dark explicitly hands the mode back to
                // the user — system-follow ends right here
                ThemeService.ApplyWithSystem(false, mode, ThemeService.Palette);
            }
            finally { _syncing = false; }
        }

        private void BuildPaletteSwatches()
        {
            foreach (var (palette, swatch) in Palettes)
            {
                var p = palette; // capture
                // Swatch = circle + selection ring + checkmark layered on a
                // shared 54px hit Grid. NEVER nest the circle inside a
                // rounded Border: WPF Border clips its child to the rounded
                // interior, and 54 − 2×2.5 border − 2×4 padding = 41px of
                // interior vs a 44px circle sheared the bottom arc clean
                // off — the lop-sided "blob" swatches from the round-2
                // screenshot review.
                var circle = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(swatch),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                // selection badge — the mini-pill: a 22×12 primary pill
                // with the white dot parked right, a miniature of the
                // settings switch. Replaces the checkmark chip the same
                // way every other selector checkmark went mini-pill
                var badge = new MiniPill
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 2, 2),
                    Visibility = Visibility.Collapsed,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(0, 0)
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
                    Cursor = Cursors.Hand,
                    ToolTip = p + " palette"
                };
                hit.Children.Add(circle);
                hit.Children.Add(ring);
                hit.Children.Add(badge);
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
                var tag = new StackPanel { Tag = (ring, name, p, badge, circle) };
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
                var (ring, name, p, badge, circle) =
                    ((System.Windows.Shapes.Ellipse, TextBlock, Md3Palette, Border, Border))tag.Tag;
                bool selected = ThemeService.Palette == p;
                ring.Stroke = selected ? (Brush)FindResource("B.Primary") : Brushes.Transparent;
                name.Foreground = selected ? (Brush)FindResource("B.Primary") : (Brush)FindResource("B.OnSurfaceVariant");
                badge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (selected && _lastSelectedPalette != p)
                {
                    // the badge pops in and the swatch gives a tiny spring —
                    // the old checkmark's motion, split across both layers
                    var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                    {
                        From = 0.94,
                        EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                    };
                    var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                    {
                        From = 0.4,
                        EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
                    };
                    ((ScaleTransform)circle.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                    ((ScaleTransform)circle.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                    ((ScaleTransform)badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    ((ScaleTransform)badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
            }
            _lastSelectedPalette = ThemeService.Palette;
        }

        // ---------- about: identity row (click-to-copy chip) ----------

        private void VersionRow_Click(object sender, MouseButtonEventArgs e)
        {
            try { Clipboard.SetText(AppInfo.DisplayVersion); }
            catch { /* a locked clipboard just skips the copy */ }
            ShowCopiedChip(AboutCopyChip);
        }

        private void AppVersionRow_Click(object sender, MouseButtonEventArgs e)
        {
            try { Clipboard.SetText(AppInfo.DisplayVersion); }
            catch { /* a locked clipboard just skips the copy */ }
            ShowCopiedChip(AppVersionChip);
        }

        /// <summary>Computes the running exe's SHA-256 (the hash the
        /// release page publishes) and copies it — the same value the
        /// updater verifies downloads against.</summary>
        private void ShaRow_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    InfoToast("Couldn't hash", "The running exe's path could not be located.");
                    return;
                }
                string hash;
                using (var fs = File.OpenRead(exe))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (byte b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                    hash = sb.ToString();
                }
                Clipboard.SetText(hash);
                ShaRow.ToolTip = hash; // the full hash, one hover away
                InfoToast("SHA-256 copied", hash.Substring(0, 16) + "…");
            }
            catch { InfoToast("Couldn't hash", "Reading the running exe failed."); }
        }

        /// <summary>The "Copied" chip: fades in over the row, holds a
        /// breath, fades out. Replaces the old corner-text checkmark.</summary>
        private void ShowCopiedChip(Border chip)
        {
            chip.Visibility = Visibility.Visible;
            chip.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            if (chip.Tag is DispatcherTimer timer)
            {
                timer.Stop();
            }
            else
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    fadeOut.Completed += (s2, e2) => chip.Visibility = Visibility.Collapsed;
                    chip.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                chip.Tag = timer;
            }
            timer.Start();
        }

        // ---------- updates: the switches' whole rows toggle them ----------

        private void VerboseRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton) return;
            VerboseCheck.IsChecked = VerboseCheck.IsChecked != true;
        }

        private void AutoCheckRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton) return;
            AutoCheckSwitch.IsChecked = AutoCheckSwitch.IsChecked != true;
        }

        private void BetaCheckRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton) return;
            BetaCheckSwitch.IsChecked = BetaCheckSwitch.IsChecked != true;
        }

        private void NotifyRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton) return;
            NotifySwitch.IsChecked = NotifySwitch.IsChecked != true;
        }

        // ---------- updates: maintenance ----------

        private void ClearDownloadsRow_Click(object sender, MouseButtonEventArgs e)
        {
            int removed = UpdateService.ClearStagedUpdate();
            RefreshClearDownloadsRow();
            InfoToast(removed > 0 ? "Staged download cleared" : "Nothing to clear",
                removed > 0 ? "The updater's staging folder is empty again."
                            : "No staged update on disk.");
        }

        /// <summary>The clear-downloads row's caption — honest about what
        /// (if anything) a previous download left staged.</summary>
        private void RefreshClearDownloadsRow()
        {
            string info = UpdateService.StagedUpdateInfo;
            ClearDownloadsCaption.Text = info != null
                ? "Staged: " + info + " — click to delete"
                : "No staged update on disk";
        }

        // ---------- updates: the changelog drill-in ----------

        private void ChangelogRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (_changelogOpen) return;
            _changelogOpen = true;
            UpdatesView.Visibility = Visibility.Collapsed;
            ChangelogPane.Visibility = Visibility.Visible;
            AnimateViewIn(ChangelogPane);
            if (_releases == null && !_releasesLoading) LoadReleases();
        }

        /// <summary>Leaves the drill-in. Called by the back button (the
        /// user means it) and by every sub-nav switch (which returns to
        /// whatever tab was picked — the drill-in never traps a nav).</summary>
        private void ChangelogBack_Click(object sender, MouseButtonEventArgs e) => CloseChangelog();

        private void CloseChangelog()
        {
            if (!_changelogOpen) return;
            _changelogOpen = false;
            ChangelogPane.Visibility = Visibility.Collapsed;
            UpdatesView.Visibility = SubNav.SelectedIndex == 3
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CommitsRow_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/commits/main");

        /// <summary>Loads the release list once (newest first, drafts
        /// skipped) and renders the pill strip. Stable releases only — the
        /// rolling nightly is a moving target the pills would never settle
        /// on; the current build's pill is preselected when its tag is in
        /// the list.</summary>
        private void LoadReleases()
        {
            _releasesLoading = true;
            VersionPills.Children.Clear();
            var loading = new TextBlock
            {
                Text = "Asking GitHub for the release list…",
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 2)
            };
            loading.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            VersionPills.Children.Add(loading);

            ChangelogFeed.FetchReleasesAsync((list, error) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _releasesLoading = false;
                VersionPills.Children.Clear();
                if (list == null)
                {
                    var err = new TextBlock
                    {
                        Text = "Couldn't load the release list" +
                               (error != null ? " — " + error : "") + ".",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };
                    err.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                    VersionPills.Children.Add(err);
                    ReleaseTitle.Text = "No releases loaded";
                    return;
                }
                _releases = list;

                var stable = list.Where(r => !r.Prerelease).ToList();
                if (stable.Count == 0) stable = list;
                string current = AppInfo.DisplayVersion;
                int selectIdx = 0;
                for (int i = 0; i < stable.Count; i++)
                {
                    var r = stable[i];
                    var pill = new RadioButton
                    {
                        GroupName = "ReleasePills",
                        Content = r.Tag,
                        Style = (Style)FindResource("Md3PillChip"),
                        Margin = new Thickness(0, 0, 8, 0),
                        ToolTip = (r.DateIso != null ? r.DateIso + " — " : "") + "release notes"
                    };
                    pill.Checked += (s2, e2) => ShowRelease(r);
                    VersionPills.Children.Add(pill);
                    if (string.Equals(r.Tag.TrimStart('v'), current.TrimStart('v'),
                            StringComparison.OrdinalIgnoreCase))
                        selectIdx = i;
                }
                ((RadioButton)VersionPills.Children[selectIdx]).IsChecked = true;
            })));
        }

        /// <summary>Renders one release: heading, date, hero (the
        /// Stablemd "image::" line), description and the same bullet rows
        /// the feed renderer lays out everywhere — parsed with the
        /// workflow's own Stablemd rules.</summary>
        private void ShowRelease(ChangelogFeed.ReleaseSummary r)
        {
            ReleaseTitle.Text = r.Tag;
            if (DateTime.TryParseExact(r.DateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                ReleaseDate.Text = "Released " + dt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            else
                ReleaseDate.Text = null;

            var entry = StablemdParser.Parse(r.Tag, r.DateIso, r.Url, r.Body);
            ReleaseIntro.Text = entry.Description ?? "This release carries no description.";

            ReleaseHero.Visibility = Visibility.Collapsed;
            ReleaseHeroImage.Source = null;
            if (!string.IsNullOrEmpty(entry.ImageUrl))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(entry.ImageUrl);
                    bmp.EndInit();
                    ReleaseHeroImage.Source = bmp; // decodes async off the UI thread
                    ReleaseHero.Visibility = Visibility.Visible;
                }
                catch { /* a dead image URL just stays hidden */ }
            }

            ChangelogView.BuildInto(ReleaseSections, entry,
                includeDescription: false, showSectionTitles: true);

            ReleaseLink.Tag = string.IsNullOrEmpty(r.Url) ? null : r.Url;
            ReleaseLink.Visibility = string.IsNullOrEmpty(r.Url)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ReleaseLink_Click(object sender, MouseButtonEventArgs e)
        {
            if (ReleaseLink.Tag is string url) OpenUrl(url);
        }

        // ---------- about: project links (suggestion 20) ----------

        private void RepoRow_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl);

        private void IssueRow_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/issues/new");

        // ---------- updates: the status row (the four faces, one row) ----------

        private void UpdateStatusRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (UpdateService.PendingUpdate != null)
            {
                // something's already waiting — the row opens the updater
                OpenUpdater();
                return;
            }
            _manualChecking = true;
            RefreshUpdateStatus(); // the checking state, immediately
            UpdateService.CheckNowAsync((result, entry) =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _manualChecking = false;
                    RefreshUpdateStatus();
                })));
        }

        private void UpdateOpenButton_Click(object sender, RoutedEventArgs e) => OpenUpdater();

        private void OpenUpdater()
        {
            // the updater window owns the whole flow and stands the badge
            // and toast down via MarkSeen
            var win = UpdateService.PendingUpdate != null
                ? new UpdateWindow(UpdateService.PendingUpdate)
                : new UpdateWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
            RefreshUpdateStatus();
        }

        private void UpdatePageLink_Click(object sender, MouseButtonEventArgs e)
        {
            string url = UpdateService.PendingUpdate != null && !string.IsNullOrEmpty(UpdateService.PendingUpdate.Url)
                ? UpdateService.PendingUpdate.Url
                : RepoUrl + "/releases";
            OpenUrl(url);
        }

        private void UpdateSkipLink_Click(object sender, MouseButtonEventArgs e)
        {
            if (UpdateService.PendingUpdate == null) return;
            string version = UpdateService.PendingUpdate.Version;
            UpdateService.SetSkipVersion(version);
            UpdateService.PendingUpdate = null;
            UpdateService.MarkSeen();
            RefreshUpdateStatus();
            InfoToast("Version skipped", "v" + version + " stays quiet — the next release speaks up.");
        }

        /// <summary>Picks the status row's value from the service's last
        /// answer: checking → available → error → up-to-date / skipped /
        /// never-checked. "Available" is the one state that unfurls the
        /// detail strip (notes line + actions, skip included).</summary>
        private void RefreshUpdateStatus()
        {
            bool checking = _manualChecking || UpdateService.IsChecking;
            var status = UpdateService.LastCheckStatus;
            string ago = null;
            if (UpdateService.LastCheckUtc.HasValue)
                ago = HistoryStore.TimeAgo(UpdateService.LastCheckUtc.Value.ToLocalTime());

            bool available = !checking &&
                status == UpdateCheckStatus.UpdateAvailable &&
                UpdateService.PendingUpdate != null;
            bool error = !checking && !available && status == UpdateCheckStatus.Error;

            UpdateStatusSpinner.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
            if (checking)
            {
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                StatusSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            else
            {
                StatusSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            UpdateCheckedCaption.Text = ago != null
                ? "Last checked " + ago
                : "No check has run this session";

            if (checking)
            {
                UpdateStatusValue.Text = "Checking…";
                UpdateStatusValue.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                UpdateDetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (available)
            {
                UpdateStatusValue.Text = "v" + UpdateService.PendingUpdate.Version + " available";
                UpdateStatusValue.SetResourceReference(TextBlock.ForegroundProperty, "B.Primary");
                UpdateAvailSub.Text = "You're on v" + AppInfo.Version +
                    " — see what's new, then let the updater download, verify and swap it in.";
                UpdatePageLink.Visibility = Visibility.Visible;
                UpdateSkipLink.Visibility = Visibility.Visible;
                UpdateDetailPanel.Visibility = Visibility.Visible;
                return;
            }

            if (error)
            {
                UpdateStatusValue.Text = "Check failed";
                UpdateStatusValue.SetResourceReference(TextBlock.ForegroundProperty, "B.Error");
                UpdateAvailSub.Text = UpdateService.LastCheckMessage ??
                    "The releases page couldn't be reached.";
                UpdatePageLink.Visibility = Visibility.Visible;
                UpdateSkipLink.Visibility = Visibility.Collapsed;
                UpdateDetailPanel.Visibility = Visibility.Visible;
                return;
            }

            // idle: genuinely current, skipped-quiet, or never checked
            bool skipped = status == UpdateCheckStatus.UpdateAvailable &&
                           UpdateService.PendingUpdate == null;
            bool known = status == UpdateCheckStatus.UpToDate;
            UpdateStatusValue.Text = known ? "Up to date" : skipped ? "Skipped" : "Not checked yet";
            UpdateStatusValue.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            if (skipped)
            {
                UpdateAvailSub.Text = "v" + (UpdateService.SkipVersion ?? "?") +
                    " is skipped — the next release speaks up again. The release page still has it.";
                UpdatePageLink.Visibility = Visibility.Visible;
                UpdateSkipLink.Visibility = Visibility.Collapsed;
                UpdateDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateDetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        // ---------- misc ----------

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { /* browser launch refused — nothing sensible to do */ }
        }
    }
}
