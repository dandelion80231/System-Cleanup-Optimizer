using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 「干净卸载」公共基建（D1 去重，原 OneDriveUninstall / TeamsUninstall 各持有一份近乎逐字重复的实现）：
    ///  ① 可重装目录的递归强删 + 被占用目录的「重启自动补删」待补删标记（Config 目录优先，%LOCALAPPDATA% 回退）；
    ///  ② 注册表残留清扫（HKLM/HKCU 双根键树删除、Run 自启值删除）。
    /// 调用方只需传入各自的标记文件名（onedrive_/teams_pending_cleanup.txt）与日志标签（[OneDrive]/[Teams]）。
    /// </summary>
    internal static class RegistryUninstallHelper
    {
        // ==================== 目录清扫与待补删标记 ====================

        /// <summary>递归强删目录（PowerShell Remove-Item -Recurse -Force -EA 0，单个文件占用错误被忽略）；
        /// 不存在的目录自动跳过。删完仍存在的由调用方用 <see cref="LockedLeftovers"/> 复查。</summary>
        public static void RemoveDirs(string[] dirs, string logTag, Action<string> log)
        {
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                log(logTag + " 删除目录: " + d);
                Exec.RunPowerShell("Remove-Item -Path " + Exec.QuotePS(d) + " -Recurse -Force -EA 0", log);
            }
        }

        /// <summary>复查一批目录中仍存在的（删不掉＝被 shell 扩展/后台进程占用），供写待补删标记。</summary>
        public static List<string> LockedLeftovers(IEnumerable<string> dirs)
        {
            var lockedLeftovers = new List<string>();
            foreach (var d in dirs)
            {
                try { if (Directory.Exists(d)) lockedLeftovers.Add(d); } catch (Exception ex) { DebugLog.Ignore(ex); }
            }
            return lockedLeftovers;
        }

        /// <summary>解析待补删标记文件位置：优先数据根 cpq-tool\配置（AppPaths.ConfigDir，exe 同目录语义），
        /// 不可写时回退 %LOCALAPPDATA%\CpqSystemTool\。完全无处可写返回 null（标记放弃）。
        /// markerFile：如 "onedrive_pending_cleanup.txt" / "teams_pending_cleanup.txt"。</summary>
        public static string MarkerFile(string markerFile)
        {
            try
            {
                if (AppPaths.EnsureConfigDir())
                    return Path.Combine(AppPaths.ConfigDir, markerFile);
            }
            catch { /* 不可写，回退 */ }
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CpqSystemTool");
            try
            {
                Directory.CreateDirectory(fallback);
                return Path.Combine(fallback, markerFile);
            }
            catch { return null; /* 完全无处可写，标记放弃 */ }
        }

        /// <summary>把删不净的目录写进待补删标记（文本每行一个绝对路径，与已有内容去重合并）。</summary>
        public static void WriteMarker(string markerFile, List<string> dirs, string logTag, Action<string> log)
        {
            string file = MarkerFile(markerFile);
            if (file == null)
            {
                log(logTag + " 无法写入待补删标记（Config 目录与 %LOCALAPPDATA% 均不可写），需手动删除上述目录");
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
                log(logTag + " 已写入待补删标记: " + file);
            }
            catch (Exception ex)
            {
                log(logTag + " 写入待补删标记失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 程序启动时调用：若存在「待补删」标记，则对其中每个目录再删一遍（此时锁已释放）；
        /// 删成功的从标记移除，全删净则删除标记文件。返回是否仍有删不净的目录。
        /// markerFile：如 "onedrive_pending_cleanup.txt"；log 允许为 null（静默模式）。
        /// </summary>
        public static bool TryCleanupMarker(string markerFile, string logTag, Action<string> log)
        {
            string file = null;
            try
            {
                // 与 MarkerFile 一致的优先级：先数据根 cpq-tool\配置，再 %LOCALAPPDATA%\CpqSystemTool
                string inConfig = Path.Combine(AppPaths.ConfigDir, markerFile);
                string inLocal = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CpqSystemTool", markerFile);
                if (File.Exists(inConfig)) file = inConfig;
                else if (File.Exists(inLocal)) file = inLocal;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }

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
            catch (Exception ex) { DebugLog.Ignore(ex); remaining = null; }
            if (remaining == null) return false;

            // [Q3] 安全校验：marker 位于用户可写目录（ConfigDir / %LOCALAPPDATA%\CpqSystemTool），若被手工写入越权路径，
            // 本工具（管理员运行）会对它 Remove-Item -Recurse -Force。仅允许删除「已知应用残留根」下的子目录；
            // 越权目标（盘符根/系统目录/其它盘）拒绝并保留在 stillThere（不删空 marker）。
            string[] safeRoots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            static bool UnderRoot(string p, string root)
            {
                if (string.IsNullOrEmpty(root)) return false;
                string a = Path.TrimEndingDirectorySeparator(p), b = Path.TrimEndingDirectorySeparator(root);
                return a.Length > b.Length
                    && a.StartsWith(b, System.StringComparison.OrdinalIgnoreCase)
                    && a[b.Length] == Path.DirectorySeparatorChar;
            }
            var stillThere = new List<string>();
            foreach (var d in remaining)
            {
                if (!Directory.Exists(d)) continue;
                string full;
                try { full = Path.GetFullPath(d); }
                catch (Exception ex) { DebugLog.Ignore(ex); if (log != null) log(logTag + " 拒绝非法路径（无法解析全路径）: " + d); stillThere.Add(d); continue; }
                if (!safeRoots.Any(r => UnderRoot(full, r)))
                {
                    if (log != null) log(logTag + " 拒绝越权补删路径（不在已知残留根 " + "Program Files/ProgramData/本地/用户 下）: " + full);
                    stillThere.Add(d);
                    continue;
                }
                if (log != null) log(logTag + " 补删残留目录: " + d);
                // 与卸载阶段一致的强删方式（PowerShell 忽略单文件占用错误）
                Exec.RunPowerShell("Remove-Item -Path " + Exec.QuotePS(d) + " -Recurse -Force -EA 0", log);
                try { if (Directory.Exists(d)) stillThere.Add(d); } catch (Exception ex) { DebugLog.Ignore(ex); }
            }

            // 更新标记：全删净则删除文件；否则把仍残留的路径写回
            try
            {
                if (stillThere.Count == 0)
                {
                    File.Delete(file);
                    if (log != null) log(logTag + " 重启后残留目录已全部补删干净");
                }
                else
                {
                    File.WriteAllLines(file, stillThere);
                    if (log != null) log(logTag + " 仍有 " + stillThere.Count + " 个目录被占用，留待下次启动");
                }
            }
            catch { /* 标记读写失败不影响主流程 */ }

            return stillThere.Count > 0;
        }

        // ==================== 注册表残留清扫 ====================

        /// <summary>删除注册表键树（先探存在性再 DeleteSubKeyTree，避免误报）；HKLM 与 HKCU 两个根都删一遍。
        /// relativePath 为相对根的路径（如 @"SOFTWARE\Microsoft\Teams" 或 Uninstall 卸载项相对路径）。</summary>
        public static void DeleteKeyTreeBothRoots(string relativePath, Action<string> log)
        {
            DeleteKeyTree(Registry.LocalMachine, relativePath, log);
            DeleteKeyTree(Registry.CurrentUser, relativePath, log);
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

        /// <summary>删除 Run 自启值（HKCU + HKLM 逐根独立 try/catch，与原实现逐次调用 DeleteValue 语义一致）。</summary>
        public static void DeleteValueBothRoots(string keyPath, string valueName, Action<string> log)
        {
            DeleteValue(Registry.CurrentUser, keyPath, valueName, log);
            DeleteValue(Registry.LocalMachine, keyPath, valueName, log);
        }

        private static void DeleteValue(RegistryKey root, string keyPath, string valueName, Action<string> log)
        {
            try
            {
                using (var k = root.OpenSubKey(keyPath, true))
                {
                    if (k == null) return;
                    if (k.GetValue(valueName) != null) k.DeleteValue(valueName);
                }
            }
            catch (Exception ex)
            {
                // 单个 Run 值删除失败不影响整体，仅记录
                log("  [!] 注册表 Run 值删除失败: " + keyPath + "\\" + valueName + " — " + ex.Message);
            }
        }
    }
}
