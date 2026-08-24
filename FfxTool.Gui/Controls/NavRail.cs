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
    /// </summary>
    public partial class NavRail : UserControl
    {
        public event Action<int> SelectionChanged;
        public event Action FabClicked;

        private const int ItemHeight = 68;
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

        public void AddItem(string text, string icon)
        {
            int index = _buttons.Count;

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var iconEl = new IconGlyph
            {
                IconName = icon,
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            content.Children.Add(iconEl);
            content.Children.Add(label);

            var btn = new Button
            {
                Content = content,
                Width = 88,
                Height = ItemHeight,
                Focusable = false,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = text,
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
            double targetY = index * ItemHeight + (ItemHeight - PillSize) / 2.0;
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
                    glyph.Foreground = selected
                        ? (Brush)FindResource("B.Primary")
                        : (Brush)FindResource("B.OnSurfaceVariant");
                if (_buttons[i].Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                {
                    tb.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                    tb.Foreground = selected
                        ? (Brush)FindResource("B.Primary")
                        : (Brush)FindResource("B.OnSurfaceVariant");
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
            const string x =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "                 xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "                 TargetType=\"Button\">" +
                "  <Border x:Name=\"Bg\" Background=\"Transparent\" CornerRadius=\"14\">" +
                "    <ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "  </Border>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "      <Setter TargetName=\"Bg\" Property=\"Background\" Value=\"{DynamicResource B.SCHigh}\"/>" +
                "    </Trigger>" +
                "    <Trigger Property=\"IsPressed\" Value=\"True\">" +
                "      <Setter TargetName=\"Bg\" Property=\"Background\" Value=\"{DynamicResource B.SC}\"/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(x);
        }
    }
}
