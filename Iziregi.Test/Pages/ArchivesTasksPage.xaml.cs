// File: Pages/ArchivesTasksPage.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

// ✅ Types WPF (évite ambiguïtés avec System.Drawing)
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

using Iziregi.Test.Data;

namespace Iziregi.Test.Pages;

// ✅ 30.07.2026 (demande de Joe) : "Archives tâches" -- fonctionne comme pour les BI
// (lecture seule, restaurer ou supprimer définitivement), même entête que la grille des
// tâches (page Planning). Contrairement aux bons (SQLite, Id numérique + UPDATE ciblé), les
// tâches vivent dans un fichier JSON par projet (voir Data/ProjectTasksStore.cs) sans clé
// numérique -- restaurer/supprimer réécrit donc la LISTE COMPLÈTE (actives + archivées) du
// fichier, en mutant les TaskRecord partagés par référence entre _allTasks et _rows.
public partial class ArchivesTasksPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    public class TaskArchiveRow
    {
        public TaskRecord Task { get; set; } = new();
        public bool IsSelected { get; set; }

        public MediaBrush CompanyBrush { get; set; } = MediaBrushes.Transparent;
        public MediaBrush CompanyTextBrush { get; set; } = MediaBrushes.Black;
    }

    private List<TaskRecord> _allTasks = new();

    // ✅ Filtres façon Excel dans les en-têtes de colonne (demande de Joe : "comme dans
    // Archives des bons d'intervention"), même principe qu'ArchivesPage.xaml.cs mais sans
    // colonnes date (les tâches archivées n'en ont pas dans cette grille).
    private List<TaskArchiveRow> _allRows = new();
    private List<TaskArchiveRow> _rows = new();

    public ArchivesTasksPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        RefreshBatchSelectionUi();
    }

    // ✅ Retour (04.08.2026, demande de Joe) : ramène à "Planification", page d'origine depuis
    // laquelle ces Archives (tâches) sont maintenant accessibles (voir PlanningPage.xaml).
    private void BackToParent_Click(object sender, RoutedEventArgs e) => _host.ShowPlanning();

    // ✅ Accès direct à l'autre sous-page (demande de Joe, 04.08.2026) : depuis Archives tâches,
    // va directement à Corbeille tâches sans repasser par "Planification".
    private void GoToTrash_Click(object sender, RoutedEventArgs e) => _host.ShowTrashedTasks();

    public void Reload()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _allTasks = new List<TaskRecord>();
            _allRows = new List<TaskArchiveRow>();
            _rows = new List<TaskArchiveRow>();

            ArchivedTasksGrid.ItemsSource = _rows;
            ArchivedTasksGrid.Items.Refresh();

            RefreshBatchSelectionUi();

            System.Windows.MessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant d’afficher les archives.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectId = projectIdNullable.Value;

        BuildingHeaderText.Text = Db.GetLabelPlace(projectId);
        FloorHeaderText.Text = Db.GetLabelEtage(projectId);
        CategoryHeaderText.Text = Db.GetLabelTaskCategory(projectId);
        CompanyHeaderText.Text = Db.GetLabelPerformedBy(projectId);
        UrgencyHeaderText.Text = Db.GetLabelTaskUrgency(projectId);

        _allTasks = ProjectTasksStore.Load(projectId);

        var colorMap = Db.GetCompanyColorMap(projectId);
        var gradientMap = Db.GetCompanyGradientMap(projectId);

        // ✅ Fix (04.08.2026, demande de Joe) : exclut désormais les tâches à la fois archivées
        // ET à la corbeille (nouveau cas depuis que "Supprimer définitivement" devient
        // "Déplacer dans la corbeille", voir DeleteSelected_Click), même principe que
        // Db.GetArchivedWorkOrders (WHERE IsTrashed=0 AND IsArchived=1) côté Bons.
        _allRows = _allTasks
            .Where(t => t.IsArchived && !t.IsTrashed)
            .Select(t =>
            {
                var company = (t.Company ?? "").Trim();
                colorMap.TryGetValue(company, out var hex);

                var fg = GetTextBrushForBackground(BrushFromHexOrDefault(hex, MediaBrushes.Transparent));
                var bg = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, gradientMap.Contains(company)) ?? MediaBrushes.Transparent;

                return new TaskArchiveRow
                {
                    Task = t,
                    IsSelected = false,
                    CompanyBrush = bg,
                    CompanyTextBrush = fg
                };
            })
            .ToList();

        ApplyFilters();
        UpdateFilterButtonIndicators();
    }

    // ✅ Filtres façon Excel (demande de Joe : "comme dans Archives des bons d'intervention"),
    // même principe qu'ArchivesPage.xaml.cs (ColumnFilterButton_Click/ColumnFilterApply_Click/
    // ApplyFilters), simplifié : uniquement des colonnes texte/statut ici, pas de colonne date.
    private string? _sortColumnKey;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private readonly Dictionary<string, HashSet<string>> _activeValueFilters = new();
    private string? _currentFilterColumnKey;
    private List<FilterOption> _filterPopupOptions = new();

    public class FilterOption : INotifyPropertyChanged
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public string DisplayText => $"{Value}  ({Count})";

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ✅ "Done" est un statut à 2 valeurs fixes, toujours proposées (comme AllStatusLabels côté
    // Bons), pas de tri alphabétique proposé pour cette colonne (pas de sens ici non plus).
    private static readonly string[] AllDoneLabels = { "Effectué", "Non effectué" };

    private static bool IsStatusColumn(string columnKey) => columnKey == "Done";

    private static string GetColumnValue(TaskArchiveRow r, string columnKey) => columnKey switch
    {
        "Building" => string.IsNullOrWhiteSpace(r.Task.Building) ? "(Non défini)" : r.Task.Building.Trim(),
        "Floor" => string.IsNullOrWhiteSpace(r.Task.Floor) ? "(Non défini)" : r.Task.Floor.Trim(),
        "Category" => string.IsNullOrWhiteSpace(r.Task.Category) ? "(Non défini)" : r.Task.Category.Trim(),
        "Company" => string.IsNullOrWhiteSpace(r.Task.Company) ? "(Non défini)" : r.Task.Company.Trim(),
        "Urgency" => string.IsNullOrWhiteSpace(r.Task.Urgent) ? "(Non défini)" : r.Task.Urgent.Trim(),
        "Done" => r.Task.Done ? "Effectué" : "Non effectué",
        _ => ""
    };

    private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string columnKey) return;

        _currentFilterColumnKey = columnKey;
        var isStatus = IsStatusColumn(columnKey);

        AlphaSortCheckBox.Visibility = isStatus ? Visibility.Collapsed : Visibility.Visible;
        AlphaSortCheckBox.IsChecked = _sortColumnKey == columnKey && _sortDirection == ListSortDirection.Ascending;

        var allValues = isStatus
            ? AllDoneLabels.ToList()
            : _allRows
                .Select(r => GetColumnValue(r, columnKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var counts = _allRows
            .GroupBy(r => GetColumnValue(r, columnKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var active = _activeValueFilters.TryGetValue(columnKey, out var set) ? set : null;

        _filterPopupOptions = allValues
            .Select(v => new FilterOption { Value = v, Count = counts.TryGetValue(v, out var c) ? c : 0, IsChecked = active == null || active.Contains(v) })
            .ToList();

        FilterOptionsItemsControl.ItemsSource = _filterPopupOptions;
        FilterSelectAllCheckBox.IsChecked = _filterPopupOptions.All(o => o.IsChecked);

        ColumnFilterPopup.PlacementTarget = fe;
        ColumnFilterPopup.IsOpen = true;
    }

    private void FilterSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var check = FilterSelectAllCheckBox.IsChecked == true;
        foreach (var o in _filterPopupOptions)
            o.IsChecked = check;
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

        var checkedValues = _filterPopupOptions.Where(o => o.IsChecked).Select(o => o.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (checkedValues.Count == _filterPopupOptions.Count)
            _activeValueFilters.Remove(columnKey);
        else
            _activeValueFilters[columnKey] = checkedValues;

        if (!IsStatusColumn(columnKey) && AlphaSortCheckBox.IsChecked == true)
        {
            _sortColumnKey = columnKey;
            _sortDirection = ListSortDirection.Ascending;
        }
        else if (_sortColumnKey == columnKey)
        {
            _sortColumnKey = null;
        }

        ColumnFilterPopup.IsOpen = false;

        UpdateFilterButtonIndicators();
        ApplyFilters();
    }

    private void UpdateFilterButtonIndicators()
    {
        SetFilterHeaderState(BuildingFilterButton, BuildingHeaderBorder, "Building");
        SetFilterHeaderState(FloorFilterButton, FloorHeaderBorder, "Floor");
        SetFilterHeaderState(CategoryFilterButton, CategoryHeaderBorder, "Category");
        SetFilterHeaderState(CompanyFilterButton, CompanyHeaderBorder, "Company");
        SetFilterHeaderState(UrgencyFilterButton, UrgencyHeaderBorder, "Urgency");
        SetFilterHeaderState(DoneFilterButton, DoneHeaderBorder, "Done");
    }

    private bool IsColumnFilterActive(string columnKey) =>
        _activeValueFilters.ContainsKey(columnKey) || _sortColumnKey == columnKey;

    private void SetFilterHeaderState(System.Windows.Controls.Button? button, System.Windows.Controls.Border? headerBorder, string columnKey)
    {
        var active = IsColumnFilterActive(columnKey);

        if (headerBorder != null)
            headerBorder.BorderBrush = active
                ? new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#2563EB")!)
                : MediaBrushes.Transparent;

        if (button != null)
            button.Style = (Style)(active
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
                if (!kvp.Value.Contains(value)) return false;
            }
            return true;
        });

        _rows = _sortColumnKey != null
            ? (_sortDirection == ListSortDirection.Ascending
                ? filtered.OrderBy(r => GetColumnValue(r, _sortColumnKey), StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(r => GetColumnValue(r, _sortColumnKey), StringComparer.OrdinalIgnoreCase)).ToList()
            : filtered.ToList();

        ArchivedTasksGrid.ItemsSource = _rows;

        RefreshBatchSelectionUi();
    }

    private void ResetFiltersAndSortToDefault()
    {
        _activeValueFilters.Clear();
        _sortColumnKey = null;
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

    private static TaskArchiveRow? GetRow(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskArchiveRow row)
            return row;

        return null;
    }

    private static TaskArchiveRow? GetSelectedRow(DataGrid? grid)
        => grid?.SelectedItem as TaskArchiveRow;

    private List<TaskArchiveRow> GetActiveRows()
    {
        if (ArchivedTasksGrid?.ItemsSource == null)
            return new List<TaskArchiveRow>();

        return ArchivedTasksGrid.ItemsSource.Cast<TaskArchiveRow>().ToList();
    }

    // ✅ Fix (04.08.2026, demande de Joe) : actif si au moins une case est cochée OU si une
    // ligne est sélectionnée (bordure bleue), même principe que DashboardPage.
    private void RefreshBatchSelectionUi()
    {
        var rows = GetActiveRows();
        var anySelected = rows.Any(x => x.IsSelected) || ArchivedTasksGrid?.SelectedItem != null;

        if (RestoreSelectionButton != null)
            RestoreSelectionButton.IsEnabled = anySelected;

        if (DeleteSelectionButton != null)
            DeleteSelectionButton.IsEnabled = anySelected;
    }

    private void ArchivedTasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBatchSelectionUi();

    private List<TaskRecord> GetActionSelectionOrFallbackToRow(TaskArchiveRow? fallbackRow)
    {
        var sel = GetActiveRows().Where(r => r.IsSelected).Select(r => r.Task).ToList();

        if (sel.Count == 0 && fallbackRow?.Task != null)
            sel = new List<TaskRecord> { fallbackRow.Task };

        return sel;
    }

    private void RowSelectCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row == null)
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

        ArchivedTasksGrid?.Items.Refresh();
        RefreshBatchSelectionUi();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ResetFiltersAndSortToDefault();
        Reload();
    }

    // ✅ Réécrit le fichier complet (actives + archivées) : les TaskRecord passés en
    // paramètre sont les mêmes références que celles dans _allTasks (LINQ Where/Select ne
    // clone pas), donc les mutations faites dessus (IsArchived/ArchivedAt) sont déjà
    // reflétées dans _allTasks au moment de l'appel.
    private void PersistAllTasks()
    {
        var projectId = Db.GetCurrentProjectId();
        ProjectTasksStore.Save(projectId, _allTasks);
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(ArchivedTasksGrid));

        if (sel.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Coche une ou plusieurs tâches (colonne de gauche).",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Restaurer la tâche N°{sel[0].Ref} (retour à la grille des tâches) ?"
            : $"Restaurer {sel.Count} tâches (retour à la grille des tâches) ?";

        var ok = System.Windows.MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var t in sel)
        {
            t.IsArchived = false;
            t.ArchivedAt = null;
        }

        PersistAllTasks();
        Reload();
    }

    // ✅ Renommé (demande de Joe, 04.08.2026) : "Supprimer définitivement" devient "Déplacer
    // dans la corbeille" -- même principe que ArchivesPage.TrashSelected_Click côté Bons
    // (IsTrashed=true, IsArchived reste inchangé), au lieu d'une suppression irréversible.
    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(ArchivedTasksGrid));

        if (sel.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Coche une ou plusieurs tâches (colonne de gauche).",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var msg = sel.Count == 1
            ? $"Mettre la tâche N°{sel[0].Ref} à la corbeille ?"
            : $"Mettre {sel.Count} tâches à la corbeille ?";

        var ok = System.Windows.MessageBox.Show(msg, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var t in sel)
        {
            t.IsTrashed = true;
            t.TrashedAt = DateTime.Now;
        }

        PersistAllTasks();
        Reload();
    }

    private void RestoreRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.Task == null)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Restaurer la tâche N°{row.Task.Ref} (retour à la grille des tâches) ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        row.Task.IsArchived = false;
        row.Task.ArchivedAt = null;

        PersistAllTasks();
        Reload();
    }

    // ✅ Fix (demande de Joe) : le bouton rouge de la colonne Actions supprimait réellement la
    // tâche (_allTasks.Remove, irréversible) au lieu de la mettre à la corbeille comme
    // DeleteSelected_Click (sélection groupée) le fait déjà -- même comportement que TrashRow_Click
    // côté ArchivesPage.xaml.cs (Bons).
    private void TrashRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.Task == null)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Mettre la tâche N°{row.Task.Ref} à la corbeille ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        row.Task.IsTrashed = true;
        row.Task.TrashedAt = DateTime.Now;

        PersistAllTasks();
        Reload();
    }
}
