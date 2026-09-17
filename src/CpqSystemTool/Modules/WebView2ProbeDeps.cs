using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CpqSystemTool
{
    /// <summary>
    /// 运行时按需补齐 WebView2 探针依赖。历史：net48 时代托管 DLL 未随发布、需从 NuGet 拉 3 个托管程序集；
    /// .NET 10 单文件下托管程序集（Core/WinForms/Wpf）已内嵌进 exe（deps 闭包），只剩原生
    /// WebView2Loader.dll 必须与 exe 并列（P/Invoke 解析规则：只能从 exe 所在目录找，不能放 cpq-tool）。
    /// 诊断日志写入数据根（cpq-tool\webview2_deps.log），不再散到 exe 目录。
    /// 该特性在用户点击“修复/安装 WebView2”以及探针初始化前都会触发。
    /// 全程不抛异常：失败仅通过 log 报告，探针随后会按既有逻辑回退到 Node 方案。
    /// </summary>
    internal static class WebView2ProbeDeps
    {
        // 与项目构建所针对的 Microsoft.Web.WebView2 版本一致（RESOLVED VERSION）。
        private const string WebView2PkgVersion = "1.0.2045.28";
        private const string NuGetFlatContainerBase = "https://api.nuget.org/v3-flatcontainer";
        private const int DownloadTimeoutMs = 60000;

        /// <summary>
        /// 持久化诊断日志：写入 exe 目录下的 webview2_deps.log，便于离线排查下载/解压失败根因。
        /// </summary>
        private static void WriteDepsLog(string message)
        {
            try
            {
                // 日志归位数据根（cpq-tool），与 exe 目录解耦（跟随文件原则）；单文件下
                // Assembly.Location 恒空（IL3000），路径一律走 AppPaths。
                string dir = AppPaths.DataRoot;
                Directory.CreateDirectory(dir);
                string logPath = Path.Combine(dir, "webview2_deps.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { /* 日志写入失败不得影响主逻辑 */ }
        }

        /// <summary>
        /// 构造下载进度行：以 \r 开头表示“原地刷新日志框最后一行”（避免刷屏）。
        /// 集中在此处，避免进度文案在多处硬编码重复。
        /// </summary>
        public static string ProgressLine(int percent) => "\r[下载 WebView2 依赖 " + percent + "%]";

        /// <summary>
        /// 确保 WebView2 探针依赖齐备：原生 loader 与 exe 并列（缺则从 NuGet 拉），
        /// 托管程序集已内嵌单文件无需拉取。已齐备则立即返回（幂等）；
        /// 下载/解压失败时通过 log 报告并优雅返回（不抛异常）。
        /// 本方法为真正异步实现：下载在后台线程池执行，调用方（含 UI/STA 线程）await 它即可，
        /// 无需自行用 Task.Run 包裹，避免依赖“调用方约定”来保障不冻结界面。
        /// </summary>
        public static async Task EnsureWebView2ProbeDepsAsync(Action<string> log, Action<int> progress = null)
        {
            if (log == null) log = s => { };
            WriteDepsLog("=== EnsureWebView2ProbeDeps 开始 ===");

            // 单文件下 Assembly.GetExecutingAssembly().Location 恒为空（IL3000 警告），
            // 必须用 AppPaths.ExeDir（Environment.ProcessPath 优先）拿真实 exe 目录。
            string exeDir = AppPaths.ExeDir;
            if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
            {
                log("[!] exe 目录不可用，跳过 WebView2 探针依赖补齐：" + (exeDir ?? "null"));
                WriteDepsLog("[!] exe 目录不可用：" + (exeDir ?? "null"));
                return;
            }
            WriteDepsLog("exeDir=" + exeDir);

            // .NET 10 单文件：托管程序集（Core/WinForms/Wpf）已内嵌进 exe（deps 闭包），
            // 不再需要从 NuGet 拉 3 个托管 DLL（net48 时代遗留逻辑，已移除）。
            // 唯一需要磁盘落位的是原生 WebView2Loader.dll——P/Invoke 只从 exe 所在目录解析，
            // 故它必须与 exe 并列（不能挪到 cpq-tool），缺了才下载。
            string loader = Path.Combine(exeDir, "WebView2Loader.dll");
            if (File.Exists(loader))
            {
                log("[*] WebView2 探针依赖齐备（原生 loader 就位；托管程序集已内嵌）。");
                WriteDepsLog("[*] loader 已存在，跳过下载");
                return;
            }

            log("[*] 缺少 WebView2 原生 loader，尝试从 NuGet 下载（版本 " + WebView2PkgVersion + "）…");
            WriteDepsLog("[*] 开始下载 nupkg，版本=" + WebView2PkgVersion);

            string nupkgUrl = $"{NuGetFlatContainerBase}/microsoft.web.webview2/{WebView2PkgVersion}/microsoft.web.webview2.{WebView2PkgVersion}.nupkg";
            WriteDepsLog("URL=" + nupkgUrl);
            string tempNupkg = null;
            try
            {
                tempNupkg = Path.Combine(Path.GetTempPath(), "Microsoft.Web.WebView2." + WebView2PkgVersion + ".nupkg");
                WriteDepsLog("tempNupkg=" + tempNupkg);
                await DownloadFileWithClientAsync(nupkgUrl, tempNupkg, log, progress).ConfigureAwait(false);
                WriteDepsLog("[*] nupkg 下载完成，大小=" + new FileInfo(tempNupkg).Length);

                log("[*] 下载完成，开始解压原生 loader 到 exe 目录…");
                ExtractNativeLoader(tempNupkg, exeDir, log);
                // 真实验证：解压后 loader 必须真实存在于磁盘才算成功，避免“假成功”掩盖失败。
                bool extracted = File.Exists(loader);
                if (extracted)
                {
                    log("[✓] WebView2 原生 loader 已就地写入：" + loader);
                    WriteDepsLog("[✓] 解压完成，loader 存在=" + extracted);
                }
                else
                {
                    string w = "[!] 解压后仍未在 exe 目录找到 WebView2Loader.dll，WebView2 探针可能不可用（将回退 Node）";
                    log(w);
                    WriteDepsLog(w);
                }
            }
            catch (Exception ex)
            {
                log("[!] 拉取 WebView2 探针依赖失败（将回退 Node 方案）：" + ex.GetType().Name + " - " + ex.Message);
                WriteDepsLog("[!] 拉取失败：" + ex);
            }
            finally
            {
                if (tempNupkg != null)
                {
                    try { if (File.Exists(tempNupkg)) File.Delete(tempNupkg); } catch { /* 临时文件清理失败可忽略 */ }
                }
                WriteDepsLog("=== EnsureWebView2ProbeDeps 结束 ===");
            }
        }

        /// <summary>
        /// 同步兼容包装：供后台线程（RunInBg 调用路径）使用，阻塞等待 EnsureWebView2ProbeDepsAsync 完成。
        /// 内部全程 ConfigureAwait(false) + Task.Run，不会在调用方 SynchronizationContext 上死锁；
        /// UI/STA 线程请改用 EnsureWebView2ProbeDepsAsync 并 await，避免阻塞界面。
        /// </summary>
        public static void EnsureWebView2ProbeDeps(Action<string> log, Action<int> progress = null)
            => EnsureWebView2ProbeDepsAsync(log, progress).GetAwaiter().GetResult();

        /// <summary>
        /// 异步下载 nupkg 到临时文件，并在下载过程中通过 progress 回调报告百分比（0–100）。
        /// 内部统一走 Downloader（基于 HttpClients.Default 单例 + 请求级 CTS 超时），进度语义与原实现一致；
        /// 失败返回 false 时抛异常，由外层 EnsureWebView2ProbeDepsAsync 的 try/catch 统一报告并回退 Node 方案。
        /// </summary>
        private static async Task DownloadFileWithClientAsync(string url, string destPath, Action<string> log, Action<int> progress)
        {
            bool ok = await Downloader.DownloadAsync(url, destPath, log, progress,
                maxAttempts: 1, timeoutMs: DownloadTimeoutMs, readTimeoutMs: 60000).ConfigureAwait(false);
            if (!ok)
                throw new IOException("下载 WebView2 依赖失败（详见日志）");
        }

        /// <summary>只解压原生 loader（按进程位数选 win-x64/win-x86 条目）。托管 DLL 不再需要（.NET 10 单文件已内嵌）。</summary>
        private static void ExtractNativeLoader(string nupkgPath, string exeDir, Action<string> log)
        {
            using (var archive = ZipFile.OpenRead(nupkgPath))
            {
                // 原生 loader 按进程位数选择（64 位进程取 win-x64 条目）。
                string nativeHint = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                ExtractByMatch(archive, exeDir, log,
                    f => f.IndexOf(nativeHint, StringComparison.OrdinalIgnoreCase) >= 0
                         && f.EndsWith("WebView2Loader.dll", StringComparison.OrdinalIgnoreCase),
                    "WebView2Loader.dll");
            }
        }

        private static void ExtractByMatch(ZipArchive archive, string exeDir, Action<string> log, Func<string, bool> match, string fileName)
        {
            ZipArchiveEntry entry = null;
            foreach (var e in archive.Entries)
            {
                if (match(e.FullName)) { entry = e; break; }
            }
            if (entry == null)
            {
                string msg = "[!] nupkg 中未找到条目：" + fileName;
                log(msg); WriteDepsLog(msg);
                return;
            }

            string target = Path.Combine(exeDir, fileName);

            // 跳过已存在（不覆盖），避免破坏用户可能手动放置的新版本 DLL。
            if (File.Exists(target)) { string s = "[*] 已存在，跳过：" + fileName; log(s); WriteDepsLog(s); return; }

            try
            {
                using (var src = entry.Open())
                using (var dst = File.Create(target))
                {
                    src.CopyTo(dst);
                }
                string ok = "[✓] 已写入：" + fileName;
                log(ok); WriteDepsLog(ok);
            }
            catch (Exception ioEx)
            {
                // 目录只读（如 Program Files 安装位置）会抛 UnauthorizedAccessException（IOException 的兄弟类，非子类），
                // 故捕获 Exception 而非仅 IOException；记录并继续，不崩溃。
                string e2 = "[!] 写入 " + fileName + " 失败（目录可能只读或无权限）：" + ioEx.Message;
                log(e2); WriteDepsLog(e2);
            }
        }
    }
}
