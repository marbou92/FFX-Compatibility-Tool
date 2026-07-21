using System;
using System.IO;
using System.Text.Json;

namespace FfxTool.Gui
{
    /// <summary>
    /// Persists whether the nav rail is collapsed (icon-only) or expanded
    /// (icon+label), the same way PluginProfile persists plugin ownership —
    /// small standalone JSON file in the same app-data folder, kept
    /// separate from ThemeManager's appearance.json since this isn't a
    /// color/theme concern.
    /// </summary>
    public static class NavRailPrefs
    {
        class Stored { public bool collapsed { get; set; } }

        static string Path_()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = System.IO.Path.Combine(baseDir, "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "nav_rail.json");
        }

        public static bool LoadCollapsed()
        {
            try
            {
                var path = Path_();
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Stored>(File.ReadAllText(path))?.collapsed ?? false;
            }
            catch (Exception) { /* default to expanded on any read failure */ }
            return false;
        }

        public static void SaveCollapsed(bool collapsed)
        {
            try { File.WriteAllText(Path_(), JsonSerializer.Serialize(new Stored { collapsed = collapsed })); }
            catch (Exception) { /* best-effort — don't block the UI toggle on a failed save */ }
        }
    }
}
