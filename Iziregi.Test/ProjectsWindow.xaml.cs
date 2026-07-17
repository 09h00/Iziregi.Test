// File: ProjectsWindow.xaml.cs
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Microsoft.VisualBasic;
using System.Runtime.InteropServices;

// ✅ Color picker Windows (WinForms)
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

// ✅ Fix ambiguïté MessageBox (WPF)
using WpfMessageBox = System.Windows.MessageBox;

// ✅ Fix ambiguïtés WPF vs WinForms (DataObject/DataFormats/TextBox)
using WpfDataObject = System.Windows.DataObject;
using WpfDataFormats = System.Windows.DataFormats;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Iziregi.Test;

public partial class ProjectsWindow : Window
{
    private Project? _selectedProject;
    private bool _isLoading;

    public ProjectsWindow()
    {
        InitializeComponent();

        Db.Init();

        // ✅ Prévisualisation couleur
        ProjectColorHexTextBox.TextChanged += (_, __) => UpdateColorPreview();

        // ✅ Empêche collage non numérique dans le NPA
        WpfDataObject.AddPastingHandler(ProjectZipTextBox, OnZipPaste);

        LoadProjects();
        ClearForm();
    }

    // =========================
    // Adresse helpers (split/join)
    // =========================
    private static (string line, string zip, string city) SplitAddress(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
            return ("", "", "");

        // Format attendu: "Adresse, NPA Ville"
        var lastComma = s.LastIndexOf(',');
        if (lastComma >= 0 && lastComma < s.Length - 1)
        {
            var addr = s.Substring(0, lastComma).Trim();
            var rest = s.Substring(lastComma + 1).Trim();

            // tente "NPA Ville"
            var parts = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                return (addr, parts[0].Trim(), parts[1].Trim());

            // ex: "1208" seulement
            return (addr, rest.Trim(), "");
        }

        // pas de virgule : tout en adresse
        return (s, "", "");
    }

    private static string JoinAddress(string? line, string? zip, string? city)
    {
        var a = (line ?? "").Trim();
        var z = (zip ?? "").Trim();
        var c = (city ?? "").Trim();

        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(z) && string.IsNullOrWhiteSpace(c))
            return "";

        var zipCity = (z + " " + c).Trim();

        if (string.IsNullOrWhiteSpace(zipCity))
            return a;

        if (string.IsNullOrWhiteSpace(a))
            return zipCity;

        return a + ", " + zipCity;
    }

    // =========================
    // ZIP validation (clavier + collage)
    // =========================
    private static bool IsDigitsOnly(string? s)
        => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);

    // XAML: PreviewTextInput="Zip_PreviewTextInput"
    private void Zip_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // autorise uniquement des chiffres
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private void OnZipPaste(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (!e.DataObject.GetDataPresent(WpfDataFormats.Text))
                return;

            var paste = (e.DataObject.GetData(WpfDataFormats.Text) as string) ?? "";
            paste = paste.Trim();

            if (paste.Length == 0)
                return;

            // refuser si non numérique
            if (!paste.All(char.IsDigit))
            {
                e.CancelCommand();
                return;
            }

            // refuser si dépasse MaxLength (4) en tenant compte de la sélection
            if (sender is WpfTextBox tb)
            {
                var selectionLen = tb.SelectionLength;
                var futureLen = (tb.Text?.Length ?? 0) - selectionLen + paste.Length;
                if (tb.MaxLength > 0 && futureLen > tb.MaxLength)
                    e.CancelCommand();
            }
        }
        catch
        {
            // si doute, on bloque le collage
            e.CancelCommand();
        }
    }

    // =========================
    // Couleur (HEX) helpers
    // =========================
    private static string NormalizeHex(string? s)
    {
        s = (s ?? "").Trim();

        if (string.IsNullOrWhiteSpace(s))
            return "";

        if (!s.StartsWith("#", StringComparison.Ordinal))
            s = "#" + s;

        // On garde seulement #RRGGBB
        if (s.Length > 7)
            s = s.Substring(0, 7);

        return s;
    }

    private void UpdateColorPreview()
    {
        if (ProjectColorPreviewBorder == null) return;

        try
        {
            var hex = NormalizeHex(ProjectColorHexTextBox?.Text);
            if (string.IsNullOrWhiteSpace(hex))
            {
                ProjectColorPreviewBorder.Background = System.Windows.Media.Brushes.Transparent;
                return;
            }

            var brush = new System.Windows.Media.BrushConverter().ConvertFromString(hex) as System.Windows.Media.Brush;
            ProjectColorPreviewBorder.Background = brush ?? System.Windows.Media.Brushes.Transparent;
        }
        catch
        {
            ProjectColorPreviewBorder.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private static string ColorToHex(System.Drawing.Color c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // =========================
    // Color picker
    // =========================
    private void PickProjectColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WpfMessageBox.Show(
                    "Le sélecteur de couleur n'est disponible que sous Windows.",
                    "Couleur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new WinFormsColorDialog
            {
                FullOpen = true
            };

            if (dlg.ShowDialog() != WinFormsDialogResult.OK)
                return;

            ProjectColorHexTextBox.Text = ColorToHex(dlg.Color);
            UpdateColorPreview();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible d’ouvrir le sélecteur de couleur.\n\n{ex.Message}",
                "Couleur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // =========================
    // Confirmation renforcée (code à recopier)
    // =========================
    private static string GenerateDeleteCode(int length = 6)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sans O/0/I/1
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);

        var code = new char[length];
        for (int i = 0; i < length; i++)
            code[i] = chars[bytes[i] % chars.Length];

        return new string(code);
    }

    private static bool RequireDeleteCode(string projectName)
    {
        var code = GenerateDeleteCode(6);

        var input = Interaction.InputBox(
            $"Dernière sécurité avant suppression.\n\n" +
            $"Dossier : {projectName}\n\n" +
            $"Recopie exactement ce code : {code}",
            "Suppression dossier — code obligatoire",
            "");

        return string.Equals((input ?? "").Trim(), code, StringComparison.OrdinalIgnoreCase);
    }

    // =========================
    // Chargement
    // =========================
    private void LoadProjects()
    {
        _isLoading = true;

        try
        {
            var projects = Db.GetProjects(false)
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Name)
                .ToList();

            ProjectsGrid.ItemsSource = projects;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadProjects();
        StatusTextBlock.Text = $"Liste mise à jour : {DateTime.Now:HH:mm}";
    }

    // =========================
    // Sélection (ne remplit plus forcément le formulaire)
    // =========================
    private void ProjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        if (ProjectsGrid.SelectedItem is not Project project)
            return;

        _selectedProject = project;
        StatusTextBlock.Text = $"Dossier sélectionné : {project.Name} (clique sur Modifier)";
    }

    // Bouton XAML: Click="EditProject_Click"
    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null || _selectedProject.Id <= 0)
        {
            WpfMessageBox.Show(
                "Sélectionne d’abord un dossier dans la liste.",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProjectNameTextBox.Text = _selectedProject.Name ?? "";

        var (addrLine, zip, city) = SplitAddress(_selectedProject.Address ?? "");
        ProjectAddressLineTextBox.Text = addrLine;
        ProjectZipTextBox.Text = zip;
        ProjectCityTextBox.Text = city;

        ProjectManagerNameTextBox.Text = _selectedProject.ManagerName ?? "";
        ProjectManagerContactTextBox.Text = _selectedProject.ManagerContact ?? "";

        ProjectIsActiveCheckBox.IsChecked = _selectedProject.IsActive;

        ProjectColorHexTextBox.Text = string.IsNullOrWhiteSpace(_selectedProject.ColorHex) ? "#111827" : _selectedProject.ColorHex;
        UpdateColorPreview();

        StatusTextBlock.Text = $"Modification : {_selectedProject.Name}";
    }

    // =========================
    // Formulaire
    // =========================
    private void ClearForm()
    {
        _selectedProject = null;

        ProjectNameTextBox.Text = "";
        ProjectAddressLineTextBox.Text = "";
        ProjectZipTextBox.Text = "";
        ProjectCityTextBox.Text = "";

        ProjectManagerNameTextBox.Text = "";
        ProjectManagerContactTextBox.Text = "";

        ProjectIsActiveCheckBox.IsChecked = true;

        ProjectColorHexTextBox.Text = "#111827";
        UpdateColorPreview();

        ProjectsGrid.SelectedItem = null;

        StatusTextBlock.Text = "Nouveau dossier.";
        ProjectNameTextBox.Focus();
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var name = (ProjectNameTextBox.Text ?? "").Trim();

        var addressLine = (ProjectAddressLineTextBox.Text ?? "").Trim();
        var zip = (ProjectZipTextBox.Text ?? "").Trim();
        var city = (ProjectCityTextBox.Text ?? "").Trim();

        // ✅ Validation NPA (4 chiffres CH)
        if (!string.IsNullOrWhiteSpace(zip) && (zip.Length != 4 || !zip.All(char.IsDigit)))
        {
            WpfMessageBox.Show(
                "Le code postal doit contenir exactement 4 chiffres (ex: 1208).",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ProjectZipTextBox.Focus();
            return;
        }

        var address = JoinAddress(addressLine, zip, city);

        var managerName = (ProjectManagerNameTextBox.Text ?? "").Trim();
        var managerContact = (ProjectManagerContactTextBox.Text ?? "").Trim();

        var isActive = ProjectIsActiveCheckBox.IsChecked == true;
        var colorHex = NormalizeHex(ProjectColorHexTextBox.Text);

        if (string.IsNullOrWhiteSpace(name))
        {
            WpfMessageBox.Show(
                "Indique le nom du dossier.",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ProjectNameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(addressLine))
        {
            WpfMessageBox.Show(
                "Indique au minimum l’adresse (ligne).",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ProjectAddressLineTextBox.Focus();
            return;
        }

        try
        {
            long idToUpdateColor;

            if (_selectedProject == null || _selectedProject.Id <= 0)
            {
                var newId = Db.InsertProject(name, address);
                idToUpdateColor = newId;

                if (!isActive)
                    Db.SetProjectActive(newId, false);

                Db.SetCurrentProjectId(newId);

                StatusTextBlock.Text = $"Dossier créé : {name}";
            }
            else
            {
                _selectedProject.Name = name;
                _selectedProject.Address = address;
                _selectedProject.IsActive = isActive;
                _selectedProject.ColorHex = colorHex;

                Db.UpdateProject(_selectedProject);

                idToUpdateColor = _selectedProject.Id;

                if (isActive)
                    Db.SetCurrentProjectId(_selectedProject.Id);

                StatusTextBlock.Text = $"Dossier mis à jour : {name}";
            }

            // force la couleur via requête dédiée
            Db.SetProjectColorHex(idToUpdateColor, colorHex);
            Db.SetProjectManager(idToUpdateColor, managerName, managerContact);

            LoadProjects();
            SelectProjectByName(name);

            // Rafraîchir le combobox global si la fenêtre a un Owner MainWindow
            try
            {
                if (this.Owner is Iziregi.Test.MainWindow mw)
                {
                    mw.RefreshProjectSelector();
                }
            }
            catch { }

            try
            {
                System.Diagnostics.Debug.WriteLine($"ProjectsWindow.SaveProject_Click saved id={idToUpdateColor} color={colorHex}");
            }
            catch { }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible d’enregistrer le dossier.\n\n{ex.Message}",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SelectProjectByName(string name)
    {
        if (ProjectsGrid.ItemsSource is not System.Collections.IEnumerable items)
            return;

        foreach (var item in items)
        {
            if (item is Project p &&
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                ProjectsGrid.SelectedItem = p;
                ProjectsGrid.ScrollIntoView(p);
                _selectedProject = p;
                break;
            }
        }
    }

    // =========================
    // Activer / désactiver
    // =========================
    private void DisableProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null || _selectedProject.Id <= 0)
        {
            WpfMessageBox.Show(
                "Sélectionne d’abord un dossier.",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Db.SetProjectActive(_selectedProject.Id, false);

            StatusTextBlock.Text = $"Dossier désactivé : {_selectedProject.Name}";

            LoadProjects();
            ClearForm();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible de désactiver le dossier.\n\n{ex.Message}",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void EnableProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null || _selectedProject.Id <= 0)
        {
            WpfMessageBox.Show(
                "Sélectionne d’abord un dossier.",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Db.SetProjectActive(_selectedProject.Id, true);
            Db.SetCurrentProjectId(_selectedProject.Id);

            StatusTextBlock.Text = $"Dossier réactivé : {_selectedProject.Name}";

            LoadProjects();
            SelectProjectByName(_selectedProject.Name ?? "");
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible de réactiver le dossier.\n\n{ex.Message}",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // =========================
    // Suppression
    // =========================
    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null || _selectedProject.Id <= 0)
        {
            WpfMessageBox.Show(
                "Sélectionne d’abord un dossier.",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var count = Db.GetWorkOrderCountForProject(_selectedProject.Id);

            var msg1 =
                $"Tu es sur le point de supprimer le dossier :\n\n" +
                $"{_selectedProject.Name}\n\n" +
                $"Conséquence : ce dossier ET tous les bons d'intervention associés seront supprimés.\n\n" +
                $"Bons liés : {count}";

            var ok1 = WpfMessageBox.Show(
                msg1,
                "Suppression dossier — avertissement 1/2",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ok1 != MessageBoxResult.Yes)
                return;

            var msg2 =
                $"CONFIRMATION FINALE\n\n" +
                $"Supprimer définitivement :\n" +
                $"- Dossier : {_selectedProject.Name}\n" +
                $"- {count} bon(s) d'intervention + leurs lignes\n\n" +
                $"Cette action est irréversible.";

            var ok2 = WpfMessageBox.Show(
                msg2,
                "Suppression dossier — avertissement 2/2",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (ok2 != MessageBoxResult.Yes)
                return;

            if (!RequireDeleteCode(_selectedProject.Name ?? ""))
            {
                WpfMessageBox.Show(
                    "Code incorrect. Suppression annulée.",
                    "Dossier",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Db.DeleteProjectAndWorkOrders(_selectedProject.Id);

            StatusTextBlock.Text = $"Dossier supprimé (et {count} bon(s)) : {_selectedProject.Name}";
            LoadProjects();
            ClearForm();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible de supprimer le dossier.\n\n{ex.Message}",
                "Dossier",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // =========================
    // Fermeture
    // =========================
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}