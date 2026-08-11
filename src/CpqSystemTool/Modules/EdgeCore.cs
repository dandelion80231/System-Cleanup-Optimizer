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
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return "未安装"; }
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
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return "未安装"; }
        }

        // Issue 6: 当前用户级别 WebView2（分开显示在 UI 上）
        public static string GetWebView2CurrentUserVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView"))
                    return key?.GetValue("Version")?.ToString() ?? "未安装";
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return "未安装"; }
        }

        // === WebView2 安装/卸载 ===
        public static void InstallWebView2(Action<string> log)
        {
            log("正在下载 WebView2 Runtime...");
            Exec.RunPowerShell("Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile \"$env:TEMP\\MicrosoftEdgeWebview2Setup.exe\"", log);
            log("正在安装 WebView2 Runtime...");
            Exec.RunCmd(new[] { "cmd", "/c", $"\"{Environment.GetEnvironmentVariable("TEMP")}\\MicrosoftEdgeWebview2Setup.exe\"", "/silent", "/install" }, log);
            log("WebView2 Runtime 安装/升级完成");
        }

        public static void UninstallWebView2(Action<string> log)
        {
            log("正在卸载 WebView2 Runtime...");
            Exec.RunCmd(new[] { "cmd", "/c", "%ProgramFiles(x86)%\\Microsoft\\EdgeWebView\\Application\\*.*\\Installer\\setup.exe --uninstall --force-uninstall --system-level" }, log);
            log("WebView2 Runtime 卸载完成");
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
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
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

            if (!File.Exists(bootstrapper))
            {
                log("[!] 下载后引导程序文件缺失，无法继续。");
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
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  }

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

        private static void ForceCleanupEdge(string displayName, Action<string> log)
        {
            // 清理安装目录
            Exec.RunCmd(new[] { "cmd", "/c", $"rd /S /Q \"%ProgramFiles(x86)%\\Microsoft\\Edge\\\"" }, log);
            Exec.RunCmd(new[] { "cmd", "/c", $"rd /S /Q \"%ProgramFiles(x86)%\\Microsoft\\Temp\\\"" }, log);
            Exec.RunCmd(new[] { "cmd", "/c", $"rd /S /Q \"%ProgramFiles(x86)%\\Microsoft\\EdgeCore\\\"" }, log);
            Exec.RunCmd(new[] { "cmd", "/c", $"rd /S /Q \"%LocalAppData%\\Microsoft\\Edge\\\"" }, log);

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

            // 阻止 Edge 启动增强
            RegistryHelper.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, log);
            RegistryHelper.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, log);

            log("Edge 自动更新已禁止");
        }

        // === 恢复 Edge 更新 ===
        public static void RestoreEdgeUpdate(Action<string> log)
        {
            log("=== 恢复 Edge 自动更新 ===");
            RegistryHelper.DeleteKeyTree(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", log);
            log("已清除 Edge 组策略限制");
        }

        // === Edge 启动增强 ===
        public static bool IsStartupBoostEnabled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
                {
                    if (key?.GetValue("StartupBoostEnabled") is int val) return val != 0;
                }
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  }
            return true; // 默认启用
        }

        public static void SetStartupBoost(bool enabled, Action<string> log)
        {
            if (enabled)
                RegistryHelper.DeleteKeyTree(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", log);
            else
                RegistryHelper.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, log);
        }
    }
}
