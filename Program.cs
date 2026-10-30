using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("时区切换")]
[assembly: System.Reflection.AssemblyDescription("Windows 11 本地时区切换工具")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace TimeZoneSwitcher
{
    internal static class Program
    {
        internal static readonly int ShowMessage = Native.RegisterWindowMessage("TimeZoneSwitcher.ShowWindow.v1");
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            string userId = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            using (var mutex = new Mutex(true, "Local\\TimeZoneSwitcher.v1." + userId, out created))
            {
                if (!created)
                {
                    // A message-only rendezvous window survives hiding the main window to the tray.
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        IntPtr window = Native.FindWindow(null, "TimeZoneSwitcher.Rendezvous." + userId);
                        if (window != IntPtr.Zero)
                        {
                            uint owner;
                            Native.GetWindowThreadProcessId(window, out owner);
                            Native.AllowSetForegroundWindow(owner);
                            Native.PostMessage(window, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                            return;
                        }
                        Thread.Sleep(100);
                    }
                    MessageBox.Show("工具已在运行，请从右下角托盘打开。", "时区切换");
                    return;
                }
                try
                {
                    var catalog = new ZoneCatalog();
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeZoneSwitcher", "settings.json");
                    var store = new SettingsStore(path, catalog);
                    Application.Run(new MainForm(catalog, store, new TimeZoneService(catalog, new WindowsCommandRunner(), TimeZoneService.ReadSystemId), userId));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("程序无法继续运行：\r\n" + ex.Message, "时区切换", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int RegisterWindowMessage(string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string text);
    }
}
