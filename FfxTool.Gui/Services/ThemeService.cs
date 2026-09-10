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
    /// App-wide theme state: 4 palettes x light/dark — plus a "follow the
    /// Windows theme" switch that resolves the mode from the OS app-mode
    /// personalization on every apply and keeps listening while it runs —
    /// persisted to %APPDATA%\FFXCompatibilityTool\appearance.json (same
    /// file + key names the WinForms version used, so existing settings
    /// carry over; the follow key is new and simply defaults to off).
    /// Applying swaps the merged color dictionary — every DynamicResource
    /// in the UI re-themes instantly.
    /// </summary>
    public static class ThemeService
    {
        public static Md3Mode Mode { get; private set; } = Md3Mode.Light;
        public static Md3Palette Palette { get; private set; } = Md3Palette.Teal;

        /// <summary>When true, Mode is not stored — it is resolved from the
        /// Windows personalization on every apply (and on system changes).
        /// The stored file keeps the last resolved mode so old builds and
        /// old files stay readable.</summary>
        public static bool FollowSystem { get; private set; }

        public static event Action Changed;

        [DataContract(Namespace = "")]
        private class Stored
        {
            [DataMember(Name = "mode")] public string Mode;
            [DataMember(Name = "palette")] public string Palette;
            [DataMember(Name = "follow")] public bool Follow;
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
                            FollowSystem = s.Follow;
                        }
                }
            }
            catch { /* defaults on any read failure */ }
            Apply(Mode, Palette, save: false);
            ListenForSystemThemeChanges();
        }

        public static void Apply(Md3Mode mode, Md3Palette palette, bool save = true)
        {
            ApplyCore(mode, palette, FollowSystem, save);
        }

        /// <summary>Turns "follow the Windows theme" on or off. When on, the
        /// mode argument is ignored — the OS app-mode decides.</summary>
        public static void ApplyWithSystem(bool followSystem, Md3Mode mode, Md3Palette palette, bool save = true)
        {
            ApplyCore(mode, palette, followSystem, save);
        }

        private static void ApplyCore(Md3Mode mode, Md3Palette palette, bool followSystem, bool save)
        {
            FollowSystem = followSystem;
            if (followSystem) mode = SystemMode();
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
                            Palette = Palette.ToString(),
                            Follow = FollowSystem
                        });
                }
                catch { /* best-effort save */ }
            }

            Changed?.Invoke();
        }

        /// <summary>The Windows personalization "app mode"
        /// (HKCU\...\Themes\Personalize\AppsUseLightTheme — 0 means dark
        /// apps). A missing key (Win7 has no personalization page) or any
        /// read failure falls back to Light.</summary>
        public static Md3Mode SystemMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (v is int i) return i == 0 ? Md3Mode.Dark : Md3Mode.Light;
                }
            }
            catch { /* locked registry or exotic OS — Light is the default */ }
            return Md3Mode.Light;
        }

        /// <summary>While following the system, a live flip of the Windows
        /// app-mode re-applies immediately (UserPreferenceChanged fires with
        /// the General category on exactly this change). Subscribed once,
        /// best-effort: a SystemEvents failure must never break themes.</summary>
        private static void ListenForSystemThemeChanges()
        {
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (!FollowSystem || e.Category != Microsoft.Win32.UserPreferenceCategory.General)
                        return;
                    // SystemEvents raises on a broadcast thread — resource
                    // dictionaries are UI-thread property, so marshal
                    var app = Application.Current;
                    if (app == null) return;
                    app.Dispatcher.Invoke((Action)(() =>
                        ApplyCore(Mode, Palette, followSystem: true, save: false)));
                };
            }
            catch { /* live following is cosmetic — polling-free fallback is simply restart */ }
        }
    }
}
