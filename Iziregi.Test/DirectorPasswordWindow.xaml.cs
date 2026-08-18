// File: DirectorPasswordWindow.xaml.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Iziregi.Test.Data;

namespace Iziregi.Test;

// ✅ Fenêtre "Mot de passe général des dossiers" (18.08.2026, demande de Joe) : extraite de la
// page Paramètres > Admin, y compris le flux de réinitialisation par email (inchangé, voir
// Iziregi.Server /internal/password-reset/request et /verify).
public partial class DirectorPasswordWindow : Window
{
    private static readonly HttpClient ResetHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly System.Windows.Media.Brush LockEnabledBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly System.Windows.Media.Brush LockDisabledBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC2, 0x66, 0x0A));

    public DirectorPasswordWindow()
    {
        InitializeComponent();
        PopulateStatus();
        PopulateLockStatus();
    }

    private void PopulateStatus()
    {
        var hasPassword = Db.HasDirectorPassword();
        StatusTextBlock.Text = hasPassword
            ? "Un mot de passe général des dossiers est actuellement défini."
            : "Aucun mot de passe général des dossiers défini.";
        ChangeButton.Content = hasPassword ? "Changer le mot de passe" : "Définir un mot de passe";
        // ✅ "Mot de passe actuel" masqué tant qu'aucun mot de passe n'est encore défini (rien
        // à confirmer dans ce cas), demande de Joe.
        CurrentPasswordSection.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
    }

    // ✅ NOUVEAU (18.08.2026, demande de Joe) : remplace l'ancien bouton "Retirer la
    // protection" par un interrupteur "Activer/Désactiver verrouillage dossiers" -- le
    // directeur décide si l'ouverture des dossiers exige un mot de passe, sans effacer les
    // mots de passe individuels déjà enregistrés sur chaque dossier. Statut coloré (vert =
    // activé, orange = désactivé), demande de Joe.
    private void PopulateLockStatus()
    {
        var enabled = Db.GetDossierLockEnabled();
        LockStatusTextBlock.Text = enabled
            ? "Verrouillage des dossiers : Activé"
            : "Verrouillage des dossiers : Désactivé";
        LockStatusTextBlock.Foreground = enabled ? LockEnabledBrush : LockDisabledBrush;
        ToggleLockButton.Content = enabled ? "Désactiver verrouillage dossiers" : "Activer verrouillage dossiers";

        LockClosedIcon.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LockOpenIcon.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LockClosedBody.Fill = LockEnabledBrush;
        LockClosedShackle.Stroke = LockEnabledBrush;
        LockOpenBody.Fill = LockDisabledBrush;
        LockOpenShackle.Stroke = LockDisabledBrush;
    }

    private void ToggleLockButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = Db.GetDossierLockEnabled();

        if (enabled)
        {
            var ok = System.Windows.MessageBox.Show(
                "Désactiver le verrouillage des dossiers ? Tous les dossiers s'ouvriront sans demande de mot de passe, même ceux qui en ont un défini. Les mots de passe existants ne sont pas effacés -- réactiver le verrouillage les remet en application.",
                "Verrouillage des dossiers",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ok != MessageBoxResult.Yes)
                return;
        }

        Db.SetDossierLockEnabled(!enabled);
        PopulateLockStatus();
    }

    // ✅ Formulaire intégré (18.08.2026, demande de Joe : "on peut tout mettre dans la même
    // fenêtre") : "Mot de passe actuel" (si un mot de passe est déjà défini), "Nouveau mot de
    // passe", "Confirmer le nouveau mot de passe", tout sur cette fenêtre -- plus de
    // PasswordPromptWindow/NewPasswordWindow séparées.
    private void ChangeButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorTextBlock.Visibility = Visibility.Collapsed;

        if (Db.HasDirectorPassword())
        {
            var current = CurrentPasswordBox.Password ?? "";
            if (string.IsNullOrEmpty(current) || !Db.VerifyDirectorPassword(current))
            {
                ShowError("Mot de passe actuel incorrect.");
                return;
            }
        }

        var newPwd = NewPasswordBox.Password ?? "";
        var confirmPwd = ConfirmPasswordBox.Password ?? "";

        if (string.IsNullOrEmpty(newPwd))
        {
            ShowError("Saisis un nouveau mot de passe.");
            return;
        }

        if (newPwd != confirmPwd)
        {
            ShowError("Les 2 mots de passe ne correspondent pas.");
            return;
        }

        Db.SetDirectorPassword(newPwd);
        CurrentPasswordBox.Password = "";
        NewPasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
        PopulateStatus();

        // ✅ Confirmation (18.08.2026, demande de Joe) : "je veux un pop up me signalant que le
        // nouveau mot de passe est enregistré".
        System.Windows.MessageBox.Show(
            "Le nouveau mot de passe a été enregistré.",
            "Mot de passe général des dossiers",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

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

    private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Un code à usage unique va être envoyé par email à l'adresse de contact enregistrée pour ton bureau. Continuer ?",
            "Mot de passe oublié",
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

                System.Windows.MessageBox.Show(message, "Mot de passe oublié", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible de joindre le serveur.\n\n{ex.Message}",
                "Mot de passe oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var resetWin = new PasswordResetWindow
        {
            Owner = this
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

                System.Windows.MessageBox.Show(message, "Mot de passe oublié", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Db.SetDirectorPassword(resetWin.NewPassword);
            PopulateStatus();

            System.Windows.MessageBox.Show(
                "Le mot de passe général des dossiers a été réinitialisé avec succès.",
                "Mot de passe oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible de joindre le serveur.\n\n{ex.Message}",
                "Mot de passe oublié",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
