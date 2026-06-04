// File: Services/PackageImportService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Iziregi.Test.Models;

namespace Iziregi.Test.Services;

public static class PackageImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ✅ Doit être au moins aussi accessible que les propriétés publiques qui l'exposent
    public class Manifest
    {
        public string FileType { get; set; } = "";
        public int Version { get; set; } = 0;
        public string Package { get; set; } = ""; // "devis" | "signature"
        public long WorkOrderId { get; set; }
        public long? ProjectId { get; set; }
        public string ExportedAtUtc { get; set; } = "";
    }

    public class ImportedPackage
    {
        public string PackageType { get; set; } = ""; // "devis" | "signature"
        public Manifest Manifest { get; set; } = new();
        public WorkOrder WorkOrder { get; set; } = new();
        public List<WorkOrderLine> Lines { get; set; } = new();
    }

    public static ImportedPackage Load(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Chemin de fichier invalide.", nameof(packagePath));

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Fichier introuvable.", packagePath);

        using var fs = File.OpenRead(packagePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

        var manifest = ReadJsonEntry<Manifest>(zip, "manifest.json");
        if (manifest == null)
            throw new InvalidOperationException("manifest.json manquant ou illisible.");

        if (!string.Equals(manifest.FileType, "iziregi-package", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ce fichier n’est pas un package Iziregi valide (FileType).");

        if (manifest.Version != 1)
            throw new InvalidOperationException($"Version de package non supportée : {manifest.Version} (attendu: 1).");

        var packageType = (manifest.Package ?? "").Trim().ToLowerInvariant();
        if (packageType != "devis" && packageType != "signature")
            throw new InvalidOperationException($"Type de package invalide : '{manifest.Package}' (attendu: devis|signature).");

        var wo = ReadJsonEntry<WorkOrder>(zip, "workOrder.json");
        if (wo == null)
            throw new InvalidOperationException("workOrder.json manquant ou illisible.");

        var lines = ReadJsonEntry<List<WorkOrderLine>>(zip, "lines.json") ?? new List<WorkOrderLine>();

        return new ImportedPackage
        {
            PackageType = packageType,
            Manifest = manifest,
            WorkOrder = wo,
            Lines = lines
        };
    }

    private static T? ReadJsonEntry<T>(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry == null) return default;

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }
}