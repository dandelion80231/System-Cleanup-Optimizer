using System;
using System.Collections.Generic;
using System.IO;
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
        // 修复（默认偏暗）：DarkOpacity 原 0.55 = 深色图压到 55% 不透明，透出 BgGradient 深底 #10151C，
        // 用户选的深色图几乎被吞没（图偏亮时 45% 深底透出 → 整块过暗；图偏暗时 ≈ 全黑 → 图白选）。
        // 提到 0.85：尊重用户图本身，仅轻叠主题底色，深浅两张图都能被看见。
        public double DarkOpacity { get; set; } = 0.85;
        public double LightOpacity { get; set; } = 1.0;

        // ---- 纯色模式 ----
        // 修复（默认近纯黑）：原默认 #10151C（≈纯黑）作为「纯色」首进显示不合理——
        // 用户切到纯色模式第一眼看到整窗近黑。改为与色轮/主题 accent 一致的 #16E0BD，
        // 纯色模式首进时色轮、HEX、预览三者天然一致（不再失步），观感也明确是有意的配色而非黑屏。
        // 出厂默认纯色：亮色青绿（V≈88%），首进纯色模式不再像纯黑（10/7 由近黑的 #10151C 改来）
        public const string DefaultSolidColor = "#16E0BD";
        // 10/7 之前的旧出厂默认纯色（V≈11%，近纯黑）。老配置里存的 SolidColor 都是这个值
        // 而非用户自选色，载入时识别并迁移（见 FromJson）。
        public const string LegacyDefaultSolidColor = "#10151C";
        public string SolidColor { get; set; } = DefaultSolidColor;
        // 「深浅背景切换」：浅色主题下的纯色（与 SolidColor 平行）。空 = 浅色未单独设置，渲染/编辑时回退用 SolidColor。
        public string LightSolidColor { get; set; } = "";

        // ---- 线性/径向渐变 ----
        public double GradientAngle { get; set; } = 45.0;
        public double RadialCenterX { get; set; } = 0.5;
        public double RadialCenterY { get; set; } = 0.5;
        // 默认 1.0（半径=整屏宽/高，光覆盖全屏；旧默认 0.5 = 光圆半径半屏宽，宽屏下四角大面积落边色，用户指出 1 更合理）
        // 归一化语义：0.5 = 半径为视口宽/高的一半（RelativeToBoundingBox）；CSS 导出按 ×100% 换算
        public double RadialRadiusX { get; set; } = 1.0;
        public double RadialRadiusY { get; set; } = 1.0;

        // ---- 线性渐变中心（0.0~1.0，相对画布）----
        // 与 RadialCenter 平行：线性渐变允许「整体平移位置 + 旋转角度」二者兼得。
        // 笔刷构建公式：StartPoint = 中心 - 0.5·方向向量，EndPoint = 中心 + 0.5·方向向量。
        public double LinearCenterX { get; set; } = 0.5;
        public double LinearCenterY { get; set; } = 0.5;

        public List<GradientStopSetting> Stops { get; set; } = new List<GradientStopSetting>();
        // 「深浅背景切换」：浅色主题下的停靠点（与 Stops 平行）。空 = 回退用 Stops。
        public List<GradientStopSetting> LightStops { get; set; }

        // ---- 网格渐变 ----
        public List<MeshBlobSetting> Blobs { get; set; } = new List<MeshBlobSetting>();
        /// <summary>网格底色（Base color）：无光斑覆盖处的填充色。</summary>
        public string MeshBaseColor { get; set; } = "#0E1116";
        // 「深浅背景切换」：浅色主题下的网格光斑/底色（与 Blobs/MeshBaseColor 平行）。空 = 回退深色套。
        public List<MeshBlobSetting> LightBlobs { get; set; }
        public string LightMeshBaseColor { get; set; } = "";

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
                    new GradientStopSetting { Color = "#16E0BD", Offset = 0.1 },
                    new GradientStopSetting { Color = "#4B7285", Offset = 0.9 }   // 默认深色 V≈52%（旧 #10151C V≈11% 近纯黑——用户：默认 V 应≥50）；偏移 0.1/0.9（0/1 贴边圆点看不见——用户）
                }
            };
        }

        /// <summary>创建默认的网格渐变。
        /// 修复（默认过艳）：原实现 3 个高饱和实色光斑（#16E0BD/#2563EB/#7C3AED，Opacity 0.7~0.8）
        /// 叠在深底上观感偏艳，与系统工具的冷静观感不符。现对齐预设「海洋」的收敛风格
        /// （低不透明度 0.42~0.52、大面积柔和重叠），进入 Mesh 模式第一眼即为 multi-blob 柔和 mesh。</summary>
        public static BackgroundSettings CreateDefaultMesh()
        {
            return new BackgroundSettings
            {
                Mode = BackgroundMode.MeshGradient,
                MeshBaseColor = "#0E1116",
                Blobs = new List<MeshBlobSetting>
                {
                    new MeshBlobSetting { Color = "#38BDF8", CenterX = 0.15, CenterY = 0.15, Radius = 0.80, Opacity = 0.46 },
                    new MeshBlobSetting { Color = "#2DD4BF", CenterX = 0.85, CenterY = 0.20, Radius = 0.72, Opacity = 0.42 },
                    new MeshBlobSetting { Color = "#60A5FA", CenterX = 0.50, CenterY = 0.85, Radius = 0.76, Opacity = 0.42 },
                    new MeshBlobSetting { Color = "#22D3EE", CenterX = 0.50, CenterY = 0.50, Radius = 0.95, Opacity = 0.34 }
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

        /// <summary>颜色转十六进制：不透明输出 6 位 #RRGGBB，带 alpha 输出 8 位 #RRGGBBAA。
        /// 修复：原实现恒输出 6 位，丢弃 c.A——带透明度的颜色（ParseColor 支持 8 位 #RRGGBBAA 解析）
        /// 经「ColorToHex → 存 JSON → 再 ParseColor」往返后 alpha 被静默丢成不透明。
        /// 6 位输出与旧数据完全一致（向后兼容，ParseColor 对 6 位按不透明解析）；8 位仅在 c.A<255 时出现。</summary>
        public static string ColorToHex(Color c)
        {
            return c.A >= 255
                ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        }

        /// <summary>颜色转 6 位 #RRGGBB（忽略 alpha，用于需要纯 6 位输出的 UI 展示场景）。</summary>
        public static string ColorToHexRgb(Color c)
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
            sb.AppendLine("  \"LightSolidColor\": " + JsonStr(LightSolidColor) + ",");
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
            // 浅色停靠点（深浅背景切换）：null 时不输出，保持旧版 JSON 最小
            if (LightStops != null && LightStops.Count > 0)
            {
                sb.Append("  \"LightStops\": [");
                for (int i = 0; i < LightStops.Count; i++)
                {
                    var s = LightStops[i];
                    sb.Append("{\"Color\":" + JsonStr(s.Color) + ",\"Offset\":" + s.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                    if (i < LightStops.Count - 1) sb.Append(",");
                }
                sb.AppendLine("],");
            }
            else
            {
                sb.AppendLine("  \"LightStops\": [],");
            }
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
            if (LightBlobs != null && LightBlobs.Count > 0)
            {
                sb.Append("  \"LightBlobs\": [");
                for (int i = 0; i < LightBlobs.Count; i++)
                {
                    var b = LightBlobs[i];
                    sb.Append("{\"Color\":" + JsonStr(b.Color) +
                        ",\"CenterX\":" + b.CenterX.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"CenterY\":" + b.CenterY.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"Radius\":" + b.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"Opacity\":" + b.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                    if (i < LightBlobs.Count - 1) sb.Append(",");
                }
                sb.AppendLine("],");
            }
            else
            {
                sb.AppendLine("  \"LightBlobs\": [],");
            }
            sb.AppendLine("  \"MeshBaseColor\": " + JsonStr(MeshBaseColor) + ",");
            sb.AppendLine("  \"LightMeshBaseColor\": " + JsonStr(LightMeshBaseColor));
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>数据目录下壁纸存档位置（cpq-tool\壁纸\）。自定义背景图本地化后存放于此，
        /// 随数据文件夹一起迁移（挪 exe 数据自动跟随），避免原图片被移动/删除/换机后背景丢失。</summary>
        public static string WallpaperArchiveDir => AppPaths.WallpaperDir;

        /// <summary>路径是否已在本机数据目录的壁纸存档内（幂等判断；空路径/非法路径返回 false，不抛异常）。</summary>
        public static bool IsLocalImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string localRoot = Path.GetFullPath(WallpaperArchiveDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(localRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// 将图片文件复制进数据目录壁纸存档（cpq-tool\壁纸\），返回存档内路径。
        /// 已在存档内 → 原样返回（幂等）；源文件不存在或复制失败（磁盘满/权限等）→ 返回原路径（降级为引用原件，不阻断用户）。
        /// 同名冲突时追加 _2/_3 序号，不覆盖既有文件。
        /// </summary>
        public static string EnsureImageLocal(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return src;
            if (IsLocalImagePath(src)) return src;
            if (!File.Exists(src)) return src;
            try
            {
                string dir = AppPaths.EnsureDir(WallpaperArchiveDir);
                string name = Path.GetFileName(src);
                string stem = Path.GetFileNameWithoutExtension(name);
                string ext = Path.GetExtension(name);
                string dest = Path.Combine(dir, name);
                int n = 2;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(dir, string.Format("{0}_{1}{2}", stem, n, ext));
                    n++;
                }
                File.Copy(src, dest, false);
                return dest;
            }
            catch { return src; }
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
                if (solid != null)
                {
                    // 一次性迁移（旧出厂默认值）：#10151C 是 10/7 前的出厂默认纯色（V≈11%，近纯黑），
                    // 老配置里存的都是这个值而非用户自选色（用户多次反馈「默认看起来像纯黑」——
                    // 当时只改了新出厂默认 #16E0BD，没迁旧值，老配置打开纯色模式仍是黑，同一类错）。
                    // 仅当值恰好等于旧默认时替换为现默认亮色；其它任何值一律不动。
                    s.SolidColor = solid.Trim().Equals(LegacyDefaultSolidColor, StringComparison.OrdinalIgnoreCase)
                        ? DefaultSolidColor
                        : solid;
                }
                var lSolid = ExtractJsonString(json, "LightSolidColor");
                if (lSolid != null) s.LightSolidColor = lSolid;

                var ga = ExtractJsonDouble(json, "GradientAngle");
                if (ga.HasValue && double.IsFinite(ga.Value)) s.GradientAngle = ga.Value;
                var rcx = ExtractJsonDouble(json, "RadialCenterX");
                if (rcx.HasValue && double.IsFinite(rcx.Value)) s.RadialCenterX = rcx.Value;
                var rcy = ExtractJsonDouble(json, "RadialCenterY");
                if (rcy.HasValue && double.IsFinite(rcy.Value)) s.RadialCenterY = rcy.Value;
                var rrx = ExtractJsonDouble(json, "RadialRadiusX");
                if (rrx.HasValue && double.IsFinite(rrx.Value)) s.RadialRadiusX = rrx.Value;
                var rry = ExtractJsonDouble(json, "RadialRadiusY");
                if (rry.HasValue && double.IsFinite(rry.Value)) s.RadialRadiusY = rry.Value;
                var lcx = ExtractJsonDouble(json, "LinearCenterX");
                if (lcx.HasValue && double.IsFinite(lcx.Value)) s.LinearCenterX = lcx.Value;
                var lcy = ExtractJsonDouble(json, "LinearCenterY");
                if (lcy.HasValue && double.IsFinite(lcy.Value)) s.LinearCenterY = lcy.Value;

                s.Stops = ExtractGradientStops(json, "Stops");
                s.LightStops = ExtractGradientStops(json, "LightStops"); // 旧配置无此键 → 空列表 = 回退深色（GetStops 处理）
                s.Blobs = ExtractMeshBlobs(json, "Blobs");
                s.LightBlobs = ExtractMeshBlobs(json, "LightBlobs");     // 同上
                var mbc = ExtractJsonString(json, "MeshBaseColor");
                if (mbc != null) s.MeshBaseColor = mbc;
                var lmbc = ExtractJsonString(json, "LightMeshBaseColor");
                if (lmbc != null) s.LightMeshBaseColor = lmbc;
            }
            catch (Exception ex)
            {
                // 修复：原先 catch (Exception ex) { DebugLog.Ignore(ex); } 静默吞掉一切异常，脏 JSON 只表现为「设置莫名回到默认值」且无从排查。
                // 保留「单个字段出错仍返回可用实例」的兼容行为，但输出带上下文的诊断信息。
                DebugLog.Ignore(ex);
                System.Diagnostics.Debug.WriteLine("[BackgroundSettings.FromJson] 解析失败，已回退默认值。JSON 长度=" + (json == null ? -1 : json.Length));
            }
            s.EnsureGradientStops();
            // 默认值迁移（f307f7e 把径向默认半径 0.5→1.0 只对新设置生效；存量 json 仍存旧默认 0.5，
            // 打开弹窗显示的还是 0.5——用户：「为什么半径还是显示的 0.5」）：
            // 半径仍为旧默认指纹（X、Y 都是 0.5 = 用户从未显式设置过半径）时，迁到新默认 1.0；
            // 显式设过其他值的不受影响。
            if (s.RadialRadiusX == 0.5 && s.RadialRadiusY == 0.5)
            {
                s.RadialRadiusX = 1.0;
                s.RadialRadiusY = 1.0;
            }
            // 默认停靠点套迁移（用户：默认 0%/100% 圆点贴边看不见 + 默认深色 #10151C 太暗）：
            // 完整默认指纹 = 恰好 2 点 + 偏移 {0,1} + 颜色 {#16E0BD,#10151C} → 重写为新默认套（#16E0BD@0.1 + #4B7285@0.9）；
            // 用户改过任一停靠点（颜色或位置）的不迁移。深浅两套同规则。
            MigrateDefaultStops(s.Stops);
            MigrateDefaultStops(s.LightStops);
            s.EnsureMeshBlobs();
            return s;
        }

        private static string JsonStr(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string ExtractJsonString(string json, string key)
        {
            int idx;
            // 修复（键定位误命中）：原实现 json.IndexOf("\""+key+"\"") 是【子串】查找——若 background.json
            // 被手工编辑、某字符串值内部恰好含 "Mode"/"DarkPath" 等键名子串（如路径/描述里嵌了 "Mode"），
            // 会从字符串值内部误命中，解出脏数据。改为从字符串外部扫描：跟踪引号状态（含 \" 转义），
            // 只在结构上下文中匹配 "key"。
            idx = FindStructKeyIndex(json, key);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx + key.Length + 2);
            if (colon < 0) return null;
            // 跳过冒号后空白，值必须仍是引号起始的字符串（防御"值其实是数字"的脏 JSON）
            int p = colon + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '"') return null;
            var end = p + 1;
            while (end < json.Length)
            {
                if (json[end] == '\\') { end += 2; continue; }
                if (json[end] == '"') break;
                end++;
            }
            if (end >= json.Length) return null;
            return json.Substring(p + 1, end - p - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        /// <summary>返回 key 作为【JSON 对象字段】的键名位置（"key" 的起始引号下标）。
        /// 只在字符串外部匹配：跟踪引号状态（含 \" 转义），字符串值内部出现的同名子串一律忽略。
        /// 找不到返回 -1。</summary>
        private static int FindStructKeyIndex(string json, string key)
        {
            int from = 0;
            while (from < json.Length)
            {
                int idx = json.IndexOf("\"" + key + "\"", from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return -1;
                if (IsOutsideString(json, idx))
                {
                    // 确认是字段名：紧邻其后是 ':'（允许空白），而非嵌套对象值（'{'/'[' 前才是字段，
                    // 值串 "key" 在字符串外部且紧接 ','/'}' 的也接受——但 ToJson 输出中值串总在后）
                    int c = idx + key.Length + 2;
                    while (c < json.Length && char.IsWhiteSpace(json[c])) c++;
                    if (c < json.Length && json[c] == ':') return idx;
                }
                from = idx + 1;
            }
            return -1;
        }

        /// <summary>判断 json[pos]（'"'）处于字符串外部（即它是字段名或值的起始引号，
        /// 且其本身不被外层字符串包裹）。从 0 起跟踪引号状态（含反斜杠转义），O(pos)。</summary>
        private static bool IsOutsideString(string json, int pos)
        {
            bool inStr = false;
            for (int i = 0; i < pos; i++)
            {
                char ch = json[i];
                if (ch == '\\' && inStr) { i++; continue; } // 跳过转义字符
                if (ch == '"') inStr = !inStr;
            }
            return !inStr;
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

        // ---- 深浅主题取套（「深浅背景切换」功能核心）----
        // 浅色套（LightSolidColor/LightStops/LightBlobs/LightMeshBaseColor）为空时一律回退深色主套，
        // 旧配置（无浅色字段）行为与以前完全一致。

        /// <summary>取指定主题下的纯色。dark=true 取 SolidColor；dark=false 取 LightSolidColor，空则回退 SolidColor。</summary>
        public string GetSolidColor(bool dark)
        {
            if (dark) return SolidColor;
            return string.IsNullOrEmpty(LightSolidColor) ? SolidColor : LightSolidColor;
        }

        /// <summary>取指定主题下的停靠点。dark=false 且 LightStops 为空时回退深色 Stops。</summary>
        public List<GradientStopSetting> GetStops(bool dark)
        {
            if (dark) return Stops;
            return (LightStops != null && LightStops.Count > 0) ? LightStops : Stops;
        }

        /// <summary>取指定主题下的网格光斑。dark=false 且 LightBlobs 为空时回退深色 Blobs。</summary>
        public List<MeshBlobSetting> GetBlobs(bool dark)
        {
            if (dark) return Blobs;
            return (LightBlobs != null && LightBlobs.Count > 0) ? LightBlobs : Blobs;
        }

        /// <summary>取指定主题下的网格底色。dark=false 且 LightMeshBaseColor 为空时回退 MeshBaseColor。</summary>
        public string GetMeshBaseColor(bool dark)
        {
            if (dark) return MeshBaseColor;
            return string.IsNullOrEmpty(LightMeshBaseColor) ? MeshBaseColor : LightMeshBaseColor;
        }

        /// <summary>确保渐变至少有两个停靠点。</summary>
        /// <summary>默认套迁移：恰好 2 停靠点 + 偏移 {0,1} + 颜色 {#16E0BD,#10151C}（从未动过的旧默认套）
        /// → 新默认套（#16E0BD@0.1 + #4B7285@0.9）；任一停靠点被用户改过则不动。</summary>
        private static void MigrateDefaultStops(List<GradientStopSetting> stops)
        {
            if (stops == null || stops.Count != 2) return;
            double o0 = stops[0].Offset, o1 = stops[1].Offset;
            bool offDef = (Math.Abs(o0) < 1e-6 && Math.Abs(o1 - 1.0) < 1e-6) || (Math.Abs(o0 - 1.0) < 1e-6 && Math.Abs(o1) < 1e-6);
            if (!offDef) return;
            string c0 = (stops[0].Color ?? "").ToUpperInvariant(), c1 = (stops[1].Color ?? "").ToUpperInvariant();
            bool light0 = c0 == "#16E0BD", light1 = c1 == "#16E0BD";
            if (!((light0 && c1 == "#10151C") || (light1 && c0 == "#10151C"))) return;
            stops[0].Color = light0 ? "#16E0BD" : "#4B7285";
            stops[0].Offset = light0 ? 0.1 : 0.9;
            stops[1].Color = light1 ? "#16E0BD" : "#4B7285";
            stops[1].Offset = light1 ? 0.1 : 0.9;
        }

        public void EnsureGradientStops()
        {
            if (Stops == null) Stops = new List<GradientStopSetting>();
            if (Stops.Count == 0)
            {
                Stops.Add(new GradientStopSetting { Color = "#16E0BD", Offset = 0.1 });
                Stops.Add(new GradientStopSetting { Color = "#4B7285", Offset = 0.9 });   // 同 CreateDefaultLinear
            }
            else if (Stops.Count == 1)
            {
                // 修复：单停靠点时补第二点必须取「互补端」。原实现写死 Offset=1.0——
                // 若仅存的点是 Offset=1.0（尾点），补出的第二点与它同 Offset=1.0，
                // LinearGradientBrush 在 1.0 处已终止，第二个停靠点永不生效（渐变退化为单色）。
                // 现改为：首点（Offset≈0）→ 补 1.0；尾点（Offset≈1）→ 补 0.0；中间点 → 两端各补不对称中点。
                double o = Stops[0].Offset;
                double fill;
                if (o < 0.5) fill = 0.9;
                else if (o > 0.5) fill = 0.1;
                else { // o == 0.5 中间点：补 0.1 端点（下面补第二点，另一端靠用户/预设）
                    fill = 0.1;
                }
                Stops.Add(new GradientStopSetting { Color = Stops[0].Color, Offset = fill });
                Stops = Stops.OrderBy(x => x.Offset).ToList();
            }
        }

        /// <summary>确保网格渐变至少有一个光斑（安全兜底：避免渲染空列表报错）。</summary>
        public void EnsureMeshBlobs()
        {
            if (Blobs == null) Blobs = new List<MeshBlobSetting>();
            if (Blobs.Count == 0)
            {
                Blobs.Add(new MeshBlobSetting { Color = "#16E0BD", CenterX = 0.5, CenterY = 0.5, Radius = 0.9, Opacity = 0.7 });   // 大光斑 + 中不透明（对齐参考站可见度；旧 0.5/1.0 效果差）
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
                // 大半径 + 低不透明（对齐参考实现：CSS farthest-corner 大光斑 + 海洋预设 0.34~0.46 低 α 同风格；
                // 旧 0.3~0.5 小光斑 + 0.6~0.9 高 α = 一坨圆形色块，观感差——用户反馈）
                double radius = 0.7 + rnd.NextDouble() * 0.3;  // 0.7~1.0
                double opacity = 0.55 + rnd.NextDouble() * 0.25; // 0.55~0.80（对齐参考站可见度）
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
                LightSolidColor = LightSolidColor,
                GradientAngle = GradientAngle,
                RadialCenterX = RadialCenterX,
                RadialCenterY = RadialCenterY,
                RadialRadiusX = RadialRadiusX,
                RadialRadiusY = RadialRadiusY,
                LinearCenterX = LinearCenterX,
                LinearCenterY = LinearCenterY,
                Stops = Stops?.Select(s => new GradientStopSetting { Color = s.Color, Offset = s.Offset }).ToList() ?? new List<GradientStopSetting>(),
                LightStops = LightStops?.Select(s => new GradientStopSetting { Color = s.Color, Offset = s.Offset }).ToList(),
                MeshBaseColor = MeshBaseColor,
                LightMeshBaseColor = LightMeshBaseColor,
                Blobs = Blobs?.Select(b => new MeshBlobSetting
                {
                    Color = b.Color,
                    CenterX = b.CenterX,
                    CenterY = b.CenterY,
                    Radius = b.Radius,
                    Opacity = b.Opacity
                }).ToList() ?? new List<MeshBlobSetting>(),
                LightBlobs = LightBlobs?.Select(b => new MeshBlobSetting
                {
                    Color = b.Color,
                    CenterX = b.CenterX,
                    CenterY = b.CenterY,
                    Radius = b.Radius,
                    Opacity = b.Opacity
                }).ToList()
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
                MB("#2DD4BF", 0.10, 0.12, 0.80, 0.80),
                MB("#38BDF8", 0.88, 0.16, 0.74, 0.74),
                MB("#818CF8", 0.50, 0.08, 0.78, 0.70),
                MB("#34D399", 0.50, 0.90, 0.76, 0.67),
                MB("#60A5FA", 0.10, 0.86, 0.70, 0.64),
                MB("#A78BFA", 0.88, 0.84, 0.68, 0.61),
                MB("#4ADE80", 0.32, 0.50, 0.66, 0.58),
                MB("#22D3EE", 0.68, 0.50, 0.66, 0.58),
                MB("#5EEAD4", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "日落", BaseColor = "#4A2430", Blobs = new List<MeshBlobSetting> {
                MB("#FB923C", 0.10, 0.12, 0.80, 0.80),
                MB("#F472B6", 0.88, 0.16, 0.74, 0.70),
                MB("#FCD34D", 0.50, 0.08, 0.76, 0.67),
                MB("#C084FC", 0.50, 0.90, 0.74, 0.61),
                MB("#F87171", 0.10, 0.86, 0.70, 0.64),
                MB("#FB7185", 0.88, 0.84, 0.68, 0.58),
                MB("#FDBA74", 0.08, 0.50, 0.66, 0.54),
                MB("#E879F9", 0.92, 0.50, 0.64, 0.54),
                MB("#FCA5A5", 0.50, 0.50, 0.95, 0.51) } },
            new MeshPreset { Name = "海洋", BaseColor = "#10324A", Blobs = new List<MeshBlobSetting> {
                MB("#38BDF8", 0.10, 0.12, 0.82, 0.83),
                MB("#2DD4BF", 0.88, 0.16, 0.76, 0.74),
                MB("#60A5FA", 0.50, 0.08, 0.78, 0.70),
                MB("#0EA5E9", 0.50, 0.90, 0.76, 0.67),
                MB("#22D3EE", 0.10, 0.86, 0.70, 0.64),
                MB("#818CF8", 0.88, 0.84, 0.68, 0.58),
                MB("#0284C7", 0.08, 0.50, 0.70, 0.61),
                MB("#5EEAD4", 0.92, 0.50, 0.64, 0.54),
                MB("#7DD3FC", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "森林", BaseColor = "#1E3A2A", Blobs = new List<MeshBlobSetting> {
                MB("#4ADE80", 0.10, 0.12, 0.80, 0.80),
                MB("#A3E635", 0.88, 0.16, 0.74, 0.67),
                MB("#34D399", 0.50, 0.08, 0.78, 0.70),
                MB("#10B981", 0.50, 0.90, 0.78, 0.67),
                MB("#86EFAC", 0.10, 0.86, 0.70, 0.61),
                MB("#65A30D", 0.88, 0.84, 0.68, 0.58),
                MB("#166534", 0.08, 0.50, 0.72, 0.58),
                MB("#BEF264", 0.92, 0.50, 0.64, 0.51),
                MB("#6EE7B7", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "霓虹", BaseColor = "#3A1B4A", Blobs = new List<MeshBlobSetting> {
                MB("#F472B6", 0.10, 0.12, 0.80, 0.77),
                MB("#22D3EE", 0.88, 0.16, 0.74, 0.70),
                MB("#A78BFA", 0.50, 0.08, 0.78, 0.67),
                MB("#FDE047", 0.50, 0.90, 0.74, 0.61),
                MB("#E879F9", 0.10, 0.86, 0.70, 0.61),
                MB("#67E8F9", 0.88, 0.84, 0.68, 0.54),
                MB("#C084FC", 0.08, 0.50, 0.68, 0.58),
                MB("#FDA4AF", 0.92, 0.50, 0.64, 0.51),
                MB("#F0ABFC", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "黄昏", BaseColor = "#3A2A45", Blobs = new List<MeshBlobSetting> {
                MB("#FBBF24", 0.10, 0.12, 0.80, 0.77),
                MB("#FB7185", 0.88, 0.16, 0.74, 0.67),
                MB("#A5B4FC", 0.50, 0.08, 0.78, 0.67),
                MB("#F9A8D4", 0.50, 0.90, 0.74, 0.61),
                MB("#C084FC", 0.10, 0.86, 0.70, 0.58),
                MB("#FCD34D", 0.88, 0.84, 0.68, 0.54),
                MB("#818CF8", 0.08, 0.50, 0.68, 0.58),
                MB("#FDBA74", 0.92, 0.50, 0.64, 0.51),
                MB("#FCA5A5", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "薄荷", BaseColor = "#1F4440", Blobs = new List<MeshBlobSetting> {
                MB("#5EEAD4", 0.10, 0.12, 0.80, 0.80),
                MB("#99F6E4", 0.88, 0.16, 0.74, 0.67),
                MB("#2DD4BF", 0.50, 0.08, 0.78, 0.70),
                MB("#14B8A6", 0.50, 0.90, 0.78, 0.67),
                MB("#A7F3D0", 0.10, 0.86, 0.70, 0.61),
                MB("#0D9488", 0.88, 0.84, 0.68, 0.58),
                MB("#6EE7B7", 0.08, 0.50, 0.66, 0.54),
                MB("#CCFBF1", 0.92, 0.50, 0.64, 0.51),
                MB("#7DD3FC", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "玫瑰金", BaseColor = "#4A3230", Blobs = new List<MeshBlobSetting> {
                MB("#FBD5B5", 0.10, 0.12, 0.80, 0.74),
                MB("#F6AD7F", 0.88, 0.16, 0.74, 0.67),
                MB("#FCA5A5", 0.50, 0.08, 0.76, 0.64),
                MB("#E8A87C", 0.50, 0.90, 0.76, 0.61),
                MB("#F8C8B8", 0.10, 0.86, 0.70, 0.58),
                MB("#D97706", 0.88, 0.84, 0.68, 0.54),
                MB("#F5C6A5", 0.08, 0.50, 0.66, 0.54),
                MB("#FDBA74", 0.92, 0.50, 0.62, 0.51),
                MB("#F9A8D4", 0.50, 0.50, 0.95, 0.51) } },
            new MeshPreset { Name = "草莓", BaseColor = "#4A1A24", Blobs = new List<MeshBlobSetting> {
                MB("#FB7185", 0.10, 0.12, 0.80, 0.80),
                MB("#F43F5E", 0.88, 0.16, 0.74, 0.74),
                MB("#FDA4AF", 0.50, 0.08, 0.78, 0.70),
                MB("#FCA5A5", 0.50, 0.90, 0.76, 0.67),
                MB("#F9A8D4", 0.10, 0.86, 0.70, 0.64),
                MB("#F472B6", 0.88, 0.84, 0.68, 0.61),
                MB("#EF4444", 0.08, 0.50, 0.66, 0.58),
                MB("#FBBF24", 0.92, 0.50, 0.64, 0.54),
                MB("#FECACA", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "紫晶", BaseColor = "#2E1A47", Blobs = new List<MeshBlobSetting> {
                MB("#A78BFA", 0.10, 0.12, 0.80, 0.80),
                MB("#8B5CF6", 0.88, 0.16, 0.74, 0.74),
                MB("#C084FC", 0.50, 0.08, 0.78, 0.70),
                MB("#D8B4FE", 0.50, 0.90, 0.76, 0.67),
                MB("#7C3AED", 0.10, 0.86, 0.70, 0.64),
                MB("#6D28D9", 0.88, 0.84, 0.68, 0.61),
                MB("#F0ABFC", 0.08, 0.50, 0.66, 0.58),
                MB("#E879F9", 0.92, 0.50, 0.64, 0.54),
                MB("#DDD6FE", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "沙漠", BaseColor = "#4A2C20", Blobs = new List<MeshBlobSetting> {
                MB("#FDBA74", 0.10, 0.12, 0.80, 0.80),
                MB("#FB923C", 0.88, 0.16, 0.74, 0.74),
                MB("#FCD34D", 0.50, 0.08, 0.78, 0.70),
                MB("#F87171", 0.50, 0.90, 0.76, 0.67),
                MB("#F6AD7F", 0.10, 0.86, 0.70, 0.64),
                MB("#D97706", 0.88, 0.84, 0.68, 0.61),
                MB("#FDE68A", 0.08, 0.50, 0.66, 0.58),
                MB("#FEF3C7", 0.92, 0.50, 0.64, 0.54),
                MB("#FDBA74", 0.50, 0.50, 0.95, 0.54) } },
            new MeshPreset { Name = "冰川", BaseColor = "#1A3A4A", Blobs = new List<MeshBlobSetting> {
                MB("#BAE6FD", 0.10, 0.12, 0.80, 0.80),
                MB("#7DD3FC", 0.88, 0.16, 0.74, 0.74),
                MB("#38BDF8", 0.50, 0.08, 0.78, 0.70),
                MB("#CFFAFE", 0.50, 0.90, 0.76, 0.67),
                MB("#A5F3FC", 0.10, 0.86, 0.70, 0.64),
                MB("#22D3EE", 0.88, 0.84, 0.68, 0.61),
                MB("#E0F2FE", 0.08, 0.50, 0.66, 0.58),
                MB("#60A5FA", 0.92, 0.50, 0.64, 0.54),
                MB("#F0F9FF", 0.50, 0.50, 0.95, 0.54) } }
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
                // 两段线性衰减（0%→α、100%→0）：与 WPF 渲染（PopulateBlobCanvas）及参考实现 CSS 同源
                    "      <stop offset=\"0%\" stop-color=\"#{0:X2}{1:X2}{2:X2}\" stop-opacity=\"{3}\"/>", c.R, c.G, c.B, a));
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
                // 修复（CSS 往返丢半径）：原生 CSS radial-gradient 不携带半径。每层尾部加
                // 本工具私有注释 /* r:<radius> */（标准 CSS 解析器会忽略注释，不破坏兼容性），
                // ParseCssGradient 优先读回该值，保证「导出 → 导入」半径不变。
                parts.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "radial-gradient(at {0:F2}% {1:F2}%, hsla({2:F1},{3:F2}%,{4:F2}%,{5}) 0%, hsla({2:F1},{3:F2}%,{4:F2}%,0) 100%) /* r:{6:F2} */",
                    b.CenterX * 100.0, b.CenterY * 100.0, h, s * 100.0, l * 100.0, a, b.Radius));
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// 解析 gradients.app 导出的多层 radial-gradient CSS 为光斑列表。
        /// 支持 hsla/hsl、rgba/rgb、#rrggbb/#rgb/#rrggbbaa、transparent。
        /// 每个径向层 → 一个光斑：X/Y = 百分比/100，Color = 解析色，Opacity = 中心色 alpha（0..1）。
        /// 半径：原生 CSS 不携带半径，导入按 0.5 还原；若层尾带本工具导出的
        /// /* r:<0.0-1.0> */ 注释（见 ToCssGradient），则取注释值，保证「导出→导入」往返半径不变。
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

                    // 修复（CSS 往返丢半径）：原生 CSS 无半径字段，旧实现一律按 0.5 还原，
                    // 用户「导出 CSS → 再导入」后每个光斑尺寸悄悄变化。现先查层尾
                    // 本工具导出的 /* r:<v> */ 注释，命中则取注释值，未命中（外部 CSS）才回退 0.5。
                    double radius = 0.5;
                    var rm = Regex.Match(layer, @"/\*\s*r:([\d.]+)\s*\*/", RegexOptions.IgnoreCase);
                    if (rm.Success && double.TryParse(rm.Groups[1].Value,
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rv))
                    {
                        radius = ClampRange(rv, MinBlobRadius, MaxBlobRadius, 0.5);
                    }

                    blobs.Add(new MeshBlobSetting
                    {
                        Color = ColorToHex(col),
                        CenterX = x,
                        CenterY = y,
                        Radius = radius,
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

        // ===================== 线性/径向渐变 CSS 导入导出（补全仅网格有的 CSS 往返能力） =====================

        /// <summary>导出线性渐变为标准 CSS：linear-gradient(&lt;cssDeg&gt;deg, &lt;color&gt; &lt;offset&gt;% ...)。
        /// 角度换算：WPF GradientAngle（0°=左→右、90°=底→顶）与 CSS（0deg=向上、90deg=向右、顺时针）相差 90°，
        /// cssDeg = (90 − wpfDeg + 360) % 360，保证浏览器渲染方向与工具内屏幕效果一致。</summary>
        public static string ToCssLinear(double angleDeg, List<GradientStopSetting> stops)
        {
            if (stops == null || stops.Count == 0) return string.Empty;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double cssDeg = (90.0 - angleDeg) % 360.0;
            if (cssDeg < 0) cssDeg += 360.0;
            var parts = stops.OrderBy(s => s.Offset)
                .Select(s => string.Format(inv, "{0} {1}%", ColorToHexRgb(ParseColor(s.Color)), (int)Math.Round(Clamp01(s.Offset) * 100.0)))
                .ToList();
            return string.Format(inv, "linear-gradient({0}deg, {1})", (int)Math.Round(cssDeg), string.Join(", ", parts));
        }

        /// <summary>导出径向渐变为标准 CSS：radial-gradient(&lt;rx&gt;% &lt;ry&gt;% at &lt;cx&gt;% &lt;cy&gt;%, ...)。
        /// 双值 size = 椭圆：WPF 的 RadiusX/RadiusY 为逐轴比例，CSS 百分比半径同为逐轴（宽/高），语义一致。</summary>
        public static string ToCssRadial(double centerX, double centerY, double radiusX, double radiusY, List<GradientStopSetting> stops)
        {
            if (stops == null || stops.Count == 0) return string.Empty;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var parts = stops.OrderBy(s => s.Offset)
                .Select(s => string.Format(inv, "{0} {1}%", ColorToHexRgb(ParseColor(s.Color)), (int)Math.Round(Clamp01(s.Offset) * 100.0)))
                .ToList();
            return string.Format(inv, "radial-gradient({0}% {1}% at {2}% {3}%, {4})",
                (int)Math.Round(Clamp01(radiusX) * 100.0), (int)Math.Round(Clamp01(radiusY) * 100.0),
                (int)Math.Round(Clamp01(centerX) * 100.0), (int)Math.Round(Clamp01(centerY) * 100.0),
                string.Join(", ", parts));
        }

        /// <summary>解析 linear-gradient(...) CSS 为角度（WPF 约定）+ 停靠点。
        /// 支持 deg/turn/grad/rad 角度单位与 to &lt;方向&gt;；无位置的颜色停靠点按均匀分布（CSS auto 放置近似）；颜色格式同 TryParseColorSegment（不支持命名色）。</summary>
        public static bool TryParseCssLinear(string css, out double wpfAngle, out List<GradientStopSetting> stops)
        {
            wpfAngle = 45.0;
            stops = null;
            var layers = ExtractFunctionCalls(css, "linear-gradient");
            if (layers == null || layers.Count == 0) return false;
            var segs = SplitTopLevel(layers[0], ',');
            if (segs.Count == 0) return false;
            var head = segs[0].Trim();
            double cssDeg = 180.0;
            int stopStart = 1;
            var am = Regex.Match(head, @"^(-?[\d.]+)\s*(deg|rad|grad|turn)$");
            if (am.Success)
            {
                cssDeg = ParseDoubleOrZero(am.Groups[1].Value);
                switch (am.Groups[2].Value.ToLowerInvariant())
                {
                    case "turn": cssDeg *= 360.0; break;
                    case "grad": cssDeg *= 0.9; break;
                    case "rad": cssDeg = cssDeg * 180.0 / Math.PI; break;
                }
            }
            else
            {
                var tm = Regex.Match(head, @"^to\s+(top|bottom|left|right)\s*(top|bottom|left|right)?$");
                if (tm.Success)
                    cssDeg = CornerToCssDeg(tm.Groups[1].Value, (tm.Groups[2].Value ?? "").Trim());
                else
                {
                    cssDeg = 180.0;
                    stopStart = 0;
                }
            }
            wpfAngle = ((90.0 - cssDeg) % 360.0 + 360.0) % 360.0;
            stops = ParseCssStops(segs.GetRange(stopStart, segs.Count - stopStart));
            return stops != null;
        }

        /// <summary>解析 radial-gradient(...) CSS（单层、非网格）为中心/半径/停靠点。
        /// 支持「N% N% at A% B%」与「at A% B%」几何前缀；多层网格走 ParseCssGradient（返回 false）；
        /// circle/closest-side 等形状关键字不支持，会解析失败返回 false。</summary>
        public static bool TryParseCssRadial(string css, out double centerX, out double centerY, out double radiusX, out double radiusY, out List<GradientStopSetting> stops)
        {
            centerX = 0.5; centerY = 0.5; radiusX = 0.5; radiusY = 0.5;
            stops = null;
            var layers = ExtractFunctionCalls(css, "radial-gradient");
            if (layers == null || layers.Count != 1) return false;
            var segs = SplitTopLevel(layers[0], ',');
            if (segs.Count == 0) return false;
            var head = segs[0].Trim();
            int stopStart;
            var gm = Regex.Match(head, @"^([\d.]+)\s*%?\s+([\d.]+)\s*%?\s*at\s*([\d.]+)\s*%?\s*([\d.]+)\s*%?\s*$");
            if (gm.Success)
            {
                radiusX = Clamp01(ParseDoubleOrZero(gm.Groups[1].Value));
                radiusY = Clamp01(ParseDoubleOrZero(gm.Groups[2].Value));
                centerX = Clamp01(ParseDoubleOrZero(gm.Groups[3].Value) / 100.0);
                centerY = Clamp01(ParseDoubleOrZero(gm.Groups[4].Value) / 100.0);
                stopStart = 1;
            }
            else
            {
                var am = Regex.Match(head, @"^at\s*([\d.]+)\s*%?\s*([\d.]+)\s*%?\s*$", RegexOptions.IgnoreCase);
                if (am.Success)
                {
                    centerX = Clamp01(ParseDoubleOrZero(am.Groups[1].Value) / 100.0);
                    centerY = Clamp01(ParseDoubleOrZero(am.Groups[2].Value) / 100.0);
                    stopStart = 1;
                }
                else
                {
                    stopStart = 0;
                }
            }
            stops = ParseCssStops(segs.GetRange(stopStart, segs.Count - stopStart));
            return stops != null;
        }

        private static double CornerToCssDeg(string a, string b)
        {
            if (string.IsNullOrEmpty(b))
                return a.ToLowerInvariant() switch { "top" => 0, "right" => 90, "bottom" => 180, "left" => 270, _ => 180 };
            var pair = (a + "-" + b).ToLowerInvariant();
            return pair switch
            {
                "top-right" or "right-top" => 45,
                "bottom-right" or "right-bottom" => 135,
                "bottom-left" or "left-bottom" => 225,
                "top-left" or "left-top" => 315,
                _ => 180
            };
        }

        /// <summary>解析 CSS 颜色停靠点序列（color [position%] ...）：颜色格式走 TryParseColorSegment；
        /// 任一停靠点缺位置时全部按均匀分布（CSS auto 放置近似），带 % 的按百分比钳制到 [0,1]。</summary>
        private static List<GradientStopSetting> ParseCssStops(List<string> segs)
        {
            var result = new List<GradientStopSetting>();
            var explicitPct = new List<double>();
            foreach (var raw in segs)
            {
                var t = (raw ?? "").Trim();
                if (t.Length == 0) continue;
                double pct = -1;
                var pm = Regex.Match(t, @"\s+(-?[\d.]+)\s*(%|px|pt|cm|mm|in|em|rem)?\s*$");
                string colorPart = t;
                if (pm.Success)
                {
                    colorPart = t.Substring(0, pm.Index).Trim();
                    if (pm.Groups[2].Value == "%" || pm.Groups[2].Value == "")
                        pct = ParseDoubleOrZero(pm.Groups[1].Value);
                }
                if (!TryParseColorSegment(colorPart, out var c, out var a)) continue;
                result.Add(new GradientStopSetting { Color = a < 0.999 ? ColorToHex(c) : ColorToHexRgb(c), Offset = 0.0 });
                explicitPct.Add(pct);
            }
            if (result.Count == 0) return null;
            bool anyExplicit = explicitPct.Any(p => p >= 0);
            for (int i = 0; i < result.Count; i++)
            {
                double even = result.Count == 1 ? 0.0 : (double)i / (result.Count - 1);
                result[i].Offset = (!anyExplicit || explicitPct[i] < 0)
                    ? even
                    : Clamp01(explicitPct[i] / 100.0);
            }
            return result;
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
