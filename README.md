# Windows 11 时区切换

一个轻量的中文 Windows 托盘工具，用来快速切换**整个系统的时区**。默认提供上海、洛杉矶、纽约，支持从 Windows 全部时区中搜索和管理常用项。

使用 C#、WinForms 和 Windows 自带的 `tzutil.exe`，不需要 Electron、NuGet 或 .NET SDK。程序本体约 40 KB，适用于 Windows 11 x64 和 .NET Framework 4.8 或更高版本。

## 下载与使用

直接下载 [v1.0.0 免安装包](https://github.com/cjt0325/win11-timezone-switcher/raw/refs/heads/main/downloads/TimeZoneSwitcher-v1.0.0-win-x64.zip)，解压后运行 `TimeZoneSwitcher.exe`。文件校验值见 [SHA256SUMS.txt](downloads/SHA256SUMS.txt)。

也可以下载 [仓库源码 ZIP](https://github.com/cjt0325/win11-timezone-switcher/archive/refs/heads/main.zip)，或使用 Git 克隆本仓库。

请将 `TimeZoneSwitcher.exe.config` 与程序放在一起，它用于高清显示配置。需要桌面入口时，可以为 exe 创建桌面快捷方式。

- 左键托盘时钟图标，打开管理窗口。
- 右键托盘图标，直接切换常用时区；菜单会勾选当前系统时区。
- 管理窗口支持搜索中文名称、Windows 时区 ID 和 UTC 偏移，并添加或移除常用项。
- 关闭窗口后继续在托盘运行；从托盘菜单选择“退出”结束程序。
- 再次启动会唤回已有窗口，不会重复常驻。

默认手动启动，不注册开机启动项、服务或计划任务。

## 切换与权限

切换在后台执行，程序读取系统设置确认生效后才显示成功。普通权限失败时，会显示错误原因并提供“管理员重试”；取消授权后不会显示成功。

美国时区使用 Windows 的夏令时规则。如果 Windows 开启了自动设置时区，之后可能覆盖手动选择，可通过窗口中的“系统日期和时间”入口检查。

此工具更改系统时区和本地时间显示，不修改 UTC 时钟，不执行网络查询。

## 本地配置

常用时区保存在：

```text
%LOCALAPPDATA%\TimeZoneSwitcher\settings.json
```

配置写入采用临时文件替换。无法读取时会恢复默认列表并提示；可备份的损坏文件会保留为 `settings.json.damaged-日期时间`。

## 从源码编译

在 Windows PowerShell 中进入仓库目录，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

程序输出到 `bin`。构建脚本使用 Windows 自带的 .NET Framework C# 编译器；图标已包含在源码中。

也可以指定输出目录：

```powershell
.\build.ps1 -OutputDirectory .\dist
```

## 验证

核心与界面逻辑测试使用模拟切换，不更改真实系统时区：

```powershell
.\build.ps1 -Tests
```

首版通过 19 项核心检查和 47 项界面/布局断言，包含参数校验、配置恢复、权限失败、取消授权、夏令时、托盘菜单逻辑、单实例唤回和关闭/退出行为。100%/125%/150% 的布局通过离屏缩放渲染检查；这不等同于所有真实显示器组合的测试。

下面的集成测试会实际切换到洛杉矶、纽约和上海，并在 `finally` 中恢复原时区和夏令时禁用标志：

```powershell
.\build.ps1 -IntegrationTests
```

真实切换已在 Windows 11 x64 上验证。管理员授权取消路径采用模拟测试。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `Core.cs` | 时区目录、配置存储、系统命令执行与结果确认 |
| `MainForm.cs` | 管理窗口、托盘菜单、单实例唤回窗口 |
| `Program.cs` | 程序入口与单实例控制 |
| `build.ps1` | 编译、图标生成和测试入口 |
| `tests/` | 核心、界面逻辑和真实切换测试 |

## 许可证

[MIT License](LICENSE)，允许使用、修改和分发，请保留许可证和版权声明。

## 系统接口参考

- [Windows tzutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tzutil)
- [.NET Framework WinForms 高清显示支持](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms)

---

Lightweight Windows 11 system time zone switcher with a Chinese UI, tray shortcuts, favorites and verified system changes. Built with C# and WinForms. MIT licensed.
