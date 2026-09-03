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
                    // 提取关键行（版本/许可状态/产品密钥）
                    foreach (var line in outp.Split('\n'))
                    {
                        var t = line.Trim();
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (t.Contains("名称:") || t.Contains("Name:") ||
                            t.Contains("描述:") || t.Contains("Description:") ||
                            t.Contains("许可证状态:") || t.Contains("License Status:") ||
                            t.Contains("许可证剩余:") || t.Contains("License Remaining:") ||
                            t.Contains("密钥:") || t.Contains("Product Key:"))
                            log(t);
                    }
                }
                else log("   [!] 未能读取 Windows 激活状态（slmgr 输出为空）");

                log("=== 激活到期时间 ===");
                string xpr = Exec.RunCmdGet(new[] { "cscript.exe", "/nologo", slmgr, "/xpr" }, log);
                if (!string.IsNullOrEmpty(xpr))
                {
                    foreach (var line in xpr.Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line.Trim())) log(line.Trim());
                }
                else log("   [!] 未能读取到期时间");
            }
            else
            {
                log("   [!] 未找到 slmgr.vbs");
                log("=== 激活到期时间 ===");
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
                if (outp.Contains("LICENSED"))
                {
                    log("✅ Office 已激活");
                    // 提取关键信息（产品名 + 许可证状态）
                    foreach (var line in outp.Split('\n').Take(8))
                        if (!string.IsNullOrWhiteSpace(line)) log(line.Trim());
                }
                else
                {
                    log("❌ Office 未激活或激活状态异常");
                    foreach (var line in outp.Split('\n').Take(5))
                        if (!string.IsNullOrWhiteSpace(line)) log(line.Trim());
                }
            }
            else log("未检测到 Office 2013/2016/2019/2021/365");
        }

        // === MAS 联网激活（方案 B：真集成 Microsoft Activation Scripts）===
        // 官方无人值守一行式（Windows 8+）：
        //   & ([ScriptBlock]::Create((irm https://get.activated.win))) /<switch>
        // 参数来源：https://massgrave.dev/command_line_switches（大小写不敏感、空格分隔、可组合）
        private static readonly System.Collections.Generic.Dictionary<string, string> MasSwitches =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "HWID",    "/HWID" },                       // 数字许可证（硬件永久，需联网）
                { "KMS38",   "/KMS38" },                      // KMS38（激活至 2038 年，无需联网）
                { "Ohook",   "/Ohook" },                      // Office DLL 劫持激活（无需联网）
                { "KMS",     "/K-Windows" },                  // Online KMS（Windows，180 天可续期）
                { "TSforge", "/Z-WindowsESUOffice" },         // TSforge（强制写入激活 Windows + ESU + Office）
            };

        /// <summary>该 methodId 是否走 MAS 联网脚本（需二次确认 + 联网）。</summary>
        public static bool IsMasMethod(string methodId)
            => methodId != null && MasSwitches.ContainsKey(methodId);

        // MAS 交互式提权脚本超时：30 分钟（用户可能手动操作）
        private const int MAS_TIMEOUT_MS = 1800000;

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
                + "执行前请确认网络环境可信。脚本内容随版本更新，故未做哈希固定（以官方地址为准）。");
            log("若弹出用户账户控制，请允许；过程中请按脚本窗口提示操作。");

            // 预置 TLS1.2 兼容老系统；-Command 内用 ScriptBlock 包装以正确传递开关参数
            string ps = "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;"
                      + "& ([ScriptBlock]::Create((irm https://get.activated.win))) " + sw;

            try
            {
                // 安全加固：
                // 1) 复用 Helpers/Exec.cs 取得 powershell 完整路径（%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe），
                //    避免依赖 PATH 解析裸 "powershell.exe" 被同名恶意程序劫持（PATH hijacking）；
                // 2) 继续用 -EncodedCommand（Base64 UTF-16LE）传递脚本，规避 -Command 引号转义陷阱（与 Exec.RunPS 一致）；
                // 3) 保留 UseShellExecute + Verb=runas 提权，让 MAS 能写入激活信息。
                // 依赖 HTTPS 信任：脚本经官方 get.activated.win 下载，未做哈希钉扎（MAS 脚本内容随版本更新会变化，以官方地址为准）。
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
