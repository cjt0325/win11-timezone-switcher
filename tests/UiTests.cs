using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TimeZoneSwitcher;

internal static class UiTests
{
    private static int count;
    private static void Check(bool value, string description)
    { if (!value) throw new Exception("FAIL: " + description); count++; Console.WriteLine("PASS: " + description); }
    private static T Get<T>(object target, string name)
    { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Invoke(object target, string name, params object[] args)
    { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
    private static IEnumerable<Control> Tree(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (Control nested in Tree(child)) yield return nested;
    }
    private static void WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
        Check(condition(), "async operation finishes");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string temp = Path.Combine(Path.GetTempPath(), "TimeZoneSwitcher-UI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var catalog = new ZoneCatalog();
            var store = new SettingsStore(Path.Combine(temp, "settings.json"), catalog);
            string current = "China Standard Time";
            var runner = new FakeRunner();
            runner.OnRun = () => { current = runner.Arguments.Contains("Pacific") ? "Pacific Standard Time" : "Eastern Standard Time"; };
            using (var form = new MainForm(catalog, store, new TimeZoneService(catalog, runner, () => current), "ui-test", true))
            {
                IntPtr handle = form.Handle; form.PerformLayout();
                var favorites = Get<ZoneList>(form, "favorites");
                var available = Get<ZoneList>(form, "available");
                var search = Get<TextBox>(form, "search");
                search.Text = "东京";
                Check(available.Items.Count == 1 && ((ZoneEntry)available.Items[0]).Id == "Tokyo Standard Time", "search filters native catalog");
                available.SelectedIndex = 0; Invoke(form, "AddFavorite");
                Check(favorites.Items.Count == 4 && store.Load().FavoriteIds.Contains("Tokyo Standard Time"), "favorite added and persisted from UI");
                Invoke(form, "RemoveFavorite");
                Check(favorites.Items.Count == 3 && !store.Load().FavoriteIds.Contains("Tokyo Standard Time"), "favorite removed and persisted from UI");
                var menu = Get<ContextMenuStrip>(form, "trayMenu");
                ((ToolStripMenuItem)menu.Items[2]).PerformClick();
                WaitUntil(() => !Get<bool>(form, "busy"));
                Check(Get<Label>(form, "currentZone").Text.Contains("洛杉矶") && ((ToolStripMenuItem)menu.Items[2]).Checked, "tray action switches and checks actual timezone");
                runner.OnRun = null; runner.Result = new CommandResult { ExitCode = 1, Error = "Insufficient privileges" };
                ((ToolStripMenuItem)menu.Items[3]).PerformClick();
                WaitUntil(() => !Get<bool>(form, "busy"));
                Check(Get<Label>(form, "status").Text.Contains("Insufficient privileges") && Get<Button>(form, "retry").Enabled, "failure reaches status and administrator retry");
                runner.Result = new CommandResult { ExitCode = -1, Cancelled = true, Error = "cancelled" };
                // Invoke the same method used by the admin button, with a fake runner; no UAC dialog is opened.
                var retryTask = (System.Threading.Tasks.Task)typeof(MainForm).GetMethod("SwitchAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { "Eastern Standard Time", true });
                WaitUntil(() => retryTask.IsCompleted);
                Check(Get<Label>(form, "status").Text == "cancelled" && current == "Pacific Standard Time", "cancelled elevation leaves no success message");
                foreach (int dpi in new[] { 96, 120, 144 })
                {
                    using (var layout = new MainForm(catalog, store, new TimeZoneService(catalog, runner, () => current), "layout", true))
                    {
                        IntPtr layoutHandle = layout.Handle;
                        var originalFonts = Tree(layout).ToDictionary(c => c, c => c.Font);
                        float ratio = (float)dpi / layout.DeviceDpi;
                        layout.AutoScaleMode = AutoScaleMode.None;
                        layout.Scale(new SizeF(ratio, ratio));
                        foreach (var pair in originalFonts) pair.Key.Font = new Font(pair.Value.FontFamily, pair.Value.Size * ratio, pair.Value.Style);
                        foreach (var list in Tree(layout).OfType<ZoneList>()) list.ItemHeight = (int)(60 * dpi / 96F);
                        layout.PerformLayout();
                        foreach (var label in Tree(layout).OfType<Label>())
                            Check(label.Height >= label.Font.Height, "label height at " + dpi + " DPI: " + label.Text);
                        using (var bitmap = new Bitmap(layout.Width, layout.Height))
                        {
                            typeof(Control).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(layout, new object[] { 2, true });
                            layout.CreateControl();
                            layout.DrawToBitmap(bitmap, new Rectangle(Point.Empty, layout.Size));
                            if (args.Length > 1) bitmap.Save(Path.Combine(args[1], "layout-" + dpi + ".png"));
                        }
                    }
                }
                form.Close();
                Check(!form.IsDisposed && !form.Visible, "closing window keeps tray application alive");
                ((ToolStripMenuItem)menu.Items[menu.Items.Count - 1]).PerformClick();
                Check(form.IsDisposed, "exit menu closes and disposes application");
            }
            if (args.Length > 0)
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                bool first;
                using (var mutex = new Mutex(true, "Local\\TimeZoneSwitcher.v1." + user, out first))
                {
                    Check(first, "no prior instance running during single-instance test");
                    bool received = false;
                    using (var rendezvous = new RendezvousWindow(user, () => { received = true; }))
                    using (var second = Process.Start(new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                    {
                        WaitUntil(() => second.HasExited && received);
                        Check(second.ExitCode == 0 && received, "second launch signals existing hidden rendezvous and exits");
                    }
                    mutex.ReleaseMutex();
                }
            }
            Console.WriteLine("TOTAL UI/LAYOUT: " + count + " passed"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
