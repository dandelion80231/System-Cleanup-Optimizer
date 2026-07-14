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
            root.Children.Add(Header("系统工具", "Windows 版本转换 + 上帝模式 + 系统还原点。均为低频高危操作，建议先创建还原点再执行转换。"));

            // 共享进度条 + 日志（两模块共用，避免之前各页独立导致底部两份日志）
            var pb = MakeProgress();
            var sharedLog = MakeLogBox();
            sharedLog.Height = 120;
            sharedLog.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var sharedLogBorder = WrapLogBox(sharedLog);

            // ===== 卡片 1：Windows 版本转换 =====
            var vsCard = Card();
            var vsInner = (StackPanel)vsCard.Child;
            // 紧凑布局：标题与小字提示同行（提示不单独占行；窗口窄时自动换行）
            var vsHeadRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            vsHeadRow.Children.Add(new Emoji.Wpf.TextBlock { Text = "🔄 Windows 版本转换", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            vsHeadRow.Children.Add(new TextBlock
            {
                Text = "建议先关闭杀毒软件/Defender 实时保护；会自动重启一次并切换为未激活状态，需重新激活。转换前请先创建系统还原点。",
                FontSize = 10.5,
                Foreground = _textDim,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            vsInner.Children.Add(vsHeadRow);

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
                    new { Id="KMS4k",    Name="KMS4k",      Sub="卷许长期激活",         Desc="仅限批量/卷许版（Windows+Office）\n有效期4000+年，零售版、家庭版无效", Color=new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB)) },
                    new { Id="Ohook",    Name="Ohook",      Sub="Office 激活",          Desc="仅激活 Microsoft Office 套件，不影响 Windows", Color=new SolidColorBrush(Color.FromRgb(0x9B,0x59,0xB6)) },
                    new { Id="KMS",      Name="Online KMS", Sub="在线KMS（每180天）",   Desc="在线KMS服务器激活，需每180天续期或配合计划任务", Color=new SolidColorBrush(Color.FromRgb(0xE6,0x7E,0x22)) },
                    new { Id="TSforge",  Name="TSforge",    Sub="强制激活",             Desc="强制写入激活信息，绕过常规检测（可能被检测）", Color=new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)) },
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
                        else if (methodId == "windows" || methodId == "win" || methodId == "office")
                        {
                            // fix-9：此前 Windows/Office 激活直接执行、无确认弹窗（与 MAS 路径不一致）。
                            // 增加二次确认，如实说明将使用第三方 KMS 服务器 kms.03k.org 及潜在风险。
                            var what = (methodId == "office") ? "Office" : "Windows";
                            var msg = "即将通过第三方 KMS 服务器 kms.03k.org 对【" + what + "】执行激活。\n\n"
                                    + "• 需要联网访问 kms.03k.org\n"
                                    + "• 将安装批量授权密钥并设置 KMS 服务器，激活状态有效期约 180 天，需定期续期\n"
                                    + "• 使用第三方 KMS 服务器存在授权合规与服务器可用性风险\n\n"
                                    + "是否继续？";
                            if (System.Windows.MessageBox.Show(this, msg, "KMS 激活确认",
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
            var godModeBtn = Btn("打开上帝模式（创建 GodMode.{ED7BA470-8E54-465E-825C-99712043E01C} 链接到桌面）", true, () =>
            {
                GodMode.Create(msg => sharedLog.AppendText(msg + "\r\n"));
            }, 380);
            // 紧凑布局：标题与按钮同行（省一行纵向空间）
            // 标题左对齐，打开上帝模式按钮在剩余位置内居中
            var godRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            godRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            godRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var gt = new Emoji.Wpf.TextBlock { Text = "🌌 上帝模式", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(gt, 0);
            godRow.Children.Add(gt);
            godModeBtn.Margin = new Thickness(0);
            godModeBtn.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(godModeBtn, 1);
            godRow.Children.Add(godModeBtn);
            godInner.Children.Add(godRow);
            root.Children.Add(godCard);

            // ===== 卡片 3：系统还原 =====
            var restoreCard = Card();
            var restoreInner = (StackPanel)restoreCard.Child;
            var listBox = new ListBox { MaxHeight = 180, Margin = new Thickness(0, 0, 0, 8), Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1) };
            listBox.ItemContainerStyle = new Style(typeof(ListBoxItem));
            listBox.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty, _textMain));

            var bCreateRp = Btn("📌 创建还原点", false, () =>
                {
                    // [Q25] 防重入：创建还原点走全局 OperationLock，避免连点/与其它系统工具并发
                    if (!OperationLock.TryEnter("系统工具", out string busyBy))
                    {
                        MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    pb.Visibility = Visibility.Visible;
                    RunInBg(sharedLog, l => RestorePoint.Create("ZyperTool-" + DateTime.Now.ToString("MMdd-HHmm"), l), "还原点已创建", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; });
                }, 130);
            var bRefreshRp = Btn("🔄 刷新列表", true, () =>
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
                }, 110);
            var bRestoreSel = Btn("⏪ 还原选中", false, () =>
                {
                    var sel = listBox.SelectedItem as RestorePoint.RestoreInfo;
                    if (sel == null) { sharedLog.AppendText("[!] 请先选择还原点\r\n"); return; }
                    // [Q25] 防重入：还原（高危）走全局 OperationLock
                    if (!OperationLock.TryEnter("系统工具", out string busyBy))
                    {
                        MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    pb.Visibility = Visibility.Visible;
                    RunInBg(sharedLog, l => RestorePoint.Restore(sel.Seq, l), "已发起还原", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; });
                }, 110);
            // 标题左对齐 + 三按钮星等分平分间距、占满剩余位置（MakeBtnRow 三列 star）
            var wp = MakeBtnRow(bCreateRp, bRefreshRp, bRestoreSel);
            var restoreRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            restoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            restoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rt = new Emoji.Wpf.TextBlock { Text = "⏪ 系统还原", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(rt, 0);
            restoreRow.Children.Add(rt);
            wp.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetColumn(wp, 1);
            restoreRow.Children.Add(wp);
            restoreInner.Children.Add(restoreRow);
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
                // [Q25] 防重入：版本转换（高危、需重启）走全局 OperationLock，避免连点/并发
                if (!OperationLock.TryEnter("系统工具", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(sharedLog, l =>
                {
                    // [Q29] 备份激活(slmgr /dlv, 1-5s)原在 UI 线程跑子进程会阻塞窗口；移入后台任务、用 bg 日志 l
                    VersionSwitch.BackupActivation(l);
                    VersionSwitch.SwitchEdition(edition, key, l);
                }, "版本转换结束", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; vsRestoreBtn.IsEnabled = true; });
            };
            vsRestoreBtn.Click += (s, e) =>
            {
                if (!VersionSwitch.HasBackup()) { sharedLog.AppendText("[!] 没有可用的备份\r\n"); return; }
                if (System.Windows.MessageBox.Show("确认从备份还原激活信息？\n\n将显示备份的时间和版本信息。", "还原激活信息", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                RunInBg(sharedLog, VersionSwitch.RestoreActivation, "还原完成", null);
            };

            return root;
        }

        // =====================================================================
        //  Module: Office 部署（激活页）页面缓存（驱动清理页同款「窗口累积」模式）
        // =====================================================================
        // 导航每次切页都调 Build() 重建，重建会丢掉 Office 部署日志与正在跑的 ODT 进度 UI。
        // 缓存整页根节点（含 logRich 日志区 + OfficeDeployControl）：创建一次后跨导航复用，
        // 日志在软件关闭前始终累积；主题切换时失效重建（应用新笔刷，与驱动清理页语义一致）。
        private UIElement _cachedActivationRoot;

        private UIElement BuildActivation()
        {
            if (_cachedActivationRoot != null) return _cachedActivationRoot;
            _cachedActivationRoot = BuildActivationInner();
            return _cachedActivationRoot;
        }

        /// <summary>清空 Office 部署页缓存，下次访问时重建（主题切换后应用新笔刷；日志随页重建清空——与驱动清理页一致）。</summary>
        private void InvalidateActivationCache() => _cachedActivationRoot = null;

        private UIElement BuildActivationInner()
        {
            // 日志行改 Star（撑满剩余视口）：让「日志框」高度由剩余空间驱动——默认窗口≈8 行、
            // 最大化≈25 行，只有日志框自身（内部 ScrollViewer）滚动，页面级滚动已移除。
            // （对齐 Software 页「Star 行 + BindRootHeightToViewport(root) + 内部 ScrollViewer 撑满」的成熟模式。）
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 0: Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 1: Office 部署
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 2: 日志（Star 撑满剩余）

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
            // 日志框高度策略（彻底版）：
            // - MinHeight=145：空日志框时的最小可见高度（约 8 行，防止只剩一条细线）；内容更多时 WPF 按实际内容渲染，
            //   在 Auto 行里 MinHeight 只是下限、不是固定值，故 145/148/150 之间肉眼无差（被内容主导，非被 MinHeight 主导）。
            // - MinHeight=145（≈8 行）：空日志框时的最小可见高度（Star 行给的空间不足 8 行时也保底 8 行）。
            // - MaxHeight=450（≈25 行）：最大化时撑高的上限——日志框最多显示 25 行，再多由内部滚动条滚，
            //   避免窗口很大时日志框被拉得过高。行高≈18px：8 行=145、25 行=450。
            // 历史教训：MinHeight 只是下限，真正决定"撑多高"的是 Star 行给的空间 + MaxHeight 上限。
            var logRichScroll = new ScrollViewer
            {
                Content = logRich,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 145,
                MaxHeight = 450,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var logRichBorder = WrapLogBoxRich(logRichScroll, cornerRadius: 6);

            // 右键复制所有日志内容（ItemsControl 不支持原生文本选择，需提供此功能）。
            // 样式对齐 v1.21.exe：自定义 Popup + Border/TextBlock（与 Maint.cs MakeMenuItem 一致），
            //   框宽紧贴文字内容、四周主题色统一、外框与全站一致（规避标准 ContextMenu 的 gutter/白边/框过长问题）。
            // 行为：手动管理关闭（见下方 closeCopyLogOnOutsideClick），效果等同标准右键菜单——
            //   右键弹出、左键点别处自动消失、切页随页面卸载关闭（不再常驻）。
            //   关键：本项目 net10/WPF 下 StaysOpen=false 的 Popup 打开后会因 Light-Dismiss 自关（点不开/闪退，
            //   见 Maint.cs depsPopup 注释），故必须 StaysOpen=true 并复用「管理依赖」菜单的同法手动关闭。
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
            // 与「管理依赖」下拉一致：AllowsTransparency=true 会以独立顶层 HWND 卸载并带 WS_EX_TOPMOST，
            // 导致弹窗浮到最顶层；此处禁用置顶使其落在正常层级。
            UiShapes.DisablePopupTopmost(copyLogPopup);

            // 复制时带上图标对应的 emoji 文本。"check" 是 XAML 绿色矢量勾的标识（非 emoji 字符），必须映射成 ✅，
            // 否则粘贴出来是 "check" 字样。使用重试机制写入剪贴板，规避 OfficeClickToRun 占用时 OpenClipboard 失败。
            async void CopyAllLogAsync()
            {
                try
                {
                    var lines = logRich.Items.Cast<OfficeDeployControl.LogEntry>()
                        .Select(le => $"{le.Timestamp}  {(le.Icon == "check" ? "✅" : le.Icon)} {le.Text}")
                        .ToList();
                    if (await TrySetClipboardTextAsync(string.Join("\n", lines)))
                        SetStatus("日志已复制到剪贴板");
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }
            }

            copyMenuPanel.Children.Add(MakeMenuItem("复制日志", copyLogPopup, () => CopyAllLogAsync()));
            logRichBorder.PreviewMouseRightButtonDown += (s, e) =>
            {
                e.Handled = true;
                copyLogPopup.IsOpen = true;
            };

            // ----- 手动管理"点击弹窗外部关闭"（行为对齐标准右键菜单，避免 StaysOpen=true 后常驻）-----
            // 仅响应左键（右键用于打开弹窗，若窗口级 PreviewMouseDown 也处理右键会把刚开的弹窗误关）；
            // 排除列表只含弹窗自身内容（copyMenuBorder/copyMenuPanel，菜单项内点击由 MakeMenuItem 自关）。
            // 关键：不能把打开触发区 logRichBorder 列入排除——日志框很大，右键打开后左键点回日志框内仍会命中
            //   logRichBorder 而不关闭，正是"常驻"的根因。左键点日志框内任意处 / 点别处 / 切页点击导航都落在排除列表外 → 关闭。
            // 挂 / 解绑与页面生命周期对齐：root.Unloaded（导航切走 / 主题重建页面 / 窗口关闭）解绑并收起弹窗，
            // 杜绝旧写法"只 += 从不 -="造成的窗口级事件泄漏；Loaded 重绑幂等，反复切主题也不会累积处理器。
            MouseButtonEventHandler closeCopyLogOnOutsideClick = (s, e) =>
            {
                try
                {
                    // 只处理左键：右键用于打开弹窗，若窗口级 PreviewMouseDown 也响应右键会把刚打开的弹窗误关。
                    if (e.ChangedButton != MouseButton.Left) return;
                    if (!copyLogPopup.IsOpen) return;
                    var cur = e.OriginalSource as DependencyObject;
                    while (cur != null)
                    {
                        // 仅排除弹窗自身内容（菜单项内点击由 MakeMenuItem 自关）；
                        // 注意：不能把打开触发区 logRichBorder 列入排除——日志框很大，右键打开后左键点回
                        // 日志框内仍会命中 logRichBorder 而不关闭，导致"常驻"。
                        if (ReferenceEquals(cur, copyMenuBorder) || ReferenceEquals(cur, copyMenuPanel))
                            return;
                        cur = VisualTreeHelper.GetParent(cur) as DependencyObject
                              ?? LogicalTreeHelper.GetParent(cur) as DependencyObject;
                    }
                    copyLogPopup.IsOpen = false;
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }
            };
            RoutedEventHandler attachCopyLogOutsideHook = null;
            RoutedEventHandler detachCopyLogOutsideHook = null;
            attachCopyLogOutsideHook = (s, e) =>
            {
                this.PreviewMouseDown -= closeCopyLogOnOutsideClick;
                this.PreviewMouseDown += closeCopyLogOnOutsideClick;
            };
            detachCopyLogOutsideHook = (s, e) =>
            {
                this.PreviewMouseDown -= closeCopyLogOnOutsideClick;
                try { copyLogPopup.IsOpen = false; }
                catch (Exception ex) { DebugLog.Ignore(ex); }
            };
            // 先立即挂上：即使 Loaded 因故未触发，也退化为始终挂着（比永不生效更稳）。
            attachCopyLogOutsideHook(null, null);
            root.Loaded += attachCopyLogOutsideHook;
            root.Unloaded += detachCopyLogOutsideHook;

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
                    // 显示上限 2000 行（与 OfficeDeployControl.MaxLogLines 同一约定）：
                    // 只裁显示列表（从最旧端移除）；ODT 进程、磁盘文件、运行状态均不受影响。
                    while (logRich.Items.Count > 2000) logRich.Items.RemoveAt(0);
                });
                OfficeDeployLog.Append(entry); // 本地留一份：cpq-tool\Office 部署\log\（按天分文件，保留 14 天）
            };
            var officeCard = Card();
            // 卡片底内边距 16→7：按钮行到卡片下边框 = 按钮底边距 2 + 控件根底边距 3 + 卡片底 7 = 12px，
            // 与按钮行到上方进度条的 12px（进度条底 2 + 按钮行上 10）对齐，上下最整齐
            officeCard.Padding = new Thickness(16, 16, 16, 7);
            var officeCardInner = (StackPanel)officeCard.Child;
            officeCardInner.Children.Add(officeDeploy);
            Grid.SetRow(officeCard, 1);
            root.Children.Add(officeCard);

            // 日志区：嵌套 Grid —— row0=标题(Auto)、row1=日志框(Star 撑满剩余)。
            // 日志框放进 Star 行后，其高度由「剩余视口空间」驱动：默认窗口≈8 行、最大化≈25 行（MaxHeight 封顶），
            // 只有 logRichScroll 自身滚动；页面级 ScrollViewer 已移除（默认/最大化都装得下，不需要页面级滚动）。
            var logGrid = new Grid();
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var logHeader = new Emoji.Wpf.TextBlock
            {
                // 彩色 emoji 页头：📋 由 Emoji.Wpf.TextBlock 渲染原色图形
                Text = "📋 执行日志（Office）",
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 8)
            };
            logGrid.Children.Add(logHeader);
            Grid.SetRow(logHeader, 0);

            logRichBorder.VerticalAlignment = VerticalAlignment.Stretch;
            logGrid.Children.Add(logRichBorder);
            Grid.SetRow(logRichBorder, 1);

            Grid.SetRow(logGrid, 2);
            root.Children.Add(logGrid);

            // Star 行需要高度约束：把 root.MaxHeight 绑到视口（跟随初始 + 缩放）。
            // 日志框填满「root 剩余空间」并被 Min/MaxHeight 夹住，超出才内部滚动。
            // 外层 pageScroll 已移除：默认/最大化都装得下，不需要页面级滚动控件；
            // 极小窗口下日志框底缘可能被裁，但日志仍可内部滚动查看（对齐用户诉求：整页不再需要滚动控件）。
            BindRootHeightToViewport(root);

            return root;
        }
    }
}
