using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public static class EdgeCore
    {
        // =====================================================================
        //  Edge 频道统一定义（唯一事实来源）
        //  背景：此前 GetEdgeVersion（stable/beta/dev/canary/sxs 五个）、InstallEdge（三个）、
        //  UninstallEdge（两个）各写一份 switch，三处支持范围互不一致。UI 上给了 5 个频道，
        //  选 Canary/SxS 卸载时 EdgeCore 只打一行"未知频道"就返回，而 UI 仍无条件报"卸载完成"
        //  （假成功）。现统一为下面这一张表，所有频道相关逻辑一律查表，新增频道只改这里。
        // =====================================================================

        /// <summary>单个 Edge 频道的注册表/显示/能力信息。</summary>
        public sealed class EdgeChannelInfo
        {
            /// <summary>UI 传入的频道标识：stable / beta / dev / canary / sxs。</summary>
            public string Key { get; private set; }
            /// <summary>注册表 DisplayName，同时用于按名搜索版本与强制清理。</summary>
            public string DisplayName { get; private set; }
            /// <summary>HKLM 下该频道的卸载键完整路径。</summary>
            public string UninstallKeyPath { get; private set; }
            /// <summary>EdgeUpdate\Clients\{GUID}：读不到 UninstallString 时据此定位 setup.exe。</summary>
            public string UpdateClientPath { get; private set; }
            /// <summary>官方下载地址；null 表示本工具不支持自动安装该频道。</summary>
            public string InstallUrl { get; private set; }
            /// <summary>是否支持自动卸载。Canary/SxS 是当前用户级安装（不在 HKLM 系统级卸载键下），
            /// 本工具的系统级卸载流程对其无效，故置 false，由调用方明确报"不支持"而非假成功。</summary>
            public bool CanUninstall { get; private set; }

            internal EdgeChannelInfo(string key, string displayName, string uninstallKeyPath,
                                     string updateClientPath, string installUrl, bool canUninstall)
            {
                Key = key;
                DisplayName = displayName;
                UninstallKeyPath = uninstallKeyPath;
                UpdateClientPath = updateClientPath;
                InstallUrl = installUrl;
                CanUninstall = canUninstall;
            }
        }

        private const string UninstallKeyRoot = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\";
        private const string UpdateClientsRoot = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\";

        /// <summary>全部频道定义，顺序与 UI 下拉一致（stable/beta/dev/canary/sxs）。
        /// 注意 EdgeUpdate 的 Clients GUID 每个频道各不相同——原实现里 Beta 误用了 Stable 的 GUID，
        /// 会在读不到 UninstallString 时定位到 Stable 的 setup.exe（卸载 Beta 却动到 Stable）。</summary>
        public static readonly EdgeChannelInfo[] EdgeChannels =
        {
            new EdgeChannelInfo("stable", "Microsoft Edge",        UninstallKeyRoot + "Microsoft Edge",        UpdateClientsRoot + "{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}", "https://c2rsetup.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeStablePage&Channel=Stable&language=zh-cn&brand=M100", true),
            new EdgeChannelInfo("beta",   "Microsoft Edge Beta",   UninstallKeyRoot + "Microsoft Edge Beta",   UpdateClientsRoot + "{2CD8A007-E189-409D-A2C8-9AF4EF3C72AA}", "https://c2rsetup.edog.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeInsiderPage&Channel=Beta&language=zh-cn", true),
            new EdgeChannelInfo("dev",    "Microsoft Edge Dev",    UninstallKeyRoot + "Microsoft Edge Dev",    UpdateClientsRoot + "{0D50BFEC-CD6A-4F9A-964C-C7416E3ACB10}", "https://c2rsetup.edog.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeInsiderPage&Channel=Dev&language=zh-cn", true),
            new EdgeChannelInfo("canary", "Microsoft Edge Canary", UninstallKeyRoot + "Microsoft Edge Canary", UpdateClientsRoot + "{65C35B14-6C1D-4122-AC46-7148CC9D6497}", null, false),
            new EdgeChannelInfo("sxs",    "Microsoft Edge SxS",    UninstallKeyRoot + "Microsoft Edge SxS",    null,                                                         null, false),
        };

        /// <summary>按频道标识查表；未知频道返回 null（调用方必须据此报错，不得当成功处理）。</summary>
        public static EdgeChannelInfo FindChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            foreach (var c in EdgeChannels)
                if (string.Equals(c.Key, channel, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>供日志提示用的可选频道列表，如 "stable/beta/dev/canary/sxs"。</summary>
        private static string KnownChannelList()
        {
            return string.Join("/", EdgeChannels.Select(c => c.Key));
        }

        // === 版本检测 ===
        public static string GetEdgeVersion(string channel)
        {
            var info = FindChannel(channel);
            if (info == null) return "未知";
            string subKey = info.UninstallKeyPath;
            string displayName = info.DisplayName;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    // 注：注册表视图本身没有"二次重定向"问题——用 32 位 reg.exe 实测
                    // HKLM\SOFTWARE\WOW6432Node\... 能正常读到值（重定向器不会拼出 WOW6432Node\WOW6432Node），
                    // 所以这里保留原路径不变；真正的失效点是下面这个"固定键名"。
                    var v = key?.GetValue("Version")?.ToString();
                    // 只采信点分版本号形态；Edge 的 Version 值也可能是 EdgeUpdate 内部序号（如 2533363745），
                    // 这种情况继续走下面的 DisplayName 兜底去取 DisplayVersion。
                    if (IsDottedVersion(v)) return v;
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }

            // 兜底：现代 Edge（Stable）的卸载键名已不是 "Microsoft Edge"，而是安装程序生成的 GUID
            // （本机实测为 {C5DA3FA9-BB21-33F6-AC6E-73839ACE9E08}，其 DisplayName 才是 "Microsoft Edge"），
            // 所以按固定键名读会恒定返回"未安装"。这里退化为按 DisplayName 在 Uninstall 下搜索。
            return FindVersionByDisplayName(displayName) ?? "未安装";
        }

        /// <summary>
        /// 在 HKLM\...\Uninstall 下按 DisplayName 精确匹配查找已安装版本（先 64 位视图，再回退 32 位视图）。
        /// 用于 Edge 这类"卸载键名是 GUID、只有 DisplayName 稳定"的场景；找不到返回 null。
        /// </summary>
        private static string FindVersionByDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            var views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Default };
            foreach (var view in views)
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstall = root.OpenSubKey(uninstallPath))
                    {
                        if (uninstall == null) continue;
                        foreach (var name in uninstall.GetSubKeyNames())
                        {
                            using (var key = uninstall.OpenSubKey(name))
                            {
                                if (key == null) continue;
                                var dn = key.GetValue("DisplayName")?.ToString();
                                if (!string.Equals(dn, displayName, StringComparison.OrdinalIgnoreCase)) continue;
                                // 不能无脑取 Version：本机实测 Edge 的 GUID 卸载键里 Version="2533363745"
                                // （EdgeUpdate 的内部序号），真正的四段版本在 DisplayVersion="152.0.4191.53"。
                                // 故这里只在值确实是"数字.数字[.数字...]"形态时才采信，优先采信形似版本号的那个。
                                var vVersion = key.GetValue("Version")?.ToString();
                                var vDisplay = key.GetValue("DisplayVersion")?.ToString();
                                if (IsDottedVersion(vDisplay)) return vDisplay;
                                if (IsDottedVersion(vVersion)) return vVersion;
                                if (!string.IsNullOrWhiteSpace(vVersion)) return vVersion;
                                if (!string.IsNullOrWhiteSpace(vDisplay)) return vDisplay;
                            }
                        }
                    }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
            return null;
        }

        public static string GetWebView2Version()
        {
            // 与 GetEdgeVersion 采用同一套策略：
            //   1) 先按固定键名读，且**值必须是点分版本号才采信** —— Version 值可能被写成
            //      EdgeUpdate 的内部序号（如 2533363745），直接展示会让人误以为是版本号；
            //   2) 固定键名读不到（或值不可信）时，退化为按 DisplayName 在 Uninstall 下搜索。
            // 原实现只要读不到就立刻返回"未安装"，既不校验值形态、也没有兜底，
            // 在键名变化或 Version 被写成内部序号的机器上会误报"未安装"或显示错误数字。
            var probes = new[]
            {
                new { Hive = Registry.LocalMachine, Sub = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView" },
                new { Hive = Registry.CurrentUser,  Sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView" },
            };
            foreach (var probe in probes)
            {
                try
                {
                    using (var key = probe.Hive.OpenSubKey(probe.Sub))
                    {
                        if (key == null) continue;
                        var vDisplay = key.GetValue("DisplayVersion")?.ToString();
                        if (IsDottedVersion(vDisplay)) return vDisplay;
                        var vVersion = key.GetValue("Version")?.ToString();
                        if (IsDottedVersion(vVersion)) return vVersion;
                        if (!string.IsNullOrWhiteSpace(vDisplay)) return vDisplay;
                        if (!string.IsNullOrWhiteSpace(vVersion)) return vVersion;
                    }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
            return FindVersionByDisplayName("Microsoft Edge WebView2 Runtime") ?? "未安装";
        }

        // Issue 6: 当前用户级别 WebView2（分开显示在 UI 上）
        public static string GetWebView2CurrentUserVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView"))
                {
                    if (key == null) return "未安装";
                    // 与 GetWebView2Version 一致：优先 DisplayVersion，且只采信点分版本号形态，
                    // 避免把 EdgeUpdate 的内部序号（如 2533363745）当版本号显示给用户。
                    var vDisplay = key.GetValue("DisplayVersion")?.ToString();
                    if (IsDottedVersion(vDisplay)) return vDisplay;
                    var vVersion = key.GetValue("Version")?.ToString();
                    if (IsDottedVersion(vVersion)) return vVersion;
                    return !string.IsNullOrWhiteSpace(vDisplay) ? vDisplay
                         : !string.IsNullOrWhiteSpace(vVersion) ? vVersion
                         : "未安装";
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return "未安装"; }
        }

        // === 运行时版本目录挑选（关键：先过滤非版本目录，再按版本号自然排序） ===
        // 背景：Edge/EdgeWebView 的 Application 目录下除了 "152.0.4191.53" 这类版本目录，
        // 还会有 SetupMetrics（引导程序自己写的指标目录）、Installer、Dictionaries 等非版本目录。
        // 旧实现用 Directory.GetDirectories(root).OrderByDescending(d => d).FirstOrDefault() 取"最新版本"，
        // 但 .NET 默认字符串比较里字母排在数字之后，"...\SetupMetrics" > "...\152.0.4191.53"，
        // 于是 FirstOrDefault 永远选中 SetupMetrics（里面只有 .pma 文件），进而误报
        // "运行时文件仍不完整（msedgewebview2.exe 或 msedge.dll 缺失）"——且跑一次修复就会生成
        // SetupMetrics，之后每次修复都误报。故这里统一走下面这套"过滤 + 版本号自然排序"。

        /// <summary>
        /// 是否为"点分版本号"形态：只允许数字与 '.'，且至少两段（如 152.0.4191.53）。
        /// 既用于判断版本目录名（排除 SetupMetrics / Installer / Dictionaries 等非版本目录），
        /// 也用于判断注册表里的版本值是否可信（Edge 的 Version 值可能是 2533363745 这类内部序号）。
        /// </summary>
        private static bool IsDottedVersion(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int segments = 0, digitsInSegment = 0;
            foreach (var c in name)
            {
                if (c == '.')
                {
                    if (digitsInSegment == 0) return false;  // 空段（"152..1" / ".152"）
                    segments++;
                    digitsInSegment = 0;
                }
                else if (c >= '0' && c <= '9') digitsInSegment++;
                else return false;                           // 含字母/符号 → 非版本目录（SetupMetrics 等）
            }
            return segments >= 1 && digitsInSegment > 0;
        }

        /// <summary>版本号自然比较：a 比 b 新返回正数，相等返回 0，a 比 b 旧返回负数。
        /// 逐段按整数比较（不能按字符串，否则 "99..." 会大于 "152..."），缺失段视作 0。</summary>
        private static int CompareVersionDir(string a, string b)
        {
            var pa = (a ?? "").Split('.');
            var pb = (b ?? "").Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int na = (i < pa.Length && int.TryParse(pa[i], out int x)) ? x : 0;
                int nb = (i < pb.Length && int.TryParse(pb[i], out int y)) ? y : 0;
                if (na != nb) return na - nb;
            }
            return 0;
        }

        /// <summary>列出根目录下所有版本目录，按版本号从新到旧排序；非版本目录一律排除。
        /// 任何异常都吞掉并返回空列表，绝不上抛到 UI 线程。</summary>
        private static List<string> GetVersionDirsNewestFirst(string root)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (IsDottedVersion(name)) list.Add(dir);
                }
                // 新→旧：比较时把参数反过来实现降序
                list.Sort((x, y) => CompareVersionDir(Path.GetFileName(y), Path.GetFileName(x)));
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            return list;
        }

        /// <summary>取根目录下最新的版本目录（已过滤非版本目录）；不存在或异常返回 null。</summary>
        private static string GetLatestVersionDir(string root)
        {
            try
            {
                var dirs = GetVersionDirsNewestFirst(root);
                return dirs.Count > 0 ? dirs[0] : null;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); return null; }
        }

        /// <summary>
        /// 运行时根目录候选：同时覆盖 Program Files 与 Program Files (x86)（已去重、保持 x86 优先）。
        /// 本机 Edge/EdgeWebView 是 x86 装在 Program Files (x86)，但部分机器是 x64 装在 Program Files，
        /// 只查前者会漏掉后者的安装；32 位进程下两者可能同值，故必须去重。
        /// </summary>
        /// <param name="relative">相对 Program Files 的子路径，如 Microsoft\EdgeWebView\Application</param>
        private static List<string> GetRuntimeRootCandidates(string relative)
        {
            var roots = new List<string>();
            var bases = new List<string>();
            try
            {
                Action<string> addBase = p =>
                {
                    // 未展开的环境变量（32 位系统上 %ProgramFiles(x86)% 未定义，会原样返回带 % 的字串）
                    // 直接拼路径会得到一个不存在的目录，这里一律丢弃。
                    if (string.IsNullOrEmpty(p) || p.IndexOf('%') >= 0) return;
                    if (!bases.Any(b => string.Equals(b, p, StringComparison.OrdinalIgnoreCase)))
                        bases.Add(p);
                };
                // x86 优先：本机 Edge/EdgeWebView 就是 x86 装在 Program Files (x86) 下。
                addBase(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%"));
                // 关键：本程序是 32 位进程，%ProgramFiles% 会被 WOW64 虚拟化成 "... (x86)"，
                // 想拿到真正的 64 位 Program Files 只能读 ProgramW6432（32 位系统上不存在 → 自动跳过）。
                try { addBase(Environment.GetEnvironmentVariable("ProgramW6432")); }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                addBase(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%"));
                // 环境变量理论上可能被用户改坏，再补一层 SpecialFolder 作为兜底
                try { addBase(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                try { addBase(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }

            foreach (var b in bases)
            {
                try
                {
                    var full = Path.Combine(b, relative);
                    if (!roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
                        roots.Add(full);
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
            return roots;
        }

        // === WebView2 安装/卸载 ===
        public static void InstallWebView2(Action<string> log)
        {
            log("正在下载 WebView2 Runtime...");
            Exec.RunPowerShell("Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile \"$env:TEMP\\MicrosoftEdgeWebview2Setup.exe\"", log);
            log("正在安装 WebView2 Runtime...");
            // 不经 cmd：原写法 cmd /c "\"路径\"" 会被 Exec.QuoteCmd 把内层 " 翻倍成 ""，
            // TEMP 含空格时 cmd 解析失败、安装静默 no-op。exe 直接作 args[0]（FileName，无需引号）。
            Exec.RunCmd(new[] { Environment.GetEnvironmentVariable("TEMP") + "\\MicrosoftEdgeWebview2Setup.exe", "/silent", "/install" }, log);
            log("WebView2 Runtime 安装/升级完成");

            // 同步就地补上单文件分发所需的 WebView2 探针托管依赖（NuGet 运行时拉取）。
            // 兜底：这一步要联网 + 解压 + 写 exe 同目录，失败绝不能让异常逸出到 UI 线程（net48 下会直接崩进程）。
            try
            {
                WebView2ProbeDeps.EnsureWebView2ProbeDeps(log, p => log(WebView2ProbeDeps.ProgressLine(p)));
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log("[!] 补齐 WebView2 探针依赖失败: " + caughtEx.Message);
            }
        }

        public static void UninstallWebView2(Action<string> log)
        {
            log("正在卸载 WebView2 Runtime...");
            // cmd 不会解析路径中间的 *.* 通配符（%ProgramFiles(x86)%\...\Application\*.*\Installer\setup.exe），
            // 改为在 C# 枚举真实存在的 setup.exe 逐个调用，避免静默 no-op。
            // 根目录候选覆盖 Program Files / Program Files (x86) 两者；只遍历版本目录，
            // 避免把 SetupMetrics 之类非版本目录当成待卸载版本（其下没有 Installer\setup.exe，虽无实际危害但会污染日志判断）。
            var candidates = GetRuntimeRootCandidates(@"Microsoft\EdgeWebView\Application");
            bool found = false;
            try
            {
                foreach (var baseDir in candidates)
                {
                    foreach (var verDir in GetVersionDirsNewestFirst(baseDir))
                    {
                        string setup = Path.Combine(verDir, "Installer", "setup.exe");
                        if (File.Exists(setup))
                        {
                            found = true;
                            Exec.RunCmd(new[] { setup, "--uninstall", "--force-uninstall", "--system-level" }, log);
                        }
                    }
                }
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log("  [!] 枚举 WebView2 安装目录时出错: " + caughtEx.Message);
            }
            if (!found) log("  [!] 未找到 WebView2 Runtime 安装目录（可能已卸载）；已查: " + string.Join("; ", candidates));
            else log("WebView2 Runtime 卸载完成");
        }

        /// <summary>
        /// 修复 / 重装 WebView2 Runtime：先下载官方 Evergreen Bootstrapper 执行静默安装，
        /// 若引导程序因"已安装"而 no-op、注册表仍未恢复，则扫描磁盘上实际存在的运行时目录并补全注册表指针。
        /// 这是应对此前本机"二进制完好但 EdgeWebView\Applications 注册键缺失"导致 EnsureCoreWebView2Async 挂起的根治方案。
        /// </summary>
        public static void RepairWebView2(Action<string> log)
        {
            log("=== 修复 / 重装 WebView2 Runtime ===");
            // 最外层兜底：本方法由 UI 后台线程直接调用，net48 下任何逸出的异常都会直接终止进程，
            // 因此全流程任何一步都不允许把异常抛回调用方（每一步的 try/catch 见下）。
            try
            {
                RepairWebView2Internal(log);
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log("[!] 修复过程发生未预期错误: " + caughtEx.Message);
            }
            finally
            {
                log("=== 修复流程结束 ===");
            }
        }

        /// <summary>RepairWebView2 的实际实现（外层包装只负责异常兜底与结束日志）。</summary>
        private static void RepairWebView2Internal(Action<string> log)
        {
            string bootstrapper = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "MicrosoftEdgeWebViewModelInstaller.exe");

            try
            {
                log("正在下载官方 WebView2 Evergreen Bootstrapper…");
                Exec.RunPowerShell(
                    $"Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile {Exec.QuotePS(bootstrapper)}",
                    log);
            }
            catch (Exception ex)
            {
                log("[!] 下载引导程序失败: " + ex.Message);
                return;
            }

            // 安全加固：下载后校验文件存在且非空，避免后续对损坏/截断的引导程序静默执行
            bool bootstrapperOk = false;
            try { bootstrapperOk = File.Exists(bootstrapper) && new FileInfo(bootstrapper).Length > 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            if (!bootstrapperOk)
            {
                log("[!] 下载后引导程序文件缺失或损坏（为空），无法继续。");
                return;
            }

            log("正在运行引导程序（/silent /install）…");
            int exitCode = -1;
            try
            {
                // 不经 cmd：bootstrapper 位于 exe 同目录（可能含空格），原写法的内层引号会被
                // Exec.QuoteCmd 翻倍成 ""，cmd 无法解析路径 → 引导程序根本没跑起来。
                exitCode = Exec.RunCmd(new[] { bootstrapper, "/silent", "/install" }, log);
            }
            catch (Exception caughtEx)
            {
                // 兜底：引导程序启动失败只影响"重装"这一步，后续的文件终验仍要照常执行，
                // 否则用户会看不到"其实已经装好了"的结论，直接被误导去重装 Edge。
                DebugLog.Ignore(caughtEx);
                log("[!] 运行引导程序异常: " + caughtEx.Message);
            }
            log($"[*] 引导程序退出码: {exitCode}");

            // 注册表指针修复（应对“二进制完好但注册键缺失”的场景）。
            try
            {
                if (!IsWebView2RegistryHealthy())
                {
                    log("[!] 引导程序未恢复注册表（可能判定已安装而跳过），尝试扫描磁盘并修复注册表指针…");
                    RepairWebView2RegistryFromDisk(log);
                }
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log("[!] 注册表检测/修复步骤异常: " + caughtEx.Message);
            }

            // 终验：不仅看注册表，更要看运行时实际文件是否完整。
            // 注意：现代 Edge 主二进制名为 msedge.dll（旧版才叫 chrome.dll），目录里本来就没有 chrome.dll。
            // 真正决定 WebView2 能否初始化的核心文件是 msedgewebview2.exe + msedge.dll。
            // 关键：版本目录必须"先过滤再按版本号自然排序"。旧的
            // Directory.GetDirectories(root).OrderByDescending(d => d).FirstOrDefault()
            // 会选中 SetupMetrics（字符串比较里字母 > 数字）——那正是引导程序自己生成的指标目录，
            // 里面只有 .pma 文件，于是把"本来完好的运行时"误报成"文件仍不完整"。
            var coreFiles = new[] { "msedgewebview2.exe", "msedge.dll" };
            // 根目录候选同时覆盖 Program Files 与 Program Files (x86)：本机是 x86 装在 (x86) 下，
            // 但部分机器是 x64 装在 Program Files 下，只查前者会漏判。
            var runtimeRoots = GetRuntimeRootCandidates(@"Microsoft\EdgeWebView\Application");
            string checkedDir = null;
            var missingFiles = new List<string>();
            foreach (var root in runtimeRoots)
            {
                var vd = GetLatestVersionDir(root);
                if (vd == null) continue;   // 该候选目录下没有版本目录 → 换下一个候选
                checkedDir = vd;
                foreach (var f in coreFiles)
                    if (!File.Exists(Path.Combine(vd, f))) missingFiles.Add(f);
                break;
            }

            // 分情况输出：三种结论的处置方式完全不同，混成一句"文件不完整"会误导用户去无谓重装 Edge。
            if (checkedDir == null)
            {
                log("[!] 未在任何候选目录中找到 WebView2 版本目录（已查: " + string.Join("; ", runtimeRoots)
                    + "）。本机似乎尚未安装 WebView2 Runtime，请先安装 WebView2 Runtime（或 Microsoft Edge）后重试。");
            }
            else if (missingFiles.Count == 0)
            {
                log("[✓] WebView2 Runtime 文件已完整（版本目录: " + checkedDir
                    + "），建议重启本程序后重试 WebView2 探针。");
            }
            else
            {
                log("[!] WebView2 运行时文件不完整：版本目录 " + checkedDir + " 下缺失 "
                    + string.Join("、", missingFiles) + "。本机 WebView2 由 Microsoft Edge 提供，"
                    + "若微软载荷 CDN 不可达 / 同版本不修复，自动修复可能无效，"
                    + "请手动从微软官网下载并重新安装 Microsoft Edge（或运行 Windows 修复），再重试。");
            }

            // 同步就地补上单文件分发所需的 WebView2 探针托管依赖（NuGet 运行时拉取）。
            try
            {
                WebView2ProbeDeps.EnsureWebView2ProbeDeps(log, p => log(WebView2ProbeDeps.ProgressLine(p)));
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log("[!] 补齐 WebView2 探针依赖失败: " + caughtEx.Message);
            }
        }

        /// <summary>检测 WebView2 运行时注册表根键是否健康（EdgeWebView\Applications 存在且有子键）。</summary>
        private static bool IsWebView2RegistryHealthy()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeWebView\Applications"))
                    return key != null && key.SubKeyCount > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 扫描磁盘上 WebView2 Runtime 的实际安装目录，并把找到的版本目录写入注册表 Applications 下。
        /// 这是 CreateAsync(null) 能正常解析运行时的最小必要注册信息。
        /// </summary>
        private static void RepairWebView2RegistryFromDisk(Action<string> log)
        {
            // 两个 Program Files 都作为候选根（本机 x86 装在 (x86) 下，部分机器 x64 装在 Program Files 下）
            var runtimeRoots = GetRuntimeRootCandidates(@"Microsoft\EdgeWebView\Application");
            var edgeRoots = GetRuntimeRootCandidates(@"Microsoft\Edge\Application");
            string foundVer = null;
            string foundDir = null;

            // 优先扫描 WebView2 Runtime 目录下的版本子目录。
            // 只遍历版本目录并按版本号从新到旧：既排除 SetupMetrics 等非版本目录，
            // 也保证"取到的一定是存在 msedgewebview2.exe 的最新版本"而不是字符串序最大的那个。
            foreach (var runtimeRoot in runtimeRoots)
            {
                foreach (var d in GetVersionDirsNewestFirst(runtimeRoot))
                {
                    if (File.Exists(Path.Combine(d, "msedgewebview2.exe")))
                    {
                        foundDir = d;
                        foundVer = Path.GetFileName(d);
                        break;
                    }
                }
                if (foundDir != null) break;
            }

            // 兜底：扫描完整 Edge 目录（Edge 浏览器自带 msedgewebview2.exe，可作为运行时来源）
            if (foundDir == null)
            {
                foreach (var edgeRoot in edgeRoots)
                {
                    foreach (var d in GetVersionDirsNewestFirst(edgeRoot))
                    {
                        if (File.Exists(Path.Combine(d, "msedge.exe")) && File.Exists(Path.Combine(d, "msedgewebview2.exe")))
                        {
                            foundDir = d;
                            foundVer = Path.GetFileName(d);
                            break;
                        }
                    }
                    if (foundDir != null) break;
                }
            }

            if (foundDir == null)
            {
                log("[!] 未在磁盘上找到可用的 WebView2 Runtime 目录，无法修复注册表。已查: "
                    + string.Join("; ", runtimeRoots) + " / " + string.Join("; ", edgeRoots));
                return;
            }

            log("[*] 找到运行时目录: " + foundDir);

            // 写 32 位视图（WOW6432Node）——这是 .NET 4.8 x64 进程通过 32 位 WebView2 loader 读取的位置。
            WriteWebView2AppRegistration(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeWebView\Applications", foundVer, foundDir, log);
            // 同时补 64 位视图，便于其他 64 位进程/未来使用。
            WriteWebView2AppRegistration(Registry.LocalMachine, @"SOFTWARE\Microsoft\EdgeWebView\Applications", foundVer, foundDir, log);
        }

        private static void WriteWebView2AppRegistration(RegistryKey hive, string basePath, string version, string dir, Action<string> log)
        {
            try
            {
                using (var baseKey = hive.CreateSubKey(basePath))
                {
                    if (baseKey == null)
                    {
                        log("[!] 无法创建/打开注册表项: " + basePath);
                        return;
                    }
                    using (var verKey = baseKey.CreateSubKey(version))
                    {
                        verKey?.SetValue("", dir);
                        verKey?.SetValue("pv", version);
                    }
                }
                log("[*] 已写入注册表: " + basePath + "\\" + version);
            }
            catch (Exception ex)
            {
                log("[!] 写入注册表失败: " + basePath + " — " + ex.Message);
            }
        }

        // === 安装 Edge ===
        public static void InstallEdge(string channel, Action<string> log)
        {
            var info = FindChannel(channel);
            if (info == null)
            {
                log("不支持的频道: " + (string.IsNullOrEmpty(channel) ? "(空)" : channel)
                    + "（可选: " + KnownChannelList() + "）");
                return;
            }
            if (string.IsNullOrEmpty(info.InstallUrl))
            {
                log("不支持的频道: " + info.DisplayName + " 没有可用的官方静默安装包，请前往微软 Insider 官网手动安装。");
                return;
            }
            string url = info.InstallUrl;
            log($"正在下载 Edge {channel}...");
            Exec.RunPowerShell($"Invoke-WebRequest -Uri '{url}' -OutFile \"$env:TEMP\\MicrosoftEdgeSetup.exe\"", log);
            log("正在安装...");
            // 不要写成 cmd /c "\"路径\"" ——Exec.QuoteCmd 会把内层 " 翻倍成 ""，
            // TEMP 含空格（如 C:\Users\张 三\AppData\Local\Temp）时 cmd 解析失败、安装静默 no-op。
            // 直接把 exe 作为 args[0]（ProcessStartInfo.FileName，无需引号），参数各自独立传。
            Exec.RunCmd(new[] { Environment.GetEnvironmentVariable("TEMP") + "\\MicrosoftEdgeSetup.exe", "/silent", "/install" }, log);
            log($"Edge {channel} 安装完成");
        }

        // === 卸载 Edge ===
        /// <summary>卸载指定频道的 Edge。返回是否"确实执行了卸载动作"：
        /// 频道未知或该频道不支持自动卸载时返回 false —— 调用方（UI）必须据此决定是否报"卸载完成"，
        /// 不能像原实现那样无条件弹成功（选 Canary/SxS 时 EdgeCore 只打一行日志就返回，UI 却报卸载完成）。</summary>
        public static bool UninstallEdge(string channel, bool forceClean, Action<string> log)
        {
            var info = FindChannel(channel);
            if (info == null)
            {
                log("不支持的频道: " + (string.IsNullOrEmpty(channel) ? "(空)" : channel)
                    + "（可选: " + KnownChannelList() + "）；已跳过，未执行任何卸载操作。");
                return false;
            }
            if (!info.CanUninstall || string.IsNullOrEmpty(info.UpdateClientPath))
            {
                log("不支持的频道: " + info.DisplayName + " 属于当前用户级安装（不在系统级卸载键下），"
                    + "本工具的系统级卸载流程对其无效；已跳过，请在「设置 → 应用 → 已安装的应用」中卸载。");
                return false;
            }

            string uninstallKeyPath = info.UninstallKeyPath;
            string regPath = info.UpdateClientPath;
            string displayName = info.DisplayName;

            log($"=== 卸载 Edge {channel} ===");

            // 结束进程
            Exec.RunCmd(new[] { "taskkill", "/f", "/im", "msedge.exe" }, log);

            // 读卸载字符串
            string uninstallString = null;
            try
            {
                // 注意：uninstallKeyPath 已是 "...\Uninstall\Microsoft Edge" 完整键路径，
                // 不要再拼 displayName（会变成不存在的重复路径），直接打开该键读取。
                using (var key = Registry.LocalMachine.OpenSubKey(uninstallKeyPath))
                    uninstallString = key?.GetValue("UninstallString")?.ToString();
                if (uninstallString == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        var loc = key?.GetValue("location")?.ToString();
                        if (loc != null)
                        {
                            var setupPath = Path.Combine(loc, "..", "..", "Install", "setup.exe");
                            if (File.Exists(setupPath))
                                uninstallString = $"\"{Path.GetFullPath(setupPath)}\" --uninstall --system-level --verbose-logging --force-uninstall";
                        }
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

            if (string.IsNullOrEmpty(uninstallString))
            {
                log("未找到卸载信息，使用强制卸载");
                ForceCleanupEdge(displayName, log);
                return true;
            }

            log("执行卸载命令: " + uninstallString);
            // uninstallString 是注册表里已成型的命令行（exe 路径本身带引号），
            // 再包一层 cmd /c 交给 Exec.QuoteCmd 会把内层 " 翻倍成 ""：
            //   "C:\Program Files (x86)\...\setup.exe" --uninstall
            //   → cmd /c """C:\Program Files (x86)\...\setup.exe"" --uninstall"
            // cmd 去掉首尾引号后剩下 ""C:\Program Files...""，路径被空格拆断，卸载静默失败。
            // 故这里按 Windows 引号规则拆成 argv 直接启动 setup.exe，不再经 cmd 转手。
            var argv = SplitCommandLine(uninstallString);
            if (argv.Length == 0)
            {
                log("[!] 卸载命令无法解析，改用强制清理");
                ForceCleanupEdge(displayName, log);
                return true;
            }
            Exec.RunCmd(argv, log);

            if (forceClean)
            {
                log("执行强制清理...");
                ForceCleanupEdge(displayName, log);
            }

            log($"Edge {channel} 卸载完成");
            return true;
        }

        /// <summary>
        /// 按 Windows 命令行引号规则把"已成型的命令行"拆成 argv（args[0]=可执行文件路径）。
        /// 用途：注册表 UninstallString 自带引号，不能再交给 Exec.QuoteCmd 二次转义。
        /// 规则：双引号成对开关引用状态；引用态内的 "" 表示一个字面双引号；引用态外的空格/制表符分隔参数。
        /// </summary>
        private static string[] SplitCommandLine(string commandLine)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine)) return args.ToArray();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                    {
                        sb.Append('"');   // "" → 一个字面双引号
                        i++;
                    }
                    else inQuotes = !inQuotes;
                    hasToken = true;      // 空引号 "" 也算一个（空）参数
                }
                else if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    if (hasToken)
                    {
                        args.Add(sb.ToString());
                        sb.Length = 0;
                        hasToken = false;
                    }
                }
                else
                {
                    sb.Append(c);
                    hasToken = true;
                }
            }
            if (hasToken) args.Add(sb.ToString());
            return args.ToArray();
        }

        /// <summary>
        /// 待删目录是否为明确的 Edge 目录：展开环境变量并规范化后，路径必须包含
        /// Microsoft\Edge 或 Microsoft\EdgeWebView（分隔符统一为 '\'，大小写不敏感）。
        /// 强制清理用的是 rd /S /Q 递归删除，一旦环境变量展开失败或路径拼接异常就会删错目录，
        /// 因此这里先做身份校验，缺少 Edge 标识的路径一律不删（防止误删用户数据）。
        /// </summary>
        private static bool IsEdgeInstallPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            if (fullPath.IndexOf('%') >= 0) return false;   // 环境变量未展开（路径不可信），视为无效
            string norm;
            try { norm = Path.GetFullPath(fullPath).Replace('/', '\\'); }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); return false; }
            return norm.IndexOf(@"\microsoft\edge", StringComparison.OrdinalIgnoreCase) >= 0
                || norm.IndexOf(@"\microsoft\edgewebview", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ForceCleanupEdge(string displayName, Action<string> log)
        {
            // 防误删校验：删除前先把待删目录展开成真实路径并逐个校验 Edge 标识。
            // 必需的 Edge 安装目录校验不过 → 整体中止并记录日志；附属缓存目录校验不过 → 仅跳过该项。
            // 按频道 identity 派生渠道专属目录名：DisplayName 形如 "Microsoft Edge" / "Microsoft Edge Beta" /
            // "Microsoft Edge Dev"，去掉前缀 "Microsoft " 得到 "Edge" / "Edge Beta" / "Edge Dev"，
            // 据此拼出该频道专属的安装/缓存目录，避免卸载 Beta/Dev 时误删 Stable 目录。
            // 仅 Stable 频道（sub == "Edge"）额外清理 EdgeCore 与 Temp 残留子目录（Beta/Dev 无这些目录）。
            string sub = (displayName != null ? displayName.Replace("Microsoft ", "") : "").Trim();
            if (string.IsNullOrWhiteSpace(sub)) sub = "Edge";
            bool isStable = string.Equals(sub, "Edge", StringComparison.OrdinalIgnoreCase);
            var targets = isStable
                ? new[]
                {
                    new { Path = @"%ProgramFiles(x86)%\Microsoft\Edge",     Required = true  },
                    new { Path = @"%ProgramFiles(x86)%\Microsoft\EdgeCore", Required = true  },
                    new { Path = @"%LocalAppData%\Microsoft\Edge",          Required = true  },
                    new { Path = @"%ProgramFiles(x86)%\Microsoft\Temp",     Required = false }
                }
                : new[]
                {
                    new { Path = @"%ProgramFiles(x86)%\Microsoft\" + sub,   Required = true  },
                    new { Path = @"%LocalAppData%\Microsoft\" + sub,        Required = true  }
                };
            var dirs = new List<string>();
            foreach (var t in targets)
            {
                string full = Exec.ExpandEnv(t.Path);
                if (IsEdgeInstallPath(full)) { dirs.Add(full); continue; }
                if (t.Required)
                {
                    log("  [!] 强制清理已中止：待删路径缺少 Edge 标识（防止误删用户数据）: " + (string.IsNullOrEmpty(full) ? t.Path : full));
                    return;
                }
                log("  [SKIP] 非 Edge 目录，跳过删除（防止误删用户数据）: " + (string.IsNullOrEmpty(full) ? t.Path : full));
            }

            // 清理安装目录
            // rd 是 cmd 内置命令，必须经 cmd 执行；但待删路径要作为**独立参数**交给 Exec.QuoteCmd 加引号，
            // 不能自己先拼好 "rd /S /Q \"路径\"" ——那样内层 " 会被 QuoteCmd 翻倍成 ""，
            // 含空格的 Program Files 路径被拆断，rd 报错、目录实际没删掉。
            foreach (var d in dirs)
                Exec.RunCmd(new[] { "cmd", "/c", "rd", "/S", "/Q", d }, log);

            // 清理注册表
            using (var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet", true)) k1?.DeleteSubKeyTree(displayName, false);
            using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true)) k2?.DeleteSubKeyTree(displayName, false);
            using (var classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", true))
            {
                classes?.DeleteSubKeyTree("MSEdgeHTM", false);
                classes?.DeleteSubKeyTree("MSEdgePDF", false);

                // 清理 EdgeUpdate 相关键
                foreach (var name in new[] { "MicrosoftEdgeUpdate.", "microsoft-edge" })
                    classes?.DeleteSubKeyTree(name, false);
            }

            log("强制清理完成");
        }

        // === 禁止 Edge 自动更新 ===
        public static void BlockEdgeUpdate(Action<string> log)
        {
            log("=== 禁止 Edge + WebView2 自动更新 ===");

            // 删除更新服务
            foreach (var svc in new[] { "edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService" })
            {
                Exec.RunCmd(new[] { "sc", "stop", svc }, log);
                Exec.RunCmd(new[] { "sc", "delete", svc }, log);
            }

            // 删除更新计划任务
            Exec.RunPowerShell("Get-ScheduledTask -TaskPath '\\Microsoft\\EdgeUpdate\\' -ErrorAction SilentlyContinue | Disable-ScheduledTask | Out-Null", log);
            Exec.RunPowerShell("schtasks /delete /tn 'MicrosoftEdgeUpdateTask' /f 2>nul", log);

            // 删除更新目录
            // 同上：路径作为独立参数传，避免内层引号被 QuoteCmd 翻倍；
            // 顺带去掉结尾的 '\'（对 rd 无意义，且会触发 QuoteCmd 的"结尾反斜杠 2n+1"转义分支）。
            Exec.RunCmd(new[] { "cmd", "/c", "rd", "/S", "/Q", Exec.ExpandEnv(@"%ProgramFiles(x86)%\Microsoft\EdgeUpdate") }, log);

            // 删除更新注册表
            // 旧写法 DeleteSubKeyTree("", false) 传入空子键名会抛 ArgumentException，
            // 而本方法此前无 try/catch，异常会直接中断下面两条 SetEdgePolicy 的写入。
            // 改用 RegistryHelper.DeleteKeyTree：内部 try/catch + 64/32 双视图 + 删后二次校验，
            // 保证策略写入一定执行，且失败只记日志不影响后续步骤。
            // 删除失败（如 DACL 拒绝）时记一行告警，不要静默吞掉 —— 组策略仍会写入，
            // 但日志必须如实反映，避免"显示已禁止、实际键还在"的假成功。
            if (!RegistryHelper.DeleteKeyTree(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate", log))
                log("  [!] EdgeUpdate 注册表键未能完全删除（组策略仍会写入，不影响禁止更新生效）");

            // 阻止 Edge 启动增强（同时写 HKCU 与 HKLM，确保任一 hive 下都生效）
            RegistryHelper.SetEdgePolicy("StartupBoostEnabled", 0, log);
            RegistryHelper.SetEdgePolicy("BackgroundModeEnabled", 0, log);

            log("Edge 自动更新已禁止");
        }

        // === 恢复 Edge 更新 ===
        public static void RestoreEdgeUpdate(Action<string> log)
        {
            log("=== 恢复 Edge 自动更新 ===");
            // 同时清 HKCU 与 HKLM（含 Recommended 子键），避免残留于任一 hive 导致 edge://management 仍报「由组织管理」
            RegistryHelper.DeleteEdgePolicyTree(log);
            log("已清除 Edge 组策略限制");
        }

        // === Edge 启动增强 ===
        public static bool IsStartupBoostEnabled()
        {
            try
            {
                // 任一 hive（HKCU/HKLM）将 StartupBoostEnabled 显式置 0 即视为已禁用
                foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using (var key = hive.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
                    {
                        if (key?.GetValue("StartupBoostEnabled") is int val && val == 0) return false;
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return true; // 默认启用
        }

        public static void SetStartupBoost(bool enabled, Action<string> log)
        {
            if (enabled)
                // 恢复启动增强：两 hive 都清（含 Recommended 子键），移除 StartupBoostEnabled/BackgroundModeEnabled 等限制
                RegistryHelper.DeleteEdgePolicyTree(log);
            else
            {
                RegistryHelper.SetEdgePolicy("StartupBoostEnabled", 0, log);
                RegistryHelper.SetEdgePolicy("BackgroundModeEnabled", 0, log);
            }
        }

        // === Edge 实验性 Flags（edge://flags，注册表 HKCU\Software\Microsoft\Edge\EdgeFlags）===
        public const string EdgeFlagsRegPath = @"Software\Microsoft\Edge\EdgeFlags";

        /// <summary>读取指定 flag 当前值；未设置（Default）返回 null。</summary>
        public static string GetEdgeFlag(string flagName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(EdgeFlagsRegPath))
                    return key?.GetValue(flagName) as string;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); return null; }
        }

        /// <summary>设置 flag 值（"1"/"0"/枚举字符串）。</summary>
        public static void SetEdgeFlag(string flagName, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(EdgeFlagsRegPath))
                    key?.SetValue(flagName, value ?? "", Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
        }

        /// <summary>删除 flag（= 恢复 Edge 默认 Default）。</summary>
        public static void ClearEdgeFlag(string flagName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(EdgeFlagsRegPath, true))
                    key?.DeleteValue(flagName, false);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
        }

        /// <summary>供 UI 展示的 flag 元数据：Key=注册表值名，Label=界面中文名，Values=可选值（首元素为默认值），Recommend=推荐值（对应 Values 元素，UI 上标 ⭐）。</summary>
        public static readonly (string Key, string Label, string[] Values, string Recommend)[] EdgeFlagDefs =
        {
            ("use-angle", "ANGLE 图形后端", new[] { "gl", "d3d11", "d3d11on12", "vulkan", "swiftshader" }, "default"),
            ("edge-copilot-mode", "Edge Copilot 模式", new[] { "disabled", "enabled", "optin" }, "disabled"),
            ("enable-parallel-downloading", "并行下载", new[] { "1", "0" }, "1"),
            ("enable-gpu-rasterization", "GPU 栅格化", new[] { "1", "0" }, "1"),
            ("enable-accelerated-video-decode", "硬件加速视频解码", new[] { "1", "0" }, "1"),
            ("enable-quic", "QUIC / HTTP/3 协议", new[] { "1", "0" }, "1"),
            ("back-forward-cache", "前进/后退缓存", new[] { "1", "0" }, "1"),
            ("smooth-scrolling", "平滑滚动", new[] { "1", "0" }, "1"),
            ("enable-tls13-early-data", "TLS 1.3 Early Data", new[] { "1", "0" }, "1"),
            ("enable-force-dark", "强制深色模式", new[] { "1", "0" }, "0"),
            ("edge-overlay-scrollbars-win-style", "Fluent 悬浮滚动条", new[] { "1", "0" }, "1"),
        };

        /// <summary>按元数据写 flag：value 为 null/空串/字面量 "default"（=Edge 出厂默认语义）时删除注册表值，否则显式写入。
        /// ⚠ 不得用 value==Values[0] 判断默认：Values[0] 只是 Edge 出厂值（如开关类 "1"），显式写 "1" 仍是「启用」而非「默认」——
        /// 否则一键优化推荐值 =Values[0] 的 9 项会被误清（曾致注册表只写入 enable-force-dark 一项）。</summary>
        public static void ApplyEdgeFlag(string key, string value)
        {
            bool isDefault = string.IsNullOrEmpty(value) || value == "default";
            if (isDefault) ClearEdgeFlag(key);
            else SetEdgeFlag(key, value);
        }

        /// <summary>把所有 flags 一次性设为 EdgeFlagDefs 中的推荐值。返回应用成功的项数；逐一记日志。</summary>
        public static int ApplyAllRecommendedFlags(Action<string> log)
        {
            int ok = 0;
            foreach (var def in EdgeFlagDefs)
            {
                ApplyEdgeFlag(def.Key, def.Recommend);
                log?.Invoke("⚡ 已设推荐值：" + def.Label + " = " + def.Recommend);
                ok++;
            }
            return ok;
        }

        /// <summary>清除 EdgeFlags 注册表下本程序管理的所有 11 项（恢复 Edge 默认）。</summary>
        public static int ClearAllEdgeFlags(Action<string> log)
        {
            int ok = 0;
            foreach (var def in EdgeFlagDefs)
            {
                ClearEdgeFlag(def.Key);
                log?.Invoke("↩ 已恢复默认：" + def.Label);
                ok++;
            }
            return ok;
        }

        /// <summary>强制结束所有 msedge.exe 进程并重新启动（让 flags 立即生效）。返回是否成功重启。
        /// 注意：会丢失 Edge 未保存的标签页/表单——UI 端必须先弹确认。</summary>
        public static bool ForceRestartEdge(Action<string> log)
        {
            try
            {
                // 1. 结束所有 msedge.exe（包括 helper 进程）
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM msedge.exe /T",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(5000); // 最多等 5 秒
                }
                System.Threading.Thread.Sleep(800); // 缓冲：等进程彻底退出
                log?.Invoke("✓ 已结束所有 Edge 进程");

                // 2. 重新启动 Edge（PATH 解析 msedge.exe；无参 = 默认用户 profile）
                Process.Start(new ProcessStartInfo
                {
                    FileName = "msedge.exe",
                    UseShellExecute = true
                });
                log?.Invoke("→ 已启动新 Edge 进程（flags 生效）");
                return true;
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log?.Invoke("✗ 重启 Edge 失败：" + caughtEx.Message);
                return false;
            }
        }
    }
}
