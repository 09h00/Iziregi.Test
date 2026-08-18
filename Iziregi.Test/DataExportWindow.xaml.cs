// File: DataExportWindow.xaml.cs
using System;
using System.Windows;
using Iziregi.Test.Services;

namespace Iziregi.Test;

public partial class DataExportWindow : Window
{
    public DataExportWindow()
    {
        InitializeComponent();
    }

    private void ExportAllData_Click(object sender, RoutedEventArgs e)
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
            ExportService.ExportAllData(sfd.FileName);
            System.Windows.MessageBox.Show(
                "Export terminé. Toutes vos données ont été enregistrées dans le fichier zip choisi.",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"L'export a échoué : {ex.Message}",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
