using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    /// <summary>
    /// Batch: point the tool at a folder of .ffx presets and run one of two
    /// jobs across all of them —
    ///   Inspect  the effect lister's deep read, one summary row per file;
    ///            nothing is ever written.
    ///   Convert  the convert page's proven pipeline (version downgrade +
    ///            optional removal of effects the profile says you don't
    ///            own), one output file per input. Originals are only
    ///            touched when the user explicitly picks "Overwrite" — and
    ///            even then only after a Yes/No warning.
    /// Both jobs run on a worker thread with live progress and a cancel
    /// that takes effect between files, and both export their table as CSV.
    /// </summary>
    public partial class BatchPage : UserControl, ISection
    {
        public class BatchRow
        {
            public string FileName { get; set; }
            public string Folder { get; set; }
            public string Status { get; set; }
            public string Effects { get; set; }
            public string Params { get; set; }
            public string Animated { get; set; }
            public string Size { get; set; }
            public string Note { get; set; }
        }

        private enum JobMode { Inspect, Convert }
        private enum OutputMode { Subfolder, Suffix, Overwrite }

        private readonly PluginProfile _profile;
        private JobMode _mode = JobMode.Inspect;
        private string _sourceDir;                 // folder source
        private List<string> _droppedFiles;        // explicit file-list source
        private readonly ObservableCollection<BatchRow> _rows = new ObservableCollection<BatchRow>();
        private CancellationTokenSource _cts;
        private volatile bool _running;
        private int _runTotal;
        private string _lastOutputDir;

        // DragEnter/DragLeave fire on every child boundary crossing; a depth
        // counter is the only flicker-free way to know the drag truly left.
        private int _dragDepth;

        private static readonly Dictionary<string, string> DisplayNames =
            new Dictionary<string, string> { { "cs5.5", "After Effects CS5.5" } };

        public BatchPage(PluginProfile profile)
        {
            InitializeComponent();
            _profile = profile;

            TargetCombo.ItemsSource = Pipeline.KnownVersions.Keys
                .OrderBy(k => k)
                .Select(k => DisplayNames.TryGetValue(k, out var v) ? v : k)
                .ToList();
            TargetCombo.SelectedIndex = 0;
            EffectsCombo.SelectedIndex = 0;
            OutputCombo.SelectedIndex = 0;

            ResultList.ItemsSource = _rows;
            ApplyModeVisuals();
        }

        public void OnShown() { }

        /// <summary>Ctrl+O / the FAB route here: on the batch page the
        /// "open" gesture means choosing the source folder.</summary>
        public void OpenFile() => BrowseFolder();

        // Effect resolution happens inside each convert run, so a profile
        // edit is picked up by the next job with no page state to refresh.
        public void OnProfileChanged() { }

        // ---------- mode ----------

        private void ModeNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OptTarget == null) return; // XAML not fully loaded yet
            _mode = ModeNav.SelectedIndex == 1 ? JobMode.Convert : JobMode.Inspect;
            ApplyModeVisuals();
        }

        private void ApplyModeVisuals()
        {
            bool conv = _mode == JobMode.Convert;
            OptRecursive.Visibility = conv ? Visibility.Collapsed : Visibility.Visible;
            OptTarget.Visibility = conv ? Visibility.Visible : Visibility.Collapsed;
            OptEffects.Visibility = conv ? Visibility.Visible : Visibility.Collapsed;
            OptOutput.Visibility = conv ? Visibility.Visible : Visibility.Collapsed;
            // the numeric columns mean different things per job — relabel them
            HdCol1.Text = conv ? "Kept" : "Effects";
            HdCol2.Text = conv ? "Removed" : "Params";
            HdCol3.Text = conv ? "Target" : "Animated";
            UpdateCta();
        }

        // ---------- source ----------

        private void PickFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder();

        private void BrowseFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Pick the folder that holds the .ffx presets.",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            SetFolderSource(dlg.SelectedPath);
        }

        private void SetFolderSource(string path)
        {
            _droppedFiles = null;
            _sourceDir = path;
            SourcePathText.Text = path;
            ResetResults();
            UpdateCta();
        }

        private void SourceOption_Changed(object sender, RoutedEventArgs e)
        {
            if (SourceCountText == null) return; // XAML not fully loaded yet
            if (_droppedFiles == null) UpdateCta(); // recount for the new depth
        }

        /// <summary>The exact job list: the dropped files, or everything
        /// under the chosen folder (depth per the checkbox).</summary>
        private List<string> CollectFiles()
        {
            if (_droppedFiles != null) return _droppedFiles.ToList();
            if (string.IsNullOrEmpty(_sourceDir) || !Directory.Exists(_sourceDir))
                return new List<string>();
            var found = new List<string>();
            Walk(_sourceDir, RecursiveCheck.IsChecked == true, found);
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>Recursive walk that survives locked/denied subtrees —
        /// Directory.EnumerateFiles with AllDirectories throws mid-walk on
        /// .NET Framework and loses everything after the bad directory;
        /// per-directory catch means one unreadable subtree contributes
        /// nothing instead of killing the whole scan.</summary>
        private static void Walk(string dir, bool recursive, List<string> into)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.ffx"))
                    into.Add(f);
                if (recursive)
                    foreach (var d in Directory.EnumerateDirectories(dir))
                        Walk(d, true, into);
            }
            catch { /* a locked or denied subtree just contributes nothing */ }
        }

        private void UpdateCta()
        {
            if (_running || SourcePathText == null) return;
            int n = CountFiles();
            if (n == 0)
            {
                SourceCountText.Text = _droppedFiles == null && _sourceDir != null
                    ? "No .ffx presets found here"
                    : "Pick a folder or drop presets to begin";
                RunBtn.IsEnabled = false;
                RunBtn.Content = _mode == JobMode.Inspect
                    ? "Choose a source to start the scan"
                    : "Choose a source to start the batch conversion";
                return;
            }
            SourceCountText.Text = n + " .ffx preset" + (n == 1 ? "" : "s") + " found";
            RunBtn.IsEnabled = true;
            RunBtn.Content = _mode == JobMode.Inspect
                ? "Inspect · " + n + " preset" + (n == 1 ? "" : "s")
                : "Convert · " + n + " preset" + (n == 1 ? "" : "s");
        }

        private int CountFiles() => CollectFiles().Count;

        // ---------- drag feedback ----------

        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (!HasBatchPayload(e.Data)) return;
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
                var files = new List<string>();
                string folder = items.FirstOrDefault(Directory.Exists);
                if (folder != null) { SetFolderSource(folder); return; }
                foreach (var it in items)
                    if (it.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && File.Exists(it))
                        files.Add(it);
                if (files.Count > 0)
                {
                    _sourceDir = null;
                    _droppedFiles = files.Distinct(StringComparer.OrdinalIgnoreCase)
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                         .ToList();
                    SourcePathText.Text = files.Count == 1
                        ? files[0]
                        : _droppedFiles.Count + " files dropped";
                    ResetResults();
                    UpdateCta();
                    return;
                }
            }
            MessageBox.Show(this.FindWindow(),
                "No .ffx preset was found in the dropped items.",
                "Unsupported file", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool HasBatchPayload(IDataObject data) =>
            data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => Directory.Exists(f) ||
                           f.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));

        private void ResetResults()
        {
            _rows.Clear();
            SummaryText.Text = "";
            ExportCsvLink.Visibility = Visibility.Collapsed;
            OpenOutLink.Visibility = Visibility.Collapsed;
            ProgScale.ScaleX = 0;
        }

        // ---------- run ----------

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            var files = CollectFiles();
            if (files.Count == 0) return;

            OutputMode output = OutputMode.Subfolder;
            string targetKey = "cs5.5";
            bool removeMissing = false;
            if (_mode == JobMode.Convert)
            {
                output = (OutputMode)Math.Max(0, OutputCombo.SelectedIndex);
                if (output == OutputMode.Overwrite)
                {
                    var choice = MessageBox.Show(this.FindWindow(),
                        "Overwrite the original preset files in place?\n\n" +
                        "The originals cannot be recovered — every conversion is verified before writing, but nothing backs them up.",
                        "Overwrite originals", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes) return;
                }
                var display = TargetCombo.SelectedItem as string ?? "After Effects CS5.5";
                targetKey = DisplayNames.FirstOrDefault(kv => kv.Value == display).Key ?? "cs5.5";
                removeMissing = EffectsCombo.SelectedIndex == 1;
            }

            // output root for the subfolder mode: the source folder, or the
            // first dropped file's folder when the job came from a file drop
            string root = _sourceDir ?? Path.GetDirectoryName(files[0]);
            string outDir = null;
            if (_mode == JobMode.Convert && output == OutputMode.Subfolder)
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
            _lastOutputDir = outDir ?? root;

            _running = true;
            _runTotal = files.Count;
            _cts = new CancellationTokenSource();
            RunBtn.IsEnabled = false;
            CancelLink.Visibility = Visibility.Visible;
            ResetResults();

            int done = 0, ok = 0, warned = 0, failed = 0;
            string label = _mode == JobMode.Inspect ? "Inspecting" : "Converting";
            ProgressText.Text = label + "… 0/" + _runTotal;
            var reporter = new Progress<(int done, string text)>(v =>
            {
                ProgressText.Text = v.text;
                ProgScale.ScaleX = _runTotal == 0 ? 0 : (double)v.done / _runTotal;
            });

            bool cancelled = false;
            string fatal = null;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var path in files)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        var row = _mode == JobMode.Inspect
                            ? InspectOne(path)
                            : ConvertOne(path, targetKey, removeMissing, output, outDir);
                        var captured = row;
                        Dispatcher.BeginInvoke(new Action(() => _rows.Add(captured)));
                        done++;
                        if (row.Status == "FAILED") failed++;
                        else if (row.Status == "WARN") { warned++; ok++; }
                        else ok++;
                        ((IProgress<(int, string)>)reporter).Report((done,
                            label + "… " + done + "/" + _runTotal + " — " + Path.GetFileName(path)));
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
            RunBtn.IsEnabled = true;
            ProgScale.ScaleX = _runTotal == 0 ? 0 : Math.Min(1, (double)done / _runTotal);

            if (fatal != null)
            {
                ProgressText.Text = "The job stopped early: " + fatal;
                LogService.Append("batch " + _mode + ": fatal — " + fatal);
            }
            else
            {
                ProgressText.Text = cancelled
                    ? "Cancelled after " + done + " of " + _runTotal + "."
                    : "Done — " + done + " file" + (done == 1 ? "" : "s") + " processed.";
            }
            SummaryText.Text = done == 0
                ? "Nothing was processed."
                : done + " processed — " + ok + " ok · " + warned + " warning" + (warned == 1 ? "" : "s") +
                  " · " + failed + " failed" + (cancelled ? " · cancelled" : "");
            if (done > 0) ExportCsvLink.Visibility = Visibility.Visible;
            if (_mode == JobMode.Convert && ok > 0) OpenOutLink.Visibility = Visibility.Visible;
            LogService.Append("batch " + _mode + ": " + done + " file(s) — " + ok + " ok, " +
                              warned + " warn, " + failed + " failed" + (cancelled ? " (cancelled)" : ""));
            UpdateCta();
        }

        private void Cancel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_running || _cts == null) return;
            _cts.Cancel();
            ProgressText.Text = "Cancelling… (finishes the current file first)";
            CancelLink.Visibility = Visibility.Collapsed;
        }

        /// <summary>The read-only job: everything the effect lister would
        /// tell you about one preset, as one row.</summary>
        private BatchRow InspectOne(string path)
        {
            var row = BaseRow(path);
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var errors = new List<string>();
                var effects = PresetInspector.Inspect(data, errors);
                int par = 0, anim = 0;
                foreach (var e in effects) { par += e.Parameters.Count; anim += e.AnimatedCount; }
                row.Effects = effects.Count.ToString();
                row.Params = par.ToString();
                row.Animated = anim.ToString();
                if (errors.Count > 0)
                {
                    row.Status = "WARN";
                    row.Note = JoinNotes(errors);
                }
                else if (effects.Count == 0)
                {
                    row.Status = "WARN";
                    row.Note = "No effects or property groups decoded";
                }
            }
            catch (Exception ex)
            {
                MarkFailed(row, ex.Message);
            }
            return row;
        }

        /// <summary>The writing job: the convert page's pipeline, verbatim,
        /// applied to one file. Writes only the chosen output — and for the
        /// derived outputs, a re-run overwrites its own result (never the
        /// input) so re-running a folder stays idempotent.</summary>
        private BatchRow ConvertOne(string path, string targetKey, bool removeMissing,
                                    OutputMode output, string outDir)
        {
            var row = BaseRow(path);
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var effects = Pipeline.ListEffects(data);
                int realCount = effects.Count(x => !x.IsSentinel);

                HashSet<string> toRemove = null;
                if (removeMissing)
                {
                    var table = PluginLookup.LoadTable();
                    var names = EffectNameLookup.Load();
                    toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var eff in effects.Where(x => !x.IsSentinel))
                    {
                        // same recognition chain as the single-file convert
                        // page — system scan first, reference tables second
                        var match = PluginRecognition.Resolve(eff.MatchName, table, names);
                        if (!match.Installed && _profile.Owns(match.Vendor) == false)
                            toRemove.Add(eff.MatchName);
                    }
                }

                var result = Pipeline.Convert(data, targetKey,
                    toRemove != null && toRemove.Count > 0 ? toRemove : null);

                string outPath = OutputPathFor(path, output, outDir, targetKey);
                File.WriteAllBytes(outPath, result.Data);

                int kept = realCount - (result.RemovedEffects != null ? result.RemovedEffects.Count : 0);
                row.Effects = kept + " kept";
                row.Params = result.RemovedEffects != null && result.RemovedEffects.Count > 0
                    ? result.RemovedEffects.Count + " removed"
                    : "—";
                row.Animated = targetKey;
                bool hasWarnings = result.Warnings != null && result.Warnings.Count > 0;
                row.Status = hasWarnings ? "WARN" : "OK";
                row.Note = hasWarnings
                    ? JoinNotes(result.Warnings)
                    : "→ " + ((output == OutputMode.Overwrite)
                        ? "overwritten in place"
                        : Path.GetFileName(outPath));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                MarkFailed(row, ex.Message);
            }
            return row;
        }

        /// <summary>Derived outputs overwrite their own previous result;
        /// the one guarded edge is a name collision with the INPUT file
        /// (subfolder mode meeting a file already named *_cs55.ffx) — that
        /// falls back to the suffix form instead of destroying the source.</summary>
        private static string OutputPathFor(string path, OutputMode mode, string outDir, string targetKey)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string suffix = targetKey.Replace(".", "").ToLowerInvariant();
            string candidate;
            switch (mode)
            {
                case OutputMode.Overwrite:
                    return path;
                case OutputMode.Suffix:
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

        private static BatchRow BaseRow(string path)
        {
            var row = new BatchRow
            {
                FileName = Path.GetFileName(path),
                Folder = path,
                Status = "OK",
                Effects = "—",
                Params = "—",
                Animated = "—",
                Size = "—",
                Note = ""
            };
            try { row.Size = FmtSize(new FileInfo(path).Length); }
            catch { /* unreadable metadata — the size stays "—" */ }
            return row;
        }

        private static void MarkFailed(BatchRow row, string message)
        {
            row.Status = "FAILED";
            row.Effects = "—";
            row.Params = "—";
            row.Animated = "—";
            row.Note = message;
        }

        private static string JoinNotes(List<string> notes)
        {
            var text = string.Join(" | ", notes.Take(2));
            return notes.Count > 2 ? text + " …" : text;
        }

        private static string FmtSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return ((double)bytes / (1024 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024) return ((double)bytes / 1024).ToString("0.#") + " KB";
            return bytes + " B";
        }

        // ---------- result handoff ----------

        private void ExportCsv_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_rows.Count == 0) return;
            var dlg = new SaveFileDialog
            {
                Filter = "CSV report (*.csv)|*.csv",
                FileName = "batch_report.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("File,Folder,Status,Effects,Params,Animated,Size,Note");
                foreach (var r in _rows)
                    sb.AppendLine(string.Join(",",
                        Q(r.FileName), Q(r.Folder), Q(r.Status), Q(r.Effects),
                        Q(r.Params), Q(r.Animated), Q(r.Size), Q(r.Note)));
                // UTF-8 with BOM: Excel on Windows reads it correctly
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                ProgressText.Text = "CSV written: " + dlg.FileName;
                LogService.Append("batch report: " + _rows.Count + " row(s) → " + dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this.FindWindow(),
                    "Could not write the CSV:\n" + ex.Message,
                    "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>CSV field quoting: wrap in quotes and double any inner
        /// quote — commas inside file names/notes can never split a cell.</summary>
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private void OpenOut_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastOutputDir) || !Directory.Exists(_lastOutputDir)) return;
            try
            {
                // Process lives in System.Diagnostics — the CS0234 lesson
                Process.Start("explorer.exe", "\"" + _lastOutputDir + "\"");
            }
            catch { /* Explorer refused — nothing sensible to do */ }
        }

        private Window FindWindow() => Window.GetWindow(this);
    }
}
