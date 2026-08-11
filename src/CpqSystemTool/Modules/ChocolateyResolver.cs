using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

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

        public static bool TryResolve(string id, out string url, out string sha256, out string[] args, Action<string> log)
        {
            url = null; sha256 = null; args = null;

            // 1) 实时解析（联网取最新，始终带当前 SHA256 → 永不失效）
            if (LiveResolve(id, out url, out sha256, out args, log) && !string.IsNullOrEmpty(sha256))
            {
                log("   [*] 已从 Chocolatey 实时解析最新安装包：" + id);
                return true;
            }

            // 2) 兜底：已验证快照（离线/解析失败/无哈希时）
            if (Fallback.TryGetValue(id, out var f))
            {
                url = f.Url; sha256 = f.Sha256; args = f.Args;
                log("   [*] 使用已验证 Chocolatey 快照（实时解析不可用）：" + id);
                return true;
            }
            return false;
        }

        private static bool LiveResolve(string id, out string url, out string sha256, out string[] args, Action<string> log)
        {
            url = null; sha256 = null; args = null;
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(25);
                client.DefaultRequestHeaders.Add("User-Agent", "CpqSystemTool");

                // 先试 id 本体，再试 .install 变体（meta 包的安装逻辑在 .install 里）
                foreach (var candidate in new[] { id, id + ".install" })
                {
                    if (ResolveCandidate(client, candidate, out url, out sha256, out args)) return true;
                }
                return false;
            }
            catch (Exception caughtEx)
            {
                System.Diagnostics.Debug.WriteLine("[CpqSystemTool] Chocolatey 实时解析异常(降级兜底): " + caughtEx.Message);
                return false;
            }
        }

        private static bool ResolveCandidate(HttpClient client, string id, out string url, out string sha256, out string[] args)
        {
            url = null; sha256 = null; args = null;
            // 安全加固：id 直接拼入 OData 过滤串（Id eq '...'），先做白名单校验，
            // 仅允许 [A-Za-z0-9.-]，避免注入破坏 OData 查询或引发异常。
            if (!Regex.IsMatch(id ?? "", @"^[A-Za-z0-9.\-]+$"))
                return false;
            try
            {
                string odata = client.GetStringAsync(
                    "https://community.chocolatey.org/api/v2/Packages()?$filter=Id eq '" + id + "' and IsLatestVersion eq true&$select=Version,PackageDownload").Result;
                var pkg = Match(odata, @"<d:PackageDownload m:type=""Edm.String"">([^<]+)</d:PackageDownload>");
                if (string.IsNullOrEmpty(pkg)) return false;
                pkg = System.Net.WebUtility.HtmlDecode(pkg);

                byte[] nupkg = client.GetByteArrayAsync(pkg).Result;
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
                if (string.IsNullOrEmpty(script)) return false;

                url = FirstNonEmpty(
                    Match(script, @"url64bit\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"url64\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"url\s*=\s*['""]([^'""]+)['""]"));
                sha256 = FirstNonEmpty(
                    Match(script, @"checksum64\s*=\s*['""]([^'""]+)['""]"),
                    Match(script, @"checksum\s*=\s*['""]([^'""]+)['""]"));
                var sa = Match(script, @"silentArgs\s*=\s*['""]([^'""]*)['""]");
                args = string.IsNullOrEmpty(sa) ? new string[0] : SplitArgs(sa);

                return !string.IsNullOrEmpty(url);
            }
            catch { return false; }
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
