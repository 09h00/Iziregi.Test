// File: Pages/AddressBookPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Iziregi.Test.Data;

// ✅ Alias WPF (piège connu de ce projet : UseWPF + UseWindowsForms tous les deux activés
// dans le .csproj -> usings implicites des deux frameworks sur TOUT le projet, Button/
// TextBox/CheckBox/Color/ColorConverter/MessageBox/Clipboard/Brushes/Cursors/Orientation
// sinon ambigus, CS0104, même sans "using System.Windows.Forms;" explicite dans ce fichier).
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;
using WpfCursors = System.Windows.Input.Cursors;
using WpfEllipse = System.Windows.Shapes.Ellipse;

namespace Iziregi.Test.Pages;

// ✅ Ajouté (demande de Joe, 04.08.2026) : carnet d'adresses structuré par dossier, façon
// carnet d'adresses conventionnel. Contacts et Groupes suivent EXACTEMENT le même principe
// (demande de Joe, 3e passe) : une liste à gauche/droite (nom cliquable), et UNE SEULE fiche
// au centre pour l'élément sélectionné (contact OU groupe, mutuellement exclusifs). La fiche
// contact a une section "Groupes" (cases à cocher), la fiche groupe une section "Contacts"
// (cases à cocher) -- l'affectation d'un contact à un groupe se fait donc indifféremment
// depuis l'une ou l'autre fiche, sans mécanisme de sélection multiple séparé.
//
// Envoi groupé par mailto: (ouvre le client mail par défaut) plutôt qu'un envoi SMTP intégré
// -- ça demanderait des identifiants de messagerie stockés dans l'app pour un gain limité.
//
// ✅ 4e passe (demande de Joe, 04.08.2026) : page embarquée (comme les autres pages, menu de
// navigation visible, pleine page) au lieu d'une fenêtre modale séparée -- même principe que
// ListsPage (_projectId relu à chaque Reload() via Db.GetCurrentProjectId(), plutôt que figé
// à la construction, pour suivre les changements de dossier actif sans rouvrir la page).
public partial class AddressBookPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;
    private long _projectId;
    private List<Db.Contact> _contacts = new();
    private List<Db.ContactGroup> _groups = new();

    // ✅ Sélection mutuellement exclusive : sélectionner un contact désélectionne le groupe
    // actif, et vice-versa (une seule fiche affichée à la fois au centre).
    private long? _selectedContactId;
    private long? _selectedGroupId;

    // ✅ Cases à cocher par contact + "Sélectionner tout" (demande de Joe).
    private readonly HashSet<long> _checkedContactIds = new();

    // ✅ "Copier" copie le champ actuellement sélectionné (demande de Joe), pas toujours
    // l'e-mail. Mémorisé via GotFocus (voir BuildContactDetail) : au moment du clic sur le
    // bouton, le focus clavier est déjà passé au bouton lui-même (Keyboard.FocusedElement ne
    // pointerait plus sur le TextBox), ce champ garde donc la référence du dernier TextBox
    // réellement édité.
    private WpfTextBox? _lastFocusedFieldTextBox;

    public AddressBookPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        Reload();
    }

    public void Reload()
    {
        _projectId = Db.GetCurrentProjectId() ?? 0;
        _selectedContactId = null;
        _selectedGroupId = null;
        _checkedContactIds.Clear();

        RebuildContacts();
        RebuildGroupsList();
    }

    // =========================
    // Liste des contacts
    // =========================
    private void RebuildContacts()
    {
        // ✅ Ordre alphabétique (demande de Joe), sans en-têtes de lettre (retirées, 2e
        // demande de Joe) : les contacts sans nom ("(Sans nom)") sont triés en dernier.
        _contacts = Db.GetContacts(_projectId)
            .OrderBy(c => string.IsNullOrWhiteSpace(c.Name) ? 1 : 0)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_selectedContactId.HasValue && _contacts.All(c => c.Id != _selectedContactId.Value))
            _selectedContactId = null;

        // ✅ Une case cochée pour un contact supprimé entre-temps n'a plus de sens.
        _checkedContactIds.IntersectWith(_contacts.Select(c => c.Id));

        ContactsListPanel.Children.Clear();

        foreach (var contact in _contacts)
        {
            var displayName = string.IsNullOrWhiteSpace(contact.Name) ? "(Sans nom)" : contact.Name.Trim();
            var contactId = contact.Id;
            var checkBox = new WpfCheckBox { IsChecked = _checkedContactIds.Contains(contactId), VerticalAlignment = VerticalAlignment.Center };
            checkBox.Click += (s, e) =>
            {
                if (checkBox.IsChecked == true) _checkedContactIds.Add(contactId);
                else _checkedContactIds.Remove(contactId);
                UpdateSelectAllContactsCheckBoxState();
                // ✅ Fix (demande de Joe : "aucun panneau n'apparaît") : oublié ici, le panneau
                // n'était réévalué que par RebuildContacts() (rebuild complet), jamais appelé
                // sur un simple clic de case individuelle.
                UpdateBulkActionsPanel();
            };

            ContactsListPanel.Children.Add(BuildListRow(
                text: displayName,
                isSelected: _selectedContactId == contact.Id,
                onClick: () =>
                {
                    _selectedContactId = contact.Id;
                    _selectedGroupId = null;
                    RebuildContacts();
                    RebuildGroupsList();
                },
                onDelete: () =>
                {
                    Db.DeleteContact(contactId);
                    if (_selectedContactId == contactId) _selectedContactId = null;
                    RebuildContacts();
                },
                leadingCheckBox: checkBox));
        }

        UpdateSelectAllContactsCheckBoxState();
        UpdateBulkActionsPanel();
        RebuildDetail();
    }


    private void UpdateSelectAllContactsCheckBoxState()
    {
        SelectAllContactsCheckBox.IsChecked = _contacts.Count > 0 && _contacts.All(c => _checkedContactIds.Contains(c.Id));
    }

    private void SelectAllContactsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (SelectAllContactsCheckBox.IsChecked == true)
            _checkedContactIds.UnionWith(_contacts.Select(c => c.Id));
        else
            _checkedContactIds.Clear();

        RebuildContacts();
    }

    private void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedContactId = Db.InsertContact(_projectId);
        _selectedGroupId = null;
        RebuildContacts();
        RebuildGroupsList();
    }

    // ✅ Export/Import CSV (demande de Joe, 04.08.2026) : format propre à l'app (colonnes dans
    // l'ordre des champs de la fiche Contact), pas de compatibilité avec des formats externes
    // (vCard, Outlook, Gmail...) -- décision volontaire, voir discussion avec Joe.
    private static readonly string[] ContactCsvHeader =
    {
        "Titre", "Nom", "Intervenant", "Entreprise", "Adresse",
        "Téléphone mobile", "Téléphone bureau", "E-mail", "Site web", "Champ libre"
    };

    private void ExportContactsCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter les contacts",
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"Contacts-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                AddExtension = true,
                DefaultExt = ".csv",
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", ContactCsvHeader.Select(CsvEscape)));

            foreach (var c in _contacts)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    c.Title, c.Name, c.Intervenant, c.Company, c.Address,
                    c.Phone, c.Phone2, c.Email, c.Website, c.FreeText
                }.Select(CsvEscape)));
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
            WpfMessageBox.Show($"{_contacts.Count} contact(s) exporté(s).", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Impossible d'exporter les contacts.\n\n{ex.Message}", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportContactsCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importer des contacts",
                Filter = "CSV (*.csv)|*.csv",
            };
            if (dlg.ShowDialog() != true) return;

            var text = System.IO.File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            var rows = ParseCsv(text);
            if (rows.Count == 0) return;

            // ✅ Ignore la ligne d'en-tête si elle correspond au format exporté -- pas de
            // mapping de colonnes par nom, ordre fixe attendu (notre propre format, voir
            // ContactCsvHeader).
            var startIndex = rows[0].Length > 0 && string.Equals(rows[0][0].Trim(), "Titre", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            var imported = 0;
            for (var i = startIndex; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length <= 1 && string.IsNullOrWhiteSpace(r.Length > 0 ? r[0] : ""))
                    continue;

                string Get(int idx) => idx < r.Length ? r[idx] : "";

                var contactId = Db.InsertContact(_projectId);
                if (contactId <= 0) continue;

                Db.UpdateContact(contactId, "Title", Get(0));
                Db.UpdateContact(contactId, "Name", Get(1));
                Db.UpdateContact(contactId, "Intervenant", Get(2));
                Db.UpdateContact(contactId, "Company", Get(3));
                Db.UpdateContact(contactId, "Address", Get(4));
                Db.UpdateContact(contactId, "Phone", Get(5));
                Db.UpdateContact(contactId, "Phone2", Get(6));
                Db.UpdateContact(contactId, "Email", Get(7));
                Db.UpdateContact(contactId, "Website", Get(8));
                Db.UpdateContact(contactId, "FreeText", Get(9));
                imported++;
            }

            RebuildContacts();
            WpfMessageBox.Show($"{imported} contact(s) importé(s).", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Impossible d'importer le fichier.\n\n{ex.Message}", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string CsvEscape(string? s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // ✅ Parseur CSV minimal (gère guillemets, virgules/retours à la ligne dans un champ
    // guillemeté, guillemets échappés) -- symétrique de CsvEscape ci-dessus.
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            if (c == '"') { inQuotes = true; i++; continue; }
            if (c == ',') { row.Add(field.ToString()); field.Clear(); i++; continue; }
            if (c == '\r') { i++; continue; }
            if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row = new List<string>();
                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    // =========================
    // Liste des groupes
    // =========================
    private void RebuildGroupsList()
    {
        _groups = Db.GetContactGroups(_projectId);

        if (_selectedGroupId.HasValue && _groups.All(g => g.Id != _selectedGroupId.Value))
            _selectedGroupId = null;

        GroupsListPanel.Children.Clear();
        foreach (var group in _groups)
            GroupsListPanel.Children.Add(BuildListRow(
                text: string.IsNullOrWhiteSpace(group.Name) ? "(Sans nom)" : group.Name,
                isSelected: _selectedGroupId == group.Id,
                onClick: () =>
                {
                    _selectedGroupId = group.Id;
                    _selectedContactId = null;
                    RebuildContacts();
                    RebuildGroupsList();
                },
                onDelete: () =>
                {
                    Db.DeleteContactGroup(group.Id);
                    if (_selectedGroupId == group.Id) _selectedGroupId = null;
                    RebuildGroupsList();
                }));

        UpdateBulkActionsPanel();
        RebuildDetail();
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        // ✅ Champ "Nom" laissé vide à la création (demande de Joe, pas de "Nouveau groupe"
        // pré-rempli).
        _selectedGroupId = Db.InsertContactGroup(_projectId, "");
        _selectedContactId = null;
        RebuildGroupsList();
    }

    // ✅ Ligne de liste partagée (demande de Joe : "même principe" pour contacts et groupes) :
    // nom cliquable (sélectionne l'élément et affiche sa fiche au centre) + croix de
    // suppression, surlignée en bleu clair quand sélectionnée. Case à cocher optionnelle
    // (demande de Joe, uniquement utilisée pour les contacts) insérée en 1ère colonne.
    private Border BuildListRow(string text, bool isSelected, Action onClick, Action onDelete, WpfCheckBox? leadingCheckBox = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        if (leadingCheckBox != null)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameColumn = 0;
        if (leadingCheckBox != null)
        {
            Grid.SetColumn(leadingCheckBox, 0);
            row.Children.Add(leadingCheckBox);
            nameColumn = 1;
        }

        var nameText = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = WpfBrushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = WpfCursors.Hand,
        };
        nameText.MouseLeftButtonDown += (s, e) => onClick();
        Grid.SetColumn(nameText, nameColumn);
        row.Children.Add(nameText);

        var deleteButton = new WpfButton { Content = "✕", Style = (Style)Resources["DeleteButtonStyle"] };
        deleteButton.Click += (s, e) => onDelete();
        Grid.SetColumn(deleteButton, nameColumn + 1);
        row.Children.Add(deleteButton);

        return new Border
        {
            Background = isSelected ? new SolidColorBrush(WpfColor.FromRgb(0xDB, 0xEA, 0xFE)) : WpfBrushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2, 4, 2, 4),
            Child = row,
        };
    }

    // =========================
    // Fiche unique (contact OU groupe)
    // =========================
    private void RebuildDetail()
    {
        DetailPanel.Children.Clear();

        if (_selectedContactId.HasValue)
        {
            var contact = _contacts.FirstOrDefault(c => c.Id == _selectedContactId.Value);
            if (contact != null)
            {
                CloseDetailButton.Visibility = Visibility.Visible;
                BuildContactDetail(contact);
                return;
            }
        }

        if (_selectedGroupId.HasValue)
        {
            var group = _groups.FirstOrDefault(g => g.Id == _selectedGroupId.Value);
            if (group != null)
            {
                CloseDetailButton.Visibility = Visibility.Visible;
                BuildGroupDetail(group);
                return;
            }
        }

        CloseDetailButton.Visibility = Visibility.Collapsed;
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Sélectionnez un contact ou un groupe dans les listes, ou créez-en un nouveau.",
            FontSize = 13,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8)),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    // ✅ Fermeture manuelle de la fiche (demande de Joe) : croix flottante (XAML) et bouton
    // "Fermer" (bas de chaque fiche) font tous les deux la même chose.
    private void CloseDetail()
    {
        _selectedContactId = null;
        _selectedGroupId = null;
        RebuildContacts();
        RebuildGroupsList();
    }

    private void CloseDetailButton_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private void BuildContactDetail(Db.Contact contact)
    {
        // ✅ Évite de référencer un TextBox d'une fiche précédente déjà démontée.
        _lastFocusedFieldTextBox = null;

        // ✅ Largeur FIXE (au lieu de "*", demande de Joe) pour la colonne des champs : la
        // Border de la fiche est maintenant HorizontalAlignment="Left" (dimensionnée à son
        // contenu), une colonne "*" n'aurait plus rien vers quoi s'étirer et s'effondrerait.
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // ✅ Largeur +3cm (280 -> 393px, demande de Joe, 04.08.2026) : 3cm = 113px à 96 DPI.
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(393) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        const int rowCount = 10;
        for (var i = 0; i < rowCount; i++)
            fields.RowDefinitions.Add(new RowDefinition());

        WpfTextBox AddRow(int row, string label, string value)
        {
            var lbl = new TextBlock { Text = label, Style = (Style)Resources["FieldLabelStyle"] };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            fields.Children.Add(lbl);

            var tb = new WpfTextBox { Text = value, Style = (Style)Resources["FieldTextBoxStyle"] };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, 1);
            tb.GotFocus += (s, e) => _lastFocusedFieldTextBox = tb;
            fields.Children.Add(tb);

            return tb;
        }

        // ✅ "Intervenant" (demande de Joe, 04.08.2026), en 2e position : lié à la liste
        // Entreprise du projet (Db.GetCompanies/GetCompanyColorMap, même source que Bons
        // d'intervention/Planning), avec pastille de couleur bien visible. ComboBox éditable
        // (IsEditable=True) : propose les entreprises connues mais accepte aussi du texte libre
        // (un contact n'est pas forcément une des entreprises intervenantes du projet).
        WpfComboBox AddIntervenantRow(int row, string value)
        {
            var lbl = new TextBlock { Text = "Intervenant", Style = (Style)Resources["FieldLabelStyle"] };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            fields.Children.Add(lbl);

            var cell = new Grid();
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var swatch = new WpfEllipse
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(0xD1, 0xD5, 0xDB)),
                StrokeThickness = 1,
                Fill = WpfBrushes.Transparent,
            };
            Grid.SetColumn(swatch, 0);
            cell.Children.Add(swatch);

            var colorMap = Db.GetCompanyColorMap(_projectId);

            void UpdateSwatch(string? text)
            {
                var name = (text ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name) && colorMap.TryGetValue(name, out var hex))
                {
                    try { swatch.Fill = (WpfBrush)new BrushConverter().ConvertFromString(hex)!; }
                    catch { swatch.Fill = WpfBrushes.Transparent; }
                }
                else
                {
                    swatch.Fill = WpfBrushes.Transparent;
                }
            }

            var combo = new WpfComboBox
            {
                ItemsSource = Db.GetCompanies(_projectId),
                Text = value,
                IsEditable = true,
                IsTextSearchEnabled = true,
                Style = (Style)Resources["FieldComboBoxStyle"],
                FontSize = 15,
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
            };
            // ✅ Empêche "Copier sélection" de recopier le dernier TextBox visité avant ce
            // ComboBox (qui n'en est pas un) -- retombe correctement sur l'e-mail par défaut.
            combo.GotFocus += (s, e) => _lastFocusedFieldTextBox = null;
            combo.SelectionChanged += (s, e) => UpdateSwatch(combo.Text);
            combo.LostFocus += (s, e) => UpdateSwatch(combo.Text);
            UpdateSwatch(value);
            Grid.SetColumn(combo, 1);
            cell.Children.Add(combo);

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            fields.Children.Add(cell);

            return combo;
        }

        // ✅ Ordre (demande de Joe) : "Titre" en 1ère position (04.08.2026), E-mail juste après
        // Téléphone bureau, Site web en dernier, "Champ libre" tout en bas.
        // ✅ Renommés "Téléphone mobile"/"Téléphone bureau" (demande de Joe, 04.08.2026),
        // remplace "Téléphone 1"/"Téléphone 2".
        var titleBox = AddRow(0, "Titre", contact.Title);
        var nameBox = AddRow(1, "Nom", contact.Name);
        var intervenantCombo = AddIntervenantRow(2, contact.Intervenant);
        var companyBox = AddRow(3, "Entreprise", contact.Company);
        var addressBox = AddRow(4, "Adresse", contact.Address);
        var phoneBox = AddRow(5, "Téléphone mobile", contact.Phone);
        var phone2Box = AddRow(6, "Téléphone bureau", contact.Phone2);
        const int emailRow = 7;
        var emailBox = AddRow(emailRow, "E-mail", contact.Email);
        var websiteBox = AddRow(8, "Site web", contact.Website);
        var freeTextBox = AddRow(9, "Champ libre", contact.FreeText);

        // ✅ Fix (demande de Joe : "chevauche encore la croix") : le bouton "Copier" ne peut
        // pas être positionné en haut-droite de la fiche, cette zone est occupée par la croix
        // de fermeture (CloseDetailButton, overlay fixe du XAML) quel que soit son contenu
        // (icône ou texte) -- déplacé en bas, entre "Enregistrer" et "Fermer".
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Fiche contact",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x0F, 0x17, 0x2A)),
            Margin = new Thickness(0, 0, 0, 10),
        });
        DetailPanel.Children.Add(fields);

        var contactId = contact.Id;

        // ✅ Boutons sous le dernier champ, alignés à gauche (demande de Joe, 04.08.2026,
        // remplace l'alignement à droite précédent) ; la section "Associé à :" passe en
        // dessous, alignée à gauche aussi. "Fermer" fait la même chose que la croix flottante
        // du coin haut-droit.
        var bottomButtons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var saveButton = new WpfButton
        {
            Content = "Enregistrer",
            Style = (Style)FindResource("PrimaryButtonStyle"),
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        saveButton.Click += (s, e) =>
        {
            Db.UpdateContact(contactId, "Title", titleBox.Text);
            Db.UpdateContact(contactId, "Name", nameBox.Text);
            Db.UpdateContact(contactId, "Intervenant", intervenantCombo.Text);
            Db.UpdateContact(contactId, "Company", companyBox.Text);
            Db.UpdateContact(contactId, "Address", addressBox.Text);
            Db.UpdateContact(contactId, "Phone", phoneBox.Text);
            Db.UpdateContact(contactId, "Phone2", phone2Box.Text);
            Db.UpdateContact(contactId, "Email", emailBox.Text);
            Db.UpdateContact(contactId, "Website", websiteBox.Text);
            Db.UpdateContact(contactId, "FreeText", freeTextBox.Text);
            // ✅ La fiche se ferme après enregistrement (demande de Joe).
            CloseDetail();
        };
        bottomButtons.Children.Add(saveButton);

        // ✅ "Copier" entre "Enregistrer" et "Fermer" (demande de Joe, 2e essai après le
        // chevauchement avec la croix de fermeture) : copie le champ actuellement sélectionné
        // (_lastFocusedFieldTextBox, voir GotFocus dans AddRow), repli sur l'e-mail si rien
        // n'a encore été sélectionné dans cette fiche.
        var copyButton = new WpfButton { Content = "Copier sélection", Style = (Style)Resources["CopyButtonStyle"], Margin = new Thickness(0, 0, 8, 0) };
        copyButton.Click += (s, e) =>
        {
            var target = _lastFocusedFieldTextBox ?? emailBox;
            if (!string.IsNullOrWhiteSpace(target.Text))
            {
                WpfClipboard.SetText(target.Text);
                ShowCopiedFeedback(copyButton);
            }
        };
        bottomButtons.Children.Add(copyButton);

        var closeButton = new WpfButton { Content = "Fermer", Style = (Style)Resources["CloseButtonStyle"], Margin = new Thickness(0, 0, 8, 0) };
        closeButton.Click += (s, e) => CloseDetail();
        bottomButtons.Children.Add(closeButton);

        // ✅ "Envoyer un e-mail" (demande de Joe) : aussi disponible depuis la fiche contact,
        // envoie à l'adresse de CE contact uniquement.
        var sendButton = new WpfButton { Content = "Envoyer un e-mail >", Style = (Style)Resources["EmailButtonStyle"], Margin = new Thickness(0, 0, 8, 0) };
        sendButton.Click += (s, e) => SendContactEmail(contactId);
        bottomButtons.Children.Add(sendButton);

        // ✅ "Supprimer" avec confirmation (demande de Joe), directement depuis la fiche
        // (jusqu'ici seule la croix ✕ de la liste permettait de supprimer un contact).
        var deleteButton = new WpfButton { Content = "Supprimer", Style = (Style)Resources["DeleteTextButtonStyle"] };
        deleteButton.Click += (s, e) =>
        {
            var confirm = WpfMessageBox.Show(
                "Supprimer définitivement ce contact ?",
                "Carnet d'adresses",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            Db.DeleteContact(contactId);
            CloseDetail();
        };
        bottomButtons.Children.Add(deleteButton);

        DetailPanel.Children.Add(bottomButtons);

        // ✅ "Associé à :" (demande de Joe, remplace "Groupes") : appartenance de CE contact à
        // chaque groupe existant, cases à cocher togglées immédiatement (pas besoin
        // d'"Enregistrer" pour celles-ci). Placée sous les boutons, alignée à gauche
        // (demande de Joe).
        DetailPanel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 14, 0, 10),
        });
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Associé à :",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.Black,
            Margin = new Thickness(0, 0, 0, 6),
        });

        if (_groups.Count == 0)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "Aucun groupe pour l'instant.",
                FontSize = 12,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8)),
            });
        }

        // ✅ Zone de défilement indépendante (demande de Joe) : si la liste de groupes
        // s'allonge, seule cette section défile (hauteur plafonnée), les champs et boutons
        // au-dessus restent toujours visibles sans avoir à faire défiler toute la fiche.
        var groupsScroll = new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var groupsPanel = new StackPanel();
        groupsScroll.Content = groupsPanel;

        foreach (var group in _groups)
        {
            var groupId = group.Id;
            var isMember = Db.GetContactGroupMemberIds(groupId).Contains(contactId);
            var checkBox = new WpfCheckBox
            {
                Content = string.IsNullOrWhiteSpace(group.Name) ? "(Sans nom)" : group.Name,
                IsChecked = isMember,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
            };
            checkBox.Click += (s, e) => Db.SetContactGroupMember(groupId, contactId, checkBox.IsChecked == true);
            groupsPanel.Children.Add(checkBox);
        }

        DetailPanel.Children.Add(groupsScroll);
    }

    private void BuildGroupDetail(Db.ContactGroup group)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Fiche groupe",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x0F, 0x17, 0x2A)),
            Margin = new Thickness(0, 0, 0, 10),
        });

        // ✅ Largeur fixe (au lieu de "*", demande de Joe), même raison que BuildContactDetail.
        // ✅ Largeur +3cm (280 -> 393px, demande de Joe, 04.08.2026) : 3cm = 113px à 96 DPI.
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(393) });

        var lbl = new TextBlock { Text = "Nom", Style = (Style)Resources["FieldLabelStyle"] };
        Grid.SetColumn(lbl, 0);
        nameRow.Children.Add(lbl);

        var nameBox = new WpfTextBox { Text = group.Name, Style = (Style)Resources["FieldTextBoxStyle"] };
        Grid.SetColumn(nameBox, 1);
        nameRow.Children.Add(nameBox);

        DetailPanel.Children.Add(nameRow);

        var groupId = group.Id;

        // ✅ "Enregistrer" + "Fermer" + "Envoyer un e-mail" en bas à gauche (demande de Joe,
        // 04.08.2026, remplace l'alignement à droite précédent). "Fermer" fait la même chose
        // que la croix flottante du coin haut-droit. Sous le dernier champ ; la section
        // "Associé à :" passe en dessous, alignée à gauche aussi.
        var bottomButtons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var saveButton = new WpfButton
        {
            Content = "Enregistrer",
            Style = (Style)FindResource("PrimaryButtonStyle"),
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        saveButton.Click += (s, e) =>
        {
            Db.RenameContactGroup(groupId, nameBox.Text);
            RebuildGroupsList();
        };
        bottomButtons.Children.Add(saveButton);

        var closeButton = new WpfButton { Content = "Fermer", Style = (Style)Resources["CloseButtonStyle"], Margin = new Thickness(0, 0, 8, 0) };
        closeButton.Click += (s, e) => CloseDetail();
        bottomButtons.Children.Add(closeButton);

        var sendButton = new WpfButton
        {
            Content = "Envoyer un e-mail >",
            Style = (Style)Resources["EmailButtonStyle"],
            Margin = new Thickness(0, 0, 8, 0),
        };
        sendButton.Click += (s, e) => SendGroupEmail(groupId);
        bottomButtons.Children.Add(sendButton);

        // ✅ "Copier les adresses" (demande de Joe) : filet de sécurité fiable quand mailto:
        // ne fonctionne pas (dépend de la configuration du navigateur/client mail par défaut,
        // hors contrôle de l'app -- voir Vivaldi + panneau Mail intégré, qui n'a jamais
        // transmis les adresses). Colle les adresses (séparées par virgule) dans le
        // presse-papiers, à coller soi-même dans le champ "À" du nouveau message.
        var copyAddressesButton = new WpfButton { Content = "Copier les adresses mail", Style = (Style)Resources["CopyButtonStyle"], Margin = new Thickness(0, 0, 8, 0) };
        copyAddressesButton.Click += (s, e) =>
        {
            var emails = _contacts
                .Where(c => Db.GetContactGroupMemberIds(groupId).Contains(c.Id) && !string.IsNullOrWhiteSpace(c.Email))
                .Select(c => c.Email.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (emails.Count == 0)
            {
                WpfMessageBox.Show("Aucune adresse e-mail dans ce groupe.", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            WpfClipboard.SetText(string.Join(", ", emails));
            ShowCopiedFeedback(copyAddressesButton);
        };
        bottomButtons.Children.Add(copyAddressesButton);

        // ✅ "Supprimer" avec confirmation (demande de Joe), directement depuis la fiche
        // (jusqu'ici seule la croix ✕ de la liste permettait de supprimer un groupe).
        var deleteButton = new WpfButton { Content = "Supprimer", Style = (Style)Resources["DeleteTextButtonStyle"] };
        deleteButton.Click += (s, e) =>
        {
            var confirm = WpfMessageBox.Show(
                "Supprimer définitivement ce groupe ?",
                "Carnet d'adresses",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            Db.DeleteContactGroup(groupId);
            CloseDetail();
        };
        bottomButtons.Children.Add(deleteButton);

        DetailPanel.Children.Add(bottomButtons);

        // ✅ "Associé à :" (demande de Joe, remplace "Contacts", symétrique avec la fiche
        // contact) : appartenance de chaque contact à CE groupe, cases à cocher togglées
        // immédiatement. Placée sous les boutons, alignée à gauche (demande de Joe).
        DetailPanel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 14, 0, 10),
        });
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Associé à :",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.Black,
            Margin = new Thickness(0, 0, 0, 6),
        });

        if (_contacts.Count == 0)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "Aucun contact pour l'instant.",
                FontSize = 12,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8)),
            });
        }

        // ✅ Zone de défilement indépendante (demande de Joe), même principe que la section
        // "Associé à :" de BuildContactDetail.
        var contactsScroll = new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var contactsPanel = new StackPanel();
        // ✅ Fix (04.08.2026, demande de Joe : "certains mots sont coupés") : colonnes en
        // largeur fixe -> Auto + SharedSizeGroup, chaque colonne prend exactement la largeur du
        // plus long texte de sa colonne (sur toutes les lignes), sans jamais tronquer, tout en
        // restant alignée d'une ligne à l'autre.
        Grid.SetIsSharedSizeScope(contactsPanel, true);
        contactsScroll.Content = contactsPanel;

        // ✅ Pastille de couleur (demande de Joe, 04.08.2026), même source que le champ
        // "Intervenant" de la fiche contact -- permet de voir en un coup d'œil à quelle
        // entreprise chaque contact appartient en composant un groupe.
        var groupColorMap = Db.GetCompanyColorMap(_projectId);

        var memberIds = Db.GetContactGroupMemberIds(groupId);
        foreach (var contact in _contacts)
        {
            var contactId = contact.Id;

            var swatch = new WpfEllipse
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(0xD1, 0xD5, 0xDB)),
                StrokeThickness = 1,
                Fill = WpfBrushes.Transparent,
            };
            var intervenant = (contact.Intervenant ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(intervenant) && groupColorMap.TryGetValue(intervenant, out var hex))
            {
                try { swatch.Fill = (WpfBrush)new BrushConverter().ConvertFromString(hex)!; }
                catch { /* couleur invalide, laisse le swatch transparent */ }
            }

            // ✅ 7 informations par contact (demande de Joe, 04.08.2026, 4e passe : respecte
            // l'ordre chronologique des champs de la fiche Contact) : Titre, Nom, couleur
            // (pastille)+Intervenant, Entreprise, E-mail, Champ libre -- Grid (pas StackPanel)
            // pour que les colonnes s'alignent d'une ligne à l'autre. Colonnes Auto +
            // SharedSizeGroup (voir IsSharedSizeScope ci-dessus) : chacune prend exactement la
            // largeur nécessaire, pas de troncature.
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactTitle" });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactName" });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactIntervenant" });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactCompany" });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactEmail" });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ContactFreeText" });

            var titleText = new TextBlock
            {
                Text = contact.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(titleText, 0);
            content.Children.Add(titleText);

            var nameText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(contact.Name) ? "(Sans nom)" : contact.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(nameText, 1);
            content.Children.Add(nameText);

            Grid.SetColumn(swatch, 2);
            content.Children.Add(swatch);

            var intervenantText = new TextBlock
            {
                Text = contact.Intervenant,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(intervenantText, 3);
            content.Children.Add(intervenantText);

            var companyText = new TextBlock
            {
                Text = contact.Company,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(companyText, 4);
            content.Children.Add(companyText);

            var emailText = new TextBlock
            {
                Text = contact.Email,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x64, 0x74, 0x8B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(emailText, 5);
            content.Children.Add(emailText);

            var freeTextText = new TextBlock
            {
                Text = contact.FreeText,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x64, 0x74, 0x8B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(freeTextText, 6);
            content.Children.Add(freeTextText);

            // ✅ ToolTip récapitulatif (demande implicite, colonnes serrées) : les valeurs
            // tronquées par TextTrimming restent consultables au survol.
            var tooltipParts = new[] { contact.Title, contact.Name, contact.Intervenant, contact.Company, contact.Email, contact.FreeText }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            var checkBox = new WpfCheckBox
            {
                Content = content,
                IsChecked = memberIds.Contains(contactId),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = string.Join(" • ", tooltipParts),
            };
            checkBox.Click += (s, e) => Db.SetContactGroupMember(groupId, contactId, checkBox.IsChecked == true);
            contactsPanel.Children.Add(checkBox);
        }

        DetailPanel.Children.Add(contactsScroll);
    }

    // ✅ Envoi groupé (demande de Joe) : mailto: vers le client mail par défaut (Outlook,
    // etc.), pas d'envoi SMTP intégré depuis l'app -- voir commentaire de la classe.
    private void SendGroupEmail(long groupId)
    {
        var memberIds = Db.GetContactGroupMemberIds(groupId);

        var emails = _contacts
            .Where(c => memberIds.Contains(c.Id) && !string.IsNullOrWhiteSpace(c.Email))
            .Select(c => c.Email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0)
        {
            WpfMessageBox.Show("Aucune adresse e-mail dans ce groupe.", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenMailClient(emails);
    }

    // ✅ Envoi individuel (demande de Joe) : même mécanisme que SendGroupEmail, mais pour
    // l'adresse d'un seul contact (depuis sa fiche).
    private void SendContactEmail(long contactId)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId);
        var email = contact?.Email.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            WpfMessageBox.Show("Aucune adresse e-mail pour ce contact.", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenMailClient(new[] { email });
    }

    // ✅ Signalé par Joe : le navigateur ouvre bien une fenêtre de rédaction (donc le relais
    // mailto: -> webmail fonctionne), mais le champ "À" reste vide. 2e essai : la 1ère
    // correction encodait les adresses (Uri.EscapeDataString transforme "@" en "%40"), ce qui
    // peut casser un analyseur mailto naïf côté webmail qui cherche littéralement "@" dans la
    // valeur de "to=" -- adresses non encodées cette fois, seulement jointes par des virgules.
    private static void OpenMailClient(IEnumerable<string> emails)
    {
        try
        {
            Process.Start(new ProcessStartInfo("mailto:?to=" + string.Join(",", emails)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Impossible d'ouvrir le client mail.\n\n{ex.Message}", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ✅ Confirmation visuelle de copie (demande de Joe : "aucun signe qui me montre que le
    // copier se fait") : le bouton affiche "Copié !" brièvement puis revient à son texte
    // d'origine, même principe que les boutons "copier" habituels (GitHub, etc.).
    private static void ShowCopiedFeedback(WpfButton button)
    {
        var originalContent = button.Content;
        button.Content = "Copié !";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (s, e) =>
        {
            button.Content = originalContent;
            timer.Stop();
        };
        timer.Start();
    }

    // =========================
    // ✅ Actions groupées sur la sélection de contacts (demande de Joe) : associer à un
    // groupe, envoyer un e-mail, ou supprimer les contacts cochés dans la liste.
    // =========================
    private void UpdateBulkActionsPanel()
    {
        BulkActionsPanel.Visibility = _checkedContactIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var previouslySelected = BulkGroupComboBox.SelectedItem as Db.ContactGroup;
        BulkGroupComboBox.ItemsSource = _groups;
        if (previouslySelected != null && _groups.Any(g => g.Id == previouslySelected.Id))
            BulkGroupComboBox.SelectedItem = _groups.First(g => g.Id == previouslySelected.Id);
        else if (_groups.Count > 0)
            BulkGroupComboBox.SelectedIndex = 0;
    }

    private void BulkAssignButton_Click(object sender, RoutedEventArgs e)
    {
        if (BulkGroupComboBox.SelectedItem is not Db.ContactGroup group)
        {
            WpfMessageBox.Show("Créez d'abord un groupe.", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var id in _checkedContactIds)
            Db.SetContactGroupMember(group.Id, id, true);

        _checkedContactIds.Clear();
        RebuildContacts();
    }

    private void BulkEmailButton_Click(object sender, RoutedEventArgs e)
    {
        var emails = _contacts
            .Where(c => _checkedContactIds.Contains(c.Id) && !string.IsNullOrWhiteSpace(c.Email))
            .Select(c => c.Email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0)
        {
            WpfMessageBox.Show("Aucune adresse e-mail dans la sélection.", "Carnet d'adresses", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenMailClient(emails);
    }

    // ✅ Confirmation demandée (demande destructive, comme la Corbeille/Archives des Bons) :
    // pas de retour en arrière possible ensuite pour ces contacts.
    private void BulkDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var count = _checkedContactIds.Count;
        if (count == 0) return;

        var confirm = WpfMessageBox.Show(
            $"Supprimer définitivement {count} contact(s) sélectionné(s) ?",
            "Carnet d'adresses",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        foreach (var id in _checkedContactIds.ToList())
        {
            Db.DeleteContact(id);
            if (_selectedContactId == id) _selectedContactId = null;
        }

        _checkedContactIds.Clear();
        RebuildContacts();
    }
}
