using System;
using System.IO;
using System.Text;

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
            "Office 2021 专业增强版 (零售) — 主流稳定 / 永久授权",
            "Office 2021 LTSC 专业增强版 (批量) — 长期支持 / 企业级",
            "Office 2019 专业增强版 (零售) — 经典兼容 / 永久授权",
            "Office 2019 LTSC 专业增强版 (批量) — 长期支持 / 老硬件"
        };

        // 每个版本对应的 Product ID 与 Channel
        private static readonly string[] Pids =
        {
            "O365ProPlusRetail", "ProPlus2024Retail", "ProPlus2021Retail", "ProPlus2021Volume", "ProPlus2019Retail", "ProPlus2019Volume"
        };
        private static readonly string[] Channels =
        {
            "Current", "Current", "PerpetualVL2021", "PerpetualVL2021", "Current", "PerpetualVL2019"
        };

        public static void Install(int editionIndex, Action<string> log)
        {
            if (editionIndex < 0 || editionIndex >= Pids.Length) { log("  [!] 无效的版本选择"); return; }
            string arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            string xml = BuildConfig(Pids[editionIndex], Channels[editionIndex], arch, false);
            string dir = Path.Combine(Path.GetTempPath(), "ZyperOffice");
            try { Directory.CreateDirectory(dir); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            string xmlPath = Path.Combine(dir, "config.xml");
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);
            log("配置已生成：" + xmlPath);

            string setup = EnsureSetupExe(dir, log);
            if (string.IsNullOrEmpty(setup)) return;

            log("开始安装 Office（需联网，可能耗时数分钟，请耐心等待）...");
            Exec.RunCmd(new[] { setup, "/configure", xmlPath }, log);
            log("  [完成] 安装结束，请查看上方输出确认结果（安装失败多为网络/版本密钥问题）");
        }

        public static void Uninstall(Action<string> log)
        {
            string arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            string xml = BuildConfig("", "", arch, true);
            string dir = Path.Combine(Path.GetTempPath(), "ZyperOffice");
            try { Directory.CreateDirectory(dir); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            string xmlPath = Path.Combine(dir, "uninstall.xml");
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);

            string setup = EnsureSetupExe(dir, log);
            if (string.IsNullOrEmpty(setup)) return;

            log("开始强力卸载 Office（C2R）...");
            Exec.RunCmd(new[] { setup, "/configure", xmlPath }, log);
            CleanLeftovers(log);
            log("  [完成] 卸载结束");
        }

        /// <summary>确保 setup.exe 存在：本地有就用，否则从官方 CDN 下载。</summary>
        private static string EnsureSetupExe(string dir, Action<string> log)
        {
            string setup = Path.Combine(dir, "setup.exe");
            if (File.Exists(setup) && new FileInfo(setup).Length > 100000) return setup;

            log("下载 Office 部署工具 (ODT) setup.exe ...");
            // 官方零售通道 setup.exe 直链（Office Tool Plus 同款回退地址）
            string url = "https://officecdn.microsoft.com/pr/ws01/Office/Setup.exe";
            bool downloaded = false, setupOk = false;
            try
            {
                // 统一走 Downloader（阻塞式，等价原 WebClient.DownloadFile；失败原因经 log 输出）
                downloaded = Downloader.DownloadAsync(url, setup, log,
                    maxAttempts: 1,
                    timeoutMs: 100000,      // 等价 WebClient 默认 100 秒超时
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
    }
}
