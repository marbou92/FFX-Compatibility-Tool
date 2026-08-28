using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FfxTool.Core
{
    /// <summary>
    /// One display entry: the human effect name plus the AE menu group a
    /// match name belongs to (e.g. "ADBE Gaussian Blur 2" → "Gaussian Blur",
    /// Blur &amp; Sharpen). Built from David Torno's public "After Effects
    /// Plugin Match Name" spreadsheet (stock AE tables plus contributed
    /// 3rd-party suites); display-only — it never affects compatibility
    /// verdicts, which stay profile-driven.
    /// </summary>
    public class EffectNameEntry
    {
        public string matchName { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string vendor { get; set; }
        public string suite { get; set; }
        public string version { get; set; }
        public bool stock { get; set; }
        public string aeVersion { get; set; }
    }

    internal class EffectNameFile
    {
        public List<EffectNameEntry> effects { get; set; }
    }

    /// <summary>
    /// Match-name → display name/category lookup over data/effect_names.json.
    /// Exact match first, then a case-insensitive pass (some presets mutate
    /// match-name case). Mirrors PluginLookup's contract: a missing or
    /// corrupt table degrades to an empty one with LoadError set — it never
    /// throws, and the UI falls back to the match name itself.
    /// </summary>
    public static class EffectNameLookup
    {
        /// <summary>
        /// Non-null when the last Load call failed (missing, unreadable or
        /// corrupt effect_names.json). The GUI logs each new reason once.
        /// </summary>
        public static string LoadError { get; private set; }

        public static List<EffectNameEntry> Load(string path = null)
        {
            path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "effect_names.json");
            try
            {
                var json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<EffectNameFile>(json);
                LoadError = null;
                return root?.effects ?? new List<EffectNameEntry>();
            }
            catch (Exception ex)
            {
                // the display table must never take a preset load down —
                // degrade to an empty table and say why (no throw, ever)
                LoadError = ex.GetType().Name + ": " + ex.Message;
                return new List<EffectNameEntry>();
            }
        }

        /// <summary>
        /// Resolves a match name to its display entry: exact dictionary hit,
        /// then case-insensitive, else null (caller falls back to the match
        /// name itself, exactly like AE would when nothing is known).
        /// </summary>
        public static EffectNameEntry Resolve(string matchName, List<EffectNameEntry> table)
        {
            if (string.IsNullOrEmpty(matchName) || table == null || table.Count == 0) return null;
            foreach (var e in table)
            {
                if (e != null && e.matchName == matchName) return e;
            }
            foreach (var e in table)
            {
                if (e != null && string.Equals(e.matchName, matchName, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }
    }
}
