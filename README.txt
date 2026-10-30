时区切换 1.0 — Windows 11
========================

使用
1. 双击桌面“时区切换”快捷方式，或运行 TimeZoneSwitcher.exe。
2. 在左侧选中上海、洛杉矶或纽约，点击“切换所选”。程序读回系统时区后才会提示成功。
3. 右侧可以搜索中文名称、英文 Windows 时区 ID 或 UTC 偏移，再“添加到常用”。
4. 关闭窗口后程序留在托盘。在任务栏右下角的隐藏图标区域，也可找到蓝色时钟图标。
5. 左键图标打开管理窗口，右键图标直接切换常用时区，点“退出”完全结束。
6. 已运行时再打开桌面快捷方式，会显示原有窗口，不会重复常驻。

权限与系统设置
普通权限下先尝试切换，失败会显示原因，并出现“管理员重试”按钮。
管理员重试会使用 Windows 自身的授权提示；取消后不会显示成功。
如果 Windows 正在自动设置时区，后续可能覆盖你的选择，可通过窗口右下角进入系统设置检查。
程序遵循 Windows 自带的夏令时规则，因此洛杉矶和纽约的 UTC 偏移会随日期变化。
切换会改变整个系统的本地时区和时间显示，不修改 UTC 时钟，不涉及网络连接或账号设置。

文件与数据
TimeZoneSwitcher.exe.config 用于高清显示配置，请与 exe 放在一起。
常用列表：%LOCALAPPDATA%\TimeZoneSwitcher\settings.json
损坏配置会备份为 settings.json.damaged-日期时间，并回到默认列表。
默认手动启动，不添加开机启动项、计划任务或 Windows 服务。
移动程序文件夹后，需重新创建指向新位置的桌面快捷方式。
移除工具：先从托盘退出，再删除程序文件夹和桌面快捷方式。

重新编译
源码包包含 build.ps1、C# 文件、app.config、app.manifest、图标和 tests。
在 Windows PowerShell 或 PowerShell 中进入源码目录后运行：
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
输出位于源码目录 bin，使用系统自带 .NET Framework 编译器，无需 NuGet 或 .NET SDK。
核心测试：
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Tests
核心测试使用模拟命令，不更改真实系统时区。
测试还包括隐藏窗口逻辑、单实例唤回和 100%/125%/150% 比例的布局检查。
真实切换验证（会临时更改系统时区，结束时恢复原时区）：
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -IntegrationTests

技术参考
https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tzutil
https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms
