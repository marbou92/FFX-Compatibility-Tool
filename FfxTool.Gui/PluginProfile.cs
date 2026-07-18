using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Port of ffx_gui/profile_store.py. Saves which plugin vendors the
    /// user has installed, persisted to a JSON file in the OS's standard
    /// per-user app-data folder (same %APPDATA% location the Python
    /// version used on Windows) so it survives between launches.
    /// </summary>
    public class PluginProfile
    {
        public HashSet<string> OwnedVendors { get; set; } = new HashSet<string>();
        string _path;

        static string ConfigPath()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(baseDir, "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "plugin_profile.json");
        }

        public static PluginProfile Load()
        {
            var path = ConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<StoredProfile>(json);
                    return new PluginProfile
                    {
                        OwnedVendors = new HashSet<string>(data.owned_vendors ?? new List<string>()),
                        _path = path,
                    };
                }
                catch (Exception)
                {
                    // fall through to a fresh empty profile rather than crash
                }
            }
            return new PluginProfile { _path = path };
        }

        public void Save()
        {
            var data = new StoredProfile { owned_vendors = OwnedVendors.OrderBy(v => v).ToList() };
            File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }

        public List<string> AllKnownVendors(List<PluginTableEntry> table)
        {
            return table.Where(e => e.vendor != "Adobe").Select(e => e.vendor).Distinct().OrderBy(v => v).ToList();
        }

        /// <summary>True/False if we have an opinion, null if the vendor is
        /// unknown (Adobe native, or an unrecognized match-name) — null
        /// means "no missing-plugin warning applies", not "confirmed missing".</summary>
        public bool? Owns(string vendor)
        {
            if (vendor == null || vendor == "Adobe") return null;
            return OwnedVendors.Contains(vendor);
        }

        public void SetOwned(string vendor, bool owned)
        {
            if (owned) OwnedVendors.Add(vendor);
            else OwnedVendors.Remove(vendor);
        }

        class StoredProfile
        {
            public List<string> owned_vendors { get; set; }
        }
    }
}
