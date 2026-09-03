using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: Appx 管理（两行三列卡片布局，含当前用户/系统预装切换）
        // =====================================================================

        // Appx 商店页缓存（同「常用软件」M1 模式：整页实例缓存）。
        // 主题一致 + key 匹配时复用已构建页面，Refresh 复位搜索/勾选/日志等动态状态并后台刷新。
        private readonly PageCache<UIElement> _appxCache = new PageCache<UIElement>();

        private UIElement BuildAppx()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且 catalog 目录未变化且主题一致 → 复用已构建页面，仅后台刷新动态状态
            var cached = _appxCache.TryGet(buildDark, string.Join("|", AppxManager.Catalog.Select(c => c.PackageFamily)));
            if (cached != null) return cached;

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
            // ★ 搜索应用按钮已迁移到「常用软件」页（列表式更和谐）。
            // （原 StoreSearchWindow 类经审查确认无任何实例化点，已在死代码清理中移除）
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
                // 从 Border.Tag 直接取 CheckBox 引用，统一交由 ToggleSelectAll 处理（□/☑ 约定）
                ToggleSelectAll(
                    cardsPanel.Children.OfType<Border>()
                        .Select(b => (b.Tag as System.Tuple<string, System.Windows.Controls.CheckBox>)?.Item2)
                        .Where(cb => cb != null),
                    ref allSelected, btnToggleSel);
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
                }, "卸载完成", () => { _appxCache.Invalidate(); _appxRawCache.Invalidate(); pb.Visibility = Visibility.Collapsed; LoadAndRender(rbCurrent.IsChecked == true, cardsPanel, countLbl, searchBox.Text, log, UpdateAppxSelCount); });
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
            // ---- 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅刷新动态状态 ----
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _appxCache.Set(root, buildDark);
                _appxCache.SetContentKey(string.Join("|", AppxManager.Catalog.Select(c => c.PackageFamily)));
                _appxCache.SetRefresh(() =>
                {
                    // 复位动态状态（与新建页面行为一致）：
                    // 清空搜索（TextChanged 触发后台重载）、复位全选/勾选/模式/选项；
                    // 搜索本就为空时显式后台重载（保证每次进页后台全量刷新一次）
                    if (searchBox.Text.Length > 0) searchBox.Text = "";
                    else LoadAndRender(true, cardsPanel, countLbl, "", log, UpdateAppxSelCount);
                    allSelected = false;
                    btnToggleSel.Content = "□ 全选";
                    rbCurrent.IsChecked = true;
                    chkConfirm.IsChecked = true;
                    chkWithProvisioned.IsChecked = true;
                });
            }

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
                        try { Dispatcher.Invoke(() =>
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
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }
                    else
                    {
                        var items = AppxManager.ListProvisioned(_ => {});
                        try { Dispatcher.Invoke(() =>
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
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }
                }
                catch (Exception ex) { try { Dispatcher.Invoke(() => log.AppendText("[!] " + ex.Message + "\r\n")); } catch { /* 窗口已关闭，忽略 */ } }
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
                // 安装/卸载完成 → 显式失效整页缓存（下次进页重建，避免复用 stale 状态）
                _appxCache.Invalidate();
                _appxRawCache.Invalidate();
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
                    try { Dispatcher.Invoke(() =>
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
                    }); } catch { /* 窗口已关闭，忽略 */ }
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

        // Appx 管理页缓存（同 Appx 商店页模式：整页实例缓存，主题一致时复用，Refresh 复位动态状态并后台刷新）
        private readonly PageCache<UIElement> _appxRawCache = new PageCache<UIElement>();

        private UIElement BuildAppxRaw()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅后台刷新列表
            // （列表签名来自 ListInstalled 的 PowerShell 扫描结果，无法在不扫描的前提下计算 →
            //   用主题作唯一 key + 卸载后显式 invalidate 保证数据正确）
            var cached = _appxRawCache.TryGet(buildDark);
            if (cached != null) return cached;

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
                    try { Dispatcher.Invoke(() =>
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
                                    "已卸载: " + it.Name, () => { _appxCache.Invalidate(); _appxRawCache.Invalidate(); pb.Visibility = Visibility.Collapsed; RefreshList(); });
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
                    }); } catch { /* 窗口已关闭，忽略 */ }
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
            var btnToggleRaw = Btn("□ 全选", false, null, 100);
            btnToggleRaw.HorizontalAlignment = HorizontalAlignment.Center;
            btnToggleRaw.Click += (s, e) =>
            {
                ToggleSelectAll(rowItems.Select(t => t.Item1), ref rawAllSelected, btnToggleRaw);
                UpdateRawSelCount();
            };
            var btnUninstallSel = Btn("卸载选中", false, () =>
            {
                var sel = rowItems.Where(t => t.Item1.IsChecked == true && !string.IsNullOrEmpty(t.Item3.FullName))
                    .Select(t => t.Item3.FullName).ToList();
                if (sel.Count == 0) { log.AppendText("[!] 请先勾选要卸载的应用\r\n"); return; }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => AppxManager.Uninstall(sel, l), "卸载完成", () => { _appxCache.Invalidate(); _appxRawCache.Invalidate(); pb.Visibility = Visibility.Collapsed; RefreshList(true); });
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
            // ---- 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅刷新动态状态 ----
            if (buildDark == _isDarkMode)
            {
                _appxRawCache.Set(root, buildDark);
                _appxRawCache.SetRefresh(() =>
                {
                    // 复位动态状态（与新建页面行为一致）：清空勾选、复位全选、清空日志、后台刷新列表
                    foreach (var t in rowItems) t.Item1.IsChecked = false;
                    rawAllSelected = false;
                    btnToggleRaw.Content = "□ 全选";
                    log.Clear();
                    RefreshList(false);
                });
            }

            BindRootHeightToViewport(root);
            return root;
        }

        // 统一的「全选/取消全选」切换：翻转状态位、勾选全部传入的 CheckBox、并将按钮文案切换为 □/☑ 约定。
        // Appx 的两个面板（卡片视图 / 原始包视图）共用，避免重复且 emoji 不一致。
        private void ToggleSelectAll(IEnumerable<System.Windows.Controls.CheckBox> boxes, ref bool allSelected, Button btn)
        {
            allSelected = !allSelected;
            foreach (var cb in boxes) cb.IsChecked = allSelected;
            btn.Content = allSelected ? "☑ 取消全选" : "□ 全选";
        }
    }
}
