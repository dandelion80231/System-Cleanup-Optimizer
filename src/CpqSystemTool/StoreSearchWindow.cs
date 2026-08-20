using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CpqSystemTool
{
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
