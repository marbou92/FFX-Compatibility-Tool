using System;
using System.Diagnostics;
using System.IO;

namespace FfxTool.Gui
{
    /// <summary>
    /// Persists console output to %APPDATA%\FFXCompatibilityTool\logs\ so the
    /// About page's Logs button has something real to reveal. Writes are
    /// best-effort (a locked or read-only profile folder must never crash the
    /// app) and roll per day, keeping each session clearly banner-separated.
    /// </summary>
    public static class LogService
    {
        private static bool _sessionBannerWritten;
        private static string _currentLogPath;

        public static string LogsDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXCompatibilityTool", "logs");
                try { Directory.CreateDirectory(dir); } catch { /* read-only profile */ }
                return dir;
            }
        }

        /// <summary>The file today's lines are being written to (may not exist yet).</summary>
        public static string CurrentLogPath =>
            _currentLogPath ?? (_currentLogPath = Path.Combine(
                LogsDirectory, "console-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"));

        /// <summary>Newest existing log file, or null when logs were never written.</summary>
        public static string LatestExistingLog()
        {
            try
            {
                var dir = new DirectoryInfo(LogsDirectory);
                if (!dir.Exists) return null;
                var newest = null as FileInfo;
                foreach (var f in dir.GetFiles("console-*.log"))
                    if (newest == null || f.LastWriteTime > newest.LastWriteTime)
                        newest = f;
                return newest?.FullName;
            }
            catch { return null; }
        }

        /// <summary>Append one console line to today's log (first write emits a session banner).</summary>
        public static void Append(string line)
        {
            try
            {
                string path = CurrentLogPath;
                if (!_sessionBannerWritten)
                {
                    _sessionBannerWritten = true;
                    File.AppendAllText(path,
                        $"----- session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} · FFX Compatibility Tool v{AppInfo.Version} -----" +
                        Environment.NewLine);
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { /* best-effort — logging must never break conversion */ }
        }

        /// <summary>Show the latest log selected in Explorer; fall back to the folder itself.</summary>
        public static void RevealLatest()
        {
            try
            {
                string latest = LatestExistingLog();
                if (latest != null)
                {
                    Process.Start("explorer.exe", $"/select,\"{latest}\"");
                    return;
                }
                Process.Start("explorer.exe", $"\"{LogsDirectory}\"");
            }
            catch { /* user refused UAC or Explorer blocked — nothing sensible */ }
        }
    }
}
