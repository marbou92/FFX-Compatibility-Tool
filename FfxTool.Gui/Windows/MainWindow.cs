using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Common surface MainWindow uses to drive the active page.</summary>
    public interface ISection
    {
        void OpenFile();
        void OnShown();
    }

    public partial class MainWindow : Window
    {
        private readonly PluginProfile _profile;
        private readonly ConvertPage _convert;
        private readonly ListerPage _lister;
        private readonly ProfilePage _profilePage;
        private readonly SettingsPage _settings;

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowBounds();

            _profile = PluginProfile.Load();

            _lister = new ListerPage(_profile);
            _profilePage = new ProfilePage(_profile, OnProfileChanged);
            _convert = new ConvertPage(_profile);
            _settings = new SettingsPage();

            Rail.AddItem("Effect Lister", "List");
            Rail.AddItem("Plugin Profile", "Plugin");
            Rail.AddItem("Convert", "SwapHoriz");
            Rail.AddItem("Settings", "Settings");
            Rail.SelectionChanged += i => ShowSection(i);
            Rail.FabClicked += () => ActiveSection()?.OpenFile();

            MinBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            MaxBtn.Click += (s, e) => ToggleMaximize();
            CloseBtn.Click += (s, e) => Close();
            StateChanged += (s, e) => MaxIcon.IconName =
                WindowState == WindowState.Maximized ? "Restore" : "Maximize";

            ShowSection(0);
            UpdateFooter();

            ThemeService.Changed += () => UpdateFooter();
        }

        private ISection ActiveSection()
        {
            switch (Rail.SelectedIndex)
            {
                case 0: return _lister;
                case 1: return null;
                case 2: return _convert;
                case 3: return null;
                default: return null;
            }
        }

        private void ShowSection(int index)
        {
            switch (index)
            {
                case 0: PageHost.Content = _lister; break;
                case 1: PageHost.Content = _profilePage; break;
                case 2: PageHost.Content = _convert; break;
                case 3: PageHost.Content = _settings; break;
            }
            (PageHost.Content as ISection)?.OnShown();
        }

        private void OnProfileChanged()
        {
            _convert.OnProfileChanged();
            _lister.OnProfileChanged();
            UpdateFooter();
        }

        private void UpdateFooter()
        {
            FooterLeft.Text = $"Profile: {_profile.OwnedVendors.Count} vendor(s)";
            int db = 0;
            try { db = PluginLookup.LoadTable().Count; } catch { }
            FooterRight.Text = db > 0 ? $"Plugin DB: {db} entries" : "Plugin DB: not found";
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                ActiveSection()?.OpenFile();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D4)
            {
                Rail.SelectWithoutNotify((int)e.Key - (int)Key.D1);
                ShowSection((int)e.Key - (int)Key.D1);
                e.Handled = true;
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized; // WindowChrome handles the working-area bounds correctly
        }

        // ---------- window bounds persistence (window.json) ----------
        [DataContract(Namespace = "")]
        private class StoredBounds
        {
            [DataMember] public double X;
            [DataMember] public double Y;
            [DataMember] public double Width;
            [DataMember] public double Height;
            [DataMember] public bool Maximized;
        }

        private string BoundsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "window.json");
        }

        private void LoadWindowBounds()
        {
            try
            {
                if (!File.Exists(BoundsPath())) return;
                StoredBounds b;
                var serializer = new DataContractJsonSerializer(typeof(StoredBounds));
                using (var fs = File.OpenRead(BoundsPath()))
                    b = serializer.ReadObject(fs) as StoredBounds;
                if (b == null) return;

                if (b.Width >= MinWidth && b.Height >= MinHeight)
                {
                    Width = b.Width;
                    Height = b.Height;
                }
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.X;
                Top = b.Y;
                if (b.Maximized) WindowState = WindowState.Maximized;
                // make sure a saved off-screen position can't strand the window
                if (Left < -Width + 100 || Left > SystemParameters.VirtualScreenWidth - 100)
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch { /* defaults */ }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var b = new StoredBounds
                {
                    X = RestoreBounds.Left,
                    Y = RestoreBounds.Top,
                    Width = RestoreBounds.Width,
                    Height = RestoreBounds.Height,
                    Maximized = WindowState == WindowState.Maximized
                };
                var serializer = new DataContractJsonSerializer(typeof(StoredBounds));
                using (var fs = File.Create(BoundsPath()))
                    serializer.WriteObject(fs, b);
            }
            catch { /* best-effort */ }
            base.OnClosing(e);
        }
    }
}
