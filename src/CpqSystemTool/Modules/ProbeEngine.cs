using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CpqSystemTool
{
    // ===================== 浏览器驱动抽象（WebView2 实现 / 可测试桩） =====================
    public interface IProbeBrowser : IDisposable
    {
        // 初始化（启动隐藏 WebView2 宿主窗体 + 启用 CDP 网络域）。返回 false 表示环境不可用（如无 WebView2 Runtime）。
        // diag：诊断日志回调，用于报告初始化各阶段（创建窗口/Loaded/CreateAsync/EnsureCoreWebView2Async/完成），便于定位挂起点。
        Task<bool> InitAsync(Action<string> diag);
        // 单站探测：导航 + DOM 扫描 + 点击策略 + CDP 网络/下载捕获。返回候选直链（url + strategy）。
        Task<BrowserProbeResult> ProbeSiteAsync(string entryUrl, bool skipDownloadCheck);
        // 全名搜索：Bing 中文搜索定位官网下载页。
        Task<BrowserSearchResult> SearchAsync(string name);
    }

    public class BrowserProbeResult
    {
        public List<CandidateUrl> Candidates = new List<CandidateUrl>();
        public string Error;
        public bool StoreOnly;
        public string StoreUrl;
    }

    public class CandidateUrl
    {
        public string Url;
        public string Strategy;
    }

    public class BrowserSearchResult
    {
        public string Url;
        public bool StoreOnly;
        public string StoreUrl;
        public bool NotFound;
    }

    // 引擎内部候选模型（与 JS finalizeResult 对齐）
    internal class Cand
    {
        public string Url;
        public string Strategy;
        public bool IsX64;
        public bool IsArm64;
        public bool IsX86;
        public bool Denylisted;
        public bool Verified;
        public bool LowTrust;
        public string Status;
        public string Ct;
        public int Redirects;
        public int Score;
    }

    // 探针最终结果
    internal class ProbeEngineResult
    {
        public List<MainWindow.ProbeCandidateRow> Rows = new List<MainWindow.ProbeCandidateRow>();
        public string Recommended = "";
        public bool SearchLocated;
        public bool UsedBrowser;   // 是否有厂商依赖浏览器路径（用于决定是否回退）
    }

    // ===================== 静态数据 + 纯逻辑（由 official_exe_finder.js 移植） =====================
    internal static class ProbeData
    {
        public const int VerifyTimeout = 15000;

        public static readonly Regex ExeBinCt = new Regex("application/octet-stream|application/x-msdownload|application/x-msdos-program|application/force-download|binary", RegexOptions.IgnoreCase);
        public static readonly Regex MobileMacPkg = new Regex(@"\.apk|\.ipa|\.dmg|\.app|\.pkg|\.deb|\.rpm(\?|$)", RegexOptions.IgnoreCase);
        public static readonly Regex ExeUrlRe = new Regex(@"https?://[^\s""'<>()\\]+?\.exe(?:\?[^\s""'<>()\\]*)?", RegexOptions.IgnoreCase);
        public static readonly Regex PackageUrlRe = new Regex(@"https?://[^\s""'<>()\\]+?\.(?:zip|7z|rar)(?:\?[^\s""'<>()\\]*)?", RegexOptions.IgnoreCase);
        public static readonly Regex FileRedirectRe = new Regex(@"https?://[^\s""'<>()\\]*?file_redirect\.fcg[^\s""'<>()\\]*", RegexOptions.IgnoreCase);

        public const string KimiWinCdn = "https://kimi-img.moonshot.cn/app/download/windows/kimi_3.1.3.exe";
        public const string CozeWinCdn = "https://lf3-cdn-tos.bytegoofy.com/obj/tron-demo/7617773946401724698/447322331/1.1.29/win32-x64/Coze-v1.1.29-win32-x64.exe";

        public static readonly Dictionary<string, string> FallbackCdn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "coze", CozeWinCdn },
            { "kimi", KimiWinCdn },
        };

        public static readonly Dictionary<string, string> VendorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "qq", "https://im.qq.com/pcqq/" },
            { "qqnt", "https://im.qq.com/pcqq/" },
            { "腾讯qq", "https://im.qq.com/pcqq/" },
            { "pcqq", "https://im.qq.com/pcqq/" },
            { "qqmusic", "https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback" },
            { "qq音乐", "https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback" },
            { "douyin", "https://www.douyin.com/downloadpage" },
            { "抖音", "https://www.douyin.com/downloadpage" },
            { "抖音pc", "https://www.douyin.com/downloadpage" },
            { "抖音电脑版", "https://www.douyin.com/downloadpage" },
            { "sogou", "https://pinyin.sogou.com/" },
            { "搜狗", "https://pinyin.sogou.com/" },
            { "搜狗拼音", "https://pinyin.sogou.com/" },
            { "搜狗输入法", "https://pinyin.sogou.com/" },
            { "sogoupinyin", "https://pinyin.sogou.com/" },
            { "123pan", "https://www.123pan.com/" },
            { "123云盘", "https://www.123pan.com/" },
            { "aliyunpan", "https://www.aliyundrive.com/download" },
            { "阿里云盘", "https://www.aliyundrive.com/download" },
            { "alipan", "https://www.aliyundrive.com/download" },
            { "阿里网盘", "https://www.aliyundrive.com/download" },
            { "raylink", "https://www.raylink.live/download.html" },
            { "瑞联", "https://www.raylink.live/download.html" },
            { "coze", CozeWinCdn },
            { "扣子", CozeWinCdn },
            { "扣子coze", CozeWinCdn },
            { "xshell", "https://www.netsarang.com/en/xshell/" },
            { "xshell7", "https://www.netsarang.com/en/xshell/" },
            { "netsarang", "https://www.netsarang.com/en/xshell/" },
            { "kimi", KimiWinCdn },
            { "kimi智能助手", KimiWinCdn },
            { "月之暗面", KimiWinCdn },
            { "wechat", "https://pc.weixin.qq.com/" },
            { "微信", "https://pc.weixin.qq.com/" },
            { "weixin", "https://pc.weixin.qq.com/" },
            // Geek Uninstaller：官方直链静态可访问（HTTP 200, application/octet-stream），无需浏览器渲染
            { "geek", "https://geekuninstaller.com/geek.exe" },
            { "geekuninstaller", "https://geekuninstaller.com/geek.exe" },
            { "Geek Uninstaller", "https://geekuninstaller.com/geek.exe" },
        };

        public static readonly Dictionary<string, string> VendorCanon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "qq", "qq" }, { "qqnt", "qq" }, { "腾讯qq", "qq" }, { "pcqq", "qq" },
            { "qqmusic", "qqmusic" }, { "qq音乐", "qqmusic" },
            { "douyin", "douyin" }, { "抖音", "douyin" }, { "抖音pc", "douyin" }, { "抖音电脑版", "douyin" },
            { "sogou", "sogou" }, { "搜狗", "sogou" }, { "搜狗拼音", "sogou" }, { "搜狗输入法", "sogou" }, { "sogoupinyin", "sogou" },
            { "123pan", "123pan" }, { "123云盘", "123pan" },
            { "aliyunpan", "aliyunpan" }, { "阿里云盘", "aliyunpan" }, { "alipan", "aliyunpan" }, { "阿里网盘", "aliyunpan" },
            { "raylink", "raylink" }, { "瑞联", "raylink" },
            { "coze", "coze" }, { "扣子", "coze" }, { "扣子coze", "coze" },
            { "xshell", "xshell" }, { "xshell7", "xshell" }, { "netsarang", "xshell" },
            { "kimi", "kimi" }, { "kimi智能助手", "kimi" }, { "月之暗面", "kimi" },
            { "wechat", "wechat" }, { "微信", "wechat" }, { "weixin", "wechat" },
            { "geek", "geek" }, { "geekuninstaller", "geek" }, { "Geek Uninstaller", "geek" },
        };

        public static readonly Dictionary<string, List<string>> OfficialDomains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "qq", new List<string> { "qq.com", "im.qq.com" } },
            { "qqmusic", new List<string> { "y.qq.com", "qq.com" } },
            { "douyin", new List<string> { "douyin.com", "bytedance.com" } },
            { "sogou", new List<string> { "sogou.com" } },
            { "123pan", new List<string> { "123pan.com" } },
            { "aliyunpan", new List<string> { "aliyundrive.com", "alipan.com" } },
            { "raylink", new List<string> { "raylink.live", "raylink.com" } },
            { "coze", new List<string> { "coze.cn", "bytegoofy.com" } },
            { "xshell", new List<string> { "netsarang.com" } },
            { "kimi", new List<string> { "kimi.com", "moonshot.cn" } },
            { "wechat", new List<string> { "weixin.qq.com", "qq.com" } },
        };

        public static readonly HashSet<string> JsHeavy = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aliyunpan", "阿里云盘", "alipan", "阿里网盘",
            "raylink", "瑞联",
            "xshell", "xshell7", "netsarang",
            "123pan", "123云盘",
        };

        public static readonly HashSet<string> AppStoreHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apps.microsoft.com", "apps.apple.com", "play.google.com", "appgallery.huawei.com",
            "app.mi.com", "store.oppo.com", "store.vivo.com.cn", "a.vmall.com", "sj.qq.com", "store.steampowered.com",
        };

        public static readonly HashSet<string> WrongSiteHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "weiyun.com", "www.weiyun.com", "www.weiyun.com.cn",
        };

        public static readonly Dictionary<string, string> StoreOnlyVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "deepseek", "Microsoft Store（Windows）/ App Store（macOS）/ 华为·小米·OPPO·vivo·应用宝 等安卓应用市场" },
            { "深度求索", "Microsoft Store（Windows）/ App Store（macOS）/ 华为·小米·OPPO·vivo·应用宝 等安卓应用市场" },
        };

        public static readonly HashSet<string> GlobalTrustedCdn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github.com", "githubusercontent.com", "bytegoofy.com", "qq.com",
        };

        // 由输入名 fuzzy 推断厂商 canonical key
        public static string InferVendorKey(string name)
        {
            var n = (name ?? "").ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(n)) return null;
            string best = null; int bestLen = 0;
            foreach (var key in VendorMap.Keys)
            {
                var k = key.ToLowerInvariant();
                if (k.Length < 2) continue;
                bool hit = false;
                if (n == k) hit = true;
                else if (n.Contains(k))
                {
                    if (Regex.IsMatch(k, @"^[a-z0-9]+$"))
                    {
                        var esc = Regex.Escape(k);
                        hit = Regex.IsMatch(n, "(^|[^a-z0-9])" + esc + "([^a-z0-9]|$)");
                    }
                    else hit = true;
                }
                if (hit && k.Length > bestLen) { best = key; bestLen = k.Length; }
            }
            if (best == null) return null;
            return VendorCanon.TryGetValue(best, out var canon) ? canon : best;
        }

        public static bool IsWrongSiteUrl(string u)
        {
            try { return WrongSiteHosts.Contains(new Uri(u).Host); } catch { return false; }
        }

        public static bool IsAppStoreUrl(string u)
        {
            try
            {
                var url = new Uri(u);
                var h = url.Host.ToLowerInvariant();
                if (AppStoreHosts.Contains(h)) return true;
                if (h.EndsWith(".microsoft.com") && Regex.IsMatch(url.PathAndQuery, @"/store(\/|$)", RegexOptions.IgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        public static string HostOf(string u)
        {
            try { return new Uri(u).Host.ToLowerInvariant(); } catch { return ""; }
        }

        // 归一化到注册域（去 www. 前缀，取 eTLD+1）
        public static string Registrable(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            if (host.StartsWith("www.")) host = host.Substring(4);
            var parts = host.Split('.');
            if (parts.Length <= 2) return host;
            return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
        }

        public static (bool officialDomain, bool lowTrust) ClassifyTrust(string candUrl, string entryUrl, string source)
        {
            var ch = HostOf(candUrl);
            if (string.IsNullOrEmpty(ch)) return (false, false);
            var eh = HostOf(entryUrl);
            var cr = Registrable(ch); var er = Registrable(eh);
            bool official = !string.IsNullOrEmpty(er) && (cr == er || ch.EndsWith("." + eh) || ch == eh);
            if (!official && GlobalTrustedCdn.Contains(Registrable(ch))) official = true;
            bool lowTrust = source == "search" && !official;
            return (official, lowTrust);
        }

        // 性能优化：Classify 会对每个候选链接调用一次，原来每次调用都 new 4 个 Regex
        // （构造 + JIT 编译 + 缓存预热，是纯浪费）。提为静态只读：编译一次、永久复用。
        private static readonly Regex ReExe = new Regex(@"\.exe(\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ReX64 = new Regex(@"x64|x86[_-]?64|win64|amd64|64bit", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ReNoX64 = new Regex(@"no-?x64", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ReArm64 = new Regex(@"arm64|aarch64", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ReX86 = new Regex(@"(^|[._-])x86([._-]|$)|win32|ia32|32bit", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // 旧版/拒绝名单：原来是每次调用 new 出来的 4 个实例，同样提为静态只读。
        private static readonly Regex[] Denylist =
        {
            new Regex("PCQQ9\\.7\\.25", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("-old[._-]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("_old[._-]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("old\\b(?:version)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        // 架构 / 是否 exe / 是否明显旧版
        public static (bool isExe, bool isX64, bool isArm64, bool isX86, bool denylisted) Classify(string rawUrl)
        {
            var url = rawUrl.Split('#')[0];
            var lower = url.ToLowerInvariant();
            var fn = Uri.UnescapeDataString(url.Split('?')[0].Split('/').Length > 0 ? url.Split('?')[0].Split('/')[url.Split('?')[0].Split('/').Length - 1] : "");
            bool isExe = ReExe.IsMatch(url);
            bool isX64 = isExe && ReX64.IsMatch(lower) && !ReNoX64.IsMatch(lower);
            bool isArm64 = isExe && ReArm64.IsMatch(lower);
            bool isX86 = isExe && ReX86.IsMatch(lower) && !isX64;
            bool denylisted = Array.Exists(Denylist, r => r.IsMatch(url) || r.IsMatch(fn));
            return (isExe, isX64, isArm64, isX86, denylisted);
        }
    }

    // ===================== 探针引擎（进程内，替代 Node + Playwright，优先 WebView2） =====================
    internal static class ProbeEngine
    {
        // 重定向步数硬上限：手动跟随重定向（AllowAutoRedirect=false）时避免无限循环/环回
        private const int MAX_REDIRECTS = 10;

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,  // 显式禁用代理：探针目标多为国外站点，系统代理往往无法访问，默认 UseProxy=true 会导致静默超时
        })
        {
            Timeout = TimeSpan.FromMilliseconds(ProbeData.VerifyTimeout)
        };

        // UA 池：近期 Chrome/Edge 桌面 UA（Win11 x64），按静态计数器轮换，避免固定单一 UA 被目标站按指纹识别。
        // 不再在 DefaultRequestHeaders 写死 UA，改为每个请求自行带一个池内 UA。
        private static readonly string[] UaPool =
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36 Edg/132.0.0.0"
        };
        private static int _uaCounter;

        // 轮换取一个 UA：Interlocked 保证并发安全，uint 取模避免计数器溢出为负时出现负索引
        private static string NextUa()
        {
            int idx = (int)((uint)Interlocked.Increment(ref _uaCounter) % (uint)UaPool.Length);
            return UaPool[idx];
        }

        // 主入口：解析输入 → 快速 HTTP 路径 → 浏览器路径（WebView2）→ 兜底 CDN → 产出行。
        // browser 为 null 或初始化失败时，仅靠快速路径 + 兜底 CDN（不依赖浏览器）。
        public static async Task<ProbeEngineResult> RunAsync(string input, bool skipDownloadCheck, IProbeBrowser browser, Action<string> logf)
        {
            var result = new ProbeEngineResult();
            var trimmed = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                logf("[!] 输入为空或过短，请填写软件官网 URL 或厂商名后重试。");
                return result;
            }

            // 路径 0：仅商店分发厂商
            if (ProbeData.StoreOnlyVendors.TryGetValue(trimmed, out var storeMsg))
            {
                logf("[*] 「" + trimmed + "」仅通过应用商店分发，无直接 exe 直链，跳过探测。");
                result.Rows.Add(MakeStoreOnlyRow(trimmed, storeMsg));
                return result;
            }

            string entryUrl = null;
            string source = "url";
            string vendorKey = null;

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                entryUrl = trimmed; source = "url";
                if (ProbeData.IsAppStoreUrl(entryUrl)) { logf("[*] 入口为应用商店页，无直接 exe 直链，跳过探测。"); result.Rows.Add(MakeStoreOnlyRow(entryUrl, null, entryUrl)); return result; }
            }
            else if (ProbeData.VendorMap.TryGetValue(trimmed, out var mapped))
            {
                entryUrl = mapped; source = "vendor";
                ProbeData.VendorCanon.TryGetValue(trimmed, out vendorKey);
            }
            else if ((vendorKey = ProbeData.InferVendorKey(trimmed)) != null)
            {
                // 路径 2.5：fuzzy 推断为已知厂商 → 直接采用官方入口，跳过搜索（防错站）
                var alias = "";
                foreach (var k in ProbeData.VendorMap.Keys)
                    if ((ProbeData.VendorCanon.TryGetValue(k, out var c) ? c : k) == vendorKey) { alias = k; break; }
                entryUrl = ProbeData.VendorMap[alias];
                source = "vendor";
                logf(">>> 由「" + trimmed + "」推断为厂商「" + vendorKey + "」，直接采用官方入口（跳过搜索，避免错站）。");
            }
            else
            {
                // 路径 3：全名搜索
                logf(">>> 未命中别名表，尝试通过搜索引擎定位「" + trimmed + "」的官网…");
                if (browser != null)
                {
                    try
                    {
                        var sr = await browser.SearchAsync(trimmed);
                        if (sr != null && !string.IsNullOrEmpty(sr.Url) && !sr.StoreOnly && !sr.NotFound)
                        {
                            logf(">>> 通过搜索定位到官网: " + sr.Url);
                            entryUrl = sr.Url; source = "search";
                        }
                        else if (sr != null && sr.StoreOnly)
                        {
                            logf("[*] 搜索结果均为应用商店页（无直接 exe 直链）。");
                            result.Rows.Add(MakeStoreOnlyRow(sr.Url, null, sr.Url));
                            return result;
                        }
                        else
                        {
                            logf("[!] 未找到「" + trimmed + "」的官网，请直接输入官网下载页 URL 重试");
                            return result;
                        }
                    }
                    catch (Exception ex) { logf("[!] 搜索异常: " + ex.Message); return result; }
                }
                else
                {
                    logf("[!] 未命中别名且浏览器不可用，无法搜索，请直接输入官网下载页 URL 重试");
                    return result;
                }
            }

            logf(">>> 探测: " + trimmed + " -> " + entryUrl + "  (source=" + source + ")");

            BrowserProbeResult site = null;
            var isJsHeavy = ProbeData.JsHeavy.Contains(trimmed);
            if (!isJsHeavy && Uri.IsWellFormedUriString(entryUrl, UriKind.Absolute))
            {
                logf("   → 尝试无浏览器快速路径（HTTP 扫描入口）…");
                site = await ProbeSiteFastAsync(entryUrl, skipDownloadCheck, logf);
                if (site != null && site.Candidates.Count > 0)
                    logf("   ✅ 快速路径命中 " + site.Candidates.Count + " 个候选");
                else
                    logf("   快速路径未命中（无静态直链），回退浏览器渲染路径…");
            }
            if ((site == null || site.Candidates.Count == 0) && !ProbeData.ExeUrlRe.IsMatch(entryUrl))
            {
                if (browser != null)
                {
                    result.UsedBrowser = true;
                    try { site = await browser.ProbeSiteAsync(entryUrl, skipDownloadCheck); }
                    catch (Exception ex) { logf("[!] 浏览器探测异常: " + ex.Message); site = null; }
                }
                else
                {
                    logf("[!] 需要浏览器渲染但 WebView2 不可用，跳过。");
                }
            }

            if (site != null && site.Candidates.Count > 0)
            {
                var finalized = await FinalizeAsync(entryUrl, site.Candidates);
                ApplyTrust(finalized, entryUrl, source);
                result.Rows.AddRange(BuildRows(entryUrl, source, finalized));
                result.SearchLocated = source == "search";
                var rec = PickRecommended(finalized);
                result.Recommended = rec != null ? rec.Url : "";
                logf("   候选 " + finalized.Count + " 个，推荐: " + (rec != null ? rec.Url : "无"));
            }

            // 域名反向匹配 VendorMap 直链兜底：
            // 当用户输入官网首页 URL（source=url）时，主探测扫首页 HTML 只找到 /download 等相对链接，
            // 快速路径 + 浏览器渲染均无法提取直链。此时若 URL 域名命中 VendorMap，
            // 直接用 VendorMap 中已验证的直链作为兜底（如 geekuninstaller.com → geek.exe）。
            if ((site == null || site.Candidates.Count == 0 || result.Recommended == "")
                && string.IsNullOrEmpty(vendorKey)
                && Uri.TryCreate(entryUrl, UriKind.Absolute, out var entryUri)
                && entryUri.Host != null)
            {
                string matchedKey = null;
                foreach (var vmKey in ProbeData.VendorMap.Keys)
                {
                    if (ProbeData.OfficialDomains.TryGetValue(vmKey, out var domains)
                        && domains.Exists(d => d.Equals(entryUri.Host, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedKey = vmKey;
                        break;
                    }
                }
                if (matchedKey != null)
                {
                    vendorKey = matchedKey;
                    string vmUrl = ProbeData.VendorMap[matchedKey];
                    logf(">>> 域名「" + entryUri.Host + "」命中厂商「" + matchedKey + "」，使用 VendorMap 直链兜底: " + vmUrl);
                    var fbRes = await ProbeSiteFastAsync(vmUrl, skipDownloadCheck, logf);
                    if (fbRes != null && fbRes.Candidates.Count > 0)
                    {
                        var fin = await FinalizeAsync(vmUrl, fbRes.Candidates);
                        ApplyTrust(fin, vmUrl, "vendor");
                        var rec = PickRecommended(fin);
                        if (rec != null)
                        {
                            result.Rows.AddRange(BuildRows(vmUrl, "vendor", fin));
                            if (string.IsNullOrEmpty(result.Recommended)) result.Recommended = rec.Url;
                            logf("   ✅ 域名反向匹配兜底命中: " + rec.Url);
                        }
                    }
                    else
                    {
                        logf("   ⚠️ VendorMap 直链验证失败（可能 404），请手动更新探针数据。");
                    }
                }
            }

            // 版本漂移兜底：主探测未获直链，且该厂商配置了官方 CDN 兜底
            if ((site == null || site.Candidates.Count == 0 || result.Recommended == "") && !string.IsNullOrEmpty(vendorKey) && ProbeData.FallbackCdn.TryGetValue(vendorKey, out var fb))
            {
                logf(">>> 主探测未获直链，尝试官方 CDN 兜底: " + fb);
                var fbRes = await ProbeSiteFastAsync(fb, skipDownloadCheck, logf);
                if (fbRes != null && fbRes.Candidates.Count > 0)
                {
                    var fin = await FinalizeAsync(fb, fbRes.Candidates);
                    ApplyTrust(fin, fb, "vendor");
                    var rec = PickRecommended(fin);
                    if (rec != null)
                    {
                        result.Rows.AddRange(BuildRows(fb, "vendor", fin));
                        if (string.IsNullOrEmpty(result.Recommended)) result.Recommended = rec.Url;
                        logf("   ✅ 兜底命中: " + rec.Url);
                    }
                    else logf("   ⚠️ 兜底 CDN 直链验证失败（可能版本已过期 404），请在探针中更新版本号或改回官网实时抓取。");
                }
                else
                {
                    logf("   ⚠️ 兜底 CDN 直链验证失败（可能版本已过期 404），请在探针中更新版本号或改回官网实时抓取。");
                    result.Rows.Add(MakeCdnFailedRow(trimmed, vendorKey));
                }
            }

            return result;
        }

        // 无浏览器快速路径：HTTP 抓取入口 HTML/JSONP，扫描直链并验证
        private static async Task<BrowserProbeResult> ProbeSiteFastAsync(string entryUrl, bool skipDownloadCheck, Action<string> logf)
        {
            try
            {
                logf("   [DIAG] ProbeSiteFastAsync: entryUrl=" + entryUrl);
                var got = await HttpGetAsync(entryUrl, MAX_REDIRECTS, 0, 8000);
                logf("   [DIAG] HttpGetAsync 返回: ok=" + got.ok + ", status=" + got.status + ", isBinary=" + got.isBinary + ", bodyLen=" + (got.body?.Length ?? 0));
                if (!got.ok)
                {
                    logf("   [DIAG] HttpGetAsync 失败，返回 null");
                    return null;
                }

                var found = new Dictionary<string, CandidateUrl>(StringComparer.OrdinalIgnoreCase);
                void Add(string url, string strategy)
                {
                    if (string.IsNullOrEmpty(url)) return;
                    var norm = url.Split('#')[0];
                    if (!found.ContainsKey(norm)) found[norm] = new CandidateUrl { Url = norm, Strategy = strategy };
                    else if (!found[norm].Strategy.Contains(strategy)) found[norm].Strategy += "+" + strategy;
                }

                // 帮助将 HTML 中的相对路径（如 /geek.zip）解析为绝对 URL
                string ResolveRelative(string raw)
                {
                    if (string.IsNullOrEmpty(raw)) return raw;
                    var normalized = raw.Split('#')[0].Trim();
                    if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return normalized;
                    try
                    {
                        var baseUri = new Uri(entryUrl);
                        var resolved = new Uri(baseUri, normalized).ToString();
                        return resolved;
                    }
                    catch { return null; }
                }

                // 入口本身是二进制可执行文件
                bool entryIsExe = ProbeData.ExeUrlRe.IsMatch(entryUrl);
                logf("   [DIAG] ExeUrlRe.IsMatch(entryUrl)=" + entryIsExe + ", isBinary=" + got.isBinary);
                if (got.isBinary && entryIsExe)
                {
                    Add(entryUrl, "anchor");
                    logf("   [DIAG] 已添加直链: " + entryUrl);
                }

                if ((got.body ?? "").Length < 50 && !got.isBinary)
                {
                    logf("   [DIAG] body太短且非二进制，返回 null");
                    return null;
                }

                // 扫描所有可能的下载链接（.exe + .zip/.7z/.rar，并解析相对路径）
                var exes = ProbeData.ExeUrlRe.Matches(got.body ?? "");
                foreach (Match m in exes)
                {
                    var abs = ResolveRelative(m.Value);
                    if (!string.IsNullOrEmpty(abs)) Add(abs, "anchor");
                }
                var packages = ProbeData.PackageUrlRe.Matches(got.body ?? "");
                foreach (Match m in packages)
                {
                    var abs = ResolveRelative(m.Value);
                    if (!string.IsNullOrEmpty(abs)) Add(abs, "anchor");
                }
                var fcgs = ProbeData.FileRedirectRe.Matches(got.body ?? "");
                foreach (Match m in fcgs)
                {
                    var abs = ResolveRelative(m.Value);
                    if (!string.IsNullOrEmpty(abs)) Add(abs, "jsonp");
                }

                if (found.Count == 0)
                {
                    logf("   [DIAG] found.Count==0，返回 null");
                    return null;
                }
                logf("   [DIAG] found.Count=" + found.Count + "，返回结果");
                var res = new BrowserProbeResult();
                res.Candidates.AddRange(found.Values);
                return res;
            }
            catch { return null; }
        }

        // 把 found 整理为候选：并行验证 + 打分
        private static async Task<List<Cand>> FinalizeAsync(string entryUrl, List<CandidateUrl> found)
        {
            var cands = new List<Cand>();
            using (var gate = new SemaphoreSlim(5, 5))
            {
                var tasks = found.Select(async f =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var cl = ProbeData.Classify(f.Url);
                        bool shouldVerify = cl.isExe || ProbeData.FileRedirectRe.IsMatch(f.Url);
                        VerifyResult v;
                        if (shouldVerify) v = await VerifyExeAsync(f.Url, 0).ConfigureAwait(false);
                        else v = new VerifyResult { url = f.Url, verified = false, status = "SKIP", ct = "", redirects = 0 };
                        if (v.redirects > 0 && Regex.IsMatch(v.finalUrl ?? f.Url, @"\.exe(\?|$)", RegexOptions.IgnoreCase))
                            f.Strategy = (f.Strategy ?? "") + "+redirect";
                        return new Cand
                        {
                            Url = f.Url,
                            Strategy = f.Strategy,
                            IsX64 = cl.isX64,
                            IsArm64 = cl.isArm64,
                            IsX86 = cl.isX86,
                            Denylisted = cl.denylisted,
                            Verified = v.verified,
                            Status = v.status,
                            Ct = v.ct ?? "",
                            Redirects = v.redirects,
                        };
                    }
                    finally { gate.Release(); }
                }).ToList();
                cands.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            }

            foreach (var c in cands)
            {
                int score = 0;
                if (c.Verified) score += 3;
                if (Regex.IsMatch(c.Url, @"\.exe(\?|$)", RegexOptions.IgnoreCase)) score += 1;
                if (c.IsX64) score += 4;
                if (c.IsArm64) score -= 10;
                if (c.IsX86) score -= 2;
                if (c.Denylisted) score -= 100;
                c.Score = score;
            }
            cands.Sort((a, b) => b.Score - a.Score);
            return cands;
        }

        // 按来源做域名信任判定并标记 lowTrust（search 来源非官方域名的候选需人工核对，不自动推荐）
        private static void ApplyTrust(List<Cand> cands, string entryUrl, string source)
        {
            foreach (var c in cands)
            {
                var t = ProbeData.ClassifyTrust(c.Url, entryUrl, source);
                c.LowTrust = t.lowTrust;
            }
        }

        private static Cand PickRecommended(List<Cand> cands)
        {
            // 首选：高分且明确可信（已验证或显式 x64）的候选
            var eligible = cands.FindAll(s => !s.Denylisted && !s.LowTrust && s.Score > 0 && (s.Verified || s.IsX64));
            if (eligible.Count > 0) return eligible[0];

            // 降级：部分官方入口（如 qq）返回的直链真实但未被 VerifyExe 验证、也未显式标记 x64，
            // 此时 (Verified || IsX64) 全为 false 会把真实 exe 滤掉 → 推荐为空。
            // 第二兜底：返回排序首条「非封禁、非低信任、URL 是合法 .exe」的候选。
            var fallbackExe = cands.FindAll(s => !s.Denylisted && !s.LowTrust
                && Uri.IsWellFormedUriString(s.Url, UriKind.Absolute)
                && ProbeData.ExeUrlRe.IsMatch(s.Url));
            if (fallbackExe.Count > 0) return fallbackExe[0];

            // 第三兜底：连 .exe 直链都没有时，返回首条「非封禁、非低信任、URL 合法、且不是脚本/配置/页面文件」的候选（可能是官网落地页）。
            // 这样「推荐直链」框至少展示最可信入口，而不是空白；同时避免把 .js/.css 等页面资源误当作安装包推荐。
            var nonInstallerExt = new Regex(@"\.(js|css|html|htm|json|xml|txt|png|jpg|jpeg|gif|svg|webp|ico|woff|woff2|ttf|eot)(\?|$)", RegexOptions.IgnoreCase);
            var fallbackAny = cands.FindAll(s => !s.Denylisted && !s.LowTrust
                && Uri.IsWellFormedUriString(s.Url, UriKind.Absolute)
                && !nonInstallerExt.IsMatch(s.Url));
            return fallbackAny.Count > 0 ? fallbackAny[0] : null;
        }

        // ===================== HTTP 工具 =====================

        // 补齐浏览器常用请求头：UA 轮换、Accept / Accept-Encoding / Accept-Language、同源入口 Referer。
        // Referer 取当前 URL 的 scheme://host/（同源入口），贴近浏览器导航行为；跨源重定向时按新 URL 重新计算，天然同源、不泄漏上一跳。
        private static void ApplyBrowserHeaders(HttpRequestMessage req, string url)
        {
            req.Headers.TryAddWithoutValidation("User-Agent", NextUa());
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            try
            {
                var u = new Uri(url);
                req.Headers.TryAddWithoutValidation("Referer", u.GetLeftPart(UriPartial.Authority) + "/");
            }
            catch { /* URL 非法时由上层校验兜底，这里不设 Referer */ }
        }

        // 按 Content-Encoding 手动解压响应正文再读为 UTF-8 字符串。
        // HttpClientHandler 未开 AutomaticDecompression，协商了 gzip/deflate 后必须自行解压，否则读到的是压缩字节乱码。
        private static async Task<string> ReadBodyDecompressedAsync(HttpContent content, string encoding)
        {
            using (var stream = await content.ReadAsStreamAsync())
            {
                if (encoding == "gzip")
                    using (var ds = new GZipStream(stream, CompressionMode.Decompress))
                    using (var sr = new StreamReader(ds, Encoding.UTF8))
                        return await sr.ReadToEndAsync();
                using (var ds = new DeflateStream(stream, CompressionMode.Decompress))
                using (var sr = new StreamReader(ds, Encoding.UTF8))
                    return await sr.ReadToEndAsync();
            }
        }

        private static async Task<(bool ok, int status, string finalUrl, string body, bool isBinary)> HttpGetAsync(string url, int maxRedirect, int depth, int timeoutMs)
        {
            if (depth > maxRedirect || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return (false, 0, url, "", false);
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyBrowserHeaders(req, url);
                using var resp = await Http.SendAsync(req, cts.Token);
                int code = (int)resp.StatusCode;
                if (resp.Headers.Location != null && (code == 301 || code == 302 || code == 303 || code == 307 || code == 308))
                {
                    var next = resp.Headers.Location.IsAbsoluteUri ? resp.Headers.Location.AbsoluteUri : new Uri(new Uri(url), resp.Headers.Location.ToString()).AbsoluteUri;
                    return await HttpGetAsync(next, maxRedirect, depth + 1, timeoutMs);
                }
                var ct = (resp.Content.Headers.ContentType?.MediaType ?? "").ToLowerInvariant();
                bool isBinary = ProbeData.ExeBinCt.IsMatch(ct) || ProbeData.ExeUrlRe.IsMatch(url);
                if (isBinary) return (code >= 200 && code < 300, code, url, "", true);
                if (code < 200 || code >= 300) return (false, code, url, "", false);
                string body;
                var enc = (resp.Content.Headers.ContentEncoding.FirstOrDefault() ?? "").ToLowerInvariant();
                if (enc == "gzip" || enc == "deflate")
                    body = await ReadBodyDecompressedAsync(resp.Content, enc);
                else
                    body = await resp.Content.ReadAsStringAsync();
                if (body.Length > 5 * 1024 * 1024) body = body.Substring(0, 5 * 1024 * 1024);
                return (true, code, url, body, false);
            }
            catch { return (false, 0, url, "", false); }
        }

        private static async Task<VerifyResult> VerifyExeAsync(string rawUrl, int depth)
        {
            if (depth > MAX_REDIRECTS) return new VerifyResult { url = rawUrl, status = "TOO_MANY_REDIRECTS", verified = false, redirects = depth };
            if (!Uri.IsWellFormedUriString(rawUrl, UriKind.Absolute)) return new VerifyResult { url = rawUrl, status = "INVALID_URL", verified = false, redirects = depth };
            try
            {
                using var cts = new CancellationTokenSource(ProbeData.VerifyTimeout);
                var req = new HttpRequestMessage(HttpMethod.Get, rawUrl);
                ApplyBrowserHeaders(req, rawUrl);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023);
                using var resp = await Http.SendAsync(req, cts.Token);
                int code = (int)resp.StatusCode;
                var loc = resp.Headers.Location;
                var ct = (resp.Content.Headers.ContentType?.MediaType ?? "").ToLowerInvariant();
                if (loc != null && (code == 301 || code == 302 || code == 303 || code == 307 || code == 308))
                {
                    var next = loc.IsAbsoluteUri ? loc.AbsoluteUri : new Uri(new Uri(rawUrl), loc.ToString()).AbsoluteUri;
                    return await VerifyExeAsync(next, depth + 1);
                }
                return new VerifyResult
                {
                    url = rawUrl,
                    finalUrl = rawUrl,
                    status = code.ToString(),
                    ct = ct,
                    verified = (code >= 200 && code < 300) && ProbeData.ExeBinCt.IsMatch(ct),
                    redirects = depth,
                };
            }
            catch (Exception ex) { return new VerifyResult { url = rawUrl, status = "ERR:" + ex.Message, verified = false, redirects = depth }; }
        }

        private class VerifyResult
        {
            public string url;
            public string finalUrl;
            public string status;
            public bool verified;
            public string ct;
            public int redirects;
        }

        // ===================== 行模型构建 =====================
        private static List<MainWindow.ProbeCandidateRow> BuildRows(string entryUrl, string source, List<Cand> cands)
        {
            var rows = new List<MainWindow.ProbeCandidateRow>();
            string recUrl = PickRecommended(cands)?.Url ?? "";
            foreach (var c in cands)
            {
                if (c.Status == "SKIP") continue; // 未验证的非安装包资源（如 .js 页面脚本）不显示在候选列表中
                if (c.Status == "404") continue;  // 死链（服务器明确返回 404）不显示在候选列表中
                var trust = ProbeData.ClassifyTrust(c.Url, entryUrl, source);
                rows.Add(new MainWindow.ProbeCandidateRow
                {
                    Source = entryUrl,
                    Url = c.Url,
                    Strategy = c.Strategy,
                    Arch = c.IsX64 ? "x64" : (c.IsArm64 ? "arm64" : (c.IsX86 ? "x86" : "?")),
                    Verified = c.Verified,
                    StatusText = c.Status,
                    ContentType = c.Ct,
                    IsRecommended = !string.IsNullOrEmpty(recUrl) && c.Url == recUrl,
                    LowTrust = trust.lowTrust,
                });
            }
            return rows;
        }

        private static MainWindow.ProbeCandidateRow MakeStoreOnlyRow(string entryUrl, string storeNote, string storeUrl = null)
        {
            return new MainWindow.ProbeCandidateRow
            {
                Source = entryUrl,
                Url = storeUrl ?? "(应用商店分发)",
                Strategy = "store-only",
                Arch = "?",
                Verified = false,
                StatusText = "STORE_ONLY",
                ContentType = "",
                IsRecommended = false,
                LowTrust = false,
            };
        }

        private static MainWindow.ProbeCandidateRow MakeCdnFailedRow(string input, string vendorKey)
        {
            return new MainWindow.ProbeCandidateRow
            {
                Source = input,
                Url = "(官方 CDN 兜底失败，可能版本已过期)",
                Strategy = "cdn-fallback-failed",
                Arch = "?",
                Verified = false,
                StatusText = "CDN_FALLBACK_FAILED",
                ContentType = "",
                IsRecommended = false,
                LowTrust = false,
            };
        }
    }
}
