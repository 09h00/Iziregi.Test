// File: UpdateProgressWindow.xaml.cs
// ✅ Fenêtre de progression affichée pendant le téléchargement + le lancement de
// l'installateur, pour que l'utilisateur voie clairement que l'application travaille
// (et ne pense pas qu'elle est figée) pendant les quelques secondes que ça prend.
using System.Windows;

namespace Iziregi.Test;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>Met à jour la barre de progression pendant le téléchargement.
    /// Si la taille totale est inconnue (serveur sans Content-Length), affiche une
    /// barre indéterminée plutôt qu'un pourcentage incorrect.</summary>
    public void ReportProgress(long bytesReceived, long? totalBytes)
    {
        if (totalBytes.HasValue && totalBytes.Value > 0)
        {
            var pct = Math.Min(100.0, (double)bytesReceived / totalBytes.Value * 100.0);
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = pct;
            StatusText.Text = $"Téléchargement de la mise à jour... {pct:0}%";
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
            StatusText.Text = "Téléchargement de la mise à jour...";
        }
    }

    /// <summary>Affiché juste avant de lancer l'installateur téléchargé.</summary>
    public void SetInstalling()
    {
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "Téléchargement terminé — lancement de l'installateur...";
    }
}
