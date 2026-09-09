using System.Reflection;

namespace FfxTool.Gui
{
    /// <summary>
    /// Single source of truth for the app's version strings, so the About
    /// page and the status footers can never drift apart again (they used
    /// to print two different formats). ToString(3) trims .NET's 4-part
    /// assembly version down to the conventional three-part "1.0.0".
    /// </summary>
    public static class AppInfo
    {
        /// <summary>The numeric build version — what update checks compare.</summary>
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        /// <summary>
        /// What the user sees: the assembly's informational version stamp.
        /// Release builds carry the plain version ("0.1.0"); a nightly
        /// carries the latest release followed by "-nightly", or a bare
        /// "nightly" when no release exists yet (stamped by the Nightly
        /// workflow's /p:InformationalVersion). Falls back to the numeric
        /// version when no stamp is present.
        /// </summary>
        public static string DisplayVersion
        {
            get
            {
                var info = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string text = info != null ? info.InformationalVersion : null;
                return string.IsNullOrWhiteSpace(text) ? Version : text;
            }
        }
    }
}
