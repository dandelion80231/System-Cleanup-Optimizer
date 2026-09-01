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

---

## [v1.16.1] - 2026-08-30
