using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
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
            // C4: 以下 5 项（thumb/d3d/term/prefetch/winsxs）的 Name/Desc 统一引用 CleanupExt.Items，新增项只需改 CleanupExt.Items 一处
            new CleanupItemDef { Id="thumb", Name=CleanupExt.Items.First(x=>x.Id=="thumb").Name, Desc=CleanupExt.Items.First(x=>x.Id=="thumb").Desc, Category="缓存文件", DefaultChecked=true, Action=log=>CleanupExt.RunSelected(new[]{"thumb"},log) },
            new CleanupItemDef { Id="d3d", Name=CleanupExt.Items.First(x=>x.Id=="d3d").Name, Desc=CleanupExt.Items.First(x=>x.Id=="d3d").Desc, Category="缓存文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"d3d"},log) },
            new CleanupItemDef { Id="term", Name=CleanupExt.Items.First(x=>x.Id=="term").Name, Desc=CleanupExt.Items.First(x=>x.Id=="term").Desc, Category="缓存文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"term"},log) },
            new CleanupItemDef { Id="prefetch", Name=CleanupExt.Items.First(x=>x.Id=="prefetch").Name, Desc=CleanupExt.Items.First(x=>x.Id=="prefetch").Desc, Category="缓存文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"prefetch"},log) },
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
            new CleanupItemDef { Id="whesvc_diag", Name="Whesvc 诊断日志", Desc="Win健康状况服务本地性能追踪(可安全删，会再生)", Category="系统文件", DefaultChecked=false, Action=log=>Cleanup.WhesvcDiag(log) },
            new CleanupItemDef { Id="winsxs_dism", Name=CleanupExt.Items.First(x=>x.Id=="winsxs").Name, Desc=CleanupExt.Items.First(x=>x.Id=="winsxs").Desc, Category="系统文件", DefaultChecked=false, Action=log=>CleanupExt.RunSelected(new[]{"winsxs"},log) },

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

        // 清理优化页整页缓存（与 M1 常用软件页同款模式：面板/页面实例缓存）。
        // 首次构建完成后缓存整页外壳；再次进入复用已构建面板，仅复位勾选/日志/进度等动态状态，
        // 避免每次导航重建 ~35 项 CheckBox × 多事件闭包。清理执行（改变可清理大小）时缓存失效重建。
        private readonly PageCache<UIElement> _cleanupCache = new PageCache<UIElement>();

        private UIElement BuildCleanup()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅复位动态状态
            var cached = _cleanupCache.TryGet(buildDark);
            if (cached != null) return cached;

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
            var expanders = new List<Expander>();   // 缓存复用时复位分组展开状态

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
                expanders.Add(groupExpander);
                foreach (var item in cat)
                {
                    var chk = new CheckBox
                    {
                        Tag = item.Id,
                        IsChecked = item.DefaultChecked,
                        Margin = new Thickness(0, 3, 0, 0),
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Top,
                        VerticalContentAlignment = VerticalAlignment.Top
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
                // P1 防重入：全局互斥，防止连点或跨模块并发（清理/优化同一时间只允许一个耗时操作）
                if (!OperationLock.TryEnter("清理", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
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
                }, "清理完成", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; _cleanupCache.Invalidate(); });
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
                        RunInBg(log, l2 => Cleanup.DeleteTier3(toDel, l2), "第三档删除完成", () => { pb.Visibility = Visibility.Collapsed; _cleanupCache.Invalidate(); });
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

            // ---- 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅复位动态状态 ----
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _cleanupCache.Set(root, buildDark);
                _cleanupCache.SetRefresh(() =>
                {
                    // 复位动态状态（与每次新建页面行为一致）：
                    // 恢复默认勾选、复位「全选当前页」与分组展开、清空日志（含旧扫描结果→回到未扫描态）、
                    // 进度条收起、按钮高亮回到「开始清理」、刷新勾选计数
                    foreach (var c in allCheckBoxes)
                    {
                        var def = CleanupCatalog.FirstOrDefault(d => d.Id == (string)c.Tag);
                        c.IsChecked = (def != null && def.DefaultChecked);
                    }
                    chkAll.IsChecked = false;
                    foreach (var ex in expanders) ex.IsExpanded = true;
                    log.Clear();
                    pb.Visibility = Visibility.Collapsed;
                    ApplyMode(btnClean);
                    UpdateCleanupSelCount();
                });
            }
            return root;
        }
    }
}
