using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// Office 部署工具（ODT）配置选择控件。
    /// 提供 10 组件网格勾选 + 架构/语言选择 + ODT 路径管理 + 安装执行。
    /// </summary>
    public partial class OfficeDeployControl : UserControl, INotifyPropertyChanged
    {
        #region -- ViewModel properties --

        private readonly ObservableCollection<ComponentItem> _components = new();
        public ObservableCollection<ComponentItem> Components => _components;

        /// <summary>
        /// 宿主页面提供的共用日志接收器。设置后本控件自带的日志框自动隐藏，
        /// 全部输出转发到宿主的共用日志框 —— 避免同一页面出现「两个日志框」。
        /// 未设置时（被独立使用）退回写自带日志框，行为不变。
        /// </summary>
        public Action<string> ExternalLogSink
        {
            get { return _externalLogSink; }
            set
            {
                _externalLogSink = value;
                LogBox.Visibility = value == null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private Action<string> _externalLogSink;

        public string SelectedArchitecture { get; set; } = "64";
        public string SelectedLanguage { get; set; } = "zh-CN";

        /// <summary>
        /// 版本下拉选中项：下标一一对应 OfficeInstall.Editions / ProductIds / Channels。
        /// 默认 0 = Microsoft 365。BuildArgs() 据此确定套件 ProductId 与 &lt;Add&gt; 通道。
        /// </summary>
        public int SelectedEdition { get; set; } = 0;

        public string SetupExePath { get; set; } = OdtSetup.CacheDir;

        private string _statusMessage = "就绪";
        private int _progressValue;
        private string _installLog = string.Empty;
        private const int MaxLogLines = 2000; // 防止长时间运行后内存持续增长
        private bool _isInstalling;

        public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
        public int ProgressValue { get => _progressValue; private set { _progressValue = value; OnPropertyChanged(); } }
        public string InstallLog { get => _installLog; private set { _installLog = value; OnPropertyChanged(); } }
        public bool IsInstalling { get => _isInstalling; private set { _isInstalling = value; OnPropertyChanged(); } }

        public ICommand InstallCommand { get; }
        public ICommand OpenXmlCommand { get; }
        public ICommand BrowseSetupCommand { get; }
        public ICommand OpenSetupFolderCommand { get; }

        #endregion

        #region -- Constructor --

        public OfficeDeployControl()
        {
            InitializeComponent();
            InitializeComponents();
            SetupBindings();
            // 还原合并时丢失的「已装组件自动识别」：打开即按本机实际安装的 Office 预勾选组件 / 架构 / 语言 / 版本
            DetectInstalledOffice();
        }

        private void InitializeComponents()
        {
            // 图标是嵌入资源，必须用 WPF pack URI；单文件 bundle 模式下 BaseDirectory 不含 Icons 目录
            const string packBase = "pack://application:,,,/系统清理与优化工具;component/Icons/";

            var catalog = new[]
            {
                (ComponentCatalog.Word,      "Word",      "Word"),
                (ComponentCatalog.Excel,     "Excel",     "Excel"),
                (ComponentCatalog.PowerPoint, "PowerPoint", "PowerPoint"),
                (ComponentCatalog.Outlook,   "Outlook",   "Outlook"),
                (ComponentCatalog.OneNote,   "OneNote",   "OneNote"),
                (ComponentCatalog.Access,    "Access",    "Access"),
                (ComponentCatalog.Publisher, "Publisher", "Publisher"),
                (ComponentCatalog.Visio,     "Visio",     "Visio"),
                (ComponentCatalog.Project,   "Project",   "Project"),
                (ComponentCatalog.OneDrive,  "OneDrive",  "OneDrive"),
            };

            foreach (var (comp, name, iconFile) in catalog)
            {
                var item = new ComponentItem(comp, name, iconFile)
                {
                    IconPath = packBase + iconFile + ".png"
                };
                // 默认全部不勾选；实际选中项由 DetectInstalledOffice() 按本机已安装 Office 动态识别
                // （新系统未装任何 Office 时保持全部不选）
                _components.Add(item);
            }
        }

        /// <summary>语言下拉候选项：Display 是界面显示用的中文名，Code 是 ODT 需要的区域码。</summary>
        private static readonly (string Code, string Display)[] OfficeLanguages =
        {
            ("zh-CN", "简体中文"),
            ("en-US", "英语（美国）"),
        };

        /// <summary>按区域码反查并选中对应下拉项；找不到时兜底选第一项，避免出现空白下拉框。</summary>
        private void SelectLanguageByCode(string code)
        {
            foreach (var obj in LangCombo.Items)
            {
                if (obj is ComboBoxItem cb && (string)cb.Tag == code)
                {
                    LangCombo.SelectedItem = cb;
                    return;
                }
            }
            if (LangCombo.Items.Count > 0) LangCombo.SelectedIndex = 0;
        }

        private void SetupBindings()
        {
            // 架构下拉
            ArchCombo.ItemsSource = new[] { "64", "32" };
            ArchCombo.SelectedItem = SelectedArchitecture;
            ArchCombo.SelectionChanged += (s, e) =>
            {
                if (ArchCombo.SelectedItem is string arch) SelectedArchitecture = arch;
            };

            // 语言下拉：界面显示中文，Tag 里存 ODT config.xml 真正需要的区域码
            LangCombo.Items.Clear();
            foreach (var lang in OfficeLanguages)
                LangCombo.Items.Add(new ComboBoxItem { Content = lang.Display, Tag = lang.Code });
            LangCombo.SelectionChanged += (s, e) =>
            {
                if (LangCombo.SelectedItem is ComboBoxItem item && item.Tag is string code)
                    SelectedLanguage = code;
            };
            SelectLanguageByCode(SelectedLanguage);

            // 版本下拉：复用 OfficeInstall.Editions 作为显示名；
            // 实际 ProductId / Channel 由 OfficeInstall.ProductIds / Channels 同源提供（下标一一对应）。
            EditionCombo.ItemsSource = OfficeInstall.Editions;
            EditionCombo.SelectedIndex = SelectedEdition;
            EditionCombo.SelectionChanged += (s, e) =>
            {
                if (EditionCombo.SelectedIndex >= 0) SelectedEdition = EditionCombo.SelectedIndex;
            };

            // ODT 路径绑定
            SetupPathBox.Text = SetupExePath;
            SetupPathBox.TextChanged += (s, e) =>
            {
                SetupExePath = SetupPathBox.Text ?? string.Empty;
                OdtSetup.SetCacheDir(SetupExePath);
            };

            // 按钮命令（用 lambda 包装以适配 ICommand）
            InstallBtn.Command = new RelayCommand(_ => OnInstallExecute(), _ => !IsInstalling);
            UninstallBtn.Command = new RelayCommand(_ => OnUninstallExecute(), _ => !IsInstalling);
            OpenXmlBtn.Command = new RelayCommand(_ => OnOpenXmlExecute(), _ => true);
            BrowseBtn.Command = new RelayCommand(_ => OnBrowseSetupExecute(), _ => true);
            OpenFolderBtn.Command = new RelayCommand(_ => OnOpenSetupFolderExecute(), _ => true);
        }

        /// <summary>
        /// 检测本机已安装的 Office（Click-to-Run）并据此预勾选组件网格 / 架构 / 语言 / 版本。
        /// 合并进 OfficeDeployControl 时，原型 office-ui-prototype 的这套「已装组件自动识别」被整段丢失，
        /// 导致只硬编码默认勾选 4 项（Word/Excel/PowerPoint/Outlook），与用户本机实际安装不符。
        /// 这里还原并扩展：套件 = 全量应用 − 注册表排除项（含 OneNote/Access/Publisher），
        /// 独立产品（Visio/Project）单独识别。注册表读取失败则保留硬编码默认，不影响主流程。
        /// </summary>
        private void DetectInstalledOffice()
        {
            try
            {
                // 优先 16.0 路径；同时尝试带/不带 WOW6432Node 与 ClickToRun 根
                var configPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
                };
                RegistryKey configKey = null;
                foreach (var p in configPaths)
                {
                    configKey = Registry.LocalMachine.OpenSubKey(p);
                    if (configKey != null) break;
                }
                if (configKey == null) return; // 未安装 Office：保留硬编码默认

                using (configKey)
                {
                    // 已安装产品（套件 + 独立产品，逗号分隔）
                    var products = (configKey.GetValue("ProductReleaseIds") as string) ?? string.Empty;
                    var productIds = products.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();
                    if (productIds.Count == 0) return;

                    // 收集排除的应用（小写），如 "Access,Publisher,OneNote"
                    var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in configKey.GetValueNames())
                    {
                        if (name.EndsWith(".ExcludedApps", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = configKey.GetValue(name) as string;
                            if (!string.IsNullOrEmpty(v))
                                foreach (var a in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    excluded.Add(a.Trim());
                        }
                    }

                    // 套件全量应用（ProPlus 系列默认包含这些，除非被排除）；Groove/OneDrive 随套件默认安装
                    var suiteApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Word", "Word" },
                        { "Excel", "Excel" },
                        { "PowerPoint", "PowerPoint" },
                        { "Outlook", "Outlook" },
                        { "OneNote", "OneNote" },
                        { "Access", "Access" },
                        { "Publisher", "Publisher" },
                        { "Groove", "OneDrive" },
                        { "OneDrive", "OneDrive" },
                    };

                    var toSelect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool hasSuite = false;
                    foreach (var pid in productIds)
                    {
                        if (pid.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                            || pid.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase))
                        {
                            hasSuite = true;
                        }
                        if (pid.StartsWith("VisioPro", StringComparison.OrdinalIgnoreCase) || pid.StartsWith("VisioStd", StringComparison.OrdinalIgnoreCase))
                            toSelect.Add("Visio");
                        if (pid.StartsWith("ProjectPro", StringComparison.OrdinalIgnoreCase) || pid.StartsWith("ProjectStd", StringComparison.OrdinalIgnoreCase))
                            toSelect.Add("Project");
                    }

                    if (hasSuite)
                    {
                        foreach (var kv in suiteApps)
                            if (!excluded.Contains(kv.Key)) toSelect.Add(kv.Value);
                    }

                    // 未识别到任何可映射组件：保留硬编码默认，避免清空勾选
                    if (toSelect.Count == 0) return;

                    // 应用勾选（覆盖 InitializeComponents 的硬编码默认）
                    foreach (var c in _components)
                        c.IsSelected = toSelect.Contains(c.DisplayName);

                    // 架构：Platform = x64 / x86
                    var platform = configKey.GetValue("Platform") as string;
                    if (!string.IsNullOrEmpty(platform))
                        SelectedArchitecture = platform.Equals("x86", StringComparison.OrdinalIgnoreCase) ? "32" : "64";

                    // 语言：优先 zh-cn，其次 en-us（网格仅这两种）
                    var langs = (configKey.GetValue("Languages") as string) ?? string.Empty;
                    if (langs.IndexOf("zh-cn", StringComparison.OrdinalIgnoreCase) >= 0)
                        SelectedLanguage = "zh-CN";
                    else if (langs.IndexOf("en-us", StringComparison.OrdinalIgnoreCase) >= 0)
                        SelectedLanguage = "en-US";

                    // 版本：根据检测到的套件产品匹配最贴近的 Edition 下标
                    var suitePid = productIds.FirstOrDefault(p =>
                        p.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                        || p.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase));
                    if (suitePid != null) SelectedEdition = MapProductToEdition(suitePid);

                    // 同步 UI 下拉（构造晚期调用，下拉已填充）
                    ArchCombo.SelectedItem = SelectedArchitecture;
                    SelectLanguageByCode(SelectedLanguage);
                    EditionCombo.SelectedIndex = SelectedEdition;
                }
            }
            catch
            {
                // 注册表读取异常不影响主流程：保留硬编码默认
            }
        }

        /// <summary>本机是否检测到已安装的 Click-to-Run Office（读注册表 Configuration 键的 ProductReleaseIds）。</summary>
        /// 用于区分「全不选」的两种语义：本机装了 Office 时全不选=全卸；新系统没装时全不选=无操作。
        private static bool IsOfficeInstalled()
        {
            var configPaths = new[]
            {
                @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
            };
            foreach (var p in configPaths)
            {
                using (var k = Registry.LocalMachine.OpenSubKey(p))
                {
                    if (k == null) continue;
                    var products = k.GetValue("ProductReleaseIds") as string;
                    if (!string.IsNullOrWhiteSpace(products)
                        && products.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => x.Length > 0))
                        return true;
                }
            }
            return false;
        }

        /// <summary>套件 ProductId → OfficeInstall.Editions 下标（与 ProductIds / Channels 一一对应）。</summary>
        private static int MapProductToEdition(string pid)
        {
            if (pid.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)) return 0;
            if (pid.StartsWith("ProPlus2024Retail", StringComparison.OrdinalIgnoreCase)) return 1;
            if (pid.StartsWith("ProPlus2024Volume", StringComparison.OrdinalIgnoreCase)) return 2;
            if (pid.StartsWith("ProPlus2021Retail", StringComparison.OrdinalIgnoreCase)) return 3;
            if (pid.StartsWith("ProPlus2021Volume", StringComparison.OrdinalIgnoreCase)) return 4;
            if (pid.StartsWith("ProPlus2019Retail", StringComparison.OrdinalIgnoreCase)) return 5;
            if (pid.StartsWith("ProPlus2019Volume", StringComparison.OrdinalIgnoreCase)) return 6;
            return 0;
        }

        #endregion

        #region -- Command handlers --

        private void OnUninstallExecute()
        {
            // 强力卸载按钮 → 走全量卸载共用入口（与「全点掉再点安装」路径同一套逻辑）
            ExecuteFullUninstall();
        }

        /// <summary>
        /// 全量卸载共用入口：IsOfficeInstalled() 闸门 + 破坏性操作二次确认弹窗 + 后台执行
        /// OfficeInstall.Uninstall（Remove All=TRUE + 清残留目录 + 清注册表）。
        /// 两条触发路径共用此方法，保证行为完全一致且都有防误触保护：
        ///   1) 「🗑 强力卸载 Office」按钮（OnUninstallExecute）
        ///   2) 全部组件点掉后点「🚀 开始安装」（OnInstallExecute 全不选分支）
        /// 本机未装 Office 时直接取消（避免在干净系统上硬跑全卸）；已装则弹确认框后全卸。
        /// </summary>
        private void ExecuteFullUninstall()
        {
            // 闸门1：本机没检测到已装 Office 时直接取消，避免在干净系统上硬跑
            // <Remove All="TRUE"/> + 残留清理（删空目录/无意义操作）。
            if (!IsOfficeInstalled())
            {
                AppendLog("  [!] 未检测到本机已安装 Office，已取消全量卸载。");
                SetStatus("ℹ️ 未检测到已装 Office，已取消");
                return;
            }

            // 闸门2：破坏性操作二次确认弹窗（移除全部组件 + C2R 引擎 + 清残留目录与注册表，不可恢复）
            var confirm = MessageBox.Show(
                "将执行 Office 全量卸载：移除全部 Office 组件与 C2R 引擎，并清理残留目录和注册表，此操作不可恢复。\n\n确定继续吗？",
                "确认全量卸载 Office",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                AppendLog("  [已取消] 用户未在确认弹窗选择「是」，未执行卸载。");
                SetStatus("已取消（确认弹窗选否）");
                return;
            }

            var t = new Thread(() =>
            {
                try
                {
                    SetInstalling(true);
                    SetStatus("正在全量卸载 Office（C2R）...");
                    AppendLog("开始全量卸载 Office...");
                    OfficeInstall.Uninstall(AppendLog);
                    SetStatus("✅ 已全量卸载 Office");
                    AppendLog("  [完成] 卸载结束");
                }
                catch (Exception ex)
                {
                    AppendLog("[!] 全量卸载异常: " + ex.Message);
                    SetStatus("❌ 全量卸载出错: " + ex.Message);
                }
                finally
                {
                    SetInstalling(false);
                }
            }) { IsBackground = true, Name = "OfficeFullRemoveWorker" };
            t.Start();
            SetProgress(0);
            SetStatus("准备中...");
        }

        private void OnInstallExecute()
        {
            // 全不选分流（区分「全点掉=全卸」与「新系统误触」两种语义）：
            // 全部组件点掉后点安装 = 全量卸载，与「🗑 强力卸载 Office」按钮走同一套共用逻辑
            // （IsOfficeInstalled 闸门 + 二次确认弹窗 + 全卸），保证两条路径行为完全一致。
            if (!_components.Any(c => c.IsSelected))
            {
                ExecuteFullUninstall();
                return;
            }

            var t = new Thread(() =>
            {
                try
                {
                    OdtSetup.SetCacheDir(SetupExePath);
                    AppendLog("配置参数：架构=" + SelectedArchitecture + "，语言=" + SelectedLanguage
                        + "，版本=" + OfficeInstall.Editions[SelectedEdition]
                        + "，组件=" + _components.Count(c => c.IsSelected) + " 个已选");

                    string setup = OdtSetup.Ensure(AppendLog, p =>
                    {
                        SetProgress(p);
                        SetStatus("\r进度: " + p + "%");
                    });
                    if (setup == null)
                    {
                        SetStatus("❌ ODT setup.exe 下载失败，请检查网络或手动放置到缓存目录");
                        return;
                    }

                    AppendLog("已就绪: " + setup);
                    var args = BuildArgs();
                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("config.xml 已生成（" + args.Products.Count + " 个 Product）");

                    SetStatus("正在执行安装...");
                    int rc = OdtSetup.RunConfig(setup, xml, AppendLog);
                    SetProgress(100);
                    SetStatus(rc == 0
                        ? "✅ 安装命令执行完毕（退出码 0）"
                        : "⚠️ 安装命令完成（退出码 " + rc + "），请查看上方日志确认结果");
                }
                catch (Exception ex)
                {
                    AppendLog("[!] 异常: " + ex.Message);
                    SetStatus("❌ 执行出错: " + ex.Message);
                }
                finally
                {
                    SetInstalling(false);
                }
            }) { IsBackground = true, Name = "OfficeDeployWorker" };
            t.Start();
            SetInstalling(true);
            SetStatus("准备中...");
            SetProgress(0);
        }

        private void OnOpenXmlExecute()
        {
            try
            {
                var args = BuildArgs();
                string xml = ConfigXmlBuilder.Build(args);
                var dlg = new SaveFileDialog
                {
                    Filter = "XML 文件|*.xml",
                    FileName = "office-deploy-config.xml",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog() == true)
                {
                    ConfigXmlBuilder.WriteConfigXml(dlg.FileName, args);
                    AppendLog("XML 已保存到: " + dlg.FileName);
                    SetStatus("XML 已保存");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[!] 保存 XML 失败: " + ex.Message);
            }
        }

        private void OnBrowseSetupExecute()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "可执行文件|*.exe|所有文件|*.*",
                Title = "选择 ODT setup.exe"
            };
            if (dlg.ShowDialog() == true)
            {
                string dir = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
                SetupExePath = dir;
                OdtSetup.SetCacheDir(dir);
                SetupPathBox.Text = dir;
                AppendLog("ODT 路径已更新: " + dlg.FileName);
                SetStatus("ODT 路径已设置");
            }
        }

        private void OnOpenSetupFolderExecute()
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(SetupExePath)
                    ? OdtSetup.CacheDir
                    : SetupExePath.TrimEnd('\\', '/');
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        #endregion

        #region -- Build arguments --

        private OfficeInstallArguments BuildArgs()
        {
            // 边界保护：选中项越界时兜底回 0（Microsoft 365），避免下标越界崩溃
            if (SelectedEdition < 0 || SelectedEdition >= OfficeInstall.ProductIds.Length) SelectedEdition = 0;

            var args = new OfficeInstallArguments
            {
                Architecture = SelectedArchitecture,
                // 通道随所选版本：Microsoft 365 / 零售版 = Current，LTSC 批量版 = PerpetualVLxxxx
                Channel = OfficeInstall.Channels[SelectedEdition],
            };

            // 勾选 = 要保留 / 安装的组件。全部不勾选时 selected 为空 → 不生成任何 Product，
            // 由 OnInstallExecute 在最前面拦截（避免“全不选兜底选第一个”导致误装一整套）。
            var selected = _components.Where(c => c.IsSelected).ToList();

            // 分组：套件内应用（靠 ExcludeApp 控制）vs 独立产品（靠 StandaloneProductId 单独成节点）
            var selectedSuite = selected
                .Where(c => !string.IsNullOrEmpty(c.Component.ExcludeAppId))
                .ToList();
            // 独立产品：无 ExcludeAppId、有 StandaloneProductId 的真正单品（Visio / Project）。
            // 套件内应用（Word/Excel… 既有 ExcludeAppId 又有 StandaloneProductId）只走套件节点，
            // 否则会被重复加为 Volume 独立产品，在订阅通道下触发“通道冲突”。
            var standaloneComponents = selected
                .Where(c => string.IsNullOrEmpty(c.Component.ExcludeAppId)
                            && !string.IsNullOrEmpty(c.Component.StandaloneProductId))
                .ToList();

            // 勾选了任意一个套件内应用 → 安装完整套件 Product，
            // 并用 ExcludeApp 剔除「未勾选」的套件应用（ODT 语义：ExcludeApp = 从套件中排除）。
            if (selectedSuite.Count > 0)
            {
                var allSuiteIds = _components
                    .Where(c => !string.IsNullOrEmpty(c.Component.ExcludeAppId))
                    .Select(c => c.Component.ExcludeAppId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var selectedSuiteIds = selectedSuite
                    .Select(c => c.Component.ExcludeAppId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var product = new OfficeProductConfig
                {
                    ProductId = OfficeInstall.ProductIds[SelectedEdition],
                    Languages = { SelectedLanguage }
                };
                // 排除项 = 全部套件应用 − 勾选的 = 未勾选的
                foreach (var ex in allSuiteIds.Where(id => !selectedSuiteIds.Contains(id)))
                    product.ExcludeApps.Add(ex);
                args.Products.Add(product);
            }

            // 独立产品各自一个 Product 节点
            // 关键：订阅制套件（Channel=Current 等）不能承载 Volume 版独立产品，须改用 Retail 版
            // （VisioPro2024Retail / ProjectPro2024Retail）；LTSC 批量套件（PerpetualVL 通道）保持 Volume 版。
            // 这样 M365 + Visio/Project 可在单个 config 内合法同装，校验自动通过（方案 A）。
            bool suiteIsSubscription = ConfigXmlBuilder.IsSubscriptionChannel(OfficeInstall.Channels[SelectedEdition]);
            foreach (var item in standaloneComponents)
            {
                string pid = (suiteIsSubscription && !string.IsNullOrEmpty(item.Component.StandaloneProductIdRetail))
                    ? item.Component.StandaloneProductIdRetail
                    : item.Component.StandaloneProductId;
                var product = new OfficeProductConfig
                {
                    ProductId = pid,
                    Languages = { SelectedLanguage }
                };
                args.Products.Add(product);
            }

            return args;
        }

        #endregion

        #region -- Log & UI helpers --

        private void AppendLog(string text)
        {
            if (text == null) return;
            Dispatcher.Invoke(() =>
            {
                _installLog += text + "\n";
                // 裁剪日志行数，防止内存持续增长
                var lines = _installLog.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > MaxLogLines)
                    _installLog = string.Join("\n", lines.Skip(lines.Length - MaxLogLines)) + "\n";
                OnPropertyChanged(nameof(InstallLog));

                // 优先转发到宿主的共用日志框（此时已在 UI 线程，宿主回调可直接操作控件）
                var sink = _externalLogSink;
                if (sink != null)
                {
                    try { sink(text); } catch { /* 忽略 */ }
                    return;
                }

                LogBox.AppendText(text + "\n");
                try { LogBox.ScrollToEnd(); } catch { /* 忽略 */ }
            });
        }

        private void SetProgress(int value)
        {
            Dispatcher.Invoke(() => ProgressValue = Math.Max(0, Math.Min(100, value)));
        }

        private void SetStatus(string msg)
        {
            Dispatcher.Invoke(() => StatusMessage = msg);
        }

        private void SetInstalling(bool val)
        {
            Dispatcher.Invoke(() =>
            {
                _isInstalling = val;
                OnPropertyChanged(nameof(IsInstalling));
                // 通知 Command 重新评估 CanExecute
                CommandManager.InvalidateRequerySuggested();
            });
        }

        #endregion

        #region -- INotifyPropertyChanged --

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }

    /// <summary>简单的 RelayCommand 实现。</summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
    }
}
