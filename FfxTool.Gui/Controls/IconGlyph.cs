using System;
using System.Windows;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Vector icon glyph. Draws a Material Symbols geometry from
    /// Themes/Icons.xaml ("Icon.{Name}") tinted with the inherited
    /// Foreground — crisp at any size, re-themes automatically.
    ///
    /// Rendering is GRID-based: every glyph is placed at its designed
    /// position on the 24x24 Material grid and the grid's centre is put on
    /// the element's centre. Scaling by each glyph's own bounds (the old
    /// approach) pushed every icon in the app ~3px right + down and over
    /// the edge of its box, because the bounds carry an on-grid origin
    /// (Bounds.X/Y) that a size-only centring forgets to cancel.
    /// </summary>
    public class IconGlyph : FrameworkElement
    {
        public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
            nameof(IconName), typeof(string), typeof(IconGlyph),
            new FrameworkPropertyMetadata("Info", FrameworkPropertyMetadataOptions.AffectsRender));

        // Inherits: an unpinned glyph picks up the ambient tint (caption
        // buttons brighten it on hover; sub-nav icons flip with selection).
        // Explicit setters / SetResourceReference still win over inheritance.
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(IconGlyph),
            new FrameworkPropertyMetadata(Brushes.Gray,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits));

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

            var b = _geometry.Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) size = 24;

            // Scale by the 24x24 design GRID, never by the glyph's own
            // bounds: each icon keeps its designed optical size and internal
            // padding, so the set looks consistent side by side.
            const double Grid = 24.0;
            double scale = size / Grid;

            // Put the grid centre (12,12) on the element centre — dead-on
            // centring for every on-grid glyph, no origin cancellation math.
            double tx = ActualWidth / 2 - (Grid / 2) * scale;
            double ty = ActualHeight / 2 - (Grid / 2) * scale;

            var brush = Foreground ?? Brushes.Gray;
            dc.PushTransform(new TranslateTransform(tx, ty));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, null, _geometry);
            dc.Pop();
            dc.Pop();
        }
    }
}
