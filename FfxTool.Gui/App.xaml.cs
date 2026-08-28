using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FfxTool.Gui
{
    public partial class App : Application
    {
        // ---- crash-funnel state (static: Report is static) ----
        // WPF re-throws a layout/render failure on EVERY dispatcher pass,
        // so without a dedup guard one bad row would stack problem-report
        // window upon problem-report window — the app would look far more
        // broken than it is. Rule: log every occurrence, show at most one
        // dialog per burst (same exception within 3 seconds), and never
        // show two at once.
        private static bool _showingReport;
        private static string _lastSignature = "";
        private static DateTime _lastReportAtUtc = DateTime.MinValue;
        private static int _repeatCount;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ---- the crash system ----
            // 1. UI-thread exceptions: log, show the problem report, recover.
            DispatcherUnhandledException += (s, args) =>
            {
                Report("the app hit an unexpected error", args.Exception);
                args.Handled = true;
            };
            // 2. Non-UI thread / terminal exceptions: still leave a readable
            //    trace behind even if the process is going down.
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Report(args.IsTerminating
                            ? "a background error occurred — the app must close"
                            : "a background error occurred",
                       args.ExceptionObject as Exception);

            try
            {
                ThemeService.Load(); // must run before any window reads theme colors
                new MainWindow().Show();
            }
            catch (Exception startup)
            {
                // a startup failure must leave the same readable trace and
                // visible report as any other crash — never a silent exit
                Report("the app failed to start", startup);
                Shutdown(1);
            }
        }

        /// <summary>
        /// The single funnel every crash goes through. In order:
        /// 1. Append the FULL trace to crash.log AND the session log —
        ///    repeat bursts are counted in the log, never dropped.
        /// 2. Show the problem-report window — a system-styled dialog built
        ///    without any theme resources, so it still comes up when the
        ///    crash IS a theme/resource failure — at most one per burst,
        ///    always on the UI thread (a foreign-thread report is
        ///    marshalled over; while the process is dying a plain
        ///    MessageBox is attempted instead).
        /// 3. If even that fails, the log entries remain as the record.
        /// </summary>
        public static void Report(string context, Exception ex)
        {
            if (ex == null) return;

            // (1) dedup: the same exception within 3 s is the dispatcher
            // hitting the same throw on another layout pass — count it, log
            // it, don't restack the dialog
            string signature = ex.GetType().FullName + "|" + ex.Message;
            bool suppressDialog;
            if (signature == _lastSignature &&
                (DateTime.UtcNow - _lastReportAtUtc).TotalSeconds < 3.0)
            {
                _repeatCount++;
                suppressDialog = true;
            }
            else
            {
                _lastSignature = signature;
                _lastReportAtUtc = DateTime.UtcNow;
                _repeatCount = 0;
                suppressDialog = false;
            }

            string crashLogPath = "";
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FFXCompatibilityTool");
                System.IO.Directory.CreateDirectory(dir);
                crashLogPath = System.IO.Path.Combine(dir, "crash.log");
                System.IO.File.AppendAllText(crashLogPath,
                    DateTime.Now + " — " + context +
                    (suppressDialog ? " (repeat #" + _repeatCount + ")" : "") +
                    "\n" + ex + "\n\n");
            }
            catch { /* a locked profile folder must never escalate the crash */ }

            LogService.Append("CRASH — " + context + " — " +
                              ex.GetType().FullName + ": " + ex.Message +
                              (suppressDialog
                                  ? "  (repeated x" + (_repeatCount + 1) +
                                    " — report dialog suppressed; full trace in crash.log)"
                                  : ""));
            // the stack lands in the session log too, so About → Logs tells
            // the whole story without ever opening %LOCALAPPDATA%
            LogService.Append(ex.ToString());

            if (suppressDialog || _showingReport) return;

            var ui = Current != null ? Current.Dispatcher : null;
            if (ui == null) return; // no dispatcher yet — the logs carry everything

            if (ui.CheckAccess())
            {
                ShowReportDialog(context, ex, crashLogPath);
            }
            else
            {
                try
                {
                    // foreign thread (the AppDomain handler): marshal to the
                    // UI thread so the dialog is owned and pumped correctly
                    ui.Invoke((Action)(() => ShowReportDialog(context, ex, crashLogPath)));
                }
                catch
                {
                    TryMessageBoxFallback(context, ex, crashLogPath);
                }
            }
        }

        private static void ShowReportDialog(string context, Exception ex, string crashLogPath)
        {
            if (_showingReport) return; // re-entrancy: never stack dialogs
            _showingReport = true;
            try
            {
                string report = CrashReportWindow.BuildReport(context, ex);
                if (crashLogPath != "")
                    report += "\n\nFull trace saved to:\n" + crashLogPath;

                var win = new CrashReportWindow(context, report);
                if (Current.MainWindow != null && Current.MainWindow.IsLoaded)
                    win.Owner = Current.MainWindow;
                win.ShowDialog();
            }
            catch
            {
                TryMessageBoxFallback(context, ex, crashLogPath);
            }
            finally
            {
                _showingReport = false;
            }
        }

        private static void TryMessageBoxFallback(string context, Exception ex, string crashLogPath)
        {
            try
            {
                string report = CrashReportWindow.BuildReport(context, ex);
                if (crashLogPath != "")
                    report += "\n\nFull trace saved to:\n" + crashLogPath;
                MessageBox.Show(report, "FFX Compatibility Tool",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* no UI possible — the logs carry everything */ }
        }
    }
}
