using System.Drawing;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    public class MainForm : Form
    {
        readonly PluginProfile _profile;
        readonly ListerTab _listerTab;
        readonly ProfileTab _profileTab;
        readonly ConvertTab _convertTab;
        readonly SettingsTab _settingsTab;
        readonly Panel _contentHost;
        readonly NavRail _navRail;
        readonly TableLayoutPanel _root;
        readonly TableLayoutPanel _body;
        readonly Md3TitleBar _titleBar;

        // resize-border thickness for the WM_NCHITTEST override below —
        // wide enough to grab comfortably with a mouse, matching roughly
        // what native Windows borders feel like.
        const int ResizeBorder = 6;

        public MainForm()
        {
            ThemeManager.Load(); // must happen before any control reads ThemeManager.Current

            // Custom chrome: no native title bar/border at all — Md3TitleBar
            // (below) replaces minimize/maximize/close/drag, and the
            // WndProc override replaces resize-by-edge-drag. See
            // Md3TitleBar.cs for why these specific techniques were chosen
            // over anything deeper/riskier.
            FormBorderStyle = FormBorderStyle.None;

            Text = "FFX Compatibility Tool";
            MinimumSize = new Size(820, 560);
            Size = new Size(1000, 680);
            BackColor = ThemeManager.Current.Surface;
            Font = Md3Tokens.BodyLarge;

            _profile = PluginProfile.Load();

            _listerTab = new ListerTab(_profile) { Dock = DockStyle.Fill, Visible = false };
            _profileTab = new ProfileTab(_profile, OnProfileChanged) { Dock = DockStyle.Fill, Visible = false };
            _convertTab = new ConvertTab(_profile) { Dock = DockStyle.Fill, Visible = false };
            _settingsTab = new SettingsTab { Dock = DockStyle.Fill, Visible = false };

            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Current.Surface, Padding = new Padding(Md3Tokens.Space6) };
            _contentHost.Controls.Add(_settingsTab);
            _contentHost.Controls.Add(_convertTab);
            _contentHost.Controls.Add(_profileTab);
            _contentHost.Controls.Add(_listerTab);

            _navRail = new NavRail();
            _navRail.AddItem("Effect Lister", _listerTab, Md3Icons.Icon.EffectList);
            _navRail.AddItem("Plugin Profile", _profileTab, Md3Icons.Icon.Plugin);
            _navRail.AddItem("Convert", _convertTab, Md3Icons.Icon.Convert);
            _navRail.AddItem("Settings", _settingsTab, Md3Icons.Icon.Settings, pinned: true);
            _navRail.SelectionChanged += OnNavSelectionChanged;

            _titleBar = new Md3TitleBar(this, "FFX Compatibility Tool");

            _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavRail.RailWidth));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.Controls.Add(_navRail, 0, 0);
            _body.Controls.Add(_contentHost, 1, 0);
            _navRail.Dock = DockStyle.Fill;

            _root.Controls.Add(_titleBar, 0, 0);
            _root.Controls.Add(_body, 0, 1);

            Controls.Add(_root);

            ShowTab(0);

            // Re-theme the entire open window the moment the user changes
            // mode/palette in Settings — no restart required.
            ThemeManager.ThemeChanged += () =>
            {
                BackColor = ThemeManager.Current.Surface;
                _contentHost.BackColor = ThemeManager.Current.Surface;
                ThemeManager.ApplyToTree(this);
            };
        }

        void OnNavSelectionChanged(int index) => ShowTab(index);

        void ShowTab(int index)
        {
            _listerTab.Visible = index == 0;
            _profileTab.Visible = index == 1;
            _convertTab.Visible = index == 2;
            _settingsTab.Visible = index == 3;
        }

        void OnProfileChanged()
        {
            _listerTab.Refresh_();
            _convertTab.Refresh_();
        }

        // --- resize-by-edge-drag, the other half of the custom-chrome
        // implementation (drag-to-move lives in Md3TitleBar). Standard
        // WM_NCHITTEST override: tell Windows which edge/corner the
        // cursor is over so it can handle the actual resize natively —
        // same "let the OS do the real work" approach as the title bar's
        // drag handling, not a from-scratch resize implementation.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                var screenPoint = new Point(m.LParam.ToInt32());
                var clientPoint = PointToClient(screenPoint);

                bool left = clientPoint.X <= ResizeBorder;
                bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
                bool top = clientPoint.Y <= ResizeBorder;
                bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;

                if (top && left) m.Result = (System.IntPtr)13;      // HTTOPLEFT
                else if (top && right) m.Result = (System.IntPtr)14; // HTTOPRIGHT
                else if (bottom && left) m.Result = (System.IntPtr)16; // HTBOTTOMLEFT
                else if (bottom && right) m.Result = (System.IntPtr)17; // HTBOTTOMRIGHT
                else if (left) m.Result = (System.IntPtr)10;   // HTLEFT
                else if (right) m.Result = (System.IntPtr)11;  // HTRIGHT
                else if (top) m.Result = (System.IntPtr)12;    // HTTOP
                else if (bottom) m.Result = (System.IntPtr)15; // HTBOTTOM
                return;
            }
            base.WndProc(ref m);
        }
    }
}
