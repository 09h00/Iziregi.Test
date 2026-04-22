// File: Pages/AccountingPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Microsoft.Win32;

namespace Iziregi.Test.Pages;

public partial class AccountingPage : UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    private List<Project> _projects = new();
    private List<WorkOrder> _allAccountingWorkOrders = new();
    private List<WorkOrderAccountingRow> _currentRows = new();

    public AccountingPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        var today = DateTime.Today;
        FromDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        ToDatePicker.SelectedDate = FromDatePicker.SelectedDate.Value.AddMonths(1).AddDays(-1);

        FromDatePicker.SelectedDateChanged += (_, __) => ApplyFiltersAndRender();
        ToDatePicker.SelectedDateChanged += (_, __) => ApplyFiltersAndRender();
    }

    private class CompanyTotalsRow
    {
        public string Company { get; set; } = "";
        public double TotalHt { get; set; }
        public double TotalTva { get; set; }
        public double TotalTtc { get; set; }
        public int Count { get; set; }
    }

    private class CompanyChartRow
    {
        public string Company { get; set; } = "";
        public double TotalTtc { get; set; }
        public double BarHeight { get; set; }
    }

    private class WorkOrderAccountingRow
    {
        public long WorkOrderId { get; set; }
        public int BdrNumber { get; set; }
        public DateTime RequestDate { get; set; }
        public string Place { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public string Company { get; set; } = "";
        public string ProjectName { get; set; } = "";

        public double Material { get; set; }
        public double Labor { get; set; }
        public double Travel { get; set; }

        public double Ht { get; set; }
        public double TvaRate { get; set; }
        public double Tva { get; set; }
        public double Ttc { get; set; }
    }

    private class DetailsRow
    {
        public string Bdr { get; set; } = "";
        public DateTime Date { get; set; }
        // ✅ Projet supprimé
        public string Place { get; set; } = "";
        public double Ht { get; set; }
        public double Tva { get; set; }
        public double Ttc { get; set; }
    }

    public void Reload()
    {
        _allAccountingWorkOrders = Db.GetWorkOrdersForAccounting();
        _projects = Db.GetProjects(onlyActive: false);

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

        var projectItems = new List<string> { "Tous" };
        projectItems.AddRange(_projects.OrderBy(p => p.Name).Select(p => $"[{p.Id}] {p.Name}"));

        ProjectFilterComboBox.ItemsSource = projectItems;
        ProjectFilterComboBox.SelectedIndex = 0;
    }

    private (DateTime? from, DateTime? to) GetDateRange()
    {
        var from = FromDatePicker.SelectedDate?.Date;
        var to = ToDatePicker.SelectedDate?.Date;
        return (from, to);
    }

    private long? GetSelectedProjectId()
    {
        var s = ProjectFilterComboBox.SelectedItem?.ToString() ?? "";
        if (!s.StartsWith("[", StringComparison.OrdinalIgnoreCase)) return null;

        var end = s.IndexOf(']');
        if (end <= 1) return null;

        var idStr = s.Substring(1, end - 1);
        return long.TryParse(idStr, out var id) ? id : null;
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
        var projectId = GetSelectedProjectId();

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

        if (projectId.HasValue)
            q = q.Where(w => w.ProjectId == projectId.Value);

        return q.ToList();
    }

    private WorkOrderAccountingRow ComputeRow(WorkOrder wo)
    {
        var lines = Db.GetWorkOrderLines(wo.Id);
        var material = Math.Round(lines.Sum(l => l.LineTotal), 2);

        var labor = Math.Round(wo.LaborHours * wo.LaborRate, 2);
        var travel = Math.Round(wo.TravelQty * wo.TravelRate, 2);

        var ht = Math.Round(material + labor + travel, 2);
        var tva = Math.Round(ht * (wo.TvaRate / 100.0), 2);
        var ttc = Math.Round(ht + tva, 2);

        var company = (wo.PerformedBy ?? "").Trim();
        if (string.IsNullOrWhiteSpace(company)) company = "(Non défini)";

        string projectName = "";
        if (wo.ProjectId.HasValue)
            projectName = _projects.FirstOrDefault(p => p.Id == wo.ProjectId.Value)?.Name ?? "";

        return new WorkOrderAccountingRow
        {
            WorkOrderId = wo.Id,
            BdrNumber = wo.BdrNumber,
            RequestDate = wo.RequestDate.Date,
            Place = wo.Place ?? "",
            RequestedBy = wo.RequestedBy ?? "",
            Company = company,
            ProjectName = projectName,

            Material = material,
            Labor = labor,
            Travel = travel,

            Ht = ht,
            TvaRate = wo.TvaRate,
            Tva = tva,
            Ttc = ttc
        };
    }

    private void ApplyFiltersAndRender()
    {
        var filtered = ApplyWorkOrderFilters();
        _currentRows = filtered.Select(ComputeRow).ToList();

        var grouped = _currentRows
            .GroupBy(r => r.Company)
            .Select(g => new CompanyTotalsRow
            {
                Company = g.Key,
                TotalHt = Math.Round(g.Sum(x => x.Ht), 2),
                TotalTva = Math.Round(g.Sum(x => x.Tva), 2),
                TotalTtc = Math.Round(g.Sum(x => x.Ttc), 2),
                Count = g.Count()
            })
            .OrderByDescending(r => r.TotalTtc)
            .ToList();

        ByCompanyGrid.ItemsSource = grouped;
        ByCompanyGrid.Items.Refresh();

        var totalHt = Math.Round(grouped.Sum(r => r.TotalHt), 2);
        var totalTva = Math.Round(grouped.Sum(r => r.TotalTva), 2);
        var totalTtc = Math.Round(grouped.Sum(r => r.TotalTtc), 2);

        TotalHtTextBlock.Text = totalHt.ToString("0.00", CultureInfo.InvariantCulture);
        TotalTvaTextBlock.Text = totalTva.ToString("0.00", CultureInfo.InvariantCulture);
        TotalTtcTextBlock.Text = totalTtc.ToString("0.00", CultureInfo.InvariantCulture);

        double max = grouped.Count == 0 ? 0 : grouped.Max(r => r.TotalTtc);
        double maxBarPx = 140;

        var chart = grouped.Select(r => new CompanyChartRow
        {
            Company = r.Company,
            TotalTtc = r.TotalTtc,
            BarHeight = (max <= 0) ? 0 : Math.Round((r.TotalTtc / max) * maxBarPx, 0)
        }).ToList();

        ChartItems.ItemsSource = chart;
        ChartItems.Items.Refresh();

        DetailsTitleTextBlock.Text = "Détail — sélectionne une entreprise";
        DetailsGrid.ItemsSource = null;
        DetailsGrid.Items.Refresh();
    }

    private void Filters_Changed(object sender, SelectionChangedEventArgs e)
        => ApplyFiltersAndRender();

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => Reload();

    private void ByCompanyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ByCompanyGrid.SelectedItem is not CompanyTotalsRow row)
            return;

        var company = row.Company;

        DetailsTitleTextBlock.Text = $"Détail — {company}";

        var details = _currentRows
            .Where(r => string.Equals(r.Company, company, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.RequestDate)
            .ThenByDescending(r => r.BdrNumber)
            .Select(r => new DetailsRow
            {
                Bdr = $"BDR-{r.BdrNumber}",
                Date = r.RequestDate,
                Place = r.Place,
                Ht = r.Ht,
                Tva = r.Tva,
                Ttc = r.Ttc
            })
            .ToList();

        DetailsGrid.ItemsSource = details;
        DetailsGrid.Items.Refresh();
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

        var sfd = new SaveFileDialog
        {
            Title = "Exporter CSV (détail)",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"comptabilite-detail-{DateTime.Today:yyyyMMdd}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (sfd.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("BDR;Date;Projet;Entreprise;Lieu;Demande_par;Materiel;Main_oeuvre;Deplacements;HT;TVA_% ;TVA;TTC");

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(";",
                CsvEscape($"BDR-{r.BdrNumber}"),
                CsvEscape(r.RequestDate.ToString("yyyy-MM-dd")),
                CsvEscape(r.ProjectName),
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
        MessageBox.Show("CSV détail exporté.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportCompaniesCsv_Click(object sender, RoutedEventArgs e)
    {
        var filtered = ApplyWorkOrderFilters();
        var rows = filtered.Select(ComputeRow).ToList();

        var grouped = rows
            .GroupBy(r => r.Company)
            .Select(g => new CompanyTotalsRow
            {
                Company = g.Key,
                TotalHt = Math.Round(g.Sum(x => x.Ht), 2),
                TotalTva = Math.Round(g.Sum(x => x.Tva), 2),
                TotalTtc = Math.Round(g.Sum(x => x.Ttc), 2),
                Count = g.Count()
            })
            .OrderByDescending(r => r.TotalTtc)
            .ToList();

        var sfd = new SaveFileDialog
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
        MessageBox.Show("CSV totaux exporté.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}