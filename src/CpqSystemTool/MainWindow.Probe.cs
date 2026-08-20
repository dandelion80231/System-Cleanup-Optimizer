using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // ===================== 结果面板行模型（DataGrid 绑定） =====================
        internal class ProbeCandidateRow
        {
            public string Source { get; set; }        // 来源入口
            public string Url { get; set; }           // 直链
            public string Strategy { get; set; }      // 命中策略
            public string Arch { get; set; }          // 架构：x64 / x86 / arm64 / ?
            public bool Verified { get; set; }        // 是否验证为真 exe
            public string VerifiedText => Verified ? "✅ 真exe" : "⚠️ 未验证";
            public string RecMark => IsRecommended ? "★" : "";
            public string StatusText { get; set; }    // HTTP 状态 / SKIP / TIMEOUT / ERR
            public string ContentType { get; set; }   // Content-Type
            public bool IsRecommended { get; set; }   // 是否推荐直链（高亮）
            public bool LowTrust { get; set; }        // 搜索来源且非官方域名：需人工核对（仿冒风险）
            public string TrustText => LowTrust ? "⚠ 需核对" : "官方";
        }

        // 运行时缓存：后台线程写入，UI 线程（onDoneUi）读取
        private List<ProbeCandidateRow> _probeRows = new List<ProbeCandidateRow>();
        private string _probeRecommendedUrl = "";
        private bool _probeSearchLocated = false; // 结果是否由搜索引擎定位（用于 UI 提示人工核对）

        // ===================== 路径定位 =====================

        /// <summary>
        /// 定位探针目录 tools/probes：从程序所在目录逐级向上查找。
        /// 找不到则返回 null，由调用方提示用户放置 probes 目录，不再回退到开发者本机固定路径。
        /// </summary>
        private static string ResolveProbesDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            for (var d = new DirectoryInfo(baseDir); d != null; d = d.Parent)
            {
                var cand = Path.Combine(d.FullName, "tools", "probes");
                if (Directory.Exists(cand)) return cand;
            }
            return null;
        }

        // ===================== 输入名称 → 入口 URL 解析 =====================

        /// <summary>
        /// 把用户在「维护工具」页输入的软件名/别名解析为探针可识别的入口 URL。
        /// 若是 http(s) 开头则原样返回；否则先查别名表（中文/英文/简称）。
        /// </summary>
        private static string ResolveProbeEntry(string input)
        {
            // 空字符串/过短（<1 字符）视为无效输入，返回 null 由调用方日志提示
            if (string.IsNullOrWhiteSpace(input)) return null;
            var trimmed = input.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            var key = trimmed.ToLowerInvariant();
            switch (key)
            {
                // 腾讯 QQ
                case "qq":
                case "qqnt":
                case "腾讯qq":
                case "pcqq":
                    return "https://im.qq.com/pcqq/";
                // QQ 音乐
                case "qq音乐":
                case "qqmusic":
                    return "https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback";
                // 抖音
                case "抖音":
                case "抖音pc":
                case "抖音电脑版":
                case "douyin":
                    return "https://www.douyin.com/downloadpage";
                // 搜狗拼音
                case "搜狗":
                case "搜狗拼音":
                case "搜狗输入法":
                case "sogou":
                case "sogoupinyin":
                    return "https://pinyin.sogou.com/";
                // 123 云盘
                case "123云盘":
                case "123pan":
                    return "https://www.123pan.com/";
                // 阿里云盘
                case "阿里云盘":
                case "aliyunpan":
                case "alipan":
                case "阿里网盘":
                    return "https://www.aliyundrive.com/download";
                // RayLink / 瑞联
                case "raylink":
                case "瑞联":
                    return "https://www.raylink.com/";
                // Xshell
                case "xshell":
                case "xshell7":
                case "netsarang":
                    return "https://www.netsarang.com/en/xshell/";
            }
            // 未命中别名表：原样返回，探针自己的 VENDOR_MAP 会再做一次兜底解析
            return trimmed;
        }

        // ===================== Node 解析（优先系统 PATH） =====================

        /// <summary>
        /// 解析可用的 Node 可执行文件：优先系统 PATH 的 node（用户本机可能已装），
        /// 其次项目内 .tools\node\node.exe，都没有则返回 null。
        /// </summary>
        private static string ResolveNodeExe(string probesDir)
        {
            // 1) 系统 PATH 的 node（where node）
            try
            {
                var psi = new ProcessStartInfo("where", "node")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string outp = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(10000)) { try { p.Kill(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); } }
                        var lines = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var firstLine = lines.Length > 0 ? lines[0] : null;
                        if (!string.IsNullOrWhiteSpace(firstLine))
                        {
                            string candidate = firstLine.Trim();
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }

            // 2) 本地 .tools\node\node.exe
            var local = Path.Combine(probesDir, ".tools", "node", "node.exe");
            if (File.Exists(local)) return local;

            return null;
        }

        /// <summary>
        /// 判定 Node + Playwright 依赖是否就绪（探针安装、依赖状态刷新、依赖安装后校验三处共用，抽此一处避免散落重复）。
        /// Ready = 解析到 Node 可执行文件 且 node_modules/playwright 目录存在；
        /// NodeExe = 解析到的 node 路径（可能为 null）；
        /// PlaywrightExists = node_modules/playwright 目录是否存在（用于区分“Node 缺失”还是“Playwright 缺失”的日志提示）。
        /// </summary>
        private static (bool Ready, string NodeExe, bool PlaywrightExists) IsNodeDepsReady(string probesDir)
        {
            var nodeExe = ResolveNodeExe(probesDir);
            var pwDir = Path.Combine(probesDir ?? "", "node_modules", "playwright");
            bool pw = Directory.Exists(pwDir);
            return (nodeExe != null && pw, nodeExe, pw);
        }

        // ===================== 子进程运行（实时流式输出） =====================

        /// <summary>
        /// 运行一个子进程，stdout 累积到 outStdout 并逐行写入日志；stderr 实时写入日志。
        /// 在后台线程调用即可（日志通过 logf 回到 UI 线程）。返回进程是否成功退出。
        /// </summary>
        private bool RunProbeProcess(string workingDir, string fileName, string arguments,
            Dictionary<string, string> extraEnv, Action<string> logf, out string outStdout)
        {
            outStdout = "";
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                if (extraEnv != null)
                    foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;

                using (var p = Process.Start(psi))
                {
                    if (p == null) { logf("[!] 无法启动: " + fileName); return false; }
                    var sb = new StringBuilder();
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) { sb.AppendLine(e.Data); logf(e.Data); }
                    };
                    p.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) logf(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    // 超时 15 分钟强制结束，避免 UI 永久挂起
                    if (!p.WaitForExit(900000))
                    {
                        try { p.Kill(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        outStdout = sb.ToString();
                        logf("[!] 进程超时（15 分钟）已被强制结束。");
                        return false;
                    }
                    outStdout = sb.ToString();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                logf("[!] 启动失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 以 -EncodedCommand 调用 PowerShell 脚本，并在脚本执行前强制 stdout 编码为 UTF-8。
        /// 解决中文 Windows（尤其是开启“Beta: 使用 UTF-8”）上 PowerShell 重定向输出乱码的问题。
        /// 同时过滤 PowerShell CLIXML 进度/信息流噪声，但保留 ErrorRecord 中的人类可读错误文本。
        /// </summary>
        private bool RunPowerShellScript(string workingDir, string scriptPath, Action<string> logf)
        {
            var psCmd = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $OutputEncoding=[System.Text.Encoding]::UTF8; & '" + scriptPath.Replace("'", "''") + "'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psCmd));
            bool inClixml = false;
            var clixmlLock = new object();
            Action<string> cleanLog = s =>
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var t = s.Trim();
                lock (clixmlLock)
                {
                    // PowerShell 信息/错误流默认以 CLIXML 序列化输出。stdout 的信息流（进度、HostInformationMessage）
                    // 对人类无用；stderr 的 ErrorRecord 包含有用错误信息，需要提取 Message/ToString 字段保留。
                    if (inClixml)
                    {
                        if (t.Contains("</Objs>")) inClixml = false;
                        var txt = ExtractClixmlHumanText(t);
                        if (!string.IsNullOrWhiteSpace(txt)) logf(txt);
                        return;
                    }
                    if (t.StartsWith("#<CLIXML>", StringComparison.Ordinal) || t.Contains("<Objs"))
                    {
                        if (!t.Contains("</Objs>")) inClixml = true;
                        var txt = ExtractClixmlHumanText(t);
                        if (!string.IsNullOrWhiteSpace(txt)) logf(txt);
                        return;
                    }
                }
                // 零星 XML 标签（进度 / 信息记录内部字段）也过滤。
                if (t.StartsWith("<Obj S=", StringComparison.Ordinal) ||
                    t.StartsWith("<TN", StringComparison.Ordinal) ||
                    t.StartsWith("<MS>", StringComparison.Ordinal) ||
                    t.StartsWith("<Props>", StringComparison.Ordinal) ||
                    t.StartsWith("<S N=", StringComparison.Ordinal) ||
                    t.StartsWith("<B N=", StringComparison.Ordinal) ||
                    t.StartsWith("<DT N=", StringComparison.Ordinal) ||
                    t.StartsWith("<U32", StringComparison.Ordinal) ||
                    t.StartsWith("<I64", StringComparison.Ordinal) ||
                    t.StartsWith("<LST>", StringComparison.Ordinal) ||
                    t.StartsWith("</", StringComparison.Ordinal))
                    return;
                logf(s);
            };
            return RunProbeProcess(workingDir, "powershell", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, null, cleanLog, out _);
        }

        /// <summary>
        /// 从 PowerShell CLIXML 片段中提取 ErrorRecord 的 Message / ToString 字段的人类可读文本。
        /// 进度/信息流的 CLIXML 不含这些字段，会被自然丢弃。
        /// </summary>
        private static string ExtractClixmlHumanText(string line)
        {
            // 匹配 <S N="Message">...</S> 或 <S N="ToString">...</S> 中的文本，做最基本的 XML 实体反转义。
            string Pick(string field)
            {
                string open = "<S N=\"" + field + "\">";
                int i = line.IndexOf(open, StringComparison.Ordinal);
                if (i < 0) return null;
                i += open.Length;
                int j = line.IndexOf("</S>", i, StringComparison.Ordinal);
                if (j < 0) return null;
                return line.Substring(i, j - i)
                    .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&apos;", "'");
            }
            var msg = Pick("Message");
            if (!string.IsNullOrWhiteSpace(msg)) return msg;
            var tos = Pick("ToString");
            if (!string.IsNullOrWhiteSpace(tos)) return tos;
            return null;
        }

        // ===================== 一键抓取（先装依赖再跑探针） =====================

        /// <summary>
        /// 执行一次完整抓取：优先使用进程内 WebView2 探针（直接调用本机 Edge，无需 Node/Chromium）。
        /// WebView2 失败时不再静默回退 Node，而是询问用户是否切换到 Node + Playwright 方案。
        /// </summary>
        private void RunProbeInternal(string input, bool skipDownloadCheck, Action<string> logf,
            out List<ProbeCandidateRow> rows, out string recommended, out bool searchLocated)
        {
            rows = new List<ProbeCandidateRow>();
            recommended = "";
            searchLocated = false;

            // 优先尝试 WebView2 进程内探针（替代 Node + Chromium 依赖）。
            // CreateAsync 会通过系统注册表定位 Runtime；若注册表损坏则 ProbeBrowserHost 内部已改为显式扫描磁盘目录兜底。
            bool webView2Succeeded = false;
            try
            {
                logf("[*] 尝试 WebView2 进程内探针（直接调用本机 Edge，无需下载依赖）…");
                // 安全网：探针初始化前确保 exe 目录存在 WebView2 托管依赖（单文件分发场景，
                // 用户从未点过“修复/安装”时也要能拉到）。失败仅记录，探针随后回退 Node 方案。
                WebView2ProbeDeps.EnsureWebView2ProbeDeps(logf, p => logf(WebView2ProbeDeps.ProgressLine(p)));
                using (var host = new ProbeBrowserHost())
                {
                    if (host.InitAsync(TimeSpan.FromSeconds(20), logf).GetAwaiter().GetResult())
                    {
                        var res = ProbeEngine.RunAsync(input, skipDownloadCheck, host, logf).GetAwaiter().GetResult();
                        if (res != null && res.Rows.Count > 0)
                        {
                            rows = res.Rows;
                            recommended = res.Recommended;
                            searchLocated = res.SearchLocated;
                            logf("[✓] WebView2 探针完成（候选 " + rows.Count + " 个）");
                            webView2Succeeded = true;
                            return;
                        }
                        logf("[*] WebView2 探针未产出可用结果。");
                    }
                    else
                    {
                        var detail = host.InitError;
                        logf("[!] WebView2 初始化失败（未安装 WebView2 Runtime 或不可用）"
                            + (string.IsNullOrEmpty(detail) ? "" : "：" + detail));
                    }
                }
            }
            catch (Exception ex)
            {
                logf("[!] WebView2 探针异常: " + ex.Message);
            }

            if (webView2Succeeded) return;

            // 不再自动回退 Node：由用户选择，避免在用户已删 Node 测试 WebView2 时强行跑安装脚本。
            bool switchToNode = false;
            Dispatcher.Invoke(new Action(() =>
            {
                var result = MessageBox.Show(this,
                    "WebView2 探针无法使用（详见日志），是否切换到 Node + Playwright 方案继续抓取？\n\n" +
                    "选「是」将安装/使用本地 Node 依赖；选「否」可在「维护工具 → 管理依赖 → 安装/升级/修复 WebView2 Runtime」中修复。",
                    "WebView2 不可用", MessageBoxButton.YesNo, MessageBoxImage.Question);
                switchToNode = result == MessageBoxResult.Yes;
            }));

            if (switchToNode)
            {
                logf("[*] 用户选择切换到 Node 探针…");
                RunProbeInternalNode(input, skipDownloadCheck, logf, out rows, out recommended, out searchLocated);
            }
            else
            {
                logf("[*] 已取消自动回退。可在「维护工具 → 管理依赖」中修复 WebView2 Runtime 或安装 Node 方案后再试。");
            }
        }

        /// <summary>
        /// 回退路径：若本地依赖（Node/Playwright）缺失则先跑 install_deps.ps1，
        /// 然后用本地 Node 运行 official_exe_finder.js --json，解析结果。
        /// </summary>
        private void RunProbeInternalNode(string input, bool skipDownloadCheck, Action<string> logf,
            out List<ProbeCandidateRow> rows, out string recommended, out bool searchLocated)
        {
            rows = new List<ProbeCandidateRow>();
            recommended = "";
            searchLocated = false;

            var probesDir = ResolveProbesDir();
            if (probesDir == null)
            {
                logf("[!] 未找到 probes 目录。请在程序所在目录（或逐级向上的某一级）放置 tools/probes 文件夹，");
                logf("    即确保存在 <程序目录>\\tools\\probes\\（内含 official_exe_finder.js 与 install_deps.ps1）。");
                return;
            }
            logf("[*] 探针目录: " + probesDir);
            var dep = IsNodeDepsReady(probesDir);
            string nodeExe = dep.NodeExe;
            bool depsReady = dep.Ready;

            if (!depsReady)
            {
                logf("[*] 未检测到本地依赖（Node 或 Playwright 缺失），先运行 install_deps.ps1 安装……");
                var installPs = Path.Combine(probesDir, "install_deps.ps1");
                if (!File.Exists(installPs))
                {
                    logf("[!] 找不到 install_deps.ps1，无法自动安装依赖。请手动在 probes 目录运行。");
                    return;
                }
                // install_deps.ps1 在失败时以 exit 1 退出（npm install / playwright install chromium 失败都会 throw）。
                // 必须校验脚本退出码 + 重新校验 Node 与 Playwright 目录，否则会出现“安装失败却报成功”的误判。
                bool installOk = RunPowerShellScript(probesDir, installPs, logf);
                // 安装后重新解析：脚本可能把 Node 下载到 .tools，或检测到系统 PATH 的 Node；
                // 同时重新校验 node_modules/playwright 是否真正落地（仅 Node 就绪而 Playwright 缺失仍不可用）。
                dep = IsNodeDepsReady(probesDir);
                nodeExe = dep.NodeExe;
                bool depsNowReady = installOk && dep.Ready;
                if (!depsNowReady)
                {
                    logf("[!] 依赖安装未完成（脚本退出码=" + (installOk ? "0" : "非零") +
                         "，Node=" + (dep.NodeExe != null ? "就绪" : "缺失") +
                         "，Playwright=" + (dep.PlaywrightExists ? "就绪" : "缺失") + "）。");
                    logf("    请检查网络后重试，或手动在 probes 目录运行 install_deps.ps1。");
                    return;
                }
                logf("[✓] 依赖安装完成（Node + Playwright 就绪）。");
            }
            else
            {
                logf("[✓] 本地依赖已就绪，跳过安装（使用 " + nodeExe + "）。");
            }

            // 把中文名/别名解析为入口 URL；同时保留原输入用于日志提示
            var resolvedInput = ResolveProbeEntry(input);
            if (resolvedInput == null)
            {
                logf("[!] 输入为空或过短，请填写软件官网 URL 或厂商名后重试。");
                return;
            }
            // 判断是否需要「全名搜索」：既非 http(s) URL，也未被别名表改写（即未命中内置别名）
            bool needsSearch = !resolvedInput.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !resolvedInput.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(resolvedInput, input.Trim(), StringComparison.OrdinalIgnoreCase);
            if (needsSearch)
                logf("未匹配内置别名，尝试通过搜索引擎定位「" + input.Trim() + "」的官网…");
            else if (resolvedInput != input)
                logf(">>> 探测: " + input + " -> " + resolvedInput);

            // 组装探针参数：脚本 + 输入 + --json（纯 JSON 输出更易解析）
            var script = Path.Combine(probesDir, "official_exe_finder.js");
            var argsb = new StringBuilder("\"").Append(script).Append("\" \"")
                .Append(resolvedInput.Replace("\"", "\\\""))   // 转义输入内的双引号
                .Append("\" --json");
            if (skipDownloadCheck) argsb.Append(" --no-download-check");

            var env = new Dictionary<string, string> { ["PLAYWRIGHT_BROWSERS_PATH"] = "0" };
            logf("[*] 启动探针: " + Path.GetFileName(nodeExe) + " official_exe_finder.js ……");
            bool ok = RunProbeProcess(probesDir, nodeExe, argsb.ToString(), env, logf, out string stdout);
            if (!ok)
            {
                logf("[!] 探针进程异常退出（详见上方日志）。");
                return;
            }

            // 解析 JSON（兼容 --json 纯 JSON 与 默认模式的 ===JSON=== 块）
            try
            {
                ParseProbeOutput(stdout, out rows, out recommended);
                searchLocated = _probeSearchLocated; // 是否由搜索引擎定位（非 URL 直抓/非别名映射）
                logf("[✓] 解析完成：共 " + rows.Count + " 个候选，推荐直链：" + (string.IsNullOrEmpty(recommended) ? "无" : recommended));
            }
            catch (Exception ex)
            {
                logf("[!] JSON 解析失败：" + ex.Message + "（原始输出已保留在日志中，可手动复制）");
                if (!string.IsNullOrWhiteSpace(stdout))
                    logf(stdout.Length > 4000 ? stdout.Substring(0, 4000) : stdout);
            }
        }

        // ===================== JSON 解析（收编至 Helpers/MiniJson.cs，零依赖） =====================

        /// <summary>
        /// 从探针 JSON 输出中解析候选行与推荐直链。
        /// 兼容两种形态：纯 JSON 数组，或含 "===JSON===" 标记的文本（取标记之后的部分）。
        /// </summary>
        private void ParseProbeOutput(string raw, out List<ProbeCandidateRow> rows, out string recommended)
        {
            rows = new List<ProbeCandidateRow>();
            recommended = "";

            string json = raw != null ? raw.Trim() : "";
            int marker = json.IndexOf("===JSON===", StringComparison.Ordinal);
            if (marker >= 0)
                json = json.Substring(marker + "===JSON===".Length).Trim();

            object parsed = MiniJson.Parse(json);

            // 顶层可能是数组；若单个对象则包成数组处理
            List<object> arr = parsed as List<object>;
            if (arr == null)
            {
                var single = parsed as Dictionary<string, object>;
                if (single == null) throw new Exception("JSON 顶层既不是数组也不是对象");
                arr = new List<object> { single };
            }

            bool foundSearch = false;
            foreach (var siteObj in arr)
            {
                var site = siteObj as Dictionary<string, object>;
                if (site == null) continue;

                string source = AsString(site, "entryUrl");
                // 记录来源标记（url/vendor/search），供 UI 判断是否需提示人工核对
                if (AsString(site, "source") == "search") foundSearch = true;

                // 推荐直链（每个入口一个）
                string recUrl = "";
                var recRaw = site.ContainsKey("recommended") ? site["recommended"] : null;
                if (recRaw is Dictionary<string, object> recDict)
                    recUrl = AsString(recDict, "url");

                // 候选数组
                var candRaw = site.ContainsKey("candidates") ? site["candidates"] : null;
                var candArr = candRaw as List<object>;
                if (candArr != null)
                {
                    foreach (var cObj in candArr)
                    {
                        var c = cObj as Dictionary<string, object>;
                        if (c == null) continue;
                        rows.Add(new ProbeCandidateRow
                        {
                            Source = source,
                            Url = AsString(c, "url"),
                            Strategy = AsString(c, "strategy"),
                            Arch = ArchOf(c),
                            Verified = AsBool(c, "verified"),
                            StatusText = AsString(c, "status"),
                            ContentType = AsString(c, "ct"),
                            IsRecommended = !string.IsNullOrEmpty(recUrl) && AsString(c, "url") == recUrl,
                            LowTrust = AsBool(c, "lowTrust"),
                        });
                    }
                }

                if (string.IsNullOrEmpty(recommended) && !string.IsNullOrEmpty(recUrl))
                    recommended = recUrl;
            }
            _probeSearchLocated = foundSearch;
        }

        // ---- JSON 取值辅助（兼容 status 为数字或字符串等混合类型）----
        private static string AsString(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return d[key].ToString();
        }

        private static bool AsBool(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return false;
            var v = d[key];
            if (v is bool b) return b;
            return v.ToString() == "True";
        }

        private static string ArchOf(Dictionary<string, object> c)
        {
            if (AsBool(c, "isX64")) return "x64";
            if (AsBool(c, "isArm64")) return "arm64";
            if (AsBool(c, "isX86")) return "x86";
            return "?";
        }

        // ===================== JSON 解析 =====================
        // 通用递归下降解析器已收编至 Helpers/MiniJson.cs（Parse 方法，签名不变）。
    }
}
