using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// Office 快速安装 / 强力卸载（Click-to-Run）。
    /// 对应 ZyperWin++ 的「Office 快速安装」与「C2R 强力卸载」。
    /// 实现：生成 ODT config.xml → 确保 setup.exe（官方 CDN 下载，失败给出手动指引）→ 执行。
    /// </summary>
    internal static class OfficeInstall
    {
        public static readonly string[] Editions =
        {
            "Microsoft 365 (Office365) — 订阅制 / 云端协作",
            "Office 2024 专业增强版 (零售) — 最新 / 永久授权",
            "Office 2024 LTSC 专业增强版 (批量) — 长期支持 / 企业级",
            "Office 家庭版 (零售) 2024 — 家用 / 永久授权",
            "Office 家庭商务版 (零售) 2024 — 含Outlook / 永久授权",
            "Office 2021 专业增强版 (零售) — 主流稳定 / 永久授权",
            "Office 2021 LTSC 专业增强版 (批量) — 长期支持 / 企业级",
            "Office 家庭版 (零售) 2021 — 家用 / 永久授权",
            "Office 家庭商务版 (零售) 2021 — 家用办公 / 永久授权",
            "Office 2019 专业增强版 (零售) — 经典兼容 / 永久授权",
            "Office 2019 LTSC 专业增强版 (批量) — 长期支持 / 老硬件",
            "Office 家庭版 (零售) 2019 — 考二级/家用 / 永久授权",
            "Office 家庭商务版 (零售) 2019 — 家用办公 / 永久授权",
            "Office 2016 专业增强版 (零售) — 考二级练习 / 永久授权",
            "Office 家庭版 (零售) 2016 — 考二级/家用 / 永久授权",
            "Office 家庭商务版 (零售) 2016 — 家用办公 / 永久授权"
        };

        // 每个版本对应的 Product ID 与 Channel。
        // v1.20：改为 public，供 OfficeDeployControl 的版本下拉框复用 —— 单一事实来源，
        // 避免「老 Office 安装路径」和「ODT 组件安装路径」两处各维护一套 Product/Channel 而走样。
        // 三个数组下标必须一一对应 Editions，改动时务必同步。
        public static readonly string[] ProductIds =
        {
            "O365ProPlusRetail", "ProPlus2024Retail", "ProPlus2024Volume", "Home2024Retail", "HomeBusiness2024Retail", "ProPlus2021Retail", "ProPlus2021Volume", "HomeStudent2021Retail",
            "HomeBusiness2021Retail", "ProPlus2019Retail", "ProPlus2019Volume", "HomeStudentRetail", "HomeBusinessRetail", "ProPlusRetail", "HomeStudentRetail", "HomeBusinessRetail"
        };
        public static readonly string[] Channels =
        {
            "Current", "Current", "PerpetualVL2024", "Current", "Current", "PerpetualVL2021", "PerpetualVL2021", "PerpetualVL2021",
            "PerpetualVL2021", "Current", "PerpetualVL2019", "Current", "Current", "PerpetualVL2016", "PerpetualVL2016", "PerpetualVL2016"
        };

        public static void Install(int editionIndex, Action<string> log)
        {
            if (editionIndex < 0 || editionIndex >= ProductIds.Length) { log("  [!] 无效的版本选择"); return; }
            string arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            string xml = BuildConfig(ProductIds[editionIndex], Channels[editionIndex], arch, false);
            string dir = Path.Combine(Path.GetTempPath(), "ZyperOffice");
            try { Directory.CreateDirectory(dir); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            string xmlPath = Path.Combine(dir, "config.xml");
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);
            log("配置已生成：" + xmlPath);

            string setup = EnsureSetupExe(dir, log);
            if (string.IsNullOrEmpty(setup)) return;

            log("开始安装 Office（需联网，可能耗时数分钟，请耐心等待）...");
            // ODT setup.exe 无 /quiet 顶层开关（微软仅支持 /download /configure /customize /help），
            // 误加会被引擎视为未知参数而打印 usage 帮助并直接退出（假成功）。静默由 config 的
            // <Display Level> 控制：此处安装用 Level="Full" 显示原生进度，无需 /quiet。
            // workingDirectory=setup.exe 所在目录：固定 ODT 子进程 CWD，避免其继承父进程 CWD（双击 exe=桌面）
            // 后在桌面建日志/数据文件夹。
            Exec.RunCmd(new[] { setup, "/configure", xmlPath }, log, workingDirectory: Path.GetDirectoryName(setup));
            log("  [完成] 安装结束，请查看上方输出确认结果（安装失败多为网络/版本密钥问题）");
        }

        /// <summary>
        /// 全量卸载 C2R Office。返回是否真正执行了卸载命令（供调用方据此报成功，避免
        /// 「拿不到 setup.exe 提前 return 却仍报已全量卸载」的误导——此前 ExecuteFullUninstall
        /// 无条件报成功，用户在 ODT setup.exe 缺失/下载失败时仍看到「✅ 已全量卸载」）。
        /// </summary>
        public static bool Uninstall(Action<string> log)
        {
            string arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            string xml = BuildConfig("", "", arch, true);
            string dir = Path.Combine(Path.GetTempPath(), "ZyperOffice");
            try { Directory.CreateDirectory(dir); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            string xmlPath = Path.Combine(dir, "uninstall.xml");
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);

            string setup = EnsureSetupExe(dir, log);
            if (string.IsNullOrEmpty(setup))
            {
                log("  [!] 未取得 ODT setup.exe，全量卸载未执行（Office 未被移除，请勿据此判定已卸载）");
                return false;
            }

            log("开始强力卸载 Office（C2R）...");
            // 必须接收卸载命令的退出码：CleanLeftovers 会递归强删 Office 目录，
            // 若卸载本身失败却继续删目录，会留下装不回也卸不掉的半残环境，
            // 且可能误删 Office 目录下的用户模板/加载项等数据（防止误删用户数据）。
            // ODT setup.exe 无 /quiet 开关，静默由 config 的 <Display Level="None"> 控制（见 BuildConfig remove 分支）。
            // workingDirectory=setup.exe 所在目录：固定 ODT 子进程 CWD，避免其继承父进程 CWD（双击 exe=桌面）
            // 后在桌面建日志/数据文件夹（本次「卸载 office 组件时桌面冒出两个无名文件夹」的根因修复）。
            int rc = Exec.RunCmd(new[] { setup, "/configure", xmlPath }, log, workingDirectory: Path.GetDirectoryName(setup));
            if (rc != 0)
            {
                log("  [!] Office 卸载命令返回非零退出码 " + rc + "，已中止残留清理（未删除任何 Office 目录，也未清除注册表，请先解决上述错误再重试）");
                return false;
            }
            CleanLeftovers(log);
            // Remove All=TRUE 会停 ClickToRunSvc 服务、卸 C2R 引擎，但服务定义与 ClickToRun 键
            // 可能残留（ODT 全卸已知不完全现象）。卸载成功（rc==0）后才兜底清除，同款安全闸门。
            CleanRegistry(log);
            log("  [注册表] C2R 台账（ProductReleaseIds / ExcludedApps / OSPPReady）已随引擎键一并移除");
            log("  [完成] 卸载结束（含残留目录与注册表清理）");
            return true;
        }

        /// <summary>确保 setup.exe 存在：本地有就用，否则从官方 CDN 下载。
        /// 【P2-12】⚠ 内部 GetAwaiter().GetResult() 同步阻塞下载（最长 ~100 秒）：仅限后台线程调用（现调用点在 Office 部署页 Task.Run 内）；UI 线程调用会卡界面。</summary>
        private static string EnsureSetupExe(string dir, Action<string> log)
        {
            string setup = Path.Combine(dir, "setup.exe");
            if (File.Exists(setup) && new FileInfo(setup).Length > 100000) return setup;

            // 优先复用「组件部署」已成功下载的 ODT setup.exe（OdtSetup.Ensure 动态解析微软下载中心，比下方写死 CDN 可靠）。
            // 此前全量卸载只走下方写死 CDN，该直链现返回 400 → 拿不到 setup.exe → Remove All 没真执行，
            // 却仍报「已全量卸载」。改走 OdtSetup.Ensure：命中 odt_cache 缓存直接返回，缺失则动态下载（同源、可用）。
            string viaOdt = OdtSetup.Ensure(log);
            if (!string.IsNullOrEmpty(viaOdt) && File.Exists(viaOdt) && new FileInfo(viaOdt).Length > 100000)
                return viaOdt;

            log("下载 Office 部署工具 (ODT) setup.exe ...");
            // 最后回退：官方零售通道直链（已可能失效：返回 400 等）。正常应被上面 OdtSetup.Ensure 覆盖到。
            string url = "https://officecdn.microsoft.com/pr/ws01/Office/Setup.exe";
            bool downloaded = false, setupOk = false;
            try
            {
                // 统一走 Downloader（阻塞式，等价原 WebClient.DownloadFile；失败原因经 log 输出）
                downloaded = Downloader.DownloadAsync(url, setup, log,
                    maxAttempts: 1,
                    timeoutMs: 100000,      // 等价 WebClient 默认 100 秒超时
                    readTimeoutMs: 60000,   // 60s 读空闲超时，防慢连接永久挂起
                    userAgent: "Mozilla/5.0").GetAwaiter().GetResult();
                // 安全加固：下载后校验文件存在且大小合理（非空），避免后续对损坏/截断的 setup.exe 静默执行
                if (downloaded)
                {
                    try { setupOk = File.Exists(setup) && new FileInfo(setup).Length > 100000; }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); setupOk = false; }
                }
            }
            catch (Exception ex)
            {
                log("  [!] 下载失败: " + ex.Message);
            }
            if (setupOk) return setup;
            if (downloaded) log("  [!] 下载的 setup.exe 无效");
            log("  [提示] 请手动下载 Office 部署工具：https://www.microsoft.com/en-us/download/details.aspx?id=49117");
            log("        将 setup.exe 放到：" + dir);
            return "";
        }

        private static string BuildConfig(string pid, string channel, string arch, bool remove)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<Configuration>");
            if (remove)
            {
                sb.AppendLine("  <Remove All=\"TRUE\">");
                sb.AppendLine("    <Product ID=\"All\">");
                sb.AppendLine("      <Language ID=\"All\" />");
                sb.AppendLine("    </Product>");
                sb.AppendLine("  </Remove>");
                sb.AppendLine("  <Display Level=\"None\" AcceptEULA=\"TRUE\" />");
            }
            else
            {
                sb.AppendLine("  <Add OfficeClientEdition=\"" + arch + "\" Channel=\"" + System.Security.SecurityElement.Escape(channel) + "\">");
                sb.AppendLine("    <Product ID=\"" + System.Security.SecurityElement.Escape(pid) + "\">");
                sb.AppendLine("      <Language ID=\"zh-CN\" />");
                sb.AppendLine("      <Language ID=\"en-US\" />");
                sb.AppendLine("    </Product>");
                sb.AppendLine("  </Add>");
                sb.AppendLine("  <Display Level=\"Full\" AcceptEULA=\"TRUE\" />");
            }
            sb.AppendLine("  <Property Name=\"AUTOACTIVATE\" Value=\"0\" />");
            sb.AppendLine("</Configuration>");
            return sb.ToString();
        }

        private static void CleanLeftovers(Action<string> log)
        {
            string[] dirs =
            {
                @"%ProgramFiles%\Microsoft Office",
                @"%ProgramFiles(x86)%\Microsoft Office",
                @"%ProgramData%\Microsoft\Office",
                @"%CommonProgramFiles%\Microsoft Shared\Office"
            };
            foreach (var d in dirs)
            {
                string p = Exec.ExpandEnv(d);
                if (Directory.Exists(p))
                    Exec.RunPowerShell("Remove-Item -Path " + Exec.QuotePS(p) + " -Recurse -Force -EA 0", log);
            }
        }

        /// <summary>
        /// 全卸后兜底清除 C2R 相关注册表残留（Remove All=TRUE 已知不完全现象）。
        /// 仅在卸载成功（rc==0）后由 Uninstall 调用——失败时绝不碰注册表，保持安全闸门一致。
        /// 会递归删除 ClickToRun 引擎键及其 Configuration 子树（含 ProductReleaseIds / *.ExcludedApps / *.OSPPReady 等 C2R 台账），
        /// 全量卸载后台账整体被移除；部分卸载场景不会走到这里（rc!=0 提前中止）。
        /// </summary>
        private static void CleanRegistry(Action<string> log)
        {
            // ClickToRunSvc 服务定义：Remove All 停服务后服务键常残留
            // ClickToRun 引擎键：C2R 引擎卸载后的壳
            // WOW6432Node 变体：32/64 位 Office 共存时的镜像键
            string[] regKeys =
            {
                @"SYSTEM\CurrentControlSet\Services\ClickToRunSvc",
                @"SOFTWARE\Microsoft\Office\ClickToRun",
                @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun",
                @"SOFTWARE\Classes\ClickToRunSvc",
                @"SOFTWARE\Microsoft\Office\16.0\ClickToRun",
                @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun",
            };
            foreach (var keyPath in regKeys)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (k == null) continue;
                        // 单参版本：外层已 k==null 继续 + try/catch 兜底，无需 preserveReadOnly 变体
                        Registry.LocalMachine.DeleteSubKeyTree(keyPath);
                        log("  [注册表] 已清除残留键: " + keyPath);
                    }
                }
                catch (Exception ex)
                {
                    // 单个键删除失败不影响整体（可能已被 ODT 自身清掉），仅记录不中断
                    log("  [!] 注册表键删除失败（可能已不存在或被占用）: " + keyPath + " — " + ex.Message);
                }
            }
        }
    }
}
