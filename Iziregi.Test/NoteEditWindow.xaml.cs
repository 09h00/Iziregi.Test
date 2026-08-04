// File: NoteEditWindow.xaml.cs
using System.Windows;
using System.Windows.Media;

namespace Iziregi.Test;

// ✅ Ajouté (demande de Joe) : édition agrandie d'une note du widget "Bloc-notes"
// (OverviewPage), ouverte par double-clic sur un post-it. ResultText n'est à prendre en
// compte que si "Enregistrer" a été cliqué (DialogResult == true) -- l'appelant
// (OverviewPage) applique alors le changement en base ET met à jour le texte du post-it
// dans la mosaïque.
public partial class NoteEditWindow : Window
{
    public string ResultText { get; private set; } = "";

    // ✅ colorHex reprend exactement la couleur du post-it d'origine (demande de Joe),
    // NoteTileColors dans OverviewPage.xaml.cs.
    public NoteEditWindow(string text, string colorHex)
    {
        InitializeComponent();
        NoteTextBox.Text = text;
        NoteTextBox.Focus();
        NoteTextBox.CaretIndex = NoteTextBox.Text.Length;

        try
        {
            // ✅ Qualifié en System.Windows.Media.* (piège connu du projet : WPF + WinForms
            // référencés, Color/ColorConverter sinon ambigus, CS0104).
            NoteCardBorder.Background = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
        }
        catch
        {
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultText = NoteTextBox.Text;
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
