// File: Data/Db.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Dapper;
using Microsoft.Data.Sqlite;
using Iziregi.Test.Models;

namespace Iziregi.Test.Data;

public static class Db
{
    private static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "Data");

    private static string DbPath => Path.Combine(DataDir, "iziregi.db");

    private static string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DbPath
    }.ToString();

    public static SqliteConnection Open()
    {
        Directory.CreateDirectory(DataDir);
        return new SqliteConnection(ConnectionString);
    }

    public static void Init()
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            CREATE TABLE IF NOT EXISTS WorkOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BdrNumber INTEGER NOT NULL,
                Place TEXT NOT NULL,
                RequestedBy TEXT NOT NULL,
                PerformedBy TEXT NOT NULL,
                RequestDate TEXT NOT NULL,
                IsValidated INTEGER NOT NULL DEFAULT 0,
                IsPerformed INTEGER NOT NULL DEFAULT 0
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Places (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
        """);

        // ✅ NOUVEAU : Etages (liste)
        con.Execute("""
            CREATE TABLE IF NOT EXISTS Etages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Companies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Requesters (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Address TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1
            );
        """);
        TryAddColumn(con, "Projects", "ColorHex", "TEXT");
        TryAddColumn(con, "Projects", "AddressLine", "TEXT NOT NULL DEFAULT ''");
        TryAddColumn(con, "Projects", "ZipCity", "TEXT NOT NULL DEFAULT ''");
        TryAddColumn(con, "Projects", "ManagerName", "TEXT NOT NULL DEFAULT ''");
        TryAddColumn(con, "Projects", "ManagerContact", "TEXT NOT NULL DEFAULT ''");

        con.Execute("""
            CREATE TABLE IF NOT EXISTS WorkOrderLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WorkOrderId INTEGER NOT NULL,
                Label TEXT NOT NULL,
                Qty REAL NOT NULL,
                UnitPrice REAL NOT NULL,
                LineTotal REAL NOT NULL,
                FOREIGN KEY (WorkOrderId) REFERENCES WorkOrders(Id)
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        """);

        con.Execute("""
            CREATE TABLE IF NOT EXISTS Reserves (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
        """);

        // ✅ NOUVEAU : Couleurs des entreprises (par projet)
        con.Execute("""
            CREATE TABLE IF NOT EXISTS CompanyColors (
                ProjectId INTEGER NOT NULL,
                CompanyName TEXT NOT NULL,
                ColorHex TEXT NOT NULL,
                PRIMARY KEY (ProjectId, CompanyName)
            );
        """);

        // ✅ NOUVEAU : Couleurs des catégories de tâches (par projet) — indépendant des
        // couleurs d'entreprises (demande de Joe, 17.07.2026), même schéma en miroir.
        con.Execute("""
            CREATE TABLE IF NOT EXISTS TaskCategoryColors (
                ProjectId INTEGER NOT NULL,
                CategoryName TEXT NOT NULL,
                ColorHex TEXT NOT NULL,
                PRIMARY KEY (ProjectId, CategoryName)
            );
        """);

        // ✅ NOUVEAU : Liste "Zone de texte planning" (par projet)
        con.Execute("""
            CREATE TABLE IF NOT EXISTS PlanningTextZones (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Name TEXT NOT NULL
            );
        """);
        TryCreateUniqueIndex(con, "UX_PlanningTextZones_ProjectId_Name", "PlanningTextZones", "ProjectId, Name");

        // ✅ NOUVEAU : Listes "Cat." et "Urg." des tâches de Planning (par projet)
        con.Execute("""
            CREATE TABLE IF NOT EXISTS TaskCategories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Name TEXT NOT NULL
            );
        """);
        TryCreateUniqueIndex(con, "UX_TaskCategories_ProjectId_Name", "TaskCategories", "ProjectId, Name");

        con.Execute("""
            CREATE TABLE IF NOT EXISTS TaskUrgencies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Name TEXT NOT NULL
            );
        """);
        TryCreateUniqueIndex(con, "UX_TaskUrgencies_ProjectId_Name", "TaskUrgencies", "ProjectId, Name");

        // Colonnes ajoutées progressivement (compat)
        TryAddColumn(con, "WorkOrders", "Description", "TEXT");
        TryAddColumn(con, "WorkOrders", "IsCancelled", "INTEGER NOT NULL DEFAULT 0");

        TryAddColumn(con, "WorkOrders", "LaborHours", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "LaborRate", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TravelQty", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TravelRate", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TvaRate", "REAL NOT NULL DEFAULT 8.1");

        // ✅ NOUVEAU : rabais (%) persistant
        TryAddColumn(con, "WorkOrders", "DiscountRate", "REAL NOT NULL DEFAULT 0");

        // =========================
        // ✅ NOUVEAU : Forfait selon doc annexé (ligne forfait) + PDF devis forfaitaire
        // =========================
        TryAddColumn(con, "WorkOrders", "ForfaitQty", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "ForfaitUnitPrice", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "ForfaitPdfFileName", "TEXT NOT NULL DEFAULT ''");
        TryAddColumn(con, "WorkOrders", "ForfaitPdfFileBytes", "BLOB");

        TryAddColumn(con, "WorkOrders", "QuoteNotes", "TEXT");

        TryAddColumn(con, "WorkOrders", "ProjectId", "INTEGER");

        TryAddColumn(con, "WorkOrders", "SignatureName", "TEXT");
        TryAddColumn(con, "WorkOrders", "SignatureDate", "TEXT");
        TryAddColumn(con, "WorkOrders", "SignaturePng", "BLOB");

        TryAddColumn(con, "WorkOrders", "IsInCreation", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsSentToCompany", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsQuoteReceived", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsSentToSigner", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsValidatedPdfSent", "INTEGER NOT NULL DEFAULT 0");

        TryAddColumn(con, "WorkOrders", "IsPendingValidation", "INTEGER NOT NULL DEFAULT 0");

        TryAddColumn(con, "WorkOrders", "IsTrashed", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TrashedAt", "TEXT");

        TryAddColumn(con, "WorkOrders", "IsArchived", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "ArchivedAt", "TEXT");

        TryAddColumn(con, "WorkOrders", "Reserve", "TEXT");

        // Ajouts récents (devis)
        TryAddColumn(con, "WorkOrders", "QuoteName", "TEXT");
        TryAddColumn(con, "WorkOrders", "QuoteDate", "TEXT");

        // ✅ Nouveau : délai (date) - stocké en TEXT yyyy-MM-dd
        TryAddColumn(con, "WorkOrders", "DeadlineDate", "TEXT");

        // ✅ Import packages : trace de la source
        TryAddColumn(con, "WorkOrders", "ImportedFromWorkOrderId", "INTEGER");
        TryAddColumn(con, "WorkOrders", "ImportedAtUtc", "TEXT");

        // ✅ Dashboard : dates indépendantes (stockées en TEXT yyyy-MM-dd)
        TryAddColumn(con, "WorkOrders", "DistributedAt", "TEXT");
        TryAddColumn(con, "WorkOrders", "PerformedAt", "TEXT");

        // ✅ Etage (liste)
        TryAddColumn(con, "WorkOrders", "Etage", "TEXT");

        // ✅ NOUVEAU : décision de validation (Validé / Refusé / Annulé)
        TryAddColumn(con, "WorkOrders", "ValidationDecision", "TEXT");

        // ✅ NOUVEAU : timestamps des envois de liens BDR (expiration)
        TryAddColumn(con, "WorkOrders", "CompanyLinkSentAt", "TEXT");
        TryAddColumn(con, "WorkOrders", "SignerLinkSentAt", "TEXT");

        // =========================
        // ✅ Refactor "Listes par projet"
        // =========================
        TryAddColumn(con, "Places", "ProjectId", "INTEGER");
        TryAddColumn(con, "Etages", "ProjectId", "INTEGER");
        TryAddColumn(con, "Companies", "ProjectId", "INTEGER");
        TryAddColumn(con, "Requesters", "ProjectId", "INTEGER");
        TryAddColumn(con, "Reserves", "ProjectId", "INTEGER");

        // ✅ IMPORTANT :
        // Les tables ont été créées historiquement avec "Name UNIQUE".
        // Ça empêche d’avoir la même valeur dans 2 projets.
        // Il faut migrer les tables pour enlever l’unicité globale sur Name.
        MigrateListsToPerProjectUniqueness(con);

        // Migration simple : rattacher les anciennes entrées au projet courant, sinon au 1er projet.
        BackfillListProjectIds(con);

        // Unicité par projet (index unique)
        TryCreateUniqueIndex(con, "UX_Places_ProjectId_Name", "Places", "ProjectId, Name");
        TryCreateUniqueIndex(con, "UX_Etages_ProjectId_Name", "Etages", "ProjectId, Name");
        TryCreateUniqueIndex(con, "UX_Companies_ProjectId_Name", "Companies", "ProjectId, Name");
        TryCreateUniqueIndex(con, "UX_Requesters_ProjectId_Name", "Requesters", "ProjectId, Name");
        TryCreateUniqueIndex(con, "UX_Reserves_ProjectId_Name", "Reserves", "ProjectId, Name");
        BackfillProjectsAddressSplit(con);
        BackfillArchitectAddressSplitAndWebsiteDefaults();
    }

    private static bool ColumnExists(SqliteConnection con, string table, string column)
    {
        try
        {
            var rows = con.Query("PRAGMA table_info(" + table + ");").ToList();

            foreach (var r in rows)
            {
                try
                {
                    var dict = (IDictionary<string, object>)r;
                    if (!dict.TryGetValue("name", out var v) || v == null)
                        continue;

                    var name = v.ToString() ?? "";
                    if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static void TryAddColumn(SqliteConnection con, string table, string column, string sqlType)
    {
        try
        {
            if (ColumnExists(con, table, column))
                return;

            con.Execute($"ALTER TABLE {table} ADD COLUMN {column} {sqlType};");
        }
        catch
        {
        }
    }

    private static void TryCreateUniqueIndex(SqliteConnection con, string indexName, string table, string columnsCsv)
    {
        try
        {
            con.Execute($"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {table} ({columnsCsv});");
        }
        catch
        {
        }
    }

    private static bool TableHasUniqueNameConstraint(SqliteConnection con, string table)
    {
        try
        {
            var sql = con.ExecuteScalar<string?>(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name=@Name;",
                new { Name = table }
            ) ?? "";

            sql = sql.ToUpperInvariant().Replace("\r", " ").Replace("\n", " ");
            return sql.Contains("NAME") && sql.Contains("UNIQUE");
        }
        catch
        {
            return false;
        }
    }

    private static void MigrateListsToPerProjectUniqueness(SqliteConnection con)
    {
        if (con.State != System.Data.ConnectionState.Open)
            con.Open();

        using var tx = con.BeginTransaction();

        try
        {
            MigrateOneListTable(con, tx, "Places");
            MigrateOneListTable(con, tx, "Etages");
            MigrateOneListTable(con, tx, "Companies");
            MigrateOneListTable(con, tx, "Requesters");
            MigrateOneListTable(con, tx, "Reserves");

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
        }
    }

    private static void MigrateOneListTable(SqliteConnection con, SqliteTransaction tx, string table)
    {
        if (!TableHasUniqueNameConstraint(con, table))
            return;

        var newTable = $"{table}_New_NoUniqueName";

        try { con.Execute($"DROP TABLE IF EXISTS {newTable};", transaction: tx); } catch { }

        con.Execute($"""
            CREATE TABLE {newTable} (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ProjectId INTEGER
            );
        """, transaction: tx);

        con.Execute($"""
            INSERT INTO {newTable} (Id, Name, ProjectId)
            SELECT Id, Name, ProjectId
            FROM {table};
        """, transaction: tx);

        con.Execute($"DROP TABLE {table};", transaction: tx);
        con.Execute($"ALTER TABLE {newTable} RENAME TO {table};", transaction: tx);
    }

    private static void BackfillListProjectIds(SqliteConnection con)
    {
        try
        {
            long? current = GetCurrentProjectId();
            long first = 0;

            try
            {
                first = con.ExecuteScalar<long>("SELECT COALESCE(MIN(Id), 0) FROM Projects;");
            }
            catch { first = 0; }

            long target = (current.HasValue && current.Value > 0) ? current.Value : first;

            if (target <= 0)
                return;

            con.Execute("UPDATE Places SET ProjectId=@Id WHERE ProjectId IS NULL;", new { Id = target });
            con.Execute("UPDATE Etages SET ProjectId=@Id WHERE ProjectId IS NULL;", new { Id = target });
            con.Execute("UPDATE Companies SET ProjectId=@Id WHERE ProjectId IS NULL;", new { Id = target });
            con.Execute("UPDATE Requesters SET ProjectId=@Id WHERE ProjectId IS NULL;", new { Id = target });
            con.Execute("UPDATE Reserves SET ProjectId=@Id WHERE ProjectId IS NULL;", new { Id = target });
        }
        catch
        {
        }
    }

    private static long AsLong(object? v, long def = 0)
    {
        if (v == null || v is DBNull) return def;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return def; }
    }

    private static int AsInt(object? v, int def = 0)
    {
        if (v == null || v is DBNull) return def;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; }
    }

    private static double AsDouble(object? v, double def = 0)
    {
        if (v == null || v is DBNull) return def;
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
    }

    private static string AsString(object? v, string def = "")
    {
        if (v == null || v is DBNull) return def;
        return v.ToString() ?? def;
    }

    private static bool AsBool01(object? v) => AsInt(v, 0) == 1;

    private static DateTime AsDate(string? s, DateTime def)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : def;

    private static DateTime? AsNullableDate(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : AsDate(s, DateTime.Today);

    // =========================
    // ✅ Helpers split/join adresse (migration)
    // =========================
    private static (string line1, string line2) SplitAddressTwoLines(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
            return ("", "");

        // Coupe au dernier ","
        var lastComma = s.LastIndexOf(',');
        if (lastComma >= 0 && lastComma < s.Length - 1)
        {
            var a = s.Substring(0, lastComma).Trim();
            var b = s.Substring(lastComma + 1).Trim();
            return (a, b);
        }

        return (s, "");
    }

    private static void BackfillProjectsAddressSplit(SqliteConnection con)
    {
        try
        {
            var rows = con.Query<(long Id, string Address, string AddressLine, string ZipCity)>(@"
            SELECT Id,
                   COALESCE(Address,'') AS Address,
                   COALESCE(AddressLine,'') AS AddressLine,
                   COALESCE(ZipCity,'') AS ZipCity
            FROM Projects;
        ").ToList();

            foreach (var r in rows)
            {
                var a = (r.AddressLine ?? "").Trim();
                var z = (r.ZipCity ?? "").Trim();

                // Déjà rempli => rien à faire
                if (!string.IsNullOrWhiteSpace(a) || !string.IsNullOrWhiteSpace(z))
                    continue;

                var raw = (r.Address ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var (line1, line2) = SplitAddressTwoLines(raw);

                con.Execute(@"
                UPDATE Projects
                SET AddressLine=@A,
                    ZipCity=@B
                WHERE Id=@Id;
            ", new { Id = r.Id, A = line1, B = line2 });
            }
        }
        catch
        {
        }
    }

    private static void BackfillArchitectAddressSplitAndWebsiteDefaults()
    {
        try
        {
            var old = GetSetting("ArchitectAddress") ?? "";
            var line = GetSetting("ArchitectAddressLine");
            var zip = GetSetting("ArchitectZipCity");
            var web = GetSetting("ArchitectWebsite");

            // website par défaut vide
            if (string.IsNullOrWhiteSpace(web))
                SetSetting("ArchitectWebsite", "");

            // backfill split depuis l'ancien champ si les nouveaux sont vides
            if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(zip) && !string.IsNullOrWhiteSpace(old))
            {
                var (a, b) = SplitAddressTwoLines(old);
                SetSetting("ArchitectAddressLine", a);
                SetSetting("ArchitectZipCity", b);
            }
            else
            {
                // si clés absentes => on les crée
                if (line == null) SetSetting("ArchitectAddressLine", "");
                if (zip == null) SetSetting("ArchitectZipCity", "");
            }
        }
        catch
        {
        }
    }

    // =========================
    // Settings
    // =========================
    private static string? GetSetting(string key)
    {
        using var con = Open();
        con.Open();
        return con.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key=@Key;", new { Key = key });
    }

    private static void SetSetting(string key, string value)
    {
        using var con = Open();
        con.Open();
        con.Execute("""
            INSERT INTO Settings(Key, Value) VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
        """, new { Key = key, Value = value });
    }

    private static string MakeProjectKey(string baseKey, long projectId) => $"{baseKey}.P{projectId}";

    public static string GetDefaultPlace(long projectId) =>
        GetSetting(MakeProjectKey("DefaultPlace", projectId)) ?? "";

    public static void SetDefaultPlace(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultPlace", projectId), (value ?? "").Trim());

    public static string GetDefaultCompany(long projectId) =>
        GetSetting(MakeProjectKey("DefaultCompany", projectId)) ?? "";

    public static void SetDefaultCompany(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultCompany", projectId), (value ?? "").Trim());

    public static string GetDefaultRequester(long projectId) =>
        GetSetting(MakeProjectKey("DefaultRequester", projectId)) ?? "";

    public static void SetDefaultRequester(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultRequester", projectId), (value ?? "").Trim());

    public static string GetDefaultReserve(long projectId) =>
        GetSetting(MakeProjectKey("DefaultReserve", projectId)) ?? "";

    public static void SetDefaultReserve(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultReserve", projectId), (value ?? "").Trim());

    // ✅ Etage par défaut
    public static string GetDefaultEtage(long projectId) =>
        GetSetting(MakeProjectKey("DefaultEtage", projectId)) ?? "";

    public static void SetDefaultEtage(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultEtage", projectId), (value ?? "").Trim());

    // ✅ NOUVEAU : Zone de texte planning par défaut (par projet)
    public static string GetDefaultPlanningTextZone(long projectId) =>
        GetSetting(MakeProjectKey("DefaultPlanningTextZone", projectId)) ?? "";

    public static void SetDefaultPlanningTextZone(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultPlanningTextZone", projectId), (value ?? "").Trim());

    // ✅ NOUVEAU : "Cat." / "Urg." par défaut (tâches de Planning, par projet)
    public static string GetDefaultTaskCategory(long projectId) =>
        GetSetting(MakeProjectKey("DefaultTaskCategory", projectId)) ?? "";

    public static void SetDefaultTaskCategory(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultTaskCategory", projectId), (value ?? "").Trim());

    // ✅ NOUVEAU (18.07.2026, demande de Joe) : affichage Samedi et Dimanche par défaut,
    // indépendamment l'un de l'autre (par projet) — un utilisateur peut travailler tous les
    // samedis mais jamais les dimanches. S'applique aux nouvelles semaines du planning
    // hebdomadaire ET, quand la case est cochée, immédiatement à toutes les semaines déjà
    // sauvegardées (voir PlanningPage.xaml.cs, WeekendSaturdayDefault_Changed/WeekendSundayDefault_Changed).
    public static bool GetDefaultShowSaturday(long projectId) =>
        GetSetting(MakeProjectKey("DefaultShowSaturday", projectId)) == "1";

    public static void SetDefaultShowSaturday(long projectId, bool value) =>
        SetSetting(MakeProjectKey("DefaultShowSaturday", projectId), value ? "1" : "0");

    public static bool GetDefaultShowSunday(long projectId) =>
        GetSetting(MakeProjectKey("DefaultShowSunday", projectId)) == "1";

    public static void SetDefaultShowSunday(long projectId, bool value) =>
        SetSetting(MakeProjectKey("DefaultShowSunday", projectId), value ? "1" : "0");

    // ✅ NOUVEAU (19.07.2026, demande de Joe) : structure de la semaine (5 ou 6 jours de
    // base), persistée par projet. Défaut "5" pour un projet jamais configuré (livraison
    // aux nouveaux utilisateurs) -- une fois que l'utilisateur a fait un choix, celui-ci est
    // mémorisé pour les prochains lancements de l'app.
    public static int GetPlanningWeekDayCount(long projectId) =>
        GetSetting(MakeProjectKey("PlanningWeekDayCount", projectId)) == "6" ? 6 : 5;

    public static void SetPlanningWeekDayCount(long projectId, int value) =>
        SetSetting(MakeProjectKey("PlanningWeekDayCount", projectId), value == 6 ? "6" : "5");

    public static string GetDefaultTaskUrgency(long projectId) =>
        GetSetting(MakeProjectKey("DefaultTaskUrgency", projectId)) ?? "";

    public static void SetDefaultTaskUrgency(long projectId, string value) =>
        SetSetting(MakeProjectKey("DefaultTaskUrgency", projectId), (value ?? "").Trim());

    public static string GetDefaultPlace() => GetSetting("DefaultPlace") ?? "";
    public static void SetDefaultPlace(string value) => SetSetting("DefaultPlace", (value ?? "").Trim());

    public static string GetDefaultCompany() => GetSetting("DefaultCompany") ?? "";
    public static void SetDefaultCompany(string value) => SetSetting("DefaultCompany", (value ?? "").Trim());

    public static string GetDefaultPlanningTextZone() => GetSetting("DefaultPlanningTextZone") ?? "";
    public static void SetDefaultPlanningTextZone(string value) => SetSetting("DefaultPlanningTextZone", (value ?? "").Trim());

    public static string GetDefaultRequester() => GetSetting("DefaultRequester") ?? "";
    public static void SetDefaultRequester(string value) => SetSetting("DefaultRequester", (value ?? "").Trim());
    // ✅ NEW: Server sync cursor (UTC ISO string, e.g. 2026-06-05T13:10:00Z)
    public static string? GetLastServerSyncUtc()
        => GetSetting("LastServerSyncUtc");

    public static void SetLastServerSyncUtc(string utcIso)
        => SetSetting("LastServerSyncUtc", (utcIso ?? "").Trim());

    public static string GetDefaultReserve() => GetSetting("DefaultReserve") ?? "";
    public static void SetDefaultReserve(string value) => SetSetting("DefaultReserve", (value ?? "").Trim());

    public static string GetDefaultEtage() => GetSetting("DefaultEtage") ?? "";
    public static void SetDefaultEtage(string value) => SetSetting("DefaultEtage", (value ?? "").Trim());

    // ✅ Colonnes à listes déroulantes visibles dans le tableau des Tâches (Planning),
    // choisies via le bouton "Colonnes" — mémorisé globalement (pas par projet), liste
    // de clés séparées par des virgules parmi : Company, Building, Floor, Category, Reserve.
    public static string GetTasksVisibleColumns() => GetSetting("TasksVisibleColumns") ?? "Company,Building,Floor,Category";
    public static void SetTasksVisibleColumns(string value) => SetSetting("TasksVisibleColumns", (value ?? "").Trim());

    // =========================
    // ✅ Libellés UI (noms affichés) — par projet
    // =========================
    private static string MakeLabelKey(string field, long projectId) => $"Label.{field}.P{projectId}";
    private static string MakeLabelKeyGlobal(string field) => $"Label.{field}";

    public static string GetLabel(long projectId, string field, string defaultValue)
    {
        field = (field ?? "").Trim();
        if (string.IsNullOrWhiteSpace(field))
            return defaultValue;

        // 1) Par projet
        var perProject = GetSetting(MakeLabelKey(field, projectId));
        if (!string.IsNullOrWhiteSpace(perProject))
            return perProject.Trim();

        // 2) Global (fallback)
        var global = GetSetting(MakeLabelKeyGlobal(field));
        if (!string.IsNullOrWhiteSpace(global))
            return global.Trim();

        return defaultValue;
    }

    public static void SetLabel(long projectId, string field, string value)
    {
        field = (field ?? "").Trim();
        if (string.IsNullOrWhiteSpace(field))
            return;

        value = (value ?? "").Trim();
        SetSetting(MakeLabelKey(field, projectId), value);
    }

    // Champs "Demande" (libellés par défaut demandés)
    public static string GetLabelReserve(long projectId) => GetLabel(projectId, "Reserve", "Concerne");
    public static void SetLabelReserve(long projectId, string value) => SetLabel(projectId, "Reserve", value);

    public static string GetLabelRequestedBy(long projectId) => GetLabel(projectId, "RequestedBy", "Demandé par");
    public static void SetLabelRequestedBy(long projectId, string value) => SetLabel(projectId, "RequestedBy", value);

    public static string GetLabelPerformedBy(long projectId) => GetLabel(projectId, "PerformedBy", "Entreprise");
    public static void SetLabelPerformedBy(long projectId, string value) => SetLabel(projectId, "PerformedBy", value);

    public static string GetLabelPlace(long projectId) => GetLabel(projectId, "Place", "Bâtiment");
    public static void SetLabelPlace(long projectId, string value) => SetLabel(projectId, "Place", value);

    public static string GetLabelEtage(long projectId) => GetLabel(projectId, "Etage", "Étage");
    public static void SetLabelEtage(long projectId, string value) => SetLabel(projectId, "Etage", value);

    public static string GetLabelDeadline(long projectId) => GetLabel(projectId, "DeadlineDate", "Délai");
    public static void SetLabelDeadline(long projectId, string value) => SetLabel(projectId, "DeadlineDate", value);

    // ✅ NOUVEAU : Libellé pour la liste "Zone de texte planning" (page Listes)
    public static string GetLabelPlanningTextZone(long projectId) => GetLabel(projectId, "PlanningTextZone", "Zone de texte planning");
    public static void SetLabelPlanningTextZone(long projectId, string value) => SetLabel(projectId, "PlanningTextZone", value);

    // ✅ NOUVEAU : Libellés pour les listes "Cat." et "Urg." (page Listes)
    public static string GetLabelTaskCategory(long projectId) => GetLabel(projectId, "TaskCategory", "Cat.");
    public static void SetLabelTaskCategory(long projectId, string value) => SetLabel(projectId, "TaskCategory", value);

    public static string GetLabelTaskUrgency(long projectId) => GetLabel(projectId, "TaskUrgency", "Urg.");
    public static void SetLabelTaskUrgency(long projectId, string value) => SetLabel(projectId, "TaskUrgency", value);

    // =========================
    // ✅ Liste "Zone de texte planning" — par projet
    // =========================
    public static List<string> GetPlanningTextZones(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM PlanningTextZones WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertPlanningTextZone(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO PlanningTextZones (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void DeletePlanningTextZone(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM PlanningTextZones WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenamePlanningTextZone(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM PlanningTextZones WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Zone de texte planning » (pour ce dossier).");

        con.Execute(
            "UPDATE PlanningTextZones SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    // =========================
    // ✅ Liste "Cat." des tâches de Planning — par projet
    // =========================
    public static List<string> GetTaskCategories(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM TaskCategories WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertTaskCategory(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO TaskCategories (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void DeleteTaskCategory(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM TaskCategories WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );

        // ✅ supprime aussi la couleur associée
        DeleteTaskCategoryColor(projectId, name);
    }

    public static void RenameTaskCategory(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM TaskCategories WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Cat. » (pour ce dossier).");

        con.Execute(
            "UPDATE TaskCategories SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();

        // ✅ renommer aussi la couleur associée
        RenameTaskCategoryColor(projectId, oldName, newName);
    }

    // =========================
    // ✅ Liste "Urg." des tâches de Planning — par projet
    // =========================
    public static List<string> GetTaskUrgencies(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM TaskUrgencies WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertTaskUrgency(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO TaskUrgencies (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void DeleteTaskUrgency(long projectId, string name)
    {
        name = (name ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM TaskUrgencies WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenameTaskUrgency(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM TaskUrgencies WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Urg. » (pour ce dossier).");

        con.Execute(
            "UPDATE TaskUrgencies SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    // =========================
    // ✅ Couleurs Entreprises — par projet
    // =========================
    public static string? GetCompanyColorHex(long projectId, string companyName)
    {
        companyName = (companyName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(companyName))
            return null;

        using var con = Open();
        con.Open();

        return con.ExecuteScalar<string?>(
            "SELECT ColorHex FROM CompanyColors WHERE ProjectId=@ProjectId AND CompanyName=@CompanyName;",
            new { ProjectId = projectId, CompanyName = companyName }
        );
    }

    public static void SetCompanyColorHex(long projectId, string companyName, string colorHex)
    {
        companyName = (companyName ?? "").Trim();
        colorHex = (colorHex ?? "").Trim();

        if (projectId <= 0 || string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(colorHex))
            return;

        using var con = Open();
        con.Open();

        con.Execute("""
            INSERT INTO CompanyColors(ProjectId, CompanyName, ColorHex)
            VALUES (@ProjectId, @CompanyName, @ColorHex)
            ON CONFLICT(ProjectId, CompanyName) DO UPDATE SET ColorHex=excluded.ColorHex;
        """, new { ProjectId = projectId, CompanyName = companyName, ColorHex = colorHex });
    }

    public static void DeleteCompanyColor(long projectId, string companyName)
    {
        companyName = (companyName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(companyName))
            return;

        using var con = Open();
        con.Open();

        con.Execute(
            "DELETE FROM CompanyColors WHERE ProjectId=@ProjectId AND CompanyName=@CompanyName;",
            new { ProjectId = projectId, CompanyName = companyName }
        );
    }

    public static void RenameCompanyColor(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();

        // si une couleur existe déjà pour le nouveau nom, on supprime l'ancienne
        var existsNew = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM CompanyColors WHERE ProjectId=@ProjectId AND CompanyName=@CompanyName;",
            new { ProjectId = projectId, CompanyName = newName }
        );

        if (existsNew > 0)
        {
            con.Execute(
                "DELETE FROM CompanyColors WHERE ProjectId=@ProjectId AND CompanyName=@OldName;",
                new { ProjectId = projectId, OldName = oldName }
            );
            return;
        }

        con.Execute("""
            UPDATE CompanyColors
            SET CompanyName=@NewName
            WHERE ProjectId=@ProjectId AND CompanyName=@OldName;
        """, new { ProjectId = projectId, OldName = oldName, NewName = newName });
    }

    public static Dictionary<string, string> GetCompanyColorMap(long projectId)
    {
        if (projectId <= 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var con = Open();
        con.Open();

        var rows = con.Query<(string CompanyName, string ColorHex)>(
            "SELECT CompanyName, ColorHex FROM CompanyColors WHERE ProjectId=@ProjectId;",
            new { ProjectId = projectId }
        ).ToList();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var name = (r.CompanyName ?? "").Trim();
            var hex = (r.ColorHex ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(hex))
                dict[name] = hex;
        }

        return dict;
    }

    // =========================
    // ✅ Couleurs Catégories de tâches — par projet (indépendant des couleurs
    // d'entreprises, demande de Joe, 17.07.2026 : même mécanisme, en miroir exact).
    // =========================
    public static string? GetTaskCategoryColorHex(long projectId, string categoryName)
    {
        categoryName = (categoryName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(categoryName))
            return null;

        using var con = Open();
        con.Open();

        return con.ExecuteScalar<string?>(
            "SELECT ColorHex FROM TaskCategoryColors WHERE ProjectId=@ProjectId AND CategoryName=@CategoryName;",
            new { ProjectId = projectId, CategoryName = categoryName }
        );
    }

    public static void SetTaskCategoryColorHex(long projectId, string categoryName, string colorHex)
    {
        categoryName = (categoryName ?? "").Trim();
        colorHex = (colorHex ?? "").Trim();

        if (projectId <= 0 || string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(colorHex))
            return;

        using var con = Open();
        con.Open();

        con.Execute("""
            INSERT INTO TaskCategoryColors(ProjectId, CategoryName, ColorHex)
            VALUES (@ProjectId, @CategoryName, @ColorHex)
            ON CONFLICT(ProjectId, CategoryName) DO UPDATE SET ColorHex=excluded.ColorHex;
        """, new { ProjectId = projectId, CategoryName = categoryName, ColorHex = colorHex });
    }

    public static void DeleteTaskCategoryColor(long projectId, string categoryName)
    {
        categoryName = (categoryName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(categoryName))
            return;

        using var con = Open();
        con.Open();

        con.Execute(
            "DELETE FROM TaskCategoryColors WHERE ProjectId=@ProjectId AND CategoryName=@CategoryName;",
            new { ProjectId = projectId, CategoryName = categoryName }
        );
    }

    public static void RenameTaskCategoryColor(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (projectId <= 0 || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();

        // si une couleur existe déjà pour le nouveau nom, on supprime l'ancienne
        var existsNew = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM TaskCategoryColors WHERE ProjectId=@ProjectId AND CategoryName=@CategoryName;",
            new { ProjectId = projectId, CategoryName = newName }
        );

        if (existsNew > 0)
        {
            con.Execute(
                "DELETE FROM TaskCategoryColors WHERE ProjectId=@ProjectId AND CategoryName=@OldName;",
                new { ProjectId = projectId, OldName = oldName }
            );
            return;
        }

        con.Execute("""
            UPDATE TaskCategoryColors
            SET CategoryName=@NewName
            WHERE ProjectId=@ProjectId AND CategoryName=@OldName;
        """, new { ProjectId = projectId, OldName = oldName, NewName = newName });
    }

    public static Dictionary<string, string> GetTaskCategoryColorMap(long projectId)
    {
        if (projectId <= 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var con = Open();
        con.Open();

        var rows = con.Query<(string CategoryName, string ColorHex)>(
            "SELECT CategoryName, ColorHex FROM TaskCategoryColors WHERE ProjectId=@ProjectId;",
            new { ProjectId = projectId }
        ).ToList();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var name = (r.CategoryName ?? "").Trim();
            var hex = (r.ColorHex ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(hex))
                dict[name] = hex;
        }

        return dict;
    }

    // =========================
    // Dashboard identity
    // =========================
    public static string GetArchitectName() => GetSetting("ArchitectName") ?? "";
    public static void SetArchitectName(string value) => SetSetting("ArchitectName", (value ?? "").Trim());

    // ✅ "Réf." : nom + éventuellement téléphone du responsable, affiché à côté de "Bureau"
    public static string GetArchitectRef() => GetSetting("ArchitectRef") ?? "";
    public static void SetArchitectRef(string value) => SetSetting("ArchitectRef", (value ?? "").Trim());

    // ✅ Champ libre 2 (sous le champ libre 1), affiché à la suite dans les bons/PDF
    public static string GetArchitectRef2() => GetSetting("ArchitectRef2") ?? "";
    public static void SetArchitectRef2(string value) => SetSetting("ArchitectRef2", (value ?? "").Trim());

    public static string GetArchitectAddress() => GetSetting("ArchitectAddress") ?? "";
    public static void SetArchitectAddress(string value) => SetSetting("ArchitectAddress", (value ?? "").Trim());
    public static string GetArchitectAddressLine() => GetSetting("ArchitectAddressLine") ?? "";
    public static void SetArchitectAddressLine(string value) => SetSetting("ArchitectAddressLine", (value ?? "").Trim());

    public static string GetArchitectZipCity() => GetSetting("ArchitectZipCity") ?? "";
    public static void SetArchitectZipCity(string value) => SetSetting("ArchitectZipCity", (value ?? "").Trim());

    public static string GetArchitectWebsite() => GetSetting("ArchitectWebsite") ?? "";
    public static void SetArchitectWebsite(string value) => SetSetting("ArchitectWebsite", (value ?? "").Trim());

    public static string GetArchitectLogoPath() => GetSetting("ArchitectLogoPath") ?? "";
    public static void SetArchitectLogoPath(string value) => SetSetting("ArchitectLogoPath", (value ?? "").Trim());

    public static long? GetCurrentProjectId()
    {
        var s = GetSetting("CurrentProjectId");
        return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    public static void SetCurrentProjectId(long? projectId)
    {
        SetSetting("CurrentProjectId", projectId.HasValue ? projectId.Value.ToString(CultureInfo.InvariantCulture) : "");
    }

    // =========================
    // Numérotation BDR (par projet)
    // =========================
    public static int GetNextBdrNumberForProject(long projectId)
    {
        if (projectId <= 0)
            return GetNextBdrNumber();

        using var con = Open();
        con.Open();
        return con.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(BdrNumber), 0) + 1 FROM WorkOrders WHERE ProjectId=@Id;",
            new { Id = projectId }
        );
    }

    public static int GetNextBdrNumber()
    {
        using var con = Open();
        con.Open();
        return con.ExecuteScalar<int>("SELECT COALESCE(MAX(BdrNumber), 0) + 1 FROM WorkOrders;");
    }

    // =========================
    // WorkOrders
    // =========================
    public static List<WorkOrder> GetWorkOrders()
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 0
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetWorkOrders(long projectId)
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 0
              AND ProjectId = @Id
            ORDER BY Id DESC;
        """, new { Id = projectId }).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetTrashedWorkOrders()
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 1
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetTrashedWorkOrders(long projectId)
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 1
              AND ProjectId = @Id
            ORDER BY Id DESC;
        """, new { Id = projectId }).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetArchivedWorkOrders()
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 1
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetArchivedWorkOrders(long projectId)
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 1
              AND ProjectId = @Id
            ORDER BY Id DESC;
        """, new { Id = projectId }).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetWorkOrdersForAccounting()
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsCancelled, 0) = 0
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetWorkOrdersForAccounting(long projectId)
    {
        using var con = Open();
        con.Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsCancelled, 0) = 0
              AND ProjectId = @Id
            ORDER BY Id DESC;
        """, new { Id = projectId }).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static WorkOrder? GetWorkOrderById(long id)
    {
        using var con = Open();
        con.Open();

        var row = con.QueryFirstOrDefault("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE Id=@Id;
        """, new { Id = id });

        if (row == null) return null;
        return MapWorkOrderRow(row);
    }

    // =========================
    // ✅ Import packages (Option A + fallback par numéro)
    // =========================
    public static WorkOrder? GetImportedWorkOrderBySourceId(long sourceWorkOrderId)
    {
        if (sourceWorkOrderId <= 0)
            return null;

        using var con = Open();
        con.Open();

        var row = con.QueryFirstOrDefault("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE ImportedFromWorkOrderId=@SourceId
            ORDER BY Id DESC
            LIMIT 1;
        """, new { SourceId = sourceWorkOrderId });

        if (row == null) return null;
        return MapWorkOrderRow(row);
    }

    private static WorkOrder? GetWorkOrderByProjectAndBdrNumber(long projectId, int bdrNumber)
    {
        if (projectId <= 0 || bdrNumber <= 0)
            return null;

        using var con = Open();
        con.Open();

        var row = con.QueryFirstOrDefault("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent, IsPerformed, IsCancelled,
                IsTrashed, TrashedAt, IsArchived, ArchivedAt,
                Description,
                QuoteName, QuoteDate,
                DeadlineDate,
                DistributedAt, PerformedAt, CompanyLinkSentAt, SignerLinkSentAt,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, SignatureName, SignatureDate, SignaturePng,
                Reserve
            FROM WorkOrders
            WHERE ProjectId=@ProjectId
              AND BdrNumber=@BdrNumber
              AND COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 0
            ORDER BY Id DESC
            LIMIT 1;
        """, new { ProjectId = projectId, BdrNumber = bdrNumber });

        if (row == null) return null;
        return MapWorkOrderRow(row);
    }

    private static bool WorkOrderNumberExistsInProject(long projectId, int bdrNumber)
    {
        if (projectId <= 0 || bdrNumber <= 0)
            return false;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM WorkOrders WHERE ProjectId=@ProjectId AND BdrNumber=@BdrNumber;",
            new { ProjectId = projectId, BdrNumber = bdrNumber }
        );

        return count > 0;
    }

    private static void UpdateImportedWorkOrderRow(long existingId, WorkOrder imported, long sourceWorkOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET Place=@Place,
                Etage=@Etage,
                RequestedBy=@RequestedBy,
                PerformedBy=@PerformedBy,
                RequestDate=@RequestDate,
                Description=@Description,
                Reserve=@Reserve,

                QuoteName=@QuoteName,
                QuoteDate=@QuoteDate,
                DeadlineDate=@DeadlineDate,
                LaborHours=@LaborHours,
                LaborRate=@LaborRate,
                TravelQty=@TravelQty,
                TravelRate=@TravelRate,
                TvaRate=@TvaRate,
                DiscountRate=@DiscountRate,
                ForfaitQty=@ForfaitQty,
                ForfaitUnitPrice=@ForfaitUnitPrice,
                ForfaitPdfFileName=@ForfaitPdfFileName,
                ForfaitPdfFileBytes=@ForfaitPdfFileBytes,
                QuoteNotes=@QuoteNotes,

                SignatureName=@SignatureName,
                SignatureDate=@SignatureDate,
                SignaturePng=@SignaturePng,

                ImportedFromWorkOrderId=@ImportedFromWorkOrderId,
                ImportedAtUtc=@ImportedAtUtc,

                ValidationDecision=@ValidationDecision
            WHERE Id=@Id;
        """, new
        {
            Id = existingId,

            Place = (imported.Place ?? "").Trim(),
            Etage = (imported.Etage ?? "").Trim(),
            RequestedBy = (imported.RequestedBy ?? "").Trim(),
            PerformedBy = (imported.PerformedBy ?? "").Trim(),
            RequestDate = imported.RequestDate == default
                ? DateTime.Today.ToString("yyyy-MM-dd")
                : imported.RequestDate.ToString("yyyy-MM-dd"),

            Description = imported.Description ?? "",
            Reserve = (imported.Reserve ?? "").Trim(),

            QuoteName = (imported.QuoteName ?? "").Trim(),
            QuoteDate = imported.QuoteDate.ToString("yyyy-MM-dd"),
            DeadlineDate = imported.DeadlineDate.ToString("yyyy-MM-dd"),

            imported.LaborHours,
            imported.LaborRate,
            imported.TravelQty,
            imported.TravelRate,
            imported.TvaRate,

            DiscountRate = imported.DiscountRate,

            ForfaitQty = imported.ForfaitQty,
            ForfaitUnitPrice = imported.ForfaitUnitPrice,
            ForfaitPdfFileName = (imported.ForfaitPdfFileName ?? "").Trim(),
            ForfaitPdfFileBytes = imported.ForfaitPdfFileBytes,

            QuoteNotes = imported.QuoteNotes ?? "",

            SignatureName = imported.SignatureName ?? "",
            SignatureDate = imported.SignatureDate?.ToString("yyyy-MM-dd"),
            SignaturePng = imported.SignaturePng,

            ImportedFromWorkOrderId = sourceWorkOrderId > 0 ? sourceWorkOrderId : (long?)null,
            ImportedAtUtc = DateTime.UtcNow.ToString("o"),

            ValidationDecision = (imported.ValidationDecision ?? "").Trim()
        });
    }

    public static long UpsertImportedWorkOrder_OptionA(WorkOrder imported, long sourceWorkOrderId)
    {
        if (imported == null)
            throw new ArgumentNullException(nameof(imported));

        WorkOrder? existing = null;
        if (sourceWorkOrderId > 0)
            existing = GetImportedWorkOrderBySourceId(sourceWorkOrderId);

        if (existing == null)
        {
            var pid = imported.ProjectId;
            if (!pid.HasValue || pid.Value <= 0)
                pid = GetCurrentProjectId();

            if (pid.HasValue && pid.Value > 0 && imported.BdrNumber > 0)
                existing = GetWorkOrderByProjectAndBdrNumber(pid.Value, imported.BdrNumber);
        }

        if (existing != null)
        {
            if (existing.IsValidated)
                throw new InvalidOperationException("Ce bon est déjà validé. Crée un nouveau bon pour refaire le processus.");

            UpdateImportedWorkOrderRow(existing.Id, imported, sourceWorkOrderId);
            return existing.Id;
        }

        var projectId = imported.ProjectId;
        if (!projectId.HasValue || projectId.Value <= 0)
            projectId = GetCurrentProjectId();

        if (!projectId.HasValue || projectId.Value <= 0)
            throw new InvalidOperationException("Aucun dossier courant pour importer le bon.");

        var wanted = imported.BdrNumber;
        int bdrLocal;

        if (wanted > 0 && !WorkOrderNumberExistsInProject(projectId.Value, wanted))
            bdrLocal = wanted;
        else
            bdrLocal = GetNextBdrNumberForProject(projectId.Value);

        using var con = Open();
        con.Open();

        con.Execute("""
            INSERT INTO WorkOrders (
                ProjectId, BdrNumber,
                Place, Etage, RequestedBy, PerformedBy, RequestDate,
                Description, Reserve,
                QuoteName, QuoteDate,
                DeadlineDate,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate, DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent,
                IsPerformed, IsCancelled,
                IsTrashed, TrashedAt,
                IsArchived, ArchivedAt,
                ImportedFromWorkOrderId, ImportedAtUtc
            )
            VALUES (
                @ProjectId, @BdrNumber,
                @Place, @Etage, @RequestedBy, @PerformedBy, @RequestDate,
                @Description, @Reserve,
                @QuoteName, @QuoteDate,
                @DeadlineDate,
                @LaborHours, @LaborRate, @TravelQty, @TravelRate, @TvaRate, @DiscountRate,
                @ForfaitQty, @ForfaitUnitPrice, @ForfaitPdfFileName, @ForfaitPdfFileBytes,
                @QuoteNotes,
                @SignatureName, @SignatureDate, @SignaturePng,
                0, 0, 0, 0,
                0, @ValidationDecision, 0,
                0, 0,
                0, NULL,
                0, NULL,
                @ImportedFromWorkOrderId, @ImportedAtUtc
            );
        """, new
        {
            ProjectId = projectId.Value,
            BdrNumber = bdrLocal,

            Place = (imported.Place ?? "").Trim(),
            Etage = (imported.Etage ?? "").Trim(),
            RequestedBy = (imported.RequestedBy ?? "").Trim(),
            PerformedBy = (imported.PerformedBy ?? "").Trim(),
            RequestDate = imported.RequestDate == default
                ? DateTime.Today.ToString("yyyy-MM-dd")
                : imported.RequestDate.ToString("yyyy-MM-dd"),

            Description = imported.Description ?? "",
            Reserve = (imported.Reserve ?? "").Trim(),

            QuoteName = (imported.QuoteName ?? "").Trim(),
            QuoteDate = imported.QuoteDate.ToString("yyyy-MM-dd"),
            DeadlineDate = imported.DeadlineDate.ToString("yyyy-MM-dd"),

            imported.LaborHours,
            imported.LaborRate,
            imported.TravelQty,
            imported.TravelRate,
            imported.TvaRate,

            DiscountRate = imported.DiscountRate,

            ForfaitQty = imported.ForfaitQty,
            ForfaitUnitPrice = imported.ForfaitUnitPrice,
            ForfaitPdfFileName = (imported.ForfaitPdfFileName ?? "").Trim(),
            ForfaitPdfFileBytes = imported.ForfaitPdfFileBytes,

            QuoteNotes = imported.QuoteNotes ?? "",

            SignatureName = imported.SignatureName ?? "",
            SignatureDate = imported.SignatureDate?.ToString("yyyy-MM-dd"),
            SignaturePng = imported.SignaturePng,

            ImportedFromWorkOrderId = sourceWorkOrderId > 0 ? sourceWorkOrderId : (long?)null,
            ImportedAtUtc = DateTime.UtcNow.ToString("o"),

            ValidationDecision = (imported.ValidationDecision ?? "").Trim()
        });

        return con.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    public static void ReplaceWorkOrderLines(long workOrderId, List<WorkOrderLine> lines)
    {
        if (workOrderId <= 0)
            throw new ArgumentException("WorkOrderId invalide.", nameof(workOrderId));

        lines ??= new List<WorkOrderLine>();

        using var con = Open();
        con.Open();

        using var tx = con.BeginTransaction();

        con.Execute("DELETE FROM WorkOrderLines WHERE WorkOrderId=@Id;", new { Id = workOrderId }, tx);

        foreach (var l in lines)
        {
            var label = (l.Label ?? "").Trim();
            var qty = l.Qty;
            var unit = l.UnitPrice;

            var isEmpty =
                string.IsNullOrWhiteSpace(label)
                && Math.Abs(qty) < 0.0000000001
                && Math.Abs(unit) < 0.0000000001;

            if (isEmpty)
                continue;

            var total = Math.Round(qty * unit, 2);

            con.Execute("""
                INSERT INTO WorkOrderLines (WorkOrderId, Label, Qty, UnitPrice, LineTotal)
                VALUES (@WorkOrderId, @Label, @Qty, @UnitPrice, @LineTotal);
            """, new
            {
                WorkOrderId = workOrderId,
                Label = label,
                Qty = qty,
                UnitPrice = unit,
                LineTotal = total
            }, tx);
        }

        tx.Commit();
    }

    private static WorkOrder MapWorkOrderRow(dynamic row)
    {
        long? projectId = row.ProjectId == null || row.ProjectId is DBNull ? null : AsLong(row.ProjectId);

        var sigDateStr = AsString(row.SignatureDate, "");
        DateTime? sigDate = AsNullableDate(sigDateStr);

        byte[]? sigPng = null;
        try { sigPng = row.SignaturePng as byte[]; } catch { }

        var quoteDateStr = AsString(row.QuoteDate, "");
        var quoteDate = string.IsNullOrWhiteSpace(quoteDateStr) ? DateTime.Today : AsDate(quoteDateStr, DateTime.Today);

        var deadlineDateStr = AsString(row.DeadlineDate, "");
        var deadlineDate = string.IsNullOrWhiteSpace(deadlineDateStr) ? DateTime.Today : AsDate(deadlineDateStr, DateTime.Today);

        var trashedAtStr = AsString(row.TrashedAt, "");
        DateTime? trashedAt = AsNullableDate(trashedAtStr);

        var archivedAtStr = AsString(row.ArchivedAt, "");
        DateTime? archivedAt = AsNullableDate(archivedAtStr);

        var distributedAtStr = AsString(row.DistributedAt, "");
        DateTime? distributedAt = AsNullableDate(distributedAtStr);

        var performedAtStr = AsString(row.PerformedAt, "");
        DateTime? performedAt = AsNullableDate(performedAtStr);

        var companyLinkSentAtStr = AsString(row.CompanyLinkSentAt, "");
        DateTime? companyLinkSentAt = AsNullableDate(companyLinkSentAtStr);

        var signerLinkSentAtStr = AsString(row.SignerLinkSentAt, "");
        DateTime? signerLinkSentAt = AsNullableDate(signerLinkSentAtStr);

        byte[]? forfaitPdfBytes = null;
        try { forfaitPdfBytes = row.ForfaitPdfFileBytes as byte[]; } catch { }

        return new WorkOrder
        {
            Id = AsLong(row.Id),
            ProjectId = projectId,
            BdrNumber = AsInt(row.BdrNumber),

            Place = AsString(row.Place),
            Etage = AsString(row.Etage),
            RequestedBy = AsString(row.RequestedBy),
            PerformedBy = AsString(row.PerformedBy),
            RequestDate = AsDate(AsString(row.RequestDate), DateTime.Today),

            IsInCreation = AsBool01(row.IsInCreation),
            IsSentToCompany = AsBool01(row.IsSentToCompany),
            IsQuoteReceived = AsBool01(row.IsQuoteReceived),
            IsSentToSigner = AsBool01(row.IsSentToSigner),
            IsValidated = AsBool01(row.IsValidated),
            ValidationDecision = AsString(row.ValidationDecision),
            IsValidatedPdfSent = AsBool01(row.IsValidatedPdfSent),

            IsPerformed = AsBool01(row.IsPerformed),
            IsCancelled = AsBool01(row.IsCancelled),

            DistributedAt = distributedAt,
            PerformedAt = performedAt,

            CompanyLinkSentAt = companyLinkSentAt,
            SignerLinkSentAt = signerLinkSentAt,

            IsTrashed = AsBool01(row.IsTrashed),
            TrashedAt = trashedAt,

            IsArchived = AsBool01(row.IsArchived),
            ArchivedAt = archivedAt,

            Description = AsString(row.Description),

            QuoteName = AsString(row.QuoteName),
            QuoteDate = quoteDate,

            DeadlineDate = deadlineDate,

            LaborHours = AsDouble(row.LaborHours),
            LaborRate = AsDouble(row.LaborRate),
            TravelQty = AsDouble(row.TravelQty),
            TravelRate = AsDouble(row.TravelRate),
            TvaRate = AsDouble(row.TvaRate, 8.1),

            DiscountRate = AsDouble(row.DiscountRate, 0),

            ForfaitQty = AsDouble(row.ForfaitQty, 0),
            ForfaitUnitPrice = AsDouble(row.ForfaitUnitPrice, 0),
            ForfaitPdfFileName = AsString(row.ForfaitPdfFileName, ""),
            ForfaitPdfFileBytes = forfaitPdfBytes,

            QuoteNotes = AsString(row.QuoteNotes),

            SignatureName = AsString(row.SignatureName),
            SignatureDate = sigDate,
            SignaturePng = sigPng,

            Reserve = AsString(row.Reserve)
        };
    }

    // ✅ Dates indépendantes (dashboard)
    public static void SetDistributedAt(long workOrderId, DateTime? date)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET DistributedAt=@D
            WHERE Id=@Id;
        """, new
        {
            Id = workOrderId,
            D = date.HasValue ? date.Value.ToString("yyyy-MM-dd") : null
        });
    }

    public static void SetPerformedAt(long workOrderId, DateTime? date)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET PerformedAt=@D
            WHERE Id=@Id;
        """, new
        {
            Id = workOrderId,
            D = date.HasValue ? date.Value.ToString("yyyy-MM-dd") : null
        });
    }

    public static void InsertWorkOrder(WorkOrder wo)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            INSERT INTO WorkOrders (
                BdrNumber, Place, Etage, RequestedBy, PerformedBy, RequestDate,
                IsInCreation, IsSentToCompany, IsQuoteReceived, IsSentToSigner,
                IsValidated, ValidationDecision, IsValidatedPdfSent,
                IsPerformed, Description, IsCancelled,
                IsTrashed, TrashedAt,
                IsArchived, ArchivedAt,
                QuoteName, QuoteDate,
                DeadlineDate,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate, DiscountRate,
                ForfaitQty, ForfaitUnitPrice, ForfaitPdfFileName, ForfaitPdfFileBytes,
                QuoteNotes, ProjectId,
                SignatureName, SignatureDate, SignaturePng,
                Reserve,
                DistributedAt, PerformedAt
            )
            VALUES (
                @BdrNumber, @Place, @Etage, @RequestedBy, @PerformedBy, @RequestDate,
                @IsInCreation, @IsSentToCompany, @IsQuoteReceived, @IsSentToSigner,
                @IsValidated, @ValidationDecision, @IsValidatedPdfSent,
                @IsPerformed, @Description, @IsCancelled,
                @IsTrashed, @TrashedAt,
                @IsArchived, @ArchivedAt,
                @QuoteName, @QuoteDate,
                @DeadlineDate,
                @LaborHours, @LaborRate, @TravelQty, @TravelRate, @TvaRate, @DiscountRate,
                @ForfaitQty, @ForfaitUnitPrice, @ForfaitPdfFileName, @ForfaitPdfFileBytes,
                @QuoteNotes, @ProjectId,
                @SignatureName, @SignatureDate, @SignaturePng,
                @Reserve,
                @DistributedAt, @PerformedAt
            );
        """, new
        {
            wo.BdrNumber,
            Place = wo.Place ?? "",
            Etage = wo.Etage ?? "",
            RequestedBy = wo.RequestedBy ?? "",
            PerformedBy = wo.PerformedBy ?? "",
            RequestDate = wo.RequestDate.ToString("yyyy-MM-dd"),

            IsInCreation = wo.IsInCreation ? 1 : 0,
            IsSentToCompany = wo.IsSentToCompany ? 1 : 0,
            IsQuoteReceived = wo.IsQuoteReceived ? 1 : 0,
            IsSentToSigner = wo.IsSentToSigner ? 1 : 0,
            IsValidated = wo.IsValidated ? 1 : 0,
            ValidationDecision = (wo.ValidationDecision ?? "").Trim(),
            IsValidatedPdfSent = wo.IsValidatedPdfSent ? 1 : 0,

            IsPerformed = wo.IsPerformed ? 1 : 0,
            Description = wo.Description ?? "",
            IsCancelled = wo.IsCancelled ? 1 : 0,

            IsTrashed = wo.IsTrashed ? 1 : 0,
            TrashedAt = wo.TrashedAt?.ToString("yyyy-MM-dd"),

            IsArchived = wo.IsArchived ? 1 : 0,
            ArchivedAt = wo.ArchivedAt?.ToString("yyyy-MM-dd"),

            QuoteName = (wo.QuoteName ?? "").Trim(),
            QuoteDate = wo.QuoteDate.ToString("yyyy-MM-dd"),

            DeadlineDate = wo.DeadlineDate.ToString("yyyy-MM-dd"),

            wo.LaborHours,
            wo.LaborRate,
            wo.TravelQty,
            wo.TravelRate,
            wo.TvaRate,

            DiscountRate = wo.DiscountRate,

            ForfaitQty = wo.ForfaitQty,
            ForfaitUnitPrice = wo.ForfaitUnitPrice,
            ForfaitPdfFileName = (wo.ForfaitPdfFileName ?? "").Trim(),
            ForfaitPdfFileBytes = wo.ForfaitPdfFileBytes,

            QuoteNotes = wo.QuoteNotes ?? "",
            wo.ProjectId,

            SignatureName = wo.SignatureName ?? "",
            SignatureDate = wo.SignatureDate?.ToString("yyyy-MM-dd"),
            SignaturePng = wo.SignaturePng,

            Reserve = (wo.Reserve ?? "").Trim(),

            DistributedAt = wo.DistributedAt?.ToString("yyyy-MM-dd"),
            PerformedAt = wo.PerformedAt?.ToString("yyyy-MM-dd")
        });

        // ✅ FIX CRITIQUE :
        // Sans ça, wo.Id reste à 0 => les transitions (SetStage...) font UPDATE Id=0 => rien ne se passe.
        wo.Id = con.ExecuteScalar<long>("SELECT last_insert_rowid();");

        // ✅ Optionnel mais utile : si le workflow attend que "Créé" soit le défaut,
        // on force le stage "Création" quand aucun stage n’est déjà défini.
        if (!wo.IsInCreation && !wo.IsSentToCompany && !wo.IsQuoteReceived && !wo.IsSentToSigner && !wo.IsValidated)
        {
            SetStageInCreation(wo.Id);
            wo.IsInCreation = true;
        }
    }

    public static void UpdateWorkOrderHeader(WorkOrder wo)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET Place=@Place,
                Etage=@Etage,
                RequestedBy=@RequestedBy,
                PerformedBy=@PerformedBy,
                RequestDate=@RequestDate,
                Description=@Description,
                ProjectId=@ProjectId,
                Reserve=@Reserve,
                DeadlineDate=@DeadlineDate
            WHERE Id=@Id;
        """, new
        {
            wo.Id,
            Place = wo.Place ?? "",
            Etage = wo.Etage ?? "",
            RequestedBy = wo.RequestedBy ?? "",
            PerformedBy = wo.PerformedBy ?? "",
            RequestDate = wo.RequestDate.ToString("yyyy-MM-dd"),
            Description = wo.Description ?? "",
            wo.ProjectId,
            Reserve = (wo.Reserve ?? "").Trim(),
            DeadlineDate = wo.DeadlineDate.ToString("yyyy-MM-dd")
        });
    }

    public static void UpdateWorkOrderQuote(WorkOrder wo)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET QuoteName=@QuoteName,
                QuoteDate=@QuoteDate,
                LaborHours=@LaborHours,
                LaborRate=@LaborRate,
                TravelQty=@TravelQty,
                TravelRate=@TravelRate,
                TvaRate=@TvaRate,
                DiscountRate=@DiscountRate,
                ForfaitQty=@ForfaitQty,
                ForfaitUnitPrice=@ForfaitUnitPrice,
                ForfaitPdfFileName=@ForfaitPdfFileName,
                ForfaitPdfFileBytes=@ForfaitPdfFileBytes,
                QuoteNotes=@QuoteNotes
            WHERE Id=@Id;
        """, new
        {
            wo.Id,
            QuoteName = (wo.QuoteName ?? "").Trim(),
            QuoteDate = wo.QuoteDate.ToString("yyyy-MM-dd"),
            wo.LaborHours,
            wo.LaborRate,
            wo.TravelQty,
            wo.TravelRate,
            wo.TvaRate,
            DiscountRate = wo.DiscountRate,

            ForfaitQty = wo.ForfaitQty,
            ForfaitUnitPrice = wo.ForfaitUnitPrice,
            ForfaitPdfFileName = (wo.ForfaitPdfFileName ?? "").Trim(),
            ForfaitPdfFileBytes = wo.ForfaitPdfFileBytes,

            QuoteNotes = wo.QuoteNotes ?? ""
        });
    }

    public static void UpdateWorkOrderSignatureRaw(WorkOrder wo)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET SignatureName=@SignatureName,
                SignatureDate=@SignatureDate,
                SignaturePng=@SignaturePng
            WHERE Id=@Id;
        """, new
        {
            wo.Id,
            SignatureName = wo.SignatureName ?? "",
            SignatureDate = wo.SignatureDate?.ToString("yyyy-MM-dd"),
            SignaturePng = wo.SignaturePng
        });
    }

    // ✅ NOUVEAU : persister la décision (Validé / Refusé / Annulé)
    public static void UpdateWorkOrderValidationDecision(long workOrderId, string? decision)
    {
        decision = (decision ?? "").Trim();

        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET ValidationDecision=@D
            WHERE Id=@Id;
        """, new { Id = workOrderId, D = decision });
    }

    // ✅ NOUVEAU : PDF devis forfaitaire (lecture seule)
    public static void UpdateWorkOrderForfaitPdf(long workOrderId, string? fileName, byte[]? pdfBytes)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET ForfaitPdfFileName=@N,
                ForfaitPdfFileBytes=@B
            WHERE Id=@Id;
        """, new
        {
            Id = workOrderId,
            N = (fileName ?? "").Trim(),
            B = pdfBytes
        });
    }

    public static void SetStageInCreation(long workOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=1,
                IsSentToCompany=0,
                IsQuoteReceived=0,
                IsSentToSigner=0,
                IsValidated=0
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageSentToCompany(long workOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=1,
                IsQuoteReceived=0,
                IsSentToSigner=0,
                IsValidated=0,
                CompanyLinkSentAt=datetime('now')
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageQuoteReceived(long workOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=0,
                IsQuoteReceived=1,
                IsSentToSigner=0,
                IsValidated=0
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageSentToSigner(long workOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=0,
                IsQuoteReceived=0,
                IsSentToSigner=1,
                IsValidated=0,
                SignerLinkSentAt=datetime('now')
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageValidated(long workOrderId)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=0,
                IsQuoteReceived=0,
                IsSentToSigner=0,
                IsValidated=1
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetValidatedPdfSent(long workOrderId, bool sent)
    {
        using var con = Open();
        con.Open();

        con.Execute("UPDATE WorkOrders SET IsValidatedPdfSent=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = sent ? 1 : 0 });
    }

    public static void SetPerformed(long workOrderId, bool isPerformed)
    {
        using var con = Open();
        con.Open();

        con.Execute("UPDATE WorkOrders SET IsPerformed=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = isPerformed ? 1 : 0 });
    }

    public static void SetCancelled(long workOrderId, bool isCancelled)
    {
        using var con = Open();
        con.Open();

        con.Execute("UPDATE WorkOrders SET IsCancelled=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = isCancelled ? 1 : 0 });
    }

    public static void SetTrashed(long workOrderId, bool trashed)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsTrashed=@T,
                TrashedAt=@At
            WHERE Id=@Id;
        """, new
        {
            Id = workOrderId,
            T = trashed ? 1 : 0,
            At = trashed ? DateTime.Today.ToString("yyyy-MM-dd") : null
        });
    }

    public static void DeleteWorkOrderPermanently(long workOrderId)
    {
        using var con = Open();
        con.Open();

        using var tx = con.BeginTransaction();

        con.Execute("DELETE FROM WorkOrderLines WHERE WorkOrderId=@Id;", new { Id = workOrderId }, tx);
        con.Execute("DELETE FROM WorkOrders WHERE Id=@Id;", new { Id = workOrderId }, tx);

        tx.Commit();
    }

    public static void SetArchived(long workOrderId, bool archived)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrders
            SET IsArchived=@A,
                ArchivedAt=@At
            WHERE Id=@Id;
        """, new
        {
            Id = workOrderId,
            A = archived ? 1 : 0,
            At = archived ? DateTime.Today.ToString("yyyy-MM-dd") : null
        });
    }

    private static long RequireProjectId(long? projectId)
    {
        if (projectId.HasValue && projectId.Value > 0)
            return projectId.Value;

        var cur = GetCurrentProjectId();
        if (cur.HasValue && cur.Value > 0)
            return cur.Value;

        throw new Exception("Aucun dossier courant. Sélectionne un dossier avant d’utiliser les listes.");
    }

    public static List<string> GetPlaces(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM Places WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertPlace(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO Places (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = (name ?? "").Trim() }
        );
    }

    public static void DeletePlace(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM Places WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenamePlace(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Places WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste des lieux (pour ce dossier).");

        con.Execute(
            "UPDATE Places SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        con.Execute(
            "UPDATE WorkOrders SET Place=@NewName WHERE ProjectId=@ProjectId AND Place=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    // ✅ Etages
    public static List<string> GetEtages(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM Etages WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertEtage(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO Etages (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = (name ?? "").Trim() }
        );
    }

    public static void DeleteEtage(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM Etages WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenameEtage(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Etages WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste des étages (pour ce dossier).");

        con.Execute(
            "UPDATE Etages SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        con.Execute(
            "UPDATE WorkOrders SET Etage=@NewName WHERE ProjectId=@ProjectId AND Etage=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    public static List<string> GetCompanies(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM Companies WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertCompany(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO Companies (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = (name ?? "").Trim() }
        );
    }

    public static void DeleteCompany(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM Companies WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );

        // ✅ supprime aussi la couleur associée
        DeleteCompanyColor(projectId, name);
    }

    public static void RenameCompany(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Companies WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste des entreprises (pour ce dossier).");

        con.Execute(
            "UPDATE Companies SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        con.Execute(
            "UPDATE WorkOrders SET PerformedBy=@NewName WHERE ProjectId=@ProjectId AND PerformedBy=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();

        // ✅ renommer aussi la couleur associée
        RenameCompanyColor(projectId, oldName, newName);
    }

    public static List<string> GetRequesters(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM Requesters WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertRequester(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO Requesters (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = (name ?? "").Trim() }
        );
    }

    public static void DeleteRequester(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM Requesters WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenameRequester(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Requesters WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Demandé par » (pour ce dossier).");

        con.Execute(
            "UPDATE Requesters SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        con.Execute(
            "UPDATE WorkOrders SET RequestedBy=@NewName WHERE ProjectId=@ProjectId AND RequestedBy=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    public static List<string> GetReserves(long projectId)
    {
        using var con = Open();
        con.Open();
        return con.Query<string>(
            "SELECT Name FROM Reserves WHERE ProjectId=@Id ORDER BY Name;",
            new { Id = projectId }
        ).ToList();
    }

    public static void InsertReserve(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "INSERT OR IGNORE INTO Reserves (ProjectId, Name) VALUES (@ProjectId, @Name);",
            new { ProjectId = projectId, Name = (name ?? "").Trim() }
        );
    }

    public static void DeleteReserve(long projectId, string name)
    {
        using var con = Open();
        con.Open();
        con.Execute(
            "DELETE FROM Reserves WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = name }
        );
    }

    public static void RenameReserve(long projectId, string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Reserves WHERE ProjectId=@ProjectId AND Name=@Name;",
            new { ProjectId = projectId, Name = newName },
            tx
        );
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Réserve » (pour ce dossier).");

        con.Execute(
            "UPDATE Reserves SET Name=@NewName WHERE ProjectId=@ProjectId AND Name=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        con.Execute(
            "UPDATE WorkOrders SET Reserve=@NewName WHERE ProjectId=@ProjectId AND Reserve=@OldName;",
            new { ProjectId = projectId, OldName = oldName, NewName = newName },
            tx
        );

        tx.Commit();
    }

    public static List<string> GetPlaces() => GetPlaces(RequireProjectId(null));
    public static void InsertPlace(string name) => InsertPlace(RequireProjectId(null), name);
    public static void DeletePlace(string name) => DeletePlace(RequireProjectId(null), name);
    public static void RenamePlace(string oldName, string newName) => RenamePlace(RequireProjectId(null), oldName, newName);

    public static List<string> GetEtages() => GetEtages(RequireProjectId(null));
    public static void InsertEtage(string name) => InsertEtage(RequireProjectId(null), name);
    public static void DeleteEtage(string name) => DeleteEtage(RequireProjectId(null), name);
    public static void RenameEtage(string oldName, string newName) => RenameEtage(RequireProjectId(null), oldName, newName);

    public static List<string> GetCompanies() => GetCompanies(RequireProjectId(null));
    public static void InsertCompany(string name) => InsertCompany(RequireProjectId(null), name);
    public static void DeleteCompany(string name) => DeleteCompany(RequireProjectId(null), name);
    public static void RenameCompany(string oldName, string newName) => RenameCompany(RequireProjectId(null), oldName, newName);

    public static List<string> GetRequesters() => GetRequesters(RequireProjectId(null));
    public static void InsertRequester(string name) => InsertRequester(RequireProjectId(null), name);
    public static void DeleteRequester(string name) => DeleteRequester(RequireProjectId(null), name);
    public static void RenameRequester(string oldName, string newName) => RenameRequester(RequireProjectId(null), oldName, newName);

    public static List<string> GetReserves() => GetReserves(RequireProjectId(null));
    public static void InsertReserve(string name) => InsertReserve(RequireProjectId(null), name);
    public static void DeleteReserve(string name) => DeleteReserve(RequireProjectId(null), name);
    public static void RenameReserve(string oldName, string newName) => RenameReserve(RequireProjectId(null), oldName, newName);

    // ✅ Ajoute une option vide sélectionnable en tête d'une liste de référence (nettoyée,
    // dédupliquée, triée) — pour les listes déroulantes où l'utilisateur doit pouvoir
    // choisir explicitement "rien" plutôt que de devoir effacer le texte à la main.
    public static List<string> WithEmptyOption(List<string> items)
    {
        items ??= new List<string>();
        var cleaned = items.Select(s => (s ?? "").Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                           .ToList();

        cleaned.Insert(0, ""); // option vide sélectionnable
        return cleaned;
    }

    public static void SeedPlacesIfEmpty(params string[] places)
    {
        long? cur = GetCurrentProjectId();
        if (!cur.HasValue || cur.Value <= 0)
            return;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Places WHERE ProjectId=@Id;", new { Id = cur.Value });
        if (count > 0) return;

        foreach (var p in places)
            con.Execute("INSERT INTO Places (ProjectId, Name) VALUES (@ProjectId, @Name);", new { ProjectId = cur.Value, Name = p });
    }

    public static void SeedEtagesIfEmpty(params string[] etages)
    {
        long? cur = GetCurrentProjectId();
        if (!cur.HasValue || cur.Value <= 0)
            return;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Etages WHERE ProjectId=@Id;", new { Id = cur.Value });
        if (count > 0) return;

        foreach (var e in etages)
            con.Execute("INSERT INTO Etages (ProjectId, Name) VALUES (@ProjectId, @Name);", new { ProjectId = cur.Value, Name = e });
    }

    public static void SeedCompaniesIfEmpty(params string[] companies)
    {
        long? cur = GetCurrentProjectId();
        if (!cur.HasValue || cur.Value <= 0)
            return;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Companies WHERE ProjectId=@Id;", new { Id = cur.Value });
        if (count > 0) return;

        foreach (var c in companies)
            con.Execute("INSERT INTO Companies (ProjectId, Name) VALUES (@ProjectId, @Name);", new { ProjectId = cur.Value, Name = c });
    }

    public static void SeedRequestersIfEmpty(params string[] names)
    {
        long? cur = GetCurrentProjectId();
        if (!cur.HasValue || cur.Value <= 0)
            return;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Requesters WHERE ProjectId=@Id;", new { Id = cur.Value });
        if (count > 0) return;

        foreach (var n in names)
            con.Execute("INSERT INTO Requesters (ProjectId, Name) VALUES (@ProjectId, @Name);", new { ProjectId = cur.Value, Name = n });
    }

    public static void SeedReservesIfEmpty(params string[] names)
    {
        long? cur = GetCurrentProjectId();
        if (!cur.HasValue || cur.Value <= 0)
            return;

        using var con = Open();
        con.Open();

        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Reserves WHERE ProjectId=@Id;", new { Id = cur.Value });
        if (count > 0) return;

        foreach (var n in names)
            con.Execute("INSERT INTO Reserves (ProjectId, Name) VALUES (@ProjectId, @Name);", new { ProjectId = cur.Value, Name = n });
    }

    public static List<Project> GetProjects(bool onlyActive = true)
    {
        using var con = Open();
        con.Open();

        if (onlyActive)
            return con.Query<Project>("SELECT * FROM Projects WHERE IsActive = 1 ORDER BY Name;").ToList();

        return con.Query<Project>("SELECT * FROM Projects ORDER BY Name;").ToList();
    }

    public static Project? GetProjectById(long id)
    {
        using var con = Open();
        con.Open();

        return con.QueryFirstOrDefault<Project>(
            "SELECT * FROM Projects WHERE Id=@Id;",
            new { Id = id }
        );
    }

    public static Project? GetCurrentProject()
    {
        var id = GetCurrentProjectId();
        return id.HasValue ? GetProjectById(id.Value) : null;
    }

    public static long InsertProject(string name, string address)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            INSERT INTO Projects (Name, Address, IsActive)
            VALUES (@Name, @Address, 1);
        """, new { Name = (name ?? "").Trim(), Address = (address ?? "").Trim() });

        return con.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    public static void UpdateProject(Project project)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE Projects
            SET Name=@Name,
                Address=@Address,
                IsActive=@IsActive
            WHERE Id=@Id;
        """, new
        {
            project.Id,
            Name = (project.Name ?? "").Trim(),
            Address = (project.Address ?? "").Trim(),
            IsActive = project.IsActive ? 1 : 0
        });
    }

    public static void SetProjectActive(long projectId, bool isActive)
    {
        using var con = Open();
        con.Open();

        con.Execute(
            "UPDATE Projects SET IsActive=@IsActive WHERE Id=@Id;",
            new { Id = projectId, IsActive = isActive ? 1 : 0 }
        );
    }

    public static int GetWorkOrderCountForProject(long projectId)
    {
        using var con = Open();
        con.Open();

        return con.ExecuteScalar<int>("SELECT COUNT(1) FROM WorkOrders WHERE ProjectId=@Id;", new { Id = projectId });
    }

    public static void DeleteProjectAndWorkOrders(long projectId)
    {
        using var con = Open();
        con.Open();

        using var tx = con.BeginTransaction();

        con.Execute(
            "DELETE FROM WorkOrderLines WHERE WorkOrderId IN (SELECT Id FROM WorkOrders WHERE ProjectId=@Id);",
            new { Id = projectId },
            tx
        );

        con.Execute(
            "DELETE FROM WorkOrders WHERE ProjectId=@Id;",
            new { Id = projectId },
            tx
        );

        con.Execute("DELETE FROM Projects WHERE Id=@Id;", new { Id = projectId }, tx);

        var currentId = GetCurrentProjectId();
        if (currentId.HasValue && currentId.Value == projectId)
        {
            con.Execute("""
                INSERT INTO Settings(Key, Value) VALUES ('CurrentProjectId', '')
                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
            """, transaction: tx);
        }

        tx.Commit();
    }

    public static List<WorkOrderLine> GetWorkOrderLines(long workOrderId)
    {
        using var con = Open();
        con.Open();

        return con.Query<WorkOrderLine>(
            "SELECT * FROM WorkOrderLines WHERE WorkOrderId=@WorkOrderId ORDER BY Id;",
            new { WorkOrderId = workOrderId }
        ).ToList();
    }

    public static long InsertWorkOrderLine(long workOrderId, string label, double qty, double unitPrice)
    {
        using var con = Open();
        con.Open();

        var lineTotal = Math.Round(qty * unitPrice, 2);

        con.Execute("""
            INSERT INTO WorkOrderLines (WorkOrderId, Label, Qty, UnitPrice, LineTotal)
            VALUES (@WorkOrderId, @Label, @Qty, @UnitPrice, @LineTotal);
        """, new
        {
            WorkOrderId = workOrderId,
            Label = (label ?? "").Trim(),
            Qty = qty,
            UnitPrice = unitPrice,
            LineTotal = lineTotal
        });

        return con.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    public static void UpdateWorkOrderLine(WorkOrderLine line)
    {
        using var con = Open();
        con.Open();

        con.Execute("""
            UPDATE WorkOrderLines
            SET Label=@Label,
                Qty=@Qty,
                UnitPrice=@UnitPrice,
                LineTotal=@LineTotal
            WHERE Id=@Id;
        """, new
        {
            line.Id,
            Label = (line.Label ?? "").Trim(),
            line.Qty,
            line.UnitPrice,
            line.LineTotal
        });
    }

    public static void DeleteWorkOrderLine(long lineId)
    {
        using var con = Open();
        con.Open();

        con.Execute("DELETE FROM WorkOrderLines WHERE Id=@Id;", new { Id = lineId });
    }

    public static void SetProjectColorHex(long projectId, string? colorHex)
    {
        if (projectId <= 0) return;

        colorHex = (colorHex ?? "").Trim();

        using var con = Open();
        con.Open();

        con.Execute("UPDATE Projects SET ColorHex=@ColorHex WHERE Id=@Id;",
            new { Id = projectId, ColorHex = colorHex });
    }

    public static void SetProjectManager(long projectId, string? managerName, string? managerContact)
    {
        if (projectId <= 0) return;

        using var con = Open();
        con.Open();

        con.Execute("UPDATE Projects SET ManagerName=@ManagerName, ManagerContact=@ManagerContact WHERE Id=@Id;",
            new { Id = projectId, ManagerName = (managerName ?? "").Trim(), ManagerContact = (managerContact ?? "").Trim() });
    }
}