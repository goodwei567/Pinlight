using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class MainForm : Form
    {
        private readonly AppContext app;
        private readonly Panel content = new BufferedPanel();
        private readonly Label status = Theme.Label("就绪", 12, Theme.Muted);
        private readonly Dictionary<string, SoftButton> navigation = new Dictionary<string, SoftButton>();
        private readonly ContextMenuStrip delayMenu;
        private string view = "history";
        private bool reallyClose, initialShowPending = true;
        private DataGridView grid;
        private PictureBox preview;
        private CaptureRecord currentCapture;
        private StickerForm currentPin;
        internal MainForm(AppContext context)
        {
            app = context; Text = "Pinlight";
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; Font = Theme.Font(14);
            BackColor = Theme.Paper; ForeColor = Theme.Ink;
            ClientSize = new Size(1040, 640); MinimumSize = new Size(800, 510);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Icon = app.AppIcon;
            delayMenu = new ContextMenuStrip { Font = Theme.Font(13) };
            Theme.ConfigureMenu(delayMenu);
            foreach (int seconds in new int[] { 3, 5, 10 })
            {
                int delay = seconds;
                delayMenu.Items.Add(delay + " 秒", null, delegate { delayMenu.Close(); app.BeginCapture(delay); });
            }
            FlowLayoutPanel commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 53, Padding = new Padding(9, 8, 5, 5), WrapContents = false, BackColor = Theme.Paper };
            Command(commands, "截图", delegate { app.BeginCapture(0); }, false);
            SoftButton delayed = null;
            delayed = Command(commands, "延时截图", delegate { delayMenu.Show(delayed, new Point(0, delayed.Height + 1)); }, false);
            Command(commands, "剪贴板贴图", app.PasteClipboard, false);
            Command(commands, "导入", app.ImportPicture, false);
            Command(commands, "复制", delegate { if (currentCapture != null) app.CopyCapture(currentCapture); else if (currentPin != null) currentPin.CopyImage(); }, false);
            Command(commands, "另存为", delegate { if (currentCapture != null) app.ExportCapture(currentCapture); else if (currentPin != null) currentPin.ExportImage(); }, false);
            Command(commands, "设置", delegate { OpenView("settings"); }, false);
            FlowLayoutPanel tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(9, 0, 5, 0), WrapContents = false };
            string[] ids = { "history", "pins", "favorites" }, labels = { "截图历史", "贴图", "收藏" };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i]; SoftButton button = Command(tabs, labels[i], delegate { OpenView(id); }, false);
                button.Height = 28; button.Width = 88; navigation[id] = button;
            }
            content.Dock = DockStyle.Fill; content.Padding = new Padding(10, 3, 10, 9);
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Paper };
            status.Dock = DockStyle.Fill; status.Padding = new Padding(12, 0, 4, 0); footer.Controls.Add(status);
            Controls.Add(content); Controls.Add(tabs); Controls.Add(commands); Controls.Add(footer);
            ShowView("history");
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x02000000;
                return parameters;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Paper);
        }
        private SoftButton Command(FlowLayoutPanel parent, string text, Action action, bool primary)
        {
            SoftButton button = new SoftButton(text, primary) { Width = text.Length > 4 ? 113 : 87, Height = 34, Margin = new Padding(0, 0, 8, 0) };
            button.Click += delegate { try { action(); } catch (Exception error) { app.Store.Log(error); SetStatus(error.Message); } };
            parent.Controls.Add(button); return button;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.PrepareWindow(Handle, app.Store.State.Settings.HideWindowOnCapture);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!initialShowPending) return;
            initialShowPending = false;
            PerformLayout();
            Refresh();
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed) return;
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                Activate();
            }));
        }
        protected override void OnDeactivate(EventArgs e) { app.ResumeHotkeys(); base.OnDeactivate(e); }
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Action<Control> visit = null;
            visit = delegate(Control parent)
            {
                foreach (Control control in parent.Controls)
                {
                    if (control is HotkeyInput && control.ContainsFocus) { app.SuspendHotkeys(); return; }
                    if (control.ContainsFocus) visit(control);
                }
            };
            visit(this);
        }
        internal void SetStatus(string text) { status.Text = text; }
        internal void RefreshView() { if (!IsDisposed && Visible) ShowView(view); }
        private void OpenView(string name)
        {
            if (name == "overview") name = "history";
            if (view == name && content.Controls.Count > 0) return;
            ShowView(name);
        }
        internal void ShowView(string name)
        {
            app.ResumeHotkeys();
            if (name == "overview") name = "history";
            view = name; currentCapture = null; currentPin = null;
            if (preview != null) { Image image = preview.Image; preview.Image = null; if (image != null) image.Dispose(); preview = null; }
            grid = null; bool redraw = content.IsHandleCreated;
            if (redraw) Native.SetRedraw(content.Handle, false);
            content.SuspendLayout();
            try
            {
                Control[] old = new Control[content.Controls.Count]; content.Controls.CopyTo(old, 0);
                content.Controls.Clear(); foreach (Control control in old) control.Dispose();
                foreach (KeyValuePair<string, SoftButton> entry in navigation) { entry.Value.Selected = entry.Key == name; entry.Value.Invalidate(); }
                if (name == "settings") BuildSettings();
                else if (name == "guide") BuildGuide();
                else BuildCollection(name);
            }
            finally
            {
                content.ResumeLayout(true);
                if (redraw)
                {
                    Native.SetRedraw(content.Handle, true);
                    content.Invalidate(true); content.Update();
                }
            }
        }
        private void BuildCollection(string name)
        {
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Line, SplitterWidth = 5, BorderStyle = BorderStyle.FixedSingle };
            split.Panel1.BackColor = split.Panel2.BackColor = Theme.Surface;
            split.Size = new Size(Math.Max(740, content.ClientSize.Width - 20), Math.Max(360, content.ClientSize.Height - 12));
            split.SplitterDistance = (int)(split.Width * .60);
            grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, GridColor = Theme.Line,
                EnableHeadersVisualStyles = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                ReadOnly = true, MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeight = 31, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Ink, SelectionBackColor = Theme.Mint, SelectionForeColor = Color.White, Font = Theme.Font(13), Padding = new Padding(5, 0, 3, 0) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Cream, ForeColor = Theme.Ink, Font = Theme.Font(13), SelectionBackColor = Theme.Cream, Padding = new Padding(4, 0, 0, 0) };
            grid.RowTemplate.Height = 31;
            grid.Columns.Add("title", "名称"); grid.Columns.Add("size", "尺寸"); grid.Columns.Add("time", name == "pins" ? "缩放" : "时间");
            grid.Columns[0].FillWeight = 51; grid.Columns[1].FillWeight = 23; grid.Columns[2].FillWeight = 26;
            preview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(36, 36, 36), SizeMode = PictureBoxSizeMode.Zoom };
            Label detail = Theme.Label("", 12, Theme.Muted); detail.Dock = DockStyle.Bottom; detail.Height = 30; detail.Padding = new Padding(8, 0, 0, 0);
            split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(preview); split.Panel2.Controls.Add(detail); content.Controls.Add(split);
            ContextMenuStrip menu = new ContextMenuStrip { Font = Theme.Font(13) }; Theme.ConfigureMenu(menu);
            menu.Items.Add("贴到桌面 / 定位", null, delegate { OpenSelected(); });
            menu.Items.Add("复制", null, delegate { if (currentCapture != null) app.CopyCapture(currentCapture); else if (currentPin != null) currentPin.CopyImage(); });
            menu.Items.Add("另存为…", null, delegate { if (currentCapture != null) app.ExportCapture(currentCapture); else if (currentPin != null) currentPin.ExportImage(); });
            menu.Items.Add("收藏 / 取消收藏", null, delegate { if (currentCapture != null) { currentCapture.Favorite = !currentCapture.Favorite; app.Save(); ShowView(view); } });
            menu.Items.Add("删除 / 关闭", null, delegate { DeleteSelected(); }); grid.ContextMenuStrip = menu;
            grid.Disposed += delegate { menu.Dispose(); };
            if (name == "pins")
            {
                foreach (StickerForm pin in app.Pins.ToArray())
                {
                    int index = grid.Rows.Add("贴图 " + pin.Record.Id.Substring(0, 6), pin.OriginalSize.Width + " × " + pin.OriginalSize.Height, Math.Round(pin.Record.Scale * 100) + "%");
                    grid.Rows[index].Tag = pin;
                }
            }
            else foreach (CaptureRecord record in app.Store.State.Captures)
            {
                if (name == "favorites" && !record.Favorite) continue;
                int index = grid.Rows.Add((record.Favorite ? "★ " : "") + record.Title, record.Width + " × " + record.Height, record.Created.ToString("MM-dd HH:mm:ss"));
                grid.Rows[index].Tag = record;
            }
            Action select = delegate
            {
                currentCapture = null; currentPin = null;
                if (grid == null || grid.CurrentRow == null) return;
                currentCapture = grid.CurrentRow.Tag as CaptureRecord; currentPin = grid.CurrentRow.Tag as StickerForm;
                string file = currentCapture != null ? currentCapture.File : currentPin != null ? currentPin.Record.File : null;
                if (file == null) return;
                try
                {
                    Image previous = preview.Image;
                    preview.Image = app.Store.LoadImage(file); if (previous != null) previous.Dispose();
                    detail.Text = currentCapture != null ? currentCapture.Width + " × " + currentCapture.Height + " px   PNG" : "100% 原图预览";
                }
                catch (Exception error) { SetStatus(error.Message); }
            };
            grid.SelectionChanged += delegate { select(); };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenSelected(); };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.CurrentCell = grid.Rows[e.RowIndex].Cells[0]; };
            if (grid.Rows.Count > 0) { grid.CurrentCell = grid.Rows[0].Cells[0]; select(); }
            else detail.Text = "无记录";
            SetStatus(grid.Rows.Count + (name == "pins" ? " 张贴图" : " 张截图"));
        }
        private void OpenSelected() { if (currentCapture != null) app.PinCapture(currentCapture); else if (currentPin != null) currentPin.BringForward(); }
        private void DeleteSelected()
        {
            if (currentPin != null) { currentPin.Close(); return; }
            if (currentCapture == null) return;
            if (MessageBox.Show(this, "删除这条截图历史？", "Pinlight", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            app.Store.State.Captures.Remove(currentCapture); app.Save(); app.Clean(); ShowView(view);
        }
        private void BuildSettings()
        {
            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15) }; content.Controls.Add(body);
            Settings settings = app.Store.State.Settings;
            AddCheck(body, "显示放大镜", settings.Magnifier, 15, delegate(bool value) { settings.Magnifier = value; app.Save(); });
            AddCheck(body, "保存截图历史", settings.SaveHistory, 51, delegate(bool value) { settings.SaveHistory = value; app.Save(); });
            AddCheck(body, "启动时恢复贴图", settings.RestorePins, 87, delegate(bool value) { settings.RestorePins = value; app.Save(); });
            AddCheck(body, "截图时隐藏 Pinlight 窗口", settings.HideWindowOnCapture, 123, delegate(bool value)
            {
                settings.HideWindowOnCapture = value;
                Native.PrepareWindow(Handle, value);
                app.Save();
            });
            bool startupReverting = false;
            CheckBox startup = null;
            startup = AddCheck(body, "开机自动启动（后台托盘）", StartupManager.IsEnabled(), 159, delegate(bool value)
            {
                if (startupReverting) return;
                string error;
                if (!StartupManager.SetEnabled(value, out error))
                {
                    startupReverting = true;
                    startup.Checked = !value;
                    startupReverting = false;
                    MessageBox.Show(this, error, "Pinlight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SetStatus(value ? "已开启开机自启动" : "已关闭开机自启动");
            });
            Label opacity = Theme.Label("默认不透明度", 14, Theme.Ink); opacity.SetBounds(15, 193, 180, 28); body.Controls.Add(opacity);
            NumericUpDown number = new NumericUpDown { Minimum = 10, Maximum = 100, Value = settings.DefaultOpacity, Increment = 5, Font = Theme.Font(14), BackColor = Theme.Surface, ForeColor = Theme.Ink };
            number.SetBounds(230, 194, 95, 28); body.Controls.Add(number); number.ValueChanged += delegate { settings.DefaultOpacity = (int)number.Value; app.Save(); };
            Label heading = Theme.Label("快捷键", 16, Theme.Ink); heading.SetBounds(15, 231, 250, 31); body.Controls.Add(heading);
            Label hint = Theme.Label("点击输入框后按组合键，再点应用。", 12, Theme.Muted); hint.SetBounds(15, 264, 450, 25); body.Controls.Add(hint);
            Keys[] current = KeyBindings.Get(settings); HotkeyInput[] inputs = new HotkeyInput[4];
            for (int i = 0; i < inputs.Length; i++)
            {
                Label label = Theme.Label(KeyBindings.Labels[i], 14, Theme.Ink); label.SetBounds(15, 296 + i * 39, 200, 28); body.Controls.Add(label);
                inputs[i] = new HotkeyInput(current[i]); inputs[i].SetBounds(230, 297 + i * 39, 205, 28); body.Controls.Add(inputs[i]);
                inputs[i].Enter += delegate { app.SuspendHotkeys(); };
                inputs[i].Leave += delegate { app.ResumeHotkeys(); };
            }
            Label result = Theme.Label("", 12, Theme.Muted); result.SetBounds(15, 491, 640, 30); body.Controls.Add(result);
            SoftButton apply = new SoftButton("应用快捷键", true); apply.SetBounds(230, 451, 112, 32); body.Controls.Add(apply);
            apply.Click += delegate
            {
                Keys[] requested = new Keys[4]; for (int i = 0; i < 4; i++) requested[i] = inputs[i].Combination;
                string error; bool success = app.UpdateHotkeys(requested, out error);
                result.ForeColor = success ? Theme.Green : Color.Salmon; result.Text = success ? "已应用" : error;
            };
            SoftButton reset = new SoftButton("恢复默认", false); reset.SetBounds(350, 451, 85, 32); body.Controls.Add(reset);
            reset.Click += delegate { Keys[] defaults = KeyBindings.Get(new Settings()); for (int i = 0; i < 4; i++) { inputs[i].Combination = defaults[i]; inputs[i].Text = KeyBindings.Format(defaults[i]); } };
            SoftButton folder = new SoftButton("数据目录", false); folder.SetBounds(15, 530, 110, 32); body.Controls.Add(folder); folder.Click += delegate { Process.Start("explorer.exe", app.Store.Root); };
            SetStatus("设置");
        }
        private static CheckBox AddCheck(Panel panel, string text, bool value, int y, Action<bool> changed)
        {
            CheckBox check = new CheckBox { Text = text, Checked = value, Font = Theme.Font(14), ForeColor = Theme.Ink, AutoSize = true, Location = new Point(15, y) };
            check.CheckedChanged += delegate { changed(check.Checked); }; panel.Controls.Add(check); return check;
        }
        private void BuildGuide()
        {
            Label body = Theme.Label("截图：框选后标注，Enter 复制，Ctrl+S 保存。\n滚动截图：工具栏开启，连续向下滚动，侧边实时预览。\n贴图：拖动移动，滚轮缩放，Ctrl+滚轮透明度。\n文字：选择工具可拖动，双击编辑。\n右键托盘图标可退出程序。", 14, Theme.Muted);
            body.Dock = DockStyle.Top; body.Height = 170; content.Controls.Add(body);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); SetStatus("已最小化到托盘"); }
            base.OnFormClosing(e);
        }
        internal void ExitWindow() { reallyClose = true; Close(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && preview != null && preview.Image != null) { Image image = preview.Image; preview.Image = null; image.Dispose(); }
            if (disposing) delayMenu.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class CaptureTile : UserControl
    {
        private readonly Bitmap image;
        private readonly string title, detail;
        private readonly Action clicked;
        internal CaptureTile(Repository store, string file, string name, string description, Action action)
        {
            title = name; detail = description; clicked = action;
            Size = new Size(260, 194); DoubleBuffered = true; Cursor = Cursors.Hand;
            using (Bitmap full = store.LoadImage(file))
            {
                Rectangle fit = Geometry.Contain(full.Size, new Rectangle(0, 0, 480, 300));
                image = new Bitmap(fit.Width, fit.Height);
                using (Graphics g = Graphics.FromImage(image)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(full, new Rectangle(Point.Empty, image.Size)); }
            }
            ContextMenuStrip = new ContextMenuStrip(); Theme.ConfigureMenu(ContextMenuStrip);
            ContextMenuStrip.Items.Add("贴图", null, delegate { clicked(); });
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            Rectangle area = new Rectangle(5, 5, Width - 10, Height - 65);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image, Geometry.Contain(image.Size, area));
            Theme.Text(e.Graphics, title, 12, Theme.Ink, new Rectangle(9, Height - 54, Width - 18, 24), ContentAlignment.MiddleLeft);
            Theme.Text(e.Graphics, detail, 11, Theme.Muted, new Rectangle(9, Height - 29, Width - 18, 23), ContentAlignment.MiddleLeft);
        }
        protected override void OnDoubleClick(EventArgs e) { clicked(); base.OnDoubleClick(e); }
        protected override void Dispose(bool disposing) { if (disposing) { image.Dispose(); ContextMenuStrip.Dispose(); } base.Dispose(disposing); }
    }
}
