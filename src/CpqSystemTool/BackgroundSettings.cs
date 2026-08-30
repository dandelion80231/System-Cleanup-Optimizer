using System;
using System.Collections.Generic;
using System.Linq;
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
        public List<GradientStopSetting> Stops { get; set; } = new List<GradientStopSetting>();

        // ---- 网格渐变 ----
        public List<MeshBlobSetting> Blobs { get; set; } = new List<MeshBlobSetting>();

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
                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    return Color.FromRgb(r, g, b);
                }
                if (s.Length == 8)
                {
                    byte a = Convert.ToByte(s.Substring(0, 2), 16);
                    byte r = Convert.ToByte(s.Substring(2, 2), 16);
                    byte g = Convert.ToByte(s.Substring(4, 2), 16);
                    byte b = Convert.ToByte(s.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { }
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
            sb.AppendLine("]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>从手动 JSON 反序列化（兼容旧版只有 DarkPath/LightPath/Opacity 的格式）。</summary>
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
                if (dOp.HasValue) s.DarkOpacity = dOp.Value;
                var lOp = ExtractJsonDouble(json, "LightOpacity");
                if (lOp.HasValue) s.LightOpacity = lOp.Value;

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

                s.Stops = ExtractGradientStops(json, "Stops");
                s.Blobs = ExtractMeshBlobs(json, "Blobs");
            }
            catch { }
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
                list.Add(new GradientStopSetting { Color = color, Offset = off ?? 0.0 });
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
                list.Add(new MeshBlobSetting
                {
                    Color = color,
                    CenterX = ExtractJsonDouble(item, "CenterX") ?? 0.5,
                    CenterY = ExtractJsonDouble(item, "CenterY") ?? 0.5,
                    Radius = ExtractJsonDouble(item, "Radius") ?? 0.5,
                    Opacity = ExtractJsonDouble(item, "Opacity") ?? 1.0
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

        /// <summary>确保网格渐变至少有一个光斑。</summary>
        public void EnsureMeshBlobs()
        {
            if (Blobs == null) Blobs = new List<MeshBlobSetting>();
            if (Blobs.Count == 0)
            {
                Blobs.Add(new MeshBlobSetting { Color = "#16E0BD", CenterX = 0.5, CenterY = 0.5, Radius = 0.5, Opacity = 1.0 });
            }
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
                Stops = Stops?.Select(s => new GradientStopSetting { Color = s.Color, Offset = s.Offset }).ToList() ?? new List<GradientStopSetting>(),
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
    }
}
