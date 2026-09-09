// Office 部署工具（ODT）配置构建与获取 —— 由 office-config-prototype / office-ui-prototype 移植。
// 思路来源：Office Tool Plus 公开历史版本的配置构建方式（仅学思路，代码自研）。
// 本模块提供组件目录（10 个组件）、安装参数模型、config.xml 生成与校验，以及 ODT
// setup.exe 的下载中心解析下载（复用 cpq Downloader / Exec，与 OfficeInstall.cs 同一执行风格）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

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

        public OfficeComponent(string name, string excludeAppId, string standaloneProductId, string standaloneProductIdRetail = null)
        {
            Name = name;
            ExcludeAppId = excludeAppId;
            StandaloneProductId = standaloneProductId;
            StandaloneProductIdRetail = standaloneProductIdRetail;
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
        public static readonly OfficeComponent Outlook = new OfficeComponent("Outlook", "Outlook", "Outlook2024Volume");
        public static readonly OfficeComponent OneNote = new OfficeComponent("OneNote", "OneNote", "OneNote2021Volume"); // 单品仅 2021 版
        public static readonly OfficeComponent Access = new OfficeComponent("Access", "Access", "Access2024Volume");
        public static readonly OfficeComponent Publisher = new OfficeComponent("Publisher", "Publisher", "Publisher2021Volume"); // 单品仅 2021 版
        // 套件内、但 ID 与显示名不同
        public static readonly OfficeComponent OneDrive = new OfficeComponent("OneDrive", "Groove", null); // 官方用 Groove
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

        /// <summary>是否启用“通道-许可类型”合法性校验；默认开启。当某 ProductId 含 "Volume" 却使用订阅制通道时报错。</summary>
        public bool ValidateChannel { get; set; }

        public List<OfficeProductConfig> Products { get; }

        public OfficeInstallArguments()
        {
            Architecture = "64";
            AcceptEula = true;
            ValidateChannel = true;
            Products = new List<OfficeProductConfig>();
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

            // 更新开关：仅当显式设置时输出 <Updates>
            if (args.UpdateEnabled.HasValue)
            {
                configChildren.Add(BuildElement("Updates", new (string Attr, string Value)[]
                {
                    ("Enabled", args.UpdateEnabled.Value ? "TRUE" : "FALSE"),
                }));
            }

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
        private const string DetailsPage = "https://www.microsoft.com/zh-cn/download/details.aspx?id=49117";
        private const string ConfirmPage = "https://www.microsoft.com/zh-cn/download/confirmation.aspx?id=49117";
        // 默认 %TEMP%\odt_cache；可通过 SetCacheDir 自定义，传空串则回退默认。
        private static readonly string DefaultCacheDir = Path.Combine(Path.GetTempPath(), "odt_cache");
        private static string _cacheDir = DefaultCacheDir;

        /// <summary>ODT 下载缓存目录（默认 %TEMP%\odt_cache；SetCacheDir 后立即生效）。</summary>
        public static string CacheDir { get { return _cacheDir; } }

        /// <summary>设置下载缓存目录；传空串则回退默认 %TEMP%\odt_cache。目录立即生效（后续自动下载与缓存查找都用新目录）。</summary>
        public static void SetCacheDir(string dir)
        {
            _cacheDir = string.IsNullOrWhiteSpace(dir) ? DefaultCacheDir : dir.TrimEnd('\\', '/');
        }

        /// <summary>已下载的 setup.exe 缓存路径（随 CacheDir 联动）。</summary>
        public static string CachedExe { get { return Path.Combine(_cacheDir, "setup.exe"); } }

        /// <summary>
        /// 获取 setup.exe：命中缓存直接返回路径，否则解析下载中心拿到 ODT 直链并经 Downloader 下载。
        /// log 输出进度文本；progress 回显下载百分比（0–100）。返回 setup.exe 路径；失败返回 null。
        /// </summary>
        public static string Ensure(Action<string> log, Action<int> progress = null)
        {
            if (File.Exists(CachedExe) && new FileInfo(CachedExe).Length > 100000) return CachedExe;

            try { Directory.CreateDirectory(CacheDir); }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 无法创建缓存目录: " + ex.Message); return null; }

            string url = FindExeUrl(log);
            if (url == null)
            {
                log("  [!] 无法从微软下载中心解析 ODT 下载链接（页面结构可能已变更）。请手动下载：");
                log("      " + DetailsPage);
                log("      将 setup.exe 放到：" + CacheDir);
                return null;
            }

            log("下载 Office 部署工具 (ODT) setup.exe: " + url);
            string tmp = CachedExe + ".tmp";
            // 复用 cpq 统一下载器：请求级超时 + 失败重试 + 进度回调；先写 .tmp 再改名，避免半截缓存被误判已就绪
            bool ok = Downloader.DownloadAsync(url, tmp, log,
                progress: progress,
                maxAttempts: 2,
                timeoutMs: 120000,
                readTimeoutMs: 120000,
                userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)").GetAwaiter().GetResult();
            if (!ok) return null;

            try
            {
                if (File.Exists(CachedExe)) File.Delete(CachedExe);
                File.Move(tmp, CachedExe);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 缓存 setup.exe 失败: " + ex.Message); return null; }

            // 安全加固：校验文件存在且大小合理（非空），避免对损坏/截断的 setup.exe 静默执行
            try
            {
                if (new FileInfo(CachedExe).Length < 100000)
                {
                    log("  [!] 下载的 setup.exe 无效（体积过小）");
                    return null;
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
            if (progress != null) progress(100);
            return CachedExe;
        }

        /// <summary>
        /// 写 config.xml（UTF-8 带 BOM）到临时目录并以 ODT 执行 /configure，返回退出码（0 通常成功）。
        /// 本程序以管理员运行，子进程继承权限，无需额外提权。
        /// </summary>
        public static int RunConfig(string setupExe, string configXml, Action<string> log)
        {
            string dir;
            try { dir = Path.Combine(Path.GetTempPath(), "odt_run_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 无法创建 ODT 运行目录: " + ex.Message); return -1; }
            string configPath = Path.Combine(dir, "config.xml");
            try { File.WriteAllText(configPath, configXml, new UTF8Encoding(true)); }
            catch (Exception ex) { DebugLog.Ignore(ex); log("  [!] 写入 config.xml 失败: " + ex.Message); return -1; }

            try
            {
                int rc = Exec.RunCmd(new[] { setupExe, "/configure", configPath }, log);
                log((rc == 0 ? "  [完成] ODT 退出码 0" : "  [!] ODT 退出码 " + rc) + "，请查看上方输出确认结果");
                return rc;
            }
            catch (Exception ex)
            {
                log("  [!] 执行 ODT 失败: " + ex.Message);
                return -1;
            }
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
                    using (var resp = HttpClients.Default.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseContentRead, cts.Token).GetAwaiter().GetResult())
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
