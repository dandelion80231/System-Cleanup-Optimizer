using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CpqSystemTool
{
    /// <summary>
    /// 驱动存储管理模块（参考开源工具 Driver Store Explorer / RAPR 的核心能力）。
    /// 本质是对 Windows 自带 <c>pnputil.exe</c> 的封装：
    ///   - 枚举已安装驱动包（/enum-drivers）
    ///   - 通过 WMI（Win32_PnPSignedDriver）识别"在役"驱动，保护正在使用的设备
    ///   - 同系列（原始 INF 同名）下保留最新版，其余标记为"旧版冗余"
    ///   - 删除选中驱动包（/delete-driver，禁用正在使用的驱动）
    ///   - 导出备份选中驱动包（/export-driver）
    /// 程序已以管理员身份运行（app.manifest requireAdministrator），删除无需额外提权。
    /// </summary>
    internal static class DriverStore
    {
        /// <summary>单条驱动包信息。</summary>
        internal class DriverInfo
        {
            public string OemName = "";       // 发布名称，如 oem12.inf（删除/导出用它）
            public string OriginalName = "";  // 原始 INF 名，如 nv_dispig.inf
            public string Provider = "";      // 提供程序
            public string Class = "";         // 驱动类
            public string Version = "";       // 驱动版本（原始字符串，如 31.0.15.1234）
            public DateTime? Date;            // 驱动日期
            public string Signer = "";        // 数字签名者
            public string WhcpLevel = "";     // WHCP 级别（未知/等）
            public bool IsOld;                // 同系列下的旧版本（可清理）
            public bool InUse;                // 当前有设备在使用（受保护，禁止删除）
            public double SizeMB;             // 该系列驱动包估算占用（MB，近似值）
            public bool Selected { get; set; } // UI 勾选态（仅用于对话框选择；须为属性以支持 TwoWay 绑定）

            /// <summary>状态显示文本：在役/旧版可清/当前。</summary>
            public string StatusText
            {
                get
                {
                    if (InUse) return "在役·保护";
                    if (IsOld) return "旧版可清";
                    return "当前";
                }
            }

            /// <summary>日期显示文本。</summary>
            public string DateText => Date.HasValue ? Date.Value.ToString("yyyy-MM-dd") : "";

            /// <summary>占用显示文本（近似值）。</summary>
            public string SizeText => SizeMB > 0 ? "约 " + FormatSize(SizeMB) : "—";
        }

        // 字段标签：同时兼容中文 / 英文（部分系统 Beta UTF-8 下仍可能输出英文）。
        // 关键：解析不依赖"按行"，而是全局定位标签 + 截断到下一个标签，
        // 以容错 pnputil 在 Beta UTF-8 下「WHCP 版本: 未知发布名称: oemX.inf」挤在同一行的现象。
        private static readonly Dictionary<string, string[]> LabelPatterns = new Dictionary<string, string[]>
        {
            { "PublishedName", new[] { "发布名称", "Published Name" } },
            { "OriginalName",  new[] { "原始名称", "Original Name" } },
            { "Provider",      new[] { "提供程序名称", "Provider Name" } },
            { "Class",         new[] { "类", "Class" } },
            { "DriverDate",    new[] { "驱动程序日期", "Driver Date" } },
            { "DriverVersion", new[] { "驱动程序版本", "Driver Version" } },
            { "Signer",        new[] { "数字签名者", "Signer" } },
            { "ClassGuid",     new[] { "类 GUID", "Class GUID" } },
            { "WhcpLevel",     new[] { "WHCP 版本", "WHCP Level" } },
            { "Locale",        new[] { "区域设置", "Locale" } },
        };

        /// <summary>
        /// 枚举全部第三方驱动包（pnputil /enum-drivers），容错解析后返回列表。
        /// 解析失败或 pnputil 无输出时返回空列表并通过 log 提示。
        /// </summary>
        internal static List<DriverInfo> Enumerate(Action<string> log)
        {
            var outp = Exec.RunCmdGet(new[] { "pnputil", "/enum-drivers" }, log);
            if (string.IsNullOrWhiteSpace(outp))
            {
                log?.Invoke("[!] 未获取到 pnputil 输出（可能无第三方驱动，或 pnputil 不可用）。");
                return new List<DriverInfo>();
            }

            var list = ParseEnumOutput(outp, log);
            log?.Invoke($"[✓] 枚举完成：共 {list.Count} 个驱动包。");
            return list;
        }

        /// <summary>把 pnputil /enum-drivers 的文本解析成 DriverInfo 列表（标签定位法，容错挤行）。</summary>
        private static List<DriverInfo> ParseEnumOutput(string text, Action<string> log)
        {
            var markers = new List<(int pos, string key, int valStart)>();
            foreach (var kv in LabelPatterns)
            {
                foreach (var pat in kv.Value)
                {
                    // 标签后必须跟半角/全角冒号（可选空白），避免误匹配正文中的字样
                    var re = new Regex(Regex.Escape(pat) + @"\s*[:：]\s*", RegexOptions.CultureInvariant);
                    foreach (Match m in re.Matches(text))
                        markers.Add((m.Index, kv.Key, m.Index + m.Length));
                }
            }
            if (markers.Count == 0)
            {
                log?.Invoke("[!] 未能从 pnputil 输出中识别任何驱动字段，请检查系统语言或 pnputil 版本。");
                return new List<DriverInfo>();
            }

            markers.Sort((a, b) => a.pos.CompareTo(b.pos));

            var list = new List<DriverInfo>();
            DriverInfo cur = null;
            for (int i = 0; i < markers.Count; i++)
            {
                var (pos, key, valStart) = markers[i];
                int valEnd = (i + 1 < markers.Count) ? markers[i + 1].pos : text.Length;
                var value = text.Substring(valStart, valEnd - valStart).Trim();

                if (key == "PublishedName")
                {
                    if (cur != null) list.Add(cur);
                    cur = new DriverInfo { OemName = CleanValue(value) };
                }
                else if (cur != null)
                {
                    switch (key)
                    {
                        case "OriginalName":  cur.OriginalName = CleanValue(value); break;
                        case "Provider":      cur.Provider = CleanValue(value); break;
                        case "Class":         cur.Class = CleanValue(value); break;
                        case "DriverDate":
                            cur.Date = ParseDate(CleanValue(value));
                            break;
                        case "DriverVersion": cur.Version = CleanValue(value); break;
                        case "Signer":        cur.Signer = CleanValue(value); break;
                        case "WhcpLevel":     cur.WhcpLevel = CleanValue(value); break;
                            // ClassGuid / Locale 暂未使用，保留解析容错以避免干扰
                    }
                }
            }
            if (cur != null) list.Add(cur);

            // 丢弃没有发布名称的脏记录
            list.RemoveAll(d => string.IsNullOrWhiteSpace(d.OemName));
            return list;
        }

        private static string CleanValue(string v)
        {
            if (v == null) return "";
            // 去掉可能混入的换行（挤行场景下值内不应有换行，但保险起见归一）
            return v.Replace("\r", "").Replace("\n", " ").Trim();
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            // 兼容 2023/10/17、2023-10-17、10/17/2023 等
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2)) return d2;
            return null;
        }

        /// <summary>
        /// 通过 WMI（Win32_PnPSignedDriver）取得当前"在役"设备的原始 INF 名集合。
        /// 注意：WMI 返回的是原始名（如 nv_dispig.inf），不是 oemX.inf，故需按原始名映射。
        /// </summary>
        internal static HashSet<string> GetActiveInfNames(Action<string> log)
        {
            var script = "(Get-CimInstance -ClassName Win32_PnPSignedDriver -ErrorAction SilentlyContinue).InfName | Where-Object { $_ }";
            var outp = Exec.RunPowerShellGet(script, log);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(outp)) return set;
            foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        /// <summary>
        /// 标记在役驱动：把在役设备对应的原始名系列中"最新版"标记为 InUse（受保护）。
        /// 若某系列在役，仅最新版不可删；其余旧版仍标记为可清理（与 RAPR 行为一致，安全）。
        /// </summary>
        internal static void DetectInUse(List<DriverInfo> list, Action<string> log)
        {
            var active = GetActiveInfNames(log);
            if (active.Count == 0)
            {
                log?.Invoke("[*] 未发现在役设备关联（或 WMI 查询为空），将按版本判断旧驱动。");
                return;
            }

            // 先标记同系列最新版
            MarkNewestPerFamily(list);

            int protectedCount = 0;
            foreach (var d in list)
            {
                if (active.Contains(d.OriginalName) && !d.IsOld)
                {
                    d.InUse = true;
                    protectedCount++;
                }
            }
            log?.Invoke($"[✓] 在役检测：{protectedCount} 个驱动包正被设备使用，已设为保护（不可删除）。");
        }

        /// <summary>
        /// 同原始名系列下，仅最新版标记为非旧（IsOld=false），其余标记 IsOld=true。
        /// 比较规则：先比版本号（按点分整数），再比日期；都无法判定时按发布名称排序兜底。
        /// </summary>
        internal static void MarkOldVersions(List<DriverInfo> list)
        {
            MarkNewestPerFamily(list);
        }

        private static void MarkNewestPerFamily(List<DriverInfo> list)
        {
            var groups = list.GroupBy(d => d.OriginalName, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                var items = g.ToList();
                if (items.Count <= 1) { items[0].IsOld = false; continue; }
                DriverInfo best = items[0];
                foreach (var it in items.Skip(1))
                {
                    if (IsNewer(it, best)) best = it;
                }
                foreach (var it in items) it.IsOld = !ReferenceEquals(it, best);
            }
        }

        /// <summary>判断 a 是否比 b 更新（版本号优先，其次日期）。</summary>
        private static bool IsNewer(DriverInfo a, DriverInfo b)
        {
            int vc = CompareVersion(a.Version, b.Version);
            if (vc != 0) return vc > 0;
            if (a.Date.HasValue && b.Date.HasValue) return a.Date.Value > b.Date.Value;
            if (a.Date.HasValue != b.Date.HasValue) return a.Date.HasValue; // 有日期者更新
            // 兜底：发布名称字符串比较（oem 序号越大通常越新）
            return string.Compare(a.OemName, b.OemName, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static int CompareVersion(string a, string b)
        {
            var aa = SplitVersion(a);
            var bb = SplitVersion(b);
            int n = Math.Max(aa.Count, bb.Count);
            for (int i = 0; i < n; i++)
            {
                int x = i < aa.Count ? aa[i] : 0;
                int y = i < bb.Count ? bb[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        private static List<int> SplitVersion(string v)
        {
            var r = new List<int>();
            if (string.IsNullOrWhiteSpace(v)) return r;
            foreach (var part in v.Split(new[] { '.', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) r.Add(n);
                else r.Add(0);
            }
            return r;
        }

        /// <summary>
        /// 估算每个驱动包（按原始名系列）在 DriverStore\FileRepository 中的占用。
        /// 注意：同系列多版本共享原始名前缀，此处返回的是"该系列合计占用"（近似值），
        /// 并非单版本精确值——精确值需 SetupAPI，超出本工具范围；UI 中以"约"标注。
        /// </summary>
        internal static void EstimateSizes(List<DriverInfo> list, Action<string> log)
        {
            string repo = null;
            try
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                repo = Path.Combine(win, "System32", "DriverStore", "FileRepository");
            }
            catch { }

            if (string.IsNullOrEmpty(repo) || !Directory.Exists(repo))
            {
                log?.Invoke("[*] 未找到驱动存储目录，跳过体积估算。");
                return;
            }

            // 一次扫描，按原始名前缀（foo.inf）累加各版本文件夹大小
            var prefixSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var dir in Directory.GetDirectories(repo))
                {
                    var name = Path.GetFileName(dir);
                    int idx = name.IndexOf('_');
                    if (idx <= 0) continue;
                    var prefix = name.Substring(0, idx); // 形如 "foo.inf"
                    try { prefixSizes[prefix] = prefixSizes.TryGetValue(prefix, out var s) ? s + DirSize(dir) : DirSize(dir); }
                    catch { }
                }
            }
            catch (Exception ex) { log?.Invoke("[!] 扫描驱动存储失败：" + ex.Message); }

            foreach (var d in list)
            {
                if (prefixSizes.TryGetValue(d.OriginalName, out var bytes))
                    d.SizeMB = bytes / (1024.0 * 1024.0);
            }
        }

        private static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        /// <summary>
        /// 删除一组驱动包。在役（InUse）驱动会被跳过并警告，不会删除。
        /// 返回是否全部成功（无任何失败/跳过）。
        /// </summary>
        internal static bool Delete(List<DriverInfo> items, Action<string> log)
        {
            if (items == null || items.Count == 0) { log?.Invoke("[!] 没有可删除的驱动。"); return false; }

            int ok = 0, skipped = 0, fail = 0;
            foreach (var d in items)
            {
                if (d.InUse)
                {
                    log?.Invoke($"[!] 跳过（在役/受保护）：{d.OemName}（{d.OriginalName}）— 正在被设备使用，删除可能导致设备失效。");
                    skipped++;
                    continue;
                }
                log?.Invoke($"[*] 删除 {d.OemName}（{d.OriginalName} {d.Version}）…");
                // 默认不带 /force：若仍被引用，pnputil 会自行报错，作为最后一道防线。
                int code = Exec.RunCmd(new[] { "pnputil", "/delete-driver", d.OemName }, log);
                if (code == 0) { log?.Invoke($"    [✓] 已删除 {d.OemName}。"); ok++; }
                else { log?.Invoke($"    [!] 删除失败（退出码 {code}）：{d.OemName}。"); fail++; }
            }

            log?.Invoke($"[✓] 删除结束：成功 {ok}，跳过（受保护）{skipped}，失败 {fail}。");
            return fail == 0 && skipped == 0;
        }

        /// <summary>
        /// 导出备份一组驱动包到目标目录（pnputil /export-driver）。
        /// 逐个导出以便逐条报告结果；目标目录不存在时尝试创建。
        /// </summary>
        internal static void Export(List<DriverInfo> items, string dir, Action<string> log)
        {
            if (items == null || items.Count == 0) { log?.Invoke("[!] 没有可导出的驱动。"); return; }
            if (string.IsNullOrWhiteSpace(dir)) { log?.Invoke("[!] 未指定导出目录。"); return; }

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { log?.Invoke("[!] 无法创建导出目录：" + ex.Message); return; }

            int ok = 0, fail = 0;
            foreach (var d in items)
            {
                log?.Invoke($"[*] 导出 {d.OemName}（{d.OriginalName}） → {dir}");
                int code = Exec.RunCmd(new[] { "pnputil", "/export-driver", d.OemName, dir }, log);
                if (code == 0) { log?.Invoke($"    [✓] 已导出 {d.OemName}。"); ok++; }
                else { log?.Invoke($"    [!] 导出失败（退出码 {code}）：{d.OemName}。"); fail++; }
            }
            log?.Invoke($"[✓] 导出结束：成功 {ok}，失败 {fail}。备份位于：{dir}");
        }

        /// <summary>格式化占用大小为友好字符串。</summary>
        internal static string FormatSize(double mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("F2") + " GB";
            if (mb >= 1) return mb.ToString("F0") + " MB";
            return (mb * 1024.0).ToString("F0") + " KB";
        }
    }
}
