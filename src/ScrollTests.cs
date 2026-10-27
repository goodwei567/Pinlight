using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class ScrollTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Pump(int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        private static Bitmap Document(int width, int height, int seed)
        {
            Bitmap image = new Bitmap(width, height, PixelFormat.Format32bppArgb); Random random = new Random(seed);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.FromArgb(247, 246, 241));
                for (int y = 0; y < height; y += 27)
                {
                    using (Brush color = new SolidBrush(Color.FromArgb(random.Next(30, 190), random.Next(30, 190), random.Next(30, 190))))
                        graphics.FillRectangle(color, 15 + random.Next(15), y + 5, random.Next(width / 3, width - 60), 5);
                    using (Brush color = new SolidBrush(Color.FromArgb(random.Next(70, 200), random.Next(70, 200), random.Next(70, 200))))
                        graphics.FillRectangle(color, 22 + random.Next(20), y + 15, random.Next(width / 4, width - 70), 6);
                }
            }
            return image;
        }
        private static Bitmap Crop(Bitmap image, int top, int height) { return image.Clone(new Rectangle(0, top, image.Width, height), PixelFormat.Format32bppArgb); }
        private static void EqualPixels(Bitmap result, Bitmap original, int top)
        {
            for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x += 7)
                Assert(result.GetPixel(x, y).ToArgb() == original.GetPixel(x, top + y).ToArgb(), "pixel mismatch at " + x + "," + y);
        }
        private static void Mouse(Control control, string method, Point point)
        {
            typeof(Control).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        }
        private static ScrollCaptureForm GetSession(CaptureForm editor)
        {
            return (ScrollCaptureForm)typeof(CaptureForm).GetField("scrollCapture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
        }
        private static SoftButton FindButton(Control parent, string name)
        {
            foreach (Control control in parent.Controls)
            {
                SoftButton found = control as SoftButton; if (found != null && found.Text == name) return found;
                found = FindButton(control, name); if (found != null) return found;
            }
            return null;
        }
        internal static void Run(Action<string, Action> check, string output)
        {
            check("scroll stitching keeps exact pixels over irregular downward steps", delegate
            {
                using (Bitmap document = Document(480, 2200, 81)) using (Bitmap first = Crop(document, 0, 400))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    foreach (int top in new int[] { 93, 278, 511, 700, 961, 1160, 1440, 1603, 1800 })
                    {
                        using (Bitmap frame = Crop(document, top, 400)) Assert(stitcher.Append(frame) == StitchResult.Appended, "failed offset " + top);
                        Assert(stitcher.Height == top + 400, "wrong shift at " + top + ": " + stitcher.Height);
                    }
                    using (Bitmap result = stitcher.Build()) { EqualPixels(result, document, 0); result.Save(Path.Combine(output, "scroll-stitched.png"), ImageFormat.Png); }
                }
            });
            check("scroll stitches single pixel downward steps without losing rows", delegate
            {
                using (Bitmap document = Document(400, 900, 42)) using (Bitmap first = Crop(document, 0, 320))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    foreach (int top in new int[] { 1, 2, 3, 40, 41 })
                    {
                        using (Bitmap frame = Crop(document, top, 320)) Assert(stitcher.Append(frame) == StitchResult.Appended, "failed small offset " + top);
                        Assert(stitcher.Height == top + 320, "lost rows at " + top);
                    }
                    using (Bitmap result = stitcher.Build()) EqualPixels(result, document, 0);
                }
            });
            check("scroll accepts fast movement with narrow visible overlap", delegate
            {
                using (Bitmap document = Document(480, 2000, 63)) using (Bitmap first = Crop(document, 0, 400))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    foreach (int top in new int[] { 360, 700, 1030, 1370 })
                    {
                        using (Bitmap frame = Crop(document, top, 400))
                            Assert(stitcher.Append(frame) == StitchResult.Appended, "fast frame rejected at " + top);
                        Assert(stitcher.Height == top + 400, "fast frame lost rows at " + top);
                    }
                    using (Bitmap result = stitcher.Build()) EqualPixels(result, document, 0);
                }
            });
            check("scroll matcher remains responsive on a large selection", delegate
            {
                using (Bitmap document = Document(800, 4200, 74)) using (Bitmap first = Crop(document, 0, 600))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    Stopwatch watch = Stopwatch.StartNew(); int frames = 0;
                    foreach (int top in new int[] { 70, 150, 250, 360, 480, 610, 750, 900, 1060, 1230 })
                    using (Bitmap frame = Crop(document, top, 600))
                    {
                        Assert(stitcher.Append(frame) == StitchResult.Appended, "benchmark frame rejected at " + top);
                        frames++;
                    }
                    watch.Stop();
                    double average = watch.Elapsed.TotalMilliseconds / frames;
                    Assert(average < 35, "scroll matching is too slow: " + average.ToString("0.0") + " ms/frame");
                    using (Bitmap initialPreview = stitcher.BuildThumbnail(new Size(240, 600))) { }
                    Stopwatch previewWatch = new Stopwatch();
                    int previewFrames = 0;
                    foreach (int top in new int[] { 1400, 1580, 1770, 1960, 2160, 2370, 2590, 2810 })
                    {
                        using (Bitmap frame = Crop(document, top, 600)) Assert(stitcher.Append(frame) == StitchResult.Appended, "preview frame rejected at " + top);
                        previewWatch.Start();
                        using (Bitmap preview = stitcher.BuildThumbnail(new Size(240, 600))) { }
                        previewWatch.Stop();
                        previewFrames++;
                    }
                    double previewAverage = previewWatch.Elapsed.TotalMilliseconds / previewFrames;
                    File.WriteAllText(Path.Combine(output, "scroll-performance.txt"), "matching=" + average.ToString("0.00") + " ms/frame" + Environment.NewLine + "preview=" + previewAverage.ToString("0.00") + " ms/frame" + Environment.NewLine + "poll=" + ScrollCaptureForm.PollInterval + " ms");
                    Assert(previewAverage < 20, "preview rendering is too slow: " + previewAverage.ToString("0.0") + " ms");
                    using (Bitmap result = stitcher.Build()) EqualPixels(result, document, 0);
                }
            });
            check("scroll duplicate frame does not append repeated content", delegate
            {
                using (Bitmap document = Document(400, 900, 42)) using (Bitmap first = Crop(document, 0, 320))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    Assert(stitcher.Append(first) == StitchResult.Unchanged && stitcher.Height == 320, "first duplicate added");
                    using (Bitmap next = Crop(document, 150, 320))
                    {
                        Assert(stitcher.Append(next) == StitchResult.Appended, "next frame failed");
                        Assert(stitcher.Append(next) == StitchResult.Unchanged && stitcher.Height == 470, "bottom duplicate added");
                    }
                }
            });
            check("scroll rejects unrelated and upward frames and recovers overlap", delegate
            {
                using (Bitmap document = Document(400, 1200, 54)) using (Bitmap other = Document(400, 320, 900))
                using (Bitmap first = Crop(document, 0, 320)) using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    Assert(stitcher.Append(other) == StitchResult.NoMatch && stitcher.Height == 320, "unrelated frame appended");
                    using (Bitmap next = Crop(document, 160, 320)) Assert(stitcher.Append(next) == StitchResult.Appended && stitcher.Height == 480, "recovery failed");
                    Assert(stitcher.Append(first) == StitchResult.NoMatch, "upward scroll appended");
                }
            });
            check("scroll ambiguous blank content and size changes are rejected", delegate
            {
                using (Bitmap first = new Bitmap(300, 240)) using (Bitmap next = new Bitmap(300, 240))
                {
                    using (Graphics g = Graphics.FromImage(first)) g.Clear(Color.White);
                    using (Graphics g = Graphics.FromImage(next)) g.Clear(Color.LightGray);
                    using (ScrollStitcher stitcher = new ScrollStitcher(first))
                    {
                        Assert(stitcher.Append(next) == StitchResult.NoMatch, "blank shift guessed");
                        using (Bitmap wrong = new Bitmap(301, 240))
                        {
                            bool rejected = false; try { stitcher.Append(wrong); } catch (ArgumentException) { rejected = true; }
                            Assert(rejected, "size change accepted");
                        }
                    }
                }
            });
            check("scroll enforces height limit without modifying accepted strips", delegate
            {
                using (Bitmap document = Document(400, 900, 42)) using (Bitmap first = Crop(document, 0, 320))
                using (ScrollStitcher stitcher = new ScrollStitcher(first)) using (Bitmap next = Crop(document, 150, 320))
                {
                    typeof(ScrollStitcher).GetField("<Height>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(stitcher, ScrollStitcher.MaxHeight - 10);
                    Assert(stitcher.Append(next) == StitchResult.Limit && stitcher.FrameCount == 1, "limit changed frames");
                }
            });
            check("scroll thumbnail contains accepted strips without changing original pixels", delegate
            {
                using (Bitmap document = Document(400, 1600, 42)) using (Bitmap first = Crop(document, 0, 320))
                using (ScrollStitcher stitcher = new ScrollStitcher(first))
                {
                    foreach (int top in new int[] { 150, 310, 480, 650, 800 })
                        using (Bitmap next = Crop(document, top, 320)) Assert(stitcher.Append(next) == StitchResult.Appended, "thumbnail setup failed");
                    using (Bitmap exact = stitcher.BuildThumbnail(new Size(400, 1200))) EqualPixels(exact, document, 0);
                    using (Bitmap small = stitcher.BuildThumbnail(new Size(240, 600)))
                    {
                        Assert(small.Width <= 240 && small.Height <= 600, "thumbnail exceeds bounds");
                        Assert(Math.Abs((double)small.Width / small.Height - 400.0 / 1120) < .003, "thumbnail aspect distorted");
                        Assert(small.GetPixel(0, 0).A == 255 && small.GetPixel(0, small.Height - 1).A == 255, "thumbnail has empty edges");
                        small.Save(Path.Combine(output, "scroll-thumbnail.png"), ImageFormat.Png);
                    }
                    using (Bitmap full = stitcher.Build()) EqualPixels(full, document, 0);
                }
            });
            check("scroll preview placement uses right then left and fits negative monitor bounds", delegate
            {
                Rectangle screen = new Rectangle(0, 0, 1600, 900);
                Rectangle area = new Rectangle(100, 100, 500, 400), controls = new Rectangle(100, 510, 700, 88);
                Rectangle right = ScrollPreviewForm.Place(area, screen, controls, new Size(500, 1800));
                Assert(screen.Contains(right) && right.Left > area.Right && !right.IntersectsWith(controls), "right placement overlaps");
                area = new Rectangle(1100, 100, 450, 400); controls = new Rectangle(850, 510, 700, 88);
                Rectangle left = ScrollPreviewForm.Place(area, screen, controls, new Size(450, 1800));
                Assert(screen.Contains(left) && left.Right < area.Left && !left.IntersectsWith(controls), "left placement overlaps");
                screen = new Rectangle(-1600, -200, 1600, 900); area.Offset(-1600, -200); controls.Offset(-1600, -200);
                Rectangle negative = ScrollPreviewForm.Place(area, screen, controls, new Size(450, 1800));
                Assert(screen.Contains(negative) && negative.Right < area.Left && !negative.IntersectsWith(controls), "negative origin placement");
                Rectangle wide = ScrollPreviewForm.Place(screen, screen, new Rectangle(-800, 600, 700, 88), new Size(1600, 30000));
                Assert(screen.Contains(wide) && wide.Width > 12 && wide.Height > 12, "full-screen fallback outside monitor");
            });
            RunLive(check, output);
        }
        private static void RunLive(Action<string, Action> check, string output)
        {
            check("single poll appends rapid frame and updates preview immediately", delegate
            {
                Point cursor = Cursor.Position;
                using (Bitmap document = Document(450, 1200, 281)) using (ScrollSurface surface = new ScrollSurface(document))
                {
                    surface.Show(); surface.Activate(); Pump(100);
                    using (Bitmap first = Imaging.CaptureRegion(surface.Viewport))
                    using (ScrollCaptureForm session = new ScrollCaptureForm(surface.Viewport, first, delegate { }))
                    {
                        session.Show(); Pump(220);
                        surface.ScrollTo(90); Native.FlushDesktop();
                        session.Poll();
                        Assert(session.ResultHeight == 410, "one poll did not append a fast frame");
                        Assert(session.Preview != null && session.Preview.ContentHeight == 410, "preview waited for another tick");
                        session.Close(); Pump(80);
                    }
                    surface.Close();
                }
                Native.SetCursorPos(cursor.X, cursor.Y);
            });
            check("real wheel reaches underlying window and pause resume handles stable frames", delegate
            {
                Point cursor = Cursor.Position;
                using (Bitmap document = Document(450, 1200, 237)) using (ScrollSurface surface = new ScrollSurface(document))
                {
                    surface.Show(); surface.Activate(); Pump(100);
                    using (Bitmap first = Imaging.CaptureRegion(surface.Viewport))
                    using (ScrollCaptureForm session = new ScrollCaptureForm(surface.Viewport, first, delegate { }))
                    {
                        session.Show(); Pump(450);
                        ScrollPreviewForm preview = session.Preview;
                        Assert(preview != null && preview.Visible && preview.ContentHeight == 320, "first preview missing");
                        Assert(!preview.Bounds.IntersectsWith(surface.Viewport), "preview covers scroll content despite side space");
                        Assert((Native.Styles(preview.Handle) & Native.WS_EX_TRANSPARENT) != 0, "preview intercepts clicks");
                        SoftButton pause = FindButton(session, "暂停"); pause.PerformClick();
                        Native.SetCursorPos(surface.Viewport.Left + 220, surface.Viewport.Top + 140);
                        surface.Activate();
                        Native.mouse_event(0x0800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); Pump(800);
                        Assert(surface.Offset == 90, "native wheel was intercepted by capture window");
                        Assert(session.IsPaused && session.ResultHeight == 320, "pause appended a frame");
                        Assert(preview.ContentHeight == 320, "preview changed while paused");
                        FindButton(session, "继续").PerformClick(); Pump(900);
                        Assert(!session.IsPaused && session.ResultHeight == 410, "resume lost overlapping frame");
                        Assert(preview.ContentHeight == 410, "preview failed to grow after resume");
                        session.Close(); Pump(100);
                        Assert(preview.IsDisposed, "preview remains alive after cancel");
                        foreach (Form form in Application.OpenForms) Assert(!(form is ScrollBorderForm), "scroll border left open");
                    }
                    surface.Close();
                }
                Native.SetCursorPos(cursor.X, cursor.Y);
            });
            check("toolbar scroll button releases desktop and cancel restores selection", delegate
            {
                Rectangle bounds;
                using (Bitmap desktop = Imaging.CaptureDesktop(out bounds))
                using (CaptureForm editor = new CaptureForm(new Bitmap(desktop), bounds, new Settings { Magnifier = false }, delegate { }))
                {
                    editor.Show(); Pump(80);
                    Mouse(editor, "OnMouseDown", new Point(120, 100)); Mouse(editor, "OnMouseMove", new Point(1020, 474)); Mouse(editor, "OnMouseUp", new Point(1020, 474));
                    SoftButton button = FindButton(editor, "滚动截图"); Assert(button != null && button.Visible, "toolbar entry missing");
                    using (Bitmap preview = new Bitmap(editor.Width, editor.Height))
                    {
                        editor.DrawToBitmap(preview, new Rectangle(Point.Empty, editor.Size));
                        preview.Save(Path.Combine(output, "scroll-toolbar.png"), ImageFormat.Png);
                    }
                    button.PerformClick(); Pump(90);
                    ScrollCaptureForm session = GetSession(editor);
                    Assert(session != null && session.Visible && !editor.Visible, "desktop input not released");
                    session.Close(); Pump(90);
                    Assert(GetSession(editor) == null && editor.Visible, "selection not restored on cancel");
                    editor.Close();
                }
            });
            check("live scroll captures stitch and export clipboard history and native pin", delegate
            {
                using (Bitmap document = Document(450, 1600, 236))
                using (ScrollSurface surface = new ScrollSurface(document))
                {
                    surface.Show(); surface.Activate(); Pump(120);
                    AppContext app = new AppContext(Path.Combine(output, "scroll-app-" + Guid.NewGuid().ToString("N")), null, false);
                    try
                    {
                        app.Window.Hide();
                        Rectangle viewport = surface.Viewport;
                        Rectangle bounds;
                        using (Bitmap desktop = Imaging.CaptureDesktop(out bounds))
                        using (CaptureForm editor = new CaptureForm(new Bitmap(desktop), bounds, new Settings { Magnifier = false }, delegate(Bitmap result, string action, Point point)
                        {
                            EqualPixels(result, document, 0);
                            typeof(AppContext).GetMethod("FinishCapture", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, new object[] { result, action, point });
                        }))
                        {
                            editor.Show(); Pump(80);
                            Point begin = new Point(viewport.Left - bounds.Left, viewport.Top - bounds.Top);
                            Point end = new Point(begin.X + viewport.Width, begin.Y + viewport.Height);
                            Mouse(editor, "OnMouseDown", begin); Mouse(editor, "OnMouseMove", end); Mouse(editor, "OnMouseUp", end);
                            FindButton(editor, "滚动截图").PerformClick(); Pump(120);
                            ScrollCaptureForm session = GetSession(editor);
                            Assert(session != null && session.Visible, "live scroll session missing");
                            surface.Activate();
                            foreach (int top in new int[] { 140, 310, 480, 670, 880 })
                            {
                                surface.ScrollTo(top); Pump(950);
                                Assert(session.ResultHeight == top + viewport.Height, "live append failed at " + top + ": " + session.ResultHeight);
                                Assert(session.Preview != null && session.Preview.ContentHeight == session.ResultHeight, "live preview is stale");
                            }
                            ScrollPreviewForm side = session.Preview;
                            Assert(side.Visible && side.ImageSize.Width < 450 && side.ImageSize.Height < 1200, "preview not scaled");
                            using (Bitmap thumbnail = new Bitmap(side.Width, side.Height))
                            {
                                side.DrawToBitmap(thumbnail, new Rectangle(Point.Empty, side.Size));
                                thumbnail.Save(Path.Combine(output, "scroll-preview.png"), ImageFormat.Png);
                            }
                            Rectangle sceneBounds = Rectangle.Union(Rectangle.Union(surface.Bounds, side.Bounds), session.Bounds);
                            sceneBounds.Inflate(20, 20);
                            using (Bitmap scene = new Bitmap(sceneBounds.Width, sceneBounds.Height))
                            using (Graphics graphics = Graphics.FromImage(scene))
                            {
                                graphics.Clear(Theme.Paper);
                                foreach (Form form in new Form[] { surface, side, session })
                                using (Bitmap image = new Bitmap(form.Width, form.Height))
                                {
                                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                                    graphics.DrawImageUnscaled(image, form.Left - sceneBounds.Left, form.Top - sceneBounds.Top);
                                }
                                scene.Save(Path.Combine(output, "scroll-preview-layout.png"), ImageFormat.Png);
                            }
                            using (Bitmap preview = new Bitmap(session.Width, session.Height))
                            {
                                session.DrawToBitmap(preview, new Rectangle(Point.Empty, session.Size));
                                preview.Save(Path.Combine(output, "scroll-controls.png"), ImageFormat.Png);
                            }
                            session.Finish("copy"); Pump(100);
                            Assert(side.IsDisposed, "preview left alive after export");
                            Assert(!editor.Visible && app.Store.State.Captures.Count == 1, "export did not finish or save history");
                            using (Image copied = Clipboard.GetImage()) Assert(copied != null && copied.Size == new Size(450, 1200), "scroll clipboard dimensions");
                            app.PinCapture(app.Store.State.Captures[0]);
                            Assert(app.Pins.Count == 1 && app.Pins[0].OriginalSize == new Size(450, 1200), "native pin dimensions");
                        }
                    }
                    finally { app.Exit(); app.Dispose(); surface.Close(); }
                }
            });
            check("overlapping preview is absent from capture with exclusion and hide fallback", delegate
            {
                using (Bitmap document = Document(450, 1200, 276)) using (ScrollSurface surface = new ScrollSurface(document))
                {
                    surface.Show(); surface.Activate(); Pump(120);
                    using (Bitmap first = Imaging.CaptureRegion(surface.Viewport))
                    using (ScrollCaptureForm session = new ScrollCaptureForm(surface.Viewport, first, delegate { }))
                    {
                        session.Show(); Pump(450);
                        ScrollPreviewForm preview = session.Preview;
                        preview.Location = new Point(surface.Left + 40, surface.Top + 20); preview.BringToFront(); Native.FlushDesktop();
                        MethodInfo captureFrame = typeof(ScrollCaptureForm).GetMethod("CaptureFrame", BindingFlags.NonPublic | BindingFlags.Instance);
                        using (Bitmap actual = (Bitmap)captureFrame.Invoke(session, null)) EqualPixels(actual, document, 0);
                        typeof(ScrollPreviewForm).GetField("captureExcluded", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(preview, false);
                        Assert(preview.MustHideForCapture(surface.Viewport), "overlap fallback not enabled");
                        using (Bitmap actual = (Bitmap)captureFrame.Invoke(session, null)) EqualPixels(actual, document, 0);
                        Assert(preview.Visible, "fallback did not restore preview");
                        session.Close(); Assert(preview.IsDisposed, "fallback preview not disposed");
                    }
                    surface.Close();
                }
            });
            check("closing hidden editor releases scroll capture timer and border", delegate
            {
                Rectangle bounds;
                using (Bitmap desktop = Imaging.CaptureDesktop(out bounds))
                using (CaptureForm editor = new CaptureForm(new Bitmap(desktop), bounds, new Settings { Magnifier = false }, delegate { }))
                {
                    editor.Show(); Pump(70);
                    Mouse(editor, "OnMouseDown", new Point(120, 100)); Mouse(editor, "OnMouseMove", new Point(700, 400)); Mouse(editor, "OnMouseUp", new Point(700, 400));
                    FindButton(editor, "滚动截图").PerformClick(); Pump(100);
                    ScrollCaptureForm session = GetSession(editor); Assert(session != null, "session missing");
                    ScrollPreviewForm preview = session.Preview; Assert(preview != null, "preview missing before editor close");
                    editor.Close(); Pump(100);
                    Assert(session.IsDisposed && !editor.Visible, "hidden editor close left scroll session alive");
                    Assert(preview.IsDisposed, "hidden editor close left preview alive");
                    foreach (Form form in Application.OpenForms) Assert(!(form is ScrollBorderForm), "border left alive");
                }
            });
        }
        private sealed class ScrollSurface : Form
        {
            private readonly Bitmap document;
            private int offset;
            internal int Offset { get { return offset; } }
            internal Rectangle Viewport { get { return RectangleToScreen(ClientRectangle); } }
            internal ScrollSurface(Bitmap image)
            {
                document = image; AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual; Location = new Point(100, 120);
                ClientSize = new Size(image.Width, 320); TopMost = true; DoubleBuffered = true;
                Text = "Pinlight scroll test surface";
            }
            internal void ScrollTo(int value)
            {
                offset = Geometry.Clamp(value, 0, document.Height - ClientSize.Height);
                Invalidate(); Update(); Native.FlushDesktop();
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(document, 0, -offset); }
            protected override void OnMouseWheel(MouseEventArgs e) { ScrollTo(offset - Math.Sign(e.Delta) * 90); base.OnMouseWheel(e); }
        }
    }
}
