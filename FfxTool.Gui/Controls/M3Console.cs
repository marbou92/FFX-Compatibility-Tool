using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>
    /// Themed console: colored log levels, copy-to-clipboard, clear,
    /// auto-scroll. Entries remember their level so a palette/dark-mode
    /// swap can re-tint every stored line, not just future ones.
    /// </summary>
    public partial class M3Console : UserControl
    {
        public class LogEntry
        {
            public string Text { get; set; }
            public string Level { get; set; }
            public Brush Brush { get; set; }
        }

        private readonly ObservableCollection<LogEntry> _entries = new ObservableCollection<LogEntry>();

        public M3Console()
        {
            InitializeComponent();
            LogList.ItemsSource = _entries;
            // re-map stored line colors when the theme changes
            ThemeService.Changed += RethemeEntries;
        }

        private Brush BrushFor(string level)
        {
            bool dark = ThemeService.Mode == Md3Mode.Dark;
            switch (level)
            {
                case "SUCCESS": return new SolidColorBrush(dark ? Color.FromRgb(0x8B, 0xD6, 0xA6) : Color.FromRgb(0x2E, 0x7D, 0x4F));
                case "ERROR": return new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0xB4, 0xAB) : Color.FromRgb(0xBA, 0x1A, 0x1A));
                case "WARNING": return new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0xD5, 0x8F) : Color.FromRgb(0x9A, 0x6A, 0x00));
                case "SYSTEM": return new SolidColorBrush(dark ? Color.FromRgb(0xA9, 0xC7, 0xFF) : Color.FromRgb(0x00, 0x5B, 0xBF));
                default: return (Brush)FindResource("B.OnSurfaceVariant");
            }
        }

        public void Log(string line)
        {
            string level = line.StartsWith("[") && line.IndexOf(']', 1) > 0
                ? line.Substring(1, line.IndexOf(']', 1) - 1)
                : "INFO";
            _entries.Add(new LogEntry { Text = line, Level = level, Brush = BrushFor(level) });
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            LogService.Append(line); // persisted for the About → Logs button
        }

        private void RethemeEntries()
        {
            if (_entries.Count == 0) return;
            var snapshot = _entries.ToList();
            _entries.Clear();
            foreach (var entry in snapshot)
            {
                entry.Brush = BrushFor(entry.Level);
                _entries.Add(entry);
            }
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        public void Clear()
        {
            _entries.Clear();
            Log("[SYSTEM] Log cleared.");
        }

        private void Copy_Click(object sender, MouseButtonEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var entry in _entries) sb.AppendLine(entry.Text);
            try { Clipboard.SetText(sb.ToString()); } catch { /* clipboard can be locked */ }
        }

        private void Clear_Click(object sender, MouseButtonEventArgs e) => Clear();
    }
}
