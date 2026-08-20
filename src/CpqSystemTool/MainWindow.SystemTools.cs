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
            vsInner.Children.Add(new TextBlock { Text = "🔄 Windows 版本转换", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
            vsInner.Children.Add(new TextBlock
            {
                Text = "建议先关闭杀毒软件/Defender 实时保护；会自动重启一次并切换为未激活状态，需重新激活。转换前请先创建系统还原点。",
                FontSize = 10.5,
                Foreground = _textDim,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });

            var vsCurrentTb = new TextBlock
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
            godInner.Children.Add(new TextBlock { Text = "🌌 上帝模式", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
            var godModeBtn = Btn("打开上帝模式（创建 GodMode.{ED7BA470-8E54-465E-825C-99712043E01C} 链接到桌面）", true, () =>
            {
                GodMode.Create(msg => sharedLog.AppendText(msg + "\r\n"));
            }, 380);
            godInner.Children.Add(godModeBtn);
            root.Children.Add(godCard);

            // ===== 卡片 3：系统还原 =====
            var restoreCard = Card();
            var restoreInner = (StackPanel)restoreCard.Child;
            restoreInner.Children.Add(new TextBlock { Text = "⏪ 系统还原", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 4, 0, 8) });
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

            // 上帝模式按钮已在上方通过 Btn(..., onClick) 直接接线（日志写入共享日志，无需此处的脆弱 FirstOrDefault 查找）

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
            // 用 Grid 而非 StackPanel，让日志窗口撑满剩余空间（Star 高度）
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
                new { Id="KMS",      Name="Online KMS", Sub="在线KMS（每180天）",   Desc="通过在线 KMS 服务器激活，需每180天续期或配合计划任务", Color=new SolidColorBrush(Color.FromRgb(0xE6,0x7E,0x22)) },
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
            var actInner = (StackPanel)activationCard.Child;
            actInner.Children.Add(new TextBlock { Text = "🎯 激活方式（点击卡片）", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) });

            var cardsPanel = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Rows = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 14)
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
                    Padding = new Thickness(14, 12, 14, 12),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 8, 8),
                    MinHeight = 90,
                    Tag = methodId
                };
                var cardSp = new StackPanel();
                cardSp.Children.Add(new TextBlock { Text = m.Name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = m.Color });
                cardSp.Children.Add(new TextBlock { Text = m.Sub, FontSize = 13, Foreground = _textDim, Margin = new Thickness(0, 2, 0, 0) });
                cardSp.Children.Add(new TextBlock { Text = m.Desc, FontSize = 11, Foreground = _textDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75 });
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

            // ----- Office 安装/卸载（Issue 13: 合并进来）-----
            actInner.Children.Add(new TextBlock { Text = "📄 Office 安装 / 卸载", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 8) });
            // 用 Grid 让 ComboBox 自动填满、按钮按列分布，比 WrapPanel 视觉更稳定
            var officeBar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            officeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // ComboBox 自适应
            officeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });                    // 间距
            officeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 安装按钮
            officeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });                    // 间距
            officeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 卸载按钮
            var cb = new ComboBox { MinHeight = 34, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题；项样式含字号/内边距/悬浮/选中）
            UiShapes.ApplyComboBoxTheme(cb, UiShapes.ComboBoxTheme.Create(
                _inputBg, _inputFg, _windowBg, _panelBorder, _textMain, _rowHover, _rowSelected, _textDim));
            foreach (var e in OfficeInstall.Editions) cb.Items.Add(e);
            cb.SelectedIndex = 0;
            Grid.SetColumn(cb, 0);
            officeBar.Children.Add(cb);
            var installBtn = Btn("安装所选版本", true, () =>
            {
                int i = cb.SelectedIndex;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => OfficeInstall.Install(i, l), "Office 安装结束", () => pb.Visibility = Visibility.Collapsed);
            }, 130);
            Grid.SetColumn(installBtn, 2);
            installBtn.Margin = new Thickness(0);
            officeBar.Children.Add(installBtn);
            var uninstallBtn = Btn("强力卸载 Office", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, OfficeInstall.Uninstall, "Office 卸载结束", () => pb.Visibility = Visibility.Collapsed);
            }, 140);
            Grid.SetColumn(uninstallBtn, 4);
            uninstallBtn.Margin = new Thickness(0);
            officeBar.Children.Add(uninstallBtn);
            actInner.Children.Add(officeBar);

            actInner.Children.Add(pb);
            // 使用统一日志框包装：避免 TextBox 自身边框 + 外层 Border 形成双层边框
            log.Height = 100;
            var logBorder = WrapLogBox(log);
            Grid.SetRow(logBorder, 2);
            root.Children.Add(logBorder);

            Grid.SetRow(activationCard, 1);
            root.Children.Add(activationCard);

            // 动态 MaxHeight：最大化时 root 跟随视口拉伸
            // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放，规避 vp=0 跳过）
            BindRootHeightToViewport(root);

            return root;
        }
    }
}
