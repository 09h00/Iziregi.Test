// File: Pages/SettingsPage.xaml.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Iziregi.Test.Data;
using Iziregi.Test.Services;

namespace Iziregi.Test.Pages;

// ✅ 01.08.2026 (demande de Joe) : "Paramètres" devient un point d'entrée unique avec un
// sous-menu, au lieu d'un bouton "Identité Société" séparé et d'une fenêtre modale
// (ConfigSetupWindow) pour le reste. ConfigSetupWindow n'est PAS supprimée (voir son fichier) --
// toujours utilisée telle quelle pour la configuration initiale obligatoire au 1er lancement ;
// cette page reprend le même contenu (Connexion/Démarrage/Données/Éthique) pour un accès courant.
public partial class SettingsPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;
    private ArchitectIdentityPage? _identityPage;
    private ProjectsBankPage? _projectsBankPage;

    // ✅ Les 7 premières pages de la barre de nav principale, dans leur ordre gauche à
    // droite (demande de Joe, 10.08.2026). Archives/Corbeille retirées : ce ne sont plus
    // des boutons de nav à part entière (accessibles via le sous-menu ▾ de "Bons
    // d'intervention"), remplacées ici par Carnet d'adresses et Paramètres, désormais
    // dans les 7 premiers boutons. Aide/Contact (8e/9e) restent hors de cette liste.
    private static readonly (string Key, string Display)[] StartupPageOptions =
    {
        ("", "Tableau de bord"),
        ("Planning", "Planification"),
        ("Bons", "Bons d'intervention"),
        ("Comptabilité", "Comptabilité"),
        ("Listes", "Listes"),
        ("AddressBook", "Carnet d'adresses"),
        ("Settings", "Paramètres"),
    };

    public SettingsPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    // ✅ Mot de passe Admin (18.08.2026, demande de Joe) : reste déverrouillé pour le reste de
    // la session une fois saisi (comme le mot de passe Directeur pour les dossiers), pas besoin
    // de le ressaisir à chaque clic sur "Admin" pendant la même session de travail.
    private bool _adminUnlockedThisSession;

    public void Reload()
    {
        // ✅ Landing par défaut = "Démarrage" (18.08.2026, demande de Joe).
        ShowSection("Startup");
        PopulateConnectionFields();
        PopulateStartupSection();
    }

    // =========================
    // Navigation entre sections
    // =========================
    private void SubNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag) return;

        // ✅ 01.08.2026 (demande de Joe) : "CGU" est un lien externe, comme Aide/Contact
        // ailleurs dans l'app -- réutilise directement le handler de MainWindow (rendu
        // internal) plutôt que de dupliquer la logique d'ouverture de navigateur.
        if (tag == "Cgu")
        {
            _host.NavCgvEssai_Click(sender, e);
            return;
        }

        // ✅ Page "Admin" (18.08.2026, demande de Joe) : protégée par le mot de passe Admin,
        // distinct du mot de passe Directeur (voir Db.VerifyAdminPassword). Si aucun mot de
        // passe Admin n'est encore défini, accès libre (même bootstrap que le mot de passe
        // Directeur -- le premier réglage se fait sans verrou).
        if (tag == "Admin" && Db.HasAdminPassword() && !_adminUnlockedThisSession)
        {
            var prompt = new PasswordPromptWindow(
                "Accès Admin",
                "Cette page est protégée par le mot de passe Admin.",
                Db.VerifyAdminPassword)
            {
                Owner = System.Windows.Window.GetWindow(this)
            };

            if (prompt.ShowDialog() != true)
                return;

            _adminUnlockedThisSession = true;
        }

        ShowSection(tag);
    }

    private void ShowSection(string tag)
    {
        AdminPanel.Visibility = tag == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        ProjectsHost.Visibility = tag == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        StartupPanel.Visibility = tag == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        EthicsPanel.Visibility = tag == "Ethics" ? Visibility.Visible : Visibility.Collapsed;

        // ✅ Sous-menu masqué sur "Banque de dossiers" (18.08.2026, demande de Joe) : la liste
        // de dossiers a besoin de toute la largeur. Le bouton "Fermer" de ProjectsBankPage
        // (CloseRequested, voir plus bas) ramène sur "Démarrage" avec le sous-menu réaffiché.
        SubNavColumn.Width = tag == "Projects" ? new GridLength(0) : new GridLength(200);
        SubNavSpacerColumn.Width = tag == "Projects" ? new GridLength(0) : new GridLength(20);
        PageTitleTextBlock.Visibility = tag == "Projects" ? Visibility.Collapsed : Visibility.Visible;

        var active = tag switch
        {
            "Admin" => NavAdminButton,
            "Projects" => NavProjectsButton,
            "Startup" => NavStartupButton,
            "Ethics" => NavEthicsButton,
            _ => NavProjectsButton
        };

        foreach (var b in new[] { NavAdminButton, NavProjectsButton, NavStartupButton, NavEthicsButton })
            b.Style = (Style)FindResource(b == active ? "SubNavButtonActiveStyle" : "SubNavButtonStyle");

        if (tag == "Admin")
        {
            _identityPage ??= new ArchitectIdentityPage(_host, MainWindow.ServerBaseUrl, MainWindow.ServerApiKey);
            IdentityHost.Content = _identityPage;
            _identityPage.Reload();

            DirectorPasswordBox.Password = "";
            PopulateDirectorPasswordStatus();

            AdminPasswordBox.Password = "";
            PopulateAdminPasswordStatus();
        }

        // ✅ Page embarquée (18.08.2026, demande de Joe) : la fenêtre séparée "Banque de
        // dossiers" est obsolète, remplacée par ProjectsBankPage affichée directement dans cet
        // onglet (clic sur "Banque de dossiers" OU landing par défaut de Paramètres, voir Reload).
        if (tag == "Projects")
        {
            if (_projectsBankPage == null)
            {
                _projectsBankPage = new ProjectsBankPage(_host);
                // ✅ "Fermer" (Actions, 18.08.2026, demande de Joe) : ramène sur "Démarrage" avec
                // le sous-menu réaffiché, au lieu de fermer une fenêtre (page embarquée, plus de
                // fenêtre à fermer).
                _projectsBankPage.CloseRequested += (_, __) => ShowSection("Startup");
            }
            ProjectsHost.Content = _projectsBankPage;
            _projectsBankPage.Reload();
        }
    }

    // ✅ Mot de passe "Admin" (18.08.2026, demande de Joe) : distinct du mot de passe
    // Directeur -- protège cette page, jamais délégué.
    private void PopulateAdminPasswordStatus()
    {
        AdminPasswordStatusTextBlock.Text = Db.HasAdminPassword()
            ? "Un mot de passe Admin est actuellement défini."
            : "Aucun mot de passe Admin défini -- cette page reste accessible à tous tant que tu n'en définis pas un.";
    }

    private void SaveAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(AdminPasswordBox.Password))
        {
            System.Windows.MessageBox.Show(
                "Saisis un mot de passe, ou utilise \"Retirer\" pour l'enlever.",
                "Mot de passe Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Db.SetAdminPassword(AdminPasswordBox.Password);
        AdminPasswordBox.Password = "";
        PopulateAdminPasswordStatus();
    }

    private void RemoveAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!Db.HasAdminPassword())
        {
            System.Windows.MessageBox.Show(
                "Aucun mot de passe Admin n'est défini.",
                "Mot de passe Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var ok = System.Windows.MessageBox.Show(
            "Retirer le mot de passe Admin ? Cette page redeviendra accessible à tous.",
            "Mot de passe Admin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetAdminPassword(null);
        AdminPasswordBox.Password = "";
        PopulateAdminPasswordStatus();
    }

    // ✅ Mot de passe "Directeur" (18.08.2026, demande de Joe).
    private void PopulateDirectorPasswordStatus()
    {
        DirectorPasswordStatusTextBlock.Text = Db.HasDirectorPassword()
            ? "Un mot de passe Directeur est actuellement défini."
            : "Aucun mot de passe Directeur défini.";
    }

    private void SaveDirectorPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(DirectorPasswordBox.Password))
        {
            System.Windows.MessageBox.Show(
                "Saisis un mot de passe, ou utilise \"Retirer\" pour l'enlever.",
                "Mot de passe Directeur",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Db.SetDirectorPassword(DirectorPasswordBox.Password);
        DirectorPasswordBox.Password = "";
        PopulateDirectorPasswordStatus();
    }

    private void RemoveDirectorPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!Db.HasDirectorPassword())
        {
            System.Windows.MessageBox.Show(
                "Aucun mot de passe Directeur n'est défini.",
                "Mot de passe Directeur",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var ok = System.Windows.MessageBox.Show(
            "Retirer le mot de passe Directeur ?",
            "Mot de passe Directeur",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ok != MessageBoxResult.Yes)
            return;

        Db.SetDirectorPassword(null);
        DirectorPasswordBox.Password = "";
        PopulateDirectorPasswordStatus();
    }

    // =========================
    // ✅ Reset par email du mot de passe Directeur (18.08.2026, demande de Joe) : le mot de
    // passe reste local (Db.SetDirectorPassword), le serveur prouve seulement l'identité du
    // tenant via un code à usage unique envoyé à son email de contact (voir Iziregi.Server,
    // /internal/password-reset/request et /verify).
    // =========================
    private static readonly HttpClient ResetHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    private static string NormalizeServerBaseUrl(string? baseUrl)
    {
        var v = (baseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) v = "https://iziregi.com";
        return v.TrimEnd('/');
    }

    private static async Task<HttpResponseMessage> PostWithApiKeyAsync(string url, HttpContent? content)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(MainWindow.ServerApiKey))
            req.Headers.Add("X-Api-Key", MainWindow.ServerApiKey);
        return await ResetHttp.SendAsync(req);
    }

    private async void ForgotDirectorPassword_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Un code à usage unique va être envoyé par email à l'adresse de contact enregistrée pour ton bureau. Continuer ?",
            "Mot de passe Directeur oublié",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        var baseUrl = NormalizeServerBaseUrl(MainWindow.ServerBaseUrl);

        try
        {
            using var resp = await PostWithApiKeyAsync($"{baseUrl}/internal/password-reset/request", null);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var error = TryReadJsonError(body);
                var message = error switch
                {
                    "no_contact_email" => "Aucun email de contact n'est enregistré pour ton bureau. Contacte le support Iziregi pour le configurer.",
                    "rate_limited" => "Une demande a déjà été envoyée récemment. Attends 2 minutes avant de réessayer.",
                    "email_send_failed" => "L'email n'a pas pu être envoyé. Réessaie plus tard ou contacte le support.",
                    _ => $"Le serveur a refusé la demande (HTTP {(int)resp.StatusCode})."
                };

                System.Windows.MessageBox.Show(message, "Mot de passe Directeur oublié", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible de joindre le serveur.\n\n{ex.Message}",
                "Mot de passe Directeur oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var resetWin = new PasswordResetWindow
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (resetWin.ShowDialog() != true)
            return;

        try
        {
            var json = JsonSerializer.Serialize(new { code = resetWin.Code });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await PostWithApiKeyAsync($"{baseUrl}/internal/password-reset/verify", content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var error = TryReadJsonError(body);
                var message = error switch
                {
                    "invalid_code" => "Code incorrect.",
                    "too_many_attempts" => "Trop de tentatives incorrectes. Refais une demande de code.",
                    "no_pending_code" => "Ce code a expiré ou n'existe plus. Refais une demande.",
                    _ => $"Le serveur a refusé le code (HTTP {(int)resp.StatusCode})."
                };

                System.Windows.MessageBox.Show(message, "Mot de passe Directeur oublié", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Db.SetDirectorPassword(resetWin.NewPassword);
            PopulateDirectorPasswordStatus();

            System.Windows.MessageBox.Show(
                "Le mot de passe Directeur a été réinitialisé avec succès.",
                "Mot de passe Directeur oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible de joindre le serveur.\n\n{ex.Message}",
                "Mot de passe Directeur oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string TryReadJsonError(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            return doc.RootElement.TryGetProperty("error", out var errProp) ? (errProp.GetString() ?? "") : "";
        }
        catch
        {
            return "";
        }
    }

    // =========================
    // Connexion
    // =========================
    private void PopulateConnectionFields()
    {
        var existingUrl = IziregiConfigService.Current.ServerBaseUrl;
        ServerUrlTextBox.Text = string.IsNullOrWhiteSpace(existingUrl) ? "https://iziregi.com" : existingUrl;
        ServerKeyTextBox.Text = IziregiConfigService.Current.ServerApiKey;
        ConnectionStatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowConnectionStatus(string text, bool isError)
    {
        ConnectionStatusText.Text = text;
        ConnectionStatusText.Foreground = new SolidColorBrush(isError ? System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C) : System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D));
        ConnectionStatusText.Visibility = Visibility.Visible;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlTextBox.Text.Trim().TrimEnd('/');
        var key = ServerKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            ShowConnectionStatus("Veuillez renseigner l'URL et la clé avant de tester.", true);
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Api-Key", key);
            var resp = await http.GetAsync($"{url}/internal/ping");

            if (resp.IsSuccessStatusCode)
                ShowConnectionStatus("Connexion réussie ! Le serveur répond correctement.", false);
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                ShowConnectionStatus("Clé d'accès refusée par le serveur (401). Vérifiez la clé API.", true);
            else
                ShowConnectionStatus($"Le serveur a répondu avec le code {(int)resp.StatusCode}.", true);
        }
        catch (HttpRequestException ex)
        {
            ShowConnectionStatus($"Impossible de joindre le serveur : {ex.Message}", true);
        }
        catch (TaskCanceledException)
        {
            ShowConnectionStatus("Le serveur ne répond pas (délai dépassé).", true);
        }
    }

    private void SaveConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlTextBox.Text.Trim().TrimEnd('/');
        var key = ServerKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            ShowConnectionStatus("Veuillez renseigner l'URL du serveur et la clé d'accès.", true);
            return;
        }

        IziregiConfigService.Save(new IziregiConfig { ServerBaseUrl = url, ServerApiKey = key });
        ShowConnectionStatus("Enregistré.", false);
    }

    // =========================
    // Démarrage (propre au dossier actuellement sélectionné, voir ConfigSetupWindow)
    // =========================
    private void PopulateStartupSection()
    {
        var projectId = Db.GetCurrentProjectId();
        var project = projectId.HasValue ? Db.GetProjectById(projectId.Value) : null;

        if (project == null)
        {
            StartupDescText.Text = "Sélectionne d'abord un dossier pour configurer sa page de démarrage.";
            StartupPageComboBox.IsEnabled = false;
            StartupPageComboBox.Items.Clear();
            return;
        }

        StartupPageComboBox.IsEnabled = true;
        StartupDescText.Text = $"Ce réglage est propre au dossier « {project.Name} ». Chaque dossier peut avoir sa propre page de démarrage.";

        StartupPageComboBox.SelectionChanged -= StartupPageComboBox_SelectionChanged;
        StartupPageComboBox.Items.Clear();

        var currentValue = Db.GetDefaultStartupPage(projectId!.Value);
        foreach (var (key, display) in StartupPageOptions)
        {
            var item = new ComboBoxItem { Content = display, Tag = key };
            StartupPageComboBox.Items.Add(item);
            if (string.Equals(key, currentValue, StringComparison.Ordinal))
                StartupPageComboBox.SelectedItem = item;
        }
        if (StartupPageComboBox.SelectedItem == null)
            StartupPageComboBox.SelectedIndex = 0;

        StartupPageComboBox.SelectionChanged += StartupPageComboBox_SelectionChanged;
    }

    private void StartupPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var projectId = Db.GetCurrentProjectId();
        if (!projectId.HasValue) return;

        if (StartupPageComboBox.SelectedItem is ComboBoxItem item)
            Db.SetDefaultStartupPage(projectId.Value, (string)item.Tag);
    }

    // =========================
    // Vos données
    // =========================
    private void ExportAllData_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter toutes les données",
            Filter = "Archive ZIP (*.zip)|*.zip",
            FileName = $"iziregi-export-complet-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            DefaultExt = ".zip"
        };

        if (sfd.ShowDialog(System.Windows.Window.GetWindow(this)) != true)
            return;

        try
        {
            ExportService.ExportAllData(sfd.FileName);
            System.Windows.MessageBox.Show(
                "Export terminé. Toutes vos données ont été enregistrées dans le fichier zip choisi.",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"L'export a échoué : {ex.Message}",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
