// ConfigSetupWindow.cs
// Fenêtre de configuration initiale : affichée au 1er lancement si iziregi-config.json est absent
// ou si la clé API est vide. Construction 100% code (pas de XAML associé).
//
// ✅ Restructurée en onglets (17.07.2026, demande de Joe) : "Connexion" (serveur/clé,
// inchangé), "Vos données" (export complet, séparé du reste) et "Éthique & confidentialité"
// (nouvelle page, texte fourni par Joe — hébergement suisse, nLPD, portabilité des données).

using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Iziregi.Test.Services;
using WpfButton      = System.Windows.Controls.Button;
using WpfColor       = System.Windows.Media.Color;
using WpfMessageBox  = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox     = System.Windows.Controls.TextBox;
using WpfTabControl  = System.Windows.Controls.TabControl;
using WpfTabItem     = System.Windows.Controls.TabItem;

namespace Iziregi.Test;

internal class ConfigSetupWindow : Window
{
    private readonly WpfTextBox _urlBox;
    private readonly WpfTextBox _keyBox;

    public ConfigSetupWindow()
    {
        Title = "Configuration Iziregi";
        Width  = 540;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245));

        var tabs = new WpfTabControl { Margin = new Thickness(12) };

        var connectionTab = new WpfTabItem { Header = "Connexion" };
        connectionTab.Content = BuildConnectionTabContent(out _urlBox, out _keyBox);
        tabs.Items.Add(connectionTab);

        var dataTab = new WpfTabItem { Header = "Vos données" };
        dataTab.Content = BuildDataTabContent();
        tabs.Items.Add(dataTab);

        var ethicsTab = new WpfTabItem { Header = "Éthique & confidentialité" };
        ethicsTab.Content = BuildEthicsTabContent();
        tabs.Items.Add(ethicsTab);

        Content = tabs;
    }

    // =========================
    // Onglet "Connexion" (inchangé, juste déplacé dans son propre onglet)
    // =========================
    private FrameworkElement BuildConnectionTabContent(out WpfTextBox urlBox, out WpfTextBox keyBox)
    {
        var grid = new Grid { Margin = new Thickness(16, 20, 16, 16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Titre
        var title = new TextBlock
        {
            Text = "Connexion au serveur Iziregi",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(title, 0); Grid.SetColumnSpan(title, 2);
        grid.Children.Add(title);

        // URL
        var urlLabel = new TextBlock
        {
            Text = "URL du serveur :",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(urlLabel, 1); Grid.SetColumn(urlLabel, 0);
        grid.Children.Add(urlLabel);

        var existingUrl = IziregiConfigService.Current.ServerBaseUrl;
        urlBox = new WpfTextBox
        {
            Text = string.IsNullOrWhiteSpace(existingUrl) ? "https://iziregi.com" : existingUrl,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(urlBox, 1); Grid.SetColumn(urlBox, 1);
        grid.Children.Add(urlBox);

        // Clé API
        var keyLabel = new TextBlock
        {
            Text = "Clé d'accès :",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(keyLabel, 2); Grid.SetColumn(keyLabel, 0);
        grid.Children.Add(keyLabel);

        keyBox = new WpfTextBox
        {
            Text = IziregiConfigService.Current.ServerApiKey,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(keyBox, 2); Grid.SetColumn(keyBox, 1);
        grid.Children.Add(keyBox);

        // Aide
        var help = new TextBlock
        {
            Text = "La clé d'accès vous est fournie par votre administrateur Iziregi.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(help, 4); Grid.SetColumnSpan(help, 2);
        grid.Children.Add(help);

        // Boutons Tester / Enregistrer
        var btnPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(btnPanel, 5); Grid.SetColumnSpan(btnPanel, 2);

        var btnTest = new WpfButton { Content = "Tester", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        btnTest.Click += (_, _) => TestConnection();
        btnPanel.Children.Add(btnTest);

        var btnSave = new WpfButton { Content = "Enregistrer", Width = 110, Height = 30, IsDefault = true };
        btnSave.Click += (_, _) => Save();
        btnPanel.Children.Add(btnSave);
        grid.Children.Add(btnPanel);

        return grid;
    }

    // =========================
    // Onglet "Vos données" (17.07.2026, demande de Joe) : contrepartie concrète à la
    // promesse de portabilité des données dans les futures CGV — permet à l'utilisateur de
    // récupérer l'intégralité de ses données (tous projets, tous bons, listes, comptabilité)
    // dans un format ouvert (CSV dans un zip), utilisable même sans Iziregi installé. Voir
    // ExportService.ExportAllData (introspection dynamique du schéma, donc toujours à jour
    // même si de nouvelles colonnes sont ajoutées plus tard).
    // =========================
    private FrameworkElement BuildDataTabContent()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 20, 16, 16) };

        var dataTitle = new TextBlock
        {
            Text = "Vos données",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(dataTitle);

        var dataDesc = new TextBlock
        {
            Text = "Exportez l'intégralité de vos données (projets, bons, listes, comptabilité) dans des fichiers CSV, lisibles avec Excel ou tout tableur, même sans Iziregi.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        panel.Children.Add(dataDesc);

        var btnExportAll = new WpfButton
        {
            Content = "Exporter toutes mes données…",
            Height = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(14, 0, 14, 0)
        };
        btnExportAll.Click += (_, _) => ExportAllData();
        panel.Children.Add(btnExportAll);

        return panel;
    }

    // =========================
    // Onglet "Éthique & confidentialité" (17.07.2026, demande de Joe) : texte fourni tel
    // quel par Joe — présentation de l'hébergement suisse (Infomaniak, ISO 27001, Swiss
    // Hosting/Swiss Made Software), non-revente des données, portabilité, conformité nLPD.
    // =========================
    private FrameworkElement BuildEthicsTabContent()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 20, 16, 16) };

        var ethicsTitle = new TextBlock
        {
            Text = "Vos données, en toute confiance",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(ethicsTitle);

        var ethicsIntro = new TextBlock
        {
            Text = "Iziregi est hébergé exclusivement en Suisse, chez Infomaniak — hébergeur suisse indépendant (fondé à Genève en 1994), certifié ISO 27001 (sécurité de l'information) et labellisé Swiss Hosting / Swiss Made Software. Vos données ne quittent jamais le territoire suisse.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(ethicsIntro);

        AddEthicsBullet(panel, "Hébergement 100% souverain, sur des serveurs suisses gérés par un hébergeur suisse certifié");
        AddEthicsBullet(panel, "Aucune donnée n'est partagée, revendue ou exploitée à des fins commerciales ou publicitaires");
        AddEthicsBullet(panel, "Vous pouvez à tout moment exporter l'intégralité de vos données dans un format ouvert (Excel, tableur), sans dépendre d'Iziregi pour les consulter");
        AddEthicsBullet(panel, "Conforme à la législation suisse sur la protection des données (nLPD)");

        return panel;
    }

    private static void AddEthicsBullet(StackPanel panel, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var check = new TextBlock
        {
            Text = "✓",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(16, 185, 129)), // #10B981
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(check, 0);
        row.Children.Add(check);

        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        panel.Children.Add(row);
    }

    private void ExportAllData()
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter toutes les données",
            Filter = "Archive ZIP (*.zip)|*.zip",
            FileName = $"iziregi-export-complet-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            DefaultExt = ".zip"
        };

        if (sfd.ShowDialog(this) != true)
            return;

        try
        {
            Iziregi.Test.Services.ExportService.ExportAllData(sfd.FileName);
            WpfMessageBox.Show(
                "Export terminé. Toutes vos données ont été enregistrées dans le fichier zip choisi.",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"L'export a échoué : {ex.Message}",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void TestConnection()
    {
        var url = _urlBox.Text.Trim().TrimEnd('/');
        var key = _keyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            WpfMessageBox.Show("Veuillez renseigner l'URL et la clé avant de tester.",
                "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // ✅ Sécurité : clé API envoyée via l'en-tête HTTP plutôt que dans l'URL.
            http.DefaultRequestHeaders.Add("X-Api-Key", key);
            var resp = await http.GetAsync($"{url}/internal/ping");

            if (resp.IsSuccessStatusCode)
            {
                WpfMessageBox.Show("Connexion réussie ! Le serveur répond correctement.",
                    "Test réussi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                WpfMessageBox.Show(
                    "Clé d'accès refusée par le serveur (401).\nVérifiez la clé API.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                WpfMessageBox.Show(
                    $"Le serveur a répondu avec le code {(int)resp.StatusCode}.",
                    "Réponse inattendue", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (HttpRequestException ex)
        {
            WpfMessageBox.Show(
                $"Impossible de joindre le serveur :\n{ex.Message}",
                "Erreur de connexion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (TaskCanceledException)
        {
            WpfMessageBox.Show("Le serveur ne répond pas (délai dépassé).",
                "Délai expiré", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save()
    {
        var url = _urlBox.Text.Trim().TrimEnd('/');
        var key = _keyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            WpfMessageBox.Show(
                "Veuillez renseigner l'URL du serveur et la clé d'accès.",
                "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IziregiConfigService.Save(new IziregiConfig { ServerBaseUrl = url, ServerApiKey = key });
        DialogResult = true;
        Close();
    }
}
