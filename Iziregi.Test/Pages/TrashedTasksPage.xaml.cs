// File: Pages/TrashedTasksPage.xaml.cs
using System;
using System.Collections.Generic;
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

// ✅ Fix (demande de Joe : "il manque la ligne Corbeille" dans le widget Tâches du Tableau de
// bord) : copie fonctionnelle d'ArchivesTasksPage (lecture seule, restaurer ou supprimer
// définitivement), IsTrashed/TrashedAt au lieu d'IsArchived/ArchivedAt. Même remarque que
// là-bas : les tâches vivent dans un fichier JSON par projet (Data/ProjectTasksStore.cs) sans
// clé numérique -- restaurer/supprimer réécrit donc la LISTE COMPLÈTE du fichier.
public partial class TrashedTasksPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    public class TaskTrashRow
    {
        public TaskRecord Task { get; set; } = new();
        public bool IsSelected { get; set; }

        public MediaBrush CompanyBrush { get; set; } = MediaBrushes.Transparent;
        public MediaBrush CompanyTextBrush { get; set; } = MediaBrushes.Black;
    }

    private List<TaskRecord> _allTasks = new();
    private List<TaskTrashRow> _rows = new();

    public TrashedTasksPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        RefreshBatchSelectionUi();
    }

    // ✅ Retour (04.08.2026, demande de Joe) : ramène à "Planification", page d'origine depuis
    // laquelle cette Corbeille (tâches) est maintenant accessible (voir PlanningPage.xaml).
    private void BackToParent_Click(object sender, RoutedEventArgs e) => _host.ShowPlanning();

    // ✅ Accès direct à l'autre sous-page (demande de Joe, 04.08.2026) : depuis Corbeille
    // tâches, va directement à Archives tâches sans repasser par "Planification".
    private void GoToArchives_Click(object sender, RoutedEventArgs e) => _host.ShowArchivesTasks();

    public void Reload()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _allTasks = new List<TaskRecord>();
            _rows = new List<TaskTrashRow>();

            TrashedTasksGrid.ItemsSource = _rows;
            TrashedTasksGrid.Items.Refresh();

            RefreshBatchSelectionUi();

            System.Windows.MessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant d’afficher la corbeille.",
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

        _rows = _allTasks
            .Where(t => t.IsTrashed)
            .Select(t =>
            {
                var company = (t.Company ?? "").Trim();
                colorMap.TryGetValue(company, out var hex);

                var fg = GetTextBrushForBackground(BrushFromHexOrDefault(hex, MediaBrushes.Transparent));
                var bg = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, gradientMap.Contains(company)) ?? MediaBrushes.Transparent;

                return new TaskTrashRow
                {
                    Task = t,
                    IsSelected = false,
                    CompanyBrush = bg,
                    CompanyTextBrush = fg
                };
            })
            .ToList();

        TrashedTasksGrid.ItemsSource = _rows;
        TrashedTasksGrid.Items.Refresh();

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

    private static TaskTrashRow? GetRow(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskTrashRow row)
            return row;

        return null;
    }

    private static TaskTrashRow? GetSelectedRow(DataGrid? grid)
        => grid?.SelectedItem as TaskTrashRow;

    private List<TaskTrashRow> GetActiveRows()
    {
        if (TrashedTasksGrid?.ItemsSource == null)
            return new List<TaskTrashRow>();

        return TrashedTasksGrid.ItemsSource.Cast<TaskTrashRow>().ToList();
    }

    // ✅ Fix (04.08.2026, demande de Joe) : actif si au moins une case est cochée OU si une
    // ligne est sélectionnée (bordure bleue), même principe que DashboardPage.
    private void RefreshBatchSelectionUi()
    {
        var rows = GetActiveRows();
        var anySelected = rows.Any(x => x.IsSelected) || TrashedTasksGrid?.SelectedItem != null;

        if (RestoreSelectionButton != null)
            RestoreSelectionButton.IsEnabled = anySelected;

        if (DeleteSelectionButton != null)
            DeleteSelectionButton.IsEnabled = anySelected;
    }

    private void TrashedTasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBatchSelectionUi();

    private List<TaskRecord> GetActionSelectionOrFallbackToRow(TaskTrashRow? fallbackRow)
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

        TrashedTasksGrid?.Items.Refresh();
        RefreshBatchSelectionUi();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    // ✅ Réécrit le fichier complet (actives + archivées + en corbeille) : les TaskRecord passés
    // en paramètre sont les mêmes références que celles dans _allTasks (LINQ Where/Select ne
    // clone pas), donc les mutations faites dessus (IsTrashed/TrashedAt) sont déjà reflétées
    // dans _allTasks au moment de l'appel.
    private void PersistAllTasks()
    {
        var projectId = Db.GetCurrentProjectId();
        ProjectTasksStore.Save(projectId, _allTasks);
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(TrashedTasksGrid));

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
            t.IsTrashed = false;
            t.TrashedAt = null;
        }

        PersistAllTasks();
        Reload();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = GetActionSelectionOrFallbackToRow(GetSelectedRow(TrashedTasksGrid));

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
            ? $"Supprimer définitivement la tâche N°{sel[0].Ref} ?\n\nCette action est irréversible."
            : $"Supprimer définitivement {sel.Count} tâches ?\n\nCette action est irréversible.";

        var ok = System.Windows.MessageBox.Show(msg, "Suppression définitive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes)
            return;

        foreach (var t in sel)
            _allTasks.Remove(t);

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

        row.Task.IsTrashed = false;
        row.Task.TrashedAt = null;

        PersistAllTasks();
        Reload();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRow(sender);
        if (row?.Task == null)
            return;

        var ok = System.Windows.MessageBox.Show(
            $"Supprimer définitivement la tâche N°{row.Task.Ref} ?\n\nCette action est irréversible.",
            "Suppression définitive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes)
            return;

        _allTasks.Remove(row.Task);

        PersistAllTasks();
        Reload();
    }
}
