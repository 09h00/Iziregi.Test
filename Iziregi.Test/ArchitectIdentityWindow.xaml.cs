// File: ArchitectIdentityWindow.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Iziregi.Test.Data;

namespace Iziregi.Test;

public partial class ArchitectIdentityWindow : Window
{
    private string _logoPath = "";

    public ArchitectIdentityWindow()
    {
        InitializeComponent();
        Db.Init();
        LoadFromDb();
    }

    private void LoadFromDb()
    {
        ArchitectNameTextBox.Text = Db.GetArchitectName();
        ArchitectAddressTextBox.Text = Db.GetArchitectAddress();

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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Db.SetArchitectName((ArchitectNameTextBox.Text ?? "").Trim());
            Db.SetArchitectAddress((ArchitectAddressTextBox.Text ?? "").Trim());
            Db.SetArchitectLogoPath((_logoPath ?? "").Trim());

            StatusTextBlock.Text = "Identité architecte enregistrée.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’enregistrer l’identité architecte.\n\n{ex.Message}",
                "Identité architecte",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}