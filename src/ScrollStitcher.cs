using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Pinlight
{
    internal enum StitchResult { Appended, Unchanged, NoMatch, Limit }

    // Match overlapping physical pixels; store only the newly exposed strip for each frame.
    internal sealed class ScrollStitcher : IDisposable
    {
        internal const int MaxHeight = 30000;
        internal const long MaxPixels = 50000000;
        private Bitmap previous;
        private PixelFrame previousPixels;
        private readonly List<Bitmap> strips = new List<Bitmap>();
        private Bitmap preview;
        private int previewSourceHeight, previewStripCount;
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int FrameCount { get { return strips.Count; } }
        internal ScrollStitcher(Bitmap first)
        {
            if (first.Width < 80 || first.Height < 100) throw new ArgumentException("滚动区域至少为 80 × 100 像素。");
            if ((long)first.Width * first.Height > MaxPixels) throw new ArgumentException("选区过大。");
            Width = first.Width; Height = first.Height;
            previous = new Bitmap(first); previousPixels = new PixelFrame(previous);
            strips.Add(new Bitmap(first));
        }
        internal StitchResult Append(Bitmap next)
        {
            if (next.Width != Width || next.Height != previous.Height) throw new ArgumentException("滚动过程中选区尺寸发生变化。");
            PixelFrame pixels = new PixelFrame(next);
            if (previousPixels.Similar(pixels, 1.0, .015)) return StitchResult.Unchanged;
            int shift = FindShift(previousPixels, pixels);
            if (shift < 1) return StitchResult.NoMatch;
            if (Height + shift > MaxHeight || (long)Width * (Height + shift) > MaxPixels) return StitchResult.Limit;
            Bitmap strip = next.Clone(new Rectangle(0, next.Height - shift, Width, shift), PixelFormat.Format32bppArgb);
            Bitmap replacement = null;
            try { replacement = new Bitmap(next); strips.Add(strip); }
            catch { strip.Dispose(); if (replacement != null) replacement.Dispose(); throw; }
            previous.Dispose(); previous = replacement; previousPixels = pixels; Height += shift;
            return StitchResult.Appended;
        }
        private sealed class Match
        {
            internal int Shift, Information;
            internal double Error, Ratio;
        }
        private static int FindShift(PixelFrame previous, PixelFrame next)
        {
            // Rapid scrolling must still be replayable. A 24px strip carries enough
            // edge information for normal text/UI; the ambiguity guard below rejects
            // blank or repetitive content instead of guessing.
            int minOverlap = Math.Max(24, previous.Height / 12);
            int maxShift = previous.Height - minOverlap;
            int rowStep = Math.Max(2, previous.Height / 90);
            SampleGrid previousCoarse = previous.Sample(20), nextCoarse = next.Sample(20);
            List<Match> candidates = new List<Match>();
            for (int shift = 1; shift <= maxShift; shift++)
            {
                Match match = Score(previousCoarse, nextCoarse, shift, rowStep);
                if (match.Information < 18) continue;
                candidates.Add(match);
            }
            candidates.Sort(delegate(Match a, Match b) { return a.Error.CompareTo(b.Error); });
            SampleGrid previousFine = previous.Sample(44), nextFine = next.Sample(44);
            List<Match> refined = new List<Match>();
            for (int i = 0; i < Math.Min(12, candidates.Count); i++) refined.Add(Score(previousFine, nextFine, candidates[i].Shift, Math.Max(1, previous.Height / 200)));
            refined.Sort(delegate(Match a, Match b) { return a.Error.CompareTo(b.Error); });
            if (refined.Count == 0) return -1;
            Match best = refined[0];
            if (best.Information < 30 || best.Error > 7 || best.Ratio < .90) return -1;
            // Repetitive/blank content is ambiguous: pause rather than invent or duplicate rows.
            foreach (Match other in refined)
                if (Math.Abs(other.Shift - best.Shift) > 5 && other.Error <= best.Error + 1.0) return -1;
            return best.Shift;
        }
        private static Match Score(SampleGrid a, SampleGrid b, int shift, int rowStep)
        {
            double sum = 0; int count = 0, matched = 0;
            for (int y = 3; y < a.Height - shift - 3; y += rowStep)
            for (int column = 0; column < a.Columns; column++)
            {
                int i = (y + shift) * a.Columns + column, j = y * b.Columns + column;
                int feature = Math.Max(a.Features[i], b.Features[j]);
                if (feature < 14) continue;
                int error = PixelFrame.Difference(a.Colors[i], b.Colors[j]);
                sum += Math.Min(90, error); count++;
                if (error <= 16) matched++;
            }
            return new Match { Shift = shift, Error = count == 0 ? 255 : sum / count, Ratio = count == 0 ? 0 : (double)matched / count, Information = count };
        }
        internal Bitmap Build()
        {
            Bitmap output = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    int y = 0; foreach (Bitmap strip in strips) { g.DrawImageUnscaled(strip, 0, y); y += strip.Height; }
                }
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        internal Bitmap BuildThumbnail(Size bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1) throw new ArgumentException("Invalid preview size.");
            double scale = Math.Min(1, Math.Min((double)bounds.Width / Width, (double)bounds.Height / Height));
            int width = Math.Max(1, (int)Math.Round(Width * scale));
            int height = Math.Max(1, (int)Math.Round(Height * scale));
            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(output))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                    if (width == Width && height == Height)
                    {
                        int exactY = 0;
                        foreach (Bitmap strip in strips) { g.DrawImageUnscaled(strip, 0, exactY); exactY += strip.Height; }
                    }
                    else if (preview != null && previewStripCount <= strips.Count && previewSourceHeight <= Height)
                    {
                        // Reuse the already scaled overview for old content and draw only
                        // the newly accepted strips. This keeps live preview work bounded.
                        int oldBottom = (int)Math.Round((double)previewSourceHeight * height / Height);
                        if (oldBottom > 0) g.DrawImage(preview, new Rectangle(0, 0, width, oldBottom), 0, 0, preview.Width, preview.Height, GraphicsUnit.Pixel, attributes);
                        int y = 0;
                        for (int i = 0; i < strips.Count; i++)
                        {
                            Bitmap strip = strips[i]; int next = y + strip.Height;
                            if (next > previewSourceHeight)
                            {
                                int sourceTop = Math.Max(0, previewSourceHeight - y);
                                int top = (int)Math.Round((double)(y + sourceTop) * height / Height);
                                int bottom = (int)Math.Round((double)next * height / Height);
                                if (bottom > top)
                                    g.DrawImage(strip, new Rectangle(0, top, width, bottom - top), 0, sourceTop, strip.Width, strip.Height - sourceTop, GraphicsUnit.Pixel, attributes);
                            }
                            y = next;
                        }
                    }
                    else
                    {
                        // First preview: render accepted strips without allocating a full-resolution image.
                        int y = 0;
                        foreach (Bitmap strip in strips)
                        {
                            int top = (int)((long)y * height / Height);
                            y += strip.Height;
                            int bottom = (int)((long)y * height / Height);
                            if (bottom > top) g.DrawImage(strip, new Rectangle(0, top, width, bottom - top), 0, 0, strip.Width, strip.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                }
                if (preview != null) preview.Dispose();
                preview = new Bitmap(output); previewSourceHeight = Height; previewStripCount = strips.Count;
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (preview != null) { preview.Dispose(); preview = null; }
            if (previous != null) { previous.Dispose(); previous = null; }
            foreach (Bitmap strip in strips) strip.Dispose(); strips.Clear(); previousPixels = null;
        }
    }

    internal sealed class PixelFrame
    {
        internal readonly int[] Pixels;
        internal readonly int Width, Height;
        internal PixelFrame(Bitmap bitmap)
        {
            Width = bitmap.Width; Height = bitmap.Height; Pixels = new int[Width * Height];
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), Pixels, y * Width, Width); }
            finally { bitmap.UnlockBits(data); }
        }
        internal static int Difference(int a, int b)
        {
            return (Math.Abs((a & 255) - (b & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255))) / 3;
        }
        internal int Edge(int index)
        {
            return Math.Max(Difference(Pixels[index], Pixels[index - 2]), Math.Max(Difference(Pixels[index], Pixels[index + 2]), Difference(Pixels[index], Pixels[index + Width * 2])));
        }
        internal bool Similar(PixelFrame other, double averageLimit, double changedLimit)
        {
            if (other == null || Width != other.Width || Height != other.Height) return false;
            long total = 0; int count = 0, changed = 0;
            int stepX = Math.Max(1, Width / 100), stepY = Math.Max(1, Height / 100);
            for (int y = 0; y < Height; y += stepY) for (int x = 0; x < Width; x += stepX)
            {
                int i = y * Width + x, error = Difference(Pixels[i], other.Pixels[i]);
                total += error; count++; if (error > 12) changed++;
            }
            return (double)total / count <= averageLimit && (double)changed / count <= changedLimit;
        }
        private Dictionary<int, SampleGrid> samples;
        internal SampleGrid Sample(int columns)
        {
            if (samples == null) samples = new Dictionary<int, SampleGrid>();
            SampleGrid grid;
            if (samples.TryGetValue(columns, out grid)) return grid;
            int left = Math.Max(4, Width / 22), right = Width - left;
            int[] xs = new int[columns];
            for (int column = 0; column < columns; column++)
                xs[column] = left + (right - left - 1) * column / Math.Max(1, columns - 1);
            int[] colors = new int[Height * columns];
            byte[] features = new byte[Height * columns];
            for (int y = 2; y < Height - 2; y++)
            for (int column = 0; column < columns; column++)
            {
                int x = xs[column], index = y * Width + x, sample = y * columns + column;
                colors[sample] = Pixels[index];
                features[sample] = (byte)Math.Min(255, Edge(index));
            }
            grid = new SampleGrid(columns, Height, colors, features);
            samples.Add(columns, grid);
            return grid;
        }
    }

    internal sealed class SampleGrid
    {
        internal readonly int Columns, Height;
        internal readonly int[] Colors;
        internal readonly byte[] Features;
        internal SampleGrid(int columns, int height, int[] colors, byte[] features)
        {
            Columns = columns; Height = height; Colors = colors; Features = features;
        }
    }
}
