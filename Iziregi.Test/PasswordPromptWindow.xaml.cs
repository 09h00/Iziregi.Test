// File: PasswordPromptWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;

namespace Iziregi.Test;

// ✅ NOUVEAU (18.08.2026, demande de Joe) : prompt réutilisable pour déverrouiller un dossier
// protégé par mot de passe (ou le mot de passe "Directeur", qui ouvre n'importe quel dossier).
// Le mot de passe saisi n'est jamais exposé en dehors de la vérification (PasswordBox, pas de
// binding, comparaison via Verify passé par l'appelant).
public partial class PasswordPromptWindow : Window
{
    private readonly Func<string, bool> _verify;

    public PasswordPromptWindow(string projectName, Func<string, bool> verify)
    {
        InitializeComponent();

        _verify = verify;
        MessageTextBlock.Text = $"Le dossier « {projectName} » est protégé par un mot de passe.";

        Loaded += (_, __) => PasswordInputBox.Focus();
    }

    // ✅ NOUVEAU (18.08.2026) : variante pour les prompts non liés à un dossier (ex: accès à la
    // page Admin), avec un titre/message entièrement personnalisés au lieu du texte "Le dossier
    // «...»" ci-dessus.
    public PasswordPromptWindow(string title, string message, Func<string, bool> verify)
    {
        InitializeComponent();

        _verify = verify;
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        Loaded += (_, __) => PasswordInputBox.Focus();
    }

    private void TryUnlock()
    {
        var pwd = PasswordInputBox.Password ?? "";

        if (string.IsNullOrEmpty(pwd) || !_verify(pwd))
        {
            ErrorTextBlock.Visibility = Visibility.Visible;
            PasswordInputBox.SelectAll();
            PasswordInputBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void PasswordInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryUnlock();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
