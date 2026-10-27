using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Pinlight
{
    internal sealed class Repository
    {
        internal readonly string Root;
        internal Workspace State;
        internal string LoadWarning;
        internal Repository(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "images"));
            State = new Workspace();
            string file = Path.Combine(Root, "workspace.json");
            if (!File.Exists(file)) return;
            try
            {
                State = ReadState(file);
                if (State == null || State.Settings == null || State.Captures == null || State.Pins == null) throw new InvalidDataException("工作空间格式不完整。");
                State.Captures.RemoveAll(delegate(CaptureRecord r) { return !SafeImage(r.File) || !File.Exists(ImagePath(r.File)); });
                State.Pins.RemoveAll(delegate(PinRecord r) { return !SafeImage(r.File) || !File.Exists(ImagePath(r.File)); });
                State.Settings.DefaultOpacity = Geometry.Clamp(State.Settings.DefaultOpacity, 10, 100);
            }
            catch (Exception exception)
            {
                string backup = file + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
                try { File.Copy(file, backup, false); } catch { }
                State = null;
                foreach (string recovery in new string[] { file + ".pending", file + ".backup" })
                {
                    try
                    {
                        if (!File.Exists(recovery)) continue;
                        Workspace recovered = ReadState(recovery);
                        if (recovered == null || recovered.Settings == null || recovered.Captures == null || recovered.Pins == null) continue;
                        State = recovered; break;
                    }
                    catch (Exception) { }
                }
                LoadWarning = State == null ? "本地记录无法读取，原文件已保留。" : "已从写入备份恢复记录。";
                if (State == null) State = new Workspace();
                Log(exception);
            }
        }
        private static Workspace ReadState(string file)
        {
            using (FileStream stream = File.OpenRead(file)) return (Workspace)new DataContractJsonSerializer(typeof(Workspace)).ReadObject(stream);
        }
        private static void WriteBytes(string file, byte[] data)
        {
            using (FileStream stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length); stream.Flush(true);
            }
        }
        private static bool SafeImage(string file) { return !String.IsNullOrEmpty(file) && file == Path.GetFileName(file) && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase); }
        internal string ImagePath(string file)
        {
            if (!SafeImage(file)) throw new InvalidDataException("无效的本地图像路径。");
            return Path.Combine(Root, "images", file);
        }
        internal Bitmap LoadImage(string file)
        {
            using (Image image = Image.FromFile(ImagePath(file))) return new Bitmap(image);
        }
        internal string StoreImage(Bitmap image)
        {
            string file = Guid.NewGuid().ToString("N") + ".png";
            image.Save(ImagePath(file), ImageFormat.Png);
            return file;
        }
        internal CaptureRecord AddCapture(Bitmap image, string file)
        {
            CaptureRecord record = new CaptureRecord { Id = Guid.NewGuid().ToString("N"), File = file, Title = "截图 " + DateTime.Now.ToString("MM-dd HH:mm:ss"), Width = image.Width, Height = image.Height, Created = DateTime.Now };
            State.Captures.Insert(0, record);
            Save();
            return record;
        }
        internal void Save()
        {
            string file = Path.Combine(Root, "workspace.json"), temp = file + ".pending";
            bool committed = false;
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(Workspace)).WriteObject(stream, State);
                    stream.Flush(true);
                }
                try { Native.CommitFile(temp, file); }
                catch (System.ComponentModel.Win32Exception error)
                {
                    if (error.NativeErrorCode != 17 && error.NativeErrorCode != 5 && error.NativeErrorCode != 50) throw;
                    // EFS/redirected folders may reject rename/replace. Retain a flushed recovery
                    // journal and previous valid bytes before falling back to an in-place write.
                    byte[] next = File.ReadAllBytes(temp);
                    if (File.Exists(file))
                    {
                        try { Workspace previous = ReadState(file); if (previous != null) WriteBytes(file + ".backup", File.ReadAllBytes(file)); }
                        catch (System.Runtime.Serialization.SerializationException) { }
                    }
                    WriteBytes(file, next);
                }
                committed = true;
            }
            finally { if (committed && File.Exists(temp)) { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
        }
        internal void CleanOrphans()
        {
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CaptureRecord capture in State.Captures) keep.Add(capture.File);
            foreach (PinRecord pin in State.Pins) keep.Add(pin.File);
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "images"), "*.png"))
                if (!keep.Contains(Path.GetFileName(file))) { try { File.Delete(file); } catch (IOException) { } }
        }
        internal void Log(Exception exception)
        {
            try { File.AppendAllText(Path.Combine(Root, "errors.log"), DateTime.Now.ToString("s") + " " + exception + Environment.NewLine); } catch { }
        }
    }
}
