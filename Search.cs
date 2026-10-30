using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace TimeZoneSwitcher
{
    [DataContract]
    public sealed class ZoneSearchInfo
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "display")] public string Display;
        [DataMember(Name = "countries")] public string[] Countries;
        [DataMember(Name = "cities")] public string[] Cities;
        [DataMember(Name = "iana")] public string[] Iana;
        [DataMember(Name = "aliases")] public string[] Aliases;
    }

    public static class SearchIndex
    {
        private static readonly Dictionary<string, ZoneSearchInfo> entries = Load();
        public static int Count { get { return entries.Count; } }
        public static string Warning { get; private set; }
        private static Dictionary<string, ZoneSearchInfo> Load()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TimeZoneSwitcher.TimeZones.json"))
                {
                    if (stream == null) throw new InvalidOperationException("缺少国家和城市搜索索引");
                    var data = (List<ZoneSearchInfo>)new DataContractJsonSerializer(typeof(List<ZoneSearchInfo>)).ReadObject(stream);
                    return data.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException || ex is SerializationException || ex is ArgumentException)) throw;
                Warning = "国家/城市索引未加载，仍可按系统时区名称、ID 或 UTC 偏移搜索。";
                return new Dictionary<string, ZoneSearchInfo>(StringComparer.OrdinalIgnoreCase);
            }
        }
        public static ZoneSearchInfo Find(string id)
        {
            ZoneSearchInfo result;
            return entries.TryGetValue(id, out result) ? result : new ZoneSearchInfo
            { Id = id, Display = "", Countries = new string[0], Cities = new string[0], Iana = new string[0], Aliases = new string[0] };
        }
    }

    public static class ZoneSearch
    {
        private static readonly Regex offsetPattern = new Regex(@"^(?:(?:utc|gmt)\s*)?([+\-])(\d{1,2})(?::?(\d{2}))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, string> common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "China Standard Time", "中国时间 北京时间 中国标准时间 CST Asia/Shanghai" },
            { "Tokyo Standard Time", "日本时间 日本標準時 日本時間 日本标准时间 JST Japan Asia/Tokyo 大阪 札幌" },
            { "Pacific Standard Time", "美西 美国西部 美国西海岸 洛杉矶 旧金山 西雅图 PST PDT PT" },
            { "Eastern Standard Time", "美东 美国东部 美国东海岸 纽约 华盛顿 EST EDT ET" },
            { "Central Standard Time", "美国中部 芝加哥 休斯敦 达拉斯 CST CDT CT" },
            { "Mountain Standard Time", "美国山地 丹佛 MST MDT MT" },
            { "GMT Standard Time", "英国 伦敦 GMT BST UK United Kingdom" },
            { "Korea Standard Time", "韩国 南韩 首尔 汉城 KST Asia/Seoul" },
            { "India Standard Time", "印度 新德里 孟买 加尔各答 IST Asia/Kolkata" },
            { "AUS Eastern Standard Time", "澳大利亚 澳洲 悉尼 堪培拉 AEST AEDT" },
            { "Singapore Standard Time", "新加坡 马来西亚 吉隆坡 SGT MYT" },
            { "UTC", "世界协调时 协调世界时 格林尼治 Zulu Z" }
        };
        public static string Normalize(string value)
        {
            string text = (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('−', '-');
            foreach (string suffix in new[] { "标准时间", "標準時間", "夏令时间", "夏令時間", "标准时区", "標準時區", "时间", "時間", "时区", "時區", "標準時" }) text = text.Replace(suffix, "");
            return text.Replace(" ", "").Replace("_", "").Replace("/", "").Replace("-", "").Replace("·", "");
        }
        public static bool TryOffset(string query, out TimeSpan offset)
        {
            string value = (query ?? "").Normalize(NormalizationForm.FormKC).Trim().Replace('−', '-');
            value = String.Concat(value.Where(c => !Char.IsWhiteSpace(c)));
            if (new[] { "UTC", "GMT", "Z" }.Any(x => String.Equals(value, x, StringComparison.OrdinalIgnoreCase)))
            { offset = TimeSpan.Zero; return true; }
            Match match = offsetPattern.Match(value);
            int hours, minutes = 0;
            if (match.Success && Int32.TryParse(match.Groups[2].Value, out hours) && hours <= 23 &&
                (!match.Groups[3].Success || Int32.TryParse(match.Groups[3].Value, out minutes)) && minutes < 60)
            {
                offset = TimeSpan.FromMinutes((hours * 60 + minutes) * (match.Groups[1].Value == "-" ? -1 : 1));
                return true;
            }
            offset = TimeSpan.Zero; return false;
        }
        public static string[] BuildTerms(ZoneEntry entry)
        {
            string colloquial;
            if (!common.TryGetValue(entry.Id, out colloquial)) colloquial = "";
            return new[] { entry.Name, entry.Id, entry.Zone.DisplayName, entry.Zone.StandardName }
                .Concat(entry.SearchInfo.Aliases ?? new string[0]).Concat(colloquial.Split(' ')).Select(Normalize).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        }
        public static int Score(ZoneEntry entry, string query)
        {
            if (String.IsNullOrWhiteSpace(query)) return 0;
            TimeSpan offset;
            if (TryOffset(query, out offset)) return entry.Zone.GetUtcOffset(DateTimeOffset.UtcNow) == offset ? 0 : -1;
            string[] aliases = entry.SearchTerms;
            string normalized = Normalize(query);
            if (normalized.Length == 0) return 0;
            if (aliases.Any(alias => alias == normalized)) return 0;
            var terms = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!terms.All(term => MatchesTerm(entry, aliases, term))) return -1;
            return aliases.Any(alias => alias.StartsWith(normalized, StringComparison.Ordinal)) ? 10 : 20;
        }
        private static bool MatchesTerm(ZoneEntry entry, string[] aliases, string term)
        {
            TimeSpan offset;
            if (TryOffset(term, out offset)) return entry.Zone.GetUtcOffset(DateTimeOffset.UtcNow) == offset;
            string normalized = Normalize(term);
            return aliases.Any(alias => alias.IndexOf(normalized, StringComparison.Ordinal) >= 0);
        }
    }
}
