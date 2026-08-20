using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Diagnostics;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 软件一键安装：移植自 software_installer.py。URL 下载安装包 → （zip 则解压首个安装器）→ 静默安装。
    /// 微软商店应用走 winget 分支。安装状态通过扫描卸载注册表判定。
    /// </summary>
    internal class SoftwareDef
    {
        public string Id;
        public string Name;
        public string Desc;
        /// <summary>软件分类（结构化，用于搜索页筛选/展示分类胶囊）。取值见 SoftwareCategories；为空兼容旧数据，展示时按「其他」处理。</summary>
        public string Category;
        public string Risk = "low";
        public string DownloadUrl;
        public string[] InstallArgs = new string[0];
        /// <summary>非空时标记该包走 Chocolatey 运行时解析（主流国际包）：安装时实时拉取官方 URL+SHA256+参数，离线则回退已验证快照。见 ChocolateyResolver。</summary>
        public string ChocolateyId;
        public string StoreId;                 // 非空则走微软商店(winget)分支
        public int DownloadTimeout = 320;
        public int InstallTimeout = 300;
        public string UninstallKeywords;
        public string[] AltKeywords = new string[0]; // 英文/别名备选，用于注册表匹配
        public string[] KnownExePaths = new string[0]; // 已知exe路径（文件存在性降级检测）
        /// <summary>精确注册表路径（备用精确注册表路径）</summary>
        public string RegKey;
        /// <summary>备用精确注册表路径（如 WOW6432Node 或 HKCU 分支）</summary>
        public string RegKey2;
        /// <summary>可选：期望的 SHA256（十六进制，大小写不限）。下载后校验，不匹配则拒绝安装（防篡改/损坏）。为空则不校验。</summary>
        public string Sha256;
        /// <summary>可选：下载时附加的 HTTP Referer 头。部分厂商（如 bilibili）直链需特定 Referer 否则 403。为空则不发送。</summary>
        public string Referer;
        /// <summary>非空时标记该包走『官方下载页链接解析』运行时机制：安装时 HttpClient 抓取 PageUrl（官方下载页或 latest 指针），提取/跟随出真实 .exe 直链；解析失败自动回退 DownloadUrl（私有镜像）。</summary>
        public string PageUrl;

        /// <summary>
        /// 自定义安装目录的开关前缀：
        /// "/D=" = NSIS 安装器（路径不能含空格，/D= 必须是最后一个参数）
        /// "/DIR=" = Inno Setup 安装器（支持带空格路径）
        /// null = 不支持自定义路径（MSI/商店/其他安装器），使用默认安装目录
        /// 由 Builder 构造函数根据 installArgs 自动推断（含 /S → NSIS；含 /VERYSILENT → Inno）。
        /// </summary>
        public string InstallDirSwitch;

        /// <summary>CheckInstalled() 找到的实际卸载子键完整路径（含根），供 GetInstalledVersion() 复用避免重复搜索。</summary>
        internal string _cachedUninstallKeyPath;

        /// <summary>便携版标记：解压即完成安装，无需运行安装程序（如 aria2 无参启动等于空跑）。</summary>
        internal bool IsPortable = false;

        private string _tempDir;

        /// <summary>卸载信息所在的三个注册表根路径（统一使用 HKEY_ 前缀格式，确保 Registry.GetValue 兼容）</summary>
        private static readonly string[] UNINSTALL_ROOTS = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        /// <summary>Uninstall 根一次性枚举的缓存条目：子键完整路径 → (DisplayName, DisplayVersion)。</summary>
        private sealed class UninstallEntry
        {
            public string DisplayName;
            public string DisplayVersion;
        }

        // 常用软件页会对几十个软件×每个关键词各做一次 Uninstall 根全量枚举（GetSubKeyNames+逐个 OpenSubKey），
        // 成本高且彼此重复。此处做进程内一次性枚举缓存（TTL 5 秒）：页面连续渲染所有软件共享同一次枚举；
        // 安装/卸载后最多 5 秒内自动失效重取，避免陈旧误判。全部读取走同一把锁，保证后台/UI 线程安全。
        private static readonly object _uninstallCacheLock = new object();
        private static Dictionary<string, UninstallEntry> _uninstallCache;
        private static DateTime _uninstallCacheStamp;
        private const int UNINSTALL_CACHE_TTL_MS = 5000;

        // 私有构造：仅允许通过 Builder 创建，消除 11 参数长列表（数据簇味道）。
        private SoftwareDef() { }

        // 卸载子进程等待超时（毫秒）：15 分钟；超时强制 Kill，避免 UI 永久挂起
        private const int UNINSTALL_TIMEOUT_MS = 900000;

        /// <summary>
        /// 流式构造器：把"检测相关"的 5 个参数（UninstallKeywords/AltKeywords/KnownExePaths/RegKey/RegKey2）
        /// 收拢为具名方法，避免长参数列表与位置错配，调用点可读性更好。
        /// </summary>
        public class Builder
        {
            private readonly SoftwareDef _d = new SoftwareDef();

            public Builder(string id, string name, string desc, string url, params string[] installArgs)
            {
                _d.Id = id;
                _d.Name = name;
                _d.Desc = desc;
                _d.DownloadUrl = url;
                _d.InstallArgs = installArgs ?? new string[0];
                _d.UninstallKeywords = name; // 默认与主名一致，可被 UninstallKeywords() 覆盖
                // 自动推断安装器类型（用于自定义安装目录注入）：
                // - 含 /S（NSIS 官方静默参数）→ NSIS 安装器，支持 /D=
                // - 含 /VERYSILENT（Inno 官方静默参数）→ Inno Setup，支持 /DIR=
                // - 已显式 /D= 的（如 Notepad3）也识别为 NSIS
                // - 其他（MSI /quiet /silent /ai 等）→ 不支持自定义路径
                if (_d.InstallArgs != null)
                {
                    foreach (var a in _d.InstallArgs)
                    {
                        if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) { _d.InstallDirSwitch = "/D="; break; }
                        if (string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase)) { _d.InstallDirSwitch = "/D="; break; }
                        if (string.Equals(a, "/VERYSILENT", StringComparison.OrdinalIgnoreCase)) { _d.InstallDirSwitch = "/DIR="; break; }
                    }
                }
            }

            public Builder Risk(string risk) { _d.Risk = risk; return this; }
            public Builder Category(string category) { _d.Category = category; return this; }
            public Builder StoreId(string storeId) { _d.StoreId = storeId; return this; }
            public Builder ChocolateyId(string id) { _d.ChocolateyId = id; return this; }
            public Builder UninstallKeywords(string keyword) { _d.UninstallKeywords = keyword; return this; }
            public Builder AltKeywords(params string[] keywords) { _d.AltKeywords = keywords ?? new string[0]; return this; }
            public Builder KnownExePaths(params string[] paths) { _d.KnownExePaths = paths ?? new string[0]; return this; }
            public Builder RegKey(string key) { _d.RegKey = key; return this; }
            public Builder RegKey2(string key) { _d.RegKey2 = key; return this; }
            public Builder Sha256(string sha256) { _d.Sha256 = sha256; return this; }
            public Builder PageResolver(string pageUrl) { _d.PageUrl = pageUrl; return this; }
            public Builder Referer(string referer) { _d.Referer = referer; return this; }
            public Builder Portable() { _d.IsPortable = true; return this; }
            /// <summary>显式指定自定义安装目录开关前缀（/D= 或 /DIR=）。自定义软件条目可借此覆盖构造器的自动推断。</summary>
            public Builder InstallDirSwitch(string sw) { _d.InstallDirSwitch = sw; return this; }
            public SoftwareDef Build() => _d;
        }

        private string GetTempDir()
        {
            if (_tempDir == null) _tempDir = Path.Combine(Path.GetTempPath(), "swinst_" + Id + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            return _tempDir;
        }

        public bool Install(Action<string> log, string customDir = null)
        {
            if (!string.IsNullOrEmpty(StoreId)) return InstallFromStore(log);
            log("=== 安装 " + Name + " ===");

            // 运行时解析（Chocolatey 源）：主流包实时取官方 URL + SHA256 + 静默参数；离线回退快照
            string downloadUrl = DownloadUrl;
            string[] args = InstallArgs;
            string sha256 = Sha256;
            if (!string.IsNullOrEmpty(ChocolateyId))
            {
                if (ChocolateyResolver.TryResolve(ChocolateyId, out var rUrl, out var rSha, out var rArgs, log))
                {
                    downloadUrl = rUrl; args = rArgs; sha256 = rSha;
                }
                else
                {
                    log("   [!] Chocolatey 解析失败且无兜底，无法安装 " + Name);
                    return false;
                }
            }

            // 运行时解析（官方下载页链接）：国产包实时抓取官方页/配置提取最新直链；失败时回退 DownloadUrl（已是官方直链，不再依赖私有镜像）
            if (!string.IsNullOrEmpty(PageUrl))
            {
                string resolved = PageLinkResolver.Resolve(PageUrl, log);
                if (!string.IsNullOrEmpty(resolved)) { downloadUrl = resolved; log("   [*] 已从官方页解析直链: " + resolved); }
                else { log("   [!] 官方页解析失败，回退私有镜像 " + Name); }
            }

            // 自定义安装目录注入：优先使用构造/BuildBuilder 显式设置的开关（自定义条目可经 Builder.InstallDirSwitch 覆盖自动推断），
            // 为空再依据实际使用的静默参数推断安装器类型。内置条目字段值与推断一致，行为不变。
            string installDirSwitch = InstallDirSwitch;
            if (string.IsNullOrEmpty(installDirSwitch))
            {
                foreach (var a in args)
                {
                    if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) { installDirSwitch = "/D="; break; }
                    if (string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase)) { installDirSwitch = "/D="; break; }
                    if (string.Equals(a, "/VERYSILENT", StringComparison.OrdinalIgnoreCase)) { installDirSwitch = "/DIR="; break; }
                }
            }
            if (!string.IsNullOrEmpty(customDir) && !string.IsNullOrEmpty(installDirSwitch))
            {
                if (installDirSwitch == "/D=" && customDir.IndexOf(' ') >= 0)
                {
                    log("   [!] " + Name + " 使用 NSIS 安装器，/D= 参数不支持含空格的路径，请改用不含空格的目录（如 D:\\Softwares）");
                    return false;
                }
                var dirArg = installDirSwitch + customDir;
                var newArgs = new string[args.Length + 1];
                Array.Copy(args, newArgs, args.Length);
                newArgs[args.Length] = dirArg;  // /D= 必须是最后一个参数
                args = newArgs;
                log("   [*] 自定义安装目录: " + customDir);
            }
            else if (!string.IsNullOrEmpty(customDir))
            {
                log("   [*] " + Name + " 安装器不支持自定义目录，将使用默认路径安装");
            }

            string rawExt = System.IO.Path.GetExtension(downloadUrl)?.ToLowerInvariant();
            string ext = (rawExt == ".exe" || rawExt == ".msi" || rawExt == ".zip") ? rawExt : ".exe";
            string dest = Path.Combine(GetTempDir(), Id + "_setup" + ext);
            if (!Download(downloadUrl, dest, log, DownloadTimeout, sha256)) return false;
            if (!VerifyIntegrity(dest, log)) return false;   // 下载文件完整性/签名校验
            string runPath = dest;
            bool extracted = false;
            if (dest.ToLowerInvariant().EndsWith(".zip"))
            {
                string inst = ExtractFirstInstaller(dest, log);
                if (string.IsNullOrEmpty(inst)) { CleanupTemp(); return false; }
                runPath = inst; extracted = true;
                if (!VerifyIntegrity(runPath, log)) { CleanupTemp(); return false; }  // 解压出的安装器再校验一次
            }
            // 便携版（如 aria2）：解压即完成，无需运行安装程序（无参运行 exe 等于空跑）
            if (IsPortable)
            {
                string loc = extracted ? System.IO.Path.GetDirectoryName(runPath) : dest;
                log("   [OK] 便携版已就绪：" + loc + "（无需安装程序）");
                return true;  // 便携版保留在临时目录（清理会删除软件本体）
            }
            bool ok = RunInstaller(runPath, args, log, InstallTimeout);
            CleanupTemp();  // 安装结束（无论成败）清理临时下载/解压文件，避免堆积
            return ok;
        }

        /// <summary>
        /// 下载后完整性/可信性校验（针对可执行安装包 .exe/.msi）：
        /// 1) 数字签名（Authenticode）验证 —— 无需预设哈希，即可发现文件被篡改/损坏；
        ///    - 有效 → 通过；未签名 → 仅警告（保持改动前放行行为）；无效 → 默认警告放行，StrictSignatureCheck=true 时拒绝。
        /// 2) 若配置了 SoftwareDef.Sha256，哈希校验已在 Download() 内完成（此处不重复）。
        /// 非 .exe/.msi 文件（如 .zip 容器）不做签名校验，直接放行。
        /// </summary>
        private bool VerifyIntegrity(string filePath, Action<string> log)
        {
            string lower = filePath.ToLowerInvariant();
            if (!lower.EndsWith(".exe") && !lower.EndsWith(".msi")) return true;

            log("   [*] 正在校验安装包完整性 / 数字签名…");
            var sig = AuthenticodeVerifier.Verify(filePath, log);
            // 官方可信源判定：官方下载页解析的包（PageUrl）或配置为 https 官方直链的包，均视为来源可信（不误导"可能被篡改"）
            bool officialSource = !string.IsNullOrEmpty(PageUrl)
                || (!string.IsNullOrEmpty(DownloadUrl) && DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            switch (sig)
            {
                case AuthenticodeVerifier.SigStatus.Valid:
                    log("   [✓] 数字签名验证通过：" + Path.GetFileName(filePath) + "（来源可信）");
                    return true;
                case AuthenticodeVerifier.SigStatus.NotSigned:
                    // 官方钉死直链 / 官方下载页解析的包：未签名属正常（多数国产官方安装器不签 Authenticode），
                    // 因来源本身可信，不应提示"可能被篡改"，避免误导用户。
                    if (officialSource)
                        log("   [i] 未签名（" + Path.GetFileName(filePath) + "）：来源为官方直链，可放心安装（官方安装器通常未做 Authenticode 签名，仅无法校验文件完整性）");
                    else
                        log("   [i] 未签名（" + Path.GetFileName(filePath) + "）：无法校验完整性，请确认 " + Name + " 的下载来源可靠");
                    return true; // 保持改动前行为（无校验即放行），仅给出提示
                case AuthenticodeVerifier.SigStatus.Invalid:
                    if (SoftwareInstall.StrictSignatureCheck)
                    {
                        log("   [✗] 数字签名验证未通过，文件可能被篡改，已拒绝安装 " + Name + "（可在软件安装设置中关闭“严格签名校验”以放行）");
                        return false;
                    }
                    // 官方源包出现"签名无效"多为证书链/时间戳问题，并非一定被篡改：明确区分、
                    // 不再笼统说"可能被篡改"，避免对官方直链包造成不必要的恐慌。
                    if (officialSource)
                        log("   [!] 签名校验未通过（" + Path.GetFileName(filePath) + "）：但来源为官方直链，通常可忽略；如多次出现建议重新下载");
                    else
                        log("   [!] 数字签名验证未通过（" + Path.GetFileName(filePath) + "）：签名无效或文件损坏，可能被篡改，强烈建议核查来源（非严格模式仍继续安装）");
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>安装/解压完成后清理临时下载目录（便携版不调用，避免删除软件本体）。</summary>
        private void CleanupTemp()
        {
            try { if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        private bool InstallFromStore(Action<string> log)
        {
            log("=== 安装 " + Name + "（微软商店）===");
            log("   [*] 尝试通过 winget（微软商店源）静默安装…");
            int rc = Exec.RunCmd(new[] { "winget", "install", "--id", StoreId, "--source", "msstore",
                "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements" }, log);
            if (rc == 0) { log("   [OK] 已通过微软商店安装完成。"); return true; }
            log("   [*] 微软商店源失败，回退 winget 社区源…");
            rc = Exec.RunCmd(new[] { "winget", "install", "--id", StoreId,
                "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements" }, log);
            if (rc == 0) { log("   [OK] 已通过 winget 社区源安装完成。"); return true; }
            log("   [!] winget 安装失败，请手动从微软商店搜索 " + Name + " 安装。");
            return false;
        }

        public bool Uninstall(Action<string> log)
        {
            if (!string.IsNullOrEmpty(StoreId)) return UninstallFromStore(log);
            log("=== 卸载 " + Name + " ===");

            string u = null;

            // 1) 优先复用 CheckInstalled 阶段缓存的精确卸载项路径（最可靠）
            if (!string.IsNullOrEmpty(_cachedUninstallKeyPath))
            {
                log("   [*] 复用检测阶段缓存的卸载项: " + _cachedUninstallKeyPath);
                u = ReadRegString(_cachedUninstallKeyPath, "QuietUninstallString");
                if (string.IsNullOrEmpty(u)) u = ReadRegString(_cachedUninstallKeyPath, "UninstallString");
            }

            // 2) 没有缓存或缓存项读不到卸载命令时，按关键词重新搜索
            string sub = null;
            if (u == null)
            {
                sub = FindUninstaller(UninstallKeywords);
                if (sub == null)
                {
                    foreach (var alt in AltKeywords)
                    {
                        sub = FindUninstaller(alt);
                        if (sub != null) break;
                    }
                }
                if (sub == null) { log("   [SKIP] 未在注册表找到安装记录"); return false; }
                u = GetUninstallString(sub);
            }

            if (string.IsNullOrEmpty(u)) { log("   [!] 未找到卸载命令"); return false; }
            log("   [*] 卸载命令: " + u);

            int rc = RunUninstallCommand(u, log);
            if (rc == 0) log("   [OK] 卸载命令执行完成。");
            else log("   [!] 卸载命令返回非零退出码: " + rc + "（可能已弹出卸载向导，请检查）");
            return true;
        }

        /// <summary>
        /// 执行卸载命令。
        /// 关键修正（2026-08-05）：
        ///   1) 不再经 cmd /c + CreateNoWindow 隐藏窗口——这样 GUI 卸载器若不支持静默参数会被彻底藏死、静默失败；
        ///      改为直接启动卸载程序本体（UseShellExecute=true），静默卸载器本就不弹窗，GUI 卸载器则可见可交互。
        ///   2) 优先以"提权(runas)"启动。非管理员进程直接跑 Program Files 里的卸载程序会因访问拒绝而静默失败，
        ///     提权后才能真正写入 Program Files / HKLM。UAC 被拒或本就不需要提权时，回退普通权限再试一次。
        /// </summary>
        private int RunUninstallCommand(string u, Action<string> log)
        {
            ParseUninstallCommand(u, out string exe, out string args);
            // MSI 卸载若未带 /q（静默）参数，补上 /qn，避免弹交互向导
            if (string.Equals(Path.GetFileName(exe), "msiexec.exe", StringComparison.OrdinalIgnoreCase) && !args.ToLowerInvariant().Contains("/q"))
                args = args + " /qn";
            log("   [*] 解析卸载程序: " + exe + (string.IsNullOrEmpty(args) ? "" : "  参数: " + args));

            int TryLaunch(bool elevate)
            {
                try
                {
                    var psi = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute = true,
                        Verb = elevate ? "runas" : null
                        // 不设置 CreateNoWindow：静默卸载器本就不弹窗；GUI 卸载器可见可手动完成
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (p == null) { log("  [!] 无法启动卸载程序" + (elevate ? "(提权)" : "")); return -1; }
                        if (!p.WaitForExit(UNINSTALL_TIMEOUT_MS)) { try { p.Kill(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); } return -2; }
                        return p.ExitCode;
                    }
                }
                catch (Exception ex)
                {
                    log("  [!] 启动卸载程序失败" + (elevate ? "(提权被拒绝?)" : "") + ": " + ex.Message);
                    return -1;
                }
            }

            bool isAdmin = IsRunningElevated();
            log("   [*] 当前进程" + (isAdmin ? "已以管理员身份运行" : "未以管理员运行（将尝试提权启动卸载程序）"));
            int rc = TryLaunch(true);            // 先尝试提权
            if (rc == -1 && !isAdmin)
            {
                log("   [*] 提权启动失败，回退普通权限启动一次...");
                rc = TryLaunch(false);
            }
            return rc;
        }

        /// <summary>把卸载字符串解析为 (exe路径, 参数)。兼容："C:\a b\Uninstall.exe" /S、C:\x\Uninstall.exe /S、MsiExec.exe /X{GUID}。</summary>
        private static void ParseUninstallCommand(string u, out string exe, out string args)
        {
            u = (u ?? "").Trim();
            if (u.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) || u.StartsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                exe = "msiexec.exe";
                args = u.Substring(11).TrimStart();
                return;
            }
            if (u.StartsWith("\""))
            {
                int close = u.IndexOf('"', 1);
                if (close > 0) { exe = u.Substring(1, close - 1); args = u.Substring(close + 1).Trim(); return; }
            }
            int sp = u.IndexOf(' ');
            if (sp > 0)
            {
                // 未加引号路径可能含空格（如 C:\Program Files\...）：扫描所有空格位置，
                // 取「实际存在文件的最长前缀」作为 exe，避免截断成 "C:\Program"。
                string best = u.Substring(0, sp);
                int idx = sp;
                while (idx > 0 && (idx = u.IndexOf(' ', idx + 1)) > 0)
                {
                    string cand = u.Substring(0, idx);
                    if (File.Exists(cand)) best = cand;
                }
                exe = best;
                args = u.Substring(best.Length).Trim();
            }
            else { exe = u; args = ""; }
        }

        private static bool IsRunningElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        private bool UninstallFromStore(Action<string> log)
        {
            log("=== 卸载 " + Name + "（微软商店）===");
            int rc = Exec.RunCmd(new[] { "winget", "uninstall", "--id", StoreId, "--source", "msstore", "--exact", "--silent" }, log);
            if (rc == 0) { log("   [OK] 已卸载。"); return true; }
            rc = Exec.RunCmd(new[] { "winget", "uninstall", "--id", StoreId, "--exact", "--silent" }, log);
            if (rc == 0) { log("   [OK] 已卸载。"); return true; }
            log("   [!] winget 卸载失败，请手动在设置中卸载。");
            return false;
        }

        /// <summary>
        /// 检测软件是否已安装（Win11EasyConfig 同策略：优先精确注册表路径）。
        /// 优先级：RegKey精确路径 → RegKey2 → DisplayName关键词搜索 → AltKeywords → 文件存在性
        /// 找到后会缓存实际子键路径供 GetInstalledVersion() 复用。
        /// </summary>
        public bool CheckInstalled()
        {
            _cachedUninstallKeyPath = null; // 每次重新检测时清空缓存

            // 策略0（最高优先级）：精确注册表路径直接读 DisplayVersion（Win11EasyConfig 核心策略）
            if (!string.IsNullOrEmpty(RegKey) && ReadRegString(RegKey, "DisplayVersion") != null)
            { _cachedUninstallKeyPath = RegKey; return true; }
            if (!string.IsNullOrEmpty(RegKey2) && ReadRegString(RegKey2, "DisplayVersion") != null)
            { _cachedUninstallKeyPath = RegKey2; return true; }

            // 策略1：注册表 DisplayName 匹配（主关键词）
            var found = FindUninstallerFull(UninstallKeywords);
            if (found != null)
            { _cachedUninstallKeyPath = found; return true; }
            // 策略2：注册表 DisplayName 匹配（备选英文/别名关键词）
            foreach (var alt in AltKeywords)
            {
                found = FindUninstallerFull(alt);
                if (found != null)
                { _cachedUninstallKeyPath = found; return true; }
            }
            // 策略3：文件存在性降级检测
            if (CheckFileExists()) return true;
            return false;
        }

        /// <summary>读取注册表字符串值（对齐 Win11EasyConfig：直接 Registry.GetValue(完整路径)）</summary>
        private static string ReadRegString(string keyPath, string valueName)
        {
            try
            {
                object val = Microsoft.Win32.Registry.GetValue(keyPath, valueName, null);
                return val as string;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return null; }
        }

        /// <summary>
        /// 获取已安装版本号。
        /// 优先级：CheckInstalled 缓存的精确路径 → RegKey → RegKey2 → 关键词搜索（FindVersion）。
        /// 返回版本号字符串；空串表示未获取到。
        /// </summary>
        public string GetInstalledVersion()
        {
            // 策略0（最高优）：复用 CheckInstalled 缓存的实际找到路径
            if (!string.IsNullOrEmpty(_cachedUninstallKeyPath))
            {
                var v = ReadVersionFromPath(_cachedUninstallKeyPath);
                if (!string.IsNullOrEmpty(v)) return v;
            }

            // 策略1：精确路径
            if (!string.IsNullOrEmpty(RegKey))
            {
                var v = ReadVersionFromPath(RegKey);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            if (!string.IsNullOrEmpty(RegKey2))
            {
                var v = ReadVersionFromPath(RegKey2);
                if (!string.IsNullOrEmpty(v)) return v;
            }

            // 策略2：关键词搜索回退（遍历三个注册表根，匹配 DisplayName 后读版本）
            return FindVersion(UninstallKeywords, AltKeywords);
        }

        /// <summary>从指定注册表路径读取版本号，多值回退</summary>
        private static string ReadVersionFromPath(string keyPath)
        {
            try
            {
                var v = ReadRegString(keyPath, "DisplayVersion");
                if (!string.IsNullOrEmpty(v)) return v;
                v = ReadRegString(keyPath, "Version");
                if (!string.IsNullOrEmpty(v)) return v;
                var major = ReadRegString(keyPath, "VersionMajor");
                var minor = ReadRegString(keyPath, "VersionMinor");
                if (!string.IsNullOrEmpty(major))
                    return !string.IsNullOrEmpty(minor) ? major + "." + minor : major;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return "";
        }

        /// <summary>
        /// 通过检查已知可执行文件路径来判断是否已安装（注册表检测失败时的降级方案）。
        /// 覆盖常见安装目录：Program Files / AppData/Local / 用户自定义。
        /// </summary>
        private bool CheckFileExists()
        {
            foreach (var exe in KnownExePaths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return true;
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            }
            return false;
        }

        // ---- 内部实现 ----
        // 采用流式下载：边下边落盘，避免把整个安装包（可能数百 MB）一次性读入内存造成 GC 压力；
        // 相比 GetByteArrayAsync + WriteAllBytes，对大文件更省内存、更快，且 SHA256 仍照常校验（不降低安全性）。
        private bool Download(string url, string dest, Action<string> log, int timeout, string sha256 = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                string expect = string.IsNullOrWhiteSpace(sha256) ? Sha256 : sha256;
                bool needHash = !string.IsNullOrWhiteSpace(expect);
                log("   [*] 正在下载安装包…");
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout))) // 原 client.Timeout → 请求级超时（单例不改全局 Timeout）
                {
                    var client = HttpClients.Default; // 进程内共享单例复用（B4）：避免每次 new/dispose 造成 socket TIME_WAIT 堆积
                    using var req = new HttpRequestMessage(HttpMethod.Get, url); // UA/Referer 改请求级注入（不能写共享单例的 DefaultRequestHeaders）
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    if (!string.IsNullOrEmpty(Referer))
                        req.Headers.Referrer = new Uri(Referer);
                    // ResponseHeadersRead：读完响应头即开始落盘，不等整包缓冲完（更快显现进度、更低峰值内存）
                    using (var resp = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).Result)
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            log("   [!] 下载失败: HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                            return false;
                        }
                        using (var inStream = resp.Content.ReadAsStreamAsync().Result)
                        using (var outStream = File.Create(dest))
                        {
                            inStream.CopyTo(outStream); // 默认 80KB 缓冲块，增量写入
                        }
                    }
                }
                // 完整性校验：若配置了期望 SHA256（字段或运行时解析覆盖），则必须匹配（防篡改/损坏），不匹配直接拒绝
                if (needHash)
                {
                    string actual;
                    using (var fs = File.OpenRead(dest))
                    using (var sha = new SHA256Managed())
                        actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(actual, expect.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(dest); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        log("   [!] 校验失败：下载文件 SHA256 不匹配（期望 " + expect.Trim() + "，实际 " + actual + "），已拒绝安装。");
                        return false;
                    }
                }
                log("   [OK] 下载完成（" + (new FileInfo(dest).Length / 1024) + " KB）" + (needHash ? "，校验通过" : ""));
                return true;
            }
            catch (Exception e)
            {
                log("   [!] 下载失败: " + e.Message);
                return false;
            }
        }

        private string ExtractFirstInstaller(string zip, Action<string> log)
        {
            string outDir = zip + "_ext";
            try
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                ZipFile.ExtractToDirectory(zip, outDir);
                var exe = Directory.GetFiles(outDir, "*.exe", SearchOption.AllDirectories);
                if (exe.Length > 0) return exe[0];
                var msi = Directory.GetFiles(outDir, "*.msi", SearchOption.AllDirectories);
                if (msi.Length > 0) return msi[0];
                log("   [!] 压缩包内未找到安装程序");
                return null;
            }
            catch (Exception e) { log("   [!] 解压失败: " + e.Message); return null; }
        }

        private bool RunInstaller(string path, string[] args, Action<string> log, int timeout)
        {
            if (args == null) args = new string[0];
            // 强制超时控制：避免挂起的安装程序永久冻结 UI。
            // 原实现把 timeout 传给 Exec.RunCmd 却未实际生效（Exec.RunCmd 使用固定 15 分钟硬上限，
            // 且不回显安装器输出）；这里直接用 Process 受 timeout 约束，超时则 Kill 并上报，
            // 同时把 stdout/stderr 实时 pipe 给 log。
            string QuoteArg(string s)
            {
                if (string.IsNullOrEmpty(s)) return "\"\"";
                bool needsQuote = s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('&') >= 0
                    || s.IndexOf('^') >= 0 || s.IndexOf('|') >= 0 || s.IndexOf('<') >= 0
                    || s.IndexOf('>') >= 0 || s.IndexOf('%') >= 0;
                return needsQuote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            }
            var psi = new ProcessStartInfo(path, string.Join(" ", System.Array.ConvertAll(args, QuoteArg)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) { log("   [!] 无法启动安装程序: " + path); return false; }
                p.OutputDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) log("   [ERR] " + e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                // 受控等待 + 心跳日志：每 10 秒输出一次进度，避免静默安装器长时间无反馈；
                // 总等待上限仍为 timeout，超时则 Kill 并上报（与原逻辑一致，不重试）。
                int waited = 0;
                const int pollMs = 1000;
                while (!p.WaitForExit(pollMs))
                {
                    waited += pollMs;
                    if (waited >= timeout)
                    {
                        try { p.Kill(); } catch { }
                        log("   [!] 安装超时（>" + (timeout / 1000) + " 秒），已强制终止。");
                        return false;
                    }
                    if (waited % 10000 == 0)
                        log("   [i] 安装进行中…（已 " + (waited / 1000) + " 秒）");
                }
                if (p.ExitCode == 0) { log("   [OK] 安装完成。"); return true; }
                log("   [!] 安装程序返回非零退出码: " + p.ExitCode);
                return false;
            }
        }

        private static RegistryKey OpenKey(string root)
        {
            RegistryHive hive;
            string sub;
            if (root.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)) { hive = RegistryHive.LocalMachine; sub = root.Substring(19); }
            else if (root.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)) { hive = RegistryHive.CurrentUser; sub = root.Substring(18); }
            else if (root.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)) { hive = RegistryHive.LocalMachine; sub = root.Substring(4); }
            else if (root.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)) { hive = RegistryHive.CurrentUser; sub = root.Substring(4); }
            else return null;
            try { using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default)) return baseKey.OpenSubKey(sub); }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return null; }
        }

        /// <summary>
        /// 一次性枚举 3 个 Uninstall 根下全部子键路径→(DisplayName, DisplayVersion)，结果做进程内缓存（TTL 5 秒）。
        /// 所有软件/关键词共享这一次枚举，替代原来每次调用重复的 GetSubKeyNames+逐个 OpenSubKey 全量遍历。
        /// 线程安全：整个方法在锁内执行，后台线程与 UI 线程均可安全调用；缓存字典发布后不再被修改。
        /// 失败时回退旧缓存（若有）；无旧缓存则返回本次已收集的部分结果（或空表），调用方自然回落为"未找到"。
        /// 注意：这里只缓存枚举层数据（键存在性/DisplayName/DisplayVersion）；精确值读取（版本号、卸载命令等）仍走直读，不经过本缓存。
        /// </summary>
        private static Dictionary<string, UninstallEntry> EnumerateUninstallCache()
        {
            var now = DateTime.UtcNow;
            lock (_uninstallCacheLock)
            {
                if (_uninstallCache != null
                    && (now - _uninstallCacheStamp).TotalMilliseconds < UNINSTALL_CACHE_TTL_MS)
                    return _uninstallCache;

                var fresh = new Dictionary<string, UninstallEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var root in UNINSTALL_ROOTS)
                    {
                        using (var key = OpenKey(root))
                        {
                            if (key == null) continue;
                            foreach (var sub in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var sk = key.OpenSubKey(sub))
                                    {
                                        if (sk == null) continue;
                                        fresh[root + "\\" + sub] = new UninstallEntry
                                        {
                                            DisplayName = sk.GetValue("DisplayName") as string,
                                            DisplayVersion = sk.GetValue("DisplayVersion") as string
                                        };
                                    }
                                }
                                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                            }
                        }
                    }
                    _uninstallCache = fresh;
                    _uninstallCacheStamp = now;
                }
                catch (Exception caughtEx)
                {
                    // 枚举失败：回退旧缓存保持健壮；无旧缓存则返回已收集的部分结果。时间戳不更新，下次调用会重试。
                    DebugLog.Ignore(caughtEx);
                    if (_uninstallCache != null) return _uninstallCache;
                    return fresh;
                }
                return _uninstallCache;
            }
        }

        private string FindUninstaller(string keyword)
        {
            var entries = EnumerateUninstallCache();
            foreach (var root in UNINSTALL_ROOTS)
            {
                var prefix = root + "\\";
                foreach (var kv in entries)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = kv.Value.DisplayName;
                    if (!string.IsNullOrEmpty(name) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Key.Substring(prefix.Length); // 仅子键名（不带根前缀），与原实现一致
                }
            }
            return null;
        }

        /// <summary>
        /// 查找卸载注册表项，返回**完整路径**（含 HKLM/HKCU 前缀），供版本号读取复用。
        /// 比 FindUninstaller 多返回根前缀信息。
        /// </summary>
        private string FindUninstallerFull(string keyword)
        {
            var entries = EnumerateUninstallCache();
            foreach (var root in UNINSTALL_ROOTS)
            {
                var prefix = root + "\\";
                foreach (var kv in entries)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = kv.Value.DisplayName;
                    if (!string.IsNullOrEmpty(name) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Key; // 返回完整路径
                }
            }
            return null;
        }

        internal static string FindVersion(string keyword, string[] altKeywords = null)
        {
            // REG_DWORD 值以 int 形式返回，用 as string 会得到 null；统一解析整数。
            int? ToVersionInt(object o)
            {
                if (o is int i) return i;
                if (o is long l) return (int)l;
                if (o is string s && int.TryParse(s, out int r)) return r;
                return null;
            }

            var entries = EnumerateUninstallCache();
            var allKeywords = new List<string> { keyword };
            if (altKeywords != null) allKeywords.AddRange(altKeywords);
            foreach (var root in UNINSTALL_ROOTS)
            {
                var prefix = root + "\\";
                foreach (var kv in entries)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = kv.Value.DisplayName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        foreach (var kw in allKeywords)
                        {
                            if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try
                                {
                                    // 版本号属于"安装后需精确读最新值"的数据，不经缓存，直接读被匹配键的实时值
                                    using (var sk = OpenKey(kv.Key))
                                    {
                                        if (sk == null) continue;
                                        // 按优先级尝试多个版本值名（参考 Win11EasyConfig 策略）
                                        var v = sk.GetValue("DisplayVersion") as string
                                            ?? sk.GetValue("Version") as string
                                            ?? sk.GetValue("VersionMajor") as string;
                                        if (!string.IsNullOrEmpty(v)) return v;
                                        // VersionMajor/VersionMinor 为 REG_DWORD，必须按整数读取后拼接
                                        var major = ToVersionInt(sk.GetValue("VersionMajor"));
                                        if (major.HasValue)
                                        {
                                            var minor = ToVersionInt(sk.GetValue("VersionMinor"));
                                            return minor.HasValue ? major.Value + "." + minor.Value : major.Value.ToString();
                                        }
                                    }
                                }
                                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                            }
                        }
                    }
                }
            }
            return "";
        }

        private string GetUninstallString(string sub)
        {
            var entries = EnumerateUninstallCache();
            foreach (var root in UNINSTALL_ROOTS)
            {
                var prefix = root + "\\";
                foreach (var kv in entries)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(kv.Key.Substring(prefix.Length), sub, StringComparison.OrdinalIgnoreCase)) continue;
                    // 卸载命令属于精确操作数据，不经缓存，直接读取实时值
                    var u = ReadRegString(kv.Key, "UninstallString");
                    if (!string.IsNullOrEmpty(u)) return u;
                }
            }
            return null;
        }
    }

    internal static class SoftwareInstall
    {
        /// <summary>
        /// 严格签名校验开关：默认 false（非严格 —— 数字签名无效的安装包仅警告并继续，保持改动前放行行为）。
        /// 设为 true 时，签名无效的 .exe/.msi 安装包将被拒绝（防篡改更强，但可能拦截个别使用非主流 CA 签名的合法安装包）。
        /// </summary>
        public static bool StrictSignatureCheck = false;

        /// <summary>兜底分类：软件未标注分类或自定义条目缺 category 字段时使用的默认分类。</summary>
        public const string DefaultCategory = "其他";

        /// <summary>软件分类固定集合（用于编辑框下拉与搜索页分类筛选）。顺序即下拉顺序；末尾为兜底分类。</summary>
        public static readonly string[] SoftwareCategories = new[]
        {
            "浏览器", "视频软件", "音乐", "图像", "通讯", "下载工具", "压缩",
            "云存储", "系统工具", "办公", "开发", "游戏", "虚拟机",
            "输入法", "远程控制", DefaultCategory
        };

        private static readonly List<SoftwareDef> SOFTWARE_LIST = new List<SoftwareDef>
        {
            // ---- 格式： SoftwareDef.Builder(id, name, desc, url, installArgs...).Risk().StoreId().AltKeywords().KnownExePaths().RegKey().RegKey2().Build() ----
            new SoftwareDef.Builder("winrar", "WinRAR", "压缩工具", "https://www.rarlab.com/rar/winrar-x64-723.exe", "/S").ChocolateyId("winrar")
                .Risk("low")
                .AltKeywords("WinRAR")
                .KnownExePaths(@"C:\Program Files\WinRAR\WinRAR.exe", @"C:\Program Files (x86)\WinRAR\WinRAR.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinRAR archiver")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\WinRAR archiver")
                .Category("压缩").Build(),
            new SoftwareDef.Builder("notepad3", "NotePad3", "文本编辑器", "https://github.com/rizonesoft/Notepad3/releases/download/RELEASE_7.26.602.1/Notepad3_7.26.602.1_x64_Setup.exe", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-").ChocolateyId("notepad3")
                .Risk("low")
                .AltKeywords("NotePad3", "Notepad3")
                .KnownExePaths(@"C:\Program Files\Notepad3\Notepad3.exe")
                .Category("系统工具").Build(),
            new SoftwareDef.Builder("xnview", "XnViewMP", "图片查看/转换", "https://www.xnview.com/download.php?file=XnViewMP-win-x64.exe", "/VERYSILENT", "/NORESTART").ChocolateyId("xnviewmp")
                .Risk("low")
                .AltKeywords("XnView", "XnViewMP")
                .KnownExePaths(@"C:\Program Files\XnViewMP\xnviewmp.exe")
                .Category("图像").Build(),
            new SoftwareDef.Builder("potplayer", "PotPlayer", "影音播放器", "https://t1.daumcdn.net/potplayer/PotPlayer/Version/Latest/PotPlayerSetup64.exe", "/S").ChocolateyId("potplayer")
                .Risk("low")
                .AltKeywords("PotPlayer", "Daum PotPlayer")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Daum\\PotPlayer\\PotPlayerMini.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Kakao\\PotPlayer\\PotPlayerMini.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Daum PotPlayer")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Daum PotPlayer")
                .Category("视频软件").Build(),
            new SoftwareDef.Builder("7zip", "7-Zip", "压缩工具", "https://github.com/ip7z/7zip/releases/download/26.02/7z2602-x64.exe", "/S").ChocolateyId("7zip")
                .Risk("low")
                .AltKeywords("7-Zip", "7zip", "7-Zip FM")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\7-Zip\\7zFM.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\7-Zip\\7z.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip")
                .Category("压缩").Build(),
            new SoftwareDef.Builder("everything", "Everything", "文件搜索", "https://www.voidtools.com/Everything-1.4.1.1032.x64-Setup.exe", "/S", "/NORESTART").ChocolateyId("everything")
                .Risk("low")
                .AltKeywords("Everything")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Everything\\Everything.exe", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Everything\\Everything.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Everything")
                .RegKey2(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Everything")
                .Category("系统工具").Build(),
            new SoftwareDef.Builder("vcredist", "Visual C++ 运行库合集", "系统运行库(abbodi1406 AIO)", "https://github.com/abbodi1406/vcredist/releases/download/v0.105.0/VisualCppRedist_AIO_x86_x64.exe", "/ai", "/gm2")
                .Risk("mid")
                .Category("系统工具").Build(),
            new SoftwareDef.Builder("edge", "Microsoft Edge", "浏览器", "https://c2rsetup.officeapps.live.com/c2r/downloadEdge.aspx?platform=Default&source=EdgeStablePage&Channel=Stable&language=zh-cn&brand=M100", "/silent", "/install")
                .Risk("low")
                .AltKeywords("Microsoft Edge")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Microsoft\\Edge\\Application\\msedge.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge")
                .Category("浏览器").Build(),
            new SoftwareDef.Builder("webview2", "WebView2 Runtime", "Web 运行时", "https://go.microsoft.com/fwlink/p/?LinkId=2124703", "/silent", "/install")
                .Risk("low")
                .AltKeywords("WebView2", "Microsoft Edge WebView2")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge WebView2 Runtime")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge WebView2 Runtime")
                .Category("系统工具").Build(),
            new SoftwareDef.Builder("aria2", "aria2", "命令行下载工具", "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip").Portable().ChocolateyId("aria2").Category("下载工具").Build(),
            new SoftwareDef.Builder("weixin", "微信", "即时通讯", "https://dldir1.qq.com/weixin/Windows/WeChatSetup.exe", "/S")
                .Risk("low")
                .AltKeywords("WeChat", "微信", "Tencent WeChat", "微信电脑版")
                .KnownExePaths(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Tencent\\WeChat\\WeChat.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Tencent\\WeChat\\WeChat.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Programs\\Tencent\\WeChat\\WeChat.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Tencent\\WeChat\\WeChat.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Tencent\\WeChat\\WeChat.exe")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("qq", "QQ", "即时通讯", "https://qqdl.gtimg.cn/qqfile/QQNT/9.9.33/release/a0ce07ad/QQ_9.9.33_260730_x64_01.exe", "/S")
                .PageResolver("https://cdn-go.cn/qq-web/im.qq.com_new/latest/rainbow/windowsConfig.js")
                .Risk("low")
                .AltKeywords("QQ", "QQNT", "Tencent QQ", "腾讯QQ")
                .KnownExePaths(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Tencent\\QQ\\QQ.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Tencent\\QQ\\QQ.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Tencent\\QQNT\\QQ.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Programs\\Tencent\\QQ\\QQ.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Tencent\\QQNT\\QQ.exe")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("steam", "Steam", "游戏平台", "https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe", "/S")
                .Risk("low")
                .AltKeywords("Steam", "Valve Steam")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Steam\\steam.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Steam\\steam.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam")
                .RegKey2(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam")
                .Category("游戏").Build(),
            new SoftwareDef.Builder("virtualbox", "VirtualBox", "虚拟机", "https://download.virtualbox.org/virtualbox/7.2.14/VirtualBox-7.2.14-174565-Win.exe", "-s", "-l", "-msiparams", "REBOOT=ReallySuppress", "ALLUSERS=1").ChocolateyId("virtualbox")
                .Risk("low")
                .AltKeywords("VirtualBox", "Oracle VM VirtualBox")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Oracle\\VirtualBox\\VirtualBox.exe")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Oracle VM VirtualBox")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Oracle VM VirtualBox")
                .Category("虚拟机").Build(),
            new SoftwareDef.Builder("bandizip", "Bandizip", "压缩工具", "https://www.bandisoft.com/bandizip/dl.php?product=bandizip&lang=zh-cn&type=normal", "/S")
                .Risk("low")
                .AltKeywords("Bandizip")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Bandizip\\Bandizip.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Bandizip\\Bandizip.exe")
                .Category("压缩").Build(),
            new SoftwareDef.Builder("tortoisegit", "TortoiseGit", "Git 客户端", "https://download.tortoisegit.org/tgit/2.19.1.0/TortoiseGit-2.19.1.0-64bit.msi", "/quiet", "/qn", "/norestart", "REBOOT=ReallySuppress").ChocolateyId("tortoisegit")
                .Risk("low")
                .AltKeywords("TortoiseGit")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\TortoiseGit\\bin\\TortoiseGitProc.exe")
                .Category("开发").Build(),
            new SoftwareDef.Builder("wps", "WPS", "办公套件", "https://official-package.wpscdn.cn/wps/download/WPS_Setup_28043.exe", "/S")
                .Risk("low")
                .AltKeywords("WPS", "Kingsoft WPS", "WPS Office")
                .KnownExePaths(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\WPS Office\\wpsoffice.exe")
                .Category("办公").Build(),
            new SoftwareDef.Builder("baidupan", "百度网盘", "云存储", "https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_7.45.2.1.exe", "/S")
                .Risk("low")
                .AltKeywords("BaiduNetdisk", "百度网盘")
                .Category("云存储").Build(),
            new SoftwareDef.Builder("thunder", "迅雷", "下载工具", "https://down.sandai.net/thunder11/XunLeiWebSetup12.4.10.3940xl11.exe", "/S")
                .Risk("low")
                .AltKeywords("Xunlei", "Thunder")
                .Category("下载工具").Build(),
            new SoftwareDef.Builder("xshell", "Xshell", "SSH 客户端", "https://cdn.netsarang.net/180f2808/Xshell-8.0.0102p.exe").PageResolver("https://cdn.netsarang.net/v8/Xshell-latest-p").Category("开发").Build(),
            new SoftwareDef.Builder("quark", "夸克网盘", "云存储", "https://umcdn.quark.cn/download/37211/quarkclouddrivepc/pckk@product_guanwang/QuarkCloudDrivePC_V7.0.5.766_pc_pf30001_%28zh-cn%29_release_%28Build3102129-1000-x64%29.exe").Category("云存储").Build(),
            new SoftwareDef.Builder("translucente", "TranslucentTB", "任务栏透明化", "")
                .Risk("low")
                .StoreId("9PGJ3W9GK6L7")
                .Category("系统工具").Build(),
            // ---- 微软商店 / 社区源应用（原版常用软件扩充）----
            new SoftwareDef.Builder("devhome", "Dev Home", "开发人员主页", "")
                .Risk("low")
                .StoreId("Microsoft.DevHome")
                .Category("开发").Build(),
            new SoftwareDef.Builder("people", "微软人脉", "Microsoft People", "")
                .Risk("low")
                .StoreId("Microsoft.People")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("teams", "Microsoft Teams", "团队协作", "")
                .Risk("low")
                .StoreId("Microsoft.Teams")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("skype", "Skype", "语音视频通话", "")
                .Risk("low")
                .StoreId("Skype.Skype")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("outlook", "Microsoft Outlook", "邮件日历", "")
                .Risk("low")
                .StoreId("Microsoft.OutlookForWindows")
                .Category("办公").Build(),
            new SoftwareDef.Builder("go", "Go 语言运行环境", "Golang 开发环境", "")
                .Risk("low")
                .StoreId("GoLang.Go")
                .Category("开发").Build(),
            new SoftwareDef.Builder("vmware", "VMware Workstation", "虚拟机", "")
                .Risk("mid")
                .StoreId("VMware.VMwareWorkstationPro")
                .Category("虚拟机").Build(),
            new SoftwareDef.Builder("xftp", "Xftp", "SFTP 客户端", "")
                .Risk("low")
                .StoreId("NetSarang.Xftp")
                .Category("开发").Build(),
            // ---- 从 Win11EasyConfig 补充的软件条目 ----
            new SoftwareDef.Builder("tim", "腾讯TIM", "QQ简化版办公沟通", "https://qqdl.gtimg.cn/qqfile/qq/TIM/TIM3.5.1/TIM3.5.1.22172.exe")
                .Risk("low")
                .AltKeywords("TIM", "Tencent TIM")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\TIM")
                .Category("通讯").Build(),
            new SoftwareDef.Builder("qqmusic", "QQ音乐", "音乐播放器", "https://c.y.qq.com/cgi-bin/file_redirect.fcg?bid=dldir&file=ecosfile%2Fmusic_clntupate%2Fpc%2Fother%2FQQMusic_Setup_2241.exe&sign=1-42f47326a332ba52627a5104e0b52130a573d3c67ff58ba8376b516619c39614-6a561755").PageResolver("https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback")
                .Risk("low")
                .AltKeywords("QQMusic", "Tencent QQMusic")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\QQMusic")
                .Category("音乐").Build(),
            new SoftwareDef.Builder("aliyunpan", "阿里云盘", "云存储", "https://cdn.aliyundrive.net/downloads/apps/desktop/aDrive-6.9.3.exe")
                .Risk("low")
                .AltKeywords("aDrive", "阿里云盘", "Aliyun Drive")
                .RegKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\300f80e0-781e-56db-ae9d-9d0190486ca9")
                .Category("云存储").Build(),
            new SoftwareDef.Builder("weiyun", "腾讯微云", "云存储", "https://dldir1.qq.com/weiyun/electron-update/release/5.2.1611/WeiyunApp-Setup-X64-5.2.1611.exe")
                .Risk("low")
                .AltKeywords("Weiyun", "微云")
                .RegKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\c5cf1501-68df-5d7e-9349-7223666c05d9")
                .Category("云存储").Build(),
            new SoftwareDef.Builder("123pan", "123云盘", "云存储", "https://app.123pan.com/pc-pro/windows/321/123pan_3.2.1.exe")
                .Risk("low")
                .AltKeywords("123pan", "123云盘")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\123pan")
                .Category("云存储").Build(),
            new SoftwareDef.Builder("onedrive", "微软OneDrive", "云存储", "https://go.microsoft.com/fwlink/p/?LinkId=248256")
                .Risk("low")
                .AltKeywords("OneDrive", "Microsoft OneDrive")
                .RegKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe")
                .RegKey2(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe")
                .Category("云存储").Build(),
            new SoftwareDef.Builder("git", "Git For Windows", "版本控制", "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.3/Git-2.55.0.3-64-bit.exe", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL", "/SP-", "/LOG").ChocolateyId("git")
                .Risk("low")
                .AltKeywords("Git", "Git for Windows")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Git_is1")
                .Category("开发").Build(),
            new SoftwareDef.Builder("unlocker", "IObit Unlocker", "文件解锁工具", "https://cdn.iobit.com/dl/unlocker-setup.exe", "/S")
                .Risk("low")
                .AltKeywords("Unlocker", "IObit Unlocker")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\IObit Unlocker_is1")
                .Category("系统工具").Build(),
            new SoftwareDef.Builder("sogoupinyin", "搜狗拼音输入法", "中文输入法", "https://ime.gtimg.com/pc/pinyin_guanwang_16.7.exe")
                .Risk("low")
                .AltKeywords("Sogou Input", "搜狗输入法")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Sogou Input")
                .Category("输入法").Build(),
            new SoftwareDef.Builder("baidupinyin", "百度拼音输入法", "中文输入法", "https://imeres.baidu.com/imeres/imeres/ime-res/guanwang/dl/online/BaiduPinyinSetup_6.1.13.13.exe", "/S")
                .Risk("low")
                .AltKeywords("BaiduPinyin", "百度输入法")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\BaiduPinyin")
                .Category("输入法").Build(),
            new SoftwareDef.Builder("wymusic", "网易云音乐", "音乐播放器", "https://d8.music.126.net/dmusic2/NeteaseCloudMusic_Music_official_3.1.28.205001_64.exe")
                .Risk("low")
                .AltKeywords("NeteaseCloudMusic", "网易云音乐")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\网易云音乐")
                .Category("音乐").Build(),
            new SoftwareDef.Builder("kgmusic", "酷狗音乐", "音乐播放器", "https://pcpackagebssdlbigapk.cosama.cn/202608041801/dc6a73202616a028ea54d0f9420b0f01/release_20141_x64.exe")
                .Risk("low")
                .AltKeywords("Kugou", "酷狗音乐")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\酷狗音乐")
                .Category("音乐").Build(),
            new SoftwareDef.Builder("kwmusic", "酷我音乐", "音乐播放器", "https://pkgdown.kuwo.cn/6ba3138119bf3ff448c17ea08c6b6203/6a71b8a1/mbox/kwmusic_web_1.exe")
                .Risk("low")
                .AltKeywords("KwMusic", "酷我音乐")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\KwMusic7")
                .Category("音乐").Build(),
            // 已更新到 1.18.0 官方直链（bili_win-install.exe?v=1.18.0-3，由 Playwright 抓自 app.bilibili.com）。该直链实测带任意 bilibili 域 Referer 均返回 2xx 二进制，附 Referer 以稳妥；SHA256 留空走 Authenticode 校验。
            new SoftwareDef.Builder("bilibili", "哔哩哔哩", "视频平台", "https://dl.hdslb.com/mobile/fixed/bili_win/bili_win-install.exe?v=1.18.0-3")
                .Risk("low")
                .Referer("https://www.bilibili.com/")
                .AltKeywords("BiliBili", "哔哩哔哩")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BiliBili")
                .Category("视频软件").Build(),
            new SoftwareDef.Builder("txvideo", "腾讯视频", "视频平台", "https://dldir1.qq.com/qqtv/TencentVideo11.176.3261.0.exe")
                .Risk("low")
                .AltKeywords("TencentVideo", "腾讯视频", "qqlive")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\qqlive")
                .Category("视频软件").Build(),
            new SoftwareDef.Builder("iqiyi", "爱奇艺", "视频平台", "https://cdndata.video.iqiyi.com/cdn/pca/20260804/14.7.5.10167/channel/1785814312514/IQIYIsetup_w01f.exe")
                .Risk("low")
                .AltKeywords("iQiyi", "爱奇艺", "PPStream")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PPStream")
                .Category("视频软件").Build(),
            new SoftwareDef.Builder("douyin", "抖音", "短视频平台", "https://www.douyin.com/download/pc/obj/douyin-pc-web/douyin-pc-client/7044145585217083655/releases/432763571/8.3.0/win32-ia32/douyin-downloader-v8.3.0-win32-ia32-douyin.exe")
                .Risk("low")
                .AltKeywords("Douyin", "抖音", "TikTok")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\douyin")
                .Category("视频软件").Build(),
            new SoftwareDef.Builder("raylink", "RayLink远程", "远程控制", "https://download.raylink.live/web2.0/RayLink/RayLink_v8.1.8.8.exe")
                .Risk("low")
                .AltKeywords("RayLink")
                .RegKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\RayLink")
                .Category("远程控制").Build(),
        };

        // ---- 合并列表（内置 + 自定义）：自定义同 ID 覆盖内置；缓存随 SoftwareDefPersistence.Version 失效 ----
        private static List<SoftwareDef> _effList;
        private static Dictionary<string, SoftwareDef> _effMap;
        private static int _effVersion = -1;

        private static void EnsureEffective()
        {
            if (_effList != null && _effVersion == SoftwareDefPersistence.Version) return;
            var custom = SoftwareDefPersistence.Load();
            // 自定义 ID 与内置 ID 仅大小写不同时仍视为覆盖（与 BuildItems/其它匹配处一致用 OrdinalIgnoreCase）
            var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in custom) if (e != null) overridden.Add(e.id);
            var list = new List<SoftwareDef>();
            foreach (var s in SOFTWARE_LIST) if (!overridden.Contains(s.Id)) list.Add(s);
            foreach (var e in custom) if (e != null) list.Add(e.ToSoftwareDef());
            _effList = list;
            _effMap = new Dictionary<string, SoftwareDef>();
            foreach (var s in list) _effMap[s.Id] = s;
            _effVersion = SoftwareDefPersistence.Version;
        }

        /// <summary>合并后的有效软件列表（内置 + 自定义，自定义同 ID 覆盖内置）。供常用软件页/安装/卸载/清理遍历使用。</summary>
        public static List<SoftwareDef> GetEffectiveList() { EnsureEffective(); return _effList; }

        private static Dictionary<string, SoftwareDef> EffectiveMap() { EnsureEffective(); return _effMap; }

        /// <summary>判断指定 ID 是否属于内置默认列表（用于管理对话框区分「内置(覆盖)」与「增补」）。</summary>
        public static bool IsBuiltInId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var s in SOFTWARE_LIST)
                if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public class SoftwareInfo
        {
            public string Id;
            public string Name;
            public string Desc;
            public string Category;
            public string Risk;
            public bool Installed;
            public string Version = "";
        }

        public static List<SoftwareInfo> GetAllStatus()
        {
            var list = new List<SoftwareInfo>();
            foreach (var sw in GetEffectiveList())
            {
                var info = new SoftwareInfo { Id = sw.Id, Name = sw.Name, Desc = sw.Desc, Category = sw.Category, Risk = sw.Risk, Installed = sw.CheckInstalled() };
                if (info.Installed) info.Version = sw.GetInstalledVersion();
                list.Add(info);
            }
            return list;
        }

        /// <summary>重新检测单个软件的安装状态（供卸载/安装后原地刷新行状态，避免重建整页丢失日志）。</summary>
        public static SoftwareInfo GetStatus(string id)
        {
            var map = EffectiveMap();
            if (!map.ContainsKey(id)) return null;
            var sw = map[id];
            var info = new SoftwareInfo { Id = sw.Id, Name = sw.Name, Desc = sw.Desc, Category = sw.Category, Risk = sw.Risk, Installed = sw.CheckInstalled() };
            if (info.Installed) info.Version = sw.GetInstalledVersion();
            return info;
        }

        public static bool Install(string id, Action<string> log, string customDir = null)
        {
            var map = EffectiveMap();
            if (!map.ContainsKey(id)) { log("  [!] 未知软件 ID: " + id); return false; }
            return map[id].Install(log, customDir);
        }

        public static bool Uninstall(string id, Action<string> log)
        {
            var map = EffectiveMap();
            if (!map.ContainsKey(id)) { log("  [!] 未知软件 ID: " + id); return false; }
            return map[id].Uninstall(log);
        }

        public static void CleanupDownloads(Action<string> log)
        {
            int count = 0;
            foreach (var sw in GetEffectiveList())
            {
                // 通过反射无关方式：仅清理已知临时目录前缀
                try
                {
                    var tmp = Path.GetTempPath();
                    if (Directory.Exists(tmp))
                    {
                        foreach (var d in Directory.GetDirectories(tmp, "swinst_" + sw.Id + "_*"))
                        {
                            try { Directory.Delete(d, true); count++; } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                        }
                    }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            }
            if (log != null) log("   [OK] 已清理 " + count + " 个临时目录。");
        }
    }

    /// <summary>
    /// Authenticode 数字签名校验（封装 WinVerifyTrust）。无需预设哈希即可判断 PE 文件（.exe/.msi/.dll）
    /// 的签名是否有效，用于在无 TLS 的 HTTP 镜像场景下检测下载文件被篡改或传输损坏。
    /// 不检查证书吊销（离线环境避免误拒），仅验证签名链与文件完整性。
    /// </summary>
    internal static class AuthenticodeVerifier
    {
        public enum SigStatus { Valid, Invalid, NotSigned }

        private static readonly Guid WintrustActionGenericVerifyV2 =
            new Guid("{00AAC56B-CD44-11d3-8A2E-009027105932}");

        private const int WTD_UI_NONE = 2;
        private const int WTD_REVOKE_NONE = 0;
        private const int WTD_CHOICE_FILE = 1;
        private const int WTD_STATEACTION_VERIFY = 1;
        private const int WTD_STATEACTION_CLOSE = 2;
        // 仅用本地证书存储验证链，不联网拉取缺失的中间证书/CTL：消除在线校验的网络等待与离线环境挂起，
        // 不降低安全性（仍完整校验签名链与文件完整性，仅不去远端补全证书）。
        private const int WTD_CACHE_ONLY_URL_RETRIEVAL = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public int dwUIChoice;
            public int fdwRevocationChecks;
            public int dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags;
            public int dwUIContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WinTrustData pWVTData);

        /// <summary>
        /// 校验文件 Authenticode 签名。
        /// 返回：Valid=签名有效；NotSigned=未签名（或无法判定签名，保守放行）；Invalid=签名校验失败（可能篡改）。
        /// </summary>
        public static SigStatus Verify(string filePath, Action<string> log)
        {
            IntPtr pFile = IntPtr.Zero;
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo)),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };
            var data = new WinTrustData
            {
                cbStruct = Marshal.SizeOf(typeof(WinTrustData)),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                dwStateAction = WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                dwUIContext = 0,
            };
            try
            {
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, pFile, false);
                data.pFile = pFile;

                int hr = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);
                if (hr == 0) return SigStatus.Valid;
                uint u = (uint)hr;
                // TRUST_E_NOSIGNATURE=0x800B0100 / CRYPT_E_NOT_FOUND=0x80092009 / CRYPT_E_FILE_ERROR=0x80092003 → 视为未签名
                if (u == 0x800B0100 || u == 0x80092009 || u == 0x80092003) return SigStatus.NotSigned;
                if (log != null) log("   [!] Authenticode 校验返回 0x" + hr.ToString("X8"));
                return SigStatus.Invalid;
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                return SigStatus.NotSigned; // 校验过程异常时保守放行（仅记入调试日志），不改变原有安装行为
            }
            finally
            {
                if (pFile != IntPtr.Zero)
                {
                    data.dwStateAction = WTD_STATEACTION_CLOSE;
                    try { WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data); } catch { }
                    Marshal.DestroyStructure(pFile, typeof(WinTrustFileInfo)); // 释放 StructureToPtr 为 pcwszFilePath 分配的非托管字符串
                    Marshal.FreeHGlobal(pFile);
                }
            }
        }
    }

    /// <summary>
    /// 官方下载页链接解析器：用于国产/小众软件包。安装时 HttpClient 抓取 PageUrl（官方下载页或 latest 指针），
    /// 提取/跟随出真实 .exe 直链；解析失败返回 null，由调用方回退私有镜像 DownloadUrl。绝不抛异常。
    /// </summary>
    internal static class PageLinkResolver
    {
        public static string Resolve(string pageUrl, Action<string> log)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30))) // 原 client.Timeout=30s → 请求级超时（单例不改全局 Timeout）
                {
                    var client = HttpClients.Default; // 单例默认 handler 即 AllowAutoRedirect=true，与原显式配置行为一致；UA/Referer 改请求级注入
                    using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    if (pageUrl.IndexOf("y.qq.com/download", StringComparison.OrdinalIgnoreCase) >= 0)
                        req.Headers.Referrer = new Uri("https://y.qq.com/download/download.html");
                    using (var resp = client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).Result)
                    {
                        string contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                        // 文件 / latest 指针直接重定向到文件（如 Xshell）：返回跟随重定向后的最终文件 URL
                        if (contentType.IndexOf("octet-stream", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            contentType.IndexOf("x-msdownload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            contentType.IndexOf("x-ms-dos-executable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            contentType.IndexOf("x-msdos-program", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            contentType.IndexOf("exe", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var finalUri = (resp.RequestMessage != null && resp.RequestMessage.RequestUri != null) ? resp.RequestMessage.RequestUri.ToString() : pageUrl;
                            return finalUri;
                        }

                        // 读正文（HTML 或 JSONP 配置等）
                        string body = resp.Content.ReadAsStringAsync().Result;

                        // QQ音乐官方配置（download.js JSONP）：提取 Windows PC 条目 Flink1（服务端签发签名，长期有效，随版本刷新）
                        if (body.IndexOf("\"Flink1\"", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try
                            {
                                string json = body;
                                int lp = json.IndexOf('('); int rp = json.LastIndexOf(')');
                                if (lp >= 0 && rp > lp) json = json.Substring(lp + 1, rp - lp - 1);
                                var winRe = new System.Text.RegularExpressions.Regex("\"Ftitle\"\\s*:\\s*\"Windows[^\"]*\"[^{}]*?\"Flink1\"\\s*:\\s*\"(?<u>[^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                var wm = winRe.Match(json);
                                if (wm.Success) { string u = wm.Groups["u"].Value; log("   [*] QQ音乐已从官方配置解析 Windows PC 直链: " + u); return u; }
                                var fr = new System.Text.RegularExpressions.Regex("\"Flink1\"\\s*:\\s*\"(?<u>[^\"]+)\"");
                                var fm = fr.Match(json);
                                if (fm.Success) { log("   [*] QQ音乐未定位 Windows PC 条目，采用首个 Flink1"); return fm.Groups["u"].Value; }
                            }
                            catch (Exception ex) { log("   [!] QQ音乐配置解析异常: " + ex.Message); }
                            log("   [!] QQ音乐配置未找到 Flink1，将回退官方直链"); return null;
                        }

                        // QQ 官方配置（windowsConfig.js）：提取 QQNT x64 直链（版本跟随，长期有效）
                        if (body.IndexOf("ntDownloadX64Url", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try
                            {
                                var qqRe = new System.Text.RegularExpressions.Regex("\"ntDownloadX64Url\"\\s*:\\s*\"(?<u>[^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                var qm = qqRe.Match(body);
                                if (qm.Success) { string u = qm.Groups["u"].Value; log("   [*] QQ 已从官方配置解析 x64 直链: " + u); return u; }
                            }
                            catch (Exception ex) { log("   [!] QQ 配置解析异常: " + ex.Message); }
                            log("   [!] QQ 配置未找到 ntDownloadX64Url，将回退官方直链"); return null;
                        }

                        // 其它 HTML 下载页：正则提取首个 .exe 直链（Xshell/QQ）
                        if (contentType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bool isQqPage = pageUrl.IndexOf("im.qq.com", StringComparison.OrdinalIgnoreCase) >= 0;
                            var re = new System.Text.RegularExpressions.Regex(@"https?://[^\s""'<>]+\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var matches = re.Matches(body);
                            string fallback = null;
                            string qqX64 = null;
                            foreach (System.Text.RegularExpressions.Match m in matches)
                            {
                                string u = m.Value;
                                if (u.IndexOf("xshell", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return u;
                                }
                                // QQ 官方页：优先 QQNT x64 安装包（排除 x86/arm64/旧 PCQQ9.7.25）
                                if (isQqPage && u.IndexOf("QQNT", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    u.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    u.IndexOf("arm64", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    qqX64 = u;
                                    continue;
                                }
                                if (fallback == null) fallback = u;
                            }
                            if (isQqPage)
                            {
                                if (qqX64 != null) { log("   [*] QQ 已从官方页解析 x64 直链: " + qqX64); return qqX64; }
                                log("   [!] QQ 官方页未找到 QQNT x64 直链，将回退官方直链");
                                return null;
                            }
                            if (fallback != null) return fallback;
                            log("   [!] 官方页未找到 .exe 直链，将回退私有镜像");
                            return null;
                        }
                        else
                        {
                            // 其它内容类型（如纯文本 latest 指针且未重定向）：尝试按正文 URL 解析
                            log("   [!] 官方页内容类型未知 (" + contentType + ")，将回退私有镜像");
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log("   [!] 官方页链接解析失败，将回退私有镜像: " + e.Message);
                return null;
            }
        }
    }
}
