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
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FFXCompatibilityTool");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, "crash.log"),
                        DateTime.Now + "\n" + args.Exception + "\n\n");
                }
                catch { }
                MessageBox.Show(args.Exception.Message, "FFX Compatibility Tool",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            ThemeService.Load(); // must run before any window reads theme colors
            new MainWindow().Show();
        }
    }
}
