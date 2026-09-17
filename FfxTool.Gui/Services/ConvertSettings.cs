using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FfxTool.Gui
{
    /// <summary>
    /// The Convert page's remembered settings — the "last-used" half of the
    /// 0.2.1 round. What comes back next launch:
    ///
    ///   • the single-preset target version (the Target version picker)
    ///   • the batch's multi-target set (the file manager's Targets field)
    ///   • the batch output mode (the five-entry Output combo)
    ///   • the batch checkboxes (include subfolders, remove effects missing
    ///     from the profile) and the single-preset overwrite choice
    ///   • the three last-used folders — opened presets, saved output and
    ///     the browsed preset folder — so the dialogs reopen where the user
    ///     last was instead of the system default
    ///
    /// Lives in %APPDATA%\FFXCompatibilityTool\convert.json, the same
    /// DataContractJsonSerializer pattern as updates.json (the updater) and
    /// appearance.json (the theme). Every read and write is best-effort: a
    /// missing, locked or half-written file just means defaults, never an
    /// error dialog. Known-versions validation happens on the consumer side
    /// (ConvertPage), which owns the version list.
    /// </summary>
    public static class ConvertSettings
    {
        [DataContract(Namespace = "")]
        private class Stored
        {
            [DataMember(Name = "target")] public string Target = "cs5.5";
            [DataMember(Name = "queueTargets")] public List<string> QueueTargets = new List<string>();
            [DataMember(Name = "queueOutput")] public int QueueOutput;
            [DataMember(Name = "queueRecursive")] public bool QueueRecursive = true;
            [DataMember(Name = "queueRemoveMissing")] public bool QueueRemoveMissing;
            [DataMember(Name = "overwriteOriginal")] public bool OverwriteOriginal;
            [DataMember(Name = "lastOpenDir")] public string LastOpenDir;
            [DataMember(Name = "lastSaveDir")] public string LastSaveDir;
            [DataMember(Name = "lastFolderDir")] public string LastFolderDir;
        }

        private static readonly Stored Data = new Stored();
        private static bool _loaded;

        private static string SettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "convert.json");
        }

        /// <summary>Reads convert.json once per process. Idempotent — later
        /// calls are no-ops, so pages can call it defensively.</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return;
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.OpenRead(path))
                    if (serializer.ReadObject(fs) is Stored s)
                    {
                        Data.Target = string.IsNullOrEmpty(s.Target) ? "cs5.5" : s.Target;
                        Data.QueueTargets = s.QueueTargets != null
                            ? new List<string>(s.QueueTargets)
                            : new List<string>();
                        Data.QueueOutput = s.QueueOutput;
                        Data.QueueRecursive = s.QueueRecursive;
                        Data.QueueRemoveMissing = s.QueueRemoveMissing;
                        Data.OverwriteOriginal = s.OverwriteOriginal;
                        Data.LastOpenDir = s.LastOpenDir;
                        Data.LastSaveDir = s.LastSaveDir;
                        Data.LastFolderDir = s.LastFolderDir;
                    }
            }
            catch { /* defaults on any read failure */ }
        }

        public static string Target => Data.Target;
        public static IReadOnlyList<string> QueueTargets => Data.QueueTargets;
        public static int QueueOutputIndex => Data.QueueOutput;
        public static bool QueueRecursive => Data.QueueRecursive;
        public static bool QueueRemoveMissing => Data.QueueRemoveMissing;
        public static bool OverwriteOriginal => Data.OverwriteOriginal;
        public static string LastOpenDir => Data.LastOpenDir;
        public static string LastSaveDir => Data.LastSaveDir;
        public static string LastFolderDir => Data.LastFolderDir;

        public static void SetTarget(string key)
        {
            Data.Target = key;
            Save();
        }

        public static void SetQueueTargets(IEnumerable<string> keys)
        {
            Data.QueueTargets = new List<string>(keys ?? new string[0]);
            Save();
        }

        public static void SetQueueOutput(int index)
        {
            Data.QueueOutput = index;
            Save();
        }

        public static void SetQueueRecursive(bool on)
        {
            Data.QueueRecursive = on;
            Save();
        }

        public static void SetQueueRemoveMissing(bool on)
        {
            Data.QueueRemoveMissing = on;
            Save();
        }

        public static void SetOverwriteOriginal(bool on)
        {
            Data.OverwriteOriginal = on;
            Save();
        }

        public static void RememberOpenDir(string dir)
        {
            Data.LastOpenDir = dir;
            Save();
        }

        public static void RememberSaveDir(string dir)
        {
            Data.LastSaveDir = dir;
            Save();
        }

        public static void RememberFolderDir(string dir)
        {
            Data.LastFolderDir = dir;
            Save();
        }

        public static void Save()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.Create(SettingsPath()))
                    serializer.WriteObject(fs, Data);
            }
            catch { /* best-effort save — a locked profile folder must never take Convert down */ }
        }
    }
}
