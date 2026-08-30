using System;
using System.ServiceProcess;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public static class PrivacyCore
    {
        private static readonly RegistryKey HKLM = Registry.LocalMachine;
        private static readonly RegistryKey HKCU = Registry.CurrentUser;

        // 隐私设置枚举
        public static void DisableCloudSearch(Action<string> log)
        {
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCloudSearch", 0, log);
            log("[OK] 已禁止云内容搜索");
        }
        public static void EnableCloudSearch(Action<string> log)
        {
            RegistryHelper.DeleteKeyTree(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", log);
            log("[OK] 已恢复云内容搜索");
        }
        public static bool IsCloudSearchDisabled()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search"))
                return k?.GetValue("AllowCloudSearch") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        public static void DisableWebSearch(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, log);
            log("[OK] 已禁止Web搜索");
        }
        public static void EnableWebSearch(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1, log);
            log("[OK] 已恢复Web搜索");
        }
        public static bool IsWebSearchDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search"))
                return k?.GetValue("BingSearchEnabled") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        public static void DisableAdvertisingID(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, log);
            log("[OK] 已禁用广告ID");
        }
        public static void EnableAdvertisingID(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, log);
            log("[OK] 已启用广告ID");
        }
        public static bool IsAdIDDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo"))
                return k?.GetValue("Enabled") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        public static bool IsTelemetryDisabled()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                return k?.GetValue("AllowTelemetry") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }
        public static void DisableTelemetry(Action<string> log)
        {
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, log);
            log("[OK] 已禁用遥测");
        }
        public static void EnableTelemetry(Action<string> log)
        {
            RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", log);
            log("[OK] 已恢复遥测");
        }

        public static void DisableDeliveryOptimization(Action<string> log)
        {
            // 设为0 = 关闭传递优化
            string sid = "S-1-5-20";
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DownloadMode", 0, log);
            // HKEY_USERS 下可能不存在该 SID 的加载配置（如 NETWORK SERVICE 通常未加载用户配置），必须判空避免 NRE
            using (var users = Registry.Users.OpenSubKey(sid, true))
            {
                if (users == null)
                    log("  [*] HKEY_USERS\\" + sid + " 未加载，跳过用户级传递优化设置。");
                else
                    RegistryHelper.SetDword(users, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization", "DownloadMode", 0, log);
            }
            log("[OK] 已禁用传递优化");
        }
        public static void EnableDeliveryOptimization(Action<string> log)
        {
            RegistryHelper.DeleteKeyTree(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", log);
            log("[OK] 已恢复传递优化");
        }
        public static bool IsDeliveryOptimizationDisabled()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config"))
                return k?.GetValue("DownloadMode") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        public static bool IsActivityHistoryDisabled()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System"))
                return k?.GetValue("EnableActivityFeed") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }
        public static void DisableActivityHistory(Action<string> log)
        {
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, log);
            log("[OK] 已禁用活动历史");
        }
        public static void EnableActivityHistory(Action<string> log)
        {
            RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", log);
            log("[OK] 已恢复活动历史");
        }

        // 禁止Windows大版本更新（留至2042）
        public static void BlockFeatureUpdate(Action<string> log)
        {
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersion", 1, log);
            RegistryHelper.SetSz(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersionInfo", "24H2", log);
            log("[OK] 已锁定当前大版本（禁止升级到更新版本）");
        }
        public static void UnblockFeatureUpdate(Action<string> log)
        {
            RegistryHelper.DeleteKeyTree(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", log);
            log("[OK] 已解除版本锁定");
        }
        public static bool IsFeatureUpdateBlocked()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"))
                return k?.GetValue("TargetReleaseVersion") is int v && v == 1; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // === Issue 8: 补齐隐私设置（对齐 Win11EasyConfig Form5）===

        // 禁止本地存储搜索历史记录
        public static void DisableSearchHistory(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 0, log);
            log("[OK] 已禁止本地存储搜索历史记录");
        }
        public static void EnableSearchHistory(Action<string> log)
        {
            RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", log);
            log("[OK] 已恢复搜索历史记录");
        }
        public static bool IsSearchHistoryDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings"))
                return k?.GetValue("IsDeviceSearchHistoryEnabled") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // WSearch 服务停止/恢复
        public static void BlockWSearchService(Action<string> log)
        {
            log("停止并禁止 Windows Search 服务...");
            try
            {
                using (var sc = new ServiceController("WSearch"))
                {
                    if (sc.Status == ServiceControllerStatus.Running) sc.Stop();
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            Exec.RunCmd(new[] { "sc", "config", "WSearch", "start=disabled" }, log);
            log("[OK] Windows Search 服务已禁用");
        }
        public static void AllowWSearchService(Action<string> log)
        {
            log("恢复并允许 Windows Search 服务...");
            Exec.RunCmd(new[] { "sc", "config", "WSearch", "start=delayed-auto" }, log);
            Exec.RunCmd(new[] { "sc", "start", "WSearch" }, log);
            log("[OK] Windows Search 服务已恢复");
        }
        public static bool IsWSearchServiceBlocked()
        {
            try
            {
                using (var sc = new ServiceController("WSearch"))
                    return sc.StartType == ServiceStartMode.Disabled;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // 添加防火墙规则阻止 SearchHost.exe 联网
        public static void AddSearchFirewallRule(Action<string> log)
        {
            log("添加阻止 Windows 搜索联网的防火墙规则...");
            // 修正：原先丢弃 RunPowerShell 的退出码，规则添加失败也照样打印 [OK]
            int rc = Exec.RunPowerShell("Remove-NetFirewallRule -DisplayName '阻止Windows搜索联网' -ErrorAction SilentlyContinue;" +
                "New-NetFirewallRule -DisplayName '阻止Windows搜索联网' -Direction Outbound " +
                "-Program \"$env:SystemRoot\\SystemApps\\Microsoft.Windows.Search_cw5n1h2txyewy\\SearchHost.exe\" -Action Block", log);
            if (rc == 0) log("[OK] 防火墙规则已添加");
            else log("[FAIL] 防火墙规则添加失败（退出码 " + rc + "）");
        }
        public static void RemoveSearchFirewallRule(Action<string> log)
        {
            log("移除阻止 Windows 搜索联网的防火墙规则...");
            // 修正：同 AddSearchFirewallRule，原先丢弃退出码，移除失败也打印 [OK]
            int rc = Exec.RunPowerShell("Remove-NetFirewallRule -DisplayName '阻止Windows搜索联网' -ErrorAction SilentlyContinue", log);
            if (rc == 0) log("[OK] 防火墙规则已移除");
            else log("[FAIL] 防火墙规则移除失败（退出码 " + rc + "）");
        }
        public static bool IsSearchFirewallRulePresent()
        {
            string outp = Exec.RunPowerShellGet("Get-NetFirewallRule -DisplayName '阻止Windows搜索联网' -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count", null);
            return outp?.Trim() == "1";
        }

        // 墨迹书写和键入词典
        public static void DisableInkDict(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, log);
            log("[OK] 已禁用墨迹书写和键入词典");
        }
        public static void EnableInkDict(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 1, log);
            log("[OK] 已启用墨迹书写和键入词典");
        }
        public static bool IsInkDictDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Personalization\Settings"))
                return k?.GetValue("AcceptedPrivacyPolicy") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // 应用启动跟踪
        public static void DisableAppStartTracking(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0, log);
            log("[OK] 已禁止跟踪应用启动");
        }
        public static void EnableAppStartTracking(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 1, log);
            log("[OK] 已启用跟踪应用启动");
        }
        public static bool IsAppStartTrackingDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                return k?.GetValue("Start_TrackProgs") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // 网站语言列表
        public static void DisableLanguageList(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, log);
            log("[OK] 已禁止网站使用语言列表");
        }
        public static void EnableLanguageList(Action<string> log)
        {
            RegistryHelper.DeleteValue(HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", log);
            log("[OK] 已恢复网站使用语言列表");
        }
        public static bool IsLanguageListDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"Control Panel\International\User Profile"))
                return k?.GetValue("HttpAcceptLanguageOptOut") is int v && v == 1; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // 设置应用建议内容（3 个 SubscribedContent-*）
        public static void DisableSuggestedContent(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", 0, log);
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0, log);
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0, log);
            log("[OK] 已禁止设置应用建议内容");
        }
        public static void EnableSuggestedContent(Action<string> log)
        {
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", 1, log);
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 1, log);
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 1, log);
            log("[OK] 已恢复设置应用建议内容");
        }
        public static bool IsSuggestedContentDisabled()
        {
            try { using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"))
                return k?.GetValue("SubscribedContent-338393Enabled") is int v && v == 0; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // Windows 更新不包括恶意软件删除工具 (MRT)
        public static void DisableMRTUpdate(Action<string> log)
        {
            RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\MRT", "DontOfferThroughWUAU", 1, log);
            log("[OK] 已禁止 MRT 更新");
        }
        public static void EnableMRTUpdate(Action<string> log)
        {
            RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\MRT", "DontOfferThroughWUAU", log);
            log("[OK] 已恢复 MRT 更新");
        }
        public static bool IsMRTUpdateDisabled()
        {
            try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Policies\Microsoft\MRT"))
                return k?.GetValue("DontOfferThroughWUAU") is int v && v == 1; }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // 开始菜单推荐项目显示行数（1/3/4）
        public static void SetStartLayout(int rows, Action<string> log)
        {
            // 修正：原注释写「Start_Layout: 0=关闭(默认), 1=一行, 2=三行(默认), 3=四行」，与实现和微软文档都对不上。
            // 微软文档（Windows 11 设置参考）对 Start_Layout 的定义只有三个值：
            //   0 = Default（默认布局）、1 = More Pins（更多固定项 → 推荐区被压缩为一行）、
            //   2 = More Recommendations（更多推荐 → 推荐区展开为四行）。
            // 实现是有意为之且正确：1 行 → 1（More Pins）、3 行 → 0（Default）、4 行 → 2（More Recommendations）；
            // 其余入参（含 UI 不提供的非法值）走 else 分支回退为 0（默认布局）——原注释把「三行」标成 2，
            // 实际上三行落到 else 分支写的是 0，且值只有 0/1/2 三档，不存在注释里的「3=四行」。
            int val = 0;
            if (rows == 1) val = 1;
            else if (rows == 4) val = 2;
            else val = 0;
            RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_Layout", val, log);
            // 修正：原日志恒称「已设置为 N 行」，但传入 3 以外的值时写的是 0（默认布局），并非 N 行，
            // 会误导用户。改为如实输出实际写入的注册表值。
            if (rows == 1 || rows == 3 || rows == 4)
                log("[OK] 开始菜单布局已设置为 " + rows + " 行推荐（Start_Layout=" + val + "）");
            else
                log("[!] 不支持的行数 " + rows + "，已回退为默认布局（Start_Layout=0；仅支持 1/3/4 行）");
        }
        public static int GetStartLayout()
        {
            try { using (var k = HKCU.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                { var v = k?.GetValue("Start_Layout"); if (v is int i) return i; } }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return 0;
        }
    }
}
