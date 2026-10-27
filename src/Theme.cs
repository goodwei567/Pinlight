using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class Theme
    {
        internal static readonly Color Paper = ColorTranslator.FromHtml("#202020"), Surface = ColorTranslator.FromHtml("#2c2c2c"), Ink = ColorTranslator.FromHtml("#e8e8e8"), Muted = ColorTranslator.FromHtml("#aaaeb2"), Line = ColorTranslator.FromHtml("#505050"), Green = ColorTranslator.FromHtml("#5d9dbd"), Mint = ColorTranslator.FromHtml("#334956"), Cream = ColorTranslator.FromHtml("#383838");
        internal static Font Font(float size) { return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static Font Display(float size) { return new Font("Bahnschrift", size, FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static GraphicsPath Round(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            if (d <= 0) { path.AddRectangle(rectangle); return path; }
            path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void Card(Graphics graphics, Rectangle rectangle, Color color, int radius)
        {
            using (GraphicsPath path = Round(rectangle, radius)) using (Brush brush = new SolidBrush(color)) using (Pen pen = new Pen(Line)) { graphics.FillPath(brush, path); graphics.DrawPath(pen, path); }
        }
        internal static void Text(Graphics graphics, string text, float size, Color color, Rectangle rectangle, ContentAlignment alignment)
        {
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (alignment == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            else if (alignment == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            else flags |= TextFormatFlags.VerticalCenter;
            using (Font font = Font(size)) TextRenderer.DrawText(graphics, text, font, rectangle, color, flags);
        }
        internal static Label Label(string text, int size, Color color)
        {
            return new Label { Text = text, Font = Font(size), ForeColor = color, AutoSize = false, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        }
        internal static void ConfigureMenu(ContextMenuStrip menu)
        {
            menu.BackColor = Surface; menu.ForeColor = Ink; menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        }
        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuItemSelected { get { return Mint; } }
            public override Color MenuItemBorder { get { return Line; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Surface; } }
        }
    }
    internal sealed class SoftButton : Button
    {
        internal bool Primary, Selected;
        private bool hover;
        internal SoftButton(string text, bool primary)
        {
            Text = text; Primary = primary; Font = Theme.Font(13); Height = 38; Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Theme.Surface; ForeColor = Theme.Ink;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            AccessibleName = text;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Paper : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? (hover ? Color.FromArgb(68, 128, 160) : Theme.Green) : (Selected || hover ? Theme.Mint : BackColor);
            Rectangle rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Card(e.Graphics, rectangle, fill, 3);
            Color text = !Enabled ? Color.FromArgb(105, 109, 112) : Primary ? Color.White : Selected ? Theme.Green : ForeColor;
            TextRenderer.DrawText(e.Graphics, Text, Font, rectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused) using (Pen pen = new Pen(Theme.Green) { DashStyle = DashStyle.Dot }) e.Graphics.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
        }
    }
    internal sealed class InputDialog : Form
    {
        private TextBox input;
        internal string Value { get { return input.Text.Trim(); } }
        internal InputDialog(string title, string initial, bool multiline)
        {
            Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(410, multiline ? 240 : 180); BackColor = Theme.Paper; Font = Theme.Font(14);
            Label label = Theme.Label(title, 18, Theme.Ink); label.SetBounds(24, 17, 360, 30); Controls.Add(label);
            input = new TextBox { Text = initial, Multiline = multiline, MaxLength = multiline ? 2000 : 80, Font = Theme.Font(15), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Ink };
            input.SetBounds(25, 62, 360, multiline ? 105 : 32); Controls.Add(input);
            SoftButton cancel = new SoftButton("取消", false); cancel.SetBounds(195, ClientSize.Height - 52, 86, 35); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel);
            SoftButton ok = new SoftButton("确定", true); ok.SetBounds(292, ClientSize.Height - 52, 93, 35); Controls.Add(ok);
            ok.Click += delegate { if (!String.IsNullOrWhiteSpace(input.Text)) { DialogResult = DialogResult.OK; Close(); } };
            CancelButton = cancel;
            if (!multiline) AcceptButton = ok;
            Shown += delegate { input.Focus(); input.SelectAll(); };
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Native.PrepareWindow(Handle, false);
        }
    }
}
