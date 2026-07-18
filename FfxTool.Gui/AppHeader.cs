using System.Drawing;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>MD3 "top app bar" — simple title strip with a bottom hairline, replacing the bare form title as the only header.</summary>
    public class AppHeader : Panel
    {
        readonly Label _label;

        public AppHeader(string title)
        {
            Dock = DockStyle.Top;
            Height = 56;
            BackColor = ThemeManager.Current.Surface;

            _label = new Label
            {
                Text = title,
                Font = Md3Tokens.TitleLarge,
                ForeColor = ThemeManager.Current.OnSurface,
                AutoSize = true,
                Location = new Point(Md3Tokens.Space6, 14),
            };
            Controls.Add(_label);

            // BackColor/ForeColor are static properties set once above —
            // they won't pick up a live theme switch on their own, unlike
            // custom-painted controls that re-read ThemeManager.Current
            // inside OnPaint. Subscribe explicitly so this header actually
            // re-themes instead of staying stuck on whatever theme was
            // active when the app started.
            ThemeManager.ThemeChanged += () =>
            {
                BackColor = ThemeManager.Current.Surface;
                _label.ForeColor = ThemeManager.Current.OnSurface;
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ThemeManager.Current.OutlineVariant))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}
