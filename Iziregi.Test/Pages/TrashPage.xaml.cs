// File: Pages/TrashPage.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test.Pages;

public partial class TrashPage : UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // Wrapper pour la grille (sélection par checkbox)
    public class WorkOrderRow
    {
        public WorkOrder WorkOrder { get; set; } = new();
        public bool IsSelected { get; set; }
    }

    private List<WorkOrderRow> _rows = new();

    public TrashPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    public void Reload()
    {
        var list = Db.GetTrashedWorkOrders();
        _rows = list.Select(w => new WorkOrderRow { WorkOrder = w, IsSelected = false }).ToList();

        TrashedGrid.ItemsSource = _rows;
        TrashedGrid.Items.Refresh();

        if (SelectAllCheckBox != null)
            SelectAllCheckBox.IsChecked = false;
    }

    private WorkOrderRow? SelectedRow => TrashedGrid.SelectedItem as WorkOrderRow;
    private WorkOrder? SelectedWorkOrder => SelectedRow?.WorkOrder;

    private List<WorkOrder> CheckedWorkOrders =>
        _rows.Where(r => r.IsSelected).Select(r => r.WorkOrder).ToList();

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheckBox.IsChecked == true;

        foreach (var r in _rows)
            r.IsSelected = check;

        TrashedGrid.Items.Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = CheckedWorkOrders;

        // fallback si aucune checkbox cochée : utiliser la ligne sélectionnée
        if (sel.Count == 0 && SelectedWorkOrder != null)
            sel = new List<WorkOrder> { SelectedWorkOrder };

        if (sel.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Restaurer le bon BDR-{sel[0].BdrNumber} ?"
            : $"Restaurer {sel.Count} bons ?";

        var ok = MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.SetTrashed(wo.Id, false);

        Reload();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = CheckedWorkOrders;

        // fallback si aucune checkbox cochée : utiliser la ligne sélectionnée
        if (sel.Count == 0 && SelectedWorkOrder != null)
            sel = new List<WorkOrder> { SelectedWorkOrder };

        if (sel.Count == 0)
        {
            MessageBox.Show("Coche un ou plusieurs bons (colonne de gauche).", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Supprimer définitivement le bon BDR-{sel[0].BdrNumber} ?\n\nCette action est irréversible."
            : $"Supprimer définitivement {sel.Count} bons ?\n\nCette action est irréversible.";

        var ok = MessageBox.Show(msg, "Suppression définitive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in sel)
            Db.DeleteWorkOrderPermanently(wo.Id);

        Reload();
    }
}