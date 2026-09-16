// Office 安装位置重定向（junction）
// 原理：Microsoft ODT / Click-to-Run 没有"自定义安装路径"参数，Office 永远写死到
// %ProgramFiles%\Microsoft Office（64位）或 %ProgramFiles(x86)%\Microsoft Office（32位）。
// 本模块通过目录 junction（mklink /J）在写死路径挂一个指向用户目标盘的 junction，
// ODT 照常往写死路径写，数据实际落到目标盘。创建 Program Files 下的 junction 需要管理员权限。
// 风格对齐 Modules/OfficeInstall.cs、Modules/OfficeDeploy.cs（均带 Action<string> log 参数）。
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace CpqSystemTool
{
    /// <summary>
    /// Office 安装位置重定向（junction）：提供固定盘符枚举、junction 创建/移除/查询、
    /// UAC 提权运行（本程序非管理员时）、目标盘可写性校验。
    /// </summary>
    public static class OfficeInstallLocation
    {
        // ================================================================
        //  1. 获取可用的固定安装盘符
        // ================================================================

        /// <summary>
        /// 返回固定本地磁盘（DriveType.Fixed）的盘符数组（如 "D:\\"），用于 UI 下拉。
        /// 排除系统盘（ODT 默认就装系统盘，无需再"装到系统盘"）。按盘符排序。
        /// 异常时返回空数组（不抛出）。
        /// </summary>
        public static string[] GetFixedDiskDrives()
        {
            try
            {
                // 系统盘：Environment.SystemDirectory 形如 C:\Windows，取根目录盘符（如 "C:"）
                string sysDrive = null;
                try
                {
                    string sysDir = Environment.SystemDirectory;
                    string root = Path.GetPathRoot(sysDir); // "C:\"
                    if (!string.IsNullOrEmpty(root))
                        sysDrive = root.TrimEnd('\\').ToUpperInvariant(); // "C:"
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }

                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed)
                    .Where(d => d.IsReady) // 未就绪（无盘/未插U盘被识别为Fixed极少见，保险起见排除）
                    .Where(d =>
                    {
                        string drive = d.Name.TrimEnd('\\').ToUpperInvariant();
                        return drive != sysDrive;
                    })
                    .Select(d => d.Name.TrimEnd('\\')) // "D:\" 保留盘符+冒号，去掉末尾反斜杠
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return drives ?? new string[0];
            }
            catch (Exception ex)
            {
                // 异常（极少见，如所有驱动信息查询失败）不抛出，返回空数组
                DebugLog.Ignore(ex);
                return new string[0];
            }
        }

        // ================================================================
        //  2. junction 操作
        // ================================================================

        /// <summary>
        /// 返回指定架构 Office 写死安装根路径：
        /// 64 → %ProgramFiles%\Microsoft Office；32 → %ProgramFiles(x86)%\Microsoft Office。
        /// 用环境变量展开，不硬编码盘符。
        /// </summary>
        public static string GetOfficeRootPath(string arch)
        {
            string envVar = arch == "32" ? "ProgramFiles(x86)" : "ProgramFiles";
            string baseDir = Environment.GetEnvironmentVariable(envVar) ?? "C:\\Program Files";
            return Path.Combine(baseDir, "Microsoft Office");
        }

        /// <summary>判断 path 当前是否是一个 directory junction/symlink（reparse point）。
        /// 注意：先判断 Directory.Exists 再查属性，避免对不存在的路径查属性抛异常。</summary>
        public static bool IsJunction(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return false;
                var info = new FileInfo(path);
                return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return false; }
        }

        /// <summary>返回 junction 实际指向的目标路径；非 junction 或无法解析时返回 null。</summary>
        public static string GetJunctionTarget(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !IsJunction(path))
                    return null;
                // 用 fsutil 解析 reparse point 目标（最可靠，不依赖 P/Invoke）
                string outp = Exec.RunCmdGet(
                    new[] { "cmd.exe", "/c", "fsutil", "reparsepoint", "query", path },
                    null);
                // fsutil 输出形如：
                //   类型为 40018：Junction
                //   路径为: D:\Office
                //   替代数据: ...
                // 解析"路径为:"（或英文"Path:"）后那行
                string target = ParseFsutilTarget(outp);
                return target;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return null; }
        }

        /// <summary>解析 fsutil reparsepoint query 输出中的目标路径行。</summary>
        private static string ParseFsutilTarget(string fsutilOut)
        {
            if (string.IsNullOrWhiteSpace(fsutilOut)) return null;
            string[] lines = fsutilOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                // 中文："路径为: D:\Office"  或  "路径为 D:\Office"
                // 英文："Path: D:\Office"     或  "Path D:\Office"
                string trimmed = line.Trim();
                string cnKey = "路径为";
                string enKey = "Path";
                int idx = trimmed.IndexOf(cnKey, StringComparison.Ordinal);
                string target = null;
                if (idx >= 0)
                    target = trimmed.Substring(idx + cnKey.Length).Trim().TrimStart(':').Trim();
                else
                {
                    idx = trimmed.IndexOf(enKey, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        target = trimmed.Substring(idx + enKey.Length).Trim().TrimStart(':').Trim();
                }
                if (!string.IsNullOrEmpty(target) && target.Length > 1 && target[1] == ':')
                    return target; // 确认是合法盘符路径
            }
            return null;
        }

        /// <summary>
        /// 安装前调用：确保 officeRoot 是指向 targetDir 的 junction。
        /// 三种分支：
        ///   ① officeRoot 不存在 → 创建 targetDir + mklink /J，成功返回 true
        ///   ② officeRoot 已存在且是 junction → 校验目标是否等于 targetDir（忽略末尾\和大小写），是则 true；不是则 log 警告并返回 false（不静默改）
        ///   ③ officeRoot 已存在且是真实目录（非 junction）→ log 提示"已装 Office，本路径为真实目录；本功能仅支持全新安装到其他盘"，返回 false
        /// </summary>
        public static bool EnsureJunction(string officeRoot, string targetDir, Action<string> log)
        {
            log?.Invoke("检查 junction 状态: " + officeRoot);
            try
            {
                if (string.IsNullOrEmpty(officeRoot) || string.IsNullOrEmpty(targetDir))
                { log?.Invoke("  [!] 参数为空，无法创建 junction"); return false; }

                // 先确认目标目录所在盘可写
                if (!IsWritableDisk(targetDir, log))
                {
                    log?.Invoke("  [!] 目标盘/目录不可写: " + targetDir);
                    return false;
                }

                // 分支①：officeRoot 不存在 → 创建
                if (!Directory.Exists(officeRoot))
                {
                    log?.Invoke("  [*] officeRoot 不存在，创建目标目录并挂 junction: " + targetDir);
                    Directory.CreateDirectory(targetDir); // 若已存在则无副作用
                    int rc;
                    if (MemoryAnalyzer.IsAdministrator())
                    {
                        rc = Exec.RunCmd(new[] { "cmd", "/c", "mklink", "/J", officeRoot, targetDir }, log, capture: true);
                    }
                    else
                    {
                        // 非管理员：创建 Program Files 下的 junction 需管理员权限，
                        // 在 mklink 前先弹 UAC 单独提权（UAC 成功即 junction 已挂好），提权子进程跑完即退出。
                        // 成功后 ODT 本身走 RunConfig 非提权——ODT 写的是 junction 内的目录（目标盘用户目录），
                        // 普通用户权限即可，无需 UAC，故此处 UAC 弹窗只弹一次。
                        log?.Invoke("  [提权] 创建 junction 需管理员权限，弹出 UAC 单独提权...");
                        rc = RunElevated(new[] { "cmd.exe", "/c", "mklink", "/J", officeRoot, targetDir }, null, log);
                    }
                    if (rc == 0 && IsJunction(officeRoot))
                    {
                        log?.Invoke("  [OK] junction 创建成功: " + officeRoot + " → " + targetDir);
                        return true;
                    }
                    else
                    {
                        log?.Invoke("  [!] junction 创建失败（mklink 退出码 " + rc + "）");
                        return false;
                    }
                }

                // officeRoot 已存在：判断是 junction 还是真实目录
                if (IsJunction(officeRoot))
                {
                    // 分支②：已存在 junction → 校验目标
                    string currentTarget = GetJunctionTarget(officeRoot);
                    string norm1 = NormPath(currentTarget);
                    string norm2 = NormPath(targetDir);
                    log?.Invoke("  [*] officeRoot 已是 junction，当前目标: " + (currentTarget ?? "(无法解析)"));
                    if (norm1 == norm2)
                    {
                        log?.Invoke("  [OK] junction 已指向正确目标: " + targetDir);
                        return true;
                    }
                    log?.Invoke("  [!] junction 目标与期望不一致（当前: " + currentTarget + "，期望: " + targetDir + "），不静默修改，请先移除旧 junction");
                    return false;
                }
                else
                {
                    // 分支③：真实目录（本机已装 Office）
                    log?.Invoke("  [!] 本机已装 Office，本路径为真实目录: " + officeRoot);
                    log?.Invoke("      本功能仅支持全新安装到其他盘；已装 Office 的迁移将在下一批提供");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("  [!] EnsureJunction 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>路径归一化：去末尾反斜杠、转大写（用于大小写不敏感比较）。</summary>
        private static string NormPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return string.Empty;
            return p.TrimEnd('\\', '/').ToUpperInvariant();
        }

        /// <summary>
        /// 移除 junction 壳（`rd /q` 只对 junction 有效且安全，不会穿过 junction 删目标数据）。
        /// 调用方应先确认 IsJunction(officeRoot) 为 true 再调用。
        /// </summary>
        public static bool RemoveJunctionShell(string officeRoot, Action<string> log)
        {
            try
            {
                if (string.IsNullOrEmpty(officeRoot))
                { log?.Invoke("  [!] officeRoot 为空，无法移除 junction"); return false; }
                log?.Invoke("移除 junction 壳: " + officeRoot);
                int rc = Exec.RunCmd(new[] { "cmd", "/c", "rd", "/q", officeRoot }, log, capture: true);
                if (rc == 0 && !Directory.Exists(officeRoot))
                { log?.Invoke("  [OK] junction 壳已移除（目标数据未受影响）"); return true; }
                log?.Invoke("  [!] 移除失败（rd 退出码 " + rc + "）");
                return false;
            }
            catch (Exception ex)
            { log?.Invoke("  [!] 移除 junction 失败: " + ex.Message); return false; }
        }

        /// <summary>
        /// 便捷判断：当前 arch 对应的 OfficeRoot 是否为本功能创建的、且目标仍存在的 junction。
        /// 用于卸载后清理残留壳时识别"该壳是本功能挂的，可以安全移除"。
        /// </summary>
        public static bool IsJunctionActive(string arch)
        {
            try
            {
                string officeRoot = GetOfficeRootPath(arch);
                if (!Directory.Exists(officeRoot)) return false;
                if (!IsJunction(officeRoot)) return false;
                string target = GetJunctionTarget(officeRoot);
                // 目标存在且非 officeRoot 自身（排除自指）
                return !string.IsNullOrEmpty(target)
                    && Directory.Exists(target)
                    && NormPath(target) != NormPath(officeRoot);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return false; }
        }

        // ================================================================
        //  3. 提权运行（UAC 双保险）
        // ================================================================

        /// <summary>
        /// 以管理员提权启动子进程并等待退出、返回退出码。
        /// 用途：当本程序非管理员运行时，"创建 junction"和"跑 ODT 装到目标盘"需要 UAC。
        /// 注意：Verb="runas" 需要 UseShellExecute=true（与项目 Exec 默认的 false 不同），
        /// 所以此处自己写 ProcessStartInfo，不复用 Exec.RunCmd。
        /// 参数含空格时加引号；setup.exe 路径含空格务必加引号。
        /// 用户拒绝 UAC（OperationCanceledException/Win32Exception）时 catch 并 log"用户取消了提权"，返回 -1。
        /// </summary>
        public static int RunElevated(string[] args, string workingDirectory, Action<string> log)
        {
            if (args == null || args.Length == 0)
            { log?.Invoke("  [!] RunElevated 参数为空"); return -1; }

            try
            {
                string exePath = args[0];
                log?.Invoke("以管理员提权运行: " + exePath + " " + JoinQuoted(args, 1));

                // 拼接参数（跳过 args[0] 程序名），对含空格/特殊字符的参数加引号
                var sb = new System.Text.StringBuilder();
                for (int i = 1; i < args.Length; i++)
                    sb.Append(' ').Append(QuoteForCmdLine(args[i]));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = sb.ToString(),
                    UseShellExecute = true,   // 必须：Verb="runas" 依赖 shell execute
                    Verb = "runas",
                    CreateNoWindow = false   // runas 时 CreateNoWindow 无意义，且子进程需正常显示
                };
                if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    { log?.Invoke("  [!] 无法提权启动进程: " + exePath); return -1; }

                    // 等待子进程退出（ODT 安装可能耗时较长，给 15 分钟超时，与 Exec.PROCESS_TIMEOUT_MS 对齐）
                    const int TIMEOUT_MS = 900000;
                    if (!p.WaitForExit(TIMEOUT_MS))
                    {
                        log?.Invoke("  [!] 提权子进程超时（" + (TIMEOUT_MS / 1000) + " 秒），强制终止");
                        try { p.Kill(); } catch { }
                        return -1;
                    }

                    int rc = p.ExitCode;
                    log?.Invoke("  [*] 提权子进程退出码: " + rc);
                    return rc;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 用户拒绝 UAC 或系统拒绝提权时 Win32Exception
                log?.Invoke("  [!] 用户取消了提权（UAC）: " + ex.Message);
                return -1;
            }
            catch (System.Exception ex)
            {
                log?.Invoke("  [!] 提权运行失败: " + ex.Message);
                return -1;
            }
        }

        /// <summary>把 args[startIndex..] 拼接为带引号的参数字符串（不含前导空格，供日志展示）。</summary>
        private static string JoinQuoted(string[] args, int startIndex)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = startIndex; i < args.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteForCmdLine(args[i]));
            }
            return sb.ToString();
        }

        /// <summary>对单个参数做 Windows 命令行引号处理（含空格/特殊字符时加双引号，内部引号翻倍）。</summary>
        private static string QuoteForCmdLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            bool needsQuote = s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0
                || s.IndexOf('&') >= 0 || s.IndexOf('^') >= 0
                || s.IndexOf('|') >= 0 || s.IndexOf('<') >= 0
                || s.IndexOf('>') >= 0 || s.IndexOf('%') >= 0;
            if (!needsQuote) return s;
            string body = s.Replace("\"", "\"\"");
            // 末尾反斜杠处理（同 Exec.QuoteCmd）：n 个尾随\ 写成 2n+1 个
            int trailing = 0;
            for (int i = body.Length - 1; i >= 0 && body[i] == '\\'; i--) trailing++;
            if (trailing > 0)
                body = body.Substring(0, body.Length - trailing) + new string('\\', trailing * 2 + 1);
            return "\"" + body + "\"";
        }

        // ================================================================
        //  4. 磁盘空间/可写性校验
        // ================================================================

        /// <summary>
        /// 在目标路径（或目标盘）建一个临时文件再删，判断可写。
        /// 用于在挂 junction 前校验目标盘确实可写。
        /// 异常时返回 false（不抛出）。
        /// </summary>
        public static bool IsWritableDisk(string driveOrPath, Action<string> log)
        {
            try
            {
                // 优先用传入路径；若路径不存在，取所在盘根目录
                string testDir;
                if (Directory.Exists(driveOrPath))
                {
                    testDir = driveOrPath;
                }
                else
                {
                    // 取盘符根目录
                    string root = Path.GetPathRoot(driveOrPath);
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    { log?.Invoke("  [!] 路径不存在且无法解析盘符: " + driveOrPath); return false; }
                    testDir = root;
                }

                // 建临时文件（带随机名避免冲突）
                string tmp = Path.Combine(testDir, "cpq_wtest_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(tmp, "ok");
                bool canRead = File.Exists(tmp);
                try { File.Delete(tmp); } catch { /* 删不掉不影响可写判断 */ }
                bool writable = canRead && !File.Exists(tmp); // 建+删都成功才算可写
                if (!writable)
                    log?.Invoke("  [!] 目标路径不可写: " + driveOrPath + "（建/删临时文件失败）");
                return writable;
            }
            catch (Exception ex)
            {
                log?.Invoke("  [!] 可写性检测失败: " + ex.Message);
                return false;
            }
        }
    }
}
