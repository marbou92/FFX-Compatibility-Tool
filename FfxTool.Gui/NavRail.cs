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
    /// Win7's default theme). This is the single biggest visual upgrade
    /// over the tab-based v1 layout.
    /// </summary>
    public class NavRail : Panel
    {
        public class NavItem
        {
            public string Text;
            public Control Content; // the tab's content control, shown when this item is selected
        }

        readonly List<NavItem> _items = new List<NavItem>();
        readonly List<Rectangle> _itemBounds = new List<Rectangle>();
        int _selectedIndex = -1;

        public event Action<int> SelectionChanged;

        const int ItemHeight = 48;
        const int ItemMarginX = Md3Tokens.Space3;
        const int ItemMarginY = Md3Tokens.Space2;

        public NavRail()
        {
            Width = 200;
            Dock = DockStyle.Left;
            BackColor = Md3Tokens.SurfaceContainer;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            MouseClick += OnMouseClick;
        }

        public void AddItem(string text, Control content)
        {
            _items.Add(new NavItem { Text = text, Content = content });
            if (_selectedIndex == -1) _selectedIndex = 0;
            Invalidate();
        }

        public int SelectedIndex => _selectedIndex;

        void OnMouseClick(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < _itemBounds.Count; i++)
            {
                if (_itemBounds[i].Contains(e.Location) && i != _selectedIndex)
                {
                    _selectedIndex = i;
                    Invalidate();
                    SelectionChanged?.Invoke(i);
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _itemBounds.Clear();

            // subtle divider against the content area, MD3 outline-variant tone
            using (var pen = new Pen(Md3Tokens.OutlineVariant))
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            int y = Md3Tokens.Space6;
            for (int i = 0; i < _items.Count; i++)
            {
                var bounds = new Rectangle(ItemMarginX, y, Width - ItemMarginX * 2, ItemHeight - ItemMarginY);
                _itemBounds.Add(bounds);

                bool selected = i == _selectedIndex;
                if (selected)
                {
                    using (var path = PillPath(bounds))
                    using (var brush = new SolidBrush(Md3Tokens.PrimaryContainer))
                        e.Graphics.FillPath(brush, path);
                }

                var textColor = selected ? Md3Tokens.OnPrimaryContainer : Md3Tokens.OnSurfaceVariant;
                var font = selected ? Md3Tokens.TitleMedium : Md3Tokens.BodyLarge;
                TextRenderer.DrawText(e.Graphics, _items[i].Text, font, bounds, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                y += ItemHeight;
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
