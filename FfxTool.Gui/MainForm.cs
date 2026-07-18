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
        readonly Panel _contentHost;
        readonly NavRail _navRail;

        public MainForm()
        {
            Text = "FFX Compatibility Tool";
            MinimumSize = new Size(820, 560);
            Size = new Size(1000, 680);
            BackColor = Md3Tokens.Surface;
            Font = Md3Tokens.BodyLarge;

            _profile = PluginProfile.Load();

            _listerTab = new ListerTab(_profile) { Dock = DockStyle.Fill, Visible = false };
            _profileTab = new ProfileTab(_profile, OnProfileChanged) { Dock = DockStyle.Fill, Visible = false };
            _convertTab = new ConvertTab(_profile) { Dock = DockStyle.Fill, Visible = false };

            // The content host swaps which tab's control is visible —
            // replaces TabControl's built-in page-switching, since the
            // nav rail is now driving selection instead of native tabs.
            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Md3Tokens.Surface, Padding = new Padding(Md3Tokens.Space6) };
            _contentHost.Controls.Add(_convertTab);
            _contentHost.Controls.Add(_profileTab);
            _contentHost.Controls.Add(_listerTab);

            _navRail = new NavRail();
            _navRail.AddItem("Effect Lister", _listerTab);
            _navRail.AddItem("Plugin Profile", _profileTab);
            _navRail.AddItem("Convert", _convertTab);
            _navRail.SelectionChanged += OnNavSelectionChanged;

            var header = new AppHeader("FFX Compatibility Tool");

            // Layout: header spans the top; below it, nav rail (left) +
            // content host (fill) split the remaining space. A
            // TableLayoutPanel here instead of manual Dock ordering makes
            // the header-then-body split explicit and immune to Z-order
            // bugs (Dock ordering is a classic WinForms footgun — the
            // last-added Dock.Top control ends up visually first, which
            // is unintuitive and easy to get backwards).
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(_navRail, 0, 0);
            body.Controls.Add(_contentHost, 1, 0);
            _navRail.Dock = DockStyle.Fill; // fill its TableLayoutPanel cell instead of using Dock.Left directly on the form

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(body, 0, 1);

            Controls.Add(root);

            ShowTab(0);
        }

        void OnNavSelectionChanged(int index) => ShowTab(index);

        void ShowTab(int index)
        {
            _listerTab.Visible = index == 0;
            _profileTab.Visible = index == 1;
            _convertTab.Visible = index == 2;
        }

        void OnProfileChanged()
        {
            // Both other tabs read from the same PluginProfile instance,
            // so a profile edit should immediately affect how they flag
            // effects — same wiring as before.
            _listerTab.Refresh_();
            _convertTab.Refresh_();
        }
    }
}
