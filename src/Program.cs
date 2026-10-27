using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Pinlight")]
[assembly: AssemblyDescription("Windows screenshot and pin-to-screen utility")]
[assembly: AssemblyCompany("Pinlight")]
[assembly: AssemblyProduct("Pinlight")]
[assembly: AssemblyVersion("0.3.2.0")]
[assembly: AssemblyFileVersion("0.3.2.0")]

namespace Pinlight
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinlightDesktop");
            bool hidden = Array.IndexOf(args, "--tray") >= 0;
            bool test = args.Length > 1 && args[0] == "--test-workspace";
            if (test) root = Path.GetFullPath(args[1]);
            string suffix = test ? ".Test" : "";
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\PinlightDesktop.v1" + suffix, out created))
            using (EventWaitHandle activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PinlightDesktop.Show" + suffix))
            {
                if (!created) { activate.Set(); return 0; }
                AppContext context = null;
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    if (context != null) context.Store.Log(e.Exception);
                    MessageBox.Show("操作未完成：" + e.Exception.Message, "Pinlight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                try
                {
                    context = new AppContext(root, activate, hidden); Application.Run(context);
                    return 0;
                }
                catch (Exception error)
                {
                    try { Directory.CreateDirectory(root); File.AppendAllText(Path.Combine(root, "errors.log"), error.ToString() + Environment.NewLine); } catch { }
                    MessageBox.Show("Pinlight 未能启动：\n" + error.Message, "Pinlight", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { if (context != null) context.Dispose(); mutex.ReleaseMutex(); }
            }
        }
    }
}
