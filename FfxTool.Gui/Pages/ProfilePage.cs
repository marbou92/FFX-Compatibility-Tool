using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Plugin Profiles: a CURATED set of vendor cards — the AE reference
    /// dataset's long tail (one-off script authors and catalog entries)
    /// deliberately does not generate cards here anymore; it still powers
    /// recognition in the Lister, but the profile grid only offers real,
    /// installable plugin makers, and every card carries a description.
    /// Cards live in one of two sections — LINKED (switch on) and
    /// AVAILABLE (switch off) — and physically move between them (with a
    /// fade + rise) the moment a switch flips. Every card's switch is a
    /// real Md3Switch and the badge beside it is a check chip — a primary
    /// circle with a white check when linked, a quiet outlined plus when
    /// not — the same mark language the palette swatch and the Copied
    /// chip speak (the old switch miniature read as a second toggle in a
    /// card that already has the real switch, and retired). A filter box
    /// narrows both sections live; the section
    /// headers carry two-step Link all / Unlink all actions; the custom
    /// vendor card actually adds vendors now; and per-vendor counts from
    /// the scan catalog read as ".aex files cataloged" captions. All
    /// colors are attached via SetResourceReference (NOT captured brush
    /// instances) so the cards re-theme live when the palette or dark
    /// mode changes — the old FindResource captures froze with the theme
    /// that was active at startup and turned unreadable after switching.
    /// </summary>
    public partial class ProfilePage : UserControl
    {
        private readonly PluginProfile _profile;
        private readonly Action _onChange;
        private readonly Dictionary<string, ToggleButtonSwitchPair> _switches = new Dictionary<string, ToggleButtonSwitchPair>();
        private TextBlock _scanStatus; // live result line inside the discovery card
        private WrapPanel _scanChips;  // per-vendor result chips under it
        private TextBlock _aeSuggest;  // the detected AE Plug-ins folder shortcut
        // the two profile sections and the cards that move between them
        private Grid _linkedHead;
        private Grid _otherHead;
        private WrapPanel _linkedCards;
        private WrapPanel _otherCards;
        private Border _emptyLinked;
        private TextBlock _linkedCount;
        private TextBlock _otherCount;
        private TextBlock _linkAll;
        private TextBlock _unlinkAll;
        private Border _customCard;
        private Border _discoveryCard;
        private readonly List<string> _vendorOrder = new List<string>();
        private readonly Dictionary<string, Border> _cards = new Dictionary<string, Border>();
        // per-vendor .aex counts from the scan catalog (loaded off-thread)
        private Dictionary<string, int> _fileCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _countsLoading;

        private class ToggleButtonSwitchPair
        {
            public System.Windows.Controls.Primitives.ToggleButton Toggle;
            public Border Badge;
            public TextBlock BadgeText;
            public Border BadgeChip;
            public IconGlyph BadgeGlyph;
            public TextBlock CountCaption;
        }

        /// <summary>The curated profile list, in display order. Every entry
        /// carries an icon and a one-line description — a card without a
        /// description never ships. This list (plus legacy owned vendors)
        /// is the WHOLE grid: the AE reference dataset's script authors are
        /// catalog data for recognition, not profile cards.</summary>
        private static readonly (string vendor, string icon, string suites)[] CuratedVendors =
        {
            ("Red Giant / Maxon", "AutoAwesome", "Trapcode Particular, Magic Bullet, Universe — the Maxon effects collection"),
            ("Boris FX", "Diamond", "Sapphire, Continuum, Mocha Pro — broadcast-grade VFX and planar tracking"),
            ("Video Copilot", "Flare", "Optical Flares, Element 3D, Saber — lens flares and element 3D"),
            ("RE:Vision Effects", "Eye", "Twixtor, ReelSmart Motion Blur, FieldsKit — retiming and motion blur"),
            ("Rowbyte", "Plugin", "Plexus, TV Distortion — data-driven point grids and retro broadcast looks"),
            ("Plugin Everything", "Plugin", "Deep Glow, AutoFill, Shadow Studio — modern utility effects"),
            ("Frischluft", "Plugin", "Lenscare — fast, camera-accurate depth of field"),
            ("Mettle", "Plugin", "FreeForm, Shape Shifter, SkyBox — 3D mesh warping and 360°/VR"),
            ("Neat Video", "Plugin", "Temporal noise reduction for grainy or low-light footage"),
            ("Knoll Light Factory", "Flare", "Lens flares built by John Knoll (Industrial Light & Magic)"),
        };

        /// <summary>Fast lookup over CuratedVendors by vendor name.</summary>
        private static readonly Dictionary<string, (string icon, string suites)> VendorMeta =
            CuratedVendors.ToDictionary(e => e.vendor, e => (e.icon, e.suites), StringComparer.Ordinal);

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
            // The grid is the curated vendor list — the AE reference
            // dataset's one-off script authors no longer mint profile
            // cards (that catalog stays a recognition source in the
            // Lister, nothing more). Vendors already recorded in a legacy
            // profile stay visible so no saved switch is ever orphaned.
            foreach (var entry in CuratedVendors)
            {
                _vendorOrder.Add(entry.vendor);
                _cards[entry.vendor] = BuildVendorCard(entry.vendor);
            }
            foreach (var vendor in _profile.OwnedVendors)
            {
                if (_cards.ContainsKey(vendor)) continue;
                _vendorOrder.Add(vendor);
                _cards[vendor] = BuildVendorCard(vendor);
            }

            // two labeled sections: linked first, available below — a
            // flip physically moves the card between them. The headers
            // speak the settings GroupHeader language (small uppercase
            // labels), so the grid reads as part of the same page system,
            // and each carries its two-step bulk action on the right.
            var linkedTitle = new TextBlock
            {
                Text = "LINKED TO YOUR PROFILE",
                Style = (Style)FindResource("GroupHeader")
            };
            _linkedCount = new TextBlock { FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _linkedCount.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            _unlinkAll = BuildSectionAction("Unlink all", "Clears every linked vendor — click again to confirm", () => SetAll(false));
            _linkedHead = BuildSectionHead(linkedTitle, _linkedCount, _unlinkAll, new Thickness(0, 0, 16, 8));

            _emptyLinked = BuildEmptyLinked();

            _linkedCards = new WrapPanel();
            Cards.Children.Add(_linkedHead);
            Cards.Children.Add(_emptyLinked);
            Cards.Children.Add(_linkedCards);

            var otherTitle = new TextBlock
            {
                Text = "AVAILABLE VENDORS",
                Style = (Style)FindResource("GroupHeader")
            };
            _otherCount = new TextBlock { FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _otherCount.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
            _linkAll = BuildSectionAction("Link all", "Links every available vendor — click again to confirm", () => SetAll(true));
            _otherHead = BuildSectionHead(otherTitle, _otherCount, _linkAll, new Thickness(0, 14, 16, 8));

            _otherCards = new WrapPanel();
            Cards.Children.Add(_otherHead);
            Cards.Children.Add(_otherCards);

            // tools row: the custom-vendor card (real now) and the
            // discovery card close out the page
            _customCard = BuildAddCustomCard();
            _discoveryCard = BuildDiscoveryCard();
            _otherCards.Children.Add(_customCard);
            _otherCards.Children.Add(_discoveryCard);

            RefreshSections();
            LoadFileCounts();

            // the live filter: narrows both sections as you type
            FilterBox.TextChanged += (s, e) => ApplyFilter();
        }

        /// <summary>One section header: title + count on the left, the
        /// bulk action right-aligned — the settings-row header language.</summary>
        private Grid BuildSectionHead(System.Windows.Controls.TextBlock title,
            System.Windows.Controls.TextBlock count, TextBlock action, Thickness margin)
        {
            var head = new Grid { Margin = margin };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(title, 0);
            Grid.SetColumn(count, 1);
            Grid.SetColumn(action, 2);
            action.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(title);
            head.Children.Add(count);
            head.Children.Add(action);
            return head;
        }

        /// <summary>A two-step bulk action: first click arms it ("… — sure?")
        /// for three seconds; a second click inside the window executes —
        /// the RestoreButton pattern, in section-header size.</summary>
        private TextBlock BuildSectionAction(string label, string confirmTip, Action apply)
        {
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = confirmTip
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "B.Primary");
            bool armed = false;
            DispatcherTimer timer = null;
            tb.MouseLeftButtonUp += (s, e) =>
            {
                if (!armed)
                {
                    armed = true;
                    tb.Text = label + " — sure?";
                    timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s2, e2) =>
                    {
                        timer.Stop();
                        armed = false;
                        tb.Text = label;
                    };
                    timer.Start();
                    return;
                }
                if (timer != null) timer.Stop();
                armed = false;
                tb.Text = label;
                apply();
            };
            return tb;
        }

        /// <summary>Links or unlinks every vendor in one move — the bulk
        /// actions' executor. One save, one re-file, one callback.</summary>
        private void SetAll(bool owned)
        {
            foreach (var vendor in _vendorOrder)
                _profile.SetOwned(vendor, owned);
            _profile.Save();
            RefreshSections();
            _onChange?.Invoke();
        }

        /// <summary>The LINKED section's empty state: a quiet slot that
        /// says what to do and carries the scan shortcut inline — led by
        /// the same 34px icon tile every row and the discovery card use.</summary>
        private Border BuildEmptyLinked()
        {
            var iconTile = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconTile.SetResourceReference(Border.BackgroundProperty, "B.SCHighest");
            var icon = new IconGlyph { IconName = "Plugin", Width = 17, Height = 17 };
            icon.SetResourceReference(IconGlyph.ForegroundProperty, "B.Primary");
            iconTile.Child = icon;

            var text = new TextBlock
            {
                Text = "Nothing linked yet — flip a vendor's switch or scan your system.",
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 300,
                Margin = new Thickness(13, 0, 0, 0)
            };

            var scan = new Button
            {
                Content = "Scan System",
                Style = (Style)FindResource("TonalButton"),
                Height = 30,
                MinWidth = 110,
                VerticalAlignment = VerticalAlignment.Center
            };
            scan.Click += (s, e) => ScanFolder();

            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(iconTile);
            host.Children.Add(text);
            host.Children.Add(scan);

            // the slot surface matches the Add-custom-vendor card: SCLow
            // body, outline hairline (a bare BorderThickness rendered
            // nothing — no brush ever rode on it)
            var empty = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 16, 10),
                Child = host
            };
            empty.SetResourceReference(Border.BackgroundProperty, "B.SCLow");
            empty.SetResourceReference(Border.BorderBrushProperty, "B.OutlineVariant");
            return empty;
        }

        /// <summary>The card's arrival when it moves sections: fade + a
        /// small rise — the flip feels physical instead of teleporting.</summary>
        private void AnimateCardIn(Border card)
        {
            var slide = card.RenderTransform as TranslateTransform;
            if (slide == null)
            {
                slide = new TranslateTransform();
                card.RenderTransform = slide;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            card.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, dur) { EasingFunction = ease });
        }

        /// <summary>The live filter: cards hide unless the query hits the
        /// vendor name or its description; the tool cards (custom vendor,
        /// discovery) hide while a query is on; headers and counts follow
        /// what is actually visible.</summary>
        private void ApplyFilter()
        {
            string q = (FilterBox.Text ?? "").Trim();
            FilterPlaceholder.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            int linkedVisible = 0, otherVisible = 0;
            foreach (var vendor in _vendorOrder)
            {
                var card = _cards[vendor];
                bool owned = _profile.OwnedVendors.Contains(vendor);
                bool match = q.Length == 0 ||
                    vendor.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (VendorMeta.TryGetValue(vendor, out var meta) &&
                     meta.suites.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match)
                {
                    if (owned) linkedVisible++;
                    else otherVisible++;
                }
            }
            _customCard.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _discoveryCard.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            _linkedHead.Visibility = linkedVisible > 0 ? Visibility.Visible : Visibility.Collapsed;
            _otherHead.Visibility = otherVisible > 0 ? Visibility.Visible : Visibility.Collapsed;
            _emptyLinked.Visibility = q.Length == 0 && linkedVisible == 0
                ? Visibility.Visible : Visibility.Collapsed;

            _linkedCount.Text = linkedVisible == 0 ? "" : "· " + linkedVisible;
            _otherCount.Text = "· " + otherVisible;
        }

        /// <summary>Re-files every card into its section, then re-applies
        /// the filter so counts and headers stay honest. Runs after Build
        /// and after every toggle / bulk action.</summary>
        private void RefreshSections()
        {
            UpdateSections();
            if (_customCard != null && _discoveryCard != null) ApplyFilter();
        }

        /// <summary>
        /// Re-files every vendor card into its section (linked vs.
        /// available) in display order; cards that actually move get the
        /// fade + rise arrival. Counts, headers and the empty state are
        /// ApplyFilter's business — this only physically places cards.
        /// Runs after Build and after every toggle/scan flip, so the
        /// sections are always an honest snapshot of the saved profile.
        /// </summary>
        private void UpdateSections()
        {
            foreach (var vendor in _vendorOrder)
            {
                var card = _cards[vendor];
                bool owned = _profile.OwnedVendors.Contains(vendor);
                var target = owned ? _linkedCards : _otherCards;
                if (ReferenceEquals(card.Parent, target)) continue;
                (card.Parent as Panel)?.Children.Remove(card);
                target.Children.Add(card);
                AnimateCardIn(card);
            }
        }

        private const int CardW = 300;

        private Border BuildVendorCard(string vendor)
        {
            var meta = VendorMeta.TryGetValue(vendor, out var m)
                ? m
                // legacy/custom entries keep a description too — no bare
                // cards on this page
                : (icon: "Plugin", suites: "Custom vendor kept from your saved profile");

            var iconGlyph = new IconGlyph { IconName = meta.icon, Width = 18, Height = 18 };
            // B.Primary on the settings-row tile — tracks palette swaps live
            iconGlyph.SetResourceReference(IconGlyph.ForegroundProperty, "B.Primary");

            // the settings-row tile: 34px, corner 10, highest surface —
            // the same tile every row in the other tabs leads with
            var iconChip = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(10),
                Child = iconGlyph
            };
            iconChip.SetResourceReference(Border.BackgroundProperty, "B.SCHighest");

            var title = new TextBlock
            {
                Text = vendor,
                Style = (Style)FindResource("Body"),
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text = meta.suites,
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };

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

            // the badge is a check chip — the palette swatch's mark: a
            // primary circle with a white check when linked, a quiet
            // outlined plus when not. The switch miniature read as a
            // second toggle in a card that already carries the real one.
            var badgeChip = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeGlyph = new IconGlyph
            {
                IconName = "Check",
                Width = 11,
                Height = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeChip.Child = badgeGlyph;
            SetLinkedChip(badgeChip, badgeGlyph, sw.IsChecked == true);
            var badgeText = new TextBlock
            {
                Text = sw.IsChecked == true ? "Profile linked" : "Not in profile",
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var badgeStack = new StackPanel { Orientation = Orientation.Horizontal };
            badgeStack.Children.Add(badgeChip);
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

            // the per-vendor inventory line: what the scan catalog knows
            // about this vendor, e.g. "12 .aex files cataloged"
            var countCaption = new TextBlock
            {
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var badgeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 8, 0, 0)
            };
            badgeRow.Children.Add(badge);
            badgeRow.Children.Add(countCaption);

            var card = new Border
            {
                Style = (Style)FindResource("Card"),
                Width = CardW,
                MinHeight = 128, // auto-height: wrapped subtitles no longer crush the badge row out of the card
                Margin = new Thickness(0, 0, 16, 16),
                Child = new Grid
                {
                    RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() },
                    Children = { header, badgeRow }
                }
            };
            Grid.SetRow(badgeRow, 1);

            var pair = new ToggleButtonSwitchPair { Toggle = sw, Badge = badge, BadgeText = badgeText, BadgeChip = badgeChip, BadgeGlyph = badgeGlyph, CountCaption = countCaption };
            _switches[vendor] = pair;

            sw.Checked += (s, e) => { UpdateBadge(vendor); SaveVendor(vendor, true); };
            sw.Unchecked += (s, e) => { UpdateBadge(vendor); SaveVendor(vendor, false); };
            return card;
        }

        /// <summary>Dresses the linked chip: a primary circle with a white
        /// check when the vendor is linked, a quiet outlined circle with a
        /// plus when it isn't — check for "in", plus for "not yet". Colors
        /// attach to resource KEYS (never captured brushes) so the chip
        /// re-themes live with the palette.</summary>
        private static void SetLinkedChip(Border chip, IconGlyph glyph, bool owned)
        {
            if (owned)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "B.Primary");
                chip.BorderThickness = new Thickness(0);
                glyph.IconName = "Check";
                glyph.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnPrimary");
            }
            else
            {
                chip.Background = Brushes.Transparent;
                chip.SetResourceReference(Border.BorderBrushProperty, "B.OutlineVariant");
                chip.BorderThickness = new Thickness(1);
                glyph.IconName = "Add";
                glyph.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnSurfaceVariant");
            }
        }

        private void UpdateBadge(string vendor)
        {
            var pair = _switches[vendor];
            bool owned = pair.Toggle.IsChecked == true;
            pair.BadgeText.Text = owned ? "Profile linked" : "Not in profile";
            pair.Badge.Opacity = owned ? 1 : 0.6;
            SetLinkedChip(pair.BadgeChip, pair.BadgeGlyph, owned);
        }

        private void SaveVendor(string vendor, bool owned)
        {
            _profile.SetOwned(vendor, owned);
            _profile.Save();
            RefreshSections(); // the card physically moves between the sections
            _onChange?.Invoke();
        }

        /// <summary>The custom-vendor card, real now: click it and a
        /// mini-form drops in — a name box, Add, Cancel. The name becomes
        /// a full card (switch, mini-pill badge, sections) and future
        /// system scans match the name against plugin paths like any
        /// curated vendor's hints.</summary>
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
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

            var rest = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            rest.Children.Add(icon);
            rest.Children.Add(label);

            var nameBox = new TextBox
            {
                FontSize = 13,
                MinWidth = 170,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 5, 8, 5)
            };
            var addBtn = new Button
            {
                Content = "Add",
                Style = (Style)FindResource("TonalButton"),
                MinWidth = 74,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Style = (Style)FindResource("OutlinedButton"),
                MinWidth = 74,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var formRow = new StackPanel { Orientation = Orientation.Horizontal };
            formRow.Children.Add(nameBox);
            formRow.Children.Add(addBtn);
            formRow.Children.Add(cancelBtn);

            var hint = new TextBlock
            {
                Text = "The name becomes the card — future scans match it against plugin paths too.",
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 240,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var form = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            form.Children.Add(formRow);
            form.Children.Add(hint);
            form.Visibility = Visibility.Collapsed;

            var host = new Grid();
            host.Children.Add(rest);
            host.Children.Add(form);

            var card = new Border
            {
                Width = CardW,
                Height = 128,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1),
                Child = host
            };
            card.SetResourceReference(Border.BackgroundProperty, "B.SCLow");
            card.SetResourceReference(Border.BorderBrushProperty, "B.OutlineVariant");

            rest.Cursor = Cursors.Hand;
            rest.MouseLeftButtonUp += (s, e) =>
            {
                rest.Visibility = Visibility.Collapsed;
                form.Visibility = Visibility.Visible;
                nameBox.Focus();
            };
            cancelBtn.Click += (s, e) =>
            {
                form.Visibility = Visibility.Collapsed;
                rest.Visibility = Visibility.Visible;
                nameBox.Text = "";
            };

            var submit = new Action(() =>
            {
                string name = (nameBox.Text ?? "").Trim();
                if (name.Length == 0)
                {
                    nameBox.Focus();
                    return;
                }
                if (!_cards.ContainsKey(name))
                {
                    _vendorOrder.Add(name);
                    _cards[name] = BuildVendorCard(name);
                }
                _profile.SetOwned(name, true);
                _profile.Save();
                RefreshSections();
                _onChange?.Invoke();
                form.Visibility = Visibility.Collapsed;
                rest.Visibility = Visibility.Visible;
                nameBox.Text = "";
            });
            addBtn.Click += (s, e) => submit();
            nameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) submit();
            };
            return card;
        }

        private Border BuildDiscoveryCard()
        {
            var title = new TextBlock
            {
                Text = "Automatic Plugin Discovery",
                Style = (Style)FindResource("Body"),
                FontWeight = FontWeights.Medium
            };

            var desc = new TextBlock
            {
                Text = "Select your After Effects 'Plug-ins' directory — we'll catalog every effect on your system (file names plus match names read from the plugins themselves) and check that catalog FIRST, before the reference tables.",
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(title);
            textStack.Children.Add(desc);

            // the discovery card joins the settings-row language: a small
            // icon tile leads the row, like every other card on the page
            var tile = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 13, 0)
            };
            tile.SetResourceReference(Border.BackgroundProperty, "B.SCHighest");
            var tileIcon = new IconGlyph { IconName = "Search", Width = 17, Height = 17 };
            tileIcon.SetResourceReference(IconGlyph.ForegroundProperty, "B.Primary");
            tile.Child = tileIcon;

            var scanBtn = new Button
            {
                Content = "Scan System",
                Style = (Style)FindResource("TonalButton"),
                Width = 170,
                VerticalAlignment = VerticalAlignment.Center
            };
            scanBtn.Click += (s, e) => ScanFolder();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tile, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(scanBtn, 2);
            grid.Children.Add(tile);
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

            _scanChips = new WrapPanel
            {
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };

            // the detected AE Plug-ins folder, offered as one click —
            // no dialog hunting through Program Files
            _aeSuggest = new TextBlock
            {
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand
            };
            _aeSuggest.SetResourceReference(TextBlock.ForegroundProperty, "B.Primary");
            string suggested = SuggestAePluginsFolder();
            if (suggested != null)
            {
                _aeSuggest.Text = "Use " + suggested;
                _aeSuggest.ToolTip = "Scans this detected AE Plug-ins folder";
                string path = suggested;
                _aeSuggest.MouseLeftButtonUp += (s, e) => ScanPath(path);
            }

            var host = new StackPanel();
            host.Children.Add(grid);
            host.Children.Add(status);
            host.Children.Add(_scanChips);
            host.Children.Add(_aeSuggest);

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
            // curated hints first, then every custom vendor's own name as a
            // hint — a vendor added by hand gets recognized by its name
            // sitting in the plugin path, same as the curated ones (only
            // names long enough to be worth matching)
            var hints = new List<KeyValuePair<string, string[]>>(VendorFileHints);
            foreach (var vendor in _vendorOrder)
                if (!VendorMeta.ContainsKey(vendor) && vendor.Length >= 4)
                    hints.Add(new KeyValuePair<string, string[]>(vendor, new[] { vendor.ToLowerInvariant() }));
            foreach (var kv in hints)
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
            // the catalog changed — per-vendor counts refresh from it
            LoadFileCounts();

            string catalogText = catalog.NameCount + " effect names cataloged";
            ShowScanResult(files.Count, found, flipped, catalogText);
            LogService.Append("plugin scan: " + files.Count + " .aex files under \"" + root + "\" — " +
                              (_scanStatus != null ? _scanStatus.Text : "done"));
        }

        /// <summary>First vendor whose hints hit the file's path relative to
        /// the scan root, or null when no folder/file name names one.</summary>
        private string VendorFor(string root, string file)
        {
            foreach (var kv in VendorFileHints)
                if (HintHit(root, file, kv.Value)) return kv.Key;
            return null;
        }

        /// <summary>The scan's report: a summary line plus one chip per
        /// recognized vendor carrying its cataloged file count — the old
        /// single sentence made a 5-vendor find read like a footnote.</summary>
        private void ShowScanResult(int fileCount, List<string> found, int flipped, string catalogText)
        {
            if (_scanStatus == null) return;
            string summary;
            if (fileCount == 0)
                summary = "no .aex plugin files found there — that doesn't look like an AE Plug-ins folder";
            else if (found.Count == 0)
                summary = fileCount + " plugin files scanned — " + catalogText +
                          " — none match a profile vendor";
            else
                summary = fileCount + " plugin files scanned — " + catalogText +
                          " — recognized " + found.Count + (found.Count == 1 ? " vendor" : " vendors") +
                          (flipped > 0 ? " (" + flipped + " linked now)" : " (already in profile)");
            _scanStatus.Text = summary;
            _scanStatus.Visibility = Visibility.Visible;

            if (_scanChips == null) return;
            _scanChips.Children.Clear();
            foreach (var vendor in found)
            {
                int count = _fileCounts.TryGetValue(vendor, out var c) ? c : 0;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 6, 8, 0)
                };
                chip.SetResourceReference(Border.BackgroundProperty, "B.PrimaryContainer");
                var t = new TextBlock
                {
                    FontSize = 11.5,
                    Text = vendor + " — " + count + " file" + (count == 1 ? "" : "s")
                };
                t.SetResourceReference(TextBlock.ForegroundProperty, "B.OnPrimaryContainer");
                chip.Child = t;
                _scanChips.Children.Add(chip);
            }
            _scanChips.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Per-vendor .aex counts from the scan catalog, loaded
        /// off-thread (the catalog file can be megabytes) — feeds the
        /// count captions and the scan chips. The catalog's Files view is
        /// read-only; no scan state is touched.</summary>
        private void LoadFileCounts()
        {
            if (_countsLoading) return;
            _countsLoading = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                try
                {
                    foreach (var f in PluginRecognition.Catalog.Files)
                        if (!string.IsNullOrEmpty(f.Vendor))
                            counts[f.Vendor] = (counts.TryGetValue(f.Vendor, out var c) ? c : 0) + 1;
                }
                catch { /* no catalog yet — counts stay empty */ }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _countsLoading = false;
                    _fileCounts = counts;
                    RefreshCountCaptions();
                }));
            });
        }

        /// <summary>Shows or hides each card's "N .aex files cataloged"
        /// caption from the loaded counts. A vendor with nothing on disk
        /// stays quiet — absence is not an accusation.</summary>
        private void RefreshCountCaptions()
        {
            foreach (var kv in _switches)
            {
                var caption = kv.Value.CountCaption;
                if (caption == null) continue;
                if (_fileCounts.TryGetValue(kv.Key, out int n) && n > 0)
                {
                    caption.Text = n + " .aex file" + (n == 1 ? "" : "s") + " cataloged";
                    caption.Visibility = Visibility.Visible;
                }
                else
                {
                    caption.Visibility = Visibility.Collapsed;
                }
            }
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
