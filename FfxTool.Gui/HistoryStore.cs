using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FfxTool.Gui
{
    public class RecentFileEntry
    {
        public string path { get; set; }
        public string fileName { get; set; }
        public long bytes { get; set; }
        public DateTime timestamp { get; set; }
        public int effectCount { get; set; }
    }

    public static class HistoryStore
    {
        const int MaxEntries = 5;

        static string StorePath()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(baseDir, "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "recent_files.json");
        }

        public static List<RecentFileEntry> Load()
        {
            try
            {
                var p = StorePath();
                if (!File.Exists(p)) return new List<RecentFileEntry>();
                var json = File.ReadAllText(p);
                var list = JsonSerializer.Deserialize<List<RecentFileEntry>>(json);
                return list ?? new List<RecentFileEntry>();
            }
            catch { return new List<RecentFileEntry>(); }
        }

        public static void Push(string path, int effectCount = 0)
        {
            try
            {
                var list = Load();
                var existing = list.FirstOrDefault(r => r.path.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) list.Remove(existing);
                var info = new FileInfo(path);
                list.Insert(0, new RecentFileEntry
                {
                    path = path,
                    fileName = Path.GetFileName(path),
                    bytes = info.Exists ? info.Length : 0,
                    timestamp = DateTime.Now,
                    effectCount = effectCount
                });
                if (list.Count > MaxEntries) list = list.Take(MaxEntries).ToList();
                File.WriteAllText(StorePath(), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
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
