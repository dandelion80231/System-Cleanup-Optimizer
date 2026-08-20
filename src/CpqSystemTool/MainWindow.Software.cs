using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.VisualBasic;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
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
                    try { Dispatcher.Invoke(() =>
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
                        // 修复：AllowsTransparency=true 会以独立顶层 HWND 承载并带 WS_EX_TOPMOST，
                        // 导致下拉浮到最顶层。剥离该样式使其落到正常层级（与"管理依赖"下拉一致）。
                        UiShapes.DisablePopupTopmost(catPopup);
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
                                    try { Dispatcher.Invoke(() =>
                                    {
                                        foreach (var info in list) RefreshRow(info.Id, info);
                                        pb.Visibility = Visibility.Collapsed;
                                        onComplete?.Invoke();
                                    }); } catch { /* 窗口已关闭，忽略 */ }
                                }
                                catch (Exception ex)
                                {
                                    try { Dispatcher.Invoke(() =>
                                    {
                                        pb.Visibility = Visibility.Collapsed;
                                        log.AppendText("[!] 刷新列表失败: " + ex.Message + "\r\n");
                                        onComplete?.Invoke();
                                    }); } catch { /* 窗口已关闭，忽略 */ }
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
                                // InstallAsync 为真异步（内部下载/解析不再阻塞）；此处后台线程同步等待其完成：
                                // 无 SynchronizationContext，GetAwaiter().GetResult() 无死锁且异常同步传播（RunInBg 的 try/catch 可捕获，
                                // 不用 async void lambda —— 其异常会逃逸到 ThreadPool 触发 UnhandledException 崩溃）。
                                RunInBg(log, l => SoftwareInstall.InstallAsync(sw.Id, l, customDir).GetAwaiter().GetResult(),
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
                                    // 后台线程同步等待异步安装（无 SyncContext 无死锁；异常同步传播到 RunInBg 兜底）
                                    SoftwareInstall.InstallAsync(sw.Id, l, customDir).GetAwaiter().GetResult();
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
                            string label;
                            Brush bg, border, fg;
                            FontWeight fw;
                            string tooltip;
                            if (!string.IsNullOrEmpty(saved))
                            {
                                label = "📂 安装到: " + saved;
                                bg = _btnSecondaryBg;          // 随深/浅色主题自适应（替换原硬编码浅薄荷色 0xE6F7F4）
                                border = _accent;
                                fg = _accent;
                                fw = FontWeights.SemiBold;
                                tooltip = "当前自定义安装路径：" + saved + "\n点击修改";
                            }
                            else
                            {
                                label = "📂 安装到: 默认路径";
                                bg = _btnSecondaryBg;
                                border = _panelBorder;
                                fg = _btnSecondaryFg;
                                fw = FontWeights.Normal;
                                tooltip = "当前使用各软件默认安装路径\n点击设置自定义路径";
                            }
                            var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                            btnPath.Content = UiShapes.MakeTextWithArrowGrid(text, _textDim, minWidth: true);
                            btnPath.Background = bg;
                            btnPath.BorderBrush = border;
                            btnPath.Foreground = fg;
                            btnPath.FontWeight = fw;
                            btnPath.ToolTip = tooltip;
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
                    }); } catch { /* 窗口已关闭，忽略 */ }
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
    }
}
