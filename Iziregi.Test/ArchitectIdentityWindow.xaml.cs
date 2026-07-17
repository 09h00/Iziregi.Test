// File: ArchitectIdentityWindow.xaml.cs
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using Iziregi.Test.Data;

namespace Iziregi.Test;

public partial class ArchitectIdentityWindow : Window
{
    private string _logoPath = "";
    private readonly string _serverBaseUrl;
    private readonly string _serverApiKey;

    public ArchitectIdentityWindow(string serverBaseUrl = "", string serverApiKey = "")
    {
        InitializeComponent();
        _serverBaseUrl = serverBaseUrl.TrimEnd('/');
        _serverApiKey  = serverApiKey;
        Db.Init();
        LoadFromDb();
    }

    private void LoadFromDb()
    {
        ArchitectNameTextBox.Text = Db.GetArchitectName();
        ArchitectRefTextBox.Text = Db.GetArchitectRef();
        ArchitectRef2TextBox.Text = Db.GetArchitectRef2();

        var fullAddress = (Db.GetArchitectAddress() ?? "").Replace("\r\n", "\n");
        var parts = fullAddress.Split('\n');
        ArchitectAddressTextBox.Text = parts.Length > 0 ? parts[0].Trim() : "";
        ArchitectZipCityTextBox.Text = parts.Length > 1
            ? string.Join(", ", parts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0))
            : "";

        _logoPath = Db.GetArchitectLogoPath() ?? "";
        LogoPathTextBox.Text = _logoPath;

        LoadLogoPreview(_logoPath);
        StatusTextBlock.Text = "";
    }

    private void LoadLogoPreview(string? path)
    {
        LogoImage.Source = null;
        LogoEmptyText.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            LogoImage.Source = bmp;
            LogoEmptyText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            LogoImage.Source = null;
            LogoEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void ImportLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer un logo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        _logoPath = dlg.FileName;
        LogoPathTextBox.Text = _logoPath;
        LoadLogoPreview(_logoPath);

        StatusTextBlock.Text = "Logo chargé (pense à Enregistrer).";
    }

    private void RemoveLogo_Click(object sender, RoutedEventArgs e)
    {
        _logoPath = "";
        LogoPathTextBox.Text = "";
        LoadLogoPreview("");

        StatusTextBlock.Text = "Logo retiré (pense à Enregistrer).";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name        = (ArchitectNameTextBox.Text    ?? "").Trim();
            var reference   = (ArchitectRefTextBox.Text     ?? "").Trim();
            var reference2  = (ArchitectRef2TextBox.Text    ?? "").Trim();
            var addressLine = (ArchitectAddressTextBox.Text ?? "").Trim();
            var zipCity     = (ArchitectZipCityTextBox.Text ?? "").Trim();
            var logo        = (_logoPath ?? "").Trim();

            var address = string.Join("\n", new[] { addressLine, zipCity }.Where(p => p.Length > 0));

            Db.SetArchitectName(name);
            Db.SetArchitectRef(reference);
            Db.SetArchitectRef2(reference2);
            Db.SetArchitectAddress(address);
            Db.SetArchitectLogoPath(logo);

            StatusTextBlock.Text = "Enregistré localement…";

            if (!string.IsNullOrWhiteSpace(_serverBaseUrl) && !string.IsNullOrWhiteSpace(_serverApiKey))
                await SyncToServerAsync(name, reference, reference2, address, logo);
            else
                StatusTextBlock.Text = "Identité Société enregistrée (serveur non configuré).";

            // ✅ Retour direct au Dashboard après enregistrement (plus besoin de cliquer "Fermer").
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’enregistrer l’identité société.\n\n{ex.Message}",
                "Identité Société",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async System.Threading.Tasks.Task SyncToServerAsync(string name, string refField, string refField2, string address, string logoPath)
    {
        try
        {
            byte[]? logoBytes = null;
            string? contentType = null;

            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                logoBytes = File.ReadAllBytes(logoPath);
                var ext = Path.GetExtension(logoPath).ToLowerInvariant();
                contentType = ext is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png";
            }

            var payload = new
            {
                name,
                refField,
                refField2,
                address,
                logoBase64      = logoBytes != null ? Convert.ToBase64String(logoBytes) : (string?)null,
                logoContentType = contentType
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(payload, options);

            using var client  = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "IziregiClient/1.0"); // ✅ voir MainWindow/WorkOrderWindow
            // ✅ Sécurité : clé API envoyée via l'en-tête HTTP plutôt que dans l'URL.
            if (!string.IsNullOrWhiteSpace(_serverApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", _serverApiKey);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url  = $"{_serverBaseUrl}/internal/architect-identity/upsert";
            var resp = await client.PostAsync(url, content);
            resp.EnsureSuccessStatusCode();

            StatusTextBlock.Text = "✓ Identité Société enregistrée et synchronisée avec le serveur.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Enregistré localement — sync serveur échoué : {ex.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}