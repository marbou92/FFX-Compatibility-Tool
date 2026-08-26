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
    /// All colors are attached via SetResourceReference (NOT captured brush
    /// instances) so the cards re-theme live when the palette or dark mode
    /// changes — the old FindResource captures froze with the theme that was
    /// active at startup and turned unreadable after switching.
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

            var iconGlyph = new IconGlyph { IconName = meta.icon, Width = 24, Height = 24 };
            // B.Primary — tracks palette swaps live
            iconGlyph.SetResourceReference(IconGlyph.ForegroundProperty, "B.Primary");

            var iconChip = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Child = iconGlyph
            };
            iconChip.SetResourceReference(Border.BackgroundProperty, "B.SCHigh");

            var title = new TextBlock
            {
                Text = vendor,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");

            var subtitle = new TextBlock
            {
                Text = meta.suites,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(iconChip, 0);
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
                Height = 14
            };
            var badgeText = new TextBlock
            {
                Text = sw.IsChecked == true ? "Profile linked" : "Not in profile",
                FontSize = 11,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

            var badgeStack = new StackPanel { Orientation = Orientation.Horizontal };
            badgeStack.Children.Add(badgeIcon);
            badgeStack.Children.Add(badgeText);

            var badge = new Border
            {
                Opacity = sw.IsChecked == true ? 1 : 0.6,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = badgeStack
            };
            badge.SetResourceReference(Border.BackgroundProperty, "B.SCHighest");

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
            // re-point the resource KEY (not a frozen brush) so the tint both
            // switches instantly and keeps tracking palette/dark-mode swaps
            pair.BadgeIcon.SetResourceReference(IconGlyph.ForegroundProperty,
                owned ? "B.Primary" : "B.OnSurfaceVariant");
        }

        private void SaveVendor(string vendor, bool owned)
        {
            _profile.SetOwned(vendor, owned);
            _profile.Save();
            _onChange?.Invoke();
        }

        private Border BuildAddCustomCard()
        {
            var icon = new IconGlyph
            {
                IconName = "Add",
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            icon.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnSurfaceVariant");

            var label = new TextBlock
            {
                Text = "Add custom vendor",
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(icon);
            stack.Children.Add(label);

            var card = new Border
            {
                Width = CardW,
                Height = 128,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1),
                Opacity = 0.55,
                ToolTip = "Not yet implemented",
                Child = stack
            };
            card.SetResourceReference(Border.BackgroundProperty, "B.SCLow");
            card.SetResourceReference(Border.BorderBrushProperty, "B.OutlineVariant");
            return card;
        }

        private Border BuildDiscoveryCard()
        {
            var title = new TextBlock
            {
                Text = "Automatic Plugin Discovery",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");

            var desc = new TextBlock
            {
                Text = "Select your After Effects 'Plug-ins' directory and we'll automatically check matching vendors.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Margin = new Thickness(0, 4, 0, 0)
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

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
