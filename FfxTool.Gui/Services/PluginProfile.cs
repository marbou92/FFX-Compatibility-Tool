using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Which plugin vendors the user has installed, persisted to
    /// %APPDATA%\FFXCompatibilityTool\plugin_profile.json (same file and
    /// "owned_vendors" key the WinForms version used). Port of
    /// ffx_gui/profile_store.py.
    /// </summary>
    public class PluginProfile
    {
        public HashSet<string> OwnedVendors { get; set; } = new HashSet<string>();
        private readonly string _path;

        [DataContract(Namespace = "")]
        private class StoredProfile
        {
            [DataMember(Name = "owned_vendors")] public List<string> OwnedVendors;
        }

        private static string ConfigPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "plugin_profile.json");
        }

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(StoredProfile));

        public static PluginProfile Load()
        {
            var path = ConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    StoredProfile data;
                    using (var fs = File.OpenRead(path))
                        data = Serializer.ReadObject(fs) as StoredProfile;
                    return new PluginProfile(path,
                        new HashSet<string>(data?.OwnedVendors ?? new List<string>()));
                }
                catch { /* fresh profile on any read failure */ }
            }
            return new PluginProfile(path, new HashSet<string>());
        }

        private PluginProfile(string path, HashSet<string> owned)
        {
            _path = path;
            OwnedVendors = owned;
        }

        public void Save()
        {
            try
            {
                var data = new StoredProfile { OwnedVendors = OwnedVendors.OrderBy(v => v).ToList() };
                using (var fs = File.Create(_path))
                    Serializer.WriteObject(fs, data);
            }
            catch { /* best-effort — don't crash on a locked file */ }
        }

        /// <summary>
        /// Every vendor the UI offers a profile switch for: the prefix
        /// table's vendors plus the AE reference dataset's third-party
        /// vendors (so a preset using e.g. Rowbyte Plexus is recognized and
        /// can be marked owned instead of stuck on "Unknown plugin").
        /// Bundled vendors (Adobe stock, Cycore CC*) never get a switch —
        /// they ship with AE and can't be missing.
        /// </summary>
        public List<string> AllKnownVendors(List<PluginTableEntry> table, List<EffectNameEntry> names = null)
        {
            var vendors = new SortedSet<string>(StringComparer.Ordinal);
            if (table != null)
                foreach (var v in table.Select(e => e?.vendor))
                    if (!string.IsNullOrEmpty(v) && !PluginLookup.IsBundledVendor(v))
                        vendors.Add(v);
            if (names != null)
                foreach (var v in names.Select(e => e?.vendor))
                    if (!string.IsNullOrEmpty(v) && !PluginLookup.IsBundledVendor(v))
                        vendors.Add(v);
            return vendors.ToList();
        }

        /// <summary>True/False if we have an opinion, null when the vendor is
        /// bundled with AE (Adobe stock, Cycore CC*) or unmatched — null means
        /// "no missing-plugin warning applies", not "confirmed missing".</summary>
        public bool? Owns(string vendor)
        {
            if (string.IsNullOrEmpty(vendor) || PluginLookup.IsBundledVendor(vendor)) return null;
            return OwnedVendors.Contains(vendor);
        }

        public void SetOwned(string vendor, bool owned)
        {
            if (owned) OwnedVendors.Add(vendor);
            else OwnedVendors.Remove(vendor);
        }
    }
}
