using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class ScrollPreviewForm : Form
    {
        private const int Inset = 6, Gap = 12, MarginSize = 8;
        private readonly Rectangle selection, screen, controls;
        private Bitmap thumbnail;
        private bool captureExcluded;
        internal Size ImageSize { get { return thumbnail == null ? Size.Empty : thumbnail.Size; } }
        internal int ContentHeight { get; private set; }
        internal ScrollPreviewForm(Rectangle area, Rectangle controlBounds)
        {
            selection = area; controls = controlBounds; screen = Screen.FromRectangle(area).Bounds;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
            BackColor = Theme.Surface; Text = "Pinlight - 滚动预览";
            DoubleBuffered = true;
        }
        internal static Rectangle Place(Rectangle area, Rectangle display, Rectangle controlBounds, Size content)
        {
            int rightSpace = display.Right - MarginSize - area.Right - Gap;
            int leftSpace = area.Left - Gap - display.Left - MarginSize;
            bool right = rightSpace >= 100, left = !right && leftSpace >= 100;
            int maxWidth = Math.Min(252, right ? rightSpace : left ? leftSpace : display.Width - MarginSize * 2);
            int x = right ? area.Right + Gap : left ? area.Left - Gap - maxWidth : display.Right - MarginSize - maxWidth;
            int edge = right ? area.Right + Gap : left ? area.Left - Gap : display.Right - MarginSize;
            // Prefer an unobstructed side lane before shortening the overview above the controls.
            if (right && x < controlBounds.Right + Gap && x + maxWidth > controlBounds.Left - Gap && display.Right - MarginSize - controlBounds.Right - Gap >= 100)
            {
                edge = Math.Max(edge, controlBounds.Right + Gap);
                maxWidth = Math.Min(252, display.Right - MarginSize - edge); x = edge;
            }
            else if (left && x < controlBounds.Right + Gap && x + maxWidth > controlBounds.Left - Gap && controlBounds.Left - Gap - display.Left - MarginSize >= 100)
            {
                edge = Math.Min(edge, controlBounds.Left - Gap);
                maxWidth = Math.Min(252, edge - display.Left - MarginSize); x = edge - maxWidth;
            }
            int top = display.Top + MarginSize, bottom = display.Bottom - MarginSize;
            if (x < controlBounds.Right + Gap && x + maxWidth > controlBounds.Left - Gap)
            {
                int above = controlBounds.Top - Gap - top, below = bottom - controlBounds.Bottom - Gap;
                if (above >= 100 && (area.Top < controlBounds.Top || above >= below)) bottom = controlBounds.Top - Gap;
                else if (below >= 100) top = controlBounds.Bottom + Gap;
            }
            int maxHeight = Math.Max(Inset * 2 + 1, Math.Min(720, bottom - top));
            maxWidth = Math.Max(Inset * 2 + 1, maxWidth);
            double scale = Math.Min(1, Math.Min((double)(maxWidth - Inset * 2) / content.Width, (double)(maxHeight - Inset * 2) / content.Height));
            int width = Math.Max(1, (int)Math.Round(content.Width * scale)) + Inset * 2;
            int height = Math.Max(1, (int)Math.Round(content.Height * scale)) + Inset * 2;
            x = right ? edge : left ? edge - width : display.Right - MarginSize - width;
            x = Geometry.Clamp(x, display.Left + MarginSize, display.Right - MarginSize - width);
            int y = Geometry.Clamp(area.Top, top, bottom - height);
            return new Rectangle(x, y, width, height);
        }
        internal void UpdateContent(ScrollStitcher stitcher)
        {
            Rectangle layout = Place(selection, screen, controls, new Size(stitcher.Width, stitcher.Height));
            Bitmap next = stitcher.BuildThumbnail(new Size(layout.Width - Inset * 2, layout.Height - Inset * 2));
            Bitmap old = thumbnail; thumbnail = next; ContentHeight = stitcher.Height;
            Bounds = layout; if (old != null) old.Dispose(); Invalidate(); Update();
        }
        internal bool MustHideForCapture(Rectangle area) { return Visible && !captureExcluded && Bounds.IntersectsWith(area); }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED); return parameters; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Native.PrepareWindow(Handle, true);
            Native.SetPassThrough(Handle, true, 255);
            captureExcluded = Native.ExcludeCapture(Handle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            if (thumbnail != null) e.Graphics.DrawImageUnscaled(thumbnail, (ClientSize.Width - thumbnail.Width) / 2, (ClientSize.Height - thumbnail.Height) / 2);
            using (Pen pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && thumbnail != null) { thumbnail.Dispose(); thumbnail = null; }
            base.Dispose(disposing);
        }
    }
}
