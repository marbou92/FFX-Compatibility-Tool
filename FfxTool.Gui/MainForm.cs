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

        public MainForm()
        {
            Text = "FFX Compatibility Tool";
            Size = new Size(900, 650);
            BackColor = Md3Tokens.Surface;
            Font = Md3Tokens.BodyLarge;

            _profile = PluginProfile.Load();

            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Md3Tokens.LabelLarge };

            _listerTab = new ListerTab(_profile) { Dock = DockStyle.Fill };
            _profileTab = new ProfileTab(_profile, OnProfileChanged) { Dock = DockStyle.Fill };
            _convertTab = new ConvertTab(_profile) { Dock = DockStyle.Fill };

            var t1 = new TabPage("Effect Lister") { BackColor = Md3Tokens.Surface };
            t1.Controls.Add(_listerTab);
            var t2 = new TabPage("Plugin Profile") { BackColor = Md3Tokens.Surface };
            t2.Controls.Add(_profileTab);
            var t3 = new TabPage("Convert") { BackColor = Md3Tokens.Surface };
            t3.Controls.Add(_convertTab);

            tabs.TabPages.Add(t1);
            tabs.TabPages.Add(t2);
            tabs.TabPages.Add(t3);

            Controls.Add(tabs);
        }

        void OnProfileChanged()
        {
            // Both other tabs read from the same PluginProfile instance,
            // so a profile edit should immediately affect how they flag
            // effects — same wiring as the Python version's MainWindow.
            _listerTab.Refresh_();
            _convertTab.Refresh_();
        }
    }
}
