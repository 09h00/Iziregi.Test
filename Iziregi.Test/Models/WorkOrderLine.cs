using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Iziregi.Test.Models;

public class WorkOrderLine : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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
        set { _label = value ?? ""; OnPropertyChanged(); }
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

        // Autorise "," ou "." en saisie, sans limiter le nombre de décimales
        s = s.Trim().Replace(',', '.');

        return double.TryParse(
            s,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var v) ? v : 0;
    }

    private static string EmptyIfZeroUnlimited(double v)
    {
        if (Math.Abs(v) < 0.0000000001) return "";

        // "G17" = représentation compacte qui conserve la précision d’un double
        // (et n’impose pas 2 décimales)
        return v.ToString("G17", CultureInfo.InvariantCulture);
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

    // ✅ Prix/pc : décimales illimitées (pas de 0.00 forcé)
    public string UnitPriceDisplay
    {
        get => EmptyIfZeroUnlimited(UnitPrice);
        set => UnitPrice = ParseDouble(value);
    }

    // ✅ Les totaux restent à 2 décimales (si tu veux)
    public void RecomputeLineTotal()
    {
        LineTotal = Math.Round(Qty * UnitPrice, 2);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}