using System;

namespace CpqSystemTool
{
    /// <summary>
    /// 语义化版本号工具（R4a 收编：原 MainWindow.Pages.cs 的 CompareVersion/NormalizeVersion
    /// 与 Modules/DriverStore.cs 的 CompareVersion/SplitVersion 合并而来）。
    /// 语义取两者并集：
    ///  - Pages.cs：去掉 v/V 前缀、两段式补零（"1.03" → "1.0.3"），每段按整数保序比较，缺失段视作 0；
    ///  - DriverStore.cs：null/空白输入视作空（比较时全段为 0，更健壮），兼容 ',' / 空格分隔，
    ///    无法解析的段视作 0。
    /// </summary>
    public static class VersionUtil
    {
        /// <summary>
        /// 去掉 v/V 前缀并按 '.' 拆成数字段。兼容早期两段简写：当只有两段且第二段数值 ≤ 9 时
        /// （如 "1.03"），在中间补 0 规范为三段（"1.0.3"），以匹配本项目 1.0.x 的版本习惯；
        /// 第二段 &gt; 9（如 "1.10"）则保持原样（视为 1.10.0，避免误判）。
        /// 也兼容 ',' / 空格分隔（DriverStore 语义）；null / 空白返回空数组。
        /// </summary>
        public static string[] NormalizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return Array.Empty<string>();
            var parts = v.TrimStart('v', 'V').Split(new[] { '.', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out int second) && second <= 9)
                return new[] { parts[0], "0", parts[1] };
            return parts;
        }

        /// <summary>
        /// 语义化比较版本号；a&lt;b 返回负数，相等返回 0，a&gt;b 返回正数。
        /// 每段按整数比较，缺失段视作 0，无法解析的段也视作 0（左对齐零填充，即标准 semver 比较）。
        /// </summary>
        public static int CompareVersion(string a, string b)
        {
            var pa = NormalizeVersion(a);
            var pb = NormalizeVersion(b);
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int na = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                int nb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }
    }
}
