// File: DashboardPage.xaml.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Pages;
using Iziregi.Test.Services;

// ✅ Aliases WPF (évite ambiguïtés avec WinForms / System.Drawing)
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushConverter = System.Windows.Media.BrushConverter;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

// ✅ Interfaces WPF
using System.Windows.Data;

namespace Iziregi.Test;

// ✅ BUG (statut qui ne change pas après le pop-up "Réponse reçue") : DashboardPage avait déjà
// une méthode publique Reload() avec la BONNE signature, mais la classe ne déclarait PAS
// implémenter IReloadablePage. Du coup partout où le code fait
// "if (MainContent.Content is IReloadablePage p) p.Reload();" (après ApplySubmissionToLocalDbAsync,
// après import INBOX, etc.), le test échouait silencieusement quand le Dashboard était la page
// affichée : aucune exception, mais le Reload() n'était JAMAIS appelé. Résultat : la grille du
// Dashboard restait sur l'ancien statut tant qu'on n'ouvrait pas le bon (lecture fraîche depuis
// la DB) — exactement le symptôme "je suis obligé de l'ouvrir pour qu'il se mette à jour".
public partial class DashboardPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private bool _isLoadingProjects;
    private string _logoPath = "";

    // =========================
    // ✅ Mode "obliger OK" (dirty strict)
    // =========================
    private bool _identityDirty;
    private bool _suspendDirtyTracking;
    private long? _lastProjectId;

    // Filtre dashboard
    private System.Collections.Generic.List<WorkOrder> _allWorkOrders = new();
    private System.ComponentModel.ICollectionView? _workOrdersView;
    private string _filterStatus = "Tous";
    private string _filterText = "";

    public DashboardPage()
    {
        InitializeComponent();

        // ✅ Converters stockés en ressources (et utilisés en code-behind)
        Resources["CompanyToBrush"] = new CompanyToBrushConverter_Local();
        Resources["CompanyToForegroundBrush"] = new CompanyToForegroundBrushConverter_Local();

        // ✅ Applique le style couleur uniquement sur la cellule "Entreprise"
        ApplyPerformedByCompanyCellStyle();

        Db.Init();

        // ✅ Drag & Drop de fichiers .iziregi-package sur le dashboard
        AllowDrop = true;
        PreviewDragOver += DashboardPage_PreviewDragOver;
        Drop += DashboardPage_Drop;

        HookDirtyTracking();

        LoadIdentity();
        LoadProjects();
        RefreshAll();

        MarkIdentityDirty(false);
    }

    // Compatibilité avec MainWindow.xaml.cs qui appelle new DashboardPage(this)
    public DashboardPage(object? _)
        : this()
    {
    }

    public void Reload()
    {
        LoadIdentity();
        LoadProjects();
        RefreshAll();
    }

    // =========================
    // ✅ Dirty tracking (strict)
    // =========================
    private void HookDirtyTracking()
    {
        // ✅ Champs Identité architecte désormais en lecture seule sur le Dashboard
        // (édition exclusivement via la fenêtre "Identité architecte") : plus de
        // suivi dirty nécessaire ici.

        HookDirty(ProjectNameEditTextBox);
        HookDirty(ProjectAddressEditTextBox);
        HookDirty(ProjectZipCityEditTextBox);

        // Empêche changement projet si dirty (rollback)
        if (ProjectComboBox != null)
        {
            ProjectComboBox.SelectionChanged -= ProjectComboBox_SelectionChanged_DirtyProxy;
            ProjectComboBox.SelectionChanged += ProjectComboBox_SelectionChanged_DirtyProxy;
        }
    }

    private void HookDirty(object? control)
    {
        if (control == null) return;

        var tb = control as global::System.Windows.Controls.TextBox;
        if (tb == null) return;

        tb.TextChanged -= AnyIdentityField_TextChanged;
        tb.TextChanged += AnyIdentityField_TextChanged;
    }

    private void AnyIdentityField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suspendDirtyTracking) return;
        MarkIdentityDirty(true);
    }

    private void MarkIdentityDirty(bool dirty)
    {
        _identityDirty = dirty;
        ApplyIdentityLocks();
    }

    private void ApplyIdentityLocks()
    {
        var allowContinue = !_identityDirty;

        // ✅ Bannière visible
        if (DirtyBanner != null)
            DirtyBanner.Visibility = allowContinue ? Visibility.Collapsed : Visibility.Visible;

        // ✅ Désactive TOUTES les actions tant que dirty (silencieux)
        if (NewWorkOrderTopButton != null) NewWorkOrderTopButton.IsEnabled = allowContinue;
        if (RefreshTopButton != null) RefreshTopButton.IsEnabled = allowContinue;
        if (ManageProjectsButton != null) ManageProjectsButton.IsEnabled = allowContinue;

        if (ArchiveSelectionButton != null) ArchiveSelectionButton.IsEnabled = allowContinue && AnyBatchSelected();
        if (TrashSelectionButton != null) TrashSelectionButton.IsEnabled = allowContinue && AnyBatchSelected();

        AllowDrop = allowContinue;

        if (ProjectComboBox != null) ProjectComboBox.IsEnabled = allowContinue;

        if (WorkOrdersGrid != null) WorkOrdersGrid.IsEnabled = allowContinue;
        if (ArchivedGrid != null) ArchivedGrid.IsEnabled = allowContinue;
        if (TrashedGrid != null) TrashedGrid.IsEnabled = allowContinue;
    }

    private bool EnsureNotDirtyOrWarn()
    {
        if (!_identityDirty) return true;

        System.Windows.MessageBox.Show(
            "Modifications non enregistrées.\n\nClique sur OK pour enregistrer avant de continuer.",
            "Modifications non enregistrées",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private void ProjectComboBox_SelectionChanged_DirtyProxy(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendDirtyTracking) return;
        if (!_identityDirty) return;

        // rollback
        try
        {
            _suspendDirtyTracking = true;

            if (ProjectComboBox != null)
            {
                ProjectComboBox.SelectionChanged -= ProjectComboBox_SelectionChanged;

                if (_lastProjectId.HasValue)
                    ProjectComboBox.SelectedValue = _lastProjectId.Value;

                ProjectComboBox.SelectionChanged += ProjectComboBox_SelectionChanged;
            }
        }
        finally
        {
            _suspendDirtyTracking = false;
        }

        System.Windows.MessageBox.Show(
            "Modifications non enregistrées.\n\nClique sur OK pour enregistrer avant de changer de projet.",
            "Modifications non enregistrées",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool AnyBatchSelected()
    {
        try
        {
            var items = GetActiveWorkOrders();
            return items.Any(x => x.IsBatchSelected);
        }
        catch
        {
            return false;
        }
    }

    // =========================
    // ✅ Helpers : split/join adresse <-> "NPA/Ville"
    // =========================
    private static (string line1, string line2) SplitAddressTwoLines(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
            return ("", "");

        // On coupe au dernier ","
        var lastComma = s.LastIndexOf(',');
        if (lastComma >= 0 && lastComma < s.Length - 1)
        {
            var a = s.Substring(0, lastComma).Trim();
            var b = s.Substring(lastComma + 1).Trim();
            return (a, b);
        }

        return (s, "");
    }

    private static string JoinAddressTwoLines(string? line1, string? line2)
    {
        var a = (line1 ?? "").Trim();
        var b = (line2 ?? "").Trim();

        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return "";

        if (string.IsNullOrWhiteSpace(b))
            return a;

        if (string.IsNullOrWhiteSpace(a))
            return b;

        return $"{a}, {b}";
    }

    // =========================
    // ✅ Style cellule Entreprise (couleur + contraste) en code-behind
    // =========================
    private void ApplyPerformedByCompanyCellStyle()
    {
        try
        {
            if (WorkOrdersGrid == null)
                return;

            var bgConv = Resources["CompanyToBrush"] as IValueConverter;
            var fgConv = Resources["CompanyToForegroundBrush"] as IValueConverter;

            if (bgConv == null || fgConv == null)
                return;

            DataGridTextColumn? performedByCol = null;

            foreach (var col in WorkOrdersGrid.Columns)
            {
                if (col is not DataGridTextColumn textCol)
                    continue;

                // Colonne Entreprise via Binding Path = "PerformedBy"
                if (textCol.Binding is WpfBinding b && string.Equals(b.Path?.Path, "PerformedBy", StringComparison.OrdinalIgnoreCase))
                {
                    performedByCol = textCol;
                    break;
                }
            }

            if (performedByCol == null)
                return;

            var cellStyle = new Style(typeof(DataGridCell));

            cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(6, 0, 6, 0)));
            cellStyle.Setters.Add(new Setter(DataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            cellStyle.Setters.Add(new Setter(DataGridCell.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center));

            // Border
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, (WpfBrush)new WpfBrushConverter().ConvertFromString("#E5E7EB")!));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));

            // Background = couleur entreprise
            cellStyle.Setters.Add(new Setter(
                DataGridCell.BackgroundProperty,
                new WpfBinding("PerformedBy") { Converter = bgConv }
            ));

            // Foreground = contraste auto
            cellStyle.Setters.Add(new Setter(
                DataGridCell.ForegroundProperty,
                new WpfBinding("PerformedBy") { Converter = fgConv }
            ));

            // Sélection = bleu standard
            var selectedTrigger = new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, (WpfBrush)new WpfBrushConverter().ConvertFromString("#DBEAFE")!));
            selectedTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, (WpfBrush)new WpfBrushConverter().ConvertFromString("#111827")!));
            cellStyle.Triggers.Add(selectedTrigger);

            performedByCol.CellStyle = cellStyle;
        }
        catch
        {
            // non bloquant
        }
    }

    // =========================
    // ✅ Libellés dashboard (headers)
    // =========================
    private void ApplyDashboardLabels()
    {
        try
        {
            if (ReserveHeaderTextBlock == null
                || RequestedByHeaderTextBlock == null
                || PerformedByHeaderTextBlock == null)
                return;

            if (ProjectComboBox?.SelectedItem is not Project p || p.Id <= 0)
            {
                ReserveHeaderTextBlock.Text = "Concerne";
                RequestedByHeaderTextBlock.Text = "Demandé par";
                PerformedByHeaderTextBlock.Text = "Entreprise";
                return;
            }

            ReserveHeaderTextBlock.Text = Db.GetLabelReserve(p.Id);
            RequestedByHeaderTextBlock.Text = Db.GetLabelRequestedBy(p.Id);
            PerformedByHeaderTextBlock.Text = Db.GetLabelPerformedBy(p.Id);
        }
        catch
        {
            // non bloquant
        }
    }

    // =========================
    // ✅ Aperçu descriptif (zone 50% à droite)
    // =========================
    private void UpdateSelectedWorkOrderPreview()
    {
        try
        {
            if (SelectedWorkOrderDescriptionPreviewTextBlock == null)
                return;

            var wo = GetSelectedWorkOrder(WorkOrdersGrid);
            if (wo == null)
            {
                SelectedWorkOrderDescriptionPreviewTextBlock.Text = "";
                return;
            }

            var desc = (wo.Description ?? "").Trim();

            // premières lignes “utiles”
            var lines = desc
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();

            SelectedWorkOrderDescriptionPreviewTextBlock.Text =
                lines.Count == 0 ? "" : string.Join(" — ", lines);
        }
        catch
        {
            // non bloquant
        }
    }

    // =========================
    // ✅ Preview border (bleu/gris selon sélection)
    // =========================
    private void UpdatePreviewBorderFromSelection()
    {
        var hasSelection = WorkOrdersGrid != null && WorkOrdersGrid.SelectedItem != null;

        if (SelectedWorkOrderDescriptionPreviewBorder != null)
        {
            SelectedWorkOrderDescriptionPreviewBorder.BorderBrush =
                (WpfBrush)new WpfBrushConverter().ConvertFromString(hasSelection ? "#2563EB" : "#D1D5DB")!;

            SelectedWorkOrderDescriptionPreviewBorder.BorderThickness =
                hasSelection ? new Thickness(2) : new Thickness(1);
        }
    }

    // =========================
    // Chargement global
    // =========================
    private void RefreshAll()
    {
        ApplyDashboardLabels();

        RefreshWorkOrders();
        RefreshArchived();
        RefreshTrashed();
        RefreshBatchSelectionUi();

        // ✅ force refresh visuel (utile si les couleurs entreprise ont changé)
        WorkOrdersGrid?.Items.Refresh();

        // ✅ met à jour l’aperçu du descriptif (après refresh)
        UpdateSelectedWorkOrderPreview();

        // ✅ synchronise la bordure du preview avec la sélection
        UpdatePreviewBorderFromSelection();
    }

    private void RefreshWorkOrders()
    {
        if (WorkOrdersGrid == null)
            return;

        if (ProjectComboBox?.SelectedItem is not Project selectedProject)
        {
            _allWorkOrders = new System.Collections.Generic.List<WorkOrder>();
            _workOrdersView = null;
            WorkOrdersGrid.ItemsSource = Array.Empty<WorkOrder>();
            RefreshBatchSelectionUi();
            UpdateSelectedWorkOrderPreview();
            UpdatePreviewBorderFromSelection();
            return;
        }

        _allWorkOrders = Db.GetWorkOrders(selectedProject.Id);

        foreach (var wo in _allWorkOrders)
            wo.IsBatchSelected = false;

        _workOrdersView = System.Windows.Data.CollectionViewSource.GetDefaultView(_allWorkOrders);
        _workOrdersView.Filter = WorkOrderFilter;
        WorkOrdersGrid.ItemsSource = _workOrdersView;

        RefreshBatchSelectionUi();
    }

    // =========================
    // Filtre dashboard
    // =========================
    private bool WorkOrderFilter(object obj)
    {
        if (obj is not WorkOrder wo) return false;

        var matchStatus = _filterStatus switch
        {
            "En cours"      => !wo.IsValidated && string.IsNullOrEmpty(wo.ValidationDecision) && !wo.HasExpiredLink,
            "Lien expiré"   => wo.HasExpiredLink,
            "Validé"        => wo.ValidationDecision == "Validé",
            "Refusé/Annulé" => wo.ValidationDecision == "Refusé" || wo.ValidationDecision == "Annulé",
            _               => true
        };
        if (!matchStatus) return false;

        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            var q = _filterText.Trim().ToLowerInvariant();
            return (wo.BdrDisplay?.ToLowerInvariant().Contains(q) == true)
                || (wo.PerformedBy?.ToLowerInvariant().Contains(q) == true)
                || (wo.RequestedBy?.ToLowerInvariant().Contains(q) == true)
                || (wo.Place?.ToLowerInvariant().Contains(q) == true)
                || (wo.Reserve?.ToLowerInvariant().Contains(q) == true)
                || (wo.Description?.ToLowerInvariant().Contains(q) == true);
        }

        return true;
    }

    private void ApplyWorkOrderFilter() => _workOrdersView?.Refresh();

    private void SetFilterChip(string status)
    {
        _filterStatus = status;
        var active   = (System.Windows.Style)FindResource("FilterChipActiveStyle");
        var inactive = (System.Windows.Style)FindResource("FilterChipStyle");
        if (FilterAllButton        != null) FilterAllButton.Style        = status == "Tous"           ? active : inactive;
        if (FilterInProgressButton != null) FilterInProgressButton.Style = status == "En cours"       ? active : inactive;
        if (FilterExpiredButton    != null) FilterExpiredButton.Style    = status == "Lien expiré"    ? active : inactive;
        if (FilterValidatedButton  != null) FilterValidatedButton.Style  = status == "Validé"         ? active : inactive;
        if (FilterRefusedButton    != null) FilterRefusedButton.Style    = status == "Refusé/Annulé"  ? active : inactive;
        ApplyWorkOrderFilter();
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e)        => SetFilterChip("Tous");
    private void FilterInProgress_Click(object sender, RoutedEventArgs e) => SetFilterChip("En cours");
    private void FilterExpired_Click(object sender, RoutedEventArgs e)     => SetFilterChip("Lien expiré");
    private void FilterValidated_Click(object sender, RoutedEventArgs e)   => SetFilterChip("Validé");
    private void FilterRefused_Click(object sender, RoutedEventArgs e)     => SetFilterChip("Refusé/Annulé");

    private void WorkOrderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText = WorkOrderSearchBox?.Text ?? "";
        ApplyWorkOrderFilter();
    }

    private void RefreshArchived()
    {
        if (ArchivedGrid == null)
            return;

        if (ProjectComboBox?.SelectedItem is not Project selectedProject)
        {
            ArchivedGrid.ItemsSource = Array.Empty<WorkOrder>();
            return;
        }

        ArchivedGrid.ItemsSource = Db.GetArchivedWorkOrders(selectedProject.Id);
    }

    private void RefreshTrashed()
    {
        if (TrashedGrid == null)
            return;

        if (ProjectComboBox?.SelectedItem is not Project selectedProject)
        {
            TrashedGrid.ItemsSource = Array.Empty<WorkOrder>();
            return;
        }

        TrashedGrid.ItemsSource = Db.GetTrashedWorkOrders(selectedProject.Id);
    }

    private static WorkOrder? GetRowWorkOrder(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WorkOrder wo)
            return wo;

        return null;
    }

    private static WorkOrder? GetSelectedWorkOrder(DataGrid? grid)
    {
        return grid?.SelectedItem as WorkOrder;
    }

    // =========================
    // ✅ SelectionChanged -> maj aperçu + bordure
    // =========================
    private void WorkOrdersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedWorkOrderPreview();
        UpdatePreviewBorderFromSelection();
    }

    // =========================
    // Batch selection (multi)
    // =========================
    private System.Collections.Generic.List<WorkOrder> GetActiveWorkOrders()
    {
        if (WorkOrdersGrid?.ItemsSource == null)
            return new System.Collections.Generic.List<WorkOrder>();

        return WorkOrdersGrid.ItemsSource.Cast<WorkOrder>().ToList();
    }

    private void RefreshBatchSelectionUi()
    {
        var items = GetActiveWorkOrders();
        var selectedCount = items.Count(x => x.IsBatchSelected);

        if (ArchiveSelectionButton != null)
            ArchiveSelectionButton.IsEnabled = !_identityDirty && selectedCount > 0;

        if (TrashSelectionButton != null)
            TrashSelectionButton.IsEnabled = !_identityDirty && selectedCount > 0;
    }

    private void BatchSelectCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender);
        if (wo == null || wo.Id <= 0)
            return;

        if (sender is System.Windows.Controls.CheckBox cb)
            wo.IsBatchSelected = cb.IsChecked == true;

        RefreshBatchSelectionUi();
    }

    private void SelectAllWorkOrdersCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        if (sender is not System.Windows.Controls.CheckBox cb)
            return;

        var items = GetActiveWorkOrders();
        if (items.Count == 0)
            return;

        var newValue = cb.IsChecked != false;

        foreach (var wo in items)
            wo.IsBatchSelected = newValue;

        WorkOrdersGrid?.Items.Refresh();
        RefreshBatchSelectionUi();
    }

    private void ArchiveSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var items = GetActiveWorkOrders().Where(x => x.IsBatchSelected).ToList();
        if (items.Count == 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Archiver {items.Count} bon(s) sélectionné(s) ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in items)
            Db.SetArchived(wo.Id, true);

        RefreshAll();
    }

    private void TrashSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var items = GetActiveWorkOrders().Where(x => x.IsBatchSelected).ToList();
        if (items.Count == 0)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Mettre {items.Count} bon(s) sélectionné(s) à la corbeille ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var wo in items)
            Db.SetTrashed(wo.Id, true);

        RefreshAll();
    }

    // =========================
    // Dates indépendantes (dashboard)
    // =========================
    private void DistributedDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender);
        if (wo == null || wo.Id <= 0)
            return;

        if (sender is DatePicker dp)
        {
            wo.DistributedAt = dp.SelectedDate;
            Db.SetDistributedAt(wo.Id, wo.DistributedAt);

            UpdateSelectedWorkOrderPreview();
            UpdatePreviewBorderFromSelection();
        }
    }

    private void PerformedDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender);
        if (wo == null || wo.Id <= 0)
            return;

        if (sender is DatePicker dp)
        {
            wo.PerformedAt = dp.SelectedDate;
            Db.SetPerformedAt(wo.Id, wo.PerformedAt);

            UpdateSelectedWorkOrderPreview();
            UpdatePreviewBorderFromSelection();
        }
    }

    // =========================
    // Drag & Drop import packages
    // =========================
    private void DashboardPage_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        try
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;

            if (_identityDirty)
                return;

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            if (files.Any(IsIziregiPackageFile))
                e.Effects = System.Windows.DragDropEffects.Copy;
        }
        catch
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void DashboardPage_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        try
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var firstPackage = files.FirstOrDefault(IsIziregiPackageFile);
            if (string.IsNullOrWhiteSpace(firstPackage))
            {
                System.Windows.MessageBox.Show(
                    "Dépose un fichier .iziregi-package pour importer.",
                    "Import package",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ImportPackageFromPath(firstPackage);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’importer le package.\n\n{ex.Message}",
                "Import package",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool IsIziregiPackageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(path)
            && string.Equals(Path.GetExtension(path), ".iziregi-package", StringComparison.OrdinalIgnoreCase);
    }

    private void ImportPackageFromPath(string filePath)
    {
        var imported = PackageImportService.Load(filePath);

        var sourceId = imported.Manifest.WorkOrderId;

        var existing = Db.GetImportedWorkOrderBySourceId(sourceId);
        if (existing != null && existing.IsValidated)
            throw new InvalidOperationException(
                $"Ce bon ({existing.BdrDisplay}) est déjà validé.\n\n" +
                "Pour le refaire, crée un nouveau bon avec un nouveau numéro."
            );

        if (ProjectComboBox?.SelectedItem is Project p)
        {
            var sameNumber = Db.GetWorkOrders(p.Id)
                .FirstOrDefault(x => x.BdrNumber == imported.WorkOrder.BdrNumber);

            if (sameNumber != null && sameNumber.IsValidated)
                throw new InvalidOperationException(
                    $"Un bon {sameNumber.BdrDisplay} est déjà validé dans ce projet.\n\n" +
                    "Pour le refaire, crée un nouveau bon avec un nouveau numéro."
                );
        }

        var id = Db.UpsertImportedWorkOrder_OptionA(imported.WorkOrder, sourceId);
        Db.ReplaceWorkOrderLines(id, imported.Lines);

        var mode = imported.PackageType == "devis"
            ? WorkOrderEditMode.EntrepriseDevis
            : WorkOrderEditMode.Signataire;

        var win = new WorkOrderWindow(id, mode)
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };

        try
        {
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Exception opening WorkOrderWindow from Dashboard: " + ex);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
                // Silent fallback: no MessageBox shown
            }
            catch { }
        }
        RefreshAll();
    }

    // =========================
    // Identité architecte
    // =========================
    private void LoadIdentity()
    {
        _suspendDirtyTracking = true;
        try
        {
            if (ArchitectNameTextBox != null)
                ArchitectNameTextBox.Text = Db.GetArchitectName();

            // ✅ L'adresse architecte est saisie comme un seul champ (avec un
            // retour à la ligne entre la rue et le NPA/Ville). On la découpe ici
            // pour retrouver le même affichage à 2 lignes (Adresse / NPA-Ville)
            // que le bloc "Projet" voisin.
            var fullAddress = (Db.GetArchitectAddress() ?? "").Replace("\r\n", "\n");
            var addressParts = fullAddress.Split('\n');
            var addressLine = addressParts.Length > 0 ? addressParts[0].Trim() : "";
            var addressZipCity = addressParts.Length > 1
                ? string.Join(", ", addressParts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0))
                : "";

            if (ArchitectAddressTextBox != null)
                ArchitectAddressTextBox.Text = addressLine;

            if (ArchitectZipCityTextBox != null)
                ArchitectZipCityTextBox.Text = addressZipCity;

            _logoPath = Db.GetArchitectLogoPath();
            LoadLogoPreview(_logoPath);
        }
        finally
        {
            _suspendDirtyTracking = false;
        }
    }

    private void LoadLogoPreview(string? path)
    {
        if (LogoImage != null)
            LogoImage.Source = null;

        if (LogoEmptyText != null)
            LogoEmptyText.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            if (LogoImage != null)
                LogoImage.Source = bmp;

            if (LogoEmptyText != null)
                LogoEmptyText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            if (LogoImage != null)
                LogoImage.Source = null;

            if (LogoEmptyText != null)
                LogoEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void ImportLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer un logo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        _logoPath = dlg.FileName;
        LoadLogoPreview(_logoPath);

        MarkIdentityDirty(true);
    }

    private void RemoveLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        _logoPath = "";
        LoadLogoPreview("");

        Db.SetArchitectLogoPath("");

        MarkIdentityDirty(true);
    }

    private async System.Threading.Tasks.Task SyncArchitectToServerAsync(string name, string address, string logoPath)
    {
        try
        {
            byte[]? logoBytes = null;
            string? contentType = null;

            if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
            {
                logoBytes = System.IO.File.ReadAllBytes(logoPath);
                var ext = System.IO.Path.GetExtension(logoPath).ToLowerInvariant();
                contentType = ext is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png";
            }

            var payload = new
            {
                name,
                address,
                logoBase64      = logoBytes != null ? Convert.ToBase64String(logoBytes) : (string?)null,
                logoContentType = contentType
            };

            var options = new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json    = System.Text.Json.JsonSerializer.Serialize(payload, options);

            using var client  = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "IziregiClient/1.0"); // ✅ voir MainWindow/WorkOrderWindow
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var url  = $"{MainWindow.ServerBaseUrl}/internal/architect-identity/upsert?apiKey={Uri.EscapeDataString(MainWindow.ServerApiKey)}";
            var resp = await client.PostAsync(url, content);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("SyncArchitectToServerAsync failed: " + ex.Message);
        }
    }

    // =========================
    // Projets
    // =========================
    private void LoadProjects()
    {
        if (ProjectComboBox == null)
            return;

        _isLoadingProjects = true;
        _suspendDirtyTracking = true;

        try
        {
            var projects = Db.GetProjects(true)
                .OrderBy(p => p.Name)
                .ToList();

            ProjectComboBox.ItemsSource = projects;

            var currentId = Db.GetCurrentProjectId();

            if (currentId.HasValue && projects.Any(p => p.Id == currentId.Value))
            {
                ProjectComboBox.SelectedValue = currentId.Value;
            }
            else if (projects.Count > 0)
            {
                ProjectComboBox.SelectedIndex = 0;

                if (ProjectComboBox.SelectedItem is Project p)
                    Db.SetCurrentProjectId(p.Id);
            }
            else
            {
                ProjectComboBox.SelectedItem = null;
            }

            _lastProjectId = ProjectComboBox.SelectedItem is Project cur ? cur.Id : null;

            LoadSelectedProjectIntoFields();
            ApplyDashboardLabels();
        }
        finally
        {
            _suspendDirtyTracking = false;
            _isLoadingProjects = false;
        }
    }

    private void LoadSelectedProjectIntoFields()
    {
        _suspendDirtyTracking = true;
        try
        {
            if (ProjectComboBox?.SelectedItem is Project project)
            {
                if (ProjectNameEditTextBox != null)
                    ProjectNameEditTextBox.Text = project.Name ?? "";

                var raw = project.Address ?? "";
                var (addr, zipCity) = SplitAddressTwoLines(raw);

                if (ProjectAddressEditTextBox != null)
                    ProjectAddressEditTextBox.Text = addr;

                if (ProjectZipCityEditTextBox != null)
                    ProjectZipCityEditTextBox.Text = zipCity;
            }
            else
            {
                if (ProjectNameEditTextBox != null)
                    ProjectNameEditTextBox.Text = "";

                if (ProjectAddressEditTextBox != null)
                    ProjectAddressEditTextBox.Text = "";

                if (ProjectZipCityEditTextBox != null)
                    ProjectZipCityEditTextBox.Text = "";
            }
        }
        finally
        {
            _suspendDirtyTracking = false;
        }
    }

    private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingProjects)
            return;

        if (_identityDirty)
            return;

        if (ProjectComboBox?.SelectedItem is Project project)
        {
            // Informe MainWindow via son API afin qu'il mette à jour son état et l'affichage global
            try
            {
                var mw = Window.GetWindow(this) as Iziregi.Test.MainWindow ?? System.Windows.Application.Current.MainWindow as Iziregi.Test.MainWindow;
                if (mw != null)
                {
                    mw.SetSelectedProject(project);
                    // si MainWindow expose un rafraîchisseur de sélecteur, on l'appelle pour synchroniser l'UI
                    try { mw.RefreshProjectSelector(); } catch { /* non bloquant */ }
                }
                else
                {
                    Db.SetCurrentProjectId(project.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ProjectComboBox_SelectionChanged exception: " + ex);
                Db.SetCurrentProjectId(project.Id);
            }

            _lastProjectId = project.Id;

            LoadSelectedProjectIntoFields();
            ApplyDashboardLabels();
        }

        RefreshAll();
    }

    private void SaveProjectIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        try
        {
            if (ProjectComboBox?.SelectedItem is not Project project)
                throw new InvalidOperationException("Aucun projet sélectionné.");

            project.Name = (ProjectNameEditTextBox?.Text ?? "").Trim();

            var addr = (ProjectAddressEditTextBox?.Text ?? "").Trim();
            var zipCity = (ProjectZipCityEditTextBox?.Text ?? "").Trim();
            project.Address = JoinAddressTwoLines(addr, zipCity);

            if (string.IsNullOrWhiteSpace(project.Name))
                throw new InvalidOperationException("Le nom du projet est obligatoire.");

            if (string.IsNullOrWhiteSpace(addr))
                throw new InvalidOperationException("L’adresse du projet est obligatoire.");

            Db.UpdateProject(project);

            LoadProjects();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’enregistrer le projet.\n\n{ex.Message}",
                "Projet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ManageProjects_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        try
        {
            var win = new ProjectsWindow
            {
                Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
            };
        try
        {
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Exception opening ProjectsWindow from Dashboard: " + ex);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
                System.Windows.MessageBox.Show($"Erreur à l'ouverture de la fenêtre Projets : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

            LoadProjects();
            RefreshAll();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’ouvrir la fenêtre Projets.\n\n{ex.Message}",
                "Projets",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // =========================
    // Boutons principaux
    // =========================
    private void NewWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        if (ProjectComboBox?.SelectedItem is Project project)
            Db.SetCurrentProjectId(project.Id);

        OpenWorkOrderWindow(null, createMode: true);
        RefreshAll();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        LoadIdentity();
        LoadProjects();
        RefreshAll();
    }

    // =========================
    // Handlers XAML (grille principale)
    // =========================
    private void WorkOrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
        RefreshAll();
    }

    private void ViewWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender) ?? GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
        RefreshAll();
    }

    private void EditWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender) ?? GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
        RefreshAll();
    }

    private void ArchiveWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender) ?? GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        var ok = System.Windows.MessageBox.Show(
            $"Archiver le bon {wo.BdrDisplay} ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetArchived(wo.Id, true);
        RefreshAll();
    }

    private void TrashWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender) ?? GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        var ok = System.Windows.MessageBox.Show(
            $"Mettre le bon {wo.BdrDisplay} à la corbeille ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetTrashed(wo.Id, true);
        RefreshAll();
    }

    // =========================
    // Handlers XAML (grilles cachées)
    // =========================
    private void ArchivedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetSelectedWorkOrder(ArchivedGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
        RefreshAll();
    }

    private void TrashedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetSelectedWorkOrder(TrashedGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
        RefreshAll();
    }

    // =========================
    // Helpers (ouverture bon)
    // =========================
    private void OpenWorkOrderWindow(WorkOrder? workOrder, bool createMode)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        Window win;

        if (createMode || workOrder == null || workOrder.Id <= 0)
            win = new WorkOrderWindow();
        else
            win = new WorkOrderWindow(workOrder.Id, WorkOrderEditMode.Architecte);

        win.Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
        try
        {
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Exception opening WorkOrderWindow from Dashboard (helper): " + ex);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
                System.Windows.MessageBox.Show($"Erreur à l'ouverture du bon : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        ApplyDashboardLabels();
    }

    private void ImportResponse_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importer un package Iziregi",
                Filter = "Iziregi package|*.iziregi-package|Tous les fichiers|*.*"
            };

            if (dlg.ShowDialog() != true)
                return;

            ImportPackageFromPath(dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’importer le package.\n\n{ex.Message}",
                "Import package",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenLists_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        System.Windows.MessageBox.Show(
            "Listes : à raccorder dans une prochaine étape.",
            "Iziregi",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenAccounting_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        System.Windows.MessageBox.Show(
            "Comptabilité : à raccorder dans une prochaine étape.",
            "Iziregi",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        System.Windows.MessageBox.Show(
            "Vider la corbeille : à raccorder dans une prochaine étape.",
            "Iziregi",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // =========================
    // ✅ Converters locaux (WPF-only)
    // =========================
    private sealed class CompanyToBrushConverter_Local : IValueConverter
    {
        public WpfBrush FallbackBrush { get; set; } = WpfBrushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var company = (value?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(company))
                    return FallbackBrush;

                var pid = Db.GetCurrentProjectId();
                if (!pid.HasValue || pid.Value <= 0)
                    return FallbackBrush;

                var hex = Db.GetCompanyColorHex(pid.Value, company);
                if (string.IsNullOrWhiteSpace(hex))
                    return FallbackBrush;

                var color = (WpfColor)WpfColorConverter.ConvertFromString(hex);
                var brush = new WpfSolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return FallbackBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class CompanyToForegroundBrushConverter_Local : IValueConverter
    {
        public WpfBrush LightTextBrush { get; set; } = WpfBrushes.White;
        public WpfBrush DarkTextBrush { get; set; } = WpfBrushes.Black;

        public double Threshold { get; set; } = 0.55;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var company = (value?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(company))
                    return DarkTextBrush;

                var pid = Db.GetCurrentProjectId();
                if (!pid.HasValue || pid.Value <= 0)
                    return DarkTextBrush;

                var hex = Db.GetCompanyColorHex(pid.Value, company);
                if (string.IsNullOrWhiteSpace(hex))
                    return DarkTextBrush;

                var color = (WpfColor)WpfColorConverter.ConvertFromString(hex);

                static double SrgbToLinear(double c) => c <= 0.04045 ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4);

                var r = SrgbToLinear(color.R / 255.0);
                var g = SrgbToLinear(color.G / 255.0);
                var b = SrgbToLinear(color.B / 255.0);

                var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                return luminance < Threshold ? LightTextBrush : DarkTextBrush;
            }
            catch
            {
                return DarkTextBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}