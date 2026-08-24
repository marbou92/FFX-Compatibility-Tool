using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Convert: two-pane workspace â€” hero drop zone / effect checklist,
    /// target + encoding options and a full-width CTA on the left;
    /// insight callout + themed console on the right.
    /// </summary>
    public partial class ConvertPage : UserControl, ISection
    {
        public class EffectRow
        {
            public string MatchName { get; set; }
            public string VendorLabel { get; set; }
            public bool IsChecked { get; set; }
        }

        private readonly PluginProfile _profile;
        private byte[] _inputData;
        private string _inputPath;
        private List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();
        private readonly ObservableCollection<EffectRow> _rows = new ObservableCollection<EffectRow>();

        private static readonly Dictionary<string, string> DisplayNames =
            new Dictionary<string, string> { { "cs5.5", "After Effects CS5.5" } };

        public ConvertPage(PluginProfile profile)
        {
            InitializeComponent();
            _profile = profile;

            TargetCombo.ItemsSource = Pipeline.KnownVersions.Keys
                .OrderBy(k => k)
                .Select(DisplayNameFor)
                .ToList();
            TargetCombo.SelectedIndex = 0;

            EffectList.ItemsSource = _rows;
            Console.Log("[SYSTEM] Engine initialized.");
            Console.Log("[INFO] Waiting for file inputâ€¦");
        }

        private static string DisplayNameFor(string key) =>
            DisplayNames.TryGetValue(key, out var v) ? v : key;

        private static string InternalKeyFor(string display) =>
            DisplayNames.FirstOrDefault(kv => kv.Value == display).Key ?? display;

        public void OnShown() { }

        public void OnProfileChanged() => RefreshEffects();

        // ---------- file loading ----------
        public void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" };
            if (dlg.ShowDialog() == true) LoadFile(dlg.FileName);
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length > 0 && files[0].EndsWith(".ffx", StringComparison.OrdinalIgnoreCase))
                e.Effects = DragDropEffects.Copy;
        }

        private void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                LoadFile(files[0]);
        }

        private void LoadFile(string path)
        {
            try
            {
                _inputPath = path;
                _inputData = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(_inputData);

                StatusText.Text = "Status: " + System.IO.Path.GetFileName(path);
                Console.Log($"[INFO] Loaded {System.IO.Path.GetFileName(path)} ({_inputData.Length} bytes).");
                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));

                RefreshEffects();
                ConvertBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Console.Log($"[ERROR] Failed to read '{System.IO.Path.GetFileName(path)}': {ex.Message}");
                MessageBox.Show(this.FindWindow(),
                    $"Failed to read '{System.IO.Path.GetFileName(path)}':\n{ex.Message}",
                    "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshEffects()
        {
            var table = PluginLookup.LoadTable();
            _rows.Clear();
            foreach (var eff in _currentEffects.Where(e => !e.IsSentinel))
            {
                var match = PluginLookup.Resolve(eff.MatchName, table);
                bool missing = _profile.Owns(match.Vendor) == false;
                _rows.Add(new EffectRow
                {
                    MatchName = eff.MatchName,
                    VendorLabel = $"({match.Vendor ?? "unknown vendor"})",
                    IsChecked = missing
                });
            }
            bool hasEffects = _rows.Count > 0;
            Hero.Visibility = hasEffects ? Visibility.Collapsed : Visibility.Visible;
            HeroDash.Visibility = Hero.Visibility;
            EffectList.Visibility = hasEffects ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- conversion ----------
        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (_inputData == null) return;

            var toRemove = new HashSet<string>(
                _rows.Where(r => r.IsChecked).Select(r => r.MatchName));

            string target = InternalKeyFor(TargetCombo.SelectedItem as string ?? "After Effects CS5.5");
            Console.Log($"[SYSTEM] Converting to target '{target}'â€¦");

            Pipeline.ConversionResult result;
            try
            {
                result = Pipeline.Convert(_inputData, target, toRemove.Count > 0 ? toRemove : null);
            }
            catch (Exception ex)
            {
                Console.Log($"[ERROR] {ex.Message}");
                MessageBox.Show(this.FindWindow(), ex.Message, "Conversion failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string outPath;
            if (OverwriteCheck.IsChecked == true && !string.IsNullOrEmpty(_inputPath))
            {
                outPath = _inputPath;
                File.WriteAllBytes(outPath, result.Data);
            }
            else
            {
                var dlg = new SaveFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" };
                if (dlg.ShowDialog() != true) { Console.Log("[INFO] Save cancelled."); return; }
                outPath = dlg.FileName;
                File.WriteAllBytes(outPath, result.Data);
            }

            Console.Log($"[SUCCESS] Saved: {outPath}");
            if (result.RemovedEffects.Count > 0)
                Console.Log($"[INFO] Removed: {string.Join(", ", result.RemovedEffects)}");
            foreach (var w in result.Warnings) Console.Log($"[WARNING] {w}");
            // Pipeline.Convert runs Verify() internally and throws on failure,
            // so reaching this line means the output is clean.
            Console.Log("[OK] Verification pass clean â€” structure, indices and keyframe data intact.");
        }

        private Window FindWindow() => Window.GetWindow(this);
    }
}
