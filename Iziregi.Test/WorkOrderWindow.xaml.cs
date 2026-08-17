// File: WorkOrderWindow.xaml.cs
using Iziregi.Test.Data;
using Iziregi.Test.Models;
using Iziregi.Test.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfDataFormats = System.Windows.DataFormats;
// ✅ Fix ambiguïtés DataObject + DataFormats (WinForms vs WPF)
using WpfDataObject = System.Windows.DataObject;
using WpfInkCanvas = System.Windows.Controls.InkCanvas;
// ✅ Fix ambiguïtés WinForms/WPF (TextBox, etc.)
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUIElement = System.Windows.UIElement;

namespace Iziregi.Test;

public partial class WorkOrderWindow : Window
{
    // =========================
    // ✅ Liens magiques (serveur)
    // =========================
    private static string ServerBaseUrl => IziregiConfigService.Current.ServerBaseUrl;
    private static string ServerApiKey  => IziregiConfigService.Current.ServerApiKey;

    private static readonly HttpClient Http = CreateHttpClient();

    // ✅ Ajout d'un User-Agent : certains serveurs/WAF bloquent les requêtes sans en-tête User-Agent,
    // ce que HttpClient n'envoie pas par défaut, contrairement à un navigateur ou PowerShell.
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("User-Agent", "IziregiClient/1.0");
        return client;
    }

    // ✅ Sécurité : envoie la clé API via l'en-tête HTTP "X-Api-Key" plutôt que dans
    // l'URL (voir MainWindow.xaml.cs pour l'explication complète).
    private static async Task<HttpResponseMessage> PostWithApiKeyAsync(HttpClient client, string url, HttpContent? content)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(ServerApiKey))
            req.Headers.Add("X-Api-Key", ServerApiKey);
        return await client.SendAsync(req);
    }

    private readonly ObservableCollection<WorkOrderLine> _lines = new();
    private readonly List<long> _deletedLineIds = new();

    private WorkOrder? _workOrder;
    private bool _isCreateMode;
    private bool _isLoading;

    // ✅ Bouton 2 positions Devis standard/Devis PDF (04.08.2026, demande de Joe) : None = zone
    // neutre (bon neuf, aucune position choisie -- rien de remplissable), voir
    // SetQuoteMode/ApplyQuoteModeUi/DetectQuoteModeFromData.
    private enum QuoteMode { None, Standard, Pdf }
    private QuoteMode _quoteMode = QuoteMode.None;

    // ✅ Lecture seule par défaut (23.07.2026, demande de Joe) : un bon déjà enregistré s'ouvre
    // verrouillé pour éviter les modifications accidentelles (frappe clavier, trait dans la
    // signature...). Le bouton "Modifier" déverrouille explicitement. Un nouveau bon (création)
    // reste éditable directement.
    private bool _formLocked;

    // ✅ Détection fiable des modifications réelles (23.07.2026, demande de Joe) : au lieu d'un
    // suivi événementiel par champ (abandonné, faux positifs sur événements différés WPF), on
    // compare un instantané de tous les champs pris au déverrouillage avec l'état au moment de
    // fermer — une seule comparaison de valeurs, pas d'écoute d'événements.
    private string? _unlockedSnapshot;

    private byte[]? _existingSignaturePng;
    private bool _signatureCleared;

    private WorkOrderEditMode _mode = WorkOrderEditMode.Architecte;
    private bool _recomputeQueued;

    private const int ReserveMaxLength = 20;
    private bool _reserveLimitHooked = false;

    private const int QuoteMaxLines = 12;
    private const int QuoteHardMaxItems = QuoteMaxLines;

    private bool _pdfAvailableForExternal = false;

    private static string InboxDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "INBOX");

    private static readonly JsonSerializerOptions ReplyJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private class IziregiReplyFile
    {
        public string FileType { get; set; } = "iziregi-reponse";
        public string Package { get; set; } = "";
        public string RepliedAt { get; set; } = "";
        public long WorkOrderId { get; set; }
        public WorkOrder? WorkOrder { get; set; }
        public List<WorkOrderLine> Lines { get; set; } = new();

        public string SignatureName { get; set; } = "";
        public string SignatureDate { get; set; } = "";
        public byte[]? SignaturePng { get; set; }
    }

    private const int DescriptionMaxCharsPerLine = 34;
    private const int DescriptionMaxLines = 10;

    private bool _descriptionGuard;
    private bool _descriptionLimitHooked;

    private void HookDescriptionLimit()
    {
        if (_descriptionLimitHooked) return;
        _descriptionLimitHooked = true;

        if (DescriptionTextBox == null) return;

        DescriptionTextBox.TextChanged -= DescriptionTextBox_TextChanged_EnforceRules;

        DescriptionTextBox.PreviewKeyDown -= DescriptionTextBox_PreviewKeyDown_BlockEnterAtMaxLines;
        DescriptionTextBox.PreviewKeyDown += DescriptionTextBox_PreviewKeyDown_BlockEnterAtMaxLines;

        DescriptionTextBox.LostKeyboardFocus -= DescriptionTextBox_LostKeyboardFocus_EnforceRules;
        DescriptionTextBox.LostKeyboardFocus += DescriptionTextBox_LostKeyboardFocus_EnforceRules;

        WpfDataObject.RemovePastingHandler(DescriptionTextBox, DescriptionTextBox_OnPaste_EnforceRules);
        WpfDataObject.AddPastingHandler(DescriptionTextBox, DescriptionTextBox_OnPaste_EnforceRules);
    }

    private void DescriptionTextBox_PreviewKeyDown_BlockEnterAtMaxLines(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not WpfTextBox tb) return;

        var isEnter = e.Key == System.Windows.Input.Key.Return || e.Key == System.Windows.Input.Key.Enter;
        if (!isEnter) return;

        if (tb.SelectionLength > 0) return;

        var lines = SplitLines(tb.Text ?? "");
        if (lines.Length >= DescriptionMaxLines)
            e.Handled = true;
    }

    private void DescriptionTextBox_LostKeyboardFocus_EnforceRules(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isLoading) return;
        if (_descriptionGuard) return;
        if (sender is not WpfTextBox tb) return;

        try
        {
            var before = NormalizeNewlines(tb.Text ?? "");
            var after = EnforceDescriptionRules(before);

            if (string.Equals(before, after, StringComparison.Ordinal))
                return;

            _descriptionGuard = true;
            try
            {
                tb.Text = after;
                tb.CaretIndex = Math.Min(after.Length, tb.CaretIndex);
            }
            finally
            {
                _descriptionGuard = false;
            }
        }
        catch { }
    }

    private void DescriptionTextBox_OnPaste_EnforceRules(object sender, DataObjectPastingEventArgs e)
    {
        if (_isLoading) return;
        if (_descriptionGuard) return;
        if (sender is not WpfTextBox tb) return;

        try
        {
            if (!e.DataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;

            var pasteText = e.DataObject.GetData(WpfDataFormats.UnicodeText) as string ?? "";
            pasteText = NormalizeNewlines(pasteText);

            var current = NormalizeNewlines(tb.Text ?? "");
            var selStart = tb.SelectionStart;
            var selLen = tb.SelectionLength;

            if (selStart < 0) selStart = 0;
            if (selStart > current.Length) selStart = current.Length;
            if (selLen < 0) selLen = 0;
            if (selStart + selLen > current.Length) selLen = current.Length - selStart;

            var composed = current.Substring(0, selStart) + pasteText + current.Substring(selStart + selLen);
            var enforced = EnforceDescriptionRules(composed);

            e.CancelCommand();
            _descriptionGuard = true;
            try
            {
                tb.Text = enforced;
                tb.CaretIndex = Math.Min(enforced.Length, selStart + pasteText.Length);
            }
            finally
            {
                _descriptionGuard = false;
            }
        }
        catch { }
    }

    private void DescriptionTextBox_TextChanged_EnforceRules(object sender, TextChangedEventArgs e) { }

    private static string EnforceDescriptionRules(string input)
    {
        input = NormalizeNewlines(input);
        var wrapped = EnforceWordWrap(input, DescriptionMaxCharsPerLine);

        var lines = SplitLines(wrapped);
        if (lines.Length <= DescriptionMaxLines)
            return wrapped;

        return string.Join("\n", lines.Take(DescriptionMaxLines));
    }

    private static string EnforceWordWrap(string input, int maxCharsPerLine)
    {
        input = NormalizeNewlines(input);
        if (maxCharsPerLine <= 0) return input;

        var rawLines = input.Split('\n');
        var outLines = new List<string>();

        foreach (var raw in rawLines)
        {
            var line = raw ?? "";

            if (line.Length == 0)
            {
                outLines.Add("");
                continue;
            }

            var remaining = line;

            while (remaining.Length > maxCharsPerLine)
            {
                int cut = -1;
                for (int i = maxCharsPerLine; i >= 0; i--)
                {
                    if (i < remaining.Length && remaining[i] == ' ')
                    {
                        cut = i;
                        break;
                    }
                }

                if (cut <= 0)
                {
                    outLines.Add(remaining.Substring(0, maxCharsPerLine));
                    remaining = remaining.Substring(maxCharsPerLine);
                    continue;
                }

                outLines.Add(remaining.Substring(0, cut));

                int nextStart = cut + 1;
                while (nextStart < remaining.Length && remaining[nextStart] == ' ')
                    nextStart++;

                remaining = nextStart <= remaining.Length ? remaining.Substring(nextStart) : "";
            }

            outLines.Add(remaining);
        }

        return string.Join("\n", outLines);
    }

    private const int QuoteNotesMaxCharsPerLine = 40;
    // ✅ 5 -> 7 (04.08.2026, demande de Joe).
    private const int QuoteNotesMaxLines = 7;

    private bool _quoteNotesGuard;
    private bool _quoteNotesLimitHooked;

    private void HookQuoteNotesLimit()
    {
        if (_quoteNotesLimitHooked) return;
        _quoteNotesLimitHooked = true;

        if (QuoteNotesTextBox == null) return;

        QuoteNotesTextBox.PreviewKeyDown -= QuoteNotesTextBox_PreviewKeyDown_BlockEnterAtMaxLines;
        QuoteNotesTextBox.PreviewKeyDown += QuoteNotesTextBox_PreviewKeyDown_BlockEnterAtMaxLines;

        QuoteNotesTextBox.LostKeyboardFocus -= QuoteNotesTextBox_LostKeyboardFocus_EnforceRules;
        QuoteNotesTextBox.LostKeyboardFocus += QuoteNotesTextBox_LostKeyboardFocus_EnforceRules;

        WpfDataObject.RemovePastingHandler(QuoteNotesTextBox, QuoteNotesTextBox_OnPaste_EnforceRules);
        WpfDataObject.AddPastingHandler(QuoteNotesTextBox, QuoteNotesTextBox_OnPaste_EnforceRules);
    }

    private void QuoteNotesTextBox_PreviewKeyDown_BlockEnterAtMaxLines(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not WpfTextBox tb) return;

        var isEnter = e.Key == System.Windows.Input.Key.Return || e.Key == System.Windows.Input.Key.Enter;
        if (!isEnter) return;

        if (tb.SelectionLength > 0) return;

        var lines = SplitLines(tb.Text ?? "");
        if (lines.Length >= QuoteNotesMaxLines)
            e.Handled = true;
    }

    private void QuoteNotesTextBox_LostKeyboardFocus_EnforceRules(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isLoading) return;
        if (_quoteNotesGuard) return;
        if (sender is not WpfTextBox tb) return;

        try
        {
            var before = NormalizeNewlines(tb.Text ?? "");
            var after = EnforceQuoteNotesRules(before);

            if (string.Equals(before, after, StringComparison.Ordinal))
                return;

            _quoteNotesGuard = true;
            try
            {
                tb.Text = after;
                tb.CaretIndex = Math.Min(after.Length, tb.CaretIndex);
            }
            finally
            {
                _quoteNotesGuard = false;
            }
        }
        catch { }
    }

    private void QuoteNotesTextBox_OnPaste_EnforceRules(object sender, DataObjectPastingEventArgs e)
    {
        if (_isLoading) return;
        if (_quoteNotesGuard) return;
        if (sender is not WpfTextBox tb) return;

        try
        {
            if (!e.DataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;

            var pasteText = e.DataObject.GetData(WpfDataFormats.UnicodeText) as string ?? "";
            pasteText = NormalizeNewlines(pasteText);

            var current = NormalizeNewlines(tb.Text ?? "");
            var selStart = tb.SelectionStart;
            var selLen = tb.SelectionLength;

            if (selStart < 0) selStart = 0;
            if (selStart > current.Length) selStart = current.Length;
            if (selLen < 0) selLen = 0;
            if (selStart + selLen > current.Length) selLen = current.Length - selStart;

            var composed = current.Substring(0, selStart) + pasteText + current.Substring(selStart + selLen);
            var enforced = EnforceQuoteNotesRules(composed);

            e.CancelCommand();
            _quoteNotesGuard = true;
            try
            {
                tb.Text = enforced;
                tb.CaretIndex = Math.Min(enforced.Length, selStart + pasteText.Length);
            }
            finally
            {
                _quoteNotesGuard = false;
            }
        }
        catch { }
    }

    // ✅ Simplifié (demande de Joe, 11.08.2026, "le mot entier doit passer à la ligne même
    // si on n'a pas atteint les 40 caractères") : réutilise EnforceWordWrap (déjà utilisé pour
    // Descriptif), qui coupe au dernier espace avant la limite au lieu de couper au milieu d'un
    // mot -- même principe que EnforceDescriptionRules ci-dessus. Un mot isolé plus long que
    // QuoteNotesMaxCharsPerLine reste coupé en dur (aucun espace disponible où couper).
    private static string EnforceQuoteNotesRules(string input)
    {
        input = NormalizeNewlines(input);
        var wrapped = EnforceWordWrap(input, QuoteNotesMaxCharsPerLine);

        var lines = SplitLines(wrapped);
        if (lines.Length <= QuoteNotesMaxLines)
            return wrapped;

        return string.Join("\n", lines.Take(QuoteNotesMaxLines));
    }

    private static string NormalizeNewlines(string s)
    {
        s ??= "";
        return s.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string[] SplitLines(string s)
    {
        s = NormalizeNewlines(s);
        if (s.Length == 0) return Array.Empty<string>();
        return s.Split('\n');
    }

    private string GetSelectedValidationDecision()
    {
        try
        {
            if (DecisionValidateRadio?.IsChecked == true) return "Validé";
            if (DecisionRefuseRadio?.IsChecked == true) return "Refusé";
            if (DecisionCancelRadio?.IsChecked == true) return "Annulé";
        }
        catch { }
        return "";
    }

    private void ApplyValidationDecisionToUi(string? decision)
    {
        decision = (decision ?? "").Trim();

        try
        {
            if (DecisionValidateRadio != null) DecisionValidateRadio.IsChecked = false;
            if (DecisionRefuseRadio != null) DecisionRefuseRadio.IsChecked = false;
            if (DecisionCancelRadio != null) DecisionCancelRadio.IsChecked = false;

            if (string.Equals(decision, "Validé", StringComparison.OrdinalIgnoreCase))
            {
                if (DecisionValidateRadio != null) DecisionValidateRadio.IsChecked = true;
                return;
            }

            if (string.Equals(decision, "Refusé", StringComparison.OrdinalIgnoreCase))
            {
                if (DecisionRefuseRadio != null) DecisionRefuseRadio.IsChecked = true;
                return;
            }

            if (string.Equals(decision, "Annulé", StringComparison.OrdinalIgnoreCase))
            {
                if (DecisionCancelRadio != null) DecisionCancelRadio.IsChecked = true;
                return;
            }
        }
        catch { }
    }

    // ✅ Champs obligatoires du Devis PDF (04.08.2026, demande de Joe : "enlever les obligations
    // entre les champs 'Ajouter pdf' et 'Montant TTC du pdf'") : les 3 conditions (pdf joint, N°
    // du devis, Montant TTC) sont désormais indépendantes -- chacune obligatoire par elle-même en
    // position "Devis PDF", plutôt que "Montant TTC"/"N° du devis" obligatoires SEULEMENT SI un
    // pdf est déjà joint. Bloque l'enregistrement (voir SaveWorkOrder) tant que les 3 ne sont pas
    // toutes remplies.
    private bool EnsureQuoteRequiredFieldsOrWarn()
    {
        if (_quoteMode != QuoteMode.Pdf) return true;

        var hasPdf = _workOrder?.ForfaitPdfFileBytes != null && _workOrder.ForfaitPdfFileBytes.Length > 0;
        if (!hasPdf)
        {
            System.Windows.MessageBox.Show(
                this,
                "En mode \"Devis pdf\", l'ajout de votre devis pdf est obligatoire",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var quoteNumber = (ForfaitQuoteNumberTextBox?.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(quoteNumber))
        {
            System.Windows.MessageBox.Show(
                this,
                "En mode \"Devis pdf\", le numéro de votre devis pdf est obligatoire",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var forfaitTtc = ParseDouble(ForfaitTtcTextBox?.Text, 0);
        if (Math.Abs(forfaitTtc) < 0.0000000001)
        {
            System.Windows.MessageBox.Show(
                this,
                "En mode \"Devis pdf\", le montant TTC de votre pdf est obligatoire",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private bool EnsureValidationIsNotPartialOrWarn()
    {
        var decision = (GetSelectedValidationDecision() ?? "").Trim();
        var name = (SignatureNameComboBox?.Text ?? "").Trim();
        var date = SignatureDatePicker?.SelectedDate;

        bool hasInk = false;
        try { hasInk = SignatureInkCanvas != null && SignatureInkCanvas.Strokes.Count > 0; } catch { hasInk = false; }

        bool hasSignature =
            hasInk
            || (_existingSignaturePng != null && _existingSignaturePng.Length > 0);

        bool allEmpty =
            string.IsNullOrWhiteSpace(decision)
            && string.IsNullOrWhiteSpace(name)
            && !date.HasValue
            && !hasSignature
            && !_signatureCleared;

        if (allEmpty)
            return true;

        bool isComplete =
            !string.IsNullOrWhiteSpace(decision)
            && !string.IsNullOrWhiteSpace(name)
            && date.HasValue
            && hasSignature;

        if (!isComplete)
        {
            System.Windows.MessageBox.Show(
                this,
                "Validation incomplète : tous les champs obligatoires ne sont pas remplis.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var png = CaptureSignaturePng();
        if (png == null || png.Length == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Validation incomplète : tous les champs obligatoires ne sont pas remplis.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        _existingSignaturePng = png;
        _signatureCleared = false;

        return true;
    }

    private void ResetValidationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DecisionValidateRadio != null) DecisionValidateRadio.IsChecked = false;
            if (DecisionRefuseRadio != null) DecisionRefuseRadio.IsChecked = false;
            if (DecisionCancelRadio != null) DecisionCancelRadio.IsChecked = false;

            if (SignatureNameComboBox != null) SignatureNameComboBox.Text = "";
            if (SignatureDatePicker != null) SignatureDatePicker.SelectedDate = null;

            try { SignatureInkCanvas?.Strokes.Clear(); } catch { }
            try { if (SignatureInkCanvas != null) SignatureInkCanvas.Background = MediaBrushes.White; } catch { }
            try { SignaturePreviewImage.Source = null; } catch { }

            _existingSignaturePng = null;

            // ✅ Correctif (22.07.2026, demande de Joe) : _signatureCleared=true signifie ici
            // "l'utilisateur vient d'effacer la signature en cours d'édition" (voir
            // ClearSignatureButton_Click), un état PARTIEL qui doit bloquer un renvoi tant que
            // le reste n'est pas re-rempli. Mais un Reset complet remet TOUT à vide (décision,
            // nom, date, signature) -> EnsureValidationIsNotPartialOrWarn doit reconnaître ça
            // comme un état vierge (allEmpty), pas comme une validation partielle. Sans ce
            // correctif, renvoyer le lien pour validation après un reset échouait toujours avec
            // "Validation incomplète", même bon vidé intégralement.
            _signatureCleared = false;

            if (_workOrder == null || _workOrder.Id <= 0)
            {
                ApplyMode();
                return;
            }

            _workOrder.SignatureName = "";
            _workOrder.SignatureDate = null;
            _workOrder.SignaturePng = null;
            _workOrder.ValidationDecision = "";

            Db.UpdateWorkOrderSignatureRaw(_workOrder);
            Db.UpdateWorkOrderValidationDecision(_workOrder.Id, "");

            var hasQuoteData = HasAnyQuoteData(_workOrder) || _lines.Any(l => !string.IsNullOrWhiteSpace(l?.Label));
            if (hasQuoteData)
                Db.SetStageQuoteReceived(_workOrder.Id);
            else
                Db.SetStageInCreation(_workOrder.Id);

            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            ApplyMode();
            UpdatePdfButtonVisibility();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de reset la validation.\n\n{ex.Message}",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ResetValidationBottomButton_Click(object sender, RoutedEventArgs e) => ResetValidationButton_Click(sender, e);

    // ✅ "Reset Devis" (17.08.2026, demande de Joe) : vide entièrement la carte Devis (nom, date,
    // lignes, main d'œuvre, déplacements, rabais, TVA, forfait, notes), même principe que
    // ResetValidationButton_Click ci-dessus (reset immédiat + persistance en base).
    private void ResetQuoteBottomButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (QuoteNameTextBox != null) QuoteNameTextBox.Text = "";
            if (QuoteDatePicker != null) QuoteDatePicker.SelectedDate = DateTime.Today;

            if (LaborHoursTextBox != null) LaborHoursTextBox.Text = "";
            if (LaborRateTextBox != null) LaborRateTextBox.Text = "";
            if (TravelQtyTextBox != null) TravelQtyTextBox.Text = "";
            if (TravelRateTextBox != null) TravelRateTextBox.Text = "";
            if (DiscountRateTextBox != null) DiscountRateTextBox.Text = "";
            if (DiscountRateTextBox2 != null) DiscountRateTextBox2.Text = "";
            if (DiscountNameTextBox != null) DiscountNameTextBox.Text = "";
            if (DiscountName2TextBox != null) DiscountName2TextBox.Text = "";
            if (TvaRateTextBox != null) TvaRateTextBox.Text = 8.1.ToString("0.00", CultureInfo.InvariantCulture);

            if (ForfaitTtcTextBox != null) ForfaitTtcTextBox.Text = "";
            if (ForfaitQuoteNumberTextBox != null) ForfaitQuoteNumberTextBox.Text = "";

            if (QuoteNotesTextBox != null) QuoteNotesTextBox.Text = "";

            foreach (var line in _lines)
                if (line.Id > 0)
                    _deletedLineIds.Add(line.Id);

            _lines.Clear();
            _lines.Add(new WorkOrderLine());

            if (AddLineButton != null)
                AddLineButton.Content = $"+ Ajouter ligne ({_lines.Count} / {QuoteMaxLines} max.)";

            _quoteMode = QuoteMode.Standard;
            ApplyQuoteModeUi();
            QueueRecomputeTotals();

            if (_workOrder == null || _workOrder.Id <= 0)
            {
                ApplyMode();
                return;
            }

            _workOrder.QuoteName = "";
            _workOrder.QuoteDate = DateTime.Today;
            _workOrder.LaborHours = 0;
            _workOrder.LaborRate = 0;
            _workOrder.TravelQty = 0;
            _workOrder.TravelRate = 0;
            _workOrder.DiscountRate = 0;
            _workOrder.DiscountRate2 = 0;
            _workOrder.DiscountName = "";
            _workOrder.DiscountName2 = "";
            _workOrder.TvaRate = 8.1;
            _workOrder.ForfaitTtc = 0;
            _workOrder.ForfaitQuoteNumber = "";
            _workOrder.ForfaitPdfFileBytes = null;
            _workOrder.ForfaitPdfFileName = "";
            _workOrder.QuoteNotes = "";

            Db.UpdateWorkOrderQuote(_workOrder);
            Db.ReplaceWorkOrderLines(_workOrder.Id, _lines.ToList());
            _deletedLineIds.Clear();

            var hasQuoteData = HasAnyQuoteData(_workOrder) || _lines.Any(l => !string.IsNullOrWhiteSpace(l?.Label));
            if (hasQuoteData)
                Db.SetStageQuoteReceived(_workOrder.Id);
            else
                Db.SetStageInCreation(_workOrder.Id);

            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            ApplyMode();
            UpdatePdfButtonVisibility();
            UpdateCompanyPdfButtons();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de reset le devis.\n\n{ex.Message}",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public WorkOrderWindow()
        : this(null, WorkOrderEditMode.Architecte, createMode: true)
    {
    }

    public WorkOrderWindow(long workOrderId, WorkOrderEditMode mode)
        : this(Db.GetWorkOrderById(workOrderId), mode, createMode: false)
    {
    }

    // ✅ Permet à MainWindow de retrouver une fenêtre déjà ouverte pour un bon donné
    // (utilisé pour rafraîchir l'affichage après une synchronisation serveur en tâche de fond).
    public long? CurrentWorkOrderId => _workOrder?.Id > 0 ? _workOrder.Id : (long?)null;

    // ✅ Fix (demande de Joe : "je veux pouvoir travailler sur les 2 fenêtres sans avoir à
    // les fermer") : cette fenêtre n'est plus modale (voir tous les appels ShowDialog -> Show
    // dans MainWindow/DashboardPage/AccountingPage/ArchivesPage/TrashPage), donc plusieurs
    // fenêtres peuvent maintenant être ouvertes en même temps. Deux garde-fous nécessaires
    // pour éviter un vrai risque de données introduit par ce changement :
    // 1) Ouvrir DEUX FOIS le même bon existant (par deux endroits différents) donnerait deux
    //    copies en mémoire qui s'écraseraient l'une l'autre à l'enregistrement -> on active la
    //    fenêtre déjà ouverte au lieu d'en créer une deuxième (ActivateIfAlreadyOpen).
    // 2) Le numéro d'un NOUVEAU bon est calculé à l'OUVERTURE (MAX+1, voir CreateDefaultWorkOrder),
    //    pas à l'enregistrement : ouvrir deux bons "Nouveau" avant d'en enregistrer un premier
    //    leur donnerait le MÊME numéro -> on empêche d'ouvrir un deuxième bon en création tant
    //    qu'un premier est encore ouvert (AnyCreateModeWindowOpen), on active celui déjà ouvert.
    public bool IsCreateMode => _isCreateMode;

    public static bool ActivateIfAlreadyOpen(long workOrderId)
    {
        if (workOrderId <= 0) return false;

        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is WorkOrderWindow wow && wow.CurrentWorkOrderId == workOrderId)
            {
                try
                {
                    if (wow.WindowState == WindowState.Minimized) wow.WindowState = WindowState.Normal;
                    wow.Activate();
                }
                catch { }
                return true;
            }
        }

        return false;
    }

    public static bool ActivateExistingCreateModeWindow()
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is WorkOrderWindow wow && wow.IsCreateMode)
            {
                try
                {
                    if (wow.WindowState == WindowState.Minimized) wow.WindowState = WindowState.Normal;
                    wow.Activate();
                }
                catch { }
                return true;
            }
        }

        return false;
    }

    // ✅ Recharge les données du bon depuis la base locale (appelé après une synchro serveur
    // qui a mis à jour ce bon alors que la fenêtre était déjà ouverte) : sans cela, les lignes
    // de devis, le PDF forfait, etc. reçus de l'entreprise n'apparaissaient pas tant que la
    // fenêtre n'était pas fermée puis réouverte.
    public void ReloadAfterServerSync()
    {
        try
        {
            if (_workOrder == null) return;

            void DoReload()
            {
                try
                {
                    LoadWorkOrder();
                    ApplyMode();
                    RecomputeTotals();
                    UpdatePdfButtonVisibility();
                    UpdateCompanyPdfButtons();
                }
                catch { }
            }

            if (Dispatcher.CheckAccess())
                DoReload();
            else
                Dispatcher.Invoke(DoReload);
        }
        catch { }
    }

    private WorkOrderWindow(WorkOrder? workOrder, WorkOrderEditMode mode, bool createMode)
    {
        try
        {
            InitializeComponent();

            // ✅ Côté Architecte : ouvrir le bon en plein écran par défaut, sans flash de
            // la fenêtre en taille normale avant la bascule (même correctif que
            // MainWindow, voir MainWindow.xaml.cs pour le détail du piège WPF).
            // SourceInitialized se déclenche avant que la fenêtre ne soit peinte à
            // l'écran, contrairement à Loaded qui arrive après un premier rendu visible.
            if (mode == WorkOrderEditMode.Architecte)
                this.SourceInitialized += (s, e) => { WindowState = WindowState.Maximized; };

            Db.Init();

            _workOrder = workOrder;
            _mode = mode;
            _isCreateMode = createMode || workOrder == null;

            LinesGrid.ItemsSource = _lines;

            HookReserveMaxLength();
            HookNumericInputsNoSelectAll();
            HookMainScrollFix();
            HookQuoteNotesLimit();
            HookDescriptionLimit();

            LoadStaticHeader();
            LoadLists();
            ApplyDemandLabels();
            LoadWorkOrder();

            _pdfAvailableForExternal = (_mode == WorkOrderEditMode.Signataire);

            ApplyMode();
            RecomputeTotals();

            Closing += WorkOrderWindow_Closing;
        }
        catch (Exception ex)
        {
            try
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Erreur lors de l'ouverture du bon d'intervention :\n\n{ex.Message}",
                    "Bon d'intervention",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }

            // Fallback: initialise un bon par défaut pour permettre l'affichage
            try
            {
                _workOrder = CreateDefaultWorkOrder();
                _isCreateMode = true;
                ApplyMode();
                RecomputeTotals();
            }
            catch { }
        }
    }

    private void HookMainScrollFix()
    {
        try
        {
            if (LinesGrid != null)
            {
                LinesGrid.PreviewMouseWheel -= LinesGrid_PreviewMouseWheel;
                LinesGrid.PreviewMouseWheel += LinesGrid_PreviewMouseWheel;
            }

            PreviewMouseWheel -= WorkOrderWindow_PreviewMouseWheel;
            PreviewMouseWheel += WorkOrderWindow_PreviewMouseWheel;

            // ✅ La ligne sélectionnée du tableau Libellé/Matériel restait bordée en bleu
            // même après un clic ailleurs sur la fenêtre (20.07.2026, demande de Joe) :
            // IsSelected d'une DataGridRow ne se désélectionne pas tout seul en perdant le
            // focus. On désélectionne explicitement dès qu'un clic a lieu hors de LinesGrid.
            PreviewMouseDown -= WorkOrderWindow_PreviewMouseDown_ClearLinesGridSelection;
            PreviewMouseDown += WorkOrderWindow_PreviewMouseDown_ClearLinesGridSelection;
        }
        catch { }
    }

    private void WorkOrderWindow_PreviewMouseDown_ClearLinesGridSelection(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (LinesGrid == null) return;

            var dep = e.OriginalSource as DependencyObject;
            if (FindAncestor<DataGrid>(dep) == LinesGrid) return;

            // ✅ Ne pas désélectionner si le clic vient du bouton "Supprimer ligne" lui-même
            // (22.07.2026, correctif) : le bouton est hors du DataGrid, donc ce handler
            // PreviewMouseDown (qui se déclenche AVANT le Click du bouton) videait la
            // sélection juste avant que DeleteLineButton_Click ne la lise, rendant la
            // suppression impossible.
            var clickedButton = FindAncestor<System.Windows.Controls.Button>(dep);
            if (clickedButton == DeleteLineButton || clickedButton == AddLineButton) return;

            LinesGrid.UnselectAllCells();
            LinesGrid.UnselectAll();
            LinesGrid.SelectedItem = null;
            LinesGrid.SelectedIndex = -1;
        }
        catch { }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void WorkOrderWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (MainScrollViewer == null) return;

            if (e.Handled)
            {
                MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
        catch { }
    }

    private void LinesGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (MainScrollViewer == null) return;

            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
        catch { }
    }

    private void HookNumericInputsNoSelectAll()
    {
        HookNoSelectAll(LaborHoursTextBox);
        HookNoSelectAll(LaborRateTextBox);
        HookNoSelectAll(TravelQtyTextBox);
        HookNoSelectAll(TravelRateTextBox);
        HookNoSelectAll(TvaRateTextBox);
        HookNoSelectAll(DiscountRateTextBox);
        HookNoSelectAll(DiscountRateTextBox2);
        HookNoSelectAll(ForfaitTtcTextBox);
    }

    private void HookNoSelectAll(WpfTextBox? tb)
    {
        if (tb == null) return;

        tb.PreviewMouseLeftButtonDown += (s, e) =>
        {
            try
            {
                if (tb.IsKeyboardFocusWithin) return;

                e.Handled = true;
                tb.Focus();

                var p = e.GetPosition(tb);
                int idx = tb.GetCharacterIndexFromPoint(p, true);
                if (idx < 0) idx = tb.Text?.Length ?? 0;
                tb.CaretIndex = idx;
                tb.SelectionLength = 0;
            }
            catch { }
        };

        tb.GotKeyboardFocus += (s, e) =>
        {
            try { tb.SelectionLength = 0; } catch { }
        };
    }

    private void DiscountRateTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfTextBox tb) return;

        if (!tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
            tb.CaretIndex = tb.Text?.Length ?? 0;
        }
    }

    private void PercentInt_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfTextBox tb) return;

        var v = ParseDouble(tb.Text, 0);
        v = Math.Max(0, v);
        var intVal = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        tb.Text = intVal == 0 ? "" : intVal.ToString(CultureInfo.InvariantCulture);
    }

    private void HookReserveMaxLength()
    {
        if (_reserveLimitHooked) return;
        _reserveLimitHooked = true;

        if (ReserveComboBox == null) return;

        ReserveComboBox.IsEditable = true;

        ReserveComboBox.Loaded += (_, __) =>
        {
            try
            {
                if (ReserveComboBox.Template?.FindName("PART_EditableTextBox", ReserveComboBox) is WpfTextBox tb)
                {
                    tb.MaxLength = ReserveMaxLength;

                    tb.TextChanged -= ReserveEditableTextBox_TextChanged;
                    tb.TextChanged += ReserveEditableTextBox_TextChanged;

                    if ((tb.Text ?? "").Length > ReserveMaxLength)
                    {
                        tb.Text = (tb.Text ?? "").Substring(0, ReserveMaxLength);
                        tb.CaretIndex = tb.Text.Length;
                    }
                }

                ReserveComboBox.SelectionChanged -= ReserveComboBox_SelectionChanged_Truncate;
                ReserveComboBox.SelectionChanged += ReserveComboBox_SelectionChanged_Truncate;

                if ((ReserveComboBox.Text ?? "").Length > ReserveMaxLength)
                    ReserveComboBox.Text = (ReserveComboBox.Text ?? "").Substring(0, ReserveMaxLength);
            }
            catch { }
        };
    }

    private void ReserveEditableTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not WpfTextBox tb) return;

            if ((tb.Text ?? "").Length > ReserveMaxLength)
            {
                var caret = tb.CaretIndex;
                tb.Text = (tb.Text ?? "").Substring(0, ReserveMaxLength);
                tb.CaretIndex = Math.Min(caret, tb.Text.Length);
            }
        }
        catch { }
    }

    private void ReserveComboBox_SelectionChanged_Truncate(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (ReserveComboBox == null) return;

            var t = ReserveComboBox.Text ?? "";
            if (t.Length > ReserveMaxLength)
                ReserveComboBox.Text = t.Substring(0, ReserveMaxLength);
        }
        catch { }
    }

    private void ApplyMode()
    {
        ApplyFieldPermissions();
        UpdatePdfButtonVisibility();
        UpdateExportButtonsVisibility();
        UpdateCompanyPdfButtons();
        // ✅ Rafraîchit l'affichage selon _quoteMode courant (ne le redétecte pas, voir
        // DetectQuoteModeFromData, appelé une seule fois au chargement).
        ApplyQuoteModeUi();
    }

    private void UpdateExportButtonsVisibility()
    {
        var isArchitecte = _mode == WorkOrderEditMode.Architecte;

        if (ExportQuoteRequestButton != null)
            ExportQuoteRequestButton.Visibility = isArchitecte ? Visibility.Visible : Visibility.Collapsed;

        if (ExportSignatureRequestButton != null)
            ExportSignatureRequestButton.Visibility = isArchitecte ? Visibility.Visible : Visibility.Collapsed;

        if (ResetValidationBottomButton != null)
            ResetValidationBottomButton.Visibility = isArchitecte ? Visibility.Visible : Visibility.Collapsed;

        // ✅ "Reset Devis" (17.08.2026, demande de Joe) : réservé à l'utilisateur (Architecte),
        // pas à l'Entreprise, même visibilité que "Reset validation".
        if (ResetQuoteBottomButton != null)
            ResetQuoteBottomButton.Visibility = isArchitecte ? Visibility.Visible : Visibility.Collapsed;
    }

    // ✅ Toujours disponible (23.07.2026, demande de Joe) : le bouton se cachait auparavant selon
    // le mode (Entreprise/Devis) et la présence de données de devis, ce qui le rendait absent dans
    // certains cas alors qu'on doit toujours pouvoir générer le PDF.
    private void UpdatePdfButtonVisibility()
    {
        if (PdfButton == null) return;

        PdfButton.Visibility = Visibility.Visible;
        PdfButton.IsEnabled = true;
    }

    private void ApplyFieldPermissions()
    {
        var isArchitecte = !_formLocked;
        var isEntreprise = !_formLocked;
        var isSignataire = !_formLocked;

        SetTextBoxEditable(BdrNumberTextBox, isArchitecte);
        SetEnabled(RequestDatePicker, isArchitecte);

        SetEnabled(ReserveComboBox, isArchitecte);
        SetEnabled(RequestedByComboBox, isArchitecte);
        SetEnabled(PerformedByComboBox, isArchitecte);
        SetEnabled(PlaceComboBox, isArchitecte);
        SetEnabled(EtageComboBox, isArchitecte);
        SetEnabled(DeadlineDatePicker, isArchitecte);

        SetTextBoxEditable(DescriptionTextBox, isArchitecte);

        var devisEditable = isArchitecte || isEntreprise;

        // ✅ Boutons 2 positions grisés hors édition (04.08.2026, demande de Joe) : sinon un clic
        // sur "Devis standard"/"Devis PDF" en lecture seule ne fait rien visiblement, sans
        // indiquer pourquoi.
        if (QuoteModeStandardButton != null) QuoteModeStandardButton.IsEnabled = devisEditable;
        if (QuoteModePdfButton != null) QuoteModePdfButton.IsEnabled = devisEditable;

        // ✅ Bouton 2 positions Devis standard/Devis PDF (04.08.2026, demande de Joe) : remplace
        // l'ancienne détection implicite (forfaitUsed/forfaitTtcUsed dérivés des valeurs saisies)
        // par la position explicitement choisie -- _quoteMode est maintenant la seule source de
        // vérité. Zone neutre (QuoteMode.None) : aucun des deux n'est éditable.
        var devisStandardEditable = devisEditable && _quoteMode == QuoteMode.Standard;
        var forfaitTtcEditable = devisEditable && _quoteMode == QuoteMode.Pdf;

        SetTextBoxEditable(QuoteNameTextBox, devisEditable);
        SetEnabled(QuoteDatePicker, devisEditable);

        SetTextBoxEditable(LaborHoursTextBox, devisStandardEditable);
        SetTextBoxEditable(LaborRateTextBox, devisStandardEditable);
        SetTextBoxEditable(TravelQtyTextBox, devisStandardEditable);
        SetTextBoxEditable(TravelRateTextBox, devisStandardEditable);
        SetTextBoxEditable(DiscountRateTextBox, devisStandardEditable);
        SetTextBoxEditable(DiscountRateTextBox2, devisStandardEditable);
        SetTextBoxEditable(DiscountNameTextBox, devisStandardEditable);
        SetTextBoxEditable(DiscountName2TextBox, devisStandardEditable);
        SetTextBoxEditable(ForfaitTtcTextBox, forfaitTtcEditable);
        // ✅ N° du devis (04.08.2026, demande de Joe) : même condition d'édition que Forfait TTC,
        // les deux champs sont liés au même devis PDF.
        SetTextBoxEditable(ForfaitQuoteNumberTextBox, forfaitTtcEditable);

        SetTextBoxEditable(TvaRateTextBox, devisEditable);
        SetTextBoxEditable(QuoteNotesTextBox, devisEditable);

        if (AddLineButton != null) AddLineButton.IsEnabled = devisStandardEditable;
        if (DeleteLineButton != null) DeleteLineButton.IsEnabled = devisStandardEditable;
        if (LinesGrid != null) LinesGrid.IsReadOnly = !devisStandardEditable;

        // ✅ Compteur de lignes (22.07.2026, demande de Joe) : réplique du "(N / 15 max.)"
        // déjà affiché côté Blazor.
        if (AddLineButton != null)
            AddLineButton.Content = $"+ Ajouter ligne ({_lines.Count} / {QuoteMaxLines} max.)";

        var validationEditable = isArchitecte || isSignataire;
        SetReadOnlyLook(SignatureNameComboBox, validationEditable);
        SetReadOnlyLook(SignatureDatePicker, validationEditable);

        SetEnabled(DecisionValidateRadio, validationEditable);
        SetEnabled(DecisionRefuseRadio, validationEditable);
        SetEnabled(DecisionCancelRadio, validationEditable);

        if (SignatureInkCanvas is WpfInkCanvas ic)
        {
            ic.IsEnabled = validationEditable;
            ic.EditingMode = validationEditable ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        }

        if (ImportSignatureButton != null) ImportSignatureButton.IsEnabled = validationEditable;
        if (ClearSignatureButton != null) ClearSignatureButton.IsEnabled = validationEditable;
        if (ResetValidationBottomButton != null) ResetValidationBottomButton.IsEnabled = isArchitecte;
        if (ResetQuoteBottomButton != null) ResetQuoteBottomButton.IsEnabled = isArchitecte;

        if (SaveButton != null) SaveButton.IsEnabled = !_formLocked;
        if (ModifyButton != null) ModifyButton.IsEnabled = _formLocked;

        // boutons PDF devis forfaitaire
        UpdateCompanyPdfButtons();
    }

    private void ModifyButton_Click(object sender, RoutedEventArgs e)
    {
        _formLocked = false;
        _unlockedSnapshot = BuildFormSnapshot();
        ApplyMode();
    }

    private void UpdateCompanyPdfButtons()
    {
        var hasPdf = false;
        try
        {
            hasPdf = _workOrder != null
                && _workOrder.ForfaitPdfFileBytes != null
                && _workOrder.ForfaitPdfFileBytes.Length > 0;
        }
        catch { hasPdf = false; }

        if (CompanyPdfOpenButton != null) CompanyPdfOpenButton.IsEnabled = hasPdf;
        if (CompanyPdfRemoveButton != null) CompanyPdfRemoveButton.IsEnabled = hasPdf && !_formLocked;

        // ✅ Fix (04.08.2026, demande de Joe : "hide don't delete" au changement de position) :
        // l'exclusivité standard/pdf est désormais assurée par _quoteMode (le bouton n'est même
        // plus visible hors position "Devis PDF", voir QuotePdfSection) -- ne dépend plus de
        // AreStandardQuoteFieldsAllZero, qui resterait faux si d'anciennes données standard sont
        // encore présentes mais masquées.
        if (CompanyPdfUploadButton != null) CompanyPdfUploadButton.IsEnabled = !_formLocked;

        if (CompanyPdfFileNameTextBlock != null)
        {
            var name = _workOrder?.ForfaitPdfFileName ?? "";
            CompanyPdfFileNameTextBlock.Text = string.IsNullOrWhiteSpace(name) ? "" : name;
        }

        // ✅ N° du devis (04.08.2026, demande de Joe) : reste toujours visible désormais (comme
        // "Montant TTC du pdf"), plus de masquage tant qu'aucun pdf n'est joint -- seule
        // l'obligation à l'enregistrement subsiste (voir EnsureQuoteRequiredFieldsOrWarn).
    }

    // =========================
    // ✅ Bouton 2 positions Devis standard/Devis PDF (04.08.2026, demande de Joe)
    // =========================

    // ✅ Pré-sélection automatique à l'ouverture d'un bon existant : PDF déjà joint -> position
    // PDF ; lignes/Main d'œuvre/Déplacements/Rabais déjà remplis -> position standard ; sinon
    // (bon neuf) -> zone neutre, aucune position choisie.
    private QuoteMode DetectQuoteModeFromData()
    {
        if (_workOrder == null) return QuoteMode.None;

        var hasPdf = _workOrder.ForfaitPdfFileBytes != null && _workOrder.ForfaitPdfFileBytes.Length > 0;
        if (hasPdf) return QuoteMode.Pdf;

        // ✅ Ancien Forfait (ForfaitQty*UnitPrice, gelé depuis le 20.07.2026 -- plus de champ UI
        // pour le saisir, voir historique Git) : compté comme donnée "standard" pour ne pas
        // renvoyer en zone neutre un bon très ancien qui l'utilisait déjà.
        var hasStandardData =
            _lines.Any(l => !string.IsNullOrWhiteSpace(l.Label) || Math.Abs(l.LineTotal) > 0.0000000001)
            || Math.Abs(_workOrder.LaborHours) > 0.0000000001
            || Math.Abs(_workOrder.LaborRate) > 0.0000000001
            || Math.Abs(_workOrder.TravelQty) > 0.0000000001
            || Math.Abs(_workOrder.TravelRate) > 0.0000000001
            || Math.Abs(_workOrder.DiscountRate) > 0.0000000001
            || Math.Abs(_workOrder.DiscountRate2) > 0.0000000001
            || Math.Abs(_workOrder.ForfaitQty * _workOrder.ForfaitUnitPrice) > 0.0000000001;

        return hasStandardData ? QuoteMode.Standard : QuoteMode.None;
    }

    private void QuoteModeStandardButton_Click(object sender, RoutedEventArgs e) => TrySwitchQuoteMode(QuoteMode.Standard);
    private void QuoteModePdfButton_Click(object sender, RoutedEventArgs e) => TrySwitchQuoteMode(QuoteMode.Pdf);

    // ✅ Changement de position après coup (demande de Joe) : les données de l'AUTRE mode ne
    // sont jamais effacées, seulement masquées -- reclique sur le premier mode pour les
    // retrouver. Pas de confirmation nécessaire puisque rien n'est perdu.
    private void TrySwitchQuoteMode(QuoteMode mode)
    {
        if (_formLocked) return;
        if (_quoteMode == mode) return;

        _quoteMode = mode;
        ApplyQuoteModeUi();
        QueueRecomputeTotals();
    }

    private void ApplyQuoteModeUi()
    {
        if (QuoteModeStandardButton == null || QuoteModePdfButton == null) return;

        var standard = _quoteMode == QuoteMode.Standard;
        var pdf = _quoteMode == QuoteMode.Pdf;

        QuoteModeStandardButton.Style = (Style)FindResource(standard ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        QuoteModePdfButton.Style = (Style)FindResource(pdf ? "QuotePdfButtonActiveStyle" : "SecondaryButtonStyle");

        var standardVisibility = standard ? Visibility.Visible : Visibility.Collapsed;
        if (QuoteLineButtonsPanel != null) QuoteLineButtonsPanel.Visibility = standardVisibility;
        if (LinesGrid != null) LinesGrid.Visibility = standardVisibility;
        if (TotalMaterialBox != null) TotalMaterialBox.Visibility = standardVisibility;

        var standardRowHeight = standard ? new GridLength(26) : new GridLength(0);
        if (LaborRowDef != null) LaborRowDef.Height = standardRowHeight;
        if (TravelRowDef != null) TravelRowDef.Height = standardRowHeight;
        if (DiscountRowDef != null) DiscountRowDef.Height = standardRowHeight;
        if (DiscountRow2Def != null) DiscountRow2Def.Height = standardRowHeight;

        if (QuotePdfSection != null) QuotePdfSection.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;

        UpdateCompanyPdfButtons();
    }

    private static void SetEnabled(WpfUIElement? e, bool enabled)
    {
        if (e == null) return;
        e.IsEnabled = enabled;
    }

    private static void SetTextBoxEditable(WpfTextBox? tb, bool editable)
    {
        if (tb == null) return;
        tb.IsReadOnly = !editable;
        tb.IsEnabled = true;
    }

    // ✅ 11.08.2026 (demande de Joe) : "Nom"/"Date" (section Validation) restaient grisés
    // (IsEnabled=False, via SetEnabled) dès qu'un BI existant est ouvert par l'architecte
    // (_formLocked=true) -- le texte déjà saisi devenait illisible en gris clair. Même
    // principe que SetTextBoxEditable : IsHitTestVisible bloque l'interaction (clic,
    // ouverture du calendrier/de la liste) sans jamais désactiver le contrôle, donc le texte
    // garde son rendu normal (noir) au lieu du gris "disabled" du thème WPF.
    private static void SetReadOnlyLook(System.Windows.FrameworkElement? e, bool editable)
    {
        if (e == null) return;
        e.IsHitTestVisible = editable;
        e.Focusable = editable;
        e.IsEnabled = true;
    }

    private void ApplyDemandLabels()
    {
        try
        {
            var pid = Db.GetCurrentProjectId();
            if (!pid.HasValue || pid.Value <= 0) return;

            var projectId = pid.Value;

            if (ReserveLabelTextBlock != null) ReserveLabelTextBlock.Text = Db.GetLabelReserve(projectId);
            if (RequestedByLabelTextBlock != null) RequestedByLabelTextBlock.Text = Db.GetLabelRequestedBy(projectId);
            if (PerformedByLabelTextBlock != null) PerformedByLabelTextBlock.Text = Db.GetLabelPerformedBy(projectId);
            if (PlaceLabelTextBlock != null) PlaceLabelTextBlock.Text = Db.GetLabelPlace(projectId);
            if (EtageLabelTextBlock != null) EtageLabelTextBlock.Text = Db.GetLabelEtage(projectId);
            // ✅ 29.07.2026 (demande de Joe) : "Délai" n'est plus personnalisable, le texte
            // figé "Délai" du XAML (DeadlineLabelTextBlock) reste tel quel.

            // ✅ 29.07.2026 (demande de Joe, oubli lors de l'ajout de la liste "Nom") : le
            // libellé de la section Validation ne suivait pas le renommage fait dans la page
            // Listes (LabelSignatoryNameTextBox / Db.GetLabelSignatoryName), contrairement aux
            // 5 autres libellés ci-dessus.
            if (SignatoryNameLabelTextBlock != null)
                SignatoryNameLabelTextBlock.Text = Db.GetLabelSignatoryName(projectId) + " (obligatoire)";
        }
        catch { }
    }

    private static (string line1, string line2) SplitAddressTwoLines(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
            return ("", "");

        var lastComma = s.LastIndexOf(',');
        if (lastComma >= 0 && lastComma < s.Length - 1)
        {
            var a = s.Substring(0, lastComma).Trim();
            var b = s.Substring(lastComma + 1).Trim();
            return (a, b);
        }

        return (s, "");
    }

    private void LoadStaticHeader()
    {
        ArchitectNameTextBlock.Text =
            string.IsNullOrWhiteSpace(Db.GetArchitectName()) ? "Architecte" : Db.GetArchitectName();

        var archAddr = Db.GetArchitectAddress() ?? "";
        var (a1, a2) = SplitAddressTwoLines(archAddr);
        if (ArchitectAddressLine1TextBlock != null) ArchitectAddressLine1TextBlock.Text = a1;
        if (ArchitectAddressLine2TextBlock != null) ArchitectAddressLine2TextBlock.Text = a2;
        if (ArchitectRefTextBlock != null) ArchitectRefTextBlock.Text = Db.GetArchitectRef();
        if (ArchitectRef2TextBlock != null) ArchitectRef2TextBlock.Text = Db.GetArchitectRef2();

        LoadArchitectLogo(Db.GetArchitectLogoPath());

        var currentProject = Db.GetCurrentProject();
        ProjectNameTextBlock.Text = currentProject?.Name ?? "";

        var projAddr = currentProject?.Address ?? "";
        var (p1, p2) = SplitAddressTwoLines(projAddr);
        if (ProjectAddressLine1TextBlock != null) ProjectAddressLine1TextBlock.Text = p1;
        if (ProjectAddressLine2TextBlock != null) ProjectAddressLine2TextBlock.Text = p2;

        var managerName = (currentProject?.ManagerName ?? "").Trim();
        var managerContact = (currentProject?.ManagerContact ?? "").Trim();

        if (ProjectManagerRefTextBlock != null)
            ProjectManagerRefTextBlock.Text = string.IsNullOrWhiteSpace(managerName) ? "" : $"Réf : {managerName}";

        if (ProjectManagerContactTextBlock != null)
            ProjectManagerContactTextBlock.Text = managerContact;
    }

    private void LoadArchitectLogo(string? path)
    {
        ArchitectLogoImage.Source = null;
        ArchitectLogoEmptyText.Visibility = Visibility.Visible;

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

            ArchitectLogoImage.Source = bmp;
            ArchitectLogoEmptyText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ArchitectLogoImage.Source = null;
            ArchitectLogoEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void LoadLists()
    {
        _isLoading = true;
        try
        {
            ReserveComboBox.ItemsSource = Db.WithEmptyOption(Db.GetReserves());
            RequestedByComboBox.ItemsSource = Db.WithEmptyOption(Db.GetRequesters());
            PerformedByComboBox.ItemsSource = Db.WithEmptyOption(Db.GetCompanies());
            PlaceComboBox.ItemsSource = Db.WithEmptyOption(Db.GetPlaces());
            EtageComboBox.ItemsSource = Db.WithEmptyOption(Db.GetEtages());
            SignatureNameComboBox.ItemsSource = Db.WithEmptyOption(Db.GetSignatoryNames());
        }
        finally { _isLoading = false; }
    }

    private void LoadWorkOrder()
    {
        _isLoading = true;
        try
        {
            if (_workOrder == null)
            {
                _workOrder = CreateDefaultWorkOrder();
                Title = "Nouveau bon d'intervention";
            }
            else
            {
                try
                {
                    _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;
                    Title = $"Bon d'intervention — N° {_workOrder.BdrDisplay}";
                }
                catch (Exception ex)
                {
                    // si lecture bdd plante, afficher message et basculer en création
                    try { System.Windows.MessageBox.Show(this, $"Impossible de charger le bon depuis la base :\n\n{ex.Message}", "Bon d'intervention", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
                    _workOrder = CreateDefaultWorkOrder();
                    _isCreateMode = true;
                    Title = "Nouveau bon d'intervention";
                }
            }

            _formLocked = !_isCreateMode && _workOrder.Id > 0;

            BdrNumberTextBox.Text = _workOrder.BdrNumber.ToString(CultureInfo.InvariantCulture);
            // ✅ Fix (demande de Joe : "sur le bon, il m'écrit P1") : ce TextBlock n'était
            // jamais rempli en code, il gardait donc le texte de conception figé dans le XAML
            // ("- P1", un ancien préfixe jamais mis à jour) au lieu du vrai tag du dossier
            // (WorkOrder.ProjectTag, en "D" depuis le renommage).
            ProjectTagTextBlock.Text = string.IsNullOrWhiteSpace(_workOrder.ProjectTag) ? "" : $"- {_workOrder.ProjectTag}";
            RequestDatePicker.SelectedDate = _workOrder.RequestDate == default ? DateTime.Today : _workOrder.RequestDate;

            ReserveComboBox.Text = _workOrder.Reserve ?? "";
            RequestedByComboBox.Text = _workOrder.RequestedBy ?? "";
            PerformedByComboBox.Text = _workOrder.PerformedBy ?? "";

            PlaceComboBox.Text = _workOrder.Place ?? "";
            EtageComboBox.Text = _workOrder.Etage ?? "";
            // ✅ 29.07.2026 (demande de Joe) : champ vide par défaut (pas de pré-remplissage
            // à la date du jour) tant qu'aucun délai n'a été renseigné.
            DeadlineDatePicker.SelectedDate = _workOrder.DeadlineDate == default ? (DateTime?)null : _workOrder.DeadlineDate;

            DescriptionTextBox.Text = EnforceDescriptionRules(_workOrder.Description ?? "");

            QuoteNameTextBox.Text = _workOrder.QuoteName ?? "";
            QuoteDatePicker.SelectedDate = _workOrder.QuoteDate == default ? DateTime.Today : _workOrder.QuoteDate;

            LaborHoursTextBox.Text = FormatInputNumber(_workOrder.LaborHours);
            LaborRateTextBox.Text = FormatMoney2DecOrEmpty(_workOrder.LaborRate);
            TravelQtyTextBox.Text = FormatInputNumber(_workOrder.TravelQty);
            TravelRateTextBox.Text = FormatMoney2DecOrEmpty(_workOrder.TravelRate);

            ForfaitTtcTextBox.Text = FormatMoney2DecOrEmpty(_workOrder.ForfaitTtc);
            ForfaitQuoteNumberTextBox.Text = _workOrder.ForfaitQuoteNumber ?? "";

            var tvaRate = _workOrder.TvaRate <= 0 ? 8.1 : _workOrder.TvaRate;
            TvaRateTextBox.Text = tvaRate.ToString("0.00", CultureInfo.InvariantCulture);

            // ✅ Vide par défaut (18.08.2026, demande de Joe : "enlever les 0 écrits par défaut
            // dans la colonne Qt"), même principe que Labor/Travel Qt (FormatInputNumber).
            DiscountRateTextBox.Text = _workOrder.DiscountRate <= 0
                ? ""
                : ((int)Math.Round(_workOrder.DiscountRate, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            DiscountRateTextBox2.Text = _workOrder.DiscountRate2 <= 0
                ? ""
                : ((int)Math.Round(_workOrder.DiscountRate2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

            DiscountNameTextBox.Text = _workOrder.DiscountName ?? "";
            DiscountName2TextBox.Text = _workOrder.DiscountName2 ?? "";

            QuoteNotesTextBox.Text = EnforceQuoteNotesRules(_workOrder.QuoteNotes ?? "");

            SignatureNameComboBox.Text = _workOrder.SignatureName ?? "";
            SignatureDatePicker.SelectedDate = _workOrder.SignatureDate;

            _existingSignaturePng = _workOrder.SignaturePng;
            _signatureCleared = false;
            LoadSignaturePreview(_existingSignaturePng);

            ApplyValidationDecisionToUi(_workOrder.ValidationDecision);

            _lines.Clear();
            _deletedLineIds.Clear();

            if (!_isCreateMode && _workOrder.Id > 0)
            {
                foreach (var line in Db.GetWorkOrderLines(_workOrder.Id))
                {
                    line.RecomputeLineTotal();
                    _lines.Add(line);
                }
            }

            if (_lines.Count == 0)
                _lines.Add(new WorkOrderLine());

            TrimTrailingEmptyLinesToMax();

            // ✅ Bouton 2 positions Devis standard/Devis PDF (04.08.2026, demande de Joe) :
            // pré-sélection automatique à partir des données existantes, une seule fois au
            // chargement (pas à chaque ApplyMode(), qui ne fait que rafraîchir l'affichage).
            _quoteMode = DetectQuoteModeFromData();

            RecomputeTotals();
            ApplyMode();
            UpdatePdfButtonVisibility();
            UpdateCompanyPdfButtons();

            _unlockedSnapshot = BuildFormSnapshot();
        }
        finally { _isLoading = false; }
    }

    // ✅ Voir _unlockedSnapshot ci-dessus : capture tous les champs qui comptent pour
    // "Enregistrer" (mêmes champs que SaveWorkOrder lit depuis l'UI), pour comparaison de valeurs
    // plutôt qu'un suivi événementiel.
    private string BuildFormSnapshot()
    {
        var sb = new StringBuilder();
        void Add(string? s) => sb.Append(s ?? "").Append('␟');
        void AddDate(DateTime? d) => sb.Append(d?.ToString("O") ?? "").Append('␟');

        Add(BdrNumberTextBox.Text);
        Add(ReserveComboBox.Text);
        Add(RequestedByComboBox.Text);
        Add(PerformedByComboBox.Text);
        Add(PlaceComboBox.Text);
        Add(EtageComboBox.Text);
        AddDate(RequestDatePicker.SelectedDate);
        AddDate(DeadlineDatePicker.SelectedDate);
        Add(DescriptionTextBox.Text);
        Add(QuoteNameTextBox.Text);
        AddDate(QuoteDatePicker.SelectedDate);
        Add(LaborHoursTextBox.Text);
        Add(LaborRateTextBox.Text);
        Add(TravelQtyTextBox.Text);
        Add(TravelRateTextBox.Text);
        Add(DiscountRateTextBox.Text);
        Add(DiscountRateTextBox2.Text);
        Add(DiscountNameTextBox.Text);
        Add(DiscountName2TextBox.Text);
        Add(ForfaitTtcTextBox.Text);
        Add(ForfaitQuoteNumberTextBox.Text);
        Add(TvaRateTextBox.Text);
        Add(QuoteNotesTextBox.Text);
        Add(SignatureNameComboBox.Text);
        AddDate(SignatureDatePicker.SelectedDate);
        Add(GetSelectedValidationDecision());

        foreach (var l in _lines)
        {
            Add(l.Label);
            Add(l.QtyDisplay);
            Add(l.UnitPriceDisplay);
        }

        var sig = CaptureSignaturePng();
        Add(sig == null ? "" : Convert.ToBase64String(sig));

        return sb.ToString();
    }

    private WorkOrder CreateDefaultWorkOrder()
    {
        var currentProject = Db.GetCurrentProject();
        long? projectId = currentProject?.Id;

        if (!projectId.HasValue || projectId.Value <= 0)
            projectId = Db.GetCurrentProjectId();

        var bdr = projectId.HasValue && projectId.Value > 0
            ? Db.GetNextBdrNumberForProject(projectId.Value)
            : Db.GetNextBdrNumber();

        return new WorkOrder
        {
            BdrNumber = bdr,
            ProjectId = projectId,

            Reserve = projectId.HasValue ? Db.GetDefaultReserve(projectId.Value) : Db.GetDefaultReserve(),
            RequestedBy = projectId.HasValue ? Db.GetDefaultRequester(projectId.Value) : Db.GetDefaultRequester(),
            PerformedBy = projectId.HasValue ? Db.GetDefaultCompany(projectId.Value) : Db.GetDefaultCompany(),
            Place = projectId.HasValue ? Db.GetDefaultPlace(projectId.Value) : Db.GetDefaultPlace(),
            Etage = projectId.HasValue ? Db.GetDefaultEtage(projectId.Value) : Db.GetDefaultEtage(),

            RequestDate = DateTime.Today,
            // ✅ 29.07.2026 (demande de Joe) : pas de date par défaut pour le Délai.

            Description = "",
            QuoteName = "",
            QuoteDate = DateTime.Today,

            LaborHours = 0,
            LaborRate = 0,
            TravelQty = 0,
            TravelRate = 0,

            ForfaitQty = 0,
            ForfaitUnitPrice = 0,
            ForfaitPdfFileName = "",
            ForfaitPdfFileBytes = null,
            ForfaitTtc = 0,

            TvaRate = 8.1,
            QuoteNotes = "",
            DiscountRate = 0,
            DiscountRate2 = 0,
            // ✅ Nom de rabais par défaut (18.08.2026, demande de Joe) : pré-rempli avec le dernier
            // texte saisi par l'architecte (voir Db.SetDefaultDiscountName, appelé à
            // l'enregistrement).
            DiscountName = Db.GetDefaultDiscountName(),
            DiscountName2 = Db.GetDefaultDiscountName2(),

            SignatureName = "",
            SignatureDate = null,
            SignaturePng = null,

            ValidationDecision = ""
        };
    }

    private void LinesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (LinesGrid == null) return;

            // ✅ Sélection multiple (17.08.2026, demande de Joe) : Ctrl/Shift enfoncé -> on laisse
            // le comportement natif du DataGrid (SelectionMode="Extended") gérer la sélection au
            // lieu de forcer une sélection simple + édition de cellule ci-dessous.
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
                return;

            var dep = (DependencyObject)e.OriginalSource;

            while (dep != null && dep is not DataGridRow && dep is not DataGridCell && dep is not DataGridColumnHeader)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridColumnHeader)
                return;

            if (dep is DataGridRow row)
            {
                if (!row.IsSelected)
                {
                    LinesGrid.SelectedItem = row.Item;
                    LinesGrid.ScrollIntoView(row.Item);
                }
                row.Focus();
                return;
            }

            if (dep is DataGridCell cell)
            {
                if (cell.DataContext != null && !cell.IsSelected)
                {
                    LinesGrid.SelectedItem = cell.DataContext;
                    LinesGrid.ScrollIntoView(cell.DataContext);
                }

                if (!cell.IsFocused)
                    cell.Focus();

                if (!cell.IsEditing)
                {
                    LinesGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
                    LinesGrid.BeginEdit();
                    e.Handled = true;
                }

                return;
            }
        }
        catch { }
    }

    private static bool IsLineEmpty(WorkOrderLine? l)
    {
        if (l == null) return true;

        var label = (l.Label ?? "").Trim();
        var hasNumbers = Math.Abs(l.Qty) > 0.0000000001 || Math.Abs(l.UnitPrice) > 0.0000000001;

        return string.IsNullOrWhiteSpace(label) && !hasNumbers;
    }

    private void TrimTrailingEmptyLinesToMax()
    {
        while (_lines.Count > QuoteHardMaxItems)
        {
            var last = _lines.LastOrDefault();
            if (!IsLineEmpty(last))
                break;

            _lines.RemoveAt(_lines.Count - 1);
        }
    }

    private void AddLineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count >= QuoteHardMaxItems)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Maximum {QuoteMaxLines} lignes dans le devis.",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var newLine = new WorkOrderLine();
        newLine.RecomputeLineTotal();

        _lines.Add(newLine);

        LinesGrid.SelectedItem = newLine;
        LinesGrid.ScrollIntoView(newLine);

        if (AddLineButton != null)
            AddLineButton.Content = $"+ Ajouter ligne ({_lines.Count} / {QuoteMaxLines} max.)";

        QueueRecomputeTotals();
    }

    // ✅ Suppression multi-lignes (17.08.2026, demande de Joe) : sélection ctrl/souris possible
    // depuis le passage de LinesGrid en SelectionMode="Extended" (voir aussi
    // LinesGrid_PreviewMouseLeftButtonDown, qui laisse maintenant passer le comportement natif du
    // DataGrid quand Ctrl/Shift est enfoncé).
    private void DeleteLineButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = LinesGrid.SelectedItems.Cast<WorkOrderLine>().ToList();
        if (selected.Count == 0)
            return;

        foreach (var line in selected)
        {
            if (line.Id > 0)
                _deletedLineIds.Add(line.Id);

            _lines.Remove(line);
        }

        if (AddLineButton != null)
            AddLineButton.Content = $"+ Ajouter ligne ({_lines.Count} / {QuoteMaxLines} max.)";

        QueueRecomputeTotals();
    }

    private void LinesGrid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (_isLoading) return;
        QueueRecomputeTotals();
    }

    private void LinesGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (_isLoading) return;
        QueueRecomputeTotals();
    }

    private void TotalsInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        QueueRecomputeTotals();
    }

    private void QueueRecomputeTotals()
    {
        if (_recomputeQueued) return;
        _recomputeQueued = true;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _recomputeQueued = false;
            RecomputeTotals();
            ApplyFieldPermissions();
            UpdatePdfButtonVisibility();
        }));
    }

    private void RecomputeTotals()
    {
        if (_isLoading) return;

        foreach (var line in _lines)
            line.RecomputeLineTotal();

        double materialTotal = 0;

        foreach (var l in _lines)
        {
            if (l == null) continue;
            if (IsLineEmpty(l)) continue;

            materialTotal += l.LineTotal;
        }

        // ✅ Plus de champ UI pour le forfait (ligne supprimée du tableau, 20.07.2026) : la valeur
        // vient directement de _workOrder, pour ne pas perdre le montant d'un bon existant.
        var forfaitQty = _workOrder?.ForfaitQty ?? 0;
        var forfaitUnit = _workOrder?.ForfaitUnitPrice ?? 0;
        var forfaitTotal = forfaitQty * forfaitUnit;

        var laborHours = ParseDouble(LaborHoursTextBox?.Text);
        var laborRate = ParseDouble(LaborRateTextBox?.Text);
        var laborTotal = laborHours * laborRate;

        var travelQty = ParseDouble(TravelQtyTextBox?.Text);
        var travelRate = ParseDouble(TravelRateTextBox?.Text);
        var travelTotal = travelQty * travelRate;

        var totalHtBrut = materialTotal + laborTotal + travelTotal + forfaitTotal;

        var discountRate = ParseDouble(DiscountRateTextBox?.Text, 0);
        discountRate = Math.Max(0, discountRate);

        // ✅ Rabais 2 (17.08.2026, demande de Joe) : appliqué consécutivement, sur le montant déjà
        // réduit par Rabais 1 (pas additionné aux taux avant application).
        var discountRate2 = ParseDouble(DiscountRateTextBox2?.Text, 0);
        discountRate2 = Math.Max(0, discountRate2);

        var tvaRate = ParseDouble(TvaRateTextBox?.Text, 8.1);

        double totalHtNet, discountAmount, discountAmount2, tvaAmount, totalTtc;
        var afterDiscount1 = totalHtBrut * (1.0 - (discountRate / 100.0));

        // ✅ Forfait TTC (20.07.2026, demande de Joe) : montant TTC saisi directement -> HT et TVA
        // recalculés à rebours à partir de ce montant, au lieu du sens normal HT -> TVA -> TTC.
        if (IsForfaitTtcUsedFromUi())
        {
            totalTtc = ParseDouble(ForfaitTtcTextBox?.Text, 0);
            totalHtNet = totalTtc / (1.0 + (tvaRate / 100.0));
            tvaAmount = totalTtc - totalHtNet;
            discountAmount = 0;
            discountAmount2 = 0;
        }
        else
        {
            discountAmount = afterDiscount1 - totalHtBrut;

            totalHtNet = afterDiscount1 * (1.0 - (discountRate2 / 100.0));
            discountAmount2 = totalHtNet - afterDiscount1;

            tvaAmount = totalHtNet * (tvaRate / 100.0);
            totalTtc = totalHtNet + tvaAmount;
        }

        var fr = CultureInfo.GetCultureInfo("fr-FR");

        if (MaterialTotalTextBlock != null) MaterialTotalTextBlock.Text = materialTotal.ToString("0.00", fr);
        if (LaborTotalTextBlock != null) LaborTotalTextBlock.Text = laborTotal.ToString("0.00", fr);
        if (TravelTotalTextBlock != null) TravelTotalTextBlock.Text = travelTotal.ToString("0.00", fr);

        var discountDisplay = Math.Abs(discountAmount) < 0.0000000001 ? 0 : discountAmount;
        if (DiscountAmountTextBlock != null) DiscountAmountTextBlock.Text = discountDisplay.ToString("0.00", fr);

        var discountDisplay2 = Math.Abs(discountAmount2) < 0.0000000001 ? 0 : discountAmount2;
        if (DiscountAmountTextBlock2 != null) DiscountAmountTextBlock2.Text = discountDisplay2.ToString("0.00", fr);

        if (TotalHtTextBlock != null) TotalHtTextBlock.Text = totalHtNet.ToString("0.00", fr);

        if (TvaAmountTextBlock != null) TvaAmountTextBlock.Text = tvaAmount.ToString("0.00", fr);
        if (TotalTtcTextBlock != null) TotalTtcTextBlock.Text = totalTtc.ToString("0.00", fr);

        // ✅ "Sous-total 1"/"Sous-total 2" (18.08.2026, demande de Joe) : masquées tant qu'aucun
        // taux n'est saisi sur le rabais correspondant, visibles uniquement en position "Devis
        // standard" (jamais en "Devis PDF", comme Labor/Travel/Discount rows).
        var showSubtotal1 = _quoteMode == QuoteMode.Standard && discountRate > 0.0000000001;
        if (Subtotal1RowDef != null) Subtotal1RowDef.Height = showSubtotal1 ? new GridLength(26) : new GridLength(0);
        if (Subtotal1AmountTextBlock != null) Subtotal1AmountTextBlock.Text = totalHtBrut.ToString("0.00", fr);

        var showSubtotal2 = _quoteMode == QuoteMode.Standard && discountRate2 > 0.0000000001;
        if (Subtotal2RowDef != null) Subtotal2RowDef.Height = showSubtotal2 ? new GridLength(26) : new GridLength(0);
        if (Subtotal2AmountTextBlock != null) Subtotal2AmountTextBlock.Text = afterDiscount1.ToString("0.00", fr);
    }

    // ✅ Forfait TTC : mutuelle exclusivité avec le devis détaillé (20.07.2026, demande de Joe).
    // ✅ Fix (04.08.2026, demande de Joe : "hide don't delete" au changement de position) :
    // exige aussi _quoteMode == Pdf, sinon un montant TTC resté dans le champ (masqué, pas
    // effacé) continuerait à piloter le calcul des totaux même en position "Devis standard".
    private bool IsForfaitTtcUsedFromUi()
        => _quoteMode == QuoteMode.Pdf && Math.Abs(ParseDouble(ForfaitTtcTextBox?.Text, 0)) > 0.0000000001;

    // ✅ Pop-up d'avertissement au clic sans pdf joint (20.07.2026, 3e demande de Joe) : le champ
    // reste cliquable (voir ApplyFieldPermissions), mais on retire aussitôt le focus si aucun pdf
    // n'a encore été chargé, pour empêcher toute saisie.
    private void ForfaitTtcTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var hasPdf = _workOrder?.ForfaitPdfFileBytes != null && _workOrder.ForfaitPdfFileBytes.Length > 0;
        if (hasPdf) return;

        System.Windows.MessageBox.Show(
            this,
            "Charge d'abord le pdf du devis forfaitaire (bouton \"Ajouter PDF\") avant de saisir un montant.",
            "Forfait : Montant TTC",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        Keyboard.ClearFocus();
    }

    private static double ParseDouble(string? value, double def = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return def;
        value = value.Trim().Replace("’", "").Replace("'", "").Replace(',', '.');
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : def;
    }

    private static string FormatInputNumber(double value)
        => Math.Abs(value) < 0.0000000001 ? "" : value.ToString("G17", CultureInfo.InvariantCulture);

    private static string FormatMoney2DecOrEmpty(double value)
        => Math.Abs(value) < 0.0000000001 ? "" : value.ToString("0.00", CultureInfo.InvariantCulture);

    private void Money2Decimals_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        var v = ParseDouble(tb.Text);
        // ✅ fr-FR (23.07.2026, demande de Joe) : cohérent avec les colonnes Total/HT/TTC.
        tb.Text = Math.Abs(v) < 0.0000000001 ? "" : v.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
    }

    private void ClearSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureInkCanvas.Strokes.Clear();
        SignatureInkCanvas.Background = MediaBrushes.White;
        try { SignaturePreviewImage.Source = null; } catch { }

        _existingSignaturePng = null;
        _signatureCleared = true;
    }

    private void ImportSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer une signature",
            Filter = "PNG|*.png|Images|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _existingSignaturePng = bytes;
            _signatureCleared = false;
            LoadSignaturePreview(bytes);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Impossible d’importer la signature.\n\n{ex.Message}",
                "Signature",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadSignaturePreview(byte[]? png)
    {
        SignatureInkCanvas.Strokes.Clear();
        SignatureInkCanvas.Background = MediaBrushes.White;
        try { SignaturePreviewImage.Source = null; } catch { }

        if (png == null || png.Length == 0) return;

        try
        {
            // ✅ Recadre à l'affichage (22.07.2026, demande de Joe) : corrige immédiatement,
            // sans attendre un nouvel enregistrement, l'aperçu des bons déjà signés AVANT le
            // correctif de CaptureSignaturePng (PNG stocké = canvas entier, non recadré).
            png = PdfService.CropSignatureToContent(png);

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(png);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            // ✅ On affiche l'image dans SignaturePreviewImage (Stretch=Uniform,
            // StretchDirection=DownOnly dans le XAML) plutôt que comme
            // ImageBrush en fond du InkCanvas : un ImageBrush agrandit TOUJOURS
            // l'image pour remplir le cadre, ce qui "zoomait" une signature
            // recadrée serrée (cropToContent côté web). DownOnly réduit l'image
            // si elle est trop grande, mais ne l'agrandit jamais au-delà de sa
            // taille réelle : elle garde ainsi son échelle d'origine, centrée.
            SignatureInkCanvas.Background = MediaBrushes.Transparent;
            SignaturePreviewImage.Source = bmp;
        }
        catch
        {
            SignatureInkCanvas.Background = MediaBrushes.White;
            try { SignaturePreviewImage.Source = null; } catch { }
        }
    }

    private byte[]? CaptureSignaturePng()
    {
        if (SignatureInkCanvas.Strokes.Count == 0)
        {
            var existing = _signatureCleared ? null : _existingSignaturePng;
            // ✅ Recadre aussi une signature déjà existante (importée, ou capturée avant ce
            // correctif) : idempotent si déjà recadrée, corrige les anciennes non recadrées.
            return existing != null && existing.Length > 0
                ? PdfService.CropSignatureToContent(existing)
                : existing;
        }

        // ✅ L'architecte/signataire a tracé de nouveaux traits : on capture sur un
        // fond blanc opaque (et non transparent, qui peut rester actif après
        // l'affichage d'un aperçu via SignaturePreviewImage) pour garder un PNG
        // cohérent avec celui généré côté web.
        SignatureInkCanvas.Background = MediaBrushes.White;

        var width = Math.Max(1, (int)SignatureInkCanvas.ActualWidth);
        var height = Math.Max(1, (int)SignatureInkCanvas.ActualHeight);

        SignatureInkCanvas.Measure(new System.Windows.Size(width, height));
        SignatureInkCanvas.Arrange(new System.Windows.Rect(new System.Windows.Size(width, height)));
        SignatureInkCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(SignatureInkCanvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);

        // ✅ Recadrage sur le tracé réel (22.07.2026, demande de Joe) : le PNG stocké était
        // jusqu'ici le canvas entier (fond blanc inclus), recadré seulement au moment de
        // générer le PDF (PdfService.CropSignatureToContent). Résultat : en rouvrant le bon,
        // l'aperçu WPF (qui affichait le PNG stocké tel quel) montrait la signature à une
        // taille/position différente du PDF. On recadre maintenant dès la capture, pour que
        // le PNG stocké soit partout le même (aperçu WPF, PDF, sync serveur).
        return PdfService.CropSignatureToContent(ms.ToArray());
    }

    private enum StageRank
    {
        None = 0,
        InCreation = 1,
        SentToCompany = 2,
        QuoteReceived = 3,
        SentToSigner = 4,
        Validated = 5
    }

    private static StageRank GetStageRank(WorkOrder wo)
    {
        if (wo.IsValidated) return StageRank.Validated;
        if (wo.IsSentToSigner) return StageRank.SentToSigner;
        if (wo.IsQuoteReceived) return StageRank.QuoteReceived;
        if (wo.IsSentToCompany) return StageRank.SentToCompany;
        if (wo.IsInCreation) return StageRank.InCreation;
        return StageRank.None;
    }

    private static bool HasAnyQuoteData(WorkOrder wo)
    {
        if (!string.IsNullOrWhiteSpace(wo.QuoteName)) return true;
        if (!string.IsNullOrWhiteSpace(wo.QuoteNotes)) return true;
        if (Math.Abs(wo.LaborHours) > 0.0000000001) return true;
        if (Math.Abs(wo.LaborRate) > 0.0000000001) return true;
        if (Math.Abs(wo.TravelQty) > 0.0000000001) return true;
        if (Math.Abs(wo.TravelRate) > 0.0000000001) return true;
        if (Math.Abs(wo.DiscountRate) > 0.0000000001) return true;
        if (Math.Abs(wo.DiscountRate2) > 0.0000000001) return true;
        if (Math.Abs(wo.ForfaitQty * wo.ForfaitUnitPrice) > 0.0000000001) return true;
        if (Math.Abs(wo.ForfaitTtc) > 0.0000000001) return true;
        return false;
    }

    private StageRank ComputeDesiredStageAfterSave()
    {
        if (_workOrder == null) return StageRank.None;

        if (_workOrder.HasFullSignature)
            return StageRank.Validated;

        if (HasAnyQuoteData(_workOrder) || _lines.Any(l => !string.IsNullOrWhiteSpace(l.Label)))
            return StageRank.QuoteReceived;

        return StageRank.InCreation;
    }

    private void ApplyStageIfAdvancing(long workOrderId, StageRank desired)
    {
        var fresh = Db.GetWorkOrderById(workOrderId);
        if (fresh == null) return;

        var current = GetStageRank(fresh);
        if (desired <= current) return;

        switch (desired)
        {
            case StageRank.InCreation:
                Db.SetStageInCreation(workOrderId);
                break;
            case StageRank.SentToCompany:
                Db.SetStageSentToCompany(workOrderId);
                break;
            case StageRank.QuoteReceived:
                Db.SetStageQuoteReceived(workOrderId);
                break;
            case StageRank.SentToSigner:
                Db.SetStageSentToSigner(workOrderId);
                break;
            case StageRank.Validated:
                Db.SetStageValidated(workOrderId);
                break;
        }

        _workOrder = Db.GetWorkOrderById(workOrderId) ?? _workOrder;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                Keyboard.ClearFocus();
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            var pdfWasAvailable = _pdfAvailableForExternal;

            var saved = SaveWorkOrder();
            if (!saved) return;

            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Bon invalide (Id manquant après enregistrement).");

            var desired = ComputeDesiredStageAfterSave();
            ApplyStageIfAdvancing(_workOrder.Id, desired);

            // ✅ Re-verrouillage après enregistrement (23.07.2026, demande de Joe) : évite une
            // manœuvre accidentelle juste après avoir sauvegardé ; il faut recliquer "Modifier".
            _formLocked = true;
            ApplyMode();

            if (_mode == WorkOrderEditMode.EntrepriseDevis && !pdfWasAvailable && _pdfAvailableForExternal)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "Devis enregistré. Vous pouvez maintenant générer le PDF.",
                    "PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            System.Windows.MessageBox.Show(
                this,
                "Bon enregistré.",
                "Enregistrement",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’enregistrer le bon d'intervention.\n\n{ex.Message}",
                "Enregistrement",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool SaveWorkOrder()
    {
        if (_workOrder == null)
            _workOrder = CreateDefaultWorkOrder();

        _workOrder.BdrNumber = int.TryParse(BdrNumberTextBox.Text, out var n) ? n : _workOrder.BdrNumber;

        var reserve = (ReserveComboBox.Text ?? "").Trim();
        if (reserve.Length > ReserveMaxLength)
            reserve = reserve.Substring(0, ReserveMaxLength);

        _workOrder.Reserve = reserve;
        _workOrder.RequestedBy = (RequestedByComboBox.Text ?? "").Trim();
        _workOrder.PerformedBy = (PerformedByComboBox.Text ?? "").Trim();
        _workOrder.Place = (PlaceComboBox.Text ?? "").Trim();
        _workOrder.Etage = (EtageComboBox.Text ?? "").Trim();

        _workOrder.RequestDate = RequestDatePicker.SelectedDate ?? DateTime.Today;
        _workOrder.DeadlineDate = DeadlineDatePicker.SelectedDate ?? default;

        _workOrder.Description = EnforceDescriptionRules(DescriptionTextBox.Text ?? "");

        _workOrder.QuoteName = (QuoteNameTextBox.Text ?? "").Trim();
        _workOrder.QuoteDate = QuoteDatePicker.SelectedDate ?? DateTime.Today;

        // ✅ Exclusivité DS/DF à l'enregistrement (05.08.2026, demande de Joe : "c'est ou l'un ou
        // l'autre") : le va-et-vient entre positions pendant l'édition reste réversible (rien
        // n'est perdu tant qu'on n'enregistre pas, voir TrySwitchQuoteMode/ApplyQuoteModeUi),
        // mais au moment d'Enregistrer, seules les données de la position _quoteMode active sont
        // conservées -- celles de l'autre position sont réellement effacées (pas juste masquées),
        // pour empêcher les 2 jeux de données de coexister en base.
        if (_quoteMode == QuoteMode.Pdf)
        {
            _workOrder.LaborHours = 0;
            _workOrder.LaborRate = 0;
            _workOrder.TravelQty = 0;
            _workOrder.TravelRate = 0;
            _workOrder.DiscountRate = 0;
            _workOrder.DiscountRate2 = 0;
            _workOrder.DiscountName = "";
            _workOrder.DiscountName2 = "";
            _workOrder.ForfaitQty = 0;
            _workOrder.ForfaitUnitPrice = 0;

            if (LaborHoursTextBox != null) LaborHoursTextBox.Text = "";
            if (LaborRateTextBox != null) LaborRateTextBox.Text = "";
            if (TravelQtyTextBox != null) TravelQtyTextBox.Text = "";
            if (TravelRateTextBox != null) TravelRateTextBox.Text = "";
            if (DiscountRateTextBox != null) DiscountRateTextBox.Text = "";
            if (DiscountRateTextBox2 != null) DiscountRateTextBox2.Text = "";
            if (DiscountNameTextBox != null) DiscountNameTextBox.Text = "";
            if (DiscountName2TextBox != null) DiscountName2TextBox.Text = "";

            _workOrder.ForfaitTtc = ParseDouble(ForfaitTtcTextBox.Text, 0);
            _workOrder.ForfaitQuoteNumber = (ForfaitQuoteNumberTextBox.Text ?? "").Trim();
        }
        else
        {
            _workOrder.ForfaitTtc = 0;
            _workOrder.ForfaitQuoteNumber = "";
            _workOrder.ForfaitPdfFileBytes = null;
            _workOrder.ForfaitPdfFileName = "";
            if (ForfaitTtcTextBox != null) ForfaitTtcTextBox.Text = "";
            if (ForfaitQuoteNumberTextBox != null) ForfaitQuoteNumberTextBox.Text = "";

            if (_quoteMode == QuoteMode.Standard)
            {
                _workOrder.LaborHours = ParseDouble(LaborHoursTextBox.Text);
                _workOrder.LaborRate = ParseDouble(LaborRateTextBox.Text);
                _workOrder.TravelQty = ParseDouble(TravelQtyTextBox.Text);
                _workOrder.TravelRate = ParseDouble(TravelRateTextBox.Text);

                _workOrder.DiscountRate = ParseDouble(DiscountRateTextBox.Text, 0);
                if (_workOrder.DiscountRate < 0) _workOrder.DiscountRate = 0;

                _workOrder.DiscountRate2 = ParseDouble(DiscountRateTextBox2.Text, 0);
                if (_workOrder.DiscountRate2 < 0) _workOrder.DiscountRate2 = 0;

                _workOrder.DiscountName = (DiscountNameTextBox.Text ?? "").Trim();
                _workOrder.DiscountName2 = (DiscountName2TextBox.Text ?? "").Trim();

                // ✅ Mémorisé comme nouveau défaut pour le prochain nouveau bon (18.08.2026,
                // demande de Joe : "l'utilisateur puisse y mettre les mots qu'il veut par
                // défaut") : il suffit de taper le texte voulu et d'enregistrer, sans réglage
                // séparé.
                Db.SetDefaultDiscountName(_workOrder.DiscountName);
                Db.SetDefaultDiscountName2(_workOrder.DiscountName2);
            }
            else
            {
                // QuoteMode.None : aucune position choisie, rien à conserver côté devis.
                _workOrder.LaborHours = 0;
                _workOrder.LaborRate = 0;
                _workOrder.TravelQty = 0;
                _workOrder.TravelRate = 0;
                _workOrder.DiscountRate = 0;
                _workOrder.DiscountRate2 = 0;
                _workOrder.DiscountName = "";
                _workOrder.DiscountName2 = "";
            }
        }

        _workOrder.TvaRate = ParseDouble(TvaRateTextBox.Text, 8.1);

        _workOrder.QuoteNotes = EnforceQuoteNotesRules(QuoteNotesTextBox.Text ?? "");

        if (!EnsureQuoteRequiredFieldsOrWarn())
            return false;

        if (!EnsureValidationIsNotPartialOrWarn())
            return false;

        _workOrder.SignatureName = (SignatureNameComboBox.Text ?? "").Trim();
        _workOrder.SignatureDate = SignatureDatePicker.SelectedDate;
        _workOrder.SignaturePng = CaptureSignaturePng();
        _workOrder.ValidationDecision = GetSelectedValidationDecision();

        if (_isCreateMode || _workOrder.Id <= 0)
        {
            if (!_workOrder.ProjectId.HasValue || _workOrder.ProjectId.Value <= 0)
                _workOrder.ProjectId = Db.GetCurrentProjectId();

            Db.InsertWorkOrder(_workOrder);
            _isCreateMode = false;
        }
        else
        {
            Db.UpdateWorkOrderHeader(_workOrder);
            Db.UpdateWorkOrderQuote(_workOrder);
            Db.UpdateWorkOrderSignatureRaw(_workOrder);
        }

        if (_workOrder.Id > 0)
            Db.UpdateWorkOrderValidationDecision(_workOrder.Id, _workOrder.ValidationDecision);

        // ✅ position "Devis PDF" => on supprime toutes les lignes matériel en base (exclusivité
        // DS/DF à l'enregistrement, voir plus haut).
        if (_workOrder.Id > 0)
        {
            foreach (var l in _lines)
                l.RecomputeLineTotal();

            TrimTrailingEmptyLinesToMax();

            if (_quoteMode == QuoteMode.Pdf)
            {
                _lines.Clear();
                _lines.Add(new WorkOrderLine());
            }

            Db.ReplaceWorkOrderLines(_workOrder.Id, _lines.ToList());
            _deletedLineIds.Clear();
        }

        if (_mode == WorkOrderEditMode.EntrepriseDevis)
            _pdfAvailableForExternal = true;

        UpdateCompanyPdfButtons();

        return true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    // ✅ Avertissement à la fermeture (23.07.2026, demande de Joe) : condition simple et fiable
    // basée sur le verrouillage (déverrouillé via "Modifier" = potentiellement modifié, reverrouillé
    // automatiquement après un enregistrement réussi) plutôt qu'un suivi événementiel par champ
    // (abandonné plus tôt à cause de faux positifs sur des événements différés WPF).
    private void WorkOrderWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_formLocked) return;

        if (_unlockedSnapshot != null && _unlockedSnapshot == BuildFormSnapshot())
            return;

        var result = System.Windows.MessageBox.Show(
            this,
            "Voulez-vous enregistrer les modifications avant de fermer?",
            "Bon d'intervention",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.Yes)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            if (!_formLocked)
                e.Cancel = true;
        }
    }

    // ✅ Envoi de lien (Devis/Validation) après modification non enregistrée (23.07.2026, demande
    // de Joe) : ces boutons enregistraient déjà silencieusement (SaveWorkOrder()) avant d'envoyer
    // le lien, ce qui persistait une manœuvre accidentelle sans prévenir. Même instantané que
    // WorkOrderWindow_Closing pour détecter s'il y a vraiment quelque chose à enregistrer.
    private bool ConfirmSaveBeforeSendingLink()
    {
        if (_formLocked) return true;
        if (_unlockedSnapshot != null && _unlockedSnapshot == BuildFormSnapshot()) return true;

        var result = System.Windows.MessageBox.Show(
            this,
            "Voulez-vous enregistrer les modifications avant d'envoyer le lien?",
            "Bon d'intervention",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private static string NormalizeServerBaseUrl(string? baseUrl)
    {
        var v = (baseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) v = "https://iziregi.com";
        return v.TrimEnd('/');
    }

    private static string ExtractFirstHref(string html)
    {
        html ??= "";
        var m = Regex.Match(html, "href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value ?? "") : "";
    }
    private sealed class WorkOrderUpsertDto
    {
        public string WorkOrderRef { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string ProjectAddressLine { get; set; } = "";
        public string ProjectZipCity { get; set; } = "";

        public string Place { get; set; } = "";
        public string Etage { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public string PerformedBy { get; set; } = "";
        public string RequestDate { get; set; } = "";
        public string DeadlineDate { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private static string IsoDate(System.DateTime dt)
        => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task PublishWorkOrderToServerAsync(string workOrderRef)
    {
        if (_workOrder == null || _workOrder.Id <= 0)
            throw new InvalidOperationException("Le bon doit être enregistré avant publication serveur.");

        workOrderRef = (workOrderRef ?? "").Trim();
        if (string.IsNullOrWhiteSpace(workOrderRef))
            throw new InvalidOperationException("Référence du bon invalide.");

        Project? project = null;
        try
        {
            if (_workOrder.ProjectId.HasValue && _workOrder.ProjectId.Value > 0)
                project = Db.GetProjectById(_workOrder.ProjectId.Value);
        }
        catch { }

        var dto = new WorkOrderUpsertDto
        {
            WorkOrderRef = workOrderRef,
            ProjectName = project?.Name ?? "",
            ProjectAddressLine = project?.AddressLine ?? "",
            ProjectZipCity = project?.ZipCity ?? "",

            Place = _workOrder.Place ?? "",
            Etage = _workOrder.Etage ?? "",
            RequestedBy = _workOrder.RequestedBy ?? "",
            PerformedBy = _workOrder.PerformedBy ?? "",
            RequestDate = IsoDate(_workOrder.RequestDate),
            DeadlineDate = IsoDate(_workOrder.DeadlineDate),
            Description = _workOrder.Description ?? "",
        };

        var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
        var url = $"{baseUrl}/internal/workorders/upsert";

        var json = JsonSerializer.Serialize(dto, ReplyJsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await PostWithApiKeyAsync(Http, url, content);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Publication serveur impossible : HTTP {(int)resp.StatusCode}.");
    }

    private async Task PublishWorkOrderToServerAsync(WorkOrder wo)
    {
        var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);
        var url = $"{baseUrl}/internal/workorders/upsert";

        // Récupère le projet pour nom + adresse
        string projectName = "";
        string projectAddressLine = "";
        string projectZipCity = "";
        string managerName = "";
        string managerContact = "";

        if (wo.ProjectId.HasValue && wo.ProjectId.Value > 0)
        {
            try
            {
                var proj = Db.GetProjectById(wo.ProjectId.Value);
                if (proj != null)
                {
                    projectName = proj.Name ?? "";
                    var raw = (proj.Address ?? "").Trim();
                    var comma = raw.LastIndexOf(',');
                    if (comma >= 0 && comma < raw.Length - 1)
                    {
                        projectAddressLine = raw.Substring(0, comma).Trim();
                        projectZipCity     = raw.Substring(comma + 1).Trim();
                    }
                    else
                    {
                        projectAddressLine = raw;
                    }

                    managerName = proj.ManagerName ?? "";
                    managerContact = proj.ManagerContact ?? "";
                }
            }
            catch { }
        }

        var payload = new
        {
            workOrderRef     = wo.BdrDisplay,
            projectName,
            projectAddressLine,
            projectZipCity,
            managerName,
            managerContact,
            place            = wo.Place ?? "",
            etage            = wo.Etage ?? "",
            requestedBy      = wo.RequestedBy ?? "",
            performedBy      = wo.PerformedBy ?? "",
            requestDate      = wo.RequestDate.ToString("yyyy-MM-dd"),
            deadlineDate     = wo.DeadlineDate.ToString("yyyy-MM-dd"),
            description      = wo.Description ?? "",
            // ✅ Champ "Concerne" (Reserve) : doit être publié pour apparaître côté entreprise/serveur
            reserve          = wo.Reserve ?? ""
        };

        var json    = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp    = await PostWithApiKeyAsync(Http, url, content);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Publication serveur impossible : HTTP {(int)resp.StatusCode}.");
    }

    private async Task<string> CreateMagicLinkAsync(string role, string workOrderRef)
    {
        role = (role ?? "").Trim().ToLowerInvariant();
        workOrderRef = (workOrderRef ?? "").Trim();

        if (string.IsNullOrWhiteSpace(workOrderRef))
            throw new InvalidOperationException("Référence du bon invalide.");

        var baseUrl = NormalizeServerBaseUrl(ServerBaseUrl);

        // ✅ même endpoint que celui utilisé sur le VPS (admin)
        var url =
            $"{baseUrl}/internal/create-link?role={Uri.EscapeDataString(role)}&workOrderRef={Uri.EscapeDataString(workOrderRef)}";

        using var resp = await PostWithApiKeyAsync(Http, url, null);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Serveur : HTTP {(int)resp.StatusCode}.");

        var html = await resp.Content.ReadAsStringAsync();
        var href = ExtractFirstHref(html);
        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidOperationException("Lien introuvable dans la réponse du serveur.");

        // href est du style "/company?..." ou "http://127.0.0.1:5000/company?..."
        if (href.StartsWith("/", StringComparison.Ordinal))
            return baseUrl + href;

        return href;
    }

    // ✅ 11.08.2026 (demande de Joe) : édite le modèle par défaut de texte avant/après le lien
    // devis (LinkTextWindow), enregistré immédiatement (Db.SetQuoteLinkTextBefore/After) --
    // réutilisé automatiquement au prochain "Envoyer lien pour devis" (celui-ci ou un futur BI).
    private void QuoteLinkTextButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new LinkTextWindow("Texte du lien devis", Db.GetQuoteLinkTextBefore(), Db.GetQuoteLinkTextAfter())
        {
            Owner = this
        };

        if (win.ShowDialog() == true)
        {
            Db.SetQuoteLinkTextBefore(win.BeforeText);
            Db.SetQuoteLinkTextAfter(win.AfterText);
        }
    }

    private void SignatureLinkTextButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new LinkTextWindow("Texte du lien validation", Db.GetSignatureLinkTextBefore(), Db.GetSignatureLinkTextAfter())
        {
            Owner = this
        };

        if (win.ShowDialog() == true)
        {
            Db.SetSignatureLinkTextBefore(win.BeforeText);
            Db.SetSignatureLinkTextAfter(win.AfterText);
        }
    }

    private async void ExportQuoteRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveBeforeSendingLink()) return;

        try
        {
            try
            {
                Keyboard.ClearFocus();
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            var saved = SaveWorkOrder();
            if (!saved) return;

            _formLocked = true;
            ApplyMode();

            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Le bon doit être enregistré avant génération du lien.");

            var workOrderRef = (_workOrder.BdrDisplay ?? "").Trim(); // ex: "24-P1"

            // ✅ Publie d'abord les données du bon sur le serveur
            await PublishWorkOrderToServerAsync(_workOrder);

            var link = await CreateMagicLinkAsync(role: "company", workOrderRef: workOrderRef);

            // ✅ Texte avant/après personnalisable (demande de Joe, 11.08.2026, bouton "+ texte") :
            // remplace l'ancien texte codé en dur, voir Db.GetQuoteLinkTextBefore/After.
            var before = Db.GetQuoteLinkTextBefore().Replace("{ref}", workOrderRef);
            var after = Db.GetQuoteLinkTextAfter().Replace("{ref}", workOrderRef);
            var plainBody = $"{before}\r\n\r\n{link}\r\n\r\n{after}";

            // ✅ Fix (demande de Joe, 11.08.2026, "les textes d'accompagnement ne s'inscrivent
            // pas") : le presse-papier ne contenait que le lien nu -- si l'ouverture automatique
            // du mail (ci-dessous) échoue silencieusement (pas de client mail par défaut
            // configuré), coller depuis le presse-papier ne donnait donc jamais le texte
            // avant/après. Le presse-papier contient maintenant le message complet.
            System.Windows.Clipboard.SetText(plainBody);

            // ✅ Option B : ouvre un nouveau mail (destinataire vide) avec le lien
            try
            {
                var subject = Uri.EscapeDataString($"Iziregi — Bon {workOrderRef} — Devis à compléter");
                var body = Uri.EscapeDataString(plainBody);
                var mailto = $"mailto:?subject={subject}&body={body}";
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            }
            catch { }

            Db.SetStageSentToCompany(_workOrder.Id);
            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            System.Windows.MessageBox.Show(
                this,
                "Lien pour devis copié (mail ouvert).",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de générer le lien pour devis.\n\n{ex.Message}",
                "Devis",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportSignatureRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveBeforeSendingLink()) return;

        try
        {
            try
            {
                Keyboard.ClearFocus();
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            var saved = SaveWorkOrder();
            if (!saved) return;

            _formLocked = true;
            ApplyMode();

            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Le bon doit être enregistré avant génération du lien.");

            var workOrderRef = (_workOrder.BdrDisplay ?? "").Trim(); // ex: "24-P1"

            // ✅ Publie d'abord les données du bon sur le serveur (sinon le bon
            // peut être absent côté serveur si le lien "devis" n'a jamais été généré)
            await PublishWorkOrderToServerAsync(_workOrder);

            var link = await CreateMagicLinkAsync(role: "signer", workOrderRef: workOrderRef);

            // ✅ Texte avant/après personnalisable (demande de Joe, 11.08.2026, bouton "+ texte") :
            // remplace l'ancien texte codé en dur, voir Db.GetSignatureLinkTextBefore/After.
            var before = Db.GetSignatureLinkTextBefore().Replace("{ref}", workOrderRef);
            var after = Db.GetSignatureLinkTextAfter().Replace("{ref}", workOrderRef);
            var plainBody = $"{before}\r\n\r\n{link}\r\n\r\n{after}";

            // ✅ Fix (demande de Joe, 11.08.2026, "les textes d'accompagnement ne s'inscrivent
            // pas") : voir le même correctif sur ExportQuoteRequestButton_Click -- le
            // presse-papier contient maintenant le message complet, pas juste le lien nu.
            System.Windows.Clipboard.SetText(plainBody);

            // ✅ Option B : ouvre un nouveau mail (destinataire vide) avec le lien
            try
            {
                var subject = Uri.EscapeDataString($"Iziregi — Bon {workOrderRef} — Validation / signature");
                var body = Uri.EscapeDataString(plainBody);
                var mailto = $"mailto:?subject={subject}&body={body}";
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            }
            catch { }

            Db.SetStageSentToSigner(_workOrder.Id);
            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            System.Windows.MessageBox.Show(
                this,
                "Lien pour validation copié (mail ouvert).",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de générer le lien pour validation.\n\n{ex.Message}",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static async Task WaitForFileReadyAsync(string filePath, int attempts = 25, int delayMs = 150)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Chemin PDF invalide.", nameof(filePath));

        Exception? last = null;

        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Fichier PDF introuvable.", filePath);

                var fi = new FileInfo(filePath);
                if (fi.Length <= 0)
                    throw new IOException("PDF vide (taille = 0).");

                using (var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                }

                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(delayMs);
            }
        }

        throw new IOException("Le PDF n'est pas prêt à l'ouverture (encore verrouillé ou incomplet).", last);
    }

    private async void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                Keyboard.ClearFocus();
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            var saved = SaveWorkOrder();
            if (!saved) return;

            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Le bon doit être enregistré avant export PDF.");

            var lines = Db.GetWorkOrderLines(_workOrder.Id) ?? new List<WorkOrderLine>();

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Enregistrer le PDF (bon d'intervention)",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"BDR-{_workOrder.BdrDisplay}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true
            };

            if (dlg.ShowDialog() != true)
                return;

            PdfService.GenerateWorkOrderPdf(dlg.FileName, _workOrder, lines);
            await WaitForFileReadyAsync(dlg.FileName, attempts: 25, delayMs: 150);

            // ✅ Ouvre automatiquement le PDF
            try
            {
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch { }

            System.Windows.MessageBox.Show(
                this,
                "PDF généré.",
                "PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de générer le PDF.\n\n{ex.Message}",
                "PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CompanyPdfUploadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // ✅ Fix (04.08.2026, demande de Joe : "hide don't delete") : l'exclusivité
            // standard/pdf est désormais assurée par _quoteMode (ce bouton n'est visible qu'en
            // position "Devis PDF", voir QuotePdfSection) -- d'anciennes données standard
            // masquées ne doivent plus bloquer silencieusement l'ajout d'un pdf.
            if (_workOrder == null)
                _workOrder = CreateDefaultWorkOrder();

            if (_workOrder.Id <= 0)
            {
                var saved = SaveWorkOrder();
                if (!saved) return;
            }

            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Le bon doit être enregistré avant d’ajouter un PDF.");

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ajouter un PDF (devis forfaitaire)",
                Filter = "PDF (*.pdf)|*.pdf",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true)
                return;

            var fi = new FileInfo(dlg.FileName);
            if (fi.Exists && fi.Length > 15 * 1024 * 1024)
                throw new InvalidOperationException("PDF trop volumineux (max 15 Mo).");

            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("PDF vide.");

            _workOrder.ForfaitPdfFileName = Path.GetFileName(dlg.FileName);
            _workOrder.ForfaitPdfFileBytes = bytes;

            Db.UpdateWorkOrderForfaitPdf(_workOrder.Id, _workOrder.ForfaitPdfFileName, _workOrder.ForfaitPdfFileBytes);

            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            // ✅ ApplyFieldPermissions (au lieu de UpdateCompanyPdfButtons seul, 20.07.2026,
            // demande de Joe) : deverrouille aussi Forfait TTC, qui exige desormais un pdf joint.
            ApplyFieldPermissions();

            System.Windows.MessageBox.Show(
                this,
                "PDF ajouté.",
                "PDF devis forfaitaire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’ajouter le PDF.\n\n{ex.Message}",
                "PDF devis forfaitaire",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CompanyPdfOpenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Bon non enregistré.");

            var bytes = _workOrder.ForfaitPdfFileBytes;
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Aucun PDF.");

            var name = (_workOrder.ForfaitPdfFileName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Devis-Forfait.pdf";

            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                name += ".pdf";

            var dir = Path.Combine(Path.GetTempPath(), "Iziregi");
            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}");

            File.WriteAllBytes(filePath, bytes);

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible d’ouvrir le PDF.\n\n{ex.Message}",
                "PDF devis forfaitaire",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CompanyPdfRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_workOrder == null || _workOrder.Id <= 0)
                throw new InvalidOperationException("Bon non enregistré.");

            _workOrder.ForfaitPdfFileName = "";
            _workOrder.ForfaitPdfFileBytes = null;

            Db.UpdateWorkOrderForfaitPdf(_workOrder.Id, "", null);

            _workOrder = Db.GetWorkOrderById(_workOrder.Id) ?? _workOrder;

            // ✅ Le montant Forfait TTC est effacé quand le pdf est supprimé (20.07.2026, 4e
            // demande de Joe) : un montant forfait sans le pdf qui le justifie n'a plus de sens.
            // ✅ Même règle pour le N° du devis (04.08.2026, demande de Joe).
            if (ForfaitTtcTextBox != null) ForfaitTtcTextBox.Text = "";
            _workOrder.ForfaitTtc = 0;
            if (ForfaitQuoteNumberTextBox != null) ForfaitQuoteNumberTextBox.Text = "";
            _workOrder.ForfaitQuoteNumber = "";

            // ✅ ApplyFieldPermissions (au lieu de UpdateCompanyPdfButtons seul) : recalcule aussi
            // l'état du champ Forfait TTC.
            ApplyFieldPermissions();
            RecomputeTotals();

            System.Windows.MessageBox.Show(
                this,
                "PDF supprimé.",
                "PDF devis forfaitaire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Impossible de supprimer le PDF.\n\n{ex.Message}",
                "PDF devis forfaitaire",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}