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

    public MainWindow()
    {
        InitializeComponent();

        // DB init
        Db.Init();

        // Seeds de base (si vide)
        Db.SeedPlacesIfEmpty("D20", "D21", "Extérieur");
        Db.SeedCompaniesIfEmpty("Electricien", "Sanitaire", "Ventilation");
        Db.SeedRequestersIfEmpty("Architecte");

        // Watcher INBOX
        StartInboxWatcher();

        // Page par défaut
        ShowDashboard();
    }

    // =========================
    // Navigation menu handlers
    // =========================
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void NavAccounting_Click(object sender, RoutedEventArgs e) => ShowAccounting();
    private void NavArchives_Click(object sender, RoutedEventArgs e) => ShowArchives();
    private void NavTrash_Click(object sender, RoutedEventArgs e) => ShowTrash();
    private void NavLists_Click(object sender, RoutedEventArgs e) => ShowLists();

    private void ShowDashboard()
    {
        _dashboardPage ??= new DashboardPage(this);
        MainContent.Content = _dashboardPage;
        _dashboardPage.Reload();
    }

    private void ShowAccounting()
    {
        _accountingPage ??= new AccountingPage(this);
        MainContent.Content = _accountingPage;
        _accountingPage.Reload();
    }

    private void ShowArchives()
    {
        _archivesPage ??= new ArchivesPage(this);
        MainContent.Content = _archivesPage;
        _archivesPage.Reload();
    }

    private void ShowTrash()
    {
        _trashPage ??= new TrashPage(this);
        MainContent.Content = _trashPage;
        _trashPage.Reload();
    }

    private void ShowLists()
    {
        _listsPage ??= new ListsPage(this);
        MainContent.Content = _listsPage;
        _listsPage.Reload();
    }

    // =========================
    // API appelée depuis les pages
    // =========================
    public Project? GetSelectedProject() => _selectedProject;
    public void SetSelectedProject(Project? p) => _selectedProject = p;

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
            _selectedProject = w.SelectedProject;

        if (MainContent.Content is IReloadablePage p)
            p.Reload();
    }

    // =========================
    // ✅ Création nouveau bon (utilise les valeurs par défaut configurées)
    // =========================
    public void CreateNewWorkOrderAndOpen()
    {
        // Lieu par défaut
        var defaultPlace = (Db.GetDefaultPlace() ?? "").Trim();
        var place = !string.IsNullOrWhiteSpace(defaultPlace)
            ? defaultPlace
            : (Db.GetPlaces().FirstOrDefault() ?? "D21");

        // Entreprise par défaut
        var defaultCompany = (Db.GetDefaultCompany() ?? "").Trim();
        var performedBy = !string.IsNullOrWhiteSpace(defaultCompany)
            ? defaultCompany
            : (Db.GetCompanies().FirstOrDefault() ?? "Electricien");

        // Demandé par par défaut
        var defaultRequester = (Db.GetDefaultRequester() ?? "").Trim();
        var requestedBy = !string.IsNullOrWhiteSpace(defaultRequester)
            ? defaultRequester
            : "Architecte";

        var wo = new WorkOrder
        {
            BdrNumber = Db.GetNextBdrNumber(),
            Place = place,
            RequestedBy = requestedBy,
            PerformedBy = performedBy,
            RequestDate = DateTime.Today,

            Description = "",

            // Pipeline
            IsInCreation = false,
            IsSentToCompany = false,
            IsQuoteReceived = false,
            IsSentToSigner = false,
            IsValidated = false,
            IsValidatedPdfSent = false,

            // Manuels
            IsPerformed = false,
            IsCancelled = false,

            // Corbeille / Archives
            IsTrashed = false,
            TrashedAt = null,
            IsArchived = false,
            ArchivedAt = null,

            // Devis
            LaborHours = 0,
            LaborRate = 0,
            TravelQty = 0,
            TravelRate = 0,
            TvaRate = 8.1,
            QuoteNotes = "",

            ProjectId = _selectedProject?.Id
        };

        Db.InsertWorkOrder(wo);

        // Insérer une première ligne vide (V1)
        var created = Db.GetWorkOrders().FirstOrDefault();
        if (created != null)
            Db.InsertWorkOrderLine(created.Id, "", 0, 0);

        created = Db.GetWorkOrders().FirstOrDefault();
        if (created == null) return;

        OpenWorkOrder(created.Id, WorkOrderEditMode.Architecte);
    }

    // =========================
    // Import manuel (boutons de page Dashboard)
    // =========================
    public void ImportCompanyQuoteReply_ManualPicker()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Importer retour entreprise (devis)",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur import devis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ImportSignerReply_ManualPicker()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Importer retour signataire",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur import signataire", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, ex.Message, "Erreur INBOX", MessageBoxButton.OK, MessageBoxImage.Error);
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

                var kind = DetectReplyKindSafe(path); // "devis" ou "signature"
                var displayKind =
                    string.Equals(kind, "devis", StringComparison.OrdinalIgnoreCase) ? "Devis rempli" :
                    string.Equals(kind, "signature", StringComparison.OrdinalIgnoreCase) ? "Signature signée" :
                    "Retour";

                var ok = MessageBox.Show(this,
                    $"Retour reçu : {displayKind}\n\nFichier : {Path.GetFileName(path)}\n\nCliquer OK pour importer.",
                    "Retour Iziregi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (ok == MessageBoxResult.OK)
                {
                    ImportReplyFile(path);
                    MoveToImported(path);

                    if (MainContent.Content is IReloadablePage p)
                        p.Reload();
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
            MessageBox.Show(this, ex.Message, "Erreur déplacement INBOX", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        MessageBox.Show(this, "Devis importé. Statut: Devis rempli.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
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

        MessageBox.Show(this, "Signature importée. Statut: Validé.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
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