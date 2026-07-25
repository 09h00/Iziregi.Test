using System;
using System.Globalization;
using System.Windows.Data;

namespace Iziregi.Test;

public sealed class BoolToActiveStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "Actif" : "Désactivé";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
