using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 对话框按钮 hover 反馈（与主窗口行为一致：悬浮时轻微变暗，离开复原）
    /// </summary>
    internal static class DialogBtnFx
    {
        internal static void HoverLift(this System.Windows.Controls.Button b)
        {
            b.MouseEnter += (s, e) => { if (b.IsEnabled) b.Opacity = 0.82; };
            b.MouseLeave += (s, e) => { b.Opacity = 1.0; };
        }

        /// <summary>
        /// 生成带圆角边框的 Button ControlTemplate（独立 Window 不继承主窗口 XAML Style）
        /// </summary>
        internal static ControlTemplate RoundedTemplate(CornerRadius radius)
        {
            var tmpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, radius);
            var bgBinding = new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
            border.SetBinding(Border.BackgroundProperty, bgBinding);
            var bbBinding = new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
            border.SetBinding(Border.BorderBrushProperty, bbBinding);
            var btBinding = new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
            border.SetBinding(Border.BorderThicknessProperty, btBinding);
            var padBinding = new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
            border.SetBinding(Border.PaddingProperty, padBinding);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            tmpl.VisualTree = border;
            return tmpl;
        }
    }

    /// <summary>
    /// "其他优化项" 对话框 — 完全对齐 Win11EasyConfig Form4 + ZyperWin Others
    /// </summary>
    public class OtherTweaksDialog : Window
    {
        private readonly MainWindow _owner;
        private Brush _bg, _fg, _accent, _cardBg, _cardBorder, _dimText, _rowHover;
        private Brush _success, _danger, _btnFg, _btnSec, _inputBg, _inputFg;

        public OtherTweaksDialog(MainWindow owner)
        {
            _owner = owner;
            _bg = _owner._windowBg;
            _fg = _owner._textMain;
            _accent = _owner._accent;
            _cardBg = _owner._bgCard;
            _cardBorder = _owner._panelBorder;
            _dimText = _owner._textDim;
            _rowHover = _owner._rowHover;
            _success = _owner._successGreen;
            _danger = _owner._dangerRed;
            _btnFg = _owner._btnPrimaryFg;
            _btnSec = _owner._btnSecondaryBg;
            _inputBg = _owner._inputBg;
            _inputFg = _owner._inputFg;

            Title = "系统功能调节";
            Width = 540;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            Background = _bg;
            Foreground = _fg;
            FontFamily = new FontFamily("Microsoft YaHei");

            // 根容器：Grid（底层背景图 + 上层内容）
            var rootGrid = new Grid();

            // 背景图层：直接加载与主窗口相同的背景图案（六边形蜂窝）
            var bgImg = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            try
            {
                // 直接复用主窗口 BgImage 的 Source（已由 Theme.cs 加载好）
                // 不复制 Source 对象引用（Freeze 后可能跨线程问题），而是重新加载同一资源
                var ownerSrc = _owner.BgImage.Source;
                if (ownerSrc != null)
                {
                    // 从已有 Source 获取 UriSource，重新创建 BitmapImage
                    var bmi = ownerSrc as System.Windows.Media.Imaging.BitmapImage;
                    if (bmi?.UriSource != null)
                    {
                        var img = new System.Windows.Media.Imaging.BitmapImage(bmi.UriSource);
                        img.Freeze();
                        bgImg.Source = img;
                    }
                    else
                    {
                        // 非 URI 来源（如自定义图片），直接复用（只读共享）
                        bgImg.Source = ownerSrc;
                    }
                    bgImg.Opacity = _owner.BgImage.Opacity;
                }
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  /* 无背景图时静默降级为纯色 */ }
            rootGrid.Children.Add(bgImg);

            // 内容层：ScrollViewer（背景透明，让底层六边形图案透出来；卡片自带 _cardBg）
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12), Background = System.Windows.Media.Brushes.Transparent };
            var root = new StackPanel();

            // 第1组：电源管理
            root.Children.Add(SectionHeader("电源管理"));
            AddToggle(root, "系统休眠",
                "启用/禁用系统休眠。禁用将删除 hiberfil.sys 释放磁盘空间",
                () => System.IO.File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\..\\hiberfil.sys"),
                on => Exec.RunCmd(new[] { "cmd", "/c", on ? "powercfg /h on" : "powercfg /h off" }, _ => { }));
            AddToggle(root, "快速启动",
                "启用/禁用混合启动（启用后加快开机速度）",
                () => { try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power")) return k?.GetValue("HiberbootEnabled") is int v && v == 1; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } },
                on => RegistryHelper.SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", on ? 1 : 0, _ => { }));

            // 第2组：SysMain 服务及关联
            root.Children.Add(SectionHeader("SysMain 服务管理"));
            AddToggle(root, "SysMain 服务",
                "SysMain (Superfetch) 服务。禁用后内存压缩/预启动/页面合并将不可用",
                () => { try { using (var sc = new System.ServiceProcess.ServiceController("SysMain")) return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } },
                on => Exec.RunCmd(new[] { "cmd", "/c", on ? "sc config SysMain start=auto && sc start SysMain" : "sc stop SysMain && sc config SysMain start=disabled" }, _ => { }));
            AddToggle(root, "内存压缩",
                "MemoryCompression 使用 CPU 压缩内存以节省物理内存（需SysMain已启用）",
                () => { var o = Exec.RunPowerShellGet("(Get-MMAgent).MemoryCompression", null); return o?.Trim() == "True"; },
                on => Exec.RunPowerShell((on ? "enable" : "disable") + "-mmagent -MemoryCompression", _ => { }));
            AddToggle(root, "应用预启动",
                "ApplicationPreLaunch 根据使用习惯预启动应用（需SysMain已启用）",
                () => { var o = Exec.RunPowerShellGet("(Get-MMAgent).ApplicationPreLaunch", null); return o?.Trim() == "True"; },
                on => Exec.RunPowerShell((on ? "enable" : "disable") + "-mmagent -ApplicationPreLaunch", _ => { }));
            AddToggle(root, "页面合并",
                "PageCombining 合并物理内存中相同内容的页面以降低内存使用",
                () => { var o = Exec.RunPowerShellGet("(Get-MMAgent).PageCombining", null); return o?.Trim() == "True"; },
                on => Exec.RunPowerShell((on ? "enable" : "disable") + "-mmagent -PageCombining", _ => { }));

            // 第3组：远程管理
            root.Children.Add(SectionHeader("远程管理"));
            AddToggle(root, "远程协助",
                "允许/禁止远程协助连接到此计算机",
                () => { try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Remote Assistance")) return k?.GetValue("fAllowToGetHelp") is int v && v == 1; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } },
                on => RegistryHelper.SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", on ? 1 : 0, _ => { }));
            AddToggle(root, "远程桌面",
                "启用/禁用远程桌面连接",
                () => { try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server")) return k?.GetValue("fDenyTSConnections") is int v && v == 0; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } },
                on => RegistryHelper.SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", on ? 0 : 1, _ => { }));
            AddButtonItem(root, "远程桌面端口设置",
                "更改远程桌面监听端口（默认3389），会自动添加防火墙规则",
                "更改端口", () =>
                {
                    var dlg = new RemotePortDialog(_owner);
                    dlg.Owner = this;
                    dlg.ShowDialog();
                });

            // 第4组：系统维护
            root.Children.Add(SectionHeader("系统维护"));
            AddButtonItem(root, "清除系统日志",
                "清空所有 Windows 事件日志（EventLog 服务）",
                "清除日志", () =>
                {
                    if (MessageBox.Show("确定要完全清除Windows系统日志吗？", "清除系统日志", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        // 先枚举全部日志名，再逐条 wevtutil cl "日志名" 清空（wevtutil cl 需要日志名作参数，不能从管道读）
                        Cleanup.EventLogs(_ => { });
                    }
                });
            AddButtonItem(root, "刷新 DNS 解析缓存",
                "清空 DNS 客户端缓存",
                "刷新DNS", () => Exec.RunCmd(new[] { "cmd", "/c", "ipconfig /flushdns" }, _ => { }));
            AddButtonItem(root, "刷新桌面图标缓存",
                "刷新系统图标缓存（SHChangeNotify）",
                "刷新", () => Exec.RunCmd(new[] { "cmd", "/c", "ie4uinit.exe -show" }, _ => { }));
            AddButtonItem(root, "重启资源管理器",
                "终止并重新启动 Explorer.exe",
                "重启", () =>
                {
                    Exec.RunCmd(new[] { "taskkill", "/f", "/im", "explorer.exe" }, _ => { });
                    try { System.Diagnostics.Process.Start("explorer.exe"); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  }
                });
            AddButtonItem(root, "修改 HOSTS 文件",
                "用记事本打开 %SystemRoot%\\drivers\\etc\\hosts",
                "打开HOSTS", () => Process.Start("notepad", Environment.SystemDirectory + "\\drivers\\etc\\hosts"));
            AddButtonItem(root, "事件查看器",
                "打开 Windows 事件查看器",
                "打开", () => Process.Start("eventvwr.msc"));

            // 第5组：系统保护
            root.Children.Add(SectionHeader("系统保护"));
            AddToggle(root, "禁止 UCPD 驱动",
                "UCPD (User Choice Protection Driver) 防止第三方修改注册表。关闭 UCPD 使优化项成功设置",
                () => { try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UCPD")) return k?.GetValue("Start") is int v && v == 4; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return false; } },
                on => Exec.RunCmd(new[] { "cmd", "/c", on ? "sc config UCPD start= disabled" : "sc config UCPD start= auto && sc start UCPD" }, _ => { }));

            var closeBtn = new Button
            {
                Content = "关闭",
                Width = 90, Height = 32,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = _accent,
                Foreground = _btnFg,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderBrush = _cardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 4, 16, 4)
            };
            closeBtn.Template = DialogBtnFx.RoundedTemplate(new CornerRadius(6));
            closeBtn.Click += (s, e) => Close();
            closeBtn.HoverLift();
            root.Children.Add(closeBtn);

            scroll.Content = root;
            rootGrid.Children.Add(scroll);
            Content = rootGrid;
        }

        /// <summary>
        /// 生成带圆角边框的 Button ControlTemplate（独立 Window 不继承主窗口 XAML Style）
        /// 已移至 DialogBtnFx.RoundedTemplate() 共享方法
        /// </summary>

        private Border SectionHeader(string text)
        {
            return new Border
            {
                Background = _cardBg,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 6, 0, 3),
                Child = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = _accent }
            };
        }

        /// <summary>
        /// 共享行 builder：统一构建「卡片 Border + 两列 Grid(标题区|按钮) + 整行悬浮高亮 + 分隔线」，
        /// 消除 AddToggle / AddButtonItem 的重复脚手架（纯重构，行为不变）。
        /// getState/apply 同时非 null → 开关项（含 [已启用]/[已禁用] 状态标签与异步切换逻辑）；
        /// 否则 → 纯按钮项（点击执行 onClick）。
        /// </summary>
        private void AddRow(StackPanel parent, string title, string desc, string btnText,
            Action onClick, Func<bool> getState = null, Action<bool> apply = null)
        {
            bool isToggle = getState != null && apply != null;
            bool initial = false;
            if (isToggle) { try { initial = getState(); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  } }

            // 外层卡片 Border（保持圆角+边框）
            var card = new Border
            {
                Background = _cardBg,
                BorderBrush = _cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 0)
            };

            // Grid 两列布局：左侧标题+描述（Star） | 右侧按钮（Auto）
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧：标题（折叠于 WrapPanel，开关项附状态标签）+ 描述
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleSp = new WrapPanel { Margin = new Thickness(0, 0, 0, 3) };
            titleSp.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _fg, VerticalAlignment = VerticalAlignment.Center });
            TextBlock stateTag = null;
            if (isToggle)
            {
                // 行内状态标签（替代原来的 ✔/✘ 大徽章）
                stateTag = new TextBlock
                {
                    Text = initial ? "[已启用]" : "[已禁用]",
                    Foreground = initial ? _success : _danger,
                    FontSize = 11,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                titleSp.Children.Add(stateTag);
            }
            info.Children.Add(titleSp);
            info.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = _dimText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            // 右侧按钮（文字风格，与服务页 Btn() 一致）
            var btn = new Button
            {
                Content = isToggle ? (initial ? "禁用" : "启用") : btnText,
                MinWidth = 60, Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = _btnSec,
                Foreground = _fg,
                FontWeight = FontWeights.Normal,
                FontSize = 12,
                BorderBrush = _cardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            btn.Template = DialogBtnFx.RoundedTemplate(new CornerRadius(4));
            Grid.SetColumn(btn, 1);
            grid.Children.Add(btn);

            // 整行悬浮高亮（与服务页一致）
            grid.MouseEnter += (s, e) => { if (card.Background == _cardBg) card.Background = _rowHover; };
            grid.MouseLeave += (s, e) => { card.Background = _cardBg; };

            // 分隔线：除第一项外，每张卡片前加一条（避免末尾多余分隔线出现在关闭按钮上方）
            if (parent.Children.Count > 0)
                parent.Children.Add(new Separator { Margin = new Thickness(0, 3, 0, 3), Background = _cardBorder });

            card.Child = grid;
            parent.Children.Add(card);

            btn.Click += (s, e) =>
            {
                if (isToggle)
                {
                    btn.IsEnabled = false;
                    btn.Content = "⏳";
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            bool wasOn = initial;
                            if (wasOn)
                            {
                                apply(false);
                                Dispatcher.Invoke(() =>
                                {
                                    stateTag.Text = "[已禁用]";
                                    stateTag.Foreground = _danger;
                                    btn.Content = "启用";
                                    btn.Foreground = _fg;
                                    initial = false;
                                    btn.IsEnabled = true;
                                });
                            }
                            else
                            {
                                apply(true);
                                Dispatcher.Invoke(() =>
                                {
                                    stateTag.Text = "[已启用]";
                                    stateTag.Foreground = _success;
                                    btn.Content = "禁用";
                                    btn.Foreground = _fg;
                                    initial = true;
                                    btn.IsEnabled = true;
                                });
                            }
                        }
                        catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); 
                            Dispatcher.Invoke(() => { btn.Content = "❌ 失败"; btn.IsEnabled = true; });
                        }
                    });
                }
                else
                {
                    onClick();
                }
            };
            btn.HoverLift();
        }

        private void AddToggle(StackPanel parent, string title, string desc, Func<bool> getState, Action<bool> apply)
            => AddRow(parent, title, desc, null, null, getState, apply);

        private void AddButtonItem(StackPanel parent, string title, string desc, string btnText, Action onClick)
            => AddRow(parent, title, desc, btnText, onClick);
    }

    /// <summary>
    /// 远程桌面端口设置子对话框
    /// </summary>
    public class RemotePortDialog : Window
    {
        private System.Windows.Controls.TextBox _portInput;
        private readonly MainWindow _owner;
        private Brush _bg, _fg, _dimText, _accent, _cardBorder, _inputBg, _inputFg, _btnFg, _btnSec;
        public RemotePortDialog(MainWindow owner)
        {
            _owner = owner;
            _bg = owner._windowBg; _fg = owner._textMain; _dimText = owner._textDim;
            _accent = owner._accent; _cardBorder = owner._panelBorder;
            _inputBg = owner._inputBg; _inputFg = owner._inputFg;
            _btnFg = owner._btnPrimaryFg; _btnSec = owner._btnSecondaryBg;

            Title = "远程桌面端口设置";
            Width = 340; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            Background = _bg;

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "远程桌面端口号", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _fg, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "输入范围：1000-65535，默认值：3389", FontSize = 11, Foreground = _dimText, Margin = new Thickness(0, 0, 0, 8) });
            _portInput = new System.Windows.Controls.TextBox
            {
                Text = GetCurrentPort().ToString(),
                Width = 120, Height = 28,
                Margin = new Thickness(0, 0, 0, 12),
                Background = _inputBg,
                Foreground = _inputFg,
                BorderBrush = _accent
            };
            root.Children.Add(_portInput);

            var btnBar = new WrapPanel();
            var applyBtn = new Button
            {
                Content = "更改端口", Width = 100, Height = 30,
                Background = _accent,
                Foreground = _btnFg,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                BorderBrush = _cardBorder,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(14, 4, 14, 4)
            };
            applyBtn.Template = DialogBtnFx.RoundedTemplate(new CornerRadius(6));
            applyBtn.Click += (s, e) => ApplyPort();
            applyBtn.HoverLift();
            btnBar.Children.Add(applyBtn);
            var closeBtn = new Button
            {
                Content = "取消", Width = 80, Height = 30,
                Background = _btnSec,
                Foreground = _fg,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                BorderBrush = _cardBorder,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(14, 4, 14, 4)
            };
            closeBtn.Template = DialogBtnFx.RoundedTemplate(new CornerRadius(6));
            closeBtn.Click += (s, e) => Close();
            closeBtn.HoverLift();
            btnBar.Children.Add(closeBtn);
            root.Children.Add(btnBar);
            Content = root;
        }

        private int GetCurrentPort()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp")) return k?.GetValue("PortNumber") is int v ? v : 3389; } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return 3389; }
        }

        private void ApplyPort()
        {
            if (_portInput == null || !int.TryParse(_portInput.Text, out int port) || port < 1000 || port > 65535)
            {
                MessageBox.Show("请输入有效的端口号 (1000-65535)", "无效输入", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RegistryHelper.SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", "PortNumber", port, _ => { });
            Exec.RunPowerShell(
                $"Remove-NetFirewallRule -DisplayName 'RDPPORT-TCP-In' -ErrorAction SilentlyContinue;" +
                $"New-NetFirewallRule -DisplayName 'RDPPORT-TCP-In' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {port}", _ => { });
            MessageBox.Show($"远程桌面端口已更改为 {port}。\n防火墙规则已添加（TCP）。\n重启后生效。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
