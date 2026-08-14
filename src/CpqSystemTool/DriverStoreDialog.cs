using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 驱动管理对话框（参考 RAPR / Driver Store Explorer）。
    /// 列出系统已安装驱动包，支持：刷新枚举、勾选旧版冗余可清理驱动、导出备份、删除选中。
    /// 安全护栏：正在使用的驱动（InUse）禁止删除，删除前二次确认；默认不暴露 /force。
    /// 后台扫描不卡 UI。
    /// </summary>
    internal class DriverStoreDialog : Window
    {
        private readonly MainWindow _owner;
        private readonly DataGrid _dg;
        private List<DriverStore.DriverInfo> _drivers = new List<DriverStore.DriverInfo>();
        private readonly TextBox _log;
        private readonly TextBlock _selStatus;
        private readonly CheckBox _headerCb;

        // 主题笔刷（来自主窗口）
        private readonly SolidColorBrush _fg, _dim, _panelBorder, _windowBg, _danger, _accent,
            _warn, _success, _rowSelected, _rowHover, _bgDeep, _btnSecBg, _btnSecFg, _btnPriFg, _inputBg, _inputFg;

        public DriverStoreDialog(MainWindow owner)
        {
            _owner = owner;
            DialogChrome.Apply(this, owner);

            _fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            _dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            _panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            _windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            _danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));
            _accent = owner?._accent ?? new SolidColorBrush(Color.FromRgb(0x2D, 0xC8, 0x8C));
            _warn = owner?._warnOrange ?? new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D));
            _success = owner?._successGreen ?? new SolidColorBrush(Color.FromRgb(0x2D, 0xC8, 0x8C));
            _rowSelected = owner?._rowSelected ?? _accent;
            _rowHover = owner?._rowHover ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x26, 0x30));
            _bgDeep = owner?._bgDeep ?? new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x16));
            _btnSecBg = owner?._btnSecondaryBg ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            _btnSecFg = owner?._btnSecondaryFg ?? new SolidColorBrush(Color.FromRgb(0xD0, 0xD6, 0xDE));
            _btnPriFg = owner?._btnPrimaryFg ?? new SolidColorBrush(Colors.White);
            _inputBg = owner?._inputBg ?? Brushes.Transparent;
            _inputFg = owner?._inputFg ?? _fg;

            Title = "驱动管理";
            Width = 880;
            Height = 640;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            var root = new Border
            {
                Background = _windowBg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                }
            };
            var stack = new StackPanel();

            // 标题栏
            stack.Children.Add(DialogChrome.BuildTitleBar(this, "驱动管理（清理老旧冗余 / 备份导出）", _fg, _dim, _danger, _panelBorder));

            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

            // 说明
            body.Children.Add(new TextBlock
            {
                Text = "列出系统已安装的驱动包。标注「旧版可清」的多为该驱动的历史版本，可放心清理以节省空间；标注「在役·保护」的正在被设备使用，禁止删除。删除不可恢复，请按需勾选。",
                FontSize = 11.5,
                Foreground = _dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // 工具栏
            var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var refreshBtn = owner != null ? owner.Btn("刷新", true, () => Refresh(), 96) : new Button { Content = "刷新", Width = 96 };
            var selOldBtn = owner != null ? owner.Btn("全选可清理", false, () => SelectCleanable(), 110) : new Button { Content = "全选可清理", Width = 110 };
            var exportBtn = owner != null ? owner.Btn("导出备份", false, () => ExportSelected(), 110) : new Button { Content = "导出备份", Width = 110 };
            var delBtn = owner != null ? owner.Btn("删除选中", false, () => DeleteSelected(), 110) : new Button { Content = "删除选中", Width = 110 };
            // 删除按钮用危险色强调
            if (owner != null)
            {
                delBtn.Background = _danger;
                delBtn.Foreground = _btnPriFg;
                delBtn.BorderThickness = new Thickness(0);
            }
            toolRow.Children.Add(refreshBtn);
            toolRow.Children.Add(selOldBtn);
            toolRow.Children.Add(exportBtn);
            toolRow.Children.Add(delBtn);
            body.Children.Add(toolRow);

            // 选择状态行
            _selStatus = new TextBlock
            {
                FontSize = 11.5,
                Foreground = _dim,
                Margin = new Thickness(0, 0, 0, 6)
            };
            body.Children.Add(_selStatus);

            // 数据网格
            _dg = new DataGrid
            {
                AutoGenerateColumns = false,
                Background = Brushes.Transparent,
                Foreground = _fg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 0),
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                RowBackground = Brushes.Transparent,
                AlternatingRowBackground = Brushes.Transparent,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = _panelBorder,
                SelectionMode = DataGridSelectionMode.Single,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                EnableRowVirtualization = false
            };
            StyleHeader(_dg, _fg, _panelBorder);
            StyleCells(_dg, _fg);

            // 选择列（表头全选 + 单元格绑定 Selected）
            var selCol = new DataGridTemplateColumn { Header = MakeHeaderCheckBox(out _headerCb), Width = new DataGridLength(46) };
            var selFactory = new FrameworkElementFactory(typeof(CheckBox));
            selFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            selFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            selFactory.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => UpdateSelStatus()));
            selFactory.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => UpdateSelStatus()));
            selCol.CellTemplate = new DataTemplate { VisualTree = selFactory };
            _dg.Columns.Add(selCol);

            _dg.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding("StatusText"), Width = new DataGridLength(72) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "供应商", Binding = new Binding("Provider"), Width = new DataGridLength(130) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "类", Binding = new Binding("Class"), Width = new DataGridLength(90) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "版本", Binding = new Binding("Version"), Width = new DataGridLength(110) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "日期", Binding = new Binding("DateText"), Width = new DataGridLength(90) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "占用", Binding = new Binding("SizeText"), Width = new DataGridLength(90) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "发布名(INF)", Binding = new Binding("OemName"), Width = new DataGridLength(90) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "原始名", Binding = new Binding("OriginalName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });

            // 行样式：在役红 / 旧版橙 / 选中青，悬停高亮
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.Transparent));
            rowStyle.Setters.Add(new Setter(DataGridRow.ForegroundProperty, _fg));
            rowStyle.Setters.Add(new Setter(DataGridRow.BorderBrushProperty, Brushes.Transparent));
            _dg.RowStyle = rowStyle;
            _dg.LoadingRow += (s, e) =>
            {
                var di = e.Row.Item as DriverStore.DriverInfo;
                if (di == null) return;
                e.Row.Foreground = di.InUse ? _danger : (di.IsOld ? _warn : _fg);
            };
            body.Children.Add(_dg);

            // 日志区
            _log = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = _fg,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas, 'Courier New'"),
                FontSize = 11,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 90,
                MaxHeight = 150,
                Margin = new Thickness(0, 10, 0, 0),
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
                Margin = new Thickness(0, 10, 0, 0)
            };
            body.Children.Add(logBorder);

            // 底部按钮
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var closeBtn = owner != null ? owner.Btn("关闭", false, () => { DialogResult = true; }, 100) : new Button { Content = "关闭", Width = 100 };
            btnRow.Children.Add(closeBtn);
            body.Children.Add(btnRow);

            stack.Children.Add(body);
            root.Child = stack;
            Content = root;

            UpdateSelStatus();
            // 打开即刷新枚举
            Refresh();
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

        private void SelectCleanable()
        {
            foreach (var d in _drivers) d.Selected = d.IsOld && !d.InUse;
            _dg.Items.Refresh();
            UpdateSelStatus();
            int n = _drivers.Count(x => x.Selected);
            _owner?.SetStatus($"已勾选 {n} 个可清理（旧版且未在使用）的驱动包");
        }

        private void UpdateSelStatus()
        {
            int sel = _drivers.Count(x => x.Selected);
            int oldCnt = _drivers.Count(x => x.IsOld && !x.InUse);
            int inUseCnt = _drivers.Count(x => x.InUse);
            _selStatus.Text = $"已选 {sel}/{_drivers.Count} 项  ·  可清理旧版 {oldCnt} 个  ·  在役受保护 {inUseCnt} 个";
            _selStatus.Foreground = sel > 0 ? _accent : _dim;
        }

        private void Refresh()
        {
            RunInBg(logf =>
            {
                var list = DriverStore.Enumerate(logf);
                DriverStore.MarkOldVersions(list);
                DriverStore.DetectInUse(list, logf);
                DriverStore.EstimateSizes(list, logf);
                Dispatcher.Invoke(() =>
                {
                    _drivers = list;
                    _dg.ItemsSource = null;
                    _dg.ItemsSource = _drivers;
                    UpdateSelStatus();
                });
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

        private void DeleteSelected()
        {
            var targets = _drivers.Where(x => x.Selected).ToList();
            if (targets.Count == 0) { System.Windows.MessageBox.Show("请先勾选要删除的驱动包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            int blocked = targets.Count(x => x.InUse);
            int toDelete = targets.Count - blocked;
            if (toDelete == 0)
            {
                System.Windows.MessageBox.Show(
                    "所选驱动全部为「在役·保护」状态，禁止删除（删除会导致对应设备失效）。\n请改选「旧版可清」的驱动。",
                    "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var r = System.Windows.MessageBox.Show(
                $"即将删除 {toDelete} 个驱动包" + (blocked > 0 ? $"（另有 {blocked} 个在役驱动会被自动跳过保护）" : "") + "。\n\n删除操作不可恢复，且可能影响相关硬件的驱动回退能力。\n确定继续吗？",
                "确认删除驱动", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            RunInBg(logf =>
            {
                DriverStore.Delete(targets, logf);
                // 删除后重新枚举以刷新状态
                var list = DriverStore.Enumerate(logf);
                DriverStore.MarkOldVersions(list);
                DriverStore.DetectInUse(list, logf);
                DriverStore.EstimateSizes(list, logf);
                Dispatcher.Invoke(() =>
                {
                    _drivers = list;
                    _dg.ItemsSource = null;
                    _dg.ItemsSource = _drivers;
                    UpdateSelStatus();
                });
            }, "驱动删除完成");
        }

        // ---- 后台任务编排（独立窗口内自建，不依赖 MainWindow.RunInBg） ----
        private void RunInBg(Action<Action<string>> work, string done)
        {
            var disp = Dispatcher;
            Action<string> logf = s => disp.BeginInvoke(() =>
            {
                if (_log != null)
                {
                    _log.Visibility = Visibility.Visible;
                    _log.AppendText(s + "\r\n");
                    _log.ScrollToEnd();
                }
            });
            new Thread(() =>
            {
                try
                {
                    work(logf);
                    disp.Invoke(() => { _owner?.SetStatus(done); });
                }
                catch (Exception ex)
                {
                    disp.Invoke(() =>
                    {
                        logf("[!] 异常: " + ex.Message);
                        _owner?.SetStatus("执行出错");
                    });
                }
            }).Start();
        }

        // ---- 主题样式复用（与维护页 DataGrid 一致） ----
        private static void StyleHeader(DataGrid dg, Brush fg, Brush border)
        {
            var h = new Style(typeof(DataGridColumnHeader));
            h.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            h.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            h.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            h.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            dg.ColumnHeaderStyle = h;
        }

        private static void StyleCells(DataGrid dg, Brush fg)
        {
            var c = new Style(typeof(DataGridCell));
            c.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
            c.Setters.Add(new Setter(DataGridCell.ForegroundProperty, fg));
            c.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent));
            dg.CellStyle = c;
        }
    }
}
