using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
            // 还原合并时丢失的「已装组件自动识别」：打开即按本机实际安装的 Office 预勾选组件 / 架构 / 语言 / 版本。
            // 内部已做 C2R 台账 ∪ 磁盘已装组件并集（含无 C2R 台账时的磁盘兜底），故单独一行即可，无需额外调用。
            DetectInstalledOffice();
            // 同步探测经典版 Outlook（C2R 套件应用）：已装则勾选网格复选框。
            // 新版 Outlook（OutlookForWindows，Store/AppX）不在组件区驱动，由用户在应用内一键切换获取。
            // 改为同步执行，消除"切页后 ~1s 才勾上 Outlook"的延迟。
            DetectOutlookStates();
            // 探测本机当前套件版本/更新通道，填「更新通道」状态区并决定「切通道/切版本」按钮可用性
            DetectCurrentChannel();

            // 选项行 Grid 的一次性左缘校准：首帧渲染后，用 WPF TransformToVisual 精确测量
            // 「安装位置」标签 (LabelLocation) 相对 OptionGrid 的左缘 X，再用 RenderTransform 把
            // 安装位置提醒 (LocationHintText，目前在水平 StackPanel 里跟在 ARM 后面) 的左缘精确对齐。
            // 只在 Loaded 时执行一次（不挂 LayoutUpdated/SizeChanged → 不做每帧 RenderTransform → 不闪）。
            Loaded += (s, e) => CalibrateHintRowLeftEdgeOnce();

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

        /// <summary>
        /// 一次性校准安装位置提醒 (LocationHintText) 的左缘，精确对齐「安装位置」标签 (LabelLocation) 左缘。
        /// 只在首帧 Loaded 时执行一次；用 RenderTransform 纯渲染位移（不触发重排、不闪烁）。
        /// 若未来字体 / DPI / 窗口尺寸变化导致位置漂移，可在此重新计算并更新 RenderTransform。
        /// </summary>
        private void CalibrateHintRowLeftEdgeOnce()
        {
            if (LabelLocation == null || LocationHintText == null) return;
            // 标签相对 OptionGrid 的渲染坐标（WPF 官方 API：TransformToVisual 返回 控件→目标 的变换矩阵，
            // 把本地点 (0,0) 变换到目标坐标系，即 标签左上角在 OptionGrid 中的 X/Y）
            Point labelTopLeft = LabelLocation.TransformToVisual(OptionGrid).Transform(new Point(0, 0));
            // 安装位置提醒 相对 OptionGrid 的渲染坐标
            Point hintTopLeft = LocationHintText.TransformToVisual(OptionGrid).Transform(new Point(0, 0));
            // 让 LocationHintText 左缘对齐 标签左缘
            double delta = labelTopLeft.X - hintTopLeft.X;
            // 纯渲染位移，不触碰 Margin / 列宽，不触发布局循环，不闪烁
            LocationHintText.RenderTransform = new TranslateTransform(delta, 0);
            LocationHintText.RenderTransformOrigin = new Point(0, 0.5); // 沿水平中线位移，避免垂直漂移
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

            // 安装位置下拉（Office 装到其他盘）：第 0 项固定默认（系统盘 C:\，不挂 junction），
            // 其余项 = 本机全部固定磁盘盘符（由 OfficeInstallLocation.GetFixedDiskDrives() 提供，
            // Content 由盘符 + 容量/卷名简述组成，Tag=盘符根路径；该方法不可用时兜底只保留默认项）
            LocationCombo.Items.Clear();
            LocationCombo.Items.Add(new ComboBoxItem { Content = "默认（系统盘 C:\\）", Tag = "default" });
            try
            {
                foreach (var d in OfficeInstallLocation.GetFixedDiskDrives())
                {
                    string label;
                    try
                    {
                        var dr = new DriveInfo(d + "\\");
                        if (dr.IsReady)
                        {
                            double gb = dr.TotalFreeSpace / 1073741824.0;
                            label = d.TrimEnd('\\') + "（" + dr.VolumeLabel + "，可用 " + gb.ToString("0.#") + " GB）";
                        }
                        else
                        {
                            label = d.TrimEnd('\\') + "（未就绪）";
                        }
                    }
                    catch { label = d.TrimEnd('\\'); }
                    LocationCombo.Items.Add(new ComboBoxItem { Content = label, Tag = d });
                }
            }
            catch { /* GetFixedDiskDrives 不可用时只保留默认项，不影响主流程 */ }
            LocationCombo.SelectedIndex = 0;

            // ODT 路径绑定
            SetupPathBox.Text = SetupExePath;
            SetupPathBox.TextChanged += (s, e) =>
            {
                SetupExePath = SetupPathBox.Text ?? string.Empty;
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
                if (configKey == null)
                {
                    // 无 C2R 台账（未装 C2R 版 Office / 老版永久授权 / 被精简）→ 纯磁盘兜底：
                    // 按 Office16 目录各组件 exe 真实存在性勾选，使网格仍反映磁盘实际装了哪些组件。
                    var diskOnly = DetectDiskInstalledComponents();
                    foreach (var c in _components) c.IsSelected = diskOnly.Contains(c.DisplayName);
                    return; // 架构/语言/版本无从判定（注册表读不到），保留默认
                }

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
                        // 套件判定：ProPlus / O365 / Business / Home 任一前缀（覆盖 O365Business、EEANoTeams、家庭版等非纯 ProPlus 套件）。
                        if (IsSuitePid(pid)) hasSuite = true;
                        // 单品命中（按组件名开头匹配，不随套件）：装了独立单品就勾对应组件，与套件取并集。
                        // Word/Excel/PowerPoint/Access/Publisher/OneNote/Visio/Project。
                        // Outlook 经典版走下方套件−ExcludedApps + 新版 AppX 探测，不在此单品匹配（避免与新版混淆）。
                        var comp = MapPidToComponent(pid);
                        if (comp != null) toSelect.Add(comp);
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

                    // ===== A 方案：磁盘兜底并集 =====
                    // C2R 台账可能滞后于用户手动装/删组件（手动装了 Access 独立版却没回写 ExcludedApps /
                    // ProductReleaseIds）。按 Office16 目录各组件 exe 真实存在性补勾选，与上面 C2R 结果取并集。
                    // 单向增强：只补勾，不误删 C2R 已勾的；C2R 未装/无台账时纯靠磁盘也能识别。
                    foreach (var diskComp in DetectDiskInstalledComponents())
                        toSelect.Add(diskComp);

                    // 未识别到任何可映射组件（C2R + 磁盘都为空）：清空所有勾选（与有组件时语义一致，避免残留硬编码默认）。
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

                    // 版本：多 PID 共存时优先选 O365ProPlusRetail（365 订阅），避免 2016 ProPlusRetail 排在前面时误判。
                    var suitePid = productIds.Where(IsSuitePid)
                        .OrderByDescending(p => p.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .FirstOrDefault();
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
        /// 磁盘兜底探测：C2R 台账（注册表）滞后于用户手动装/删组件时（如手动装了 Access 独立版却没回写
        /// ExcludedApps），按 Office16 目录下各组件可执行文件是否真实存在，返回磁盘上已装组件的
        /// DisplayName 集合。供 DetectInstalledOffice 与「C2R 台账结果」取并集，使勾选反映磁盘真实状态。
        /// 全程同步、仅 File.Exists、不启动进程、任何异常返回空集合（保守，不影响主流程）。
        /// </summary>
        private static HashSet<string> DetectDiskInstalledComponents()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // 候选 Office 安装根目录（32/64 位 × C2R root/老版 MSI 两种落点）。
                // 多根可共存（如 64 位 C2R 套件 + 32 位独立 MSI 版 Visio/Project 在企业机常见），
                // 旧实现 Array.Find 只扫第一个存在的根、漏掉其余根里的 32 位单品 → 现改为所有存在根取并集。
                var roots = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft Office\root\Office16"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft Office\Office16"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Office\root\Office16"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Office\Office16"),
                };

                // 组件 → 可执行文件名映射（C2R 单机 exe 名）。任一存在即视为该组件磁盘已装。
                // Outlook 此处也探测：经典版 OUTLOOK.EXE 在 Office16 下；新版 AppX 由 DetectOutlookStates 单独补。
                var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Word", new[] { "WINWORD.EXE" } },
                    { "Excel", new[] { "EXCEL.EXE" } },
                    { "PowerPoint", new[] { "POWERPNT.EXE" } },
                    { "Access", new[] { "MSACCESS.EXE" } },
                    { "Publisher", new[] { "MSPUB.EXE" } },
                    { "Outlook", new[] { "OUTLOOK.EXE" } },
                    { "OneNote", new[] { "ONENOTE.EXE", "ONENOTE16.EXE" } },
                    { "Visio", new[] { "VISIO.EXE", "VISIO32.EXE", "VISIO64.EXE" } },
                    { "Project", new[] { "MSPROJECT.EXE", "MSPROJ.EXE" } },
                };
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var kv in map)
                    {
                        foreach (var exe in kv.Value)
                            if (File.Exists(Path.Combine(root, exe)))
                            {
                                found.Add(kv.Key);
                                break;
                            }
                    }
                }

                // OneDrive（MDOB / 独立 OneDrive）由 OneDriveSetup.exe 安装，程序目录在
                // Program Files\Microsoft OneDrive 或 Program Files (x86)\Microsoft OneDrive（版本号子目录），
                // 不在 Office16 目录下，须单独探测。复用 IsStandaloneInstalled（已覆盖 64/32 位两个根），
                // 使网格「OneDrive」勾选反映磁盘真实状态（C2R 台账排除 onedrive 时仍能在磁盘兜底里补勾回来）。
                if (OneDriveUninstall.IsStandaloneInstalled())
                    found.Add("OneDrive");
            }
            catch { /* 探测失败保守返回空，不影响 C2R 主路径 */ }
            return found;
        }

        /// <summary>
        /// 同步探测经典版 Outlook（C2R 套件应用）安装状态，用于同步网格「Outlook」复选框与经典版标记：
        /// - 经典版 Outlook（C2R 套件应用）：IsClassicOutlookInstalled() 读注册表瞬时判定，记入 _classicOutlookInstalled 供强制卸载弹窗使用。
        /// - 新版 Outlook（OutlookForWindows，Store/AppX）不在组件区驱动，由用户在新版 Outlook 应用内一键切换获取；
        ///   其探测仅用于强制卸载弹窗（ExecuteFullUninstall）展示"可移除新版"选项，不参与组件区勾选。
        /// 全程同步、不启动进程，结果在 UI 线程直接勾选复选框，消除「切页后 ~1s 才勾上 Outlook」的延迟。
        /// </summary>
        private void DetectOutlookStates()
        {
            try
            {
                bool classicInstalled = IsClassicOutlookInstalled();
                _classicOutlookInstalled = classicInstalled;
                if (classicInstalled)
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

        /// <summary>套件 PID 判定：C2R 台账里属于「Office 全家桶」的 ProductReleaseId。</summary>
        /// 覆盖全部 7 个版本套件形态（与 OfficeInstall.ProductIds 同源口径）：
        ///   O365 订阅系（O365ProPlus* / O365Business* / O365ProPlusEEANoTeams*）；
        ///   ProPlus 永久授权系（ProPlus{2016,2019,2021,2024}{Retail,Volume} / 裸 ProPlusRetail/ProPlusVolume）；
        ///   Home 家庭/家庭商务系（Home{2016,2019,2021,2024}{,Business}Retail）。
        /// 用「前缀」而非「子串」判定，避免把单品 PID（Word/Excel…）误判成套件。
        private static bool IsSuitePid(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return false;
            return pid.StartsWith("O365", StringComparison.OrdinalIgnoreCase)
                || pid.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase)
                || pid.StartsWith("Home", StringComparison.OrdinalIgnoreCase)
                || pid.StartsWith("Business", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 单品 PID → 可勾选组件名（10 组件之一），非单品返回 null。
        /// 微软 ODT 单品 ProductId 固定形态：&lt;组件&gt;&lt;版本&gt;&lt;渠道&gt;，
        /// 组件 ∈ {Word, Excel, PowerPoint, Access, Publisher, OneNote, Visio, Project}。
        /// 用「以组件名开头」匹配（区分大小写不敏感），保证：
        ///   ① 不随套件的独立单品（Word2021Retail / Access2024Volume / OneNote2021Volume / Publisher2021Volume / VisioPro2024Retail / ProjectStd…）能识别；
        ///   ② 套件 PID（ProPlus*/O365*/Home*）开头不是组件名，不会误判出单品。
        /// Outlook 单品无独立 C2R ProductId（经典版随套件、新版走 AppX），故不在此表，
        /// 经典版由「套件−ExcludedApps」+ 新版 AppX 探测（DetectOutlookStates）覆盖。
        /// </summary>
        private static string MapPidToComponent(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return null;
            // 长组件名在前，避免 PowerPoint 被 PowerPoi* 误截、OneNote 与 Other 混淆。
            var rules = new[]
            {
                "PowerPoint", "OneNote", "Publisher", "Project", "Visio",
                "Word", "Excel", "Access",
            };
            foreach (var r in rules)
                if (pid.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
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
                // 匹配优先级：O365ProPlusRetail（365 订阅）> ProPlus（裸 2016 等）。
                // 用 List + OrderByDescending 保证多 PID 共存时优先选订阅版，
                // 避免 2016 ProPlusRetail 排在前面时 FirstOrDefault 永远匹配到 2016。
                var products = (configKey.GetValue("ProductReleaseIds") as string) ?? string.Empty;
                var allPids = products
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0
                        && (p.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase)
                            || p.StartsWith("ProPlus", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                // O365ProPlusRetail 排最前（订阅版 365 优先级最高）
                allPids.Sort((a, b) =>
                {
                    int scoreA = a.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    int scoreB = b.StartsWith("O365ProPlusRetail", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    return scoreB.CompareTo(scoreA);
                });
                var suite = allPids.FirstOrDefault();
                if (string.IsNullOrEmpty(suite)) return false;

                    suitePid = suite;
                    // 真实键名是 UpdateChannel（无空格），旧代码误读带空格的 "Update Channel" 永远读到 null；
                    // 且其取值可能是 CDN URL 而非通道名，须归一化为 ODT 标准通道名
                    var ch = configKey.GetValue("UpdateChannel") as string ?? configKey.GetValue("Update Channel") as string;
                    channel = NormalizeRawChannel(ch ?? string.Empty);
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
            bool newOutlookInstalled = AppxManager.IsNewOutlookInstalledFast();

            // 闸门1：Office / OneDrive / Teams / 新版 Outlook 都没装，直接取消（避免弹出一堆禁用项的空弹窗）
            if (!officeInstalled && !oneDriveInstalled && !teamsInstalled
                && !newOutlookInstalled)
            {
                AppendLog("  [!] 未检测到本机已安装 Office / OneDrive / Teams / 新版 Outlook，已取消全量卸载。");
                SetStatus("ℹ️ 未检测到可卸载组件，已取消");
                return;
            }

            // 读取上次偏好（null = 尚未做过选择）
            var prefs = UninstallPrefs.Load();
            // OneDrive 默认态：本机检测到 OneDrive 客户端（PF/PF(x86)\Microsoft OneDrive，MDOB 或独立 OneDrive 均覆盖）
            // → 默认勾选（「全量卸载」按钮本意 = 删掉本机实际存在的组件）；未检测到 → 默认不勾（本就没有，跳过即可）。
            // 两种情况下，用户一旦在弹窗里做过选择即被记住（prefs 非 null 时沿用），不会反复打回默认。
            bool oneDriveDefault = oneDriveInstalled ? (prefs.OneDrive ?? true) : (prefs.OneDrive ?? false);
            bool teamsDefault = prefs.Teams ?? false;

            // 自定义多选项确认弹窗（宿主窗口经 Window.GetWindow 取得，转型 MainWindow 供 DialogChrome.Apply 用主题笔刷）
            var owner = Window.GetWindow(this) as MainWindow;
            var dlg = new UninstallConfirmDialog(owner, officeInstalled, oneDriveInstalled, teamsInstalled,
                newOutlookInstalled, oneDriveDefault, teamsDefault)
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

            // 持久化本次偏好（仅 OneDrive / Teams 参与；Office 始终默认勾选，不参与）。
            // 只回写「实际显示项」的勾选值：组件未安装（项隐藏）时 dlg 值恒为 false，若照写会覆盖用户
            // 上次记住的偏好（违反“记住上次选择”），故保留隐藏项的旧值。
            UninstallPrefs.Save(
                oneDriveInstalled ? dlg.UninstallOneDrive : (prefs.OneDrive ?? false),
                teamsInstalled ? dlg.UninstallTeams : (prefs.Teams ?? false));

            // 放宽闸门：最终执行项 = 「已勾选 且 本机已安装」。无任何可执行项则取消。
            bool doOffice = dlg.UninstallOffice && officeInstalled;
            bool doOneDrive = dlg.UninstallOneDrive && oneDriveInstalled;
            bool doTeams = dlg.UninstallTeams && teamsInstalled;
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
                    AppendLog(GetVersionSelfCheck());
                    SetInstalling(true);
                    SetStatus("正在卸载所选组件...");
                    int done = 0;
                    int total = (doOffice ? 1 : 0) + (doOneDrive ? 1 : 0) + (doTeams ? 1 : 0) + (doNewOutlook ? 1 : 0);

                    // ① Office（全量，C2R）
                    if (doOffice)
                    {
                        AppendLog("==== 开始全量卸载 Office（C2R）====");
                        // 先拆 junction 壳（若 Office 之前装在其他盘）：ODT 全卸不会删 C 盘的 junction 壳，
                        // 且必须先于卸载清残留执行——否则 CleanLeftovers 的 Remove-Item -Recurse 会穿透
                        // junction 递归删除目标盘的真实数据。拆壳失败仅记日志，不阻塞卸载主流程。
                        try
                        {
                            if (OfficeInstallLocation.IsJunctionActive("64") || OfficeInstallLocation.IsJunctionActive("32"))
                            {
                                string offRoot = OfficeInstallLocation.GetOfficeRootPath("64");
                                if (OfficeInstallLocation.IsJunction(offRoot))
                                    OfficeInstallLocation.RemoveJunctionShell(offRoot, AppendLog);
                                offRoot = OfficeInstallLocation.GetOfficeRootPath("32");
                                if (OfficeInstallLocation.IsJunction(offRoot))
                                    OfficeInstallLocation.RemoveJunctionShell(offRoot, AppendLog);
                            }
                        }
                        catch (Exception jx) { AppendLog("  [!] 拆 junction 壳异常（不阻塞卸载）: " + jx.Message); }
                        bool ok = OfficeInstall.Uninstall(AppendLog);
                        if (ok) { AppendLog("  [完成] Office 卸载结束"); done++; }
                        else { AppendLog("  [!] Office 卸载未成功（未实际执行或 ODT 失败），Office 仍在，请查看日志"); }
                    }

                    // ①.5 新版 Outlook（Store/AppX 应用）卸载
                    if (doNewOutlook)
                    {
                        AppendLog("==== 开始卸载新版 Outlook（Store/AppX 应用）====");
                        try
                        {
                            AppxManager.Uninstall(new List<string> { "Microsoft.OutlookForWindows_8wekyb3d8bbwe" }, AppendLog);
                            // B 层（C 方案）：卸包后清 AppX 残留注册表状态键，避免残留键让 IsNewOutlookInstalledFast
                            // 注册表兜底误判"新版 Outlook 还在"导致网格复选框误勾。best-effort，失败不阻塞。
                            AppxManager.RemoveNewOutlookResidualRegistryKeys(AppendLog);
                            // A 方案（治本）：清 WindowsApps 下 Microsoft.OutlookForWindows_* 残留目录壳
                            // （Remove-AppxPackage 删包后目录常残留，导致主路径目录命中仍勾选）。best-effort。
                            AppxManager.RemoveNewOutlookWindowsAppsResidual(AppendLog);
                            AppendLog("  [完成] 新版 Outlook 卸载结束"); done++;
                        }
                        catch (Exception noEx) { AppendLog("  [!] 新版 Outlook 卸载异常: " + noEx.Message); }
                    }

                    // ② OneDrive（云同步客户端；可能为 ODT 套件自带，也可能独立手装）
                    if (doOneDrive)
                    {
                        AppendLog("==== 开始卸载 OneDrive 客户端 ====");
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

            public static (bool? OneDrive, bool? Teams) Load()
            {
                try
                {
                    string f = FilePath();
                    if (!File.Exists(f)) return (null, null); // 无记录 → null（与“记录为 false”区分）
                    var obj = MiniJson.Parse(File.ReadAllText(f, Encoding.UTF8));
                    bool? od = null, ts = null;
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
                    return (null, null);
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

        /// <summary>
        /// 用户在「安装位置」下拉中选择的非默认目标盘根路径（= ComboBoxItem.Tag）。
        /// 选「默认（系统盘 C:\）」或未选时返回 null，表示不挂 junction、走原默认流程。
        /// 调用方（安装主流程）据此决定是否用 OfficeInstallLocation.EnsureJunction 重定向 Office 根目录。
        /// </summary>
        private string GetSelectedInstallLocation()
        {
            if (LocationCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && !string.IsNullOrWhiteSpace(tag)
                && !string.Equals(tag, "default", StringComparison.OrdinalIgnoreCase))
            {
                return tag;
            }
            return null;
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

            // 捕获：OneDrive 组件是否被「取消勾选」（在 UI 线程取，代表用户当前意图）。
            // 勾选=保留/安装；取消勾选=不装。取消勾选且本机存在「独立 OneDrive 云同步客户端」
            // （OneDriveSetup.exe 安装，非 C2R 套件组件）时，ODT 装完（rc=0）后二次确认并干净卸载它
            // —— ODT 只会排 C2R 里的 Groove 组件，管不到这个独立客户端。
            bool onedriveDeselected = _components
                .Any(c => c.Component == ComponentCatalog.OneDrive && !c.IsSelected);

            // 提前在 UI 线程捕获安装位置（GetSelectedInstallLocation 读 LocationCombo，后台线程不可读）
            string uiCapturedInstallLocation = GetSelectedInstallLocation();
            string uiCapturedArchitecture = SelectedArchitecture;

            var t = new Thread(() =>
            {
                try
                {
                    AppendLog(GetVersionSelfCheck());

                    // 【版本冲突前置拦截】：本机已装 Office 且用户勾选了套件内应用、所选版本 ≠ 已装版本时，
                    // BuildArgs 的 auto-align 会把版本静默替换成本机版本 → 跑完 ODT 实际无变化（NoOp）。
                    // 必须在启动 ODT 之前拦截并提醒；若用户确实想要更低/不同版本，引导其用「切换到其他版本」（= 卸载重装旧版）。
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
                    // 仅当用户勾选了套件内应用（auto-align 才会触发，与 BuildArgs 一致）且所选 ≠ 已装时才拦截，
                    // 避免误拦「仅装独立产品（Visio/Project）到不同版本」这类合法场景。
                    var suiteSel = _components
                        .Where(c => !string.IsNullOrEmpty(c.Component.ExcludeAppId))
                        .ToList();
                    if (suiteSel.Count > 0 && !string.IsNullOrEmpty(installedVersionName) && !string.IsNullOrEmpty(userVersionName)
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
                                "⚠️ 版本冲突：安装将「无变化」\n\n"
                                + "本机已装：" + installedVersionName + "（PID=" + suitePid + "）\n"
                                + "您选择的：" + userVersionName + "\n\n"
                                + "由于本机已装 Office，继续安装会被自动对齐为「" + installedVersionName + "」，"
                                + "实际等同于什么都不做（NoOp）。\n\n"
                                + "若您确实想安装【更低或不同】的版本，请点「否」，随后改用「切换到其他版本」（= 卸载重装旧版）。\n\n"
                                + "仍要继续这次无意义的对齐安装吗？",
                                "版本冲突：安装将无变化",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            return r == MessageBoxResult.Yes;
                        });
                        if (!autoAlignConfirmed)
                        {
                            // 用户选择改用「切换到其他版本」：在 UI 线程打开切换版本对话框
                            this.Dispatcher.Invoke(() => OnSwitchEditionExecute());
                            AppendLog("  [已取消] 版本冲突，已打开「切换到其他版本」供重新部署目标版本。");
                            SetStatus("已打开切换版本");
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
                    // 直接触碰 WPF 绑定属性 IsIndeterminateProgress / ElapsedVisibility，后台线程必须切 UI 线程
                    RunOnUi(() =>
                    {
                        IsIndeterminateProgress = false;
                        ElapsedVisibility = Visibility.Collapsed;
                    });
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
                    // 后台线程直接赋值 WPF 绑定属性 + DispatcherTimer.Start() 会抛线程亲和性异常，
                    // 必须切到创建 _elapsedTimer 的 UI 线程执行
                    RunOnUi(() =>
                    {
                        IsIndeterminateProgress = true;
                        _installStartTime = DateTime.Now;
                        ElapsedVisibility = Visibility.Visible;
                        _elapsedTimer.Start();
                    });
                    SetStatus("正在安装 Office...");

                    // === 安装位置（Office 装到其他盘）===
                    // 用户选了非默认盘时，在跑 ODT 前挂 junction 把 Office 根目录重定向到目标盘。
                    // 约束：只服务「全新安装」——本机已装 Office 时选其他盘无效（迁移未实现），
                    // 此时放弃 junction、按默认 C 盘安装（安全第一，本批不做迁移）。
                    string targetRoot = uiCapturedInstallLocation;
                    bool elevateOdt = false;    // 非管理员且需挂 junction 时，ODT 提权执行（UAC）
                    if (!string.IsNullOrEmpty(targetRoot))
                    {
                        if (IsOfficeInstalled())
                        {
                            AppendLog("  [!] 本机已装 Office：「选其他盘」仅对全新安装有效，迁移功能暂未提供；"
                                + "已放弃 junction，将按默认 C 盘安装（" + targetRoot + " 未使用）。");
                            targetRoot = null;
                        }
                        else
                        {
                            // targetRoot 形如 "D:"（GetFixedDiskDrives 返回盘符+冒号），补反斜杠供 mklink/CreateDirectory 使用
                            string targetDir = targetRoot.EndsWith("\\") ? targetRoot : targetRoot + "\\";
                            string officeRoot = OfficeInstallLocation.GetOfficeRootPath(uiCapturedArchitecture);
                            bool ok = OfficeInstallLocation.EnsureJunction(officeRoot, targetDir, AppendLog);
                            if (ok)
                            {
                                AppendLog("已通过 junction 将 Office 安装位置重定向到 " + targetRoot);
                            }
                            else if (!MemoryAnalyzer.IsAdministrator())
                            {
                                // 当前非管理员（CreateJunction 需写系统盘根目录）→ 改为 UAC 提权跑 ODT，
                                // 由提权子进程内部完成挂 junction + 安装（RunElevated 返回 ODT 退出码）。
                                AppendLog("  [提权] 当前进程非管理员，挂 junction 失败；改为以管理员提权执行 ODT（需 UAC 确认，junction + 安装一并在提权进程完成）");
                                elevateOdt = true;
                            }
                            else
                            {
                                AppendLog("  [!] junction 创建失败（" + officeRoot + " → " + targetRoot + "），已回退默认 C 盘安装");
                            }
                        }
                    }

                    // 提交 ODT 前后各读一次 C2R 台账 ExcludedApps，用于事后校验移除是否真的生效
                    // （退出码 0 已被证明不可信：C2R 引擎未响应时 ODT 空转也返回 0）。
                    var excludedBefore = OdtSetup.ReadExcludedApps();
                    var pidsBefore = OdtSetup.ReadProductReleaseIds();
                    int rc;
                    if (elevateOdt)
                    {
                        // 提权执行：args[0]=setup.exe 完整路径（RunElevated 以 FileName 启动），junction 在提权子进程内完成
                        rc = OfficeInstallLocation.RunElevated(
                            new[] { setup, "/configure", xml },
                            Path.GetDirectoryName(setup) ?? OdtSetup.GetOdtDir(),
                            AppendLog);
                        AppendLog(rc == 0
                            ? "  [完成] 提权执行 ODT 成功，Office 安装至 junction 目标 " + targetRoot
                            : "  [!] 提权执行 ODT 退出码 " + rc + "，详见日志");
                    }
                    else
                    {
                        // 默认 / 已挂 junction（当前进程即管理员）：走原 RunConfig（一行语义不变）
                        // ODT 执行会阻塞数分钟（下载 Office 包）。原 RunConfig 捕获 stdout 到进程结束才输出，
                        // 期间日志完全静默。此处包一层「轮询进度」：RunConfig 放独立后台线程跑，
                        // 轮询线程每 3s 重扫 Office 落盘字节量（基线在启动前取）显示「已写入 N MB」。
                        // expectDownload：提前判定本次配置会不会下载新组件（新装/重装）——会→进度日志显示「已写入 N MB」，
                        // 不会（纯删减/排除/无变更）→显示「组件配置中」（删减 C2R 不下载新字节，显示 0 MB 会误导）。
                        rc = WaitOdtWithProgress(setup, xml, AppendLog, OdtSetup.OdtWillDownload(args, excludedBefore, pidsBefore));
                    }

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

                    // 后台线程：_elapsedTimer.Stop() + 直接赋值 IsIndeterminateProgress 必须切 UI 线程
                    RunOnUi(() =>
                    {
                        _elapsedTimer.Stop();
                        IsIndeterminateProgress = false;
                    });
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
                    // 且 ② ODT 装成功（rc=0）且 ③ 本机确实装了 OneDrive 客户端 时触发。
                    // ODT 只会排 C2R 套件里的 Groove，管不到 OneDriveSetup.exe 安装/更新的这个
                    // MDOB 客户端目录（PF(x86)\Microsoft OneDrive），故须额外卸。先弹二次确认
                    // （说明会卸 OneDrive 客户端、保留同步数据）。措辞中性（不主张套件自带/独立手装，避免归属误判）。
                    // OneDrive 客户端（MDOB/独立）由 OneDriveSetup 管理，可能落在 64 位 PF 或 32 位 PF(x86) 的
                    // \Microsoft OneDrive（版本号子目录），ODT 套件排除 Groove 只管不到它，故须额外卸。
                    if (rc == 0 && onedriveDeselected && OneDriveUninstall.IsStandaloneInstalled())
                    {
                        bool confirmed;
                        string odDesc = "检测到本机装有 OneDrive 客户端（OneDriveSetup 管理，位于 Program Files 或 Program Files (x86) 的 \\Microsoft OneDrive，保留同步数据目录）。\n";
                        try
                        {
                            confirmed = this.Dispatcher.Invoke(() =>
                                MessageBox.Show(
                                    odDesc
                                    + "你已取消勾选 OneDrive 组件，是否一并干净卸载该客户端？\n\n"
                                    + "卸载将：结束进程 → 官方卸载 → 清理程序目录与注册表残留。\n"
                                    + "会保留你的 OneDrive 同步数据目录（%LOCALAPPDATA%\\OneDrive）。\n\n确定继续吗？",
                                    "确认卸载 OneDrive 客户端",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
                        }
                        catch (Exception mbEx) { AppendLog("[!] 确认框异常: " + mbEx.Message); confirmed = false; }

                        if (confirmed)
                        {
                            AppendLog("开始干净卸载 OneDrive 客户端...");
                            try
                            {
                                OneDriveUninstall.Uninstall(AppendLog);
                                SetStatus("✅ 安装完成，并已干净卸载 OneDrive 客户端");
                            }
                            catch (Exception odEx)
                            {
                                AppendLog("[!] OneDrive 客户端卸载异常: " + odEx.Message);
                                SetStatus("⚠️ OneDrive 卸载出错，请查看日志");
                            }
                        }
                        else
                        {
                            AppendLog("  [已跳过] 用户未确认卸载 OneDrive 客户端。");
                        }
                    }
                    } // end if (odtHasWork)

                    // 新版 Outlook（OutlookForWindows）不再由本组件区驱动：用户在新版 Outlook 应用内一键切换获取，
                    // 强力卸载弹窗内单独提供可移除项（见 ExecuteFullUninstall / UninstallConfirmDialog）。
                    SetProgress(100);
                    if (!odtHasWork) SetStatus("✅ 已完成（无变更）");
                }
                catch (Exception ex)
                {
                    AppendLog("[!] 异常: " + ex.Message);
                    SetStatus("❌ 执行出错: " + ex.Message);
                }
                finally
                {
                    // 停止计时器，显示最终耗时
                    // 停止计时器，显示最终耗时（后台线程：_elapsedTimer.Stop() + 绑定属性赋值必须切 UI 线程）
                    var totalElapsed = DateTime.Now - _installStartTime;
                    RunOnUi(() =>
                    {
                        _elapsedTimer.Stop();
                        if (_elapsedVisibility == Visibility.Visible)
                        {
                            ElapsedTime = $"总耗时: {totalElapsed.Minutes}:{totalElapsed.Seconds:D2}";
                        }
                        else
                        {
                            ElapsedVisibility = Visibility.Collapsed;
                            ElapsedTime = "";
                        }
                    });

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
                SetupExePath = file;
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
                // Lync/Teams 不在 UI 目录、恒定排除；OutlookForWindows（新版 MSIX）不是 ODT 套件组件，
                // 不进勾选体系、恒定排除（新版由应用内一键切换获取，强力卸载弹窗内单独移除）。
                // 经典版 Outlook 改回随组件区勾选受控（勾选=保留、不勾选=排除），故不在此强制列表。
                foreach (var forced in new[] { "Lync", "Teams", "OutlookForWindows" })
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
            // 提前在 UI 线程捕获架构（ArchCombo 后台线程不可读），后台线程体改用局部变量
            string uiCapturedArchitecture = archIdx ?? "64";
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
                    AppendLog(GetVersionSelfCheck());
                    SetInstalling(true);
                    SetStatus($"正在切换更新通道（{before} → {targetFinal}，不重装）...");
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
                        Architecture = uiCapturedArchitecture,
                        Channel = string.Empty,
                        Version = "MatchInstalled",
                        UpdateChannel = targetFinal,
                        ForceAppShutdown = true,
                        AcceptEula = true,
                        ValidateChannel = false   // 纯改通道无 Volume 产品，跳过订阅/批量校验
                    };
                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("  已生成仅含 <Updates Channel=\"" + targetFinal + "\"> 的 config.xml");

                    int rc = WaitOdtWithProgress(setup, xml, AppendLog, false); // 纯改通道（MatchInstalled）：不下载新组件，expectDownload=false

                    // 回读注册表「Update Channel」校验是否真的变更（轮询 C2R 台账异步落盘）
                    string after = before;
                    for (int i = 0; i < 4; i++)
                    {
                        if (TryGetInstalledSuite(out _, out var c)) after = c;
                        if (!string.IsNullOrEmpty(after) && !string.Equals(after, before, StringComparison.OrdinalIgnoreCase)) break;
                        System.Threading.Thread.Sleep(2000);
                    }
                    // 后台线程：_elapsedTimer.Stop() + 直接赋值 IsIndeterminateProgress 必须切 UI 线程
                    RunOnUi(() =>
                    {
                        _elapsedTimer.Stop();
                        IsIndeterminateProgress = false;
                    });
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

        /// <summary>
        /// 把注册表读到的「原始通道值」归一化为 ODT 标准通道名（Current/MonthlyEnterprise/SemiAnnual）。
        /// C2R 台账里 UpdateChannel 的取值有三种形态（本机实测）：
        ///   ① ODT 标准名（如 "Current"）——直接保留；
        ///   ② 永久版 PID（如 "PerpetualVL2019"）——原样返回（DetectCurrentChannel 已按 Volume/Perpetual 识别）；
        ///   ③ CDN 端点 URL（如 http://officecdn.microsoft.com/pr/492350f6-…）——按 GUID→通道名映射转友好名。
        /// 注意真实键名是 <b>UpdateChannel（无空格）</b>，旧代码误读带空格的 "Update Channel" 导致永远读到空。
        /// GUID 表来源：微软官方 M365 更新通道文档（officecdn.microsoft.com/pr/&lt;guid&gt; 与各通道对应）。</summary>
        private static string NormalizeRawChannel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            raw = raw.Trim();
            // 形态②：永久版 PID（PerpetualVL2019/2024），直接保留供上层按 Volume 识别
            if (raw.IndexOf("PerpetualVL", StringComparison.OrdinalIgnoreCase) >= 0) return raw;
            // 形态③：CDN URL（或裸 GUID），取 /pr/ 后的 36 位 GUID 段查表
            if (raw.IndexOf("officecdn.microsoft.com", StringComparison.OrdinalIgnoreCase) >= 0
                || System.Text.RegularExpressions.Regex.IsMatch(raw,
                     @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))
            {
                int slash = raw.LastIndexOf('/');
                string guid = slash >= 0 ? raw.Substring(slash + 1) : raw;
                // 去掉可能的查询串
                int q = guid.IndexOf('?');
                if (q > 0) guid = guid.Substring(0, q);
                switch (guid.ToLowerInvariant())
                {
                    case "492350f6-3a01-4f97-b9c0-c7c6ddf67d60": return "Current";
                    case "64256afe-f5d9-4f86-8936-8840a6a4f5be": return "CurrentPreview";
                    case "55336b82-a18d-4dd6-b5f6-9e5095c314a6": return "MonthlyEnterprise";
                    case "7ffbc6bf-bc32-4f92-8982-f9dd17fd3114": return "SemiAnnual";
                    case "b8f9b850-328d-4355-9145-c59439a0c4cf": return "SemiAnnualPreview";
                    default: break;
                }
            }
            // 形态①：已是标准名（Current/MonthlyEnterprise/SemiAnnual/BetaChannel…），原样保留
            return raw;
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
            // 提前在 UI 线程捕获架构与语言（ArchCombo 后台线程不可读），后台线程体改用局部变量
            string uiCapturedArchitecture = ArchCombo.SelectedItem as string ?? "64";
            string uiCapturedLanguage = SelectedLanguage;

            IsIndeterminateProgress = true;
            _installStartTime = DateTime.Now;
            ElapsedVisibility = Visibility.Visible;
            _elapsedTimer.Start();
            var t = new Thread(() =>
            {
                try
                {
                    AppendLog(GetVersionSelfCheck());
                    SetInstalling(true);
                    SetStatus("正在重新部署 Office（" + edName + "）...");
                    AppendLog("==== 切换版本（重新部署）：" + edName + "（" + pid + " / " + ch + "）====");

                    string setup = OdtSetup.Ensure(AppendLog, p => { SetProgress(p); SetStatus($"准备 ODT: {p}%"); });
                    if (setup == null) { AppendLog("  [!] ODT setup.exe 获取失败，切换未执行。"); SetStatus("❌ ODT setup.exe 获取失败"); return; }

                    // 保持原版本的组件集：先读本机 C2R 台账当前套件 PID 的 ExcludedApps（逗号分隔值），
                    // 填入新版本的 OfficeProductConfig.ExcludeApps，使重新部署后组件集与本机一致。
                    // 读不到 C2R 台账时（新装/无台账）不排除，退化为全量安装。
                    // 注意：ReadExcludedApps 的 key 是完整注册表值名（如 "O365ProPlusRetail.ExcludedApps"），
                    // 须用 pid + ".ExcludedApps" 匹配，而非裸 pid。
                    var keepExcluded = new List<string>();
                    try
                    {
                        var excludedDict = OdtSetup.ReadExcludedApps();
                        string suffixKey = pid + ".ExcludedApps";
                        if (excludedDict != null && excludedDict.TryGetValue(suffixKey, out var excludedCsv)
                            && !string.IsNullOrWhiteSpace(excludedCsv))
                        {
                            keepExcluded.AddRange(excludedCsv
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            AppendLog("  [组件保持] 本机排除列表: " + string.Join(",", keepExcluded));
                        }
                        else
                        {
                            AppendLog("  [组件保持] 未找到本机排除列表（新装或无台账），将装全量组件");
                        }
                    }
                    catch { /* 读台账异常不影响切换：退化为全量安装 */ }

                    var args = new OfficeInstallArguments
                    {
                        Architecture = uiCapturedArchitecture,
                        Channel = ch,
                        Version = "MatchInstalled",
                        ForceAppShutdown = true,
                        AcceptEula = true,
                        DisplayFull = false
                    };
                    var productCfg = new OfficeProductConfig
                    {
                        ProductId = pid,
                        Languages = { uiCapturedLanguage }
                    };
                    if (keepExcluded.Count > 0)
                    {
                        productCfg.ExcludeApps.AddRange(keepExcluded);
                        AppendLog("  [组件保持] 沿用本机原排除列表（" + string.Join(",", keepExcluded) + "）");
                    }
                    args.Products.Add(productCfg);

                    // 提交前再读一次 C2R 台账快照，用于 ODT 跑完后判定台账是否已落盘（异步写入）。
                    var excludedBefore = OdtSetup.ReadExcludedApps();

                    string xml = ConfigXmlBuilder.Build(args);
                    AppendLog("  已生成重新部署 config.xml（" + pid + " / " + ch + "）");

                    int rc = WaitOdtWithProgress(setup, xml, AppendLog, false); // 重新部署/改通道（MatchInstalled）：不下载新组件，expectDownload=false
                    // 后台线程：_elapsedTimer.Stop() + 直接赋值 IsIndeterminateProgress 必须切 UI 线程
                    RunOnUi(() =>
                    {
                        _elapsedTimer.Stop();
                        IsIndeterminateProgress = false;
                    });
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
                    // C2R 台账写入是引擎异步落盘的，ODT 返回瞬间可能还没写完。
                    // 轮询最多 3 次（每次间隔 2 秒，总计约 6 秒）等台账变化后再刷新 UI，
                    // 与主安装流程（1163-1172 行）一致；前后相等说明台账已不再变化，提前结束。
                    var excludedAfter = OdtSetup.ReadExcludedApps();
                    for (int i = 0; i < 3 && DictValuesEqual(excludedBefore, excludedAfter); i++)
                    {
                        System.Threading.Thread.Sleep(2000);
                        excludedAfter = OdtSetup.ReadExcludedApps();
                    }
                    DetectCurrentChannel();
                    // 刷新版本下拉框：ODT 重新部署完成后 C2R 台账已更新，重读套件 PID
                    // 同步 EditionCombo / SelectedEdition，让「版本」字段显示真实当前版本。
                    try
                    {
                        if (TryGetInstalledSuite(out var newPid, out _))
                        {
                            int newEd = MapProductToEdition(newPid);
                            if (newEd >= 0 && newEd < OfficeInstall.Editions.Length)
                            {
                                SelectedEdition = newEd;
                                RunOnUi(() => { EditionCombo.SelectedIndex = newEd; });
                                AppendLog("  [版本刷新] 当前已装版本下拉已同步为 " + OfficeInstall.Editions[newEd]);
                            }
                        }
                    }
                    catch { /* 刷新失败不影响主流程：台账已刷新，仅下拉显示滞后 */ }
                }
                catch (Exception ex) { AppendLog("  [!] 切换版本异常: " + ex.Message); AppendLog("  [堆栈] " + ex.StackTrace); SetStatus("❌ 切换异常"); }
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
            ok.Click += (_, __) => w.DialogResult = true;   // 修复：点「确定」关闭对话框并返回选择
            root.Children.Add(btns);
            w.Content = root;

            if (w.ShowDialog() != true || list.SelectedIndex < 0) return null;
            return OfficeInstall.Editions[list.SelectedIndex];
        }

        #endregion

        #endregion

        /// <summary>后台线程里跑 ODT：RunConfig 放独立后台线程（阻塞到 ODT 结束），
        /// 另起进度轮询线程每 3s 重扫 Office 落盘字节量（基线在 ODT 启动前取）驱动进度。
        /// ODT 引擎没有百分比查询接口（setup.exe /? 仅 /download /configure /customize /help，
        /// 旧版 /progress 开关已不存在），故用「执行前后 Office 落盘字节差量」作真实进度指标：
        /// 状态栏显示「已写入 N MB」（高水位、只升不降，防 temp→root 拷贝期双计/清理期回落），
        /// 每 200MB 打一条日志；轮询默认 3s，单次扫描过慢时自动放宽到 5/10s（防大 Office/HDD 上与下载抢 I/O）。
        /// 通道切换/减组件等场景差量 ≤ 0，状态显示「配置变更中」。ODT 结束后恢复进度条到 100。返回 ODT 退出码。</summary>
        private int WaitOdtWithProgress(string setup, string xml, Action<string> log, bool expectDownload)
        {
            int rc = 0;
            // 基线必须在 ODT 启动前取（此刻 C2R agent 尚未开始本次下载）
            long baseBytes = OdtSetup.OverlayStoreBytesSnapshot();
            var odtThread = new System.Threading.Thread(() => { rc = OdtSetup.RunConfig(setup, xml, log); })
            { IsBackground = true };
            odtThread.Start();
            // 开头日志按「会不会下载」切措辞：下载期 → “正在下载/安装”；删减/排除/无变更 → “正在应用组件配置”
            log(expectDownload
                ? "ODT 引擎运行中（正在下载/安装 Office 组件，可能需数分钟）..."
                : "ODT 引擎运行中（正在应用组件配置：删减/排除，无新增下载，可能需数分钟）...");
            if (baseBytes >= 0)
                log("  [*] ODT 无百分比接口，进度以磁盘实测写入量显示（每 3s 重扫）");
            else
            {
                log("  [!] 落盘字节基线读取失败，本次仅显示 ODT 运行状态");
                // 基线不可用时轮询线程不再刷状态，避免状态栏卡在前置文案（如"准备中..."）
                SetStatus("ODT 运行中（无法统计写入量，计时中）...");
            }

            // 进度轮询线程：每 3s 重扫 Office root + C2R 下载临时目录（实测 4.7GB/1.1 万文件约 340ms，可承受）。
            // 纯本地文件统计，无副作用（旧方案起 setup.exe 副进程查 /progress，ODT 单实例锁下会阻塞整个执行期）。
            //
            // 多线程下载不影响统计准确性（落盘字节是物理量，线程多只是涨得快），但有一个失真点：
            // C2R delta 安装时先下载到 OfficeC2R* temp、再拷入 root、最后删 temp——重叠期双计、
            // 清理后总量回落，「已写入 MB」会看起来倒退。故用高水位（历史最大差量）驱动显示/日志，保证不回退。
            long maxWritten = 0; // 高水位，只升不降
            int sleepMs = 3000; // 自适应刷新间隔：扫描慢了放宽，避免扫盘与 C2R 下载抢 I/O
            bool slowLogged = false;
            // —— 周期性「一直在跑」日志：每 10s 写「已用 Xs · 已写入 N MB」；外加状态栏「校验/对齐中」标注 ——
            long odtStartTick = Environment.TickCount64; // 本次 ODT 计时基准（自包含，不依赖卸载路径可能未设的 _installStartTime）
            long lastAliveTick = odtStartTick;           // 上次打周期日志的时刻
            int lastSeenMb = -1;                         // 最近一次见到的 mb 值（用于判断「有无进展」）
            long lastProgressTick = odtStartTick;         // 上次 mb 发生变化时的基准时刻
            var pollThread = new System.Threading.Thread(() =>
            {
                while (odtThread.IsAlive)
                {
                    try
                    {
                        if (baseBytes >= 0)
                        {
                            long sw0 = Environment.TickCount64;
                            long total = OdtSetup.OverlayStoreBytesSnapshot();
                            long scanMs = Environment.TickCount64 - sw0;
                            // 扫描耗时 ∝ Office 体量（本机 4.7G 约 340ms；大机/HDD 可到 1s+）：
                            // >1.5s 放宽到 10s，>800ms 放宽到 5s，否则恢复 3s（快机可恢复）
                            if (scanMs > 1500) sleepMs = 10000;
                            else if (scanMs > 800) sleepMs = 5000;
                            else sleepMs = 3000;
                            if (sleepMs > 3000 && !slowLogged)
                            {
                                slowLogged = true;
                                log("  [*] 磁盘扫描耗时 " + (scanMs / 1000.0).ToString("0.#") + "s，进度刷新间隔自动放宽至 " + (sleepMs / 1000) + "s（避免与下载抢 I/O，数字本身不受影响）");
                            }
                            if (total >= 0)
                            {
                                long written = total - baseBytes;
                                if (written > maxWritten) maxWritten = written;
                                int mb = (int)(maxWritten / 1048576);
                                long nowT = Environment.TickCount64;

                                // 追踪「进展」：mb 变化才更新基准时刻；30s 无新写入 = 校验/对齐/下载尾段
                                if (mb != lastSeenMb) { lastSeenMb = mb; lastProgressTick = nowT; }
                                bool verifying = (nowT - lastProgressTick >= 30000);

                                // 状态栏（每次轮询刷新）：
                                //  · 下载期（expectDownload）：有进展→「下载/安装中」；30s 无新写入→「校验/对齐中」
                                //  · 无新增下载（删减/排除/改通道）：统一「正在应用组件配置」
                                if (expectDownload)
                                {
                                    if (verifying)
                                        SetStatus($"ODT 校验/对齐配置中，已写入 {mb} MB（30s 无新写入，属正常：校验、配置对齐或下载尾段）");
                                    else if (written < 0 && maxWritten == 0)
                                        SetStatus("ODT 应用配置变更中（落盘字节减少，如通道切换/减组件）");
                                    else
                                        SetStatus($"ODT 下载/安装中，已写入 {mb} MB");
                                }
                                else
                                {
                                    SetStatus(written < 0 && maxWritten > 0
                                        ? $"ODT 正在应用组件配置（删减/排除），落盘字节变动中，当前 {mb} MB"
                                        : "ODT 正在应用组件配置（删减/排除，无新增下载）");
                                }

                                // 周期性「一直在跑」日志：每 10s 一条。下载期 →「已写入 N MB」；无新增下载 →「组件配置中」
                                // （删减/排除不会下载新字节，写「0 MB」会误导）。
                                if (nowT - lastAliveTick >= 10000)
                                {
                                    lastAliveTick = nowT;
                                    int sec = (int)((nowT - odtStartTick) / 1000);
                                    log(expectDownload
                                        ? "  [ODT] 已用 " + sec + "s · 已写入 " + mb + " MB"
                                        : "  [ODT] 已用 " + sec + "s · 组件配置中");
                                }
                            }
                        }
                    }
                    catch { /* 快照失败：跳过本轮，不中断轮询 */ }
                    System.Threading.Thread.Sleep(sleepMs);
                }
            }) { IsBackground = true };
            pollThread.Start();

            odtThread.Join();
            // ODT 结束后等待轮询线程自然退出（下一轮 3s 内检测到 IsAlive=false）
            pollThread.Join(5000);
            RunOnUi(() => { IsIndeterminateProgress = false; });
            SetProgress(100);
            return rc;
        }

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
            void DoLog()
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
            }
            // UI 线程直接执行（日志框即时刷新）；后台线程用 BeginInvoke 非阻塞投递，
            // 避免同步 Invoke 在 UI 线程忙时死锁/抛线程亲和性异常。
            if (Dispatcher.CheckAccess()) DoLog();
            else Dispatcher.BeginInvoke(DoLog);
        }

        private void SetProgress(int value)
        {
            int v = Math.Max(0, Math.Min(100, value));
            if (Dispatcher.CheckAccess()) { ProgressValue = v; }
            else Dispatcher.BeginInvoke(() => ProgressValue = v);
        }

        /// <summary>返回「版本自检」一行：vX.Y.Z + exe 最后修改时间 + SHA256 前 8 位。用于日志首行，让用户确认跑的是哪个版本 exe。</summary>
        private static string GetVersionSelfCheck()
        {
            try
            {
                string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
                string loc = Environment.ProcessPath;
                if (string.IsNullOrEmpty(loc)) loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc) || !File.Exists(loc))
                {
                    return "自检: v" + ver + " | 构建未知 | SHA ?";
                }
                string mtime = new FileInfo(loc).LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                string sha = "";
                try
                {
                    using var fs = new FileStream(loc, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var h = string.Concat(System.Security.Cryptography.SHA256.HashData(fs).Take(4).Select(b => b.ToString("x2")).ToArray());
                    sha = h;
                }
                catch { /* 读不到 SHA 不影响自检 */ }
                return $"自检: v{ver} | 构建 {mtime} | SHA {sha}";
            }
            catch { return "自检: 版本信息读取失败"; }
        }

        private void SetStatus(string msg)
        {
            if (Dispatcher.CheckAccess()) { StatusMessage = msg; }
            else Dispatcher.BeginInvoke(() => StatusMessage = msg);
        }

        private void SetInstalling(bool val)
        {
            // 必须用 BeginInvoke（非阻塞）：若用 Invoke（同步），后台线程每次调用都会占住 UI 线程消息泵，
            // DispatcherTimer（_elapsedTimer 的 tick）正是在消息泵里触发的，被挤掉后"已用 X 秒"数字停止更新。
            Dispatcher.BeginInvoke(() =>
            {
                _isInstalling = val;
                OnPropertyChanged(nameof(IsInstalling));
                // 通知 Command 重新评估 CanExecute
                CommandManager.InvalidateRequerySuggested();
            });
        }

        /// <summary>
        /// 线程安全的 UI 线程执行器：把直接触碰 WPF 绑定属性（IsIndeterminateProgress /
        /// ElapsedVisibility / ElapsedTime）与 DispatcherTimer（_elapsedTimer.Start/Stop）的
        /// 操作统一收口到创建它们的 UI 线程。
        /// 若当前已在 UI 线程（如切换通道/切换版本在 new Thread 之前、UI 线程里执行）则直接跑，
        /// 避免不必要的投递开销；若在后台线程（安装/卸载工作线程体）则切到 UI 线程。
        /// 后台线程用 BeginInvoke 非阻塞投递，避免 Dispatcher.Invoke 跨线程同步死锁。
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.BeginInvoke(action);   // 非阻塞投递，避免跨线程同步 Invoke 死锁
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
