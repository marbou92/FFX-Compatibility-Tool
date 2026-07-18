using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Port of ffx_gui/tab_profile.py. Per-vendor checklist +
    /// folder-scan auto-detect.</summary>
    public class ProfileTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Action _onChange;
        readonly System.Collections.Generic.Dictionary<string, CheckBox> _checkboxes = new System.Collections.Generic.Dictionary<string, CheckBox>();

        static readonly System.Collections.Generic.Dictionary<string, string[]> VendorFileHints = new System.Collections.Generic.Dictionary<string, string[]>
        {
            { "Boris FX", new[] { "sapphire", "continuum", "bcc" } },
            { "Red Giant / Maxon", new[] { "magic bullet", "trapcode", "red giant" } },
            { "Video Copilot", new[] { "element", "optical flares", "saber", "twitch" } },
            { "Plugin Everything", new[] { "deep glow", "shadow studio" } },
            { "RE:Vision Effects", new[] { "twixtor", "reelsmart" } },
        };

        public ProfileTab(PluginProfile profile, Action onChange)
        {
            _profile = profile;
            _onChange = onChange;
            BackColor = Md3Tokens.Surface;
            Padding = new Padding(Md3Tokens.Space6);

            var intro = new Label
            {
                Text = "Check off every plugin vendor you have installed in your target After Effects\n" +
                       "version. Checking a vendor covers all of its effects (e.g. checking \"Boris FX\"\n" +
                       "covers both Sapphire and Continuum effects).",
                Font = Md3Tokens.BodyLarge, ForeColor = Md3Tokens.OnSurface,
                Location = new System.Drawing.Point(0, 0), AutoSize = true,
            };
            Controls.Add(intro);

            var table = PluginLookup.LoadTable();
            var vendors = _profile.AllKnownVendors(table);
            int y = intro.Bottom + Md3Tokens.Space6;

            foreach (var vendor in vendors)
            {
                var card = new Md3Card { Location = new System.Drawing.Point(0, y), Size = new System.Drawing.Size(400, 44) };
                var cb = new CheckBox
                {
                    Text = vendor, Checked = _profile.OwnedVendors.Contains(vendor),
                    Font = Md3Tokens.BodyLarge, ForeColor = Md3Tokens.OnSurface,
                    Dock = DockStyle.Fill, Padding = new Padding(Md3Tokens.Space2),
                };
                cb.CheckedChanged += (s, e) =>
                {
                    _profile.SetOwned(vendor, cb.Checked);
                    _profile.Save();
                    _onChange();
                };
                card.Controls.Add(cb);
                _checkboxes[vendor] = cb;
                Controls.Add(card);
                y = card.Bottom + Md3Tokens.Space2;
            }

            var scanBtn = new Md3Button { Text = "Scan a plugins folder…", Width = 200, Location = new System.Drawing.Point(0, y + Md3Tokens.Space4) };
            scanBtn.Click += (s, e) => ScanFolder();
            var scanHint = new Label
            {
                Text = "Point this at your AE plugins directory to auto-check vendors whose files are found there.",
                Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                Location = new System.Drawing.Point(scanBtn.Right + Md3Tokens.Space4, y + Md3Tokens.Space4 + 10),
                AutoSize = true,
            };
            Controls.Add(scanBtn);
            Controls.Add(scanHint);
        }

        void ScanFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select your AE plugins folder" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string[] filenames;
                try { filenames = Directory.GetFiles(dlg.SelectedPath).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToArray(); }
                catch (IOException) { return; }

                // Conservative by design, same as the Python version: a
                // false "not found" just means manual check-off still
                // works; a false positive would silently hide a real
                // missing-plugin warning, which is worse.
                foreach (var kv in VendorFileHints)
                {
                    if (!_checkboxes.ContainsKey(kv.Key)) continue;
                    bool found = filenames.Any(fname => kv.Value.Any(hint => fname.Contains(hint)));
                    if (found) _checkboxes[kv.Key].Checked = true;
                }
            }
        }
    }
}
