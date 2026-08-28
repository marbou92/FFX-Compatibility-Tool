using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FfxTool.Core
{
    /// <summary>
    /// Plugin match-name lookup: resolve an effect's match-name (e.g.
    /// "S_Sharpen") to a vendor/suite, using the same seed table
    /// (data/plugin_table.json) the Python version uses — this file is
    /// shared verbatim between both, not duplicated/retyped.
    /// </summary>
    public class PluginTableEntry
    {
        public string prefix { get; set; }
        public string vendor { get; set; }
        public string suite { get; set; }
        public bool confirmed { get; set; }
        public string note { get; set; }
    }

    public class PluginMatch
    {
        public string MatchName;
        public string Vendor;
        public string Suite;
        public string PrefixMatched;
        public bool Confirmed;

        /// <summary>True when the identity came from a fallback rule (the
        /// CC* namespace or the AE reference dataset) instead of a verified
        /// table prefix — the UI marks the origin so nothing looks more
        /// certain than it is.</summary>
        public bool Inferred;
    }

    public static class PluginLookup
    {
        /// <summary>
        /// Non-null when the last LoadTable call failed (missing, unreadable
        /// or corrupt plugin_table.json). The GUI logs each new reason; the
        /// compatibility list keeps working either way — every match name
        /// then resolves to "Unknown plugin", which is the honest status
        /// when no table is available.
        /// </summary>
        public static string TableLoadError { get; private set; }

        public static List<PluginTableEntry> LoadTable(string path = null)
        {
            path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugin_table.json");
            try
            {
                var json = File.ReadAllText(path);
                TableLoadError = null;
                return JsonSerializer.Deserialize<List<PluginTableEntry>>(json) ?? new List<PluginTableEntry>();
            }
            catch (Exception ex)
            {
                // the shared seed table must never take a preset load down —
                // degrade to an empty table and say why (no throw, ever)
                TableLoadError = ex.GetType().Name + ": " + ex.Message;
                return new List<PluginTableEntry>();
            }
        }

        /// <summary>
        /// Look up a match-name against the prefix table. Longest-prefix
        /// match wins so e.g. "BCC3Directional Blur" doesn't accidentally
        /// match a shorter, less specific "BCC" entry ahead of a more
        /// specific one.
        /// </summary>
        public static PluginMatch Resolve(string matchName, List<PluginTableEntry> table)
        {
            if (matchName == "ADBE Effect Parade" || matchName == "ADBE End of path sentinel")
            {
                return new PluginMatch
                {
                    MatchName = matchName, Vendor = "Adobe", Suite = "structural marker",
                    PrefixMatched = null, Confirmed = true,
                };
            }

            PluginTableEntry best = null;
            if (table != null && matchName != null)
            {
                foreach (var entry in table)
                {
                    string prefix = entry?.prefix;
                    // a malformed row (null/empty prefix) must not throw —
                    // skip it and keep the longest-prefix rule intact
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (matchName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        if (best == null || prefix.Length > (best.prefix ?? "").Length)
                            best = entry;
                    }
                }
            }

            if (best == null)
                return new PluginMatch { MatchName = matchName, Vendor = null, Suite = null, PrefixMatched = null, Confirmed = false };

            return new PluginMatch
            {
                MatchName = matchName,
                Vendor = best.vendor,
                Suite = best.suite,
                PrefixMatched = best.prefix,
                Confirmed = best.confirmed,
            };
        }

        /// <summary>
        /// Vendors whose plug-ins ship inside After Effects itself — they
        /// can never be "missing" and never get a profile switch. Adobe
        /// builds the stock set; Cycore's CC* effects have shipped with AE
        /// since version 5.0.
        /// </summary>
        public static bool IsBundledVendor(string vendor)
        {
            return vendor == "Adobe" || vendor == "Cycore";
        }

        /// <summary>
        /// Recognition fallback for match names no table prefix claims: the
        /// CC* namespace first (Cycore FX ships with AE), then the AE
        /// reference dataset's vendor field. Returns null when nothing can
        /// be derived — the caller keeps its honest "unrecognized" wording.
        /// Inferred hits carry Inferred=true so the UI can say where the
        /// identity came from.
        /// </summary>
        public static PluginMatch Infer(string matchName, EffectNameEntry nameEntry)
        {
            if (string.IsNullOrEmpty(matchName)) return null;
            if (matchName.StartsWith("CC ", StringComparison.Ordinal) ||
                matchName.StartsWith("CCF", StringComparison.Ordinal))
                return new PluginMatch
                {
                    MatchName = matchName, Vendor = "Cycore", Suite = "Bundled with AE",
                    PrefixMatched = null, Confirmed = true, Inferred = true
                };
            string vendor = nameEntry?.vendor;
            if (string.IsNullOrEmpty(vendor)) return null;
            return new PluginMatch
            {
                MatchName = matchName, Vendor = vendor,
                Suite = !string.IsNullOrEmpty(nameEntry.suite) ? nameEntry.suite : nameEntry.category,
                PrefixMatched = null, Confirmed = false, Inferred = true
            };
        }

        /// <summary>
        /// Full recognition chain — prefix table first, then the fallback
        /// for names no prefix claims. The compatibility list and the
        /// convert flow both resolve through this overload so one match
        /// name can never carry two different vendors.
        /// </summary>
        public static PluginMatch Resolve(string matchName, List<PluginTableEntry> table, List<EffectNameEntry> names)
        {
            var m = Resolve(matchName, table);
            if (m.Vendor != null) return m;
            return Infer(matchName, EffectNameLookup.Resolve(matchName, names)) ?? m;
        }

        public static List<PluginMatch> ResolveMany(IEnumerable<string> matchNames, List<PluginTableEntry> table)
        {
            return matchNames.Select(n => Resolve(n, table)).ToList();
        }
    }
}
