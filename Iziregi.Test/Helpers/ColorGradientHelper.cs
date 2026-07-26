using System;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfGradientStop = System.Windows.Media.GradientStop;
using WpfRadialGradientBrush = System.Windows.Media.RadialGradientBrush;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Iziregi.Test.Helpers;

// ✅ Degrade optionnel EN PLUS de la couleur pleine (25.07.2026, demande de Joe) : chaque
// entreprise/categorie garde une seule couleur choisie (ColorHex, inchange), mais une case
// "Degrade" (IsGradient, colonne dediee en base) decide si elle s'affiche pleine ou en
// degrade (vers une teinte plus claire de la meme couleur, calculee automatiquement).
public static class ColorGradientHelper
{
    public static WpfBrush? BuildBrush(string? hex, bool isGradient)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(hex.Trim());
            return isGradient ? BuildGradientBrush(color) : new WpfSolidColorBrush(color);
        }
        catch
        {
            return null;
        }
    }

    public static WpfBrush BuildGradientBrush(WpfColor baseColor)
    {
        // ✅ Couleur pleine au centre (pour rester lisible derriere le texte), degrade vers
        // le noir sur les 4 bords (25.07.2026, demande de Joe : le rendu vers le blanc
        // n'etait pas concluant, essai avec le noir a la place).
        var black = Darken(baseColor, 1.0);

        var brush = new WpfRadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.9,
            RadiusY = 0.6
        };
        brush.GradientStops.Add(new WpfGradientStop(baseColor, 0.0));
        brush.GradientStops.Add(new WpfGradientStop(baseColor, 0.5));
        brush.GradientStops.Add(new WpfGradientStop(black, 1.0));
        return brush;
    }

    private static WpfColor Darken(WpfColor c, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        byte Blend(byte component) => (byte)(component * (1.0 - amount));
        return WpfColor.FromArgb(c.A, Blend(c.R), Blend(c.G), Blend(c.B));
    }
}
