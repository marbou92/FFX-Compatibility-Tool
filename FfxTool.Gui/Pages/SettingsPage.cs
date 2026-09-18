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
using System.Windows.Threading;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings hub, 0.2.2 edition. Four sub-pages on a shared M3 system:
    /// Appearance (Light/Dark/System segmented control, palette swatches
    /// with selection checkmarks, a live theme preview, a two-step restore),
    /// Storage (disk-usage meter, two-step delete confirms with toast
    /// feedback, the storage-folder and log rows), Plugin Profiles (the
    /// existing ProfilePage embedded verbatim, in matching chrome) and
    /// About (click-to-copy version, what's-new feed, an earlier-releases
    /// timeline fetched from GitHub, link rows, and an update status card
    /// with idle / checking / available / error faces).
    /// Above it all: a search box that jumps to any row, a sub-nav whose
    /// pill slides between items, Alt+1..4 shortcuts and a remembered
    /// last-visited tab (UiPrefs).
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

        // earlier-releases timeline
        private sealed class ReleaseRow
        {
            public string Tag;
            public string Date;
            public string Url;
        }
        private bool _timelineOpen;
        private bool _timelineLoaded;
        private bool _timelineLoading;

        private DispatcherTimer _copyHintTimer;

        public SettingsPage(ProfilePage profilePage)
        {
            InitializeComponent();
            VersionText.Text = "Version " + AppInfo.DisplayVersion;

            ProfileHost.Content = profilePage ?? throw new ArgumentNullException(nameof(profilePage));

            // ---- segmented theme control ----
            SegLight.Checked += (s, e) => ApplyMode(Md3Mode.Light);
            SegDark.Checked += (s, e) => ApplyMode(Md3Mode.Dark);
            SegSystem.Checked += (s, e) => ApplyFollow(true);

            // ---- storage ----
            VerboseCheck.Checked += (s, e) => LogService.Verbose = true;
            VerboseCheck.Unchecked += (s, e) => LogService.Verbose = false;
            VerboseCheck.IsChecked = LogService.Verbose;

            // ---- updates ----
            AutoCheckCheck.IsChecked = UpdateService.AutoCheckEnabled;
            AutoCheckCheck.Checked += (s, e) => UpdateService.SetAutoCheck(true);
            AutoCheckCheck.Unchecked += (s, e) => UpdateService.SetAutoCheck(false);
            // checks run on worker threads (the startup auto-check included)
            // — refresh the status card on the dispatcher whatever thread
            // the answer arrives on
            UpdateService.CheckFinished += result => Dispatcher.BeginInvoke(new Action(() =>
            {
                _manualChecking = false;
                RefreshUpdateCard();
            }));

            BuildWhatsNew();
            BuildSearchIndex();
            BuildPaletteSwatches();
            SyncFromTheme();
            RefreshUpdateCard();

            // remember the last-visited sub-tab (suggestion 1): restoring
            // fires SelectionChanged, which applies the views and saves the
            // value back; equal to the default index 0 it simply no-ops and
            // the XAML's own initial visibility carries the day.
            int saved = UiPrefs.SettingsTab;
            if (saved < 0 || saved > 3) saved = 0;
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
            // re-align, and freshen the status card's "last checked" line
            MoveSubNavPill(false);
            RefreshUpdateCard();
        }

        /// <summary>Keyboard sub-tab switching (Alt+1..4, wired from
        /// MainWindow) — also used by the search results to navigate.</summary>
        public void SelectSubTab(int index)
        {
            if (index < 0 || index > 3) return;
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
            bool appearance = idx == 0, storage = idx == 1, profile = idx == 2, about = idx == 3;
            AppearanceView.Visibility = appearance ? Visibility.Visible : Visibility.Collapsed;
            StorageView.Visibility = storage ? Visibility.Visible : Visibility.Collapsed;
            ProfileView.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
            AboutView.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

            if (storage) RefreshStorageInfo();

            AnimateViewIn(appearance ? (UIElement)AppearanceView
                           : storage ? (UIElement)StorageView
                           : profile ? (UIElement)ProfileView
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
            AddSearch(2, "Plugin Profiles", "Vendor checkboxes", "plugins profile vendors after effects versions", null);
            AddSearch(3, "About", "Version", "copy version number about build", VersionRow);
            AddSearch(3, "About", "What's new", "changelog release notes feed vivi", WhatsNewCard);
            AddSearch(3, "About", "Earlier releases", "history versions timeline github releases", TimelineToggle);
            AddSearch(3, "About", "Check for updates automatically", "auto silent startup badge toast", AutoCheckRow);
            AddSearch(3, "About", "Update status", "latest version download install up to date error retry", UpdateCard);
            AddSearch(3, "About", "GitHub repository", "source code repo open", RepoRow);
            AddSearch(3, "About", "Report an issue", "bug feedback problem support", IssueRow);
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
                var check = new IconGlyph
                {
                    IconName = "Check",
                    Width = 19,
                    Height = 19,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                check.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnPrimary");
                circle.Child = check;

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
                var tag = new StackPanel { Tag = (ring, name, p, check, circle) };
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
                var (ring, name, p, check, circle) =
                    ((System.Windows.Shapes.Ellipse, TextBlock, Md3Palette, IconGlyph, Border))tag.Tag;
                bool selected = ThemeService.Palette == p;
                ring.Stroke = selected ? (Brush)FindResource("B.Primary") : Brushes.Transparent;
                name.Foreground = selected ? (Brush)FindResource("B.Primary") : (Brush)FindResource("B.OnSurfaceVariant");
                check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (selected && _lastSelectedPalette != p)
                {
                    // a tiny spring on the newly selected swatch
                    var scale = (ScaleTransform)circle.RenderTransform;
                    var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                    {
                        From = 0.86,
                        EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                }
            }
            _lastSelectedPalette = ThemeService.Palette;
        }

        // ---------- about: identity, what's new, timeline ----------

        private void VersionRow_Click(object sender, MouseButtonEventArgs e)
        {
            try { Clipboard.SetText(AppInfo.DisplayVersion); }
            catch { /* a locked clipboard just skips the copy */ }
            CopyHint.Visibility = Visibility.Visible;
            if (_copyHintTimer == null)
            {
                _copyHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                _copyHintTimer.Tick += (s, e2) =>
                {
                    _copyHintTimer.Stop();
                    CopyHint.Visibility = Visibility.Collapsed;
                };
            }
            _copyHintTimer.Stop();
            _copyHintTimer.Start();
        }

        /// <summary>Renders the vivi-style what's-new panel from the
        /// changelog feed. The embedded snapshot (the newest release the
        /// build shipped with) fills it instantly; a successful online
        /// fetch of a DIFFERENT (newer) version's feed replaces it, so the
        /// panel never claims the wrong version.</summary>
        private void BuildWhatsNew()
        {
            var entry = ChangelogFeed.LoadEmbedded();
            if (entry == null)
            {
                WhatsNewTitle.Text = "What's new in this build";
                return;
            }
            WhatsNewTitle.Text = "What's new in v" + entry.Version;
            ChangelogView.BuildInto(WhatsNewPanel, entry, includeDescription: true);

            ChangelogFeed.FetchAsync(fresh =>
            {
                if (fresh == null || fresh.Version == entry.Version) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WhatsNewTitle.Text = "What's new in v" + fresh.Version + " — the latest release";
                    ChangelogView.BuildInto(WhatsNewPanel, fresh, includeDescription: true);
                }));
            });
        }

        // ---------- about: earlier-releases timeline (suggestion 18) ----------

        private void TimelineToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _timelineOpen = !_timelineOpen;
            TimelinePanel.Visibility = _timelineOpen ? Visibility.Visible : Visibility.Collapsed;
            var chevron = new DoubleAnimation(_timelineOpen ? 0 : -90, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            TimelineChevronRotate.BeginAnimation(RotateTransform.AngleProperty, chevron);
            if (!_timelineOpen)
            {
                TimelineCount.Text = _timelineLoaded ? TimelineCount.Text : "tap to load from GitHub";
                return;
            }
            if (!_timelineLoaded && !_timelineLoading) LoadTimeline();
        }

        private void LoadTimeline()
        {
            _timelineLoading = true;
            TimelineCount.Text = "loading…";
            TimelineList.Children.Clear();
            var loading = new TextBlock
            {
                Text = "Asking GitHub for the release list…",
                Margin = new Thickness(6, 6, 6, 8),
                FontSize = 12
            };
            loading.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            TimelineList.Children.Add(loading);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string json = null;
                string error = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create(
                        RepoUrl + "/releases?per_page=30");
                    req.Method = "GET";
                    req.UserAgent = "FFXCompatibilityTool/" + AppInfo.Version;
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                        System.Net.Cache.RequestCacheLevel.BypassCache);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var ms = new MemoryStream())
                    {
                        resp.GetResponseStream().CopyTo(ms);
                        json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
                catch (Exception ex) { error = ex.Message; }

                var releases = new List<ReleaseRow>();
                if (json != null)
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                if (el.ValueKind != JsonValueKind.Object) continue;
                                bool draft = el.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
                                bool pre = el.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True;
                                if (draft || pre) continue; // the nightly never answers here anyway
                                string tag = Str(el, "tag_name");
                                string url = Str(el, "html_url");
                                if (tag == null || url == null) continue;
                                string date = null;
                                if (el.TryGetProperty("published_at", out var pub) &&
                                    pub.ValueKind == JsonValueKind.String &&
                                    DateTime.TryParse(pub.GetString(), CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                                    date = dt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
                                releases.Add(new ReleaseRow { Tag = tag, Date = date, Url = url });
                                if (releases.Count >= 15) break;
                            }
                        }
                    }
                    catch { releases.Clear(); error = "the release list didn't parse"; }
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _timelineLoading = false;
                    TimelineList.Children.Clear();
                    if (json == null || (releases.Count == 0 && error != null))
                    {
                        _timelineLoaded = false; // the next expand retries
                        var err = new TextBlock
                        {
                            Text = "Couldn't load the release list" +
                                   (error != null ? " — " + error : "") + ".",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(6, 6, 6, 4),
                            FontSize = 12
                        };
                        err.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                        TimelineList.Children.Add(err);
                        TimelineList.Children.Add(MakeTimelineLinkRow("Open the releases page in your browser",
                            RepoUrl + "/releases"));
                        TimelineCount.Text = "couldn't load";
                        return;
                    }
                    foreach (var r in releases) TimelineList.Children.Add(MakeTimelineReleaseRow(r));
                    TimelineCount.Text = releases.Count + (releases.Count == 1 ? " release" : " releases");
                    _timelineLoaded = true;
                }));
            });
        }

        private static string Str(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private Border MakeTimelineReleaseRow(ReleaseRow r)
        {
            var grid = new Grid { Margin = new Thickness(6, 3, 6, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var clock = new IconGlyph { IconName = "Schedule", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            clock.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnSurfaceVariant");
            Grid.SetColumn(clock, 0);

            var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var tagText = new TextBlock { Text = r.Tag, FontSize = 12.5, FontWeight = FontWeights.Medium };
            tagText.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");
            stack.Children.Add(tagText);
            if (r.Date != null)
            {
                var dateText = new TextBlock { Text = r.Date, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0) };
                dateText.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                stack.Children.Add(dateText);
            }
            Grid.SetColumn(stack, 1);

            var open = new IconGlyph { IconName = "OpenInNew", Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center };
            open.SetResourceReference(IconGlyph.ForegroundProperty, "B.Primary");
            Grid.SetColumn(open, 2);

            grid.Children.Add(clock);
            grid.Children.Add(stack);
            grid.Children.Add(open);

            var border = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Opens " + r.Tag + " on GitHub"
            };
            border.MouseEnter += (s, e) => border.SetResourceReference(Border.BackgroundProperty, "B.SCHigh");
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (s, e) => OpenUrl(r.Url);
            return border;
        }

        private Border MakeTimelineLinkRow(string text, string url)
        {
            var label = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(6, 4, 6, 6) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "B.Primary");
            var border = new Border
            {
                Child = label,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 10, 4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            border.MouseLeftButtonUp += (s, e) => OpenUrl(url);
            return border;
        }

        // ---------- about: project links (suggestion 20) ----------

        private void RepoRow_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl);

        private void IssueRow_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/issues/new");

        // ---------- about: update status card (suggestion 17) ----------

        private void UpdateCheckNow_Click(object sender, RoutedEventArgs e)
        {
            _manualChecking = true;
            RefreshUpdateCard(); // the checking face, immediately
            UpdateService.CheckNowAsync((result, entry) =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _manualChecking = false;
                    RefreshUpdateCard();
                })));
        }

        private void UpdateOpenButton_Click(object sender, RoutedEventArgs e)
        {
            // the updater window owns the whole flow and stands the badge
            // and toast down via MarkSeen
            var win = UpdateService.PendingUpdate != null
                ? new UpdateWindow(UpdateService.PendingUpdate)
                : new UpdateWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
            RefreshUpdateCard();
        }

        private void UpdatePageLink_Click(object sender, MouseButtonEventArgs e)
        {
            string url = UpdateService.PendingUpdate != null && !string.IsNullOrEmpty(UpdateService.PendingUpdate.Url)
                ? UpdateService.PendingUpdate.Url
                : RepoUrl + "/releases";
            OpenUrl(url);
        }

        /// <summary>Picks the status card's face from the service's last
        /// answer: checking → available → error → up-to-date/never-checked.
        /// The "available" face tints the whole card primary-container so
        /// it reads as the one state that wants action.</summary>
        private void RefreshUpdateCard()
        {
            bool checking = _manualChecking || UpdateService.IsChecking;
            var status = UpdateService.LastCheckStatus;
            string ago = null;
            if (UpdateService.LastCheckUtc.HasValue)
                ago = HistoryStore.TimeAgo(UpdateService.LastCheckUtc.Value.ToLocalTime());

            bool checkingFace = checking;
            bool availableFace = !checking &&
                status == UpdateCheckStatus.UpdateAvailable &&
                UpdateService.PendingUpdate != null;
            bool errorFace = !checking && !availableFace && status == UpdateCheckStatus.Error;
            bool idleFace = !checking && !availableFace && !errorFace;

            StateChecking.Visibility = checkingFace ? Visibility.Visible : Visibility.Collapsed;
            StateAvailable.Visibility = availableFace ? Visibility.Visible : Visibility.Collapsed;
            StateError.Visibility = errorFace ? Visibility.Visible : Visibility.Collapsed;
            StateIdle.Visibility = idleFace ? Visibility.Visible : Visibility.Collapsed;

            if (checkingFace)
            {
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            else
            {
                SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            if (availableFace)
            {
                UpdateCard.SetResourceReference(Border.BackgroundProperty, "B.PrimaryContainer");
                UpdateAvailTitle.Text = "v" + UpdateService.PendingUpdate.Version + " is out";
                UpdateAvailSub.Text = "You're on v" + AppInfo.Version +
                    " — see what's new, then let the updater download, verify and swap it in.";
            }
            else
            {
                UpdateCard.SetResourceReference(Border.BackgroundProperty, "B.SC");
            }

            if (errorFace)
            {
                UpdateErrorSub.Text = UpdateService.LastCheckMessage ??
                    "The releases page couldn't be reached.";
            }

            if (idleFace)
            {
                bool known = status == UpdateCheckStatus.UpToDate;
                UpdateStateTitle.Text = known ? "You're up to date" : "Not checked yet";
                string stamp = ago != null ? "Last checked " + ago : "No check has run this session";
                UpdateStateSub.Text = known
                    ? stamp + " · v" + AppInfo.Version + " is the latest published version."
                    : stamp + " — the check resolves the project's latest release on GitHub; nothing else leaves this machine.";
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
