using System;
using System.Globalization;
using Iziregi.Test.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Iziregi.Test.Services;

public static class PdfService
{
    // Appelé au démarrage
    public static void Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void GenerateWorkOrderPdf(string filePath, WorkOrder wo, List<WorkOrderLine> lines)
    {
        if (wo == null) throw new ArgumentNullException(nameof(wo));
        lines ??= new List<WorkOrderLine>();

        var culture = new CultureInfo("fr-CH");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Element(header =>
                {
                    header.Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Iziregi").SemiBold().FontSize(14);
                            col.Item().Text("Bon de régie").FontSize(12);
                        });

                        row.ConstantItem(220).Column(col =>
                        {
                            col.Item().AlignRight().Text($"BDR — N° {wo.BdrNumber}").SemiBold().FontSize(14);
                            col.Item().AlignRight().Text($"{wo.Place}");
                            col.Item().AlignRight().Text($"{wo.RequestDate:dd.MM.yyyy}");
                        });
                    });

                    header.LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    // ---- DEMANDE ----
                    col.Item().Element(section =>
                    {
                        section.Column(s =>
                        {
                            s.Item().Text("Demande").SemiBold().FontSize(13);

                            s.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(120);
                                    c.RelativeColumn();
                                });

                                t.Cell().Text("Bat./Salle").SemiBold();
                                t.Cell().Text(wo.Place ?? "");

                                t.Cell().Text("Demandé par").SemiBold();
                                t.Cell().Text(wo.RequestedBy ?? "");

                                t.Cell().Text("Effectué par").SemiBold();
                                t.Cell().Text(wo.PerformedBy ?? "");

                                t.Cell().Text("Date").SemiBold();
                                t.Cell().Text(wo.RequestDate.ToString("dd.MM.yyyy"));
                            });

                            s.Item().PaddingTop(6).Text("Descriptif").SemiBold();
                            s.Item().Text(string.IsNullOrWhiteSpace(wo.Description) ? "—" : wo.Description);
                        });
                    });

                    // ---- DEVIS ----
                    col.Item().Element(section =>
                    {
                        section.Column(s =>
                        {
                            s.Item().Text("Devis").SemiBold().FontSize(13);

                            s.Item().PaddingTop(4).Text("Matériel").SemiBold();

                            s.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(24);
                                    c.RelativeColumn();
                                    c.ConstantColumn(50);
                                    c.ConstantColumn(75);
                                    c.ConstantColumn(75);
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("#").SemiBold();
                                    h.Cell().Text("Libellé").SemiBold();
                                    h.Cell().AlignRight().Text("Qt").SemiBold();
                                    h.Cell().AlignRight().Text("Prix").SemiBold();
                                    h.Cell().AlignRight().Text("Total").SemiBold();
                                    h.Cell().ColumnSpan(5).PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                                });

                                if (lines.Count == 0)
                                {
                                    t.Cell().ColumnSpan(5).Text("—").FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    for (int i = 0; i < lines.Count; i++)
                                    {
                                        var l = lines[i];
                                        var lineTotal = Math.Round(l.Qty * l.UnitPrice, 2);

                                        t.Cell().Text((i + 1).ToString());
                                        t.Cell().Text(l.Label ?? "");
                                        t.Cell().AlignRight().Text(l.Qty.ToString("0.##", culture));
                                        t.Cell().AlignRight().Text(l.UnitPrice.ToString("0.00", culture));
                                        t.Cell().AlignRight().Text(lineTotal.ToString("0.00", culture));
                                    }
                                }
                            });

                            var laborTotal = Math.Round(wo.LaborHours * wo.LaborRate, 2);
                            var travelTotal = Math.Round(wo.TravelQty * wo.TravelRate, 2);
                            var materialTotal = Math.Round(lines.Sum(x => Math.Round(x.Qty * x.UnitPrice, 2)), 2);

                            var totalHt = Math.Round(materialTotal + laborTotal + travelTotal, 2);
                            var tvaAmount = Math.Round(totalHt * (wo.TvaRate / 100.0), 2);
                            var totalTtc = Math.Round(totalHt + tvaAmount, 2);

                            s.Item().PaddingTop(8).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn();
                                    c.ConstantColumn(120);
                                });

                                t.Cell().Text("Main d’œuvre").SemiBold();
                                t.Cell().AlignRight().Text(laborTotal.ToString("0.00", culture));

                                t.Cell().Text("Déplacements").SemiBold();
                                t.Cell().AlignRight().Text(travelTotal.ToString("0.00", culture));

                                t.Cell().Text("Total HT").SemiBold();
                                t.Cell().AlignRight().Text(totalHt.ToString("0.00", culture));

                                t.Cell().Text($"TVA {wo.TvaRate:0.##}%").SemiBold();
                                t.Cell().AlignRight().Text(tvaAmount.ToString("0.00", culture));

                                t.Cell().Text("Total TTC").SemiBold().FontSize(12);
                                t.Cell().AlignRight().Text(totalTtc.ToString("0.00", culture)).SemiBold().FontSize(12);
                            });

                            s.Item().PaddingTop(6).Text("Remarques").SemiBold();
                            s.Item().Text(string.IsNullOrWhiteSpace(wo.QuoteNotes) ? "—" : wo.QuoteNotes);
                        });
                    });

                    // ---- SIGNATURE ----
                    col.Item().Element(section =>
                    {
                        section.Column(s =>
                        {
                            s.Item().Text("Validation / Signature").SemiBold().FontSize(13);

                            s.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(120);
                                    c.RelativeColumn();
                                });

                                t.Cell().Text("Nom").SemiBold();
                                t.Cell().Text(string.IsNullOrWhiteSpace(wo.SignatureName) ? "—" : wo.SignatureName);

                                t.Cell().Text("Date").SemiBold();
                                s
                                    .Item(); // noop: garde la structure stable (QuestPDF est parfois strict sur les chaines)
                                t.Cell().Text(wo.SignatureDate.HasValue
                                    ? wo.SignatureDate.Value.ToString("d MMMM yyyy", culture)  // ✅ format long
                                    : "—");
                            });

                            s.Item().PaddingTop(6).Text("Signature").SemiBold();

                            // ✅ Zone stable (même hauteur) + bordure légère
                            s.Item().Height(90).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Element(sig =>
                            {
                                if (wo.SignaturePng != null && wo.SignaturePng.Length > 0)
                                {
                                    // ✅ Conserve le ratio et rentre dans la zone
                                    sig.Image(wo.SignaturePng).FitArea();
                                }
                                else
                                {
                                    sig.AlignMiddle().AlignCenter().Text("Non signé").FontColor(Colors.Grey.Darken1);
                                }
                            });
                        });
                    });
                });

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.Span($"Bon de régie {wo.BdrNumber} — ");
                    txt.CurrentPageNumber();
                    txt.Span("/");
                    txt.TotalPages();
                });
            });
        })
        .GeneratePdf(filePath);
    }
}