// File: Pages/ArchivesPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// ✅ Types WPF (évite ambiguïtés avec System.Drawing)
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test.Pages;

public partial class ArchivesPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // Wrapper pour la grille (sélection par checkbox)
    public class WorkOrderRow
    {
        public WorkOrder WorkOrder { get; set; } = new();
        public bool IsSelected { get; set; }

        // ✅ Couleurs entreprise
        public MediaBrush CompanyBrush { get; set; } = MediaBrushes.Transparent;
        public MediaBrush CompanyTextBrush { get; set; } = MediaBrushes.Black;
    }

    private List<WorkOrderRow> _rows = new();

    public ArchivesPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        RefreshBatchSelectionUi();
    }

    public void Reload()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _rows = new List<WorkOrderRow>();

            ArchivedGrid.ItemsSource = _rows;
            ArchivedGrid.Items.Refresh();

            RefreshBatchSelectionUi();

            System.Windows.MessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant d’afficher les archives.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectId = projectIdNullable.Value;

        var list = Db.GetArchivedWorkOrders(projectId);

        // ✅ couleurs entreprises par projet
        var colorMap = Db.GetCompanyColorMap(projectId);

        _rows = list.Select(w =>
        {
            var company = (w.PerformedBy ?? "").Trim();
            if (string.IsNullOrWhiteSpace(company))
                company = "(Non défini)";

            colorMap.TryGetValue(company, out var hex);

            var bg = BrushFromHexOrDefault(hex, MediaBrushes.Transparent);
            var fg = GetTextBrushForBackground(bg);

            return new WorkOrderRow
            {
                WorkOrder = w,
                IsSelected = false,
                CompanyBrush = bg,
                CompanyTextBrush = fg
            };
        }).ToList();

        ArchivedGrid.ItemsSource = _rows;
        ArchivedGrid.Items.Refresh();

        RefreshBatchSelectionUi();
    }

    private static MediaBrush BrushFromHexOrDefault(string? hex, MediaBrush def)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
                return def;

            var s = hex.Trim();
            if (!s.StartsWith("#", StringComparison.Ordinal))
                s = "#" + s;

            var obj = MediaColorConverter.ConvertFromString(s);
            if (obj is MediaColor c)
                return new MediaSolidColorBrush(c);

            return def;
        }
        catch
        {
            return def;
        }
    }

    private static bool TryGetSolidColor(MediaBrush brush, out MediaColor color)
    {
        if (brush is MediaSolidColorBrush scb)
        {
            color = scb.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static MediaBrush GetTextBrushForBackground(MediaBrush bg)
    {
        if (!TryGetSolidColor(bg, out var c))
            return MediaBrushes.Black;

        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        return luminance < 0.55 ? MediaBrushes.White : MediaBrushes.Black;
    }

    private static WorkOrderRow? GetRow(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WorkOrderRow row)
            return row;

        return null;
    }

    private static WorkOrderRow? GetSelectedRow(DataGrid? grid)
        => grid?.SelectedItem as WorkOrderRow;

    private List<WorkOrderRow> GetActiveRows()
    {
        if (ArchivedGrid?.ItemsSource == null)
            return new List<WorkOrderRow>();

        return ArchivedGrid.ItemsSource.Cast<WorkOrderRow>().ToList();
    }

    private void RefreshBatchSelectionUi()
    {
        var rows = GetActiveRows();
        var selectedCount = rows.Count(x => x.IsSelected);

        if (RestoreSelectionButton != null)
            RestoreSelectionButton.IsEnabled = selectedCount > 0;

        if (TrashSelectionButton != null)
            TrashSelectionButton.IsEnabled = selectedCount > 0;
    }

    private List<WorkOrder> GetActionSelectionOrFallbackToRow(WorkOrderRow? fallbackRow)
    {
        var sel = GetActiveRows().Where(r => r.IsSelected).Select(r => r.WorkOrder).ToList();

        if (sel.Count == 0 && fallbackRow?.WorkOrder != null && fallbackRow.WorkOrder.Id > 0)
            sel = new List<WorkOrder> { fallbackRow.WorkOrder };

        return sel;
    }

    private void RowSelectCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row == null || row.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        if (sender is System.Windows.Controls.CheckBox cb)
            row.IsSelected = cb.IsChecked == true;

        RefreshBatchSelectionUi();
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox cb)
            return;

        var rows = GetActiveRows();
        if (rows.Count == 0)
            return;

        var newValue = cb.IsChecked != false;

        foreach (var r in rows)
            r.IsSelected = newValue;

        ArchivedGrid?.Items.Refresh();
        RefreshBatchSelectionUi();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(ArchivedGrid));

        if (sel.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Coche un ou plusieurs bons (colonne de gauche).",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Restaurer le bon {sel[0].BdrDisplay} (retour au Tableau de bord) ?"
            : $"Restaurer {sel.Count} bons (retour au Tableau de bord) ?";

        var ok = System.Windows.MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetArchived(wo.Id, false);

        Reload();
    }

    private void TrashSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(ArchivedGrid));

        if (sel.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Coche un ou plusieurs bons (colonne de gauche).",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Mettre le bon {sel[0].BdrDisplay} à la corbeille ?"
            : $"Mettre {sel.Count} bons à la corbeille ?";

        var ok = System.Windows.MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetTrashed(wo.Id, true);

        Reload();
    }

    private void RestoreRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Restaurer le bon {row.WorkOrder.BdrDisplay} (retour au Tableau de bord) ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetArchived(row.WorkOrder.Id, false);
        Reload();
    }

    private void TrashRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Mettre le bon {row.WorkOrder.BdrDisplay} à la corbeille ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetTrashed(row.WorkOrder.Id, true);
        Reload();
    }

    private void ArchivedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = GetSelectedRow(ArchivedGrid);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        try
        {
            var win = new WorkOrderWindow(row.WorkOrder.Id, WorkOrderEditMode.Architecte)
            {
                Owner = Window.GetWindow(this)
            };
            try
            {
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Exception opening WorkOrderWindow from ArchivesPage: " + ex);
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                    System.IO.File.WriteAllText(path, ex.ToString());
                    // Silent fallback: no MessageBox shown
                }
                catch { }
            }
            Reload();
        }
        catch
        {
            // non bloquant
        }
    }

    // =========================
    // Dashboard-like date columns
    // =========================
    private void DistributedDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not DatePicker dp) return;
            if (dp.DataContext is not WorkOrderRow row) return;
            if (row.WorkOrder == null || row.WorkOrder.Id <= 0) return;

            var d = dp.SelectedDate?.Date;
            row.WorkOrder.DistributedAt = d;

            Db.SetDistributedAt(row.WorkOrder.Id, d);
        }
        catch
        {
        }
    }

    private void PerformedDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not DatePicker dp) return;
            if (dp.DataContext is not WorkOrderRow row) return;
            if (row.WorkOrder == null || row.WorkOrder.Id <= 0) return;

            var d = dp.SelectedDate?.Date;
            row.WorkOrder.PerformedAt = d;

            Db.SetPerformedAt(row.WorkOrder.Id, d);
        }
        catch
        {
        }
    }
}