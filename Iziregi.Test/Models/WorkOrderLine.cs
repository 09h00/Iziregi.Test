// File: Models/WorkOrderLine.cs
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Iziregi.Test.Models;

public class WorkOrderLine : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ✅ A4/PDF sync : limite de caractères sur 1 ligne pour "Libellé / Matériel"
    // Calé sur ce que le PDF supporte réellement en 1 ligne (tests : 42).
    private const int LabelMaxChars = 42;

    private long _id;
    public long Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    private long _workOrderId;
    public long WorkOrderId
    {
        get => _workOrderId;
        set { _workOrderId = value; OnPropertyChanged(); }
    }

    private string _label = "";
    public string Label
    {
        get => _label;
        set
        {
            var s = value ?? "";
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            // Interdit les retours à la ligne dans le DataGrid (1 ligne)
            if (s.Contains('\n'))
                s = s.Replace("\n", " ");

            // Limite la longueur
            if (s.Length > LabelMaxChars)
                s = s.Substring(0, LabelMaxChars);

            _label = s;
            OnPropertyChanged();
        }
    }

    private double _qty;
    public double Qty
    {
        get => _qty;
        set
        {
            _qty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QtyDisplay));
        }
    }

    private double _unitPrice;
    public double UnitPrice
    {
        get => _unitPrice;
        set
        {
            _unitPrice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnitPriceDisplay));
        }
    }

    private double _lineTotal;
    public double LineTotal
    {
        get => _lineTotal;
        set { _lineTotal = value; OnPropertyChanged(); }
    }

    // =========================
    // Parsing / affichage
    // =========================
    private static double ParseDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;

        // Autorise "," ou "." en saisie
        s = s.Trim().Replace(',', '.');

        return double.TryParse(
            s,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var v) ? v : 0;
    }

    // ✅ fr-FR au lieu de InvariantCulture (23.07.2026, demande de Joe) : Qt/Prix affichaient un
    // point "." alors que la colonne Total (StringFormat, ConverterCulture=fr-FR) affiche une
    // virgule ",", incohérence visible sur la même ligne du tableau.
    private static readonly CultureInfo FrFr = CultureInfo.GetCultureInfo("fr-FR");

    private static string EmptyIfZeroUnlimited(double v)
    {
        if (Math.Abs(v) < 0.0000000001) return "";

        // "G17" = représentation compacte qui conserve la précision d’un double
        return v.ToString("G17", FrFr);
    }

    private static string EmptyIfZero2Decimals(double v)
    {
        if (Math.Abs(v) < 0.0000000001) return "";
        return v.ToString("0.00", FrFr);
    }

    // =========================
    // Propriétés utilisées par le DataGrid
    // =========================

    // ✅ Quantité : décimales illimitées (pas de 0.00 forcé)
    public string QtyDisplay
    {
        get => EmptyIfZeroUnlimited(Qty);
        set => Qty = ParseDouble(value);
    }

    // ✅ Prix/pc : TOUJOURS 2 décimales (112.00), sauf vide si 0
    public string UnitPriceDisplay
    {
        get => EmptyIfZero2Decimals(UnitPrice);
        set => UnitPrice = ParseDouble(value);
    }

    // ✅ Les totaux restent à 2 décimales
    public void RecomputeLineTotal()
    {
        LineTotal = Math.Round(Qty * UnitPrice, 2);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}