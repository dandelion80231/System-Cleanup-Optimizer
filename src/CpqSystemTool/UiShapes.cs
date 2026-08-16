using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 线条箭头（开放折线 chevron）构造助手。统一下拉按钮 / 排序头 / 展开器的箭头样式，
    /// 消除多处重复的 Path 构造代码（实心三角已废弃，统一为线条箭头）。
    /// </summary>
    public static class UiShapes
    {
        private const string DefaultChevronData = "M 0,0 L 4,4 L 8,0";

        /// <summary>构造一个线条 chevron（开放折线：透明填充 + 描边 + 圆角线帽）。默认朝下。</summary>
        public static System.Windows.Shapes.Path MakeChevron(Brush stroke, string data = DefaultChevronData)
        {
            return new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Fill = Brushes.Transparent,
                Stroke = stroke,
                StrokeThickness = 1.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 8,
                Height = 4,
                Stretch = Stretch.None,
                SnapsToDevicePixels = true
            };
        }

        /// <summary>把线条 chevron 样式应用到 ControlTemplate 内的 Path FrameworkElementFactory（用于 Expander 等模板化箭头）。布局相关的对齐/边距由调用方另行设置。</summary>
        public static void ConfigureChevronFactory(FrameworkElementFactory factory, Brush stroke, string data = DefaultChevronData)
        {
            factory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse(data));
            factory.SetValue(System.Windows.Shapes.Path.FillProperty, Brushes.Transparent);
            factory.SetValue(System.Windows.Shapes.Path.StrokeProperty, stroke);
            factory.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.2);
            factory.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            factory.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            factory.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
            factory.SetValue(System.Windows.Shapes.Path.WidthProperty, 8.0);
            factory.SetValue(System.Windows.Shapes.Path.HeightProperty, 4.0);
            factory.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.None);
            factory.SetValue(System.Windows.Shapes.Path.SnapsToDevicePixelsProperty, true);
        }

        /// <summary>构造「文字 + 右侧线条箭头」两列 Grid（第 0 列 Star 放文字、第 1 列 Auto 放箭头），
        /// 用于「管理依赖」「全部分类」等下拉按钮内容，消除重复 Grid 构造。minWidth=true 时设 MinWidth=100。</summary>
        public static Grid MakeTextWithArrowGrid(UIElement text, Brush arrowStroke, bool minWidth = false)
        {
            var grid = new Grid();
            if (minWidth) grid.MinWidth = 100;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = MakeChevron(arrowStroke);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            arrow.Margin = new Thickness(6, 2, 0, 0);
            Grid.SetColumn(text, 0);
            Grid.SetColumn(arrow, 1);
            grid.Children.Add(text);
            grid.Children.Add(arrow);
            return grid;
        }
    }
}
