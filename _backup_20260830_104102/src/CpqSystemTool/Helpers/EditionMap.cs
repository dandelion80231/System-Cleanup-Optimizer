using System;
using System.Collections.Generic;
using System.Linq;

namespace CpqSystemTool
{
    /// <summary>
    /// Windows 版本名中英映射（R4c 收编，消除 Shotgun Surgery）。
    /// 合并了三处重复字典为单一权威源 <see cref="EnglishToChinese"/>，并派生双向查询：
    ///  - 原 MainWindow.SystemTools.cs 的 ChineseEditionName（英文 → 中文，24 键，UI 标签）；
    ///  - 原 Modules/SystemInfo.cs 的 EditionChineseMap（英文 → 中文，38 键，系统信息页）；
    ///  - 原 Modules/VersionSwitch.cs 的 MapChineseToEnglish（中文 → 英文，30 键，版本转换）。
    /// 权威源键取三处并集（去重），中文值冲突项保留"调用最多的语义"（见各条注释）；
    /// <see cref="ChineseToEnglish"/> 由权威源反向派生，供 VersionSwitch 方向使用。
    /// </summary>
    public static class EditionMap
    {
        // 标准 EditionID 优先序：反向派生时同名中文值多英文键取标准键，
        // 保证与 GVLK 密钥表 / 原 ChineseEditionName 键对齐（口语键如 "Pro" / "Pro for Workstations" 仅用于 SystemInfo 显示）。
        private static readonly HashSet<string> StandardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Professional", "Professional N", "ProfessionalWorkstation", "ProfessionalWorkstation N",
            "ProfessionalEducation", "ProfessionalEducation N", "ProfessionalSingleLanguage",
            "ProfessionalCountrySpecific", "Enterprise", "Enterprise N", "EnterpriseG", "EnterpriseGN",
            "EnterpriseS", "ServerRdsh", "IoTEnterprise", "Education", "Education N",
            "Home", "Home N", "Home Single Language", "Home China",
            "Core", "Core N", "CoreSingleLanguage",
        };

        /// <summary>单一权威源：英文 EditionID/ProductName → 中文显示名（OrdinalIgnoreCase 键比较）。</summary>
        public static readonly IReadOnlyDictionary<string, string> EnglishToChinese =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 工作站版（含 N）
                { "ProfessionalWorkstationN", "专业工作站版 N" },
                { "ProfessionalWorkstation", "专业工作站版" },
                { "ProfessionalWorkstation N", "专业工作站版 N" },
                { "Pro for Workstations N", "专业工作站版 N" },
                { "Pro for Workstations", "专业工作站版" },
                // 专业版（含 N / 教育 / 单语言 / 中文）
                { "ProfessionalEducationN", "专业教育版 N" },
                { "ProfessionalEducation", "专业教育版" },
                { "ProfessionalEducation N", "专业教育版 N" },
                { "ProfessionalN", "专业版 N" },
                { "Professional", "专业版" },
                { "Professional N", "专业版 N" },
                { "Pro N", "专业版 N" },
                { "Pro", "专业版" },
                { "ProfessionalSingleLanguage", "专业单语言版" },
                { "ProfessionalCountrySpecific", "专业中文版" },
                // 企业版（含 N / S(LTSC) / G / 评估 / 多会话）
                { "EnterpriseSN", "企业版 SN" },
                // 冲突项：ChineseEditionName="企业版 LTSC"、EditionChineseMap="企业版 S"；
                // VersionSwitch 注释/中文键亦为"企业 LTSC"，取"企业版 LTSC"。
                { "EnterpriseS", "企业版 LTSC" },
                { "EnterpriseEvaluation", "企业版 评估版" },
                { "EnterpriseN", "企业版 N" },
                { "Enterprise", "企业版" },
                { "Enterprise N", "企业版 N" },
                { "EnterpriseG", "企业版 G" },
                { "EnterpriseGN", "企业版 G N" },
                // 冲突项：ChineseEditionName="虚拟桌面版"、EditionChineseMap="企业版多会话"；
                // VersionSwitch 中文键亦为"虚拟桌面版"，取"虚拟桌面版"。
                { "ServerRdsh", "虚拟桌面版" },
                // 教育版（含 N）
                { "EducationN", "教育版 N" },
                { "Education", "教育版" },
                { "Education N", "教育版 N" },
                // 家庭版（含单语言 / 中国 / N）；Core 系为 Home 的内部 EditionID。
                // 冲突项：Core 系列 ChineseEditionName 用"核心版"、EditionChineseMap 用"家庭版"；
                // 取"核心版"以避免与 Home 中文名冲突导致反向解析歧义（"核心版"→Core 唯一）。
                { "CoreSingleLanguage", "核心单语言版" },
                { "CoreCountrySpecific", "家庭中文版" },
                { "CoreN", "核心版 N" },
                { "Core N", "核心版 N" },
                { "HomeSingleLanguage", "家庭单语言版" },
                { "Home", "家庭版" },
                { "Home N", "家庭版 N" },
                { "Home Single Language", "家庭单语言版" },
                { "Home China", "家庭中文版" },
                { "Core", "核心版" },
                // S 模式（Cloud）
                { "CloudN", "S 模式版 N" },
                { "Cloud", "S 模式版" },
                // IoT 企业版
                { "IoTEnterpriseS", "IoT 企业版 S" },
                { "IoTEnterprise", "IoT 企业版" },
                // 服务器
                { "ServerDatacenter", "服务器数据中心版" },
                { "ServerStandard", "服务器标准版" },
                { "Server", "服务器版" },
                // 其它
                { "Ultimate", "旗舰版" },
            };

        /// <summary>中文版名 → 英文版名（由权威源反向派生 + 历史中文名变体补充），供 VersionSwitch 反向查询。</summary>
        public static readonly IReadOnlyDictionary<string, string> ChineseToEnglish = BuildChineseToEnglish();

        private static IReadOnlyDictionary<string, string> BuildChineseToEnglish()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 同一中文名对应多个英文键时（如 "家庭中文版" 来自 Home China / CoreCountrySpecific），
            // 取标准 EditionID（Home 系等），保证 VersionSwitch 解析结果与 GVLK 键一致。
            foreach (var group in EnglishToChinese.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            {
                string pick = group.Select(g => g.Key)
                    .OrderByDescending(k => StandardKeys.Contains(k))
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .First();
                m[group.Key] = pick;
            }
            // 历史中文名变体（原 MapChineseToEnglish 独有键，无法由反向映射覆盖）
            m["Windows 10 企业 LTSC 版"] = "EnterpriseS";
            m["Windows 11 企业 LTSC 版"] = "EnterpriseS";
            return m;
        }

        /// <summary>
        /// 英文版名 → 中文显示名。null/empty 返回 null（由调用点决定 fallback，如 "(未知)"）；
        /// 兼容 "Windows 10 Pro" / "Windows 11 Home" 等 ProductName 全文；未命中返回原文。
        /// （对应原 MainWindow.SystemTools.cs ChineseEditionName 的完整语义。）
        /// </summary>
        public static string ToChinese(string english)
        {
            if (string.IsNullOrEmpty(english)) return null;
            if (EnglishToChinese.TryGetValue(english, out string cn)) return cn;
            // 支持 "Windows 10 Pro" / "Windows 11 Home" 等 ProductName 全文（剥离 "Windows 10/11 " 前缀）
            if (english.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
            {
                string afterWin = english.Substring("Windows ".Length).Trim();
                int sp = afterWin.IndexOf(' ');
                string shortName = sp >= 0 ? afterWin.Substring(sp + 1).Trim() : afterWin;
                if (EnglishToChinese.TryGetValue(shortName, out cn)) return cn;
            }
            return english;  // 找不到映射返回原文
        }

        /// <summary>
        /// 中文版名 → 英文版名。null 返回 null；输入会 Trim。
        /// 精确查（含 "Windows 10/11 " 前缀形式）→ 剥离前缀再查 → 关键字模糊匹配兜底
        /// （对应原 Modules/VersionSwitch.cs MapChineseToEnglish 的完整语义）。
        /// </summary>
        public static string ToEnglish(string chinese)
        {
            if (chinese == null) return null;
            chinese = chinese.Trim();
            if (ChineseToEnglish.TryGetValue(chinese, out string en)) return en;
            // 剥离 "Windows 10/11 " 前缀再查（原 MapChineseToEnglish 键多为带前缀形式）
            if (chinese.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
            {
                int sp = chinese.IndexOf(' ');
                string shortName = chinese.Substring(sp + 1).Trim();
                if (ChineseToEnglish.TryGetValue(shortName, out en)) return en;
            }
            // 模糊匹配（原 MapChineseToEnglish 语义，兼容中英混合输入）
            // 专业 + 教育 → ProfessionalEducation（必须优先，避免误命中普通 Professional）
            if (chinese.Contains("专业") && chinese.Contains("教育")) return "ProfessionalEducation";
            if (chinese.Contains("专业") && chinese.Contains("工作站")) return "ProfessionalWorkstation";
            if (chinese.Contains("专业") && chinese.Contains("单语言")) return "ProfessionalSingleLanguage";
            if (chinese.Contains("专业") && chinese.Contains("中文")) return "ProfessionalCountrySpecific";
            if (chinese.Contains("专业")) return "Professional";
            if (chinese.Contains("教育")) return "Education";
            return null;
        }
    }
}
