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
        AppHeader _header;

        public MainForm()
        {
            ThemeManager.Load(); // must happen before any control reads ThemeManager.Current

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
            _navRail.AddItem("Settings", _settingsTab, Md3Icons.Icon.Settings);
            _navRail.SelectionChanged += OnNavSelectionChanged;

            _header = new AppHeader("FFX Compatibility Tool");

            _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(_navRail, 0, 0);
            body.Controls.Add(_contentHost, 1, 0);
            _navRail.Dock = DockStyle.Fill;

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(body, 0, 1);

            Controls.Add(_root);

            ShowTab(0);

            // Re-theme the entire open window the moment the user changes
            // mode/palette in Settings — no restart required. Custom-
            // painted controls (Md3Button, NavRail, Md3Switch, etc.)
            // already read ThemeManager.Current fresh on every OnPaint, so
            // Invalidate(true) is enough for those; ApplyToTree additionally
            // pushes fresh colors onto native controls (ListView, TextBox,
            // Panel, Label) that hold a static color property instead.
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
    }
}
