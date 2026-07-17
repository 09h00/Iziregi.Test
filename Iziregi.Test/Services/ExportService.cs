// File: Services/ExportService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Microsoft.Data.Sqlite;

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

    // ✅ Sauvegarde/export complet (17.07.2026, demande de Joe) : contrepartie concrète à la
    // promesse de portabilité des données dans les futures CGV. Exporte TOUTES les tables de
    // la base locale en CSV (un fichier par table), dans un seul zip, en format ouvert
    // (lisible avec Excel/LibreOffice/Google Sheets, sans avoir besoin d'Iziregi installé).
    //
    // Introspection dynamique du schéma (sqlite_master + PRAGMA table_info) plutôt qu'une
    // liste de colonnes écrite en dur : le schéma évolue par ajout de colonnes au fil du temps
    // (voir TryAddColumn dans Db.cs) — avec une liste figée, il faudrait penser à mettre ce
    // fichier à jour à chaque nouvelle colonne, avec le risque réel d'oublier et de livrer un
    // export silencieusement incomplet. Avec l'introspection, toute nouvelle colonne apparaît
    // automatiquement.
    //
    // Ne contient aucune donnée sensible : la clé API du poste est stockée séparément, chiffrée
    // via DPAPI (iziregi-config.json, hors de cette base SQLite) — jamais dans une des tables
    // exportées ici.
    public static void ExportAllData(string targetZipPath)
    {
        if (string.IsNullOrWhiteSpace(targetZipPath))
            throw new ArgumentException("Chemin de fichier invalide.", nameof(targetZipPath));

        var dir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(targetZipPath))
            File.Delete(targetZipPath);

        using var con = Db.Open();
        con.Open();

        using var fs = File.Create(targetZipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        var tableNames = new List<string>();
        using (var cmdTables = con.CreateCommand())
        {
            cmdTables.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var rd = cmdTables.ExecuteReader();
            while (rd.Read())
                tableNames.Add(rd.GetString(0));
        }

        foreach (var table in tableNames)
            WriteTableCsvEntry(zip, con, table);

        WriteReadmeEntry(zip, tableNames);
    }

    private static void WriteTableCsvEntry(ZipArchive zip, SqliteConnection con, string table)
    {
        var columns = new List<string>();
        using (var cmdInfo = con.CreateCommand())
        {
            // ✅ Nom de table issu de sqlite_master (schéma de l'appli, jamais une saisie
            // utilisateur) : l'interpolation directe ici est sûre, contrairement aux requêtes
            // avec des valeurs saisies par l'utilisateur ailleurs dans l'app (qui utilisent
            // des paramètres).
            cmdInfo.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var rd = cmdInfo.ExecuteReader();
            var nameOrdinal = -1;
            while (rd.Read())
            {
                if (nameOrdinal < 0) nameOrdinal = rd.GetOrdinal("name");
                columns.Add(rd.GetString(nameOrdinal));
            }
        }

        if (columns.Count == 0) return;

        var entry = zip.CreateEntry($"{table}.csv", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine(string.Join(",", columns.ConvertAll(CsvEscape)));

        using var cmdData = con.CreateCommand();
        cmdData.CommandText = $"SELECT {string.Join(",", columns)} FROM \"{table}\"";
        using var rdData = cmdData.ExecuteReader();
        while (rdData.Read())
        {
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                values[i] = rdData.IsDBNull(i)
                    ? ""
                    : CsvEscape(Convert.ToString(rdData.GetValue(i), CultureInfo.InvariantCulture) ?? "");
            }
            writer.WriteLine(string.Join(",", values));
        }
    }

    private static void WriteReadmeEntry(ZipArchive zip, List<string> tableNames)
    {
        var entry = zip.CreateEntry("LISEZ-MOI.txt", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine("Export complet des données Iziregi");
        writer.WriteLine($"Généré le {DateTime.Now:yyyy-MM-dd HH:mm}");
        writer.WriteLine();
        writer.WriteLine("Ce fichier contient une copie complète de vos données, un fichier CSV par catégorie :");
        foreach (var t in tableNames)
            writer.WriteLine($"  - {t}.csv");
        writer.WriteLine();
        writer.WriteLine("Chaque CSV peut être ouvert avec Excel, LibreOffice Calc, Google Sheets, ou tout autre tableur.");
        writer.WriteLine("Ces fichiers restent lisibles et utilisables même sans l'application Iziregi installée.");
    }

    private static string CsvEscape(string s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}