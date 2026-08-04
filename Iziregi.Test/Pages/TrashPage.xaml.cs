// File: Pages/TrashPage.xaml.cs
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

public partial class TrashPage : System.Windows.Controls.UserControl, IReloadablePage
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

    public TrashPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        RefreshBatchSelectionUi();
    }

    // ✅ Retour (04.08.2026, demande de Joe) : ramène à "Bons d'intervention", page d'origine
    // depuis laquelle cette Corbeille est maintenant accessible (voir DashboardPage.xaml).
    private void BackToParent_Click(object sender, RoutedEventArgs e) => _host.ShowDashboard();

    // ✅ Accès direct à l'autre sous-page (demande de Joe, 04.08.2026) : depuis Corbeille, va
    // directement à Archives sans repasser par "Bons d'intervention".
    private void GoToArchives_Click(object sender, RoutedEventArgs e) => _host.ShowArchives();

    public void Reload()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _rows = new List<WorkOrderRow>();

            TrashedGrid.ItemsSource = _rows;
            TrashedGrid.Items.Refresh();

            RefreshBatchSelectionUi();

            System.Windows.MessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant d’afficher la corbeille.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectId = projectIdNullable.Value;

        var list = Db.GetTrashedWorkOrders(projectId);

        // ✅ couleurs entreprises par projet
        var colorMap = Db.GetCompanyColorMap(projectId);
        var gradientMap = Db.GetCompanyGradientMap(projectId);

        _rows = list.Select(w =>
        {
            var company = (w.PerformedBy ?? "").Trim();
            if (string.IsNullOrWhiteSpace(company))
                company = "(Non défini)";

            colorMap.TryGetValue(company, out var hex);

            var fg = GetTextBrushForBackground(BrushFromHexOrDefault(hex, MediaBrushes.Transparent));
            var bg = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, gradientMap.Contains(company)) ?? MediaBrushes.Transparent;

            return new WorkOrderRow
            {
                WorkOrder = w,
                IsSelected = false,
                CompanyBrush = bg,
                CompanyTextBrush = fg
            };
        }).ToList();

        TrashedGrid.ItemsSource = _rows;
        TrashedGrid.Items.Refresh();

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
        if (TrashedGrid?.ItemsSource == null)
            return new List<WorkOrderRow>();

        return TrashedGrid.ItemsSource.Cast<WorkOrderRow>().ToList();
    }

    // ✅ Fix (04.08.2026, demande de Joe) : actif si au moins une case est cochée OU si une
    // ligne est sélectionnée (bordure bleue), même principe que DashboardPage.
    private void RefreshBatchSelectionUi()
    {
        var rows = GetActiveRows();
        var anySelected = rows.Any(x => x.IsSelected) || TrashedGrid?.SelectedItem != null;

        if (RestoreSelectionButton != null)
            RestoreSelectionButton.IsEnabled = anySelected;

        if (DeleteSelectionButton != null)
            DeleteSelectionButton.IsEnabled = anySelected;
    }

    private void TrashedGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBatchSelectionUi();

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

        TrashedGrid?.Items.Refresh();
        RefreshBatchSelectionUi();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(TrashedGrid));

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
            ? $"Restaurer le bon {sel[0].BdrDisplay} ?"
            : $"Restaurer {sel.Count} bons ?";

        var ok = System.Windows.MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetTrashed(wo.Id, false);

        Reload();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(TrashedGrid));

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
            ? $"Supprimer définitivement le bon {sel[0].BdrDisplay} ?\n\nCette action est irréversible."
            : $"Supprimer définitivement {sel.Count} bons ?\n\nCette action est irréversible.";

        var ok = System.Windows.MessageBox.Show(msg, "Suppression définitive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.DeleteWorkOrderPermanently(wo.Id);

        Reload();
    }

    private void RestoreRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Restaurer le bon {row.WorkOrder.BdrDisplay} ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetTrashed(row.WorkOrder.Id, false);
        Reload();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Supprimer définitivement le bon {row.WorkOrder.BdrDisplay} ?\n\nCette action est irréversible.",
            "Suppression définitive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.DeleteWorkOrderPermanently(row.WorkOrder.Id);
        Reload();
    }

    private void TrashedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = GetSelectedRow(TrashedGrid);
        if (row?.WorkOrder == null || row.WorkOrder.Id <= 0)
            return;

        try
        {
            if (WorkOrderWindow.ActivateIfAlreadyOpen(row.WorkOrder.Id)) return;

            var win = new WorkOrderWindow(row.WorkOrder.Id, WorkOrderEditMode.Architecte)
            {
                Owner = Window.GetWindow(this)
            };
            // ✅ Fix (demande de Joe : fenêtre non modale) : Reload() se faisait après ShowDialog
            // (bloquant jusqu'à la fermeture) ; avec Show() non modal, sur Closed à la place.
            win.Closed += (s, e) => { try { Reload(); } catch { } };
            try
            {
                win.Show();
            }
            catch (Exception ex)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Exception opening WorkOrderWindow from TrashPage: " + ex);
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                    System.IO.File.WriteAllText(path, ex.ToString());
                    // Silent fallback: no MessageBox shown
                }
                catch { }
            }
        }
        catch
        {
            // non bloquant
        }
    }
}