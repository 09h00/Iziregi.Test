using System;
using System.Globalization;
using System.Windows.Data;

using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

using Iziregi.Test.Data;

namespace Iziregi.Test;

public sealed class CompanyToForegroundBrushConverter : IValueConverter
{
    public WpfBrush LightTextBrush { get; set; } = WpfBrushes.White;
    public WpfBrush DarkTextBrush { get; set; } = WpfBrushes.Black;

    // Seuil de luminance (0..1). Plus haut => plus souvent texte blanc.
    public double Threshold { get; set; } = 0.55;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var company = (value?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(company))
                return DarkTextBrush;

            var pid = Db.GetCurrentProjectId();
            if (!pid.HasValue || pid.Value <= 0)
                return DarkTextBrush;

            var hex = Db.GetCompanyColorHex(pid.Value, company);
            if (string.IsNullOrWhiteSpace(hex))
                return DarkTextBrush;

            var color = (WpfColor)WpfColorConverter.ConvertFromString(hex);

            // Relative luminance (sRGB)
            static double SrgbToLinear(double c) => c <= 0.04045 ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4);

            var r = SrgbToLinear(color.R / 255.0);
            var g = SrgbToLinear(color.G / 255.0);
            var b = SrgbToLinear(color.B / 255.0);

            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            return luminance < Threshold ? LightTextBrush : DarkTextBrush;
        }
        catch
        {
            return DarkTextBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}