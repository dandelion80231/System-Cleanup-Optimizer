using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: 系统优化（含预设按钮）
        // =====================================================================

        // 系统优化页缓存（降级方案：面板/页面实例缓存）：首次构建完成后缓存整页，
        // 再次进入复用已构建面板，仅复位动态状态（勾选/预设高亮/展开折叠/输出/计数），
        // 避免每次导航重建 116 项 × 多控件。主题一致性由 _tweaksCacheDark 保证。
        private UIElement _cachedTweaksPage;
        private string _tweaksCacheKey;
        private bool _tweaksCacheDark;
        private Action _tweaksRefresh;

        private UIElement BuildTweaks()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅复位动态状态
            if (_cachedTweaksPage != null && _tweaksCacheDark == buildDark && _tweaksCacheKey != null)
            {
                _tweaksRefresh?.Invoke();
                return _cachedTweaksPage;
            }
            // 主题/失效 → 丢弃旧缓存，走完整重建
            _cachedTweaksPage = null;
            _tweaksRefresh = null;
            _tweaksCacheKey = null;

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
            // 记录各分组 Expander，供缓存刷新时复位展开状态
            var groupExpanders = new List<Expander>();
            foreach (var g in groups)
            {
                var exHeader = new TextBlock { Text = g.Key, FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13.5 };
                var content = new StackPanel { Margin = new Thickness(20, 4, 0, 4) };
                var expander = MakeLineArrowExpander(exHeader, content, true, new Thickness(0, 4, 0, 4));
                treePanel.Children.Add(expander);
                groupExpanders.Add(expander);
                groupContents[g.Key] = content;
            }

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
                        // 修复：原写法 chk.IsChecked = !chk.IsChecked，而 !null 仍是 null，
                        // 三态项处于「系统默认」(null) 时点名字毫无反应，永远切不出去。
                        // 三态循环顺序与 WPF CheckBox 原生点击保持一致（false → true → null → false），
                        // 避免"点复选框"和"点名字"两种操作走出不同顺序让用户困惑。
                        if (chk.IsThreeState)
                            chk.IsChecked = chk.IsChecked == null ? false : (chk.IsChecked == true ? (bool?)null : true);
                        else
                            chk.IsChecked = chk.IsChecked != true;
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
                    // P2 常驻提示：关闭系统还原后危险操作将失去还原点兜底（提示位于该项下方，保证用户能看到）
                    if (t.Id == "system_restore")
                    {
                        content.Children.Add(new TextBlock
                        {
                            Text = "⚠ 关闭系统还原后，危险操作将失去还原点兜底",
                            Foreground = _textDim,
                            FontSize = 11,
                            Margin = new Thickness(44, 0, 0, 6),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
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
            // 封装为可复用委托：首次构建与缓存命中进页时均调用，保证每次进页都展示真实优化状态（与旧版重建行为一致）
            Action reloadTweaksStates = () =>
            {
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
                    try { Dispatcher.BeginInvoke(new Action(() =>
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
                    }), System.Windows.Threading.DispatcherPriority.Background); } catch { /* 窗口已关闭，忽略 */ }
                }

                try { Dispatcher.BeginInvoke(new Action(() =>
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
                }), System.Windows.Threading.DispatcherPriority.Background); } catch { /* 窗口已关闭，忽略 */ }
            });
            };
            reloadTweaksStates();

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

            // 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅复位动态状态
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _cachedTweaksPage = root;
                _tweaksCacheKey = "tweaks";   // 列表静态定义，常量键即可；主题一致性由 _tweaksCacheDark 保证
                _tweaksCacheDark = buildDark;
                _tweaksRefresh = () =>
                {
                    // 复位动态状态（与旧版每次新建页面行为一致）：
                    // 勾选复位为默认（二态未勾选/三态系统默认）、文本配色复位、预设按钮高亮复位（仅「基本优化」保持高亮）、
                    // 分组折叠复位为展开、输出/状态计数复位、Touched 集合清空、右侧已选列表复位，随后后台重读真实优化状态
                    foreach (var kv in checkBoxes)
                    {
                        var t = Tweaks.All.FirstOrDefault(x => x.Id == kv.Key);
                        kv.Value.IsChecked = t != null && t.IsThreeState ? (bool?)null : false;
                    }
                    SyncAllTweakColors();
                    foreach (var ex in groupExpanders) ex.IsExpanded = true;
                    highlightPreset(btnBasic);
                    TweaksOutputLine.Text = "";
                    TweaksTouched = new HashSet<string>(StringComparer.Ordinal);
                    UpdateSelectedPanel();
                    _tweaksStatusBase = "";
                    RefreshTweaksStatus();
                    reloadTweaksStates();
                };
            }

            return root;
        }

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
            // P1 确认：应用前弹出确认，避免误点导致批量改动系统设置（此前无任何确认对话框）
            var confirm = System.Windows.MessageBox.Show(this,
                "确定要应用这些优化吗？\n\n勾选=启用优化、取消勾选=恢复系统默认。部分优化项（如关闭系统还原、高风险项）可能影响系统稳定或失去还原点兜底。",
                "确认应用优化", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
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
                string configFolder = AppPaths.ConfigDir;
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
                string configFolder = AppPaths.ConfigDir;
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
            // P1 防重入：全局互斥，防止连点或跨模块并发（清理/优化同一时间只允许一个耗时操作）
            if (!OperationLock.TryEnter("优化", out string busyBy))
            {
                System.Windows.MessageBox.Show(this, "已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            // 1) 同步勾选框（仅纳入范围且非"系统默认"的项；Default 留待刷新回写）
            foreach (var kv in desired)
                if (TweaksCheckBoxes != null && TweaksCheckBoxes.TryGetValue(kv.Key, out var cb) && kv.Value != null)
                    cb.IsChecked = kv.Value == TweakState.On;
            UpdateSelectedPanel();

            // 进度日志收集（后台线程写本地缓冲，避免跨线程写 UI）
            var sb = new System.Text.StringBuilder();
            object lk = new object();
            Action<string> bgLog = s => { lock (lk) sb.AppendLine(s); };

            // 2) 后台线程执行 + 不重建页面（避免闪烁）
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
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
                    try { Dispatcher.Invoke(() =>
                    {
                        // 应用完成后 116 项勾选/实际状态可能已变化 → 整页缓存失效，下次进页重建以读取真实状态
                        _cachedTweaksPage = null;
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
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }
                finally
                {
                    OperationLock.Exit();   // P1：成功/失败/异常均释放全局互斥，避免锁泄漏
                }
            });
        }

        /// <summary>三态 → 勾选框：On=true / Off=false / Default=null(不确定)。</summary>
        private static bool? ToCheckBox(TweakState st) => st == TweakState.On ? true : st == TweakState.Off ? false : (bool?)null;
    }
}
