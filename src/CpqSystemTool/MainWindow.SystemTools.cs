using System;
using System.Collections.Generic;
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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 激活卡片
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 日志（Auto，不撑满）

            var headerTb = Header("系统激活 & Office", "基于 MAS (Microsoft Activation Scripts) 激活 Windows/Office。下方为 Office 安装/卸载管理。");
            Grid.SetRow(headerTb, 0);
            root.Children.Add(headerTb);

            var methods = new[]
            {
                new { Id="HWID",     Name="HWID",      Sub="硬件永久激活",       Desc="数字许可证绑定硬件，永久有效（重装后可能失效）", Color=_accent },
                new { Id="KMS38",    Name="KMS38",      Sub="激活至2038年",         Desc="KMS 密钥激活，有效期至2038年1月，适合长期使用", Color=new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB)) },
                new { Id="Ohook",    Name="Ohook",      Sub="Office 激活",          Desc="仅激活 Microsoft Office 套件，不影响 Windows", Color=new SolidColorBrush(Color.FromRgb(0x9B,0x59,0xB6)) },
                new { Id="KMS",      Name="Online KMS", Sub="在线KMS（每180天）",   Desc="在线KMS服务器激活，需每180天续期或配合计划任务", Color=new SolidColorBrush(Color.FromRgb(0xE6,0x7E,0x22)) },
                new { Id="TSforge",  Name="TSforge",    Sub="强制激活",             Desc="强制写入激活信息，绕过常规检测（可能被检测）", Color=_warnOrange },
                new { Id=Activation.DiagnosticMethodId, Name="诊断", Sub="查看激活状态", Desc="不执行激活，仅显示当前 Windows/Office 激活详情", Color=_textDim },
            };

            // Issue 13: 2行3列 网格布局（用 Grid + UniformGrid 实现固定 6 卡片均匀分布）
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var pb = MakeProgress();
            var log = MakeLogBox();

            var activationCard = Card();
            // 执行日志上方的圆角边框底部内边距 16→6，整体往上缩 10px（与下方「执行日志」行留 10px 间距即可）
            activationCard.Padding = new Thickness(16, 16, 16, 6);
            var actInner = (StackPanel)activationCard.Child;
            // 彩色 emoji 页头：Emoji.Wpf.TextBlock 的 emoji 字形自动以原色图形渲染（原型 office-ui-prototype 同一方案）
            actInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🎯 激活方式（点击卡片）", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) });

            var cardsPanel = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Rows = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Issue 36: 卡片单选高亮（点击的卡片保持高亮，其他自动取消）
            // 选中态颜色：复用 Theme.cs 的主题字段（_rowSelected/_rowHover），消除重复硬编码
            var selectedBg = _rowSelected;
            var hoverBg = _rowHover;
            var cards = new List<Border>();

            foreach (var m in methods)
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
                // v1.20 布局调整：Name + Sub 合并为同一行（如「HWID---硬件永久激活」），整体水平居中，
                // Desc 为第二行；卡片高度压到约一半（MinHeight=46），六张卡片统一此样式。
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
                cards.Add(cardBorder);

                // 悬停提示（仅当卡片未选中时生效）
                cardBorder.MouseEnter += (s, e) =>
                {
                    var b = (Border)s;
                    if (b.Background != selectedBg) b.Background = hoverBg;
                };
                cardBorder.MouseLeave += (s, e) =>
                {
                    var b = (Border)s;
                    if (b.Background != selectedBg) b.Background = _isDarkMode ? Brushes.Transparent : _bgCard;
                };

                // 点击：单选高亮 + 实际激活
                cardBorder.MouseLeftButtonUp += (s, e) =>
                {
                    var b = (Border)s;
                    // 清空其他卡片高亮
                    foreach (var other in cards) other.Background = _isDarkMode ? Brushes.Transparent : _bgCard;
                    // 高亮当前
                    b.Background = selectedBg;

                    // 二次确认：MAS 为联网下载执行的第三方脚本（需管理员授权）
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
                            b.Background = _isDarkMode ? Brushes.Transparent : _bgCard; // 取消则撤高亮
                            return;
                        }
                    }

                    // 实际激活
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, l => Activation.Activate(methodId, l),
                        methodId == Activation.DiagnosticMethodId ? "诊断完成" : "激活完成",
                        () => pb.Visibility = Visibility.Collapsed);
                };
                cardsPanel.Children.Add(cardBorder);
            }
            actInner.Children.Add(cardsPanel);

            // ----- Office 安装/卸载（v1.20: 使用 OfficeDeployControl，内含卸载按钮）-----
            var officeDeploy = new OfficeDeployControl();
            actInner.Children.Add(officeDeploy);

            // v1.20 修复：合并成 OfficeDeployControl 时把进度条和日志框一起删掉了，
            // 结果点击任一激活方式后界面完全没有任何反馈（RunInBg 的输出写进了一个不在视觉树里的 TextBox）。
            actInner.Children.Add(pb);

            // 共用日志框：MAS 激活的输出（RunInBg(log, …)）与 Office 安装/卸载的输出都汇入这一个框。
            // OfficeDeployControl 设置 ExternalLogSink 后会自动隐藏自带日志框，页面只保留一个日志区。
            // 回调已在 UI 线程（控件 AppendLog 内部 Dispatcher.Invoke 之后才调用），可直接操作控件。
            officeDeploy.ExternalLogSink = s =>
            {
                log.AppendText(s + "\n");
                log.ScrollToEnd();
            };

            var logWrap = new StackPanel();
            logWrap.Children.Add(new Emoji.Wpf.TextBlock
            {
                // 彩色 emoji 页头：📋 由 Emoji.Wpf.TextBlock 渲染原色图形
                Text = "📋 执行日志（激活 / Office）",
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 8)
            });
            log.Height = 120;
            logWrap.Children.Add(WrapLogBox(log));
            Grid.SetRow(logWrap, 2);
            root.Children.Add(logWrap);

            Grid.SetRow(activationCard, 1);
            root.Children.Add(activationCard);

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
