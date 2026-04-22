// File: Pages/ArchivesPage.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test.Pages;

public partial class ArchivesPage : UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // Wrapper pour la grille (sélection par checkbox)
    public class WorkOrderRow
    {
        public WorkOrder WorkOrder { get; set; } = new();
        public bool IsSelected { get; set; }
    }

    private List<WorkOrderRow> _rows = new();

    public ArchivesPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    public void Reload()
    {
        var list = Db.GetArchivedWorkOrders();
        _rows = list.Select(w => new WorkOrderRow { WorkOrder = w, IsSelected = false }).ToList();

        ArchivedGrid.ItemsSource = _rows;
        ArchivedGrid.Items.Refresh();

        if (SelectAllCheckBox != null)
            SelectAllCheckBox.IsChecked = false;
    }

    private WorkOrderRow? SelectedRow => ArchivedGrid.SelectedItem as WorkOrderRow;
    private WorkOrder? SelectedWorkOrder => SelectedRow?.WorkOrder;

    private List<WorkOrder> CheckedWorkOrders =>
        _rows.Where(r => r.IsSelected).Select(r => r.WorkOrder).ToList();

    private List<WorkOrder> GetActionSelectionOrFallbackToRow()
    {
        var sel = CheckedWorkOrders;
        if (sel.Count == 0 && SelectedWorkOrder != null)
            sel = new List<WorkOrder> { SelectedWorkOrder };
        return sel;
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheckBox.IsChecked == true;

        foreach (var r in _rows)
            r.IsSelected = check;

        ArchivedGrid.Items.Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow();
        if (sel.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Restaurer le bon BDR-{sel[0].BdrNumber} (retour au Tableau de bord) ?"
            : $"Restaurer {sel.Count} bons (retour au Tableau de bord) ?";

        var ok = MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetArchived(wo.Id, false);

        Reload();
    }

    private void TrashSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow();
        if (sel.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Mettre le bon BDR-{sel[0].BdrNumber} à la corbeille ?"
            : $"Mettre {sel.Count} bons à la corbeille ?";

        var ok = MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetTrashed(wo.Id, true);

        Reload();
    }
}