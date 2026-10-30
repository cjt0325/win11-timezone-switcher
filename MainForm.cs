using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TimeZoneSwitcher
{
    internal sealed class SearchBox : TextBox
    {
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SendMessage(Handle, 0x1501, new IntPtr(1), "搜索城市、时区名称或 ID");
        }
    }
    internal sealed class RendezvousWindow : NativeWindow, IDisposable
    {
        private readonly Action show;
        public RendezvousWindow(string userId, Action show)
        {
            this.show = show;
            CreateHandle(new CreateParams { Caption = "TimeZoneSwitcher.Rendezvous." + userId,
                Style = unchecked((int)0x80000000), ExStyle = 0x80 });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Program.ShowMessage) show();
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    internal sealed class ZoneList : ListBox
    {
        public ZoneList()
        {
            Dock = DockStyle.Fill;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            BackColor = Color.White;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 60;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = LogicalToDeviceUnits(60);
        }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ItemHeight = LogicalToDeviceUnits(60);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var zone = (ZoneEntry)Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(232, 240, 255) : Color.White))
                e.Graphics.FillRectangle(brush, e.Bounds);
            int inset = LogicalToDeviceUnits(12);
            int top = e.Bounds.Top + LogicalToDeviceUnits(8);
            Color mainColor = selected ? Color.FromArgb(27, 75, 160) : Color.FromArgb(35, 44, 60);
            var first = new Rectangle(e.Bounds.Left + inset, top, e.Bounds.Width - inset * 2, ItemHeight / 2 - LogicalToDeviceUnits(3));
            TextRenderer.DrawText(e.Graphics, zone.Name, Font, first, mainColor,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string detail = zone.OffsetText + "    " + TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone.Zone).ToString("HH:mm");
            first.Y = e.Bounds.Top + ItemHeight / 2;
            TextRenderer.DrawText(e.Graphics, detail, Font, first, Color.FromArgb(111, 123, 143),
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly ZoneCatalog catalog;
        private readonly SettingsStore store;
        private readonly TimeZoneService service;
        private readonly bool headlessTest;
        private List<string> favoriteIds;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly RendezvousWindow rendezvous;
        private readonly Timer timer;
        private readonly ZoneList favorites = new ZoneList();
        private readonly ZoneList available = new ZoneList();
        private readonly TextBox search = new SearchBox();
        private readonly Label currentZone = new Label();
        private readonly Label currentClock = new Label();
        private readonly Label currentDetail = new Label();
        private readonly Label status = new Label();
        private readonly Button apply = MakeButton("切换所选", true);
        private readonly Button remove = MakeButton("移除", false);
        private readonly Button add = MakeButton("添加到常用", false);
        private readonly Button retry = MakeButton("管理员重试", false);
        private bool quitting;
        private bool busy;
        private bool hideTipShown;
        private string actualId;
        private string retryId;
        private int tick;

        public MainForm(ZoneCatalog catalog, SettingsStore store, TimeZoneService service, string userId, bool headlessTest = false)
        {
            SuspendLayout();
            this.catalog = catalog; this.store = store; this.service = service;
            this.headlessTest = headlessTest;
            favoriteIds = store.Load().FavoriteIds;
            Text = "时区切换";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            ForeColor = Color.FromArgb(35, 44, 60);
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(700, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildLayout();
            trayMenu = new ContextMenuStrip { Font = Font };
            trayMenu.Opening += delegate { RefreshSystemState(); BuildTrayMenu(); };
            tray = new NotifyIcon { Icon = Icon, Text = "时区切换", ContextMenuStrip = trayMenu, Visible = !headlessTest };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowPanel(); };
            if (!headlessTest) rendezvous = new RendezvousWindow(userId, ShowPanel);
            favorites.SelectedIndexChanged += delegate { UpdateButtons(); };
            available.SelectedIndexChanged += delegate { UpdateButtons(); };
            search.TextChanged += delegate { FilterAvailable(); };
            apply.Click += async delegate { var zone = favorites.SelectedItem as ZoneEntry; if (zone != null) await SwitchAsync(zone.Id, false); };
            add.Click += delegate { AddFavorite(); };
            remove.Click += delegate { RemoveFavorite(); };
            retry.Click += async delegate { if (retryId != null) await SwitchAsync(retryId, true); };
            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                if (!busy) RefreshSystemState();
                if (++tick % 30 == 0) { favorites.Invalidate(); available.Invalidate(); }
            };
            if (!headlessTest) timer.Start();
            RefreshFavorites(null);
            FilterAvailable();
            RefreshSystemState();
            SetStatus(store.Warning ?? "选择常用时区，点击切换。美国时区会自动遵循夏令时。", store.Warning != null, false);
            Shown += delegate { RefreshSystemState(); };
            FormClosing += OnClosing;
            // Set the design DPI after adding the entire control tree. Setting Font earlier can
            // otherwise consume WinForms' initial autoscale pass with an empty form.
            AutoScaleDimensions = new SizeF(96F, 96F);
            ResumeLayout(true);
        }

        private static Label MakeLabel(string text, float size, Color color)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), Margin = Padding.Empty };
        }
        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button { Text = text, AutoSize = false, Height = 36, Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Color.FromArgb(44, 98, 217) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(50, 66, 91),
                Margin = new Padding(0), UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(44, 98, 217) : Color.FromArgb(211, 220, 234);
            return button;
        }
        private void BuildLayout()
        {
            SuspendLayout();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(24, 16, 24, 12), BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            heading.Controls.Add(MakeLabel("时区切换", 21, ForeColor), 0, 0);
            heading.Controls.Add(MakeLabel("常用时区，一点切换", 9, Color.FromArgb(116, 129, 149)), 0, 1);
            root.Controls.Add(heading, 0, 0);

            var current = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 12), ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 6, 0, 14) };
            current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            current.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            current.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            current.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            current.Controls.Add(MakeLabel("当前系统时区", 9, Color.FromArgb(116, 129, 149)), 0, 0);
            currentZone.Dock = DockStyle.Fill; currentZone.TextAlign = ContentAlignment.MiddleLeft;
            currentZone.Font = new Font(Font.FontFamily, 14F); currentZone.AutoEllipsis = true;
            current.Controls.Add(currentZone, 0, 1);
            currentDetail.Dock = DockStyle.Fill; currentDetail.ForeColor = Color.FromArgb(116, 129, 149);
            currentDetail.TextAlign = ContentAlignment.MiddleLeft; currentDetail.AutoEllipsis = true;
            current.Controls.Add(currentDetail, 0, 2);
            currentClock.Dock = DockStyle.Fill; currentClock.TextAlign = ContentAlignment.MiddleRight;
            currentClock.Font = new Font("Segoe UI", 23F);
            current.Controls.Add(currentClock, 1, 0); current.SetRowSpan(currentClock, 3);
            root.Controls.Add(current, 0, 1);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 9, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            left.Controls.Add(MakeLabel("常用时区", 10F, ForeColor), 0, 0);
            favorites.AccessibleName = "常用时区列表";
            left.Controls.Add(favorites, 0, 1);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            apply.Margin = new Padding(0, 0, 8, 0);
            actions.Controls.Add(apply, 0, 0); actions.Controls.Add(remove, 1, 0);
            left.Controls.Add(actions, 0, 2);
            columns.Controls.Add(left, 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(9, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            right.Controls.Add(MakeLabel("添加其他时区", 10F, ForeColor), 0, 0);
            search.Dock = DockStyle.Top; search.AccessibleName = "搜索城市、时区名称或 ID";
            search.Margin = new Padding(0, 2, 0, 8);
            var tip = new ToolTip(); tip.SetToolTip(search, "输入城市、UTC 偏移或 Windows 时区 ID，例如：东京、UTC+09:00、Tokyo");
            right.Controls.Add(search, 0, 1);
            available.AccessibleName = "全部时区搜索结果";
            right.Controls.Add(available, 0, 2);
            add.Margin = new Padding(0, 10, 0, 0);
            right.Controls.Add(add, 0, 3);
            columns.Controls.Add(right, 1, 0);
            root.Controls.Add(columns, 0, 2);

            var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.Margin = new Padding(0, 0, 8, 0); status.AutoEllipsis = true;
            status.AccessibleName = "切换状态";
            retry.AutoSize = true; retry.Dock = DockStyle.None; retry.Anchor = AnchorStyles.Right;
            retry.Padding = new Padding(8, 0, 8, 0); retry.Visible = false;
            statusPanel.Controls.Add(status, 0, 0); statusPanel.Controls.Add(retry, 1, 0);
            root.Controls.Add(statusPanel, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            footer.Controls.Add(MakeLabel("关闭窗口后，仍可从右下角托盘切换", 8.5F, Color.FromArgb(116, 129, 149)), 0, 0);
            var settingsLink = new LinkLabel { Text = "系统日期和时间", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight, LinkBehavior = LinkBehavior.HoverUnderline,
                LinkColor = Color.FromArgb(44, 98, 217) };
            settingsLink.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); }
                catch (Exception ex) { SetStatus("无法打开系统设置：" + ex.Message, true, false); }
            };
            footer.Controls.Add(settingsLink, 1, 0);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
            ResumeLayout(true);
        }

        private void FilterAvailable()
        {
            available.BeginUpdate(); available.Items.Clear();
            available.Items.AddRange(catalog.All.Where(z => z.Matches(search.Text)).Cast<object>().ToArray());
            available.EndUpdate();
            if (available.Items.Count > 0) available.SelectedIndex = 0;
            UpdateButtons();
        }
        private void RefreshFavorites(string selectedId)
        {
            favorites.BeginUpdate(); favorites.Items.Clear();
            foreach (string id in favoriteIds) { var zone = catalog.Find(id); if (zone != null) favorites.Items.Add(zone); }
            favorites.EndUpdate();
            for (int i = 0; i < favorites.Items.Count; i++)
                if (((ZoneEntry)favorites.Items[i]).Id == selectedId) favorites.SelectedIndex = i;
            if (favorites.SelectedIndex < 0 && favorites.Items.Count > 0) favorites.SelectedIndex = 0;
            if (trayMenu != null) BuildTrayMenu();
            UpdateButtons();
        }
        private void AddFavorite()
        {
            var zone = available.SelectedItem as ZoneEntry;
            if (zone == null || favoriteIds.Contains(zone.Id)) return;
            var next = new List<string>(favoriteIds); next.Add(zone.Id);
            SaveFavorites(next, zone.Id);
        }
        private void RemoveFavorite()
        {
            var zone = favorites.SelectedItem as ZoneEntry;
            if (zone == null) return;
            SaveFavorites(favoriteIds.Where(id => id != zone.Id).ToList(), null);
        }
        private void SaveFavorites(List<string> next, string selectedId)
        {
            try
            {
                store.Save(next); favoriteIds = next; RefreshFavorites(selectedId);
                SetStatus("常用时区列表已保存。", false, false);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                SetStatus("列表未能保存：" + ex.Message, true, false);
            }
        }
        private void RefreshSystemState()
        {
            try
            {
                string id = service.CurrentId;
                var zone = catalog.Find(id);
                if (actualId != id) { actualId = id; if (trayMenu != null && !trayMenu.Visible) BuildTrayMenu(); }
                currentZone.Text = zone == null ? id : zone.Name;
                currentDetail.Text = DateTime.Now.ToString("yyyy年M月d日 dddd") + "  ·  " + ZoneEntry.FormatOffset(DateTimeOffset.Now.Offset);
                currentClock.Text = DateTime.Now.ToString("HH:mm:ss");
                if (tray != null)
                {
                    string text = "时区切换 · " + (zone == null ? id : zone.Name);
                    tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                }
            }
            catch (Exception ex) { SetStatus("无法读取系统时区：" + ex.Message, true, false); }
        }
        private void BuildTrayMenu()
        {
            // Dispose old items: rebuilding a menu must not leak native menu resources.
            foreach (ToolStripItem old in trayMenu.Items.Cast<ToolStripItem>().ToArray()) old.Dispose();
            trayMenu.Items.Clear();
            trayMenu.Items.Add(new ToolStripMenuItem("切换到…") { Enabled = false });
            if (favoriteIds.Count == 0) trayMenu.Items.Add(new ToolStripMenuItem("请先添加常用时区") { Enabled = false });
            foreach (string id in favoriteIds)
            {
                var zone = catalog.Find(id); if (zone == null) continue;
                var item = new ToolStripMenuItem(zone.ToString())
                { Checked = String.Equals(actualId, zone.Id, StringComparison.OrdinalIgnoreCase), Enabled = !busy };
                string requestedId = zone.Id;
                item.Click += async delegate { await SwitchAsync(requestedId, false); };
                trayMenu.Items.Add(item);
            }
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("管理常用时区", null, delegate { ShowPanel(); });
            var exit = new ToolStripMenuItem("退出");
            exit.Enabled = !busy;
            exit.Click += delegate { quitting = true; Close(); };
            trayMenu.Items.Add(exit);
        }
        private void UpdateButtons()
        {
            apply.Enabled = !busy && favorites.SelectedItem != null;
            remove.Enabled = !busy && favorites.SelectedItem != null;
            var zone = available.SelectedItem as ZoneEntry;
            add.Enabled = !busy && zone != null && !favoriteIds.Contains(zone.Id);
            search.Enabled = !busy;
            favorites.Enabled = !busy; available.Enabled = !busy; retry.Enabled = !busy;
        }
        private void SetStatus(string message, bool error, bool showRetry)
        {
            status.Text = message; status.ForeColor = error ? Color.FromArgb(164, 59, 38) : Color.FromArgb(58, 95, 79);
            status.AccessibleDescription = message;
            retry.Visible = showRetry;
        }
        private async Task SwitchAsync(string id, bool elevated)
        {
            if (busy) return;
            busy = true; retryId = id; UpdateButtons();
            if (trayMenu != null) BuildTrayMenu();
            var zone = catalog.Find(id);
            SetStatus(elevated ? "等待管理员授权并切换时区…" : "正在切换到" + (zone == null ? id : zone.Name) + "…", false, false);
            try
            {
                SwitchResult result = await Task.Run(() => service.Switch(id, elevated));
                RefreshSystemState();
                SetStatus(result.Message, !result.Success && !result.Cancelled, !result.Success && !result.Cancelled && !elevated);
                if (!result.Success && !Visible) ShowPanel();
                if (result.Success && tray.Visible)
                {
                    tray.BalloonTipTitle = "系统时区已确认";
                    tray.BalloonTipText = result.Message;
                    tray.BalloonTipIcon = ToolTipIcon.Info;
                    tray.ShowBalloonTip(2500);
                }
            }
            catch (Exception ex) { ShowPanel(); SetStatus("切换未成功确认：" + ex.Message, true, !elevated); }
            finally { busy = false; UpdateButtons(); BuildTrayMenu(); }
        }
        private void ShowPanel()
        {
            if (IsDisposed || headlessTest) return;
            Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); BringToFront(); RefreshSystemState();
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            bool systemClosing = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
            if (!quitting && !systemClosing)
            {
                e.Cancel = true; Hide();
                if (!hideTipShown)
                {
                    hideTipShown = true; tray.BalloonTipTitle = "时区切换仍在运行";
                    tray.BalloonTipText = "在右下角托盘找到时钟图标，右键切换时区或退出。";
                    tray.BalloonTipIcon = ToolTipIcon.Info; tray.ShowBalloonTip(2500);
                }
                return;
            }
            timer.Stop(); tray.Visible = false;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                if (tray != null) tray.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
                if (rendezvous != null) rendezvous.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
