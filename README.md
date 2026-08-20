# System-Cleanup-Optimizer — 系统清理与优化工具

> 面向 Windows 10/11 的一体化系统清理、优化与维护工具。
>
> **技术栈**: WPF (C# / .NET Framework 4.8) · 单文件 exe · 零安装 · 双击即跑 · 管理员权限自动提权
>
> **版本**: v1.11
>
> **项目主页**: [https://github.com/dandelion80231/System-Cleanup-Optimizer](https://github.com/dandelion80231/System-Cleanup-Optimizer)
>
> **官网**: [https://cpq-system-tool.pages.dev/](https://cpq-system-tool.pages.dev/)

---

## 📋 目录

- [功能概览](#功能概览)
- [快速开始](#快速开始)
- [功能详解](#功能详解)
    - [1. 系统优化](#1-系统优化)
    - [2. 清理优化](#2-清理优化)
    - [3. 服务优化](#3-服务优化)
    - [4. Appx 商店](#4-appx-商店)
    - [5. Appx 管理](#5-appx-管理)
    - [6. 常用软件](#6-常用软件)
    - [7. 安全防护](#7-安全防护)
    - [8. Edge 管理](#8-edge-管理)
    - [9. 隐私设置](#9-隐私设置)
    - [10. 系统工具](#10-系统工具)
    - [11. 内存工具](#11-内存工具)
    - [12. 激活工具](#12-激活工具)
    - [13. 系统信息](#13-系统信息)
    - [14. 维护工具](#14-维护工具)
    - [15. 驱动清理](#15-驱动清理)
    - [16. 配置管理](#16-配置管理)
- [技术架构](#技术架构)
- [界面与交互实现](#界面与交互实现)
- [构建与部署](#构建与部署)
- [开源与许可](#开源与许可)
- [免责声明](#免责声明)
- [常见问题 (FAQ)](#常见问题-faq)
- [联系与反馈](#联系与反馈)

---

## 功能概览

系统清理与优化工具提供 **16 个主要功能页**（另含「关于」页，共 17 页），覆盖日常清理、系统优化、隐私安全、软件管理、系统维护、驱动管理、内存分析与官方安装包直链探针。

| 模块 | 核心能力 | 实现规模 |
|------|----------|----------|
| 系统优化 | 注册表/策略类开关，7 大分组 | 116 项可勾选优化 |
| 清理优化 | 缓存/系统/更新残留/浏览器/日志/大空间回收 | 34 项常规 + 5 项扩展清理 |
| 服务优化 | 系统服务一键优化/还原 | 19 个预设推荐服务 |
| Appx 商店 | 微软商店 59 款精选应用安装/卸载/预配移除 | 59 个预置 + winget/adguard/Store 三级回退 |
| Appx 管理 | 系统中所有原始 AppX 包枚举与批量卸载 | Get-AppxPackage 原始列表 |
| 常用软件 | 一键安装/卸载，自定义路径 | 48 款内置软件，16 大类 |
| 安全防护 | Defender + 更新 + 防火墙 + 计量连接 | 多模块合并页 |
| Edge 管理 | 多频道安装/卸载/禁更新/关启动增强 | 5 个频道支持 |
| 隐私设置 | 隐私注册表开关 | 12 项独立开关 |
| 系统工具 | 上帝模式/还原点/版本切换 | 3 个独立子模块 |
| 内存工具 | 只读内存仪表盘/使用拆解 + 内存优化（清 Standby/空工作集） | MemoryAnalyzer + MainWindow.Memory |
| 激活工具 | 集成 MAS 五种激活方式 | 6 张卡片 (5 激活 + 1 诊断) |
| 系统信息 | 硬件/软件信息汇总与导出 | WMI + 注册表 + P/Invoke |
| 维护工具 | 官网 exe 直链探针 + 探针环境管理 | WebView2 Runtime / Node+Playwright 双驱动 |
| 驱动清理 | 驱动枚举/在役保护/旧版清理/备份导出/添加安装 | pnputil + DISM 双后端 |
| 配置管理 | 配置导出/导入/自动保存/源码导出 | 零依赖 JSON 序列化 |

> 本章节所有数字、类名、方法名均对照 `src/CpqSystemTool` 实际源码核实。

---

## 快速开始

### 系统要求

- **操作系统**: Windows 10 1903+ 或 Windows 11
- **运行时**: .NET Framework 4.8（系统自带，无需额外安装）
- **权限**: 多数功能需管理员权限，程序会自动请求 UAC 提权

> [!CAUTION]
> **本工具拥有修改系统注册表、服务、策略的完整权限。**
> 在使用任何修改系统设置的功能前，**强烈建议**先在「系统工具 → 系统还原点」中创建一个还原点。
> 如因不当使用导致系统问题，开发者不承担任何责任。

### 下载与运行

1. 前往 [Releases](https://github.com/dandelion80231/System-Cleanup-Optimizer/releases/latest) 或 [官网](https://cpq-system-tool.pages.dev/) 下载最新版 `.exe`（文件名如 `系统清理与优化工具_v1.11.exe`）。
2. 双击运行即可，**无需安装**。所有资源（背景图、图标、SKU 许可令牌、源码包）均已嵌入单文件 exe。
3. 首次使用建议：先创建系统还原点，再进行优化配置。

### 通用操作约定

- **风险标识**: 高风险项以红色/橙色标注，执行前会弹二次确认对话框。
- **可逆优先**: 注册表优化、服务、隐私开关均可通过对应页面的「还原/一键恢复」反向写回，无需重装系统。
- **主题切换**: 右上角可在「深色/浅色/跟随系统」之间切换，自动响应 Windows 个性化设置变更。
- **配置保存**: 优化结果与勾选状态会自动写入 `Config\autosave.json`，换机时可通过「配置管理 → 导出/导入」迁移。

---

## 功能详解

> 每个功能包含三块：**实现原理**（技术思路）、**代码实现方法**（实际落地的源文件 / 类 / 关键方法:行号 / 注册表键与命令，便于在源码中检索）、**使用方法**（操作步骤）。所有引用均对照 `src/CpqSystemTool` 源码核实。

### 1. 系统优化

**核心能力**: 116 项可逆注册表优化，按 7 组分类（外观、性能、安全、Edge、系统、更新、隐私），支持基本/深度/全选预设。

**实现原理**:
- 每项优化封装为一个条目，包含分组、风险等级与正向/反向注册表操作；高风险项单独红色标识，执行前二次确认。
- 页面初始化时并行查询各注册表项状态；应用优化时不重建整页，避免视觉闪烁。
- 预设：基本（仅低风险）、深度（排除高风险）、全选（116 项）。
- 配置可导出为 `.ini`（默认，含全部项状态，带时间戳文件名），并兼容旧版 `.json`（仅勾选 ID 列表）导入。

**代码实现方法**:
- `Modules/Tweaks.cs` — `TweakEntry` 类(:16) 描述单项；`All` 属性经 `Build()`(:90-92) 生成全部 116 项；注册表写入辅助 `DwordR`(:53)/`ApplyDword3`(:60)/`GetDword3`(:68)。
- `MainWindow.Pages.cs` — `BuildTweaks`(:44) 构建 UI；预设按钮调用 `BasicOptimize`/`DeepOptimize`/`SelectAll`(:95-97)；`ApplyTweaks`(:613) 按勾选应用；`ExportConfig`(:521, 导出文件名 `CpqSystemTool优化-{yyyyMMddHHmmss}.ini`:530)、`ImportConfig`(:563)。
- 注册表辅助：`Helpers/RegistryHelper.cs` 的 `SetDword`(:13)/`GetDword`(:71)。

**使用方法**:
1. 选择「基本优化/深度优化/全选」预设，或逐条手动勾选。
2. 右侧「已选中」面板实时汇总当前选择。
3. 点击「应用优化」执行，点击「还原所有项」可整体回退。
4. 可「导出配置」保存本次勾选，便于在其他机器复用。

**权限/风险**: 写 `HKLM` 需管理员权限；高风险项执行前有确认提示。还原机制为反向写注册表，不依赖备份文件。

---

### 2. 清理优化

**核心能力**: 6 大类 34 项常规清理 + 5 项扩展清理（缩略图缓存、D3D 缓存、终端历史、预读取、WinSxS），共 40 个清理项（v1.09 新增「Whesvc 诊断日志」清理项，系统文件类、默认不勾选，清理 Windows 健康状况和优化体验服务的本地性能追踪日志）。

**实现原理**:
- 安全分级：第一档（默认勾选，纯缓存）可安全删；第二档（默认不勾选，更新残留）删后下次更新需重下；第三档（旧资产）经「扫描旧资产」按钮触发，逐项二次确认。
- 并行加速：对独立子任务用 `Parallel.Invoke`；全选时各大类分组并行，分组内串行避免磁盘争用；日志经 `Dispatcher.BeginInvoke` 封送 UI 线程。
- 破坏性操作（清空回收站、关闭休眠、删 Windows.old）执行前弹 `YesNo` 警告；WinSxS 走 `dism /Online /Cleanup-Image /StartComponentCleanup /ResetBase`。

**代码实现方法**:
- `Modules/Cleanup.cs` — 静态类 `Cleanup`(:16)；`CleanDir`(:27)/`CleanPath`(:74) 执行单类清理；`Parallel.Invoke` 并行入口在 :224 与 :356；`ScanTier3`(:651) 即「扫描旧资产」；`WinSxS Temp` 项 :732。
- `Modules/CleanupExt.cs` — `ExtraItem`(:15, 含 `winsxs` :26)、`RunSelected`(:29)；DISM 命令 `Exec.RunCmd(new[]{"dism","/Online","/Cleanup-Image","/StartComponentCleanup","/ResetBase"})`(:36)。
- `MainWindow.Pages.cs` — `BuildCleanup`(:741) 构建 UI；日志封送 `Dispatcher.BeginInvoke`(:271/:286)。

**使用方法**:
1. 勾选要清理的类别，先点「扫描」预览可释放空间。
2. 确认无误后点「清理」，高风险项会再次提示。
3. 第三档「旧资产」需通过独立按钮扫描，逐项确认后删除。

**权限/风险**: 用户目录标准用户即可清理；系统目录需管理员权限。关闭休眠、删除 MEMORY.DMP/Windows.old 属高风险，建议先确认无需要保留的文件。

---

### 3. 服务优化

**核心能力**: 对系统服务进行管理，含一键优化/还原，自动跳过关键系统服务；v1.09 候选清单新增 `whesvc`（Windows 健康状况和优化体验，风险等级 mid）。

**实现原理**:
- 枚举 `HKLM\SYSTEM\CurrentControlSet\Services` 下 Win32 服务（`Type ≠ 0x20` 驱动服务被过滤），形成可操作列表。
- 列表内置每项的风险标注与默认推荐状态；一键优化按推荐将服务设为「禁用/手动」，一键还原恢复默认。

**代码实现方法**:
- `Modules/ServiceOptimizer.cs` — 静态类 `ServiceOptimizer`(:10)；`ServiceEntry`(:12) 描述单项（含 Name/Display/Desc/Risk）；`All` 列表(:21) 为全部候选；`IsDisabled`(:46) 经 `sc qc` 读取当前启动类型；`Apply`(:59)/`SetService`(:66) 调用 `sc config <name> start= disabled`(:69)、`sc start`(:70)、`sc stop`(:71)。
- `MainWindow.Pages.cs` — `BuildServices`(:1907) 构建 UI 并调用一键优化/还原循环。

**使用方法**:
1. 进入页面查看系统服务列表，包含当前状态和推荐操作。
2. 点击「一键优化」快速应用推荐设置，或手动对单个服务操作。
3. 点击「一键还原」恢复所有服务为系统默认状态。

**权限/风险**: 需管理员权限。禁用打印（Spooler）、搜索（WSearch）等服务会影响对应功能，请按需选择。

---

### 4. Appx 商店

**核心能力**: 列出微软商店 59 款精选应用（含已安装与未安装），支持安装、卸载、预配移除；含搜索过滤与功能选项。

**实现原理**:
- 内置精选 Catalog（约 59 个 `AppxDef`，按 StoreId 索引），调用 `AppxManager.ListCatalogWithStatus` 合并本地安装状态。
- 卸载：`Remove-AppxPackage` + `Remove-AppxProvisionedPackage -Online`（脚本经 Base64 UTF-16LE `-EncodedCommand` 执行）。
- 安装三级回退：① `winget` 静默安装 → ② 从 `store.rg-adguard.net` 下载 `.appxbundle` 后 `Add-AppxPackage` → ③ 打开 Microsoft Store 页面手动安装。
- 双模式：「当前用户应用管理」与「系统预装应用卸载」，已安装绿色、未安装红色标识。

**代码实现方法**:
- `Modules/AppxManager.cs` — `public static class AppxManager`(:23)；`AppxDef`(:14) 预置 59 项（StoreId 目录 :30-88）；`ListCatalogWithStatus`(:131) 获取 Catalog 与状态；`Uninstall`(:186) 调 `Remove-AppxPackage -Package`(:205) 与 `Remove-AppxProvisionedPackage -Online -PackageName`(:206)；`Install`(:219) 三级回退：`winget install --id <storeId> --source msstore`(:233) → `InstallViaAdguard`(:367, POST `https://store.rg-adguard.net/api/GetFiles`:413) → Store 页；`UninstallProvisioned`(:522) 调 `DISM.exe /Online /Remove-ProvisionedAppxPackage /PackageName:`(:527)。
- PowerShell 的 `-EncodedCommand`（Base64 UTF-16LE）封装统一在 `Helpers/Exec.cs` 的 `RunPS`(:62, 编码 :75)，不在 AppxManager 内。

**使用方法**:
1. 在列表勾选要卸载的应用，点击「卸载」。
2. 需要恢复时切换至「已卸载/可安装」标签页，点击「安装」。
3. 预配包移除后，新用户首次登录时不会自动安装该应用。

**权限/风险**: 移除系统级 Appx（尤其 `-AllUsers`）需管理员权限。

---

### 5. Appx 管理

**核心能力**: 列出系统中所有原始 AppX 包（含系统组件），勾选后批量卸载。

**实现原理**:
- 直接枚举当前用户已安装的 AppX 包（`Get-AppxPackage`），显示友好名称与完整包名。
- 勾选后批量调用 `Remove-AppxPackage` 卸载；不处理预配包。

**代码实现方法**:
- `Modules/AppxManager.cs` — `ListInstalled`(:91) 调用 PowerShell `Get-AppxPackage` 并解析为 `AppxInfo` 列表；`Uninstall`(:186) 按 `FullName` 移除。
- `MainWindow.Pages.cs` — `BuildAppxRaw`(:2618) 构建原始包列表页，支持全选、反选、批量卸载与实时计数。

**使用方法**:
1. 进入「Appx 管理」页，等待列表加载。
2. 勾选要卸载的原始 AppX 包（可「全选」）。
3. 点击「卸载选中」批量移除。

**权限/风险**: 卸载系统组件可能导致开始菜单/商店等功能异常，建议先确认包名；需管理员权限。

---

### 6. 常用软件

**核心能力**: 48 款内置软件（浏览器、视频、压缩、通讯、开发、虚拟机等 16 大类）一键安装/卸载，支持自定义安装路径。

**实现原理**:
- 安装策略优先级：商店应用（`winget`）→ 官方直链/Chocolatey 社区源解析（验证 SHA256）→ 官方下载页链接解析兜底。
- 完整性校验：安装包经 `WinVerifyTrust` P/Invoke 做 Authenticode 签名校验，签名无效警告但不默认阻止（`StrictSignatureCheck=false`）。
- 静默安装：自动推断安装器类型（NSIS/Inno/MSI）注入对应静默参数；NSIS 支持 `/D=` 自定义路径。
- 已安装检测：注册表路径、DisplayName 关键词或已知 exe 存在性多维度判断。

**代码实现方法**:
- `Modules/SoftwareInstall.cs` — `SoftwareDef`(:20)/`Builder`(:84) 描述软件；`Install`(:136) 逐条执行；`InstallFromStore`(:278, winget)；`CheckInstalled`(:434)/`GetInstalledVersion`(:476)；`Download`(:539, 含 SHA256 校验)；`RunInstaller`(:609)；静态类 `SoftwareInstall`(:759) 中 `StrictSignatureCheck = false`(:765)、入口 `Install(id,...)`(:1099)；`AuthenticodeVerifier`(:1141) 封装 `WinVerifyTrust` P/Invoke(:1184, 调用 :1220)；`PageLinkResolver`(:1250) 解析官方下载页（如 QQ `ntDownloadX64Url` 正则 :1303）。
- `Modules/ChocolateyResolver.cs` — `ChocolateyResolver`(:18)；`TryResolve`(:59)/`LiveResolve`(:80)/`ResolveCandidate`(:103, 命中 `community.chocolatey.org` API) 运行时拉取官方直链。
- `Modules/SoftwareDefPersistence.cs` — 静态类(:107)；内置 `SOFTWARE_LIST` 即 `SoftwareInstall.All`，自定义项存 `custom_software.json`(:111)，`Load`(:121)/`Save`(:155)/`ApplyPendingBakeIfAny`(:301)；与内置在 `BuildCommonSoftware`(:3526) 合并。

**使用方法**:
1. 在搜索框中定位所需软件，勾选后点击「安装选中」。
2. 可在右上角自定义安装路径（注意：部分 NSIS 安装器不支持含空格路径）。
3. 工具会自动下载、校验并静默安装，过程实时显示在日志框。
4. 已安装软件可勾选后点击「卸载选中」移除。

**权限/风险**: 写入 ProgramFiles 需提权。软件定义含下载地址与静默参数，安装包签名校验默认宽松模式（`false`）。

---

### 7. 安全防护

**核心能力**: 合并 Defender 控制、Windows 更新管理、防火墙配置、计量连接四个子模块。

**实现原理**:
- **Defender**: 通过策略注册表键（如 `DisableAntiSpyware`/`DisableAntiVirus`、各类 `Disable*Monitoring`）整体开关实时防护，启用按相反顺序恢复；提供 Runtime 诊断。
- **更新管理**: 组策略 `NoAutoUpdate=1` 禁用自动更新；长期暂停写 `FlightSettingsMaxPauseDays`（系统实际支持的天数上限，通常为 10000）配合 `PauseFeatureUpdates`/`PauseQualityUpdates` 标志。
- **防火墙**: 经 `Get-NetFirewallProfile`/`Get-NetFirewallRule` 读写；`AddBlockAddressRule` 创建出站阻止规则，地址经白名单正则校验并单引号转义防注入。
- **计量连接**: 经 advapi32 P/Invoke 夺取 `TrustedInstaller` 所有权、改写 DACL，写 `DefaultMediaCost` 下网络类型值为 2 再还原所有权。

**代码实现方法**:
- `Modules/Defender.cs` — `public static class Defender`(:15)；`Disable`(:331)/`Enable`(:343) 靠策略注册表（GP 根键清除 :257）；`GetRealtime/SetRealtime`(:123/:129)、`DiagnoseRuntime`(:309) 等。
- `Modules/Updater.cs` — `WU_AU_KEY=HKLM\...\WindowsUpdate\AU`(:11)；`NoAutoUpdate` 写(:41)；`AllowLongPause`(:152) 写 `FlightSettingsMaxPauseDays`(:155) + `PauseFeatureUpdates`/`PauseQualityUpdates`(:158-159)；`IsLongPaused`(:187)。
- `Modules/FirewallCore.cs` — `Get-NetFirewallProfile`(:40)/`Get-NetFirewallRule`(:77)；`AddBlockAddressRule`(:106) 内白名单正则 + `Exec.EscapeSingleQuote`(:121)。
- `Modules/MeteredConnection.cs` — `SUBKEY=...DefaultMediaCost`(:15)； advapi32 P/Invoke `SetNamedSecurityInfoW`(:66)/`RegOpenKeyExW`(:96)/`RegSetValueExW`(:108)；`TakeOwnership`(:150, 改写 TrustedInstaller/Administrators 所有权)；Ethernet/WiFi/Default 写 DWORD 2。

**使用方法**:
1. **Defender**: 点击「一键禁用 WD」或「一键恢复 WD」，底部可「清理策略残留」或「诊断 Runtime 状态」。
2. **更新管理**: 查看当前生效策略（按钮高亮显示），点击切换不同策略。
3. **防火墙**: 查看配置文件状态，增删规则，一键阻断遥测。
4. **计量连接**: 勾选要设为计量的网络类型，点击应用。

**权限/风险**: 所有操作均需管理员权限。Windows 11 24H2+ 重启后部分 Defender 设置可能被系统还原，属已知限制。

---

### 8. Edge 管理

**核心能力**: 支持 5 个频道（Stable/Beta/Dev/Canary/SxS）的版本检测、安装、卸载、禁用自动更新、关闭启动增强。

**实现原理**:
- 版本检测读各频道 `WOW6432Node\...\Uninstall` 键；安装从官方在线安装器静默安装；卸载读 `UninstallString` 或强清目录+注册表；禁自动更新删除 `edgeupdate` 服务与计划任务并写组策略；关启动增强写 `StartupBoostEnabled=0`。

**代码实现方法**:
- `Modules/EdgeCore.cs` — `public static class EdgeCore`(:10)；版本检测用 Uninstall 键（stable/beta/dev/canary/sxs :18-22）；`InstallEdge`(:72) 下载 `c2rsetup.officeapps.live.com` 后 `MicrosoftEdgeSetup.exe /silent /install`(:80)；`UninstallEdge`(:85)；`BlockEdgeUpdate`(:179) 调 `sc stop`/`sc delete edgeupdate`(:186-187) 与 `schtasks /delete`(:191-192)；`StartupBoostEnabled=0` 经 `RegistryHelper.SetDword`(:201/:234)，`IsStartupBoostEnabled`(:216)；`RestoreEdgeUpdate`(:208)。

**使用方法**: 选择频道 → 安装/卸载；「禁用自动更新」「关闭启动增强」为独立开关。

**权限/风险**: 卸载与禁更新需管理员权限。

---

### 9. 隐私设置

**核心能力**: 12 项隐私注册表开关，覆盖云搜索、Web 搜索、广告 ID、遥测、传递优化、活动历史、搜索历史、墨迹词典、应用启动跟踪、语言列表、建议内容、MRT 大版本更新锁定等。

**实现原理**:
- 每项均为 `Disable*`/`Enable*` 互逆方法对，直接写/删对应注册表值，无备份文件，还原即反向操作。
- 覆盖 `HKCU`（标准用户即可）与 `HKLM` 策略项（需管理员权限）两类作用域。

**代码实现方法**:
- `Modules/PrivacyCore.cs` — `public static class PrivacyCore`(:7)，字段 `HKLM`/`HKCU`(:9-10)；12 个 `Disable*` 各配 `Enable*` 反转：`CloudSearch`(:13)、`WebSearch`(:30)、`AdvertisingID`(:47)、`Telemetry`(:70)、`DeliveryOptimization`(:81)、`ActivityHistory`(:114)、`SearchHistory`(:147)、`InkDict`(:218)、`AppStartTracking`(:236)、`LanguageList`(:254)、`SuggestedContent`(:272)、`MRTUpdate`(:294)；注册表辅助 `RegistryHelper.SetDword`/`DeleteValue`。

**使用方法**: 逐条勾选要关闭的隐私项 → 应用；再次点击可还原。

**权限/风险**: 多为 `HKCU`（标准用户即可）；`HKLM` 策略项需管理员权限。

---

### 10. 系统工具

**核心能力**: 上帝模式、系统还原点、Windows 版本切换三个子模块。

**实现原理**:
- **上帝模式**: 在桌面创建 `GodMode.{GUID}` 文件夹并打开，汇聚所有控制面板项。
- **还原点**: 调 PowerShell `Checkpoint-Computer`/`Get-ComputerRestorePoint`/`Restore-Computer`。
- **版本切换**: `dism /Get-CurrentEdition` 查当前版本 → 从嵌入资源 `*.xrm-ms` 注入 SKU 许可证书（`slmgr /ilc` + `/rilc`）→ `slmgr /ipk` 装零售通用密钥 → `changepk.exe /ProductKey` 触发重启。密钥为微软官方公开发布的零售通用安装密钥，非 KMS GVLK。

**代码实现方法**:
- `Modules/GodMode.cs` — `GODMODE_NAME="GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}"`(:12)，`Create`(:14)。
- `Modules/RestorePoint.cs` — `Create`(:27) 调 `Checkpoint-Computer -RestorePointType 'MODIFY_SETTINGS'`(:30)；`List`(:37)/`Restore`(:60) 调 `Get-ComputerRestorePoint`/`Restore-Computer`。
- `Modules/VersionSwitch.cs` — `GetCurrentEdition`(:20) 调 `dism.exe /Online /Get-CurrentEdition`(:321)；`InstallSkuCert`(:152) 从 `asm.GetManifestResourceStream(...xrm-ms)`(:205) 提取令牌 → `slmgr.vbs /ilc`(:211) + `/rilc`(:164/:229)；`SwitchEdition`(:403) 调 `slmgr /ipk`(:457) 与 `changepk.exe /ProductKey`(:470)；`BackupActivation`(:312)/`RestoreActivation`(:355)。

**使用方法**:
1. **在做任何重大更改前，先点击「创建系统还原点」**。
2. **上帝模式**: 点击开关即可创建/删除上帝模式文件夹。
3. **版本切换**: 选择目标版本（14 个可选），点击「切换」并确认。系统将自动重启完成切换。

**权限/风险**: 需管理员权限。版本切换会重启系统，且切换后可能变为未激活状态，建议先备份激活信息（`slmgr /dlv`）。

---

### 11. 内存工具

**核心能力**: 只读实时内存仪表盘（总/可用物理、占用 %、已提交/上限、内核分页/非分页池）+ 内存使用拆解（Active/Standby/Modified/Free+Zero 占比条、提交/缓存/池明细、进程工作集 Top 10）；附「内存优化」区（清 Standby 列表 / 空工作集，默认收起、中风险、仅管理员启用）。

**实现原理**:
- 只读层（A/B）全部走**文档化 API**，规避未文档化结构体偏移猜错导致的「静默假数据」：
  - `GlobalMemoryStatusEx`（kernel32）：总/可用物理、内存占用 %。
  - `GetPerformanceInfo`（psapi）：已提交、提交上限、内核分页/非分页池、页大小、进程数。
  - WMI `Win32_PerfFormattedData_PerfOS_Memory`：拆解 Active/Standby/Modified/(Free+Zero) 及系统缓存、提交、池使用。
  - 逐进程 `GetProcessMemoryInfo`（psapi）+ `EnumProcesses`：进程工作集 Top 10。
- 优化层（C，未文档化 + 需管理员 + 提权，UI 默认 `Expander.IsExpanded=false`，非管理员禁用按钮）：
  - `NtSetSystemInformation(SystemMemoryListInformation=0x50, cmd=2 purge standby)` 清空备用列表。
  - 逐进程 `EmptyWorkingSet`（psapi）清空工作集。
  - 提权 `AdjustTokenPrivileges` 启用 `SeProfileSingleProcessPrivilege` + `SeIncreaseQuotaPrivilege`。
  - 常量 purge standby=2 取 Process Hacker / Windows Internals（网上有写 3/4 的错值）；未文档化 API 跨版本稳定但微软不保证，全程 try/catch 优雅降级。

**代码实现方法**:
- `Modules/MemoryAnalyzer.cs` — `internal static class MemoryAnalyzer`；`MEMORYSTATUSEX`+`GlobalMemoryStatusEx`、P/Invoke `GetPerformanceInfo`（psapi，`PERFORMANCE_INFORMATION`）、`NtSetSystemInformation(int,int,int)`（ntdll，`SystemMemoryListInformation=0x50`，`MemoryPurgeStandbyList=2`）、`EmptyWorkingSet`/`EnumProcesses`/`OpenProcess`/`GetProcessMemoryInfo`+`PROCESS_MEMORY_COUNTERS_EX`；提权 `OpenProcessToken`/`LookupPrivilegeValue`/`AdjustTokenPrivileges`/`TOKEN_PRIVILEGES`/`LUID`；数据模型 `MemoryOverview`/`MemoryUseCounts`/`ProcessMemInfo`；公开 `IsAdministrator()`/`FormatBytes(ulong)`/`GetOverview()`/`GetUseCounts(ulong)`/`GetProcessWorkingSets(int)`/`OptimizePurgeStandby()`/`OptimizeEmptyWorkingSets()`。
- `MainWindow.Memory.cs` — `BuildMemory()` 构建页面（卡片 A 总览 + 卡片 B 使用拆解 + 占比条 + 进程 Top10；卡片 C 优化区默认收起）；`DoMemoryAnalyze` 后台取数经 `Dispatcher.Invoke(applyUi)` 回写 UI；固定语义色（青=在用/橙=Standby/紫=Modified/绿=Free+Zero）；`Btn(...,null,...)` + `Click +=` 规避 CS0841 前向引用。
- `MainWindow.Nav.cs` — 导航项 `Key="memory", Title="内存工具", Icon="🧠", Build=BuildMemory`（位于「系统工具」之后）。

**使用方法**:
1. 进入「内存工具」页，自动加载只读仪表盘与拆解视图。
2. 点击「重新分析」刷新实时数据。
3. 展开「内存优化」区（需管理员）：点「清空备用列表」或「清空工作集」释放内存；非管理员时按钮禁用并提示需提权。

**权限/风险**: 只读层标准用户即可；优化层需管理员权限，属中风险操作（可能短暂影响系统响应），仅建议确有需要时执行。

---

### 12. 激活工具

**核心能力**: 6 张卡片（HWID/KMS38/Ohook/Online KMS/TSforge + 诊断），集成 Microsoft Activation Scripts (MAS)。

**实现原理**:
- 点击激活卡后，经二次确认，启动提权 PowerShell 执行 `irm https://get.activated.win | iex` 并带对应开关；脚本经 `-EncodedCommand`（Base64 UTF-16LE）传入。
- 执行方式：`Process.Start` + `UseShellExecute=true` + `Verb="runas"`，弹出可见提权窗口让 MAS 脚本写入激活信息；以脚本执行完毕后的状态刷新作为反馈。
- 诊断卡：本地 `cscript slmgr.vbs /dli`/`/xpr` + `SoftwareLicensingProduct` WMI 查询，不联网。
- Office 管理：页面下半部分提供 Office 在线安装/卸载（ODT `setup.exe`）。

**代码实现方法**:
- `Modules/Activation.cs` — `GetWindowsActivationStatus`(:19)/`IsWindowsActivated`(WMI `SoftwareLicensingProduct` :38)；`ActivateWithMAS`(:240) 构造 `& ([ScriptBlock]::Create((irm https://get.activated.win))) /<switch>`(:255)，经 `RunPS` 的 `-EncodedCommand`(:259-264)，`Process.Start` `Verb="runas"`(:267-269)；开关映射 HWID/KMS38/Ohook/K-Windows/Z-WindowsESUOffice(:225-229)；`CheckStatus`(:301) 调 `slmgr /dli`(:66)/`/xpr`(:85)；Office 走 `OSPP.VBS`(:158-162)。
- `Modules/OfficeInstall.cs` — `Editions`(:15)；`Install`(:35)/`Uninstall`(:54)；ODT `setup.exe` 来自 `officecdn.microsoft.com`(:80)。

**使用方法**:
1. 选择所需激活方式卡片（如 HWID 永久激活 Windows，Ohook 永久激活 Office）。
2. 点击后弹窗确认，等待 PowerShell 提权窗口执行完毕。
3. 点击「诊断」卡片刷新查看激活状态。

**权限/风险**: 需管理员权限。MAS 为第三方 GPL v3 脚本，由官方地址 `get.activated.win` 运行时远程获取，**本工具不打包、不修改该脚本**。激活涉及系统授权变更，请遵守当地法律法规及 Microsoft 许可条款。

---

### 13. 系统信息

**核心能力**: 汇总 CPU、内存、显卡、磁盘、网卡、主板等硬件信息，以及系统版本、EditionID、DisplayVersion、UBR、安装日期等软件信息，支持导出 TXT。

**实现原理**:
- 硬件经 WMI（`Win32_Processor`/`Win32_PhysicalMemory`/`Win32_VideoController`/`Win32_DiskDrive`/`Win32_NetworkAdapter`/`Win32_BaseBoard`）；系统版本经注册表 `CurrentVersion`；内存经 `GlobalMemoryStatusEx` P/Invoke；显存三级探测（注册表 `qwMemorySize` → `nvidia-smi` → WMI 兜底）。

**代码实现方法**:
- `Modules/SystemInfo.cs` — `DualReport`(:19)；WMI 类分别在 :38/:64/:98/:175/:213/:234；VRAM `qwMemorySize` 注册表(:131-146) 与 `nvidia-smi --query-gpu=name,memory.total`(:162)；`GlobalMemoryStatusEx` P/Invoke(:337, 调用 :345)。
- `MainWindow.Pages.cs` — `BuildSystemInfo`(:4369) 构建 UI；导出 TXT 文件名 `system-info-{yyyyMMdd-HHmmss}.txt`(:4398)。

**使用方法**: 打开页面即自动加载；点击「导出」保存为文本文件。标准用户可读取所有信息。

---

### 14. 维护工具

**核心能力**: 抓取官网软件安装包（exe）直链，管理本地探针依赖（WebView2 Runtime / Node + Playwright + Chromium）。

**实现原理**:
- 官方 exe 直链探针：输入厂商名或入口 URL，调用浏览器 CDP（WebView2 Runtime 优先）或 Node + Playwright（兜底）抓取最终安装包直链。
- 双驱动环境：优先复用系统已安装的 WebView2 Runtime；注册表损坏时主动扫描磁盘目录兜底；未就绪时自动提示切换 Node + Playwright 方案。
- 环境管理：一键安装/卸载/修复 WebView2 或 Node + Playwright 依赖。

**代码实现方法**:
- `Modules/ProbeEngine.cs` — `ProbeEngine.RunAsync`(:299) 协调搜索流程；解析结果 `ProbeEngineResult`(:64)。
- `Modules/ProbeBrowserHost.cs` — `ProbeBrowserHost`(:24) 基于 WinForms + WebView2 的 CDP 浏览器宿主；`CheckWebView2ReadyAsync`(:239) 检测环境；注册表损坏时显式扫描 EdgeWebView/Edge 目录(:327)。
- `MainWindow.Maint.cs` — `BuildMaintenanceTools`(:20) 构建维护工具页；调用探针并管理依赖。
- `MainWindow.Probe.cs` — 探针 UI 与结果展示。

**使用方法**:
1. 进入「维护工具」页，在输入框填入厂商名（如 `qq`、`douyin`）或官方下载页 URL。
2. 点击探测，工具会尝试获取官方安装包直链。
3. 若探针环境缺失，点击「管理依赖」安装 WebView2 Runtime 或 Node + Playwright 环境。

**权限/风险**: 下载安装包仅获取直链，不自动执行安装；环境安装需联网下载官方/Node 组件。

---

### 15. 驱动清理

**核心能力**: 基于 Windows 原生驱动管理接口（PnP 实用工具 / DISM / SetupAPI）独立实现，界面列布局与行为设计参考 Driver Store Explorer（RAPR）；对系统驱动存储（Driver Store）进行枚举、识别、备份与清理；支持设备名称补全、列头三态排序、启动后台预加载与每次进入自动刷新。

> **第三方来源与致谢**：本模块的列顺序、标题与部分行为设计参考了 [Driver Store Explorer（RAPR）](https://github.com/lostindark/DriverStoreExplorer)（上游采用 GPL v2 许可），但为**独立实现**——仅使用 Windows 原生 API，未包含其任何源代码，与上游项目无隶属或衍生关系。

**实现原理**:
- **枚举**: 默认 PnP 实用工具后端 `pnputil /enum-drivers`（仅含第三方驱动）解析 OEM 驱动列表；可切换 DISM 后端（`Get-WindowsDriver`）列出含系统内置驱动的全量清单（DISM 后端仅查看，删除/导出/安装按钮自动禁用，避免误操作）。
- **在役保护**: 经 WMI `Win32_PnPSignedDriver` 取得当前在用设备的 INF 名集合，标记在役驱动，默认不可删。
- **旧版冗余识别**: 同系列驱动仅保留最新版受保护，其余标记为可清理旧版；按 `DriverStore\FileRepository` 估算占用空间。
- **设备名称补全**: 主源 SetupAPI（`SetupDiGetDeviceRegistryPropertyW`）枚举在役设备；WMI `Win32_PnPSignedDriver`/`Win32_PnPEntity` 双键（OemName/OriginalName）兜底；仍无匹配时按 `Provider + ClassDescription` 兜底，减少空白项。
- **删除**: 默认不带 `/force`，仅删未在役的旧版 `oem#.inf`；删除前二次确认（危险操作红色提示）；勾选「包含启动关键驱动」方可操作 `BootCritical` 项（默认保护）。
- **导出备份**: `pnputil /export-driver` 将选中（或未选则全部）驱动导出到指定目录，便于回滚。
- **三态排序**: 点击列头循环「无 → 升序 → 降序 → 无」，方向 ▲/▼ 实时显示，按 backing 属性（日期/大小/版本）而非显示字符串排序。
- **预加载与自动刷新**: 启动后在后台预加载驱动列表并缓存页面实例；每次进入「驱动清理」页都自动后台刷新（`Navigate` 触发 `Refresh()`），进入即见已加载数据。

**代码实现方法**:
- `Modules/DriverStore.cs` — `internal static class DriverStore`(:26)；`DriverEngine` 枚举(:29, `PnpUtil`/`Dism`)；`DriverInfo` 模型(:38, `DeviceName`/`IsDism`/`BootCritical` 等)；`Enumerate`(:292) 按引擎分发 `pnputil /enum-drivers`(:316) 或 `ParseDismOutput`(:616)；`ParseEnumOutput` 标签定位法容错（:333，兼容中文/英文/UTF-8 挤行）；`ResolveDeviceNames`(:511) 经 `BuildDeviceNameMapViaSetupApi`(:162, `SetupDiGetDeviceRegistryPropertyW`(:134)) 与 `BuildDeviceNameMapViaWmi`(:569, `Win32_PnPSignedDriver`(:575)/`Win32_PnPEntity`(:589)) 双源补全设备名；`GetActiveInfNames`(:735, `Win32_PnPSignedDriver`) 识别在役；`Delete`(:904, `/delete-driver` 默认不带 `/force`，`/force`:926)；`AddDriver`(:941, `/add-driver`)、`Export`(:968, `/export-driver`)、安装经 pnputil `/install-driver`。
- `DriverStorePanel.cs` — `DriverStorePanel` 页 UI；`GroupMode` 枚举(:44, None/Class/Provider)；`_dg.Sorting += OnDataGridSorting`(:244)；`OnDataGridSorting`(:504) 三态，`ListCollectionView.SortDescriptions`(:523)；`ApplyGrouping` 经 `PropertyGroupDescription("ClassDescription"/"Provider")`(:557-558)；`Reenumerate`(:463) 后台枚举；`AddDriverDialog`(:603)/`InstallSelected`(:621)；`includeBootCritical`(:41) 启动关键保护；DISM 后端按钮禁用 `SetButtonEnabled`(:417-420) + 提示 `_dismHint`(:198)。
- `MainWindow.DriverStore.cs` — `BuildDriverStore` 构建页面并缓存实例（`_cachedDriverStoreRoot`）；`PreloadDriverStore`（启动预加载）；`InvalidateDriverStoreCache`（主题切换时重建）。
- `MainWindow.Nav.cs` — 导航项 `Key="driverstore", Title="驱动清理", Build=BuildDriverStore`(:38)；进入时触发 `_driverStorePanel?.Refresh()`(:180)。

**使用方法**:
1. 进入「驱动清理」页（启动即后台预加载，进入自动刷新）。
2. 勾选要操作的驱动：在役驱动默认不可删（红色标识），旧版冗余标为可清理。
3. 点击「删除选中」清理冗余驱动（二次确认）；点「导出」备份到目录；点「添加驱动包」/「安装选中」新增或安装驱动。
4. 可按列头三态排序、按类别/供应商分组，切换 PnP 实用工具 / DISM 后端查看。

**权限/风险**: 删除/导出/安装需管理员权限；默认保护在役与启动关键驱动，强制删除（`/force`）会移除仍在引用的包，属高风险操作，请先导出备份。

---

### 16. 配置管理

**核心能力**: 全局配置导出/导入、自动保存、源码包导出、背景图设置。

**实现原理**:
- 自研零依赖 `MiniJson` 序列化，包含优化勾选、待卸载 Appx、清理项勾选、Flags、TweakStates 等字段。
- 程序退出经 `App.Exit` 自动保存到 `Config\autosave.json`；配置导出保存为带中文时间戳的 JSON；源码导出从嵌入资源 `CpqSystemTool.src.zip` 解压；背景图持久化到 `Config\background.json`。

**代码实现方法**:
- `Modules/ConfigBackup.cs` — `MiniJson`(:29, `Serialize`:31)、`MiniJsonParser`(:104, `Parse`:106)、`ConfigBackup`(:228) 的 `Save`(:234)/`Load`(:246)/`AutoSave`(→ `autosave.json`:258)。
- `MainWindow.Pages.cs` — `BuildConfig`(:4532) 构建 UI；全套配置导出默认名 `系统清理与优化配置_{yyyyMMdd_HHmmss}.json`(:4661)；源码包解压 `asm.GetManifestResourceStream("CpqSystemTool.src.zip")` → 写出 `src.zip`(:4700-4704)。
- `MainWindow.Theme.cs` — 背景图 `Config\background.json`（加载 :27 / 保存 :47）。

**使用方法**:
- **导出配置**: 选择路径保存为 `系统清理与优化配置_{日期时间}.json`（全套备份）。
- **导入配置**: 导入之前备份的 JSON 文件，整份还原所有设置。
- **导出源码**: 将内置源码包解压到本地，方便学习研究。
- **换机迁移**: 将旧电脑 `Config\` 文件夹下的文件复制到新电脑同目录即可。

---

## 技术架构

### 项目结构

```
MainWindow.xaml              # 外壳布局
MainWindow.xaml.cs           # 启动入口、主题状态
MainWindow.Nav.cs            # 侧边栏构建、导航
MainWindow.Theme.cs          # 深/浅两套配色、系统主题跟随、背景图持久化
MainWindow.Helpers.cs        # UI 辅助工厂
MainWindow.Pages.cs          # 功能页 UI 构造函数（约 5200 行）
MainWindow.Probe.cs          # 系统探针
MainWindow.Maint.cs          # 维护工具页（官方 exe 直链探针）
MainWindow.DriverStore.cs     # 驱动清理页构建 + 预加载缓存
MainWindow.Memory.cs         # 内存工具页（A/B/C 三层）
DriverStorePanel.cs          # 驱动清理页 UI（DataGrid 三态排序 / 分组 / 预加载）
MainWindow.Pages.cs 页面构造器（Build*，按导航顺序）:
  BuildTweaks / BuildCleanup / BuildServices /
  BuildAppx / BuildAppxRaw / BuildCommonSoftware /
  BuildSecurity / BuildEdge / BuildPrivacy / BuildSystemTools /
  BuildMemory /
  BuildActivation / BuildSystemInfo / BuildMaintenanceTools /
  BuildDriverStore / BuildConfig /
  BuildAbout（隐藏页）
OtherTweaksDialog.cs         # 其他优化项对话框
Modules/                     # 核心功能模块（25 个 .cs）
├── Tweaks.cs                # 116 项注册表优化 (TweakEntry)
├── Cleanup.cs               # 磁盘清理核心 (Cleanup)
├── CleanupExt.cs            # 扩展清理 (CleanupExt, DISM WinSxS)
├── Activation.cs            # MAS 激活集成 (ActivateWithMAS)
├── OfficeInstall.cs         # Office 安装/卸载 (ODT)
├── Updater.cs               # Windows 更新控制 (NoAutoUpdate/AllowLongPause)
├── MeteredConnection.cs     # 计量连接 P/Invoke (DefaultMediaCost)
├── ServiceOptimizer.cs      # 服务枚举/优化 (ServiceEntry)
├── AppxManager.cs           # UWP 应用管理 (AppxDef/Uninstall/Install)
├── Defender.cs              # Defender 禁用/启用 (策略注册表)
├── DriverStore.cs           # 驱动清理核心（pnputil/DISM 封装、解析、在役识别、删除/导出/添加）
├── EdgeCore.cs              # Edge 安装/卸载/优化 (5 频道)
├── PrivacyCore.cs           # 隐私设置（12 个 Disable*）
├── SoftwareInstall.cs       # 常用软件安装器 (SoftwareDef/AuthenticodeVerifier)
├── ChocolateyResolver.cs    # Chocolatey 官方直链解析
├── SoftwareDefPersistence.cs# 自定义软件定义持久化 (custom_software.json)
├── FirewallCore.cs          # 防火墙规则管理 (AddBlockAddressRule)
├── GodMode.cs               # 上帝模式
├── RestorePoint.cs          # 系统还原点 (Checkpoint-Computer)
├── SystemInfo.cs            # 系统信息采集 (WMI/P/Invoke)
├── ConfigBackup.cs          # 配置导入/导出 (MiniJson)
├── MemoryAnalyzer.cs         # 内存分析/优化 (GlobalMemoryStatusEx/GetPerformanceInfo/WMI/EmptyWorkingSet/NtSetSystemInformation)
└── VersionSwitch.cs         # Windows 版本切换 (dism/slmgr/changepk)
Helpers/
├── Exec.cs                  # 进程执行封装 (RunPS 内置 Base64 UTF-16LE 编码)
└── RegistryHelper.cs        # 注册表读写 (SetDword/GetDword/DeleteValue)
CpqSystemTool.csproj         # SDK-style 项目
```

### 主题系统

- **两套配色，25 个笔刷字段**: `MainWindow.xaml.cs:22-50` 定义 25 个 `internal SolidColorBrush` 字段（强调色、文本、面板、卡片、语义色、表格、按钮、窗口背景等；含本轮新增的危急操作描边 `_dangerDark`）。强调色 `_accent` = `#16E0BD`(深) / `#089182`(浅)。
- **切换机制（Brush 实例捕获 + 整页重建）**: `SetDarkColors()` / `SetLightColors()`（`MainWindow.Theme.cs:229/:261`）对字段重新 `new SolidColorBrush(...)` 赋值；各页面在 `Build*` 时直接捕获这些**笔刷实例**，因此切换主题需 `Navigate(_activeNavKey)` 整体重建当前页（`ThemeToggle_Click:371-382`）。
- **少数资源走 Resources 替换（可实时刷新）**: 外壳级 `ApplyShellColors()` 用 `Resources[key]=new SolidColorBrush(...)` 直接替换 `ScrollThumbBrush` / `AccentBrush` / `ButtonHoverBrush`（`:358/:361/:366`），故滚动条/按钮悬浮色随主题即时更新。
- **系统主题跟随**: `DetectSystemLightTheme()`（`:410`）读 `HKCU\...\Themes\Personalize\AppsUseLightTheme`；`HookSystemThemeChange()`（`:431`）订阅 `SystemEvents.UserPreferenceChanged`，仅在用户未手动覆盖（`!_userOverrodeTheme`）时自动重跑 `ApplyTheme`+`Navigate`。

### 安全模型

- 所有 PowerShell 脚本经 **Base64 UTF-16LE `-EncodedCommand`** 执行（封装于 `Helpers/Exec.cs` 的 `RunPS`，脚本为程序内置常量，不下载外部脚本注入）。
- 仅「激活 (MAS)」与「Edge 安装」会联网下载可执行内容，且来源为官方地址（`get.activated.win`、`c2rsetup.officeapps.live.com`）。
- 防火墙地址、版本切换等涉及外部输入处均做了**白名单校验与注入防护**（`FirewallCore.AddBlockAddressRule` 正则 + `Exec.EscapeSingleQuote`）。

---

## 界面与交互实现

> 本章汇总项目里**较复杂的 WPF 界面与交互实现方案**，均对照 `src/CpqSystemTool` 源码核实（file:line 可直接检索）。

### 外壳布局与导航

- **外壳**（`MainWindow.xaml`）：根 `Grid` 两列（`SidebarCol=180` + `*`）；背景图 `BgImage` 跨两列 `Stretch=Fill`；右侧 `Grid` 三行 = 顶栏 `TopBar`(Auto) / 内容 `ContentArea`(ScrollViewer, `*`) / 底栏 `StatusBar`(Auto)。
- **侧边栏**（`MainWindow.Nav.cs:21-154`）：`DockPanel` 顶部标题 + `StackPanel` 导航按钮 + 底部品牌区（图标 + 版本号，「关于」入口）；导航按钮 hover/选中复用 `_rowHover`/`_accent`。
- **侧边栏拖拽条**：用自定义 `Border`（`SidebarDragger`）替代 `GridSplitter`（`MainWindow.xaml.cs:238-261`），避免切分条白屏。
- **页面路由**：`Navigate(key)`（`Nav.cs:156`）→ `ContentArea.Content=null` + `UpdateLayout` → `SetPageContent(n.Build())`（`Nav.cs:209`）直接赋值（不套双层 `Grid`，避免 `Star` 行在 `Auto` 父行塌缩）。
- **每页独立 ScrollViewer**：每个 `Build*` 自管可滚区域；主 `ContentArea` 始终 `HorizontalScrollBarVisibility=Disabled`，页面根高经 `BindRootHeightToViewport` 跟随视口。

### 自定义窗口边框与对话框样式（DialogChrome）

- 静态类 `DialogChrome`（`CustomSoftwareDialog.cs:18-62`），核心 `DialogChrome.Apply(Window w, MainWindow owner)`（`:21`）：注入 `WindowStyle=None` + `AllowsTransparency=True` + `Background=Transparent`，内层 `Border`（`CornerRadius(12)` + `DropShadowEffect`）浮起呈现**圆角阴影卡**（`:23-27`）。
- 用 `FrameworkElementFactory` 构建 `ControlTemplate`（`Border` + `ContentPresenter` + 三触发器：hover→`ButtonHoverBrush`、press→`Opacity 0.8`、disable→`Opacity 0.5`），并注入同名笔刷 `AccentBrush`/`ButtonHoverBrush`（取自 `owner`，`:31-32`），使独立窗口沿用主窗口主题。
- **为何需手动注入**：每个 `Window` 有独立 `ResourceDictionary`，主窗口 `MainWindow.xaml` 的 `Window.Resources` **不会**自动继承到独立窗口。
- **调用点**：`CustomSoftwareEditDialog`（`:80`）、`CustomSoftwareManagerDialog`（`:531`）、`Tier3ConfirmDialog`（`:26`）。
- **已知不一致（事实，非缺陷）**：`InstallPathDialog`（`:20`）内联复制了同套模板（`:55-87`）而非调 `Apply`；`OtherTweaksDialog`（`:51`）未注入圆角模板，用 `ToolWindow` 默认边框（`:78`）+ 复制 13 个 owner 笔刷（`:60-72`），视觉语言与其他弹窗不同。

### 视口高度绑定（首帧 vp=0 漂移修复）

- `BindRootHeightToViewport(FrameworkElement root)`（`MainWindow.Helpers.cs:248-252`）：将 `root.MaxHeight` 绑定到 `ContentArea.ActualHeight`（`OneWay`）。
- **原理**：`ViewportHeight` 不是 `DependencyProperty`，绑定只求值一次 → 首帧 `vp=0` 不填充、还原窗口尺寸后漂移；改绑只读 DP `ActualHeight` 后尺寸变化自动通知。
- **调用点**：`BuildCommonSoftware`（`:3531`）、`BuildAppx`（~`:2215`）、`BuildAppxRaw`（~`:2604`）、`BuildCleanup`/`BuildActivation` 等多页。
- **配套**：`ContentArea.HorizontalScrollBarVisibility=Disabled` 固定（`MainWindow.xaml.cs:147` + `MainWindow.xaml:121`），不再用手动 `SizeChanged`。

### UI 工厂辅助方法（`MainWindow.Helpers.cs`）

- `Card`（`:27`）：圆角 `Border`（`CornerRadius 12`）+ `DropShadowEffect`（`Blur 16, Depth 2, Opacity .3`）包裹 `StackPanel`，统一卡片视觉。
- `Btn`（`:50`）：主/次按钮（主按钮用 `_accent` 底、次按钮用 `_btnSecondaryBg`+边框）。
- `MakeBtnRow`（`:149`）：等宽均分——`Grid` 每列 `1*`，按钮 `HorizontalAlignment=Center` 保持原始尺寸（多按钮操作行通用）。
- `MakeSearchBox`（`:320`）：`TextBox` + 🔍 图标叠放（`Grid` + `Panel.SetZIndex(icon,1)` + `IsHitTestVisible=false`），Appx / 常用软件两页复用。
- `LinkText`（`:172`）：下划线 + `Cursor.Hand` + hover 改 `Opacity`（始终保留下划线，满足 WCAG 1.4.1 非颜色依赖）。
- 另含 `SetClipboardTextWin32` / `TrySetClipboardTextAsync`（`:76-143`），绕过 WPF `Clipboard` 占用异常，提供稳定复制。

### 复杂表格 / 列表对齐工程

- **表头与数据行对齐**（`BuildCommonSoftware:3526`）：数据行 `rowGrid` 列定义（`:3904-3909`）`Auto`+`1.2*`+`0.9*`+`0.65*`+`0.55*`+`1.7*`；修复点（`:3793-3803`）表头 `hdrGrid` 列 0 放一个 `IsEnabled=false, Opacity=0` 的**占位 `CheckBox`**（同 `Margin`），否则表头 `Auto` 列宽为 0 → 后续 `Star` 列总宽与数据行不一致 → 表头错位。
- **ScrollViewer 无限宽破坏 Star 列**：关键技法为 `HorizontalScrollBarVisibility=Disabled`（如 `BuildAppxRaw:2430`；主 `ContentArea` 在 `xaml.cs:147` 固定 `Disabled`），让 `measure` 阶段传入有限宽度，`Star` 列才能正确解析；若 `Auto/Visible`，`ScrollViewer` 传无限宽使 `Star` 列塌缩。
- **ComboBox → ListBox 替换**（`Pages.cs:3588,3657`）：ComboBox 模板未 `TemplateBinding ScrollViewer.CanContentScroll`，物理滚动补丁不可靠；改用 `ToggleButton`+`Popup`+`ListBox`，`ScrollViewer.SetCanContentScroll(catList,false)` 真正生效。
- **对齐模式**：行内文本普遍 `HorizontalAlignment=Stretch` + `TextAlignment=Center`（标签用 `Label`、文本用 `TextBlock`），确保在分配列宽内真正居中，而非仅设 `Center`（控件 `Auto` 宽时无效）。

### 自定义对话框清单（含主题注入方式）

| 类 | 文件:行 | 作用 | 主题注入 |
|---|---|---|---|
| `MainWindow` | `MainWindow.xaml.cs:18` | 主窗口 | 自身 `MainWindow.xaml` 资源 |
| `InstallPathDialog` | `InstallPathDialog.cs:20` | 安装路径选择 | 内联等价模板（`:55-87`） |
| `CustomSoftwareEditDialog` | `CustomSoftwareDialog.cs:67` | 新增/编辑软件 | `DialogChrome.Apply`（`:80`） |
| `CustomSoftwareManagerDialog` | `CustomSoftwareDialog.cs:522` | 软件管理列表 | `DialogChrome.Apply`（`:531`） |
| `OtherTweaksDialog` | `OtherTweaksDialog.cs:51` | 系统功能调节 | 复制 13 个 owner 笔刷（`:60-72`） |
| `RemotePortDialog` | `OtherTweaksDialog.cs:399` | 远程端口 | `_owner._accent` 等（`:408`） |
| `Tier3ConfirmDialog` | `Tier3ConfirmDialog.cs:18` | 三级确认 | `DialogChrome.Apply`（`:26`） |
| `StoreSearchWindow` | `Pages.cs:5100` | Appx 商店搜索 | 内部 `Window` 子类 |

> 注：「维护工具」是 `MainWindow.Maint.cs` 的 `BuildMaintenanceTools`（页面，非独立窗口），其 `DataGrid` 样式见 `Maint.cs:201-226`。

### 动画与视觉打磨（诚实说明：基本静态）

- **已实现的视觉**：`DropShadowEffect` 卡片阴影（如 `Helpers.Card:39`、`InstallPathDialog.cs:96`）；错误文本淡入 `BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0,1,160ms))`（`InstallPathDialog.cs:318`、`CustomSoftwareDialog.cs:513`）；链接 hover `Opacity` 过渡。
- **类玻璃拟态**：背景图 `BgImage` 半透明（`Opacity` 深 0.55 / 浅 1.0，`Theme.cs:316/334`）+ 卡片 `_bgCard=Brushes.Transparent` 透出背景，形成半透明叠层观感（非 Win32 Acrylic 真模糊）。
- **未使用**：`Storyboard`、`RenderTransform`/`ScaleTransform`、磁性悬浮（magnetic hover）、真模糊。当前动效以 `ControlTemplate.Trigger` 瞬时状态为主，无缓动补间。

### 滚轮穿透修复

- `MainWindow.xaml.cs:113-135` 在 `ContentArea.PreviewMouseWheel` 中 `HitTest` 找到鼠标下的真实 `ScrollViewer`（含 `Popup` 独立视觉树），将滚动事件转发给它，避免嵌套滚动卡顿。

---

## 构建与部署

### 环境要求

- **编译器**: .NET SDK 6.0+ 或 Visual Studio 2022 +「.NET 桌面开发」工作负载
- **目标运行时**: .NET Framework 4.8（Windows 10/11 自带）

### 构建命令

```bash
cd src\CpqSystemTool
# 方式一：使用仓库内置便捷脚本
build.bat
# 方式二：直接使用 dotnet 命令
dotnet build -c Release
# 输出文件: bin\Release\net48\系统清理与优化工具.exe
```

### 分发

只需分发单个 `系统清理与优化工具_v1.11.exe` 文件（由构建输出 `系统清理与优化工具.exe` 按版本重命名而来）。所有资源（背景图、图标、SKU 许可令牌、源码包）均已嵌入。

---

## 开源与许可

本项目以 [Apache-2.0](LICENSE) 协议开源。

### 第三方引用

| 项目 | 许可 | 说明 |
|------|------|------|
| [Microsoft Activation Scripts (MAS)](https://massgrave.dev) | GPL v3 | 激活功能运行时远程调用官方脚本，未打包、未修改 |

详见项目内的 [NOTICE](NOTICE) 文件。

---

## 免责声明

本工具仅供学习研究与个人使用。部分功能（如系统激活、版本切换、Defender 管理、注册表优化、服务禁用等）会修改操作系统设置，请在使用前创建系统还原点，并确保您了解每项操作的影响。

激活功能涉及系统授权变更，请遵守当地法律法规及 Microsoft 许可条款。内置微软 SKU 许可令牌（`*.xrm-ms`）仅用于本机版本切换所需的证书安装。

使用本工具所产生的一切后果由使用者自行承担。

---

## 常见问题 (FAQ)

问：为什么我无法使用某些功能？

答：绝大多数修改系统设置的功能（如系统优化、服务优化、Defender 管理）需要管理员权限。请右键点击 exe，选择「以管理员身份运行」。

问：我优化了系统，但想恢复原状怎么办？

答：大部分功能都提供「一键还原」或反向操作。例如，在「系统优化」页面点击「还原所有项」，在「服务优化」页面点击「一键还原」。如果不确定，可随时使用「系统工具 → 系统还原点」进行系统恢复。

问：激活工具 (MAS) 是如何工作的，安全吗？

答：该功能是一个「启动器」。它会在您的电脑上打开一个提权后的 PowerShell 窗口，并自动执行来自 `https://get.activated.win` 的官方 MAS 脚本。本工具本身不包含、不修改任何激活代码。MAS 是一个开源项目，您可以在其 GitHub 查看源码。

问：软件下载失败怎么办？

答：「常用软件」模块的下载链接来自官方源或 Chocolatey 社区，可能受网络环境影响。您可以尝试更换网络环境，或检查软件的下载地址是否仍然有效。对于部分软件，工具也提供了「官方下载页解析」作为兜底策略。

问：如何备份和迁移我的设置？

答：将旧电脑 `Config\` 文件夹下的 `autosave.json` 及配置备份文件复制到新电脑 exe 同目录下的 `Config\` 文件夹中，即可恢复所有设置。

---

## 联系与反馈

- **项目主页**: [https://github.com/dandelion80231/System-Cleanup-Optimizer](https://github.com/dandelion80231/System-Cleanup-Optimizer)
- **联系邮箱**: dandelion8023@365ms.cc
- **抖音号**: [1142736528](https://www.douyin.com/user/MS4wLjABAAAAK7pMpJ1pN-NvaDUQgDP8ytHUgzvRh61mM-M6TLwk5X0)

如有问题或建议，欢迎通过 GitHub Issues 反馈。
