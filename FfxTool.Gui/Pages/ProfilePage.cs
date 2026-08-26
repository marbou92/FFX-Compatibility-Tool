using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Plugin Profiles: bento grid of vendor cards with expressive switches,
    /// dynamic linked/not-linked badges, and the plugin-folder discovery card.
    /// </summary>
    public partial class ProfilePage : UserControl
    {
        private readonly PluginProfile _profile;
        private readonly Action _onChange;
        private readonly Dictionary<string, ToggleButtonSwitchPair> _switches = new Dictionary<string, ToggleButtonSwitchPair>();

        private class ToggleButtonSwitchPair
        {
            public System.Windows.Controls.Primitives.ToggleButton Toggle;
            public Border Badge;
            public TextBlock BadgeText;
            public IconGlyph BadgeIcon;
        }

        private static readonly Dictionary<string, (string icon, string suites)> VendorMeta =
            new Dictionary<string, (string, string)>
            {
                { "Boris FX", ("Diamond", "Sapphire, Continuum, Mocha") },
                { "Plugin Everything", ("Plugin", "Deep Glow, AutoFill") },
                { "RE:Vision Effects", ("Eye", "Twixtor, ReelSmart Motion Blur") },
                { "Red Giant / Maxon", ("AutoAwesome", "Trapcode, Magic Bullet, VFX") },
                { "Video Copilot", ("Flare", "Optical Flares, Element 3D, Saber") },
            };

        private static readonly Dictionary<string, string[]> VendorFileHints =
            new Dictionary<string, string[]>
            {
                { "Boris FX", new[] { "sapphire", "continuum", "bcc" } },
                { "Red Giant / Maxon", new[] { "magic bullet", "trapcode", "red giant" } },
                { "Video Copilot", new[] { "element", "optical flares", "saber", "twitch" } },
                { "Plugin Everything", new[] { "deep glow", "shadow studio" } },
                { "RE:Vision Effects", new[] { "twixtor", "reelsmart" } },
            };

        public ProfilePage(PluginProfile profile, Action onChange)
        {
            InitializeComponent();
            _profile = profile;
            _onChange = onChange;
            Build();
        }

        private void Build()
        {
            var table = PluginLookup.LoadTable();
            foreach (var vendor in _profile.AllKnownVendors(table))
                Cards.Children.Add(BuildVendorCard(vendor));
            Cards.Children.Add(BuildAddCustomCard());
            Cards.Children.Add(BuildDiscoveryCard());
        }

        private const int CardW = 300;

        private Border BuildVendorCard(string vendor)
        {
            var meta = VendorMeta.TryGetValue(vendor, out var m) ? m : (icon: "Plugin", suites: "");

            var iconChip = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("B.SCHigh"),
                Child = new IconGlyph
                {
                    IconName = meta.icon,
                    Width = 24,
                    Height = 24,
                    // B.Primary (teal) — the old B.TertiaryContainer was pale
                    // blue on a pale gray tile, ~1.5:1 contrast, nearly invisible
                    Foreground = (Brush)FindResource("B.Primary")
                }
            };

            var title = new TextBlock
            {
                Text = vendor,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("B.OnSurface"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var subtitle = new TextBlock
            {
                Text = meta.suites,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("B.OnSurfaceVariant"),
                Margin = new Thickness(0, 2, 0, 0)
            };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(iconChip, 0);
            Grid.SetColumn(title, 1);
            var titleStack = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            Grid.SetColumn(titleStack, 1);

            var sw = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("Md3Switch"),
                IsChecked = _profile.OwnedVendors.Contains(vendor),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sw, 2);
            header.Children.Add(iconChip);
            header.Children.Add(titleStack);
            header.Children.Add(sw);

            var badgeIcon = new IconGlyph
            {
                IconName = sw.IsChecked == true ? "Check" : "Info",
                Width = 14,
                Height = 14,
                Foreground = sw.IsChecked == true
                    ? (Brush)FindResource("B.Primary")
                    : (Brush)FindResource("B.OnSurfaceVariant")
            };
            var badgeText = new TextBlock
            {
                Text = sw.IsChecked == true ? "Profile linked" : "Not in profile",
                FontSize = 11,
                Foreground = (Brush)FindResource("B.OnSurfaceVariant"),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badge = new Border
            {
                Background = new SolidColorBrush(((Color)FindResource("P.SCHighest"))),
                Opacity = sw.IsChecked == true ? 1 : 0.6,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new StackPanel { Orientation = Orientation.Horizontal }
            };
            ((StackPanel)badge.Child).Children.Add(badgeIcon);
            ((StackPanel)badge.Child).Children.Add(badgeText);

            var card = new Border
            {
                Style = (Style)FindResource("Card"),
                Width = CardW,
                MinHeight = 128, // auto-height: wrapped subtitles no longer crush the badge row out of the card
                Margin = new Thickness(0, 0, 16, 16),
                Child = new Grid
                {
                    RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() },
                    Children = { header, badge }
                }
            };
            Grid.SetRow(badge, 1);
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = new Thickness(0, 8, 0, 0);

            var pair = new ToggleButtonSwitchPair { Toggle = sw, Badge = badge, BadgeText = badgeText, BadgeIcon = badgeIcon };
            _switches[vendor] = pair;

            sw.Checked += (s, e) => { UpdateBadge(vendor); SaveVendor(vendor, true); };
            sw.Unchecked += (s, e) => { UpdateBadge(vendor); SaveVendor(vendor, false); };
            return card;
        }

        private void UpdateBadge(string vendor)
        {
            var pair = _switches[vendor];
            bool owned = pair.Toggle.IsChecked == true;
            pair.BadgeText.Text = owned ? "Profile linked" : "Not in profile";
            pair.Badge.Opacity = owned ? 1 : 0.6;
            pair.BadgeIcon.IconName = owned ? "Check" : "Info";
            pair.BadgeIcon.Foreground = owned
                ? (Brush)FindResource("B.Primary")
                : (Brush)FindResource("B.OnSurfaceVariant");
        }

        private void SaveVendor(string vendor, bool owned)
        {
            _profile.SetOwned(vendor, owned);
            _profile.Save();
            _onChange?.Invoke();
        }

        private Border BuildAddCustomCard()
        {
            var card = new Border
            {
                Width = CardW,
                Height = 128,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(20),
                Background = (Brush)FindResource("B.SCLow"),
                BorderBrush = (Brush)FindResource("B.OutlineVariant"),
                BorderThickness = new Thickness(1),
                Opacity = 0.55,
                ToolTip = "Not yet implemented",
                Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var icon = new IconGlyph
            {
                IconName = "Add",
                Width = 22,
                Height = 22,
                Foreground = (Brush)FindResource("B.OnSurfaceVariant"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = "Add custom vendor",
                FontSize = 12.5,
                Foreground = (Brush)FindResource("B.OnSurfaceVariant"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            ((StackPanel)card.Child).Children.Add(icon);
            ((StackPanel)card.Child).Children.Add(label);
            return card;
        }

        private Border BuildDiscoveryCard()
        {
            var title = new TextBlock
            {
                Text = "Automatic Plugin Discovery",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("B.OnSurface")
            };
            var desc = new TextBlock
            {
                Text = "Select your After Effects 'Plug-ins' directory and we'll automatically check matching vendors.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Foreground = (Brush)FindResource("B.OnSurfaceVariant"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(title);
            textStack.Children.Add(desc);

            var scanBtn = new Button
            {
                Content = "Scan System",
                Style = (Style)FindResource("TonalButton"),
                Width = 170,
                VerticalAlignment = VerticalAlignment.Center
            };
            scanBtn.Click += (s, e) => ScanFolder();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(scanBtn, 1);
            grid.Children.Add(textStack);
            grid.Children.Add(scanBtn);

            return new Border
            {
                Style = (Style)FindResource("Card"),
                Width = CardW * 2 + 16,
                MinHeight = 100,
                Margin = new Thickness(0, 0, 16, 16),
                Child = grid
            };
        }

        private void ScanFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select your AE plugins folder" })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string[] files;
                try
                {
                    files = Directory.GetFiles(dlg.SelectedPath)
                        .Select(fn => Path.GetFileName(fn).ToLowerInvariant())
                        .ToArray();
                }
                catch
                {
                    // unreadable folder (locked/no access) — scan is best-effort
                    return;
                }
                foreach (var kv in VendorFileHints)
                {
                    if (!_switches.ContainsKey(kv.Key)) continue;
                    if (files.Any(fn => kv.Value.Any(h => fn.Contains(h))) && _switches[kv.Key].Toggle.IsChecked != true)
                        _switches[kv.Key].Toggle.IsChecked = true; // fires Checked → SaveVendor
                }
            }
        }
    }
}
