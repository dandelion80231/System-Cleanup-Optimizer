# 更新日志 (CHANGELOG)

本项目所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/)。

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
