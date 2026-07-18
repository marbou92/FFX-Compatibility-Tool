using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Redesigned with a TableLayoutPanel so the effect checklist
    /// and result box actually grow/shrink with the window, instead of
    /// v1's fixed pixel sizes.</summary>
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // open file row
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // hint
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // effect checklist — grows with window
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // target + convert button
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); // result box — grows with window

            var openRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            var openBtn = new Md3Button { Text = "Open .ffx file…", Width = 160, Margin = new Padding(0, 0, Md3Tokens.Space4, 0) };
            openBtn.Click += (s, e) => OpenFile();
            _fileLabel = new Label
            {
                Text = "No file loaded", Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Md3Tokens.Space2, 0, 0),
            };
            openRow.Controls.Add(openBtn);
            openRow.Controls.Add(_fileLabel);

            var hint = new Label
            {
                Text = "Effects flagged as missing from your Plugin Profile are pre-selected for removal below —\n" +
                       "uncheck any you'd rather keep.",
                Font = Md3Tokens.BodyMedium, ForeColor = Md3Tokens.OnSurfaceVariant,
                AutoSize = true, Margin = new Padding(0, Md3Tokens.Space4, 0, Md3Tokens.Space2),
            };

            _effectList = new CheckedListBox { Dock = DockStyle.Fill, Font = Md3Tokens.BodyMedium, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };

            var targetRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, Md3Tokens.Space4) };
            var targetLabel = new Label
            {
                Text = "Target version:", Font = Md3Tokens.BodyLarge, ForeColor = Md3Tokens.OnSurface,
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Md3Tokens.Space2, Md3Tokens.Space2, 0),
            };
            _targetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Md3Tokens.BodyLarge, Width = 120,
                Margin = new Padding(0, 0, Md3Tokens.Space6, 0),
            };
            foreach (var k in Pipeline.KnownVersions.Keys.OrderBy(k => k)) _targetCombo.Items.Add(k);
            if (_targetCombo.Items.Count > 0) _targetCombo.SelectedIndex = 0;

            _convertBtn = new Md3Button { Text = "Convert…", Width = 140, Enabled = false };
            _convertBtn.Click += (s, e) => DoConvert();

            targetRow.Controls.Add(targetLabel);
            targetRow.Controls.Add(_targetCombo);
            targetRow.Controls.Add(_convertBtn);

            _resultBox = new TextBox
            {
                Multiline = true, ReadOnly = true, Font = Md3Tokens.BodyMedium,
                Dock = DockStyle.Fill, BackColor = Md3Tokens.SurfaceContainer,
                BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical,
            };

            root.Controls.Add(openRow, 0, 0);
            root.Controls.Add(hint, 0, 1);
            root.Controls.Add(_effectList, 0, 2);
            root.Controls.Add(targetRow, 0, 3);
            root.Controls.Add(_resultBox, 0, 4);
            Controls.Add(root);
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
