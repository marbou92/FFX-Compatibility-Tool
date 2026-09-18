using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FfxTool.Gui
{
    /// <summary>
    /// Small UI preference store — %APPDATA%\FFXCompatibilityTool\ui.json,
    /// same DataContractJsonSerializer pattern as appearance.json /
    /// updates.json. Currently carries one key: the Settings page's last
    /// visited sub-tab, so reopening Settings lands where the user left
    /// it instead of always snapping back to Appearance. Written on every
    /// change, read once at startup (App.OnStartup → UiPrefs.Load).
    /// </summary>
    public static class UiPrefs
    {
        [DataContract(Namespace = "")]
        private class Stored
        {
            [DataMember(Name = "settingsTab")] public int SettingsTab;
        }

        private static Stored _state = new Stored();

        /// <summary>The sub-tab Settings should open on (0..3; values are
        /// clamped by the reader, so a bad file can never crash startup).</summary>
        public static int SettingsTab
        {
            get { return _state.SettingsTab; }
            set
            {
                if (_state.SettingsTab == value) return;
                _state.SettingsTab = value;
                Save();
            }
        }

        private static string SettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "ui.json");
        }

        /// <summary>Called once at startup, before MainWindow is created.</summary>
        public static void Load()
        {
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return;
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.OpenRead(path))
                    if (serializer.ReadObject(fs) is Stored s)
                        _state = s ?? new Stored();
            }
            catch { /* defaults on any read failure */ }
        }

        private static void Save()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.Create(SettingsPath()))
                    serializer.WriteObject(fs, _state);
            }
            catch { /* best-effort save */ }
        }
    }
}
