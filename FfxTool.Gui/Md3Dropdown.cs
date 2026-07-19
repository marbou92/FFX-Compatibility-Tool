using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// A real MD3 "outlined dropdown menu" (m3.material.io/components/menus#dropdown-menus),
    /// built from scratch instead of skinning a native ComboBox.
    ///
    /// Why: WinForms' ComboBox renders its closed-box chrome (including
    /// the dropdown arrow button) through the OS's native composite
    /// rendering, not through a normal OnPaint call you can fully
    /// override — that's why the previous Md3ComboBox attempt still
    /// showed a native arrow glyph poking through no matter how much was
    /// painted over it (confirmed visually in a real screenshot, not a
    /// guess). A ComboBox literally cannot be made to look 100% custom
    /// without deep Win32 message interception, which is a lot of
    /// fragile code for one dropdown.
    ///
    /// This control sidesteps the problem entirely: it's just a themed
    /// clickable box that opens a small borderless popup Form with a
    /// custom-painted option list. Nothing native to fight.
    /// </summary>
    public class Md3Dropdown : Control
    {
        readonly List<string> _items = new List<string>();
        int _selectedIndex = -1;
        Form _popup;

        public event Action SelectionChanged;

        public Md3Dropdown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Font = Md3Tokens.BodyLarge;
            Height = 36;
            Cursor = Cursors.Hand;
            Click += (s, e) => TogglePopup();
            ThemeManager.ThemeChanged += Invalidate_;
        }

        void Invalidate_() => Invalidate();

        public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
        {
            _items.Clear();
            _items.AddRange(items);
            _selectedIndex = _items.Count > 0 ? Math.Min(selectedIndex, _items.Count - 1) : -1;
            Invalidate();
        }

        public string SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { _selectedIndex = value; Invalidate(); }
        }

        void TogglePopup()
        {
            if (_popup != null) { ClosePopup(); return; }
            if (_items.Count == 0) return;

            _popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Size = new Size(Width, Math.Min(_items.Count, 8) * 32 + 4),
                BackColor = ThemeManager.Current.Surface,
            };
            var screenLoc = Parent?.PointToScreen(Location) ?? Location;
            _popup.Location = new Point(screenLoc.X, screenLoc.Y + Height + 2);

            var list = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var row = new Panel { Height = 32, Dock = DockStyle.Top, Cursor = Cursors.Hand };
                bool selected() => idx == _selectedIndex;
                row.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var bg = selected() ? ThemeManager.Current.PrimaryContainer : ThemeManager.Current.Surface;
                    var fg = selected() ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurface;
                    using (var brush = new SolidBrush(bg))
                        e.Graphics.FillRectangle(brush, row.ClientRectangle);
                    var textRect = new Rectangle(Md3Tokens.Space3, 0, row.Width - Md3Tokens.Space3 * 2, row.Height);
                    TextRenderer.DrawText(e.Graphics, _items[idx], Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                };
                row.Click += (s, e) =>
                {
                    _selectedIndex = idx;
                    ClosePopup();
                    Invalidate();
                    SelectionChanged?.Invoke();
                };
                list.Controls.Add(row);
            }
            // Panel.Controls.Add with Dock.Top stacks in reverse insertion
            // order — reverse the visual order back to match _items order.
            for (int i = list.Controls.Count - 1; i >= 0; i--)
                list.Controls.SetChildIndex(list.Controls[i], list.Controls.Count - 1 - i);

            _popup.Controls.Add(list);
            _popup.Deactivate += (s, e) => ClosePopup();
            _popup.Show(FindForm());
        }

        void ClosePopup()
        {
            _popup?.Close();
            _popup?.Dispose();
            _popup = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent is Md3Card || Parent?.GetType().Name.Contains("Card") == true
                ? ThemeManager.Current.SurfaceContainer
                : ThemeManager.Current.Surface);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            // MD3 spec: dropdown menu box = "small" (8px) corner.
            using (var path = RoundedRect(bounds, Md3Tokens.CornerSmall))
            using (var fillBrush = new SolidBrush(ThemeManager.Current.Surface))
            using (var pen = new Pen(ThemeManager.Current.Outline, 1f))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(pen, path);
            }

            if (SelectedItem != null)
            {
                var textRect = new Rectangle(Md3Tokens.Space3, 0, Width - Md3Tokens.Space6 - 16, Height);
                TextRenderer.DrawText(g, SelectedItem, Font, textRect, ThemeManager.Current.OnSurface,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }

            var caretCenter = new Point(Width - 16, Height / 2);
            using (var pen = new Pen(ThemeManager.Current.OnSurfaceVariant, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, caretCenter.X - 4, caretCenter.Y - 2, caretCenter.X, caretCenter.Y + 2);
                g.DrawLine(pen, caretCenter.X, caretCenter.Y + 2, caretCenter.X + 4, caretCenter.Y - 2);
            }
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
