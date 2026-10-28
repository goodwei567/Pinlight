using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class SelfTests
    {
        private static int passed, failed;
        private static readonly StringBuilder report = new StringBuilder();
        private static void Check(string name, Action test)
        {
            try { test(); passed++; report.AppendLine("PASS " + name); }
            catch (Exception error) { failed++; report.AppendLine("FAIL " + name + ": " + error); }
        }
        private static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        private static void Mouse(Control control, string method, Point point, MouseButtons buttons)
        {
            typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new MouseEventArgs(buttons, 1, point.X, point.Y, 0) });
        }
        private static void Pump(int milliseconds)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        private static void Press(Keys key)
        {
            Native.keybd_event((byte)key, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)key, 0, 2, UIntPtr.Zero);
        }
        private static Bitmap Sample()
        {
            Bitmap image = new Bitmap(640, 420, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Theme.Paper);
                for (int y = 0; y < 420; y += 8) for (int x = 0; x < 640; x += 8)
                    using (Brush brush = new SolidBrush(Color.FromArgb((x * 7) % 255, (y * 3) % 255, ((x + y) * 5) % 255))) g.FillRectangle(brush, x, y, 8, 8);
            }
            return image;
        }
        internal static int Run(string output)
        {
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            Check("geometry clamps negative and overflowing rectangles", delegate
            {
                Rectangle value = Geometry.Fit(new Rectangle(-22, 430, 200, 500), new Size(640, 420));
                Assert(value == new Rectangle(0, 0, 200, 420), value.ToString());
                Assert(Geometry.Rect(new Point(20, 30), new Point(-5, 10)) == new Rectangle(-5, 10, 25, 20), "reverse drag");
            });
            Check("undo redo deep copy and branching", delegate
            {
                UndoStack history = new UndoStack();
                List<Shape> shapes = new List<Shape> { new Shape { Tool = Tool.Pen, Points = new List<Point> { new Point(3, 4), new Point(8, 9) } } };
                history.Push(shapes); shapes[0].Points[0] = new Point(99, 99);
                Assert(history.Undo().Count == 0, "undo to empty");
                Assert(history.Redo()[0].Points[0] == new Point(3, 4), "must deep copy");
                history.Undo(); history.Push(new List<Shape> { new Shape { Tool = Tool.Rectangle } });
                Assert(!history.CanRedo, "branch discards redo");
            });
            Check("native capture returns physical virtual desktop", delegate
            {
                Rectangle bounds;
                using (Bitmap image = Imaging.CaptureDesktop(out bounds)) Assert(image.Width == Native.GetSystemMetrics(78) && image.Height == Native.GetSystemMetrics(79), bounds.ToString());
            });
            Check("pixel exact crop without annotations", delegate
            {
                using (Bitmap source = Sample()) using (Bitmap result = Imaging.Compose(source, new List<Shape>(), new Rectangle(23, 31, 240, 180)))
                {
                    Assert(result.Size == new Size(240, 180), "output dimensions");
                    Assert(result.GetPixel(33, 42).ToArgb() == source.GetPixel(56, 73).ToArgb(), "physical pixel mismatch");
                }
            });
            Check("annotation tools and privacy layer export", delegate
            {
                using (Bitmap source = Sample())
                {
                    List<Shape> shapes = new List<Shape>();
                    shapes.Add(new Shape { Tool = Tool.Mosaic, Start = new Point(50, 50), End = new Point(240, 120), Width = 6 });
                    shapes.Add(new Shape { Tool = Tool.Blur, Start = new Point(260, 50), End = new Point(440, 120), Width = 6 });
                    shapes.Add(new Shape { Tool = Tool.Rectangle, Start = new Point(45, 45), End = new Point(245, 125), Color = Color.Red, Width = 4 });
                    shapes.Add(new Shape { Tool = Tool.Ellipse, Start = new Point(300, 150), End = new Point(460, 260), Color = Color.Blue, Width = 4 });
                    shapes.Add(new Shape { Tool = Tool.Arrow, Start = new Point(90, 260), End = new Point(280, 180), Color = Color.Red, Width = 4 });
                    shapes.Add(new Shape { Tool = Tool.Text, Start = new Point(40, 320), Text = "Pinlight test", Color = Theme.Green, FontSize = 28 });
                    shapes.Add(new Shape { Tool = Tool.Step, Start = new Point(540, 90), Number = 1, Color = Theme.Green });
                    shapes.Add(new Shape { Tool = Tool.Pen, Points = new List<Point> { new Point(470, 300), new Point(500, 340), new Point(540, 295) }, Color = Color.Red, Width = 4 });
                    shapes.Add(new Shape { Tool = Tool.Highlight, Points = new List<Point> { new Point(40, 375), new Point(220, 375) }, Color = Color.Gold, Width = 4 });
                    using (Bitmap result = Imaging.Compose(source, shapes, new Rectangle(0, 0, 640, 420)))
                    {
                        result.Save(Path.Combine(output, "annotation-tools.png"), ImageFormat.Png);
                        Assert(result.GetPixel(45, 80).R > 220, "rectangle annotation is missing");
                        Assert(result.GetPixel(70, 70).ToArgb() != source.GetPixel(70, 70).ToArgb(), "privacy filter is missing");
                    }
                }
            });
            Check("text and color cards render", delegate
            {
                using (Bitmap text = Imaging.TextCard("Keep a good idea nearby.\nPinlight"))
                using (Bitmap color = Imaging.TextCard("#789B89"))
                {
                    Assert(text.Width == 700 && text.Height >= 100, "text dimensions");
                    Assert(color.GetPixel(50, 50).ToArgb() == ColorTranslator.FromHtml("#789B89").ToArgb(), "color value");
                    color.Save(Path.Combine(output, "color-card.png"), ImageFormat.Png);
                }
            });
            Check("rotation preserves pixel dimensions", delegate
            {
                using (Bitmap source = Sample()) using (Bitmap turned = Imaging.Rotate(source, 1)) Assert(turned.Size == new Size(420, 640), "rotation size");
            });
            Check("atomic repository save reload and orphan cleanup", delegate
            {
                string root = Path.Combine(output, "repository-" + Guid.NewGuid().ToString("N"));
                Repository repository = new Repository(root);
                using (Bitmap image = Sample())
                {
                    string file = repository.StoreImage(image); repository.AddCapture(image, file);
                    repository.State.Settings.DefaultOpacity = 75; repository.State.Settings.HideWindowOnCapture = false; repository.Save();
                    Repository reloaded = new Repository(root);
                    Assert(reloaded.State.Captures.Count == 1 && reloaded.State.Settings.DefaultOpacity == 75, "save reload");
                    Assert(!reloaded.State.Settings.HideWindowOnCapture, "capture window visibility setting did not reload");
                    string orphan = reloaded.StoreImage(image); reloaded.CleanOrphans();
                    Assert(!File.Exists(reloaded.ImagePath(orphan)), "orphan remains");
                    Assert(File.Exists(reloaded.ImagePath(file)), "referenced image removed");
                    bool blocked = false; try { reloaded.ImagePath("..\\private.png"); } catch (InvalidDataException) { blocked = true; }
                    Assert(blocked, "path traversal not blocked");
                }
            });
            Check("capture form drag resize annotation and pin callback", delegate
            {
                string resultAction = null; Size resultSize = Size.Empty; Point resultLocation = Point.Empty;
                using (CaptureForm form = new CaptureForm(Sample(), new Rectangle(20, 20, 640, 420), new Settings { Magnifier = false }, delegate(Bitmap image, string action, Point point)
                {
                    resultAction = action; resultSize = image.Size; resultLocation = point;
                    image.Save(Path.Combine(output, "capture-result.png"), ImageFormat.Png);
                }))
                {
                    form.Show(); Application.DoEvents();
                    Mouse(form, "OnMouseDown", new Point(80, 70), MouseButtons.Left);
                    Mouse(form, "OnMouseMove", new Point(430, 300), MouseButtons.Left);
                    Mouse(form, "OnMouseUp", new Point(430, 300), MouseButtons.Left);
                    typeof(CaptureForm).GetField("tool", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, Tool.Rectangle);
                    Mouse(form, "OnMouseDown", new Point(120, 110), MouseButtons.Left);
                    Mouse(form, "OnMouseMove", new Point(250, 200), MouseButtons.Left);
                    Mouse(form, "OnMouseUp", new Point(250, 200), MouseButtons.Left);
                    using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, form.ClientRectangle); preview.Save(Path.Combine(output, "capture-editor.png"), ImageFormat.Png); }
                    form.Hotkey(2);
                    Assert(resultAction == "pin" && resultSize == new Size(350, 230), "capture result dimensions or action");
                    Assert(resultLocation == new Point(100, 90), "screen origin mapping");
                }
            });
            Check("native sticker topmost toolwindow passthrough and disposal", delegate
            {
                PinRecord record = new PinRecord { Id = "selftest-pin", File = "test.png", X = 120, Y = 120, Scale = .5, Opacity = .9 };
                using (StickerForm form = new StickerForm(Sample(), record, delegate { }))
                {
                    form.Show(); Application.DoEvents();
                    Assert(form.TopMost && !form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None, "native attributes");
                    Assert((Native.Styles(form.Handle) & Native.WS_EX_TOOLWINDOW) != 0, "toolwindow style missing");
                    form.TogglePassThrough(); Assert((Native.Styles(form.Handle) & Native.WS_EX_TRANSPARENT) != 0, "enable passthrough");
                    form.TogglePassThrough(); Assert((Native.Styles(form.Handle) & Native.WS_EX_TRANSPARENT) == 0, "disable passthrough");
                    typeof(StickerForm).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.None, 0, 50, 50, 120) });
                    Assert(record.Scale > .5, "wheel zoom failed");
                    form.Persist(); form.Shutdown();
                }
            });
            Check("workspace UI pages and persistence lifecycle", delegate
            {
                string workspace = Path.Combine(output, "ui-" + Guid.NewGuid().ToString("N"));
                AppContext app = new AppContext(workspace, null, false);
                try
                {
                    app.Window.Show(); Application.DoEvents();
                    using (Bitmap preview = new Bitmap(app.Window.Width, app.Window.Height))
                    {
                        app.Window.DrawToBitmap(preview, new Rectangle(Point.Empty, app.Window.Size));
                        preview.Save(Path.Combine(output, "home.png"), ImageFormat.Png);
                    }
                    foreach (string view in new string[] { "history", "pins", "favorites", "settings", "guide" })
                    {
                        app.Window.ShowView(view); Application.DoEvents();
                        using (Bitmap preview = new Bitmap(app.Window.Width, app.Window.Height))
                        {
                            app.Window.DrawToBitmap(preview, new Rectangle(Point.Empty, app.Window.Size));
                            preview.Save(Path.Combine(output, "page-" + view + ".png"), ImageFormat.Png);
                        }
                    }
                    using (Bitmap image = Sample())
                    {
                        typeof(AppContext).GetMethod("FinishCapture", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, new object[] { image, "pin", new Point(200, 150) });
                    }
                    Assert(app.Pins.Count == 1 && app.Store.State.Captures.Count == 1, "capture to history and native pin");
                    app.TogglePins(); Assert(!app.Pins[0].Visible, "hide all");
                    app.TogglePins(); Assert(app.Pins[0].Visible, "show all");
                    app.Pins[0].Persist();
                    app.Window.ShowView("history"); Application.DoEvents();
                    using (Bitmap preview = new Bitmap(app.Window.Width, app.Window.Height))
                    {
                        app.Window.DrawToBitmap(preview, new Rectangle(Point.Empty, app.Window.Size));
                        preview.Save(Path.Combine(output, "history-with-image.png"), ImageFormat.Png);
                    }
                }
                finally { app.Exit(); app.Dispose(); }
                Repository saved = new Repository(workspace);
                Assert(saved.State.Captures.Count == 1 && saved.State.Pins.Count == 1, "exit did not retain capture and pin");
                AppContext restored = new AppContext(workspace, null, true);
                try { Assert(restored.Pins.Count == 1, "pin restore failed"); }
                finally { restored.Exit(); restored.Dispose(); }
            });
            Check("real global F1 and F3 deliver screenshot to native sticker", delegate
            {
                AppContext app = new AppContext(Path.Combine(output, "hotkey-" + Guid.NewGuid().ToString("N")), null, false);
                try
                {
                    app.Window.Activate(); Pump(200);
                    Press(Keys.F1); Pump(1100);
                    CaptureForm editor = (CaptureForm)typeof(AppContext).GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                    Assert(editor != null && editor.Visible, "global F1 was not received");
                    Mouse(editor, "OnMouseDown", new Point(40, 40), MouseButtons.Left);
                    Mouse(editor, "OnMouseMove", new Point(200, 160), MouseButtons.Left);
                    Mouse(editor, "OnMouseUp", new Point(200, 160), MouseButtons.Left);
                    Press(Keys.F3); Pump(400);
                    Assert(app.Pins.Count == 1 && app.Store.State.Captures.Count == 1, "global F3 did not pin capture");
                    Assert(app.Store.State.Captures[0].Width == 160 && app.Store.State.Captures[0].Height == 120, "selection size");
                    Press(Keys.F4); Pump(150); Assert(app.Pins[0].PassThrough, "global F4 enable");
                    Press(Keys.F4); Pump(150); Assert(!app.Pins[0].PassThrough, "global F4 recover");
                    Native.keybd_event((byte)Keys.ShiftKey, 0, 0, UIntPtr.Zero); Press(Keys.F3); Native.keybd_event((byte)Keys.ShiftKey, 0, 2, UIntPtr.Zero);
                    Pump(150); Assert(!app.Pins[0].Visible, "global Shift+F3 hide");
                    Native.keybd_event((byte)Keys.ShiftKey, 0, 0, UIntPtr.Zero); Press(Keys.F3); Native.keybd_event((byte)Keys.ShiftKey, 0, 2, UIntPtr.Zero);
                    Pump(150); Assert(app.Pins[0].Visible, "global Shift+F3 show");
                }
                finally { app.Exit(); app.Dispose(); }
            });
            Check("legacy settings retain default hotkeys", delegate
            {
                string root = Path.Combine(output, "legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "workspace.json"), "{\"Captures\":[],\"Pins\":[],\"Settings\":{\"DefaultOpacity\":80,\"Magnifier\":false,\"RestorePins\":true,\"SaveHistory\":true}}");
                Repository store = new Repository(root);
                Assert(store.State.Settings.CaptureKey == (int)Keys.F1 && store.State.Settings.PinKey == (int)Keys.F3, "legacy hotkey defaults missing");
                Assert(!store.State.Settings.Magnifier && store.State.Settings.DefaultOpacity == 80, "legacy settings changed");
                Assert(store.State.Settings.HideWindowOnCapture, "legacy capture visibility default missing");
            });
            Check("EFS workspace repeated atomic commits preserve data", delegate
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinlightTests", Guid.NewGuid().ToString("N"));
                Repository store = new Repository(root);
                for (int i = 0; i < 8; i++) { store.State.Settings.DefaultOpacity = 70 + i; store.Save(); }
                Assert(new Repository(root).State.Settings.DefaultOpacity == 77, "EFS repeated save lost latest value");
                report.AppendLine("INFO EFS workspace attributes=" + File.GetAttributes(Path.Combine(root, "workspace.json")));
            });
            Check("aspect ratio retained for portrait landscape and square", delegate
            {
                foreach (Size image in new Size[] { new Size(300, 900), new Size(1920, 1080), new Size(400, 400) })
                {
                    Rectangle fit = Geometry.Contain(image, new Rectangle(5, 5, 600, 300));
                    Assert(fit.Width <= 600 && fit.Height <= 300 && fit.X >= 5 && fit.Y >= 5, "preview exceeds bounds");
                    Assert(Math.Abs((double)fit.Width / fit.Height - (double)image.Width / image.Height) < .01, "preview stretched");
                }
            });
            Check("clipboard image persists after source disposal", delegate
            {
                using (Bitmap source = Sample()) ClipboardService.CopyImage(source);
                Assert(Clipboard.ContainsImage(), "clipboard image missing");
                using (Image read = Clipboard.GetImage())
                {
                    Assert(read.Size == new Size(640, 420), "clipboard resolution changed");
                    using (Bitmap pixels = new Bitmap(read)) using (Bitmap source = Sample()) Assert(pixels.GetPixel(56, 73).ToArgb() == source.GetPixel(56, 73).ToArgb(), "clipboard pixels changed");
                }
                Assert(Clipboard.GetDataObject().GetDataPresent("PNG"), "lossless PNG clipboard format missing");
            });
            Check("hotkey validation rejects duplicate and editing shortcuts", delegate
            {
                Keys[] defaults = KeyBindings.Get(new Settings());
                Assert(KeyBindings.Validate(defaults) == null, "defaults invalid");
                defaults[0] = defaults[1]; Assert(KeyBindings.Validate(defaults) != null, "duplicate accepted");
                defaults[0] = Keys.Control | Keys.C; Assert(KeyBindings.Validate(defaults) != null, "copy hijack accepted");
            });
            Check("custom hotkeys save rollback conflict and actual activation", delegate
            {
                string workspace = Path.Combine(output, "custom-" + Guid.NewGuid().ToString("N"));
                AppContext app = new AppContext(workspace, null, false);
                IntPtr blockHandle = IntPtr.Zero;
                try
                {
                    Keys[] keys = { Keys.Control | Keys.Alt | Keys.F8, Keys.Control | Keys.Alt | Keys.F9, Keys.Control | Keys.Alt | Keys.F10, Keys.Control | Keys.Alt | Keys.F11 };
                    string error; Assert(app.UpdateHotkeys(keys, out error), error);
                    Assert(new Repository(workspace).State.Settings.CaptureKey == (int)keys[0], "custom bindings not saved");
                    using (Form blocker = new Form())
                    {
                        blockHandle = blocker.Handle;
                        Assert(Native.RegisterHotKey(blockHandle, 888, 0x4000 | 1 | 2, (uint)Keys.F7), "test conflict reservation failed");
                        Keys[] conflict = (Keys[])keys.Clone(); conflict[0] = Keys.Control | Keys.Alt | Keys.F7;
                        Assert(!app.UpdateHotkeys(conflict, out error), "external conflict accepted");
                        Assert(app.Store.State.Settings.CaptureKey == (int)keys[0], "conflict changed settings");
                        Native.UnregisterHotKey(blockHandle, 888); blockHandle = IntPtr.Zero;
                    }
                    Native.keybd_event((byte)Keys.ControlKey, 0, 0, UIntPtr.Zero); Native.keybd_event((byte)Keys.Menu, 0, 0, UIntPtr.Zero);
                    Press(Keys.F8);
                    Native.keybd_event((byte)Keys.Menu, 0, 2, UIntPtr.Zero); Native.keybd_event((byte)Keys.ControlKey, 0, 2, UIntPtr.Zero);
                    Pump(800);
                    CaptureForm editor = (CaptureForm)typeof(AppContext).GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                    Assert(editor != null && editor.Visible, "custom screenshot hotkey not delivered after rollback");
                    editor.Close(); Pump(120);
                }
                finally { if (blockHandle != IntPtr.Zero) Native.UnregisterHotKey(blockHandle, 888); app.Exit(); app.Dispose(); }
            });
            Check("hidden window is absent from captured pixels across three captures", delegate
            {
                Color backgroundColor = Color.FromArgb(71, 93, 121);
                using (Form background = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(50, 50, 1000, 700), TopMost = true, BackColor = backgroundColor, ShowInTaskbar = false })
                {
                    background.Show(); Pump(150);
                    AppContext app = new AppContext(Path.Combine(output, "ghost-" + Guid.NewGuid().ToString("N")), null, false);
                    try
                    {
                        app.Window.TopMost = true; app.Window.SetBounds(80, 80, 850, 550);
                        for (int i = 0; i < 3; i++)
                        {
                            app.ShowWindow(); app.Window.BringToFront(); Pump(100);
                            app.BeginCapture(0); Pump(780);
                            CaptureForm editor = (CaptureForm)typeof(AppContext).GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                            Assert(editor != null && editor.Visible, "editor missing");
                            Bitmap captured = (Bitmap)typeof(CaptureForm).GetField("source", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                            Rectangle desktop = Native.VirtualBounds;
                            for (int y = 140; y < 500; y += 41) for (int x = 140; x < 800; x += 47)
                                Assert(captured.GetPixel(x - desktop.X, y - desktop.Y).ToArgb() == backgroundColor.ToArgb(), "window residue at " + x + "," + y);
                            if (i == 2) using (Bitmap proof = captured.Clone(new Rectangle(120 - desktop.X, 120 - desktop.Y, 700, 400), PixelFormat.Format32bppArgb)) proof.Save(Path.Combine(output, "hidden-window-check.png"), ImageFormat.Png);
                            editor.Close(); Pump(150);
                        }
                    }
                    finally { app.Exit(); app.Dispose(); background.Close(); }
                }
            });
            Check("capture visibility setting controls workspace hiding and capture exclusion", delegate
            {
                string workspace = Path.Combine(output, "capture-visibility-" + Guid.NewGuid().ToString("N"));
                AppContext app = new AppContext(workspace, null, false);
                try
                {
                    app.Window.ShowView("settings"); Pump(100);
                    CheckBox option = null;
                    Action<Control> visit = null;
                    visit = delegate(Control parent)
                    {
                        foreach (Control control in parent.Controls)
                        {
                            if (control is CheckBox && control.Text == "截图时隐藏 Pinlight 窗口") option = (CheckBox)control;
                            visit(control);
                        }
                    };
                    visit(app.Window);
                    Assert(option != null && option.Checked, "capture visibility option missing");
                    option.Checked = false; Pump(80);
                    Assert(!app.Store.State.Settings.HideWindowOnCapture, "capture visibility option not applied");
                    Assert(Native.CaptureAffinity(app.Window.Handle) == 0, "workspace remains excluded from capture");
                    app.BeginCapture(0); Pump(800);
                    CaptureForm editor = (CaptureForm)typeof(AppContext).GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                    Assert(editor != null && editor.Visible, "capture editor missing");
                    Assert(app.Window.Visible, "workspace was hidden despite the disabled option");
                    editor.Close(); Pump(150);
                    Assert(!new Repository(workspace).State.Settings.HideWindowOnCapture, "capture visibility option was not saved");
                }
                finally { app.Exit(); app.Dispose(); }
            });
            Check("copy closes successfully even when history file is locked", delegate
            {
                string root = Path.Combine(output, "locked-" + Guid.NewGuid().ToString("N"));
                AppContext app = new AppContext(root, null, false);
                try
                {
                    app.Store.Save();
                    using (FileStream locked = new FileStream(Path.Combine(root, "workspace.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    using (CaptureForm editor = new CaptureForm(Sample(), new Rectangle(20, 20, 640, 420), new Settings { Magnifier = false }, delegate(Bitmap image, string action, Point point)
                    {
                        typeof(AppContext).GetMethod("FinishCapture", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, new object[] { image, action, point });
                    }))
                    {
                        editor.Show(); Pump(60);
                        Mouse(editor, "OnMouseDown", new Point(80, 70), MouseButtons.Left);
                        Mouse(editor, "OnMouseMove", new Point(430, 300), MouseButtons.Left);
                        Mouse(editor, "OnMouseUp", new Point(430, 300), MouseButtons.Left);
                        typeof(CaptureForm).GetMethod("Complete", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, new object[] { "copy" });
                        Assert(!editor.Visible, "history failure left copy editor open");
                        using (Image copied = Clipboard.GetImage()) Assert(copied != null && copied.Size == new Size(350, 230), "copy image lost due to history failure");
                    }
                }
                finally { app.Exit(); app.Dispose(); }
            });
            Check("hotkey recorder accepts a binding while current global keys are suspended", delegate
            {
                AppContext app = new AppContext(Path.Combine(output, "recorder-" + Guid.NewGuid().ToString("N")), null, false);
                try
                {
                    app.Window.ShowView("settings"); Pump(100);
                    List<HotkeyInput> inputs = new List<HotkeyInput>();
                    Action<Control> visit = null;
                    visit = delegate(Control parent) { foreach (Control control in parent.Controls) { if (control is HotkeyInput) inputs.Add((HotkeyInput)control); visit(control); } };
                    visit(app.Window); Assert(inputs.Count == 4, "recorder fields missing");
                    inputs[0].Focus(); Pump(50);
                    Press(Keys.F3); Pump(50);
                    Assert(inputs[0].Combination == Keys.F3, "current global shortcut intercepted recorder input");
                    app.Window.Focus(); app.ResumeHotkeys();
                }
                finally { app.Exit(); app.Dispose(); }
            });
            Check("main toolbar uses transient capture highlight and avoids duplicate view rebuilds", delegate
            {
                AppContext app = new AppContext(Path.Combine(output, "toolbar-" + Guid.NewGuid().ToString("N")), null, false);
                try
                {
                    List<SoftButton> buttons = new List<SoftButton>();
                    Action<Control> visit = null;
                    visit = delegate(Control parent)
                    {
                        foreach (Control control in parent.Controls)
                        {
                            SoftButton button = control as SoftButton;
                            if (button != null) buttons.Add(button);
                            visit(control);
                        }
                    };
                    visit(app.Window);
                    SoftButton capture = buttons.Find(delegate(SoftButton button) { return button.Text == "截图"; });
                    Assert(capture != null && !capture.Primary, "capture button is permanently highlighted");
                    ContextMenuStrip delayMenu = (ContextMenuStrip)typeof(MainForm).GetField("delayMenu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app.Window);
                    Assert(delayMenu.Items.Count == 3 && delayMenu.Items[0].Text == "3 秒" && delayMenu.Items[1].Text == "5 秒" && delayMenu.Items[2].Text == "10 秒", "delay choices missing");
                    Panel content = (Panel)typeof(MainForm).GetField("content", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app.Window);
                    Control current = content.Controls.Count > 0 ? content.Controls[0] : null;
                    Assert(current != null, "history view missing");
                    typeof(MainForm).GetMethod("OpenView", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app.Window, new object[] { "history" });
                    Assert(ReferenceEquals(current, content.Controls[0]), "duplicate view rebuild");
                }
                finally { app.Exit(); app.Dispose(); }
            });
            Check("pending journal recovers an interrupted in-place save", delegate
            {
                string root = Path.Combine(output, "recovery-" + Guid.NewGuid().ToString("N"));
                Repository store = new Repository(root); store.State.Settings.DefaultOpacity = 65; store.Save();
                string file = Path.Combine(root, "workspace.json"); File.WriteAllBytes(file + ".pending", File.ReadAllBytes(file)); File.WriteAllText(file, "{\"invalid\":");
                Repository recovered = new Repository(root);
                Assert(recovered.State.Settings.DefaultOpacity == 65 && recovered.LoadWarning != null, "pending journal not recovered");
                recovered.Save(); Assert(new Repository(root).State.Settings.DefaultOpacity == 65, "recovered data not committed");
            });
            ScrollTests.Run(Check, output);
            report.AppendLine("RESULT " + passed + " passed, " + failed + " failed");
            File.WriteAllText(Path.Combine(output, "self-test-report.txt"), report.ToString(), Encoding.UTF8);
            return failed == 0 ? 0 : 1;
        }
    }
}
