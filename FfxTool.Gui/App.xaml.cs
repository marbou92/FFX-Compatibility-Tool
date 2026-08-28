using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FfxTool.Gui
{
    public partial class App : Application
    {
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

            ThemeService.Load(); // must run before any window reads theme colors
            new MainWindow().Show();
        }

        /// <summary>
        /// The single funnel every crash goes through: append to crash.log
        /// AND the session log, then show the problem-report window — a
        /// system-styled dialog built without any theme resources, so it
        /// still comes up when the crash IS a theme/resource failure. If
        /// even that fails, fall back to a plain MessageBox; if that fails
        /// too, the log entries remain as the record.
        /// </summary>
        public static void Report(string context, Exception ex)
        {
            if (ex == null) return;

            string crashLogPath = "";
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FFXCompatibilityTool");
                System.IO.Directory.CreateDirectory(dir);
                crashLogPath = System.IO.Path.Combine(dir, "crash.log");
                System.IO.File.AppendAllText(crashLogPath,
                    DateTime.Now + " — " + context + "\n" + ex + "\n\n");
            }
            catch { /* a locked profile folder must never escalate the crash */ }
            LogService.Append("CRASH — " + context + " — " +
                              ex.GetType().FullName + ": " + ex.Message);

            try
            {
                // no UI from a foreign thread (AppDomain handler) — the log is the record
                if (Current == null || !Current.Dispatcher.CheckAccess()) return;

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
                try
                {
                    MessageBox.Show(CrashReportWindow.BuildReport(context, ex),
                        "FFX Compatibility Tool",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { /* no UI possible — the logs carry everything */ }
            }
        }
    }
}
