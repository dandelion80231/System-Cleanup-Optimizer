using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 主窗口背景模式。
    /// </summary>
    public enum BackgroundMode
    {
        /// <summary>使用内置/自定义背景图（原有行为）。</summary>
        Image,
        /// <summary>纯色背景。</summary>
        Solid,
        /// <summary>线性渐变。</summary>
        LinearGradient,
        /// <summary>径向渐变。</summary>
        RadialGradient,
        /// <summary>网格渐变（多层径向渐变叠加）。</summary>
        MeshGradient
    }

    /// <summary>
    /// 渐变颜色停靠点。
    /// </summary>
    [Serializable]
    public class GradientStopSetting
    {
        /// <summary>16 进制颜色，例如 #16E0BD。</summary>
        public string Color { get; set; } = "#16E0BD";
        /// <summary>停靠点位置 0.0~1.0。</summary>
        public double Offset { get; set; } = 0.0;
    }

    /// <summary>
    /// 网格渐变中的单个颜色光斑。
    /// </summary>
    [Serializable]
    public class MeshBlobSetting
    {
        /// <summary>光斑颜色。</summary>
        public string Color { get; set; } = "#16E0BD";
        /// <summary>光斑中心 X 坐标 0.0~1.0。</summary>
        public double CenterX { get; set; } = 0.5;
        /// <summary>光斑中心 Y 坐标 0.0~1.0。</summary>
        public double CenterY { get; set; } = 0.5;
        /// <summary>光斑半径 0.0~2.0。</summary>
        public double Radius { get; set; } = 0.5;
        /// <summary>不透明度 0.0~1.0。</summary>
        public double Opacity { get; set; } = 1.0;
    }

    /// <summary>
    /// 主窗口背景设置（持久化到 Config\background.json）。
    /// 保持向后兼容：旧版字段 DarkPath/LightPath/DarkOpacity/LightOpacity 继续保留。
    /// </summary>
    [Serializable]
    public class BackgroundSettings
    {
        /// <summary>当前背景模式。</summary>
        public BackgroundMode Mode { get; set; } = BackgroundMode.Image;

        // ---- 图片模式字段（兼容旧版） ----
        public string DarkPath { get; set; } = "";
        public string LightPath { get; set; } = "";
        public double DarkOpacity { get; set; } = 0.55;
        public double LightOpacity { get; set; } = 1.0;

        // ---- 纯色模式 ----
        public string SolidColor { get; set; } = "#10151C";

        // ---- 线性/径向渐变 ----
        public double GradientAngle { get; set; } = 45.0;
        public double RadialCenterX { get; set; } = 0.5;
        public double RadialCenterY { get; set; } = 0.5;
        public double RadialRadiusX { get; set; } = 0.5;
        public double RadialRadiusY { get; set; } = 0.5;

        // ---- 线性渐变中心（0.0~1.0，相对画布）----
        // 与 RadialCenter 平行：线性渐变允许「整体平移位置 + 旋转角度」二者兼得。
        // 笔刷构建公式：StartPoint = 中心 - 0.5·方向向量，EndPoint = 中心 + 0.5·方向向量。
        public double LinearCenterX { get; set; } = 0.5;
        public double LinearCenterY { get; set; } = 0.5;

        public List<GradientStopSetting> Stops { get; set; } = new List<GradientStopSetting>();

        // ---- 网格渐变 ----
        public List<MeshBlobSetting> Blobs { get; set; } = new List<MeshBlobSetting>();
        /// <summary>网格底色（Base color）：无光斑覆盖处的填充色。</summary>
        public string MeshBaseColor { get; set; } = "#0E1116";

        // 类级共享 RNG：避免 CreateDefaultMeshBlobs 等静态方法内反复 new Random() 触发同种子序列。
        // 修复：.NET Framework 的 Random 实例非线程安全，静态单例被多线程并发调用会破坏内部状态并持续返回 0，
        // 导致生成的光斑全部退化成同一位置/半径。改为每线程独立实例（[ThreadStatic] + 懒加载），
        // 既保留「复用实例」的初衷，又彻底规避跨线程竞争。
        [ThreadStatic]
        private static Random _rng;

        /// <summary>获取当前线程的 Random 实例（懒加载，首次访问时才创建）。</summary>
        private static Random Rng => _rng ?? (_rng = new Random(Guid.NewGuid().GetHashCode()));

        /// <summary>创建默认的线性渐变（青色 accent → 深蓝）。</summary>
        public static BackgroundSettings CreateDefaultLinear()
        {
            return new BackgroundSettings
            {
                Mode = BackgroundMode.LinearGradient,
                GradientAngle = 135.0,
                Stops = new List<GradientStopSetting>
                {
                    new GradientStopSetting { Color = "#16E0BD", Offset = 0.0 },
                    new GradientStopSetting { Color = "#10151C", Offset = 1.0 }
                }
            };
        }

        /// <summary>创建默认的网格渐变。</summary>
        public static BackgroundSettings CreateDefaultMesh()
        {
            return new BackgroundSettings
            {
                Mode = BackgroundMode.MeshGradient,
                Blobs = new List<MeshBlobSetting>
                {
                    new MeshBlobSetting { Color = "#16E0BD", CenterX = 0.2, CenterY = 0.2, Radius = 0.6, Opacity = 0.8 },
                    new MeshBlobSetting { Color = "#2563EB", CenterX = 0.8, CenterY = 0.3, Radius = 0.6, Opacity = 0.7 },
                    new MeshBlobSetting { Color = "#7C3AED", CenterX = 0.5, CenterY = 0.8, Radius = 0.7, Opacity = 0.7 }
                }
            };
        }

        /// <summary>解析十六进制颜色为 WPF Color。</summary>
        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Colors.Transparent;
            var s = hex.Trim().TrimStart('#');
            try
            {
                // 支持 3 位 #RGB 与 4 位 #RGBA：每位字符重复一次展开为 6/8 位
                if (s.Length == 3)
                    s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
                else if (s.Length == 4)
                    // 修正：原注释写「本解析器 8 位按 ARGB 读取，故展开为 AARRGGBB」，与下方 8 位分支的实现
                    // （CSS #RRGGBBAA，即 R-G-B-A 顺序）正好相反，展开顺序也跟着写反了——
                    // "#RGBA" 会被展开成 AARRGGBB 再按 RRGGBBAA 解析，得到完全错乱的颜色。
                    // #RGBA 是 CSS 的 R-G-B-A 顺序，8 位分支同样按 RGBA 读取，故这里展开为 RRGGBBAA。
                    s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2], s[3], s[3] });

                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    return Color.FromRgb(r, g, b);
                }
                if (s.Length == 8)
                {
                    // CSS #RRGGBBAA：R-G-B-A 顺序（与 4 位 #RGBA 分支一致），转换为 WPF AARRGGBB
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    byte a = Convert.ToByte(s.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
            return Colors.Transparent;
        }

        /// <summary>颜色转十六进制（#RRGGBB）。</summary>
        public static string ColorToHex(Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>将当前设置序列化为紧凑 JSON（不引入外部 JSON 库）。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Mode\": " + JsonStr(Mode.ToString()) + ",");
            sb.AppendLine("  \"DarkPath\": " + JsonStr(DarkPath) + ",");
            sb.AppendLine("  \"LightPath\": " + JsonStr(LightPath) + ",");
            sb.AppendLine("  \"DarkOpacity\": " + DarkOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"LightOpacity\": " + LightOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"SolidColor\": " + JsonStr(SolidColor) + ",");
            sb.AppendLine("  \"GradientAngle\": " + GradientAngle.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"RadialCenterX\": " + RadialCenterX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"RadialCenterY\": " + RadialCenterY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"RadialRadiusX\": " + RadialRadiusX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"RadialRadiusY\": " + RadialRadiusY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"LinearCenterX\": " + LinearCenterX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"LinearCenterY\": " + LinearCenterY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
            sb.Append("  \"Stops\": [");
            if (Stops != null)
            {
                for (int i = 0; i < Stops.Count; i++)
                {
                    var s = Stops[i];
                    sb.Append("{\"Color\":" + JsonStr(s.Color) + ",\"Offset\":" + s.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                    if (i < Stops.Count - 1) sb.Append(",");
                }
            }
            sb.AppendLine("],");
            sb.Append("  \"Blobs\": [");
            if (Blobs != null)
            {
                for (int i = 0; i < Blobs.Count; i++)
                {
                    var b = Blobs[i];
                    sb.Append("{\"Color\":" + JsonStr(b.Color) +
                        ",\"CenterX\":" + b.CenterX.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"CenterY\":" + b.CenterY.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"Radius\":" + b.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"Opacity\":" + b.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                    if (i < Blobs.Count - 1) sb.Append(",");
                }
            }
            sb.AppendLine("],");
            sb.AppendLine("  \"MeshBaseColor\": " + JsonStr(MeshBaseColor));
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 从手动 JSON 反序列化（兼容旧版只有 DarkPath/LightPath/Opacity 的格式）。
        /// background.json 可被用户直接编辑，属不可信输入：所有数值都会钳制到合法区间
        /// （Offset/Opacity/CenterX/CenterY → [0,1]，Radius → [0.01,10]，NaN/±∞ 回退默认值），
        /// 避免越界值在渲染期抛 GradientStop/Ellipse 尺寸异常。
        /// </summary>
        public static BackgroundSettings FromJson(string json)
        {
            var s = new BackgroundSettings();
            if (string.IsNullOrWhiteSpace(json)) return s;
            try
            {
                string mode = ExtractJsonString(json, "Mode");
                if (!string.IsNullOrEmpty(mode) && System.Enum.TryParse<BackgroundMode>(mode, out var m)) s.Mode = m;
                else s.Mode = BackgroundMode.Image; // 旧版默认图片模式

                var dark = ExtractJsonString(json, "DarkPath");
                if (dark != null) s.DarkPath = dark;
                var light = ExtractJsonString(json, "LightPath");
                if (light != null) s.LightPath = light;

                var dOp = ExtractJsonDouble(json, "DarkOpacity");
                // 修复：不透明度越界会让 Brush.Opacity 赋值抛异常，统一钳到 [0,1]（沿用旧值作为非法值回退）
                if (dOp.HasValue) s.DarkOpacity = ClampRange(dOp.Value, 0.0, 1.0, s.DarkOpacity);
                var lOp = ExtractJsonDouble(json, "LightOpacity");
                if (lOp.HasValue) s.LightOpacity = ClampRange(lOp.Value, 0.0, 1.0, s.LightOpacity);

                var solid = ExtractJsonString(json, "SolidColor");
                if (solid != null) s.SolidColor = solid;

                var ga = ExtractJsonDouble(json, "GradientAngle");
                if (ga.HasValue) s.GradientAngle = ga.Value;
                var rcx = ExtractJsonDouble(json, "RadialCenterX");
                if (rcx.HasValue) s.RadialCenterX = rcx.Value;
                var rcy = ExtractJsonDouble(json, "RadialCenterY");
                if (rcy.HasValue) s.RadialCenterY = rcy.Value;
                var rrx = ExtractJsonDouble(json, "RadialRadiusX");
                if (rrx.HasValue) s.RadialRadiusX = rrx.Value;
                var rry = ExtractJsonDouble(json, "RadialRadiusY");
                if (rry.HasValue) s.RadialRadiusY = rry.Value;
                var lcx = ExtractJsonDouble(json, "LinearCenterX");
                if (lcx.HasValue) s.LinearCenterX = lcx.Value;
                var lcy = ExtractJsonDouble(json, "LinearCenterY");
                if (lcy.HasValue) s.LinearCenterY = lcy.Value;

                s.Stops = ExtractGradientStops(json, "Stops");
                s.Blobs = ExtractMeshBlobs(json, "Blobs");
                var mbc = ExtractJsonString(json, "MeshBaseColor");
                if (mbc != null) s.MeshBaseColor = mbc;
            }
            catch (Exception ex)
            {
                // 修复：原先 catch (Exception ex) { DebugLog.Ignore(ex); } 静默吞掉一切异常，脏 JSON 只表现为「设置莫名回到默认值」且无从排查。
                // 保留「单个字段出错仍返回可用实例」的兼容行为，但输出带上下文的诊断信息。
                DebugLog.Ignore(ex);
                System.Diagnostics.Debug.WriteLine("[BackgroundSettings.FromJson] 解析失败，已回退默认值。JSON 长度=" + (json == null ? -1 : json.Length));
            }
            s.EnsureGradientStops();
            s.EnsureMeshBlobs();
            return s;
        }

        private static string JsonStr(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string ExtractJsonString(string json, string key)
        {
            var idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var start = json.IndexOf('"', colon + 1);
            if (start < 0) return null;
            var end = start + 1;
            while (end < json.Length)
            {
                if (json[end] == '\\') { end += 2; continue; }
                if (json[end] == '"') break;
                end++;
            }
            if (end >= json.Length) return null;
            return json.Substring(start + 1, end - start - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static double? ExtractJsonDouble(string json, string key)
        {
            var idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int j = i;
            while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '.' || json[j] == '-' || json[j] == '+' || json[j] == 'e' || json[j] == 'E')) j++;
            if (double.TryParse(json.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            return null;
        }

        private static List<GradientStopSetting> ExtractGradientStops(string json, string key)
        {
            var list = new List<GradientStopSetting>();
            var idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return list;
            var start = json.IndexOf('[', idx);
            var end = FindBracketEnd(json, start, '[', ']');
            if (start < 0 || end <= start) return list;
            var inner = json.Substring(start + 1, end - start - 1);
            var items = SplitJsonObjects(inner);
            foreach (var item in items)
            {
                var color = ExtractJsonString(item, "Color");
                var off = ExtractJsonDouble(item, "Offset");
                if (color == null) color = "#16E0BD";
                // 修复：Offset 必须落在 [0,1]，否则 new GradientStop(...) 会抛 ArgumentException
                list.Add(new GradientStopSetting { Color = color, Offset = ClampRange(off ?? 0.0, 0.0, 1.0, 0.0) });
            }
            return list;
        }

        private static List<MeshBlobSetting> ExtractMeshBlobs(string json, string key)
        {
            var list = new List<MeshBlobSetting>();
            var idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return list;
            var start = json.IndexOf('[', idx);
            var end = FindBracketEnd(json, start, '[', ']');
            if (start < 0 || end <= start) return list;
            var inner = json.Substring(start + 1, end - start - 1);
            var items = SplitJsonObjects(inner);
            foreach (var item in items)
            {
                var color = ExtractJsonString(item, "Color") ?? "#16E0BD";
                var radius = ExtractJsonDouble(item, "Radius") ?? 0.5;
                // 修复：JSON 路径此前完全不校验范围。负半径会让 Ellipse 尺寸为负、渲染期抛异常
                // （UI 侧有 Math.Max(0.05, …) 兜底，JSON 侧没有）；半径取绝对值后再钳到 [0.01, 10]。
                // 中心坐标钳到 [0,1] 防止光斑被推到画布外，不透明度钳到 [0,1] 防止 Color.FromArgb 算出非法 alpha。
                list.Add(new MeshBlobSetting
                {
                    Color = color,
                    CenterX = ClampRange(ExtractJsonDouble(item, "CenterX") ?? 0.5, 0.0, 1.0, 0.5),
                    CenterY = ClampRange(ExtractJsonDouble(item, "CenterY") ?? 0.5, 0.0, 1.0, 0.5),
                    Radius = ClampRange(Math.Abs(radius), MinBlobRadius, MaxBlobRadius, 0.5),
                    Opacity = ClampRange(ExtractJsonDouble(item, "Opacity") ?? 1.0, 0.0, 1.0, 1.0)
                });
            }
            return list;
        }

        private static int FindBracketEnd(string json, int start, char open, char close)
        {
            if (start < 0) return -1;
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') inStr = !inStr;
                if (inStr) continue;
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static List<string> SplitJsonObjects(string inner)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(inner)) return list;
            int start = -1, depth = 0;
            bool inStr = false;
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '"') inStr = !inStr;
                if (inStr) continue;
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        list.Add(inner.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }
            return list;
        }

        /// <summary>确保渐变至少有两个停靠点。</summary>
        public void EnsureGradientStops()
        {
            if (Stops == null) Stops = new List<GradientStopSetting>();
            if (Stops.Count == 0)
            {
                Stops.Add(new GradientStopSetting { Color = "#16E0BD", Offset = 0.0 });
                Stops.Add(new GradientStopSetting { Color = "#10151C", Offset = 1.0 });
            }
            else if (Stops.Count == 1)
            {
                Stops.Add(new GradientStopSetting { Color = Stops[0].Color, Offset = 1.0 });
            }
        }

        /// <summary>确保网格渐变至少有一个光斑（安全兜底：避免渲染空列表报错）。</summary>
        public void EnsureMeshBlobs()
        {
            if (Blobs == null) Blobs = new List<MeshBlobSetting>();
            if (Blobs.Count == 0)
            {
                Blobs.Add(new MeshBlobSetting { Color = "#16E0BD", CenterX = 0.5, CenterY = 0.5, Radius = 0.5, Opacity = 1.0 });
            }
        }

        /// <summary>
        /// 基于主色生成 4 个不同位置/颜色的默认光斑（四色组和谐色：0/90/180/270°），
        /// 让用户在进入网格模式第一眼看到的就是 multi-blob mesh，而不是单色径向。
        /// 位置在画布四角附近均匀分布并加小随机偏移；半径 0.3~0.5，不透明度 0.6~0.9。
        /// 返回的光斑是普通 MeshBlobSetting，保存后与手工添加的光斑无差别。
        /// </summary>
        public static List<MeshBlobSetting> CreateDefaultMeshBlobs(string baseColorHex)
        {
            var baseColor = ParseColor(baseColorHex);
            RgbToHsl(baseColor.R, baseColor.G, baseColor.B, out double h, out double s, out double l);
            if (s < 0.05) s = 0.7;   // 灰阶主色时给默认饱和度，保证光斑可见
            if (l < 0.15) l = 0.55;  // 太暗的主色时提亮，避免光斑几乎看不见
            if (l > 0.92) l = 0.7;   // 太亮时略压暗，避免与窗口底色混淆

            // 四角位置（加小随机偏移后均匀铺开）
            double[,] positions = {
                { 0.25, 0.25 },
                { 0.75, 0.25 },
                { 0.25, 0.75 },
                { 0.75, 0.75 }
            };
            double[] hueOffsets = { 0, 90, 180, 270 }; // 四色组
            var rnd = Rng;
            var blobs = new List<MeshBlobSetting>();
            for (int i = 0; i < 4; i++)
            {
                double hue = ((h + hueOffsets[i]) % 360 + 360) % 360;
                var c = HslToRgb(hue, s, l);
                double ox = (rnd.NextDouble() - 0.5) * 0.1; // ±0.05
                double oy = (rnd.NextDouble() - 0.5) * 0.1;
                double cx = Clamp01(positions[i, 0] + ox);
                double cy = Clamp01(positions[i, 1] + oy);
                double radius = 0.3 + rnd.NextDouble() * 0.2;  // 0.3~0.5
                double opacity = 0.6 + rnd.NextDouble() * 0.3; // 0.6~0.9
                blobs.Add(new MeshBlobSetting
                {
                    Color = ColorToHex(c),
                    CenterX = cx,
                    CenterY = cy,
                    Radius = radius,
                    Opacity = opacity
                });
            }
            return blobs;
        }

        /// <summary>深拷贝一份设置（弹窗编辑用，避免直接改运行时实例）。</summary>
        public BackgroundSettings Clone()
        {
            return new BackgroundSettings
            {
                Mode = Mode,
                DarkPath = DarkPath,
                LightPath = LightPath,
                DarkOpacity = DarkOpacity,
                LightOpacity = LightOpacity,
                SolidColor = SolidColor,
                GradientAngle = GradientAngle,
                RadialCenterX = RadialCenterX,
                RadialCenterY = RadialCenterY,
                RadialRadiusX = RadialRadiusX,
                RadialRadiusY = RadialRadiusY,
                LinearCenterX = LinearCenterX,
                LinearCenterY = LinearCenterY,
                Stops = Stops?.Select(s => new GradientStopSetting { Color = s.Color, Offset = s.Offset }).ToList() ?? new List<GradientStopSetting>(),
                MeshBaseColor = MeshBaseColor,
                Blobs = Blobs?.Select(b => new MeshBlobSetting
                {
                    Color = b.Color,
                    CenterX = b.CenterX,
                    CenterY = b.CenterY,
                    Radius = b.Radius,
                    Opacity = b.Opacity
                }).ToList() ?? new List<MeshBlobSetting>()
            };
        }

        // ===== 预设模板（与 gradients.app 同风格的精选配色方案） =====

        /// <summary>一个网格渐变预设：名称 + 底色 + 一组光斑。</summary>
        [Serializable]
        public class MeshPreset
        {
            public string Name { get; set; } = "";
            public string BaseColor { get; set; } = "#0E1116";
            public List<MeshBlobSetting> Blobs { get; set; } = new List<MeshBlobSetting>();
        }

        private static MeshBlobSetting MB(string hex, double x, double y, double r, double o) =>
            new MeshBlobSetting { Color = hex, CenterX = x, CenterY = y, Radius = r, Opacity = o };

        /// <summary>
        /// 精选的网格渐变预设（底色 + 9 个光斑,渐近 gradients.app 多光斑叠加风格）。
        /// 参数对齐参考站 farthest-corner 策略:半径 0.60~0.95 大面积重叠,边缘 alpha 衰减极低时才与相邻层相遇,
        /// 透明度 0.30~0.55 避免单斑过实。
        /// 修正：原注释称「底色取各光斑 HSL 加权平均后暗化（亮度 0.25~0.40）」，实现并非如此——
        /// 每个预设的 BaseColor 是手工挑选后写死的字面量（如"极光"= #16333F），运行时不做任何 HSL 计算。
        /// 这些字面量的取值风格确实是「贴近光斑色相的暗色」（亮度大致落在 0.25~0.40），以与光斑自然融合、
        /// 避免反差硬边，但它是预先算好固化下来的，不是解析时算出来的。
        /// 修复：原先每次调用都 new 12×9=108 个 MeshBlobSetting + 13 个 List，而预设是恒定不变的数据，
        /// 改为进程内构建一次的静态只读集合（调用方 BackgroundSettingsDialog.ApplyPreset 只读 Name/BaseColor，
        /// 并对每个光斑逐字段复制后再写入 _settings，不会改动缓存对象）。
        /// </summary>
        private static readonly List<MeshPreset> _meshPresets = new List<MeshPreset>
        {
            new MeshPreset { Name = "极光", BaseColor = "#16333F", Blobs = new List<MeshBlobSetting> {
                MB("#2DD4BF", 0.10, 0.12, 0.80, 0.50),
                MB("#38BDF8", 0.88, 0.16, 0.74, 0.46),
                MB("#818CF8", 0.50, 0.08, 0.78, 0.44),
                MB("#34D399", 0.50, 0.90, 0.76, 0.42),
                MB("#60A5FA", 0.10, 0.86, 0.70, 0.40),
                MB("#A78BFA", 0.88, 0.84, 0.68, 0.38),
                MB("#4ADE80", 0.32, 0.50, 0.66, 0.36),
                MB("#22D3EE", 0.68, 0.50, 0.66, 0.36),
                MB("#5EEAD4", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "日落", BaseColor = "#4A2430", Blobs = new List<MeshBlobSetting> {
                MB("#FB923C", 0.10, 0.12, 0.80, 0.50),
                MB("#F472B6", 0.88, 0.16, 0.74, 0.44),
                MB("#FCD34D", 0.50, 0.08, 0.76, 0.42),
                MB("#C084FC", 0.50, 0.90, 0.74, 0.38),
                MB("#F87171", 0.10, 0.86, 0.70, 0.40),
                MB("#FB7185", 0.88, 0.84, 0.68, 0.36),
                MB("#FDBA74", 0.08, 0.50, 0.66, 0.34),
                MB("#E879F9", 0.92, 0.50, 0.64, 0.34),
                MB("#FCA5A5", 0.50, 0.50, 0.95, 0.32) } },
            new MeshPreset { Name = "海洋", BaseColor = "#10324A", Blobs = new List<MeshBlobSetting> {
                MB("#38BDF8", 0.10, 0.12, 0.82, 0.52),
                MB("#2DD4BF", 0.88, 0.16, 0.76, 0.46),
                MB("#60A5FA", 0.50, 0.08, 0.78, 0.44),
                MB("#0EA5E9", 0.50, 0.90, 0.76, 0.42),
                MB("#22D3EE", 0.10, 0.86, 0.70, 0.40),
                MB("#818CF8", 0.88, 0.84, 0.68, 0.36),
                MB("#0284C7", 0.08, 0.50, 0.70, 0.38),
                MB("#5EEAD4", 0.92, 0.50, 0.64, 0.34),
                MB("#7DD3FC", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "森林", BaseColor = "#1E3A2A", Blobs = new List<MeshBlobSetting> {
                MB("#4ADE80", 0.10, 0.12, 0.80, 0.50),
                MB("#A3E635", 0.88, 0.16, 0.74, 0.42),
                MB("#34D399", 0.50, 0.08, 0.78, 0.44),
                MB("#10B981", 0.50, 0.90, 0.78, 0.42),
                MB("#86EFAC", 0.10, 0.86, 0.70, 0.38),
                MB("#65A30D", 0.88, 0.84, 0.68, 0.36),
                MB("#166534", 0.08, 0.50, 0.72, 0.36),
                MB("#BEF264", 0.92, 0.50, 0.64, 0.32),
                MB("#6EE7B7", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "霓虹", BaseColor = "#3A1B4A", Blobs = new List<MeshBlobSetting> {
                MB("#F472B6", 0.10, 0.12, 0.80, 0.48),
                MB("#22D3EE", 0.88, 0.16, 0.74, 0.44),
                MB("#A78BFA", 0.50, 0.08, 0.78, 0.42),
                MB("#FDE047", 0.50, 0.90, 0.74, 0.38),
                MB("#E879F9", 0.10, 0.86, 0.70, 0.38),
                MB("#67E8F9", 0.88, 0.84, 0.68, 0.34),
                MB("#C084FC", 0.08, 0.50, 0.68, 0.36),
                MB("#FDA4AF", 0.92, 0.50, 0.64, 0.32),
                MB("#F0ABFC", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "黄昏", BaseColor = "#3A2A45", Blobs = new List<MeshBlobSetting> {
                MB("#FBBF24", 0.10, 0.12, 0.80, 0.48),
                MB("#FB7185", 0.88, 0.16, 0.74, 0.42),
                MB("#A5B4FC", 0.50, 0.08, 0.78, 0.42),
                MB("#F9A8D4", 0.50, 0.90, 0.74, 0.38),
                MB("#C084FC", 0.10, 0.86, 0.70, 0.36),
                MB("#FCD34D", 0.88, 0.84, 0.68, 0.34),
                MB("#818CF8", 0.08, 0.50, 0.68, 0.36),
                MB("#FDBA74", 0.92, 0.50, 0.64, 0.32),
                MB("#FCA5A5", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "薄荷", BaseColor = "#1F4440", Blobs = new List<MeshBlobSetting> {
                MB("#5EEAD4", 0.10, 0.12, 0.80, 0.50),
                MB("#99F6E4", 0.88, 0.16, 0.74, 0.42),
                MB("#2DD4BF", 0.50, 0.08, 0.78, 0.44),
                MB("#14B8A6", 0.50, 0.90, 0.78, 0.42),
                MB("#A7F3D0", 0.10, 0.86, 0.70, 0.38),
                MB("#0D9488", 0.88, 0.84, 0.68, 0.36),
                MB("#6EE7B7", 0.08, 0.50, 0.66, 0.34),
                MB("#CCFBF1", 0.92, 0.50, 0.64, 0.32),
                MB("#7DD3FC", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "玫瑰金", BaseColor = "#4A3230", Blobs = new List<MeshBlobSetting> {
                MB("#FBD5B5", 0.10, 0.12, 0.80, 0.46),
                MB("#F6AD7F", 0.88, 0.16, 0.74, 0.42),
                MB("#FCA5A5", 0.50, 0.08, 0.76, 0.40),
                MB("#E8A87C", 0.50, 0.90, 0.76, 0.38),
                MB("#F8C8B8", 0.10, 0.86, 0.70, 0.36),
                MB("#D97706", 0.88, 0.84, 0.68, 0.34),
                MB("#F5C6A5", 0.08, 0.50, 0.66, 0.34),
                MB("#FDBA74", 0.92, 0.50, 0.62, 0.32),
                MB("#F9A8D4", 0.50, 0.50, 0.95, 0.32) } },
            new MeshPreset { Name = "草莓", BaseColor = "#4A1A24", Blobs = new List<MeshBlobSetting> {
                MB("#FB7185", 0.10, 0.12, 0.80, 0.50),
                MB("#F43F5E", 0.88, 0.16, 0.74, 0.46),
                MB("#FDA4AF", 0.50, 0.08, 0.78, 0.44),
                MB("#FCA5A5", 0.50, 0.90, 0.76, 0.42),
                MB("#F9A8D4", 0.10, 0.86, 0.70, 0.40),
                MB("#F472B6", 0.88, 0.84, 0.68, 0.38),
                MB("#EF4444", 0.08, 0.50, 0.66, 0.36),
                MB("#FBBF24", 0.92, 0.50, 0.64, 0.34),
                MB("#FECACA", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "紫晶", BaseColor = "#2E1A47", Blobs = new List<MeshBlobSetting> {
                MB("#A78BFA", 0.10, 0.12, 0.80, 0.50),
                MB("#8B5CF6", 0.88, 0.16, 0.74, 0.46),
                MB("#C084FC", 0.50, 0.08, 0.78, 0.44),
                MB("#D8B4FE", 0.50, 0.90, 0.76, 0.42),
                MB("#7C3AED", 0.10, 0.86, 0.70, 0.40),
                MB("#6D28D9", 0.88, 0.84, 0.68, 0.38),
                MB("#F0ABFC", 0.08, 0.50, 0.66, 0.36),
                MB("#E879F9", 0.92, 0.50, 0.64, 0.34),
                MB("#DDD6FE", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "沙漠", BaseColor = "#4A2C20", Blobs = new List<MeshBlobSetting> {
                MB("#FDBA74", 0.10, 0.12, 0.80, 0.50),
                MB("#FB923C", 0.88, 0.16, 0.74, 0.46),
                MB("#FCD34D", 0.50, 0.08, 0.78, 0.44),
                MB("#F87171", 0.50, 0.90, 0.76, 0.42),
                MB("#F6AD7F", 0.10, 0.86, 0.70, 0.40),
                MB("#D97706", 0.88, 0.84, 0.68, 0.38),
                MB("#FDE68A", 0.08, 0.50, 0.66, 0.36),
                MB("#FEF3C7", 0.92, 0.50, 0.64, 0.34),
                MB("#FDBA74", 0.50, 0.50, 0.95, 0.34) } },
            new MeshPreset { Name = "冰川", BaseColor = "#1A3A4A", Blobs = new List<MeshBlobSetting> {
                MB("#BAE6FD", 0.10, 0.12, 0.80, 0.50),
                MB("#7DD3FC", 0.88, 0.16, 0.74, 0.46),
                MB("#38BDF8", 0.50, 0.08, 0.78, 0.44),
                MB("#CFFAFE", 0.50, 0.90, 0.76, 0.42),
                MB("#A5F3FC", 0.10, 0.86, 0.70, 0.40),
                MB("#22D3EE", 0.88, 0.84, 0.68, 0.38),
                MB("#E0F2FE", 0.08, 0.50, 0.66, 0.36),
                MB("#60A5FA", 0.92, 0.50, 0.64, 0.34),
                MB("#F0F9FF", 0.50, 0.50, 0.95, 0.34) } }
        };

        /// <summary>返回精选的网格渐变预设（返回进程内共享的只读预设集合，请勿修改其中元素）。</summary>
        public static List<MeshPreset> GetMeshPresets()
        {
            return _meshPresets;
        }

        /// <summary>
        /// 把网格渐变导出为 SVG（忠实复刻 WPF 渲染：底色矩形 + 多层正圆径向渐变椭圆，三段 0→α/4→0 柔边、无模糊）。
        /// 几何与衰减曲线均与 MainWindow.Theme.cs 的 PopulateBlobCanvas 对齐，保证「所见即所得」。
        /// </summary>
        public static string ToSvg(List<MeshBlobSetting> blobs, string baseColor, int w = 1200, int h = 800)
        {
            if (blobs == null || blobs.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">", w, h));
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  <rect width=\"{0}\" height=\"{1}\" fill=\"{2}\"/>", w, h, baseColor ?? "#0E1116"));
            sb.AppendLine("  <defs>");
            // 修复（与 MainWindow.Theme.cs 的 PopulateBlobCanvas 保持一致）：
            // WPF 侧明确「不使用 BlurEffect」，平滑度完全靠 RadialGradientBrush 的 0→α/4→0 三段柔边堆叠，
            // 而旧版 SVG 却额外套了 stdDeviation = min(w,h)*0.05 的高斯模糊（1200×800 时高达 40px），
            // 导致导出图整体发虚、光斑边界被抹开，与屏幕渲染差异明显。
            // 这里保留 filter 节点（删除可能影响个别解析器的引用完整性），但把 stdDeviation 设为 0，即模糊为空操作。
            sb.AppendLine("    <filter id=\"bgBlur\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\">");
            sb.AppendLine("      <feGaussianBlur stdDeviation=\"0\"/>");
            sb.AppendLine("    </filter>");
            for (int i = 0; i < blobs.Count; i++)
            {
                var b = blobs[i];
                var c = ParseColor(b.Color);
                double a = Clamp01(b.Opacity);
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "    <radialGradient id=\"bg{0}\" cx=\"50%\" cy=\"50%\" r=\"50%\">", i));
                // 修复（与 PopulateBlobCanvas 保持一致）：由 2 段线性衰减改为 3 段停靠点
                // 0% → α、65% → α/4、100% → 0，与 WPF 的 GradientStop(α, 0.0)/(α/4, 0.65)/(0, 1.0) 一一对应，
                // 中间停靠点让衰减前段更平缓、边缘更渐进，消除光斑之间的圆形轮廓感。
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "      <stop offset=\"0%\" stop-color=\"#{0:X2}{1:X2}{2:X2}\" stop-opacity=\"{3}\"/>", c.R, c.G, c.B, a));
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "      <stop offset=\"65%\" stop-color=\"#{0:X2}{1:X2}{2:X2}\" stop-opacity=\"{3}\"/>", c.R, c.G, c.B, a / 4.0));
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "      <stop offset=\"100%\" stop-color=\"#{0:X2}{1:X2}{2:X2}\" stop-opacity=\"0\"/>", c.R, c.G, c.B));
                sb.AppendLine("    </radialGradient>");
            }
            sb.AppendLine("  </defs>");
            // 全部光斑统一包在带滤镜（stdDeviation=0，空操作）的 g 内；单光斑尺寸差异通过 gradient 自身控制
            sb.AppendLine("  <g filter=\"url(#bgBlur)\">");
            for (int i = 0; i < blobs.Count; i++)
            {
                var b = blobs[i];
                double cx = b.CenterX * w;
                double cy = b.CenterY * h;
                // 修复（与 PopulateBlobCanvas 保持一致）：WPF 侧 pr = b.Radius * Math.Min(canvasW, canvasH)，
                // 光斑恒为正圆；旧版 SVG 用 rx = Radius*w、ry = Radius*h，在非正方形画布上会被拉伸成椭圆
                // （1200×800 导出时横向比屏幕渲染宽 1.5 倍）。这里 rx/ry 统一取 Radius * Min(w, h) 保证正圆。
                double rx = b.Radius * Math.Min(w, h);
                double ry = rx;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "    <ellipse cx=\"{0:F1}\" cy=\"{1:F1}\" rx=\"{2:F1}\" ry=\"{3:F1}\" fill=\"url(#bg{4})\"/>", cx, cy, rx, ry, i));
            }
            sb.AppendLine("  </g>");
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>线性插值两个颜色（t=0 返回 a，t=1 返回 b）。用于 Shades/Tints/Tones 生成。</summary>
        public static Color LerpColor(Color a, Color b, double t)
        {
            t = Clamp01(t);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ===== 网格渐变 CSS 导入/导出（与 gradients.app 同构） =====

        /// <summary>
        /// 将光斑列表序列化为与 gradients.app 同构的 CSS：
        /// radial-gradient(at X% Y%, hsla(h,s%,l%,a) 0%, hsla(h,s%,l%,0) 100%)，多层用 ", " 连接。
        /// 注意：该 CSS 格式本身不携带半径字段，导出/导入往返会丢失各光斑的 Radius；导入时默认用 0.5。
        /// </summary>
        public static string ToCssGradient(List<MeshBlobSetting> blobs)
        {
            if (blobs == null || blobs.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (var b in blobs)
            {
                var c = ParseColor(b.Color);
                RgbToHsl(c.R, c.G, c.B, out double h, out double s, out double l);
                double a = Clamp01(b.Opacity);
                parts.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "radial-gradient(at {0:F2}% {1:F2}%, hsla({2:F1},{3:F2}%,{4:F2}%,{5}) 0%, hsla({2:F1},{3:F2}%,{4:F2}%,0) 100%)",
                    b.CenterX * 100.0, b.CenterY * 100.0, h, s * 100.0, l * 100.0, a));
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// 解析 gradients.app 导出的多层 radial-gradient CSS 为光斑列表。
        /// 支持 hsla/hsl、rgba/rgb、#rrggbb/#rgb/#rrggbbaa、transparent。
        /// 每个径向层 → 一个光斑：X/Y = 百分比/100，Color = 解析色，Opacity = 中心色 alpha（0..1）。
        /// 注意：CSS 格式不携带半径，Radius 默认用 0.5。
        /// 解析失败（没有任何 radial-gradient 层）返回 null，由调用方给出可读错误。
        /// </summary>
        public static List<MeshBlobSetting> ParseCssGradient(string css)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(css)) return null;
                var layers = ExtractFunctionCalls(css, "radial-gradient");
                if (layers == null || layers.Count == 0) return null;

                var blobs = new List<MeshBlobSetting>();
                foreach (var layer in layers)
                {
                    double x = 0.5, y = 0.5;
                    var am = Regex.Match(layer, @"at\s+([\d.]+)\s*%\s+([\d.]+)\s*%", RegexOptions.IgnoreCase);
                    if (am.Success)
                    {
                        x = Clamp01(ParseDoubleOrZero(am.Groups[1].Value) / 100.0);
                        y = Clamp01(ParseDoubleOrZero(am.Groups[2].Value) / 100.0);
                    }

                    // 取第一个颜色段（0% 处）作为光斑主色，其 alpha 作为不透明度
                    Color col = ParseColor("#16E0BD");
                    double alpha = 1.0;
                    var segs = SplitTopLevel(layer, ',');
                    foreach (var seg in segs)
                    {
                        if (TryParseColorSegment(seg, out var c, out var a))
                        {
                            col = c;
                            alpha = a;
                            break;
                        }
                    }
                    blobs.Add(new MeshBlobSetting
                    {
                        Color = ColorToHex(col),
                        CenterX = x,
                        CenterY = y,
                        Radius = 0.5,            // CSS 格式不含半径，使用默认
                        Opacity = Clamp01(alpha)
                    });
                }
                if (blobs.Count == 0) return null;
                return blobs;
            }
            catch
            {
                return null;
            }
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        // ---- 反序列化数值校验常量（background.json 是用户可直接编辑的明文本，必须当作不可信输入处理）----

        /// <summary>光斑半径下限：必须 &gt; 0，否则 Ellipse.Width/Height 为负会在 WPF 渲染时抛异常。</summary>
        private const double MinBlobRadius = 0.01;
        /// <summary>光斑半径上限：防止超大数值把渲染尺寸撑爆（Radius 会乘以画布短边）。</summary>
        private const double MaxBlobRadius = 10.0;

        /// <summary>
        /// 把反序列化得到的数值钳制到 [min, max]。
        /// NaN / ±∞ 视为非法值（double.TryParse 会把 "NaN"/"Infinity" 解析成功），回退到 fallback。
        /// 修复要点：JSON 路径此前完全不校验范围，越界 Offset 会让 GradientStop 构造抛 ArgumentException，
        /// 负 Radius 会让 Ellipse 尺寸为负抛异常，且这些异常发生在渲染期而非加载期，极难定位。
        /// </summary>
        private static double ClampRange(double v, double min, double max, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static double ParseDoubleOrZero(string s)
        {
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return 0.0;
        }

        /// <summary>把 RGB(0..255) 转为 HSL：h(0..360), s(0..1), l(0..1)。</summary>
        public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
            double max = Math.Max(rn, Math.Max(gn, bn));
            double min = Math.Min(rn, Math.Min(gn, bn));
            double d = max - min;
            l = (max + min) / 2.0;
            if (d < 1e-9) { h = 0; s = 0; return; }
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rn) h = 60 * (((gn - bn) / d) % 6);
            else if (max == gn) h = 60 * ((bn - rn) / d + 2);
            else h = 60 * ((rn - gn) / d + 4);
            if (h < 0) h += 360;
        }

        /// <summary>把 HSL 转回 WPF Color。h(0..360), s/l(0..1)。</summary>
        public static Color HslToRgb(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            s = Clamp01(s);
            l = Clamp01(l);
            if (s < 1e-9)
            {
                byte v = (byte)Math.Round(l * 255);
                return Color.FromRgb(v, v, v);
            }
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        /// <summary>RGB → CMYK（0.0~1.0）。</summary>
        public static void RgbToCmyk(byte r, byte g, byte b, out double c, out double m, out double y, out double k)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            k = 1.0 - Math.Max(rf, Math.Max(gf, bf));
            if (k < 1.0)
            {
                c = (1.0 - rf - k) / (1.0 - k);
                m = (1.0 - gf - k) / (1.0 - k);
                y = (1.0 - bf - k) / (1.0 - k);
            }
            else
            {
                c = m = y = 0.0;
            }
        }

        /// <summary>解析一个 CSS 颜色段（可能带末尾百分比，如 "hsla(...) 0%"），返回颜色与 alpha。</summary>
        private static bool TryParseColorSegment(string seg, out Color color, out double alpha)
        {
            color = Colors.Black;
            alpha = 1.0;
            seg = seg.Trim();
            if (string.IsNullOrEmpty(seg)) return false;

            if (seg.StartsWith("transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = Colors.Black;
                alpha = 0.0;
                return true;
            }

            var m = Regex.Match(seg, @"#([0-9a-fA-F]{3,8})");
            if (m.Success)
            {
                string hex = m.Groups[1].Value;
                if (hex.Length == 8)
                {
                    var c = ParseColor(hex);
                    color = c;
                    alpha = c.A / 255.0;
                    return true;
                }
                color = ParseColor(hex);
                alpha = 1.0;
                return true;
            }

            m = Regex.Match(seg, @"(hsla?)\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var parts = m.Groups[2].Value.Split(',');
                if (parts.Length >= 3)
                {
                    double h = ParseDoubleOrZero(parts[0]);
                    double s = ParsePct(parts[1]);
                    double l = ParsePct(parts[2]);
                    alpha = parts.Length >= 4 ? ParseAlphaOrZero(parts[3]) : 1.0;
                    color = HslToRgb(h, s, l);
                    return true;
                }
            }

            m = Regex.Match(seg, @"(rgba?)\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var parts = m.Groups[2].Value.Split(',');
                if (parts.Length >= 3)
                {
                    byte r = (byte)Math.Round(ParseDoubleOrZero(parts[0]));
                    byte g = (byte)Math.Round(ParseDoubleOrZero(parts[1]));
                    byte b = (byte)Math.Round(ParseDoubleOrZero(parts[2]));
                    alpha = parts.Length >= 4 ? ParseAlphaOrZero(parts[3]) : 1.0;
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
            }

            return false;
        }

        private static double ParsePct(string s)
        {
            s = s.Trim();
            if (s.EndsWith("%", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 1);
            return Clamp01(ParseDoubleOrZero(s) / 100.0);
        }

        /// <summary>解析颜色 alpha（0..1）：支持无单位 CSS 小数（如 0.8）与百分比（如 80%）。</summary>
        private static double ParseAlphaOrZero(string s)
        {
            s = s.Trim();
            if (s.EndsWith("%", StringComparison.OrdinalIgnoreCase))
                return Clamp01(ParseDoubleOrZero(s.Substring(0, s.Length - 1)) / 100.0);
            return Clamp01(ParseDoubleOrZero(s));
        }

        /// <summary>按顶层逗号切分（忽略括号/函数内的逗号）。</summary>
        private static List<string> SplitTopLevel(string text, char sep)
        {
            var list = new List<string>();
            int depth = 0;
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c == '(') depth++;
                else if (c == ')') { if (depth > 0) depth--; }
                if (c == sep && depth == 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>提取所有名为 name 的函数的括号内容（如所有 radial-gradient(...) 内部）。</summary>
        private static List<string> ExtractFunctionCalls(string text, string name)
        {
            var result = new List<string>();
            string marker = name + "(";
            int idx = 0;
            while ((idx = text.IndexOf(marker, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int start = idx + marker.Length;
                int depth = 1, i = start;
                while (i < text.Length && depth > 0)
                {
                    char c = text[i];
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    i++;
                }
                if (depth == 0 && i > start)
                    result.Add(text.Substring(start, i - start - 1));
                idx = i;
            }
            return result;
        }
    }
}
