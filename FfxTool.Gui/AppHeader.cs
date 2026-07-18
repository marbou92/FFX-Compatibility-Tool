using System.Drawing;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>MD3 "top app bar" — simple title strip with a bottom hairline, replacing the bare form title as the only header.</summary>
    public class AppHeader : Panel
    {
        public AppHeader(string title)
        {
            Dock = DockStyle.Top;
            Height = 56;
            BackColor = Md3Tokens.Surface;

            var label = new Label
            {
                Text = title,
                Font = Md3Tokens.TitleLarge,
                ForeColor = Md3Tokens.OnSurface,
                AutoSize = true,
                Location = new Point(Md3Tokens.Space6, 14),
            };
            Controls.Add(label);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Md3Tokens.OutlineVariant))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}
