using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public static class EdgeCore
    {
        // === 版本检测 ===
        public static string GetEdgeVersion(string channel)
        {
            string subKey;
            switch (channel)
            {
                case "stable": subKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge"; break;
                case "beta":   subKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Beta"; break;
                case "dev":    subKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Dev"; break;
                case "canary": subKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Canary"; break;
                case "sxs":    subKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge SxS"; break;
                default: return "未知";
            }
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    if (key == null) return "未安装";
                    return key.GetValue("Version")?.ToString() ?? "未安装";
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return "未安装"; }
        }

        public static string GetWebView2Version()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView"))
                {
                    if (key == null)
                    {
                        using (var key2 = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView"))
                        {
                            return key2?.GetValue("Version")?.ToString() ?? "未安装";
                        }
                    }
                    return key.GetValue("Version")?.ToString() ?? "未安装";
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return "未安装"; }
        }

        // Issue 6: 当前用户级别 WebView2（分开显示在 UI 上）
        public static string GetWebView2CurrentUserVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView"))
                    return key?.GetValue("Version")?.ToString() ?? "未安装";
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return "未安装"; }
        }

        // === WebView2 安装/卸载 ===
        public static void InstallWebView2(Action<string> log)
        {
            log("正在下载 WebView2 Runtime...");
            Exec.RunPowerShell("Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile \"$env:TEMP\\MicrosoftEdgeWebview2Setup.exe\"", log);
            log("正在安装 WebView2 Runtime...");
            Exec.RunCmd(new[] { "cmd", "/c", $"\"{Environment.GetEnvironmentVariable("TEMP")}\\MicrosoftEdgeWebview2Setup.exe\"", "/silent", "/install" }, log);
            log("WebView2 Runtime 安装/升级完成");

            // 同步就地补上单文件分发所需的 WebView2 探针托管依赖（NuGet 运行时拉取）。
            WebView2ProbeDeps.EnsureWebView2ProbeDeps(log, p => log(WebView2ProbeDeps.ProgressLine(p)));
        }

        public static void UninstallWebView2(Action<string> log)
        {
            log("正在卸载 WebView2 Runtime...");
            // cmd 不会解析路径中间的 *.* 通配符（%ProgramFiles(x86)%\...\Application\*.*\Installer\setup.exe），
            // 改为在 C# 枚举真实存在的 setup.exe 逐个调用，避免静默 no-op。
            string baseDir = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application");
            bool found = false;
            if (Directory.Exists(baseDir))
            {
                foreach (var verDir in Directory.GetDirectories(baseDir))
                {
                    string setup = Path.Combine(verDir, "Installer", "setup.exe");
                    if (File.Exists(setup))
                    {
                        found = true;
                        Exec.RunCmd(new[] { setup, "--uninstall", "--force-uninstall", "--system-level" }, log);
                    }
                }
            }
            if (!found) log("  [!] 未找到 WebView2 Runtime 安装目录（可能已卸载）");
            else log("WebView2 Runtime 卸载完成");
        }

        /// <summary>
        /// 修复 / 重装 WebView2 Runtime：先下载官方 Evergreen Bootstrapper 执行静默安装，
        /// 若引导程序因"已安装"而 no-op、注册表仍未恢复，则扫描磁盘上实际存在的运行时目录并补全注册表指针。
        /// 这是应对此前本机"二进制完好但 EdgeWebView\Applications 注册键缺失"导致 EnsureCoreWebView2Async 挂起的根治方案。
        /// </summary>
        public static void RepairWebView2(Action<string> log)
        {
            log("=== 修复 / 重装 WebView2 Runtime ===");
            string bootstrapper = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "MicrosoftEdgeWebViewModelInstaller.exe");

            try
            {
                log("正在下载官方 WebView2 Evergreen Bootstrapper…");
                Exec.RunPowerShell(
                    $"Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile {Exec.QuotePS(bootstrapper)}",
                    log);
            }
            catch (Exception ex)
            {
                log("[!] 下载引导程序失败: " + ex.Message);
                return;
            }

            // 安全加固：下载后校验文件存在且非空，避免后续对损坏/截断的引导程序静默执行
            bool bootstrapperOk = false;
            try { bootstrapperOk = File.Exists(bootstrapper) && new FileInfo(bootstrapper).Length > 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            if (!bootstrapperOk)
            {
                log("[!] 下载后引导程序文件缺失或损坏（为空），无法继续。");
                return;
            }

            log("正在运行引导程序（/silent /install）…");
            int exitCode = Exec.RunCmd(new[] { "cmd", "/c", $"\"{bootstrapper}\"", "/silent", "/install" }, log);
            log($"[*] 引导程序退出码: {exitCode}");

            // 注册表指针修复（应对“二进制完好但注册键缺失”的场景）。
            if (!IsWebView2RegistryHealthy())
            {
                log("[!] 引导程序未恢复注册表（可能判定已安装而跳过），尝试扫描磁盘并修复注册表指针…");
                RepairWebView2RegistryFromDisk(log);
            }

            // 终验：不仅看注册表，更要看运行时实际文件是否完整。
            // 注意：现代 Edge 主二进制名为 msedge.dll（旧版才叫 chrome.dll），目录里本来就没有 chrome.dll。
            // 真正决定 WebView2 能否初始化的核心文件是 msedgewebview2.exe + msedge.dll。
            var runtimeRoot = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application");
            bool filesComplete = false;
            if (Directory.Exists(runtimeRoot))
            {
                var vd = Directory.GetDirectories(runtimeRoot).OrderByDescending(d => d).FirstOrDefault();
                if (vd != null)
                    filesComplete = File.Exists(Path.Combine(vd, "msedgewebview2.exe"))
                        && File.Exists(Path.Combine(vd, "msedge.dll"));
            }
            if (filesComplete)
                log("[✓] WebView2 Runtime 文件已恢复完整，建议重启本程序后重试 WebView2 探针。");
            else
                log("[!] WebView2 运行时文件仍不完整（msedgewebview2.exe 或 msedge.dll 缺失）。本机 WebView2 由 Microsoft Edge 提供，且微软载荷 CDN 不可达 / 同版本不修复，自动修复无效。请手动从微软官网下载并重新安装 Microsoft Edge（或运行 Windows 修复），再重试。");

            // 同步就地补上单文件分发所需的 WebView2 探针托管依赖（NuGet 运行时拉取）。
            WebView2ProbeDeps.EnsureWebView2ProbeDeps(log, p => log(WebView2ProbeDeps.ProgressLine(p)));
        }

        /// <summary>检测 WebView2 运行时注册表根键是否健康（EdgeWebView\Applications 存在且有子键）。</summary>
        private static bool IsWebView2RegistryHealthy()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeWebView\Applications"))
                    return key != null && key.SubKeyCount > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 扫描磁盘上 WebView2 Runtime 的实际安装目录，并把找到的版本目录写入注册表 Applications 下。
        /// 这是 CreateAsync(null) 能正常解析运行时的最小必要注册信息。
        /// </summary>
        private static void RepairWebView2RegistryFromDisk(Action<string> log)
        {
            string runtimeRoot = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application");
            string edgeRoot = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application");
            string foundVer = null;
            string foundDir = null;

            // 优先扫描 WebView2 Runtime 目录下的版本子目录
            if (Directory.Exists(runtimeRoot))
            {
                foreach (var d in Directory.GetDirectories(runtimeRoot).OrderByDescending(d => d))
                {
                    if (File.Exists(Path.Combine(d, "msedgewebview2.exe")))
                    {
                        foundDir = d;
                        foundVer = Path.GetFileName(d);
                        break;
                    }
                }
            }

            // 兜底：扫描完整 Edge 目录
            if (foundDir == null && Directory.Exists(edgeRoot))
            {
                foreach (var d in Directory.GetDirectories(edgeRoot).OrderByDescending(d => d))
                {
                    if (File.Exists(Path.Combine(d, "msedge.exe")) && File.Exists(Path.Combine(d, "msedgewebview2.exe")))
                    {
                        foundDir = d;
                        foundVer = Path.GetFileName(d);
                        break;
                    }
                }
            }

            if (foundDir == null)
            {
                log("[!] 未在磁盘上找到可用的 WebView2 Runtime 目录，无法修复注册表。");
                return;
            }

            log("[*] 找到运行时目录: " + foundDir);

            // 写 32 位视图（WOW6432Node）——这是 .NET 4.8 x64 进程通过 32 位 WebView2 loader 读取的位置。
            WriteWebView2AppRegistration(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeWebView\Applications", foundVer, foundDir, log);
            // 同时补 64 位视图，便于其他 64 位进程/未来使用。
            WriteWebView2AppRegistration(Registry.LocalMachine, @"SOFTWARE\Microsoft\EdgeWebView\Applications", foundVer, foundDir, log);
        }

        private static void WriteWebView2AppRegistration(RegistryKey hive, string basePath, string version, string dir, Action<string> log)
        {
            try
            {
                using (var baseKey = hive.CreateSubKey(basePath))
                {
                    if (baseKey == null)
                    {
                        log("[!] 无法创建/打开注册表项: " + basePath);
                        return;
                    }
                    using (var verKey = baseKey.CreateSubKey(version))
                    {
                        verKey?.SetValue("", dir);
                        verKey?.SetValue("pv", version);
                    }
                }
                log("[*] 已写入注册表: " + basePath + "\\" + version);
            }
            catch (Exception ex)
            {
                log("[!] 写入注册表失败: " + basePath + " — " + ex.Message);
            }
        }

        // === 安装 Edge ===
        public static void InstallEdge(string channel, Action<string> log)
        {
            string url;
            switch (channel)
            {
                case "stable": url = "https://c2rsetup.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeStablePage&Channel=Stable&language=zh-cn&brand=M100"; break;
                case "beta":   url = "https://c2rsetup.edog.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeInsiderPage&Channel=Beta&language=zh-cn"; break;
                case "dev":    url = "https://c2rsetup.edog.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeInsiderPage&Channel=Dev&language=zh-cn"; break;
                default: log("未知频道"); return;
            }
            log($"正在下载 Edge {channel}...");
            Exec.RunPowerShell($"Invoke-WebRequest -Uri '{url}' -OutFile \"$env:TEMP\\MicrosoftEdgeSetup.exe\"", log);
            log("正在安装...");
            Exec.RunCmd(new[] { "cmd", "/c", $"\"{Environment.GetEnvironmentVariable("TEMP")}\\MicrosoftEdgeSetup.exe\"", "/silent", "/install" }, log);
            log($"Edge {channel} 安装完成");
        }

        // === 卸载 Edge ===
        public static void UninstallEdge(string channel, bool forceClean, Action<string> log)
        {
            string uninstallKeyPath;
            string regPath;
            string displayName;

            switch (channel)
            {
                case "stable":
                    uninstallKeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge";
                    regPath = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}";
                    displayName = "Microsoft Edge";
                    break;
                case "beta":
                    uninstallKeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Beta";
                    regPath = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}";
                    displayName = "Microsoft Edge Beta";
                    break;
                default: log("未知频道"); return;
            }

            log($"=== 卸载 Edge {channel} ===");

            // 结束进程
            Exec.RunCmd(new[] { "taskkill", "/f", "/im", "msedge.exe" }, log);

            // 读卸载字符串
            string uninstallString = null;
            try
            {
                // 注意：uninstallKeyPath 已是 "...\Uninstall\Microsoft Edge" 完整键路径，
                // 不要再拼 displayName（会变成不存在的重复路径），直接打开该键读取。
                using (var key = Registry.LocalMachine.OpenSubKey(uninstallKeyPath))
                    uninstallString = key?.GetValue("UninstallString")?.ToString();
                if (uninstallString == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        var loc = key?.GetValue("location")?.ToString();
                        if (loc != null)
                        {
                            var setupPath = Path.Combine(loc, "..", "..", "Install", "setup.exe");
                            if (File.Exists(setupPath))
                                uninstallString = $"\"{Path.GetFullPath(setupPath)}\" --uninstall --system-level --verbose-logging --force-uninstall";
                        }
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

            if (string.IsNullOrEmpty(uninstallString))
            {
                log("未找到卸载信息，使用强制卸载");
                ForceCleanupEdge(displayName, log);
                return;
            }

            log("执行卸载命令: " + uninstallString);
            Exec.RunCmd(new[] { "cmd", "/c", uninstallString }, log);

            if (forceClean)
            {
                log("执行强制清理...");
                ForceCleanupEdge(displayName, log);
            }

            log($"Edge {channel} 卸载完成");
        }

        /// <summary>
        /// 待删目录是否为明确的 Edge 目录：展开环境变量并规范化后，路径必须包含
        /// Microsoft\Edge 或 Microsoft\EdgeWebView（分隔符统一为 '\'，大小写不敏感）。
        /// 强制清理用的是 rd /S /Q 递归删除，一旦环境变量展开失败或路径拼接异常就会删错目录，
        /// 因此这里先做身份校验，缺少 Edge 标识的路径一律不删（防止误删用户数据）。
        /// </summary>
        private static bool IsEdgeInstallPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            if (fullPath.IndexOf('%') >= 0) return false;   // 环境变量未展开（路径不可信），视为无效
            string norm;
            try { norm = Path.GetFullPath(fullPath).Replace('/', '\\'); }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); return false; }
            return norm.IndexOf(@"\microsoft\edge", StringComparison.OrdinalIgnoreCase) >= 0
                || norm.IndexOf(@"\microsoft\edgewebview", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ForceCleanupEdge(string displayName, Action<string> log)
        {
            // 防误删校验：删除前先把待删目录展开成真实路径并逐个校验 Edge 标识。
            // 必需的 Edge 安装目录校验不过 → 整体中止并记录日志；附属缓存目录校验不过 → 仅跳过该项。
            var targets = new[]
            {
                new { Path = @"%ProgramFiles(x86)%\Microsoft\Edge",     Required = true  },
                new { Path = @"%ProgramFiles(x86)%\Microsoft\EdgeCore", Required = true  },
                new { Path = @"%LocalAppData%\Microsoft\Edge",          Required = true  },
                new { Path = @"%ProgramFiles(x86)%\Microsoft\Temp",     Required = false }
            };
            var dirs = new List<string>();
            foreach (var t in targets)
            {
                string full = Exec.ExpandEnv(t.Path);
                if (IsEdgeInstallPath(full)) { dirs.Add(full); continue; }
                if (t.Required)
                {
                    log("  [!] 强制清理已中止：待删路径缺少 Edge 标识（防止误删用户数据）: " + (string.IsNullOrEmpty(full) ? t.Path : full));
                    return;
                }
                log("  [SKIP] 非 Edge 目录，跳过删除（防止误删用户数据）: " + (string.IsNullOrEmpty(full) ? t.Path : full));
            }

            // 清理安装目录
            foreach (var d in dirs)
                Exec.RunCmd(new[] { "cmd", "/c", "rd /S /Q \"" + d + "\"" }, log);

            // 清理注册表
            using (var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet", true)) k1?.DeleteSubKeyTree(displayName, false);
            using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true)) k2?.DeleteSubKeyTree(displayName, false);
            using (var classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", true))
            {
                classes?.DeleteSubKeyTree("MSEdgeHTM", false);
                classes?.DeleteSubKeyTree("MSEdgePDF", false);

                // 清理 EdgeUpdate 相关键
                foreach (var name in new[] { "MicrosoftEdgeUpdate.", "microsoft-edge" })
                    classes?.DeleteSubKeyTree(name, false);
            }

            log("强制清理完成");
        }

        // === 禁止 Edge 自动更新 ===
        public static void BlockEdgeUpdate(Action<string> log)
        {
            log("=== 禁止 Edge + WebView2 自动更新 ===");

            // 删除更新服务
            foreach (var svc in new[] { "edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService" })
            {
                Exec.RunCmd(new[] { "sc", "stop", svc }, log);
                Exec.RunCmd(new[] { "sc", "delete", svc }, log);
            }

            // 删除更新计划任务
            Exec.RunPowerShell("Get-ScheduledTask -TaskPath '\\Microsoft\\EdgeUpdate\\' -ErrorAction SilentlyContinue | Disable-ScheduledTask | Out-Null", log);
            Exec.RunPowerShell("schtasks /delete /tn 'MicrosoftEdgeUpdateTask' /f 2>nul", log);

            // 删除更新目录
            Exec.RunCmd(new[] { "cmd", "/c", "rd /S /Q \"%ProgramFiles(x86)%\\Microsoft\\EdgeUpdate\\\"" }, log);

            // 删除更新注册表
            using (var k3 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate", true)) k3?.DeleteSubKeyTree("", false);

            // 阻止 Edge 启动增强（同时写 HKCU 与 HKLM，确保任一 hive 下都生效）
            RegistryHelper.SetEdgePolicy("StartupBoostEnabled", 0, log);
            RegistryHelper.SetEdgePolicy("BackgroundModeEnabled", 0, log);

            log("Edge 自动更新已禁止");
        }

        // === 恢复 Edge 更新 ===
        public static void RestoreEdgeUpdate(Action<string> log)
        {
            log("=== 恢复 Edge 自动更新 ===");
            // 同时清 HKCU 与 HKLM（含 Recommended 子键），避免残留于任一 hive 导致 edge://management 仍报「由组织管理」
            RegistryHelper.DeleteEdgePolicyTree(log);
            log("已清除 Edge 组策略限制");
        }

        // === Edge 启动增强 ===
        public static bool IsStartupBoostEnabled()
        {
            try
            {
                // 任一 hive（HKCU/HKLM）将 StartupBoostEnabled 显式置 0 即视为已禁用
                foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using (var key = hive.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
                    {
                        if (key?.GetValue("StartupBoostEnabled") is int val && val == 0) return false;
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return true; // 默认启用
        }

        public static void SetStartupBoost(bool enabled, Action<string> log)
        {
            if (enabled)
                // 恢复启动增强：两 hive 都清（含 Recommended 子键），移除 StartupBoostEnabled/BackgroundModeEnabled 等限制
                RegistryHelper.DeleteEdgePolicyTree(log);
            else
            {
                RegistryHelper.SetEdgePolicy("StartupBoostEnabled", 0, log);
                RegistryHelper.SetEdgePolicy("BackgroundModeEnabled", 0, log);
            }
        }

        // === Edge 实验性 Flags（edge://flags，注册表 HKCU\Software\Microsoft\Edge\EdgeFlags）===
        public const string EdgeFlagsRegPath = @"Software\Microsoft\Edge\EdgeFlags";

        /// <summary>读取指定 flag 当前值；未设置（Default）返回 null。</summary>
        public static string GetEdgeFlag(string flagName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(EdgeFlagsRegPath))
                    return key?.GetValue(flagName) as string;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); return null; }
        }

        /// <summary>设置 flag 值（"1"/"0"/枚举字符串）。</summary>
        public static void SetEdgeFlag(string flagName, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(EdgeFlagsRegPath))
                    key?.SetValue(flagName, value ?? "", Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
        }

        /// <summary>删除 flag（= 恢复 Edge 默认 Default）。</summary>
        public static void ClearEdgeFlag(string flagName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(EdgeFlagsRegPath, true))
                    key?.DeleteValue(flagName, false);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
        }

        /// <summary>供 UI 展示的 flag 元数据：Key=注册表值名，Label=界面中文名，Values=可选值（首元素为默认值），Recommend=推荐值（对应 Values 元素，UI 上标 ⭐）。</summary>
        public static readonly (string Key, string Label, string[] Values, string Recommend)[] EdgeFlagDefs =
        {
            ("use-angle", "ANGLE 图形后端", new[] { "gl", "d3d11", "d3d11on12", "vulkan", "swiftshader" }, "default"),
            ("edge-copilot-mode", "Edge Copilot 模式", new[] { "disabled", "enabled", "optin" }, "disabled"),
            ("enable-parallel-downloading", "并行下载", new[] { "1", "0" }, "1"),
            ("enable-gpu-rasterization", "GPU 栅格化", new[] { "1", "0" }, "1"),
            ("enable-accelerated-video-decode", "硬件加速视频解码", new[] { "1", "0" }, "1"),
            ("enable-quic", "QUIC / HTTP/3 协议", new[] { "1", "0" }, "1"),
            ("back-forward-cache", "前进/后退缓存", new[] { "1", "0" }, "1"),
            ("smooth-scrolling", "平滑滚动", new[] { "1", "0" }, "1"),
            ("enable-tls13-early-data", "TLS 1.3 Early Data", new[] { "1", "0" }, "1"),
            ("enable-force-dark", "强制深色模式", new[] { "1", "0" }, "0"),
            ("edge-overlay-scrollbars-win-style", "Fluent 悬浮滚动条", new[] { "1", "0" }, "1"),
        };

        /// <summary>按元数据写 flag：value 为 null/空串/字面量 "default"（=Edge 出厂默认语义）时删除注册表值，否则显式写入。
        /// ⚠ 不得用 value==Values[0] 判断默认：Values[0] 只是 Edge 出厂值（如开关类 "1"），显式写 "1" 仍是「启用」而非「默认」——
        /// 否则一键优化推荐值 =Values[0] 的 9 项会被误清（曾致注册表只写入 enable-force-dark 一项）。</summary>
        public static void ApplyEdgeFlag(string key, string value)
        {
            bool isDefault = string.IsNullOrEmpty(value) || value == "default";
            if (isDefault) ClearEdgeFlag(key);
            else SetEdgeFlag(key, value);
        }

        /// <summary>把所有 flags 一次性设为 EdgeFlagDefs 中的推荐值。返回应用成功的项数；逐一记日志。</summary>
        public static int ApplyAllRecommendedFlags(Action<string> log)
        {
            int ok = 0;
            foreach (var def in EdgeFlagDefs)
            {
                ApplyEdgeFlag(def.Key, def.Recommend);
                log?.Invoke("⚡ 已设推荐值：" + def.Label + " = " + def.Recommend);
                ok++;
            }
            return ok;
        }

        /// <summary>清除 EdgeFlags 注册表下本程序管理的所有 11 项（恢复 Edge 默认）。</summary>
        public static int ClearAllEdgeFlags(Action<string> log)
        {
            int ok = 0;
            foreach (var def in EdgeFlagDefs)
            {
                ClearEdgeFlag(def.Key);
                log?.Invoke("↩ 已恢复默认：" + def.Label);
                ok++;
            }
            return ok;
        }

        /// <summary>强制结束所有 msedge.exe 进程并重新启动（让 flags 立即生效）。返回是否成功重启。
        /// 注意：会丢失 Edge 未保存的标签页/表单——UI 端必须先弹确认。</summary>
        public static bool ForceRestartEdge(Action<string> log)
        {
            try
            {
                // 1. 结束所有 msedge.exe（包括 helper 进程）
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM msedge.exe /T",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(5000); // 最多等 5 秒
                }
                System.Threading.Thread.Sleep(800); // 缓冲：等进程彻底退出
                log?.Invoke("✓ 已结束所有 Edge 进程");

                // 2. 重新启动 Edge（PATH 解析 msedge.exe；无参 = 默认用户 profile）
                Process.Start(new ProcessStartInfo
                {
                    FileName = "msedge.exe",
                    UseShellExecute = true
                });
                log?.Invoke("→ 已启动新 Edge 进程（flags 生效）");
                return true;
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                log?.Invoke("✗ 重启 Edge 失败：" + caughtEx.Message);
                return false;
            }
        }
    }
}
