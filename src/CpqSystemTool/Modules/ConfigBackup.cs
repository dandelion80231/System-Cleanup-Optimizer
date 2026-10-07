using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 配置导入/导出（含优化后自动保存）。
    /// 对应 ZyperWin++ 的核心卖点「配置还原选项」——把当前开关/勾选状态序列化为 JSON，
    /// 支持导出分享、导入还原、应用后自动落盘。零风险、纯序列化。
    /// 注意：本文件刻意不引用 System.Web（WPF XAML 编译器无法解析该程序集，会导致 MC1000），
    /// JSON 序列化/解析统一使用 Helpers/MiniJson.cs（零依赖）。
    /// </summary>
    public class ToolConfig
    {
        public List<string> EnabledTweaks = new List<string>();
        public List<string> SelectedAppx = new List<string>();
        public List<string> SelectedCleanup = new List<string>();
        public Dictionary<string, string> Flags = new Dictionary<string, string>();
        // 三态优化项状态：id -> "On"/"Off"/"Default"（旧 EnabledTweaks 仅存二态"已启用"项，保留向后兼容）
        public Dictionary<string, string> TweakStates = new Dictionary<string, string>();
    }

    /// <summary>
    /// 极简零依赖 JSON 解析（只解析本工具生成的简单结构：对象 / 字符串数组 / 字符串字典）。
    /// 已收编至 Helpers/MiniJson.cs（ParseToolConfig）。
    /// </summary>

    public static class ConfigBackup
    {
        private static string _configDir = AppPaths.ConfigDir;
        public static string ConfigDir { get => _configDir; set => _configDir = value; }
        public static string AutoPath => Path.Combine(ConfigDir, "autosave.json");

        public static void Save(string path, ToolConfig cfg, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                var json = MiniJson.Serialize(cfg);
                AtomicFile.WriteFileAtomic(path, json);   // tmp + 原子替换，避免崩溃留下半截 JSON
                log("[OK] 已导出配置: " + path);
            }
            catch (Exception ex) { log("[!] 导出失败: " + ex.Message); }
        }

        /// <summary>读取并解析配置。
        /// 契约：文件为空/非本工具配置/读取异常 → 返回 null（由调用方判空中止，不应用空配置）。
        /// 修复（假成功误清优化项）：原实现失败路径返回 <c>new ToolConfig()</c>（非 null），
        /// 使 MainWindow.Config.cs 导入处的 <c>if (cfg == null)</c> 判空成为死代码——
        /// 导入损坏/空/非本工具的 .json 会照常用「空配置」ApplyConfig，把当前所有已启用的非三态优化项全部关掉，
        /// 且 parse 为 null 时仍先打假的 [OK]「已读取配置」。现改为 null 契约 + [OK] 仅在通过校验后打印。</summary>
        public static ToolConfig Load(string path, Action<string> log)
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                // 关键：ParseToolConfig 【永不返回 null】——空串/外来文件也返回全默认 new ToolConfig()，
                // 因此「文件是否本工具配置」必须由 Load 自己判：合法导出（MiniJson.Serialize）恒含全部 5 个已知字段。
                // 要求 ≥2 个已知键同时出现：既挡住空/外来文件，又容忍外来 JSON 恰好含单个同名键（如别的工具的 "Flags"）。
                int knownKeys = 0;
                foreach (var k in new[] { "\"EnabledTweaks\"", "\"SelectedAppx\"", "\"SelectedCleanup\"", "\"Flags\"", "\"TweakStates\"" })
                    if (json != null && json.Contains(k, StringComparison.Ordinal)) knownKeys++;
                if (knownKeys < 2)
                {
                    log("[!] 文件为空或不是本工具的有效配置，未应用任何优化项: " + path);
                    return null;
                }
                var cfg = MiniJson.ParseToolConfig(json);
                log("[OK] 已读取配置: " + path);
                return cfg;
            }
            catch (Exception ex) { log("[!] 读取失败: " + ex.Message); return null; }
        }

        public static void AutoSave(ToolConfig cfg, Action<string> log) => Save(AutoPath, cfg, log);

        public static List<string> ListConfigs()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) return new List<string>();
                var list = new List<string>(Directory.GetFiles(ConfigDir, "*.json"));
                return list;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return new List<string>(); }
        }
    }
}
