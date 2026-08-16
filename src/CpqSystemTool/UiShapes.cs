using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
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

        // =====================================================================
        // 下拉框（Popup / ComboBox）层级与主题修复助手
        // =====================================================================

        // ---- Popup 不再浮到最顶层（剥离 WS_EX_TOPMOST）----
        private const int GwlExstyle = -20;
        private const int WsExTopmost = 0x00000008;
        private const int WmWindowposchanged = 0x0047;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static void StripTopmost(IntPtr hwnd)
        {
            IntPtr ex = GetWindowLongPtr(hwnd, GwlExstyle);
            if (((long)ex & WsExTopmost) != 0)
                SetWindowLongPtr(hwnd, GwlExstyle, (IntPtr)((long)ex & ~WsExTopmost));
        }

        /// <summary>
        /// 修复 WPF Popup（AllowsTransparency=true 时）被宿主为独立顶层 HWND 并带 WS_EX_TOPMOST，
        /// 导致下拉菜单"浮到最顶层、压在所有窗口（含其他应用）之上"的问题。
        /// 打开时剥离 WS_EX_TOPMOST，并挂 Hook；WPF 在 WM_WINDOWPOSCHANGED 可能重新置顶，持续剥离以保持正常层级。
        /// </summary>
        public static void DisablePopupTopmost(Popup popup)
        {
            popup.Opened += (s, e) =>
            {
                var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                if (source == null) return;
                StripTopmost(source.Handle);
                source.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == WmWindowposchanged) // WPF 可能重新置顶，持续剥离
                        StripTopmost(h);
                    return IntPtr.Zero;
                });
            };
        }

        // ---- ComboBox 深/浅色自适应（闭合框 + 下拉弹层背景/字体统一跟随主题）----
        // 模板与项样式均引用 DynamicResource 键，键在调用方 combo.Resources 中按当前主题注入具体笔刷，
        // 因此页面随主题重建时会自动套用正确色彩（与自定义 Popup 下拉一致）。
        private const string ComboBoxTemplateXaml = @"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" TargetType=""ComboBox"">
  <Grid x:Name=""MainGrid"" SnapsToDevicePixels=""True"">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width=""*""/>
      <ColumnDefinition MinWidth=""17"" Width=""Auto""/>
    </Grid.ColumnDefinitions>
    <Border x:Name=""Border"" Grid.ColumnSpan=""2""
            Background=""{TemplateBinding Background}""
            BorderBrush=""{TemplateBinding BorderBrush}""
            BorderThickness=""{TemplateBinding BorderThickness}""
            CornerRadius=""4"">
      <Grid>
        <ContentPresenter x:Name=""ContentPresenter""
            Content=""{TemplateBinding SelectionBoxItem}""
            ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
            ContentStringFormat=""{TemplateBinding SelectionBoxItemStringFormat}""
            HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}""
            VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""
            IsHitTestVisible=""False""
            Margin=""6,2,22,2""
            SnapsToDevicePixels=""{TemplateBinding SnapsToDevicePixels}""/>
        <ToggleButton x:Name=""ToggleButton"" Grid.ColumnSpan=""2"" Focusable=""False"" ClickMode=""Press""
            IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
          <ToggleButton.Template>
            <ControlTemplate TargetType=""ToggleButton"">
              <Border Background=""Transparent"">
                <Path Data=""M0,0 L4,4 L8,0"" Fill=""Transparent""
                      Stroke=""{DynamicResource ComboBoxArrow}"" StrokeThickness=""1.2""
                      StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""
                      HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,10,0""/>
              </Border>
            </ControlTemplate>
          </ToggleButton.Template>
        </ToggleButton>
      </Grid>
    </Border>
    <Popup x:Name=""PART_Popup"" AllowsTransparency=""False"" Grid.ColumnSpan=""2""
           IsOpen=""{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}""
           Placement=""Bottom"" Margin=""1,1,-1,-1"" VerticalOffset=""1"">
      <Border x:Name=""DropDownBorder""
              Background=""{DynamicResource ComboBoxPopupBg}""
              BorderBrush=""{DynamicResource ComboBoxPopupBorder}""
              BorderThickness=""1"" CornerRadius=""0""
              MaxHeight=""{TemplateBinding MaxDropDownHeight}""
              MinWidth=""{Binding ActualWidth, ElementName=Border}"">
        <ScrollViewer>
          <ItemsPresenter x:Name=""ItemsPresenter"" KeyboardNavigation.DirectionalNavigation=""Contained"" SnapsToDevicePixels=""{TemplateBinding SnapsToDevicePixels}""/>
        </ScrollViewer>
      </Border>
    </Popup>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""HasItems"" Value=""False"">
      <Setter TargetName=""DropDownBorder"" Property=""MinHeight"" Value=""40""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private const string ComboBoxItemStyleXaml = @"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" TargetType=""ComboBoxItem"">
  <Setter Property=""Foreground"" Value=""{DynamicResource ComboBoxItemFg}""/>
  <Setter Property=""Padding"" Value=""8,6,8,6""/>
  <Setter Property=""HorizontalContentAlignment"" Value=""Left""/>
  <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
  <Setter Property=""Background"" Value=""Transparent""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ComboBoxItem"">
        <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" SnapsToDevicePixels=""True""
                Padding=""{TemplateBinding Padding}"">
          <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}""
                            VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""
                            SnapsToDevicePixels=""{TemplateBinding SnapsToDevicePixels}""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""{DynamicResource ComboBoxItemHoverBg}""/>
          </Trigger>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""{DynamicResource ComboBoxItemSelectedBg}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        /// <summary>
        /// 让 ComboBox 的背景、字体、边框以及下拉弹层（含选中/悬浮态）统一跟随深/浅色主题笔刷。
        /// 通过自定义 ControlTemplate（闭合框 + PART_Popup 弹层均引用主题键）与 ComboBoxItem 样式实现，
        /// 替代默认跟随系统色的 Aero2 模板（深模式下弹层为刺眼的白色）。
        /// bg/fg/border = 闭合框；popupBg/popupBorder = 弹层；itemFg/hover/selected = 项文字与高亮；arrow = 箭头描边。
        /// </summary>
        public static void ApplyComboBoxTheme(ComboBox combo,
            Brush bg, Brush fg, Brush border,
            Brush popupBg, Brush popupBorder,
            Brush itemFg, Brush itemHoverBg, Brush itemSelectedBg,
            Brush arrow)
        {
            combo.Background = bg;
            combo.Foreground = fg;
            combo.BorderBrush = border;
            combo.BorderThickness = new Thickness(1);
            combo.MaxDropDownHeight = 280;

            var r = combo.Resources;
            r["ComboBoxPopupBg"] = popupBg;
            r["ComboBoxPopupBorder"] = popupBorder;
            r["ComboBoxItemFg"] = itemFg;
            r["ComboBoxItemHoverBg"] = itemHoverBg;
            r["ComboBoxItemSelectedBg"] = itemSelectedBg;
            r["ComboBoxArrow"] = arrow;

            combo.Template = (ControlTemplate)XamlReader.Parse(ComboBoxTemplateXaml);
            combo.ItemContainerStyle = (Style)XamlReader.Parse(ComboBoxItemStyleXaml);
        }
    }
}
