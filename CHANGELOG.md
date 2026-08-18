# 更新日志 (CHANGELOG)

本项目所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/)。

---

## [v1.10] - 2026-08-18

> 相对 v1.09 的源码变更：新增「内存工具」导航页（镜像 RAMMap 只读视图 + 可选内存优化），置于「系统工具」之下；例行版本提升 v1.09 → v1.10。

### ✨ 新增
- **内存工具页（镜像 RAMMap 只读视图）**：左侧导航新增「内存工具」（🧠，挂在「系统工具」之下），纯代码构建独立页面，分三层——
  - **A 内存总览（只读）**：`GlobalMemoryStatusEx` + `GetPerformanceInfo`（均为 Windows 文档化 API）展示总/可用物理内存、内存占用百分比、已提交/提交上限、内核分页/非分页池。
  - **B 内存使用拆解（只读）**：`WMI Win32_PerfFormattedData_PerfOS_Memory`（文档化计数器）把物理内存拆为「使用中 / 备用 / 已修改 / 空闲+零页」四类占比条 + 图例（含字节数与百分数）；并展示可用/系统缓存/已提交/提交上限/分页池/非分页池明细，下方列出进程工作集 Top 10（`GetProcessMemoryInfo` + `EnumProcesses`）。
  - **C 内存优化（默认收起 · 中风险 · 仅管理员）**：`Expander` 默认折叠，仅供管理员启用；提供「清空备用列表(Standby)」「清空所有进程工作集」两项——前者调 `NtSetSystemInformation(MemoryPurgeStandbyList=2)` 清 Standby，后者逐进程 `EmptyWorkingSet`；均带 `SeProfileSingleProcessPrivilege` 提权与风险说明（优化为临时效果，用缓存/工作集换即时空闲内存）。
- **内存采集模块 `Modules/MemoryAnalyzer.cs`**：封装全部 P/Invoke（kernel32/psapi/ntdll）与 WMI 查询逻辑，全程 `try/catch` 优雅降级（WMI 不可用时拆解数据单独提示不可用，总览数据仍可用）。

### ♻️ 变更 / 策略
- **设计为「文档化 API 优先、避免未文档化结构体偏移」**：内存拆解刻意改用 WMI 文档化性能计数器还原 RAMMap 视图，规避 `NtQuerySystemInformation(0x32)` 未文档化结构体偏移猜错导致静默假数据的风险；仅优化层（Standby 清理）使用经验证权威常量 `MemoryPurgeStandbyList=2`（网上部分资料误写为 3/4）。
- **版本提升 v1.09 → v1.10**：同步 csproj（1.0.10.0 ×3）/ `APP_VERSION`（`v1.10`）/ 交付文件名 `系统清理与优化工具_v1.10.exe`。

### 🐞 修复
- **内存工具卡片 A 布局**：总览 6 个统计块由 `WrapPanel` 改为 2 行 × 3 列网格，占满页面宽度（不再随窗口宽度换行错落）。
- **内存工具卡片 B 数据可靠性**：`WMI Win32_PerfFormattedData_PerfOS_Memory` 首次查询常返回全 0（计数器尚未「cook」），`GetUseCounts` 增加一次重试（+80ms），修复占比条闪一下即消失、拆解全显示 0 B 的问题；取数仍不可用时不再把占比条收缩为 0 宽度（改为整条灰色占位 + 文字提示），避免「消失」观感；提交上限在 WMI `CommitLimitBytes` 返回空时回退到 `GetPerformanceInfo` 的可靠值。

---

## [v1.09] - 2026-08-18

> 相对 v1.08 的源码变更：新增 Whesvc 诊断日志清理项与服务禁用项；例行版本提升 v1.08 → v1.09。

### ✨ 新增
- **Whesvc 诊断日志清理**：在「系统文件」清理类新增 `Whesvc 诊断日志` 项（默认不勾选），清理 `C:\Windows\Temp\DiagOutputDir\Whesvc` 下 Windows 健康状况和优化体验服务生成本地性能追踪 ETL 日志。该日志可安全删除、服务重新启用时会再生；服务运行时文件被占用会自动跳过。
- **服务项优化新增 `whesvc`**：在可禁用服务清单新增「Windows 健康状况和优化体验」，风险等级 `mid`，说明注明「本地性能诊断日志(占C盘)，关掉无性能提升、笔记本可能影响节能」。

### ♻️ 变更 / 策略
- **版本提升 v1.08 → v1.09**：同步 csproj（1.0.9.0 ×3）/ `APP_VERSION`（`v1.09`）/ 交付文件名 `系统清理与优化工具_v1.09.exe`。

---

## [v1.08] - 2026-08-17

> 相对 v1.07 的源码变更：关于页新增官网地址链接，检查更新改为从官网 version.json 获取新版本，官网安装包统一改为中文名。

### ✨ 新增
- **关于页新增官网地址**：在「开发者与协议」卡片新增 `官网：cpq-system-tool.pages.dev` 链接，指向 https://cpq-system-tool.pages.dev/。

### ♻️ 变更 / 策略
- **检查更新改为从官网 version.json 获取新版本**：原检查更新从 GitHub Releases API 拉取 `tag_name`，现改为读取官网根 `version.json` 的 `version`/`name`/`url` 字段，普通用户无需访问 GitHub、下载更快；版本比较与「下载更新」弹窗逻辑保持不变。
- **官网安装包统一改为中文名**：官网托管与下载页全部 exe 由 `System-Cleanup-Optimizer_vX.XX.exe` 改为 `系统清理与优化工具_vX.XX.exe`（v1.01–v1.08），`version.json` 的 `name`/`url` 同步使用中文名；GitHub Release 资产保留英文名 `System-Cleanup-Optimizer_v1.08.exe`（规避 gh 中文文件名截断）。
- **版本提升 v1.07 → v1.08**：同步 csproj（1.0.8.0 ×3）/ `APP_VERSION`（`v1.08`）/ 交付文件名 `系统清理与优化工具_v1.08.exe`。

---

## [v1.07] - 2026-08-17

> 相对 v1.06 的源码变更：完成全部下拉框（ComboBox）深/浅色主题统一与自定义下拉 Popup 层级修复，修复「安装到」按钮主题自适应，并例行版本提升 v1.06 → v1.07。

### 🐛 修复
- **「安装到」按钮背景/字体色随主题切换**：自定义安装路径态此前硬编码浅薄荷背景 `Color.FromRgb(0xE6,0xF7,0xF4)`，深色模式下始终不变；现改为主题笔刷 `_btnSecondaryBg` + `_accent` 高亮文字/边框，与默认态均随深/浅色自动变换。
- **修复自定义下拉「浮到最顶层」**：「管理依赖」「全部分类」两个 Popup（AllowsTransparency=true 会以独立顶层 HWND 带 WS_EX_TOPMOST 渲染）在打开时剥离 WS_EX_TOPMOST，并挂 HwndSource Hook 在 WM_WINDOWPOSCHANGED 时持续剥离，使其落到正常层级、 不再压在所有窗口（含其他应用）之上（`UiShapes.DisablePopupTopmost`）。

### ♻️ 变更 / 策略
- **版本提升 v1.06 → v1.07**：同步 csproj（1.0.7.0 ×3）/ `APP_VERSION`（`v1.07`）/ 交付文件名 `系统清理与优化工具_v1.07.exe`。
- **统一全部 ComboBox 深/浅色自适应**：新增 `UiShapes.ApplyComboBoxTheme`，以自定义 ControlTemplate（闭合框 + 下拉弹层均引用主题键）+ ComboBoxItem 样式，让 7 个 ComboBox（版本切换目标 / Office 版本 / Edge 频道 / 驱动引擎 / 分组 / 软件分类 / 风险等级）的背景、字体、边框与下拉弹层（含选中/悬浮态）统一跟随深/浅色主题笔刷，替代默认跟随系统色的 Aero2 模板（深模式下弹层为刺眼白底）；弹层刻意关闭 AllowsTransparency 以复用默认 ComboBox 的非置顶行为，避免重新引入浮层问题。

---

## [v1.06] - 2026-08-16

> 相对 v1.05 的源码变更：WebView2 浏览器探针依赖改为「运行时从 NuGet 拉取」兜底（摆脱 Costura 嵌入）；全部实心箭头改为开放折线 chevron（抽出 UiShapes 共享）；修复清理优化页分组 Expander 标题丢失与箭头错位；若干健壮性修复 + 版本提升至 v1.06。

### ✨ 新增
- **WebView2 探针依赖运行时下载（兜底）**：新增 `Modules/WebView2ProbeDeps.cs`。单文件/裸 exe 分发到其他机器缺失 3 个托管 WebView2 DLL 时，运行时从 NuGet 拉取 `Microsoft.Web.WebView2 1.0.2045.28`（3 托管 + 原生 Loader）到 exe 目录；幂等（sentinel=`Core.dll`）、不抛异常（失败仅记录日志、探针随后回退 Node+Playwright）、后台下载不阻塞 UI。挂钩 `EdgeCore` 安装/修复、`ProbeBrowserHost` 初始化、`RunProbeInternal` 共 4 处。

### ♻️ 变更 / 策略
- **版本提升 v1.05 → v1.06**：同步 csproj（1.0.6.0 ×3）/ `APP_VERSION`（`v1.06`）/ 交付文件名 `系统清理与优化工具_v1.06.exe`。
- **全量箭头线条化**：实心三角 ▲▼◄► / Path 填充改为开放折线 chevron（`Fill=Transparent` + `Stroke` + 圆角线帽、无 `Z`）；抽出 `UiShapes.MakeChevron`（真实 Path）与 `ConfigureChevronFactory`（ControlTemplate 工厂）消除 4 处重复；排序箭头方向语义纠正为「升=上、降=下」。
- **下载路径收敛**：`EdgeCore.RepairWebView2` 安装器下载路径由桌面改为 `AppDomain.CurrentDomain.BaseDirectory`（exe 目录）。

### 🐛 修复
- **清理优化页分组 Expander 标题丢失 + 箭头错位**：`MakeLineArrowExpander` 未把 `Expander.Header` 绑到 `HeaderSite.Content`，导致模板内 `ContentPresenter` 无内容、标题整体空白；并修正 Grid 列序——箭头置于第 0 列（左侧、居中）、标题置于第 1 列（居左）。折叠时箭头仍朝右。
- **ProbeBrowserHost STA 线程崩溃守卫**：STA lambda 体整体 try/catch，异常写入 `_initError` 并以 `TrySetResult(false)` 完成，避免缺失 WebView2 程序集时进程崩溃（探针正常回退 Node 提示）；新增 `webview2_deps.log` 诊断下载成败。
- **BOM 合规**：新增 `UiShapes.cs` / `WebView2ProbeDeps.cs` 补全 UTF-8 BOM（项目硬性约定）。

### ♻️ 质量打磨（行为保持）
- 抽出 `AppendOrReplaceLog`（原地百分比进度重写）、`repositionDepsPopup`（主窗口拖动时下拉跟随）等局部优化。

### 🔧 发布后跟进（v1.06 即时修补）
- **WebView2 探针依赖下载改为 API 自身异步卸载**：`WebView2ProbeDeps.EnsureWebView2ProbeDeps` 重构为 `EnsureWebView2ProbeDepsAsync`（真正异步、下载走线程池、可 await），`ProbeBrowserHost.InitAsync` 与 `CheckWebView2ReadyAsync` 改为直接 `await`，不再依赖「调用方用 Task.Run 包裹」的约定来避免 UI 冻结；保留同步兼容包装供后台线程（RunInBg）调用方使用，全程 `ConfigureAwait(false)` 无死锁风险。
- **抽取 `UiShapes.MakeTextWithArrowGrid`**：消除「管理依赖」「全部分类」两处下拉按钮重复的「文字 + 右侧箭头」2 列 Grid 构造，统一由共享方法生成（布局与原先完全一致）。

---

## [v1.05] - 2026-08-14

> 相对 v1.04 的源码变更：新增「驱动清理」模块（参考 Driver Store Explorer / RAPR 界面与行为设计，基于 Windows 原生 API 独立实现）+ 多项交互与体验增强 + 版本提升 v1.04 → v1.05。

### ✨ 新增
- **驱动清理模块（参考 RAPR / Driver Store Explorer 设计，基于 Windows 原生 API 独立实现）**：左侧导航新增独立「驱动清理」页，提供——
  - **枚举已装驱动包**：调用 `pnputil /enum-drivers` 解析 OEM 驱动列表（供应商、类、版本、日期、发布名、原始 inf 名、占用空间）。
  - **在役保护**：通过 WMI `Win32_PnPSignedDriver` 比对 `InfName` 映射，标记当前在用的驱动，默认不可删。
  - **旧版冗余识别**：同系列驱动仅保留最新版受保护，其余标记为可清理旧版。
  - **删除冗余驱动**：默认不带 `/force`，仅删未在役的旧版 `oem#.inf`；删除前二次 MessageBox 确认（危险操作红色提示）。
  - **导出备份驱动**：可选目录将选中（或未选则全部）驱动导出到指定文件夹，便于回滚。
- **设备名称补全**：SetupAPI 枚举在役设备 + WMI `Win32_PnPSignedDriver`/`Win32_PnPEntity` 双键匹配；仍无匹配时自动用 `Provider + ClassDescription` 兜底，避免设备名列大量空白。
- **列头三态排序**：驱动列表支持点击列头循环排序（无→升序→降序→无），排序方向 ▲/▼ 实时显示；按 backing 属性排序（日期/大小/版本等），而非显示字符串。
- **预加载与自动刷新**：启动后在后台预加载驱动列表；每次进入「驱动清理」页都自动后台刷新，进入即见已加载数据，无需手动点「刷新」。
- **pnputil 输出容错解析**：兼容 Beta UTF-8 控制台下中文标签挤行（「WHCP 版本: 未知发布名称: oemX.inf」），采用「标签定位 + 截断到下一标签」分词，中英文双标签兜底。
- **驱动管理能力增强**：支持**添加驱动包**（AddDriverDialog）/ **安装选中驱动**；PnP 实用工具（`pnputil`）与 **DISM 系统映像**双后端切换（DISM 含系统内置驱动）；按**类别 / 供应商**分组；**启动关键驱动默认保护**（避免误删导致无法启动）。

### ♻️ 变更 / 策略
- **版本提升 v1.04 → v1.05**：同步 csproj（1.0.5.0）×3 / `APP_VERSION`（`v1.05`）/ 交付文件名 `系统清理与优化工具_v1.05.exe` / 关于页更新日志。
- **UI 统一**：驱动清理页下拉框回归默认白底黑字样式，与首页 Office 风格一致；页面最大化时自动撑满视口；单元格支持鼠标悬停跟随提示；运行日志框与 DataGrid 各自独立滚动，避免外层整页滚动。

### 🐛 修复
- **PrivacyCore**：PowerShell 中 `%SystemRoot%` 不被展开，改为 `$env:SystemRoot`。
- **Activation**：系统激活状态判定由严格整行匹配改为子串包含 `---LICENSED---`（兼容 `cscript /dstatusall` 输出左侧空格差异）。
- **ConfigBackup**：`MiniJsonParser` 的 `\u` 转义越界保护，避免超长转义截断崩溃。
- **RestorePoint**：还原前用 `Get-ComputerRestorePoint` 校验序号是否存在，避免无效序号静默谎报成功；引号处理统一改用 `Exec.QuotePS`。
- **Updater / EdgeCore**：Updater 引号处理统一 `Exec.QuotePS`；EdgeCore 的 WebView2 卸载由 cmd 通配改为 C# 枚举版本目录、逐个 `setup.exe --uninstall`（修复通配被 cmd 忽略导致卸载静默 no-op）。
- **MeteredConnection**：`SetDacl` 原生内存泄漏修复，所有 `AllocHGlobal` 在 `finally` 统一释放。
- **VersionSwitch**：`MapChineseToEnglish` 死代码（中文键里搜英文子串恒为 false）改为 5 条直接回退（如「专业工作站」→ `ProfessionalWorkstation`）。
- **ProbeBrowserHost**：`NavigateAndWaitAsync` 增加 20s 超时，避免永久挂起。
- **MainWindow.Maint**：删除死诊断 `depsDiag` 与未用字段 `_depOutsideClick`（消除 CS0169 警告）。

### ♻️ 质量打磨（行为保持）
- **DriverStorePanel**：删除 4 个声明赋值后从未读取的死画刷字段；强制删除按钮去硬编码红边，改用主题画刷 `_dangerDark`。
- **SoftwareInstall**：安装器等待逻辑加入心跳日志（每 10 秒输出一次进度），不加重试、保持原有超时/Kill/退出码行为。

### 📄 文档 / 合规
- **驱动清理模块许可证澄清**：上游 Driver Store Explorer（RAPR）实际采用 GPL v2 许可（与本项目 Apache-2.0 不兼容）。经核查本仓库未包含其任何源代码，驱动清理为基于 Windows 原生 `SetupAPI` / `PnPUtil` / `DISM` 的独立实现，仅界面列布局与行为设计受其启发。已将 README / NOTICE 措辞修正为「参考 / 独立实现」，并在 NOTICE 补充「第三方来源与致谢」（非隶属、非衍生声明）。本澄清不影响 Apache-2.0 许可合规性。

---

## [v1.04] - 2026-08-12

> 相对 v1.03 的源码变更：Edge 组策略双 hive 修复 + WYSIWYG 应用策略 + 清理降级 + 更新下载代理回退（详见 code-review 全量检查）。

### 🐛 修复
- **Edge 优化「恢复不掉」根因修复（两层）**：① `Tweaks.cs` 前 4 个 Edge 优化项（欢迎页/标签页性能检测/新标签页资讯/个性化广告）的 `Disable` 原误写成「设值 0/1」而非「删除策略值」，Edge 检测到有值即视为「由组织管理」而恢复不掉；改为 `DeleteEdgePolicy` 彻底删除。② `ApplyChecked` 原只对勾选项做启用、不处理取消勾选项，导致「取消勾选 + 开始优化」什么都不做；改为 WYSIWYG 全量纳入。
- **Edge 组策略双 hive 彻底清除**：`RegistryHelper` 新增 `EdgePolicyHives`（HKCU + HKLM）辅助块（`SetEdgePolicy` / `SetEdgePolicyRecommended` / `DeleteEdgePolicy` / `DeleteEdgePolicyRecommended` / `DeleteEdgePolicyTree` / `GetEdgePolicyState` / `GetEdgePolicyRecommendedState`）。仅清 HKLM 会因 HKCU 残留而清不掉「由组织管理」状态，现统一双 hive 操作。`EdgeCore` 的 `BlockEdgeUpdate` / `RestoreEdgeUpdate` / `SetStartupBoost` / `IsStartupBoostEnabled` 同步迁移到双 hive。`DeleteKeyTree` 增加「键不存在即视为成功」前置判断，避免 HKCU 无 Edge 键时的误报 `[!] 删键失败`。
- **系统信息版本显示本地化**：`Modules/SystemInfo.cs` 按 `CurrentBuild` 判断 Windows 代际（11/10/8.1/8/7），并将 `EditionID` 与 `ProductName` 中的版本片段映射为中文（如 `ProfessionalWorkstation` / `Pro for Workstations` → "专业工作站版"）；原英文 `Build:` 改为中文 `版本号：`。映射表已覆盖 Windows 10/11 全部主流版本（含 N / S(LTSC) / S 模式 / IoT 企业版 / 服务器 / 多会话），并按键长降序匹配避免短片段误命中。**映射缺失时优雅降级为英文原文（EditionID 或 ProductName 去 "Windows X " 前缀后的片段），绝不报错。**

### ♻️ 变更 / 策略
- **WYSIWYG 应用策略（勾选即优化、取消即还原）**：底部「开始优化」按钮按当前勾选状态应用【所有】项——勾选=启用优化(On)，取消=还原系统默认(Off)；三态项的不确定=交还系统默认(Default)。因此「取消勾选 + 开始优化」即可单独还原某项，无需动用「还原所有项」误伤其它优化项。顶部说明文案同步更新。
- **Edge 优化首次勾选提示**：首次勾选「Edge优化」组任一项时弹 YesNo 提示，说明组策略副作用（edge://management 显示「由你的组织管理」为固有表现，非故障），本次会话仅提示一次。
- **清理兜底降级**：`Cleanup.cs` 删除失败改用 `Exec.RunPowerShellGetFull` 捕获 stderr/exitCode；「文件正被另一进程使用」属预期（程序运行中），降级为安静 `[SKIP]` 提示，不再刷 `[PS-ERR]` 噪声；其余真实错误仍如实暴露。
- **版本切换下拉默认本机版本**：`vsTargetCombo` 默认选中当前系统 `EditionID`，不再固定首项。
- **更新下载代理回退增强**：`DownloadStringWithProxyFallback` / `DownloadFileWithProxyFallback` 依次尝试 系统代理 → 直连 → 本地常见回环代理端口 三层自动回退；`DownloadUpdate` 改为 `async/await` 替代原 `Task.Run` + `while(IsBusy) Sleep` 忙等轮询。
- **版本号规范化（恢复两段式）**：v1.03 → v1.04，**统一回到本项目两段式惯例 `vX.YY`**（历史 v1.01 / v1.02 / v1.03 均为两段；上一版误用三段式 `v1.0.4` 导致「检查更新」段位错位误报「已高于线上」）。同步 csproj（1.0.4.0，内部程序集版本保持不变）/ `APP_VERSION`（`v1.04`）；交付文件名 `系统清理与优化工具_v1.04.exe`。`CompareVersion` 保留 `NormalizeVersion` 防御层（两段且第二段≤9 自动补 0），杜绝日后混用。

---

## [v1.03] - 2026-08-11

> 相对 v1.02 的源码变更：三遍代码审查发现项修复 + 代码清理（详见 code-fix-2026-08-11.md）。

### 🐛 修复
- **安全加固（信任边界）**：MAS 激活改走系统目录完整路径 `powershell.exe` + `-EncodedCommand`，消除 PATH 劫持风险；Chocolatey OData 过滤的 `id` 加白名单 `^[A-Za-z0-9.\-]+$` 校验；Office 部署 XML 中 `pid`/`channel` 用户值用 `SecurityElement.Escape` 转义；WebView2 引导程序与 Office 安装包下载加存在性与非空校验，避免对损坏文件静默执行。
- **健壮性**：维护工具依赖检测异常由空 `catch{}` 改为 `Debug.WriteLine` 可见日志，不再把检测异常误报为 Node 未安装；`ProbeBrowserHost` 同步异常路径经 `TaskCompletionSource` 传播，避免调用方 `.GetAwaiter().GetResult()` 永久挂起。

### 🔧 变更 / 清理
- **Dialog 脚手架复用**：标题栏与错误提示公共逻辑提取到 `DialogChrome`（新增 `ShowError` / `BuildTitleBar`），3 个 Dialog 复用，减少重复；删除 `Theme.cs` 冗余 `Debug.WriteLine`、死文档注释、冗余列宽赋值。
- **Tier3 勾选大小求和修复**：原写法在非连续勾选时会错位，改为索引对齐配对遍历。
- **注册表 Dword 读取模板统一**：多处 `try{OpenSubKey...is int v}` 模板收敛为 `RegistryHelper.GetDwordState`。
- **关于页更新体验增强**：检测到新版本后新增「下载更新」按钮，支持自选保存路径下载新 exe；更新日志区域增加 `ScrollViewer` 并限制最大高度，避免版本增多后卡片无限拉长。
- **更新下载健壮性修复**：下载直链改由 GitHub API 返回的 `browser_download_url` 取得（避免本地拼中文资产名与线上实际英文名不一致导致 404）；下载增加系统代理 → 直连 → `127.0.0.1:26561` 三层自动回退，解决无代理环境下连接 GitHub CDN 失败的问题。
- **版本号规范化**：v1.02 → v1.03，同步 6 处。

---

## [v1.02] - 2026-08-11

> 相对 v1.01 的源码变更面：14 个文件修改（+1093 / −667 行），新增 2 个源码模块与 1 个构建资源。

### 🐛 修复
- **WebView2 探针初始化 20s 超时（终极根因修复）**：死锁点位于 `SetupCdp()` 在 UI/STA 线程上**同步阻塞**等待 CDP 响应，而响应需经同一线程消息循环回派 → 永久死锁 → 超时。改为 `async Task SetupCdpAsync()` + `await`，端到端验证 `SearchAsync("7-zip")` 返回真实直链成功。
- **qq 抓取候选列表噪音**：原列表会出现 `SKIP`（非安装包 `.js`）与 `404` 死链。在 `ProbeEngine.BuildRows` 增加 `SKIP` / `404` 过滤，列表只展示可用的真实 exe 直链。
- **PowerShell 调用统一化**：Tweaks / RestorePoint / OtherTweaksDialog / EdgeCore / Theme / Activation 等模块的 `powershell -Command` 调用统一迁移到 `Exec.RunPowerShell/RunPowerShellGet`（底层 `-EncodedCommand` Base64 Unicode），消除引号/中文路径乱码与命令注入风险。
- **`.gitignore` 行内注释 bug**：git 不支持行内注释（仅行首 `#` 生效），`runtimes/  # 注释` 整行被当模式导致规则失效；更严重的是 `!src/CpqSystemTool/src.zip  # 注释` 取反失效会让构建必需的 `src.zip` 被 `*.zip` 错误忽略（克隆后报 CS1566）。改为独立 `#` 行后恢复正确忽略。

### ✨ 新增
- **WebView2 官方 exe 直链探针模块**：`Modules/ProbeBrowserHost.cs`（独立 STA 线程 + WinForms 承载 WebView2，复用系统 Edge Runtime，主动扫描 `msedgewebview2.exe` 绕过损坏注册表）与 `Modules/ProbeEngine.cs`（参考 official_exe_finder.js 的纯逻辑引擎，独立实现）。
- **依赖管理 UI 增强**（`MainWindow.Maint.cs`，+363 行）：Node + Playwright + Chromium 回退路径的安装 / 卸载与状态刷新；`IsNodeDepsReady` 收口为统一就绪判定。

### 🔧 变更
- **版本号规范化**：v1.01 → v1.02，同步 6 处（csproj 版本号 / app.manifest / `APP_VERSION` / 关于页更新日志 / README / build.bat）。
- **交付文件名带版本号**：`系统清理与优化工具_v1.02.exe`（保留 AssemblyName=中文名，仅部署文件名带版本，避免资源 URI 连锁修改）。
- **代码审查确认**：Node + Playwright + Chromium 回退路径代码健康可用（本轮无代码改动，仅验证）。

---

## [v1.01] - 2026-08-06

- 初始版本发布。完成系统清理、优化与维护核心功能：
  - 系统优化（一键/按需调校，操作前可创建还原点）
  - 清理优化（6 大类 34 项细粒度清理，先扫描后清理）
  - 服务优化、Appx 商店管理、常用软件官方直链下载
  - 安全防护（安全中心 / 防火墙 / Defender）、Edge 管理、隐私设置
  - 系统工具、激活工具（MAS）、系统信息、维护工具、配置管理
- 详见 `README.md`。
