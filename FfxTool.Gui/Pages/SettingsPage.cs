using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings hub with sub-settings: Appearance, Plugin Profiles (the
    /// existing ProfilePage embedded verbatim — its logic is untouched) and
    /// About (with real project links). Theme changes apply instantly via
    /// ThemeService.
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
            ProfileHost.Visibility = SubNav.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            AboutView.Visibility = SubNav.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- project links ----------
        private void RepoLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl);

        private void IssueLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenUrl(RepoUrl + "/issues/new");

        private void Logs_Click(object sender, RoutedEventArgs e) =>
            LogService.RevealLatest();

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
