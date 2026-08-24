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
        public event Action FabClicked;

        // Spec: rail-width 88px per stitch code.html (overrides DESIGN.md 80px) — expressive uses 88 + FAB
        public const int RailWidth = 88;
        const int LogoAreaHeight = 128; // compact expressive: logo 32 + FAB 48 + gaps
        const int ItemHeight = 64; // compact for Win7, not 72
        const int PillSize = 48;   // compact pill, not 56
        Rectangle _fabBounds;

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

            using (var pen = new Pen(Color.FromArgb(77, ThemeManager.Current.OutlineVariant.R, ThemeManager.Current.OutlineVariant.G, ThemeManager.Current.OutlineVariant.B)))
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            // brand mark — larger expressive 32px with pill bg
            int logoSize = 32;
            var logoBg = new Rectangle((Width - 48) / 2, Md3Tokens.Space4, 48, 48);
            using (var path = PillPath(new Rectangle(logoBg.X, logoBg.Y, 48, 48)))
            using (var brush = new SolidBrush(Color.FromArgb(20, ThemeManager.Current.Primary.R, ThemeManager.Current.Primary.G, ThemeManager.Current.Primary.B)))
                e.Graphics.FillPath(brush, path);
            var logoBounds = new Rectangle((Width - logoSize) / 2, logoBg.Y + (48 - logoSize) / 2, logoSize, logoSize);
            Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Logo, logoBounds, ThemeManager.Current.Primary, 2.0f);

            // FAB — stitch expressive w-full aspect-square bg-primary-container rounded-2xl below logo (real SVG direct)
            var fabBounds = new Rectangle((Width - 48) / 2, logoBg.Bottom + Md3Tokens.Space3, 48, 48);
            _fabBounds = fabBounds;
            using (var path = RoundedRect(fabBounds, Md3Tokens.CornerLargeIncreased))
            using (var brush = new SolidBrush(ThemeManager.Current.PrimaryContainer))
                e.Graphics.FillPath(brush, path);
            Md3Icons.Draw(e.Graphics, Md3Icons.Icon.Add, new Rectangle(fabBounds.X + 12, fabBounds.Y + 12, 24, 24), ThemeManager.Current.OnPrimaryContainer, 1.8f);

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
