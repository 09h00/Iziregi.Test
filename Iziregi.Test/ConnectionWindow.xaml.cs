// File: ConnectionWindow.xaml.cs
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Iziregi.Test.Services;

namespace Iziregi.Test;

public partial class ConnectionWindow : Window
{
    public ConnectionWindow()
    {
        InitializeComponent();
        PopulateFields();
    }

    private void PopulateFields()
    {
        var existingUrl = IziregiConfigService.Current.ServerBaseUrl;
        ServerUrlTextBox.Text = string.IsNullOrWhiteSpace(existingUrl) ? "https://iziregi.com" : existingUrl;
        ServerKeyTextBox.Text = IziregiConfigService.Current.ServerApiKey;
        ConnectionStatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string text, bool isError)
    {
        ConnectionStatusText.Text = text;
        ConnectionStatusText.Foreground = new SolidColorBrush(isError
            ? System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C)
            : System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D));
        ConnectionStatusText.Visibility = Visibility.Visible;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlTextBox.Text.Trim().TrimEnd('/');
        var key = ServerKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            ShowStatus("Veuillez renseigner l'URL et la clé avant de tester.", true);
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Api-Key", key);
            var resp = await http.GetAsync($"{url}/internal/ping");

            if (resp.IsSuccessStatusCode)
                ShowStatus("Connexion réussie ! Le serveur répond correctement.", false);
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                ShowStatus("Clé d'accès refusée par le serveur (401). Vérifiez la clé API.", true);
            else
                ShowStatus($"Le serveur a répondu avec le code {(int)resp.StatusCode}.", true);
        }
        catch (HttpRequestException ex)
        {
            ShowStatus($"Impossible de joindre le serveur : {ex.Message}", true);
        }
        catch (TaskCanceledException)
        {
            ShowStatus("Le serveur ne répond pas (délai dépassé).", true);
        }
    }

    private void SaveConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlTextBox.Text.Trim().TrimEnd('/');
        var key = ServerKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            ShowStatus("Veuillez renseigner l'URL du serveur et la clé d'accès.", true);
            return;
        }

        IziregiConfigService.Save(new IziregiConfig { ServerBaseUrl = url, ServerApiKey = key });
        ShowStatus("Enregistré.", false);
    }
}
