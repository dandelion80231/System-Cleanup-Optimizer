using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // 中文版名映射已收编至 Helpers/EditionMap.cs（ToChinese / EnglishToChinese）
        // =====================================================================
        //  Module: 系统激活 + Office（合并：6 卡片 2行3列 + Office 安装/卸载区）
        // =====================================================================

        // =====================================================================
        //  Module: Windows 版本转换（独立页面，对齐 OSSQ 一键转换 7.0）
        // =====================================================================

        private UIElement BuildSystemTools()
        {
            var root = new StackPanel();
            root.Children.Add(Header("系统工具", "Windows 版本转换（对齐一键转换 7.0）+ 上帝模式 + 系统还原点。均为低频高危操作，建议先创建还原点再执行转换。"));

            // 共享进度条 + 日志（两模块共用，避免之前各页独立导致底部两份日志）
            var pb = MakeProgress();
            var sharedLog = MakeLogBox();
            sharedLog.Height = 120;
            sharedLog.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var sharedLogBorder = WrapLogBox(sharedLog);

            // ===== 卡片 1：Windows 版本转换 =====
            var vsCard = Card();
            var vsInner = (StackPanel)vsCard.Child;
            vsInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🔄 Windows 版本转换", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
            vsInner.Children.Add(new TextBlock
            {
                Text = "建议先关闭杀毒软件/Defender 实时保护；会自动重启一次并切换为未激活状态，需重新激活。转换前请先创建系统还原点。",
                FontSize = 10.5,
                Foreground = _textDim,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });

            var vsCurrentTb = new Emoji.Wpf.TextBlock
            {
                Text = "当前版本: 查询中…",
                FontSize = 13,
                Foreground = _textMain,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var vsGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            vsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(vsCurrentTb, 0);
            vsGrid.Children.Add(vsCurrentTb);

            var vsTargetCombo = new ComboBox
            {
                MinHeight = 32,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = false
            };
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(vsTargetCombo, UiShapes.ComboBoxTheme.Create(
                _inputBg, _inputFg, _windowBg, _panelBorder, _textMain, _rowHover, _rowSelected, _textDim));
            Grid.SetColumn(vsTargetCombo, 2);
            vsGrid.Children.Add(vsTargetCombo);

            var vsKeyBox = new TextBox
            {
                MinHeight = 32,
                FontSize = 12.5,
                Width = 150,
                Padding = new Thickness(8, 5, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _accent,
                BorderThickness = new Thickness(1),
                ToolTip = "可留空：自动使用目标版本的内置零售通用密钥（GVLK）"
            };
            vsKeyBox.SetValue(System.Windows.Controls.TextBox.TextProperty, "");
            Grid.SetColumn(vsKeyBox, 4);
            vsGrid.Children.Add(vsKeyBox);

            var vsStartBtn = Btn("开始转换", true, null, 90);
            vsStartBtn.Background = _dangerRed;
            vsStartBtn.Foreground = Brushes.White;

            var vsRestoreBtn = Btn("查看备份", false, null, 92);
            vsRestoreBtn.IsEnabled = VersionSwitch.HasBackup();

            // 两个按钮均分右侧剩余空间（替代原来 Auto+gap+Auto 的不均匀布局）
            var vsBtnRow = MakeBtnRow(vsStartBtn, vsRestoreBtn);
            Grid.SetColumn(vsBtnRow, 6);
            vsGrid.Children.Add(vsBtnRow);
            vsInner.Children.Add(vsGrid);

            // 渠道选择：Consumer-Retail 零售版 / Business-VOL 批量版（均分整行）
            var channelPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            channelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            channelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rbRetail = new System.Windows.Controls.RadioButton
            {
                Content = "Consumer-Retail 零售版",
                GroupName = "VsChannel",
                IsChecked = true,
                Foreground = _textMain,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var rbVol = new System.Windows.Controls.RadioButton
            {
                Content = "Business-VOL 批量版",
                GroupName = "VsChannel",
                IsChecked = false,
                Foreground = _textMain,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rbRetail, 0);
            channelPanel.Children.Add(rbRetail);
            Grid.SetColumn(rbVol, 1);
            channelPanel.Children.Add(rbVol);
            vsInner.Children.Add(channelPanel);
            root.Children.Add(vsCard);

            // ===== 卡片（插入）：系统激活（6 卡片 2行3列，从原「激活工具」页移植，置于版本转换下方）=====
            // 复用本页共享日志框 sharedLog（纯文本 TextBox）与进度条 pb。
            {
                var activationMethods = new[]
                {
                    new { Id="HWID",     Name="HWID",      Sub="硬件永久激活",       Desc="数字许可证绑定硬件，永久有效（重装后可能失效）", Color=_accent },
                    new { Id="KMS38",    Name="KMS38",      Sub="激活至2038年",         Desc="KMS 密钥激活，有效期至2038年1月，适合长期使用", Color=new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB)) },
                    new { Id="Ohook",    Name="Ohook",      Sub="Office 激活",          Desc="仅激活 Microsoft Office 套件，不影响 Windows", Color=new SolidColorBrush(Color.FromRgb(0x9B,0x59,0xB6)) },
                    new { Id="KMS",      Name="Online KMS", Sub="在线KMS（每180天）",   Desc="在线KMS服务器激活，需每180天续期或配合计划任务", Color=new SolidColorBrush(Color.FromRgb(0xE6,0x7E,0x22)) },
                    new { Id="TSforge",  Name="TSforge",    Sub="强制激活",             Desc="强制写入激活信息，绕过常规检测（可能被检测）", Color=_warnOrange },
                    new { Id=Activation.DiagnosticMethodId, Name="诊断", Sub="查看激活状态", Desc="不执行激活，仅显示当前 Windows/Office 激活详情", Color=_textDim },
                };

                var actCard = Card();
                actCard.Padding = new Thickness(16, 16, 16, 6);
                var actInternal = (StackPanel)actCard.Child;
                actInternal.Children.Add(new Emoji.Wpf.TextBlock { Text = "🎯 激活方式（点击卡片）", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) });

                var actCardsPanel = new System.Windows.Controls.Primitives.UniformGrid
                {
                    Columns = 3,
                    Rows = 2,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var actSelectedBg = _rowSelected;
                var actHoverBg = _rowHover;
                var actCards = new List<Border>();

                foreach (var m in activationMethods)
                {
                    var methodId = m.Id;
                    var cardBorder = new Border
                    {
                        Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                        BorderBrush = m.Color,
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(10, 6, 10, 6),
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 8, 8),
                        MinHeight = 46,
                        Tag = methodId
                    };
                    var cardSp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    cardSp.Children.Add(new TextBlock
                    {
                        Text = m.Name + "---" + m.Sub,
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = m.Color,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    cardSp.Children.Add(new Emoji.Wpf.TextBlock
                    {
                        Text = m.Desc,
                        FontSize = 11,
                        Foreground = _textDim,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0),
                        Opacity = 0.75
                    });
                    cardBorder.Child = cardSp;
                    actCards.Add(cardBorder);

                    cardBorder.MouseEnter += (s, e) =>
                    {
                        var b = (Border)s;
                        if (b.Background != actSelectedBg) b.Background = actHoverBg;
                    };
                    cardBorder.MouseLeave += (s, e) =>
                    {
                        var b = (Border)s;
                        if (b.Background != actSelectedBg) b.Background = _isDarkMode ? Brushes.Transparent : _bgCard;
                    };

                    cardBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        var b = (Border)s;
                        foreach (var other in actCards) other.Background = _isDarkMode ? Brushes.Transparent : _bgCard;
                        b.Background = actSelectedBg;

                        if (Activation.IsMasMethod(methodId))
                        {
                            var msg = "即将联网下载并执行官方 Microsoft Activation Scripts (MAS) 进行【" + methodId + "】激活。\n\n"
                                    + "• 需要联网访问 get.activated.win\n"
                                    + "• 脚本来自开源项目 massgrave.dev（采用 GNU GPL v3 许可）\n"
                                    + "• 会弹出脚本窗口，请按其提示操作（可能需管理员授权）\n\n"
                                    + "是否继续？";
                            if (System.Windows.MessageBox.Show(this, msg, "联网激活确认",
                                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
                                != System.Windows.MessageBoxResult.Yes)
                            {
                                b.Background = _isDarkMode ? Brushes.Transparent : _bgCard;
                                return;
                            }
                        }

                        pb.Visibility = Visibility.Visible;
                        // 激活日志写入本页共享纯文本日志框 sharedLog（不再走彩色富日志）
                        RunInBg(sharedLog, l => Activation.Activate(methodId, l),
                            methodId == Activation.DiagnosticMethodId ? "诊断完成" : "激活完成",
                            () => pb.Visibility = Visibility.Collapsed);
                    };
                    actCardsPanel.Children.Add(cardBorder);
                }
                actInternal.Children.Add(actCardsPanel);
                root.Children.Add(actCard);
            }

            // ===== 卡片 2：上帝模式 =====
            var godCard = Card();
            var godInner = (StackPanel)godCard.Child;
            godInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🌌 上帝模式", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
            var godModeBtn = Btn("打开上帝模式（创建 GodMode.{ED7BA470-8E54-465E-825C-99712043E01C} 链接到桌面）", true, () =>
            {
                GodMode.Create(msg => sharedLog.AppendText(msg + "\r\n"));
            }, 380);
            godInner.Children.Add(godModeBtn);
            root.Children.Add(godCard);

            // ===== 卡片 3：系统还原 =====
            var restoreCard = Card();
            var restoreInner = (StackPanel)restoreCard.Child;
            restoreInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "⏪ 系统还原", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 4, 0, 8) });
            var listBox = new ListBox { MaxHeight = 180, Margin = new Thickness(0, 0, 0, 8), Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1) };
            listBox.ItemContainerStyle = new Style(typeof(ListBoxItem));
            listBox.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty, _textMain));

            var wp = MakeBtnRow(
                Btn("📌 创建还原点", false, () =>
                {
                    pb.Visibility = Visibility.Visible;
                    RunInBg(sharedLog, l => RestorePoint.Create("ZyperTool-" + DateTime.Now.ToString("MMdd-HHmm"), l), "还原点已创建", () => pb.Visibility = Visibility.Collapsed);
                }, 130),
                Btn("🔄 刷新列表", true, () =>
                {
                    pb.Visibility = Visibility.Visible;
                    RunInBg(sharedLog, l =>
                    {
                        var list = RestorePoint.List(l);
                        try { Dispatcher.Invoke(() =>
                        {
                            listBox.Items.Clear();
                            foreach (var r in list) listBox.Items.Add(r);
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }, "列表已刷新", () => pb.Visibility = Visibility.Collapsed);
                }, 110),
                Btn("⏪ 还原选中", false, () =>
                {
                    var sel = listBox.SelectedItem as RestorePoint.RestoreInfo;
                    if (sel == null) { sharedLog.AppendText("[!] 请先选择还原点\r\n"); return; }
                    pb.Visibility = Visibility.Visible;
                    RunInBg(sharedLog, l => RestorePoint.Restore(sel.Seq, l), "已发起还原", () => pb.Visibility = Visibility.Collapsed);
                }, 110)
            );
            wp.Margin = new Thickness(0, 0, 0, 8);
            restoreInner.Children.Add(wp);
            restoreInner.Children.Add(listBox);
            root.Children.Add(restoreCard);

            // 修正：原注释描述的是「此处曾有一段按名称 FirstOrDefault 查找按钮的接线代码」，该代码早已删除，
            // 注释却还留着，反而让人以为按钮在别处接线。现状：上帝模式按钮已在上方通过 Btn(..., onClick) 直接接线，
            // 日志写入共享日志框，此处不需要（也没有）任何额外查找。

            // 共享进度条 + 日志（放最后，竖向堆叠）
            root.Children.Add(pb);
            root.Children.Add(sharedLogBorder);

            // ===== 版本转换：UI 线程同步读注册表填充（< 50ms，无需异步）=====
            try
            {
                string cur = VersionSwitch.GetCurrentEdition(null);
                if (string.IsNullOrEmpty(cur)) vsCurrentTb.Text = "当前版本: 未知（读注册表失败，可能需以管理员运行）";
                else
                {
                    string osMaj = VersionSwitch.GetOsMajor() ?? "";
                    vsCurrentTb.Text = "当前版本: " + (osMaj.Length > 0 ? osMaj + " " : "") + (EditionMap.ToChinese(cur) ?? "(未知)") + " (" + cur + ")";
                }

                var items = new List<ComboBoxItem>();
                foreach (var t in VersionSwitch.GetTargetEditions(null))
                    items.Add(new ComboBoxItem { Content = (EditionMap.ToChinese(t) ?? "(未知)") + " (" + t + ")", Tag = t });
                vsTargetCombo.ItemsSource = items;
                int selIdx = 0;
                if (!string.IsNullOrEmpty(cur))
                {
                    for (int i = 0; i < items.Count; i++)
                        if (string.Equals(items[i].Tag as string, cur, StringComparison.OrdinalIgnoreCase))
                        { selIdx = i; break; }
                }
                vsTargetCombo.SelectedIndex = selIdx;
                vsTargetCombo.IsEnabled = true;
            }
            catch (Exception ex)
            {
                vsCurrentTb.Text = "当前版本: 异常 " + ex.Message;
            }

            vsStartBtn.Click += (s, e) =>
            {
                if (vsTargetCombo.SelectedItem == null) { sharedLog.AppendText("[!] 请先选择目标版本\r\n"); return; }
                string edition = (vsTargetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.IsNullOrEmpty(edition)) return;
                string key = vsKeyBox.Text.Trim();
                if (string.IsNullOrEmpty(key)) sharedLog.AppendText("[*] 密钥留空，将使用内置零售通用密钥（GVLK）转换，转换后需 MAS 激活\r\n");
                if (rbVol.IsChecked == true) sharedLog.AppendText("[*] 已选 Business-VOL 批量版：转换后需自行配置 KMS 服务器激活（参考本工具「系统激活」页 KMS 方式）\r\n");
                string cnName = EditionMap.ToChinese(edition) ?? "(未知)";
                if (System.Windows.MessageBox.Show(VersionSwitch.WARNING + "\n\n确认转换到 " + cnName + " ？", "版本转换（需重启 + 重新激活）", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                VersionSwitch.BackupActivation(text => sharedLog.AppendText(text + "\r\n"));
                pb.Visibility = Visibility.Visible;
                RunInBg(sharedLog, l => VersionSwitch.SwitchEdition(edition, key, l), "版本转换结束", () => { pb.Visibility = Visibility.Collapsed; vsRestoreBtn.IsEnabled = true; });
            };
            vsRestoreBtn.Click += (s, e) =>
            {
                if (!VersionSwitch.HasBackup()) { sharedLog.AppendText("[!] 没有可用的备份\r\n"); return; }
                if (System.Windows.MessageBox.Show("确认从备份还原激活信息？\n\n将显示备份的时间和版本信息。", "还原激活信息", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                RunInBg(sharedLog, VersionSwitch.RestoreActivation, "还原完成", null);
            };

            return root;
        }

        private UIElement BuildActivation()
        {
            // 修正：原注释称「让日志窗口撑满剩余空间（Star 高度）」，与下面的 RowDefinitions 不符——
            // 三行全是 GridLength.Auto，日志行也是 Auto（不撑满）。用 Grid 而非 StackPanel 只是为了让
            // 各行能独立按内容取高，日志保持 Auto 高度（内容多时由内部 ScrollViewer 滚动）。
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Office 部署
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 日志（Auto，不撑满）

            var headerTb = Header("office部署", "使用 Office 部署工具 (ODT) 安装 / 卸载 Microsoft Office，可自定义组件与激活方式。");
            Grid.SetRow(headerTb, 0);
            root.Children.Add(headerTb);

            // 彩色富日志（Office 安装/卸载输出共用，图标行自动渲染彩色 emoji / 绿色矢量勾）
            var logRich = new ItemsControl
            {
                ItemTemplate = (DataTemplate)FindResource("LogLineTemplate"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 0),
                Background = Brushes.Transparent,
            };
            // 拆分前 OfficeDeployControl 自带 LogBox 设了 MinHeight=80/MaxHeight=200，空时也常驻可见；
            // 拆分后改用本页级 logRich，若不给 MinHeight，空 ItemsControl 高度≈0 只剩一条细线，看起来像"默认隐藏"。
            // 这里套一层 ScrollViewer 并设 MinHeight/MaxHeight，复刻"常驻可见空框、内容多时内部滚动"的体验。
            var logRichScroll = new ScrollViewer
            {
                Content = logRich,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 90,
                MaxHeight = 220,
            };
            var logRichBorder = WrapLogBoxRich(logRichScroll, cornerRadius: 6);

            // 右键复制所有日志内容（ItemsControl 不支持原生文本选择，需提供此功能）。
            // 注意：这里不用标准 WPF ContextMenu。标准 ContextMenu 有两大问题，正是用户反馈的"菜单框比需求长、外框样式不对"：
            //   ① 默认模板左侧有固定图标槽/gutter，且框宽会被撑成"内容区宽度"（非文字内容宽度）→ 菜单明显过长；
            //   ② 默认主题下深/浅色会出现与全站不一致的白色竖边。
            // 改用与「管理依赖」下拉（Maint.cs MakeMenuItem）一致的自定义 Popup + Border/TextBlock：
            //   框宽紧贴文字内容（"复制日志"），四周主题色统一、外观与全站一致。
            var copyLogPopup = new Popup
            {
                PlacementTarget = logRichBorder,
                Placement = PlacementMode.MousePoint,
                AllowsTransparency = true,
                StaysOpen = true
            };
            var copyMenuPanel = new StackPanel { Background = _windowBg };
            var copyMenuBorder = new Border
            {
                Background = _windowBg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(1),
                Child = copyMenuPanel
            };
            copyLogPopup.Child = copyMenuBorder;
            // 修复：AllowsTransparency=true 会以独立顶层 HWND 承载并带 WS_EX_TOPMOST，导致弹层浮到最顶层。
            // 剥离该样式使其落到正常层级（与"管理依赖"/分类下拉一致）。
            UiShapes.DisablePopupTopmost(copyLogPopup);

            // 复制时带上图标对应的 emoji 文本。"check" 是 XAML 绿色矢量勾的哨兵值（非 emoji 字符），必须映射成 ✅，
            // 否则粘贴出来是 "check" 字样。使用重试机制写入剪贴板，避免 OfficeClickToRun 占用时 OpenClipboard 失败。
            async void CopyAllLogAsync()
            {
                try
                {
                    var lines = logRich.Items.Cast<OfficeDeployControl.LogEntry>()
                        .Select(le => $"{le.Timestamp}  {(le.Icon == "check" ? "✅" : le.Icon)} {le.Text}")
                        .ToList();
                    await TrySetClipboardTextAsync(string.Join("\n", lines));
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }
            }

            copyMenuPanel.Children.Add(MakeMenuItem("复制日志", copyLogPopup, () => CopyAllLogAsync()));
            logRichBorder.PreviewMouseRightButtonDown += (s, e) =>
            {
                e.Handled = true;
                copyLogPopup.IsOpen = true;
            };

            // ----- Office 安装/卸载（v1.20: 使用 OfficeDeployControl，内含卸载按钮）-----
            // 激活卡片已移至「系统工具」页（版本转换下方），本页仅保留 Office 部署功能。
            // 用大圆角卡片把部署相关功能框起来，与全站其它页面（系统工具卡片等）视觉一致。
            var officeDeploy = new OfficeDeployControl();
            // 共用日志框：Office 安装/卸载的输出汇入彩色富日志 logRich
            // （OfficeDeployControl 设置 ExternalLogSink 后会自动隐藏自带日志框，页面只保留一个日志区）。
            // LogEntry.Icon=="check" → 绿色矢量对勾；其它 → 彩色 emoji。
            officeDeploy.ExternalLogSink = entry =>
            {
                logRich.Dispatcher.Invoke(() =>
                {
                    logRich.Items.Add(entry);
                });
            };
            var officeCard = Card();
            var officeCardInner = (StackPanel)officeCard.Child;
            officeCardInner.Children.Add(officeDeploy);
            Grid.SetRow(officeCard, 1);
            root.Children.Add(officeCard);

            var logWrap = new StackPanel();
            logWrap.Children.Add(new Emoji.Wpf.TextBlock
            {
                // 彩色 emoji 页头：📋 由 Emoji.Wpf.TextBlock 渲染原色图形
                Text = "📋 执行日志（Office）",
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 8)
            });
            logWrap.Children.Add(logRichBorder);
            Grid.SetRow(logWrap, 2);
            root.Children.Add(logWrap);

            // v1.20 修复：本页三行全是 Auto，内容总高常常超出默认窗口高度。
            // 原来只把 root.MaxHeight 绑到视口 → 超出部分被 ContentArea 直接裁掉且不出滚动条。
            // 改为套一层页面级 ScrollViewer：限高落在 ScrollViewer 上，root 拿到无限高度按内容
            // 自然排布，超出即滚动。滚轮路由沿用 MainWindow.xaml.cs 的「鼠标下方 ScrollViewer 优先」逻辑。
            var pageScroll = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Background = System.Windows.Media.Brushes.Transparent
            };
            BindRootHeightToViewport(pageScroll);

            return pageScroll;
        }
    }
}
