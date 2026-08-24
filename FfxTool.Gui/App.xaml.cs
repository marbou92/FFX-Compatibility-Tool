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
                MessageBox.Show(args.Exception.Message, "FFX Compatibility Tool",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            ThemeService.Load(); // must run before any window reads theme colors
            new MainWindow().Show();
        }
    }
}
