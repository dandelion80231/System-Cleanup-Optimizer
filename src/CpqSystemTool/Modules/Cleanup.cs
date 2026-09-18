using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 系统清理：保守清理 / 全面清理 / 统计预览 / 大空间回收。
    /// </summary>
    internal static class Cleanup
    {
        // 并行清理限流：避免无节制并发导致 I/O 争用/进程挤占。
        // 内层（单方法内的子任务：如 NVIDIA 6 项缓存、包管理器缓存）用较小并发；
        // 外层（方案 B 跨类别）在 MainWindow.Pages 中另设独立限流，二者叠加总并发受控。
        private static readonly ParallelOptions InnerPar = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(3, Environment.ProcessorCount)) };

        // ---- 通用原语（原生 C# 版，参考 ZyperWin++ 等的实现方式）----
        //  改用 System.IO 直接删除，避免每次启停 powershell.exe 的开销。
        //  出错时仍 fallback 到 PowerShell（处理权限/占用等极端情况）。

        // 统一记录"已忽略的异常"，避免重复样板。
        private static void LogIgnored(Exception ex) => DebugLog.Ignore(ex);

        internal static void CleanDir(string name, string path, Action<string> log)
        {
            path = Exec.ExpandEnv(path);
            // 【UI】名称与结果同行输出（旧：名称一行、结果下一行）
            string res;
            if (Directory.Exists(path))
                res = TryCleanDir(path) ? "  [OK]" : "  [SKIP] 部分残留（可能被占用，建议关闭相关程序后重试）";
            else res = "  [SKIP] 路径不存在";
            log(name + res);
        }

        // 尝试清空并删除目录（保留目录本身：删内容后重建空目录）。
        // 返回 true 表示目录已不存在或已清空（成功）；false 表示仍有残留（被占用/权限不足）。
        // PowerShell 兜底不写日志（聚合调用方自行汇总），避免逐目录刷屏。
        // 【修 P2-8】原生替代「cmd /c del /f /q 通配符」：不派生 cmd 进程；
        // 逐文件 catch 跳过被占用文件（等价 del /q 遇锁不中断的行为），返回被占用跳过数。
        // 目录不存在/空匹配 = 0 删 0 占，调用方自行判 [OK]。
        // 【UI】只返回 (已删数, 被占用数)，不再自刷详情行：由调用方把「名称+结果」合成一行输出，
        // 避免「图标缓存 / 已删 15 个文件 / [OK]」三行观感（3 个调用方：IconCache/Recent/Notifications）
        private static (int deleted, int locked) DelFilesInDir(string dirExp, string pattern)
        {
            string dir = Exec.ExpandEnv(dirExp);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return (0, 0);
            int ok = 0, locked = 0;
            foreach (var f in Directory.EnumerateFiles(dir, pattern))
            {
                try { File.Delete(f); ok++; }
                catch (Exception ex) { locked++; DebugLog.Ignore(ex); }
            }
            return (ok, locked);
        }

        private static bool TryCleanDir(string path)
        {
            try
            {
                // 删目录及其所有内容，再重建空目录（保持原语义：清空内容，保留目录本身）
                Directory.Delete(path, true);
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception caughtEx)
            {
                LogIgnored(caughtEx);// 原生批量删除失败，改为逐个文件/目录尝试删除，避免被占用文件导致整个目录无法清理
                try
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { /* 被占用/权限不足则跳过 */ }
                    }
                    var dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
                    Array.Sort(dirs, (a, b) => b.Split(Path.DirectorySeparatorChar).Length.CompareTo(a.Split(Path.DirectorySeparatorChar).Length));
                    foreach (var dir in dirs)
                    {
                        try { Directory.Delete(dir, false); } catch (Exception ex) { DebugLog.Ignore(ex); }
                    }
                    try { Directory.Delete(path, true); Directory.CreateDirectory(path); } catch (Exception ex) { DebugLog.Ignore(ex); }
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }
                // 【P3-17】原生单遍后先统计残留文件：0 个→不必再启动 PS；≤10 个→原生逐个重试一次（锁可能刚释放，
                // 且残留目录空壳借 Delete(true) 再试一次）；>10 个才启动 PowerShell 全量重扫（PS 启动 300~800ms +
                // Get-ChildItem -Recurse 单线程，小残留不值得）
                int residualFiles = CountResidualFiles(path);
                if (residualFiles > 0 && residualFiles <= 10)
                {
                    foreach (var f in EnumResidualFiles(path))
                    {
                        try { File.Delete(f); } catch { /* 仍被占用，交给 PS 兜底 */ }
                    }
                    try { Directory.Delete(path, true); Directory.CreateDirectory(path); } catch (Exception ex2) { DebugLog.Ignore(ex2); }
                    residualFiles = CountResidualFiles(path);
                }
                if (residualFiles > 0)
                {
                    // 兜底：用 PowerShell 静默再清一次（-EA 0 抑制被占用错误）
                    Exec.RunPowerShell("Get-ChildItem -Path " + Exec.QuotePS(path + "\\*") + " -Recurse -Force -EA 0 | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -Recurse -EA 0 }", _ => { });
                }
                // 兜底后再校验：目录已清空才算成功，否则如实返回 false（调用方据此报「部分残留」）
                return !(Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length > 0);
            }
        }

        /// <summary>统计目录下残留文件数（P3-17 用）；路径已不存在/无权限枚举→返回 0，交给调用方末校验兜底。</summary>
        private static int CountResidualFiles(string path)
        {
            try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length; }
            catch { return 0; }
        }

        private static IEnumerable<string> EnumResidualFiles(string path)
        {
            try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        internal static void CleanPath(string name, string path, Action<string> log)
        {
            path = Exec.ExpandEnv(path);
            // 【fix-1】node_modules 目录保护：任何名为 node_modules 的目录一律不删（项目/运行时依赖，非缓存）。
            // 即使未来有其它调用方误传 node_modules 路径，也在此统一拦截，确保清理逻辑不会删除任何 node_modules。
            if (Directory.Exists(path) && Path.GetFileName(path.TrimEnd('\\', '/')).Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            {
                log(name + "  [SKIP] node_modules 受保护（依赖目录，不清理）");
                return;
            }
            // 【UI】名称与结果同行输出（旧：先刷名称行、删完再刷结果行 → 两行观感）
            string res;
            if (File.Exists(path) || Directory.Exists(path))
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    else if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    // 删除后立即校验：确实不存在才算成功（避免部分失败被误报为成功）
                    if (File.Exists(path) || Directory.Exists(path))
                        throw new IOException("删除后路径仍存在");
                    res = "  [OK]";
                }
                catch (Exception caughtEx) { LogIgnored(caughtEx);// 原生删除失败（权限/只读/占用），按 P3-17 分级处理：残留 ≤10 个文件先原生重试，
                    // 仍残留才启动 PowerShell 兜底（避免小残留也启 PS 全量重扫的无谓开销）。0 残留直接原生成功。
                    int residualFiles = CountResidualFiles(path);
                    if (residualFiles > 0 && residualFiles <= 10)
                    {
                        foreach (var f in EnumResidualFiles(path))
                        {
                            try { File.Delete(f); } catch { /* 仍被占用 */ }
                        }
                        if (Directory.Exists(path)) { try { Directory.Delete(path, true); } catch (Exception ex2) { DebugLog.Ignore(ex2); } }
                        residualFiles = CountResidualFiles(path);
                    }
                    if (residualFiles > 0)
                    {
                        // 「文件被另一进程使用」属预期（浏览器/程序运行中），降级为安静提示，不刷 [PS-ERR] 噪声；其余错误仍如实暴露，便于排查真实权限/路径问题。
                        var (ecPS, soPS, sePS) = Exec.RunPowerShellGetFull("Remove-Item -LiteralPath " + Exec.QuotePS(path) + " -Recurse -Force", log);
                        bool inUse = !string.IsNullOrWhiteSpace(sePS)
                            && (sePS.Contains("正由另一进程使用") || sePS.Contains("being used by another process") || sePS.Contains("The process cannot access"));
                        // 兜底后再校验：真正删掉了才算成功，否则如实上报（不再谎报）
                        if (File.Exists(path) || Directory.Exists(path))
                        {
                            if (inUse) res = "  [SKIP] 部分文件被占用（建议关闭相关程序后重试）";
                            else if (!string.IsNullOrWhiteSpace(sePS)) res = "  [PS-ERR] " + sePS.Trim();
                            else res = "  [SKIP] 部分残留（可能被占用，建议关闭相关程序后重试）";
                        }
                        else
                            res = "  [OK] (PS 兜底)";
                    }
                    else
                    {
                        // 原生重试已清干净（或路径已不存在），不必启动 PS
                        if (File.Exists(path) || Directory.Exists(path))
                            res = "  [SKIP] 部分残留（可能被占用，建议关闭相关程序后重试）";
                        else
                            res = "  [OK]";
                    }
                }
            }
            else res = "  [SKIP] 不存在";
            log(name + res);
        }

        // ---- 快速目录大小计算（原生 C# + 并行，替代原 PowerShell Measure-Object）----
        // PowerShell 每次启动 300~800ms，且 Get-ChildItem -Recurse 单线程；对于全选扫描的 30+ 路径，
        // 原生并行枚举通常快 5~10 倍（尤其大缓存目录）。忽略无权限/占用文件，行为与 PowerShell -EA 0 一致。
        // 注意：.NET Framework 4.8 没有 EnumerationOptions，这里用显式栈 + 逐目录 try/catch 实现安全递归。
        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(cur); }
                catch { files = Enumerable.Empty<string>(); }
                foreach (var f in files) yield return f;
                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(cur); }
                catch { dirs = Enumerable.Empty<string>(); }
                foreach (var d in dirs) stack.Push(d);
            }
        }

        internal static double SizeOfNative(string path)
        {
            path = Exec.ExpandEnv(path);
            if (File.Exists(path))
            {
                try { return new FileInfo(path).Length / 1024.0 / 1024.0; }
                catch { return 0; }
            }
            if (!Directory.Exists(path)) return -1; // 标记不存在
            long total = SumFileSizes(EnumerateFilesSafe(path));
            return total / 1024.0 / 1024.0;
        }

        // 并行累加一组文件的大小（字节）；单文件读取失败忽略，目录无法枚举返回 0。
        private static long SumFileSizes(IEnumerable<string> files, int maxFiles = int.MaxValue)
        {
            long total = 0;
            try
            {
                var limited = files.Take(maxFiles);
                Parallel.ForEach<string, long>(limited, () => 0L,
                    (f, state, local) =>
                    {
                        try { local += new FileInfo(f).Length; } catch (Exception ex) { DebugLog.Ignore(ex); }
                        return local;
                    },
                    local => Interlocked.Add(ref total, local));
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
            return total;
        }

        // 批量并行计算多个路径大小，返回与输入同序的结果（日志按原顺序输出，不混乱）。
        private class SizeEntry { public string Name; public string Path; public double MB; }
        private static List<SizeEntry> SizeOfBatch(List<SizeEntry> entries)
        {
            Parallel.ForEach(entries, e => e.MB = SizeOfNative(e.Path));
            return entries;
        }

        internal static double SizeOf(string name, string path, Action<string> log)
        {
            double mb = SizeOfNative(path);
            if (mb < 0) { log(name + " : 不存在"); return 0; }
            log(name + " : 约 " + FmtSizeMB(mb));
            return mb;
        }

        // 【UI】容量格式化：MB 数 ≥1024 自动升 GB（总计 1433 MB → 1.40 GB，避免“一位多长的 MB 数”观感）
        internal static string FmtSizeMB(double mb) => mb >= 1024 ? (mb / 1024).ToString("F2") + " GB" : mb.ToString("F2") + " MB";

        // 【UI】全盘命中扫描聚合：按目录名分组求和，每组一行（旧：每个命中路径各打一行，百行刷屏）
        private static double ScanWholeDriveGroup(bool tier2, string prefix, Action<string> log)
        {
            double total = 0;
            foreach (var g in FindWholeDriveCaches(tier2, log).GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            {
                int count = 0; double sum = 0;
                foreach (var p in g)
                {
                    double mb = SizeOfNative(p);
                    if (mb >= 0) { sum += mb; count++; }
                }
                total += sum;
                log(prefix + g.Key + "（" + count + " 处合计）: 约 " + FmtSizeMB(sum));
            }
            return total;
        }

        internal static bool BroCookies(string baseDir, string name, Action<string> log)
        {
            baseDir = Exec.ExpandEnv(baseDir);
            // 【修】未安装（目录不存在）静默跳过：不刷名称行/不刷 [SKIP]（「不存在的浏览器」本不该出现日志）；
            // 全部未装时由 Cookies() 统一打一行汇总。已知 Cookie 存储位置为主流 5 家（Chrome/Edge/Brave/360/Firefox），
            // 不采用注册表全量枚举：小众/新浏览器（Vivaldi/Opera/QQ 浏览器等）的 Cookie 路径不统一、误删风险不可控，主流 5 家已覆盖绝大多数机器
            if (!Directory.Exists(baseDir)) return false;
            // 【UI】结果与名称合并为一行（「Edge Cookies  [OK]」），与其他清理项的“名字+结果”单行风格统一；
            // 删除完成后一次打行，避免先刷名称再刷 [OK] 的双行观感
            try
            {
                foreach (var profileDir in Directory.EnumerateDirectories(baseDir))
                {
                    string nf = Path.Combine(profileDir, "Network");
                    if (Directory.Exists(nf))
                    {
                        string[] ckFiles = { "Cookies", "Cookies-journal" };
                        foreach (var cf in ckFiles)
                        {
                            string fp = Path.Combine(nf, cf);
                            try { if (File.Exists(fp)) File.Delete(fp); } catch (Exception caughtEx) { LogIgnored(caughtEx); }
                        }
                        // 删除剩余的 Cookies-* 文件
                        foreach (var cf in Directory.EnumerateFiles(nf, "Cookies-*", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(cf); } catch (Exception caughtEx) { LogIgnored(caughtEx); }
                        }
                    }
                }
                log(name + " Cookies  [OK]");
                return true;
            }
            catch (Exception caughtEx)
            {
                LogIgnored(caughtEx);
                string script = "Get-ChildItem " + Exec.QuotePS(baseDir) + " -Directory -EA 0 | ForEach-Object { " +
                    "$nf=Join-Path $_.FullName 'Network'; if(Test-Path $nf){ " +
                    "@('Cookies','Cookies-journal') | ForEach-Object { $f=Join-Path $nf $_; if(Test-Path $f){ Remove-Item $f -Force -EA 0 } }; " +
                    "Get-ChildItem $nf -Filter 'Cookies-*' -Force -EA 0 | Remove-Item -Force -EA 0 } }";
                Exec.RunPowerShell(script, log);
                log(name + " Cookies  [OK] (PS fallback)");
                return true;
            }
        }

        // ---- 各清理子模块 ----
        internal static void Nvidia(Action<string> log)
        {
            log("NVIDIA 缓存清理");
            log("停止 NVIDIA 服务...");
            foreach (var svc in new[] { "NVDisplay.ContainerLocalSystem", "NVIDIA Display Container" })
                Exec.RunCmd(new[] { "net", "stop", svc, "/y" }, log);
            log("  [OK]");
            // 服务停止后，6 项缓存清理互相独立 → 并行加速（受 InnerPar 限流，不挤占主线程）。
            var nvidiaCaches = new Action[]
            {
                () => CleanPath("NVIDIA grd(驱动缓存)", @"%PROGRAMDATA%\NVIDIA Corporation\NVIDIA app\UpdateFramework\ota-artifacts\grd", log),
                () => CleanPath("NVIDIA crd(组件缓存)", @"%PROGRAMDATA%\NVIDIA Corporation\NVIDIA app\UpdateFramework\ota-artifacts\crd", log),
                () => CleanPath("NVIDIA OTA", @"%PROGRAMDATA%\NVIDIA Corporation\OTA", log),
                () => CleanDir("NVIDIA GLCache", @"%LOCALAPPDATA%\NVIDIA\GLCache", log),
                () => CleanDir("NVIDIA D3D", @"%LOCALAPPDATA%\NVIDIA\D3d", log),
                () => CleanDir("NVIDIA ComputeCache", @"%APPDATA%\NVIDIA\ComputeCache", log),
            };
            Parallel.Invoke(InnerPar, nvidiaCaches);
        }

        internal static void NetCache(Action<string> log)
        {
            // 【UI】名称+结果同行（旧：名称一行、[OK]/[SKIP] 下一行）
            string res = null;
            bool done = false;
            foreach (var ng in new[] {
                @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\ngen.exe",
                @"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\ngen.exe" })
            {
                string p = Exec.ExpandEnv(ng);
                if (File.Exists(p)) { Exec.RunCmd(new[] { p, "executequeueditems" }, log); res = "  [OK]"; done = true; break; }
            }
            if (!done) res = "  [SKIP] ngen not found";
            log(".NET 程序集缓存(Native Image)" + res);
        }

        internal static void Defender(Action<string> log)
        {
            CleanDir("Defender Support", @"%ProgramData%\Microsoft\Windows Defender\Support", log);
            CleanDir("Defender 扫描历史", @"%ProgramData%\Microsoft\Windows Defender\Scans\History\Resource", log);
        }

        internal static void IconCache(Action<string> log)
        {
            // 【UI】名称+结果同行：旧三行（名称/已删 N 个/[OK]）合并为一行
            var (del, locked) = DelFilesInDir(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "iconcache*.db");
            log("图标缓存：已删 " + del + " 个文件" + (locked > 0 ? "（" + locked + " 个被占用）" : "") +
                (locked == 0 ? "  [OK]" : "  [FAIL] 部分被占用未清理"));
        }

        internal static void FontCache(Action<string> log)
        {
            log("字体缓存");
            Exec.RunCmd(new[] { "net", "stop", "FontCache", "/y" }, log);
            // 清空 FontCache 目录（CleanDir 已改为原生 C#，不启 PowerShell）
            CleanDir("FontCache", @"%SystemRoot%\ServiceProfiles\LocalService\AppData\Local\FontCache", log);
            string fnt = Exec.ExpandEnv(@"%SystemRoot%\System32\FNTCACHE.DAT");
            if (File.Exists(fnt)) { try { File.Delete(fnt); } catch (Exception caughtEx) { LogIgnored(caughtEx);} }
            int fstart = Exec.RunCmd(new[] { "net", "start", "FontCache" }, log);
            if (fstart == 0)
                log("  [OK] 字体服务已启动");
            else
            {
                // net start 退出码非 0 可能是「已运行」或「启动失败」；以 sc query 实际状态为准，避免误报成功。
                string fst = Exec.RunCmdGet(new[] { "sc", "query", "FontCache" }, log);
                if (!string.IsNullOrEmpty(fst) && fst.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0)
                    log("  [OK] 字体服务已启动（此前已在运行）");
                else
                    log("  [FAIL] 字体服务启动失败（退出码 " + fstart + "，可能需要管理员权限，或在 services.msc 手动启动）");
            }
        }

        internal static void EventLogs(Action<string> log)
        {
            log("⚠️ 事件日志（破坏性）：将清空【全部】日志通道，操作不可恢复；系统之后会自动重建为空日志。");
            string outp = Exec.RunCmdGet(new[] { "wevtutil", "el" }, log);
            // 先串行枚举全部通道（单次进程、需完整输出再解析），再并行清空各通道以加速。
            var channels = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(line => line.Trim())
                               .Where(s => !string.IsNullOrEmpty(s))
                               .ToList();
            int failed = 0;
            Parallel.ForEach(channels, InnerPar, ch => { if (Exec.RunCmd(new[] { "wevtutil", "cl", ch }, log) != 0) Interlocked.Increment(ref failed); });
            log(failed == 0 ? "  [OK]" : "  [FAIL] " + failed + " 个日志通道清空失败（系统保护/正在写入的通道属正常现象，其余已清空；重启后重试可能清掉剩余）");
        }

        internal static void CrashDumps(Action<string> log)
        {
            CleanDir("用户崩溃转储", @"%LOCALAPPDATA%\CrashDumps", log);
        }

        internal static void Recent(Action<string> log)
        {
            // 【UI】名称+结果同行：旧 4+ 行（名称/每目录 已删 N 个/[OK]）合并为一行
            int delTotal = 0, lockedTotal = 0;
            foreach (var p in new[] {
                @"%APPDATA%\Microsoft\Windows\Recent",
                @"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations",
                @"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations" })
            {
                var (d, l) = DelFilesInDir(p, "*");
                delTotal += d; lockedTotal += l;
            }
            log("最近使用 / 跳转列表：已删 " + delTotal + " 个文件" + (lockedTotal > 0 ? "（" + lockedTotal + " 个被占用）" : "") +
                (lockedTotal == 0 ? "  [OK]" : "  [FAIL] " + lockedTotal + " 个被占用未清理"));
        }

        internal static void WuLogs(Action<string> log) { CleanDir("Windows Update 日志", @"%SystemRoot%\Logs\WindowsUpdate", log); }
        internal static void CbsPersist(Action<string> log) { CleanDir("CBS 持久日志", @"%SystemRoot%\Logs\CBS\Persist", log); }

        // Whesvc（Windows 健康状况和优化体验）本地性能诊断追踪目录。
        // 仅本机卡顿时生成 ETL 日志，可安全删除、服务重新启用时会再生；服务运行时文件被占用，CleanDir 会自动跳过被锁文件。
        internal static void WhesvcDiag(Action<string> log) { CleanDir("Whesvc 诊断日志", @"%SystemRoot%\Temp\DiagOutputDir\Whesvc", log); }

        internal static void Notifications(Action<string> log)
        {
            // 【UI】名称+结果同行：旧三行（名称/已删 N 个/[OK]）合并为一行
            var (del, locked) = DelFilesInDir(@"%LOCALAPPDATA%\Microsoft\Windows\Notifications", "wpndatabase*.db");
            log("通知数据库：已删 " + del + " 个" + (locked > 0 ? "（" + locked + " 个被占用）" : "") +
                (locked == 0 ? "  [OK]" : "  [FAIL] 部分被占用未清理"));
        }

        internal static void Spotlight(Action<string> log)
        {
            // 【UI】名称+结果同行（旧：名称一行、[OK] 下一行）
            string baseDir = Exec.ExpandEnv(@"%LOCALAPPDATA%\Packages");
            if (Directory.Exists(baseDir))
            {
                foreach (var pkgDir in Directory.EnumerateDirectories(baseDir, "Microsoft.Windows.ContentDeliveryManager_*"))
                {
                    string assetsDir = Path.Combine(pkgDir, @"LocalState\Assets");
                    if (Directory.Exists(assetsDir))
                    {
                        try { Directory.Delete(assetsDir, true); Directory.CreateDirectory(assetsDir); } catch (Exception caughtEx) { LogIgnored(caughtEx);}
                    }
                }
            }
            log("Windows Spotlight 壁纸缓存  [OK]");
        }

        internal static void Activity(Action<string> log) { CleanDir("活动历史", @"%LOCALAPPDATA%\ConnectedDevicesPlatform", log); }
        internal static void BranchCache(Action<string> log) { CleanDir("BranchCache", @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\PeerDist", log); }

        internal static void Recycle(Action<string> log)
        {
            // 【UI】名称+结果同行（旧：名称一行、结果下一行）；P3 复审的事后校验语义保留
            Exec.RunPowerShell("Clear-RecycleBin -Force -EA 0", log);
            // 【P3 复审】Clear-RecycleBin 被 -EA 0 静默，失败（受保护项/访问拒绝）也会打 [OK]；
            // 以 C:\$Recycle.bin 残留量兑底（requireAdministrator 下可直接枚举）
            int remain = -1;
            try { if (Directory.Exists(@"C:\$Recycle.bin")) remain = Directory.GetFiles(@"C:\$Recycle.bin", "*", SearchOption.AllDirectories).Length; else remain = 0; }
            catch { remain = -1; }
            string res = remain > 0 ? "[PARTIAL] 回收站清理不完全（受保护/锁定项，余 " + remain + " 个文件）"
                : remain >= 0 ? "[OK]"
                : "[?] 无法验证回收站状态（枚举权限不足），已执行 Clear-RecycleBin";
            log("回收站  " + res);
        }

        internal static void Cookies(Action<string> log)
        {
            log("浏览器 Cookies（会登出网站登录态）...");
            // 各浏览器 Cookies 互不相关 → 并行清理（含 Firefox 的 PowerShell 块）。
            // 未安装的浏览器静默跳过（BroCookies 返回 false 且不打行）；全部未装时补一行汇总，避免本项日志空落
            bool[] touched = new bool[5];
            var jobs = new Action[]
            {
                () => touched[0] = BroCookies(@"%LOCALAPPDATA%\Google\Chrome\User Data", "Chrome", log),
                () => touched[1] = BroCookies(@"%LOCALAPPDATA%\Microsoft\Edge\User Data", "Edge", log),
                () => touched[2] = BroCookies(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", "Brave", log),
                () => touched[3] = BroCookies(@"%LOCALAPPDATA%\360Chrome\Chrome\User Data", "360安全浏览器", log),
                () => {
                    try
                    {
                        string fb = Exec.ExpandEnv(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
                        if (Directory.Exists(fb))
                        {
                            // 【P3 复审·OCR修真 bug】原脚本内层 ForEach-Object 的 $_（文件名）遮蔽了外层 $_（目录 FileInfo），
                            // Join-Path $_.FullName $_ 会拼成 “cookies.sqlite\cookies.sqlite”，Test-Path 永远为假 → 兜底静默失效。
                            // 外层先捕获 $d=目录全路径，内层再按文件名拼：
                            string script = "Get-ChildItem " + Exec.QuotePS(fb) + " -Directory -EA 0 | ForEach-Object { " +
                                "$d=$_.FullName; " +
                                "@('cookies.sqlite','cookies.sqlite-shm','cookies.sqlite-wal') | " +
                                "ForEach-Object { $f=Join-Path $d $_; if(Test-Path $f){ Remove-Item $f -Force -EA 0 } } }";
                            Exec.RunPowerShell(script, log);
                            touched[4] = true;
                        }
                    }
                    catch (Exception caughtEx) { LogIgnored(caughtEx); }
                }
            };
            Parallel.Invoke(InnerPar, jobs);
            // 【修】原此处另打一个总 [OK]（每个浏览器已各自报 [OK]，多出来的那个是用户看到的“第 5 个 [OK]”）→ 删除，避免假成功叠加
            if (!touched[0] && !touched[1] && !touched[2] && !touched[3] && !touched[4])
                log("  [SKIP] 未检测到已安装的浏览器（Chrome/Edge/Brave/360/Firefox），无 Cookies 可清");
        }

        // ---- 开发/包管理器缓存：2026-09-18 起并入第三档确认流（扫描 → 可点开逐项查看 → 逐项勾选删除） ----
        // 原为清理页独立一级动作项（09-15 事故后默认不勾）；用户反馈：放独立项时「全选清理」仍可能误扫，
        // 第三档有二次确认且能点开看，更安全。node_modules 依赖目录不在名称表（Tier1DirNames）内，
        // 且删除侧有统一保护，任何情况下都不会被列出/删除。
        private static readonly (string name, string path)[] DevCacheFixed =
        {
            ("npm 缓存", @"%LOCALAPPDATA%\npm-cache"),
            ("npm 缓存(Roaming)", @"%APPDATA%\npm-cache"),
            ("pnpm 缓存", @"%LOCALAPPDATA%\pnpm-cache"),
            ("NuGet v3 缓存", @"%LOCALAPPDATA%\NuGet\v3-cache"),
            ("NuGet http 缓存", @"%LOCALAPPDATA%\NuGet\http-cache"),
            ("NuGet 包全局缓存", @"%USERPROFILE%\.nuget\packages"),
            ("pip 缓存", @"%LOCALAPPDATA%\pip\Cache"),
            ("uv 缓存", @"%LOCALAPPDATA%\uv\cache"),
            ("Yarn 缓存", @"%LOCALAPPDATA%\Yarn\Cache"),
            ("cargo registry 缓存", @"%USERPROFILE%\.cargo\registry\cache"),
            ("cargo registry 源码", @"%USERPROFILE%\.cargo\registry\src"),
        };
        private const double Tier3DevMinMB = 50.0;   // 开发缓存 ≥50MB 才进第三档名单，避免小缓存刷屏

        /// <summary>第三档候选：开发/包管理器缓存（固定位 11 处 + 全盘额外命中；仅入册供逐项确认，不直接删除）。</summary>
        private static void CollectDevCacheCandidates(Action<string> log, List<Tier3Candidate> found)
        {
            log("  [开发/包管理器缓存] 固定缓存位 + 全盘额外扫描（node_modules 依赖目录不入册）...");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string p)
            {
                if (!seen.Add(p.ToLowerInvariant())) return;
                if (!Directory.Exists(p)) return;
                double mb = DirSizeCapped(p, 300000) / 1024.0 / 1024.0;
                if (mb < Tier3DevMinMB) return;
                DateTime lw = Directory.GetLastWriteTime(p), la = Directory.GetLastAccessTime(p);
                found.Add(new Tier3Candidate { Path = p, SizeMB = Math.Round(mb, 1), LastActivity = lw > la ? lw : la, DaysUnused = -1, Description = "开发/包管理器缓存（可重建：下次 install 重新下载）；删除安全" });
                log("    发现开发缓存: " + p + "  —  约 " + FmtSizeMB(mb));
            }
            foreach (var (name, raw) in DevCacheFixed) Add(Exec.ExpandEnv(raw));
            foreach (var p in FindWholeDriveCaches(false, log)) Add(p);   // 全盘额外命中（固定位已在其排除表内，不会重复）
        }

        // ---- 第二档：基本安全（软件自动更新的旧安装包，删了只是下次更新重下） ----
        internal static void UpdatePkgTier2(Action<string> log)
        {
            log("更新残留·安装包缓存（第二档·基本安全，下次更新会重下）");
            // ClickOnce / 安装程序下载缓存，删了相关程序再次启动时会重新下载
            CleanDir("ClickOnce 安装缓存", @"%LOCALAPPDATA%\Downloaded Installations", log);
            // Windows 更新 P2P 分发缓存（Delivery Optimization）
            CleanDir("Delivery Optimization 缓存", @"%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache", log);
            // NVIDIA 下载器缓存
            CleanDir("NVIDIA 下载器缓存", @"%PROGRAMDATA%\NVIDIA Corporation\Downloader", log);
            // 应用自动更新残留（仅本机存在对应目录时才会清理，不存在则自动跳过）
            CleanDir("ComfyUI 更新缓存", @"%LOCALAPPDATA%\comfyui-desktop-2-updater", log);
            CleanDir("g-menu 更新缓存", @"%LOCALAPPDATA%\g-menu-updater", log);
            log("  [全盘筛查] 在 C 盘用户/程序目录查找额外同类更新残留...");
            CleanWholeDriveCaches(true, "全盘更新残留·", log);
        }

        // ---- 全盘筛查：在 C 盘用户/程序相关根目录中按安全模式名发现额外缓存/更新残留（避免遗漏） ----
        //   仅扫描用户与程序数据所在根（Users / ProgramData / Program Files / *AppData），不触碰 Windows 系统目录。
        // 【fix-1】node_modules 已从第一档缓存名单移除：它是项目/运行时依赖目录而非缓存，
        // 全盘筛查不再收集任何名为 node_modules 的目录；删除侧另有 CleanPath 统一保护（见下）。
        private static readonly HashSet<string> Tier1DirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "npm-cache", "pnpm-cache", "yarn-cache", "__pycache__", "v3-cache", "http-cache"
        };
        private static readonly HashSet<string> Tier2DirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "downloaded installations", "ota-artifacts", "ota"
        };
        private static readonly string[] Tier2Suffixes = { "updater" };

        private static string[] ScanRoots()
        {
            var r = new List<string>
            {
                Exec.ExpandEnv(@"%LOCALAPPDATA%"),
                Exec.ExpandEnv(@"%APPDATA%"),
                Exec.ExpandEnv(@"%PROGRAMDATA%"),
                Exec.ExpandEnv(@"%ProgramFiles%"),
                Exec.ExpandEnv(@"%ProgramFiles(x86)%"),
                Exec.ExpandEnv(@"%USERPROFILE%")
            };
            return r.Where(Directory.Exists).ToArray();
        }

        /// <summary>
        /// 构造「扫描根边界集合」（性能优化）。
        /// LOCALAPPDATA / APPDATA 都是 USERPROFILE 的子树（C:\Users\x\AppData\Local|Roaming），
        /// 原实现把三者并列扫描，等于把同一棵用户目录树完整遍历 3 遍。
        /// 遍历某个根时，一旦遇到集合内的目录就停止下钻——那棵子树会由它自己作为独立根、
        /// 以完整的深度预算（maxDepth）再扫一遍。因此既不重复遍历共享前缀，又不会因为
        /// 「根变浅 → 深度预算被吃掉」而漏扫深层目录，结果集与逐根全量扫描完全一致。
        /// 用 OrdinalIgnoreCase 集合直接比字符串，不额外规范化，遍历时零分配。
        /// </summary>
        private static HashSet<string> BuildRootBoundary(IEnumerable<string> roots)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in roots)
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                set.Add(r.TrimEnd('\\', '/'));
            }
            return set;
        }

        /// <summary>是否是不该继续下钻的「其它扫描根」边界：是 → 只做本层判定，不再递归。</summary>
        private static bool IsOtherScanRoot(string dir, HashSet<string> rootBoundary)
        {
            return rootBoundary != null && rootBoundary.Contains(dir);
        }

        private static bool IsProtectedScanPath(string full)
        {
            string f = full.Replace('/', '\\').ToLowerInvariant();
            return f.StartsWith(@"c:\windows") || f.Contains(@"\windows\") || f.Contains(@"\system32\") ||
                   f.Contains(@"\winsxs\") || f.Contains(@"\windowsapps\") || f.Contains(@"$recycle.bin") ||
                   f.Contains(@"\recovery\") || f.Contains(@"\boot\");
        }

        private static void CollectDirsByName(string root, HashSet<string> names, bool tier2, List<string> outPaths, HashSet<string> exclude, HashSet<string> rootBoundary, int maxDepth, int depth)
        {
            if (depth > maxDepth || !Directory.Exists(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); }
            catch { return; }
            foreach (var d in subs)
            {
                // 扫描侧拦截应用运行时目录（.pi/Roaming\npm/pi-desktop）：
                // 2026-09-15 事故：node_modules 全盘筛查误扫误删 pi 运行时依赖。删除侧 IsProtectedPath 已有闸，扫描侧不拦会误列误报。
                if (IsProtectedScanPath(d) || IsAppRuntimePath(d)) continue;
                string nm = Path.GetFileName(d);
                bool hit = names.Contains(nm);
                if (!hit && tier2)
                {
                    foreach (var suf in Tier2Suffixes)
                        if (nm.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                }
                if (hit)
                {
                    string norm = d.ToLowerInvariant();
                    if (exclude == null || !exclude.Contains(norm)) outPaths.Add(d);
                    continue; // 命中后不再向下钻取，避免嵌套重复
                }
                // 遇到另一个扫描根（如扫 USERPROFILE 时走到 AppData\Local）→ 停止下钻：
                // 那棵子树会由它自己作为独立根按完整深度再扫一遍，此处继续递归只会白扫一遍。
                if (IsOtherScanRoot(d, rootBoundary)) continue;
                CollectDirsByName(d, names, tier2, outPaths, exclude, rootBoundary, maxDepth, depth + 1);
            }
        }

        /// <summary>在 C 盘安全根目录中按模式名发现额外缓存（tier2=false）或更新残留（tier2=true），排除已固定目录、去重。</summary>
        internal static List<string> FindWholeDriveCaches(bool tier2, Action<string> log)
        {
            var names = tier2 ? Tier2DirNames : Tier1DirNames;
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!tier2)
            {
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\npm-cache"));
                exclude.Add(Exec.ExpandEnv(@"%APPDATA%\npm-cache"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\pnpm-cache"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\NuGet\v3-cache"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\NuGet\http-cache"));
                exclude.Add(Exec.ExpandEnv(@"%USERPROFILE%\.nuget\packages"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\pip\Cache"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\uv\cache"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\Yarn\Cache"));
                exclude.Add(Exec.ExpandEnv(@"%USERPROFILE%\.cargo\registry\cache"));
                exclude.Add(Exec.ExpandEnv(@"%USERPROFILE%\.cargo\registry\src"));
            }
            else
            {
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\Downloaded Installations"));
                exclude.Add(Exec.ExpandEnv(@"%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache"));
                exclude.Add(Exec.ExpandEnv(@"%PROGRAMDATA%\NVIDIA Corporation\Downloader"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\comfyui-desktop-2-updater"));
                exclude.Add(Exec.ExpandEnv(@"%LOCALAPPDATA%\g-menu-updater"));
            }
            // 多根目录并行扫描，各 root 使用独立列表避免锁竞争，最后合并去重。
            // rootBoundary：扫到别的扫描根就停止下钻，避免 LOCALAPPDATA/APPDATA ⊂ USERPROFILE
            // 造成的同一棵树扫 3 遍（详见 BuildRootBoundary 注释）。
            var roots = ScanRoots();
            var rootBoundary = BuildRootBoundary(roots);
            var bag = new ConcurrentBag<string>();
            Parallel.ForEach(roots, root =>
            {
                var local = new List<string>();
                CollectDirsByName(root, names, tier2, local, exclude, rootBoundary, 5, 0);
                foreach (var p in local) bag.Add(p);
            });
            return bag.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---- 防误删保护：受保护根目录黑名单 ----
        // 全盘筛查是按「目录名 / 后缀」模糊匹配命中的（如 ota、*-updater），
        // 在 Program Files / ProgramData 这类目录里极易命中与缓存无关的软件目录或用户数据，
        // 一旦递归删除就是不可逆的数据丢失。因此在这个递归删除入口统一校验：
        // 命中黑名单的路径直接跳过并记录跳过原因到 log（防止误删用户数据），
        // 不弹确认框、不打断自动化清理流程。
        // 【修 P2-7】原 EnsureProtectedRoots 是「双检 + 无锁赋值」惰性初始化，check 与 assign 不同原子，
        // 两线程并发首次调用时可双建/互相覆盖。现改为静态构造器：CLR 保证 static ctor 线程安全且只跑一次，
        // 字段 readonly，删除手双检。
        private static readonly string[] _protectedSubtreeRoots;   // 连同其子目录一并保护
        private static readonly string[] _protectedSelfRoots;      // 仅保护该目录本身
        private static readonly string[] _appRuntimeProtectedRoots; // 应用运行时根（遍历侧轻拦截）

        static Cleanup()
        {
            _protectedSubtreeRoots = new[]
            {
                @"%SystemRoot%",            // C:\Windows
                @"%ProgramFiles%",
                @"%ProgramFiles(x86)%",
                @"%ProgramData%",
                // 【2026-09-15 事故】以下为应用运行时目录（非缓存），内里的 node_modules 是程序本体/插件依赖，
                // 不能当第一档缓存删：实发中全盘 node_modules 筛查误删 .pi\agent\npm 与 Roaming\npm，导致 pi 损坏、需抢救重装。
                @"%USERPROFILE%\.pi",      // pi 代理运行时（插件/技能/npm 包）
                @"%APPDATA%\npm",          // npm 全局包（pi 本体装在这里）
                @"%APPDATA%\pi-desktop",   // PiDeck 应用数据（Local Storage/Preferences 等）
                @"%LOCALAPPDATA%\pi-desktop"
            }.Select(Exec.ExpandEnv).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            // 仅应用运行时根（.pi/npm/pi-desktop）：遍历侧轻拦截专用。
            // 不能用 IsProtectedPath 整体拦——它含 Program Files/ProgramData，而它们是扫描根，
            // 整体拦会让第二档「更新残留」(ota-artifacts 等) 在这些目录里彻底哑火。
            _appRuntimeProtectedRoots = new[]
            {
                @"%USERPROFILE%\.pi",
                @"%APPDATA%\npm",
                @"%APPDATA%\pi-desktop",
                @"%LOCALAPPDATA%\pi-desktop"
            }.Select(Exec.ExpandEnv).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            // 用户目录根本身不允许删除（防止误删用户数据）；其下 AppData 等缓存目录
            // 仍是全盘筛查的既定目标，只保护根本身以免整个用户目录被清空。
            _protectedSelfRoots = new[] { @"%USERPROFILE%" }
                .Select(Exec.ExpandEnv).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        }

        /// <summary>应用运行时根（.pi/npm/pi-desktop）子树判定：遍历侧专用轻拦截，避免误扫误删 pi 等应用的运行时依赖。</summary>
        private static bool IsAppRuntimePath(string fullPath)
        {
            string norm = NormalizeForCompare(fullPath);
            if (norm == null) return false;
            foreach (var root in _appRuntimeProtectedRoots)
            {
                string r = NormalizeForCompare(root);
                if (r == null) continue;
                if (norm == r || norm.StartsWith(r + @"\", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>路径规范化：Path.GetFullPath → '/' 统一为 '\' → 去尾部 '\' → 小写，便于大小写不敏感的前缀比较。</summary>
        private static string NormalizeForCompare(string path)
        {
            try { return Path.GetFullPath(path).Replace('/', '\\').TrimEnd('\\').ToLowerInvariant(); }
            catch (Exception caughtEx) { LogIgnored(caughtEx); return null; }
        }

        /// <summary>
        /// 待删路径是否受保护：位于受保护根目录之内、就是受保护根目录本身，或是某个盘符根目录（C:\、D:\ …）。
        /// 命中前缀后必须紧跟分隔符或已结束，避免 "C:\Program Files" 误匹配 "C:\Program FilesData"。
        /// </summary>
        private static bool IsProtectedPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return true;   // 空路径一律不删
            string norm = NormalizeForCompare(fullPath);
            if (norm == null) return true;                          // 无法规范化的路径视为受保护，宁可不删
            if (norm.Length <= 2 || (norm.Length == 3 && norm[1] == ':' && norm[2] == '\\')) return true; // 盘符根目录

            foreach (var root in _protectedSubtreeRoots)
            {
                string r = NormalizeForCompare(root);
                if (r == null) continue;
                if (norm == r || norm.StartsWith(r + "\\", StringComparison.Ordinal)) return true;
            }
            foreach (var root in _protectedSelfRoots)
            {
                string r = NormalizeForCompare(root);
                if (r == null) continue;
                if (norm == r) return true;
            }
            return false;
        }

        // 全盘筛查批量清理 + 聚合日志：按目录名分组，每组只打一行汇总（清理 N 处 / M 处残留），
        // 避免开发机上百个 node_modules/__pycache__ 每个刷一行导致日志爆炸。
        internal static void CleanWholeDriveCaches(bool tier2, string prefix, Action<string> log)
        {
            var paths = FindWholeDriveCaches(tier2, log);
            if (paths.Count == 0) { log("  [全盘筛查] 未额外发现" + (tier2 ? "更新残留" : "缓存")); return; }
            foreach (var g in paths.GroupBy(p => Path.GetFileName(p)).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                int ok = 0, skip = 0, guard = 0;
                foreach (var p in g)
                {
                    // 防误删：受保护根（Windows / Program Files / ProgramData / 用户目录根 / 盘符根）
                    // 下的同名命中可能是用户数据，跳过删除并记录跳过原因，不打断自动化清理。
                    if (IsProtectedPath(p))
                    {
                        guard++;
                        log("  [SKIP] 受保护路径，跳过删除（防误删用户数据）: " + p);
                        continue;
                    }
                    if (TryCleanDir(p)) ok++; else skip++;
                }
                string line = "  " + prefix + g.Key + ": 清理 " + ok + " 处";
                if (skip > 0) line += "，" + skip + " 处残留（被占用）";
                if (guard > 0) line += "，" + guard + " 处跳过（受保护路径）";
                // 【修】标签与内容自洽：有残留/受保护跳过时不能盖 [OK]（原逻辑 ok>0 即 [OK]，
                // 出现「清理 23 处，3 处残留（被占用） [OK]」自相矛盾）；[PARTIAL] 会让尾行“重启再清”提示正确触发
                if (ok > 0 && skip == 0 && guard == 0) line += " [OK]";
                else if (ok > 0) line += " [PARTIAL]";
                else if (skip > 0) line += " [PARTIAL]";
                else line += " [SKIP] 受保护路径，未删除";
                log(line);
            }
        }

        // ---- 第三档：旧资产/可能的数据（多半可删，但需逐项确认；先扫描后清理） ----
        internal class Tier3Candidate
        {
            public string Path;
            public double SizeMB;
            public DateTime LastActivity;
            public int DaysUnused;
            public string Description;
        }

        // 任何祖先或自身名在保留列表中 → 跳过（保护系统/关键用户/常用软件数据）
        // 注意：不保留 "appdata" / "users" 根，否则第三档会漏掉 AppData\Local 下的停用工具旧数据
        private static readonly HashSet<string> Tier3KeepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "documents","desktop","pictures","videos","music","saved games","contacts","links",
            "downloads","searches","favorites","program files","program files (x86)","windows",
            "programdata","microsoft","mozilla","google","brave","steam","epic games",
            "tencent","onedrive","apple","intel","amd","nvidia","dell","hp","lenovo","realtek","windowsapps",
            "openclaw",".openclaw","workbuddy",".workbuddy","qclaw"
        };
        private const int Tier3DaysThreshold = 60;
        private const double Tier3MBThreshold = 200.0;
        private static readonly HashSet<string> Tier3SigNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "webcast_mate","jianyingpro",".lmstudio","comfyui","blender","blenderkit"
        };
        private static readonly string[] Tier3SigPatterns = { @"\.old$", @"_old$", "backup", "deprecated", "old_version", "version_old" };
        private static readonly Dictionary<string, string> Tier3DescMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "webcast_mate", "抖音直播/虚拟偶像工具数据；可能包含模型或录像，不使用时可删" },
            { "jianyingpro", "剪映项目/缓存；项目文件可能有用，缓存部分可清" },
            { ".lmstudio", "LM Studio 下载的模型；删了需要重新下载" },
            { ".qclaw-backups", "QClaw 历史备份；通常只保留最新即可" },
            { "obsplus-virtualcam", "OBS Plus 虚拟摄像头缓存/临时文件" },
            { "comfyui", "ComfyUI 模型/节点/工作流缓存" },
            { "backup", "软件自动生成的备份目录；通常只保留最新即可" },
            { "deprecated", "已弃用的旧组件/脚本；一般可安全删除" },
            { "old_version", "旧版本残留；升级后通常无用" },
            { "old", "旧数据/旧版本残留；确认无用后可删" }
        };

        private static string GetTier3Desc(string path)
        {
            string nm = Path.GetFileName(path);
            if (Tier3DescMap.TryGetValue(nm, out var d)) return d;
            // 按路径部分匹配（优先最长/最具体的 key）
            string bestKey = "", bestVal = "";
            foreach (var kv in Tier3DescMap)
            {
                if (path.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 && kv.Key.Length > bestKey.Length)
                {
                    bestKey = kv.Key;
                    bestVal = kv.Value;
                }
            }
            return bestVal;
        }

        private static bool InKeepPath(string full)
        {
            var parts = full.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts) if (Tier3KeepNames.Contains(p)) return true;
            return false;
        }

        private static long DirSizeCapped(string path, int maxFiles)
        {
            return SumFileSizes(EnumerateFilesSafe(path), maxFiles);
        }

        private static void CollectTier3Sig(string root, List<string> outPaths, HashSet<string> rootBoundary, int maxDepth, int depth)
        {
            if (depth > maxDepth || !Directory.Exists(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); } catch { return; }
            foreach (var d in subs)
            {
                if (IsProtectedScanPath(d) || IsAppRuntimePath(d)) continue;   // 受保护根子树（.pi/npm 运行时等）不进第三档签名扫描
                string nm = Path.GetFileName(d);
                bool hit = Tier3SigNames.Contains(nm);
                if (!hit)
                {
                    foreach (var pat in Tier3SigPatterns)
                        if (Regex.IsMatch(nm, pat, RegexOptions.IgnoreCase)) { hit = true; break; }
                }
                if (hit) { outPaths.Add(d); continue; }
                // 同 CollectDirsByName：不下钻到其它扫描根，避免重复遍历 USERPROFILE 下的 AppData 子树
                if (IsOtherScanRoot(d, rootBoundary)) continue;
                CollectTier3Sig(d, outPaths, rootBoundary, maxDepth, depth + 1);
            }
        }

        private static void CollectTier3LargeOld(string root, List<Tier3Candidate> found, HashSet<string> rootBoundary, int maxDepth, int depth)
        {
            if (depth > maxDepth || found.Count >= 60 || !Directory.Exists(root)) return;
            if (InKeepPath(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); } catch { return; }
            foreach (var d in subs)
            {
                if (IsProtectedScanPath(d) || IsAppRuntimePath(d) || InKeepPath(d)) continue;   // 受保护根子树不进「大且旧」候选
                string nm = Path.GetFileName(d);
                if (nm.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || Tier1DirNames.Contains(nm)) continue;
                if (found.Count >= 60) break;
                if (depth >= 1)
                {
                    DateTime lastWrite = Directory.GetLastWriteTime(d);
                    DateTime lastAccess = Directory.GetLastAccessTime(d);
                    DateTime lastAct = lastWrite > lastAccess ? lastWrite : lastAccess;
                    int days = (int)(DateTime.Now - lastAct).TotalDays;
                    if (days >= Tier3DaysThreshold)
                    {
                        double mb = DirSizeCapped(d, 300000) / 1024.0 / 1024.0;
                        if (mb >= Tier3MBThreshold)
                        {
                            string desc = GetTier3Desc(d);
                            found.Add(new Tier3Candidate { Path = d, SizeMB = Math.Round(mb, 1), LastActivity = lastAct, DaysUnused = days, Description = desc });
                        }
                    }
                }
                // 同 CollectDirsByName：不下钻到其它扫描根，避免重复遍历 USERPROFILE 下的 AppData 子树
                if (IsOtherScanRoot(d, rootBoundary)) continue;
                CollectTier3LargeOld(d, found, rootBoundary, maxDepth, depth + 1);
            }
        }

        internal static void ScanTier3(Action<string> log, out List<Tier3Candidate> found)
        {
            found = new List<Tier3Candidate>();
            log("=== 第三档·旧资产筛查（先扫描，删除前逐项确认）===");
            log("  规则：仅列出【≥" + Tier3MBThreshold + " MB 且 ≥" + Tier3DaysThreshold + " 天未使用】或【已知停用工具/备份旧目录】或【开发/包管理器缓存 ≥" + Tier3DevMinMB.ToString("F0") + " MB】，且不含系统关键数据；删除需你逐项勾选确认。node_modules 依赖目录永不入册。");
            var roots = ScanRoots();
            var rootBoundary = BuildRootBoundary(roots);   // 避免 LOCALAPPDATA/APPDATA ⊂ USERPROFILE 重复遍历
            var sigBag = new ConcurrentBag<string>();
            Parallel.ForEach(roots, r =>
            {
                var local = new List<string>();
                CollectTier3Sig(r, local, rootBoundary, 4, 0);
                foreach (var p in local) sigBag.Add(p);
            });
            foreach (var p in sigBag.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (InKeepPath(p) || !Directory.Exists(p)) continue;
                double mb = DirSizeCapped(p, 300000) / 1024.0 / 1024.0;
                if (mb < Tier3MBThreshold) continue;   // 签名命中也按大小筛选，避免 0MB 小备份刷屏
                DateTime lw = Directory.GetLastWriteTime(p), la = Directory.GetLastAccessTime(p);
                DateTime lastAct = lw > la ? lw : la;
                string desc = GetTier3Desc(p);
                // 【UI】签名命中不走「≥N 天未使用」判定（DaysUnused=-1 标记），日志不再显示误导性的「已 0 天未使用」
                found.Add(new Tier3Candidate { Path = p, SizeMB = Math.Round(mb, 1), LastActivity = lastAct, DaysUnused = -1, Description = desc });
                log("  发现旧资产(签名): " + p + "  —  约 " + FmtSizeMB(mb) + "（" + desc + "）");
            }
            // 大且旧扫描：每个 root 独立收集，最后合并去重/排序，避免共享 found 列表的锁竞争。
            var largeOldBag = new ConcurrentBag<Tier3Candidate>();
            Parallel.ForEach(roots, r =>
            {
                var local = new List<Tier3Candidate>();
                CollectTier3LargeOld(r, local, rootBoundary, 3, 0);
                foreach (var c in local) largeOldBag.Add(c);
            });
            found.AddRange(largeOldBag);
            CollectDevCacheCandidates(log, found);   // 开发/包管理器缓存并入第三档候选（≥50MB，逐项确认）
            // 去重（按路径），按体积降序，最终保留前 60 项（与原版全局上限语义一致，避免对话框过长）。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            found = found.Where(c => seen.Add(c.Path)).OrderByDescending(c => c.SizeMB).Take(60).ToList();
            if (found.Count == 0) log("  [结果] 未发现明显可删的旧资产（或均已较新）");
            else
            {
                log("  [结果] 共发现 " + found.Count + " 项候选，请逐项确认后再删除：");
                foreach (var c in found)
                    log("    · " + c.Path + "  —  " + FmtSizeMB(c.SizeMB) + "，" +
                        (c.DaysUnused < 0 ? "签名规则命中" : "已 " + c.DaysUnused + " 天未使用") +
                        (string.IsNullOrEmpty(c.Description) ? "" : "（" + c.Description + "）"));
            }
        }

        internal static void DeleteTier3(List<Tier3Candidate> items, Action<string> log)
        {
            log("=== 第三档·删除（仅删除你已逐项确认的项）===");
            int n = 0;
            foreach (var it in items)
            {
                try { CleanPath("第三档·" + Path.GetFileName(it.Path), it.Path, log); n++; }
                catch (Exception ex) { log("  [!] " + it.Path + " 删除失败: " + ex.Message); }
            }
            log("\r\n[OK] 第三档已处理 " + n + " 项");
        }

        // ---- 清理编排 ----
        public static void RunScan(Action<string> log)
        {
            log("=== 统计预览（不删除，仅查看）===");
            double total = 0;

            // 辅助：把同组条目并行算大小，再按原顺序打印并累加，避免多线程打日志顺序混乱。
            double ScanGroup(string title, List<SizeEntry> entries)
            {
                log(title);
                SizeOfBatch(entries);
                double sum = 0;
                foreach (var e in entries)
                {
                    if (e.MB < 0) log(e.Name + " : 不存在");
                    else { log(e.Name + " : 约 " + FmtSizeMB(e.MB)); sum += e.MB; }
                }
                return sum;
            }

            total += ScanGroup("--- 临时文件 / 缓存 ---", new List<SizeEntry> {
                new SizeEntry { Name = "系统 Temp", Path = @"%SystemRoot%\Temp" },
                new SizeEntry { Name = "用户 Temp", Path = @"%TEMP%" },
                new SizeEntry { Name = "Win更新缓存", Path = @"%SystemRoot%\SoftwareDistribution\Download" },
                new SizeEntry { Name = "WinSxS Temp", Path = @"%SystemRoot%\WinSxS\Temp" },
                new SizeEntry { Name = "缩略图/图标缓存", Path = @"%LOCALAPPDATA%\Microsoft\Windows\Explorer" },
                new SizeEntry { Name = "字体缓存", Path = @"%SystemRoot%\ServiceProfiles\LocalService\AppData\Local\FontCache" },
            });

            total += ScanGroup("--- 日志 / 错误报告 ---", new List<SizeEntry> {
                new SizeEntry { Name = "WER 错误报告", Path = @"%ProgramData%\Microsoft\Windows\WER" },
                new SizeEntry { Name = "诊断数据", Path = @"%ProgramData%\Microsoft\Diagnosis" },
                new SizeEntry { Name = "Windows Update 日志", Path = @"%SystemRoot%\Logs\WindowsUpdate" },
                new SizeEntry { Name = "CBS 持久日志", Path = @"%SystemRoot%\Logs\CBS\Persist" },
                new SizeEntry { Name = "Defender扫描记录", Path = @"%ProgramData%\Microsoft\Windows Defender\Support" },
            });

            total += ScanGroup("--- 浏览器 ---", new List<SizeEntry> {
                new SizeEntry { Name = "Chrome 缓存", Path = @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache" },
                new SizeEntry { Name = "Edge 缓存", Path = @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache" },
            });

            total += ScanGroup("--- 用户开发/包缓存（第三档·逐项确认后清） ---", new List<SizeEntry> {
                new SizeEntry { Name = "npm 缓存", Path = @"%LOCALAPPDATA%\npm-cache" },
                new SizeEntry { Name = "pnpm 缓存", Path = @"%LOCALAPPDATA%\pnpm-cache" },
                new SizeEntry { Name = "NuGet v3 缓存", Path = @"%LOCALAPPDATA%\NuGet\v3-cache" },
                new SizeEntry { Name = "NuGet 包全局缓存", Path = @"%USERPROFILE%\.nuget\packages" },
                new SizeEntry { Name = "pip 缓存", Path = @"%LOCALAPPDATA%\pip\Cache" },
                new SizeEntry { Name = "uv 缓存", Path = @"%LOCALAPPDATA%\uv\cache" },
                new SizeEntry { Name = "Yarn 缓存", Path = @"%LOCALAPPDATA%\Yarn\Cache" },
                new SizeEntry { Name = "cargo registry 缓存", Path = @"%USERPROFILE%\.cargo\registry\cache" },
            });

            log("--- 全盘额外开发缓存（第一档·避免遗漏） ---");
            total += ScanWholeDriveGroup(false, "全盘缓存·", log);

            total += ScanGroup("--- 系统深度 ---", new List<SizeEntry> {
                new SizeEntry { Name = "Delivery Optimization", Path = @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization" },
                new SizeEntry { Name = "Windows 搜索索引", Path = @"%ProgramData%\Microsoft\Search\Indexer" },
                new SizeEntry { Name = "NVIDIA OTA", Path = @"%PROGRAMDATA%\NVIDIA Corporation\OTA" },
                new SizeEntry { Name = "D3D着色器缓存", Path = @"%LOCALAPPDATA%\D3DSCache" },
                new SizeEntry { Name = "RDP连接缓存", Path = @"%LOCALAPPDATA%\Microsoft\Terminal Server Client\Cache" },
                new SizeEntry { Name = "用户崩溃转储", Path = @"%LOCALAPPDATA%\CrashDumps" },
                new SizeEntry { Name = "活动历史", Path = @"%LOCALAPPDATA%\ConnectedDevicesPlatform" },
                new SizeEntry { Name = "BranchCache", Path = @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\PeerDist" },
                new SizeEntry { Name = "系统程序集缓存", Path = @"%SystemRoot%\assembly" },
            });

            total += ScanGroup("--- 更新残留（第二档·基本安全） ---", new List<SizeEntry> {
                new SizeEntry { Name = "ClickOnce 安装缓存", Path = @"%LOCALAPPDATA%\Downloaded Installations" },
                new SizeEntry { Name = "Delivery Optimization 缓存", Path = @"%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache" },
                new SizeEntry { Name = "NVIDIA 下载器缓存", Path = @"%PROGRAMDATA%\NVIDIA Corporation\Downloader" },
            });

            log("--- 全盘额外更新残留（第二档·避免遗漏） ---");
            total += ScanWholeDriveGroup(true, "全盘更新残留·", log);

            total += ScanGroup("--- 大文件（谨慎，仅[大空间回收]会动） ---", new List<SizeEntry> {
                new SizeEntry { Name = "休眠文件 hiberfil.sys", Path = @"%SystemRoot%\System32\hiberfil.sys" },
                new SizeEntry { Name = "内存转储 MEMORY.DMP", Path = @"%SystemRoot%\MEMORY.DMP" },
            });

            log("=== 可清理文件大小总计：约 " + FmtSizeMB(total) + " ===");
            log("（如需清理上述大文件，请勾选「高级/大空间」中的对应项）");
        }

        // ---- 大空间回收（谨慎操作，单独成组） ----
        internal static void BigSpaceHiberfilOff(Action<string> log)
        {
            log("关闭休眠并删除 hiberfil.sys...");
            int hib = Exec.RunCmd(new[] { "powercfg", "/hibernate", "off" }, log);
            if (hib == 0)
                log("  [OK] 已关闭休眠（可释放与内存等量的磁盘空间）");
            else
                log("  [FAIL] 关闭休眠失败（退出码 " + hib + "，需要管理员权限，或休眠已关闭）");
        }

        internal static void BigSpaceMemoryDmp(Action<string> log)
        {
            CleanPath("内存转储 MEMORY.DMP", @"%SystemRoot%\MEMORY.DMP", log);
        }

        internal static void BigSpaceWindowsOld(Action<string> log)
        {
            string path = Exec.ExpandEnv(@"%SystemDrive%\Windows.old");
            if (!Directory.Exists(path))
            {
                log("Windows.old 备份 : 不存在");
                return;
            }
            log("Windows.old 备份（系统保护目录，先接管所有权再删除）...");
            // Windows.old 通常由 TrustedInstaller 拥有，普通管理员直接删会失败；
            // 先 takeown 夺取所有权（/A 归 Administrators 组），再 icacls 赋完全控制，最后才删除
            Exec.RunCmd(new[] { "takeown", "/F", path, "/R", "/D", "Y", "/A" }, log);
            Exec.RunCmd(new[] { "icacls", path, "/grant", "administrators:F", "/T" }, log);
            CleanPath("Windows.old 备份", @"%SystemDrive%\Windows.old", log);
        }
    }
}
