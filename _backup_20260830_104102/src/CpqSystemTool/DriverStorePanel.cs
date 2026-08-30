using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 驱动管理面板（独立驱动清理页面）。
    /// 列出系统已安装驱动包，支持：
    ///   - 后端引擎切换（PnP 实用工具 pnputil / DISM）
    ///   - 刷新枚举、勾选旧版冗余可清理驱动、导出备份、添加驱动包、安装到设备
    ///   - 删除选中（默认安全）/ 强制删除（/force）
    ///   - 启动关键驱动默认受保护（可选"包含启动关键驱动程序"解除保护）
    /// 安全护栏：正在使用的驱动（InUse）禁止删除；删除前二次确认；强制删除额外严厉确认。
    /// 后台扫描不卡 UI。
    /// </summary>
    internal class DriverStorePanel : UserControl
    {
        private readonly MainWindow _owner;
        private readonly TextBox _externalLog;
        private DataGrid _dg;
        private List<DriverStore.DriverInfo> _drivers = new List<DriverStore.DriverInfo>();
        private TextBox _log;
        private TextBlock _selStatus;
        private TextBlock _dismHint;
        private CheckBox _headerCb;
        private Button _delBtn, _forceBtn, _exportBtn, _installBtn;
        private ComboBox _groupCombo;

        // 后端引擎 / 启动关键包含开关（UI 状态）
        private DriverStore.DriverEngine _engine = DriverStore.DriverEngine.PnpUtil;
        private bool _includeBootCritical;

        // 分组方式：None / Class / Provider
        public enum GroupMode { None, Class, Provider }
        private GroupMode _groupMode = GroupMode.Class;

        // 列头排序状态
        private readonly Dictionary<DataGridTextColumn, string> _originalHeaders = new Dictionary<DataGridTextColumn, string>();
        private string _currentSortPath;
        private ListSortDirection? _currentSortDir;

        // 主题笔刷（来自主窗口）
        private readonly SolidColorBrush _fg, _dim, _panelBorder, _danger, _accent,
            _warn, _rowHover, _btnPriFg;

        /// <summary>创建驱动管理面板。logBox 为外部日志框（共用页面运行日志）；传入 null 则使用面板自带日志框。</summary>
        public DriverStorePanel(MainWindow owner, TextBox logBox = null)
        {
            _owner = owner;
            _externalLog = logBox;

            _fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            _dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            _panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            _danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));
            _accent = owner?._accent ?? new SolidColorBrush(Color.FromRgb(0x2D, 0xC8, 0x8C));
            _warn = owner?._warnOrange ?? new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D));
            _rowHover = owner?._rowHover ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x26, 0x30));
            _btnPriFg = owner?._btnPrimaryFg ?? new SolidColorBrush(Colors.White);

            BuildUi();
            UpdateActionButtons();
            UpdateSelStatus();
            // 注意：构造时不再自动 Refresh()。枚举由主窗口在「启动预加载」与「每次进入该页」时显式触发，
            // 以保证「进入页面即看到已加载数据，并每次进入都后台刷新」。
        }

        private void BuildUi()
        {
            Background = Brushes.Transparent;
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: 顶部控件区（说明/引擎/工具栏/提示/状态）
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: 数据网格（撑满剩余空间）
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: 日志区

            var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetRow(topPanel, 0);
            body.Children.Add(topPanel);

            // 说明文字已上移至 MainWindow.DriverStore.cs（放在圆角卡片之外，与维护工具页一致）

            // 后端引擎行：引擎下拉 + 刷新 + 分组 + 启动关键包含勾选
            var engineRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
            engineRow.Children.Add(new TextBlock
            {
                Text = "后端引擎：",
                Foreground = _dim,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var engineCombo = new ComboBox
            {
                Width = 240,
                MinWidth = 200,
                SelectedIndex = 0,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(engineCombo, UiShapes.ComboBoxTheme.Create(
            _owner?._inputBg ?? UiShapes.DefaultInputBackground, _fg,
            _owner?._windowBg ?? Brushes.White, _panelBorder,
                _fg, _rowHover, _owner?._rowSelected ?? UiShapes.RowSelectedBrush, _dim));
            engineCombo.Items.Add(new ComboBoxItem { Content = "PnP 实用工具 (pnputil) — 仅第三方", Tag = DriverStore.DriverEngine.PnpUtil });
            engineCombo.Items.Add(new ComboBoxItem { Content = "DISM（系统映像）— 含内置驱动", Tag = DriverStore.DriverEngine.Dism });
            engineCombo.SelectionChanged += (s, e) =>
            {
                if (engineCombo.SelectedItem is ComboBoxItem ci && ci.Tag is DriverStore.DriverEngine eng)
                {
                    _engine = eng;
                    UpdateActionButtons();
                    Refresh();
                }
            };
            engineRow.Children.Add(engineCombo);

            var refreshBtn = _owner != null ? _owner.Btn("刷新", true, () => Refresh(), 96) : new Button { Content = "刷新", Width = 96 };
            refreshBtn.Margin = new Thickness(10, 0, 10, 0);
            engineRow.Children.Add(refreshBtn);

            engineRow.Children.Add(new TextBlock
            {
                Text = "分组：",
                Foreground = _dim,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            _groupCombo = new ComboBox
            {
                Width = 120,
                MinWidth = 100,
                SelectedIndex = 1,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "按驱动类别或供应商分组显示，方便批量查看同类驱动"
            };
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(_groupCombo, UiShapes.ComboBoxTheme.Create(
            _owner?._inputBg ?? UiShapes.DefaultInputBackground, _fg,
            _owner?._windowBg ?? Brushes.White, _panelBorder,
                _fg, _rowHover, _owner?._rowSelected ?? UiShapes.RowSelectedBrush, _dim));
            _groupCombo.Items.Add(new ComboBoxItem { Content = "不分组", Tag = GroupMode.None });
            _groupCombo.Items.Add(new ComboBoxItem { Content = "按类别", Tag = GroupMode.Class });
            _groupCombo.Items.Add(new ComboBoxItem { Content = "按供应商", Tag = GroupMode.Provider });
            _groupCombo.SelectionChanged += (s, e) =>
            {
                if (_groupCombo.SelectedItem is ComboBoxItem ci && ci.Tag is GroupMode gm)
                {
                    _groupMode = gm;
                    UpdateGrouping();
                }
            };
            engineRow.Children.Add(_groupCombo);

            var includeBootCb = new CheckBox
            {
                Content = "包含启动关键驱动程序",
                Foreground = _dim,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                ToolTip = "取消勾选时，启动关键驱动（Boot Critical）受保护、不可删除；勾选后允许在（强制）删除时移除——可能导致系统无法启动，请谨慎。"
            };
            includeBootCb.Checked += (s, e) => { _includeBootCritical = true; };
            includeBootCb.Unchecked += (s, e) => { _includeBootCritical = false; };
            engineRow.Children.Add(includeBootCb);
            topPanel.Children.Add(engineRow);

            // 工具栏（7 列等宽均分整行）
            var selOldBtn = _owner != null ? _owner.Btn("全选可清理", false, () => SelectCleanable(), 110) : new Button { Content = "全选可清理", Width = 110 };
            var selVerBtn = _owner != null ? _owner.Btn("选中旧版", false, () => SelectOld(), 96) : new Button { Content = "选中旧版", Width = 96 };
            _exportBtn = _owner != null ? _owner.Btn("导出备份", false, () => ExportSelected(), 110) : new Button { Content = "导出备份", Width = 110 };
            var addBtn = _owner != null ? _owner.Btn("添加驱动包", false, () => AddDriverDialog(), 120) : new Button { Content = "添加驱动包", Width = 120 };
            _installBtn = _owner != null ? _owner.Btn("安装选中", false, () => InstallSelected(), 110) : new Button { Content = "安装选中", Width = 110 };
            _delBtn = _owner != null ? _owner.Btn("删除选中", false, () => DeleteSelected(), 110) : new Button { Content = "删除选中", Width = 110 };
            _forceBtn = _owner != null ? _owner.Btn("强制删除", false, () => ForceDelete(), 110) : new Button { Content = "强制删除", Width = 110 };
            // 删除按钮用危险色强调
            if (_owner != null)
            {
                _delBtn.Background = _danger;
                _delBtn.Foreground = _btnPriFg;
                _delBtn.BorderThickness = new Thickness(0);
                _forceBtn.Background = _danger;
                _forceBtn.Foreground = _btnPriFg;
                _forceBtn.BorderThickness = new Thickness(1.5);
                _forceBtn.BorderBrush = _owner?._dangerDark ?? _danger;   // 主题危急描边，避免硬编码魔法色
            }
            var toolRow = MainWindow.MakeBtnRow(selOldBtn, selVerBtn, _exportBtn, addBtn, _installBtn, _delBtn, _forceBtn);
            toolRow.Margin = new Thickness(0, 0, 0, 8);
            topPanel.Children.Add(toolRow);

            // DISM 后端提示：明确告诉用户为什么部分按钮被禁用，避免误以为是 bug
            _dismHint = new TextBlock
            {
                Text = "当前使用 DISM 后端，仅支持查看驱动信息。删除 / 导出 / 安装功能已禁用；如需操作请切换回「PnP 实用工具」后端。",
                Foreground = _warn,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            topPanel.Children.Add(_dismHint);

            // 选择状态行
            _selStatus = new TextBlock
            {
                FontSize = 11.5,
                Foreground = _dim,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                Margin = new Thickness(0, 0, 0, 6)
            };
            topPanel.Children.Add(_selStatus);

            // 数据网格
            _dg = new DataGrid
            {
                AutoGenerateColumns = false,
                Background = Brushes.Transparent,
                Foreground = _fg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 760,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                RowBackground = Brushes.Transparent,
                AlternatingRowBackground = Brushes.Transparent,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = _panelBorder,
                SelectionMode = DataGridSelectionMode.Single,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                EnableRowVirtualization = false
            };
            _dg.Sorting += OnDataGridSorting;
            var dsStyles = MainWindow.MakeDataGridStyles(_fg, _panelBorder);
            _dg.ColumnHeaderStyle = dsStyles.Header;
            _dg.CellStyle = dsStyles.Cell;

            // 选择列（表头全选 + 单元格绑定 Selected）
            var selCol = new DataGridTemplateColumn
            {
                Header = MakeHeaderCheckBox(out _headerCb),
                Width = new DataGridLength(46),
                CanUserSort = false,
                // 继承 DataGrid 的列头样式（透明底 + 主题文字色），仅覆盖为居中；
                // 否则该列单独设 HeaderStyle 会整体覆盖 ColumnHeaderStyle，回退到默认系统色 → 第一列表头出现白底方块。
                HeaderStyle = new Style(typeof(DataGridColumnHeader))
                {
                    BasedOn = _dg.ColumnHeaderStyle,
                    Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center) }
                }
            };
            var selFactory = new FrameworkElementFactory(typeof(CheckBox));
            selFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            selFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            selFactory.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => UpdateSelStatus()));
            selFactory.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => UpdateSelStatus()));
            selCol.CellTemplate = new DataTemplate { VisualTree = selFactory };
            _dg.Columns.Add(selCol);

            // 文本单元格统一左对齐，避免表头居中、内容居左导致的视觉偏差
            var cellLeftStyle = new Style(typeof(TextBlock));
            cellLeftStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left));
            cellLeftStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));

            // 列顺序与标题对齐 Driver Store Explorer / RAPR：INF、驱动类别、提供商、版本、日期、安装日期、大小、设备名称
            AddSortableTextColumn("INF", "OemName", "OemName", new DataGridLength(0.8, DataGridLengthUnitType.Star), 90, cellLeftStyle);
            AddSortableTextColumn("驱动类别", "ClassDescription", "ClassDescription", new DataGridLength(1.0, DataGridLengthUnitType.Star), 100, cellLeftStyle);
            AddSortableTextColumn("提供商", "Provider", "Provider", new DataGridLength(1.2, DataGridLengthUnitType.Star), 120, cellLeftStyle);
            AddSortableTextColumn("版本", "Version", "Version", DataGridLength.Auto, 85, cellLeftStyle);
            AddSortableTextColumn("日期", "DateText", "Date", DataGridLength.Auto, 85, cellLeftStyle);
            AddSortableTextColumn("安装日期", "InstallDateText", "InstallDate", DataGridLength.Auto, 85, cellLeftStyle);
            AddSortableTextColumn("大小", "SizeText", "SizeMB", DataGridLength.Auto, 60, cellLeftStyle);
            AddSortableTextColumn("设备名称", "DeviceNameText", "DeviceName", new DataGridLength(1.5, DataGridLengthUnitType.Star), 130, cellLeftStyle);

            // 行样式：透明底 + 统一前景；具体颜色在 LoadingRow 按在役/关键/旧版设置
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.Transparent));
            rowStyle.Setters.Add(new Setter(DataGridRow.ForegroundProperty, _fg));
            rowStyle.Setters.Add(new Setter(DataGridRow.BorderBrushProperty, Brushes.Transparent));
            _dg.RowStyle = rowStyle;
            _dg.LoadingRow += (s, e) =>
            {
                var di = e.Row.Item as DriverStore.DriverInfo;
                if (di == null) return;
                e.Row.Foreground = di.InUse ? _danger : (di.BootCritical ? _warn : (di.IsOld ? _warn : _fg));
                // 移除状态/原始名显示列后，用 Tooltip 保留关键信息
                e.Row.ToolTip = $"状态：{di.StatusText}\n原始 INF：{di.OriginalName}\n签名：{di.Signer}";
                // 整行鼠标悬停高亮：与其他页面（常用软件、清理优化等）统一，直接用 MouseEnter/MouseLeave 事件
                // 修复（事件重复挂载）：DataGrid.LoadingRow 对同一个行容器可能多次触发（枚举刷新、排序、
                // 分组切换时行会被重新加载），直接 += 匿名委托会挂上多份 handler，一次悬停触发多次、
                // 后一次覆盖前一次，表现为高亮异常/闪烁。改为具名局部函数先 -= 再 +=，保证只挂一份。
                void RowMouseEnter(object rs, MouseEventArgs re)
                {
                    if (e.Row.Background == Brushes.Transparent)
                        e.Row.Background = _rowHover;
                }
                void RowMouseLeave(object rs, MouseEventArgs re)
                {
                    e.Row.Background = Brushes.Transparent;
                }
                e.Row.MouseEnter -= RowMouseEnter;
                e.Row.MouseEnter += RowMouseEnter;
                e.Row.MouseLeave -= RowMouseLeave;
                e.Row.MouseLeave += RowMouseLeave;
            };
            // 圆角数据网格外框：统一与其他页面的卡片风格（移除 DataGrid 自身方形边框，改由此外框提供圆角边框）
            var dgBorder = new Border
            {
                Child = _dg,
                Background = Brushes.Transparent,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true
            };
            _dg.BorderThickness = new Thickness(0);
            Grid.SetRow(dgBorder, 1);
            body.Children.Add(dgBorder);

            // 日志区（仅在没有外部日志框时显示自带日志）
            // 单层圆角边框（BorderThickness=1, CornerRadius=8, 边框画刷 _panelBorder），内部 _log 保持 BorderThickness=0，
            // 与维护工具页 WrapLogBox 一致——去掉的是"嵌套双层边框"，而非完全无框。
            var logLabel = new TextBlock
            {
                Text = "运行日志：",
                FontWeight = FontWeights.Bold,
                Foreground = _dim,
                FontSize = 12.0,
                Margin = new Thickness(0, 10, 0, 4)
            };
            _log = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = _fg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(0),   // 内部文本框无边框，避免与外层 logBorder 形成嵌套双层边框
                FontFamily = new FontFamily("Consolas, 'Courier New'"),
                FontSize = 11.0,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,       // 日志框自带纵向滚动条（用户要求：运行日志独立滚动控制）
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // 禁用横向滚动条，避免最外侧出现多余滚动控制
                Height = 72,                                                       // 固定压低高度（参考维护页 WrapLogBox=120，此处明显更矮）；最大化时由 Star 行保证 DataGrid 占满剩余空间
                Margin = new Thickness(0, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var logBorder = new Border
            {
                Child = _log,
                Background = Brushes.Transparent,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 0)
            };
            var logPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            logPanel.Children.Add(logLabel);
            logPanel.Children.Add(logBorder);
            // 有外部日志框时隐藏自带日志框（标签 + 文本框）
            if (_externalLog != null) logPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(logPanel, 2);
            body.Children.Add(logPanel);

            // 整页圆角卡片外框：与其他页面（激活工具 / 系统优化 / 清理优化等）的圆角卡片风格统一。
            // 背景设为 Transparent（深/浅模式通用，与 Card() 辅助方法一致），靠边框 + 圆角体现卡片层次。
            var card = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = body
            };
            Content = card;
        }

        private CheckBox MakeHeaderCheckBox(out CheckBox cb)
        {
            cb = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "全选 / 全不选"
            };
            cb.Checked += (s, e) => SetAllSelected(true);
            cb.Unchecked += (s, e) => SetAllSelected(false);
            return cb;
        }

        private void SetAllSelected(bool v)
        {
            foreach (var d in _drivers) d.Selected = v;
            _dg.Items.Refresh();
            UpdateSelStatus();
        }

        private void SelectWhere(Func<DriverStore.DriverInfo, bool> pred, string statusFmt)
        {
            foreach (var d in _drivers) d.Selected = pred(d);
            _dg.Items.Refresh();
            UpdateSelStatus();
            int n = _drivers.Count(x => x.Selected);
            _owner?.SetStatus(string.Format(statusFmt, n));
        }

        private void SelectCleanable() => SelectWhere(d => d.IsOld && !d.InUse, "已勾选 {0} 个可清理（旧版且未在使用）的驱动包");

        /// <summary>根据当前引擎启用/禁用依赖 oemX.inf 的操作按钮（DISM 后端没有 oem 发布名）。</summary>
        private void UpdateActionButtons()
        {
            bool dism = _engine == DriverStore.DriverEngine.Dism;
            string dismTip = "DISM 后端仅列出驱动信息，不包含 oemX.inf 发布名，无法执行删除/导出/安装。请切换到 PnPUtil 后端。";
            SetButtonEnabled(_delBtn, !dism, _danger, _btnPriFg, dism ? dismTip : null);
            SetButtonEnabled(_forceBtn, !dism, _danger, _btnPriFg, dism ? dismTip : null);
            SetButtonEnabled(_exportBtn, !dism, null, null, dism ? dismTip : null);
            SetButtonEnabled(_installBtn, !dism, null, null, dism ? dismTip : null);
            if (_dismHint != null) _dismHint.Visibility = dism ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>设置按钮启用/禁用状态，并同步视觉（禁用后变淡，避免看起来像可点）。</summary>
        private void SetButtonEnabled(Button btn, bool enabled, SolidColorBrush enabledBg, SolidColorBrush enabledFg, string tip)
        {
            if (btn == null) return;
            btn.IsEnabled = enabled;
            btn.ToolTip = tip;
            if (!enabled)
            {
                // 禁用态：保留按钮原有的（可读）前景/背景，交由全局 Button Style 的
                // IsEnabled=False → Opacity=0.5 触发器统一变淡。
                // 旧逻辑把禁用态写成「灰底 + 灰字(#555C64/#9AA0A8)」，在深色背景下两色过于接近，
                // 导致文字几乎不可见（顶部操作按钮行"看不清"的根因），故移除该覆盖。
                return;
            }
            if (enabledBg != null) btn.Background = enabledBg;
            if (enabledFg != null) btn.Foreground = enabledFg;
        }

        /// <summary>一键选中所有旧版驱动（删除时在役/启动关键仍会被护栏跳过）。</summary>
        private void SelectOld() => SelectWhere(d => d.IsOld, "已勾选 {0} 个旧版驱动包（在役/启动关键会在删除时被跳过保护）");

        private void UpdateSelStatus()
        {
            int sel = _drivers.Count(x => x.Selected);
            int oldCnt = _drivers.Count(x => x.IsOld && !x.InUse);
            int inUseCnt = _drivers.Count(x => x.InUse);
            double selSize = _drivers.Where(x => x.Selected).Sum(x => x.SizeMB);
            _selStatus.Text = $"已选 {sel}/{_drivers.Count} 项  ·  可清理旧版 {oldCnt} 个  ·  在役受保护 {inUseCnt} 个  ·  已选占用 {DriverStore.FormatSize(selSize)}";
            _selStatus.Foreground = sel > 0 ? _accent : _dim;
        }

        /// <summary>重新枚举（按当前引擎）并刷新表格与状态（在后台线程调用，内部切回 UI 线程更新）。</summary>
        private void Reenumerate(Action<string> logf)
        {
            var list = DriverStore.Enumerate(_engine, logf);
            DriverStore.MarkOldVersions(list);
            DriverStore.DetectInUse(list, logf);
            DriverStore.EstimateSizes(list, logf);
            try { Dispatcher.Invoke(() =>
            {
                _drivers = list;
                ApplyGrouping();
                UpdateSelStatus();
            }); } catch { /* 窗口已关闭，忽略 */ }
        }

        /// <summary>按当前分组方式应用到 _drivers，并设置为 DataGrid 数据源。</summary>
        private void ApplyGrouping()
        {
            var view = ConfigureGroupDescriptions();
            if (view == null) return;
            // 刷新/重建视图时保留当前排序（先清后加，保证仅一个排序键、不重复累积）
            if (_currentSortDir.HasValue && !string.IsNullOrEmpty(_currentSortPath))
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(_currentSortPath, _currentSortDir.Value));
            }
            _dg.ItemsSource = view;
        }

        /// <summary>分组方式切换时刷新现有视图（不重新枚举），并显式重放当前排序。</summary>
        private void UpdateGrouping()
        {
            var view = ConfigureGroupDescriptions();
            if (view != null && _currentSortDir.HasValue && !string.IsNullOrEmpty(_currentSortPath))
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(_currentSortPath, _currentSortDir.Value));
            }
            view?.Refresh();
        }

        /// <summary>列头点击排序：无→升序→降序→无循环；支持分组视图内排序。</summary>
        private void OnDataGridSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (!(e.Column is DataGridTextColumn col) || string.IsNullOrEmpty(col.SortMemberPath))
                return;

            var path = col.SortMemberPath;
            ListSortDirection? nextDir;
            if (_currentSortPath == path)
            {
                if (_currentSortDir == ListSortDirection.Ascending) nextDir = ListSortDirection.Descending;
                else if (_currentSortDir == ListSortDirection.Descending) nextDir = null;
                else nextDir = ListSortDirection.Ascending;
            }
            else
            {
                nextDir = ListSortDirection.Ascending;
            }

            var view = CollectionViewSource.GetDefaultView(_drivers) as ListCollectionView;
            if (view != null)
            {
                view.SortDescriptions.Clear();
                if (nextDir.HasValue)
                    view.SortDescriptions.Add(new SortDescription(path, nextDir.Value));
                view.Refresh();
            }

            UpdateSortHeader(path, nextDir);
        }

        /// <summary>刷新列头排序箭头显示。</summary>
        private void UpdateSortHeader(string activePath, ListSortDirection? dir)
        {
            foreach (var col in _dg.Columns.OfType<DataGridTextColumn>())
            {
                if (!_originalHeaders.TryGetValue(col, out var original)) continue;
                col.Header = col.SortMemberPath == activePath && dir.HasValue
                    ? MakeSortHeader(original, dir.Value == ListSortDirection.Ascending)
                    : (object)original;
            }
            _currentSortPath = activePath;
            _currentSortDir = dir;
        }

        /// <summary>构造带线条排序箭头的列头。</summary>
        private UIElement MakeSortHeader(string text, bool ascending)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var arrow = UiShapes.MakeChevron(_dim, ascending ? "M 0,4 L 4,0 L 8,4" : "M 0,0 L 4,4 L 8,0");
            arrow.Margin = new Thickness(6, 0, 0, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(arrow);
            return sp;
        }

        /// <summary>根据 _groupMode 配置 CollectionView 的分组描述。</summary>
        private ICollectionView ConfigureGroupDescriptions()
        {
            if (_drivers == null) return null;
            var view = CollectionViewSource.GetDefaultView(_drivers);
            view.GroupDescriptions.Clear();
            switch (_groupMode)
            {
                case GroupMode.Class: view.GroupDescriptions.Add(new PropertyGroupDescription("ClassDescription")); break;
                case GroupMode.Provider: view.GroupDescriptions.Add(new PropertyGroupDescription("Provider")); break;
            }
            return view;
        }

        // 枚举进行中标记，防止预加载/进入页面/切换引擎/手动刷新并发重复枚举。
        private bool _enumerating;

        /// <summary>后台刷新驱动列表。幂等：若正在枚举则跳过（避免重复并发枚举）。可由主窗口在预加载与每次进入页面时调用。</summary>
        internal void Refresh()
        {
            if (_enumerating) return;
            _enumerating = true;
            RunInBg(logf =>
            {
                try { Reenumerate(logf); }
                finally { _enumerating = false; }
            }, "驱动列表已刷新");
        }

        private void ExportSelected()
        {
            var targets = _drivers.Where(x => x.Selected).ToList();
            bool exportAll = targets.Count == 0;
            if (exportAll)
            {
                var r = System.Windows.MessageBox.Show(
                    "当前未勾选任何驱动。是否备份全部已安装驱动包？\n（将导出到您选择的目录，可能包含较多文件）",
                    "备份全部驱动", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                targets = _drivers.ToList();
            }
            if (targets.Count == 0) { System.Windows.MessageBox.Show("没有可导出的驱动。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.Description = "选择驱动备份保存目录";
                fbd.ShowNewFolderButton = true;
                if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string dir = fbd.SelectedPath;
                RunInBg(logf => DriverStore.Export(targets, dir, logf), "驱动备份完成");
            }
        }

        /// <summary>添加驱动包：选择 .inf 文件后调用 pnputil /add-driver。</summary>
        private void AddDriverDialog()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "驱动信息文件 (*.inf)|*.inf|所有文件 (*.*)|*.*",
                Title = "选择要添加的驱动 INF 文件"
            };
            if (dlg.ShowDialog() != true) return;
            string inf = dlg.FileName;
            RunInBg(logf =>
            {
                DriverStore.AddDriver(inf, logf);
                // 添加后重新枚举当前引擎
                Reenumerate(logf);
            }, "驱动包已添加");
        }

        /// <summary>把勾选的驱动包安装到匹配的设备。</summary>
        private void InstallSelected()
        {
            var targets = _drivers.Where(x => x.Selected).ToList();
            if (targets.Count == 0) { System.Windows.MessageBox.Show("请先勾选要安装的驱动包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            RunInBg(logf =>
            {
                foreach (var d in targets) DriverStore.InstallDriver(d.OemName, logf);
            }, "驱动安装完成");
        }

        private void DeleteSelected() => DeleteWithConfirm(false);
        private void ForceDelete() => DeleteWithConfirm(true);

        /// <summary>计算勾选目标中在役/启动关键/可删除数量（与删除护栏口径一致）。</summary>
        private void ComputeTargets(out int blocked, out int bootCrit, out int toDelete)
        {
            var targets = _drivers.Where(x => x.Selected).ToList();
            blocked = targets.Count(x => x.InUse);
            bootCrit = targets.Count(x => x.BootCritical && !_includeBootCritical);
            toDelete = targets.Count - blocked - bootCrit;
        }

        /// <summary>二次确认删除：含 toDelete==0 护栏与 force 分支的差异化提示文案；返回是否确认继续。</summary>
        private bool ConfirmDelete(bool force, int toDelete, int blocked, int bootCrit)
        {
            if (toDelete == 0)
            {
                if (force)
                    System.Windows.MessageBox.Show(
                        "所选驱动全部为「在役·保护」或「启动关键」（未勾选包含）状态。\n强制删除也无法移除正在被设备使用的驱动，启动关键驱动需先勾选「包含启动关键驱动程序」。",
                        "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    System.Windows.MessageBox.Show(
                        "所选驱动全部为「在役·保护」或「启动关键」（未勾选包含）状态，禁止删除（删除会导致对应设备失效或系统无法启动）。\n请改选「旧版可清」的驱动。",
                        "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string blockedMsg = blocked > 0
                ? (force ? $"（{blocked} 个在役驱动仍会被跳过，强制删除也无法移除正在使用的设备驱动）"
                        : $"（另有 {blocked} 个在役驱动会被自动跳过保护）")
                : "";
            string bootCritMsg = bootCrit > 0
                ? (force ? $"（{bootCrit} 个启动关键驱动会被跳过，除非勾选包含启动关键）"
                        : $"（另有 {bootCrit} 个启动关键驱动会被自动跳过保护）")
                : "";

            if (force)
            {
                var r = System.Windows.MessageBox.Show(
                    $"即将【强制】删除 {toDelete} 个驱动包{blockedMsg}{bootCritMsg}。\n\n⚠ 强制删除会移除仍在被引用的驱动文件，可能导致相关硬件功能丢失或系统无法启动；此操作不可恢复。\n确定继续吗？",
                    "确认强制删除", MessageBoxButton.YesNo, MessageBoxImage.Error);
                return r == MessageBoxResult.Yes;
            }
            else
            {
                var r = System.Windows.MessageBox.Show(
                    $"即将删除 {toDelete} 个驱动包{blockedMsg}{bootCritMsg}。\n\n删除操作不可恢复，且可能影响相关硬件的驱动回退能力。\n确定继续吗？",
                    "确认删除驱动", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return r == MessageBoxResult.Yes;
            }
        }

        /// <summary>删除/强制删除共享流程：校验选择 → 计算目标 → 二次确认 → 后台删除并重新枚举。</summary>
        private void DeleteWithConfirm(bool force)
        {
            var targets = _drivers.Where(x => x.Selected).ToList();
            if (targets.Count == 0) { System.Windows.MessageBox.Show("请先勾选要删除的驱动包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            ComputeTargets(out int blocked, out int bootCrit, out int toDelete);
            if (!ConfirmDelete(force, toDelete, blocked, bootCrit)) return;

            RunInBg(logf =>
            {
                DriverStore.Delete(targets, logf, force: force, includeBootCritical: _includeBootCritical);
                // 删除后重新枚举以刷新状态
                Reenumerate(logf);
            }, force ? "强制删除完成" : "驱动删除完成");
        }

        // ---- 后台任务编排 ----

        /// <summary>当前后台任务的取消源：发起新任务前先取消上一个仍在运行的任务。</summary>
        private CancellationTokenSource _bgCts;

        /// <summary>
        /// 取消仍在运行的后台任务。修复：原实现 new Thread(...) 后无任何取消手段，
        /// 刷新/切换引擎/删除等并发触发时，旧线程仍会跑完并把过期结果写回 UI 与状态栏。
        /// </summary>
        private void CancelBgWork()
        {
            var cts = _bgCts;
            if (cts == null) return;
            _bgCts = null;
            try { cts.Cancel(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        private void RunInBg(Action<Action<string>> work, string done)
        {
            // 新任务开始前先取消上一个任务，避免旧任务结果覆盖新结果
            CancelBgWork();
            var cts = new CancellationTokenSource();
            _bgCts = cts;
            var token = cts.Token;

            var disp = Dispatcher;
            // 窗口关闭后 Dispatcher 关停，BeginInvoke/Invoke 均抛 InvalidOperationException；
            // 后台线程未处理异常在 net48 会直接终止进程。safeUi 统一兜底：UI 更新静默忽略。
            Action<Action> safeUi = a => { try { disp.BeginInvoke(a); } catch { /* 窗口已关闭，忽略 */ } };
            Action<string> logf = s =>
            {
                if (token.IsCancellationRequested) return;  // 任务已被新任务取代，不再回写过期日志
                safeUi(() =>
                {
                    if (_externalLog != null)
                    {
                        _externalLog.Visibility = Visibility.Visible;
                        _externalLog.AppendText(s + "\r\n");
                        _externalLog.ScrollToEnd();
                    }
                    else if (_log != null)
                    {
                        _log.Visibility = Visibility.Visible;
                        _log.AppendText(s + "\r\n");
                        _log.ScrollToEnd();
                    }
                });
            };
            new Thread(() =>
            {
                try
                {
                    work(logf);
                    if (token.IsCancellationRequested) return;  // 已被新任务取代，不再回写状态
                    safeUi(() => { _owner?.SetStatus(done); });
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) return;  // 取消导致的异常无需提示
                    safeUi(() =>
                    {
                        logf("[!] 异常: " + ex.Message);
                        _owner?.SetStatus("执行出错");
                    });
                }
                finally
                {
                    // 只清理自己那份取消源，避免误清新任务的
                    if (_bgCts == cts) _bgCts = null;
                }
            }) { IsBackground = true }.Start();   // IsBackground=true：窗口关闭后不再因前台线程未结束而拖住进程退出
        }

        // ---- 主题样式复用：表头/单元格透明底样式统一由 MainWindow.MakeDataGridStyles 提供（见修复 #4） ----

        private static DataGridTextColumn MakeTextColumn(string header, string binding, string sortMemberPath, DataGridLength width, double minWidth, Style elementStyle)
        {
            // 每列独立 Style，以便绑定该列对应的 ToolTip（显示完整内容并跟随鼠标）
            var style = new Style(typeof(TextBlock));
            foreach (Setter setter in elementStyle.Setters)
                style.Setters.Add(setter);
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(binding)));
            style.Setters.Add(new Setter(ToolTipService.PlacementProperty, PlacementMode.Mouse));

            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                SortMemberPath = sortMemberPath,
                Width = width,
                MinWidth = minWidth,
                ElementStyle = style
            };
        }

        /// <summary>创建文本列并记录原始标题，用于排序箭头显示。</summary>
        private void AddSortableTextColumn(string header, string binding, string sortMemberPath, DataGridLength width, double minWidth, Style elementStyle)
        {
            var col = MakeTextColumn(header, binding, sortMemberPath, width, minWidth, elementStyle);
            _originalHeaders[col] = header;
            _dg.Columns.Add(col);
        }

        // 去重：本类原有的 MakeBtnRow 与 MainWindow.Helpers.cs 中的实现逐行重复，
        // 已删除本类拷贝，统一调用 MainWindow 的 internal static 版本（唯一真源）。
    }
}
