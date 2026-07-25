using System;
using System.Globalization;
using System.Windows.Data;

using WpfBrushes = System.Windows.Media.Brushes;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace Iziregi.Test;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = (value?.ToString() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(hex))
            return WpfBrushes.Transparent;

        try
        {
            return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return WpfBrushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
