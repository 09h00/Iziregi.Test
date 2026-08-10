// File: Pages/OverviewPage.xaml.cs
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using System.Windows;
using System.Windows.Media;
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test;

namespace Iziregi.Test.Pages;

// ✅ 31.07.2026 (demande de Joe) : nouveau "vrai" Tableau de bord (résumé général), voir
// commentaire en tête de OverviewPage.xaml. L'ancienne page "Tableau de bord" (liste des
// bons) reste inchangée en interne (classe DashboardPage, ShowDashboard()), seul son
// libellé de menu devient "Bons d'intervention".
public partial class OverviewPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;
    private static readonly CultureInfo FrenchCulture = new CultureInfo("fr-FR");
    private readonly DispatcherTimer _clockTimer;

    // ✅ Bloc-notes (demande de Joe) : projet auquel appartient le texte actuellement affiché
    // dans NoteTextBox, mémorisé pour que NoteTextBox_LostFocus sache où enregistrer même si
    // l'utilisateur change de dossier sans avoir quitté le champ au clavier/à la souris avant.
    private long _noteProjectId;

    public OverviewPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clockTimer.Tick += (s, e) => UpdateClock();
        _clockTimer.Start();
    }

    // ✅ Widget Heure/Date/Semaine (re-ajouté au-dessus de "Tâches", demande de Joe).
    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTextBlock.Text = now.ToString("HH:mm");
        DateTextBlock.Text = now.ToString("dddd d MMMM yyyy", FrenchCulture);
        WeekTextBlock.Text = $"Semaine {System.Globalization.ISOWeek.GetWeekOfYear(now)}";
    }

    // ✅ 23e passe (demande de Joe) : "0" en gris clair pour tous les totaux (tous widgets),
    // et "Créé"/"Devis reçu"/"Validé" (widget Bons) en rouge quand ils ne sont pas à 0 --
    // signale que ces bons attendent une action (relance devis, envoi validation, distribution).
    private static readonly System.Windows.Media.Brush ZeroCountBrush = FreezeBrush(0xCB, 0xD5, 0xE1);
    private static readonly System.Windows.Media.Brush NormalCountBrush = FreezeBrush(0x0F, 0x17, 0x2A);
    private static readonly System.Windows.Media.Brush AlertCountBrush = FreezeBrush(0xDC, 0x26, 0x26);

    private static System.Windows.Media.Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void SetCount(TextBlock tb, int value, bool alertIfNonZero = false)
    {
        tb.Text = value.ToString(CultureInfo.InvariantCulture);
        var isAlert = alertIfNonZero && value != 0;
        tb.Foreground = value == 0 ? ZeroCountBrush : (alertIfNonZero ? AlertCountBrush : NormalCountBrush);
        // ✅ Fix (demande de Joe : "quand les totaux sont écrits en rouge, ils doivent être une
        // police plus grande") : agrandi uniquement quand réellement en alerte, sinon revient à
        // la taille normale (13, StageRowCountStyle) -- ne peut pas se fier au seul Style vu que
        // FontSize local a déjà pu être posé lors d'un appel précédent.
        tb.FontSize = isAlert ? 16 : 13;
    }

    private static readonly System.Windows.Media.Color DefaultProjectColor = System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB);

    // ✅ Même logique que ProjectsWindow.NormalizeHex/ColorToHex : "#RRGGBB", repli sur le bleu
    // par défaut si vide ou invalide (dossier sans couleur choisie).
    private static System.Windows.Media.Color ParseProjectColor(string? hex)
    {
        hex = (hex ?? "").Trim();
        if (string.IsNullOrWhiteSpace(hex)) return DefaultProjectColor;
        if (!hex.StartsWith("#", StringComparison.Ordinal)) hex = "#" + hex;

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c) return c;
        }
        catch { }

        return DefaultProjectColor;
    }


    public void Reload()
    {
        var project = Db.GetCurrentProject();

        if (project == null)
        {
            ProjectNameTextBlock.Text = "Aucun dossier sélectionné";
            ProjectAddressTextBlock.Text = "—";
            ProjectCityTextBlock.Text = "—";
            ProjectManagerNameTextBlock.Text = "—";
            ProjectManagerContactTextBlock.Text = "—";
            ProjectCardBorder.BorderBrush = new SolidColorBrush(DefaultProjectColor);
            ProjectCardShadow.Color = DefaultProjectColor;

            SetCount(WorkOrdersActiveRun, 0);
            SetCount(StageCreatedOnlyRun, 0, alertIfNonZero: true);
            SetCount(StageSentToCompanyRun, 0);
            SetCount(StageQuoteReceivedRun, 0, alertIfNonZero: true);
            SetCount(StageSentToSignerRun, 0);
            SetCount(StageValidatedRun, 0, alertIfNonZero: true);
            SetCount(StageDistributedRun, 0);
            SetCount(StagePerformedRun, 0);
            SetCount(StageRefusedRun, 0);
            SetCount(StageCancelledRun, 0);
            SetCount(WorkOrdersArchivedRun, 0);
            SetCount(WorkOrdersTrashedRun, 0);
            SetCount(WorkOrdersTotalRun, 0);
            SetCount(ExpiredCompanyLinkRun, 0);
            SetCount(ExpiredSignerLinkRun, 0);

            SetCount(TasksActiveRun, 0);
            SetCount(TasksUrgency1Run, 0);
            SetCount(TasksUrgency2Run, 0);
            SetCount(TasksUrgency3Run, 0);
            SetCount(TasksDoneRun, 0);
            SetCount(TasksArchivedRun, 0);
            SetCount(TasksTotalRun, 0);
            AccountingTotalTtcTextBlock.Text = "0.00";

            _noteProjectId = 0;
            AddNoteButton.IsEnabled = false;
            RebuildNotesList();
            return;
        }

        AddNoteButton.IsEnabled = true;
        _noteProjectId = project.Id;
        RebuildNotesList();

        ProjectNameTextBlock.Text = project.Name;

        // ✅ 24e passe (demande de Joe) : "Coordonnées du dossier", mêmes champs que
        // ProjectsWindow (Banque de dossiers).
        ProjectAddressTextBlock.Text = string.IsNullOrWhiteSpace(project.AddressLine) ? "—" : project.AddressLine;
        ProjectCityTextBlock.Text = string.IsNullOrWhiteSpace(project.ZipCity) ? "—" : project.ZipCity;
        ProjectManagerNameTextBlock.Text = string.IsNullOrWhiteSpace(project.ManagerName) ? "—" : project.ManagerName;
        ProjectManagerContactTextBlock.Text = string.IsNullOrWhiteSpace(project.ManagerContact) ? "—" : project.ManagerContact;

        // ✅ 26e passe (demande de Joe) : bordure + ombre de la carte "Dossier actif" teintées
        // de la couleur de la pastille du dossier (Project.ColorHex, même champ que
        // ProjectsWindow). Repli sur le bleu par défaut si vide/invalide.
        var projectColor = ParseProjectColor(project.ColorHex);
        ProjectCardBorder.BorderBrush = new SolidColorBrush(projectColor);
        ProjectCardShadow.Color = projectColor;

        // ✅ 31.07.2026 (demande de Joe) : "Statuts des bons" façon Excel -- chaque bon actif
        // (ni archivé ni à la corbeille) tombe dans EXACTEMENT une des catégories ci-dessous,
        // reflétant l'étape la plus avancée atteinte. Même ORDRE DE PRIORITÉ que GetStatusLabel
        // dans DashboardPage (décision de validation vérifiée EN PREMIER) -- corrige un bug de
        // comptage (10e passe, demande de Joe : "il n'y a pas de bon qui s'arrête à Créé") où un
        // bon déjà Validé mais dont l'ancien flag IsSentToCompany n'avait jamais été renseigné
        // (données historiques) était compté à la fois dans "Créé" ET dans "Validé".
        var workOrders = Db.GetWorkOrders(project.Id);

        static string ClassifyStage(WorkOrder w)
        {
            var decision = (w.ValidationDecision ?? "").Trim();
            if (string.Equals(decision, "Validé", StringComparison.OrdinalIgnoreCase))
            {
                if (w.PerformedAt != null) return "Performed";
                if (w.DistributedAt != null) return "Distributed";
                return "Validated";
            }
            if (string.Equals(decision, "Refusé", StringComparison.OrdinalIgnoreCase)) return "Refused";
            if (string.Equals(decision, "Annulé", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
            // ✅ 13e passe (demande de Joe) : "les liens expirés doivent faire partie du
            // comptage" -- un bon avec lien expiré était compté à la fois dans son étape
            // normale (Demande devis/validation envoyée) ET dans la ligne "Lien expiré"
            // (compteur indépendant), ce qui faisait que la somme des lignes affichées ne
            // correspondait plus à "Actifs". Un bon au lien expiré bascule maintenant DANS la
            // catégorie "Lien expiré" au lieu de rester dans sa catégorie d'origine.
            if (w.IsSentToSigner) return w.IsSignerLinkExpired ? "ExpiredSignerLink" : "SentToSigner";
            if (w.IsQuoteReceived) return "QuoteReceived";
            if (w.IsSentToCompany) return w.IsCompanyLinkExpired ? "ExpiredCompanyLink" : "SentToCompany";
            return "CreatedOnly";
        }

        var stageGroups = workOrders.GroupBy(ClassifyStage).ToDictionary(g => g.Key, g => g.Count());
        int StageCount(string key) => stageGroups.TryGetValue(key, out var n) ? n : 0;

        var createdOnly = StageCount("CreatedOnly");
        var sentToCompany = StageCount("SentToCompany");
        var quoteReceived = StageCount("QuoteReceived");
        var sentToSigner = StageCount("SentToSigner");
        var validated = StageCount("Validated");
        var distributed = StageCount("Distributed");
        var performed = StageCount("Performed");
        var refused = StageCount("Refused");
        var cancelled = StageCount("Cancelled");
        var expiredCompanyLink = StageCount("ExpiredCompanyLink");
        var expiredSignerLink = StageCount("ExpiredSignerLink");

        SetCount(WorkOrdersActiveRun, workOrders.Count);
        SetCount(StageCreatedOnlyRun, createdOnly, alertIfNonZero: true);
        SetCount(StageSentToCompanyRun, sentToCompany);
        SetCount(StageQuoteReceivedRun, quoteReceived, alertIfNonZero: true);
        SetCount(StageSentToSignerRun, sentToSigner);
        SetCount(StageValidatedRun, validated, alertIfNonZero: true);
        SetCount(StageDistributedRun, distributed);
        SetCount(StagePerformedRun, performed);
        SetCount(StageRefusedRun, refused);
        SetCount(StageCancelledRun, cancelled);

        var archivedCount = Db.GetArchivedWorkOrdersCount(project.Id);
        var trashedCount = Db.GetTrashedWorkOrders(project.Id).Count;
        SetCount(WorkOrdersArchivedRun, archivedCount);
        SetCount(WorkOrdersTrashedRun, trashedCount);
        SetCount(WorkOrdersTotalRun, workOrders.Count + archivedCount + trashedCount);

        SetCount(ExpiredCompanyLinkRun, expiredCompanyLink);
        SetCount(ExpiredSignerLinkRun, expiredSignerLink);

        // ✅ 16e passe (demande de Joe) : "Tâches actives" (comme "Actifs" côté Bons) + 1 ligne
        // par niveau d'urgence (1/2/3, liste de référence "Urg." du projet) + "Effectué", en
        // pastilles arrondies (19e passe, demande de Joe : essai option "A").
        // ✅ 20e passe (demande de Joe) : "il ne faut comptabiliser que les lignes avec au
        // moins 1 inscription" -- une ligne de tâche vide (créée automatiquement en bas de la
        // grille Planning pour la saisie rapide) n'a que son numéro (Ref), tous les autres
        // champs sont vides ; elle ne doit pas être comptée comme une tâche active.
        static bool HasContent(TaskRecord t) =>
            !string.IsNullOrWhiteSpace(t.Company) ||
            !string.IsNullOrWhiteSpace(t.Building) ||
            !string.IsNullOrWhiteSpace(t.Floor) ||
            !string.IsNullOrWhiteSpace(t.Todo) ||
            !string.IsNullOrWhiteSpace(t.Category) ||
            !string.IsNullOrWhiteSpace(t.Reserve) ||
            !string.IsNullOrWhiteSpace(t.Urgent) ||
            t.Done;

        // ✅ 21e passe (demande de Joe) : "les tâches se répercutent encore dans le passé" --
        // le widget sommait TOUT l'historique (toutes semaines confondues) au lieu de refléter
        // la semaine en cours, comme le fait la grille Planning (PlanningPage.
        // TaskRowVisibleInCurrentWeek) : une tâche Effectué la semaine dernière n'y est déjà
        // plus visible aujourd'hui, mais restait comptée ici indéfiniment. Même règle reprise
        // ici (semaine calée sur aujourd'hui, jour de départ Lundi par défaut).
        static DateTime SnapToStartOfWeek(DateTime date, DayOfWeek startDay)
        {
            var d = date.Date;
            while (d.DayOfWeek != startDay) d = d.AddDays(-1);
            return d;
        }

        var currentWeekStart = SnapToStartOfWeek(DateTime.Today, DayOfWeek.Monday);

        bool VisibleThisWeek(TaskRecord t)
        {
            if (t.CreatedWeekStart.HasValue && currentWeekStart < SnapToStartOfWeek(t.CreatedWeekStart.Value, DayOfWeek.Monday))
                return false;

            if (t.Done && t.DoneAt.HasValue)
            {
                var doneWeekStart = SnapToStartOfWeek(t.DoneAt.Value, DayOfWeek.Monday);
                if (t.CreatedWeekStart.HasValue)
                {
                    var createdWeekStart = SnapToStartOfWeek(t.CreatedWeekStart.Value, DayOfWeek.Monday);
                    if (doneWeekStart < createdWeekStart) doneWeekStart = createdWeekStart;
                }
                if (currentWeekStart > doneWeekStart) return false;
            }

            return true;
        }

        var allTasks = ProjectTasksStore.Load(project.Id);
        // ✅ Fix (demande de Joe) : une tâche mise à la Corbeille (IsTrashed, voir
        // TrashedTasksPage) ne doit plus être comptée comme active -- exclue ici comme
        // IsArchived l'est déjà.
        var activeTasks = allTasks.Where(t => !t.IsArchived && !t.IsTrashed && HasContent(t) && VisibleThisWeek(t)).ToList();
        var doneTasks = activeTasks.Count(t => t.Done);
        var inProgressTasks = activeTasks.Count - doneTasks;
        var urgency1 = activeTasks.Count(t => !t.Done && t.Urgent == "1");
        var urgency2 = activeTasks.Count(t => !t.Done && t.Urgent == "2");
        var urgency3 = activeTasks.Count(t => !t.Done && t.Urgent == "3");

        // ✅ Total = Actives + Effectuées + Archivées + Corbeille (demande de Joe, comme côté
        // Bons) : les niveaux d'urgence ne s'y ajoutent pas (ce sont déjà un sous-détail
        // d'Actives, pas un total à part).
        var archivedTasksCount = allTasks.Count(t => t.IsArchived && HasContent(t));
        var trashedTasksCount = allTasks.Count(t => t.IsTrashed && HasContent(t));

        SetCount(TasksActiveRun, inProgressTasks);
        SetCount(TasksUrgency1Run, urgency1);

        // ✅ "Niveau d'urgence 1" en rouge + police 1pt plus grand quand non nul (demande de
        // Joe) : si 0, on ne touche à rien (garde l'apparence posée par SetCount ci-dessus).
        // Volontairement à part de "alertIfNonZero" (SetCount) : cet écart-là saute à 16
        // (utilisé ailleurs sur les bons), ici Joe veut seulement +1pt (13 -> 14).
        if (urgency1 != 0)
        {
            TasksUrgency1Run.Foreground = AlertCountBrush;
            TasksUrgency1Run.FontSize = 14;
        }

        SetCount(TasksUrgency2Run, urgency2);
        SetCount(TasksUrgency3Run, urgency3);
        SetCount(TasksDoneRun, doneTasks);
        SetCount(TasksArchivedRun, archivedTasksCount);
        SetCount(TasksTrashedRun, trashedTasksCount);
        SetCount(TasksTotalRun, activeTasks.Count + archivedTasksCount + trashedTasksCount);

        // ✅ 22e passe (demande de Joe) : niveaux d'urgence visibles uniquement si la colonne
        // "Urg." est actuellement affichée dans la grille Planning (Db.GetTasksVisibleColumns,
        // même clé "Urgency" que PlanningPage.ApplyTaskColumnVisibility).
        var urgencyColumnVisible = Db.GetTasksVisibleColumns().Split(',', StringSplitOptions.RemoveEmptyEntries).Contains("Urgency");
        var urgencyVisibility = urgencyColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        TasksUrgency1Chip.Visibility = urgencyVisibility;
        TasksUrgency1Run.Visibility = urgencyVisibility;
        TasksUrgency1Divider.Visibility = urgencyVisibility;
        TasksUrgency2Chip.Visibility = urgencyVisibility;
        TasksUrgency2Run.Visibility = urgencyVisibility;
        TasksUrgency2Divider.Visibility = urgencyVisibility;
        TasksUrgency3Chip.Visibility = urgencyVisibility;
        TasksUrgency3Run.Visibility = urgencyVisibility;
        TasksUrgency3Divider.Visibility = urgencyVisibility;

        // ✅ 28e passe (demande de Joe) : widget "Comptabilité", Total TTC du 1er tableau de la
        // page Comptabilité ("Totaux par entreprise"), même critère d'éligibilité (bons
        // Validé) et même formule de prix, réutilisés depuis AccountingPage.
        var accountingTotalTtc = Db.GetWorkOrdersForAccounting(project.Id)
            .Where(AccountingPage.IsAccountingEligible)
            .Sum(AccountingPage.ComputeTtc);
        AccountingTotalTtcTextBlock.Text = accountingTotalTtc.ToString("0.00", CultureInfo.InvariantCulture);
    }

    // ✅ 31.07.2026 (demande de Joe) : "lien hyperactif" -- Archivés/Corbeille ouvrent leur
    // page dédiée, le reste de la carte ouvre "Bons d'intervention". e.Handled=true sur les
    // 2 pilules pour empêcher le clic de "remonter" (bubbling) jusqu'au gestionnaire de la
    // carte englobante, qui ouvrirait sinon aussi "Bons d'intervention" juste après.
    private void BonsStatusTable_Click(object sender, MouseButtonEventArgs e) => _host.ShowDashboard();
    private void AccountingCard_Click(object sender, MouseButtonEventArgs e) => _host.ShowAccounting();

    private void ArchivedRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _host.ShowArchives();
    }

    private void TrashRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _host.ShowTrash();
    }

    private void TasksArchivedRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _host.ShowArchivesTasks();
    }

    private void TasksTrashedRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _host.ShowTrashedTasks();
    }

    // ✅ Bloc-notes (demande de Joe) : plusieurs notes indépendantes par dossier, construites
    // dynamiquement (pas de binding/ObservableCollection ici, cohérent avec le reste de cette
    // page). Reconstruit la liste entière à chaque ajout/suppression -- volume attendu faible
    // (quelques notes), pas besoin d'une mise à jour incrémentale.
    private void RebuildNotesList()
    {
        NotesListPanel.Children.Clear();

        var notes = _noteProjectId > 0 ? Db.GetDashboardNotes(_noteProjectId) : new List<Db.DashboardNote>();
        NotesCountTextBlock.Text = $"({notes.Count})";

        foreach (var note in notes)
            NotesListPanel.Children.Add(BuildNoteTile(note));
    }

    // ✅ Style "mosaïque" (demande de Joe, 2e essai après la version en liste empilée) : petits
    // post-its de couleurs variées, façon vrais post-its papier. Couleur fixée à la création
    // (Db.DashboardNote.ColorHex, choisie parmi cette palette dans AddNoteButton_Click) plutôt
    // que recalculée par position à chaque affichage (demande de Joe : "les notes gardent leur
    // couleur du début à la fin" -- sinon, supprimer une note décalait l'index de toutes les
    // suivantes et changeait leur couleur).
    private static readonly string[] NoteTileColors = { "#FEF9C3", "#FCE7F3", "#DBEAFE", "#DCFCE7", "#FFEDD5" };

    private Border BuildNoteTile(Db.DashboardNote note)
    {
        // ✅ Repli sur la 1ère couleur de la palette pour les notes créées avant l'ajout de
        // cette colonne (ColorHex vide en base).
        var tileColorHex = string.IsNullOrWhiteSpace(note.ColorHex) ? NoteTileColors[0] : note.ColorHex;

        // ✅ Qualifié en System.Windows.Controls.* / System.Windows.Media.Color (piège connu de
        // ce fichier : WPF + WinForms référencés, TextBox/Button/Color sinon ambigus, CS0104).
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = note.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 14, 0, 0),
        };
        // ✅ Enregistrement automatique à la perte de focus (demande de Joe), même principe
        // que les autres réglages "légers" de cette page (Db.SetDefaultXxx) -- pas de bouton
        // "Enregistrer" séparé.
        textBox.LostFocus += (s, e) => Db.UpdateDashboardNoteText(note.Id, textBox.Text);

        // ✅ Double-clic -> NoteEditWindow (demande de Joe) : agrandit la note dans une petite
        // fenêtre. PreviewMouseLeftButtonDown + e.Handled=true (pas d'événement dédié
        // "MouseDoubleClick" sur TextBox) : évite aussi le comportement par défaut de
        // sélection du mot au double-clic, qu'on remplace ici par l'ouverture de la fenêtre.
        textBox.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount != 2)
                return;

            e.Handled = true;

            var editWindow = new NoteEditWindow(textBox.Text, tileColorHex) { Owner = System.Windows.Window.GetWindow(this) };
            if (editWindow.ShowDialog() == true)
            {
                textBox.Text = editWindow.ResultText;
                Db.UpdateDashboardNoteText(note.Id, editWindow.ResultText);
            }
        };

        var deleteButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Style = (Style)Resources["NoteDeleteButtonStyle"],
            Width = 18,
            Height = 18,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };
        deleteButton.Click += (s, e) =>
        {
            Db.DeleteDashboardNote(note.Id);
            RebuildNotesList();
        };

        // ✅ Superposition simple (Grid à une seule cellule) : la croix flotte dans le coin
        // haut-droit, le TextBox garde une marge haute (14px) pour ne pas passer dessous.
        var content = new Grid();
        content.Children.Add(textBox);
        content.Children.Add(deleteButton);

        return new Border
        {
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(tileColorHex)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Width = 140,
            Height = 110,
            Margin = new Thickness(0, 0, 8, 8),
            Child = content,
        };
    }

    private void AddNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_noteProjectId <= 0)
            return;

        // ✅ Couleur choisie une fois pour toutes ici, à la création (demande de Joe : "les
        // notes gardent leur couleur du début à la fin") -- cycle selon le nombre de notes
        // déjà existantes, comme le faisait l'ancien calcul par index, mais figé en base.
        var newNoteColorHex = NoteTileColors[Db.GetDashboardNotes(_noteProjectId).Count % NoteTileColors.Length];
        Db.InsertDashboardNote(_noteProjectId, "", newNoteColorHex);
        RebuildNotesList();

        // ✅ Focus direct sur la nouvelle note (dernier post-it ajouté), demande implicite de
        // fluidité : pas besoin de chercher le post-it vide à la main après un clic "+".
        if (NotesListPanel.Children.Count > 0 &&
            NotesListPanel.Children[NotesListPanel.Children.Count - 1] is Border lastTile &&
            lastTile.Child is Grid lastContent)
        {
            foreach (var child in lastContent.Children)
            {
                if (child is System.Windows.Controls.TextBox lastTextBox)
                {
                    lastTextBox.Focus();
                    break;
                }
            }
        }
    }

    // ✅ 27e passe (demande de Joe) : "tous les titres de widgets doivent avoir un lien avec
    // leur page associée" -- "Tâches" ouvre Planification, "Dossier actif" ouvre la Banque de
    // dossiers (même fenêtre que SettingsPage > Banque de dossiers).
    private void TasksCard_Click(object sender, MouseButtonEventArgs e) => _host.ShowPlanning();

    private void ProjectCard_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var win = new ProjectsWindow
            {
                Owner = System.Windows.Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
            };
            win.ShowDialog();
        }
        catch { }
    }
}
