// File: Pages/PlanningPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml;
using Microsoft.VisualBasic;
using Iziregi.Test.Data;
using Iziregi.Test.Services;

namespace Iziregi.Test.Pages;

// ✅ Alias WPF (évite ambiguïtés avec System.Drawing / WinForms)
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;

using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

// ✅ Aliases Media (évite ambiguïtés avec System.Drawing)
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFonts = System.Windows.Media.Fonts;
using WpfTextDecorations = System.Windows.TextDecorations;
using WpfTextDecorationCollection = System.Windows.TextDecorationCollection;

// ✅ IMPORTANT : évite Brushes/Color/ColorConverter ambigus avec System.Drawing.*
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColorConverter = System.Windows.Media.ColorConverter;

public partial class PlanningPage : WpfUserControl, IReloadablePage, INotifyPropertyChanged
{
    private const int DEFAULT_TASK_ROWS = 5;
    private const int DEFAULT_PLANNING_ROWS = 5;

    private readonly MainWindow _main;

    public List<string> Companies { get; private set; } = new();
    public List<string> Buildings { get; private set; } = new();
    public List<string> Floors { get; private set; } = new();
    public List<string> PlanningTextZones { get; private set; } = new();
    public List<string> Reserves { get; private set; } = new();
    public List<string> TaskCategories { get; private set; } = new();
    public List<string> TaskUrgencies { get; private set; } = new();

    // ✅ Libellés (page Listes) affichés comme en-têtes des colonnes "Cat."/"Urg."
    public string TaskCategoryLabel { get; private set; } = "Cat.";
    public string TaskUrgencyLabel { get; private set; } = "Urg.";
    public string CompanyLabel { get; private set; } = "Entreprise";
    public string BuildingLabel { get; private set; } = "Bâtiment";
    public string FloorLabel { get; private set; } = "Étage";

    // ✅ Même liste que PlanningTextZones, avec une entrée vide en tête pour permettre
    // de ne sélectionner aucun titre de zone de texte. Utilisée uniquement pour les
    // ComboBox de titre des zones de texte (pas pour la colonne "Cat." des tâches).
    public List<string> PlanningTextZonesWithBlank
        => new List<string> { "" }.Concat(PlanningTextZones).ToList();

    // ✅ Listes "Cat." et "Urg." des tâches (gérées page Listes), avec une entrée vide
    // en tête pour permettre de ne rien sélectionner.
    public List<string> TaskCategoriesWithBlank
        => new List<string> { "" }.Concat(TaskCategories).ToList();

    public List<string> TaskUrgenciesWithBlank
        => new List<string> { "" }.Concat(TaskUrgencies).ToList();

    // ✅ Couleurs entreprise (par projet)
    public Dictionary<string, string> CompanyColorMap { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<TaskRow> _taskRows = new();
    private readonly ObservableCollection<PlanningRow> _planningRows = new();

    // ✅ Vue filtrée de _taskRows (28.07.2026, demande de Joe) : une tâche "Effectué" ne se
    // reporte plus dans les semaines SUIVANT celle où elle a été cochée (voir
    // TaskRowVisibleInCurrentWeek). _taskRows reste la source de vérité complète (ajout/
    // suppression/édition inchangés) ; seul l'affichage de TasksDataGrid passe par cette vue.
    private ICollectionView? _taskRowsView;

    // ✅ jour de départ configurable (par l’utilisateur)
    private DayOfWeek _weekStartDay = DayOfWeek.Monday;

    // ✅ Structure de la semaine : 5 ou 6 jours de base, + Samedi/Dimanche optionnels
    // (19.07.2026, demande de Joe) — "6 à 8 jours" (actuel) ou "5 à 7 jours". Persisté par
    // projet (Db.GetPlanningWeekDayCount/SetPlanningWeekDayCount) -- défaut 5 pour un projet
    // jamais configuré (livraison aux nouveaux utilisateurs), puis mémorisé selon le choix
    // fait. La valeur ici n'est qu'un repli avant le premier Reload().
    private int _weekDayCount = 5;

    private DateTime _startDay = DateTime.Today;
    private bool _isSyncingDates = false;
    private bool _isWeekAnimating = false;

    // Drag paint done
    private bool _isPaintingDone = false;
    private bool _paintDoneValue = false;

    // ✅ Guard anti-crash: la page peut être déchargée quand on change d’onglet/page
    private bool _isPageActive = true;

    // ============================================================
    // Plan (plusieurs images) + stickers
    // ============================================================

    // Banque stickers
    public ObservableCollection<StickerItem> Stickers { get; } = new();

    // Stickers posés sur le plan (copies)
    public ObservableCollection<PlacedStickerItem> PlacedStickers { get; } = new();

    // ✅ Images posées sur le plan (plusieurs)
    public ObservableCollection<PlacedPlanImageItem> PlacedPlanImages { get; } = new();

    // ✅ Images favorites (19.07.2026, demande de Joe) : banque persistée par projet,
    // pour replacer rapidement une image récurrente sans repasser par l'explorateur.
    public ObservableCollection<ImageFavoriteItem> ImageFavorites { get; } = new();

    private const double StickerDropDefaultSize = 26;
    // ✅ Stickers rectangulaires (au lieu de ronds) pour accueillir jusqu'à 4 chiffres
    // (numérotation de tâche annuelle, voir LoadProjectTasks/SaveProjectTasks).
    private const double StickerDropDefaultWidth = 42;
    private const double PlanImageMinSize = 60;

    // ✅ sticker sélectionné dans la banque (pour appliquer couleur entreprise)
    private StickerItem? _selectedBankSticker;

    // ============================================================
    // ✅ Stickers bank persistence (JSON) — simple, no DB changes
    // ============================================================
    private bool _isLoadingStickerBank = false;

    private sealed class StickerBankState
    {
        public int Version { get; set; } = 1;
        public List<StickerState> Stickers { get; set; } = new();
    }

    // Force refresh des couleurs et listes même si la page n'est pas active
    public void RefreshCompanyColors()
    {
        try
        {
            var prev = _isPageActive;
            _isPageActive = true;
            LoadLists();
            OnPropertyChanged(nameof(CompanyColorMap));
            SyncCompanyColorStickers();
            // Mise à jour UI
            try { this.Dispatcher?.Invoke(() => { this.UpdateLayout(); }); } catch { }
            _isPageActive = prev;
        }
        catch
        {
            // non bloquant
        }
    }

    // ✅ 25.07.2026, demande de Joe : chaque intervenant coloré devient automatiquement
    // disponible dans la banque de stickers (même couleur, même dégradé "Mix" si actif),
    // sans étape manuelle. Les stickers n'ont PAS vocation à porter un nom -- leur étiquette
    // reste un numéro libre saisi par Joe (ex: numérotation de tâche) -- donc l'association
    // sticker <-> intervenant se fait via un champ interne invisible (StickerItem.CompanyName),
    // jamais via le Label. Un intervenant déjà lié à un sticker voit juste sa couleur mise à
    // jour ; sinon le premier emplacement NON lié (CompanyName vide) est utilisé, sans toucher
    // à son étiquette existante.
    private void SyncCompanyColorStickers()
    {
        try
        {
            var pid = Db.GetCurrentProjectId();
            if (!pid.HasValue || pid.Value <= 0) return;

            var colorMap = Db.GetCompanyColorMap(pid.Value);
            if (colorMap.Count == 0) return;

            var gradientMap = Db.GetCompanyGradientMap(pid.Value);
            var wasLoading = _isLoadingStickerBank;
            _isLoadingStickerBank = true; // évite une sauvegarde par sticker modifié ci-dessous

            var changed = false;

            // ✅ Nettoyage ponctuel (25.07.2026) : une version précédente de cette fonction,
            // dans la même session, avait écrit le nom (en lettres) directement dans le Label
            // -- corrigé depuis (lien via CompanyName, Label jamais touché), mais les anciennes
            // étiquettes-lettres laissées par ce bug doivent être effacées. Un vrai numéro de
            // tâche saisi par Joe est toujours numérique, donc sans risque de confusion.
            foreach (var s in Stickers)
            {
                if (!string.IsNullOrEmpty(s.Label) && s.Label.All(char.IsLetter))
                {
                    s.Label = "";
                    changed = true;
                }
            }

            foreach (var kv in colorMap)
            {
                var companyName = kv.Key;
                var hex = kv.Value;
                if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(hex))
                    continue;

                var isGradient = gradientMap.Contains(companyName);

                var existing = Stickers.FirstOrDefault(s => string.Equals(s.CompanyName, companyName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!string.Equals(existing.ColorHex, hex, StringComparison.OrdinalIgnoreCase) || existing.IsGradient != isGradient)
                    {
                        existing.ColorHex = hex;
                        existing.IsGradient = isGradient;
                        changed = true;
                    }
                    continue;
                }

                var freeSlot = Stickers.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.CompanyName));
                if (freeSlot != null)
                {
                    freeSlot.CompanyName = companyName;
                    freeSlot.ColorHex = hex;
                    freeSlot.IsGradient = isGradient;
                    changed = true;
                }
                // Sinon : banque pleine (20/20), on ne remplace pas un sticker existant.
            }

            _isLoadingStickerBank = wasLoading;
            if (changed) SaveStickerBankToDisk();
        }
        catch
        {
            // non bloquant
        }
    }

    // ✅ Voir le champ _taskRowsView : filtre les tâches "Effectué" hors des semaines qui
    // suivent celle où elles ont été terminées (28.07.2026, demande de Joe). Reconstruit la
    // vue à chaque appel plutôt que de réutiliser une instance mise en cache : plus robuste
    // face aux détachements/réattachements répétés d'ItemsSource déjà pratiqués ailleurs sur
    // cette page (RecreateDataGridItemsSources).
    private void AttachTaskRowsView()
    {
        // ✅ PAS de IsLiveFiltering (28.07.2026) : ça re-filtre/revirtualise la grille EN
        // PLEIN MILIEU d'une interaction utilisateur dès qu'une propriété suivie change
        // (ex. cocher "Effectué"), ce qui pouvait décaler les conteneurs de lignes recyclés
        // par le DataGrid et faire "sauter" la case à cocher de sélection d'une autre ligne
        // (bug signalé par Joe : "je ne peux cliquer que dans certaines cases"). Le filtre
        // n'est donc réévalué qu'à des moments précis et maîtrisés : changement de semaine
        // (ApplyPlanningHeadersAndSyncDatePickers) et coche "Effectué" (TaskDoneCheckBox_Click).
        var view = CollectionViewSource.GetDefaultView(_taskRows);
        view.Filter = TaskRowVisibleInCurrentWeek;

        _taskRowsView = view;
        TasksDataGrid.ItemsSource = view;
    }

    private bool TaskRowVisibleInCurrentWeek(object obj)
    {
        if (obj is not TaskRow r) return true;

        var viewedWeekStart = SnapToStartOfWeek(_startDay, _weekStartDay);

        // ✅ Borne basse (28.07.2026, demande de Joe) : pas de report dans les semaines
        // PRÉCÉDANT celle où la tâche a été créée. Null (tâches créées avant cet ajout) ->
        // pas de borne basse, comportement historique conservé.
        if (r.CreatedWeekStart.HasValue && viewedWeekStart < SnapToStartOfWeek(r.CreatedWeekStart.Value, _weekStartDay))
            return false;

        // ✅ Borne haute : pas de report dans les semaines SUIVANT celle où "Effectué" a été
        // coché.
        if (r.Done && r.DoneAt.HasValue)
        {
            var doneWeekStart = SnapToStartOfWeek(r.DoneAt.Value, _weekStartDay);

            // ✅ Garde-fou (28.07.2026, résidu de bug constaté : "invisible dans toutes les
            // semaines") : DoneAt ne peut pas être antérieur à CreatedWeekStart (une tâche ne
            // peut pas être terminée avant d'avoir été créée) -- si des données anciennes/
            // incohérentes existent malgré tout, on ne laisse jamais la borne haute passer
            // sous la borne basse, ce qui rendrait la tâche mathématiquement invisible
            // partout.
            if (r.CreatedWeekStart.HasValue)
            {
                var createdWeekStart = SnapToStartOfWeek(r.CreatedWeekStart.Value, _weekStartDay);
                if (doneWeekStart < createdWeekStart) doneWeekStart = createdWeekStart;
            }

            if (viewedWeekStart > doneWeekStart) return false;
        }

        return true;
    }

    // ✅ Bascule "Afficher toutes les semaines" (28.07.2026, demande de Joe) : désactive
    // temporairement le filtre par semaine pour voir/gérer absolument toutes les tâches,
    // y compris celles devenues invisibles partout à cause d'un bug de date déjà corrigé.
    private void ShowAllWeeksTasksCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_taskRowsView == null) return;

        _taskRowsView.Filter = ShowAllWeeksTasksCheckBox.IsChecked == true
            ? null
            : TaskRowVisibleInCurrentWeek;
    }

    // ✅ Recalcule la visibilité "Effectué" au moment précis du clic plutôt que via un
    // rafraîchissement automatique (IsLiveFiltering) qui perturbait d'autres cases de la
    // grille en cours d'interaction (voir commentaire dans AttachTaskRowsView). Différé via
    // Dispatcher pour laisser le binding TwoWay de la case terminer sa mise à jour d'abord.
    private void TaskDoneCheckBox_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { _taskRowsView?.Refresh(); } catch { }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    // Force re-création des ItemsSource des DataGrids pour reconstruire les cellules
    public void RecreateDataGridItemsSources()
    {
        try
        {
            // detach and reattach to force regeneration
            var tasks = _taskRows.ToList();
            var planning = _planningRows.ToList();

            TasksDataGrid.ItemsSource = null;
            PlanningDataGrid.ItemsSource = null;

            // petite pause d'UI pour s'assurer que WPF détruit les containers
            try { System.Threading.Thread.Sleep(10); } catch { }

            AttachTaskRowsView();
            PlanningDataGrid.ItemsSource = _planningRows;

            TasksDataGrid.Dispatcher?.Invoke(() => { TasksDataGrid.UpdateLayout(); });
            PlanningDataGrid.Dispatcher?.Invoke(() => { PlanningDataGrid.UpdateLayout(); });
        }
        catch
        {
            // non bloquant
        }
    }

    private sealed class StickerState
    {
        public string Label { get; set; } = "";
        public string ColorHex { get; set; } = "#F59E0B";
        public bool IsGradient { get; set; } = false;
        // ✅ Lien invisible sticker <-> intervenant (25.07.2026), jamais affiché : le Label
        // reste un numéro libre saisi par Joe.
        public string CompanyName { get; set; } = "";
    }

    private static string GetStickerBankFilePath(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture)
            : "0";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi",
            "Planning");

        return Path.Combine(dir, $"planning-stickers-{pid}.json");
    }

    private void AttachStickerAutoSaveHandlers()
    {
        foreach (var s in Stickers)
        {
            s.PropertyChanged -= StickerItem_PropertyChanged_Save;
            s.PropertyChanged += StickerItem_PropertyChanged_Save;
        }
    }

    private void StickerItem_PropertyChanged_Save(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingStickerBank) return;
        if (!_isPageActive) return;

        if (e.PropertyName == nameof(StickerItem.ColorHex) || e.PropertyName == nameof(StickerItem.Label) || e.PropertyName == nameof(StickerItem.IsGradient))
        {
            SaveStickerBankToDisk();
        }
    }

    private void EnsureStickerBankLoadedOrInitialized()
    {
        _isLoadingStickerBank = true;
        try
        {
            var pid = Db.GetCurrentProjectId();
            var filePath = GetStickerBankFilePath(pid);

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<StickerBankState>(json);

                if (state?.Stickers != null && state.Stickers.Count > 0)
                {
                    Stickers.Clear();

                    foreach (var it in state.Stickers)
                    {
                        Stickers.Add(new StickerItem
                        {
                            Label = it?.Label ?? "",
                            ColorHex = string.IsNullOrWhiteSpace(it?.ColorHex) ? "#F59E0B" : it!.ColorHex,
                            IsGradient = it?.IsGradient ?? false,
                            CompanyName = it?.CompanyName ?? ""
                        });
                    }

                    // Sécurité : compléter / tronquer à 20
                    while (Stickers.Count < 20)
                        Stickers.Add(new StickerItem { Label = "", ColorHex = StickerPalette[Stickers.Count % StickerPalette.Length] });

                    while (Stickers.Count > 20)
                        Stickers.RemoveAt(Stickers.Count - 1);

                    return;
                }
            }

            // Sinon : init par défaut + sauvegarde
            Stickers.Clear();
            for (int i = 0; i < 20; i++)
                Stickers.Add(new StickerItem { Label = "", ColorHex = StickerPalette[i % StickerPalette.Length] });

            SaveStickerBankToDisk();
        }
        catch
        {
            // Fallback sans crash
            if (Stickers.Count == 0)
            {
                for (int i = 0; i < 20; i++)
                    Stickers.Add(new StickerItem { Label = "", ColorHex = StickerPalette[i % StickerPalette.Length] });
            }
        }
        finally
        {
            _isLoadingStickerBank = false;
            AttachStickerAutoSaveHandlers();
        }
    }

    private void SaveStickerBankToDisk()
    {
        try
        {
            var pid = Db.GetCurrentProjectId();
            var filePath = GetStickerBankFilePath(pid);

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var state = new StickerBankState
            {
                Version = 1,
                Stickers = Stickers.Select(s => new StickerState
                {
                    Label = s?.Label ?? "",
                    ColorHex = string.IsNullOrWhiteSpace(s?.ColorHex) ? "#F59E0B" : s!.ColorHex,
                    IsGradient = s?.IsGradient ?? false,
                    CompanyName = s?.CompanyName ?? ""
                }).ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // non bloquant
        }
    }

    // ============================================================
    // ✅ Images favorites persistence (JSON) — même mécanisme que la banque stickers,
    // mais les fichiers favoris sont copiés dans un sous-dossier "Favoris" séparé du
    // dossier d'images normal, pour ne jamais être perdus quand une image est retirée
    // du plan d'une semaine (Retirer image ne supprime que l'entrée PlacedPlanImages,
    // jamais le fichier disque).
    // ============================================================
    private sealed class ImageFavoriteState
    {
        public string FilePath { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class ImageFavoritesBankState
    {
        public int Version { get; set; } = 1;
        public List<ImageFavoriteState> Favorites { get; set; } = new();
    }

    private static string GetImageFavoritesDir(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture) : "0";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning", "Images", pid, "Favoris");
    }

    private static string GetImageFavoritesFilePath(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture) : "0";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi",
            "Planning");

        return Path.Combine(dir, $"planning-image-favorites-{pid}.json");
    }

    private void EnsureImageFavoritesLoaded()
    {
        try
        {
            ImageFavorites.Clear();

            var pid = Db.GetCurrentProjectId();
            var filePath = GetImageFavoritesFilePath(pid);
            if (!File.Exists(filePath)) return;

            var json = File.ReadAllText(filePath);
            var state = JsonSerializer.Deserialize<ImageFavoritesBankState>(json);
            if (state?.Favorites == null) return;

            foreach (var f in state.Favorites)
            {
                var path = (f?.FilePath ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    ImageFavorites.Add(new ImageFavoriteItem { FilePath = path, ImageSource = bmp, Name = f?.Name ?? "" });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            LogPlanningError("EnsureImageFavoritesLoaded", ex);
        }
    }

    private void SaveImageFavoritesToDisk()
    {
        try
        {
            var pid = Db.GetCurrentProjectId();
            var filePath = GetImageFavoritesFilePath(pid);

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var state = new ImageFavoritesBankState
            {
                Version = 1,
                Favorites = ImageFavorites.Select(f => new ImageFavoriteState { FilePath = f?.FilePath ?? "", Name = f?.Name ?? "" }).ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            LogPlanningError("SaveImageFavoritesToDisk", ex);
        }
    }

    // ✅ Capture une sous-région du plan (image + tout ce qui la superpose visuellement,
    // notamment les stickers) en un seul bitmap figé. "Solution aplatir" retenue le
    // 19.07.2026 (demande de Joe) car les stickers ne sont pas rattachés à une image en
    // mémoire — pas de vrai lien à exploiter, donc on fige plutôt ce qui est affiché.
    private static BitmapSource? CaptureCanvasRegionToBitmap(FrameworkElement canvas, Rect regionInCanvas)
    {
        if (canvas == null) return null;

        canvas.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(canvas);
        int fullW = (int)Math.Ceiling(canvas.ActualWidth * dpi.DpiScaleX);
        int fullH = (int)Math.Ceiling(canvas.ActualHeight * dpi.DpiScaleY);
        if (fullW <= 0 || fullH <= 0) return null;

        var rtb = new RenderTargetBitmap(fullW, fullH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        int x = (int)Math.Max(0, Math.Round(regionInCanvas.X * dpi.DpiScaleX));
        int y = (int)Math.Max(0, Math.Round(regionInCanvas.Y * dpi.DpiScaleY));
        int w = (int)Math.Max(1, Math.Round(regionInCanvas.Width * dpi.DpiScaleX));
        int h = (int)Math.Max(1, Math.Round(regionInCanvas.Height * dpi.DpiScaleY));

        if (x >= fullW || y >= fullH) return null;
        if (x + w > fullW) w = fullW - x;
        if (y + h > fullH) h = fullH - y;
        if (w <= 0 || h <= 0) return null;

        var cropped = new CroppedBitmap(rtb, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        return cropped;
    }

    // ✅ Ajoute l'image actuellement sélectionnée aux favoris, aplatie avec les stickers
    // qui la superposent visuellement (voir CaptureCanvasRegionToBitmap).
    private void AddSelectedImageToFavorites_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selectedPlacedPlanImage == null)
            {
                System.Windows.MessageBox.Show(
                    "Sélectionne d'abord une image sur le plan (clique dessus), puis réessaie.",
                    "Ajouter aux favoris", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var it = _selectedPlacedPlanImage;

            // ✅ Cache le contour bleu de sélection le temps de la capture, sinon il se
            // retrouve figé dans le favori.
            bool wasSelected = it.IsSelected;
            it.IsSelected = false;
            PlanCanvas.UpdateLayout();

            BitmapSource? captured;
            try
            {
                captured = CaptureCanvasRegionToBitmap(PlanCanvas, new Rect(it.X, it.Y, it.Width, it.Height));
            }
            finally
            {
                it.IsSelected = wasSelected;
            }

            if (captured == null) return;

            var favDir = GetImageFavoritesDir(Db.GetCurrentProjectId());
            Directory.CreateDirectory(favDir);
            var destPath = Path.Combine(favDir, $"{Guid.NewGuid():N}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(captured));
            using (var fs = new FileStream(destPath, FileMode.Create))
                encoder.Save(fs);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(destPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var newFavorite = new ImageFavoriteItem { FilePath = destPath, ImageSource = bmp };
            ImageFavorites.Add(newFavorite);
            SaveImageFavoritesToDisk();

            // ✅ Nom demandé directement à l'ajout (28.07.2026, demande de Joe), plutôt
            // qu'uniquement via le bouton "✎" après coup — remplace l'ancien popup de
            // simple confirmation (19.07.2026), cette boîte de dialogue sert déjà de
            // confirmation visuelle de l'ajout.
            var name = Interaction.InputBox("Nom de l'image (facultatif) :", "Ajouter aux favoris", "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                TryApplyFavoriteName(newFavorite, name);
        }
        catch (Exception ex)
        {
            LogPlanningError("AddSelectedImageToFavorites_Click", ex);
        }
    }

    private void ImageFavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ImageFavoritesPopup != null)
            ImageFavoritesPopup.IsOpen = !ImageFavoritesPopup.IsOpen;
    }

    // ✅ Clic sur une vignette favorite : ajoute cette image (déjà stockée de façon
    // permanente) directement sur le plan, sans repasser par une nouvelle copie.
    private void ImageFavoriteThumbnail_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ImageFavoriteItem fav)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fav.FilePath) || !File.Exists(fav.FilePath)) return;

                var it = new PlacedPlanImageItem
                {
                    FilePath = fav.FilePath,
                    ImageSource = fav.ImageSource,
                    X = 20 + (PlacedPlanImages.Count * 20),
                    Y = 20 + (PlacedPlanImages.Count * 20),
                    Width = 360,
                    Height = 240
                };

                PlacedPlanImages.Add(it);
                SelectPlacedPlanImage(it);
            }
            catch (Exception ex)
            {
                LogPlanningError("ImageFavoriteThumbnail_MouseLeftButtonUp", ex);
            }
        }

        if (ImageFavoritesPopup != null)
            ImageFavoritesPopup.IsOpen = false;
    }

    private void ImageFavoriteRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ImageFavoriteItem fav)
        {
            ImageFavorites.Remove(fav);
            SaveImageFavoritesToDisk();
        }
    }

    // ✅ Nommer les favoris (28.07.2026, demande de Joe) : pour mieux les distinguer dans le
    // popup une fois réduits en vignette. Même InputBox que "Renommer" dans ListsPage.xaml.cs.
    private void ImageFavoriteRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ImageFavoriteItem fav) return;

        var newName = Interaction.InputBox("Nom de l'image :", "Renommer", fav.Name).Trim();
        if (string.IsNullOrWhiteSpace(newName)) return;

        TryApplyFavoriteName(fav, newName);
    }

    // ✅ Comme l'enregistrement d'un fichier (28.07.2026, demande de Joe) : si un autre
    // favori porte déjà ce nom, prévient et demande confirmation avant de le remplacer
    // (l'ancien favori est retiré de la liste, mais son fichier reste sur le disque, comme
    // pour "Retirer des favoris" — voir ImageFavoriteRemove_Click).
    private bool TryApplyFavoriteName(ImageFavoriteItem target, string name)
    {
        var conflict = ImageFavorites.FirstOrDefault(f =>
            !ReferenceEquals(f, target) &&
            !string.IsNullOrWhiteSpace(f.Name) &&
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (conflict != null)
        {
            var result = System.Windows.MessageBox.Show(
                $"Ce nom existe déjà ({name}). Voulez-vous le remplacer ?",
                "Favoris", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return false;

            ImageFavorites.Remove(conflict);
        }

        target.Name = name;
        SaveImageFavoritesToDisk();
        return true;
    }

    // ============================================================
    // ✅ Text zones persistence (JSON) — même mécanisme que la banque stickers.
    // Avant ce correctif, les 4 zones de texte riche n'étaient jamais persistées
    // nulle part : leur contenu se perdait à chaque changement de page ou
    // redémarrage de l'appli (PlanningPage est recréée à chaque navigation).
    // ============================================================
    private bool _isLoadingTextZones = false;

    // ✅ Diagnostic : les catch { } de cette zone étaient jusqu'ici silencieux, ce qui a
    // rendu la panne de sauvegarde invisible lors du premier test. On journalise désormais
    // toute exception rencontrée pendant la sérialisation/sauvegarde/chargement.
    private static void LogPlanningError(string context, Exception ex) => LogPlanningTrace(context, ex.ToString());

    private static void LogPlanningTrace(string context, string detail)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "Iziregi_planning_errors.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O}  {context} -> {detail}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    private sealed class TextZoneState
    {
        public bool Visible { get; set; }
        public string Title { get; set; } = "";
        public string DocumentXaml { get; set; } = "";
    }

    // ✅ Compat migration (19.07.2026) : avant ce correctif, les 4 zones de texte étaient
    // partagées pour tout le projet (fichier planning-textzones-{pid}.json). Une semaine
    // qui n'a pas encore sa propre sauvegarde per-semaine (TextZoneStates absent du fichier
    // de semaine) reprend une fois le contenu de cet ancien fichier, pour ne pas perdre ce
    // qui avait déjà été saisi. Dès que la semaine est modifiée/sauvegardée, elle a son
    // propre contenu et n'utilise plus ce fallback.
    private sealed class LegacyProjectTextZoneBankState
    {
        public int Version { get; set; } = 1;
        public List<TextZoneState> Zones { get; set; } = new();
    }

    private static List<TextZoneState>? TryReadLegacyProjectTextZones(long? projectId)
    {
        try
        {
            var pid = (projectId.HasValue && projectId.Value > 0)
                ? projectId.Value.ToString(CultureInfo.InvariantCulture) : "0";
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Iziregi", "Planning", $"planning-textzones-{pid}.json");

            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var state = JsonSerializer.Deserialize<LegacyProjectTextZoneBankState>(json);
            return (state?.Zones != null && state.Zones.Count > 0) ? state.Zones : null;
        }
        catch { return null; }
    }

    private void AttachTextZoneAutoSaveHandlers()
    {
        foreach (var z in TextZones)
        {
            z.PropertyChanged -= TextZoneItem_PropertyChanged_Save;
            z.PropertyChanged += TextZoneItem_PropertyChanged_Save;
        }
    }

    private void TextZoneItem_PropertyChanged_Save(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingTextZones) return;
        if (!_isPageActive) return;

        // ✅ Zones de texte indépendantes PAR SEMAINE (19.07.2026, demande de Joe — avant ce
        // correctif elles étaient partagées par tout le projet, donc taper dans une zone
        // modifiait la même zone sur toutes les semaines). On sauvegarde dans le fichier de
        // la semaine affichée, à chaque frappe comme avant.
        SaveCurrentWeekState();
    }

    // ✅ Recharge les 4 zones de texte pour la semaine affichée (appelé depuis
    // LoadWeekState). `states` vient du fichier de la semaine (null si semaine jamais
    // sauvegardée -> défaut : 2 premières zones visibles, vides).
    private void LoadTextZonesForWeek(List<TextZoneState>? states)
    {
        _isLoadingTextZones = true;
        try
        {
            TextZones.Clear();

            if (states != null && states.Count > 0)
            {
                foreach (var z in states)
                    TextZones.Add(new TextZoneItem
                    {
                        Visible = z?.Visible ?? false,
                        Title = z?.Title ?? "",
                        DocumentXaml = z?.DocumentXaml ?? ""
                    });
            }
            else
            {
                TextZones.Add(new TextZoneItem { Visible = true });
                TextZones.Add(new TextZoneItem { Visible = true });
                TextZones.Add(new TextZoneItem { Visible = false });
                TextZones.Add(new TextZoneItem { Visible = false });
            }

            // Sécurité : toujours exactement 4 zones (Rtb0..Rtb3)
            while (TextZones.Count < 4)
                TextZones.Add(new TextZoneItem { Visible = false });
            while (TextZones.Count > 4)
                TextZones.RemoveAt(TextZones.Count - 1);

            RestoreZoneXamlToRtb(0, Rtb0);
            RestoreZoneXamlToRtb(1, Rtb1);
            RestoreZoneXamlToRtb(2, Rtb2);
            RestoreZoneXamlToRtb(3, Rtb3);

            SelectedTextZoneIndex = FindFirstVisibleTextZoneIndex();
        }
        catch (Exception ex)
        {
            LogPlanningError("LoadTextZonesForWeek", ex);
            if (TextZones.Count == 0)
            {
                TextZones.Add(new TextZoneItem { Visible = true });
                TextZones.Add(new TextZoneItem { Visible = true });
                TextZones.Add(new TextZoneItem { Visible = false });
                TextZones.Add(new TextZoneItem { Visible = false });
            }
        }
        finally
        {
            _isLoadingTextZones = false;
            AttachTextZoneAutoSaveHandlers();
        }
    }

    // Drag depuis banque stickers
    private bool _bankIsDragging = false;
    private WpfPoint _bankDragStart;
    private StickerItem? _bankDragSticker;

    // Drag sticker posé
    private bool _placedIsDragging = false;
    private PlacedStickerItem? _placedDraggingSticker;
    private WpfPoint _placedDragStartPointOnCanvas;
    private double _placedDragStartX;
    private double _placedDragStartY;

    // Drag image posée
    private bool _placedPlanImageIsDragging = false;
    private PlacedPlanImageItem? _placedPlanImageDraggingItem;
    private WpfPoint _placedPlanImageDragStartPointOnCanvas;
    private double _placedPlanImageDragStartX;
    private double _placedPlanImageDragStartY;

    // ============================================================
    // ✅ Reset stickers posés
    // ============================================================
    private void ResetPlacedStickers_Click(object sender, RoutedEventArgs e)
    {
        try { PlacedStickers.Clear(); } catch { }
    }

    // ============================================================
    // ✅ Export PDF planning (CAPTURE PNG -> PDF)
    // ============================================================

    // ✅ Permet d'appeler l'export PDF depuis MainWindow (menu "Planning > Export PDF"),
    // qui n'a pas accès aux contrôles internes de la page. Auparavant ce menu affichait
    // un message "TODO" alors que cet export fonctionnait déjà depuis le bouton de la page.
    public void ExportPdf()
    {
        ExportPdfButton_Click(this, new RoutedEventArgs());
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Enregistrer le PDF (planning)",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"PLANNING-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true
            };

            if (dlg.ShowDialog() != true)
                return;

            // ✅ Masque UI inutile le temps de la capture
            var restore = new List<Action>();
            try
            {
                PreparePdfCaptureUi(restore);

                var png = CaptureScrollViewerContentToPngBytes(MainScrollViewer);

                PdfService.GeneratePlanningPdfFromSections(
                    dlg.FileName,
                    new List<byte[]> { png }
                );

                // ✅ Ouvrir le PDF après export
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dlg.FileName,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch
                {
                    // Non bloquant si Windows ne peut pas ouvrir automatiquement
                }
            }
            finally
            {
                // Restaure UI
                for (int i = restore.Count - 1; i >= 0; i--)
                {
                    try { restore[i](); } catch { }
                }
            }

            System.Windows.MessageBox.Show(
                _main,
                "PDF planning généré.",
                "Planning",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                _main,
                $"Impossible de générer le PDF planning.\n\n{ex.Message}",
                "Planning",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PreparePdfCaptureUi(List<Action> restore)
    {
        // ✅ Objectif PDF :
        // - Garder les titres visuels
        // - Ne PAS afficher les boutons / UI d'action

        // ---- Section Tâches : cacher boutons Ajouter/Supprimer/Colonnes + crayons Descriptif
        HideElementForCapture(AddTaskRowButton, restore);
        HideElementForCapture(RemoveTaskRowButton, restore);
        HideElementForCapture(TaskColumnsButton, restore);

        // ✅ TaskExpandDescriptionColumn est un DataGridColumn (pas un UIElement) : sa
        // visibilité se gère différemment de HideElementForCapture (voir plus bas).
        if (TaskExpandDescriptionColumn != null)
        {
            var oldColVis = TaskExpandDescriptionColumn.Visibility;
            restore.Add(() => TaskExpandDescriptionColumn.Visibility = oldColVis);
            TaskExpandDescriptionColumn.Visibility = Visibility.Collapsed;
        }

        // ---- Section Planning hebdomadaire : cacher boutons Ajouter/Supprimer + samedi
        HideElementForCapture(AddPlanningRowButton, restore);
        HideElementForCapture(RemovePlanningRowButton, restore);
        HideElementForCapture(WeekStructureButton, restore);

        // ---- Section Image : cacher boutons Ajouter/Retirer/Reset
        HideElementForCapture(PlanAddButton, restore);
        HideElementForCapture(PlanRemoveButton, restore);
        HideElementForCapture(ResetStickersButton, restore);

        // ---- Zones de texte : cacher entièrement la barre d’actions + export + panneau stickers + toolbar police
        HideElementForCapture(TextZoneHeaderToolbar, restore);

        // Export button itself (inutile dans PDF)
        HideElementForCapture(ExportPdfButton, restore);

        // Sticker bank + palette (inutile dans PDF)
        HideElementForCapture(StickerBankPanel, restore);

        // Font toolbar (inutile dans PDF)
        HideElementForCapture(TextZoneFontToolbar, restore);

        // ✅ Police/hauteurs de ligne agrandies UNIQUEMENT pendant la capture PDF (18.07.2026,
        // demande de Joe) : la taille normale à l'écran (voir PlanningGridStyle) reste
        // inchangée -- ce n'est que l'image capturée pour le PDF qui doit avoir une police
        // plus lisible une fois imprimée.
        EnlargeGridForCapture(TasksDataGrid, restore, fontSize: 18, rowHeight: 34, columnHeaderHeight: 38);
        EnlargeGridForCapture(PlanningDataGrid, restore, fontSize: 18, rowHeight: 34, columnHeaderHeight: 38);

        // Appliquer immédiatement
        this.UpdateLayout();
    }

    private static void HideElementForCapture(UIElement? el, List<Action> restore)
    {
        if (el == null) return;

        var old = el.Visibility;
        restore.Add(() => el.Visibility = old);
        el.Visibility = Visibility.Collapsed;
    }

    private static void EnlargeGridForCapture(DataGrid? grid, List<Action> restore, double fontSize, double rowHeight, double columnHeaderHeight)
    {
        if (grid == null) return;

        // ✅ ClearValue (pas de réassigner l'ancienne valeur) : revient proprement à la valeur
        // du style (PlanningGridStyle) plutôt que de figer une valeur locale en dur.
        restore.Add(() =>
        {
            grid.ClearValue(DataGrid.FontSizeProperty);
            grid.ClearValue(DataGrid.RowHeightProperty);
            grid.ClearValue(DataGrid.ColumnHeaderHeightProperty);
        });

        grid.FontSize = fontSize;
        grid.RowHeight = rowHeight;
        grid.ColumnHeaderHeight = columnHeaderHeight;
    }

    private static byte[] CaptureScrollViewerContentToPngBytes(ScrollViewer sv)
    {
        if (sv == null)
            throw new ArgumentNullException(nameof(sv));

        if (sv.Content is not FrameworkElement content)
            throw new InvalidOperationException("MainScrollViewer.Content n'est pas un FrameworkElement.");

        sv.UpdateLayout();
        content.UpdateLayout();

        // ✅ IMPORTANT : capturer avec une largeur contrainte (sinon les zones de texte peuvent devenir ultra-étroites)
        double targetWidth = sv.ViewportWidth;
        if (double.IsNaN(targetWidth) || double.IsInfinity(targetWidth) || targetWidth <= 0)
            targetWidth = sv.ActualWidth;

        if (double.IsNaN(targetWidth) || double.IsInfinity(targetWidth) || targetWidth <= 0)
            targetWidth = content.ActualWidth;

        if (double.IsNaN(targetWidth) || double.IsInfinity(targetWidth) || targetWidth <= 0)
            targetWidth = 1100;

        // Sauvegarde pour restaurer
        double oldWidth = content.Width;
        double oldHeight = content.Height;

        try
        {
            // Force la largeur pendant le rendu off-screen
            content.Width = targetWidth;

            // Mesurer tout le contenu (hauteur infinie)
            content.Measure(new System.Windows.Size(targetWidth, double.PositiveInfinity));
            content.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0),
                new System.Windows.Size(targetWidth, content.DesiredSize.Height)));
            content.UpdateLayout();

            // ✅ 2e passe (demande de Joe, 16.07.2026) : la colonne étoile "Descriptif" de la
            // grille des Tâches ne recalcule sa largeur qu'après qu'ActualWidth soit mis à
            // jour par la 1ère passe — sans ce second passage, une bande vide apparaissait
            // après la dernière colonne ("Effectué") sur le PDF capturé.
            content.Measure(new System.Windows.Size(targetWidth, double.PositiveInfinity));
            content.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0),
                new System.Windows.Size(targetWidth, content.DesiredSize.Height)));
            content.UpdateLayout();

            int w = (int)Math.Ceiling(targetWidth);
            int h = (int)Math.Ceiling(content.DesiredSize.Height);

            if (w <= 0) w = 1100;
            if (h <= 0) h = 800;

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            // Restore
            content.Width = oldWidth;
            content.Height = oldHeight;
            content.UpdateLayout();
        }
    }

    // ============================================================
    // ✅ Clear sélection DataGrids quand on clique ailleurs
    // ============================================================

    private void MainScrollViewer_PreviewMouseDown_ClearSelections(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var dep = e.OriginalSource as DependencyObject;
            var grid = FindAncestor<DataGrid>(dep);

            if (ReferenceEquals(grid, TasksDataGrid))
            {
                ClearDataGridSelection(PlanningDataGrid);
            }
            else if (ReferenceEquals(grid, PlanningDataGrid))
            {
                ClearDataGridSelection(TasksDataGrid);
            }
            else
            {
                ClearDataGridSelection(TasksDataGrid);
                ClearDataGridSelection(PlanningDataGrid);
            }
        }
        catch { }
    }

    private static void ClearDataGridSelection(DataGrid? grid)
    {
        if (grid == null) return;

        try { grid.UnselectAllCells(); } catch { }
        try { grid.UnselectAll(); } catch { }

        try { grid.SelectedItem = null; } catch { }
        try { grid.SelectedIndex = -1; } catch { }

        try { grid.CurrentCell = new DataGridCellInfo(); } catch { }
    }

    // ============================================================
    // ✅ NEW: édition sticker posé sur double-clic (sinon drag)
    // ============================================================

    private void PlacedSticker_BeginEditFromDoubleClick(object sender, WpfMouseButtonEventArgs e)
    {
        try
        {
            if (!_isPageActive) return;

            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not PlacedStickerItem s) return;

            s.IsEditing = true;

            _placedIsDragging = false;
            _placedDraggingSticker = null;

            if (fe.IsMouseCaptured)
                fe.ReleaseMouseCapture();

            e.Handled = true;

            fe.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_isPageActive) return;
                    if (!fe.IsLoaded) return;

                    var tb = FindDescendant<WpfTextBox>(fe);
                    if (tb == null) return;

                    // ✅ IMPORTANT: le layout doit être OK avant Focus/Caret
                    if (!tb.IsLoaded) return;
                    if (tb.Visibility != Visibility.Visible) return;

                    // Force un layout minimal
                    try { tb.UpdateLayout(); } catch { }

                    // Le focus/caret peut lever TS_E_NOLAYOUT => on absorbe
                    try
                    {
                        tb.Focus();
                        tb.CaretIndex = tb.Text?.Length ?? 0;
                        tb.SelectAll();
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        // ignore (page change / pas de layout)
                    }
                    catch (InvalidOperationException)
                    {
                        // ignore (layout pas prêt)
                    }
                }
                catch
                {
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch { }
    }

    private void PlacedSticker_EndEditOnLostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not WpfTextBox tb) return;
            if (tb.DataContext is not PlacedStickerItem s) return;

            s.IsEditing = false;
        }
        catch { }
    }

    private void PlacedStickerTextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        try
        {
            if (sender is not WpfTextBox tb) return;
            if (tb.DataContext is not PlacedStickerItem s) return;

            if (e.Key == Key.Enter)
            {
                s.IsEditing = false;
                e.Handled = true;
                Keyboard.ClearFocus();
            }
            else if (e.Key == Key.Escape)
            {
                s.IsEditing = false;
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        }
        catch { }
    }

    private void PlacedStickerTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not WpfTextBox tb) return;
            if (tb.DataContext is not PlacedStickerItem s) return;

            if (e.ClickCount >= 2)
            {
                s.IsEditing = true;

                e.Handled = true;

                tb.Focus();
                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.SelectAll();
                return;
            }
        }
        catch { }
    }

    private void StickerBankTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not WpfTextBox tb)
                return;

            if (tb.DataContext is StickerItem s)
                _selectedBankSticker = s;

            tb.Focus();
            tb.CaretIndex = tb.Text?.Length ?? 0;
        }
        catch { }
    }

    // ============================================================
    // Zones de texte
    // ============================================================

    public ObservableCollection<TextZoneItem> TextZones { get; } = new();

    private int _selectedTextZoneIndex = -1; // 0..3
    public int SelectedTextZoneIndex
    {
        get => _selectedTextZoneIndex;
        private set => SetField(ref _selectedTextZoneIndex, value);
    }

    // ============================================================
    // RichTextBox toolbar
    // ============================================================

    private WpfRichTextBox? _activeRtb;
    private bool _rtbInternalUpdate = false;

    private readonly List<double> _fontSizes = new()
    {
        8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72
    };

    private const double RtbDefaultLineHeight = 14;
    private static readonly Thickness ParagraphZeroMargin = new Thickness(0);

    private void InitTextFormattingToolbar()
    {
        if (FontFamilyCombo != null && FontFamilyCombo.Items.Count == 0)
        {
            var fonts = WpfFonts.SystemFontFamilies
                .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FontFamilyCombo.ItemsSource = fonts;
            FontFamilyCombo.DisplayMemberPath = "Source";
        }

        if (FontSizeCombo != null && FontSizeCombo.Items.Count == 0)
            FontSizeCombo.ItemsSource = _fontSizes;

        if (FontFamilyCombo != null) FontFamilyCombo.SelectedItem = new WpfFontFamily("Segoe UI");
        if (FontSizeCombo != null) FontSizeCombo.SelectedItem = 12.0;

        if (FontColorSwatch != null)
            FontColorSwatch.Background = new WpfSolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));

        if (FillColorSwatch != null)
            FillColorSwatch.Background = new WpfSolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#FDE047"));
    }

    private void SetActiveRichTextBox(WpfRichTextBox? rtb)
    {
        if (!_isPageActive) return;

        _activeRtb = rtb;
        UpdateToolbarFromSelection();
        UpdateResizeImageHintVisibility();
    }

    // ✅ Astuce Ctrl+molette (26.07.2026, demande de Joe) : affichée uniquement si la zone
    // actuellement active contient une image, pas en permanence.
    private void UpdateResizeImageHintVisibility()
    {
        if (ResizeImageHintBorder == null) return;
        ResizeImageHintBorder.Visibility = _activeRtb?.Document != null && DocumentHasImage(_activeRtb.Document)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool DocumentHasImage(FlowDocument doc) => FindFirstInlineUIContainer(doc.Blocks) != null;

    private static InlineUIContainer? FindFirstInlineUIContainer(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph p)
            {
                var found = FindFirstInlineUIContainerInInlines(p.Inlines);
                if (found != null) return found;
            }
            else if (block is Section sec)
            {
                var found = FindFirstInlineUIContainer(sec.Blocks);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static InlineUIContainer? FindFirstInlineUIContainerInInlines(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer iuc) return iuc;
            if (inline is Span span)
            {
                var found = FindFirstInlineUIContainerInInlines(span.Inlines);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void UpdateToolbarFromSelection()
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;
        if (!_activeRtb.IsLoaded) return;

        _rtbInternalUpdate = true;
        try
        {
            object fw = _activeRtb.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            object fs = _activeRtb.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            object td = _activeRtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            object ff = _activeRtb.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
            object fsz = _activeRtb.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            object fg = _activeRtb.Selection.GetPropertyValue(TextElement.ForegroundProperty);
            object bg = _activeRtb.Selection.GetPropertyValue(TextElement.BackgroundProperty);

            if (BoldToggle != null)
                BoldToggle.IsChecked = (fw != DependencyProperty.UnsetValue) && fw.Equals(FontWeights.Bold);

            if (ItalicToggle != null)
                ItalicToggle.IsChecked = (fs != DependencyProperty.UnsetValue) && fs.Equals(FontStyles.Italic);

            if (UnderlineToggle != null)
                UnderlineToggle.IsChecked =
                    (td != DependencyProperty.UnsetValue) &&
                    td is WpfTextDecorationCollection coll &&
                    coll == WpfTextDecorations.Underline;

            if (FontFamilyCombo != null && ff != DependencyProperty.UnsetValue && ff is WpfFontFamily fam)
                FontFamilyCombo.SelectedItem = fam;

            if (FontSizeCombo != null && fsz != DependencyProperty.UnsetValue && fsz is double d)
            {
                var nearest = _fontSizes.OrderBy(x => Math.Abs(x - d)).FirstOrDefault();
                FontSizeCombo.SelectedItem = nearest;
            }

            if (FontColorSwatch != null && fg != DependencyProperty.UnsetValue && fg is WpfSolidColorBrush brush)
                FontColorSwatch.Background = new WpfSolidColorBrush(brush.Color);

            if (FillColorSwatch != null && bg != DependencyProperty.UnsetValue && bg is WpfSolidColorBrush bgb)
                FillColorSwatch.Background = new WpfSolidColorBrush(bgb.Color);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // ✅ La page est en train de changer => ignorer
        }
        catch (InvalidOperationException)
        {
            // ✅ Layout pas prêt => ignorer
        }
        finally
        {
            _rtbInternalUpdate = false;
        }
    }

    private void ApplyToggle(DependencyProperty formattingProperty, object valueWhenChecked, object valueWhenUnchecked, bool? isChecked)
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;

        var value = (isChecked == true) ? valueWhenChecked : valueWhenUnchecked;
        _activeRtb.Selection.ApplyPropertyValue(formattingProperty, value);
        _activeRtb.Focus();
    }

    private void ApplyFontFamily(WpfFontFamily family)
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;

        _activeRtb.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        _activeRtb.Focus();
    }

    private void ApplyFontSize(double size)
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;

        _activeRtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        _activeRtb.Focus();
    }

    private void ApplyFontColor(WpfColor c)
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;

        _activeRtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new WpfSolidColorBrush(c));
        _activeRtb.Focus();
    }

    private void ApplyFillColor(WpfColor c)
    {
        if (!_isPageActive) return;
        if (_activeRtb == null) return;

        _activeRtb.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new WpfSolidColorBrush(c));
        _activeRtb.Focus();
    }

    // ✅ Insertion d'image dans une zone de texte (25.07.2026, demande de Joe) : PNG/photo
    // uniquement (pas de PDF -- pas une image, ne peut pas s'incruster dans du texte). Même
    // stockage stable que les images posées sur le plan (CopyImageIntoAppStorage), pour ne
    // pas perdre l'image si le fichier source est déplacé/supprimé.
    private void InsertImageIntoActiveTextZone_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeRtb == null)
            {
                System.Windows.MessageBox.Show(
                    "Clique d'abord dans une zone de texte pour y insérer une image.",
                    "Insérer une image", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Insérer une image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (ofd.ShowDialog() != true) return;

            var storedPath = CopyAndCompressImageForRichText(ofd.FileName);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(storedPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            new InlineUIContainer(CreateResizableInlineImage(bmp), _activeRtb.CaretPosition);
            _activeRtb.Focus();
        }
        catch (Exception ex)
        {
            LogPlanningError("InsertImageIntoActiveTextZone_Click", ex);
        }
    }

    // ✅ Compression à l'insertion (26.07.2026, demande de Joe) : une photo de téléphone non
    // retouchée peut faire plusieurs Mo pour un affichage qui ne dépassera jamais ~220px de
    // large dans le texte -> réduit à 1600px de long côté max (largement suffisant à l'écran
    // et à l'impression) avant stockage, pour ne pas alourdir inutilement le disque/la
    // mémoire. Ne touche PAS CopyImageIntoAppStorage (images du plan) : ces images doivent
    // rester en pleine résolution pour la clarté du plan imprimé. Dupliqué dans
    // TaskDescriptionWindow.xaml.cs (champ Descriptif) pour la même raison que le reste.
    private const int RichTextImageMaxDimension = 1600;

    private static string CopyAndCompressImageForRichText(string sourcePath)
    {
        var dir = GetPlanImagesDir(Db.GetCurrentProjectId());
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var destPath = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");

        try
        {
            var decoder = BitmapDecoder.Create(new Uri(sourcePath, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            if (frame.PixelWidth > RichTextImageMaxDimension || frame.PixelHeight > RichTextImageMaxDimension)
            {
                var scale = (double)RichTextImageMaxDimension / Math.Max(frame.PixelWidth, frame.PixelHeight);
                var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));

                BitmapEncoder encoder = ext.ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 85 },
                    ".gif" => new GifBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(scaled));

                using var fs = new FileStream(destPath, FileMode.Create);
                encoder.Save(fs);
                return destPath;
            }
        }
        catch { /* fallback : copie brute ci-dessous */ }

        File.Copy(sourcePath, destPath, overwrite: false);
        return destPath;
    }

    // ============================================================
    // Images redimensionnables dans le texte enrichi (26.07.2026, demande de Joe) :
    // redimensionnement par Ctrl+molette au-dessus de l'image. Deux approches par bouton
    // (poignée glissée, puis boutons cliquables －/＋) ont été essayées avant celle-ci et
    // toutes les deux échouaient en pratique : le RichTextBox capture la souris dès le clic
    // pour gérer sa propre sélection de texte, ce qui empêche tout contrôle enfant (Thumb ou
    // Button) posé sur l'image de recevoir un clic ou un glisser-déposer complet — vérifié
    // par un test avec clic souris simulé (aucune réaction, confirmé par Joe en pratique
    // aussi). Ctrl+molette contourne le problème : c'est un événement écouté directement sur
    // le RichTextBox lui-même (pas sur un enfant), donc pas de conflit de capture souris.
    // Duplique aussi dans TaskDescriptionWindow.xaml.cs (champ Descriptif), qui n'a pas
    // accès à ces membres privés de PlanningPage.
    // ============================================================

    private const double InlineImageMinSize = 30;
    private const double InlineImageMaxSize = 600;

    private static FrameworkElement CreateResizableInlineImage(BitmapImage bmp)
    {
        double w = bmp.PixelWidth > 0 ? bmp.PixelWidth : 220;
        double h = bmp.PixelHeight > 0 ? bmp.PixelHeight : 220;
        if (w > 220 || h > 220)
        {
            var scale = 220 / Math.Max(w, h);
            w *= scale; h *= scale;
        }

        var image = new System.Windows.Controls.Image
        {
            Source = bmp,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill,
            ToolTip = "Ctrl + molette pour redimensionner"
        };

        // ✅ Le Grid doit avoir une taille EXPLICITE (identique à l'image) et ne pas
        // s'étirer (HorizontalAlignment/VerticalAlignment = Left/Top) : sinon, inséré dans
        // le flux de texte, il s'étire sur toute la largeur de ligne restante et la
        // hauteur de ligne réservée devient incohérente (image qui semble "coupée").
        var grid = new Grid
        {
            Width = w,
            Height = h,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        grid.Children.Add(image);
        return grid;
    }

    // ✅ Appelé depuis PreviewMouseWheel du RichTextBox (Rtb_PreviewMouseWheel) : trouve
    // l'image sous le curseur (via un test de collision, pas via un contrôle enfant qui ne
    // recevrait pas l'événement) et la redimensionne si Ctrl est enfoncé.
    private static bool TryResizeInlineImageUnderCursor(WpfRichTextBox rtb, System.Windows.Point position, int wheelDelta)
    {
        var hit = VisualTreeHelper.HitTest(rtb, position);
        var image = FindAncestorOrSelf<System.Windows.Controls.Image>(hit?.VisualHit);
        if (image?.Parent is not Grid grid) return false;

        var factor = wheelDelta > 0 ? 1.15 : 0.85;
        var newW = Math.Clamp(image.Width * factor, InlineImageMinSize, InlineImageMaxSize);
        var newH = Math.Clamp(image.Height * factor, InlineImageMinSize, InlineImageMaxSize);
        image.Width = grid.Width = newW;
        image.Height = grid.Height = newH;
        return true;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T match) return match;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ✅ Après désérialisation XAML (SaveRtbToZoneXaml/RestoreZoneXamlToRtb), efface
    // d'éventuelles anciennes poignées/boutons de redimensionnement (Thumb ou boutons －/＋,
    // laissés par de précédentes versions de cette fonctionnalité qui ne fonctionnaient pas)
    // pour ne garder que l'image.
    private static void RewireResizableInlineImages(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
            RewireResizableInlineImagesInBlock(block);
    }

    private static void RewireResizableInlineImagesInBlock(Block block)
    {
        if (block is Paragraph p)
        {
            foreach (var inline in p.Inlines)
                RewireResizableInlineImagesInInline(inline);
        }
        else if (block is Section sec)
        {
            foreach (var b in sec.Blocks)
                RewireResizableInlineImagesInBlock(b);
        }
    }

    private static void RewireResizableInlineImagesInInline(Inline inline)
    {
        if (inline is InlineUIContainer iuc)
        {
            if (iuc.Child is Grid grid)
            {
                var image = grid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (image != null)
                {
                    var stale = grid.Children.OfType<FrameworkElement>().Where(c => !ReferenceEquals(c, image)).ToList();
                    foreach (var s in stale) grid.Children.Remove(s);
                    image.ToolTip = "Ctrl + molette pour redimensionner";
                }
            }
            // ✅ Images insérées AVANT ce mécanisme (image seule, pas encore enveloppée dans
            // le Grid redimensionnable) : mise à niveau automatique vers le nouveau format.
            else if (iuc.Child is System.Windows.Controls.Image oldImage && oldImage.Source is BitmapImage oldBmp)
            {
                iuc.Child = CreateResizableInlineImage(oldBmp);
            }
        }
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                RewireResizableInlineImagesInInline(child);
        }
    }

    private void CompactRichTextBoxDocument(WpfRichTextBox rtb)
    {
        if (rtb.Document == null)
            rtb.Document = new FlowDocument();

        rtb.Document.LineHeight = RtbDefaultLineHeight;
        // ✅ MaxHeight (pas BlockLineHeight) : BlockLineHeight fige TOUTES les lignes à
        // exactement RtbDefaultLineHeight (14px), y compris celle contenant une image
        // insérée (25.07.2026) — bien plus haute que 14px -> l'image (et donc les boutons
        // －/＋ ancrés dans son coin) se retrouvait quasi entièrement coupée. MaxHeight garde
        // le texte normal compact tout en laissant une ligne grandir pour accueillir un
        // élément plus grand qu'elle contient. Bug signalé par Joe le 26.07.2026.
        rtb.Document.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        rtb.Document.PagePadding = new Thickness(0);

        foreach (var p in rtb.Document.Blocks.OfType<Paragraph>())
        {
            p.Margin = ParagraphZeroMargin;
            p.LineHeight = RtbDefaultLineHeight;
            p.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        }
    }

    private void EnsureRtbHasAtLeastOneParagraph(WpfRichTextBox rtb)
    {
        if (rtb.Document == null)
            rtb.Document = new FlowDocument();

        if (!rtb.Document.Blocks.OfType<Paragraph>().Any())
        {
            var p = new Paragraph
            {
                Margin = ParagraphZeroMargin,
                LineHeight = RtbDefaultLineHeight,
                LineStackingStrategy = LineStackingStrategy.MaxHeight
            };
            rtb.Document.Blocks.Add(p);
        }
    }

    // ============================================================
    // Sauvegarde/Restoration formatage (XAML FlowDocument)
    // ============================================================

    private static string SerializeFlowDocumentToXaml(FlowDocument doc)
    {
        try
        {
            using var sw = new StringWriter(CultureInfo.InvariantCulture);
            using var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false });
            XamlWriter.Save(doc, xw);
            xw.Flush();
            return sw.ToString();
        }
        catch (Exception ex) { LogPlanningError("SerializeFlowDocumentToXaml", ex); return ""; }
    }

    private static FlowDocument? DeserializeFlowDocumentFromXaml(string? xaml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(xaml))
                return null;

            using var sr = new StringReader(xaml);
            using var xr = XmlReader.Create(sr);
            return XamlReader.Load(xr) as FlowDocument;
        }
        catch (Exception ex) { LogPlanningError("DeserializeFlowDocumentFromXaml", ex); return null; }
    }

    private void SaveRtbToZoneXaml(WpfRichTextBox rtb)
    {
        if (!_isPageActive) return;

        var idx = GetZoneIndexFromRtb(rtb);
        if (idx < 0 || idx > 3) return;

        TextZones[idx].DocumentXaml = SerializeFlowDocumentToXaml(rtb.Document);
    }

    private void RestoreZoneXamlToRtb(int zoneIndex, WpfRichTextBox rtb)
    {
        var doc = DeserializeFlowDocumentFromXaml(TextZones[zoneIndex].DocumentXaml) ?? new FlowDocument();
        rtb.Document = doc;

        EnsureRtbHasAtLeastOneParagraph(rtb);
        CompactRichTextBoxDocument(rtb);
        RewireResizableInlineImages(doc);
    }

    private int GetZoneIndexFromRtb(WpfRichTextBox rtb)
    {
        if (ReferenceEquals(rtb, Rtb0)) return 0;
        if (ReferenceEquals(rtb, Rtb1)) return 1;
        if (ReferenceEquals(rtb, Rtb2)) return 2;
        if (ReferenceEquals(rtb, Rtb3)) return 3;
        return -1;
    }

    // ============================================================
    // Handlers XAML - Plan: import
    // ============================================================

    private void PlanAdd_Click(object sender, RoutedEventArgs e) => BrowseAndAddPlanImages();

    // Retire: supprime l'image sélectionnée (la dernière cliquée) ou la dernière ajoutée
    private void PlanRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPlacedPlanImage != null)
        {
            var it = _selectedPlacedPlanImage;
            SelectPlacedPlanImage(null);
            PlacedPlanImages.Remove(it);
            return;
        }

        if (PlacedPlanImages.Count > 0)
            PlacedPlanImages.RemoveAt(PlacedPlanImages.Count - 1);
    }

    public void BrowseAndAddPlanImages()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Ajouter une image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
            Multiselect = true
        };

        if (ofd.ShowDialog() != true)
            return;

        foreach (var fp in ofd.FileNames ?? Array.Empty<string>())
            TryAddPlacedPlanImage(fp);
    }

    private static string GetPlanImagesDir(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture) : "0";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning", "Images", pid);
    }

    // ✅ Copie l'image importée dans le stockage de l'app : si le fichier source est
    // déplacé/supprimé/sur une clé USB retirée, le plan ne perd plus son image.
    private string CopyImageIntoAppStorage(string sourcePath)
    {
        var dir = GetPlanImagesDir(Db.GetCurrentProjectId());
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var destPath = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
        File.Copy(sourcePath, destPath, overwrite: false);
        return destPath;
    }

    private void TryAddPlacedPlanImage(string filePath)
    {
        try
        {
            var path = (filePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            string storedPath;
            try { storedPath = CopyImageIntoAppStorage(path); }
            catch { storedPath = path; }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(storedPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var it = new PlacedPlanImageItem
            {
                FilePath = storedPath,
                ImageSource = bmp,
                X = 20 + (PlacedPlanImages.Count * 20),
                Y = 20 + (PlacedPlanImages.Count * 20),
                Width = 360,
                Height = 240
            };

            PlacedPlanImages.Add(it);
            SelectPlacedPlanImage(it);
        }
        catch { }
    }

    // ============================================================
    // PlanCanvas (stickers drop)
    // ============================================================

    private void PlanCanvas_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        // ✅ Clic sur le fond du canvas (ou sur une image, qui reste tunnelé après ce
        // gestionnaire car il est déclaré sur un enfant) : désélectionne d'abord ; un clic
        // réel sur une image la resélectionnera juste après via
        // PlacedPlanImage_PreviewMouseLeftButtonDown.
        SelectPlacedPlanImage(null);

        if (_bankDragSticker != null)
        {
            var pos = e.GetPosition(PlanCanvas);
            AddPlacedStickerFromBank(_bankDragSticker, pos.X, pos.Y);
            _bankDragSticker = null;
            _bankIsDragging = false;
            e.Handled = true;
        }
    }

    private void PlanCanvas_PreviewMouseMove(object sender, WpfMouseEventArgs e) { }
    private void PlanCanvas_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e) { }

    // ============================================================
    // Stickers: banque -> drop / déplacer / supprimer
    // ============================================================

    private void StickerBank_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is StickerItem s)
        {
            _selectedBankSticker = s;

            _bankIsDragging = true;
            _bankDragStart = e.GetPosition(null);
            _bankDragSticker = s;
        }
    }

    private void StickerBank_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_bankIsDragging || _bankDragSticker == null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _bankIsDragging = false;
            _bankDragSticker = null;
            return;
        }

        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _bankDragStart.X) < 4 && Math.Abs(p.Y - _bankDragStart.Y) < 4)
            return;
    }

    private void StickerBank_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        _bankIsDragging = false;
    }

    private void AddPlacedStickerFromBank(StickerItem source, double x, double y)
    {
        PlacedStickers.Add(new PlacedStickerItem
        {
            Label = source.Label,
            ColorHex = source.ColorHex,
            IsGradient = source.IsGradient,
            Size = StickerDropDefaultSize,
            Width = StickerDropDefaultWidth,
            X = Math.Max(0, x - StickerDropDefaultWidth / 2),
            Y = Math.Max(0, y - StickerDropDefaultSize / 2),
        });
    }

    // ✅ NEW: drag au simple clic, édition au double-clic
    private void PlacedSticker_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        // Double-clic => édition
        if (e.ClickCount >= 2)
        {
            PlacedSticker_BeginEditFromDoubleClick(sender, e);
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is PlacedStickerItem s)
        {
            // Si on est en mode édition, ne pas démarrer un drag.
            if (s.IsEditing)
                return;

            _placedIsDragging = true;
            _placedDraggingSticker = s;
            _placedDragStartPointOnCanvas = e.GetPosition(PlanCanvas);
            _placedDragStartX = s.X;
            _placedDragStartY = s.Y;

            fe.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PlacedSticker_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isPageActive) return;

        if (!_placedIsDragging || _placedDraggingSticker == null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            PlacedDragEnd(sender);
            return;
        }

        var p = e.GetPosition(PlanCanvas);
        var dx = p.X - _placedDragStartPointOnCanvas.X;
        var dy = p.Y - _placedDragStartPointOnCanvas.Y;

        _placedDraggingSticker.X = Math.Max(0, _placedDragStartX + dx);
        _placedDraggingSticker.Y = Math.Max(0, _placedDragStartY + dy);

        e.Handled = true;
    }

    private void PlacedSticker_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        PlacedDragEnd(sender);
        e.Handled = true;
    }

    private void PlacedDragEnd(object sender)
    {
        _placedIsDragging = false;
        _placedDraggingSticker = null;

        if (sender is UIElement el && el.IsMouseCaptured)
            el.ReleaseMouseCapture();
    }

    private void PlacedSticker_RightClick(object sender, WpfMouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is FrameworkElement fe && fe.DataContext is PlacedStickerItem s)
        {
            PlacedStickers.Remove(s);
        }
    }

    // ============================================================
    // Images posées: déplacer / redimensionner / supprimer
    // ============================================================

    private PlacedPlanImageItem? _selectedPlacedPlanImage;

    // ✅ Sélectionne (ou désélectionne si null) une image posée, en tenant l'unique
    // source de vérité (_selectedPlacedPlanImage) synchronisée avec IsSelected sur
    // chaque item, pour que le contour bleu suive toujours la sélection réelle.
    private void SelectPlacedPlanImage(PlacedPlanImageItem? item)
    {
        _selectedPlacedPlanImage = item;
        foreach (var img in PlacedPlanImages)
            img.IsSelected = ReferenceEquals(img, item);
    }

    private void PlacedPlanImage_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return;

        if (sender is FrameworkElement fe && fe.DataContext is PlacedPlanImageItem it)
        {
            SelectPlacedPlanImage(it);

            _placedPlanImageIsDragging = true;
            _placedPlanImageDraggingItem = it;
            _placedPlanImageDragStartPointOnCanvas = e.GetPosition(PlanCanvas);
            _placedPlanImageDragStartX = it.X;
            _placedPlanImageDragStartY = it.Y;

            fe.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PlacedPlanImage_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isPageActive) return;

        if (!_placedPlanImageIsDragging || _placedPlanImageDraggingItem == null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            PlacedPlanImageDragEnd(sender);
            return;
        }

        var p = e.GetPosition(PlanCanvas);
        var dx = p.X - _placedPlanImageDragStartPointOnCanvas.X;
        var dy = p.Y - _placedPlanImageDragStartPointOnCanvas.Y;

        _placedPlanImageDraggingItem.X = Math.Max(0, _placedPlanImageDragStartX + dx);
        _placedPlanImageDraggingItem.Y = Math.Max(0, _placedPlanImageDragStartY + dy);

        e.Handled = true;
    }

    private void PlacedPlanImage_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        PlacedPlanImageDragEnd(sender);
        e.Handled = true;
    }

    private void PlacedPlanImageDragEnd(object sender)
    {
        _placedPlanImageIsDragging = false;
        _placedPlanImageDraggingItem = null;

        if (sender is UIElement el && el.IsMouseCaptured)
            el.ReleaseMouseCapture();
    }

    private void PlacedPlanImageResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isPageActive) return;

        if (sender is not Thumb th) return;
        if (th.DataContext is not PlacedPlanImageItem it) return;

        it.Width = Math.Max(PlanImageMinSize, it.Width + e.HorizontalChange);
        it.Height = Math.Max(PlanImageMinSize, it.Height + e.VerticalChange);
    }

    private void PlacedPlanImage_RightClick(object sender, WpfMouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is FrameworkElement fe && fe.DataContext is PlacedPlanImageItem it)
        {
            if (_selectedPlacedPlanImage == it) SelectPlacedPlanImage(null);
            PlacedPlanImages.Remove(it);
        }
    }

    // ============================================================
    // Text zones
    // ============================================================

    public void ActivateAllTextZones()
    {
        for (int i = 0; i < TextZones.Count; i++)
            TextZones[i].Visible = true;

        if (SelectedTextZoneIndex < 0)
            SelectedTextZoneIndex = FindFirstVisibleTextZoneIndex();
    }

    public void RemoveSelectedTextZone()
    {
        if (SelectedTextZoneIndex < 0 || SelectedTextZoneIndex > 3)
            return;

        var idx = SelectedTextZoneIndex;
        var z = TextZones[idx];
        if (!z.Visible) return;

        z.Visible = false;

        // ✅ Efface le texte de la zone : sinon, la remettre (Ajouter) faisait
        // réapparaître l'ancien contenu au lieu de repartir vide.
        z.DocumentXaml = "";
        var rtb = GetRtbFromZoneIndex(idx);
        if (rtb != null)
        {
            rtb.Document = new FlowDocument();
            EnsureRtbHasAtLeastOneParagraph(rtb);
        }

        SelectedTextZoneIndex = FindFirstVisibleTextZoneIndex();
    }

    private WpfRichTextBox? GetRtbFromZoneIndex(int idx) => idx switch
    {
        0 => Rtb0,
        1 => Rtb1,
        2 => Rtb2,
        3 => Rtb3,
        _ => null
    };

    private int FindFirstVisibleTextZoneIndex()
    {
        for (int i = 0; i < TextZones.Count; i++)
            if (TextZones[i].Visible) return i;
        return -1;
    }

    private void SelectTextZone(int idx)
    {
        if (idx < 0 || idx > 3) return;
        if (!TextZones[idx].Visible) return;
        SelectedTextZoneIndex = idx;
    }

    private void TextZoneAdd_Click(object sender, RoutedEventArgs e) => ActivateAllTextZones();
    private void TextZoneRemove_Click(object sender, RoutedEventArgs e) => RemoveSelectedTextZone();

    // ✅ Sélectionner tout / Copier / Coller (19.07.2026, demande de Joe) : pour copier le
    // texte d'une zone entière et le coller dans une zone de texte d'une autre semaine.
    // _activeRtb = zone de texte ayant reçu le focus en dernier (voir SetActiveRichTextBox).
    private void TextZoneSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _activeRtb?.SelectAll();
        _activeRtb?.Focus();
    }

    private void TextZoneCopy_Click(object sender, RoutedEventArgs e)
    {
        _activeRtb?.Copy();
    }

    private void TextZonePaste_Click(object sender, RoutedEventArgs e)
    {
        if (_activeRtb == null) return;
        _activeRtb.Focus();
        _activeRtb.Paste();
    }

    private void TextZone_Focus0(object sender, RoutedEventArgs e) => SelectTextZone(0);
    private void TextZone_Focus1(object sender, RoutedEventArgs e) => SelectTextZone(1);
    private void TextZone_Focus2(object sender, RoutedEventArgs e) => SelectTextZone(2);
    private void TextZone_Focus3(object sender, RoutedEventArgs e) => SelectTextZone(3);

    private void TextZone_Focus0(object sender, WpfMouseButtonEventArgs e) => SelectTextZone(0);
    private void TextZone_Focus1(object sender, WpfMouseButtonEventArgs e) => SelectTextZone(1);
    private void TextZone_Focus2(object sender, WpfMouseButtonEventArgs e) => SelectTextZone(2);
    private void TextZone_Focus3(object sender, WpfMouseButtonEventArgs e) => SelectTextZone(3);

    private void Rtb_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        if (sender is WpfRichTextBox rtb)
        {
            EnsureRtbHasAtLeastOneParagraph(rtb);
            CompactRichTextBoxDocument(rtb);
            SetActiveRichTextBox(rtb);
        }
    }

    private void Rtb_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isPageActive) return;

        if (sender is not WpfRichTextBox rtb) return;

        CompactRichTextBoxDocument(rtb);
        SaveRtbToZoneXaml(rtb);

        if (ReferenceEquals(rtb, _activeRtb)) UpdateResizeImageHintVisibility();

        if (_rtbInternalUpdate) return;
        if (ReferenceEquals(rtb, _activeRtb)) UpdateToolbarFromSelection();
    }

    // ✅ Redimensionnement d'image par Ctrl+molette (26.07.2026) : voir le commentaire au-
    // dessus de CreateResizableInlineImage pour pourquoi ce n'est pas un bouton/poignée.
    private void Rtb_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isPageActive) return;
        if (sender is not WpfRichTextBox rtb) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        if (TryResizeInlineImageUnderCursor(rtb, e.GetPosition(rtb), e.Delta))
        {
            SaveRtbToZoneXaml(rtb);
            e.Handled = true;
        }
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageActive) return;
        if (_rtbInternalUpdate) return;
        if (FontFamilyCombo?.SelectedItem is WpfFontFamily fam) ApplyFontFamily(fam);
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageActive) return;
        if (_rtbInternalUpdate) return;

        if (FontSizeCombo?.SelectedItem is double size) ApplyFontSize(size);
        else if (FontSizeCombo?.SelectedItem is int i) ApplyFontSize(i);
    }

    private void BoldToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;
        if (BoldToggle == null) return;
        ApplyToggle(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal, BoldToggle.IsChecked);
    }

    private void ItalicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;
        if (ItalicToggle == null) return;
        ApplyToggle(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal, ItalicToggle.IsChecked);
    }

    private void UnderlineToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;
        if (UnderlineToggle == null || _activeRtb == null) return;

        if (UnderlineToggle.IsChecked == true)
            _activeRtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, WpfTextDecorations.Underline);
        else
            _activeRtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);

        _activeRtb.Focus();
    }

    private void FontColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        try
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };

            if (FontColorSwatch?.Background is WpfSolidColorBrush b)
                dlg.Color = System.Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);

            var ok = dlg.ShowDialog();
            if (ok != System.Windows.Forms.DialogResult.OK) return;

            var c = WpfColor.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);

            if (FontColorSwatch != null)
                FontColorSwatch.Background = new WpfSolidColorBrush(c);

            ApplyFontColor(c);
        }
        catch { }
    }

    private void FillColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        try
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };

            if (FillColorSwatch?.Background is WpfSolidColorBrush b)
                dlg.Color = System.Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);

            var ok = dlg.ShowDialog();
            if (ok != System.Windows.Forms.DialogResult.OK) return;

            var c = WpfColor.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);

            if (FillColorSwatch != null)
                FillColorSwatch.Background = new WpfSolidColorBrush(c);

            ApplyFillColor(c);
        }
        catch { }
    }

    // ============================================================
    // Tâches / planning + Reload
    // ============================================================

    private void AddTaskRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        int count = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 5 : 1;

        for (int i = 0; i < count; i++)
        {
            var nextRef = (_taskRows.Count == 0)
                ? 1
                : _taskRows.Select(x => int.TryParse(x.Ref, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            // ✅ Semaine de création (28.07.2026, demande de Joe) : symétrique de DoneAt, une
            // tâche créée en semaine 31 ne se répercute plus dans les semaines PRÉCÉDENTES
            // (voir TaskRowVisibleInCurrentWeek). Null pour les tâches déjà existantes avant
            // cet ajout -- elles gardent leur comportement historique (visibles partout).
            _taskRows.Add(new TaskRow { Ref = nextRef.ToString(), CreatedWeekStart = SnapToStartOfWeek(_startDay, _weekStartDay) });
        }

        TasksDataGrid.ScrollIntoView(_taskRows.Last());

        // ✅ Persisté immédiatement (pas seulement au changement de semaine/page) : ces
        // tâches sont désormais communes à toute l'année, on ne veut pas dépendre d'une
        // navigation ultérieure pour les enregistrer.
        SaveProjectTasks();
    }

    private void RemoveTaskRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        // ✅ Fix (28.07.2026, demande de Joe : "parfois elle supprime, parfois non") : sans
        // valider la cellule/ligne en cours d'édition d'abord, supprimer une ligne pendant
        // que le DataGrid est encore en mode édition peut échouer silencieusement pour
        // certaines lignes selon l'état exact — même précaution que SaveProjectTasks().
        try { TasksDataGrid.CommitEdit(DataGridEditingUnit.Cell, true); TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

        var toRemove = _taskRows.Where(r => r.IsSelected).ToList();

        if (toRemove.Count == 0)
        {
            toRemove = TasksDataGrid.SelectedItems.Cast<object>().OfType<TaskRow>().ToList();
            if (toRemove.Count == 0 && TasksDataGrid.SelectedItem is TaskRow one) toRemove.Add(one);
        }

        if (toRemove.Count == 0) return;

        foreach (var r in toRemove)
            _taskRows.Remove(r);

        SaveProjectTasks();
    }

    // ✅ Éditeur agrandi du Descriptif (16.07.2026, demande de Joe) : ouvre TaskDescriptionWindow
    // en modal sur la ligne concernée. Le texte n'est appliqué que si l'utilisateur clique sur
    // "Enregistrer" dans la fenêtre (DialogResult == true) ; "Annuler" ne modifie rien.
    private void TaskExpandDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;
        if (sender is not WpfButton btn || btn.DataContext is not TaskRow row) return;

        var win = new TaskDescriptionWindow(
            row.Ref, row.Company, row.Building, row.Floor, row.Category, row.Reserve, row.Urgent, row.Done, row.Todo, row.TodoDocumentXaml,
            CompanyLabel, BuildingLabel, FloorLabel, TaskCategoryLabel, TaskUrgencyLabel,
            showCompany: TaskCompanyColumn.Visibility == Visibility.Visible,
            showBuilding: TaskBuildingColumn.Visibility == Visibility.Visible,
            showFloor: TaskFloorColumn.Visibility == Visibility.Visible,
            showCategory: TaskCategoryColumn.Visibility == Visibility.Visible,
            showUrgent: TaskUrgentColumn.Visibility == Visibility.Visible)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (win.ShowDialog() == true)
        {
            row.Todo = win.ResultText;
            row.TodoDocumentXaml = win.ResultDocumentXaml;
            SaveProjectTasks();
        }
    }

    private void AddPlanningRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        int count = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 5 : 1;

        for (int i = 0; i < count; i++)
            _planningRows.Add(new PlanningRow());

        PlanningDataGrid.ScrollIntoView(_planningRows.Last());
    }

    private void RemovePlanningRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        var toRemove = PlanningDataGrid.SelectedItems.Cast<object>().OfType<PlanningRow>().ToList();
        if (toRemove.Count == 0 && PlanningDataGrid.SelectedItem is PlanningRow one) toRemove.Add(one);

        foreach (var r in toRemove)
            _planningRows.Remove(r);

        if (_planningRows.Count == 0)
            _planningRows.Add(new PlanningRow());
    }

    // ✅ Toutes les options de structure de la semaine regroupées sous un seul bouton
    // (19.07.2026, demande de Joe : auparavant deux boutons séparés). Contient : structure
    // 5/6 jours (_weekDayCount, en mémoire pour la session), Samedi/Dimanche visibles
    // (mémorisés PAR SEMAINE, voir LoadWeekState), et leurs défauts par projet (voir
    // WeekendSaturdayDefaultCheckBox/WeekendSundayDefaultCheckBox ci-dessous).
    private void WeekStructureButton_Click(object sender, RoutedEventArgs e)
    {
        WeekStructurePopup.IsOpen = !WeekStructurePopup.IsOpen;
    }

    private void WeekDayCount_Changed(object sender, RoutedEventArgs e)
    {
        _weekDayCount = WeekDayCount5RadioButton.IsChecked == true ? 5 : 6;

        // ✅ Mémorisé par projet (19.07.2026, demande de Joe) : une fois l'utilisateur choisi,
        // l'app rouvrira dans ce mode -- plus seulement le défaut "5 jours" de livraison.
        var pid = Db.GetCurrentProjectId();
        if (pid.HasValue && pid.Value > 0)
            Db.SetPlanningWeekDayCount(pid.Value, _weekDayCount);

        ApplyPlanningHeadersAndSyncDatePickers();
        PlanningDataGrid?.UpdateLayout();
    }

    private void WeekendColumnVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (SaturdayColumn != null)
            SaturdayColumn.Visibility = WeekendShowSaturdayCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SundayColumn != null)
            SundayColumn.Visibility = WeekendShowSundayCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // ✅ Correctif (19.07.2026, demande de Joe) : la date de fin en haut de page ne se
        // mettait pas à jour quand on affichait/masquait Samedi/Dimanche via ces cases --
        // voir ApplyPlanningHeadersAndSyncDatePickers, qui recalcule maintenant la date de
        // fin en tenant compte de la visibilité Samedi/Dimanche.
        ApplyPlanningHeadersAndSyncDatePickers();
        PlanningDataGrid?.UpdateLayout();
    }

    // ✅ Défauts Samedi/Dimanche indépendants par projet (18.07.2026, demande de Joe) : un
    // utilisateur peut travailler tous les samedis mais jamais les dimanches (ou l'inverse),
    // donc deux cases séparées plutôt qu'un seul défaut "weekend" combiné (1er essai,
    // corrigé après retour de Joe).
    // ✅ Cocher OU décocher applique IMMÉDIATEMENT le jour concerné à toutes les semaines
    // déjà sauvegardées du projet, en plus de devenir le défaut pour les semaines futures.
    // Comportement symétrique (18.07.2026, corrigé après test de Joe) : un 1er essai où
    // seul "cocher" agissait rétroactivement laissait des réglages incohérents -- ex.
    // Dimanche resté affiché partout après un test précédent, alors que la case Dimanche
    // était pourtant décochée.
    private void WeekendSaturdayDefault_Changed(object sender, RoutedEventArgs e)
    {
        var pid = Db.GetCurrentProjectId();
        if (!pid.HasValue || pid.Value <= 0) return;

        var isChecked = WeekendSaturdayDefaultCheckBox.IsChecked == true;
        Db.SetDefaultShowSaturday(pid.Value, isChecked);
        ApplyShowWeekendDayToAllSavedWeeks(pid.Value, isSaturday: true, show: isChecked);

        if (WeekendShowSaturdayCheckBox != null) WeekendShowSaturdayCheckBox.IsChecked = isChecked;
        if (SaturdayColumn != null) SaturdayColumn.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        PlanningDataGrid?.UpdateLayout();
    }

    private void WeekendSundayDefault_Changed(object sender, RoutedEventArgs e)
    {
        var pid = Db.GetCurrentProjectId();
        if (!pid.HasValue || pid.Value <= 0) return;

        var isChecked = WeekendSundayDefaultCheckBox.IsChecked == true;
        Db.SetDefaultShowSunday(pid.Value, isChecked);
        ApplyShowWeekendDayToAllSavedWeeks(pid.Value, isSaturday: false, show: isChecked);

        if (WeekendShowSundayCheckBox != null) WeekendShowSundayCheckBox.IsChecked = isChecked;
        if (SundayColumn != null) SundayColumn.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        PlanningDataGrid?.UpdateLayout();
    }

    private void ApplyShowWeekendDayToAllSavedWeeks(long projectId, bool isSaturday, bool show)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning");

        if (!Directory.Exists(dir)) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

        foreach (var filePath in Directory.EnumerateFiles(dir, $"planning-week-{projectId}-*.json"))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<WeekStateFile>(json, opts);
                if (state == null) continue;

                if (isSaturday) state.ShowSaturday = show;
                else state.ShowSunday = show;

                File.WriteAllText(filePath, JsonSerializer.Serialize(state, opts));
            }
            catch { /* fichier illisible : ignoré, non bloquant */ }
        }
    }

    // ✅ Choix jour de départ (combo) - affichage FR via options Label/Value
    private sealed class WeekdayOption
    {
        public string Label { get; set; } = "";
        public DayOfWeek Value { get; set; }
    }

    private void StartWeekdayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageActive) return;
        if (_isSyncingDates) return;

        if (StartWeekdayCombo?.SelectedValue is DayOfWeek dow)
        {
            _weekStartDay = dow;

            var baseDate = StartDatePicker.SelectedDate ?? DateTime.Today;
            _startDay = SnapToStartOfWeek(baseDate, _weekStartDay);

            ApplyPlanningHeadersAndSyncDatePickers();
        }
    }

    private void StartDatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPageActive) return;
        if (_isSyncingDates) return;

        var selected = StartDatePicker.SelectedDate ?? DateTime.Today;
        _startDay = SnapToStartOfWeek(selected, _weekStartDay);

        ApplyPlanningHeadersAndSyncDatePickers();
    }

    private void EndDatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPageActive) return;
        if (_isSyncingDates) return;
        ApplyPlanningHeadersAndSyncDatePickers();
    }

    private async void PrevWeekButton_Click(object sender, RoutedEventArgs e)
    {
        await AnimateWeekNavigationAsync(-7);
    }

    private async void NextWeekButton_Click(object sender, RoutedEventArgs e)
    {
        await AnimateWeekNavigationAsync(+7);
    }

    private async Task AnimateWeekNavigationAsync(int days)
    {
        if (_isWeekAnimating) return;
        if (!_isPageActive) return;
        _isWeekAnimating = true;
        PrevWeekButton.IsEnabled = false;
        NextWeekButton.IsEnabled = false;

        try
        {
            // Sauvegarder la semaine courante avant de glisser
            SaveCurrentWeekState();

            var transform = new TranslateTransform();
            MainScrollViewer.RenderTransform = transform;

            double width = MainScrollViewer.ActualWidth;
            double slideOutTo = days > 0 ? -width : width;
            double slideInFrom = days > 0 ? width : -width;

            // --- Slide OUT ---
            var tcs1 = new TaskCompletionSource<bool>();
            var slideOut = new DoubleAnimation(0, slideOutTo, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideOut.Completed += (_, __) => tcs1.TrySetResult(true);
            transform.BeginAnimation(TranslateTransform.XProperty, slideOut);
            await tcs1.Task;

            // --- Mise à jour des données (hors écran, invisible) ---
            _startDay = _startDay.AddDays(days);
            LoadWeekState(SnapToStartOfWeek(_startDay, _weekStartDay));
            ApplyPlanningHeadersAndSyncDatePickers();

            // --- Repositionner de l'autre côté sans animation ---
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = slideInFrom;

            // --- Slide IN ---
            var tcs2 = new TaskCompletionSource<bool>();
            var slideIn = new DoubleAnimation(slideInFrom, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slideIn.Completed += (_, __) => tcs2.TrySetResult(true);
            transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            await tcs2.Task;

            MainScrollViewer.RenderTransform = Transform.Identity;
        }
        catch
        {
            // non bloquant — réinitialisation en cas d'erreur
            try { MainScrollViewer.RenderTransform = Transform.Identity; } catch { }
        }
        finally
        {
            _isWeekAnimating = false;
            PrevWeekButton.IsEnabled = true;
            NextWeekButton.IsEnabled = true;
        }
    }

    // ============================================================
    // ✅ Persistance par semaine (sauvegarde / chargement / duplication)
    // ============================================================

    private string GetWeekKey(DateTime weekStart)
    {
        var pid = Db.GetCurrentProjectId() ?? 0;
        return $"{pid}-{weekStart:yyyy}-W{ISOWeek.GetWeekOfYear(weekStart):D2}";
    }

    private static string GetWeekFilePath(string weekKey)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning");
        return Path.Combine(dir, $"planning-week-{weekKey}.json");
    }

    private WeekStateFile BuildCurrentWeekStateForKey(string weekKey)
    {
        return new WeekStateFile
        {
            WeekKey = weekKey,
            // ✅ Samedi/Dimanche : figé PAR SEMAINE (pas mémorisé globalement) — si tu
            // les affiches pour une semaine donnée, le choix reste attaché à cette
            // semaine et se retrouve tel quel en y revenant plus tard.
            ShowSaturday = WeekendShowSaturdayCheckBox?.IsChecked == true,
            ShowSunday = WeekendShowSundayCheckBox?.IsChecked == true,
            // ✅ Les tâches ne sont plus persistées par semaine (voir LoadProjectTasks /
            // SaveProjectTasks) : elles sont désormais communes à tout le projet, pour
            // qu'une tâche non terminée reste visible d'une semaine à l'autre au lieu
            // d'être remise à zéro. Ce champ est laissé vide pour ne pas dupliquer une
            // donnée qui n'est plus lue depuis ce fichier.
            TaskRows = new(),
            PlanningRows = _planningRows.Select(r => new PlanningRowState
            {
                Company = r.Company,
                D1 = r.D1,
                D2 = r.D2,
                D3 = r.D3,
                D4 = r.D4,
                D5 = r.D5,
                D6 = r.D6,
                Sat = r.Sat,
                Sun = r.Sun
            }).ToList(),
            PlacedStickerStates = PlacedStickers.Select(s => new PlacedStickerState
            {
                Label = s.Label,
                ColorHex = s.ColorHex,
                X = s.X,
                Y = s.Y,
                Size = s.Size,
                Width = s.Width,
                IsGradient = s.IsGradient
            }).ToList(),
            PlacedImageStates = PlacedPlanImages.Select(i => new PlacedImageState
            {
                FilePath = i.FilePath,
                X = i.X,
                Y = i.Y,
                Width = i.Width,
                Height = i.Height
            }).ToList(),
            TextZoneStates = TextZones.Select(z => new TextZoneState
            {
                Visible = z.Visible,
                Title = z.Title,
                DocumentXaml = z.DocumentXaml
            }).ToList()
        };
    }

    // ✅ Point d'entrée public : MainWindow appelle ceci JUSTE AVANT de remplacer
    // MainContent.Content par une autre page (Dashboard, etc.). On ne peut pas se fier
    // uniquement à l'événement Unloaded de cette page pour déclencher la sauvegarde : son
    // déclenchement n'est pas garanti assez tôt/de façon fiable dans ce contexte précis
    // (constaté après tests : les grilles, images et stickers restaient perdus même avec
    // la sauvegarde câblée sur Unloaded, alors que ça fonctionnait pour les zones de texte
    // qui, elles, sauvegardent à chaque frappe et ne dépendent d'aucun événement de
    // fermeture de page). Cet appel explicite et synchrone, déclenché par l'action de
    // navigation elle-même plutôt que par un événement WPF, est fiable dans tous les cas.
    public void FlushPendingChanges()
    {
        SaveCurrentWeekState();
    }

    private void SaveCurrentWeekState()
    {
        try
        {
            // ✅ BUG (grilles perdues) : une cellule de DataGrid en cours d'édition ne transfère
            // sa valeur vers l'objet lié (TaskRow/PlanningRow) qu'à la sortie de cellule (Tab,
            // clic ailleurs, Entrée) — jamais à chaque frappe comme les zones de texte. Si on
            // sauvegarde pendant qu'une cellule est encore en édition (ex: changement d'onglet
            // juste après avoir tapé), la valeur en cours de saisie n'est pas encore dans
            // _taskRows/_planningRows et est donc silencieusement perdue. On force ici la
            // validation de toute édition en cours avant de construire l'état à sauvegarder.
            try { TasksDataGrid.CommitEdit(DataGridEditingUnit.Cell, true); TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
            try { PlanningDataGrid.CommitEdit(DataGridEditingUnit.Cell, true); PlanningDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

            var weekStart = SnapToStartOfWeek(_startDay, _weekStartDay);
            var weekKey = GetWeekKey(weekStart);
            var filePath = GetWeekFilePath(weekKey);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var state = BuildCurrentWeekStateForKey(weekKey);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            // ✅ Les tâches sont sauvegardées séparément, par projet (pas par semaine).
            SaveProjectTasks();
        }
        catch { }
    }

    // ============================================================
    // ✅ Tâches — persistance par PROJET (pas par semaine)
    // ============================================================
    // Avant ce changement, les tâches étaient stockées dans le fichier de la semaine
    // affichée : naviguer d'une semaine à l'autre vidait donc le tableau des tâches et
    // la numérotation automatique repartait de 1 à chaque semaine. Les tâches sont
    // maintenant communes à tout le projet (fichier séparé, indépendant de la semaine) :
    // le tableau reste affiché tel quel en changeant de semaine (une tâche non terminée
    // "se répercute" naturellement d'une semaine sur l'autre), et la numérotation
    // automatique (voir AddTaskRowButton_Click, max des Ref existants + 1) devient
    // continue sur toute l'année plutôt que de se réinitialiser.
    private static string GetProjectTasksFilePath(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture)
            : "0";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning");
        return Path.Combine(dir, $"planning-tasks-{pid}.json");
    }

    private void LoadProjectTasks()
    {
        _taskRows.Clear();

        var pid = Db.GetCurrentProjectId();
        var filePath = GetProjectTasksFilePath(pid);

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rows = JsonSerializer.Deserialize<List<TaskRowState>>(json, opts) ?? new();

                foreach (var r in rows)
                    _taskRows.Add(new TaskRow
                    {
                        Ref = r.Ref,
                        Company = r.Company,
                        Building = r.Building,
                        Floor = r.Floor,
                        Todo = r.Todo,
                        TodoDocumentXaml = r.TodoDocumentXaml,
                        Category = r.Category,
                        Reserve = r.Reserve,
                        Urgent = r.Urgent,
                        // ✅ Done déclenche un effet de bord (met DoneAt = DateTime.Now) — DoneAt
                        // DOIT être réassigné après coup pour restaurer la vraie date sauvegardée
                        // plutôt que la date du jour (l'ordre du bloc d'initialisation suit l'ordre
                        // d'écriture ci-dessous, pas l'ordre de déclaration de la classe).
                        Done = r.Done,
                        DoneAt = r.DoneAt,
                        CreatedWeekStart = r.CreatedWeekStart
                    });

                return;
            }
            catch { }
        }

        // ✅ Migration ponctuelle (une seule fois) : avant ce changement, les tâches
        // vivaient dans le fichier de la semaine courante. Si le fichier par-projet
        // n'existe pas encore, on reprend les tâches déjà visibles dans la semaine
        // actuellement affichée, pour ne rien perdre au premier lancement après la mise
        // à jour.
        try
        {
            var weekKey = GetWeekKey(SnapToStartOfWeek(_startDay, _weekStartDay));
            var weekFilePath = GetWeekFilePath(weekKey);

            if (File.Exists(weekFilePath))
            {
                var json = File.ReadAllText(weekFilePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var state = JsonSerializer.Deserialize<WeekStateFile>(json, opts);

                if (state?.TaskRows != null)
                {
                    foreach (var r in state.TaskRows)
                        _taskRows.Add(new TaskRow
                        {
                            Ref = r.Ref,
                            Company = r.Company,
                            Building = r.Building,
                            Floor = r.Floor,
                            Todo = r.Todo,
                            Category = r.Category,
                            Reserve = r.Reserve,
                            Urgent = r.Urgent,
                            Done = r.Done
                        });
                }
            }
        }
        catch { }

        if (_taskRows.Count > 0)
            SaveProjectTasks();
    }

    private void SaveProjectTasks()
    {
        try
        {
            // Force la validation de toute cellule en cours d'édition avant de sauvegarder
            // (même raison que dans SaveCurrentWeekState).
            try { TasksDataGrid.CommitEdit(DataGridEditingUnit.Cell, true); TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

            var pid = Db.GetCurrentProjectId();
            var filePath = GetProjectTasksFilePath(pid);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var rows = _taskRows.Select(r => new TaskRowState
            {
                Ref = r.Ref,
                Company = r.Company,
                Building = r.Building,
                Floor = r.Floor,
                Todo = r.Todo,
                TodoDocumentXaml = r.TodoDocumentXaml,
                Category = r.Category,
                Reserve = r.Reserve,
                Urgent = r.Urgent,
                Done = r.Done,
                DoneAt = r.DoneAt,
                CreatedWeekStart = r.CreatedWeekStart
            }).ToList();

            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch { }
    }

    private void LoadWeekState(DateTime weekStart)
    {
        var weekKey = GetWeekKey(weekStart);
        var filePath = GetWeekFilePath(weekKey);

        // ✅ _taskRows n'est plus vidé/rechargé ici : les tâches sont désormais chargées une
        // seule fois par projet (LoadProjectTasks), indépendamment de la semaine affichée.
        _planningRows.Clear();
        PlacedStickers.Clear();
        PlacedPlanImages.Clear();
        _selectedPlacedPlanImage = null;

        // ✅ Samedi/Dimanche : figé par semaine (voir BuildCurrentWeekStateForKey). Pour une
        // semaine sans fichier (jamais visitée/sauvegardée), le point de départ est le défaut
        // par projet et par jour (18.07.2026, demande de Joe — voir Db.GetDefaultShowSaturday/
        // GetDefaultShowSunday), plutôt qu'un masquage systématique.
        var pid = Db.GetCurrentProjectId();
        bool showSaturday = pid.HasValue && pid.Value > 0 && Db.GetDefaultShowSaturday(pid.Value);
        bool showSunday = pid.HasValue && pid.Value > 0 && Db.GetDefaultShowSunday(pid.Value);
        List<TextZoneState>? textZoneStates = null;

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var state = JsonSerializer.Deserialize<WeekStateFile>(json, opts);

                if (state != null)
                {
                    showSaturday = state.ShowSaturday;
                    showSunday = state.ShowSunday;
                    textZoneStates = state.TextZoneStates;

                    // ✅ state.TaskRows n'est plus utilisé (voir LoadProjectTasks) — laissé
                    // dans le fichier/la classe uniquement pour compatibilité de lecture des
                    // anciens fichiers, sans effet ici.

                    foreach (var r in state.PlanningRows)
                        _planningRows.Add(new PlanningRow
                        {
                            Company = r.Company,
                            D1 = r.D1,
                            D2 = r.D2,
                            D3 = r.D3,
                            D4 = r.D4,
                            D5 = r.D5,
                            D6 = r.D6,
                            Sat = r.Sat,
                            Sun = r.Sun
                        });

                    foreach (var s in state.PlacedStickerStates)
                        PlacedStickers.Add(new PlacedStickerItem
                        {
                            Label = s.Label,
                            ColorHex = s.ColorHex,
                            X = s.X,
                            Y = s.Y,
                            Size = s.Size,
                            Width = s.Width,
                            IsGradient = s.IsGradient
                        });

                    foreach (var i in state.PlacedImageStates)
                    {
                        var path = (i.FilePath ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.UriSource = new Uri(path, UriKind.Absolute);
                            bmp.EndInit();
                            bmp.Freeze();
                            PlacedPlanImages.Add(new PlacedPlanImageItem
                            {
                                FilePath = path,
                                ImageSource = bmp,
                                X = i.X,
                                Y = i.Y,
                                Width = i.Width,
                                Height = i.Height
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        EnsureDefaultRows();

        // ✅ Migration : semaine sans zones de texte propres -> reprend une fois l'ancien
        // contenu partagé par projet, s'il existe (voir TryReadLegacyProjectTextZones).
        if (textZoneStates == null || textZoneStates.Count == 0)
            textZoneStates = TryReadLegacyProjectTextZones(pid);

        LoadTextZonesForWeek(textZoneStates);

        // ✅ Samedi/Dimanche : applique le choix figé pour CETTE semaine (voir plus haut),
        // que ce soit au premier affichage (Reload) ou en changeant de semaine (◀ ▶), qui
        // passe uniquement par LoadWeekState sans repasser par Reload().
        if (SaturdayColumn != null) SaturdayColumn.Visibility = showSaturday ? Visibility.Visible : Visibility.Collapsed;
        if (SundayColumn != null) SundayColumn.Visibility = showSunday ? Visibility.Visible : Visibility.Collapsed;
        if (WeekendShowSaturdayCheckBox != null) WeekendShowSaturdayCheckBox.IsChecked = showSaturday;
        if (WeekendShowSundayCheckBox != null) WeekendShowSundayCheckBox.IsChecked = showSunday;
    }

    private void DuplicateCurrentWeekTo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        int days = btn.Tag?.ToString() == "prev" ? -7 : +7;

        var targetWeekStart = SnapToStartOfWeek(_startDay.AddDays(days), _weekStartDay);
        var weekKey = GetWeekKey(targetWeekStart);
        var filePath = GetWeekFilePath(weekKey);

        var semaineNum = ISOWeek.GetWeekOfYear(targetWeekStart);
        var label = $"semaine {semaineNum} ({targetWeekStart:dd.MM.yyyy})";

        if (File.Exists(filePath))
        {
            var result = System.Windows.MessageBox.Show(
                $"La {label} contient déjà des données.\nLes remplacer par celles de la semaine courante ?",
                "Confirmer la duplication",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var state = BuildCurrentWeekStateForKey(weekKey);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            System.Windows.MessageBox.Show(
                $"Planning copié vers la {label}.",
                "Duplication réussie", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Erreur lors de la duplication : " + ex.Message,
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ✅ FIX: samedi après vendredi (recalage des index de colonnes)
    private void ApplyPlanningHeadersAndSyncDatePickers()
    {
        if (!_isPageActive) return;

        // ✅ Repère statique (28.07.2026, demande de Joe) : voir TaskRow.CurrentViewedWeekStart.
        TaskRow.CurrentViewedWeekStart = SnapToStartOfWeek(_startDay, _weekStartDay);

        // ✅ La semaine affichée (_startDay) a pu changer avant cet appel (navigation semaine
        // précédente/suivante, sélecteur de date...) : réévalue quelles tâches "Effectué"
        // doivent rester masquées pour cette semaine (28.07.2026, demande de Joe).
        try { _taskRowsView?.Refresh(); } catch { }

        // 0 = Entreprise
        // 1 = D1
        // 2 = D2
        // 3 = D3
        // 4 = D4
        // 5 = D5 (vendredi)
        // 6 = Samedi (SaturdayColumn)
        // 7 = Dimanche (SundayColumn)
        // 8 = D6 (lundi suivant) -- masquée en mode 5 jours (Day6Column, _weekDayCount)
        if (PlanningDataGrid.Columns.Count < 9)
            return;

        var start = SnapToStartOfWeek(_startDay, _weekStartDay);
        var businessDays = BuildBusinessDays(start, _weekDayCount);

        PlanningDataGrid.Columns[1].Header = HeaderForDay(businessDays[0]);
        PlanningDataGrid.Columns[2].Header = HeaderForDay(businessDays[1]);
        PlanningDataGrid.Columns[3].Header = HeaderForDay(businessDays[2]);
        PlanningDataGrid.Columns[4].Header = HeaderForDay(businessDays[3]);
        PlanningDataGrid.Columns[5].Header = HeaderForDay(businessDays[4]);

        var saturdayDate = NextDayOfWeek(businessDays[4], DayOfWeek.Saturday);
        SaturdayColumn.Header = HeaderForDay(saturdayDate);
        SundayColumn.Header = HeaderForDay(saturdayDate.AddDays(1));

        // ✅ Mode 5 jours (19.07.2026, demande de Joe) : colonne "Jour 6" masquée, businessDays
        // ne contient alors que 5 entrées -- les données déjà saisies dans D6 restent en
        // mémoire (non effacées), simplement pas affichées, comme Samedi/Dimanche masqués.
        if (Day6Column != null)
            Day6Column.Visibility = _weekDayCount >= 6 ? Visibility.Visible : Visibility.Collapsed;

        if (_weekDayCount >= 6)
            PlanningDataGrid.Columns[8].Header = HeaderForDay(businessDays[5]);

        var lastDay = businessDays[_weekDayCount - 1];

        // ✅ Correctif (19.07.2026, demande de Joe) : la date de fin restait sur le dernier
        // jour ouvré (ex. vendredi) même quand Samedi/Dimanche sont affichés dans la grille,
        // alors que ce sont alors les vraies dernières colonnes visibles. Sans effet en mode
        // 6 jours : "Jour 6" (lundi suivant) est toujours après le dimanche.
        if (SundayColumn.Visibility == Visibility.Visible && saturdayDate.AddDays(1) > lastDay)
            lastDay = saturdayDate.AddDays(1);
        else if (SaturdayColumn.Visibility == Visibility.Visible && saturdayDate > lastDay)
            lastDay = saturdayDate;

        _isSyncingDates = true;
        try
        {
            StartDatePicker.SelectedDate = businessDays[0];
            EndDatePicker.SelectedDate = lastDay;
        }
        finally { _isSyncingDates = false; }

        WeekTextBlock.Text = $"Semaine {ISOWeek.GetWeekOfYear(businessDays[0])}";
    }

    private static DateTime SnapToStartOfWeek(DateTime date, DayOfWeek startDay)
    {
        var d = date.Date;
        while (d.DayOfWeek != startDay)
            d = d.AddDays(-1);
        return d;
    }

    // ✅ Généralisée à 5 ou 6 jours (19.07.2026, demande de Joe -- avant : toujours 6,
    // BuildSixBusinessDays). "Jours ouvrés" au sens large ici : compte simplement les jours
    // qui ne sont ni Samedi ni Dimanche, peu importe le jour de départ choisi.
    private static List<DateTime> BuildBusinessDays(DateTime start, int count)
    {
        var res = new List<DateTime>(count);
        var d = start.Date;
        while (res.Count < count)
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                res.Add(d);
            d = d.AddDays(1);
        }
        return res;
    }

    private static DateTime NextDayOfWeek(DateTime from, DayOfWeek target)
    {
        var d = from.Date.AddDays(1);
        while (d.DayOfWeek != target) d = d.AddDays(1);
        return d;
    }

    // ✅ Format "Lundi 18 juil." (mois abrégé) pour toutes les semaines — 18.07.2026, demande
    // de Joe. La distinction avec le mois complet pour les semaines "à 5 jours" a été retirée
    // : ce cas n'existe pas en pratique (minimum 6 jours), le format abrégé convient à toutes.
    private static string HeaderForDay(DateTime d)
    {
        var fr = CultureInfo.GetCultureInfo("fr-CH");
        var name = fr.DateTimeFormat.GetDayName(d.DayOfWeek);
        name = char.ToUpper(name[0]) + name.Substring(1);
        return $"{name} {d.ToString("d MMM", fr)}";
    }

    public void Reload()
    {
        if (!_isPageActive) return;

        var start = SnapToStartOfWeek(DateTime.Today, _weekStartDay);

        if (StartDatePicker.SelectedDate == null)
            StartDatePicker.SelectedDate = start;

        _startDay = SnapToStartOfWeek(StartDatePicker.SelectedDate ?? start, _weekStartDay);

        // ✅ Défauts Samedi/Dimanche pour les nouvelles semaines (18.07.2026, demande de Joe) :
        // reflètent le réglage du projet courant dans les cases à cocher du menu "Weekend".
        {
            var pidForDefault = Db.GetCurrentProjectId();
            var hasProjectForDefault = pidForDefault.HasValue && pidForDefault.Value > 0;

            if (WeekendSaturdayDefaultCheckBox != null)
                WeekendSaturdayDefaultCheckBox.IsChecked = hasProjectForDefault && Db.GetDefaultShowSaturday(pidForDefault!.Value);

            if (WeekendSundayDefaultCheckBox != null)
                WeekendSundayDefaultCheckBox.IsChecked = hasProjectForDefault && Db.GetDefaultShowSunday(pidForDefault!.Value);

            // ✅ Structure de la semaine (19.07.2026, demande de Joe) : 5 jours par défaut pour
            // un projet jamais configuré, puis mémorisé par projet selon le choix de
            // l'utilisateur (voir Db.GetPlanningWeekDayCount).
            _weekDayCount = hasProjectForDefault ? Db.GetPlanningWeekDayCount(pidForDefault!.Value) : 5;
            if (WeekDayCount6RadioButton != null && WeekDayCount5RadioButton != null)
            {
                WeekDayCount6RadioButton.IsChecked = _weekDayCount == 6;
                WeekDayCount5RadioButton.IsChecked = _weekDayCount == 5;
            }
        }

        LoadLists();
        EnsureStickerBankLoadedOrInitialized();
        SyncCompanyColorStickers();
        EnsureImageFavoritesLoaded();

        // ✅ Tâches : indépendantes de la semaine, chargées une seule fois par instance de
        // page (voir LoadProjectTasks) — jamais réinitialisées lors de la navigation entre
        // semaines.
        if (_taskRows.Count == 0)
            LoadProjectTasks();

        // Charger l'état de la semaine (planning, stickers, images) depuis le fichier
        // (première ouverture ou retour sur la page)
        // ✅ Samedi/Dimanche (figés par semaine) sont restaurés par LoadWeekState() lui-même
        // dans la branche ci-dessous ; si les données sont déjà en mémoire (branche
        // EnsureDefaultRows), l'état actuel des colonnes/cases est laissé tel quel.
        if (_planningRows.Count == 0
            && PlacedStickers.Count == 0 && PlacedPlanImages.Count == 0)
            LoadWeekState(_startDay);
        else
            EnsureDefaultRows();

        InitializeTaskColumnsVisibility();

        ApplyPlanningHeadersAndSyncDatePickers();
    }

    // ✅ Choix des colonnes à listes déroulantes du tableau des Tâches (Entreprise,
    // Bâtiment, Étage, Catégorie, Urg.) — affichées/masquées via le bouton "Colonnes",
    // mémorisé entre les sessions (Db.GetTasksVisibleColumns/SetTasksVisibleColumns).
    // Seules les colonnes à listes déroulantes sont concernées (pas N°, Descriptif,
    // ni Effectué, qui restent toujours visibles).
    private void InitializeTaskColumnsVisibility()
    {
        var visible = (Db.GetTasksVisibleColumns() ?? "Company,Building,Floor,Category")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ApplyTaskColumnVisibility(TaskCompanyColumn, ColShowCompanyCheckBox, visible.Contains("Company"));
        ApplyTaskColumnVisibility(TaskBuildingColumn, ColShowBuildingCheckBox, visible.Contains("Building"));
        ApplyTaskColumnVisibility(TaskFloorColumn, ColShowFloorCheckBox, visible.Contains("Floor"));
        ApplyTaskColumnVisibility(TaskCategoryColumn, ColShowCategoryCheckBox, visible.Contains("Category"));
        ApplyTaskColumnVisibility(TaskUrgentColumn, ColShowUrgencyCheckBox, visible.Contains("Urgency"));
    }

    private static void ApplyTaskColumnVisibility(DataGridColumn column, WpfCheckBox checkBox, bool isVisible)
    {
        column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (checkBox != null)
            checkBox.IsChecked = isVisible;
    }

    private void TaskColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        TaskColumnsPopup.IsOpen = !TaskColumnsPopup.IsOpen;
    }

    private void TaskColumnVisibility_Changed(object sender, RoutedEventArgs e)
    {
        TaskCompanyColumn.Visibility = ColShowCompanyCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TaskBuildingColumn.Visibility = ColShowBuildingCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TaskFloorColumn.Visibility = ColShowFloorCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TaskCategoryColumn.Visibility = ColShowCategoryCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TaskUrgentColumn.Visibility = ColShowUrgencyCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        var visibleKeys = new List<string>();
        if (ColShowCompanyCheckBox.IsChecked == true) visibleKeys.Add("Company");
        if (ColShowBuildingCheckBox.IsChecked == true) visibleKeys.Add("Building");
        if (ColShowFloorCheckBox.IsChecked == true) visibleKeys.Add("Floor");
        if (ColShowCategoryCheckBox.IsChecked == true) visibleKeys.Add("Category");
        if (ColShowUrgencyCheckBox.IsChecked == true) visibleKeys.Add("Urgency");

        Db.SetTasksVisibleColumns(string.Join(",", visibleKeys));
    }

    private void LoadLists()
    {
        var pid = Db.GetCurrentProjectId();
        var p = (pid.HasValue && pid.Value > 0) ? pid.Value : 0;

        Companies = p > 0 ? Db.WithEmptyOption(Db.GetCompanies(p)) : new List<string>();
        Buildings = p > 0 ? Db.WithEmptyOption(Db.GetPlaces(p)) : new List<string>();
        Floors = p > 0 ? Db.WithEmptyOption(Db.GetEtages(p)) : new List<string>();
        PlanningTextZones = p > 0 ? Db.GetPlanningTextZones(p) : new List<string>();
        Reserves = p > 0 ? Db.WithEmptyOption(Db.GetReserves(p)) : new List<string>();
        TaskCategories = p > 0 ? Db.GetTaskCategories(p) : new List<string>();
        TaskUrgencies = p > 0 ? Db.GetTaskUrgencies(p) : new List<string>();
        TaskCategoryLabel = p > 0 ? Db.GetLabelTaskCategory(p) : "Cat.";
        TaskUrgencyLabel = p > 0 ? Db.GetLabelTaskUrgency(p) : "Urg.";
        CompanyLabel = p > 0 ? Db.GetLabelPerformedBy(p) : "Entreprise";
        BuildingLabel = p > 0 ? Db.GetLabelPlace(p) : "Bâtiment";
        FloorLabel = p > 0 ? Db.GetLabelEtage(p) : "Étage";

        CompanyColorMap = p > 0
            ? Db.GetCompanyColorMap(p)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Diagnostic temporaire supprimé.


        OnPropertyChanged(nameof(Companies));
        OnPropertyChanged(nameof(Buildings));
        OnPropertyChanged(nameof(Floors));
        OnPropertyChanged(nameof(PlanningTextZones));
        OnPropertyChanged(nameof(PlanningTextZonesWithBlank));
        OnPropertyChanged(nameof(Reserves));
        OnPropertyChanged(nameof(TaskCategories));
        OnPropertyChanged(nameof(TaskCategoriesWithBlank));
        OnPropertyChanged(nameof(TaskUrgencies));
        OnPropertyChanged(nameof(TaskUrgenciesWithBlank));
        OnPropertyChanged(nameof(TaskCategoryLabel));
        OnPropertyChanged(nameof(TaskUrgencyLabel));
        OnPropertyChanged(nameof(CompanyLabel));
        OnPropertyChanged(nameof(BuildingLabel));
        OnPropertyChanged(nameof(FloorLabel));
        OnPropertyChanged(nameof(CompanyColorMap));

        // Forcer la recréation des ItemsSource pour éviter le partage de brushes
        try { RecreateDataGridItemsSources(); } catch { }
    }

    private void EnsureDefaultRows()
    {
        if (_taskRows.Count == 0)
            for (int i = 0; i < DEFAULT_TASK_ROWS; i++)
                _taskRows.Add(new TaskRow { Ref = (i + 1).ToString() });

        if (_planningRows.Count == 0)
            for (int i = 0; i < DEFAULT_PLANNING_ROWS; i++)
                _planningRows.Add(new PlanningRow());
    }

    // ============================================================
    // DataGrid helpers + Done paint
    // ============================================================

    private void DataGrid_PreviewMouseLeftButtonDown_FocusCell(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        if (sender is not DataGrid grid)
            return;

        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not DataGridCell)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridCell cell)
            return;

        if (!cell.IsFocused)
            cell.Focus();

        grid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
        grid.SelectedItem = cell.DataContext;
    }

    private void ComboBox_OpenOnSingleClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not WpfComboBox)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not WpfComboBox combo)
            return;

        if (combo.IsDropDownOpen)
            return;

        combo.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isPageActive) return;
            if (!combo.IsLoaded) return;

            if (!combo.IsKeyboardFocusWithin && !combo.IsMouseOver)
                return;

            combo.IsDropDownOpen = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CloseDropdownOnEscape(object sender, WpfKeyEventArgs e)
    {
        if (!_isPageActive) return;

        if (e.Key != Key.Escape)
            return;

        var dep = Keyboard.FocusedElement as DependencyObject;
        while (dep != null && dep is not WpfComboBox)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is WpfComboBox combo && combo.IsDropDownOpen)
        {
            combo.IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void ForwardMouseWheelToMainScrollViewer(object sender, MouseWheelEventArgs e)
    {
        if (!_isPageActive) return;
        if (MainScrollViewer == null) return;

        e.Handled = true;

        var ev = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };

        MainScrollViewer.RaiseEvent(ev);
    }

    private void TasksGrid_PreviewMouseLeftButtonDown_DonePaint(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not WpfCheckBox)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not WpfCheckBox cb)
            return;

        // ✅ Ne concerne que la case "Effectué" — pas la case de sélection à gauche,
        // qui doit garder son comportement de toggle normal (binding IsSelected).
        var cellDep = (DependencyObject)cb;
        while (cellDep != null && cellDep is not DataGridCell)
            cellDep = VisualTreeHelper.GetParent(cellDep);

        if (cellDep is not DataGridCell cell || !ReferenceEquals(cell.Column, TaskDoneColumn))
            return;

        if (cb.DataContext is not TaskRow row)
            return;

        var newValue = !row.Done;

        var selectedRows = TasksDataGrid.SelectedItems.Cast<object>().OfType<TaskRow>().ToList();
        if (selectedRows.Count >= 2)
        {
            foreach (var r in selectedRows) r.Done = newValue;
            e.Handled = true;
            return;
        }

        row.Done = newValue;

        _isPaintingDone = true;
        _paintDoneValue = newValue;

        Mouse.Capture(TasksDataGrid);
        e.Handled = true;
    }

    private void TasksGrid_PreviewMouseMove_DonePaint(object sender, WpfMouseEventArgs e)
    {
        if (!_isPageActive) return;

        if (!_isPaintingDone)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPainting();
            return;
        }

        var pos = Mouse.GetPosition(TasksDataGrid);
        var hit = TasksDataGrid.InputHitTest(pos) as DependencyObject;

        var dgRow = FindAncestor<DataGridRow>(hit);
        if (dgRow?.Item is TaskRow row)
        {
            if (row.Done != _paintDoneValue)
                row.Done = _paintDoneValue;
        }
    }

    private void TasksGrid_StopPaint(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;

        if (!_isPaintingDone) return;
        StopPainting();
    }

    private void StopPainting()
    {
        _isPaintingDone = false;

        TasksDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (Mouse.Captured == TasksDataGrid)
            Mouse.Capture(null);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = GetParentSafe(current);
        }
        return null;
    }

    private static DependencyObject? GetParentSafe(DependencyObject current)
    {
        try
        {
            if (current is Visual || current is Visual3D)
                return VisualTreeHelper.GetParent(current);

            return LogicalTreeHelper.GetParent(current);
        }
        catch
        {
            return null;
        }
    }

    private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current == null) return null;

        // ✅ Fix crash: FlowDocument / non-Visual => pas de VisualTree
        if (current is not Visual && current is not Visual3D)
            return null;

        int count;
        try { count = VisualTreeHelper.GetChildrenCount(current); }
        catch { return null; }

        for (int i = 0; i < count; i++)
        {
            DependencyObject child;
            try { child = VisualTreeHelper.GetChild(current, i); }
            catch { continue; }

            if (child is T match)
                return match;

            var deeper = FindDescendant<T>(child);
            if (deeper != null) return deeper;
        }

        return null;
    }

    // ============================================================
    // Stickers: palette
    // ============================================================

    private static readonly string[] StickerPalette =
    {
        "#EF4444", "#F97316", "#F59E0B", "#EAB308",
        "#84CC16", "#22C55E", "#10B981", "#14B8A6",
        "#06B6D4", "#3B82F6", "#6366F1", "#8B5CF6",
        "#A855F7", "#EC4899", "#F43F5E", "#111827"
    };

    public void CycleStickerColor(StickerItem sticker)
    {
        if (sticker == null) return;

        var cur = (sticker.ColorHex ?? "").Trim();
        var i = Array.FindIndex(StickerPalette, x => string.Equals(x, cur, StringComparison.OrdinalIgnoreCase));
        var next = (i < 0) ? StickerPalette[0] : StickerPalette[(i + 1) % StickerPalette.Length];
        sticker.ColorHex = next;
    }

    private void Sticker_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not StickerItem s) return;
        CycleStickerColor(s);
    }

    private void Sticker_RightClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        e.Handled = true;

        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not StickerItem s) return;

        ChooseStickerColor(s);
    }

    private void ChooseStickerColor(StickerItem sticker)
    {
        try
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };

            var (r, g, b) = ParseHexColor(sticker.ColorHex);
            dlg.Color = System.Drawing.Color.FromArgb(r, g, b);

            var ok = dlg.ShowDialog();
            if (ok != System.Windows.Forms.DialogResult.OK) return;

            var c = dlg.Color;
            sticker.ColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { }
    }

    private static (int r, int g, int b) ParseHexColor(string? hex)
    {
        var s = (hex ?? "").Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length != 6) return (0, 0, 0);

        int r = Convert.ToInt32(s.Substring(0, 2), 16);
        int g = Convert.ToInt32(s.Substring(2, 2), 16);
        int b = Convert.ToInt32(s.Substring(4, 2), 16);
        return (r, g, b);
    }

    // ============================================================
    // INotifyPropertyChanged (page)
    // ============================================================

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    // ============================================================
    // Constructor
    // ============================================================

    public PlanningPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;

        DataContext = this;

        // ✅ Culture explicite (19.07.2026) : cohérente avec le format personnalisé posé sur
        // StartDatePicker.Text/EndDatePicker.Text dans ApplyPlanningHeadersAndSyncDatePickers.
        var frCH = System.Windows.Markup.XmlLanguage.GetLanguage("fr-CH");
        StartDatePicker.Language = frCH;
        EndDatePicker.Language = frCH;

        // ✅ Guard page active/inactive (évite COMException quand on change de page)
        Loaded += (_, __) =>
        {
            _isPageActive = true;
        };

        Unloaded += (_, __) =>
        {
            // ✅ BUG (grilles planning/tâches perdues au changement de page) : ShowPlanning()
            // recrée une TOUTE NOUVELLE instance de PlanningPage à chaque navigation, ce qui
            // détruit l'ancienne (Unloaded se déclenche). Seule la navigation semaine
            // précédente/suivante appelait SaveCurrentWeekState() — changer d'onglet (Dashboard,
            // etc.) sans changer de semaine perdait donc silencieusement toute saisie récente
            // dans la grille des tâches et le planning hebdomadaire. On sauvegarde donc aussi ici.
            SaveCurrentWeekState();
            _isPageActive = false;
            _activeRtb = null;
        };

        AttachTaskRowsView();
        PlanningDataGrid.ItemsSource = _planningRows;
        // Forcer récréation des brushes lors du changement de projet :
        // on s'assure que les converters reçoivent une nouvelle instance du dictionnaire
        this.DataContextChanged += (_, __) =>
        {
            try { OnPropertyChanged(nameof(CompanyColorMap)); } catch { }
        };

        TasksDataGrid.PreviewMouseLeftButtonDown += DataGrid_PreviewMouseLeftButtonDown_FocusCell;
        PlanningDataGrid.PreviewMouseLeftButtonDown += DataGrid_PreviewMouseLeftButtonDown_FocusCell;

        TasksDataGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ComboBox_OpenOnSingleClick), true);
        PlanningDataGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ComboBox_OpenOnSingleClick), true);

        TasksDataGrid.PreviewKeyDown += CloseDropdownOnEscape;
        PlanningDataGrid.PreviewKeyDown += CloseDropdownOnEscape;

        TasksDataGrid.PreviewMouseLeftButtonDown += TasksGrid_PreviewMouseLeftButtonDown_DonePaint;
        TasksDataGrid.PreviewMouseMove += TasksGrid_PreviewMouseMove_DonePaint;
        TasksDataGrid.PreviewMouseLeftButtonUp += TasksGrid_StopPaint;
        TasksDataGrid.MouseLeave += TasksGrid_StopPaint;

        // ✅ roulette souris = scroll page
        TasksDataGrid.PreviewMouseWheel += ForwardMouseWheelToMainScrollViewer;
        PlanningDataGrid.PreviewMouseWheel += ForwardMouseWheelToMainScrollViewer;

        // ✅ clear sélection quand on clique ailleurs
        if (MainScrollViewer != null)
            MainScrollViewer.PreviewMouseDown += MainScrollViewer_PreviewMouseDown_ClearSelections;

        // ✅ charge / init la banque stickers persistée (JSON)
        EnsureStickerBankLoadedOrInitialized();

        // ✅ charge la banque d'images favorites persistée (JSON) — par projet.
        EnsureImageFavoritesLoaded();

        // ✅ Zones de texte : chargées PAR SEMAINE via LoadWeekState (voir
        // LoadTextZonesForWeek), appelé plus bas par Reload() -- pas ici.

        // ✅ init combo jour de départ (FR)
        if (StartWeekdayCombo != null)
        {
            StartWeekdayCombo.ItemsSource = new List<WeekdayOption>
            {
                new() { Label = "Lundi", Value = DayOfWeek.Monday },
                new() { Label = "Mardi", Value = DayOfWeek.Tuesday },
                new() { Label = "Mercredi", Value = DayOfWeek.Wednesday },
                new() { Label = "Jeudi", Value = DayOfWeek.Thursday },
                new() { Label = "Vendredi", Value = DayOfWeek.Friday },
            };
            StartWeekdayCombo.SelectedValue = _weekStartDay;
        }

        // ✅ Structure de la semaine (5/6 jours) : chargée par projet dans Reload() (voir
        // Db.GetPlanningWeekDayCount), pas ici -- une seule fois au constructeur ne suffirait
        // pas si l'utilisateur change de projet en cours de session.

        InitTextFormattingToolbar();

        Reload();
    }

    // ============================================================
    // Models
    // ============================================================

    public sealed class StickerItem : INotifyPropertyChanged
    {
        private string _label = "";
        private string _colorHex = "#F59E0B";
        private bool _isGradient = false;
        private string _companyName = "";

        public string Label { get => _label; set => SetField(ref _label, value); }

        // ✅ Lien invisible sticker <-> intervenant (25.07.2026), jamais montré dans l'UI --
        // le Label reste un numéro libre saisi par Joe, distinct de cette association.
        public string CompanyName { get => _companyName; set => SetField(ref _companyName, value); }

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (!SetField(ref _colorHex, value)) return;
                OnPropertyChanged(nameof(TextColorHex));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        // ✅ Sticker "Mix" (25.07.2026, demande de Joe) : suit le meme systeme degrade
        // optionnel que les couleurs entreprise/categorie (ColorGradientHelper).
        public bool IsGradient
        {
            get => _isGradient;
            set
            {
                if (!SetField(ref _isGradient, value)) return;
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public System.Windows.Media.Brush ColorBrush =>
            Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(ColorHex, IsGradient)
            ?? System.Windows.Media.Brushes.Transparent;

        public string TextColorHex => GetContrastingTextColorHex(ColorHex);

        public static string GetContrastingTextColorHex(string? bgHex)
        {
            if (string.IsNullOrWhiteSpace(bgHex))
                return "#FFFFFF";

            var s = bgHex.Trim();
            if (s.StartsWith("#")) s = s[1..];
            if (s.Length != 6) return "#FFFFFF";

            int r = Convert.ToInt32(s.Substring(0, 2), 16);
            int g = Convert.ToInt32(s.Substring(2, 2), 16);
            int b = Convert.ToInt32(s.Substring(4, 2), 16);

            double luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
            return luminance > 0.6 ? "#000000" : "#FFFFFF";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class PlacedStickerItem : INotifyPropertyChanged
    {
        private string _label = "";
        private string _colorHex = "#F59E0B";
        private bool _isGradient = false;
        private double _x;
        private double _y;
        private double _size = StickerDropDefaultSize;
        private double _width = StickerDropDefaultWidth;

        // ✅ NEW: mode édition (double-clic)
        private bool _isEditing;

        public string Label { get => _label; set => SetField(ref _label, value); }

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (!SetField(ref _colorHex, value)) return;
                OnPropertyChanged(nameof(TextColorHex));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public bool IsGradient
        {
            get => _isGradient;
            set
            {
                if (!SetField(ref _isGradient, value)) return;
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public System.Windows.Media.Brush ColorBrush =>
            Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(ColorHex, IsGradient)
            ?? System.Windows.Media.Brushes.Transparent;

        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Size { get => _size; set => SetField(ref _size, value); }

        // ✅ Largeur indépendante de la hauteur (Size) : sticker rectangulaire, assez
        // large pour afficher un numéro de tâche à 4 chiffres.
        public double Width { get => _width; set => SetField(ref _width, value); }

        public bool IsEditing { get => _isEditing; set => SetField(ref _isEditing, value); }

        public string TextColorHex => StickerItem.GetContrastingTextColorHex(ColorHex);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class PlacedPlanImageItem : INotifyPropertyChanged
    {
        private string _filePath = "";
        private ImageSource? _imageSource;
        private double _x;
        private double _y;
        private double _width = 360;
        private double _height = 240;
        private bool _isSelected;

        public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }
        public ImageSource? ImageSource { get => _imageSource; set => SetField(ref _imageSource, value); }

        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Width { get => _width; set => SetField(ref _width, value); }
        public double Height { get => _height; set => SetField(ref _height, value); }

        // ✅ Contour visuel (19.07.2026, demande de Joe) : indique quelle image est
        // sélectionnée pour "Retirer image" / "Ajouter aux favoris".
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class ImageFavoriteItem : INotifyPropertyChanged
    {
        private string _filePath = "";
        private ImageSource? _imageSource;
        private string _name = "";

        public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }
        public ImageSource? ImageSource { get => _imageSource; set => SetField(ref _imageSource, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class TextZoneItem : INotifyPropertyChanged
    {
        private bool _visible = true;
        private string _title = "";
        private string _documentXaml = "";

        public bool Visible { get => _visible; set => SetField(ref _visible, value); }
        public string Title { get => _title; set => SetField(ref _title, value); }

        public string DocumentXaml
        {
            get => _documentXaml;
            set => SetField(ref _documentXaml, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    private sealed class TaskRow : INotifyPropertyChanged
    {
        private string _ref = "";
        private string _company = "";
        private string _building = "";
        private string _floor = "";
        private string _todo = "";
        // ✅ Descriptif enrichi (25.07.2026, demande de Joe) : Todo reste le texte brut
        // (aperçu 1 ligne dans la grille, recherche/tri) ; ce champ stocke la mise en forme
        // + images éventuelles (même mécanisme que les zones de texte du plan), édité
        // uniquement via TaskDescriptionWindow. Vide -> pas de contenu enrichi (juste Todo).
        private string _todoDocumentXaml = "";
        private string _category = "";
        private string _reserve = "";
        private string _urgent = "";
        private bool _done;
        private bool _isSelected;
        // ✅ Date à laquelle "Effectué" a été coché (28.07.2026, demande de Joe) : sert à ne
        // plus reporter la tâche dans les semaines SUIVANT celle où elle a été terminée (voir
        // TaskRowVisibleInCurrentWeek). Effacée si la case est décochée (redevient "en cours",
        // visible partout comme avant).
        private DateTime? _doneAt;

        // ✅ Semaine actuellement consultée dans le Planning (28.07.2026, demande de Joe :
        // "tâche disparue") — maintenu à jour par PlanningPage (ApplyPlanningHeadersAndSync
        // DatePickers) à chaque changement de semaine. "Effectué" peut être coché en
        // consultant N'IMPORTE QUELLE semaine (passée ou future), pas forcément celle
        // d'aujourd'hui : DoneAt doit refléter CETTE semaine-là, pas la date réelle du jour,
        // sinon la tâche pouvait disparaître immédiatement (comparée à la mauvaise semaine).
        public static DateTime? CurrentViewedWeekStart;

        // ✅ Semaine de création (28.07.2026, demande de Joe) : symétrique de DoneAt — une
        // tâche ne se répercute plus dans les semaines PRÉCÉDANT celle-ci non plus. Null pour
        // les tâches créées avant cet ajout (comportement historique conservé : visibles
        // partout, y compris dans le passé).
        private DateTime? _createdWeekStart;

        public string Ref { get => _ref; set => SetField(ref _ref, value); }
        public string Company { get => _company; set => SetField(ref _company, value); }
        public string Building { get => _building; set => SetField(ref _building, value); }
        public string Floor { get => _floor; set => SetField(ref _floor, value); }
        public string Todo { get => _todo; set => SetField(ref _todo, value); }
        public string TodoDocumentXaml { get => _todoDocumentXaml; set => SetField(ref _todoDocumentXaml, value); }
        public string Category { get => _category; set => SetField(ref _category, value); }
        public string Reserve { get => _reserve; set => SetField(ref _reserve, value); }
        public string Urgent { get => _urgent; set => SetField(ref _urgent, value); }
        public DateTime? CreatedWeekStart { get => _createdWeekStart; set => SetField(ref _createdWeekStart, value); }

        // ✅ Une tâche marquée "Effectué" n'a plus besoin d'indicateur d'urgence — on
        // l'efface automatiquement dès que la case est cochée.
        public bool Done
        {
            get => _done;
            set
            {
                if (!SetField(ref _done, value)) return;
                if (value)
                {
                    Urgent = "";
                    DoneAt = CurrentViewedWeekStart ?? DateTime.Now;
                }
                else
                {
                    DoneAt = null;
                }
            }
        }

        public DateTime? DoneAt { get => _doneAt; set => SetField(ref _doneAt, value); }

        // ✅ Sélection via case à cocher (colonne de gauche) pour suppression multiple — non persistée.
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    private sealed class PlanningRow : INotifyPropertyChanged
    {
        private string _company = "";
        private string _d1 = "";
        private string _d2 = "";
        private string _d3 = "";
        private string _d4 = "";
        private string _d5 = "";
        private string _d6 = "";
        private string _sat = "";
        private string _sun = "";

        public string Company { get => _company; set => SetField(ref _company, value); }
        public string D1 { get => _d1; set => SetField(ref _d1, value); }
        public string D2 { get => _d2; set => SetField(ref _d2, value); }
        public string D3 { get => _d3; set => SetField(ref _d3, value); }
        public string D4 { get => _d4; set => SetField(ref _d4, value); }
        public string D5 { get => _d5; set => SetField(ref _d5, value); }
        public string D6 { get => _d6; set => SetField(ref _d6, value); }
        public string Sat { get => _sat; set => SetField(ref _sat, value); }
        public string Sun { get => _sun; set => SetField(ref _sun, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    // ============================================================
    // ✅ DTOs — persistance par semaine (JSON)
    // ============================================================

    private sealed class WeekStateFile
    {
        public string WeekKey { get; set; } = "";
        public List<TaskRowState> TaskRows { get; set; } = new();
        public List<PlanningRowState> PlanningRows { get; set; } = new();
        public List<PlacedStickerState> PlacedStickerStates { get; set; } = new();
        public List<PlacedImageState> PlacedImageStates { get; set; } = new();
        public bool ShowSaturday { get; set; }
        public bool ShowSunday { get; set; }

        // ✅ Zones de texte, indépendantes par semaine (19.07.2026, demande de Joe).
        public List<TextZoneState> TextZoneStates { get; set; } = new();
    }

    private sealed class TaskRowState
    {
        public string Ref { get; set; } = "";
        public string Company { get; set; } = "";
        public string Building { get; set; } = "";
        public string Floor { get; set; } = "";
        public string Todo { get; set; } = "";
        public string TodoDocumentXaml { get; set; } = "";
        public string Category { get; set; } = "";
        public string Reserve { get; set; } = "";
        public string Urgent { get; set; } = "";
        public bool Done { get; set; }
        public DateTime? DoneAt { get; set; }
        public DateTime? CreatedWeekStart { get; set; }
    }

    private sealed class PlanningRowState
    {
        public string Company { get; set; } = "";
        public string D1 { get; set; } = "";
        public string D2 { get; set; } = "";
        public string D3 { get; set; } = "";
        public string D4 { get; set; } = "";
        public string D5 { get; set; } = "";
        public string D6 { get; set; } = "";
        public string Sat { get; set; } = "";
        public string Sun { get; set; } = "";
    }

    private sealed class PlacedStickerState
    {
        public string Label { get; set; } = "";
        public string ColorHex { get; set; } = "#F59E0B";
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; set; } = 24;
        // ✅ Absent des anciens fichiers (avant les stickers rectangulaires) : la valeur
        // par défaut ci-dessous s'applique alors automatiquement à la désérialisation.
        public double Width { get; set; } = StickerDropDefaultWidth;
        public bool IsGradient { get; set; } = false;
    }

    private sealed class PlacedImageState
    {
        public string FilePath { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 360;
        public double Height { get; set; } = 240;
    }
}

// ============================================================
// ✅ Converters (TOP-LEVEL pour être trouvés par XAML)
// ============================================================

// ✅ Colore la colonne "Urg." selon la valeur sélectionnée (1=rouge, 2=orange,
// 3=jaune clair) — converter au lieu de DataTrigger : binding direct, sans dépendre
// du timing de sélection d'un ComboBox éditable (IsEditable="True" sur GridComboStyle).
public sealed class UrgencyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        return s switch
        {
            "1" => new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#DC2626")),
            "2" => new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FED7AA")),
            "3" => new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FEF9C3")),
            _ => WpfBrushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ✅ Texte en blanc sur fond rouge vif (valeur "1"), foncé sinon — voir UrgencyToBrushConverter.
public sealed class UrgencyToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        return s == "1" ? WpfBrushes.White : new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#111827"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ✅ Bouton "Voir +" / crayon "Descriptif" (saisi via l'éditeur agrandi, TaskDescriptionWindow).
// Seuil corrigé le 16.07.2026 (demande de Joe) : "plus d'une ligne" (donc 2+), pas "plus de
// 2 lignes" comme dans la 1ère version.
internal static class TodoLineCountHelper
{
    public static bool HasMoreThanOneLine(object value)
    {
        var s = value as string;
        if (string.IsNullOrEmpty(s)) return false;
        return s.Replace("\r\n", "\n").Split('\n').Length > 1;
    }
}

// ✅ Colonne "Descriptif" (grille) : n'affiche que la 1ère ligne en mode lecture, même si
// le texte complet (saisi via l'éditeur agrandi) contient plusieurs lignes.
public sealed class TodoFirstLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrEmpty(s)) return "";
        var firstLine = s.Replace("\r\n", "\n").Split('\n')[0];
        return firstLine;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ✅ Bouton "agrandir le Descriptif" : crayon si 1 seule ligne, texte "Voir +" si plus d'une
// ligne (voir TodoShowVoirPlusConverter) — pas de couleur de fond dans les deux cas.
public sealed class TodoShowPencilConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TodoLineCountHelper.HasMoreThanOneLine(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TodoShowVoirPlusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TodoLineCountHelper.HasMoreThanOneLine(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CompanyNameToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var name = (values.Length > 0 ? values[0] : null) as string;
            var map = (values.Length > 1 ? values[1] : null) as Dictionary<string, string>;

            if (string.IsNullOrWhiteSpace(name) || map == null)
                return WpfBrushes.Transparent;

            if (!map.TryGetValue(name.Trim(), out var hex))
                return WpfBrushes.Transparent;

            if (string.IsNullOrWhiteSpace(hex))
                return WpfBrushes.Transparent;

            var pid = Db.GetCurrentProjectId();
            var isGradient = pid.HasValue && pid.Value > 0 && Db.GetCompanyIsGradient(pid.Value, name.Trim());
            return Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex.Trim(), isGradient) ?? WpfBrushes.Transparent;
        }
        catch
        {
            return WpfBrushes.Transparent;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CompanyNameToTextBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var name = (values.Length > 0 ? values[0] : null) as string;
            var map = (values.Length > 1 ? values[1] : null) as Dictionary<string, string>;

            if (string.IsNullOrWhiteSpace(name) || map == null)
                return WpfBrushes.Black;

            if (!map.TryGetValue(name.Trim(), out var hex))
                return WpfBrushes.Black;

            var c = (WpfColor)WpfColorConverter.ConvertFromString(hex.Trim());

            double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return luminance > 0.6 ? WpfBrushes.Black : WpfBrushes.White;
        }
        catch
        {
            return WpfBrushes.Black;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CellTextAndCompanyToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var cellText = (values.Length > 0 ? values[0] : null) as string;
            var company = (values.Length > 1 ? values[1] : null) as string;
            var map = (values.Length > 2 ? values[2] : null) as Dictionary<string, string>;

            if (string.IsNullOrWhiteSpace(cellText))
                return WpfBrushes.Transparent;

            if (string.IsNullOrWhiteSpace(company) || map == null)
                return WpfBrushes.Transparent;

            if (!map.TryGetValue(company.Trim(), out var hex) || string.IsNullOrWhiteSpace(hex))
                return WpfBrushes.Transparent;

            var pid = Db.GetCurrentProjectId();
            var isGradient = pid.HasValue && pid.Value > 0 && Db.GetCompanyIsGradient(pid.Value, company.Trim());
            return Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(hex.Trim(), isGradient) ?? WpfBrushes.Transparent;
        }
        catch
        {
            return WpfBrushes.Transparent;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CellTextAndCompanyToTextBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var cellText = (values.Length > 0 ? values[0] : null) as string;
            var company = (values.Length > 1 ? values[1] : null) as string;
            var map = (values.Length > 2 ? values[2] : null) as Dictionary<string, string>;

            if (string.IsNullOrWhiteSpace(cellText))
                return WpfBrushes.Black;

            if (string.IsNullOrWhiteSpace(company) || map == null)
                return WpfBrushes.Black;

            if (!map.TryGetValue(company.Trim(), out var hex) || string.IsNullOrWhiteSpace(hex))
                return WpfBrushes.Black;

            var c = (WpfColor)WpfColorConverter.ConvertFromString(hex.Trim());

            double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return luminance > 0.6 ? WpfBrushes.Black : WpfBrushes.White;
        }
        catch
        {
            return WpfBrushes.Black;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}