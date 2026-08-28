using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Convert: two-pane workspace — clickable hero drop zone / effect checklist,
    /// target + output options with an auto-naming fallback, a CTA that doubles as
    /// a job summary, and a post-save banner with Explorer handoff. Right pane:
    /// insight callout + themed console.
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
        private string _lastOutput;

        // DragEnter/DragLeave fire on every child boundary crossing; a depth
        // counter is the only flicker-free way to know the drag truly left.
        private int _dragDepth;

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
            // checkbox events bubble to the list — re-count whenever a row toggles
            EffectList.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => UpdateCta()));
            EffectList.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => UpdateCta()));

            Console.Log("[SYSTEM] Engine initialized.");
            Console.Log("[INFO] Waiting for file input…");
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

        private void Hero_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenFile();

        // ---------- drag feedback ----------
        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (!HasFfx(e.Data)) return;
            _dragDepth++;
            DragOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            if (_dragDepth > 0) _dragDepth--;
            if (_dragDepth == 0) DragOverlay.Visibility = Visibility.Collapsed;
        }

        private void Page_Drop(object sender, DragEventArgs e)
        {
            _dragDepth = 0;
            DragOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var ffx = files.FirstOrDefault(f => f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));
                if (ffx != null) { LoadFile(ffx); return; }
            }
            MessageBox.Show(this.FindWindow(),
                "No .ffx preset was found in the dropped items.",
                "Unsupported file", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool HasFfx(IDataObject data) =>
            data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));

        private void LoadFile(string path)
        {
            try
            {
                SaveBanner.Visibility = Visibility.Collapsed; // fresh run clears the last result

                _inputPath = path;
                _inputData = File.ReadAllBytes(path);
                _currentEffects = Pipeline.ListEffects(_inputData);

                StatusText.Text = System.IO.Path.GetFileName(path);
                Console.Log($"[INFO] Loaded {System.IO.Path.GetFileName(path)} ({_inputData.Length} bytes).");
                HistoryStore.Push(path, _currentEffects.Count(e => !e.IsSentinel));

                RefreshEffects();
                ConvertBtn.IsEnabled = true;
                UpdateCta();
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
            var names = EffectNameLookup.Load();
            _rows.Clear();
            foreach (var eff in _currentEffects.Where(e => !e.IsSentinel))
            {
                // same recognition chain as the lister — system scan first,
                // reference tables second — so one match name can never
                // carry two different identities across the two flows
                var match = PluginRecognition.Resolve(eff.MatchName, table, names);
                bool missing = !match.Installed && _profile.Owns(match.Vendor) == false;
                _rows.Add(new EffectRow
                {
                    MatchName = eff.MatchName,
                    VendorLabel = $"({match.Vendor ?? "unknown vendor"})",
                    IsChecked = missing
                });
            }
            bool hasEffects = _rows.Count > 0;
            Hero.Visibility = hasEffects ? Visibility.Collapsed : Visibility.Visible;
            EffectList.Visibility = hasEffects ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Cta text doubles as a job summary: kept vs marked-for-removal.</summary>
        private void UpdateCta()
        {
            if (_inputData == null)
            {
                ConvertBtn.IsEnabled = false;
                ConvertBtn.Content = "Load a preset to start conversion";
                return;
            }
            ConvertBtn.IsEnabled = true;
            int total = _rows.Count;
            int removed = _rows.Count(r => r.IsChecked);
            ConvertBtn.Content = total == 0
                ? "Convert (preset has no removable effects)"
                : $"Convert · {total - removed} kept · {removed} marked for removal";
        }

        // ---------- conversion ----------
        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (_inputData == null) return;

            var toRemove = new HashSet<string>(
                _rows.Where(r => r.IsChecked).Select(r => r.MatchName));

            string targetKey = InternalKeyFor(TargetCombo.SelectedItem as string ?? "After Effects CS5.5");
            Console.Log($"[SYSTEM] Converting to target '{targetKey}'…");

            Pipeline.ConversionResult result;
            try
            {
                result = Pipeline.Convert(_inputData, targetKey, toRemove.Count > 0 ? toRemove : null);
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
                try { File.WriteAllBytes(outPath, result.Data); }
                catch (Exception ex)
                {
                    Console.Log($"[ERROR] Could not write '{outPath}': {ex.Message}");
                    MessageBox.Show(this.FindWindow(),
                        $"Could not overwrite '{outPath}':\n{ex.Message}\n\nIs the file open in After Effects?",
                        "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "After Effects Presets (*.ffx)|*.ffx",
                    FileName = SuggestedFileName(targetKey)
                };
                var dir = string.IsNullOrEmpty(_inputPath) ? null : Path.GetDirectoryName(_inputPath);
                if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog() != true) { Console.Log("[INFO] Save cancelled."); return; }
                outPath = dlg.FileName;
                try { File.WriteAllBytes(outPath, result.Data); }
                catch (Exception ex)
                {
                    Console.Log($"[ERROR] Could not write '{outPath}': {ex.Message}");
                    MessageBox.Show(this.FindWindow(),
                        $"Could not save to '{outPath}':\n{ex.Message}",
                        "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            _lastOutput = outPath;
            SavedToText.Text = outPath;
            ShowBanner();
            Console.Log($"[SUCCESS] Saved: {outPath}");
            if (result.RemovedEffects.Count > 0)
                Console.Log($"[INFO] Removed: {string.Join(", ", result.RemovedEffects)}");
            foreach (var w in result.Warnings) Console.Log($"[WARNING] {w}");
            // Pipeline.Convert runs Verify() internally and throws on failure,
            // so reaching this line means the output is clean.
            Console.Log("[OK] Verification pass clean — structure, indices and keyframe data intact.");
        }

        /// <summary>"MyPreset_cs55.ffx" next to the source — derived from the chosen target.</summary>
        private string SuggestedFileName(string targetKey) =>
            $"{System.IO.Path.GetFileNameWithoutExtension(_inputPath)}_{targetKey.Replace(".", "").ToLowerInvariant()}.ffx";

        private void ShowBanner()
        {
            SaveBanner.BeginAnimation(OpacityProperty, null);
            SaveBanner.Opacity = 0;
            SaveBanner.Visibility = Visibility.Visible;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            SaveBanner.BeginAnimation(OpacityProperty, anim);
        }

        private void OpenFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastOutput)) return;
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_lastOutput}\"");
            }
            catch
            {
                var fallback = Path.GetDirectoryName(_lastOutput);
                if (!string.IsNullOrEmpty(fallback))
                    Process.Start("explorer.exe", $"\"{fallback}\"");
            }
        }

        private Window FindWindow() => Window.GetWindow(this);
    }
}
