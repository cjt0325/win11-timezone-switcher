"""Generate the bundled offline search index from pinned Unicode CLDR data.

Python is needed only to refresh this index, not to build or run the app.
"""
from collections import defaultdict
import json
from pathlib import Path
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TAG = "release-48"
headers = {"User-Agent": "Win11-TimeZoneSwitcher-data-generator"}

def fetch(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=40) as result:
        return result.read()

def main():
    commit = json.loads(fetch("https://api.github.com/repos/unicode-org/cldr/commits/" + TAG))["sha"]
    base = "https://raw.githubusercontent.com/unicode-org/cldr/" + commit + "/"
    paths = ["common/supplemental/windowsZones.xml", "common/main/zh.xml", "common/main/en.xml", "common/supplemental/supplementalMetadata.xml", "LICENSE"]
    raw = {path: fetch(base + path) for path in paths}
    maps = ET.fromstring(raw[paths[0]]).findall(".//mapZone")
    locales = {key: ET.fromstring(raw["common/main/" + key + ".xml"]) for key in ("zh", "en")}
    countries = {key: defaultdict(set) for key in locales}
    cities = {key: {} for key in locales}
    for locale, tree in locales.items():
        for node in tree.findall("./localeDisplayNames/territories/territory"):
            if node.text and node.text not in ("↑↑↑", "∅∅∅"): countries[locale][node.attrib["type"]].add(node.text)
        for node in tree.findall("./dates/timeZoneNames/zone"):
            city = node.findtext("exemplarCity")
            if city and city not in ("↑↑↑", "∅∅∅"): cities[locale][node.attrib["type"]] = city
    extra = defaultdict(set)
    for node in ET.fromstring(raw[paths[3]]).findall(".//zoneAlias"):
        for replacement in node.attrib["replacement"].split():
            extra[replacement].add(node.attrib["type"])
    grouped = defaultdict(list)
    for node in maps: grouped[node.attrib["other"]].append(node.attrib)
    entries = []
    for windows_id, rows in sorted(grouped.items()):
        aliases, country_names, city_names, iana_ids = set(), set(), set(), set()
        primary_iana = next((r["type"].split()[0] for r in rows if r["territory"] == "001"), rows[0]["type"].split()[0])
        primary_country = next((r["territory"] for r in rows if r["territory"] not in ("001", "ZZ") and primary_iana in r["type"].split()), "")
        for row in rows:
            region = row["territory"]
            if region not in ("001", "ZZ"):
                aliases.add(region)
                for locale in locales:
                    country_names.update(countries[locale].get(region, []))
            for iana in row["type"].split():
                iana_ids.add(iana)
                iana_ids.update(extra.get(iana, []))
        for iana in sorted(iana_ids):
            if iana.startswith("Etc/"): continue
            english_city = iana.split("/")[-1].replace("_", " ")
            city_names.add(english_city)
            for locale in locales:
                if iana in cities[locale]: city_names.add(cities[locale][iana])
        city = cities["zh"].get(primary_iana, primary_iana.split("/")[-1].replace("_", " "))
        country = next(iter(sorted(countries["zh"].get(primary_country, []))), "")
        display = (country + " · " + city) if country and country != city else city
        if primary_iana.startswith("Etc/"): display = ""
        aliases.update(country_names); aliases.update(city_names); aliases.update(iana_ids)
        entries.append({"id": windows_id, "display": display, "countries": sorted(country_names), "cities": sorted(city_names), "iana": sorted(iana_ids), "aliases": sorted(aliases)})
    # Labels for newer Windows entries and legacy entries absent from this CLDR release.
    # Windows remains authoritative for their offsets and daylight-saving rules.
    supplements = [
        ("British Columbia Standard Time", "加拿大 · 不列颠哥伦比亚", ["加拿大", "Canada"], ["温哥华", "Vancouver", "不列颠哥伦比亚", "British Columbia"], ["CA", "America/Vancouver"]),
        ("Alberta Standard Time", "加拿大 · 艾伯塔", ["加拿大", "Canada"], ["艾伯塔", "Alberta", "埃德蒙顿", "Edmonton", "卡尔加里", "Calgary"], ["CA", "America/Edmonton"]),
        ("Manitoba Standard Time", "加拿大 · 马尼托巴", ["加拿大", "Canada"], ["马尼托巴", "Manitoba", "温尼伯", "Winnipeg"], ["CA", "America/Winnipeg"]),
        ("Mid-Atlantic Standard Time", "中大西洋（旧用）", [], ["中大西洋", "Mid Atlantic"], ["旧用", "legacy"]),
        ("Kamchatka Standard Time", "俄罗斯 · 堪察加（旧用）", ["俄罗斯", "Russia"], ["堪察加", "Kamchatka", "彼得罗巴甫洛夫斯克"], ["RU", "旧用", "legacy", "Asia/Kamchatka"]),
    ]
    for zone_id, display, country_list, city_list, aliases in supplements:
        if zone_id not in grouped:
            entries.append({"id": zone_id, "display": display, "countries": country_list, "cities": city_list, "iana": [], "aliases": sorted(set(country_list + city_list + aliases))})
    entries.sort(key=lambda row: row["id"])
    data = ROOT / "data"
    data.mkdir(exist_ok=True)
    (data / "timezones.json").write_text(json.dumps(entries, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    licenses = ROOT / "licenses"
    licenses.mkdir(exist_ok=True)
    (licenses / "UNICODE-LICENSE.txt").write_bytes(raw["LICENSE"])
    provenance = {"project": "Unicode CLDR with app-maintained supplemental search labels", "release": TAG, "commit": commit, "license": "Unicode-3.0 (CLDR data); MIT (app supplements)", "sources": [base + p for p in paths], "cldr_windows_zone_entries": len(grouped), "windows_zone_entries": len(entries), "app_supplements": [row[0] for row in supplements if row[0] not in grouped]}
    (data / "sources.json").write_text(json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"entries": len(entries), "commit": commit, "index_bytes": (data / "timezones.json").stat().st_size, "license_header": raw["LICENSE"].decode("utf-8").splitlines()[:4]}, ensure_ascii=False))

if __name__ == "__main__": main()
