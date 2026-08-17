// File: PasswordResetWindow.xaml.cs
using System.Windows;

namespace Iziregi.Test;

// ✅ NOUVEAU (18.08.2026, demande de Joe) : formulaire pur (code + nouveau mot de passe),
// aucun appel réseau ici -- la vérification du code auprès du serveur et l'écriture du
// nouveau mot de passe en local (Db.SetDirectorPassword) sont faites par l'appelant
// (SettingsPage.ForgotDirectorPassword_Click) une fois ce dialogue validé.
public partial class PasswordResetWindow : Window
{
    public string Code { get; private set; } = "";
    public string NewPassword { get; private set; } = "";

    public PasswordResetWindow()
    {
        InitializeComponent();
        Loaded += (_, __) => CodeTextBox.Focus();
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var code = (CodeTextBox.Text ?? "").Trim();
        var pwd = NewPasswordBox.Password ?? "";
        var confirm = ConfirmPasswordBox.Password ?? "";

        if (string.IsNullOrEmpty(code))
        {
            ShowError("Saisis le code reçu par email.");
            return;
        }

        if (string.IsNullOrEmpty(pwd))
        {
            ShowError("Saisis un nouveau mot de passe.");
            return;
        }

        if (pwd != confirm)
        {
            ShowError("Les 2 mots de passe ne correspondent pas.");
            return;
        }

        Code = code;
        NewPassword = pwd;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
