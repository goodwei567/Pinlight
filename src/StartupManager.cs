using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pinlight
{
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Pinlight";

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName, null);
                    return value is string && String.Equals(((string)value).Trim(), CurrentCommand(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void RefreshExisting()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null || key.GetValue(ValueName, null) == null) return;
                key.SetValue(ValueName, CurrentCommand(), RegistryValueKind.String);
            }
        }

        internal static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户的启动项。";
                        return false;
                    }
                    if (enabled) key.SetValue(ValueName, CurrentCommand(), RegistryValueKind.String);
                    else if (key.GetValue(ValueName, null) != null) key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "开机自启动设置失败：" + exception.Message;
                return false;
            }
        }

        private static string CurrentCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --tray";
        }
    }
}
