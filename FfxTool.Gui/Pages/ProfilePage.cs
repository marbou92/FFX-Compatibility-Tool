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
        private TextBlock _scanStatus; // live result line inside the discovery card

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
                { "Rowbyte", ("Plugin", "Plexus, TV Distortion") },
                { "Frischluft", ("Plugin", "Lenscare depth of field") },
                { "Mettle", ("Plugin", "FreeForm, Shape Shifter, SkyBox") },
                { "Neat Video", ("Plugin", "Temporal noise reduction") },
                { "Knoll Light Factory", ("Flare", "Lens flares by John Knoll") },
            };

        private static readonly Dictionary<string, string[]> VendorFileHints =
            new Dictionary<string, string[]>
            {
                { "Boris FX", new[] { "sapphire", "continuum", "bcc" } },
                { "Red Giant / Maxon", new[] { "magic bullet", "magicbullet", "trapcode", "red giant", "redgiant", "universe" } },
                { "Video Copilot", new[] { "element", "optical flares", "opticalflares", "saber", "twitch", "video copilot", "videocopilot" } },
                { "Plugin Everything", new[] { "deep glow", "deepglow", "shadow studio", "shadowstudio", "autofill", "plugin everything" } },
                { "RE:Vision Effects", new[] { "twixtor", "reelsmart", "re:vision", "revision", "re_vision", "rsmb" } },
                { "Rowbyte", new[] { "plexus", "rowbyte", "tv distortion", "tvdistortion", "bad tv", "badtv" } },
                { "Frischluft", new[] { "lenscare", "frischluft", "depth of field", "depthoffield" } },
                { "Mettle", new[] { "mettle", "freeform", "shape shifter", "shapeshifter", "skybox" } },
                { "Neat Video", new[] { "neat video", "neatvideo" } },
                { "Knoll Light Factory", new[] { "knoll", "light factory", "lightfactory" } },
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
            // dataset vendors join the prefix table's — every third-party
            // maker the AE reference knows gets a profile switch
            foreach (var vendor in _profile.AllKnownVendors(table, EffectNameLookup.Load()))
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
                Text = "Select your After Effects 'Plug-ins' directory — we'll catalog every effect on your system (file names plus match names read from the plugins themselves) and check that catalog FIRST, before the reference tables.",
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

            // live result line — the old scan reported nothing at all, so a
            // scan that found nothing was indistinguishable from a broken one
            var status = new TextBlock
            {
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            _scanStatus = status;

            var host = new StackPanel();
            host.Children.Add(grid);
            host.Children.Add(status);

            return new Border
            {
                Style = (Style)FindResource("Card"),
                Width = CardW * 2 + 16,
                MinHeight = 100,
                Margin = new Thickness(0, 0, 16, 16),
                Child = host
            };
        }

        private void ScanFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select your After Effects 'Plug-ins' folder",
                ShowNewFolderButton = false
            })
            {
                // open the dialog inside the newest AE install's Plug-ins
                // folder when one exists — no hunting through Program Files
                string suggested = SuggestAePluginsFolder();
                if (suggested != null) dlg.SelectedPath = suggested;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                ScanPath(dlg.SelectedPath);
            }
        }

        /// <summary>Newest Adobe After Effects install's Plug-ins dir, or null.</summary>
        private static string SuggestAePluginsFolder()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe");
                if (!Directory.Exists(root)) return null;
                return Directory.GetDirectories(root, "Adobe After Effects *")
                    .OrderByDescending(Directory.GetLastWriteTime)
                    .Select(inst => Path.Combine(inst, "Support Files", "Plug-ins"))
                    .FirstOrDefault(Directory.Exists);
            }
            catch { return null; }
        }

        /// <summary>
        /// Recursive, access-tolerant scan of an AE Plug-ins folder. Every
        /// .aex below the root counts; vendor identity is matched over the
        /// path RELATIVE to the root, so vendor subfolders ("Trapcode\\",
        /// "Video Copilot\\") carry the hit the way real installs nest.
        /// Matches flip their vendor switch on (Checked → SaveVendor) and
        /// the card reports exactly what was found — the old top-level-only
        /// scan silently found nothing on real machines, because every
        /// vendor nests in subfolders and one locked folder aborted it.
        /// </summary>
        private void ScanPath(string root)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                try
                {
                    files.AddRange(Directory.EnumerateFiles(dir, "*.aex", SearchOption.TopDirectoryOnly));
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                        pending.Push(sub);
                }
                catch (Exception ex)
                {
                    // one locked/odd subfolder must not abort the whole scan
                    LogService.Append("plugin scan: skipped \"" + dir + "\" — " +
                                      ex.GetType().Name + ": " + ex.Message);
                }
            }

            // catalog EVERY file — this is the first recognition option:
            // file/folder names, the file stem, and match-name-like strings
            // read straight out of each plugin binary (PiPL resources carry
            // them as plain ASCII)
            var catalog = new PluginCatalog();
            foreach (string file in files)
            {
                var entry = new CatalogFile { FilePath = file, Vendor = VendorFor(root, file) };
                try
                {
                    entry.Names.AddRange(PluginCatalog.HarvestNames(File.ReadAllBytes(file)));
                }
                catch (Exception ex)
                {
                    // an unreadable file still keeps its name in the catalog
                    LogService.Append("plugin scan: unreadable \"" + file + "\" — " +
                                      ex.GetType().Name + ": " + ex.Message);
                }
                string stem = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(stem) && !entry.Names.Contains(stem))
                    entry.Names.Insert(0, stem);
                catalog.Add(entry);
            }

            int flipped = 0;
            var found = new List<string>();
            foreach (var kv in VendorFileHints)
            {
                if (!files.Any(f => HintHit(root, f, kv.Value))) continue;
                found.Add(kv.Key);
                if (_switches.TryGetValue(kv.Key, out var pair))
                {
                    if (pair.Toggle.IsChecked != true)
                    {
                        pair.Toggle.IsChecked = true; // fires Checked → SaveVendor
                        flipped++;
                    }
                }
                else
                {
                    _profile.SetOwned(kv.Key, true);
                    flipped++;
                }
            }
            if (flipped > 0) _profile.Save();

            // only a scan that actually found something replaces the catalog
            // — picking a wrong folder must not wipe the previous one
            if (files.Count > 0)
            {
                catalog.Save();
                PluginRecognition.ResetCatalog();
            }

            string catalogText = catalog.NameCount + " effect names cataloged";
            string result;
            if (files.Count == 0)
                result = "no .aex plugin files found there — that doesn't look like an AE Plug-ins folder";
            else if (found.Count == 0)
                result = files.Count + " plugin files scanned — " + catalogText +
                         " — none match a profile vendor";
            else
                result = files.Count + " plugin files scanned — " + catalogText +
                         " — recognized: " + string.Join(", ", found) +
                         (flipped > 0 ? " (" + flipped + " linked now)" : " (already in profile)");
            ShowScanStatus(result);
            LogService.Append("plugin scan: " + files.Count + " .aex files under \"" + root + "\" — " + result);
        }

        /// <summary>First vendor whose hints hit the file's path relative to
        /// the scan root, or null when no folder/file name names one.</summary>
        private string VendorFor(string root, string file)
        {
            foreach (var kv in VendorFileHints)
                if (HintHit(root, file, kv.Value)) return kv.Key;
            return null;
        }

        private void ShowScanStatus(string text)
        {
            if (_scanStatus == null) return;
            _scanStatus.Text = text;
            _scanStatus.Visibility = Visibility.Visible;
        }

        /// <summary>Hint match over the path RELATIVE to the scan root —
        /// vendor subfolders ("Trapcode\\Particular.aex") carry the identity.</summary>
        private static bool HintHit(string root, string file, string[] hints)
        {
            string rel = file.Length > root.Length
                ? file.Substring(root.Length).TrimStart('\\', '/')
                : Path.GetFileName(file);
            rel = rel.ToLowerInvariant();
            return hints.Any(h => rel.Contains(h));
        }
    }
}
