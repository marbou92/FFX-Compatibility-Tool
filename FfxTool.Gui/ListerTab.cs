using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Port of ffx_gui/tab_lister.py. Open a .ffx, see every effect it
    /// uses, resolved vendor/suite, and a warning if it's flagged as
    /// missing from the user's Plugin Profile.
    /// </summary>
    public class ListerTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Label _fileLabel;
        readonly ListView _list;
        readonly Label _summaryLabel;
        string _currentPath;
        System.Collections.Generic.List<Pipeline.EffectInfo> _currentEffects = new System.Collections.Generic.List<Pipeline.EffectInfo>();

        public ListerTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = Md3Tokens.Surface;
            Padding = new Padding(Md3Tokens.Space6);

            var openBtn = new Md3Button { Text = "Open .ffx file…", Width = 160, Location = new System.Drawing.Point(0, 0) };
            openBtn.Click += (s, e) => OpenFile();

            _fileLabel = new Label
            {
                Text = "No file loaded", ForeColor = Md3Tokens.OnSurfaceVariant, Font = Md3Tokens.BodyMedium,
                Location = new System.Drawing.Point(openBtn.Right + Md3Tokens.Space4, 8), AutoSize = true,
            };

            _list = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = false,
                Location = new System.Drawing.Point(0, openBtn.Bottom + Md3Tokens.Space4),
                Size = new System.Drawing.Size(820, 400),
                Font = Md3Tokens.BodyMedium, BackColor = Md3Tokens.Surface,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            _list.Columns.Add("#", 40);
            _list.Columns.Add("Match name", 220);
            _list.Columns.Add("Vendor / Suite", 320);
            _list.Columns.Add("Status", 220);

            _summaryLabel = new Label
            {
                Text = "", Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                Location = new System.Drawing.Point(0, _list.Bottom + Md3Tokens.Space2),
                AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };

            Controls.Add(openBtn);
            Controls.Add(_fileLabel);
            Controls.Add(_list);
            Controls.Add(_summaryLabel);
        }

        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                LoadFile(dlg.FileName);
            }
        }

        public void LoadFile(string path)
        {
            _currentPath = path;
            _fileLabel.Text = path;
            var data = File.ReadAllBytes(path);
            _currentEffects = Pipeline.ListEffects(data);
            Refresh_();
        }

        /// <summary>Re-render the list. Called on file load, and again
        /// whenever the Plugin Profile changes.</summary>
        public void Refresh_()
        {
            var table = PluginLookup.LoadTable();
            var realEffects = _currentEffects.Where(e => !e.IsSentinel).ToList();

            _list.Items.Clear();
            int missingCount = 0, unknownCount = 0;

            for (int i = 0; i < realEffects.Count; i++)
            {
                var eff = realEffects[i];
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);

                string status;
                if (match.Vendor == null) { status = "Unknown plugin — not in lookup table"; unknownCount++; }
                else if (owned == false) { status = "NOT in your profile — likely to fail"; missingCount++; }
                else if (owned == true) { status = "You have this" + (match.Confirmed ? "" : " (unverified prefix)"); }
                else { status = "Native / always available"; }

                var item = new ListViewItem(new[]
                {
                    (i + 1).ToString(), eff.MatchName, $"{match.Vendor ?? "?"} — {match.Suite ?? "?"}", status,
                });
                if (owned == false) item.BackColor = Md3Tokens.ErrorContainer;
                else if (match.Vendor == null) item.BackColor = Md3Tokens.TertiaryContainer;
                _list.Items.Add(item);
            }

            var parts = new System.Collections.Generic.List<string> { $"{realEffects.Count} effect(s)" };
            if (missingCount > 0) parts.Add($"{missingCount} flagged as missing from your profile");
            if (unknownCount > 0) parts.Add($"{unknownCount} unrecognized");
            _summaryLabel.Text = string.Join(" · ", parts);
        }
    }
}
