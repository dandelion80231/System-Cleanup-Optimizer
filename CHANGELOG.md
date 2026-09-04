# 更新日志 (CHANGELOG)

本项目所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/)。


## [v1.18] - 2026-08-31

> 相对 v1.17 的源码变更（3 提交，1 文件，+43 / −18 行）：修复 Geek Uninstaller 下载卡死、优化便携版安装路径、改用 ZIP 加速下载；并规范 Release 资产（禁止上传 src.zip）。

### 🐛 修复
- **Geek Uninstaller 下载卡死修复（SoftwareInstall）**：为 `SoftwareInstall.cs` 新增 `ReadTimeoutMs` / `DownloadTimeout` 可配置字段，Geek 配置总超时 900s + 读空闲 120s，应对极慢速服务器（~12 KB/s）反复超时失败。
- **便携版路径优化**：默认安装路径由 `%LOCALAPPDATA%\CpqSystemTool\Portable\{id}\` 改为桌面根目录，打开即可见；同步更新 `KnownExePaths` 检测路径。
- **Geek 下载加速**：改用官方 ZIP 包（3.2 MB）替代裸 EXE（7.5 MB），下载时间减少约 60%，并加 SHA256 校验（来源 Chocolatey 官方 checksum），超时由 900s 调为 120s。

### ♻️ 项目卫生
- **禁止上传 src.zip 到 Release**：`src.zip` 为内嵌资源（供「导出源码」使用），不是 Release 资产；Release 仅上传 exe + README.md。


## [v1.17] - 2026-08-30

> 相对 v1.16.1 的源码变更（15 个提交，73 个文件，+8163 / −1913 行）：背景编辑器大规模迭代（HSV 色轮性能优化、颜色格式显示、对比度检查、网格光斑拖拽）、配置管理页重构（导出源码功能）、探针工程重构（独立 HttpClient、TLS 1.2/1.3、UA 池轮换）、App.xaml 异常处理前置、src.zip 防呆机制。

### ✨ 新增
- **背景编辑器功能扩展（v1.17）**：
  - **HSV 色轮性能优化**：复用 WriteableBitmap 与其像素数组，避免每次重绘都全量分配；拖明度/取色一帧内可能触发多次 `RenderColorWheel`，用 `BeginInvoke` + 标志位把多次请求压缩成一次真实渲染；明度变化才重算像素，拖色轮改色相/饱和度时只需移动指示器。
  - **颜色格式显示**：新增 RGB / HSL / HSV / CMYK 四格式只读显示，每行标签+数值+复制按钮，Star 行均匀铺开占满剩余空间。
  - **对比度检查提示**：右侧底部显示当前颜色与背景的对比度比值，辅助用户选择合适的深色/浅色组合。
  - **图片模式双列布局**：左列控件（标题 + 恢复默认 + 深色/浅色选择 + 独立透明度），右列随主题显示的大预览图，最小高度 320px。
  - **网格渐变光斑拖拽交互**：新增 `_blobHandles` 字典映射预览区光斑句柄，支持拖拽移动光斑位置；每模式最近一次的几何参数存回旧 mode、切回时从字典读回，避免 Linear 90° 切 Radial 后 90° 丢失。
  - **线性/径向渐变几何参数记忆**：`_angleByMode` / `_centerXByMode` / `_centerYByMode` 三个字典，模式切换时把当前值存回旧 mode、切回时从字典读回。
  - **关闭时撤销机制改进**：根据是否编辑过/应用过决定回滚目标——若已"应用"后又编辑，回到最近一次 Apply 的快照；否则回到打开前。
  - **HEX 输入校验优化**：支持 `#RGB` / `#RRGGBB` / `#RRGGBBAA` 三种格式，`'#'` 可省略；防止中间态输入被误解析。
  - **明度滑块宽度调整**：从 180px 改为 150px，与整体布局更协调。

### 🐛 修复
- **探针工程大规模重构（v1.17）**：
  - **静态 HttpClient 连接池超时修复**：`ProbeSiteFastAsync` 由共享静态 HttpClient 改为每次调用创建独立实例（`using var client = new HttpClient(handler)`），彻底解决间歇性 `TaskCanceledException`。
  - **TLS 1.2/1.3 显式启用**：`ServicePointManager.SecurityProtocol |= Tls12 | Tls13`，.NET Framework 4.8 默认仅 TLS 1.0/1.1，现代 HTTPS 服务器握手失败误报为"无法连接"。
  - **UA 池轮换**：5 条 Chrome/Edge UA 池按静态计数器轮换，避免固定单一 UA 被 WAF/CDN 识别为 bot。
  - **VendorMap 域名反向匹配兜底**：用户输入官网首页 URL 时，快速路径 + 浏览器渲染均无法提取直链，此时若域名命中 VendorMap，直接用已验证的直链作为兜底（如 `geekuninstaller.com` → `geek.exe`）。
  - **快速路径支持压缩包格式**：新增 `PackageUrlRe` 正则匹配 `.zip/.7z/.rar`，通用修复下载页直链提取问题。
  - **代理回退三层策略**：系统代理 → 直连 → Watt Toolkit 本地代理（127.0.0.1:26561）依次尝试，最多重试 3 次，间隔 5 秒。
- **App.xaml 异常处理前置（v1.17）**：移除 `StartupUri="MainWindow.xaml"`，改由 `App.xaml.cs` 的 `OnStartup` 手动创建并显示主窗口；`DispatcherUnhandledException` handler 挂载在 `base.OnStartup(e)` 之前，保证构造期异常能被捕获并写 crash.log。
- **单实例 Mutex 释放修复**：新增 `OnExit` 覆写，显式调用 `ReleaseSingleInstanceMutex()` 释放并销毁 Mutex，避免异常退出/热重启场景下互斥量残留导致新实例误判为"已有实例"。
- **BackgroundSettings 颜色解析修复**：修复 `#RGBA` 4 位格式展开顺序错误（原注释说 ARGB 但实际展开为 RRGGBBAA，与 CSS `#RRGGBBAA` 8 位分支的读取顺序不一致）；Offset 越界钳制到 `[0,1]`，避免 `new GradientStop(...)` 抛 `ArgumentException`。
- **背景设置 JSON 反序列化健壮性提升**：原 `catch { }` 静默吞掉一切异常，脏 JSON 只表现为「设置莫名回到默认值」且无从排查；改为 `Debug.WriteLine` 输出带上下文的诊断信息。

### 🔧 变更 / 策略
- **配置管理页重构（v1.17）**：
  - **导出源码功能**：新增「📦 导出源码」按钮，点击后弹出 FolderBrowserDialog 选择保存目录，从程序集嵌入资源读取 `src.zip`，解压到 `系统清理与优化工具_源码` 文件夹。
  - **背景图设置优化**：路径输入框+浏览按钮+应用按钮同一行；恢复默认背景按钮移至右上角；提示文字缩短。
  - **预览区裁剪优化**：`bgCard` 和 `logClip` 都加 `ClipToBounds = true`，防止最大化时子内容溢出 + 缩小时残留大尺寸渲染缓存。
  - **日志框固定高度**：改为 60px 固定高度贴底，不再占用 Star 行，避免透明空白区。
- **src.zip 防呆机制（v1.17）**：在 `CpqSystemTool.csproj` 新增 MSBuild 任务 `CheckSrcZipFreshness`，构建前比对 src.zip 与最新源文件的时间戳；过期则报 warning，借本项目「交付构建必须 0 warning」的门禁，强制在构建前先重生成 `src.zip`。
- **主题笔刷冻结优化（v1.17）**：新增 `ThemeBrush(Color c)` 私有方法，创建后调用 `Freeze()`，避免 WPF 为维护 Freezable 变更订阅/失效传播带来的额外开销。
- **探针请求头优化**：`Accept-Encoding: gzip, deflate`（手动解压）；固定 UA → 5 条 UA 池轮换；补 `Accept-Language` 与同源 Referer。
- **探针性能：静态 Regex**：`Classify` 方法中的正则提为静态只读（`ReExe`/`ReX64`/`ReNoX64`/`ReArm64`/`ReX86`/`Denylist`），避免每次调用都重新构造 + JIT 编译。

### ♻️ 质量打磨
- **页面整页缓存铺开**：9 个高频页面（常用软件 / Appx 商店 / Appx 管理 / 系统优化 / 服务优化 / 安全防护 / 清理优化 / 内存工具 / 配置管理）整页实例缓存，二次进页不再全量重建。
- **日志框行数上限**：超过 3000 行自动裁剪头部，长任务运行不再内存/渲染无限膨胀。
- **全量箭头线条化**：实心三角 ▲▼◄► / Path 填充改为开放折线 chevron，抽出 `UiShapes.MakeChevron` 共享。
- **PowerShell 调用统一化**：Tweaks / RestorePoint / OtherTweaksDialog / EdgeCore / Theme / Activation 等模块统一迁移到 `Exec.RunPowerShell/RunPowerShellGet`（底层 `-EncodedCommand` Base64 Unicode），消除引号/中文路径乱码与命令注入风险。

### 🌐 官网重建与同步
- **版本号单一来源机制**：`site-src/version.json` 为唯一真源，`render_site.py` 渲染生成 `site-dist/`；改后 `validate_html.py` 四页全 OK 再部署。
- **download 页两栏布局**：右侧完整 changelog 与 changelog 页一致，每个版本 panel 加「本版更新」折叠区，v1.01~v1.16 共 16 个 panel 填充真实历史摘要。
- **CSS 源纳入 git**：`site-css/style.css` 归入版本控制，不再是 gitignore 状态。
- **CF 缓存策略优化**：HTML 缓存从 `no-store` → `no-cache` → `max-age=300` 调优，确保部署后立即可见；`.worker.js` 图片资源补充 Content-Type 映射（png/ico/svg/jpg/gif/webp）。
- **结构化数据与 SEO**：新增 SoftwareApplication JSON-LD；链接对比度 / 下载页二级导航 / CSP 头三项优化；首页副标题去掉"无需安装"强调右键管理员运行。
- **关于页重构**：删除与功能页重复的「功能简介」区块，开发者联系方式三列同行显示（按钮 + 明文地址分离），升级样式。

### 🎨 UI / 布局改进
- **侧边栏导航均分占满**：导航按钮由固定高度改为 `Grid` Star **平均分配**全部可用空间，标准窗口（740 高）下 16 项正好占满、无底部空白、无滚动条；矮窗口自动出现细滚动条兜底。
- **Edge 优化 WYSIWYG 应用策略**：「取消勾选 + 开始优化」即可单独还原某项，无需动用「还原所有项」误伤其它优化项；首次勾选提示 Edge 组策略副作用说明。
- **Edge 组策略双 hive 彻底清除**：新增 `RegistryHelper.EdgePolicyHives`（HKCU + HKLM 统一操作），仅清 HKLM 会因 HKCU 残留而清不掉「由组织管理」状态。

### 🛡️ 安全 / 性能修复
- **自定义软件 ID 路径穿越封禁**：`swinst_` 临时目录路径直接拼接用户输入，恶意 `..\` 可逃逸 %TEMP%；修复：输入层校验 ID 仅允许 `[A-Za-z0-9_-]`（长度 1-64），使用 `SanitizeSwId` 防御性清洗。
- **Defender 状态缓存跨线程同步**：`_cacheRealtime` 等 6 个静态缓存字段后台线程写、UI 线程读；修复：全部 `volatile` + `_cacheLock` 整体包住刷新/读取。
- **安全加固**：MAS 激活改走系统目录完整路径 `powershell.exe` + `-EncodedCommand`，消除 PATH 劫持风险；Chocolatey OData 过滤的 `id` 加白名单校验；Office 部署 XML 中 `pid`/`channel` 用 `SecurityElement.Escape` 转义。

### 🧹 项目卫生（v1.17 即时清理）
- 清理工作目录：删除 15+ 个备份文件（.bak*）、10+ 个日志文件、4 个临时目录（`.bak_pagecache_*` / `site-css` / `site-js` / `.bak_*`）。
- 建立长期记忆规则：禁止在交付目录保留无版本号副本；清理 `.bak*` 备份、`.log` 日志、`.bak_*` 临时目录。
- Git commit: e60ec39 chore: 清理备份文件和日志


## [v1.16.1] - 2026-08-30

### 🐛 软件安装下载修复
- **Geek Uninstaller 等便携软件下载卡住**：`DownloadAsync` 改用 `Downloader.DownloadAsync`，开启 `useProxyFallback: true`（系统代理 → 直连 → Watt Toolkit 本地代理依次尝试），最多重试 3 次、间隔 5 秒，解决代理环境下直连失败导致永久挂起的问题。
- **便携版支持自定义安装目录**：`IsPortable` 单文件分支（如 Geek Uninstaller）优先使用用户指定的 `customDir`，不再硬编码 `%LOCALAPPDATA%\CpqSystemTool\Portable\<id>\`。
- **保留 Referer 支持**：`Downloader.DownloadAsync` 新增可选 `referer` 参数，`SoftwareInstall.DownloadAsync` 透传 `SoftwareDef.Referer`，确保哔哩哔哩等需要 Referer 头的软件仍可正常下载。


## [v1.16] - 2026-08-22

> 相对 v1.15 的源码变更：Edge 管理页新增「实验性功能 flags」批量管理（11 项推荐配置 + 一键优化/恢复 + 强制重启生效）。

### 🎯 Edge 实验性功能（edge://flags）批量管理
- **11 项 flags 推荐配置**：ANGLE 图形后端（推荐默认）、Edge Copilot 模式（推荐禁用）、并行下载、GPU 栅格化、硬件加速视频解码、QUIC/HTTP3、前进后退缓存、平滑滚动、TLS 1.3 Early Data、强制深色模式（推荐禁用）、Fluent 悬浮滚动条——每项下拉含「默认 (Default) / 启用 / 禁用」及 ⭐ 推荐值标记。
- **⚡ 一键优化**：把所有 flags 设为推荐值（性能类启用、Copilot 禁用、ANGLE 保持默认）→ 自动写入注册表 `HKCU\Software\Microsoft\Edge\EdgeFlags` → **强制重启 Edge 立即生效**。
- **↩ 一键恢复默认**：清除本程序管理的全部 flags 注册表值，恢复 Edge 出厂默认 → 强制重启 Edge。
- **下载更新默认路径（v1.16 补丁）**：「下载更新」的 SaveFileDialog 默认保存目录从 `%UserProfile%` 改为**当前已安装 exe 同级目录**（`AppContext.BaseDirectory`）——覆盖更新（如 v1.14→v1.16）时下载文件默认落到旧版本旁边，不再跳到用户目录。
- **UI 联动**：修改后 ComboBox 实时同步注册表状态；手动切换即写注册表（选「默认」恢复出厂）。
- **注意事项**：实验性功能可能不稳定，逐项开启并在 edge://flags 可随时重置；修改需重启 Edge 生效（一键按钮已内置强制重启）。

## [v1.15] - 2026-08-21

> 相对 v1.14 的源码变更：修复侧边栏「配置管理」在标准窗口下被截断的问题——按钮区改为 Grid Star 行均分 + ScrollViewer 滚动兜底。

### 🎨 UI / 布局修复
- **侧边栏导航均分占满**：导航按钮由固定高度改为 `Grid` 16 行 Star **平均分配**全部可用空间，标准窗口（740 高）下 16 项正好占满、无底部空白、无滚动条；窗口最大化时每项自动变高。
- **矮窗口滚动兜底**：按钮区包 `ScrollViewer`（`VerticalScrollBarVisibility=Auto`），窗口被压缩（小屏 / 高 DPI 缩放）时出现细滚动条，全部导航项仍可达；标题 / 副标题固定顶部不随滚动。
- 按钮文字垂直居中，行内图标 + 文本视觉更整齐。


## [v1.14] - 2026-08-21

> 相对 v1.13 的源码变更：第 6 轮深度审查驱动的安全/竞态/性能优化——封死自定义软件 ID 路径穿越、Defender 缓存跨线程同步，并把「整页缓存」铺开到 9 个高频页面（常用软件 / Appx 商店 / Appx 管理 / 系统优化 / 服务优化 / 安全防护 / 清理优化 / 内存工具 / 配置管理），附带日志行数上限与探针请求头优化。

### 🛡️ 安全 / 竞态修复
- **自定义软件 ID 路径穿越封禁（H1·安全）**：`swinst_` 临时目录路径直接拼接用户输入的软件 ID，恶意 `..\` 可逃逸 %TEMP% 以管理员权限写任意路径。修复：输入层校验 ID 仅允许 `[A-Za-z0-9_-]`（长度 1-64），使用点 `SanitizeSwId` 防御性清洗后拼路径（临时目录 / 下载目标 / 清理枚举全覆盖）。
- **Defender 状态缓存跨线程同步（H2）**：`_cacheRealtime` 等 6 个静态缓存字段后台线程写、UI 线程读，无同步可能读到半更新状态。修复：全部 `volatile` + `_cacheLock` 整体包住刷新/读取，顺带消除并发双 PowerShell 子进程。

### ⚡ 性能：页面整页缓存铺开（9 页）
- **常用软件 / Appx 商店 / Appx 管理**：整页实例缓存（ID 签名 + 主题双失效键），二次进页不再全量重建，后台照常刷新安装状态。
- **系统优化 / 服务优化 / 安全防护 / 清理优化 / 内存工具 / 配置管理**：整页外壳缓存 + Refresh 委托复位动态状态 + 重触发原有后台数据刷新（服务状态 / Defender 状态 / 内存分析 / 配置列表），页内操作完成自动失效缓存。
- **日志框行数上限**：超过 3000 行自动裁剪头部（保留尾部最新日志），长任务运行不再内存/渲染无限膨胀。
- **探针请求头优化**：`Accept-Encoding: identity` → `gzip, deflate`（手动解压）；固定 UA → 5 条 Chrome/Edge UA 池轮换；补 `Accept-Language` 与同源 Referer，降低被 WAF/CDN 识别为 bot 的概率。


## [v1.13] - 2026-08-21

> 相对 v1.12 的源码变更：按「基础设计缺失扫描」结论整体补强——exe 自替换原子化、全局操作防重入、配置原子写入、危险操作确认、更新状态锁，并新增两处 UI 提醒。

### 🛡️ 健壮性 / 补强
- **清理页 CheckBox 垂直对齐修复（v1.13 补丁）**：清理项列表的 CheckBox 由垂直居中改为**顶端对齐 + 3px 微调**，与文字顶端对齐（修复部分项 CheckBox 视觉偏低的上下错位）；WrapPanel 换行行为保持不变。
- **「检查更新」IPv4 直连修复（v1.13 补丁·核心原因）**：根因是 `.NET Framework 4.8` 的 `HttpWebRequest` DNS 解析 **IPv6 优先且失败不回退 IPv4**，而 Cloudflare Pages 返回 AAAA 记录、本机无 IPv6 连通 → 直接超时"无法连接"（浏览器有 Happy Eyeballs 自动回退所以正常）。修复：`DownloadStringWithProxyFallback` 首选「手动解析 A 记录 → IP 直连 + Host 头保留域名（SNI/证书正确）」，系统代理 / Watt Toolkit 作回退；顺带每次显式升 TLS 1.2。
- **exe 自替换原子化（P0）**：`ApplyPendingBakeIfAny` 由「先改名后替换」两步改为 `MoveFileEx` 原子替换（`MOVEFILE_REPLACE_EXISTING|WRITE_THROUGH`），占用时回退「改名+移入」并带**失败回滚**——中途失败不再让主程序停在 `.old`，自包含更新始终可用。
- **全局操作防重入（P1）**：新增 `OperationLock` 全局互斥，清理 / 优化 / 安全防护（Defender 禁用/恢复、开关、防火墙规则、更新管理）等耗时操作同一时间只允许一个——按钮连点或跨模块并发不再并行删同目录 / 并行写同注册表键；冲突时提示「已有XX操作正在运行」。
- **配置原子写入（P1）**：`ConfigBackup.Save` / `Theme.SaveBackgroundSettings` / `SoftwareDefPersistence.StageBake` 由 `WriteAllText` 直写改为「同目录 tmp + `MoveFileEx` 原子替换」——崩溃不再留半截 JSON 导致配置静默丢失。
- **危险操作确认（P1）**：「开始优化」「一键禁用 Defender」「清理策略残留」「移除防火墙规则」等此前点击即执行的高危操作，补 YesNo 确认对话框。
- **更新状态锁（P1）**：「检查更新」「下载更新」加 `_checkingUpdate` / `_downloadingUpdate` 状态锁 + 按钮禁用联动——并发触发不再竞态写更新地址 / 重复弹下载框。
- **UI 提醒（P2）**：「关闭系统还原」优化项下方常驻提示「⚠ 关闭系统还原后，危险操作将失去还原点兜底」；配置目录不可写时首次弹窗告知（含路径），不再静默失败。


## [v1.12] - 2026-08-21

> 相对 v1.11 的源码变更：继续清理技术债——统一 4 套下载实现、封死后台线程 Dispatcher 关窗崩溃面、消除全部 sync-over-async（`.Result`），并修复「可多开窗口」的单实例缺失。

### 🐞 修复
- **单实例保护（v1.12 补丁）**：此前无任何 Mutex 保护，双击 exe 可无限多开窗口；现在同一时间只允许一个实例——第二实例启动时自动激活已有窗口（最小化则恢复前台）并退出，不再创建新窗口；Mutex 获取异常时放行启动，保证工具始终可用。

### ♻️ 重构 / 清理
- **下载实现统一（`Helpers/Downloader.cs`）**：此前 4 套重复的 HTTP 下载逻辑（About 代理回退下载 / AppxManager 断点续传 / WebView2ProbeDeps 进度下载 / OfficeInstall 过时 `WebClient`）合并为单一 `Downloader.DownloadAsync`——支持重试、进度回调、请求级超时（CTS）、代理回退、断点续传（Range）、UA 注入；后续修下载类 bug 只需改一处，行为一致。
- **后台线程 Dispatcher 调用全部加兜底**：全项目甄别 35 处 `Dispatcher.Invoke/BeginInvoke`，其中 32 处后台线程调用（下载进度回调、Appx/软件页 ThreadPool 加载、Defender/防火墙状态刷新、还原点列表、内存分析、系统事件线程等）统一补 `try { } catch { /* 窗口已关闭，忽略 */ }`——后台任务运行中关闭窗口不再因 Dispatcher 关停抛未处理异常导致进程终止（延续 v1.11 的 RunInBg 修复，封死剩余面）。
- **消除全部 `.Result`（sync-over-async）**：9 处阻塞等待全部改造——ChocolateyResolver 解析链、SoftwareInstall 下载/安装/页面解析链真 async 化（`TryResolveAsync`/`DownloadAsync`/`ResolveAsync`/`InstallAsync`），ProbeBrowserHost 已 await 完成取值改 `GetAwaiter().GetResult()`；全项目 `.Result` 清零，消除死锁隐患。


## [v1.11] - 2026-08-20

> 相对 v1.10 的源码变更：三轮代码审查（结构 / 性能 / 缺陷）驱动的一次全面优化——修复 6 类真实缺陷、5 项性能提速、5 项结构与冗余重构。

### 🐞 修复
- **子进程双流读取死锁（Exec）**：`RunPS`/`RunCmd`/`RunCmdGet`/`RunVbs`/`RunVbsGet` 由顺序 `ReadToEnd` 改为异步并行排水（`BeginOutputReadLine`/`BeginErrorReadLine`），消除子进程 stderr 写满 64KB 管道缓冲导致的互相阻塞（此前最坏挂到 15 分钟超时被杀）；capture 模式 stderr 不再静默丢弃（输出 `[STDERR]`）。
- **后台任务运行时关闭窗口崩溃**：`MainWindow.RunInBg` 与 `DriverStorePanel.RunInBg` 的 Dispatcher 调用统一加 `safeUi` 兜底——清理/下载/探针/驱动加载等长任务执行中关闭主窗口，不再因 net48 未处理后台线程异常导致进程直接终止。
- **卸载命令路径截断**：未加引号的 `UninstallString`（如 `C:\Program Files\X\un.exe /S`）按「最长存在的文件前缀」解析，不再截断成 `C:\Program` 导致卸载失败。
- **`where node` 无超时挂死**：`ResolveNodeExe` 的 `WaitForExit()` 加 10s 超时 + 强杀，安全软件拦截时后台线程不再永久挂死。
- **async void 异常逃逸面**：`RefreshDepStatus` / `DownloadUpdate` 改为 `async Task` + 外层兜底，消除 UI 线程崩溃面。

### ⚡ 性能
- **官方直链探针候选验证并行化**：串行 `foreach + await` → `SemaphoreSlim(5)` + `Task.WhenAll`（保序），多候选场景最坏 225s → 约 45s（5 倍提速）。
- **常用软件页注册表枚举缓存**：3 个 Uninstall 根一次性枚举 + 5s TTL 内存缓存，页面渲染枚举成本 O(100×N) → O(N)。
- **WebView2 就绪检测缓存**：`CheckWebView2ReadyAsync` 30s TTL（成功/失败均缓存）+ 「管理依赖」重入锁，反复打开秒回。
- **Chocolatey / 页面解析结果缓存**：24h TTL（失败不缓存），重复安装每次省 2~6s。
- **日志框写入优化**：进度行 O(n) 全量读写 → TextBox 行索引定位 O(log n)；长日志（>500 行）滚动降频，减轻 UI 线程布局压力。

### ♻️ 重构 / 清理
- **`MainWindow.Pages.cs` 拆分**：5,599 → 833 行，按功能域拆 8 个 partial（Tweaks/Appx/Software/About/Security/Config/Cleanup/SystemTools）+ 2 个独立类（`StoreSearchWindow`/`BoolToBrushConverter`）。
- **日志统一**：113 处「异常(已忽略)」内联复制 → 公共 `DebugLog.Ignore(ex)`。
- **公共工具收编**：版本比较两套合一（`VersionUtil`）、MiniJson 双实现合一（`MiniJson`）、3 处版本名映射字典合一（`EditionMap`，单一数据源双向查询）。
- **Config 路径集中**：4 处 `BaseDirectory\Config` 硬编码 → `AppPaths.ConfigDir`。
- **HttpClient 静态单例复用**：4 处「用完即弃」`new HttpClient` → 共享单例，消除 socket TIME_WAIT 堆积；请求级超时（CTS）与 UA/Referer 注入，探针专用单例保留。


## [v1.10] - 2026-08-18

> 相对 v1.09 的源码变更：新增「内存工具」导航页（镜像 RAMMap 只读视图 + 可选内存优化），置于「系统工具」之下。

### ✨ 新增
- **内存工具页（镜像 RAMMap 只读视图）**：左侧导航新增「内存工具」（🧠，挂在「系统工具」之下），纯代码构建独立页面，分三层——
  - **A 内存总览（只读）**：`GlobalMemoryStatusEx` + `GetPerformanceInfo`（均为 Windows 文档化 API）展示总/可用物理内存、内存占用百分比、已提交/提交上限、内核分页/非分页池。
  - **B 内存使用拆解（只读）**：`WMI Win32_PerfFormattedData_PerfOS_Memory`（文档化计数器）把物理内存拆为「使用中 / 备用 / 已修改 / 空闲+零页」四类占比条 + 图例（含字节数与百分数）；并展示可用/系统缓存/已提交/提交上限/分页池/非分页池明细，下方列出进程工作集 Top 10（`GetProcessMemoryInfo` + `EnumProcesses`）。
  - **C 内存优化（默认收起 · 中风险 · 仅管理员）**：`Expander` 默认折叠，仅供管理员启用；提供「清空备用列表(Standby)」「清空所有进程工作集」两项——前者调 `NtSetSystemInformation(MemoryPurgeStandbyList=2)` 清 Standby，后者逐进程 `EmptyWorkingSet`；均带 `SeProfileSingleProcessPrivilege` 提权与风险说明（优化为临时效果，用缓存/工作集换即时空闲内存）。
- **内存采集模块 `Modules/MemoryAnalyzer.cs`**：封装全部 P/Invoke（kernel32/psapi/ntdll）与 WMI 查询逻辑，全程 `try/catch` 优雅降级（WMI 不可用时拆解数据单独提示不可用，总览数据仍可用）。

### ♻️ 变更 / 策略
- **设计为「文档化 API 优先、避免未文档化结构体偏移」**：内存拆解刻意改用 WMI 文档化性能计数器还原 RAMMap 视图，规避 `NtQuerySystemInformation(0x32)` 未文档化结构体偏移猜错导致静默假数据的风险；仅优化层（Standby 清理）使用经验证权威常量 `MemoryPurgeStandbyList=2`（网上部分资料误写为 3/4）。

### 🐞 修复
- **内存工具卡片 A 布局**：总览 6 个统计块由 `WrapPanel` 改为 2 行 × 3 列网格，占满页面宽度（不再随窗口宽度换行错落）。
- **内存工具卡片 B 数据可靠性**：`WMI Win32_PerfFormattedData_PerfOS_Memory` 首次查询常返回全 0（计数器尚未「cook」），`GetUseCounts` 增加一次重试（+80ms）；新增 **四级回退链**：WMI 格式化类 → 重试 → WMI 原始类（`Win32_PerfRawData_PerfOS_Memory`）→ **PDH 性能计数器**（`pdh.dll` `PdhAddEnglishCounter` 直接读 `\Memory\*` 计数器，绕过 WMI）→ 基于 `GetOverview` 的降级视图，确保 WMI 不可用时仍能拿到真实拆解数据、功能不整体失效；修复占比条闪一下即消失、拆解全显示 0 B 的问题；取数仍不可用时不再把占比条收缩为 0 宽度（改为整条灰色占位 + 文字提示），避免「消失」观感。
- **PDH 回退进一步加固（避免「全有或全无」）**：`TryQueryUseCountsPdh` 改为**逐计数器容错**——某计数器名在本 Windows 版本不存在（如旧版无 Standby 细分）时仅跳过该计数器、零值填充其余有效值，只要关键计数器「可用内存(Available)」成功即采用真实数据，不再因单个计数器缺失而丢弃其余 10 个有效值；全部读取失败时仍正确回退到降级视图。
- **异常处理不静默造假数据**：`GetUseCounts` 的 `catch` 与「全部回退失败」路径统一置 `IsDegraded = true`，即使 `overview == null` 连降级视图都构造不出，也确保 UI 走「数据不可用」灰色占位 + 清晰提示，绝不把全 0 当成真实内存拆解渲染（占比条消失 / 明细显示 0 B）。

### 🔧 发布后跟进（v1.10 即时修补）
- **内存拆解「使用中为 0」+ 天文数字 GB（根因修复）**：`Modules/MemoryAnalyzer.cs` 的 PDH 格式常量 `PDH_FMT_LARGE` 被误写成 `0x00000200`（该值实为 `PDH_FMT_DOUBLE`）。代码据此请求 DOUBLE 格式却又用 `cv.longValue`（64 位整数）读取，把 IEEE-754 double 的二进制位当成整数读 → 出现数十亿 GB 的假数据；进而「可用 + 已修改」远大于总物理内存，使「使用中 = 总 − 可用 − 已修改」被钳为 0。修复：补 `PDH_FMT_DOUBLE = 0x00000200` 并将 `PDH_FMT_LARGE` 改正为 `0x00000400`，`fmt` 现在真正请求 LARGE 格式、`longValue` 读取合法 → 「使用中」显示真实值、各项字节数回归正常量级。
- **内存优化「点了没反应」**：「清空备用列表 / 清空所有进程工作集」两个按钮执行后未重新分析内存，拆解视图一直冻结，误以为优化无效（RAMMap 清理后即可瞬间看到变化）。修复：两个 `RunInBg` 调用补充第 4 参 `onDoneUi = () => DoMemoryAnalyze(pb, applyUi)`，在后台优化完成后于 UI 线程自动重新抓取并刷新「内存使用拆解」视图，效果即时可见。


## [v1.09] - 2026-08-18

> 相对 v1.08 的源码变更：新增 Whesvc 诊断日志清理项与服务禁用项。

### ✨ 新增
- **Whesvc 诊断日志清理**：在「系统文件」清理类新增 `Whesvc 诊断日志` 项（默认不勾选），清理 `C:\Windows\Temp\DiagOutputDir\Whesvc` 下 Windows 健康状况和优化体验服务生成本地性能追踪 ETL 日志。该日志可安全删除、服务重新启用时会再生；服务运行时文件被占用会自动跳过。
- **服务项优化新增 `whesvc`**：在可禁用服务清单新增「Windows 健康状况和优化体验」，风险等级 `mid`，说明注明「本地性能诊断日志(占C盘)，关掉无性能提升、笔记本可能影响节能」。

### ♻️ 变更 / 策略


## [v1.08] - 2026-08-17

> 相对 v1.07 的源码变更：关于页新增官网地址链接，检查更新改为从官网 version.json 获取新版本，官网安装包统一改为中文名。

### ✨ 新增
- **关于页新增官网地址**：在「开发者与协议」卡片新增 `官网：cpq-system-tool.pages.dev` 链接，指向 https://cpq-system-tool.pages.dev/。

### ♻️ 变更 / 策略
- **检查更新改为从官网 version.json 获取新版本**：原检查更新从 GitHub Releases API 拉取 `tag_name`，现改为读取官网根 `version.json` 的 `version`/`name`/`url` 字段，普通用户无需访问 GitHub、下载更快；版本比较与「下载更新」弹窗逻辑保持不变。
- **官网安装包统一改为中文名**：官网托管与下载页全部 exe 由 `System-Cleanup-Optimizer_vX.XX.exe` 改为 `系统清理与优化工具_vX.XX.exe`（v1.01–v1.08），`version.json` 的 `name`/`url` 同步使用中文名；GitHub Release 资产保留英文名 `System-Cleanup-Optimizer_v1.08.exe`（规避 gh 中文文件名截断）。


## [v1.07] - 2026-08-17

> 相对 v1.06 的源码变更：完成全部下拉框（ComboBox）深/浅色主题统一与自定义下拉 Popup 层级修复，修复「安装到」按钮主题自适应。

### 🐛 修复
- **「安装到」按钮背景/字体色随主题切换**：自定义安装路径态此前硬编码浅薄荷背景 `Color.FromRgb(0xE6,0xF7,0xF4)`，深色模式下始终不变；现改为主题笔刷 `_btnSecondaryBg` + `_accent` 高亮文字/边框，与默认态均随深/浅色自动变换。
- **修复自定义下拉「浮到最顶层」**：「管理依赖」「全部分类」两个 Popup（AllowsTransparency=true 会以独立顶层 HWND 带 WS_EX_TOPMOST 渲染）在打开时剥离 WS_EX_TOPMOST，并挂 HwndSource Hook 在 WM_WINDOWPOSCHANGED 时持续剥离，使其落到正常层级、 不再压在所有窗口（含其他应用）之上（`UiShapes.DisablePopupTopmost`）。

### ♻️ 变更 / 策略
- **统一全部 ComboBox 深/浅色自适应**：新增 `UiShapes.ApplyComboBoxTheme`，以自定义 ControlTemplate（闭合框 + 下拉弹层均引用主题键）+ ComboBoxItem 样式，让 7 个 ComboBox（版本切换目标 / Office 版本 / Edge 频道 / 驱动引擎 / 分组 / 软件分类 / 风险等级）的背景、字体、边框与下拉弹层（含选中/悬浮态）统一跟随深/浅色主题笔刷，替代默认跟随系统色的 Aero2 模板（深模式下弹层为刺眼白底）；弹层刻意关闭 AllowsTransparency 以复用默认 ComboBox 的非置顶行为，避免重新引入浮层问题。


## [v1.06] - 2026-08-16

> 相对 v1.05 的源码变更：WebView2 浏览器探针依赖改为「运行时从 NuGet 拉取」兜底（摆脱 Costura 嵌入）；全部实心箭头改为开放折线 chevron（抽出 UiShapes 共享）；修复清理优化页分组 Expander 标题丢失与箭头错位；若干健壮性修复。

### ✨ 新增
- **WebView2 探针依赖运行时下载（兜底）**：新增 `Modules/WebView2ProbeDeps.cs`。单文件/裸 exe 分发到其他机器缺失 3 个托管 WebView2 DLL 时，运行时从 NuGet 拉取 `Microsoft.Web.WebView2 1.0.2045.28`（3 托管 + 原生 Loader）到 exe 目录；幂等（sentinel=`Core.dll`）、不抛异常（失败仅记录日志、探针随后回退 Node+Playwright）、后台下载不阻塞 UI。挂钩 `EdgeCore` 安装/修复、`ProbeBrowserHost` 初始化、`RunProbeInternal` 共 4 处。

### ♻️ 变更 / 策略
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


## [v1.05] - 2026-08-14

> 相对 v1.04 的源码变更：新增「驱动清理」模块（参考 Driver Store Explorer / RAPR 界面与行为设计，基于 Windows 原生 API 独立实现）+ 多项交互与体验增强。

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


## [v1.01] - 2026-08-06

- 初始版本发布。完成系统清理、优化与维护核心功能：
  - 系统优化（一键/按需调校，操作前可创建还原点）
  - 清理优化（6 大类 34 项细粒度清理，先扫描后清理）
  - 服务优化、Appx 商店管理、常用软件官方直链下载
  - 安全防护（安全中心 / 防火墙 / Defender）、Edge 管理、隐私设置
  - 系统工具、激活工具（MAS）、系统信息、维护工具、配置管理
- 详见 `README.md`。
