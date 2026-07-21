using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// MD3 navigation rail, now supporting both of MD3's real states:
    /// expanded (icon + label, m3.material.io/components/navigation-rail)
    /// and collapsed (icon-only, same spec — this isn't a deviation, MD3
    /// explicitly defines both as valid states of the same component).
    ///
    /// Also splits items into a main group and a "pinned" group (Settings)
    /// rendered at the bottom with a visual gap, per the layout sketch
    /// this was built from.
    /// </summary>
    public class NavRail : Panel
    {
        public class NavItem
        {
            public string Text;
            public Control Content;
            public Md3Icons.Icon Icon;
            public bool Pinned; // rendered in the bottom group, separated by a gap
        }

        readonly List<NavItem> _items = new List<NavItem>();
        readonly List<Rectangle> _itemBounds = new List<Rectangle>();
        int _selectedIndex = -1;
        bool _isCollapsed;

        readonly ToolTip _tooltip = new ToolTip { InitialDelay = 400, ShowAlways = true };
        int _hoveredIndex = -1;

        readonly Timer _animTimer;
        float _pillY, _pillTargetY;
        DateTime _animStart;
        const int AnimMs = 150;

        public event Action<int> SelectionChanged;
        public event Action CollapsedChanged;

        const int ItemHeight = 48;
        const int ItemMarginX = Md3Tokens.Space3;
        const int ToggleButtonHeight = 40;
        public const int ExpandedWidth = 200;
        public const int CollapsedWidth = 72;

        public bool IsCollapsed => _isCollapsed;
        public int TargetWidth => _isCollapsed ? CollapsedWidth : ExpandedWidth;

        public NavRail()
        {
            _isCollapsed = NavRailPrefs.LoadCollapsed();
            Width = TargetWidth;
            Dock = DockStyle.Left;
            BackColor = ThemeManager.Current.SurfaceContainerLow;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            MouseClick += OnMouseClick;
            MouseMove += OnMouseMove;
            MouseLeave += (s, e) => { _hoveredIndex = -1; };
            ThemeManager.ThemeChanged += () => { BackColor = ThemeManager.Current.SurfaceContainerLow; Invalidate(); };

            _animTimer = new Timer { Interval = 15 };
            _animTimer.Tick += (s, e) => TickAnimation();
        }

        public void AddItem(string text, Control content, Md3Icons.Icon icon, bool pinned = false)
        {
            _items.Add(new NavItem { Text = text, Content = content, Icon = icon, Pinned = pinned });
            if (_selectedIndex == -1)
            {
                _selectedIndex = 0;
                _pillY = _pillTargetY = ItemBoundsY(0);
            }
            Invalidate();
        }

        public int SelectedIndex => _selectedIndex;

        float ItemBoundsY(int index)
        {
            // main-group items stack from the top (below the toggle button);
            // pinned items stack from the bottom, in insertion order, with
            // a gap separating them from the main group.
            var mainItems = _items.FindAll(i => !i.Pinned);
            var pinnedItems = _items.FindAll(i => i.Pinned);

            if (!_items[index].Pinned)
            {
                int mainIdx = mainItems.IndexOf(_items[index]);
                return ToggleButtonHeight + Md3Tokens.Space4 + mainIdx * ItemHeight;
            }
            else
            {
                int pinnedIdx = pinnedItems.IndexOf(_items[index]);
                int fromBottom = pinnedItems.Count - pinnedIdx;
                return Height - fromBottom * ItemHeight - Md3Tokens.Space4;
            }
        }

        void OnMouseClick(object sender, MouseEventArgs e)
        {
            var toggleBounds = new Rectangle(0, 0, Width, ToggleButtonHeight);
            if (toggleBounds.Contains(e.Location))
            {
                _isCollapsed = !_isCollapsed;
                NavRailPrefs.SaveCollapsed(_isCollapsed);
                Width = TargetWidth;
                Invalidate();
                CollapsedChanged?.Invoke();
                return;
            }

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

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isCollapsed) { _hoveredIndex = -1; return; } // tooltips only needed when labels are hidden
            for (int i = 0; i < _itemBounds.Count; i++)
            {
                if (_itemBounds[i].Contains(e.Location))
                {
                    if (_hoveredIndex != i)
                    {
                        _hoveredIndex = i;
                        _tooltip.SetToolTip(this, _items[i].Text);
                    }
                    return;
                }
            }
            _hoveredIndex = -1;
        }

        void TickAnimation()
        {
            var elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            float t = (float)Math.Min(1.0, elapsed / AnimMs);
            float eased = 1f - (float)Math.Pow(1f - t, 3);
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

            // collapse/expand toggle — simple 3-line "menu" glyph, drawn
            // inline rather than adding a dedicated Md3Icons entry for
            // one-off chrome (kept the icon set focused on nav/action icons)
            var toggleBounds = new Rectangle(0, 0, Width, ToggleButtonHeight);
            using (var pen = new Pen(ThemeManager.Current.OnSurfaceVariant, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int cx = Md3Tokens.Space4 + 8;
                int cy = ToggleButtonHeight / 2;
                for (int i = -1; i <= 1; i++)
                    e.Graphics.DrawLine(pen, cx - 8, cy + i * 5, cx + 8, cy + i * 5);
            }

            int iconSize = 20;
            const int iconTextGap = Md3Tokens.Space2;

            bool sawPinnedGap = false;
            foreach (var item in _items)
            {
                int i = _items.IndexOf(item);
                bool selected = i == _selectedIndex;

                if (item.Pinned && !sawPinnedGap)
                {
                    sawPinnedGap = true;
                    float gapY = ItemBoundsY(i) - Md3Tokens.Space4;
                    using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                        e.Graphics.DrawLine(pen, ItemMarginX, gapY, Width - ItemMarginX, gapY);
                }

                var bounds = new Rectangle(ItemMarginX, (int)ItemBoundsY(i), Width - ItemMarginX * 2, ItemHeight - Md3Tokens.Space2);
                _itemBounds.Add(bounds);

                var itemColor = selected ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurfaceVariant;

                if (selected)
                {
                    var pillBounds = _isCollapsed
                        ? new Rectangle(bounds.X + (bounds.Width - bounds.Height) / 2, (int)_pillY, bounds.Height, bounds.Height - Md3Tokens.Space2)
                        : new Rectangle(bounds.X, (int)_pillY, bounds.Width, bounds.Height - Md3Tokens.Space2);
                    using (var path = PillPath(pillBounds))
                    using (var brush = new SolidBrush(ThemeManager.Current.PrimaryContainer))
                        e.Graphics.FillPath(brush, path);
                }

                if (_isCollapsed)
                {
                    var iconBounds = new Rectangle(bounds.X + (bounds.Width - iconSize) / 2, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
                    Md3Icons.Draw(e.Graphics, item.Icon, iconBounds, itemColor, selected ? 2.0f : 1.6f);
                }
                else
                {
                    var iconBounds = new Rectangle(bounds.X + Md3Tokens.Space2, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
                    Md3Icons.Draw(e.Graphics, item.Icon, iconBounds, itemColor, selected ? 2.0f : 1.6f);
                    var textBounds = new Rectangle(iconBounds.Right + iconTextGap, bounds.Y, bounds.Width - (iconBounds.Right + iconTextGap - bounds.X), bounds.Height);
                    var font = selected ? Md3Tokens.LabelLarge : Md3Tokens.LabelMedium;
                    TextRenderer.DrawText(e.Graphics, item.Text, font, textBounds, itemColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
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
