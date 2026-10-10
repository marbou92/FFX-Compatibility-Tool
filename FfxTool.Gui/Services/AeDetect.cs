using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FfxTool.Core;
using Microsoft.Win32;

namespace FfxTool.Gui
{
    public class AeInstall
    {
        public string Key;      // TargetCatalog key ("cs5.5", "2020", …)
        public string ExePath;  // the AfterFX.exe this version was found at
        public string Via;      // "registry" | "folder" — how it was found
    }

    /// <summary>
    /// Auto-detects the After Effects versions installed on this machine —
    /// the "targets you actually have" pass. Two read-only sources, merged:
    /// Adobe's own registry keys (HKLM\SOFTWARE\Adobe\After Effects\&lt;ver&gt;
    /// → ApplicationPath, probed in BOTH registry views so 32-bit CS-era
    /// entries are seen too) and a filesystem sweep of
    /// "Adobe\Adobe After Effects &lt;token&gt;\Support Files\AfterFX.exe".
    /// Nothing is written, nothing is launched — a probe the Convert page
    /// runs once per process on a worker thread and caches forever after.
    /// </summary>
    internal static class AeDetect
    {
        private static IReadOnlyList<AeInstall> _cache;
        private static readonly object _lock = new object();

        public static IReadOnlyList<AeInstall> Detect()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                var found = new Dictionary<string, AeInstall>(StringComparer.OrdinalIgnoreCase);
                ProbeRegistry(found);
                ProbeFolders(found);
                // TargetCatalog order — chronological, exactly the pickers' order
                _cache = TargetCatalog.All
                    .Where(e => found.ContainsKey(e.Key))
                    .Select(e => found[e.Key]).ToList();
                return _cache;
            }
        }

        private static void Add(Dictionary<string, AeInstall> found, string key, string exe, string via)
        {
            if (!found.ContainsKey(key))
                found[key] = new AeInstall { Key = key, ExePath = exe, Via = via };
        }

        private static void ProbeRegistry(Dictionary<string, AeInstall> found)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var aeRoot = hive.OpenSubKey(@"SOFTWARE\Adobe\After Effects"))
                    {
                        if (aeRoot == null) continue;
                        foreach (var sub in aeRoot.GetSubKeyNames())
                        {
                            var entry = TargetCatalog.All.FirstOrDefault(
                                e => string.Equals(e.AeVersion, sub.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (entry == null) continue;
                            using (var k = aeRoot.OpenSubKey(sub))
                            {
                                // ApplicationPath points at AfterFX.exe (Adobe
                                // sometimes quotes it — trim before testing)
                                string p = k == null ? null : (k.GetValue("ApplicationPath") as string);
                                if (string.IsNullOrWhiteSpace(p)) continue;
                                p = p.Trim().Trim('"');
                                if (File.Exists(p)) Add(found, entry.Key, p, "registry");
                            }
                        }
                    }
                }
                catch { /* an unreadable hive just contributes nothing */ }
            }
        }

        private static void ProbeFolders(Dictionary<string, AeInstall> found)
        {
            // This exe may run 32-bit (Prefer32Bit), so SpecialFolder.ProgramFiles
            // resolves to "Program Files (x86)" under WOW64 — ProgramW6432 is the
            // only handle on the real 64-bit Program Files from in here, and the
            // 64-bit one is where every AE since CS5.5 installs.
            string pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var roots = new[] { pf64, pf, pf86 }
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.Combine(r, "Adobe"))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                string[] dirs = null;
                try
                {
                    if (Directory.Exists(root))
                        dirs = Directory.GetDirectories(root, "Adobe After Effects*");
                }
                catch { dirs = null; }
                if (dirs == null) continue;

                foreach (var dir in dirs)
                {
                    string leaf = Path.GetFileName(dir);
                    const string prefix = "Adobe After Effects ";
                    if (string.IsNullOrEmpty(leaf) ||
                        !leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string token = leaf.Substring(prefix.Length).Trim();
                    var entry = TargetCatalog.All.FirstOrDefault(
                        e => string.Equals(e.FolderToken, token, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;
                    string exe = Path.Combine(dir, "Support Files", "AfterFX.exe");
                    try { if (File.Exists(exe)) Add(found, entry.Key, exe, "folder"); }
                    catch { /* a locked directory just contributes nothing */ }
                }
            }
        }
    }
}
