using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 第三档·旧资产删除确认对话框。
    /// 列出扫描发现的旧资产候选（大且久未使用 / 已知停用工具数据），每项默认不勾选，
    /// 用户须逐项确认（二次确认）后才能删除；未勾选项不会删除。
    /// </summary>
    internal class Tier3ConfirmDialog : Window
    {
        public List<Cleanup.Tier3Candidate> Selected { get; private set; } = new List<Cleanup.Tier3Candidate>();
        private readonly List<Cleanup.Tier3Candidate> _all;
        private readonly List<CheckBox> _boxes = new List<CheckBox>();

        public Tier3ConfirmDialog(MainWindow owner, List<Cleanup.Tier3Candidate> candidates)
        {
            DialogChrome.Apply(this, owner);
            _all = candidates;

            var fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            var dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            var danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));
            var accent = owner?._accent ?? new SolidColorBrush(Color.FromRgb(0x2D, 0xC8, 0x8C));

            Title = "第三档·旧资产删除确认";
            Width = 760;
            Height = 540;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            var root = new Border
            {
                Background = windowBg,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = panelBorder,
                BorderThickness = new Thickness(1)
            };
            var sp = new StackPanel();

            // 未复用 DialogChrome.BuildTitleBar：本弹窗标题栏与之有 4 处外观差异，替换会改变观感，故保留原实现。
            // 差异：①标题带 ⚠️ 且用 danger 红、Bold/16（对话框版为 fg、SemiBold/15）；
            //      ②Padding (0,0,0,6)（对话框版为 16,12,12,12，叠加 root 的 16 会撑宽内容区）；
            //      ③关闭按钮 Margin (8,0,-8,0)（对话框版无，会使 ✕ 右移 8px）；
            //      ④无 CornerRadius（对话框版 12,12,0,0 会裁剪带负右边距的 ✕）。
            // 若要复用，需为 BuildTitleBar 增加 padding/字号/字重/按钮边距等参数，代价大于收益，暂不合并。
            var titleBar = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = panelBorder,
                Cursor = Cursors.SizeAll
            };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleTb = new TextBlock
            {
                Text = "⚠️ 第三档·旧资产删除确认",
                Foreground = danger,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleGrid.Children.Add(titleTb);
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 13,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = dim,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(8, 0, -8, 0)
            };
            closeBtn.Click += (s, e) => DialogResult = false;
            closeBtn.MouseEnter += (s, e) => closeBtn.Foreground = danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.Foreground = dim;
            Grid.SetColumn(closeBtn, 1);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject src && closeBtn.IsAncestorOf(src)) return;
                DragMove();
            };
            sp.Children.Add(titleBar);
            sp.Children.Add(new TextBlock
            {
                Text = "以下项目「多半可删」，但可能包含你的数据。请逐项勾选你要删除的项；未勾选的不会删除。删除不可恢复。",
                Foreground = dim,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 全选 / 全不选 + 计数
            var toolRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var selectAll = new CheckBox
            {
                Content = "全选（谨慎：将勾选全部候选）",
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(selectAll, Dock.Left);
            toolRow.Children.Add(selectAll);
            var countTb = new TextBlock
            {
                Foreground = dim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            toolRow.Children.Add(countTb);
            sp.Children.Add(toolRow);

            // 候选列表
            var listSv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Margin = new Thickness(0, 4, 0, 8)
            };
            var listSp = new StackPanel();
            string FormatSize(double mb)
            {
                if (mb >= 1024) return (mb / 1024.0).ToString("F1") + " GB";
                return mb.ToString("F0") + " MB";
            }

            foreach (var c in _all)
            {
                var rowBd = new Border
                {
                    BorderBrush = panelBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = Brushes.Transparent
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var chk = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 8, 0),
                    Cursor = Cursors.Hand
                };
                Grid.SetColumn(chk, 0);
                grid.Children.Add(chk);

                var info = new StackPanel();

                // 第一行：路径（左，可换行）+ 大小 / 未使用天数 / 最后活动（右，固定不被挤压）
                // 注意：必须用两列 Grid 而非 DockPanel —— DockPanel 会先测量不设宽的路径 TextBlock，
                // 长路径（深层目录）会撑满整行，导致右侧元数据拿到 0 宽度被裁掉。
                var topRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var pathTb = new TextBlock
                {
                    Text = c.Path,
                    Foreground = accent,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Cursor = Cursors.Hand,
                    TextDecorations = TextDecorations.Underline,
                    ToolTip = "点击在资源管理器中打开该目录",
                    VerticalAlignment = VerticalAlignment.Top
                };
                string pathForShell = c.Path;
                pathTb.MouseLeftButtonUp += (s, e) =>
                {
                    string path = pathForShell.Replace('/', '\\');
                    try
                    {
                        if (Directory.Exists(path))
                            Process.Start("explorer.exe", "/e,\"" + path + "\"");
                        else if (File.Exists(path))
                            Process.Start("explorer.exe", "/select,\"" + path + "\"");
                        else
                        {
                            string parent = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(parent))
                                Process.Start("explorer.exe", "/e,\"" + parent + "\"");
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("无法打开路径：" + path + "\n" + ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                // 右键菜单：复制路径
                var pathContextMenu = new ContextMenu
                {
                    Background = windowBg,
                    Foreground = fg,
                    BorderBrush = panelBorder,
                    BorderThickness = new Thickness(1)
                };
                var copyItem = new MenuItem { Header = "复制路径", Cursor = Cursors.Hand };
                copyItem.Click += (s, e) =>
                {
                    try { Clipboard.SetText(pathForShell); }
                    catch (Exception ex) { MessageBox.Show("无法复制路径：" + ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                pathContextMenu.Items.Add(copyItem);
                pathTb.ContextMenu = pathContextMenu;
                Grid.SetColumn(pathTb, 0);
                topRow.Children.Add(pathTb);
                var rightMeta = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                rightMeta.Children.Add(new TextBlock
                {
                    Text = FormatSize(c.SizeMB),
                    Foreground = dim,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                });
                rightMeta.Children.Add(new TextBlock
                {
                    Text = $"  ·  已 {c.DaysUnused} 天未使用",
                    Foreground = dim,
                    FontSize = 11.5,
                    Margin = new Thickness(6, 1, 0, 0)
                });
                rightMeta.Children.Add(new TextBlock
                {
                    Text = $"  ·  最后活动 {c.LastActivity:yyyy-MM-dd}",
                    Foreground = dim,
                    FontSize = 11.5,
                    Margin = new Thickness(6, 1, 0, 0)
                });
                Grid.SetColumn(rightMeta, 1);
                topRow.Children.Add(rightMeta);
                info.Children.Add(topRow);
                if (!string.IsNullOrEmpty(c.Description))
                {
                    info.Children.Add(new TextBlock
                    {
                        Foreground = dim,
                        FontSize = 11.5,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        Text = "说明：" + c.Description
                    });
                }
                Grid.SetColumn(info, 1);
                grid.Children.Add(info);

                rowBd.Child = grid;
                listSp.Children.Add(rowBd);
                _boxes.Add(chk);
            }
            listSv.Content = listSp;
            sp.Children.Add(listSv);

            void UpdateCount()
            {
                int n = _boxes.Count(x => x.IsChecked == true);
                double sum = _all.Zip(_boxes, (it, b) => b.IsChecked == true ? it.SizeMB : 0).Sum();
                countTb.Text = $"已选 {n}/{_boxes.Count} 项（约 {sum:F0} MB）";
            }
            selectAll.Click += (s, e) =>
            {
                bool v = selectAll.IsChecked == true;
                foreach (var b in _boxes) b.IsChecked = v;
                UpdateCount();
            };
            foreach (var b in _boxes)
            {
                b.Checked += (s, e) => UpdateCount();
                b.Unchecked += (s, e) => UpdateCount();
            }
            UpdateCount();

            // 底部按钮（右对齐、紧密排列）
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var btnOk = new Button
            {
                Content = "删除选中项",
                Width = 140,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Background = danger,
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent
            };
            btnOk.Click += (s, e) =>
            {
                for (int i = 0; i < _boxes.Count; i++)
                    if (_boxes[i].IsChecked == true) Selected.Add(_all[i]);
                DialogResult = true;
            };
            var btnCancel = new Button
            {
                Content = "取消",
                Width = 110,
                Height = 34,
                Background = panelBorder,
                Foreground = fg,
                BorderBrush = panelBorder
            };
            btnCancel.Click += (s, e) => DialogResult = false;
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            sp.Children.Add(btnRow);

            root.Child = sp;
            Content = root;
        }
    }
}
