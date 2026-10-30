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
        int exitCode = 1;
        using (var starter = new System.Windows.Forms.Timer { Interval = 1 })
        {
            starter.Tick += delegate { starter.Stop(); try { exitCode = RunChecks(args); } finally { Application.ExitThread(); } };
            starter.Start();
            // Keep a real UI message loop alive; DoEvents alone uninstalls the async context.
            Application.Run();
        }
        return exitCode;
    }
    private static int RunChecks(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "TimeZoneSwitcher-UI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var catalog = new ZoneCatalog();
            var store = new SettingsStore(Path.Combine(temp, "settings.json"), catalog);
            string current = "China Standard Time";
            var runner = new FakeRunner();
            runner.OnRun = () => { current = runner.Arguments.Substring(3).Trim('"'); };
            using (var form = new MainForm(catalog, store, new TimeZoneService(catalog, runner, () => current), "ui-test", true))
            {
                IntPtr handle = form.Handle;
                // Managed visibility enables button-event tests without displaying a native window.
                typeof(Control).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { 2, true });
                form.CreateControl(); form.PerformLayout();
                var favorites = Get<ZoneList>(form, "favorites");
                var available = Get<ZoneList>(form, "available");
                var search = Get<TextBox>(form, "search");
                search.Text = "德国时间";
                Check(available.Items.Cast<ZoneEntry>().Any(z => z.Id == "W. Europe Standard Time"), "country search reaches UI results");
                Check(Get<TextBox>(form, "preview").Text.Contains("Windows：") && Get<TextBox>(form, "preview").Text.Contains("IANA："), "preview explains identifiers and local time");
                search.Text = "美国";
                Check(available.Items.Count > 1 && Get<Label>(form, "resultsCount").Text.Contains(available.Items.Count.ToString()), "multi-zone country displays result count");
                search.Text = "no-such-city-xyz123";
                Check(available.Items.Count == 0 && !Get<Button>(form, "switchAvailable").Enabled && !Get<Button>(form, "copyInfo").Enabled, "empty results disable switching and stale preview copying");
                Get<Button>(form, "clearSearch").PerformClick();
                Check(search.TextLength == 0 && available.Items.Count == catalog.All.Count, "clear returns complete list");
                search.Text = "日本时间";
                Check(available.Items.Count == 1 && ((ZoneEntry)available.Items[0]).Id == "Tokyo Standard Time" &&
                    ((ZoneEntry)available.Items[0]).Name.Contains("日本"), "Japanese country name filters and labels Tokyo timezone");
                search.Text = "JST";
                Check(available.Items.Count == 1 && ((ZoneEntry)available.Items[0]).Id == "Tokyo Standard Time", "JST alias filters Tokyo timezone");
                search.Text = "东京";
                Check(available.Items.Count == 1 && ((ZoneEntry)available.Items[0]).Id == "Tokyo Standard Time", "search filters native catalog");
                available.SelectedIndex = 0; Invoke(form, "AddFavorite");
                Check(favorites.Items.Count == 4 && store.Load().FavoriteIds.Contains("Tokyo Standard Time"), "favorite added and persisted from UI");
                Invoke(form, "RemoveFavorite");
                Check(favorites.Items.Count == 3 && !store.Load().FavoriteIds.Contains("Tokyo Standard Time"), "favorite removed and persisted from UI");
                favorites.SelectedIndex = 1; Invoke(form, "MoveFavorite", -1);
                Check(((ZoneEntry)favorites.Items[0]).Id == "Pacific Standard Time" && store.Load().FavoriteIds[0] == "Pacific Standard Time", "favorite reorder persists identity and order");
                Invoke(form, "MoveFavorite", 1);
                var menu = Get<ContextMenuStrip>(form, "trayMenu");
                ((ToolStripMenuItem)menu.Items[2]).PerformClick();
                WaitUntil(() => !Get<bool>(form, "busy"));
                Check(Get<Label>(form, "currentZone").Text.Contains("洛杉矶") && ((ToolStripMenuItem)menu.Items[2]).Checked, "tray action switches and checks actual timezone");
                Check(Get<Button>(form, "undo").Enabled, "successful change enables undo");
                Get<Button>(form, "undo").PerformClick(); WaitUntil(() => !Get<bool>(form, "busy"));
                Check(current == "China Standard Time", "undo restores previous timezone");
                Get<Button>(form, "undo").PerformClick(); WaitUntil(() => !Get<bool>(form, "busy"));
                Check(current == "Pacific Standard Time", "undo can toggle back after its own successful change");
                current = "Eastern Standard Time"; Invoke(form, "RefreshSystemState");
                Check(!Get<Button>(form, "undo").Enabled, "external timezone change invalidates undo");
                current = "Pacific Standard Time"; Invoke(form, "RefreshSystemState");
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
                if (args.Length > 1)
                {
                    current = TimeZoneService.ReadSystemId(); Invoke(form, "RefreshSystemState"); search.Text = "日本";
                    Invoke(form, "SetStatus", "可搜索国家、城市、缩写或 UTC 偏移；选中后直接切换或加入常用。", false, false);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(args[1], "preview-japan.png")); }
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
                    if (!first) Console.WriteLine("SKIP: single-instance activation probe; an existing user instance is running.");
                    else
                    {
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
            }
            Console.WriteLine("TOTAL UI/LAYOUT: " + count + " passed"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
