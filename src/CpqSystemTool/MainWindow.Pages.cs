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
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.ServiceProcess;
using Microsoft.VisualBasic;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // 关于页「下载更新」按钮与待下载版本标签（由 CheckForUpdate 设置）。
        private Button _aboutDownloadUpdateBtn;
        private string _pendingUpdateTag;
        private string _pendingUpdateUrl;   // 从 GitHub API 取得的真实浏览器下载直链（含正确的资产文件名）

        /// <summary>MakeCheck 复选框勾选态的语义：勾选 = 禁用，还是勾选 = 启用。</summary>
        private enum CheckSemantics { CheckedMeansDisable, CheckedMeansEnable }

        /// <summary>在可视化树中查找指定类型的第一个子元素。</summary>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

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
        //  Module: 系统优化（含预设按钮）
        // =====================================================================

        private UIElement BuildTweaks()
        {
            App.Trace("BuildTweaks.start");
            List<TweakEntry> allTweaks;
            try { allTweaks = Tweaks.All; }
            catch (Exception ex)
            {
                App.Trace("BuildTweaks.TweaksAllFailed: " + ex.Message);
                // Tweaks 初始化失败时返回错误提示页（避免空白）
                var errRoot = new StackPanel { Margin = new Thickness(24) };
                errRoot.Children.Add(new TextBlock { Text = "⚙ 系统优化", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = _accent, Margin = new Thickness(0, 0, 0, 12) });
                errRoot.Children.Add(new TextBlock { Text = "优化项加载失败：" + ex.Message, Foreground = _dangerRed, FontSize = 13, TextWrapping = TextWrapping.Wrap });
                errRoot.Children.Add(new TextBlock { Text = "请检查注册表访问权限或重启应用。", Foreground = _textDim, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) });
                return errRoot;
            }

            var root = new DockPanel();

            // Issue 21: 顶部只保留副标题说明（大标题已由顶部 PageTitle 显示）
            var titleBlock = new StackPanel();
            titleBlock.Children.Add(Header("", $"共 {allTweaks.Count} 项优化。勾选=启用优化、取消勾选=恢复默认；点「开始优化」按当前勾选状态应用全部项。优化过的项目前面会打勾。"));
            DockPanel.SetDock(titleBlock, Dock.Top);
            root.Children.Add(titleBlock);

            // ========== 底部按钮栏（基本优化 深度优化 全选 重置 其他 导入 导出 开始优化 还原所有项） ==========
            // 等宽均分整行：Grid(9×★Star) + 按钮居中、保持原始大小（与安全防护更新按钮行一致）
            Func<string, bool, Action, Button> mkBtn = (text, primary, action) =>
            {
                var b = Btn(text, primary, action, 100);
                b.Padding = new Thickness(10, 6, 10, 6);
                b.FontSize = 12;
                return b;
            };

            // 预设按钮互斥高亮：点击后将 accent 填充转移到当前按钮，其他恢复 secondary 样式
            Button btnBasic = null, btnDeep = null, btnSelectAll = null, btnReset = null;
            Action<Button> highlightPreset = active =>
            {
                foreach (var btn in new[] { btnBasic, btnDeep, btnSelectAll, btnReset })
                {
                    if (btn == null) continue;
                    bool on = btn == active;
                    btn.Background = on ? _accent : _btnSecondaryBg;
                    btn.Foreground = on ? _btnPrimaryFg : _btnSecondaryFg;
                    btn.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                    btn.BorderThickness = on ? new Thickness(0) : new Thickness(1);
                    btn.BorderBrush = on ? Brushes.Transparent : _panelBorder;
                }
            };

            Grid btnBar = null;
            btnBasic = mkBtn("基本优化", true, () => { BasicOptimize(); highlightPreset(btnBasic); });
            btnDeep = mkBtn("深度优化", false, () => { DeepOptimize(); highlightPreset(btnDeep); });
            btnSelectAll = mkBtn("全选", false, () => { SelectAll(true); highlightPreset(btnSelectAll); });
            btnReset = mkBtn("重置", false, () => { SelectAll(false); highlightPreset(btnReset); });
            btnBar = MakeBtnRow(
                btnBasic,
                btnDeep,
                btnSelectAll,
                btnReset,
                mkBtn("其他", false, () => OtherMenuPopup(btnBar)),
                mkBtn("导入", false, () => ImportConfig()),
                mkBtn("导出", false, () => ExportConfig()),
                mkBtn("开始优化", true, () => ApplyChecked()),
                mkBtn("还原所有项", false, () => RestoreAll())
            );
            btnBar.Margin = new Thickness(0, 16, 0, 4);
            DockPanel.SetDock(btnBar, Dock.Bottom);
            root.Children.Add(btnBar);

            // ========== 主内容：左侧 Tree + 右侧 已选项目（填满剩余空间） ==========
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 左侧优化项目（撑满）
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });                       // 分隔间距
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });                     // 右侧已选项目
            mainGrid.VerticalAlignment = VerticalAlignment.Stretch;
            // 显式 Star 行：让主内容区填满 DockPanel 剩余高度（无此定义则默认 Auto 行按内容撑高、不填充）
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 左侧：分组 Tree（垂直填满 Grid 行）
            var leftPanel = new Border { Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Stretch };
            // 左侧：Grid(Auto标签 + Star滚动区) —— Star 行自动填满，ScrollViewer 被高度约束自然滚动，无需手工 MaxHeight
            var leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var leftLabel = new TextBlock { Text = "优化项目:", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(leftLabel, 0);
            leftGrid.Children.Add(leftLabel);

            var treeScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var treePanel = new StackPanel();

            // 先创建右侧已选面板，供 UpdateSelectedPanel 字段提前赋值使用
            var selectedPanel = new StackPanel();
            selectedPanel.Children.Add(new TextBlock { Text = "(尚无选中项)", Foreground = _textDim, FontSize = 12 });

            // 存储所有 CheckBox / TextBlock 用于后续异步刷新状态
            var checkBoxes = new Dictionary<string, System.Windows.Controls.CheckBox>();
            var textBlocks = new Dictionary<string, TextBlock>();
            var groupContents = new Dictionary<string, StackPanel>(StringComparer.Ordinal);
            // 记录用户是否在状态加载完成前手动修改过该项，避免异步结果覆盖用户选择
            TweaksTouched = new HashSet<string>(StringComparer.Ordinal);

            // Issue 3: 按预定顺序排序 7 个分组
            var groupOrder = new Dictionary<string, int>
            {
                ["外观/资源管理器"] = 0,
                ["性能优化"] = 1,
                ["安全设置"] = 2,
                ["Edge优化"] = 3,
                ["系统设置"] = 4,
                ["更新设置"] = 5,
                ["隐私设置"] = 6
            };
            var groups = allTweaks.GroupBy(t => t.Group)
                .OrderBy(g => groupOrder.TryGetValue(g.Key, out var idx) ? idx : 99)
                .ThenBy(g => g.Key);

            // 先创建分组折叠标题（同步，用户能立刻看到分组结构，不会白板）
            foreach (var g in groups)
            {
                var exHeader = new TextBlock { Text = g.Key, FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13.5 };
                var content = new StackPanel { Margin = new Thickness(20, 4, 0, 4) };
                var expander = MakeLineArrowExpander(exHeader, content, true, new Thickness(0, 4, 0, 4));
                treePanel.Children.Add(expander);
                groupContents[g.Key] = content;
            }

            // 提前赋值，让下方 lambda 能捕获字段（页面返回前即完成，避免空引用）
            UpdateSelectedPanelRef = selectedPanel;
            TweaksCheckBoxes = checkBoxes;
            TweaksTextBlocks = textBlocks;
            UpdateSelectedPanel = delegate () { RefreshSelectedList(checkBoxes, selectedPanel); };

            // 同步填充每行 CheckBox + 名称（用默认/未知状态，避免等待注册表读取）
            foreach (var g in groups)
            {
                var content = groupContents[g.Key];
                foreach (var t in g)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var chk = new System.Windows.Controls.CheckBox
                    {
                        IsThreeState = t.IsThreeState,
                        IsChecked = t.IsThreeState ? (bool?)null : false,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Tag = t.Id
                    };
                    Grid.SetColumn(chk, 0); row.Children.Add(chk);
                    checkBoxes[t.Id] = chk;

                    var tb = new TextBlock
                    {
                        Text = t.Name,
                        Foreground = _textMain,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(tb, 1); row.Children.Add(tb);
                    textBlocks[t.Id] = tb;

                    // 单项点击名字 = 等价勾选；勾选后文本变主题色，未选中保持白色/主文本色
                    tb.MouseLeftButtonUp += (s, e) =>
                    {
                        TweaksTouched.Add(t.Id);
                        chk.IsChecked = !chk.IsChecked;
                        SyncTweakTextColor(t.Id);
                        UpdateSelectedPanel();
                        RefreshTweaksStatus();
                    };

                    // Tooltip（三态项附加状态说明）
                    var tip = t.Desc + "  [" + RiskLabel(t.Risk) + "]" + (t.IsThreeState ? "  [三态: 勾选=开 / 留空=系统默认 / 取消=关]" : "");
                    chk.ToolTip = tip;
                    tb.ToolTip = tip;
                    // 整行悬浮高亮
                    row.MouseEnter += (s, e) => { if (row.Background == Brushes.Transparent) row.Background = _rowHover; };
                    row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; };
                    content.Children.Add(row);
                }
            }

            // 检查变更刷新右侧与颜色；标记被用户手动改动过的项，异步状态刷新时不再覆盖
            foreach (var kv in checkBoxes)
            {
                var id = kv.Key;
                var cb = kv.Value;
                RoutedEventHandler markTouched = (s, e) =>
                {
                    TweaksTouched.Add(id);
                    SyncTweakTextColor(id);
                    UpdateSelectedPanel();
                    RefreshTweaksStatus();
                    // Edge 优化：首次勾选开启时，提示组策略副作用（edge://management 会显示「由组织管理」，组策略固有表现，非故障）
                    if (cb.IsChecked == true && !_edgeOptimWarnShown)
                    {
                        var t = Tweaks.All.FirstOrDefault(x => x.Id == id);
                        if (t != null && t.Group == "Edge优化")
                        {
                            _edgeOptimWarnShown = true;
                            var res = System.Windows.MessageBox.Show(this,
                                "已开启 Edge 优化项。\n\n注意：这些优化通过写入 Edge 组策略（注册表 Policies\\Microsoft\\Edge）实现，开启后访问 edge://management/ 会显示「由你的组织管理」——这是组策略的固有表现，并非故障，重启 Microsoft Edge 后生效。\n\n是否继续开启？（本次会话不再提示）",
                                "Edge 优化提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                            if (res != System.Windows.MessageBoxResult.Yes)
                            {
                                cb.IsChecked = false;
                                return;
                            }
                        }
                    }
                };
                cb.Checked += markTouched;
                cb.Unchecked += markTouched;
                cb.Indeterminate += markTouched;
            }
            UpdateSelectedPanel();

            // 后台读取真实状态，分块刷新 UI，避免一次性大量更新阻塞渲染线程
            var states = new ConcurrentDictionary<string, bool?>();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                Parallel.ForEach(allTweaks, new ParallelOptions { MaxDegreeOfParallelism = 6 }, t =>
                {
                    bool? s = null;
                    try { s = t.IsThreeState ? ToCheckBox(t.GetState3()) : (bool?)(t.State()); }
                    catch { s = null; }
                    states[t.Id] = s;
                });

                // 分块派回 UI 线程，用 Background 优先级让渲染优先
                var ids = states.Keys.ToList();
                const int chunkSize = 25;
                for (int i = 0; i < ids.Count; i += chunkSize)
                {
                    var capturedChunk = ids.Skip(i).Take(chunkSize).ToList();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var id in capturedChunk)
                        {
                            if (TweaksTouched.Contains(id)) continue;   // 用户已手动改动，保留其选择
                            if (!states.TryGetValue(id, out var st)) continue;
                            if (checkBoxes.TryGetValue(id, out var chk))
                            {
                                chk.IsChecked = st;
                                SyncTweakTextColor(id);
                            }
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 状态读取完成后，建立「当前已优化」集合，用于右侧列表显示 (已优化)
                    TweaksOptimized = new HashSet<string>(
                        states.Where(kv => kv.Value == true).Select(kv => kv.Key),
                        StringComparer.Ordinal);
                    UpdateSelectedPanel();
                    int optCount = TweaksOptimized.Count;
                    if (optCount > 0)
                    {
                        SetTweaksOutput($"已检测到 {optCount} 项处于优化状态。如需备份当前方案，可点击「导出」保存优化配置。");
                        SetTweaksStatus($"已检测到 {optCount} 项处于优化状态，建议导出配置备份");
                    }
                    else
                    {
                        SetTweaksOutput("当前没有检测到已优化项目。可点击「基本优化」或「深度优化」选择方案，然后点「开始优化」应用。");
                        SetTweaksStatus("当前没有已优化项目");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            });

            treeScroll.Content = treePanel;
            Grid.SetRow(treeScroll, 1);
            leftGrid.Children.Add(treeScroll);
            leftPanel.Child = leftGrid;
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // 右侧：已选项目（绝对主体）+ 内容输出（极致紧凑，最小化占用）
            var rightPanel = new Border { Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch };
            // 右侧：Grid(Auto标签 + Star滚动区 + Auto输出行) —— 同左，Star 行自动填满
            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // 2: 输出行自适应，避免选中计数被裁掉
            var rLabel = new TextBlock { Text = "已选中的项目:", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(rLabel, 0);
            rightGrid.Children.Add(rLabel);
            // selectedPanel 已在左侧 tree 构建前创建并赋值给 UpdateSelectedPanel
            var selectedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = selectedPanel };
            Grid.SetRow(selectedScroll, 1);
            rightGrid.Children.Add(selectedScroll);

            // 内容输出：提示当前已选中的预设范围（随基本/深度/全选/重置变化）
            TweaksOutputLine = new TextBlock
            {
                Text = "",
                Foreground = _textDim,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                MaxHeight = 60,
                Opacity = 0.7
            };
            Grid.SetRow(TweaksOutputLine, 2);
            rightGrid.Children.Add(TweaksOutputLine);

            rightPanel.Child = rightGrid;
            Grid.SetColumn(rightPanel, 2);
            mainGrid.Children.Add(rightPanel);

            // 字段已在 tree 构建前赋值；右侧只是复用该面板
            // 内容输出已简化为静态文本，不再需要动态更新列表

            // mainGrid 不设 Dock（作为最后一个无 Dock 子元素，自动填满 DockPanel 剩余空间）
            root.Children.Add(mainGrid);

            // 稳健高度约束：绑定到 ContentArea.ActualHeight（只读 DP，自动跟随首帧+缩放，无 vp=0 时序 bug）
            BindRootHeightToViewport(root);

            return root;
        }

        // 字段引用，供 BuildTweaks 内部 lambda 使用（避免闭包问题）
        private StackPanel UpdateSelectedPanelRef;
        private Dictionary<string, System.Windows.Controls.CheckBox> TweaksCheckBoxes;
        private Dictionary<string, TextBlock> TweaksTextBlocks; // 优化项名称 TextBlock，用于同步选色
        private Action UpdateSelectedPanel;
        private TextBlock TweaksOutputLine;   // ApplyTweaks 进度日志出口（原静态 outputLine 提升为字段）
        private HashSet<string> TweaksTouched; // 记录用户在状态加载完成前手动改动过的项
        private HashSet<string> TweaksOptimized; // 当前系统实际已处于优化状态的项（用于右侧「已优化」标识）
        private bool _edgeOptimWarnShown; // Edge 优化组策略副作用提示：本会话仅提示一次

        private string _tweaksStatusBase = ""; // 窗口底部状态栏提示的“正文”，选中计数自动追加在后

        private void SetTweaksOutput(string baseText)
        {
            if (TweaksOutputLine == null) return;
            TweaksOutputLine.Text = baseText;
        }

        private void SetTweaksStatus(string baseText)
        {
            _tweaksStatusBase = baseText;
            RefreshTweaksStatus();
        }

        private void RefreshTweaksStatus()
        {
            int selected = TweaksCheckBoxes?.Values.Count(cb => cb.IsChecked == true) ?? 0;
            int total = TweaksCheckBoxes?.Count ?? 0;
            SetStatus($"{_tweaksStatusBase}（已选中 {selected} 项；共 {total} 项）");
        }

        private void RefreshSelectedList(Dictionary<string, System.Windows.Controls.CheckBox> boxes, StackPanel panel)
        {
            panel.Children.Clear();
            int count = 0;
            foreach (var kv in boxes)
            {
                if (kv.Value.IsChecked == true)
                {
                    var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                    if (t != null)
                    {
                        bool isOptimized = TweaksOptimized != null && TweaksOptimized.Contains(kv.Key);
                        panel.Children.Add(new TextBlock
                        {
                            Text = (isOptimized ? "(已优化) " : "  · ") + t.Name,
                            Foreground = _textMain,
                            FontSize = 13,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                        count++;
                    }
                }
            }
            if (count == 0)
                panel.Children.Add(new TextBlock { Text = "(尚无选中项)", Foreground = _textDim, FontSize = 12 });
        }

        /// <summary>同步单个优化项名称颜色：勾选(选中)=主题色，未勾选=白色/主文本色。</summary>
        private void SyncTweakTextColor(string id)
        {
            if (TweaksCheckBoxes != null && TweaksTextBlocks != null &&
                TweaksCheckBoxes.TryGetValue(id, out var cb) &&
                TweaksTextBlocks.TryGetValue(id, out var tb))
            {
                tb.Foreground = cb.IsChecked == true ? _accent : _textMain;
            }
        }

        private void SyncAllTweakColors()
        {
            if (TweaksCheckBoxes == null) return;
            foreach (var id in TweaksCheckBoxes.Keys) SyncTweakTextColor(id);
        }

        // 顶部预设按钮动作（Issue 2: 仅勾选/反选，不实际应用；仅"开始优化"按钮执行 ApplyByIds）
        private void BasicOptimize()
        {
            // 基本优化 = 所有低风险项（安全推荐）
            foreach (var kv in TweaksCheckBoxes)
            {
                var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                kv.Value.IsChecked = t != null && t.Risk == "low";
                TweaksTouched.Add(kv.Key);
                SyncTweakTextColor(kv.Key);
            }
            UpdateSelectedPanel();
            SetTweaksOutput("已选择基本优化项目（安全推荐）");
            SetTweaksStatus("已选择基本优化项目（安全推荐），点「开始优化」应用");
        }

        private void DeepOptimize()
        {
            // 深度优化 = 低风险(low) + 中风险(mid)，不含高风险(high)项。
            // 注意：参考项目 ZyperWin++ 的深度优化是「全部项剔除 24 个可选/外观项」(127/151)，
            // 并非全选。本项目的深度优化采用 Risk 分级作为真子集：low+mid 是明确的子集(100/104)，
            // 比「全选」更保守，符合「深度≠全选」的语义。最危险的高风险项仅由「全选」包含。
            foreach (var kv in TweaksCheckBoxes)
            {
                var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                kv.Value.IsChecked = t != null && t.Risk != "high";
                TweaksTouched.Add(kv.Key);
                SyncTweakTextColor(kv.Key);
            }
            UpdateSelectedPanel();
            SetTweaksOutput("已选择深度优化项目（低风险+中风险，请注意风险）");
            SetTweaksStatus("已选择深度优化项目（低风险+中风险，请注意风险），点「开始优化」应用");
        }

        private void SelectAll(bool sel)
        {
            // 全选 = 所有风险等级（含高风险）
            foreach (var kv in TweaksCheckBoxes)
            {
                kv.Value.IsChecked = sel;
                TweaksTouched.Add(kv.Key);
                SyncTweakTextColor(kv.Key);
            }
            UpdateSelectedPanel();
            SetTweaksOutput(sel ? "已全选所有优化项目" : "");
            SetTweaksStatus(sel ? "已全选所有优化项目，点「开始优化」应用" : "已取消全部选择");
        }

        // 底部"开始优化"按钮 = 按当前勾选状态应用【所有】项（WYSIWYG）：
        // 勾选=启用优化(On)，取消勾选=还原系统默认(Off)；三态项的不确定=交还系统默认(Default)。
        // 因此"取消勾选 + 开始优化"即可把该项恢复默认，无需动用"还原所有项"（避免误伤其它优化项）。
        private void ApplyChecked()
        {
            if (TweaksCheckBoxes == null || TweaksCheckBoxes.Count == 0) { System.Windows.MessageBox.Show(this, "当前没有可优化的项目", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); return; }
            var desired = new Dictionary<string, TweakState?>();
            foreach (var kv in TweaksCheckBoxes)
            {
                var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                if (t == null) continue;
                var cb = kv.Value;
                // 所有项都纳入：勾选框即为期望状态。二态项取消=Off(还原默认)，三态项按 On/Off/Default。
                if (t.IsThreeState)
                    desired[kv.Key] = cb.IsChecked == true ? TweakState.On : cb.IsChecked == false ? TweakState.Off : TweakState.Default;
                else
                    desired[kv.Key] = cb.IsChecked == true ? TweakState.On : TweakState.Off;
            }
            ApplyTweaks(desired, "开始优化（" + desired.Count + "项）");
        }

        // "还原所有项" = 二态项全部关闭，三态项全部交还系统默认
        private void RestoreAll()
        {
            var desired = new Dictionary<string, TweakState?>();
            foreach (var kv in TweaksCheckBoxes)
            {
                var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                if (t == null) continue;
                desired[kv.Key] = t.IsThreeState ? TweakState.Default : TweakState.Off;
            }
            ApplyTweaks(desired, "还原所有项");
        }

        private void OtherMenuPopup(Panel anchor)
        {
            var dlg = new OtherTweaksDialog(this) { Owner = this };
            dlg.ShowDialog();
        }

        private void ExportConfig()
        {
            try
            {
                string configFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
                System.IO.Directory.CreateDirectory(configFolder);
                var dlg = new SaveFileDialog
                {
                    Filter = "配置文件 (*.ini)|*.ini|JSON 旧版 (*.json)|*.json",
                    FileName = $"CpqSystemTool优化-{DateTime.Now:yyyyMMddHHmmss}.ini",
                    InitialDirectory = configFolder
                };
                if (dlg.ShowDialog() != true) return;

                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext == ".json")
                {
                    // 保留旧版 JSON 格式，便于兼容历史导出文件
                    var selected = TweaksCheckBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                    System.IO.File.WriteAllText(dlg.FileName, "[" + string.Join(",", selected.Select(s => "\"" + s + "\"")) + "]");
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("[CpqSystemTool优化配置]");
                    sb.AppendLine($"生成时间={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();
                    foreach (var kv in TweaksCheckBoxes)
                    {
                        string val = kv.Value.IsChecked == true ? "1"
                                   : kv.Value.IsChecked == false ? "0"
                                   : "2"; // 三态 Checkbox 的“系统默认”
                        sb.AppendLine($"{kv.Key}={val}");
                    }
                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                }

                System.Windows.MessageBox.Show(this, "已导出到：\n" + dlg.FileName, "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, "导出失败：" + ex.Message, "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        }

        private void ImportConfig()
        {
            try
            {
                string configFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
                System.IO.Directory.CreateDirectory(configFolder);
                var dlg = new OpenFileDialog
                {
                    Filter = "配置文件 (*.ini;*.json)|*.ini;*.json",
                    InitialDirectory = configFolder
                };
                if (dlg.ShowDialog() != true) return;

                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext == ".json")
                {
                    var json = System.IO.File.ReadAllText(dlg.FileName).Trim();
                    if (json.StartsWith("[") && json.EndsWith("]"))
                    {
                        var ids = json.Substring(1, json.Length - 2).Split(',').Select(s => s.Trim(' ', '"')).ToList();
                        foreach (var kv in TweaksCheckBoxes) kv.Value.IsChecked = ids.Contains(kv.Key);
                    }
                }
                else
                {
                    foreach (var raw in System.IO.File.ReadAllLines(dlg.FileName))
                    {
                        var line = raw.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("[") || line.StartsWith(";") || line.StartsWith("#")) continue;
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        string key = line.Substring(0, idx).Trim();
                        string val = line.Substring(idx + 1).Trim();
                        if (!TweaksCheckBoxes.TryGetValue(key, out var cb)) continue;
                        if (val == "1") cb.IsChecked = true;
                        else if (val == "0") cb.IsChecked = false;
                        else if (val == "2" && cb.IsThreeState) cb.IsChecked = null;
                    }
                }

                SyncAllTweakColors();
                UpdateSelectedPanel();
                System.Windows.MessageBox.Show(this, "已导入配置\n点「开始优化」应用", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, "导入失败：" + ex.Message, "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        }

        /// <summary>统一应用入口：按期望三态应用一组优化项。
        /// desired[id]=null 表示该项不在此次范围（跳过，不改变系统）；
        /// 三态项按 On/Off/Default 应用，二态项仅 On 时 Enable、Off 时 Disable。</summary>
        private void ApplyTweaks(Dictionary<string, TweakState?> desired, string label)
        {
            // 1) 同步勾选框（仅纳入范围且非"系统默认"的项；Default 留待刷新回写）
            foreach (var kv in desired)
                if (TweaksCheckBoxes != null && TweaksCheckBoxes.TryGetValue(kv.Key, out var cb) && kv.Value != null)
                    cb.IsChecked = kv.Value == TweakState.On;
            UpdateSelectedPanel();

            var pb = MakeProgress();
            pb.Visibility = Visibility.Visible;

            // 进度日志收集（后台线程写本地缓冲，避免跨线程写 UI）
            var sb = new System.Text.StringBuilder();
            object lk = new object();
            Action<string> bgLog = s => { lock (lk) sb.AppendLine(s); };

            // 2) 后台线程执行 + 不重建页面（避免闪烁）
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                int ok = 0, fail = 0;
                foreach (var kv in desired)
                {
                    var st = kv.Value;
                    if (st == null) continue; // 不在范围，跳过
                    var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                    if (t == null) { fail++; continue; }
                    try
                    {
                        if (t.IsThreeState) t.Apply3(st.Value, bgLog);
                        else if (st == TweakState.On) t.Enable(bgLog);
                        else t.Disable(bgLog);
                        ok++;
                    }
                    catch { fail++; }
                }
                Dispatcher.Invoke(() =>
                {
                    pb.Visibility = Visibility.Collapsed;
                    if (TweaksOptimized == null)
                        TweaksOptimized = new HashSet<string>(StringComparer.Ordinal);
                    // 3) 在原页面就地刷新每个项的实际状态（三态项反映 On/Off/系统默认），同步维护「已优化」集合
                    foreach (var kv in desired)
                    {
                        if (kv.Value == null) continue;
                        var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                        if (t == null) continue;
                        if (TweaksCheckBoxes != null && TweaksCheckBoxes.TryGetValue(kv.Key, out var cb))
                        {
                            bool? curState = t.IsThreeState ? ToCheckBox(t.GetState3()) : (bool?)(t.State());
                            cb.IsChecked = curState;
                            if (curState == true)
                                TweaksOptimized.Add(kv.Key);
                            else
                                TweaksOptimized.Remove(kv.Key);
                        }
                    }
                    SyncAllTweakColors();
                    UpdateSelectedPanel();
                    if (sb.Length > 0) TweaksOutputLine.Text = sb.ToString().Trim();
                    SetStatus($"{label}: {ok}项完成, {fail}项失败");
                });
            });
        }

        /// <summary>三态 → 勾选框：On=true / Off=false / Default=null(不确定)。</summary>
        private static bool? ToCheckBox(TweakState st) => st == TweakState.On ? true : st == TweakState.Off ? false : (bool?)null;

        // =====================================================================
        //  Module: 清理优化（参考 ZyperWin++ 分类勾选明细）
        // =====================================================================

        private class CleanupItemDef
        {
            public string Id, Name, Desc;
            public string Category;
            public bool DefaultChecked;
            public Action<Action<string>> Action;
        }

        private static readonly List<CleanupItemDef> CleanupCatalog = new List<CleanupItemDef>
        {
            // ---- 缓存文件 ----
            new CleanupItemDef { Id="thumb", Name="缩略图缓存", Desc="thumbcache_*.db", Category="缓存文件", DefaultChecked=true, Action=log=>CleanupExt.RunSelected(new[]{"thumb"},log) },
            new CleanupItemDef { Id="d3d", Name="D3D着色器缓存", Desc="DirectX 着色器缓存", Category="缓存文件", DefaultChecked=true, Action=log=>CleanupExt.RunSelected(new[]{"d3d"},log) },
            new CleanupItemDef { Id="term", Name="终端缓存", Desc="Windows Terminal 缓存", Category="缓存文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"term"},log) },
            new CleanupItemDef { Id="prefetch", Name="预读取文件", Desc="Prefetch 预读取", Category="缓存文件", DefaultChecked=true, Action=log=>CleanupExt.RunSelected(new[]{"prefetch"},log) },
            new CleanupItemDef { Id="edge_cache", Name="Edge/Chrome 缓存", Desc="浏览器缓存文件", Category="缓存文件", DefaultChecked=true, Action=log=>{ Cleanup.CleanPath("Edge 缓存",@"%LocalAppData%\Microsoft\Edge\User Data\Default\Cache",log); Cleanup.CleanPath("Chrome 缓存",@"%LocalAppData%\Google\Chrome\User Data\Default\Cache",log); }},
            new CleanupItemDef { Id="font_cache", Name="字体缓存", Desc="系统字体缓存（需重启字体服务）", Category="缓存文件", DefaultChecked=false, Action=log=>Cleanup.FontCache(log) },
            new CleanupItemDef { Id="icon_cache", Name="图标缓存", Desc="系统图标缓存", Category="缓存文件", DefaultChecked=false, Action=log=>Cleanup.IconCache(log) },
            new CleanupItemDef { Id="net_cache", Name=".NET程序集缓存", Desc="Native Image Cache (ngen)", Category="缓存文件", DefaultChecked=false, Action=log=>Cleanup.NetCache(log) },
            new CleanupItemDef { Id="tier1_usercache", Name="用户缓存·开发/包管理器", Desc="npm/pnpm/NuGet/pip/cargo 可重建缓存", Category="缓存文件", DefaultChecked=true, Action=log=>Cleanup.UserCacheTier1(log) },

            // ---- 系统文件 ----
            new CleanupItemDef { Id="sys_temp", Name="系统 Temp", Desc="%SystemRoot%\\Temp 临时文件", Category="系统文件", DefaultChecked=true, Action=log=>Cleanup.CleanDir("系统 Temp",@"%SystemRoot%\Temp",log) },
            new CleanupItemDef { Id="user_temp", Name="用户 Temp", Desc="%TEMP% 用户临时文件", Category="系统文件", DefaultChecked=true, Action=log=>Cleanup.CleanDir("用户 Temp",@"%TEMP%",log) },
            new CleanupItemDef { Id="wu_download", Name="Windows 更新缓存", Desc="SoftwareDistribution\\Download", Category="系统文件", DefaultChecked=true, Action=log=>Cleanup.CleanDir("Win更新缓存",@"%SystemRoot%\SoftwareDistribution\Download",log) },
            new CleanupItemDef { Id="winsxs_temp", Name="WinSxS 临时文件", Desc="WinSxS\\Temp", Category="系统文件", DefaultChecked=false, Action=log=>Cleanup.CleanDir("WinSxS Temp",@"%SystemRoot%\WinSxS\Temp",log) },
            new CleanupItemDef { Id="wer_reports", Name="WER 错误报告", Desc="Windows 错误报告", Category="系统文件", DefaultChecked=true, Action=log=>Cleanup.CleanDir("WER 错误报告",@"%ProgramData%\Microsoft\Windows\WER",log) },
            new CleanupItemDef { Id="diagnosis", Name="诊断数据", Desc="系统诊断数据", Category="系统文件", DefaultChecked=true, Action=log=>Cleanup.CleanDir("诊断数据",@"%ProgramData%\Microsoft\Diagnosis",log) },
            new CleanupItemDef { Id="winsxs_dism", Name="WinSxS 冗余(DISM)", Desc="DISM /ResetBase（耗时数分钟）", Category="系统文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"winsxs"},log) },

            // ---- 更新残留（第二档：基本安全，旧安装包/更新缓存） ----
            new CleanupItemDef { Id="tier2_updatepkgs", Name="更新残留·安装包缓存", Desc="ClickOnce/Win更新P2P/应用自动更新缓存（下次更新会重下）", Category="更新残留", DefaultChecked=false, Action=log=>Cleanup.UpdatePkgTier2(log) },

            // ---- 浏览器数据 ----
            new CleanupItemDef { Id="cookies", Name="浏览器 Cookies", Desc="会登出网站登录态（高风险）", Category="浏览器数据", DefaultChecked=false, Action=log=>Cleanup.Cookies(log) },
            new CleanupItemDef { Id="ie_cache", Name="IE/INetCache", Desc="IE 和系统网络缓存", Category="浏览器数据", DefaultChecked=false, Action=log=>Cleanup.CleanDir("IE/INetCache",@"%LocalAppData%\Microsoft\Windows\INetCache",log) },

            // ---- 日志 / 历史 ----
            new CleanupItemDef { Id="event_logs", Name="事件日志", Desc="全部 Windows 事件日志（系统会重建）", Category="日志/历史", DefaultChecked=false, Action=log=>Cleanup.EventLogs(log) },
            new CleanupItemDef { Id="recent_docs", Name="最近使用/跳转列表", Desc="运行对话框和文档历史", Category="日志/历史", DefaultChecked=true, Action=log=>Cleanup.Recent(log) },
            new CleanupItemDef { Id="wu_logs", Name="Windows Update 日志", Desc="更新相关日志", Category="日志/历史", DefaultChecked=false, Action=log=>Cleanup.WuLogs(log) },
            new CleanupItemDef { Id="cbs_log", Name="CBS 持久日志", Desc="组件存储日志", Category="日志/历史", DefaultChecked=false, Action=log=>Cleanup.CbsPersist(log) },
            new CleanupItemDef { Id="notifications", Name="通知数据库", Desc="Windows 通知缓存", Category="日志/历史", DefaultChecked=false, Action=log=>Cleanup.Notifications(log) },
            new CleanupItemDef { Id="crash_dumps", Name="用户崩溃转储", Desc="应用崩溃转储文件", Category="日志/历史", DefaultChecked=true, Action=log=>Cleanup.CrashDumps(log) },

            // ---- 高级 / 大空间回收 ----
            new CleanupItemDef { Id="recycle", Name="回收站", Desc="清空回收站", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.Recycle(log) },
            new CleanupItemDef { Id="nvidia", Name="NVIDIA 缓存", Desc="NVIDIA 驱动/DLSS/OTA 缓存", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.Nvidia(log) },
            new CleanupItemDef { Id="defender_log", Name="Defender 扫描记录", Desc="WD 扫描历史和支持文件", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.Defender(log) },
            new CleanupItemDef { Id="spotlight", Name="Spotlight 壁纸缓存", Desc="Windows Spotlight 缓存图片", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.Spotlight(log) },
            new CleanupItemDef { Id="activity", Name="活动历史", Desc="时间线活动历史", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.Activity(log) },
            new CleanupItemDef { Id="branch_cache", Name="BranchCache", Desc="分支缓存数据", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.BranchCache(log) },
            new CleanupItemDef { Id="hiberfil_off", Name="关闭休眠", Desc="删除 hiberfil.sys 释放大空间", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.BigSpaceHiberfilOff(log) },
            new CleanupItemDef { Id="memory_dmp", Name="删除内存转储", Desc="删除 %SystemRoot%\\MEMORY.DMP", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.BigSpaceMemoryDmp(log) },
            new CleanupItemDef { Id="windows_old", Name="清理 Windows.old", Desc="删除系统升级遗留备份", Category="高级/大空间", DefaultChecked=false, Action=log=>Cleanup.BigSpaceWindowsOld(log) },
        };

        private UIElement BuildCleanup()
        {
            // 双栏布局：左侧=清理项（撑满），右侧=操作按钮+日志（紧凑固定宽度）
            var root = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // 左：清理项
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });                    // 右：按钮+日志

            // ===== 左侧：清理项卡片（Grid 约束高度，确保 ScrollViewer 滚动） =====
            var leftGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Header
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Card 撑满剩余

            var leftSp = new StackPanel();
            leftSp.Children.Add(Header("清理优化", "按类别勾选要清理的项目，右侧执行操作并查看日志。"));

            var card = Card();
            var inner = new StackPanel();

            // 全选/反选快捷操作
            var selectBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var chkAll = new CheckBox { Content = "全选当前页", Foreground = _textDim, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            selectBar.Children.Add(chkAll);
            inner.Children.Add(selectBar);

            var pb = MakeProgress();
            var log = MakeLogBox();

            // 按类别分组展示
            var categories = CleanupCatalog.GroupBy(c => c.Category).OrderBy(g => g.Key);
            var allCheckBoxes = new List<CheckBox>();

            void UpdateCleanupSelCount()
            {
                int c = allCheckBoxes.Count(x => x.IsChecked == true);
                int total = allCheckBoxes.Count;
                SetStatus(c > 0 ? $"已选中 {c}/{total} 项" : "就绪");
            }

            foreach (var cat in categories)
            {
                var exHeader = new TextBlock { Text = cat.Key + " (" + cat.Count() + " 项)", Foreground = _accent, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
                var grpSp = new StackPanel { Margin = new Thickness(16, 4, 0, 8) };
                var groupExpander = MakeLineArrowExpander(exHeader, grpSp, true, new Thickness(0, 6, 0, 2));
                foreach (var item in cat)
                {
                    var chk = new CheckBox
                    {
                        Tag = item.Id,
                        IsChecked = item.DefaultChecked,
                        Margin = new Thickness(0),
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var nameTb = new TextBlock
                    {
                        Text = item.Name,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand,
                        TextWrapping = TextWrapping.Wrap
                    };
                    // 用绑定同步名称颜色：勾选=主题色，未勾选=正文色，避免事件链或模板覆盖导致失效
                    nameTb.SetBinding(TextBlock.ForegroundProperty, new Binding("IsChecked")
                    {
                        Source = chk,
                        Converter = new BoolToBrushConverter { TrueBrush = _accent, FalseBrush = _textMain }
                    });

                    var descTb = new TextBlock
                    {
                        Text = "  —  " + item.Desc,
                        Foreground = _textDim,
                        FontSize = 12.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var textSp = new WrapPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand };
                    textSp.Children.Add(nameTb);
                    textSp.Children.Add(descTb);

                    // 整行：CheckBox + 名称/描述文本并列，文本不在 CheckBox Content 内，避免默认模板覆盖 Foreground
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(chk, 0);
                    row.Children.Add(chk);
                    Grid.SetColumn(textSp, 1);
                    row.Children.Add(textSp);

                    // 点击名称/描述等价于勾选
                    nameTb.MouseLeftButtonUp += (s, e) => { chk.IsChecked = !chk.IsChecked; UpdateCleanupSelCount(); };
                    descTb.MouseLeftButtonUp += (s, e) => { chk.IsChecked = !chk.IsChecked; UpdateCleanupSelCount(); };

                    // 为每行套一个 Border，实现鼠标跟随背景填充
                    var rowBd = new Border
                    {
                        Background = Brushes.Transparent,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 3, 6, 3),
                        Margin = new Thickness(-6, 1, -6, 1),
                        Child = row
                    };
                    rowBd.MouseEnter += (s, e) => rowBd.Background = _rowHover;
                    rowBd.MouseLeave += (s, e) => rowBd.Background = Brushes.Transparent;
                    grpSp.Children.Add(rowBd);
                    allCheckBoxes.Add(chk);
                    chk.Checked += (s, e) => UpdateCleanupSelCount();
                    chk.Unchecked += (s, e) => UpdateCleanupSelCount();
                }
                groupExpander.Content = grpSp;
                inner.Children.Add(groupExpander);
            }

            // 全选/反选联动（名称颜色由绑定自动同步，此处只需更新计数）
            chkAll.Click += (s, e) =>
            {
                bool val = chkAll.IsChecked == true;
                foreach (var c in allCheckBoxes) c.IsChecked = val;
                UpdateCleanupSelCount();
            };

            // ★ 关键：inner（StackPanel）必须包在 ScrollViewer 里才能滚动！
            //   leftGrid 的 Star 行约束了 card 高度，但 StackPanel 本身不滚动
            //   没有 SV → 内容超长时直接截断，无法滚动查看
            card.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = inner };
            Grid.SetRow(leftSp, 0);
            leftGrid.Children.Add(leftSp);
            Grid.SetRow(card, 1);
            leftGrid.Children.Add(card);
            Grid.SetColumn(leftGrid, 0);
            root.Children.Add(leftGrid);

            // ===== 右侧：操作按钮 + 进度条 + 日志（垂直填满） =====
            var rightPanel = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var rightGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 0: 操作按钮 + 日志标题（同行）
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 1: 第三档扫描按钮
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 2: 进度条
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: 日志框（撑满剩余）
            int rRow = 0;

            // 日志标题 + 操作按钮（水平同行：左侧标题，右侧按钮）—— 日志在前
            var btnAndHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var logHeader = new TextBlock { Text = "📋 执行日志", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(logHeader, Dock.Left);
            btnAndHeader.Children.Add(logHeader);

            // 操作按钮（右侧，左对齐避免贴边）
            var actionBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            // 操作按钮（右侧）：三个按钮互斥高亮，点击谁就把 accent 填充转移到谁
            Button btnClean = null;
            Button btnScan = null;
            Button btnSelectAll = null;
            // 切换三个按钮的选中态：选中的用 accent 填充，未选中的恢复 secondary 样式
            void ApplyMode(Button sel)
            {
                foreach (var b in new[] { btnClean, btnScan, btnSelectAll })
                {
                    if (b == null) continue;
                    bool on = b == sel;
                    b.Background = on ? _accent : _btnSecondaryBg;
                    b.Foreground = on ? _btnPrimaryFg : _btnSecondaryFg;
                    b.BorderBrush = on ? Brushes.Transparent : _panelBorder;
                }
            }
            btnClean = Btn("🗑 开始清理", true, () =>
            {
                ApplyMode(btnClean);   // 切换到「开始清理」高亮
                var sel = allCheckBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
                if (sel.Count == 0) { log.AppendText("[!] 请先勾选要清理的项目\r\n"); return; }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l =>
                {
                    l("=== 开始清理 " + sel.Count + " 项 ===\r\n");
                    // 方案 B：按 Category 分组，跨类别并行、类别内仍串行 —— 全选多类别时整体加速。
                    // log 经 Dispatcher.BeginInvoke 线程安全，并行调用不会损坏 UI；并发度受 MaxDegreeOfParallelism 限流。
                    var catPar = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount)) };
                    var groups = CleanupCatalog.Where(d => sel.Contains(d.Id)).GroupBy(d => d.Category).ToList();
                    System.Threading.Tasks.Parallel.ForEach(groups, catPar, group =>
                    {
                        foreach (var def in group)
                        {
                            try { def.Action(l); } catch (Exception ex) { l("[!] " + def.Name + " 出错: " + ex.Message + "\r\n"); }
                        }
                    });
                    l("\r\n[OK] 清理完成！建议重启电脑以释放被占用的文件\r\n");
                }, "清理完成", () => pb.Visibility = Visibility.Collapsed);
            }, 100);
            actionBar.Children.Add(btnClean);

            btnScan = Btn("🔍 扫描大小", false, () =>
            {
                ApplyMode(btnScan);   // 切换到「扫描大小」高亮（填充转移）
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Cleanup.RunScan, "扫描完成", () => pb.Visibility = Visibility.Collapsed);
            }, 90);
            actionBar.Children.Add(btnScan);

            btnSelectAll = Btn("☑ 全选安全项", false, () =>
            {
                ApplyMode(btnSelectAll);   // 切换到「全选安全项」高亮
                foreach (var c in allCheckBoxes)
                {
                    var def = CleanupCatalog.FirstOrDefault(d => d.Id == (string)c.Tag);
                    c.IsChecked = (def != null && def.DefaultChecked);
                }
                UpdateCleanupSelCount();
            }, 100);
            actionBar.Children.Add(btnSelectAll);

            // 默认选中「开始清理」（与初始 primary 一致）
            ApplyMode(btnClean);
            // actionBar 已在上方定义，直接添加到 btnAndHeader
            btnAndHeader.Children.Add(actionBar);
            Grid.SetRow(btnAndHeader, rRow++);
            rightGrid.Children.Add(btnAndHeader);

            // ===== 第三档：扫描旧资产（先扫描 → 逐项目确认 → 删除） =====
            var btnTier3 = new Button
            {
                Content = new TextBlock
                {
                    Text = "🔍 第三档：多半可删，但需你确认（含旧资产/可能的数据）",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left,
                    FontSize = 12,
                    Foreground = _btnSecondaryFg
                },
                Background = _btnSecondaryBg,
                Foreground = _btnSecondaryFg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 7, 12, 7),
                MinHeight = 34,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            btnTier3.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                List<Cleanup.Tier3Candidate> found = null;
                RunInBg(log, l =>
                {
                    Cleanup.ScanTier3(l, out found);
                }, "第三档扫描完成", () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                    if (found == null || found.Count == 0)
                    {
                        log.AppendText("\r\n[i] 未发现明显可删的旧资产（或均已较新），无需确认。\r\n");
                        return;
                    }
                    var dlg = new Tier3ConfirmDialog(this, found);
                    if (dlg.ShowDialog() == true)
                    {
                        var toDel = dlg.Selected;
                        if (toDel.Count == 0) { log.AppendText("\r\n[i] 未勾选任何项，已取消删除。\r\n"); return; }
                        pb.Visibility = Visibility.Visible;
                        RunInBg(log, l2 => Cleanup.DeleteTier3(toDel, l2), "第三档删除完成", () => pb.Visibility = Visibility.Collapsed);
                    }
                });
            };
            var tier3Row = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8) };
            tier3Row.Children.Add(btnTier3);
            var tier3Hint = new TextBlock
            {
                Text = "仅显示 ≥200 MB 或已知停用工具的目录；扫描后须逐项勾选确认才会删除。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = _textMain,
                Opacity = 0.8,
                FontSize = 11,
                Margin = new Thickness(2, 4, 0, 0)
            };
            tier3Row.Children.Add(tier3Hint);
            Grid.SetRow(tier3Row, rRow++);
            rightGrid.Children.Add(tier3Row);

            // 进度条
            Grid.SetRow(pb, rRow++);
            rightGrid.Children.Add(pb);

            // 日志框直接占 Star 行，VerticalAlignment=Stretch 确保填满
            // 注意：TextBox 本身已启用 VerticalScrollBarVisibility=Auto，不能再包一层 ScrollViewer，
            // 否则两层滚动条会互相拦截滚轮/拖动事件，导致滚动控制失效。
            var logBorder = WrapLogBox(log);
            logBorder.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(logBorder, rRow++);
            rightGrid.Children.Add(logBorder);

            rightPanel.Child = rightGrid;
            Grid.SetColumn(rightPanel, 1);
            root.Children.Add(rightPanel);

            // 动态 MaxHeight：最大化时 root 跟随视口拉伸
            // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放，规避 vp=0 跳过）
            BindRootHeightToViewport(root);

            UpdateCleanupSelCount();
            return root;
        }

        // =====================================================================
        /// <summary>英文版名 → 中文友好显示（用于 UI 标签，DISM 输出翻译）</summary>
        private static string ChineseEditionName(string enName)
        {
            if (string.IsNullOrEmpty(enName)) return "(未知)";
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Professional",        "专业版" },
                { "Professional N",      "专业版 N" },
                { "ProfessionalWorkstation", "专业工作站版" },
                { "ProfessionalWorkstation N", "专业工作站版 N" },
                { "ProfessionalEducation",   "专业教育版" },
                { "ProfessionalEducation N", "专业教育版 N" },
                { "ProfessionalSingleLanguage", "专业单语言版" },
                { "ProfessionalCountrySpecific", "专业中文版" },
                { "Enterprise",          "企业版" },
                { "Enterprise N",        "企业版 N" },
                { "EnterpriseG",         "企业版 G" },
                { "EnterpriseGN",        "企业版 G N" },
                { "EnterpriseS",         "企业版 LTSC" },
                { "ServerRdsh",          "虚拟桌面版" },
                { "IoTEnterprise",       "IoT 企业版" },
                { "Education",           "教育版" },
                { "Education N",         "教育版 N" },
                { "Home",                "家庭版" },
                { "Home N",              "家庭版 N" },
                { "Home Single Language","家庭单语言版" },
                { "Home China",          "家庭中文版" },
                { "Core",                "核心版" },
                { "Core N",              "核心版 N" },
                { "CoreSingleLanguage",  "核心单语言版" },
            };
            if (map.TryGetValue(enName, out string cn)) return cn;
            // 支持 "Windows 10 Pro" / "Windows 11 Home" 等 ProductName 全文（剥离 "Windows 10/11 " 前缀）
            if (enName.StartsWith("Windows "))
            {
                string afterWin = enName.Substring("Windows ".Length).Trim();  // "10 Pro" / "11 Home"
                int sp = afterWin.IndexOf(' ');
                string shortName = sp >= 0 ? afterWin.Substring(sp + 1).Trim() : afterWin;  // "Pro" / "Home"
                if (map.TryGetValue(shortName, out cn)) return cn;
            }
            // 去掉 "Single Language" 后缀再试
            if (enName.EndsWith("Single Language") && map.TryGetValue(enName.Replace("Single Language", "单语言版"), out cn)) return cn;
            return enName;  // 找不到映射返回原文
        }

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
                        Dispatcher.Invoke(() =>
                        {
                            listBox.Items.Clear();
                            foreach (var r in list) listBox.Items.Add(r);
                        });
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
                    vsCurrentTb.Text = "当前版本: " + (osMaj.Length > 0 ? osMaj + " " : "") + ChineseEditionName(cur) + " (" + cur + ")";
                }

                var items = new List<ComboBoxItem>();
                foreach (var t in VersionSwitch.GetTargetEditions(null))
                    items.Add(new ComboBoxItem { Content = ChineseEditionName(t) + " (" + t + ")", Tag = t });
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
                string cnName = ChineseEditionName(edition);
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
            // 下拉项加大高度，避免文字被截断
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 4, 6)));
            itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            cb.ItemContainerStyle = itemStyle;
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

        // =====================================================================
        //  Module: 关于（独立实现声明 + 开源引用清单）
        // =====================================================================

        private UIElement BuildAbout()
        {
            var root = new StackPanel { Margin = new Thickness(0) };
            root.Children.Add(Header("关于", "软件信息、开源协议与免责声明。"));

            TextBlock SectionTitle(string text) => new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };

            void AttachCardHover(Border card)
            {
                card.MouseEnter += (s, e) => { if (card.Background == Brushes.Transparent) card.Background = _rowHover; };
                card.MouseLeave += (s, e) => { card.Background = Brushes.Transparent; };
            }

            // 1. 身份定位卡：图标 + 名称 + 版本 + 一句话简介（右上角风险提醒）
            var identity = Card();
            AttachCardHover(identity);
            var identityInner = (StackPanel)identity.Child;
            var identityRow = new Grid();
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconContainer = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            try
            {
                var icon = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/brush.png", UriKind.Absolute)),
                    Width = 48,
                    Height = 48,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                iconContainer.Child = icon;
            }
            catch (Exception caughtEx)
            {
                System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);
                iconContainer.Child = new TextBlock { Text = "🛠", FontSize = 40, VerticalAlignment = VerticalAlignment.Center };
            }
            Grid.SetColumn(iconContainer, 0);
            identityRow.Children.Add(iconContainer);

            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            nameRow.Children.Add(new TextBlock
            {
                Text = "系统清理与优化工具",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = _textMain
            });
            nameRow.Children.Add(new Border
            {
                Child = new TextBlock
                {
                    Text = APP_VERSION.ToUpperInvariant(),
                    FontSize = 11,
                    Foreground = _textMain,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(6, 2, 6, 2)
                },
                Background = _rowHover,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            namePanel.Children.Add(nameRow);
            namePanel.Children.Add(new TextBlock
            {
                Text = "面向 Windows 10/11 的一体化系统清理、优化与维护工具。",
                FontSize = 12.5,
                Foreground = _textDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(namePanel, 1);
            identityRow.Children.Add(namePanel);

            var riskText = new TextBlock
            {
                Text = "使用本工具产生的任何后果由使用者自行承担。",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = _warnOrange,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(riskText, 2);
            identityRow.Children.Add(riskText);

            identityInner.Children.Add(identityRow);
            root.Children.Add(identity);

            // 2. 功能简介卡
            var featureCard = Card(
                SectionTitle("📋 功能简介"),
                new TextBlock
                {
                    Text = "本工具是一款面向 Windows 10/11 的一体化系统清理、优化与维护工具，秉承「最小侵入、可一键还原」的理念，帮助用户在日常使用中快速释放空间、优化系统行为、管理常用软件与系统组件，并保留对关键操作的撤销能力。主要功能覆盖：系统优化、清理优化、服务优化、Appx 商店/管理、常用软件安装、安全防护、Edge 管理、隐私设置、系统工具、激活工具、系统信息采集与配置管理。",
                    FontSize = 12.5,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                }
            );
            AttachCardHover(featureCard);
            root.Children.Add(featureCard);

            // 3. 开发者与协议卡
            var devCard = Card();
            var devInner = (StackPanel)devCard.Child;
            devInner.Children.Add(SectionTitle("👤 开发者与协议"));
            var devGrid = new Grid();
            // 2 行 × 4 列：左标签、左值、右标签、右值；右侧放置项目主页/开源协议，与左侧开发者/抖音号同行，更紧凑。
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            devGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock DevLabel(string text, bool rightSide = false) => new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = _textDim,
                Margin = rightSide ? new Thickness(24, 0, 12, 0) : new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Row 0: 开发者 + 抖音主页 | 项目主页 + GitHub
            var devLabel = DevLabel("开发者：");
            Grid.SetRow(devLabel, 0); Grid.SetColumn(devLabel, 0);
            devGrid.Children.Add(devLabel);

            var douyinLink = LinkText("狸奴呦            ╲", "https://www.douyin.com/user/MS4wLjABAAAAK7pMpJ1pN-NvaDUQgDP8ytHUgzvRh61mM-M6TLwk5X0", 12.5);
            Grid.SetRow(douyinLink, 0); Grid.SetColumn(douyinLink, 1);
            devGrid.Children.Add(douyinLink);

            var homeLabel = DevLabel("项目主页：", rightSide: true);
            Grid.SetRow(homeLabel, 0); Grid.SetColumn(homeLabel, 2);
            devGrid.Children.Add(homeLabel);

            var homeValue = LinkText("System-Cleanup-Optimizer", "https://github.com/dandelion80231/System-Cleanup-Optimizer", 12.5);
            Grid.SetRow(homeValue, 0); Grid.SetColumn(homeValue, 3);
            devGrid.Children.Add(homeValue);

            // Row 1: 抖音号 + 可复制文本框 | 开源协议 + MIT
            var idLabel = DevLabel("抖音号：");
            idLabel.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(idLabel, 1); Grid.SetColumn(idLabel, 0);
            devGrid.Children.Add(idLabel);

            var douyinBox = new TextBox
            {
                Text = "1142736528",
                FontSize = 12.5,
                Foreground = _textMain,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "双击或按 Ctrl+C 复制抖音号"
            };
            // 获得焦点时自动全选，方便一键复制
            douyinBox.GotFocus += (s, e) => douyinBox.SelectAll();
            douyinBox.MouseDoubleClick += (s, e) =>
            {
                douyinBox.SelectAll();
                try { Clipboard.SetText(douyinBox.Text); SetStatus("抖音号已复制到剪贴板"); } catch { }
            };
            Grid.SetRow(douyinBox, 1); Grid.SetColumn(douyinBox, 1);
            devGrid.Children.Add(douyinBox);

            var licenseLabel = DevLabel("开源协议：", rightSide: true);
            licenseLabel.Margin = new Thickness(24, 4, 12, 0);
            Grid.SetRow(licenseLabel, 1); Grid.SetColumn(licenseLabel, 2);
            devGrid.Children.Add(licenseLabel);

            var licenseValue = new TextBlock
            {
                Text = "Apache License 2.0",
                FontSize = 12.5,
                Foreground = _textMain,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(licenseValue, 1); Grid.SetColumn(licenseValue, 3);
            devGrid.Children.Add(licenseValue);

            // Row 2: 邮箱 + 可复制文本框
            var emailLabel = DevLabel("邮箱：");
            emailLabel.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(emailLabel, 2); Grid.SetColumn(emailLabel, 0);
            devGrid.Children.Add(emailLabel);

            var emailBox = new TextBox
            {
                Text = "dandelion8023@365ms.cc",
                FontSize = 12.5,
                Foreground = _textMain,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "双击或按 Ctrl+C 复制邮箱"
            };
            // 获得焦点时自动全选，方便一键复制
            emailBox.GotFocus += (s, e) => emailBox.SelectAll();
            emailBox.MouseDoubleClick += (s, e) =>
            {
                emailBox.SelectAll();
                try { Clipboard.SetText(emailBox.Text); SetStatus("邮箱已复制到剪贴板"); } catch { }
            };
            Grid.SetRow(emailBox, 2); Grid.SetColumn(emailBox, 1);
            devGrid.Children.Add(emailBox);

            devInner.Children.Add(devGrid);
            AttachCardHover(devCard);
            root.Children.Add(devCard);

            // 更新日志卡（此处构建，实际添加到容器最底部）
            var updateCard = Card();
            var updateInner = (StackPanel)updateCard.Child;
            var updateHeaderRow = new Grid();
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            updateHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var updateTitle = SectionTitle("🔄 更新日志");
            updateTitle.Margin = new Thickness(0);
            Grid.SetColumn(updateTitle, 0);
            updateHeaderRow.Children.Add(updateTitle);
            var checkUpdateBtn = Btn("检查更新", true, () =>
            {
                CheckForUpdate();
            }, 90);
            checkUpdateBtn.FontSize = 11;
            checkUpdateBtn.Padding = new Thickness(10, 4, 10, 4);
            checkUpdateBtn.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(checkUpdateBtn, 1);
            updateHeaderRow.Children.Add(checkUpdateBtn);
            var downloadUpdateBtn = Btn("下载更新", true, () => DownloadUpdate(), 90);
            downloadUpdateBtn.FontSize = 11;
            downloadUpdateBtn.Padding = new Thickness(10, 4, 10, 4);
            downloadUpdateBtn.Margin = new Thickness(0);
            downloadUpdateBtn.Visibility = Visibility.Collapsed;
            Grid.SetColumn(downloadUpdateBtn, 2);
            updateHeaderRow.Children.Add(downloadUpdateBtn);
            _aboutDownloadUpdateBtn = downloadUpdateBtn;
            updateInner.Children.Add(updateHeaderRow);
            var changelogScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 280
            };
            var changelogText = new TextBlock
            {
                Text = LoadChangelogFromEmbeddedMarkdown(),
                FontSize = 12,
                Foreground = _textMain,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            changelogScroller.Content = changelogText;
            updateInner.Children.Add(changelogScroller);

            // 第三方开源组件 / OSS 声明卡：与免责声明并列，仅列运行时实际调用的第三方软件（MAS）。
            // 本工具自身代码为原创实现，未复制/打包其他项目代码；下列为唯一运行时调用的第三方开源软件。
            var ossCard = Card(
                SectionTitle("📦 第三方开源组件 / OSS 声明"),
                new TextBlock
                {
                    Text = "本工具自身代码为原创实现；下列为运行时实际调用的第三方开源软件（仅调用、未打包、未修改）。",
                    FontSize = 11.5,
                    LineHeight = 18,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                },
                OssRow(
                    "Microsoft Activation Scripts (MAS)",
                    "GNU GPL v3",
                    ("项目主页", "https://massgrave.dev"),
                    ("在线脚本", "https://get.activated.win")));
            AttachCardHover(ossCard);
            root.Children.Add(ossCard);

            // 免责声明卡：与上方卡片保持统一边框样式与标题/图标格式。
            var disclaimerCard = Card(
                SectionTitle("⚠ 免责声明"),
                new TextBlock
                {
                    Text = "本工具仅供学习、研究与个人使用。部分功能（服务禁用、防火墙、激活、注册表与隐私设置等）会改变系统默认行为，使用前请充分了解并建议创建系统还原点。\n" +
                           "激活功能会调用第三方脚本（MAS，详见上方「第三方开源组件 / OSS 声明」），本工具不对其内容或结果负责。",
                    FontSize = 11.5,
                    LineHeight = 18,
                    Foreground = _textMain,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 900,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
            AttachCardHover(disclaimerCard);
            root.Children.Add(disclaimerCard);

            // 更新日志卡置于最底部：日志条目增多时只向下延伸，不影响上方固定布局（身份/功能/开发者/免责声明）。
            AttachCardHover(updateCard);
            root.Children.Add(updateCard);

            return root;
        }

        /// <summary>从嵌入的 CHANGELOG.md 解析各版本更新日志，转为 About 页纯文本（去除 Markdown 标记）。
        /// CHANGELOG.md 为唯一事实来源：改一处即可，避免 About 文本与其不同步。</summary>
        private static string LoadChangelogFromEmbeddedMarkdown()
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("CHANGELOG.md"))
                {
                    if (stream == null) return "（更新日志资源缺失）";
                    string md;
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                        md = reader.ReadToEnd();

                    var sb = new StringBuilder();
                    bool inVersion = false;
                    foreach (var rawLine in md.Split('\n'))
                    {
                        var line = rawLine.TrimEnd('\r');
                        if (line.StartsWith("## ["))
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(
                                line, @"##\s*\[(v[0-9.]+)\]\s*-\s*([0-9-]+)");
                            sb.AppendLine(m.Success
                                ? $"{m.Groups[1].Value}（{m.Groups[2].Value}）"
                                : line.Substring(2).Trim());
                            inVersion = true;
                            continue;
                        }
                        if (line.StartsWith("### "))
                        {
                            if (inVersion) sb.AppendLine(line.Substring(4).Trim() + "：");
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            inVersion = false;
                            continue;
                        }
                        if (!inVersion) continue;
                        if (line.StartsWith(">")) continue;
                        if (line.StartsWith("- "))
                        {
                            sb.AppendLine("• " + line.Substring(2).Replace("**", "").Trim());
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        sb.AppendLine(line.Replace("**", "").Trim());
                    }
                    var result = sb.ToString().Trim();
                    return string.IsNullOrEmpty(result) ? "（更新日志为空）" : result;
                }
            }
            catch (Exception ex)
            {
                return "（更新日志读取失败：" + ex.Message + "）";
            }
        }

        // .NET Framework 的 WebClient 无 Timeout 属性（.NET 5+ 才有）；通过重写 GetWebRequest 设置底层请求超时。
        // Proxy 继承自基类 WebClient，外部可直接设置 wc.Proxy。
        private class WebClientWithTimeout : System.Net.WebClient
        {
            public int TimeoutMs { get; set; } = 10000;
            protected override System.Net.WebRequest GetWebRequest(Uri uri)
            {
                var w = base.GetWebRequest(uri);
                if (w != null) w.Timeout = TimeoutMs;
                return w;
            }
        }

        /// <summary>返回候选代理列表：系统代理 → 直连 → Watt Toolkit 本地 HTTP 代理。</summary>
        private static System.Net.IWebProxy[] GetProxyCandidates()
        {
            return new System.Net.IWebProxy[]
            {
                System.Net.WebRequest.DefaultWebProxy,              // 1) 系统代理（Watt Toolkit System 模式等）
                null,                                               // 2) 直连（无代理）
                new System.Net.WebProxy("http://127.0.0.1:26561", false) // 3) Watt Toolkit 本地端口（PAC/System 模式）
            };
        }

        /// <summary>依次尝试多种代理方式下载字符串，任一成功即返回；全部失败抛出汇总异常。</summary>
        private static string DownloadStringWithProxyFallback(string url)
        {
            System.Exception last = null;
            foreach (var proxy in GetProxyCandidates())
            {
                try
                {
                    using (var wc = new WebClientWithTimeout { TimeoutMs = 10000, Proxy = proxy })
                    {
                        wc.Headers.Add("User-Agent", "CpqSystemTool");
                        return wc.DownloadString(url);
                    }
                }
                catch (System.Exception ex) { last = ex; }
            }
            throw new System.Exception("所有网络方式均失败：" + (last?.Message ?? "未知错误"), last);
        }

        /// <summary>依次尝试多种代理方式下载文件，任一成功即返回；全部失败抛出汇总异常。</summary>
        private static async System.Threading.Tasks.Task DownloadFileWithProxyFallback(string url, string fileName, string tag, System.Windows.Threading.Dispatcher disp, System.Action<int> onProgress)
        {
            System.Exception last = null;
            foreach (var proxy in GetProxyCandidates())
            {
                try
                {
                    using (var wc = new WebClientWithTimeout { TimeoutMs = 120000, Proxy = proxy })
                    {
                        wc.Headers.Add("User-Agent", "CpqSystemTool");
                        wc.DownloadProgressChanged += (s, e) => onProgress?.Invoke(e.ProgressPercentage);
                        var tcs = new System.Threading.Tasks.TaskCompletionSource<object>();
                        wc.DownloadFileCompleted += (s, e) =>
                        {
                            if (e.Error != null) tcs.TrySetException(e.Error);
                            else if (e.Cancelled) tcs.TrySetCanceled();
                            else tcs.TrySetResult(null);
                        };
                        wc.DownloadFileAsync(new Uri(url), fileName);
                        await tcs.Task;
                        return;
                    }
                }
                catch (System.Exception ex) { last = ex; }
            }
            throw new System.Exception("所有网络方式均失败：" + (last?.Message ?? "未知错误"), last);
        }

        /// <summary>检查 GitHub Release 是否有新版本，结果经 Dispatcher 回到 UI 线程写入状态栏。</summary>
        private void CheckForUpdate()
        {
            SetStatus("正在检查更新…");
            var disp = Dispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var json = DownloadStringWithProxyFallback("https://api.github.com/repos/dandelion80231/System-Cleanup-Optimizer/releases/latest");
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!m.Success) { disp.Invoke(() => SetStatus("检查更新：未获取到版本信息")); return; }
                    var latest = m.Groups[1].Value.Trim();
                    // 从同一份 JSON 提取真实浏览器下载直链（含正确的资产文件名，可能是中文也可能是英文），避免自己拼文件名导致 404。
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                    _pendingUpdateUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                    var cmp = CompareVersion(APP_VERSION, latest);
                    if (cmp < 0)
                    {
                        _pendingUpdateTag = latest;
                        disp.Invoke(() =>
                        {
                            SetStatus("发现新版本 " + latest + "，可点击右侧「下载更新」保存到本地");
                            if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.Visibility = Visibility.Visible;
                        });
                    }
                    else if (cmp == 0)
                    {
                        _pendingUpdateTag = null;
                        disp.Invoke(() =>
                        {
                            SetStatus("当前已是最新版本 " + APP_VERSION);
                            if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.Visibility = Visibility.Collapsed;
                        });
                    }
                    else
                    {
                        _pendingUpdateTag = null;
                        disp.Invoke(() =>
                        {
                            SetStatus("当前版本 " + APP_VERSION + " 已高于线上 " + latest);
                            if (_aboutDownloadUpdateBtn != null) _aboutDownloadUpdateBtn.Visibility = Visibility.Collapsed;
                        });
                    }
                }
                catch (System.Net.WebException ex)
                {
                    disp.Invoke(() => SetStatus("检查更新失败：无法连接 GitHub（" + ex.Status + "）"));
                }
                catch (System.Exception ex)
                {
                    disp.Invoke(() => SetStatus("检查更新失败：" + ex.Message));
                }
            });
        }

        /// <summary>
        /// 语义化比较版本号；a&lt;b 返回负数，相等返回 0，a&gt;b 返回正数。
        /// 每段按整数比较，缺失段视作 0，无法解析的段也视作 0（左对齐零填充，即标准 semver 比较）。
        /// 兼容本项目早期把补丁号写在次版本位的简写：仅两段且第二段数值 ≤ 9 的写法 "vX.YY"（如 v1.03）
        /// 会被规范为 "vX.0.YY"（即 1.0.3）再比较，避免 "1.03" 被误判为高于 "1.0.4"。
        /// 规范做法：所有版本号统一使用两段式 vX.YY（见 RELEASE_CHECKLIST；历史版本 v1.01~v1.05 均为两段式，v1.0.4 是唯一异类）。
        /// </summary>
        private static int CompareVersion(string a, string b)
        {
            var pa = NormalizeVersion(a);
            var pb = NormalizeVersion(b);
            int len = System.Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int na = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                int nb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }

        /// <summary>
        /// 去掉 v/V 前缀并按 '.' 拆成数字段。兼容早期两段简写：当只有两段且第二段数值 ≤ 9 时
        /// （如 "1.03"），在中间补 0 规范为三段（"1.0.3"），以匹配本项目 1.0.x 的版本习惯；
        /// 第二段 &gt; 9（如 "1.10"）则保持原样（视为 1.10.0，避免误判）。
        /// </summary>
        private static string[] NormalizeVersion(string v)
        {
            var parts = v.TrimStart('v', 'V').Split('.');
            if (parts.Length == 2 && int.TryParse(parts[1], out int second) && second <= 9)
                return new[] { parts[0], "0", parts[1] };
            return parts;
        }

        /// <summary>用户点击「下载更新」后：弹出 SaveFileDialog 自选保存路径，然后从 GitHub Release 下载对应版本 exe。</summary>
        private async void DownloadUpdate()
        {
            if (string.IsNullOrEmpty(_pendingUpdateTag))
            {
                SetStatus("没有检测到可用的新版本，请先点击「检查更新」");
                return;
            }
            var tag = _pendingUpdateTag;
            var fileName = $"系统清理与优化工具_{tag}.exe";
            var dlg = new SaveFileDialog
            {
                FileName = fileName,
                DefaultExt = ".exe",
                Filter = "可执行文件 (*.exe)|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) dlg.InitialDirectory = downloads;

            if (dlg.ShowDialog() != true) return;

            // 优先使用从 GitHub API 取得的真实浏览器下载直链（文件名正确，避免 404）；仅在缺失时回退到本地拼装。
            var url = _pendingUpdateUrl;
            if (string.IsNullOrEmpty(url)) url = $"https://github.com/dandelion80231/System-Cleanup-Optimizer/releases/download/{tag}/{fileName}";
            SetStatus($"正在下载 {tag} …");
            var disp = Dispatcher;
            try
            {
                await DownloadFileWithProxyFallback(url, dlg.FileName, tag, disp, pct =>
                {
                    disp.Invoke(() => SetStatus($"正在下载 {tag}：{pct}%"));
                });
                SetStatus($"新版本已保存：{dlg.FileName}");
                if (MessageBox.Show("下载完成，是否打开所在文件夹？", "下载完成", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\""); } catch { }
                }
            }
            catch (System.Exception ex)
            {
                SetStatus($"下载失败：{ex.Message}");
            }
        }

        /// <summary>开源引用清单的一行：名称 + 许可证标签 + 一个或多个可点击来源链接。</summary>
        private UIElement OssRow(string name, string license, params (string text, string url)[] links)
        {
            var row = new Grid { Margin = new Thickness(0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = _textMain, TextWrapping = TextWrapping.Wrap });
            left.Children.Add(new TextBlock { Text = "许可：" + license, FontSize = 11, Foreground = _textDim, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(left, 0);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var (text, url) in links)
            {
                var link = LinkText(text, url, 11.5);
                link.HorizontalAlignment = HorizontalAlignment.Right;
                right.Children.Add(link);
            }
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            return row;
        }

        // =====================================================================
        //  Module: 服务优化（保持不变）
        // =====================================================================

        private UIElement BuildServices()
        {
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
                    Dispatcher.Invoke(() =>
                    {
                        foreach (var s in ServiceOptimizer.All)
                        {
                            if (!btnByService.TryGetValue(s.Name, out var btn)) continue;
                            bool dis = states.TryGetValue(s.Name, out var d) && d;
                            var wasDis = dis;
                            btn.Content = dis ? "恢复" : "禁用";
                            btn.IsEnabled = true;
                            btn.Click += (sender, e) =>
                            {
                                pb.Visibility = Visibility.Visible;
                                RunInBg(log, l2 => ServiceOptimizer.Apply(s, !wasDis, l2),
                                    wasDis ? "已恢复: " + s.Display : "已禁用: " + s.Display,
                                    () => { pb.Visibility = Visibility.Collapsed; wasDis = !wasDis; btn.Content = wasDis ? "恢复" : "禁用"; });
                            };
                        }
                        pb.Visibility = Visibility.Collapsed;
                    });
                }, "服务状态已加载");
            });

            return root;
        }

        // =====================================================================
        //  Module: Appx 管理（两行三列卡片布局，含当前用户/系统预装切换）
        // =====================================================================

        private UIElement BuildAppx()
        {
            // 用 Grid 让 cardsCard 撑满剩余空间，cardsScroll 内部独立滚动（不动搜索框/计数器）
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 工具栏
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // optionsRow
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // cardsCard（撑满）
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // pb
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // log
            int rootRow = 0;

            var headerTb = Header("Appx 商店", "列出微软商店可用的 61 个 App（含已安装+未安装），5 列紧凑布局。含搜索 + 功能选项 + 本页操作。");
            Grid.SetRow(headerTb, rootRow++);
            root.Children.Add(headerTb);

            // 公共组件
            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 80;
            var logBorder = WrapLogBox(log);
            logBorder.Visibility = Visibility.Collapsed;  // 默认隐藏，进入页面时不显示

            // ===== 预先创建所有控件（按钮的 lambda 闭包需要引用） =====
            var (searchBoxWrap, searchBox) = MakeSearchBox(12.5, "过滤 Catalog（60 个精选应用，按名称/ID 匹配）\n\n想搜任意应用（Chrome / Python / QQ 等）？请点右侧「🔍 搜索应用」按钮");
            var countLbl = new TextBlock { Foreground = _textDim, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var cardsPanel = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 5,  // 一行 5 列
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var rbCurrent = new System.Windows.Controls.RadioButton
            {
                Content = "当前用户应用管理",
                Foreground = _textMain,
                FontSize = 13,
                IsChecked = true,
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand
            };
            var rbProvisioned = new System.Windows.Controls.RadioButton
            {
                Content = "系统预装应用卸载",
                Foreground = _textMain,
                FontSize = 13,
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand
            };
            var chkConfirm = new System.Windows.Controls.CheckBox
            {
                Content = "卸载前询问",
                Foreground = _textMain,
                FontSize = 13,
                IsChecked = true,
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand
            };
            var chkWithProvisioned = new System.Windows.Controls.CheckBox
            {
                Content = "同时卸载预装应用",
                Foreground = _textMain,
                FontSize = 13,
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand
            };

            // ===== 工具栏行：搜索框（占满空余）+ 计数器 + 操作按钮 =====
            var toolBarRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            toolBarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 搜索框占满
            toolBarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                    // 计数器
            toolBarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                    // 操作按钮
            Grid.SetColumn(searchBoxWrap, 0);
            toolBarRow.Children.Add(searchBoxWrap);
            Grid.SetColumn(countLbl, 1);
            toolBarRow.Children.Add(countLbl);

            var toolBar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            // ★ 搜索应用按钮已迁移到「常用软件」页（列表式更和谐），Search 类复用 StoreSearchWindow
            var btnRefresh = Btn("🔄 刷新", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                LoadAndRender(rbCurrent.IsChecked == true, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount);
                pb.Visibility = Visibility.Collapsed;
            }, 90);
            toolBar.Children.Add(btnRefresh);
            var allSelected = false;
            // 选中计数更新（写入 StatusBar）—— 复用通用 UpdateSelStatus
            void UpdateAppxSelCount()
            {
                int c = 0, total = 0;
                foreach (var b in cardsPanel.Children.OfType<Border>())
                {
                    if (b.Tag is System.Tuple<string, System.Windows.Controls.CheckBox> t) { total++; if (t.Item2.IsChecked == true) c++; }
                }
                UpdateSelStatus(c, total, "个应用");
            }
            var btnToggleSel = Btn("□ 全选", false, null, 90);
            btnToggleSel.Click += (s, e) =>
            {
                allSelected = !allSelected;
                // 从 Border.Tag 直接取 CheckBox 引用（之前用 b.Child as Grid 是错的，Card.Child 是 StackPanel）
                foreach (var b in cardsPanel.Children.OfType<Border>())
                {
                    if (b.Tag is System.Tuple<string, System.Windows.Controls.CheckBox> t)
                        t.Item2.IsChecked = allSelected;
                }
                // emoji 和实际状态对应：未选 □ / 已选 ☑
                btnToggleSel.Content = allSelected ? "☑ 取消全选" : "□ 全选";
                UpdateAppxSelCount();
            };
            toolBar.Children.Add(btnToggleSel);
            var btnUninstall = Btn("🗑 卸载选中", true, () =>
            {
                // 从 Border.Tag 直接取 CheckBox 引用（更高效，不再靠视觉树查找）
                var selected = cardsPanel.Children.OfType<Border>()
                    .Select(b => b.Tag as System.Tuple<string, System.Windows.Controls.CheckBox>)
                    .Where(t => t != null && t.Item2.IsChecked == true)
                    .Select(t => t.Item1)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                if (selected.Count == 0) { log.AppendText("[!] 请先勾选要卸载的应用\r\n"); return; }
                bool ask = chkConfirm.IsChecked == true;
                bool alsoProvisioned = chkWithProvisioned.IsChecked == true;
                // ★ 防双击：已有任务在跑就拒绝新点击（解决"卸载两次" + 闪退）
                if (pb.Visibility == Visibility.Visible) return;
                if (ask)
                {
                    var preview = "即将卸载 " + selected.Count + " 个应用：\n  " + string.Join("\n  ", selected.Take(5).Select(s => AppxManager.Catalog.Find(c => c.PackageFamily == s)?.Label ?? s)) + (selected.Count > 5 ? "\n  ..." : "");
                    // ★ 直接同步弹 MessageBox（UI 线程），不要再 Dispatcher.Invoke 嵌套
                    var ans = System.Windows.MessageBox.Show(preview, "确认卸载", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                    if (ans != System.Windows.MessageBoxResult.Yes) return;
                }
                pb.Visibility = Visibility.Visible;
                // ★ 单层 RunInBg：直接跑 Uninstall，不再 ThreadPool.QueueUserWorkItem 嵌套
                RunInBg(log, l =>
                {
                    AppxManager.Uninstall(selected, l);
                    if (alsoProvisioned)
                    {
                        foreach (var pn in selected)
                        {
                            var fam = AppxManager.Catalog.Find(c => c.PackageFamily == pn);
                            if (fam != null) AppxManager.UninstallProvisioned(new List<string> { fam.PackageFamily }, s => l(s));
                        }
                    }
                }, "卸载完成", () => { pb.Visibility = Visibility.Collapsed; LoadAndRender(rbCurrent.IsChecked == true, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount); });
            }, 110);
            toolBar.Children.Add(btnUninstall);
            Grid.SetColumn(toolBar, 2);
            toolBarRow.Children.Add(toolBar);
            Grid.SetRow(toolBarRow, rootRow++);
            root.Children.Add(toolBarRow);

            // ===== 选项 + 本页操作：WrapPanel 自然换行（避免右侧溢出） =====
            var optionsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            optionsRow.Children.Add(new TextBlock { Text = "应用范围:", FontSize = 13, Foreground = _accent, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            optionsRow.Children.Add(rbCurrent);
            optionsRow.Children.Add(rbProvisioned);
            optionsRow.Children.Add(new Border { Width = 1, Background = _panelBorder, Margin = new Thickness(12, 2, 12, 0) });
            optionsRow.Children.Add(new TextBlock { Text = "本页操作:", FontSize = 13, Foreground = _accent, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            optionsRow.Children.Add(chkConfirm);
            optionsRow.Children.Add(chkWithProvisioned);
            var installWingetLink = new TextBlock
            {
                Text = "安装新版 WinGet",
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA0, 0xFF)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline
            };
            installWingetLink.MouseLeftButtonUp += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://github.com/microsoft/winget-cli/releases", UseShellExecute = true });
            };
            optionsRow.Children.Add(installWingetLink);
            Grid.SetRow(optionsRow, rootRow++);
            root.Children.Add(optionsRow);

            // ===== 应用网格 Card（5 列） =====
            var cardsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(0, 0, 12, 0) };
            var cardsCard = new Border
            {
                Background = _bgCard,  // Transparent
                CornerRadius = new CornerRadius(12),
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,  // 撑满 Grid Row 4 的 Height=* 空间
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.30,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                },
                Child = cardsScroll
                // 不设 MinHeight/MaxHeight，让 cardsScroll 自然撑满 cardsCard 内部
            };
            cardsScroll.Content = cardsPanel;
            Grid.SetRow(cardsCard, rootRow++);
            root.Children.Add(cardsCard);

            Grid.SetRow(pb, rootRow++);
            root.Children.Add(pb);
            Grid.SetRow(logBorder, rootRow++);
            root.Children.Add(logBorder);

            // 模式切换刷新
            rbCurrent.Click += (s, e) => { if (rbCurrent.IsChecked == true) LoadAndRender(true, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount); };
            rbProvisioned.Click += (s, e) => { if (rbProvisioned.IsChecked == true) LoadAndRender(false, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount); };
            searchBox.TextChanged += (s, e) => LoadAndRender(rbCurrent.IsChecked == true, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount);

            // 打开时默认加载（静默加载，不显示进度条，避免切换页面时感觉慢）
            AutoLoad(() =>
            {
                LoadAndRender(true, cardsPanel, countLbl, "", log, UpdateAppxSelCount);
            });

            // 高度约束：BindRootHeightToViewport 把 root.MaxHeight 绑定到 ContentArea.ActualHeight
            // （只读 DP，尺寸变化时自动通知）→ Star 行撑满剩余空间、外层永不滚动；
            // 首次布局与窗口缩放均自动跟随，无 vp=0 时序 bug。内容超出时由内层 cardsScroll 内部滚动。
            BindRootHeightToViewport(root);
            return root;
        }

        // Issue 4 + 24: 加载并渲染 Appx 列表（4 列 UniformGrid，紧凑布局 + 搜索过滤）
        private void LoadAndRender(bool currentUser, System.Windows.Controls.Primitives.UniformGrid cardsPanel, TextBlock countLbl, string keyword, TextBox log, Action onCheckChanged = null)
        {
            log.Clear();  // 每次刷新清空日志，避免切换 RadioButton/搜索时累积旧内容
            cardsPanel.Children.Clear();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string q = (keyword ?? "").Trim().ToLowerInvariant();
                    string qClean = string.IsNullOrEmpty(q) ? "" : new string(q.Where(c => char.IsLetterOrDigit(c)).ToArray());
                    Func<string, bool> matchFn = name =>
                    {
                        if (string.IsNullOrEmpty(q)) return true;
                        string n = name.ToLowerInvariant();
                        string nc = new string(n.Where(c => char.IsLetterOrDigit(c)).ToArray());
                        return n.Contains(q) || nc.Contains(qClean);
                    };
                    if (currentUser)
                    {
                        // 不在日志输出调试信息（_=>{} 吃掉后端日志），只更新 countLbl + 填充卡片
                        var items = AppxManager.ListCatalogWithStatus(_ => {});
                        Dispatcher.Invoke(() =>
                        {
                            int showCount = 0;
                            foreach (var it in items)
                            {
                                if (!matchFn(it.Name)) continue;
                                bool installed = !string.IsNullOrEmpty(it.FullName);
                                cardsPanel.Children.Add(BuildAppxCard(it.Name, it.FullName, it.PackageName, log, false, installed, onCheckChanged));
                                showCount++;
                            }
                            int installedCount = items.Count(x => !string.IsNullOrEmpty(x.FullName));
                            countLbl.Text = $"[OK] 共 {items.Count} 个应用\n（{installedCount} 已安装）";
                            if (!string.IsNullOrEmpty(q))
                            {
                                countLbl.Text += $"\n · 筛选: {showCount}";
                                if (showCount == 0)
                                {
                                    countLbl.Foreground = _warnOrange;
                                    countLbl.Text += $"\n · Catalog 无匹配。点「🔍 搜索应用」搜全网（QQ / Chrome / Python 等）";
                                }
                                else countLbl.Foreground = _textDim;
                            }
                        });
                    }
                    else
                    {
                        var items = AppxManager.ListProvisioned(_ => {});
                        Dispatcher.Invoke(() =>
                        {
                            int showCount = 0;
                            foreach (var it in items)
                            {
                                if (!matchFn(it.Name)) continue;
                                bool installed = !string.IsNullOrEmpty(it.PackageName);
                                cardsPanel.Children.Add(BuildAppxCard(it.Name, it.PackageName, it.PackageName, log, true, installed, onCheckChanged));
                                showCount++;
                            }
                            countLbl.Text = $"[OK] 共 {items.Count} 个系统预装应用";
                            if (!string.IsNullOrEmpty(q)) countLbl.Text += $" · 筛选: {showCount}";
                        });
                    }
                }
                catch (Exception ex) { Dispatcher.Invoke(() => log.AppendText("[!] " + ex.Message + "\r\n")); }
            });
        }

        // Issue 27+33: 单个 Appx 卡片 - 已安装用浅绿背景填充，未安装用浅红背景；色号统一为字段
        private Border BuildAppxCard(string displayName, string packageId, string uninstallTarget, TextBox log, bool isProvisioned = false, bool isInstalled = true, Action onCheckChanged = null)
        {
            var cardBg = isInstalled ? _installedBg : _notInstalledBg;
            var cardBorder = isInstalled ? _installedBorder : _notInstalledBorder;
            var cardFg = isInstalled ? _installedFg : _textMain;
            var card = new Border
            {
                Background = cardBg,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 10, 8, 10),  // 上下 padding 加大，让卡片本身更高
                Margin = new Thickness(0, 0, 10, 6)   // 右 margin 加大到最后一张卡片右边留 10px（Tag 在下方统一用 Tuple 重写）
            };
            var sp = new StackPanel();

            // 第一行：checkbox + name（紧凑同行）
            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var chk = new System.Windows.Controls.CheckBox
            {
                Foreground = _textMain,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            DockPanel.SetDock(chk, Dock.Left);
            // 把 CheckBox 引用也存到 Border.Tag 里（用 Tuple），让全选按钮能直接拿到
            card.Tag = new System.Tuple<string, System.Windows.Controls.CheckBox>(uninstallTarget, chk);
            // 选中状态变化时回调（用于 StatusBar 计数）
            if (onCheckChanged != null) { chk.Checked += (s, e) => onCheckChanged(); chk.Unchecked += (s, e) => onCheckChanged(); }
            headerRow.Children.Add(chk);

            var nameTb = new TextBlock
            {
                Text = displayName,
                Foreground = cardFg,    // 已安装用 _installedFg（深绿/浅绿），未安装用 _textMain，对比度更高
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(nameTb);
            sp.Children.Add(headerRow);

            // 第二行：PackageFamily
            var pkgTb = new TextBlock
            {
                Text = ((uninstallTarget?.Length ?? 0) > 28 ? uninstallTarget.Substring(0, 28) + "…" : (uninstallTarget ?? "")),
                Foreground = _textDim,
                FontSize = 9.5,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            sp.Children.Add(pkgTb);

            // 第三行：按钮
            var btnBar = new WrapPanel { Orientation = Orientation.Horizontal };
            btnBar.Children.Add(Btn("详情", false, () =>
            {
                var def = AppxManager.Catalog.Find(c => c.Label == displayName || c.PackageFamily == uninstallTarget);
                string url = def != null ? "https://apps.microsoft.com/detail/" + def.StoreId : "https://apps.microsoft.com/";
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { SetStatus("打开链接失败: " + ex.Message); }
            }, 50));

            // 安装/卸载完成后【原地刷新本卡片】：重新查询状态并替换该卡片，保留页面与日志，不重建整页（避免日志丢失）
            Action refreshThisCard = () =>
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    bool nowInstalled = false;
                    try
                    {
                        var items = AppxManager.ListCatalogWithStatus(_ => { });
                        var it = items.FirstOrDefault(x => x.Name == displayName || x.PackageName == uninstallTarget);
                        nowInstalled = it != null && !string.IsNullOrEmpty(it.FullName);
                    }
                    catch { }
                    Dispatcher.Invoke(() =>
                    {
                        var parent = (Panel)card.Parent;
                        if (parent != null)
                        {
                            int idx = parent.Children.IndexOf(card);
                            if (idx >= 0)
                            {
                                var fresh = BuildAppxCard(displayName, nowInstalled ? uninstallTarget : "", uninstallTarget, log, isProvisioned, nowInstalled, onCheckChanged);
                                parent.Children[idx] = fresh;
                                return;
                            }
                        }
                        SetPageContent(BuildAppx()); // 兜底：找不到容器时重建整页
                    });
                });
            };

            var uninstallBtn = Btn(isInstalled ? "卸载" : "安装", !isInstalled, () =>
            {
                if (!isInstalled)
                {
                    var def = AppxManager.Catalog.Find(c => c.Label == displayName || c.PackageFamily == uninstallTarget);
                    if (def != null)
                        RunInBg(log, l => AppxManager.Install(def.StoreId, l), "安装完成: " + displayName, refreshThisCard);
                    return;
                }
                RunInBg(log, l =>
                {
                    if (isProvisioned) AppxManager.UninstallProvisioned(new System.Collections.Generic.List<string> { uninstallTarget }, l);
                    else AppxManager.Uninstall(new System.Collections.Generic.List<string> { uninstallTarget }, l);
                }, "卸载完成: " + displayName, refreshThisCard);
            }, 50);
            if (!isInstalled) uninstallBtn.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
            btnBar.Children.Add(uninstallBtn);
            sp.Children.Add(btnBar);

            card.Child = sp;
            return card;
        }

        // =====================================================================
        //  Module: Appx 管理（ZyperWin 风格：原始包名列表 + 勾选批量卸载）
        // =====================================================================

        private UIElement BuildAppxRaw()
        {
            // Grid 布局：header / 工具栏 / 列表卡（撑满）/ 进度条 / 日志
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 工具栏
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 列表卡（撑满）
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // pb
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // log
            int rootRow = 0;

            var appxRawHeader = Header("Appx 管理", "列出系统中所有原始 AppX 包（含系统组件），勾选后批量卸载。");
            Grid.SetRow(appxRawHeader, rootRow++);
            root.Children.Add(appxRawHeader);

            // ===== 列表 Card（撑满 Grid 第 3 行） =====
            var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 12, 0) };
            var listStack = new StackPanel { Margin = new Thickness(2) };
            listScroll.Content = listStack;
            var pb = MakeProgress();
            var log = MakeLogBox();
            var logBorder = WrapLogBox(log);
            logBorder.Visibility = Visibility.Collapsed;  // 默认隐藏

            var countLbl = new TextBlock { Foreground = _textDim, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };

            // 工具栏：4 列均分（刷新 / 计数 / 全选 / 卸载选中），列定义在按钮创建后统一设置
            var toolBar = new Grid { Margin = new Thickness(0, 8, 0, 10) };

            // 存储所有行的 CheckBox 和 Border 引用（用于"全选"和批量卸载）
            var rowItems = new List<Tuple<System.Windows.Controls.CheckBox, Border, AppxInfo>>();

            void RefreshList(bool showProgress = true)
            {
                if (showProgress) pb.Visibility = Visibility.Visible;
                RunInBg(log, l =>
                {
                    var items = AppxManager.ListInstalled(l);
                    Dispatcher.Invoke(() =>
                    {
                        listStack.Children.Clear();
                        rowItems.Clear();
                        foreach (var it in items)
                        {
                            var rowBorder = new Border
                            {
                                Background = Brushes.Transparent,
                                BorderBrush = _panelBorder,
                                BorderThickness = new Thickness(0, 0, 0, 1),
                                Padding = new Thickness(8, 6, 8, 6),
                                Tag = it.FullName
                            };
                            var rowGrid = new Grid();
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 勾选框
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 名称
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 卸载按钮

                            // 勾选框
                            var chk = new System.Windows.Controls.CheckBox
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 8, 0),
                                Foreground = _textMain
                            };
                            Grid.SetColumn(chk, 0);
                            // 选中状态变化时更新 StatusBar 计数
                            chk.Checked += (s, e) => UpdateRawSelCount();
                            chk.Unchecked += (s, e) => UpdateRawSelCount();
                            rowGrid.Children.Add(chk);

                            // 名称（优先显示友好名，不显示冗长 FullName）
                            var nameText = it.Name;
                            var nameTb = new TextBlock
                            {
                                Text = nameText,
                                Foreground = _textMain,
                                FontSize = 13,
                                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextWrapping = TextWrapping.NoWrap,
                                Margin = new Thickness(0, 0, 8, 0)
                            };
                            Grid.SetColumn(nameTb, 1);
                            rowGrid.Children.Add(nameTb);

                            // 单行卸载按钮
                            var uninstBtn = Btn("卸载", false, () =>
                            {
                                if (string.IsNullOrEmpty(it.FullName)) { log.AppendText("[!] 未安装的包无法卸载\r\n"); return; }
                                pb.Visibility = Visibility.Visible;
                                RunInBg(log, ll => AppxManager.Uninstall(new System.Collections.Generic.List<string> { it.FullName }, ll),
                                    "已卸载: " + it.Name, () => { pb.Visibility = Visibility.Collapsed; RefreshList(); });
                            }, 60);
                            uninstBtn.FontSize = 11;
                            Grid.SetColumn(uninstBtn, 2);
                            rowGrid.Children.Add(uninstBtn);

                            rowBorder.Child = rowGrid;

                            // Issue 37: 鼠标悬停高亮（统一浅色 = _rowHover）
                            rowBorder.MouseEnter += (s, e) => { ((Border)s).Background = _rowHover; };
                            rowBorder.MouseLeave += (s, e) => { ((Border)s).Background = Brushes.Transparent; };

                            listStack.Children.Add(rowBorder);
                            rowItems.Add(Tuple.Create(chk, rowBorder, it));
                        }
                        countLbl.Text = $"[OK] 共 {items.Count} 个应用包";
                    });
                }, "列表已刷新", () => pb.Visibility = Visibility.Collapsed);
            }

            // 工具栏按钮（顺序：刷新 / 全选 / 卸载选中，绑定到对应列）
            var btnRefresh = Btn("🔄 刷新列表", true, () => RefreshList(true), 110);
            // 把"刷新"放到第 1 列（占满剩余的左侧），把全选和卸载选中右移——但我们只有 4 列
            // 改方案：把工具栏拆为 2 行：第 1 行 = 计数 + 操作按钮（用 Grid）
            // 简化：刷新单独放第一行（row 0），工具栏放第二行（row 1）
            // 这里先保持原结构，把"刷新"放最前（用户视觉优先级）
            // 重新设计：4 列 [刷新][占满][全选][卸载选中]
            // ——但 Grid 已 Add 了 countLbl 在 col 0，先把 countLbl 放第 1 列（占满）
            // 重做列定义

            // 工具栏：4 列均分（刷新 / 计数 / 全选 / 卸载选中）
            toolBar.Children.Clear();
            toolBar.ColumnDefinitions.Clear();
            for (int ti = 0; ti < 4; ti++)
                toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 全选/取消全选（合并为一个切换按钮）
            var rawAllSelected = false;
            // 选中计数更新（写入 StatusBar）—— 复用通用 UpdateSelStatus
            void UpdateRawSelCount()
            {
                int c = rowItems.Count(t => t.Item1.IsChecked == true);
                UpdateSelStatus(c, rowItems.Count, "个包");
            }
            var btnToggleRaw = Btn("📋 全选", false, null, 100);
            btnToggleRaw.HorizontalAlignment = HorizontalAlignment.Center;
            btnToggleRaw.Click += (s, e) =>
            {
                rawAllSelected = !rawAllSelected;
                foreach (var t in rowItems) t.Item1.IsChecked = rawAllSelected;
                btnToggleRaw.Content = rawAllSelected ? "☐ 取消全选" : "📋 全选";
                UpdateRawSelCount();
            };
            var btnUninstallSel = Btn("卸载选中", false, () =>
            {
                var sel = rowItems.Where(t => t.Item1.IsChecked == true && !string.IsNullOrEmpty(t.Item3.FullName))
                    .Select(t => t.Item3.FullName).ToList();
                if (sel.Count == 0) { log.AppendText("[!] 请先勾选要卸载的应用\r\n"); return; }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => AppxManager.Uninstall(sel, l), "卸载完成", () => { pb.Visibility = Visibility.Collapsed; RefreshList(true); });
            }, 110);
            btnUninstallSel.HorizontalAlignment = HorizontalAlignment.Center;

            btnRefresh.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(btnRefresh, 0);
            toolBar.Children.Add(btnRefresh);
            Grid.SetColumn(countLbl, 1);
            toolBar.Children.Add(countLbl);
            Grid.SetColumn(btnToggleRaw, 2);
            toolBar.Children.Add(btnToggleRaw);
            Grid.SetColumn(btnUninstallSel, 3);
            toolBar.Children.Add(btnUninstallSel);
            Grid.SetRow(toolBar, rootRow++);
            root.Children.Add(toolBar);

            // 列表卡（含 listScroll）
            var listCard = new Border
            {
                Background = _bgCard,
                CornerRadius = new CornerRadius(12),
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = listScroll
            };
            Grid.SetRow(listCard, rootRow++);
            root.Children.Add(listCard);

            Grid.SetRow(pb, rootRow++);
            root.Children.Add(pb);
            Grid.SetRow(logBorder, rootRow++);
            root.Children.Add(logBorder);

            // 打开页面时自动加载（静默加载，进度条不显示，避免切换页面时感觉慢）
            // 用户手动点 🔄 刷新列表 时才会显示进度条
            AutoLoad(() => RefreshList(false));

            // 关键：把 root 的 MaxHeight 绑定到 ContentArea.ViewportHeight（同 Appx 商店）
            // 这样外层 ContentArea 永不滚动，列表卡撑满剩余空间，溢出由 listScroll 内部滚动
            // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放，规避 vp=0 跳过）
            BindRootHeightToViewport(root);
            return root;
        }

        // =====================================================================
        //  Module: Defender（增强服务状态详情，参考 Win11EasyConfig）
        // =====================================================================

        // =====================================================================
        //  Module: 安全防护（Defender + 更新管理 合并页）
        // =====================================================================

        private UIElement BuildSecurity()
        {
            var root = new StackPanel();
            root.Children.Add(Header("安全防护", "Windows Defender 防病毒与 Windows Update 更新管控。均为高风险操作，谨慎使用。"));

            // 关键：BuildSecurity 入口不再同步刷 Defender 状态缓存，而是先渲染骨架，
            // 后台线程跑一次 PowerShell 拿全部 5 个值，再填充 UI，避免切页时 UI 卡死。

            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 100;
            log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var logBorder = WrapLogBox(log);

            // ===== 上：Windows Defender 卡片 =====
            var defCard = Card();
            var defInner = (StackPanel)defCard.Child;
            defInner.Children.Add(new TextBlock { Text = "🛡 Windows Defender", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 状态区（可刷新，禁用/恢复 WD 后重建而不丢日志）
            var defStatusHost = new StackPanel();
            defInner.Children.Add(defStatusHost);
            var defLoading = new TextBlock
            {
                Text = "正在检测 Defender 状态…",
                Foreground = _textDim,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
            defStatusHost.Children.Add(defLoading);

            void BuildDefenderStatus()
            {
                defStatusHost.Children.Clear();

                // 极简版：只显示一行总状态。详细 5 项由下方 toggle 区实时反映（避免视觉重复）。
                bool policyOff = Defender.IsDisabled();
                bool allOn = !policyOff;
                bool allOff = policyOff;
                bool fullyOk = (allOn || allOff) && Defender.LastOperationFullSuccess;
                var overallStatus = new TextBlock
                {
                    Text = allOff
                        ? (Defender.LastOperationFullSuccess ? "✓ 当前状态：实时保护已禁用" : "⚠ 当前状态：已禁用（部分失败）")
                        : "✓ 当前状态：正常运行",
                    Foreground = fullyOk ? _successGreen : _warnOrange,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                defStatusHost.Children.Add(overallStatus);

                var note = new TextBlock
                {
                    Text = allOff
                        ? "提示：下方 5 个开关可单独微调（无需重启）。⚠ 请勿重启——Windows 11 24H2+ 重启会还原 Defender 配置。恢复请点击右侧「一键恢复 WD」。"
                        : "提示：下方 5 个开关可单独切换（无需重启）。",
                    Foreground = fullyOk ? _textMain : _warnOrange,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                defStatusHost.Children.Add(note);
            }

            // 等宽均分整行：Grid(2×★Star) + 按钮居中、保持原始大小（与安全防护更新按钮行一致）
            var defWp = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(defWp);

            // 前移 defToggles 声明：bDisable/bEnable 的 onDone 闭包会调 SyncDefToggles，
            // SyncDefToggles 内部要引用 defToggles——必须先声明
            var defToggles = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
            defInner.Children.Add(defToggles);

            // Defender 按钮：填充状态与上方状态区同步（参考更新管理 RebuildUpdateButtons 模式）
            // 填充规则：哪个按钮代表"当前实际状态"，哪个就填充；点击后最后操作的按钮也填充
            string _lastDefAction = null;
            bool ShouldFillDef(string actionKey, bool stateDefault)
            {
                if (_lastDefAction != null)
                    return _lastDefAction == actionKey;
                return stateDefault;
            }
            void RebuildDefenderButtons()
            {
                bool disabled = Defender.IsDisabled();
                defWp.Children.Clear();
                var bDisable = Btn("✘ 一键禁用 WD", ShouldFillDef("disable", disabled), () =>
                {
                    _lastDefAction = "disable";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Disable, "已禁用 Defender", () => { pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); });
                });
                bDisable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bDisable, 0);
                defWp.Children.Add(bDisable);
                var bEnable = Btn("✔ 一键恢复 WD", ShouldFillDef("restore", !disabled), () =>
                {
                    _lastDefAction = "restore";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Enable, "已启用 Defender", () => { pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); });
                });
                bEnable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bEnable, 1);
                defWp.Children.Add(bEnable);
            }

            // ============ 5 个独立 Defender 开关（每个 Get/Set 实时同步） ============
            // 用 PowerShell Set-MpPreference 官方 API，立即生效、不需要重启、不需要 TI 提权。
            // TP 开启时部分选项（云保护/样本提交/TP 本身）会被拦，UI 会在异步回调时回滚到 Get* 当前值。
            // 注：defToggles 已在上面声明（bDisable/bEnable 闭包需要）
            // mkTog 放在 SyncDefToggles 函数体内（避免 click lambda 与 SyncDefToggles 互相引用的位置依赖）
            void SyncDefToggles(bool refreshCache = true)
            {
                // 重建前刷一次缓存（Set 后值变了，缓存可能过期）；初始加载时已在后台刷好，传 false 避免重复阻塞 UI
                if (refreshCache) Defender.RefreshStatusCache();
                defToggles.Children.Clear();
                System.Func<string, Func<bool>, Action<bool, Action<string>>, System.Windows.Controls.CheckBox> mkTog = (label, getState, setter) =>
                {
                    bool initial = false;
                    try { initial = getState(); } catch { }
                    var chk = new System.Windows.Controls.CheckBox
                    {
                        Content = label,
                        IsChecked = initial,
                        Foreground = _textMain,
                        FontSize = 13,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    chk.Click += (s, e) =>
                    {
                        bool target = chk.IsChecked == true;
                        pb.Visibility = Visibility.Visible;
                        RunInBg(log, l => setter(target, l), (target ? "已启用 " : "已禁用 ") + label,
                            () =>
                            {
                                pb.Visibility = Visibility.Collapsed;
                                SyncDefToggles();         // 重新读 Get* 刷新 toggle（Set 失败时自动回滚）
                                BuildDefenderStatus();    // 刷新"当前状态"行
                                RebuildDefenderButtons(); // 刷新一键禁用/恢复按钮填充
                            });
                    };
                    return chk;
                };

                defToggles.Children.Add(mkTog("实时保护（含开发人员驱动的保护）",
                    () => Defender.GetRealtime(), (b, l) => Defender.SetRealtime(b, l)));
                defToggles.Children.Add(mkTog("行为监控",
                    () => Defender.GetBehavior(), (b, l) => Defender.SetBehavior(b, l)));
                defToggles.Children.Add(mkTog("云提供的保护",
                    () => Defender.GetCloud(), (b, l) => Defender.SetCloud(b, l)));
                defToggles.Children.Add(mkTog("自动提交样本",
                    () => Defender.GetSampleSubmit(), (b, l) => Defender.SetSampleSubmit(b, l)));
                defToggles.Children.Add(mkTog("篡改防护（关后其它被锁开关才可改）",
                    () => Defender.GetTamper(), (b, l) => Defender.SetTamper(b, l)));
            }

            // 清理策略 + 诊断 Runtime 按钮同一行
            var policyBar = new Grid { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            policyBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            policyBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(policyBar);

            // 底部两个动作按钮也加入「最后点击高亮」互斥组，和清理页操作按钮保持一致
            var bClear = Btn("🧹 清理策略残留", false, null);
            bClear.HorizontalAlignment = HorizontalAlignment.Stretch;
            bClear.Margin = new Thickness(0);
            Grid.SetColumn(bClear, 0);
            policyBar.Children.Add(bClear);

            var bDiag = Btn("🔍 诊断 Runtime 状态", false, null);
            bDiag.HorizontalAlignment = HorizontalAlignment.Stretch;
            bDiag.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(bDiag, 1);
            policyBar.Children.Add(bDiag);

            // 初始加载完成前禁用底部动作按钮，避免用户点击时触发同步阻塞
            bClear.IsEnabled = false;
            bDiag.IsEnabled = false;

            // 局部函数：切换底部两个按钮的高亮态（点击谁谁变 accent）
            void ApplyPolicyMode(Button sel)
            {
                foreach (var b in new[] { bClear, bDiag })
                {
                    if (b == null) continue;
                    bool on = b == sel;
                    b.Background = on ? _accent : _btnSecondaryBg;
                    b.Foreground = on ? _btnPrimaryFg : _btnSecondaryFg;
                    b.BorderBrush = on ? Brushes.Transparent : _panelBorder;
                    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }

            bClear.Click += (s, e) =>
            {
                ApplyPolicyMode(bClear);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => Defender.ClearAllPolicies(l), "策略已清理", () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                    SyncDefToggles();
                    BuildDefenderStatus();
                    RebuildDefenderButtons();
                });
            };
            bDiag.Click += (s, e) =>
            {
                ApplyPolicyMode(bDiag);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Defender.DiagnoseRuntime, "诊断完成", () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                });
            };

            // 默认不高亮底部动作按钮
            ApplyPolicyMode(null);

            // 后台一次性拉取 Defender 状态，避免切页卡顿；UI 先显示骨架，缓存好后瞬间填充
            pb.Visibility = Visibility.Visible;
            var disp = Dispatcher;
            new Thread(() =>
            {
                try { Defender.RefreshStatusCache(); }
                catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
                disp.Invoke(() =>
                {
                    defStatusHost.Children.Remove(defLoading);
                    BuildDefenderStatus();
                    RebuildDefenderButtons();
                    SyncDefToggles(false);
                    bClear.IsEnabled = true;
                    bDiag.IsEnabled = true;
                    pb.Visibility = Visibility.Collapsed;
                });
            }) { IsBackground = true, Name = "DefenderInitLoader" }.Start();

            root.Children.Add(defCard);

            // ===== 中：Windows Defender 防火墙卡片 =====
            var fwCard = Card();
            var fwInner = (StackPanel)fwCard.Child;
            fwInner.Children.Add(new TextBlock { Text = "🛡 Windows Defender 防火墙", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 状态区（异步加载）
            var fwStatusHost = new StackPanel();
            fwInner.Children.Add(fwStatusHost);
            fwStatusHost.Children.Add(new TextBlock { Text = "正在检测防火墙状态…", Foreground = _textDim, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });

            var fwProfileMap = new Dictionary<string, string> { ["Domain"] = "域", ["Private"] = "专用", ["Public"] = "公用" };
            void BuildFirewallStatus(List<FirewallCore.ProfileInfo> preset = null)
            {
                fwStatusHost.Children.Clear();
                var profiles = preset ?? FirewallCore.GetProfiles();
                if (profiles == null || profiles.Count == 0)
                {
                    fwStatusHost.Children.Add(new TextBlock { Text = "⚠ 未能读取防火墙状态（请查看下方日志了解具体原因）", Foreground = _warnOrange, FontSize = 13, TextWrapping = TextWrapping.Wrap });
                    return;
                }
                foreach (var p in profiles)
                {
                    var cn = fwProfileMap.ContainsKey(p.Name) ? fwProfileMap[p.Name] : p.Name;
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = cn + " 配置文件", Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center });
                    var tbState = new TextBlock
                    {
                        Text = p.Enabled ? "● 已开启" : "○ 已关闭",
                        Foreground = p.Enabled ? _successGreen : _warnOrange,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(tbState, 1);
                    row.Children.Add(tbState);
                    fwStatusHost.Children.Add(row);
                }
            }

            // 操作按钮行：打开高级安全 + 刷新状态
            var fwBtnRow = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            fwBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fwBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bOpenFw = Btn("🔧 打开高级安全", false, () => FirewallCore.OpenAdvanced());
            bOpenFw.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bOpenFw, 0);
            fwBtnRow.Children.Add(bOpenFw);
            var bRefreshFw = Btn("🔄 刷新状态", false, null);
            bRefreshFw.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRefreshFw, 1);
            fwBtnRow.Children.Add(bRefreshFw);
            fwInner.Children.Add(fwBtnRow);

            // 规则管理面板
            fwInner.Children.Add(new TextBlock { Text = "🔧 防火墙规则管理", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) });

            var TELEMETRY_HOSTS = new[] { "vortex-win.data.microsoft.com", "settings-win.data.microsoft.com", "watson.telemetry.microsoft.com", "telemetry.microsoft.com", "oca.telemetry.microsoft.com" };

            // 添加常用规则按钮行（4 列均分：阻止 SearchHost / 阻止遥测 / 移除 SearchHost / 移除选中）
            var ruleAddBar = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bBlockSearch = Btn("➕ 阻止 SearchHost 联网", false, null);
            bBlockSearch.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bBlockSearch, 0);
            ruleAddBar.Children.Add(bBlockSearch);
            var bBlockTele = Btn("➕ 阻止遥测域", false, null);
            bBlockTele.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bBlockTele, 1);
            ruleAddBar.Children.Add(bBlockTele);
            var bRemoveSearch = Btn("➖ 移除 SearchHost 规则", false, null);
            bRemoveSearch.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRemoveSearch, 2);
            ruleAddBar.Children.Add(bRemoveSearch);
            var bRemoveSel = Btn("🗑 移除选中规则", false, null);
            bRemoveSel.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRemoveSel, 3);
            ruleAddBar.Children.Add(bRemoveSel);
            fwInner.Children.Add(ruleAddBar);

            // 规则列表
            var ruleList = new System.Windows.Controls.ListBox
            {
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 180
            };
            var ruleScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = ruleList, MaxHeight = 180 };
            fwInner.Children.Add(ruleScroll);

            // 空状态提示：若 PowerShell 执行失败，真实错误会输出到日志，这里不再盲目归因于权限
            var ruleEmptyHint = new TextBlock
            {
                Text = "未获取到防火墙规则（请查看下方日志了解具体原因）。",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            fwInner.Children.Add(ruleEmptyHint);

            void LoadFirewallData()
            {
                pb.Visibility = Visibility.Visible;
                var d = Dispatcher;
                new Thread(() =>
                {
                    // 后台线程经 Dispatcher 封送写日志，避免跨线程访问 UI；FirewallCore 内部已兜底，此处不再静默吞错
                    Action<string> flog = s => d.Invoke(() => log.AppendText("[防火墙] " + s + "\r\n"));
                    var profiles = FirewallCore.GetProfiles(flog);
                    var rules = FirewallCore.ListRules(flog);
                    d.Invoke(() =>
                    {
                        BuildFirewallStatus(profiles);
                        var ruleSrc = rules ?? new List<FirewallCore.RuleInfo>();
                        ruleList.ItemsSource = ruleSrc;
                        ruleEmptyHint.Visibility = ruleSrc.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        pb.Visibility = Visibility.Collapsed;
                    });
                }) { IsBackground = true, Name = "FirewallLoader" }.Start();
            }

            bRefreshFw.Click += (s, e) => LoadFirewallData();
            bBlockSearch.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.AddSearchFirewallRule, "已添加阻止 SearchHost 规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bBlockTele.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.AddBlockAddressRule("阻止Windows遥测域", TELEMETRY_HOSTS, l), "已添加阻止遥测域规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bRemoveSearch.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.RemoveSearchFirewallRule, "已移除 SearchHost 规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bRemoveSel.Click += (s, e) =>
            {
                var src = ruleList.ItemsSource as System.Collections.IList;
                if (src == null || src.Count == 0)
                {
                    System.Windows.MessageBox.Show(this, "未获取到防火墙规则列表。请查看页面下方日志了解具体原因，若提示访问被拒绝则需以管理员身份运行程序。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                var sel = ruleList.SelectedItem as FirewallCore.RuleInfo;
                if (sel == null) { System.Windows.MessageBox.Show(this, "请先在列表中选择一条规则", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); return; }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.RemoveRule(sel.DisplayName, l), "已移除规则: " + sel.DisplayName, () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };

            root.Children.Add(fwCard);
            // 打开页面时静默加载防火墙状态与规则列表
            LoadFirewallData();

            // ===== 下：Windows 更新管理卡片 =====
            var updCard = Card();
            var updInner = (StackPanel)updCard.Child;
            updInner.Children.Add(new TextBlock { Text = "⬇ Windows 更新", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // Windows 更新卡片状态改为异步加载：避免切页时同步调用 reg.exe / PowerShell 阻塞 UI。
            // 先以默认值渲染骨架按钮，后台线程读取真实状态后再刷新高亮。
            var updateState = (blocked: false, paused: false, metered: false);

            void LoadUpdateState()
            {
                var d = Dispatcher;
                new Thread(() =>
                {
                    bool b = false, p = false, m = false;
                    try { b = Updater.IsUpdatesBlocked(); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
                    try { p = Updater.IsLongPaused(); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
                    try { m = MeteredConnection.IsMetered(); } catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message); }
                    d.Invoke(() =>
                    {
                        updateState = (b, p, m);
                        RebuildUpdateButtons();
                    });
                }) { IsBackground = true, Name = "UpdateStateLoader" }.Start();
            }

            bool ShouldFill(string actionKey, bool stateDefault)
            {
                if (_lastUpdateAction != null)
                    return _lastUpdateAction == actionKey;
                return stateDefault;
            }

            var updateBtnHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (int ci = 0; ci < 6; ci++)
                updateBtnHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updInner.Children.Add(updateBtnHost);

            // 更新操作：写操作完成后重新异步读取真实状态并刷新按钮高亮；读操作保留日志内容。
            void RunUpdate(Action<Action<string>> work, string label, bool navWhenDone = true)
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, work, label, () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                    if (navWhenDone) LoadUpdateState();
                });
            }

            void RebuildUpdateButtons()
            {
                updateBtnHost.Children.Clear();
                void AddBtn(string text, bool primary, Action onClick, int col)
                {
                    var b = Btn(text, primary, onClick);
                    b.HorizontalAlignment = HorizontalAlignment.Center;   // 按钮保持原始大小，居中于列
                    b.Margin = new Thickness(0);
                    Grid.SetColumn(b, col);
                    updateBtnHost.Children.Add(b);
                }
                AddBtn("禁用更新", ShouldFill("block", updateState.blocked), () => { _lastUpdateAction = "block"; RunUpdate(Updater.BlockUpdates, "已禁用更新"); }, 0);
                AddBtn("恢复更新", ShouldFill("restore", !updateState.blocked), () => { _lastUpdateAction = "restore"; RunUpdate(Updater.RestoreUpdates, "已恢复更新"); }, 1);
                AddBtn("长期暂停(10000天)", ShouldFill("pause", updateState.paused), () => { _lastUpdateAction = "pause"; RunUpdate(Updater.AllowLongPause, "已设置长期暂停"); }, 2);
                AddBtn("查看更新状态", ShouldFill("status", false), () => { _lastUpdateAction = "status"; RunUpdate(Updater.UpdateStatus, "状态已刷新", false); }, 3);
                AddBtn("计量连接 · 切换", ShouldFill("metered-toggle", updateState.metered), () => { _lastUpdateAction = "metered-toggle"; RunUpdate(MeteredConnection.ToggleMetered, "计量连接已切换"); }, 4);
                AddBtn("计量连接 · 状态", ShouldFill("metered-status", false), () => { _lastUpdateAction = "metered-status"; RunUpdate(MeteredConnection.MeteredStatus, "状态已刷新", false); }, 5);
            }
            RebuildUpdateButtons();
            LoadUpdateState();

            root.Children.Add(updCard);
            root.Children.Add(pb);
            root.Children.Add(logBorder);
            return root;
        }

        private static bool CheckServiceExists(string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, false)) return k != null; }
            catch { return false; }
        }

        /// <summary>查询服务进程是否真正在运行（使用 ServiceController，非注册表值）</summary>
        private static bool CheckServiceRunning(string name)
        {
            try { return new ServiceController(name).Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }

        private static bool ServiceStartDisabled(string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, false)) { var v = k?.GetValue("Start"); return v != null && (int)v == 4; } }
            catch { return false; }
        }

        private static bool CheckTamperProtection()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Features", false))
                { var v = k?.GetValue("TamperProtection"); return v != null && ((int)v == 1 || (int)v == 5); }
            }
            catch { return false; }
        }

        // =====================================================================
        //  Module: Edge / WebView2 管理（参考 Win11EasyConfig Form3 设计，独立实现，两列布局）
        // =====================================================================

        private UIElement BuildEdge()
        {
            var root = new StackPanel();
            root.Children.Add(Header("Edge / WebView2", "Microsoft Edge 浏览器（含 Stable/Beta/Dev/Canary/SxS）和 WebView2 Runtime 的安装、卸载、自动更新、启动增强控制。"));

            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 100;
            var logBorder = WrapLogBox(log);

            // 2 列 Grid（左右各 1 个独立 Card）
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });   // 间距
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

            Grid.SetColumn(rightCard, 2);
            mainGrid.Children.Add(rightCard);

            root.Children.Add(mainGrid);
            root.Children.Add(pb);
            root.Children.Add(logBorder);
            return root;
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
//        //  Module: 软件安装（已删除，与常用软件重复 — 合并到 BuildCommonSoftware）
//        // =====================================================================

// =====================================================================
        //  Module: 常用软件（参考 Win11EasyConfig 风格：表格 + 一键安装/卸载）
        // =====================================================================

        private UIElement BuildCommonSoftware()
        {
            // Grid 布局：header / actionBar / toolBar / 列表卡（撑满）/ pb / log
            var root = new Grid();
            // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放）
            BindRootHeightToViewport(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0: header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1: actionBar（刷新/安装到/清理）
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 2: 列表卡（Star 撑满剩余空间，listScroll 自动滚动）
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 3: pb
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 4: log
            int rootRow = 0;

            // row 0: 描述（左）+ actionBar（右）放同一行
            var headerTb = Header("常用软件", "精选常用软件，一键安装/卸载。已安装的显示版本号和绿色状态，未安装的显示红色。");
            // 创建一个 2 列 Grid 让 actionBar 跟描述共享 row 0
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 描述占满左侧
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // actionBar 右侧
            Grid.SetColumn(headerTb, 0);
            headerRow.Children.Add(headerTb);
            // 创建一个占位 Border 占住 col 1，Dispatcher 完成后 actionBar 替换它
            var actionBarSlot = new Border { HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 1 };
            Grid.SetColumn(actionBarSlot, 1);
            headerRow.Children.Add(actionBarSlot);
            Grid.SetRow(headerRow, rootRow++);  // row 0
            root.Children.Add(headerRow);

            // 占位符：先放进 root row 2（列表卡位置），等扫描完再替换
            var loadingSp = new StackPanel { Margin = new Thickness(0, 30, 0, 30), HorizontalAlignment = HorizontalAlignment.Center };
            loadingSp.Children.Add(new TextBlock { Text = "⏳ 正在扫描已安装软件...", Foreground = _textDim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            var loadingBar = new ProgressBar { IsIndeterminate = true, Width = 200, Height = 4, Margin = new Thickness(0, 10, 0, 0), Foreground = _accent, Background = _panelBorder };
            loadingSp.Children.Add(loadingBar);
            var loadingBorder = new Border { Child = loadingSp, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Background = _bgCard, CornerRadius = new CornerRadius(12), BorderBrush = _panelBorder, BorderThickness = new Thickness(1), Padding = new Thickness(16) };
            Grid.SetRow(loadingBorder, 2);  // 列表卡固定在 row 2（预留 row 0=headerRow, row 1=toolBar）
            root.Children.Add(loadingBorder);

            var pb = MakeProgress();
            Grid.SetRow(pb, 3);
            root.Children.Add(pb);
            var log = MakeLogBox();
            var logBorder = WrapLogBox(log);
            Grid.SetRow(logBorder, 4);
            root.Children.Add(logBorder);

            // 后台线程：扫描所有软件安装状态
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var allSw = SoftwareInstall.GetAllStatus();
                    Dispatcher.Invoke(() =>
                    {
                        // 移除占位
                        root.Children.Remove(loadingBorder);
                        int listRow = 2;  // 列表卡固定在 row 2（让出 row 0=headerRow, row 1=toolBar）
                        log.Visibility = Visibility.Collapsed;  // 默认隐藏

                        // ========== 搜索/选择/批量操作栏（DockPanel：搜索框填满剩余，按钮右对齐） ==========
                        var toolBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

                        // 分类筛选下拉（左对齐）：用 ToggleButton + Popup + ListBox 替代 ComboBox
                        // 原因：ComboBox 默认模板未把 ScrollViewer.CanContentScroll 通过 TemplateBinding 接出来，
                        // 导致物理滚动补丁不可靠，滚动时底部仍会出现空白行；ListBox 模板会 TemplateBind，因此可控。
                        // 自定义 ToggleButton 模板：Border 承载背景 + IsMouseOver/IsChecked 触发器，
                        // 悬浮色直接复用标准按钮的 ButtonHoverBrush 资源（DynamicResource，随主题切换），
                        // 与「全选」等按钮悬浮色完全一致，避免之前用 _rowHover 偏暗导致的色差；
                        // 选中态用 accent 更高不透明度的变体做区分（仍与主题一致）
                        var catBtnTemplate = new ControlTemplate(typeof(ToggleButton));
                        var catBtnBd = new FrameworkElementFactory(typeof(Border), "Bd");
                        catBtnBd.SetBinding(Border.BackgroundProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BackgroundProperty) });
                        catBtnBd.SetBinding(Border.BorderBrushProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BorderBrushProperty) });
                        catBtnBd.SetBinding(Border.BorderThicknessProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(BorderThicknessProperty) });
                        catBtnBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                        catBtnBd.SetBinding(Border.PaddingProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(PaddingProperty) });
                        var catBtnCp = new FrameworkElementFactory(typeof(ContentPresenter));
                        catBtnCp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                        catBtnCp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                        catBtnBd.AppendChild(catBtnCp);
                        catBtnTemplate.VisualTree = catBtnBd;
                        // 选中态填充：accent 更高不透明度，比 hover 更明显（仍与主题一致）
                        var catSelectedBrush = _isDarkMode
                            ? new SolidColorBrush(Color.FromArgb(0x73, 0x16, 0xE0, 0xBD))  // #16E0BD @ ~45%
                            : new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x91, 0x82)); // #089182 @ ~55%
                        var catBtnHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                        catBtnHover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ButtonHoverBrush"), "Bd"));
                        var catBtnChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                        catBtnChecked.Setters.Add(new Setter(Border.BackgroundProperty, catSelectedBrush, "Bd"));
                        catBtnTemplate.Triggers.Add(catBtnHover);
                        catBtnTemplate.Triggers.Add(catBtnChecked);

                        var catBtn = new ToggleButton
                        {
                            FontSize = 13,
                            MinHeight = 34,
                            Padding = new Thickness(6, 4, 6, 4),
                            BorderBrush = _panelBorder,
                            BorderThickness = new Thickness(1),
                            Margin = new Thickness(0, 0, 8, 0),
                            MinWidth = 100,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                            Foreground = _textMain,
                            Template = catBtnTemplate
                        };
                        // 按钮内容：文字 + 右侧下拉箭头，模拟 ComboBox 外观
                        var catBtnText = new TextBlock { Text = "全部分类", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                        var catBtnContent = UiShapes.MakeTextWithArrowGrid(catBtnText, _textDim, minWidth: true);
                        catBtn.Content = catBtnContent;

                        var catList = new ListBox
                        {
                            FontSize = 13,
                            BorderThickness = new Thickness(0),
                            Background = Brushes.Transparent,
                            Foreground = _textMain,
                            Padding = new Thickness(0),
                            MaxHeight = 280
                        };
                        VirtualizingPanel.SetIsVirtualizing(catList, false);
                        catList.ItemsPanel = new ItemsPanelTemplate(new System.Windows.FrameworkElementFactory(typeof(StackPanel)));
                        ScrollViewer.SetCanContentScroll(catList, false); // ListBox 模板会 TemplateBind，这里真正生效
                        var catItemStyle = new Style(typeof(ListBoxItem));
                        catItemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
                        catItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 4, 6)));
                        catItemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
                        catItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
                        catItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                        var catItemHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                        catItemHover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ButtonHoverBrush")));
                        var catItemSelected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                        catItemSelected.Setters.Add(new Setter(Control.BackgroundProperty, catSelectedBrush));
                        catItemStyle.Triggers.Add(catItemHover);
                        catItemStyle.Triggers.Add(catItemSelected);
                        catList.ItemContainerStyle = catItemStyle;
                        catList.Items.Add("全部分类");
                        foreach (var c in SoftwareInstall.SoftwareCategories) catList.Items.Add(c);
                        catList.SelectedIndex = 0;

                        var catPopupBorder = new Border
                        {
                            Background = _btnSecondaryBg,
                            BorderBrush = _panelBorder,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Child = catList
                        };

                        var catPopup = new Popup
                        {
                            PlacementTarget = catBtn,
                            Placement = PlacementMode.Bottom,
                            StaysOpen = false,
                            AllowsTransparency = true,
                            Child = catPopupBorder,
                            MaxHeight = 280
                        };
                        catPopup.Opened += (s, e) =>
                        {
                            catBtn.IsChecked = true;
                            catPopup.Width = Math.Max(catBtn.ActualWidth, 100);
                        };
                        catPopup.Closed += (s, e) => catBtn.IsChecked = false;
                        catBtn.Click += (s, e) => catPopup.IsOpen = !catPopup.IsOpen;
                        catList.SelectionChanged += (s, e) =>
                        {
                            catBtnText.Text = catList.SelectedItem?.ToString() ?? "全部分类";
                            catPopup.IsOpen = false;
                        };

                        DockPanel.SetDock(catBtn, Dock.Left);
                        toolBar.Children.Add(catBtn);

                        // 右侧按钮（先 Dock，按添加顺序从右向左排列）
                        var btnUninstall = Btn("🗑 卸载选中", false, null, 110);
                        DockPanel.SetDock(btnUninstall, Dock.Right);
                        toolBar.Children.Add(btnUninstall);

                        var btnInstall = Btn("⬇ 安装选中", true, null, 110);
                        DockPanel.SetDock(btnInstall, Dock.Right);
                        toolBar.Children.Add(btnInstall);

                        var btnAll = Btn("☑ 全选", false, null, 80);
                        DockPanel.SetDock(btnAll, Dock.Right);
                        toolBar.Children.Add(btnAll);

                        // 筛选结果计数（右对齐）
                        var countLabel = new TextBlock { Text = $"共 {allSw.Count} 款", Foreground = _textDim, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
                        DockPanel.SetDock(countLabel, Dock.Right);
                        toolBar.Children.Add(countLabel);

                        // 搜索框（最后添加，自动填满剩余空间）
                        // 内部用 Grid 叠放 TextBox + 🔍 图标（用 Panel.ZIndex 强制 z-stack）
                        var (searchBoxWrap, searchBox) = MakeSearchBox(13, null, true);
                        toolBar.Children.Add(searchBoxWrap);

                        Grid.SetRow(toolBar, 1);  // 工具栏放 row 1（row 0 = headerRow 包含描述+actionBar）
                        root.Children.Add(toolBar);

                        // 列表卡：ScrollViewer 高度由 root row2(Star) → listCard(Border Stretch) 约束，自动填满+滚动
                        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = _isDarkMode ? Brushes.Transparent : _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 12, 0) };
                        var listInner = new StackPanel();
                        listScroll.Content = listInner;
                        var listCard = new Border
                        {
                            Background = _bgCard,
                            CornerRadius = new CornerRadius(12),
                            BorderBrush = _panelBorder,
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(16),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            ClipToBounds = true,
                            Child = listScroll
                        };
                        Grid.SetRow(listCard, listRow);  // row 2
                        root.Children.Add(listCard);

                        // 软件列表：每行一个 Border（含内部 Grid 5 列），整行背景可切
                        var rowsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                        // 选中行背景色：跟随主题（深色=深青蓝；浅色=浅天蓝）+ 未选中行=透明
                        var selectedRowBg = _rowSelected;
                        var defaultRowBg = Brushes.Transparent;
                        var hoverBg = _rowHover;

                        // ---- 表头行 ----
                        var hdrBorder = new Border
                        {
                            Background = _isDarkMode ? Brushes.Transparent : _bgTableHead,
                            BorderBrush = _panelBorder,
                            BorderThickness = new Thickness(1, 1, 1, 0),
                            Padding = new Thickness(0, 6, 0, 6)
                        };
                        var hdrGrid = new Grid();
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // 勾选（紧凑）
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // 软件名称
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // 分类
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.65, GridUnitType.Star) }); // 安装（窄）
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) }); // 卸载（窄，靠近安装）
                        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) }); // 状态（宽）
                        string[] colNames = { "", "软件名称", "分类", "安装", "卸载", "状态" };
                        for (int c = 0; c < colNames.Length; c++)
                        {
                            var hdr = new Label
                            {
                                Content = colNames[c],
                                FontWeight = FontWeights.SemiBold,
                                Foreground = _accent,
                                FontSize = 13,
                                Padding = new Thickness(0),
                                VerticalContentAlignment = VerticalAlignment.Center,
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                                Background = Brushes.Transparent
                            };
                            Grid.SetColumn(hdr, c);
                            hdrGrid.Children.Add(hdr);
                        }
                        // 表头勾选列占位：与数据行 CheckBox 同尺寸/边距，否则 Auto 列宽为 0，导致后面 Star 列总宽与数据行不一致、表头错位
                        var hdrChkPlaceholder = new System.Windows.Controls.CheckBox
                        {
                            IsEnabled = false,
                            Opacity = 0,
                            Margin = new Thickness(8, 6, 8, 6),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        Grid.SetColumn(hdrChkPlaceholder, 0);
                        hdrGrid.Children.Add(hdrChkPlaceholder);

                        hdrBorder.Child = hdrGrid;
                        rowsPanel.Children.Add(hdrBorder);

                        // 存储每行的 (CheckBox, Border, SoftwareInfo)
                        var rowItems = new List<Tuple<System.Windows.Controls.CheckBox, Border, SoftwareInstall.SoftwareInfo>>();
                        // 状态/按钮映射（id → 控件），供卸载/安装后原地刷新，避免重建整页丢失日志
                        var statusMap = new Dictionary<string, Label>();
                        var installBtnMap = new Dictionary<string, Button>();
                        var uninstallBtnMap = new Dictionary<string, Button>();
                        // 原地刷新单行状态与按钮（不重建页面，保留日志可见）
                        void RefreshRow(string id, SoftwareInstall.SoftwareInfo info)
                        {
                            if (info == null) return;
                            // 同步更新缓存的数据对象（供后续批量操作判断）
                            var stored = rowItems.FirstOrDefault(t => t.Item3.Id == id);
                            if (stored != null) { stored.Item3.Installed = info.Installed; stored.Item3.Version = info.Version; }

                            if (statusMap.TryGetValue(id, out var tb))
                            {
                                string verStr = !string.IsNullOrEmpty(info.Version) ? " · 版本: " + info.Version : "";
                                tb.Content = info.Installed ? ("已安装" + verStr) : "未安装";
                                tb.Foreground = info.Installed ? _successGreen : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                                tb.FontWeight = info.Installed ? FontWeights.SemiBold : FontWeights.Normal;
                            }
                            if (installBtnMap.TryGetValue(id, out var instBtn))
                                instBtn.Content = info.Installed ? "修复安装" : "一键安装";
                            if (uninstallBtnMap.TryGetValue(id, out var uninstBtn))
                            {
                                uninstBtn.IsEnabled = info.Installed;
                                uninstBtn.Opacity = info.Installed ? 1.0 : 0.4;
                            }
                        }
                        // 卸载/安装完成后自动全量刷新列表（保留日志），进度条在刷新期间保持可见
                        void RefreshAllRows(Action onComplete = null)
                        {
                            pb.Visibility = Visibility.Visible;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try
                                {
                                    var list = SoftwareInstall.GetAllStatus();
                                    Dispatcher.Invoke(() =>
                                    {
                                        foreach (var info in list) RefreshRow(info.Id, info);
                                        pb.Visibility = Visibility.Collapsed;
                                        onComplete?.Invoke();
                                    });
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        pb.Visibility = Visibility.Collapsed;
                                        log.AppendText("[!] 刷新列表失败: " + ex.Message + "\r\n");
                                        onComplete?.Invoke();
                                    });
                                }
                            });
                        }
                        var swAllSelected = false;

                        // 选中计数更新（写入窗口底部 StatusBar，绝对最底部）—— 复用通用 UpdateSelStatus
                        void UpdateSelCount()
                        {
                            int c = rowItems.Count(t => t.Item1.IsChecked == true);
                            UpdateSelStatus(c, allSw.Count, "款");
                        }
                        // 初始显示
                        UpdateSelCount();

                        // 给已创建的 btnAll（☐ 全选）补上 Click 事件（btnAll 已在 DockPanel 中创建）
                        btnAll.Click += (s, e) =>
                        {
                            swAllSelected = !swAllSelected;
                            foreach (var t in rowItems) t.Item1.IsChecked = swAllSelected;
                            btnAll.Content = swAllSelected ? "☐ 取消全选" : "☑ 全选";
                            UpdateSelCount();
                        };

                        foreach (var sw in allSw)
                        {
                            // 一行容器
                            var rowBorder = new Border
                            {
                                Background = defaultRowBg,
                                BorderBrush = _panelBorder,
                                BorderThickness = new Thickness(1, 0, 1, 1),
                                Padding = new Thickness(0, 0, 0, 0)
                            };
                            // 鼠标悬停轻微高亮
                            rowBorder.MouseEnter += (s, e) => { if (((Border)s).Background != selectedRowBg) ((Border)s).Background = hoverBg; };
                            rowBorder.MouseLeave += (s, e) =>
                            {
                                var b = (Border)s;
                                var chk = b.Tag as System.Windows.Controls.CheckBox;
                                b.Background = (chk != null && chk.IsChecked == true) ? selectedRowBg : defaultRowBg;
                            };

                            var rowGrid = new Grid();
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // 勾选（紧凑）
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // 软件名称
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // 分类
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.65, GridUnitType.Star) }); // 安装（窄）
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) }); // 卸载（窄，靠近安装）
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) }); // 状态（宽）

                            // 勾选
                            var chk = new System.Windows.Controls.CheckBox
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Margin = new Thickness(8, 6, 8, 6),
                                IsChecked = false,
                                Foreground = _textMain
                            };
                            Grid.SetColumn(chk, 0);
                            // 选中时整行背景变蓝 + 更新底部计数
                            chk.Checked += (s, e) => { rowBorder.Background = selectedRowBg; UpdateSelCount(); };
                            chk.Unchecked += (s, e) => { rowBorder.Background = defaultRowBg; UpdateSelCount(); };
                            rowGrid.Children.Add(chk);
                            rowBorder.Tag = chk;  // 用于 mouseLeave 事件回查

                            // 软件名称
                            var nameCell = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Margin = new Thickness(0, 4, 0, 4)
                            };
                            var nameTb = new TextBlock
                            {
                                Text = sw.Name,
                                Foreground = _textMain,
                                FontSize = 13,
                                FontWeight = FontWeights.SemiBold,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            };
                            nameCell.Children.Add(nameTb);
                            Grid.SetColumn(nameCell, 1);
                            rowGrid.Children.Add(nameCell);

                            // 分类（独立列，居中对齐）
                            var catText = string.IsNullOrEmpty(sw.Category) ? SoftwareInstall.DefaultCategory : sw.Category;
                            var catPill = new Border
                            {
                                Background = _isDarkMode
                                    ? new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x4A))
                                    : new SolidColorBrush(Color.FromRgb(0xEF, 0xE7, 0xFB)),
                                BorderBrush = _isDarkMode
                                    ? new SolidColorBrush(Color.FromRgb(0x4A, 0x3A, 0x7A))
                                    : new SolidColorBrush(Color.FromRgb(0xD9, 0xC7, 0xF0)),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(9),
                                Padding = new Thickness(6, 2, 6, 2),
                                Margin = new Thickness(0),
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center
                            };
                            catPill.Child = new TextBlock
                            {
                                Text = catText,
                                FontSize = 10.5,
                                Foreground = _isDarkMode
                                    ? new SolidColorBrush(Color.FromRgb(0xC9, 0xB8, 0xF0))
                                    : new SolidColorBrush(Color.FromRgb(0x5B, 0x3A, 0x8C)),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextAlignment = TextAlignment.Center
                            };
                            Grid.SetColumn(catPill, 2);
                            rowGrid.Children.Add(catPill);

                            // 安装/修复按钮
                            var instBtnText = sw.Installed ? "修复安装" : "一键安装";
                            var instBtn = Btn(instBtnText, false, () =>
                            {
                                pb.Visibility = Visibility.Visible;
                                string customDir = null;
                                try
                                {
                                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\CpqSystemTool"))
                                        customDir = k?.GetValue("InstallPath") as string;
                                }
                                catch { }
                                RunInBg(log, l => SoftwareInstall.Install(sw.Id, l, customDir),
                                    (sw.Installed ? "修复完成: " : "安装完成: ") + sw.Name, () => { RefreshAllRows(); });
                            }, 90);
                            instBtn.MinHeight = 26;
                            instBtn.Margin = new Thickness(2, 2, 2, 2);
                            instBtn.Padding = new Thickness(6, 2, 6, 2);
                            instBtn.FontSize = 11;
                            instBtn.HorizontalAlignment = HorizontalAlignment.Center;
                            Grid.SetColumn(instBtn, 3);
                            rowGrid.Children.Add(instBtn);
                            installBtnMap[sw.Id] = instBtn;

                            // 卸载按钮
                            var uninstBtn = Btn("卸载", false, () =>
                            {
                                pb.Visibility = Visibility.Visible;
                            RunInBg(log, l => SoftwareInstall.Uninstall(sw.Id, l),
                                "卸载完成: " + sw.Name, () =>
                                {
                                    // 卸载完成后自动全量刷新列表（保留日志可见），状态/按钮同步更新
                                    RefreshAllRows(() => log.AppendText("— 卸载流程结束，详情见上方日志 —\r\n"));
                                });
                            }, 60);
                            uninstBtn.MinHeight = 26;
                            uninstBtn.Margin = new Thickness(2, 2, 2, 2);
                            uninstBtn.Padding = new Thickness(6, 2, 6, 2);
                            uninstBtn.FontSize = 11;
                            uninstBtn.HorizontalAlignment = HorizontalAlignment.Center;
                            uninstBtn.IsEnabled = sw.Installed;
                            uninstBtn.Opacity = sw.Installed ? 1.0 : 0.4;
                            Grid.SetColumn(uninstBtn, 4);
                            rowGrid.Children.Add(uninstBtn);
                            uninstallBtnMap[sw.Id] = uninstBtn;

                            // 状态
                            string verStr = !string.IsNullOrEmpty(sw.Version) ? " · 版本: " + sw.Version : "";
                            var statusTb = new Label
                            {
                                Content = sw.Installed ? ("已安装" + verStr) : "未安装",
                                Foreground = sw.Installed ? _successGreen : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                                FontSize = 12.5,
                                FontWeight = sw.Installed ? FontWeights.SemiBold : FontWeights.Normal,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                                Padding = new Thickness(0),
                                Background = Brushes.Transparent
                            };
                            Grid.SetColumn(statusTb, 5);
                            rowGrid.Children.Add(statusTb);

                            rowBorder.Child = rowGrid;
                            rowsPanel.Children.Add(rowBorder);
                            rowItems.Add(Tuple.Create(chk, rowBorder, sw));
                            statusMap[sw.Id] = statusTb;
                        }

                        // 搜索过滤逻辑（名称模糊匹配 + 分类下拉筛选）
                        // 例："7z" → 匹配 "7-Zip"；"视频" → 匹配分类含「视频软件」的条目
                        void ApplyFilter()
                        {
                            string q = searchBox.Text.Trim().ToLowerInvariant();
                            string qClean = string.IsNullOrEmpty(q) ? "" : new string(q.Where(c => char.IsLetterOrDigit(c)).ToArray());
                            string catSel = catList.SelectedIndex > 0 ? SoftwareInstall.SoftwareCategories[catList.SelectedIndex - 1] : null;
                            int visible = 0;
                            foreach (var t in rowItems)
                            {
                                bool match;
                                if (string.IsNullOrEmpty(q) && catSel == null)
                                    match = true;
                                else
                                {
                                    string cat = t.Item3.Category ?? SoftwareInstall.DefaultCategory;
                                    string nameLower = t.Item3.Name.ToLowerInvariant();
                                    string nameClean = new string(nameLower.Where(c => char.IsLetterOrDigit(c)).ToArray());
                                    // 名称匹配：原文包含 或 去符号后包含（英文简称）；空 qClean 时不参与，避免非空查询误匹配全部
                                    bool nameTextMatch = nameLower.Contains(q) || (!string.IsNullOrEmpty(qClean) && nameClean.Contains(qClean));
                                    // 分类文字匹配：如搜「视频」可筛出分类含「视频软件」的条目
                                    bool catTextMatch = !string.IsNullOrEmpty(q) && cat.ToLowerInvariant().Contains(q);
                                    bool nameOrCatMatch = nameTextMatch || catTextMatch;
                                    // 分类下拉筛选（与文字搜索取交集）
                                    bool catDropMatch = catSel == null || string.Equals(cat, catSel, StringComparison.OrdinalIgnoreCase);
                                    match = nameOrCatMatch && catDropMatch;
                                }
                                t.Item2.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                                if (match) visible++;
                            }
                            countLabel.Text = (string.IsNullOrEmpty(q) && catSel == null) ? $"共 {allSw.Count} 款" : $"筛选: {visible}/{allSw.Count} 款";
                        }
                        searchBox.TextChanged += (s, e) => ApplyFilter();
                        catList.SelectionChanged += (s, e) => ApplyFilter();
                        ApplyFilter();

                        // 安装选中（btnInstall 已在 DockPanel 中创建）
                        btnInstall.Click += (s, e) =>
                        {
                            var selected = rowItems.Where(t => t.Item1.IsChecked == true).Select(t => t.Item3).ToList();
                            if (selected.Count == 0) { log.AppendText("[!] 请先勾选要安装的软件\r\n"); return; }
                            // 先弹窗询问自定义安装路径
                            string customDir = null;
                            var dlg = new InstallPathDialog(this) { Owner = this };
                            if (dlg.ShowDialog() != true) return;  // 用户取消
                            if (!dlg.UseDefault) customDir = dlg.InstallPath;
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                foreach (var sw in selected)
                                {
                                    if (sw.Installed) { l("  [SKIP] " + sw.Name + " 已安装"); continue; }
                                    SoftwareInstall.Install(sw.Id, l, customDir);
                                }
                            }, $"已安装 {selected.Count(s => !s.Installed)}/{selected.Count} 款",
                            () => { RefreshAllRows(); });
                        };

                        // 卸载选中（btnUninstall 已在 DockPanel 中创建）
                        btnUninstall.Click += (s, e) =>
                        {
                            var selected = rowItems.Where(t => t.Item1.IsChecked == true).Select(t => t.Item3).ToList();
                            if (selected.Count == 0) { log.AppendText("[!] 请先勾选要卸载的软件\r\n"); return; }
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                foreach (var sw in selected)
                                {
                                    if (!sw.Installed) { l("  [SKIP] " + sw.Name + " 未安装"); continue; }
                                    SoftwareInstall.Uninstall(sw.Id, l);
                                }
                            }, $"已卸载 {selected.Count(s => s.Installed)}/{selected.Count} 款",
                            () =>
                            {
                                // 批量卸载完成后自动全量刷新列表（保留日志可见）
                                RefreshAllRows(() => log.AppendText("— 批量卸载结束，详情见上方日志 —\r\n"));
                            });
                        };

                        // 操作按钮（固定）
                        // ===== 操作按钮行：放在左上角（Header 下方、工具栏上方），按用户要求排序：搜索应用 → 刷新 → 安装到 → 清理缓存 =====
                        var actionBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
                        // 0) 搜索应用（本地 + winget 在线自动匹配，**结果直接内嵌在下方列表区**，不再弹窗）
                        // 搜索结果状态：null=本地列表；非空=显示搜索结果
                        var btnBackToLocal = Btn("🔙 返回本地列表", false, () =>
                        {
                            // 刷新整个页面恢复本地列表（最简单可靠）
                            SetPageContent(BuildCommonSoftware());
                        }, 140);
                        btnBackToLocal.Visibility = Visibility.Collapsed;  // 默认隐藏
                        actionBar.Children.Add(btnBackToLocal);
                        actionBar.Children.Add(Btn("🔍 搜索应用", false, () =>
                        {
                            // 优先读搜索框内容（用户体验好），搜索框为空再弹 InputBox
                            string input = searchBox?.Text?.Trim();
                            if (string.IsNullOrEmpty(input))
                            {
                                input = Interaction.InputBox(
                                    "搜索应用（本地 + winget 在线自动匹配）\n\n· 输入关键词：如 Chrome / QQ / WebP\n· 粘贴 Store 链接 / 9 位 ID：直接安装",
                                    "搜索 / 安装应用", "");
                            }
                            input = input?.Trim();
                            if (string.IsNullOrEmpty(input)) return;
                            if (pb.Visibility == Visibility.Visible) return;

                            // 模式 1：URL / StoreId 直装
                            string direct = AppxManager.ParseStoreIdFromInput(input);
                            if (direct != null)
                            {
                                log.AppendText("[OK] 识别 StoreId: " + direct + "，直接安装\r\n");
                                pb.Visibility = Visibility.Visible;
                            RunInBg(log, l => AppxManager.Install(direct, l), "安装启动",
                                () => RefreshAllRows(() => log.AppendText("— 安装启动完成，详情见上方日志 —\r\n")));
                                return;
                            }

                            // 模式 2：关键词合并搜索，结果**内嵌到**下方列表区（不再弹窗）
                            pb.Visibility = Visibility.Visible;
                            var rs = AppxManager.SearchMerged(input, l => log.AppendText(l + "\r\n"));
                            pb.Visibility = Visibility.Collapsed;
                            if (rs.Count == 0)
                            {
                                MessageBox.Show("没有找到匹配 '" + input + "' 的应用。\n\n提示：可在浏览器打开 https://apps.microsoft.com/store/search?q=" + input + " 找到应用后复制链接回来粘贴。",
                                    "搜索结果", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            // ★ 关键改造：rowsPanel 清空，替换成搜索结果行（3 列：名称+源标 / 安装）
                            rowsPanel.Children.Clear();
                            countLabel.Text = $"🔍 搜索结果：{rs.Count} 个（关键词：{input}）";
                            // 搜索结果专用 3 列 header（名称/来源/操作）
                            var hdrBorder = new Border { Background = _bgCard, BorderBrush = _panelBorder, BorderThickness = new Thickness(1, 1, 1, 0), Padding = new Thickness(0) };
                            var hdrGrid = new Grid();
                            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            string[] colNames = { "软件名称", "来源", "操作" };
                            for (int c = 0; c < colNames.Length; c++)
                            {
                                var hdr = new TextBlock { Text = colNames[c], FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13, Padding = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                                Grid.SetColumn(hdr, c);
                                hdrGrid.Children.Add(hdr);
                            }
                            hdrBorder.Child = hdrGrid;
                            rowsPanel.Children.Add(hdrBorder);

                            // 渲染搜索结果行（每行：名称 / 来源标签 / 一键安装按钮）
                            foreach (var r in rs)
                            {
                                rowsPanel.Children.Add(BuildSearchResultRow(r, pb, log, () => log.AppendText("— 安装启动完成，详情见上方日志 —\r\n")));
                            }
                            btnBackToLocal.Visibility = Visibility.Visible;  // 显示"返回本地列表"
                        }, 200));
                        // 1) 刷新状态（原地刷新，保留日志）
                        actionBar.Children.Add(Btn("🔄 刷新状态", false, () =>
                        {
                            RefreshAllRows(() => SetStatus("状态已刷新"));
                        }, 110));
                        // 2) 安装到（显示当前默认路径，点击修改）
                        var btnPath = new Button
                        {
                            Padding = new Thickness(10, 4, 10, 4),
                            Margin = new Thickness(8, 0, 0, 0),
                            Cursor = Cursors.Hand,
                            FontSize = 13,
                            BorderThickness = new Thickness(1)
                        };
                        void RefreshPathBtn()
                        {
                            string saved = null;
                            try
                            {
                                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\CpqSystemTool"))
                                    saved = k?.GetValue("InstallPath") as string;
                            }
                            catch { }
                            if (!string.IsNullOrEmpty(saved))
                            {
                                btnPath.Content = "📂 安装到: " + saved + "  ✎";
                                btnPath.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF7, 0xF4));
                                btnPath.BorderBrush = _accent;
                                btnPath.Foreground = _accent;
                                btnPath.FontWeight = FontWeights.SemiBold;
                                btnPath.ToolTip = "当前自定义安装路径：" + saved + "\n点击修改";
                            }
                            else
                            {
                                btnPath.Content = "📂 安装到: 默认路径  ✎";
                                btnPath.Background = _btnSecondaryBg;
                                btnPath.BorderBrush = _panelBorder;
                                btnPath.Foreground = _btnSecondaryFg;
                                btnPath.FontWeight = FontWeights.Normal;
                                btnPath.ToolTip = "当前使用各软件默认安装路径\n点击设置自定义路径";
                            }
                        }
                        RefreshPathBtn();
                        btnPath.Click += (s, e) =>
                        {
                            var dlg = new InstallPathDialog(this) { Owner = this };
                            if (dlg.ShowDialog() == true) RefreshPathBtn();
                        };
                        actionBar.Children.Add(btnPath);
                        // 3) 清理下载缓存
                        actionBar.Children.Add(Btn("🗑 清理下载缓存", false, () => RunInBg(log, SoftwareInstall.CleanupDownloads, "清理完成"), 110));

                        // 把 actionBar 嵌进 headerRow 的 col 1（右上角）
                        // 先把占位的 actionBarSlot 替换为真正的 actionBar
                        var parent = actionBarSlot.Parent as Grid;  // headerRow
                        if (parent != null)
                        {
                            parent.Children.Remove(actionBarSlot);
                            actionBar.HorizontalAlignment = HorizontalAlignment.Right;
                            Grid.SetColumn(actionBar, 1);
                            parent.Children.Add(actionBar);
                        }

                        listInner.Children.Add(rowsPanel);

                        // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放，规避 vp=0 跳过）
                        // listScroll 改由 root row2(Star) → listCard(Border Stretch) 约束，自动填满+滚动，无需手工 MaxHeight
                        BindRootHeightToViewport(root);
                    });
                }
                catch { /* 静默 */ }
            });

            return root;
        }

        // =====================================================================
        //  Module: 上帝模式 + 系统还原（合并）
        // =====================================================================


        /// <summary>搜索结果行（3 列：名称+ID / 来源标签 / 一键安装按钮）。注：此方法在 BuildCommonSoftware 内调用，
        /// 闭包捕获外层的 defaultRowBg/hoverBg/selectedRowBg/_bgCard/_panelBorder/_textMain/_textDim/_accent 等样式资源。</summary>
        private Border BuildSearchResultRow(StoreSearchResult r, ProgressBar pb, TextBox log, Action onDone)
        {
            var defaultRowBg = _bgCard;
            var hoverBg = _rowHover;  // 统一使用主题 hover 色（青绿色系）
            var selectedRowBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x4A, 0x7A));

            var rowBorder = new Border
            {
                Background = defaultRowBg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Padding = new Thickness(0, 0, 0, 0)
            };
            rowBorder.MouseEnter += (s, e) => { if (((Border)s).Background != selectedRowBg) ((Border)s).Background = hoverBg; };
            rowBorder.MouseLeave += (s, e) => ((Border)s).Background = defaultRowBg;

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });  // 名称
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 来源
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 操作

            // 名称 + ID（小灰字）—— 紧凑对齐按钮
            var namePanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var nameTb = new TextBlock { Text = r.Name, Foreground = _textMain, FontSize = 13, FontWeight = FontWeights.SemiBold };
            namePanel.Children.Add(nameTb);
            var idTb = new TextBlock { Text = r.Id, Foreground = _textDim, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
            namePanel.Children.Add(idTb);
            Grid.SetColumn(namePanel, 0);
            rowGrid.Children.Add(namePanel);

            // 来源标签（Catalog=蓝，msstore=绿，winget=紫）—— 紧凑
            var sourceColor = r.Source == "Catalog" ? Color.FromRgb(0x4A, 0x9E, 0xFF)
                : r.Source == "msstore" ? Color.FromRgb(0x4C, 0xAF, 0x50)
                : Color.FromRgb(0xAB, 0x47, 0xBC);
            var sourceBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(sourceColor.R, sourceColor.G, sourceColor.B)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 24
            };
            var sourceText = new TextBlock { Text = r.Source, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
            sourceBadge.Child = sourceText;
            Grid.SetColumn(sourceBadge, 1);
            rowGrid.Children.Add(sourceBadge);

            // 一键安装按钮（按 Source 分发）—— 关键：VerticalAlignment.Center + 固定 MinHeight/MaxHeight 防行高拉高
            Button installBtn = null;
            installBtn = Btn("⬇ 一键安装", true, () =>
            {
                if (pb.Visibility == Visibility.Visible) return;  // 防双击
                pb.Visibility = Visibility.Visible;
                // 安装完成后【原地】禁用本行按钮并标记已安装，保留搜索结果与日志，
                // 不重建整页（避免被踢回本地列表、丢失搜索上下文与日志）
                Action done = () =>
                {
                    installBtn.IsEnabled = false;
                    installBtn.Content = "已安装";
                    onDone?.Invoke();
                };
                if (r.Source == "winget")
                    RunInBg(log, l => AppxManager.InstallWingetId(r.Id, l), "安装启动", done);
                else
                    RunInBg(log, l => AppxManager.Install(r.Id, l), "安装启动", done);
            }, 110);
            installBtn.Margin = new Thickness(4, 0, 8, 0);
            installBtn.Padding = new Thickness(10, 2, 10, 2);  // 紧凑垂直
            installBtn.FontSize = 11;
            installBtn.VerticalAlignment = VerticalAlignment.Center;  // 关键：不占满行高
            installBtn.MinHeight = 26;  // 固定最小高度
            installBtn.MaxHeight = 26;  // 固定最大高度
            Grid.SetColumn(installBtn, 2);
            rowGrid.Children.Add(installBtn);

            rowBorder.Child = rowGrid;
            return rowBorder;
        }


        // =====================================================================
        //  Module: 系统信息（自动采集 + 复制/导出 TXT）
        // =====================================================================

        private string _lastSystemInfo = "";

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
                        _lastSystemInfo += "\r\n[OK] 已导出到: " + dlg.FileName;
                    }
                }, 150),
                Btn("🔄 重新采集", false, () =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(null, l =>
                {
                    var d = SystemInfo.CollectDual();
                    Dispatcher.Invoke(() =>
                    {
                        leftInfoBox.Clear(); leftInfoBox.AppendText(d.Left);
                        rightInfoBox.Clear(); rightInfoBox.AppendText(d.Right);
                        _lastSystemInfo = d.Left + "\r\n" + d.Right;
                    });
                }, "信息采集完成", () => pb.Visibility = Visibility.Collapsed);
            }));
            btnBar.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(btnBar, Dock.Top);
            inner.Children.Add(btnBar);
            DockPanel.SetDock(pb, Dock.Top);
            inner.Children.Add(pb);

            // 两列布局 - 左侧 TextBox + 右侧 TextBox，中间用 GridSplitter 推拉调整
            var twoColGrid = new Grid();
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });

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
                    Dispatcher.Invoke(() =>
                    {
                        leftInfoBox.AppendText(d.Left);
                        rightInfoBox.AppendText(d.Right);
                        _lastSystemInfo = d.Left + "\r\n" + d.Right;
                    });
                }, "信息采集完成", null);
            });

            // 动态 MaxHeight：最大化时 root 跟随视口拉伸
            // 稳健布局：root.MaxHeight 绑定到 ContentArea.ViewportHeight（自动跟随初始+缩放，规避 vp=0 跳过）
            BindRootHeightToViewport(root);

            return root;
        }

        // Issue 23: 系统信息双列 TextBox 字段引用
        private TextBox leftInfoBox;
        private TextBox rightInfoBox;

        // =====================================================================
        //  Module: 配置管理（显示默认路径 + 可修改）
        // =====================================================================

        private UIElement BuildConfig()
        {
            // Grid 布局：内容卡撑满视口，最大化时日志贴底、背景图放大、无死区
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 内容卡（Star：撑满视口剩余空间）
            int rootRow = 0;

            var headerTb = Header("配置管理", "导出 / 导入当前勾选与开关状态（JSON），支持自动保存与默认路径设置。");
            Grid.SetRow(headerTb, rootRow++);
            root.Children.Add(headerTb);

            var card = Card();
            // ★ 核心修复：Star 给 bgCard（背景图卡片）而非日志
            // 原因：_bgCard=Transparent，若日志行=Star则膨胀区域透明→透出六边形背景→看起来像"空白"
            // 新方案：bgCard=Star（撑大预览区，内容靠顶）+ 日志固定高度紧凑贴底
            var inner = new Grid { ClipToBounds = true };  // ★ 裁剪溢出：防止 Star 行缩小时残留渲染缓存
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [0] 路径卡片
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [1] 操作按钮栏
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // [2] ★Star 背景图卡片（吸收多余空间）
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [3] 进度条
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });          // [4] 日志固定60px（紧凑贴底）
            int r = 0;

            var log = MakeLogBox();
            // 日志容器：固定60px高 + 透明背景（与整体卡片风格一致，透出六边形窗口背景）
            // 固定高度保证不会因 Star 膨胀产生透明空白区
            var logClip = new Border
            {
                Child = log,
                ClipToBounds = true,
                Background = Brushes.Transparent,  // 透明：与 Card() 的 _bgCard 一致，保持设计统一
                CornerRadius = new CornerRadius(6),
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0),         // 与 Grid [4] 行高严格同步，避免顶部 Margin 导致底部被 ClipToBounds 裁掉圆角
                Height = 60  // 与 Grid [4] 行高同步，固定不膨胀
            };
            // log 本身不再设 Height/MaxHeight/MinHeight（由容器控制）
            log.ClearValue(HeightProperty);
            log.ClearValue(MaxHeightProperty);
            log.ClearValue(MinHeightProperty);
            log.BorderThickness = new Thickness(0);  // 边框改到外层容器
            log.Background = Brushes.Transparent;    // 背景改到外层容器
            var pb = MakeProgress();

            // 默认路径提示区
            var pathCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var pathSp = new StackPanel();
            pathSp.Children.Add(new TextBlock { Text = "📁 配置默认保存路径", FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            // 路径输入 + 浏览按钮 同一行
            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // 可编辑路径输入框
            var pathInput = new TextBox
            {
                Text = ConfigBackup.ConfigDir,
                FontSize = 12.5,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                Padding = new Thickness(8, 6, 8, 6),
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _accent,
                CaretBrush = _accent
            };
            Grid.SetColumn(pathInput, 0);
            pathRow.Children.Add(pathInput);
            // 浏览按钮
            var browseBtn = Btn("📂 浏览…", false, () =>
            {
                try
                {
                    using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        fbd.Description = "选择配置默认保存文件夹";
                        fbd.SelectedPath = ConfigBackup.ConfigDir;
                        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            pathInput.Text = fbd.SelectedPath;
                            log.AppendText("[OK] 已选择新路径: " + fbd.SelectedPath + "\r\n");
                        }
                    }
                }
                catch (Exception ex) { log.AppendText("[!] 浏览失败: " + ex.Message + "\r\n"); }
            }, 90);
            browseBtn.Margin = new Thickness(6, 0, 0, 0);
            browseBtn.Padding = new Thickness(10, 5, 10, 5);
            browseBtn.FontSize = 11;
            Grid.SetColumn(browseBtn, 1);
            pathRow.Children.Add(browseBtn);
            // 应用路径按钮紧跟浏览后面
            var applyPathBtn = Btn("✅ 应用路径", true, () =>
            {
                string newPath = pathInput.Text.Trim();
                if (string.IsNullOrEmpty(newPath)) { log.AppendText("[!] 路径不能为空\r\n"); return; }
                try
                {
                    Directory.CreateDirectory(newPath);
                    ConfigBackup.ConfigDir = newPath;
                    log.AppendText("[OK] 配置路径已更改为: " + newPath + "\r\n");
                    SetPageContent(BuildConfig());
                }
                catch (Exception ex) { log.AppendText("[!] 无效路径: " + ex.Message + "\r\n"); }
            }, 90);
            applyPathBtn.Margin = new Thickness(6, 0, 0, 0);
            applyPathBtn.Padding = new Thickness(10, 5, 10, 5);
            applyPathBtn.FontSize = 11;
            Grid.SetColumn(applyPathBtn, 2);
            pathRow.Children.Add(applyPathBtn);
            pathSp.Children.Add(pathRow);
            pathSp.Children.Add(new TextBlock { Text = "提示：自动保存功能会将配置保存到上述路径下的 autosave.json 文件。可直接编辑路径，或点「📂 浏览…」选择。修改后点击「应用路径」生效。", Foreground = _textDim, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            pathCard.Child = pathSp;
            Grid.SetRow(pathCard, r++);
            inner.Children.Add(pathCard);

            // ========== 导出/导入操作栏（上移到背景图前面，更易操作） ==========
            var wp = MakeBtnRow(
                Btn("📥 导出配置...", true, () =>
                {
                    var defaultName = $"系统清理与优化配置_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = defaultName, InitialDirectory = ConfigBackup.ConfigDir };
                    if (dlg.ShowDialog() == true)
                    {
                        var cfg = CollectConfig();
                        ConfigBackup.Save(dlg.FileName, cfg, s => log.AppendText(s + "\r\n"));
                    }
                }),
                Btn("📤 导入配置...", false, () =>
                {
                    var dlg = new OpenFileDialog { Filter = "JSON|*.json", InitialDirectory = ConfigBackup.ConfigDir };
                    if (dlg.ShowDialog() == true)
                    {
                        var cfg = ConfigBackup.Load(dlg.FileName, s => log.AppendText(s + "\r\n"));
                        ApplyConfig(cfg, log);
                    }
                }),
                Btn("💾 自动保存当前配置", false, () =>
                {
                    var cfg = CollectConfig();
                    ConfigBackup.AutoSave(cfg, s => log.AppendText(s + "\r\n"));
                    log.AppendText("[OK] 已保存到: " + Path.Combine(ConfigBackup.ConfigDir, "autosave.json") + "\r\n");
                }),
                Btn("📋 列出已存配置", false, () =>
                {
                    var configs = ConfigBackup.ListConfigs();
                    log.AppendText("默认配置目录: " + ConfigBackup.ConfigDir + "\r\n");
                    log.AppendText("已存配置:\r\n" + (configs.Count > 0 ? string.Join("\r\n", configs) : "(无)") + "\r\n");
                }),
                Btn("📦 导出源码", false, () =>
                {
                    try
                    {
                        var dlg = new System.Windows.Forms.FolderBrowserDialog();
                        dlg.Description = "选择保存源码的目录";
                        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            string target = dlg.SelectedPath;
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            string resName = "CpqSystemTool.src.zip";
                            using (var stream = asm.GetManifestResourceStream(resName))
                            {
                                if (stream == null) { log.AppendText("[!] 未找到嵌入的源码包 src.zip\r\n"); return; }
                                string zipPath = Path.Combine(target, "src.zip");
                                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                                {
                                    stream.CopyTo(fs);
                                }
                                string extractDir = Path.Combine(target, "系统清理与优化工具_源码");
                                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                                Directory.CreateDirectory(extractDir);
                                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
                                File.Delete(zipPath);
                            }
                            log.AppendText("[OK] 源码已导出到: " + target + "\\系统清理与优化工具_源码\r\n");
                            System.Windows.MessageBox.Show(this, "源码已导出到：\n" + target + "\\系统清理与优化工具_源码\n\n包含所有 .cs/.xaml/.csproj 文件。", "导出成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex) { log.AppendText("[!] 导出源码失败: " + ex.Message + "\r\n"); }
                })
            );
            wp.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(wp, r++);
            inner.Children.Add(wp);

            // ========== 背景图设置卡片（预览加大 + 透明度并排） ==========
            var bgCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true // ★ 防止最大化时子内容溢出 + 缩小时残留大尺寸渲染缓存
            };
            // ★ bgSp 改为 Grid：标题 Auto + 预览区 Star（自动填充剩余空间，默认/最大化都利用完）
            var bgSp = new Grid();
            bgSp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // [0] 标题行（固定）
            bgSp.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // [1] ★预览区（填充剩余空间）
            // 前向声明（标题行按钮闭包会引用）
            System.Windows.Controls.Slider darkOpSlider = null, lightOpSlider = null;
            Action refreshThumbs = null;

            // 标题行：标题 + 提示（左） | 恢复默认背景按钮（右）
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var titleLeft = new StackPanel { Orientation = Orientation.Horizontal };
            titleLeft.Children.Add(new TextBlock { Text = "🎨 自定义背景图", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            titleLeft.Children.Add(new TextBlock { Text = "  提示：支持 PNG/JPG/BMP/GIF/WebP；图片会被引用（不嵌入 exe），请勿删除原文件。切换主题后新背景自动生效。", Foreground = _textDim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            DockPanel.SetDock(titleLeft, Dock.Left);
            titleRow.Children.Add(titleLeft);
            // 恢复默认背景按钮（右上角）
            var resetBgBtn = Btn("🔄 恢复默认背景", false, () =>
            {
                _customBgDarkPath = "";
                _customBgLightPath = "";
                _customBgDarkOpacity = 0.55;
                _customBgLightOpacity = 1.0;
                darkOpSlider.Value = 0.55;
                lightOpSlider.Value = 1.0;
                SaveBackgroundSettings();
                log.AppendText("[OK] 已恢复为内置默认背景\r\n");
                refreshThumbs();
                ApplyShellColors();
            }, 130);
            resetBgBtn.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(resetBgBtn, Dock.Right);
            titleRow.Children.Add(resetBgBtn);
            bgSp.Children.Add(titleRow);
            Grid.SetRow(titleRow, 0);

            // ===== 两列布局：左深色 | 右浅色，Star 列随容器自动均分宽度 =====

            var bgTwoCol = new Grid { Margin = new Thickness(12, 0, 12, 0) };  // 拉伸全宽（紧凑边距）
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 深色列 ★
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 间距（固定）
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 浅色列 ★

            // ── 左列：深色模式（按钮行 / 预览图 Star 填充 / 透明度调整 Auto） ──
            var darkCol = new Grid();
            darkCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [0] 选择背景按钮（图片上方，居中）
            darkCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // [1] 预览图占满剩余高度，最大化时填满，默认时自动让出滑块
            darkCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [2] 透明度调整

            // 深色预览图：Viewbox Uniform 在 Star 行内完整显示，容器随可用空间拉伸
            // 不画自身边框，避免与外层 bgCard 边框叠加形成多余框线
            var darkThumb = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var darkThumbImg = new Image { IsHitTestVisible = false };
            var darkViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = darkThumbImg };
            darkThumb.Child = darkViewbox;

            // 选择背景按钮：放在图片上方一行，与图片居中对齐
            var darkBtn = Btn("🌙 选择深色背景", false, () =>
            {
                // png/jpg/bmp/gif/webp 全支持（webp 走 System.Drawing 转码通道）
                var dlg = new OpenFileDialog
                {
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                    Title = "选择深色模式背景图"
                };
                if (dlg.ShowDialog() == true)
                {
                    var testImg = MainWindow.TryLoadImagePublic(dlg.FileName);
                    if (testImg == null)
                    {
                        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        if (ext == ".webp" && !MainWindow.IsWebpCodecAvailable())
                        {
                            // 自动后台安装 WebP 解码器，装完重试
                            log.AppendText("[*] 系统缺少 WebP 解码器，正在后台自动安装（约 1 分钟）...\r\n");
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                if (MainWindow.InstallWebpExtension(l))
                                {
                                    var retry = MainWindow.TryLoadImagePublic(dlg.FileName);
                                    if (retry != null)
                                    {
                                        _customBgDarkPath = dlg.FileName;
                                        SaveBackgroundSettings();
                                        l("[OK] WebP 解码器已安装，背景已自动应用");
                                        Dispatcher.Invoke(() => { refreshThumbs(); ApplyShellColors(); });
                                        return;
                                    }
                                    l("[FAIL] 解码器已安装但该图片仍无法加载（文件可能损坏）");
                                }
                                // 未装成功或仍失败——给手动指引
                                l("提示：可右键 webp → 打开方式 → 画图 → 另存为 PNG 后再选。");
                            }, "WebP 扩展安装中", () => { pb.Visibility = Visibility.Collapsed; });
                            return;
                        }
                        log.AppendText("[FAIL] 图片加载失败：" + Path.GetFileName(dlg.FileName) + "\r\n");
                        string hint = ext == ".webp"
                            ? "\n\n当前是 webp 格式。系统未安装 WebP 解码器。\n\n最快方案：右键 webp → 画图 → 另存为 PNG/JPG。"
                            : "\n\n请确认图片文件有效（png/jpg/bmp/gif 或 webp）。";
                        System.Windows.MessageBox.Show(this,
                            "图片加载失败。\n\n" + hint,
                            "背景图加载失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _customBgDarkPath = dlg.FileName;
                    SaveBackgroundSettings();
                    log.AppendText("[OK] 深色背景已设置: " + Path.GetFileName(dlg.FileName) + "\r\n");
                    refreshThumbs();
                    ApplyShellColors();
                }
            }, 110);
            darkBtn.FontSize = 11;
            darkBtn.Padding = new Thickness(6, 3, 6, 3);
            darkBtn.Margin = new Thickness(0);
            var darkBtnBg = _btnSecondaryBg.Clone(); darkBtnBg.Opacity = 0.88; darkBtn.Background = darkBtnBg;

            darkBtn.HorizontalAlignment = HorizontalAlignment.Center;
            darkBtn.Margin = new Thickness(0, 0, 0, 6);
            darkCol.Children.Add(darkBtn);
            Grid.SetRow(darkBtn, 0);

            darkCol.Children.Add(darkThumb);
            Grid.SetRow(darkThumb, 1);

            // 透明度调整（滑块加长，视觉更舒展）
            var darkOpLbl = new TextBlock { Text = "透明度:", FontSize = 11.5, Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            darkOpSlider = new System.Windows.Controls.Slider
            { Minimum = 0.1, Maximum = 1.0, Value = _customBgDarkOpacity, TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            var darkOpVal = new TextBlock { Text = _customBgDarkOpacity.ToString("P0"), FontSize = 11.5, Foreground = _accent, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34, Margin = new Thickness(4, 0, 0, 0) };
            darkOpSlider.ValueChanged += (s, e) =>
            {
                _customBgDarkOpacity = darkOpSlider.Value;
                darkOpVal.Text = _customBgDarkOpacity.ToString("P0");
                SaveBackgroundSettings();
                darkThumbImg.Opacity = _customBgDarkOpacity;
                BgImage.Opacity = _customBgDarkOpacity;
            };
            var darkCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            darkCtrlRow.Children.Add(darkOpLbl); darkCtrlRow.Children.Add(darkOpSlider); darkCtrlRow.Children.Add(darkOpVal);
            darkCol.Children.Add(darkCtrlRow);
            Grid.SetRow(darkCtrlRow, 2);

            Grid.SetColumn(darkCol, 0);
            bgTwoCol.Children.Add(darkCol);

            // ── 右列：浅色模式（按钮行 / 预览图 Star 填充 / 透明度调整 Auto） ──
            var lightCol = new Grid();
            lightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [0] 选择背景按钮（图片上方，居中）
            lightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // [1] 预览图占满剩余高度，最大化时填满，默认时自动让出滑块
            lightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [2] 透明度调整

            // 浅色预览图：Viewbox Uniform 在 Star 行内完整显示，容器随可用空间拉伸
            // 不画自身边框，避免与外层 bgCard 边框叠加形成多余框线
            var lightThumb = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var lightThumbImg = new Image { IsHitTestVisible = false };
            var lightViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = lightThumbImg };
            lightThumb.Child = lightViewbox;

            // 选择背景按钮：放在图片上方一行，与图片居中对齐
            var lightBtn = Btn("☀️ 选择浅色背景", false, () =>
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                    Title = "选择浅色模式背景图"
                };
                if (dlg.ShowDialog() == true)
                {
                    var testImg = MainWindow.TryLoadImagePublic(dlg.FileName);
                    if (testImg == null)
                    {
                        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        if (ext == ".webp" && !MainWindow.IsWebpCodecAvailable())
                        {
                            log.AppendText("[*] 系统缺少 WebP 解码器，正在后台自动安装（约 1 分钟）...\r\n");
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                if (MainWindow.InstallWebpExtension(l))
                                {
                                    var retry = MainWindow.TryLoadImagePublic(dlg.FileName);
                                    if (retry != null)
                                    {
                                        _customBgLightPath = dlg.FileName;
                                        SaveBackgroundSettings();
                                        l("[OK] WebP 解码器已安装，背景已自动应用");
                                        Dispatcher.Invoke(() => { refreshThumbs(); ApplyShellColors(); });
                                        return;
                                    }
                                    l("[FAIL] 解码器已安装但该图片仍无法加载（文件可能损坏）");
                                }
                                l("提示：可右键 webp → 打开方式 → 画图 → 另存为 PNG 后再选。");
                            }, "WebP 扩展安装中", () => { pb.Visibility = Visibility.Collapsed; });
                            return;
                        }
                        log.AppendText("[FAIL] 图片加载失败：" + Path.GetFileName(dlg.FileName) + "\r\n");
                        string hint = ext == ".webp"
                            ? "\n\n当前是 webp 格式。系统未安装 WebP 解码器。\n\n最快方案：右键 webp → 画图 → 另存为 PNG/JPG。"
                            : "\n\n请确认图片文件有效（png/jpg/bmp/gif 或 webp）。";
                        System.Windows.MessageBox.Show(this,
                            "图片加载失败。\n\n" + hint,
                            "背景图加载失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _customBgLightPath = dlg.FileName;
                    SaveBackgroundSettings();
                    log.AppendText("[OK] 浅色背景已设置: " + Path.GetFileName(dlg.FileName) + "\r\n");
                    refreshThumbs();
                    ApplyShellColors();
                }
            }, 110);
            lightBtn.FontSize = 11;
            lightBtn.Padding = new Thickness(6, 3, 6, 3);
            lightBtn.Margin = new Thickness(0);
            var lightBtnBg = _btnSecondaryBg.Clone(); lightBtnBg.Opacity = 0.88; lightBtn.Background = lightBtnBg;

            lightBtn.HorizontalAlignment = HorizontalAlignment.Center;
            lightBtn.Margin = new Thickness(0, 0, 0, 6);
            lightCol.Children.Add(lightBtn);
            Grid.SetRow(lightBtn, 0);

            lightCol.Children.Add(lightThumb);
            Grid.SetRow(lightThumb, 1);

            // 透明度调整（滑块加长，视觉更舒展）
            var lightOpLbl = new TextBlock { Text = "透明度:", FontSize = 11.5, Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            lightOpSlider = new System.Windows.Controls.Slider
            { Minimum = 0.1, Maximum = 1.0, Value = _customBgLightOpacity, TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            var lightOpVal = new TextBlock { Text = _customBgLightOpacity.ToString("P0"), FontSize = 11.5, Foreground = _accent, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34, Margin = new Thickness(4, 0, 0, 0) };
            lightOpSlider.ValueChanged += (s, e) =>
            {
                _customBgLightOpacity = lightOpSlider.Value;
                lightOpVal.Text = _customBgLightOpacity.ToString("P0");
                SaveBackgroundSettings();
                lightThumbImg.Opacity = _customBgLightOpacity;
                if (!_isDarkMode) BgImage.Opacity = _customBgLightOpacity;
            };
            var lightCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            lightCtrlRow.Children.Add(lightOpLbl); lightCtrlRow.Children.Add(lightOpSlider); lightCtrlRow.Children.Add(lightOpVal);
            lightCol.Children.Add(lightCtrlRow);
            Grid.SetRow(lightCtrlRow, 2);

            Grid.SetColumn(lightCol, 2);
            bgTwoCol.Children.Add(lightCol);

            bgSp.Children.Add(bgTwoCol);
            Grid.SetRow(bgTwoCol, 1);

            // 刷新缩略图辅助方法（闭包捕获）
            refreshThumbs = () =>
            {
                var dImg = TryLoadImage(_customBgDarkPath);
                if (dImg == null)
                {
                    try { dImg = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/background.png", UriKind.Absolute)); dImg.Freeze(); } catch { }
                }
                darkThumbImg.Source = dImg;
                darkThumbImg.Opacity = _customBgDarkOpacity;

                var lImg = TryLoadImage(_customBgLightPath);
                if (lImg == null)
                {
                    try { lImg = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/background-light.png", UriKind.Absolute)); lImg.Freeze(); } catch { }
                }
                lightThumbImg.Source = lImg;
                lightThumbImg.Opacity = _customBgLightOpacity;
            };

            bgCard.Child = bgSp;
            Grid.SetRow(bgCard, r++);
            inner.Children.Add(bgCard);

            // 初始化缩略图
            refreshThumbs();
            Grid.SetRow(pb, r++);
            inner.Children.Add(pb);
            // 日志：包在固定高度容器中，物理截断，绝不膨胀
            Grid.SetRow(logClip, r++);  // row 4 (最后一行)
            inner.Children.Add(logClip);
            card.Child = inner;
            Grid.SetRow(card, rootRow++);  // Star 行：撑满剩余空间
            root.Children.Add(card);

            // 打开时默认列出配置目录下的所有 *.json → 写入日志
            AutoLoad(() =>
            {
                try
                {
                    var configs = ConfigBackup.ListConfigs();
                    string listText = "默认配置目录: " + ConfigBackup.ConfigDir + "\r\n" +
                        "已存配置 (" + configs.Count + " 个):\r\n" +
                        (configs.Count > 0 ? string.Join("\r\n", configs.Select(c => "  • " + c)) : "  (无)");
                    Dispatcher.Invoke(() => log.AppendText(listText + "\r\n"));
                }
                catch { }
            });

            // 稳健高度约束：绑定到 ContentArea.ActualHeight（只读 DP，自动跟随首帧+缩放，
            // 彻底消除"首次打开未填充 / 最大化后恢复默认尺寸内容漂移"两类时序 bug）
            BindRootHeightToViewport(root);

            return root;
        }

        // ---------- Config helpers ----------

        private ToolConfig CollectConfig()
        {
            var cfg = new ToolConfig();
            foreach (var t in Tweaks.All)
            {
                if (t.IsThreeState)
                {
                    TweakState st; try { st = t.GetState3(); } catch { st = TweakState.Default; }
                    cfg.TweakStates[t.Id] = st.ToString(); // "On"/"Off"/"Default"
                }
                else if (t.State()) cfg.EnabledTweaks.Add(t.Id);
            }
            return cfg;
        }

        private void ApplyConfig(ToolConfig cfg, TextBox log)
        {
            foreach (var t in Tweaks.All)
            {
                if (t.IsThreeState)
                {
                    // 三态项：仅当配置显式记录时才应用；缺省则不改动（保留系统现状）
                    if (cfg.TweakStates.TryGetValue(t.Id, out var sv) && Enum.TryParse<TweakState>(sv, out var st))
                    {
                        try { t.Apply3(st, s => log.AppendText(s + "\r\n")); }
                        catch (Exception ex) { log.AppendText("[!] " + t.Id + ": " + ex.Message + "\r\n"); }
                    }
                }
                else
                {
                    bool want = cfg.EnabledTweaks.Contains(t.Id);
                    bool has = t.State();
                    if (want && !has) t.Enable(s => log.AppendText(s + "\r\n"));
                    else if (!want && has) t.Disable(s => log.AppendText(s + "\r\n"));
                }
            }
            log.AppendText("[OK] 已按配置应用优化项\r\n");
        }
    }

    /// <summary>winget search 结果列表窗（双击行或点「安装选中」返回选中项，含 Source 用于分发安装通道）。</summary>
    public class StoreSearchWindow : Window
    {
        public StoreSearchResult Selected { get; private set; }
        public StoreSearchWindow(System.Collections.Generic.List<StoreSearchResult> results)
        {
            Title = "搜索结果 - 双击行安装（" + results.Count + " 个）";
            Width = 820;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "来源说明：Catalog=本地精选（走三通道）· msstore=Microsoft Store 应用（走三通道）· winget=社区源应用（winget 直接装）。双击行安装。",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                Margin = new Thickness(10, 8, 10, 4),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(hint, 0);
            grid.Children.Add(hint);

            var dg = new DataGrid
            {
                ItemsSource = results,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                RowBackground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(0),
                RowHeight = 28,
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 13
            };
            dg.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = new DataGridLength(200) });
            dg.Columns.Add(new DataGridTextColumn { Header = "版本", Binding = new Binding("Version"), Width = new DataGridLength(110) });
            dg.Columns.Add(new DataGridTextColumn { Header = "来源", Binding = new Binding("Source"), Width = new DataGridLength(90) });
            dg.MouseDoubleClick += (s, e) => SelectAndClose(dg);
            Grid.SetRow(dg, 1);
            grid.Children.Add(dg);

            var btnBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 8, 10, 10) };
            var btnInstall = new Button { Content = "安装选中", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
            btnInstall.Click += (s, e) => SelectAndClose(dg);
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(14, 6, 14, 6) };
            btnClose.Click += (s, e) => { DialogResult = false; Close(); };
            btnBar.Children.Add(btnInstall);
            btnBar.Children.Add(btnClose);
            Grid.SetRow(btnBar, 2);
            grid.Children.Add(btnBar);

            Content = grid;
        }

        private void SelectAndClose(DataGrid dg)
        {
            if (dg.SelectedItem is StoreSearchResult r)
            {
                Selected = r;
                DialogResult = true;
                Close();
            }
        }

    }
}

/// <summary>bool? → Brush 转换器：true 用 TrueBrush，其余用 FalseBrush。用于 CheckBox.IsChecked 绑定到名称颜色。</summary>
internal class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; }
    public Brush FalseBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
