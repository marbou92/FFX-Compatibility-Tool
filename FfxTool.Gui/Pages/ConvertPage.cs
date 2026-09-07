using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Convert: two-pane workspace — clickable hero drop zone / effect checklist,
    /// target + output options with an auto-naming fallback, a CTA that doubles as
    /// a job summary, and a post-save banner with Explorer handoff. Right pane:
    /// insight callout + themed console.
    ///
    /// Folder mode (baked in — the old Batch section): a whole folder, or a
    /// multi-file drop/selection, swaps the checklist for a queue card and runs
    /// one conversion per preset with the queue's settings, streaming per-file
    /// results into the console. Derived outputs overwrite their own re-run
    /// result, never the input file; an in-place overwrite demands a Yes/No.
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

        // folder / multi-file queue (the baked-in batch): null = single preset
        private enum QueueOutputMode { Subfolder, Suffix, Overwrite, ZipTree, FolderTree }
        private List<string> _queue;
        private string _queueRoot;                 // the source folder, for output derivation
        // per-file effect removals toggled by hand (double-click a file
        // manager row → the checklist) — keyed by full path, applied on
        // top of the profile-driven recognition chain during a queue run
        private readonly Dictionary<string, HashSet<string>> _queueRemovals =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        // the queued preset currently open for editing (null = file manager)
        private string _queueEditPath;
        // file rows in _queue order — the worker indexes these; the display
        // collection interleaves the subfolder group headers
        private readonly List<QueueFileVm> _queueRowFiles = new List<QueueFileVm>();
        private CancellationTokenSource _cts;
        private volatile bool _running;
        // the file manager rows of the loaded queue (same order as _queue)
        private readonly ObservableCollection<QueueFileVm> _queueRows = new ObservableCollection<QueueFileVm>();

        private sealed class QueueResult
        {
            public bool Ok;
            public bool Warn;
            public int Kept;
            public int Removed;
            public string Note;
        }

        /// <summary>One row of the folder file manager: name and size while
        /// queued, status + note filling in live as the batch runs.</summary>
        private sealed class QueueFileVm : System.ComponentModel.INotifyPropertyChanged
        {
            public string Name { get; set; }
            public string Size { get; set; }
            // full path (double-click opens the preset) and the display
            // folder the row groups under when the queue came from a folder
            public string Path { get; set; }
            public string Group { get; set; }

            string _status = "";
            public string Status
            {
                get { return _status; }
                set { _status = value; Raise(nameof(Status)); }
            }

            string _note = "";
            public string Note
            {
                get { return _note; }
                set { _note = value; Raise(nameof(Note)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            void Raise(string prop) =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
        }

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
            // checkbox events bubble to the list — re-count and remember
            // the edits whenever a row toggles
            EffectList.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => QueueEditToggled()));
            EffectList.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => QueueEditToggled()));

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
            var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx", Multiselect = true };
            if (dlg.ShowDialog() != true) return;
            if (dlg.FileNames.Length == 1) { LoadFile(dlg.FileNames[0]); return; }
            LoadQueue(dlg.FileNames.Where(File.Exists).ToList(), null);
        }

        /// <summary>Handoff target for the Effect Lister's “Convert this
        /// preset…” button — MainWindow switches the section and loads the
        /// file here.</summary>
        public void LoadExternal(string path) => LoadFile(path);

        /// <summary>Handoff target for the Effect Lister's convert button
        /// when a whole selection (or an untouched folder) goes over — the
        /// file manager here keeps the folder's subfolder layout.</summary>
        public void LoadExternalQueue(List<string> files, string root) => LoadQueue(files, root);

        private void Hero_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenFile();

        private void FolderBtn_Click(object sender, RoutedEventArgs e) => BrowseFolder();

        private void BrowseFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Pick the folder that holds the .ffx presets.",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            LoadQueue(FolderScan.Collect(dlg.SelectedPath, QueueRecursive.IsChecked == true), dlg.SelectedPath);
        }

        // QueueRecursive is IsChecked="True" in the XAML, so this fires while
        // the BAML is still being applied — guard on IsInitialized (the
        // round-32 lesson: never on a control that happens to be non-null yet)
        private void QueueOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            // only a folder source has a walk to redo; a dropped file list is fixed
            if (_queue != null && !string.IsNullOrEmpty(_queueRoot))
                LoadQueue(FolderScan.Collect(_queueRoot, QueueRecursive.IsChecked == true), _queueRoot);
        }

        // ---------- drag feedback ----------
        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (!HasPayload(e.Data)) return;
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

            if (e.Data.GetData(DataFormats.FileDrop) is string[] items && items.Length > 0)
            {
                // a folder drop means the whole folder, recursively per the checkbox
                string folder = items.FirstOrDefault(Directory.Exists);
                if (folder != null)
                {
                    LoadQueue(FolderScan.Collect(folder, QueueRecursive.IsChecked == true), folder);
                    return;
                }

                var dropped = items.Where(f => f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
                if (dropped.Count == 1) { LoadFile(dropped[0]); return; }
                if (dropped.Count > 1) { LoadQueue(dropped, null); return; }
            }
            MessageBox.Show(this.FindWindow(),
                "No .ffx preset was found in the dropped items.",
                "Unsupported file", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool HasPayload(IDataObject data) =>
            data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => Directory.Exists(f) ||
                           f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));

        private void LoadFile(string path) => LoadFile(path, false);

        /// <summary>inQueue: the preset was opened from the folder file
        /// manager (double-click) — the queue survives so "All presets"
        /// returns to it, and the checklist's hand toggles are remembered
        /// for exactly this file.</summary>
        private void LoadFile(string path, bool inQueue)
        {
            if (_running)
            {
                Console.Log("[INFO] Finish or cancel the running batch first.");
                return;
            }
            try
            {
                // a plain single-preset load leaves folder mode — the queue
                // card swaps out for the hero / checklist
                if (inQueue) _queueEditPath = path;
                else
                {
                    _queue = null;
                    _queueRoot = null;
                    _queueEditPath = null;
                }
                QueueCard.Visibility = Visibility.Collapsed;
                QueuePanel.Visibility = inQueue && _queue != null
                    ? Visibility.Visible : Visibility.Collapsed;
                SaveBannerTitle.Text = "Conversion saved";
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
                _queueEditPath = null; // the open failed — nothing is being edited
                Console.Log($"[ERROR] Failed to read '{System.IO.Path.GetFileName(path)}': {ex.Message}");
                MessageBox.Show(this.FindWindow(),
                    $"Failed to read '{System.IO.Path.GetFileName(path)}':\n{ex.Message}",
                    "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Folder / multi-file mode: the queue card replaces the
        /// single-preset workspace; every .ffx becomes one conversion job.</summary>
        private void LoadQueue(List<string> files, string root)
        {
            if (_running)
            {
                Console.Log("[INFO] Finish or cancel the running batch first.");
                return;
            }
            files = files ?? new List<string>();
            if (files.Count == 0)
            {
                MessageBox.Show(this.FindWindow(),
                    "No .ffx presets were found there.", "Nothing to convert",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // group order follows the files; sort by (rel folder, name) so
            // each subfolder forms ONE contiguous group — and so the
            // worker's per-file status lands on the matching row
            if (root != null)
                files = files.OrderBy(
                        f => FolderScan.RelUnder(root, f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            _queue = files;
            _queueRoot = root;

            SaveBanner.Visibility = Visibility.Collapsed;
            Hero.Visibility = Visibility.Collapsed;
            EffectList.Visibility = Visibility.Collapsed;
            QueueCard.Visibility = Visibility.Visible;
            QueueText.Text = root ?? files.Count + " presets queued";
            if (QueueOutput.SelectedIndex < 0) QueueOutput.SelectedIndex = 0;

            // the file manager: one row per preset with its size; each
            // row's status fills in live as the batch runs. A folder queue
            // groups its rows under the subfolders they came from.
            _queueRows.Clear();
            _queueRowFiles.Clear();
            _queueRemovals.Clear();
            _queueEditPath = null;
            bool grouped = root != null && files.Any(
                f => FolderScan.RelUnder(root, f).Length > 0);
            string leaf = root != null ? FolderScan.LeafName(root) : "";
            foreach (var f in files)
            {
                string size = "—";
                try { size = FolderScan.FmtSize(new FileInfo(f).Length); }
                catch { /* unreadable metadata — the size stays “—” */ }
                string rel = root != null ? FolderScan.RelUnder(root, f) : "";
                var vm = new QueueFileVm
                {
                    Name = Path.GetFileName(f),
                    Size = size,
                    Path = f,
                    Group = !grouped ? null
                        : (rel.Length == 0 ? leaf : leaf + "\\" + rel)
                };
                _queueRows.Add(vm);
                _queueRowFiles.Add(vm);
            }
            if (grouped)
            {
                var view = new ListCollectionView(_queueRows);
                view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
                QueueFileList.ItemsSource = view;
            }
            else
            {
                QueueFileList.ItemsSource = _queueRows;
            }
            QueuePanel.Visibility = Visibility.Visible;

            StatusText.Text = files.Count + " preset" + (files.Count == 1 ? "" : "s") + " queued";
            Console.Log($"[INFO] Queue loaded: {files.Count} preset(s)" +
                        (root != null ? " from " + root : "") + ".");
            UpdateCta();
        }

        private void RefreshEffects()
        {
            var table = PluginLookup.LoadTable();
            var names = EffectNameLookup.Load();
            // a preset opened from the file manager keeps its hand toggles:
            // the checked set recorded for it re-applies over the defaults
            HashSet<string> kept = null;
            if (_queueEditPath != null)
                _queueRemovals.TryGetValue(_queueEditPath, out kept);
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
                    IsChecked = kept != null ? kept.Contains(eff.MatchName) : missing
                });
            }
            bool hasEffects = _rows.Count > 0;
            Hero.Visibility = hasEffects ? Visibility.Collapsed : Visibility.Visible;
            EffectList.Visibility = hasEffects ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>A checklist toggle while a queued preset is open for
        /// editing: the checked match names are remembered for exactly this
        /// file and ride into the queue run on top of the profile chain.</summary>
        private void QueueEditToggled()
        {
            if (_queueEditPath == null) { UpdateCta(); return; }
            var checkedNow = new HashSet<string>(
                _rows.Where(r => r.IsChecked).Select(r => r.MatchName),
                StringComparer.OrdinalIgnoreCase);
            if (checkedNow.Count > 0) _queueRemovals[_queueEditPath] = checkedNow;
            else _queueRemovals.Remove(_queueEditPath);
            UpdateCta();
        }

        /// <summary>Cta text doubles as a job summary: kept vs marked-for-removal
        /// (single preset) or the queue size (folder mode).</summary>
        private void UpdateCta()
        {
            if (_running) return;
            if (_queue != null && _queueEditPath == null)
            {
                ConvertBtn.IsEnabled = true;
                ConvertBtn.Content = "Convert · " + _queue.Count +
                                     " preset" + (_queue.Count == 1 ? "" : "s");
                return;
            }
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
            if (_running) return;
            if (_queue != null && _queueEditPath == null) { RunQueue(); return; }
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

        // ---------- folder queue conversion ----------

        /// <summary>The baked-in batch: one pipeline run per queued preset,
        /// settings from the queue card, per-file results streamed to the
        /// console. Cancellation lands between files.</summary>
        private async void RunQueue()
        {
            var files = new List<string>(_queue);
            var output = (QueueOutputMode)Math.Max(0, QueueOutput.SelectedIndex);
            if (output == QueueOutputMode.Overwrite)
            {
                var choice = MessageBox.Show(this.FindWindow(),
                    "Overwrite the original preset files in place?\n\n" +
                    "The originals cannot be recovered — every conversion is verified before writing, but nothing backs them up.",
                    "Overwrite originals", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.Yes) return;
            }
            string targetKey = InternalKeyFor(TargetCombo.SelectedItem as string ?? "After Effects CS5.5");
            bool removeMissing = RemoveMissingCheck.IsChecked == true;

            // output root for the subfolder mode: the source folder, or the
            // first dropped file's folder when the job came from a file drop
            string root = _queueRoot ?? Path.GetDirectoryName(files[0]);
            string outDir = null;
            if (output == QueueOutputMode.Subfolder)
            {
                outDir = Path.Combine(root ?? ".", "converted");
                try { Directory.CreateDirectory(outDir); }
                catch (Exception ex)
                {
                    MessageBox.Show(this.FindWindow(),
                        "Could not create the output folder:\n" + ex.Message,
                        "Output folder failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // tree modes: the converted set mirrors the source folder's
            // subfolders — one ZIP beside the source folder, or a plain
            // folder next to it. The derived output reuses (overwrites) its
            // own previous result, never the inputs.
            string outBase = null;
            string zipPath = null;
            string staging = null;
            if (output == QueueOutputMode.ZipTree || output == QueueOutputMode.FolderTree)
            {
                string baseName = _queueRoot != null
                    ? FolderScan.LeafName(_queueRoot) + " (converted)"
                    : "Converted presets";
                string parent = _queueRoot != null ? Path.GetDirectoryName(_queueRoot) : root;
                if (string.IsNullOrEmpty(parent)) parent = ".";
                if (output == QueueOutputMode.FolderTree)
                {
                    outBase = Path.Combine(parent, baseName);
                    try { Directory.CreateDirectory(outBase); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this.FindWindow(),
                            "Could not create the output folder:\n" + ex.Message,
                            "Output folder failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    zipPath = Path.Combine(parent, baseName + ".zip");
                    staging = Path.Combine(Path.GetTempPath(),
                        "FFX-Convert-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    try { Directory.CreateDirectory(staging); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this.FindWindow(),
                            "Could not create the staging folder:\n" + ex.Message,
                            "Output failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            _running = true;
            _cts = new CancellationTokenSource();
            ConvertBtn.IsEnabled = false;
            CancelLink.Visibility = Visibility.Visible;
            SaveBanner.Visibility = Visibility.Collapsed;
            SaveBannerTitle.Text = "Batch finished";
            int total = files.Count;
            Console.Log($"[SYSTEM] Batch conversion started — {total} preset(s) → {targetKey}.");
            var reporter = new Progress<(int done, string text)>(v =>
                ConvertBtn.Content = v.text);

            int done = 0, ok = 0, warned = 0, failed = 0, removedTotal = 0;
            bool cancelled = false;
            string fatal = null;
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        string path = files[i];
                        string name = Path.GetFileName(path);
                        var row = _queueRowFiles[i];
                        Dispatcher.BeginInvoke(new Action(() => row.Status = "Converting…"));
                        ((IProgress<(int, string)>)reporter).Report((done,
                            "Converting… " + (done + 1) + "/" + total + " — " + name));
                        _queueRemovals.TryGetValue(path, out var userRemove);
                        string forcedOut = null;
                        if (output == QueueOutputMode.ZipTree || output == QueueOutputMode.FolderTree)
                        {
                            // the mirrored relative path keeps the source
                            // folder's subfolder layout inside the output
                            string rel = FolderScan.RelUnder(_queueRoot, path);
                            string baseDir = output == QueueOutputMode.ZipTree ? staging : outBase;
                            forcedOut = rel.Length == 0
                                ? Path.Combine(baseDir, Path.GetFileName(path))
                                : Path.Combine(baseDir, rel, Path.GetFileName(path));
                        }
                        var r = QueueConvertOne(path, targetKey, removeMissing, output, outDir,
                                                userRemove, forcedOut);
                        done++;
                        if (!r.Ok) failed++;
                        else { ok++; if (r.Warn) warned++; }
                        removedTotal += r.Removed;
                        string rowStatus = !r.Ok ? "FAILED" : r.Warn ? "WARN" : "OK";
                        string rowNote = r.Note ?? "";
                        Dispatcher.BeginInvoke(new Action(() =>
                        { row.Status = rowStatus; row.Note = rowNote; }));
                        string line = !r.Ok
                            ? "[ERROR] " + name + " — " + r.Note
                            : "[OK] " + name + " — " + r.Kept + " effect(s) kept"
                              + (r.Removed > 0 ? " · " + r.Removed + " removed" : "")
                              + (r.Warn ? " — warnings: " + r.Note : "");
                        Dispatcher.BeginInvoke(new Action(() => Console.Log(line)));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                fatal = ex.Message;
            }

            _running = false;
            _cts.Dispose();
            _cts = null;
            CancelLink.Visibility = Visibility.Collapsed;
            UpdateCta(); // re-enables the CTA with the right summary

            if (fatal != null)
                Console.Log("[ERROR] The job stopped early: " + fatal);
            string summary = done == 0
                ? "Nothing was processed."
                : done + " of " + total + " processed — " + ok + " ok · " + warned +
                  " warning" + (warned == 1 ? "" : "s") + " · " + failed + " failed" +
                  (removedTotal > 0 ? " · " + removedTotal + " effects removed" : "") +
                  (cancelled ? " · cancelled" : "");
            Console.Log(fatal != null ? "[ERROR] " + summary
                        : cancelled ? "[SYSTEM] " + summary
                        : "[SUCCESS] " + summary);
            LogService.Append("batch convert: " + summary);

            if (output == QueueOutputMode.ZipTree && staging != null)
            {
                // whatever converted cleanly becomes the ZIP — a cancelled
                // or failed run contributes less but never a broken archive;
                // nothing converted means there is nothing to hand over
                if (ok > 0 && !cancelled && fatal == null)
                {
                    try
                    {
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        System.IO.Compression.ZipFile.CreateFromDirectory(
                            staging, zipPath,
                            System.IO.Compression.CompressionLevel.Optimal, false);
                        Console.Log("[SUCCESS] ZIP written: " + zipPath);
                    }
                    catch (Exception ex)
                    {
                        Console.Log("[ERROR] The ZIP could not be written: " + ex.Message);
                        zipPath = null;
                    }
                }
                else zipPath = null;
                try { Directory.Delete(staging, true); } catch { }
            }
            if (done > 0 && (output != QueueOutputMode.ZipTree || zipPath != null))
            {
                SavedToText.Text = summary;
                ShowBanner();
            }
            if (output == QueueOutputMode.ZipTree) _lastOutput = zipPath;
            else if (output == QueueOutputMode.FolderTree) _lastOutput = outBase;
            else _lastOutput = output == QueueOutputMode.Subfolder ? outDir : root;
        }

        /// <summary>The proven single-file pipeline applied to one queued
        /// preset: version patch plus optional profile-driven removal of
        /// effects the user doesn't own — the same recognition chain as
        /// the single-preset checklist (system scan first, reference tables
        /// second).</summary>
        private QueueResult QueueConvertOne(string path, string targetKey, bool removeMissing,
                                            QueueOutputMode output, string outDir,
                                            HashSet<string> userRemove, string forcedOutPath)
        {
            var res = new QueueResult { Ok = true, Note = "" };
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var effects = Pipeline.ListEffects(data);

                HashSet<string> toRemove = null;
                if (removeMissing)
                {
                    var table = PluginLookup.LoadTable();
                    var names = EffectNameLookup.Load();
                    toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var eff in effects.Where(x => !x.IsSentinel))
                    {
                        var match = PluginRecognition.Resolve(eff.MatchName, table, names);
                        if (!match.Installed && _profile.Owns(match.Vendor) == false)
                            toRemove.Add(eff.MatchName);
                    }
                }

                if (userRemove != null && userRemove.Count > 0)
                {
                    // hand-toggled effects (the file manager's double-click
                    // checklist) ride on top of the profile-driven chain
                    toRemove = toRemove ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in userRemove) toRemove.Add(n);
                }

                var result = Pipeline.Convert(data, targetKey,
                    toRemove != null && toRemove.Count > 0 ? toRemove : null);

                string outPath = forcedOutPath ?? OutputPathFor(path, output, outDir, targetKey);
                var outFolder = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outFolder)) Directory.CreateDirectory(outFolder);
                File.WriteAllBytes(outPath, result.Data);

                res.Removed = result.RemovedEffects != null ? result.RemovedEffects.Count : 0;
                res.Kept = effects.Count(x => !x.IsSentinel) - res.Removed;
                bool hasWarnings = result.Warnings != null && result.Warnings.Count > 0;
                res.Warn = hasWarnings;
                if (hasWarnings)
                    res.Note = string.Join(" | ", result.Warnings.Take(2)) +
                               (result.Warnings.Count > 2 ? " …" : "");
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Note = ex.Message;
            }
            return res;
        }

        /// <summary>Derived outputs overwrite their own previous result;
        /// the one guarded edge is a name collision with the INPUT file
        /// (subfolder mode meeting a file already named *_cs55.ffx) — that
        /// falls back to a numbered suffix instead of destroying the source.</summary>
        private static string OutputPathFor(string path, QueueOutputMode mode, string outDir, string targetKey)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string suffix = targetKey.Replace(".", "").ToLowerInvariant();
            string candidate;
            switch (mode)
            {
                case QueueOutputMode.Overwrite:
                    return path;
                case QueueOutputMode.Suffix:
                    candidate = Path.Combine(Path.GetDirectoryName(path), name + "_" + suffix + ".ffx");
                    break;
                default:
                    candidate = Path.Combine(outDir, name + "_" + suffix + ".ffx");
                    break;
            }
            if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                candidate = Path.Combine(Path.GetDirectoryName(path), name + "_" + suffix + "_1.ffx");
            return candidate;
        }

        /// <summary>Back to the folder file manager from an opened preset
        /// (the "All presets" link) — the queue survives untouched, its rows
        /// keep whatever statuses the last run left.</summary>
        private void ShowQueueView()
        {
            _queueEditPath = null;
            _inputData = null;
            _inputPath = null;
            Hero.Visibility = Visibility.Collapsed;
            EffectList.Visibility = Visibility.Collapsed;
            QueueCard.Visibility = Visibility.Visible;
            SaveBanner.Visibility = Visibility.Collapsed;
            StatusText.Text = _queue.Count + " preset" + (_queue.Count == 1 ? "" : "s") + " queued";
            Console.Log("[INFO] Back to the file manager.");
            UpdateCta();
        }

        private void QueueLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_queue != null) ShowQueueView();
        }

        /// <summary>Double-click a file manager row: the preset opens in
        /// the checklist — its effects with their plugins (match names),
        /// each toggleable — while the queue waits behind "All presets".</summary>
        private void QueueFileList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var vm = QueueFileList.SelectedItem as QueueFileVm;
            if (vm == null || string.IsNullOrEmpty(vm.Path)) return;
            LoadFile(vm.Path, true);
        }

        private void Cancel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_running || _cts == null) return;
            _cts.Cancel();
            Console.Log("[INFO] Cancelling — the current preset finishes first.");
            CancelLink.Visibility = Visibility.Collapsed;
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
                // a batch handoff points at a FOLDER (open it directly); a
                // single-file handoff selects the file in its parent folder
                if (Directory.Exists(_lastOutput))
                    Process.Start("explorer.exe", "\"" + _lastOutput + "\"");
                else
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
