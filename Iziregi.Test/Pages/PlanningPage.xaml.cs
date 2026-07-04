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
using Iziregi.Test.Data;
using Iziregi.Test.Services;

namespace Iziregi.Test.Pages;

// ✅ Alias WPF (évite ambiguïtés avec System.Drawing / WinForms)
using WpfUserControl = System.Windows.Controls.UserControl;
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

    // ✅ Couleurs entreprise (par projet)
    public Dictionary<string, string> CompanyColorMap { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<TaskRow> _taskRows = new();
    private readonly ObservableCollection<PlanningRow> _planningRows = new();

    private bool _isSaturdayVisible = false;

    // ✅ jour de départ configurable (par l’utilisateur)
    private DayOfWeek _weekStartDay = DayOfWeek.Monday;

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

    private const double StickerDropDefaultSize = 26;
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
            // Mise à jour UI
            try { this.Dispatcher?.Invoke(() => { this.UpdateLayout(); }); } catch { }
            _isPageActive = prev;
        }
        catch
        {
            // non bloquant
        }
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

            TasksDataGrid.ItemsSource = _taskRows;
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

        if (e.PropertyName == nameof(StickerItem.ColorHex) || e.PropertyName == nameof(StickerItem.Label))
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
                            ColorHex = string.IsNullOrWhiteSpace(it?.ColorHex) ? "#F59E0B" : it!.ColorHex
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
                    ColorHex = string.IsNullOrWhiteSpace(s?.ColorHex) ? "#F59E0B" : s!.ColorHex
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

    private sealed class TextZoneBankState
    {
        public int Version { get; set; } = 1;
        public List<TextZoneState> Zones { get; set; } = new();
    }

    private sealed class TextZoneState
    {
        public bool Visible { get; set; }
        public string Title { get; set; } = "";
        public string DocumentXaml { get; set; } = "";
    }

    private static string GetTextZonesFilePath(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture)
            : "0";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi",
            "Planning");

        return Path.Combine(dir, $"planning-textzones-{pid}.json");
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

        // Visible, Title ou DocumentXaml (saisie de texte) : on sauvegarde à chaque fois,
        // comme pour la banque de stickers.
        SaveTextZonesToDisk();
    }

    private void EnsureTextZonesLoadedOrInitialized()
    {
        _isLoadingTextZones = true;
        try
        {
            var pid = Db.GetCurrentProjectId();
            var filePath = GetTextZonesFilePath(pid);

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<TextZoneBankState>(json);

                if (state?.Zones != null && state.Zones.Count > 0)
                {
                    TextZones.Clear();

                    foreach (var it in state.Zones)
                    {
                        TextZones.Add(new TextZoneItem
                        {
                            Visible = it?.Visible ?? false,
                            Title = it?.Title ?? "",
                            DocumentXaml = it?.DocumentXaml ?? ""
                        });
                    }

                    // Sécurité : toujours exactement 4 zones (Rtb0..Rtb3)
                    while (TextZones.Count < 4)
                        TextZones.Add(new TextZoneItem { Visible = false });

                    while (TextZones.Count > 4)
                        TextZones.RemoveAt(TextZones.Count - 1);

                    return;
                }
            }

            // Sinon : init par défaut (2 premières zones visibles) + sauvegarde
            TextZones.Clear();
            TextZones.Add(new TextZoneItem { Visible = true });
            TextZones.Add(new TextZoneItem { Visible = true });
            TextZones.Add(new TextZoneItem { Visible = false });
            TextZones.Add(new TextZoneItem { Visible = false });

            SaveTextZonesToDisk();
        }
        catch (Exception ex)
        {
            LogPlanningError("EnsureTextZonesLoadedOrInitialized", ex);
            // Fallback sans crash
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

    private void SaveTextZonesToDisk()
    {
        try
        {
            var pid = Db.GetCurrentProjectId();
            var filePath = GetTextZonesFilePath(pid);

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var state = new TextZoneBankState
            {
                Version = 1,
                Zones = TextZones.Select(z => new TextZoneState
                {
                    Visible = z?.Visible ?? false,
                    Title = z?.Title ?? "",
                    DocumentXaml = z?.DocumentXaml ?? ""
                }).ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            LogPlanningError("SaveTextZonesToDisk", ex);
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

        // ---- Section Tâches : cacher boutons Ajouter/Supprimer
        HideElementForCapture(AddTaskRowButton, restore);
        HideElementForCapture(RemoveTaskRowButton, restore);

        // ---- Section Planning hebdomadaire : cacher boutons Ajouter/Supprimer + samedi
        HideElementForCapture(AddPlanningRowButton, restore);
        HideElementForCapture(RemovePlanningRowButton, restore);
        HideElementForCapture(ToggleSaturdayButton, restore);

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

    private void CompactRichTextBoxDocument(WpfRichTextBox rtb)
    {
        if (rtb.Document == null)
            rtb.Document = new FlowDocument();

        rtb.Document.LineHeight = RtbDefaultLineHeight;
        rtb.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        rtb.Document.PagePadding = new Thickness(0);

        foreach (var p in rtb.Document.Blocks.OfType<Paragraph>())
        {
            p.Margin = ParagraphZeroMargin;
            p.LineHeight = RtbDefaultLineHeight;
            p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
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
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
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
            PlacedPlanImages.Remove(_selectedPlacedPlanImage);
            _selectedPlacedPlanImage = null;
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

    private void TryAddPlacedPlanImage(string filePath)
    {
        try
        {
            var path = (filePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var it = new PlacedPlanImageItem
            {
                FilePath = path,
                ImageSource = bmp,
                X = 20 + (PlacedPlanImages.Count * 20),
                Y = 20 + (PlacedPlanImages.Count * 20),
                Width = 360,
                Height = 240
            };

            PlacedPlanImages.Add(it);
            _selectedPlacedPlanImage = it;
        }
        catch { }
    }

    // ============================================================
    // PlanCanvas (stickers drop)
    // ============================================================

    private void PlanCanvas_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
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
            Size = StickerDropDefaultSize,
            X = Math.Max(0, x - StickerDropDefaultSize / 2),
            Y = Math.Max(0, y - StickerDropDefaultSize / 2),
        });
    }

    private void CompanyColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selectedBankSticker == null)
                return;

            if (sender is FrameworkElement fe && fe.DataContext is KeyValuePair<string, string> kv)
            {
                var hex = (kv.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(hex)) return;

                _selectedBankSticker.ColorHex = hex;
            }
        }
        catch
        {
        }
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

    private void PlacedPlanImage_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPageActive) return;

        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return;

        if (sender is FrameworkElement fe && fe.DataContext is PlacedPlanImageItem it)
        {
            _selectedPlacedPlanImage = it;

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
            if (_selectedPlacedPlanImage == it) _selectedPlacedPlanImage = null;
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

        var z = TextZones[SelectedTextZoneIndex];
        if (!z.Visible) return;

        z.Visible = false;
        SelectedTextZoneIndex = FindFirstVisibleTextZoneIndex();
    }

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

        if (_rtbInternalUpdate) return;
        if (ReferenceEquals(rtb, _activeRtb)) UpdateToolbarFromSelection();
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
            _taskRows.Add(new TaskRow { Ref = nextRef.ToString() });
        }

        TasksDataGrid.ScrollIntoView(_taskRows.Last());
    }

    private void RemoveTaskRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        var toRemove = TasksDataGrid.SelectedItems.Cast<object>().OfType<TaskRow>().ToList();
        if (toRemove.Count == 0 && TasksDataGrid.SelectedItem is TaskRow one) toRemove.Add(one);

        foreach (var r in toRemove)
            _taskRows.Remove(r);
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

    private void ToggleSaturdayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPageActive) return;

        _isSaturdayVisible = !_isSaturdayVisible;

        if (SaturdayColumn != null)
            SaturdayColumn.Visibility = _isSaturdayVisible ? Visibility.Visible : Visibility.Collapsed;

        SetToggleSaturdayButtonUi();
        PlanningDataGrid?.UpdateLayout();
    }

    private void SetToggleSaturdayButtonUi()
    {
        ToggleSaturdayButton.Content = _isSaturdayVisible ? "Masquer samedi" : "Afficher samedi";
        ToggleSaturdayButton.Style = (Style)Resources[_isSaturdayVisible ? "SmallBlackButtonStyle" : "SmallBlueButtonStyle"];
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
            TaskRows = _taskRows.Select(r => new TaskRowState
            {
                Ref = r.Ref,
                Company = r.Company,
                Building = r.Building,
                Floor = r.Floor,
                Todo = r.Todo,
                Category = r.Category,
                Reserve = r.Reserve,
                Done = r.Done
            }).ToList(),
            PlanningRows = _planningRows.Select(r => new PlanningRowState
            {
                Company = r.Company,
                D1 = r.D1,
                D2 = r.D2,
                D3 = r.D3,
                D4 = r.D4,
                D5 = r.D5,
                D6 = r.D6,
                Sat = r.Sat
            }).ToList(),
            PlacedStickerStates = PlacedStickers.Select(s => new PlacedStickerState
            {
                Label = s.Label,
                ColorHex = s.ColorHex,
                X = s.X,
                Y = s.Y,
                Size = s.Size
            }).ToList(),
            PlacedImageStates = PlacedPlanImages.Select(i => new PlacedImageState
            {
                FilePath = i.FilePath,
                X = i.X,
                Y = i.Y,
                Width = i.Width,
                Height = i.Height
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
        }
        catch { }
    }

    private void LoadWeekState(DateTime weekStart)
    {
        var weekKey = GetWeekKey(weekStart);
        var filePath = GetWeekFilePath(weekKey);

        _taskRows.Clear();
        _planningRows.Clear();
        PlacedStickers.Clear();
        PlacedPlanImages.Clear();

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var state = JsonSerializer.Deserialize<WeekStateFile>(json, opts);

                if (state != null)
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
                            Done = r.Done
                        });

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
                            Sat = r.Sat
                        });

                    foreach (var s in state.PlacedStickerStates)
                        PlacedStickers.Add(new PlacedStickerItem
                        {
                            Label = s.Label,
                            ColorHex = s.ColorHex,
                            X = s.X,
                            Y = s.Y,
                            Size = s.Size
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

        // 0 = Entreprise
        // 1 = D1
        // 2 = D2
        // 3 = D3
        // 4 = D4
        // 5 = D5 (vendredi)
        // 6 = Samedi (SaturdayColumn)
        // 7 = D6 (lundi suivant)
        if (PlanningDataGrid.Columns.Count < 8)
            return;

        var start = SnapToStartOfWeek(_startDay, _weekStartDay);
        var businessDays = BuildSixBusinessDays(start);

        PlanningDataGrid.Columns[1].Header = HeaderForDay(businessDays[0]);
        PlanningDataGrid.Columns[2].Header = HeaderForDay(businessDays[1]);
        PlanningDataGrid.Columns[3].Header = HeaderForDay(businessDays[2]);
        PlanningDataGrid.Columns[4].Header = HeaderForDay(businessDays[3]);
        PlanningDataGrid.Columns[5].Header = HeaderForDay(businessDays[4]);

        var saturdayDate = NextDayOfWeek(businessDays[4], DayOfWeek.Saturday);
        SaturdayColumn.Header = HeaderForDay(saturdayDate);

        PlanningDataGrid.Columns[7].Header = HeaderForDay(businessDays[5]);

        _isSyncingDates = true;
        try
        {
            StartDatePicker.SelectedDate = businessDays[0];
            EndDatePicker.SelectedDate = businessDays[5];
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

    private static List<DateTime> BuildSixBusinessDays(DateTime start)
    {
        var res = new List<DateTime>(6);
        var d = start.Date;
        while (res.Count < 6)
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

    private static string HeaderForDay(DateTime d)
    {
        var fr = CultureInfo.GetCultureInfo("fr-CH");
        var name = fr.DateTimeFormat.GetDayName(d.DayOfWeek);
        name = char.ToUpper(name[0]) + name.Substring(1);
        return $"{name} {d:dd.MM}";
    }

    public void Reload()
    {
        if (!_isPageActive) return;

        var start = SnapToStartOfWeek(DateTime.Today, _weekStartDay);

        if (StartDatePicker.SelectedDate == null)
            StartDatePicker.SelectedDate = start;

        _startDay = SnapToStartOfWeek(StartDatePicker.SelectedDate ?? start, _weekStartDay);

        LoadLists();
        EnsureStickerBankLoadedOrInitialized();

        // Charger l'état de la semaine depuis le fichier (première ouverture ou retour sur la page)
        if (_taskRows.Count == 0 && _planningRows.Count == 0
            && PlacedStickers.Count == 0 && PlacedPlanImages.Count == 0)
            LoadWeekState(_startDay);
        else
            EnsureDefaultRows();

        _isSaturdayVisible = false;
        SaturdayColumn.Visibility = Visibility.Collapsed;
        SetToggleSaturdayButtonUi();

        ApplyPlanningHeadersAndSyncDatePickers();
    }

    private void LoadLists()
    {
        var pid = Db.GetCurrentProjectId();
        var p = (pid.HasValue && pid.Value > 0) ? pid.Value : 0;

        Companies = p > 0 ? Db.GetCompanies(p) : new List<string>();
        Buildings = p > 0 ? Db.GetPlaces(p) : new List<string>();
        Floors = p > 0 ? Db.GetEtages(p) : new List<string>();
        PlanningTextZones = p > 0 ? Db.GetPlanningTextZones(p) : new List<string>();
        Reserves = p > 0 ? Db.GetReserves(p) : new List<string>();

        CompanyColorMap = p > 0
            ? Db.GetCompanyColorMap(p)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Diagnostic temporaire supprimé.


        OnPropertyChanged(nameof(Companies));
        OnPropertyChanged(nameof(Buildings));
        OnPropertyChanged(nameof(Floors));
        OnPropertyChanged(nameof(PlanningTextZones));
        OnPropertyChanged(nameof(Reserves));
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

        TasksDataGrid.ItemsSource = _taskRows;
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

        // ✅ charge / init les zones de texte persistées (JSON) — avant ce correctif,
        // elles étaient réinitialisées vides à chaque navigation vers cette page.
        EnsureTextZonesLoadedOrInitialized();

        SelectedTextZoneIndex = FindFirstVisibleTextZoneIndex();

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

        InitTextFormattingToolbar();

        RestoreZoneXamlToRtb(0, Rtb0);
        RestoreZoneXamlToRtb(1, Rtb1);
        RestoreZoneXamlToRtb(2, Rtb2);
        RestoreZoneXamlToRtb(3, Rtb3);

        Reload();
    }

    // ============================================================
    // Models
    // ============================================================

    public sealed class StickerItem : INotifyPropertyChanged
    {
        private string _label = "";
        private string _colorHex = "#F59E0B";

        public string Label { get => _label; set => SetField(ref _label, value); }

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (!SetField(ref _colorHex, value)) return;
                OnPropertyChanged(nameof(TextColorHex));
            }
        }

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
        private double _x;
        private double _y;
        private double _size = StickerDropDefaultSize;

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
            }
        }

        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Size { get => _size; set => SetField(ref _size, value); }

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

        public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }
        public ImageSource? ImageSource { get => _imageSource; set => SetField(ref _imageSource, value); }

        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Width { get => _width; set => SetField(ref _width, value); }
        public double Height { get => _height; set => SetField(ref _height, value); }

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
        private string _category = "";
        private string _reserve = "";
        private bool _done;

        public string Ref { get => _ref; set => SetField(ref _ref, value); }
        public string Company { get => _company; set => SetField(ref _company, value); }
        public string Building { get => _building; set => SetField(ref _building, value); }
        public string Floor { get => _floor; set => SetField(ref _floor, value); }
        public string Todo { get => _todo; set => SetField(ref _todo, value); }
        public string Category { get => _category; set => SetField(ref _category, value); }
        public string Reserve { get => _reserve; set => SetField(ref _reserve, value); }
        public bool Done { get => _done; set => SetField(ref _done, value); }

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

        public string Company { get => _company; set => SetField(ref _company, value); }
        public string D1 { get => _d1; set => SetField(ref _d1, value); }
        public string D2 { get => _d2; set => SetField(ref _d2, value); }
        public string D3 { get => _d3; set => SetField(ref _d3, value); }
        public string D4 { get => _d4; set => SetField(ref _d4, value); }
        public string D5 { get => _d5; set => SetField(ref _d5, value); }
        public string D6 { get => _d6; set => SetField(ref _d6, value); }
        public string Sat { get => _sat; set => SetField(ref _sat, value); }

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
    }

    private sealed class TaskRowState
    {
        public string Ref { get; set; } = "";
        public string Company { get; set; } = "";
        public string Building { get; set; } = "";
        public string Floor { get; set; } = "";
        public string Todo { get; set; } = "";
        public string Category { get; set; } = "";
        public string Reserve { get; set; } = "";
        public bool Done { get; set; }
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
    }

    private sealed class PlacedStickerState
    {
        public string Label { get; set; } = "";
        public string ColorHex { get; set; } = "#F59E0B";
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; set; } = 24;
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

            var c = (WpfColor)WpfColorConverter.ConvertFromString(hex.Trim());
            return new WpfSolidColorBrush(c);
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

            var c = (WpfColor)WpfColorConverter.ConvertFromString(hex.Trim());
            return new WpfSolidColorBrush(c);
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