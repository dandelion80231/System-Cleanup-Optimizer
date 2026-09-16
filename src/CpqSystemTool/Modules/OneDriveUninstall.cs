using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 独立 OneDrive 云同步客户端（OneDriveSetup.exe 安装，非 C2R 套件内组件）的干净卸载。
    /// 参考「Geek Uninstaller」强力卸载方式：先杀进程 → 官方卸载器 /uninstall /allusers →
    /// 清扫可重装的程序目录与注册表项。刻意【不删】%LOCALAPPDATA%\OneDrive\（用户同步数据）。
    /// 说明：C2R 套件里的「Groove/OneDrive for Business」是另一回事（ODT 管），不在本类范围。
    /// </summary>
    internal static class OneDriveUninstall
    {
        /// <summary>
        /// 检测本机是否安装了独立 OneDrive 客户端（64 位装在 C:\Program Files\Microsoft OneDrive，
        /// 32 位 / OneDrive for Business 装在 C:\Program Files (x86)\Microsoft OneDrive；
        /// 两处的任一版本目录里有 OneDriveSetup.exe 即视为已安装）。
        /// </summary>
        public static bool IsStandaloneInstalled()
        {
            foreach (string root in OneDriveProgramRoots())
            {
                try
                {
                    if (Directory.GetDirectories(root).Any(d =>
                        File.Exists(Path.Combine(d, "OneDriveSetup.exe"))))
                        return true;
                }
                catch { /* 枚举失败看下一个根 */ }
            }
            return false;
        }

        /// <summary>OneDrive 程序目录根（只返回实际存在的）：64 位 PF 与 32 位 PF(x86) 两个候选。
        /// OneDrive for Business（MDOB）随系统装 32 位版，只查 64 位 PF 会漏检（历史 bug，本机即此场景）。</summary>
        private static List<string> OneDriveProgramRoots()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var roots = new List<string>();
            foreach (string r in new[] { Path.Combine(pf, "Microsoft OneDrive"), Path.Combine(pfx86, "Microsoft OneDrive") })
                if (!string.IsNullOrEmpty(r) && Directory.Exists(r)) roots.Add(r);
            return roots;
        }

        /// <summary>
        /// 干净卸载独立 OneDrive 客户端。步骤（对齐 Geek 强力卸载，但保留用户数据）：
        ///  1) 杀掉 onedrive.exe 进程（避免文件占用导致卸载/删除失败）
        ///  2) 官方卸载器 OneDriveSetup.exe /uninstall /allusers（扫 64 位 PF 与 32 位 PF(x86) 两个根）
        ///  3) 清扫 4 个可重装程序目录（PF / PF(x86) / ProgramData / LOCALAPPDATA\Microsoft\OneDrive）
        ///  4) 删除注册表残留（3 个 Uninstall 卸载项 HKCU/HKLM/32位视图 + 2 组 Run 自启值各 HKCU/HKLM；
        ///     注册表「版本信息键」纯展示数据，刻意不删，装新客户端时自动重写）
        /// 刻意不删 %LOCALAPPDATA%\OneDrive\（用户 OneDrive 同步目录，属个人数据）。
        /// </summary>
        public static void Uninstall(Action<string> log)
        {
            // 1) 杀掉 onedrive 进程（taskkill /f /im；进程不存在时报错被忽略，不影响主流程）
            log("  [OneDrive] 结束 OneDrive 客户端进程...");
            Exec.RunCmd(new[]
            {
                Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                "/f", "/im", "onedrive.exe"
            }, log);

            // 2) 官方卸载器：扫 64 位 PF 与 32 位 PF(x86) 两个根的各版本目录找 OneDriveSetup.exe
            string setup = null;
            foreach (string root in OneDriveProgramRoots())
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(root))
                    {
                        string cand = Path.Combine(d, "OneDriveSetup.exe");
                        if (File.Exists(cand)) { setup = cand; break; }
                    }
                }
                catch { /* 枚举失败看下一个根 */ }
                if (setup != null) break;
            }
            if (setup != null)
            {
                log("  [OneDrive] 执行官方卸载器: " + setup + " /uninstall /allusers");
                Exec.RunCmd(new[] { setup, "/uninstall", "/allusers", "/quiet" }, log);
            }
            else
            {
                log("  [OneDrive] 未找到 OneDriveSetup.exe，跳过官方卸载器（可能已手动删过）");
            }

            // 3) 清扫 4 个可重装程序目录（PF 与 PF(x86) 两个 OneDrive 根 + ProgramData + LOCALAPPDATA，
            //    用 PowerShell 递归强删，-EA 0 忽略单个文件占用错误；不存在的目录自动跳过）
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] dirs =
            {
                Path.Combine(pf, "Microsoft OneDrive"),
                Path.Combine(pfx86, "Microsoft OneDrive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft OneDrive"),
                Path.Combine(localAppData, "Microsoft", "OneDrive"),
            };
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                log("  [OneDrive] 删除目录: " + d);
                Exec.RunPowerShell("Remove-Item -Path " + Exec.QuotePS(d) + " -Recurse -Force -EA 0", log);
            }

            // 3.5) 复查：Program Files 下的 OneDrive 若因 shell 扩展被 explorer 锁定而删不掉，
            //      记录为「待补删」标记，下次程序启动（锁已释放）时由 TryPendingCleanup 重试。
            var lockedLeftovers = new List<string>();
            foreach (var d in dirs)
            {
                try { if (Directory.Exists(d)) lockedLeftovers.Add(d); } catch { }
            }
            if (lockedLeftovers.Count > 0)
            {
                WritePendingCleanup(lockedLeftovers, log);
                log("  [OneDrive] 以下目录被占用暂未删净，重启/下次启动时自动补删: " + string.Join("; ", lockedLeftovers));
            }

            // 4) 删除注册表残留（Uninstall 卸载项 + Run 自启值；版本信息键纯展示不删）
            CleanRegistry(log);

            log("  [OneDrive] 独立 OneDrive 客户端卸载完成（用户数据目录 %LOCALAPPDATA%\\OneDrive 已保留）");
        }

        // ==================== 重启自动补删 ====================

        /// <summary>待补删标记文件名（与 exe 同目录的 Config 子目录，便携；不可写时回退 %LOCALAPPDATA%\CpqSystemTool）。</summary>
        private static string PendingFile()
        {
            // 优先：exe 同目录下的 Config\（复用 AppPaths.ConfigDir，便携单 exe 语义）
            try
            {
                if (AppPaths.EnsureConfigDir())
                    return Path.Combine(AppPaths.ConfigDir, "onedrive_pending_cleanup.txt");
            }
            catch { /* 不可写，回退 */ }
            // 回退：%LOCALAPPDATA%\CpqSystemTool\
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CpqSystemTool");
            try
            {
                Directory.CreateDirectory(fallback);
                return Path.Combine(fallback, "onedrive_pending_cleanup.txt");
            }
            catch { return null; /* 完全无处可写，标记放弃 */ }
        }

        /// <summary>把删不净的目录写进待补删标记（文本每行一个绝对路径，去重）。</summary>
        private static void WritePendingCleanup(List<string> dirs, Action<string> log)
        {
            string file = PendingFile();
            if (file == null)
            {
                log("  [OneDrive] 无法写入待补删标记（Config 目录与 %LOCALAPPDATA% 均不可写），需手动删除上述目录");
                return;
            }
            try
            {
                // 合并已有标记（本文件若已有内容，保留去重），只追加新的目录
                var merged = new List<string>(dirs);
                if (File.Exists(file))
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        string p = line.Trim();
                        if (p.Length > 0 && !merged.Contains(p)) merged.Add(p);
                    }
                }
                File.WriteAllLines(file, merged);
                log("  [OneDrive] 已写入待补删标记: " + file);
            }
            catch (Exception ex)
            {
                log("  [OneDrive] 写入待补删标记失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 程序启动时调用：若存在「待补删」标记，则对其中每个目录再删一遍（此时 shell 扩展锁已释放）；
        /// 删成功的从标记移除，全删净则删除标记文件。返回是否仍有删不净的目录。
        /// </summary>
        public static bool TryPendingCleanup(Action<string> log)
        {
            string file = null;
            try
            {
                // 与 PendingFile 一致的优先级：先 exe 同目录 Config，再 %LOCALAPPDATA%
                string inConfig = Path.Combine(AppPaths.ConfigDir, "onedrive_pending_cleanup.txt");
                string inLocal = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CpqSystemTool", "onedrive_pending_cleanup.txt");
                if (File.Exists(inConfig)) file = inConfig;
                else if (File.Exists(inLocal)) file = inLocal;
            }
            catch { }

            if (file == null || !File.Exists(file)) return false; // 无标记 → 无事可做

            List<string> remaining;
            try
            {
                remaining = File.ReadAllLines(file)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Distinct()
                    .ToList();
            }
            catch { remaining = null; }
            if (remaining == null) return false;

            var stillThere = new List<string>();
            foreach (var d in remaining)
            {
                if (!Directory.Exists(d)) continue;
                if (log != null) log("  [OneDrive] 补删残留目录: " + d);
                // 与卸载阶段一致的强删方式（PowerShell 忽略单文件占用错误）
                Exec.RunPowerShell("Remove-Item -Path " + Exec.QuotePS(d) + " -Recurse -Force -EA 0", log);
                try { if (Directory.Exists(d)) stillThere.Add(d); } catch { }
            }

            // 更新标记：全删净则删除文件；否则把仍残留的路径写回
            try
            {
                if (stillThere.Count == 0)
                {
                    File.Delete(file);
                    if (log != null) log("  [OneDrive] 重启后残留目录已全部补删干净");
                }
                else
                {
                    File.WriteAllLines(file, stillThere);
                    if (log != null) log("  [OneDrive] 仍有 " + stillThere.Count + " 个目录被占用，留待下次启动");
                }
            }
            catch { /* 标记读写失败不影响主流程 */ }

            return stillThere.Count > 0;
        }

        private static void CleanRegistry(Action<string> log)
        {
            // 卸载项（3 个）：HKCU/HKLM/HKLM-WOW6432 的 Uninstall\OneDriveSetup.exe
            string[] regKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
            };

            // 自启 Run 值（对应 Geek 的另外几项 HKCU/HKLM Run 残留）
            string runKeyHkcu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            string runKeyHklm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            string[] runValues = { "OneDriveSetup", "OneDrive" };

            foreach (var rel in regKeys)
            {
                // HKLM 侧
                DeleteKeyTree(Registry.LocalMachine, rel, log);
                // HKCU 侧（首项在 HKCU 同样存在）
                DeleteKeyTree(Registry.CurrentUser, rel, log);
            }

            // 清 Run 自启值（HKCU + HKLM）
            foreach (var val in runValues)
            {
                DeleteValue(Registry.CurrentUser, runKeyHkcu, val, log);
                DeleteValue(Registry.LocalMachine, runKeyHklm, val, log);
            }
        }

        private static void DeleteKeyTree(RegistryKey root, string relativePath, Action<string> log)
        {
            try
            {
                using (var k = root.OpenSubKey(relativePath))
                {
                    if (k == null) return;
                }
                root.DeleteSubKeyTree(relativePath, false);
                log("  [注册表] 已删 " + (root == Registry.LocalMachine ? "HKLM\\" : "HKCU\\") + relativePath);
            }
            catch (Exception ex)
            {
                log("  [!] 注册表键删除失败（可能已不存在/被占用）: " + relativePath + " — " + ex.Message);
            }
        }

        private static void DeleteValue(RegistryKey root, string relativePath, string valueName, Action<string> log)
        {
            try
            {
                using (var k = root.OpenSubKey(relativePath, true))
                {
                    if (k == null) return;
                    if (k.GetValue(valueName) != null) k.DeleteValue(valueName);
                }
            }
            catch (Exception ex)
            {
                // 单个 Run 值删除失败不影响整体，仅记录
                log("  [!] 注册表 Run 值删除失败: " + relativePath + "\\" + valueName + " — " + ex.Message);
            }
        }
    }
}
