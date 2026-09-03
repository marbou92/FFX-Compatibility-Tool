using System;
using System.IO;
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
    /// The tiniest possible update check. The repository root carries a
    /// one-line VERSION.txt (served raw by GitHub for the default branch);
    /// this class fetches that one file on a worker thread and compares it
    /// against the running build. No telemetry, no installers, no payload,
    /// no phone-home beyond the single GET — the user decides what to do
    /// with the answer.
    /// </summary>
    public static class UpdateChecker
    {
        // GitHub serves the raw bytes of the default branch at this stable
        // URL; bumping the version for a new round is a one-line file edit
        // in the same commit as the code.
        private const string VersionUrl =
            "https://raw.githubusercontent.com/marbou92/FFX-Compatibility-Tool/main/VERSION.txt";

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

                var req = (HttpWebRequest)WebRequest.Create(VersionUrl);
                req.Method = "GET";
                req.UserAgent = "FFXCompatibilityTool/" + current;
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.BypassCache);

                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    body = reader.ReadToEnd();

                var latest = (body ?? "").Trim();
                // a 404 page or captive-portal HTML must not parse as a version
                Version v;
                if (latest.Length == 0 || latest.Length > 32 || !TryParseVersion(latest, out v))
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        CurrentVersion = current,
                        Message = "The version file online doesn't look like a version" +
                                  (latest.Length > 0 ? " (got \"" + Truncate(latest, 24) + "\")." : " (empty response).")
                    };

                if (v != null && IsNewer(v, current))
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.UpdateAvailable,
                        CurrentVersion = current,
                        LatestVersion = latest,
                        Message = "Update available — v" + latest + " is out (you're on v" + current + ")."
                    };

                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate,
                    CurrentVersion = current,
                    LatestVersion = latest,
                    Message = "You're up to date — v" + current + " is the latest published version."
                };
            }
            catch (Exception ex)
            {
                string reason = ex.Message;
                var web = ex as WebException;
                var http = web != null ? web.Response as HttpWebResponse : null;
                if (http != null)
                    reason = "HTTP " + (int)http.StatusCode + " " + http.StatusDescription;
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    CurrentVersion = current,
                    Message = "Couldn't check for updates — " + reason
                };
            }
        }

        /// <summary>Strict "is the online version newer" — numeric on every
        /// part, never a string compare (so 1.0.9 &lt; 1.0.31).</summary>
        private static bool IsNewer(Version online, string local)
        {
            Version mine;
            if (!Version.TryParse(local, out mine)) return online.Major > 0;
            return online.CompareTo(mine) > 0;
        }

        /// <summary>Accepts only a plain numeric version ("1.0.31", "v1.0.31").
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
    }
}
