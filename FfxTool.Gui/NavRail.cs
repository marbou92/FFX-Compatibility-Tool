using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// MD3 navigation rail, rebuilt to match the user's real design spec
    /// exactly: fixed 80px width ("Navigation Rail: A slim 80px vertical
    /// bar that remains fixed"), icon centered above a small label
    /// (Label Small, 11px/500), a brand mark at the top, and a pill-shaped
    /// active indicator behind just the icon.
    ///
    /// This REPLACES the earlier toggleable expand/collapse version built
    /// from the user's own rough sketch — the real spec supersedes that
    /// interpretation. Fixed-width, not collapsible, per the actual design.
    /// </summary>
    public class NavRail : Panel
    {
        public class NavItem
        {
            public string Text;
            public Md3Icons.Icon Icon;
            public bool Pinned; // rendered in the bottom group, separated by a gap
        }

        readonly List<NavItem> _items = new List<NavItem>();
        readonly List<Rectangle> _itemBounds = new List<Rectangle>();
        int _selectedIndex = -1;
        int _hoverIndex = -1;   // hovered nav item (-1 = none)
        bool _fabHover, _fabPressed;

        readonly Timer _animTimer;
        float _pillY;                 // current drawn pill position
        float _animFromY, _animToY;   // interpolation endpoints
        DateTime _animStart;
        const int AnimMs = Md3Tokens.MotionDurationMs;
        // ease-out-back: fast start, gentle spring overshoot at the end —
        // the M3 Expressive "springy" feel, subtle enough for a desktop tool
        const float Overshoot = 1.35f;

        public event Action<int> SelectionChanged;
        public event Action FabClicked;

        // Spec: rail-width 88px per stitch code.html (overrides DESIGN.md 80px) — expressive uses 88 + FAB
        public const int RailWidth = 88;
        const int LogoAreaHeight = 136; // expressive: logo 48 + gap 12 + FAB 56 + gap
        const int ItemHeight = Md3Tokens.NavItemHeight;
        const int PillSize = Md3Tokens.PillSize;
        const int FabSize = Md3Tokens.FabSize;
        Rectangle _fabBounds;

        public NavRail()
        {
            Width = RailWidth;
            Dock = DockStyle.Left;
            BackColor = ThemeManager.Current.NavigationSurface;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            MouseClick += OnMouseClick;
            MouseMove += OnMouseMove;
            MouseDown += (s, e) => { if (_fabBounds.Contains(e.Location)) { _fabPressed = true; Invalidate(); } };
            MouseUp += (s, e) => { if (_fabPressed) { _fabPressed = false; Invalidate(); } };
            MouseLeave += (s, e) => { if (_hoverIndex != -1 || _fabHover) { _hoverIndex = -1; _fabHover = false; Invalidate(); } };
            ThemeManager.ThemeChanged += () => { BackColor = ThemeManager.Current.NavigationSurface; Invalidate(); };

            _animTimer = new Timer { Interval = 15 };
            _animTimer.Tick += (s, e) => TickAnimation();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void AddItem(string text, Control content, Md3Icons.Icon icon, bool pinned = false)
        {
            _items.Add(new NavItem { Text = text, Icon = icon, Pinned = pinned });
            if (_selectedIndex == -1)
            {
                _selectedIndex = 0;
                _pillY = _animFromY = _animToY = ItemBoundsY(0);
            }
            Invalidate();
        }

        public int SelectedIndex => _selectedIndex;

        float ItemBoundsY(int index)
        {
            var mainItems = _items.FindAll(i => !i.Pinned);
            var pinnedItems = _items.FindAll(i => i.Pinned);

            if (!_items[index].Pinned)
            {
                int mainIdx = mainItems.IndexOf(_items[index]);
                return LogoAreaHeight + mainIdx * ItemHeight;
            }
            else
            {
                int pinnedIdx = pinnedItems.IndexOf(_items[index]);
                int fromBottom = pinnedItems.Count - pinnedIdx;
                return Height - fromBottom * ItemHeight - Md3Tokens.Space4;
            }
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            bool overFab = _fabBounds.Contains(e.Location);
            int overItem = -1;
            if (!overFab)
                for (int i = 0; i < _itemBounds.Count; i++)
                    if (_itemBounds[i].Contains(e.Location)) { overItem = i; break; }

            if (overItem != _hoverIndex || overFab != _fabHover)
            {
                _hoverIndex = overItem;
                _fabHover = overFab;
                Invalidate();
            }
        }

        void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (_fabBounds.Contains(e.Location))
            {
                FabClicked?.Invoke();
                return;
            }
            for (int i = 0; i < _itemBounds.Count; i++)
            {
                if (_itemBounds[i].Contains(e.Location) && i != _selectedIndex)
                {
                    _selectedIndex = i;
                    _animFromY = _pillY;
                    _animToY = ItemBoundsY(i);
                    _animStart = DateTime.Now;
                    _animTimer.Start();
                    SelectionChanged?.Invoke(i);
                    return;
                }
            }
        }

        void TickAnimation()
        {
            double elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            float t = (float)Math.Min(1.0, elapsed / AnimMs);
            // ease-out-back with a gentle single overshoot
            float u = t - 1f;
            float eased = 1f + (Overshoot + 1f) * u * u * u + Overshoot * u * u;
            _pillY = _animFromY + (_animToY - _animFromY) * eased;
            if (t >= 1.0f)
            {
                _pillY = _animToY;
                _animTimer.Stop();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _itemBounds.Clear();

            using (var pen = new Pen(Color.FromArgb(77, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            // brand mark — expressive 48px tonal square with 32px logo
            int logoSize = 32;
            var logoBg = new Rectangle((Width - 48) / 2, Md3Tokens.Space4, 48, 48);
            using (var path = RoundedRect(new Rectangle(logoBg.X, logoBg.Y, 48, 48), Md3Tokens.CornerMedium))
            using (var brush = new SolidBrush(Color.FromArgb(20, ThemeManager.Current.Primary.R, ThemeManager.Current.Primary.G, ThemeManager.Current.Primary.B)))
                e.Graphics.FillPath(brush, path);
            var logoBounds = new Rectangle((Width - logoSize) / 2, logoBg.Y + (48 - logoSize) / 2, logoSize, logoSize);
            Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Logo, logoBounds, ThemeManager.Current.Primary, 2.0f);

            // FAB — expressive large (56px), hover state-layer + press scale
            int fabSize = _fabPressed ? (int)(FabSize * 0.94f) : FabSize;
            int fabX = (Width - fabSize) / 2;
            int fabY = logoBg.Bottom + Md3Tokens.Space3 + (FabSize - fabSize) / 2;
            var fabBounds = new Rectangle(fabX, fabY, fabSize, fabSize);
            _fabBounds = new Rectangle((Width - FabSize) / 2, logoBg.Bottom + Md3Tokens.Space3, FabSize, FabSize); // stable hit target
            using (var path = RoundedRect(fabBounds, Md3Tokens.CornerLargeIncreased))
            using (var brush = new SolidBrush(ThemeManager.Current.PrimaryContainer))
                e.Graphics.FillPath(brush, path);
            if (_fabHover && !_fabPressed)
                using (var path = RoundedRect(fabBounds, Md3Tokens.CornerLargeIncreased))
                    Md3StateLayer.Paint(e.Graphics, path, ThemeManager.Current.OnPrimaryContainer, Md3Tokens.HoverStateAlpha);
            int fabIcon = (int)(fabSize * (24f / FabSize));
            Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Add, new Rectangle(fabBounds.X + (fabSize - fabIcon) / 2, fabBounds.Y + (fabSize - fabIcon) / 2, fabIcon, fabIcon), ThemeManager.Current.OnPrimaryContainer, 1.8f);

            using (var pen = new Pen(Color.FromArgb(60, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                e.Graphics.DrawLine(pen, Md3Tokens.Space4, LogoAreaHeight - Md3Tokens.Space2, Width - Md3Tokens.Space4, LogoAreaHeight - Md3Tokens.Space2);

            int iconSize = 24;
            bool sawPinnedGap = false;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                bool selected = i == _selectedIndex;

                if (item.Pinned && !sawPinnedGap)
                {
                    sawPinnedGap = true;
                    float gapY = ItemBoundsY(i) - Md3Tokens.Space2;
                    using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                        e.Graphics.DrawLine(pen, Md3Tokens.Space4, gapY, Width - Md3Tokens.Space4, gapY);
                }

                var bounds = new Rectangle(0, (int)ItemBoundsY(i), Width, ItemHeight - Md3Tokens.Space2);
                _itemBounds.Add(bounds);

                var itemColor = selected ? ThemeManager.Current.Primary : ThemeManager.Current.OnSurfaceVariant;

                // hover state-layer on unselected items (M3 interaction states)
                if (i == _hoverIndex && !selected)
                {
                    var hoverBounds = new Rectangle((Width - PillSize) / 2, bounds.Y + Md3Tokens.Space1, PillSize, PillSize);
                    using (var path = PillPath(hoverBounds))
                        Md3StateLayer.Paint(e.Graphics, path, ThemeManager.Current.OnSurfaceVariant, Md3Tokens.HoverStateAlpha);
                }

                // active indicator pill behind just the icon (spec), spring-animated
                if (selected)
                {
                    var pillBounds = new Rectangle((Width - PillSize) / 2, (int)_pillY + Md3Tokens.Space1, PillSize, PillSize);
                    using (var path = PillPath(pillBounds))
                    using (var brush = new SolidBrush(ThemeManager.Current.PrimaryContainer))
                        e.Graphics.FillPath(brush, path);
                }

                var iconBounds = new Rectangle((Width - iconSize) / 2, bounds.Y + Md3Tokens.Space2 + (PillSize - iconSize) / 2, iconSize, iconSize);
                Md3Icons.Draw(e.Graphics, item.Icon, iconBounds, itemColor, selected ? 2.0f : 1.6f);

                var labelBounds = new Rectangle(0, iconBounds.Bottom + Md3Tokens.Space1, Width, 16);
                // spec: nav labels are Label Small (11px) in both states —
                // selection is signaled by the pill + icon color, not size/weight
                TextRenderer.DrawText(e.Graphics, item.Text, Md3Tokens.LabelSmall, labelBounds, itemColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
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

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
