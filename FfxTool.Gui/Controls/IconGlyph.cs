using System;
using System.Windows;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Vector icon glyph. Draws a Material Symbols geometry from
    /// Themes/Icons.xaml ("Icon.{Name}") tinted with the inherited
    /// Foreground — crisp at any size, re-themes automatically.
    /// </summary>
    public class IconGlyph : FrameworkElement
    {
        public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
            nameof(IconName), typeof(string), typeof(IconGlyph),
            new FrameworkPropertyMetadata("Info", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(IconGlyph),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public string IconName
        {
            get => (string)GetValue(IconNameProperty);
            set => SetValue(IconNameProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public IconGlyph()
        {
            // Center by default: with the inherited Stretch alignment and an
            // explicit Width/Height, WPF left-top-aligns the glyph inside its
            // container (rail items, icon chips, logo boxes...) — every icon
            // in the app sat off-center. Fixed once, here, for all usages.
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
        }

        private Geometry _geometry;
        private string _resolvedFrom;

        protected override void OnRender(DrawingContext dc)
        {
            string key = "Icon." + IconName;
            if (_geometry == null || _resolvedFrom != key)
            {
                _geometry = Application.Current.TryFindResource(key) as Geometry;
                _resolvedFrom = key;
            }
            if (_geometry == null) return;

            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) size = 24;
            double sw = _geometry.Bounds.Width, sh = _geometry.Bounds.Height;
            if (sw <= 0 || sh <= 0) return;
            double scale = Math.Min(size / sw, size / sh);

            var brush = Foreground ?? Brushes.Gray;
            dc.PushTransform(new TranslateTransform((ActualWidth - sw * scale) / 2, (ActualHeight - sh * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, null, _geometry);
            dc.Pop();
            dc.Pop();
        }
    }
}
