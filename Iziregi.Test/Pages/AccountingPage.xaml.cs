// File: Pages/AccountingPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using System.Windows;
using System.Windows.Controls;

using System.Windows.Media;
using System.Windows.Media.Imaging;

using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Services;

// ✅ Fix ambiguïtés WinForms vs WPF
using WpfMessageBox = System.Windows.MessageBox;
using WpfUserControl = System.Windows.Controls.UserControl;

// ✅ Fix ambiguïtés System.Drawing vs WPF (Brush/Color)
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Iziregi.Test.Pages;

public partial class AccountingPage : WpfUserControl, IReloadablePage
{
    private readonly MainWindow _host;

    private List<WorkOrder> _allAccountingWorkOrders = new();
    private List<WorkOrderAccountingRow> _currentRows = new();

    // ✅ couleurs entreprise (réutilisées pour surligner le titre "Détail")
    private Dictionary<string, string> _companyColorMap = new(StringComparer.OrdinalIgnoreCase);

    // ✅ Tri + échelle du graphique (pilotent aussi le tableau)
    private enum SortMode
    {
        TotalTtcDesc,
        TotalTtcAsc,
        Alpha
    }

    private SortMode _sortMode = SortMode.TotalTtcDesc;

    private double _chartMaxBarPx = 260; // calculé dynamiquement
    private List<CompanyTotalsRow>? _lastCompanyRows; // sans TOTAL

    public AccountingPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        // ✅ Les dates par défaut sont définies dans Reload()
        // (date de départ = date du plus ancien bon du projet courant)

        FromDatePicker.SelectedDateChanged += (_, __) => ApplyFiltersAndRender();
        ToDatePicker.SelectedDateChanged += (_, __) => ApplyFiltersAndRender();

        // ✅ recalcul échelle au resize
        ChartItems.SizeChanged += (_, __) =>
        {
            UpdateChartScaleFromActualWidth();
            RenderTotalsTableAndChart();
        };
    }

    // =========================
    // View models
    // =========================
    private class CompanyTotalsRow
    {
        public bool IsTotal { get; set; }
        public string Company { get; set; } = "";
        public int Count { get; set; }

        public double TotalHt { get; set; }
        public double TotalTva { get; set; }
        public double TotalTtc { get; set; }

        public MediaBrush CompanyBrush { get; set; } = MediaBrushes.Transparent;
        public MediaBrush CompanyTextBrush { get; set; } = MediaBrushes.Black;
    }

    private class CompanyChartRow
    {
        public string Company { get; set; } = "";
        public double TotalTtc { get; set; }
        public double BarWidth { get; set; }
        public MediaBrush BarBrush { get; set; } = MediaBrushes.SteelBlue;
    }

    private class WorkOrderAccountingRow
    {
        public long WorkOrderId { get; set; }
        public int BdrNumber { get; set; }
        public DateTime RequestDate { get; set; }

        public string Place { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public string Company { get; set; } = "";

        public double Material { get; set; }
        public double Labor { get; set; }
        public double Travel { get; set; }

        // ✅ HT / TVA / TTC = NET après rabais (comme dans le BDR)
        public double Ht { get; set; }
        public double TvaRate { get; set; }
        public double Tva { get; set; }
        public double Ttc { get; set; }
    }

    private class DetailsRow
    {
        public bool IsTotal { get; set; }

        // ✅ lien vers le bon de régie
        public long WorkOrderId { get; set; }

        public string Bdr { get; set; } = "";
        public DateTime? Date { get; set; }

        // ✅ entreprise + couleurs (pour le titre "Détail")
        public string Company { get; set; } = "";
        public MediaBrush CompanyBrush { get; set; } = MediaBrushes.Transparent;
        public MediaBrush CompanyTextBrush { get; set; } = MediaBrushes.Black;

        public double Ht { get; set; }
        public double Tva { get; set; }
        public double Ttc { get; set; }
    }

    // =========================
    // ✅ Dates par défaut
    // =========================
    private void SetDefaultDateRangeFromCurrentData()
    {
        DateTime from;
        DateTime to;

        if (_allAccountingWorkOrders != null && _allAccountingWorkOrders.Count > 0)
        {
            // ✅ Important : la plage par défaut doit couvrir TOUS les bons comptabilisés
            // (du plus ancien au plus récent), sinon un bon nouvellement validé dont la
            // date de demande tombe après le mois du tout premier bon comptabilisé se
            // retrouvait hors de la plage "Du / Au" par défaut et semblait "ne pas
            // apparaître" dans la Comptabilité, même si la donnée était correcte.
            from = _allAccountingWorkOrders.Min(w => w.RequestDate.Date);
            to = _allAccountingWorkOrders.Max(w => w.RequestDate.Date);
        }
        else
        {
            var today = DateTime.Today;
            from = new DateTime(today.Year, today.Month, 1);
            to = new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        }

        FromDatePicker.SelectedDate = from;
        ToDatePicker.SelectedDate = to;
    }

    // =========================
    // Reload
    // =========================
    public void Reload()
    {
        var projectIdNullable = Db.GetCurrentProjectId();
        if (!projectIdNullable.HasValue || projectIdNullable.Value <= 0)
        {
            _allAccountingWorkOrders = new List<WorkOrder>();

            SetDefaultDateRangeFromCurrentData();
            ReloadFilterSources();
            ApplyFiltersAndRender();

            WpfMessageBox.Show(
                "Aucun dossier courant. Sélectionne un dossier avant d’afficher la comptabilité.",
                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var projectId = projectIdNullable.Value;

        // ✅ Comptabilisation uniquement des bons VALIDÉS :
        // - Validé (nouveau système)
        // - OU anciens bons (IsValidated=1) qui n'ont pas encore ValidationDecision renseigné
        _allAccountingWorkOrders = Db.GetWorkOrdersForAccounting(projectId)
            .Where(w =>
            {
                if (w == null) return false;

                var decision = (w.ValidationDecision ?? "").Trim();

                // cas normal
                if (string.Equals(decision, "Validé", StringComparison.OrdinalIgnoreCase))
                    return true;

                // compat anciens bons (avant la colonne ValidationDecision)
                return w.IsValidated && string.IsNullOrWhiteSpace(decision);
            })
            .ToList();

        // ✅ Date de départ = date du plus ancien bon (dans la compta du projet)
        SetDefaultDateRangeFromCurrentData();

        ReloadFilterSources();
        ApplyFiltersAndRender();
    }

    private void ReloadFilterSources()
    {
        var companies = _allAccountingWorkOrders
            .Select(w => (w.PerformedBy ?? "").Trim())
            .Select(s => string.IsNullOrWhiteSpace(s) ? "(Non défini)" : s)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        CompanyFilterComboBox.ItemsSource = new List<string> { "Toutes" }.Concat(companies).ToList();
        CompanyFilterComboBox.SelectedIndex = 0;
    }

    private (DateTime? from, DateTime? to) GetDateRange()
    {
        var from = FromDatePicker.SelectedDate?.Date;
        var to = ToDatePicker.SelectedDate?.Date;
        return (from, to);
    }

    private string? GetSelectedCompany()
    {
        var s = CompanyFilterComboBox.SelectedItem?.ToString() ?? "Toutes";
        if (string.Equals(s, "Toutes", StringComparison.OrdinalIgnoreCase)) return null;
        return s;
    }

    private List<WorkOrder> ApplyWorkOrderFilters()
    {
        var (from, to) = GetDateRange();
        var company = GetSelectedCompany();

        IEnumerable<WorkOrder> q = _allAccountingWorkOrders;

        if (from.HasValue)
            q = q.Where(w => w.RequestDate.Date >= from.Value);

        if (to.HasValue)
            q = q.Where(w => w.RequestDate.Date <= to.Value);

        if (!string.IsNullOrWhiteSpace(company))
        {
            q = q.Where(w =>
            {
                var c = (w.PerformedBy ?? "").Trim();
                if (string.IsNullOrWhiteSpace(c)) c = "(Non défini)";
                return string.Equals(c, company, StringComparison.OrdinalIgnoreCase);
            });
        }

        return q.ToList();
    }

    // ✅ Rabais : après HT brut, avant TVA/TTC
    private WorkOrderAccountingRow ComputeRow(WorkOrder wo)
    {
        var lines = Db.GetWorkOrderLines(wo.Id);
        var material = Math.Round(lines.Sum(l => l.LineTotal), 2);

        var labor = Math.Round(wo.LaborHours * wo.LaborRate, 2);
        var travel = Math.Round(wo.TravelQty * wo.TravelRate, 2);

        // ✅ BUG RÉEL : le PDF du devis (PdfService.cs) inclut le Forfait (ForfaitQty ×
        // ForfaitUnitPrice) dans le HT brut quand le devis a été fait en mode "Forfait selon
        // doc annexé", mais ce calcul ne le faisait pas ici. Résultat : un bon devisé en
        // Forfait (sans lignes de matériel ni heures de main d'œuvre saisies) ressortait à
        // 0.00 partout dans le détail Comptabilité, alors qu'il a un vrai montant facturé.
        // On reprend exactement la même logique que PdfService pour rester cohérent.
        var forfait = Math.Round(wo.ForfaitQty * wo.ForfaitUnitPrice, 2);
        var hasForfait = Math.Abs(forfait) > 0.0000000001;

        var htBrut = Math.Round(material + labor + travel + (hasForfait ? forfait : 0), 2);

        var discountRate = wo.DiscountRate;
        if (double.IsNaN(discountRate) || double.IsInfinity(discountRate)) discountRate = 0;
        discountRate = Math.Max(0, discountRate);

        var htNet = Math.Round(htBrut * (1.0 - (discountRate / 100.0)), 2);

        var tva = Math.Round(htNet * (wo.TvaRate / 100.0), 2);
        var ttc = Math.Round(htNet + tva, 2);

        var company = (wo.PerformedBy ?? "").Trim();
        if (string.IsNullOrWhiteSpace(company)) company = "(Non défini)";

        return new WorkOrderAccountingRow
        {
            WorkOrderId = wo.Id,
            BdrNumber = wo.BdrNumber,
            RequestDate = wo.RequestDate.Date,

            Place = wo.Place ?? "",
            RequestedBy = wo.RequestedBy ?? "",
            Company = company,

            // ✅ On ajoute le forfait au Matériel pour l'affichage/export (pas de colonne
            // dédiée "Forfait" dans la grille/CSV) afin que Matériel+Main d'œuvre+
            // Déplacements reste cohérent avec le HT total.
            Material = Math.Round(material + (hasForfait ? forfait : 0), 2),
            Labor = labor,
            Travel = travel,

            // ✅ Compta = HT/TVA/TTC NET (après rabais)
            Ht = htNet,
            TvaRate = wo.TvaRate,
            Tva = tva,
            Ttc = ttc
        };
    }

    // =========================
    // Colors + contrast
    // =========================
    private static MediaBrush BrushFromHexOrDefault(string? hex, MediaBrush def)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
                return def;

            var s = hex.Trim();
            if (!s.StartsWith("#", StringComparison.Ordinal)) s = "#" + s;

            var c = (MediaColor)MediaColorConverter.ConvertFromString(s);
            return new MediaSolidColorBrush(c);
        }
        catch
        {
            return def;
        }
    }

    private Dictionary<string, string> GetCompanyColorMap()
    {
        var pid = Db.GetCurrentProjectId();
        if (pid.HasValue && pid.Value > 0)
            return Db.GetCompanyColorMap(pid.Value);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private MediaBrush GetCompanyBackgroundBrush(string company, Dictionary<string, string> colorMap)
    {
        colorMap.TryGetValue(company, out var hex);
        return BrushFromHexOrDefault(hex, MediaBrushes.Transparent);
    }

    // =========================
    // Tri commun (table + graphique)
    // =========================
    private List<CompanyTotalsRow> ApplySort(List<CompanyTotalsRow> rows)
    {
        return _sortMode switch
        {
            SortMode.TotalTtcAsc => rows.OrderBy(r => r.TotalTtc).ToList(),
            SortMode.Alpha => rows.OrderBy(r => r.Company, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rows.OrderByDescending(r => r.TotalTtc).ToList(),
        };
    }

    // =========================
    // Chart scale + render
    // =========================
    private void UpdateChartScaleFromActualWidth()
    {
        var w = ChartItems?.ActualWidth ?? 0;

        // Dans XAML: label 140 + spacer 4 + valeur 70 + marge sécurité
        var usable = w - (140 + 4 + 70 + 30);

        _chartMaxBarPx = Math.Max(140, usable);
    }

    private void RenderTotalsTableAndChart()
    {
        if (_lastCompanyRows == null)
            return;

        UpdateChartScaleFromActualWidth();

        // 1) Tri commun
        var sorted = ApplySort(_lastCompanyRows);

        // 2) TOTAL (général)
        var totalRow = new CompanyTotalsRow
        {
            IsTotal = true,
            Company = "TOTAL",
            Count = sorted.Sum(r => r.Count),
            TotalHt = Math.Round(sorted.Sum(r => r.TotalHt), 2),
            TotalTva = Math.Round(sorted.Sum(r => r.TotalTva), 2),
            TotalTtc = Math.Round(sorted.Sum(r => r.TotalTtc), 2),
            CompanyBrush = MediaBrushes.Transparent,
            CompanyTextBrush = MediaBrushes.Black
        };

        // 3) TABLE: tri commun + total à la fin
        var byCompanyWithTotal = new List<CompanyTotalsRow>();
        byCompanyWithTotal.AddRange(sorted);
        byCompanyWithTotal.Add(totalRow);

        ByCompanyGrid.ItemsSource = byCompanyWithTotal;
        ByCompanyGrid.Items.Refresh();

        // 4) Graph header : Total TTC général (montant à droite)
        if (ChartTotalTtcTextBlock != null)
            ChartTotalTtcTextBlock.Text = $"{totalRow.TotalTtc:0.00}";

        // 5) garder le titre fixe
        if (ChartTitleTextBlock != null)
            ChartTitleTextBlock.Text = "Graphique-TTC par entreprise";

        // 6) GRAPH
        double max = sorted.Count == 0 ? 0 : sorted.Max(r => r.TotalTtc);

        var chart = sorted.Select(r =>
        {
            var barBrush = r.CompanyBrush;
            if (barBrush == null || barBrush == MediaBrushes.Transparent)
                barBrush = MediaBrushes.SteelBlue;

            return new CompanyChartRow
            {
                Company = r.Company,
                TotalTtc = r.TotalTtc,
                BarWidth = (max <= 0) ? 0 : Math.Round((r.TotalTtc / max) * _chartMaxBarPx, 0),
                BarBrush = barBrush
            };
        }).ToList();

        ChartItems.ItemsSource = chart;
        ChartItems.Items.Refresh();
    }

    private void ChartSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        _sortMode = ChartSortComboBox?.SelectedIndex switch
        {
            1 => SortMode.TotalTtcAsc,
            2 => SortMode.Alpha,
            _ => SortMode.TotalTtcDesc
        };

        RenderTotalsTableAndChart();
    }

    // =========================
    // Render (filters -> compute -> render)
    // =========================
    private void ApplyFiltersAndRender()
    {
        var filtered = ApplyWorkOrderFilters();
        _currentRows = filtered.Select(ComputeRow).ToList();

        // ✅ conserver le map pour le titre "Détail"
        _companyColorMap = GetCompanyColorMap();

        // Lignes entreprises (sans TOTAL)
        var companyRows = _currentRows
            .GroupBy(r => r.Company)
            .Select(g =>
            {
                var companyName = g.Key;

                var ht = Math.Round(g.Sum(x => x.Ht), 2);
                var tva = Math.Round(g.Sum(x => x.Tva), 2);
                var ttc = Math.Round(g.Sum(x => x.Ttc), 2);

                var bg = GetCompanyBackgroundBrush(companyName, _companyColorMap);
                var fg = GetTextBrushForBackground(bg);

                return new CompanyTotalsRow
                {
                    IsTotal = false,
                    Company = companyName,
                    Count = g.Count(),
                    TotalHt = ht,
                    TotalTva = tva,
                    TotalTtc = ttc,
                    CompanyBrush = bg,
                    CompanyTextBrush = fg
                };
            })
            .ToList();

        _lastCompanyRows = companyRows.ToList();

        // Rendu table + chart (tri commun + total)
        RenderTotalsTableAndChart();

        // Reset Détail
        DetailsTitleTextBlock.Text = "Détail — sélectionne une entreprise";
        DetailsTitleBorder.Background = MediaBrushes.Transparent;
        DetailsTitleTextBlock.Foreground = MediaBrushes.Black;

        DetailsGrid.ItemsSource = null;
        DetailsGrid.Items.Refresh();
    }

    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => ApplyFiltersAndRender();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void ByCompanyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ByCompanyGrid.SelectedItem is not CompanyTotalsRow row)
            return;

        if (row.IsTotal)
        {
            DetailsTitleTextBlock.Text = "Détail — sélectionne une entreprise";
            DetailsTitleBorder.Background = MediaBrushes.Transparent;
            DetailsTitleTextBlock.Foreground = MediaBrushes.Black;

            DetailsGrid.ItemsSource = null;
            DetailsGrid.Items.Refresh();
            return;
        }

        var company = row.Company;
        DetailsTitleTextBlock.Text = $"Détail — {company}";

        // ✅ surbrillance du titre avec la couleur entreprise
        var bg = GetCompanyBackgroundBrush(company, _companyColorMap);
        var fg = GetTextBrushForBackground(bg);
        DetailsTitleBorder.Background = bg;
        DetailsTitleTextBlock.Foreground = fg;

        var details = _currentRows
            .Where(r => string.Equals(r.Company, company, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.RequestDate)
            .ThenByDescending(r => r.BdrNumber)
            .Select(r =>
            {
                var bg2 = GetCompanyBackgroundBrush(r.Company, _companyColorMap);
                var fg2 = GetTextBrushForBackground(bg2);

                return new DetailsRow
                {
                    IsTotal = false,
                    WorkOrderId = r.WorkOrderId,
                    Bdr = $"BDR-{r.BdrNumber}",
                    Date = r.RequestDate,

                    Company = r.Company,
                    CompanyBrush = bg2,
                    CompanyTextBrush = fg2,

                    Ht = r.Ht,
                    Tva = r.Tva,
                    Ttc = r.Ttc
                };
            })
            .ToList();

        var total = new DetailsRow
        {
            IsTotal = true,
            WorkOrderId = 0,
            Bdr = "TOTAL",
            Date = null,

            Company = "",
            CompanyBrush = MediaBrushes.Transparent,
            CompanyTextBrush = MediaBrushes.Black,

            Ht = Math.Round(details.Sum(x => x.Ht), 2),
            Tva = Math.Round(details.Sum(x => x.Tva), 2),
            Ttc = Math.Round(details.Sum(x => x.Ttc), 2)
        };

        details.Add(total);

        DetailsGrid.ItemsSource = details;
        DetailsGrid.Items.Refresh();
    }

    // ✅ Clic sur une ligne du détail -> ouvre le bon de régie
    private void DetailsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailsGrid.SelectedItem is not DetailsRow row)
            return;

        if (row.IsTotal || row.WorkOrderId <= 0)
            return;

        try
        {
            var win = new WorkOrderWindow(row.WorkOrderId, WorkOrderEditMode.Architecte)
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
                    System.Diagnostics.Debug.WriteLine("Exception opening WorkOrderWindow from AccountingPage: " + ex);
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                    System.IO.File.WriteAllText(path, ex.ToString());
                    // Silent fallback: no MessageBox shown
                }
                catch { }
            }

            // ✅ BUG (Comptabilité non mise à jour automatiquement) : ouvrir un bon depuis le
            // détail Comptabilité créait sa propre WorkOrderWindow directement (sans passer par
            // MainWindow.OpenWorkOrder, qui rafraîchit MainContent après fermeture). Si l'architecte
            // validait/modifiait le bon depuis cette fenêtre puis la fermait, la page Comptabilité
            // (totaux par entreprise + détail) restait figée sur les anciennes valeurs tant qu'on ne
            // cliquait pas manuellement sur "Rafraîchir". On recharge donc explicitement ici.
            Reload();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible d’ouvrir le bon d'intervention.\n\n{ex.Message}",
                "Ouverture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // -------------------------
    // ✅ Export PDF (handler attendu par XAML : Click="ExportPdf_Click")
    // -------------------------
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AccountingPrintArea == null)
                throw new InvalidOperationException("AccountingPrintArea introuvable.");

            AccountingPrintArea.UpdateLayout();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            var png = RenderElementToPng(AccountingPrintArea, scale: 2.0);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Enregistrer le PDF (Comptabilité)",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"comptabilite-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true
            };

            if (dlg.ShowDialog() != true)
                return;

            PdfService.GenerateAccountingPdfFromBitmapPng(dlg.FileName, png);

            WpfMessageBox.Show("PDF Comptabilité généré.", "PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible de générer le PDF Comptabilité.\n\n{ex.Message}",
                "PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static byte[] RenderElementToPng(FrameworkElement element, double scale)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (scale <= 0) scale = 1.0;

        // ✅ Mesure/Arrange + capture via VisualBrush (évite COMException “Aucune disposition actuellement disponible”)
        element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(new System.Windows.Size(element.DesiredSize.Width, element.DesiredSize.Height)));
        element.UpdateLayout();

        var widthDip = Math.Max(1.0, element.ActualWidth > 0 ? element.ActualWidth : element.DesiredSize.Width);
        var heightDip = Math.Max(1.0, element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height);

        int widthPx = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
        int heightPx = Math.Max(1, (int)Math.Ceiling(heightDip * scale));

        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            var vb = new VisualBrush(element) { Stretch = Stretch.None };
            ctx.DrawRectangle(vb, null, new Rect(new System.Windows.Size(widthDip, heightDip)));
        }

        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // -------------------------
    // Export CSV
    // -------------------------
    private static string CsvEscape(string s)
    {
        s ??= "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private void ExportDetailsCsv_Click(object sender, RoutedEventArgs e)
    {
        var filtered = ApplyWorkOrderFilters();
        var rows = filtered.Select(ComputeRow).OrderBy(r => r.Company).ThenBy(r => r.BdrNumber).ToList();

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter CSV (détail)",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"comptabilite-detail-{DateTime.Today:yyyyMMdd}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (sfd.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("BDR;Date;Entreprise;Lieu;Demande_par;Materiel;Main_oeuvre;Deplacements;HT;TVA_% ;TVA;TTC");

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(";",
                CsvEscape($"BDR-{r.BdrNumber}"),
                CsvEscape(r.RequestDate.ToString("yyyy-MM-dd")),
                CsvEscape(r.Company),
                CsvEscape(r.Place),
                CsvEscape(r.RequestedBy),
                F2(r.Material),
                F2(r.Labor),
                F2(r.Travel),
                F2(r.Ht),
                r.TvaRate.ToString("0.00", CultureInfo.InvariantCulture),
                F2(r.Tva),
                F2(r.Ttc)
            ));
        }

        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
        WpfMessageBox.Show("CSV détail exporté.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportCompaniesCsv_Click(object sender, RoutedEventArgs e)
    {
        var filtered = ApplyWorkOrderFilters();
        var rows = filtered.Select(ComputeRow).ToList();

        var grouped = rows
            .GroupBy(r => r.Company)
            .Select(g => new
            {
                Company = g.Key,
                Count = g.Count(),
                TotalHt = Math.Round(g.Sum(x => x.Ht), 2),
                TotalTva = Math.Round(g.Sum(x => x.Tva), 2),
                TotalTtc = Math.Round(g.Sum(x => x.Ttc), 2),
            })
            .OrderByDescending(r => r.TotalTtc)
            .ToList();

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter CSV (totaux entreprises)",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"comptabilite-totaux-entreprises-{DateTime.Today:yyyyMMdd}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (sfd.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Entreprise;Nb_de_bons;HT;TVA;TTC");

        foreach (var r in grouped)
        {
            sb.AppendLine(string.Join(";",
                CsvEscape(r.Company),
                r.Count.ToString(CultureInfo.InvariantCulture),
                F2(r.TotalHt),
                F2(r.TotalTva),
                F2(r.TotalTtc)
            ));
        }

        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
        WpfMessageBox.Show("CSV totaux exporté.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}