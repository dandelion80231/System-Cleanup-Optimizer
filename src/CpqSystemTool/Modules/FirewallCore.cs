using System;
using System.Collections.Generic;
using System.Linq;

namespace CpqSystemTool
{
    /// <summary>
    /// Windows Defender 防火墙封装：配置文件开关状态、规则列表与增删、打开高级控制台。
    /// 通过 Get/Set/New/Remove-NetFirewallRule（PS）操作，与现有 SearchHost 防火墙按钮同模式。
    /// </summary>
    internal static class FirewallCore
    {
        public class ProfileInfo
        {
            public string Name;    // Domain / Private / Public
            public bool Enabled;
        }

        public class RuleInfo
        {
            public string DisplayName;
            public string Direction; // Inbound / Outbound
            public string Action;    // Allow / Block
            public bool Enabled;
            public override string ToString() =>
                DisplayName + "  [" + (Direction == "Inbound" ? "入站" : "出站") + "/" + (Action == "Block" ? "阻止" : "允许") + "]";
        }

        /// <summary>读取三个防火墙配置文件（域/专用/公用）的开关状态。兼容旧调用方（忽略错误）。</summary>
        public static List<ProfileInfo> GetProfiles(Action<string> log = null) => GetProfiles(log, out _);

        /// <summary>读取三个防火墙配置文件；失败时通过 error 返回 stderr/退出码摘要（不再静默吞错）。</summary>
        public static List<ProfileInfo> GetProfiles(Action<string> log, out string error)
        {
            return ParseNetItems(
                "Get-NetFirewallProfile | ForEach-Object { \"$($_.Name)|$($_.Enabled)\" }",
                p => new ProfileInfo
                {
                    Name = p[0].Trim(),
                    Enabled = p[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase)
                },
                2, "防火墙配置文件读取失败", log, out error);
        }

        /// <summary>列出全部防火墙规则（名称/方向/动作/启用）。兼容旧调用方（忽略错误）。</summary>
        public static List<RuleInfo> ListRules(Action<string> log = null) => ListRules(log, out _);

        /// <summary>列出全部防火墙规则；失败时通过 error 返回 stderr/退出码摘要（不再静默吞错）。</summary>
        public static List<RuleInfo> ListRules(Action<string> log, out string error)
        {
            return ParseNetItems(
                "Get-NetFirewallRule | ForEach-Object { \"$($_.DisplayName)|$($_.Direction)|$($_.Action)|$($_.Enabled)\" }",
                p => new RuleInfo
                {
                    DisplayName = p[0].Trim(),
                    Direction = p[1].Trim(),
                    Action = p[2].Trim(),
                    Enabled = p[3].Trim().Equals("True", StringComparison.OrdinalIgnoreCase)
                },
                4, "防火墙规则读取失败", log, out error);
        }

        /// <summary>添加"阻止远程地址"出站规则（可批量以逗号分隔的域/IP）。</summary>
        public static void AddBlockAddressRule(string displayName, string[] addresses, Action<string> log)
        {
            log?.Invoke("添加防火墙规则: " + displayName);
            // 安全加固：逐个校验地址（仅允许 IP/域名/CIDR 的合法字符，拒绝空白与 shell 元字符
            // ; & | $ ( ) ' " 等），并对每个地址用单引号包裹 + 转义内部单引号，避免命令注入。
            var validAddrs = new System.Collections.Generic.List<string>();
            foreach (var raw in addresses ?? new string[0])
            {
                var a = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(a)) continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(a, @"^[A-Za-z0-9._:/-]+$"))
                {
                    log?.Invoke("  [!] 跳过非法地址（含空白或特殊字符）: " + a);
                    continue;
                }
                validAddrs.Add("'" + Exec.EscapeSingleQuote(a) + "'");
            }
            if (validAddrs.Count == 0)
            {
                log?.Invoke("  [!] 没有合法地址，跳过防火墙规则添加");
                return;
            }
            var addr = string.Join(",", validAddrs);
            // 修正：原先丢弃 RunPowerShell 的退出码，命令失败（无管理员权限、参数被拒等）也照样打印 [OK]。
            // 改为接收返回值，非零时打印 [FAIL] 并附上退出码。
            int rc = Exec.RunPowerShell(
                $"Remove-NetFirewallRule -DisplayName '{EscapeLiteral(displayName)}' -ErrorAction SilentlyContinue;" +
                $"New-NetFirewallRule -DisplayName '{Escape(displayName)}' -Direction Outbound -RemoteAddress {addr} -Action Block", log);
            if (rc == 0) log?.Invoke("[OK] 防火墙规则已添加");
            else log?.Invoke("[FAIL] 防火墙规则添加失败（退出码 " + rc + "）: " + displayName);
        }

        public static void RemoveRule(string displayName, Action<string> log)
        {
            log?.Invoke("移除防火墙规则: " + displayName);
            // 修正：同 AddBlockAddressRule，原先丢弃退出码，Remove-NetFirewallRule 失败也打印 [OK]
            int rc = Exec.RunPowerShell($"Remove-NetFirewallRule -DisplayName '{EscapeLiteral(displayName)}' -ErrorAction SilentlyContinue", log);
            if (rc == 0) log?.Invoke("[OK] 防火墙规则已移除");
            else log?.Invoke("[FAIL] 防火墙规则移除失败（退出码 " + rc + "）: " + displayName);
        }

        public static void OpenAdvanced()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wf.msc") { UseShellExecute = true }); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 打开高级防火墙失败: " + ex.Message); }
        }

        /// <summary>判断错误信息是否为权限类（访问被拒绝），供 UI 分流提示使用。</summary>
        public static bool IsPermissionError(string error) =>
            !string.IsNullOrEmpty(error)
            && (error.Contains("拒绝访问") || error.Contains("Access is denied")
                || error.Contains("0x80070005") || error.Contains("E_ACCESSDENIED"));

        // PS 单引号字符串：' 转义为 ''（复用 Exec.EscapeSingleQuote）；-DisplayName 按通配符匹配，故对 [ ] * ? 加 ` 转义，避免误删
        private static string Escape(string s) => Exec.EscapeSingleQuote(s);
        private static string EscapeLiteral(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Exec.EscapeSingleQuote(s);
            foreach (var c in new[] { '[', ']', '*', '?' })
                s = s.Replace(c.ToString(), "`" + c);
            return s;
        }

        // 通用：执行 PS、检查退出码、按 '|' 解析多行输出为多态项列表（GetProfiles / ListRules 共用）。
        private static List<T> ParseNetItems<T>(string ps, Func<string[], T> map, int minParts, string debugTag, Action<string> log, out string error)
        {
            error = null;
            var list = new List<T>();
            try
            {
                var r = Exec.RunPowerShellGetFull(ps, log);
                if (r.exitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(r.stderr) ? ("退出码 " + r.exitCode) : r.stderr.Trim();
                    return list;
                }
                if (!string.IsNullOrWhiteSpace(r.stdout))
                {
                    foreach (var line in r.stdout.Split('\n'))
                    {
                        var s = line.Trim();
                        if (string.IsNullOrEmpty(s)) continue;
                        var parts = s.Split('|');
                        if (parts.Length >= minParts) list.Add(map(parts));
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; System.Diagnostics.Debug.WriteLine("[CpqSystemTool] " + debugTag + ": " + ex.Message); }
            return list;
        }
    }
}
