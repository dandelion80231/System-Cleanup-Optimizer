using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public partial class MainWindow
    {

        /// <summary>MakeCheck 复选框勾选态的语义：勾选 = 禁用，还是勾选 = 启用。</summary>
        private enum CheckSemantics { CheckedMeansDisable, CheckedMeansEnable }

        /// <summary>构造带线条下拉箭头的 Expander（替代系统默认实心三角箭头）。</summary>
        private Expander MakeLineArrowExpander(UIElement header, UIElement content, bool expanded = true, Thickness? margin = null)
        {
            var expander = new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = expanded,
                Margin = margin ?? new Thickness(0, 6, 0, 2),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var template = new ControlTemplate(typeof(Expander));

            // 外层：标题 ToggleButton 在上，内容在下
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            // HeaderSite：ToggleButton，IsChecked 与 Expander.IsExpanded 双向绑定
            var headerSite = new FrameworkElementFactory(typeof(ToggleButton), "HeaderSite");
            headerSite.SetValue(DockPanel.DockProperty, Dock.Top);
            headerSite.SetValue(ToggleButton.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            headerSite.SetValue(ToggleButton.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
            headerSite.SetValue(ToggleButton.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            headerSite.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
            headerSite.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
            headerSite.SetValue(ToggleButton.PaddingProperty, new Thickness(0));
            headerSite.SetBinding(ToggleButton.IsCheckedProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(Expander.IsExpandedProperty),
                Mode = BindingMode.TwoWay
            });

            // 关键：把 Expander.Header 绑定到 HeaderSite 的 Content，否则模板内 ContentPresenter 无内容显示
            headerSite.SetBinding(ToggleButton.ContentProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(Expander.HeaderProperty)
            });
            headerSite.SetBinding(ToggleButton.ContentTemplateProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(Expander.HeaderTemplateProperty)
            });

            // ToggleButton 模板：箭头在左，标题文字在右
            var tbTemplate = new ControlTemplate(typeof(ToggleButton));
            var tbBorder = new FrameworkElementFactory(typeof(Border));
            tbBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            var tbGrid = new FrameworkElementFactory(typeof(Grid));
            tbGrid.SetValue(Grid.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            tbGrid.SetValue(Grid.VerticalAlignmentProperty, VerticalAlignment.Center);

            var tbCol0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            tbCol0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var tbCol1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            tbCol1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            tbGrid.AppendChild(tbCol0);
            tbGrid.AppendChild(tbCol1);

            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "Arrow");
            arrow.SetValue(Grid.ColumnProperty, 0);
            arrow.SetValue(System.Windows.Shapes.Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(System.Windows.Shapes.Path.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(System.Windows.Shapes.Path.MarginProperty, new Thickness(0, 0, 6, 0));
            arrow.SetValue(System.Windows.Shapes.Path.RenderTransformOriginProperty, new Point(0.5, 0.5));
            UiShapes.ConfigureChevronFactory(arrow, _accent);
            tbGrid.AppendChild(arrow);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetBinding(ContentPresenter.ContentProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(ToggleButton.ContentProperty)
            });
            cp.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(ToggleButton.ContentTemplateProperty)
            });
            tbGrid.AppendChild(cp);

            tbBorder.AppendChild(tbGrid);
            tbTemplate.VisualTree = tbBorder;

            // 折叠时箭头朝右：旋转 90°
            var collapsedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
            collapsedTrigger.Setters.Add(new Setter(System.Windows.Shapes.Path.RenderTransformProperty, new RotateTransform(90), "Arrow"));
            tbTemplate.Triggers.Add(collapsedTrigger);

            headerSite.SetValue(ToggleButton.TemplateProperty, tbTemplate);

            // ExpandSite：内容区域
            var expandSite = new FrameworkElementFactory(typeof(ContentPresenter), "ExpandSite");
            expandSite.SetValue(DockPanel.DockProperty, Dock.Bottom);
            expandSite.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            expandSite.SetBinding(ContentPresenter.ContentProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(Expander.ContentProperty)
            });
            expandSite.SetBinding(ContentPresenter.VisibilityProperty, new Binding
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(Expander.IsExpandedProperty),
                Converter = new BooleanToVisibilityConverter()
            });

            dock.AppendChild(headerSite);
            dock.AppendChild(expandSite);
            template.VisualTree = dock;

            expander.Template = template;
            return expander;
        }

        // =====================================================================
        //  Module: 服务优化（保持不变）
        // =====================================================================

        // 服务优化页缓存（与常用软件/Appx 页同款降级方案：整页实例缓存）。
        // 服务列表构建时同步枚举，禁用状态由后台 AutoLoad 并行读取（每服务一次 sc qc，必须在后台线程）；
        // 再次进页复用已构建面板，仅通过 _servicesRefresh 复位按钮并后台重刷状态，避免每次导航重建 ~20 行控件与重复枚举。
        private readonly PageCache<UIElement> _servicesCache = new PageCache<UIElement>();

        private UIElement BuildServices()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅后台刷新服务状态
            var cached = _servicesCache.TryGet(buildDark);
            if (cached != null) return cached;

            var root = new StackPanel();
            root.Children.Add(Header("服务优化", "列出可安全禁用的后台服务，一键禁用/恢复。"));

            var card = Card();
            var inner = new StackPanel();
            var pb = MakeProgress();
            var log = MakeLogBox();

            // 先建骨架：按钮初始为"检测中…"(禁用)，后台读取状态后再刷新。
            // 关键：ServiceOptimizer.IsDisabled 会为每个服务 spawn 一次 sc qc 子进程，
            // 若在主线程串行执行 20 次会卡住 UI 数秒——必须在后台线程并行读取。
            var btnByService = new Dictionary<string, Button>();
            foreach (var s in ServiceOptimizer.All)
            {
                var row = new Grid { Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = s.Display + "  [" + RiskLabel(s.Risk) + "]", FontWeight = FontWeights.SemiBold, Foreground = _textMain });
                info.Children.Add(new TextBlock { Text = s.Desc, FontSize = 11.5, Foreground = _textDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                Grid.SetColumn(info, 0);

                // 初始按钮：检测中（加载完成前禁用，避免点击无响应；稍后异步刷新为 禁用/恢复）
                var btn = Btn("检测中…", false, null, 90);
                btn.IsEnabled = false;
                Grid.SetColumn(btn, 1);

                row.Children.Add(info);
                row.Children.Add(btn);
                // 整行悬浮高亮：鼠标移到行上时整行变色
                row.MouseEnter += (s, e) => { if (row.Background == Brushes.Transparent) row.Background = _rowHover; };
                row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; };
                inner.Children.Add(row);
                inner.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 0), Background = _panelBorder });

                btnByService[s.Name] = btn;
            }
            inner.Children.Add(pb);
            inner.Children.Add(WrapLogBox(log));
            card.Child = inner;
            root.Children.Add(card);

            // 后台并行读取每个服务的禁用状态，再回到 UI 线程刷新按钮（绝不阻塞 UI 线程）
            // 静默加载，不显示进度条（避免切换页面时感觉慢）
            // 提取为局部方法：首次构建与缓存命中后的 _servicesRefresh 共用（保留原后台服务状态刷新逻辑，不重复）
            void RefreshServiceStates()
            {
                AutoLoad(() =>
                {
                    RunInBg(log, l =>
                    {
                        var states = new ConcurrentDictionary<string, bool>();
                        Parallel.ForEach(ServiceOptimizer.All, new ParallelOptions { MaxDegreeOfParallelism = 6 }, s =>
                        {
                            bool dis = false;
                            try { dis = ServiceOptimizer.IsDisabled(s.Name); } catch { dis = false; }
                            states[s.Name] = dis;
                        });
                        try { Dispatcher.Invoke(() =>
                        {
                            foreach (var s in ServiceOptimizer.All)
                            {
                                if (!btnByService.TryGetValue(s.Name, out var btn)) continue;
                                bool dis = states.TryGetValue(s.Name, out var d) && d;
                                btn.Content = dis ? "恢复" : "禁用";
                                btn.IsEnabled = true;
                                // 仅首次挂 Click（btn.Tag 记录当前禁用态作去重标志），缓存复用重刷时不重复挂载，避免一次点击触发两次 Apply
                                if (btn.Tag == null)
                                {
                                    btn.Click += (sender, e) =>
                                    {
                                        bool curDis = btn.Tag is bool tb && tb;
                                        pb.Visibility = Visibility.Visible;
                                        RunInBg(log, l2 => ServiceOptimizer.Apply(s, !curDis, l2),
                                            curDis ? "已恢复: " + s.Display : "已禁用: " + s.Display,
                                            () => { _servicesCache.Invalidate(); pb.Visibility = Visibility.Collapsed; btn.Tag = !curDis; btn.Content = !curDis ? "恢复" : "禁用"; });
                                    };
                                }
                                btn.Tag = dis;
                            }
                            pb.Visibility = Visibility.Collapsed;
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }, "服务状态已加载");
                });
            }
            RefreshServiceStates();

            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _servicesCache.Set(root, buildDark);
                _servicesCache.SetRefresh(() =>
                {
                    // 复位动态状态（与旧版每次新建页面行为一致）：复位各服务按钮为"检测中…"禁用态、后台重刷服务状态
                    foreach (var s in ServiceOptimizer.All)
                    {
                        if (btnByService.TryGetValue(s.Name, out var b))
                        {
                            b.Content = "检测中…";
                            b.IsEnabled = false;
                        }
                    }
                    RefreshServiceStates();
                });
            }

            return root;
        }

        // =====================================================================
        //  Module: Edge / WebView2 管理（参考 Win11EasyConfig Form3 设计，独立实现，两列布局）
        // =====================================================================

        // flags 行 ComboBox 引用（一键优化/恢复后据此刷新 SelectedItem，使 UI 反映新值）
        private readonly List<ComboBox> _edgeFlagCombos = new List<ComboBox>();

        /// <summary>RefreshEdgeFlagCombos 设置 ComboBox.SelectedItem 时临时为 true，防止 SelectionChanged 事件误清注册表（因为 ApplyEdgeFlag 把 Recommend==Values[0] 的值误判为默认）。</summary>
        private bool _suppressFlagEvents;

        private UIElement BuildEdge()
        {
            // 重置 flag 组合框引用列表（每次 BuildEdge 重建时刷新）
            _edgeFlagCombos.Clear();

            var root = new StackPanel();
            root.Children.Add(Header("Edge / WebView2", "Edge 浏览器（含 Stable/Beta/Dev/Canary/SxS）和 WebView2 Runtime 的安装、卸载、自动更新、启动增强控制，及 flags（edge://flags）一键修改与重启生效。"));

            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 100;
            var logBorder = WrapLogBox(log);

            // 2 列 Grid（左右各 1 个独立 Card）
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });   // 间距
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // 单列模式需要第二行；双列模式只用第 0 行。行定义由 applyEdgeColumns 按需重建。
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 5 频道标识
            string[] channels = { "stable", "beta", "dev", "canary", "sxs" };
            string[] displayNames = { "正式版 (Stable)", "测试版 (Beta)", "开发版 (Dev)", "金丝雀版 (Canary)", "侧载版 (SxS)" };

            // ===== 左列：Edge 浏览器 =====
            var leftCard = Card();
            var leftInner = (StackPanel)leftCard.Child;
            leftInner.Children.Add(new TextBlock { Text = "📦 Edge 浏览器", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 5 频道状态紧凑网格（每行 2 频道，最后一行只有 1 个）
            var chGrid = new Grid();
            for (int i = 0; i < 5; i += 2)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                // 关键边界：保证 idx < channels.Length，避免越界（最后一行只有 1 个频道）
                for (int j = 0; j < 2 && (i + j) < channels.Length; j++)
                {
                    int idx = i + j;
                    var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 6) };
                    cell.Children.Add(new TextBlock { Text = displayNames[idx], FontSize = 13, Foreground = _textMain, FontWeight = FontWeights.SemiBold });
                    var v = EdgeCore.GetEdgeVersion(channels[idx]);
                    cell.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(v) ? "未安装" : v, Foreground = string.IsNullOrEmpty(v) ? _dangerRed : _successGreen, FontSize = 11.5, FontFamily = new FontFamily("Consolas, Courier New, monospace") });
                    Grid.SetColumn(cell, j * 2);
                    row.Children.Add(cell);
                }
                chGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(row, i / 2);
                chGrid.Children.Add(row);
            }
            leftInner.Children.Add(chGrid);

            // 分隔线
            leftInner.Children.Add(new Border { Height = 1, Background = _panelBorder, Margin = new Thickness(0, 4, 0, 12) });

            // 版本选择 + 操作
            leftInner.Children.Add(new TextBlock { Text = "选择要操作的版本", FontSize = 13, Foreground = _textMain, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var channelCombo = new ComboBox { FontSize = 13, MinHeight = 32, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
            for (int i = 0; i < channels.Length; i++) channelCombo.Items.Add(displayNames[i]);
            channelCombo.SelectedIndex = 0;
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(channelCombo, UiShapes.ComboBoxTheme.Create(
                _inputBg, _inputFg, _windowBg, _panelBorder, _textMain, _rowHover, _rowSelected, _textDim));
            leftInner.Children.Add(channelCombo);

            var actionBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var installBtn = Btn("⬇ 安装/升级", true, () =>
            {
                int idx = channelCombo.SelectedIndex;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => EdgeCore.InstallEdge(channels[idx], l), "Edge " + displayNames[idx] + " 安装完成", () => pb.Visibility = Visibility.Collapsed);
            }, 100);
            installBtn.Margin = new Thickness(0);
            Grid.SetColumn(installBtn, 0);
            actionBar.Children.Add(installBtn);
            var uninstallBtn = Btn("🗑 卸载", false, () =>
            {
                int idx = channelCombo.SelectedIndex;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => EdgeCore.UninstallEdge(channels[idx], false, l), "Edge " + displayNames[idx] + " 卸载完成", () => pb.Visibility = Visibility.Collapsed);
            }, 100);
            uninstallBtn.Margin = new Thickness(0);
            Grid.SetColumn(uninstallBtn, 2);
            actionBar.Children.Add(uninstallBtn);
            leftInner.Children.Add(actionBar);

            // 启动增强
            var sbChk = new System.Windows.Controls.CheckBox
            {
                Content = "禁用 Edge 启动增强（开机启动后台驻留）",
                Foreground = _textMain,
                FontSize = 12.5,
                IsChecked = !EdgeCore.IsStartupBoostEnabled(),
                Margin = new Thickness(0, 4, 0, 0),
                Cursor = Cursors.Hand
            };
            sbChk.Click += (s, e) =>
            {
                bool disable = sbChk.IsChecked == true;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => EdgeCore.SetStartupBoost(!disable, l), disable ? "已禁用启动增强" : "已启用启动增强", () => pb.Visibility = Visibility.Collapsed);
            };
            leftInner.Children.Add(sbChk);

            // 顶部留间距
            leftInner.Children.Add(new TextBlock { Height = 6 });

            // 一键优化 / 一键恢复按钮（2 列等宽 Grid）
            var flagBatchBar = new Grid();
            flagBatchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            flagBatchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            flagBatchBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var applyAllBtn = Btn("⚡ 一键优化", true, () => {
                if (MessageBox.Show(
                    "一键把 11 项 Edge 实验性 flags 设为推荐值（性能类 9 项启用 + Copilot 禁用 + ANGLE 默认），\n并强制重启 Edge 浏览器使 flags 立即生效。\n\n⚠ Edge 浏览器将自动重启，未保存的标签页/表单会丢失。\n\n确认执行？",
                    "一键优化 Edge flags", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => {
                    EdgeCore.ApplyAllRecommendedFlags(l);
                    EdgeCore.ForceRestartEdge(l);
                }, "⚡ Edge flags 一键优化完成 + 已重启 Edge", () => { RefreshEdgeFlagCombos(); pb.Visibility = Visibility.Collapsed; });
            }, 0);
            Grid.SetColumn(applyAllBtn, 0);
            flagBatchBar.Children.Add(applyAllBtn);

            var clearAllBtn = Btn("↩ 一键恢复默认", false, () => {
                if (MessageBox.Show(
                    "清除本程序管理的 11 项 flags 注册表值，恢复 Edge 出厂默认。\n并强制重启 Edge 浏览器使 flags 立即生效。\n\n⚠ Edge 浏览器将自动重启，未保存的标签页/表单会丢失。\n\n确认执行？",
                    "一键恢复 Edge flags", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => {
                    EdgeCore.ClearAllEdgeFlags(l);
                    EdgeCore.ForceRestartEdge(l);
                }, "↩ Edge flags 已恢复默认 + 已重启 Edge", () => { RefreshEdgeFlagCombos(); pb.Visibility = Visibility.Collapsed; });
            }, 0);
            Grid.SetColumn(clearAllBtn, 2);
            flagBatchBar.Children.Add(clearAllBtn);

            leftInner.Children.Add(flagBatchBar);
            leftInner.Children.Add(new TextBlock 
            { 
                Text = "⚡ 应用 11 项 flags 推荐值（性能类启用、Copilot 禁用、ANGLE 默认）；↩ 清除所有 flags 注册表值恢复出厂。两项都会强制重启 Edge 让 flags 立即生效。", 
                Foreground = _textDim, 
                FontSize = 11.5, 
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap 
            });

            Grid.SetColumn(leftCard, 0);
            mainGrid.Children.Add(leftCard);

            // ===== 右列：WebView2 + 自动更新 =====
            var rightCard = Card();
            var rightInner = (StackPanel)rightCard.Child;
            rightInner.Children.Add(new TextBlock { Text = "🌐 WebView2 Runtime", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 全局 WebView2（带版本）
            var (w1Sp, w1Ver) = MakeEdgeRowInfo("全局 WebView2", EdgeCore.GetWebView2Version());
            var w1Row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            w1Row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            w1Row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(w1Sp, 0);
            w1Row.Children.Add(w1Sp);
            var w1Btn = Btn("⬇ 安装/升级", true, () =>
            {
                pb.Visibility = Visibility.Visible;
                // 改为 RunInBg 异步执行：下载+安装是网络/IO 操作，同步跑会卡住 UI 线程（进度条来不及渲染即 Collapsed）
                RunInBg(log, EdgeCore.InstallWebView2, "WebView2 安装/升级完成", () => pb.Visibility = Visibility.Collapsed);
            }, 110);
            Grid.SetColumn(w1Btn, 1);
            w1Row.Children.Add(w1Btn);
            rightInner.Children.Add(w1Row);

            // 当前用户 WebView2
            var (w2Sp, w2Ver) = MakeEdgeRowInfo("当前用户 WebView2", EdgeCore.GetWebView2CurrentUserVersion());
            var w2Row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            w2Row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            w2Row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(w2Sp, 0);
            w2Row.Children.Add(w2Sp);
            var w2Btn = Btn("🗑 卸载", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, EdgeCore.UninstallWebView2, "WebView2 卸载完成", () => pb.Visibility = Visibility.Collapsed);
            }, 110);
            Grid.SetColumn(w2Btn, 1);
            w2Row.Children.Add(w2Btn);
            rightInner.Children.Add(w2Row);

            // 分隔线
            rightInner.Children.Add(new Border { Height = 1, Background = _panelBorder, Margin = new Thickness(0, 4, 0, 12) });

            // 自动更新控制
            rightInner.Children.Add(new TextBlock { Text = "⚙ 自动更新控制", FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var updateBar = new Grid();
            updateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            updateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var blockBtn = Btn("🚫 禁止自动更新", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, EdgeCore.BlockEdgeUpdate, "已禁止自动更新", () => pb.Visibility = Visibility.Collapsed);
            }, 100);
            blockBtn.Margin = new Thickness(0);
            Grid.SetColumn(blockBtn, 0);
            updateBar.Children.Add(blockBtn);
            var restoreBtn = Btn("✅ 恢复自动更新", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, EdgeCore.RestoreEdgeUpdate, "已恢复自动更新", () => pb.Visibility = Visibility.Collapsed);
            }, 100);
            restoreBtn.Margin = new Thickness(0);
            Grid.SetColumn(restoreBtn, 2);
            updateBar.Children.Add(restoreBtn);
            rightInner.Children.Add(updateBar);

            // ===== 实验性功能 (edge://flags) =====
            rightInner.Children.Add(new TextBlock { Text = "⚙ Edge 实验性功能 (edge://flags)", FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 4) });

            // flag 值 → 用户可读显示名映射（use-angle / edge-copilot-mode 枚举 + 开关类 "1"/"0"）
            var flagDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = "默认",
                ["gl"] = "GL (OpenGL)",
                ["d3d11"] = "D3D11",
                ["d3d11on12"] = "D3D11on12",
                ["vulkan"] = "Vulkan",
                ["swiftshader"] = "SwiftShader",
                ["disabled"] = "禁用",
                ["enabled"] = "启用",
                ["optin"] = "Opt-in",
                ["1"] = "启用",
                ["0"] = "禁用"
            };

            // 按元数据动态生成行（不硬编码 flag 名）；def.Recommend 命中的枚举项追加 " ⭐"
            for (int f = 0; f < EdgeCore.EdgeFlagDefs.Length; f++)
            {
                var def = EdgeCore.EdgeFlagDefs[f];
                bool isLast = f == EdgeCore.EdgeFlagDefs.Length - 1;

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = def.Label,
                    Foreground = _textMain,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (!isLast) label.Margin = new Thickness(0, 4, 0, 0);
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var cb = new ComboBox
                {
                    Width = 150,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontSize = 12.5,
                    Tag = def.Key
                };
                // 首个固定项「默认 (Default)」：Tag=null 代表 Edge 出厂默认（写入时删除恢复）
                // 推荐值为 default（或空）时，「默认 (Default)」项加 ⭐（推荐 = 不写注册表、保持 Edge 出厂默认）
                bool recIsDefault = string.IsNullOrEmpty(def.Recommend) || def.Recommend == "default";
                cb.Items.Add(new ComboBoxItem { Content = "默认 (Default)" + (recIsDefault ? " ⭐" : ""), Tag = null });
                foreach (var v in def.Values)
                {
                    string display = flagDisplayNames.TryGetValue(v, out var dn) ? dn : v;
                    cb.Items.Add(new ComboBoxItem { Content = display + (def.Recommend == v ? " ⭐" : ""), Tag = v });
                }
                Grid.SetColumn(cb, 1);
                row.Children.Add(cb);

                // 初始选中当前注册表状态；未设置(null)或旧值不匹配时回退「默认 (Default)」
                var isInit = true;
                cb.SelectionChanged += (s, e) =>
                {
                    if (cb.SelectedItem is ComboBoxItem it && !isInit && !_suppressFlagEvents)
                    {
                        EdgeCore.ApplyEdgeFlag((string)cb.Tag, it.Tag as string);
                    }
                };
                string cur = EdgeCore.GetEdgeFlag(def.Key);
                ComboBoxItem hit = null;
                foreach (ComboBoxItem it in cb.Items)
                {
                    if ((cur == null && it.Tag == null) || (cur != null && cur.Equals(it.Tag))) { hit = it; break; }
                }
                cb.SelectedItem = hit ?? (ComboBoxItem)cb.Items[0];
                isInit = false;

                _edgeFlagCombos.Add(cb);

                rightInner.Children.Add(row);
            }

            rightInner.Children.Add(new TextBlock { Text = "⚠ 修改需重启 Edge 才生效；选「默认」即恢复出厂设置；⭐ 为推荐值", Foreground = _textDim, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) });

            Grid.SetColumn(rightCard, 2);
            mainGrid.Children.Add(rightCard);

            // 双列 ⇄ 单列切换。
            // 阈值推导：两张卡片内部都有「标签 + ComboBox」行和双按钮操作行，
            // 单张卡片舒适宽度约 350px（低于此值按钮文字会被挤压/换行错位），
            // 故双列最少需要 350 × 2 + 12(列间距) = 712px，取 720 并留少量余量防临界抖动。
            // 折叠后两张卡片上下堆叠，各自宽度回到整个视口，内容完整可见。
            const double EdgeTwoColumnMinWidth = 720;
            Action<bool> applyEdgeColumns = twoColumns =>
            {
                mainGrid.RowDefinitions.Clear();
                if (twoColumns)
                {
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(leftCard, 0);  Grid.SetColumn(leftCard, 0);  Grid.SetColumnSpan(leftCard, 1);
                    Grid.SetRow(rightCard, 0); Grid.SetColumn(rightCard, 2); Grid.SetColumnSpan(rightCard, 1);
                }
                else
                {
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });   // 两卡之间的间距
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(leftCard, 0);  Grid.SetColumn(leftCard, 0);  Grid.SetColumnSpan(leftCard, 3);
                    Grid.SetRow(rightCard, 2); Grid.SetColumn(rightCard, 0); Grid.SetColumnSpan(rightCard, 3);
                }
            };
            applyEdgeColumns(true);   // 先按双列摆好，随后由自适应按实际宽度纠正
            EnableTwoColumnResponsive(root, EdgeTwoColumnMinWidth, applyEdgeColumns);

            root.Children.Add(mainGrid);
            root.Children.Add(pb);
            root.Children.Add(logBorder);
            return root;
        }

        /// <summary>根据当前注册表值刷新 flags ComboBox 的 SelectedItem，确保 UI 反映新值。
        /// 三重保险：① Dispatcher 强制 UI 线程 ② 用 SelectedIndex 而非 SelectedItem（引用更稳） ③ 设完调 InvalidateVisual 强制重绘。
        /// 调试：写 Debug 输出每次的实际值，便于诊断。</summary>
        private void RefreshEdgeFlagCombos()
        {
            // 1. 强制 UI 线程（防御性：onDone 通常已在 UI 线程，但 Dispatcher 包裹无副作用）
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshEdgeFlagCombos));
                return;
            }

            _suppressFlagEvents = true;
            try
            {
                for (int i = 0; i < EdgeCore.EdgeFlagDefs.Length && i < _edgeFlagCombos.Count; i++)
                {
                    var def = EdgeCore.EdgeFlagDefs[i];
                    var cb = _edgeFlagCombos[i];
                    if (cb == null) continue;

                    string current = EdgeCore.GetEdgeFlag(def.Key);

                    // 找 idx（按 ComboBoxItem.Tag 严格匹配；找不到回退 0）
                    int idx = 0;
                    for (int k = 0; k < cb.Items.Count; k++)
                    {
                        if (cb.Items[k] is System.Windows.Controls.ComboBoxItem item)
                        {
                            bool match = (current == null && item.Tag == null)
                                         || (current != null && item.Tag != null && current.Equals(item.Tag));
                            if (match) { idx = k; break; }
                        }
                    }

                    // 强重置：先清空再设索引（防止旧 SelectedItem 缓存/事件订阅导致视觉不更新）
                    cb.SelectedItem = null;
                    cb.SelectedIndex = -1;
                    cb.SelectedIndex = idx;
                    cb.UpdateLayout();
                    cb.InvalidateVisual();

                    // 诊断 ToolTip：鼠标悬停就能看到 Refresh 读到的 cur 和设的 idx
                    cb.ToolTip = $"cur={(current ?? "<null>"),-12} → idx={idx} / {cb.Items.Count}";

                    // Debug 输出（VS 输出窗口可见）
                    System.Diagnostics.Debug.WriteLine($"[Flag] {def.Key,-40} cur={(current ?? "<null>"),-12} idx={idx} items={cb.Items.Count}");
                }
            }
            finally
            {
                _suppressFlagEvents = false;
            }
        }

        // 辅助：构建 Edge 状态行（名称 + 版本号），返回 (StackPanel, versionText)
        private (StackPanel, TextBlock) MakeEdgeRowInfo(string label, string version)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = label + "：", FontSize = 13, Foreground = _textMain, FontWeight = FontWeights.SemiBold });
            var verTb = new TextBlock { Text = string.IsNullOrEmpty(version) ? "未安装" : version, Foreground = string.IsNullOrEmpty(version) ? _dangerRed : _successGreen, FontSize = 11.5, FontFamily = new FontFamily("Consolas, Courier New, monospace") };
            sp.Children.Add(verTb);
            return (sp, verTb);
        }

        // =====================================================================
        //  Module: 隐私设置（完整版，匹配 Win11EasyConfig Form5：12+ 项 CheckBox 直接切换）
        // =====================================================================

        private UIElement BuildPrivacy()
        {
            var root = new StackPanel();
            root.Children.Add(Header("隐私设置", "云搜索 / Web 搜索 / 广告ID / 遥测 / 传递优化 等 12+ 项，全部可逆。CheckBox 直接切换状态。"));

            var card = Card();
            var inner = new StackPanel();
            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 100;
            var logBorder = WrapLogBox(log);

            // 状态由各 MakeCheck / 按钮内联展示，无需 ShowStatus() 汇总块

            // 直接点击 CheckBox 切换（对齐 Win11EasyConfig Form5 模式）
            void MakeCheck(string title, Func<bool> getState, Action<Action<string>, bool> apply, string desc = "", CheckSemantics semantics = CheckSemantics.CheckedMeansDisable)
            {
                var item = new Border
                {
                    Background = _isDarkMode ? Brushes.Transparent : _bgTable,
                    BorderBrush = _panelBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var sp = new StackPanel();
                bool initial = false;
                try { initial = getState(); } catch { }
                var chk = new System.Windows.Controls.CheckBox
                {
                    Content = title,
                    IsChecked = initial,
                    Foreground = _textMain,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                sp.Children.Add(chk);
                if (!string.IsNullOrEmpty(desc))
                    sp.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = _textDim, Margin = new Thickness(20, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
                chk.Click += (s, e) =>
                {
                    bool want = chk.IsChecked == true;
                    pb.Visibility = Visibility.Visible;
                    string verb = (semantics == CheckSemantics.CheckedMeansDisable)
                        ? (want ? "已禁用: " : "已启用: ")
                        : (want ? "已启用: " : "已禁用: ");
                    RunInBg(log, l => apply(l, want), verb + title,
                        () => { pb.Visibility = Visibility.Collapsed; });
                };
                item.Child = sp;
                // 鼠标悬停高亮（与 AddRow + 常用软件列表统一风格）
                var mcOrigBg = item.Background;
                item.MouseEnter += (s, e) => { ((Border)s).Background = _rowHover; };
                item.MouseLeave += (s, e) => { ((Border)s).Background = mcOrigBg; };
                inner.Children.Add(item);
            }

            // ---- 搜索组 ----
            inner.Children.Add(new TextBlock { Text = "🔍 搜索设置", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 6) });
            MakeCheck("关闭搜索栏云端结果（OneDrive / SharePoint / Outlook / Bing）",
                () => PrivacyCore.IsCloudSearchDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableCloudSearch(l); else PrivacyCore.EnableCloudSearch(l); });
            MakeCheck("关闭搜索栏联网结果（当前用户）",
                () => PrivacyCore.IsWebSearchDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableWebSearch(l); else PrivacyCore.EnableWebSearch(l); });
            MakeCheck("不保留本地搜索历史（当前用户）",
                () => PrivacyCore.IsSearchHistoryDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableSearchHistory(l); else PrivacyCore.EnableSearchHistory(l); });

            // ---- WSearch 服务 + 防火墙规则按钮（四个按钮同一行） ----
            inner.Children.Add(new TextBlock { Text = "🛡 Windows Search 服务与防火墙", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 6) });
            // 等宽均分整行：Grid(4×★Star) + 按钮居中、保持原始大小（与安全防护更新按钮行一致）
            var serviceBar = new Grid { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            serviceBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serviceBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serviceBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serviceBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void AddSvcBtn(string text, Action onClick, int col, double minW)
            {
                var b = Btn(text, false, onClick, minW);
                b.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(b, col);
                serviceBar.Children.Add(b);
            }
            AddSvcBtn("停止并禁止Windows Search服务", () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.BlockWSearchService, "Windows Search 服务已禁用", () => { pb.Visibility = Visibility.Collapsed; });
            }, 0, 210);
            AddSvcBtn("恢复并允许Windows Search服务", () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.AllowWSearchService, "Windows Search 服务已恢复", () => { pb.Visibility = Visibility.Collapsed; });
            }, 1, 210);
            AddSvcBtn("添加防火墙规则(阻止SearchHost联网)", () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.AddSearchFirewallRule, "防火墙规则已添加", () => { pb.Visibility = Visibility.Collapsed; });
            }, 2, 230);
            AddSvcBtn("移除防火墙规则", () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.RemoveSearchFirewallRule, "防火墙规则已移除", () => { pb.Visibility = Visibility.Collapsed; });
            }, 3, 170);
            inner.Children.Add(serviceBar);

            // ---- 更新组 ----
            inner.Children.Add(new TextBlock { Text = "📦 Windows 更新", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 6) });
            MakeCheck("禁止Windows更新传递优化",
                () => PrivacyCore.IsDeliveryOptimizationDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableDeliveryOptimization(l); else PrivacyCore.EnableDeliveryOptimization(l); });
            MakeCheck("Windows更新不包括恶意软件删除工具(MRT)",
                () => PrivacyCore.IsMRTUpdateDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableMRTUpdate(l); else PrivacyCore.EnableMRTUpdate(l); });
            MakeCheck("禁止Win大版本更新(如23H2->24H2)",
                () => PrivacyCore.IsFeatureUpdateBlocked(),
                (l, b) => { if (b) PrivacyCore.BlockFeatureUpdate(l); else PrivacyCore.UnblockFeatureUpdate(l); });
            MakeCheck("禁止Windows遥测数据收集",
                () => PrivacyCore.IsTelemetryDisabled(),
                (l, b) => { if (b) PrivacyCore.DisableTelemetry(l); else PrivacyCore.EnableTelemetry(l); });

            // ---- 隐私与安全组 ----
            inner.Children.Add(new TextBlock { Text = "🔒 隐私与安全（仅当前用户）", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 6) });
            MakeCheck("允许Windows收集活动历史记录",
                () => !PrivacyCore.IsActivityHistoryDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableActivityHistory(l); else PrivacyCore.DisableActivityHistory(l); }, semantics: CheckSemantics.CheckedMeansEnable);
            MakeCheck("允许应用使用广告ID展示个性化广告",
                () => !PrivacyCore.IsAdIDDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableAdvertisingID(l); else PrivacyCore.DisableAdvertisingID(l); }, semantics: CheckSemantics.CheckedMeansEnable);
            MakeCheck("允许网站通过访问语言列表来显示本地相关内容",
                () => !PrivacyCore.IsLanguageListDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableLanguageList(l); else PrivacyCore.DisableLanguageList(l); }, semantics: CheckSemantics.CheckedMeansEnable);
            MakeCheck("允许Windows跟踪应用启动以改进搜索结果",
                () => !PrivacyCore.IsAppStartTrackingDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableAppStartTracking(l); else PrivacyCore.DisableAppStartTracking(l); }, semantics: CheckSemantics.CheckedMeansEnable);
            MakeCheck("在设置应用中为我显示建议的内容",
                () => !PrivacyCore.IsSuggestedContentDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableSuggestedContent(l); else PrivacyCore.DisableSuggestedContent(l); }, semantics: CheckSemantics.CheckedMeansEnable);
            MakeCheck("自定义墨迹书写和键入词典",
                () => !PrivacyCore.IsInkDictDisabled(),
                (l, b) => { if (b) PrivacyCore.EnableInkDict(l); else PrivacyCore.DisableInkDict(l); }, semantics: CheckSemantics.CheckedMeansEnable);

            // ---- 开始菜单推荐行数 ----
            inner.Children.Add(new TextBlock { Text = "📐 开始菜单推荐的项目显示行数", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 8, 0, 6) });
            var layoutBar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int li = 0; li < 3; li++)
                layoutBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int currentLayout = PrivacyCore.GetStartLayout();
            void MakeLayoutBtn(int rows, string label, int col)
            {
                var rb = new System.Windows.Controls.RadioButton
                {
                    Content = label,
                    GroupName = "StartLayoutGroup",
                    IsChecked = currentLayout == (rows == 1 ? 1 : rows == 4 ? 2 : 0),
                    Foreground = _textMain,
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                rb.Click += (s, e) =>
                {
                    if (rb.IsChecked != true) return;
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, l => PrivacyCore.SetStartLayout(rows, l), "已设置为 " + label, () => pb.Visibility = Visibility.Collapsed);
                };
                Grid.SetColumn(rb, col);
                layoutBar.Children.Add(rb);
            }
            MakeLayoutBtn(1, "一行", 0);
            MakeLayoutBtn(3, "三行（默认）", 1);
            MakeLayoutBtn(4, "四行", 2);
            inner.Children.Add(layoutBar);

            inner.Children.Add(pb);
            inner.Children.Add(logBorder);
            card.Child = inner;
            root.Children.Add(card);
            return root;
        }

        // =====================================================================
        //  Module: 系统信息（自动采集 + 复制/导出 TXT）
        // =====================================================================

        private string _lastSystemInfo = "";

        /// <summary>
        /// 窄窗自适应：视口宽度不足时把两列布局折叠成单列，宽度够时恢复双列。
        ///
        /// ★ 为什么不用「给页面外面包一层 HorizontalScrollBarVisibility=Auto 的 ScrollViewer」？
        ///   那是个陷阱，对**任何一层** ScrollViewer 都成立：ScrollViewer 会把 CanHorizontallyScroll
        ///   设为 (H != Disabled)，于是 ScrollContentPresenter.MeasureOverride 用「无限宽」度量子内容；
        ///   而 Grid.MeasureOverride 在无限宽下【不解析 Star 列】，Star 退化成内容宽度，
        ///   只有 Arrange 阶段才按最终宽度重分配。放开横滚等于把全局布局 bug 缩小到单页，
        ///   长文本不换行、Star 列不再等分、底部常驻横条，问题并没有消失。
        ///   （这也是全局 ContentArea.HorizontalScrollBarVisibility 必须保持 Disabled 的原因，
        ///    详见 MainWindow.xaml.cs 中该行处的注释。）
        ///
        /// 所以正确解法是「响应式重排」：窄了就少用一列，让内容按有限宽度正常排版。
        /// </summary>
        /// <param name="viewportElement">宽度随视口变化的元素（一般是页面 root，其在 ContentArea 内宽度有限）</param>
        /// <param name="minWidthForTwoColumns">维持双列所需的最小宽度（低于此值折叠为单列）</param>
        /// <param name="applyTwoColumn">切换回调，true=双列 / false=单列；仅在模式真正变化时才调用</param>
        private static void EnableTwoColumnResponsive(
            FrameworkElement viewportElement,
            double minWidthForTwoColumns,
            Action<bool> applyTwoColumn)
        {
            if (viewportElement == null || applyTwoColumn == null) return;

            // 当前模式（null = 尚未确定）。SizeChanged 在布局过程中会反复触发，
            // 必须靠它挡住"模式没变却反复重排"造成的抖动甚至布局死循环。
            bool? current = null;
            viewportElement.SizeChanged += (s, e) =>
            {
                try
                {
                    bool want = e.NewSize.Width >= minWidthForTwoColumns;
                    if (current == want) return;   // 模式未变 → 直接返回，不碰布局
                    current = want;
                    applyTwoColumn(want);
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            };
            // 说明：这里用 lambda 挂 SizeChanged 是安全的 —— 每次页面重建都会产生一个新的 root，
            // 旧 root 连同其处理器一起被丢弃，不会跨重建累积（不像在 LoadingRow 里对复用的行容器反复挂）。
        }

        private UIElement BuildSystemInfo()
        {
            // Grid 布局：让内容区在最大化时撑满剩余空间
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 内容卡（撑满）
            int rootRow = 0;

            var headerTb = Header("系统信息", "全面采集 CPU / 内存 / 显卡 / 主板 / 硬盘 / 网络 / 安装日期 等信息。支持复制和导出。");
            Grid.SetRow(headerTb, rootRow++);
            root.Children.Add(headerTb);

            var card = Card();
            // 用 DockPanel 替代 StackPanel，让内容区（TextBox）在最大化时撑满剩余空间
            var inner = new DockPanel();
            var pb = MakeProgress();

            var btnBar = MakeBtnRow(
                Btn("📋 复制到剪贴板", true, () =>
                {
                    if (!string.IsNullOrEmpty(_lastSystemInfo))
                    {
                        Clipboard.SetText(_lastSystemInfo);
                        SetStatus("已复制到剪贴板");
                    }
                }, 150),
                Btn("💾 导出为 TXT...", false, () =>
                {
                    if (string.IsNullOrEmpty(_lastSystemInfo)) return;
                    var dlg = new SaveFileDialog { Filter = "文本文件|*.txt", FileName = "system-info-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
                    if (dlg.ShowDialog() == true)
                    {
                        File.WriteAllText(dlg.FileName, _lastSystemInfo, Encoding.UTF8);
                        SetStatus("已导出到: " + dlg.FileName);
                    }
                }, 150),
                Btn("🔄 重新采集", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(null, l =>
                {
                    var d = SystemInfo.CollectDual();
                    try { Dispatcher.Invoke(() =>
                    {
                        leftInfoBox.Clear(); leftInfoBox.AppendText(d.Left);
                        rightInfoBox.Clear(); rightInfoBox.AppendText(d.Right);
                        _lastSystemInfo = d.Left + "\r\n" + d.Right;
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }, "信息采集完成", () => pb.Visibility = Visibility.Collapsed);
            }));
            btnBar.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(btnBar, Dock.Top);
            inner.Children.Add(btnBar);
            DockPanel.SetDock(pb, Dock.Top);
            inner.Children.Add(pb);

            // 两列布局 - 左侧 TextBox + 右侧 TextBox，中间用 GridSplitter 推拉调整。
            // 窄窗时由 EnableTwoColumnResponsive 折叠成「上下单列」（见下方 applySysInfoColumns），
            // 避免两列各 MinWidth=180 撑不下时内容被硬裁掉。
            var twoColGrid = new Grid();
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 分隔条（宽 1）
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            // 单列模式需要第二行；双列模式只用第 0 行。行定义由 applySysInfoColumns 按需重建。
            twoColGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 左侧
            leftInfoBox = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, 'Courier New', monospace"),
                FontSize = 14,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12, 10, 12, 10),
                AcceptsReturn = true
            };
            var leftBorder = new Border
            {
                Child = leftInfoBox,
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2)
            };
            Grid.SetColumn(leftBorder, 0);
            twoColGrid.Children.Add(leftBorder);

            // 中间可拖拽分隔条
            var splitter = new GridSplitter
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = _panelBorder,
                Cursor = System.Windows.Input.Cursors.SizeWE
            };
            Grid.SetColumn(splitter, 1);
            twoColGrid.Children.Add(splitter);

            // 右侧
            rightInfoBox = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, 'Courier New', monospace"),
                FontSize = 14,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12, 10, 12, 10),
                AcceptsReturn = true
            };
            var rightBorder = new Border
            {
                Child = rightInfoBox,
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2)
            };
            Grid.SetColumn(rightBorder, 2);
            twoColGrid.Children.Add(rightBorder);

            // 双列 ⇄ 单列切换。
            // 阈值推导：双列最少需要 左列 180 + 分隔条 1 + 右列 180 = 361px，
            // 再留约 40px 余量避免临界宽度反复抖动 → 取 400。
            // 单列时两个 TextBox 各占一行（上半 / 下半），各自都有垂直滚动条，
            // 因此内容一条不少，只是从"左右并排"变成"上下堆叠"，不会被裁掉。
            const double SysInfoTwoColumnMinWidth = 400;
            Action<bool> applySysInfoColumns = twoColumns =>
            {
                twoColGrid.RowDefinitions.Clear();
                if (twoColumns)
                {
                    twoColGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    splitter.Visibility = Visibility.Visible;
                    Grid.SetRow(leftBorder, 0); Grid.SetColumn(leftBorder, 0); Grid.SetColumnSpan(leftBorder, 1);
                    Grid.SetRow(splitter, 0);   Grid.SetColumn(splitter, 1);   Grid.SetColumnSpan(splitter, 1);
                    Grid.SetRow(rightBorder, 0); Grid.SetColumn(rightBorder, 2); Grid.SetColumnSpan(rightBorder, 1);
                }
                else
                {
                    twoColGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    twoColGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });   // 两块之间的间距
                    twoColGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    // 单列时没有左右两列可推拉，分隔条必须收起，否则会留下一条突兀的竖线
                    splitter.Visibility = Visibility.Collapsed;
                    Grid.SetRow(leftBorder, 0); Grid.SetColumn(leftBorder, 0); Grid.SetColumnSpan(leftBorder, 3);
                    Grid.SetRow(rightBorder, 2); Grid.SetColumn(rightBorder, 0); Grid.SetColumnSpan(rightBorder, 3);
                }
            };
            applySysInfoColumns(true);   // 先按双列摆好，随后由自适应按实际宽度纠正
            EnableTwoColumnResponsive(root, SysInfoTwoColumnMinWidth, applySysInfoColumns);

            // 左右两列 TextBox 已自带垂直滚动条，不需要外层整体滚动控件
            // 作为 DockPanel 最后一个子元素，自动填满剩余空间
            inner.Children.Add(twoColGrid);  // DockPanel 最后子元素 → 填满剩余空间
            card.Child = inner;
            Grid.SetRow(card, rootRow++);  // Star 行：撑满剩余空间
            root.Children.Add(card);

            // 打开时自动采集（静默加载，不显示进度条，避免切换页面时感觉慢）
            AutoLoad(() =>
            {
                RunInBg(null, l =>
                {
                    var d = SystemInfo.CollectDual();
                    try { Dispatcher.Invoke(() =>
                    {
                        leftInfoBox.AppendText(d.Left);
                        rightInfoBox.AppendText(d.Right);
                        _lastSystemInfo = d.Left + "\r\n" + d.Right;
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }, "信息采集完成", null);
            });

            // 动态 MaxHeight：最大化时 root 跟随视口拉伸
            // 修正：原注释写「绑定到 ContentArea.ViewportHeight」，实现（MainWindow.Helpers.cs 的
            // BindRootHeightToViewport）绑的是 ContentArea.ActualHeight——只读 DP，尺寸变化时自动通知，
            // 首次布局与窗口缩放均自动跟随，规避了旧手动方案的 vp=0 时序 bug。
            BindRootHeightToViewport(root);

            return root;
        }

        // Issue 23: 系统信息双列 TextBox 字段引用
        private TextBox leftInfoBox;
        private TextBox rightInfoBox;

    }

}

