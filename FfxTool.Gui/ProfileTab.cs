using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Redesigned with FlowLayoutPanel for the vendor checklist
    /// instead of v1's manual Y-coordinate increment loop.</summary>
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Text = "Check off every plugin vendor you have installed in your target After Effects\n" +
                       "version. Checking a vendor covers all of its effects (e.g. checking \"Boris FX\"\n" +
                       "covers both Sapphire and Continuum effects).",
                Font = Md3Tokens.BodyLarge, ForeColor = Md3Tokens.OnSurface,
                AutoSize = true, Margin = new Padding(0, 0, 0, Md3Tokens.Space6),
            };

            var vendorFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
            };

            var table = PluginLookup.LoadTable();
            var vendors = _profile.AllKnownVendors(table);

            foreach (var vendor in vendors)
            {
                var card = new Md3Card { Width = 420, Height = 44, Margin = new Padding(0, 0, 0, Md3Tokens.Space2) };
                var cb = new Md3Switch
                {
                    Text = vendor, Checked = _profile.OwnedVendors.Contains(vendor),
                    Dock = DockStyle.Fill,
                };
                cb.CheckedChanged += (s, e) =>
                {
                    _profile.SetOwned(vendor, cb.Checked);
                    _profile.Save();
                    _onChange();
                };
                card.Controls.Add(cb);
                _checkboxes[vendor] = cb;
                vendorFlow.Controls.Add(card);
            }

            var scanRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, Md3Tokens.Space6, 0, 0),
            };
            var scanBtn = new Md3Button { Text = "Scan a plugins folder…", Width = 200, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            scanBtn.Click += (s, e) => ScanFolder();
            var scanHint = new Label
            {
                Text = "Point this at your AE plugins directory to auto-check vendors whose files are found there.",
                Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Md3Tokens.Space2, 0, 0),
            };
            scanRow.Controls.Add(scanBtn);
            scanRow.Controls.Add(scanHint);

            root.Controls.Add(intro, 0, 0);
            root.Controls.Add(vendorFlow, 0, 1);
            root.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 2); // spacer — pushes the scan row to sit right after the cards rather than stretching
            root.Controls.Add(scanRow, 0, 3);
            Controls.Add(root);
        }

        void ScanFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select your AE plugins folder" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string[] filenames;
                try { filenames = Directory.GetFiles(dlg.SelectedPath).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToArray(); }
                catch (IOException) { return; }

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
