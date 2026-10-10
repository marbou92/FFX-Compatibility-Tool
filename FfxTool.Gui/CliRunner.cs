using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// The command-line face of the app: the same pipeline, no window.
    /// App.OnStartup routes any --convert / --list-targets / --help
    /// invocation here BEFORE any WPF service loads; the calling
    /// terminal's console is attached, conversions print their lines, and
    /// the process exits with a scriptable code:
    ///   0 all conversions succeeded · 1 a conversion failed or nothing
    ///   converted · 2 usage error · 3 unexpected error.
    /// Folder runs write the same conversion-report.csv the GUI writes,
    /// and an overwrite run gets the same backup zip the GUI gets.
    /// </summary>
    internal static class CliRunner
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        private const string UsageText =
            "FFX Compatibility Tool — command line\r\n" +
            "\r\n" +
            "  FFXCompatibilityTool.exe --convert <path> [options]\r\n" +
            "\r\n" +
            "  <path>              one .ffx preset or a folder of presets (subfolders included)\r\n" +
            "\r\n" +
            "options:\r\n" +
            "  --target <version>  one or more targets — keys (cs5.5, cs6, cc2013 … 2025),\r\n" +
            "                      display names (\"After Effects CS6\") or folder tokens\r\n" +
            "                      (\"CS6\"); comma-separated or repeatable; default: cs5.5\r\n" +
            "  --output <mode>     converted | suffix | overwrite | zip | folder\r\n" +
            "                      default: converted\r\n" +
            "  --out <dir>         the output folder for the \"converted\" mode\r\n" +
            "                      (default: a \"converted\" subfolder inside the source)\r\n" +
            "  --remove-missing    also strip effects your saved plugin profile says you don't own\r\n" +
            "  --no-backup         overwrite mode: skip the backup zip of the originals\r\n" +
            "  --quiet             per-file lines suppressed — errors and the summary only\r\n" +
            "  --list-targets      print every valid target and exit\r\n" +
            "  --help              this text\r\n" +
            "\r\n" +
            "output modes:\r\n" +
            "  converted   a \"converted\" subfolder inside the source folder (clean rebuild)\r\n" +
            "  suffix      a <name>_<version>.ffx copy beside each original\r\n" +
            "  overwrite   replace the originals in place — a backup zip is written first\r\n" +
            "              unless --no-backup; backups are listed in Settings → Storage\r\n" +
            "  zip         one ZIP beside the source folder mirroring the subfolders\r\n" +
            "  folder      one folder beside the source folder mirroring the subfolders\r\n" +
            "\r\n" +
            "exit codes:  0 all conversions succeeded · 1 a conversion failed or nothing converted\r\n" +
            "             2 usage error · 3 unexpected error\r\n" +
            "Folder runs write conversion-report.csv next to the derived outputs.";

        public static bool IsCliInvocation(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            switch (args[0].ToLowerInvariant())
            {
                case "--convert":
                case "--list-targets":
                case "--help":
                case "-h":
                case "-?":
                    return true;
                default:
                    return false;
            }
        }

        public static int Run(string[] args)
        {
            try { AttachConsole(ATTACH_PARENT_PROCESS); }
            catch { /* launched without a console (Explorer) — output goes nowhere, codes still work */ }
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { /* a redirected/closed stdout must not take the run down */ }
            try { return Execute(args); }
            catch (Exception ex)
            {
                try
                {
                    Console.Error.WriteLine("error: " + ex.Message);
                    Console.WriteLine();
                }
                catch { }
                return 3;
            }
        }

        private sealed class Options
        {
            public string Path;
            public readonly List<string> Targets = new List<string>();
            public string OutputMode = "converted";
            public string OutDir;
            public bool RemoveMissing;
            public bool Quiet;
            public bool NoBackup;
            // mirror-output surfaces — set in Execute, consumed by OutputPathFor
            public string Root;
            public string Staging;
            public string OutBase;
        }

        private static int UsageError(string message)
        {
            Console.Error.WriteLine("error: " + message);
            Console.Error.WriteLine("Run with --help for the usage text.");
            return 2;
        }

        private static int Execute(string[] args)
        {
            if (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("-?", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write(UsageText);
                Console.WriteLine();
                Console.WriteLine();
                return 0;
            }
            if (args[0].Equals("--list-targets", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var e in TargetCatalog.All)
                    Console.WriteLine("  " + e.Key.PadRight(10) + e.Display);
                Console.WriteLine();
                return 0;
            }

            // ---- parse ----
            var opt = new Options();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--target":
                        if (i + 1 >= args.Length)
                            return UsageError("--target needs a version (try --list-targets)");
                        foreach (var part in args[++i].Split(','))
                        {
                            string p = part.Trim();
                            if (p.Length == 0) continue;
                            string key;
                            if (!TargetCatalog.TryParseTarget(p, out key))
                                return UsageError("Unknown target '" + p + "' — try --list-targets");
                            if (!opt.Targets.Contains(key)) opt.Targets.Add(key);
                        }
                        break;
                    case "--output":
                        if (i + 1 >= args.Length)
                            return UsageError("--output needs a mode (converted, suffix, overwrite, zip, folder)");
                        opt.OutputMode = args[++i].Trim().ToLowerInvariant();
                        break;
                    case "--out":
                        if (i + 1 >= args.Length)
                            return UsageError("--out needs a folder path");
                        opt.OutDir = args[++i];
                        break;
                    case "--remove-missing":
                        opt.RemoveMissing = true;
                        break;
                    case "--quiet":
                    case "-q":
                        opt.Quiet = true;
                        break;
                    case "--no-backup":
                        opt.NoBackup = true;
                        break;
                    default:
                        if (a.StartsWith("-"))
                            return UsageError("Unknown option '" + a + "'");
                        if (opt.Path != null)
                            return UsageError("Only one <path> per run — '" + a + "' is a second one");
                        opt.Path = a;
                        break;
                }
            }
            if (opt.Path == null)
                return UsageError("Nothing to convert — give a .ffx file or a folder: --convert <path>");
            bool isDir = Directory.Exists(opt.Path);
            if (!isDir && !File.Exists(opt.Path))
                return UsageError("No such file or folder: " + opt.Path);
            switch (opt.OutputMode)
            {
                case "converted":
                case "suffix":
                case "overwrite":
                case "zip":
                case "folder":
                    break;
                default:
                    return UsageError("Unknown output mode '" + opt.OutputMode +
                                      "' — converted, suffix, overwrite, zip or folder");
            }
            if (opt.Targets.Count == 0) opt.Targets.Add("cs5.5");
            if (opt.OutputMode == "overwrite" && opt.Targets.Count > 1)
                return UsageError("Overwrite works with a single target — with several, the last " +
                                  "conversion written would erase every other one");

            // ---- gather ----
            var files = isDir
                ? FolderScan.Collect(opt.Path, true)
                : new List<string> { opt.Path };
            if (files.Count == 0)
            {
                Console.WriteLine("No .ffx presets found in '" + opt.Path + "'.");
                return 1;
            }
            string root = isDir ? opt.Path : Path.GetDirectoryName(opt.Path);
            opt.Root = root;
            int conversions = files.Count * opt.Targets.Count;

            // ---- output surfaces (same contracts as the GUI's batch) ----
            string outDir = null;
            string outBase = null;
            string zipPath = null;
            string staging = null;
            if (opt.OutputMode == "converted")
            {
                // clean rebuild: the folder is this tool's own derived output,
                // so wiping it first means the result is exactly this run
                outDir = string.IsNullOrEmpty(opt.OutDir)
                    ? Path.Combine(root ?? ".", "converted")
                    : opt.OutDir;
                try
                {
                    if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                    Directory.CreateDirectory(outDir);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: could not create the output folder: " + ex.Message);
                    return 1;
                }
            }
            else if (opt.OutputMode == "zip" || opt.OutputMode == "folder")
            {
                string baseName = FolderScan.LeafName(root) + " (converted)";
                string parent = Path.GetDirectoryName(root);
                if (string.IsNullOrEmpty(parent)) parent = ".";
                if (opt.OutputMode == "folder")
                {
                    outBase = Path.Combine(parent, baseName);
                    opt.OutBase = outBase;
                    try
                    {
                        if (Directory.Exists(outBase)) Directory.Delete(outBase, true);
                        Directory.CreateDirectory(outBase);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("error: could not create the output folder: " + ex.Message);
                        return 1;
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
                        Console.Error.WriteLine("error: could not create the staging folder: " + ex.Message);
                        return 1;
                    }
                    opt.Staging = staging;
                }
            }

            // ---- the overwrite safety net (same as the GUI's) ----
            if (opt.OutputMode == "overwrite" && !opt.NoBackup)
            {
                try
                {
                    var rec = BackupService.CreateBackup(files, root);
                    Console.WriteLine("Backup: " + rec.ZipPath + " — " + rec.Files + " original(s)");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: the backup could not be written (" + ex.Message + ")");
                    Console.WriteLine("Overwrite aborted — use --no-backup to overwrite without a backup.");
                    return 1;
                }
            }

            // ---- convert ----
            PluginProfile profile = null;
            if (opt.RemoveMissing)
            {
                try { profile = PluginProfile.Load(); }
                catch { profile = null; }
                if (profile == null)
                    Console.WriteLine("warning: no saved plugin profile — --remove-missing has nothing to match against");
            }

            if (!opt.Quiet)
                Console.WriteLine("Converting " + files.Count + " preset" + (files.Count == 1 ? "" : "s") +
                                  " × " + opt.Targets.Count + " target" + (opt.Targets.Count == 1 ? "" : "s") +
                                  " = " + conversions + " conversion" + (conversions == 1 ? "" : "s") + "…");

            int done = 0, ok = 0, warned = 0, failed = 0;
            var report = new List<string[]>
            {
                new[] { "File", "Folder", "Target", "Status", "Kept", "Removed", "Note" }
            };
            foreach (var path in files)
            {
                string name = Path.GetFileName(path);
                string relFull = FolderScan.RelUnder(isDir ? opt.Path : null, path);
                int relSlash = relFull.LastIndexOf('\\');
                string relFolder = relSlash >= 0 ? relFull.Substring(0, relSlash) : "";
                foreach (var targetKey in opt.Targets)
                {
                    done++;
                    string targetLabel = TargetCatalog.DisplayNameFor(targetKey);
                    var r = ConvertOne(path, targetKey, opt, profile);
                    report.Add(new[] { name, relFolder, targetLabel,
                                       !r.Ok ? "FAILED" : r.Warn ? "WARN" : "OK",
                                       r.Kept.ToString(), r.Removed.ToString(), r.Note ?? "" });
                    if (!r.Ok)
                    {
                        failed++;
                        Console.WriteLine("[ERROR] " + name + " → " + targetLabel + " — " + r.Note);
                    }
                    else
                    {
                        ok++;
                        if (r.Warn) warned++;
                        if (!opt.Quiet)
                            Console.WriteLine("[OK] " + name + " → " + targetLabel +
                                              " — " + r.Kept + " effect(s) kept" +
                                              (r.Removed > 0 ? " · " + r.Removed + " removed" : "") +
                                              (r.Warn ? " — warnings: " + r.Note : ""));
                    }
                }
            }

            // ---- finalize the derived outputs (same rules as the GUI) ----
            if (opt.OutputMode == "zip")
            {
                // whatever converted cleanly becomes the ZIP — a partial run
                // contributes less but never a broken archive (the GUI's own
                // contract); nothing converted means no archive at all
                if (ok > 0)
                {
                    WriteReport(staging, report);
                    try
                    {
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        System.IO.Compression.ZipFile.CreateFromDirectory(
                            staging, zipPath,
                            System.IO.Compression.CompressionLevel.Optimal, false);
                        Console.WriteLine("ZIP written: " + zipPath + " — " + ok + " preset(s) + conversion-report.csv");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("error: the ZIP could not be written: " + ex.Message);
                        zipPath = null;
                    }
                }
                else zipPath = null;
                try { Directory.Delete(staging, true); } catch { }
            }
            if (ok == 0)
            {
                // a zero-ok run leaves the derived output an empty shell
                if (opt.OutputMode == "folder" && outBase != null)
                    try { Directory.Delete(outBase, true); } catch { }
                else if (opt.OutputMode == "converted" && outDir != null)
                    try { Directory.Delete(outDir, true); } catch { }
            }
            else if (opt.OutputMode == "folder")
            {
                WriteReport(outBase, report);
            }
            else if (opt.OutputMode == "converted")
            {
                WriteReport(outDir, report);
            }

            string summary;
            if (done == 0) summary = "Nothing was processed.";
            else
            {
                summary = done + " of " + conversions + " conversions — " + ok + " ok" +
                          (warned > 0 ? " · " + warned + " warning" + (warned == 1 ? "" : "s") : "") +
                          (failed > 0 ? " · " + failed + " failed" : "");
                if (opt.OutputMode == "converted" && ok > 0) summary += " — output: " + outDir;
                else if (opt.OutputMode == "suffix" && ok > 0) summary += " — output: beside the originals";
                else if (opt.OutputMode == "overwrite" && ok > 0) summary += " — originals replaced in place";
                else if (opt.OutputMode == "folder" && ok > 0) summary += " — output: " + outBase;
                else if (opt.OutputMode == "zip" && zipPath != null) summary += " — output: " + zipPath;
            }
            Console.WriteLine();
            Console.WriteLine((failed > 0 ? "[FAILED] " : "[SUCCESS] ") + summary);
            Console.WriteLine();
            return failed > 0 ? 1 : 0;
        }

        private class ConvResult
        {
            public bool Ok = true;
            public bool Warn;
            public int Kept;
            public int Removed;
            public string Note = "";
        }

        /// <summary>The proven single-file pipeline, mirroring the GUI's
        /// QueueConvertOne: profile-driven removal only (the CLI has no
        /// hand-toggled checklist), the same output-path rules, the same
        /// verification-before-write guarantee (Pipeline.Convert throws
        /// rather than write anything unclean).</summary>
        private static ConvResult ConvertOne(string path, string targetKey, Options opt, PluginProfile profile)
        {
            var res = new ConvResult();
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var effects = Pipeline.ListEffects(data);

                HashSet<string> toRemove = null;
                if (opt.RemoveMissing && profile != null)
                {
                    var table = PluginLookup.LoadTable();
                    var names = EffectNameLookup.Load();
                    toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var eff in effects.Where(x => !x.IsSentinel))
                    {
                        var match = PluginRecognition.Resolve(eff.MatchName, table, names);
                        if (!match.Installed && profile.Owns(match.Vendor) == false)
                            toRemove.Add(eff.MatchName);
                    }
                }

                var result = Pipeline.Convert(data, targetKey,
                    toRemove != null && toRemove.Count > 0 ? toRemove : null);

                string outPath = OutputPathFor(path, opt, targetKey);
                string outFolder = Path.GetDirectoryName(outPath);
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

        /// <summary>Same rules as the GUI's OutputPathFor: overwrite returns
        /// the source path itself; the suffix mode falls back to a numbered
        /// suffix instead of ever destroying a source file.</summary>
        private static string OutputPathFor(string path, Options opt, string targetKey)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string suffix = targetKey.Replace(".", "").ToLowerInvariant();
            string candidate;
            switch (opt.OutputMode)
            {
                case "overwrite":
                    return path;
                case "suffix":
                    candidate = Path.Combine(Path.GetDirectoryName(path), name + "_" + suffix + ".ffx");
                    break;
                case "zip":
                case "folder":
                {
                    string baseDir = opt.OutputMode == "zip" ? opt.Staging : opt.OutBase;
                    string relFull = FolderScan.RelUnder(opt.Root, path);
                    int relSlash = relFull.LastIndexOf('\\');
                    string relFolder = relSlash >= 0 ? relFull.Substring(0, relSlash) : "";
                    string outName = opt.Targets.Count > 1
                        ? name + "_" + suffix + ".ffx"
                        : Path.GetFileName(path);
                    return Path.Combine(baseDir, relFolder, outName);
                }
                default: // converted
                    candidate = Path.Combine(opt.OutDir, name + "_" + suffix + ".ffx");
                    break;
            }
            if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                candidate = Path.Combine(Path.GetDirectoryName(path), name + "_" + suffix + "_1.ffx");
            return candidate;
        }

        /// <summary>The conversion report: one quoted-CSV row per conversion,
        /// UTF-8 with BOM so Excel reads it straight. Best-effort — a report
        /// failure never fails the run.</summary>
        private static void WriteReport(string dir, List<string[]> rows)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var row in rows)
                    sb.AppendLine(string.Join(",", row.Select(Q)));
                File.WriteAllText(Path.Combine(dir, "conversion-report.csv"),
                                  sb.ToString(), new UTF8Encoding(true));
            }
            catch { /* a locked destination loses the report, not the conversions */ }
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
