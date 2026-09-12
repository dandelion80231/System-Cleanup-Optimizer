using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
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
        public Action<LogEntry> ExternalLogSink
        {
            get { return _externalLogSink; }
            set
            {
                _externalLogSink = value;
                LogBox.Visibility = value == null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private Action<LogEntry> _externalLogSink;

        public string SelectedArchitecture { get; set; } = "64";
        public string SelectedLanguage { get; set; } = "zh-CN";

        /// <summary>
        /// 版本下拉选中项：下标一一对应 OfficeInstall.Editions / ProductIds / Channels。
        /// 默认 0 = Microsoft 365。BuildArgs() 据此确定套件 ProductId 与 &lt;Add&gt; 通道。
        /// </summary>
        public int SelectedEdition { get; set; } = 0;

        public string SetupExePath { get; set; } = OdtSetup.GetOdtDir() + "\\setup.exe";

        private string _statusMessage = "就绪";
        private int _progressValue;
        private readonly ObservableCollection<LogEntry> _logEntries = new();
        private const int MaxLogLines = 2000; // 防止长时间运行后内存持续增长
        private bool _isInstalling;
        private bool _isIndeterminateProgress;
        /// <summary>本机是否检测到经典版 Outlook（C2R 套件应用），供强制卸载弹窗"保留经典版"选项使用。</summary>
        private bool _classicOutlookInstalled;
        /// <summary>探测到的本机已装套件 ProductId（TryGetInstalledSuite 结果），null=未装/读不到。</summary>
        private string _installedSuitePid;
        /// <summary>探测到的本机更新通道（"Update Channel" 注册表值），空=读不到（保守处理）。</summary>
        private string _installedChannel = string.Empty;
        private string _elapsedTime = "";
        private Visibility _elapsedVisibility = Visibility.Collapsed;
        private DateTime _installStartTime;
        private readonly DispatcherTimer _elapsedTimer;

        public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
        public int ProgressValue { get => _progressValue; private set { _progressValue = value; OnPropertyChanged(); } }
        public bool IsIndeterminateProgress { get => _isIndeterminateProgress; private set { _isIndeterminateProgress = value; OnPropertyChanged(); } }
        public string ElapsedTime { get => _elapsedTime; private set { _elapsedTime = value; OnPropertyChanged(); } }
        public Visibility ElapsedVisibility { get => _elapsedVisibility; private set { _elapsedVisibility = value; OnPropertyChanged(); } }
        public ObservableCollection<LogEntry> InstallLog => _logEntries;
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
            // 同步补探测 Outlook 两种形态：新版（Store/AppX，不在 C2R 台账，走 IsNewOutlookInstalledFast 快路径）
            // + 经典版标记；已装则勾选网格复选框。改为同步执行，消除"切页后 ~1s 才勾上 Outlook"的延迟。
            DetectOutlookStates();
            // 探测本机当前套件版本/更新通道，填「更新通道」状态区并决定「切通道/切版本」按钮可用性
            DetectCurrentChannel();

            // 运行时间计时器（每 1 秒更新一次）
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _installStartTime;
                ElapsedTime = elapsed.TotalSeconds < 60
                    ? $"已用: {elapsed.Seconds} 秒"
                    : $"已用: {elapsed.Minutes}:{elapsed.Seconds:D2}";
            };
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
                OdtSetup.SetCacheDir(Path.GetDirectoryName(SetupExePath) ?? SetupExePath);
            };

            // 按钮命令（用 lambda 包装以适配 ICommand）
            InstallBtn.Command = new RelayCommand(_ => OnInstallExecute(), _ => !IsInstalling);
            UninstallBtn.Command = new RelayCommand(_ => OnUninstallExecute(), _ => !IsInstalling);
            OpenXmlBtn.Command = new RelayCommand(_ => OnOpenXmlExecute(), _ => true);
            BrowseBtn.Command = new RelayCommand(_ => OnBrowseSetupExecute(), _ => true);
            OpenFolderBtn.Command = new RelayCommand(_ => OnOpenSetupFolderExecute(), _ => true);
            // 更新通道区（A 方案）：订阅版真·切通道 + 永久版重部署切版本
            SwitchChannelBtn.Command = new RelayCommand(_ => OnSwitchChannelExecute(), _ => !IsInstalling);
            SwitchEditionBtn.Command = new RelayCommand(_ => OnSwitchEditionExecute(), _ => !IsInstalling);
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
                        {
                        // 经典版(ExcludeAppId="Outlook") 与新版(OutlookForWindows) 是两个彼此独立的应用：
                        // - 经典版是否保留，看 C2R 套件 ExcludedApps 里是否含 "Outlook"；
                        // - 新版是独立 Store/AppX 应用，其安装状态不在 C2R 排除列表里反映，
                        //   必须单独按 AppX 包存在性判定（见下方后台补充检测），这里只做经典版的快速同步判定。
                        // 两个版本任一已装即视为 Outlook 已装 → 勾选，避免"本机装了新版 Outlook 但复选框不勾选"。
                        if (string.Equals(kv.Key, "Outlook", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!excluded.Contains("Outlook")) toSelect.Add(kv.Value);
                        }
                            else if (!excluded.Contains(kv.Key))
                            {
                                toSelect.Add(kv.Value);
                            }
                        }
                    }

                    // 未识别到任何可映射组件：清空所有勾选（与有组件时语义一致，避免残留硬编码默认）。
                    // 不再 early-return 跳过 foreach，统一走下方重置；架构/语言/版本无从判定时才在清空后安全返回。
                    if (toSelect.Count == 0)
                    {
                        foreach (var c in _components) c.IsSelected = false;
                        return;
                    }

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

        /// <summary>
        /// 同步探测 Outlook 两种形态的安装状态，用于同步网格「Outlook」复选框与经典版标记：
        /// - 新版 Outlook（OutlookForWindows，Store/AppX）：AppxManager.IsNewOutlookInstalledFast() 纯同步判定（WindowsApps 前缀通配 + 注册表兜底，无 PowerShell）；
        /// - 经典版 Outlook（C2R 套件应用）：IsClassicOutlookInstalled() 读注册表瞬时判定，记入 _classicOutlookInstalled 供强制卸载弹窗使用。
        /// 全程同步、不启动进程，结果在 UI 线程直接勾选复选框，消除「切页后 ~1s 才勾上 Outlook」的延迟。
        /// </summary>
        private void DetectOutlookStates()
        {
            try
            {
                bool newInstalled = AppxManager.IsNewOutlookInstalledFast();
                bool classicInstalled = IsClassicOutlookInstalled();
                _classicOutlookInstalled = classicInstalled;
                if (newInstalled || classicInstalled)
                {
                    var item = _components.FirstOrDefault(c => c.Component == ComponentCatalog.Outlook);
                    if (item != null) item.IsSelected = true;
                }
            }
            catch { /* 探测失败保守视为未装，不影响主流程 */ }
        }

        /// <summary>本机是否安装经典版 Outlook（C2R 套件应用）。优先检测 OUTLOOK.EXE 典型路径，
        /// 回退到 C2R 台账判定（套件已装且未排除 Outlook）。探测失败视为未装。</summary>
        private static bool IsClassicOutlookInstalled()
        {
            try
            {
                var exePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft Office\root\Office16\OUTLOOK.EXE"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Office\root\Office16\OUTLOOK.EXE"),
                };
                foreach (var p in exePaths)
                    if (File.Exists(p)) return true;

                var configPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\ClickToRun\Configuration",
                    @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration",
                };
                foreach (var cp in configPaths)
                {
                    using var k = Registry.LocalMachine.OpenSubKey(cp);
                    if (k == null) continue;
                    var products = (k.GetValue("ProductReleaseIds") as string) ?? string.Empty;
                    bool hasSuite = products.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(p => p.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                               || p.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase));
                    if (!hasSuite) continue;
                    bool excluded = false;
                    foreach (var name in k.GetValueNames())
                    {
                        if (!name.EndsWith(".ExcludedApps", StringComparison.OrdinalIgnoreCase)) continue;
                        var v = (k.GetValue(name) as string) ?? string.Empty;
                        if (v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Any(a => string.Equals(a.Trim(), "Outlook", StringComparison.OrdinalIgnoreCase)))
                        { excluded = true; break; }
                    }
                    if (hasSuite && !excluded) return true;
                }
            }
            catch { /* 读取异常视为未装 */ }
            return false;
        }

        /// <summary>读 C2R 台账已装产品 PID 列表（ProductReleaseIds，逗号分隔）。未装/读取失败返回空列表。</summary>
        private static List<string> GetInstalledProductIds()
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
                using var k = Registry.LocalMachine.OpenSubKey(p);
                if (k == null) continue;
                var products = (k.GetValue("ProductReleaseIds") as string) ?? string.Empty;
                return products.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
            }
            return new List<string>();
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
            if (pid.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)) return 0;   // M365 订阅，置顶
            if (pid.StartsWith("ProPlus2024Retail", StringComparison.OrdinalIgnoreCase)) return 1;    // 2024 专增 零售
            if (pid.StartsWith("ProPlus2024Volume", StringComparison.OrdinalIgnoreCase)) return 2;    // 2024 专增 批量
            if (pid.StartsWith("ProPlus2021Retail", StringComparison.OrdinalIgnoreCase)) return 5;    // 2021 专增 零售
            if (pid.StartsWith("ProPlus2021Volume", StringComparison.OrdinalIgnoreCase)) return 6;    // 2021 专增 批量
            if (pid.StartsWith("ProPlus2019Retail", StringComparison.OrdinalIgnoreCase)) return 9;    // 2019 专增 零售
            if (pid.StartsWith("ProPlus2019Volume", StringComparison.OrdinalIgnoreCase)) return 10;   // 2019 专增 批量
            // Office 2016（零售 ProPlusRetail / 批量 ProPlusVolume，旧值兼容 PerpetualVL2016）：
            // ProPlusRetail / ProPlusVolume 是 2016 的无年份裸 ID，须放在所有带年份的 ProPlus 之后匹配（下标 13，最末）。
            if (pid.StartsWith("ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                || pid.StartsWith("ProPlusVolume", StringComparison.OrdinalIgnoreCase)) return 13;
            // 注意：裸 HomeStudentRetail / HomeBusinessRetail 是 2019 与 2016 两代家庭版共用的 C2R ProductId
            // （仅 Channel 区分代：2019=Current，2016=PerpetualVL2016）。反查时此串匹配到 2019（下标 11/12），
            // 2016 家庭版（下标 14/15）由用户从下拉手动选择安装，不走此反查路径，故歧义不影响安装正确性。
            // Office 家庭版 / 家庭商务版（零售 Home*）：带年份的 Home* 先于裸 Home* 匹配。
            if (pid.StartsWith("HomeStudent2021Retail", StringComparison.OrdinalIgnoreCase)) return 7;   // 2021 家庭 零售
            if (pid.StartsWith("HomeBusiness2021Retail", StringComparison.OrdinalIgnoreCase)) return 8;   // 2021 家庭商务 零售
            if (pid.StartsWith("Home2024Retail", StringComparison.OrdinalIgnoreCase)) return 3;          // 2024 家庭 零售
            if (pid.StartsWith("HomeBusiness2024Retail", StringComparison.OrdinalIgnoreCase)) return 4;   // 2024 家庭商务 零售
            if (pid.StartsWith("HomeStudentRetail", StringComparison.OrdinalIgnoreCase)) return 11;       // 2019 家庭 零售
            if (pid.StartsWith("HomeBusinessRetail", StringComparison.OrdinalIgnoreCase)) return 12;      // 2019 家庭商务 零售
            return 0;
        }

        /// <summary>
        /// 读本机已装套件的产品/通道（复用 DetectInstalledOffice 的 4 条 C2R Configuration 路径）。
        /// 返回 true 当且仅当能取到非空 suitePid（以 O365ProPlus/ProPlus 开头的套件）；
        /// channel 可能为空（本机曾实测空），此时传空字符串由调用方保守处理。
        /// 读失败/未装 Office 返回 false，全程不抛异常。
        /// </summary>
        private static bool TryGetInstalledSuite(out string suitePid, out string channel)
        {
            suitePid = null;
            channel = string.Empty;
            try
            {
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
                if (configKey == null) return false;

                using (configKey)
                {
                    var products = (configKey.GetValue("ProductReleaseIds") as string) ?? string.Empty;
                    var suite = products
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(p => p.Trim())
                        .FirstOrDefault(p => p.Length > 0
                            && (p.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                                || p.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase)));
                    if (string.IsNullOrEmpty(suite)) return false;

                    suitePid = suite;
                    // 通道键名带空格："Update Channel"
                    var ch = configKey.GetValue("Update Channel") as string;
                    channel = string.IsNullOrEmpty(ch) ? string.Empty : ch.Trim();
                    return true;
                }
            }
            catch
            {
                return false;
            }
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
        /// <summary>
        /// 全量卸载共用入口（升级版）：原本只卸 Office，现升级为「卸载 Office 全家桶」的三选项二次确认弹窗。
        /// 触发路径（两条）与旧版一致：
        ///   1) 「🗑 强力卸载 Office」按钮（OnUninstallExecute）
        ///   2) 全部组件点掉后点「🚀 开始安装」（OnInstallExecute 全不选分支）
        /// 改动要点：
        ///   - 确认弹窗从原生 MessageBox 换成自定义勾选弹窗（UninstallConfirmDialog），含 Office / OneDrive / Teams 三选项；
        ///   - 闸门放宽：不再「没装 Office 就整段取消」，改为「没有任何『已勾选且已安装』的组件时才取消」
        ///     （Office 没装但勾了 OneDrive/Teams 且它们已装，仍执行）；
        ///   - OneDrive / Teams 勾选状态持久化到 uninstall_prefs.json（默认不勾，但记住用户上次选择）；
        ///   - 确认后按 Office→OneDrive→Teams 顺序，对「已勾选且已安装」的逐项调用其 Uninstall，互不影响，最后汇总状态。
        /// </summary>
        private void ExecuteFullUninstall()
        {
            // 探测本机已安装的组件（决定是否显示对应勾选项 + 执行时二次校验）
            bool officeInstalled = IsOfficeInstalled();
            bool oneDriveInstalled = OneDriveUninstall.IsStandaloneInstalled();
            bool teamsInstalled = TeamsUninstall.IsStandaloneInstalled();
            bool classicOutlookInstalled = IsClassicOutlookInstalled();
            bool newOutlookInstalled = AppxManager.IsNewOutlookInstalledFast();

            // 闸门1：Office / OneDrive / Teams / 新旧 Outlook 都没装，直接取消（避免弹出一堆禁用项的空弹窗）
            if (!officeInstalled && !oneDriveInstalled && !teamsInstalled
                && !classicOutlookInstalled && !newOutlookInstalled)
            {
                AppendLog("  [!] 未检测到本机已安装 Office / OneDrive / Teams，已取消全量卸载。");
                SetStatus("ℹ️ 未检测到可卸载组件，已取消");
                return;
            }

            // 读取上次偏好（OneDrive / Teams 默认不勾，但记住上次选择）
            var prefs = UninstallPrefs.Load();

            // 自定义三选项确认弹窗（宿主窗口经 Window.GetWindow 取得，转型 MainWindow 供 DialogChrome.Apply 用主题笔刷）
            var owner = Window.GetWindow(this) as MainWindow;
            var dlg = new UninstallConfirmDialog(owner, officeInstalled, oneDriveInstalled, teamsInstalled,
                classicOutlookInstalled, newOutlookInstalled, prefs.OneDrive, prefs.Teams)
            {
                Owner = owner
            };
            bool? confirmed = dlg.ShowDialog();
            if (confirmed != true)
            {
                AppendLog("  [已取消] 用户在确认弹窗选择「取消」，未执行卸载。");
                SetStatus("已取消（确认弹窗取消）");
                return;
            }

            // 持久化本次偏好（仅 OneDrive / Teams 参与；Office 始终默认勾选，不参与）
            UninstallPrefs.Save(dlg.UninstallOneDrive, dlg.UninstallTeams);

            // 放宽闸门：最终执行项 = 「已勾选 且 本机已安装」。无任何可执行项则取消。
            bool doOffice = dlg.UninstallOffice && officeInstalled;
            bool doOneDrive = dlg.UninstallOneDrive && oneDriveInstalled;
            bool doTeams = dlg.UninstallTeams && teamsInstalled;
            bool keepClassic = dlg.KeepClassicOutlook && classicOutlookInstalled;
            bool doNewOutlook = dlg.UninstallNewOutlook && newOutlookInstalled;
            if (!doOffice && !doOneDrive && !doTeams && !doNewOutlook)
            {
                AppendLog("  [!] 没有任何「已勾选且已安装」的组件，已取消卸载。");
                SetStatus("ℹ️ 未选择任何可卸载组件，已取消");
                return;
            }

            var t = new Thread(() =>
            {
                try
                {
                    SetInstalling(true);
                    SetStatus("正在卸载所选组件...");
                    int done = 0;
                    int total = (doOffice ? 1 : 0) + (doOneDrive ? 1 : 0) + (doTeams ? 1 : 0) + (doNewOutlook ? 1 : 0);

                    // ① Office（全量，C2R）；勾选「保留经典版 Outlook」时改为 ODT 修改部署，仅移除其余套件应用、保留经典版
                    if (doOffice)
                    {
                        if (keepClassic)
                        {
                            AppendLog("==== 卸载 Office（保留经典版 Outlook）====");
                            OdtKeepClassicOutlook(AppendLog);
                            done++;
                        }
                        else
                        {
                            AppendLog("==== 开始全量卸载 Office（C2R）====");
                            bool ok = OfficeInstall.Uninstall(AppendLog);
                            if (ok) { AppendLog("  [完成] Office 卸载结束"); done++; }
                            else { AppendLog("  [!] Office 卸载未成功（未实际执行或 ODT 失败），Office 仍在，请查看日志"); }
                        }
                    }

                    // ①.5 新版 Outlook（Store/AppX 应用）卸载
                    if (doNewOutlook)
                    {
                        AppendLog("==== 开始卸载新版 Outlook（Store/AppX 应用）====");
                        try
                        {
                            AppxManager.Uninstall(new List<string> { "Microsoft.OutlookForWindows_8wekyb3d8bbwe" }, AppendLog);
                            AppendLog("  [完成] 新版 Outlook 卸载结束"); done++;
                        }
                        catch (Exception noEx) { AppendLog("  [!] 新版 Outlook 卸载异常: " + noEx.Message); }
                    }

                    // ② OneDrive（独立云同步客户端）
                    if (doOneDrive)
                    {
                        AppendLog("==== 开始卸载独立 OneDrive 客户端 ====");
                        try { OneDriveUninstall.Uninstall(AppendLog); AppendLog("  [完成] OneDrive 卸载结束"); done++; }
                        catch (Exception odEx) { AppendLog("  [!] OneDrive 卸载异常: " + odEx.Message); }
                    }

                    // ③ Teams（机器级 / 经典 / 新商店版）
                    if (doTeams)
                    {
                        AppendLog("==== 开始卸载 Teams ====");
                        try { TeamsUninstall.Uninstall(AppendLog); AppendLog("  [完成] Teams 卸载结束"); done++; }
                        catch (Exception tsEx) { AppendLog("  [!] Teams 卸载异常: " + tsEx.Message); }
                    }

                    SetStatus(done == total
                        ? "✅ 已卸载所选全部组件"
                        : "⚠️ 部分组件未成功卸载，请查看日志");
                }
                catch (Exception ex)
                {
                    AppendLog("[!] 卸载过程异常: " + ex.Message);
                    SetStatus("❌ 卸载出错: " + ex.Message);
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

        /// <summary>
        /// OneDrive / Teams 卸载偏好的持久化（uninstall_prefs.json，位于 AppPaths.ConfigDir）。
        /// 规则：默认不勾选，但记住用户上次选择；Office 项始终默认勾选，不参与持久化。
        /// </summary>
        private static class UninstallPrefs
        {
            private static string FilePath() => Path.Combine(AppPaths.ConfigDir, "uninstall_prefs.json");

            public static (bool OneDrive, bool Teams) Load()
            {
                try
                {
                    string f = FilePath();
                    if (!File.Exists(f)) return (false, false);
                    var obj = MiniJson.Parse(File.ReadAllText(f, Encoding.UTF8));
                    bool od = false, ts = false;
                    if (obj is Dictionary<string, object> d)
                    {
                        if (d.TryGetValue("OneDrive", out var o1))
                            od = o1 is bool b1 ? b1 : (o1 is double n1 && n1 != 0);
                        if (d.TryGetValue("Teams", out var o2))
                            ts = o2 is bool b2 ? b2 : (o2 is double n2 && n2 != 0);
                    }
                    return (od, ts);
                }
                catch
                {
                    return (false, false);
                }
            }

            public static void Save(bool oneDrive, bool teams)
            {
                try
                {
                    if (!AppPaths.EnsureConfigDir()) return;
                    var sb = new StringBuilder();
                    sb.Append('{');
                    sb.Append("\"OneDrive\":").Append(oneDrive ? "true" : "false");
                    sb.Append(',');
                    sb.Append("\"Teams\":").Append(teams ? "true" : "false");
                    sb.Append('}');
                    File.WriteAllText(FilePath(), sb.ToString(), Encoding.UTF8);
                }
                catch { /* 偏好写入失败不影响卸载主流程 */ }
            }
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

            // 网格「Outlook」复选框现在代表「新版 Outlook（OutlookForWindows，Store/AppX 应用）」，
            // 由 AppX 单独安装/卸载（复用 AppxManager，与 AppX 商店页同一套代码），不进 ODT。
            // 此处捕获用户意图（UI 线程），ODT 跑完后再接 AppX 装/卸。
            bool outlookSelected = _components
                .Any(c => c.Component == ComponentCatalog.Outlook && c.IsSelected);

            // 捕获：OneDrive 组件是否被「取消勾选」（在 UI 线程取，代表用户当前意图）。
            // 勾选=保留/安装；取消勾选=不装。取消勾选且本机存在「独立 OneDrive 云同步客户端」
            // （OneDriveSetup.exe 安装，非 C2R 套件组件）时，ODT 装完（rc=0）后二次确认并干净卸载它
            // —— ODT 只会排 C2R 里的 Groove 组件，管不到这个独立客户端。
            bool onedriveDeselected = _components
                .Any(c => c.Component == ComponentCatalog.OneDrive && !c.IsSelected);

            var t = new Thread(() =>
            {
                try
                {
                    OdtSetup.SetCacheDir(Path.GetDirectoryName(SetupExePath) ?? SetupExePath);

                    // 【版本冲突前置拦截】：用户选了某版本但本机已装 Office 且 auto-align 会替换时，
                    // 必须在「跑 ODT 下载+执行」之前就弹窗警告（后台线程无法直接弹框，所以提前算好结果）。
                    // 复用构造期 DetectCurrentChannel() 已缓存的 _installedSuitePid，避免后台线程重复读注册表。
                    bool userPickedDifferentFromInstalled = false;
                    string userVersionName = "";
                    string installedVersionName = "";
                    string suitePid = null;
                    if (SelectedEdition >= 0 && SelectedEdition < OfficeInstall.Editions.Length)
                        userVersionName = OfficeInstall.Editions[SelectedEdition];
                    if (!string.IsNullOrEmpty(_installedSuitePid))
                    {
                        suitePid = _installedSuitePid;
                        int installedEd = MapProductToEdition(suitePid);
                        if (installedEd >= 0 && installedEd < OfficeInstall.Editions.Length)
                            installedVersionName = OfficeInstall.Editions[installedEd];
                    }
                    // 只要本机有已装 Office 且用户选择 ≠ 本机已装，就触发拦截（auto-align 会偷偷换版本）
                    if (!string.IsNullOrEmpty(installedVersionName) && !string.IsNullOrEmpty(userVersionName)
                        && !string.Equals(userVersionName, installedVersionName, StringComparison.OrdinalIgnoreCase))
                    {
                        userPickedDifferentFromInstalled = true;
                    }

                    // 若检测到冲突，在 UI 线程弹确认框（必须 UI 线程才能弹 MessageBox）
                    bool autoAlignConfirmed = true;
                    if (userPickedDifferentFromInstalled)
                    {
                        autoAlignConfirmed = this.Dispatcher.Invoke<bool>(() =>
                        {
                            var r = MessageBox.Show(Window.GetWindow(this),
                                "⚠️ 检测到本机已装 Office，您的选择将被自动对齐：\n\n"
                                + "本机已装：" + installedVersionName + "（PID=" + suitePid + "）\n"
                                + "您选择的是：" + userVersionName + "\n\n"
                                + "为确保兼容性，产品版本将被替换为「" + installedVersionName + "」继续安装。\n\n"
                                + "是否继续？",
                                "版本自动对齐提示",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning) == MessageBoxResult.Yes;
                            return r;
                        });
                        if (!autoAlignConfirmed)
                        {
                            AppendLog("  [已取消] 用户拒绝版本自动对齐，未执行安装。");
                            SetStatus("已取消（版本对齐冲突）");
                            return;
                        }
                    }

                    AppendLog("配置参数：架构=" + SelectedArchitecture + "，语言=" + SelectedLanguage
                        + "，版本=" + OfficeInstall.Editions[SelectedEdition]
                        + "，组件=" + _components.Count(c => c.IsSelected) + " 个已选");

                    // 先构建参数预判是否需要跑 ODT：网格「Outlook」代表的新版 Outlook 走 AppX、不属 ODT，
                    // 若本次没有任何 ODT 套件/独立产品、也无移除项，ODT 无事可做，跳过下载与执行以免误报。
                    var args = BuildArgs();
                    bool odtHasWork = args.Products.Count > 0 || args.RemoveProductIds.Count > 0;
                    if (odtHasWork)
                    {
                    // Phase 1: ODT shell 下载（determinate + 百分比）
                    IsIndeterminateProgress = false;
                    ElapsedVisibility = Visibility.Collapsed;
                    SetProgress(0);
                    SetStatus("正在下载 ODT 组件...");

                    string setup = OdtSetup.Ensure(AppendLog, p =>
                    {
                        SetProgress(p);
                        SetStatus($"进度: {p}%");
                    });
                    if (setup == null)
                    {
                        SetStatus("❌ ODT setup.exe 下载失败，请检查网络或手动放置到缓存目录");
                        return;
                    }

                    AppendLog("已就绪: " + setup);
                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("config.xml 已生成（" + args.Products.Count + " 个 Product）");

                    // Phase 2: Office 安装（indeterminate + 运行时间）
                    IsIndeterminateProgress = true;
                    _installStartTime = DateTime.Now;
                    ElapsedVisibility = Visibility.Visible;
                    _elapsedTimer.Start();
                    SetStatus("正在安装 Office...");

                    // 提交 ODT 前后各读一次 C2R 台账 ExcludedApps，用于事后校验移除是否真的生效
                    // （退出码 0 已被证明不可信：C2R 引擎未响应时 ODT 空转也返回 0）。
                    var excludedBefore = OdtSetup.ReadExcludedApps();
                    var pidsBefore = OdtSetup.ReadProductReleaseIds();
                    int rc = OdtSetup.RunConfig(setup, xml, AppendLog);

                    // C2R 台账的写入是引擎异步落盘的，ODT 返回瞬间可能还没写完。
                    // 故轮询最多 3 次（每次间隔 2 秒，总计约 6 秒）：一旦前后不再相等说明已生效，提前结束。
                    // 套件变化看 ExcludedApps；独立产品（Visio/Project）卸载看 ProductReleaseIds，两者都要等落盘。
                    var excludedAfter = OdtSetup.ReadExcludedApps();
                    var pidsAfter = OdtSetup.ReadProductReleaseIds();
                    for (int i = 0; i < 3
                        && DictValuesEqual(excludedBefore, excludedAfter)
                        && pidsBefore.SetEquals(pidsAfter); i++)
                    {
                        System.Threading.Thread.Sleep(2000);
                        excludedAfter = OdtSetup.ReadExcludedApps();
                        pidsAfter = OdtSetup.ReadProductReleaseIds();
                    }

                    // 本次 config 中所有目标排除项（.cs 里 OfficeProductConfig.ExcludeApps，跨 Product 汇总）
                    var targetExcluded = args.Products
                        .Where(p => p != null && p.ExcludeApps != null)
                        .SelectMany(p => p.ExcludeApps)
                        .ToList();
                    var outcome = OdtSetup.VerifyRemoveResult(excludedBefore, excludedAfter, targetExcluded, rc, AppendLog,
                        args.RemoveProductIds, pidsAfter);

                    _elapsedTimer.Stop();
                    IsIndeterminateProgress = false;
                    SetProgress(100);
                    if (rc == -2)
                    {
                        // RunConfig 用 -2 标记「ODT 打印了 usage，配置无效、根本没执行」——与 0/N 的失败区分开，
                        // 避免用户只看到 -2 不知所谓。详细日志已由 RunConfig 写入上方。
                        SetStatus("❌ ODT 未真正执行（打印了 usage 帮助），config.xml 可能无效，详见上方日志");
                    }
                    else if (rc != 0)
                    {
                        SetStatus("❌ ODT 命令失败（退出码 " + rc + "），详见上方日志");
                    }
                    else if (outcome == OdtSetup.VerifyOutcome.Failure)
                    {
                        SetStatus("⚠️ ODT 退出码 0，但组件状态未实际变化（可能 C2R 未响应），详见日志");
                    }
                    else if (outcome == OdtSetup.VerifyOutcome.NoOp)
                    {
                        SetStatus("✅ 配置已生效，组件状态与选择一致（无需变更），详见上方校验日志");
                    }
                    else
                    {
                        SetStatus("✅ ODT 执行完成，组件状态已按配置更新（详见上方校验日志）");
                    }

                    // 独立 OneDrive 云同步客户端的干净卸载：仅当 ① OneDrive 组件被取消勾选
                    // 且 ② ODT 装成功（rc=0）且 ③ 本机确实装了独立客户端 时触发。
                    // ODT 只会排 C2R 套件里的 Groove，管不到 OneDriveSetup.exe 装的这个独立客户端，
                    // 故须额外卸。先弹二次确认（说明会卸独立 OneDrive 客户端、保留同步数据）。
                    if (rc == 0 && onedriveDeselected && OneDriveUninstall.IsStandaloneInstalled())
                    {
                        bool confirmed;
                        try
                        {
                            confirmed = this.Dispatcher.Invoke(() =>
                                MessageBox.Show(
                                    "检测到本机装有「独立 OneDrive 云同步客户端」（OneDriveSetup 安装，不归 ODT 管理）。\n"
                                    + "你已取消勾选 OneDrive 组件，是否一并干净卸载该独立客户端？\n\n"
                                    + "卸载将：结束进程 → 官方卸载 → 清理 3 个程序目录与 6 项注册表残留。\n"
                                    + "会保留你的 OneDrive 同步数据目录（%LOCALAPPDATA%\\OneDrive）。\n\n确定继续吗？",
                                    "确认卸载独立 OneDrive 客户端",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
                        }
                        catch (Exception mbEx) { AppendLog("[!] 确认框异常: " + mbEx.Message); confirmed = false; }

                        if (confirmed)
                        {
                            AppendLog("开始干净卸载独立 OneDrive 客户端...");
                            try
                            {
                                OneDriveUninstall.Uninstall(AppendLog);
                                SetStatus("✅ 安装完成，并已干净卸载独立 OneDrive 客户端");
                            }
                            catch (Exception odEx)
                            {
                                AppendLog("[!] 独立 OneDrive 客户端卸载异常: " + odEx.Message);
                                SetStatus("⚠️ 独立 OneDrive 卸载出错，请查看日志");
                            }
                        }
                        else
                        {
                            AppendLog("  [已跳过] 用户未确认卸载独立 OneDrive 客户端。");
                        }
                    }
                    } // end if (odtHasWork)

                    // === 新版 Outlook（OutlookForWindows，Store/AppX 应用）装/卸 ===
                    // 与 ODT 相互独立：勾选且未装 → 安装；取消勾选且已装 → 卸载。
                    // ODT 成败不影响此步（新版是 Store 应用，不由 ODT 管理）。
                    try
                    {
                        bool newOutlookNow = AppxManager.IsNewOutlookInstalledFast();
                        if (outlookSelected && !newOutlookNow)
                        {
                            AppendLog("==== 安装新版 Outlook（Store/AppX 应用）====");
                            AppxManager.Install("9NRX63209R7B", AppendLog);
                            SetProgress(100);
                            SetStatus("✅ 新版 Outlook 安装完成（Store 应用）");
                        }
                        else if (!outlookSelected && newOutlookNow)
                        {
                            AppendLog("==== 卸载新版 Outlook（Store/AppX 应用）====");
                            AppxManager.Uninstall(new List<string> { "Microsoft.OutlookForWindows_8wekyb3d8bbwe" }, AppendLog);
                            SetProgress(100);
                            SetStatus("✅ 新版 Outlook 已卸载（Store 应用）");
                        }
                        else
                        {
                            SetProgress(100);
                            if (!odtHasWork) SetStatus("✅ 已完成（无变更）");
                        }
                    }
                    catch (Exception olEx)
                    {
                        AppendLog("[!] 新版 Outlook（AppX）装/卸异常: " + olEx.Message);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("[!] 异常: " + ex.Message);
                    SetStatus("❌ 执行出错: " + ex.Message);
                }
                finally
                {
                    // 停止计时器，显示最终耗时
                    _elapsedTimer.Stop();
                    var totalElapsed = DateTime.Now - _installStartTime;
                    if (_elapsedVisibility == Visibility.Visible)
                    {
                        ElapsedTime = $"总耗时: {totalElapsed.Minutes}:{totalElapsed.Seconds:D2}";
                    }
                    else
                    {
                        ElapsedVisibility = Visibility.Collapsed;
                        ElapsedTime = "";
                    }

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
                string file = dlg.FileName;
                string dir = Path.GetDirectoryName(file) ?? string.Empty;
                SetupExePath = file;
                OdtSetup.SetCacheDir(dir);
                SetupPathBox.Text = file;
                AppendLog("ODT 路径已更新: " + file);
                SetStatus("ODT 路径已设置");
            }
        }

        private void OnOpenSetupFolderExecute()
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(SetupExePath)
                    ? OdtSetup.GetOdtDir()
                    : (Path.GetDirectoryName(SetupExePath) ?? OdtSetup.GetOdtDir());
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

            // auto-align：本机已装 Office 且用户勾选了套件内应用（修改/移除场景）时，
            // 把通道 / 套件产品 / 版本对齐到本机 C2R 台账，减少因产品/通道不匹配加剧 C2R 空转。
            // 纯全新安装（未装 Office 或未勾套件应用）完全保持现状：不设 MatchInstalled、不动 channel/product。
            int alignEd = SelectedEdition;
            string alignChannel = OfficeInstall.Channels[SelectedEdition];
            string alignSuitePid = OfficeInstall.ProductIds[SelectedEdition];
            bool autoAligned = false;
            if (selectedSuite.Count > 0 && TryGetInstalledSuite(out var suitePid, out var channel))
            {
                alignEd = MapProductToEdition(suitePid);
                // 通道：读出非空优先用本机实际值；为空则回退 MapProductToEdition 反查通道
                alignChannel = string.IsNullOrEmpty(channel)
                    ? OfficeInstall.Channels[alignEd]
                    : channel;
                alignSuitePid = OfficeInstall.ProductIds[alignEd];
                autoAligned = true;
                // 检测本次 auto-align 是否替换了用户的版本选择（用于提前拦截）
                bool editionChanged = alignEd != SelectedEdition;
                // 触发时打一行日志；未触发（纯安装）不打，避免刷屏
                AppendLog("⚙️ auto-align：对齐到本机已装 " + suitePid
                    + "，Channel=" + alignChannel + "，Version=MatchInstalled"
                    + (editionChanged ? "（您选的版本已被替换）" : ""));
            }

            var args = new OfficeInstallArguments
            {
                Architecture = SelectedArchitecture,
                // 通道随所选版本（或 auto-align 对齐后的本机通道）：Microsoft 365 / 零售版 = Current，LTSC 批量版 = PerpetualVLxxxx
                Channel = alignChannel,
            };
            if (autoAligned) args.Version = "MatchInstalled";

            // 勾选了任意一个套件内应用 → 安装完整套件 Product，
            // 并用 ExcludeApp 剔除「未勾选」的套件应用（ODT 语义：ExcludeApp = 从套件中排除）。
            if (selectedSuite.Count > 0)
            {
                // 收集每个组件对应的全部套件内排除 ID（主 ID + 附加 ID，如 Outlook 经典版 Outlook
                // + 新版 OutlookForWindows）。一个 UI 组件因此可同时绑定多个 ODT ID，实现"一起装、一起删"
                // 的同步控制：勾选时两 ID 都进 selectedSuiteIds（都不进排除项），取消时两 ID 都进排除项。
                HashSet<string> CollectSuiteIds(IEnumerable<ComponentItem> comps) =>
                    comps
                        .Where(c => !string.IsNullOrEmpty(c.Component.ExcludeAppId))
                        .SelectMany(c =>
                        {
                            var ids = new List<string> { c.Component.ExcludeAppId };
                            if (c.Component.ExtraExcludeAppIds != null)
                                ids.AddRange(c.Component.ExtraExcludeAppIds);
                            return ids;
                        })
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var allSuiteIds = CollectSuiteIds(_components);
                var selectedSuiteIds = CollectSuiteIds(selectedSuite);

                var product = new OfficeProductConfig
                {
                    // 触发 auto-align 时用对齐后的套件 ProductId，否则用用户所选版本
                    ProductId = alignSuitePid,
                    Languages = { SelectedLanguage }
                };
                // 排除项 = 全部套件应用 − 勾选的 = 未勾选的
                foreach (var ex in allSuiteIds.Where(id => !selectedSuiteIds.Contains(id)))
                    product.ExcludeApps.Add(ex);
                // Teams 与 Lync（Skype for Business）默认都不保留：两者都不在 UI 组件目录中展示给用户，
                // 但必须始终保持排除——否则 ODT 全量替代语义会把已排除的它们意外装回。
                // 手动加入 ExcludeApps，确保 config.xml 始终包含 <ExcludeApp ID="Lync"/> 与 <ExcludeApp ID="Teams"/>。
                // Outlook 经典版与新版（OutlookForWindows）默认强制排除：经典版由本工具统一不通过 ODT 部署，
                // 新版是独立 Store/AppX 应用、ODT 的 ExcludeApp 仅用于阻止随 Office 部署，真正的安装/卸载由 AppX 负责。
                // 故无论用户如何勾选，只要走 ODT 套件部署就始终排除这两 ID。
                foreach (var forced in new[] { "Lync", "Teams", "Outlook", "OutlookForWindows" })
                    if (product.ExcludeApps.All(x => !string.Equals(x, forced, StringComparison.OrdinalIgnoreCase)))
                        product.ExcludeApps.Add(forced);
                args.Products.Add(product);
            }

            // 独立产品各自一个 Product 节点
            // 关键：订阅制套件（Channel=Current 等）不能承载 Volume 版独立产品，须改用 Retail 版
            // （VisioPro2024Retail / ProjectPro2024Retail）；LTSC 批量套件（PerpetualVL 通道）保持 Volume 版。
            // 这样 M365 + Visio/Project 可在单个 config 内合法同装，校验自动通过（方案 A）。
            // 用对齐后的通道判断（auto-align 触发时为本机实际通道），而非写死 OfficeInstall.Channels[SelectedEdition]。
            bool suiteIsSubscription = ConfigXmlBuilder.IsSubscriptionChannel(args.Channel);
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

            // 独立产品「取消勾选 = 移除」：对「本机已装、但 UI 未勾选」的独立产品（Visio/Project），
            // 显式填 RemoveProductIds，由 ConfigXmlBuilder 生成 <Remove ProductID> 节点。
            // ODT 不会因某产品不出现在 <Add> 里就删它；不加这步，取消勾选独立产品只会让 config 忽略它、ODT 留它不动。
            var installedPids = GetInstalledProductIds();
            if (installedPids.Count > 0)
            {
                var uncheckedStandalone = _components
                    .Where(c => !c.IsSelected
                                && string.IsNullOrEmpty(c.Component.ExcludeAppId)
                                && !string.IsNullOrEmpty(c.Component.StandaloneProductId))
                    .ToList();
                foreach (var item in uncheckedStandalone)
                {
                    var possible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        item.Component.StandaloneProductId,
                        item.Component.StandaloneProductIdRetail
                    };
                    possible.RemoveWhere(string.IsNullOrEmpty);
                    var match = installedPids.FirstOrDefault(pid => possible.Contains(pid));
                    if (!string.IsNullOrEmpty(match))
                        args.RemoveProductIds.Add(match);
                }
            }

            return args;
        }

        /// <summary>
        /// 卸载 Office 套件中除「经典版 Outlook」外的全部应用（ODT 修改部署）：供用户在强制卸载弹窗勾选「保留经典版 Outlook」时使用。
        /// 生成仅含套件、且 ExcludeApps 排除除 Outlook 外所有套件应用 + 强制排除 Lync/Teams 的 config，
        /// 让 ODT 移除 Word/Excel/... 但保留经典版 Outlook。无本机套件信息时直接返回（不执行）。
        /// </summary>
        private void OdtKeepClassicOutlook(Action<string> log)
        {
            if (!TryGetInstalledSuite(out var suitePid, out var channel))
            {
                log("  [!] 未检测到本机套件，无法以「保留经典版 Outlook」方式卸载，已跳过。");
                return;
            }
            int ed = MapProductToEdition(suitePid);
            var args = new OfficeInstallArguments
            {
                Architecture = SelectedArchitecture,
                Channel = string.IsNullOrEmpty(channel) ? OfficeInstall.Channels[ed] : channel,
                Version = "MatchInstalled"
            };
            var product = new OfficeProductConfig
            {
                ProductId = OfficeInstall.ProductIds[ed],
                Languages = { SelectedLanguage }
            };
            // 套件全部应用（含 Groove/OneDrive/Lync/Teams/OutlookForWindows），保留 Outlook，其余全排除
            var suiteAll = new[] { "Word", "Excel", "PowerPoint", "OneNote", "Access", "Publisher", "Groove", "OneDrive", "Lync", "Teams", "OutlookForWindows" };
            foreach (var id in suiteAll)
                if (!string.Equals(id, "Outlook", StringComparison.OrdinalIgnoreCase))
                    product.ExcludeApps.Add(id);
            args.Products.Add(product);
            try
            {
                string setup = OdtSetup.Ensure(log, _ => { });
                if (setup == null) { log("  [!] ODT setup.exe 获取失败，「保留经典版 Outlook」卸载中止。"); return; }
                string xml = ConfigXmlBuilder.Build(args);
                int rc = OdtSetup.RunConfig(setup, xml, log);
                log(rc == 0
                    ? "  [完成] 已保留经典版 Outlook，其余 Office 组件已卸载。"
                    : "  [!] ODT 退出码 " + rc + "，保留经典版 Outlook 卸载可能未完全生效，请查看日志。");
            }
            catch (Exception ex) { log("  [!] 保留经典版 Outlook 卸载异常: " + ex.Message); }
        }

        #region -- 更新通道 / 版本切换（A 方案：订阅版真·切通道 + 永久版重部署切版本）--

        /// <summary>订阅版可用的更新通道选项（ODT 合法值，依据微软官方 ODT 文档）。
        /// 每项 (值, 显示名, 节奏说明)。2026 起 SemiAnnual 将改为每月功能更新，节奏说明以官方为准、此处不写死月数。</summary>
        private static readonly (string Value, string Label, string Cadence)[] ChannelOptions =
        {
            ("Current",           "Current（最新）",              "功能就绪即发，通常每月至少一次；支持周期约 1 个月"),
            ("MonthlyEnterprise", "Monthly Enterprise（月度企业）", "每月一次（第二周二）；支持周期约 3 个月（可回滚）"),
            ("SemiAnnual",        "Semi-Annual（半年企业）",       "半年节奏，最稳；支持周期约 8 个月（2026-07 起改每月功能更新）"),
        };

        /// <summary>探测本机已装套件的 PID + 更新通道，填「更新通道」状态区 CurChannelText 并决定按钮可用性。
        /// 订阅版（Current/MonthlyEnterprise/SemiAnnual 等）→ 可原地切通道；永久版（PerpetualVL*）→ 只识别、不做原地切通道。
        /// 探测走 Dispatcher.Invoke 更新 UI，读注册表本身轻量、不阻塞构造。</summary>
        private void DetectCurrentChannel()
        {
            try
            {
                if (!TryGetInstalledSuite(out var pid, out var ch))
                {
                    _installedSuitePid = null;
                    _installedChannel = string.Empty;
                    this.Dispatcher.Invoke(() =>
                    {
                        CurChannelText.Text = "未检测到已装 Office（或无法读取 C2R 台账）";
                        ChannelHintText.Text = "需先安装订阅版/批量版 Office 后才能切换更新通道或版本。";
                    });
                    return;
                }
                _installedSuitePid = pid;
                _installedChannel = ch ?? string.Empty;

                bool isPerpetual = pid.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0
                    || !string.IsNullOrEmpty(ch) && ch.StartsWith("PerpetualVL", StringComparison.OrdinalIgnoreCase);
                string status = isPerpetual
                    ? "永久版（LTSC / 2019 批量）：无更新通道可切，需「重新部署」换版本"
                    : (string.IsNullOrEmpty(ch) ? "订阅版（更新通道值未读到，按订阅版处理）" : "订阅版，当前通道：" + ch);
                this.Dispatcher.Invoke(() =>
                {
                    CurChannelText.Text = status;
                    ChannelHintText.Text = isPerpetual
                        ? "永久版(LTSC/2019) 无更新通道，换版本请用「切换到其他版本」（重新部署，会重装）。"
                        : "订阅版可「原地切通道」(不重装)；降级到更低通道会丢失尚未发布的新功能。";
                });
            }
            catch
            {
                // 探测失败不影响主流程：保留默认「未检测到」
                this.Dispatcher.Invoke(() => CurChannelText.Text = "未检测到已装 Office");
            }
        }

        /// <summary>
        /// 订阅版「切换更新通道」：不重装，生成仅含 <Updates Channel> 的 ODT 包，C2R 更新器把本机对齐到目标通道当前构建。
        /// 闸门：本机必须已装订阅版（_installedSuitePid 非 Volume）；目标=当前则提示无需操作；
        /// 降级（当前=Current→更低节奏通道）时给「功能会回退」二次确认。执行后回读注册表通道校验。
        /// </summary>
        private void OnSwitchChannelExecute()
        {
            if (string.IsNullOrEmpty(_installedSuitePid)
                || _installedSuitePid.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                MessageBox.Show(Window.GetWindow(this), "本机装的是永久版(LTSC/2019 批量)，没有可切换的更新通道。\n如需换大版本请用「切换到其他版本」(重新部署)。",
                    "无法切换更新通道", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var owner = Window.GetWindow(this);
            string target = ShowChannelPicker(owner, _installedChannel, "选择目标更新通道（不重装，原地对齐到目标通道的当前构建）");
            if (string.IsNullOrEmpty(target))
            {
                AppendLog("  [已取消] 用户未选择目标更新通道，未执行切换。");
                return;
            }
            if (string.Equals(target, _installedChannel, StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("  [!] 目标通道 " + target + " 与本机当前一致，无需切换。");
                SetStatus("已是目标通道，无需操作");
                return;
            }

            bool downgrade = ChannelOptions.Any(o => o.Value == target)
                && ChannelOptions.Any(o => string.Equals(o.Value, _installedChannel, StringComparison.OrdinalIgnoreCase))
                && ChannelIndex(target) < ChannelIndex(_installedChannel);
            if (downgrade)
            {
                var r = MessageBox.Show(Window.GetWindow(this),
                    "从「" + _installedChannel + "」切换到「" + target + "」属于降级：\n"
                    + "本机将回退到目标通道的当前构建，目标通道尚未发布的新功能会暂时不可用。\n\n确认继续？",
                    "确认降级更新通道", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK)
                {
                    AppendLog("  [已取消] 用户取消降级切换（" + _installedChannel + " → " + target + "）。");
                    return;
                }
            }

            var editionIdx = MapProductToEdition(_installedSuitePid);
            var archIdx = ArchCombo.SelectedItem as string;
            var lang = SelectedLanguage;
            string targetFinal = target;
            int edIdx = editionIdx;
            string before = _installedChannel;

            IsIndeterminateProgress = true;
            _installStartTime = DateTime.Now;
            ElapsedVisibility = Visibility.Visible;
            _elapsedTimer.Start();
            var t = new Thread(() =>
            {
                try
                {
                    SetInstalling(true);
                    SetStatus($"正在切换更新通道（{before} → {targetFinal}，不重装）...");
                    OdtSetup.SetCacheDir(Path.GetDirectoryName(SetupExePath) ?? SetupExePath);
                    AppendLog("==== 切换更新通道：" + before + " → " + targetFinal + "（订阅版原地对齐，不重装）====");

                    string setup = OdtSetup.Ensure(AppendLog, p => { SetProgress(p); SetStatus($"准备 ODT: {p}%"); });
                    if (setup == null)
                    {
                        AppendLog("  [!] ODT setup.exe 获取失败，切换未执行。");
                        SetStatus("❌ ODT setup.exe 获取失败");
                        return;
                    }

                    // 纯改通道包：只带 <Updates Channel>，不带 <Add> 产品。
                    var args = new OfficeInstallArguments
                    {
                        Architecture = ArchCombo.SelectedItem as string ?? "64",
                        Channel = string.Empty,
                        Version = "MatchInstalled",
                        UpdateChannel = targetFinal,
                        ForceAppShutdown = true,
                        AcceptEula = true,
                        ValidateChannel = false   // 纯改通道无 Volume 产品，跳过订阅/批量校验
                    };
                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("  已生成仅含 <Updates Channel=\"" + targetFinal + "\"> 的 config.xml");

                    int rc = OdtSetup.RunConfig(setup, xml, AppendLog);

                    // 回读注册表「Update Channel」校验是否真的变更（轮询 C2R 台账异步落盘）
                    string after = before;
                    for (int i = 0; i < 4; i++)
                    {
                        if (TryGetInstalledSuite(out _, out var c)) after = c;
                        if (!string.IsNullOrEmpty(after) && !string.Equals(after, before, StringComparison.OrdinalIgnoreCase)) break;
                        System.Threading.Thread.Sleep(2000);
                    }
                    _elapsedTimer.Stop();
                    IsIndeterminateProgress = false;
                    SetProgress(100);

                    if (rc == -2) { AppendLog("  [!] ODT 打印 usage（配置无效、未真正执行），详见日志。"); SetStatus("❌ ODT 未真正执行（config 无效）"); }
                    else if (rc != 0) { AppendLog("  [!] ODT 退出码 " + rc + "，切换可能未成功，详见日志。"); SetStatus("❌ ODT 退出码 " + rc); }
                    else if (!string.IsNullOrEmpty(after) && !string.Equals(after, before, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog("  [完成] 更新通道已切换：" + before + " → " + after);
                        SetStatus("✅ 更新通道已切换至 " + after);
                    }
                    else
                    {
                        AppendLog("  [!] ODT 返回 0，但注册表更新通道未检测到变化（C2R 可能稍后生效），请手动确认。");
                        SetStatus("⚠️ 切换已提交，但通道值暂未变化（可能延迟生效）");
                    }
                    DetectCurrentChannel(); // 刷新状态区
                }
                catch (Exception ex) { AppendLog("  [!] 切换更新通道异常: " + ex.Message); SetStatus("❌ 切换异常"); }
                finally { SetInstalling(false); }
            });
            t.Start();
        }

        private static int ChannelIndex(string ch)
        {
            for (int i = 0; i < ChannelOptions.Length; i++)
                if (string.Equals(ChannelOptions[i].Value, ch, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>目标更新通道选择弹窗（单选列表，Current/MonthlyEnterprise/SemiAnnual）。返回选中值，取消返回 null。</summary>
        private string ShowChannelPicker(Window owner, string current, string prompt)
        {
            var w = new Window { Title = "切换更新通道", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = prompt, FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var list = new ListBox { Width = 420, Height = 150 };
            foreach (var o in ChannelOptions)
            {
                string mark = string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase) ? "（当前）" : "";
                list.Items.Add(new ListBoxItem { Content = o.Label + " " + mark + "\n    " + o.Cadence, Padding = new Thickness(6) });
            }
            int def = ChannelIndex(current);
            if (def < 0) def = 0;
            list.SelectedIndex = def;
            root.Children.Add(list);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = new Button { Content = "确定", MinWidth = 90, Padding = new Thickness(14, 4, 14, 4), IsDefault = true };
            Button cancel = new Button { Content = "取消", MinWidth = 90, Padding = new Thickness(14, 4, 14, 4), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            root.Children.Add(btns);
            w.Content = root;

            if (w.ShowDialog() != true || list.SelectedIndex < 0) return null;
            return ChannelOptions[list.SelectedIndex].Value;
        }

        /// <summary>
        /// 「切换到其他版本」（永久版 / 跨大版本）：用 ODT 重新部署目标版本的 ProPlus，本质是重新安装（会重装、保留激活）。
        /// 弹出 7 版本选择（OfficeInstall.Editions）→ 确认后走 ODT Add 部署。目标=当前版本则提示无需操作。
        /// 说明：与「切通道」不同，这是重新部署而非秒切，UI 文案已如实告知。
        /// </summary>
        private void OnSwitchEditionExecute()
        {
            int cur = !string.IsNullOrEmpty(_installedSuitePid) ? MapProductToEdition(_installedSuitePid) : -1;
            string picked = ShowEditionPicker(Window.GetWindow(this), cur);
            int target = OfficeInstall.Editions.Length > 0 ? Array.FindIndex(OfficeInstall.Editions, e => e == picked) : -1;
            if (target < 0)
            {
                AppendLog("  [已取消] 用户未选择目标版本，未执行切换。");
                return;
            }
            if (target == cur)
            {
                AppendLog("  [!] 目标版本与本机当前一致，无需切换。");
                SetStatus("已是该版本，无需操作");
                return;
            }

            var r = MessageBox.Show(Window.GetWindow(this),
                "切换到「" + OfficeInstall.Editions[target] + "」需要【重新部署】Office（会重装、保留激活），耗时数分钟。\n\n"
                + "（订阅版「切更新通道」是不重装的原地对齐，与此不同。）\n\n确认继续？",
                "确认重新部署切换版本", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK)
            {
                AppendLog("  [已取消] 用户取消版本重新部署（" + OfficeInstall.Editions[target] + "）。");
                return;
            }

            string edName = OfficeInstall.Editions[target];
            int edIdx = target;
            string pid = OfficeInstall.ProductIds[target];
            string ch = OfficeInstall.Channels[target];

            IsIndeterminateProgress = true;
            _installStartTime = DateTime.Now;
            ElapsedVisibility = Visibility.Visible;
            _elapsedTimer.Start();
            var t = new Thread(() =>
            {
                try
                {
                    SetInstalling(true);
                    SetStatus("正在重新部署 Office（" + edName + "）...");
                    OdtSetup.SetCacheDir(Path.GetDirectoryName(SetupExePath) ?? SetupExePath);
                    AppendLog("==== 切换版本（重新部署）：" + edName + "（" + pid + " / " + ch + "）====");

                    string setup = OdtSetup.Ensure(AppendLog, p => { SetProgress(p); SetStatus($"准备 ODT: {p}%"); });
                    if (setup == null) { AppendLog("  [!] ODT setup.exe 获取失败，切换未执行。"); SetStatus("❌ ODT setup.exe 获取失败"); return; }

                    var args = new OfficeInstallArguments
                    {
                        Architecture = ArchCombo.SelectedItem as string ?? "64",
                        Channel = ch,
                        Version = "MatchInstalled",
                        ForceAppShutdown = true,
                        AcceptEula = true,
                        DisplayFull = false
                    };
                    args.Products.Add(new OfficeProductConfig { ProductId = pid, Languages = { SelectedLanguage } });

                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("  已生成重新部署 config.xml（" + pid + " / " + ch + "）");

                    int rc = OdtSetup.RunConfig(setup, xml, AppendLog);
                    _elapsedTimer.Stop();
                    IsIndeterminateProgress = false;
                    SetProgress(100);

                    if (rc == 0)
                    {
                        AppendLog("  [完成] 已重新部署至 " + edName + "，请查看日志确认结果。");
                        SetStatus("✅ 已重新部署至 " + edName);
                    }
                    else
                    {
                        AppendLog("  [!] ODT 退出码 " + rc + "，重新部署可能未成功，详见日志。");
                        SetStatus("❌ 重新部署 ODT 退出码 " + rc);
                    }
                    DetectCurrentChannel();
                }
                catch (Exception ex) { AppendLog("  [!] 切换版本异常: " + ex.Message); SetStatus("❌ 切换异常"); }
                finally { SetInstalling(false); }
            });
            t.Start();
        }

        /// <summary>版本选择弹窗（7 个 OfficeInstall.Editions，单选），返回选中的显示名，取消返回 null。</summary>
        private string ShowEditionPicker(Window owner, int currentIdx)
        {
            var w = new Window { Title = "选择目标版本", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "选择要切换到的 Office 版本（将【重新部署】该版本，会重装、保留激活）：", FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var list = new ListBox { Width = 460, Height = 180 };
            for (int i = 0; i < OfficeInstall.Editions.Length; i++)
            {
                string mark = i == currentIdx ? "（当前）" : "";
                list.Items.Add(new ListBoxItem { Content = OfficeInstall.Editions[i] + mark, Padding = new Thickness(6) });
            }
            list.SelectedIndex = currentIdx >= 0 ? currentIdx : 0;
            root.Children.Add(list);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = new Button { Content = "确定", MinWidth = 90, Padding = new Thickness(14, 4, 14, 4), IsDefault = true };
            Button cancel = new Button { Content = "取消", MinWidth = 90, Padding = new Thickness(14, 4, 14, 4), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            root.Children.Add(btns);
            w.Content = root;

            if (w.ShowDialog() != true || list.SelectedIndex < 0) return null;
            return OfficeInstall.Editions[list.SelectedIndex];
        }

        #endregion

        #endregion

        #region -- Log & UI helpers --

        /// <summary>
        /// 单行日志条目：时间戳 + 小图标 + 文本（合并前 office-ui-prototype 的富日志渲染，本次还原）。
        /// Icon == "check" 时 UI 渲染为绿色矢量对勾；其它值按彩色 emoji 直接显示。
        /// </summary>
        public sealed class LogEntry
        {
            public string Timestamp { get; }
            public string Icon { get; }
            public string Text { get; }

            public LogEntry(string timestamp, string icon, string text)
            {
                Timestamp = timestamp;
                Icon = icon;
                Text = text;
            }
        }

        /// <summary>按日志内容挑选左侧小图标：成功→绿色对勾(check)、警告/失败→⚠️、对齐/生成→⚙️、其它→普通步骤(•)。</summary>
        public static string PickLogIcon(string text)
        {
            if (text.Contains("[OK]") || text.Contains("✅") || text.Contains("完成") || text.Contains("已卸载")
                || text.Contains("已生效") || text.Contains("成功"))
                return "check";
            if (text.Contains("[!]") || text.Contains("⚠") || text.Contains("异常") || text.Contains("失败")
                || text.Contains("未实际") || text.Contains("错误") || text.Contains("无法") || text.Contains("重试"))
                return "⚠️";
            // 卸载/删除类
            if (text.Contains("卸载") || text.Contains("删除") || text.Contains("移除"))
                return "🗑";
            // 下载/准备/就绪
            if (text.Contains("下载") || text.Contains("准备") || text.Contains("就绪")
                || text.Contains("setup.exe"))
                return "🚀";
            // 配置文件
            if (text.Contains("config") || text.Contains("配置") || text.Contains("XML") || text.Contains("xml"))
                return "📄";
            // 执行/安装/运行
            if (text.Contains("执行") || text.Contains("安装") || text.Contains("运行") || text.Contains("启动"))
                return "💻";
            // 对齐/生成
            if (text.Contains("⚙️") || text.Contains("auto-align") || text.Contains("自动对齐")
                || text.Contains("生成"))
                return "⚙️";
            // 默认（普通步骤）：⚙️。
            // 注意：绝不可返回 "▪"(U+25AA) 之类几何符号——它们没有彩色版本，
            // 任何彩色 emoji 渲染器都只会画出黑块/黑点（用户反馈"实心黑点"）。
            return "⚙️";
        }

        /// <summary>
        /// 剥离文本开头的冗余 emoji 标记（避免与行首左图标重复显示）。
        /// 当 PickLogIcon 已选定对应图标时，文本里的同类 emoji 应去掉。
        /// </summary>
        public static string StripRedundantEmojiPrefix(string text)
        {
            if (text == null) return null;
            // check 图标 → 去掉 [OK] / ✅ 前缀
            text = text.TrimStart();
            if (text.StartsWith("[OK]", StringComparison.Ordinal))
                text = text.Substring("[OK]".Length).TrimStart();
            else if (text.StartsWith("✅", StringComparison.Ordinal))
                text = text.Substring(2).TrimStart();
            // ⚠️ 图标 → 去掉 [!] / ⚠ 前缀
            else if (text.StartsWith("[!]", StringComparison.Ordinal))
                text = text.Substring("[!]".Length).TrimStart();
            else if (text.StartsWith("⚠", StringComparison.Ordinal))
                text = text.Substring(1).TrimStart();
            // ⚙️ 图标 → 去掉 ⚙️ 前缀
            else if (text.StartsWith("⚙️", StringComparison.Ordinal))
                text = text.Substring(2).TrimStart();
            return text;
        }

        private void AppendLog(string text)
        {
            if (text == null) return;
            Dispatcher.Invoke(() =>
            {
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string icon = PickLogIcon(text);
                // 剥离文本开头的冗余 emoji 标记，避免与行首左图标重复显示
                string cleanText = StripRedundantEmojiPrefix(text);
                var entry = new LogEntry(ts, icon, cleanText);

                // 自带日志框走 ItemsControl 富渲染（check 显示为绿色矢量对勾）
                _logEntries.Add(entry);
                while (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);
                try { LogScroll?.ScrollToEnd(); } catch { /* 忽略 */ }

                // 转发宿主共用日志框：宿主持有彩色富日志框（ItemsControl + emoji:TextBlock + 绿勾 Path），
                // 直接推结构化 LogEntry，图标自动彩色渲染。
                var sink = _externalLogSink;
                if (sink != null)
                {
                    try { sink(entry); } catch { /* 忽略 */ }
                }
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

        /// <summary>
        /// 判定两个 C2R 台账 ExcludedApps 字典（key=ProductReleaseId，value=逗号分隔组件列表）是否"值完全相等"。
        /// null 安全：任一为 null 视为空字典；键大小写不敏感；值按逗号拆分后归一化（小写）比对。
        /// </summary>
        private static bool DictValuesEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        {
            if (ReferenceEquals(a, b)) return true;
            var la = a ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lb = b ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (la.Count != lb.Count) return false;

            // 对每个键的值做集合级比较（忽略顺序，忽略空项）
            foreach (var kv in la)
            {
                if (!lb.TryGetValue(kv.Key, out var bv)) return false;
                if (!CsvEqual(kv.Value, bv)) return false;
            }
            return true;
        }

        private static bool CsvEqual(string csvA, string csvB)
        {
            var sa = NormCsv(csvA).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sb = NormCsv(csvB).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (sa.Count != sb.Count) return false;
            foreach (var x in sa) if (!sb.Contains(x)) return false;
            return true;
        }

        private static IEnumerable<string> NormCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) yield break;
            foreach (var part in csv.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) yield return t;
            }
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
