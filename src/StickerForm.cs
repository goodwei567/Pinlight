using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class StickerForm : Form
    {
        internal readonly PinRecord Record;
        internal Size OriginalSize { get { return original.Size; } }
        internal bool PassThrough { get; private set; }
        private readonly Bitmap original;
        private Bitmap display;
        private readonly Action<StickerForm, bool> changed;
        private readonly Timer saveTimer = new Timer { Interval = 450 };
        private Point dragStart, windowStart;
        private bool dragging, shuttingDown;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        internal StickerForm(Bitmap image, PinRecord record, Action<StickerForm, bool> onChanged)
        {
            original = image; Record = record; changed = onChanged;
            Record.Scale = Geometry.Clamp(Record.Scale, .1, 10); Record.Opacity = Geometry.Clamp(Record.Opacity, .1, 1);
            display = Imaging.Rotate(original, Record.Rotation);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true; BackColor = Theme.Surface; KeyPreview = true;
            Text = "Pinlight - 贴图 " + record.Id.Substring(0, Math.Min(8, record.Id.Length));
            Cursor = Cursors.SizeAll; Opacity = Record.Opacity;
            Location = new Point(record.X, record.Y); UpdateSize(); EnsureVisible();
            menu.Font = Theme.Font(13);
            Theme.ConfigureMenu(menu);
            AddMenu("复制图片", CopyImage);
            AddMenu("保存为 PNG…", SaveImage);
            menu.Items.Add(new ToolStripSeparator());
            AddMenu("逆时针旋转    1", delegate { Rotate(-1); });
            AddMenu("顺时针旋转    2", delegate { Rotate(1); });
            AddMenu("原始尺寸    0", delegate { Record.Scale = 1; Record.Collapsed = false; UpdateSize(); QueueSave(); });
            AddMenu("缩略 / 展开    Shift+双击", ToggleCollapse);
            AddMenu("鼠标穿透", delegate { TogglePassThrough(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu("关闭贴图    Esc / 双击", Close);
            ContextMenuStrip = menu;
            saveTimer.Tick += delegate { saveTimer.Stop(); Commit(); };
            LocationChanged += delegate { if (IsHandleCreated && Visible) QueueSave(); };
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= (int)Native.WS_EX_TOOLWINDOW; p.ClassStyle |= 0x20000; return p; }
        }
        private void AddMenu(string label, Action action)
        {
            menu.Items.Add(label, null, delegate
            {
                try { action(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "贴图操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            });
        }
        private void UpdateSize()
        {
            if (Record.Collapsed) ClientSize = new Size(44, 44);
            else ClientSize = new Size(Geometry.Clamp((int)Math.Round(display.Width * Record.Scale), 24, 12000) + 8, Geometry.Clamp((int)Math.Round(display.Height * Record.Scale), 24, 12000) + 8);
            Invalidate();
        }
        private void EnsureVisible()
        {
            bool visible = false;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle intersect = Rectangle.Intersect(screen.WorkingArea, Bounds);
                if (intersect.Width >= Math.Min(60, Width) && intersect.Height >= Math.Min(60, Height)) { visible = true; break; }
            }
            if (!visible)
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Left + 60, area.Top + 60);
            }
        }
        private void QueueSave() { if (shuttingDown) return; saveTimer.Stop(); saveTimer.Start(); }
        private void Commit()
        {
            Record.X = Left; Record.Y = Top; Record.Opacity = Opacity;
            if (changed != null) changed(this, false);
        }
        internal void Persist() { saveTimer.Stop(); Commit(); }
        internal void Shutdown() { shuttingDown = true; saveTimer.Stop(); Close(); }
        internal void TogglePassThrough()
        {
            Native.SetPassThrough(Handle, !PassThrough, (byte)Math.Round(Opacity * 255)); PassThrough = !PassThrough; Invalidate();
        }
        internal void SetVisible(bool value)
        {
            if (value) { Show(); TopMost = true; } else Hide();
        }
        internal void BringForward()
        {
            if (PassThrough) TogglePassThrough(); Show(); TopMost = true; Activate();
        }
        private void Rotate(int direction)
        {
            Record.Rotation = ((Record.Rotation + direction) % 4 + 4) % 4;
            Bitmap previous = display; display = Imaging.Rotate(original, Record.Rotation); previous.Dispose();
            UpdateSize(); QueueSave();
        }
        private void ToggleCollapse() { Record.Collapsed = !Record.Collapsed; UpdateSize(); QueueSave(); }
        private void SaveImage()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "保存贴图", Filter = "PNG 图像|*.png", DefaultExt = "png", AddExtension = true, FileName = "Pinlight-贴图.png", OverwritePrompt = true })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    using (Bitmap output = Imaging.Rotate(original, Record.Rotation)) output.Save(dialog.FileName, ImageFormat.Png);
        }
        internal void CopyImage() { using (Bitmap output = Imaging.Rotate(original, Record.Rotation)) ClipboardService.CopyImage(output); }
        internal void ExportImage() { SaveImage(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (!Record.Collapsed && Math.Abs(Record.Scale - 1) < .00001) e.Graphics.DrawImageUnscaled(display, 4, 4);
            else e.Graphics.DrawImage(display, Geometry.Contain(display.Size, new Rectangle(4, 4, Math.Max(1, ClientSize.Width - 8), Math.Max(1, ClientSize.Height - 8))));
            using (Pen pen = new Pen(PassThrough ? Color.FromArgb(188, 165, 105) : ContainsFocus ? Theme.Green : Theme.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragging = true; dragStart = Cursor.Position; windowStart = Location; Capture = true; Activate(); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { Point current = Cursor.Position; Location = new Point(windowStart.X + current.X - dragStart.X, windowStart.Y + current.Y - dragStart.Y); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); dragging = false; Capture = false; QueueSave();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            dragging = false; Capture = false;
            if (e.Button == MouseButtons.Left) { if ((ModifierKeys & Keys.Shift) != 0) ToggleCollapse(); else Close(); }
            base.OnMouseDoubleClick(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                Opacity = Geometry.Clamp(Opacity + Math.Sign(e.Delta) * .05, .1, 1); Record.Opacity = Opacity;
            }
            else
            {
                if (Record.Collapsed) return;
                Point screen = PointToScreen(e.Location);
                double rx = (double)e.X / Math.Max(1, Width), ry = (double)e.Y / Math.Max(1, Height);
                Record.Scale = Geometry.Clamp(Record.Scale * (e.Delta > 0 ? 1.1 : 1 / 1.1), .1, 10);
                UpdateSize(); Location = new Point(screen.X - (int)(rx * Width), screen.Y - (int)(ry * Height));
            }
            QueueSave();
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.D1 || keyData == Keys.NumPad1) { Rotate(-1); return true; }
            if (keyData == Keys.D2 || keyData == Keys.NumPad2) { Rotate(1); return true; }
            if (keyData == Keys.D0 || keyData == Keys.NumPad0) { Record.Scale = 1; UpdateSize(); QueueSave(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopyImage(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveImage(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            saveTimer.Stop();
            if (!shuttingDown && changed != null) changed(this, true);
            base.OnFormClosed(e);
        }
        private bool imagesDisposed;
        protected override void Dispose(bool disposing)
        {
            if (disposing && !imagesDisposed)
            {
                imagesDisposed = true; saveTimer.Dispose(); menu.Dispose(); display.Dispose(); original.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
