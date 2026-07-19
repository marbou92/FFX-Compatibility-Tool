using System.Drawing;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    /// <summary>
    /// A friendly empty-state placeholder (icon + title + message),
    /// meant to sit in the same space as a list/table when it has no
    /// content yet. Previously Effect Lister and Convert's checklist just
    /// showed a bare empty box with no guidance — not a bug exactly, but
    /// a real gap flagged in the layout/UX pass.
    /// </summary>
    public class Md3EmptyState : Panel
    {
        readonly Md3Icons.Icon _icon;
        readonly string _title;
        readonly string _message;

        public Md3EmptyState(Md3Icons.Icon icon, string title, string message)
        {
            _icon = icon;
            _title = title;
            _message = message;
            DoubleBuffered = true;
            ThemeManager.ThemeChanged += Invalidate_;
        }

        void Invalidate_() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(ThemeManager.Current.Surface);

            const int iconSize = 48;
            int centerX = Width / 2;
            int contentHeight = iconSize + Md3Tokens.Space4 + 24 + Md3Tokens.Space2 + 40; // rough stack height for vertical centering
            int top = System.Math.Max(Md3Tokens.Space6, (Height - contentHeight) / 2);

            var iconBounds = new Rectangle(centerX - iconSize / 2, top, iconSize, iconSize);
            Md3Icons.Draw(g, _icon, iconBounds, ThemeManager.Current.OutlineVariant, 1.6f);

            var titleRect = new Rectangle(0, iconBounds.Bottom + Md3Tokens.Space4, Width, 24);
            TextRenderer.DrawText(g, _title, Md3Tokens.TitleSmall, titleRect, ThemeManager.Current.OnSurfaceVariant,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

            var msgRect = new Rectangle(Md3Tokens.Space8, titleRect.Bottom + Md3Tokens.Space2, Width - Md3Tokens.Space8 * 2, 40);
            TextRenderer.DrawText(g, _message, Md3Tokens.BodyMedium, msgRect, ThemeManager.Current.OnSurfaceVariant,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }
}
