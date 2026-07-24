// File: TaskDescriptionWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using Iziregi.Test.Services;

// ✅ Fix ambiguïté MessageBox (WPF)
using WpfMessageBox = System.Windows.MessageBox;

namespace Iziregi.Test;

// ✅ Ajouté le 16.07.2026 (demande de Joe) : éditeur agrandi + génération PDF pour le champ
// "Descriptif" du tableau des Tâches (page Planning). Ouvert en modal depuis PlanningPage
// (TaskExpandDescriptionButton_Click) avec les valeurs actuelles de la ligne ; ne modifie rien tant
// que l'utilisateur n'a pas cliqué sur "Enregistrer" (DialogResult == true), à ce moment
// PlanningPage relit ResultText et l'applique sur la ligne (TaskRow.Todo).
public partial class TaskDescriptionWindow : Window
{
    private readonly string _taskRef;
    private readonly string _company;
    private readonly string _building;
    private readonly string _floor;
    private readonly string _category;
    private readonly string _reserve;

    public string ResultText { get; private set; } = "";

    // ✅ Avertissement si fermeture sans avoir cliqué "Enregistrer" (demande de Joe,
    // 16.07.2026) : mémorise le texte de départ pour détecter une vraie modification,
    // et évite de redemander une fois que l'utilisateur a confirmé qu'il veut abandonner.
    private string _originalTodo = "";
    private bool _discardConfirmed;

    private bool HasUnsavedChanges => DescriptifTextBox.Text != _originalTodo;

    // ✅ 16.07.2026 (2e demande de Joe) : en-tête réordonné comme la grille (Bâtiment,
    // Étage, Catégorie, Entreprise, Urg., Effectué), libellés dynamiques (page Listes), et
    // un champ n'apparaît QUE si sa colonne est actuellement visible dans la grille
    // (showCompany/showBuilding/showFloor/showCategory/showUrgent — "Effectué" n'a pas de
    // sélecteur de colonne, donc toujours visible).
    public TaskDescriptionWindow(
        string taskRef,
        string company,
        string building,
        string floor,
        string category,
        string reserve,
        string urgent,
        bool done,
        string todo,
        string companyLabel,
        string buildingLabel,
        string floorLabel,
        string categoryLabel,
        string urgentLabel,
        bool showCompany,
        bool showBuilding,
        bool showFloor,
        bool showCategory,
        bool showUrgent)
    {
        InitializeComponent();

        _taskRef = taskRef ?? "";
        _company = company ?? "";
        _building = building ?? "";
        _floor = floor ?? "";
        _category = category ?? "";
        _reserve = reserve ?? "";

        TaskRefTextBlock.Text = string.IsNullOrWhiteSpace(_taskRef) ? "Descriptif de la tâche" : $"Tâche N° {_taskRef}";

        BuildingLabelTextBlock.Text = string.IsNullOrWhiteSpace(buildingLabel) ? "Bâtiment" : buildingLabel;
        FloorLabelTextBlock.Text = string.IsNullOrWhiteSpace(floorLabel) ? "Étage" : floorLabel;
        CategoryLabelTextBlock.Text = string.IsNullOrWhiteSpace(categoryLabel) ? "Catégorie" : categoryLabel;
        CompanyLabelTextBlock.Text = string.IsNullOrWhiteSpace(companyLabel) ? "Entreprise" : companyLabel;
        UrgentLabelTextBlock.Text = string.IsNullOrWhiteSpace(urgentLabel) ? "Urg." : urgentLabel;

        BuildingTextBlock.Text = string.IsNullOrWhiteSpace(_building) ? "—" : _building;
        FloorTextBlock.Text = string.IsNullOrWhiteSpace(_floor) ? "—" : _floor;
        CategoryTextBlock.Text = string.IsNullOrWhiteSpace(_category) ? "—" : _category;
        CompanyTextBlock.Text = string.IsNullOrWhiteSpace(_company) ? "—" : _company;
        UrgentTextBlock.Text = string.IsNullOrWhiteSpace(urgent) ? "—" : urgent;
        DoneTextBlock.Text = done ? "Oui" : "Non";

        BuildingPanel.Visibility = showBuilding ? Visibility.Visible : Visibility.Collapsed;
        FloorPanel.Visibility = showFloor ? Visibility.Visible : Visibility.Collapsed;
        CategoryPanel.Visibility = showCategory ? Visibility.Visible : Visibility.Collapsed;
        CompanyPanel.Visibility = showCompany ? Visibility.Visible : Visibility.Collapsed;
        UrgentPanel.Visibility = showUrgent ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_reserve))
            ReservePanel.Visibility = Visibility.Collapsed;
        else
            ReserveTextBlock.Text = _reserve;

        _originalTodo = todo ?? "";
        DescriptifTextBox.Text = _originalTodo;
        DescriptifTextBox.Focus();
        DescriptifTextBox.CaretIndex = DescriptifTextBox.Text.Length;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultText = DescriptifTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ✅ Lit les panneaux (libellé + valeur) réellement affichés dans l'en-tête de CETTE
    // fenêtre — ni plus, ni moins — pour que le PDF reflète toujours exactement ce que
    // l'utilisateur voit à l'écran (mêmes colonnes visibles, mêmes libellés dynamiques).
    // Corrige le bug du 16.07.2026 où Urg./Effectué manquaient dans le PDF.
    private List<(string Label, string Value)> GetVisibleInfoFields()
    {
        var fields = new List<(string, string)>();

        foreach (var panel in new[] { BuildingPanel, FloorPanel, CategoryPanel, CompanyPanel, UrgentPanel, DonePanel, ReservePanel })
        {
            if (panel.Visibility != Visibility.Visible) continue;
            if (panel.Children.Count < 2) continue;

            if (panel.Children[0] is TextBlock labelBlock && panel.Children[1] is TextBlock valueBlock)
                fields.Add((labelBlock.Text, valueBlock.Text));
        }

        return fields;
    }

    // ✅ Se déclenche pour TOUTE fermeture (Annuler, croix en haut à droite, Alt+F4) —
    // pas seulement le bouton Annuler. On ne prévient PAS si "Enregistrer" a déjà été
    // cliqué (DialogResult == true) ni si le texte n'a en fait pas changé.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true && HasUnsavedChanges && !_discardConfirmed)
        {
            var result = WpfMessageBox.Show(
                this,
                "Vous avez des modifications non enregistrées.\nVoulez-vous vraiment fermer sans enregistrer ?",
                "Iziregi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _discardConfirmed = true;
        }

        base.OnClosing(e);
    }

    private void GeneratePdfButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichier PDF (*.pdf)|*.pdf",
                FileName = string.IsNullOrWhiteSpace(_taskRef) ? "Descriptif.pdf" : $"Descriptif - Tache {_taskRef}.pdf"
            };

            if (dlg.ShowDialog() != true) return;

            PdfService.GenerateTaskDescriptionPdf(
                dlg.FileName,
                _taskRef,
                GetVisibleInfoFields(),
                DescriptifTextBox.Text);

            // ✅ Ouvre automatiquement le PDF (23.07.2026, demande de Joe : tous les pdf générés
            // dans l'app doivent s'ouvrir automatiquement).
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch { }

            WpfMessageBox.Show(this, "PDF généré avec succès.", "Iziregi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, "Impossible de générer le PDF :\n" + ex.Message, "Iziregi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
