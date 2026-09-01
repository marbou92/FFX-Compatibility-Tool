using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FfxTool.Gui
{
    public class RecentFileEntry
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public long Bytes { get; set; }
        public DateTime Timestamp { get; set; }
        public int EffectCount { get; set; }
    }

    /// <summary>
    /// Recent-files history, persisted to %APPDATA%\FFXCompatibilityTool\
    /// recent_files.json (same file the WinForms version wrote; timestamps
    /// round-trip as ISO strings so old entries keep working).
    /// </summary>
    public static class HistoryStore
    {
        private const int MaxEntries = 5;

        [DataContract(Namespace = "")]
        private class StoredEntry
        {
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "fileName")] public string FileName;
            [DataMember(Name = "bytes")] public long Bytes;
            [DataMember(Name = "timestamp")] public string Timestamp;
            [DataMember(Name = "effectCount")] public int EffectCount;
        }

        /// <summary>Where the history file lives — a pure path with no side
        /// effects, so the Storage page can probe its size before anything
        /// has ever been written.</summary>
        public static string StorePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXCompatibilityTool", "recent_files.json");
            }
        }

        /// <summary>The history file, with its directory guaranteed to exist.</summary>
        private static string FilePath()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
            return StorePath;
        }

        /// <summary>
        /// Deletes the history file entirely (the Storage settings' "Delete
        /// History"). The next Load reads an empty list and the next Push
        /// starts a fresh one. True when a stored file was actually removed.
        /// </summary>
        public static bool Clear()
        {
            try
            {
                if (!File.Exists(StorePath)) return false;
                File.Delete(StorePath);
                return true;
            }
            catch { return false; } /* a locked file must not take Settings down */
        }

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(List<StoredEntry>));

        public static List<RecentFileEntry> Load()
        {
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return new List<RecentFileEntry>();
                List<StoredEntry> raw;
                using (var fs = File.OpenRead(p))
                    raw = Serializer.ReadObject(fs) as List<StoredEntry> ?? new List<StoredEntry>();
                var list = new List<RecentFileEntry>();
                foreach (var e in raw)
                {
                    DateTime ts = DateTime.Now;
                    DateTime.TryParse(e.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out ts);
                    list.Add(new RecentFileEntry
                    {
                        Path = e.Path,
                        FileName = e.FileName,
                        Bytes = e.Bytes,
                        Timestamp = ts,
                        EffectCount = e.EffectCount
                    });
                }
                return list;
            }
            catch { return new List<RecentFileEntry>(); }
        }

        public static void Push(string path, int effectCount = 0)
        {
            try
            {
                var list = Load();
                list.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                var info = new FileInfo(path);
                list.Insert(0, new RecentFileEntry
                {
                    Path = path,
                    FileName = System.IO.Path.GetFileName(path),
                    Bytes = info.Exists ? info.Length : 0,
                    Timestamp = DateTime.Now,
                    EffectCount = effectCount
                });
                if (list.Count > MaxEntries) list = list.GetRange(0, MaxEntries);

                var raw = new List<StoredEntry>();
                foreach (var e in list)
                    raw.Add(new StoredEntry
                    {
                        Path = e.Path,
                        FileName = e.FileName,
                        Bytes = e.Bytes,
                        Timestamp = e.Timestamp.ToString("o"),
                        EffectCount = e.EffectCount
                    });

                using (var fs = File.Create(FilePath()))
                    Serializer.WriteObject(fs, raw);
            }
            catch { /* best-effort */ }
        }

        public static string TimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 2) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} mins ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 2) return "Yesterday";
            return $"{(int)span.TotalDays} days ago";
        }
    }
}
