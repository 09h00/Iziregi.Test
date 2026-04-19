using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test;

public partial class WorkOrderWindow : Window
{
    private readonly long _workOrderId;
    private readonly WorkOrderEditMode _mode;
    private WorkOrder? _wo;

    private TextBox? _activeEditTextBox;

    public WorkOrderWindow(long workOrderId, WorkOrderEditMode mode)
    {
        InitializeComponent();

        _workOrderId = workOrderId;
        _mode = mode;

        DataContext = this;

        LinesGrid.CellEditEnding += LinesGrid_CellEditEnding;
        LinesGrid.PreparingCellForEdit += LinesGrid_PreparingCellForEdit;

        // ✅ Neutralise tout filtre clavier "global" au niveau DataGrid
        LinesGrid.AddHandler(UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(LinesGrid_PreviewTextInput_HandledToo), true);
        LinesGrid.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(LinesGrid_PreviewKeyDown_HandledToo), true);

        LoadWorkOrder();
        ApplyMode();
        RecalculateTotals();
    }

    // =========================
    // Display props
    // - Quantités (heures + qt) : sans décimales
    // - Prix / TVA : 2 décimales
    // =========================
    public string LaborHoursDisplay
    {
        get => _wo == null ? "" : EmptyIfZero0(_wo.LaborHours);
        set { if (_wo == null) return; _wo.LaborHours = ParseDouble(value); RecalculateTotals(); }
    }

    public string LaborRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.LaborRate);
        set { if (_wo == null) return; _wo.LaborRate = ParseDouble(value); RecalculateTotals(); }
    }

    public string TravelQtyDisplay
    {
        get => _wo == null ? "" : EmptyIfZero0(_wo.TravelQty);
        set { if (_wo == null) return; _wo.TravelQty = ParseDouble(value); RecalculateTotals(); }
    }

    public string TravelRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.TravelRate);
        set { if (_wo == null) return; _wo.TravelRate = ParseDouble(value); RecalculateTotals(); }
    }

    public string TvaRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.TvaRate);
        set { if (_wo == null) return; _wo.TvaRate = ParseDouble(value); RecalculateTotals(); }
    }

    // =========================
    // Mode / Load
    // =========================
    private void ApplyMode()
    {
        DemandPanel.IsEnabled = _mode == WorkOrderEditMode.Architecte;
        QuotePanel.IsEnabled = _mode == WorkOrderEditMode.Architecte || _mode == WorkOrderEditMode.EntrepriseDevis;
        SignaturePanel.IsEnabled = _mode == WorkOrderEditMode.Signataire;

        SaveHeaderButton.Visibility = _mode == WorkOrderEditMode.Architecte ? Visibility.Visible : Visibility.Collapsed;
        CreatePdfButton.Visibility = _mode == WorkOrderEditMode.Architecte ? Visibility.Visible : Visibility.Collapsed;
        ExportForSignatureButton.Visibility = _mode == WorkOrderEditMode.Architecte ? Visibility.Visible : Visibility.Collapsed;

        AddMaterialLineButton.Visibility =
            (_mode == WorkOrderEditMode.Architecte || _mode == WorkOrderEditMode.EntrepriseDevis)
                ? Visibility.Visible : Visibility.Collapsed;

        DeleteMaterialLineButton.Visibility =
            (_mode == WorkOrderEditMode.Architecte || _mode == WorkOrderEditMode.EntrepriseDevis)
                ? Visibility.Visible : Visibility.Collapsed;

        SaveQuoteButton.Visibility = _mode == WorkOrderEditMode.Architecte ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadWorkOrder()
    {
        _wo = Db.GetWorkOrderById(_workOrderId);
        if (_wo == null)
        {
            MessageBox.Show("Bon introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        TitleTextBlock.Text = $"Bon de régie — N° {_wo.BdrNumber}";

        PlaceComboBox.ItemsSource = Db.GetPlaces();
        PerformedByComboBox.ItemsSource = Db.GetCompanies();

        PlaceComboBox.SelectedItem = _wo.Place;
        RequestedByTextBox.Text = _wo.RequestedBy;
        PerformedByComboBox.SelectedItem = _wo.PerformedBy;
        RequestDatePicker.SelectedDate = _wo.RequestDate;
        DescriptionTextBox.Text = _wo.Description ?? "";

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;

        QuoteNotesTextBox.Text = _wo.QuoteNotes ?? "";

        RefreshDisplayBindings();
    }

    private void RefreshDisplayBindings()
    {
        var dc = DataContext;
        DataContext = null;
        DataContext = dc;
    }

    // =========================
    // ✅ Anti-limitation 2 chiffres au niveau DataGrid
    // =========================
    private bool IsEditingQtyOrPriceCell()
    {
        var col = LinesGrid.CurrentColumn;
        if (col == null) return false;

        var header = col.Header?.ToString() ?? "";
        return header.Contains("Qt", StringComparison.OrdinalIgnoreCase)
               || header.Contains("Prix", StringComparison.OrdinalIgnoreCase);
    }

    private void LinesGrid_PreviewTextInput_HandledToo(object sender, TextCompositionEventArgs e)
    {
        if (!IsEditingQtyOrPriceCell())
            return;

        // Si un filtre global a mis Handled=true (limite à 2 chiffres), on annule ce blocage
        e.Handled = false;
    }

    private void LinesGrid_PreviewKeyDown_HandledToo(object sender, KeyEventArgs e)
    {
        if (!IsEditingQtyOrPriceCell())
            return;

        e.Handled = false;
    }

    // =========================
    // DataGrid: total live pendant la saisie
    // =========================
    private void LinesGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        var header = e.Column?.Header?.ToString() ?? "";
        bool isQtyOrPrice =
            header.Contains("Qt", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Prix", StringComparison.OrdinalIgnoreCase);

        if (!isQtyOrPrice) return;
        if (e.EditingElement is not TextBox tb) return;

        // Force illimité localement aussi
        tb.MaxLength = 0;

        if (_activeEditTextBox != null)
            _activeEditTextBox.TextChanged -= ActiveEditTextBox_TextChanged;

        _activeEditTextBox = tb;
        _activeEditTextBox.TextChanged += ActiveEditTextBox_TextChanged;
    }

    private void ActiveEditTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_wo == null) return;

        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);

        if (LinesGrid.CurrentItem is not WorkOrderLine line)
            return;

        line.RecomputeLineTotal();
        RecalculateTotals();
    }

    private void LinesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        => Dispatcher.BeginInvoke(new Action(SaveCurrentGridLinesToDb));

    // =========================
    // Persistence + Add/Delete
    // =========================
    private void SaveCurrentGridLinesToDb()
    {
        if (_wo == null) return;

        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var lines = (LinesGrid.ItemsSource as IEnumerable<WorkOrderLine>)?.ToList();
        if (lines == null) return;

        foreach (var l in lines)
        {
            l.Label = (l.Label ?? "").Trim();
            l.RecomputeLineTotal();

            if (l.Id > 0)
                Db.UpdateWorkOrderLine(l);
        }
    }

    private void AddMaterialLine_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();

        Db.InsertWorkOrderLine(_wo.Id, "", 0, 0);

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;
        RecalculateTotals();
    }

    private void DeleteMaterialLine_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();

        if (LinesGrid.SelectedItem is not WorkOrderLine line) return;

        Db.DeleteWorkOrderLine(line.Id);

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;
        RecalculateTotals();
    }

    // =========================
    // Totaux
    // =========================
    private void RecalculateTotals()
    {
        if (_wo == null) return;

        var lines = (LinesGrid.ItemsSource as IEnumerable<WorkOrderLine>)?.ToList()
                    ?? Db.GetWorkOrderLines(_wo.Id);

        foreach (var l in lines)
            l.RecomputeLineTotal();

        var materialTotal = Math.Round(lines.Sum(l => l.LineTotal), 2);

        var laborTotal = Math.Round(_wo.LaborHours * _wo.LaborRate, 2);
        var travelTotal = Math.Round(_wo.TravelQty * _wo.TravelRate, 2);

        var totalHt = Math.Round(materialTotal + laborTotal + travelTotal, 2);
        var tvaAmount = Math.Round(totalHt * (_wo.TvaRate / 100.0), 2);
        var totalTtc = Math.Round(totalHt + tvaAmount, 2);

        LaborTotalTextBlock.Text = F2(laborTotal);
        TravelTotalTextBlock.Text = F2(travelTotal);
        TvaAmountTextBlock.Text = F2(tvaAmount);

        TotalHtTextBlock.Text = $"Total HT : {F2(totalHt)}";
        TotalTtcTextBlock.Text = $"Total TTC : {F2(totalTtc)}";
    }

    // =========================
    // Handlers requis par le XAML (placeholders)
    // =========================
    private void Back_Click(object sender, RoutedEventArgs e) => Close();
    private void SaveHeader_Click(object sender, RoutedEventArgs e) { }
    private void SaveQuote_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();
        _wo.QuoteNotes = QuoteNotesTextBox.Text ?? "";
        Db.UpdateWorkOrderQuote(_wo);

        MessageBox.Show("Devis enregistré.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void SaveSignerReply_Click(object sender, RoutedEventArgs e) { }
    private void SignerExportPdf_Click(object sender, RoutedEventArgs e) { }
    private void ExportForSignature_Click(object sender, RoutedEventArgs e) { }
    private void CreatePdf_Click(object sender, RoutedEventArgs e) { }

    // =========================
    // Helpers
    // =========================
    private static string F2(double v) => v.ToString("0.00");

    private static string EmptyIfZero2(double v)
        => Math.Abs(v) < 0.0000001 ? "" : v.ToString("0.00");

    private static string EmptyIfZero0(double v)
        => Math.Abs(v) < 0.0000001 ? "" : Math.Round(v, 0).ToString("0");

    private static double ParseDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Replace(',', '.');
        return double.TryParse(
            s,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0;
    }
}