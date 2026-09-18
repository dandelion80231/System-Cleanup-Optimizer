using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 系统激活：Windows + Office 激活状态检测与激活操作
    /// 来源：ZyperWin Activate.cs + Win11EasyConfig
    /// </summary>
    public static class Activation
    {
        // methodId 标识：诊断（仅查看状态，不执行激活）
        public const string DiagnosticMethodId = "诊断";

        // === Windows 激活 ===
        public static string GetWindowsActivationStatus()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return "未知";
                    var prodId = key.GetValue("ProductId")?.ToString() ?? "";
                    var ed = key.GetValue("EditionID")?.ToString() ?? "";
                    return $"版本: {ed}  产品ID: {prodId}";
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Activation.GetWindowsActivationStatus 失败: " + ex.Message); return "读取失败"; }
        }

        public static bool IsWindowsActivated()
        {
            try
            {
                string outp = Exec.RunPowerShellGet("(Get-WmiObject -Class SoftwareLicensingProduct -Filter \"PartialProductKey is not null AND LicenseIsAddon = false\").LicenseStatus", null);
                return outp != null && outp.Trim() == "1";
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Activation.IsWindowsActivated 失败: " + ex.Message); return false; }
        }

        public static void ActivateWindows(Action<string> log)
        {
            log("=== 激活 Windows ===");
            log("1) 安装产品密钥（通用批量授权密钥）...");
            // 绕过 PowerShell：用 cscript.exe 直接启动 slmgr.vbs（与 CheckWindowsActivation 一致）。
            // 注意：cmd 的 >nul 重定向在 PowerShell 里会创建名为 nul 的文件而非抑制输出，故此处改用 cscript + 绝对路径。
            string slmgr = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32\\slmgr.vbs");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", slmgr, "/ipk", "W269N-WFGWX-YVC9B-4J6C9-T83GX" }, log);
            log("2) 设置 KMS 服务器...");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", slmgr, "/skms", "kms.03k.org" }, log);
            log("3) 执行激活...");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", slmgr, "/ato" }, log);
            log("激活命令已发送，请稍后刷新检查状态。如果失败，可能需要更换 KMS 地址。");
        }

        public static void CheckWindowsActivation(Action<string> log)
        {
            log("=== Windows 激活状态 ===");
            // 绕过 PowerShell：用 cscript.exe 直接启动 slmgr.vbs（%windir% 是 cmd 变量，PowerShell 不展开，必须用绝对路径）
            string slmgr = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32\\slmgr.vbs");
            if (System.IO.File.Exists(slmgr))
            {
                string outp = Exec.RunCmdGet(new[] { "cscript.exe", "/nologo", slmgr, "/dli" }, log);
                if (!string.IsNullOrEmpty(outp))
                {
                    LogParsedDli(outp, log);
                }
                else log("   [!] 未能读取 Windows 激活状态（slmgr 输出为空）");

                log("=== 激活到期时间 ===");
                string xpr = Exec.RunCmdGet(new[] { "cscript.exe", "/nologo", slmgr, "/xpr" }, log);
                if (!string.IsNullOrEmpty(xpr))
                {
                    LogParsedXpr(xpr, log);
                }
                else log("   [!] 未能读取到期时间");
            }
            else
            {
                log("   [!] 未找到 slmgr.vbs");
                log("=== 激活到期时间 ===");
            }
        }

        // slmgr 状态词中英映射（未命中的值保留原文括注）
        private static readonly System.Collections.Generic.Dictionary<string, string> SLMGR_STATUS_ZH =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "LICENSED", "已激活" },
                { "UNLICENSED", "未激活" },
                { "NOTLICENSED", "未激活" },
                { "GRACE", "宽限期" },
                { "PARTIALLICENSED", "部分激活" },
                { "EXTENDEDGRACE", "宽限期（延期）" },
            };

        private static string TranslateSlmgrStatus(string value)
        {
            string v = value.Trim();
            if (SLMGR_STATUS_ZH.TryGetValue(v, out var zh))
            {
                // 映射值里带括号时直接返回，否则补英文原文括注
                if (zh.Contains('（')) return zh;
                if (v != zh) return zh + " (" + v + ")";
                return zh;
            }
            return v + "（未识别）";
        }

        /// <summary>解析 slmgr /dli 输出（中英双语），转成中文结构化字段输出；一个字段都没解析出来时原文兜底。</summary>
        private static void LogParsedDli(string outp, Action<string> log)
        {
            int parsed = 0;
            bool inBlock = false;
            foreach (var raw in outp.Split('\n'))
            {
                string t = raw.Trim();
                if (string.IsNullOrWhiteSpace(t)) { inBlock = false; continue; }
                if (t.StartsWith("---") && t.Length >= 3 && t.All(c => c == '-'))
                {
                    inBlock = !inBlock; // 进入分隔线后的块
                    continue;
                }
                string value = null, label = null;
                int sep = t.IndexOf(':');
                if (sep > 0)
                {
                    string key = t.Substring(0, sep).Trim();
                    value = t.Substring(sep + 1).Trim();
                    if (key == "Name" || key == "名称") { label = "名称"; }
                    else if (key == "Description" || key == "描述") { label = "描述"; }
                    else if (key == "Partial Product Key" || key == "部分产品密钥") { label = "部分密钥"; }
                    else if (key == "License Status" || key == "许可证状态") { label = "状态"; }
                    else if (key == "Grace Period Remaining" || key == "宽限期剩余") { label = "宽限期剩余"; }
                    else if (key == "Windows Activation ID" || key == "Windows 激活 ID") { label = "Windows 激活 ID"; }
                }
                if (label != null)
                {
                    string val = value ?? "";
                    if (label == "状态") val = TranslateSlmgrStatus(val);
                    else if (label == "宽限期剩余")
                    {
                        // "7 days" / "7 day" / "7 天" → "7 天"
                        var m = System.Text.RegularExpressions.Regex.Match(val, @"(\d+)\s*(days?|天)");
                        if (m.Success) val = m.Groups[1].Value + " 天";
                    }
                    log(label + "：" + val);
                    parsed++;
                }
                else if (inBlock && !string.IsNullOrEmpty(t))
                {
                    // 块首行（如 "Evaluation period has expired" / 中文标题）作为许可证块标识
                    log("许可证块：" + t);
                    parsed++;
                }
                else if (sep > 0 && value != null)
                {
                    // 【P3】未识别的 key:value 行不再静默丢弃（原实现直接丢，多许可证块/未来新字段时零信息），原样保留
                    log(t);
                    parsed++;
                }
            }
            if (parsed == 0)
            {
                log("   [!] 无法解析该输出，原文如下：");
                log(outp);
            }
        }

        /// <summary>解析 slmgr /xpr 输出（中英双语），输出「激活到期时间」；一个字段都没解析出来时原文兜底。</summary>
        private static void LogParsedXpr(string xpr, Action<string> log)
        {
            int parsed = 0;
            foreach (var raw in xpr.Split('\n'))
            {
                string t = raw.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.StartsWith("---") && t.Length >= 3 && t.All(c => c == '-'))
                {
                    log(t); // 保留分隔线（如 ---LICENSED---），原文输出
                    continue;
                }
                string value = null;
                if (t.Contains(':'))
                {
                    int sep = t.IndexOf(':');
                    string key = t.Substring(0, sep).Trim();
                    value = t.Substring(sep + 1).Trim();
                    if (key == "Product" || key == "产品")
                    {
                        log("产品：" + value);
                        parsed++;
                        continue;
                    }
                    if (key == "Activation expiration date" || key == "激活到期时间")
                    {
                        log("激活到期时间：" + value);
                        parsed++;
                        continue;
                    }
                }
                // 块标题行（如 "Windows(R) Professional Edition:"）
                if (t.EndsWith(":") && t.IndexOf(':') == t.Length - 1)
                {
                    log("产品：" + t.TrimEnd(':'));
                    parsed++;
                    continue;
                }
                // 永久激活 / 到期时间描述行
                if (t.Contains("permanently activated") || t.Contains("永久激活") ||
                    t.Contains("PERPETUAL") || t.Contains("permanent activation"))
                {
                    log("激活到期时间：已永久激活");
                    parsed++;
                    continue;
                }
                if (t.Contains("license expires") || t.Contains("许可证到期") || t.Contains("激活到期"))
                {
                    log("激活到期时间：" + t);
                    parsed++;
                    continue;
                }
                // 未识别行：作为到期时间原文输出（值保留原文）
                if (parsed == 0 && t.Length < 120)
                {
                    log("激活到期时间：" + t);
                    parsed++;
                    continue;
                }
                // 其他未识别行直接原文输出
                log(t);
            }
            if (parsed == 0)
            {
                log("   [!] 无法解析该输出，原文如下：");
                log(xpr);
            }
        }

        // === Office 激活 ===
        public static bool IsOfficeInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
                {
                    if (key != null) return true;
                }
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\Common\InstallRoot"))
                {
                    if (key?.GetValue("Path") != null) return true;
                }
                // OSPP检测
                string d = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft Office\Office16");
                if (System.IO.Directory.Exists(d) && System.IO.File.Exists(System.IO.Path.Combine(d, "OSPP.VBS")))
                    return true;
                d = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Office\Office16");
                if (System.IO.Directory.Exists(d) && System.IO.File.Exists(System.IO.Path.Combine(d, "OSPP.VBS")))
                    return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("IsOfficeInstalled 异常: " + ex.Message); }
            return false;
        }

        public static bool IsOfficeActivated()
        {
            try
            {
                string d = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft Office\Office16");
                string d2 = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Office\Office16");
                string ospp = null;
                if (System.IO.File.Exists(System.IO.Path.Combine(d, "OSPP.VBS"))) ospp = System.IO.Path.Combine(d, "OSPP.VBS");
                else if (System.IO.File.Exists(System.IO.Path.Combine(d2, "OSPP.VBS"))) ospp = System.IO.Path.Combine(d2, "OSPP.VBS");
                
                    if (ospp != null)
                    {
                        // 绕过 PowerShell：cscript.exe 直接调用 OSPP.VBS（// 双斜杠经 PowerShell -Command 二次解析会丢输出）
                        string outp = Exec.RunCmdGet(new[] { "cscript.exe", "/nologo", ospp, "/dstatusall" }, null);
                        return outp != null && outp.Contains("---LICENSED---");
                }
                return false;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Activation.IsOfficeActivated 失败: " + ex.Message); return false; }
        }

        public static void ActivateOffice(Action<string> log)
        {
            log("=== 激活 Office ===");
            string d = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft Office\Office16");
            string d2 = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Office\Office16");
            string ospp = null;
            if (System.IO.File.Exists(System.IO.Path.Combine(d, "OSPP.VBS"))) ospp = System.IO.Path.Combine(d, "OSPP.VBS");
            else if (System.IO.File.Exists(System.IO.Path.Combine(d2, "OSPP.VBS"))) ospp = System.IO.Path.Combine(d2, "OSPP.VBS");

            if (ospp == null) { log("未找到 Office 安装"); return; }

            log("1) 安装 KMS 密钥...");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", ospp, "/inpkey:XQNVK-8JYDB-WJ9W3-YJ8YR-WFG99" }, log);
            log("2) 设置 KMS 服务器...");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", ospp, "/sethst:kms.03k.org" }, log);
            log("3) 激活...");
            Exec.RunCmd(new[] { "cscript.exe", "/nologo", ospp, "/act" }, log);
            log("Office 激活命令已发送");
        }

        public static void CheckOfficeActivation(Action<string> log)
        {
            log("=== Office 激活状态 ===");
            // 多路径+递归查找 OSPP.VBS（覆盖 Office 2013/2016/2019/2021/365 及 C2R 安装位置）
            string[] candidates =
            {
                @"%ProgramFiles%\Microsoft Office\Office16",
                @"%ProgramFiles(x86)%\Microsoft Office\Office16",
                @"%ProgramFiles%\Microsoft Office\root\Office16",
                @"%ProgramFiles(x86)%\Microsoft Office\root\Office16",
                @"%ProgramFiles%\Microsoft Office",
            };
            string ospp = null;
            foreach (var p in candidates)
            {
                var expanded = Environment.ExpandEnvironmentVariables(p);
                if (System.IO.Directory.Exists(expanded))
                {
                    try
                    {
                        var found = System.IO.Directory.GetFiles(expanded, "OSPP.VBS", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null) { ospp = found; break; }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("IsOfficeActivated 查找 OSPP.VBS 异常: " + ex.Message); }
                }
            }
            if (ospp != null)
            {
                // 绕过 PowerShell：cscript.exe 直接调用 OSPP.VBS（// 双斜杠在 PowerShell 里会被特殊解析导致无输出）
                string outp = Exec.RunCmdGet(new[] { "cscript.exe", "/nologo", ospp, "/dstatusall" }, log);
                if (string.IsNullOrEmpty(outp))
                {
                    log("   [!] 未能读取激活状态（cscript 输出为空）");
                    return;
                }
                if (outp.Contains("---LICENSED---"))
                {
                    log("✅ Office 已激活");
                    LogOfficeStatusBlocks(outp, log);
                }
                else
                {
                    // 订阅版（M365/O365）OSPP 永远返回 ---UNLICENSED---（激活走云端 + Resiliency 心跳，不走本地 KMS）。
                    // 对齐旧版 v1.21 行为：命中订阅 PID 即报「✅ 已激活」。
                    // 已激活时【完全不打印 OSPP 信息块】——块里「未激活 (---UNLICENSED---)」「TIMEBASED_SUB」等字样
                    // 是 OSPP 的局部状态行，对已激活的订阅版只会误导，让用户误以为没激活；故直接省略。
                    // 未激活（非订阅版）时仍打印 OSPP 块作排查参考。
                    bool isSubscription = IsSubscriptionOfficeInstalled();
                    if (isSubscription)
                    {
                        log("✅ 订阅版 Office（Microsoft 365）已激活——激活状态由 Microsoft 365 云端账号管理");
                        log("   本地 OSPP 不适用，OSPP 信息块已省略；如需查看可在 Office 应用「文件 → 账户」确认");
                    }
                    else
                    {
                        log("❌ Office 未激活或激活状态异常");
                        LogOfficeStatusBlocks(outp, log);
                    }
                }
            }
            else log("未检测到 Office 2013/2016/2019/2021/365");
        }

        // === Office /dstatusall 输出的中文化映射表（长期中文，不逐次机翻） ===
        // SKU 名称友好化：按关键字匹配 LICENSE NAME 值（大小写不敏感，顺序即优先级）
        private static readonly System.Collections.Generic.List<System.Tuple<string, string>> OfficeSkuNameMap =
            new System.Collections.Generic.List<System.Tuple<string, string>>
            {
                new System.Tuple<string, string>("O365HomeBusiness", "Microsoft 365 家庭版（订阅）"),
                new System.Tuple<string, string>("O365ProPlus", "Microsoft 365 专业版（订阅）"),
                new System.Tuple<string, string>("TIMEBASED_SUB", "订阅版（TimeBased）"),
                new System.Tuple<string, string>("HomeStudent", "家庭和学生版"),
                new System.Tuple<string, string>("PerpetualVL", "永久版（批量授权）"),
                new System.Tuple<string, string>("Visio", "Visio"),
                new System.Tuple<string, string>("Project", "Project"),
                new System.Tuple<string, string>("Access", "Access"),
            };

        /// <summary>
        /// 探测 C2R 台账是否含订阅版 ProductReleaseIds（O365ProPlusRetail 等）。
        /// 读 HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration\ProductReleaseIds，
        /// 任一 PID 以 "O365" 开头即判定为订阅版。失败返回 false（不中断主流程）。
        /// </summary>
        private static bool IsSubscriptionOfficeInstalled()
        {
            try
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                string[] keyPaths =
                {
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRunStore",
                };
                foreach (var kp in keyPaths)
                {
                    using var key = baseKey.OpenSubKey(kp);
                    if (key == null) continue;
                    if (key.GetValue("ProductReleaseIds") is string s && s.Length > 0)
                    {
                        foreach (var pid in s.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                        {
                            if (pid.StartsWith("O365", System.StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch { /* 读取失败不中断 */ }
            return false;
        }

        // 许可状态值映射（---LICENSED--- 等）
        private static readonly System.Collections.Generic.Dictionary<string, string> OfficeLicenseStatusMap =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "---LICENSED---", "已激活" },
                { "---UNLICENSED---", "未激活" },
                { "---PARTIALLICENSED---", "部分激活" },
                { "---EXTENDEDGRACE---", "宽限期内" },
            };

        // 错误描述固定映射表（命中输出中文 + 括注，未命中保留原文）
        private static readonly System.Collections.Generic.Dictionary<string, string> OfficeErrorDescriptionMap =
            new System.Collections.Generic.Dictionary<string, string>()
            {
                { "The Software Licensing Service reported that the product key is not available.",
                  "订阅版 Office 激活由 Microsoft 365 云端账号管理，本地 OSPP 无法读取产品密钥（正常现象，不影响实际可用）" },
                { "The Software Licensing Service was unable to activate this computer.",
                  "软件许可服务无法激活此计算机（网络不通或 KMS 不可达）" },
                { "0xC004F014",
                  "许可服务报告：产品密钥不可用（订阅版属正常现象，激活由云端管理）" },
            };

        /// <summary>SKU ID 前缀映射表：按前缀匹配完整 SKU 值，未命中回落到关键字匹配</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> OfficeSkuIdMap =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
            };

        /// <summary>按 ---Processing 分隔线切块，逐块识别字段并输出中文段落。
        /// <paramref name="isSubscription"/> 为 true（订阅版已激活）时，跳过「错误码 / 错误描述」两行——
        /// 订阅版 OSPP 恒报 0xC004F014/UNLICENSED，属正常现象，已激活时这两行只是噪声，不必展示。</summary>
        private static void LogOfficeStatusBlocks(string outp, Action<string> log, bool isSubscription = false)
        {
            // 按 ---Processing 分隔线切块（每块是一个 SKU）
            var blocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            var current = new System.Collections.Generic.List<string>();
            bool inBlock = false;
            foreach (var rawLine in outp.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("---Processing", StringComparison.Ordinal))
                {
                    if (inBlock && current.Count > 0) blocks.Add(current);
                    current = new System.Collections.Generic.List<string>();
                    inBlock = true;
                }
                else if (line.StartsWith("-----", StringComparison.Ordinal) || line.StartsWith("------", StringComparison.Ordinal))
                {
                    // 纯分隔线（如块尾的 -----）
                    if (inBlock && current.Count > 0)
                    {
                        blocks.Add(current);
                        current = new System.Collections.Generic.List<string>();
                        inBlock = false;
                    }
                }
                else if (line.Length > 0)
                {
                    if (inBlock) current.Add(line);
                    else
                    {
                        // 未分块前的行（理论上不应出现，兜底）
                        current.Add(line);
                    }
                }
            }
            if (inBlock && current.Count > 0) blocks.Add(current);

            int blockIndex = 0;
            foreach (var block in blocks)
            {
                blockIndex++;
                var fields = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                var unknownLines = new System.Collections.Generic.List<string>();

                foreach (var line in block)
                {
                    // 识别字段：全大写英文前缀（中英双语都认）
                    string key = null, value = null;
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        key = line.Substring(0, colonIdx).Trim();
                        value = line.Substring(colonIdx + 1).Trim();
                    }
                    bool known = false;
                    if (key != null)
                    {
                        switch (key)
                        {
                            case "SKU ID":
                            case "SKU":
                            case "许可证名称":
                            case "LICENSE NAME":
                            case "名称":
                            case "许可证描述":
                            case "LICENSE DESCRIPTION":
                            case "描述":
                            case "许可证状态":
                            case "LICENSE STATUS":
                            case "状态":
                            case "错误码":
                            case "ERROR CODE":
                            case "错误描述":
                            case "ERROR DESCRIPTION":
                                known = true;
                                if (!fields.ContainsKey(key)) fields[key] = value ?? "";
                                break;
                        }
                    }
                    if (!known) unknownLines.Add(line);
                }

                // 输出为每块一段中文（块间空一行）
                log($"【SKU {blockIndex}】");
                var nameVal = fields.ContainsKey("LICENSE NAME") ? fields["LICENSE NAME"]
                    : fields.ContainsKey("名称") ? fields["名称"] : null;
                var name = nameVal != null ? FriendlySkuName(nameVal) : null;
                if (name != null) log($"名称：{name}");
                else if (nameVal != null) log($"名称：{nameVal}");

                var descVal = fields.ContainsKey("LICENSE DESCRIPTION") ? fields["LICENSE DESCRIPTION"]
                    : fields.ContainsKey("描述") ? fields["描述"] : null;
                if (descVal != null) log($"描述：{descVal}");

                var statusVal = fields.ContainsKey("LICENSE STATUS") ? fields["LICENSE STATUS"]
                    : fields.ContainsKey("状态") ? fields["状态"] : null;
                if (statusVal != null)
                {
                    var normalized = statusVal.Replace(" ", "");
                    if (OfficeLicenseStatusMap.TryGetValue(normalized, out var zh))
                        log($"状态：{zh} ({statusVal})");
                    else log($"状态：{statusVal}");
                }

                var errCode = fields.ContainsKey("ERROR CODE") ? fields["ERROR CODE"]
                    : fields.ContainsKey("错误码") ? fields["错误码"] : null;
                // 订阅版已激活：OSPP 恒报 0xC004F014，属正常现象，跳过「错误码 / 错误描述」两行（避免误导观感）
                if (!isSubscription && errCode != null) log($"错误码：{errCode}");

                var errDescVal = fields.ContainsKey("ERROR DESCRIPTION") ? fields["ERROR DESCRIPTION"]
                    : fields.ContainsKey("错误描述") ? fields["错误描述"] : null;
                if (errDescVal != null && !isSubscription)
                {
                    var trimmedDesc = errDescVal.Trim();
                    if (OfficeErrorDescriptionMap.TryGetValue(trimmedDesc, out var zhErr))
                        log($"错误描述：{zhErr}");
                    else log($"错误描述：（原文）{trimmedDesc}");
                }

                foreach (var u in unknownLines) log($"未识别行：{u}");

                if (blockIndex < blocks.Count) log(""); // 块间空一行
            }
        }

        /// <summary>SKU 名称友好化：匹配映射表，未命中保留原文</summary>
        private static string FriendlySkuName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return rawName;
            // 先试精确 SKU ID 映射
            foreach (var kv in OfficeSkuIdMap)
            {
                if (rawName.Equals(kv.Key, System.StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            // 再试关键字包含匹配
            foreach (var tuple in OfficeSkuNameMap)
            {
                if (rawName.IndexOf(tuple.Item1, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return tuple.Item2;
            }
            return rawName;
        }

        // === MAS 联网激活（方案 B：真集成 Microsoft Activation Scripts）===
        // 官方无人值守一行式（Windows 8+）：
        //   & ([ScriptBlock]::Create((irm https://get.activated.win))) /<switch>
        // 参数来源：https://massgrave.dev/command_line_switches（大小写不敏感、空格分隔、可组合）
        private static readonly System.Collections.Generic.Dictionary<string, string> MasSwitches =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "HWID",    "/HWID" },                       // 数字许可证（硬件永久，需联网）
                { "KMS4k",   "/Z-Windows /Z-KMS4k" },         // KMS4k（TSforge 子方法：卷许版 Windows，有效期 4000+ 年，离线；家庭版/零售版无效）
                { "Ohook",   "/Ohook" },                      // Office DLL 劫持激活（无需联网）
                { "KMS",     "/K-Windows" },                  // Online KMS（Windows，180 天可续期）
                { "TSforge", "/Z-WindowsESUOffice" },         // TSforge（强制写入激活 Windows + ESU + Office）
            };

        /// <summary>该 methodId 是否走 MAS 联网脚本（需二次确认 + 联网）。</summary>
        public static bool IsMasMethod(string methodId)
            => methodId != null && MasSwitches.ContainsKey(methodId);

        // MAS 交互式提权脚本超时：30 分钟（用户可能手动操作）
        private const int MAS_TIMEOUT_MS = 1800000;

        // fix-10：钉死官方 get.activated.win 脚本的 SHA256，下载后校验一致才执行，杜绝上游被劫持即执行任意代码。
        // 哈希获取/更新方式：MAS 官方脚本会随版本更新，更新时须重新下载 https://get.activated.win，
        // 与 github.com/massgravel/Microsoft-Activation-Scripts 对应版本内容人工核对后，再替换本常量；
        // 切勿使用未经核对的哈希。哈希记录时间：2026-09-18（脚本 6133 字节）。
        private const string MasScriptSha256 = "1e64a2bc2132d274e99dc802a79aaea2bddc38b27d5ca93c41dc35636d7a2545";

        /// <summary>联网下载并执行官方 MAS 脚本完成对应方式激活，结束后自动刷新状态。</summary>
        public static void ActivateWithMAS(string methodId, Action<string> log)
        {
            if (!MasSwitches.TryGetValue(methodId ?? "", out var sw))
            {
                log("未知激活方法: " + methodId);
                return;
            }
            log("=== 启动 Microsoft Activation Scripts (" + methodId + ") ===");
            log("将联网下载并执行官方 MAS 脚本（来源 massgrave.dev，采用 GNU GPL v3 许可）。");
            log("⚠️ 安全提示：此操作会联网下载并执行 get.activated.win 的官方脚本；仅使用官方 HTTPS 地址，"
                + "执行前请确认网络环境可信。脚本下载后先做 SHA256 校验（钉死哈希 " + MasScriptSha256.Substring(0, 16) + "…），"
                + "哈希不一致将中止执行。");
            log("若弹出用户账户控制，请允许；过程中请按脚本窗口提示操作。");

            // 预置 TLS1.2 兼容老系统；下载脚本到临时文件 → 本地 SHA256 校验（fix-10 钉死哈希）→ 一致才用 ScriptBlock 执行
            // -Command 内用 ScriptBlock 包装以正确传递开关参数
            string ps =
                  "$ErrorActionPreference='Stop';"
                + "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;"
                + "$_tmp=Join-Path $env:TEMP ('mas_'+[guid]::NewGuid().ToString('N')+'.ps1');"
                + "Invoke-WebRequest -Uri 'https://get.activated.win' -OutFile $_tmp -UseBasicParsing;"
                + "$_h=(Get-FileHash -LiteralPath $_tmp -Algorithm SHA256).Hash.ToLowerInvariant();"
                + "if($_h -ne '" + MasScriptSha256 + "'){ Write-Error ('MAS 脚本哈希校验失败：期望 " + MasScriptSha256 + "，实际 '+$_h); Remove-Item -LiteralPath $_tmp -Force -ErrorAction SilentlyContinue; exit 1 };"
                + "& ([ScriptBlock]::Create((Get-Content -LiteralPath $_tmp -Raw))) " + sw + ";"
                + "Remove-Item -LiteralPath $_tmp -Force -ErrorAction SilentlyContinue";

            try
            {
                // 安全加固：
                // 1) 复用 Helpers/Exec.cs 取得 powershell 完整路径（%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe），
                //    避免依赖 PATH 解析裸 "powershell.exe" 被同名恶意程序劫持（PATH hijacking）；
                // 2) 继续用 -EncodedCommand（Base64 UTF-16LE）传递脚本，规避 -Command 引号转义陷阱（与 Exec.RunPS 一致）；
                // 3) 保留 UseShellExecute + Verb=runas 提权，让 MAS 能写入激活信息。
                // 依赖 HTTPS 信任官方源 + 本地 SHA256 钉死校验（fix-10）：脚本由 get.activated.win 下载，
                // 下载后必须与常量 MasScriptSha256 一致才执行；官方更新脚本后需按注释流程更新常量。
                var psPath = System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                var psi = new ProcessStartInfo(psPath,
                    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log("  [!] 无法启动 PowerShell（可能被安全软件拦截）"); return; }
                    // 交互式提权脚本：给足 30 分钟超时（用户可能手动操作），超时则终止避免 UI 挂起
                    if (!p.WaitForExit(MAS_TIMEOUT_MS))
                    {
                        try { p.Kill(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                        log("  [!] MAS 脚本执行超时（30 分钟），已终止。");
                        return;
                    }
                    log("MAS 脚本已退出（退出码 " + p.ExitCode + "）。");
                }

                // M365 横幅抑制：OHook 激活 Office 365 后，部分版本会弹
                // 「There was a problem checking this device's license status」。
                // 依据 MAS 官方手动步骤写入 HKCU 注册表项抑制（HKCU 无需管理员权限）。
                if (methodId == "Ohook")
                    SuppressOffice365LicenseBanner(log);
            }
            catch (Exception ex)
            {
                log("  [!] 启动 MAS 失败: " + ex.Message);
                log("  可能原因：无网络访问 / 被 ISP 或安全软件拦截。可点「诊断」查看当前状态。");
                return;
            }

            log("正在刷新激活状态...");
            CheckStatus(log);
        }

        /// <summary>
        /// 抑制部分 Office 365 版本在 OHook 激活后弹出的「There was a problem checking this device's license status」横幅。
        /// 依据 MAS 官方手动步骤：写入 HKCU\Software\Microsoft\Office\16.0\Common\Licensing\Resiliency
        /// 的 TimeOfLastHeartbeatFailure = "2040-01-01T00:00:00Z"（REG_SZ）。HKCU 无需管理员权限；
        /// 写失败不影响激活本身，仅记录并忽略。
        /// </summary>
        private static void SuppressOffice365LicenseBanner(Action<string> log)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Office\16.0\Common\Licensing\Resiliency"))
                {
                    if (key != null)
                    {
                        key.SetValue("TimeOfLastHeartbeatFailure", "2040-01-01T00:00:00Z", RegistryValueKind.String);
                        log("  [OK] 已写入注册表，抑制 Office 365 许可状态横幅。");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Ignore(ex);
                log("  [!] 抑制 Office 365 横幅的注册表写入失败（可忽略，不影响激活）: " + ex.Message);
            }
        }

        public static void Activate(string methodId, Action<string> log)
        {
            if (methodId == DiagnosticMethodId) CheckStatus(log);
            else if (IsMasMethod(methodId)) ActivateWithMAS(methodId, log);
            else if (methodId == "windows" || methodId == "win") ActivateWindows(log);
            else if (methodId == "office") ActivateOffice(log);
            else log("未知激活方法: " + methodId);
        }
        public static void CheckStatus(Action<string> log)
        {
            CheckWindowsActivation(log);
            log("");
            CheckOfficeActivation(log);
        }
    }
}
