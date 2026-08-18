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
    private ProjectsBankPage? _projectsBankPage;

    // ✅ NOUVEAU (18.08.2026, demande de Joe : "je ne dois pas être obligé de cliquer sur
    // Fermer pour changer de sous-page. En cliquant sur une autre sous-page, la fenêtre peut
    // changer tout de suite") : les 5 fenêtres du sous-menu Admin ne sont plus modales (Show,
    // pas ShowDialog) et une seule reste ouverte à la fois -- en ouvrir une nouvelle referme
    // automatiquement la précédente, voir OpenAdminSubWindow.
    private System.Windows.Window? _openAdminSubWindow;

    // ✅ NOUVEAU (18.08.2026, demande de Joe : "on peut élargir la colonne des titres de
    // sous-pages à gauche, ainsi on décale les fenêtres à droite pour laisser plus de place à
    // la colonne de gauche") : demi-largeur de la colonne élargie (voir NavAdminButton_Click),
    // pour que les 5 fenêtres du sous-menu Admin ne recouvrent plus la colonne de gauche une
    // fois centrées sur la fenêtre principale.
    private const double AdminSubWindowRightShiftPx = 30 - (50.0 * 96.0 / 25.4) - (5.0 * 96.0 / 25.4);

    // ✅ NOUVEAU (18.08.2026, demande de Joe : "10mm plus bas") : décalage vertical additionnel
    // par rapport au centrage vertical CenterOwner naturel (non touché jusque-là).
    private const double AdminSubWindowDownShiftPx = 10.0 * 96.0 / 25.4;

    // ✅ Largeur de référence = celle des 4 fenêtres "standard" (Mot de passe Admin/général des
    // dossiers, Vos données, Connexion) -- demande de Joe : "Identité société" (720, plus large
    // que les autres) doit avoir le même bord GAUCHE que les autres, pas être centrée sur sa
    // propre largeur (ce qui la décalait trop à gauche par rapport aux 4 autres).
    private const double AdminSubWindowStandardWidth = 528;

    private void OpenAdminSubWindow(System.Windows.Controls.Button trigger, System.Windows.Window window)
    {
        _openAdminSubWindow?.Close();

        var owner = System.Windows.Window.GetWindow(this);
        window.Owner = owner;
        // ✅ Fix (18.08.2026) : avec SizeToContent="Height", WPF recalcule le centrage
        // CenterOwner APRÈS l'événement Loaded (une fois la taille réelle du contenu connue),
        // ce qui écrasait le Left qu'on fixait ici -- "elle est toujours au même endroit"
        // (demande de Joe). ContentRendered se déclenche après cette repasse, une fois la
        // fenêtre définitivement positionnée/dimensionnée.
        window.ContentRendered += (_, __) =>
        {
            if (owner != null)
                window.Left = owner.Left + (owner.ActualWidth - AdminSubWindowStandardWidth) / 2 + AdminSubWindowRightShiftPx;
            window.Top += AdminSubWindowDownShiftPx;
        };
        window.Closed += (_, __) =>
        {
            if (ReferenceEquals(_openAdminSubWindow, window))
            {
                _openAdminSubWindow = null;
                // ✅ Plus de fenêtre ouverte -> plus de sous-page en surbrillance (voir aussi
                // l'appel juste en dessous, qui remet le NOUVEAU bouton en surbrillance quand
                // on bascule d'une sous-page à l'autre sans passer par "Fermer").
                SetActiveAdminSubMenuButton(null);
            }
        };

        _openAdminSubWindow = window;
        SetActiveAdminSubMenuButton(trigger);
        window.Show();
    }

    // ✅ NOUVEAU (18.08.2026, demande de Joe) : "lorsque je sélectionne une sous-page (ex. Mot
    // de passe Admin) le fond du titre du sous-menu à gauche doit être également en bleu" --
    // même style actif que la nav principale (SubNavButtonActiveStyle), appliqué au bouton du
    // sous-menu Admin correspondant à la fenêtre actuellement ouverte.
    private void SetActiveAdminSubMenuButton(System.Windows.Controls.Button? active)
    {
        foreach (var child in AdminSubMenu.Children)
        {
            if (child is System.Windows.Controls.Button b)
                b.Style = (Style)FindResource(b == active ? "SubNavButtonActiveStyle" : "SubNavButtonStyle");
        }
    }

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
            // ✅ Une seule fenêtre/section à la fois (18.08.2026, demande de Joe) : un clic sur
            // un autre item du sous-menu doit replier AdminSubMenu s'il était ouvert.
            CollapseAdminSubMenu();
            _host.NavCgvEssai_Click(sender, e);
            return;
        }

        ShowSection(tag);
    }

    // ✅ "Admin" n'est plus une section de page (18.08.2026, demande de Joe) : un clic déplie/
    // replie AdminSubMenu juste en dessous, protégé par le mot de passe Admin (voir
    // Db.VerifyAdminPassword). Si aucun mot de passe Admin n'est encore défini, accès libre
    // (même bootstrap que le mot de passe général des dossiers -- le premier réglage se fait
    // sans verrou).
    private void NavAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdminSubMenuBorder.Visibility == Visibility.Visible)
        {
            CollapseAdminSubMenu();
            return;
        }

        if (Db.HasAdminPassword() && !_adminUnlockedThisSession)
        {
            var prompt = new PasswordPromptWindow(
                "Accès Admin",
                "Ce menu est protégé par le mot de passe Admin.",
                Db.VerifyAdminPassword)
            {
                Owner = System.Windows.Window.GetWindow(this)
            };

            if (prompt.ShowDialog() != true)
                return;

            _adminUnlockedThisSession = true;
        }

        AdminSubMenuBorder.Visibility = Visibility.Visible;

        // ✅ Fix (18.08.2026, demande de Joe : "Démarrage doit disparaitre si je clique sur
        // Admin") : la section principale affichée (Démarrage/Banque de dossiers/Éthique) ne
        // disparaissait pas quand on dépliait Admin, donnant l'impression que 2 sous-pages se
        // superposaient. Aucune section n'a de sens à rester affichée pendant que le sous-menu
        // Admin est ouvert.
        ProjectsHost.Visibility = Visibility.Collapsed;
        StartupPanel.Visibility = Visibility.Collapsed;
        EthicsPanel.Visibility = Visibility.Collapsed;
        // ✅ Élargie à 300 (18.08.2026, demande de Joe) : assez large pour que le titre le plus
        // long ("Connexion au serveur Iziregi") tienne sur une seule ligne -- voir aussi
        // AdminSubWindowRightShiftPx, qui décale les fenêtres du sous-menu Admin d'autant vers
        // la droite pour ne pas recouvrir cette colonne élargie.
        SubNavColumn.Width = new GridLength(300);
        foreach (var b in new[] { NavProjectsButton, NavStartupButton, NavEthicsButton })
            b.Style = (Style)FindResource("SubNavButtonStyle");

        // ✅ Bouton "Admin" mis en surbrillance tant que son sous-menu est déplié (lien visuel
        // Admin ↔ sous-pages, demande de Joe).
        NavAdminButton.Style = (Style)FindResource("SubNavButtonActiveStyle");
    }

    // ✅ Un seul sous-menu/section visible à la fois (18.08.2026, demande de Joe : "je ne dois
    // voir apparaitre qu'une seule fenêtre à la fois, à savoir, la dernière sélectionnée").
    private void CollapseAdminSubMenu()
    {
        AdminSubMenuBorder.Visibility = Visibility.Collapsed;
        NavAdminButton.Style = (Style)FindResource("SubNavButtonStyle");
        _openAdminSubWindow?.Close();
    }

    private void OpenIdentityWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenAdminSubWindow((System.Windows.Controls.Button)sender, new IdentityWindow(_host, MainWindow.ServerBaseUrl, MainWindow.ServerApiKey));
    }

    private void ShowSection(string tag)
    {
        // ✅ Une seule section/fenêtre à la fois (18.08.2026, demande de Joe) : sélectionner
        // Démarrage/Banque de dossiers/Éthique replie AdminSubMenu s'il était ouvert.
        CollapseAdminSubMenu();

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
            "Projects" => NavProjectsButton,
            "Startup" => NavStartupButton,
            "Ethics" => NavEthicsButton,
            _ => NavProjectsButton
        };

        foreach (var b in new[] { NavProjectsButton, NavStartupButton, NavEthicsButton })
            b.Style = (Style)FindResource(b == active ? "SubNavButtonActiveStyle" : "SubNavButtonStyle");

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

    // ✅ Restructuré (18.08.2026, demande de Joe) : "Mot de passe Admin"/"Mot de passe général
    // des dossiers"/"Vos données"/"Connexion" sont maintenant des fenêtres séparées, ouvertes
    // depuis un menu sur la page Admin, plutôt que des sections empilées sur cette page.
    private void OpenAdminPasswordWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenAdminSubWindow((System.Windows.Controls.Button)sender, new AdminPasswordWindow());
    }

    private void OpenDirectorPasswordWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenAdminSubWindow((System.Windows.Controls.Button)sender, new DirectorPasswordWindow());
    }

    private void OpenDataExportWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenAdminSubWindow((System.Windows.Controls.Button)sender, new DataExportWindow());
    }

    private void OpenConnectionWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenAdminSubWindow((System.Windows.Controls.Button)sender, new ConnectionWindow());
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

}
