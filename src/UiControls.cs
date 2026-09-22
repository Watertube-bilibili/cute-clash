using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CuteClash
{
    internal static class UiShape
    {
        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            if (diameter <= 1F) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class SoftPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color EdgeColor { get; set; }
        public SoftPanel()
        {
            CornerRadius = 14; EdgeColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.FromArgb(248, 249, 251) : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiShape.Rounded(new RectangleF(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius * e.Graphics.DpiX / 96F))
            {
                using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
                if (EdgeColor != Color.Transparent) using (var pen = new Pen(EdgeColor)) e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // Still a native Button: tab order, accessibility, Enter/Space activation,
    // dialog results and event semantics remain supplied by Windows Forms.
    internal sealed class SoftButton : Button
    {
        private bool hovering;
        private bool pressing;
        public string Glyph { get; set; }
        public Color HoverColor { get; set; }
        public Color PressColor { get; set; }
        public Color EdgeColor { get; set; }
        public int CornerRadius { get; set; }
        public SoftButton()
        {
            CornerRadius = 8; EdgeColor = Color.Transparent;
            HoverColor = Color.Empty; PressColor = Color.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; pressing = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressing = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressing = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressing = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressing = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressing = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = !Enabled ? Color.FromArgb(232, 236, 242) : pressing && !PressColor.IsEmpty ? PressColor : hovering && !HoverColor.IsEmpty ? HoverColor : BackColor;
            Color foreground = Enabled ? ForeColor : Color.FromArgb(108, 122, 140);
            float scale = graphics.DpiX / 96F;
            using (var path = UiShape.Rounded(new RectangleF(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius * scale))
            {
                using (var brush = new SolidBrush(background)) graphics.FillPath(brush, path);
                if (EdgeColor != Color.Transparent && Enabled) using (var pen = new Pen(EdgeColor)) graphics.DrawPath(pen, path);
            }
            var bounds = ClientRectangle;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (TextAlign == ContentAlignment.MiddleLeft) { bounds.X += (int)(14F * scale); bounds.Width -= (int)(24F * scale); flags |= TextFormatFlags.Left; }
            else flags |= TextFormatFlags.HorizontalCenter;
            if (!String.IsNullOrEmpty(Glyph))
            {
                DrawGlyph(graphics, Glyph, new RectangleF(15F * scale, Height / 2F - 9F * scale, 18F * scale, 18F * scale), foreground, scale);
                bounds.X += (int)(30F * scale); bounds.Width -= (int)(30F * scale);
            }
            if (UseMnemonic) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(graphics, Text, Font, bounds, foreground, flags);
            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(ClientRectangle, -(int)(5F * scale), -(int)(5F * scale));
                ControlPaint.DrawFocusRectangle(graphics, focus, foreground, background);
            }
        }
        private static void DrawGlyph(Graphics g, string name, RectangleF r, Color color, float scale)
        {
            using (var pen = new Pen(color, 1.4F * scale))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                if (name == "Overview")
                {
                    g.DrawArc(pen, x, y + h * .07F, w, h * .9F, 150, 240);
                    g.DrawLine(pen, x + w * .5F, y + h * .55F, x + w * .78F, y + h * .29F);
                    g.DrawLine(pen, x + w * .27F, y + h * .88F, x + w * .73F, y + h * .88F);
                }
                else if (name == "Profiles")
                {
                    g.DrawRectangle(pen, x + w * .12F, y, w * .74F, h);
                    g.DrawLine(pen, x + w * .31F, y + h * .32F, x + w * .69F, y + h * .32F);
                    g.DrawLine(pen, x + w * .31F, y + h * .57F, x + w * .69F, y + h * .57F);
                    g.DrawLine(pen, x + w * .31F, y + h * .79F, x + w * .54F, y + h * .79F);
                }
                else if (name == "Proxies")
                {
                    g.DrawLine(pen, x + w * .5F, y + h * .28F, x + w * .5F, y + h * .62F);
                    g.DrawLine(pen, x + w * .15F, y + h * .62F, x + w * .85F, y + h * .62F);
                    g.DrawLine(pen, x + w * .15F, y + h * .62F, x + w * .15F, y + h * .78F);
                    g.DrawLine(pen, x + w * .85F, y + h * .62F, x + w * .85F, y + h * .78F);
                    g.DrawEllipse(pen, x + w * .38F, y, w * .24F, h * .25F);
                    g.DrawRectangle(pen, x, y + h * .79F, w * .29F, h * .2F);
                    g.DrawRectangle(pen, x + w * .71F, y + h * .79F, w * .29F, h * .2F);
                }
                else if (name == "Settings")
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float lineY = y + h * (.15F + i * .35F);
                        g.DrawLine(pen, x, lineY, x + w, lineY);
                        float knob = x + w * (i == 1 ? .7F : .3F);
                        g.DrawEllipse(pen, knob - 2F * scale, lineY - 2F * scale, 4F * scale, 4F * scale);
                    }
                }
                else
                {
                    g.DrawLines(pen, new[] { new PointF(x, y + h * .2F), new PointF(x + w * .3F, y + h * .5F), new PointF(x, y + h * .8F) });
                    g.DrawLine(pen, x + w * .48F, y + h * .8F, x + w, y + h * .8F);
                }
            }
        }
    }
}
