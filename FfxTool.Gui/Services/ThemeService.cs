using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;

namespace FfxTool.Gui
{
    public enum Md3Palette { Teal, Blue, Purple, Orange }
    public enum Md3Mode { Light, Dark }

    /// <summary>
    /// App-wide theme state: 4 palettes x light/dark, persisted to
    /// %APPDATA%\FFXCompatibilityTool\appearance.json (same file + key
    /// names the WinForms version used, so existing settings carry over).
    /// Applying swaps the merged color dictionary — every DynamicResource
    /// in the UI re-themes instantly.
    /// </summary>
    public static class ThemeService
    {
        public static Md3Mode Mode { get; private set; } = Md3Mode.Light;
        public static Md3Palette Palette { get; private set; } = Md3Palette.Teal;

        public static event Action Changed;

        [DataContract(Namespace = "")]
        private class Stored
        {
            [DataMember(Name = "mode")] public string Mode;
            [DataMember(Name = "palette")] public string Palette;
        }

        private static string SettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "appearance.json");
        }

        public static void Load()
        {
            try
            {
                string path = SettingsPath();
                if (File.Exists(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(Stored));
                    using (var fs = File.OpenRead(path))
                        if (serializer.ReadObject(fs) is Stored s)
                        {
                            if (Enum.TryParse(s.Mode, out Md3Mode m)) Mode = m;
                            if (Enum.TryParse(s.Palette, out Md3Palette p)) Palette = p;
                        }
                }
            }
            catch { /* defaults on any read failure */ }
            Apply(Mode, Palette, save: false);
        }

        public static void Apply(Md3Mode mode, Md3Palette palette, bool save = true)
        {
            Mode = mode;
            Palette = palette;

            var colors = new ResourceDictionary
            {
                Source = new Uri($"Themes/{palette}.{mode}.xaml", UriKind.Relative)
            };
            var app = Application.Current;
            if (app != null)
            {
                // index 0 is the reserved color slot (see App.xaml merge order)
                if (app.Resources.MergedDictionaries.Count == 0)
                    app.Resources.MergedDictionaries.Add(colors);
                else
                    app.Resources.MergedDictionaries[0] = colors;
            }

            if (save)
            {
                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(Stored));
                    using (var fs = File.Create(SettingsPath()))
                        serializer.WriteObject(fs, new Stored
                        {
                            Mode = Mode.ToString(),
                            Palette = Palette.ToString()
                        });
                }
                catch { /* best-effort save */ }
            }

            Changed?.Invoke();
        }
    }
}
