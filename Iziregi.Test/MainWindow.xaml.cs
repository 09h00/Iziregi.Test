// File: MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Pages;
using Microsoft.Win32;

// ✅ Fix ambiguïté System.Drawing vs WPF
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushConverter = System.Windows.Media.BrushConverter;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using Iziregi.Test.Services;
// ✅ Fix ambiguïté MessageBox / Clipboard (WPF vs WinForms)
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;

namespace Iziregi.Test;

public partial class MainWindow : Window
{
    // =========================
    // Contexte partagé
    // =========================
    private Project? _selectedProject;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    // =========================
    // INBOX watcher
    // =========================
    private static string InboxDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "INBOX");

    private static string ImportedDir => Path.Combine(InboxDir, "Imported");
    private static string ErrorDir => Path.Combine(InboxDir, "Error");

    private FileSystemWatcher? _inboxWatcher;
    private readonly Queue<string> _pendingInboxFiles = new();
    private bool _isProcessingInboxQueue = false;

    // =========================
    // ✅ Server sync (polling 60s)
    // =========================
    private readonly DispatcherTimer _serverSyncTimer = new();
    private bool _isServerSyncRunning = false;

    // ✅ Anti-doublon : soumissions déjà traitées dans cette session
    private readonly HashSet<string> _processedSubmissionKeys = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Http = CreateHttpClient();

    // ✅ Client HTTP dédié au téléchargement de l'installateur de mise à jour : un
    // installateur autonome peut peser plusieurs dizaines de Mo, largement au-delà du
    // délai de 20s utilisé pour les appels API classiques ci-dessus.
    private static readonly HttpClient DownloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    // ✅ Ajout d'un User-Agent : certains serveurs/WAF bloquent les requêtes sans en-tête User-Agent,
    // ce que HttpClient n'envoie pas par défaut, contrairement à un navigateur ou PowerShell.
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("User-Agent", "IziregiClient/1.0");
        return client;
    }

    // ✅ Sécurité : envoie la clé API via l'en-tête HTTP "X-Api-Key" plutôt que dans
    // l'URL (évite qu'elle apparaisse en clair dans les journaux d'accès du serveur).
    // La valeur est relue à chaque appel (pas mise en cache dans un en-tête par défaut
    // du HttpClient) car elle peut changer après la configuration initiale.
    private static async Task<HttpResponseMessage> GetWithApiKeyAsync(HttpClient client, string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(ServerApiKey))
            req.Headers.Add("X-Api-Key", ServerApiKey);
        return await client.SendAsync(req);
    }

    // =========================
    // ✅ Server sync config (MVP)
    // =========================
    internal static string ServerBaseUrl => IziregiConfigService.Current.ServerBaseUrl;
    internal static string ServerApiKey => IziregiConfigService.Current.ServerApiKey;

    // Anti-spam popups "not found"
    private readonly HashSet<string> _warnedNotFoundLocal = new(StringComparer.OrdinalIgnoreCase);

    // =========================
    // Pages (UserControls)
    // =========================
    private OverviewPage? _overviewPage;
    private DashboardPage? _dashboardPage;
    private AccountingPage? _accountingPage;
    private ArchivesPage? _archivesPage;
    private ArchivesTasksPage? _archivesTasksPage;
    private TrashPage? _trashPage;
    private TrashedTasksPage? _trashedTasksPage;
    private ListsPage? _listsPage;
    private PlanningPage? _planningPage;
    private Pages.SettingsPage? _settingsPage;
    private Pages.AddressBookPage? _addressBookPage;

    public MainWindow()
    {
        InitializeComponent();

        // ✅ Affiche la version installée dans la barre de titre : pratique pour vérifier
        // visuellement, à tout moment, quelle version tourne réellement (utile notamment
        // pour confirmer qu'une mise à jour automatique a bien été appliquée).
        try
        {
            var installedVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (installedVersion != null)
                this.Title = $"{this.Title} — v{installedVersion.ToString(3)}";
        }
        catch { }

        // ✅ Si l'application vient d'être relancée automatiquement juste après une mise
        // à jour silencieuse (voir Iziregi.iss, section [Run], paramètre "--updated"),
        // affiche un bandeau de confirmation au démarrage.
        try
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--updated", StringComparer.OrdinalIgnoreCase))
            {
                var installedVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                UpdateSuccessBannerText.Text = installedVersion != null
                    ? $"Iziregi a été mis à jour avec succès vers la version {installedVersion.ToString(3)}."
                    : "Iziregi a été mis à jour avec succès.";
                UpdateSuccessBanner.Visibility = Visibility.Visible;
            }
        }
        catch { }

        // ✅ Sécurité (17.07.2026) : configuration désormais OBLIGATOIRE si la clé API est
        // absente. Avant, fermer cette fenêtre (croix/Alt+F4) sans rien saisir laissait
        // l'appli démarrer normalement quand même (ShowDialog() sans vérifier le résultat)
        // -- ce qui rendait le contrôle de licence/essai plus bas entièrement facultatif :
        // il suffisait de ne jamais configurer de clé pour utiliser Iziregi sans limite.
        if (string.IsNullOrWhiteSpace(IziregiConfigService.Current.ServerApiKey))
        {
            var setup = new ConfigSetupWindow();
            var setupResult = setup.ShowDialog();
            if (setupResult != true || string.IsNullOrWhiteSpace(IziregiConfigService.Current.ServerApiKey))
            {
                WpfMessageBox.Show(
                    "La configuration du serveur (URL + clé d'accès) est nécessaire pour utiliser Iziregi.\n\n" +
                    "Relancez l'application pour réessayer.",
                    "Configuration requise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);
                Environment.Exit(0);
                return;
            }
        }

        // ✅ Abonnement par machine (Option A, 3 paliers) + mise à jour automatique :
        // reportés à APRÈS que la fenêtre principale soit chargée et active (événement
        // Loaded) au lieu d'être exécutés ici, dans le constructeur, avant que la
        // fenêtre ne soit visible. Le journal de diagnostic (iziregi-update-error.log)
        // a montré que la boîte de dialogue "Oui/Non" de mise à jour se fermait
        // instantanément avec le résultat "No", sans aucune action de l'utilisateur,
        // quand elle était affichée depuis le constructeur : à ce moment, aucune
        // fenêtre de l'application n'a encore le focus, et Windows semble alors
        // rejeter automatiquement la boîte. Une fois la fenêtre principale visible et
        // active (Loaded), la boîte se comporte normalement et attend un vrai clic.
        this.Loaded += async (s, e) =>
        {
            // Refus net UNIQUEMENT si le serveur répond explicitement clé invalide (401) ou
            // un refus de licence (403 : limite de machines ou abonnement/essai expiré) ;
            // en cas de serveur injoignable/réseau coupé, on ne bloque jamais (fail-open).
            if (!string.IsNullOrWhiteSpace(IziregiConfigService.Current.ServerApiKey))
            {
                var licenseInfo = await Task.Run(CheckMachineLicenseAllowedAsync);
                if (!licenseInfo.Allowed)
                {
                    // ✅ Sécurité (17.07.2026) : "invalid_key" (401, clé absente/inconnue du
                    // serveur) bloquait auparavant PAS du tout -- seul un 403 explicite
                    // ("limite atteinte") bloquait. N'importe quelle valeur non vide dans le
                    // champ "Clé d'accès" suffisait donc à contourner la licence, y compris
                    // l'expiration d'un essai de 7 jours. Voir CheckMachineLicenseAllowedAsync.
                    var message = licenseInfo.ErrorCode switch
                    {
                        "invalid_key" => "La clé d'accès configurée n'est pas reconnue par le serveur Iziregi.\n\n" +
                                          "Contactez votre administrateur Iziregi pour obtenir une clé valide, puis reconfigurez-la via Réglages.",
                        "subscription_expired" => "Votre abonnement (ou période d'essai) Iziregi est arrivé à échéance.\n\n" +
                                                   "Contactez l'éditeur Iziregi pour le renouveler.",
                        "machine_limit_reached" => "Le nombre maximum d'ordinateurs autorisés pour votre abonnement Iziregi est atteint.\n\n" +
                                                    "Contactez votre administrateur ou l'éditeur Iziregi pour augmenter votre palier.",
                        _ => "Accès refusé par le serveur Iziregi.\n\nContactez votre administrateur ou l'éditeur Iziregi."
                    };

                    WpfMessageBox.Show(
                        message,
                        "Licence Iziregi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                    Environment.Exit(0);
                    return;
                }

                // ✅ Mise à jour automatique : si le serveur annonce une version plus récente
                // que celle installée, on propose de la télécharger et de l'installer
                // maintenant. Ne bloque jamais le démarrage (serveur muet, refus utilisateur,
                // téléchargement impossible -> l'appli continue avec la version actuelle).
                PromptForUpdateIfNewer(licenseInfo.LatestVersion, licenseInfo.DownloadUrl, licenseInfo.InstallerSha256);
            }
        };

        // ✅ Le tableau de bord (dashboard) doit s'ouvrir en plein écran par défaut,
        // SANS montrer d'abord la fenêtre en taille normale centrée à l'écran (ce qui
        // créait un flash très visible et dérangeant au lancement, en plus du splash
        // natif désormais désactivé — voir Iziregi.Test.csproj).
        //
        // Piège WPF : assigner WindowState=Maximized dans le constructeur ne suffit
        // pas (WPF recalcule encore la position/taille après le constructeur), et
        // l'assigner dans Loaded (comme avant, le 06.07.2026 encore) arrive TROP TARD
        // : à ce stade, la fenêtre a déjà été peinte à l'écran en taille normale
        // pendant une fraction de seconde avant de sauter en plein écran, d'où le
        // flash. La fenêtre doit être déjà maximisée AVANT le tout premier rendu.
        //
        // Solution : utiliser SourceInitialized, qui se déclenche juste après la
        // création du handle de fenêtre (HWND) mais AVANT que Windows ne l'affiche
        // et ne la peigne à l'écran. La fenêtre apparaît donc directement en plein
        // écran, sans étape intermédiaire visible.
        this.SourceInitialized += (s, e) => { WindowState = WindowState.Maximized; };

        // DB init
        Db.Init();

        // ✅ Restaurer le projet courant depuis Settings (si existant)
        _selectedProject = Db.GetCurrentProject();
        Db.SetCurrentProjectId(_selectedProject?.Id);

        // Seeds de base (si vide) - dépend du projet courant
        Db.SeedPlacesIfEmpty("D20", "D21", "Extérieur");
        Db.SeedCompaniesIfEmpty("Electricien", "Sanitaire", "Ventilation");
        Db.SeedRequestersIfEmpty("Architecte");

        // Watcher INBOX
        StartInboxWatcher();

        // ✅ Badge projet
        UpdateProjectBadge(show: false); // Dashboard par défaut => caché

        // ✅ Page par défaut au démarrage, propre à chaque dossier (25.07.2026, demande de
        // Joe) : configurée dans Paramètres > Démarrage. Vide/dossier absent -> Dashboard.
        ShowDefaultStartupPage();

        // Init project selector
        InitProjectSelector();

        // ✅ Auto sync serveur (toutes les 60s)
        StartServerSyncTimer();
    }

    private void ProjectSelectorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Affiche un menu contextuel avec la liste des projets
            var projects = Db.GetProjects(true).OrderBy(p => p.Name).ToList();
            var menu = new System.Windows.Controls.ContextMenu();

            foreach (var p in projects)
            {
                var proj = p; // capture local copy for closure
                var mi = new System.Windows.Controls.MenuItem { Header = proj.Name, Tag = proj };
                mi.Click += (_, __) => { SetSelectedProjectRecreatePlanning(proj); try { RefreshProjectSelector(); } catch { } };
                menu.Items.Add(mi);
            }

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ProjectSelectorButton_Click exception: " + ex);
        }
    }

    private void InitProjectSelector()
    {
        try
        {
            var btn = this.FindName("ProjectSelectorButton") as System.Windows.Controls.Button;
            if (btn == null) return;

            // contenu et couleur selon projet courant
            var p = _selectedProject ?? Db.GetCurrentProject();
            btn.Content = p != null ? (p.Name ?? "Dossier") : "Dossier";

            var bg = p != null ? BrushFromHexOrNull(p.ColorHex) : BrushFromHexOrNull("#111827");
            if (bg == null || bg == MediaBrushes.Transparent)
                bg = BrushFromHexOrNull("#111827");

            try { btn.Background = bg; } catch { }
            try { btn.Foreground = GetTextBrushForBackground(bg); } catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("InitProjectSelector exception: " + ex);
        }
    }

    private void ProjectSelectorComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        try
        {
            var cb = this.FindName("ProjectSelectorComboBox") as System.Windows.Controls.ComboBox;
            if (cb?.SelectedItem is Iziregi.Test.Models.Project p)
            {
                // utilise la méthode existante pour appliquer le changement et recharger
                SetSelectedProjectRecreatePlanning(p);
                // mettre à jour l'apparence du sélecteur pour refléter la couleur du projet choisi
                try { RefreshProjectSelector(); } catch { }
                try { System.Diagnostics.Debug.WriteLine($"SetSelectedProject: id={_selectedProject?.Id} name={_selectedProject?.Name}"); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ProjectSelectorComboBox_SelectionChanged exception: " + ex);
        }
    }

    private void ManageProjectsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ChooseProject();
    }

    // Méthode publique pour rafraîchir la page Planning depuis d'autres pages
    public void RefreshPlanning()
    {
        try
        {
            _planningPage?.Reload();
            _planningPage?.RefreshCompanyColors();
        }
        catch
        {
            // non bloquant
        }
    }

    // =========================
    // ✅ Server sync timer
    // =========================
    private void StartServerSyncTimer()
    {
        _serverSyncTimer.Interval = TimeSpan.FromSeconds(60);
        _serverSyncTimer.Tick += async (_, __) => await ServerSyncTickAsync();
        _serverSyncTimer.Start();

        // 1er tick rapide au démarrage
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await ServerSyncTickAsync();
        }));
    }

    // ✅ Journalise une erreur de synchro par soumission, sans jamais faire planter quoi
    // que ce soit (le logging lui-même est protégé). Permet de diagnostiquer un futur blocage
    // au lieu de le découvrir uniquement en creusant le code en direct.
    private static void LogSyncError(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "Iziregi_sync_errors.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O}  {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    // =========================
    // ✅ Abonnement par machine (Option A) : identité machine + vérification ping
    // =========================

    // ✅ Sécurité (03.07.2026) : l'identifiant machine est maintenant dérivé du MachineGuid
    // Windows (registre), propre à CETTE installation Windows, plutôt que d'un simple
    // fichier dans Documents. Avant cette correction, machine-id.txt pouvait être copié
    // tel quel sur plusieurs ordinateurs pour tous se présenter comme "la même machine
    // déjà connue" au serveur, contournant complètement le plafond de licences par
    // machine qu'on vient de mettre en place. Le MachineGuid n'est pas copiable de cette
    // façon : il appartient à l'installation Windows elle-même, pas à un fichier utilisateur.
    private static string GetOrCreateMachineId()
    {
        try
        {
            var hwId = GetWindowsMachineGuid();
            if (!string.IsNullOrWhiteSpace(hwId))
                return hwId;
        }
        catch
        {
            // ignore, on retombe sur le fallback fichier ci-dessous
        }

        // Fallback (registre inaccessible pour une raison quelconque) : ancien mécanisme
        // par fichier — moins robuste contre la copie, mais ne bloque jamais le démarrage.
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "machine-id.txt");

            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }

            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    // Lit le MachineGuid Windows (HKLM\SOFTWARE\Microsoft\Cryptography), un identifiant
    // stable généré par Windows lui-même à l'installation, unique par machine, en lecture
    // seule pour un utilisateur standard (aucun droit administrateur requis).
    private static string? GetWindowsMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var value = key?.GetValue("MachineGuid") as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    // Interroge /internal/ping avec le machineId. Retourne false si le serveur répond
    // explicitement clé invalide (401) ou refus de licence (403 : limite de machines ou
    // abonnement/essai expiré). Toute autre situation (serveur OK, ou serveur injoignable)
    // retourne true — fail-open.
    // ✅ Sécurité (17.07.2026) : avant, seul un 403 bloquait -- un 401 (clé absente/inconnue
    // du serveur) était traité comme "toute autre situation" et laissait passer (fail-open).
    // N'importe quelle valeur non vide dans le champ "Clé d'accès" (garbage compris)
    // suffisait donc à contourner entièrement la licence, y compris l'expiration d'un essai
    // de 7 jours. ErrorCode distingue maintenant "invalid_key", "subscription_expired",
    // "machine_limit_reached" (repris du corps JSON du serveur) pour un message clair.
    private static async Task<(bool Allowed, string? ErrorCode, string? LatestVersion, string? DownloadUrl, string? InstallerSha256)> CheckMachineLicenseAllowedAsync()
    {
        try
        {
            var machineId = GetOrCreateMachineId();
            var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
            var url = $"{baseUrl}/internal/ping?machineId={Uri.EscapeDataString(machineId)}";
            LogUpdateStep($"Ping : appel de {url}");

            using var resp = await GetWithApiKeyAsync(Http, url);
            LogUpdateStep($"Ping : reponse HTTP {(int)resp.StatusCode} ({resp.StatusCode})");

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "invalid_key", null, null, null);

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var errorCode = "forbidden";
                try
                {
                    var body403 = await resp.Content.ReadAsStringAsync();
                    using var doc403 = JsonDocument.Parse(body403);
                    if (doc403.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                        errorCode = errEl.GetString() ?? errorCode;
                }
                catch { /* message générique si le corps n'est pas exploitable */ }
                return (false, errorCode, null, null, null);
            }

            string? latestVersion = null;
            string? downloadUrl = null;
            string? installerSha256 = null;
            try
            {
                var body = await resp.Content.ReadAsStringAsync();
                LogUpdateStep($"Ping : corps de la reponse = {body}");
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("latestVersion", out var lv) && lv.ValueKind == JsonValueKind.String)
                    latestVersion = lv.GetString();
                if (doc.RootElement.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String)
                    downloadUrl = du.GetString();
                // ✅ Hash SHA-256 de l'installateur publié par le serveur (vérification
                // d'intégrité avant exécution, 17.07.2026) : absent = null, ancien serveur ou
                // hash pas encore publié -> pas de vérification possible, voir DownloadAndLaunchUpdateAsync.
                if (doc.RootElement.TryGetProperty("installerSha256", out var sha) && sha.ValueKind == JsonValueKind.String)
                    installerSha256 = sha.GetString();
                LogUpdateStep($"Ping : latestVersion={latestVersion ?? "(null)"} downloadUrl={downloadUrl ?? "(null)"} installerSha256={installerSha256 ?? "(null)"}");
            }
            catch (Exception exParse)
            {
                // Réponse non-JSON ou champs absents (anciens serveurs) : pas de mise à jour proposée.
                LogUpdateStep($"Ping : ECHEC parsing JSON : {exParse.GetType().FullName}: {exParse.Message}");
            }

            return (true, null, latestVersion, downloadUrl, installerSha256);
        }
        catch (Exception ex)
        {
            // Serveur injoignable / réseau coupé : fail-open, l'appli continue de fonctionner.
            LogUpdateStep($"Ping : ECHEC (exception) : {ex.GetType().FullName}: {ex.Message}\n{ex}");
            return (true, null, null, null, null);
        }
    }

    // Compare la version annoncée par le serveur à la version installée localement (celle
    // définie par <Version> dans Iziregi.Test.csproj). Si le serveur en propose une plus
    // récente, demande à l'utilisateur s'il veut l'installer maintenant.
    private void PromptForUpdateIfNewer(string? latestVersion, string? downloadUrl, string? installerSha256)
    {
        try
        {
            LogUpdateStep($"PromptForUpdateIfNewer : latestVersion={latestVersion ?? "(null)"} downloadUrl={downloadUrl ?? "(null)"} installerSha256={installerSha256 ?? "(null)"}");

            if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                LogUpdateStep("PromptForUpdateIfNewer : sortie anticipee (latestVersion ou downloadUrl vide)");
                return;
            }

            if (!Version.TryParse(latestVersion.Trim(), out var serverVersion))
            {
                LogUpdateStep($"PromptForUpdateIfNewer : sortie anticipee (Version.TryParse a echoue pour \"{latestVersion}\")");
                return;
            }

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            LogUpdateStep($"PromptForUpdateIfNewer : serverVersion={serverVersion} currentVersion={currentVersion}");

            if (serverVersion <= currentVersion)
            {
                LogUpdateStep("PromptForUpdateIfNewer : sortie anticipee (serverVersion <= currentVersion)");
                return;
            }

            // ✅ La confirmation "Oui/Non" (WpfMessageBox modale) qui s'affichait ici a été
            // retirée. Le journal de diagnostic (iziregi-update-error.log) a montré qu'elle
            // se refermait systématiquement toute seule avec le résultat "No", en une
            // fraction de seconde, sans aucune action de l'utilisateur — et ce, que le code
            // soit exécuté depuis le constructeur ou depuis l'événement Loaded (donc pas un
            // simple problème de focus au démarrage). La cause exacte n'a pas pu être
            // identifiée avec certitude, mais le comportement était reproductible à 100 % et
            // bloquait TOUTE mise à jour automatique depuis la version 1.0.8.
            //
            // À la place, on affiche un bandeau non bloquant dans la fenêtre principale
            // (UpdateBanner, voir MainWindow.xaml) : l'utilisateur garde le choix de mettre
            // à jour immédiatement ou de continuer à travailler ("Plus tard" — la
            // notification réapparaîtra au prochain démarrage). Comme ce bandeau fait partie
            // de la fenêtre déjà affichée et active, il ne dépend d'aucune boîte de dialogue
            // modale séparée, ce qui évite complètement le problème ci-dessus.
            _pendingUpdateVersion = latestVersion;
            _pendingUpdateDownloadUrl = downloadUrl;
            _pendingUpdateSha256 = installerSha256;
            UpdateBannerText.Text = $"Une nouvelle version d'Iziregi est disponible ({latestVersion}).";
            UpdateBanner.Visibility = Visibility.Visible;
            LogUpdateStep($"Bandeau de mise a jour affiche pour la version {latestVersion} (url={downloadUrl})");
        }
        catch (Exception ex)
        {
            // ✅ Diagnostic : cette exception était avalée silencieusement avant (aucune
            // trace nulle part) — on la journalise maintenant aussi.
            LogUpdateStep($"ECHEC (PromptForUpdateIfNewer) : {ex.GetType().FullName}: {ex.Message}\n{ex}");
            // Ne jamais empêcher le démarrage normal de l'application à cause de la mise à jour.
        }
    }

    // ✅ Mémorise la mise à jour détectée par PromptForUpdateIfNewer, en attendant que
    // l'utilisateur clique sur "Mettre à jour maintenant" (ou "Plus tard") dans le bandeau.
    private string? _pendingUpdateVersion;
    private string? _pendingUpdateDownloadUrl;
    private string? _pendingUpdateSha256;

    // Bouton "Mettre à jour maintenant" du bandeau : lance le téléchargement + la
    // préparation de l'installateur, exactement comme avant, mais désormais uniquement
    // sur une action explicite de l'utilisateur (donc à un moment où la fenêtre a
    // certainement le focus).
    private void UpdateBannerInstall_Click(object sender, RoutedEventArgs e)
    {
        var downloadUrl = _pendingUpdateDownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return;

        UpdateBanner.Visibility = Visibility.Collapsed;
        LogUpdateStep($"Utilisateur a clique sur \"Mettre a jour maintenant\" pour la version {_pendingUpdateVersion} (url={downloadUrl})");

        // ✅ Fenêtre de progression : avant cet ajout, l'application ne montrait rien
        // pendant les 10-15 secondes de téléchargement (elle semblait figée). La fenêtre
        // s'affiche via ShowDialog() (boucle de messages imbriquée gérée par WPF), pendant
        // que le téléchargement se déroule de façon asynchrone dans son gestionnaire
        // Loaded — la fenêtre reste donc réactive et sa barre de progression se met à jour
        // en direct.
        var progressWindow = new UpdateProgressWindow();
        progressWindow.Loaded += async (_, _) =>
        {
            await DownloadAndLaunchUpdateAsync(downloadUrl!, _pendingUpdateSha256, progressWindow);
            // N'est atteint que si le téléchargement/lancement a échoué : en cas de
            // succès, DownloadAndLaunchUpdateAsync ferme déjà l'application avant
            // d'arriver ici.
            progressWindow.Close();
        };
        progressWindow.ShowDialog();
    }

    // Bouton "Plus tard" du bandeau : masque simplement la notification pour cette
    // session. L'application continue avec la version actuelle ; la notification
    // réapparaîtra au prochain démarrage tant que la mise à jour n'est pas installée.
    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        LogUpdateStep($"Utilisateur a clique sur \"Plus tard\" pour la version {_pendingUpdateVersion}");
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // Bouton "OK" du bandeau de confirmation de mise à jour réussie : le masque simplement.
    private void UpdateSuccessBannerOk_Click(object sender, RoutedEventArgs e)
    {
        UpdateSuccessBanner.Visibility = Visibility.Collapsed;
    }

    // Télécharge l'installateur depuis le serveur vers un fichier temporaire (en
    // rapportant la progression à la fenêtre fournie), le lance, puis ferme l'application
    // pour libérer les fichiers que l'installateur doit remplacer.
    // ✅ Diagnostic : journal texte (append) écrit à chaque étape importante de la mise à
    // jour, pas seulement en cas d'erreur. But : si le processus est interrompu (tué de
    // l'extérieur, par exemple par un antivirus) avant même d'atteindre un bloc catch, la
    // dernière ligne écrite dans ce fichier montre quand même jusqu'où on est allé.
    private static readonly string UpdateLogPath = Path.Combine(Path.GetTempPath(), "iziregi-update-error.log");

    private static void LogUpdateStep(string msg)
    {
        try
        {
            File.AppendAllText(UpdateLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { /* le journal lui-même ne doit jamais faire planter l'app */ }
    }

    private static async Task DownloadAndLaunchUpdateAsync(string downloadUrl, string? expectedSha256, UpdateProgressWindow? progressWindow = null)
    {
        try
        {
            LogUpdateStep($"Debut telechargement depuis {downloadUrl}");
            var tempPath = Path.Combine(Path.GetTempPath(), "IziregiSetup.exe");

            using (var resp = await DownloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                LogUpdateStep($"Reponse recue : HTTP {(int)resp.StatusCode} ({resp.StatusCode})");
                resp.EnsureSuccessStatusCode();
                var totalBytes = resp.Content.Headers.ContentLength;
                LogUpdateStep($"Taille annoncee par le serveur : {totalBytes} octets");

                await using var httpStream = await resp.Content.ReadAsStreamAsync();
                await using var fs = File.Create(tempPath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    progressWindow?.ReportProgress(totalRead, totalBytes);
                }
                LogUpdateStep($"Telechargement termine : {totalRead} octets ecrits dans {tempPath}");
            }

            // ✅ Vérification d'intégrité (17.07.2026, revue de sécurité) : le hash SHA-256
            // publié par le serveur (voir /internal/ping, champ installerSha256) est comparé
            // au fichier réellement téléchargé AVANT tout lancement. Si le serveur n'a pas
            // encore publié de hash (déploiement en transition, ancien serveur), on ne bloque
            // pas la mise à jour — mais si un hash EST fourni et ne correspond pas, on refuse
            // net : mieux vaut une mise à jour manquée qu'un exécutable altéré lancé en silence.
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualSha256;
                await using (var verifyStream = File.OpenRead(tempPath))
                {
                    var hashBytes = await SHA256.HashDataAsync(verifyStream);
                    actualSha256 = Convert.ToHexString(hashBytes);
                }

                LogUpdateStep($"Verification SHA-256 : attendu={expectedSha256} obtenu={actualSha256}");

                if (!string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    LogUpdateStep("ECHEC : le hash SHA-256 de l'installateur telecharge ne correspond pas -- installation refusee.");
                    try { File.Delete(tempPath); } catch { /* non bloquant */ }

                    WpfMessageBox.Show(
                        "La mise à jour téléchargée n'a pas pu être vérifiée (hash SHA-256 incorrect) et n'a donc pas été installée, par précaution.\n\nRéessayez plus tard ou contactez l'éditeur Iziregi si le problème persiste.",
                        "Mise à jour",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                LogUpdateStep("Verification SHA-256 : aucun hash publie par le serveur, installation non verifiee.");
            }

            progressWindow?.SetInstalling();
            LogUpdateStep("Lancement direct de l'installateur...");

            // ✅ On relance directement l'installateur (comme un double-clic), maintenant
            // que le téléchargement est déclenché par un clic explicite de l'utilisateur
            // (bouton "Mettre à jour maintenant" du bandeau) et non plus automatiquement,
            // en silence, au démarrage de l'app. C'était très probablement ce contexte
            // "au démarrage, sans interaction" qui posait problème (comme pour la boîte
            // Oui/Non, voir PromptForUpdateIfNewer) — un lancement déclenché par un vrai
            // clic utilisateur se comporte normalement. Windows peut encore afficher son
            // avertissement SmartScreen habituel pour un installateur non signé (normal,
            // un seul clic "Informations complémentaires" → "Exécuter quand même" suffit,
            // exactement comme lors d'un téléchargement manuel dans le navigateur) : ce
            // n'est plus un échec silencieux, l'utilisateur voit ce qui se passe.
            // ✅ /SILENT (au lieu de /VERYSILENT) /SUPPRESSMSGBOXES /NORESTART : l'assistant
            // d'installation (Suivant/Suivant/Installer/Terminer) ne s'affiche plus du tout,
            // mais Inno Setup affiche quand même sa propre fenêtre "Installation en cours..."
            // avec une barre de progression, sans aucun clic requis — pour éviter que
            // l'utilisateur se demande ce qui se passe pendant les quelques secondes où rien
            // n'est visible (/VERYSILENT n'affiche absolument rien). L'installateur n'a pas
            // besoin des droits administrateur (PrivilegesRequired=lowest dans Iziregi.iss,
            // installation dans %LOCALAPPDATA%), donc pas d'invite Windows "Contrôle de
            // compte utilisateur" à valider. L'application se relance automatiquement une
            // fois l'installation terminée (voir Iziregi.iss, section [Run]).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            });

            // ✅ Message explicite avant la fermeture (voir SetClosingForInstall) : l'app
            // doit se fermer pour que l'installateur puisse remplacer ses fichiers, ce qui
            // crée quelques secondes sans aucune fenêtre visible (l'installateur extrait son
            // contenu avant même d'afficher sa propre barre de progression). On laisse le
            // temps à l'utilisateur de lire ce message avant que la fenêtre ne disparaisse.
            progressWindow?.SetClosingForInstall();
            LogUpdateStep("Installateur lance : fermeture de l'application pour liberer les fichiers.");
            await Task.Delay(2500);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // ✅ Diagnostic : en plus du message affiché (qui peut disparaître trop vite
            // pour être lu pendant un test), on note l'erreur complète dans le journal —
            // consultable ensuite sans contrainte de temps.
            LogUpdateStep($"ECHEC : {ex.GetType().FullName}: {ex.Message}\n{ex}");

            WpfMessageBox.Show(
                "Le téléchargement de la mise à jour a échoué : " + ex.Message + "\n\nL'application va continuer avec la version actuelle.\n\nDétail écrit dans : %TEMP%\\iziregi-update-error.log",
                "Mise à jour",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string NormalizeServerBaseUrl(string? baseUrl)
    {
        var v = (baseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) v = "https://iziregi.com";
        return v.TrimEnd('/');
    }

    private static bool TryParseUtc(string? s, out DateTime utc)
    {
        utc = default;

        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return false;

        if (!DateTime.TryParse(
                s,
                null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt))
            return false;

        utc = dt.ToUniversalTime();
        return true;
    }

    // ✅ Utilise la vraie date de dernière sync (première fois : tout depuis 2000)
    private static string GetSinceUtcForQuery()
    {
        var stored = Db.GetLastServerSyncUtc();
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;
        return "2000-01-01T00:00:00Z";
    }

    private async Task ServerSyncTickAsync()
    {
        if (_isServerSyncRunning) return;
        _isServerSyncRunning = true;

        try
        {
            if (string.IsNullOrWhiteSpace(ServerApiKey))
                return;

            var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
            var sinceUtc = GetSinceUtcForQuery();

            var url = $"{baseUrl}/internal/submissions/since?sinceUtc={Uri.EscapeDataString(sinceUtc)}";

            HttpResponseMessage? resp = null;
            try
            {
                resp = await GetWithApiKeyAsync(Http, url);
            }
            catch
            {
                return;
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                    return;

                var json = await resp.Content.ReadAsStringAsync();
                json = (json ?? "").Trim();

                if (json.Length == 0)
                    return;

                // ✅ Le serveur peut renvoyer soit:
                // - un ARRAY directement: [ {..}, {..} ]
                // - ou un objet enveloppe: { "ok": true, "items": [ ... ] }
                List<ServerSubmission> items;
                try
                {
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        items = JsonSerializer.Deserialize<List<ServerSubmission>>(json, JsonOptions) ?? new List<ServerSubmission>();
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                             doc.RootElement.TryGetProperty("items", out var itemsEl) &&
                             itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        items = JsonSerializer.Deserialize<List<ServerSubmission>>(itemsEl.GetRawText(), JsonOptions) ?? new List<ServerSubmission>();
                    }
                    else
                    {
                        return;
                    }
                }
                catch
                {
                    return;
                }

                if (items.Count == 0)
                {
                    Db.SetLastServerSyncUtc(DateTime.UtcNow.ToString("o"));
                    return;
                }

                foreach (var it in items)
                {
                    // ✅ Isolation par soumission : si UNE soumission plante pendant son
                    // traitement, elle ne doit plus jamais bloquer tout le lot ni empêcher
                    // le curseur de synchro (LastServerSyncUtc) d'avancer. Sans ça, l'appli
                    // pouvait rester bloquée indéfiniment sur le même lot à chaque tick de
                    // 60s, sans aucune erreur visible (ni popup, ni log), même après un
                    // redémarrage — puisque le curseur est persisté en base locale.
                    try
                    {
                        await ApplySubmissionToLocalDbAsync(baseUrl, ServerApiKey, it);
                    }
                    catch (Exception itemEx)
                    {
                        LogSyncError($"{it.Role}:{it.WorkOrderRef}:{it.SubmittedAtUtc:O} -> {itemEx}");
                    }
                }

                Db.SetLastServerSyncUtc(DateTime.UtcNow.ToString("o"));

                if (MainContent.Content is IReloadablePage p)
                    p.Reload();
            }
        }
        catch
        {
            // MVP : pas de crash, on réessaie au prochain tick
        }
        finally
        {
            _isServerSyncRunning = false;
        }
    }

    private sealed class ServerSubmission
    {
        public string Role { get; set; } = "";              // "company" | "signer"
        public string WorkOrderRef { get; set; } = "";       // ex: "19-P1"
        public DateTime SubmittedAtUtc { get; set; }          // UTC
        public string Summary { get; set; } = "";

        // ✅ "data" peut contenir des valeurs string OU des objets JSON (ex: payload)
        public JsonElement Data { get; set; }
    }

    private static bool TryParseWorkOrderRef(string workOrderRef, out int bdrNumber, out long projectIdFromRef)
    {
        bdrNumber = 0;
        projectIdFromRef = 0;

        var s = (workOrderRef ?? "").Trim(); // "19-P1"
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], out bdrNumber)) return false;

        var p = parts[1].Trim(); // "D1" (ou "P1" pour les anciens bons créés avant le renommage)
        var i = 0;
        while (i < p.Length && char.IsLetter(p[i])) i++;
        p = p.Substring(i);

        return long.TryParse(p, out projectIdFromRef);
    }

    private static WorkOrder? FindLocalWorkOrderRobust(string workOrderRef)
    {
        if (!TryParseWorkOrderRef(workOrderRef, out var bdr, out var projectIdFromRef))
            return null;

        try
        {
            var strictList = Db.GetWorkOrders(projectIdFromRef);
            var strict = strictList.FirstOrDefault(w => w.BdrNumber == bdr);
            if (strict != null) return strict;
        }
        catch
        {
            // ignore => fallback
        }

        try
        {
            var all = Db.GetWorkOrders();
            var matches = all.Where(w => w.BdrNumber == bdr).ToList();

            if (matches.Count == 1)
                return matches[0];

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class CompanyQuoteLinePayload
    {
        public string Label { get; set; } = "";
        public double Qty { get; set; }
        public double UnitPrice { get; set; }
    }

    private sealed class CompanyQuotePayload
    {
        public string QuoteName { get; set; } = "";
        public string QuoteDateIso { get; set; } = "";
        public string Note { get; set; } = "";

        public List<CompanyQuoteLinePayload> Lines { get; set; } = new();

        public double LaborHours { get; set; }
        public double LaborRate { get; set; }
        public double TravelQty { get; set; }
        public double TravelRate { get; set; }

        public double ForfaitHt { get; set; }

        // ✅ Forfait TTC (21.07.2026, réplique BDR razor) : montant TTC saisi directement par
        // l'entreprise sur la page web, remplace ForfaitHt (qty*prix) pour les devis qui
        // utilisent ce mode. Voir WorkOrder.ForfaitTtc / WorkOrderWindow.RecomputeTotals.
        public double ForfaitTtc { get; set; }

        public double DiscountRate { get; set; }
        public double TvaRate { get; set; }

        public double TotalHtNet { get; set; }
        public double TotalTtc { get; set; }
    }

    // ✅ Retrouve une fenêtre WorkOrderWindow déjà ouverte pour un bon donné (utilisé pour
    // rafraîchir l'affichage après une synchronisation serveur en tâche de fond).
    private WorkOrderWindow? FindOpenWorkOrderWindow(long workOrderId)
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is WorkOrderWindow wow && wow.CurrentWorkOrderId == workOrderId)
                return wow;
        }
        return null;
    }

    // ✅ Détermine la fenêtre à utiliser comme propriétaire pour le pop-up "Réponse
    // entreprise/signataire reçue" : si le bon concerné est déjà ouvert dans une
    // WorkOrderWindow (modale via ShowDialog), on affiche le pop-up par-dessus CETTE
    // fenêtre (et on l'active pour qu'elle soit au premier plan). Sinon on retombe sur
    // MainWindow. Sans ça, le pop-up pouvait s'ouvrir caché derrière la WorkOrderWindow
    // déjà ouverte (propriétaire = MainWindow, désactivée par la modale en cours),
    // bloquant silencieusement la suite (changement de statut) tant qu'on ne le trouvait
    // pas par hasard.
    private Window ShowSubmissionPopupOwner(long workOrderId)
    {
        var openWin = FindOpenWorkOrderWindow(workOrderId);
        if (openWin != null)
        {
            try { openWin.Activate(); } catch { }
            return openWin;
        }
        return this;
    }

    private async Task ApplySubmissionToLocalDbAsync(string baseUrl, string apiKey, ServerSubmission it)
    {
        var role = (it.Role ?? "").Trim().ToLowerInvariant();
        var wor = (it.WorkOrderRef ?? "").Trim();
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(wor))
            return;

        if (!TryParseWorkOrderRef(wor, out _, out _))
            return;

        // ✅ Anti-doublon : skip si déjà traité dans cette session
        var submissionKey = $"{role}:{wor}:{it.SubmittedAtUtc:O}";
        if (!_processedSubmissionKeys.Add(submissionKey))
            return;

        var wo = FindLocalWorkOrderRobust(wor);
        if (wo == null)
        {
            if (_warnedNotFoundLocal.Add(wor))
            {
                WpfMessageBox.Show(
                    this,
                    $"Sync : bon introuvable en local pour {wor}.",
                    "Iziregi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        if (role == "company")
        {
            // ✅ data = JSON object
            string? payloadJson = null;
            string? quoteName2 = null;
            string? note2 = null;
            string? forfaitPdfBase64_2 = null;
            string? forfaitPdfName_2 = null;

            try
            {
                if (it.Data.ValueKind == JsonValueKind.Object)
                {
                    if (it.Data.TryGetProperty("payload", out var payloadEl))
                    {
                        // payload peut être soit une string JSON, soit directement un objet JSON
                        if (payloadEl.ValueKind == JsonValueKind.String)
                            payloadJson = payloadEl.GetString();
                        else if (payloadEl.ValueKind == JsonValueKind.Object || payloadEl.ValueKind == JsonValueKind.Array)
                            payloadJson = payloadEl.GetRawText();
                    }

                    if (it.Data.TryGetProperty("quoteName", out var quoteNameEl) && quoteNameEl.ValueKind == JsonValueKind.String)
                        quoteName2 = quoteNameEl.GetString();

                    if (it.Data.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
                        note2 = noteEl.GetString();

                    // ✅ PDF forfait annexé par l'entreprise
                    if (it.Data.TryGetProperty("forfaitPdfBase64", out var fpdfEl) && fpdfEl.ValueKind == JsonValueKind.String)
                        forfaitPdfBase64_2 = fpdfEl.GetString();

                    if (it.Data.TryGetProperty("forfaitPdfName", out var fnameEl) && fnameEl.ValueKind == JsonValueKind.String)
                        forfaitPdfName_2 = fnameEl.GetString();
                }
            }
            catch
            {
                // ignore
            }

            // ✅ Nouveau : applique le devis complet depuis payload JSON (si présent)
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<CompanyQuotePayload>(payloadJson, JsonOptions);
                    if (payload != null)
                    {
                        wo.QuoteName = (payload.QuoteName ?? "").Trim();
                        wo.QuoteNotes = payload.Note ?? "";

                        // ✅ Date du devis envoyée par l'entreprise (QuoteDateIso) : auparavant jamais
                        // appliquée à wo.QuoteDate, donc "la date n'est pas mentionnée" côté Architecte.
                        if (!string.IsNullOrWhiteSpace(payload.QuoteDateIso) &&
                            DateTime.TryParse(payload.QuoteDateIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var quoteDateParsed))
                        {
                            wo.QuoteDate = quoteDateParsed.Date;
                        }

                        wo.LaborHours = payload.LaborHours;
                        wo.LaborRate = payload.LaborRate;
                        wo.TravelQty = payload.TravelQty;
                        wo.TravelRate = payload.TravelRate;

                        // forfait HT (legacy) : qty=1, unitPrice=ForfaitHt
                        wo.ForfaitQty = payload.ForfaitHt != 0 ? 1 : 0;
                        wo.ForfaitUnitPrice = payload.ForfaitHt;

                        // ✅ Forfait TTC (21.07.2026, réplique BDR razor) : appliqué tel quel, prime
                        // sur le forfait HT legacy si l'entreprise a utilisé ce mode (les deux ne
                        // sont normalement jamais non-zéro en même temps côté formulaire web).
                        wo.ForfaitTtc = payload.ForfaitTtc;

                        wo.DiscountRate = payload.DiscountRate;
                        wo.TvaRate = payload.TvaRate;

                        Db.UpdateWorkOrderQuote(wo);

                        // lignes matériel
                        var existing = Db.GetWorkOrderLines(wo.Id);
                        foreach (var l in existing)
                            Db.DeleteWorkOrderLine(l.Id);

                        if (payload.Lines != null)
                        {
                            foreach (var l in payload.Lines)
                                Db.InsertWorkOrderLine(wo.Id, l.Label ?? "", l.Qty, l.UnitPrice);
                        }
                    }
                }
                catch
                {
                    // fallback minimal (si payload invalide)
                    if (!string.IsNullOrWhiteSpace(quoteName2))
                        wo.QuoteName = quoteName2.Trim();

                    wo.QuoteNotes = note2 ?? "";

                    Db.UpdateWorkOrderQuote(wo);
                }
            }
            else
            {
                // fallback minimal
                if (!string.IsNullOrWhiteSpace(quoteName2))
                    wo.QuoteName = quoteName2.Trim();

                wo.QuoteNotes = note2 ?? "";

                Db.UpdateWorkOrderQuote(wo);
            }

            // ✅ PDF forfait annexé par l'entreprise : enregistrement local
            if (!string.IsNullOrWhiteSpace(forfaitPdfBase64_2))
            {
                try
                {
                    var b64 = forfaitPdfBase64_2;
                    var commaIdx = b64.IndexOf(',');
                    if (commaIdx >= 0) b64 = b64[(commaIdx + 1)..];
                    var pdfBytes = Convert.FromBase64String(b64);
                    var pdfName = string.IsNullOrWhiteSpace(forfaitPdfName_2) ? "forfait.pdf" : forfaitPdfName_2.Trim();

                    wo.ForfaitPdfFileName = pdfName;
                    wo.ForfaitPdfFileBytes = pdfBytes;
                    Db.UpdateWorkOrderForfaitPdf(wo.Id, pdfName, pdfBytes);
                }
                catch
                {
                    // on continue sans bloquer la réception du devis
                }
            }

            // ✅ Le statut "Devis reçu" ne doit être appliqué qu'après le clic sur "Ok" du pop-up
            // (WpfMessageBox.Show est bloquant : le code qui suit ne s'exécute qu'après le clic).
            //
            // ⚠️ Si le bon est déjà ouvert dans une fenêtre WorkOrderWindow (modale, ShowDialog),
            // afficher ce MessageBox avec "this" (MainWindow, désactivée par la modale) comme
            // propriétaire peut le faire apparaître DERRIÈRE la fenêtre déjà ouverte : invisible,
            // il bloque alors silencieusement la suite du code (changement de statut + reload)
            // jusqu'à ce que l'utilisateur la découvre par hasard. On utilise donc la fenêtre
            // déjà ouverte pour ce bon comme propriétaire quand elle existe, et on l'active pour
            // garantir que le pop-up apparaisse bien au premier plan, visible et cliquable.
            NotifyNewSubmission();
            var quoteOwnerWin = ShowSubmissionPopupOwner(wo.Id);
            WpfMessageBox.Show(
                quoteOwnerWin,
                $"Réponse entreprise reçue pour {wor}",
                "Iziregi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Db.SetStageQuoteReceived(wo.Id);

            // ✅ Si le bon est déjà ouvert dans une fenêtre (WorkOrderWindow), on la rafraîchit
            // pour que les lignes de devis / PDF forfait reçus apparaissent sans devoir
            // fermer puis rouvrir la fenêtre. Sinon, on ouvre le bon d'office (comme pour la
            // validation signataire) pour garantir que le Dashboard affiche bien "Devis reçu"
            // immédiatement après le clic sur "Ok", sans devoir l'ouvrir manuellement.
            var openQuoteWin = FindOpenWorkOrderWindow(wo.Id);
            if (openQuoteWin != null)
                openQuoteWin.ReloadAfterServerSync();
            else
                OpenWorkOrder(wo.Id, WorkOrderEditMode.Architecte);

            return;
        }

        if (role == "signer")
        {
            string? decision = null;
            string? name = null;
            string? dateStr = null;

            try
            {
                if (it.Data.ValueKind == JsonValueKind.Object)
                {
                    if (it.Data.TryGetProperty("decision", out var dEl) && dEl.ValueKind == JsonValueKind.String)
                        decision = dEl.GetString();

                    if (it.Data.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                        name = nEl.GetString();

                    if (it.Data.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
                        dateStr = dateEl.GetString();
                }
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrWhiteSpace(decision))
            {
                // ✅ Traduit les valeurs anglaises du serveur en français (attendu par le WPF)
                var frDecision = decision.ToLowerInvariant() switch
                {
                    "validated" => "Validé",
                    "refused" => "Refusé",
                    "cancelled" => "Annulé",
                    _ => decision
                };
                Db.UpdateWorkOrderValidationDecision(wo.Id, frDecision);
                wo.ValidationDecision = frDecision;
            }

            if (!string.IsNullOrWhiteSpace(name))
                wo.SignatureName = name.Trim();

            if (!string.IsNullOrWhiteSpace(dateStr))
            {
                if (DateTime.TryParse(dateStr, out var dt))
                    wo.SignatureDate = dt.Date;
            }

            // ✅ Lit la signature directement depuis le JSON de la soumission (plus fiable)
            try
            {
                if (it.Data.TryGetProperty("signatureBase64", out var sigB64El) &&
                    sigB64El.ValueKind == JsonValueKind.String)
                {
                    var b64 = sigB64El.GetString() ?? "";
                    var commaIdx = b64.IndexOf(',');
                    if (commaIdx >= 0) b64 = b64[(commaIdx + 1)..];
                    if (!string.IsNullOrWhiteSpace(b64) && b64.Length > 100)
                    {
                        try { wo.SignaturePng = Convert.FromBase64String(b64); } catch { }
                    }
                }
            }
            catch
            {
                // on continue sans bloquer la validation
            }

            Db.UpdateWorkOrderSignatureRaw(wo);

            // ✅ Le statut "Validé" ne doit être appliqué qu'après le clic sur "Ok" du pop-up
            // (WpfMessageBox.Show est bloquant : le code qui suit ne s'exécute qu'après le clic).
            //
            // ⚠️ Même remarque que côté "company" : si le bon est déjà ouvert dans une
            // WorkOrderWindow modale, ce pop-up doit être affiché par-dessus CETTE fenêtre
            // (et pas par-dessus MainWindow, désactivée/masquée par la modale), sinon il
            // apparaît caché derrière et bloque silencieusement le changement de statut tant
            // qu'on ne l'a pas découvert et fermé. C'est ce qui causait "je clique sur le pop
            // up et le statut ne change pas directement" : le VRAI pop-up bloquant était
            // invisible, donc le code qui suit (SetStageValidated + reload) ne s'exécutait pas.
            NotifyNewSubmission();
            var signerOwnerWin = ShowSubmissionPopupOwner(wo.Id);
            WpfMessageBox.Show(
                signerOwnerWin,
                $"Réponse signataire reçue pour {wor}",
                "Iziregi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Db.SetStageValidated(wo.Id);

            // ✅ Dès que l'architecte clique sur "Ok" du pop-up, le Bdr concerné s'ouvre
            // d'office (au lieu d'obliger l'architecte à aller le consulter manuellement
            // pour que le statut "Validé" apparaisse). S'il est déjà ouvert, on se contente
            // de le rafraîchir pour que la signature reçue apparaisse.
            var openWin = FindOpenWorkOrderWindow(wo.Id);
            if (openWin != null)
                openWin.ReloadAfterServerSync();
            else
                OpenWorkOrder(wo.Id, WorkOrderEditMode.Architecte);

            return;
        }
    }

    // =========================
    // Notifications soumission : son + flash barre des tâches
    // =========================
    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    private void NotifyNewSubmission()
    {
        // Son système discret
        try { SystemSounds.Asterisk.Play(); } catch { }

        // Flash de l'icône dans la barre des tâches (si fenêtre minimisée ou en arrière-plan)
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) FlashWindow(hwnd, true);
        }
        catch { }
    }

    // =========================
    // Badge projet (affiché sauf Dashboard)
    // =========================
    private static MediaBrush BrushFromHexOrNull(string? hex)
    {
        try
        {
            var s = (hex ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return MediaBrushes.Transparent;

            if (!s.StartsWith("#", StringComparison.Ordinal))
                s = "#" + s;

            var obj = new MediaBrushConverter().ConvertFromString(s);
            return obj is MediaBrush b ? b : MediaBrushes.Transparent;
        }
        catch
        {
            return MediaBrushes.Transparent;
        }
    }

    private static bool TryGetSolidColor(MediaBrush? brush, out MediaColor color)
    {
        if (brush is MediaSolidColorBrush scb)
        {
            color = scb.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static MediaBrush GetTextBrushForBackground(MediaBrush? bg)
    {
        if (bg == null)
            return MediaBrushes.White;

        if (!TryGetSolidColor(bg, out var c))
            return MediaBrushes.White;

        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        return luminance >= 0.55 ? MediaBrushes.Black : MediaBrushes.White;
    }

    private void UpdateProjectBadge(bool show)
    {
        var panel = this.FindName("ProjectSelectorPanel") as System.Windows.Controls.Panel;
        if (panel != null)
            panel.Visibility = Visibility.Visible;

        var manageBtn = this.FindName("ManageProjectsButton") as System.Windows.Controls.Button;
        if (manageBtn != null)
        {
            manageBtn.Visibility = (MainContent.Content is DashboardPage) ? Visibility.Visible : Visibility.Collapsed;
        }

        var p = Db.GetCurrentProject();

        var cb = this.FindName("ProjectSelectorComboBox") as System.Windows.Controls.ComboBox;
        if (cb != null)
        {
            if (p != null)
                cb.SelectedValue = p.Id;

            var bg = p != null ? BrushFromHexOrNull(p.ColorHex) : MediaBrushes.Transparent;
            if (bg == null || bg == MediaBrushes.Transparent)
                bg = BrushFromHexOrNull("#111827");

            try
            {
                cb.Background = bg;
                cb.Foreground = GetTextBrushForBackground(bg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RefreshProjectSelector exception: " + ex);
            }
        }
    }

    public void RefreshProjectSelector()
    {
        try
        {
            var projects = Db.GetProjects();
            var cb = this.FindName("ProjectSelectorComboBox") as System.Windows.Controls.ComboBox;
            var border = this.FindName("ProjectSelectorBorder") as System.Windows.Controls.Border;

            if (cb != null)
            {
                cb.ItemsSource = null;
                cb.ItemsSource = projects;

                var p = Db.GetCurrentProject();
                if (p != null)
                    cb.SelectedValue = p.Id;

                cb.UpdateLayout();
                try { cb.ApplyTemplate(); } catch { }
            }

            try
            {
                var p2 = Db.GetCurrentProject();
                var bg = p2 != null ? BrushFromHexOrNull(p2.ColorHex) : MediaBrushes.Transparent;
                if (bg == null || bg == MediaBrushes.Transparent)
                    bg = BrushFromHexOrNull("#111827");

                var btn = this.FindName("ProjectSelectorButton") as System.Windows.Controls.Button;
                if (btn != null)
                {
                    try { btn.Background = bg; } catch { }
                    try { btn.Foreground = GetTextBrushForBackground(bg); } catch { }
                    try { btn.Content = p2 != null ? (p2.Name ?? "Dossier") : "Dossier"; } catch { }
                    try { btn.UpdateLayout(); } catch { }
                }
            }
            catch { }

            UpdateProjectBadge(show: true);
            try { System.Diagnostics.Debug.WriteLine($"RefreshProjectSelector: currentDbId={Db.GetCurrentProjectId()} currentSelected={_selectedProject?.Id}/{_selectedProject?.Name}"); } catch { }
        }
        catch { }
    }

    // =========================
    // Navigation menu handlers
    // =========================
    private void NavOverview_Click(object sender, RoutedEventArgs e) => ShowOverview();
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void NavAccounting_Click(object sender, RoutedEventArgs e) => ShowAccounting();

    // ✅ Sous-menus Archives/Corbeille (04.08.2026, demande de Joe : "où sont les sous-menus ?"
    // -- les avoir seulement dans le contenu des pages n'était pas assez visible, il fallait
    // que ce soit accessible depuis la barre de navigation, comme l'ancien "Corbeille ▾"). Les
    // petites flèches ▾ ouvrent un Popup séparé, le clic direct sur "Planification"/"Bons
    // d'intervention" reste inchangé (va toujours à la page elle-même).
    private void PlanningSubMenuButton_Click(object sender, RoutedEventArgs e) => PlanningSubMenuPopup.IsOpen = !PlanningSubMenuPopup.IsOpen;
    private void PlanningSubMenuArchives_Click(object sender, RoutedEventArgs e) { PlanningSubMenuPopup.IsOpen = false; ShowArchivesTasks(); }
    private void PlanningSubMenuTrash_Click(object sender, RoutedEventArgs e) { PlanningSubMenuPopup.IsOpen = false; ShowTrashedTasks(); }

    private void BonsSubMenuButton_Click(object sender, RoutedEventArgs e) => BonsSubMenuPopup.IsOpen = !BonsSubMenuPopup.IsOpen;
    private void BonsSubMenuArchives_Click(object sender, RoutedEventArgs e) { BonsSubMenuPopup.IsOpen = false; ShowArchives(); }
    private void BonsSubMenuTrash_Click(object sender, RoutedEventArgs e) { BonsSubMenuPopup.IsOpen = false; ShowTrash(); }
    private void NavLists_Click(object sender, RoutedEventArgs e) => ShowLists();
    private void NavPlanning_Click(object sender, RoutedEventArgs e) => ShowPlanning();

    // ✅ Carnet d'adresses (04.08.2026, demande de Joe) : accessible depuis n'importe quelle
    // page (barre de navigation globale).
    // ✅ 4e passe (04.08.2026, demande de Joe : "pleine page par défaut" + "menu navigation
    // visible") : devient une page embarquée (ShowAddressBook, même principe que ShowLists/
    // ShowSettings) au lieu d'une fenêtre modale séparée.
    private void NavAddressBook_Click(object sender, RoutedEventArgs e) => ShowAddressBook();

    // ✅ 01.08.2026 (demande de Joe) : "Paramètres" ouvrait auparavant ConfigSetupWindow en
    // fenêtre modale -- devient une page embarquée (SettingsPage) avec un sous-menu regroupant
    // Identité société / Banque de dossiers / Connexion / Démarrage / Vos données / Éthique.
    // ConfigSetupWindow elle-même reste inchangée, toujours utilisée pour la configuration
    // initiale obligatoire au 1er lancement.
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    // ✅ Aide (24.07.2026, demande de Joe) : ouvre la page d'aide (mode d'emploi PDF, destiné à
    // l'architecte — entreprises/signataires n'en ont pas besoin) dans le navigateur par défaut.
    // ✅ 01.08.2026 (demande de Joe) : internal -- appelée aussi depuis SettingsPage (sous-menu
    // "Paramètres" > "Mode d'emploi"), plus de bouton séparé dans la barre de nav principale.
    internal void NavHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{baseUrl}/aide") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’ouvrir la page d’aide.\n\n{ex.Message}",
                "Aide",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ✅ Contact (29.07.2026, demande de Joe) : même principe que NavHelp_Click ci-dessus.
    private void NavContact_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{baseUrl}/contact") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’ouvrir la page de contact.\n\n{ex.Message}",
                "Contact",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ✅ CGU (29.07.2026, demande de Joe) : même principe que NavHelp_Click. Internal (01.08.2026)
    // -- appelée aussi depuis SettingsPage (sous-menu "Paramètres" > "CGU").
    internal void NavCgvEssai_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{baseUrl}/cgu") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’ouvrir la page des CGV.\n\n{ex.Message}",
                "CGV",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ✅ BUG (grilles/images/stickers du Planning perdus au changement de page) : on ne
    // peut pas se fier uniquement à l'événement Unloaded de PlanningPage pour sauvegarder
    // avant de quitter — pas assez fiable dans ce contexte. On force donc une sauvegarde
    // explicite et synchrone AVANT chaque changement de page, tant que la page actuellement
    // affichée est bien le Planning (no-op sinon).
    private void FlushPlanningIfActive()
    {
        try { (MainContent.Content as PlanningPage)?.FlushPendingChanges(); } catch { }
    }

    // ✅ Modernisation du look (13.07.2026) : met en évidence l'onglet de navigation
    // correspondant à la page actuellement affichée (fond bleu, texte blanc), les autres
    // repassent au style neutre. Appelé au début de chaque Show*() ci-dessous.
    private void SetActiveNavButton(System.Windows.Controls.Button active)
    {
        var all = new[]
        {
            NavOverviewButton, NavDashboardButton, NavAccountingButton,
            NavListsButton, NavPlanningButton, NavAddressBookButton, NavSettingsButton
        };

        foreach (var b in all)
        {
            b.Style = (Style)FindResource(b == active ? "NavPillButtonActiveStyle" : "NavPillButtonStyle");
        }
    }

    // ✅ Page par défaut au démarrage, par dossier (25.07.2026, demande de Joe). Clés
    // possibles : "" (vide, nouveau Tableau de bord/résumé), "Bons", "Archives", "Corbeille",
    // "Listes", "Comptabilité", "Planning" -- voir ConfigSetupWindow, onglet "Démarrage".
    // ✅ 31.07.2026 (demande de Joe) : le nouveau Tableau de bord (résumé général, voir
    // OverviewPage) devient le vrai défaut (clé vide) -- l'ancienne page (liste des bons,
    // ex-"Tableau de bord") a maintenant sa propre clé explicite "Bons".
    internal void ShowDefaultStartupPage()
    {
        var projectId = _selectedProject?.Id ?? 0;
        var page = projectId > 0 ? Db.GetDefaultStartupPage(projectId) : "";

        switch (page)
        {
            case "Bons": ShowDashboard(); break;
            case "Archives": ShowArchives(); break;
            case "Corbeille": ShowTrash(); break;
            case "Listes": ShowLists(); break;
            case "Comptabilité": ShowAccounting(); break;
            case "Planning": ShowPlanning(); break;
            default: ShowOverview(); break;
        }
    }

    // ✅ Rétabli (28.07.2026, demande de Joe) : la page Listes s'enregistre désormais via un
    // bouton "Enregistrer" global (voir ListsPage.HasUnsavedChanges/SaveAllNow) plutôt
    // qu'automatiquement -- prévient donc à nouveau une sortie de page avec des changements
    // en attente.
    // ✅ 29.07.2026 (demande de Joe) : message standard "Voulez-vous enregistrer les
    // modifications ?" (Oui/Non/Annuler) plutôt qu'un simple "quitter quand même ?" --
    // Oui enregistre puis quitte, Non quitte sans enregistrer, Annuler reste sur la page.
    private bool ConfirmLeaveListsPageIfDirty()
    {
        if (MainContent.Content is ListsPage lp && lp.HasUnsavedChanges)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "Voulez-vous enregistrer les modifications ?",
                "Listes",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Cancel)
                return false;

            if (result == System.Windows.MessageBoxResult.Yes)
                lp.SaveAllNow();

            return true;
        }
        return true;
    }

    internal void ShowOverview()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavOverviewButton);
        _overviewPage ??= new OverviewPage(this);
        MainContent.Content = _overviewPage;
        _overviewPage.Reload();

        UpdateProjectBadge(show: true);
    }

    internal void ShowDashboard()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavDashboardButton);
        _dashboardPage ??= new DashboardPage(this);
        MainContent.Content = _dashboardPage;
        _dashboardPage.Reload();

        UpdateProjectBadge(show: true);
    }

    internal void ShowAccounting()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavAccountingButton);
        _accountingPage ??= new AccountingPage(this);
        MainContent.Content = _accountingPage;
        _accountingPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // ✅ Archives/Corbeille (04.08.2026, demande de Joe) : retirées du menu global, deviennent
    // un sous-menu de leur page respective ("Bons archivés"/"Bons Corbeille" cliquables sur
    // Bons d'intervention, boutons "Archives"/"Corbeille" sur Planification). Comme il n'y a
    // plus de bouton dédié dans le menu principal, l'onglet PARENT reste surligné (Bons
    // d'intervention pour la variante Bons, Planification pour la variante Tâches) -- repère
    // visuel du contexte d'origine plutôt qu'aucun surlignage.
    internal void ShowArchives()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavDashboardButton);
        _archivesPage ??= new ArchivesPage(this);
        MainContent.Content = _archivesPage;
        _archivesPage.Reload();

        UpdateProjectBadge(show: true);
    }

    internal void ShowArchivesTasks()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavPlanningButton);
        _archivesTasksPage ??= new ArchivesTasksPage(this);
        MainContent.Content = _archivesTasksPage;
        _archivesTasksPage.Reload();

        UpdateProjectBadge(show: true);
    }

    internal void ShowTrash()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavDashboardButton);
        _trashPage ??= new TrashPage(this);
        MainContent.Content = _trashPage;
        _trashPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // ✅ Fix (demande de Joe : "il manque la ligne Corbeille" dans le widget Tâches) : même
    // principe que ShowArchivesTasks, pour TrashedTasksPage.
    internal void ShowTrashedTasks()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavPlanningButton);
        _trashedTasksPage ??= new TrashedTasksPage(this);
        MainContent.Content = _trashedTasksPage;
        _trashedTasksPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void ShowLists()
    {
        FlushPlanningIfActive();
        SetActiveNavButton(NavListsButton);
        _listsPage ??= new ListsPage(this);
        MainContent.Content = _listsPage;
        _listsPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // ✅ internal (27e passe, demande de Joe) : appelée aussi depuis OverviewPage (titre
    // cliquable du widget "Tâches", lien vers la page Planification).
    internal void ShowPlanning()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavPlanningButton);
        _planningPage = new PlanningPage(this);
        MainContent.Content = _planningPage;
        _planningPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // ✅ 01.08.2026 (demande de Joe) : "Paramètres" en page embarquée avec sous-menu (Identité
    // société / Banque de dossiers / Connexion / Démarrage / Vos données / Éthique), remplace
    // le bouton "Identité Société" séparé et l'ouverture modale de ConfigSetupWindow.
    internal void ShowSettings()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavSettingsButton);
        _settingsPage ??= new Pages.SettingsPage(this);
        MainContent.Content = _settingsPage;
        _settingsPage.Reload();

        UpdateProjectBadge(show: true);
    }

    // ✅ Carnet d'adresses (04.08.2026, demande de Joe) : page embarquée, même principe que
    // ShowSettings ci-dessus.
    internal void ShowAddressBook()
    {
        if (!ConfirmLeaveListsPageIfDirty()) return;
        FlushPlanningIfActive();
        SetActiveNavButton(NavAddressBookButton);
        _addressBookPage ??= new Pages.AddressBookPage(this);
        MainContent.Content = _addressBookPage;
        _addressBookPage.Reload();

        UpdateProjectBadge(show: true);
    }

    private void PlanningPdf_Click(object sender, RoutedEventArgs e)
    {
        // ✅ Auparavant un simple message "TODO" : branché désormais sur l'export PDF
        // déjà fonctionnel de PlanningPage (bouton "Export PDF" de la page elle-même).
        if (_planningPage != null)
        {
            _planningPage.ExportPdf();
        }
        else
        {
            System.Windows.MessageBox.Show(
                this,
                "Ouvrez d'abord la page Planification avant d'exporter le PDF.",
                "Planification",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    // =========================
    // API appelée depuis les pages
    // =========================
    public Project? GetSelectedProject() => _selectedProject;

    public void SetSelectedProject(Project? p)
    {
        _selectedProject = p;
        Db.SetCurrentProjectId(_selectedProject?.Id);

        var panelForShow = this.FindName("ProjectSelectorPanel") as System.Windows.Controls.Panel;
        var show = panelForShow != null && panelForShow.Visibility == Visibility.Visible;
        UpdateProjectBadge(show: show);
        try { RecreatePagesForProject(); } catch { }
        try { RefreshProjectSelector(); } catch { }
    }

    private void RecreatePagesForProject()
    {
        FlushPlanningIfActive();
        var current = MainContent.Content;

        var wasOverview = current is OverviewPage;
        var wasDashboard = current is DashboardPage;
        var wasAccounting = current is AccountingPage;
        var wasArchives = current is ArchivesPage;
        var wasArchivesTasks = current is ArchivesTasksPage;
        var wasTrash = current is TrashPage;
        var wasTrashedTasks = current is TrashedTasksPage;
        var wasLists = current is ListsPage;
        var wasPlanning = current is PlanningPage;
        var wasSettings = current is Pages.SettingsPage;
        var wasAddressBook = current is Pages.AddressBookPage;

        _overviewPage = null;
        _dashboardPage = null;
        _accountingPage = null;
        _archivesPage = null;
        _archivesTasksPage = null;
        _trashPage = null;
        _listsPage = null;
        _planningPage = null;
        _settingsPage = null;
        _addressBookPage = null;

        try
        {
            if (wasOverview)
            {
                _overviewPage = new OverviewPage(this);
                MainContent.Content = _overviewPage;
                _overviewPage.Reload();
                return;
            }

            if (wasDashboard)
            {
                _dashboardPage = new DashboardPage(this);
                MainContent.Content = _dashboardPage;
                _dashboardPage.Reload();
                return;
            }

            if (wasAccounting)
            {
                _accountingPage = new AccountingPage(this);
                MainContent.Content = _accountingPage;
                _accountingPage.Reload();
                return;
            }

            if (wasArchives)
            {
                _archivesPage = new ArchivesPage(this);
                MainContent.Content = _archivesPage;
                _archivesPage.Reload();
                return;
            }

            if (wasArchivesTasks)
            {
                _archivesTasksPage = new ArchivesTasksPage(this);
                MainContent.Content = _archivesTasksPage;
                _archivesTasksPage.Reload();
                return;
            }

            if (wasTrash)
            {
                _trashPage = new TrashPage(this);
                MainContent.Content = _trashPage;
                _trashPage.Reload();
                return;
            }

            if (wasTrashedTasks)
            {
                _trashedTasksPage = new TrashedTasksPage(this);
                MainContent.Content = _trashedTasksPage;
                _trashedTasksPage.Reload();
                return;
            }

            if (wasLists)
            {
                _listsPage = new ListsPage(this);
                MainContent.Content = _listsPage;
                _listsPage.Reload();
                return;
            }

            if (wasPlanning)
            {
                _planningPage = new PlanningPage(this);
                MainContent.Content = _planningPage;
                _planningPage.Reload();
                return;
            }

            if (wasSettings)
            {
                _settingsPage = new Pages.SettingsPage(this);
                MainContent.Content = _settingsPage;
                _settingsPage.Reload();
                return;
            }

            if (wasAddressBook)
            {
                _addressBookPage = new Pages.AddressBookPage(this);
                MainContent.Content = _addressBookPage;
                _addressBookPage.Reload();
                return;
            }
        }
        catch
        {
            // non bloquant
        }
    }

    public void SetSelectedProjectRecreatePlanning(Project? p)
    {
        var wasPlanningActive = MainContent.Content is PlanningPage;

        SetSelectedProject(p);

        try
        {
            _planningPage = new PlanningPage(this);

            if (wasPlanningActive)
            {
                MainContent.Content = _planningPage;
                _planningPage.Reload();
            }
        }
        catch
        {
            // non bloquant
        }
        try { RefreshProjectSelector(); } catch { }
    }

    public void SetSelectedProjectAndReload(Project? p)
    {
        SetSelectedProject(p);

        try { _listsPage?.Reload(); } catch { }
        try { _planningPage?.Reload(); _planningPage?.RefreshCompanyColors(); } catch { }
    }

    public List<WorkOrder> GetAllWorkOrders() => Db.GetWorkOrders();

    // ✅ Fix (demande de Joe : "je veux pouvoir travailler sur les 2 fenêtres sans avoir à les
    // fermer") : ShowDialog (modal) -> Show (non modal), la fenêtre principale reste utilisable
    // pendant qu'un bon est ouvert. Le rechargement de la page courante, qui se faisait après
    // la fermeture (ShowDialog bloquant jusque-là), se fait maintenant sur l'événement Closed.
    public void OpenWorkOrder(long workOrderId, WorkOrderEditMode mode)
    {
        if (WorkOrderWindow.ActivateIfAlreadyOpen(workOrderId)) return;

        var w = new WorkOrderWindow(workOrderId, mode) { Owner = this };
        w.Closed += (s, e) => { try { if (MainContent.Content is IReloadablePage p) p.Reload(); } catch { } };
        try
        {
            w.Show();
        }
        catch (Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Exception while opening WorkOrderWindow: " + ex);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
            }
            catch { }
        }
    }

    public void ChooseProject()
    {
        var w = new ChooseProjectWindow { Owner = this };
        var ok = w.ShowDialog();
        if (ok == true && w.SelectedProject != null)
        {
            _selectedProject = w.SelectedProject;
            Db.SetCurrentProjectId(_selectedProject.Id);
        }

        var panelForShow = this.FindName("ProjectSelectorPanel") as System.Windows.Controls.Panel;
        var show = panelForShow != null && panelForShow.Visibility == Visibility.Visible;
        UpdateProjectBadge(show: show);

        if (MainContent.Content is IReloadablePage p)
            p.Reload();
    }

    // =========================
    // ✅ Création nouveau bon (utilise les valeurs par défaut configurées)
    // =========================
    public void CreateNewWorkOrderAndOpen()
    {
        var projectId = Db.GetCurrentProjectId();
        _selectedProject = projectId.HasValue ? Db.GetProjectById(projectId.Value) : null;

        var defaultPlace = projectId.HasValue ? (Db.GetDefaultPlace(projectId.Value) ?? "").Trim() : "";
        var place = !string.IsNullOrWhiteSpace(defaultPlace)
            ? defaultPlace
            : (projectId.HasValue ? (Db.GetPlaces(projectId.Value).FirstOrDefault() ?? "D21") : "D21");

        var defaultCompany = projectId.HasValue ? (Db.GetDefaultCompany(projectId.Value) ?? "").Trim() : "";
        var performedBy = !string.IsNullOrWhiteSpace(defaultCompany)
            ? defaultCompany
            : (projectId.HasValue ? (Db.GetCompanies(projectId.Value).FirstOrDefault() ?? "Electricien") : "Electricien");

        var defaultRequester = projectId.HasValue ? (Db.GetDefaultRequester(projectId.Value) ?? "").Trim() : "";
        var requestedBy = !string.IsNullOrWhiteSpace(defaultRequester)
            ? defaultRequester
            : "Architecte";

        var wo = new WorkOrder
        {
            BdrNumber = projectId.HasValue ? Db.GetNextBdrNumberForProject(projectId.Value) : Db.GetNextBdrNumber(),
            Place = place,
            RequestedBy = requestedBy,
            PerformedBy = performedBy,
            RequestDate = DateTime.Today,

            Description = "",

            IsInCreation = false,
            IsSentToCompany = false,
            IsQuoteReceived = false,
            IsSentToSigner = false,
            IsValidated = false,
            IsValidatedPdfSent = false,

            IsPerformed = false,
            IsCancelled = false,

            IsTrashed = false,
            TrashedAt = null,
            IsArchived = false,
            ArchivedAt = null,

            LaborHours = 0,
            LaborRate = 0,
            TravelQty = 0,
            TravelRate = 0,
            TvaRate = 8.1,
            QuoteNotes = "",

            ProjectId = projectId
        };

        Db.InsertWorkOrder(wo);

        var created = projectId.HasValue ? Db.GetWorkOrders(projectId.Value).FirstOrDefault() : Db.GetWorkOrders().FirstOrDefault();
        if (created != null)
            Db.InsertWorkOrderLine(created.Id, "", 0, 0);

        created = projectId.HasValue ? Db.GetWorkOrders(projectId.Value).FirstOrDefault() : Db.GetWorkOrders().FirstOrDefault();
        if (created == null) return;

        OpenWorkOrder(created.Id, WorkOrderEditMode.Architecte);
    }

    // =========================
    // Import manuel (boutons de page Dashboard)
    // =========================
    public void ImportCompanyQuoteReply_ManualPicker()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer retour entreprise (devis)",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Erreur import devis",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    public void ImportSignerReply_ManualPicker()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer retour signataire",
            Filter = "Iziregi réponse (*.iziregi-reponse)|*.iziregi-reponse",
            Multiselect = false
        };

        if (ofd.ShowDialog() != true) return;

        try { ImportReplyFile(ofd.FileName); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Erreur import signataire",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    // =========================
    // Watcher setup
    // =========================
    private void StartInboxWatcher()
    {
        try
        {
            Directory.CreateDirectory(InboxDir);
            Directory.CreateDirectory(ImportedDir);
            Directory.CreateDirectory(ErrorDir);

            _inboxWatcher = new FileSystemWatcher(InboxDir, "*.iziregi-reponse")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
            };

            _inboxWatcher.Created += (_, e) => OnInboxFileDetected(e.FullPath);
            _inboxWatcher.Renamed += (_, e) => OnInboxFileDetected(e.FullPath);

            _inboxWatcher.EnableRaisingEvents = true;

            foreach (var f in Directory.GetFiles(InboxDir, "*.iziregi-reponse"))
                OnInboxFileDetected(f);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Erreur INBOX",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        try
        {
            if (_inboxWatcher != null)
            {
                _inboxWatcher.EnableRaisingEvents = false;
                _inboxWatcher.Dispose();
                _inboxWatcher = null;
            }
        }
        catch { }
    }

    private void OnInboxFileDetected(string fullPath)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            if (!File.Exists(fullPath))
                return;

            if (_pendingInboxFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                return;

            _pendingInboxFiles.Enqueue(fullPath);

            if (!_isProcessingInboxQueue)
                ProcessInboxQueue();
        }));
    }

    private void ProcessInboxQueue()
    {
        if (_isProcessingInboxQueue)
            return;

        _isProcessingInboxQueue = true;

        try
        {
            while (_pendingInboxFiles.Count > 0)
            {
                var path = _pendingInboxFiles.Dequeue();

                if (!File.Exists(path))
                    continue;

                try
                {
                    var kind = DetectReplyKindSafe(path);
                    ImportReplyFile(path);
                    MoveToImported(path);

                    if (MainContent.Content is IReloadablePage p)
                        p.Reload();

                    var displayKind =
                        string.Equals(kind, "devis", StringComparison.OrdinalIgnoreCase) ? "Devis reçu" :
                        string.Equals(kind, "signature", StringComparison.OrdinalIgnoreCase) ? "Validation reçue" :
                        "Retour reçu";

                    System.Windows.MessageBox.Show(
                        this,
                        $"{displayKind}.\n\nFichier : {Path.GetFileName(path)}",
                        "Retour Iziregi",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"Import impossible.\n\n{ex.Message}\n\nLe fichier sera déplacé dans INBOX\\Error.",
                        "Erreur import INBOX",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    MoveToError(path);
                }
            }
        }
        finally
        {
            _isProcessingInboxQueue = false;
        }
    }

    private void MoveToImported(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            Directory.CreateDirectory(ImportedDir);

            var fileName = Path.GetFileName(path);
            var target = Path.Combine(ImportedDir, fileName);

            if (File.Exists(target))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                target = Path.Combine(ImportedDir, $"{nameNoExt}-{ts}.iziregi-reponse");
            }

            File.Move(path, target);
        }
        catch { }
    }

    private void MoveToError(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            Directory.CreateDirectory(ErrorDir);

            var fileName = Path.GetFileName(path);
            var target = Path.Combine(ErrorDir, fileName);

            if (File.Exists(target))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                target = Path.Combine(ErrorDir, $"{nameNoExt}-{ts}.iziregi-reponse");
            }

            File.Move(path, target);
        }
        catch { }
    }

    // =========================
    // Import core
    // =========================
    private class IziregiReplyFile
    {
        public string FileType { get; set; } = "iziregi-reponse";
        public string Package { get; set; } = "";
        public string RepliedAt { get; set; } = "";
        public long WorkOrderId { get; set; }
        public WorkOrder? WorkOrder { get; set; }
        public List<WorkOrderLine> Lines { get; set; } = new();

        public string SignatureName { get; set; } = "";
        public string SignatureDate { get; set; } = "";
        public byte[]? SignaturePng { get; set; }
    }

    private void ImportReplyFile(string filePath)
    {
        var json = ReadAllTextWithRetry(filePath, attempts: 10, delayMs: 200);

        var reply = JsonSerializer.Deserialize<IziregiReplyFile>(json, JsonOptions);
        if (reply == null)
            throw new Exception("Fichier invalide.");

        if (!string.Equals(reply.FileType, "iziregi-reponse", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Ce fichier n’est pas un .iziregi-reponse valide.");

        if (reply.WorkOrderId <= 0 && reply.WorkOrder != null && reply.WorkOrder.Id > 0)
            reply.WorkOrderId = reply.WorkOrder.Id;

        if (reply.WorkOrderId <= 0)
            throw new Exception("WorkOrderId manquant.");

        var pkg = (reply.Package ?? "").Trim().ToLowerInvariant();

        if (pkg == "devis")
            ImportQuoteReply(reply);
        else if (pkg == "signature")
            ImportSignatureReply(reply);
        else
            throw new Exception("Package inconnu dans le retour (attendu: devis ou signature).");
    }

    private void ImportQuoteReply(IziregiReplyFile reply)
    {
        var wo = Db.GetWorkOrderById(reply.WorkOrderId);
        if (wo == null)
            throw new Exception("Bon introuvable dans la base locale.");

        if (reply.WorkOrder != null)
        {
            wo.LaborHours = reply.WorkOrder.LaborHours;
            wo.LaborRate = reply.WorkOrder.LaborRate;
            wo.TravelQty = reply.WorkOrder.TravelQty;
            wo.TravelRate = reply.WorkOrder.TravelRate;
            wo.TvaRate = reply.WorkOrder.TvaRate;
            wo.QuoteNotes = reply.WorkOrder.QuoteNotes ?? "";
            Db.UpdateWorkOrderQuote(wo);
        }

        if (reply.Lines != null && reply.Lines.Count > 0)
        {
            var existing = Db.GetWorkOrderLines(wo.Id);
            foreach (var l in existing)
                Db.DeleteWorkOrderLine(l.Id);

            foreach (var l in reply.Lines)
                Db.InsertWorkOrderLine(wo.Id, l.Label ?? "", l.Qty, l.UnitPrice);
        }

        Db.SetStageQuoteReceived(wo.Id);
    }

    private void ImportSignatureReply(IziregiReplyFile reply)
    {
        var wo = Db.GetWorkOrderById(reply.WorkOrderId);
        if (wo == null)
            throw new Exception("Bon introuvable dans la base locale.");

        wo.SignatureName = (reply.SignatureName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(reply.SignatureDate) && DateTime.TryParse(reply.SignatureDate, out var dt))
            wo.SignatureDate = dt.Date;
        wo.SignaturePng = reply.SignaturePng;

        Db.UpdateWorkOrderSignatureRaw(wo);

        wo = Db.GetWorkOrderById(wo.Id);
        if (wo == null) throw new Exception("Bon introuvable après import.");

        if (!wo.HasFullSignature)
            throw new Exception("Signature incomplète dans le retour (nom/date/signature).");

        Db.SetStageValidated(wo.Id);
    }

    private string DetectReplyKindSafe(string filePath)
    {
        try
        {
            var json = ReadAllTextWithRetry(filePath, attempts: 5, delayMs: 150);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Package", out var pkgEl))
                return "";

            return (pkgEl.GetString() ?? "").Trim().ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private static string ReadAllTextWithRetry(string filePath, int attempts, int delayMs)
    {
        Exception? last = null;

        for (int i = 0; i < attempts; i++)
        {
            try { return File.ReadAllText(filePath); }
            catch (Exception ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        throw new Exception($"Impossible de lire le fichier : {Path.GetFileName(filePath)}", last);
    }
}