using System.Reflection;

namespace FfxTool.Gui
{
    /// <summary>
    /// Single source of truth for the app's display version, so the About
    /// page and the status footers can never drift apart again (they used
    /// to print two different formats). ToString(3) trims .NET's 4-part
    /// assembly version down to the conventional three-part "1.0.0".
    /// </summary>
    public static class AppInfo
    {
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string DisplayVersion => "v" + Version;
    }
}
