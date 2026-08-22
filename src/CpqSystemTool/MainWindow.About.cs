using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // 关于页「检查更新」「下载更新」按钮与待下载版本文件名（由 CheckForUpdate 设置，如 系统清理与优化工具_v1.08.exe）。
        private Button _aboutCheckUpdateBtn;
        private Button _aboutDownloadUpdateBtn;
        private string _pendingUpdateFileName;
        private string _pendingUpdateUrl;   // 从官网 version.json 取得的下载直链（含正确的资产文件名）

        // 更新流程状态锁：防止「检查更新」「下载更新」被并发/重复触发。均在 UI 线程读写（后台复位经 Dispatcher 回 UI 线程）。
        private bool _checkingUpdate;
        private bool _downloadingUpdate;

        /// <summary>官网根域名（含末尾斜杠），更新检查与下载直链均基于此拼接。</summary>
        private const string OfficialSiteRoot = "https://cpq-system-tool.pages.dev/";

        // =====================================================================
        //  Module: 关于（独立实现声明 + 开源引用清单）
        // =====================================================================

        private UIElement BuildAbout()
        {
            var root = new StackPanel { Margin = new Thickness(0) };
            root.Children.Add(Header("关于", "软件信息、开源协议与免责声明。"));

            TextBlock SectionTitle(string text) => new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };

            void AttachCardHover(Border card)
            {
                card.MouseEnter += (s, e) => { if (card.Background == Brushes.Transparent) card.Background = _rowHover; };
                card.MouseLeave += (s, e) => { card.Background = Brushes.Transparent; };
            }

            // 1. 身份定位卡：图标 + 名称 + 版本 + 一句话简介（右上角风险提醒）
            var identity = Card();
            AttachCardHover(identity);
            var identityInner = (StackPanel)identity.Child;
            var identityRow = new Grid();
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconContainer = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            try
            {
                var icon = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/brush.png", UriKind.Absolute)),
                    Width = 48,
                    Height = 48,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                iconContainer.Child = icon;
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                iconContainer.Child = new TextBlock { Text = "🛠", FontSize = 40, VerticalAlignment = VerticalAlignment.Center };
            }
            Grid.SetColumn(iconContainer, 0);
            identityRow.Children.Add(iconContainer);

            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            nameRow.Children.Add(new TextBlock
            {
                Text = "系统清理与优化工具",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = _textMain
            });
            nameRow.Children.Add(new Border
            {
                Child = new TextBlock
                {
                    Text = APP_VERSION.ToUpperInvariant(),
                    FontSize = 11,
                    Foreground = _textMain,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(6, 2, 6, 2)
                },
                Background = _rowHover,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            namePanel.Children.Add(nameRow);
            namePanel.Children.Add(new TextBlock
            {
                Text = "面向 Windows 10/11 的一体化系统清理、优化与维护工具。",
                FontSize = 12.5,
                Foreground = _textDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(namePanel, 1);
            identityRow.Children.Add(namePanel);

            var riskText = new TextBlock
            {
                Text = "使用本工具产生的任何后果由使用者自行承担。",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = _warnOrange,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(riskText, 2);
            identityRow.Children.Add(riskText);

            identityInner.Children.Add(identityRow);
            root.Children.Add(identity);

            // 2. 功能简介卡
            var featureCard = Card(
                SectionTitle("📋 功能简介"),
                new TextBlock
                {
                    Text = "本工具是一款面向 Windows 10/11 的一体化系统清理、优化与维护工具，秉承「最小侵入、可一键还原」的理念，帮助用户在日常使用中快速释放空间、优化系统行为、管理常用软件与系统组件，并保留对关键操作的撤销能力。主要功能覆盖：系统优化、清理优化、服务优化、Appx 商店/管理、常用软件安装、安全防护、Edge 管理、隐私设置、系统工具、激活工具、系统信息采集与配置管理。",
                    FontSize = 12.5,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                }
            );
            AttachCardHover(featureCard);
            root.Children.Add(featureCard);

            // 3. 开发者与协议卡
            var devCard = Card();
            var devInner = (StackPanel)devCard.Child;
            devInner.Children.Add(SectionTitle("👤 开发者与协议"));
            var devGrid = new Grid();
            // 2 行 × 4 列：左标签、左值、右标签、右值；右侧放置项目主页/开源协议，与左侧开发者/抖音号同行，更紧凑。
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock DevLabel(string text, bool rightSide = false) => new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = _textDim,
                Margin = rightSide ? new Thickness(24, 0, 12, 0) : new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Row 0: 开发者 + 抖音主页 | 项目主页 + GitHub
            var devLabel = DevLabel("开发者：");
            Grid.SetRow(devLabel, 0); Grid.SetColumn(devLabel, 0);
            devGrid.Children.Add(devLabel);

            var douyinLink = LinkText("狸奴呦            ╲", "https://www.douyin.com/user/MS4wLjABAAAAK7pMpJ1pN-NvaDUQgDP8ytHUgzvRh61mM-M6TLwk5X0", 12.5);
            Grid.SetRow(douyinLink, 0); Grid.SetColumn(douyinLink, 1);
            devGrid.Children.Add(douyinLink);

            var homeLabel = DevLabel("项目主页：", rightSide: true);
            Grid.SetRow(homeLabel, 0); Grid.SetColumn(homeLabel, 2);
            devGrid.Children.Add(homeLabel);

            var homeValue = LinkText("System-Cleanup-Optimizer", "https://github.com/dandelion80231/System-Cleanup-Optimizer", 12.5);
            Grid.SetRow(homeValue, 0); Grid.SetColumn(homeValue, 3);
            devGrid.Children.Add(homeValue);

            // Row 1: 抖音号 + 可复制文本框 | 开源协议 + MIT
            var idLabel = DevLabel("抖音号：");
            idLabel.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(idLabel, 1); Grid.SetColumn(idLabel, 0);
            devGrid.Children.Add(idLabel);

            var douyinBox = new TextBox
            {
                Text = "1142736528",
                FontSize = 12.5,
                Foreground = _textMain,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "双击或按 Ctrl+C 复制抖音号"
            };
            // 获得焦点时自动全选，方便一键复制
            douyinBox.GotFocus += (s, e) => douyinBox.SelectAll();
            douyinBox.MouseDoubleClick += (s, e) =>
            {
                douyinBox.SelectAll();
                try { Clipboard.SetText(douyinBox.Text); SetStatus("抖音号已复制到剪贴板"); } catch { }
            };
            Grid.SetRow(douyinBox, 1); Grid.SetColumn(douyinBox, 1);
            devGrid.Children.Add(douyinBox);

            var licenseLabel = DevLabel("开源协议：", rightSide: true);
            licenseLabel.Margin = new Thickness(24, 4, 12, 0);
            Grid.SetRow(licenseLabel, 1); Grid.SetColumn(licenseLabel, 2);
            devGrid.Children.Add(licenseLabel);

            var licenseValue = new TextBlock
            {
                Text = "Apache License 2.0",
                FontSize = 12.5,
                Foreground = _textMain,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(licenseValue, 1); Grid.SetColumn(licenseValue, 3);
            devGrid.Children.Add(licenseValue);

            // Row 2（右侧）：官网地址（紧跟开源协议行下方，与左侧邮箱同行）
            var siteLabel = DevLabel("官网：", rightSide: true);
            siteLabel.Margin = new Thickness(24, 4, 12, 0);
            Grid.SetRow(siteLabel, 2); Grid.SetColumn(siteLabel, 2);
            devGrid.Children.Add(siteLabel);

            var siteValue = LinkText("cpq-system-tool.pages.dev", "https://cpq-system-tool.pages.dev/", 12.5);
            Grid.SetRow(siteValue, 2); Grid.SetColumn(siteValue, 3);
            devGrid.Children.Add(siteValue);

            // Row 2: 邮箱 + 可复制文本框
            var emailLabel = DevLabel("邮箱：");
            emailLabel.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(emailLabel, 2); Grid.SetColumn(emailLabel, 0);
            devGrid.Children.Add(emailLabel);

            var emailBox = new TextBox
            {
                Text = "dandelion8023@365ms.cc",
                FontSize = 12.5,
                Foreground = _textMain,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "双击或按 Ctrl+C 复制邮箱"
            };
            // 获得焦点时自动全选，方便一键复制
            emailBox.GotFocus += (s, e) => emailBox.SelectAll();
            emailBox.MouseDoubleClick += (s, e) =>
            {
                emailBox.SelectAll();
                try { Clipboard.SetText(emailBox.Text); SetStatus("邮箱已复制到剪贴板"); } catch { }
            };
            Grid.SetRow(emailBox, 2); Grid.SetColumn(emailBox, 1);
            devGrid.Children.Add(emailBox);

            devInner.Children.Add(devGrid);
            AttachCardHover(devCard);
            root.Children.Add(devCard);

            // 更新日志卡（此处构建，实际添加到容器最底部）
            var updateCard = Card();
            var updateInner = (StackPanel)updateCard.Child;
            var updateHeaderRow = new Grid();
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var updateTitle = SectionTitle("🔄 更新日志");
            updateTitle.Margin = new Thickness(0);
            Grid.SetColumn(updateTitle, 0);
            updateHeaderRow.Children.Add(updateTitle);
            var checkUpdateBtn = Btn("检查更新", true, () =>
            {
                CheckForUpdate();
            }, 90);
            _aboutCheckUpdateBtn = checkUpdateBtn;
            checkUpdateBtn.FontSize = 11;
            checkUpdateBtn.Padding = new Thickness(10, 4, 10, 4);
            checkUpdateBtn.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(checkUpdateBtn, 1);
            updateHeaderRow.Children.Add(checkUpdateBtn);
            var downloadUpdateBtn = Btn("下载更新", true, () => { var _ = DownloadUpdate(); }, 90);
            downloadUpdateBtn.FontSize = 11;
            downloadUpdateBtn.Padding = new Thickness(10, 4, 10, 4);
            downloadUpdateBtn.Margin = new Thickness(0);
            downloadUpdateBtn.Visibility = Visibility.Collapsed;
            Grid.SetColumn(downloadUpdateBtn, 2);
            updateHeaderRow.Children.Add(downloadUpdateBtn);
            _aboutDownloadUpdateBtn = downloadUpdateBtn;
            updateInner.Children.Add(updateHeaderRow);
            var changelogScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 280
            };
            var changelogText = new TextBlock
            {
                Text = LoadChangelogFromEmbeddedMarkdown(),
                FontSize = 12,
                Foreground = _textMain,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            changelogScroller.Content = changelogText;
            updateInner.Children.Add(changelogScroller);

            // 第三方开源组件 / OSS 声明卡：与免责声明并列，仅列运行时实际调用的第三方软件（MAS）。
            // 本工具自身代码为原创实现，未复制/打包其他项目代码；下列为唯一运行时调用的第三方开源软件。
            var ossCard = Card(
                SectionTitle("📦 第三方开源组件 / OSS 声明"),
                new TextBlock
                {
                    Text = "本工具自身代码为原创实现；下列为运行时实际调用的第三方开源软件（仅调用、未打包、未修改）。",
                    FontSize = 11.5,
                    LineHeight = 18,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                },
                OssRow(
                    "Microsoft Activation Scripts (MAS)",
                    "GNU GPL v3",
                    ("项目主页", "https://massgrave.dev"),
                    ("在线脚本", "https://get.activated.win")));
            AttachCardHover(ossCard);
            root.Children.Add(ossCard);

            // 免责声明卡：与上方卡片保持统一边框样式与标题/图标格式。
            var disclaimerCard = Card(
                SectionTitle("⚠ 免责声明"),
                new TextBlock
                {
                    Text = "本工具仅供学习、研究与个人使用。部分功能（服务禁用、防火墙、激活、注册表与隐私设置等）会改变系统默认行为，使用前请充分了解并建议创建系统还原点。\n" +
                           "激活功能会调用第三方脚本（MAS，详见上方「第三方开源组件 / OSS 声明」），本工具不对其内容或结果负责。",
                    FontSize = 11.5,
                    LineHeight = 18,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 900,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
            AttachCardHover(disclaimerCard);
            root.Children.Add(disclaimerCard);

            // 更新日志卡置于最底部：日志条目增多时只向下延伸，不影响上方固定布局（身份/功能/开发者/免责声明）。
            AttachCardHover(updateCard);
            root.Children.Add(updateCard);

            return root;
        }

        /// <summary>从嵌入的 CHANGELOG.md 解析各版本更新日志，转为 About 页纯文本（去除 Markdown 标记）。
        /// CHANGELOG.md 为唯一事实来源：改一处即可，避免 About 文本与其不同步。</summary>
        private static string LoadChangelogFromEmbeddedMarkdown()
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("CHANGELOG.md"))
                {
                    if (stream == null) return "（更新日志资源缺失）";
                    string md;
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                        md = reader.ReadToEnd();

                    var sb = new StringBuilder();
                    bool inVersion = false;
                    foreach (var rawLine in md.Split('\n'))
                    {
                        var line = rawLine.TrimEnd('\r');
                        if (line.StartsWith("## ["))
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(
                                line, @"##\s*\[(v[0-9.]+)\]\s*-\s*([0-9-]+)");
                            sb.AppendLine(m.Success
                                ? $"{m.Groups[1].Value}（{m.Groups[2].Value}）"
                                : line.Substring(2).Trim());
                            inVersion = true;
                            continue;
                        }
                        if (line.StartsWith("### "))
                        {
                            if (inVersion) sb.AppendLine(line.Substring(4).Trim() + "：");
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            inVersion = false;
                            continue;
                        }
                        if (!inVersion) continue;
                        if (line.StartsWith(">")) continue;
                        if (line.StartsWith("- "))
                        {
                            sb.AppendLine("• " + line.Substring(2).Replace("**", "").Trim());
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        sb.AppendLine(line.Replace("**", "").Trim());
                    }
                    var result = sb.ToString().Trim();
                    return string.IsNullOrEmpty(result) ? "（更新日志为空）" : result;
                }
            }
            catch (Exception ex)
            {
                return "（更新日志读取失败：" + ex.Message + "）";
            }
        }

        // .NET Framework 的 WebClient 无 Timeout 属性（.NET 5+ 才有）；通过重写 GetWebRequest 设置底层请求超时。
        // Proxy 继承自基类 WebClient，外部可直接设置 wc.Proxy。
        // 同时显式启用 TLS 1.2（.NET 4.8 WebClient 默认仅 TLS 1.0/1.1，Cloudflare/Pages 等现代 CDN 已禁用 → 握手失败误报"无法连接"）。
        // 且 Proxy=null 在 .NET Framework 里仍会继承 IE/系统代理 → 用空 WebProxy 显式表达"不使用任何代理"。
        private class WebClientWithTimeout : System.Net.WebClient
        {
            public int TimeoutMs { get; set; } = 10000;
            protected override System.Net.WebRequest GetWebRequest(Uri uri)
            {
                var w = base.GetWebRequest(uri);
                if (w != null) w.Timeout = TimeoutMs;
                return w;
            }
        }

        /// <summary>依次尝试多种方式下载字符串，任一成功即返回；全部失败抛出汇总异常。
        /// 核心修复：.NET Framework 4.8 的 HttpWebRequest DNS 解析 IPv6 优先且失败不回退 IPv4，
        /// 而 Cloudflare Pages 返回 AAAA 记录、本机常无 IPv6 连通 → 直接超时"无法连接"。
        /// 故首选「手动解析 IPv4 + IP 直连 + Host 头保留域名」，绕开该缺陷。</summary>
        private static string DownloadStringWithProxyFallback(string url)
        {
            // 显式叠加 TLS 1.2（防御：.NET 4.8 部分环境默认仅 TLS 1.0/1.1；用 |= 只加不减，保留系统默认的 TLS 1.3 等）
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            System.Exception last = null;
            // 1) 首选：IPv4 直连（手动解析 A 记录，IP 直连 + Host 头，绕 IPv6 优先不回退 + 绕 IE 代理继承）
            try { return DownloadStringIPv4Direct(url); }
            catch (System.Exception ex) { last = ex; }
            // 2) 系统代理
            try { return DownloadStringViaProxy(url, System.Net.WebRequest.DefaultWebProxy); }
            catch (System.Exception ex) { last = ex; }
            // 3) Watt Toolkit 本地代理
            try { return DownloadStringViaProxy(url, new System.Net.WebProxy("http://127.0.0.1:26561", false)); }
            catch (System.Exception ex) { last = ex; }
            throw new System.Exception("所有网络方式均失败：" + (last?.Message ?? "未知错误"), last);
        }

        /// <summary>IPv4 直连：解析域名的 A 记录，用 IP 构造 URI 直连（无代理），Host 头写回原域名保证 SNI/证书校验正确。</summary>
        private static string DownloadStringIPv4Direct(string url)
        {
            var uri = new Uri(url);
            var ipv4 = System.Net.Dns.GetHostAddresses(uri.Host)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 == null) throw new System.Net.WebException("无法解析 IPv4 地址: " + uri.Host);
            var uri4 = new UriBuilder(uri) { Host = ipv4.ToString() }.Uri;
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(uri4);
            req.Host = uri.Host;                       // 保留域名：SNI + 证书校验
            req.Timeout = 10000;
            req.UserAgent = "CpqSystemTool";
            req.Proxy = new System.Net.WebProxy();      // 显式无代理
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                return sr.ReadToEnd();
        }

        /// <summary>走指定代理下载字符串（代理自己解析 DNS，无 IPv6 优先问题）。</summary>
        private static string DownloadStringViaProxy(string url, System.Net.IWebProxy proxy)
        {
            using (var wc = new WebClientWithTimeout { TimeoutMs = 10000, Proxy = proxy })
            {
                wc.Headers.Add("User-Agent", "CpqSystemTool");
                return wc.DownloadString(url);
            }
        }

        /// <summary>检查官网 version.json 是否有新版本，结果经 Dispatcher 回到 UI 线程写入状态栏。</summary>
        private void CheckForUpdate()
        {
            if (_checkingUpdate) { SetStatus("正在检查更新，请稍候…"); return; }
            _checkingUpdate = true;
            if (_aboutCheckUpdateBtn != null) _aboutCheckUpdateBtn.IsEnabled = false;
            SetStatus("正在检查更新…");
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var json = DownloadStringWithProxyFallback(OfficialSiteRoot + "version.json");
                    var verMatch = System.Text.RegularExpressions.Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    if (!verMatch.Success) { SetStatusUi("检查更新：未获取到版本信息"); return; }
                    var latest = verMatch.Groups[1].Value.Trim();
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(json, "\"url\"\\s*:\\s*\"([^\"]+)\"");
                    _pendingUpdateUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    // 保存完整 exe 文件名（如 系统清理与优化工具_v1.08.exe），DownloadUpdate 直接复用，无需自行拼装
                    _pendingUpdateFileName = nameMatch.Success ? nameMatch.Groups[1].Value : latest;
                    var cmp = VersionUtil.CompareVersion(APP_VERSION, latest);
                    if (cmp < 0)
                    {
                        SetStatusUi("发现新版本 " + latest + "，可前往官网下载", Visibility.Visible);
                    }
                    else if (cmp == 0)
                    {
                        _pendingUpdateFileName = null;
                        SetStatusUi("已是最新版本 (" + APP_VERSION + ")", Visibility.Collapsed);
                    }
                    else
                    {
                        _pendingUpdateFileName = null;
                        SetStatusUi("当前版本 (" + APP_VERSION + ") 已高于线上 " + latest, Visibility.Collapsed);
                    }
                }
                catch (System.Net.WebException ex)
                {
                    SetStatusUi("检查更新失败：无法连接官网（" + ex.Status + "）");
                }
                catch (System.Exception ex)
                {
                    SetStatusUi("检查更新失败：" + ex.Message);
                }
                finally
                {
                    // 复位锁并恢复按钮：成功/失败/内部提前 return 等任何完成路径都必须回到 UI 线程复位。
                    try { Dispatcher.Invoke(() => { _checkingUpdate = false; if (_aboutCheckUpdateBtn != null) _aboutCheckUpdateBtn.IsEnabled = true; }); }
                    catch { _checkingUpdate = false; }   // 窗口已关闭（Dispatcher 不可用），直接复位
                }
            });
        }

        /// <summary>回到 UI 线程写入状态栏文本。</summary>
        private void SetStatusUi(string message)
        {
            try { Dispatcher.Invoke(() => SetStatus(message)); }
            catch { /* 窗口已关闭，忽略 */ }
        }

        /// <summary>回到 UI 线程写入状态栏文本，并同步「下载更新」按钮可见性。</summary>
        private void SetStatusUi(string message, Visibility downloadBtnVisibility)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    SetStatus(message);
                    if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.Visibility = downloadBtnVisibility;
                });
            }
            catch { /* 窗口已关闭，忽略 */ }
        }

        /// <summary>用户点击「下载更新」后：弹出 SaveFileDialog 自选保存路径，然后从官网下载对应版本 exe。</summary>
        private async Task DownloadUpdate()
        {
            if (_downloadingUpdate) { SetStatus("正在下载更新，请稍候…"); return; }
            _downloadingUpdate = true;
            if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.IsEnabled = false;
            try
            {
                if (string.IsNullOrEmpty(_pendingUpdateFileName))
                {
                    SetStatus("没有检测到可用的新版本，请先点击「检查更新」");
                    return;
                }
                var tag = _pendingUpdateFileName; // e.g. "系统清理与优化工具_v1.08.exe"
                var fileName = tag;
                // 默认保存到当前已安装 exe 同级目录（v1.14/15/16 都在 D:\电脑桌面\cpq\），保证覆盖更新下载到 v1.14 旁边
                var appDir = AppContext.BaseDirectory;
                var initialDir = !string.IsNullOrEmpty(appDir) && System.IO.Directory.Exists(appDir)
                    ? appDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var dlg = new SaveFileDialog
                {
                    FileName = fileName,
                    DefaultExt = ".exe",
                    Filter = "可执行文件 (*.exe)|*.exe",
                    InitialDirectory = initialDir
                };

                if (dlg.ShowDialog() != true) return;

                // 优先使用官网 version.json 提供的直链（_pendingUpdateUrl）；仅在缺失时回退到官网根目录下的 exe 文件名。
                var url = _pendingUpdateUrl;
                if (string.IsNullOrEmpty(url)) url = OfficialSiteRoot + fileName;
                SetStatus($"正在下载 {tag} …");
                var disp = Dispatcher;
                string downloadErr = null;
                try
                {
                    // 统一走 Downloader：保留原代理回退顺序（系统→直连→Watt Toolkit）、进度回调与「保存后提示」行为
                    bool ok = await Downloader.DownloadAsync(url, dlg.FileName,
                        log: msg => { if (msg != null) downloadErr = msg; },
                        progress: pct => { try { disp.Invoke(() => SetStatus($"正在下载 {tag}：{pct}%")); } catch { /* 窗口已关闭，忽略 */ } },
                        maxAttempts: 1,          // 与原实现一致：每个代理各试一次（共 3 个候选）
                        timeoutMs: 120000,
                        readTimeoutMs: 300000,   // 等价原 WebClient 默认 ReadWriteTimeout（5 分钟无数据才断）
                        useProxyFallback: true,
                        userAgent: "CpqSystemTool");
                    if (!ok)
                    {
                        SetStatus($"下载失败：{downloadErr ?? "未知错误"}");
                        return;
                    }
                    SetStatus($"新版本已保存：{dlg.FileName}");
                    if (MessageBox.Show("下载完成，是否打开所在文件夹？", "下载完成", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\""); } catch { }
                    }
                }
                catch (System.Exception ex)
                {
                    SetStatus($"下载失败：{ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                DebugLog.Ignore(ex);
            }
            finally
            {
                // 任何完成路径（下载成功/失败/用户取消 SaveFileDialog/无版本可下）都必须复位锁并恢复按钮。
                // async 方法在 await 后回到 UI 线程（WPF SynchronizationContext），此处与入口同线程，直接复位。
                _downloadingUpdate = false;
                if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.IsEnabled = true;
            }
        }

        /// <summary>开源引用清单的一行：名称 + 许可证标签 + 一个或多个可点击来源链接。</summary>
        private UIElement OssRow(string name, string license, params (string text, string url)[] links)
        {
            var row = new Grid { Margin = new Thickness(0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = _textMain, TextWrapping = TextWrapping.Wrap });
            left.Children.Add(new TextBlock { Text = "许可：" + license, FontSize = 11, Foreground = _textDim, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(left, 0);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var (text, url) in links)
            {
                var link = LinkText(text, url, 11.5);
                link.HorizontalAlignment = HorizontalAlignment.Right;
                right.Children.Add(link);
            }
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            return row;
        }
    }
}
