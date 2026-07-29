// File: Pages/ListsPage.xaml.cs
using System;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
using System.Windows.Media;
using Iziregi.Test.Data;
using Microsoft.VisualBasic;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;

namespace Iziregi.Test.Pages;

public partial class ListsPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    // =========================
    // ✅ Libellés + listes : édition directe, mais un seul bouton "Enregistrer" global
    // valide tout (28.07.2026, demande de Joe — remplace l'auto-enregistrement du
    // 28.07.2026 précédent). _labelsDirty/_listsDirty pilotent l'état du bouton
    // Enregistrer et l'avertissement à la sortie de la page (voir HasUnsavedChanges,
    // utilisé par MainWindow.ConfirmLeaveListsPageIfDirty).
    // =========================
    private bool _isLoadingLabels = false;
    private bool _labelsDirty = false;
    private bool _listsDirty = false;

    public bool HasUnsavedChanges => _labelsDirty || _listsDirty;

    private void AnyLabelTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoadingLabels) return;
        _labelsDirty = true;
        RefreshCommonButtonsState();
    }

    // ✅ Valeurs par défaut (28.07.2026, demande de Joe) : elles aussi différées, écrites
    // seulement par SaveAllNow -- ce changement doit juste activer le bouton Enregistrer.
    private void AnyDefaultComboBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoadingLabels) return;
        _listsDirty = true;
        RefreshCommonButtonsState();
    }

    // ✅ Cliquer dans un champ de libellé (au-dessus d'une liste) désactive la liste
    // active — sinon la bordure bleue de la liste restait allumée EN MÊME TEMPS que la
    // bordure de focus du libellé, donnant l'impression de deux éléments sélectionnés.
    private void AnyLabelTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SetActiveList(ActiveListKind.None);
        if (sender is System.Windows.Controls.TextBox tb)
            _lastLabelValues[tb] = tb.Text ?? "";
    }

    // ✅ Doublon entre libellés (28.07.2026, demande de Joe) : deux champs ne peuvent pas
    // porter le même nom -- avertit et rétablit l'ancien texte si c'est le cas.
    private void AnyLabelTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        var text = (tb.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var others = new[]
        {
            LabelReserveTextBox, LabelRequestedByTextBox, LabelPerformedByTextBox, LabelPlaceTextBox,
            LabelEtageTextBox, LabelSignatoryNameTextBox, LabelPlanningTextZoneTextBox, LabelTaskCategoryTextBox,
            LabelTaskUrgencyTextBox
        };

        var duplicate = others.FirstOrDefault(o => o != null && !ReferenceEquals(o, tb)
            && string.Equals((o.Text ?? "").Trim(), text, StringComparison.OrdinalIgnoreCase));

        if (duplicate != null)
        {
            MessageBox.Show($"« {text} » est déjà utilisé comme nom de champ ailleurs.", "Doublon", MessageBoxButton.OK, MessageBoxImage.Warning);
            tb.Text = _lastLabelValues.TryGetValue(tb, out var prev) ? prev : "";
        }
    }

    // ✅ Valeur de chaque champ de libellé au moment où il a reçu le focus (28.07.2026) :
    // permet de restaurer l'ancien texte en cas de doublon détecté au LostFocus.
    private readonly Dictionary<System.Windows.Controls.TextBox, string> _lastLabelValues = new();

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
        SignatoryNames = 9,
    }

    private ActiveListKind _activeList = ActiveListKind.None;

    // ✅ Édition directe (28.07.2026, demande de Joe) : "ce qui est sélectionné" est soit une
    // LIGNE précise (_activeRowItem non null -- l'utilisateur édite ou vient d'éditer ce
    // nom), soit LA LISTE ENTIÈRE (_activeRowItem == null -- clic dans le vide de la liste).
    // Détermine le comportement de Supprimer/Copier (une ligne vs toute la liste).
    private EditableListItem? _activeRowItem;

    private void SetActiveList(ActiveListKind kind)
    {
        _activeList = kind;
        RefreshCommonButtonsState();
        RefreshActiveListBorders();
    }

    // =========================
    // ✅ Édition directe des listes (28.07.2026, demande de Joe) : chaque ligne de liste est
    // un TextBox modifiable en place (voir ListsPage.xaml, EditableRowTextBoxStyle) au lieu
    // d'un texte simple + bouton Ajouter/Renommer séparé. Rien n'est écrit en base tant que
    // "Enregistrer" n'est pas cliqué (voir SaveAllNow) -- les listes ci-dessous ne sont que
    // l'état en mémoire en cours d'édition.
    // =========================
    private sealed class EditableListItem : INotifyPropertyChanged
    {
        // Null = ligne créée pendant cette session d'édition, pas encore en base.
        public string? OriginalName { get; set; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private System.Windows.Media.Brush _colorBrush = MediaBrushes.Transparent;
        public System.Windows.Media.Brush ColorBrush
        {
            get => _colorBrush;
            set { if (_colorBrush != value) { _colorBrush = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly ObservableCollection<EditableListItem> _reservesItems = new();
    private readonly ObservableCollection<EditableListItem> _requestersItems = new();
    private readonly ObservableCollection<EditableListItem> _companiesItems = new();
    private readonly ObservableCollection<EditableListItem> _placesItems = new();
    private readonly ObservableCollection<EditableListItem> _etagesItems = new();
    private readonly ObservableCollection<EditableListItem> _planningTextZonesItems = new();
    private readonly ObservableCollection<EditableListItem> _taskCategoriesItems = new();
    private readonly ObservableCollection<EditableListItem> _taskUrgenciesItems = new();
    private readonly ObservableCollection<EditableListItem> _signatoryNamesItems = new();

    // Instantané des noms tels que chargés depuis la base, pour calculer au moment
    // d'"Enregistrer" ce qui a été ajouté/renommé/supprimé (voir SaveAllNow).
    private List<string> _reservesOriginal = new();
    private List<string> _requestersOriginal = new();
    private List<string> _companiesOriginal = new();
    private List<string> _placesOriginal = new();
    private List<string> _etagesOriginal = new();
    private List<string> _planningTextZonesOriginal = new();
    private List<string> _taskCategoriesOriginal = new();
    private List<string> _taskUrgenciesOriginal = new();
    private List<string> _signatoryNamesOriginal = new();

    private ObservableCollection<EditableListItem>? GetActiveItemsCollection() => _activeList switch
    {
        ActiveListKind.Reserves => _reservesItems,
        ActiveListKind.Requesters => _requestersItems,
        ActiveListKind.Companies => _companiesItems,
        ActiveListKind.Places => _placesItems,
        ActiveListKind.Etages => _etagesItems,
        ActiveListKind.PlanningTextZones => _planningTextZonesItems,
        ActiveListKind.TaskCategories => _taskCategoriesItems,
        ActiveListKind.TaskUrgencies => _taskUrgenciesItems,
        ActiveListKind.SignatoryNames => _signatoryNamesItems,
        _ => null
    };

    private System.Windows.Controls.ListBox? GetActiveListBox() => _activeList switch
    {
        ActiveListKind.Reserves => ReservesListBox,
        ActiveListKind.Requesters => RequestersListBox,
        ActiveListKind.Companies => CompaniesListBox,
        ActiveListKind.Places => PlacesListBox,
        ActiveListKind.Etages => EtagesListBox,
        ActiveListKind.PlanningTextZones => PlanningTextZonesListBox,
        ActiveListKind.TaskCategories => TaskCategoriesListBox,
        ActiveListKind.TaskUrgencies => TaskUrgenciesListBox,
        ActiveListKind.SignatoryNames => SignatoryNamesListBox,
        _ => null
    };

    private (ObservableCollection<EditableListItem> Collection, ActiveListKind Kind)[] AllLists() => new[]
    {
        (_reservesItems, ActiveListKind.Reserves),
        (_requestersItems, ActiveListKind.Requesters),
        (_companiesItems, ActiveListKind.Companies),
        (_placesItems, ActiveListKind.Places),
        (_etagesItems, ActiveListKind.Etages),
        (_planningTextZonesItems, ActiveListKind.PlanningTextZones),
        (_taskCategoriesItems, ActiveListKind.TaskCategories),
        (_taskUrgenciesItems, ActiveListKind.TaskUrgencies),
        (_signatoryNamesItems, ActiveListKind.SignatoryNames),
    };

    private (ObservableCollection<EditableListItem>? Collection, ActiveListKind? Kind) FindOwningCollection(EditableListItem item)
    {
        foreach (var (collection, kind) in AllLists())
            if (collection.Contains(item))
                return (collection, kind);

        return (null, null);
    }

    // =========================
    // ✅ Édition directe des lignes (28.07.2026, demande de Joe) : clic sur une ligne pour
    // l'éditer, Entrée/flèche bas pour passer à la suivante (crée une nouvelle ligne vide en
    // bas si besoin), flèche haut pour remonter. Vider une ligne la supprime (les suivantes
    // remontent).
    // =========================

    private void ListItemTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        if (tb.DataContext is not EditableListItem item) return;

        var (_, kind) = FindOwningCollection(item);
        if (kind.HasValue) SetActiveList(kind.Value);
        _activeRowItem = item;
        RefreshCommonButtonsState();

        if (kind == ActiveListKind.Companies) RefreshSelectedCompanyColorPreview();
        else if (kind == ActiveListKind.TaskCategories) RefreshSelectedTaskCategoryColorPreview();
    }

    private void ListItemTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        if (tb.DataContext is not EditableListItem item) return;

        var (collection, _) = FindOwningCollection(item);
        if (collection == null) return;

        var text = (item.Name ?? "").Trim();
        item.Name = text;

        if (string.IsNullOrWhiteSpace(text))
        {
            // ✅ Ligne vidée : supprimée (sauf si c'est déjà LA ligne vide finale — rien à
            // faire), les lignes suivantes remontent automatiquement (ObservableCollection).
            var idx = collection.IndexOf(item);
            if (idx >= 0 && idx < collection.Count - 1)
            {
                collection.RemoveAt(idx);
                _listsDirty = true;
            }
        }
        else
        {
            // ✅ Doublon (28.07.2026, demande de Joe) : pas deux fois le même nom dans la
            // même liste -- avertit et rétablit l'ancien texte (ou supprime la ligne si
            // c'était une ligne toute neuve).
            var duplicate = collection.FirstOrDefault(x => !ReferenceEquals(x, item)
                && string.Equals((x.Name ?? "").Trim(), text, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                MessageBox.Show($"« {text} » existe déjà dans cette liste.", "Doublon", MessageBoxButton.OK, MessageBoxImage.Warning);
                item.Name = item.OriginalName ?? "";
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    var idx = collection.IndexOf(item);
                    if (idx >= 0 && idx < collection.Count - 1) collection.RemoveAt(idx);
                }
                return;
            }

            // ✅ Dernière ligne remplie -> garantit toujours une ligne vide finale pour
            // continuer à taper (équivalent de "Ajouter").
            if (collection.Count > 0 && ReferenceEquals(collection[^1], item))
                collection.Add(new EditableListItem());

            _listsDirty = true;
        }

        RefreshCommonButtonsState();
    }

    private void ListItemTextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        if (tb.DataContext is not EditableListItem item) return;

        if (e.Key == WpfKey.Enter || e.Key == WpfKey.Down)
        {
            e.Handled = true;
            MoveToAdjacentRow(item, +1);
        }
        else if (e.Key == WpfKey.Up)
        {
            e.Handled = true;
            MoveToAdjacentRow(item, -1);
        }
    }

    private void MoveToAdjacentRow(EditableListItem item, int direction)
    {
        var (collection, kind) = FindOwningCollection(item);
        if (collection == null || !kind.HasValue) return;

        var listBox = GetActiveListBox();
        if (listBox == null) return;

        var idx = collection!.IndexOf(item);
        if (idx < 0) return;

        // ✅ Si on descend depuis la dernière ligne et qu'elle contient du texte, garantit
        // qu'une ligne vide existe en dessous AVANT de calculer l'index cible : c'est NOUS
        // qui déclenchons le changement de focus ici, le LostFocus normal (qui ferait la
        // même chose) n'a pas encore eu lieu à ce stade.
        if (direction > 0 && idx == collection.Count - 1 && !string.IsNullOrWhiteSpace(item.Name))
            collection.Add(new EditableListItem());

        var targetIdx = idx + direction;
        if (targetIdx < 0 || targetIdx >= collection.Count) return;

        FocusRowAtIndex(listBox, targetIdx);
    }

    private static void FocusRowAtIndex(System.Windows.Controls.ListBox listBox, int index)
    {
        if (index < 0 || index >= listBox.Items.Count) return;

        void TryFocus()
        {
            var container = listBox.ItemContainerGenerator.ContainerFromIndex(index) as System.Windows.Controls.ListBoxItem;
            var tb = container == null ? null : FindVisualChild<System.Windows.Controls.TextBox>(container);
            if (tb != null)
            {
                tb.Focus();
                tb.CaretIndex = tb.Text?.Length ?? 0;
            }
        }

        listBox.ScrollIntoView(listBox.Items[index]);
        listBox.UpdateLayout();
        TryFocus();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
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
        SetBorder(SignatoryNamesListBorder, _activeList == ActiveListKind.SignatoryNames);
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

    // ✅ Édition directe (28.07.2026) : "sélectionné" = la ligne actuellement éditée
    // (_activeRowItem), pas ListBox.SelectedItem (qui n'a plus vraiment de sens ici).
    private string? GetSelectedCompanyName() =>
        _activeList == ActiveListKind.Companies ? _activeRowItem?.Name : null;

    // ✅ Couleurs Catégories (17.07.2026) : même mécanisme que GetSelectedCompanyName ci-dessus.
    private string? GetSelectedTaskCategoryName() =>
        _activeList == ActiveListKind.TaskCategories ? _activeRowItem?.Name : null;

    private void RefreshCommonButtonsState()
    {
        if (CommonDeleteButton == null || CommonCopyButton == null || CommonPasteButton == null || CommonSaveButton == null)
            return;

        var hasProject = HasCurrentProject();
        var hasActiveList = _activeList != ActiveListKind.None;
        var collection = GetActiveItemsCollection();
        var hasAnyContent = collection?.Any(x => !string.IsNullOrWhiteSpace(x.Name)) == true;

        // ✅ Édition directe (28.07.2026) : Supprimer/Copier restent actifs dès qu'une liste
        // est active (une ligne précise OU la liste entière peut être ciblée -- voir
        // _activeRowItem), pas seulement si une ligne précise est sélectionnée.
        CommonDeleteButton.IsEnabled = hasProject && hasActiveList && (_activeRowItem != null || hasAnyContent);
        CommonCopyButton.IsEnabled = hasProject && hasActiveList && (_activeRowItem != null || hasAnyContent);
        CommonPasteButton.IsEnabled = hasProject && hasActiveList && _listClipboard != null && _listClipboard.Items.Count > 0;

        // ✅ 29.07.2026 (Joe : "ça me fait faire un clic en plus pour le dégriser") : le
        // bouton reste toujours cliquable -- cliquer sans rien à enregistrer ne fait rien
        // (SaveAllNow n'écrit que ce qui a changé).
        CommonSaveButton.IsEnabled = hasProject;

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
                ActiveListKind.SignatoryNames => pid > 0 ? Db.GetLabelSignatoryName(pid) : "Nom",
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

    public ListsPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        // ✅ Lié une seule fois : ReloadCore ne fait plus que vider/remplir ces mêmes
        // collections (PopulateItems), jamais réassigner ItemsSource (28.07.2026).
        ReservesListBox.ItemsSource = _reservesItems;
        RequestersListBox.ItemsSource = _requestersItems;
        CompaniesListBox.ItemsSource = _companiesItems;
        PlacesListBox.ItemsSource = _placesItems;
        EtagesListBox.ItemsSource = _etagesItems;
        PlanningTextZonesListBox.ItemsSource = _planningTextZonesItems;
        TaskCategoriesListBox.ItemsSource = _taskCategoriesItems;
        TaskUrgenciesListBox.ItemsSource = _taskUrgenciesItems;
        SignatoryNamesListBox.ItemsSource = _signatoryNamesItems;
    }

    public void Reload()
    {
        _isLoadingLabels = true;
        try { ReloadCore(); }
        finally { _isLoadingLabels = false; }

        _labelsDirty = false;
    }

    // ✅ Remplit une liste éditable + son instantané "original" (28.07.2026, demande de
    // Joe) : une ligne vide finale est toujours ajoutée (on y tape pour créer une entrée,
    // voir ListItemTextBox_LostFocus / PreviewKeyDown).
    private static void PopulateItems(
        ObservableCollection<EditableListItem> collection,
        List<string> names,
        List<string> originalSnapshot,
        Func<string, System.Windows.Media.Brush>? colorFor = null)
    {
        collection.Clear();
        originalSnapshot.Clear();
        originalSnapshot.AddRange(names);

        foreach (var name in names)
            collection.Add(new EditableListItem
            {
                OriginalName = name,
                Name = name,
                ColorBrush = colorFor?.Invoke(name) ?? MediaBrushes.Transparent
            });

        collection.Add(new EditableListItem());
    }

    private void ReloadCore()
    {
        _activeRowItem = null;

        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _reservesItems.Clear(); _reservesOriginal.Clear();
            _requestersItems.Clear(); _requestersOriginal.Clear();
            _companiesItems.Clear(); _companiesOriginal.Clear();
            _placesItems.Clear(); _placesOriginal.Clear();
            _etagesItems.Clear(); _etagesOriginal.Clear();
            _planningTextZonesItems.Clear(); _planningTextZonesOriginal.Clear();
            _taskCategoriesItems.Clear(); _taskCategoriesOriginal.Clear();
            _taskUrgenciesItems.Clear(); _taskUrgenciesOriginal.Clear();
            _signatoryNamesItems.Clear(); _signatoryNamesOriginal.Clear();

            DefaultReserveComboBox.ItemsSource = null;
            DefaultRequesterComboBox.ItemsSource = null;
            DefaultCompanyComboBox.ItemsSource = null;
            DefaultPlaceComboBox.ItemsSource = null;
            DefaultEtageComboBox.ItemsSource = null;
            DefaultPlanningTextZoneComboBox.ItemsSource = null;
            DefaultTaskCategoryComboBox.ItemsSource = null;
            DefaultTaskUrgencyComboBox.ItemsSource = null;
            DefaultSignatoryNameComboBox.ItemsSource = null;

            DefaultReserveComboBox.SelectedItem = null; DefaultReserveComboBox.Text = "";
            DefaultRequesterComboBox.SelectedItem = null; DefaultRequesterComboBox.Text = "";
            DefaultCompanyComboBox.SelectedItem = null; DefaultCompanyComboBox.Text = "";
            DefaultPlaceComboBox.SelectedItem = null; DefaultPlaceComboBox.Text = "";
            DefaultEtageComboBox.SelectedItem = null; DefaultEtageComboBox.Text = "";
            DefaultPlanningTextZoneComboBox.SelectedItem = null; DefaultPlanningTextZoneComboBox.Text = "";
            DefaultTaskCategoryComboBox.SelectedItem = null; DefaultTaskCategoryComboBox.Text = "";
            DefaultTaskUrgencyComboBox.SelectedItem = null; DefaultTaskUrgencyComboBox.Text = "";
            DefaultSignatoryNameComboBox.SelectedItem = null; DefaultSignatoryNameComboBox.Text = "";

            LabelReserveTextBox.Text = "";
            LabelRequestedByTextBox.Text = "";
            LabelPerformedByTextBox.Text = "";
            LabelPlaceTextBox.Text = "";
            LabelEtageTextBox.Text = "";
            LabelSignatoryNameTextBox.Text = "";
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
        var signatoryNames = Db.GetSignatoryNames(projectId);

        PopulateItems(_reservesItems, reserves, _reservesOriginal);
        PopulateItems(_requestersItems, requesters, _requestersOriginal);
        PopulateItems(_companiesItems, companies, _companiesOriginal, name => BuildCompanyColorBrush(projectId, name));
        PopulateItems(_placesItems, places, _placesOriginal);
        PopulateItems(_etagesItems, etages, _etagesOriginal);
        PopulateItems(_planningTextZonesItems, planningTextZones, _planningTextZonesOriginal);
        PopulateItems(_taskCategoriesItems, taskCategories, _taskCategoriesOriginal, name => BuildTaskCategoryColorBrush(projectId, name));
        PopulateItems(_taskUrgenciesItems, taskUrgencies, _taskUrgenciesOriginal);
        PopulateItems(_signatoryNamesItems, signatoryNames, _signatoryNamesOriginal);

        var reserveDefaults = Db.WithEmptyOption(reserves);
        var requesterDefaults = Db.WithEmptyOption(requesters);
        var companyDefaults = Db.WithEmptyOption(companies);
        var placeDefaults = Db.WithEmptyOption(places);
        var etageDefaults = Db.WithEmptyOption(etages);
        var planningDefaults = Db.WithEmptyOption(planningTextZones);
        var taskCategoryDefaults = Db.WithEmptyOption(taskCategories);
        var taskUrgencyDefaults = Db.WithEmptyOption(taskUrgencies);
        var signatoryNameDefaults = Db.WithEmptyOption(signatoryNames);

        DefaultReserveComboBox.ItemsSource = reserveDefaults;
        DefaultRequesterComboBox.ItemsSource = requesterDefaults;
        DefaultCompanyComboBox.ItemsSource = companyDefaults;
        DefaultPlaceComboBox.ItemsSource = placeDefaults;
        DefaultEtageComboBox.ItemsSource = etageDefaults;
        DefaultPlanningTextZoneComboBox.ItemsSource = planningDefaults;
        DefaultTaskCategoryComboBox.ItemsSource = taskCategoryDefaults;
        DefaultTaskUrgencyComboBox.ItemsSource = taskUrgencyDefaults;
        DefaultSignatoryNameComboBox.ItemsSource = signatoryNameDefaults;

        var defReserve = Db.GetDefaultReserve(projectId) ?? "";
        var defRequester = Db.GetDefaultRequester(projectId) ?? "";
        var defCompany = Db.GetDefaultCompany(projectId) ?? "";
        var defPlace = Db.GetDefaultPlace(projectId) ?? "";
        var defEtage = Db.GetDefaultEtage(projectId) ?? "";
        var defPlanning = Db.GetDefaultPlanningTextZone(projectId) ?? "";
        var defTaskCategory = Db.GetDefaultTaskCategory(projectId) ?? "";
        var defTaskUrgency = Db.GetDefaultTaskUrgency(projectId) ?? "";
        var defSignatoryName = Db.GetDefaultSignatoryName(projectId) ?? "";

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

        DefaultSignatoryNameComboBox.SelectedItem = signatoryNameDefaults.Contains(defSignatoryName) ? defSignatoryName : "";
        DefaultSignatoryNameComboBox.Text = defSignatoryName;

        LabelReserveTextBox.Text = Db.GetLabelReserve(projectId);
        LabelRequestedByTextBox.Text = Db.GetLabelRequestedBy(projectId);
        LabelPerformedByTextBox.Text = Db.GetLabelPerformedBy(projectId);
        LabelPlaceTextBox.Text = Db.GetLabelPlace(projectId);
        LabelEtageTextBox.Text = Db.GetLabelEtage(projectId);
        LabelSignatoryNameTextBox.Text = Db.GetLabelSignatoryName(projectId);
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
    private void SignatoryNamesListBox_GotFocus(object sender, RoutedEventArgs e) => SetActiveList(ActiveListKind.SignatoryNames);

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
        _activeRowItem = null;
        ReservesListBox.Focus();
    }

    private void RequestersListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Requesters);
        _activeRowItem = null;
        RequestersListBox.Focus();
    }

    private void CompaniesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Companies);
        _activeRowItem = null;
        CompaniesListBox.Focus();
    }

    private void PlacesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Places);
        _activeRowItem = null;
        PlacesListBox.Focus();
    }

    private void EtagesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.Etages);
        _activeRowItem = null;
        EtagesListBox.Focus();
    }

    private void PlanningTextZonesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.PlanningTextZones);
        _activeRowItem = null;
        PlanningTextZonesListBox.Focus();
    }

    private void TaskCategoriesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.TaskCategories);
        _activeRowItem = null;
        TaskCategoriesListBox.Focus();
    }

    private void TaskUrgenciesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.TaskUrgencies);
        _activeRowItem = null;
        TaskUrgenciesListBox.Focus();
    }

    private void SignatoryNamesListBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetActiveList(ActiveListKind.SignatoryNames);
        _activeRowItem = null;
        SignatoryNamesListBox.Focus();
    }

    // =========================
    // ✅ Barre commune (28.07.2026, demande de Joe) : Ajouter/Renommer/Définir par défaut
    // supprimés (édition directe dans les lignes + ComboBox de valeur par défaut déjà
    // éditable au-dessus de chaque liste). Supprimer/Copier/Coller agissent sur ce qui est
    // "sélectionné" (_activeRowItem = une ligne, sinon la liste entière), purement en
    // mémoire -- rien n'est écrit en base tant que "Enregistrer" n'est pas cliqué.
    // =========================
    private void CommonDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!HasCurrentProject()) return;

            var collection = GetActiveItemsCollection();
            if (collection == null) return;

            if (_activeRowItem != null)
            {
                var name = (_activeRowItem.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                var ok = MessageBox.Show($"Supprimer « {name} » ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.Yes) return;

                collection.Remove(_activeRowItem);
                if (!collection.Any(x => string.IsNullOrWhiteSpace(x.Name)))
                    collection.Add(new EditableListItem());

                _activeRowItem = null;
                _listsDirty = true;
            }
            else
            {
                if (!collection.Any(x => !string.IsNullOrWhiteSpace(x.Name))) return;

                var ok = MessageBox.Show("Supprimer toute la liste ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.Yes) return;

                collection.Clear();
                collection.Add(new EditableListItem());
                _listsDirty = true;
            }

            RefreshCommonButtonsState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur suppression", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!HasCurrentProject()) return;
            var projectId = RequireCurrentProjectId();

            var collection = GetActiveItemsCollection();
            if (collection == null) return;

            // ✅ "Copier la liste" copie ce qui est sélectionné : une ligne précise
            // (_activeRowItem) ou, sinon, toute la liste (28.07.2026, demande de Joe).
            List<string> names = _activeRowItem != null && !string.IsNullOrWhiteSpace(_activeRowItem.Name)
                ? new List<string> { _activeRowItem.Name.Trim() }
                : collection.Select(x => (x.Name ?? "").Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList();

            if (names.Count == 0) return;

            var payload = new ListClipboardPayload { Kind = _activeList, SourceProjectId = projectId };
            payload.Items.AddRange(names);

            if (_activeList == ActiveListKind.Companies)
            {
                foreach (var n in names)
                {
                    var hex = Db.GetCompanyColorHex(projectId, n);
                    if (!string.IsNullOrWhiteSpace(hex)) payload.CompanyColorMap[n] = hex;
                }
            }
            else if (_activeList == ActiveListKind.TaskCategories)
            {
                foreach (var n in names)
                {
                    var hex = Db.GetTaskCategoryColorHex(projectId, n);
                    if (!string.IsNullOrWhiteSpace(hex)) payload.TaskCategoryColorMap[n] = hex;
                }
            }

            _listClipboard = payload;
            RefreshCommonButtonsState();

            MessageBox.Show($"{names.Count} élément(s) copié(s).", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (!HasCurrentProject()) return;
            var projectId = RequireCurrentProjectId();

            var collection = GetActiveItemsCollection();
            if (collection == null) return;
            if (_listClipboard == null || _listClipboard.Items.Count == 0) return;

            var existingNames = new HashSet<string>(
                collection.Select(x => (x.Name ?? "").Trim()).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            // ✅ "pour combler le vide" (28.07.2026, demande de Joe) : remplit d'abord les
            // lignes vides déjà présentes avant d'en ajouter de nouvelles.
            var emptySlots = collection.Where(x => string.IsNullOrWhiteSpace(x.Name)).ToList();

            int inserted = 0, skipped = 0;
            var sameProject = _listClipboard.SourceProjectId == projectId;

            foreach (var raw in _listClipboard.Items)
            {
                var name = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (existingNames.Contains(name)) { skipped++; continue; }

                EditableListItem target;
                if (emptySlots.Count > 0)
                {
                    target = emptySlots[0];
                    emptySlots.RemoveAt(0);
                    target.Name = name;
                }
                else
                {
                    target = new EditableListItem { Name = name };
                    collection.Insert(collection.Count > 0 ? collection.Count - 1 : 0, target);
                }

                // ✅ Couleur copiée : appliquée tout de suite en base (les couleurs Entreprise/
                // Catégorie restent en écriture immédiate, indépendamment du texte -- voir
                // PickCompanyColor_Click), seulement si la source du presse-papier est le même
                // projet.
                if (sameProject && _activeList == ActiveListKind.Companies
                    && _listClipboard.CompanyColorMap.TryGetValue(name, out var hex) && !string.IsNullOrWhiteSpace(hex))
                {
                    Db.SetCompanyColorHex(projectId, name, hex);
                    target.ColorBrush = BuildCompanyColorBrush(projectId, name);
                }
                else if (sameProject && _activeList == ActiveListKind.TaskCategories
                    && _listClipboard.TaskCategoryColorMap.TryGetValue(name, out var catHex) && !string.IsNullOrWhiteSpace(catHex))
                {
                    Db.SetTaskCategoryColorHex(projectId, name, catHex);
                    target.ColorBrush = BuildTaskCategoryColorBrush(projectId, name);
                }

                existingNames.Add(name);
                inserted++;
            }

            if (collection.Count == 0 || !string.IsNullOrWhiteSpace(collection[^1].Name))
                collection.Add(new EditableListItem());

            if (inserted > 0) _listsDirty = true;
            RefreshCommonButtonsState();

            MessageBox.Show($"Collage terminé.\n\nAjoutés : {inserted}\nDéjà présents : {skipped}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommonSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveAllNow();
            MessageBox.Show("Modifications enregistrées.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur enregistrement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ✅ Enregistrement global (28.07.2026, demande de Joe) : un seul bouton valide les
    // libellés + les 8 listes + les 8 valeurs par défaut en une fois. Chaque liste est
    // diffée contre son instantané "original" par IDENTITÉ (EditableListItem.OriginalName,
    // voir PopulateItems) pour distinguer un renommage d'un supprimer+ajouter.
    public void SaveAllNow()
    {
        if (!HasCurrentProject()) return;
        var projectId = RequireCurrentProjectId();

        SaveLabels(projectId);

        SaveListChanges(projectId, _reservesItems, _reservesOriginal, Db.InsertReserve, Db.RenameReserve, Db.DeleteReserve);
        SaveListChanges(projectId, _requestersItems, _requestersOriginal, Db.InsertRequester, Db.RenameRequester, Db.DeleteRequester);
        SaveListChanges(projectId, _companiesItems, _companiesOriginal, Db.InsertCompany, Db.RenameCompany, Db.DeleteCompany);
        SaveListChanges(projectId, _placesItems, _placesOriginal, Db.InsertPlace, Db.RenamePlace, Db.DeletePlace);
        SaveListChanges(projectId, _etagesItems, _etagesOriginal, Db.InsertEtage, Db.RenameEtage, Db.DeleteEtage);
        SaveListChanges(projectId, _planningTextZonesItems, _planningTextZonesOriginal, Db.InsertPlanningTextZone, Db.RenamePlanningTextZone, Db.DeletePlanningTextZone);
        SaveListChanges(projectId, _taskCategoriesItems, _taskCategoriesOriginal, Db.InsertTaskCategory, Db.RenameTaskCategory, Db.DeleteTaskCategory);
        SaveListChanges(projectId, _taskUrgenciesItems, _taskUrgenciesOriginal, Db.InsertTaskUrgency, Db.RenameTaskUrgency, Db.DeleteTaskUrgency);
        SaveListChanges(projectId, _signatoryNamesItems, _signatoryNamesOriginal, Db.InsertSignatoryName, Db.RenameSignatoryName, Db.DeleteSignatoryName);

        Db.SetDefaultReserve(projectId, DefaultReserveComboBox.Text ?? "");
        Db.SetDefaultRequester(projectId, DefaultRequesterComboBox.Text ?? "");
        Db.SetDefaultCompany(projectId, DefaultCompanyComboBox.Text ?? "");
        Db.SetDefaultPlace(projectId, DefaultPlaceComboBox.Text ?? "");
        Db.SetDefaultEtage(projectId, DefaultEtageComboBox.Text ?? "");
        Db.SetDefaultPlanningTextZone(projectId, DefaultPlanningTextZoneComboBox.Text ?? "");
        Db.SetDefaultTaskCategory(projectId, DefaultTaskCategoryComboBox.Text ?? "");
        Db.SetDefaultTaskUrgency(projectId, DefaultTaskUrgencyComboBox.Text ?? "");
        Db.SetDefaultSignatoryName(projectId, DefaultSignatoryNameComboBox.Text ?? "");

        _listsDirty = false;
        Reload();

        try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
    }

    private static void SaveListChanges(
        long projectId,
        ObservableCollection<EditableListItem> collection,
        List<string> originalSnapshot,
        Action<long, string> insert,
        Action<long, string, string> rename,
        Action<long, string> delete)
    {
        var accountedOriginals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in collection.ToList())
        {
            var name = (item.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (item.OriginalName == null)
            {
                insert(projectId, name);
            }
            else
            {
                accountedOriginals.Add(item.OriginalName);
                if (!string.Equals(item.OriginalName, name, StringComparison.Ordinal))
                    rename(projectId, item.OriginalName, name);
            }
        }

        foreach (var original in originalSnapshot)
            if (!accountedOriginals.Contains(original))
                delete(projectId, original);
    }

    // =========================
    // Libellés (voir SaveAllNow pour l'enregistrement global)
    // =========================
    private void SaveLabels(long projectId)
    {
        Db.SetLabelReserve(projectId, (LabelReserveTextBox.Text ?? "").Trim());
        Db.SetLabelRequestedBy(projectId, (LabelRequestedByTextBox.Text ?? "").Trim());
        Db.SetLabelPerformedBy(projectId, (LabelPerformedByTextBox.Text ?? "").Trim());
        Db.SetLabelPlace(projectId, (LabelPlaceTextBox.Text ?? "").Trim());
        Db.SetLabelEtage(projectId, (LabelEtageTextBox.Text ?? "").Trim());
        Db.SetLabelSignatoryName(projectId, (LabelSignatoryNameTextBox.Text ?? "").Trim());
        Db.SetLabelPlanningTextZone(projectId, (LabelPlanningTextZoneTextBox.Text ?? "").Trim());
        Db.SetLabelTaskCategory(projectId, (LabelTaskCategoryTextBox.Text ?? "").Trim());
        Db.SetLabelTaskUrgency(projectId, (LabelTaskUrgencyTextBox.Text ?? "").Trim());
        _labelsDirty = false;
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

            // ✅ Édition directe (28.07.2026) : plus de Reload() ici -- ça effacerait les
            // modifications de texte en cours dans les autres listes. On met juste à jour la
            // pastille de la ligne concernée directement en mémoire.
            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildCompanyColorBrush(projectId, companyName);

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

            // ✅ Édition directe (28.07.2026) : plus de Reload() -- met juste à jour la
            // pastille de la ligne concernée en mémoire (voir CompanyGradientCheckBox_Changed).
            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildCompanyColorBrush(projectId, companyName);
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

            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildCompanyColorBrush(projectId, companyName);
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

            // ✅ Édition directe (28.07.2026) : plus de Reload(), voir CompanyGradientCheckBox_Changed.
            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildTaskCategoryColorBrush(projectId, categoryName);

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

            // ✅ Édition directe (28.07.2026) : plus de Reload(), voir PickCompanyColor_Click.
            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildTaskCategoryColorBrush(projectId, categoryName);
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

            if (_activeRowItem != null) _activeRowItem.ColorBrush = BuildTaskCategoryColorBrush(projectId, categoryName);
            SetSelectedTaskCategoryColorPreview(null, false);

            try { ((MainWindow)System.Windows.Application.Current.MainWindow).RefreshPlanning(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur couleur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}