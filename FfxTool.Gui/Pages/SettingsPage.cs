using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Settings: live theme controls (dark mode switch, palette swatches)
    /// and the About card. Theme changes apply instantly via ThemeService.
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private static readonly (Md3Palette palette, Color swatch)[] Palettes =
        {
            (Md3Palette.Teal,   Color.FromRgb(0x00, 0x6B, 0x5F)),
            (Md3Palette.Blue,   Color.FromRgb(0x00, 0x5B, 0xBF)),
            (Md3Palette.Purple, Color.FromRgb(0x7A, 0x4F, 0xE0)),
            (Md3Palette.Orange, Color.FromRgb(0xB4, 0x54, 0x0A)),
        };

        private bool _syncing;

        public SettingsPage()
        {
            InitializeComponent();
            VersionText.Text = "Version " +
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            DarkSwitch.Checked += (s, e) => ApplyMode(Md3Mode.Dark);
            DarkSwitch.Unchecked += (s, e) => ApplyMode(Md3Mode.Light);

            BuildPaletteSwatches();
            SyncFromTheme();

            // keep the switch in sync when the mode changes elsewhere
            // (Restore Defaults) — previously a stale-switch desync bug
            ThemeService.Changed += SyncFromTheme;
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
                var circle = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(swatch),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                var ring = new Border
                {
                    Width = 54,
                    Height = 54,
                    CornerRadius = new CornerRadius(27),
                    BorderThickness = new Thickness(2.5),
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(4),
                    Child = circle,
                    Cursor = Cursors.Hand
                };
                var name = new TextBlock
                {
                    Text = p.ToString(),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var stack = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
                stack.Children.Add(ring);
                stack.Children.Add(name);
                var tag = new StackPanel { Tag = (ring, name, p) };
                tag.Children.Add(stack);
                ring.MouseLeftButtonUp += (s, e) =>
                {
                    if (_syncing) return;
                    _syncing = true;
                    try { ThemeService.Apply(ThemeService.Mode, p); }
                    finally { _syncing = false; }
                    RefreshSwatchSelection();
                };
                ring.Tag = (name, p);
                PaletteRow.Children.Add(tag);
            }
            RefreshSwatchSelection();
        }

        private void RefreshSwatchSelection()
        {
            foreach (StackPanel tag in PaletteRow.Children)
            {
                var (ring, name, p) = ((Border, TextBlock, Md3Palette))((StackPanel)tag.Children[0]).Tag;
                bool selected = ThemeService.Palette == p;
                ring.BorderBrush = selected ? (Brush)FindResource("B.Primary") : Brushes.Transparent;
                name.Foreground = selected ? (Brush)FindResource("B.Primary") : (Brush)FindResource("B.OnSurfaceVariant");
            }
        }

        private void Restore_Click(object sender, MouseButtonEventArgs e)
        {
            ThemeService.Apply(Md3Mode.Light, Md3Palette.Teal);
        }
    }
}
