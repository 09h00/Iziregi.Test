// File: Pages/SettingsPage.xaml.cs
using System;
using System.Net.Http;
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

    public void Reload()
    {
        ShowSection("Identity");
        PopulateConnectionFields();
        PopulateStartupSection();
    }

    // =========================
    // Navigation entre sections
    // =========================
    private void SubNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag) return;

        if (tag == "Projects")
        {
            OpenProjectsBank();
            return;
        }

        // ✅ 01.08.2026 (demande de Joe) : "CGU" est un lien externe, comme Aide/Contact
        // ailleurs dans l'app -- réutilise directement le handler de MainWindow (rendu
        // internal) plutôt que de dupliquer la logique d'ouverture de navigateur.
        if (tag == "Cgu")
        {
            _host.NavCgvEssai_Click(sender, e);
            return;
        }

        ShowSection(tag);
    }

    private void ShowSection(string tag)
    {
        IdentityHost.Visibility = tag == "Identity" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.Visibility = tag == "Connection" ? Visibility.Visible : Visibility.Collapsed;
        StartupPanel.Visibility = tag == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        DataPanel.Visibility = tag == "Data" ? Visibility.Visible : Visibility.Collapsed;
        EthicsPanel.Visibility = tag == "Ethics" ? Visibility.Visible : Visibility.Collapsed;

        var active = tag switch
        {
            "Identity" => NavIdentityButton,
            "Connection" => NavConnectionButton,
            "Startup" => NavStartupButton,
            "Data" => NavDataButton,
            "Ethics" => NavEthicsButton,
            _ => NavIdentityButton
        };

        foreach (var b in new[] { NavIdentityButton, NavProjectsButton, NavConnectionButton, NavStartupButton, NavDataButton, NavEthicsButton })
            b.Style = (Style)FindResource(b == active ? "SubNavButtonActiveStyle" : "SubNavButtonStyle");

        if (tag == "Identity")
        {
            _identityPage ??= new ArchitectIdentityPage(_host, MainWindow.ServerBaseUrl, MainWindow.ServerApiKey);
            IdentityHost.Content = _identityPage;
            _identityPage.Reload();
        }
    }

    // =========================
    // Banque de dossiers (ProjectsWindow reste une fenêtre, même principe que le bouton
    // "Gérer les projets" de la page Bons d'intervention -- pas de conversion en page
    // embarquée pour l'instant, portée trop large).
    // =========================
    private void OpenProjectsBank_Click(object sender, RoutedEventArgs e) => OpenProjectsBank();

    private void OpenProjectsBank()
    {
        try
        {
            var win = new ProjectsWindow
            {
                Owner = System.Windows.Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
            };
            win.ShowDialog();
        }
        catch { }
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
