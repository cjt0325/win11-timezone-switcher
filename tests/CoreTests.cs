using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TimeZoneSwitcher;

internal sealed class FakeRunner : ICommandRunner
{
    public int Calls;
    public string Arguments;
    public bool Elevated;
    public Action OnRun;
    public CommandResult Result = new CommandResult();
    public CommandResult Run(string executable, string arguments, bool elevated)
    {
        Calls++; Arguments = arguments; Elevated = elevated;
        if (!executable.EndsWith("\\System32\\tzutil.exe", StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected executable: " + executable);
        if (OnRun != null) OnRun();
        return Result;
    }
}
internal static class CoreTests
{
    private static int count;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
        count++; Console.WriteLine("PASS: " + description);
    }
    private static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "TimeZoneSwitcher-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var catalog = new ZoneCatalog();
            var store = new SettingsStore(Path.Combine(temp, "settings.json"), catalog);
            Check(store.Load().FavoriteIds.SequenceEqual(catalog.Defaults()), "default favorites");
            store.Save(new[] { "Tokyo Standard Time", "China Standard Time", "Tokyo Standard Time", "bad" });
            Check(store.Load().FavoriteIds.SequenceEqual(new[] { "Tokyo Standard Time", "China Standard Time" }), "atomic save, reload, validation and deduplication");
            store.Save(new string[0]);
            Check(store.Load().FavoriteIds.Count == 0, "intentionally empty favorites survive restart");
            File.WriteAllText(Path.Combine(temp, "settings.json"), "{broken");
            Check(store.Load().FavoriteIds.SequenceEqual(catalog.Defaults()) && store.Warning != null, "damaged configuration recovery");
            Check(Directory.GetFiles(temp, "settings.json.damaged-*").Length == 1, "damaged configuration backup");
            store.Save(catalog.Defaults());
            Check(store.Load().FavoriteIds.Count == 3, "recovered config can be replaced");
            Check(store.Warning == null, "configuration recovery warning clears after a healthy load");
            Check(SearchIndex.Count >= 139 && SearchIndex.Warning == null, "offline CLDR index is embedded and readable");
            Check(catalog.All.All(z => z.SearchInfo.Aliases.Length > 0), "every currently installed Windows zone has search metadata");
            Check(catalog.All.All(z => !z.Name.Contains("↑↑↑") && !String.Join(" ", z.SearchInfo.Aliases).Contains("↑↑↑")), "CLDR inheritance markers are excluded from display and search");
            foreach (var countryCase in new Dictionary<string, string>
            {
                { "韩国时间", "Korea Standard Time" }, { "德国时间", "W. Europe Standard Time" },
                { "法国", "Romance Standard Time" }, { "英国时间", "GMT Standard Time" },
                { "印度时间", "India Standard Time" }, { "澳大利亚", "AUS Eastern Standard Time" },
                { "新西兰", "New Zealand Standard Time" }, { "巴西", "E. South America Standard Time" },
                { "俄罗斯", "Russian Standard Time" }, { "泰国时间", "SE Asia Standard Time" },
                { "United States", "Eastern Standard Time" }, { "Canada", "Pacific Standard Time" },
                { "Berlin", "W. Europe Standard Time" }, { "首尔", "Korea Standard Time" },
                { "Asia/Kolkata", "India Standard Time" }, { "美西", "Pacific Standard Time" }
            }) Check(catalog.Search(countryCase.Key).Any(z => z.Id == countryCase.Value), "global country/city search: " + countryCase.Key);
            Check(catalog.Search("美国").Count > 1, "multi-zone country returns multiple valid zones");
            Check(catalog.Search("Japan Standard Time").Any(z => z.Id == "Tokyo Standard Time"), "multi-word English query");
            Check(catalog.Search("日本 UTC+9").Single().Id == "Tokyo Standard Time", "country and offset can be combined");
            Check(ZoneEntry.FormatHours(TimeSpan.FromHours(26)) == "26:00", "date-line timezone difference does not wrap at 24 hours");
            Check(ZoneEntry.FormatHours(TimeSpan.FromMinutes(-330)) == "05:30", "negative half-hour difference formats correctly");
            TimeSpan spacedOffset;
            Check(ZoneSearch.TryOffset("UTC + 09:00", out spacedOffset) && spacedOffset == TimeSpan.FromHours(9), "offset search tolerates whitespace");
            foreach (var offsetQuery in new Dictionary<string, int> { { "UTC+9", 540 }, { "GMT+05:30", 330 }, { "+0530", 330 }, { "UTC−07:00", -420 }, { "UTC+1", 60 } })
            {
                var results = catalog.Search(offsetQuery.Key);
                Check(results.Count > 0 && results.All(z => z.Zone.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes == offsetQuery.Value), "exact current offset search: " + offsetQuery.Key);
            }
            Check(catalog.Search("\t ").Count == catalog.All.Count, "empty search returns complete catalog");
            Check(catalog.Search("no-such-city-xyz123").Count == 0 && catalog.Search("UTC+99").Count == 0, "unknown text and invalid offsets return no results");
            foreach (var systemZone in catalog.All) Check(systemZone.Matches(systemZone.Id), "native Windows ID stays searchable: " + systemZone.Id);
            Check(catalog.Find("Tokyo Standard Time").Matches("东京") && catalog.Find("Tokyo Standard Time").Matches("Tokyo"), "Chinese and English search");
            foreach (string japanQuery in new[] { "日本", "日本时间", "日本标准时间", "日本时区", "Japan", "JST", "Asia/Tokyo", "日本時間" })
            {
                var japanResults = catalog.All.Where(zone => zone.Matches(japanQuery)).ToList();
                Check(japanResults.Count == 1 && japanResults[0].Id == "Tokyo Standard Time", "Japanese timezone search: " + japanQuery);
            }
            Check(catalog.Find("Pacific Standard Time").Matches(catalog.Find("Pacific Standard Time").OffsetText.Replace('−', '-')), "ASCII and Unicode minus signs both searchable");
            Check(catalog.Find("Pacific Standard Time").Zone.GetUtcOffset(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)).TotalHours == -8 &&
                catalog.Find("Pacific Standard Time").Zone.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)).TotalHours == -7, "Windows DST rules retained");
            string current = "China Standard Time";
            var runner = new FakeRunner();
            var service = new TimeZoneService(catalog, runner, () => current, () => false);
            Check(service.Switch(current, false).Success && runner.Calls == 0, "already current does not execute");
            Check(!service.Switch("bad\" & bad", false).Success && runner.Calls == 0, "untrusted ID rejected before execution");
            runner.OnRun = () => { current = "Pacific Standard Time"; };
            Check(service.Switch("Pacific Standard Time", false).Success && runner.Arguments == "/s \"Pacific Standard Time\"" && !runner.Elevated, "space-containing ID quoted and read back");
            current = "China Standard Time"; runner.OnRun = null;
            Check(!service.Switch("Pacific Standard Time", false).Success, "exit zero without actual timezone change is not success");
            runner.Result = new CommandResult { ExitCode = 1, Error = "Insufficient privileges" };
            Check(!service.Switch("Eastern Standard Time", false).Success && service.Switch("Eastern Standard Time", false).Message.Contains("Insufficient privileges"), "permission error visible and not success");
            runner.Result = new CommandResult { ExitCode = -1, Cancelled = true, Error = "cancelled" };
            var cancelled = service.Switch("Eastern Standard Time", true);
            Check(cancelled.Cancelled && !cancelled.Success && runner.Elevated && current == "China Standard Time", "cancelled elevation is not success");
            runner.Result = new CommandResult { ExitCode = -1, TimedOut = true, Error = "timeout" };
            Check(!service.Switch("Eastern Standard Time", false).Success, "timeout is not success");
            runner.Result = new CommandResult { ExitCode = 0 };
            runner.OnRun = () => { current = "Eastern Standard Time"; };
            Check(service.Switch("Eastern Standard Time", true).Success, "elevated successful switch still requires readback");
            bool daylightDisabled = true;
            var dstService = new TimeZoneService(catalog, runner, () => current, () => daylightDisabled);
            runner.OnRun = () => { daylightDisabled = false; };
            int previousCalls = runner.Calls;
            Check(dstService.Switch("Eastern Standard Time", false).Success && runner.Calls == previousCalls + 1, "selecting same DST zone repairs disabled DST setting");
            daylightDisabled = true; runner.OnRun = null;
            Check(!dstService.Switch("Eastern Standard Time", false).Success, "DST remains disabled cannot be reported successful");
            Console.WriteLine("TOTAL: " + count + " passed"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
