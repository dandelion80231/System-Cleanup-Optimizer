using System;
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
                // 默认勾选前 4 个（Word/Excel/PowerPoint/Outlook）
                item.IsSelected = (name == "Word" || name == "Excel" || name == "PowerPoint" || name == "Outlook");
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

        #endregion

        #region -- Command handlers --

        private void OnUninstallExecute()
        {
            var t = new Thread(() =>
            {
                try
                {
                    SetInstalling(true);
                    SetStatus("正在卸载 Office（C2R）...");
                    AppendLog("开始强力卸载 Office...");
                    OfficeInstall.Uninstall(AppendLog);
                    SetStatus("✅ 卸载完成");
                    AppendLog("  [完成] 卸载结束");
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
            }) { IsBackground = true, Name = "OfficeUninstallWorker" };
            t.Start();
            SetStatus("准备中...");
        }

        private void OnInstallExecute()
        {
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

            var selected = _components.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                var first = _components.FirstOrDefault();
                if (first != null) selected = new[] { first }.ToList();
            }

            // 分组：套件组件（用 ExcludeApp 控制）vs 独立产品（用 StandaloneProductId）
            var suiteComponents = selected
                .Where(c => !string.IsNullOrEmpty(c.Component.ExcludeAppId))
                .ToList();
            var standaloneComponents = selected
                .Where(c => !string.IsNullOrEmpty(c.Component.StandaloneProductId))
                .ToList();

            // 套件组件合并到单个 Product 节点，ProductId 取所选版本（如 O365ProPlusRetail / ProPlus2024Retail / ProPlus2021Volume …）
            if (suiteComponents.Count > 0)
            {
                var product = new OfficeProductConfig
                {
                    ProductId = OfficeInstall.ProductIds[SelectedEdition],
                    Languages = { SelectedLanguage }
                };
                foreach (var item in suiteComponents)
                    product.ExcludeApps.Add(item.Component.ExcludeAppId);
                args.Products.Add(product);
            }

            // 独立产品各自一个 Product 节点
            foreach (var item in standaloneComponents)
            {
                var product = new OfficeProductConfig
                {
                    ProductId = item.Component.StandaloneProductId,
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
