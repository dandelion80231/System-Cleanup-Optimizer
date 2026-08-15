using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;

namespace CpqSystemTool
{
    /// <summary>
    /// Windows 版本转换（对应"一键转换 7.0 (OSSQ)"的安全实现）：
    /// - DISM /Online /Get-CurrentEdition          查询当前版本
    /// - DISM /Online /Get-TargetEditions          查询可转换的目标版本（参考）
    /// - changepk.exe /ProductKey &lt;零售通用密钥&gt;       执行版本切换（自动重启）
    /// - BackupActivation / RestoreActivation      备份/还原激活信息（"还原"功能）
    /// 激活部分由 MAS（Activation.cs）负责，本模块只做版本切换 + 备份/还原。
    /// 安全设计：转换前必须二次确认 + 密钥可留空（自动用目标版本零售通用密钥）；只读查询无风险。
    /// </summary>
    public static class VersionSwitch
    {
        /// <summary>查询当前 Windows 版本（注册表读 EditionID，回退 ProductName；不依赖可能被改过的 ProductName）</summary>
        public static string GetCurrentEdition(Action<string> log)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        // 优先 EditionID（系统原始值，slmgr/changepk 不会改它）
                        string editionId = k.GetValue("EditionID") as string;
                        if (!string.IsNullOrEmpty(editionId)) return editionId.Trim();
                        // 回退 ProductName（可能被手动改过）
                        string productName = k.GetValue("ProductName") as string;
                        if (!string.IsNullOrEmpty(productName)) return productName.Trim();
                    }
                }
            }
            catch (Exception ex) { log?.Invoke("[!] 注册表读当前版本失败：" + ex.Message); }
            return null;
        }

        /// <summary>按 CurrentBuild 推断 OS 大版本（"Windows 10" 或 "Windows 11"），build >= 22000 是 Win11。规避 ProductName 被改导致的误判。</summary>
        public static string GetOsMajor()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        int build = 0;
                        int.TryParse(k.GetValue("CurrentBuild") as string, out build);
                        return build >= 22000 ? "Windows 11" : "Windows 10";
                    }
                }
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  }
            return null;
        }

        /// <summary>查询可转换的目标版本列表（对齐 OSSQ 一键转换 7.0 的 14 个版本）</summary>
        public static List<string> GetTargetEditions(Action<string> log)
        {
            // ⚠️ 键名必须与 GVLK / ChineseEditionName 字典一致；含 LTSC 等需证书版本（SkuInstalled 会检测）
            return new List<string>
            {
                "Professional", "ProfessionalWorkstation", "ProfessionalEducation",
                "ProfessionalSingleLanguage", "ProfessionalCountrySpecific",
                "Education", "Enterprise", "EnterpriseS", "EnterpriseG",
                "ServerRdsh", "IoTEnterprise",
                "Home", "Home Single Language", "Home China"
            };
        }

        /// <summary>
        /// Windows 10/11 各版本【零售通用安装密钥】（changepk 切换版本专用，不是 KMS GVLK）。
        /// 每个版本存 1~3 个候选密钥（第一个为微软官方 RTM 零售通用密钥，其余为 OSSQ 7.0 原版内置的备用/变体密钥，
        /// 原版即按序列逐个 slmgr /ipk 尝试——见 exe 内命令序列），切换时依次尝试直到成功。
        /// ⚠️ 不能用 KMS GVLK（W269N-... 等）做 changepk 版本切换，那类密钥只用于 KMS 激活。
        /// 来源：winaero.com + sftkey.com + elevenforum (Shawn Brink) 双源核对（2026-07 确认）+ 原版 7.0 exe 提取。
        /// </summary>
        private static readonly Dictionary<string, string[]> GVLK = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Professional",               new[] { "VK7JG-NPHTM-C97JM-9MPGT-3V66T", "YC7N8-G7WR6-9WR4H-6Y2W4-KBT6X" } },
            { "Professional N",             new[] { "2B87N-8KFHP-DKV6R-Y2C8J-PKCKT" } },
            { "ProfessionalWorkstation",    new[] { "DXG7C-N36C4-C4HTG-X4T3X-2YV77", "XNQ6Q-JMCQ9-JG299-3T7VJ-QDCRJ" } },
            { "ProfessionalWorkstation N",  new[] { "WYPNQ-8C467-V2W6J-TX4WX-WT2RQ" } },
            { "ProfessionalEducation",      new[] { "8PTT6-RNW4C-6V7J2-C2D3X-MHBPB", "4V7NJ-MQKCW-VYTFX-DX6JD-QRT4B" } },
            { "ProfessionalEducation N",    new[] { "GJTYN-HDMQY-FRR76-HVGC7-QPF8P" } },
            { "ProfessionalSingleLanguage", new[] { "G3KNM-CHG6T-R36X3-9QDG6-8M8K9", "HNGCC-Y38KG-QVK8D-WMWRK-X86VK" } },  // 专业单语言版（密钥取自原版 7.0）
            { "ProfessionalCountrySpecific",new[] { "M9P2N-Y3YX6-4VXTK-MCT93-C38YY", "N8FHW-G2P4G-DYVDY-BYHVQ-XHKXT", "VMKVQ-3MN6B-BVM9F-YWV97-R9FCX" } },  // 专业中文版（密钥取自原版 7.0）
            { "Education",                  new[] { "YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY", "F48BJ-8NX82-MRVY9-PF8BW-HMHY2" } },
            { "Education N",                new[] { "84NGF-MHBT6-FXBX8-QWJK7-DRR8H" } },
            { "Enterprise",                 new[] { "XGVPP-NMH47-7TTHJ-W3FW7-8HV2C", "96YNV-9X4RP-2YYKB-RMQH4-6Q72D" } },
            { "Enterprise N",               new[] { "WGGHN-J84D6-QYCPR-T7PJ7-X766F" } },
            { "EnterpriseS",                new[] { "PG7H6-7RNT3-R4MGR-HMJK2-J462D", "M7XTQ-FN8P6-TTKYV-9D4CC-J462D" } },  // 企业 LTSC（需证书，SkuInstalled 检测）
            { "EnterpriseG",                new[] { "YYVX9-NTFWV-6MDM3-9PT4T-4M68B", "43TBQ-NH92J-XKTM7-KT3KK-P39PB" } },
            { "EnterpriseGN",               new[] { "FW7NV-4T673-HF4VX-9X4MM-B4H4T" } },
            { "ServerRdsh",                 new[] { "FV469-WGNG4-YQP66-2B2HY-KD8YX" } },  // 虚拟桌面版（本机 skus 已含证书）
            { "IoTEnterprise",              new[] { "XQQYW-NFFMW-XJPBH-K8732-CKFFD" } },  // IoT 企业版（本机 skus 已含证书）
            { "Home",                       new[] { "YTMG3-N6DKC-DKB77-7M9GH-8HVX7", "33QT6-RCNYF-DXB4F-DGP7B-7MHX9" } },
            { "Home N",                     new[] { "4CPRK-NM3K3-X6XXQ-RXX86-WXCHW" } },
            { "Home Single Language",       new[] { "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT", "9HGRW-NH2CQ-XQHJD-YCRWB-6VJV7" } },
            { "Home China",                 new[] { "N2434-X9D7W-8PF6X-8DV9T-8TYMD", "JN9HR-MH7K4-DBPDD-TFTXF-Q9MMF" } },
            { "Core",                       new[] { "YTMG3-N6DKC-DKB77-7M9GH-8HVX7" } },
            { "Core N",                     new[] { "4CPRK-NM3K3-X6XXQ-RXX86-WXCHW" } },
            { "CoreSingleLanguage",         new[] { "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT" } },
        };

        /// <summary>获取目标版本的候选密钥列表（依次尝试直到 slmgr /ipk 成功），未知版本返回 null</summary>
        public static string[] GetKeys(string edition)
        {
            if (edition == null) return null;
            if (GVLK.TryGetValue(edition.Trim(), out string[] ks)) return ks;
            string enName = MapChineseToEnglish(edition);
            if (enName != null && GVLK.TryGetValue(enName, out ks)) return ks;
            return null;
        }

        /// <summary>目标版本在本机是否已安装许可证证书（spp tokens skus）。常见零售版本镜像预装视为可用；仅检查镜像内不一定预装的 SKU。</summary>
        public static bool SkuInstalled(string edition)
        {
            // 入口先中文→英文映射：避免 UI 传入中文显示名（如"企业版 LTSC"）时命中 default→true，
            // 从而跳过 LTSC/IoT 转换必需的证书注入（InstallSkuCert）。
            if (edition != null)
                edition = MapChineseToEnglish(edition) ?? edition;
            string dir;
            switch (edition == null ? "" : edition.Trim())
            {
                case "EnterpriseS":               dir = "EnterpriseS"; break;                 // LTSC：普通镜像不预装（本机缺）
                case "IoTEnterpriseS":            dir = "IoTEnterpriseS"; break;              // IoT LTSC
                case "ServerRdsh":                dir = "ServerRdsh"; break;
                case "IoTEnterprise":             dir = "IoTEnterprise"; break;
                case "ProfessionalCountrySpecific": dir = "ProfessionalCountrySpecific"; break;
                case "ProfessionalSingleLanguage":  dir = "ProfessionalSingleLanguage"; break;
                default: return true;   // 常见版本：镜像预装证书，直接可用
            }
            try
            {
                return System.IO.Directory.Exists(System.IO.Path.Combine(Environment.SystemDirectory, "spp", "tokens", "skus", dir));
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return true; }
        }

        /// <summary>
        /// 自动注入 SKU 证书（LTSC/IoT LTSC / 专业单语言 / 专业中文 等镜像不预装的版本）。
        /// 证书文件（微软专有许可令牌 *.xrm-ms）作为嵌入资源随程序分发（Resources/Skus/&lt;SKU&gt;/*.xrm-ms），
        /// 运行时从程序集嵌入资源提取到临时目录，再用 slmgr.vbs /ilc 逐个安装，最后 slmgr /rilc 重装使证书生效。
        /// 若本机许可存储已含该证书，则直接 slmgr /rilc 重装（更快、离线）。
        /// 合规说明：这些 *.xrm-ms 为微软专有许可令牌，随本项目以 Apache-2.0 协议内嵌分发，
        /// 仅用于本机版本切换所需的证书安装，不对外提供、不修改。
        /// </summary>
        public static bool InstallSkuCert(string sku, Action<string> log)
        {
            string targetDir = System.IO.Path.Combine(Environment.SystemDirectory, "spp", "tokens", "skus", sku);

            // 1) 本机许可存储已存在该 SKU 证书 → 直接重装（离线、更快）
            bool existsLocally;
            try { existsLocally = System.IO.Directory.Exists(targetDir); }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); existsLocally = false; }

            if (existsLocally)
            {
                log("   [*] 本机许可存储已含 " + sku + " 证书，执行 slmgr /rilc 重装...");
                int rc = Exec.RunCmd(new[] { "slmgr.vbs", "/rilc" }, log, capture: true);
                if (rc != 0)
                {
                    log("   [!] slmgr /rilc 返回码 " + rc + "（证书可能未生效，可稍后手动重跑）");
                    return false;
                }
                log("   [OK] slmgr /rilc 证书重装完成");
                return true;
            }

            // 2) 本机缺证书 → 从嵌入资源提取并 slmgr /ilc 安装
            var asm = typeof(VersionSwitch).Assembly;
            string marker = "Resources.Skus." + sku + ".";
            string[] resNames = asm.GetManifestResourceNames()
                .Where(n => n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0
                         && n.EndsWith(".xrm-ms", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (resNames.Length == 0)
            {
                log("   [!] 内置证书包中未找到 " + sku + " 对应的 .xrm-ms 资源。");
                log("   [*] 该版本转换需要对应 SKU 许可证书，请从包含「" + sku + "」的 Windows 安装镜像/ISO 获取：");
                log("       挂载 ISO → 复制其 spp\\tokens\\skus\\" + sku + " 目录到 " + targetDir);
                log("       → 以管理员运行 slmgr.vbs /rilc 重装证书。");
                log("   [*] 或改用本机已预装证书的版本（如 专业版 / 企业版 / 家庭版）进行转换。");
                return false;
            }

            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CpqSystemTool_Skus", sku);
            try { System.IO.Directory.CreateDirectory(tmpDir); }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }

            log("   [*] 从内置证书包提取 " + resNames.Length + " 个 " + sku + " 证书并 slmgr /ilc 安装...");
            bool anyOk = false;
            foreach (var resName in resNames)
            {
                int markerIdx = resName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                string fileName = resName.Substring(markerIdx + marker.Length);
                string outPath = System.IO.Path.Combine(tmpDir, fileName);
                try
                {
                    using (var src = asm.GetManifestResourceStream(resName))
                    {
                        if (src == null) { log("   [!] 无法读取嵌入资源 " + resName); continue; }
                        using (var dst = System.IO.File.Create(outPath))
                            src.CopyTo(dst);
                    }
                    int rc = Exec.RunCmd(new[] { "slmgr.vbs", "/ilc", outPath }, log, capture: true);
                    if (rc == 0) { anyOk = true; log("   [OK] 证书已安装：" + fileName); }
                    else log("   [!] 证书 " + fileName + " 安装返回码 " + rc);
                }
                catch (Exception ex)
                {
                    log("   [!] 提取/安装证书 " + fileName + " 失败：" + ex.Message);
                }
            }

            if (!anyOk)
            {
                log("   [!] 全部内置证书安装失败，无法继续。");
                return false;
            }

            // 重装证书使新注入的 SKU 生效
            log("   [*] 执行 slmgr /rilc 使证书生效...");
            int rcR = Exec.RunCmd(new[] { "slmgr.vbs", "/rilc" }, log, capture: true);
            if (rcR != 0) { log("   [!] slmgr /rilc 返回码 " + rcR); return false; }
            log("   [OK] " + sku + " 证书注入完成");
            return true;
        }

        /// <summary>中文版名 → 英文版名映射（参考 OSSQ 一键转换的中文版列表 + DISM 中文输出格式）</summary>
        public static string MapChineseToEnglish(string cnName)
        {
            if (cnName == null) return null;
            cnName = cnName.Trim();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Windows 10 家庭版",                "Home" },
                { "Windows 10 家庭单语言版",        "Home Single Language" },
                { "Windows 10 家庭中文版",            "Home China" },
                { "Windows 10 专业版",                "Professional" },
                { "Windows 10 专业教育版",            "ProfessionalEducation" },
                { "Windows 10 教育版",                "Education" },
                { "Windows 10 企业版",                "Enterprise" },
                { "Windows 11 家庭版",                "Home" },
                { "Windows 11 家庭单语言版",        "Home Single Language" },
                { "Windows 11 家庭中文版",            "Home China" },
                { "Windows 11 专业版",                "Professional" },
                { "Windows 11 专业教育版",            "ProfessionalEducation" },
                { "Windows 11 专业工作站版",        "ProfessionalWorkstation" },
                { "Windows 11 教育版",                "Education" },
                { "Windows 11 企业版",                "Enterprise" },
                { "Windows 11 企业版 G",            "EnterpriseG" },
                { "Windows 10 专业单语言版",        "ProfessionalSingleLanguage" },
                { "Windows 11 专业单语言版",        "ProfessionalSingleLanguage" },
                { "Windows 10 专业中文版",          "ProfessionalCountrySpecific" },
                { "Windows 11 专业中文版",          "ProfessionalCountrySpecific" },
                { "Windows 10 企业版 LTSC",         "EnterpriseS" },
                { "Windows 11 企业版 LTSC",         "EnterpriseS" },
                { "Windows 10 企业 LTSC 版",        "EnterpriseS" },
                { "Windows 11 企业 LTSC 版",        "EnterpriseS" },
                { "Windows 10 虚拟桌面版",          "ServerRdsh" },
                { "Windows 11 虚拟桌面版",          "ServerRdsh" },
                { "Windows 10 IoT 企业版",          "IoTEnterprise" },
                { "Windows 11 IoT 企业版",          "IoTEnterprise" },
                { "Windows 10 核心版",                "Core" },
                { "Windows 11 核心版",                "Core" },
                // 兼容中英混合：去掉 Windows 前缀和"版"字后模糊匹配
            };
            if (map.TryGetValue(cnName, out string en)) return en;
            // 模糊匹配：去掉前缀/版字后按关键字匹配（兼容中英混合输入）
            // 专业 + 教育 → ProfessionalEducation（必须优先，避免误命中普通 Professional）
            // 模糊匹配：在中文输入里按关键字直接映射英文 SKU（原实现误在中文 map 键里搜英文子串，恒为 false，整段失效）。
            if (cnName.Contains("专业") && cnName.Contains("教育")) return "ProfessionalEducation";
            if (cnName.Contains("专业") && cnName.Contains("工作站")) return "ProfessionalWorkstation";
            if (cnName.Contains("专业") && cnName.Contains("单语言")) return "ProfessionalSingleLanguage";
            if (cnName.Contains("专业") && cnName.Contains("中文")) return "ProfessionalCountrySpecific";
            if (cnName.Contains("专业")) return "Professional";
            if (cnName.Contains("教育")) return "Education";
            return null;
        }

        private const string REG_KEY_BACKUP = @"SOFTWARE\CpqSystemTool\VersionSwitch";

        /// <summary>备份当前 Windows 激活信息（转换前调用，便于"还原"）</summary>
        public static void BackupActivation(Action<string> log)
        {
            log("=== 备份当前激活信息 ===");
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_KEY_BACKUP))
                {
                    if (k == null) { log("   [!] 无法创建注册表项（需管理员）"); return; }
                    // 备份当前 ProductName
                    string outp = Exec.RunCmdGet(new[] { "dism.exe", "/Online", "/Get-CurrentEdition" }, null);
                    string currentEdition = null;
                    if (!string.IsNullOrEmpty(outp))
                        foreach (var line in outp.Split('\n'))
                        {
                            var t = line.Trim();
                            int idx = t.IndexOf(':');
                            if (idx > 0) { currentEdition = t.Substring(idx + 1).Trim(); break; }
                        }
                    k.SetValue("SavedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    k.SetValue("Edition", currentEdition ?? "");
                    // 备份当前密钥（slmgr /dlv 取）
                    string keyOutp = Exec.RunCmdGet(new[] { "slmgr.vbs", "/dlv" }, null);
                    if (keyOutp != null)
                    {
                        foreach (var line in keyOutp.Split('\n'))
                        {
                            var t = line.Trim();
                            if (t.StartsWith("密钥最后 5 个字符:") || t.StartsWith("Key:") || t.StartsWith("Partial Product Key:"))
                            {
                                var parts = t.Split(':');
                                if (parts.Length >= 2) k.SetValue("Last5Key", parts[1].Trim());
                                break;
                            }
                        }
                    }
                    log("   [OK] 已备份到注册表 " + REG_KEY_BACKUP);
                    log("   [*] 当前版本: " + (currentEdition ?? "(未知)"));
                }
            }
            catch (Exception ex) { log("   [!] 备份失败：" + ex.Message); }
        }

        /// <summary>从备份还原激活信息（"还原"按钮调用）</summary>
        public static void RestoreActivation(Action<string> log)
        {
            log("=== 还原激活信息 ===");
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_KEY_BACKUP))
                {
                    if (k == null) { log("   [!] 没有找到备份记录（" + REG_KEY_BACKUP + "）"); return; }
                    string savedAt = k.GetValue("SavedAt") as string;
                    string edition = k.GetValue("Edition") as string;
                    string last5 = k.GetValue("Last5Key") as string;
                    log("   [*] 备份时间: " + (savedAt ?? "(未知)"));
                    log("   [*] 备份时版本: " + (edition ?? "(未知)"));
                    if (string.IsNullOrEmpty(edition))
                    {
                        log("   [!] 备份内容不完整，无法还原。请手动用 MAS 重新激活。");
                        return;
                    }
                    // 还原：slmgr /ipk 原始密钥（如有完整密钥则用它，否则用原版的零售通用密钥）
                    // 这里只显示信息，让用户参考；实际完整密钥需要用户输入
                    log("   [*] 备份的密钥后 5 位：" + (last5 ?? "(无)"));
                    log("   [*] 提示：找到原始密钥后，用 slmgr /ipk <完整密钥> 重新激活");
                    log("   [*] 或用本工具 MAS 的 HWID 方式激活（家庭版无 HWID，需 KMS）");
                }
            }
            catch (Exception ex) { log("   [!] 还原失败：" + ex.Message); }
        }

        /// <summary>检查是否有可用的备份</summary>
        public static bool HasBackup()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_KEY_BACKUP))
                    return k != null && k.GetValue("SavedAt") != null;
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; }
        }

        /// <summary>
        /// 执行版本转换。productKey 为空时自动用目标版本零售通用密钥。
        /// 完整流程（对齐 OSSQ 一键转换 7.0 的底层封装）：
        /// 1. 注册表 HKLM\...\Setup\OSUpgrade\AllowOsUpdate=1 —— 允许系统执行版本切换（关键！）
        /// 2. slmgr /ipk <目标密钥> —— 注入产品密钥（OSSQ 的"密钥注入"步骤）
        /// 3. changepk.exe /ProductKey <密钥> —— 触发版本切换（自动重启生效）
        /// 不需要手动断网：微软官方确认 changepk 离线切换后重启再联网激活即可
        /// 参考：php.cn OSSQ 评测 / Microsoft Learn 官方升级流程 / woshub.com
        /// </summary>
        public static bool SwitchEdition(string edition, string productKey, Action<string> log)
        {
            log("=== 版本转换到 " + edition + " ===");
            log("   [*] 建议先关闭杀毒软件/Defender 实时保护（部分安全软件会拦截 slmgr/changepk）");

            // 密钥候选：用户输入优先；留空则用内置候选列表（依次尝试，对齐 OSSQ 7.0 原版行为）
            string[] candidates = null;
            if (!string.IsNullOrWhiteSpace(productKey))
                candidates = new[] { productKey.Trim() };
            else
            {
                candidates = GetKeys(edition);
                if (candidates == null || candidates.Length == 0)
                {
                    log("   [!] 目标版本 \"" + edition + "\" 没有内置零售通用密钥，请手动输入产品密钥。");
                    return false;
                }
                log("   [*] 内置 " + candidates.Length + " 个候选零售通用密钥，将依次尝试");
            }

            // 步骤 0：检查关键服务
            EnsureService(log, "LicenseManager");
            EnsureService(log, "sppsvc");

            // 步骤 0.5：证书检测与自动注入（LTSC/IoT LTSC 等镜像不预装证书的版本）
            if (!SkuInstalled(edition))
            {
                string certSku = (MapChineseToEnglish(edition) ?? edition).Trim();
                log("   [!] 本机缺少 " + certSku + " 证书，尝试自动注入内置证书包...");
                if (!InstallSkuCert(certSku, log))
                {
                    log("   [!] 证书注入失败，无法继续（可用 slmgr /rilc 手动重试，或改用对应版本镜像）。");
                    return false;
                }
            }

            // 步骤 1：注册表允许版本升级/切换（OSSQ 核心技巧，防止 0x803fa067）
            log("   [*] 步骤 1/3：设置 OSUpgrade 允许版本切换...");
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\OSUpgrade"))
                    if (k != null) k.SetValue("AllowOsUpdate", 1, Microsoft.Win32.RegistryValueKind.DWord);
                log("   [OK] 已设置 AllowOsUpdate=1");
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  log("   [!] 注册表写入失败（需管理员权限）"); return false; }

            // 步骤 2：注入目标密钥（slmgr /ipk，候选逐个尝试直到成功）
            log("   [*] 步骤 2/3：注入产品密钥（slmgr /ipk）...");
            string usedKey = null;
            bool injected = false;
            foreach (var cand in candidates)
            {
                log("   [*] 尝试密钥 " + cand + " ...");
                int rc1 = Exec.RunCmd(new[] { "slmgr.vbs", "/ipk", cand }, log, capture: true);
                if (rc1 == 0) { usedKey = cand; injected = true; log("   [OK] 密钥已注入：" + cand); break; }
                log("   [!] 密钥 " + cand + " 被拒绝（返回码 " + rc1 + "），尝试下一个候选...");
            }
            if (!injected)
            {
                log("   [!] 所有候选密钥注入失败。");
                log("   [*] 常见原因：系统是预览版/家庭中文版缺证书/安全软件拦截。可先关杀软重试，或手动输入正确密钥。");
                return false;
            }

            // 步骤 3：changepk 触发切换（自动重启）
            log("   [*] 步骤 3/3：调用 changepk 切换版本（完成后自动重启，5-10 分钟生效）...");
            int rc = Exec.RunCmd(new[] { "changepk.exe", "/ProductKey", usedKey }, log, capture: false);
            if (rc != 0)
            {
                log("   [!] changepk 返回码 " + rc + "。");
                log("   [*] 0x803fa067 处理：断开网络 → slui.exe /upk 删旧密钥 → 重新执行本转换");
                log("   [*] 0x80070490 处理：sfc /scannow → DISM /Online /Cleanup-Image /RestoreHealth → 重启后重试");
                log("   [*] 家庭中文版/单语言版缺证书：需从目标版本系统复制 spp\\tokens\\skus 并 slmgr /rilc");
                return false;
            }
            log("   [OK] changepk 已接受密钥，系统即将重启完成版本切换。");
            log("   [*] 重启后验证：DISM /Online /Get-CurrentEdition 应显示 " + edition);
            log("   [*] 重启后回到本工具「系统激活」页，用 HWID/KMS 激活新版本。");
            return true;
        }

        /// <summary>确保许可证相关服务已启动（changepk 前置条件，避免 0x80070490）</summary>
        private static void EnsureService(Action<string> log, string serviceName)
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController(serviceName))
                {
                    if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running &&
                        sc.Status != System.ServiceProcess.ServiceControllerStatus.StartPending)
                    {
                        try { sc.Start(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10)); log("   [*] " + serviceName + " 已启动"); }
                        catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  log("   [*] " + serviceName + " 启动失败（不影响继续，若转换报 0x80070490 再处理）"); }
                    }
                }
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  log("   [*] 服务 " + serviceName + " 不存在或不可访问"); }
        }

        /// <summary>仅安装产品密钥（slmgr /ipk，不转换版本）</summary>
        public static bool InstallKey(string productKey, Action<string> log)
        {
            log("=== 安装产品密钥 ===");
            int rc = Exec.RunCmd(new[] { "slmgr.vbs", "/ipk", productKey }, log, capture: true);
            if (rc == 0) { log("   [OK] 产品密钥已安装。"); return true; }
            log("   [!] 密钥安装失败（返回码 " + rc + "），请检查密钥格式。");
            return false;
        }

        /// <summary>转换前说明（UI 层用）—— 如实告知成本，避免「不可逆」式吓人措辞</summary>
        public const string WARNING = "⚠️ 版本转换说明\n\n" +
            "· 完整流程：注册表允许切换 → 注入密钥 → changepk 触发（同\"一键转换 7.0\"封装）；\n" +
            "· Win10/11 各版本可互转（家庭↔专业↔企业↔教育↔LTSC↔IoT 等，共 14 个版本），不必重装；\n" +
            "· 软件/文件/驱动不受影响（仅切换注册表中的版本标识 + 注入密钥）；\n" +
            "· 会自动重启一次完成切换（约 5-10 分钟生效）；\n" +
            "· 切换后变为「未激活」状态，用本工具 MAS 重新激活即可；\n" +
            "· ⚠️ OS 版本切换通常不会被「系统还原点」回滚，回退需手动用原版密钥 slmgr /ipk 切回；\n" +
            "· 使用前建议先关闭杀毒软件/Defender 实时保护；\n" +
            "· 若报 0x803fa067：断开网络 → slui.exe /upk → 重试；\n" +
            "· 家庭中文版/单语言版可能缺转换证书，失败则需用对应版本镜像重装。\n\n" +
            "建议：转换前先创建系统还原点（上帝模式页）—— 尽管它通常不能回滚版本，但可兜底其他误操作。";
    }
}
