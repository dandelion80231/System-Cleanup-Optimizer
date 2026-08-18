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
        private static void LogIgnored(Exception ex) => System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + ex.Message);

        internal static void CleanDir(string name, string path, Action<string> log)
        {
            path = Exec.ExpandEnv(path);
            log(name);
            if (Directory.Exists(path))
                log(TryCleanDir(path) ? "  [OK]" : "  [SKIP] 部分残留（可能被占用，建议关闭相关程序后重试）");
            else log("  [SKIP] 路径不存在");
        }

        // 尝试清空并删除目录（保留目录本身：删内容后重建空目录）。
        // 返回 true 表示目录已不存在或已清空（成功）；false 表示仍有残留（被占用/权限不足）。
        // PowerShell 兜底不写日志（聚合调用方自行汇总），避免逐目录刷屏。
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
                        try { Directory.Delete(dir, false); } catch { }
                    }
                    try { Directory.Delete(path, true); Directory.CreateDirectory(path); } catch { }
                }
                catch { }
                // 兜底：用 PowerShell 静默再清一次（-EA 0 抑制被占用错误）
                Exec.RunPowerShell("Get-ChildItem -Path " + Exec.QuotePS(path + "\\*") + " -Recurse -Force -EA 0 | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -Recurse -EA 0 }", _ => { });
                // 兜底后再校验：目录已清空才算成功，否则如实返回 false（调用方据此报「部分残留」）
                return !(Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length > 0);
            }
        }

        internal static void CleanPath(string name, string path, Action<string> log)
        {
            path = Exec.ExpandEnv(path);
            log(name);
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
                    log("  [OK]");
                }
                catch (Exception caughtEx) { LogIgnored(caughtEx);// 原生删除失败（权限/只读/占用），用 PowerShell 兜底。
                    // 「文件被另一进程使用」属预期（浏览器/程序运行中），降级为安静提示，不刷 [PS-ERR] 噪声；其余错误仍如实暴露，便于排查真实权限/路径问题。
                    var (ecPS, soPS, sePS) = Exec.RunPowerShellGetFull("Remove-Item -LiteralPath " + Exec.QuotePS(path) + " -Recurse -Force", log);
                    bool inUse = !string.IsNullOrWhiteSpace(sePS)
                        && (sePS.Contains("正由另一进程使用") || sePS.Contains("being used by another process") || sePS.Contains("The process cannot access"));
                    // 兜底后再校验：真正删掉了才算成功，否则如实上报（不再谎报）
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        if (inUse) log("  [SKIP] 部分文件被占用（建议关闭相关程序后重试）");
                        else if (!string.IsNullOrWhiteSpace(sePS)) log("  [PS-ERR] " + sePS.Trim());
                        else log("  [SKIP] 部分残留（可能被占用，建议关闭相关程序后重试）");
                    }
                    else
                        log("  [OK] (PS 兜底)");
                }
            }
            else log("  [SKIP] 不存在");
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
                        try { local += new FileInfo(f).Length; } catch { }
                        return local;
                    },
                    local => Interlocked.Add(ref total, local));
            }
            catch { }
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
            log(name + " : 约 " + mb.ToString("F2") + " MB");
            return mb;
        }

        internal static void BroCookies(string baseDir, string name, Action<string> log)
        {
            baseDir = Exec.ExpandEnv(baseDir);
            log(name + " Cookies");
            try
            {
                if (Directory.Exists(baseDir))
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
                                try { if (File.Exists(fp)) File.Delete(fp); } catch (Exception caughtEx) { LogIgnored(caughtEx);}
                            }
                            // 删除剩余的 Cookies-* 文件
                            foreach (var cf in Directory.EnumerateFiles(nf, "Cookies-*", SearchOption.TopDirectoryOnly))
                            {
                                try { File.Delete(cf); } catch (Exception caughtEx) { LogIgnored(caughtEx);}
                            }
                        }
                    }
                }
                log("  [OK]");
            }
            catch (Exception caughtEx) { LogIgnored(caughtEx);string script = "Get-ChildItem " + Exec.QuotePS(baseDir) + " -Directory -EA 0 | ForEach-Object { " +
                    "$nf=Join-Path $_.FullName 'Network'; if(Test-Path $nf){ " +
                    "@('Cookies','Cookies-journal') | ForEach-Object { $f=Join-Path $nf $_; if(Test-Path $f){ Remove-Item $f -Force -EA 0 } }; " +
                    "Get-ChildItem $nf -Filter 'Cookies-*' -Force -EA 0 | Remove-Item -Force -EA 0 } }";
                Exec.RunPowerShell(script, log);
                log("  [OK] (PS fallback)");
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
            log(".NET 程序集缓存(Native Image)");
            bool done = false;
            foreach (var ng in new[] {
                @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\ngen.exe",
                @"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\ngen.exe" })
            {
                string p = Exec.ExpandEnv(ng);
                if (File.Exists(p)) { Exec.RunCmd(new[] { p, "executequeueditems" }, log); log("  [OK]"); done = true; break; }
            }
            if (!done) log("  [SKIP] ngen not found");
        }

        internal static void Defender(Action<string> log)
        {
            CleanDir("Defender Support", @"%ProgramData%\Microsoft\Windows Defender\Support", log);
            CleanDir("Defender 扫描历史", @"%ProgramData%\Microsoft\Windows Defender\Scans\History\Resource", log);
        }

        internal static void IconCache(Action<string> log)
        {
            log("图标缓存");
            int r = Exec.RunCmd(new[] { "cmd", "/c", "del", "/f", "/q", Exec.ExpandEnv(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache*.db") }, log);
            log(r == 0 ? "  [OK]" : "  [FAIL] 图标缓存清理失败（退出码 " + r + "，可能未提权）");
        }

        internal static void FontCache(Action<string> log)
        {
            log("字体缓存");
            Exec.RunCmd(new[] { "net", "stop", "FontCache", "/y" }, log);
            // 清空 FontCache 目录（CleanDir 已改为原生 C#，不启 PowerShell）
            CleanDir("FontCache", @"%SystemRoot%\ServiceProfiles\LocalService\AppData\Local\FontCache", log);
            string fnt = Exec.ExpandEnv(@"%SystemRoot%\System32\FNTCACHE.DAT");
            if (File.Exists(fnt)) { try { File.Delete(fnt); } catch (Exception caughtEx) { LogIgnored(caughtEx);} }
            Exec.RunCmd(new[] { "net", "start", "FontCache" }, log);
            log("  [OK]");
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
            log(failed == 0 ? "  [OK]" : "  [FAIL] " + failed + " 个日志通道清空失败（可能未提权）");
        }

        internal static void CrashDumps(Action<string> log)
        {
            CleanDir("用户崩溃转储", @"%LOCALAPPDATA%\CrashDumps", log);
        }

        internal static void Recent(Action<string> log)
        {
            log("最近使用 / 跳转列表");
            int failed = 0;
            foreach (var p in new[] {
                @"%APPDATA%\Microsoft\Windows\Recent\*.*",
                @"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations\*.*",
                @"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.*" })
            {
                if (Exec.RunCmd(new[] { "cmd", "/c", "del", "/f", "/q", Exec.ExpandEnv(p) }, log) != 0) failed++;
            }
            log(failed == 0 ? "  [OK]" : "  [FAIL] " + failed + " 项最近使用清理失败（可能未提权）");
        }

        internal static void WuLogs(Action<string> log) { CleanDir("Windows Update 日志", @"%SystemRoot%\Logs\WindowsUpdate", log); }
        internal static void CbsPersist(Action<string> log) { CleanDir("CBS 持久日志", @"%SystemRoot%\Logs\CBS\Persist", log); }

        // Whesvc（Windows 健康状况和优化体验）本地性能诊断追踪目录。
        // 仅本机卡顿时生成 ETL 日志，可安全删除、服务重新启用时会再生；服务运行时文件被占用，CleanDir 会自动跳过被锁文件。
        internal static void WhesvcDiag(Action<string> log) { CleanDir("Whesvc 诊断日志", @"%SystemRoot%\Temp\DiagOutputDir\Whesvc", log); }

        internal static void Notifications(Action<string> log)
        {
            log("通知数据库");
            int r = Exec.RunCmd(new[] { "cmd", "/c", "del", "/f", "/q", Exec.ExpandEnv(@"%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase*.db") }, log);
            log(r == 0 ? "  [OK]" : "  [FAIL] 通知数据库清理失败（退出码 " + r + "，可能未提权）");
        }

        internal static void Spotlight(Action<string> log)
        {
            log("Windows Spotlight 壁纸缓存");
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
            log("  [OK]");
        }

        internal static void Activity(Action<string> log) { CleanDir("活动历史", @"%LOCALAPPDATA%\ConnectedDevicesPlatform", log); }
        internal static void BranchCache(Action<string> log) { CleanDir("BranchCache", @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\PeerDist", log); }

        internal static void Recycle(Action<string> log)
        {
            log("回收站");
            Exec.RunPowerShell("Clear-RecycleBin -Force -EA 0", log);
            log("  [OK]");
        }

        internal static void Cookies(Action<string> log)
        {
            log("浏览器 Cookies（会登出网站登录态）...");
            // 各浏览器 Cookies 互不相关 → 并行清理（含 Firefox 的 PowerShell 块）。
            var jobs = new Action[]
            {
                () => BroCookies(@"%LOCALAPPDATA%\Google\Chrome\User Data", "Chrome", log),
                () => BroCookies(@"%LOCALAPPDATA%\Microsoft\Edge\User Data", "Edge", log),
                () => BroCookies(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", "Brave", log),
                () => BroCookies(@"%LOCALAPPDATA%\360Chrome\Chrome\User Data", "360安全浏览器", log),
                () => {
                    try
                    {
                        string fb = Exec.ExpandEnv(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
                        if (Directory.Exists(fb))
                        {
                            string script = "Get-ChildItem " + Exec.QuotePS(fb) + " -Directory -EA 0 | ForEach-Object { " +
                                "@('cookies.sqlite','cookies.sqlite-shm','cookies.sqlite-wal') | " +
                                "ForEach-Object { $f=Join-Path $_.FullName $_; if(Test-Path $f){ Remove-Item $f -Force -EA 0 } } }";
                            Exec.RunPowerShell(script, log);
                        }
                    }
                    catch (Exception caughtEx) { LogIgnored(caughtEx); }
                }
            };
            Parallel.Invoke(InnerPar, jobs);
            log("  [OK]");
        }

        // ---- 第一档：绝对安全（纯缓存/可重建，删了无任何副作用） ----
        internal static void UserCacheTier1(Action<string> log)
        {
            log("用户缓存·开发/包管理器（第一档·绝对安全，删了可重建）");
            // 包管理器缓存：删了只是下次 install 时重新下载，对任何程序无副作用 → 并行清理（受 InnerPar 限流）
            var pkgCaches = new (string name, string path)[]
            {
                ("npm 缓存", @"%LOCALAPPDATA%\npm-cache"),
                ("npm 缓存(Roaming)", @"%APPDATA%\npm-cache"),
                ("pnpm 缓存", @"%LOCALAPPDATA%\pnpm-cache"),
                ("NuGet v3 缓存", @"%LOCALAPPDATA%\NuGet\v3-cache"),
                ("NuGet http 缓存", @"%LOCALAPPDATA%\NuGet\http-cache"),
                ("NuGet 包全局缓存", @"%USERPROFILE%\.nuget\packages"),
                ("pip 缓存", @"%LOCALAPPDATA%\pip\Cache"),
                ("Yarn 缓存", @"%LOCALAPPDATA%\Yarn\Cache"),
                ("cargo registry 缓存", @"%USERPROFILE%\.cargo\registry\cache"),
                ("cargo registry 源码", @"%USERPROFILE%\.cargo\registry\src"),
            };
            Parallel.ForEach(pkgCaches, InnerPar, item => CleanDir(item.name, item.path, log));
            log("  [全盘筛查] 在 C 盘用户/程序目录查找额外同类缓存（避免遗漏）...");
            CleanWholeDriveCaches(false, "全盘缓存·", log);
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
        private static readonly HashSet<string> Tier1DirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "npm-cache", "pnpm-cache", "yarn-cache", "__pycache__", "v3-cache", "http-cache"
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

        private static bool IsProtectedScanPath(string full)
        {
            string f = full.Replace('/', '\\').ToLowerInvariant();
            return f.StartsWith(@"c:\windows") || f.Contains(@"\windows\") || f.Contains(@"\system32\") ||
                   f.Contains(@"\winsxs\") || f.Contains(@"\windowsapps\") || f.Contains(@"$recycle.bin") ||
                   f.Contains(@"\recovery\") || f.Contains(@"\boot\");
        }

        private static void CollectDirsByName(string root, HashSet<string> names, bool tier2, List<string> outPaths, HashSet<string> exclude, int maxDepth, int depth)
        {
            if (depth > maxDepth || !Directory.Exists(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); }
            catch { return; }
            foreach (var d in subs)
            {
                if (IsProtectedScanPath(d)) continue;
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
                CollectDirsByName(d, names, tier2, outPaths, exclude, maxDepth, depth + 1);
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
            var bag = new ConcurrentBag<string>();
            Parallel.ForEach(ScanRoots(), root =>
            {
                var local = new List<string>();
                CollectDirsByName(root, names, tier2, local, exclude, 5, 0);
                foreach (var p in local) bag.Add(p);
            });
            return bag.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // 全盘筛查批量清理 + 聚合日志：按目录名分组，每组只打一行汇总（清理 N 处 / M 处残留），
        // 避免开发机上百个 node_modules/__pycache__ 每个刷一行导致日志爆炸。
        internal static void CleanWholeDriveCaches(bool tier2, string prefix, Action<string> log)
        {
            var paths = FindWholeDriveCaches(tier2, log);
            if (paths.Count == 0) { log("  [全盘筛查] 未额外发现" + (tier2 ? "更新残留" : "缓存")); return; }
            foreach (var g in paths.GroupBy(p => Path.GetFileName(p)).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                int ok = 0, skip = 0;
                foreach (var p in g) { if (TryCleanDir(p)) ok++; else skip++; }
                string line = "  " + prefix + g.Key + ": 清理 " + ok + " 处";
                if (skip > 0) line += "，" + skip + " 处残留（被占用）";
                line += " [OK]";
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
        private static readonly string[] Tier3SigPatterns = { @"\.old$", @"_old$", "backup", "archive", "deprecated", "old_version", "version_old" };
        private static readonly Dictionary<string, string> Tier3DescMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "webcast_mate", "抖音直播/虚拟偶像工具数据；可能包含模型或录像，不使用时可删" },
            { "jianyingpro", "剪映项目/缓存；项目文件可能有用，缓存部分可清" },
            { ".lmstudio", "LM Studio 下载的模型；删了需要重新下载" },
            { ".qclaw-backups", "QClaw 历史备份；通常只保留最新即可" },
            { "obsplus-virtualcam", "OBS Plus 虚拟摄像头缓存/临时文件" },
            { "comfyui", "ComfyUI 模型/节点/工作流缓存" },
            { "backup", "软件自动生成的备份目录；通常只保留最新即可" },
            { "archive", "归档/历史数据；确认不再使用后可删" },
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

        private static void CollectTier3Sig(string root, List<string> outPaths, int maxDepth, int depth)
        {
            if (depth > maxDepth || !Directory.Exists(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); } catch { return; }
            foreach (var d in subs)
            {
                if (IsProtectedScanPath(d)) continue;
                string nm = Path.GetFileName(d);
                bool hit = Tier3SigNames.Contains(nm);
                if (!hit)
                {
                    foreach (var pat in Tier3SigPatterns)
                        if (Regex.IsMatch(nm, pat, RegexOptions.IgnoreCase)) { hit = true; break; }
                }
                if (hit) { outPaths.Add(d); continue; }
                CollectTier3Sig(d, outPaths, maxDepth, depth + 1);
            }
        }

        private static void CollectTier3LargeOld(string root, List<Tier3Candidate> found, int maxDepth, int depth)
        {
            if (depth > maxDepth || found.Count >= 60 || !Directory.Exists(root)) return;
            if (InKeepPath(root)) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(root); } catch { return; }
            foreach (var d in subs)
            {
                if (IsProtectedScanPath(d) || InKeepPath(d)) continue;
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
                CollectTier3LargeOld(d, found, maxDepth, depth + 1);
            }
        }

        internal static void ScanTier3(Action<string> log, out List<Tier3Candidate> found)
        {
            found = new List<Tier3Candidate>();
            log("=== 第三档·旧资产筛查（先扫描，删除前逐项确认）===");
            log("  规则：仅列出【≥" + Tier3MBThreshold + " MB 且 ≥" + Tier3DaysThreshold + " 天未使用】或【已知停用工具/备份旧目录】，且不含系统关键数据；删除需你逐项勾选确认。");
            var roots = ScanRoots();
            var sigBag = new ConcurrentBag<string>();
            Parallel.ForEach(roots, r =>
            {
                var local = new List<string>();
                CollectTier3Sig(r, local, 4, 0);
                foreach (var p in local) sigBag.Add(p);
            });
            foreach (var p in sigBag.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (InKeepPath(p) || !Directory.Exists(p)) continue;
                double mb = DirSizeCapped(p, 300000) / 1024.0 / 1024.0;
                if (mb < Tier3MBThreshold) continue;   // 签名命中也按大小筛选，避免 0MB 小备份刷屏
                DateTime lw = Directory.GetLastWriteTime(p), la = Directory.GetLastAccessTime(p);
                DateTime lastAct = lw > la ? lw : la;
                int days = (int)(DateTime.Now - lastAct).TotalDays;
                string desc = GetTier3Desc(p);
                found.Add(new Tier3Candidate { Path = p, SizeMB = Math.Round(mb, 1), LastActivity = lastAct, DaysUnused = days, Description = desc });
                log("  发现旧资产(签名): " + p + "  —  约 " + mb.ToString("F1") + " MB，已 " + days + " 天未使用");
            }
            // 大且旧扫描：每个 root 独立收集，最后合并去重/排序，避免共享 found 列表的锁竞争。
            var largeOldBag = new ConcurrentBag<Tier3Candidate>();
            Parallel.ForEach(roots, r =>
            {
                var local = new List<Tier3Candidate>();
                CollectTier3LargeOld(r, local, 3, 0);
                foreach (var c in local) largeOldBag.Add(c);
            });
            found.AddRange(largeOldBag);
            // 去重（按路径），按体积降序，最终保留前 60 项（与原版全局上限语义一致，避免对话框过长）。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            found = found.Where(c => seen.Add(c.Path)).OrderByDescending(c => c.SizeMB).Take(60).ToList();
            if (found.Count == 0) log("  [结果] 未发现明显可删的旧资产（或均已较新）");
            else
            {
                log("  [结果] 共发现 " + found.Count + " 项候选，请逐项确认后再删除：");
                foreach (var c in found) log("    · " + c.Path + "  —  " + c.SizeMB + " MB，已 " + c.DaysUnused + " 天未使用");
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
                    else { log(e.Name + " : 约 " + e.MB.ToString("F2") + " MB"); sum += e.MB; }
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

            total += ScanGroup("--- 用户开发/包缓存（第一档·绝对安全） ---", new List<SizeEntry> {
                new SizeEntry { Name = "npm 缓存", Path = @"%LOCALAPPDATA%\npm-cache" },
                new SizeEntry { Name = "pnpm 缓存", Path = @"%LOCALAPPDATA%\pnpm-cache" },
                new SizeEntry { Name = "NuGet v3 缓存", Path = @"%LOCALAPPDATA%\NuGet\v3-cache" },
                new SizeEntry { Name = "NuGet 包全局缓存", Path = @"%USERPROFILE%\.nuget\packages" },
                new SizeEntry { Name = "pip 缓存", Path = @"%LOCALAPPDATA%\pip\Cache" },
                new SizeEntry { Name = "Yarn 缓存", Path = @"%LOCALAPPDATA%\Yarn\Cache" },
                new SizeEntry { Name = "cargo registry 缓存", Path = @"%USERPROFILE%\.cargo\registry\cache" },
            });

            log("--- 全盘额外开发缓存（第一档·避免遗漏） ---");
            foreach (var p in FindWholeDriveCaches(false, log))
                total += SizeOf("全盘缓存·" + Path.GetFileName(p), p, log);

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
            foreach (var p in FindWholeDriveCaches(true, log))
                total += SizeOf("全盘更新残留·" + Path.GetFileName(p), p, log);

            total += ScanGroup("--- 大文件（谨慎，仅[大空间回收]会动） ---", new List<SizeEntry> {
                new SizeEntry { Name = "休眠文件 hiberfil.sys", Path = @"%SystemRoot%\System32\hiberfil.sys" },
                new SizeEntry { Name = "内存转储 MEMORY.DMP", Path = @"%SystemRoot%\MEMORY.DMP" },
            });

            log("=== 可清理文件大小总计：约 " + total.ToString("F2") + " MB ===");
            log("（如需清理上述大文件，请勾选「高级/大空间」中的对应项）");
        }

        // ---- 大空间回收（谨慎操作，单独成组） ----
        internal static void BigSpaceHiberfilOff(Action<string> log)
        {
            log("关闭休眠并删除 hiberfil.sys...");
            Exec.RunCmd(new[] { "powercfg", "/hibernate", "off" }, log);
            log("  [OK] 已关闭休眠（可释放与内存等量的磁盘空间）");
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
