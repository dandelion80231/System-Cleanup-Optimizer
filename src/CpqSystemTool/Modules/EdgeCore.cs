using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
