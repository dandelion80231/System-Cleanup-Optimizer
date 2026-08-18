using System;
using System.Collections.Generic;

namespace CpqSystemTool
{
    /// <summary>
    /// 服务项优化：列出可安全禁用的后台服务，支持一键禁用/恢复。
    /// 对应 ZyperWin++ 的「服务项优化」模块。每个服务带中文说明与风险等级。
    /// </summary>
    internal static class ServiceOptimizer
    {
        public class ServiceEntry
        {
            public string Name;      // 服务名（sc 用）
            public string Display;   // 显示名
            public string Desc;      // UI 说明
            public string Risk;      // low / mid / high
        }

        // 可安全禁用的后台服务清单（Risk=low 基本无副作用，mid 需按需）
        public static readonly List<ServiceEntry> All = new List<ServiceEntry>
        {
            new ServiceEntry { Name = "SysMain",            Display = "Superfetch 预读取",   Desc = "为程序预加载到内存；SSD 上收益低且增加写入磨损", Risk = "low" },
            new ServiceEntry { Name = "WSearch",            Display = "Windows 搜索索引",    Desc = "为开始菜单/资源管理器提供搜索索引，不常用搜索可关", Risk = "mid" },
            new ServiceEntry { Name = "Spooler",            Display = "打印后台处理",        Desc = "无打印机时可禁用（会禁用打印功能）", Risk = "mid" },
            new ServiceEntry { Name = "DiagTrack",          Display = "诊断跟踪(遥测)",      Desc = "收集使用数据上报微软，关闭可减后台活动", Risk = "low" },
            new ServiceEntry { Name = "DmwApiPushSvc",      Display = "设备管理模式推送",    Desc = "企业设备管理推送，个人用户无用", Risk = "low" },
            new ServiceEntry { Name = "XblAuthManager",     Display = "Xbox 身份验证",       Desc = "Xbox 账号认证，不玩 Xbox 可关", Risk = "low" },
            new ServiceEntry { Name = "XblGameSave",        Display = "Xbox 游戏保存",       Desc = "Xbox 存档同步，不玩可关", Risk = "low" },
            new ServiceEntry { Name = "XboxNetApiSvc",      Display = "Xbox Live 网络",      Desc = "Xbox 网络服务，不玩可关", Risk = "low" },
            new ServiceEntry { Name = "XboxGipSvc",         Display = "Xbox 配件",           Desc = "手柄/配件服务，无手柄可关", Risk = "low" },
            new ServiceEntry { Name = "MapsBroker",         Display = "下载地图管理",        Desc = "离线地图下载管理，不用可关", Risk = "low" },
            new ServiceEntry { Name = "lfsvc",              Display = "地理位置服务",        Desc = "应用定位，隐私敏感可关", Risk = "low" },
            new ServiceEntry { Name = "RetailDemo",         Display = "零售演示",            Desc = "商店展示用，个人无用", Risk = "low" },
            new ServiceEntry { Name = "WbioSrvc",           Display = "生物识别",            Desc = "指纹/面部识别，无则关", Risk = "low" },
            new ServiceEntry { Name = "TabletInputService", Display = "平板输入服务",        Desc = "触控键盘/手写，无触屏可关", Risk = "low" },
            new ServiceEntry { Name = "WerSvc",             Display = "Windows 错误报告",    Desc = "崩溃上报，可关（个别软件依赖）", Risk = "mid" },
            new ServiceEntry { Name = "RemoteRegistry",     Display = "远程注册表",          Desc = "允许远程修改注册表，安全建议关闭", Risk = "low" },
            new ServiceEntry { Name = "BcastDVRUserService",Display = "游戏录制后台",        Desc = "Xbox 游戏 DVR 录制，不录制可关", Risk = "low" },
            new ServiceEntry { Name = "PhoneSvc",           Display = "电话服务",            Desc = "手机链接，无则关", Risk = "low" },
            new ServiceEntry { Name = "whesvc",             Display = "Windows 健康状况和优化体验", Desc = "本地性能诊断日志(占C盘)，关掉无性能提升、笔记本可能影响节能", Risk = "mid" },
        };

        private static readonly Action<string> Silent = s => { };

        /// <summary>当前是否已禁用（true=禁用）。服务不存在时返回 false。</summary>
        public static bool IsDisabled(string name)
        {
            string outp = Exec.RunCmdGet(new[] { "sc", "qc", name }, Silent);
            if (outp.IndexOf("START_TYPE", StringComparison.Ordinal) < 0) return false; // 不存在
            foreach (var l in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (l.IndexOf("START_TYPE", StringComparison.Ordinal) >= 0)
                    return l.IndexOf("DISABLED", StringComparison.Ordinal) >= 0;
            }
            return false;
        }

        /// <summary>应用设置：enable=true 恢复，false=禁用。</summary>
        public static void Apply(ServiceEntry e, bool enable, Action<string> log)
        {
            log((enable ? "恢复" : "禁用") + " 服务：" + e.Name + " (" + e.Display + ")");
            SetService(e.Name, enable, log);
            log("  [OK]");
        }

        private static void SetService(string name, bool enable, Action<string> log)
        {
            string startType = enable ? "delayed-auto" : "disabled";
            Exec.RunCmd(new[] { "sc", "config", name, "start=" + startType }, log);
            if (enable) Exec.RunCmd(new[] { "sc", "start", name }, log);
            else Exec.RunCmd(new[] { "sc", "stop", name }, log);
        }
    }
}
