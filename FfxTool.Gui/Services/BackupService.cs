using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FfxTool.Gui
{
    public class BackupRecord
    {
        public string ZipPath;
        public string Root;
        public int Files;
        public long Bytes;
        public DateTime Created;
    }

    public class RestoreResult
    {
        public bool Ok;
        public int Count;
        public string Root;
        public string Error;
    }

    /// <summary>
    /// The overwrite safety net. Before an overwrite run replaces the
    /// originals, they go into one zip beside the source folder —
    /// FFX-backup-&lt;stamp&gt;.zip — under their relative paths plus a
    /// backup-manifest.json that records the root, so a restore is
    /// self-contained even if the record list is lost. Every backup this
    /// machine makes is recorded to backups.json (the same
    /// %APPDATA%\FFXCompatibilityTool folder as the other stores) and the
    /// Settings → Storage page lists, restores and deletes them.
    /// </summary>
    public static class BackupService
    {
        [DataContract(Namespace = "")]
        private class StoredRecord
        {
            [DataMember(Name = "zipPath")] public string ZipPath;
            [DataMember(Name = "root")] public string Root;
            [DataMember(Name = "files")] public int Files;
            [DataMember(Name = "bytes")] public long Bytes;
            [DataMember(Name = "created")] public string Created;
        }

        [DataContract(Namespace = "")]
        private class Manifest
        {
            [DataMember(Name = "root")] public string Root;
            [DataMember(Name = "created")] public string Created;
            [DataMember(Name = "app")] public string App;
            [DataMember(Name = "files")] public List<string> Files = new List<string>();
        }

        /// <summary>Where the backup records live — a pure path with no side
        /// effects, so Storage can probe before anything was ever written.</summary>
        public static string StorePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FFXCompatibilityTool", "backups.json");

        private static readonly DataContractJsonSerializer ListSerializer =
            new DataContractJsonSerializer(typeof(List<StoredRecord>));

        public static List<BackupRecord> LoadRecords()
        {
            try
            {
                if (!File.Exists(StorePath)) return new List<BackupRecord>();
                List<StoredRecord> raw;
                using (var fs = File.OpenRead(StorePath))
                    raw = ListSerializer.ReadObject(fs) as List<StoredRecord> ?? new List<StoredRecord>();
                var list = new List<BackupRecord>();
                foreach (var r in raw)
                {
                    DateTime ts = DateTime.Now;
                    DateTime.TryParse(r.Created, null, System.Globalization.DateTimeStyles.RoundtripKind, out ts);
                    list.Add(new BackupRecord
                    {
                        ZipPath = r.ZipPath, Root = r.Root,
                        Files = r.Files, Bytes = r.Bytes, Created = ts
                    });
                }
                // newest first — the list reads like a history
                list.Sort((a, b) => b.Created.CompareTo(a.Created));
                return list;
            }
            catch { return new List<BackupRecord>(); }
        }

        private static void SaveRecords(IEnumerable<BackupRecord> records)
        {
            var raw = new List<StoredRecord>();
            foreach (var r in records)
                raw.Add(new StoredRecord
                {
                    ZipPath = r.ZipPath, Root = r.Root,
                    Files = r.Files, Bytes = r.Bytes, Created = r.Created.ToString("o")
                });
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
            using (var fs = File.Create(StorePath))
                ListSerializer.WriteObject(fs, raw);
        }

        /// <summary>Zips the given originals beside `root` (or beside the
        /// first file when root is null) and records the backup. Throws on
        /// any failure — the caller decides whether to overwrite anyway.</summary>
        public static BackupRecord CreateBackup(IList<string> files, string root)
        {
            if (files == null || files.Count == 0)
                throw new ArgumentException("Nothing to back up.");
            if (string.IsNullOrEmpty(root))
                root = Path.GetDirectoryName(files[0]);
            if (string.IsNullOrEmpty(root))
                throw new ArgumentException("No folder to write the backup into.");

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zipPath = Path.Combine(root, "FFX-backup-" + stamp + ".zip");
            int bump = 2;
            while (File.Exists(zipPath))
                zipPath = Path.Combine(root, "FFX-backup-" + stamp + "-" + (bump++) + ".zip");

            var manifest = new Manifest
            {
                Root = root,
                Created = DateTime.Now.ToString("o"),
                App = AppInfo.DisplayVersion,
            };

            long bytes = 0;
            using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    // relative under the root when the file lives under it,
                    // flat by name for a mixed hand-picked queue — a name
                    // collision gets a counter suffix, nothing is dropped
                    string rel = FolderScan.RelUnder(root, f);
                    if (string.IsNullOrEmpty(rel)) rel = Path.GetFileName(f);
                    string entryName = rel.Replace('\\', '/');
                    int n = 2;
                    while (!names.Add(entryName))
                        entryName = rel.Replace('\\', '/') + " (" + (n++) + ")";
                    manifest.Files.Add(entryName);
                    zip.CreateEntryFromFile(f, entryName, CompressionLevel.Optimal);
                    bytes += new FileInfo(f).Length;
                }
                var manEntry = zip.CreateEntry("backup-manifest.json", CompressionLevel.Optimal);
                byte[] manBytes = Encoding.UTF8.GetBytes(ToJson(manifest));
                using (var s = manEntry.Open()) s.Write(manBytes, 0, manBytes.Length);
            }

            var record = new BackupRecord
            {
                ZipPath = zipPath, Root = root,
                Files = files.Count, Bytes = bytes, Created = DateTime.Now
            };
            var records = LoadRecords();
            records.Insert(0, record);
            try { SaveRecords(records); }
            catch { /* the zip exists regardless; a failed record list just hides the row */ }
            return record;
        }

        private static string ToJson(Manifest m)
        {
            var serializer = new DataContractJsonSerializer(typeof(Manifest));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, m);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static Manifest FromJson(byte[] data)
        {
            var serializer = new DataContractJsonSerializer(typeof(Manifest));
            using (var ms = new MemoryStream(data))
                return serializer.ReadObject(ms) as Manifest;
        }

        /// <summary>Extracts the backup back to the root recorded in its
        /// manifest (inside the zip — self-contained), overwriting whatever
        /// replaced the originals. The manifest entry itself is skipped.</summary>
        public static RestoreResult Restore(string zipPath)
        {
            var result = new RestoreResult();
            try
            {
                if (!File.Exists(zipPath))
                {
                    result.Error = "The backup zip no longer exists on disk.";
                    return result;
                }
                using (var fs = File.OpenRead(zipPath))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    Manifest manifest = null;
                    var manEntry = zip.GetEntry("backup-manifest.json");
                    if (manEntry != null)
                        using (var s = manEntry.Open())
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            manifest = FromJson(ms.ToArray());
                        }
                    string root = manifest != null ? manifest.Root : null;
                    if (string.IsNullOrEmpty(root))
                    {
                        result.Error = "The backup has no manifest recording its source folder.";
                        return result;
                    }
                    Directory.CreateDirectory(root);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName == "backup-manifest.json") continue;
                        // sanitized: entries can never escape the recorded root
                        string rel = entry.FullName.Replace('/', '\\');
                        if (rel.StartsWith("\\") || rel.Contains("..")) continue;
                        string dest = Path.Combine(root, rel);
                        string dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using (var src = entry.Open())
                        using (var dst = File.Create(dest))
                            src.CopyTo(dst);
                        result.Count++;
                    }
                    result.Root = root;
                    result.Ok = true;
                }
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        /// <summary>Deletes the zip from disk (when it still exists) and
        /// drops the record. True when something was actually removed.</summary>
        public static bool RemoveRecord(string zipPath)
        {
            bool removed = false;
            try
            {
                if (File.Exists(zipPath)) { File.Delete(zipPath); removed = true; }
            }
            catch { return false; } // a locked zip — the record stays, retry later
            var records = LoadRecords();
            int before = records.Count;
            records.RemoveAll(r => string.Equals(r.ZipPath, zipPath, StringComparison.OrdinalIgnoreCase));
            if (records.Count != before)
            {
                try { SaveRecords(records); }
                catch { }
                removed = true;
            }
            return removed;
        }
    }
}
