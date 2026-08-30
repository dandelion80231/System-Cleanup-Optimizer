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
            try { pid = p.Id; } catch { }   // 进程可能早已退出，取 Id 会抛

            try { p.Kill(); } catch { }              // 终止直接子进程（可能已退出，忽略异常）
            try { p.WaitForExit(5000); } catch { }   // 等其真正退出，之后读取 ExitCode 才不会抛

            if (pid <= 0) return;
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
            catch { }   // taskkill 失败（进程已退出/权限不足）不影响主流程
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

        /// <summary>执行 PowerShell 脚本，日志输出返回值。</summary>
        public static int RunPowerShell(string script, Action<string> log)
        {
            var (exitCode, stdout, stderr) = RunPS(script);
            // 修复：log 可能为 null（同文件 RunPowerShellGet 用的是 log?.Invoke），直接 log(...) 会 NRE 被吞成 -1
            if (!string.IsNullOrWhiteSpace(stdout)) log?.Invoke(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke("   [PS-ERR] " + stderr.Trim());
            return exitCode;
        }

        /// <summary>执行 PowerShell 脚本，返回 stdout（用于查询类，如统计大小）。非零退出码/ stderr 会通过 log 输出。</summary>
        public static string RunPowerShellGet(string script, Action<string> log)
        {
            var (exitCode, stdout, stderr) = RunPS(script);
            if (exitCode != 0)
            {
                log?.Invoke($"[PS-EXIT={exitCode}]");
                if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke($"[PS-ERR] {stderr.Trim()}");
            }
            return stdout ?? "";
        }

                public static (int exitCode, string stdout, string stderr) RunPowerShellGetFull(string script, Action<string> log)
        {
            var (exitCode, stdout, stderr) = RunPS(script);
            return (exitCode, stdout, stderr);
        }
        private static (int exitCode, string stdout, string stderr) RunPS(string script)
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
                    KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                    p.WaitForExit();   // 等待异步输出事件排空（Kill 后也会快速返回）
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
            // ① CLIXML 序列化错误
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

        /// <summary>执行命令行程序。capture=true 时把 stdout 输出到日志。</summary>
        public static int RunCmd(string[] args, Action<string> log, bool capture = false)
        {
            if (args == null || args.Length == 0) return -1;
            try
            {
                // .vbs 不是 PE 可执行文件：UseShellExecute=false 直接启动会报 ERROR_BAD_EXE_FORMAT（0xC1"不是有效 Win32 应用程序"）。
                // 必须显式用 64 位 cscript.exe 执行（//nologo //B 静默无窗）。
                if (args[0].EndsWith(".vbs", StringComparison.OrdinalIgnoreCase))
                    return RunVbs(args, log, capture);
                var cmdline = BuildArgs(args);
                var psi = new ProcessStartInfo(args[0], cmdline)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = capture,
                    RedirectStandardError = capture
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动: " + args[0]); return -1; }
                    if (capture)
                    {
                        var sbOut = new StringBuilder();
                        var sbErr = new StringBuilder();
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                        p.WaitForExit();   // 等待异步输出事件排空（Kill 后也会快速返回）
                        var outp = sbOut.ToString();
                        if (!string.IsNullOrWhiteSpace(outp)) log?.Invoke(outp.Trim());
                        var errp = sbErr.ToString();
                        if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [STDERR] " + errp.Trim());
                        return p.ExitCode;
                    }
                    KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 " + args[0] + " 失败: " + ex.Message); return -1; }
        }

        /// <summary>执行命令行程序，返回 stdout。
        /// 修正：原注释写「encoding=null 时用 .NET 默认（国内中文 Windows 是 GBK/CP936）」，与实现不符——
        /// 下面是先把 StandardOutputEncoding / StandardErrorEncoding 固定设为 UTF-8，只有 encoding 非 null
        /// 时才覆盖。即 encoding=null 的默认解码是 UTF-8（现代 UWP 应用如 winget、msix、PowerShell 7 输出 UTF-8）；
        /// 若目标程序确实按本地代码页输出（国内中文 Windows 是 GBK/CP936），需显式传入
        /// Encoding.GetEncoding("GBK") 覆盖该默认。</summary>
        public static string RunCmdGet(string[] args, Action<string> log, System.Text.Encoding encoding = null)
        {
            if (args == null || args.Length == 0) return "";
            try
            {
                // 同上：.vbs 走 cscript
                if (args[0].EndsWith(".vbs", StringComparison.OrdinalIgnoreCase))
                    return RunVbsGet(args, log);
                var cmdline = BuildArgs(args);
                var psi = new ProcessStartInfo(args[0], cmdline)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (encoding != null) psi.StandardOutputEncoding = encoding;
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动: " + args[0]); return ""; }
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                    p.WaitForExit();   // 等待异步输出事件排空（Kill 后也会快速返回）
                    // 修复：stderr 此前收集后从未使用，命令失败时完全没有诊断信息；仅在非空时输出
                    var errp = sbErr.ToString();
                    if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [stderr] " + errp.Trim());
                    return sbOut.ToString() ?? "";
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 " + args[0] + " 失败: " + ex.Message); return ""; }
        }

        // ================================================================
        //  VBS（cscript 显式执行，规避 ERROR_BAD_EXE_FORMAT）
        // ================================================================

        /// <summary>用 64 位 cscript 执行 .vbs 脚本（返回退出码）。</summary>
        private static int RunVbs(string[] args, Action<string> log, bool capture)
        {
            try
            {
                var psi = BuildVbsPsi(args, capture);
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动 cscript"); return -1; }
                    if (capture)
                    {
                        var sbOut = new StringBuilder();
                        var sbErr = new StringBuilder();
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                        p.WaitForExit();   // 等待异步输出事件排空（Kill 后也会快速返回）
                        var outp = sbOut.ToString();
                        if (!string.IsNullOrWhiteSpace(outp)) log?.Invoke(outp.Trim());
                        var errp = sbErr.ToString();
                        if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [STDERR] " + errp.Trim());
                        return p.ExitCode;
                    }
                    KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 执行 VBS " + args[0] + " 失败: " + ex.Message); return -1; }
        }

        /// <summary>用 64 位 cscript 执行 .vbs 脚本（返回 stdout）。</summary>
        private static string RunVbsGet(string[] args, Action<string> log)
        {
            try
            {
                var psi = BuildVbsPsi(args, redirect: true);
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log?.Invoke("  [!] 无法启动 cscript"); return ""; }
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    KillIfTimeout(p, PROCESS_TIMEOUT_MS);
                    p.WaitForExit();   // 等待异步输出事件排空（Kill 后也会快速返回）
                    // 修复：stderr 此前收集后从未使用，脚本报错（如 slmgr 无效密钥）完全没有诊断信息；仅在非空时输出
                    var errp = sbErr.ToString();
                    if (!string.IsNullOrWhiteSpace(errp)) log?.Invoke("   [stderr] " + errp.Trim());
                    return sbOut.ToString() ?? "";
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
                RedirectStandardError = redirect,
                // 修复：此前未设编码，cscript 输出按本地代码页解码，中文（如 slmgr 的许可证状态）必乱码；
                // 显式指定与同文件其它 ProcessStartInfo 一致的 UTF-8（未重定向时不设，避免无意义赋值）。
                StandardOutputEncoding = redirect ? Encoding.UTF8 : null,
                StandardErrorEncoding = redirect ? Encoding.UTF8 : null
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
