using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FfxTool.Gui
{
    /// <summary>One snapshot of the updater's progress, ready for the UI.</summary>
    public class UpdateProgress
    {
        public string Stage;          // "download" | "verify"
        public long BytesReceived;
        public long TotalBytes;       // -1 when the server didn't say
        public double Fraction;       // 0..1, or -1 = indeterminate
        public string Speed;          // formatted "3.2 MB/s", or null
    }

    /// <summary>
    /// The self-update engine. Flow, end to end:
    ///
    ///   BeginAutoCheck (startup, silent, throttled to one lookup per 6 h,
    ///     skipped entirely on nightly builds — a nightly's numeric version
    ///     stays at the csproj default, so comparing it against releases
    ///     would nag forever) → UpdateChecker's redirect probe (unchanged,
    ///     the releases/latest tag read) → when a newer version exists,
    ///     ChangelogFeed.Fetch downloads the release's changelog.json for
    ///     the notes, the exe file name and the SHA-256.
    ///
    ///   DownloadAsync streams the new exe into a staging folder under
    ///     %LOCALAPPDATA% with progress + cancel, verifies it against the
    ///     feed's SHA-256 (skipped only when the feed carries no hash),
    ///     strips the mark-of-the-web so the restarted build doesn't earn
    ///     a second publisher warning, and stages it under its real name.
    ///
    ///   ApplyUpdate performs the single-exe swap: current → exe.old,
    ///     staged copy → exe, new exe started, this process exits; the
    ///     fresh instance deletes the .old on its next startup
    ///     (App.CleanupLeftoverOldExe). A failed copy puts the old exe
    ///     back, so a half-applied update can never brick the install.
    ///
    /// Settings live in %APPDATA%\FFXCompatibilityTool\updates.json, the
    /// same DataContractJsonSerializer pattern as appearance.json.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>An auto-check discovered a newer version (worker thread).</summary>
        public static event Action<ChangelogEntry> UpdateFound;

        /// <summary>The UI showed the update (offer window or up-to-date
        /// state) — badges and toasts can stand down.</summary>
        public static event Action UpdateSeen;

        /// <summary>Any check completed (worker thread) — the About page's
        /// status line listens.</summary>
        public static event Action<UpdateCheckResult> CheckFinished;

        /// <summary>The confirmed newer version, or null. Carries the feed's
        /// notes + file name + hash when the feed was reachable.</summary>
        public static ChangelogEntry PendingUpdate;

        /// <summary>Human-readable result of the last completed check.</summary>
        public static string LastCheckMessage;

        /// <summary>Status of the last completed check — null before the
        /// first check of the session. The About page's status card reads
        /// this to pick between its up-to-date / available / error faces
        /// without re-parsing the message string.</summary>
        public static UpdateCheckStatus? LastCheckStatus;

        /// <summary>When the last check ran (UTC), or null before the first
        /// one — persisted in updates.json across sessions, so the status
        /// card can show "last checked 2 hours ago" on a fresh start.</summary>
        public static DateTime? LastCheckUtc
        {
            get { return _lastCheckUtc == DateTime.MinValue ? (DateTime?)null : _lastCheckUtc; }
        }

        private const int AutoCheckIntervalHours = 6;

        /// <summary>True while a check is in flight — the status card
        /// shows its checking face when the page opens mid-check.</summary>
        public static bool IsChecking
        {
            get { return System.Threading.Interlocked.CompareExchange(ref _checking, 0, 0) == 1; }
        }

        public static bool AutoCheckEnabled { get; private set; } = true;

        /// <summary>"Include nightlies": checks also consider prereleases
        /// (the rolling nightly), compared by publish date — see
        /// UpdateChecker.CheckPrereleases. Off by default; a nightly build
        /// only ever checks when this is on.</summary>
        public static bool BetaCheckEnabled { get; private set; }

        /// <summary>"Notify when an update is found": gates the corner
        /// toast pill. The Updates tab's dot and the status row always
        /// reflect a pending find — the switch only silences the
        /// announcement.</summary>
        public static bool NotifyEnabled { get; private set; } = true;

        /// <summary>A version the user explicitly skipped — auto and
        /// manual checks stay quiet about exactly this version until the
        /// next one lands. Persisted; the Updates page's "Skip this
        /// version" link writes it.</summary>
        public static string SkipVersion { get; private set; }

        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static int _checking;
        private static volatile bool _cancelRequested;
        private static HttpWebRequest _activeRequest;

        public static bool IsNightlyBuild =>
            (AppInfo.DisplayVersion ?? "")
                .IndexOf("nightly", StringComparison.OrdinalIgnoreCase) >= 0;

        // ---------- settings (updates.json) ----------

        [DataContract(Namespace = "")]
        private class Stored
        {
            [DataMember(Name = "autoCheck")] public bool AutoCheck = true;
            [DataMember(Name = "lastCheckUtc")] public string LastCheckUtc;
            [DataMember(Name = "betaCheck")] public bool BetaCheck;
            [DataMember(Name = "notify")] public bool Notify = true;
            [DataMember(Name = "skipVersion")] public string SkipVersion;
        }

        private static string SettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "updates.json");
        }

        /// <summary>Called once at startup, before any check could run.</summary>
        public static void LoadSettings()
        {
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return;
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.OpenRead(path))
                    if (serializer.ReadObject(fs) is Stored s)
                    {
                        AutoCheckEnabled = s.AutoCheck;
                        BetaCheckEnabled = s.BetaCheck;
                        NotifyEnabled = s.Notify;
                        SkipVersion = string.IsNullOrEmpty(s.SkipVersion) ? null : s.SkipVersion;
                        if (DateTime.TryParse(s.LastCheckUtc,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind,
                                out var t))
                            _lastCheckUtc = t.ToUniversalTime();
                    }
            }
            catch { /* defaults on any read failure */ }
        }

        public static void SetAutoCheck(bool enabled)
        {
            AutoCheckEnabled = enabled;
            SaveSettings();
        }

        public static void SetBetaCheck(bool enabled)
        {
            BetaCheckEnabled = enabled;
            SaveSettings();
        }

        public static void SetNotify(bool enabled)
        {
            NotifyEnabled = enabled;
            SaveSettings();
        }

        /// <summary>Silences exactly this version ("v" prefix stripped, as
        /// everywhere) — a later version automatically un-skips. Pass null
        /// to clear (never exposed; skip dies with the next release).</summary>
        public static void SetSkipVersion(string version)
        {
            SkipVersion = string.IsNullOrWhiteSpace(version)
                ? null
                : version.TrimStart('v', 'V');
            SaveSettings();
        }

        private static bool IsSkipped(string version)
        {
            return SkipVersion != null && version != null &&
                   version.TrimStart('v', 'V') == SkipVersion;
        }

        private static void TouchLastCheck()
        {
            _lastCheckUtc = DateTime.UtcNow;
            SaveSettings();
        }

        private static void SaveSettings()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Stored));
                using (var fs = File.Create(SettingsPath()))
                    serializer.WriteObject(fs, new Stored
                    {
                        AutoCheck = AutoCheckEnabled,
                        BetaCheck = BetaCheckEnabled,
                        Notify = NotifyEnabled,
                        SkipVersion = SkipVersion,
                        LastCheckUtc = _lastCheckUtc == DateTime.MinValue
                            ? null
                            : _lastCheckUtc.ToString("o")
                    });
            }
            catch { /* best-effort save */ }
        }

        // ---------- the checks ----------

        /// <summary>The silent startup check. Never blocks, never pops
        /// anything on its own — a result raises UpdateFound and the
        /// window decides how loudly to announce it.</summary>
        public static void BeginAutoCheck()
        {
            if (!AutoCheckEnabled || (IsNightlyBuild && !BetaCheckEnabled))
            {
                LogService.Append("update auto-check: skipped (" +
                    (!AutoCheckEnabled ? "turned off in Settings"
                        : IsNightlyBuild ? "nightly build, nightlies off"
                        : "nightly build") + ")");
                return;
            }
            if ((DateTime.UtcNow - _lastCheckUtc).TotalHours < AutoCheckIntervalHours) return;
            if (Interlocked.Exchange(ref _checking, 1) == 1) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var result = RunCheck();
                    TouchLastCheck();
                    LastCheckMessage = result.Message;
                    LastCheckStatus = result.Status;
                    LogService.Append("update auto-check: " + result.Message);

                    if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    {
                        // the feed adds notes + file name + hash; when it is
                        // unreachable (or the newest release predates the
                        // feed) the minimal entry keeps the updater working
                        // with the release page as the fallback path — and a
                        // prerelease find (a nightly) has no feed at all, so
                        // it always takes the tag-built entry
                        var entry = IsPrereleaseTag(result.LatestVersion)
                            ? MinimalEntryFromTag(result.LatestVersion)
                            : ChangelogFeed.Fetch() ?? MinimalEntryFromTag(result.LatestVersion);
                        PendingUpdate = entry;
                        UpdateFound?.Invoke(entry);
                    }
                    CheckFinished?.Invoke(result);
                }
                finally { Interlocked.Exchange(ref _checking, 0); }
            });
        }

        /// <summary>The manual check (About button / updater window).</summary>
        public static void CheckNowAsync(Action<UpdateCheckResult, ChangelogEntry> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = CheckNow();
                done(result, result.Status == UpdateCheckStatus.UpdateAvailable
                    ? PendingUpdate
                    : null);
            });
        }

        public static UpdateCheckResult CheckNow()
        {
            var result = RunCheck();
            TouchLastCheck();
            LastCheckMessage = result.Message;
            LastCheckStatus = result.Status;
            LogService.Append("update check: " + result.Message);
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
                PendingUpdate = IsPrereleaseTag(result.LatestVersion)
                    ? MinimalEntryFromTag(result.LatestVersion)
                    : ChangelogFeed.Fetch() ?? MinimalEntryFromTag(result.LatestVersion);
            CheckFinished?.Invoke(result);
            return result;
        }

        /// <summary>Which check runs: with "Include nightlies" on, every
        /// build compares against the plain releases list (prereleases
        /// included) by publish date; otherwise the classic numeric
        /// releases/latest probe. A find for a version the user skipped
        /// reads as up-to-date — the skip dies with the next release.</summary>
        private static UpdateCheckResult RunCheck()
        {
            var result = BetaCheckEnabled
                ? UpdateChecker.CheckPrereleases(BuildOrInstallTimeUtc)
                : UpdateChecker.Check();
            if (result.Status == UpdateCheckStatus.UpdateAvailable &&
                IsSkipped(result.LatestVersion))
            {
                result.Status = UpdateCheckStatus.UpToDate;
                result.Message = "v" + result.LatestVersion.TrimStart('v') +
                                 " is skipped — the next release will speak up again.";
            }
            return result;
        }

        /// <summary>A tag that a numeric compare can't judge — anything
        /// with letters after the version ("0.2.2-nightly.20260918").</summary>
        private static bool IsPrereleaseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            string t = tag.TrimStart('v', 'V');
            foreach (char c in t)
                if (c != '.' && (c < '0' || c > '9')) return true;
            return false;
        }

        /// <summary>A feed-less stand-in built from a release tag: version
        /// + release page only. File stays null, so the UI offers the
        /// release page instead of a blind download.</summary>
        private static ChangelogEntry MinimalEntryFromTag(string tag)
        {
            string version = tag != null ? tag.TrimStart('v', 'V') : "?";
            var entry = new ChangelogEntry
            {
                Version = version,
                Description = "FFX Compatibility Tool " + version +
                              " is out. The release page has the full story.",
                Url = ChangelogFeed.RepoUrl + "/releases/tag/" + tag
            };
            entry.Sections.Add(new ChangelogSection
            {
                Title = "What's New",
                Items =
                {
                    "See the release page for this version's notes — the structured changelog feed wasn't reachable for this release."
                }
            });
            return entry;
        }

        /// <summary>The user engaged with the update UI — drop the rail
        /// badge and the toast.</summary>
        public static void MarkSeen() => UpdateSeen?.Invoke();

        // ---------- download + verify ----------

        private static string StagingDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FFXCompatibilityTool", "update");
        }

        /// <summary>What a previous download left staged ("name · size"),
        /// or null when the staging folder is empty or absent — the
        /// Updates page's "Clear downloaded updates" row reads this.</summary>
        public static string StagedUpdateInfo
        {
            get
            {
                try
                {
                    var dir = new DirectoryInfo(StagingDir());
                    if (!dir.Exists) return null;
                    long bytes = 0;
                    int count = 0;
                    string name = null;
                    foreach (var f in dir.GetFiles())
                    {
                        count++;
                        bytes += f.Length;
                        // the staged exe outlives its .part sibling; prefer
                        // whichever real file is there for the label
                        if (!f.Name.EndsWith(".part") || name == null) name = f.Name;
                    }
                    if (count == 0) return null;
                    return name + " · " + FmtBytes(bytes);
                }
                catch { return null; }
            }
        }

        /// <summary>Deletes everything a download left behind (a staged exe
        /// the user never applied, a crash-orphaned .part). Returns what
        /// was actually removed, for the toast.</summary>
        public static int ClearStagedUpdate()
        {
            int removed = 0;
            try
            {
                var dir = new DirectoryInfo(StagingDir());
                if (!dir.Exists) return 0;
                foreach (var f in dir.GetFiles())
                {
                    try { f.Delete(); removed++; }
                    catch { /* a locked file just stays */ }
                }
            }
            catch { /* unreadable staging folder — report what we got */ }
            return removed;
        }

        /// <summary>When this exe landed on disk (UTC) — build time for a
        /// fresh copy, download/apply time for an updated one. The
        /// prerelease check compares release publish dates against it.</summary>
        public static DateTime BuildOrInstallTimeUtc
        {
            get
            {
                try
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        return File.GetLastWriteTimeUtc(exe);
                }
                catch { /* unreadable path — fall through to "now" */ }
                return DateTime.UtcNow; // nothing can be "older" — stay silent
            }
        }

        public static void CancelDownload()
        {
            _cancelRequested = true;
            try { _activeRequest?.Abort(); } catch { /* already done */ }
        }

        /// <summary>
        /// Downloads entry.File into the staging folder with progress,
        /// verifies SHA-256 when the feed carries one, strips the
        /// mark-of-the-web and renames the file to its final staged name.
        /// Exactly one of success/failure/canceled fires, on the worker
        /// thread — the caller marshals to the UI.
        /// </summary>
        public static void DownloadAsync(ChangelogEntry entry,
            Action<UpdateProgress> progress,
            Action<string> success,      // staged file path
            Action<string> failure,      // human-readable reason
            Action canceled)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string partPath = null;
                try
                {
                    if (entry == null || string.IsNullOrEmpty(entry.File))
                    {
                        failure?.Invoke("This release's file name is unknown — download it from the release page.");
                        return;
                    }

                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    Directory.CreateDirectory(StagingDir());
                    partPath = Path.Combine(StagingDir(), entry.File + ".part");
                    string finalPath = Path.Combine(StagingDir(), entry.File);
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    if (File.Exists(partPath)) File.Delete(partPath);

                    var url = ChangelogFeed.LatestDownloadBase + Uri.EscapeUriString(entry.File);
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = "FFXCompatibilityTool/" + AppInfo.Version;
                    req.Timeout = 15000;          // connect
                    req.ReadWriteTimeout = 30000; // per read — a stall aborts
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                        System.Net.Cache.RequestCacheLevel.BypassCache);
                    _activeRequest = req;
                    _cancelRequested = false;

                    long total = -1, received = 0;
                    var started = DateTime.UtcNow;
                    var lastReport = DateTime.UtcNow;
                    var buffer = new byte[81920];

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var src = resp.GetResponseStream())
                    using (var dst = File.Create(partPath))
                    {
                        total = resp.ContentLength;
                        progress?.Invoke(new UpdateProgress
                        {
                            Stage = "download",
                            BytesReceived = 0,
                            TotalBytes = total,
                            Fraction = total > 0 ? 0 : -1
                        });

                        int read;
                        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            // break, not return: the flow must reach the
                            // post-download cancel check below, which is
                            // what actually reports `canceled` to the UI
                            if (_cancelRequested) break;
                            dst.Write(buffer, 0, read);
                            received += read;

                            var now = DateTime.UtcNow;
                            if ((now - lastReport).TotalMilliseconds >= 100)
                            {
                                double? speed = null;
                                var seconds = (now - started).TotalSeconds;
                                if (seconds > 0.5) speed = received / seconds;
                                progress?.Invoke(new UpdateProgress
                                {
                                    Stage = "download",
                                    BytesReceived = received,
                                    TotalBytes = total,
                                    Fraction = total > 0 ? Math.Min(1.0, received / (double)total) : -1,
                                    Speed = FormatSpeed(speed)
                                });
                                lastReport = now;
                            }
                        }
                    }
                    _activeRequest = null;

                    if (_cancelRequested) { canceled?.Invoke(); return; }

                    // ---- verify ----
                    progress?.Invoke(new UpdateProgress
                    {
                        Stage = "verify",
                        BytesReceived = received,
                        TotalBytes = total,
                        Fraction = total > 0 ? 1.0 : -1
                    });
                    if (!string.IsNullOrEmpty(entry.Sha256))
                    {
                        string actual = Sha256Of(partPath);
                        if (!string.Equals(actual, entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(partPath); } catch { }
                            failure?.Invoke("The downloaded file's SHA-256 didn't match the release's published hash (" +
                                ShortHash(actual) + " vs " + ShortHash(entry.Sha256) +
                                ") — the download was thrown away. Try again; if it keeps failing, fetch the exe from the release page.");
                            return;
                        }
                    }

                    // ---- stage + de-quarantine ----
                    File.Move(partPath, finalPath);
                    partPath = null;
                    try { File.Delete(finalPath + ":Zone.Identifier"); } catch { }
                    success?.Invoke(finalPath);
                }
                catch (Exception ex)
                {
                    _activeRequest = null;
                    if (_cancelRequested)
                    {
                        canceled?.Invoke();
                        return;
                    }
                    failure?.Invoke(FriendlyError(ex));
                }
                finally
                {
                    // a canceled or failed attempt leaves no partial file
                    if (partPath != null) { try { File.Delete(partPath); } catch { } }
                    _activeRequest = null;
                }
            });
        }

        /// <summary>Swaps the freshly staged exe over the running one and
        /// starts the new build. Throws when the swap is impossible
        /// (read-only folder, locked file) — the caller shows it; the old
        /// exe is restored first, so the app keeps working either way.</summary>
        public static void ApplyUpdate(string stagedPath)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                throw new InvalidOperationException("The running exe's path could not be located.");
            if (!File.Exists(stagedPath))
                throw new FileNotFoundException("The staged update is gone — download it again.");

            string dir = Path.GetDirectoryName(exePath);
            string backup = exePath + ".old";

            try { if (File.Exists(backup)) File.Delete(backup); }
            catch { /* Move below will surface the real problem */ }

            File.Move(exePath, backup);
            try
            {
                File.Copy(stagedPath, exePath, true);
                try { Directory.Delete(StagingDir(), true); } catch { }
            }
            catch
            {
                // put the old exe back — a half-swapped install must not exist
                try
                {
                    if (File.Exists(exePath)) File.Delete(exePath);
                    File.Move(backup, exePath);
                }
                catch { /* both operations failed — the error below explains */ }
                throw;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = dir,
                UseShellExecute = true
            });
        }

        /// <summary>Reads one exception as the sentence the update window shows.</summary>
        public static string FriendlyError(Exception ex)
        {
            var web = ex as WebException;
            if (web != null)
            {
                if (web.Status == WebExceptionStatus.RequestCanceled) return "The download was canceled.";
                var http = web.Response as HttpWebResponse;
                if (http != null)
                {
                    if (http.StatusCode == HttpStatusCode.NotFound)
                        return "The release servers don't know that file (HTTP 404) — grab it from the release page.";
                    return "The release servers answered HTTP " + (int)http.StatusCode +
                           " " + http.StatusDescription + ".";
                }
                return "The release servers couldn't be reached — " + web.Message;
            }
            // the classic self-update blockers, named like a human would
            var io = ex as UnauthorizedAccessException;
            if (io != null)
                return "Windows refused the write — the app's folder needs write access. Move the exe somewhere writable (the Desktop, a tools folder) or run once as administrator.";
            return ex.Message;
        }

        // ---------- small formatting helpers ----------

        public static string FmtBytes(double bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024) return (bytes / 1024).ToString("0.#") + " KB";
            return bytes.ToString("0") + " B";
        }

        private static string FormatSpeed(double? bytesPerSecond)
        {
            if (bytesPerSecond == null || bytesPerSecond.Value <= 0) return null;
            return FmtBytes(bytesPerSecond.Value) + "/s";
        }

        private static string Sha256Of(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "?";
            return hash.Length <= 12 ? hash : hash.Substring(0, 12) + "…";
        }
    }
}
