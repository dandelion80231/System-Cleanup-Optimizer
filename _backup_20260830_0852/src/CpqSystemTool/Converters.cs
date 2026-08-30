using System;
using System.Windows.Data;
using System.Windows.Media;

/// <summary>bool? → Brush 转换器：true 用 TrueBrush，其余用 FalseBrush。用于 CheckBox.IsChecked 绑定到名称颜色。</summary>
internal class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; }
    public Brush FalseBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
