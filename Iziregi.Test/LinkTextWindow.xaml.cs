// File: LinkTextWindow.xaml.cs
using System.Windows;

namespace Iziregi.Test;

// ✅ 11.08.2026 (demande de Joe) : édite le modèle par défaut de texte avant/après le lien
// magique (Devis ou Validation, un jeu distinct par contexte -- voir Db.GetQuoteLinkTextBefore/
// GetSignatureLinkTextBefore). Toujours enregistré comme nouveau modèle par défaut à la
// fermeture via "Enregistrer" -- pas de mode "juste pour cet envoi", pour rester simple.
public partial class LinkTextWindow : Window
{
    public string BeforeText { get; private set; }
    public string AfterText { get; private set; }

    public LinkTextWindow(string title, string beforeText, string afterText)
    {
        InitializeComponent();

        TitleTextBlock.Text = title;
        BeforeTextBox.Text = beforeText ?? "";
        AfterTextBox.Text = afterText ?? "";

        BeforeText = beforeText ?? "";
        AfterText = afterText ?? "";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        BeforeText = BeforeTextBox.Text ?? "";
        AfterText = AfterTextBox.Text ?? "";
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
