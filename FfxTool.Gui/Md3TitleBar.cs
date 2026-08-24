using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// Custom title bar replacing the native Windows Aero chrome visible
    /// in every screenshot, for full MD3 immersion. This is the riskiest
    /// piece of this whole pass — a fully custom-chrome window needs to
    /// reimplement drag, resize, minimize, maximize, and close, which
    /// Windows normally gives you for free.
    ///
    /// Deliberately using the two well-established, low-risk WinForms
    /// patterns for this rather than anything deeper:
    ///   - Drag: ReleaseCapture + SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)
    ///     — tells Windows "treat this mouse-down as if it hit the title
    ///     bar," and lets the OS handle the actual drag. Standard, safe,
    ///     works the same on Win7 as anywhere else.
    ///   - Resize: WM_NCHITTEST override in MainForm's WndProc, returning
    ///     the appropriate HT edge/corner constant based on cursor
    ///     position — also standard, also lets the OS do the actual work.
    ///
    /// Explicitly NOT doing: DWM composition/blur effects, custom
    /// non-rectangular window shapes, or anything requiring
    /// per-Windows-version DWM API differences — those are the areas most
    /// likely to behave differently on Win7 specifically, which is exactly
    /// the kind of risk this whole project has been trying to avoid.
    /// </summary>
    public class Md3TitleBar : Panel
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HTCAPTION = 0x2;

        readonly Label _title;
        readonly Form _owner;
        readonly Button _maxBtn;
        Rectangle _restoreBounds;
        bool _isMaximized;

        /// <summary>True while the window is in the manual "maximized" state (see ToggleMaximize).</summary>
        public bool IsMaximized { get { return _isMaximized; } }

        // M3 Expressive Bold: 64 compact fixed height — Win7 safe, no DWM
        public Md3TitleBar(Form owner, string title)
        {
            _owner = owner;
            Dock = DockStyle.Top;
            Height = 64;
            BackColor = ThemeManager.Current.Surface; // glass approximated via alpha + border

            _title = new Label
            {
                Text = title, Font = Md3Tokens.TitleLarge, ForeColor = ThemeManager.Current.OnSurface,
                AutoSize = true, Location = new Point(Md3Tokens.Space6, 20),
            };
            Controls.Add(_title);

            // Glyphs from the widely-supported Geometric Shapes block so
            // they render on Win7's stock Segoe UI too ("▢" U+25A2 was a
            // tofu risk). Maximize toggles to the filled variant when the
            // window is in its manual-maximized state.
            var closeBtn = MakeChromeButton("✕", () => _owner.Close());
            _maxBtn = MakeChromeButton("□", ToggleMaximize);
            var minBtn = MakeChromeButton("—", () => _owner.WindowState = FormWindowState.Minimized);

            // right-to-left placement, standard Windows convention (close
            // rightmost) — positioned in Resize handler so it stays
            // correct if the bar is ever resized.
            void LayoutButtons()
            {
                int x = Width - Md3Tokens.Space2;
                foreach (var btn in new[] { closeBtn, _maxBtn, minBtn })
                {
                    x -= btn.Width;
                    btn.Location = new Point(x, (Height - btn.Height) / 2);
                    x -= Md3Tokens.Space1;
                }
            }
            Resize += (s, e) => LayoutButtons();

            Controls.Add(closeBtn);
            Controls.Add(_maxBtn);
            Controls.Add(minBtn);
            LayoutButtons();

            MouseDown += OnDragHandle;
            _title.MouseDown += OnDragHandle;
            DoubleClick += (s, e) => ToggleMaximize();
            _title.DoubleClick += (s, e) => ToggleMaximize();

            ThemeManager.ThemeChanged += () =>
            {
                BackColor = ThemeManager.Current.Surface;
                _title.ForeColor = ThemeManager.Current.OnSurface;
                Invalidate(true);
            };
            // stitch glass-pane bottom border 20% outline-variant
            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(51, ThemeManager.Current.OutlineVariant)))
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            };
        }

        void OnDragHandle(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // While manually "maximized" the OS sees a normal oversized
            // window — an HTCAPTION drag would haul the whole screen-sized
            // form around. Ignore drags in that state (double-click still
            // restores), matching native behavior closely enough.
            if (_isMaximized) return;
            ReleaseCapture();
            SendMessage(_owner.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        void ToggleMaximize()
        {
            // Deliberately not using FormWindowState.Maximized here — with
            // FormBorderStyle.None, Windows' native maximize commonly
            // covers the taskbar (a well-known WinForms gotcha, and
            // exactly the kind of thing worth avoiding given how many
            // subtle platform-specific issues this project has already
            // hit). Manually sizing to the screen's working area instead
            // sidesteps it entirely.
            if (_isMaximized)
            {
                _owner.Bounds = _restoreBounds;
                _isMaximized = false;
                _maxBtn.Text = "□";
            }
            else
            {
                _restoreBounds = _owner.Bounds;
                _owner.Bounds = Screen.FromControl(_owner).WorkingArea;
                _isMaximized = true;
                _maxBtn.Text = "▣"; // filled = restore state, like native chrome
            }
        }

        Button MakeChromeButton(string glyph, Action onClick)
        {
            var btn = new Button
            {
                Text = glyph, Width = 46, Height = 32, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f), ForeColor = ThemeManager.Current.OnSurfaceVariant,
                BackColor = ThemeManager.Current.Surface, Cursor = Cursors.Hand,
                TabStop = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ThemeManager.Current.SurfaceContainerHigh;
            btn.FlatAppearance.MouseDownBackColor = ThemeManager.Current.OutlineVariant;
            btn.Click += (s, e) => onClick();
            ThemeManager.ThemeChanged += () =>
            {
                btn.ForeColor = ThemeManager.Current.OnSurfaceVariant;
                btn.BackColor = ThemeManager.Current.Surface;
                btn.FlatAppearance.MouseOverBackColor = ThemeManager.Current.SurfaceContainerHigh;
                btn.FlatAppearance.MouseDownBackColor = ThemeManager.Current.OutlineVariant;
            };
            return btn;
        }
    }
}
