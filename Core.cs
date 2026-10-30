using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace TimeZoneSwitcher
{
    public sealed class ZoneEntry
    {
        public TimeZoneInfo Zone { get; private set; }
        public string Id { get { return Zone.Id; } }
        public string Name { get; private set; }
        public ZoneSearchInfo SearchInfo { get; private set; }
        internal string[] SearchTerms { get; private set; }
        public ZoneEntry(TimeZoneInfo zone)
        {
            Zone = zone;
            SearchInfo = SearchIndex.Find(zone.Id);
            switch (zone.Id)
            {
                case "China Standard Time": Name = "中国 · 北京 / 上海"; break;
                case "Pacific Standard Time": Name = "美国 · 洛杉矶"; break;
                case "Eastern Standard Time": Name = "美国 · 纽约"; break;
                case "GMT Standard Time": Name = "英国 · 伦敦"; break;
                case "Tokyo Standard Time":
                    Name = "日本 · 东京";
                    break;
                case "Singapore Standard Time": Name = "新加坡"; break;
                default: Name = String.IsNullOrEmpty(SearchInfo.Display) ? zone.StandardName : SearchInfo.Display; break;
            }
            SearchTerms = ZoneSearch.BuildTerms(this);
        }
        public string OffsetText
        {
            get { return FormatOffset(Zone.GetUtcOffset(DateTimeOffset.UtcNow)); }
        }
        public static string FormatOffset(TimeSpan offset)
        { return "UTC" + (offset < TimeSpan.Zero ? "−" : "+") + FormatHours(offset); }
        public static string FormatHours(TimeSpan value)
        { TimeSpan absolute = value.Duration(); return ((int)absolute.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" + absolute.Minutes.ToString("00", CultureInfo.InvariantCulture); }
        public bool Matches(string query)
        {
            return ZoneSearch.Score(this, query) >= 0;
        }
        public override string ToString() { return Name + "  ·  " + OffsetText; }
    }

    public sealed class ZoneCatalog
    {
        private readonly Dictionary<string, ZoneEntry> byId;
        public IList<ZoneEntry> All { get; private set; }
        public ZoneCatalog()
        {
            All = TimeZoneInfo.GetSystemTimeZones().Select(z => new ZoneEntry(z)).ToList().AsReadOnly();
            byId = All.ToDictionary(z => z.Id, StringComparer.OrdinalIgnoreCase);
        }
        public ZoneEntry Find(string id)
        {
            ZoneEntry entry;
            return id != null && byId.TryGetValue(id, out entry) ? entry : null;
        }
        public List<ZoneEntry> Search(string query)
        {
            return All.Select(zone => new { Zone = zone, Score = ZoneSearch.Score(zone, query) })
                .Where(item => item.Score >= 0).OrderBy(item => item.Score)
                .ThenBy(item => item.Zone.Zone.GetUtcOffset(DateTimeOffset.UtcNow))
                .ThenBy(item => item.Zone.Name, StringComparer.CurrentCulture).Select(item => item.Zone).ToList();
        }
        public List<string> Defaults()
        {
            return new[] { "China Standard Time", "Pacific Standard Time", "Eastern Standard Time" }
                .Where(id => Find(id) != null).ToList();
        }
        public List<string> Normalize(IEnumerable<string> ids)
        {
            return (ids ?? Enumerable.Empty<string>()).Select(Find).Where(z => z != null)
                .Select(z => z.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "favoriteTimeZoneIds", IsRequired = true)]
        public List<string> FavoriteIds { get; set; }
    }

    public sealed class SettingsStore
    {
        private readonly string path;
        private readonly ZoneCatalog catalog;
        public string Warning { get; private set; }
        public SettingsStore(string path, ZoneCatalog catalog) { this.path = path; this.catalog = catalog; }
        public Settings Load()
        {
            Warning = null;
            if (!File.Exists(path)) return new Settings { FavoriteIds = catalog.Defaults() };
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new SerializationException("配置文件过大");
                Settings result;
                using (var stream = File.OpenRead(path))
                    result = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                if (result == null || result.FavoriteIds == null) throw new SerializationException("缺少常用时区列表");
                List<string> normalized = catalog.Normalize(result.FavoriteIds);
                if (normalized.Count != result.FavoriteIds.Count) Warning = "已忽略配置中重复或不再存在的时区。";
                result.FavoriteIds = normalized;
                return result;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException || ex is ArgumentException)) throw;
                Warning = "配置无法读取，已恢复默认列表。";
                try
                {
                    string backup = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                    File.Copy(path, backup, false);
                    Warning += "原文件已备份。";
                }
                catch (Exception backupError)
                { if (!(backupError is IOException || backupError is UnauthorizedAccessException)) throw; }
                return new Settings { FavoriteIds = catalog.Defaults() };
            }
        }
        public void Save(IEnumerable<string> favoriteIds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var settings = new Settings { FavoriteIds = catalog.Normalize(favoriteIds) };
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public sealed class CommandResult
    {
        public int ExitCode;
        public string Error = "";
        public bool Cancelled;
        public bool TimedOut;
    }
    public interface ICommandRunner
    {
        CommandResult Run(string executable, string arguments, bool elevated);
    }
    public sealed class WindowsCommandRunner : ICommandRunner
    {
        public CommandResult Run(string executable, string arguments, bool elevated)
        {
            var start = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = elevated,
                Verb = elevated ? "runas" : "",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !elevated,
                RedirectStandardError = !elevated
            };
            try
            {
                using (var process = Process.Start(start))
                {
                    if (process == null) return new CommandResult { ExitCode = -1, Error = "系统未能启动时区命令。" };
                    Task<string> stdout = elevated ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = elevated ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch (Win32Exception) { } catch (InvalidOperationException) { }
                        return new CommandResult { ExitCode = -1, TimedOut = true, Error = "系统时区命令执行超时，请刷新后重试。" };
                    }
                    // Drain both pipes without blocking the UI thread or losing the last output chunk.
                    Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                    string output = (stderr.IsCompleted ? stderr.Result : "") + " " + (stdout.IsCompleted ? stdout.Result : "");
                    return new CommandResult { ExitCode = process.ExitCode, Error = output.Trim() };
                }
            }
            catch (Win32Exception ex)
            {
                return new CommandResult { ExitCode = -1, Cancelled = ex.NativeErrorCode == 1223,
                    Error = ex.NativeErrorCode == 1223 ? "已取消管理员授权。" : ex.Message };
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                return new CommandResult { ExitCode = -1, Error = ex.Message };
            }
        }
    }

    public sealed class SwitchResult
    {
        public bool Success;
        public bool Cancelled;
        public string ActualId;
        public string Message;
    }
    public sealed class TimeZoneService
    {
        private readonly ZoneCatalog catalog;
        private readonly ICommandRunner runner;
        private readonly Func<string> readCurrentId;
        private readonly Func<bool> isDaylightDisabled;
        public TimeZoneService(ZoneCatalog catalog, ICommandRunner runner, Func<string> readCurrentId, Func<bool> isDaylightDisabled = null)
        {
            this.catalog = catalog; this.runner = runner; this.readCurrentId = readCurrentId;
            this.isDaylightDisabled = isDaylightDisabled ?? ReadDaylightDisabled;
        }
        public static bool ReadDaylightDisabled()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\TimeZoneInformation"))
                return key != null && Convert.ToInt32(key.GetValue("DynamicDaylightTimeDisabled", 0), CultureInfo.InvariantCulture) != 0;
        }
        public static string ReadSystemId()
        {
            TimeZoneInfo.ClearCachedData();
            return TimeZoneInfo.Local.Id;
        }
        public string CurrentId { get { return readCurrentId(); } }
        public SwitchResult Switch(string id, bool elevated)
        {
            ZoneEntry zone = catalog.Find(id);
            if (zone == null) return new SwitchResult { Message = "这个时区不在 Windows 的时区列表中。", ActualId = readCurrentId() };
            if (String.Equals(readCurrentId(), zone.Id, StringComparison.OrdinalIgnoreCase) &&
                (!zone.Zone.SupportsDaylightSavingTime || !isDaylightDisabled()))
                return new SwitchResult { Success = true, ActualId = zone.Id, Message = "当前已经是" + zone.Name + "。" };
            // The canonical ID comes only from Windows' catalog. No cmd.exe or shell interpolation.
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tzutil.exe");
            CommandResult command = runner.Run(executable, "/s \"" + zone.Id + "\"", elevated);
            string actual = readCurrentId();
            if (command.Cancelled) return new SwitchResult { Cancelled = true, ActualId = actual, Message = command.Error };
            if (command.ExitCode == 0 && String.Equals(actual, zone.Id, StringComparison.OrdinalIgnoreCase) &&
                (!zone.Zone.SupportsDaylightSavingTime || !isDaylightDisabled()))
                return new SwitchResult { Success = true, ActualId = actual, Message = "已切换到" + zone.Name + "，并确认系统设置已生效。" };
            string reason = String.IsNullOrWhiteSpace(command.Error) ?
                "系统未确认目标时区及夏令时规则。可能受到权限、组织策略或自动时区设置影响。" : command.Error;
            if (command.ExitCode != 0) reason = "系统命令返回错误代码 " + command.ExitCode + "。" + reason;
            return new SwitchResult { ActualId = actual, Message = "切换未成功确认：" + reason };
        }
    }
}
