// File: Services/PdfService.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using System.Windows;
using System.Windows.Media.Imaging;

using Iziregi.Test.Data;
using Iziregi.Test.Models;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Iziregi.Test.Services;

public static class PdfService
{
    public static void Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // =========================
    // ✅ PDF Comptabilité (PAYSAGE) + EN-TÊTE COMPLET (logo + coords architecte + coords projet)
    // =========================
    public static void GenerateAccountingPdfFromBitmapPng(string filePath, byte[] pngBytes)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        if (pngBytes == null || pngBytes.Length == 0)
            throw new ArgumentException("Image PNG vide.", nameof(pngBytes));

        const float margin = 26f;

        var pageSize = PageSizes.A4.Landscape();

        var architectName = Db.GetArchitectName();
        var architectAddress = Db.GetArchitectAddress();
        var logoPath = Db.GetArchitectLogoPath();

        byte[]? logoBytes = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                logoBytes = File.ReadAllBytes(logoPath);
        }
        catch
        {
            logoBytes = null;
        }

        var project = Db.GetCurrentProject();
        var projectName = project?.Name ?? "";
        var projectAddress = project?.Address ?? "";

        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;
        var lineLight = Colors.Grey.Lighten2;
        var separatorBlack = Colors.Grey.Darken4;

        var slices = SlicePngVerticallyIntoPages(pngBytes, pageSize);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(margin);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor(textMain));

                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(1.2f).Row(left =>
                            {
                                if (logoBytes != null && logoBytes.Length > 0)
                                {
                                    left.ConstantItem(52)
                                        .Height(52)
                                        .Border(1)
                                        .BorderColor(lineLight)
                                        .Padding(4)
                                        .Image(logoBytes)
                                        .FitArea();

                                    left.ConstantItem(10);
                                }

                                left.RelativeItem().Column(c =>
                                {
                                    c.Item()
                                        .Text(string.IsNullOrWhiteSpace(architectName) ? "Architecte" : architectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var a1 = GetAddressLine1(architectAddress);
                                    var a2 = GetAddressLine2(architectAddress);

                                    if (!string.IsNullOrWhiteSpace(a1))
                                    {
                                        c.Item().PaddingTop(2).Text(a1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(a2))
                                    {
                                        c.Item().Text(a2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });

                            row.RelativeItem(0.9f).AlignCenter().Column(center =>
                            {
                                center.Item()
                                    .Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm"))
                                    .FontSize(9)
                                    .FontColor(textMuted);
                            });

                            row.RelativeItem(1.2f).Element(right =>
                            {
                                right.AlignRight().Column(c =>
                                {
                                    c.Item()
                                        .AlignLeft()
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Projet / chantier" : projectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var p1 = GetAddressLine1(projectAddress);
                                    var p2 = GetAddressLine2(projectAddress);

                                    if (!string.IsNullOrWhiteSpace(p1))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(2)
                                            .Text(p1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(p2))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(p2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(separatorBlack);
                    });
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    for (int i = 0; i < slices.Count; i++)
                    {
                        if (i > 0)
                            col.Item().PageBreak();

                        col.Item().Image(slices[i]).FitArea();
                    }
                });
            });
        }).GeneratePdf(filePath);
    }

    // =========================
    // ✅ PDF Planning (CAPTURE) — sections entières + titres
    // =========================
    public static void GeneratePlanningPdfFromSections(string filePath, List<byte[]> sectionPngs)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        sectionPngs ??= new List<byte[]>();
        sectionPngs = sectionPngs.Where(x => x != null && x.Length > 0).ToList();

        if (sectionPngs.Count == 0)
            throw new ArgumentException("Aucune section à exporter (PNG vide).", nameof(sectionPngs));

        const float margin = 26f;
        var pageSize = PageSizes.A4;

        var architectName = Db.GetArchitectName();
        var architectAddress = Db.GetArchitectAddress();
        var logoPath = Db.GetArchitectLogoPath();

        byte[]? logoBytes = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                logoBytes = File.ReadAllBytes(logoPath);
        }
        catch
        {
            logoBytes = null;
        }

        var project = Db.GetCurrentProject();
        var projectName = project?.Name ?? "";
        var projectAddress = project?.Address ?? "";

        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;
        var lineLight = Colors.Grey.Lighten2;
        var separatorBlack = Colors.Grey.Darken4;

        var planningSectionTitles = new[] { "", "Planning hebdomadaire", "Plan" };

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(margin);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor(textMain));

                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            // Gauche
                            row.RelativeItem(1.2f).Row(left =>
                            {
                                if (logoBytes != null && logoBytes.Length > 0)
                                {
                                    left.ConstantItem(52)
                                        .Height(52)
                                        .Border(1)
                                        .BorderColor(lineLight)
                                        .Padding(4)
                                        .Image(logoBytes)
                                        .FitArea();

                                    left.ConstantItem(10);
                                }

                                left.RelativeItem().Column(c =>
                                {
                                    c.Item()
                                        .Text(string.IsNullOrWhiteSpace(architectName) ? "Architecte" : architectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var a1 = GetAddressLine1(architectAddress);
                                    var a2 = GetAddressLine2(architectAddress);

                                    if (!string.IsNullOrWhiteSpace(a1))
                                    {
                                        c.Item().PaddingTop(2).Text(a1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(a2))
                                    {
                                        c.Item().Text(a2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });

                            // Centre : date uniquement
                            row.RelativeItem(0.9f).AlignCenter().Column(center =>
                            {
                                center.Item()
                                    .Text(DateTime.Now.ToString("dd.MM.yyyy"))
                                    .FontSize(9)
                                    .FontColor(textMuted);
                            });

                            // Droite
                            row.RelativeItem(1.2f).Element(right =>
                            {
                                right.AlignRight().Column(c =>
                                {
                                    c.Item()
                                        .AlignLeft()
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Projet / chantier" : projectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var p1 = GetAddressLine1(projectAddress);
                                    var p2 = GetAddressLine2(projectAddress);

                                    if (!string.IsNullOrWhiteSpace(p1))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(2)
                                            .Text(p1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(p2))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(p2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(separatorBlack);
                    });
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    for (int i = 0; i < sectionPngs.Count; i++)
                    {
                        var png = sectionPngs[i];

                        string? title = null;
                        if (i >= 0 && i < planningSectionTitles.Length)
                            title = planningSectionTitles[i];

                        col.Item().ShowEntire().Column(section =>
                        {
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                section.Item()
                                    .Text(title)
                                    .SemiBold()
                                    .FontSize(12)
                                    .FontColor(textMain);

                                section.Item().PaddingTop(4);
                            }

                            section.Item().Image(png).FitArea();
                        });
                    }
                });
            });
        }).GeneratePdf(filePath);
    }

    // =========================
    // ✅ PDF Bon de régie COMPLET (Demande + Devis + Validation)
    // =========================
    public static void GenerateWorkOrderPdf(string filePath, WorkOrder wo, List<WorkOrderLine> lines)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        if (wo == null) throw new ArgumentNullException(nameof(wo));
        lines ??= new List<WorkOrderLine>();

        var culture = new CultureInfo("fr-CH");

        // ---- Données header
        var architectName = Db.GetArchitectName();
        var architectAddress = Db.GetArchitectAddress();
        var logoPath = Db.GetArchitectLogoPath();

        byte[]? logoBytes = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                logoBytes = File.ReadAllBytes(logoPath);
        }
        catch { logoBytes = null; }

        var project = wo.ProjectId.HasValue
            ? Db.GetProjectById(wo.ProjectId.Value)
            : Db.GetCurrentProject();

        var projectName = project?.Name ?? "";
        var projectAddress = project?.Address ?? "";

        var tag = (wo.ProjectId.HasValue && wo.ProjectId.Value > 0) ? $"P{wo.ProjectId.Value}" : "";
        var bdrShort = wo.BdrNumber < 10 ? $"0{wo.BdrNumber}" : wo.BdrNumber.ToString();

        // ---- Couleurs
        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;

        var lineLight = Colors.Grey.Lighten2;
        var lineLighter = Colors.Grey.Lighten3;
        var headerBg = Colors.Grey.Lighten4;

        var separatorBlack = Colors.Grey.Darken4;

        // ---- Devis: calculs (rabais après HT brut, avant TVA/TTC)
        lines = lines.Where(l => l != null).ToList();

        double materialTotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        double laborTotal = Math.Round(wo.LaborHours * wo.LaborRate, 2);
        double travelTotal = Math.Round(wo.TravelQty * wo.TravelRate, 2);

        double forfaitTotal = Math.Round(wo.ForfaitQty * wo.ForfaitUnitPrice, 2);
        bool hasForfait = Math.Abs(forfaitTotal) > 0.0000000001;

        double htBrut = Math.Round(materialTotal + laborTotal + travelTotal + (hasForfait ? forfaitTotal : 0), 2);

        double discountRate = wo.DiscountRate;
        if (double.IsNaN(discountRate) || double.IsInfinity(discountRate)) discountRate = 0;
        discountRate = Math.Max(0, discountRate);

        double htNet = Math.Round(htBrut * (1.0 - (discountRate / 100.0)), 2);

        double discountAmount = Math.Round(htNet - htBrut, 2); // négatif si rabais
        double tvaRate = wo.TvaRate;
        if (double.IsNaN(tvaRate) || double.IsInfinity(tvaRate)) tvaRate = 0;

        double tvaAmount = Math.Round(htNet * (tvaRate / 100.0), 2);
        double ttcTotal = Math.Round(htNet + tvaAmount, 2);

        // ---- Validation (signature)
        byte[]? signatureBytes = null;
        try { signatureBytes = wo.SignaturePng; } catch { signatureBytes = null; }
        bool hasSignature = signatureBytes != null && signatureBytes.Length > 0;

        var decision = (wo.ValidationDecision ?? "").Trim();
        var sigName = (wo.SignatureName ?? "").Trim();
        var sigDate = wo.SignatureDate.HasValue ? FormatDateShort(wo.SignatureDate.Value) : "";

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor(textMain));

                // =========================
                // HEADER
                // =========================
                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(1.2f).Row(left =>
                            {
                                if (logoBytes != null && logoBytes.Length > 0)
                                {
                                    left.ConstantItem(52)
                                        .Height(52)
                                        .Border(1)
                                        .BorderColor(lineLight)
                                        .Padding(4)
                                        .Image(logoBytes)
                                        .FitArea();

                                    left.ConstantItem(10);
                                }

                                left.RelativeItem().Column(c =>
                                {
                                    c.Item()
                                        .Text(string.IsNullOrWhiteSpace(architectName) ? "Architecte" : architectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var a1 = GetAddressLine1(architectAddress);
                                    var a2 = GetAddressLine2(architectAddress);

                                    if (!string.IsNullOrWhiteSpace(a1))
                                    {
                                        c.Item().PaddingTop(2).Text(a1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(a2))
                                    {
                                        c.Item().Text(a2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });

                            row.RelativeItem(0.9f).Column(center =>
                            {
                                center.Item().AlignCenter().Text("Bon de régie")
                                    .SemiBold()
                                    .FontSize(14);

                                center.Item().AlignCenter().PaddingTop(2).Row(r =>
                                {
                                    r.Spacing(4);

                                    r.AutoItem()
                                        .AlignBottom()
                                        .Text(bdrShort)
                                        .SemiBold()
                                        .FontSize(16);

                                    if (!string.IsNullOrWhiteSpace(tag))
                                    {
                                        r.AutoItem()
                                            .AlignBottom()
                                            .Text($"-{tag}")
                                            .SemiBold()
                                            .FontSize(11)
                                            .FontColor(textMuted);
                                    }
                                });

                                center.Item().AlignCenter().PaddingTop(2).Text($"Créé le {FormatDateShort(wo.RequestDate)}")
                                    .FontSize(9)
                                    .FontColor(textMuted);
                            });

                            row.RelativeItem(1.2f).Element(right =>
                            {
                                right.AlignRight().Column(c =>
                                {
                                    c.Item()
                                        .AlignLeft()
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Projet / chantier" : projectName)
                                        .SemiBold()
                                        .FontSize(12);

                                    var p1 = GetAddressLine1(projectAddress);
                                    var p2 = GetAddressLine2(projectAddress);

                                    if (!string.IsNullOrWhiteSpace(p1))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(2)
                                            .Text(p1)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(p2))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(p2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(separatorBlack);

                        if (wo.IsCancelled)
                        {
                            col.Item().PaddingTop(6)
                                .Background(headerBg)
                                .Border(1)
                                .BorderColor(lineLight)
                                .Padding(6)
                                .AlignCenter()
                                .Text("STATUT : ANNULÉ")
                                .SemiBold()
                                .FontColor(textMain);
                        }
                    });
                });

                // =========================
                // CONTENT
                // =========================
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    // -------------------------
                    // DEMANDE
                    // -------------------------
                    col.Item().ShowEntire().Column(section =>
                    {
                        section.Item().Text("Demande")
                            .SemiBold()
                            .FontSize(13);

                        section.Item().PaddingTop(5).Row(row =>
                        {
                            row.RelativeItem(1f).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                });

                                AddInfoCell(t, "Concerne", wo.Reserve, lineLighter, textMuted, rightDivider: true);
                                AddInfoCell(t, "Demandé par", wo.RequestedBy, lineLighter, textMuted);

                                AddInfoCell(t, "Entreprise", wo.PerformedBy, lineLighter, textMuted, rightDivider: true);
                                AddInfoCell(t, "Bâtiment", wo.Place, lineLighter, textMuted);

                                AddInfoCell(t, "Étage", wo.Etage, lineLighter, textMuted, rightDivider: true);

                                var deadline = wo.DeadlineDate == default ? "" : FormatDateShort(wo.DeadlineDate);
                                AddInfoCell(t, "Délai", string.IsNullOrWhiteSpace(deadline) ? null : deadline, lineLighter, textMuted);
                            });

                            row.ConstantItem(15);

                            row.RelativeItem(1f).Column(desc =>
                            {
                                desc.Item()
                                    .Text("Descriptif")
                                    .SemiBold()
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken4);

                                var description = (wo.Description ?? "").Trim();

                                if (string.IsNullOrWhiteSpace(description))
                                {
                                    desc.Item().PaddingTop(2).Text("—").FontColor(textMuted);
                                    return;
                                }

                                var descriptionLines = description
                                    .Replace("\r\n", "\n")
                                    .Replace("\r", "\n")
                                    .Split('\n', StringSplitOptions.None)
                                    .Select(x => (x ?? "").TrimEnd())
                                    .ToList();

                                desc.Item().PaddingTop(2).Column(list =>
                                {
                                    for (int i = 0; i < descriptionLines.Count; i++)
                                    {
                                        var ln = descriptionLines[i];
                                        list.Item().Text(string.IsNullOrWhiteSpace(ln) ? " " : ln).FontSize(10);
                                    }
                                });
                            });
                        });

                        section.Item().PaddingTop(6).LineHorizontal(1).LineColor(separatorBlack);
                    });

                    // -------------------------
                    // DEVIS (Nom centré)
                    // -------------------------
                    col.Item().ShowEntire().Column(section =>
                    {
                        var d = wo.QuoteDate == default ? "" : FormatDateShort(wo.QuoteDate);
                        var quoteName = string.IsNullOrWhiteSpace(wo.QuoteName) ? "—" : wo.QuoteName;
                        var dateText = string.IsNullOrWhiteSpace(d) ? "—" : d;

                        section.Item().Row(r =>
                        {
                            r.RelativeItem(0.45f).AlignLeft().Text("Devis").SemiBold().FontSize(13);
                            r.RelativeItem(1.10f).AlignCenter().Text($"Nom : {quoteName}").FontSize(10).FontColor(textMain);
                            r.RelativeItem(0.45f).AlignRight().Text($"Date : {dateText}").FontSize(10).FontColor(textMain);
                        });

                        var printableLines = lines
                            .Where(l =>
                            {
                                var label = (l.Label ?? "").Trim();
                                var hasNumbers =
                                    Math.Abs(l.Qty) > 0.0000000001 ||
                                    Math.Abs(l.UnitPrice) > 0.0000000001 ||
                                    Math.Abs(l.LineTotal) > 0.0000000001;
                                return !string.IsNullOrWhiteSpace(label) || hasNumbers;
                            })
                            .ToList();

                        section.Item().PaddingTop(6).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(1);
                                c.ConstantColumn(34);
                                c.ConstantColumn(56);
                                c.ConstantColumn(62);
                            });

                            t.Header(h =>
                            {
                                h.Cell().Element(CellHeader).Text("Libellé / Matériel");
                                h.Cell().Element(CellHeader).AlignRight().Text("Qt");
                                h.Cell().Element(CellHeader).AlignRight().Text("Prix/pc");
                                h.Cell().Element(CellHeader).AlignRight().Text("Total");
                            });

                            foreach (var l in printableLines)
                            {
                                var label = (l.Label ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(label)) label = "—";

                                t.Cell().Element(CellBody).Text(label);
                                t.Cell().Element(CellBody).AlignRight().Text(FormatQty(l.Qty, culture));
                                t.Cell().Element(CellBody).AlignRight().Text(l.UnitPrice.ToString("0.00", culture));
                                t.Cell().Element(CellBody).AlignRight().Text(l.LineTotal.ToString("0.00", culture));
                            }
                        });

                        section.Item().PaddingTop(8).Row(row =>
                        {
                            row.RelativeItem(0.7f).Column(left =>
                            {
                                left.Item().Text("Note").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);

                                var note = (wo.QuoteNotes ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(note)) note = "—";

                                var noteLines = note
                                    .Replace("\r\n", "\n")
                                    .Replace("\r", "\n")
                                    .Split('\n', StringSplitOptions.None)
                                    .Select(x => (x ?? "").TrimEnd())
                                    .ToList();

                                left.Item()
                                    .PaddingTop(3)
                                    .Border(1).BorderColor(lineLight)
                                    .Background(Colors.White)
                                    .Padding(6)
                                    .Column(list =>
                                    {
                                        foreach (var ln in noteLines)
                                            list.Item().Text(string.IsNullOrWhiteSpace(ln) ? " " : ln).FontSize(9);
                                    });
                            });

                            row.ConstantItem(14);

                            row.RelativeItem(1.0f).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(1);
                                    c.ConstantColumn(34);
                                    c.ConstantColumn(56);
                                    c.ConstantColumn(62);
                                });

                                AddTotalsRow4Cols(t, "Total Matériel", "", "", materialTotal.ToString("0.00", culture), isStrong: true);

                                if (!hasForfait)
                                {
                                    AddTotalsRow4Cols(t, "Main d’œuvre",
                                        qtyText: FormatQty(wo.LaborHours, culture),
                                        unitPriceText: wo.LaborRate.ToString("0.00", culture),
                                        totalText: laborTotal.ToString("0.00", culture),
                                        isStrong: false);

                                    AddTotalsRow4Cols(t, "Déplacements",
                                        qtyText: FormatQty(wo.TravelQty, culture),
                                        unitPriceText: wo.TravelRate.ToString("0.00", culture),
                                        totalText: travelTotal.ToString("0.00", culture),
                                        isStrong: false);
                                }

                                if (hasForfait)
                                {
                                    AddTotalsRow4Cols(t, "Forfait selon doc annexé",
                                        qtyText: FormatQty(wo.ForfaitQty, culture),
                                        unitPriceText: wo.ForfaitUnitPrice.ToString("0.00", culture),
                                        totalText: forfaitTotal.ToString("0.00", culture),
                                        isStrong: false);
                                }

                                var rabaisLabel = discountRate <= 0 ? "Rabais (%)" : $"Rabais ({discountRate:0}%)";
                                var discountRateText = discountRate <= 0 ? "" : $"{discountRate:0}%";
                                AddTotalsRow4Cols(t, rabaisLabel, "", discountRateText, discountAmount.ToString("0.00", culture), isStrong: false);

                                AddTotalsRow4Cols(t, "Total HT", "", "", htNet.ToString("0.00", culture), isStrong: true);
                                AddTotalsRow4Cols(t, $"TVA ({tvaRate:0.00}%)", "", "", tvaAmount.ToString("0.00", culture), isStrong: false);
                                AddTotalsRow4Cols(t, "Total TTC", "", "", ttcTotal.ToString("0.00", culture), isStrong: true);
                            });
                        });

                        section.Item().PaddingTop(6).LineHorizontal(1).LineColor(separatorBlack);
                    });

                    // -------------------------
                    // VALIDATION (✅ box signature remontée ; libellé "Signature" aligné sur Nom/Date ; bas box aligné bas Date)
                    // -------------------------
                    col.Item().ShowEntire().Column(section =>
                    {
                        // Constantes d'alignement (cohérentes avec la mise en page des tables à gauche)
                        const float titleHeight = 18f;         // hauteur visuelle du titre "Validation"
                        const float afterTitleToDecision = 6f; // PaddingTop(6)
                        const float decisionBlockHeight = 28f; // hauteur approx de la table décision (1 ligne)
                        const float betweenDecisionAndNameDate = 4f; // PaddingTop(4)
                        const float nameDateBlockHeight = 28f; // hauteur approx de la table Nom/Date (1 ligne)
                        const float signatureBoxHeight = decisionBlockHeight + betweenDecisionAndNameDate + nameDateBlockHeight; // bas aligné bas Date

                        section.Item().Row(row =>
                        {
                            // Gauche
                            row.RelativeItem(1.05f).Column(left =>
                            {
                                left.Item().Text("Validation").SemiBold().FontSize(13);

                                left.Item().PaddingTop(afterTitleToDecision).Table(t =>
                                {
                                    t.ColumnsDefinition(c => c.RelativeColumn());
                                    AddInfoCell1Col(t, "Décision", string.IsNullOrWhiteSpace(decision) ? "—" : decision, lineLighter);
                                });

                                left.Item().PaddingTop(betweenDecisionAndNameDate).Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn();
                                        c.RelativeColumn();
                                    });

                                    AddInfoCell(t, "Nom", string.IsNullOrWhiteSpace(sigName) ? "—" : sigName, lineLighter, textMuted, rightDivider: true);
                                    AddInfoCell(t, "Date", string.IsNullOrWhiteSpace(sigDate) ? "—" : sigDate, lineLighter, textMuted);
                                });
                            });

                            row.ConstantItem(14);

                            // Droite
                            row.RelativeItem(1.35f).Column(right =>
                            {
                                // ✅ La box doit commencer au même niveau que la zone décision (donc "remontée")
                                right.Item().Height(titleHeight); // réserve la ligne du titre pour aligner verticalement

                                // ✅ Ligne box signature (tout de suite après le titre)
                                right.Item().Row(sig =>
                                {
                                    // Libellé "Signature" : aligné sur Nom/Date => on le descend au niveau de la table Nom/Date
                                    sig.ConstantItem(58)
                                        .PaddingTop(afterTitleToDecision + decisionBlockHeight + betweenDecisionAndNameDate)
                                        .Text("Signature")
                                        .SemiBold()
                                        .FontSize(9)
                                        .FontColor(Colors.Grey.Darken4);

                                    // Box : remonte (débute juste sous le titre) et bas aligné au bas de Date
                                    var box = sig.RelativeItem()
                                        .PaddingTop(afterTitleToDecision)
                                        .Border(1)
                                        .BorderColor(lineLight)
                                        .Background(Colors.White)
                                        .Padding(6)
                                        .Height(signatureBoxHeight);

                                    if (hasSignature)
                                        box.Image(signatureBytes!).FitArea();
                                    else
                                        box.AlignCenter().Text("—").FontColor(textMuted);
                                });
                            });
                        });
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    // =========================
    // Styles (tables)
    // =========================
    private static IContainer CellHeader(IContainer c)
    {
        return c
            .Background(Colors.Grey.Lighten4)
            .Border(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3).PaddingHorizontal(6)
            .DefaultTextStyle(x => x.SemiBold().FontSize(9));
    }

    private static IContainer CellBody(IContainer c)
    {
        return c
            .Border(1).BorderColor(Colors.Grey.Lighten3)
            .PaddingVertical(3).PaddingHorizontal(6)
            .DefaultTextStyle(x => x.FontSize(9));
    }

    private static string FormatQty(double v, CultureInfo culture)
    {
        if (Math.Abs(v) < 0.0000000001) return "0";
        return v.ToString("0.##", culture);
    }

    private static void AddTotalsRow4Cols(
        TableDescriptor t,
        string label,
        string qtyText,
        string unitPriceText,
        string totalText,
        bool isStrong)
    {
        var labelStyle = TextStyle.Default.FontFamily("Arial").FontSize(10);
        var valueStyle = TextStyle.Default.FontFamily("Arial").FontSize(10);

        if (isStrong)
        {
            labelStyle = labelStyle.SemiBold();
            valueStyle = valueStyle.SemiBold();
        }

        t.Cell().Element(CellBody).Text(label).Style(labelStyle);
        t.Cell().Element(CellBody).AlignRight().Text(qtyText ?? "");
        t.Cell().Element(CellBody).AlignRight().Text(unitPriceText ?? "");
        t.Cell().Element(CellBody).AlignRight().Text(totalText ?? "").Style(valueStyle);
    }

    // =========================
    // Helpers communs
    // =========================
    private static List<byte[]> SlicePngVerticallyIntoPages(byte[] pngBytes, PageSize pageSize)
    {
        double pageRatio = pageSize.Height / pageSize.Width;

        var bitmap = LoadPngToBitmapSource(pngBytes);
        if (bitmap == null)
            return new List<byte[]> { pngBytes };

        int imgW = bitmap.PixelWidth;
        int imgH = bitmap.PixelHeight;

        if (imgW <= 0 || imgH <= 0)
            return new List<byte[]> { pngBytes };

        int sliceHeightPx = (int)Math.Floor(imgW * pageRatio);
        sliceHeightPx = Math.Max(200, sliceHeightPx);

        if (imgH <= sliceHeightPx)
            return new List<byte[]> { pngBytes };

        var result = new List<byte[]>();
        int y = 0;

        while (y < imgH)
        {
            int h = Math.Min(sliceHeightPx, imgH - y);
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(0, y, imgW, h));
            result.Add(EncodeBitmapSourceToPng(cropped));
            y += h;
        }

        return result;
    }

    private static BitmapSource? LoadPngToBitmapSource(byte[] pngBytes)
    {
        try
        {
            using var ms = new MemoryStream(pngBytes);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] EncodeBitmapSourceToPng(BitmapSource bmp)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string? GetAddressLine1(string? raw)
    {
        var lines = SplitAddressLines(raw);
        return lines.Length >= 1 ? lines[0] : "";
    }

    private static string? GetAddressLine2(string? raw)
    {
        var lines = SplitAddressLines(raw);
        return lines.Length >= 2 ? lines[1] : "";
    }

    private static string[] SplitAddressLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var s = raw.Replace("\r\n", "\n").Replace("\r", "\n");

        var parts = s
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (parts.Length >= 2)
            return new[] { parts[0], parts[1] };

        var one = parts.Length == 1 ? parts[0] : s.Trim();
        if (string.IsNullOrWhiteSpace(one))
            return Array.Empty<string>();

        var idx = one.LastIndexOf(',');
        if (idx > 0 && idx < one.Length - 1)
        {
            var a = one.Substring(0, idx).Trim();
            var b = one.Substring(idx + 1).Trim();
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                return new[] { a, b };
        }

        return new[] { one };
    }

    private static string FormatDateShort(DateTime date)
    {
        return date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("fr-CH"));
    }

    // =========================
    // Helpers BDR (Demande / Validation)
    // =========================
    private static void AddInfoCell(
        TableDescriptor t,
        string label,
        string? value,
        string borderColor,
        string labelColor,
        bool rightDivider = false)
    {
        var cell = t.Cell()
            .BorderBottom(1)
            .BorderColor(borderColor)
            .PaddingVertical(3)
            .PaddingRight(8);

        if (rightDivider)
        {
            cell = cell
                .BorderRight(1)
                .BorderColor(borderColor)
                .PaddingRight(12);
        }

        cell.Column(c =>
        {
            c.Item().Text(label).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
            c.Item().PaddingTop(1).Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
        });
    }

    private static void AddInfoCell1Col(
        TableDescriptor t,
        string label,
        string? value,
        string borderColor)
    {
        var cell = t.Cell()
            .BorderBottom(1)
            .BorderColor(borderColor)
            .PaddingVertical(3)
            .PaddingRight(8);

        cell.Column(c =>
        {
            c.Item().Text(label).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
            c.Item().PaddingTop(1).Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
        });
    }
}