using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Microsoft.Win32;

namespace Iziregi.Test;

public partial class MainWindow : Window
{
    private Project? _selectedProject;

    private readonly HashSet<long> _alreadyValidatedIds = new();
    private bool _validatedCacheInitialized = false;

    public MainWindow()
    {
        InitializeComponent();

        Db.Init();
        Db.SeedPlacesIfEmpty("D20", "D21", "Extérieur");
        Db.SeedCompaniesIfEmpty("Electricien", "Sanitaire", "Ventilation");

        LoadWorkOrders();
    }

    public void ReloadFromChild() => LoadWorkOrders();

    private void LoadWorkOrders()
    {
        var list = Db.GetWorkOrders();
        WorkOrdersGrid.ItemsSource = list;
        WorkOrdersGrid.Items.Refresh();

        NotifyIfNewValidated(list);
    }

    private void NotifyIfNewValidated(List<WorkOrder> list)
    {
        if (!_validatedCacheInitialized)
        {
            _alreadyValidatedIds.Clear();
            foreach (var w in list)
                if (w.IsValidated)
                    _alreadyValidatedIds.Add(w.Id);

            _validatedCacheInitialized = true;
            return;
        }

        var newlyValidated = list.Where(w => w.IsValidated && !_alreadyValidatedIds.Contains(w.Id)).ToList();
        if (newlyValidated.Count == 0)
            return;

        foreach (var w in newlyValidated)
            _alreadyValidatedIds.Add(w.Id);

        var last = newlyValidated.Last();

        SystemSounds.Asterisk.Play();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            MessageBox.Show(this,
                $"Bon signé reçu.\n\nBDR N° {last.BdrNumber}",
                "Signature reçue",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }));
    }

    // -------------------------
    // Handlers UI
    // -------------------------
    private void StartNewWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        var place = Db.GetPlaces().FirstOrDefault() ?? "D21";
        var performedBy = Db.GetCompanies().FirstOrDefault() ?? "Electricien";

        var wo = new WorkOrder
        {
            BdrNumber = Db.GetNextBdrNumber(),
            Place = place,
            RequestedBy = "Architecte",
            PerformedBy = performedBy,
            RequestDate = DateTime.Today,

            IsValidated = false,
            IsPerformed = false,
            IsCancelled = false,
            IsPendingValidation = false,

            Description = "",

            IsSentToCompany = false,
            IsQuoteReceived = false,

            LaborHours = 0,
            LaborRate = 0,
            TravelQty = 0,
            TravelRate = 0,
            TvaRate = 8.1,
            QuoteNotes = "",

            ProjectId = _selectedProject?.Id
        };

        Db.InsertWorkOrder(wo);

        // Ligne par défaut vide (Qt et prix vides)
        var created = Db.GetWorkOrders().FirstOrDefault();
        if (created != null)
            Db.InsertWorkOrderLine(created.Id, "", 0, 0);

        LoadWorkOrders();

        created = Db.GetWorkOrders().FirstOrDefault();
        if (created == null) return;

        WorkOrdersGrid.SelectedItem = created;
        WorkOrdersGrid.ScrollIntoView(created);

        OpenWorkOrder(created, WorkOrderEditMode.Architecte);
    }

    private void OpenLists_Click(object sender, RoutedEventArgs e)
    {
        var w = new ListsWindow { Owner = this };
        w.ShowDialog();
    }

    private void ChooseProject_Click(object sender, RoutedEventArgs e)
    {
        var w = new ChooseProjectWindow { Owner = this };
        var ok = w.ShowDialog();
        if (ok == true && w.SelectedProject != null)
            _selectedProject = w.SelectedProject;
    }

    private void ExportForCompanyQuote_Click(object sender, RoutedEventArgs e) { }
    private void ImportCompanyQuoteReply_Click(object sender, RoutedEventArgs e) { }
    private void ImportSignerReply_Click(object sender, RoutedEventArgs e) { }

    private void CancelSelected_Click(object sender, RoutedEventArgs e)
    {
        if (WorkOrdersGrid.SelectedItem is not WorkOrder wo) return;
        Db.SetCancelled(wo.Id, true);
        LoadWorkOrders();
    }

    private void ReactivateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (WorkOrdersGrid.SelectedItem is not WorkOrder wo) return;
        Db.SetCancelled(wo.Id, false);
        LoadWorkOrders();
    }

    private WorkOrder? GetSelectedWorkOrder()
        => WorkOrdersGrid.SelectedItem as WorkOrder;

    private void OpenWorkOrder(WorkOrder wo, WorkOrderEditMode mode)
    {
        var w = new WorkOrderWindow(wo.Id, mode) { Owner = this };
        w.ShowDialog();
        LoadWorkOrders();
    }

    private void OpenWorkOrder(WorkOrderEditMode mode)
    {
        var wo = GetSelectedWorkOrder();
        if (wo == null) return;
        OpenWorkOrder(wo, mode);
    }

    private void OpenAsArchitect_Click(object sender, RoutedEventArgs e) => OpenWorkOrder(WorkOrderEditMode.Architecte);
    private void OpenAsCompany_Click(object sender, RoutedEventArgs e) => OpenWorkOrder(WorkOrderEditMode.EntrepriseDevis);
    private void OpenAsSigner_Click(object sender, RoutedEventArgs e) => OpenWorkOrder(WorkOrderEditMode.Signataire);

    private void WorkOrdersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenWorkOrder(WorkOrderEditMode.Architecte);

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadWorkOrders();
}