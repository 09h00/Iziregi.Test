// IziregiConfigService.cs
// Lecture/écriture de la configuration locale (clé API, URL serveur).
// Fichier : %APPDATA%\Iziregi\iziregi-config.json
// Ce fichier ne fait jamais partie du déploiement de l'app — il est propre à chaque poste.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Iziregi.Test.Services;

public class IziregiConfig
{
    public string ServerBaseUrl { get; set; } = "https://iziregi.com";
    public string ServerApiKey  { get; set; } = "";
}

public static class IziregiConfigService
{
    // ✅ Sécurité : format réellement écrit sur le disque. La clé API n'est plus jamais
    // écrite en clair — uniquement chiffrée avec la DPAPI Windows (liée à l'utilisateur
    // Windows courant sur ce poste). "ServerApiKey" (clair) n'est conservé ici que pour
    // LIRE les anciens fichiers de config écrits avant ce changement (migration
    // automatique, une seule fois) ; il n'est plus jamais réécrit.
    private class StoredConfig
    {
        public string ServerBaseUrl { get; set; } = "https://iziregi.com";
        public string? ServerApiKeyProtected { get; set; }
        public string? ServerApiKey { get; set; }
    }

    private static IziregiConfig? _current;

    /// <summary>Chemin du fichier de configuration sur ce poste.
    /// Cherche d'abord dans le dossier de l'app (%LOCALAPPDATA%\Iziregi),
    /// puis dans %APPDATA%\Iziregi comme fallback (multi-utilisateurs).
    /// </summary>
    public static string ConfigPath
    {
        get
        {
            // 1. Même dossier que l'executable (pratique pour les déploiements client)
            var appDir = Path.Combine(AppContext.BaseDirectory, "iziregi-config.json");
            if (File.Exists(appDir)) return appDir;

            // 2. %APPDATA%\Iziregi (roaming, survit aux mises à jour depuis une source externe)
            var roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Iziregi", "iziregi-config.json");
            if (File.Exists(roaming)) return roaming;

            // 3. Par défaut : dossier de l'app (sera créé là lors du Save)
            return appDir;
        }
    }

    /// <summary>Configuration active (chargée une seule fois, mise en cache).</summary>
    public static IziregiConfig Current => _current ??= Load();

    /// <summary>Recharge la configuration depuis le disque (utile après Save).</summary>
    public static void Reload() => _current = Load();

    // ✅ Sécurité : chiffre la clé API avec la DPAPI Windows (DataProtectionScope.CurrentUser
    // -> seul le même compte Windows, sur ce même poste, peut la déchiffrer). Si la clé est
    // vide, on ne chiffre rien (évite un blob inutile dans le fichier de config).
    private static string? ProtectApiKey(string? plainApiKey)
    {
        if (string.IsNullOrEmpty(plainApiKey)) return null;
        var plainBytes = Encoding.UTF8.GetBytes(plainApiKey);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    // ✅ Déchiffre la clé API. Renvoie "" si le blob est absent, corrompu, ou s'il a été
    // chiffré sur un autre poste / par un autre utilisateur Windows (la DPAPI ne peut alors
    // pas le déchiffrer) : dans ce cas la fenêtre de configuration se réaffiche simplement
    // au démarrage pour ressaisir la clé, sans faire planter l'application.
    private static string UnprotectApiKey(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
    }

    private static IziregiConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var stored = JsonSerializer.Deserialize<StoredConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (stored != null)
                {
                    if (!string.IsNullOrEmpty(stored.ServerApiKeyProtected))
                    {
                        // Format actuel : clé chiffrée (DPAPI).
                        return new IziregiConfig
                        {
                            ServerBaseUrl = stored.ServerBaseUrl,
                            ServerApiKey = UnprotectApiKey(stored.ServerApiKeyProtected)
                        };
                    }

                    if (!string.IsNullOrEmpty(stored.ServerApiKey))
                    {
                        // ✅ Migration : ancien fichier de config avec clé API en clair
                        // (écrit avant ce changement). On la reprend telle quelle une
                        // seule fois, puis on réécrit immédiatement le fichier au format
                        // chiffré pour qu'elle ne reste plus jamais en clair sur le disque.
                        var migrated = new IziregiConfig
                        {
                            ServerBaseUrl = stored.ServerBaseUrl,
                            ServerApiKey = stored.ServerApiKey
                        };
                        Save(migrated);
                        return migrated;
                    }

                    return new IziregiConfig { ServerBaseUrl = stored.ServerBaseUrl };
                }
            }
        }
        catch { /* config illisible → valeurs par défaut */ }

        return new IziregiConfig(); // ServerApiKey vide → dialog de setup affiché au démarrage
    }

    /// <summary>Enregistre la configuration (clé API chiffrée via DPAPI) et met à jour le cache.</summary>
    public static void Save(IziregiConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var stored = new StoredConfig
        {
            ServerBaseUrl = cfg.ServerBaseUrl,
            ServerApiKeyProtected = ProtectApiKey(cfg.ServerApiKey)
            // ServerApiKey (clair) volontairement laissé à null : on ne l'écrit plus jamais.
        };

        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        _current = cfg;
    }
}
