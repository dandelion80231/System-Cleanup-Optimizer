using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// Windows 更新管理：禁用更新 / 恢复更新 / 查看状态 / 长期暂停。
    /// </summary>
    internal static class Updater
    {
        // 注册表路径（子键路径，不含 hive 前缀；hive 统一走 Registry.LocalMachine）。
        // ★ 按仓库约定（见 Defender.cs / RegistryHelper.cs），所有注册表读写必须走 RegistryHelper，
        //   由它使用 64 位视图写入、64/32 双视图读取/删除，避免 32 位视图（Wow6432Node）错读/残留。
        private const string WU_AU_KEY = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private const string WU_UX_KEY = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        // (启用时启动类型, 禁用时启动类型)
        private static readonly Dictionary<string, Tuple<string, string>> UPDATE_SERVICES =
            new Dictionary<string, Tuple<string, string>>
        {
            { "wuauserv",     Tuple.Create("auto", "disabled") },
            { "dosvc",        Tuple.Create("auto", "disabled") },
            { "WaaSMedicSvc", Tuple.Create("manual", "disabled") },
            { "UsoSvc",       Tuple.Create("auto", "manual") }
        };

        private static readonly string[] UPDATE_TASK_PATHS = new[]
        {
            "\\Microsoft\\Windows\\WindowsUpdate\\",
            "\\Microsoft\\Windows\\UpdateOrchestrator\\"
        };

        private static string StartMap(string arg)
        {
            if (arg == "auto") return "auto";
            if (arg == "manual") return "demand";
            return "disabled";
        }

        public static void BlockUpdates(Action<string> log)
        {
            log("=== 禁用 Windows 更新 ===");
            log("1) 写入组策略：关闭自动更新...");
            // 修正：原先走 reg add 子进程，与仓库约定（必须走 RegistryHelper 的 32 位视图一致读写）不符；
            // 改为 RegistryHelper.SetDword，写入 64 位视图、结果以 bool 判定，逻辑更清晰。NoAutoUpdate 写入不可破坏。
            bool okNoAuto = RegistryHelper.SetDword(Registry.LocalMachine, WU_AU_KEY, "NoAutoUpdate", 1, log);
            bool okAuOpt = RegistryHelper.SetDword(Registry.LocalMachine, WU_AU_KEY, "AUOptions", 2, log);
            if (okNoAuto && okAuOpt) log("   [OK]");
            else log("   [FAIL] 组策略写入失败（NoAutoUpdate=" + okNoAuto + "，AUOptions=" + okAuOpt + "）");

            log("2) 停用更新相关服务...");
            foreach (var kv in UPDATE_SERVICES)
            {
                int rcCfg = Exec.RunCmd(new[] { "sc", "config", kv.Key, "start=", StartMap(kv.Value.Item2) }, log);
                int rcStop = Exec.RunCmd(new[] { "net", "stop", kv.Key, "/y" }, log);
                if (rcCfg == 0) log("   [OK] " + kv.Key + " -> " + kv.Value.Item2);
                else log("   [FAIL] " + kv.Key + " 启动类型配置失败（退出码 " + rcCfg + "）");
                // net stop 对本就未运行的服务返回非零，属正常，仅提示
                if (rcStop != 0) log("   [!] net stop " + kv.Key + " 返回 " + rcStop + "（服务可能未在运行）");
            }

            log("3) 禁用更新计划任务...");
            foreach (var path in UPDATE_TASK_PATHS)
            {
                Exec.RunPowerShell("Get-ScheduledTask -TaskPath " + Exec.QuotePS(path) + " -ErrorAction SilentlyContinue | Disable-ScheduledTask | Out-Null", log);
            }
            log("   [OK]");

            log("完成。Windows 更新已禁用（含 Windows Update Medic 防护）。需要恢复时点「恢复更新」。");
        }

        public static void RestoreUpdates(Action<string> log)
        {
            log("=== 恢复 Windows 更新 ===");
            log("1) 清除组策略...");
            // 改为 RegistryHelper.DeleteValue：同时清理 64/32 双视图，避免旧版残留（比 reg delete 更彻底）。
            RegistryHelper.DeleteValue(Registry.LocalMachine, WU_AU_KEY, "NoAutoUpdate", log);
            RegistryHelper.DeleteValue(Registry.LocalMachine, WU_AU_KEY, "AUOptions", log);
            log("   [OK]");

            log("2) 恢复更新服务为默认启动类型...");
            foreach (var kv in UPDATE_SERVICES)
            {
                Exec.RunCmd(new[] { "sc", "config", kv.Key, "start=", StartMap(kv.Value.Item1) }, log);
                Exec.RunCmd(new[] { "net", "start", kv.Key }, log);
                log("   [OK] " + kv.Key + " -> " + kv.Value.Item1);
            }

            log("3) 启用更新计划任务...");
            foreach (var path in UPDATE_TASK_PATHS)
            {
                Exec.RunPowerShell("Get-ScheduledTask -TaskPath " + Exec.QuotePS(path) + " -ErrorAction SilentlyContinue | Enable-ScheduledTask | Out-Null", log);
            }
            log("   [OK]");

            log("4) 清除长期暂停设置...");
            ClearPauseSettings(log);
            log("   [OK]");

            log("完成。Windows 更新已恢复为系统默认（自动下载/安装），且移除了长期暂停。");
        }

        public static void UpdateStatus(Action<string> log)
        {
            log("=== 查看更新状态 ===");
            // reg query 文本解析改为 RegistryHelper 直接读 Dword（先 64 位再回退 32 位视图）。
            int? noAuto = RegistryHelper.GetDwordOrNull(Registry.LocalMachine, WU_AU_KEY, "NoAutoUpdate");
            bool blocked = noAuto.HasValue && noAuto.Value == 1;
            log("组策略 NoAutoUpdate : " + (blocked ? "已设 1（拦截中）" : "未设（未通过策略拦截）"));

            foreach (var svc in UPDATE_SERVICES.Keys)
            {
                string so = Exec.RunCmdGet(new[] { "sc", "qc", svc }, log);
                string st = "未知";
                foreach (var line in so.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf("START_TYPE", StringComparison.Ordinal) >= 0)
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0) st = line.Substring(idx + 1).Trim();
                    }
                }
                log("服务 " + svc.PadRight(12) + " : " + st);
            }

            // FlightSettingsMaxPauseDays：原本需解析 reg query 的 0x/十进制文本，现直接读整数，无解析失败分支。
            int? cap = RegistryHelper.GetDwordOrNull(Registry.LocalMachine, WU_UX_KEY, "FlightSettingsMaxPauseDays");
            string capText = cap.HasValue ? "已设为 " + cap.Value + " 天" : "未设置（默认）";
            int? pf = RegistryHelper.GetDwordOrNull(Registry.LocalMachine, WU_UX_KEY, "PauseFeatureUpdates");
            bool paused = pf.HasValue && pf.Value == 1;
            log("暂停上限(FlightSettingsMaxPauseDays) : " + capText);
            log("当前暂停状态 : " + (paused ? "已暂停（功能/质量更新）" : "未暂停"));
            log("（若策略未设、服务均为 AUTO/DEMAND 且未暂停，则更新未被拦截）");
            log("");
            MeteredConnection.MeteredStatus(log);
        }

        public static void AllowLongPause(Action<string> log)
        {
            log("=== 允许长期暂停更新（上限 10000 天）===");
            // 全部改为 RegistryHelper：Dword 写 64 位视图，Sz（开始时间）用 SetSz。
            RegistryHelper.SetDword(Registry.LocalMachine, WU_UX_KEY, "FlightSettingsMaxPauseDays", 10000, log);
            log("   [OK] 已把“暂停更新”上限设为 10000 天");
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            RegistryHelper.SetDword(Registry.LocalMachine, WU_UX_KEY, "PauseFeatureUpdates", 1, log);
            RegistryHelper.SetDword(Registry.LocalMachine, WU_UX_KEY, "PauseQualityUpdates", 1, log);
            RegistryHelper.SetSz(Registry.LocalMachine, WU_UX_KEY, "PauseFeatureUpdatesStartTime", today, log);
            RegistryHelper.SetSz(Registry.LocalMachine, WU_UX_KEY, "PauseQualityUpdatesStartTime", today, log);
            log("   [OK] 已立即暂停功能更新与质量更新");
            log("完成。更新已暂停；可在「设置 → Windows 更新 → 高级选项」继续调整，或点「恢复 Windows 更新」解除暂停。");
        }

        private static void ClearPauseSettings(Action<string> log)
        {
            RegistryHelper.DeleteValue(Registry.LocalMachine, WU_UX_KEY, "FlightSettingsMaxPauseDays", log);
            foreach (var v in new[] { "PauseFeatureUpdates", "PauseQualityUpdates", "PauseFeatureUpdatesStartTime", "PauseQualityUpdatesStartTime" })
            {
                RegistryHelper.DeleteValue(Registry.LocalMachine, WU_UX_KEY, v, log);
            }
        }

        /// <summary>检测当前是否已禁用更新（NoAutoUpdate=1）</summary>
        public static bool IsUpdatesBlocked()
        {
            try
            {
                int? noAuto = RegistryHelper.GetDwordOrNull(Registry.LocalMachine, WU_AU_KEY, "NoAutoUpdate");
                return noAuto.HasValue && noAuto.Value == 1;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        /// <summary>检测当前是否处于长期暂停状态（PauseFeatureUpdates=1）</summary>
        public static bool IsLongPaused()
        {
            try
            {
                int? pf = RegistryHelper.GetDwordOrNull(Registry.LocalMachine, WU_UX_KEY, "PauseFeatureUpdates");
                return pf.HasValue && pf.Value == 1;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

    }
}
