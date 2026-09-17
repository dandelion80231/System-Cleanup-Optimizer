using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CpqSystemTool
{
    /// <summary>
    /// 独立 OneDrive 云同步客户端（OneDriveSetup.exe 安装，非 C2R 套件内组件）的干净卸载。
    /// 参考「Geek Uninstaller」强力卸载方式：先杀进程 → 官方卸载器 /uninstall /allusers →
    /// 清扫可重装的程序目录与注册表项。刻意【不删】%LOCALAPPDATA%\OneDrive\（用户同步数据）。
    /// 说明：C2R 套件里的「Groove/OneDrive for Business」是另一回事（ODT 管），不在本类范围。
    /// 目录强删 / 待补删标记 / 注册表清扫的公共逻辑在 RegistryUninstallHelper（D1 去重）。
    /// </summary>
    internal static class OneDriveUninstall
    {
        private const string MarkerFile = "onedrive_pending_cleanup.txt";
        private const string LogTag = "  [OneDrive] ";

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
            log(LogTag + "结束 OneDrive 客户端进程...");
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
                log(LogTag + "执行官方卸载器: " + setup + " /uninstall /allusers");
                Exec.RunCmd(new[] { setup, "/uninstall", "/allusers", "/quiet" }, log);
            }
            else
            {
                log(LogTag + "未找到 OneDriveSetup.exe，跳过官方卸载器（可能已手动删过）");
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
            RegistryUninstallHelper.RemoveDirs(dirs, LogTag, log);

            // 3.5) 复查：Program Files 下的 OneDrive 若因 shell 扩展被 explorer 锁定而删不掉，
            //      记录为「待补删」标记，下次程序启动（锁已释放）时由 TryPendingCleanup 重试。
            var lockedLeftovers = RegistryUninstallHelper.LockedLeftovers(dirs);
            if (lockedLeftovers.Count > 0)
            {
                RegistryUninstallHelper.WriteMarker(MarkerFile, lockedLeftovers, LogTag, log);
                log(LogTag + "以下目录被占用暂未删净，重启/下次启动时自动补删: " + string.Join("; ", lockedLeftovers));
            }

            // 4) 删除注册表残留（Uninstall 卸载项 + Run 自启值；版本信息键纯展示不删）
            CleanRegistry(log);

            log(LogTag + "独立 OneDrive 客户端卸载完成（用户数据目录 %LOCALAPPDATA%\\OneDrive 已保留）");
        }

        // ==================== 重启自动补删 ====================

        /// <summary>
        /// 程序启动时调用：若存在「待补删」标记，则对其中每个目录再删一遍（此时 shell 扩展锁已释放）；
        /// 删成功的从标记移除，全删净则删除标记文件。返回是否仍有删不净的目录。
        /// </summary>
        public static bool TryPendingCleanup(Action<string> log)
        {
            return RegistryUninstallHelper.TryCleanupMarker(MarkerFile, LogTag, log);
        }

        private static void CleanRegistry(Action<string> log)
        {
            // 卸载项（2 个相对路径 × HKLM/HKCU）：Uninstall\OneDriveSetup.exe（含 WOW6432 镜像）
            string[] regKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
            };
            foreach (var rel in regKeys)
                RegistryUninstallHelper.DeleteKeyTreeBothRoots(rel, log);

            // 清 Run 自启值（HKCU + HKLM）
            string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            foreach (var val in new[] { "OneDriveSetup", "OneDrive" })
                RegistryUninstallHelper.DeleteValueBothRoots(runKey, val, log);
        }
    }
}
