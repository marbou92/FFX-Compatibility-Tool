using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FfxTool.Gui
{
    /// <summary>
    /// M3 Expressive navigation rail: brand mark, expressive FAB, items with
    /// icon-over-label and a spring-animated active-indicator pill.
    /// Items are built in code so the pill geometry stays in sync.
    ///
    /// Item anatomy (constants below must stay consistent):
    ///   [ 50px icon wrap  <- the active pill circle backs exactly this ]
    ///   [  4px gap                                      ]
    ///   [ 16px label                                    ]
    /// = 70px content, centred in a 76px item -> 3px top inset, so the icon
    /// centre and the pill centre share the same Y. (The old layout stacked
    /// icon+label and centred the pair, leaving the icon floating ~6px
    /// above the pill's middle.)
    /// </summary>
    public partial class NavRail : UserControl
    {
        public event Action<int> SelectionChanged;
        public event Action FabClicked;

        private const int ItemHeight = 76;
        private const int IconWrap = 50;
        private const int LabelHeight = 16;
        private const int LabelGap = 4;
        private const int ContentHeight = IconWrap + LabelGap + LabelHeight; // 70
        private const int PillSize = 50;
        private readonly System.Collections.Generic.List<Button> _buttons = new System.Collections.Generic.List<Button>();
        private int _selectedIndex;

        public int SelectedIndex => _selectedIndex;

        public NavRail()
        {
            InitializeComponent();
            Fab.Click += (s, e) => FabClicked?.Invoke();
            // playful M3E touch: the FAB's plus rotates on hover
            Fab.MouseEnter += (s, e) => AnimateFabRotation(90);
            Fab.MouseLeave += (s, e) => AnimateFabRotation(0);
        }

        public void AddItem(string text, string icon, string toolTip = null)
        {
            int index = _buttons.Count;

            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var iconWrap = new Grid
            {
                Width = IconWrap,
                Height = IconWrap,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var iconEl = new IconGlyph
            {
                IconName = icon,
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconWrap.Children.Add(iconEl);
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Height = LabelHeight,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, LabelGap, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            content.Children.Add(iconWrap);
            content.Children.Add(label);

            var btn = new Button
            {
                Content = content,
                Width = 88,
                Height = ItemHeight,
                Focusable = false,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = toolTip ?? text,
                Tag = iconEl
            };
            btn.Template = MakeItemTemplate();
            btn.Click += (s, e) => Select(index);
            Items.Children.Add(btn);
            _buttons.Add(btn);

            if (_buttons.Count == 1)
            {
                _selectedIndex = 0;
                ApplySelectionVisuals(0, animate: false);
            }
        }

        public void Select(int index)
        {
            if (index == _selectedIndex || index < 0 || index >= _buttons.Count) return;
            int old = _selectedIndex;
            _selectedIndex = index;
            ApplySelectionVisuals(index, animate: true);
            SelectionChanged?.Invoke(index);
        }

        public void SelectWithoutNotify(int index)
        {
            if (index < 0 || index >= _buttons.Count) return;
            _selectedIndex = index;
            ApplySelectionVisuals(index, animate: true);
        }

        private void ApplySelectionVisuals(int index, bool animate)
        {
            // icon wrap top inside the item = (ItemHeight - ContentHeight)/2;
            // the pill backs the wrap one-to-one, so it uses the same offset.
            double targetY = index * ItemHeight + (ItemHeight - ContentHeight) / 2.0;
            if (animate)
            {
                var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut }
                };
                PillMove.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            else
            {
                PillMove.BeginAnimation(TranslateTransform.YProperty, null);
                PillMove.Y = targetY;
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool selected = i == index;
                if (_buttons[i].Tag is IconGlyph glyph)
                    // resource KEY, not a captured brush — WPF re-resolves these
                    // on every palette/dark-mode dictionary swap, so the rail
                    // can no longer go stale after a theme change
                    glyph.SetResourceReference(IconGlyph.ForegroundProperty,
                        selected ? "B.Primary" : "B.OnSurfaceVariant");
                if (_buttons[i].Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                {
                    tb.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                    tb.SetResourceReference(TextBlock.ForegroundProperty,
                        selected ? "B.Primary" : "B.OnSurfaceVariant");
                }
            }
        }

        private void AnimateFabRotation(double degrees)
        {
            if (FabIcon?.RenderTransform is RotateTransform rt)
            {
                var anim = new DoubleAnimation(degrees, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                };
                rt.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
        }

        private ControlTemplate MakeItemTemplate()
        {
            // Hover/pressed are M3 state layers (semi-transparent OnSurface),
            // NOT an opaque fill — the old SCHigh fill painted straight over
            // the pill layer and hid the active indicator under the mouse.
            const string x =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "                 xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "                 TargetType=\"Button\">" +
                "  <Grid>" +
                "    <Border x:Name=\"Bg\" Background=\"Transparent\" CornerRadius=\"14\"/>" +
                "    <Border x:Name=\"StateLayer\" Background=\"{DynamicResource B.OnSurface}\" CornerRadius=\"14\" Opacity=\"0\"/>" +
                "    <ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "  </Grid>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "      <Setter TargetName=\"StateLayer\" Property=\"Opacity\" Value=\"0.08\"/>" +
                "    </Trigger>" +
                "    <Trigger Property=\"IsPressed\" Value=\"True\">" +
                "      <Setter TargetName=\"StateLayer\" Property=\"Opacity\" Value=\"0.14\"/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(x);
        }
    }
}
