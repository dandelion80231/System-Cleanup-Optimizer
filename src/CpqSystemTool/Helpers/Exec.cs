using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CpqSystemTool
{
        /// <summary>
        /// 底层执行封装：统一管理子进程创建与输出捕获。
        /// 所有子进程均通过 ProcessStartInfo.CreateNoWindow = true 隐藏控制台窗口。
        /// </summary>
        internal static class Exec
        {
            // 子进程等待退出超时（毫秒）：15 分钟；超时强制 Kill，避免 UI 永久挂起
            private const int PROCESS_TIMEOUT_MS = 900000;

            // 注册旧代码页提供器，使 Encoding.GetEncoding("GBK")/936 可用（.NET 默认仅含 UTF-8/ASCII）
            static Exec()
            {
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
            }

            /// <summary>把流完整读入字节数组（用于绕过 Process 的编码解码，自行做 UTF-8/GBK 自适应）。</summary>
            private static byte[] ReadStreamBytes(Stream s)
            {
                if (s == null) return Array.Empty<byte>();
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }

            /// <summary>中文输出自适应解码：先按 UTF-8 解码（整段合法则用 UTF-8，兼容 PowerShell/winget 等现代程序）；
            /// 否则回退到 GBK/CP936（兼容 cscript/slmgr/ospp 等旧控制台程序在中文 Windows 上的输出）。</summary>
            private static string DecodeCjk(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0) return "";
                try
                {
                    // throwOnInvalidBytes=true：遇非法 UTF-8 序列直接抛 DecoderFallbackException，交回退分支
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    try { return Encoding.GetEncoding("GBK").GetString(bytes); }
                    catch
                    {
                        string s = Encoding.UTF8.GetString(bytes);
                        // 【P3-21】三级都解不出时，若 U+FFFD 替换字符占主导（>50%）说明源是二进制/损坏数据，
                        // 整段入日志只会刷屏噪声；给占位符便于定位是哪条命令的输出
                        int fffd = 0;
                        for (int i = 0; i < s.Length; i++) if (s[i] == '\uFFFD') fffd++;
                        if (fffd > 0 && fffd * 2 > s.Length) return "[二进制输出，解码失败]";
                        return s;
                    }
                }
            }

        /// <summary>等待子进程退出；超时则强制 Kill 整个进程树，并等其真正退出后再返回，避免 UI 永久挂起 / ExitCode 读取异常。</summary>
        private static void KillIfTimeout(System.Diagnostics.Process p, int timeoutMs)
        {
            if (!p.WaitForExit(timeoutMs)) KillTree(p);
        }

        /// <summary>强制结束进程及其整棵进程树（原各处 p.Kill() 的全部调用点均已改走这里）。修复两个缺陷：
        /// ① Kill() 是异步的，紧接读 ExitCode 会抛 InvalidOperationException（原被 catch 吞成 -1，UI 误报"操作失败"），
        ///    故 Kill 后必须 WaitForExit 等进程真正退出，调用方再读 ExitCode 才合法；
        /// ② net48 没有 Process.Kill(true)（.NET 5+ 的整树终止重载），Kill() 只终止 powershell.exe / cscript.exe 直接子进程，
        ///    其派生的 dism / winget / slmgr 等孙进程会全部残留，故补一次 taskkill /pid &lt;id&gt; /t /f 递归清理进程树。</summary>
        private static void KillTree(System.Diagnostics.Process p)
        {
            int pid = 0;
            try { pid = p.Id; } catch (Exception ex) { DebugLog.Ignore(ex); }   // 进程可能早已退出，取 Id 会抛

            try { p.Kill(); } catch (Exception ex) { DebugLog.Ignore(ex); }              // 终止直接子进程（可能已退出，忽略异常）
            try { p.WaitForExit(5000); } catch (Exception ex) { DebugLog.Ignore(ex); }   // 等其真正退出，之后读取 ExitCode 才不会抛

            if (pid <= 0) return;
            bool stillAlive;
            try { stillAlive = !p.HasExited; } catch (Exception ex) { DebugLog.Ignore(ex); stillAlive = false; }
            if (!stillAlive) return;
            // 【防 PID 复用误杀】仅当目标进程仍存活才 taskkill /t：此时 pid 必然仍属原进程（未退出就不会被系统重新分配）；
            // 反之若父进程已退出则跳过——/t 递归只能从「活着的父」挂接进程树，父已死则孙进程本就够不到，
            // 而 taskkill 去敲一个可能已被复用给无关新进程的 pid 才是危险源（系统清理工具误杀后果重）。
            try
            {
                // taskkill 位于 System32；/t 递归终止子进程树，/f 强制
                var psi = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                    "/pid " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /t /f")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var tk = Process.Start(psi))
                {
                    if (tk != null) tk.WaitForExit(5000);   // 等清理完成，避免孙进程尚未结束就返回
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }   // taskkill 失败（进程已退出/权限不足）不影响主流程
        }

        public static string ExpandEnv(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            return Environment.ExpandEnvironmentVariables(p);
        }

        /// <summary>转义 PowerShell 单引号字符串里的单引号（' → ''）。供 QuotePS 与命令内嵌字符串共用，避免各自实现。</summary>
        public static string EscapeSingleQuote(string s) => (s ?? "").Replace("'", "''");

        /// <summary>把路径包进 PowerShell 单引号并转义内部单引号。</summary>
        public static string QuotePS(string p)
        {
            return "'" + EscapeSingleQuote(p) + "'";
        }

        // ================================================================
        //  PowerShell
        // ================================================================

        /// <summary>执行 PowerShell 脚本，日志输出返回值。timeoutMs 可选，默认 15 分钟（快命令够用；长任务传大值）。</summary>
        public static int RunPowerShell(string script, Action<string> log, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            var (exitCode, stdout, stderr) = RunPS(script, timeoutMs);
            // 修复：log 可能为 null（同文件 RunPowerShellGet 用的是 log?.Invoke），直接 log(...) 会 NRE 被吞成 -1
            if (!string.IsNullOrWhiteSpace(stdout)) log?.Invoke(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke("   [PS-ERR] " + stderr.Trim());
            return exitCode;
        }

        /// <summary>执行 PowerShell 脚本，返回 stdout（用于查询类，如统计大小）。非零退出码/ stderr 会通过 log 输出。timeoutMs 可选，默认 15 分钟。</summary>
        public static string RunPowerShellGet(string script, Action<string> log, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            var (exitCode, stdout, stderr) = RunPS(script, timeoutMs);
            if (exitCode != 0)
            {
                log?.Invoke($"[PS-EXIT={exitCode}]");
                if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke($"[PS-ERR] {stderr.Trim()}");
            }
            return stdout ?? "";
        }

                public static (int exitCode, string stdout, string stderr) RunPowerShellGetFull(string script, Action<string> log, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            var (exitCode, stdout, stderr) = RunPS(script, timeoutMs);
            return (exitCode, stdout, stderr);
        }
        private static (int exitCode, string stdout, string stderr) RunPS(string script, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            try
            {
                // 使用 powershell.exe 完整路径，避免 PATH 被修改或 WOW64 重定向导致找不到/找错解释器
                var psPath = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                // 用 -EncodedCommand（Base64 UTF-16LE）传递脚本，彻底规避命令行引号转义问题：
                // 之前用 -Command "script"（script 内 Replace("\"","\"\"") 翻倍）在含双引号的脚本
                // （如防火墙的 "$(...)" 输出模板）下会破坏引号配对，powershell 报
                // ParserError: 字符串缺少终止符 / TerminatorExpectedAtEndOfString，导致读取失败、UI 误报"无管理员权限"。
                // -EncodedCommand 数据本身不含空格/引号，无需任何外层引号转义，对任意脚本都安全。
                // 同时在脚本前设置 UTF-8 输出编码，避免中文（规则名等）在重定向管道下按本地码页乱码（本机已开启 Beta UTF-8）。
                var full = "$ProgressPreference='SilentlyContinue'; $OutputEncoding=[System.Text.Encoding]::UTF8; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + script;
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
                var psi = new ProcessStartInfo(psPath,
                    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return (-1, "", "无法启动 powershell");
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    KillIfTimeout(p, timeoutMs);
                    p.WaitForExit(15000);   // 【P2 复审】加 15s 栅栏：极端内核挂起时 KillTree/taskkill 都救不出的进程会卡死无限 WaitForExit；有界等待后继续走输出排空（被杀进程管道 EOF 已关闭，WaitAll 不挂起）
                    // 清洗 PowerShell 在非交互重定向下把错误序列化成 CLIXML 的噪声（#< CLIXML ... </Objs>），
                    // 否则日志框会被一坨 XML 刷屏（如 Edge 缓存清理时文件被占用）。
                    return (p.ExitCode, SanitizeClixml(sbOut.ToString()), SanitizeClixml(sbErr.ToString()));
                }
            }
            catch (Exception ex) { return (-1, "", "powershell 执行失败: " + ex.Message); }
        }

        /// <summary>清洗 PowerShell 重定向输出，统一两种错误格式为人话：
        /// ① CLIXML 序列化错误（#&lt; CLIXML ... &lt;S S="Error"&gt;文本&lt;/S&gt; ... &lt;/Objs&gt;）—— 拆出可读文本并还原 _xHHHH_ 转义；
        /// ② 裸 PowerShell 错误记录（非交互重定向下直接写 stderr 的文本，无 CLIXML 包裹）：
        ///    Remove-Item : 无法删除项"...journal.baj"，因为该项正被另一进程使用。
        ///    所在位置 行:1 字符:1
        ///    + Remove-Item ...
        ///    + ~~~~~~~
        ///        + CategoryInfo          : WriteError: (路径) [Remove-Item], IOException
        ///        + FullyQualifiedErrorId : RemoveItemIOError,...
        ///    仅保留每条错误首行「命令: 消息」人话，丢弃 所在位置 / + 调用栈 / CategoryInfo / FullyQualifiedErrorId 样板噪声。
        /// 普通 stdout / 不含上述结构的文本原样返回，不影响数值解析。</summary>
        private static string SanitizeClixml(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            string t = s.Trim();
            // ① CLIXML 序列化错误。
            // 【P3-20】<S S= 宽松匹配是有意的：CLIXML 错误块形态为 <S S="Error">…</S>，完整标签匹配
            // 会漏掉截断/带属性的变体（SendAsync 输出中途被杀等）；误伤路径（普通输出恰好含该子串）
            // 由 SanitizeClixmlStructured 内部兑底：未匹配到 <S S="(?:Error|Warning)"> 时退化为仅剥壳原文本，
            // 不会丢失信息。保持宽松优先召回，不收紧。
            if (t.StartsWith("#< CLIXML", StringComparison.Ordinal) || t.Contains("<Objs") || t.Contains("<S S="))
                return SanitizeClixmlStructured(t);
            // ② 裸 PowerShell 错误记录：仅在识别到错误样板时才清洗，避免误伤普通输出
            if (LooksLikePlainPsError(t))
                return SanitizePlainPsError(t);
            // 普通文本 / stdout：原样返回
            return s;
        }

        /// <summary>清洗 CLIXML 序列化错误：抽 &lt;S S="Error"/"Warning"&gt; 文本、还原 _xHHHH_ 转义、剥 XML 壳。</summary>
        private static string SanitizeClixmlStructured(string s)
        {
            string t = s.Trim();
            // 去掉开头的 #< CLIXML 指令行
            if (t.StartsWith("#< CLIXML", StringComparison.Ordinal))
            {
                int nl = t.IndexOf('\n');
                if (nl >= 0) t = t.Substring(nl + 1);
            }
            // 抽取 Error/Warning 文本片段
            var sb = new System.Text.StringBuilder();
            var re = new System.Text.RegularExpressions.Regex("<S S=\"(?:Error|Warning)\">(.*?)</S>", System.Text.RegularExpressions.RegexOptions.Singleline);
            foreach (System.Text.RegularExpressions.Match m in re.Matches(t))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(DecodePsEscapes(m.Groups[1].Value));
            }
            // 没匹配到则退化为原文本；最后统一剥掉残留 XML 壳
            string result = sb.Length > 0 ? sb.ToString() : t;
            result = System.Text.RegularExpressions.Regex.Replace(result, "<Objs[^>]*>|</Objs>|<S S=\"[^\"]*\">|</S>", "");
            return result.Trim();
        }

        /// <summary>判断文本是否像「裸 PowerShell 错误记录」（非 CLIXML）：含错误样板标记即为；
        /// 用样板（中文「所在位置」/英文「At line」/CategoryInfo/FullyQualifiedErrorId）判定，避免误伤普通 stdout。</summary>
        private static bool LooksLikePlainPsError(string t)
        {
            return t.IndexOf("所在位置", StringComparison.Ordinal) >= 0
                || t.IndexOf("At line", StringComparison.Ordinal) >= 0
                || t.IndexOf("CategoryInfo", StringComparison.Ordinal) >= 0
                || t.IndexOf("FullyQualifiedErrorId", StringComparison.Ordinal) >= 0;
        }

        /// <summary>清洗裸 PowerShell 错误记录：保留每条错误首行「命令: 消息」，丢弃样板噪声行（所在位置 / + 调用栈 / CategoryInfo / FullyQualifiedErrorId）。</summary>
        private static string SanitizePlainPsError(string s)
        {
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // 样板噪声：所在位置 / + 调用栈 / CategoryInfo / FullyQualifiedErrorId
                if (line.StartsWith("所在位置", StringComparison.Ordinal)
                    || line.StartsWith("+ ", StringComparison.Ordinal)
                    || line.IndexOf("CategoryInfo", StringComparison.Ordinal) >= 0
                    || line.IndexOf("FullyQualifiedErrorId", StringComparison.Ordinal) >= 0)
                    continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.Length > 0 ? sb.ToString().Trim() : s.Trim();
        }

        /// <summary>还原 PowerShell 的 _xHHHH_ 转义（控制字符/特殊字符，如 _x000D__x000A_ = \r\n）。</summary>
        private static string DecodePsEscapes(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s, "_x([0-9A-Fa-f]{4})_", m =>
            {
                try { return ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString(); }
                catch { return m.Value; }
            });
        }

        // ================================================================
        //  CMD / 通用子进程
        // ================================================================

        /// <summary>执行命令行程序。capture=true 时把 stdout 输出到日志；workingDirectory 非空时设子进程 CWD；timeoutMs 可选，默认 15 分钟（快命令够用；长任务传大值）。</summary>
        public static int RunCmd(string[] args, Action<string> log, bool capture = false, string workingDirectory = null, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            if (args == null || args.Length == 0) return -1;
            try
            {
                // .vbs 不是 PE 可执行文件：UseShellExecute=false 直接启动会报 ERROR_BAD_EXE_FORMAT（0xC1"不是有效 Win32 应用程序"）。
                // 必须显式用 64 位 cscript.exe 执行（//nologo //B 静默无窗）。
                if (args[0].EndsWith(".vbs", StringComparison.OrdinalIgnoreCase))
                    return RunVbs(args, log, capture, timeoutMs);
                var cmdline = BuildArgs(args);
                var psi = new ProcessStartInfo(args[0], cmdline)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = capture,
                    RedirectStandardError = capture
                };
                // 关键：固定子进程 CWD。不设时子进程继承父进程 CWD（双击 exe 启动=桌面/所在目录），
                // ODT 等引擎会据此在 CWD 下建日志/数据文件夹，导致桌面冒出无名文件夹。
                if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动: " + args[0]); return -1; }
                    if (capture)
                    {
                        // 修复乱码：不再依赖 Process 的 OutputDataReceived（其按 StandardOutputEncoding 解码，
                        // cscript/slmgr/ospp 实际输出 GBK/CP936，被按 UTF-8 解必乱码）。改为直接读原始字节流，
                        // 后台排空防大输出阻塞，再用 DecodeCjk 自适应解码（UTF-8 优先、失败回退 GBK）。
                        var outTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardOutput.BaseStream));
                        var errTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardError.BaseStream));
                        KillIfTimeout(p, timeoutMs);
                        p.WaitForExit(15000);   // 【P2 复审】加 15s 栅栏防 KillTree/taskkill 均无效的极端内核挂起场景导致永久阻塞
                        System.Threading.Tasks.Task.WaitAll(outTask, errTask);
                        var outp = DecodeCjk(outTask.Result);
                        if (!string.IsNullOrWhiteSpace(outp)) log?.Invoke(outp.Trim());
                        var errp = DecodeCjk(errTask.Result);
                        if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [STDERR] " + errp.Trim());
                        return p.ExitCode;
                    }
                    KillIfTimeout(p, timeoutMs);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 " + args[0] + " 失败: " + ex.Message); return -1; }
        }

        /// <summary>执行命令行程序，返回 stdout。
        /// 【P3-19】encoding 参数为死参数（仅保留签名兼容，传值不生效）：输出统一走自适应解码
        /// （原始字节 + DecodeCjk：UTF-8 严格解析优先、失败回退 GBK、再失败保留原文），
        /// 覆盖 cscript(GBK) / winget(UWP UTF-8) 等全部已知调用场景；调用方无需也不应再传 encoding。</summary>
        public static string RunCmdGet(string[] args, Action<string> log, System.Text.Encoding encoding = null, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            if (args == null || args.Length == 0) return "";
            try
            {
                // 同上：.vbs 走 cscript
                if (args[0].EndsWith(".vbs", StringComparison.OrdinalIgnoreCase))
                    return RunVbsGet(args, log, timeoutMs);
                var cmdline = BuildArgs(args);
                var psi = new ProcessStartInfo(args[0], cmdline)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                // 不再在此设定 StandardOutputEncoding/StandardErrorEncoding：RunCmdGet 直接读原始字节 +
                // DecodeCjk 自适应解码（UTF-8 优先、失败回退 GBK），覆盖 cscript 等 GBK 输出程序。
                // 【P3-19】encoding 参数为签名兼容保留的死参数（见方法 doc），显式 _ = 抑制 unused 警告。
                _ = encoding;
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动: " + args[0]); return ""; }
                    // 修复乱码：同上，直接读原始字节 + DecodeCjk 自适应解码（UTF-8 优先、失败回退 GBK）
                    var outTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardOutput.BaseStream));
                    var errTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardError.BaseStream));
                    KillIfTimeout(p, timeoutMs);
                    p.WaitForExit(15000);   // 【P2 复审】加 15s 栅栏防 KillTree/taskkill 均无效的极端内核挂起场景导致永久阻塞
                    System.Threading.Tasks.Task.WaitAll(outTask, errTask);
                    // 修复：stderr 此前收集后从未使用，命令失败时完全没有诊断信息；仅在非空时输出
                    var errp = DecodeCjk(errTask.Result);
                    if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [stderr] " + errp.Trim());
                    return DecodeCjk(outTask.Result) ?? "";
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 " + args[0] + " 失败: " + ex.Message); return ""; }
        }

        // ================================================================
        //  VBS（cscript 显式执行，规避 ERROR_BAD_EXE_FORMAT）
        // ================================================================

        /// <summary>用 64 位 cscript 执行 .vbs 脚本（返回退出码）。timeoutMs 可选，默认 15 分钟。</summary>
        private static int RunVbs(string[] args, Action<string> log, bool capture, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            try
            {
                var psi = BuildVbsPsi(args, capture);
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动 cscript"); return -1; }
                    if (capture)
                    {
                        // 修复乱码（slmgr.vbs/ospp.vbs 经 cscript 在中文 Windows 输出 GBK/CP936，
                        // Process 按 UTF-8 解必乱码）：直接读原始字节，后台排空后用 DecodeCjk 自适应解码。
                        var outTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardOutput.BaseStream));
                        var errTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardError.BaseStream));
                        KillIfTimeout(p, timeoutMs);
                        p.WaitForExit(15000);   // 【P2 复审】加 15s 栅栏防 KillTree/taskkill 均无效的极端内核挂起场景导致永久阻塞
                        System.Threading.Tasks.Task.WaitAll(outTask, errTask);
                        var outp = DecodeCjk(outTask.Result);
                        if (!string.IsNullOrWhiteSpace(outp)) log?.Invoke(outp.Trim());
                        var errp = DecodeCjk(errTask.Result);
                        if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [STDERR] " + errp.Trim());
                        return p.ExitCode;
                    }
                    KillIfTimeout(p, timeoutMs);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 VBS " + args[0] + " 失败: " + ex.Message); return -1; }
        }

        /// <summary>用 64 位 cscript 执行 .vbs 脚本（返回 stdout）。timeoutMs 可选，默认 15 分钟。</summary>
        private static string RunVbsGet(string[] args, Action<string> log, int timeoutMs = PROCESS_TIMEOUT_MS)
        {
            try
            {
                var psi = BuildVbsPsi(args, redirect: true);
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动 cscript"); return ""; }
                    // 修复乱码（cscript 输出 GBK）：直接读原始字节 + DecodeCjk 自适应解码
                    var outTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardOutput.BaseStream));
                    var errTask = System.Threading.Tasks.Task.Run(() => ReadStreamBytes(p.StandardError.BaseStream));
                    KillIfTimeout(p, timeoutMs);
                    p.WaitForExit(15000);   // 【P2 复审】加 15s 栅栏防 KillTree/taskkill 均无效的极端内核挂起场景导致永久阻塞
                    System.Threading.Tasks.Task.WaitAll(outTask, errTask);
                    // 修复：stderr 此前收集后从未使用，脚本报错（如 slmgr 无效密钥）完全没有诊断信息；仅在非空时输出
                    var errp = DecodeCjk(errTask.Result);
                    if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [stderr] " + errp.Trim());
                    return DecodeCjk(outTask.Result) ?? "";
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 VBS " + args[0] + " 失败: " + ex.Message); return ""; }
        }

        /// <summary>构建 cscript 进程参数：cscript //nologo //B "脚本路径" [参数...]。</summary>
        private static ProcessStartInfo BuildVbsPsi(string[] args, bool redirect)
        {
            string vbsPath = args[0];
            if (!Path.IsPathRooted(vbsPath))
                vbsPath = Path.Combine(Environment.SystemDirectory, vbsPath);  // slmgr.vbs 位于 System32（64 位进程不重定向）
            var cmd = new StringBuilder("//nologo //B ").Append(QuoteCmd(vbsPath));
            for (int i = 1; i < args.Length; i++) cmd.Append(' ').Append(QuoteCmd(args[i]));
            return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cscript.exe"), cmd.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect
                // 注：不再在此设定 StandardOutputEncoding/StandardErrorEncoding。RunVbs/RunVbsGet 直接读
                // p.StandardOutput.BaseStream 原始字节，再用 DecodeCjk 自适应解码（UTF-8 优先、失败回退 GBK），
                // 因为 cscript/slmgr/ospp 在中文 Windows 实际输出 GBK/CP936，设 UTF-8 反而必乱码。
            };
        }

        /// <summary>构建命令行参数字符串（跳过 args[0] 程序名）。</summary>
        private static string BuildArgs(string[] args)
        {
            var sb = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                sb.Append(" ");
                sb.Append(QuoteCmd(args[i]));
            }
            return sb.ToString();
        }

        private static string QuoteCmd(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Windows 命令行：参数内若含空格/引号/CMD 特殊字符需用双引号包裹；
            // 引号内表示一个字面双引号须写成两个双引号（""），而非 \"（后者是 *nix 转义，Windows 不识别）。
            bool needsQuote = s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('&') >= 0
                || s.IndexOf('^') >= 0 || s.IndexOf('|') >= 0 || s.IndexOf('<') >= 0
                || s.IndexOf('>') >= 0 || s.IndexOf('%') >= 0;
            if (!needsQuote) return s;
            string body = s.Replace("\"", "\"\"");
            // 修复：路径以反斜杠结尾（如 "C:\my dir\"）时，末尾 \" 会被解析成转义的字面双引号，
            // 闭合引号丢失、后续参数被吞进同一个字符串。按 Windows CRT 规则：结尾 n 个反斜杠需写成 2n+1 个，
            // 其中 2n 个还原为 n 个字面反斜杠，第 2n+1 个与紧跟其后的闭合引号组成转义对，使引号仍起闭合作用。
            int trailing = 0;
            for (int i = body.Length - 1; i >= 0 && body[i] == '\\'; i--) trailing++;
            if (trailing > 0)
                body = body.Substring(0, body.Length - trailing) + new string('\\', trailing * 2 + 1);
            return "\"" + body + "\"";
        }
    }
}
