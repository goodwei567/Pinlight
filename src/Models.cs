using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Pinlight
{
    [DataContract] internal sealed class Settings
    {
        [DataMember] internal bool Magnifier = true;
        [DataMember] internal bool SaveHistory = true;
        [DataMember] internal bool RestorePins = true;
        [DataMember] internal bool HideWindowOnCapture = true;
        [DataMember] internal int DefaultOpacity = 100;
        [DataMember] internal int CaptureKey = (int)Keys.F1;
        [DataMember] internal int PinKey = (int)Keys.F3;
        [DataMember] internal int VisibilityKey = (int)(Keys.Shift | Keys.F3);
        [DataMember] internal int PassThroughKey = (int)Keys.F4;
        [OnDeserializing] private void BeforeLoad(StreamingContext context)
        {
            Magnifier = SaveHistory = RestorePins = HideWindowOnCapture = true; DefaultOpacity = 100;
            CaptureKey = (int)Keys.F1; PinKey = (int)Keys.F3;
            VisibilityKey = (int)(Keys.Shift | Keys.F3); PassThroughKey = (int)Keys.F4;
        }
    }
    [DataContract] internal sealed class CaptureRecord
    {
        [DataMember] internal string Id;
        [DataMember] internal string Title;
        [DataMember] internal string File;
        [DataMember] internal int Width, Height;
        [DataMember] internal DateTime Created;
        [DataMember] internal bool Favorite;
    }
    [DataContract] internal sealed class PinRecord
    {
        [DataMember] internal string Id, File;
        [DataMember] internal int X, Y, Rotation;
        [DataMember] internal double Scale = 1;
        [DataMember] internal double Opacity = 1;
        [DataMember] internal bool Collapsed;
    }
    [DataContract] internal sealed class Workspace
    {
        [DataMember] internal Settings Settings = new Settings();
        [DataMember] internal List<CaptureRecord> Captures = new List<CaptureRecord>();
        [DataMember] internal List<PinRecord> Pins = new List<PinRecord>();
    }
    internal enum Tool { Select, Rectangle, Ellipse, Arrow, Pen, Highlight, Text, Step, Mosaic, Blur }
    internal sealed class Shape
    {
        internal Tool Tool;
        internal Point Start, End;
        internal List<Point> Points = new List<Point>();
        internal Color Color = Theme.Green;
        internal int Width = 3, Number, FontSize = 28;
        internal string Text = "";
        internal Shape Copy()
        {
            return new Shape { Tool = Tool, Start = Start, End = End, Points = new List<Point>(Points), Color = Color, Width = Width, Number = Number, Text = Text, FontSize = FontSize };
        }
        internal Rectangle Bounds
        {
            get
            {
                if (Tool == Tool.Text) return new Rectangle(Start.X, Start.Y, Math.Max(30, (int)Math.Ceiling(Text.Length * FontSize * 0.82)), (Text.Split('\n').Length + 1) * FontSize);
                if (Tool == Tool.Step) return new Rectangle(Start.X - 18, Start.Y - 18, 36, 36);
                if (Points.Count > 0)
                {
                    int x = Points[0].X, y = Points[0].Y, right = x, bottom = y;
                    foreach (Point point in Points) { x = Math.Min(x, point.X); y = Math.Min(y, point.Y); right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y); }
                    return Rectangle.FromLTRB(x, y, right + 1, bottom + 1);
                }
                return Geometry.Rect(Start, End);
            }
        }
        internal void Move(int dx, int dy)
        {
            Start.Offset(dx, dy); End.Offset(dx, dy);
            for (int i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
        }
    }
    internal static class Geometry
    {
        internal static Rectangle Contain(Size image, Rectangle area)
        {
            double scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
            int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }
        internal static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        internal static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        internal static Rectangle Rect(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }
        internal static Rectangle Fit(Rectangle value, Size size)
        {
            int w = Clamp(value.Width, 1, size.Width), h = Clamp(value.Height, 1, size.Height);
            return new Rectangle(Clamp(value.X, 0, size.Width - w), Clamp(value.Y, 0, size.Height - h), w, h);
        }
        internal static Point InBounds(Point point, Rectangle bounds) { return new Point(Clamp(point.X, bounds.Left, bounds.Right - 1), Clamp(point.Y, bounds.Top, bounds.Bottom - 1)); }
    }
    internal sealed class UndoStack
    {
        private List<List<Shape>> states = new List<List<Shape>>();
        private int index;
        internal UndoStack() { Reset(); }
        internal void Reset() { states.Clear(); states.Add(new List<Shape>()); index = 0; }
        internal static List<Shape> Copy(List<Shape> shapes) { return shapes.ConvertAll(delegate(Shape s) { return s.Copy(); }); }
        internal void Push(List<Shape> shapes)
        {
            if (index < states.Count - 1) states.RemoveRange(index + 1, states.Count - index - 1);
            states.Add(Copy(shapes));
            if (states.Count > 50) states.RemoveAt(0);
            index = states.Count - 1;
        }
        internal bool CanUndo { get { return index > 0; } }
        internal bool CanRedo { get { return index < states.Count - 1; } }
        internal List<Shape> Undo() { if (CanUndo) index--; return Copy(states[index]); }
        internal List<Shape> Redo() { if (CanRedo) index++; return Copy(states[index]); }
    }
}
