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
    public static void GenerateAccountingPdfFromBitmapPng(string filePath, byte[] pngBytes, List<(double Top, double Bottom)>? avoidCutRanges = null)
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
        var projectManagerRefLine = GetProjectManagerRefLine(project);
        var projectManagerContactLine = GetProjectManagerContactLine(project);

        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;
        var lineLight = Colors.Grey.Lighten2;
        var separatorBlack = Colors.Grey.Darken4;

        var slices = SlicePngVerticallyIntoPages(pngBytes, pageSize, avoidCutRanges);

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
                                        .CornerRadius(10)
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
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Dossier / chantier" : projectName)
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

                                    if (!string.IsNullOrWhiteSpace(projectManagerRefLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(8)
                                            .Text(projectManagerRefLine)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(projectManagerContactLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(projectManagerContactLine)
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
    // ✅ "participantsPng" (18.08.2026) : capture "Participants à la séance" (Mode PV),
    // insérée EN PREMIER dans la même colonne que "sectionPngs" -- le vrai bug ("liste
    // tronquée/coupée entre pages") était dans la CAPTURE WPF elle-même (PvPanel.ActualHeight
    // comprimée par le Grid racine ClipToBounds, voir PlanningPage.CaptureElementToPngBytes),
    // pas dans la mise en page QuestPDF. Une fois la capture corrigée, le comportement
    // d'origine (empiler les sections avec ShowEntire, QuestPDF les répartit sur autant de
    // pages que nécessaire, sans jamais couper une section en deux) redonne exactement ce que
    // Joe demande : "un maximum de sections doivent apparaître sur la page 1 sans qu'une
    // seule soit coupée".
    public static void GeneratePlanningPdfFromSections(string filePath, List<byte[]> sectionPngs, byte[]? participantsPng = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        sectionPngs ??= new List<byte[]>();
        sectionPngs = sectionPngs.Where(x => x != null && x.Length > 0).ToList();

        if (sectionPngs.Count == 0 && (participantsPng == null || participantsPng.Length == 0))
            throw new ArgumentException("Aucune section à exporter (PNG vide).", nameof(sectionPngs));

        const float margin = 26f;
        var pageSize = PageSizes.A4;

        var architectName = Db.GetArchitectName();
        var architectAddress = Db.GetArchitectAddress();
        var architectRef = Db.GetArchitectRef();
        var architectRef2 = Db.GetArchitectRef2();
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
        var projectWebsite = (project?.Website ?? "").Trim();
        var projectManagerRefLine = GetProjectManagerRefLine(project);
        var projectManagerContactLine = GetProjectManagerContactLine(project);

        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;
        var lineLight = Colors.Grey.Lighten2;
        var separatorBlack = Colors.Grey.Darken4;

        // ✅ Participants en premier, puis les sections "normales", toutes empilées dans la
        // même colonne (voir plus bas) : QuestPDF les répartit sur autant de pages que
        // nécessaire, en ne coupant jamais une section en deux (ShowEntire), ce qui pousse le
        // maximum de sections complètes sur la page 1 avant de continuer sur la page 2.
        var allSections = new List<byte[]>();
        if (participantsPng != null && participantsPng.Length > 0)
            allSections.Add(participantsPng);
        allSections.AddRange(sectionPngs);

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
                                        .CornerRadius(10)
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

                                    if (!string.IsNullOrWhiteSpace(architectRef))
                                    {
                                        c.Item().Text(architectRef)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(architectRef2))
                                    {
                                        c.Item().Text(architectRef2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });

                            // Centre : titre + date (18.08.2026, demande de Joe : "le titre du
                            // Pdf, placé en dessus de la date au milieu, sera 'PV de la séance
                            // du:'").
                            row.RelativeItem(0.9f).AlignCenter().Column(center =>
                            {
                                center.Item()
                                    .AlignCenter()
                                    .Text("PV de la séance du:")
                                    .SemiBold()
                                    .FontSize(10)
                                    .FontColor(textMain);

                                center.Item()
                                    .PaddingTop(2)
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
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Dossier / chantier" : projectName)
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

                                    if (!string.IsNullOrWhiteSpace(projectManagerRefLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(8)
                                            .Text(projectManagerRefLine)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(projectManagerContactLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(projectManagerContactLine)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(projectWebsite))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(projectWebsite)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(separatorBlack);
                    });
                });

                // ✅ Sections empilées, chacune ShowEntire (jamais coupée en deux) : le titre
                // est déjà inclus dans chaque capture elle-même (ex: "1) Tâches", "Participants
                // à la séance"), pas besoin de le redessiner ici. QuestPDF pousse le maximum de
                // sections complètes sur la page 1, puis continue sur la page 2, etc.
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    foreach (var png in allSections)
                        col.Item().ShowEntire().Image(png).FitArea();
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
        var architectRef = Db.GetArchitectRef();
        var architectRef2 = Db.GetArchitectRef2();
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
        var projectManagerRefLine = GetProjectManagerRefLine(project);
        var projectManagerContactLine = GetProjectManagerContactLine(project);

        var tag = wo.ProjectTag;
        var bdrShort = wo.BdrNumberDisplay;

        // ---- Couleurs
        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;

        var lineLight = Colors.Grey.Lighten2;
        var lineLighter = Colors.Grey.Lighten3;
        var headerBg = Colors.Grey.Lighten4;

        var separatorBlack = Colors.Grey.Darken4;

        // ---- Couleurs des cartes (alignées avec le look couleur du serveur)
        var demandeBg = "#FFF7ED";
        var demandeBorder = "#FDBA74";

        var devisBg = "#EAF2FF";
        var devisBorder = "#1D4ED8";

        var validationBg = "#E9FBEA";
        var validationBorder = "#15803D";

        // ---- Devis: calculs (rabais après HT brut, avant TVA/TTC)
        lines = lines.Where(l => l != null).ToList();

        double materialTotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        double laborTotal = Math.Round(wo.LaborHours * wo.LaborRate, 2);
        double travelTotal = Math.Round(wo.TravelQty * wo.TravelRate, 2);

        double forfaitTotal = Math.Round(wo.ForfaitQty * wo.ForfaitUnitPrice, 2);

        double tvaRate = wo.TvaRate;
        if (double.IsNaN(tvaRate) || double.IsInfinity(tvaRate)) tvaRate = 0;

        // ✅ BUG RÉEL (20.07.2026) : le champ "Forfait : Montant TTC" (WorkOrder.ForfaitTtc)
        // n'était pas pris en compte ici -> le pdf d'un devis fait en Forfait TTC affichait
        // 0.00 partout. Contrairement au forfait ci-dessus (HT -> TVA -> TTC), celui-ci part du
        // TTC saisi et recalcule HT/TVA à rebours, comme WorkOrderWindow.RecomputeTotals.
        double forfaitTtc = Math.Round(wo.ForfaitTtc, 2);
        bool hasForfaitTtc = Math.Abs(forfaitTtc) > 0.0000000001;

        double discountRate = 0, discountRate2 = 0, discountAmount, discountAmount2, htNet, tvaAmount, ttcTotal;

        if (hasForfaitTtc)
        {
            ttcTotal = forfaitTtc;
            htNet = Math.Round(ttcTotal / (1.0 + (tvaRate / 100.0)), 2);
            tvaAmount = Math.Round(ttcTotal - htNet, 2);
            discountAmount = 0;
            discountAmount2 = 0;
        }
        else
        {
            double htBrut = Math.Round(materialTotal + laborTotal + travelTotal + forfaitTotal, 2);

            discountRate = wo.DiscountRate;
            if (double.IsNaN(discountRate) || double.IsInfinity(discountRate)) discountRate = 0;
            discountRate = Math.Max(0, discountRate);

            // ✅ Réplique serveur : montant du rabais toujours positif (magnitude), le "-" est
            // ajouté littéralement à l'affichage — pas déduit d'une soustraction HT net/brut.
            discountAmount = Math.Round(htBrut * (discountRate / 100.0), 2);
            var htAfterDiscount1 = Math.Round(htBrut - discountAmount, 2);

            // ✅ Rabais 2 (17.08.2026, demande de Joe) : appliqué consécutivement, sur le montant
            // déjà réduit par Rabais 1 (même logique que WorkOrderWindow.RecomputeTotals).
            discountRate2 = wo.DiscountRate2;
            if (double.IsNaN(discountRate2) || double.IsInfinity(discountRate2)) discountRate2 = 0;
            discountRate2 = Math.Max(0, discountRate2);

            discountAmount2 = Math.Round(htAfterDiscount1 * (discountRate2 / 100.0), 2);
            htNet = Math.Round(htAfterDiscount1 - discountAmount2, 2);

            tvaAmount = Math.Round(htNet * (tvaRate / 100.0), 2);
            ttcTotal = Math.Round(htNet + tvaAmount, 2);
        }

        // ---- Validation (signature)
        byte[]? signatureBytes = null;
        try { signatureBytes = wo.SignaturePng; } catch { signatureBytes = null; }
        bool hasSignature = signatureBytes != null && signatureBytes.Length > 0;

        // ✅ Recadrage sur le tracé réel (identique au cropToContent() côté web) : une
        // signature saisie sur le grand InkCanvas du client desktop est capturée avec
        // tout son fond blanc autour ; sans recadrage, le trait reste minuscule au milieu
        // de la box PDF quelle que soit sa hauteur.
        if (hasSignature) signatureBytes = CropSignatureToContent(signatureBytes!);

        var decision = DecisionLabelFr((wo.ValidationDecision ?? "").Trim());
        var sigName = (wo.SignatureName ?? "").Trim();
        var sigDate = wo.SignatureDate.HasValue ? FormatDateShort(wo.SignatureDate.Value) : "";

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(10);
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
                                        .CornerRadius(10)
                                        .Image(logoBytes)
                                        .FitArea();

                                    left.ConstantItem(10);
                                }

                                left.RelativeItem().Column(c =>
                                {
                                    // ✅ Hauteur fixe + alignement bas : la police du titre central (14pt)
                                    // est plus grande que celle-ci (12pt) — sans ça, les premières lignes
                                    // des 3 blocs de l'entête ne tombent pas sur la même ligne de base.
                                    c.Item().Height(18).AlignBottom()
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

                                    if (!string.IsNullOrWhiteSpace(architectRef))
                                    {
                                        c.Item().Text(architectRef)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(architectRef2))
                                    {
                                        c.Item().Text(architectRef2)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });

                            row.RelativeItem(0.9f).Column(center =>
                            {
                                center.Item().Height(18).AlignCenter().AlignBottom().Text("Bon d'intervention")
                                    .SemiBold()
                                    .FontSize(14);

                                // ✅ Noir partout + espacement aligné sur le BI WPF (demande de
                                // Joe, 11.08.2026) : "N°" [espace] numéro, puis "- {tag}" collé
                                // directement au numéro (pas de r.Spacing uniforme ici, un seul
                                // ConstantItem espaceur avant le numéro, comme le Margin="0,0,8,0"
                                // du TextBlock "N°" côté WPF).
                                center.Item().AlignCenter().PaddingTop(2).Row(r =>
                                {
                                    r.AutoItem()
                                        .AlignBottom()
                                        .Text("N°")
                                        .SemiBold()
                                        .FontSize(11)
                                        .FontColor(Colors.Black);

                                    r.ConstantItem(6);

                                    r.AutoItem()
                                        .AlignBottom()
                                        .Text(bdrShort)
                                        .SemiBold()
                                        .FontSize(16)
                                        .FontColor(Colors.Black);

                                    if (!string.IsNullOrWhiteSpace(tag))
                                    {
                                        // ✅ Espace supplémentaire avant le tiret (demande de Joe,
                                        // 11.08.2026).
                                        r.ConstantItem(3);

                                        r.AutoItem()
                                            .AlignBottom()
                                            .Text($"- {tag}")
                                            .SemiBold()
                                            .FontSize(11)
                                            .FontColor(Colors.Black);
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
                                        .Height(18)
                                        .AlignLeft()
                                        .AlignBottom()
                                        .Text(string.IsNullOrWhiteSpace(projectName) ? "Dossier / chantier" : projectName)
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

                                    if (!string.IsNullOrWhiteSpace(projectManagerRefLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .PaddingTop(8)
                                            .Text(projectManagerRefLine)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }

                                    if (!string.IsNullOrWhiteSpace(projectManagerContactLine))
                                    {
                                        c.Item()
                                            .AlignLeft()
                                            .Text(projectManagerContactLine)
                                            .FontSize(9)
                                            .FontColor(textMuted);
                                    }
                                });
                            });
                        });

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
                page.Content().PaddingTop(4).Column(col =>
                {
                    col.Spacing(2);

                    // -------------------------
                    // DEMANDE
                    // -------------------------
                    col.Item().ShowEntire()
                        .CornerRadius(14)
                        .Background(demandeBg)
                        .Border(2).BorderColor(demandeBorder)
                        .Padding(4)
                        .Column(section =>
                    {
                        section.Item().Text("Demande")
                            .Bold()
                            .FontSize(16);

                        section.Item().PaddingTop(5).Row(row =>
                        {
                            row.RelativeItem(1f).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                });

                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Concerne", wo.Reserve));
                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Demandé par", wo.RequestedBy));

                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Bâtiment", wo.Place));
                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Étage", wo.Etage));

                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Entreprise", wo.PerformedBy));

                                var deadline = wo.DeadlineDate == default ? "" : FormatDateShort(wo.DeadlineDate);
                                t.Cell().Padding(3).Element(e => FieldBoxText(e, "Délai", deadline));
                            });

                            row.ConstantItem(15);

                            row.RelativeItem(1f).Element(e => FieldBox(e, "Descriptif", box =>
                            {
                                var description = (wo.Description ?? "").Trim();

                                if (string.IsNullOrWhiteSpace(description))
                                {
                                    box.Text("").FontSize(10);
                                    return;
                                }

                                var descriptionLines = description
                                    .Replace("\r\n", "\n")
                                    .Replace("\r", "\n")
                                    .Split('\n', StringSplitOptions.None)
                                    .Select(x => (x ?? "").TrimEnd())
                                    .ToList();

                                box.Column(list =>
                                {
                                    foreach (var ln in descriptionLines)
                                        list.Item().Text(string.IsNullOrWhiteSpace(ln) ? " " : ln).FontSize(10);
                                });
                            }, minHeight: 55));
                        });
                    });

                    // -------------------------
                    // DEVIS (Nom centré)
                    // -------------------------
                    col.Item().ShowEntire()
                        .CornerRadius(14)
                        .Background(devisBg)
                        .Border(2).BorderColor(devisBorder)
                        .Padding(4)
                        .Column(section =>
                    {
                        var d = wo.QuoteDate == default ? "" : FormatDateLong(wo.QuoteDate);
                        var quoteName = string.IsNullOrWhiteSpace(wo.QuoteName) ? "—" : wo.QuoteName;
                        var dateText = string.IsNullOrWhiteSpace(d) ? "—" : d;

                        // ✅ Nom aligné à gauche (au lieu de centré) + marge à droite pour la date
                        // (23.07.2026, demande de Joe).
                        // ✅ AlignMiddle (23.07.2026, demande de Joe) : Nom/Date centrés
                        // verticalement sur l'axe du titre "Devis" (16pt, plus haut que leur 10pt).
                        section.Item().Row(r =>
                        {
                            r.RelativeItem(0.45f).AlignLeft().AlignMiddle().Text("Devis").Bold().FontSize(16);
                            r.RelativeItem(0.90f).AlignLeft().AlignMiddle().Text($"Nom : {quoteName}").FontSize(10).FontColor(textMain);
                            r.RelativeItem(0.65f).AlignRight().AlignMiddle().PaddingRight(8).Text($"Devis créé le : {dateText}").FontSize(10).FontColor(textMain);
                        });

                        // ✅ Position "Devis PDF" (05.08.2026, réplique de la structure WPF/Blazor
                        // QuoteMode) : le tableau Libellé/Matériel est remplacé par le rectangle
                        // bleu "Devis PDF" en pleine largeur (au lieu de rester coincé dans la
                        // colonne "Note" de 130pt), et les lignes Main d'œuvre/Déplacements/Rabais/
                        // Total Matériel disparaissent des totaux (toujours à 0 dans ce mode,
                        // l'exclusivité DS/DF étant maintenant appliquée à l'enregistrement, voir
                        // WorkOrderWindow.SaveWorkOrder).
                        bool isPdfMode = hasForfaitTtc;

                        if (!isPdfMode)
                        {
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

                            // ✅ Bordure basse fermant le tableau (23.07.2026) : les lignes n'ont plus
                            // que leur bordure haute (voir CellBody/CellBodyWhite), donc rien ne fermait
                            // le bas de la dernière ligne, quel que soit son nombre.
                            section.Item().PaddingTop(6).BorderBottom(0.75f).BorderColor("#000000").Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(1);
                                    c.ConstantColumn(55);
                                    c.ConstantColumn(72);
                                    c.ConstantColumn(82);
                                });

                                t.Header(h =>
                                {
                                    // ✅ Réplique serveur : "Qt" centré (.col-qt.c), "Prix/pc"/"Total" alignés à droite (.r).
                                    h.Cell().Element(CellHeader).Text("Libellé / Matériel");
                                    h.Cell().Element(CellHeader).AlignCenter().Text("Qt");
                                    h.Cell().Element(CellHeader).AlignRight().Text("Prix/pc");
                                    h.Cell().Element(CellHeaderLast).AlignRight().Text("Total");
                                });

                                foreach (var l in printableLines)
                                {
                                    var label = (l.Label ?? "").Trim();
                                    if (string.IsNullOrWhiteSpace(label)) label = "—";

                                    t.Cell().Element(CellBodyWhite).Text(label);
                                    t.Cell().Element(CellBodyWhite).AlignCenter().Text(FormatQty(l.Qty));
                                    t.Cell().Element(CellBodyWhite).AlignRight().Text(FmtOptInv(l.UnitPrice));
                                    t.Cell().Element(CellBody).AlignRight().Text(FmtInv(l.LineTotal));
                                }
                            });
                        }
                        else
                        {
                            // ✅ Rectangle "Devis PDF" repassé en pleine largeur (demande de Joe,
                            // 11.08.2026, "partout ailleurs il en est ainsi") : le rétrécissement du
                            // 05.08.2026 (aligné sur la colonne des totaux) est annulé, comme côté
                            // WPF (QuotePdfSection) et serveur (.forfait-pdf-box.full).
                            var quoteNumber = (wo.ForfaitQuoteNumber ?? "").Trim();

                            section.Item().PaddingTop(6).Background("#4C6D8E").CornerRadius(4).Padding(8).Column(box =>
                            {
                                box.Item().Text("Devis PDF").Italic().SemiBold().FontSize(9).FontColor(Colors.White);

                                if (!string.IsNullOrWhiteSpace(quoteNumber))
                                {
                                    box.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text("N° du devis").Italic().SemiBold().FontSize(8).FontColor(Colors.White);
                                        r.RelativeItem().AlignRight().Text(quoteNumber).SemiBold().FontSize(10).FontColor(Colors.White);
                                    });
                                }

                                box.Item().PaddingTop(6).Row(r =>
                                {
                                    r.RelativeItem().Text("MONTANT TTC du pdf").Italic().SemiBold().FontSize(8).FontColor(Colors.White);
                                    r.RelativeItem().AlignRight().Text(FmtInv(forfaitTtc)).SemiBold().FontSize(10).FontColor(Colors.White);
                                });
                            });
                        }

                        // ✅ Ratio 1 / 1.7 (05.08.2026, demande de Joe : "65% partout") : la colonne
                        // Totaux occupait ~75% de la largeur ici (ConstantItem(130) fixe pour Note,
                        // puis RelativeItem() prenant tout le reste, sur une page pdf plus large que
                        // la carte WPF) au lieu des ~65% du BI (Grid "1*, 7, 1.7*", voir
                        // WorkOrderWindow.xaml). Mêmes proportions relatives ici pour un rendu
                        // identique quelle que soit la largeur de page.
                        // ✅ Ratio Note élargi 1/1.7 -> 1.3/1.7 (demande de Joe, 11.08.2026, "ne
                        // supporte pas la capacité des 40 caractères") : la case Note était trop
                        // étroite pour la nouvelle limite de 40 caractères/ligne.
                        // ✅ 1.3/1.7 -> 0.975/2.025 (demande de Joe, 18.08.2026) : limite ramenée à 30
                        // caractères/ligne, colonne réduite en proportion (× 0.75) au profit du tableau
                        // de totaux -- même échelle appliquée à WorkOrderWindow.xaml (Grid Note/Totaux)
                        // et à Bdr.razor (.devis-bottom).
                        section.Item().PaddingTop(8).Row(row =>
                        {
                            row.RelativeItem(0.975f).Column(left =>
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
                                    .Border(0.75f).BorderColor(lineLight)
                                    .Background(Colors.White)
                                    .Padding(6)
                                    .Column(list =>
                                    {
                                        foreach (var ln in noteLines)
                                            list.Item().Text(string.IsNullOrWhiteSpace(ln) ? " " : ln).FontSize(9);
                                    });
                            });

                            row.ConstantItem(6);

                            row.RelativeItem(2.025f).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(1);
                                    c.ConstantColumn(55);
                                    c.ConstantColumn(72);
                                    c.ConstantColumn(82);
                                });

                                if (!isPdfMode)
                                {
                                    // ✅ Bordure haute ET basse (23.07.2026, demande de Joe) : avant, un
                                    // Cell séparé (ColumnSpan+PaddingVertical) simulait la ligne du bas
                                    // avec un espace ; remplacé par une vraie bordure directement sur la
                                    // ligne, comme Total HT/Total TTC.
                                    AddTotalsRow4Cols(t, "Total Matériel", "", "", FmtInv(materialTotal),
                                        isStrong: true, noInnerDividers: true, topBorderThickness: 0.75f, bottomBorderThickness: 0.75f, greyBackground: true);

                                    AddTotalsRow4Cols(t, "Main d’œuvre",
                                        qtyText: FormatQty(wo.LaborHours),
                                        unitPriceText: FmtOptInv(wo.LaborRate),
                                        totalText: FmtInv(laborTotal),
                                        isStrong: false, blueBackground: true, labelAlignLeft: true);

                                    AddTotalsRow4Cols(t, "Déplacements",
                                        qtyText: FormatQty(wo.TravelQty),
                                        unitPriceText: FmtOptInv(wo.TravelRate),
                                        totalText: FmtInv(travelTotal),
                                        isStrong: false, blueBackground: true, labelAlignLeft: true);

                                    // ✅ "Sous-total 1" (18.08.2026, demande de Joe) : affiché uniquement
                                    // quand Rabais 1 a un taux saisi, même principe que WorkOrderWindow.
                                    if (discountRate > 0.0000000001)
                                    {
                                        var htBrutDisplay = Math.Round(materialTotal + laborTotal + travelTotal + forfaitTotal, 2);
                                        AddTotalsRow4Cols(t, "Sous-total 1", "", "", FmtInv(htBrutDisplay), isStrong: true, greyBackground: true, noInnerDividers: true);
                                    }

                                    // ✅ Réplique serveur : le taux va dans la colonne Qt, le libellé reste statique,
                                    // et le total est toujours préfixé d'un "-" littéral (même à 0 : "-0.00").
                                    // ✅ Nom du rabais en labelValue (18.08.2026), pas concaténé dans le libellé --
                                    // voir AddTotalsRow4Cols pour l'alignement partagé avec Rabais 2/TVA.
                                    var discount1Name = string.IsNullOrWhiteSpace(wo.DiscountName) ? null : wo.DiscountName.Trim();
                                    AddTotalsRow4Cols(t, "Rabais 1 (%)", FormatQty(discountRate), "", $"-{FmtInv(discountAmount)}", isStrong: false, blueBackground: true, bluePrixDecorative: true, labelAlignLeft: true, noPrixRightBorder: true, labelValue: discount1Name);

                                    // ✅ "Sous-total 2" : affiché uniquement quand Rabais 2 a un taux saisi.
                                    if (discountRate2 > 0.0000000001)
                                    {
                                        var afterDiscount1Display = Math.Round((materialTotal + laborTotal + travelTotal + forfaitTotal) * (1.0 - (discountRate / 100.0)), 2);
                                        AddTotalsRow4Cols(t, "Sous-total 2", "", "", FmtInv(afterDiscount1Display), isStrong: true, greyBackground: true, noInnerDividers: true);
                                    }

                                    // ✅ Rabais 2 (17.08.2026, demande de Joe) : même structure que Rabais 1.
                                    var discount2Name = string.IsNullOrWhiteSpace(wo.DiscountName2) ? null : wo.DiscountName2.Trim();
                                    AddTotalsRow4Cols(t, "Rabais 2 (%)", FormatQty(discountRate2), "", $"-{FmtInv(discountAmount2)}", isStrong: false, blueBackground: true, bluePrixDecorative: true, labelAlignLeft: true, noPrixRightBorder: true, labelValue: discount2Name);

                                    // ✅ Pas de bordure haute ici (demande de Joe, 18.08.2026, bordures
                                    // uniformes de Total Matériel à Total TTC) : la bordure basse de Rabais 2
                                    // juste au-dessus suffit déjà, une bordure haute ici la doublerait.
                                    AddTotalsRow4Cols(t, "Total HT", "", "", FmtInv(htNet), isStrong: true, greyBackground: true, noInnerDividers: true, bottomBorderThickness: 0.75f, matchGrandTotalHeight: true);
                                }
                                else
                                {
                                    // ✅ Total HT devient la première ligne du tableau en position
                                    // "Devis PDF" (05.08.2026) : bordure haute explicite (comme "Total
                                    // Matériel" en position standard) puisque plus rien ne la précède.
                                    AddTotalsRow4Cols(t, "Total HT", "", "", FmtInv(htNet), isStrong: true, greyBackground: true, noInnerDividers: true, topBorderThickness: 0.75f, bottomBorderThickness: 0.75f, matchGrandTotalHeight: true);
                                }

                                // ✅ Bordures uniformes de Total Matériel à Total TTC (demande de Joe,
                                // 18.08.2026) : même épaisseur (0.75) partout, plus de bordure à 0.9 réservée
                                // à HT/TTC ni de bordure à 0 sur TVA pour éviter un doublon -- Total TTC n'a
                                // plus de bordure haute forcée (voir AddTotalsRow4Cols), s'appuie sur la
                                // bordure basse de TVA comme les autres jonctions du bloc.
                                // ✅ Taux affiché dans le libellé, colonne Qt laissée vide (demande de Joe,
                                // 18.08.2026) : réplique le déplacement du champ taux fait côté WPF
                                // (WorkOrderWindow.xaml, sorti de la colonne Qt vers le libellé).
                                // ✅ Valeur en labelValue, largeur de préfixe partagée avec Rabais 1/2 (demande
                                // de Joe, 18.08.2026, "8,10 aligné à Prorata") + fullBlueBackground (plus de
                                // case Qt/Prix blanche, ce libellé n'a plus de contenu séparé dans ces
                                // colonnes) + noInnerDividers (plus de séparations internes, cohérent avec
                                // l'absence de contenu Qt/Prix).
                                var tvaValue = tvaRate.ToString("0.00", CultureInfo.InvariantCulture);
                                AddTotalsRow4Cols(t, "TVA (%)", "", "", FmtInv(tvaAmount), isStrong: false, fullBlueBackground: true, bottomBorderThickness: 0.75f, labelAlignLeft: true, noInnerDividers: true, labelValue: tvaValue);
                                AddTotalsRow4Cols(t, "Total TTC", "", "", FmtInv(ttcTotal), isStrong: true, isGrandTotal: true, bottomBorderThickness: 0.75f, noInnerDividers: true);
                            });
                        });

                        section.Item().PaddingTop(6).LineHorizontal(1).LineColor(separatorBlack);
                    });

                    // -------------------------
                    // VALIDATION (✅ box signature remontée ; libellé "Signature" aligné sur Nom/Date ; bas box aligné bas Date)
                    // -------------------------
                    col.Item().ShowEntire()
                        .CornerRadius(14)
                        .Background(validationBg)
                        .Border(2).BorderColor(validationBorder)
                        .Padding(4)
                        .Column(section =>
                    {
                        // Constantes d'alignement (cohérentes avec la mise en page des tables à gauche)
                        // ✅ Hauteurs recalculées pour les champs "boîte blanche" (FieldBox), plus hauts
                        // que l'ancien style à simple ligne de séparation.
                        const float titleHeight = 18f;         // hauteur visuelle du titre "Validation"
                        const float afterTitleToDecision = 6f; // PaddingTop(6)
                        const float decisionBlockHeight = 38f; // hauteur du champ Décision (boîte blanche)
                        const float betweenDecisionAndNameDate = 4f; // PaddingTop(4)
                        const float nameDateBlockHeight = 38f; // hauteur des champs Nom/Date (boîte blanche)
                        const float signatureBoxHeight = decisionBlockHeight + betweenDecisionAndNameDate + nameDateBlockHeight; // bas aligné bas Date

                        section.Item().Row(row =>
                        {
                            // Gauche
                            row.RelativeItem(1.05f).Column(left =>
                            {
                                left.Item().Text("Validation").SemiBold().FontSize(13);

                                left.Item().PaddingTop(afterTitleToDecision)
                                    .Element(e => FieldBoxText(e, "Décision", string.IsNullOrWhiteSpace(decision) ? "—" : decision));

                                left.Item().PaddingTop(betweenDecisionAndNameDate).Row(nd =>
                                {
                                    nd.RelativeItem()
                                        .Element(e => FieldBoxText(e, "Nom", string.IsNullOrWhiteSpace(sigName) ? "—" : sigName));

                                    nd.ConstantItem(8);

                                    nd.RelativeItem()
                                        .Element(e => FieldBoxText(e, "Date", string.IsNullOrWhiteSpace(sigDate) ? "—" : sigDate));
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
                                    // ✅ Libellé "Signature" remis sur la même ligne que Nom/Date
                                    // (23.07.2026, demande de Joe, annule le AlignTop du tour précédent).
                                    sig.ConstantItem(58)
                                        // ✅ -8pt puis +4pt (~2mm puis ~1mm) (23.07.2026, demande de Joe).
                                        .PaddingTop(afterTitleToDecision + decisionBlockHeight + betweenDecisionAndNameDate - 4)
                                        .Text("Signature")
                                        .SemiBold()
                                        .FontSize(9)
                                        .FontColor(Colors.Grey.Darken4);

                                    // ✅ PaddingTop retiré (23.07.2026, demande de Joe : la boîte n'avait
                                    // pas bougé quand le libellé est remonté) : la boîte démarre
                                    // maintenant au même niveau que le libellé "Signature".
                                    var box = sig.RelativeItem()
                                        .Border(0.75f)
                                        .BorderColor(lineLight)
                                        .Background(Colors.White)
                                        .Padding(6)
                                        .Height(signatureBoxHeight);

                                    if (hasSignature)
                                        // ✅ Centre l'image (déjà recadrée sur son contenu par
                                        // CropSignatureToContent) dans la box, horizontal + vertical.
                                        box.AlignCenter().AlignMiddle().Image(signatureBytes!).FitArea();
                                    else
                                        // ✅ AlignMiddle ajouté (23.07.2026, demande de Joe) : le "—"
                                        // restait collé en haut de la grande boîte, seul le centrage
                                        // horizontal était fait.
                                        box.AlignCenter().AlignMiddle().Text("—").FontColor(textMuted);
                                });
                            });
                        });
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    // =========================
    // ✅ PDF Descriptif de tâche (Planning, tableau des Tâches, 16.07.2026) : document simple
    // généré directement à partir du texte (pas une capture d'écran) — un petit en-tête avec
    // les infos de la ligne, suivi du texte du descriptif en entier, sur autant de pages que
    // nécessaire. `infoFields` est fourni par TaskDescriptionWindow à partir des mêmes
    // libellés/valeurs affichés dans sa fenêtre (mêmes colonnes visibles, mêmes libellés
    // dynamiques) — corrige le bug du 16.07.2026 où Urg./Effectué n'apparaissaient jamais
    // dans le PDF (champs jamais transmis à cette méthode).
    // =========================
    public static void GenerateTaskDescriptionPdf(
        string filePath,
        string taskRef,
        List<(string Label, string Value)> infoFields,
        string todo)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        var culture = new CultureInfo("fr-CH");
        var textMain = Colors.Grey.Darken4;
        var textMuted = Colors.Grey.Darken1;
        var lineLight = Colors.Grey.Lighten2;

        var project = Db.GetCurrentProject();
        var projectName = project?.Name ?? "";

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor(textMain));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Tâche N° {taskRef}").FontSize(16).SemiBold().FontColor(textMain);

                    if (!string.IsNullOrWhiteSpace(projectName))
                        col.Item().PaddingTop(2).Text(projectName).FontSize(10).FontColor(textMuted);

                    // ✅ Date seule, sans l'heure (demande de Joe, 16.07.2026).
                    col.Item().PaddingTop(2).Text(DateTime.Now.ToString("dd.MM.yyyy", culture)).FontSize(8).FontColor(textMuted);

                    // ✅ Par rangées de 4 max (Urg./Effectué/Concerne peuvent porter le total
                    // à 5-7 champs selon les colonnes visibles dans la grille).
                    foreach (var chunk in (infoFields ?? new List<(string, string)>()).Chunk(4))
                    {
                        col.Item().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                for (int i = 0; i < chunk.Length; i++)
                                    c.RelativeColumn();
                            });

                            foreach (var field in chunk)
                                AddTaskInfoCell(table, field.Label, field.Value, textMuted, textMain);
                        });
                    }

                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor(lineLight);
                });

                page.Content().PaddingTop(14).Text(string.IsNullOrWhiteSpace(todo) ? "" : todo)
                    .FontSize(11)
                    .LineHeight(1.4f);

                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void AddTaskInfoCell(TableDescriptor table, string label, string? value, string labelColor, string valueColor)
    {
        table.Cell().Element(c => c
                .Background("#F9FAFB")
                .Border(1).BorderColor("#E5E7EB")
                .Padding(6))
            .Column(cc =>
            {
                cc.Item().Text(label).FontSize(8).FontColor(labelColor);
                cc.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(10).SemiBold().FontColor(valueColor);
            });
    }

    // =========================
    // Styles (tables)
    // =========================
    // ✅ Bordure Top+Right au lieu de Border() 4 côtés (23.07.2026, demande de Joe : lignes
    // plus épaisses dans Libellé que dans les autres lignes) : Border() dessine les 4
    // côtés de CHAQUE cellule -> les bords partagés entre cellules adjacentes (droite de
    // l'une + gauche de la suivante, bas de l'une + haut de la suivante) se cumulaient,
    // doublant l'épaisseur visible. Un seul côté par frontière partagée, comme dans
    // AddTotalsRow4Cols. CellHeaderLast/CellBodyLast (colonne Total, dernière colonne) :
    // pas de BorderRight (bord extérieur, pas une séparation entre colonnes). Le bas du
    // tableau (dernière ligne) est fermé par la bordure du DataGrid/du Border englobant
    // le Table, pas par les cellules elles-mêmes.
    private static IContainer CellHeader(IContainer c)
    {
        // ✅ Réplique serveur (22.07.2026) : fond gris #F3F4F6 + texte sombre #111827
        // (au lieu du bleu #DBEAFE/#1E3A8A jamais mis à jour depuis les changements Blazor).
        return c
            .Background("#F3F4F6")
            .BorderTop(0.75f).BorderRight(0.75f).BorderColor("#000000")
            .PaddingVertical(3.2f).PaddingHorizontal(7)
            .DefaultTextStyle(x => x.SemiBold().FontSize(9).FontColor("#111827"));
    }

    private static IContainer CellHeaderLast(IContainer c)
    {
        return c
            .Background("#F3F4F6")
            .BorderTop(0.75f).BorderColor("#000000")
            .PaddingVertical(3.2f).PaddingHorizontal(7)
            .DefaultTextStyle(x => x.SemiBold().FontSize(9).FontColor("#111827"));
    }

    // ✅ Bleu #EAF2FF réservé à la colonne Total (23.07.2026, demande de Joe : mêmes
    // couleurs de champs qu'en WPF) — Libellé/Qt/Prix utilisent CellBodyWhite.
    private static IContainer CellBody(IContainer c)
    {
        return c
            .Background("#EAF2FF")
            .BorderTop(0.75f).BorderColor("#000000")
            .PaddingVertical(3.2f).PaddingHorizontal(7)
            // ✅ 9 -> 10 (demande de Joe, 18.08.2026) : même taille que les lignes de totaux
            // (Main d'œuvre, Déplacements, etc., voir AddTotalsRow4Cols).
            .DefaultTextStyle(x => x.FontSize(10));
    }

    private static IContainer CellBodyWhite(IContainer c)
    {
        return c
            .Background(Colors.White)
            .BorderTop(0.75f).BorderRight(0.75f).BorderColor("#000000")
            .PaddingVertical(3.2f).PaddingHorizontal(7)
            // ✅ 9 -> 10 (demande de Joe, 18.08.2026) : même taille que les lignes de totaux.
            .DefaultTextStyle(x => x.FontSize(10));
    }

    // ✅ Réplique exacte de Fmt/FmtOpt/FmtQty côté serveur (Bdr.razor) : culture invariante
    // (décimales avec point, pas virgule), quantité/prix vides si 0 — pas de "0" affiché.
    private static string FmtInv(double v)
        => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FmtOptInv(double v)
    {
        if (Math.Abs(v) < 0.0000000001) return "";
        return v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatQty(double v)
    {
        if (Math.Abs(v) < 0.0000000001) return "";
        var rounded = Math.Round(v, 2);
        return Math.Abs(rounded % 1) < 0.0000000001
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ✅ Grille des totaux : contrôle fin des bordures (réplique .tot-row du serveur) —
    // pas de bordure latérale gauche/droite (bord extérieur), séparateurs internes
    // (BorderLeft) désactivables pour "Total Matériel", épaisseur du bas paramétrable
    // pour un séparateur plus épais (ex. avant "Total HT").
    private static void AddTotalsRow4Cols(
        TableDescriptor t,
        string label,
        string qtyText,
        string unitPriceText,
        string totalText,
        bool isStrong,
        bool isGrandTotal = false,
        bool isForfait = false,
        bool noInnerDividers = false,
        float bottomBorderThickness = 0.75f,
        bool greyBackground = false,
        float topBorderThickness = 0f,
        bool blueBackground = false,
        bool bluePrixDecorative = false,
        bool matchGrandTotalHeight = false,
        bool labelAlignLeft = false,
        bool noPrixRightBorder = false,
        bool fullBlueBackground = false,
        string? labelValue = null)
    {
        var fontSize = isGrandTotal ? 12 : 10;

        var labelStyle = TextStyle.Default.FontFamily("Arial").FontSize(fontSize);
        var valueStyle = TextStyle.Default.FontFamily("Arial").FontSize(fontSize);

        if (isStrong || isGrandTotal)
        {
            labelStyle = labelStyle.SemiBold();
            valueStyle = valueStyle.SemiBold();
        }

        if (isForfait)
        {
            labelStyle = labelStyle.Italic().FontColor("#4169E1");
        }

        // ✅ col : 0=libellé, 1=Qt, 2=Prix/pc, 3=Total — nécessaire (au lieu d'un simple
        // isFirst) pour appliquer le bleu #EAF2FF (23.07.2026, demande de Joe : mêmes
        // couleurs de champs qu'en WPF) uniquement sur libellé+Total (Main d'œuvre/
        // Déplacements/Rabais/TVA) et, en plus, sur Prix/pc pour Rabais/TVA (case
        // décorative bleue, comme le WPF).
        IContainer Cell(int col)
        {
            IContainer c = t.Cell();

            // ✅ Réplique serveur (22.07.2026) : fond gris #F3F4F6 sur Total Matériel/Total HT/
            // Total TTC (.total-mat / .tot-row.bold / .tot-row.ttc côté Blazor) — pas sur
            // Main d'œuvre/Déplacements (.tot-row.semi, gras mais sans fond).
            if (isGrandTotal || greyBackground)
                c = c.Background("#F3F4F6");
            else if (fullBlueBackground)
                // ✅ Bleu sur les 4 colonnes (demande de Joe, 18.08.2026, "pas de champ blanc pour
                // TVA") : évite les cases Qt/Prix blanches quand la ligne n'a plus de contenu séparé
                // dans ces colonnes (taux affiché dans le libellé).
                c = c.Background("#EAF2FF");
            else if (blueBackground && (col == 0 || col == 3))
                c = c.Background("#EAF2FF");
            else if (bluePrixDecorative && col == 2)
                c = c.Background("#EAF2FF");
            else if (col == 1 || col == 2)
                // ✅ Blanc explicite (23.07.2026, demande de Joe) : sans ça, Qt/Prix
                // transparents laissaient transparaître le fond bleu de la carte Devis
                // (devisBg, posé derrière tout le contenu), au lieu d'être sans couleur
                // comme dans le WPF.
                c = c.Background(Colors.White);

            // ✅ BorderRight au lieu de BorderLeft (23.07.2026, demande de Joe : bordures
            // latérales plus grosses ici que dans le tableau Libellé) : même technique que
            // CellBodyWhite/CellBody (BorderRight, colonne 0 à 2 seulement) pour une
            // cohérence structurelle totale entre les deux tableaux.
            // ✅ Bordure droite de Prix/Pc retirée pour Rabais 1/2 et TVA (demande de Joe,
            // 18.08.2026, noPrixRightBorder), même principe que côté WPF (BorderThickness sans le
            // "0.5" à droite de la colonne 2 pour ces lignes).
            if (col != 3 && !noInnerDividers && !(col == 2 && noPrixRightBorder))
                c = c.BorderRight(0.75f).BorderColor("#000000");

            // ✅ Bordures haute/basse uniformes de Total Matériel à Total TTC (demande de Joe,
            // 18.08.2026) : Total TTC n'a plus de bordure haute forcée à 0.9 -- utilise le même
            // topBorderThickness que toutes les autres lignes du bloc, passé explicitement par
            // l'appelant comme les autres.
            if (topBorderThickness > 0)
                c = c.BorderTop(topBorderThickness).BorderColor("#000000");

            if (bottomBorderThickness > 0)
                c = c.BorderBottom(bottomBorderThickness).BorderColor("#000000");

            // ✅ Hauteur de ligne Total HT/TVA alignée sur Total TTC (demande de Joe, 11.08.2026) :
            // matchGrandTotalHeight augmente juste le padding vertical (6 au lieu de 3), sans
            // toucher à la taille de police ni au fond/bordure -- seule la hauteur doit matcher
            // Total TTC (fontSize=12/padding=5), pas son style visuel de mise en avant.
            var paddingV = isGrandTotal ? 5 : (matchGrandTotalHeight ? 6 : 3);
            c = c.PaddingVertical(paddingV).PaddingHorizontal(7);

            return c;
        }

        // ✅ Réplique serveur : colonne Qt centrée (.tot-n), Prix/pc alignée à droite (.tot-n + .tot-n).
        // ✅ Libellé aligné à droite de sa colonne (demande de Joe, 18.08.2026, "Total Matériel...")
        // sauf Main d'œuvre/Déplacements/Rabais/TVA, restés à gauche comme sur l'écran WPF
        // (WorkOrderWindow.xaml) -- voir labelAlignLeft sur chaque appel concerné.
        var labelCell = Cell(0);
        if (!labelAlignLeft) labelCell = labelCell.AlignRight();
        // ✅ labelValue rendu dans une zone de largeur fixe après "label" (demande de Joe,
        // 18.08.2026, "8,10 doit être aligné verticalement à Prorata") : Rabais 1/2 et TVA
        // partagent la même largeur de préfixe (85pt) pour que leur valeur (nom du rabais / taux
        // TVA) démarre toujours à la même position horizontale, quelle que soit la longueur du
        // texte "Rabais 1 (%)"/"Rabais 2 (%)"/"TVA (%)" qui précède.
        if (labelValue != null)
        {
            labelCell.Row(row =>
            {
                row.ConstantItem(85).Text(label).Style(labelStyle);
                row.RelativeItem().Text(labelValue).Style(labelStyle);
            });
        }
        else
        {
            labelCell.Text(label).Style(labelStyle);
        }
        Cell(1).AlignCenter().Text(qtyText ?? "");
        Cell(2).AlignRight().Text(unitPriceText ?? "");
        Cell(3).AlignRight().Text(totalText ?? "").Style(valueStyle);
    }

    // =========================
    // Helpers communs
    // =========================
    private static List<byte[]> SlicePngVerticallyIntoPages(byte[] pngBytes, PageSize pageSize, List<(double Top, double Bottom)>? avoidCutRanges = null)
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
            int naiveCutY = Math.Min(y + sliceHeightPx, imgH);
            int cutY = AdjustCutToAvoidSplittingSections(naiveCutY, y, avoidCutRanges);

            int h = Math.Max(1, cutY - y);
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(0, y, imgW, h));
            result.Add(EncodeBitmapSourceToPng(cropped));
            y += h;
        }

        return result;
    }

    // ✅ Évite de couper une carte pile entre deux pages (23.07.2026, demande de Joe) : si le point
    // de coupure naturel tombe au milieu d'une carte qui commence après le début de la page
    // courante, on recule la coupure jusqu'au début de cette carte (elle démarre alors la page
    // suivante en entier). Si la carte a déjà commencé avant le début de la page courante (donc
    // plus grande qu'une page à elle seule), aucun recul n'est possible : coupure brute inchangée.
    private static int AdjustCutToAvoidSplittingSections(int naiveCutY, int y, List<(double Top, double Bottom)>? ranges)
    {
        if (ranges == null || ranges.Count == 0)
            return naiveCutY;

        double cut = naiveCutY;

        for (int i = 0; i < 10; i++)
        {
            var conflicting = ranges.Where(r => r.Top < cut && cut < r.Bottom && r.Top > y).ToList();
            if (conflicting.Count == 0) break;

            var newCut = conflicting.Min(r => r.Top);
            if (newCut >= cut) break;
            cut = newCut;
        }

        var result = (int)Math.Round(cut);
        return result > y ? result : naiveCutY;
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

    // ✅ Recadre l'image de signature sur son contenu réel (identique au cropToContent()
    // JS côté web, Bdr.razor) : une signature saisie sur le grand InkCanvas du client
    // desktop est capturée avec tout son fond blanc autour — sans ce recadrage, le trait
    // reste minuscule au milieu de la box PDF quelle que soit la hauteur de celle-ci.
    // ✅ internal (au lieu de private, 22.07.2026, demande de Joe) : réutilisé par
    // WorkOrderWindow.CaptureSignaturePng pour que le PNG stocké soit déjà recadré, et non plus
    // seulement au moment de générer le PDF — voir commentaire dans CaptureSignaturePng.
    internal static byte[] CropSignatureToContent(byte[] pngBytes)
    {
        var source = LoadPngToBitmapSource(pngBytes);
        if (source == null) return pngBytes;

        var converted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        if (width <= 0 || height <= 0) return pngBytes;

        int stride = width * 4;
        var pixels = new byte[height * stride];

        try { converted.CopyPixels(pixels, stride, 0); }
        catch { return pngBytes; }

        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = rowStart + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                // Pixel d'encre : opaque et sensiblement plus sombre que le fond blanc.
                if (a > 20 && (r < 235 || g < 235 || b < 235))
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0 || maxY < 0)
            return pngBytes; // rien détecté (page blanche) : garder l'image telle quelle

        const int padding = 10;
        int cropX = Math.Max(0, minX - padding);
        int cropY = Math.Max(0, minY - padding);
        int cropRight = Math.Min(width, maxX + padding + 1);
        int cropBottom = Math.Min(height, maxY + padding + 1);
        int cropW = cropRight - cropX;
        int cropH = cropBottom - cropY;

        if (cropW <= 0 || cropH <= 0) return pngBytes;

        try
        {
            var cropped = new CroppedBitmap(converted, new Int32Rect(cropX, cropY, cropW, cropH));
            return EncodeBitmapSourceToPng(cropped);
        }
        catch
        {
            return pngBytes;
        }
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

    private static string GetProjectManagerRefLine(Project? project)
    {
        var name = (project?.ManagerName ?? "").Trim();
        return string.IsNullOrWhiteSpace(name) ? "" : $"Réf : {name}";
    }

    private static string GetProjectManagerContactLine(Project? project)
        => (project?.ManagerContact ?? "").Trim();

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

    // ✅ Réplique de FmtDateLong côté serveur ("9 juillet 2026")
    private static string FormatDateLong(DateTime date)
    {
        return date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    }

    // ✅ Réplique du texte de DecisionLabel() côté serveur (wo.ValidationDecision contient déjà
    // le mot français), SANS les émojis : Arial n'a pas de glyphes emoji couleur, ils s'affichaient
    // comme une case à cocher cassée dans le PDF.
    private static string DecisionLabelFr(string decision) => decision switch
    {
        "Validé" => "Je valide le devis",
        "Refusé" => "Je refuse le devis",
        "Annulé" => "J'annule la demande du devis",
        _ => decision
    };

    // =========================
    // Helpers BDR — champ "boîte blanche" (réplique .flabel/.fvalue du serveur)
    // =========================
    private static void FieldBox(IContainer container, string label, Action<IContainer> content, float minHeight = 20)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(9).SemiBold().FontColor("#6B7280");

            var box = c.Item()
                .PaddingTop(4)
                .Background(Colors.White)
                .Border(0.75f).BorderColor("#D1D5DB")
                .CornerRadius(4)
                .MinHeight(minHeight)
                .Padding(5);

            content(box);
        });
    }

    private static void FieldBoxText(IContainer container, string label, string? value, float minHeight = 20)
        => FieldBox(container, label, box => box.Text(string.IsNullOrWhiteSpace(value) ? "" : value).FontSize(10), minHeight);

}