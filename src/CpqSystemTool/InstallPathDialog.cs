using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CpqSystemTool
{
    /// <summary>
    /// 自定义安装路径对话框：点"安装选中"时弹出，询问是否指定安装目录。
    /// - 留空 / 勾选"使用默认路径" → 各软件默认安装目录
    /// - 输入路径 → 注入到 NSIS(/D=) / Inno(/DIR=) 安装器
    /// 记忆上次输入（HKCU\Software\CpqSystemTool\InstallPath）。
    ///
    /// 视觉：无系统边框 + 自绘标题栏 + 圆角阴影卡，复用主界面主题笔刷(_windowBg/_accent/...) 与 Btn 基元，
    /// 与主界面卡片语言完全一致；错误用内联提示（替代原生 MessageBox，避免风格割裂）。
    /// </summary>
    public class InstallPathDialog : Window
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _useDefault;
        private readonly TextBlock _errText;
        public string InstallPath { get; private set; }   // null/空 = 使用默认路径
        public bool UseDefault { get; private set; } = true;

        private const string REG_KEY = @"Software\CpqSystemTool";
        private const string REG_VALUE = "InstallPath";

        /// <param name="owner">主窗口引用，用于读取主题颜色并复用 Btn 基元（Owner 另由调用方 .Owner 设置）</param>
        public InstallPathDialog(MainWindow owner)
        {
            // 无系统边框，自绘标题栏 + 圆角阴影卡，与主界面卡片语言统一
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;   // 透明窗口，使圆角阴影卡浮起可见（Background=Transparent，内层卡自带底色）
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            var dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            var warn = owner?._warnOrange ?? new SolidColorBrush(Color.FromRgb(0xB4, 0x5A, 0x22));
            var danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));

            Background = Brushes.Transparent;   // 透明窗口，让圆角阴影卡浮起可见（卡片底色由内层 Border 提供）
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            // 复用主界面全局圆角按钮样式（与 CustomSoftwareDialog 同源）：注入 ControlTemplate + 同名主题笔刷
            DialogChrome.Apply(this, owner);

            // 外层圆角阴影卡
            var root = new Border
            {
                Background = windowBg,
                BorderBrush = panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                }
            };

            var stack = new StackPanel();

            // ===== 自定义标题栏（可拖拽 + 关闭按钮）=====
            stack.Children.Add(DialogChrome.BuildTitleBar(this, "自定义安装路径", fg, dim, danger, panelBorder));

            // ===== 内容区 =====
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            body.Children.Add(new TextBlock
            {
                Text = "自定义安装目录",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Margin = new Thickness(0, 0, 0, 6)
            });
            body.Children.Add(new TextBlock
            {
                Text = "留空或勾选“使用默认路径”时，软件将安装到各自默认位置。\n当前仅对 NSIS / Inno Setup 类安装器生效（如 7-Zip、Notepad3、PotPlayer），其他软件仍走默认位置。",
                FontSize = 12,
                Foreground = dim,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 14)
            });

            _useDefault = new CheckBox
            {
                Content = "使用各软件默认安装路径",
                IsChecked = true,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = fg
            };
            _useDefault.Checked += (s, e) => _pathBox.IsEnabled = false;
            _useDefault.Unchecked += (s, e) => _pathBox.IsEnabled = true;
            body.Children.Add(_useDefault);

            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _pathBox = new TextBox
            {
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 8, 6),
                IsEnabled = false,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = owner?._inputBg ?? Brushes.Transparent,
                Foreground = owner?._inputFg ?? fg,
                BorderBrush = owner?._accent,
                BorderThickness = new Thickness(1)
            };
            _pathBox.CaretBrush = owner?._accent;
            _pathBox.GotFocus += (s, e) => _pathBox.BorderBrush = owner?._accent;
            _pathBox.LostFocus += (s, e) => _pathBox.BorderBrush = owner?._accent;
            Grid.SetColumn(_pathBox, 0);
            pathRow.Children.Add(_pathBox);

            var browseBtn = owner != null
                ? owner.Btn("浏览…", false, () =>
                {
                    var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "选择软件安装目录",
                        ShowNewFolderButton = true
                    };
                    if (!string.IsNullOrEmpty(_pathBox.Text) && Directory.Exists(_pathBox.Text))
                        dlg.SelectedPath = _pathBox.Text;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _pathBox.Text = dlg.SelectedPath;
                        _useDefault.IsChecked = false;  // 选了路径就取消"默认"
                    }
                }, 90)
                : new Button { Content = "浏览…", Width = 90 };
            Grid.SetColumn(browseBtn, 1);
            pathRow.Children.Add(browseBtn);
            body.Children.Add(pathRow);

            // 内联错误提示（替代原生 MessageBox，风格与主界面统一）
            _errText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = danger,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 0, 4),
                Visibility = Visibility.Collapsed
            };
            body.Children.Add(_errText);

            body.Children.Add(new TextBlock
            {
                Text = "注意：路径不能包含空格（NSIS /D= 参数限制），建议用 D:\\Softwares 这类短目录。",
                FontSize = 11,
                Foreground = warn,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 0, 14)
            });

            // 按钮行（复用主界面 Btn 基元，配色完全一致）
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            var cancelBtn = owner != null ? owner.Btn("取消", false, () => { DialogResult = false; }, 110)
                                           : new Button { Content = "取消", Width = 110 };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            var okBtn = owner != null ? owner.Btn("确定", true, () =>
            {
                UseDefault = _useDefault.IsChecked == true;
                _errText.Visibility = Visibility.Collapsed;
                if (!UseDefault)
                {
                    string p = (_pathBox.Text ?? "").Trim();
                    if (string.IsNullOrEmpty(p))
                    {
                        ShowError("请输入安装路径，或勾选“使用各软件默认安装路径”。");
                        return;
                    }
                    try { Path.GetFullPath(p); }
                    catch (Exception caughtEx)
                    {
                        System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);
                        ShowError("路径格式无效。");
                        return;
                    }
                    InstallPath = p;
                    // 记忆路径
                    try
                    {
                        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_KEY))
                            if (k != null) k.SetValue(REG_VALUE, p, Microsoft.Win32.RegistryValueKind.String);
                    }
                    catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
                }
                DialogResult = true;
            }, 110) : new Button { Content = "确定", Width = 110 };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(okBtn);
            body.Children.Add(btnRow);

            stack.Children.Add(body);
            root.Child = stack;
            Content = root;

            // 恢复上次记忆的路径
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_KEY))
                {
                    var saved = k?.GetValue(REG_VALUE) as string;
                    if (!string.IsNullOrEmpty(saved))
                    {
                        _pathBox.Text = saved;
                        _useDefault.IsChecked = false;  // 有记忆 → 默认取消"使用默认"，预填上次路径
                    }
                }
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
        }

        /// <summary>内联错误提示：统一淡入（替代原生 MessageBox，风格与主界面一致）。</summary>
        private void ShowError(string message)
        {
            DialogChrome.ShowError(_errText, message);
        }
    }
}
