// IziregiConfigService.cs
// Lecture/écriture de la configuration locale (clé API, URL serveur).
// Fichier : %APPDATA%\Iziregi\iziregi-config.json
// Ce fichier ne fait jamais partie du déploiement de l'app — il est propre à chaque poste.

using System;
using System.IO;
using System.Text.Json;

namespace Iziregi.Test.Services;

public class IziregiConfig
{
    public string ServerBaseUrl { get; set; } = "https://iziregi.com";
    public string ServerApiKey  { get; set; } = "";
}

public static class IziregiConfigService
{
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

    private static IziregiConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<IziregiConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg != null) return cfg;
            }
        }
        catch { /* config illisible → valeurs par défaut */ }

        return new IziregiConfig(); // ServerApiKey vide → dialog de setup affiché au démarrage
    }

    /// <summary>Enregistre la configuration et met à jour le cache.</summary>
    public static void Save(IziregiConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        _current = cfg;
    }
}
