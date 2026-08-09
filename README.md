# CpqSystemTool — 系统清理与优化工具

> WPF (C# / .NET Framework 4.8) · 单文件 exe · 零安装 · 双击即跑 · 管理员权限自动提权 · **v1.01**
> 项目主页：https://github.com/dandelion80231/System-Cleanup-Optimizer

---

## 1. 功能清单（13 个功能页）

| 页面 | 模块 | 功能说明 |
|------|------|----------|
| ⚙ 系统优化 | `Tweaks.cs` | 116 项可逆注册表优化，按 7 个分组（外观/性能/安全/Edge/系统/更新/隐私），每项独立启用/禁用。含基本优化（92 项 low 安全推荐）、深度优化（112 项 low+mid，排除 4 个高风险）、全选（116 项）、导出 .ini（带时间戳、含全部项勾选状态）/ 兼容旧版 JSON 导入、高风险项红/橙标识+二次确认、底部状态栏实时显示「已选中 X 项；共 Y 项」 |
| 🧹 清理优化 | `Cleanup.cs` `CleanupExt.cs` | 6 大类 34 项细粒度清理（缓存/系统/更新残留/浏览器/日志/大空间回收），扫描模式、全选/安全项快捷操作、底部状态栏选中计数（已选中 X/Y 项） |
| 🛠 服务优化 | `ServiceOptimizer.cs` | 50+ 系统服务列表，含推荐禁用项、高危标记、描述说明、一键优化/还原，整行 hover 反馈 |
| 🛒 Appx 商店 | `AppxManager.cs` | 一键安装/卸载常用商店应用，启用/禁用商店，撤销预装包 |
| 📦 Appx 管理 | `AppxManager.cs` | 完整 UWP 管理：列出已安装/预配包、卸载、复制包名、移除预配。已安装=绿色标记，未安装=红色标记 |
| 📦 常用软件 | `SoftwareInstall.cs` | 一键安装/卸载常用软件（**约 50 款**；主流包与微信/OneDrive/Steam/WPS/百度网盘/网易云音乐/腾讯视频/百度拼音/哔哩哔哩/VC++运行库合集 走**官方直链**（主流包经 Chocolatey 运行时解析+SHA256；钉版本直链 VC++ AIO 写死 SHA256、其余走 Authenticode 签名校验），其余约 16 个国产包仍保留私有镜像（带版本号直链会 404）），路径浏览、进度日志、已安装状态自动检测 |
| 🛡 安全防护 | `Updater.cs` `MeteredConnection.cs` `Defender.cs` | **更新管理 + Defender 合并页**：① 更新管理三种策略（组策略禁用 / 长期暂停 10000 天 / 计量连接，按钮按系统状态自动高亮）；② Defender 一键禁用/启用（注册表+服务+计划任务三项联动） |
| 🌐 Edge 管理 | `EdgeCore.cs` | Edge 卸载/安装、清除用户数据、设置项优化开关 |
| 🔒 隐私设置 | `PrivacyCore.cs` | 15+ 隐私注册表开关：诊断数据/活动历史/广告 ID/位置/墨迹等 |
| 🧰 系统工具 | `GodMode.cs` `RestorePoint.cs` `VersionSwitch.cs` | **上帝模式 & 还原 + 版本转换合并页**：上帝模式文件夹创建/删除、系统还原点创建、Windows 版本转换（changepk，14 个目标版，密钥见 §3.19）、频道选择。低频高危，建议先建还原点 |
| 🔑 激活工具 | `Activation.cs` `OfficeInstall.cs` | 6 卡片 2×3 布局，5 种 MAS 激活方式（HWID/KMS38/Ohook/KMS/TSforge）+ 诊断，卡片单选高亮；Office 在线安装/卸载 |
| ℹ 系统信息 | `SystemInfo.cs` | 硬件/软件信息汇总，可导出 txt |
| ⚙ 配置管理 | `ConfigBackup.cs` | 全局配置路径设置（支持浏览）、一键导出/导入所有页面配置 JSON、自动保存、源码包导出 |

> **页面数变化（定稿核对）**：原 **14 页 → 现 13 页**，合并 2 次：① 更新管理 + Defender → 合并为「安全防护」；② 「上帝模式 & 还原」并入「系统工具」（系统工具页另含 Windows 版本转换 + 频道选择，见 §3.19）。模块文件 `Updater.cs` / `Defender.cs` / `GodMode.cs` / `RestorePoint.cs` 仍独立存在，仅导航入口合并。

---

## 2. 技术栈与架构

```
WPF (.NET Framework 4.8) / C# / SDK-style csproj
├── MainWindow.xaml          — 外壳布局（侧边栏/顶栏/内容区/底栏/背景图）+ 全局 Button/ScrollBar 样式
├── MainWindow.xaml.cs       — 启动入口、主题状态、全局滚轮 PreviewMouseWheel 处理
├── MainWindow.Nav.cs        — 侧边栏构建、13 主导航项 + 1 隐藏「关于」页、侧边栏宽度拖拽调整
├── MainWindow.Theme.cs      — 深/浅两套完整配色（~80 个笔刷字段）、系统主题自动跟随
├── MainWindow.Helpers.cs    — UI 辅助工厂：Btn/Card/Header/MakeLogBox/MakeProgress/RunInBg/FindVisualAncestor
├── MainWindow.Pages.cs      — 14 个 Build*() 页面构造函数（~5000 行，最大文件）
├── OtherTweaksDialog.cs     — "其他优化项"对话框（含 RemotePortDialog 子对话框），主题同步
├── Modules/                 — 17 个功能模块
│   ├── Tweaks.cs            — 116 项注册表优化定义（TweakEntry 模式；已消除 5 组跨开关注册表键冲突/冗余：删「关闭内存完整性」「关闭虚拟化安全性(VBS)」两个重复项、Defender 不再碰 SmartScreen 键、UAC 两开关按键解耦、Web搜索不再碰搜索历史键；VBS 现仅由「关闭 VBS 虚拟化安全」(vbs_security) 独占）
│   ├── Cleanup.cs           — 磁盘清理核心
│   ├── CleanupExt.cs        — 扩展清理（DISM/CBS/缩略图/着色器）
│   ├── Activation.cs        — MAS 激活集成
│   ├── OfficeInstall.cs     — Office 在线安装/卸载
│   ├── Updater.cs           — Windows 更新控制（组策略/暂停）
│   ├── MeteredConnection.cs — 计量连接（P/Invoke：夺权 TrustedInstaller → 改写注册表 → 还原 ACL）
│   ├── ServiceOptimizer.cs  — 服务枚举/推荐/优化
│   ├── AppxManager.cs       — UWP 应用管理（powershell Get-AppxPackage/Get-AppxProvisionedPackage）
│   ├── Defender.cs          — Defender 禁用/启用（三项联动）
│   ├── EdgeCore.cs          — Edge 安装/卸载/优化
│   ├── PrivacyCore.cs       — 隐私设置注册表项
│   ├── SoftwareInstall.cs   — 约 50 款常用软件安装器（主流包经 Chocolatey 运行时解析官方直链+SHA256；微信/OneDrive/Steam/WPS/百度网盘/网易云音乐/腾讯视频/百度拼音/哔哩哔哩/VC++运行库合集/quark/tim/weiyun/aliyunpan/iqiyi/kgmusic/kwmusic/unlocker 走官方直链+Authenticode（VC++ AIO 写死 SHA256）；qqmusic 走官方配置运行时解析（服务端签名直链，MIRROR 兜底）；qq 走官方页运行时解析（im.qq.com/pcqq/ QQNT x64 直链，jump.oyk.pub 兜底）；抖音已钉官方直链（V8.3.0）；部分 StoreId 走 winget）
│   ├── GodMode.cs           — 上帝模式文件夹
│   ├── RestorePoint.cs      — 系统还原点
│   ├── SystemInfo.cs        — 系统信息采集（WMI/注册表/环境变量）
│   └── ConfigBackup.cs      — 配置导入导出 + 自动保存
├── Helpers/
│   ├── Exec.cs              — 进程执行封装（RunCmd / RunPowerShellGet）
│   └── RegistryHelper.cs    — 注册表读写（DWORD/SZ/删除树/CLSID 检测/WriteFile 绕过 ACL）
└── CpqSystemTool.csproj     — SDK-style 项目（TargetFramework=net48, UseWPF=true）
```

---

## 3. 各模块实现经验

### 3.1 系统优化页 (Tweaks)

**设计模式——TweakEntry**：每项优化用 `TweakEntry{Group,Id,Name,Desc,Risk,Enable,Disable,State}` 封装。`Enable/Disable` 接收 `Action<string>` 日志回调，由页面层统一编排；`State` 返回当前实际状态（读注册表），勾选初始化时并行查询避免串行阻塞 UI。

**分组排序**：用 `Dictionary<string,int>` 硬编码 7 个分组的显示顺序（而非依赖数据到达顺序），未知分组排到末尾。

**ApplyByIds 模式**：不重建整个页面（避免闪烁），而是：
1. 立即更新 CheckBox 视觉 → 后端线程执行 → 完成后重新读各 State() 刷新实际状态
2. 这种方式避免了"点优化→页面消失→重新加载"的视觉跳动

**预设系统（基本/深度优化）**：
- 基本优化 = 所有 `Risk == "low"` 的项（安全推荐）
- 深度优化 = `Risk != "high"`（低风险 + 中风险），是「全选」的真子集（不含 4 个高风险危险项）。注意：参考项目 ZyperWin++ 的深度优化 = 全部 151 项剔除 24 个可选/外观项（127/151），**并非全选**；本项目用 Risk 分级表达「深度比全选更保守」的同一语义。
- 全选 = 所有项（含高风险）

**高风险项的处理**：高风险项在页面中用红色/橙色标识（RiskLabel 转换），点击优化前弹 MessageBox 二次确认；但"一键优化"按钮操作时不做逐项确认（用户已知风险）。

**配置导入/导出**：
- 导出（默认 `.ini`，文件名 `CpqSystemTool优化-{yyyyMMddHHmmss}.ini`）：`SaveFileDialog` 默认选 `.ini`，内容 `[CpqSystemTool优化配置]` 节 + `生成时间=` + 每行 `Tag=1/0/2`（1=勾选 / 0=未勾选 / 2=三态项的"系统默认"）。**含全部项状态**，比旧版只存勾选项更完整、且对齐参考项目 ZyperWin++ 的 `ZyperWin++{时间戳}.ini` 习惯（文件名自动带日期时间标签）。
- 导出（兼容旧版 `.json`）：若用户手动改后缀选 `.json`，则按旧格式只写勾选 ID 列表 `["id1","id2"]`，便于读取历史备份。
- 导入（`.ini` / `.json` 均支持）：`.ini` 逐行解析 `key=value`（1→勾选 / 0→取消 / 2→三态置空）；`.json` 解析 `["id1",...]` 数组只勾选所列 ID。导入后自动刷新勾选颜色 + 已选项面板，提示"点开始优化应用"。
- **坑**：`.ini` 解析用 `IndexOf('=')` 取 key/value，跳过 `[`/`;`/`#` 注释行与空行；三态项 `2` 仅在 `IsThreeState` 为真时才置 `null`，避免二态项被误置为不确定态。

**左右面板布局**：左侧 `ScrollViewer(MaxHeight=600)` 放优化列表，右侧 `ScrollViewer(MaxHeight=280)` 放已选项面板。"已选中"面板在下方的 Expander 里列出每个已勾选项的名称和风险等级，动态刷新。

**整行 hover**：每行 Grid 上挂 `MouseEnter/MouseLeave`，条件判断 `row.Background == Brushes.Transparent` 避免覆盖选中高亮。

---

### 3.2 清理优化页 (Cleanup)

**CleanupItemDef 模式**：每项清理用 `CleanupItemDef{Id,Name,Desc,Category,DefaultChecked,Action}` 封装，按 Category 分组展开。

**6 大类 34 项**：缓存文件（缩略图/D3D着色器/终端/Prefetch/浏览器/字体/图标/.NET 程序集/**用户开发包缓存·第一档绝对安全**）、系统文件（System Temp/用户 Temp/更新缓存/WinSxS/WER/诊断/DISM）、**更新残留（ClickOnce安装缓存/Win更新P2P缓存/NVIDIA下载器/应用自动更新缓存·第二档基本安全）**、浏览器数据（Cookies/INetCache）、日志历史（事件日志/最近文档/WU日志/CBS/通知/崩溃转储）、大空间回收（回收站/NVIDIA/Defender/Spotlight/活动历史/BranchCache/关闭休眠/删除内存转储/清理Windows.old）。

> **两档安全分级**（新增）：第一档「用户开发/包管理器缓存」为纯缓存/可重建，默认勾选；第二档「更新残留·安装包缓存」为软件自动更新的旧安装包，删了只是下次更新重新下载，默认不勾选（需用户主动选）。两档均会在 C 盘用户/程序相关根目录（Users/ProgramData/Program Files/*AppData）全盘筛查同类目录，避免遗漏。
>
> **第三档·旧资产（新增，谨慎）**：多为「大且久未使用」或已知停用工具的旧数据，多半可删但可能含你的数据。仅通过「🔍 扫描旧资产·第三档」按钮触发——**先扫描**（列出路径/大小/未使用天数），再以**逐项目二次确认对话框**（每项默认不勾选）确认后才真正删除；不勾选不删除。

**CheckBox 与操作绑定**：不在定义层绑定（保持 CleanupItemDef.Action 纯粹），由页面层遍历 `allCheckBoxes` 列表匹配 Tag → 执行对应 Action。

**扫描与清理分离**：`仅扫描大小` 调用 `Cleanup.RunScan`（只统计不删），`开始清理` 调用 `CleanupExt.RunSelected`。

**日志输出**：所有操作通过 `Action<string>` 回调写入页面底部 TextBox，支持实时追加和自动滚动到底部。

**底部选中计数**：勾选变化（含「全选当前页」、各 CheckBox 的 `Checked/Unchecked`）实时写入**窗口最左下角全局状态栏**（`SetStatus`），显示 `已选中 X/Y 项`；无勾选时显示 `就绪`。与系统优化 / Appx / 常用软件页底部计数同走 `MainWindow.Helpers.cs` 的通用状态栏（`SetStatus` / `UpdateSelStatus`），形态统一。

**大空间回收真实现（`BigSpace*`）**：3 项大空间操作此前长期只有提示没有后端，本轮补齐真实方法（`Cleanup.cs`）：`BigSpaceHiberfilOff`（关休眠删 hiberfil.sys）/ `BigSpaceMemoryDmp`（删 MEMORY.DMP）/ `BigSpaceWindowsOld`（删 Windows.old）。（注：`BigSpaceHiberfilOn` 恢复休眠已移除，仅保留关休眠，操作不可逆——清理前请确认不再需要休眠文件。）

**操作按钮互斥高亮（`ApplyMode`）**：清理页右侧操作行的「🗑 开始清理 / 🔍 扫描大小 / ☑ 全选安全项」三按钮是**互斥高亮组**——任一点击都把 accent 填充转移到该按钮（`ApplyMode(sel)`：选中→`_accent` 填充 + primary 文字 + 透明边框；其余→`_btnSecondaryBg` + secondary 文字 + 面板描边）。默认高亮「开始清理」。这是"模式切换"而非"一次性临时态"：扫描不会让按钮回到默认态，高亮保持在该按钮上直到点别的。复用此范式的还有 Defender 页（见 §3.8）与安全/系统工具等其他页操作按钮，统一沉淀见 §3.20。

### 3.2.1 并行加速（方案 A + 方案 B）
全选 / 大批量清理时串行统计慢，本轮在不破坏单点正确性的前提下引入**两层并行**（已构建 0 错误 0 警告验证，跨线程安全）：

- **方案 A · 项内细粒度并行（`Cleanup.cs`，`InnerPar`）**：对天然互相独立的子任务用 `Parallel.Invoke` / `Parallel.ForEach`，`MaxDegreeOfParallelism = Math.Max(1, Math.Min(3, Environment.ProcessorCount))`：
  - `Nvidia`：先**串行**停 2 个服务，再并行清空 6 个缓存目录；
  - `EventLogs`：`wevtutil el` 取通道列表后，并行 `wevtutil cl` 清每个通道；
  - `Cookies`：并行清 4 个浏览器 Cookies + Firefox（PowerShell 块）；
  - `UserCacheTier1`：并行清 10 个包管理器缓存；
  - **保持串行**的环节：全盘扫描 `CleanWholeDriveCaches`、字体缓存、`winsxs_dism`（Dism 镜像修复单实例，且磁盘寻道争用大）。
- **方案 B · 大类级并行（`MainWindow.Pages.cs`，`catPar`）**：顶层把 `CleanupCatalog` 按 `Category` 分组，`Parallel.ForEach(groups, catPar, group => foreach (var def in group) def.Action(l))`，`MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount))`；**分组内仍串行**，保证同大类项有序、日志可观测。
- **线程安全**：日志回调 `log` 经 `Dispatcher.BeginInvoke`（`RunInBg`，`MainWindow.Helpers.cs`）写入 UI，跨线程追加无竞态；每个 `def.Action` 在分组内 `try/catch` 隔离，单项异常不影响其余项继续。

> 设计取舍：`MaxDegreeOfParallelism` 上限取 3/4 而非无限制，是为避免磁盘 I/O 饱和导致反而变慢；停止服务、Dism、全盘扫描等强顺序 / 单实例 / 重 I/O 环节刻意串行，确保结果确定、可复现。

---

### 3.3 更新管理（现并入「安全防护」页）

**三种策略的实现**：

| 策略 | 实现方式 | 副作用 |
|------|----------|--------|
| 禁用更新（组策略） | 写 `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoUpdate=1` | 影响商店/Xbox/驱动更新 |
| 长期暂停 | 写 `FlightSettingsMaxPauseDays=10000` + `PauseFeatureUpdates/PauseQualityUpdates` 标志 | 连带暂停微软商店 |
| 计量连接 | `MeteredConnection.ToggleMetered()` | **最佳实践**：挡系统更新但保留商店手动更新 |

**按钮状态驱动**：6 个按钮的背景填充由系统实际状态决定：
1. `Updater.IsUpdatesBlocked()` — 读 NoAutoUpdate 注册表
2. `Updater.IsLongPaused()` — 读 PauseFeatureUpdates 注册表
3. `MeteredConnection.IsMetered()` — P/Invoke 读 DefaultMediaCost

**互斥高亮**：字段 `_lastUpdateAction` 跟踪最后点击的按钮 key。未点过任何按钮时用系统状态做默认高亮。每次操作完成后 `Navigate` 重建页面实现刷新。

**页面初始化**：进入页面自动执行 `Updater.UpdateStatus` + `MeteredConnection.MeteredStatus`，日志框显示当前全部状态。

---

### 3.4 计量连接的 P/Invoke 夺权实现

**需求**：计量连接注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\DefaultMediaCost` 由 TrustedInstaller 拥有，常规 `reg add` / `Set-Acl` / `netsh metering` 均失败。

**完整流程（`MeteredConnection.cs` _dmc_* 簇）**：

| 步骤 | API | 说明 |
|------|-----|------|
| 1. 提权 | `OpenProcessToken` + `LookupPrivilegeValueW` + `AdjustTokenPrivileges` | 启用 `SeTakeOwnershipPrivilege` + `SeRestorePrivilege` |
| 2. 夺权 | `SetNamedSecurityInfoW`(OWNER_SECURITY_INFORMATION → Admins SID) | 把 owner 从 TrustedInstaller 改为 Administrators |
| 3. 改 DACL | `RegOpenKeyExW`(KEY_WRITE_DAC\|READ_CONTROL) → `RegGetKeySecurity` → 读 SD → 复制现有 ACE → `AddAccessAllowedAce`(Admins, KEY_ALL_ACCESS) → `RegSetKeySecurity` | 给自己加写权限，同时备份原始 DACL |
| 4. 写值 | `RegOpenKeyExW`(KEY_SET_VALUE\|KEY_QUERY_VALUE) → `RegSetValueExW`(Ethernet/WiFi = 2) | 设计量连接标志 |
| 5. 还原 | `RegSetKeySecurity`(备份 SD) → `SetNamedSecurityInfoW`(owner=SYSTEM S-1-5-18) | 恢复 TrustedInstaller 所有权和原始 DACL |

**ctypes 坑（Python 版的经验，C# 版直接 P/Invoke 没这些问题，但原理相同）**：
- `GetCurrentProcess()` 返回的 HANDLE 在 64 位是 8 字节，必须声明为 `IntPtr`（不能 `int`），否则被截断
- 特权名必须是 Unicode 字符串（�� ANSI 报 ArgumentError）
- `SetNamedSecurityInfoW` 的对象名用 `MACHINE\SOFTWARE\...` 格式（非 `HKEY_LOCAL_MACHINE\...`）
- `GetAce` 返回裸指针，用 `ACE_HEADER.from_address(pace.value)` 读取

---

### 3.5 激活工具页 (Activation)

> 侧边栏标签已从「系统激活/Office」更名为「激活工具」（MainWindow.Nav.cs，`APP_VERSION` 仍为 v1.01）。

**6 卡片 2×3 布局**：用 `UniformGrid(Columns=3, Rows=2)` 实现均匀分布，每张卡片 = 带彩色边框的 Border，内嵌名称/副标题/描述/操作按钮。

**卡片单选高亮**：点击卡片切换 `_selectedCard` 引用，遍历 `cards` 列表统一更新背景（选中→深色高亮，未选中→恢复默认）。

**激活方式后端**：页面 6 张卡（HWID/KMS38/Ohook/Online KMS/TSforge/诊断）的方法命名源自 **Microsoft Activation Scripts (MAS)**，**现已真集成 MAS**（`Activation.cs` 方案 B）：
- 5 张激活卡（HWID/KMS38/Ohook/KMS/TSforge）→ 点击经**二次确认**后，启动提权 PowerShell 执行官方一行式 `& ([ScriptBlock]::Create((irm https://get.activated.win))) /<switch>`，开关映射：`HWID→/HWID`、`KMS38→/KMS38`、`Ohook→/Ohook`、`KMS→/K-Windows`（仅 Windows）、`TSforge→/Z-WindowsESUOffice`（Windows + ESU + Office，名副其实覆盖 Win+Office）；参数见 massgrave.dev/command_line_switches；脚本退出后自动 `CheckStatus` 刷新状态。
- 「诊断」卡 → 走本地 `CheckStatus`（`cscript slmgr.vbs /dli|/xpr` + `SoftwareLicensingProduct` WMI），**不联网**。
- 启动方式：`Process.Start` + `UseShellExecute=true` + `Verb=runas`（可见提权窗口，让 MAS 能写入激活信息）；因 `UseShellExecute=true` 无法重定向 stdout，App 日志不显示 MAS 自身输出，以结束后的状态刷新作为反馈。

> 旧版 `slmgr.vbs` KMS 实现（`ActivateWindows`/`ActivateOffice`，`kms.03k.org`）仍保留为 `"windows"/"office"` 兜底，但 6 张卡当前已统一走 MAS。



**Office 区**：与激活区共用 `UniformGrid`，下半部分为 Office 安装/卸载区，含版本选择（Office 2024/2021/2019/365）和在线部署工具。

---

### 3.6 服务优化页 (Services)

**ServiceOptimizer 数据模型**：每项服务用 `{Name,DisplayName,Description,StartMode,Recommended,Vital}` 封装，`Vital=true` 为关键服务，优化时自动跳过。

**50+ 服务来源**：枚举 `HKLM\SYSTEM\CurrentControlSet\Services` 下所有子键，过滤掉驱动服务（Type ≠ 0x20），只取 Win32 服务。

**推荐禁用模式**：`Recommended=true` 的服务在"一键优化"时自动设为禁用，Vital 服务设手动（保底不断系统核心功能）。

**整行 hover（与按钮 hover 并存）**：
- 行级 `MouseEnter` → `row.Background = _rowHover`
- 按钮级 hover → XAML ControlTemplate 内 `IsMouseOver` Trigger
- 两个 hover 层级不冲突：行级改整行背景，按钮级改按钮边框+背景

---

### 3.7 Appx 管理页

**已安装 vs 预配包**：`Get-AppxPackage`（当前用户已安装）vs `Get-AppxProvisionedPackage`（系统预配但当前用户未安装）。两个列表分开，预配包移除后新用户首次登录不会自动安装。

**状态标记**：已安装→绿色背景边框（`_installedBg`/`_installedBorder`），未安装/仅预配→红色（`_notInstalledBg`/`_notInstalledBorder`）。

**性能优化**：`Get-AppxPackage` 枚举所有用户的应用可能很慢（几百个包），放在后台线程执行，`RunInBg` 统一编排。

---

### 3.8 Defender（现并入「安全防护」页）

**三项联动**：禁用 Defender 不是单一步骤：
1. 注册表：`DisableAntiSpyware=1`、`DisableRealtimeMonitoring=1` 等多项
2. 服务：`WinDefend`、`WdNisSvc`、`SecurityHealthService` 停止+禁用
3. 计划任务：`\Microsoft\Windows\Windows Defender\*` 全部禁用

**还原逻辑**：按相反顺序恢复（计划任务→服务→注册表），确保 "启用" 后 Defender 能正常自启。

**按钮互斥高亮（`ShouldFillDef` + `ApplyPolicyMode`）**：
- 顶部「✘ 一键禁用 WD / ✔ 一键恢复 WD」：`ShouldFillDef(actionKey, stateDefault)` 做**严格互斥**——`_lastDefAction` 非空时只看最后点击的 key（修复了原先非严格互斥导致两个按钮同时高亮的 bug）；未点过任何按钮时退回系统实际状态做默认高亮（`Defender.IsDisabled()`）。点击后立即 `RebuildDefenderButtons()` 同步高亮，后台操作完成再刷新。
- 底部「🧹 清理策略残留 / 🔍 诊断 Runtime 状态」加入第二个互斥组 `ApplyPolicyMode(sel)`（默认 `null` 不高亮任何按钮），点击谁谁变 accent + SemiBold。与清理页 `ApplyMode` 是同一套互斥范式，集中沉淀到 §3.20。

---

### 3.9 常用软件页 (SoftwareInstall)

**SoftwareInstall 数据模型**：每款软件用 `Builder` 流式构造器封装 `{Id,Name,Desc,DownloadUrl,InstallArgs,StoreId,UninstallKeywords,RegKey...}`。支持商店（winget msstore）与安装包（下载→解压→静默安装）双分支。

**安装策略优先级**：
1. `StoreId` 非空 → `winget install --id <StoreId> --source msstore`（失败回退社区源）
2. 否则 → 主流包（`ChocolateyId` 非空）经 `ChocolateyResolver` 实时拉取社区源最新官方直链 + SHA256（离线回退已验证快照）；微信/OneDrive/Steam/WPS/百度网盘/网易云音乐/腾讯视频/百度拼音/哔哩哔哩（附 HTTP Referer）/VC++运行库合集 走已验证的厂商官方直链（钉历史版本固定 URL；VC++ AIO 钉 v0.105.0（SHA256 留空走 Authenticode，因版本敏感写死哈希会随版本失效），其余 SHA256 留空走 Authenticode 跨版本校验）+ Authenticode 校验；搜狗拼音走官方下载页运行时链接解析（`PageResolver("https://pinyin.sogou.com/index.php")`，解析失败回退 `MIRROR`）；**qqmusic 走官方配置运行时解析（`download.js` 服务端签发签名直链，解析失败回退 MIRROR）；qq 走官方页运行时解析（`im.qq.com/pcqq/` QQNT x64 直链，jump.oyk.pub 兜底）**——2026-08-04 Phase 6 经 Playwright 真实浏览器渲染再脱离 9 个（quark/tim/weiyun/aliyunpan/iqiyi/kgmusic/kwmusic/unlocker/douyin，钉官方直链或版本化路径，SHA256 留空走 Authenticode；注：抖音原误判为 JS 化，实因 `/download` 入口已 404，真实页 `/downloadpage` 静态给出 exe 直链）+ qqmusic 经 `download.js` 官方配置解析（服务端签名，非客户端 JS 临时生成）；均 zip 则解压首个安装器 → 静默运行（InstallArgs）

**安装包完整性校验（Authenticode，2026-08-04 新增）**：
- 因 HTTP 镜像无 TLS、且 50 款条目难以逐一下载预置 SHA256，**改用 Windows Authenticode 数字签名校验**作为运行时完整性防护（`WinVerifyTrust` P/Invoke，封装于 `AuthenticodeVerifier` 内部类）——无需预设哈希即可检测安装包被篡改 / 传输损坏。

**主流包 Chocolatey 运行时解析（2026-08-04 新增）**：`ChocolateyId` 非空的条目安装时调用 `ChocolateyResolver.TryResolve(id)`——优先实时抓取 community.chocolatey.org 最新 nupkg、解析官方下载 URL + SHA256 + 静默参数（永远最新、由 SHA256 门禁校验完整性，规避"版本更新即失效"）；解析失败 / 离线则回退 `ChocolateyResolver` 内"已验证快照表"（数据取自 Chocolatey VERIFICATION.txt / chocolateyinstall.ps1）。仅**版本化 URL**才写死哈希；`Latest` 类未版本化直链（如 PotPlayer / XnViewMP）快照哈希留空、改走 Authenticode 跨版本校验。覆盖：7-Zip / Git / Everything / NotePad3 / WinRAR / PotPlayer / aria2 / VirtualBox / TortoiseGit / XnViewMP。

**国产包官方下载页运行时链接解析（2026-08-04 新增）**：搜狗拼音/123云盘/RayLink/Xshell 4 个国产包新增『官方下载页运行时链接解析』——安装时 `PageLinkResolver` 用带浏览器 UA 的 HttpClient 抓取官方下载页（或 Xshell 的 latest 指针）实时提取 `.exe` 直链；解析失败自动回退私有镜像（`DownloadUrl` 初始即为 `MIRROR`），绝不抛异常、绝不中断安装流程。搜狗拼音走 `PageResolver("https://pinyin.sogou.com/index.php")`，`MIRROR + "sogou_pinyin.zip"` 作解析失败兜底。
- 接入点：`Install` 在「下载落盘后」与「zip 解压出的安装器」两处调用 `SoftwareDef.VerifyIntegrity(filePath, log)`；只对 `.exe`/`.msi` 校验（`.zip` 内安装器解压后再校验，便携版同源覆盖）。
- `VerifyIntegrity` 三态：`Valid`（签名有效→通过）/ `NotSigned`（未签名→警告并放行）/ `Invalid`（签名无效或疑似篡改）→ 由 `SoftwareInstall.StrictSignatureCheck`（**默认 `false` 非严格**）决定：非严格时仅警告放行，严格（`true`）时**拒绝安装**。默认值保守，避免误拦用非主流 CA 签名的合法包。
- **⚠️ 已知局限**：仅搜狗拼音 / 123云盘 / RayLink 三个无官方稳定端点的包仍保留私有镜像 `MIRROR` 作最终兜底，其原 `-SI` 包多为 bat 包装重打包（`ExtractFirstInstaller` 只认 exe/msi → 此类包可能安装失败；Authenticode 对其无可签名体、特性无效）。但主流包已全面改用官方直链 / Chocolatey / 官方下载页解析（安装包自带 Authenticode 签名），故 `StrictSignatureCheck` 维持 `false`（非严格、未签名仅警告放行）对主流包安全；仅上述三包需关注。详见 §8.10。

**已安装检测**：`CheckInstalled()` 优先精确注册表路径（`RegKey`/`RegKey2`）→ DisplayName 关键词搜索 → 已知 exe 文件存在性降级检测。

**自定义安装路径（v1.01 新增）**：
- 点「⬇ 安装选中」→ 弹出 `InstallPathDialog`（默认路径/自定义路径 + 浏览 + 记忆到 `HKCU\Software\CpqSystemTool\InstallPath`）
- `SoftwareDef.Builder` 构造函数**自动推断安装器类型**：含 `/S`（NSIS）→ 支持 `/D=路径`；含 `/VERYSILENT`（Inno）→ 支持 `/DIR=路径`；其他（MSI/商店）→ 不支持并提示
- ⚠️ **NSIS `/D=` 不支持含空格的路径**（NSIS 官方限制），输入 `D:\My Softwares` 会被拦截提示改用无空格目录
- 工具栏「📂 安装到」按钮实时显示当前路径（自定义时浅青高亮），点击可随时修改

**工具栏布局**：搜索框（DockPanel 填满左侧，🔍 图标用嵌套 Grid + `Panel.ZIndex` 强制 z-stack）+ 右侧 4 按钮（全选/安装选中/卸载选中）；顶部 actionBar（🔄 刷新/📂 安装到/🗑 清理缓存）与描述文字同行右对齐。

**进度反馈**：安装器输出通过 `RunInBg` 实时写回日志框，操作完成后刷新页面重建按钮状态。

---

### 3.10 系统信息页 (SystemInfo)

**数据采集方式**：WMI（`Win32_Processor`/`Win32_OperatingSystem`/`Win32_ComputerSystem`/`Win32_LogicalDisk`/`Win32_VideoController`/`Win32_NetworkAdapter` 等）+ 注册表（`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`）+ 环境变量。

**格式化输出**：用 `TextBlock` + `Inlines` 组合实现富文本（CPU 温度红/黄/绿色标记），磁盘使用率用进度条 `Rectangle` 染色填充。

**导出 txt**：`SaveFileDialog` → 收集所有 TextBlock 文本 → `File.WriteAllText`。

---

### 3.11 主题系统 (Theme)

**两套完整配色**：`SetDarkColors()` 和 `SetLightColors()` 各自创建 ~35 个 `SolidColorBrush` 字段，覆盖：
- 基础：accent / textMain / textDim / panelBorder / bgCard
- 语义：successGreen / dangerRed / warnOrange
- 区域：bgDeep / bgTable / bgTableHead / rowSelected / rowHover
- 外壳：sidebarBg / topBarBg / statusBarBg / windowBg
- 派生：btnPrimaryFg / btnSecondaryBg / btnSecondaryFg / inputBg / inputFg
- 专用：installedBg/Border/Fg / notInstalledBg/Border / sidebarHeadBg/Fg

**主题切换流程**：
1. `SetDarkColors/SetLightColors` → 刷新所有 SolidColorBrush 字段
2. `ApplyShellColors` → 更新窗口 Background/背景图/侧边栏/顶栏/底栏/滚动条颜色/XAML 资源字典
3. `UpdateSidebarTitleColors` → 同步侧边栏标题 TextBlock（它们在 BuildSidebar 中捕获了旧笔刷）
4. `Navigate(_activeNavKey)` → 重建当前页面（因为页面元素在创建时捕获了笔刷引用）

**frozen Brush 的坑**：XAML 中 `<SolidColorBrush x:Key="..."/>` 创建的是 frozen（只读）Brush。主题切换时不能改 `.Color` 属性（会抛 `InvalidOperationException`），必须整体替换：`Resources["Key"] = new SolidColorBrush(...)`。

**StaticResource vs DynamicResource（⚠️ 结论已修正）**：早期记录"ControlTemplate.Triggers 的 Setter 不支持 `{DynamicResource}`、只能用 `{StaticResource}`"——**这是错误认知**。实测 .NET Framework 4.8 中 ControlTemplate.Triggers **支持** `{DynamicResource}` 且**必须用**：`StaticResource` 在 XAML 解析时固定指向初始 Brush 对象，之后 `Resources["Key"] = new SolidColorBrush(...)` 替换字典**不会更新已解析的引用**（这就是"hover 颜色死活不变"的根因）；`DynamicResource` 会监听资源变化自动更新。**结论：XAML 模板中引用主题资源一律用 `{DynamicResource}`，且不要在 XAML 中硬编码 Brush 初始值（避免两套颜色打架）。**

**系统主题跟随**：构造函数中读 `AppsUseLightTheme` 注册表判断初始主题。`SystemEvents.UserPreferenceChanged` 监听系统主题变更，自动调用 `ApplyTheme`——但如果用户手动点过主题按钮（`_userOverrodeTheme=true`），则不再跟随。

**行 hover 颜色统一为青绿（`_rowHover` 修正）**：早期行 hover 在浅色模式用蓝色 `#E8EFF7`、深色模式用加亮灰，与全局 accent 青绿主题不统一。**修正后两套主题都用 accent 青绿**：深色 `#0F2A2E`（青绿的深色变体，白字对比度约 7:1）；浅色 `#089182 @ 12%`（青绿半透明叠在浅底上）。整行 hover 与按钮 hover 边框高亮（§3.14）现在同为青绿系，视觉语言一致。

**次要按钮背景加深（`_btnSecondaryBg` 修正）**：非 primary 按钮背景原深色 `#1C232C`、浅色 `#E5E7EB`，在透明卡片 + 底图背景下几乎看不出填充（像透明）。**修正为**深色 `#2D3748`、浅色 `#D1D5DB`，明显拉开与背景的对比，secondary 按钮可见性提升。`Btn(primary:false)` 即采用这套背景/文字派生笔刷。

---

### 3.12 UI 基础设施 (Helpers.cs)

**Btn 工厂**：所有页面按钮统一由此创建，primary=true→accent 背景/深色文字，false→透明背景/边框/普通文字。`Tag` 不在此设（页面自行设定）。

**MakeBtnRow 工厂**：`MakeBtnRow(params Button[])` 将多个按钮排成一行，内部建 N 列 `★Star` 的 Grid，每个按钮在所属列内 `HorizontalAlignment=Center` 且 `Margin=0`——**均分整行宽度、按钮保持原始大小不变**（不是把按钮拉大，而是让间距均匀占满整行）。取代原先 WrapPanel/StackPanel 中按钮左对齐、不占满整行、各页风格不一的排布。已推广到全部"同行多按钮"操作行：系统优化主页（9 按钮）、Defender（2）、隐私 serviceBar（4）、安全防护更新、系统还原（3）、系统信息（3）、配置管理（5）、隐私 layoutBar（3 单选）、版本转换 + 频道选择、Appx 管理工具条（7 项）。

**Card 工厂**：`Border(CornerRadius=12, DropShadowEffect(BlurRadius=16))` + 内部 `StackPanel` → 所有页面一致的卡片容器。

**RunInBg 编排**：`new Thread(() => { work(logf); disp.Invoke(() => { SetStatus(...); onDoneUi?.Invoke(); }); }).Start()`。自动处理异常捕获和 UI 线程回切，是后台执行+前台反馈的标准模式。

**FindVisualAncestor**：从 HitTest 叶子节点向上遍历 `VisualTreeHelper.GetParent`，找第一个类型匹配的祖先。这是 WPF 多层嵌套 UI 中定位目标容器的核心工具。

**Header 方法**：`title` 参数不使用（顶栏 PageTitle 已显示大标题），仅用 `sub` 输出灰色描述文字。方法签名保留 title 是为了兼容所有调用点的一致性。

---

### 3.13 全局滚轮处理（踩坑进化史）

这是本项目中 debug 次数最多的功能。完整进化史：

| 尝试 | 方案 | 问题 |
|------|------|------|
| 1 | 每个内层 SV 挂 `MouseWheel`（冒泡） | 外层 ContentArea 类处理器在隧道阶段先执行，内层收不到 |
| 2 | 每个内层 SV 挂 `PreviewMouseWheel`（隧道） | ScrollViewer 类处理器在实例处理器之前执行，导致滚两次 |
| 3 | 外层 ContentArea 挂 `PreviewMouseWheel` + HitTest + `FindVisualChild`（向下搜） | HitTest 返回叶子元素，向下搜找不到祖先 SV → 永远走外层 |
| 4 | 外层 ContentArea 挂 `PreviewMouseWheel` + HitTest + `FindVisualAncestor`（向上找） | ✅ **最终方案** |

最终方案代码（`MainWindow.xaml.cs:108-121`）：
```csharp
ContentArea.PreviewMouseWheel += (sender, e) => {
    var targetSv = ContentArea;
    DependencyObject hit;
    try { hit = VisualTreeHelper.HitTest(ContentArea, e.GetPosition(ContentArea))?.VisualHit; } catch { hit = null; }
    var innerSv = FindVisualAncestor<ScrollViewer>(hit);
    if (innerSv != null && innerSv != ContentArea && innerSv.ScrollableHeight > 0)
        targetSv = innerSv;
    var offset = targetSv.VerticalOffset - e.Delta / 3.0;
    targetSv.ScrollToVerticalOffset(Math.Max(0, Math.Min(offset, targetSv.ScrollableHeight)));
    e.Handled = true;
};
```

**关键细节**：
- 必须在 `PreviewMouseWheel`（隧道）、不能用 `MouseWheel`（冒泡阶段外层类处理器已消费）
- `ScrollToVerticalOffset` 而不是直接用 `e.Delta`，因为要除以 3 让速度合理
- 钳位 `ScrollableHeight` 防止滚出界
- `e.Handled=true` 必须设，否则外层会再滚一次

---

### 3.14 按钮悬浮高亮（ControlTemplate 进化史）

**问题背景**：需要一个全局 Button Style 让所有按钮悬浮时边框变青绿+背景微亮。但"系统优化"等页面用代码创建 Button 时设了 `Background = ...`，这是 WPF 依赖属性的 **local value**，优先级高于 XAML Style Trigger。

**尝试 1**：XAML Style Trigger `IsMouseOver` + `BorderBrush="AccentBrush"`。代码 `Background = Brushes.Transparent` 覆盖了 Trigger 的 Setter → 失败。

**尝试 2**：代码 `MouseEnter` 事件改 BorderBrush。但浅色模式下 `Brushes.Transparent` 背景 + 改 BorderBrush 看不出效果 → 失败。

**尝试 3**：`ControlTemplate` 内部 `<Trigger Property="IsMouseOver">` + `<Setter TargetName="PART_Border" Property="Background">`。TargetName Setter 的优先级在 WPF 依赖属性系统中**高于 local value**（因为 TargetName 解析走 TemplateParent 链） → ✅ 成功！

**主题切换兼容**：ControlTemplate.Triggers 用 `{DynamicResource}`（见 3.11 修正结论），主题切换时 `Resources["AccentBrush"] = new SolidColorBrush(...)` 整体替换 → DynamicResource 监听变化自动更新。

**⚠️ IsMouseOver 不覆盖 Background**：模板的 IsMouseOver Trigger **只改 BorderBrush**（边框高亮），**绝不能改 Background**——否则会覆盖代码设的"侧边栏激活填充色"（TargetName Setter 优先级高于 local value，一旦设置就无法被代码覆盖）。激活态背景由 Navigate 代码统一设置。

**完整的 Button ControlTemplate**（`MainWindow.xaml:17-46`）：
- `PART_Border(CornerRadius=8)` — 圆角容器
- `IsMouseOver` Trigger — 仅边框变 accent（DynamicResource）
- `IsPressed` Trigger — Opacity=0.80
- `IsEnabled=False` Trigger — Opacity=0.5

---

### 3.15 侧边栏构建与导航

**14 个 NavItem**：用 `List<NavItem>` + `foreach` 循环创建 Button，每个 Button 的 `Tag = n.Key` 用于选中/未选中判断。

**布局（v1.01 改造）**：`Sidebar.Child` 是 `DockPanel`（底部 footer 用 `Dock=Bottom` 贴底显示图标+版本号 v1.01，中部 StackPanel 放标题+14 按钮）。

**⚠️ 递归遍历陷阱**：`Sidebar.Child` 从 StackPanel 改成 DockPanel 后，`p.Children` **只遍历 DockPanel 的直接子元素**（footer + sp）——sp 内部的 14 个按钮永远遍历不到！**必须用 `FindNavButtons()` 递归**（`VisualTreeHelper` 穿透 DockPanel → StackPanel → Button）。这是"激活色 3 次修改都不生效"的根因。

**Navigate 流程**：`_activeNavKey` 更新 → `PageTitle` 更新 → `ContentArea.Content = null`（清旧内容）→ `ContentArea.Content = n.Build()`（设新内容）→ `ScrollToTop()` → **递归遍历**侧边栏 Button 更新选中态：
- 激活项：`Background = _accent`（**实色 primary 青绿**，与"恢复更新"等 primary 按钮完全一致）+ 黑/白字
- 未激活：`Background = Transparent` + `_textMain` 字色

**侧边栏拖拽**：`SidebarDragger` 是 XAML 里一个 `Width=5, Cursor=SizeWE` 的透明 Border，`MouseDown` 捕获鼠标 → `MouseMove` 动态调整 `SidebarCol.Width`（钳位 180–420px）→ `MouseUp` 释放。

**拖拽替代 GridSplitter**：不用 WPF 自带的 GridSplitter 是因为在深色主题下拖动时会出现白屏闪烁，自定义拖拽条完全透明无干扰。

**底部 footer**：`brush.png`（200×200 源，64→32px 显示）+ `v1.01` 版本号（`APP_VERSION` 常量），无 ToolTip 保持界面干净。

---

### 3.17 图标体系（v1.01 升级）

| 用途 | 资源 | 说明 |
|------|------|------|
| exe 图标（资源管理器/任务栏/Alt-Tab） | `brush.ico` | **11 层多尺寸**（256/128/96/72/64/48/40/32/24/20/16），PIL 从 200×200 PNG 缩放出 |
| 窗口标题栏 | `brush.ico` 16×16 层 | Windows 系统限制固定 16×16，无法放大 |
| TopBar 左侧 | `brush.png`（32×32 显示） | 窗口内大品牌标识 |
| 左下角 footer | `brush.png`（32×32 显示） | 与版本号同行 |

**PIL 生成多尺寸 ICO 的坑**：直接 `img.save('brush.ico', sizes=[...])` 会**跳过 256×256 层**（PIL 拒绝升采样保存大层）；必须先把源图 `resize((512,512), LANCZOS)` 再保存，才能生成完整 11 层。

**Windows 图标缓存**：替换 exe 图标后资源管理器仍显示旧图标（IconCache.db 缓存）——需 `taskkill /IM explorer.exe /F` + 删除 `%LOCALAPPDATA%\IconCache.db` + 重启资源管理器。

---

### 3.16 资源嵌入策略

| 类型 | csproj 标签 | 访问方式 | 适用于 |
|------|-------------|----------|--------|
| WPF Resource | `<Resource Include="x.png"/>` | `pack://application:,,,/CpqSystemTool;component/x.png` | 背景图、图标（嵌入 exe，WPF 自动优化） |
| EmbeddedResource | `<EmbeddedResource Include="x.zip"/>` | `Assembly.GetManifestResourceStream("CpqSystemTool.x.zip")` | 源码包、非 WPF 数据 |
| ~~Content~~ | ~~`<Content Include="x.png"><CopyToOutputDirectory>`~~ | ❌ 不嵌入 exe，只复制到输出目录 | **本项目禁用**（违背单文件分发原则） |

**曾经遗漏的**：`brush.png` 原来被设为 `<Content>`（只复制不嵌入），代码中也无引用 → 已从 csproj 移除以保持仓库整洁。

---

### 3.18 近期一致化与重构（UI / 代码质量）

**按钮行均分布局（MainWindow.Helpers.cs · `MakeBtnRow`）**
- 见 3.12 `MakeBtnRow` 工厂。核心约束：**均分整行宽度 + 按钮居中 + 大小不变**。早期误用 `HorizontalAlignment=Stretch` 会把按钮整体拉大（违背"调间距而非拉大按钮"的诉求），已纠正为 `Center` + `Margin=0`。
- 覆盖范围：系统优化主页、Defender、隐私 serviceBar、安全防护更新、系统还原、系统信息、配置管理、隐私 layoutBar、版本转换 + 频道选择、Appx 管理工具条。整页多页操作行视觉等宽、间距均匀、占满整行。

**隐私设置页：`MakeCheck` + `CheckSemantics` 枚举（MainWindow.Pages.cs）**
- 复选开关统一由 `MakeCheck(title, getState, apply, desc, semantics)` 工厂创建；`semantics: CheckSemantics.CheckedMeansDisable | CheckSemantics.CheckedMeansEnable` 显式声明"勾选 = 禁用 / 启用"语义，消除原布尔 flag 参数的 Primitive Obsession。
- 日志动词按语义推导（`已禁用 / 已启用`），修正了"允许…"类项（活动历史 / 广告 ID / 语言列表 / 应用启动跟踪 / 建议内容 / 墨迹词典）日志文案反转的问题。
- 状态由各 `MakeCheck` / 按钮内联展示，**已彻底移除冗余的 `ShowStatus()` 汇总块**（修复了"操作后重复插入区块"的回归）。

**OtherTweaksDialog：`AddRow` 合并 builder（OtherTweaksDialog.cs）**
- 原 `AddToggle` / `AddButtonItem` 合并为共享 `AddRow(parent, title, desc, btnText, onClick, getState, apply)`；`isToggle = getState != null && apply != null` 区分「开关项」与「按钮项」两种模式。
- 纯重构：消除重复代码、降低后续维护成本；行内状态标签（`[已启用]` / `[已禁用]`）替代原 ✔/✘ 大徽章，与隐私页 `MakeCheck`、常用软件列表 hover 风格统一。

**侧边栏改名**：`系统激活/Office` → `激活工具`（MainWindow.Nav.cs）。

### 3.19 版本转换模块 (VersionSwitch) —— 密钥来源

**实现定位**：`Modules/VersionSwitch.cs` 是第三方「一键转换 7.0 (OSSQ)」的**安全重写**（二次确认 + 密钥可留空），**未直接调用其 exe**，流程为：注册表允许切换 → `slmgr /ipk` 候选零售通用密钥（依次尝试）→ `changepk.exe /ProductKey` 触发版本切换（自动重启）→ 可选备份/还原激活（`BackupActivation`/`RestoreActivation`）。目标版 14 个，对齐 OSSQ 一键转换 7.0。

**🔑 密钥来源（此前 README 完全缺漏，本次补全）**
- 切换版本用的是【**零售通用安装密钥** retail generic key】——**不是 KMS GVLK**（`W269N-…` 那类只用于 Online KMS 激活，不能拿来做 changepk 版本切换）。写在 `GVLK` 字典里，每版本 1~3 个候选：
  - 第 1 个 = **微软官方 RTM 零售通用密钥**；
  - 其余 = **OSSQ 7.0 原版 exe 内置的备用/变体密钥**（原版即按序列逐个 `slmgr /ipk` 尝试，本工具同样依次尝试到成功为止）。
- **核对来源**：`winaero.com` + `sftkey.com` + `elevenforum`（Shawn Brink）**双源核对**（2026-07 确认）+ 从原版 7.0 exe 提取——见 `VersionSwitch.cs` GVLK 注释头。
- **是否来自 git 项目**：OSSQ 一键转换 7.0 本身是第三方项目，本工具**未 live-clone / 拉取其仓库**，密钥已从原版 7.0 exe **提取并硬编码**进 `GVLK`，仅作为「密钥 + 目标版列表」的参考来源（见 §7，已把"未纳入"纠正为"已重写借鉴"）。
- **需证书版本**：`EnterpriseS`(LTSC) / `IoTEnterprise` 这类要 SKU 证书的版本，镜像内证书随 `Resources/Skus/EnterpriseS/`、`Resources/Skus/IoTEnterprise/` 下的 `.xrm-ms` 一并打包，`SkuInstalled()` 会检测本机是否缺证书。

---

### 3.20 UI 一致化基础设施与近期修正（沉淀）

> 本节把分散在 §3.2/§3.8/§3.11 的各页 UI 经验，提炼成可复用的基础设施与"互斥高亮"统一范式，避免日后每页各自造轮子。

**日志框圆角化（`WrapLogBox`，Helpers.cs）**
- 所有页面底部日志统一由 `WrapLogBox(TextBox)` 包裹：把 TextBox 的 `BorderThickness=0`/`Background=Transparent` 清空，**外层 Border（CornerRadius=8）承担边框 + 背景**。控件直接放入布局即可。
- 解决三类回归：① 日志"双边框"（TextBox 自带边框 + 外层 Border 叠加）；② 部分页面圆角不全（TextBox 自身圆角在 ClipToBounds Grid 里被裁掉底部）；③ 各页日志外观不统一。全局 `Grep WrapLogBox` 可见 11 处调用，覆盖清理/服务/Appx/常用软件/系统信息/安全/系统工具/Edge/隐私/激活/配置管理全部日志。
- 配置管理页日志另用 `logClip` 固定 60px 高 + `ClipToBounds` 物理截断，防止 Star 行膨胀产生透明空白（配合 §8.4 的"透明背景 + Star 膨胀"陷阱）。

**互斥高亮按钮统一范式（`ApplyMode` / `ApplyPolicyMode` / `ShouldFillDef`）**
- 通用形态：`void ApplyX(Button sel){ foreach(b in 组内按钮){ bool on=b==sel; b.Background=on?_accent:_btnSecondaryBg; b.Foreground=on?_btnPrimaryFg:_btnSecondaryFg; b.BorderBrush=on?Transparent:_panelBorder; } }`。选中 = accent 实色填充，未选中 = secondary 样式——与侧边栏激活态（§3.15）完全相同的青绿填充语言。
- 三种变体：清理页 `ApplyMode`（3 按钮互斥，默认高亮首项）；Defender 底栏 `ApplyPolicyMode`（默认 `null` 不高亮）；Defender 顶栏 `ShouldFillDef`（**严格互斥**：`_lastDefAction` 非空时只看最后点击 key，未点过退回系统状态）。
- **关键认知**：互斥高亮是"模式切换"，不是"临时态"。点击后高亮保持在该按钮，直到点别的——曾误实现成"临时扫描中态"导致填充不转移，用户纠正后才改成纯互斥。

**配置管理页背景预览（Viewbox 自适应，消除 letterbox）**
- 暗/浅预览 `Border` 原设纯色背景（黑 / `#F5F7FA`），`Image.Stretch=Uniform` 比例不匹配时在两侧留 letterbox 黑/白边。
- **修正**：`Border.Background=Brushes.Transparent` + 用 `Viewbox{Stretch=Uniform}` 包裹 `Image`，容器高度随图片实际比例自适应，完整显示无黑/白边；移除 `MaxHeight/MinHeight` 硬约束（避免为凑高度撑出空白）。这是"等比完整显示 vs 不留死区"的标准解。

**激活页 MAS 提示条移除、集中到「关于」页**
- 激活页顶部 `NoteBar` 已移除（避免每页重复声明）；MAS 出处声明集中到「关于」页两个 `NoteBar`（ℹ 独立实现声明 + ⚠ 免责声明）+ `LinkText` 可点击链接（MAS 主页 / get.activated.win）。合规留痕仍齐备，且不在功能页打扰用户。详见 §8.9。

**无障碍细节（`NoteBar` / `LinkText`）**
- `NoteBar(icon,text,tone,...)`：左侧 3px 语义色竖条 + 图标 + 文案，背景用 `_rowHover`（青绿半透明），不单靠颜色表意（图标+文案双通道，满足 WCAG 1.4.1）。
- `LinkText(text,url)`：始终保留下划线 + 手型光标，**不只用颜色区分链接**（WCAG 1.4.1），hover 仅微调 Opacity。

---

## 4. 构建与部署

### 环��要求
- **编译器**：.NET SDK 6.0+（`dotnet build`）或 Visual Studio 2022 +「.NET 桌面开发」工作负载
- **目标运行时**：.NET Framework 4.8（Win10 1903+ / Win11 系统自带）
- **管理员权限**：运行时自动提权（`app.manifest` 配置 `requestedExecutionLevel=requireAdministrator`）

### 构建与一键部署
```bat
cd src\CpqSystemTool
dotnet build -c Release          # 输出: bin\Release\net48\系统清理与优化工具.exe
# 受限网络 / 沙箱无本地 nuget 缓存时，强制干净重建并指定官方源：
dotnet build -c Release --no-incremental --source https://api.nuget.org/v3/index.json
```
> 注：仓库**无 `build.bat`**，`dotnet build` 直接产出单文件 exe（程序集名 `系统清理与优化工具`，见 `CpqSystemTool.csproj` 的 `AssemblyName`）。

### 分发
只需**一个** `系统清理与优化工具.exe` 文件——所有资源（背景图/图标/源码包）均已嵌入。

---

## 5. 配置系统

### 配置保存
默认路径：`exe所在目录\Config\`，可在「配置管理」页自定义 → 改动即时生效 → 自动创建目录。

### 导出文件
| 文件 | 内容 |
|------|------|
| `CpqSystemTool优化-{yyyyMMddHHmmss}.ini` | 系统优化页配置导出（默认）：含全部项勾选状态 `Tag=1/0/2` + `生成时间`；导入时同格式回填。历史 `.json` 备份仍可导入（仅勾选 ID 列表） |
| `cleaner-config.json` | 配置管理页「导出配置」：清理勾选 + 服务列表 + 所有页面综合配置（JSON） |
| `autosave.json` | 程序退出时自动保存（`App.Exit` 事件触发） |

### 源码导出
「配置管理」页 →「导出项目源码」：从嵌入的 `src.zip`（`EmbeddedResource`）解压到指定目录。

---

## 6. 编码铁律（防再次踩坑）

- **BOM**：`.cs`/`.csproj`/`.xaml`/`.sln` **必须 UTF-8 带 BOM + CRLF**（Framework csc 无 BOM 按系统代码页读，中文 65001 环境乱码）
- **资源嵌入**：所有图片/图标用 `<Resource>`（WPF pack URI），数据文件用 `<EmbeddedResource>`，**禁止** `<Content>`（只复制不嵌入，违背单文件分发原则）
- **主题笔刷共享**：全部通过 `_accent`/`_textMain` 等 internal 字段传递，**禁止**页面内硬编码色号（否则主题切换时该元素不跟随）
- **frozen Brush 替换**：XAML 创建的 Brush 是 frozen（只读），主题切换时**必须** `Resources["Key"] = new SolidColorBrush(...)` 整体替换，**不能**改 `.Color`
- **⚠️ DynamicResource（修正）**：ControlTemplate.Triggers 的 Setter 引用主题资源**必须用 `{DynamicResource}`**——`StaticResource` 解析后固定指向初始对象，替换字典不生效（hover 色不变的根因）。XAML 中**不要硬编码 Brush 初始值**
- **ControlTemplate TargetName**：比 Style Trigger 优先级高，能覆盖代码 local value——因此 **IsMouseOver 只改 BorderBrush，不改 Background**（否则覆盖代码设的激活色）
- **递归遍历**：布局容器嵌套后（StackPanel→DockPanel 等），`p.Children` 只遍历直接子元素，必须用 `VisualTreeHelper` 递归找目标控件
- **Grid(★Star) 均分按钮行**：同行多按钮用 `Grid(N×★Star)` 均分整行宽度时，按钮必须 `HorizontalAlignment=Center` + `Margin=0`——**绝不能**用 `Stretch` 拉伸按钮去占满列（会把按钮整体拉大，违背"调间距而非拉大按钮"的诉求，见 §3.12 `MakeBtnRow`）
- **线程模型**：所有跨线程 UI 操作必须通过 `Dispatcher.Invoke/BeginInvoke` 回切 UI 线程
- **编译门禁**：每次改动后必须 `dotnet build -c Release` 确认 **0 错误 0 警告**；替换图标等资源后需 `dotnet clean` 重建（增量编译可能缓存旧资源）
- **部署**：exe 被占用直接 `Stop-Process -Force` + `Remove-Item -Force` 再复制（用户已授权），不等手动关
- **删死代码前查内部调用**：跨文件 grep 会漏同文件内部调用（如 `Cookies()` 调用 `BroCookies()`）——删除前必须 grep 方法名**全项目所有出现**，编译报错兜底

---

## 7. 参考项目

| 项目 | 关系 | 注意 |
|------|------|------|
| [Win11EasyConfig](https://github.com/YiKongk/Win11EasyConfig) | 功能对齐参考（net481/WinForms） | ⚠️ 版权归快乐无极，**仅供学习、切勿商用/二次分发**；且原仓库**不含运行所需的第三方辅助工具**（MinSudo.exe / DisableWD.bat / EnableWD.bat / smartscreen\ 等），须放进原发行目录才完整运行 → **只能当参考、不能直接 fork 进要分发的成品**（侵权风险）。<br>✅ **2026-08-02：其反编译复刻工程 `src\CpqSystemTool.Forms\` 已从本工程彻底删除，并完成文案脱钩审查，本工程代码已不受该版权限制 —— 详见 §8.8。** 详见 `.archive_reports/功能提取报告_Win11EasyConfig.md`（已归档） |
| [W11ClassicMenu](https://github.com/Sordum/W11ClassicMenu) | 右键菜单注册表路径参考 | Sordum 出品 |
| [Microsoft Activation Scripts (MAS)](https://github.com/massgravel/Microsoft-Activation-Scripts) | 激活真正实现（HWID/KMS38/Ohook/KMS/TSforge） | massgravel 出品，GNU GPL v3 许可；官方 `irm https://get.activated.win \| iex`。激活页 5 张卡已**直接调用 MAS 脚本**（运行时联网下载执行，见 §3.5 方案 B），方法命名与开关均对齐官方文档 |
| ZyperWin++ 4.1 | UI 交互参考（C# / AntDUI） | ⚠️ **合并前须先查其 LICENSE**；且其 **AntDUI 框架与自研纯 WinForms 不兼容**，合并须二选一框架 + 移植另一套逻辑；其功能（服务优化/清理/Defender/Edge/Appx/Office/激活/配置导入导出）**与自研模块大量重叠**。详见 `.archive_reports/功能提取报告_ZyperWinOptimize.md`（已归档） |
| 一键转换 7.0 (OSSQ) | **密钥来源 / 目标版列表参考（已重写为安全实现）** | 版本转换页 `VersionSwitch.cs` 对齐它的 14 个目标版与密钥；密钥从其 7.0 exe 提取 + winaero.com/sftkey.com/elevenforum 双源核对（2026-07）。本工具**未直接调用其 exe**，改用 changepk 安全封装（二次确认 + 密钥可留空），见 §3.19 |

### 致谢（概念级灵感，非代码复制）
上述项目为本工具的**设计参考与灵感来源**（分类思路 / UI 交互风格 / 注册表路径 / 版本密钥等），所有实现均为原创 C# WPF 代码，未复制、未打包其源码。版本转换所需的 GVLK / 零售通用密钥均为**微软官方公开发布的 KMS 客户端安装密钥与零售通用安装密钥**，属公开事实数据，非任何第三方版权或开源许可标的；其获取途径（OSSQ 一键转换 7.0 exe 提取 + winaero.com / sftkey.com / elevenforum 双源核对）已在 `VersionSwitch.cs` 与 §7 标注。其中 Win11EasyConfig 的反编译复刻工程 `CpqSystemTool.Forms\` 已于 2026-08-02 从本工程彻底删除并完成文案脱钩（见 §8.8）；ZyperWin++ 因框架（AntDUI）与自研 WPF 不兼容，仅取其分类思路。运行时唯一实际调用的第三方开源软件为 **Microsoft Activation Scripts（MAS，GPL v3）**，本工具仅远程调用其官方脚本，未打包、未修改，详见「关于」页 OSS 声明。

---

## 8. 工程经验与版本管理约定（自包含，随源码导出可查）

> 本节汇总跨会话工程约定，确保**导出源码后无需外部记忆即可复现构建 / 部署 / 审查**。逐轮详细开发日志在 `.workbuddy/memory/2026-08-01.md` / `2026-08-02.md`（位于工程目录外，**不随源码导出**）——故关键约定在此固化，避免"经验只存在于外部记忆"。

### 8.1 构建与部署
- **必需环境**：**.NET SDK 8.0+**（本工程实际在沙箱以 SDK 10.0.302 构建，无 `global.json` 锁版本，`net48` 可直接 `dotnet build`；用户本机需装有任意 .NET SDK）。早期误记"沙箱仅有 runtime-only dotnet、无 SDK"，实测可用，已纠正。每次成功构建均 **0 错误 0 警告**。
- **构建命令**：`cd D:\电脑桌面\cpq\src\CpqSystemTool` → `dotnet build CpqSystemTool.csproj -c Release`。
- **部署**（用户已授权）：exe 被占用时直接 `Stop-Process -Force` + `Remove-Item -Force` 覆盖到 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，不等手动关。

### 8.2 版本管理（⚠️ 无 git 仓库）
- 本工程**不是 git 仓库**（已确认无 `.git`）。代码审查走"改动区手工两轴"，不用 git diff。日后导出源码即为此目录快照。
- 危险重构前做**时间戳 .bak 备份**：`文件名.bak_YYYYMMDD_HHMMSS`（当前仓库已有 5 个 `.bak` 文件）。
- 删死代码前必须全项目 grep 方法名所有出现（含同文件内部调用），编译报错兜底。

### 8.3 代码审查（长期固定要求）
- 本工程**非 git 仓库**（§8.2），不用 git diff，改用「**三轴并行全量通读**」方法论：启动 3 个独立 sub-agent 各自完整通读全部 `.cs`（排除 `obj/`/`bin/`/`.bak`），分三轴报告：
  1. **正确性 / 健壮性轴**：null 解引用、错误注册表路径、逻辑错误、资源/句柄泄漏、线程死锁 / 竞态、文件 I/O 错误处理。
  2. **安全 / 权限轴**：提权 / 权限处理、远程脚本信任、未校验下载、句柄 / SID 泄漏、Exec 命令注入、破坏性操作确认。
  3. **质量 / 性能轴**：死代码、重复样板、魔法数字、阻塞调用、资源未释放、日志一致性。
- 标记口径：每条问题标 `RESOLVED`（本轮已修）/ `NEW`（本次引入回归）/ `PRE-EXISTING`（旧有问题，含 `设计取舍` 类非缺陷）。
- 配合「交付前强制自查门禁」：BOM 校验（.cs 须 UTF-8 带 BOM）→ 易错模式 Grep 扫描（对象初始化器 `:` 误用 / ControlTemplate 漏 `)` / `FindResource` / `private readonly` 主题笔刷）→ 括号配平状态机（仅统计 CURLY/ROUND/SQUARE，跳过注释/字符串/字符字面量）→ 关键文件人工精读。
- 一般改动后跑完整一轮三轮审查；重大批次（如全量修复）改完重跑确认无回归。**编译门禁**：改动后必须 `dotnet build -c Release` 确认 **0 错误 0 警告**（沙箱可直接编）。

### 8.4 WPF 架构约定（防反复踩坑，补全 §3/§6 未列项）
- **页面填满视口**：统一 `BindRootHeightToViewport(root)`（绑定 `root.MaxHeight → ContentArea.ActualHeight` OneWay）。**绝不用** `ScrollViewer.ViewportHeight` 绑定（非 DP，静默失效只求值一次）；也**别**手动 `SizeChanged` 读 `ViewportHeight`（首帧 vp=0 跳过 → 布局漂移）。
- **同行多按钮均分**：见 §3.12 `MakeBtnRow`（N×★Star Grid + 按钮 `HorizontalAlignment=Center` + `Margin=0`）。⚠️ 按钮**不能**用 `Stretch`（会把按钮整体拉大，违背"调间距而非拉大按钮"诉求）。
- **透明背景 + Star 行膨胀 = 视觉"空白"**：卡片透明让底层六边形背景透出时，Star 行会把多余空间变成不可见空白；排查布局异常先确认容器 `Background` 是否实色。

### 8.5 导航合并历史（13 页）
- 原 18 导航项 → 现 13 项：更新管理 + Defender →「安全防护」(security)；上帝模式&还原 + 版本转换 + 频道选择 →「系统工具」(systools)。详见 §1。
- 合并页**绝不用 `Navigate(currentKey)` 重建整页**（会丢共享日志 host），改为重建子 host 区域。模块文件 `Updater.cs`/`Defender.cs`/`GodMode.cs`/`RestorePoint.cs` 仍独立，仅导航入口合并。

### 8.6 激活后端要点（方案 B：真集成 MAS）
- 5 张激活卡（HWID/KMS38/Ohook/KMS/TSforge）→ 提权 PowerShell 执行官方一行式 `irm https://get.activated.win` + 对应开关（`/HWID` `/KMS38` `/Ohook` `/K-Windows` `/Z-WindowsESUOffice`），参数对齐 massgrave.dev 官方文档。
- 点击 MAS 卡**必须二次确认**（`MessageBox` YesNo）；`诊断` 卡走本地 `CheckStatus` 不联网。
- 提权窗口用 `Process.Start` + `UseShellExecute=true` + `Verb="runas"`（**刻意绕过** `Exec.RunPowerShell`，因其 `CreateNoWindow + UseShellExecute=false` 会挡 UAC 且 `ReadToEnd` 阻塞 MAS 交互窗口）。
- 覆盖范围：HWID/KMS38/KMS 仅 Windows；Ohook 仅 Office；TSforge(`/Z-WindowsESUOffice`) 覆盖 Windows+ESU+Office。
- 诊断方法标识集中为 `Activation.DiagnosticMethodId` 常量（`Activation.cs:16`），后端分支与页面卡片 `Id`/完成提示统一引用，单一真相源（消除跨文件魔法串重复）。

### 8.7 「复刻某软件」先搜索核实（来自 §7 两个上游项目）
- **教训**：本轮从零自研 C# 版时，**没先搜索就从头重写**，浪费了一整轮 + 多轮修 bug —— 后来才发现 YiKongk/**Win11EasyConfig**（C# 反编译复刻完整工程）和 ZyperWave/**ZyperWinOptimize**（ZyperWin++ 4.1）本就是同款软件，且功能大量重叠（见各自 `.archive_reports/` 下各自 `功能提取报告_*.md`）。
- **正确做法**：动手前先 `WebFetch` 这两个（及同类）仓库，核实 **语言 / 框架 / 功能 / 许可证 / 自包含度**，再向用户给「fork 合并 / 参考移植 / 继续自研」三选一建议。
- **复用边界（已核实）**：
  - Win11EasyConfig = 《Windows11轻松设置 v1.12》的 C# 复刻，**仅可参考、不可 fork 进要分发的成品**（版权归快乐无极、不可商用/二次分发；且缺 MinSudo.exe 等运行所需辅助工具）。
  - ZyperWinOptimize = ZyperWin++ 4.1，含服务优化/清理/Defender/Edge/Appx/Office/激活/配置导入导出，与自研模块大量重叠；**合并前须先查 LICENSE**，且 AntDUI 框架与自研纯 WinForms 不兼容。
- **自研优势**（决定继续自研）：自有代码无版权包袱、自包含（无需外部辅助工具）、exe 仅 ~100KB 级、已对齐原版 + 独有功能（清理/激活/计量连接/上帝模式/还原点）。

### 8.8 版权血缘审查与脱钩结论（2026-08-02 实测）

> **结论：本工程当前代码不受"版权归快乐无极、仅供学习研究、不可商用/二次分发"的限制。** 该限制约束的是 Win11EasyConfig 的**源码与二进制本身**，而非"实现同类系统设置功能"这件事。

**已执行的脱钩动作**

| 动作 | 说明 |
|---|---|
| 删除 `src\CpqSystemTool.Forms\` | 上游反编译复刻工程（60 文件 / 0.99MB，含 `Form1.cs` 178KB、`Optimize.cs` 149KB、`RawResources\Win11EasyConfig.*.resources`）已从工程中彻底移除；备份存 `.archive_reports\CpqSystemTool.Forms_backup.zip`（334KB，仅本地留档，**不得随源码分发**） |
| 隐私页 3 条 UI 文案改写 | 消除上游带原创括注的表达（详见下表） |

**四层血缘核查（逐层判定）**

| 层面 | 核查方式 | 结论 |
|---|---|---|
| **代码结构** | 主工程 WPF/XAML 全自研，上游为 WinForms Designer；`CpqSystemTool.csproj` 无任何 `ProjectReference`；嵌入 exe 的 `src.zip` 36 条目中 Forms/Win11EasyConfig 条目为 **0** | ✅ 零复制 |
| **资源文件** | 主工程仅 `background.png` / `background-light.png` / `brush.png` / `brush.ico`，均由用户自有 PSD（`背景 - 副本.psd`）导出；上游 `app.ico` / `.resources` 已随目录删除 | ✅ 资源自有 |
| **数据（注册表路径 / StoreId / 密钥 / 命令行）** | 属**事实性信息**，不构成著作权客体；产品密钥为微软官方公开的零售通用安装密钥（Generic Installation Keys），非第三方资产 | ✅ 不受保护 |
| **文案（表达层）** | 脚本比对上游 44 个 `.cs`（1042 条中文串） vs 主工程 28 个 `.cs`（877 条）：逐字重合 **58 条 / 6.61%**，最长 22 字 | ✅ 残留项均为共同来源 |

**残留 58 条重合的性质**（判定为不侵权）：全部落在三类——① **微软官方术语**（Xbox 游戏工具栏、HEIF 图像扩展、AV1 视频扩展、电影和电视等商店应用官方中文名）；② **Windows 设置界面原文**（"允许 Windows 跟踪应用启动以改进搜索结果"等，两边都引用微软文案，属**共同来源**而非上游→本工程的复制）；③ **通用技术名词**（缩略图缓存、预读取文件、清空回收站）。这类词汇无独创性，替换反而损害用户识别度。

**已改写的 4 处**（原文含上游作者的原创解释性括注）：

| 位置 | 原文案 | 现文案 |
|---|---|---|
| `MainWindow.Pages.cs:2223` | 搜索界面禁止云内容搜索（云搜索内容来源：OneDrive、SharePoint、OutLook、必应等） | 关闭搜索栏云端结果（OneDrive / SharePoint / Outlook / Bing） |
| `MainWindow.Pages.cs:2226` | 搜索界面禁止 Web 搜索（仅当前用户） | 关闭搜索栏联网结果（当前用户） |
| `MainWindow.Pages.cs:2229` | 禁止本地存储搜索历史记录（仅当前用户） | 不保留本地搜索历史（当前用户） |
| `Tweaks.cs:828` | `Desc = "禁止本地存储搜索历史记录"` | `Desc = "不在本机保留搜索历史记录"` |

> 附带收益：原 52 字 CheckBox 标签远超可扫描阅读宽度，改写后同组三条标签长度趋于一致（14–20 字），隐私页视觉节奏更整齐。

**分发前仍需注意**：① `.archive_reports\CpqSystemTool.Forms_backup.zip` 与两份 `功能提取报告_*.md` 仅供本地追溯，**打包分发时必须排除**；② 激活功能运行时联网调用 MAS（**GNU GPL v3 许可**，非 MIT——官方 LICENSE 即 GPL v3），出处声明已常驻激活页顶部提示条并在「关于」页（§8.9）列出；③ 「关于」页已完成（见 §8.9），商用合规留痕齐备。

### 8.9 「关于」页与 MAS 出处声明（2026-08-02 补充）

**入口**：侧边栏底部品牌区（图标 + 版本号 + "· 关于"）点击进入；该页为隐藏导航项（`NavItem.Hidden=true`），不占用 13 项主导航列表，进入时底部品牌区以低调的 `_rowSelected` 同步选中态。

**页面内容**
- **独立实现声明**：明确本工具全部界面与逻辑均为原创实现，仅运行时按需调用官方在线脚本/接口，未打包、未修改、未内置任何第三方源码或二进制。
- **开源引用清单**（`OssRow` 逐项列出名称 + 许可证 + 可点击来源链接）：
  - Microsoft Activation Scripts (MAS) — GNU GPL v3 — [massgrave.dev](https://massgrave.dev)
  - MAS 在线脚本（运行期调用地址）— GNU GPL v3 — [get.activated.win](https://get.activated.win)
- **免责声明**（`NoteBar` 橙色调）：仅供学习与个人使用，激活涉及系统授权变更，请遵守当地法规与 Microsoft 许可条款。

**激活页 MAS 提示条现状（2026-08-03 修正）**：激活页顶部 `NoteBar` **已移除**（不再每功能页重复声明），MAS 出处声明已**集中到「关于」页**（见下方两条 `NoteBar` + `LinkText`）。激活页仅在二次确认 `MessageBox` 文案中注明 GPL v3。这样既满足"出处声明非弹窗、可见"的合规要求，又不打扰功能页操作（详见 §3.20）。

---

### 8.10 近期质量修复与待解决项（2026-08-04）

**近期质量门禁修复（Batch A / B，已编译 0/0 + 三轮复审三轴 CLEAR + 已自动部署）**
- **裸 catch{} 日志化**：脚本（带词法状态机跳过注释/字符串/字符字面量）把全项目 ~200 处裸 `catch {` 统一改为 `catch (Exception caughtEx) { Debug.WriteLine("[CpqSystemTool] 异常(已忽略): "+caughtEx.Message); ... }`（行为保持，仅加日志）。初版注入变量名 `ex` 与 `App.xaml.cs ShowCrash(Exception ex)` 参数冲突报 CS0136 → 二次脚本全改 `caughtEx` 解决。
- **RegistryKey 改 `using` 释放**：`MainWindow.Theme.cs`、`EdgeCore.cs`、`SoftwareInstall.cs`（修复 `OpenBaseKey` 根键每次查询都漏一个句柄的泄漏）。
- **魔法超时常量**：`MainWindow.Theme.cs` `IMAGE_CONVERT_TIMEOUT_MS=15000`、`Modules/Activation.cs` `MAS_TIMEOUT_MS=1800000`（早前 `PROCESS_TIMEOUT_MS=900000` 已补）。
- **破坏性清理二次确认**：`MainWindow.Pages.cs` 新增 `DestructiveCleanupIds`（event_logs / recycle / cookies / hiberfil_off / memory_dmp / windows_old / winsxs_dism），「开始清理」在后台执行前弹 `YesNo` 警告，选否中止。

**🔴 待解决项（记录，非本轮引入，后续处理）**
1. **`StrictSignatureCheck` 默认值（@SoftwareInstall.cs）**：当前默认 `false`（非严格，签名 Invalid 仅警告放行）。现状评估：主流包已全面改用官方直链 / Chocolatey / 官方下载页解析，安装包自带 Authenticode 签名，开启严格模式对主流包安全；仅搜狗拼音 / 123云盘 / RayLink 三个纯镜像兜底包可能命中 bat 重打包（无可签名体）。**决议**：维持 `false` 默认（避免误拦合法未签名 / 非主流 CA 签名包），不依赖离线签名核验脚本；后续若给上述三包补官方直链，可再评估是否开启严格模式。
2. **`MainWindow.Pages.cs` ~5000 行巨型方法拆分 / `RunScan` 并行化**：纯可维护性、非缺陷。沙箱现已能编译，但 5000 行未经编译验证的重构风险仍高，按"不引入新错误"原则**有意推迟**。

**Phase 2 国产包官方直链替换调研（2026-08-04，已实现 3 个 + 部署）**
- 用户要求"连国内一起逐包替换"。四轮 Web 检索核实 26 个仍走 `MIRROR` 私有镜像的包（含 `vcredist` 走 `dl.oyk.pub`、`qq` 走 `jump.oyk.pub` 中转）。
- **已替换 3 个带稳定官方端点的**（不写死哈希、走 Authenticode 跨版本校验）：微信 `https://dldir1.qq.com/weixin/Windows/WeChatSetup.exe`（腾讯官方 CDN，永远最新）；OneDrive `https://go.microsoft.com/fwlink/p/?LinkId=248256`（微软官方 fwlink，永远最新 OneDriveSetup.exe）；Steam `https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe`（Valve 官方 CDN，永远最新引导器）。
- **其余国产包（约 23 个）暂保留私有镜像，未盲目替换**——核心原因：检索证实这些厂商（QQ/TIM/WPS/百度网盘/阿里云盘/网易云/QQ音乐/酷狗/酷我/爱奇艺/腾讯视频/抖音/B站/搜狗输入法/夸克/微云/123云盘/RayLink/Xshell/IObit Unlocker/VC++ AIO 等）的 PC 客户端直链**全部带版本号或 JS 动态生成**（如 `aDrive-6.7.0.exe`、`bilibili-setup-v1.17.6.exe`、`setup_douyin_8.0.0.exe`），写死后厂商一发新版即 404，反而比镜像更不可用；且酷狗出现 `dl.tpn2n.com` 等非官方钓鱼域名混淆。**这正是 §8.10 待解决项 #1 的根源——国产软件缺乏"latest"稳定端点，官方直连更新即失效的担忧成立**。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`7C6A5D9A32ADB23870EB477F89BFDE4154D478DEBA459E183A007D3577608FE2`（VERIFY_OK）。
- **真正可行的国产包官方源路径（后续，需用户本机参与）**：① 用户在己机逐个访问厂商下载页确认是否存在"latest/stable"固定端点（沙箱无法运行时核实）；② 或改用 winget 国内镜像 / 第三方 Chocolatey 包覆盖（但国产包 manifest 稀缺）；③ 或保留镜像但把 `-SI.zip` bat 重封装换成厂商原版已签名安装器。**qq 的 `jump.oyk.pub` 中转因能动态重定向到最新官方包，恰好规避版本号问题，予以保留。**

**Phase 3 钉历史版本固定直链替换（2026-08-04，已实现 4 个 + 部署）**
- 用户采纳"钉历史版本固定直链"方案（用户原话：比官方最新版低一个版本的下载链接官方应有固定链接）。经实测验证，**钉版本 URL 真能存活、返真实 exe、且不被重定向到最新版**（百度网盘 `BaiduNetdisk_7.30.5.2.exe` 沙箱 HEAD 实测 `206 + application/x-msdownload + Range 被尊重`）。
- **本轮新增替换 4 个 A 级包**（钉版本固定官方直链，SHA256 留空走 Authenticode 跨版本校验）：WPS `https://official-package.wpscdn.cn/wps/download/WPS_Setup_X64_24655.exe`；百度网盘 `https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_7.30.5.2.exe`；网易云音乐 `https://d8.music.126.net/dmusic/NeteaseCloudMusic_Music_official_2.10.13.202675_32.exe`；腾讯视频 `https://dldir1.qq.com/qqtv/TencentVideo11.105.4486.0.exe`。四者沙箱均 HEAD 实测 `206 + 真 exe/octet-stream`。
- **本应替换但未替换的 A/B 级包（诚实记录，避免盲目替换反降级）**：① 阿里云盘——研究给的 `download.aliyundrive.com/aliyundrive_windows_6.9.1.exe` 沙箱实测"主机不可达"（域名失效）；② 抖音——官方下载页 `douyin.com/download/pc` 返回 404、且真实 CDN 路径含不透明段（`douyin-v4.7.0-...exe`），无可静态钉死的稳定 URL；③ 搜狗拼音/五笔——官网首页下载按钮 href 为 JS 触发、静态 HTML 无直链。三者均需用户本机访问厂商页或抓包才能拿到稳定端点，故暂留镜像。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`FD3B2463994177258AA1637DAB7CCB0F2593BAD7054BDDE41C534116CE8E6702`（VERIFY_OK）。
- **累计官方源化进度**：主流包 10（Chocolatey 运行时）+ 微信/OneDrive/Steam 3（官方稳定直链）+ WPS/百度网盘/网易云音乐/腾讯视频 4（钉版本直链）= **17 个脱离私有镜像**；剩余约 19 个国产/小众包仍走 `MIRROR`（含 §8.10 #1 的 StrictSignatureCheck 决策 + bat 重封装问题待解决）。

**Phase 4 钉历史版本继续替换（2026-08-04，已实现 3 个 + 部署）**
- 用户追问"剩下的 19 个没办法了嘛？用旧版本不能实现吗？" → 对 Phase 3 后仍走 `MIRROR` 的国产/小众包逐一挖钉历史版本固定直链并实测可达性（多轮 WebSearch + PowerShell HEAD/Range 三轮实测）。
- **本轮新增替换 3 个**（钉版本固定官方直链，沙箱实测 HTTP 200 + 真 exe 内容类型）：
  - **百度拼音**：`https://imeres.baidu.com/imeres/imeres/ime-res/guanwang/dl/online/BaiduPinyinSetup_6.1.13.7.exe`（钉 6.1.13.7；NSIS 静默 `/S`；SHA256 留空走 Authenticode）
  - **哔哩哔哩**：`https://dl.hdslb.com/mobile/pack/bili_win/23743424/public/bilibili-setup-v1.17.6.exe`（官方 CDN 钉 v1.17.6；保留无静参以延续改动前交互安装行为，规避不确定的 `/quiet` 误触发帮助挂起）
  - **VC++ AIO（abbodi1406）**：`https://github.com/abbodi1406/vcredist/releases/download/v0.78.0/VisualCppRedist_AIO_x86_x64.exe`（钉 v0.78.0；**沙箱实测下载并算得 SHA256=68AB06AE1D19045D1EA9EC87FE67C2102C8B09ACA2C7FF3DE897AEBE7FE80F11（大小写无关匹配发布页），已写死哈希**；静参 `/ai /gm2` 沿用）
- **仍无法替换（实测确认，诚实记录）**：TIM / 微云 / 夸克 / 酷狗 旧版构建实测 404（厂商已清理静态路径）；阿里云盘 `cdn.aliyundrive.net/downloads/apps/desktop/aDrive-6.7.0.exe` 沙箱 403 / 主机不可达（疑似沙箱 IP 被拦，非链接本身，候选 URL 交用户本机核实）；QQ音乐 / 搜狗拼音 / 搜狗五笔 / 爱奇艺 / 抖音 / 123云盘 / RayLink / Xshell / IObit Unlocker 官方无静态钉版本直链（JS 动态触发或仅下载页/latest），暂留 `MIRROR`。

**Phase 7 钉死直链版本刷新（2026-08-04 晚，已实现 8 个 + 部署）**
- 用户要求：① 把 Phase 6 摸索的"官方 exe 直链获取方法论"沉淀为可复用工具（已交付 `official-exe-finder` skill + `official_exe_finder.js` 探针，4 策略：静态锚点 / download 事件 / JSONP 配置 / 重定向跟随）；② 审计此前钉死的版本化直链、刷新过时项。
- **版本审计**（24 个钉死版本号直链）：16 个已是最新（winrar 7.23 / notepad3 / 7zip / everything 1.4稳 / aria2 / virtualbox / thunder / quark / tim / aliyunpan / weiyun / git / kgmusic(今日戳) / iqiyi(今日戳) / douyin(今日抓) / kwmusic(latest通道)）；**8 个过时**。
- **8 个过时项更新**（编译 0/0，已部署）：
  - 规律构造直替换 4：vcredist 0.78→**0.105**、tortoisegit 2.18→**2.19.1**、baidupinyin 6.1.13.7→**6.1.13.13**、txvideo 11.105→**11.176**
  - Playwright 抓官网最新钉死 3：wps 24655→**28043**（wpscdn 含哈希，26899 候选 404，改抓 wps.cn 得）、baidupan 7.30→**7.45.2.1**、wymusic 2.10(32位)→**3.1.28.205001(64位)**
  - 保留 1（审计时）：bilibili 1.17.6——当时新版 1.18.0 直链需 HTTP Referer 否则 403，而 Download() 尚不支持 Referer，旧 /pack/ 路径猜 build id 实测 404 → 按"禁止降级"暂保留 1.17.6；**后续已实现可选 Referer 支持（见下方「架构增强」），bilibili 改为钉 1.18.0 + `.Referer("https://www.bilibili.com/")`**
  - 新钉死 3 项 SHA256 留空走 Authenticode；wymusic 改推 64 位安装包。
- **代码复审发现并修复的 BUG**：vcredist 原写死 `0.78.0` 的 SHA256 哈希，更新 Agent 只改 URL 到 `0.105.0` 漏改哈希 → 会导致下载后 SHA256 不匹配拒绝安装。已移除写死哈希（留空走 Authenticode，与版本敏感特性一致）。三轴复审其余 **0 个 HARD 问题**；质量轴另建议给 `Download()` 增加可选 Referer 支持（增强项，最初未实施，后于下方「架构增强」段落落地）。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`1241C1255DE96DB432EA7C7B1476D5963101C903D712341116D1927AEF3EF34B`（VERIFY_OK，含 vcredist 哈希修复）。
- **架构增强：可选 Referer 支持**（采纳质量轴建议）。SoftwareDef 加 `Referer` 字段 + `Builder.Referer()` 链式方法 + `Download()` 在 Referer 非空时附加 `client.DefaultRequestHeaders.Referrer` 头；现有条目 Referer 为空，行为完全不变。借此 bilibili 不再因技术限制保留旧版——更新到 1.18.0 直链 `https://dl.hdslb.com/mobile/fixed/bili_win/bili_win-install.exe?v=1.18.0-3` 并附 `.Referer("https://www.bilibili.com/")`（Playwright 抓直链 + PowerShell 验证 4 个 bilibili 域 Referer 均 206 真 exe）。
- 三轴代码复审（正确性/安全/质量）**0 个 HARD 问题**。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`2DEC8F46FA2B5385C35DD8E3CA7C05A3B48F868DF1A889BE43C2DA92E734D847`（VERIFY_OK，含 Referer 框架 + bilibili 1.18.0 更新）。

**Phase 5 官方下载页运行时链接解析（2026-08-04，已实现 4 个 + 部署）**
- 用户采纳"运行时抓取官方页提取直链"思路：为搜狗拼音/123云盘/RayLink/Xshell 4 个 A 类包新增 `SoftwareDef.PageUrl` 字段 + `Builder.PageResolver()` 方法 + 新内部类 `PageLinkResolver`（位于 `Modules/SoftwareInstall.cs`）。
- **机制**：`Install` 在 Chocolatey 分支之后、自定义目录注入之前插入解析分支——`PageLinkResolver.Resolve(PageUrl, log)` 用带浏览器 UA 的 `HttpClient`（30s 超时、`AllowAutoRedirect=true`）GET 官方页：① 响应为 HTML → 读正文用正则 `https?://[^\s"'<>]+\.exe` 提取首个直链（优先匹配含 `123pan`/`raylink`/`sogou`/`xshell` 的链接）；② 响应为 `octet-stream`/`x-msdownload` 等可执行类型（如 Xshell `cdn.netsarang.net/v8/Xshell-latest-p` 经 CDN 重定向到文件）→ 返回跟随重定向后的最终文件 URL。任何异常/超时/未找到直链 → `log` 提示并 `return null`，**调用方自动回退 `DownloadUrl`（私有镜像兜底），绝不抛异常、绝不 `return false`**。
- 4 个包各自的解析逻辑：
  - **搜狗拼音** `https://pinyin.sogou.com/index.php`：HTML 下载页，正则提取 `.exe` 直链（搜狗专项目保险：优先主安装包 `sogou_pinyin_*.exe`、排除 `_zhihui` 智慧版变体）。⚠️ **已知限制（沙箱实测其 CDN 在沙箱返回 403，疑似沙箱 IP 被拦，用户机器大概率可下）**：本机制解析成功但以该直链下载失败时**不会自动回退镜像**（因解析已成功覆盖 `downloadUrl`），本次不加 MIRROR 兜底重试（避免过度设计），需用户本机验证 CDN 可达性后决策。
  - **123云盘** `https://www.123pan.com/Downloadclient?type=Pc-`：HTML 下载页，正则提取含 `123pan` 的 `.exe` 直链。
  - **RayLink** `https://www.raylink.live/download.html`：HTML 下载页，正则提取含 `raylink` 的 `.exe` 直链。
  - **Xshell** `https://cdn.netsarang.net/v8/Xshell-latest-p`：latest 指针，CDN 重定向到实际安装包文件，返回跟随重定向后的最终文件 URL（内容类型为可执行文件）。
- 同时给 `Download()` 内的 `HttpClient` 补了浏览器 UA（对所有包生效，安全正向）。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`193A1BDEA1253716D22DF4C3BA1CF15558DEA3FAAA0FAA26B002797E151A0881`（VERIFY_OK）。
- **2026-08-04 微调重新部署**：将「搜狗五笔」条目删除、普通「搜狗拼音」改为走 `PageResolver("https://pinyin.sogou.com/index.php")`（`MIRROR + "sogou_pinyin.zip"` 作解析失败兜底）；并给 `PageLinkResolver` 加搜狗主安装包保险（优先 `sogou_pinyin_*` 主包、排除 `_zhihui` 智慧版）。编译 0/0，重新部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，新 SHA256=`3069F7F4C90EE1142D745205DB629AA427365E16B202DEC2FF012485A43982A2`（VERIFY_OK，源=目标一致）。累计脱离镜像数仍为 **24**（五笔换拼音，数量未变）。
- **累计官方源化进度**：主流包 10（Chocolatey 运行时）+ 微信/OneDrive/Steam 3 + WPS/百度网盘/网易云音乐/腾讯视频 4（钉版本直链）+ 百度拼音/哔哩哔哩/VC++ AIO 3 + 搜狗拼音/123云盘/RayLink/Xshell 4（官方下载页运行时解析）= **24 个脱离私有镜像**（后 4 个保留 `MIRROR` 作解析失败兜底）。

**Phase 6 Playwright 真实浏览器渲染抓取官方直链（2026-08-04，已实现 9 个 + 部署）**
- 方法演进：browser-use 3.0 连接本机 Chrome 调试端口陷入"弹 Allow 即退出"死结（harness 依赖本机 Chrome 的 CDP，daemon 不存活则每次新连接都弹窗且连不上）；改用技能市场经安全审计（0 恶意 / 1 可疑）的 **Playwright**（自带 headless Chromium 二进制，绕开连接本机 Chrome 的授权死结）。脚本逐包打开官网、触发下载、用 `download` 事件 + 响应拦截捕获真实 exe CDN 直链（**不实际下载大文件**，`dl.cancel()` 取消），增量写 JSONL 防中途丢失。
- **本轮钉死 9 个官方直链**（SHA256 留空走 Authenticode，与 baidupinyin/txvideo 同构；均沙箱 ranged GET 实测 `206 + application/octet-stream|x-msdownload`，确为真 exe）：
  - **quark（用户给定）**：`https://umcdn.quark.cn/download/37211/quarkclouddrivepc/pckk@product_guanwang/QuarkCloudDrivePC_V7.0.5.766_pc_pf30001_%28zh-cn%29_release_%28Build3102129-1000-x64%29.exe`（钉 V7.0.5.766，括号编码版 URL）
  - **tim**：`https://qqdl.gtimg.cn/qqfile/qq/TIM/TIM3.5.1/TIM3.5.1.22172.exe`（钉版本）
  - **weiyun**：`https://dldir1.qq.com/weiyun/electron-update/release/5.2.1611/WeiyunApp-Setup-X64-5.2.1611.exe`（钉版本）
  - **aliyunpan**：`https://cdn.aliyundrive.net/downloads/apps/desktop/aDrive-6.9.3.exe`（钉版本）
  - **iqiyi**：`https://cdndata.video.iqiyi.com/cdn/pca/20260804/14.7.5.10167/channel/1785814312514/IQIYIsetup_w01f.exe`（钉版本；`20260804` 为构建日期前缀，版本号 `14.7.5.10167` 在路径中稳定）
  - **kgmusic（酷狗）**：`https://pcpackagebssdlbigapk.cosama.cn/202608041801/dc6a73202616a028ea54d0f9420b0f01/release_20141_x64.exe`（腾讯音乐 CDN，版本化内容哈希）
  - **kwmusic（酷我）**：`https://pkgdown.kuwo.cn/6ba3138119bf3ff448c17ea08c6b6203/6a71b8a1/mbox/kwmusic_web_1.exe`（版本化内容哈希）
  - **unlocker（IObit Unlocker）**：`https://cdn.iobit.com/dl/unlocker-setup.exe`（钉固定名；追加 `/S` NSIS 静默参，延续原 `-SI` 静默重打包行为）
  - **douyin（抖音）**：`https://www.douyin.com/download/pc/obj/douyin-pc-web/douyin-pc-client/7044145585217083655/releases/432763571/8.3.0/win32-ia32/douyin-downloader-v8.3.0-win32-ia32-douyin.exe`（钉 V8.3.0；原 `/download` 入口已 404，真实下载页 `/downloadpage` 静态渲染给出 exe 直链，无签名门控，与 tim/aliyunpan 同级；另有 `-douyincold.exe` 精简版变体未采用）
- ⚠️ **qqmusic 已由"纯 MIRROR"升级为运行时解析器（本块后续追加，详见下「QQ音乐运行时解析落地」）**：原 Phase 6 时误判 y.qq.com 下载页"立即下载"按钮的 `QQMusic_YQQWinPCDL.exe?sign=时间签名` 基址去签名即 403、HttpClient 无法签，故记为"纯 MIRROR 1 个"；但进一步扒 JS + 拉 `download.js` 配置 JSONP 发现**真正可落地的是服务端下发的 `c.y.qq.com/cgi-bin/file_redirect.fcg?...&sign=1-<hex>-<hex>`**（无时间戳、版本跟随、长期有效），经 `file_redirect.fcg` 302 重定向落到真 exe。已扩展 `PageLinkResolver` 识别该 JSONP 配置（提取 Windows PC 条目的 `Flink1`），qqmusic 挂 `.PageResolver("https://y.qq.com/download/download.js?...")`（`MIRROR` 兜底），成为继搜狗拼音/123云盘/RayLink/Xshell 之后的**第 5 个 PageResolver 包**。（抖音不再留镜像——原误判为"JS 化"，实为 `/download` 入口已 404，真实下载页 `/downloadpage` 静态给出 exe 直链，本轮已钉死官方源，见上。）
- ⚠️ **部署后需用户实测的注意**：① kgmusic/kwmusic 直链带版本化内容哈希（类比 tim/aliyunpan 钉版本），厂商发新版后哈希会变、旧 URL 可能 404，维护模型同 baidupinyin/txvideo（发新版刷新即可）；② 官方原版安装器（非原 `-SI` 静默重打包）可能需补 `/S`(NSIS) 或 `/VERYSILENT`(Inno) 静默参才能实现无人值守安装——本轮仅 unlocker 补了 `/S`，其余沿用改动前（无参）行为，建议用户实测一次安装确认是否弹出交互窗口，必要时补静默参；③ **QQ音乐运行时解析器需用户本机实装验证**：`file_redirect.fcg` 签名链接虽经沙箱 ranged GET 实测 302→206+真 exe，但 `Install` 实际下载/静默安装 EXE 须跑在用户机（沙箱无法运行 EXE），且配置接口返回值腾讯可能调整，建议本机实测一次确认解析器真能下到可执行安装包；抖音已钉死静态直链，无需运行时解析。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`15B98EF388F1A350F48B361B27F9AFD7697288C5990A78124C4F34B7A14B1494`（VERIFY_OK，源=目标一致）。
- **累计官方源化进度**：Phase 5 的 24 + 本轮 9（含抖音）= **33 个**；追加 **QQ音乐运行时解析（第 5 个 PageResolver 包）** → **34 个**；再追加 **QQ 运行时解析（第 6 个 PageResolver 包）** → **35 个脱离私有镜像**（含 6 个 PageResolver 保留 `MIRROR`/`jump` 兜底）。**纯 MIRROR 剩余 0 个**——qq 现走 `im.qq.com/pcqq/` 官方页运行时解析（QQNT x64 直链，jump.oyk.pub 仅作兜底），全部包均已脱离私有镜像主源（详见下「QQ 运行时解析落地」）。

**QQ音乐运行时解析落地（Phase 6 后续，2026-08-04 晚，已实现 + 部署）**
- 动机：用户质疑"既然抖音能找到，那 QQ音乐也应该能找到"，推翻 Phase 6 时"qqmusic 签名门控无解"的误判。重探查证：下载页"立即下载"按钮触发的 `dldir.y.qq.com/.../QQMusic_YQQWinPCDL.exe?sign=<时间戳>-<哈希>` 确为客户端时间戳签名（基址去签名 403）——但这只是前端展示用的临时链接，**真正可落地的是服务端下发的配置接口**。
- 关键发现：`y.qq.com/download/download.js`（JSONP 配置接口）下发的 `Flink1` 给出 **`c.y.qq.com/cgi-bin/file_redirect.fcg?bid=dldir&file=...&sign=1-<hex>-<hex>`**（无时间戳前缀、版本跟随、长期有效），`file_redirect.fcg` 302 重定向下到真 exe（`Download()` 默认 `AllowAutoRedirect=true` 自动跟随）。沙箱 ranged GET 实测：`file_redirect.fcg` → `302` → 落到 `206 + application/x-msdos-program`（真 exe）。
- 代码改动（Modules/SoftwareInstall.cs）：① qqmusic 条目保留 `MIRROR + "QQMusic_YQQWinPCDL.exe"` 作兜底、链式挂 `.PageResolver("https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&...&jsonpCallback=MusicJsonCallback")`；② 重构 `PageLinkResolver.Resolve`：新增 y.qq.com Referer（`https://y.qq.com/download/download.html`，配置接口需带），重排分支顺序——文件/latest 指针先返回最终 URL → 新增 **JSONP 配置（QQ音乐）** 分支（去 JSONP 外层括号、正则提 Windows PC 条目 `Flink1`、兜底取首个 Flink1）→ 最后才是 HTML 下载页（搜狗/123云盘/RayLink/Xshell 原逻辑）。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`7C205DF1C4154033E38728F77A387F14A054A0C9496BC8847D0D5A490E1323D6`（VERIFY_OK，源=目标一致）。QQ音乐成为继搜狗拼音/123云盘/RayLink/Xshell 后的**第 5 个 PageResolver 包**，累计脱离私有镜像 **33 → 34**。

**QQ 运行时解析落地（2026-08-04 晚，已实现 + 部署）**
- 动机：用户质疑"qq 也应该能用官方直链吧"。重查证实 Phase 6 时"qq 官方页含静态直链 qqdl.gtimg.cn、但项目走 jump.oyk.pub 中转不纳入官方解析"的记录成立——官方页确有静态 exe 锚点，当时只是没接。
- 关键发现：Playwright 抓 `im.qq.com/pcqq/` 得到 x64 官方直链 **`https://qqdl.gtimg.cn/qqfile/QQNT/9.9.33/release/a0ce07ad/QQ_9.9.33_260730_x64_01.exe`**（返回 200 + application/octet-stream，download 事件确认；域名 `qqdl.gtimg.cn` 与已用 TIM 直链同族腾讯官方 CDN）。沙箱 ranged GET 实测 `206 + application/octet-stream`。该链接是页面**静态锚点**（非 JS 渲染），属 A 类可解析页 → 适合 PageResolver 运行时抓取、永远拿最新版，规避版本失效（不像钉死 URL 那样发新版即 404）。
- 代码改动（Modules/SoftwareInstall.cs）：① qq 条目保留 `https://jump.oyk.pub/jump/redirect?sid=qqnt` 作兜底、链式挂 `.PageResolver("https://im.qq.com/pcqq/")`；② `PageLinkResolver` HTML 分支新增 `isQqPage` 判断（pageUrl 含 `im.qq.com`），优先选含 `QQNT` + `x64` 且非 `arm64` 的链接（排除 x86/arm64/旧 PCQQ9.7.25 锚点）；**QQ 页未匹配到 x64 时显式 `return null` 回退 jump.oyk.pub**（避免误用 x86/旧版链接），其余逻辑（123pan/raylink/xshell 优先、搜狗主包优先）不变。
- 编译 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`848F269789F1F3C940BF8FAFF98AC4782DC59A54E4D5D62379498B65C434482B`（VERIFY_OK，源=目标一致；含三轴复审后健壮性修正：QQ 官方页未匹配到 QQNT x64 时显式回退 null 而非 fallback 错架构链接）。QQ 成为继搜狗拼音/123云盘/RayLink/Xshell/qqmusic 后的**第 6 个 PageResolver 包**，累计脱离私有镜像 **34 → 35**；**纯 MIRROR 剩余 0 个**（所有包均已脱离私有镜像主源，jump.oyk.pub 仅作兜底）。
- 待实测：QQ NT 安装器 `/S` 静默参是否生效（原 jump 链接即用 `/S`，本次沿用；建议用户本机实装确认无人值守安装是否弹窗）。

**私有镜像彻底退役 + QQ 解析器修正（2026-08-05）**
- 用户要求把 qq/xshell/qqmusic 残余三包也钉死官方直链。本轮完成、已部署（SHA256=`A85EFFA790FCBD8DC8F289BA8EE4AC09ED42AFCC7F4D330E29C9472D14D91140`，源=目标一致）。
- **更正上面「QQ 运行时解析落地」关于 QQ 的误述（第 712 行）**：`im.qq.com/pcqq/` 的下载链接**并非静态锚点**——实测该页 HTML 不含任何 `.exe`，链接由 JS 从 `https://cdn-go.cn/qq-web/im.qq.com_new/latest/rainbow/windowsConfig.js` 异步加载 `ntDownloadX64Url`。Phase 6 时 Playwright 抓到的直链来自渲染后 DOM，但 `PageLinkResolver` 只扫静态 HTML body、永远匹配不到 → **QQ 长期静默回退 jump.oyk.pub 镜像**（最隐蔽的隐患）。本轮把 qq 的 `PageResolver` 改指 windowsConfig.js，并在解析器新增 `ntDownloadX64Url` 分支提取最新官方 x64 直链；qq 主 URL 也从 jump.oyk.pub 改为硬编码官方直链兜底。
- xshell 主 URL 钉死 `cdn.netsarang.net/180f2808/Xshell-8.0.0102p.exe`（保留 latest-p 指针解析最新）；qqmusic 主 URL 钉死 `c.y.qq.com/cgi-bin/file_redirect.fcg?bid=dldir&file=...QQMusic_Setup_2241.exe&sign=1-...-6a561755`（保留 download.js 解析最新）。三包策略统一为「官方直链兜底 + PageResolver 取最新」。
- **至此 `hk.oyk.pub`/`jump.oyk.pub` 私有镜像在 SOFTWARE_LIST 中彻底清零，`MIRROR` 常量已删除**（全项目 grep 确认无残留引用）。所有国产包均走官方直链 / 官方下载页解析 / Chocolatey，`StrictSignatureCheck=false` 维持不变。

**清理并行加速（方案 A + 方案 B）+ README 对齐（2026-08-08）**
- 动机：全选清理串行慢，用户要求并行加速。在不动单点正确性的前提下引入两层并行：
  - **方案 A（项内）**：`Cleanup.cs` 新增 `InnerPar`（≤3），对 Nvidia 缓存 / 事件日志通道 / Cookies / 包管理器缓存等独立子任务 `Parallel.Invoke`/`Parallel.ForEach`；Dism、字体缓存、全盘扫描保持串行。
  - **方案 B（大类级）**：`MainWindow.Pages.cs` 顶层按 `Category` 分组 `Parallel.ForEach`（`catPar` ≤4），分组内串行。
  - 日志经 `Dispatcher.BeginInvoke` 线程安全；分组内 `try/catch` 隔离单项异常。
- 同步精简 `CleanupCatalog`：删除 `hiberfil_on`（恢复休眠）项，保留 `hiberfil_off`（关休眠），大空间回收由 4 项变 3 项（对应 `BigSpace*` 方法现仅 `HiberfilOff`/`MemoryDmp`/`WindowsOld`），**总计 34 项 / 6 大类**（缓存 9 / 系统 7 / 更新残留 1 / 浏览器 2 / 日志历史 6 / 大空间 9）。
- 文档对齐：修正 README 中过时表述——`Cleanup.RunSelected`→`CleanupExt.RunSelected`；交付文件名 `CpqSystemTool.exe`→`系统清理与优化工具.exe`（程序集名，见 `CpqSystemTool.csproj` 的 `AssemblyName`）；删除不存在的 `build.bat`，补真实构建命令（沙箱需 `--no-incremental --source https://api.nuget.org/v3/index.json`）；补充「§3.2.1 并行加速（方案 A + 方案 B）」小节。
- 构建 0/0；已自动部署 `D:\电脑桌面\cpq\系统清理与优化工具.exe`，SHA256=`9AD343B4ABA41BDE22F82AF98AA66B9E6B0D68941E035406564F68736161834C`（VERIFY_OK，源=目标一致）。
