using System;
using System.Linq;

namespace FfxTool.Core
{
    /// <summary>
    /// The canonical AE target table: internal keys (what Pipeline speaks,
    /// what convert.json remembers, what the CLI accepts), display names
    /// (what the pickers show), AE's own internal version numbers and the
    /// install-folder tokens (what auto-detection matches registry keys
    /// and "Adobe After Effects &lt;token&gt;" folders against). CS5.5
    /// first — the verified downgrade — then every modern release
    /// chronologically. One table, three consumers (ConvertPage, AeDetect,
    /// the CLI): they can never drift apart again.
    /// </summary>
    public static class TargetCatalog
    {
        public class Entry
        {
            public string Key;         // "cs5.5", "2020", … — the pipeline's id
            public string Display;     // "After Effects CS5.5" — the pickers' label
            public string AeVersion;   // "10.5" — AE's own internal version (registry)
            public string FolderToken; // "CS5.5" — the tail of "Adobe After Effects <token>"
        }

        // AeVersion notes: Adobe skipped 19–21 (2021 = 18.0, 2022 = 22.0);
        // CC 2015.3 was 13.6. FolderToken is what the installer actually
        // names the directory ("Adobe After Effects CC 2015.3").
        public static readonly Entry[] All =
        {
            new Entry { Key = "cs5.5",    Display = "After Effects CS5.5",     AeVersion = "10.5", FolderToken = "CS5.5" },
            new Entry { Key = "cs6",      Display = "After Effects CS6",       AeVersion = "11.0", FolderToken = "CS6" },
            new Entry { Key = "cc2013",   Display = "After Effects CC 2013",   AeVersion = "12.0", FolderToken = "CC 2013" },
            new Entry { Key = "cc2014",   Display = "After Effects CC 2014",   AeVersion = "13.0", FolderToken = "CC 2014" },
            new Entry { Key = "cc2015",   Display = "After Effects CC 2015",   AeVersion = "13.5", FolderToken = "CC 2015" },
            new Entry { Key = "cc2015.3", Display = "After Effects CC 2015.3", AeVersion = "13.6", FolderToken = "CC 2015.3" },
            new Entry { Key = "cc2017",   Display = "After Effects CC 2017",   AeVersion = "14.0", FolderToken = "CC 2017" },
            new Entry { Key = "cc2018",   Display = "After Effects CC 2018",   AeVersion = "15.0", FolderToken = "CC 2018" },
            new Entry { Key = "cc2019",   Display = "After Effects CC 2019",   AeVersion = "16.0", FolderToken = "CC 2019" },
            new Entry { Key = "2020",     Display = "After Effects 2020",      AeVersion = "17.0", FolderToken = "2020" },
            new Entry { Key = "2021",     Display = "After Effects 2021",      AeVersion = "18.0", FolderToken = "2021" },
            new Entry { Key = "2022",     Display = "After Effects 2022",      AeVersion = "22.0", FolderToken = "2022" },
            new Entry { Key = "2023",     Display = "After Effects 2023",      AeVersion = "23.0", FolderToken = "2023" },
            new Entry { Key = "2024",     Display = "After Effects 2024",      AeVersion = "24.0", FolderToken = "2024" },
            new Entry { Key = "2025",     Display = "After Effects 2025",      AeVersion = "25.0", FolderToken = "2025" },
        };

        public static string DisplayNameFor(string key) =>
            All.FirstOrDefault(e => e.Key == key)?.Display ?? key;

        /// <summary>Accepts an internal key ("cs5.5", "2020"), a display
        /// name ("After Effects CS6") or a bare folder token ("CS6",
        /// "2018") — the CLI is human-typed, so be generous —
        /// case-insensitively. False for anything the pipeline would
        /// reject, so parsing and converting can never disagree.</summary>
        public static bool TryParseTarget(string text, out string key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            var byKey = All.FirstOrDefault(e => string.Equals(e.Key, t, StringComparison.OrdinalIgnoreCase));
            if (byKey != null) { key = byKey.Key; return true; }
            var byDisplay = All.FirstOrDefault(e => string.Equals(e.Display, t, StringComparison.OrdinalIgnoreCase));
            if (byDisplay != null) { key = byDisplay.Key; return true; }
            var byToken = All.FirstOrDefault(e => string.Equals(e.FolderToken, t, StringComparison.OrdinalIgnoreCase));
            if (byToken != null) { key = byToken.Key; return true; }
            return false;
        }
    }
}
