using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312;
        private const int WM_SETREDRAW = 0x000B;
        internal const int GWL_EXSTYLE = -20;
        internal const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool MoveFileEx(string source, string destination, uint flags);
        [DllImport("shell32.dll")] internal static extern IntPtr ExtractIcon(IntPtr instance, string file, int index);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch { }
        }
        internal static void FlushDesktop() { try { DwmFlush(); } catch { } }
        internal static bool ExcludeCapture(IntPtr handle) { return SetWindowDisplayAffinity(handle, 0x11); }
        internal static uint CaptureAffinity(IntPtr handle)
        {
            uint affinity;
            return GetWindowDisplayAffinity(handle, out affinity) ? affinity : uint.MaxValue;
        }
        internal static void PrepareWindow(IntPtr handle, bool excludeFromCapture)
        {
            try
            {
                int value = 1;
                DwmSetWindowAttribute(handle, 20, ref value, 4);
                // Disable only this window's DWM transitions; no system animation settings are changed.
                DwmSetWindowAttribute(handle, 3, ref value, 4);
                SetWindowDisplayAffinity(handle, excludeFromCapture ? 0x11u : 0u);
            }
            catch (DllNotFoundException) { }
        }
        internal static void RefreshDesktop()
        {
            RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100 | 0x0400);
            FlushDesktop(); FlushDesktop();
        }
        internal static void SetRedraw(IntPtr handle, bool enabled)
        {
            SendMessage(handle, WM_SETREDRAW, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
        }
        internal static void CommitFile(string source, string destination)
        {
            // Rename within the same EFS directory rather than ReplaceFile's ACL/metadata merge.
            if (!MoveFileEx(source, destination, 0x0001 | 0x0008)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        internal static Rectangle VirtualBounds { get { return new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); } }
        internal static long Styles(IntPtr handle) { return IntPtr.Size == 8 ? GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt64() : GetWindowLong32(handle, GWL_EXSTYLE); }
        internal static void SetPassThrough(IntPtr handle, bool enabled, byte alpha)
        {
            long style = Styles(handle);
            style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~WS_EX_TRANSPARENT;
            Marshal.GetLastWin32Error();
            if (IntPtr.Size == 8) SetWindowLongPtr64(handle, GWL_EXSTYLE, new IntPtr(style));
            else SetWindowLong32(handle, GWL_EXSTYLE, (int)style);
            if ((Styles(handle) & WS_EX_TRANSPARENT) != (enabled ? WS_EX_TRANSPARENT : 0)) throw new System.ComponentModel.Win32Exception("无法更改贴图穿透状态。");
            if (!SetLayeredWindowAttributes(handle, 0, alpha, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020 | 0x0010);
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        internal event Action<int> Pressed;
        private readonly List<int> registered = new List<int>();
        internal readonly List<string> Conflicts = new List<string>();
        private Keys[] bindings;
        private bool suspended;
        internal HotkeyWindow() : this(new Settings()) { }
        internal HotkeyWindow(Settings settings)
        {
            CreateHandle(new CreateParams { Caption = "Pinlight.Hotkeys", Parent = new IntPtr(-3) });
            bindings = KeyBindings.Get(settings);
            for (int i = 0; i < bindings.Length; i++) if (!Register(i + 1, bindings[i])) Conflicts.Add(KeyBindings.Labels[i] + " " + KeyBindings.Format(bindings[i]));
        }
        private bool Register(int id, Keys combination)
        {
            uint modifiers = 0x4000;
            if ((combination & Keys.Control) != 0) modifiers |= 2;
            if ((combination & Keys.Alt) != 0) modifiers |= 1;
            if ((combination & Keys.Shift) != 0) modifiers |= 4;
            if (!Native.RegisterHotKey(Handle, id, modifiers, (uint)(combination & Keys.KeyCode))) return false;
            registered.Add(id); return true;
        }
        private void Clear() { foreach (int id in registered) Native.UnregisterHotKey(Handle, id); registered.Clear(); }
        internal void Suspend() { if (suspended) return; suspended = true; Clear(); }
        internal void Resume()
        {
            if (!suspended) return; suspended = false; Conflicts.Clear();
            for (int i = 0; i < bindings.Length; i++) if (!Register(i + 1, bindings[i])) Conflicts.Add(KeyBindings.Labels[i]);
        }
        internal bool Rebind(Keys[] requested, out string error)
        {
            error = KeyBindings.Validate(requested); if (error != null) return false;
            if (suspended) { error = "请先完成快捷键输入。"; return false; }
            Keys[] previous = (Keys[])bindings.Clone(); Clear();
            for (int i = 0; i < requested.Length; i++)
            {
                if (Register(i + 1, requested[i])) continue;
                error = KeyBindings.Format(requested[i]) + " 已被占用，未修改快捷键。";
                Clear(); Conflicts.Clear();
                for (int j = 0; j < previous.Length; j++) if (!Register(j + 1, previous[j])) Conflicts.Add(KeyBindings.Labels[j]);
                return false;
            }
            bindings = (Keys[])requested.Clone(); Conflicts.Clear(); return true;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(message.WParam.ToInt32());
            base.WndProc(ref message);
        }
        public void Dispose() { Clear(); DestroyHandle(); }
    }
}
