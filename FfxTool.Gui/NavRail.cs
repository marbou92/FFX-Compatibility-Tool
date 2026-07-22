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
            public Control Content;
            public Md3Icons.Icon Icon;
            public bool Pinned; // rendered in the bottom group, separated by a gap
        }

        readonly List<NavItem> _items = new List<NavItem>();
        readonly List<Rectangle> _itemBounds = new List<Rectangle>();
        int _selectedIndex = -1;

        readonly Timer _animTimer;
        float _pillY, _pillTargetY;
        DateTime _animStart;
        const int AnimMs = 150;

        public event Action<int> SelectionChanged;

        // Spec: rail-width: 80px (fixed, not collapsible)
        public const int RailWidth = 80;
        const int LogoAreaHeight = 72;
        const int ItemHeight = 64; // icon + gap + label needs more vertical room than the old icon+label-beside layout
        const int PillSize = 44;   // spec: pill background sized to the icon, not the full item width

        public NavRail()
        {
            Width = RailWidth;
            Dock = DockStyle.Left;
            BackColor = ThemeManager.Current.NavigationSurface;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            MouseClick += OnMouseClick;
            ThemeManager.ThemeChanged += () => { BackColor = ThemeManager.Current.NavigationSurface; Invalidate(); };

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

            // brand mark, top of rail
            int logoSize = 28;
            var logoBounds = new Rectangle((Width - logoSize) / 2, Md3Tokens.Space4, logoSize, logoSize);
            Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Logo, logoBounds, ThemeManager.Current.Primary, 1.8f);
            using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
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

                // pill sized to just the icon (spec: "Active State is
                // indicated by a Pill background behind the icon"), not
                // stretched to the item's full width — a real difference
                // from the earlier expanded-rail version, which used a
                // full-width pill since it had a label sitting beside the
                // icon rather than below it.
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
                var font = selected ? Md3Tokens.LabelSmall : Md3Tokens.LabelSmall; // spec: nav labels are Label Small (11px), not Medium — both states share the size, weight differs via the color/emphasis only
                TextRenderer.DrawText(e.Graphics, item.Text, font, labelBounds, itemColor,
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
    }
}
