# 更新日志 (CHANGELOG)

本项目所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/)。

---

## [v1.05] - 2026-08-13

> 相对 v1.04 的源码变更：新增「驱动管理」模块（移植 Driver Store Explorer / RAPR 核心能力）+ 版本提升 v1.04 → v1.05。

### ✨ 新增
- **驱动管理模块（移植 RAPR / Driver Store Explorer 核心能力）**：维护工具页新增「驱动管理」入口，打开独立对话框，提供——
  - **枚举已装驱动包**：调用 `pnputil /enum-drivers` 解析 OEM 驱动列表（供应商、类、版本、日期、发布名、原始 inf 名、占用空间）。
  - **在役保护**：通过 WMI `Win32_PnPSignedDriver` 比对 `InfName` 映射，标记当前在用的驱动（红色高亮），默认不可删。
  - **旧版冗余识别**：同系列驱动仅保留最新版受保护（橙色高亮），其余标记为可清理旧版。
  - **删除冗余驱动**：默认不带 `/force`，仅删未在役的旧版 `oem#.inf`；删除前二次 MessageBox 确认（危险操作红色提示）。
  - **导出备份驱动**：可选目录将选中（或未选则全部）驱动导出到指定文件夹，便于回滚。
- **pnputil 输出容错解析**：兼容 Beta UTF-8 控制台下中文标签挤行（「WHCP 版本: 未知发布名称: oemX.inf」），采用「标签定位 + 截断到下一标签」分词，中英文双标签兜底。

### ♻️ 变更 / 策略
- **版本提升 v1.04 → v1.05**：同步 csproj（1.0.5.0）×3 / `APP_VERSION`（`v1.05`）/ 交付文件名 `系统清理与优化工具_v1.05.exe` / 关于页更新日志。

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
- **WebView2 官方 exe 直链探针模块**：`Modules/ProbeBrowserHost.cs`（独立 STA 线程 + WinForms 承载 WebView2，复用系统 Edge Runtime，主动扫描 `msedgewebview2.exe` 绕过损坏注册表）与 `Modules/ProbeEngine.cs`（移植 official_exe_finder.js 的纯逻辑引擎）。
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
