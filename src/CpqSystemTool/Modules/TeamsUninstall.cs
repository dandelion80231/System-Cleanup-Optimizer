using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// Microsoft Teams 的干净卸载（覆盖三种形态：机器级 Machine-Wide Installer、经典每用户版、
    /// 新/商店 AppX 版）。参考微软官方「彻底卸载 Teams」仪式，并对被锁目录做重启补删（与 OneDriveUninstall 同款）。
    /// 说明：本类只卸 Teams 本体，不动 Office 套件（ODT 的 ExcludeApp Teams 才管 Office 里的 Teams 组件）。
    /// 目录强删 / 待补删标记 / 注册表清扫的公共逻辑在 RegistryUninstallHelper（D1 去重）。
    /// </summary>
    internal static class TeamsUninstall
    {
        private const string MarkerFile = "teams_pending_cleanup.txt";
        private const string LogTag = "  [Teams] ";

        /// <summary>
        /// 检测本机是否安装了任意形态的 Teams。任一命中即返回 true：
        ///  (a) 机器级 Installer（Uninstall 注册表里 DisplayName 含 "Teams Machine-Wide Installer"，
        ///      或 teamsbootstrapper.exe 存在）；
        ///  (b) 经典每用户版（%LOCALAPPDATA%\Microsoft\Teams\Update.exe 存在）；
        ///  (c) 商店/新 Teams（Get-AppxPackage MSTeams 或 ProvisionedPackage DisplayName 含 Teams）。
        /// </summary>
        public static bool IsStandaloneInstalled()
        {
            if (MachineInstallerPresent(out _)) return true;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (File.Exists(Path.Combine(localAppData, "Microsoft", "Teams", "Update.exe"))) return true;

            if (AppxTeamsInstalled()) return true;

            return false;
        }

        /// <summary>
        /// 干净卸载 Teams（按微软官方「彻底卸载 Teams」仪式顺序）：
        ///  ① 杀 Teams 进程（经典 teams.exe 与新版 ms-teams.exe；进程不存在则忽略错误）；
        ///  ② 机器级 Installer：优先 teamsbootstrapper.exe -x -m；不存在则回退 MsiExec /x {ProductCode} /quiet；
        ///  ③ 新/商店 Teams：Remove-AppxPackage + Remove-AppxProvisionedPackage；
        ///  ④ 经典每用户：%LOCALAPPDATA%\Microsoft\Teams\Update.exe --uninstall /s；
        ///  ⑤ 删 4 个残留目录（LOCALAPPDATA / ProgramData / Program Files / Program Files (x86)）；
        ///  ⑥ 清 HKLM/HKCU\SOFTWARE\Microsoft\Teams 注册表键；
        ///  ⑦ 被锁目录写入待补删标记，下次启动由 TryPendingCleanup 重试。
        /// </summary>
        public static void Uninstall(Action<string> log)
        {
            // ① 杀 Teams 进程（含经典 teams.exe 与新版 ms-teams.exe；进程不存在时报错被忽略，不影响主流程）
            log(LogTag + "结束 Teams 进程...");
            Exec.RunCmd(new[]
            {
                Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                "/f", "/im", "teams.exe"
            }, log);
            Exec.RunCmd(new[]
            {
                Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                "/f", "/im", "ms-teams.exe"
            }, log);

            // ② 机器级 Installer：优先 teamsbootstrapper.exe -x -m（静默卸载机器级）；
            //    找不到 bootstrapper 则回退 MSI：从 Uninstall 注册表动态解析 "Teams Machine-Wide Installer"
            //    的 ProductCode 跑 MsiExec /x {GUID} /quiet（不硬编码产品码，版本会变）。
            if (MachineInstallerPresent(out var bootstrapper))
            {
                if (bootstrapper != null)
                {
                    log(LogTag + "执行机器级卸载器: " + bootstrapper + " -x -m");
                    Exec.RunCmd(new[] { bootstrapper, "-x", "-m" }, log);
                }
                else
                {
                    var code = FindMachineInstallerProductCode();
                    if (code != null)
                    {
                        string msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
                        log(LogTag + "未找到 teamsbootstrapper.exe，回退 MsiExec /x " + code + " /quiet");
                        Exec.RunCmd(new[] { msiexec, "/x", code, "/quiet", "/norestart" }, log);
                    }
                    else
                    {
                        log(LogTag + "未解析到机器级 Installer 产品码，跳过该步");
                    }
                }
            }
            else
            {
                log(LogTag + "未检测到机器级 Installer，跳过该步");
            }

            // ③ 新/商店 Teams（AppX）：移除已安装包 + 预置包（镜像微软官方「彻底卸载 Teams」）
            log(LogTag + "移除新/商店版 Microsoft Teams (AppX)...");
            Exec.RunPowerShell("Get-AppxPackage -Name MSTeams | Remove-AppxPackage -ErrorAction SilentlyContinue", log);
            Exec.RunPowerShell("Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like '*Teams*' } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue", log);

            // ④ 经典每用户版：Update.exe --uninstall /s
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string classicUpdate = Path.Combine(localAppData, "Microsoft", "Teams", "Update.exe");
            if (File.Exists(classicUpdate))
            {
                log(LogTag + "卸载经典每用户 Teams: " + classicUpdate + " --uninstall /s");
                Exec.RunCmd(new[] { classicUpdate, "--uninstall", "/s" }, log);
            }
            else
            {
                log(LogTag + "未找到经典每用户 Teams 的 Update.exe，跳过该步");
            }

            // ⑤ 删除 4 个残留目录（PowerShell 递归强删，-EA 0 忽略单个文件占用错误；不存在的目录自动跳过）
            string[] dirs =
            {
                Path.Combine(localAppData, "Microsoft", "Teams"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Teams"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Teams"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Teams"),
            };
            RegistryUninstallHelper.RemoveDirs(dirs, LogTag, log);

            // ⑦ 复查：被 explorer/Teams 后台进程锁定的目录删不掉时，记录为「待补删」标记，
            //    下次程序启动（锁已释放）时由 TryPendingCleanup 重试。
            var lockedLeftovers = RegistryUninstallHelper.LockedLeftovers(dirs);
            if (lockedLeftovers.Count > 0)
            {
                RegistryUninstallHelper.WriteMarker(MarkerFile, lockedLeftovers, LogTag, log);
                log(LogTag + "以下目录被占用暂未删净，重启/下次启动时自动补删: " + string.Join("; ", lockedLeftovers));
            }

            // ⑥ 删除注册表残留（HKLM/HKCU 的 SOFTWARE\Microsoft\Teams 及其 WOW6432 镜像）
            CleanRegistry(log);

            log(LogTag + "Teams 卸载流程完成");
        }

        // ==================== 检测辅助 ====================

        /// <summary>机器级 Installer 是否存在：teamsbootstrapper.exe 存在，或注册表有 "Teams Machine-Wide Installer" 卸载项。
        /// 命中且有 bootstrapper 时通过 out 参数回传其完整路径（供 -x -m 调用）。</summary>
        private static bool MachineInstallerPresent(out string bootstrapperPath)
        {
            string[] candidateDirs =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Teams Installer"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Teams Installer"),
            };
            foreach (var dir in candidateDirs)
            {
                string cand = Path.Combine(dir, "teamsbootstrapper.exe");
                if (File.Exists(cand)) { bootstrapperPath = cand; return true; }
            }
            bootstrapperPath = null;
            return FindMachineInstallerProductCode() != null;
        }

        /// <summary>从 HKLM/HKCU 的 Uninstall（含 WOW6432 镜像）里查找 "Teams Machine-Wide Installer"，
        /// 返回其产品码（子键名即 MSI ProductCode GUID）。找不到返回 null。</summary>
        private static string FindMachineInstallerProductCode()
        {
            string[] bases =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };
            foreach (var baseKey in bases)
            {
                foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (var key = root.OpenSubKey(baseKey))
                        {
                            if (key == null) continue;
                            foreach (var sub in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var sk = key.OpenSubKey(sub))
                                    {
                                        if (sk == null) continue;
                                        var name = sk.GetValue("DisplayName") as string ?? "";
                                        if (name.IndexOf("Teams Machine-Wide Installer", StringComparison.OrdinalIgnoreCase) >= 0
                                            && sub.StartsWith("{", StringComparison.Ordinal) && sub.EndsWith("}", StringComparison.Ordinal))
                                            return sub;
                                    }
                                }
                                catch { /* 单个子键读取失败不影响其它 */ }
                            }
                        }
                    }
                    catch { /* 注册表根不可读（权限等）时跳过 */ }
                }
            }
            return null;
        }

        /// <summary>是否已安装新/商店 Teams：Get-AppxPackage -Name MSTeams 或 ProvisionedPackage DisplayName 含 Teams。</summary>
        private static bool AppxTeamsInstalled()
        {
            try
            {
                var r1 = Exec.RunPowerShellGet("(Get-AppxPackage -Name MSTeams) -ne $null", null).Trim();
                if (r1.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
                var r2 = Exec.RunPowerShellGet("((Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like '*Teams*' }) -ne $null)", null).Trim();
                return r2.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return false; }
        }

        // ==================== 重启自动补删 ====================

        /// <summary>
        /// 程序启动时调用：若存在「待补删」标记，则对其中每个目录再删一遍（此时锁已释放）；
        /// 删成功的从标记移除，全删净则删除标记文件。返回是否仍有删不净的目录。
        /// </summary>
        public static bool TryPendingCleanup(Action<string> log)
        {
            return RegistryUninstallHelper.TryCleanupMarker(MarkerFile, LogTag, log);
        }

        private static void CleanRegistry(Action<string> log)
        {
            string[] regKeys =
            {
                @"SOFTWARE\Microsoft\Teams",
                @"SOFTWARE\WOW6432Node\Microsoft\Teams",
            };
            foreach (var rel in regKeys)
                RegistryUninstallHelper.DeleteKeyTreeBothRoots(rel, log);
        }
    }
}
