// File: Pages/ListsPage.xaml.cs
using System;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Microsoft.VisualBasic;
using System.Collections.Generic;

using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;

namespace Iziregi.Test.Pages;

public partial class ListsPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // =========================
    // ✅ Noms de champs/listes : modifications non enregistrées (déclenche une confirmation
    // à la sortie de la page — voir ConfirmLeaveListsPageIfDirty dans MainWindow).
    // =========================
    private bool _isLoadingLabels = false;
    private bool _labelsDirty = false;

    public bool HasUnsavedLabelChanges => _labelsDirty;

    private void AnyLabelTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoadingLabels) return;
        _labelsDirty = true;
    }

    // ✅ Cliquer dans un champ de libellé (au-dessus d'une liste) désactive la liste
    // active — sinon la bordure bleue de la liste restait allumée EN MÊME TEMPS que la
    // bordure de focus du libellé, donnant l'impression de deux éléments sélectionnés.
    private void AnyLabelTextBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.None);

    public void SaveLabelsNow() => SaveLabelsCore();

    // =========================
    // Liste active
    // =========================
    private enum ActiveListKind
    {
        None = 0,
        Reserves = 1,
        Requesters = 2,
        Companies = 3,
        Places = 4,
        Etages = 5,
        PlanningTextZones = 6,
        TaskCategories = 7,
        TaskUrgencies = 8,
    }

    private ActiveListKind _activeList = ActiveListKind.None;

    private void SetActiveList(ActiveListKind kind)
    {
        _activeList = kind;
        RefreshCommonButtonsState();
        RefreshActiveListBorders();
    }

    private void RefreshActiveListBorders()
    {
        var normal = (System.Windows.Media.Brush)(new System.Windows.Media.BrushConverter().ConvertFrom("#E5E7EB")!);
        var active = (System.Windows.Media.Brush)(new System.Windows.Media.BrushConverter().ConvertFrom("#2563EB")!);

        void SetBorder(System.Windows.Controls.Border? b, bool isActive)
        {
            if (b == null) return;
            b.BorderBrush = isActive ? active : normal;
        }

        SetBorder(ReservesListBorder, _activeList == ActiveListKind.Reserves);
        SetBorder(RequestersListBorder, _activeList == ActiveListKind.Requesters);
        SetBorder(CompaniesListBorder, _activeList == ActiveListKind.Companies);
        SetBorder(PlacesListBorder, _activeList == ActiveListKind.Places);
        SetBorder(EtagesListBorder, _activeList == ActiveListKind.Etages);
        SetBorder(PlanningTextZonesListBorder, _activeList == ActiveListKind.PlanningTextZones);
        SetBorder(TaskCategoriesListBorder, _activeList == ActiveListKind.TaskCategories);
        SetBorder(TaskUrgenciesListBorder, _activeList == ActiveListKind.TaskUrgencies);
    }

    private static long RequireCurrentProjectId()
    {
        var pid = Db.GetCurrentProjectId();
        if (!pid.HasValue || pid.Value <= 0)
            throw new Exception("Aucun dossier courant. Sélectionne un dossier avant d’utiliser les listes.");
        return pid.Value;
    }

    private long RequireCurrentProjectIdSafe()
    {
        try { return RequireCurrentProjectId(); }
        catch { return 0; }
    }

    private bool HasCurrentProject()
    {
        var pid = Db.GetCurrentProjectId();
        return pid.HasValue && pid.Value > 0;
    }

    private string? GetSelectedCompanyName()
    {
        if (CompaniesListBox.SelectedItem is CompanyListItem cli)
            return cli.Name;

        return CompaniesListBox.SelectedItem as string;
    }

    // ✅ Couleurs Catégories (17.07.2026) : TaskCategoriesListBox est maintenant lié à des
    // TaskCategoryListItem (comme CompaniesListBox), pas de simples chaînes -- même
    // mécanisme de secours que GetSelectedCompanyName ci-dessus.
    private string? GetSelectedTaskCategoryName()
    {
        if (TaskCategoriesListBox.SelectedItem is TaskCategoryListItem tci)
            return tci.Name;

        return TaskCategoriesListBox.SelectedItem as string;
    }

    private string? GetActiveSelectedItemText()
    {
        return _activeList switch
        {
            ActiveListKind.Reserves => ReservesListBox?.SelectedItem as string,
            ActiveListKind.Requesters => RequestersListBox?.SelectedItem as string,
            ActiveListKind.Companies => GetSelectedCompanyName(),
            ActiveListKind.Places => PlacesListBox?.SelectedItem as string,
            ActiveListKind.Etages => EtagesListBox?.SelectedItem as string,
            ActiveListKind.PlanningTextZones => PlanningTextZonesListBox?.SelectedItem as string,
            ActiveListKind.TaskCategories => GetSelectedTaskCategoryName(),
            ActiveListKind.TaskUrgencies => TaskUrgenciesListBox?.SelectedItem as string,
            _ => null
        };
    }

    private string GetActiveDefaultText()
    {
        return _activeList switch
        {
            ActiveListKind.Reserves => (DefaultReserveComboBox?.Text ?? ""),
            ActiveListKind.Requesters => (DefaultRequesterComboBox?.Text ?? ""),
            ActiveListKind.Companies => (DefaultCompanyComboBox?.Text ?? ""),
            ActiveListKind.Places => (DefaultPlaceComboBox?.Text ?? ""),
            ActiveListKind.Etages => (DefaultEtageComboBox?.Text ?? ""),
            ActiveListKind.PlanningTextZones => (DefaultPlanningTextZoneComboBox?.Text ?? ""),
            ActiveListKind.TaskCategories => (DefaultTaskCategoryComboBox?.Text ?? ""),
            ActiveListKind.TaskUrgencies => (DefaultTaskUrgencyComboBox?.Text ?? ""),
            _ => ""
        };
    }

    private void RefreshCommonButtonsState()
    {
        if (CommonAddButton == null || CommonRenameButton == null || CommonDeleteButton == null || CommonSetDefaultButton == null
            || CommonCopyButton == null || CommonPasteButton == null)
            return;

        var hasProject = HasCurrentProject();
        var hasActiveList = _activeList != ActiveListKind.None;
        var selected = (GetActiveSelectedItemText() ?? "").Trim();

        CommonAddButton.IsEnabled = hasProject && hasActiveList;
        CommonRenameButton.IsEnabled = hasProject && hasActiveList && !string.IsNullOrWhiteSpace(selected);
        CommonDeleteButton.IsEnabled = hasProject && hasActiveList && !string.IsNullOrWhiteSpace(selected);

        // Toujours activé si une liste est active :
        // - si item sélectionné => utilise le nom
        // - sinon => utilise le texte du ComboBox (peut être vide => efface)
        CommonSetDefaultButton.IsEnabled = hasProject && hasActiveList;

        CommonCopyButton.IsEnabled = hasProject && hasActiveList;
        CommonPasteButton.IsEnabled = hasProject && hasActiveList && _listClipboard != null && _listClipboard.Items.Count > 0;

        if (ActiveListLabel != null)
        {
            var pid = RequireCurrentProjectIdSafe();
            ActiveListLabel.Text = _activeList switch
            {
                ActiveListKind.Reserves => pid > 0 ? Db.GetLabelReserve(pid) : "Concerne",
                ActiveListKind.Requesters => pid > 0 ? Db.GetLabelRequestedBy(pid) : "Demandé par",
                ActiveListKind.Companies => pid > 0 ? Db.GetLabelPerformedBy(pid) : "Entreprise",
                ActiveListKind.Places => pid > 0 ? Db.GetLabelPlace(pid) : "Bâtiment",
                ActiveListKind.Etages => pid > 0 ? Db.GetLabelEtage(pid) : "Étage",
                ActiveListKind.PlanningTextZones => pid > 0 ? Db.GetLabelPlanningTextZone(pid) : "Zone de texte planning",
                ActiveListKind.TaskCategories => pid > 0 ? Db.GetLabelTaskCategory(pid) : "Cat.",
                ActiveListKind.TaskUrgencies => pid > 0 ? Db.GetLabelTaskUrgency(pid) : "Urg.",
                _ => "—",
            };
        }
    }

    // =========================
    // Clipboard générique (toutes listes)
    // =========================
    private sealed class ListClipboardPayload
    {
        public List<string> Items { get; } = new();
        public Dictionary<string, string> CompanyColorMap { get; } = new(StringComparer.OrdinalIgnoreCase);
        // ✅ Couleurs Catégories (17.07.2026) : même principe que CompanyColorMap ci-dessus,
        // pour que copier/coller une liste "Cat." (dans le même projet) conserve ses couleurs.
        public Dictionary<string, string> TaskCategoryColorMap { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ActiveListKind Kind { get; set; }
        public long SourceProjectId { get; set; }
    }

    private static ListClipboardPayload? _listClipboard;

    // =========================
    // Item Entreprise (pastille + nom)
    // =========================
    private sealed class CompanyListItem
    {
        public string Name { get; }
        public System.Windows.Media.Brush ColorBrush { get; }

        public CompanyListItem(string name, System.Windows.Media.Brush colorBrush)
        {
            Name = name;
            ColorBrush = colorBrush;
        }

        public override string ToString() => Name;
    }

    // =========================
    // Item Catégorie (pastille + nom) — indépendant des entreprises, 17.07.2026.
    // =========================
    private sealed class TaskCategoryListItem
    {
        public string Name { get; }
        public System.Windows.Media.Brush ColorBrush { get; }

        public TaskCategoryListItem(string name, System.Windows.Media.Brush colorBrush)
        {
            Name = name;
            ColorBrush = colorBrush;
        }

        public override string ToString() => Name;
    }

    public ListsPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    public void Reload()
    {
        _isLoadingLabels = true;
        try { ReloadCore(); }
        finally { _isLoadingLabels = false; }

        _labelsDirty = false;
    }

    private void ReloadCore()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            ReservesListBox.ItemsSource = null;
            RequestersListBox.ItemsSource = null;
            CompaniesListBox.ItemsSource = null;
            PlacesListBox.ItemsSource = null;
            EtagesListBox.ItemsSource = null;
            PlanningTextZonesListBox.ItemsSource = null;
            TaskCategoriesListBox.ItemsSource = null;
            TaskUrgenciesListBox.ItemsSource = null;

            DefaultReserveComboBox.ItemsSource = null;
            DefaultRequesterComboBox.ItemsSource = null;
            DefaultCompanyComboBox.ItemsSource = null;
            DefaultPlaceComboBox.ItemsSource = null;
            DefaultEtageComboBox.ItemsSource = null;
            DefaultPlanningTextZoneComboBox.ItemsSource = null;
            DefaultTaskCategoryComboBox.ItemsSource = null;
            DefaultTaskUrgencyComboBox.ItemsSource = null;

            DefaultReserveComboBox.SelectedItem = null; DefaultReserveComboBox.Text = "";
            DefaultRequesterComboBox.SelectedItem = null; DefaultRequesterComboBox.Text = "";
            DefaultCompanyComboBox.SelectedItem = null; DefaultCompanyComboBox.Text = "";
            DefaultPlaceComboBox.SelectedItem = null; DefaultPlaceComboBox.Text = "";
            DefaultEtageComboBox.SelectedItem = null; DefaultEtageComboBox.Text = "";
            DefaultPlanningTextZoneComboBox.SelectedItem = null; DefaultPlanningTextZoneComboBox.Text = "";
            DefaultTaskCategoryComboBox.SelectedItem = null; DefaultTaskCategoryComboBox.Text = "";
            DefaultTaskUrgencyComboBox.SelectedItem = null; DefaultTaskUrgencyComboBox.Text = "";

            LabelReserveTextBox.Text = "";
            LabelRequestedByTextBox.Text = "";
            LabelPerformedByTextBox.Text = "";
            LabelPlaceTextBox.Text = "";
            LabelEtageTextBox.Text = "";
            LabelDeadlineTextBox.Text = "";
            LabelPlanningTextZoneTextBox.Text = "";
            LabelTaskCategoryTextBox.Text = "";
            LabelTaskUrgencyTextBox.Text = "";

            SetSelectedCompanyColorPreview(null, false);

            SetActiveList(ActiveListKind.None);

            MessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant de gérer les listes.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var projectId = projectIdNullable.Value;

        var reserves = Db.GetReserves(projectId);
        var requesters = Db.GetRequesters(projectId);
        var companies = Db.GetCompanies(projectId);
        var places = Db.GetPlaces(projectId);
        var etages = Db.GetEtages(projectId);
        var planningTextZones = Db.GetPlanningTextZones(projectId);
        var taskCategories = Db.GetTaskCategories(projectId);
        var taskUrgencies = Db.GetTaskUrgencies(projectId);

        ReservesListBox.ItemsSource = reserves;
        RequestersListBox.ItemsSource = requesters;

        CompaniesListBox.ItemsSource = companies
            .Select(name => new CompanyListItem(name, BuildCompanyColorBrush(projectId, name)))
            .ToList();

        PlacesListBox.ItemsSource = places;
        EtagesListBox.ItemsSource = etages;
        PlanningTextZonesListBox.ItemsSource = planningTextZones;

        TaskCategoriesListBox.ItemsSource = taskCategories
            .Select(name => new TaskCategoryListItem(name, BuildTaskCategoryColorBrush(projectId, name)))
            .ToList();

        TaskUrgenciesListBox.ItemsSource = taskUrgencies;

        var reserveDefaults = Db.WithEmptyOption(reserves);
        var requesterDefaults = Db.WithEmptyOption(requesters);
        var companyDefaults = Db.WithEmptyOption(companies);
        var placeDefaults = Db.WithEmptyOption(places);
        var etageDefaults = Db.WithEmptyOption(etages);
        var planningDefaults = Db.WithEmptyOption(planningTextZones);
        var taskCategoryDefaults = Db.WithEmptyOption(taskCategories);
        var taskUrgencyDefaults = Db.WithEmptyOption(taskUrgencies);

        DefaultReserveComboBox.ItemsSource = reserveDefaults;
        DefaultRequesterComboBox.ItemsSource = requesterDefaults;
        DefaultCompanyComboBox.ItemsSource = companyDefaults;
        DefaultPlaceComboBox.ItemsSource = placeDefaults;
        DefaultEtageComboBox.ItemsSource = etageDefaults;
        DefaultPlanningTextZoneComboBox.ItemsSource = planningDefaults;
        DefaultTaskCategoryComboBox.ItemsSource = taskCategoryDefaults;
        DefaultTaskUrgencyComboBox.ItemsSource = taskUrgencyDefaults;

        var defReserve = Db.GetDefaultReserve(projectId) ?? "";
        var defRequester = Db.GetDefaultRequester(projectId) ?? "";
        var defCompany = Db.GetDefaultCompany(projectId) ?? "";
        var defPlace = Db.GetDefaultPlace(projectId) ?? "";
        var defEtage = Db.GetDefaultEtage(projectId) ?? "";
        var defPlanning = Db.GetDefaultPlanningTextZone(projectId) ?? "";
        var defTaskCategory = Db.GetDefaultTaskCategory(projectId) ?? "";
        var defTaskUrgency = Db.GetDefaultTaskUrgency(projectId) ?? "";

        DefaultReserveComboBox.SelectedItem = reserveDefaults.Contains(defReserve) ? defReserve : "";
        DefaultReserveComboBox.Text = defReserve;

        DefaultRequesterComboBox.SelectedItem = requesterDefaults.Contains(defRequester) ? defRequester : "";
        DefaultRequesterComboBox.Text = defRequester;

        DefaultCompanyComboBox.SelectedItem = companyDefaults.Contains(defCompany) ? defCompany : "";
        DefaultCompanyComboBox.Text = defCompany;

        DefaultPlaceComboBox.SelectedItem = placeDefaults.Contains(defPlace) ? defPlace : "";
        DefaultPlaceComboBox.Text = defPlace;

        DefaultEtageComboBox.SelectedItem = etageDefaults.Contains(defEtage) ? defEtage : "";
        DefaultEtageComboBox.Text = defEtage;

        DefaultPlanningTextZoneComboBox.SelectedItem = planningDefaults.Contains(defPlanning) ? defPlanning : "";
        DefaultPlanningTextZoneComboBox.Text = defPlanning;

        DefaultTaskCategoryComboBox.SelectedItem = taskCategoryDefaults.Contains(defTaskCategory) ? defTaskCategory : "";
        DefaultTaskCategoryComboBox.Text = defTaskCategory;

        DefaultTaskUrgencyComboBox.SelectedItem = taskUrgencyDefaults.Contains(defTaskUrgency) ? defTaskUrgency : "";
        DefaultTaskUrgencyComboBox.Text = defTaskUrgency;

        LabelReserveTextBox.Text = Db.GetLabelReserve(projectId);
        LabelRequestedByTextBox.Text = Db.GetLabelRequestedBy(projectId);
        LabelPerformedByTextBox.Text = Db.GetLabelPerformedBy(projectId);
        LabelPlaceTextBox.Text = Db.GetLabelPlace(projectId);
        LabelEtageTextBox.Text = Db.GetLabelEtage(projectId);
        LabelDeadlineTextBox.Text = Db.GetLabelDeadline(projectId);
        LabelPlanningTextZoneTextBox.Text = Db.GetLabelPlanningTextZone(projectId);
        LabelTaskCategoryTextBox.Text = Db.GetLabelTaskCategory(projectId);
        LabelTaskUrgencyTextBox.Text = Db.GetLabelTaskUrgency(projectId);

        RefreshSelectedCompanyColorPreview();
        RefreshSelectedTaskCategoryColorPreview();

        RefreshCommonButtonsState();
        RefreshActiveListBorders();
    }

    // =========================
    // Focus list
    // =========================
    private void ReservesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.Reserves);
    private void RequestersListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.Requesters);
    private void CompaniesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.Companies);
    private void PlacesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.Places);
    private void EtagesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.Etages);
    private void PlanningTextZonesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.PlanningTextZones);
    private void TaskCategoriesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.TaskCategories);
    private void TaskUrgenciesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.TaskUrgencies);

    private void AnyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCommonButtonsState();
    }

    // ✅ Couleurs Catégories (17.07.2026) : handler dédié (comme Companies) au lieu du
    // générique AnyListBox_SelectionChanged, pour rafraîchir l'aperçu de couleur sélectionné.
    private void TaskCategoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedTaskCategoryColorPreview();
        RefreshCommonButtonsState();
    }

    // =========================
    // Clic dans le blanc (Border handlers)
    // =========================
    private void ReservesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Reserves);
        ReservesListBox.Focus();
    }

    private void RequestersListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Requesters);
        RequestersListBox.Focus();
    }

    private void CompaniesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Companies);
        CompaniesListBox.Focus();
    }

    private void PlacesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Places);
        PlacesListBox.Focus();
    }

    private void EtagesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Etages);
        EtagesListBox.Focus();
    }

    private void PlanningTextZonesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.PlanningTextZones);
        PlanningTextZonesListBox.Focus();
    }

    private void TaskCategoriesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.TaskCategories);
        TaskCategoriesListBox.Focus();
    }

    private void TaskUrgenciesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.TaskUrgencies);
        TaskUrgenciesListBox.Focus();
    }

    // =========================
    // Barre commune
    // =========================
    private void CommonAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var title = _activeList switch
            {
                ActiveListKind.Reserves => $"Ajouter ({Db.GetLabelReserve(projectId)})",
                ActiveListKind.Requesters => $"Ajouter ({Db.GetLabelRequestedBy(projectId)})",
                ActiveListKind.Companies => $"Ajouter ({Db.GetLabelPerformedBy(projectId)})",
                ActiveListKind.Places => $"Ajouter ({Db.GetLabelPlace(projectId)})",
                ActiveListKind.Etages => $"Ajouter ({Db.GetLabelEtage(projectId)})",
                ActiveListKind.PlanningTextZones => $"Ajouter ({Db.GetLabelPlanningTextZone(projectId)})",
                ActiveListKind.TaskCategories => $"Ajouter ({Db.GetLabelTaskCategory(projectId)})",
                ActiveListKind.TaskUrgencies => $"Ajouter ({Db.GetLabelTaskUrgency(projectId)})",
                _ => "Ajouter"
            };

            var name = Interaction.InputBox("Nom :", title, "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            switch (_activeList)
            {
                case ActiveListKind.Reserves: Db.InsertReserve(projectId, name); break;
                case ActiveListKind.Requesters: Db.InsertRequester(projectId, name); break;
                case ActiveListKind.Companies: Db.InsertCompany(projectId, name); break;
                case ActiveListKind.Places: Db.InsertPlace(projectId, name); break;
                case ActiveListKind.Etages: Db.InsertEtage(projectId, name); break;
                case ActiveListKind.PlanningTextZones: Db.InsertPlanningTextZone(projectId, name); break;
                case ActiveListKind.TaskCategories: Db.InsertTaskCategory(projectId, name); break;
                case ActiveListKind.TaskUrgencies: Db.InsertTaskUrgency(projectId, name); break;
                default: return;
            }

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonRename_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var oldName = GetActiveSelectedItemText();
            if (string.IsNullOrWhiteSpace(oldName))
                return;

            var title = _activeList switch
            {
                ActiveListKind.Reserves => $"Renommer ({Db.GetLabelReserve(projectId)})",
                ActiveListKind.Requesters => $"Renommer ({Db.GetLabelRequestedBy(projectId)})",
                ActiveListKind.Companies => $"Renommer ({Db.GetLabelPerformedBy(projectId)})",
                ActiveListKind.Places => $"Renommer ({Db.GetLabelPlace(projectId)})",
                ActiveListKind.Etages => $"Renommer ({Db.GetLabelEtage(projectId)})",
                ActiveListKind.PlanningTextZones => $"Renommer ({Db.GetLabelPlanningTextZone(projectId)})",
                ActiveListKind.TaskCategories => $"Renommer ({Db.GetLabelTaskCategory(projectId)})",
                ActiveListKind.TaskUrgencies => $"Renommer ({Db.GetLabelTaskUrgency(projectId)})",
                _ => "Renommer"
            };

            var newName = Interaction.InputBox("Nouveau nom :", title, oldName).Trim();
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName)
                return;

            switch (_activeList)
            {
                case ActiveListKind.Reserves: Db.RenameReserve(projectId, oldName, newName); break;
                case ActiveListKind.Requesters: Db.RenameRequester(projectId, oldName, newName); break;
                case ActiveListKind.Companies: Db.RenameCompany(projectId, oldName, newName); break;
                case ActiveListKind.Places: Db.RenamePlace(projectId, oldName, newName); break;
                case ActiveListKind.Etages: Db.RenameEtage(projectId, oldName, newName); break;
                case ActiveListKind.PlanningTextZones: Db.RenamePlanningTextZone(projectId, oldName, newName); break;
                case ActiveListKind.TaskCategories: Db.RenameTaskCategory(projectId, oldName, newName); break;
                case ActiveListKind.TaskUrgencies: Db.RenameTaskUrgency(projectId, oldName, newName); break;
                default: return;
            }

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur renommage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var name = GetActiveSelectedItemText();
            if (string.IsNullOrWhiteSpace(name))
                return;

            var ok = MessageBox.Show($"Supprimer « {name} » ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes)
                return;

            switch (_activeList)
            {
                case ActiveListKind.Reserves: Db.DeleteReserve(projectId, name); break;
                case ActiveListKind.Requesters: Db.DeleteRequester(projectId, name); break;
                case ActiveListKind.Companies: Db.DeleteCompany(projectId, name); break;
                case ActiveListKind.Places: Db.DeletePlace(projectId, name); break;
                case ActiveListKind.Etages: Db.DeleteEtage(projectId, name); break;
                case ActiveListKind.PlanningTextZones: Db.DeletePlanningTextZone(projectId, name); break;
                case ActiveListKind.TaskCategories: Db.DeleteTaskCategory(projectId, name); break;
                case ActiveListKind.TaskUrgencies: Db.DeleteTaskUrgency(projectId, name); break;
                default: return;
            }

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur suppression", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonSetDefault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var value = GetActiveDefaultText() ?? "";

            // value peut être "" => efface le défaut
            switch (_activeList)
            {
                case ActiveListKind.Reserves: Db.SetDefaultReserve(projectId, value); break;
                case ActiveListKind.Requesters: Db.SetDefaultRequester(projectId, value); break;
                case ActiveListKind.Companies: Db.SetDefaultCompany(projectId, value); break;
                case ActiveListKind.Places: Db.SetDefaultPlace(projectId, value); break;
                case ActiveListKind.Etages: Db.SetDefaultEtage(projectId, value); break;
                case ActiveListKind.PlanningTextZones: Db.SetDefaultPlanningTextZone(projectId, value); break;
                case ActiveListKind.TaskCategories: Db.SetDefaultTaskCategory(projectId, value); break;
                case ActiveListKind.TaskUrgencies: Db.SetDefaultTaskUrgency(projectId, value); break;
            }

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur défaut", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var items = _activeList switch
            {
                ActiveListKind.Reserves => Db.GetReserves(projectId),
                ActiveListKind.Requesters => Db.GetRequesters(projectId),
                ActiveListKind.Companies => Db.GetCompanies(projectId),
                ActiveListKind.Places => Db.GetPlaces(projectId),
                ActiveListKind.Etages => Db.GetEtages(projectId),
                ActiveListKind.PlanningTextZones => Db.GetPlanningTextZones(projectId),
                ActiveListKind.TaskCategories => Db.GetTaskCategories(projectId),
                ActiveListKind.TaskUrgencies => Db.GetTaskUrgencies(projectId),
                _ => new List<string>()
            };

            items = items.Select(s => (s ?? "").Trim())
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                         .ToList();

            var payload = new ListClipboardPayload
            {
                Kind = _activeList,
                SourceProjectId = projectId
            };
            payload.Items.AddRange(items);

            if (_activeList == ActiveListKind.Companies)
            {
                var colors = Db.GetCompanyColorMap(projectId);
                foreach (var kv in colors)
                {
                    var name = (kv.Key ?? "").Trim();
                    var hex = (kv.Value ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(hex))
                        payload.CompanyColorMap[name] = hex;
                }
            }
            else if (_activeList == ActiveListKind.TaskCategories)
            {
                var colors = Db.GetTaskCategoryColorMap(projectId);
                foreach (var kv in colors)
                {
                    var name = (kv.Key ?? "").Trim();
                    var hex = (kv.Value ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(hex))
                        payload.TaskCategoryColorMap[name] = hex;
                }
            }

            _listClipboard = payload;
            RefreshCommonButtonsState();

            MessageBox.Show($"{items.Count} éléments copiés.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonPaste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var targetProjectId = RequireCurrentProjectId();

            if (_listClipboard == null || _listClipboard.Items.Count == 0)
                return;

            var existing = _activeList switch
            {
                ActiveListKind.Reserves => Db.GetReserves(targetProjectId),
                ActiveListKind.Requesters => Db.GetRequesters(targetProjectId),
                ActiveListKind.Companies => Db.GetCompanies(targetProjectId),
                ActiveListKind.Places => Db.GetPlaces(targetProjectId),
                ActiveListKind.Etages => Db.GetEtages(targetProjectId),
                ActiveListKind.PlanningTextZones => Db.GetPlanningTextZones(targetProjectId),
                ActiveListKind.TaskCategories => Db.GetTaskCategories(targetProjectId),
                ActiveListKind.TaskUrgencies => Db.GetTaskUrgencies(targetProjectId),
                _ => new List<string>()
            };

            existing = existing.Select(s => (s ?? "").Trim())
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                               .ToList();

            int inserted = 0;
            int skipped = 0;

            foreach (var raw in _listClipboard.Items)
            {
                var name = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (existing.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                switch (_activeList)
                {
                    case ActiveListKind.Reserves: Db.InsertReserve(targetProjectId, name); break;
                    case ActiveListKind.Requesters: Db.InsertRequester(targetProjectId, name); break;
                    case ActiveListKind.Companies:
                        Db.InsertCompany(targetProjectId, name);
                        // N'applique la couleur copiée QUE si la source du clipboard est le même projet
                        if (_listClipboard.SourceProjectId == targetProjectId)
                        {
                            if (_listClipboard.CompanyColorMap.TryGetValue(name, out var hex) && !string.IsNullOrWhiteSpace(hex))
                                Db.SetCompanyColorHex(targetProjectId, name, hex);
                        }
                        break;
                    case ActiveListKind.Places: Db.InsertPlace(targetProjectId, name); break;
                    case ActiveListKind.Etages: Db.InsertEtage(targetProjectId, name); break;
                    case ActiveListKind.PlanningTextZones: Db.InsertPlanningTextZone(targetProjectId, name); break;
                    case ActiveListKind.TaskCategories:
                        Db.InsertTaskCategory(targetProjectId, name);
                        // N'applique la couleur copiée QUE si la source du clipboard est le même projet
                        if (_listClipboard.SourceProjectId == targetProjectId)
                        {
                            if (_listClipboard.TaskCategoryColorMap.TryGetValue(name, out var catHex) && !string.IsNullOrWhiteSpace(catHex))
                                Db.SetTaskCategoryColorHex(targetProjectId, name, catHex);
                        }
                        break;
                    case ActiveListKind.TaskUrgencies: Db.InsertTaskUrgency(targetProjectId, name); break;
                    default: continue;
                }

                existing.Add(name);
                inserted++;
            }

            Reload();

            MessageBox.Show($"Collage terminé.\n\nAjoutés : {inserted}\nDéjà présents : {skipped}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================
    // Libellés
    // =========================
    private void SaveLabels_Click(object sender, RoutedEventArgs e) => SaveLabelsCore();

    private void SaveLabelsCore()
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            Db.SetLabelReserve(projectId, (LabelReserveTextBox.Text ?? "").Trim());
            Db.SetLabelRequestedBy(projectId, (LabelRequestedByTextBox.Text ?? "").Trim());
            Db.SetLabelPerformedBy(projectId, (LabelPerformedByTextBox.Text ?? "").Trim());
            Db.SetLabelPlace(projectId, (LabelPlaceTextBox.Text ?? "").Trim());
            Db.SetLabelEtage(projectId, (LabelEtageTextBox.Text ?? "").Trim());
            Db.SetLabelDeadline(projectId, (LabelDeadlineTextBox.Text ?? "").Trim());
            Db.SetLabelPlanningTextZone(projectId, (LabelPlanningTextZoneTextBox.Text ?? "").Trim());
            Db.SetLabelTaskCategory(projectId, (LabelTaskCategoryTextBox.Text ?? "").Trim());
            Db.SetLabelTaskUrgency(projectId, (LabelTaskUrgencyTextBox.Text ?? "").Trim());

            _labelsDirty = false;

            MessageBox.Show("Libellés enregistrés.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================
    // Couleur Entreprise
    // =========================
    private void CompaniesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedCompanyColorPreview();
        RefreshCommonButtonsState();
    }

    private void RefreshSelectedCompanyColorPreview()
    {
        try
        {
            var projectId = Db.GetCurrentProjectId();
            if (!projectId.HasValue || projectId.Value <= 0)
            {
                SetSelectedCompanyColorPreview(null, false);
                return;
            }

            var companyName = GetSelectedCompanyName();
            if (string.IsNullOrWhiteSpace(companyName))
            {
                SetSelectedCompanyColorPreview(null, false);
                return;
            }

            var hex = Db.GetCompanyColorHex(projectId.Value, companyName);
            var isGradient = Db.GetCompanyIsGradient(projectId.Value, companyName);
            SetSelectedCompanyColorPreview(hex, isGradient);
        }
        catch
        {
            SetSelectedCompanyColorPreview(null, false);
        }
    }

    private System.Windows.Media.Brush BuildCompanyColorBrush(long projectId, string companyName)
    {
        try
        {
            var hex = Db.GetCompanyColorHex(projectId, companyName);
            if (string.IsNullOrWhiteSpace(hex))
                return MediaBrushes.Transparent;

            var isGradient = Db.GetCompanyIsGradient(projectId, companyName);
            return Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, isGradient) ?? MediaBrushes.Transparent;
        }
        catch
        {
            return MediaBrushes.Transparent;
        }
    }

    private void SetSelectedCompanyColorPreview(string? colorHex, bool isGradient)
    {
        if (PickCompanyColorButton == null) return;

        _suppressCompanyGradientCheckBoxEvent = true;
        if (CompanyGradientCheckBox != null)
            CompanyGradientCheckBox.IsChecked = isGradient;
        _suppressCompanyGradientCheckBoxEvent = false;

        ApplyColorToButton(PickCompanyColorButton, colorHex, isGradient);
    }

    // ✅ Couleur choisie directement sur le bouton "Couleur..." (25.07.2026, demande de
    // Joe), remplace le carré de prévisualisation séparé. Texte blanc/noir choisi selon la
    // luminance pour rester lisible sur n'importe quelle couleur de fond.
    private static void ApplyColorToButton(System.Windows.Controls.Button button, string? colorHex, bool isGradient)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            return;
        }

        try
        {
            button.Background = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(colorHex, isGradient) ?? MediaBrushes.White;

            var c = (System.Windows.Media.Color)new System.Windows.Media.ColorConverter().ConvertFrom(colorHex)!;
            double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            button.Foreground = luminance > 0.55 ? MediaBrushes.Black : MediaBrushes.White;
        }
        catch
        {
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        }
    }

    private bool _suppressCompanyGradientCheckBoxEvent;

    private void CompanyGradientCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCompanyGradientCheckBoxEvent) return;

        try
        {
            var projectId = RequireCurrentProjectId();
            var companyName = GetSelectedCompanyName();
            if (string.IsNullOrWhiteSpace(companyName)) return;

            var hex = Db.GetCompanyColorHex(projectId, companyName);
            if (string.IsNullOrWhiteSpace(hex)) return;

            var isGradient = CompanyGradientCheckBox?.IsChecked == true;
            Db.SetCompanyColorHex(projectId, companyName, hex, isGradient);

            Reload();

            // ✅ Reload() recree les items de CompaniesListBox (nouvelles instances) -> la
            // selection precedente est perdue, ce qui reinitialisait visuellement la case
            // "Degrade" juste apres l'avoir cochee (25.07.2026, signale par Joe).
            CompaniesListBox.SelectedItem = (CompaniesListBox.ItemsSource as System.Collections.Generic.IEnumerable<CompanyListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, companyName, StringComparison.OrdinalIgnoreCase));

            SetSelectedCompanyColorPreview(hex, isGradient);
            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PickCompanyColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var companyName = GetSelectedCompanyName();
            if (string.IsNullOrWhiteSpace(companyName))
                return;

            var currentHex = Db.GetCompanyColorHex(projectId, companyName);

            var dlg = new Forms.ColorDialog { FullOpen = true };
            if (!string.IsNullOrWhiteSpace(currentHex))
            {
                try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(currentHex); }
                catch { }
            }

            if (dlg.ShowDialog() != Forms.DialogResult.OK)
                return;

            var c = dlg.Color;
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var isGradient = CompanyGradientCheckBox?.IsChecked == true;
            Db.SetCompanyColorHex(projectId, companyName, hex, isGradient);

            // Forcer le reload de la page Planning pour prendre en compte la nouvelle couleur
            Reload();

            // ✅ Reload() recree les items de CompaniesListBox -> la selection (et donc le
            // contraste du bouton "Couleur") se perdait juste apres avoir choisi une couleur
            // (25.07.2026, signale par Joe). Meme correctif que CompanyGradientCheckBox_Changed.
            CompaniesListBox.SelectedItem = (CompaniesListBox.ItemsSource as System.Collections.Generic.IEnumerable<CompanyListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, companyName, StringComparison.OrdinalIgnoreCase));
            SetSelectedCompanyColorPreview(hex, isGradient);

            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearCompanyColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var companyName = GetSelectedCompanyName();
            if (string.IsNullOrWhiteSpace(companyName))
                return;

            Db.DeleteCompanyColor(projectId, companyName);
            // Forcer le reload de la page Planning pour prendre en compte la suppression
            Reload();

            CompaniesListBox.SelectedItem = (CompaniesListBox.ItemsSource as System.Collections.Generic.IEnumerable<CompanyListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, companyName, StringComparison.OrdinalIgnoreCase));
            SetSelectedCompanyColorPreview(null, false);

            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================
    // ✅ Couleur Catégorie (17.07.2026) — miroir exact du bloc "Couleur Entreprise" ci-dessus,
    // indépendant (table/UI séparées, voir TaskCategoryColors dans Db.cs).
    // =========================
    private void RefreshSelectedTaskCategoryColorPreview()
    {
        try
        {
            var projectId = Db.GetCurrentProjectId();
            if (!projectId.HasValue || projectId.Value <= 0)
            {
                SetSelectedTaskCategoryColorPreview(null, false);
                return;
            }

            var categoryName = GetSelectedTaskCategoryName();
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                SetSelectedTaskCategoryColorPreview(null, false);
                return;
            }

            var hex = Db.GetTaskCategoryColorHex(projectId.Value, categoryName);
            var isGradient = Db.GetTaskCategoryIsGradient(projectId.Value, categoryName);
            SetSelectedTaskCategoryColorPreview(hex, isGradient);
        }
        catch
        {
            SetSelectedTaskCategoryColorPreview(null, false);
        }
    }

    private System.Windows.Media.Brush BuildTaskCategoryColorBrush(long projectId, string categoryName)
    {
        try
        {
            var hex = Db.GetTaskCategoryColorHex(projectId, categoryName);
            if (string.IsNullOrWhiteSpace(hex))
                return MediaBrushes.Transparent;

            var isGradient = Db.GetTaskCategoryIsGradient(projectId, categoryName);
            return Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex, isGradient) ?? MediaBrushes.Transparent;
        }
        catch
        {
            return MediaBrushes.Transparent;
        }
    }

    private bool _suppressTaskCategoryGradientCheckBoxEvent;

    private void SetSelectedTaskCategoryColorPreview(string? colorHex, bool isGradient)
    {
        if (PickTaskCategoryColorButton == null) return;

        _suppressTaskCategoryGradientCheckBoxEvent = true;
        if (TaskCategoryGradientCheckBox != null)
            TaskCategoryGradientCheckBox.IsChecked = isGradient;
        _suppressTaskCategoryGradientCheckBoxEvent = false;

        ApplyColorToButton(PickTaskCategoryColorButton, colorHex, isGradient);
    }

    private void TaskCategoryGradientCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTaskCategoryGradientCheckBoxEvent) return;

        try
        {
            var projectId = RequireCurrentProjectId();
            var categoryName = GetSelectedTaskCategoryName();
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            var hex = Db.GetTaskCategoryColorHex(projectId, categoryName);
            if (string.IsNullOrWhiteSpace(hex)) return;

            var isGradient = TaskCategoryGradientCheckBox?.IsChecked == true;
            Db.SetTaskCategoryColorHex(projectId, categoryName, hex, isGradient);

            Reload();

            // ✅ Reload() recree les items de TaskCategoriesListBox -> selection perdue,
            // meme correctif que pour CompanyGradientCheckBox_Changed ci-dessus.
            TaskCategoriesListBox.SelectedItem = (TaskCategoriesListBox.ItemsSource as System.Collections.Generic.IEnumerable<TaskCategoryListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, categoryName, StringComparison.OrdinalIgnoreCase));

            SetSelectedTaskCategoryColorPreview(hex, isGradient);
            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PickTaskCategoryColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var categoryName = GetSelectedTaskCategoryName();
            if (string.IsNullOrWhiteSpace(categoryName))
                return;

            var currentHex = Db.GetTaskCategoryColorHex(projectId, categoryName);

            var dlg = new Forms.ColorDialog { FullOpen = true };
            if (!string.IsNullOrWhiteSpace(currentHex))
            {
                try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(currentHex); }
                catch { }
            }

            if (dlg.ShowDialog() != Forms.DialogResult.OK)
                return;

            var c = dlg.Color;
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var isGradient = TaskCategoryGradientCheckBox?.IsChecked == true;
            Db.SetTaskCategoryColorHex(projectId, categoryName, hex, isGradient);

            // Forcer le reload de la page Planning pour prendre en compte la nouvelle couleur
            Reload();

            // ✅ Reload() recree les items de TaskCategoriesListBox -> meme correctif que
            // PickCompanyColor_Click (25.07.2026, contraste du bouton "Couleur" perdu).
            TaskCategoriesListBox.SelectedItem = (TaskCategoriesListBox.ItemsSource as System.Collections.Generic.IEnumerable<TaskCategoryListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            SetSelectedTaskCategoryColorPreview(hex, isGradient);

            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearTaskCategoryColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var projectId = RequireCurrentProjectId();

            var categoryName = GetSelectedTaskCategoryName();
            if (string.IsNullOrWhiteSpace(categoryName))
                return;

            Db.DeleteTaskCategoryColor(projectId, categoryName);
            // Forcer le reload de la page Planning pour prendre en compte la suppression
            Reload();

            TaskCategoriesListBox.SelectedItem = (TaskCategoriesListBox.ItemsSource as System.Collections.Generic.IEnumerable<TaskCategoryListItem>)?
                .FirstOrDefault(x => string.Equals(x.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            SetSelectedTaskCategoryColorPreview(null, false);

            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}