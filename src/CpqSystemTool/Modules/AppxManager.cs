using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

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
            new AppxDef { Label="Microsoft Store", StoreId="9WZDNCRFJBH4", PackageFamily="Microsoft.WindowsStore_8wekyb3d8bbwe", AutoRemove=false },
        };

        public static List<AppxInfo> ListInstalled(Action<string> log)
        {
            // Issue 11: 使用友好中文名（按 Catalog 的 PackageFamily / StoreId 匹配系统的 DisplayName）
            var list = new List<AppxInfo>();
            // 同时获取 Name / PackageFullName / PackageFamilyName / DisplayName
            // -AllUsers：管理员模式运行下必须指定，否则只返回管理员账户的框架包
            string ps = "Get-AppxPackage -AllUsers | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.PackageFamilyName + '|' + $_.DisplayName }";
            string outp = Exec.RunPowerShellGet(ps, log);
            if (string.IsNullOrWhiteSpace(outp)) return list;
            foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length < 4) continue;
                string name = parts[0].Trim();
                string fullName = parts[1].Trim();
                string familyName = parts[2].Trim();
                string displayName = parts[3].Trim();
                // 优先匹配 Catalog：按 PackageFamily 匹配 → 用 Label 作显示名
                string label = displayName;
                var def = Catalog.Find(c => string.Equals(c.PackageFamily, familyName, StringComparison.OrdinalIgnoreCase));
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
                list.Add(new AppxInfo { Name = label, FullName = fullName });
            }
            return list;
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
            foreach (var n in names)
            {
                log("卸载: " + n);
                // 在 PowerShell 单引号字符串中，唯一特殊字符是 '，转义为 '' 即可（backtick/$ 在单引号内均为字面量，无需转义）
                string safe = n.Replace("'", "''");
                // ★ 修复：传入的可能是 PackageFamilyName / PackageName / PackageFullName 任一。
                // 必须先 Get-AppxPackage 定位真实的 PackageFullName 与 PackageName（纯短名），
                // 否则 Remove-AppxPackage -Package 需要 full name、Remove-AppxProvisionedPackage -PackageName 需要纯 name，
                // 直接传 family name 会 PSArgumentException（参数错误）。
                string ps =
                    "$ErrorActionPreference='SilentlyContinue'; " +
                    "$pkgs = Get-AppxPackage -AllUsers | Where-Object { " +
                    "$_.PackageFamilyName -eq '" + safe + "' -or " +
                    "$_.Name -eq '" + safe + "' -or " +
                    "$_.PackageFullName -eq '" + safe + "' }; " +
                    "if ($pkgs) { foreach ($p in $pkgs) { " +
                    "Write-Host ('卸载 full=' + $p.PackageFullName + ' name=' + $p.Name); " +
                    "Remove-AppxPackage -Package $p.PackageFullName -AllUsers -ErrorAction SilentlyContinue; " +
                    "Remove-AppxProvisionedPackage -Online -PackageName $p.Name -ErrorAction SilentlyContinue } } " +
                    "else { Write-Host ('未找到已安装的包: ' + '" + safe + "') }";
                Exec.RunPowerShell(ps, log);
            }
            log("[OK] 批量卸载结束（部分系统应用可能无法移除，属正常）");
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

                // 3. 下载（断点续传，最多 3 次尝试）
                string dest = Path.Combine(Path.GetTempPath(), "cpq_appx_" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(fname));
                if (!DownloadWithResume(url, dest, log)) { log("  [!!] 下载失败"); TryDelete(dest); return false; }

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

        /// <summary>POST store.rg-adguard.net/api/GetFiles，返回 HTML（内含 CDN 直链）。</summary>
        private static string PostAdguard(string storeId, Action<string> log)
        {
            string body = "type=ProductId&url=" + Uri.EscapeDataString(storeId) + "&ring=Retail&lang=zh-CN";
            var req = (HttpWebRequest)WebRequest.Create("https://store.rg-adguard.net/api/GetFiles");
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
            req.Referer = "https://store.rg-adguard.net/";
            req.Timeout = 60000;
            using (var ws = req.GetRequestStream())
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                ws.Write(bytes, 0, bytes.Length);
            }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// 断点续传下载器：微软 CDN 支持 Range（实测 HTTP 206 + Accept-Ranges: bytes）。
        /// 每失败一次，下次从已下载字节数继续；服务器忽略 Range 时自动从头覆盖。
        /// </summary>
        private static bool DownloadWithResume(string url, string dest, Action<string> log, int maxAttempts = 3)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    long existing = File.Exists(dest) ? new FileInfo(dest).Length : 0;
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.Timeout = 60000;          // 连接超时
                    req.ReadWriteTimeout = 60000;  // 流读取超时（60s 无数据才断）
                    req.AllowAutoRedirect = true;
                    if (existing > 0) req.AddRange(existing);  // 从断点续传
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        // 服务器返回 206 且本地已有部分 → 追加；否则（200 全量）→ 覆盖
                        bool append = (resp.StatusCode == HttpStatusCode.PartialContent) && existing > 0;
                        using (var src = resp.GetResponseStream())
                        using (var dst = new FileStream(dest, append ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            byte[] buf = new byte[65536];
                            int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                        }
                    }
                    long total = new FileInfo(dest).Length;
                    log("  [下载] 完成 " + total + " 字节" + (existing > 0 ? "（续传 " + existing + " + 新 " + (total - existing) + "）" : ""));
                    return true;
                }
                catch (Exception ex)
                {
                    log("  [下载] 第 " + attempt + " 次失败: " + ex.Message);
                    if (attempt < maxAttempts) { log("  [下载] 5 秒后从断点续传重试..."); System.Threading.Thread.Sleep(5000); }
                }
            }
            return false;
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
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  }
        }

        private static string FindWinget()
        {
            string cand = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\WindowsApps\winget.exe";
            return File.Exists(cand) ? cand : null;
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
