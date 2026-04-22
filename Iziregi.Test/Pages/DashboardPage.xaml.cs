// File: Pages/DashboardPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test.Pages;

public partial class DashboardPage : UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // Wrapper pour la grille (sélection par checkbox)
    public class WorkOrderRow
    {
        public WorkOrder WorkOrder { get; set; } = new();
        public bool IsSelected { get; set; }
    }

    private List<WorkOrderRow> _rows = new();
    private List<WorkOrderRow> _filteredRows = new();

    public DashboardPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    public void Reload()
    {
        var list = Db.GetWorkOrders(); // exclut corbeille + archives
        _rows = list.Select(w => new WorkOrderRow { WorkOrder = w, IsSelected = false }).ToList();

        ApplySearchFilter();

        if (SelectAllCheckBox != null)
            SelectAllCheckBox.IsChecked = false;
    }

    private WorkOrderRow? SelectedRow => WorkOrdersGrid.SelectedItem as WorkOrderRow;
    private WorkOrder? SelectedWorkOrder => SelectedRow?.WorkOrder;

    private List<WorkOrder> CheckedWorkOrders =>
        _filteredRows.Where(r => r.IsSelected).Select(r => r.WorkOrder).ToList();

    private void ApplySearchFilter()
    {
        var q = (SearchTextBox?.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(q))
        {
            _filteredRows = _rows.ToList();
        }
        else
        {
            var qq = q.ToLowerInvariant();

            _filteredRows = _rows.Where(r =>
            {
                var wo = r.WorkOrder;

                var bdr = wo.BdrNumber.ToString();
                var place = wo.Place ?? "";
                var requestedBy = wo.RequestedBy ?? "";
                var performedBy = wo.PerformedBy ?? "";
                var desc = wo.Description ?? "";

                return
                    bdr.Contains(qq, StringComparison.OrdinalIgnoreCase) ||
                    place.Contains(qq, StringComparison.OrdinalIgnoreCase) ||
                    requestedBy.Contains(qq, StringComparison.OrdinalIgnoreCase) ||
                    performedBy.Contains(qq, StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains(qq, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        WorkOrdersGrid.ItemsSource = _filteredRows;
        WorkOrdersGrid.Items.Refresh();

        if (SelectAllCheckBox != null)
            SelectAllCheckBox.IsChecked = false;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplySearchFilter();

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheckBox.IsChecked == true;

        foreach (var r in _filteredRows)
            r.IsSelected = check;

        WorkOrdersGrid.Items.Refresh();
    }

    private void NewWorkOrder_Click(object sender, RoutedEventArgs e)
        => _host.CreateNewWorkOrderAndOpen();

    private void ChooseProject_Click(object sender, RoutedEventArgs e)
        => _host.ChooseProject();

    private void SendToCompany_Click(object sender, RoutedEventArgs e)
    {
        var wo = SelectedWorkOrder;
        if (wo == null)
        {
            MessageBox.Show("Sélectionne un bon.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _host.OpenWorkOrder(wo.Id, WorkOrderEditMode.Architecte);
    }

    private void OpenIziregiFile_Click(object sender, RoutedEventArgs e)
    {
        var wo = SelectedWorkOrder;
        if (wo == null)
        {
            MessageBox.Show("Sélectionne un bon puis utilise les boutons d’export/import dans la fenêtre du bon.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _host.OpenWorkOrder(wo.Id, WorkOrderEditMode.Architecte);
    }

    private void ImportCompanyReply_Click(object sender, RoutedEventArgs e)
        => _host.ImportCompanyQuoteReply_ManualPicker();

    private void ImportSignerReply_Click(object sender, RoutedEventArgs e)
        => _host.ImportSignerReply_ManualPicker();

    private List<WorkOrder> GetActionSelectionOrFallbackToRow()
    {
        var selected = CheckedWorkOrders;

        // fallback si aucune checkbox cochée : utiliser la ligne sélectionnée
        if (selected.Count == 0 && SelectedWorkOrder != null)
            selected = new List<WorkOrder> { SelectedWorkOrder };

        return selected;
    }

    private void TrashSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetActionSelectionOrFallbackToRow();

        if (selected.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = selected.Count == 1
            ? $"Mettre le bon BDR-{selected[0].BdrNumber} à la corbeille ?"
            : $"Mettre {selected.Count} bons à la corbeille ?";

        var ok = MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in selected)
            Db.SetTrashed(wo.Id, true);

        Reload();
    }

    private void ArchiveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetActionSelectionOrFallbackToRow();

        if (selected.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = selected.Count == 1
            ? $"Archiver le bon BDR-{selected[0].BdrNumber} ?\n\nIl disparaîtra du Tableau de bord et sera visible dans Archives."
            : $"Archiver {selected.Count} bons ?\n\nIls disparaîtront du Tableau de bord et seront visibles dans Archives.";

        var ok = MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in selected)
            Db.SetArchived(wo.Id, true);

        Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => Reload();

    private void OpenAsArchitect_Click(object sender, RoutedEventArgs e)
    {
        var wo = SelectedWorkOrder;
        if (wo == null) return;
        _host.OpenWorkOrder(wo.Id, WorkOrderEditMode.Architecte);
    }

    private void OpenAsCompany_Click(object sender, RoutedEventArgs e)
    {
        var wo = SelectedWorkOrder;
        if (wo == null) return;
        _host.OpenWorkOrder(wo.Id, WorkOrderEditMode.EntrepriseDevis);
    }

    private void OpenAsSigner_Click(object sender, RoutedEventArgs e)
    {
        var wo = SelectedWorkOrder;
        if (wo == null) return;
        _host.OpenWorkOrder(wo.Id, WorkOrderEditMode.Signataire);
    }

    private void WorkOrdersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenAsArchitect_Click(sender, e);

    private void PerformedCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not WorkOrderRow row) return;

        Db.SetPerformed(row.WorkOrder.Id, cb.IsChecked == true);
        Reload();
    }

    private void CancelledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not WorkOrderRow row) return;

        Db.SetCancelled(row.WorkOrder.Id, cb.IsChecked == true);
        Reload();
    }
}