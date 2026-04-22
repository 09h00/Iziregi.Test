// File: Data/Db.cs
using System.Globalization;
using System.IO;
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

        // Settings (valeurs par défaut configurables)
        con.Execute("""
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        """);

        // WorkOrders extensions
        TryAddColumn(con, "WorkOrders", "Description", "TEXT");
        TryAddColumn(con, "WorkOrders", "IsCancelled", "INTEGER NOT NULL DEFAULT 0");

        // Devis
        TryAddColumn(con, "WorkOrders", "LaborHours", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "LaborRate", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TravelQty", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TravelRate", "REAL NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TvaRate", "REAL NOT NULL DEFAULT 8.1");
        TryAddColumn(con, "WorkOrders", "QuoteNotes", "TEXT");

        // Projet lié
        TryAddColumn(con, "WorkOrders", "ProjectId", "INTEGER");

        // Signature / Validation
        TryAddColumn(con, "WorkOrders", "SignatureName", "TEXT");
        TryAddColumn(con, "WorkOrders", "SignatureDate", "TEXT");
        TryAddColumn(con, "WorkOrders", "SignaturePng", "BLOB");

        // Pipeline
        TryAddColumn(con, "WorkOrders", "IsInCreation", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsSentToCompany", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsQuoteReceived", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsSentToSigner", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "IsValidatedPdfSent", "INTEGER NOT NULL DEFAULT 0");

        // Ancien champ (compat)
        TryAddColumn(con, "WorkOrders", "IsPendingValidation", "INTEGER NOT NULL DEFAULT 0");

        // Corbeille
        TryAddColumn(con, "WorkOrders", "IsTrashed", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "TrashedAt", "TEXT");

        // Archives
        TryAddColumn(con, "WorkOrders", "IsArchived", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(con, "WorkOrders", "ArchivedAt", "TEXT");
    }

    private static void TryAddColumn(SqliteConnection con, string table, string column, string sqlType)
    {
        try { con.Execute($"ALTER TABLE {table} ADD COLUMN {column} {sqlType};"); }
        catch { }
    }

    // -------------------------
    // Helpers conversion
    // -------------------------
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

    // =========================
    // Settings (defaults)
    // =========================
    private static string? GetSetting(string key)
    {
        using var con = Open();
        return con.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key=@Key;", new { Key = key });
    }

    private static void SetSetting(string key, string value)
    {
        using var con = Open();
        con.Execute("""
            INSERT INTO Settings(Key, Value) VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
        """, new { Key = key, Value = value });
    }

    public static string GetDefaultPlace() => GetSetting("DefaultPlace") ?? "";
    public static void SetDefaultPlace(string value) => SetSetting("DefaultPlace", (value ?? "").Trim());

    public static string GetDefaultCompany() => GetSetting("DefaultCompany") ?? "";
    public static void SetDefaultCompany(string value) => SetSetting("DefaultCompany", (value ?? "").Trim());

    public static string GetDefaultRequester() => GetSetting("DefaultRequester") ?? "";
    public static void SetDefaultRequester(string value) => SetSetting("DefaultRequester", (value ?? "").Trim());

    // =========================
    // WorkOrders
    // =========================
    public static List<WorkOrder> GetWorkOrders()
    {
        using var con = Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,
                IsPerformed,
                IsCancelled,

                IsTrashed,
                TrashedAt,

                IsArchived,
                ArchivedAt,

                Description,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 0
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetTrashedWorkOrders()
    {
        using var con = Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,
                IsPerformed,
                IsCancelled,

                IsTrashed,
                TrashedAt,

                IsArchived,
                ArchivedAt,

                Description,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 1
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static List<WorkOrder> GetArchivedWorkOrders()
    {
        using var con = Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,
                IsPerformed,
                IsCancelled,

                IsTrashed,
                TrashedAt,

                IsArchived,
                ArchivedAt,

                Description,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsArchived, 0) = 1
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    // ✅ Comptabilité : inclut Archives, exclut Corbeille, exclut Annulé
    public static List<WorkOrder> GetWorkOrdersForAccounting()
    {
        using var con = Open();

        var rows = con.Query("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,
                IsPerformed,
                IsCancelled,

                IsTrashed,
                TrashedAt,

                IsArchived,
                ArchivedAt,

                Description,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng
            FROM WorkOrders
            WHERE COALESCE(IsTrashed, 0) = 0
              AND COALESCE(IsCancelled, 0) = 0
            ORDER BY Id DESC;
        """).ToList();

        return rows.Select(MapWorkOrderRow).ToList();
    }

    public static WorkOrder? GetWorkOrderById(long id)
    {
        using var con = Open();

        var row = con.QueryFirstOrDefault("""
            SELECT
                Id, ProjectId, BdrNumber,
                Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,
                IsPerformed,
                IsCancelled,

                IsTrashed,
                TrashedAt,

                IsArchived,
                ArchivedAt,

                Description,
                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate,
                QuoteNotes,
                SignatureName, SignatureDate, SignaturePng
            FROM WorkOrders
            WHERE Id=@Id;
        """, new { Id = id });

        if (row == null) return null;
        return MapWorkOrderRow(row);
    }

    private static WorkOrder MapWorkOrderRow(dynamic row)
    {
        long id = AsLong(row.Id);
        long? projectId = row.ProjectId == null || row.ProjectId is DBNull ? null : AsLong(row.ProjectId);

        var requestDate = AsDate(AsString(row.RequestDate), DateTime.Today);

        string sigName = AsString(row.SignatureName);
        DateTime? sigDate = null;
        var sigDateStr = AsString(row.SignatureDate, "");
        if (!string.IsNullOrWhiteSpace(sigDateStr))
            sigDate = AsDate(sigDateStr, DateTime.Today);

        byte[]? sigPng = null;
        try { sigPng = row.SignaturePng as byte[]; } catch { }

        DateTime? trashedAt = null;
        var trashedAtStr = AsString(row.TrashedAt, "");
        if (!string.IsNullOrWhiteSpace(trashedAtStr))
            trashedAt = AsDate(trashedAtStr, DateTime.Today);

        DateTime? archivedAt = null;
        var archivedAtStr = AsString(row.ArchivedAt, "");
        if (!string.IsNullOrWhiteSpace(archivedAtStr))
            archivedAt = AsDate(archivedAtStr, DateTime.Today);

        return new WorkOrder
        {
            Id = id,
            ProjectId = projectId,
            BdrNumber = AsInt(row.BdrNumber),

            Place = AsString(row.Place),
            RequestedBy = AsString(row.RequestedBy),
            PerformedBy = AsString(row.PerformedBy),
            RequestDate = requestDate,

            IsInCreation = AsBool01(row.IsInCreation),
            IsSentToCompany = AsBool01(row.IsSentToCompany),
            IsQuoteReceived = AsBool01(row.IsQuoteReceived),
            IsSentToSigner = AsBool01(row.IsSentToSigner),
            IsValidated = AsBool01(row.IsValidated),
            IsValidatedPdfSent = AsBool01(row.IsValidatedPdfSent),

            IsPerformed = AsBool01(row.IsPerformed),
            IsCancelled = AsBool01(row.IsCancelled),

            IsTrashed = AsBool01(row.IsTrashed),
            TrashedAt = trashedAt,

            IsArchived = AsBool01(row.IsArchived),
            ArchivedAt = archivedAt,

            Description = AsString(row.Description),

            LaborHours = AsDouble(row.LaborHours),
            LaborRate = AsDouble(row.LaborRate),
            TravelQty = AsDouble(row.TravelQty),
            TravelRate = AsDouble(row.TravelRate),
            TvaRate = AsDouble(row.TvaRate, 8.1),
            QuoteNotes = AsString(row.QuoteNotes),

            SignatureName = sigName,
            SignatureDate = sigDate,
            SignaturePng = sigPng
        };
    }

    public static int GetNextBdrNumber()
    {
        using var con = Open();
        return con.ExecuteScalar<int>("SELECT COALESCE(MAX(BdrNumber), 0) + 1 FROM WorkOrders;");
    }

    public static void InsertWorkOrder(WorkOrder wo)
    {
        using var con = Open();

        con.Execute("""
            INSERT INTO WorkOrders (
                BdrNumber, Place, RequestedBy, PerformedBy, RequestDate,

                IsInCreation,
                IsSentToCompany,
                IsQuoteReceived,
                IsSentToSigner,
                IsValidated,
                IsValidatedPdfSent,

                IsPerformed, Description, IsCancelled,

                IsTrashed, TrashedAt,
                IsArchived, ArchivedAt,

                LaborHours, LaborRate, TravelQty, TravelRate, TvaRate, QuoteNotes, ProjectId,
                SignatureName, SignatureDate, SignaturePng
            )
            VALUES (
                @BdrNumber, @Place, @RequestedBy, @PerformedBy, @RequestDate,

                @IsInCreation,
                @IsSentToCompany,
                @IsQuoteReceived,
                @IsSentToSigner,
                @IsValidated,
                @IsValidatedPdfSent,

                @IsPerformed, @Description, @IsCancelled,

                @IsTrashed, @TrashedAt,
                @IsArchived, @ArchivedAt,

                @LaborHours, @LaborRate, @TravelQty, @TravelRate, @TvaRate, @QuoteNotes, @ProjectId,
                @SignatureName, @SignatureDate, @SignaturePng
            );
        """, new
        {
            wo.BdrNumber,
            wo.Place,
            wo.RequestedBy,
            wo.PerformedBy,
            RequestDate = wo.RequestDate.ToString("yyyy-MM-dd"),

            IsInCreation = wo.IsInCreation ? 1 : 0,
            IsSentToCompany = wo.IsSentToCompany ? 1 : 0,
            IsQuoteReceived = wo.IsQuoteReceived ? 1 : 0,
            IsSentToSigner = wo.IsSentToSigner ? 1 : 0,
            IsValidated = wo.IsValidated ? 1 : 0,
            IsValidatedPdfSent = wo.IsValidatedPdfSent ? 1 : 0,

            IsPerformed = wo.IsPerformed ? 1 : 0,
            Description = wo.Description ?? "",
            IsCancelled = wo.IsCancelled ? 1 : 0,

            IsTrashed = wo.IsTrashed ? 1 : 0,
            TrashedAt = wo.TrashedAt?.ToString("yyyy-MM-dd"),

            IsArchived = wo.IsArchived ? 1 : 0,
            ArchivedAt = wo.ArchivedAt?.ToString("yyyy-MM-dd"),

            wo.LaborHours,
            wo.LaborRate,
            wo.TravelQty,
            wo.TravelRate,
            wo.TvaRate,
            QuoteNotes = wo.QuoteNotes ?? "",
            wo.ProjectId,

            SignatureName = wo.SignatureName ?? "",
            SignatureDate = wo.SignatureDate?.ToString("yyyy-MM-dd"),
            SignaturePng = wo.SignaturePng
        });
    }

    public static void UpdateWorkOrderHeader(WorkOrder wo)
    {
        using var con = Open();
        con.Execute("""
            UPDATE WorkOrders
            SET Place=@Place,
                RequestedBy=@RequestedBy,
                PerformedBy=@PerformedBy,
                RequestDate=@RequestDate,
                Description=@Description,
                ProjectId=@ProjectId
            WHERE Id=@Id;
        """, new
        {
            wo.Id,
            wo.Place,
            wo.RequestedBy,
            wo.PerformedBy,
            RequestDate = wo.RequestDate.ToString("yyyy-MM-dd"),
            Description = wo.Description ?? "",
            wo.ProjectId
        });
    }

    public static void UpdateWorkOrderQuote(WorkOrder wo)
    {
        using var con = Open();
        con.Execute("""
            UPDATE WorkOrders
            SET LaborHours=@LaborHours, LaborRate=@LaborRate,
                TravelQty=@TravelQty, TravelRate=@TravelRate,
                TvaRate=@TvaRate,
                QuoteNotes=@QuoteNotes
            WHERE Id=@Id;
        """, new
        {
            wo.Id,
            wo.LaborHours,
            wo.LaborRate,
            wo.TravelQty,
            wo.TravelRate,
            wo.TvaRate,
            QuoteNotes = wo.QuoteNotes ?? ""
        });
    }

    public static void UpdateWorkOrderSignatureRaw(WorkOrder wo)
    {
        using var con = Open();
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

    // =========================
    // Pipeline helpers
    // =========================
    public static void SetStageInCreation(long workOrderId)
    {
        using var con = Open();
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
        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=1,
                IsQuoteReceived=0,
                IsSentToSigner=0,
                IsValidated=0
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageQuoteReceived(long workOrderId)
    {
        using var con = Open();
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
        con.Execute("""
            UPDATE WorkOrders
            SET IsInCreation=0,
                IsSentToCompany=0,
                IsQuoteReceived=0,
                IsSentToSigner=1,
                IsValidated=0
            WHERE Id=@Id;
        """, new { Id = workOrderId });
    }

    public static void SetStageValidated(long workOrderId)
    {
        using var con = Open();
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
        con.Execute("UPDATE WorkOrders SET IsValidatedPdfSent=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = sent ? 1 : 0 });
    }

    public static void SetPerformed(long workOrderId, bool isPerformed)
    {
        using var con = Open();
        con.Execute("UPDATE WorkOrders SET IsPerformed=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = isPerformed ? 1 : 0 });
    }

    public static void SetCancelled(long workOrderId, bool isCancelled)
    {
        using var con = Open();
        con.Execute("UPDATE WorkOrders SET IsCancelled=@V WHERE Id=@Id;",
            new { Id = workOrderId, V = isCancelled ? 1 : 0 });
    }

    // =========================
    // Corbeille
    // =========================
    public static void SetTrashed(long workOrderId, bool trashed)
    {
        using var con = Open();
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
        using var tx = con.BeginTransaction();

        con.Execute("DELETE FROM WorkOrderLines WHERE WorkOrderId=@Id;", new { Id = workOrderId }, tx);
        con.Execute("DELETE FROM WorkOrders WHERE Id=@Id;", new { Id = workOrderId }, tx);

        tx.Commit();
    }

    // =========================
    // Archives
    // =========================
    public static void SetArchived(long workOrderId, bool archived)
    {
        using var con = Open();
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

    // =========================
    // Lists
    // =========================
    public static List<string> GetPlaces()
    {
        using var con = Open();
        return con.Query<string>("SELECT Name FROM Places ORDER BY Name;").ToList();
    }

    public static void InsertPlace(string name)
    {
        using var con = Open();
        con.Execute("INSERT OR IGNORE INTO Places (Name) VALUES (@Name);", new { Name = (name ?? "").Trim() });
    }

    public static void DeletePlace(string name)
    {
        using var con = Open();
        con.Execute("DELETE FROM Places WHERE Name=@Name;", new { Name = name });
    }

    public static void RenamePlace(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Places WHERE Name=@Name;", new { Name = newName }, tx);
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste des lieux.");

        con.Execute("UPDATE Places SET Name=@NewName WHERE Name=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        con.Execute("UPDATE WorkOrders SET Place=@NewName WHERE Place=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        tx.Commit();
    }

    public static List<string> GetCompanies()
    {
        using var con = Open();
        return con.Query<string>("SELECT Name FROM Companies ORDER BY Name;").ToList();
    }

    public static void InsertCompany(string name)
    {
        using var con = Open();
        con.Execute("INSERT OR IGNORE INTO Companies (Name) VALUES (@Name);", new { Name = (name ?? "").Trim() });
    }

    public static void DeleteCompany(string name)
    {
        using var con = Open();
        con.Execute("DELETE FROM Companies WHERE Name=@Name;", new { Name = name });
    }

    public static void RenameCompany(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Companies WHERE Name=@Name;", new { Name = newName }, tx);
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste des entreprises.");

        con.Execute("UPDATE Companies SET Name=@NewName WHERE Name=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        con.Execute("UPDATE WorkOrders SET PerformedBy=@NewName WHERE PerformedBy=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        tx.Commit();
    }

    public static List<string> GetRequesters()
    {
        using var con = Open();
        return con.Query<string>("SELECT Name FROM Requesters ORDER BY Name;").ToList();
    }

    public static void InsertRequester(string name)
    {
        using var con = Open();
        con.Execute("INSERT OR IGNORE INTO Requesters (Name) VALUES (@Name);", new { Name = (name ?? "").Trim() });
    }

    public static void DeleteRequester(string name)
    {
        using var con = Open();
        con.Execute("DELETE FROM Requesters WHERE Name=@Name;", new { Name = name });
    }

    public static void RenameRequester(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName)
            return;

        using var con = Open();
        con.Open();
        using var tx = con.BeginTransaction();

        var exists = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Requesters WHERE Name=@Name;", new { Name = newName }, tx);
        if (exists > 0)
            throw new Exception("Ce nom existe déjà dans la liste « Demandé par ».");

        con.Execute("UPDATE Requesters SET Name=@NewName WHERE Name=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        con.Execute("UPDATE WorkOrders SET RequestedBy=@NewName WHERE RequestedBy=@OldName;",
            new { OldName = oldName, NewName = newName }, tx);

        tx.Commit();
    }

    public static void SeedPlacesIfEmpty(params string[] places)
    {
        using var con = Open();
        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Places;");
        if (count > 0) return;

        foreach (var p in places)
            con.Execute("INSERT INTO Places (Name) VALUES (@Name);", new { Name = p });
    }

    public static void SeedCompaniesIfEmpty(params string[] companies)
    {
        using var con = Open();
        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Companies;");
        if (count > 0) return;

        foreach (var c in companies)
            con.Execute("INSERT INTO Companies (Name) VALUES (@Name);", new { Name = c });
    }

    public static void SeedRequestersIfEmpty(params string[] names)
    {
        using var con = Open();
        var count = con.ExecuteScalar<long>("SELECT COUNT(1) FROM Requesters;");
        if (count > 0) return;

        foreach (var n in names)
            con.Execute("INSERT INTO Requesters (Name) VALUES (@Name);", new { Name = n });
    }

    // =========================
    // Projects
    // =========================
    public static List<Project> GetProjects(bool onlyActive = true)
    {
        using var con = Open();

        if (onlyActive)
            return con.Query<Project>("SELECT * FROM Projects WHERE IsActive = 1 ORDER BY Name;").ToList();

        return con.Query<Project>("SELECT * FROM Projects ORDER BY Name;").ToList();
    }

    public static long InsertProject(string name, string address)
    {
        using var con = Open();
        con.Execute("""
            INSERT INTO Projects (Name, Address, IsActive)
            VALUES (@Name, @Address, 1);
        """, new { Name = (name ?? "").Trim(), Address = (address ?? "").Trim() });

        return con.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    // =========================
    // WorkOrderLines
    // =========================
    public static List<WorkOrderLine> GetWorkOrderLines(long workOrderId)
    {
        using var con = Open();
        return con.Query<WorkOrderLine>(
            "SELECT * FROM WorkOrderLines WHERE WorkOrderId=@WorkOrderId ORDER BY Id;",
            new { WorkOrderId = workOrderId }
        ).ToList();
    }

    public static long InsertWorkOrderLine(long workOrderId, string label, double qty, double unitPrice)
    {
        using var con = Open();
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
        con.Execute("""
            UPDATE WorkOrderLines
            SET Label=@Label, Qty=@Qty, UnitPrice=@UnitPrice, LineTotal=@LineTotal
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
        con.Execute("DELETE FROM WorkOrderLines WHERE Id=@Id;", new { Id = lineId });
    }
}