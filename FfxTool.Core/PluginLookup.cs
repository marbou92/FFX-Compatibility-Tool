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
    }

    public static class PluginLookup
    {
        public static List<PluginTableEntry> LoadTable(string path = null)
        {
            path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugin_table.json");
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<PluginTableEntry>>(json);
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
            foreach (var entry in table)
            {
                if (matchName.StartsWith(entry.prefix, StringComparison.Ordinal))
                {
                    if (best == null || entry.prefix.Length > best.prefix.Length)
                        best = entry;
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

        public static List<PluginMatch> ResolveMany(IEnumerable<string> matchNames, List<PluginTableEntry> table)
        {
            return matchNames.Select(n => Resolve(n, table)).ToList();
        }
    }
}
