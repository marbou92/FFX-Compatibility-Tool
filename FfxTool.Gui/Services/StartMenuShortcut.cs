using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace FfxTool.Gui
{
    /// <summary>
    /// The Start menu shortcut the app manages for itself.
    ///
    /// Why this exists: winget installs this app as a "portable" package —
    /// the exe lands in %LOCALAPPDATA%\Microsoft\WinGet\Packages\… and the
    /// only PATH entry is a symlink in WinGet\Links. Winget creates NO Start
    /// menu entry for portable packages (microsoft/winget-cli#2299), and a
    /// shortcut made by hand through that Links symlink arrives broken: the
    /// Start menu cannot resolve icons (or reliably launch) through a
    /// symlinked target, so the app shows up in "All apps" as a blank tile.
    /// The exe itself is not the problem — it carries the full 16→256px
    /// icon set; the shortcut plumbing around it is.
    ///
    /// This service owns one real shortcut: the target is the symlink-
    /// resolved exe (never WinGet's Links link), and the icon is pinned
    /// explicitly to that exe with IconLocation, so the tile always paints.
    /// It detects when an existing shortcut went stale (a winget upgrade
    /// renames the versioned exe) or blank (icon empty / through a link)
    /// and rebuilds it; the silent startup pass only ever repairs a
    /// shortcut that already exists — creating one from nothing stays the
    /// user's click on the About row.
    /// </summary>
    public static class StartMenuShortcut
    {
        public enum ShortcutState { Missing, Healthy, Broken }

        private const string ShortcutName = "FFX Compatibility Tool.lnk";

        /// <summary>Where the app's Start menu shortcut lives (the current
        /// user's Programs folder — no elevation needed, per-user icon).</summary>
        public static string ShortcutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", ShortcutName);

        /// <summary>Classifies the shortcut without changing anything.
        /// Anything it cannot verify reads as Broken — the repair writes a
        /// known-good shape, so over-eager repairs are safe.</summary>
        public static ShortcutState Detect()
        {
            if (!File.Exists(ShortcutPath)) return ShortcutState.Missing;
            try
            {
                object sc = OpenShortcut(ShortcutPath);
                string target = Get(sc, "TargetPath") as string;
                string icon = Get(sc, "IconLocation") as string;

                // healthy = launches from a real file AND paints from a real
                // file. Every failure mode Windows renders as a blank tile:
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                    return ShortcutState.Broken;                    // target gone (winget upgrade renamed the exe)
                if (IsLink(target))
                    return ShortcutState.Broken;                    // target is a symlink/junction — Start cannot resolve it
                string iconFile = IconFileOf(icon);
                if (string.IsNullOrWhiteSpace(iconFile) || !File.Exists(iconFile))
                    return ShortcutState.Broken;                    // no explicit icon → inherits the (link) target's blankness
                if (IsLink(iconFile))
                    return ShortcutState.Broken;                    // icon pinned through a link — the original blank-tile trap
                return ShortcutState.Healthy;
            }
            catch
            {
                return ShortcutState.Broken; // unreadable lnk → rebuild
            }
        }

        /// <summary>Creates or rebuilds the shortcut: target = the real exe
        /// (symlinks resolved), icon = that exe, pinned explicitly. Throws
        /// on failure — the caller owns the message.</summary>
        public static void CreateOrUpdate()
        {
            string exe = RealExePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                throw new InvalidOperationException("The running exe's path could not be located.");

            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
            object sc = OpenShortcut(ShortcutPath);
            Set(sc, "TargetPath", exe);
            Set(sc, "WorkingDirectory", Path.GetDirectoryName(exe));
            Set(sc, "IconLocation", exe + ",0");
            Set(sc, "Description", "FFX Compatibility Tool v" + AppInfo.Version +
                " — After Effects preset converter");
            Call(sc, "Save");
        }

        /// <summary>Deletes the shortcut. No-op when absent.</summary>
        public static void Remove()
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        }

        /// <summary>Silent startup pass: a shortcut that EXISTS but can no
        /// longer launch or paint its icon gets rebuilt. Never creates one
        /// from nothing — presence is the user's opt-in. Cosmetic by
        /// definition: every failure is swallowed.</summary>
        public static void RepairIfBroken()
        {
            try
            {
                if (Detect() == ShortcutState.Broken) CreateOrUpdate();
            }
            catch { /* cosmetic repair — never let it tint the startup */ }
        }

        // ---------- internals ----------

        /// <summary>The running exe's real path: the assembly location with
        /// any symlink chain resolved, so a launch through WinGet's Links
        /// link still produces a target the Start menu can open and paint.</summary>
        private static string RealExePath()
        {
            string exe;
            var entry = Assembly.GetEntryAssembly();
            if (entry != null) exe = entry.Location;
            else exe = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exe)) return null;
            string real = ResolveFinalPath(exe);
            return string.IsNullOrEmpty(real) ? exe : real;
        }

        /// <summary>True when the path reaches the same file through a link
        /// (symlink or junction): its final path differs from the given one.</summary>
        private static bool IsLink(string path)
        {
            try
            {
                string final = ResolveFinalPath(path);
                if (string.IsNullOrEmpty(final)) return false; // can't tell → don't claim broken on this rule
                string full = Path.GetFullPath(path);
                return !string.Equals(final.TrimEnd('\\'), full.TrimEnd('\\'),
                                      StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>"C:\dir\app.exe,0" → "C:\dir\app.exe" (paths may contain
        /// commas, so the split uses the last comma that parses as an index).</summary>
        private static string IconFileOf(string iconLocation)
        {
            if (string.IsNullOrWhiteSpace(iconLocation)) return null;
            string icon = iconLocation.Trim();
            int i = icon.LastIndexOf(',');
            if (i > 0 && int.TryParse(icon.Substring(i + 1).Trim(), out _))
                return icon.Substring(0, i).Trim();
            return icon;
        }

        // ---- WScript.Shell over reflection: no interop assemblies, keeps
        //      the single-exe merge untouched (IShellLink+ComTypes would
        //      drag in Microsoft.CSharp/interop weight for one task) ----

        private static object OpenShortcut(string path)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("Windows Script Host is not available.");
            object shell = Activator.CreateInstance(shellType);
            return Call(shell, "CreateShortcut", path);
        }

        private static object Call(object target, string member, params object[] args) =>
            target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

        private static object Get(object target, string member) =>
            target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

        private static void Set(object target, string member, object value) =>
            target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, new[] { value });

        // ---- Win32: resolve the final path through any symlink chain ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess,
            uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandleW(IntPtr hFile,
            StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>The true path of <paramref name="path"/> with every
        /// symlink and junction resolved, or null when it cannot be opened.
        /// Strips the \\?\ prefix GetFinalPathNameByHandle answers with.</summary>
        private static string ResolveFinalPath(string path)
        {
            const uint ShareAll = 0x7;             // read | write | delete
            const uint OpenExisting = 3;
            const uint BackupSemantics = 0x02000000; // required for directory-ish opens; harmless on files

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = CreateFileW(path, 0, ShareAll, IntPtr.Zero, OpenExisting,
                                     BackupSemantics, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return null;

                var sb = new StringBuilder(1024);
                uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
                if (len <= 0 || len >= (uint)sb.Capacity) return null;
                string final = sb.ToString(0, (int)len);
                if (final.StartsWith(@"\\?\", StringComparison.Ordinal))
                    final = final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
                        ? @"\\" + final.Substring(8)
                        : final.Substring(4);
                return final;
            }
            catch { return null; }
            finally { if (handle != IntPtr.Zero && handle != (IntPtr)(-1)) CloseHandle(handle); }
        }
    }
}
