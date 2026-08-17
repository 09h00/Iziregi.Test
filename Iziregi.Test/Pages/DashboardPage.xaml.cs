// File: DashboardPage.xaml.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    // =========================
    // ✅ Mode "obliger OK" (dirty strict)
    // =========================
    private bool _identityDirty;
    private bool _suspendDirtyTracking;
    private long? _lastProjectId;

    // ✅ Filtres façon Excel sur les en-têtes de colonnes, portés depuis Archives (24.07.2026,
    // demande de Joe) : remplace les anciennes puces "Tous/En cours/Lien expiré/Validé/Refusé"
    // + case de recherche.
    private System.Collections.Generic.List<WorkOrder> _allWorkOrders = new();

    private enum ColumnKind { Text, Date, Status }

    private static ColumnKind GetColumnKind(string columnKey) => columnKey switch
    {
        "CreatedDate" or "DistributedDate" or "PerformedDate" => ColumnKind.Date,
        "Status" => ColumnKind.Status,
        _ => ColumnKind.Text
    };

    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> _activeValueFilters = new();
    private readonly System.Collections.Generic.Dictionary<string, (DateTime? From, DateTime? To)> _activeDateRangeFilters = new();

    // ✅ Tri par défaut : Créé le décroissant (24.07.2026, demande de Joe).
    private string? _sortColumnKey = "CreatedDate";
    private System.ComponentModel.ListSortDirection _sortDirection = System.ComponentModel.ListSortDirection.Descending;

    // ✅ Distingue le tri par défaut (Créé le décroissant, pas de bordure bleue) d'un tri choisi
    // explicitement par l'utilisateur via une case à cocher (24.07.2026, demande de Joe).
    private bool _sortIsUserChosen;

    private string? _currentFilterColumnKey;
    private System.Collections.Generic.List<FilterOption> _filterPopupOptions = new();

    // ✅ Champ de recherche conservé à côté des filtres par colonne (24.07.2026, demande de Joe).
    private string _searchText = "";

    private void WorkOrderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = WorkOrderSearchBox?.Text ?? "";
        ApplyFilters();
    }

    private bool MatchesSearch(WorkOrder wo)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        var q = _searchText.Trim().ToLowerInvariant();
        return (wo.BdrDisplay?.ToLowerInvariant().Contains(q) == true)
            || (wo.PerformedBy?.ToLowerInvariant().Contains(q) == true)
            || (wo.RequestedBy?.ToLowerInvariant().Contains(q) == true)
            || (wo.Place?.ToLowerInvariant().Contains(q) == true)
            || (wo.Reserve?.ToLowerInvariant().Contains(q) == true)
            || (wo.Description?.ToLowerInvariant().Contains(q) == true);
    }

    public class FilterOption : System.ComponentModel.INotifyPropertyChanged
    {
        public string Value { get; set; } = "";

        // ✅ Nombre de bons ayant cette valeur (28.07.2026, demande de Joe), affiché à côté
        // de chaque case dans le popup de filtre plutôt qu'en compteurs permanents sur la
        // page (trop de valeurs possibles pour Intervenants/Demandé par notamment).
        public int Count { get; set; }
        public string DisplayText => $"{Value}  ({Count})";

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private static string GetColumnValue(WorkOrder w, string columnKey) => columnKey switch
    {
        "Company" => string.IsNullOrWhiteSpace(w.PerformedBy) ? "(Non défini)" : w.PerformedBy.Trim(),
        "Concerne" => string.IsNullOrWhiteSpace(w.Reserve) ? "(Non défini)" : w.Reserve.Trim(),
        "RequestedBy" => string.IsNullOrWhiteSpace(w.RequestedBy) ? "(Non défini)" : w.RequestedBy.Trim(),
        "Status" => GetStatusLabel(w),
        _ => ""
    };

    // ✅ Les 5 colonnes Statut sont des drapeaux cumulatifs : cette méthode réduit ça à une seule
    // étiquette représentant l'étape la plus avancée atteinte, pour le filtre à cases à cocher.
    private static string GetStatusLabel(WorkOrder w)
    {
        var decision = (w.ValidationDecision ?? "").Trim();
        if (string.Equals(decision, "Validé", StringComparison.OrdinalIgnoreCase)) return "Validé";
        if (string.Equals(decision, "Refusé", StringComparison.OrdinalIgnoreCase)) return "Refusé";
        if (string.Equals(decision, "Annulé", StringComparison.OrdinalIgnoreCase)) return "Annulé";
        if (w.IsSentToSigner) return "Demande validation envoyée";
        if (w.IsQuoteReceived) return "Devis reçu";
        if (w.IsSentToCompany) return "Demande devis envoyée";
        return "Créé";
    }

    // ✅ 6 statuts possibles en tout, toujours proposés dans la liste à cocher (même si aucun bon
    // n'a actuellement tel ou tel statut).
    private static readonly string[] AllStatusLabels =
    {
        "Demande devis envoyée",
        "Devis reçu",
        "Demande validation envoyée",
        "Validé",
        "Refusé",
        "Annulé"
    };

    private static DateTime? GetDateValue(WorkOrder w, string columnKey) => columnKey switch
    {
        "CreatedDate" => w.RequestDate,
        "DistributedDate" => w.DistributedAt,
        "PerformedDate" => w.PerformedAt,
        _ => null
    };

    // ✅ Libellés propres à chaque colonne date (28.07.2026, demande de Joe) plutôt qu'un
    // générique "Renseigné/Non renseigné" pour toutes.
    private static (string Filled, string Empty) GetDateFilterCountLabels(string columnKey) => columnKey switch
    {
        "DistributedDate" => ("Distribué", "Non distribué"),
        "PerformedDate" => ("Effectué", "Non effectué"),
        _ => ("Renseigné", "Non renseigné")
    };

    private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string columnKey) return;

        _currentFilterColumnKey = columnKey;
        var kind = GetColumnKind(columnKey);

        if (kind == ColumnKind.Date)
        {
            TextFilterPanel.Visibility = Visibility.Collapsed;
            DateFilterPanel.Visibility = Visibility.Visible;

            DateAscCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == System.ComponentModel.ListSortDirection.Ascending;
            DateDescCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == System.ComponentModel.ListSortDirection.Descending;

            var hasRange = _activeDateRangeFilters.TryGetValue(columnKey, out var range);
            DateFromPicker.SelectedDate = hasRange ? range.From : null;
            DateToPicker.SelectedDate = hasRange ? range.To : null;

            var withDate = _allWorkOrders.Count(w => GetDateValue(w, columnKey).HasValue);
            var withoutDate = _allWorkOrders.Count - withDate;
            var (filledLabel, emptyLabel) = GetDateFilterCountLabels(columnKey);
            DateFilterCountsTextBlock.Text = $"{filledLabel}  ({withDate})    {emptyLabel}  ({withoutDate})";
        }
        else
        {
            DateFilterPanel.Visibility = Visibility.Collapsed;
            TextFilterPanel.Visibility = Visibility.Visible;

            AlphaSortCheckBox.Visibility = kind == ColumnKind.Status ? Visibility.Collapsed : Visibility.Visible;
            AlphaSortCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == System.ComponentModel.ListSortDirection.Ascending;

            var allValues = kind == ColumnKind.Status
                ? AllStatusLabels.ToList()
                : _allWorkOrders
                    .Select(w => GetColumnValue(w, columnKey))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var counts = _allWorkOrders
                .GroupBy(w => kind == ColumnKind.Status ? GetStatusLabel(w) : GetColumnValue(w, columnKey), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var active = _activeValueFilters.TryGetValue(columnKey, out var set) ? set : null;

            _filterPopupOptions = allValues
                .Select(v => new FilterOption { Value = v, Count = counts.TryGetValue(v, out var c) ? c : 0, IsChecked = active == null || active.Contains(v) })
                .ToList();

            FilterOptionsItemsControl.ItemsSource = _filterPopupOptions;
            FilterSelectAllCheckBox.IsChecked = _filterPopupOptions.All(o => o.IsChecked);
        }

        ColumnFilterPopup.PlacementTarget = fe;
        ColumnFilterPopup.IsOpen = true;
    }

    private void FilterSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var check = FilterSelectAllCheckBox.IsChecked == true;
        foreach (var o in _filterPopupOptions)
            o.IsChecked = check;
    }

    private void DateAscCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (DateAscCheckBox.IsChecked == true) DateDescCheckBox.IsChecked = false;
    }

    private void DateDescCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (DateDescCheckBox.IsChecked == true) DateAscCheckBox.IsChecked = false;
    }

    private void ResetDateRangeButton_Click(object sender, RoutedEventArgs e)
    {
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
    }

    private void ColumnFilterCancel_Click(object sender, RoutedEventArgs e)
    {
        ColumnFilterPopup.IsOpen = false;
    }

    private void ColumnFilterApply_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilterColumnKey == null)
        {
            ColumnFilterPopup.IsOpen = false;
            return;
        }

        var columnKey = _currentFilterColumnKey;
        var kind = GetColumnKind(columnKey);

        if (kind == ColumnKind.Date)
        {
            var from = DateFromPicker.SelectedDate?.Date;
            var to = DateToPicker.SelectedDate?.Date;

            if (from.HasValue || to.HasValue)
                _activeDateRangeFilters[columnKey] = (from, to);
            else
                _activeDateRangeFilters.Remove(columnKey);

            if (DateAscCheckBox.IsChecked == true) { _sortColumnKey = columnKey; _sortDirection = System.ComponentModel.ListSortDirection.Ascending; _sortIsUserChosen = true; }
            else if (DateDescCheckBox.IsChecked == true) { _sortColumnKey = columnKey; _sortDirection = System.ComponentModel.ListSortDirection.Descending; _sortIsUserChosen = true; }
            else if (_sortColumnKey == columnKey) { _sortColumnKey = null; _sortIsUserChosen = false; }
        }
        else
        {
            var checkedValues = _filterPopupOptions.Where(o => o.IsChecked).Select(o => o.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (checkedValues.Count == _filterPopupOptions.Count)
                _activeValueFilters.Remove(columnKey);
            else
                _activeValueFilters[columnKey] = checkedValues;

            if (kind != ColumnKind.Status && AlphaSortCheckBox.IsChecked == true)
            {
                _sortColumnKey = columnKey;
                _sortDirection = System.ComponentModel.ListSortDirection.Ascending;
                _sortIsUserChosen = true;
            }
            else if (_sortColumnKey == columnKey)
            {
                _sortColumnKey = null;
                _sortIsUserChosen = false;
            }
        }

        ColumnFilterPopup.IsOpen = false;

        UpdateFilterButtonIndicators();
        ApplyFilters();
    }

    private void UpdateFilterButtonIndicators()
    {
        SetFilterHeaderState(ConcerneFilterButton, ConcerneHeaderBorder, "Concerne");
        SetFilterHeaderState(RequestedByFilterButton, RequestedByHeaderBorder, "RequestedBy");
        SetFilterHeaderState(CompanyFilterButton, CompanyHeaderBorder, "Company");
        SetFilterHeaderState(CreatedDateFilterButton, CreatedDateHeaderBorder, "CreatedDate");
        SetFilterHeaderState(DistributedDateFilterButton, DistributedDateHeaderBorder, "DistributedDate");
        SetFilterHeaderState(PerformedDateFilterButton, PerformedDateHeaderBorder, "PerformedDate");
        SetFilterHeaderState(StatusFilterButton, StatusHeaderBorder, "Status");
    }

    private bool IsColumnFilterActive(string columnKey) =>
        _activeValueFilters.ContainsKey(columnKey) ||
        _activeDateRangeFilters.ContainsKey(columnKey) ||
        (_sortColumnKey == columnKey && _sortIsUserChosen);

    private void SetFilterHeaderState(System.Windows.Controls.Button? button, System.Windows.Controls.Border? headerBorder, string columnKey)
    {
        var active = IsColumnFilterActive(columnKey);

        if (headerBorder != null)
            headerBorder.BorderBrush = active
                ? new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2563EB")!)
                : WpfBrushes.Transparent;

        SetFilterButtonStyle(button, columnKey);
    }

    private void SetFilterButtonStyle(System.Windows.Controls.Button? button, string columnKey)
    {
        if (button == null) return;
        button.Style = (Style)(IsColumnFilterActive(columnKey)
            ? FindResource("FilterHeaderButtonActiveStyle")
            : FindResource("FilterHeaderButtonStyle"));
    }

    private void ApplyFilters()
    {
        var filtered = _allWorkOrders.Where(w =>
        {
            if (!MatchesSearch(w)) return false;

            foreach (var kvp in _activeValueFilters)
            {
                var value = GetColumnValue(w, kvp.Key);
                if (kvp.Key == "Status" && value == "Créé") continue;
                if (!kvp.Value.Contains(value)) return false;
            }

            foreach (var kvp in _activeDateRangeFilters)
            {
                var d = GetDateValue(w, kvp.Key);
                var (from, to) = kvp.Value;

                if (d == null) return false;
                if (from.HasValue && d.Value.Date < from.Value.Date) return false;
                if (to.HasValue && d.Value.Date > to.Value.Date) return false;
            }

            return true;
        });

        System.Collections.Generic.List<WorkOrder> sorted;

        if (_sortColumnKey == "CreatedDate" || _sortColumnKey == "DistributedDate" || _sortColumnKey == "PerformedDate")
        {
            Func<WorkOrder, DateTime> dateKey = _sortColumnKey switch
            {
                "CreatedDate" => w => w.RequestDate,
                "DistributedDate" => w => w.DistributedAt ?? DateTime.MinValue,
                "PerformedDate" => w => w.PerformedAt ?? DateTime.MinValue,
                _ => w => w.RequestDate
            };

            sorted = (_sortDirection == System.ComponentModel.ListSortDirection.Ascending
                ? filtered.OrderBy(dateKey)
                : filtered.OrderByDescending(dateKey)).ToList();
        }
        else if (_sortColumnKey != null)
        {
            Func<WorkOrder, string> textKey = _sortColumnKey switch
            {
                "Concerne" => w => w.Reserve ?? "",
                "RequestedBy" => w => w.RequestedBy ?? "",
                "Company" => w => w.PerformedBy ?? "",
                _ => w => w.PerformedBy ?? ""
            };

            sorted = (_sortDirection == System.ComponentModel.ListSortDirection.Ascending
                ? filtered.OrderBy(textKey, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(textKey, StringComparer.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            sorted = filtered.OrderBy(w => w.RequestDate).ToList();
        }

        WorkOrdersGrid.ItemsSource = sorted;

        RefreshBatchSelectionUi();
    }

    // ✅ État par défaut : Créé le décroissant, sans bordure bleue (24.07.2026, demande de Joe) —
    // utilisé par "Rafraîchir" (voir Refresh_Click), distinct d'un tri choisi explicitement
    // (qui, lui, affiche la bordure même si c'est le même Créé le décroissant). Le bouton
    // "Effacer filtres" séparé a été retiré le 28.07.2026 (redondant avec "Rafraîchir", qui
    // fait tout ce qu'il faisait en plus de recharger les données).
    private void ResetFiltersAndSortToDefault()
    {
        _activeValueFilters.Clear();
        _activeDateRangeFilters.Clear();
        _sortColumnKey = "CreatedDate";
        _sortDirection = System.ComponentModel.ListSortDirection.Descending;
        _sortIsUserChosen = false;

        _searchText = "";
        if (WorkOrderSearchBox != null) WorkOrderSearchBox.Text = "";
    }

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
        LoadProjects();
        RefreshAll();
    }

    // =========================
    // ✅ Dirty tracking (strict)
    // =========================
    private void HookDirtyTracking()
    {
        // ✅ Les infos du dossier sont affichées en texte simple (comme dans les
        // bons/PDF) sur le Dashboard, pas éditables ici : pas de suivi dirty nécessaire.

        // Empêche changement projet si dirty (rollback)
        if (ProjectComboBox != null)
        {
            ProjectComboBox.SelectionChanged -= ProjectComboBox_SelectionChanged_DirtyProxy;
            ProjectComboBox.SelectionChanged += ProjectComboBox_SelectionChanged_DirtyProxy;
        }
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
            "Modifications non enregistrées.\n\nClique sur OK pour enregistrer avant de changer de dossier.",
            "Modifications non enregistrées",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    // ✅ Fix (04.08.2026, demande de Joe) : "Archiver sélection"/"Déplacer dans la corbeille"
    // ne réagissaient qu'aux cases cochées -- une ligne sélectionnée (bordure bleue, sans
    // cocher sa case) doit aussi les activer.
    private bool AnyBatchSelected()
    {
        try
        {
            var items = GetActiveWorkOrders();
            if (items.Any(x => x.IsBatchSelected)) return true;
            return WorkOrdersGrid?.SelectedItem != null;
        }
        catch
        {
            return false;
        }
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

            // ✅ Pas de trigger IsSelected : la couleur entreprise reste inchangée quand la
            // ligne est sélectionnée (seule la bordure bleue de la ligne doit apparaître).

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

        UpdateDashboardCounters();
    }

    // ✅ Compteurs bons actifs/archivés + tâches en cours (28.07.2026, demande de Joe).
    private void UpdateDashboardCounters()
    {
        try
        {
            if (ProjectComboBox?.SelectedItem is not Project selectedProject)
            {
                if (ActiveWorkOrdersCountRun != null) ActiveWorkOrdersCountRun.Text = "0";
                if (ArchivedWorkOrdersCountRun != null) ArchivedWorkOrdersCountRun.Text = "0";
                if (TrashedWorkOrdersCountRun != null) TrashedWorkOrdersCountRun.Text = "0";
                return;
            }

            if (ActiveWorkOrdersCountRun != null)
                ActiveWorkOrdersCountRun.Text = _allWorkOrders.Count.ToString(CultureInfo.InvariantCulture);

            if (ArchivedWorkOrdersCountRun != null)
                ArchivedWorkOrdersCountRun.Text = Db.GetArchivedWorkOrdersCount(selectedProject.Id).ToString(CultureInfo.InvariantCulture);

            if (TrashedWorkOrdersCountRun != null)
                TrashedWorkOrdersCountRun.Text = Db.GetTrashedWorkOrders(selectedProject.Id).Count.ToString(CultureInfo.InvariantCulture);
        }
        catch { }
    }

    private void RefreshWorkOrders()
    {
        if (WorkOrdersGrid == null)
            return;

        if (ProjectComboBox?.SelectedItem is not Project selectedProject)
        {
            _allWorkOrders = new System.Collections.Generic.List<WorkOrder>();
            WorkOrdersGrid.ItemsSource = Array.Empty<WorkOrder>();
            RefreshBatchSelectionUi();
            UpdateSelectedWorkOrderPreview();
            UpdatePreviewBorderFromSelection();
            return;
        }

        _allWorkOrders = Db.GetWorkOrders(selectedProject.Id);

        foreach (var wo in _allWorkOrders)
            wo.IsBatchSelected = false;

        ApplyFilters();
        UpdateFilterButtonIndicators();
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
        RefreshBatchSelectionUi();
    }

    // ✅ 25.07.2026, demande de Joe : bouton flottant pour revenir en haut de la liste.
    private void ScrollToTopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WorkOrdersGrid.Items.Count > 0)
                WorkOrdersGrid.ScrollIntoView(WorkOrdersGrid.Items[0]);
        }
        catch { }
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
        var anySelected = AnyBatchSelected();

        if (ArchiveSelectionButton != null)
            ArchiveSelectionButton.IsEnabled = !_identityDirty && anySelected;

        if (TrashSelectionButton != null)
            TrashSelectionButton.IsEnabled = !_identityDirty && anySelected;
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

    // ✅ Fallback ligne sélectionnée (04.08.2026, demande de Joe), même principe que
    // PlanningPage.ArchiveTaskRowButton_Click/RemoveTaskRowButton_Click.
    private System.Collections.Generic.List<WorkOrder> GetBatchOrRowSelectedWorkOrders()
    {
        var items = GetActiveWorkOrders().Where(x => x.IsBatchSelected).ToList();
        if (items.Count == 0 && WorkOrdersGrid?.SelectedItem is WorkOrder selected)
            items.Add(selected);
        return items;
    }

    private void ArchiveSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var items = GetBatchOrRowSelectedWorkOrders();
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

        var items = GetBatchOrRowSelectedWorkOrders();
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
                    $"Un bon {sameNumber.BdrDisplay} est déjà validé dans ce dossier.\n\n" +
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

                // ✅ Dossier verrouillé (18.08.2026, demande de Joe) : passe par
                // MainWindow.SetSelectedProject (demande le mot de passe si nécessaire) au lieu
                // de Db.SetCurrentProjectId directement -- sinon ce repli "aucun dossier courant
                // valide -> prend le premier de la liste" activait n'importe quel dossier, verrouillé
                // ou pas, sans vérification. Différé (Dispatcher) pour éviter la réentrance : cet
                // appel peut recréer cette page (RecreatePagesForProject) alors qu'on est encore
                // dans son propre LoadProjects().
                if (ProjectComboBox.SelectedItem is Project p)
                {
                    var mw = Window.GetWindow(this) as Iziregi.Test.MainWindow ?? System.Windows.Application.Current.MainWindow as Iziregi.Test.MainWindow;
                    if (mw != null)
                        Dispatcher.BeginInvoke(new Action(() => mw.SetSelectedProject(p)));
                }
            }
            else
            {
                ProjectComboBox.SelectedItem = null;
            }

            _lastProjectId = ProjectComboBox.SelectedItem is Project cur ? cur.Id : null;

            ApplyDashboardLabels();
        }
        finally
        {
            _suspendDirtyTracking = false;
            _isLoadingProjects = false;
        }
    }

    // ✅ LoadSelectedProjectIntoFields() supprimée (demande de Joe, 04.08.2026) : elle ne
    // faisait que remplir ProjectNameEditTextBox/ProjectAddressEditTextBox/
    // ProjectZipCityEditTextBox/ProjectManagerLineTextBox, retirés avec la section "Adresse
    // dossier" (remplacée par le Carnet d'adresses, voir AddressBookButton_Click).

    private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingProjects)
            return;

        if (_identityDirty)
            return;

        if (ProjectComboBox?.SelectedItem is Project project)
        {
            // Informe MainWindow via son API afin qu'il mette à jour son état et l'affichage global
            // ✅ Dossier verrouillé (18.08.2026, demande de Joe) : pas de repli vers
            // Db.SetCurrentProjectId direct si mw est introuvable/exception -- ça contournerait la
            // vérification du mot de passe (voir MainWindow.SetSelectedProject/TryUnlockProject).
            try
            {
                var mw = Window.GetWindow(this) as Iziregi.Test.MainWindow ?? System.Windows.Application.Current.MainWindow as Iziregi.Test.MainWindow;
                if (mw != null)
                {
                    mw.SetSelectedProject(project);
                    // si MainWindow expose un rafraîchisseur de sélecteur, on l'appelle pour synchroniser l'UI
                    try { mw.RefreshProjectSelector(); } catch { /* non bloquant */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ProjectComboBox_SelectionChanged exception: " + ex);
            }

            _lastProjectId = project.Id;

            ApplyDashboardLabels();
        }

        RefreshAll();
    }

    // ✅ Carnet d'adresses (04.08.2026) : bouton retiré de cette page (demande de Joe),
    // l'accès passe désormais par la barre de navigation globale (MainWindow.
    // NavAddressBook_Click), accessible depuis n'importe quelle page.

    // ✅ Archives/Corbeille (04.08.2026, demande de Joe, compromis "menu trop long") :
    // "Archives" et "Corbeille" retirées du menu global, ces badges (déjà affichés pour montrer
    // les compteurs) deviennent le sous-menu d'accès pour la variante Bons -- même mécanisme que
    // ArchivedRow_Click/TrashRow_Click sur OverviewPage.
    private void ArchivedWorkOrdersBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        GetHost()?.ShowArchives();
    }

    private void TrashedWorkOrdersBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        GetHost()?.ShowTrash();
    }

    // ✅ Boutons explicites (04.08.2026, demande de Joe : "je n'ai pas de boutons Archives et
    // Corbeille"), même navigation que les badges ci-dessus, juste une signature d'événement
    // différente (Button.Click plutôt que MouseLeftButtonDown sur le Border du badge).
    private void ArchivesTopButton_Click(object sender, RoutedEventArgs e) => GetHost()?.ShowArchives();

    private void TrashTopButton_Click(object sender, RoutedEventArgs e) => GetHost()?.ShowTrash();

    private Iziregi.Test.MainWindow? GetHost() =>
        Window.GetWindow(this) as Iziregi.Test.MainWindow ?? System.Windows.Application.Current.MainWindow as Iziregi.Test.MainWindow;

    // =========================
    // Boutons principaux
    // =========================
    private void NewWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        // ✅ Dossier verrouillé (18.08.2026, demande de Joe) : plus de Db.SetCurrentProjectId
        // direct ici -- le dossier courant est déjà activé (et vérifié) via
        // ProjectComboBox_SelectionChanged/MainWindow.SetSelectedProject dès qu'il change.
        OpenWorkOrderWindow(null, createMode: true);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        ResetFiltersAndSortToDefault();

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
    }

    private void ViewWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetRowWorkOrder(sender) ?? GetSelectedWorkOrder(WorkOrdersGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
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
    }

    private void TrashedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        var wo = GetSelectedWorkOrder(TrashedGrid);
        if (wo == null) return;

        OpenWorkOrderWindow(wo, createMode: false);
    }

    // =========================
    // Helpers (ouverture bon)
    // =========================
    // ✅ Fix (demande de Joe : "je veux pouvoir travailler sur les 2 fenêtres sans avoir à les
    // fermer") : ShowDialog (modal) -> Show (non modal). Garde-fous contre l'ouverture en
    // double du même bon / de deux bons "Nouveau" en même temps (voir WorkOrderWindow.
    // ActivateIfAlreadyOpen/ActivateExistingCreateModeWindow). RefreshAll/ApplyDashboardLabels,
    // qui se faisaient après la fermeture (ShowDialog bloquant jusque-là), se font maintenant
    // sur l'événement Closed — les appelants n'ont plus besoin de le refaire eux-mêmes.
    private void OpenWorkOrderWindow(WorkOrder? workOrder, bool createMode)
    {
        if (!EnsureNotDirtyOrWarn()) return;

        Window win;

        if (createMode || workOrder == null || workOrder.Id <= 0)
        {
            if (WorkOrderWindow.ActivateExistingCreateModeWindow()) return;
            win = new WorkOrderWindow();
        }
        else
        {
            if (WorkOrderWindow.ActivateIfAlreadyOpen(workOrder.Id)) return;
            win = new WorkOrderWindow(workOrder.Id, WorkOrderEditMode.Architecte);
        }

        win.Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
        win.Closed += (s, e) => { try { RefreshAll(); ApplyDashboardLabels(); } catch { } };
        try
        {
            win.Show();
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

                var isGradient = Db.GetCompanyIsGradient(pid.Value, company);
                var brush = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, isGradient);
                if (brush == null) return FallbackBrush;
                if (brush.CanFreeze) brush.Freeze();
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