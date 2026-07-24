// File: Pages/ArchivesPage.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        // ✅ Regroupement par entreprise, ordre alphabétique (24.07.2026, demande de Joe).
        public string CompanyName { get; set; } = "";

        // ✅ Propriétés à plat pour le tri (24.07.2026, demande de Joe : le tri alphabétique ne
        // fonctionnait pas sur Entreprise/Demandé par) : un chemin imbriqué "WorkOrder.X" dans
        // SortDescription s'est révélé peu fiable — on expose directement les valeurs à trier ici.
        public string ConcerneSortValue => WorkOrder.Reserve ?? "";
        public string RequestedBySortValue => WorkOrder.RequestedBy ?? "";
        public DateTime CreatedDateSortValue => WorkOrder.RequestDate;
        public DateTime DistributedDateSortValue => WorkOrder.DistributedAt ?? DateTime.MinValue;
        public DateTime PerformedDateSortValue => WorkOrder.PerformedAt ?? DateTime.MinValue;
    }

    private List<WorkOrderRow> _rows = new();
    private List<WorkOrderRow> _allRows = new();

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
            _allRows = new List<WorkOrderRow>();
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

        _allRows = list.Select(w =>
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
                CompanyTextBrush = fg,
                CompanyName = company
            };
        }).ToList();

        ApplyFilters();
        UpdateFilterButtonIndicators();
    }

    // ✅ Filtres façon Excel sur les en-têtes de colonnes, refonte complète (24.07.2026, demande de
    // Joe) : chaque colonne filtrable a un bouton "▾" qui ouvre ColumnFilterPopup. Deux modes :
    // - Texte/Statut : case "Trier par ordre alphabétique" + liste de valeurs à cocher.
    // - Date : cases "Trier croissant"/"Trier décroissant" (mutuellement exclusives) + plage de
    //   dates (Du/Au). _sortColumnKey/_sortDirection ne retient qu'UN tri actif à la fois, appliqué
    //   à l'intérieur du regroupement par entreprise (qui reste la structure de base).
    private enum ColumnKind { Text, Date, Status }

    private static ColumnKind GetColumnKind(string columnKey) => columnKey switch
    {
        "CreatedDate" or "DistributedDate" or "PerformedDate" => ColumnKind.Date,
        "Status" => ColumnKind.Status,
        _ => ColumnKind.Text
    };

    private readonly Dictionary<string, HashSet<string>> _activeValueFilters = new();
    private readonly Dictionary<string, (DateTime? From, DateTime? To)> _activeDateRangeFilters = new();

    // ✅ Tri par défaut : Créé le décroissant (24.07.2026, demande de Joe). Reste ensuite tel quel
    // (Reload() ne le réinitialise pas) tant que l'utilisateur ne choisit pas un autre tri.
    private string? _sortColumnKey = "CreatedDate";
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    // ✅ Distingue le tri par défaut (Créé le décroissant, pas de bordure bleue) d'un tri choisi
    // explicitement par l'utilisateur via une case à cocher (24.07.2026, demande de Joe) : les deux
    // peuvent avoir la même colonne/direction, seule cette variable change l'affichage de la bordure.
    private bool _sortIsUserChosen;

    private string? _currentFilterColumnKey;
    private List<FilterOption> _filterPopupOptions = new();

    public class FilterOption : INotifyPropertyChanged
    {
        public string Value { get; set; } = "";

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static string GetColumnValue(WorkOrderRow r, string columnKey) => columnKey switch
    {
        "Company" => r.CompanyName,
        "Concerne" => string.IsNullOrWhiteSpace(r.WorkOrder.Reserve) ? "(Non défini)" : r.WorkOrder.Reserve.Trim(),
        "RequestedBy" => string.IsNullOrWhiteSpace(r.WorkOrder.RequestedBy) ? "(Non défini)" : r.WorkOrder.RequestedBy.Trim(),
        "Status" => GetStatusLabel(r.WorkOrder),
        _ => ""
    };

    // ✅ Les 5 colonnes Statut sont des drapeaux cumulatifs (un bon validé a aussi IsSentToSigner,
    // IsQuoteReceived, etc. à True) : cette méthode réduit ça à une seule étiquette représentant
    // l'étape la plus avancée atteinte, pour le filtre à cases à cocher (idem Dashboard, qui
    // regroupe déjà Refusé/Annulé ensemble — voir WorkOrderFilter dans DashboardPage.xaml.cs).
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

    // ✅ 6 statuts possibles en tout (24.07.2026, demande de Joe : enlever "Créé", séparer
    // "Refusé" et "Annulé") : toujours proposés dans la liste à cocher, même si aucun bon archivé
    // n'a actuellement tel ou tel statut (contrairement aux autres colonnes où la liste ne montre
    // que les valeurs réellement présentes).
    private static readonly string[] AllStatusLabels =
    {
        "Demande devis envoyée",
        "Devis reçu",
        "Demande validation envoyée",
        "Validé",
        "Refusé",
        "Annulé"
    };

    private static DateTime? GetDateValue(WorkOrderRow r, string columnKey) => columnKey switch
    {
        "CreatedDate" => r.WorkOrder.RequestDate,
        "DistributedDate" => r.WorkOrder.DistributedAt,
        "PerformedDate" => r.WorkOrder.PerformedAt,
        _ => null
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

            DateAscCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == ListSortDirection.Ascending;
            DateDescCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == ListSortDirection.Descending;

            var hasRange = _activeDateRangeFilters.TryGetValue(columnKey, out var range);
            DateFromPicker.SelectedDate = hasRange ? range.From : null;
            DateToPicker.SelectedDate = hasRange ? range.To : null;
        }
        else
        {
            DateFilterPanel.Visibility = Visibility.Collapsed;
            TextFilterPanel.Visibility = Visibility.Visible;

            AlphaSortCheckBox.Visibility = kind == ColumnKind.Status ? Visibility.Collapsed : Visibility.Visible;
            AlphaSortCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == ListSortDirection.Ascending;

            // ✅ Statut : les 6 valeurs possibles sont toujours proposées, dans l'ordre du pipeline
            // (pas alphabétique) — pas seulement celles présentes parmi les bons archivés actuels.
            var allValues = kind == ColumnKind.Status
                ? AllStatusLabels.ToList()
                : _allRows
                    .Select(r => GetColumnValue(r, columnKey))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var active = _activeValueFilters.TryGetValue(columnKey, out var set) ? set : null;

            _filterPopupOptions = allValues
                .Select(v => new FilterOption { Value = v, IsChecked = active == null || active.Contains(v) })
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

    // ✅ "Réinitialiser les dates sélectionnées" (24.07.2026, demande de Joe) : vide juste la
    // plage Du/Au dans le pop-up (le tri croissant/décroissant reste inchangé) ; il faut encore
    // cliquer OK pour appliquer, comme toute autre modification dans ce pop-up.
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

            if (DateAscCheckBox.IsChecked == true) { _sortColumnKey = columnKey; _sortDirection = ListSortDirection.Ascending; _sortIsUserChosen = true; }
            else if (DateDescCheckBox.IsChecked == true) { _sortColumnKey = columnKey; _sortDirection = ListSortDirection.Descending; _sortIsUserChosen = true; }
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
                _sortDirection = ListSortDirection.Ascending;
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

    // ✅ Bordure bleue autour de l'en-tête tant qu'une sélection (tri ou filtre) a été faite via
    // cette case (24.07.2026, demande de Joe), en plus de la flèche déjà mise en bleu.
    private void SetFilterHeaderState(System.Windows.Controls.Button? button, System.Windows.Controls.Border? headerBorder, string columnKey)
    {
        var active = IsColumnFilterActive(columnKey);

        if (headerBorder != null)
            headerBorder.BorderBrush = active
                ? new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#2563EB")!)
                : MediaBrushes.Transparent;

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
        var filtered = _allRows.Where(r =>
        {
            foreach (var kvp in _activeValueFilters)
            {
                var value = GetColumnValue(r, kvp.Key);

                // ✅ "Créé" retiré des choix du filtre Statut (24.07.2026, demande de Joe) : un bon
                // encore à cette étape ne doit jamais être masqué par ce filtre puisqu'il n'a plus
                // de case correspondante à cocher/décocher.
                if (kvp.Key == "Status" && value == "Créé") continue;

                if (!kvp.Value.Contains(value)) return false;
            }

            foreach (var kvp in _activeDateRangeFilters)
            {
                var d = GetDateValue(r, kvp.Key);
                var (from, to) = kvp.Value;

                if (d == null) return false;
                if (from.HasValue && d.Value.Date < from.Value.Date) return false;
                if (to.HasValue && d.Value.Date > to.Value.Date) return false;
            }

            return true;
        });

        // ✅ Tri fait directement en LINQ, sans passer par ListCollectionView.SortDescriptions
        // (24.07.2026, demande de Joe : le tri alphabétique ne fonctionnait nulle part avec
        // SortDescriptions — mécanisme abandonné au profit d'un tri explicite, fiable à coup sûr).
        if (_sortColumnKey == "CreatedDate" || _sortColumnKey == "DistributedDate" || _sortColumnKey == "PerformedDate")
        {
            Func<WorkOrderRow, DateTime> dateKey = _sortColumnKey switch
            {
                "CreatedDate" => r => r.CreatedDateSortValue,
                "DistributedDate" => r => r.DistributedDateSortValue,
                "PerformedDate" => r => r.PerformedDateSortValue,
                _ => r => r.CreatedDateSortValue
            };

            _rows = (_sortDirection == ListSortDirection.Ascending
                ? filtered.OrderBy(dateKey)
                : filtered.OrderByDescending(dateKey)).ToList();
        }
        else if (_sortColumnKey != null)
        {
            Func<WorkOrderRow, string> textKey = _sortColumnKey switch
            {
                "Concerne" => r => r.ConcerneSortValue,
                "RequestedBy" => r => r.RequestedBySortValue,
                "Company" => r => r.CompanyName,
                _ => r => r.CompanyName
            };

            _rows = (_sortDirection == ListSortDirection.Ascending
                ? filtered.OrderBy(textKey, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(textKey, StringComparer.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            _rows = filtered.OrderBy(r => r.CreatedDateSortValue).ToList();
        }

        ArchivedGrid.ItemsSource = _rows;

        RefreshBatchSelectionUi();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ResetFiltersAndSortToDefault();
        UpdateFilterButtonIndicators();
        ApplyFilters();
    }

    // ✅ État par défaut : Créé le décroissant, sans bordure bleue (24.07.2026, demande de Joe) —
    // utilisé par "Effacer filtres" et par "Rafraîchir" (voir Refresh_Click), distinct d'un tri
    // choisi explicitement (qui, lui, affiche la bordure même si c'est le même Créé le décroissant).
    private void ResetFiltersAndSortToDefault()
    {
        _activeValueFilters.Clear();
        _activeDateRangeFilters.Clear();
        _sortColumnKey = "CreatedDate";
        _sortDirection = ListSortDirection.Descending;
        _sortIsUserChosen = false;
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

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ResetFiltersAndSortToDefault();
        Reload();
    }

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