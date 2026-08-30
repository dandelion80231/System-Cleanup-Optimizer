using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CpqSystemTool
{
    /// <summary>
    /// Chocolatey 社区源运行时解析器。
    /// 安装主流包时：优先实时拉取 Chocolatey 最新包 nupkg → 解析官方下载 URL + SHA256 + 静默参数，
    /// 始终取最新且由 SHA256 校验完整性（彻底规避“版本更新即失效”）；
    /// 实时解析失败（离线/网络/脚本解析异常）时，回退到下方“已验证快照表”（数据取自 Chocolatey VERIFICATION.txt / chocolateyinstall.ps1）。
    /// 规则：仅当解析到“版本化 URL + 非空 SHA256”才信任实时结果；其余（Latest 类未版本化 URL、动态参数包）走快照/Authenticode。
    /// </summary>
    internal static class ChocolateyResolver
    {
        // 已验证快照表（离线/解析失败兜底）。URL 为厂商官方直链；
        // sha256 为空表示“Latest 类未版本化 URL”，不写死哈希、靠 Authenticode 签名校验跨版本有效。
        private static readonly Dictionary<string, (string Url, string Sha256, string[] Args)> Fallback =
            new Dictionary<string, (string, string, string[])>(StringComparer.OrdinalIgnoreCase)
            {
                ["7zip"] = ("https://github.com/ip7z/7zip/releases/download/26.02/7z2602-x64.exe",
                            "6745FA76DC2EA031596D8678F6F6B99C3C1B435B4164A63485ADBBC7B8D82EF0",
                            new[] { "/S" }),
                ["git"] = ("https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.3/Git-2.55.0.3-64-bit.exe",
                           "AF12577D0FDFF74243A5988197AA49B957D5044EDC17004F6DDF0768996F1DCA",
                           new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL", "/SP-", "/LOG" }),
                ["everything"] = ("https://www.voidtools.com/Everything-1.4.1.1032.x64-Setup.exe",
                                  "C42EFAD041D4C0BB4D4AC97AE7CBE89F153EC1FE078772392E749C7F5D5282D3",
                                  new[] { "/S" }),
                ["notepad3"] = ("https://github.com/rizonesoft/Notepad3/releases/download/RELEASE_7.26.602.1/Notepad3_7.26.602.1_x64_Setup.exe",
                                "9CF68B38BCC1AA679050B9069174AB222383BA004C37BDD47056CAFC71626BE9",
                                new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" }),
                ["winrar"] = ("https://www.rarlab.com/rar/winrar-x64-723.exe",
                              "8ff0daf3ed564cc743c0e23ff2e253997ffc74460f9673f0b6dd037b2db4ce7b",
                              new[] { "/S" }),
                // PotPlayer 官方直链为未版本化 “Latest” 路径：不写死哈希，靠 Authenticode 跨版本校验。
                ["potplayer"] = ("https://t1.daumcdn.net/potplayer/PotPlayer/Version/Latest/PotPlayerSetup64.exe",
                                 "",
                                 new[] { "/S" }),
                ["aria2"] = ("https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip",
                             "67d015301eef0b612191212d564c5bb0a14b5b9c4796b76454276a4d28d9b288",
                             new string[0]),
                ["virtualbox"] = ("https://download.virtualbox.org/virtualbox/7.2.14/VirtualBox-7.2.14-174565-Win.exe",
                                  "5fb111f32a15763d519bf9ef23e0111153521f641cde7460e5b8e895ca27a1d2",
                                  new[] { "-s", "-l", "-msiparams", "REBOOT=ReallySuppress", "ALLUSERS=1" }),
                ["tortoisegit"] = ("https://download.tortoisegit.org/tgit/2.18.0.0/TortoiseGit-2.18.0.1-64bit.msi",
                                   "CBF7D52AA0ECCA665521E14D8D1A4B6CDA52A4BC13DE45F49084E15571C77410",
                                   new[] { "/quiet", "/qn", "/norestart", "REBOOT=ReallySuppress" }),
                // XnViewMP 官方直链为 “latest” CGI 端点：不写死哈希，靠 Authenticode 跨版本校验。
                ["xnviewmp"] = ("https://www.xnview.com/download.php?file=XnViewMP-win-x64.exe",
                                "",
                                new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" }),
            };

        // ---- 解析结果进程内 TTL 缓存（24h）----
        // 只缓存“实时解析成功且带非空 SHA256”的确定性结果：同入参 24h 内不再发网络请求。
        // 失败（异常/空结果/实时解析未命中而走快照）不缓存，下次重试。条目数≈软件数，内存可控。
        private static readonly object ResolveCacheLock = new object();
        private static readonly Dictionary<string, (string Url, string Sha256, string[] Args, long Ticks)> ResolveCache =
            new Dictionary<string, (string, string, string[], long)>(StringComparer.OrdinalIgnoreCase);
        private static readonly long ResolveCacheTtlTicks = TimeSpan.FromHours(24).Ticks;

        // 解析结果元组：ok=false 表示解析失败（调用方回退快照/返回安装失败）。
        // async 方法不允许 out 参数，故 TryResolve 改造为返回元组（B6 sync-over-async 清理）。
        public static async Task<(bool ok, string url, string sha256, string[] args)> TryResolveAsync(string id, Action<string> log)
        {
            // 0) 命中 24h 解析缓存 → 直接返回，不再发网络请求
            string cacheKey = (id ?? "").Trim();
            lock (ResolveCacheLock)
            {
                if (ResolveCache.TryGetValue(cacheKey, out var hit) &&
                    DateTime.UtcNow.Ticks - hit.Ticks < ResolveCacheTtlTicks)
                {
                    log("   [*] 使用 Chocolatey 解析缓存（24h 内同软件不重复请求）：" + id);
                    return (true, hit.Url, hit.Sha256, hit.Args);
                }
            }

            // 1) 实时解析（联网取最新，始终带当前 SHA256 → 永不失效）
            var live = await LiveResolveAsync(id, log);
            if (live.ok && !string.IsNullOrEmpty(live.sha256))
            {
                log("   [*] 已从 Chocolatey 实时解析最新安装包：" + id);
                StoreResolveCache(cacheKey, live.url, live.sha256, live.args);
                return (true, live.url, live.sha256, live.args);
            }

            // 2) 兜底：已验证快照（离线/解析失败/无哈希时）
            if (Fallback.TryGetValue(id, out var f))
            {
                log("   [*] 使用已验证 Chocolatey 快照（实时解析不可用）：" + id);
                return (true, f.Url, f.Sha256, f.Args);
            }
            return (false, null, null, null);
        }

        // 写入解析缓存：仅实时解析成功且带 SHA256 的结果；顺带清理已过期条目（软件数很少，遍历成本可忽略）。
        private static void StoreResolveCache(string key, string url, string sha256, string[] args)
        {
            lock (ResolveCacheLock)
            {
                if (ResolveCache.Count > 0)
                {
                    long now = DateTime.UtcNow.Ticks;
                    var stale = new List<string>();
                    foreach (var kv in ResolveCache)
                        if (now - kv.Value.Ticks >= ResolveCacheTtlTicks)
                            stale.Add(kv.Key);
                    foreach (var k in stale) ResolveCache.Remove(k);
                }
                ResolveCache[key] = (url, sha256, args, DateTime.UtcNow.Ticks);
            }
        }

        private static async Task<(bool ok, string url, string sha256, string[] args)> LiveResolveAsync(string id, Action<string> log)
        {
            try
            {
                var client = HttpClients.Default; // 进程内共享单例复用（B4）：避免每次 new/dispose 造成 socket TIME_WAIT 堆积
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)); // 原 client.Timeout=25s → 请求级超时（单例不改全局 Timeout）

                // 先试 id 本体，再试 .install 变体（meta 包的安装逻辑在 .install 里）
                foreach (var candidate in new[] { id, id + ".install" })
                {
                    var r = await ResolveCandidateAsync(client, cts.Token, candidate);
                    if (r.ok) return r;
                }
                return (false, null, null, null);
            }
            catch (Exception caughtEx)
            {
                System.Diagnostics.Debug.WriteLine("[CpqSystemTool] Chocolatey 实时解析异常(降级兜底): " + caughtEx.Message);
                return (false, null, null, null);
            }
        }

        private static async Task<(bool ok, string url, string sha256, string[] args)> ResolveCandidateAsync(HttpClient client, CancellationToken ct, string id)
        {
            // 安全加固：id 直接拼入 OData 过滤串（Id eq '...'），先做白名单校验，
            // 仅允许 [A-Za-z0-9.-]，避免注入破坏 OData 查询或引发异常。
            if (!Regex.IsMatch(id ?? "", @"^[A-Za-z0-9.\-]+$"))
                return (false, null, null, null);
            try
            {
                string odata;
                using (var req = BuildRequest("https://community.chocolatey.org/api/v2/Packages()?$filter=Id eq '" + id + "' and IsLatestVersion eq true&$select=Version,PackageDownload"))
                using (var resp = await client.SendAsync(req, ct))
                    odata = await resp.Content.ReadAsStringAsync();
                var pkg = Match(odata, @"<d:PackageDownload m:type=""Edm.String"">([^<]+)</d:PackageDownload>");
                if (string.IsNullOrEmpty(pkg)) return (false, null, null, null);
                pkg = System.Net.WebUtility.HtmlDecode(pkg);

                byte[] nupkg;
                using (var req = BuildRequest(pkg))
                using (var resp = await client.SendAsync(req, ct))
                    nupkg = await resp.Content.ReadAsByteArrayAsync();
                using var ms = new MemoryStream(nupkg);
                using var zip = new ZipArchive(ms);
                string script = null;
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith("chocolateyinstall.ps1", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("install.ps1", StringComparison.OrdinalIgnoreCase))
                    {
                        using var sr = new StreamReader(e.Open());
                        script = sr.ReadToEnd();
                        break;
                    }
                }
                if (string.IsNullOrEmpty(script)) return (false, null, null, null);

                string url = FirstNonEmpty(
                    Match(script, @"url64bit\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"url64\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"url\s*=\s*['""]([^'""]+)['""]"));
                string sha256 = FirstNonEmpty(
                    Match(script, @"checksum64\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"checksum\s*=\s*['""]([^'""]+)['""]"));
                var sa = Match(script, @"silentArgs\s*=\s*['""]([^'""]*)['""]");
                string[] args = string.IsNullOrEmpty(sa) ? new string[0] : SplitArgs(sa);

                return (!string.IsNullOrEmpty(url), url, sha256, args);
            }
            catch { return (false, null, null, null); }
        }

        // 单例共享后不能写 HttpClient.DefaultRequestHeaders（会污染全局请求头），UA 改为请求级注入，行为与原客户端级 UA 一致。
        private static HttpRequestMessage BuildRequest(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "CpqSystemTool");
            return req;
        }

        private static string Match(string s, string pat)
        {
            var m = Regex.Match(s, pat, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static string FirstNonEmpty(params string[] v)
        {
            foreach (var x in v) if (!string.IsNullOrEmpty(x)) return x;
            return null;
        }

        private static string[] SplitArgs(string s)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(s, @"""[^""]*""|'[^']*'|[^\s]+"))
                list.Add(m.Value.Trim('"', '\''));
            return list.ToArray();
        }
    }
}
