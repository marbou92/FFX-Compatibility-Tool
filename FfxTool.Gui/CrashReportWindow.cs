using System;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// The app's problem-report dialog — the visible half of the crash
    /// system: the global handlers in App catch, log and recover, this
    /// window SHOWS what happened (what the app was doing, the exception
    /// chain with the failing method, and where the full log lives).
    ///
    /// Built entirely in code with system colors and the standard window
    /// chrome ON PURPOSE: if the theming, the resource dictionary or the
    /// custom chrome themselves are what crashed, this dialog must still
    /// come up — including on Windows 7, the app's oldest supported host.
    /// </summary>
    public class CrashReportWindow : Window
    {
        public CrashReportWindow(string context, string report)
        {
            Title = "FFX Compatibility Tool — problem report";
            Width = 660;
            Height = 500;
            MinWidth = 480;
            MinHeight = 340;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = SystemColors.ControlBrush;

            var header = new TextBlock
            {
                Text = "The app hit a problem but kept running.",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var sub = new TextBlock
            {
                Text = (string.IsNullOrEmpty(context) ? "An unexpected error occurred." : context) +
                       "  The details below name the failing method — copy them into a bug report, or attach the log file.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SystemColors.GrayTextBrush,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var details = new System.Windows.Controls.TextBox
            {
                Text = report,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = SystemColors.WindowBrush,
                Foreground = SystemColors.WindowTextBrush,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var copy = NewButton("Copy report", 100);
            var logs = NewButton("Open log folder", 116);
            var close = NewButton("Continue", 96);
            close.IsDefault = true;
            close.IsCancel = true;

            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(details.Text);
                    copy.Content = "Copied";
                }
                catch { /* the clipboard can refuse (RDP, low IL) — the text stays selectable */ }
            };
            logs.Click += (s, e) =>
            {
                try
                {
                    string latest = LogService.LatestExistingLog();
                    if (latest != null)
                        Process.Start("explorer.exe", "/select,\"" + latest + "\"");
                    else
                        Process.Start("explorer.exe", "\"" + LogService.LogsDirectory + "\"");
                }
                catch { /* Explorer refused — nothing sensible */ }
            };
            close.Click += (s, e) => Close();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(copy);
            buttons.Children.Add(logs);
            buttons.Children.Add(close);

            // Grid, not a StackPanel: the details box takes the leftover
            // space (its own box scrolls) and the button row is pinned to
            // the bottom — a long trace must never push Continue out of
            // reach, which a fixed-height StackPanel happily did
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(header, 0);
            Grid.SetRow(sub, 1);
            Grid.SetRow(details, 2);
            Grid.SetRow(buttons, 3);
            root.Children.Add(header);
            root.Children.Add(sub);
            root.Children.Add(details);
            root.Children.Add(buttons);

            Content = root;
            Loaded += (s, e) => details.Focus();
        }

        static System.Windows.Controls.Button NewButton(string label, double minWidth)
        {
            return new System.Windows.Controls.Button
            {
                Content = label,
                MinWidth = minWidth,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 0, 8, 0)
            };
        }

        /// <summary>
        /// One selectable text with everything a bug report needs: what the
        /// app was doing, when, on what, and the full exception chain (type,
        /// message, stack) — no truncation, the box scrolls.
        /// </summary>
        public static string BuildReport(string context, Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine("What happened:  " + (context ?? "an unexpected error"));
            sb.AppendLine("When:  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("App:  FFX Compatibility Tool " + AppInfo.DisplayVersion);
            sb.AppendLine("Running on:  " + Environment.OSVersion.VersionString +
                          "  ·  .NET " + Environment.Version);
            sb.AppendLine();

            var ex = exception;
            int level = 0;
            while (ex != null && level < 8)
            {
                if (level > 0) sb.AppendLine("--- inner exception ---");
                sb.AppendLine("[" + ex.GetType().FullName + "]");
                sb.AppendLine(ex.Message);
                sb.AppendLine(ex.StackTrace ?? "(no stack trace available)");
                ex = ex.InnerException;
                level++;
            }
            return sb.ToString();
        }
    }
}
