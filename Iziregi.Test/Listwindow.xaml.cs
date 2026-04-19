using System.Windows;
using Iziregi.Test.Data;

namespace Iziregi.Test;

public partial class ListsWindow : Window
{
    public ListsWindow()
    {
        InitializeComponent();
        ReloadLists();
    }

    private void ReloadLists()
    {
        PlacesListBox.ItemsSource = Db.GetPlaces();
        CompaniesListBox.ItemsSource = Db.GetCompanies();
    }

    private void AddPlace_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPlaceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Db.InsertPlace(name);
        NewPlaceTextBox.Text = "";
        ReloadLists();
    }

    private void DeletePlace_Click(object sender, RoutedEventArgs e)
    {
        if (PlacesListBox.SelectedItem is not string name) return;

        Db.DeletePlace(name);
        ReloadLists();
    }

    private void AddCompany_Click(object sender, RoutedEventArgs e)
    {
        var name = NewCompanyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Db.InsertCompany(name);
        NewCompanyTextBox.Text = "";
        ReloadLists();
    }

    private void DeleteCompany_Click(object sender, RoutedEventArgs e)
    {
        if (CompaniesListBox.SelectedItem is not string name) return;

        Db.DeleteCompany(name);
        ReloadLists();
    }
}