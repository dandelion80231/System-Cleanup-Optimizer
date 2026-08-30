using System;
using System.Collections.Generic;

namespace CpqSystemTool
{
    /// <summary>
    /// Windows 更新管理：禁用更新 / 恢复更新 / 查看状态 / 长期暂停。
    /// </summary>
    internal static class Updater
    {
        private const string WU_AU_KEY = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private const string WU_UX_KEY = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

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
            Exec.RunCmd(new[] { "reg", "add", WU_AU_KEY, "/v", "NoAutoUpdate", "/t", "REG_DWORD", "/d", "1", "/f" }, log);
            Exec.RunCmd(new[] { "reg", "add", WU_AU_KEY, "/v", "AUOptions", "/t", "REG_DWORD", "/d", "2", "/f" }, log);
            log("   [OK]");

            log("2) 停用更新相关服务...");
            foreach (var kv in UPDATE_SERVICES)
            {
                Exec.RunCmd(new[] { "sc", "config", kv.Key, "start=", StartMap(kv.Value.Item2) }, log);
                Exec.RunCmd(new[] { "net", "stop", kv.Key, "/y" }, log);
                log("   [OK] " + kv.Key + " -> " + kv.Value.Item2);
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
            Exec.RunCmd(new[] { "reg", "delete", WU_AU_KEY, "/v", "NoAutoUpdate", "/f" }, log);
            Exec.RunCmd(new[] { "reg", "delete", WU_AU_KEY, "/v", "AUOptions", "/f" }, log);
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
            string outp = Exec.RunCmdGet(new[] { "reg", "query", WU_AU_KEY, "/v", "NoAutoUpdate" }, log);
            bool blocked = outp.IndexOf("NoAutoUpdate", StringComparison.Ordinal) >= 0 && outp.IndexOf("0x1", StringComparison.Ordinal) >= 0;
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

            string cap = Exec.RunCmdGet(new[] { "reg", "query", WU_UX_KEY, "/v", "FlightSettingsMaxPauseDays" }, log);
            bool hasCap = cap.IndexOf("FlightSettingsMaxPauseDays", StringComparison.Ordinal) >= 0;
            string capText;
            if (hasCap)
            {
                // 解析 reg query 输出的实际 REG_DWORD 值（形如 0x2710 或十进制），不再硬编码 10000
                int? capVal = null;
                foreach (var line in cap.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf("FlightSettingsMaxPauseDays", StringComparison.Ordinal) < 0) continue;
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = parts.Length - 1; i >= 0; i--)
                    {
                        var p = parts[i];
                        if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(p.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hv)) { capVal = hv; break; }
                        }
                        else if (int.TryParse(p, out int dv)) { capVal = dv; break; }
                    }
                    break;
                }
                capText = capVal.HasValue ? "已设为 " + capVal.Value + " 天" : "已设置（值无法解析）";
            }
            else
            {
                capText = "未设置（默认）";
            }
            string pf = Exec.RunCmdGet(new[] { "reg", "query", WU_UX_KEY, "/v", "PauseFeatureUpdates" }, log);
            bool paused = pf.IndexOf("PauseFeatureUpdates", StringComparison.Ordinal) >= 0 && pf.IndexOf("0x1", StringComparison.Ordinal) >= 0;
            log("暂停上限(FlightSettingsMaxPauseDays) : " + capText);
            log("当前暂停状态 : " + (paused ? "已暂停（功能/质量更新）" : "未暂停"));
            log("（若策略未设、服务均为 AUTO/DEMAND 且未暂停，则更新未被拦截）");
            log("");
            MeteredConnection.MeteredStatus(log);
        }

        public static void AllowLongPause(Action<string> log)
        {
            log("=== 允许长期暂停更新（上限 10000 天）===");
            Exec.RunCmd(new[] { "reg", "add", WU_UX_KEY, "/v", "FlightSettingsMaxPauseDays", "/t", "REG_DWORD", "/d", "10000", "/f" }, log);
            log("   [OK] 已把“暂停更新”上限设为 10000 天");
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            Exec.RunCmd(new[] { "reg", "add", WU_UX_KEY, "/v", "PauseFeatureUpdates", "/t", "REG_DWORD", "/d", "1", "/f" }, log);
            Exec.RunCmd(new[] { "reg", "add", WU_UX_KEY, "/v", "PauseQualityUpdates", "/t", "REG_DWORD", "/d", "1", "/f" }, log);
            Exec.RunCmd(new[] { "reg", "add", WU_UX_KEY, "/v", "PauseFeatureUpdatesStartTime", "/t", "REG_SZ", "/d", today, "/f" }, log);
            Exec.RunCmd(new[] { "reg", "add", WU_UX_KEY, "/v", "PauseQualityUpdatesStartTime", "/t", "REG_SZ", "/d", today, "/f" }, log);
            log("   [OK] 已立即暂停功能更新与质量更新");
            log("完成。更新已暂停；可在「设置 → Windows 更新 → 高级选项」继续调整，或点「恢复 Windows 更新」解除暂停。");
        }

        private static void ClearPauseSettings(Action<string> log)
        {
            Exec.RunCmd(new[] { "reg", "delete", WU_UX_KEY, "/v", "FlightSettingsMaxPauseDays", "/f" }, log);
            foreach (var v in new[] { "PauseFeatureUpdates", "PauseQualityUpdates", "PauseFeatureUpdatesStartTime", "PauseQualityUpdatesStartTime" })
            {
                Exec.RunCmd(new[] { "reg", "delete", WU_UX_KEY, "/v", v, "/f" }, log);
            }
        }

        /// <summary>检测当前是否已禁用更新（NoAutoUpdate=1）</summary>
        public static bool IsUpdatesBlocked()
        {
            try
            {
                string outp = Exec.RunCmdGet(new[] { "reg", "query", WU_AU_KEY, "/v", "NoAutoUpdate" }, null);
                return outp.IndexOf("0x1", StringComparison.Ordinal) >= 0;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        /// <summary>检测当前是否处于长期暂停状态（PauseFeatureUpdates=1）</summary>
        public static bool IsLongPaused()
        {
            try
            {
                string pf = Exec.RunCmdGet(new[] { "reg", "query", WU_UX_KEY, "/v", "PauseFeatureUpdates" }, null);
                return pf.IndexOf("0x1", StringComparison.Ordinal) >= 0;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

    }
}
