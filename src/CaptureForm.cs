using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class CaptureForm : Form
    {
        private readonly Bitmap source;
        private readonly Settings settings;
        private readonly Action<Bitmap, string, Point> finished;
        private readonly Point origin;
        private Rectangle selection, originalSelection;
        private Point start, mouse;
        private int mode, resizeHandle, selected = -1, stroke = 3, fontSize = 28, colorFormat;
        private Shape draft, originalShape;
        private Tool tool;
        private Color color = ColorTranslator.FromHtml("#e15c48");
        private List<Shape> shapes = new List<Shape>();
        private readonly UndoStack history = new UndoStack();
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel(), properties = new FlowLayoutPanel();
        private readonly Dictionary<Tool, SoftButton> buttons = new Dictionary<Tool, SoftButton>();
        private SoftButton undo, redo;
        private bool completing;
        private ScrollCaptureForm scrollCapture;
        private bool scrollFinished;
        private bool scrollShuttingDown;

        internal CaptureForm(Bitmap image, Rectangle bounds, Settings preferences, Action<Bitmap, string, Point> onFinished)
        {
            source = image; settings = preferences; finished = onFinished; origin = bounds.Location;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; Bounds = bounds; BackColor = Color.Black;
            DoubleBuffered = true; Text = "Pinlight - 截图与标注"; Cursor = Cursors.Cross;
            BuildToolbar();
            Shown += delegate { Activate(); Focus(); Invalidate(); };
        }
        private SoftButton Add(FlowLayoutPanel panel, string text, int width, Action action)
        {
            SoftButton button = new SoftButton(text, false); button.Font = Theme.Font(12);
            button.Size = new Size(width, 33); button.Margin = new Padding(2, 3, 0, 3);
            button.Click += delegate { action(); if (!IsDisposed && Visible) Focus(); }; panel.Controls.Add(button); return button;
        }
        private void BuildToolbar()
        {
            toolbar.BackColor = properties.BackColor = Theme.Surface;
            toolbar.Size = new Size(920, 44); properties.Size = new Size(534, 42);
            toolbar.Padding = properties.Padding = new Padding(3, 2, 3, 2);
            toolbar.WrapContents = properties.WrapContents = false;
            toolbar.Visible = properties.Visible = false; Controls.Add(toolbar); Controls.Add(properties);
            Tool[] tools = { Tool.Select, Tool.Rectangle, Tool.Ellipse, Tool.Arrow, Tool.Pen, Tool.Highlight, Tool.Text, Tool.Step, Tool.Mosaic, Tool.Blur };
            string[] labels = { "选择", "矩形", "椭圆", "箭头", "画笔", "高亮", "文字", "序号", "马赛克", "柔化" };
            for (int i = 0; i < tools.Length; i++)
            {
                Tool value = tools[i];
                buttons[value] = Add(toolbar, labels[i], value == Tool.Mosaic ? 53 : 43, delegate { tool = value; selected = -1; UpdateTools(); Invalidate(); });
            }
            undo = Add(toolbar, "撤销", 43, Undo); redo = Add(toolbar, "重做", 43, Redo);
            Add(toolbar, "滚动截图", 75, StartScrollCapture);
            Add(toolbar, "贴图", 60, delegate { Complete("pin"); });
            Add(toolbar, "保存", 47, delegate { Complete("save"); });
            Add(toolbar, "取消", 47, delegate { Close(); });
            Add(toolbar, "复制 ↵", 73, delegate { Complete("copy"); }).Primary = true;
            int total = 8; foreach (Control control in toolbar.Controls) total += control.Width + control.Margin.Horizontal;
            toolbar.Width = total;
            int minimumScreenWidth = Screen.PrimaryScreen.Bounds.Width;
            foreach (Screen screen in Screen.AllScreens) minimumScreenWidth = Math.Min(minimumScreenWidth, screen.Bounds.Width);
            if (toolbar.Width > minimumScreenWidth - 8)
            {
                toolbar.Width = minimumScreenWidth - 8;
                toolbar.WrapContents = true;
                toolbar.Height = 86;
            }
            foreach (int width in new int[] { 3, 6, 10 })
            {
                int value = width; SoftButton b = Add(properties, width == 3 ? "细" : width == 6 ? "中" : "粗", 31, delegate { stroke = value; ApplyProperties(); }); b.Tag = value;
            }
            foreach (Color swatch in new Color[] { color, Color.Orange, Color.Gold, Theme.Green, Color.SteelBlue, Color.MediumPurple, Color.White })
            {
                Color value = swatch; SoftButton b = Add(properties, "●", 32, delegate { color = value; ApplyProperties(); });
                b.Tag = value; b.ForeColor = value; b.Font = Theme.Font(22);
            }
            ComboBox fonts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Font(13), Width = 86, Margin = new Padding(8, 7, 3, 3), BackColor = Theme.Surface, ForeColor = Theme.Ink, FlatStyle = FlatStyle.Flat };
            fonts.Items.AddRange(new object[] { "20 px", "28 px", "40 px", "56 px" }); fonts.SelectedIndex = 1;
            fonts.SelectedIndexChanged += delegate { fontSize = new int[] { 20, 28, 40, 56 }[fonts.SelectedIndex]; ApplyProperties(); };
            properties.Controls.Add(fonts); UpdateTools();
        }
        private void ApplyProperties()
        {
            if (selected >= 0 && selected < shapes.Count)
            {
                shapes[selected].Color = color; shapes[selected].Width = stroke; shapes[selected].FontSize = fontSize; history.Push(shapes);
            }
            UpdateTools(); Invalidate();
        }
        private void UpdateTools()
        {
            foreach (KeyValuePair<Tool, SoftButton> pair in buttons) { pair.Value.Selected = pair.Key == tool; pair.Value.Invalidate(); }
            foreach (Control control in properties.Controls)
            {
                SoftButton button = control as SoftButton; if (button == null) continue;
                button.Selected = button.Tag is int ? (int)button.Tag == stroke : button.Tag is Color && ((Color)button.Tag).ToArgb() == color.ToArgb(); button.Invalidate();
            }
            undo.Enabled = history.CanUndo; redo.Enabled = history.CanRedo;
            Cursor = tool == Tool.Text ? Cursors.IBeam : tool == Tool.Select && selection.Width > 0 ? Cursors.SizeAll : Cursors.Cross;
        }
        private void PositionToolbar()
        {
            if (selection.Width < 3 || selection.Height < 3 || mode == 1 || mode == 5) { toolbar.Hide(); properties.Hide(); return; }
            Rectangle available = Screen.FromPoint(Cursor.Position).Bounds; available.Offset(-origin.X, -origin.Y);
            int x = Geometry.Clamp(selection.Right - toolbar.Width, Math.Max(3, available.Left + 3), Math.Max(3, available.Right - toolbar.Width - 3));
            int stackHeight = toolbar.Height + properties.Height + 10;
            int y = selection.Bottom + 12; if (y + stackHeight > available.Bottom) y = selection.Top - stackHeight - 7;
            y = Geometry.Clamp(y, Math.Max(3, available.Top + 3), Math.Max(3, available.Bottom - stackHeight - 3));
            toolbar.Location = new Point(x, y); properties.Location = new Point(x + (toolbar.Width - properties.Width) / 2, y + toolbar.Height + 5);
            toolbar.Show(); properties.Show(); toolbar.BringToFront(); properties.BringToFront();
        }
        private Rectangle[] Handles()
        {
            int l = selection.Left, t = selection.Top, r = selection.Right - 1, b = selection.Bottom - 1, mx = (l + r) / 2, my = (t + b) / 2;
            Point[] points = { new Point(l, t), new Point(mx, t), new Point(r, t), new Point(r, my), new Point(r, b), new Point(mx, b), new Point(l, b), new Point(l, my) };
            return Array.ConvertAll(points, delegate(Point p) { return new Rectangle(p.X - 3, p.Y - 3, 6, 6); });
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.DrawImageUnscaled(source, 0, 0);
            using (Region outside = new Region(ClientRectangle)) using (Brush dim = new SolidBrush(Color.FromArgb(100, 15, 27, 20)))
            {
                if (selection.Width > 0) outside.Exclude(selection); g.FillRegion(dim, outside);
            }
            if (selection.Width > 0 && selection.Height > 0)
            {
                List<Shape> all = new List<Shape>(shapes); if (draft != null) all.Add(draft);
                Imaging.DrawShapes(g, source, all, selection);
                using (Pen border = new Pen(Theme.Green)) g.DrawRectangle(border, selection.X, selection.Y, Math.Max(1, selection.Width - 1), Math.Max(1, selection.Height - 1));
                foreach (Rectangle handle in Handles()) using (Brush fill = new SolidBrush(Theme.Surface)) using (Pen pen = new Pen(Theme.Green)) { g.FillRectangle(fill, handle); g.DrawRectangle(pen, handle); }
                Rectangle label = new Rectangle(selection.Left, Math.Max(4, selection.Top - 31), 185, 25);
                using (Brush fill = new SolidBrush(Theme.Surface)) g.FillRectangle(fill, label);
                Theme.Text(g, selection.Width + " × " + selection.Height + " px", 13, Theme.Ink, label, ContentAlignment.MiddleCenter);
                if (selected >= 0 && selected < shapes.Count)
                {
                    Rectangle box = shapes[selected].Bounds; box.Inflate(5, 5);
                    using (Pen pen = new Pen(Theme.Green) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, box);
                }
            }
            else
            {
                Rectangle hint = new Rectangle(ClientSize.Width / 2 - 225, 32, 450, 45);
                using (Brush fill = new SolidBrush(Theme.Surface)) g.FillRectangle(fill, hint);
                Theme.Text(g, "拖动选择区域    Esc 取消", 14, Theme.Ink, hint, ContentAlignment.MiddleCenter);
            }
            if (settings.Magnifier && mode == 0 && !toolbar.Bounds.Contains(mouse)) DrawMagnifier(g);
        }
        private string ColorText(Color value)
        {
            if (colorFormat == 1) return "RGB " + value.R + ", " + value.G + ", " + value.B;
            if (colorFormat == 2) return "HSL " + (int)value.GetHue() + "°, " + (int)(value.GetSaturation() * 100) + "%, " + (int)(value.GetBrightness() * 100) + "%";
            return "#" + value.R.ToString("X2") + value.G.ToString("X2") + value.B.ToString("X2");
        }
        private void DrawMagnifier(Graphics g)
        {
            if (!new Rectangle(Point.Empty, source.Size).Contains(mouse)) return;
            int x = mouse.X + 25, y = mouse.Y + 28;
            if (x + 166 > ClientSize.Width) x = mouse.X - 191;
            if (y + 208 > ClientSize.Height) y = mouse.Y - 233;
            x = Math.Max(0, x); y = Math.Max(0, y);
            using (Brush bg = new SolidBrush(Theme.Paper)) g.FillRectangle(bg, x, y, 164, 204);
            Rectangle sample = new Rectangle(Geometry.Clamp(mouse.X - 7, 0, Math.Max(0, source.Width - 15)), Geometry.Clamp(mouse.Y - 7, 0, Math.Max(0, source.Height - 15)), Math.Min(15, source.Width), Math.Min(15, source.Height));
            GraphicsState saved = g.Save(); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(x + 7, y + 7, 150, 150), sample, GraphicsUnit.Pixel); g.Restore(saved);
            using (Pen grid = new Pen(Color.FromArgb(50, Color.White))) for (int i = 0; i <= 15; i++) { g.DrawLine(grid, x + 7 + i * 10, y + 7, x + 7 + i * 10, y + 157); g.DrawLine(grid, x + 7, y + 7 + i * 10, x + 157, y + 7 + i * 10); }
            int cx = x + 7 + (mouse.X - sample.X) * 10, cy = y + 7 + (mouse.Y - sample.Y) * 10;
            using (Pen black = new Pen(Color.Black, 3)) using (Pen white = new Pen(Color.White)) { g.DrawRectangle(black, cx, cy, 10, 10); g.DrawRectangle(white, cx, cy, 10, 10); }
            Theme.Text(g, ColorText(source.GetPixel(mouse.X, mouse.Y)), 12, Theme.Ink, new Rectangle(x + 4, y + 161, 156, 22), ContentAlignment.MiddleCenter);
            Theme.Text(g, (mouse.X + origin.X) + ", " + (mouse.Y + origin.Y), 11, Theme.Muted, new Rectangle(x + 4, y + 182, 156, 18), ContentAlignment.MiddleCenter);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); mouse = e.Location;
            if (e.Button == MouseButtons.Right) { if (selection.Width > 0) ResetSelection(); else Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            start = e.Location; Capture = true;
            Rectangle[] handles = Handles();
            for (int i = 0; i < handles.Length && selection.Width > 0; i++) if (Rectangle.Inflate(handles[i], 4, 4).Contains(e.Location)) { mode = 5; resizeHandle = i; originalSelection = selection; toolbar.Hide(); properties.Hide(); return; }
            if (selection.Width == 0 || !selection.Contains(e.Location))
            {
                mode = 1; selection = Rectangle.Empty; shapes.Clear(); selected = -1; history.Reset();
                tool = Tool.Select; toolbar.Hide(); properties.Hide(); UpdateTools(); Invalidate(); return;
            }
            Point point = Geometry.InBounds(e.Location, selection);
            if (tool == Tool.Select)
            {
                selected = -1;
                for (int i = shapes.Count - 1; i >= 0; i--)
                    if (Rectangle.Inflate(shapes[i].Bounds, 7, 7).Contains(point)) { selected = i; break; }
                if (selected >= 0)
                {
                    mode = 4; originalShape = shapes[selected].Copy();
                    color = originalShape.Color; stroke = originalShape.Width; fontSize = originalShape.FontSize;
                }
                else { mode = 2; originalSelection = selection; }
            }
            else if (tool == Tool.Text)
            {
                Capture = false;
                using (InputDialog dialog = new InputDialog("输入标注文字", "", true))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        shapes.Add(new Shape { Tool = Tool.Text, Start = point, End = point, Text = dialog.Value, FontSize = fontSize, Color = color, Width = stroke });
                        history.Push(shapes);
                    }
                }
            }
            else if (tool == Tool.Step)
            {
                int number = 1;
                foreach (Shape shape in shapes) if (shape.Tool == Tool.Step) number = Math.Max(number, shape.Number + 1);
                shapes.Add(new Shape { Tool = Tool.Step, Start = point, End = point, Number = number, Color = color });
                history.Push(shapes); Capture = false;
            }
            else
            {
                mode = 3; draft = new Shape { Tool = tool, Start = point, End = point, Color = color, Width = stroke };
                if (tool == Tool.Pen || tool == Tool.Highlight) draft.Points.Add(point);
            }
            UpdateTools(); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); mouse = Geometry.InBounds(e.Location, new Rectangle(Point.Empty, source.Size));
            if (mode == 1) selection = Geometry.Rect(start, mouse);
            if (mode == 2)
            {
                Rectangle value = originalSelection; value.Offset(mouse.X - start.X, mouse.Y - start.Y);
                selection = Geometry.Fit(value, source.Size);
            }
            if (mode == 3 && draft != null)
            {
                Point point = Geometry.InBounds(mouse, selection); draft.End = point;
                if (draft.Tool == Tool.Pen || draft.Tool == Tool.Highlight)
                {
                    Point previous = draft.Points[draft.Points.Count - 1];
                    if (Math.Abs(point.X - previous.X) + Math.Abs(point.Y - previous.Y) > 2) draft.Points.Add(point);
                }
            }
            if (mode == 4 && selected >= 0)
            {
                Shape value = originalShape.Copy(); value.Move(mouse.X - start.X, mouse.Y - start.Y); shapes[selected] = value;
            }
            if (mode == 5)
            {
                int l = originalSelection.Left, t = originalSelection.Top, r = originalSelection.Right, b = originalSelection.Bottom;
                if (resizeHandle == 0 || resizeHandle == 6 || resizeHandle == 7) l = Geometry.Clamp(mouse.X, 0, r - Math.Min(4, originalSelection.Width));
                if (resizeHandle == 2 || resizeHandle == 3 || resizeHandle == 4) r = Geometry.Clamp(mouse.X, l + Math.Min(4, originalSelection.Width), source.Width);
                if (resizeHandle == 0 || resizeHandle == 1 || resizeHandle == 2) t = Geometry.Clamp(mouse.Y, 0, b - Math.Min(4, originalSelection.Height));
                if (resizeHandle == 4 || resizeHandle == 5 || resizeHandle == 6) b = Geometry.Clamp(mouse.Y, t + Math.Min(4, originalSelection.Height), source.Height);
                selection = Rectangle.FromLTRB(l, t, r, b);
            }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            if (mode == 3 && draft != null)
            {
                bool valid = draft.Points.Count > 1 || (draft.Points.Count == 0 && (draft.Bounds.Width > 1 || draft.Bounds.Height > 1));
                if (valid) { shapes.Add(draft); history.Push(shapes); }
                draft = null;
            }
            if (mode == 4) history.Push(shapes);
            mode = 0; Capture = false;
            if (selection.Width < 3 || selection.Height < 3) selection = Rectangle.Empty;
            PositionToolbar(); UpdateTools(); Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (tool == Tool.Select && selected >= 0 && selected < shapes.Count && shapes[selected].Tool == Tool.Text)
            {
                Capture = false; mode = 0;
                using (InputDialog dialog = new InputDialog("编辑标注文字", shapes[selected].Text, true))
                    if (dialog.ShowDialog(this) == DialogResult.OK) { shapes[selected].Text = dialog.Value; history.Push(shapes); UpdateTools(); Invalidate(); }
            }
            base.OnMouseDoubleClick(e);
        }
        private void ResetSelection()
        {
            mode = 0; Capture = false; selection = Rectangle.Empty; draft = null; selected = -1;
            shapes.Clear(); history.Reset(); toolbar.Hide(); properties.Hide(); UpdateTools(); Invalidate();
        }
        private void Undo() { shapes = history.Undo(); selected = -1; UpdateTools(); Invalidate(); }
        private void Redo() { shapes = history.Redo(); selected = -1; UpdateTools(); Invalidate(); }
        internal void Hotkey(int id)
        {
            if (scrollCapture != null && !scrollCapture.IsDisposed) { if (id == 2) scrollCapture.Finish("pin"); return; }
            if (id == 2) Complete("pin");
        }
        private void StartScrollCapture()
        {
            if (selection.Width < 80 || selection.Height < 100)
            {
                MessageBox.Show(this, "滚动区域至少为 80 × 100 像素。", "滚动截图", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }
            if (scrollCapture != null) return;
            if (shapes.Count > 0 && MessageBox.Show(this, "滚动截图不包含当前标注，继续？", "滚动截图", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Rectangle screenRegion = selection; screenRegion.Offset(origin);
            try
            {
                using (Bitmap initial = source.Clone(selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    scrollCapture = new ScrollCaptureForm(screenRegion, initial, delegate(Bitmap result, string action)
                    {
                        finished(result, action, screenRegion.Location); scrollFinished = true;
                    });
                ScrollCaptureForm session = scrollCapture;
                session.FormClosed += delegate
                {
                    scrollCapture = null;
                    if (!scrollShuttingDown && !IsDisposed && !Disposing)
                    {
                        if (scrollFinished) Close();
                        else { Show(); Activate(); PositionToolbar(); }
                    }
                    session.Dispose();
                };
                Native.PrepareWindow(Handle, false); Hide(); Native.RefreshDesktop();
                session.Show();
            }
            catch (Exception error)
            {
                if (scrollCapture != null) { scrollCapture.Dispose(); scrollCapture = null; }
                Show(); Activate();
                MessageBox.Show(this, error.Message, "滚动截图未启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
            if (keyData == Keys.Enter) { Complete("copy"); return true; }
            if (keyData == (Keys.Control | Keys.S)) { Complete("save"); return true; }
            if (keyData == Keys.Delete && selected >= 0 && selected < shapes.Count)
            {
                shapes.RemoveAt(selected); selected = -1; history.Push(shapes); UpdateTools(); Invalidate(); return true;
            }
            if (keyData == Keys.C)
            {
                try { Clipboard.SetText(ColorText(source.GetPixel(mouse.X, mouse.Y))); Close(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "复制色值失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                return true;
            }
            int dx = keyData == Keys.Left || keyData == Keys.A ? -1 : keyData == Keys.Right || keyData == Keys.D ? 1 : 0;
            int dy = keyData == Keys.Up || keyData == Keys.W ? -1 : keyData == Keys.Down || keyData == Keys.S ? 1 : 0;
            if (dx != 0 || dy != 0)
            {
                if (selected >= 0 && selected < shapes.Count) { shapes[selected].Move(dx, dy); history.Push(shapes); }
                else if (selection.Width > 0)
                {
                    Rectangle value = selection; value.Offset(dx, dy); selection = Geometry.Fit(value, source.Size); PositionToolbar();
                }
                else { Point pointer = Cursor.Position; Native.SetCursorPos(pointer.X + dx, pointer.Y + dy); }
                UpdateTools(); Invalidate(); return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey) { colorFormat = (colorFormat + 1) % 3; Invalidate(); }
            base.OnKeyUp(e);
        }
        private void Complete(string action)
        {
            if (completing || selection.Width < 3 || selection.Height < 3) return;
            completing = true;
            try
            {
                using (Bitmap output = Imaging.Compose(source, shapes, selection))
                {
                    if (action == "copy") ClipboardService.CopyImage(output);
                    if (action == "save")
                    {
                        using (SaveFileDialog dialog = new SaveFileDialog { Title = "保存截图", Filter = "PNG 图像|*.png", DefaultExt = "png", AddExtension = true, FileName = "Pinlight-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", OverwritePrompt = true })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            output.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    // The receiver consumes or clones the result synchronously.
                    finished(output, action, new Point(origin.X + selection.X, origin.Y + selection.Y));
                }
                Close();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "截图操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { completing = false; }
        }
        private bool sourceDisposed;
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            scrollShuttingDown = true;
            if (scrollCapture != null && !scrollCapture.IsDisposed) scrollCapture.Close();
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) scrollShuttingDown = true;
            if (disposing && scrollCapture != null)
            {
                ScrollCaptureForm session = scrollCapture; scrollCapture = null; scrollFinished = true;
                session.Close(); session.Dispose();
            }
            if (disposing && !sourceDisposed) { sourceDisposed = true; source.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
