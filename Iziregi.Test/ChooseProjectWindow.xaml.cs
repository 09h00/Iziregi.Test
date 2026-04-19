using System.Windows;
using System.Windows.Controls;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test;

public partial class ChooseProjectWindow : Window
{
    private List<Project> _allProjects = new();

    public Project? SelectedProject { get; private set; }

    public ChooseProjectWindow()
    {
        InitializeComponent();
        LoadProjects();
    }

    private void LoadProjects()
    {
        _allProjects = Db.GetProjects(onlyActive: true);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = (SearchTextBox.Text ?? "").Trim().ToLowerInvariant();

        var filtered = string.IsNullOrWhiteSpace(q)
            ? _allProjects
            : _allProjects.Where(p =>
                (p.Name ?? "").ToLowerInvariant().Contains(q) ||
                (p.Address ?? "").ToLowerInvariant().Contains(q)
            ).ToList();

        ProjectsGrid.ItemsSource = filtered;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not Project p)
        {
            MessageBox.Show("Sélectionne un projet dans la liste.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedProject = p;
        DialogResult = true;
        Close();
    }

    private void ProjectsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Select_Click(sender, e);
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Nom du chantier :", "Nouveau projet", "");

        if (string.IsNullOrWhiteSpace(name))
            return;

        var address = Microsoft.VisualBasic.Interaction.InputBox(
            "Adresse du chantier :", "Nouveau projet", "");

        if (string.IsNullOrWhiteSpace(address))
            return;

        Db.InsertProject(name, address);
        LoadProjects();
    }
}