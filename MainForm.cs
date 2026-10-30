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
        { base.OnHandleCreated(e); Native.SendMessage(Handle, 0x1501, new IntPtr(1), "国家 / 城市 / JST / UTC+9"); }
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
        { if (message.Msg == Program.ShowMessage) show(); base.WndProc(ref message); }
        public void Dispose() { DestroyHandle(); }
    }
    internal sealed class ZoneList : ListBox
    {
        public ZoneList()
        {
            Dock = DockStyle.Fill; IntegralHeight = false; BorderStyle = BorderStyle.None;
            BackColor = Color.White; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 60;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ItemHeight = LogicalToDeviceUnits(60); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ItemHeight = LogicalToDeviceUnits(60); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var zone = (ZoneEntry)Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(232, 240, 255) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
            int inset = LogicalToDeviceUnits(12);
            var first = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top + LogicalToDeviceUnits(8), e.Bounds.Width - inset * 2, ItemHeight / 2 - 3);
            TextRenderer.DrawText(e.Graphics, zone.Name, Font, first, selected ? Color.FromArgb(27, 75, 160) : Color.FromArgb(35, 44, 60),
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            first.Y = e.Bounds.Top + ItemHeight / 2;
            string detail = zone.OffsetText + "  ·  " + TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone.Zone).ToString("HH:mm");
            TextRenderer.DrawText(e.Graphics, detail, Font, first, Color.FromArgb(111, 123, 143), TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
        private readonly ToolTip tips = new ToolTip();
        private readonly ZoneList favorites = new ZoneList();
        private readonly ZoneList available = new ZoneList();
        private readonly TextBox search = new SearchBox();
        private readonly TextBox preview = new TextBox();
        private readonly Label currentZone = new Label();
        private readonly Label currentClock = new Label();
        private readonly Label currentDetail = new Label();
        private readonly Label status = new Label();
        private readonly Label resultsCount = new Label();
        private readonly Label favoriteCount = new Label();
        private readonly Label emptyResults = new Label();
        private readonly Label emptyFavorites = new Label();
        private readonly Button apply = MakeButton("切换所选", true);
        private readonly Button switchAvailable = MakeButton("立即切换", true);
        private readonly Button remove = MakeButton("移除", false);
        private readonly Button add = MakeButton("加入常用", false);
        private readonly Button moveUp = MakeButton("↑", false);
        private readonly Button moveDown = MakeButton("↓", false);
        private readonly Button clearSearch = MakeButton("清空", false);
        private readonly Button copyInfo = MakeButton("复制时区信息", false);
        private readonly Button undo = MakeButton("撤销上次切换", false);
        private readonly Button retry = MakeButton("管理员重试", false);
        private bool quitting, busy, hideTipShown;
        private string actualId, retryId, previewId, undoId, lastAppliedId;
        private int tick;

        public MainForm(ZoneCatalog catalog, SettingsStore store, TimeZoneService service, string userId, bool headlessTest = false)
        {
            SuspendLayout(); this.catalog = catalog; this.store = store; this.service = service; this.headlessTest = headlessTest;
            favoriteIds = store.Load().FavoriteIds;
            Text = "时区切换"; Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251); ForeColor = Color.FromArgb(35, 44, 60);
            ClientSize = new Size(860, 744); MinimumSize = new Size(760, 700); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildLayout();
            trayMenu = new ContextMenuStrip { Font = Font };
            trayMenu.Opening += delegate { RefreshSystemState(); BuildTrayMenu(); };
            tray = new NotifyIcon { Icon = Icon, Text = "时区切换", ContextMenuStrip = trayMenu, Visible = !headlessTest };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowPanel(); };
            if (!headlessTest) rendezvous = new RendezvousWindow(userId, ShowPanel);
            favorites.SelectedIndexChanged += delegate { PreviewZone(favorites.SelectedItem as ZoneEntry); UpdateButtons(); };
            available.SelectedIndexChanged += delegate { PreviewZone(available.SelectedItem as ZoneEntry); UpdateButtons(); };
            search.TextChanged += delegate { FilterAvailable(); };
            apply.Click += async delegate { var zone = favorites.SelectedItem as ZoneEntry; if (zone != null) await SwitchAsync(zone.Id, false); };
            switchAvailable.Click += async delegate { var zone = available.SelectedItem as ZoneEntry; if (zone != null) await SwitchAsync(zone.Id, false); };
            add.Click += delegate { AddFavorite(); }; remove.Click += delegate { RemoveFavorite(); };
            moveUp.Click += delegate { MoveFavorite(-1); }; moveDown.Click += delegate { MoveFavorite(1); };
            clearSearch.Click += delegate { search.Clear(); search.Focus(); };
            copyInfo.Click += delegate { CopyPreview(); };
            undo.Click += async delegate { if (undoId != null) await SwitchAsync(undoId, false); };
            retry.Click += async delegate { if (retryId != null) await SwitchAsync(retryId, true); };
            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                if (!busy) RefreshSystemState();
                if (++tick % 30 == 0) { favorites.Invalidate(); available.Invalidate(); PreviewZone(catalog.Find(previewId)); }
            };
            if (!headlessTest) timer.Start();
            RefreshSystemState(); RefreshFavorites(actualId); FilterAvailable();
            SetStatus(store.Warning ?? SearchIndex.Warning ?? "输入国家、城市、缩写或 UTC 偏移。多时区国家会列出多个结果。", store.Warning != null || SearchIndex.Warning != null, false);
            Shown += delegate { RefreshSystemState(); if (!headlessTest) search.Focus(); };
            FormClosing += OnClosing;
            Resize += delegate { if (!headlessTest && WindowState == FormWindowState.Minimized) Hide(); };
            KeyDown += OnShortcut;
            AutoScaleDimensions = new SizeF(96F, 96F); ResumeLayout(true);
            if (!headlessTest)
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            }
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button { Text = text, Height = 36, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Color.FromArgb(44, 98, 217) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(50, 66, 91),
                Margin = Padding.Empty, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(44, 98, 217) : Color.FromArgb(211, 220, 234);
            return button;
        }
        private static Label LabelText(string text, float size, Color color)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), Margin = Padding.Empty, AutoEllipsis = true };
        }
        private static TableLayoutPanel Grid(int columns, int rows)
        { return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty }; }
        private void BuildLayout()
        {
            var root = Grid(1, 7); root.Padding = new Padding(24, 16, 24, 12);
            foreach (int height in new[] { 64, 116 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 30, 116, 70, 32 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var heading = Grid(1, 2); heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            heading.Controls.Add(LabelText("时区切换", 21, ForeColor), 0, 0);
            heading.Controls.Add(LabelText("搜索全球时区，预览当地时间，一点切换", 9, Color.SlateGray), 0, 1); root.Controls.Add(heading, 0, 0);
            var current = Grid(2, 3); current.BackColor = Color.White; current.Padding = new Padding(18, 8, 18, 10); current.Margin = new Padding(0, 6, 0, 12);
            current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            current.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); current.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); current.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            current.Controls.Add(LabelText("当前系统时区", 9, Color.SlateGray), 0, 0);
            currentZone.Dock = DockStyle.Fill; currentZone.TextAlign = ContentAlignment.MiddleLeft; currentZone.Font = new Font(Font.FontFamily, 14F); currentZone.AutoEllipsis = true;
            current.Controls.Add(currentZone, 0, 1);
            currentDetail.Dock = DockStyle.Fill; currentDetail.ForeColor = Color.SlateGray; currentDetail.TextAlign = ContentAlignment.MiddleLeft; currentDetail.AutoEllipsis = true;
            current.Controls.Add(currentDetail, 0, 2);
            currentClock.Dock = DockStyle.Fill; currentClock.TextAlign = ContentAlignment.MiddleRight; currentClock.Font = new Font("Segoe UI", 23F);
            current.Controls.Add(currentClock, 1, 0); current.SetRowSpan(currentClock, 3); root.Controls.Add(current, 0, 1);

            var columns = Grid(2, 1); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            var left = Grid(1, 3); left.Margin = new Padding(0, 0, 9, 0);
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            favoriteCount.Dock = DockStyle.Fill; favoriteCount.TextAlign = ContentAlignment.MiddleLeft; left.Controls.Add(favoriteCount, 0, 0);
            favorites.AccessibleName = "常用时区列表"; left.Controls.Add(ListWithEmpty(favorites, emptyFavorites, "还没有常用时区\r\n在右侧搜索后点击“加入常用”"), 0, 1);
            var favoriteActions = Grid(4, 1); favoriteActions.Padding = new Padding(0, 10, 0, 0);
            foreach (int width in new[] { 48, 24, 14, 14 }) favoriteActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            var favoriteButtons = new[] { apply, remove, moveUp, moveDown };
            for (int i = 0; i < favoriteButtons.Length; i++) { if (i < 3) favoriteButtons[i].Margin = new Padding(0, 0, 6, 0); favoriteActions.Controls.Add(favoriteButtons[i], i, 0); }
            tips.SetToolTip(moveUp, "上移常用时区，托盘顺序同步更新"); tips.SetToolTip(moveDown, "下移常用时区");
            left.Controls.Add(favoriteActions, 0, 2); columns.Controls.Add(left, 0, 0);
            var right = Grid(1, 4); right.Margin = new Padding(9, 0, 0, 0);
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            resultsCount.Dock = DockStyle.Fill; resultsCount.TextAlign = ContentAlignment.MiddleLeft; right.Controls.Add(resultsCount, 0, 0);
            var searchRow = Grid(2, 1); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            search.AccessibleName = "搜索国家、城市、缩写或 UTC 偏移"; search.Dock = DockStyle.Top; search.Margin = new Padding(0, 2, 8, 6);
            clearSearch.Margin = new Padding(0, 0, 0, 8); searchRow.Controls.Add(search, 0, 0); searchRow.Controls.Add(clearSearch, 1, 0); right.Controls.Add(searchRow, 0, 1);
            tips.SetToolTip(search, "例如：日本时间、韩国、德国、美国、美西、Japan、JST、Asia/Tokyo、UTC+9、+0530；偏移按今天计算");
            available.AccessibleName = "全部时区搜索结果"; right.Controls.Add(ListWithEmpty(available, emptyResults, "没有匹配的时区\r\n试试国家、城市或 UTC+9\r\n例如：日本、德国、纽约"), 0, 2);
            var resultActions = Grid(2, 1); resultActions.Padding = new Padding(0, 10, 0, 0);
            resultActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); resultActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            switchAvailable.Margin = new Padding(0, 0, 8, 0); resultActions.Controls.Add(switchAvailable, 0, 0); resultActions.Controls.Add(add, 1, 0); right.Controls.Add(resultActions, 0, 3);
            columns.Controls.Add(right, 1, 0); root.Controls.Add(columns, 0, 2);
            root.Controls.Add(LabelText("选中时区 · 当地时间与系统标识", 9, Color.SlateGray), 0, 3);
            var details = Grid(2, 1); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
            preview.Dock = DockStyle.Fill; preview.Multiline = true; preview.ReadOnly = true; preview.BorderStyle = BorderStyle.None;
            preview.BackColor = Color.White; preview.ScrollBars = ScrollBars.Vertical; preview.AccessibleName = "选中时区详情"; preview.Margin = new Padding(0, 0, 12, 0);
            details.Controls.Add(preview, 0, 0);
            var detailButtons = Grid(1, 2); detailButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); detailButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            copyInfo.Margin = new Padding(0, 6, 0, 8); undo.Margin = new Padding(0, 2, 0, 6);
            detailButtons.Controls.Add(copyInfo, 0, 0); detailButtons.Controls.Add(undo, 0, 1); details.Controls.Add(detailButtons, 1, 0); root.Controls.Add(details, 0, 4);
            var statusPanel = Grid(2, 1); statusPanel.Padding = new Padding(0, 6, 0, 0);
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.Margin = new Padding(0, 0, 8, 0);
            retry.AutoSize = true; retry.Dock = DockStyle.None; retry.Anchor = AnchorStyles.Right; retry.Padding = new Padding(8, 0, 8, 0); retry.Visible = false;
            statusPanel.Controls.Add(status, 0, 0); statusPanel.Controls.Add(retry, 1, 0); root.Controls.Add(statusPanel, 0, 5);
            var footer = Grid(2, 1); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            footer.Controls.Add(LabelText("v1.1.0  ·  Ctrl+F 搜索  ·  关闭窗口收起到托盘", 8.5F, Color.SlateGray), 0, 0);
            var settings = new LinkLabel { Text = "系统日期和时间", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, LinkBehavior = LinkBehavior.HoverUnderline, LinkColor = Color.FromArgb(44, 98, 217) };
            settings.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); } catch (Exception ex) { SetStatus("无法打开系统设置：" + ex.Message, true, false); } };
            footer.Controls.Add(settings, 1, 0); root.Controls.Add(footer, 0, 6); Controls.Add(root);
        }
        private Panel ListWithEmpty(ZoneList list, Label empty, string message)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = Padding.Empty };
            empty.Text = message; empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = Color.SlateGray;
            empty.Visible = false; panel.Controls.Add(list); panel.Controls.Add(empty); return panel;
        }
        private void FilterAvailable()
        {
            string oldId = available.SelectedItem is ZoneEntry ? ((ZoneEntry)available.SelectedItem).Id : actualId;
            var results = catalog.Search(search.Text);
            available.BeginUpdate(); available.Items.Clear(); available.Items.AddRange(results.Cast<object>().ToArray()); available.EndUpdate();
            int preferred = results.FindIndex(zone => zone.Id == oldId);
            if (results.Count > 0) available.SelectedIndex = preferred >= 0 ? preferred : 0;
            else PreviewZone(null);
            emptyResults.Visible = results.Count == 0; available.Visible = results.Count > 0;
            resultsCount.Text = String.IsNullOrWhiteSpace(search.Text) ? "全部时区 · " + results.Count : "搜索结果 · " + results.Count + " 个时区";
            UpdateButtons();
        }
        private void RefreshFavorites(string selectedId)
        {
            favorites.BeginUpdate(); favorites.Items.Clear();
            foreach (string id in favoriteIds) { var zone = catalog.Find(id); if (zone != null) favorites.Items.Add(zone); }
            favorites.EndUpdate();
            for (int i = 0; i < favorites.Items.Count; i++) if (((ZoneEntry)favorites.Items[i]).Id == selectedId) favorites.SelectedIndex = i;
            if (favorites.SelectedIndex < 0 && favorites.Items.Count > 0) favorites.SelectedIndex = 0;
            emptyFavorites.Visible = favorites.Items.Count == 0; favorites.Visible = favorites.Items.Count > 0;
            favoriteCount.Text = "常用时区 · " + favoriteIds.Count;
            if (trayMenu != null) BuildTrayMenu(); UpdateButtons();
        }
        private void AddFavorite()
        {
            var zone = available.SelectedItem as ZoneEntry; if (zone == null || favoriteIds.Contains(zone.Id)) return;
            var next = new List<string>(favoriteIds); next.Add(zone.Id); SaveFavorites(next, zone.Id);
        }
        private void RemoveFavorite()
        {
            var zone = favorites.SelectedItem as ZoneEntry; if (zone == null) return;
            int index = favorites.SelectedIndex;
            var next = favoriteIds.Where(id => id != zone.Id).ToList();
            SaveFavorites(next, next.Count > 0 ? next[Math.Min(index, next.Count - 1)] : null);
        }
        private void MoveFavorite(int direction)
        {
            int source = favorites.SelectedIndex, target = source + direction;
            if (source < 0 || target < 0 || target >= favoriteIds.Count || busy) return;
            var next = new List<string>(favoriteIds); string id = next[source]; next.RemoveAt(source); next.Insert(target, id); SaveFavorites(next, id);
        }
        private void SaveFavorites(List<string> next, string selectedId)
        {
            try { store.Save(next); favoriteIds = next; RefreshFavorites(selectedId); SetStatus("常用列表已保存，托盘菜单已同步。", false, false); }
            catch (Exception ex)
            { if (!(ex is IOException || ex is UnauthorizedAccessException)) throw; SetStatus("列表未能保存：" + ex.Message, true, false); }
        }
        private void PreviewZone(ZoneEntry zone)
        {
            previewId = zone == null ? null : zone.Id;
            if (zone == null) { preview.Text = "选择一个时区，查看当地时间、时差与标识。"; UpdateButtons(); return; }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset local = TimeZoneInfo.ConvertTime(now, zone.Zone);
            TimeSpan difference = zone.Zone.GetUtcOffset(now) - DateTimeOffset.Now.Offset;
            string delta = difference == TimeSpan.Zero ? "与本地同一时间" : "比本地" + (difference > TimeSpan.Zero ? "快 " : "慢 ") + ZoneEntry.FormatHours(difference);
            string dst = !zone.Zone.SupportsDaylightSavingTime ? "无夏令时" : zone.Zone.IsDaylightSavingTime(now) ? "当前采用夏令时" : "当前为标准时间（支持夏令时）";
            string countries = String.Join("、", (zone.SearchInfo.Countries ?? new string[0]).Where(s => s.Any(ch => ch > 127)).Take(5));
            string iana = String.Join("、", (zone.SearchInfo.Iana ?? new string[0]).Take(3));
            preview.Text = zone.Name + "  ·  " + zone.OffsetText + "\r\n" + local.ToString("yyyy-MM-dd HH:mm") + "  ·  " + delta + "  ·  " + dst +
                "\r\n地区：" + (countries.Length > 0 ? countries : zone.Zone.DisplayName) + "\r\nWindows：" + zone.Id + "\r\nIANA：" + (iana.Length > 0 ? iana : "使用 Windows 系统标识");
            UpdateButtons();
        }
        private void CopyPreview()
        {
            if (previewId == null) return;
            try { Clipboard.SetText(preview.Text); SetStatus("选中时区的信息已复制。", false, false); }
            catch (System.Runtime.InteropServices.ExternalException) { SetStatus("剪贴板正忙，请稍后重试。", true, false); }
        }
        private void RefreshSystemState()
        {
            try
            {
                string id = service.CurrentId; var zone = catalog.Find(id);
                if (actualId != id)
                {
                    bool external = actualId != null && !busy;
                    actualId = id;
                    if (external) { undoId = null; lastAppliedId = null; SetStatus("系统时区已发生变化，已刷新为" + (zone == null ? id : zone.Name) + "。", false, false); }
                    if (trayMenu != null && !trayMenu.Visible) BuildTrayMenu(); UpdateButtons();
                }
                currentZone.Text = zone == null ? id : zone.Name;
                currentDetail.Text = DateTime.Now.ToString("yyyy年M月d日 dddd") + "  ·  " + ZoneEntry.FormatOffset(DateTimeOffset.Now.Offset);
                currentClock.Text = DateTime.Now.ToString("HH:mm:ss");
                if (tray != null) { string text = "时区切换 · " + (zone == null ? id : zone.Name); tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; }
            }
            catch (Exception ex) { SetStatus("无法读取系统时区：" + ex.Message, true, false); }
        }
        private void BuildTrayMenu()
        {
            foreach (ToolStripItem old in trayMenu.Items.Cast<ToolStripItem>().ToArray()) old.Dispose(); trayMenu.Items.Clear();
            trayMenu.Items.Add(new ToolStripMenuItem("切换到…") { Enabled = false });
            if (favoriteIds.Count == 0) trayMenu.Items.Add(new ToolStripMenuItem("请先添加常用时区") { Enabled = false });
            foreach (string id in favoriteIds)
            {
                var zone = catalog.Find(id); if (zone == null) continue;
                var item = new ToolStripMenuItem(zone.ToString()) { Checked = String.Equals(actualId, zone.Id, StringComparison.OrdinalIgnoreCase), Enabled = !busy };
                string requested = zone.Id; item.Click += async delegate { await SwitchAsync(requested, false); }; trayMenu.Items.Add(item);
            }
            trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("管理常用时区", null, delegate { ShowPanel(); });
            var undoItem = new ToolStripMenuItem("撤销上次切换") { Enabled = CanUndo() }; undoItem.Click += async delegate { if (undoId != null) await SwitchAsync(undoId, false); }; trayMenu.Items.Add(undoItem);
            var exit = new ToolStripMenuItem("退出") { Enabled = !busy }; exit.Click += delegate { quitting = true; Close(); }; trayMenu.Items.Add(exit);
        }
        private bool CanUndo() { return !busy && undoId != null && actualId == lastAppliedId && undoId != actualId; }
        private void UpdateButtons()
        {
            apply.Enabled = !busy && favorites.SelectedItem != null; remove.Enabled = apply.Enabled;
            var zone = available.SelectedItem as ZoneEntry;
            switchAvailable.Enabled = !busy && zone != null; add.Enabled = !busy && zone != null && !favoriteIds.Contains(zone.Id);
            add.Text = zone != null && favoriteIds.Contains(zone.Id) ? "已在常用" : "加入常用";
            moveUp.Enabled = !busy && favorites.SelectedIndex > 0; moveDown.Enabled = !busy && favorites.SelectedIndex >= 0 && favorites.SelectedIndex < favorites.Items.Count - 1;
            clearSearch.Enabled = !busy && search.TextLength > 0; copyInfo.Enabled = previewId != null; undo.Enabled = CanUndo();
            search.Enabled = !busy; favorites.Enabled = !busy; available.Enabled = !busy; retry.Enabled = !busy;
        }
        private void SetStatus(string message, bool error, bool showRetry)
        {
            status.Text = message; status.AccessibleName = message; status.AccessibleDescription = message;
            status.ForeColor = error ? Color.FromArgb(164, 59, 38) : Color.FromArgb(58, 95, 79); retry.Visible = showRetry; tips.SetToolTip(status, message);
        }
        private async Task SwitchAsync(string id, bool elevated)
        {
            if (busy) return; busy = true; retryId = id; UpdateButtons(); if (trayMenu != null) BuildTrayMenu();
            var zone = catalog.Find(id); string before = null;
            SetStatus(elevated ? "等待管理员授权并切换时区…" : "正在切换到" + (zone == null ? id : zone.Name) + "…", false, false);
            try
            {
                before = service.CurrentId;
                SwitchResult result = await Task.Run(() => service.Switch(id, elevated));
                if (result.Success && before != result.ActualId) { undoId = before; lastAppliedId = result.ActualId; }
                RefreshSystemState(); PreviewZone(catalog.Find(id));
                SetStatus(result.Message, !result.Success && !result.Cancelled, !result.Success && !result.Cancelled && !elevated);
                if (!result.Success && !Visible) ShowPanel();
                if (result.Success && tray.Visible) { tray.BalloonTipTitle = "系统时区已确认"; tray.BalloonTipText = result.Message; tray.BalloonTipIcon = ToolTipIcon.Info; tray.ShowBalloonTip(2500); }
            }
            catch (Exception ex) { ShowPanel(); SetStatus("切换未成功确认：" + ex.Message, true, !elevated); }
            finally { busy = false; UpdateButtons(); BuildTrayMenu(); }
        }
        private void ShowPanel()
        {
            if (IsDisposed || headlessTest) return; Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); BringToFront(); RefreshSystemState(); search.Focus();
        }
        private void OnShortcut(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { if (search.TextLength > 0) search.Clear(); else Hide(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { RefreshSystemState(); FilterAvailable(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Q && !busy) { quitting = true; Close(); e.SuppressKeyPress = true; }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            bool system = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
            if (!quitting && !system)
            {
                e.Cancel = true; Hide();
                if (!hideTipShown && !headlessTest) { hideTipShown = true; tray.BalloonTipTitle = "时区切换仍在运行"; tray.BalloonTipText = "在右下角找到蓝色时钟图标，右键切换或退出。"; tray.ShowBalloonTip(2500); }
                return;
            }
            timer.Stop(); tray.Visible = false;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (timer != null) timer.Dispose(); if (tray != null) tray.Dispose(); if (trayMenu != null) trayMenu.Dispose(); if (rendezvous != null) rendezvous.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
