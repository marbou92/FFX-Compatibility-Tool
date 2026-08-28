using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FfxTool.Gui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                string logPath = "";
                try
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FFXCompatibilityTool");
                    System.IO.Directory.CreateDirectory(dir);
                    logPath = System.IO.Path.Combine(dir, "crash.log");
                    System.IO.File.AppendAllText(logPath,
                        DateTime.Now + "\n" + args.Exception + "\n\n");
                }
                catch { }

                // type + message + the stack head, not just the message: a
                // crash report that names the failing method is one the next
                // build can actually fix
                var ex = args.Exception;
                string report = ex.GetType().Name + ": " + ex.Message;
                var frames = (ex.StackTrace ?? "").Split('\n');
                var shown = frames.Take(6).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
                if (shown.Count > 0)
                    report += "\n\n" + string.Join("\n", shown.Select(f => "at " + f));
                if (logPath != "")
                    report += "\n\nFull trace saved to:\n" + logPath;

                MessageBox.Show(report, "FFX Compatibility Tool",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            ThemeService.Load(); // must run before any window reads theme colors
            new MainWindow().Show();
        }
    }
}
