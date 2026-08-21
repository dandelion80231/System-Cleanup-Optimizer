using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 内存工具页（镜像 RAMMap 只读视图 + 可选优化）。
    /// 导航项 Key="memory"，挂在「系统工具」(systools) 之下。
    ///   A 仪表盘：总/可用物理、内存占用%、已提交/上限、内核分页/非分页池。
    ///   B 拆解：Active/Standby/Modified/Free+Zero 占比条 + 图例 + 提交/缓存/池明细 + 进程工作集 Top 10。
    ///   C 优化（默认收起、中风险、仅管理员）：清 Standby 列表 / 空工作集。
    /// </summary>
    public partial class MainWindow
    {
        // 占比条 / 图例固定配色（与主题无关的固定语义色，避免对比度问题）。
        private static readonly SolidColorBrush MemBrushInUse = new SolidColorBrush(Color.FromRgb(0x2D, 0xB4, 0xC0));   // 青（使用中）
        private static readonly SolidColorBrush MemBrushStandby = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));  // 橙（备用）
        private static readonly SolidColorBrush MemBrushModified = new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6)); // 紫（已修改）
        private static readonly SolidColorBrush MemBrushFree = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));     // 绿（空闲+零页）
        private static readonly SolidColorBrush MemBrushUnknown = new SolidColorBrush(Color.FromRgb(0x9E, 0xA3, 0xA8));   // 灰（数据不可用占位）

        // ---- 整页缓存（同 M1 常用软件页模式）：首次构建完成后缓存整页外壳，二次进页复用并仅重跑分析刷新数据 ----
        private UIElement _cachedMemoryPage;
        private string _memoryCacheKey;
        private bool _memoryCacheDark;
        private Action _memoryRefresh;

        private UIElement BuildMemory()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）。
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面外壳，仅复位动态状态并重跑内存分析（数据保持最新）。
            if (_cachedMemoryPage != null && _memoryCacheDark == buildDark && _memoryCacheKey != null)
            {
                _memoryRefresh?.Invoke();
                return _cachedMemoryPage;
            }
            // 主题变化 → 丢弃旧缓存，走完整重建。
            _cachedMemoryPage = null;
            _memoryRefresh = null;
            _memoryCacheKey = null;

            var root = new StackPanel();
            root.Children.Add(Header("内存工具", "只读分析物理内存使用（使用中 / 备用 / 已修改 / 空闲），镜像 RAMMap 视图；并提供可选的内存优化（清备用列表 / 空工作集）。优化项为中风险且需管理员，默认收起。"));

            var pb = MakeProgress();

            // ===================== 卡片 A：内存总览 =====================
            var aCard = Card();
            var aInner = (StackPanel)aCard.Child;
            aInner.Children.Add(SectionTitle("📊 内存总览（A）"));
            aInner.Children.Add(new TextBlock { Text = "数据来源：GlobalMemoryStatusEx + GetPerformanceInfo（均为 Windows 文档化 API）。", FontSize = 10.5, Foreground = _textDim, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            // 2 行 × 3 列网格，占满页面宽度
            var tilesGrid = new Grid();
            for (int c = 0; c < 3; c++)
                tilesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tilesGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            tilesGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            var tTotal = MakeStatTile("总物理内存");
            var tLoad = MakeStatTile("内存占用");
            var tAvail = MakeStatTile("可用物理");
            var tCommit = MakeStatTile("已提交 / 上限");
            var tPaged = MakeStatTile("内核分页池");
            var tNonpaged = MakeStatTile("内核非分页池");
            Action<(Border tile, TextBlock value), int, int> placeTile = (t, row, col) =>
            {
                Grid.SetRow(t.tile, row);
                Grid.SetColumn(t.tile, col);
                tilesGrid.Children.Add(t.tile);
            };
            placeTile(tTotal, 0, 0);
            placeTile(tLoad, 0, 1);
            placeTile(tAvail, 0, 2);
            placeTile(tCommit, 1, 0);
            placeTile(tPaged, 1, 1);
            placeTile(tNonpaged, 1, 2);
            aInner.Children.Add(tilesGrid);

            var analyzeBtn = Btn("🔄 重新分析", true, null, 140);
            var aBtnRow = MakeBtnRow(analyzeBtn);
            aBtnRow.Margin = new Thickness(0, 10, 0, 0);
            aInner.Children.Add(aBtnRow);
            root.Children.Add(aCard);

            // ===================== 卡片 B：内存使用拆解 =====================
            var bCard = Card();
            var bInner = (StackPanel)bCard.Child;
            bInner.Children.Add(SectionTitle("🧩 内存使用拆解（B）"));
            bInner.Children.Add(new TextBlock { Text = "数据来源：WMI Win32_PerfFormattedData_PerfOS_Memory（文档化计数器）。「空闲+零页」因 WMI 不区分二者而合并展示。", FontSize = 10.5, Foreground = _textDim, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            // 占比条
            var bBar = new Grid { Height = 24, Margin = new Thickness(0, 4, 0, 8) };
            for (int i = 0; i < 4; i++)
                bBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var segBrushes = new[] { MemBrushInUse, MemBrushStandby, MemBrushModified, MemBrushFree };
            var segments = new Border[4];
            for (int i = 0; i < 4; i++)
            {
                var seg = new Border { Background = segBrushes[i] };
                Grid.SetColumn(seg, i);
                bBar.Children.Add(seg);
                segments[i] = seg;
            }
            bInner.Children.Add(bBar);

            // 图例
            var legendPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            bInner.Children.Add(legendPanel);

            // 明细（可用 / 系统缓存 / 已提交 / 提交上限 / 分页池 / 非分页池）
            var mAvailable = MakeMetricRow("可用内存 (Available)");
            var mCache = MakeMetricRow("系统缓存 (Cache)");
            var mCommit = MakeMetricRow("已提交 (Committed)");
            var mCommitLimit = MakeMetricRow("提交上限 (Commit Limit)");
            var mPoolPaged = MakeMetricRow("分页池 (Paged Pool)");
            var mPoolNonpaged = MakeMetricRow("非分页池 (Nonpaged Pool)");
            bInner.Children.Add(mAvailable.row);
            bInner.Children.Add(mCache.row);
            bInner.Children.Add(mCommit.row);
            bInner.Children.Add(mCommitLimit.row);
            bInner.Children.Add(mPoolPaged.row);
            bInner.Children.Add(mPoolNonpaged.row);

            var noteTb = new TextBlock { FontSize = 10.5, Foreground = _textDim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Text = "" };
            bInner.Children.Add(noteTb);
            root.Children.Add(bCard);

            // ===================== 卡片 B2：进程工作集 Top 10 =====================
            var pCard = Card();
            var pInner = (StackPanel)pCard.Child;
            pInner.Children.Add(SectionTitle("📋 进程工作集 Top 10（B）"));
            pInner.Children.Add(new TextBlock { Text = "按工作集(Working Set)降序排列，取自 GetProcessMemoryInfo。", FontSize = 10.5, Foreground = _textDim, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            var procGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
            pInner.Children.Add(procGrid);
            root.Children.Add(pCard);

            // ===================== 卡片 C：内存优化（默认收起）=====================
            var exp = new Expander
            {
                Header = "⚡ 内存优化（中风险 · 需管理员）",
                IsExpanded = false,
                Foreground = _textMain,
                Margin = new Thickness(0, 0, 0, 0)
            };
            var expInner = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            expInner.Children.Add(new TextBlock
            {
                Text = "⚠ 优化本质是用「缓存 / 工作集」换「即时空闲内存」：清 Standby 让备用缓存转为可用；空工作集让进程内存回写。效果为临时——Windows 再次访问文件 / 内存会产生缺页延迟。仅在你明确需要大块连续空闲内存（虚拟机、游戏、Docker）时使用。",
                Foreground = _warnOrange,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var optLog = MakeLogBox();
            optLog.Height = 130;
            optLog.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var optLogBorder = WrapLogBox(optLog);

            bool isAdmin = MemoryAnalyzer.IsAdministrator();
            var btnPurge = Btn("🧹 清空备用列表(Standby)", true, null, 210);
            var btnEmpty = Btn("🗑 清空所有进程工作集", false, null, 210);

            if (!isAdmin)
            {
                btnPurge.IsEnabled = false;
                btnEmpty.IsEnabled = false;
                expInner.Children.Add(new TextBlock
                {
                    Text = "当前未以管理员身份运行，优化按钮已禁用。请以管理员身份重启本工具后再使用。",
                    Foreground = _dangerRed,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
            var optBtnRow = MakeBtnRow(btnPurge, btnEmpty);
            optBtnRow.Margin = new Thickness(0, 0, 0, 8);
            expInner.Children.Add(optBtnRow);
            expInner.Children.Add(optLogBorder);
            exp.Content = expInner;
            root.Children.Add(exp);

            // 进度条放最后
            root.Children.Add(pb);

            // 闭包：把抓取到的数据写入所有 UI 控件（在 UI 线程执行）。
            Action<MemoryAnalyzer.MemoryOverview, MemoryAnalyzer.MemoryUseCounts, List<MemoryAnalyzer.ProcessMemInfo>> applyUi =
                (overview, use, procs) =>
                {
                    tTotal.value.Text = MemoryAnalyzer.FormatBytes(overview.TotalPhys);
                    tLoad.value.Text = overview.MemoryLoad + " %";
                    tAvail.value.Text = MemoryAnalyzer.FormatBytes(overview.AvailPhys);
                    tCommit.value.Text = MemoryAnalyzer.FormatBytes(overview.CommitTotal) + " / " + MemoryAnalyzer.FormatBytes(overview.CommitLimit);
                    tPaged.value.Text = MemoryAnalyzer.FormatBytes(overview.KernelPaged);
                    tNonpaged.value.Text = MemoryAnalyzer.FormatBytes(overview.KernelNonpaged);

                    double total = (double)use.Total;
                    if (total <= 0) total = 1;
                    if (MemoryAnalyzer.IsBreakdownEmpty(use))
                    {
                        // 数据不可用：占比条保持整条可见（灰色占位），绝不收缩为 0 宽度而"消失"。
                        noteTb.Text = "（内存拆解数据不可用：已尝试 WMI 与 PDH 性能计数器均失败（或服务不可用）。总览数据仍可用，可点击「重新分析」重试。）";
                        for (int i = 0; i < 4; i++)
                        {
                            bBar.ColumnDefinitions[i].Width = new GridLength(1, GridUnitType.Star);
                            segments[i].Background = MemBrushUnknown;
                        }
                        legendPanel.Children.Clear();
                    }
                    else
                    {
                        noteTb.Text = use.IsDegraded
                            ? "（WMI/PDH 均不可用，当前拆解视图为基于总览数据的降级显示：仅区分使用中/可用，备用/已修改/缓存无法细分。）"
                            : "";
                        var fracs = new[] { use.InUse, use.Standby, use.Modified, use.FreeZero };
                        for (int i = 0; i < 4; i++)
                        {
                            bBar.ColumnDefinitions[i].Width = new GridLength(Math.Max(fracs[i], 0) / total, GridUnitType.Star);
                            segments[i].Background = segBrushes[i];
                        }
                        FillMemoryLegend(legendPanel, use);
                    }

                    // 明细行：WMI 拆解值为 0（不可用）时回落到 GetOverview 的可靠值，避免把"0 B"当作真实数据显示；
                    // 系统缓存(Cache)在 WMI 不可用时无可靠替代源，显式标记为 N/A。
                    ulong availBytes = use.Available > 0 ? use.Available : overview.AvailPhys;
                    ulong committed = use.Committed > 0 ? use.Committed : overview.CommitTotal;
                    ulong commitLimit = use.CommitLimit > 0 ? use.CommitLimit : overview.CommitLimit;
                    ulong pagedPool = use.PoolPaged > 0 ? use.PoolPaged : overview.KernelPaged;
                    ulong nonpagedPool = use.PoolNonpaged > 0 ? use.PoolNonpaged : overview.KernelNonpaged;
                    mAvailable.value.Text = MemoryAnalyzer.FormatBytes(availBytes);
                    mCache.value.Text = use.Cache > 0 ? MemoryAnalyzer.FormatBytes(use.Cache) : "N/A（WMI 不可用）";
                    mCommit.value.Text = MemoryAnalyzer.FormatBytes(committed);
                    mCommitLimit.value.Text = MemoryAnalyzer.FormatBytes(commitLimit);
                    mPoolPaged.value.Text = MemoryAnalyzer.FormatBytes(pagedPool);
                    mPoolNonpaged.value.Text = MemoryAnalyzer.FormatBytes(nonpagedPool);

                    FillProcessGrid(procGrid, procs);
                };

            // 重新分析按钮：闭包声明后方可绑定（避免前向引用 CS0841）。
            analyzeBtn.Click += (s, e) => DoMemoryAnalyze(pb, applyUi);

            // 优化按钮：优化完成后在 UI 线程自动重新分析内存，让「内存使用拆解」视图实时刷新
            // （RAMMap 清理后即可瞬间看到变化；此前缺少 onDoneUi 回调，视图一直冻结，误以为优化无效）。
            Action reanalyze = () => DoMemoryAnalyze(pb, applyUi);
            btnPurge.Click += (s, e) => RunInBg(optLog, l =>
            {
                string r = MemoryAnalyzer.OptimizePurgeStandby();
                foreach (var line in r.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) l(line);
            }, "已清空备用列表", reanalyze);
            btnEmpty.Click += (s, e) => RunInBg(optLog, l =>
            {
                string r = MemoryAnalyzer.OptimizeEmptyWorkingSets();
                foreach (var line in r.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) l(line);
            }, "已尝试清空工作集", reanalyze);

            // 初次分析（后台拉取，不阻塞 UI）
            DoMemoryAnalyze(pb, applyUi);

            // ---- 页面级缓存（同 M1 常用软件页模式）：首次构建完成后缓存整页；再次进入复用并仅复位动态状态 + 重跑分析 ----
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）。
            if (buildDark == _isDarkMode)
            {
                _cachedMemoryPage = root;
                _memoryCacheKey = "memory";
                _memoryCacheDark = buildDark;
                _memoryRefresh = () =>
                {
                    // 复位动态状态（与旧版每次新建页面行为一致）：优化卡片收起、清空优化日志、进度条隐藏。
                    exp.IsExpanded = false;
                    optLog.Clear();
                    pb.Visibility = Visibility.Collapsed;
                    // 重新触发原页面构建时的 AutoLoad 内存分析，保证二次进页内存数据最新。
                    DoMemoryAnalyze(pb, applyUi);
                };
            }

            return root;
        }

        // 后台拉取并填充所有只读数据。
        private void DoMemoryAnalyze(ProgressBar pb, Action<MemoryAnalyzer.MemoryOverview, MemoryAnalyzer.MemoryUseCounts, List<MemoryAnalyzer.ProcessMemInfo>> applyUi)
        {
            pb.Visibility = Visibility.Visible;
            RunInBg(null, l =>
            {
                var overview = MemoryAnalyzer.GetOverview();
                var use = MemoryAnalyzer.GetUseCounts(overview.TotalPhys, overview);
                var procs = MemoryAnalyzer.GetProcessWorkingSets(10);
                try { Dispatcher.Invoke(() => applyUi(overview, use, procs)); } catch { /* 窗口已关闭，忽略 */ }
            }, "内存分析完成", () => pb.Visibility = Visibility.Collapsed);
        }

        // 占比条图例：颜色块 + 名称 + 大小 + 百分比。
        private void FillMemoryLegend(StackPanel panel, MemoryAnalyzer.MemoryUseCounts u)
        {
            panel.Children.Clear();
            double total = (double)u.Total;
            if (total <= 0) total = 1;
            AddLegendRow(panel, MemBrushInUse, "使用中 (Active)", u.InUse, total);
            AddLegendRow(panel, MemBrushStandby, "备用 (Standby)", u.Standby, total);
            AddLegendRow(panel, MemBrushModified, "已修改 (Modified)", u.Modified, total);
            AddLegendRow(panel, MemBrushFree, "空闲+零页 (Free+Zero)", u.FreeZero, total);
        }

        private void AddLegendRow(StackPanel panel, Brush color, string name, ulong bytes, double total)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chip = new Border { Width = 12, Height = 12, Background = color, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var n = new TextBlock { Text = name, Foreground = _textMain, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            double pct = bytes / total * 100.0;
            if (pct < 0) pct = 0;
            var v = new TextBlock { Text = MemoryAnalyzer.FormatBytes(bytes) + "  (" + pct.ToString("F1") + "%)", Foreground = _textDim, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chip, 0);
            Grid.SetColumn(n, 1);
            Grid.SetColumn(v, 2);
            row.Children.Add(chip);
            row.Children.Add(n);
            row.Children.Add(v);
            row.Margin = new Thickness(0, 2, 0, 2);
            panel.Children.Add(row);
        }

        // 进程工作集表格填充。
        private void FillProcessGrid(Grid g, List<MemoryAnalyzer.ProcessMemInfo> procs)
        {
            g.Children.Clear();
            g.RowDefinitions.Clear();
            int r = 0;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Action<int, string, bool, HorizontalAlignment> add = (col, text, bold, align) =>
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = bold ? _textMain : _textDim,
                    HorizontalAlignment = align,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4)
                };
                Grid.SetColumn(tb, col);
                Grid.SetRow(tb, r);
                g.Children.Add(tb);
            };
            add(0, "进程名称", true, HorizontalAlignment.Left);
            add(1, "PID", true, HorizontalAlignment.Right);
            add(2, "工作集", true, HorizontalAlignment.Right);
            add(3, "私有内存", true, HorizontalAlignment.Right);
            r++;
            foreach (var p in procs)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                add(0, p.Name, false, HorizontalAlignment.Left);
                add(1, p.Pid.ToString(), false, HorizontalAlignment.Right);
                add(2, MemoryAnalyzer.FormatBytes(p.WorkingSet), false, HorizontalAlignment.Right);
                add(3, MemoryAnalyzer.FormatBytes(p.PrivateBytes), false, HorizontalAlignment.Right);
                r++;
            }
            if (procs.Count == 0)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var empty = new TextBlock { Text = "（无数据）", Foreground = _textDim, FontSize = 12, Margin = new Thickness(6, 4, 6, 4) };
                Grid.SetColumn(empty, 0);
                Grid.SetColumnSpan(empty, 4);
                Grid.SetRow(empty, r);
                g.Children.Add(empty);
            }
        }

        // ---- 小型 UI 辅助（本页专用，不与 Helpers.cs 冲突）----
        private TextBlock SectionTitle(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
        }

        private (Border tile, TextBlock value) MakeStatTile(string label)
        {
            var value = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = _textMain, Margin = new Thickness(0, 0, 0, 2), Text = "—" };
            var lbl = new TextBlock { FontSize = 11, Foreground = _textDim, Text = label, TextWrapping = TextWrapping.Wrap };
            var sp = new StackPanel();
            sp.Children.Add(value);
            sp.Children.Add(lbl);
            var b = new Border
            {
                Child = sp,
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                MinWidth = 150,
                Margin = new Thickness(5)
            };
            return (b, value);
        }

        private (Grid row, TextBlock value) MakeMetricRow(string name)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = name, Foreground = _textDim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock { Foreground = _textMain, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = "—" };
            Grid.SetColumn(n, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(n);
            g.Children.Add(v);
            g.Margin = new Thickness(0, 3, 0, 3);
            return (g, v);
        }
    }
}
