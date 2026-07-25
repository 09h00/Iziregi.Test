using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Iziregi.Test;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCFCE7"));
    private static readonly SolidColorBrush InactiveBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEE2E2"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
