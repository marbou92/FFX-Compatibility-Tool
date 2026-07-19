using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// An MD3 "navigation rail" — a vertical list of destinations with a
    /// pill-shaped selected-state indicator, replacing WinForms' native
    /// TabControl (which cannot be restyled at all and looks dated on
    /// Win7's default theme).
    ///
    /// The selection pill animates its Y position on change (a simple
    /// Timer-driven lerp, ~150ms) rather than snapping instantly — the
    /// one contained animation added in this pass, kept local to this
    /// control so it can't destabilize anything else.
    /// </summary>
    public class NavRail : Panel
    {
        public class NavItem
        {
            public string Text;
            public Control Content;
        }

        readonly List<NavItem> _items = new List<NavItem>();
        readonly List<Rectangle> _itemBounds = new List<Rectangle>();
        int _selectedIndex = -1;

        // animation state
        readonly Timer _animTimer;
        float _pillY;       // current animated Y of the pill
        float _pillTargetY; // Y it's animating toward
        DateTime _animStart;
        const int AnimMs = 150;

        public event Action<int> SelectionChanged;

        const int ItemHeight = 48;
        const int ItemMarginX = Md3Tokens.Space3;
        const int ItemMarginY = Md3Tokens.Space2;

        public NavRail()
        {
            Width = 200;
            Dock = DockStyle.Left;
            BackColor = ThemeManager.Current.SurfaceContainer;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            MouseClick += OnMouseClick;
            ThemeManager.ThemeChanged += () => { BackColor = ThemeManager.Current.SurfaceContainer; Invalidate(); };

            _animTimer = new Timer { Interval = 15 };
            _animTimer.Tick += (s, e) => TickAnimation();
        }

        public void AddItem(string text, Control content)
        {
            _items.Add(new NavItem { Text = text, Content = content });
            if (_selectedIndex == -1)
            {
                _selectedIndex = 0;
                _pillY = _pillTargetY = ItemBoundsY(0);
            }
            Invalidate();
        }

        public int SelectedIndex => _selectedIndex;

        float ItemBoundsY(int index) => Md3Tokens.Space6 + index * ItemHeight;

        void OnMouseClick(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < _itemBounds.Count; i++)
            {
                if (_itemBounds[i].Contains(e.Location) && i != _selectedIndex)
                {
                    _selectedIndex = i;
                    _pillTargetY = ItemBoundsY(i);
                    _animStart = DateTime.Now;
                    _animTimer.Start();
                    SelectionChanged?.Invoke(i);
                    return;
                }
            }
        }

        void TickAnimation()
        {
            var elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            float t = (float)Math.Min(1.0, elapsed / AnimMs);
            // ease-out cubic — MD3's standard easing is closer to this
            // than a plain linear lerp, keeps the motion from feeling mechanical
            float eased = 1f - (float)Math.Pow(1f - t, 3);

            float startY = _pillY;
            // recompute from wherever we currently are toward the target
            // each tick, rather than storing a separate "start" snapshot —
            // simpler and still visually smooth at 15ms tick granularity
            _pillY += (_pillTargetY - _pillY) * eased * 0.5f;

            if (Math.Abs(_pillY - _pillTargetY) < 0.5f || t >= 1.0f)
            {
                _pillY = _pillTargetY;
                _animTimer.Stop();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _itemBounds.Clear();

            using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            // the animated pill, painted once behind whichever item it's
            // currently passing over/settled on
            var pillBounds = new Rectangle(ItemMarginX, (int)_pillY, Width - ItemMarginX * 2, ItemHeight - ItemMarginY);
            using (var path = PillPath(pillBounds))
            using (var brush = new SolidBrush(ThemeManager.Current.PrimaryContainer))
                e.Graphics.FillPath(brush, path);

            for (int i = 0; i < _items.Count; i++)
            {
                var bounds = new Rectangle(ItemMarginX, (int)ItemBoundsY(i), Width - ItemMarginX * 2, ItemHeight - ItemMarginY);
                _itemBounds.Add(bounds);

                bool selected = i == _selectedIndex;
                var textColor = selected ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurfaceVariant;
                // MD3 spec (component-type table): "Navigation label" ->
                // Label Medium. Was previously TitleMedium/BodyLarge — the
                // wrong scale entirely (that's card-title/body-text style,
                // not navigation).
                var font = selected ? Md3Tokens.LabelLarge : Md3Tokens.LabelMedium;
                TextRenderer.DrawText(e.Graphics, _items[i].Text, font, bounds, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        static GraphicsPath PillPath(Rectangle bounds)
        {
            var path = new GraphicsPath();
            int radius = bounds.Height / 2;
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
