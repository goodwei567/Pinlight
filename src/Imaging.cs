using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class Imaging
    {
        internal static Bitmap CaptureRegion(Rectangle bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1 || (long)bounds.Width * bounds.Height > ScrollStitcher.MaxPixels) throw new ArgumentException("选区尺寸无效。");
            Bitmap image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch { image.Dispose(); throw; }
        }
        internal static Bitmap CaptureDesktop(out Rectangle bounds)
        {
            bounds = Native.VirtualBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 100000000) throw new InvalidOperationException("桌面尺寸无效或超过首版支持的 1 亿像素。");
            Bitmap image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(image)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch { image.Dispose(); throw; }
        }
        internal static Bitmap OpenImage(string file)
        {
            if (new FileInfo(file).Length > 40 * 1024 * 1024) throw new InvalidOperationException("图片请小于 40 MB。");
            using (Image image = Image.FromFile(file))
            {
                if ((long)image.Width * image.Height > 50000000) throw new InvalidOperationException("图片请控制在 5000 万像素以内。");
                return new Bitmap(image);
            }
        }
        internal static void DrawShapes(Graphics graphics, Bitmap source, IList<Shape> shapes, Rectangle selection)
        {
            GraphicsState saved = graphics.Save();
            graphics.SetClip(selection);
            foreach (Shape shape in shapes) if (shape.Tool == Tool.Mosaic || shape.Tool == Tool.Blur) DrawPrivacy(graphics, source, shape);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Shape shape in shapes) if (shape.Tool != Tool.Mosaic && shape.Tool != Tool.Blur) DrawShape(graphics, shape);
            graphics.Restore(saved);
        }
        internal static void DrawShape(Graphics graphics, Shape shape)
        {
            using (Pen pen = new Pen(shape.Color, shape.Width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Brush brush = new SolidBrush(shape.Color))
            {
                Rectangle rectangle = shape.Bounds;
                if (shape.Tool == Tool.Rectangle && rectangle.Width > 0 && rectangle.Height > 0) graphics.DrawRectangle(pen, rectangle);
                if (shape.Tool == Tool.Ellipse && rectangle.Width > 0 && rectangle.Height > 0) graphics.DrawEllipse(pen, rectangle);
                if (shape.Tool == Tool.Arrow)
                {
                    using (AdjustableArrowCap arrow = new AdjustableArrowCap(4, 6, true)) { pen.CustomEndCap = arrow; graphics.DrawLine(pen, shape.Start, shape.End); }
                }
                if ((shape.Tool == Tool.Pen || shape.Tool == Tool.Highlight) && shape.Points.Count > 1)
                {
                    if (shape.Tool == Tool.Highlight) { pen.Color = Color.FromArgb(90, shape.Color); pen.Width = shape.Width * 6; }
                    graphics.DrawLines(pen, shape.Points.ToArray());
                }
                if (shape.Tool == Tool.Text)
                {
                    using (Font font = Theme.Font(shape.FontSize)) graphics.DrawString(shape.Text, font, brush, shape.Start, StringFormat.GenericTypographic);
                }
                if (shape.Tool == Tool.Step)
                {
                    graphics.FillEllipse(brush, rectangle);
                    Theme.Text(graphics, shape.Number.ToString(), 20, Theme.Surface, rectangle, ContentAlignment.MiddleCenter);
                }
            }
        }
        private static void DrawPrivacy(Graphics graphics, Bitmap source, Shape shape)
        {
            Rectangle rectangle = Rectangle.Intersect(new Rectangle(Point.Empty, source.Size), shape.Bounds);
            if (rectangle.Width < 1 || rectangle.Height < 1) return;
            int block = Math.Max(6, shape.Width * 4);
            int w = Math.Max(1, rectangle.Width / block), h = Math.Max(1, rectangle.Height / block);
            using (Bitmap reduced = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics buffer = Graphics.FromImage(reduced))
                {
                    buffer.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    buffer.DrawImage(source, new Rectangle(0, 0, w, h), rectangle, GraphicsUnit.Pixel);
                }
                GraphicsState saved = graphics.Save();
                graphics.InterpolationMode = shape.Tool == Tool.Mosaic ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(reduced, rectangle, 0, 0, w, h, GraphicsUnit.Pixel, attributes);
                }
                graphics.Restore(saved);
            }
        }
        internal static Bitmap Compose(Bitmap source, IList<Shape> shapes, Rectangle selection)
        {
            selection = Geometry.Fit(selection, source.Size);
            Bitmap output = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    graphics.TranslateTransform(-selection.X, -selection.Y);
                    graphics.DrawImageUnscaled(source, 0, 0);
                    DrawShapes(graphics, source, shapes, selection);
                }
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        internal static Bitmap Rotate(Bitmap source, int rotation)
        {
            Bitmap image = new Bitmap(source);
            int turns = ((rotation % 4) + 4) % 4;
            if (turns == 1) image.RotateFlip(RotateFlipType.Rotate90FlipNone);
            if (turns == 2) image.RotateFlip(RotateFlipType.Rotate180FlipNone);
            if (turns == 3) image.RotateFlip(RotateFlipType.Rotate270FlipNone);
            return image;
        }
        internal static Bitmap TextCard(string text)
        {
            if (text.Length > 4000) text = text.Substring(0, 4000);
            bool isColor = Regex.IsMatch(text.Trim(), "^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$");
            Size measured;
            using (Font font = Theme.Font(27)) measured = TextRenderer.MeasureText(text, font, new Size(590, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int height = isColor ? 410 : Math.Max(100, Math.Min(6000, measured.Height + 72));
            Bitmap image = new Bitmap(700, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Theme.Surface);
                if (isColor)
                {
                    using (Brush brush = new SolidBrush(ColorTranslator.FromHtml(text.Trim()))) g.FillRectangle(brush, 0, 0, 700, 310);
                    Theme.Text(g, text.Trim().ToUpperInvariant(), 32, Theme.Ink, new Rectangle(32, 335, 630, 50), ContentAlignment.MiddleLeft);
                }
                else
                {
                    using (Font font = Theme.Font(27)) TextRenderer.DrawText(g, text, font, new Rectangle(32, 32, 635, height - 64), Theme.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
            }
            return image;
        }
    }
}
