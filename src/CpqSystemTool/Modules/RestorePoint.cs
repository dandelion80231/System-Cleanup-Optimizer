using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 系统还原点：优化/修改前的安全兜底。
    /// fix-11：原头注声称「每次优化前自动创建还原点」与实现不符——当前仅在「系统工具」页点击
    /// 「创建还原点」按钮时手动调用 RestorePoint.Create（见 MainWindow.SystemTools.cs），
    /// 清理/优化流程并未自动调用。此注释已修正为如实描述。
    /// 底层使用 PowerShell 的 Checkpoint-Computer / Get-ComputerRestorePoint / Restore-Computer。
    /// </summary>
    internal static class RestorePoint
    {
        public class RestoreInfo
        {
            public int Seq;
            public string Description;
            public string CreationTime;

            public override string ToString()
            {
                return "[" + Seq + "] " + Description + "  (" + CreationTime + ")";
            }
        }

        /// <summary>创建系统还原点。</summary>
        public static void Create(string desc, Action<string> log)
        {
            log("创建系统还原点：" + desc);
            string script = "Checkpoint-Computer -Description " + Exec.QuotePS(desc) + " -RestorePointType 'MODIFY_SETTINGS'";
            int r = Exec.RunPowerShell(script, log);
            if (r == 0) { log("  [OK] 还原点已创建（可在「系统还原」中查看/还原）"); return; }
            // VSS（卷影复制）服务未运行时 Checkpoint-Computer 直接失败：先尝试启动服务再重试一次。
            // 服务处于「禁用」状态时 Start-Service 会失败（-EA SilentlyContinue 静默），重试同样失败 → 走下方针对性提示。
            log("  [*] 首次创建失败，尝试启动 VSS 服务后重试...");
            r = Exec.RunPowerShell("Start-Service vss -EA SilentlyContinue; " + script, log);
            if (r == 0) { log("  [OK] 还原点已创建（启动 VSS 服务后成功）"); return; }
            log("  [!] 创建失败：VSS 服务无法自动启动（可能处于「禁用」状态）。可打开 services.msc，将「卷影复制(VSS)」设为手动并启动后重试；若系统保护未开启，请在「系统属性→系统保护」中启用。");
        }

        /// <summary>列出已有还原点。</summary>
        public static List<RestoreInfo> List(Action<string> log)
        {
            var list = new List<RestoreInfo>();
            string script = "Get-ComputerRestorePoint -EA 0 | ForEach-Object { " +
                "Write-Output ($_.SequenceNumber.ToString() + '|' + $_.Description + '|' + $_.CreationTime.ToString('yyyy-MM-dd HH:mm')) }";
            string outp = Exec.RunPowerShellGet(script, log);
            if (!string.IsNullOrEmpty(outp))
            {
                foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        int seq; int.TryParse(parts[0].Trim(), out seq);
                        list.Add(new RestoreInfo { Seq = seq, Description = parts[1].Trim(), CreationTime = parts[2].Trim() });
                    }
                }
            }
            if (list.Count == 0) log("  [提示] 暂无还原点，或系统还原未启用");
            return list;
        }

        /// <summary>还原到指定序号（需重启生效）。</summary>
        public static void Restore(int seq, Action<string> log)
        {
            log("请求系统还原到序号 " + seq + "（完成后需重启电脑）");
            // 先确认该序号的还原点存在，避免无效序号静默 no-op 却谎报成功
            string check = Exec.RunPowerShellGet("Get-ComputerRestorePoint -SequenceNumber " + seq.ToString() + " -EA 0 | Measure-Object | Select-Object -ExpandProperty Count", null);
            if (check?.Trim() != "1")
            {
                log("  [!] 未找到序号 " + seq + " 对应的系统还原点（可能已被清理或序号有误）");
                return;
            }
            string script = "Get-ComputerRestorePoint -SequenceNumber " + seq.ToString() + " -EA 0 | Restore-Computer";
            int r = Exec.RunPowerShell(script, log);
            if (r == 0) log("  [OK] 已发起还原，请重启电脑以生效");
            else log("  [!] 还原请求失败（退出码 " + r + "）");
        }

    }
}
