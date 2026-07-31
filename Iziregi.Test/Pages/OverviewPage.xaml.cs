// File: Pages/OverviewPage.xaml.cs
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using System.Windows;
using Iziregi.Test.Data;
using Iziregi.Test.Models;

namespace Iziregi.Test.Pages;

// ✅ 31.07.2026 (demande de Joe) : nouveau "vrai" Tableau de bord (résumé général), voir
// commentaire en tête de OverviewPage.xaml. L'ancienne page "Tableau de bord" (liste des
// bons) reste inchangée en interne (classe DashboardPage, ShowDashboard()), seul son
// libellé de menu devient "Bons d'intervention".
public partial class OverviewPage : System.Windows.Controls.UserControl, IReloadablePage
{
    private readonly MainWindow _host;
    private readonly DispatcherTimer _clockTimer;

    public OverviewPage(MainWindow host)
    {
        InitializeComponent();
        _host = host;

        // ✅ 31.07.2026 (demande de Joe) : plus de secondes affichées -- une mise à jour par
        // minute suffit (au lieu d'une par seconde).
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
    }

    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTextBlock.Text = now.ToString("HH:mm", FrenchCulture);

        var dateText = now.ToString("dddd d MMMM yyyy", FrenchCulture);
        DateTextBlock.Text = char.ToUpper(dateText[0], FrenchCulture) + dateText.Substring(1);
    }

    public void Reload()
    {
        var project = Db.GetCurrentProject();

        if (project == null)
        {
            ProjectNameTextBlock.Text = "Aucun dossier sélectionné";

            WorkOrdersActiveRun.Text = "0";
            StageCreatedOnlyRun.Text = "0";
            StageSentToCompanyRun.Text = "0";
            StageQuoteReceivedRun.Text = "0";
            StageSentToSignerRun.Text = "0";
            StageValidatedRun.Text = "0";
            StageDistributedRun.Text = "0";
            StagePerformedRun.Text = "0";
            StageRefusedRun.Text = "0";
            StageCancelledRun.Text = "0";
            WorkOrdersArchivedRun.Text = "0";
            WorkOrdersTrashedRun.Text = "0";
            WorkOrdersTotalRun.Text = "0";
            ExpiredCompanyLinkRun.Text = "0";
            ExpiredSignerLinkRun.Text = "0";

            TasksActiveRun.Text = "0";
            TasksUrgency1Run.Text = "0";
            TasksUrgency2Run.Text = "0";
            TasksUrgency3Run.Text = "0";
            TasksDoneRun.Text = "0";
            return;
        }

        ProjectNameTextBlock.Text = project.Name;

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

        WorkOrdersActiveRun.Text = workOrders.Count.ToString(CultureInfo.InvariantCulture);
        StageCreatedOnlyRun.Text = createdOnly.ToString(CultureInfo.InvariantCulture);
        StageSentToCompanyRun.Text = sentToCompany.ToString(CultureInfo.InvariantCulture);
        StageQuoteReceivedRun.Text = quoteReceived.ToString(CultureInfo.InvariantCulture);
        StageSentToSignerRun.Text = sentToSigner.ToString(CultureInfo.InvariantCulture);
        StageValidatedRun.Text = validated.ToString(CultureInfo.InvariantCulture);
        StageDistributedRun.Text = distributed.ToString(CultureInfo.InvariantCulture);
        StagePerformedRun.Text = performed.ToString(CultureInfo.InvariantCulture);
        StageRefusedRun.Text = refused.ToString(CultureInfo.InvariantCulture);
        StageCancelledRun.Text = cancelled.ToString(CultureInfo.InvariantCulture);

        var archivedCount = Db.GetArchivedWorkOrdersCount(project.Id);
        var trashedCount = Db.GetTrashedWorkOrders(project.Id).Count;
        WorkOrdersArchivedRun.Text = archivedCount.ToString(CultureInfo.InvariantCulture);
        WorkOrdersTrashedRun.Text = trashedCount.ToString(CultureInfo.InvariantCulture);
        WorkOrdersTotalRun.Text = (workOrders.Count + archivedCount + trashedCount).ToString(CultureInfo.InvariantCulture);

        ExpiredCompanyLinkRun.Text = expiredCompanyLink.ToString(CultureInfo.InvariantCulture);
        ExpiredSignerLinkRun.Text = expiredSignerLink.ToString(CultureInfo.InvariantCulture);

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
        var activeTasks = allTasks.Where(t => !t.IsArchived && HasContent(t) && VisibleThisWeek(t)).ToList();
        var doneTasks = activeTasks.Count(t => t.Done);
        var urgency1 = activeTasks.Count(t => !t.Done && t.Urgent == "1");
        var urgency2 = activeTasks.Count(t => !t.Done && t.Urgent == "2");
        var urgency3 = activeTasks.Count(t => !t.Done && t.Urgent == "3");

        TasksActiveRun.Text = activeTasks.Count.ToString(CultureInfo.InvariantCulture);
        TasksUrgency1Run.Text = urgency1.ToString(CultureInfo.InvariantCulture);
        TasksUrgency2Run.Text = urgency2.ToString(CultureInfo.InvariantCulture);
        TasksUrgency3Run.Text = urgency3.ToString(CultureInfo.InvariantCulture);
        TasksDoneRun.Text = doneTasks.ToString(CultureInfo.InvariantCulture);
    }

    // ✅ 31.07.2026 (demande de Joe) : "lien hyperactif" -- Archivés/Corbeille ouvrent leur
    // page dédiée, le reste de la carte ouvre "Bons d'intervention". e.Handled=true sur les
    // 2 pilules pour empêcher le clic de "remonter" (bubbling) jusqu'au gestionnaire de la
    // carte englobante, qui ouvrirait sinon aussi "Bons d'intervention" juste après.
    private void BonsStatusTable_Click(object sender, MouseButtonEventArgs e) => _host.ShowDashboard();

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
}
