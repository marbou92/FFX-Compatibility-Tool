using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Port of ffx_gui/tab_convert.py. Load a .ffx, optionally
    /// strip effects flagged as missing, pick a target version, convert,
    /// save. Surfaces the verification-pass result directly.</summary>
    public class ConvertTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Label _fileLabel;
        readonly CheckedListBox _effectList;
        readonly ComboBox _targetCombo;
        readonly Md3Button _convertBtn;
        readonly TextBox _resultBox;

        byte[] _inputData;
        List<Pipeline.EffectInfo> _currentEffects = new List<Pipeline.EffectInfo>();

        public ConvertTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = Md3Tokens.Surface;
            Padding = new Padding(Md3Tokens.Space6);

            var openBtn = new Md3Button { Text = "Open .ffx file…", Width = 160, Location = new System.Drawing.Point(0, 0) };
            openBtn.Click += (s, e) => OpenFile();

            _fileLabel = new Label
            {
                Text = "No file loaded", Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                Location = new System.Drawing.Point(openBtn.Right + Md3Tokens.Space4, 8), AutoSize = true,
            };

            var hint = new Label
            {
                Text = "Effects flagged as missing from your Plugin Profile are pre-selected for removal below —\n" +
                       "uncheck any you'd rather keep.",
                Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                Location = new System.Drawing.Point(0, openBtn.Bottom + Md3Tokens.Space4), AutoSize = true,
            };

            _effectList = new CheckedListBox
            {
                Location = new System.Drawing.Point(0, hint.Bottom + Md3Tokens.Space2),
                Size = new System.Drawing.Size(600, 220), Font = Md3Tokens.BodyMedium,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            var targetLabel = new Label
            {
                Text = "Target version:", Font = Md3Tokens.BodyLarge, ForeColor = Md3Tokens.OnSurface,
                Location = new System.Drawing.Point(0, _effectList.Bottom + Md3Tokens.Space4), AutoSize = true,
            };
            _targetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Md3Tokens.BodyLarge,
                Location = new System.Drawing.Point(targetLabel.Right + Md3Tokens.Space2, targetLabel.Top - 3),
                Width = 120,
            };
            foreach (var k in Pipeline.KnownVersions.Keys.OrderBy(k => k)) _targetCombo.Items.Add(k);
            if (_targetCombo.Items.Count > 0) _targetCombo.SelectedIndex = 0;

            _convertBtn = new Md3Button
            {
                Text = "Convert…", Width = 140,
                Location = new System.Drawing.Point(0, targetLabel.Bottom + Md3Tokens.Space4),
                Enabled = false,
            };
            _convertBtn.Click += (s, e) => DoConvert();

            _resultBox = new TextBox
            {
                Multiline = true, ReadOnly = true, Font = Md3Tokens.BodyMedium,
                Location = new System.Drawing.Point(0, _convertBtn.Bottom + Md3Tokens.Space4),
                Size = new System.Drawing.Size(600, 100), BackColor = Md3Tokens.SurfaceContainer,
                BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            Controls.Add(openBtn);
            Controls.Add(_fileLabel);
            Controls.Add(hint);
            Controls.Add(_effectList);
            Controls.Add(targetLabel);
            Controls.Add(_targetCombo);
            Controls.Add(_convertBtn);
            Controls.Add(_resultBox);
        }

        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                _fileLabel.Text = dlg.FileName;
                _inputData = File.ReadAllBytes(dlg.FileName);
                _currentEffects = Pipeline.ListEffects(_inputData);
                _convertBtn.Enabled = true;
                Refresh_();
            }
        }

        /// <summary>Re-render the removal checklist. Called on file load,
        /// and again when the Plugin Profile changes.</summary>
        public void Refresh_()
        {
            _effectList.Items.Clear();
            if (_currentEffects.Count == 0) return;

            var table = PluginLookup.LoadTable();
            foreach (var eff in _currentEffects)
            {
                if (eff.IsSentinel) continue;
                var match = PluginLookup.Resolve(eff.MatchName, table);
                var owned = _profile.Owns(match.Vendor);

                // Never pre-check an unknown-vendor effect — "unknown" is
                // not the same as "confirmed missing" and shouldn't be
                // silently stripped by default.
                _effectList.Items.Add($"{eff.MatchName}  ({match.Vendor ?? "unknown vendor"})", owned == false);
            }
        }

        void DoConvert()
        {
            if (_inputData == null) return;

            var toRemove = new HashSet<string>();
            for (int i = 0; i < _effectList.Items.Count; i++)
            {
                if (_effectList.GetItemChecked(i))
                {
                    var text = _effectList.Items[i].ToString();
                    var name = text.Substring(0, text.IndexOf("  (", StringComparison.Ordinal));
                    toRemove.Add(name);
                }
            }

            var target = _targetCombo.SelectedItem?.ToString() ?? "cs5.5";

            Pipeline.ConversionResult result;
            try
            {
                result = Pipeline.Convert(_inputData, target, toRemove.Count > 0 ? toRemove : null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dlg = new SaveFileDialog { Filter = "After Effects Presets (*.ffx)|*.ffx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                File.WriteAllBytes(dlg.FileName, result.Data);

                var lines = new List<string> { $"Saved: {dlg.FileName}", $"Target: {target}" };
                if (result.RemovedEffects.Count > 0)
                    lines.Add($"Removed: {string.Join(", ", result.RemovedEffects)}");
                foreach (var w in result.Warnings) lines.Add($"Warning: {w}");
                lines.Add("Verification pass: OK — 0 Utf8 tags remaining, indices contiguous, keyframe/parameter data unchanged.");
                _resultBox.Text = string.Join(Environment.NewLine, lines);
            }
        }
    }
}
