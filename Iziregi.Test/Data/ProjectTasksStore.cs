// File: Data/ProjectTasksStore.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Iziregi.Test.Data;

// ✅ Schéma partagé du fichier "planning-tasks-{projectId}.json" (30.07.2026) : extrait de
// PlanningPage.xaml.cs (TaskRowState) pour être réutilisable par ArchivesTasksPage sans
// dupliquer/risquer une divergence du format de fichier entre les deux pages.
public sealed class TaskRecord
{
    public string Ref { get; set; } = "";
    public string Company { get; set; } = "";
    public string Building { get; set; } = "";
    public string Floor { get; set; } = "";
    public string Todo { get; set; } = "";
    public string TodoDocumentXaml { get; set; } = "";
    public string Category { get; set; } = "";
    public string Reserve { get; set; } = "";
    public string Urgent { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? DoneAt { get; set; }
    public DateTime? CreatedWeekStart { get; set; }

    // ✅ Archivage des tâches (30.07.2026, demande de Joe) : même principe que
    // WorkOrders.IsArchived/ArchivedAt (voir Db.cs), mais ici en JSON puisque les tâches ne
    // vivent pas dans SQLite.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // ✅ Corbeille des tâches (demande de Joe), même principe qu'IsArchived/ArchivedAt.
    public bool IsTrashed { get; set; }
    public DateTime? TrashedAt { get; set; }

    // ✅ NOUVEAU (20.08.2026, demande de Joe : "avec un bouton placé dans le descriptif, je
    // dois pouvoir sélectionner si je le place dans la nouvelle section") : coche par tâche,
    // choisie via un bouton dans la cellule Descriptif de la grille (voir PlanningPage.xaml,
    // TaskTodoColumn) -- seules les tâches cochées apparaissent dans "Détails tâches"
    // (RebuildTaskDetailsUI). Faux par défaut : rien n'apparaît tant que Joe n'a rien choisi.
    public bool IncludeInTaskDetails { get; set; }
}

public static class ProjectTasksStore
{
    public static string GetFilePath(long? projectId)
    {
        var pid = (projectId.HasValue && projectId.Value > 0)
            ? projectId.Value.ToString(CultureInfo.InvariantCulture)
            : "0";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Iziregi", "Planning");
        return Path.Combine(dir, $"planning-tasks-{pid}.json");
    }

    public static List<TaskRecord> Load(long? projectId)
    {
        var filePath = GetFilePath(projectId);
        if (!File.Exists(filePath))
            return new List<TaskRecord>();

        try
        {
            var json = File.ReadAllText(filePath);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<TaskRecord>>(json, opts) ?? new();
        }
        catch
        {
            return new List<TaskRecord>();
        }
    }

    public static void Save(long? projectId, List<TaskRecord> rows)
    {
        var filePath = GetFilePath(projectId);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
