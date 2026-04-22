// File: WorkOrderWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Services;
using Microsoft.Win32;

namespace Iziregi.Test;

public partial class WorkOrderWindow : Window
{
    private readonly long _workOrderId;
    private readonly WorkOrderEditMode _mode;
    private WorkOrder? _wo;

    private TextBox? _activeEditTextBox;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string InboxDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "INBOX");

    public WorkOrderWindow(long workOrderId, WorkOrderEditMode mode)
    {
        InitializeComponent();

        _workOrderId = workOrderId;
        _mode = mode;

        DataContext = this;

        LinesGrid.CellEditEnding += LinesGrid_CellEditEnding;
        LinesGrid.PreparingCellForEdit += LinesGrid_PreparingCellForEdit;

        LoadWorkOrder();
        ApplyMode();
        RecalculateTotals();
    }

    public string LaborHoursDisplay
    {
        get => _wo == null ? "" : EmptyIfZero0(_wo.LaborHours);
        set { if (_wo == null) return; _wo.LaborHours = ParseDouble(value); RecalculateTotals(); }
    }

    public string LaborRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.LaborRate);
        set { if (_wo == null) return; _wo.LaborRate = ParseDouble(value); RecalculateTotals(); }
    }

    public string TravelQtyDisplay
    {
        get => _wo == null ? "" : EmptyIfZero0(_wo.TravelQty);
        set { if (_wo == null) return; _wo.TravelQty = ParseDouble(value); RecalculateTotals(); }
    }

    public string TravelRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.TravelRate);
        set { if (_wo == null) return; _wo.TravelRate = ParseDouble(value); RecalculateTotals(); }
    }

    public string TvaRateDisplay
    {
        get => _wo == null ? "" : EmptyIfZero2(_wo.TvaRate);
        set { if (_wo == null) return; _wo.TvaRate = ParseDouble(value); RecalculateTotals(); }
    }

    private void ApplyMode()
    {
        DemandBorder.Visibility = Visibility.Visible;
        QuoteBorder.Visibility = Visibility.Visible;
        SignatureBorder.Visibility = Visibility.Visible;

        bool isArchitect = _mode == WorkOrderEditMode.Architecte;
        bool isCompany = _mode == WorkOrderEditMode.EntrepriseDevis;
        bool isSigner = _mode == WorkOrderEditMode.Signataire;

        bool canEditDemand = isArchitect;
        bool canEditQuote = isArchitect || isCompany;
        bool canEditSignature = isArchitect || isSigner;

        DemandPanel.IsEnabled = canEditDemand;
        QuotePanel.IsEnabled = canEditQuote;
        SignaturePanel.IsEnabled = canEditSignature;

        SaveHeaderButton.Visibility = isArchitect ? Visibility.Visible : Visibility.Collapsed;
        SendToCompanyButton.Visibility = isArchitect ? Visibility.Visible : Visibility.Collapsed;
        ExportForSignatureButton.Visibility = isArchitect ? Visibility.Visible : Visibility.Collapsed;
        SendValidatedToCompanyButton.Visibility = Visibility.Collapsed;
        CreatePdfButton.Visibility = isArchitect ? Visibility.Visible : Visibility.Collapsed;

        bool showQuoteButtons = !isSigner;

        AddMaterialLineButton.Visibility =
            (showQuoteButtons && (isArchitect || isCompany))
                ? Visibility.Visible
                : Visibility.Collapsed;

        DeleteMaterialLineButton.Visibility =
            (showQuoteButtons && (isArchitect || isCompany))
                ? Visibility.Visible
                : Visibility.Collapsed;

        SaveQuoteButton.Visibility =
            (showQuoteButtons && (isArchitect || isCompany))
                ? Visibility.Visible
                : Visibility.Collapsed;

        SaveReplyButtonQuote.Visibility = isCompany ? Visibility.Visible : Visibility.Collapsed;
        SaveReplyButtonSignature.Visibility = isSigner ? Visibility.Visible : Visibility.Collapsed;

        ExportForSignatureButton.IsEnabled = isArchitect && (_wo?.IsQuoteReceived ?? false);

        if (isArchitect && (_wo?.IsValidated ?? false))
            SendValidatedToCompanyButton.Visibility = Visibility.Visible;
        else
            SendValidatedToCompanyButton.Visibility = Visibility.Collapsed;

        ClearSignatureButton.Visibility = canEditSignature ? Visibility.Visible : Visibility.Collapsed;
        SaveSignatureButton.Visibility = canEditSignature ? Visibility.Visible : Visibility.Collapsed;

        SignatureInkCanvas.IsEnabled = canEditSignature;
        SignatureNameTextBox.IsEnabled = canEditSignature;
        SignatureDatePicker.IsEnabled = canEditSignature;
        ClearSignatureButton.IsEnabled = canEditSignature;
        SaveSignatureButton.IsEnabled = canEditSignature;

        if (canEditSignature)
        {
            SignatureInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Black,
                Width = 2.0,
                Height = 2.0,
                FitToCurve = true,
                IgnorePressure = true
            };
        }
    }

    private void LoadWorkOrder()
    {
        _wo = Db.GetWorkOrderById(_workOrderId);
        if (_wo == null)
        {
            MessageBox.Show("Bon introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        TitleTextBlock.Text = $"Bon de régie — N° {_wo.BdrNumber}";

        PlaceComboBox.ItemsSource = Db.GetPlaces();
        PerformedByComboBox.ItemsSource = Db.GetCompanies();

        // ✅ RequestedBy (Demandé par) depuis la liste
        RequestedByComboBox.ItemsSource = Db.GetRequesters();

        PlaceComboBox.SelectedItem = _wo.Place;

        // Si la valeur n’est pas dans la liste (ancien bon), on la met comme texte
        if ((RequestedByComboBox.ItemsSource as IEnumerable<string>)?.Contains(_wo.RequestedBy) == true)
            RequestedByComboBox.SelectedItem = _wo.RequestedBy;
        else
            RequestedByComboBox.Text = _wo.RequestedBy;

        PerformedByComboBox.SelectedItem = _wo.PerformedBy;
        RequestDatePicker.SelectedDate = _wo.RequestDate;
        DescriptionTextBox.Text = _wo.Description ?? "";

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;

        QuoteNotesTextBox.Text = _wo.QuoteNotes ?? "";

        SignatureNameTextBox.Text = _wo.SignatureName ?? "";
        var date = _wo.SignatureDate?.Date ?? DateTime.Today;
        SignatureDatePicker.SelectedDate = date;

        if (_wo.SignaturePng != null && _wo.SignaturePng.Length > 0)
            SignatureImage.Source = PngBytesToImageSource(_wo.SignaturePng);
        else
            SignatureImage.Source = null;

        SignatureInkCanvas.Strokes.Clear();

        RefreshDisplayBindings();
        ApplyMode();
    }

    private void RefreshDisplayBindings()
    {
        var dc = DataContext;
        DataContext = null;
        DataContext = dc;
    }

    private void LinesGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        var header = e.Column?.Header?.ToString() ?? "";
        bool isQtyOrPrice =
            header.Contains("Qt", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Prix", StringComparison.OrdinalIgnoreCase);

        if (!isQtyOrPrice) return;
        if (e.EditingElement is not TextBox tb) return;

        tb.MaxLength = 0;

        if (_activeEditTextBox != null)
            _activeEditTextBox.TextChanged -= ActiveEditTextBox_TextChanged;

        _activeEditTextBox = tb;
        _activeEditTextBox.TextChanged += ActiveEditTextBox_TextChanged;
    }

    private void ActiveEditTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_wo == null) return;
        if (sender is not TextBox tb) return;

        var be = tb.GetBindingExpression(TextBox.TextProperty);
        be?.UpdateSource();

        if (LinesGrid.CurrentItem is not WorkOrderLine line)
            return;

        line.RecomputeLineTotal();
        RecalculateTotals();
    }

    private void LinesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        => Dispatcher.BeginInvoke(new Action(SaveCurrentGridLinesToDb));

    private void SaveCurrentGridLinesToDb()
    {
        if (_wo == null) return;

        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var lines = (LinesGrid.ItemsSource as IEnumerable<WorkOrderLine>)?.ToList();
        if (lines == null) return;

        foreach (var l in lines)
        {
            l.Label = (l.Label ?? "").Trim();
            l.RecomputeLineTotal();

            if (l.Id > 0)
                Db.UpdateWorkOrderLine(l);
        }
    }

    private List<WorkOrderLine> GetCurrentLinesFromGrid()
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var lines = (LinesGrid.ItemsSource as IEnumerable<WorkOrderLine>)?.ToList()
                    ?? new List<WorkOrderLine>();

        foreach (var l in lines)
        {
            l.Label = (l.Label ?? "").Trim();
            l.RecomputeLineTotal();
        }

        return lines;
    }

    private void AddMaterialLine_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();
        Db.InsertWorkOrderLine(_wo.Id, "", 0, 0);

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;
        RecalculateTotals();
    }

    private void DeleteMaterialLine_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();
        if (LinesGrid.SelectedItem is not WorkOrderLine line) return;

        Db.DeleteWorkOrderLine(line.Id);

        var lines = Db.GetWorkOrderLines(_wo.Id);
        foreach (var l in lines)
            l.RecomputeLineTotal();

        LinesGrid.ItemsSource = lines;
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        if (_wo == null) return;

        var lines = (LinesGrid.ItemsSource as IEnumerable<WorkOrderLine>)?.ToList()
                    ?? Db.GetWorkOrderLines(_wo.Id);

        foreach (var l in lines)
            l.RecomputeLineTotal();

        var materialTotal = Math.Round(lines.Sum(l => l.LineTotal), 2);

        var laborTotal = Math.Round(_wo.LaborHours * _wo.LaborRate, 2);
        var travelTotal = Math.Round(_wo.TravelQty * _wo.TravelRate, 2);

        var totalHt = Math.Round(materialTotal + laborTotal + travelTotal, 2);
        var tvaAmount = Math.Round(totalHt * (_wo.TvaRate / 100.0), 2);
        var totalTtc = Math.Round(totalHt + tvaAmount, 2);

        LaborTotalTextBlock.Text = F2(laborTotal);
        TravelTotalTextBlock.Text = F2(travelTotal);
        TvaAmountTextBlock.Text = F2(tvaAmount);

        TotalHtTextBlock.Text = $"Total HT : {F2(totalHt)}";
        TotalTtcTextBlock.Text = $"Total TTC : {F2(totalTtc)}";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private bool DemandIsFilled()
    {
        var place = PlaceComboBox.SelectedItem?.ToString() ?? "";
        var reqBy = RequestedByComboBox.Text ?? "";
        var perfBy = PerformedByComboBox.SelectedItem?.ToString() ?? "";
        var desc = DescriptionTextBox.Text ?? "";

        return
            !string.IsNullOrWhiteSpace(place) &&
            !string.IsNullOrWhiteSpace(reqBy) &&
            !string.IsNullOrWhiteSpace(perfBy) &&
            !string.IsNullOrWhiteSpace(desc);
    }

    private void SaveHeader_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Architecte) return;

        _wo.Place = PlaceComboBox.SelectedItem?.ToString() ?? "";
        _wo.RequestedBy = (RequestedByComboBox.Text ?? "").Trim();
        _wo.PerformedBy = PerformedByComboBox.SelectedItem?.ToString() ?? "";
        _wo.RequestDate = RequestDatePicker.SelectedDate ?? DateTime.Today;
        _wo.Description = DescriptionTextBox.Text ?? "";

        Db.UpdateWorkOrderHeader(_wo);

        if (DemandIsFilled())
            Db.SetStageInCreation(_wo.Id);

        MessageBox.Show("Demande enregistrée.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadWorkOrder();
    }

    private bool QuoteIsReallyFilled()
    {
        if (_wo == null) return false;

        var lines = GetCurrentLinesFromGrid();
        bool hasMaterial = lines.Any(l =>
            !string.IsNullOrWhiteSpace(l.Label) ||
            (l.Qty > 0) ||
            (l.UnitPrice > 0));

        bool hasLabor = _wo.LaborHours > 0 || _wo.LaborRate > 0;
        bool hasTravel = _wo.TravelQty > 0 || _wo.TravelRate > 0;

        return hasMaterial || hasLabor || hasTravel;
    }

    private void SaveQuote_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        SaveCurrentGridLinesToDb();
        _wo.QuoteNotes = QuoteNotesTextBox.Text ?? "";
        Db.UpdateWorkOrderQuote(_wo);

        if (_mode == WorkOrderEditMode.Architecte && QuoteIsReallyFilled())
            Db.SetStageQuoteReceived(_wo.Id);

        MessageBox.Show("Devis enregistré.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadWorkOrder();
    }

    private void SendToCompany_Click(object sender, RoutedEventArgs e)
        => SaveSignerReply_Click(sender, e);

    private class IziregiExportFile
    {
        public string FileType { get; set; } = "iziregi";
        public string Package { get; set; } = "";
        public string ExportedAt { get; set; } = "";
        public WorkOrder? WorkOrder { get; set; }
        public List<WorkOrderLine> Lines { get; set; } = new();
    }

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

    private void SaveReply_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;

        Directory.CreateDirectory(InboxDir);

        if (_mode == WorkOrderEditMode.EntrepriseDevis)
        {
            SaveCurrentGridLinesToDb();
            _wo.QuoteNotes = QuoteNotesTextBox.Text ?? "";
            Db.UpdateWorkOrderQuote(_wo);

            var lines = GetCurrentLinesFromGrid();

            var reply = new IziregiReplyFile
            {
                FileType = "iziregi-reponse",
                Package = "devis",
                RepliedAt = DateTime.UtcNow.ToString("o"),
                WorkOrderId = _wo.Id,
                WorkOrder = _wo,
                Lines = lines
            };

            var fileName = $"BDR-{_wo.BdrNumber}-devis.iziregi-reponse";
            var path = Path.Combine(InboxDir, fileName);

            File.WriteAllText(path, JsonSerializer.Serialize(reply, JsonOptions));

            MessageBox.Show("Réponse devis enregistrée (INBOX).", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }

        if (_mode == WorkOrderEditMode.Signataire)
        {
            var refreshed = Db.GetWorkOrderById(_wo.Id);
            if (refreshed == null)
            {
                MessageBox.Show("Bon introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!refreshed.HasFullSignature)
            {
                MessageBox.Show("Signature incomplète. Fais d’abord « Enregistrer signature ».", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reply = new IziregiReplyFile
            {
                FileType = "iziregi-reponse",
                Package = "signature",
                RepliedAt = DateTime.UtcNow.ToString("o"),
                WorkOrderId = refreshed.Id,
                SignatureName = refreshed.SignatureName ?? "",
                SignatureDate = refreshed.SignatureDate?.ToString("yyyy-MM-dd") ?? "",
                SignaturePng = refreshed.SignaturePng
            };

            var fileName = $"BDR-{refreshed.BdrNumber}-signature.iziregi-reponse";
            var path = Path.Combine(InboxDir, fileName);

            File.WriteAllText(path, JsonSerializer.Serialize(reply, JsonOptions));

            MessageBox.Show("Réponse signature enregistrée (INBOX).", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }
    }

    private void SaveSignerReply_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Architecte) return;

        SaveHeader_Click(sender, e);
        SaveCurrentGridLinesToDb();
        _wo.QuoteNotes = QuoteNotesTextBox.Text ?? "";
        Db.UpdateWorkOrderQuote(_wo);

        var lines = GetCurrentLinesFromGrid();

        var payload = new IziregiExportFile
        {
            FileType = "iziregi",
            Package = "devis",
            ExportedAt = DateTime.UtcNow.ToString("o"),
            WorkOrder = _wo,
            Lines = lines
        };

        var sfd = new SaveFileDialog
        {
            Title = "Exporter pour l’entreprise (Devis)",
            Filter = "Iziregi (*.iziregi)|*.iziregi",
            FileName = $"BDR-{_wo.BdrNumber}-devis.iziregi",
            AddExtension = true,
            DefaultExt = ".iziregi"
        };

        if (sfd.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sfd.FileName, json);

            Db.SetStageSentToCompany(_wo.Id);

            MessageBox.Show("Devis exporté. Statut: Envoyé à l’entreprise.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadWorkOrder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportForSignature_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Architecte) return;

        if (!_wo.IsQuoteReceived)
        {
            MessageBox.Show("Le devis n’est pas encore rempli.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var lines = GetCurrentLinesFromGrid();

        var payload = new IziregiExportFile
        {
            FileType = "iziregi",
            Package = "signature",
            ExportedAt = DateTime.UtcNow.ToString("o"),
            WorkOrder = _wo,
            Lines = lines
        };

        var sfd = new SaveFileDialog
        {
            Title = "Exporter pour le signataire (Signature)",
            Filter = "Iziregi (*.iziregi)|*.iziregi",
            FileName = $"BDR-{_wo.BdrNumber}-signature.iziregi",
            AddExtension = true,
            DefaultExt = ".iziregi"
        };

        if (sfd.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sfd.FileName, json);

            Db.SetStageSentToSigner(_wo.Id);

            MessageBox.Show("Package signature exporté. Statut: Envoyé au signataire.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadWorkOrder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur export signature", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearSignature_Click(object sender, RoutedEventArgs e)
    {
        SignatureInkCanvas.Strokes.Clear();
        SignatureImage.Source = null;
    }

    private void SaveSignature_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Signataire && _mode != WorkOrderEditMode.Architecte) return;

        var name = (SignatureNameTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Nom obligatoire.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SignatureInkCanvas.Strokes == null || SignatureInkCanvas.Strokes.Count == 0)
        {
            MessageBox.Show("Signature obligatoire.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var date = SignatureDatePicker.SelectedDate ?? DateTime.Today;

        _wo.SignatureName = name;
        _wo.SignatureDate = date;
        _wo.SignaturePng = StrokesToPngBytes(SignatureInkCanvas, 800, 250);

        Db.UpdateWorkOrderSignatureRaw(_wo);

        SignatureImage.Source = PngBytesToImageSource(_wo.SignaturePng);
        SignatureInkCanvas.Strokes.Clear();

        var refreshed = Db.GetWorkOrderById(_wo.Id);
        if (refreshed != null && refreshed.HasFullSignature)
            Db.SetStageValidated(_wo.Id);

        MessageBox.Show("Signature enregistrée.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadWorkOrder();
    }

    private static string PdfValidatedFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "PDF validés");

    private string GetDefaultValidatedPdfPath()
    {
        if (_wo == null) throw new Exception("WorkOrder null");
        Directory.CreateDirectory(PdfValidatedFolder);

        var baseName = $"Bon-de-regie-BDR-{_wo.BdrNumber}.pdf";
        var path = Path.Combine(PdfValidatedFolder, baseName);

        if (!File.Exists(path))
            return path;

        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var alt = $"Bon-de-regie-BDR-{_wo.BdrNumber}-{ts}.pdf";
        return Path.Combine(PdfValidatedFolder, alt);
    }

    private void CreatePdf_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Architecte) return;

        if (!_wo.IsValidated)
        {
            MessageBox.Show("Le document n’est pas validé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var lines = GetCurrentLinesFromGrid();
            var pdfPath = GetDefaultValidatedPdfPath();

            PdfService.GenerateWorkOrderPdf(pdfPath, _wo, lines);

            Db.SetValidatedPdfSent(_wo.Id, true);

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{pdfPath}\"") { UseShellExecute = true });

            MessageBox.Show("PDF validé créé et classé. Statut: PDF validé envoyé.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadWorkOrder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SendValidatedToCompany_Click(object sender, RoutedEventArgs e)
    {
        if (_wo == null) return;
        if (_mode != WorkOrderEditMode.Architecte) return;

        if (!_wo.IsValidated)
        {
            MessageBox.Show("Le document n’est pas validé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            CreatePdf_Click(sender, e);

            var subject = Uri.EscapeDataString($"Bon de régie BDR-{_wo.BdrNumber} — Document validé (PDF)");
            var body = Uri.EscapeDataString(
                "Bonjour,\n\nVeuillez trouver ci-joint le PDF du bon de régie validé.\n\nMerci."
            );

            var mailto = $"mailto:?subject={subject}&body={body}";
            Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });

            MessageBox.Show("Email préparé. Joins le PDF depuis le dossier « PDF validés ».", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur email", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SignerExportPdf_Click(object sender, RoutedEventArgs e) { }

    private static byte[] StrokesToPngBytes(InkCanvas canvas, int width, int height)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        var grid = new Grid { Width = width, Height = height, Background = Brushes.White };
        var clone = new InkCanvas
        {
            Width = width,
            Height = height,
            Background = Brushes.White
        };
        clone.Strokes = canvas.Strokes.Clone();

        grid.Children.Add(clone);
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();

        rtb.Render(grid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static ImageSource? PngBytesToImageSource(byte[]? pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0) return null;

        var image = new BitmapImage();
        using var ms = new MemoryStream(pngBytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string F2(double v) => v.ToString("0.00");

    private static string EmptyIfZero2(double v)
        => Math.Abs(v) < 0.0000001 ? "" : v.ToString("0.00");

    private static string EmptyIfZero0(double v)
        => Math.Abs(v) < 0.0000001 ? "" : Math.Round(v, 0).ToString("0");

    private static double ParseDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim().Replace(',', '.');
        return double.TryParse(
            s,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0;
    }
}