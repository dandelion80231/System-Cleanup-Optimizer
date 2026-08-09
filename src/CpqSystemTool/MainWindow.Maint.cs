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
            var root = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

            // 顶部说明
            root.Children.Add(Header("", "维护工具：抓取官网软件安装包（exe）直链、管理本地探针依赖等。首次使用需先安装探针依赖（Node + Chromium）——点击「抓取直链」会自动检测并在缺失时一键安装。"));

            // ========== 探针卡片 ==========
            var probeCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14)
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

            // 选项 + 按钮行（4 列均分占满整行：跳过检测 / 抓取直链 / 安装依赖 / 管理软件）
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

            var installBtn = Btn("安装/修复依赖", false, null, 150);
            installBtn.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(installBtn, 2);
            optRow.Children.Add(installBtn);

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

            // 日志区（声明提前，事件处理需要引用 logBox；视觉顺序放到最下方，与其他页面统一）
            var logBox = MakeLogBox();
            logBox.Height = 120;                    // 固定高度，不随日志内容自动扩展（MakeLogBox 已启用滚动条）
            logBox.Foreground = _textMain;          // 深色/浅色模式下都保证足够对比度
            var logBorder = WrapLogBox(logBox);
            // 保持透明，不额外加填充；靠 _textMain 主题文字色保证可读
            logBorder.Background = Brushes.Transparent;

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

            // 「安装/修复依赖」：单独跑 install_deps.ps1（强制安装/修复）
            installBtn.Click += (s, e) =>
            {
                var originalBg = installBtn.Background;
                var originalFg = installBtn.Foreground;
                installBtn.IsEnabled = false;
                installBtn.Content = "安装中...";
                installBtn.Background = _accent;
                installBtn.Foreground = _btnPrimaryFg;
                RunInBg(logBox, logf =>
                {
                    var probesDir = ResolveProbesDir();
                    var installPs = Path.Combine(probesDir, "install_deps.ps1");
                    if (!File.Exists(installPs))
                    {
                        logf("[!] 找不到 install_deps.ps1（目录：" + probesDir + "）");
                        return;
                    }
                    logf("[*] 开始安装/修复依赖（Node + Playwright + Chromium）……");
                    // 统一走 RunPowerShellScript（-EncodedCommand + 强制 UTF-8），与抓取路径一致，避免重定向输出乱码
                    RunPowerShellScript(probesDir, installPs, logf);
                    logf("[✓] 依赖安装流程结束，可回到上方点击「抓取直链」。");
                }, "依赖安装结束", () =>
                {
                    installBtn.IsEnabled = true;
                    installBtn.Content = "安装/修复依赖";
                    installBtn.Background = originalBg;
                    installBtn.Foreground = originalFg;
                });
            };

            return root;
        }
    }
}
