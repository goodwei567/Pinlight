using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Pinlight
{
    internal sealed class AppContext : ApplicationContext
    {
        internal readonly Repository Store;
        internal readonly Icon AppIcon;
        internal readonly List<StickerForm> Pins = new List<StickerForm>();
        internal readonly MainForm Window;
        private readonly NotifyIcon tray;
        private readonly HotkeyWindow hotkeys;
        private readonly System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer signalTimer = new System.Windows.Forms.Timer { Interval = 500 };
        private readonly EventWaitHandle signal;
        private CaptureForm capture;
        private StickerForm activePin;
        private bool pinsHidden, closing, capturePending, mainWasVisible;
        private DateTime hiddenAt;
        private int captureDelay;

        internal AppContext(string dataPath, EventWaitHandle activation, bool hidden)
        {
            Store = new Repository(dataPath); signal = activation;
            using (Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)) AppIcon = executableIcon == null ? (Icon)SystemIcons.Application.Clone() : (Icon)executableIcon.Clone();
            Window = new MainForm(this);
            tray = new NotifyIcon { Icon = AppIcon, Text = "Pinlight", Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip { Font = Theme.Font(13) };
            Theme.ConfigureMenu(menu);
            menu.Items.Add("打开工作空间", null, delegate { ShowWindow(); });
            menu.Items.Add("开始截图    F1", null, delegate { BeginCapture(0); });
            menu.Items.Add("3 秒后截图", null, delegate { BeginCapture(3); });
            menu.Items.Add("剪贴板贴图    F3", null, delegate { PasteClipboard(); });
            menu.Items.Add("隐藏 / 显示全部贴图    Shift+F3", null, delegate { TogglePins(); });
            menu.Items.Add("恢复所有穿透贴图的鼠标操作", null, delegate { RecoverPins(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 Pinlight", null, delegate { Exit(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowWindow(); };
            hotkeys = new HotkeyWindow(Store.State.Settings); hotkeys.Pressed += HandleHotkey; UpdateTrayKeys();
            captureTimer.Tick += delegate { captureTimer.Stop(); StartCapture(); };
            signalTimer.Tick += delegate { if (signal != null && signal.WaitOne(0)) ShowWindow(); }; signalTimer.Start();
            if (!hidden) Window.Show();
            if (Store.State.Settings.RestorePins)
            {
                foreach (PinRecord record in Store.State.Pins.ToArray())
                {
                    try { AddPinWindow(record); } catch (Exception error) { Store.Log(error); }
                }
            }
            if (hotkeys.Conflicts.Count > 0) Notify("快捷键已被其他程序占用：" + String.Join("、", hotkeys.Conflicts.ToArray()) + "。请使用首页或托盘菜单。", true);
            if (Store.LoadWarning != null) Notify(Store.LoadWarning, true);
            Save();
        }
        internal void Notify(string text, bool warning = false)
        {
            if (closing) return;
            Window.SetStatus(text);
            if (warning) tray.ShowBalloonTip(3500, "Pinlight", text, ToolTipIcon.Warning);
        }
        internal void Save()
        {
            try { Store.Save(); }
            catch (Exception error) { Store.Log(error); Notify("本地记录未能保存：" + error.Message, true); }
        }
        internal void Clean() { try { Store.CleanOrphans(); } catch (Exception error) { Store.Log(error); } }
        internal void SuspendHotkeys() { if (hotkeys != null) hotkeys.Suspend(); }
        internal void ResumeHotkeys() { if (hotkeys != null) hotkeys.Resume(); }
        private void UpdateTrayKeys()
        {
            Keys[] keys = KeyBindings.Get(Store.State.Settings);
            tray.ContextMenuStrip.Items[1].Text = "截图    " + KeyBindings.Format(keys[0]);
            tray.ContextMenuStrip.Items[3].Text = "剪贴板贴图    " + KeyBindings.Format(keys[1]);
            tray.ContextMenuStrip.Items[4].Text = "隐藏 / 显示贴图    " + KeyBindings.Format(keys[2]);
        }
        internal bool UpdateHotkeys(Keys[] requested, out string error)
        {
            Keys[] previous = KeyBindings.Get(Store.State.Settings);
            if (!hotkeys.Rebind(requested, out error)) return false;
            KeyBindings.Set(Store.State.Settings, requested);
            try { Store.Save(); UpdateTrayKeys(); return true; }
            catch (Exception exception)
            {
                KeyBindings.Set(Store.State.Settings, previous);
                string ignored; hotkeys.Rebind(previous, out ignored);
                Store.Log(exception); error = "设置未能保存：" + exception.Message; return false;
            }
        }
        internal void ShowWindow()
        {
            if (closing) return;
            Window.Show(); if (Window.WindowState == FormWindowState.Minimized) Window.WindowState = FormWindowState.Normal;
            Window.RefreshView(); Window.Activate();
        }
        private void HandleHotkey(int id)
        {
            if (capture != null && !capture.IsDisposed) { if (id == 2) capture.Hotkey(id); return; }
            if (id == 1) BeginCapture(0);
            else if (id == 2) PasteClipboard();
            else if (id == 3) TogglePins();
            else if (id == 4)
            {
                try
                {
                    if (activePin == null || activePin.IsDisposed) activePin = Pins.Count > 0 ? Pins[Pins.Count - 1] : null;
                    if (activePin == null) { Notify("无贴图"); return; }
                    activePin.TogglePassThrough();
                    Notify(activePin.PassThrough ? "穿透已开启，再按 " + KeyBindings.Format((Keys)Store.State.Settings.PassThroughKey) + " 恢复" : "穿透已关闭");
                }
                catch (Exception error) { Store.Log(error); Notify(error.Message, true); }
            }
        }
        internal void BeginCapture(int delaySeconds)
        {
            if (closing || capturePending || (capture != null && !capture.IsDisposed)) return;
            mainWasVisible = Window.Visible; capturePending = true;
            Native.PrepareWindow(Window.Handle, true);
            Window.Hide();
            hiddenAt = DateTime.UtcNow; captureDelay = delaySeconds > 0 ? delaySeconds * 1000 : 320;
            captureTimer.Interval = captureDelay;
            captureTimer.Start();
        }
        private void StartCapture()
        {
            if (closing) return;
            if (Native.IsWindowVisible(Window.Handle) || (DateTime.UtcNow - hiddenAt).TotalMilliseconds < captureDelay)
            {
                Window.Hide(); captureTimer.Interval = 60; captureTimer.Start(); return;
            }
            Bitmap image = null;
            try
            {
                Native.RefreshDesktop(); Rectangle bounds; image = Imaging.CaptureDesktop(out bounds);
                capture = new CaptureForm(image, bounds, Store.State.Settings, FinishCapture); image = null;
                capture.FormClosed += delegate
                {
                    CaptureForm old = capture; capture = null; capturePending = false;
                    if (old != null) old.Dispose();
                    if (mainWasVisible && !closing) ShowWindow();
                };
                capture.Show();
            }
            catch (Exception error)
            {
                if (image != null) image.Dispose();
                capturePending = false; if (mainWasVisible) ShowWindow();
                Store.Log(error); Notify("截图未能开始：" + error.Message, true);
            }
        }
        private void FinishCapture(Bitmap image, string action, Point position)
        {
            string file = null;
            string warning = null;
            if (Store.State.Settings.SaveHistory || action == "pin")
            {
                try
                {
                    file = Store.StoreImage(image);
                    if (Store.State.Settings.SaveHistory) Store.AddCapture(image, file);
                }
                catch (Exception error)
                {
                    Store.Log(error); warning = "历史未保存：" + error.Message;
                    if (action == "pin" && file == null) throw;
                }
            }
            if (action == "pin") CreatePin(image, file, position, true);
            Notify((action == "copy" ? "已复制" : action == "save" ? "已保存" : "已贴图") + (warning == null ? "" : "；" + warning), warning != null);
            Window.RefreshView();
        }
        private void AddPinWindow(PinRecord record)
        {
            Bitmap image = Store.LoadImage(record.File); StickerForm form;
            try { form = new StickerForm(image, record, OnPinChanged); }
            catch { image.Dispose(); throw; }
            Pins.Add(form); form.Activated += delegate { activePin = form; };
            form.Show(); if (pinsHidden) form.Hide(); activePin = form;
        }
        private void OnPinChanged(StickerForm form, bool removed)
        {
            if (removed)
            {
                Pins.Remove(form); Store.State.Pins.Remove(form.Record);
                if (activePin == form) activePin = Pins.Count > 0 ? Pins[Pins.Count - 1] : null;
            }
            Save(); if (removed) { Clean(); Window.RefreshView(); }
        }
        private void CreatePin(Bitmap image, string existingFile, Point position, bool exactPosition)
        {
            string file = existingFile ?? Store.StoreImage(image);
            Rectangle area = Screen.FromPoint(position).WorkingArea;
            double scale = 1;
            if (!exactPosition) scale = Math.Min(1, Math.Min((double)(area.Width * .6) / image.Width, (double)(area.Height * .65) / image.Height));
            PinRecord record = new PinRecord { Id = Guid.NewGuid().ToString("N"), File = file, X = position.X, Y = position.Y, Scale = Geometry.Clamp(scale, .1, 10), Opacity = Store.State.Settings.DefaultOpacity / 100.0 };
            Store.State.Pins.Add(record);
            try { AddPinWindow(record); Save(); }
            catch { Store.State.Pins.Remove(record); throw; }
        }
        internal void PasteClipboard()
        {
            if (closing || capturePending) return;
            try
            {
                Bitmap image = null;
                if (Clipboard.ContainsImage()) using (Image clipboardImage = Clipboard.GetImage()) { if (clipboardImage != null) image = new Bitmap(clipboardImage); }
                else if (Clipboard.ContainsFileDropList())
                {
                    foreach (string file in Clipboard.GetFileDropList())
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif") { image = Imaging.OpenImage(file); break; }
                    }
                }
                else if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText(); if (!String.IsNullOrWhiteSpace(text)) image = Imaging.TextCard(text);
                }
                if (image == null) { Notify("剪贴板无图片或文字"); return; }
                using (image)
                {
                    if ((long)image.Width * image.Height > 50000000) throw new InvalidOperationException("剪贴板图片过大，请控制在 5000 万像素以内。");
                    Point point = Cursor.Position; CreatePin(image, null, new Point(point.X + 12, point.Y + 12), false);
                }
                Window.RefreshView(); Notify("已从剪贴板创建置顶贴图。");
            }
            catch (Exception error) { Store.Log(error); Notify("贴图未完成：" + error.Message, true); }
        }
        internal void PinCapture(CaptureRecord record)
        {
            try { using (Bitmap image = Store.LoadImage(record.File)) CreatePin(image, record.File, new Point(Window.Left + 280, Window.Top + 150), false); Window.RefreshView(); }
            catch (Exception error) { Store.Log(error); Notify(error.Message, true); }
        }
        internal void CopyCapture(CaptureRecord record)
        {
            try { using (Bitmap image = Store.LoadImage(record.File)) ClipboardService.CopyImage(image); Notify("已复制"); }
            catch (Exception error) { Notify(error.Message, true); }
        }
        internal void ExportCapture(CaptureRecord record)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "PNG 图像|*.png", DefaultExt = "png", AddExtension = true, FileName = record.Title.Replace(":", "-") + ".png", OverwritePrompt = true })
                if (dialog.ShowDialog(Window) == DialogResult.OK)
                    try { using (Bitmap image = Store.LoadImage(record.File)) image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png); }
                    catch (Exception error) { MessageBox.Show(Window, error.Message, "保存失败"); }
        }
        internal void ImportPicture()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择一张图片贴到桌面", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*", CheckFileExists = true })
                if (dialog.ShowDialog(Window) == DialogResult.OK)
                    try
                    {
                        using (Bitmap image = Imaging.OpenImage(dialog.FileName))
                        {
                            string file = Store.StoreImage(image); Store.AddCapture(image, file);
                            CreatePin(image, file, new Point(Window.Left + 270, Window.Top + 160), false);
                        }
                        Window.RefreshView();
                    }
                    catch (Exception error) { Store.Log(error); MessageBox.Show(Window, error.Message, "导入失败"); }
        }
        internal void TogglePins()
        {
            pinsHidden = !pinsHidden; foreach (StickerForm form in Pins) form.SetVisible(!pinsHidden);
            Notify(pinsHidden ? "贴图已隐藏" : "贴图已显示");
        }
        private void RecoverPins()
        {
            foreach (StickerForm form in Pins.ToArray()) if (form.PassThrough) form.TogglePassThrough();
            Notify("所有贴图已恢复鼠标操作。");
        }
        internal void Exit()
        {
            if (closing) return;
            closing = true;
            captureTimer.Stop(); signalTimer.Stop();
            if (capture != null && !capture.IsDisposed) capture.Close();
            foreach (StickerForm form in Pins.ToArray()) form.Persist();
            Save();
            foreach (StickerForm form in Pins.ToArray()) form.Shutdown();
            Pins.Clear();
            hotkeys.Dispose(); captureTimer.Dispose(); signalTimer.Dispose();
            tray.Visible = false;
            if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            Window.ExitWindow(); Window.Dispose(); AppIcon.Dispose();
            ExitThread();
        }
    }
}
