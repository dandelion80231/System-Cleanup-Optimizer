using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 单个优化项：描述/风险/分组/状态检测与切换。
    /// Enable/Disable 接收 log 回调；State 返回当前是否已"启用"。
    /// 本文件以原版 Windows11 轻松设置 的优化项列表为唯一真相源，
    /// 前 9 个分组 = 原版功能 1:1；最后"更多优化(增强)"组 = 我们在原版之外额外加的实用项。
    /// </summary>
    /// <summary>三态开关状态：Off=关 / On=开 / Default=系统默认（不强制写入，交还系统行为）。</summary>
    internal enum TweakState { Off, On, Default }

    internal class TweakEntry
    {
        public string Group, Id, Name, Desc, Risk;
        public Action<Action<string>> Enable;
        public Action<Action<string>> Disable;
        public Func<bool> State;
        // 三态（可选）：GetState3 与 Apply3 均非空时，UI 自动切到三态 CheckBox（On/Off/系统默认）。
        // Enable/Disable/State 仍保留作为二进制回退（旧配置导入与 ApplyByIds 兼容），
        // 三态项应让它们分别委托到 Apply3(On)/Apply3(Off)/(GetState3()==On)。
        public Func<TweakState> GetState3;
        public Action<TweakState, Action<string>> Apply3;
        public bool IsThreeState => GetState3 != null && Apply3 != null;
    }

    internal static class Tweaks
    {
        public static readonly RegistryKey HKCU = Registry.CurrentUser;
        public static readonly RegistryKey HKLM = Registry.LocalMachine;

        private const string ADV = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string POW = @"SYSTEM\CurrentControlSet\Control\Power";
        private const string SMP = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string WU_POL = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string MRT_POL = @"SOFTWARE\Policies\Microsoft\MRT";
        private const string NT_CV = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        private const string CTRL = @"SYSTEM\ControlSet001\Services";
        private const string STUCK = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
        private const string UCPD_TASK = @"Microsoft\Windows\AppxDeploymentClient\UCPD velocity";
        // 三态项所需的注册表路径常量
        private const string DG = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";                                   // VBS / HVCI(WDAC) / 内存完整性
        private const string MEM = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";            // DEP / Meltdown / 预取
        private const string PREF = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters"; // MaxPrefetchFiles / EnablePrefetcher
        private const string SYS = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";                     // UAC
        private const string RES = @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Reserve Manager";            // 保留存储
        private const string TCP = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";                            // BBR2

        /// <summary>设置 DWORD 并重启资源管理器（封装最常见的"设置+重启"样板）。</summary>
        private static void DwordR(RegistryKey root, string sub, string name, int val, Action<string> log)
        {
            RegistryHelper.SetDword(root, sub, name, val, log);
            RegistryHelper.RestartExplorer(log);
        }

        /// <summary>三态 DWORD 切换：On=onVal / Off=offVal / Default=删除值（交还系统默认）。</summary>
        private static void ApplyDword3(RegistryKey root, string sub, string name, int onVal, int offVal, TweakState st, Action<string> log)
        {
            if (st == TweakState.Default) { RegistryHelper.DeleteValue(root, sub, name, log); return; }
            RegistryHelper.SetDword(root, sub, name, st == TweakState.On ? onVal : offVal, log);
        }

        /// <summary>读取 DWORD 并映射为三态：等于 onVal→On；等于 offVal→Off；其余（含缺省/不存在）→Default。
        /// def 用 -1 哨兵，确保「值不存在」不会误判成 onVal=0 的 On 态。</summary>
        private static TweakState GetDword3(RegistryKey root, string sub, string name, int onVal, int offVal, int def = -1)
        {
            int v = RegistryHelper.GetDword(root, sub, name, def);
            if (v == onVal) return TweakState.On;
            if (v == offVal) return TweakState.Off;
            return TweakState.Default;
        }

        /// <summary>三态 DWORD 开关工厂：一次性绑定 GetState3/Apply3/Enable/Disable/State，消除每处重复的 5 行样板。
        /// On=onVal / Off=offVal / Default=删除值交还系统默认（与 ApplyDword3/GetDword3 语义一致）。</summary>
        private static TweakEntry ThreeStateDword(string group, string id, string name, string desc, string risk,
            RegistryKey root, string sub, string regName, int onVal, int offVal)
        {
            var t = new TweakEntry { Group = group, Id = id, Name = name, Desc = desc, Risk = risk };
            t.GetState3 = () => GetDword3(root, sub, regName, onVal, offVal);
            t.Apply3 = (st, log) => ApplyDword3(root, sub, regName, onVal, offVal, st, log);
            t.Enable = log => ApplyDword3(root, sub, regName, onVal, offVal, TweakState.On, log);
            t.Disable = log => ApplyDword3(root, sub, regName, onVal, offVal, TweakState.Off, log);
            t.State = () => GetDword3(root, sub, regName, onVal, offVal) == TweakState.On;
            return t;
        }

        public static List<TweakEntry> All { get; } = Build();

        private static List<TweakEntry> Build()
        {
            var L = new List<TweakEntry>();

            // ================= 一、资源管理器 =================
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "classic_menu", Name = "经典右键菜单",
                Desc = "恢复 Win10 风格右键菜单（需重启资源管理器）", Risk = "low",
                Enable = log => { RegistryHelper.SetSz(HKCU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "", log); RegistryHelper.RestartExplorer(log); },
                Disable = log => { RegistryHelper.DeleteKeyTree(HKCU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", log); RegistryHelper.RestartExplorer(log); },
                State = () => RegistryHelper.ClsIdDefaultEmpty(HKCU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32")
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "win10_explorer", Name = "Win10 资源管理器风格",
                Desc = "切换为 Win10 经典资源管理器（非 Xaml Island，需重启资源管理器）", Risk = "low",
                Enable = log =>
                {
                    foreach (var clsid in new[] { "{2aa9162e-c906-4dd9-ad0b-3d24a8eef5a0}", "{6480100b-5a83-4d1e-9f69-8ae5a88e9a33}" })
                        RegistryHelper.SetSz(HKCU, @"Software\Classes\CLSID\" + clsid + @"\InprocServer32", "", "", log);
                    RegistryHelper.RestartExplorer(log);
                },
                Disable = log =>
                {
                    foreach (var clsid in new[] { "{2aa9162e-c906-4dd9-ad0b-3d24a8eef5a0}", "{6480100b-5a83-4d1e-9f69-8ae5a88e9a33}" })
                        RegistryHelper.DeleteKeyTree(HKCU, @"Software\Classes\CLSID\" + clsid, log);
                    RegistryHelper.RestartExplorer(log);
                },
                State = () => RegistryHelper.ClsIdDefaultEmpty(HKCU, @"Software\Classes\CLSID\{2aa9162e-c906-4dd9-ad0b-3d24a8eef5a0}\InprocServer32")
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_frequent", Name = "显示常用文件夹",
                Desc = "在快速访问中显示「常用文件夹」", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "ShowFrequent", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "ShowFrequent", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "ShowFrequent", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_empty_drives", Name = "显示空驱动器",
                Desc = "在资源管理器中显示空驱动器（无介质的光驱/U盘）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "HideDrivesWithNoMedia", 0, log); },
                Disable = log => { DwordR(HKCU, ADV, "HideDrivesWithNoMedia", 1, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "HideDrivesWithNoMedia", 0) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_ext", Name = "显示文件扩展名",
                Desc = "始终显示文件扩展名（如 .txt/.exe）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "HideFileExt", 0, log); },
                Disable = log => { DwordR(HKCU, ADV, "HideFileExt", 1, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "HideFileExt", 0) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_hidden", Name = "显示隐藏与系统文件",
                Desc = "显示隐藏文件，并同时显示受保护的系统文件", Risk = "low",
                Enable = log => { RegistryHelper.SetDword(HKCU, ADV, "Hidden", 1, log); DwordR(HKCU, ADV, "ShowSuperHidden", 1, log); },
                Disable = log => { RegistryHelper.SetDword(HKCU, ADV, "Hidden", 2, log); DwordR(HKCU, ADV, "ShowSuperHidden", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "Hidden", 2) == 1 && RegistryHelper.GetDword(HKCU, ADV, "ShowSuperHidden", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "launch_to", Name = "打开此电脑",
                Desc = "打开资源管理器时默认打开「此电脑」（而非快速访问）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "LaunchTo", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "LaunchTo", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "LaunchTo", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_recent", Name = "显示最近使用的文件",
                Desc = "在快速访问中显示「最近使用的文件」", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "ShowRecent", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "ShowRecent", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "ShowRecent", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "show_thumbnails", Name = "显示图片缩略图预览",
                Desc = "桌面与资源管理器中显示图片内容缩略图（而非通用图标）；关闭则仅显示图标。需重启资源管理器", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKCU, ADV, "ShowIconPreview", 1, log); // 桌面图标缩略图
                    RegistryHelper.SetDword(HKCU, ADV, "IconsOnly", 0, log);       // 文件夹视图缩略图
                    RegistryHelper.RestartExplorer(log);
                },
                Disable = log =>
                {
                    RegistryHelper.SetDword(HKCU, ADV, "ShowIconPreview", 0, log);
                    DwordR(HKCU, ADV, "IconsOnly", 1, log);
                },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "ShowIconPreview", 1) == 1
                          && RegistryHelper.GetDword(HKCU, ADV, "IconsOnly", 0) == 0
            });

            // ================= 二、任务栏 =================
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "taskbar_center", Name = "任务栏图标居中",
                Desc = "Win11 默认将开始菜单与图标居中显示", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "TaskbarAl", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "TaskbarAl", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "TaskbarAl", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "widgets", Name = "显示小组件",
                Desc = "任务栏显示/隐藏小组件按钮", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "TaskbarDa", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "TaskbarDa", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "TaskbarDa", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "chat", Name = "显示聊天/Copilot",
                Desc = "任务栏显示/隐藏聊天(Copilot)入口", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "TaskbarMn", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "TaskbarMn", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "TaskbarMn", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "taskview", Name = "显示任务视图",
                Desc = "任务栏显示/隐藏任务视图按钮", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "ShowTaskViewButton", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "ShowTaskViewButton", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "ShowTaskViewButton", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "searchbox", Name = "显示搜索框",
                Desc = "任务栏显示搜索框（关闭则仅显示搜索图标）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "SearchboxTaskbarMode", 2, log); },
                Disable = log => { DwordR(HKCU, ADV, "SearchboxTaskbarMode", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "SearchboxTaskbarMode", 0) == 2
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "clock_seconds", Name = "时钟显示秒",
                Desc = "任务栏时钟显示秒数", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "ShowSecondsInSystemClock", 1, log); },
                Disable = log => { DwordR(HKCU, ADV, "ShowSecondsInSystemClock", 0, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "ShowSecondsInSystemClock", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "combine", Name = "合并任务栏按钮",
                Desc = "始终合并同类窗口按钮（关闭则分开显示）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "TaskbarGlomLevel", 0, log); },
                Disable = log => { DwordR(HKCU, ADV, "TaskbarGlomLevel", 2, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "TaskbarGlomLevel", 0) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "外观/资源管理器", Id = "autohide", Name = "自动隐藏任务栏",
                Desc = "鼠标离开时自动隐藏任务栏", Risk = "mid",
                Enable = log => SetAutohide(log, true),
                Disable = log => SetAutohide(log, false),
                State = () => GetAutohide()
            });

            // ================= 三、隐私 =================
            L.Add(new TweakEntry
            {
                Group = "隐私设置", Id = "websearch", Name = "禁止开始菜单 Web 搜索",
                Desc = "关闭开始菜单的必应网络搜索与云搜索", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "BingSearchEnabled", 0, log);
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCloudSearch", 0, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "BingSearchEnabled", log);
                    RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCloudSearch", log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "BingSearchEnabled", 1) == 0
            });
            // 注：adsid/startsuggest/typing/mrt/block_feature_update 已合并到独立「隐私设置页」，
            //     此处保留全局策略项 websearch/disable-ad-id/exclude_driver_wu 等，避免 HKLM 设置入口丢失。
            L.Add(new TweakEntry
            {
                Group = "隐私设置", Id = "exclude_driver_wu", Name = "禁止更新夹带驱动",
                Desc = "禁止质量更新中包含驱动程序（ExcludeWUDriversInQualityUpdate）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, WU_POL, "ExcludeWUDriversInQualityUpdate", 1, log),
                Disable = log => RegistryHelper.SetDword(HKLM, WU_POL, "ExcludeWUDriversInQualityUpdate", 0, log),
                State = () => RegistryHelper.GetDword(HKLM, WU_POL, "ExcludeWUDriversInQualityUpdate", 0) == 1
            });

            // ================= 四、其他 =================
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "onedrive", Name = "禁用 OneDrive 同步",
                Desc = "通过组策略禁用 OneDrive 文件同步", Risk = "mid",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "ucpd", Name = "禁用 UCPD 驱动",
                Desc = "关闭拦截注册表修改的 UCPD 驱动（重启生效）", Risk = "mid",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\UCPD", "Start", 4, log),
                Disable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\UCPD", "Start", 1, log),
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\UCPD", "Start", 1) == 4
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "ucpd_task", Name = "禁用 UCPD 计划任务",
                Desc = "禁用 \\Microsoft\\Windows\\AppxDeploymentClient\\UCPD velocity 计划任务", Risk = "low",
                Enable = log => RegistryHelper.RunCommand("schtasks", @"/change /disable /tn """ + UCPD_TASK + @"""", log),
                Disable = log => RegistryHelper.RunCommand("schtasks", @"/change /enable /tn """ + UCPD_TASK + @"""", log),
                State = () => UcpdTaskDisabled()
            });

            // ================= 五、安全（高风险） =================
            L.Add(new TweakEntry
            {
                Group = "安全设置", Id = "smartscreen", Name = "关闭 SmartScreen",
                Desc = "超强关闭 SmartScreen 筛选（含 CLSID 删除，降低下载/运行防护，可一键还原）", Risk = "high",
                Enable = log =>
                {
                    RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off", log);
                    RegistryHelper.SetDword(HKLM, @"Software\Policies\Microsoft\Windows Defender\SmartScreen", "EnableSmartScreen", 0, log);
                    RegistryHelper.SetDword(HKLM, @"Software\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControl", 0, log);
                    RegistryHelper.SetDword(HKLM, @"Software\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControlEnabled", 0, log);
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", "DefaultFileTypeRisk", 6152, log);
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", "SaveZoneInformation", 1, log);
                    RegistryHelper.SetSz(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", "LowRiskFileTypes", ".avi;.bat;.com;.cmd;.exe;.htm;.html;.lnk;.mpg;.mpeg;.mov;.mp3;.msi;.m3u;.rar;.reg;.txt;.vbs;.wav;.zip;", log);
                    RegistryHelper.SetSz(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", "ModRiskFileTypes", ".bat;.exe;.reg;.vbs;.chm;.msi;.js;.cmd", log);
                    RegistryHelper.SetDword(HKCU, @"Software\Policies\Microsoft\MicrosoftEdge\PhishingFilter", "EnabledV9", 0, log);
                    RegistryHelper.SetDword(HKLM, @"Software\Policies\Microsoft\MicrosoftEdge\PhishingFilter", "EnabledV9", 0, log);
                    KillSmartScreenClsid(log);
                    log("  [提示] 已执行超强禁用（删除 SmartScreen CLSID）。彻底还原建议用原版工具或重装系统组件。");
                },
                Disable = log =>
                {
                    RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "On", log);
                    RegistryHelper.DeleteKeyTree(HKLM, @"Software\Policies\Microsoft\Windows Defender\SmartScreen", log);
                    RegistryHelper.DeleteKeyTree(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", log);
                    RegistryHelper.DeleteValue(HKCU, @"Software\Policies\Microsoft\MicrosoftEdge\PhishingFilter", "EnabledV9", log);
                    RegistryHelper.DeleteValue(HKLM, @"Software\Policies\Microsoft\MicrosoftEdge\PhishingFilter", "EnabledV9", log);
                    RestoreSmartScreenClsid(log);
                },
                State = () => RegistryHelper.GetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "On") == "Off"
            });
            // ================= 六、系统设置 =================
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "system_restore", Name = "禁用系统还原",
                Desc = "关闭系统还原功能（释放还原点占用的磁盘空间）", Risk = "mid",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableSR", 1, log);
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableConfig", 1, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableSR", log);
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableConfig", 0, log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableSR", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "search_highlights", Name = "关闭搜索热点",
                Desc = "隐藏任务栏搜索框中的 Web 热点/推荐内容", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", log),
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "auto_driver", Name = "关闭自动驱动更新",
                Desc = "阻止 Windows Update 自动下载安装驱动程序", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, log);
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1, log);
                },
                Disable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 1, log);
                    RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "ceip", Name = "关闭体验改善计划(CEIP)",
                Desc = "停止向微软发送使用数据和诊断信息", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0, log);
                    RegistryHelper.RunCommand("sc", "config DiagTrack start= disabled", log);
                    RegistryHelper.RunCommand("sc", "stop DiagTrack", log);
                    RegistryHelper.RunCommand("sc", "config dmwappushservice start= disabled", log);
                    RegistryHelper.RunCommand("sc", "stop dmwappushservice", log);
                },
                Disable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 1, log);
                    RegistryHelper.RunCommand("sc", "config DiagTrack start= demand", log);
                    RegistryHelper.RunCommand("sc", "config dmwappushservice start= demand", log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "pca", Name = "禁用程序兼容性助手",
                Desc = "关闭 PCA（减少「正在为此应用查找解决方案」弹窗）", Risk = "mid",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\PcaSvc", "Start", 4, log),
                Disable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\PcaSvc", "Start", 2, log),
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\PcaSvc", "Start", 2) == 4
            });
            L.Add(new TweakEntry
            {
                Group = "系统设置", Id = "diag_svc", Name = "禁用诊断策略服务",
                Desc = "停止诊断数据收集服务组（wdiagsvc/DPS 等）", Risk = "mid",
                Enable = log =>
                {
                    foreach (var s in new[] { "wdiagsvc", "DiagnosticPolicyService", "DiagnosticSystemServiceHost", "DiagnosticExecutionService" })
                        RegistryHelper.SetDword(HKLM, CTRL + "\\" + s, "Start", 4, log);
                },
                Disable = log =>
                {
                    foreach (var s in new[] { "wdiagsvc", "DiagnosticPolicyService", "DiagnosticSystemServiceHost", "DiagnosticExecutionService" })
                        RegistryHelper.SetDword(HKLM, CTRL + "\\" + s, "Start", 2, log);
                },
                State = () => RegistryHelper.GetDword(HKLM, CTRL + "\\wdiagsvc", "Start", 2) == 4
            });

            // ================= 七、高级设置 =================
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "meltdown_spectre", Name = "关闭 Meltdown/Spectre 缓解",
                Desc = "关闭 CPU 漏洞缓解（旧 CPU 可能提升性能，降低安全性）", Risk = "mid",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 3, log);
                    RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverrideMask", 3, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", log);
                    RegistryHelper.DeleteValue(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverrideMask", log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 0) == 3
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "wdac", Name = "关闭 WD 应用程序控制",
                Desc = "关闭 WDAC（Windows Defender Application Control，由系统决定）", Risk = "mid",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\WDAC", "Enabled", 0, log),
                Disable = log => RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\WDAC", "Enabled", 1, log),
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\WDAC", "Enabled", 1) == 0
            });
            L.Add(ThreeStateDword("安全设置", "vbs_security", "关闭 VBS 虚拟化安全",
                "关闭基于虚拟化的安全性（释放 ~4-8% 性能，影响内存完整性等）。更改后需重启生效。三态：系统默认=交还系统设定", "mid",
                HKLM, DG, "EnableVirtualizationBasedSecurity", 0, 1));
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "tcp_bbr2", Name = "启用 TCP BBR2 拥塞控制",
                Desc = "使用 BBR2 算法替代 Cubic（提升高延迟网络吞吐量）", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetSz(HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "CongestionProvider", "bbr2", log);
                    RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Tcpccp", 3, log);
                    RegistryHelper.RunCommand("netsh", "int tcp set supplemental custom=congestionprovider=bbr2", log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "CongestionProvider", log);
                    RegistryHelper.DeleteValue(HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Tcpccp", log);
                    RegistryHelper.RunCommand("netsh", "int tcp set supplemental default", log);
                },
                State = () => RegistryHelper.GetSz(HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "CongestionProvider", "") == "bbr2"
            });
            L.Add(ThreeStateDword("性能优化", "dep", "DEP 数据执行保护",
                "数据执行保护：Opt-In(仅系统组件)=开 / Opt-Out(全系统)=关 / 系统默认=交还系统设定", "mid",
                HKLM, MEM, "ExecuteOptions", 2, 0));
            L.Add(ThreeStateDword("性能优化", "uac", "关闭 UAC",
                "禁用用户账户控制提示（所有程序静默以管理员运行，不推荐长期关闭）。三态：系统默认=交还系统设定", "high",
                HKLM, SYS, "EnableLUA", 0, 1));
            L.Add(ThreeStateDword("系统设置", "reserved_storage", "关闭保留存储",
                "禁用 Windows 保留存储（释放约 7GB 磁盘空间）。三态：系统默认=交还系统设定", "low",
                HKLM, RES, "ShippedWithReservations", 0, 1));
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "mem_compress", Name = "内存压缩",
                Desc = "启用/禁用系统内存压缩（Enable/Disable-MMAgent -mc）", Risk = "mid",
                Enable = log => Exec.RunPowerShell("Enable-MMAgent -mc", log),
                Disable = log => Exec.RunPowerShell("Disable-MMAgent -mc", log),
                State = () => MMAgentProp("MemoryCompression")
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "page_combining", Name = "内存页面合并",
                Desc = "启用/禁用内存页面合并（Enable/Disable-MMAgent -PageCombining）", Risk = "mid",
                Enable = log => Exec.RunPowerShell("Enable-MMAgent -PageCombining", log),
                Disable = log => Exec.RunPowerShell("Disable-MMAgent -PageCombining", log),
                State = () => MMAgentProp("PageCombining")
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "app_prelaunch", Name = "应用预启动",
                Desc = "启用/禁用应用预启动（Enable/Disable-MMAgent -ApplicationPreLaunch）", Risk = "mid",
                Enable = log => Exec.RunPowerShell("Enable-MMAgent -ApplicationPreLaunch", log),
                Disable = log => Exec.RunPowerShell("Disable-MMAgent -ApplicationPreLaunch", log),
                State = () => MMAgentProp("ApplicationPreLaunch")
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "sysmain", Name = "SysMain 服务",
                Desc = "超级预读服务：启用（自动+启动）/禁用（停止+禁用）", Risk = "mid",
                Enable = log => { RegistryHelper.RunCommand("sc", "config SysMain start=auto", log); RegistryHelper.RunCommand("sc", "start SysMain", log); },
                Disable = log => { RegistryHelper.RunCommand("sc", "stop SysMain", log); RegistryHelper.RunCommand("sc", "config SysMain start=disabled", log); },
                State = () => RegistryHelper.GetDword(HKLM, CTRL + "\\SysMain", "Start", 2) == 2
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "wsearch", Name = "Windows 搜索服务",
                Desc = "Windows 搜索服务：启用（延迟自动+启动）/禁用（停止+禁用）", Risk = "mid",
                Enable = log => { RegistryHelper.RunCommand("sc", "config WSearch start=delayed-auto", log); RegistryHelper.RunCommand("sc", "start WSearch", log); },
                Disable = log => { RegistryHelper.RunCommand("sc", "stop WSearch", log); RegistryHelper.RunCommand("sc", "config WSearch start=disabled", log); },
                State = () => RegistryHelper.GetDword(HKLM, CTRL + "\\WSearch", "Start", 4) == 2
            });

            // === 预取优化（新增：对齐参考软件 Windows11 轻松设置）===
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "max_prefetch", Name = "最大预取文件数",
                Desc = "设置预取缓存文件上限为 1024（推荐值）；关闭则删除键值交还系统默认", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, PREF, "MaxPrefetchFiles", 1024, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, PREF, "MaxPrefetchFiles", log),
                State = () => RegistryHelper.GetDword(HKLM, PREF, "MaxPrefetchFiles", -1) == 1024
            });
            L.Add(ThreeStateDword("性能优化", "app_prefetch", "应用启动预取",
                "启用/禁用应用启动预取（EnablePrefetcher）。三态：系统默认=交还系统设定(通常应用+启动皆预取)", "low",
                HKLM, PREF, "EnablePrefetcher", 1, 0));

            // ================= 八、电源 =================
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "hibernate", Name = "系统休眠",
                Desc = "开启/关闭系统休眠（关闭可删除 hiberfil.sys 释放空间）", Risk = "low",
                Enable = log => { RegistryHelper.SetDword(HKLM, POW, "HibernateEnabled", 1, log); RegistryHelper.RunCommand("powercfg", "/h on", log); },
                Disable = log => { RegistryHelper.SetDword(HKLM, POW, "HibernateEnabled", 0, log); RegistryHelper.RunCommand("powercfg", "/h off", log); },
                State = () => RegistryHelper.GetDword(HKLM, POW, "HibernateEnabled", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "fast_startup", Name = "快速启动",
                Desc = "开启/关闭 Windows 快速启动（真实路径 Session Manager\\Power）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, SMP, "HiberbootEnabled", 1, log),
                Disable = log => RegistryHelper.SetDword(HKLM, SMP, "HiberbootEnabled", 0, log),
                State = () => RegistryHelper.GetDword(HKLM, SMP, "HiberbootEnabled", 0) == 1
            });

            // ================= 九、网络远程（高风险） =================
            L.Add(new TweakEntry
            {
                Group = "安全设置", Id = "rdp", Name = "远程桌面 (RDP)",
                Desc = "开启/关闭远程桌面并放行 3389 入站（关闭即拒绝连接）", Risk = "high",
                Enable = log =>
                {
                    string ps = "Set-ItemProperty 'HKLM:\\System\\CurrentControlSet\\Control\\Terminal Server' -Name fDenyTSConnections -Value 0; Enable-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue";
                    Exec.RunPowerShell(ps, log);
                },
                Disable = log =>
                {
                    string ps = "Set-ItemProperty 'HKLM:\\System\\CurrentControlSet\\Control\\Terminal Server' -Name fDenyTSConnections -Value 1; Disable-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue";
                    Exec.RunPowerShell(ps, log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "安全设置", Id = "remote_assist", Name = "远程协助",
                Desc = "开启/关闭远程协助并放行防火墙（关闭即拒绝帮助请求）", Risk = "high",
                Enable = log =>
                {
                    string ps = "Set-ItemProperty 'HKLM:\\System\\CurrentControlSet\\Control\\Remote Assistance' -Name fAllowToGetHelp -Value 1; New-NetFirewallRule -Name 'AllowRA' -DisplayGroup 'Remote Assistance' -Enabled True -Direction Inbound -Protocol TCP -ErrorAction SilentlyContinue";
                    Exec.RunPowerShell(ps, log);
                },
                Disable = log =>
                {
                    string ps = "Set-ItemProperty 'HKLM:\\System\\CurrentControlSet\\Control\\Remote Assistance' -Name fAllowToGetHelp -Value 0; Remove-NetFirewallRule -Name 'AllowRA' -ErrorAction SilentlyContinue";
                    Exec.RunPowerShell(ps, log);
                },
                State = () => RegistryHelper.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0) == 1
            });

            // ================= 十、更多优化（增强：原版之外我们额外加的实用项） =================
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "ink_dict", Name = "关闭墨迹/键入词典收集",
                Desc = "停止上传手写墨迹与键入习惯词典", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, log);
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", log);
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", log);
                },
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "start_recommend", Name = "关闭开始菜单推荐",
                Desc = "隐藏开始菜单中的「推荐的项目」（需重启资源管理器）", Risk = "low",
                Enable = log => { DwordR(HKCU, ADV, "Start_IrisRecommendations", 0, log); },
                Disable = log => { DwordR(HKCU, ADV, "Start_IrisRecommendations", 1, log); },
                State = () => RegistryHelper.GetDword(HKCU, ADV, "Start_IrisRecommendations", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "delivery_opt", Name = "关闭传递优化",
                Desc = "停止通过 P2P 上传更新/商店内容（节省带宽，但会略慢获取更新）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings", "DownloadMode", 0, log),
                Disable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings", "DownloadMode", 1, log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings", "DownloadMode", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "gamebar", Name = "关闭游戏栏/Game DVR",
                Desc = "关闭 Xbox 游戏栏与后台录制（提升游戏性能）", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, log);
                    RegistryHelper.SetDword(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0, log);
                    RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, log);
                },
                Disable = log =>
                {
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1, log);
                    RegistryHelper.SetDword(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 1, log);
                    RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", log);
                },
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "cortana", Name = "禁用 Cortana",
                Desc = "通过组策略禁用 Cortana 语音助手", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "clipboard_hist", Name = "关闭剪贴板历史",
                Desc = "关闭剪贴板历史记录（含云同步）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 0, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", log),
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "jump_list", Name = "关闭跳转列表",
                Desc = "不在任务栏/开始记录最近使用的文档跳转列表", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, ADV, "Start_TrackDocs", 0, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, ADV, "Start_TrackDocs", log),
                State = () => RegistryHelper.GetDword(HKCU, ADV, "Start_TrackDocs", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "lock_screen", Name = "关闭锁屏",
                Desc = "登录前跳过锁屏（Win10 组策略）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "wer", Name = "禁用错误报告弹窗",
                Desc = "关闭 Windows 错误报告（WER）弹窗与上传", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "search_index", Name = "禁用搜索索引服务",
                Desc = "停用 Windows Search 索引服务（首次搜索变慢，节省资源）", Risk = "mid",
                Enable = log => { RegistryHelper.RunCommand("sc", "stop WSearch", log); RegistryHelper.RunCommand("sc", "config WSearch start=disabled", log); },
                Disable = log => { RegistryHelper.RunCommand("sc", "config WSearch start=delayed-auto", log); RegistryHelper.RunCommand("sc", "start WSearch", log); },
                State = () => RegistryHelper.GetDword(HKLM, CTRL + "\\WSearch", "Start", 2) == 4
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "spotlight", Name = "关闭 Spotlight 自动壁纸",
                Desc = "停止 Windows 自动更换锁屏/壁纸（Spotlight）", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", 0, log);
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenAllowStartupPhotos", 0, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", log);
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenAllowStartupPhotos", log);
                },
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "bg_apps", Name = "禁用后台应用",
                Desc = "禁止 Microsoft Store 应用后台运行（省电）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", log),
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "toast", Name = "关闭 Toast 通知",
                Desc = "禁用 Windows 推送通知（Toast）", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", log),
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "quick_access", Name = "关闭快速访问记录",
                Desc = "资源管理器快速访问不再显示最近/常用文件", Risk = "low",
                Enable = log =>
                {
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, log);
                    RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, log);
                },
                Disable = log =>
                {
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", log);
                    RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", log);
                },
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 1) == 0
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "autoplay", Name = "关闭自动播放",
                Desc = "插入U盘/光盘时不自动播放", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", log),
                State = () => RegistryHelper.GetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 0) == 1
            });
            L.Add(new TweakEntry
            {
                Group = "性能优化", Id = "feedback", Name = "关闭反馈通知",
                Desc = "不再弹出「向我们发送反馈」类通知", Risk = "low",
                Enable = log => RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, log),
                Disable = log => RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", log),
                State = () => RegistryHelper.GetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 0) == 1
            });

            
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "hide-taskbar-search", Name = "隐藏任务栏搜索框", Desc = "隐藏任务栏搜索框", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "hide-taskview-btn", Name = "隐藏任务视图按钮", Desc = "隐藏任务栏任务视图按钮", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "show-all-tray-icons", Name = "始终在任务栏显示所有图标", Desc = "任务栏显示所有系统托盘图标", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "boost-foreground", Name = "提高前台程序显示速度", Desc = "提高前台程序CPU优先级响应速度", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 26, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "disable-window-anim", Name = "不要显示窗口出现和消失动画", Desc = "关闭窗口动画效果", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "explorer-this-pc", Name = "打开资源管理器时显示此电脑", Desc = "默认打开此电脑而非快速访问", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "no-auto-store-update", Name = "禁止应用商店自动下载安装更新", Desc = "关闭商店自动更新", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "speed-shutdown", Name = "加快关机速度", Desc = "缩短关机等待时间2000ms", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", 2000, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", 5000, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", 2000) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-kernel-paging", Name = "禁止内核与驱动分页到硬盘", Desc = "禁止系统内核驱动程序分页", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-hpet", Name = "禁用高精度事件定时器HPET", Desc = "关闭HPET提高性能", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DisableHpet", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DisableHpet", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DisableHpet", 1) });
            L.Add(new TweakEntry { Group = "安全设置", Id = "uac-never-notify", Name = "UAC从不通知", Desc = "管理员提权时从不弹窗提示(静默执行，需UAC已开启)", Risk = "high", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0, log); RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 5, log); RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 1, log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")) { var c = k?.GetValue("ConsentPromptBehaviorAdmin"); var p = k?.GetValue("PromptOnSecureDesktop"); return c is int cv && cv == 0 && p is int pv && pv == 0; } } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-no-welcome", Name = "Edge不要显示首次运行欢迎页面", Desc = "禁止Edge首次运行欢迎", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 1) });
            L.Add(new TweakEntry { Group = "更新设置", Id = "wu-no-driver", Name = "Windows更新不包括驱动程序", Desc = "排除WU驱动更新", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-ad-id", Name = "禁用广告标识符", Desc = "关闭广告ID", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0) });

            // === 新增项：对齐 ZyperWin 150+ 项 ===

            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "show-cloud-files", Name = "快速访问不显示Office.com文件", Desc = "快速访问不显示来自Office.com的文件", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowCloudFilesInQuickAccess", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowCloudFilesInQuickAccess", 1, log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowCloudFilesInQuickAccess", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "show-empty-drives", Name = "显示空的驱动器", Desc = "文件资源管理器显示空的驱动器", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideDrivesWithNoMedia", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideDrivesWithNoMedia", 1, log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideDrivesWithNoMedia", 0) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "show-hidden-files", Name = "显示隐藏的文件和文件夹", Desc = "显示隐藏的文件、文件夹和驱动器", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2, log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1) });
            L.Add(new TweakEntry { Group = "外观/资源管理器", Id = "hide-system-files", Name = "隐藏受保护的系统文件", Desc = "隐藏受保护的操作系统文件(推荐)", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 1, log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 0) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "hide-start-suggestions", Name = "不允许在开始菜单显示建议", Desc = "关闭开始菜单的应用建议", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "ShowContentInSuggested", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "ShowContentInSuggested", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "ShowContentInSuggested", 0) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "ceip-disable", Name = "关闭客户体验改善计划(CEIP)", Desc = "关闭Windows客户体验改善计划", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-diagnostic-svc", Name = "禁用诊断服务", Desc = "禁用Diagnostic服务以释放系统资源", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\DPS", "Start", 4, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\DPS", "Start", 3, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Services\DPS", "Start", 4) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-sysmain-svc", Name = "禁用SysMain服务", Desc = "禁用SysMain(Superfetch)服务。SSD用户可尝试禁用", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 3, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-wsearch-svc", Name = "禁用Windows Search服务", Desc = "禁用WSearch索引服务。不使用Windows搜索可禁用", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 4, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 3, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 4) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "disable-homegroup-svc", Name = "禁用家庭组服务", Desc = "禁用家庭组相关服务", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\HomeGroupListener", "Start", 4, log); RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\HomeGroupProvider", "Start", 4, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\HomeGroupListener", "Start", 3, log); RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\HomeGroupProvider", "Start", 3, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Services\HomeGroupListener", "Start", 4) });
            L.Add(new TweakEntry { Group = "性能优化", Id = "no-low-disk-warn", Name = "禁用磁盘空间不足警告", Desc = "当磁盘空间不足时不弹出警告通知", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks", 0, log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks", 1) });
            L.Add(ThreeStateDword("安全设置", "disable-memory-integrity", "关闭内存完整性",
                "关闭内核隔离内存完整性（提高性能但降低安全性）。更改后需重启生效。三态：系统默认=交还系统设定", "high",
                HKLM, DG + @"\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1));
            L.Add(new TweakEntry { Group = "安全设置", Id = "disable-smart-app-control", Name = "关闭智能应用控制", Desc = "关闭智能应用控制(Smart App Control)", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", 0) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-no-tab-perf", Name = "禁用标签页性能检测器", Desc = "禁用Edge标签页性能检测器(睡眠标签)", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PerformanceDetectorEnabled", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PerformanceDetectorEnabled", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PerformanceDetectorEnabled", 0) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-no-news", Name = "禁用新标签页资讯", Desc = "禁用新选项卡页面上的微软资讯内容", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageContentEnabled", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageContentEnabled", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageContentEnabled", 0) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-no-personal-ads", Name = "禁用个性化广告", Desc = "禁用Edge个性化广告和体验", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 0) });
            L.Add(new TweakEntry { Group = "系统设置", Id = "disk-check-timeout-5s", Name = "缩短磁盘检查等待时间", Desc = "将磁盘错误检查chkdsk的等待时间缩短到5秒", Risk = "low", Enable = log => { RegistryHelper.SetSz(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager", "BootExecute", "autocheck timeout:5 autochk *", log); }, Disable = log => { RegistryHelper.SetSz(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager", "BootExecute", "autocheck autochk *", log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager")) return k?.GetValue("BootExecute") is string s && s.Contains("timeout:5"); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "系统设置", Id = "disable-system-restore", Name = "关闭系统还原", Desc = "禁用系统还原功能以释放磁盘空间", Risk = "mid", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR", 1) });
            L.Add(new TweakEntry { Group = "更新设置", Id = "wu-no-reboot-logged", Name = "更新挂起不自动重启", Desc = "更新挂起时若有用户登录则不自动重启计算机", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 0, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1) });
            // 注：no-search-history/no-ink-dict/no-app-start-track/no-language-list/no-suggested-content
            //     已合并到独立「隐私设置页」（HKCU 当前用户），避免 Tweaks 与隐私页重复控制。
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-sms-router", Name = "禁用SMS路由器服务", Desc = "禁用SMS路由器服务以保护隐私", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\SmsRouter", "Start", 4, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Services\SmsRouter", "Start", 3, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Services\SmsRouter", "Start", 4) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-app-filesystem", Name = "禁止应用访问文件系统", Desc = "禁止应用访问文件系统(需重启生效)", Risk = "low", Enable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\broadFileSystemAccess", "Value", "Deny", log); }, Disable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\broadFileSystemAccess", "Value", "Allow", log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\broadFileSystemAccess")) return k?.GetValue("Value") is string s && s == "Deny"; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-app-documents", Name = "禁止应用访问文档", Desc = "禁止应用访问文档库", Risk = "low", Enable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\documentsLibrary", "Value", "Deny", log); }, Disable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\documentsLibrary", "Value", "Allow", log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\documentsLibrary")) return k?.GetValue("Value") is string s && s == "Deny"; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-welcome-experience", Name = "禁用Windows欢迎体验", Desc = "禁用首次登录时的Windows欢迎体验", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0, log); }, Disable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 1, log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0) });

            // === 对齐 ZyperWin：Edge优化组补充 6 项（全部 low）===
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-bing-ads-suppress", Name = "阻止必应搜索结果中的广告", Desc = "屏蔽 Edge 必应搜索结果的广告（BingAdsSuppression）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BingAdsSuppression", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BingAdsSuppression", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BingAdsSuppression", 1) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-hide-default-topsites", Name = "新标签页隐藏默认热门站点", Desc = "新标签页不再显示默认热门站点（NewTabPageHideDefaultTopSites）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageHideDefaultTopSites", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageHideDefaultTopSites", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "NewTabPageHideDefaultTopSites", 1) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-hide-sidebar", Name = "隐藏 Edge 浏览器边栏", Desc = "隐藏 Edge 右侧边栏（Recommended\\HubsSidebarEnabled）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", "HubsSidebarEnabled", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", "HubsSidebarEnabled", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", "HubsSidebarEnabled", 0) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-suppress-os-warning", Name = "关闭停止支持旧系统的通知", Desc = "关闭 Edge 停止支持旧系统版本的通知（SuppressUnsupportedOSWarning）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "SuppressUnsupportedOSWarning", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "SuppressUnsupportedOSWarning", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "SuppressUnsupportedOSWarning", 1) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-no-diag-data", Name = "不发送 Edge 诊断数据", Desc = "Edge 不发送任何诊断数据（DiagnosticData）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData", 0) });
            L.Add(new TweakEntry { Group = "Edge优化", Id = "edge-disable-insecure-dl-warn", Name = "禁用不安全下载警告", Desc = "⚠ 谨慎：关闭 Edge 对潜在不安全下载的警告（ShowDownloadsInsecureWarningsEnabled）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "ShowDownloadsInsecureWarningsEnabled", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "ShowDownloadsInsecureWarningsEnabled", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "ShowDownloadsInsecureWarningsEnabled", 0) });

            // === 对齐 ZyperWin：隐私设置组补充 16 项（全部 low，均为可逆注册表写入）===
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-page-prediction", Name = "禁用页面预测", Desc = "禁用 Explorer 页面预测功能（AllowPagePrediction）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "AllowPagePrediction", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "AllowPagePrediction", log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "AllowPagePrediction", 0) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-tailored-experiences", Name = "禁用活动收集", Desc = "禁用量身定制体验诊断数据收集（TailoredExperiencesWithDiagnosticDataEnabled）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "deny-calendar", Name = "禁止应用访问日历", Desc = "在 CapabilityAccessManager 中拒绝应用访问日历", Risk = "low", Enable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appointments", "Value", "Deny", log); }, Disable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appointments", "Value", "Allow", log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appointments")) return k?.GetValue("Value") is string s && s == "Deny"; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "deny-contacts", Name = "禁止应用访问联系人", Desc = "在 CapabilityAccessManager 中拒绝应用访问联系人", Risk = "low", Enable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\contacts", "Value", "Deny", log); }, Disable = log => { RegistryHelper.SetSz(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\contacts", "Value", "Allow", log); }, State = () => { try { using (var k = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\contacts")) return k?.GetValue("Value") is string s && s == "Deny"; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } } });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-first-run-animate", Name = "禁用 Windows 欢迎体验", Desc = "禁用首次登录时的 Windows 欢迎体验动画（DisableFirstRunAnimate）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableFirstRunAnimate", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableFirstRunAnimate", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableFirstRunAnimate", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-inking-typing", Name = "禁用墨迹书写和键入词典", Desc = "禁用自定义墨迹书写和键入个性化词典（Inking&TypingPersonalization）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Input\Settings", "Inking&TypingPersonalization", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Input\Settings", "Inking&TypingPersonalization", log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Input\Settings", "Inking&TypingPersonalization", 0) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-thirdparty-suggestions", Name = "禁用赞助商应用安装", Desc = "禁止 Windows 安装赞助商应用（DisableThirdPartySuggestions）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableThirdPartySuggestions", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableThirdPartySuggestions", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableThirdPartySuggestions", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "block-non-domain-wifi", Name = "禁止自动连接热点", Desc = "禁止自动连接到开放热点网络（fBlockNonDomain）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local", "fBlockNonDomain", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local", "fBlockNonDomain", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\Local", "fBlockNonDomain", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "no-typing-insights", Name = "禁用键入见解", Desc = "禁用输入法键入见解收集（EnableTypingInsights）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Microsoft\Input\Settings", "EnableTypingInsights", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKCU, @"Software\Microsoft\Input\Settings", "EnableTypingInsights", log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Microsoft\Input\Settings", "EnableTypingInsights", 0) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-preinstalled-apps", Name = "禁用预安装应用", Desc = "禁止系统预安装推广应用（DisablePreInstalledApps）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "DisablePreInstalledApps", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "DisablePreInstalledApps", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "DisablePreInstalledApps", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-netfx-telemetry", Name = "禁用 .NET 遥测", Desc = "禁止 .NET Framework 遥测上报（DisableNetFrameworkTelemetry）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DisableNetFrameworkTelemetry", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DisableNetFrameworkTelemetry", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DisableNetFrameworkTelemetry", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-ps-telemetry", Name = "禁用 PowerShell 遥测", Desc = "禁止 PowerShell 遥测上报（EnablePowerShellTelemetry）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\PowerShell", "EnablePowerShellTelemetry", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\PowerShell", "EnablePowerShellTelemetry", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\PowerShell", "EnablePowerShellTelemetry", 0) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-voice-activation", Name = "禁用语音激活(Cortana)", Desc = "强制拒绝应用通过语音激活（LetAppsActivateWithVoice=2）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoice", 2, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoice", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoice", 2) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-location", Name = "禁用位置服务", Desc = "⚠ 注意：关闭位置服务会影响依赖定位的应用（地图/天气等）（DisableLocation）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-problem-reports", Name = "禁用步骤记录器", Desc = "禁用问题步骤记录器（DisableProblemReports）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKCU, @"Software\Policies\Microsoft\Windows\ProblemReports", "DisableProblemReports", 1, log); }, Disable = log => { RegistryHelper.DeleteValue(HKCU, @"Software\Policies\Microsoft\Windows\ProblemReports", "DisableProblemReports", log); }, State = () => RegistryHelper.GetDwordState(HKCU, @"Software\Policies\Microsoft\Windows\ProblemReports", "DisableProblemReports", 1) });
            L.Add(new TweakEntry { Group = "隐私设置", Id = "disable-debug-print-filter", Name = "禁用写入调试信息", Desc = "禁用内核调试打印过滤（Debug Print Filter DEFAULT）", Risk = "low", Enable = log => { RegistryHelper.SetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Debug Print Filter", "DEFAULT", 0, log); }, Disable = log => { RegistryHelper.DeleteValue(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Debug Print Filter", "DEFAULT", log); }, State = () => RegistryHelper.GetDwordState(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Debug Print Filter", "DEFAULT", 0) });

            return L;
        }

        // ---- StuckRects3 自动隐藏（二进制第 9 字节 bit3 = 0x08）----
        private static void SetAutohide(Action<string> log, bool hide)
        {
            var data = RegistryHelper.GetBinary(HKCU, STUCK, "Settings");
            if (data != null && data.Length > 8)
            {
                if (hide) data[8] = (byte)(data[8] | 0x08);
                else data[8] = (byte)(data[8] & ~0x08);
                RegistryHelper.SetBinary(HKCU, STUCK, "Settings", data, log);
            }
            RegistryHelper.RestartExplorer(log);
        }

        private static bool GetAutohide()
        {
            var data = RegistryHelper.GetBinary(HKCU, STUCK, "Settings");
            return data != null && data.Length > 8 && (data[8] & 0x08) != 0;
        }

        // ---- UCPD 计划任务状态（schtasks /query 含 "Disabled" 即本优化已启用）----
        private static bool UcpdTaskDisabled()
        {
            var outp = Exec.RunCmdGet(new[] { "schtasks", "/query", "/tn", UCPD_TASK }, _ => { });
            return outp.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- MMAgent 属性查询（(Get-MMAgent).Xxx 返回 True/False）----
        private static bool MMAgentProp(string name)
        {
            var outp = Exec.RunPowerShellGet("(Get-MMAgent)." + name, _ => { });
            return outp.Trim().ToLowerInvariant() == "true";
        }

        // ---- SmartScreen 超强禁用：删除/重建其 COM 注册 CLSID ----
        private static readonly string[] SMART_CLSIDS = new[]
        {
            @"Software\Classes\CLSID\{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}",
            @"Software\Classes\WOW6432Node\CLSID\{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}",
            @"Software\Classes\AppID\{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}",
            @"Software\Classes\WOW6432Node\AppID\{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}"
        };

        private static void KillSmartScreenClsid(Action<string> log)
        {
            foreach (var p in SMART_CLSIDS)
                RegistryHelper.DeleteKeyTree(HKLM, p, log);
        }

        private static void RestoreSmartScreenClsid(Action<string> log)
        {
            foreach (var p in SMART_CLSIDS)
            {
                if (p.IndexOf(@"AppID\{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}", StringComparison.Ordinal) >= 0)
                {
                    RegistryHelper.SetDword(HKLM, p, "AppIDFlags", 8, log);
                    RegistryHelper.SetSz(HKLM, p, "RunAs", "Interactive User", log);
                    RegistryHelper.SetDword(HKLM, p, "PreferredServerBitness", -2147483648, log);
                }
                else
                {
                    RegistryHelper.SetSz(HKLM, p, "AppID", "{a463fcb9-6b1c-4e0d-a80b-a2ca7999e25d}", log);
                    RegistryHelper.SetSz(HKLM, p + @"\InProcServer32", "ThreadingModel", "Both", log);
                }
            }
        }
    }
}
