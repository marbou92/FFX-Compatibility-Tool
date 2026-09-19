using System;
using System.Net;

namespace FfxTool.Gui
{
    /// <summary>Outcome of one update check.</summary>
    public enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Error
    }

    /// <summary>Everything the UI needs to render one update check's answer.</summary>
    public class UpdateCheckResult
    {
        public UpdateCheckStatus Status;
        public string CurrentVersion;
        public string LatestVersion; // null on Error
        public string Message;       // human-readable, UI-ready
    }

    /// <summary>
    /// The tiniest possible update check. The project's own releases page
    /// is the single source of truth: this class resolves the
    /// releases/latest redirect — which always points at the newest full
    /// release (prereleases like the rolling nightly never answer) — and
    /// reads the version out of the redirect target's tag. Publishing a
    /// release IS the announcement; there is no version file to keep in
    /// sync anywhere. No telemetry, no installers, no payload, no
    /// phone-home beyond the single redirect lookup — the user decides
    /// what to do with the answer.
    /// </summary>
    public static class UpdateChecker
    {
        // GitHub answers this URL with a 302 to /releases/tag/<tag> when a
        // full release exists, and with 404 when none does.
        private const string LatestReleaseUrl =
            "https://github.com/marbou92/FFX-Compatibility-Tool/releases/latest";

        /// <summary>
        /// Runs the check on a worker thread; the callback fires on that
        /// same thread, so the caller marshals to the UI dispatcher.
        /// </summary>
        public static void CheckAsync(Action<UpdateCheckResult> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => done(Check()));
        }

        public static UpdateCheckResult Check()
        {
            var current = AppInfo.Version;
            try
            {
                // GitHub requires TLS 1.2; pin it explicitly so Windows 7
                // machines whose OS-level defaults predate it still connect.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var req = (HttpWebRequest)WebRequest.Create(LatestReleaseUrl);
                req.Method = "GET";
                req.AllowAutoRedirect = false; // the redirect IS the answer
                req.UserAgent = "FFXCompatibilityTool/" + current;
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.BypassCache);

                string location;
                using (var resp = (HttpWebResponse)req.GetResponse())
                    location = resp.Headers["Location"];

                // a redirect to the releases LIST (no /tag/ segment) means no
                // full release exists yet — this build is then the newest
                // thing the project offers
                if (location != null && !location.Contains("/releases/tag/"))
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.UpToDate,
                        CurrentVersion = current,
                        Message = "No release has been published yet — this build is current."
                    };

                string tag = TagFromLocation(location);

                // a captive portal or proxy page must not parse as a version
                Version v;
                if (tag == null || !TryParseVersion(tag, out v))
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        CurrentVersion = current,
                        Message = "The release page's answer didn't look like a version tag" +
                                  (string.IsNullOrEmpty(tag) ? "." : " (got \"" + Truncate(tag, 24) + "\").")
                    };

                if (v != null && IsNewer(v, current))
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.UpdateAvailable,
                        CurrentVersion = current,
                        LatestVersion = tag.TrimStart('v', 'V'),
                        Message = "Update available — " + tag + " is out (you're on v" + current + ")."
                    };

                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate,
                    CurrentVersion = current,
                    LatestVersion = tag.TrimStart('v', 'V'),
                    Message = "You're up to date — v" + current + " is the latest published version."
                };
            }
            catch (Exception ex)
            {
                string reason = ex.Message;
                bool noRelease = false;
                var web = ex as WebException;
                var http = web != null ? web.Response as HttpWebResponse : null;
                if (http != null)
                {
                    reason = "HTTP " + (int)http.StatusCode + " " + http.StatusDescription;
                    // no full release published yet — this build is then the
                    // newest thing the project offers (prereleases don't count)
                    noRelease = http.StatusCode == HttpStatusCode.NotFound;
                }
                return new UpdateCheckResult
                {
                    Status = noRelease ? UpdateCheckStatus.UpToDate : UpdateCheckStatus.Error,
                    CurrentVersion = current,
                    Message = noRelease
                        ? "No release has been published yet — this build is current."
                        : "Couldn't check for updates — " + reason
                };
            }
        }

        /// <summary>Extracts the tag from a releases/latest redirect target
        /// ("…/releases/tag/v0.1.0" → "v0.1.0"); null when the header is
        /// missing or not a release-tag URL.</summary>
        private static string TagFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) return null;
            const string marker = "/releases/tag/";
            int i = location.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string tag = Uri.UnescapeDataString(location.Substring(i + marker.Length));
            if (tag.Length == 0 || tag.Length > 32) return null;
            return tag;
        }

        /// <summary>Strict "is the online version newer" — numeric on every
        /// part, never a string compare (so 0.1.9 &lt; 0.1.10).</summary>
        private static bool IsNewer(Version online, string local)
        {
            Version mine;
            if (!Version.TryParse(local, out mine)) return online.Major > 0;
            return online.CompareTo(mine) > 0;
        }

        /// <summary>Accepts only a plain numeric version ("0.1.0", "v0.1.0").
        /// Any letter, space or dash disqualifies the whole string.</summary>
        private static bool TryParseVersion(string text, out Version v)
        {
            v = null;
            var t = text.TrimStart('v', 'V');
            foreach (var c in t)
                if (c != '.' && (c < '0' || c > '9')) return false;
            return Version.TryParse(t, out v);
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        // ---------- the prerelease check ("Include nightlies") ----------

        /// <summary>
        /// The beta-path check: is there a release — the rolling nightly
        /// included — published after this build landed on disk? Nightly
        /// tags carry no comparable version number (the numeric version
        /// stays at the csproj default on purpose), so the comparison is
        /// by PUBLISH DATE against the running exe's own write time: the
        /// updater's File.Copy preserves the source timestamp, a manual
        /// download lands with the download moment, and a fresh build
        /// carries its build time — in every case "the exe is older than
        /// the release" reads honestly as "a newer build exists".
        /// Hits the plain /releases endpoint (newest first, prereleases
        /// included) instead of releases/latest, which never answers with
        /// one. A pre-release find reports its tag; the caller routes it
        /// to the release page, since the feed's exe+hash shape only
        /// exists for full releases.
        /// </summary>
        public static UpdateCheckResult CheckPrereleases(DateTime installedOnUtc)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(
                    "https://github.com/marbou92/FFX-Compatibility-Tool/releases?per_page=1");
                req.Method = "GET";
                req.UserAgent = "FFXCompatibilityTool/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.BypassCache);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var ms = new MemoryStream())
                {
                    resp.GetResponseStream().CopyTo(ms);
                    var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                            string tag = el.TryGetProperty("tag_name", out var t) &&
                                         t.ValueKind == System.Text.Json.JsonValueKind.String
                                ? t.GetString() : null;
                            if (string.IsNullOrEmpty(tag)) continue;

                            DateTime publishedUtc = DateTime.MinValue;
                            if (el.TryGetProperty("published_at", out var pub) &&
                                pub.ValueKind == System.Text.Json.JsonValueKind.String &&
                                DateTime.TryParse(pub.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AssumeUniversal |
                                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                                publishedUtc = dt;

                            string when = publishedUtc > DateTime.MinValue
                                ? publishedUtc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                                : "recently";

                            if (publishedUtc > installedOnUtc)
                                return new UpdateCheckResult
                                {
                                    Status = UpdateCheckStatus.UpdateAvailable,
                                    CurrentVersion = AppInfo.Version,
                                    LatestVersion = tag,
                                    Message = "Update available — " + tag + " was published " + when +
                                              ", after this build."
                                };
                            return new UpdateCheckResult
                            {
                                Status = UpdateCheckStatus.UpToDate,
                                CurrentVersion = AppInfo.Version,
                                LatestVersion = tag,
                                Message = "No newer build than this one — " + tag + " predates it."
                            };
                        }
                    }
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.UpToDate,
                        CurrentVersion = AppInfo.Version,
                        Message = "No releases published yet — this build is current."
                    };
                }
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    CurrentVersion = AppInfo.Version,
                    Message = "Couldn't check for updates — " + ex.Message
                };
            }
        }
    }
}
