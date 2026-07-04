// ConfigSetupWindow.cs
// Fenêtre de configuration initiale : affichée au 1er lancement si iziregi-config.json est absent
// ou si la clé API est vide. Construction 100% code (pas de XAML associé).

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

namespace Iziregi.Test;

internal class ConfigSetupWindow : Window
{
    private readonly WpfTextBox _urlBox;
    private readonly WpfTextBox _keyBox;

    public ConfigSetupWindow()
    {
        Title = "Configuration Iziregi";
        Width  = 500;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245));

        var grid = new Grid { Margin = new Thickness(24, 20, 24, 20) };
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
        _urlBox = new WpfTextBox
        {
            Text = string.IsNullOrWhiteSpace(existingUrl) ? "https://iziregi.com" : existingUrl,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_urlBox, 1); Grid.SetColumn(_urlBox, 1);
        grid.Children.Add(_urlBox);

        // Clé API
        var keyLabel = new TextBlock
        {
            Text = "Clé d'accès :",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(keyLabel, 2); Grid.SetColumn(keyLabel, 0);
        grid.Children.Add(keyLabel);

        _keyBox = new WpfTextBox
        {
            Text = IziregiConfigService.Current.ServerApiKey,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_keyBox, 2); Grid.SetColumn(_keyBox, 1);
        grid.Children.Add(_keyBox);

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

        // Bouton Enregistrer
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

        Content = grid;
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
