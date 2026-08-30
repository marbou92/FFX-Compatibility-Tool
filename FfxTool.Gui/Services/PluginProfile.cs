using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
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

    /// <summary>
    /// One plugin file the system scan cataloged: where it lives, the vendor
    /// its folder/file name points at, and every match-name-like string
    /// harvested from the binary — a plugin's PiPL resource carries its
    /// match names as plain ASCII, so the exact string a preset asks for
    /// can be found on disk when the plugin is installed.
    /// </summary>
    public class CatalogFile
    {
        public string FilePath;
        public string Vendor;
        public List<string> Names = new List<string>();
    }

    /// <summary>
    /// The FIRST recognition option: what the user's own system reports.
    /// The plugin scan cataloges every .aex under the AE Plug-ins folder —
    /// file names plus match-name candidates read out of the binaries —
    /// and persists them to %APPDATA%\FFXCompatibilityTool\plugin_catalog.txt.
    /// A preset match name found here is installed, full stop. The
    /// aescripts.com / David Torno reference data stays the SECOND option,
    /// via PluginLookup's prefix table and name dataset.
    /// </summary>
    public sealed class PluginCatalog
    {
        private const int MaxFileBytes = 64 * 1024 * 1024; // skip monster packs
        private const int MaxNamesPerFile = 4000;

        private readonly List<CatalogFile> _files = new List<CatalogFile>();
        private readonly Dictionary<string, CatalogFile> _exact =
            new Dictionary<string, CatalogFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CatalogFile> _loose =
            new Dictionary<string, CatalogFile>(StringComparer.Ordinal);

        public static string CatalogPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXCompatibilityTool");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "plugin_catalog.txt");
            }
        }

        public int FileCount { get { return _files.Count; } }

        public int NameCount { get { return _exact.Count; } }

        public static PluginCatalog Load()
        {
            var cat = new PluginCatalog();
            try
            {
                if (File.Exists(CatalogPath))
                {
                    foreach (string line in File.ReadAllLines(CatalogPath))
                    {
                        // path \t vendor \t name<US>name<US>… — paths cannot
                        // contain tabs and candidates cannot contain control
                        // bytes, so this stays unambiguous without a serializer
                        string[] parts = line.Split('\t');
                        if (parts.Length < 3) continue;
                        var f = new CatalogFile
                        {
                            FilePath = parts[0],
                            Vendor = parts[1].Length > 0 ? parts[1] : null
                        };
                        foreach (string n in parts[2].Split('\u001f'))
                            if (n.Length > 0) f.Names.Add(n);
                        cat.Add(f);
                    }
                }
            }
            catch { /* a corrupt catalog degrades to empty — tables still cover */ }
            return cat;
        }

        public void Save()
        {
            try
            {
                using (var w = new StreamWriter(CatalogPath, false, new UTF8Encoding(false)))
                {
                    foreach (var f in _files)
                        w.WriteLine(f.FilePath + "\t" + (f.Vendor ?? "") + "\t" +
                                    string.Join("\u001f", f.Names));
                }
            }
            catch { /* best-effort — a locked file must not take the scan down */ }
        }

        public void Add(CatalogFile f)
        {
            if (f == null || string.IsNullOrEmpty(f.FilePath)) return;
            _files.Add(f);
            foreach (string n in f.Names)
            {
                string norm = Normalize(n);
                // Ownership of a name goes to the MOST SPECIFIC finding
                // file, not whichever file the scan reached first: a match
                // name harvested from a big multi-plugin pack AND from the
                // effect's own .aex must read as that .aex, or the panel
                // claims "installed: SomethingElse.aex" for half the list
                // (the wrong-.aex report).
                if (!_exact.TryGetValue(n, out var prev))
                    _exact[n] = f;
                else if (Beats(f, prev, norm))
                    _exact[n] = f;
                if (norm.Length >= 3)
                {
                    if (!_loose.TryGetValue(norm, out var prevLoose))
                        _loose[norm] = f;
                    else if (Beats(f, prevLoose, norm))
                        _loose[norm] = f;
                }
            }
        }

        /// <summary>Does candidate a claim the normalized name more
        /// specifically than the current owner b? A file NAMED after the
        /// effect wins over one that merely contains it, which wins over a
        /// plain harvest; an equal rank goes to the leaner name list (a
        /// dedicated plugin carries fewer strings than a bundle).</summary>
        private static bool Beats(CatalogFile a, CatalogFile b, string norm)
        {
            int ra = ClaimRank(a, norm), rb = ClaimRank(b, norm);
            if (ra != rb) return ra > rb;
            return a.Names.Count < b.Names.Count;
        }

        /// <summary>How strongly a finding file claims a normalized name:
        /// 3 = the file is NAMED after it ("OpticalFlares.aex" ← "optical
        /// flares"), 2 = the file name contains it ("TrapcodeParticular"
        /// ← "particular"), 1 = the string was merely harvested from the
        /// binary.</summary>
        private static int ClaimRank(CatalogFile f, string norm)
        {
            string stem = Normalize(Path.GetFileNameWithoutExtension(f.FilePath));
            if (stem.Length == 0) return 1;
            if (stem == norm) return 3;
            return stem.Contains(norm) ? 2 : 1;
        }

        /// <summary>
        /// Is this match name on the user's disk? Exact harvested string
        /// first, then a letters-and-digits-only comparison (so "Optical
        /// Flares" finds OpticalFlares.aex), then a containment pass for
        /// long-enough names ("Particular" inside "Trapcode Particular").
        /// Every pass picks the MOST SPECIFIC claimant rather than the
        /// first dictionary hit — several plugins can carry the same or an
        /// overlapping string, and the first-scanned one is rarely the
        /// effect's real home.
        /// </summary>
        public CatalogFile Lookup(string matchName)
        {
            if (string.IsNullOrEmpty(matchName) || _files.Count == 0) return null;
            CatalogFile hit;
            if (_exact.TryGetValue(matchName, out hit)) return hit;
            string norm = Normalize(matchName);
            if (norm.Length == 0) return null;
            if (_loose.TryGetValue(norm, out hit)) return hit;
            if (norm.Length >= 6)
            {
                hit = BestContainment(norm, true);  // harvested key contains the name
                if (hit != null) return hit;
                hit = BestContainment(norm, false); // name contains a harvested key
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Best claimant among the loose keys whose containment
        /// direction matches: strongest claim rank wins, then the leanest
        /// name list, then insertion order (strictly-better keeps the scan
        /// deterministic).</summary>
        private CatalogFile BestContainment(string norm, bool keyContainsName)
        {
            CatalogFile best = null;
            int bestRank = 0, bestCount = 0;
            foreach (var kv in _loose)
            {
                bool match = keyContainsName ? kv.Key.Contains(norm) : norm.Contains(kv.Key);
                if (!match) continue;
                int rank = ClaimRank(kv.Value, norm);
                int count = kv.Value.Names.Count;
                if (best == null || rank > bestRank ||
                    (rank == bestRank && count < bestCount))
                {
                    best = kv.Value; bestRank = rank; bestCount = count;
                }
            }
            return best;
        }

        private static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>
        /// Match-name harvest: every printable-ASCII run that could be a
        /// match name (starts uppercase, word-ish characters, no obvious
        /// system noise). Over-inclusive by design — noise only costs a few
        /// KB of catalog, while a filtered-out real name would cost a
        /// recognition.
        /// </summary>
        public static List<string> HarvestNames(byte[] data)
        {
            var runs = new List<string>();
            if (data == null || data.Length > MaxFileBytes) return runs;
            int start = -1;
            for (int i = 0; i <= data.Length; i++)
            {
                bool printable = i < data.Length && data[i] >= 0x20 && data[i] <= 0x7E;
                if (printable)
                {
                    if (start < 0) start = i;
                }
                else
                {
                    if (start >= 0)
                    {
                        int len = i - start;
                        if (len >= 3 && len <= 64 && runs.Count < MaxNamesPerFile)
                        {
                            string s = Encoding.ASCII.GetString(data, start, len);
                            if (Plausible(s)) runs.Add(s);
                        }
                        start = -1;
                    }
                }
            }
            return runs;
        }

        private static bool Plausible(string s)
        {
            if (s.Length < 3 || s.Length > 64) return false;
            if (!char.IsUpper(s[0])) return false;
            int upper = 0;
            foreach (char c in s)
            {
                bool ok = char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '/' ||
                          c == '.' || c == '-' || c == '\'' || c == '&' || c == '+';
                if (!ok) return false;
                if (char.IsUpper(c)) upper++;
            }
            if (upper == 0) return false;
            string low = s.ToLowerInvariant();
            if (low.Contains(".dll") || low.Contains(".exe") || low.Contains(".aex") ||
                low.Contains("http") || low.Contains("kernel") || low.Contains("microsoft") ||
                low.Contains("msvc") || low.Contains("opengl") || low.Contains("copyright"))
                return false;
            return true;
        }
    }

    /// <summary>
    /// The recognition chain, in the user's order: the system scan catalog
    /// FIRST (ground truth about this machine — an exact or shape match on
    /// disk reads as Installed, with the finding file's name), then the
    /// reference tables (prefix table → CC* rule → David Torno's dataset).
    /// Only names no source knows stay unrecognized, labeled honestly.
    /// </summary>
    public static class PluginRecognition
    {
        private static PluginCatalog _catalog;
        private static bool _catalogTried;

        public static PluginCatalog Catalog
        {
            get
            {
                if (!_catalogTried)
                {
                    _catalogTried = true;
                    _catalog = PluginCatalog.Load();
                }
                return _catalog;
            }
        }

        /// <summary>Drops the cached catalog so the next lookup re-reads the
        /// file the scan just rewrote.</summary>
        public static void ResetCatalog()
        {
            _catalog = null;
            _catalogTried = false;
        }

        public static PluginMatch Resolve(string matchName, List<PluginTableEntry> table, List<EffectNameEntry> names)
        {
            var hit = Catalog.Lookup(matchName);
            if (hit != null)
            {
                // the catalog proves it's installed; the tables only lend a
                // vendor label when the finding file's own names didn't name
                // one (stock effects read "Adobe — installed: …" instead of
                // "your system — installed: …")
                string vendor = hit.Vendor;
                if (vendor == null)
                    vendor = PluginLookup.Resolve(matchName, table)?.Vendor;
                return new PluginMatch
                {
                    MatchName = matchName,
                    Vendor = vendor,
                    Suite = Path.GetFileName(hit.FilePath),
                    PrefixMatched = null,
                    Confirmed = true,
                    Installed = true
                };
            }
            return PluginLookup.Resolve(matchName, table, names);
        }
    }
}
