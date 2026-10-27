using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class ScrollCaptureForm : Form
    {
        private readonly Rectangle region;
        private readonly Action<Bitmap, string> completed;
        internal const int PollInterval = 30;
        private readonly Timer timer = new Timer { Interval = PollInterval };
        private readonly Label status = Theme.Label("准备中…", 12, Theme.Ink);
        private readonly Label hint = Theme.Label("在选区内向下滚动，保持前后重叠", 11, Theme.Muted);
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        private ScrollStitcher stitcher;
        private int misses;
        private bool busy, paused, closing, mustHide;
        private DateTime readyAt;
        private ScrollBorderForm border;
        private ScrollPreviewForm preview;
        internal ScrollPreviewForm Preview { get { return preview; } }
        internal int ResultHeight { get { return stitcher == null ? 0 : stitcher.Height; } }
        internal bool IsPaused { get { return paused; } }
        internal ScrollCaptureForm(Rectangle selection, Bitmap firstFrame, Action<Bitmap, string> onCompleted)
        {
            if (selection.Width < 80 || selection.Height < 100) throw new ArgumentException("滚动区域至少为 80 × 100 像素。");
            if (firstFrame.Size != selection.Size) throw new ArgumentException("初始图像与滚动选区尺寸不一致。");
            region = selection; completed = onCompleted;
            stitcher = new ScrollStitcher(firstFrame);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = false;
            BackColor = Theme.Surface; ForeColor = Theme.Ink; Text = "Pinlight - 滚动截图";
            Rectangle screen = Screen.FromRectangle(selection).Bounds;
            ClientSize = new Size(Math.Min(700, screen.Width - 12), 88);
            int x = Geometry.Clamp(selection.Right - Width, screen.Left + 4, screen.Right - Width - 4);
            int y = selection.Bottom + 10;
            if (y + Height > screen.Bottom) y = selection.Top - Height - 10;
            if (y < screen.Top) y = screen.Bottom - Height - 4;
            Location = new Point(x, y);
            status.Text = "1 帧 · " + stitcher.Width + " × " + stitcher.Height;
            status.SetBounds(10, 4, 230, 26); hint.SetBounds(245, 4, Width - 255, 26);
            actions.SetBounds(5, 33, Width - 10, 48); actions.WrapContents = false; actions.Padding = new Padding(0, 2, 0, 0);
            Controls.Add(status); Controls.Add(hint); Controls.Add(actions);
            SoftButton pause = Add("暂停", 62, delegate
            {
                paused = !paused; timer.Enabled = !paused;
                pauseButton.Text = paused ? "继续" : "暂停";
                status.Text = paused ? "已暂停 · " + ResultHeight + " px" : "继续向下滚动";
            }); pauseButton = pause;
            Add("复制", 64, delegate { Finish("copy"); }).Primary = true;
            Add("保存", 64, delegate { Finish("save"); });
            Add("贴图", 64, delegate { Finish("pin"); });
            Add("取消", 64, Close);
            timer.Tick += delegate { Poll(); };
            Shown += delegate
            {
                bool excluded = Native.ExcludeCapture(Handle);
                mustHide = !excluded && Bounds.IntersectsWith(region);
                border = new ScrollBorderForm(region); border.Show();
                preview = new ScrollPreviewForm(region, Bounds);
                preview.UpdateContent(stitcher); preview.Show();
                readyAt = DateTime.UtcNow.AddMilliseconds(140); timer.Start();
            };
        }
        private SoftButton pauseButton;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | (int)Native.WS_EX_TOOLWINDOW; return parameters; }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.PrepareWindow(Handle, true); }
        private SoftButton Add(string text, int width, Action action)
        {
            SoftButton button = new SoftButton(text, false) { Width = width, Height = 34, Margin = new Padding(4, 1, 2, 1) };
            button.Click += delegate { action(); }; actions.Controls.Add(button); return button;
        }
        private Bitmap CaptureFrame()
        {
            bool hidePreview = preview != null && preview.MustHideForCapture(region);
            if (hidePreview) preview.Hide();
            if (mustHide) Hide();
            if (mustHide || hidePreview) Native.RefreshDesktop();
            try { Native.FlushDesktop(); return Imaging.CaptureRegion(region); }
            finally
            {
                if (mustHide && !closing) Show();
                if (hidePreview && !closing && preview != null) preview.Show();
            }
        }
        internal void Poll()
        {
            if (closing || busy || paused || DateTime.UtcNow < readyAt) return;
            busy = true;
            try
            {
                using (Bitmap frame = CaptureFrame())
                {
                    StitchResult result = stitcher.Append(frame);
                    if (result == StitchResult.Appended)
                    {
                        misses = 0;
                        status.Text = stitcher.FrameCount + " 帧 · " + stitcher.Width + " × " + stitcher.Height;
                        if (preview != null) preview.UpdateContent(stitcher);
                    }
                    if (result == StitchResult.Appended) hint.Text = "在选区内向下滚动，保持前后重叠";
                    else if (result == StitchResult.Unchanged) misses = 0;
                    else if (result == StitchResult.NoMatch)
                    {
                        // A transient repaint should not end or visibly interrupt a live capture.
                        misses++;
                        if (misses >= 3)
                        {
                            status.Text = "等待重叠画面";
                            hint.Text = "稍向上滚回一点，再继续向下";
                        }
                    }
                    else if (result == StitchResult.Limit)
                    {
                        paused = true; timer.Stop(); pauseButton.Enabled = false;
                        status.Text = "达到长度上限，请复制或保存";
                    }
                }
            }
            catch (Exception error)
            {
                paused = true; timer.Stop(); pauseButton.Text = "继续";
                status.Text = "捕获暂停"; hint.Text = error.Message;
            }
            finally { busy = false; }
        }
        internal void Finish(string action)
        {
            if (busy || closing || stitcher == null) return;
            busy = true; timer.Stop();
            try
            {
                using (Bitmap result = stitcher.Build())
                {
                    if (action == "copy") ClipboardService.CopyImage(result);
                    else if (action == "save")
                    {
                        using (SaveFileDialog dialog = new SaveFileDialog { Filter = "PNG 图像|*.png", AddExtension = true, DefaultExt = "png", FileName = "Pinlight-长截图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", OverwritePrompt = true })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            result.Save(dialog.FileName, ImageFormat.Png);
                        }
                    }
                    completed(result, action);
                }
                Close();
            }
            catch (Exception error) { status.Text = "操作未完成"; hint.Text = error.Message; }
            finally { busy = false; if (!closing && !paused) timer.Start(); }
        }
        internal void StopAndClose() { Close(); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true; timer.Stop();
            ClosePreview();
            if (border != null) { border.Close(); border.Dispose(); border = null; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClosePreview();
                if (border != null) { border.Dispose(); border = null; }
                timer.Dispose(); if (stitcher != null) { stitcher.Dispose(); stitcher = null; }
            }
            base.Dispose(disposing);
        }
        private void ClosePreview()
        {
            if (preview == null) return;
            ScrollPreviewForm old = preview; preview = null; old.Close(); old.Dispose();
        }
    }
    internal sealed class ScrollBorderForm : Form
    {
        internal ScrollBorderForm(Rectangle selection)
        {
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Bounds = Rectangle.Inflate(selection, 2, 2); BackColor = Theme.Green;
            Region outline = new Region(ClientRectangle);
            outline.Exclude(new Rectangle(2, 2, selection.Width, selection.Height));
            Region = outline;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | (int)Native.WS_EX_TOOLWINDOW; return parameters; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Native.PrepareWindow(Handle, true);
            Native.SetPassThrough(Handle, true, 230);
        }
    }
}
