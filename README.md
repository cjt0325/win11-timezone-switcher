# Windows 11 时区切换

中文、免安装的系统时区工具，支持离线搜索国家和城市、预览当地时间，并从托盘快速切换。使用 C#、WinForms、.NET Framework 4.8 和 Windows 自带的 `tzutil.exe`。

## 下载

[下载 v1.1.0 免安装包](https://github.com/cjt0325/win11-timezone-switcher/raw/refs/heads/main/downloads/TimeZoneSwitcher-v1.1.0-win-x64.zip) · [校验值](downloads/SHA256SUMS.txt) · [源码 ZIP](https://github.com/cjt0325/win11-timezone-switcher/archive/refs/heads/main.zip)

解压后运行 `TimeZoneSwitcher.exe`，保留同目录的 `.exe.config` 文件。可自行为 exe 创建桌面快捷方式。适用于 Windows 11 x64，不需要安装 .NET SDK、Electron、NuGet 或 Python。

## 搜索与预览

| 输入示例 | 行为 |
| --- | --- |
| `日本`、`日本时间`、`Japan`、`JST` | 查找日本时区，显示“日本 · 东京” |
| `德国时间`、`韩国`、`印度` | 按国家查找相关 Windows 时区 |
| `美国`、`加拿大`、`澳大利亚` | 列出该国家的多个时区，供用户选择 |
| `纽约`、`Berlin`、`美西` | 城市或常见叫法 |
| `Asia/Tokyo`、`Pacific Standard Time` | IANA 或 Windows 标识 |
| `UTC+9`、`GMT+05:30`、`+0530` | 按当前 UTC 偏移精确匹配 |
| `日本 UTC+9` | 组合条件 |

显示匹配数量、无结果提示，支持一键清空。选中结果可查看当地日期时间、相对本地的时差、夏令时状态、相关地区、Windows 和 IANA 标识，并复制信息。

偏移按今天计算；城市搜索使用 CLDR 代表城市和常见别名。时区列表及切换规则来自本机 Windows，因此会随系统更新而变化。

## 日常使用

- 搜索结果可“立即切换”，也可以先“加入常用”。
- 常用时区默认包含中国、洛杉矶、纽约，支持删除和上下排序；托盘菜单同步保存的顺序。
- 左键托盘时钟图标打开窗口，右键直接切换常用时区。
- 关闭或最小化窗口后收起到托盘，菜单“退出”结束程序；重复启动会唤回已有窗口。
- 撤销按钮可恢复上一次成功切换前的时区。外部程序改变时区后，过期撤销记录自动清除。
- `Ctrl+F` 聚焦搜索，`F5` 刷新，`Esc` 清空搜索或收起窗口，`Ctrl+Q` 退出。

默认手动启动，不注册开机启动项、服务或计划任务。

## 切换确认与配置

时区命令在后台执行。程序检查退出结果并重新读取系统设置，确认生效后才显示成功。普通权限失败时提供“管理员重试”；取消授权后正常返回。

如果 Windows 开启自动设置时区，后续可能覆盖手动选择，可通过窗口的“系统日期和时间”入口检查。此工具更改系统时区与本地时间显示，不修改 UTC 时钟，不查询公网 IP。

常用列表保存在 `%LOCALAPPDATA%\TimeZoneSwitcher\settings.json`，升级可沿用已有配置。写入采用临时文件替换；损坏配置可备份并恢复默认列表。

## 从源码编译和测试

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出到 `bin`，使用 Windows 自带 .NET Framework 编译器。索引与图标均已包含，普通构建无需联网。

```powershell
# 模拟命令与隐藏窗口测试，不改变真实时区
.\build.ps1 -Tests

# 实际验证洛杉矶、纽约、中国、日本、印度、英国；finally 恢复原时区与夏令时标志
.\build.ps1 -IntegrationTests
```

测试覆盖安装的 Windows 时区、国家和城市搜索、偏移精确匹配、配置恢复、排序、直接切换、撤销与外部变更、权限失败和取消授权。显示缩放采用 100%/125%/150% 离屏渲染与尺寸断言检查。测试计数随系统时区数量变化。

界面测试期间若已有用户实例运行，会跳过单实例唤回探针；其他检查继续。管理员授权取消使用模拟结果测试。

## 数据与许可证

代码使用 [MIT](LICENSE)。离线搜索索引由 Unicode CLDR 48 生成，另含项目维护的 Windows 新时区和旧版兼容标签；实际偏移和夏令时始终读取 Windows。

完整来源、固定提交与许可证见 [第三方数据说明](THIRD_PARTY_NOTICES.md)。刷新索引可运行 `python tools/generate_search_data.py`，仅此步骤需要 Python 和网络。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `Core.cs` / `Search.cs` | 系统目录、搜索、配置及切换确认 |
| `MainForm.cs` / `Program.cs` | 管理窗口、托盘、快捷键与单实例 |
| `data/` / `licenses/` | 离线索引、来源记录与第三方许可证 |
| `build.ps1` / `tests/` | 构建与验证 |

接口参考：[Windows tzutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tzutil)、[WinForms 高清显示](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms)。

---

Offline Windows 11 system time zone switcher with Chinese UI, country/city/IANA search, previews, favorites, tray shortcuts, undo and verified system changes. MIT licensed; bundled CLDR data uses Unicode License V3.