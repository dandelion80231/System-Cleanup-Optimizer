// Office 部署工具（ODT）配置构建与获取 —— 由 office-config-prototype / office-ui-prototype 移植。
// 思路来源：Office Tool Plus 公开历史版本的配置构建方式（仅学思路，代码自研）。
// 本模块提供组件目录（10 个组件）、安装参数模型、config.xml 生成与校验，以及 ODT
// setup.exe 的下载中心解析下载（复用 cpq Downloader / Exec，与 OfficeInstall.cs 同一执行风格）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 一个可选组件的描述：含它在套件内的排除 ID，以及它作为独立单品时的 Product ID。
    /// </summary>
    public sealed class OfficeComponent
    {
        /// <summary>中文显示名</summary>
        public string Name { get; }

        /// <summary>套件内排除 ID（对应 &lt;ExcludeApp ID="..."/&gt;）；无则为 null</summary>
        public string ExcludeAppId { get; }

        /// <summary>独立单品 Product ID（批量/Volume 版，路线 B 用）；无则为 null</summary>
        public string StandaloneProductId { get; }

        /// <summary>独立单品 Product ID 的零售版（Retail），用于订阅制套件（Channel=Current 等）同装场景；
        /// 订阅通道不能装 Volume 版，须改用 Retail 版。无则为 null。</summary>
        public string StandaloneProductIdRetail { get; }

        /// <summary>附加套件内排除 ID：与主 ExcludeAppId 同步排除/保留（用于 Outlook 经典版+新版这种"一个 UI 项对应多个 ODT ID"的场景）</summary>
        public string[] ExtraExcludeAppIds { get; }

        public OfficeComponent(string name, string excludeAppId, string standaloneProductId, string standaloneProductIdRetail = null, string[] extraExcludeAppIds = null)
        {
            Name = name;
            ExcludeAppId = excludeAppId;
            StandaloneProductId = standaloneProductId;
            StandaloneProductIdRetail = standaloneProductIdRetail;
            ExtraExcludeAppIds = extraExcludeAppIds;
        }
    }

    /// <summary>
    /// 组件目录：10 个可选组件及其 ODT 对应关系（用户确认收敛为 10 组件）。
    /// 注意：OneDrive 官方文档要求对应 Groove（与显示名不同，无独立单品）。
    /// Visio / Project 是“独立 Product”，不是套件内的 ExcludeApp。
    /// </summary>
    public static class ComponentCatalog
    {
        // 套件内应用（可用 ExcludeApp 排除，也可作为单品 Product 安装）
        public static readonly OfficeComponent Word = new OfficeComponent("Word", "Word", "Word2024Volume");
        public static readonly OfficeComponent Excel = new OfficeComponent("Excel", "Excel", "Excel2024Volume");
        public static readonly OfficeComponent PowerPoint = new OfficeComponent("PowerPoint", "PowerPoint", "PowerPoint2024Volume");
        // Outlook 复选框代表经典版 C2R 套件应用（ODT 内 ExcludeApp=Outlook）；新版 OutlookForWindows
        // 不在 ODT 组件体系内、不进勾选/排除，由用户在新版 Outlook 应用内一键切换获取，
        // 强力卸载弹窗内单独提供可移除项。
        public static readonly OfficeComponent Outlook = new OfficeComponent("Outlook", "Outlook", null);
        public static readonly OfficeComponent OneNote = new OfficeComponent("OneNote", "OneNote", "OneNote2021Volume"); // 单品仅 2021 版
        public static readonly OfficeComponent Access = new OfficeComponent("Access", "Access", "Access2024Volume");
        public static readonly OfficeComponent Publisher = new OfficeComponent("Publisher", "Publisher", "Publisher2021Volume"); // 单品仅 2021 版
        // 套件内、但 ID 与显示名不同
        public static readonly OfficeComponent OneDrive = new OfficeComponent("OneDrive", "Groove", null, extraExcludeAppIds: new[] { "OneDrive" }); // 官方用 Groove，同时排除 OneDrive
        // 独立产品（不能作为 ExcludeApp，只能作为独立 Product 出现）
        // 独立产品：Volume 版用于 LTSC 批量套件（PerpetualVLxxxx）；订阅制套件（Channel=Current 等）
        // 不能装 Volume 版，须改用 Retail 版（StandaloneProductIdRetail），由 BuildArgs 按通道自动切换。
        public static readonly OfficeComponent Visio = new OfficeComponent("Visio", null, "VisioPro2024Volume", "VisioPro2024Retail");
        public static readonly OfficeComponent Project = new OfficeComponent("Project", null, "ProjectPro2024Volume", "ProjectPro2024Retail");

        /// <summary>全部“套件内可排除”组件（即 ExcludeAppId 非 null 的组件）</summary>
        public static IEnumerable<OfficeComponent> SuiteApps
        {
            get
            {
                return new[]
                {
                    Word, Excel, PowerPoint, Outlook, OneNote,
                    Access, Publisher, OneDrive
                };
            }
        }
    }

    /// <summary>
    /// 单个 Product 的配置（对应 XML 中的 &lt;Product&gt;）。
    /// </summary>
    public sealed class OfficeProductConfig
    {
        public string ProductId { get; set; }
        public List<string> Languages { get; }
        public List<string> ExcludeApps { get; }
        public string PidKey { get; set; }

        public OfficeProductConfig()
        {
            ProductId = string.Empty;
            Languages = new List<string>();
            ExcludeApps = new List<string>();
        }

        public OfficeProductConfig(string productId) : this()
        {
            ProductId = productId;
        }
    }

    /// <summary>
    /// 完整安装 / 修改参数（对应 XML 中的 &lt;Configuration&gt; 及各子节点）。
    /// </summary>
    public sealed class OfficeInstallArguments
    {
        /// <summary>OfficeClientEdition：64 或 32</summary>
        public string Architecture { get; set; }

        public string Channel { get; set; }
        public string SourcePath { get; set; }
        public string Version { get; set; }
        public string DownloadPath { get; set; }

        /// <summary>Display Level：false=None（静默），true=Full（有界面）</summary>
        public bool DisplayFull { get; set; }

        /// <summary>是否接受许可协议（为 true 时输出 AcceptEULA="TRUE"）</summary>
        public bool AcceptEula { get; set; }

        public string LoggingPath { get; set; }

        /// <summary>自动更新开关；null=不输出 &lt;Updates&gt;，true 输出 Enabled="TRUE"，false 输出 Enabled="FALSE"</summary>
        public bool? UpdateEnabled { get; set; }

        /// <summary>更新通道；非 null 且非空时，输出到 &lt;Updates Channel="..."&gt;。
        /// 用于「不重装、原地切换已装 Office 的更新通道」场景（订阅版 M365 原地升/降版）：
        /// 此包可不带 &lt;Add&gt;（Products 为空），仅靠 &lt;Updates Channel&gt; 让 C2R 更新器把本机
        /// 对齐到目标通道的当前构建。见 OfficeDeployControl 的「切换更新通道」。</summary>
        public string UpdateChannel { get; set; }

        /// <summary>是否强制关闭阻塞安装的 Office 相关进程（对应 ODT &lt;Property Name="FORCEAPPSHUTDOWN" Value="TRUE"&gt;）。
        /// 默认开启：避免 ODT 退出码 17006（ERROR_SCENARIO_CANCELLED = Blocked update by running apps）。</summary>
        public bool ForceAppShutdown { get; set; }

        /// <summary>是否启用"通道-许可类型"合法性校验；默认开启。当某 ProductId 含 "Volume" 却使用订阅制通道时报错。</summary>
        public bool ValidateChannel { get; set; }

        public List<OfficeProductConfig> Products { get; }

        /// <summary>需要显式移除的独立产品 PID（对应 ODT &lt;Remove&gt;&lt;Product ID="..."/&gt;）。
        /// 用于「独立产品（Visio/Project）取消勾选 = 移除」场景：ODT 不会因为某产品不出现在 &lt;Add&gt; 里就删除它，
        /// 必须显式发 &lt;Remove ProductID&gt;。仅放本机确实已装的 PID（由 BuildArgs 按台账填充）。</summary>
        public List<string> RemoveProductIds { get; }

        public OfficeInstallArguments()
        {
            Architecture = "64";
            AcceptEula = true;
            ValidateChannel = true;
            ForceAppShutdown = true;  // 默认强制关闭阻塞进程，避免 ODT 17006
            Products = new List<OfficeProductConfig>();
            RemoveProductIds = new List<string>();
        }
    }
}

namespace CpqSystemTool
{
    /// <summary>
    /// 配置 XML 构建器。
    /// 思路：先用 XElement 搭出“带全部属性”的临时节点，再用 LINQ 过滤掉空属性，
    /// 最后用 XmlWriter 输出（UTF-8 带 BOM、缩进、无声明）。
    /// </summary>
    public static class ConfigXmlBuilder
    {
        public static string Build(OfficeInstallArguments args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            // 通道-许可类型合法性校验（可被 ValidateChannel 开关关闭，默认开启）
            if (args.ValidateChannel)
            {
                ValidateChannelCompatibility(args);
            }

            // 构建 <Add> 及其下的所有 <Product>
            var addChildren = new List<object>();
            foreach (var p in args.Products)
            {
                addChildren.Add(BuildProduct(p));
            }

            var addElement = BuildElement("Add", new (string Attr, string Value)[]
            {
                ("OfficeClientEdition", args.Architecture ?? string.Empty),
                ("Channel", args.Channel ?? string.Empty),
                ("SourcePath", args.SourcePath ?? string.Empty),
                ("Version", args.Version ?? string.Empty),
                ("DownloadPath", args.DownloadPath ?? string.Empty),
            }, addChildren.ToArray());

            var configChildren = new List<object> { addElement };

            // 独立产品「取消勾选 = 移除」：显式 <Remove><Product ID="..."/></Remove> 节点。
            // ODT 不会因为某产品「没出现在 <Add> 里」就删除它（与套件内 ExcludeApp 语义不同），
            // 必须显式发 <Remove ProductID> 才能真的卸载已装但未勾选的独立产品（Visio/Project）。
            if (args.RemoveProductIds != null && args.RemoveProductIds.Count > 0)
            {
                var removeProducts = args.RemoveProductIds
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => BuildElement("Product", new (string Attr, string Value)[] { ("ID", id) }))
                    .ToArray();
                if (removeProducts.Length > 0)
                    // All="false" 显式声明「只移除下面列出的独立产品（保留套件）」，与 OTP 导出的 <Remove All="false"> 一致。
                    configChildren.Add(BuildElement("Remove", new (string Attr, string Value)[] { ("All", "false") }, removeProducts));
            }

            // 更新开关 / 更新通道：仅当显式设置了 UpdateEnabled 或 UpdateChannel 时输出 <Updates>。
            // UpdateChannel 支持「纯改通道」：包可不含 <Add>（Products 为空），仅靠 <Updates Channel> 原地切通道。
            var updatesAttrs = new List<(string Attr, string Value)>();
            if (args.UpdateEnabled.HasValue)
                updatesAttrs.Add(("Enabled", args.UpdateEnabled.Value ? "TRUE" : "FALSE"));
            if (!string.IsNullOrEmpty(args.UpdateChannel))
                updatesAttrs.Add(("Channel", args.UpdateChannel));
            if (updatesAttrs.Count > 0)
                configChildren.Add(BuildElement("Updates", updatesAttrs.ToArray()));

            // 说明：纯「改通道」包 = 有 <Updates>（Enabled 或 Channel 任一）、无 <Add> Product、无 <Remove>。
            // <Add> 元素恒输出（可含 0 个 Product，ODT 容忍）；仅 <Updates> 按需在上方加入。
            // 原有「<Add> 至少一个 product」守卫已移除——纯改通道场景下 Products 必然为空。

            // 显示：Level 始终输出；AcceptEULA 仅在为 true 时输出（bool -> XML 规则）
            configChildren.Add(BuildElement("Display", new (string Attr, string Value)[]
            {
                ("Level", args.DisplayFull ? "Full" : "None"),
                ("AcceptEULA", args.AcceptEula ? "TRUE" : string.Empty),
            }));

            if (!string.IsNullOrEmpty(args.LoggingPath))
            {
                configChildren.Add(BuildElement("Logging", new (string Attr, string Value)[]
                {
                    ("Level", "Standard"),
                    ("Path", args.LoggingPath),
                }));
            }

            // FORCEAPPSHUTDOWN：默认开启，强制关闭阻塞安装的 Office 进程，避免 ODT 17006（ERROR_SCENARIO_CANCELLED）
            configChildren.Add(BuildElement("Property", new (string Attr, string Value)[]
            {
                ("Name", "FORCEAPPSHUTDOWN"),
                ("Value", args.ForceAppShutdown ? "TRUE" : "FALSE"),
            }));

            var config = new XElement("Configuration", configChildren.ToArray());

            // 用 XmlWriter 输出：UTF-8 带 BOM、缩进、省略声明、CRLF 换行
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(true),
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
            };

            using (var ms = new MemoryStream())
            using (var writer = XmlWriter.Create(ms, settings))
            {
                config.WriteTo(writer);
                // 关键修复：XmlWriter 有内部缓冲，不 Flush 就取 ms.ToArray() 只会拿到 BOM 头，
                // Build 返回空串 → ODT 读空 config.xml 静默无事可做（安装/卸载/导出全部空转、退出码 0）。
                writer.Flush();
                // 转为字符串供 UI 显示：去掉 BOM 标记，避免界面乱码。写文件时用 WriteConfigXml。
                return new UTF8Encoding(false).GetString(ms.ToArray()).TrimStart('\uFEFF');
            }
        }

        /// <summary>将 config.xml 以 UTF-8 带 BOM 写入指定路径（ODT 要求带 BOM）。</summary>
        public static void WriteConfigXml(string path, OfficeInstallArguments args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            string xml = Build(args);
            File.WriteAllText(path, xml, new UTF8Encoding(true));
        }

        // 订阅制通道：仅适用于 Microsoft 365 订阅/零售产品（如 O365ProPlusRetail）；
        // 批量许可（Volume）产品不可用，应使用 PerpetualVL 系列通道。
        private static readonly HashSet<string> SubscriptionChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Current", "CurrentPreview", "SemiAnnual", "SemiAnnualPreview", "MonthlyEnterprise", "BetaChannel",
        };

        /// <summary>
        /// 判断给定通道是否为“订阅制”通道（Current / SemiAnnual / MonthlyEnterprise / Beta 等预览通道）。
        /// 订阅制通道只能承载 Microsoft 365 订阅/零售产品；批量许可（Volume）产品须改用 PerpetualVL 系列通道。
        /// 供 BuildArgs 等外部逻辑按套件通道选择 Visio/Project 的 Retail/Volume 版 ProductId。
        /// </summary>
        public static bool IsSubscriptionChannel(string channel)
        {
            return !string.IsNullOrEmpty(channel) && SubscriptionChannels.Contains(channel);
        }

        /// <summary>
        /// 校验“通道-许可类型”是否匹配：批量许可（ProductId 含 "Volume"）产品不能使用订阅制通道。
        /// 命中冲突时抛出中文 InvalidOperationException，便于接入 UI 时尽早暴露错误。
        /// </summary>
        private static void ValidateChannelCompatibility(OfficeInstallArguments args)
        {
            if (string.IsNullOrEmpty(args.Channel) || !SubscriptionChannels.Contains(args.Channel))
            {
                return;
            }

            foreach (var p in args.Products)
            {
                if (!string.IsNullOrEmpty(p.ProductId) && p.ProductId.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        "通道冲突：产品“" + p.ProductId + "”属于批量许可（Volume）产品，不能使用订阅制通道“" + args.Channel + "”。"
                        + "批量许可产品应使用 PerpetualVL 系列通道（例如 PerpetualVL2024）。");
                }
            }
        }

        private static XElement BuildProduct(OfficeProductConfig product)
        {
            var children = new List<object>();
            foreach (var lang in product.Languages)
            {
                children.Add(BuildElement("Language", new (string Attr, string Value)[] { ("ID", lang) }));
            }

            foreach (var ex in product.ExcludeApps)
            {
                children.Add(BuildElement("ExcludeApp", new (string Attr, string Value)[] { ("ID", ex) }));
            }

            // MAK 密钥：仅当密钥长度 == 29 且 ProductId 含 "Volume" 时，才输出 PIDKEY 属性
            string pidKeyAttr = string.Empty;
            if (!string.IsNullOrEmpty(product.PidKey) &&
                product.PidKey.Length == 29 &&
                !string.IsNullOrEmpty(product.ProductId) &&
                product.ProductId.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pidKeyAttr = product.PidKey;
            }

            return BuildElement("Product", new (string Attr, string Value)[]
            {
                ("ID", product.ProductId),
                ("PIDKEY", pidKeyAttr),
            }, children.ToArray());
        }

        /// <summary>
        /// 构建 XElement 并“过滤空属性”：先建一个带全部属性的临时节点，
        /// 再用 LINQ 过滤掉空属性，从而不输出未设置的空属性。
        /// </summary>
        private static XElement BuildElement(string name, (string Attr, string Value)[] attributes, params object[] children)
        {
            var temp = new XElement(name,
                attributes.Select(a => new XAttribute(a.Attr, a.Value ?? string.Empty)));
            var kept = from el in temp.Attributes()
                       where (string)el != string.Empty
                       select el;
            return new XElement(name, kept, children);
        }
    }

    /// <summary>
    /// 获取微软官方 Office 部署工具（ODT）setup.exe。
    /// 思路来源：Mocreak 内嵌 odt-*.zip、Office Tool Plus 用官方 ODT —— 改用运行时下载最新版。
    /// 解析微软下载中心页面中的 officedeploymenttool_*.exe 直链（无额外依赖），
    /// 实际下载复用 cpq 统一 Downloader（重试/代理回退/进度回调），执行复用 Exec.RunCmd。
    /// </summary>
    internal static class OdtSetup
    {
        // ponytail: 用 en-us 而非 zh-cn —— zh-cn 下载中心已 404（2026-09-10 实测），en-us 仍可解析 ODT 直链。
        private const string DetailsPage = "https://www.microsoft.com/en-us/download/details.aspx?id=49117";
        private const string ConfirmPage = "https://www.microsoft.com/en-us/download/confirmation.aspx?id=49117";

        /// <summary>数据目录：exe 同目录 cpq-tool（统一数据根，见 AppPaths.DataRoot；引擎 + 配置文件 + 运行日志集中存放，原 cpq-tool 已迁入）。</summary>
        public static string GetDataDir() => AppPaths.DataRoot;

        /// <summary>ODT 引擎子目录：cpq-tool/odt/</summary>
        public static string GetOdtDir() => AppPaths.OdtEngineDir;

        /// <summary>配置文件子目录：cpq-tool/configs/</summary>
        public static string GetConfigsDir() => AppPaths.OdtConfigsDir;

        /// <summary>
        /// 获取 setup.exe：优先用本地已落地的 ODT 引擎（exe 同目录 cpq-tool/odt/setup.exe），
        /// 缺失则解析下载中心拿到微软官方自解压外壳（officedeploymenttool_*.exe）并经 Downloader 下载，
        /// 再用 ExtractOdt() 解出内层真正的 setup.exe 落地到 cpq-tool/odt/。
        /// log 输出进度文本；progress 回显下载百分比（0–100）。
        /// 返回 setup.exe 路径；失败返回 null（提示手动放置，无外部工具兜底——程序完全自包含）。
        /// </summary>
        public static string Ensure(Action<string> log, Action<int> progress = null)
        {
            string localOdt = Path.Combine(GetOdtDir(), "setup.exe");

            // 1. 本地已有 ODT 引擎（exe 同目录 cpq-tool/odt/setup.exe），直接复用，不下载
            try { Directory.CreateDirectory(GetOdtDir()); }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 无法创建 ODT 目录: " + ex.Message); }
            if (File.Exists(localOdt) && new FileInfo(localOdt).Length > 5_000_000)
            {
                log("  [*] 已存在本地 ODT 引擎: " + localOdt);
                return localOdt;
            }

            // 2. 解析微软下载中心拿到 ODT 自解压外壳直链
            string url = FindExeUrl(log);
            if (url == null)
            {
                log("  [!] 无法从微软下载中心解析 ODT 下载链接（页面结构可能已变更）。请手动下载并放到：");
                log("      " + localOdt);
                return null;
            }

            log("下载 Office 部署工具 (ODT) 官方外壳: " + url);
            string wrapper = Path.Combine(GetOdtDir(), "_tmp_wrapper.exe");
            // 复用 cpq 统一下载器：请求级超时 + 失败重试 + 进度回调；先写临时文件，避免半截缓存被误判已就绪
            bool ok = Downloader.DownloadAsync(url, wrapper, log,
                progress: progress,
                maxAttempts: 2,
                timeoutMs: 120000,
                readTimeoutMs: 120000,
                userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)").GetAwaiter().GetResult();
            if (!ok)
            {
                log("  [!] 下载 ODT 外壳失败。可重试，或手动下载 ODT 后放到：");
                log("      " + localOdt);
                return null;
            }

            // 3. 外壳解出内层真正的 setup.exe（~7.2MB），落地到 cpq-tool/odt/setup.exe
            string extracted = ExtractOdt(wrapper, GetOdtDir(), log);
            try { if (File.Exists(wrapper)) File.Delete(wrapper); } catch { }

            if (extracted != null && File.Exists(extracted) && new FileInfo(extracted).Length > 5_000_000)
            {
                if (progress != null) progress(100);
                return extracted;
            }

            // 4. 解压失败（残留的半截 setup.exe 因 <5MB 不会被下次误用，重试下载即可；也可手动放置）
            log("  [!] 自解压外壳解压失败。请重试，或手动下载 setup.exe 放到：");
            log("      " + localOdt);
            return null;
        }

        /// <summary>从微软下载的自解压外壳（officedeploymenttool_*.exe）中解出内层 setup.exe。
        /// 外壳内嵌 CAB 从偏移 ~265216 起（可扫描 MSCF 签名动态定位），用系统 expand.exe 解压。
        /// 返回解出的 setup.exe 路径；失败返回 null。</summary>
        private static string ExtractOdt(string wrapperExe, string targetDir, Action<string> log)
        {
            try
            {
                // 1. 扫描 MSCF 偏移（CAB 签名）
                byte[] data = File.ReadAllBytes(wrapperExe);
                int cabOff = -1;
                for (int i = 0; i < data.Length - 4; i++)
                {
                    if (data[i] == 0x4D && data[i + 1] == 0x53 && data[i + 2] == 0x43 && data[i + 3] == 0x46)
                    { cabOff = i; break; }
                }
                if (cabOff < 0) { log("  [!] 未找到内嵌 CAB，外壳格式可能已变更"); return null; }

                // 2. 截取 CAB 并写出
                byte[] cab = new byte[data.Length - cabOff];
                Buffer.BlockCopy(data, cabOff, cab, 0, cab.Length);
                string cabPath = Path.Combine(targetDir, "_tmp_inner.cab");
                File.WriteAllBytes(cabPath, cab);

                // 3. 用系统 expand.exe 解压（Environment.SystemDirectory 跟随实际系统盘，
                //    而非写死 C:\——系统盘非 C: 的机器（企业常见）也能找到）
                string expandExe = Path.Combine(Environment.SystemDirectory, "expand.exe");
                string outDir = Path.Combine(targetDir, "_tmp_extract");
                Directory.CreateDirectory(outDir);
                var psi = new ProcessStartInfo(expandExe, $"\"{cabPath}\" -F:* \"{outDir}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var proc = Process.Start(psi);
                string so = proc.StandardOutput.ReadToEnd();
                string se = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);
                if (proc.ExitCode != 0) { log("  [!] expand.exe 失败: " + se); return null; }

                // 4. 找解出的 setup.exe
                string extracted = Path.Combine(outDir, "setup.exe");
                if (!File.Exists(extracted) || new FileInfo(extracted).Length < 5_000_000)
                { log("  [!] 解压后未找到有效 setup.exe（>=5MB）"); return null; }

                // 5. 移到目标位置
                string finalPath = Path.Combine(targetDir, "setup.exe");
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(extracted, finalPath);

                // 6. 清理临时文件
                try { File.Delete(cabPath); Directory.Delete(outDir, true); } catch { }

                log("  [OK] 已从微软官方外壳解出 ODT 引擎: " + finalPath);
                return finalPath;
            }
            catch (Exception ex)
            {
                log("  [!] 解压 ODT 外壳失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 写 config.xml（UTF-8 带 BOM）到 cpq-tool/configs/（带时间戳文件名，便于事后排查）并以 ODT 执行 /configure。
        /// 运行用的临时目录改到 cpq-tool/run/，跑完延迟清理避免文件锁。返回退出码（0 通常成功）。
        /// 本程序以管理员运行，子进程继承权限，无需额外提权。
        /// </summary>
        public static int RunConfig(string setupExe, string configXml, Action<string> log)
        {
            string runDir;
            try
            {
                string baseRun = AppPaths.OdtRunDir;
                Directory.CreateDirectory(baseRun);
                runDir = Path.Combine(baseRun, "odt_run_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(runDir);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 无法创建 ODT 运行目录: " + ex.Message); return -1; }

            // 配置文件集中落到 cpq-tool/configs/，带时间戳文件名（便于事后排查）
            string configDir = GetConfigsDir();
            Directory.CreateDirectory(configDir);
            string configPath = Path.Combine(configDir, "config_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
            try { File.WriteAllText(configPath, configXml, new UTF8Encoding(true)); }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 写入 config.xml 失败: " + ex.Message); return -1; }
            // configs 目录总量控制：部署 XML 只保留最新 20 份，多余的自动清理（注册表快照已另存 安全防护\regbackup，不受影响）
            AppPaths.PruneDeployConfigs();

            try
            {
                // 注意：微软 ODT setup.exe 没有 /quiet 这个顶层开关（仅支持 /download /configure /customize /help），
                // 静默完全由 config 的 <Display Level="None"> 控制。若误加 /quiet，引擎会把它当作未知参数，
                // 直接打印 usage 帮助文本并退出、什么也不做（表现为「假成功」）。所以这里只传 /configure <config>。

                // 假成功检测：微软 setup.exe 在「配置无效」时（config.xml 不存在 / 路径含空格被拆碎 /
                // 参数错误）仍会返回退出码 0，但会打印 usage 帮助文本而真正什么都没做。单看退出码永远
                // 分不清真假成功，所以这里 capture:true 捕获 stdout，并自定义 log 委托：既把每行转发给
                // 原 log（保持原有日志行为），又把输出累积到 sb 用于 usage 特征检测。
                var sb = new StringBuilder();
                Action<string> logWithCapture = line =>
                {
                    log(line);
                    sb.Append(line).Append('\n');
                };
                // workingDirectory=runDir：固定 ODT 子进程 CWD 到 cpq-tool/run/odt_run_<guid>。
                // ODT 引擎未设 SourcePath/DownloadPath/LoggingPath 时会按 CWD 建工作文件夹，
                // 若 CWD 继承自父进程（双击 exe=桌面）会在桌面留下无名文件夹。固定到 runDir
                // 后，ODT 的 CWD 相对操作全部落在 runDir，跑完 ScheduleRunCleanup 整体清掉，不污染桌面。
                // 【修 P2-9】ODT /configure 现包含 C2R 下载（v5 起 /download 合并），慢网下 15 分钟默认超时不够，
                // 显式传 60 分钟；KillIfTimeout 到时才杀树，正常跑完远早于此值。
                int rc = Exec.RunCmd(new[] { setupExe, "/configure", configPath }, logWithCapture, capture: true, workingDirectory: runDir, timeoutMs: 3600000);

                // 检测 ODT 是否打印了 usage 帮助（大小写不敏感，命中任意一个特征串即判定为"未执行"）。
                string odtOut = sb.ToString();
                bool printedUsage =
                    odtOut.IndexOf("Displays this message", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    odtOut.IndexOf("Setup /configure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    odtOut.IndexOf("Office Deployment Tool", StringComparison.OrdinalIgnoreCase) >= 0;
                if (printedUsage)
                {
                    // 明确告知：ODT 根本没执行，不能当作成功。返回 -2 让调用方区分于普通失败码。
                    log("  [!] ODT 打印了 usage 帮助文本 —— 它没收到有效配置，未对 Office 做任何修改（假成功）");
                    log("      常见原因：config.xml 不存在 / 路径含空格被拆碎 / 参数错误。请检查上方日志与生成的配置。");
                    ScheduleRunCleanup(runDir);
                    return -2;
                }

                log((rc == 0 ? "  [完成] ODT 退出码 0" : "  [!] ODT 退出码 " + rc) + "，请查看上方输出确认结果");
                ScheduleRunCleanup(runDir);
                return rc;
            }
            catch (Exception ex)
            {
                log("  [!] 执行 ODT 失败: " + ex.Message);
                ScheduleRunCleanup(runDir);
                return -1;
            }
        }

        /// <summary>
        /// 统计 Office C2R 落盘字节总量（64/32 位安装根目录 + C:\Windows\Temp\OfficeC2R* 下载临时目录）。
        ///
        /// 背景：ODT 引擎没有下载百分比查询接口（setup.exe /? 仅 /download /configure /customize /help，
        ///   旧版 /progress 开关已不存在）；实际下写由 C2R agent 服务完成。故执行前取基线、
        ///   执行中每 3s 扫描算差量，作为「真实写入磁盘的字节数」进度指标。
        ///   实测 4.7GB/1.1 万文件扫描约 340ms，3s 轮询可承受。
        /// 返回总字节数；-1 = 取得失败（轮询端应跳过本轮）。
        /// </summary>
        public static long OverlayStoreBytesSnapshot()
        {
            try
            {
                long total = 0;
                // Office 安装根目录：64/32 两个候选都统计，只扫描实际存在的；
                // junction 重定向场景下，从 Program Files 下的 junction 起点能读到目标盘实际数据
                total += DirBytesSafe(Path.Combine(OfficeInstallLocation.GetOfficeRootPath("64"), "root"));
                total += DirBytesSafe(Path.Combine(OfficeInstallLocation.GetOfficeRootPath("32"), "root"));
                // C2R agent 下载临时目录（部分版本先写 temp 再应用；旧残留文件在基线中，差量计算自然抵消）
                string winTemp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (Directory.Exists(winTemp))
                {
                    foreach (string d in Directory.EnumerateDirectories(winTemp, "OfficeC2R*"))
                        total += DirBytesSafe(d);
                }
                return total;
            }
            catch { return -1; }
        }

        /// <summary>递归统计目录字节总量；单文件读取失败（C2R 正在写入）跳过，不打断整体统计。</summary>
        private static long DirBytesSafe(string dir)
        {
            long sum = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; }
                    catch { /* 文件正在写入/锁定：本次跳过，下一轮再统计 */ }
                }
            }
            catch { /* 枚举失败：该目录记 0（基线与轮询同条件测量，对差量影响有限） */ }
            return sum;
        }

        /// <summary>
        /// 启动时清扫 cpq-tool/run/ 下过期的 odt_run_* 临时目录。
        /// 这些目录本应在每次 ODT 执行完毕后由 ScheduleRunCleanup 延迟删除；
        /// 但若程序在延迟窗口内退出（用户秒关窗口），那个 fire-and-forget 的 Task 会被线程池回收、
        /// 删除从不发生，于是 cpq-tool/run/ 下残留空壳目录、无限堆积 —— 这正是不合理的来源。
        /// 启动时 ODT 不可能在跑，所有 odt_run_* 都是过期残留，可安全整体删除。放在后台线程，不拖累启动。
        /// </summary>
        public static void CleanupStaleRunDirs()
        {
            try
            {
                string baseRun = AppPaths.OdtRunDir;
                if (!Directory.Exists(baseRun)) return;
                foreach (var d in Directory.EnumerateDirectories(baseRun, "odt_run_*"))
                    TryDeleteDirRecursive(d);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>带重试的递归删除目录（避开 ODT 偶发的瞬时文件锁），失败静默忽略、留给下次启动兜底。</summary>
        private static void TryDeleteDirRecursive(string dir)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                    return;
                }
                catch
                {
                    if (attempt == 2) return; // 三次都失败则放弃，留给下次启动 CleanupStaleRunDirs
                    try { System.Threading.Thread.Sleep(1000); } catch { }
                }
            }
        }

        /// <summary>延迟清理 ODT 运行临时目录（cpq-tool/run/odt_run_*），避开 ODT 进程未释放的文件锁。</summary>
        private static void ScheduleRunCleanup(string dir)
        {
            try
            {
                var t = new Task(() =>
                {
                    try { System.Threading.Thread.Sleep(3000); } catch { }
                    TryDeleteDirRecursive(dir);
                });
                t.Start();
            }
            catch { /* 清理失败不影响主流程 */ }
        }

        /// <summary>
        /// 读取 C2R 台账下所有 "*.ExcludedApps" 值（4 条候选路径取并集：64/32 位视图 × 有无 16.0 前缀）。
        /// 32 位 C2R 客户端的台账物理落在 WOW6432Node\...（32 位视图），只开 64 位单路径会读空、
        /// 误判“无 C2R 台账/ODT 未生效”——与 ReadProductReleaseIds 保持同源 4 路径口径。
        /// 返回 值名 → 逗号分隔值 的字典（值名形如 "O365ProPlusRetail.ExcludedApps"）；
        /// 全部键不存在或读取异常时返回空字典（不抛异常），由调用方据此判断“本机无 C2R 台账”。
        /// </summary>
        public static IReadOnlyDictionary<string, string> ReadExcludedApps()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var keyPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                };
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (var sub in keyPaths)
                    {
                        using (var key = baseKey.OpenSubKey(sub))
                        {
                            if (key == null) continue;
                            foreach (var name in key.GetValueNames())
                            {
                                if (name.EndsWith(".ExcludedApps", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (key.GetValue(name) is string s) result[name] = s;
                                }
                            }
                        }
                    }
                }
            }
            catch { /* 读取失败返回已收集部分，不中断主流程 */ }
            return result;
        }

        /// <summary>
        /// 读取 C2R 台账的 ProductReleaseIds（已安装产品 PID 列表），用于校验「独立产品（Visio/Project）卸载」是否真正生效。
        /// 与 ReadExcludedApps 不同：独立产品靠 &lt;Remove&gt; 从 ProductReleaseIds 移除，不会体现在 ExcludedApps 里。
        /// 用 Registry64 视图，遍历 4 条候选路径（含 16.0 / WOW6432Node）。读取异常时返回空集合（不抛）。
        /// </summary>
        public static HashSet<string> ReadProductReleaseIds()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var keyPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                };
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (var sub in keyPaths)
                    {
                        using (var key = baseKey.OpenSubKey(sub))
                        {
                            if (key == null) continue;
                            if (key.GetValue("ProductReleaseIds") is string s && s.Length > 0)
                            {
                                foreach (var pid in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    if (pid.Length > 0) result.Add(pid.Trim());
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* 读取失败返回已收集部分，不中断主流程 */ }
            return result;
        }

        /// <summary>
        /// 判定本次 ODT 配置「会不会下载新组件（会往磁盘写字节）」。
        /// 用于 ODT 进度日志措辞：true → 「已写入 N MB」；false（纯删减/排除/无变更）→ 「组件配置中」。
        /// 判据（满足任一即 true）：
        /// ① 目标产品（套件或独立 PID）不在本机 C2R 台账 ProductReleaseIds → 全新安装，会下载；
        /// ② 套件已装，但存在「当前被排除、目标却需要」的组件（currentExcluded − targetExcluded）→ 重装，会下载。
        /// 两者都不满足 = 纯删减/排除/无变更，C2R 不下载新字节。
        /// 本方法为纯台账对比（不扫盘），在 ODT 启动前调用。
        /// </summary>
        public static bool OdtWillDownload(OfficeInstallArguments args,
            IReadOnlyDictionary<string, string> excludedBefore,
            HashSet<string> pidsBefore)
        {
            if (args == null) return false;
            var pids = pidsBefore ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ① 目标产品（套件或独立 PID）本机尚未安装 → 全新安装，会下载
            foreach (var p in args.Products)
            {
                if (p == null || string.IsNullOrEmpty(p.ProductId)) continue;
                if (!pids.Contains(p.ProductId))
                    return true;
            }

            // ② 套件组件重装：「当前被排除、但目标未排除（即目标需要）」的组件 → 重装，会下载
            var targetExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in args.Products)
            {
                if (p?.ExcludeApps == null) continue;
                foreach (var e in p.ExcludeApps)
                    if (!string.IsNullOrWhiteSpace(e)) targetExcluded.Add(e.Trim());
            }

            var currentExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedBefore != null)
            {
                const string suffix = ".ExcludedApps";
                foreach (var kv in excludedBefore)
                {
                    if (string.IsNullOrEmpty(kv.Value) || string.IsNullOrEmpty(kv.Key)) continue;
                    if (!kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    string pid = kv.Key.Substring(0, kv.Key.Length - suffix.Length);
                    if (!pids.Contains(pid)) continue;   // 只看本机实际在装的套件 PID
                    foreach (var s in kv.Value.Split(','))
                    {
                        var v = s.Trim();
                        if (v.Length > 0) currentExcluded.Add(v);
                    }
                }
            }
            foreach (var c in currentExcluded)
                if (!targetExcluded.Contains(c))
                    return true;   // 当前被排除但目标需要 → 重装

            return false;
        }

        /// <summary>ODT 校验结果：Success=目标排除项已生效；NoOp=提交前即已全部生效（无需变更）；Failure=有目标项提交后仍未见效。</summary>
        internal enum VerifyOutcome { Success, NoOp, Failure }

        /// <summary>
        /// ODT 执行后回读 C2R 台账，诚实判定"移除是否真正生效"（退出码 0 不代表生效）。
        /// <para>
        /// 语义（用户 2026-09-10 明确要求）：
        /// 提交 ODT 前/后各读一次 <see cref="ReadExcludedApps"/>，把 config 里生成的
        /// ExcludeApps（小写化后）作为"目标新增项"传入：
        /// 目标新增项在 before/after 里都找不到且 after==before → 报 "⚠️ ODT 未实际修改组件状态"
        /// 目标新增项在 after 出现（before 未出现）          → 报 "✅ 组件已移除（ExcludedApps 已更新）"
        /// 目标项在提交前已全在台账（无需变更）              → 报 "✅ 配置已生效，组件状态与选择一致（无需变更）"
        /// 其余情况（部分变化/部分缺失）                      → 报 "⚠️ ExcludedApps 已变，需人工核对"
        /// </para>
        /// before/after 为 null 或空视为"本机无 C2R 台账"，直接报未生效（ODT 空转铁证）。
        /// 不抛异常，不改变 ODT 主流程的返回码；仅通过 log 给出诚实提示。
        /// </summary>
        /// <param name="expectedExcluded">本次 config 中 <c>ExcludeApp</c> 的 ID 列表（如 OneNote、Lync）；
        /// 空表示本次未做移除意图（如纯安装），此时跳过校验、打日志"未做移除校验"。</param>
        /// <param name="rc">ODT 退出码，仅用于日志展示（0/非 0 不影响判定，实测 0 也常空转）。</param>
        public static VerifyOutcome VerifyRemoveResult(
            IReadOnlyDictionary<string, string> before,
            IReadOnlyDictionary<string, string> after,
            IEnumerable<string> expectedExcluded,
            int rc,
            Action<string> log,
            IEnumerable<string> expectedRemovedProducts = null,
            IEnumerable<string> afterProductIds = null)
        {
            log?.Invoke("  [校验] 回读 C2R 台账判定移除是否生效（退出码 " + rc + "）...");
            if (log == null) return VerifyOutcome.Success;

            var target = (expectedExcluded ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Norm(x))
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 纯安装场景：没有移除目标，跳过比较，仅提示
            if (target.Count == 0)
            {
                log("  [校验] 本次未做移除意图（expectedExcluded 为空），跳过 ExcludedApps 变化比对");
                return VerifyOutcome.Success; // 无移除意图，不算失败
            }

            var beforeFlat = before ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var afterFlat  = after  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 目标里有多少项"新加到 after"（before 没有、after 有）
            var newlyExcluded = new List<string>();
            // 目标里有多少项"两边都没有"（说明 ODT 没把它写进 C2R）
            var stillMissing  = new List<string>();
            foreach (var t in target)
            {
                bool inBefore = beforeFlat.Values.Any(v => SplitAndNorm(v).Contains(t));
                bool inAfter  = afterFlat.Values.Any(v => SplitAndNorm(v).Contains(t));
                if (!inBefore && inAfter) newlyExcluded.Add(t);
                else if (!inBefore && !inAfter) stillMissing.Add(t);
            }

            // No-op 判定：本次目标排除项在提交前已经全部生效（C2R 台账里已经包含它们）。
            // 此时 ODT 无需做任何变更，before==after 是正常结果，不应误报为"未响应"。
            if (target.Count > 0 && target.All(t => beforeFlat.Values.Any(v => SplitAndNorm(v).Contains(t))))
            {
                log("  [OK] 配置已生效，组件状态与选择一致（提交前已包含所有目标排除项，无需变更）");
                return VerifyOutcome.NoOp;
            }

            log("  [校验] before.ExcludedApps = " + Describe(beforeFlat));
            log("  [校验] after .ExcludedApps = " + Describe(afterFlat));
            log("  [校验] 本次 config 排除项 = " + (target.Count > 0 ? string.Join(",", target) : "(空)"));
            if (newlyExcluded.Count > 0)
                log("  [校验] 新增到 after 的项 = " + string.Join(",", newlyExcluded));
            if (stillMissing.Count > 0)
                log("  [校验] 仍未进 after 的项 = " + string.Join(",", stillMissing));

            // —— 独立产品（Visio/Project）卸载校验：靠 <Remove> 从 ProductReleaseIds 移除，不体现在 ExcludedApps ——
            bool standaloneOk = true;
            if (expectedRemovedProducts != null && afterProductIds != null)
            {
                var wantRemoved = expectedRemovedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (wantRemoved.Count > 0)
                {
                    var afterSet = afterProductIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var actuallyRemoved = wantRemoved.Where(p => !afterSet.Contains(p)).ToList();
                    var stillInstalled = wantRemoved.Where(p => afterSet.Contains(p)).ToList();
                    if (actuallyRemoved.Count > 0)
                        log("  [OK] 独立产品已卸载（ProductReleaseIds 已移除: " + string.Join(",", actuallyRemoved) + "）");
                    if (stillInstalled.Count > 0)
                    {
                        log("  [!] 独立产品仍已安装（ProductReleaseIds 仍含: " + string.Join(",", stillInstalled) + "），<Remove> 未生效");
                        standaloneOk = false;
                    }
                    if (actuallyRemoved.Count == 0 && stillInstalled.Count == 0)
                        log("  [校验] 独立产品移除：提交前后 ProductReleaseIds 无变化（可能本就未安装）");
                }
            }

            // 综合判定：只要本次要求的独立产品已真正移除（即便套件部分无变化），即视为整体成功；否则回退到套件判定。
            bool standaloneRequested = expectedRemovedProducts != null
                && expectedRemovedProducts.Any(x => !string.IsNullOrWhiteSpace(x));
            if (standaloneRequested)
                return standaloneOk ? VerifyOutcome.Success : VerifyOutcome.Failure;

            if (beforeFlat.Count == 0 && afterFlat.Count == 0)
            {
                log("  [!] ODT 未实际修改组件状态（本机无 C2R 台账可校验，可能 Office 未装或 C2R 引擎未响应）");
                return VerifyOutcome.Failure;
            }

            if (newlyExcluded.Count > 0 && stillMissing.Count == 0)
            {
                log("  [OK] 组件已移除（ExcludedApps 已更新 " + newlyExcluded.Count + " 项）");
                return VerifyOutcome.Success;
            }

            if (newlyExcluded.Count == 0 && stillMissing.Count > 0)
            {
                log("  [!] ODT 未实际修改组件状态（提交前后 ExcludedApps 对目标项均无变化，可能 C2R 引擎未响应）");
                return VerifyOutcome.Failure;
            }

            // 部分生效
            log("  [!] ExcludedApps 部分变化（" + newlyExcluded.Count + " 项进 after，"
                + stillMissing.Count + " 项未进 after），请人工核对");
            return VerifyOutcome.Failure;
        }

        /// <summary>把 ExcludedApps 单个值（如 "access,publisher,groove"）拆成归一化小写集合。</summary>
        private static HashSet<string> SplitAndNorm(string csv)
        {
            return (csv ?? string.Empty)
                .Split(',')
                .Select(Norm)
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>归一化单个 ExcludedApp ID：去空白、转小写（如 OneNote → onenote，与 C2R 台账口径一致）。</summary>
        private static string Norm(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();
        }

        /// <summary>把"值名 → 值"字典序列化成可打印的单行摘要，供日志展示。</summary>
        private static string Describe(IReadOnlyDictionary<string, string> d)
        {
            if (d == null || d.Count == 0) return "(空)";
            return string.Join(" | ",
                d.Select(kv => "[" + kv.Key + "]=" + (string.IsNullOrEmpty(kv.Value) ? "(空)" : kv.Value)));
        }

        /// <summary>解析微软下载中心页面，返回 ODT exe 直链；解析失败返回 null（由调用方回退手动指引）。</summary>
        private static string FindExeUrl(Action<string> log)
        {
            string url = FindExeUrlInPage(DetailsPage) ?? FindExeUrlInPage(ConfirmPage);
            if (url != null) log("  [*] 已解析 ODT 直链: " + url);
            return url;
        }

        private static string FindExeUrlInPage(string page)
        {
            try
            {
                string html;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, page);
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    // 微软下载中心不需要代理：用无代理直连 client，避免系统里配了死代理（如 Watt Toolkit 离线）时
                    // 被 DefaultWebProxy 拖住导致「解析失败→回退 OTP」。ODT 解析专用，不复用共享单例。
                    using (var direct = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseProxy = false }))
                    using (var resp = direct.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseContentRead, cts.Token).GetAwaiter().GetResult())
                    {
                        resp.EnsureSuccessStatusCode();
                        html = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                var m = Regex.Match(html, @"https?://[^\s""'<>]+officedeploymenttool[^\s""'<>]+\.exe", RegexOptions.IgnoreCase);
                return m.Success ? m.Value : null;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return null; }
        }
    }
}
