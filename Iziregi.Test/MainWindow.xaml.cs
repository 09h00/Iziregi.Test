// File: MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Pages;
using Microsoft.Win32;

// ✅ Fix ambiguïté System.Drawing vs WPF
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushConverter = System.Windows.Media.BrushConverter;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Iziregi.Test;

public partial class MainWindow : Window
{
    // =========================
    // Contexte partagé
    // =========================
    private Project? _selectedProject;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    // =========================
    // INBOX watcher
    // =========================
    private static string InboxDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "INBOX");

    private static string ImportedDir => Path.Combine(InboxDir, "Imported");
    private static string ErrorDir => Path.Combine(InboxDir, "Error");

    private FileSystemWatcher? _inboxWatcher;
    private readonly Queue<string> _pendingInboxFiles = new();
    private bool _isProcessingInboxQueue = false;

    // =========================
    // Pages (UserControls)
    // =========================
    private DashboardPage? _dashboardPage;
    private AccountingPage? _accountingPage;
    private ArchivesPage? _archivesPage;
    private TrashPage? _trashPage;
    private ListsPage? _listsPage;
    private PlanningPage? _planningPage;

    public MainWindow()
    {
        InitializeComponent();

        // DB init
        Db.Init();

        // ✅ Restaurer le projet courant depuis Settings (si existant)
        _selectedProject = Db.GetCurrentProject();
        Db.SetCurrentProjectId(_selectedProject?.Id);

        // Seeds de base (si vide) - dépend du projet courant
        Db.SeedPlacesIfEmpty("D20", "D21", "Extérieur");
        Db.SeedCompaniesIfEmpty("Electricien", "Sanitaire", "Ventilation");
        Db.SeedRequestersIfEmpty("Architecte");

        // Watcher INBOX
        StartInboxWatcher();

        // ✅ Badge projet
        UpdateProjectBadge(show: false); // Dashboard par défaut => caché

        // Page par défaut
        ShowDashboard();
    }

    // =========================
    // Badge projet (affiché sauf Dashboard)
    // =========================
    private static MediaBrush BrushFromHexOrNull(string? hex)
    {
        try
        {
            var s = (hex ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return MediaBrushes.Transparent;

            if (!s.StartsWith("#", StringComparison.Ordinal))
                s = "#" + s;

            var obj = new MediaBrushConverter().ConvertFromString(s);
            return obj is MediaBrush b ? b : MediaBrushes.Transparent;
        }
        catch
        {
            return MediaBrushes.Transparent;
        }
    }

    private static bool TryGetSolidColor(MediaBrush? brush, out MediaColor color)
    {
        if (brush is MediaSolidColorBrush scb)
        {
            color = scb.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static MediaBrush GetTextBrushForBackground(MediaBrush? bg)
    {
        if (bg == null)
            return MediaBrushes.White;

        if (!TryGetSolidColor(bg, out var c))
            return MediaBrushes.White;

        // luminance relative
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // clair => noir, foncé => blanc
        return luminance >= 0.55 ? MediaBrushes.Black : MediaBrushes.White;
    }

    private void UpdateProjectBadge(bool show)
    {
        if (ProjectBadgeContainer != null)
            ProjectBadgeContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
            return;

        var p = Db.GetCurrentProject();

        if (ProjectBadgeTextBlock != null)
        {
            ProjectBadgeTextBlock.Text = p != null
                ? $"Projet : {p.Name}"
                : "Projet : (aucun)";
        }

        // ✅ Couleur du badge (fond) + contraste auto (texte)
        if (ProjectBadgeContainer != null)
        {
            var bg = p != null ? BrushFromHexOrNull(p.ColorHex) : MediaBrushes.Transparent;

            // fallback si pas de couleur -> couleur par défaut existante
            if (bg == null || bg == MediaBrushes.Transparent)
                bg = BrushFromHexOrNull("#111827");

            ProjectBadgeContainer.Background = bg;

            if (ProjectBadgeTextBlock != null)
                ProjectBadgeTextBlock.Foreground = GetTextBrushForBackground(bg);
        }
    }

    // =========================
    // Navigation menu handlers
    // =========================
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void NavAccounting_Click(object sender, RoutedEventArgs e) => ShowAccounting();
    private void NavArchives_Click(object sender, RoutedEventArgs e) => ShowArchives();
    private void NavTrash_Click(object sender, RoutedEventArgs e) => ShowTrash();
    private void NavLists_Click(object sender, RoutedEventArgs e) => ShowLists();
    private void NavPlanning_Click(object sender, RoutedEventArgs e) => ShowPlanning();

    private void ShowDashboard()
    {
        _dashboardPage ??= new DashboardPage(this);
        MainContent.Content = _dashboardPage;
        _dashboardPage.Reload();

        UpdateProjectBadge(show: false);
    }

    private void ShowAccounting()
    {
        _accountingPage ??= new AccountingPage(this);
        MainContent.Content = _accountingPage;
        _accountingPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void ShowArchives()
    {
        _archivesPage ??= new ArchivesPage(this);
        MainContent.Content = _archivesPage;
        _archivesPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void ShowTrash()
    {
        _trashPage ??= new TrashPage(this);
        MainContent.Content = _trashPage;
        _trashPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void ShowLists()
    {
        _listsPage ??= new ListsPage(this);
        MainContent.Content = _listsPage;
        _listsPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void ShowPlanning()
    {
        _planningPage ??= new PlanningPage(this);
        MainContent.Content = _planningPage;
        _planningPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // =========================
    // ✅ Handler manquant (MainWindow.xaml -> Click="PlanningPdf_Click")
    // =========================
    private void PlanningPdf_Click(object sender, RoutedEventArgs e)
    {
        // Pour l’instant : handler "placeholder" pour compiler.
        // (On branchera ensuite sur le vrai export PDF du planning.)
        System.Windows.MessageBox.Show(
            this,
            "Export PDF planning : TODO",
            "Planning",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // =========================
    // API appelée depuis les pages
    // =========================
    public Project? GetSelectedProject() => _selectedProject;

    public void SetSelectedProject(Project? p)
    {
        _selectedProject = p;
        Db.SetCurrentProjectId(_selectedProject?.Id);

        // Comme on change de projet, on met à jour le badge si visible
        var show = ProjectBadgeContainer != null && ProjectBadgeContainer.Visibility == Visibility.Visible;
        UpdateProjectBadge(show: show);
    }

    public List<WorkOrder> GetAllWorkOrders() => Db.GetWorkOrders();

    public void OpenWorkOrder(long workOrderId, WorkOrderEditMode mode)
    {
        var w = new WorkOrderWindow(workOrderId, mode) { Owner = this };
        w.ShowDialog();

        if (MainContent.Content is IReloadablePage p)
            p.Reload();
    }

    public void ChooseProject()
    {
        var w = new ChooseProjectWindow { Owner = this };
        var ok = w.ShowDialog();
        if (ok == true && w.SelectedProject != null)
        {
            _selectedProject = w.SelectedProject;
            Db.SetCurrentProjectId(_selectedProject.Id);
        }

        // Mise à jour badge si visible
        var show = ProjectBadgeContainer != null && ProjectBadgeContainer.Visibility == Visibility.Visible;
        UpdateProjectBadge(show: show);

        if (MainContent.Content is IReloadablePage p)
            p.Reload();
    }

    // =========================
    // ✅ Création nouveau bon (utilise les valeurs par défaut configurées)
    // =========================
    public void CreateNewWorkOrderAndOpen()
    {
        var projectId = Db.GetCurrentProjectId();
        _selectedProject = projectId.HasValue ? Db.GetProjectById(projectId.Value) : null;

        // Lieu par défaut
        var defaultPlace = projectId.HasValue ? (Db.GetDefaultPlace(projectId.Value) ?? "").Trim() : "";
        var place = !string.IsNullOrWhiteSpace(defaultPlace)
            ? defaultPlace
            : (projectId.HasValue ? (Db.GetPlaces(projectId.Value).FirstOrDefault() ?? "D21") : "D21");

        // Entreprise par défaut
        var defaultCompany = projectId.HasValue ? (Db.GetDefaultCompany(projectId.Value) ?? "").Trim() : "";
        var performedBy = !string.IsNullOrWhiteSpace(defaultCompany)
            ? defaultCompany
            : (projectId.HasValue ? (Db.GetCompanies(projectId.Value).FirstOrDefault() ?? "Electricien") : "Electricien");

        // Demandé par par défaut
        var defaultRequester = projectId.HasValue ? (Db.GetDefaultRequester(projectId.Value) ?? "").Trim() : "";
        var requestedBy = !string.IsNullOrWhiteSpace(defaultRequester)
            ? defaultRequester
            : "Architecte";

        var wo = new WorkOrder
        {
            BdrNumber = projectId.HasValue ? Db.GetNextBdrNumberForProject(projectId.Value) : Db.GetNextBdrNumber(),
            Place = place,
            RequestedBy = requestedBy,
            PerformedBy = performedBy,
            RequestDate = DateTime.Today,

            Description = "",

            IsInCreation = false,
            IsSentToCompany = false,
            IsQuoteReceived = false,
            IsSentToSigner = false,
            IsValidated = false,
            IsValidatedPdfSent = false,

            IsPerformed = false,
            IsCancelled = false,

            IsTrashed = false,
            TrashedAt = null,
            IsArchived = false,
            ArchivedAt = null,

            LaborHours = 0,
            LaborRate = 0,
            TravelQty = 0,
            TravelRate = 0,
            TvaRate = 8.1,
            QuoteNotes = "",

            ProjectId = projectId
        };

        Db.InsertWorkOrder(wo);

        var created = projectId.HasValue ? Db.GetWorkOrders(projectId.Value).FirstOrDefault() : Db.GetWorkOrders().FirstOrDefault();
        if (created != null)
            Db.InsertWorkOrderLine(created.Id, "", 0, 0);

        created = projectId.HasValue ? Db.GetWorkOrders(projectId.Value).FirstOrDefault() : Db.GetWorkOrders().FirstOrDefault();
        if (created == null) return;

        OpenWorkOrder(created.Id, WorkOrderEditMode.Architecte);
    }

    // =========================
    // Import manuel (boutons de page Dashboard)
    // =========================
    public void ImportCompanyQuoteReply_ManualPicker()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer retour entreprise (devis)",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erreur import devis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ImportSignerReply_ManualPicker()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer retour signataire",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erreur import signataire", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================
    // Watcher setup
    // =========================
    private void StartInboxWatcher()
    {
        try
        {
            Directory.CreateDirectory(InboxDir);
            Directory.CreateDirectory(ImportedDir);
            Directory.CreateDirectory(ErrorDir);

            _inboxWatcher = new FileSystemWatcher(InboxDir, "*.iziregi-reponse")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
            };

            _inboxWatcher.Created += (_, e) => OnInboxFileDetected(e.FullPath);
            _inboxWatcher.Renamed += (_, e) => OnInboxFileDetected(e.FullPath);

            _inboxWatcher.EnableRaisingEvents = true;

            foreach (var f in Directory.GetFiles(InboxDir, "*.iziregi-reponse"))
                OnInboxFileDetected(f);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erreur INBOX", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        try
        {
            if (_inboxWatcher != null)
            {
                _inboxWatcher.EnableRaisingEvents = false;
                _inboxWatcher.Dispose();
                _inboxWatcher = null;
            }
        }
        catch { }
    }

    private void OnInboxFileDetected(string fullPath)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            if (!File.Exists(fullPath))
                return;

            if (_pendingInboxFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                return;

            _pendingInboxFiles.Enqueue(fullPath);

            if (!_isProcessingInboxQueue)
                ProcessInboxQueue();
        }));
    }

    private void ProcessInboxQueue()
    {
        if (_isProcessingInboxQueue)
            return;

        _isProcessingInboxQueue = true;

        try
        {
            while (_pendingInboxFiles.Count > 0)
            {
                var path = _pendingInboxFiles.Dequeue();

                if (!File.Exists(path))
                    continue;

                try
                {
                    var kind = DetectReplyKindSafe(path); // "devis" | "signature" | ""
                    ImportReplyFile(path);
                    MoveToImported(path);

                    if (MainContent.Content is IReloadablePage p)
                        p.Reload();

                    var displayKind =
                        string.Equals(kind, "devis", StringComparison.OrdinalIgnoreCase) ? "Devis reçu" :
                        string.Equals(kind, "signature", StringComparison.OrdinalIgnoreCase) ? "Validation reçue" :
                        "Retour reçu";

                    System.Windows.MessageBox.Show(
                        this,
                        $"{displayKind}.\n\nFichier : {Path.GetFileName(path)}",
                        "Retour Iziregi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"Import impossible.\n\n{ex.Message}\n\nLe fichier sera déplacé dans INBOX\\Error.",
                        "Erreur import INBOX",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    MoveToError(path);
                }
            }
        }
        finally
        {
            _isProcessingInboxQueue = false;
        }
    }

    private void MoveToImported(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            Directory.CreateDirectory(ImportedDir);

            var fileName = Path.GetFileName(path);
            var target = Path.Combine(ImportedDir, fileName);

            if (File.Exists(target))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                target = Path.Combine(ImportedDir, $"{nameNoExt}-{ts}.iziregi-reponse");
            }

            File.Move(path, target);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erreur déplacement INBOX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveToError(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            Directory.CreateDirectory(ErrorDir);

            var fileName = Path.GetFileName(path);
            var target = Path.Combine(ErrorDir, fileName);

            if (File.Exists(target))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                target = Path.Combine(ErrorDir, $"{nameNoExt}-{ts}.iziregi-reponse");
            }

            File.Move(path, target);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erreur déplacement INBOX\\Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // =========================
    // Import core
    // =========================
    private class IziregiReplyFile
    {
        public string FileType { get; set; } = "iziregi-reponse";
        public string Package { get; set; } = "";
        public string RepliedAt { get; set; } = "";
        public long WorkOrderId { get; set; }
        public WorkOrder? WorkOrder { get; set; }
        public List<WorkOrderLine> Lines { get; set; } = new();

        public string SignatureName { get; set; } = "";
        public string SignatureDate { get; set; } = "";
        public byte[]? SignaturePng { get; set; }
    }

    private void ImportReplyFile(string filePath)
    {
        var json = ReadAllTextWithRetry(filePath, attempts: 10, delayMs: 200);

        var reply = JsonSerializer.Deserialize<IziregiReplyFile>(json, JsonOptions);
        if (reply == null)
            throw new Exception("Fichier invalide.");

        if (!string.Equals(reply.FileType, "iziregi-reponse", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Ce fichier n’est pas un .iziregi-reponse valide.");

        if (reply.WorkOrderId <= 0 && reply.WorkOrder != null && reply.WorkOrder.Id > 0)
            reply.WorkOrderId = reply.WorkOrder.Id;

        if (reply.WorkOrderId <= 0)
            throw new Exception("WorkOrderId manquant.");

        var pkg = (reply.Package ?? "").Trim().ToLowerInvariant();

        if (pkg == "devis")
            ImportQuoteReply(reply);
        else if (pkg == "signature")
            ImportSignatureReply(reply);
        else
            throw new Exception("Package inconnu dans le retour (attendu: devis ou signature).");
    }

    private void ImportQuoteReply(IziregiReplyFile reply)
    {
        var wo = Db.GetWorkOrderById(reply.WorkOrderId);
        if (wo == null)
            throw new Exception("Bon introuvable dans la base locale.");

        if (reply.WorkOrder != null)
        {
            wo.LaborHours = reply.WorkOrder.LaborHours;
            wo.LaborRate = reply.WorkOrder.LaborRate;
            wo.TravelQty = reply.WorkOrder.TravelQty;
            wo.TravelRate = reply.WorkOrder.TravelRate;
            wo.TvaRate = reply.WorkOrder.TvaRate;
            wo.QuoteNotes = reply.WorkOrder.QuoteNotes ?? "";
            Db.UpdateWorkOrderQuote(wo);
        }

        if (reply.Lines != null && reply.Lines.Count > 0)
        {
            var existing = Db.GetWorkOrderLines(wo.Id);
            foreach (var l in existing)
                Db.DeleteWorkOrderLine(l.Id);

            foreach (var l in reply.Lines)
                Db.InsertWorkOrderLine(wo.Id, l.Label ?? "", l.Qty, l.UnitPrice);
        }

        Db.SetStageQuoteReceived(wo.Id);
    }

    private void ImportSignatureReply(IziregiReplyFile reply)
    {
        var wo = Db.GetWorkOrderById(reply.WorkOrderId);
        if (wo == null)
            throw new Exception("Bon introuvable dans la base locale.");

        wo.SignatureName = (reply.SignatureName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(reply.SignatureDate) && DateTime.TryParse(reply.SignatureDate, out var dt))
            wo.SignatureDate = dt.Date;
        wo.SignaturePng = reply.SignaturePng;

        Db.UpdateWorkOrderSignatureRaw(wo);

        wo = Db.GetWorkOrderById(wo.Id);
        if (wo == null) throw new Exception("Bon introuvable après import.");

        if (!wo.HasFullSignature)
            throw new Exception("Signature incomplète dans le retour (nom/date/signature).");

        Db.SetStageValidated(wo.Id);
    }

    private string DetectReplyKindSafe(string filePath)
    {
        try
        {
            var json = ReadAllTextWithRetry(filePath, attempts: 5, delayMs: 150);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Package", out var pkgEl))
                return "";

            return (pkgEl.GetString() ?? "").Trim().ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private static string ReadAllTextWithRetry(string filePath, int attempts, int delayMs)
    {
        Exception? last = null;

        for (int i = 0; i < attempts; i++)
        {
            try { return File.ReadAllText(filePath); }
            catch (Exception ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        throw new Exception($"Impossible de lire le fichier : {Path.GetFileName(filePath)}", last);
    }
}