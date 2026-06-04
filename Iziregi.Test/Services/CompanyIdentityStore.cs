// File: Services/CompanyIdentityStore.cs
using System;
using System.IO;
using System.Text.Json;

namespace Iziregi.Test.Services;

public sealed class CompanyIdentity
{
    public string CompanyName { get; set; } = "";
    public string CompanyAddress { get; set; } = "";

    // Optionnel : tu peux garder ça si tu veux aussi gérer un logo “référencé”
    public string? LogoPath { get; set; }
}

public static class CompanyIdentityStore
{
    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "Settings");

    private static string IdentityFilePath => Path.Combine(BaseDir, "company-identity.json");

    // Logo “interne” (celui utilisé par ton code actuel)
    private static string LogoPngPath => Path.Combine(BaseDir, "logo.png");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // ----------------------------
    // Identité (json)
    // ----------------------------
    public static CompanyIdentity Load()
    {
        try
        {
            if (!File.Exists(IdentityFilePath))
                return new CompanyIdentity();

            var json = File.ReadAllText(IdentityFilePath);
            return JsonSerializer.Deserialize<CompanyIdentity>(json, JsonOptions) ?? new CompanyIdentity();
        }
        catch
        {
            return new CompanyIdentity();
        }
    }

    public static void Save(CompanyIdentity identity)
    {
        if (identity == null) throw new ArgumentNullException(nameof(identity));

        Directory.CreateDirectory(BaseDir);

        var json = JsonSerializer.Serialize(identity, JsonOptions);
        File.WriteAllText(IdentityFilePath, json);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(IdentityFilePath))
                File.Delete(IdentityFilePath);
        }
        catch
        {
            // noop
        }
    }

    // ----------------------------
    // Logo (png bytes) — POUR CORRIGER TES ERREURS
    // ----------------------------
    public static byte[]? LoadLogoPngBytes()
    {
        try
        {
            if (!File.Exists(LogoPngPath))
                return null;

            return File.ReadAllBytes(LogoPngPath);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLogoPngBytes(byte[] pngBytes)
    {
        if (pngBytes == null) throw new ArgumentNullException(nameof(pngBytes));
        if (pngBytes.Length == 0) throw new ArgumentException("pngBytes is empty", nameof(pngBytes));

        Directory.CreateDirectory(BaseDir);
        File.WriteAllBytes(LogoPngPath, pngBytes);
    }

    public static void ClearLogo()
    {
        try
        {
            if (File.Exists(LogoPngPath))
                File.Delete(LogoPngPath);
        }
        catch
        {
            // noop
        }
    }
}