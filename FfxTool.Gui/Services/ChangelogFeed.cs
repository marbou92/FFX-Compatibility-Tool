using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FfxTool.Gui
{
    /// <summary>One titled group of bullet lines inside a changelog entry
    /// ("✨ What's New", "Fixes & Improvements", "Support"…).</summary>
    public class ChangelogSection
    {
        public string Title;
        public List<string> Items = new List<string>();
    }

    /// <summary>
    /// One release's notes, machine-readable. This is the vivi-music
    /// changelog.json system: the release workflow parses the curated
    /// Changelog/Stablemd/&lt;version&gt;.md into this shape, attaches it to the
    /// release as an asset, and installed builds download it from the
    /// permanent releases/latest/download/changelog.json URL — no GitHub
    /// API involved, so there is no rate limit to trip over. The schema is
    /// a superset of vivi's (version / description / changelog[{title,
    /// items}]) plus what a self-updating desktop exe needs: the release
    /// page URL, the exe asset's exact file name and its SHA-256.
    /// Unknown keys are ignored on purpose, so the feed can grow without
    /// breaking older builds.
    /// </summary>
    public class ChangelogEntry
    {
        public string Version;      // "0.3.0"
        public string Description;  // the brings-sentence
        public string Date;         // "2026-09-10" or null
        public string Url;          // release page
        public string File;         // exe asset file name (null on the minimal fallback)
        public string Sha256;       // "" or null = verification not possible
        public string ImageUrl;     // hero image (Stablemd "image::" line) or null
        public List<ChangelogSection> Sections = new List<ChangelogSection>();

        /// <summary>Parses a changelog.json document; null when the text
        /// is not a feed we can use (no version, broken JSON…).</summary>
        public static ChangelogEntry Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;

                    var entry = new ChangelogEntry
                    {
                        Version = GetString(root, "version"),
                        Description = GetString(root, "description"),
                        Date = GetString(root, "date"),
                        Url = GetString(root, "url"),
                        File = GetString(root, "file"),
                        Sha256 = GetString(root, "sha256")
                    };
                    // a feed without a version number cannot drive anything
                    if (string.IsNullOrWhiteSpace(entry.Version)) return null;

                    if (root.TryGetProperty("changelog", out var arr) &&
                        arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sec in arr.EnumerateArray())
                        {
                            if (sec.ValueKind != JsonValueKind.Object) continue;
                            var s = new ChangelogSection { Title = GetString(sec, "title") };
                            if (sec.TryGetProperty("items", out var items) &&
                                items.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var it in items.EnumerateArray())
                                    if (it.ValueKind == JsonValueKind.String &&
                                        !string.IsNullOrWhiteSpace(it.GetString()))
                                        s.Items.Add(it.GetString());
                            }
                            if (s.Title != null || s.Items.Count > 0)
                                entry.Sections.Add(s);
                        }
                    }
                    return entry;
                }
            }
            catch { return null; }
        }

        private static string GetString(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }
    }

    /// <summary>
    /// Downloads the release feed and falls back to the copy embedded in
    /// the exe, so the About page's what's-new viewer always has something
    /// honest to show — online it shows the newest release, offline the
    /// snapshot the build shipped with.
    /// </summary>
    public static class ChangelogFeed
    {
        public const string RepoUrl = "https://github.com/marbou92/FFX-Compatibility-Tool";

        /// <summary>Permanent GitHub shortcut: always resolves to the
        /// changelog.json asset of the newest full release. A plain file
        /// redirect — none of the 60-requests-per-hour API limits.</summary>
        public const string LatestFeedUrl =
            RepoUrl + "/releases/latest/download/changelog.json";

        /// <summary>Base for permanent per-file download URLs of the latest
        /// release (append the URL-escaped asset file name).</summary>
        public const string LatestDownloadBase =
            RepoUrl + "/releases/latest/download/";

        private const string EmbeddedResourceName = "FfxTool.Gui.changelog.json";

        /// <summary>Worker-thread fetch; the callback fires on that thread,
        /// the caller marshals to the UI. Null means "no feed available"
        /// (offline, 404 while the newest release predates the feed, or a
        /// body that didn't parse).</summary>
        public static void FetchAsync(Action<ChangelogEntry> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => done(Fetch()));
        }

        public static ChangelogEntry Fetch()
        {
            try
            {
                // same TLS 1.2 pin as the update check — Windows 7 machines
                // whose OS defaults predate TLS 1.2 must still connect
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var req = (HttpWebRequest)WebRequest.Create(LatestFeedUrl);
                req.Method = "GET";
                req.UserAgent = "FFXCompatibilityTool/" + AppInfo.Version;
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.BypassCache);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return null;
                    using (var ms = new MemoryStream())
                    using (var src = resp.GetResponseStream())
                    {
                        src.CopyTo(ms);
                        return ChangelogEntry.Parse(
                            Encoding.UTF8.GetString(ms.ToArray()));
                    }
                }
            }
            catch { return null; } // offline / 404 / proxy junk — the caller falls back
        }

        /// <summary>The feed snapshot embedded at build time (always the
        /// newest release the build knew about). Never null unless the
        /// resource is missing or corrupt — the repo copy ships valid JSON.</summary>
        public static ChangelogEntry LoadEmbedded()
        {
            try
            {
                var assembly = typeof(ChangelogFeed).Assembly;
                using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return ChangelogEntry.Parse(reader.ReadToEnd());
                }
            }
            catch { return null; }
        }

        // ---------- the release list (the changelog drill-in) ----------

        /// <summary>One row of GitHub's release list, exactly what the
        /// changelog drill-in needs: tag, date, page URL and the raw
        /// Stablemd body (parsed lazily by StablemdParser on selection).</summary>
        public sealed class ReleaseSummary
        {
            public string Tag;         // "v0.2.1"
            public string DateIso;     // "2026-09-10" or null
            public string Url;         // release page
            public string Body;        // the release's Stablemd markdown
            public bool Prerelease;
        }

        /// <summary>Fetches the project's release list (newest first, up to
        /// 30, drafts skipped) on a worker thread. The callback fires on
        /// that thread with the list — or null plus a human error line
        /// when GitHub couldn't be reached. The same endpoint the old
        /// About-page timeline used; prereleases (the rolling nightly)
        /// come back flagged so the caller can decide whether to show
        /// them.</summary>
        public static void FetchReleasesAsync(Action<List<ReleaseSummary>, string> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string json = null;
                string error = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create(
                        RepoUrl + "/releases?per_page=30");
                    req.Method = "GET";
                    req.UserAgent = "FFXCompatibilityTool/" + AppInfo.Version;
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                        System.Net.Cache.RequestCacheLevel.BypassCache);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var ms = new MemoryStream())
                    {
                        resp.GetResponseStream().CopyTo(ms);
                        json = Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
                catch (Exception ex) { error = ex.Message; }

                var releases = new List<ReleaseSummary>();
                if (json != null)
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                if (el.ValueKind != JsonValueKind.Object) continue;
                                bool draft = el.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
                                if (draft) continue;
                                string tag = Str(el, "tag_name");
                                string url = Str(el, "html_url");
                                if (tag == null || url == null) continue;
                                var r = new ReleaseSummary
                                {
                                    Tag = tag,
                                    Url = url,
                                    Body = Str(el, "body") ?? "",
                                    Prerelease = el.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True
                                };
                                if (el.TryGetProperty("published_at", out var pub) &&
                                    pub.ValueKind == JsonValueKind.String &&
                                    DateTime.TryParse(pub.GetString(), CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                                    r.DateIso = dt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                                releases.Add(r);
                            }
                        }
                    }
                    catch { releases.Clear(); error = "the release list didn't parse"; }
                }
                done(releases.Count > 0 ? releases : null, error);
            });
        }

        private static string Str(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }
    }

    /// <summary>
    /// Parses a release body written in the project's curated Stablemd
    /// format into a ChangelogEntry — the SAME rules the release workflow
    /// uses when it generates changelog.json (skip "description::" and
    /// "#" lines; the first "##" is the document title; every later "##"
    /// opens a section; lines starting with - * or • are bullets; the
    /// description is the first free-standing line before the first
    /// section). The drill-in runs this on the release body GitHub
    /// returns, so the in-app changelog renders exactly what the release
    /// page was curated from — including the "image::" hero, which the
    /// workflow drops but the viewer keeps.
    /// </summary>
    public static class StablemdParser
    {
        public static ChangelogEntry Parse(string tag, string dateIso, string url, string body)
        {
            var entry = new ChangelogEntry
            {
                Version = tag != null ? tag.TrimStart('v') : null,
                Date = dateIso,
                Url = url
            };
            if (string.IsNullOrEmpty(body)) return entry;

            ChangelogSection current = null;
            bool firstHeadingSkipped = false;
            foreach (string rawLine in body.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                // metadata lines the workflow also skips — except image::,
                // which is the drill-in's hero
                if (line.StartsWith("image::", StringComparison.Ordinal))
                {
                    string img = line.Substring(7).Trim();
                    if (img.Length > 0 && entry.ImageUrl == null) entry.ImageUrl = img;
                    continue;
                }
                if (line.StartsWith("description::", StringComparison.Ordinal)) continue;

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    // the first "##" is the document title; later ones open
                    // sections (a lone "#" h1 never opens one)
                    if (line.StartsWith("##", StringComparison.Ordinal))
                    {
                        if (!firstHeadingSkipped)
                        {
                            firstHeadingSkipped = true;
                            continue;
                        }
                        current = new ChangelogSection
                        {
                            Title = line.TrimStart('#').Trim()
                        };
                        entry.Sections.Add(current);
                    }
                    continue;
                }

                if (line.StartsWith("-", StringComparison.Ordinal) ||
                    line.StartsWith("*", StringComparison.Ordinal) ||
                    line.StartsWith("•", StringComparison.Ordinal))
                {
                    string item = line.TrimStart('-', '*', '•').Trim();
                    if (item.Length == 0) continue;
                    if (current == null)
                        current = new ChangelogSection { Title = "Changes" };
                    if (entry.Sections.IndexOf(current) < 0) entry.Sections.Add(current);
                    current.Items.Add(item);
                    continue;
                }

                // free-standing prose before the first section = description
                if (current == null && entry.Description == null)
                    entry.Description = line;
            }
            return entry;
        }
    }

    /// <summary>
    /// The shared renderer for a changelog entry — the same vivi-style
    /// look everywhere: a section title per group, and every item as a
    /// row in front of it. Used by the About page's what's-new panel and
    /// by the updater window's offer. Colors are bound to resource KEYS
    /// (not captured brushes), so the rows re-theme live exactly like the
    /// rest of the app.
    /// </summary>
    public static class ChangelogView
    {
        /// <summary>Render a changelog entry. Pass showSectionTitles:false
        /// when the host already carries a version title (the Settings
        /// what's-new card shows "What's new in vX" in its own header row),
        /// true when the panel is the whole story (the updater window).</summary>
        public static void BuildInto(StackPanel host, ChangelogEntry entry, bool includeDescription,
                                     bool showSectionTitles = true)
        {
            host.Children.Clear();
            if (entry == null) return;

            if (includeDescription && !string.IsNullOrEmpty(entry.Description))
            {
                var intro = new TextBlock
                {
                    Text = entry.Description,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                intro.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                host.Children.Add(intro);
            }

            bool first = true;
            foreach (var section in entry.Sections)
            {
                if (showSectionTitles)
                {
                    // section header in the settings-row language: a small
                    // rounded icon tile + the section name — the feed's own
                    // "✨" is dropped here because the tile IS the sparkle
                    string titleText = string.IsNullOrEmpty(section.Title) ? "Changes" : section.Title;
                    titleText = titleText.Replace("✨", "").Trim();
                    if (titleText.Length == 0) titleText = "Changes";

                    var headGrid = new Grid { Margin = new Thickness(0, first ? 6 : 14, 0, 8) };
                    headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var tile = new Border
                    {
                        Width = 26,
                        Height = 26,
                        CornerRadius = new CornerRadius(8),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    tile.SetResourceReference(Border.BackgroundProperty, "B.PrimaryContainer");
                    var tileIcon = new IconGlyph { IconName = "AutoAwesome", Width = 14, Height = 14 };
                    tileIcon.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnPrimaryContainer");
                    tile.Child = tileIcon;
                    Grid.SetColumn(tile, 0);

                    var title = new TextBlock
                    {
                        Text = titleText,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    title.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");
                    Grid.SetColumn(title, 1);

                    headGrid.Children.Add(tile);
                    headGrid.Children.Add(title);
                    host.Children.Add(headGrid);
                }

                foreach (var item in section.Items)
                {
                    // item rows in the settings-row language: a rounded
                    // row whose leading tinted circle carries the bullet
                    // dot — same silhouette as the Storage and About rows
                    var rowBorder = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(2, 5, 2, 5),
                        Margin = new Thickness(0, 0, 0, 2),
                        Background = Brushes.Transparent
                    };
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var lead = new Border
                    {
                        Width = 18,
                        Height = 18,
                        CornerRadius = new CornerRadius(9),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 1, 0, 0)
                    };
                    lead.SetResourceReference(Border.BackgroundProperty, "B.PrimaryContainer");
                    var dot = new Border
                    {
                        Width = 5,
                        Height = 5,
                        CornerRadius = new CornerRadius(2.5),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    dot.SetResourceReference(Border.BackgroundProperty, "B.OnPrimaryContainer");
                    lead.Child = dot;
                    Grid.SetColumn(lead, 0);

                    var text = new TextBlock
                    {
                        Text = item,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12.5,
                        Margin = new Thickness(10, 0, 0, 0)
                    };
                    text.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurface");
                    Grid.SetColumn(text, 1);

                    row.Children.Add(lead);
                    row.Children.Add(text);
                    rowBorder.Child = row;
                    host.Children.Add(rowBorder);
                }
                first = false;
            }

            if (first)
            {
                var empty = new TextBlock
                {
                    Text = "This release carries no itemized notes — the release page has the full story.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                host.Children.Add(empty);
            }
        }
    }
}
