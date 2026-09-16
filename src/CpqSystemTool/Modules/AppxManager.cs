using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 商店应用信息（合并自 Win11EasyConfig 61 项 + ZyperWin++ 扩展）
    /// </summary>
    public class AppxDef
    {
        public string Label;          // 显示名
        public string StoreId;        // 微软商店 ID
        public string PackageFamily;  // PackageFamilyName（卸载用）
        public string Description;    // 说明
        public bool AutoRemove;       // 默认是否可安全移除
    }

    public static class AppxManager
    {
        /// <summary>
        /// 完整商店应用目录（合并 Win11EasyConfig 的 61 项精确 StoreId）
        /// </summary>
        public static readonly List<AppxDef> Catalog = new List<AppxDef>
        {
            new AppxDef { Label="照片", StoreId="9WZDNCRFJBH4", PackageFamily="Microsoft.Windows.Photos_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="计算器", StoreId="9WZDNCRFHVN5", PackageFamily="Microsoft.WindowsCalculator_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="时钟", StoreId="9WZDNCRFJ3PR", PackageFamily="Microsoft.WindowsAlarms_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="录音机", StoreId="9WZDNCRFHWKN", PackageFamily="Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="记事本", StoreId="9MSMLRH6LZF3", PackageFamily="Microsoft.WindowsNotepad_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="画图", StoreId="9PCFS5B6T72H", PackageFamily="Microsoft.Paint_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="天气", StoreId="9WZDNCRFJ3Q2", PackageFamily="Microsoft.BingWeather_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="截图工具", StoreId="9MZ95KL8MR0L", PackageFamily="Microsoft.ScreenSketch_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="相机", StoreId="9WZDNCRFJBBG", PackageFamily="Microsoft.WindowsCamera_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Cortana", StoreId="9NFFX4SZZ23L", PackageFamily="Microsoft.549981C3F5F10_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="终端", StoreId="9N0DX20HK701", PackageFamily="Microsoft.WindowsTerminal_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="媒体播放器", StoreId="9WZDNCRFJ3PT", PackageFamily="Microsoft.ZuneMusic_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="电影和电视", StoreId="9WZDNCRFJ3P2", PackageFamily="Microsoft.ZuneVideo_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="资讯", StoreId="9WZDNCRFHVFW", PackageFamily="Microsoft.BingNews_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Dolby Vision扩展", StoreId="9PLTG1LWPHLF", PackageFamily="DolbyLaboratories.DolbyVisionAccess_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="AV1视频扩展", StoreId="9MVZQVXJBQ9V", PackageFamily="Microsoft.AV1VideoExtension_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="VP9视频扩展", StoreId="9N4D0MSMP0PT", PackageFamily="Microsoft.VP9VideoExtensions_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="WebP图像扩展", StoreId="9PG2DK419DRG", PackageFamily="Microsoft.WebpImageExtension_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="HEIF图像扩展", StoreId="9PMMSR1CGPWG", PackageFamily="Microsoft.HEIFImageExtension_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="原始图像扩展", StoreId="9NCTDW2W1BH8", PackageFamily="Microsoft.RawImageExtension_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Web媒体扩展", StoreId="9N5TDP8VCMHS", PackageFamily="Microsoft.WebMediaExtensions_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="邮件和日历", StoreId="9WZDNCRFHVQM", PackageFamily="microsoft.windowscommunicationsapps_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Xbox", StoreId="9MV0B5HZVK9Z", PackageFamily="Microsoft.GamingApp_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Xbox身份验证", StoreId="9WZDNCRD1HKW", PackageFamily="Microsoft.XboxIdentityProvider_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="游戏服务", StoreId="9MWPM2CQNLHN", PackageFamily="Microsoft.GamingServices_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Xbox主机小帮手", StoreId="9WZDNCRFJBD8", PackageFamily="Microsoft.XboxApp_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Xbox游戏工具栏", StoreId="9NZKPSTSNW4P", PackageFamily="Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="小组件", StoreId="9MSSGKG348SP", PackageFamily="MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy", AutoRemove=true },
            new AppxDef { Label="地图", StoreId="9WZDNCRDTBVB", PackageFamily="Microsoft.WindowsMaps_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Clipchamp", StoreId="9P1J8S7CCWWT", PackageFamily="Clipchamp.Clipchamp_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="使用技巧", StoreId="9WZDNCRDTBJJ", PackageFamily="Microsoft.Getstarted_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="便笺", StoreId="9NBLGGH4QGHW", PackageFamily="Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="微软365", StoreId="9WZDNCRD29V9", PackageFamily="Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="画图3D", StoreId="9NBLGGH5FV99", PackageFamily="Microsoft.MSPaint_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="待办ToDo", StoreId="9NBLGGH5R558", PackageFamily="Microsoft.Todos_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="3D查看器", StoreId="9NBLGGH42THS", PackageFamily="Microsoft.Microsoft3DViewer_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="反馈中心", StoreId="9NBLGGH4R32N", PackageFamily="Microsoft.WindowsFeedbackHub_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="获取帮助", StoreId="9PKDZBMV1H3T", PackageFamily="Microsoft.GetHelp_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="扫描", StoreId="9WZDNCRFJ3PV", PackageFamily="Microsoft.WindowsScan_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="快速助手", StoreId="9P7BP5VNWKX5", PackageFamily="MicrosoftCorporationII.QuickAssist_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Power Automate", StoreId="9NFTCH6J7FHV", PackageFamily="Microsoft.PowerAutomateDesktop_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Solitaire游戏", StoreId="9WZDNCRFHWD2", PackageFamily="Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="照片(旧版)", StoreId="9NV2L4XVMCXM", PackageFamily="Microsoft.PhotosLegacy_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="手机连接", StoreId="9NMPJ99VJBWV", PackageFamily="Microsoft.YourPhone_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="家庭安全", StoreId="9PDJDJS743XF", PackageFamily="MicrosoftCorporationII.MicrosoftFamily_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="人脉", StoreId="9NBLGGH10PG8", PackageFamily="Microsoft.People_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Microsoft Teams", StoreId="XP8BT8DW290MPQ", PackageFamily="MicrosoftTeams_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="Skype", StoreId="9WZDNCRFJ364", PackageFamily="Microsoft.SkypeApp_kzf8qxf38zg5c", AutoRemove=true },
            new AppxDef { Label="Outlook", StoreId="9NRX63209R7B", PackageFamily="Microsoft.OutlookForWindows_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="Dev Home", StoreId="9N8MHTPHNGVV", PackageFamily="Microsoft.Windows.DevHome_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="Speedtest", StoreId="9NBLGGH4Z1JC", PackageFamily="Ookla.SpeedtestbyOokla_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="PowerToys", StoreId="XP89DCGQ3K6VLD", PackageFamily="Microsoft.PowerToys_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="Bandizip MSE", StoreId="9P2W3W81SPPB", PackageFamily="Bandisoft.com.15700C60EE320_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="NanaZip", StoreId="9NZL0LRP1BNL", PackageFamily="40174MouriNaruto.NanaZipPreview_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="TranslucentTB", StoreId="9PF4KZ2VN4W9", PackageFamily="28017CharlesMilette.TranslucentTB_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="XboxTCUI", StoreId="9NKNC0LD5NN6", PackageFamily="Microsoft.Xbox.TCUI_8wekyb3d8bbwe", AutoRemove=true },
            new AppxDef { Label="HEVC(制造商)", StoreId="9N4WGH0Z6VHQ", PackageFamily="Microsoft.HEVCVideoExtension_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="HEVC(付费)", StoreId="9NMZLZ57R3T7", PackageFamily="Microsoft.HEVCVideoExtensions_8wekyb3d8bbwe", AutoRemove=false },
            new AppxDef { Label="Microsoft Store", StoreId="9WZDNCRFHVJL", PackageFamily="Microsoft.WindowsStore_8wekyb3d8bbwe", AutoRemove=false },
        };

        public static List<AppxInfo> ListInstalled(Action<string> log)
        {
            // Issue 11: 使用友好中文名（按 Catalog 的 PackageFamily / StoreId 匹配系统的 DisplayName）
            var list = new List<AppxInfo>();
            // 同时获取 Name / PackageFullName / PackageFamilyName / DisplayName / Description
            // -AllUsers：管理员模式运行下必须指定，否则只返回管理员账户的框架包
            // 字段：Name | PackageFullName | PackageFamilyName | InstallLocation | DisplayName | IsFramework | Description
            // Description（末字段）可能含 '|'，用 Split('|',7) 取剩余作描述避免字段错位；IsFramework 是 1/0 标记不含 '|'
            string ps = "Get-AppxPackage -AllUsers | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.PackageFamilyName + '|' + $_.InstallLocation + '|' + $_.DisplayName + '|' + $(if ($_.IsFramework -or $_.IsResourcePackage) { '1' } else { '0' }) + '|' + $_.Description }";
            string outp = Exec.RunPowerShellGet(ps, log);
            if (string.IsNullOrWhiteSpace(outp)) return list;
            foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 7);
                if (parts.Length < 4) continue;
                string name = parts[0].Trim();
                string fullName = parts[1].Trim();
                string familyName = parts[2].Trim();
                string installLoc = parts.Length >= 4 ? parts[3].Trim() : "";
                string displayName = parts.Length >= 5 ? parts[4].Trim() : "";
                bool isFramework = parts.Length >= 6 && parts[5].Trim() == "1";
                string ownDesc = parts.Length >= 7 ? parts[6].Trim() : "";
                // 优先匹配 Catalog：按 PackageFamily 匹配 → 用 Label 作显示名
                string label = displayName;
                AppxDef def = Catalog.Find(c => string.Equals(c.PackageFamily, familyName, StringComparison.OrdinalIgnoreCase));
                if (def != null) label = def.Label;
                else if (!string.IsNullOrEmpty(displayName) && displayName != name && displayName.Length < 60) label = displayName;
                else if (name.Contains("."))
                {
                    // 从短名提取友好名：Microsoft.Windows.Photos → Windows 照片
                    var dotParts = name.Split('.');
                    if (dotParts.Length > 2 && !name.StartsWith("{") && !Guid.TryParse(dotParts[0], out _))
                        label = string.Join(" ", dotParts.Skip(1));
                    else if (name.Length > 40)
                        // GUID 或超长名：截断显示
                        label = name.Substring(0, Math.Min(36, name.IndexOf('_') > 0 ? name.IndexOf('_') : name.Length)) + "...";
                    else label = name;
                }
                else label = name.Length > 40 ? name.Substring(0, 36) + "..." : name;
                // 说明（通用方案）：优先包自带本地化描述，空则读清单对照系统自己的发布商/描述，再关键词兑底
                string publisher = "";
                string manifestDesc = "";
                if (string.IsNullOrWhiteSpace(ownDesc))
                {
                    var mi = ReadManifestInfo(installLoc);
                    publisher = mi.Publisher;
                    manifestDesc = mi.LiteralDescription;
                }
                list.Add(new AppxInfo { Name = label, FullName = fullName, IsFramework = isFramework, Description = BuildAppxDescription(ownDesc, name, familyName, def, label, publisher, manifestDesc) });
            }
            return list;
        }

        /// <summary>
        /// 通用方案生成「说明」文案（换电脑也成立，不依赖固定目录）：
        /// ① 优先包自带本地化描述（Get-AppxPackage 的 .Description）；
        /// ② 空且命中内置目录 → 目录精选描述（预留钩子，暂未填）；
        /// ③ 还空 → 关键词兑底：系统运行时/框架→「勿删」；其它微软→「谨慎」；非微软→「第三方」。
        /// </summary>
        private static string BuildAppxDescription(string ownDesc, string name, string family, AppxDef catalogMatch, string label, string publisher, string manifestDesc)
        {
            // ① 包自带本地化描述（最通用）
            if (!string.IsNullOrWhiteSpace(ownDesc))
            {
                var d = ownDesc.Trim();
                if (d.Length > 100) d = d.Substring(0, 100) + "…";
                return d;
            }
            // ② 内置目录精选描述（预留）
            if (catalogMatch != null && !string.IsNullOrEmpty(catalogMatch.Description))
                return catalogMatch.Description;
            // ③ 系统运行时/框架 → 勿删
            string probe = !string.IsNullOrEmpty(family) ? family : name;
            foreach (var p in SystemFrameworkPatterns)
                if (probe.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Windows 系统运行时/框架组件，勿删（其他 App 依赖它）";
            // ④ 清单自带的字面量描述（系统自己的，比猜的准）
            if (!string.IsNullOrWhiteSpace(manifestDesc))
            {
                var d = manifestDesc.Trim();
                if (d.Length > 100) d = d.Substring(0, 100) + "…";
                return d;
            }
            // ⑤ 微软/第三方提示：优先对照系统自己的发布商（PublisherDisplayName），读不到清单时回退按名字前缀猜
            bool isMs;
            if (!string.IsNullOrWhiteSpace(publisher))
                isMs = publisher.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
            else
                isMs = probe.IndexOf("Microsoft", 0, StringComparison.OrdinalIgnoreCase) == 0;
            if (isMs)
            {
                // 关键看“显示出来的名字”：已清晰（含中文/短英文、非GUID非技术名）就不标自相矛盾的“未列入目录”；
                // 只有显示名也是难懂的 GUID/长点分技术名时，才保留“未列入目录”提示。
                return IsClearName(label)
                    ? "微软系统应用，删除前请确认"
                    : "微软系统应用（未列入目录），删除前请确认";
            }
            // 第三方：有系统自己的发布商就标注，方便辨认
            if (!string.IsNullOrWhiteSpace(publisher))
                return "第三方商店应用（发布商：" + publisher + "）";
            return "第三方商店应用";
        }

        /// <summary>“显示名”是否清晰：含中文、或短小的非 GUID 非点分技术英文名。清晰则不再标“未列入目录”。</summary>
        private static bool IsClearName(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            foreach (char c in label)
                if (c >= '\u4e00' && c <= '\u9fff')
                    return true;                      // 含中文 → 清晰
            if (label.Length > 24) return false;        // 太长 → 视为不明确
            if (label.IndexOf('.') >= 0) return false;  // 点分技术名 → 不明确
            if (label.StartsWith("{")) return false;   // Appx GUID 包名（{...}）→ 不明确（裸 32 位 GUID 已被上面的长度判断排除）
            return true;
        }

        /// <summary>通用 Windows 系统运行时/框架特征（每台 Windows 都相同，非“换电脑就变”的用户软件）。命中即视为“勿删”。</summary>
        private static readonly string[] SystemFrameworkPatterns =
        {
            "Microsoft.VCL.", "Microsoft.UI.Xaml", "Microsoft.NET.Native", "Microsoft.OneCoreUAP",
            "Microsoft.WindowsAppRuntime", "Microsoft.Web.WebView2", "Microsoft.DynamicX",
            "Microsoft.UI.Content", "Microsoft.SystemAppx", "Microsoft.Internal",
            "Microsoft.MixedReality", "Microsoft.PII", "Microsoft.Bluetooth",
            "Microsoft.Windows.Input", "Microsoft.GameServices", "Microsoft.UI.Input",
            "Microsoft.UIExtensions", "Microsoft.Vision",
        };

        // ── 清单对照（路 A：读 AppxManifest.xml 对照系统自己的发布商/描述，零新依赖、不碰 WinRT）──
        struct ManifestInfo
        {
            public string Publisher;          // PublisherDisplayName（系统自己的发布商，如 "Microsoft Corporation"）
            public string LiteralDescription; // 字面量 <Description>（缺失或 ms-resource 引用时为空）
        }
        private static readonly ConcurrentDictionary<string, ManifestInfo> _manifestCache = new();
        // 【P3】缓存无上限保护：Win11 系统更新后 WindowsApps 版本目录变化，旧键只会累积不会失效；
        // 超限整体 Clear（读回退成本低，一次 XML 解析；会话内重复读仍受益）
        private const int ManifestCacheCap = 256;

        /// <summary>读 InstallLocation 下的 <c>AppxManifest.xml</c>，对照系统自己的发布商（PublisherDisplayName）与 literal 描述。
        /// 按 installLocation 会话级缓存，避免重复读文件。权限/异常时返回全空 ManifestInfo（调用方回退按名字前缀推断）。
        /// WindowsApps 目录 ACL 严格，即使提权也可能读不了个别子目录 → UnauthorizedAccessException，视为「读不到」而不外抛。</summary>
        private static ManifestInfo ReadManifestInfo(string installLocation)
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return default;
            if (_manifestCache.TryGetValue(installLocation, out var cached))
                return cached;
            var mi = new ManifestInfo();
            try
            {
                string path = Path.Combine(installLocation, "AppxManifest.xml");
                if (File.Exists(path))
                {
                    var doc = XDocument.Load(path);
                    var root = doc.Root;
                    if (root != null)
                    {
                        var ns = root.GetDefaultNamespace();   // AppxManifest 有默认命名空间，必须带上才能匹配子元素
                        var props = root.Element(ns + "Properties");
                        if (props != null)
                        {
                            mi.Publisher = (props.Element(ns + "PublisherDisplayName")?.Value ?? "").Trim();
                            string d = (props.Element(ns + "Description")?.Value ?? "").Trim();
                            // 只认字面量描述；ms-resource:xxx 是未解析的资源引用（和 .DisplayName 一样），不解析 .pri 就取不到，置空
                            if (d.Length > 0 && !d.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase))
                                mi.LiteralDescription = d;
                        }
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); /* 读 WindowsApps 权限不足 / 文件不存在 → 留空，调用方回退 */ }
            if (_manifestCache.Count > ManifestCacheCap)
                _manifestCache.Clear();
            _manifestCache[installLocation] = mi;
            return mi;
        }

        // Issue 27: 返回 Catalog 中所有 App 的安装状态（Win11EasyConfig 风格：友好中文名 + 安装/未安装状态）
        public static List<AppxInfo> ListCatalogWithStatus(Action<string> log)
        {
            var result = new List<AppxInfo>();
            // 一次性获取所有已安装包的 PackageFamilyName + PublisherId + Name
            // 注意：本程序以管理员权限运行，Get-AppxPackage 默认只返回"管理员账户"的包（基本只有系统框架包）。
            // 必须加 -AllUsers 才能拿到真正登录用户安装的应用（Photos/计算器/天气等）。
            string ps = "Get-AppxPackage -AllUsers | ForEach-Object { $_.PackageFamilyName + '|' + $_.PackageFullName + '|' + $_.Name }";
            string outp = Exec.RunPowerShellGet(ps, log);
            // Debug: 记录原始数据量（方便排查 0 已安装问题）
            int rawLineCount = string.IsNullOrWhiteSpace(outp) ? 0 : outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            log("  [调试] Get-AppxPackage 返回 " + rawLineCount + " 条记录");
            // 用多个匹配维度
            var installedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(outp))
            {
                foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[0]))
                    {
                        installedFamilies.Add(parts[0].Trim());
                        installedNames.Add(parts[2].Trim());
                    }
                }
            }
            log("  [调试] 样本 family: " + string.Join(" | ", installedFamilies.Take(3)));
            log("  [调试] Catalog[0]=[" + Catalog[0].PackageFamily + "] Contains=" + installedFamilies.Contains(Catalog[0].PackageFamily));
            // 打印 installedFamilies 里所有含 "Photos" 或 "Microsoft." 的项，确认实际格式
            var photosLike = installedFamilies.Where(f => f.IndexOf("Photos", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            log("  [调试] 含Photos的family: " + (photosLike.Count > 0 ? string.Join(", ", photosLike) : "(无)"));
            var msLike = installedFamilies.Where(f => f.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
            log("  [调试] Microsoft.开头(前10): " + string.Join(", ", msLike));
            int matchCount = 0;
            foreach (var def in Catalog)
            {
                // 1) 完整 PackageFamily 精确匹配
                // 2) PackageFamily 前缀匹配（去掉 publisher id _xxx）匹配
                // 3) StoreId/Name 模糊匹配（部分应用 StoreId 与 Name 关联）
                string familyPrefix = def.PackageFamily;
                int idx = familyPrefix.IndexOf('_');
                if (idx > 0) familyPrefix = familyPrefix.Substring(0, idx);

                // 多策略匹配
                bool installed = installedFamilies.Contains(def.PackageFamily)
                    || installedFamilies.Any(f => f.StartsWith(familyPrefix + "_", StringComparison.OrdinalIgnoreCase))
                    || installedNames.Any(n => n.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase));
                if (installed) matchCount++;

                result.Add(new AppxInfo { Name = def.Label, FullName = installed ? "1" : "", PackageName = def.PackageFamily });
            }
            log("  [调试] 匹配结果: " + matchCount + "/" + Catalog.Count + " 已安装 (installedFamilies=" + installedFamilies.Count + " installedNames=" + installedNames.Count + ")");
            return result;
        }

        public static void Uninstall(List<string> names, Action<string> log)
        {
            int failCount = 0;
            foreach (var n in names)
            {
                log("卸载: " + n);
                // 在 PowerShell 单引号字符串中，唯一特殊字符是 '，转义为 '' 即可（backtick/$ 在单引号内均为字面量，无需转义）
                string safe = n.Replace("'", "''");
                // ★ 修复：传入的可能是 PackageFamilyName / PackageName / PackageFullName 任一。
                // 必须先 Get-AppxPackage 定位真实的 PackageFullName 与 PackageName（纯短名），
                // 否则 Remove-AppxPackage -Package 需要 full name、Remove-AppxProvisionedPackage -PackageName 需要纯 name，
                // 直接传 family name 会 PSArgumentException（参数错误）。
                //
                // ★ 修正「忽略退出码仍报成功」：原脚本全程 $ErrorActionPreference='SilentlyContinue'，
                // 且两个 Remove-* 都带 -ErrorAction SilentlyContinue，真实失败被静默吞掉、进程退出码恒为 0；
                // 外层又丢弃 RunPowerShell 的返回值，于是无论成败都打印 [OK]。
                // 改为：① Remove-AppxPackage 不再 SilentlyContinue，错误照常写入 stderr（由 Exec.RunPowerShell 记入日志）；
                //       ② 用 $? 统计失败数，脚本 exit $fail 把结果带出为进程退出码；
                //       ③ 包不存在时 exit 2（区别于「卸载失败」）；
                //       ④ 外层接收退出码，失败打印 [FAIL] ...（退出码 N）。
                // Remove-AppxProvisionedPackage 保留 SilentlyContinue：预置副本本就可能不存在，属正常，不计入失败。
                string ps =
                    "$ErrorActionPreference='Continue'; " +
                    "$pkgs = Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { " +
                    "$_.PackageFamilyName -eq '" + safe + "' -or " +
                    "$_.Name -eq '" + safe + "' -or " +
                    "$_.PackageFullName -eq '" + safe + "' }; " +
                    "if (-not $pkgs) { Write-Host ('未找到已安装的包: ' + '" + safe + "'); exit 2 }; " +
                    "$fail = 0; " +
                    "foreach ($p in $pkgs) { " +
                    "Write-Host ('卸载 full=' + $p.PackageFullName + ' name=' + $p.Name); " +
                    // ① 主删：-AllUsers 跨用户删包（部分用户/权限场景可能漏删当前用户）
                    "Remove-AppxPackage -Package $p.PackageFullName -AllUsers; " +
                    "if (-not $?) { $fail++ }; " +
                    // ② A 方案·兜底：当前用户视角再按 full name 删一次，确保包本体真删干净
                    //    （Remove-AppxPackage 不带 -AllUsers = 仅当前用户；与 ① 合起来覆盖全部用户）
                    "Remove-AppxPackage -Package $p.PackageFullName -ErrorAction SilentlyContinue; " +
                    "Remove-AppxProvisionedPackage -Online -PackageName $p.Name -ErrorAction SilentlyContinue; " +
                    "if (-not $?) { Write-Host ('（无预置副本，属正常）') } }; " +
                    "exit $fail";
                int rc = Exec.RunPowerShell(ps, log);
                if (rc == 0) log("[OK] 卸载完成: " + n);
                else
                {
                    log("[FAIL] 卸载 " + n + " 失败（退出码 " + rc + (rc == 2 ? "，未找到已安装的包" : "") + "）");
                    failCount++;
                }
            }
            if (failCount == 0) log("[OK] 批量卸载结束（部分系统应用可能无法移除，属正常）");
            else log("[FAIL] 批量卸载结束：" + failCount + "/" + names.Count + " 个失败，详见上方日志");
        }

        /// <summary>
        /// 安装 Store 应用。三级通道：
        /// ① winget 静默安装（msstore 源，Store 产品 ID，实测可用、无需弹 Store）
        /// ② store.rg-adguard.net 下载 .appxbundle/.msixbundle + Add-AppxPackage（覆盖 winget 搜不到的，断点续传）
        /// ③ 打开 Microsoft Store 页面兜底（用户手动点「获取」）
        /// </summary>
        public static bool Install(string storeId, Action<string> log)
        {
            log("正在安装 StoreId: " + storeId);

            // ① winget 静默安装
            // ★ 修复（安装按钮无响应的根因）：winget.exe 位于 %LOCALAPPDATA%\Microsoft\WindowsApps 下，
            //   本质是一个"应用执行别名"（reparse point），并非真正的 PE。直接以 UseShellExecute=false 调
            //   Process.Start 启动该路径时 CreateProcess 无法解析别名，返回 null → winget 根本没运行 →
            //   静默失败，进而落到已停服的 rg-adguard，最终"点了没反应"。
            //   必须通过 cmd /c 启动，由 cmd 解析别名后才真正执行 winget。
            string winget = FindWinget();
            if (winget != null)
            {
                log("  [1/3] 尝试 winget 静默安装...");
                int r = Exec.RunCmd(new[] { "cmd.exe", "/c", "winget", "install", "--id", storeId, "--source", "msstore",
                    "--accept-source-agreements", "--accept-package-agreements", "--silent" }, log, true);
                if (r == 0) { log("  [OK] winget 安装成功"); return true; }
                log("  [!] winget 退出码 " + r + "（可能未登录 msstore 源或该应用不在源内），走 rg-adguard 通道。");
            }
            else log("  [!] 未找到 winget，走 rg-adguard 通道。");

            // ② rg-adguard 下载安装
            if (InstallViaAdguard(storeId, log)) return true;

            // ③ Store 页面兜底
            log("  [3/3] 打开 Microsoft Store 页面（请手动点「获取」）...");
            Exec.RunPowerShell("start ms-windows-store://pdp/?ProductId=" + storeId, log);
            return false;
        }

        /// <summary>通过 winget search 关键词搜索应用（跨 msstore + winget + winget-font 三个源，结果实时最新）。
        /// 解析输出为 StoreSearchResult 列表（最多 maxResults 个）。
        /// 注：winget v1.29 不支持 --output JSON，需解析表格输出；微软 Store 搜索 API 未公开，所以 "msstore 源" 只能查到 winget 已收录的 ID，
        /// 任意 Store 应用搜索请用浏览器 apps.microsoft.com/store/search?query= 后粘链接到「📋 粘贴 Store 链接安装」。</summary>
        public static List<StoreSearchResult> SearchWinget(string keyword, Action<string> log, int maxResults = 30)
        {
            var list = new List<StoreSearchResult>();
            string winget = FindWinget();
            if (string.IsNullOrEmpty(keyword)) return list;
            if (winget == null) { log("  [!] 未找到 winget，无法搜索"); return list; }
            try
            {
                log("  搜索: " + keyword + "（跨 msstore + winget + winget-font 三个源）...");
                // ★ 显式传 UTF-8：winget 是 UWP 应用，输出 UTF-8；不加这个默认按 GBK 解码就中文乱码
                string outp = Exec.RunCmdGet(new[] { winget, "search", keyword, "--accept-source-agreements" }, null, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(outp)) { log("  [FAIL] 无输出"); return list; }
                var lines = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool headerPassed = false;
                foreach (var raw in lines)
                {
                    var line = raw.TrimEnd();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("---") || line.StartsWith("Name")) { headerPassed = true; continue; }
                    if (!headerPassed) continue;
                    // 拆分为多个连续空格（≥2）的字段
                    var cols = Regex.Split(line, @"\s{2,}");
                    if (cols.Length < 2) continue;
                    // 关键：用最后一列作为 source（健壮匹配：列位置随名称长度变化）
                    string source = cols[cols.Length - 1].Trim();
                    if (source != "msstore" && source != "winget" && source != "winget-font") continue;
                    string name = cols[0].Trim();
                    string id = cols[1].Trim();
                    string version = cols.Length >= 3 ? cols[cols.Length - 2].Trim() : "";
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                    list.Add(new StoreSearchResult { Name = name, Id = id, Version = version, Source = source });
                    if (list.Count >= maxResults) break;
                }
                log("  [OK] 找到 " + list.Count + " 个结果");
            }
            catch (Exception ex) { log("  [FAIL] search 异常: " + ex.Message); }
            return list;
        }

        /// <summary>从用户输入中识别 StoreId（支持 9 位 ID 或完整 Microsoft Store URL，自动提取）。</summary>
        public static string ParseStoreIdFromInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim();
            // 1) 直接是 9 位 StoreId（最常见的形式：9PG2DK419DRG）
            if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Z0-9]{9}$"))
                return input;
            // 2) 完整 URL：https://apps.microsoft.com/detail/9PG2DK419DRG 或 https://www.microsoft.com/store/detail/{slug}/{id}
            var m = System.Text.RegularExpressions.Regex.Match(input, @"/(?:detail|productId)/([A-Z0-9]{9,})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpper();
            // 3) 含 productId= 参数
            m = System.Text.RegularExpressions.Regex.Match(input, @"productId=([A-Z0-9]{9,})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpper();
            return null;
        }

        /// <summary>
        /// 统一搜索：先匹配本地 Catalog（60 个精选，Source=Catalog），再 winget 在线跨源搜索（msstore + winget 社区，实时）。
        /// 在线结果里 Source=msstore 的是 Microsoft Store 应用（9 位 ID），Source=winget 的是社区应用（如 Tencent.QQ）。
        /// 去重：同一 StoreId 只保留本地 Catalog 项。
        /// </summary>
        public static List<StoreSearchResult> SearchMerged(string keyword, Action<string> log)
        {
            var merged = new List<StoreSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(keyword)) return merged;
            string kw = keyword.Trim().ToLowerInvariant();

            // 1. 本地 Catalog 匹配（Label 或 StoreId 包含关键词）
            foreach (var def in Catalog)
            {
                bool hit = (!string.IsNullOrEmpty(def.Label) && def.Label.ToLowerInvariant().Contains(kw))
                        || (!string.IsNullOrEmpty(def.StoreId) && def.StoreId.ToLowerInvariant().Contains(kw));
                if (hit)
                {
                    merged.Add(new StoreSearchResult { Name = def.Label, Id = def.StoreId, Version = "本地收录", Source = "Catalog" });
                    if (!string.IsNullOrEmpty(def.StoreId)) seen.Add(def.StoreId);
                }
            }
            if (merged.Count > 0) log("  [本地] 匹配 " + merged.Count + " 个 Catalog 应用");

            // 2. winget 在线搜索（跨 msstore + winget 社区源）
            var online = SearchWinget(keyword, log, 40);
            foreach (var r in online)
            {
                if (string.IsNullOrEmpty(r.Id) || seen.Contains(r.Id)) continue;
                merged.Add(r);
                seen.Add(r.Id);
            }
            return merged;
        }

        /// <summary>用 winget 安装任意 ID（不限定源，自动匹配 msstore/winget 社区——用于 SearchMerged 返回的社区应用）。</summary>
        public static bool InstallWingetId(string id, Action<string> log)
        {
            string winget = FindWinget();
            if (string.IsNullOrEmpty(id)) { log("  [!!] ID 为空"); return false; }
            if (winget == null) { log("  [!!] 未找到 winget，无法安装"); return false; }
            log("  [1/1] winget install --id " + id + "（自动匹配源）...");
            try
            {
                int r = Exec.RunCmd(new[] { winget, "install", "--id", id,
                    "--accept-source-agreements", "--accept-package-agreements", "--silent" }, log, true);
                if (r == 0) { log("  [OK] winget 安装成功"); return true; }
                log("  [!!] winget 退出码 " + r);
            }
            catch (Exception ex) { log("  [!!] winget 异常: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// store.rg-adguard.net 通道：POST 查询微软 CDN 直链 → 下载 bundle → Add-AppxPackage 安装。
        /// 链接带签名时效（P1 时间戳），必须"解析后立即下载"，不能缓存链接。
        /// </summary>
        public static bool InstallViaAdguard(string storeId, Action<string> log)
        {
            log("  [2/3] rg-adguard 通道：查询微软 CDN 直链...");
            try
            {
                // 1. POST 解析（必须带 UA + Referer，否则 403）
                string html = PostAdguard(storeId, log);
                if (string.IsNullOrEmpty(html)) { log("  [!!] POST 无响应"); return false; }
                if (html.Contains("No files") || html.Contains("not found")) { log("  [!!] rg-adguard 未找到该应用"); return false; }

                // 2. 挑 .appxbundle/.msixbundle（排除 BlockMap）
                string url = null, fname = null;
                foreach (Match m in Regex.Matches(html, "href=\"(https?://[^\"]+)\"[^>]*>([^<]+\\.(?:appxbundle|msixbundle))</a>", RegexOptions.IgnoreCase))
                {
                    string n = m.Groups[2].Value.Trim();
                    if (n.IndexOf("BlockMap", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    url = m.Groups[1].Value; fname = n; break;
                }
                if (string.IsNullOrEmpty(url)) { log("  [!!] 未找到可下载的 bundle（可能付费/加密应用）"); return false; }
                log("  [OK] 找到: " + fname);

                // 3. 下载（统一走 Downloader，保留断点续传 + 重试；调用点均在后台线程，GetAwaiter().GetResult() 安全）
                // 【P2-12】⚠ 同步封异步（阻塞全程下载，分钟级）：仅限后台线程（现调用点在 Appx 页面 RunInBg 内）
                string dest = Path.Combine(Path.GetTempPath(), "cpq_appx_" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(fname));
                bool downloaded = Downloader.DownloadAsync(url, dest, log,
                    maxAttempts: 3, timeoutMs: 60000, readTimeoutMs: 60000,
                    resume: true, retryDelayMs: 5000,
                    userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)").GetAwaiter().GetResult();
                if (!downloaded) { log("  [!!] 下载失败"); TryDelete(dest); return false; }

                // 4. Add-AppxPackage 安装（当前用户；admin 下默认安装到当前用户）
                // ★ 不能用退出码判断成败：powershell.exe 对纯 cmdlet 恒返回 0（Exec.RunPS 取进程 ExitCode），
                //   必须安装后按 StoreId 查包验证（缺依赖/包损坏/权限不足都会导致查不到）。
                log("  正在安装包（Add-AppxPackage，可能需要 1-2 分钟）...");
                Exec.RunPowerShell("Add-AppxPackage -Path '" + dest.Replace("'", "''") + "' -ErrorAction SilentlyContinue", log);
                TryDelete(dest);
                if (IsInstalledByStoreId(storeId, log))
                {
                    log("  [OK] rg-adguard 通道安装成功");
                    return true;
                }
                log("  [!!] 安装后未检测到包（可能缺依赖 VCLibs、包损坏或需要登录商店）");
                return false;
            }
            catch (Exception ex) { log("  [!!] rg-adguard 通道异常: " + ex.Message); return false; }
        }

        /// <summary>POST store.rg-adguard.net/api/GetFiles，返回 HTML（内含 CDN 直链）。
        /// 【P2-12】⚠ 同步封异步（GetAwaiter().GetResult() 阻塞最长 60 秒网络 IO）：仅限后台线程调用（现调用点在 AdGuard 下载流程内）；UI 线程调用会卡界面。</summary>
        private static string PostAdguard(string storeId, Action<string> log)
        {
            string body = "type=ProductId&url=" + Uri.EscapeDataString(storeId) + "&ring=Retail&lang=zh-CN";
            // 复用进程内纯直连无代理单例（HttpClients.Default 已 UseProxy=false），等价原 WebRequest 空 WebProxy 行为，
            // 同时规避 net10 下 WebRequest/HttpWebRequest 的 SYSLIB0014 过时告警。
            var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
            var req = new HttpRequestMessage(HttpMethod.Post, "https://store.rg-adguard.net/api/GetFiles")
            {
                Content = content
            };
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            req.Headers.Referrer = new Uri("https://store.rg-adguard.net/");
            // 60s 超时走每请求 CancellationToken，不改动共享单例的 Timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var resp = HttpClients.Default.SendAsync(req, cts.Token).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        /// <summary>按 StoreId 检查包是否已安装（优先按 Catalog 的 PackageFamilyName 精确匹配，否则按 Name 模糊）。
        /// 先 -AllUsers（提权会话），查不到再查当前用户（非提权会话兜底）。</summary>
        private static bool IsInstalledByStoreId(string storeId, Action<string> log)
        {
            try
            {
                var def = Catalog.FirstOrDefault(c => string.Equals(c.StoreId, storeId, StringComparison.OrdinalIgnoreCase));
                string where = def != null
                    ? "$_.PackageFamilyName -eq '" + def.PackageFamily.Replace("'", "''") + "'"
                    : "$_.Name -like '*" + storeId.Replace("'", "''") + "*'";
                string ps =
                    "$c1 = @(Get-AppxPackage -AllUsers -EA SilentlyContinue | Where-Object { " + where + " }).Count; " +
                    "if ($c1 -gt 0) { '1' } else { @(Get-AppxPackage -EA SilentlyContinue | Where-Object { " + where + " }).Count }";
                var s = Exec.RunPowerShellGet(ps, log).Trim();
                return !string.IsNullOrEmpty(s) && s != "0";
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        /// <summary>新版 Outlook for Windows（OutlookForWindows）是否已安装（独立 Store/AppX 应用，
        /// 包名 Microsoft.OutlookForWindows_8wekyb3d8bbwe）。与经典版 Outlook（C2R 套件应用）相互独立，
        /// 其安装状态不在 C2R ExcludedApps 里反映，必须单独按 AppX 包存在性判定。
        /// 探测失败（权限/无 PowerShell/超时）时返回 false，调用方据此保守视为未装。</summary>
        public static bool IsNewOutlookInstalled()
        {
            // 快速同步路径先行：命中即无需 PowerShell，消除勾选复选框的 ~1s 延迟；
            // 仅在快速路径判定为「未装」时才回退到较慢的 PowerShell 精确确认，兼顾准确性。
            return IsNewOutlookInstalledFast() || IsInstalledByStoreId("9NRX63209R7B", null);
        }

        /// <summary>新版 Outlook（OutlookForWindows）的纯同步快速探测：不启动进程、不调用 PowerShell，
        /// 立即返回。优先按 WindowsApps 前缀通配目录命中；目录枚举因权限抛 UnauthorizedAccessException 属正常现象，
        /// 此时回退注册表探测；两者皆无果/异常则保守返回 false（视为未装）。绝不向外抛异常。</summary>
        public static bool IsNewOutlookInstalledFast()
        {
            // C 方案（治 UI 误判）：
            //   主路径 = WindowsApps 目录枚举。若能成功枚举且没命中 → 包确实不在了，直接 false
            //   （不再让残留的 AppX 注册表状态键单独误判"已装"）；只有目录枚举被 ACL/权限卡住
            //   （拿不到确定结果）时，才允许注册表兜底。
            var bases = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WindowsApps"),
            };
            const string familyPrefix = "Microsoft.OutlookForWindows";
            bool anyDirReadable = false;   // 至少一个 WindowsApps 目录被成功枚举过
            foreach (var baseDir in bases)
            {
                try
                {
                    if (!Directory.Exists(baseDir)) continue;
                    // 主判：按家族名前缀过滤（精确、即时命中，已实测本机返回 2 条）
                    if (Directory.GetDirectories(baseDir, familyPrefix + "_*").Length > 0)
                        return true;
                    // 兜判：部分系统带过滤参数行为异常/返回 0 时，全量枚举后按家族名前缀匹配
                    var allDirs = Directory.GetDirectories(baseDir);
                    foreach (var dirName in allDirs)
                        if (dirName.StartsWith(familyPrefix + "_", StringComparison.OrdinalIgnoreCase))
                            return true;
                    anyDirReadable = true;   // 该目录成功枚举且无命中
                }
                catch (UnauthorizedAccessException) { /* 权限不足，该目录不算 readable，继续下一个 */ }
                catch (IOException) { /* 目录不可访问，同上 */ }
            }
            // 关键修复：目录能成功枚举且没命中 → 包已删，残留注册表键不代表"还装着"，直接判未装。
            if (anyDirReadable)
                return false;
            // 仅当两个目录都因 ACL/权限读不到（拿不到确定结果）时，才用注册表兜底。
            try
            {
                if (IsNewOutlookInRegistry())
                    return true;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            // 安全默认：未命中即视为未装。
            return false;
        }

        /// <summary>
        /// 清除新版 Outlook 在 <c>C:\Program Files\WindowsApps</c> 下的残留包目录（A 方案·治本）。
        /// Remove-AppxPackage 删包后，WindowsApps 下 <c>Microsoft.OutlookForWindows_*</c> 目录壳有时不自动清
        /// （跨用户 / 提权场景残留），会导致 IsNewOutlookInstalledFast 的 WindowsApps 主路径仍命中 → 网格误勾。
        /// 本方法按家族名前缀枚举并删除残留目录：优先普通 Remove-Item；遇 8wekyb3d8bbwe（WindowsApps 全目录 ACL
        /// 拒绝普通访问）时回退 PowerShell <c>Remove-Item -Recurse -Force</c> 提权删。best-effort，失败/无目录均忽略。
        /// </summary>
        public static void RemoveNewOutlookWindowsAppsResidual(Action<string> log)
        {
            const string familyPrefix = "Microsoft.OutlookForWindows";
            var bases = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WindowsApps"),
            };
            foreach (var baseDir in bases)
            {
                try
                {
                    if (!Directory.Exists(baseDir)) continue;
                    var residuals = Directory.GetDirectories(baseDir, familyPrefix + "_*");
                    foreach (var dir in residuals)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            log("  [清残留] 已删 WindowsApps 目录: " + Path.GetFileName(dir));
                        }
                        catch
                        {
                            // 8wekyb3d8bbwe 全目录常带强 ACL，普通 C# 删除会 UnauthorizedAccessException。
                            // 回退 PowerShell 提权删（管理员下可过；非管理员跳过，不影响主流程）。
                            string ps = "$ErrorActionPreference='SilentlyContinue'; " +
                                        "Remove-Item -LiteralPath '" + dir.Replace("'", "''") + "' -Recurse -Force; " +
                                        "if (Test-Path -LiteralPath '" + dir.Replace("'", "''") + "') { exit 1 } else { exit 0 }";
                            int rc = Exec.RunPowerShell(ps, log);
                            log(rc == 0
                                ? "  [清残留] 已删(PS提权) WindowsApps 目录: " + Path.GetFileName(dir)
                                : "  [!] WindowsApps 残留目录删除失败（需管理员）: " + dir);
                        }
                    }
                }
                catch { /* 枚举 WindowsApps 无权限，忽略（best-effort） */ }
            }
        }

        /// <summary>
        /// 清除新版 Outlook（OutlookForWindows）的 AppX 残留注册表状态键（B 层·治本）。
        /// Windows 卸载 AppX 包后，下列"已装包"登记键里常残留 Microsoft.OutlookForWindows_&lt;hash&gt; 子键
        /// （Remove-AppxPackage 删包不保证清键，跨用户 -AllUsers 时 HKCU 侧更易残留）。残留键会让
        /// IsNewOutlookInstalledFast 的注册表兜底误判"新版 Outlook 还在" → 网格复选框误勾。
        /// 本方法 best-effort 删除这些子键（键可能已被引擎清掉，删除失败/不存在均忽略，不影响主流程）：
        ///   HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications\<家族名&gt;
        ///   HKLM\SOFTWARE\Microsoft\Windows\Appx\AppxWindows11PackageState\<家族名&gt;
        ///   HKCU\Software\Microsoft\Windows\CurrentUserAppModel\<家族名&gt;
        /// 仅在卸载调用，需管理员；HKCU 侧无权限时静默跳过。
        /// </summary>
        public static void RemoveNewOutlookResidualRegistryKeys(Action<string> log)
        {
            const string family = "Microsoft.OutlookForWindows";
            var targets = new[]
            {
                // (root, 相对路径, 是否 HKCU)
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications", false),
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Appx\AppxWindows11PackageState", false),
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentUserAppModel", true),
            };
            foreach (var t in targets)
            {
                try
                {
                    using (var parent = t.Item1.OpenSubKey(t.Item2, false))
                    {
                        if (parent == null) continue;
                        foreach (var name in parent.GetSubKeyNames())
                        {
                            if (name.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                            {
                                try { parent.DeleteSubKeyTree(name); log("  [清残留] 已删 " + (t.Item3 ? "HKCU" : "HKLM") + "\\...\\AppxAllUserStore\\..." + name); }
                                catch { /* 该子键无权限/被占用，忽略（best-effort） */ }
                            }
                        }
                    }
                }
                catch { /* 父键读不到（不存在/权限）忽略 */ }
            }
        }

        /// <summary>按注册表探测新版 Outlook 包状态（best-effort，任何读取异常均忽略）。</summary>
        private static bool IsNewOutlookInRegistry()
        {
            // 扫描 Appx 包真实注册键下的子键名/应用名，命中 Microsoft.OutlookForWindows 即判为已装。
            // 不同系统/权限下键结构可能不同：Win10/11 新版 Outlook 实际注册在
            //   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications
            // （子键名形如 "Microsoft.OutlookForWindows_8wekyb3d8bbwe"），旧键 AppxWindows11PackageState /
            // CurrentUserAppModel 可能不存在，故一并兜底。
            string[] stateKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications",
                @"SOFTWARE\Microsoft\Windows\Appx\AppxWindows11PackageState",
                @"Software\Microsoft\Windows\CurrentUserAppModel",
            };
            foreach (var keyPath in stateKeys)
            {
                try
                {
                    // 先查 HKLM，再查 HKCU（同路径分别对应不同 hive）。
                    using (var hklm = Registry.LocalMachine.OpenSubKey(keyPath, false))
                    {
                        if (hklm != null)
                        {
                            foreach (var name in hklm.GetSubKeyNames())
                            {
                                if (name.IndexOf("Microsoft.OutlookForWindows", StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                        }
                    }
                    using (var hkcu = Registry.CurrentUser.OpenSubKey(keyPath, false))
                    {
                        if (hkcu != null)
                        {
                            foreach (var name in hkcu.GetSubKeyNames())
                            {
                                if (name.IndexOf("Microsoft.OutlookForWindows", StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                        }
                    }
                }
                catch { /* 忽略单键读取异常 */ }
            }
            return false;
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        private static string FindWinget()
        {
            // winget.exe 在 %LOCALAPPDATA%\Microsoft\WindowsApps\ 下是 App Execution Alias（reparse point / 符号链接），
            // 不是真正的 PE 可执行文件。.NET 的 File.Exists 不跟随 reparse point，对符号链接永远返回 false。
            // 改为检查父目录存在性：WindowsApps 目录存在即说明系统装了应用执行别名框架，
            // cmd /c winget 由 cmd 解析别名可真正执行（Install 里的 cmd 调用走命令名 "winget"，不传路径）。
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\WindowsApps";
            if (Directory.Exists(dir))
                return dir + @"\winget.exe";
            return null;
        }

        // Issue 4: 列出系统预装应用（Get-AppxProvisionedPackage -Online）
        public static List<AppxInfo> ListProvisioned(Action<string> log)
        {
            var result = new List<AppxInfo>();
            string outp = Exec.RunPowerShellGet("Get-AppxProvisionedPackage -Online | Select-Object -Property DisplayName,PackageName | ConvertTo-Csv -NoTypeInformation", log);
            if (string.IsNullOrEmpty(outp)) return result;
            var lines = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            // 跳过首行表头 (DisplayName,PackageName)
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = ParseCsvLine(lines[i]);
                if (cols.Count < 2) continue;
                string dn = cols[0].Trim('"');
                string pn = cols[1].Trim('"');
                if (string.IsNullOrEmpty(dn)) dn = pn; // 没有 DisplayName 的用 PackageName
                if (!string.IsNullOrEmpty(pn)) result.Add(new AppxInfo { Name = dn, FullName = pn, PackageName = pn });
            }
            return result;
        }

        // Issue 4: 卸载预装应用（DISM Remove-ProvisionedAppxPackage）
        public static void UninstallProvisioned(List<string> packageNames, Action<string> log)
        {
            foreach (var pn in packageNames)
            {
                log("卸载预装: " + pn);
                Exec.RunCmd(new[] { "DISM.exe", "/Online", "/Remove-ProvisionedAppxPackage", "/PackageName:" + pn }, log);
            }
            log("[OK] 系统预装卸载结束");
        }

        // 简单 CSV 行解析（值可能含逗号但通常不引号包裹 DisplayName/ PackageName）
        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            bool inQuote = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { inQuote = !inQuote; continue; }
                if (c == ',' && !inQuote) { list.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            list.Add(cur.ToString());
            return list;
        }
    }

    public class AppxInfo
    {
        public string Name;
        public string FullName;
        public string PackageName; // Issue 4: 预装应用的 PackageName（卸载用）
        public string Description; // 中文简介（目录命中→目录描述；未命中→关键词兑底提示）
        public bool IsFramework;   // 系统框架/资源包（Get-AppxPackage 的 IsFramework/IsResourcePackage）→「隐藏系统框架组件」开关可滤除
        public override string ToString() => Name;
    }

    /// <summary>winget search 结果项（Name / Id / Version / Source）。</summary>
    public class StoreSearchResult
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string Source { get; set; }
    }
}
