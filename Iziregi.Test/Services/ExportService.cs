// File: Services/ExportService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Iziregi.Test.Models;

namespace Iziregi.Test.Services;

public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private class Manifest
    {
        public string FileType { get; set; } = "iziregi-package";
        public int Version { get; set; } = 1;
        public string Package { get; set; } = ""; // "devis" | "signature"
        public long WorkOrderId { get; set; }
        public long? ProjectId { get; set; }
        public string ExportedAtUtc { get; set; } = "";
    }

    public static void ExportQuoteRequestPackage(string targetZipPath, WorkOrder workOrder, List<WorkOrderLine> lines)
        => ExportPackage(targetZipPath, "devis", workOrder, lines);

    public static void ExportSignatureRequestPackage(string targetZipPath, WorkOrder workOrder, List<WorkOrderLine> lines)
        => ExportPackage(targetZipPath, "signature", workOrder, lines);

    private static void ExportPackage(string targetZipPath, string package, WorkOrder workOrder, List<WorkOrderLine> lines)
    {
        if (string.IsNullOrWhiteSpace(targetZipPath))
            throw new ArgumentException("Chemin de fichier invalide.", nameof(targetZipPath));

        if (workOrder == null || workOrder.Id <= 0)
            throw new InvalidOperationException("Le bon doit être enregistré avant export.");

        if (string.IsNullOrWhiteSpace(package))
            throw new ArgumentException("Package invalide.", nameof(package));

        var dir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // écrase si existe
        if (File.Exists(targetZipPath))
            File.Delete(targetZipPath);

        var manifest = new Manifest
        {
            Package = package,
            WorkOrderId = workOrder.Id,
            ProjectId = workOrder.ProjectId,
            ExportedAtUtc = DateTime.UtcNow.ToString("o")
        };

        using var fs = File.Create(targetZipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteJsonEntry(zip, "manifest.json", manifest);
        WriteJsonEntry(zip, "workOrder.json", workOrder);
        WriteJsonEntry(zip, "lines.json", lines ?? new List<WorkOrderLine>());
    }

    private static void WriteJsonEntry<T>(ZipArchive zip, string entryName, T obj)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        stream.Write(bytes, 0, bytes.Length);
    }
}