// File: Pages/ListsPage.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Microsoft.VisualBasic;

namespace Iziregi.Test.Pages;

public partial class ListsPage : UserControl, IReloadablePage
{
    private readonly MainWindow _host;

    public ListsPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;
    }

    public void Reload()
    {
        var places = Db.GetPlaces();
        var companies = Db.GetCompanies();
        var requesters = Db.GetRequesters();

        PlacesListBox.ItemsSource = places;
        CompaniesListBox.ItemsSource = companies;
        RequestersListBox.ItemsSource = requesters;

        DefaultPlaceComboBox.ItemsSource = places;
        DefaultCompanyComboBox.ItemsSource = companies;
        DefaultRequesterComboBox.ItemsSource = requesters;

        // Charger les valeurs par défaut depuis Settings
        var defPlace = Db.GetDefaultPlace();
        var defCompany = Db.GetDefaultCompany();
        var defRequester = Db.GetDefaultRequester();

        if (!string.IsNullOrWhiteSpace(defPlace))
        {
            if (places.Contains(defPlace))
                DefaultPlaceComboBox.SelectedItem = defPlace;
            else
                DefaultPlaceComboBox.Text = defPlace;
        }
        else
        {
            DefaultPlaceComboBox.SelectedItem = null;
            DefaultPlaceComboBox.Text = "";
        }

        if (!string.IsNullOrWhiteSpace(defCompany))
        {
            if (companies.Contains(defCompany))
                DefaultCompanyComboBox.SelectedItem = defCompany;
            else
                DefaultCompanyComboBox.Text = defCompany;
        }
        else
        {
            DefaultCompanyComboBox.SelectedItem = null;
            DefaultCompanyComboBox.Text = "";
        }

        if (!string.IsNullOrWhiteSpace(defRequester))
        {
            if (requesters.Contains(defRequester))
                DefaultRequesterComboBox.SelectedItem = defRequester;
            else
                DefaultRequesterComboBox.Text = defRequester;
        }
        else
        {
            DefaultRequesterComboBox.SelectedItem = null;
            DefaultRequesterComboBox.Text = "";
        }
    }

    // -------------------------
    // Defaults
    // -------------------------
    private void SetDefaultPlace_Click(object sender, RoutedEventArgs e)
    {
        var value = (DefaultPlaceComboBox.Text ?? "").Trim();
        Db.SetDefaultPlace(value);
        MessageBox.Show($"Lieu par défaut défini : {value}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        Reload();
    }

    private void SetDefaultCompany_Click(object sender, RoutedEventArgs e)
    {
        var value = (DefaultCompanyComboBox.Text ?? "").Trim();
        Db.SetDefaultCompany(value);
        MessageBox.Show($"Entreprise par défaut définie : {value}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        Reload();
    }

    private void SetDefaultRequester_Click(object sender, RoutedEventArgs e)
    {
        var value = (DefaultRequesterComboBox.Text ?? "").Trim();
        Db.SetDefaultRequester(value);
        MessageBox.Show($"« Demandé par » par défaut défini : {value}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        Reload();
    }

    // -------------------------
    // Places
    // -------------------------
    private void AddPlace_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewPlaceTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Db.InsertPlace(name);
        NewPlaceTextBox.Text = "";
        Reload();
    }

    private void DeletePlace_Click(object sender, RoutedEventArgs e)
    {
        if (PlacesListBox.SelectedItem is not string name) return;

        var ok = MessageBox.Show($"Supprimer le lieu « {name} » ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes) return;

        Db.DeletePlace(name);
        Reload();
    }

    private void RenamePlace_Click(object sender, RoutedEventArgs e)
    {
        if (PlacesListBox.SelectedItem is not string oldName)
        {
            MessageBox.Show("Sélectionne un lieu à renommer.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = Interaction.InputBox("Nouveau nom :", "Renommer lieu", oldName).Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            Db.RenamePlace(oldName, newName);
            Reload();

            MessageBox.Show(
                $"Lieu renommé.\n\n« {oldName} » → « {newName} »\n\nLes bons existants ont été mis à jour.",
                "OK",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur renommage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -------------------------
    // Companies
    // -------------------------
    private void AddCompany_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewCompanyTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Db.InsertCompany(name);
        NewCompanyTextBox.Text = "";
        Reload();
    }

    private void DeleteCompany_Click(object sender, RoutedEventArgs e)
    {
        if (CompaniesListBox.SelectedItem is not string name) return;

        var ok = MessageBox.Show($"Supprimer l’entreprise « {name} » ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes) return;

        Db.DeleteCompany(name);
        Reload();
    }

    private void RenameCompany_Click(object sender, RoutedEventArgs e)
    {
        if (CompaniesListBox.SelectedItem is not string oldName)
        {
            MessageBox.Show("Sélectionne une entreprise à renommer.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = Interaction.InputBox("Nouveau nom :", "Renommer entreprise", oldName).Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            Db.RenameCompany(oldName, newName);
            Reload();

            MessageBox.Show(
                $"Entreprise renommée.\n\n« {oldName} » → « {newName} »\n\nLes bons existants ont été mis à jour.",
                "OK",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur renommage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -------------------------
    // Requesters (Demandé par)
    // -------------------------
    private void AddRequester_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewRequesterTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Db.InsertRequester(name);
        NewRequesterTextBox.Text = "";
        Reload();
    }

    private void DeleteRequester_Click(object sender, RoutedEventArgs e)
    {
        if (RequestersListBox.SelectedItem is not string name) return;

        var ok = MessageBox.Show($"Supprimer « {name} » de la liste Demandé par ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes) return;

        Db.DeleteRequester(name);
        Reload();
    }

    private void RenameRequester_Click(object sender, RoutedEventArgs e)
    {
        if (RequestersListBox.SelectedItem is not string oldName)
        {
            MessageBox.Show("Sélectionne un élément à renommer.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = Interaction.InputBox("Nouveau nom :", "Renommer (Demandé par)", oldName).Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            Db.RenameRequester(oldName, newName);
            Reload();

            MessageBox.Show(
                $"Valeur renommée.\n\n« {oldName} » → « {newName} »\n\nLes bons existants ont été mis à jour.",
                "OK",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erreur renommage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}