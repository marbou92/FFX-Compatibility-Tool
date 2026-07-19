using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>
    /// Redesigned with a TableLayoutPanel (header row / fill row / summary
    /// row) instead of v1's manual pixel-position math, which broke on
    /// resize. The ListView now genuinely fills available space instead
    /// of relying on Anchor guesswork.
    /// </summary>
    public class ListerTab : UserControl
    {
        readonly PluginProfile _profile;
        readonly Label _fileLabel;
        readonly ListView _list;
        readonly Label _summaryLabel;
        System.Collections.Generic.List<Pipeline.EffectInfo> _currentEffects = new System.Collections.Generic.List<Pipeline.EffectInfo>();

        public ListerTab(PluginProfile profile)
        {
            _profile = profile;
            BackColor = ThemeManager.Current.Surface;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var headerRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
            };
            var openBtn = new Md3Button { Text = "Open .ffx file…", Icon = Md3Icons.Icon.FolderOpen, Width = 180, Margin = new Padding(0, 0, Md3Tokens.Space4, Md3Tokens.Space4) };
            openBtn.Click += (s, e) => OpenFile();
            _fileLabel = new Label
            {
                Text = "No file loaded", ForeColor = ThemeManager.Current.OnSurfaceVariant, Font = Md3Tokens.BodyMedium,
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Md3Tokens.Space2, 0, 0),
            };
            headerRow.Controls.Add(openBtn);
            headerRow.Controls.Add(_fileLabel);

            _list = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = false,
                Dock = DockStyle.Fill, Font = Md3Tokens.BodyMedium, BackColor = ThemeManager.Current.Surface,
                Margin = new Padding(0, Md3Tokens.Space2, 0, Md3Tokens.Space2),
                BorderStyle = BorderStyle.FixedSingle,
                // ListView (unlike ComboBox) has a real owner-draw API that
                // fully replaces its native rendering — using it here to
                // get an actual flat MD3-themed header and rows instead of
                // the stock Windows 3D-bevel header + native selection
                // highlight that was showing in the previous build.
                OwnerDraw = true,
            };
            _list.DrawColumnHeader += (s, e) =>
            {
                using (var brush = new SolidBrush(ThemeManager.Current.SurfaceContainer))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var textRect = new Rectangle(e.Bounds.X + Md3Tokens.Space2, e.Bounds.Y, e.Bounds.Width - Md3Tokens.Space4, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Md3Tokens.LabelLarge, textRect, ThemeManager.Current.OnSurfaceVariant,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _list.DrawSubItem += (s, e) =>
            {
                // e.Item.BackColor is still used deliberately here (set per-row
                // in Refresh_() below for missing/unknown status highlighting)
                // — that's a per-row semantic color, not a stale-theme bug like
                // the earlier Md3Switch/Md3Card issue, since it's re-set fresh
                // every time Refresh_() runs rather than cached once at
                // construction.
                var rowBg = e.Item.Selected
                    ? ThemeManager.Current.PrimaryContainer
                    : e.Item.BackColor != _list.BackColor
                        ? e.Item.BackColor // an explicit missing/unknown-plugin highlight set in Refresh_()
                        : ThemeManager.Current.Surface;
                using (var brush = new SolidBrush(rowBg))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                var textColor = e.Item.Selected ? ThemeManager.Current.OnPrimaryContainer : ThemeManager.Current.OnSurface;
                var textRect = new Rectangle(e.Bounds.X + Md3Tokens.Space2, e.Bounds.Y, e.Bounds.Width - Md3Tokens.Space4, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Md3Tokens.BodyMedium, textRect, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            _list.DrawItem += (s, e) => { }; // required to exist for OwnerDraw, actual painting happens per-subitem above
            ThemeManager.ThemeChanged += () => { _list.BackColor = ThemeManager.Current.Surface; _list.Invalidate(true); };
            _list.Columns.Add("#", 40);
            _list.Columns.Add("Match name", 220);
            _list.Columns.Add("Vendor / Suite", 320);
            _list.Columns.Add("Status", 220);

            _summaryLabel = new Label
            {
                Text = "", Font = Md3Tokens.BodyMedium, ForeColor = ThemeManager.Current.OnSurfaceVariant,
                Dock = DockStyle.Fill, AutoSize = true,
            };

            root.Controls.Add(headerRow, 0, 0);
            root.Controls.Add(_list, 0, 1);
            root.Controls.Add(_summaryLabel, 0, 2);
            Controls.Add(root);
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
                if (owned == false) item.BackColor = ThemeManager.Current.ErrorContainer;
                else if (match.Vendor == null) item.BackColor = ThemeManager.Current.TertiaryContainer;
                _list.Items.Add(item);
            }

            var parts = new System.Collections.Generic.List<string> { $"{realEffects.Count} effect(s)" };
            if (missingCount > 0) parts.Add($"{missingCount} flagged as missing from your profile");
            if (unknownCount > 0) parts.Add($"{unknownCount} unrecognized");
            _summaryLabel.Text = string.Join(" · ", parts);
        }
    }
}
