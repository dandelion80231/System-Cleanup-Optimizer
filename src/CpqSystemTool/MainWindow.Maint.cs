using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: 维护工具（含官方 exe 直链探针）
        // =====================================================================

        private UIElement BuildMaintenanceTools()
        {
            // root 不再单独加边距，统一沿用 ContentArea 的 Margin(22,12,22,22)，
            // 使顶部副标题高度与清理优化页一致、圆角卡片底部贴近窗口（与 Appx 管理页一致）。
            var root = new StackPanel { Margin = new Thickness(0) };

            // 顶部说明
            root.Children.Add(Header("", "维护工具：抓取官网软件安装包（exe）直链、管理本地探针依赖等。探针支持两种驱动：WebView2 Runtime（优先，复用系统 Edge，无需下载）或 Node + Playwright + Chromium（兜底）。点击「管理依赖」可分别安装/卸载两种环境。"));

            // ========== 探针卡片 ==========
            var probeCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                // 本卡片为页面最后一个元素，去掉底部外边距，使其贴近窗口底部（与 Appx 管理页一致）
                Margin = new Thickness(0)
            };
            var probeInner = new StackPanel();
            probeCard.Child = probeInner;

            probeInner.Children.Add(new TextBlock
            {
                Text = "官方 exe 直链探针",
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 输入框（带占位符水印）
            var placeholder = "输入入口 URL 或厂商名（qq/qqmusic/douyin/...）";
            var inputBox = new TextBox
            {
                Text = placeholder,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textDim,
                BorderBrush = _accent,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            bool inputTouched = false;
            inputBox.GotKeyboardFocus += (s, e) =>
            {
                if (!inputTouched) { inputBox.Text = ""; inputBox.Foreground = _textMain; inputTouched = true; }
            };
            inputBox.LostKeyboardFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(inputBox.Text))
                {
                    inputBox.Text = placeholder;
                    inputBox.Foreground = _textDim;
                    inputTouched = false;
                }
            };
            probeInner.Children.Add(inputBox);

            // 选项 + 按钮行（4 列均分占满整行：跳过检测 / 抓取直链 / 管理依赖 / 管理软件）
            var optRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0) };
            optRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var skipDlCheck = new CheckBox
            {
                Content = "跳过点击下载检测（更快）",
                Foreground = _textMain,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(skipDlCheck, 0);
            optRow.Children.Add(skipDlCheck);

            var fetchBtn = Btn("抓取直链", true, null, 120);
            fetchBtn.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(fetchBtn, 1);
            optRow.Children.Add(fetchBtn);

            // 「管理依赖」按钮：带下拉菜单（ToggleButton + Popup），支持 Node / WebView2 两种方案的安装、卸载、清理。
            // 真实根因（已由 D:\电脑桌面\deps_diag.log 证实）：此前用 StaysOpen=false，弹窗打开后
            // RefreshDepStatus 触发的 WebView2 宿主（隐藏 WinForms Form）会抢占窗口激活，Light-Dismiss
            // 把这次激活误判为"外部点击"，在 ~110ms 内自动关闭弹窗 → 用户只见"点不开"。
            // catBtn 不闪，是因为它打开时从不调用任何会激活外部窗口的逻辑，并非模板差异。
            // 修复：Popup.StaysOpen = true（不再自动关闭），改由窗口级 PreviewMouseDown（点外部关闭）
            // 与菜单项点击（MakeMenuItem 已置 IsOpen=false）手动关闭。
            // 选中态填充：accent 更高不透明度，比 hover 更明显（仍与主题一致）
            var depsSelectedBrush = _isDarkMode
                ? new SolidColorBrush(Color.FromArgb(0x73, 0x16, 0xE0, 0xBD))  // #16E0BD @ ~45%
                : new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x91, 0x82)); // #089182 @ ~55%
            var depsBtnTemplate = new ControlTemplate(typeof(ToggleButton));
            var depsBtnBd = new FrameworkElementFactory(typeof(Border), "Bd");
            depsBtnBd.SetBinding(Border.BackgroundProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BackgroundProperty) });
            depsBtnBd.SetBinding(Border.BorderBrushProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BorderBrushProperty) });
            depsBtnBd.SetBinding(Border.BorderThicknessProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BorderThicknessProperty) });
            depsBtnBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            depsBtnBd.SetBinding(Border.PaddingProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(PaddingProperty) });
            var depsBtnCp = new FrameworkElementFactory(typeof(ContentPresenter));
            depsBtnCp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            depsBtnCp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            depsBtnBd.AppendChild(depsBtnCp);
            depsBtnTemplate.VisualTree = depsBtnBd;
            var depsBtnHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            depsBtnHover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ButtonHoverBrush"), "Bd"));
            var depsBtnChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            depsBtnChecked.Setters.Add(new Setter(Border.BackgroundProperty, depsSelectedBrush, "Bd"));
            depsBtnTemplate.Triggers.Add(depsBtnHover);
            depsBtnTemplate.Triggers.Add(depsBtnChecked);

            // 按钮内容：文字 + 右侧下拉箭头，与驱动清理页 ComboBox 及「全部分类」下拉按钮保持视觉一致
            var depsBtnText = new TextBlock { Text = "管理依赖", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var depsBtnContent = UiShapes.MakeTextWithArrowGrid(depsBtnText, _textDim);

            var manageDepsBtn = new ToggleButton
            {
                Content = depsBtnContent,
                MinWidth = 100,
                MinHeight = 34,
                Padding = new Thickness(8, 7, 8, 7),
                Cursor = Cursors.Hand,
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Background = _btnSecondaryBg,
                Foreground = _btnSecondaryFg,
                BorderThickness = new Thickness(1),
                BorderBrush = _panelBorder,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = depsBtnTemplate
            };
            Grid.SetColumn(manageDepsBtn, 2);
            optRow.Children.Add(manageDepsBtn);

            var manageBtn = Btn("管理软件", false, null, 150);
            manageBtn.HorizontalAlignment = HorizontalAlignment.Center;
            manageBtn.Click += (s, e) =>
            {
                var dlg = new CustomSoftwareManagerDialog(this);
                dlg.Owner = this;
                dlg.ShowDialog();
                SetStatus("增补软件列表已更新；请到「常用软件」页点刷新查看（重启后依然保留）");
            };
            Grid.SetColumn(manageBtn, 3);
            optRow.Children.Add(manageBtn);
            probeInner.Children.Add(optRow);

            // 日志区（声明提前：下方菜单事件需要引用 logBox；视觉顺序放到最下方）
            var logBox = MakeLogBox();
            logBox.Height = 120;                    // 固定高度，不随日志内容自动扩展（MakeLogBox 已启用滚动条）
            logBox.Foreground = _textMain;          // 深色/浅色模式下都保证足够对比度
            var logBorder = WrapLogBox(logBox);
            // 保持透明，不额外加填充；靠 _textMain 主题文字色保证可读
            logBorder.Background = Brushes.Transparent;

            // ========== 「管理依赖」下拉菜单 ==========
            // 完全使用 Popup + 自定义 Border/StackPanel/TextBlock 实现，不再用 WPF ContextMenu/MenuItem。
            // 原因：ContextMenu 默认模板左侧有固定图标槽/gutter，背景读系统色，深/浅色主题下会出现白色竖边（用户截图）。
            // 自定义面板所有背景、文字、边框、hover 色都来自当前主题笔刷，四周颜色完全一致。
            var depsPopup = new Popup
            {
                PlacementTarget = manageDepsBtn,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = 0,
                VerticalOffset = 2,
                StaysOpen = true,   // 关键修复：手动管理关闭。StaysOpen=false 时 Light-Dismiss 会把打开弹窗触发的
                                    // WebView2 宿主窗体激活误判为"外部点击"而立即自关（诊断日志证实 Opened 后~110ms 即 Closed）。
                                    // 现在由窗口级 PreviewMouseDown（点外部关闭）与菜单项点击手动关闭。
                AllowsTransparency = true
            };
            var menuPanel = new StackPanel { Background = _windowBg };
            var menuBorder = new Border
            {
                Background = _windowBg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(1),
                MinWidth = 205,
                Child = menuPanel
            };
            depsPopup.Child = menuBorder;
            // 修复：AllowsTransparency=true 会让 Popup 以独立顶层 HWND 承载并带 WS_EX_TOPMOST，
            // 导致下拉菜单浮到最顶层、压在所有窗口之上。剥离该样式使其落到正常层级。
            UiShapes.DisablePopupTopmost(depsPopup);

            var nodeHeader = MakeMenuHeader("Node + Playwright + Chromium（检测中…）");
            var nodeInstall = MakeMenuItem("安装 / 修复", depsPopup, () =>
            {
                RunInBg(logBox, logf =>
                {
                    var probesDir = ResolveProbesDir();
                    var installPs = Path.Combine(probesDir, "install_deps.ps1");
                    if (!File.Exists(installPs))
                    {
                        logf("[!] 找不到 install_deps.ps1（目录：" + probesDir + "）");
                        return;
                    }
                    logf("[*] 开始安装/修复 Node + Playwright + Chromium 依赖……");
                    // 安装脚本失败时以 exit 1 退出；必须校验脚本退出码 + 重新校验 Node 与 Playwright 目录，
                    // 否则会出现“安装失败却报成功”的误判（此前踩过的坑）。
                    bool ok = RunPowerShellScript(probesDir, installPs, logf);
                    var dep = IsNodeDepsReady(probesDir);
                    if (ok && dep.Ready)
                        logf("[✓] Node 依赖安装完成（Node + Playwright 就绪）。");
                    else
                    {
                        logf("[!] Node 依赖安装未完成（脚本退出码=" + (ok ? "0" : "非零") +
                             "，Node=" + (dep.NodeExe != null ? "就绪" : "缺失") +
                             "，Playwright=" + (dep.PlaywrightExists ? "就绪" : "缺失") + "）。");
                        logf("    请检查网络后重试，或手动在 probes 目录运行 install_deps.ps1。");
                    }
                }, "依赖安装结束", null);
            });
            var nodeUninstall = MakeMenuItem("卸载本地依赖", depsPopup, () =>
            {
                var confirm = MessageBox.Show(
                    "确定要卸载本地 Node 依赖吗？\n\n将删除以下目录：\n" +
                    "- tools/probes/.tools\n" +
                    "- tools/probes/node_modules\n\n" +
                    "系统 PATH 中的 Node（如有）不会受影响。",
                    "卸载 Node 依赖", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
                RunInBg(logBox, logf =>
                {
                    var probesDir = ResolveProbesDir();
                    if (probesDir == null)
                    {
                        logf("[!] 未找到 probes 目录");
                        return;
                    }
                    var dirs = new[] { Path.Combine(probesDir, ".tools"), Path.Combine(probesDir, "node_modules") };
                    foreach (var d in dirs)
                    {
                        try
                        {
                            if (Directory.Exists(d))
                            {
                                Directory.Delete(d, true);
                                logf("[✓] 已删除：" + d);
                            }
                            else logf("[*] 目录不存在，跳过：" + d);
                        }
                        catch (Exception ex) { logf("[!] 删除失败：" + d + " — " + ex.Message); }
                    }
                    logf("[✓] Node 本地依赖卸载完成。");
                }, "卸载完成", null);
            });
            var wvHeader = MakeMenuHeader("WebView2 Runtime（系统 Edge）（检测中…）");
            var wvInstall = MakeMenuItem("安装 / 升级 / 修复", depsPopup, () =>
            {
                // RepairWebView2 会下载官方引导程序执行静默安装；若引导程序 no-op 则扫描磁盘并修复注册表指针。
                RunInBg(logBox, EdgeCore.RepairWebView2, "WebView2 修复完成", null);
            });
            var wvUninstall = MakeMenuItem("卸载", depsPopup, () =>
            {
                var confirm = MessageBox.Show(
                    "WebView2 Runtime 是系统级组件，Edge 和部分应用可能依赖它。\n\n" +
                    "确定要继续卸载吗？卸载后可能导致依赖 WebView2 的程序无法运行。",
                    "卸载 WebView2 Runtime", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
                RunInBg(logBox, EdgeCore.UninstallWebView2, "WebView2 卸载完成", null);
            });
            var wvClean = MakeMenuItem("清理探针缓存", depsPopup, () =>
            {
                RunInBg(logBox, logf =>
                {
                    var tmpRoot = Path.GetTempPath();
                    try
                    {
                        int cleaned = 0;
                        foreach (var d in Directory.GetDirectories(tmpRoot, "CpqProbeWebView2*"))
                        {
                            try { Directory.Delete(d, true); cleaned++; }
                            catch (Exception ex) { logf("[!] 清理失败：" + d + " — " + ex.Message); }
                        }
                        if (cleaned > 0) logf("[✓] 已清理 " + cleaned + " 个探针缓存目录。");
                        else logf("[*] 未发现探针缓存目录。");
                    }
                    catch (Exception ex) { logf("[!] 清理失败：" + ex.Message); }
                }, "缓存清理完成", null);
            });

            menuPanel.Children.Add(nodeHeader);
            menuPanel.Children.Add(nodeInstall);
            menuPanel.Children.Add(nodeUninstall);
            menuPanel.Children.Add(MakeMenuSeparator());
            menuPanel.Children.Add(wvHeader);
            menuPanel.Children.Add(wvInstall);
            menuPanel.Children.Add(wvUninstall);
            menuPanel.Children.Add(wvClean);

            // 拖动主窗口时强制 popup 重新定位到按钮下方：
            // popup 以独立 HWND（AllowsTransparency=true）承载，默认不跟随窗口移动（标题栏是非客户区，
            // 也不触发下方 PreviewMouseDown 的关闭逻辑），故需在窗口位置变化时手动重定位。
            // 通过微调 HorizontalOffset 触发 WPF 重新按 PlacementTarget 计算屏幕位置，归零后无可见抖动。
            EventHandler repositionDepsPopup = (s, e) =>
            {
                if (!depsPopup.IsOpen) return;
                var dx = depsPopup.HorizontalOffset;
                depsPopup.HorizontalOffset = dx + 0.01;
                depsPopup.HorizontalOffset = dx;
            };

            // 与 MainWindow.Pages.cs 里分类下拉 catBtn 完全一致的稳定写法：
            // 在 Click 事件里【同步】切换 IsOpen（不延迟），并用 Opened/Closed 同步 ToggleButton 的 IsChecked。
            // 打开的同时刷新依赖状态（Node 就绪 / WebView2 就绪）。
            // 注：此前"点不开"与 ControlTemplate 无关，根因是 StaysOpen=false 的 Light-Dismiss 自关（见上方说明）。
            manageDepsBtn.Click += (s, e) =>
            {
                depsPopup.IsOpen = !depsPopup.IsOpen;
                if (depsPopup.IsOpen) { var _ = RefreshDepStatus(nodeHeader, wvHeader, logBox); }
            };
            depsPopup.Opened += (s, e) =>
            {
                manageDepsBtn.IsChecked = true;
                depsPopup.Width = Math.Max(manageDepsBtn.ActualWidth, 205);
                this.LocationChanged += repositionDepsPopup;
            };
            depsPopup.Closed += (s, e) =>
            {
                manageDepsBtn.IsChecked = false;
                this.LocationChanged -= repositionDepsPopup;
            };

            // 手动管理"点击弹窗外部关闭"：StaysOpen=true 后不再自动关闭，需自行处理。
            // 点在按钮本身或弹窗内容内 → 不关闭（按钮 Click 负责切换；弹窗内点击由各项自关）。
            this.PreviewMouseDown += (s, e) =>
            {
                if (!depsPopup.IsOpen) return;
                var cur = e.OriginalSource as DependencyObject;
                while (cur != null)
                {
                    if (ReferenceEquals(cur, manageDepsBtn) || ReferenceEquals(cur, menuBorder) || ReferenceEquals(cur, menuPanel))
                        return;
                    cur = VisualTreeHelper.GetParent(cur) as DependencyObject
                          ?? LogicalTreeHelper.GetParent(cur) as DependencyObject;
                }
                depsPopup.IsOpen = false;
            };

            // 推荐直链区
            probeInner.Children.Add(new TextBlock
            {
                Text = "推荐直链：",
                FontWeight = FontWeights.Bold,
                Foreground = _textMain,
                Margin = new Thickness(0, 12, 0, 4)
            });
            var recDock = new DockPanel();
            var copyRecBtn = Btn("复制推荐链接", false, null, 130);
            copyRecBtn.IsEnabled = false;
            DockPanel.SetDock(copyRecBtn, Dock.Right);
            recDock.Children.Add(copyRecBtn);
            var recommendedTb = new TextBox
            {
                IsReadOnly = true,
                FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6),
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _accent,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            recDock.Children.Add(recommendedTb);
            probeInner.Children.Add(recDock);

            // 搜索定位提示：当推荐直链由搜索引擎（而非内置别名/URL 直抓）得到时显示，提醒人工核对
            var searchWarnTb = new TextBlock
            {
                Text = "（部分结果由搜索引擎定位且不在官方域名下，已标「⚠ 需核对」；复制/加入常用前需二次确认，谨防仿冒安装包）",
                Foreground = _warnOrange,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            probeInner.Children.Add(searchWarnTb);

            // 结果面板（DataGrid）：标题与提示说明放在同一行
            var candidateHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
            candidateHeader.Children.Add(new TextBlock
            {
                Text = "候选直链：",
                FontWeight = FontWeights.Bold,
                Foreground = _textMain
            });
            candidateHeader.Children.Add(new TextBlock
            {
                Text = "从下方候选直链点「加入」可直接增补，或点上方「管理软件」统一管理。",
                Foreground = _textDim,
                FontSize = 11,
                Margin = new Thickness(8, 2, 0, 0)
            });
            probeInner.Children.Add(candidateHeader);
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                // 保持透明，不额外加填充；行/单元格/列头各自透明，网格线用主题边框色
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 0),
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                RowBackground = Brushes.Transparent,
                AlternatingRowBackground = Brushes.Transparent,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = _panelBorder
            };
            // 列头透明底 + 主题文字色，避免默认系统色在深色模式下发灰/发白
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, _textMain));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, _panelBorder));
            dg.ColumnHeaderStyle = headerStyle;
            // 单元格透明底 + 主题文字色
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, _textMain));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent));
            dg.CellStyle = cellStyle;
            dg.Columns.Add(new DataGridTextColumn { Header = "来源", Binding = new Binding("Source"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "URL", Binding = new Binding("Url"), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "策略", Binding = new Binding("Strategy"), Width = new DataGridLength(90) });
            dg.Columns.Add(new DataGridTextColumn { Header = "架构", Binding = new Binding("Arch"), Width = new DataGridLength(60) });
            dg.Columns.Add(new DataGridTextColumn { Header = "验证", Binding = new Binding("VerifiedText"), Width = new DataGridLength(90) });
            dg.Columns.Add(new DataGridTextColumn { Header = "HTTP状态", Binding = new Binding("StatusText"), Width = new DataGridLength(80) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Content-Type", Binding = new Binding("ContentType"), Width = new DataGridLength(160) });
            dg.Columns.Add(new DataGridTextColumn { Header = "推荐", Binding = new Binding("RecMark"), Width = new DataGridLength(45) });
            dg.Columns.Add(new DataGridTextColumn { Header = "信任", Binding = new Binding("TrustText"), Width = new DataGridLength(70) });

            // 每行的「复制」按钮（用 FrameworkElementFactory 构建模板列）
            var copyCol = new DataGridTemplateColumn { Header = "复制", Width = new DataGridLength(56) };
            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(Button.ContentProperty, "复制");
            factory.SetValue(Button.WidthProperty, 46.0);
            factory.SetValue(Button.FontSizeProperty, 11.0);
            factory.SetValue(Button.CursorProperty, Cursors.Hand);
            factory.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (s, ev) =>
            {
                var btn = s as Button;
                var row = btn?.DataContext as ProbeCandidateRow;
                if (row == null || string.IsNullOrEmpty(row.Url)) return;
                // 低信任（搜索来源且非官方域名）候选：复制前强制二次确认，防仿冒安装包
                if (row.LowTrust)
                {
                    var confirm = MessageBox.Show(
                        "该直链由搜索引擎定位，且不在官网域名下，可能为仿冒安装包。\n\n" + row.Url + "\n\n确认仍要复制此链接吗？",
                        "域名待核对 · 安全风险", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }
                btn.IsEnabled = false;
                try
                {
                    if (await TrySetClipboardTextAsync(row.Url))
                        SetStatus("已复制: " + row.Url);
                    else
                        SetStatus("复制失败: 剪贴板被占用，请稍后重试");
                }
                finally { btn.IsEnabled = true; }
            }));
            copyCol.CellTemplate = new DataTemplate { VisualTree = factory };
            dg.Columns.Add(copyCol);

            // 每行的「加入常用」按钮：把候选直链预填进编辑对话框，保存即写入自定义软件列表（与「常用软件」页联动）
            var addCol = new DataGridTemplateColumn { Header = "加入常用", Width = new DataGridLength(70) };
            var addFactory = new FrameworkElementFactory(typeof(Button));
            addFactory.SetValue(Button.ContentProperty, "加入");
            addFactory.SetValue(Button.WidthProperty, 56.0);
            addFactory.SetValue(Button.FontSizeProperty, 11.0);
            addFactory.SetValue(Button.PaddingProperty, new Thickness(6, 3, 6, 3));
            addFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            addFactory.SetValue(Button.BackgroundProperty, _btnSecondaryBg);
            addFactory.SetValue(Button.ForegroundProperty, _btnSecondaryFg);
            addFactory.SetValue(Button.BorderThicknessProperty, new Thickness(1));
            addFactory.SetValue(Button.BorderBrushProperty, _panelBorder);
            addFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, ev) =>
            {
                var btn = s as Button;
                var row = btn?.DataContext as ProbeCandidateRow;
                if (row == null || string.IsNullOrEmpty(row.Url)) { SetStatus("该行无可用直链，无法加入常用软件"); return; }
                // 低信任候选：加入常用软件前强制二次确认，避免把仿冒直链固化进常用列表
                if (row.LowTrust)
                {
                    var confirm = MessageBox.Show(
                        "该直链由搜索引擎定位，且不在官网域名下，可能为仿冒安装包。\n\n" + row.Url + "\n\n确认仍要将其加入常用软件吗？",
                        "域名待核对 · 安全风险", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }
                var dlg = new CustomSoftwareEditDialog(this, null, row.Url, row.Source);
                dlg.Owner = this;
                if (dlg.ShowDialog() == true && dlg.Entry != null)
                {
                    try
                    {
                        SoftwareDefPersistence.AddOrUpdate(dlg.Entry);
                        SetStatus("已加入增补软件: " + (dlg.Entry.name ?? dlg.Entry.id));
                    }
                    catch (Exception ex) { SetStatus("保存失败: " + ex.Message); }
                }
            }));
            addCol.CellTemplate = new DataTemplate { VisualTree = addFactory };
            dg.Columns.Add(addCol);

            // 推荐行高亮（用 RowStyle + DataTrigger，避免仅依赖颜色表意）
            var rowStyle = new Style(typeof(DataGridRow));
            var recTrig = new DataTrigger { Binding = new Binding("IsRecommended"), Value = true };
            recTrig.Setters.Add(new Setter(DataGridRow.BackgroundProperty, _accent));
            recTrig.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Brushes.Black));
            rowStyle.Triggers.Add(recTrig);
            // 低信任行（搜索来源且非官方域名）：橙色文字提醒人工核对，与推荐高亮互斥（推荐已排除 lowTrust）
            var lowTrig = new DataTrigger { Binding = new Binding("LowTrust"), Value = true };
            lowTrig.Setters.Add(new Setter(DataGridRow.ForegroundProperty, _warnOrange));
            rowStyle.Triggers.Add(lowTrig);
            dg.RowStyle = rowStyle;

            probeInner.Children.Add(dg);

            // 日志区放到最下方，与其他页面「日志框在底部」的布局统一
            probeInner.Children.Add(new TextBlock
            {
                Text = "运行日志：",
                FontWeight = FontWeights.Bold,
                Foreground = _textMain,
                Margin = new Thickness(0, 12, 0, 4)
            });
            probeInner.Children.Add(logBorder);

            root.Children.Add(probeCard);

            // ========== 按钮事件 ==========

            // 「抓取直链」：后台运行探针，UI 不卡死
            fetchBtn.Click += (s, e) =>
            {
                var input = inputBox.Text.Trim();
                if (!inputTouched || input == placeholder)
                {
                    SetStatus("请输入入口 URL 或厂商名");
                    return;
                }
                fetchBtn.IsEnabled = false;
                fetchBtn.Content = "抓取中...";
                bool skip = skipDlCheck.IsChecked == true;

                bool searchLocated = false;
                RunInBg(logBox, logf =>
                {
                    try
                    {
                        RunProbeInternal(input, skip, logf, out var rows, out var rec, out searchLocated);
                        _probeRows = rows;
                        _probeRecommendedUrl = rec;
                    }
                    catch (Exception ex)
                    {
                        logf("[!] 运行异常: " + ex.Message);
                    }
                }, "抓取完成", () =>
                {
                    fetchBtn.IsEnabled = true;
                    fetchBtn.Content = "抓取直链";
                    try
                    {
                        recommendedTb.Text = _probeRecommendedUrl ?? "";
                        copyRecBtn.IsEnabled = !string.IsNullOrEmpty(_probeRecommendedUrl);
                        dg.ItemsSource = null;
                        dg.ItemsSource = _probeRows;
                        searchWarnTb.Visibility = searchLocated ? Visibility.Visible : Visibility.Collapsed;
                        SetStatus("抓取完成：共 " + (_probeRows?.Count ?? 0) + " 个候选" + (string.IsNullOrEmpty(_probeRecommendedUrl) ? "" : "，已推荐直链"));
                    }
                    catch (Exception ex)
                    {
                        SetStatus("结果填充失败: " + ex.Message);
                    }
                });
            };

            // 「复制推荐链接」
            copyRecBtn.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(recommendedTb.Text)) return;
                copyRecBtn.IsEnabled = false;
                try
                {
                    if (await TrySetClipboardTextAsync(recommendedTb.Text))
                        SetStatus("已复制推荐直链");
                    else
                        SetStatus("复制失败: 剪贴板被占用，请稍后重试");
                }
                finally { copyRecBtn.IsEnabled = true; }
            };

            return root;
        }

        /// <summary>
        /// 构造 DataGrid 列头/单元格的通用主题样式（透明底 + 主题前景色 + 边框），
        /// 供维护页与驱动管理页等需要透明网格的页面复用，避免样式代码散落重复。
        /// 列头样式：透明底、主题文字色 fg、边框色 border；单元格样式：透明底、前景 fg、透明边框。
        /// </summary>
        internal static (Style Header, Style Cell) MakeDataGridStyles(Brush fg, Brush border)
        {
            var h = new Style(typeof(DataGridColumnHeader));
            h.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            h.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            h.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            h.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            h.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));

            var c = new Style(typeof(DataGridCell));
            c.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
            c.Setters.Add(new Setter(DataGridCell.ForegroundProperty, fg));
            c.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent));
            c.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));

            return (h, c);
        }

        // =====================================================================
        //  「管理依赖」自定义下拉菜单辅助（Popup + 自定义面板，避免 ContextMenu 白边）
        // =====================================================================

        /// <summary>创建菜单分组标题（固定 accent 色，无 hover）。</summary>
        private TextBlock MakeMenuHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = _accent,
                FontWeight = FontWeights.Bold,
                FontSize = 12.0,
                Padding = new Thickness(8, 5, 8, 3),
                Background = _windowBg,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap
            };
        }

        /// <summary>创建自定义菜单项：透明背景、hover 行变色、点击后关闭 Popup 并执行 action。</summary>
        private Border MakeMenuItem(string text, Popup popup, Action click = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = _textMain,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5),
                Background = Brushes.Transparent
            };
            var border = new Border
            {
                Child = tb,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };
            border.MouseEnter += (s, e) => border.Background = _rowHover;
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                popup.IsOpen = false;
                click?.Invoke();
            };
            return border;
        }

        private FrameworkElement MakeMenuSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = _panelBorder,
                Margin = new Thickness(8, 3, 8, 3)
            };
        }

        // =====================================================================
        //  依赖状态刷新（维护工具页「管理依赖」菜单）
        // =====================================================================

        /// <summary>重入保护：RefreshDepStatus 正在执行（含最长 15s 的 WebView2 初始化）时为 true，连点直接忽略。仅 UI 线程访问。</summary>
        private bool _depRefreshing;

        /// <summary>
        /// 刷新「管理依赖」下拉面板中两种方案的状态文字。
        /// Node 就绪 = 本机或 probes/.tools 存在 node.exe 且 node_modules/playwright 存在。
        /// WebView2 就绪 = 真正创建离屏窗口并 EnsureCoreWebView2Async 初始化成功
        ///   （复用探针实际初始化路径，能识别「Runtime 已装但初始化挂起」这种此前误报就绪的故障）。
        /// </summary>
        private async Task RefreshDepStatus(TextBlock nodeHeader, TextBlock wvHeader, TextBox logBox = null)
        {
            // 重入保护：上一次刷新仍在进行时（WebView2 初始化最长 15s），连点直接忽略，避免并发起多个初始化。
            // 配合 ProbeBrowserHost 的 30s TTL 就绪缓存，首次真实初始化后 30s 内再点基本都会秒回。
            if (_depRefreshing) return;
            _depRefreshing = true;
            try
            {
                if (nodeHeader != null) nodeHeader.Text = "Node + Playwright + Chromium\n（检测中…）";
                if (wvHeader != null) wvHeader.Text = "WebView2 Runtime（系统 Edge）\n（检测中…）";

                var probesDir = ResolveProbesDir();
                var nodeReady = false;
                try
                {
                    nodeReady = IsNodeDepsReady(probesDir).Ready;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("IsNodeDepsReady 检测异常: " + ex.Message); }

                // 真实初始化校验：复用 InitAsync（离屏窗口渲染 + EnsureCoreWebView2Async），
                // 给足超时（15s）容纳版本预检与浏览器进程初始化，避免把“可用”误判为“超时未就绪”。
                // 下载百分比进度：经 Dispatcher 回到 UI 线程写入日志框（\r 前缀原地刷新最后一行）。
                Action<int> dlProgress = p =>
                {
                    try { Dispatcher.BeginInvoke(new Action(() => { if (logBox != null) AppendOrReplaceLog(logBox, WebView2ProbeDeps.ProgressLine(p)); })); }
                    catch { /* 窗口已关闭，忽略 */ }
                };
                var (wvReady, wvError) = await ProbeBrowserHost.CheckWebView2ReadyAsync(TimeSpan.FromSeconds(15), null, dlProgress);

                if (nodeHeader != null)
                    nodeHeader.Text = "Node + Playwright + Chromium\n" + (nodeReady ? "（已就绪）" : "（未安装）");
                if (wvHeader != null)
                    wvHeader.Text = "WebView2 Runtime（系统 Edge）\n" + (wvReady ? "（已就绪）" : "（未安装" + (string.IsNullOrEmpty(wvError) ? "" : "：" + wvError) + "）");
            }
            catch (System.Exception ex)
            {
                DebugLog.Ignore(ex);
            }
            finally
            {
                _depRefreshing = false;
            }
        }
    }
}
