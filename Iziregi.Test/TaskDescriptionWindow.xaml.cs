// File: TaskDescriptionWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

using Iziregi.Test.Data;
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

    // ✅ Descriptif enrichi (25.07.2026, demande de Joe) : mise en forme + images
    // éventuelles, sérialisées en XAML (même mécanisme que TextZones[i].DocumentXaml dans
    // PlanningPage). ResultText reste le texte brut (aperçu 1 ligne dans la grille,
    // recherche/tri) ; ResultDocumentXaml n'est utilisé que par cette fenêtre.
    public string ResultDocumentXaml { get; private set; } = "";

    // ✅ NOUVEAU (20.08.2026, demande de Joe) : même bascule que la case à cocher de la
    // cellule Descriptif dans la grille (TaskRow.IncludeInTaskDetails), exposée ici aussi
    // ("dans le descriptif lui-même"). Lue par PlanningPage.TaskExpandDescriptionButton_Click
    // au retour de la fenêtre.
    public bool ResultIncludeInTaskDetails { get; private set; }

    // ✅ Avertissement si fermeture sans avoir cliqué "Enregistrer" (demande de Joe,
    // 16.07.2026) : mémorise le texte de départ pour détecter une vraie modification,
    // et évite de redemander une fois que l'utilisateur a confirmé qu'il veut abandonner.
    private string _originalTodo = "";
    private bool _discardConfirmed;
    private bool _isLoadingDocument;
    private bool _hasEdited;

    private bool HasUnsavedChanges => _hasEdited;

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
        string todoDocumentXaml,
        string companyLabel,
        string buildingLabel,
        string floorLabel,
        string categoryLabel,
        string urgentLabel,
        bool showCompany,
        bool showBuilding,
        bool showFloor,
        bool showCategory,
        bool showUrgent,
        bool includeInTaskDetails = false,
        string? companyColorHex = null,
        bool companyIsGradient = false,
        double? descriptifContentWidth = null)
    {
        InitializeComponent();

        IncludeInTaskDetailsCheckBox.IsChecked = includeInTaskDetails;
        ResultIncludeInTaskDetails = includeInTaskDetails;

        // ✅ NOUVEAU (20.08.2026, demande de Joe) : largeur de la zone de texte alignée sur la
        // largeur réelle d'une fiche dans "Détails descriptifs" (voir
        // PlanningPage.ComputeTaskDetailCardWidth), pour que le texte se césure au même endroit
        // à l'écriture qu'à l'affichage dans la nouvelle section ("respecter le nombre de
        // caractères par largeur").
        if (descriptifContentWidth.HasValue)
        {
            DescriptifRichTextBox.Width = descriptifContentWidth.Value;
            DescriptifRichTextBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }

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

        // ✅ NOUVEAU (20.08.2026, demande de Joe) : le nom de l'intervenant est surligné de sa
        // couleur (même couleur que celle de la page Listes / grille des Tâches), voir
        // PlanningPage.TaskExpandDescriptionButton_Click.
        if (!string.IsNullOrWhiteSpace(_company) && !string.IsNullOrWhiteSpace(companyColorHex))
        {
            var bg = Iziregi.Test.Helpers.ColorGradientHelper.BuildBrush(companyColorHex, companyIsGradient);
            if (bg != null)
            {
                CompanyTextBlock.Background = bg;
                CompanyTextBlock.Foreground = GetTextBrushForBackground(bg);
                CompanyTextBlock.Padding = new Thickness(4, 1, 4, 2);
            }
        }
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

        _isLoadingDocument = true;
        var doc = DeserializeFlowDocumentFromXaml(todoDocumentXaml) ?? MakeFlowDocumentFromPlainText(_originalTodo);
        DescriptifRichTextBox.Document = doc;
        RewireResizableInlineImages(doc);
        _isLoadingDocument = false;

        DescriptifRichTextBox.Focus();
        DescriptifRichTextBox.CaretPosition = DescriptifRichTextBox.Document.ContentEnd;
        UpdateResizeImageHintVisibility();
    }

    // ✅ Astuce Ctrl+molette (26.07.2026, demande de Joe) : affichée uniquement s'il y a
    // effectivement une image dans le document, pas en permanence.
    private void UpdateResizeImageHintVisibility()
    {
        ResizeImageHintBorder.Visibility = DocumentHasImage(DescriptifRichTextBox.Document)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool DocumentHasImage(FlowDocument doc) => FindFirstInlineUIContainer(doc.Blocks) != null;

    private static InlineUIContainer? FindFirstInlineUIContainer(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph p)
            {
                var found = FindFirstInlineUIContainerInInlines(p.Inlines);
                if (found != null) return found;
            }
            else if (block is Section sec)
            {
                var found = FindFirstInlineUIContainer(sec.Blocks);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static InlineUIContainer? FindFirstInlineUIContainerInInlines(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer iuc) return iuc;
            if (inline is Span span)
            {
                var found = FindFirstInlineUIContainerInInlines(span.Inlines);
                if (found != null) return found;
            }
        }
        return null;
    }

    // ✅ Duplique le même mécanisme que ArchivesPage/TrashPage/AccountingPage (contraste
    // texte/fond selon luminance) : convention déjà établie dans ce projet (chaque page/fenêtre
    // garde ses propres styles locaux plutôt qu'une centralisation à large portée).
    private static bool TryGetSolidColor(System.Windows.Media.Brush brush, out System.Windows.Media.Color color)
    {
        if (brush is SolidColorBrush scb)
        {
            color = scb.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static System.Windows.Media.Brush GetTextBrushForBackground(System.Windows.Media.Brush bg)
    {
        if (!TryGetSolidColor(bg, out var c))
            return System.Windows.Media.Brushes.Black;

        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        return luminance < 0.55 ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
    }

    private static FlowDocument MakeFlowDocumentFromPlainText(string text)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run(text ?? "")));
        return doc;
    }

    private static string GetPlainTextFromDocument(FlowDocument doc)
        => new TextRange(doc.ContentStart, doc.ContentEnd).Text.TrimEnd('\r', '\n');

    private static string SerializeFlowDocumentToXaml(FlowDocument doc)
    {
        try
        {
            using var sw = new StringWriter(CultureInfo.InvariantCulture);
            using var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false });
            XamlWriter.Save(doc, xw);
            xw.Flush();
            return sw.ToString();
        }
        catch { return ""; }
    }

    private static FlowDocument? DeserializeFlowDocumentFromXaml(string? xaml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(xaml)) return null;
            using var sr = new StringReader(xaml);
            using var xr = XmlReader.Create(sr);
            return XamlReader.Load(xr) as FlowDocument;
        }
        catch { return null; }
    }

    private void DescriptifRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDocument) return;
        _hasEdited = true;
        UpdateResizeImageHintVisibility();
    }

    private void DescriptifRichTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        if (TryResizeInlineImageUnderCursor(DescriptifRichTextBox, e.GetPosition(DescriptifRichTextBox), e.Delta))
        {
            _hasEdited = true;
            e.Handled = true;
        }
    }

    private void DescriptifRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (BoldToggle == null || ItalicToggle == null || UnderlineToggle == null) return;

        try
        {
            var sel = DescriptifRichTextBox.Selection;
            var fw = sel.GetPropertyValue(TextElement.FontWeightProperty);
            var fs = sel.GetPropertyValue(TextElement.FontStyleProperty);
            var td = sel.GetPropertyValue(Inline.TextDecorationsProperty);

            BoldToggle.IsChecked = fw != DependencyProperty.UnsetValue && fw is FontWeight w && w == FontWeights.Bold;
            ItalicToggle.IsChecked = fs != DependencyProperty.UnsetValue && fs is System.Windows.FontStyle st && st == FontStyles.Italic;
            UnderlineToggle.IsChecked = td != DependencyProperty.UnsetValue && td is TextDecorationCollection tdc && tdc.Count > 0;
        }
        catch { }
    }

    private void BoldToggle_Click(object sender, RoutedEventArgs e)
    {
        DescriptifRichTextBox.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,
            BoldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
        DescriptifRichTextBox.Focus();
        _hasEdited = true;
    }

    private void ItalicToggle_Click(object sender, RoutedEventArgs e)
    {
        DescriptifRichTextBox.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,
            ItalicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);
        DescriptifRichTextBox.Focus();
        _hasEdited = true;
    }

    private void UnderlineToggle_Click(object sender, RoutedEventArgs e)
    {
        DescriptifRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            UnderlineToggle.IsChecked == true ? TextDecorations.Underline : null);
        DescriptifRichTextBox.Focus();
        _hasEdited = true;
    }

    // ✅ Couleur de police / surlignage (28.07.2026, demande de Joe) : même mécanisme que
    // FontColorButton_Click/FillColorButton_Click dans PlanningPage.xaml.cs (zones de texte).
    private void FontColorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };

            if (FontColorSwatch?.Background is System.Windows.Media.SolidColorBrush b)
                dlg.Color = System.Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var c = System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            FontColorSwatch!.Background = new System.Windows.Media.SolidColorBrush(c);

            DescriptifRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush(c));
            DescriptifRichTextBox.Focus();
            _hasEdited = true;
        }
        catch { }
    }

    private void FillColorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };

            if (FillColorSwatch?.Background is System.Windows.Media.SolidColorBrush b)
                dlg.Color = System.Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var c = System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            FillColorSwatch!.Background = new System.Windows.Media.SolidColorBrush(c);

            DescriptifRichTextBox.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new System.Windows.Media.SolidColorBrush(c));
            DescriptifRichTextBox.Focus();
            _hasEdited = true;
        }
        catch { }
    }

    // ✅ Même mécanisme que CopyImageIntoAppStorage / InsertImageIntoActiveTextZone_Click
    // dans PlanningPage.xaml.cs (copie l'image dans le stockage de l'app, insertion via
    // InlineUIContainer), dupliqué ici car TaskDescriptionWindow est une fenêtre séparée
    // sans accès aux membres privés de PlanningPage.
    private void InsertImageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Insérer une image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (ofd.ShowDialog() != true) return;

            var storedPath = CopyImageIntoAppStorage(ofd.FileName);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(storedPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            new InlineUIContainer(CreateResizableInlineImage(bmp), DescriptifRichTextBox.CaretPosition);
            DescriptifRichTextBox.Focus();
            _hasEdited = true;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, "Impossible d'insérer l'image :\n" + ex.Message, "Iziregi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ============================================================
    // Image redimensionnable (26.07.2026, demande de Joe) : redimensionnement par
    // Ctrl+molette au-dessus de l'image. Deux approches par bouton (poignée glissée, puis
    // boutons cliquables －/＋) ont été essayées avant celle-ci et toutes les deux
    // échouaient en pratique : le RichTextBox capture la souris dès le clic pour gérer sa
    // propre sélection de texte, ce qui empêche tout contrôle enfant (Thumb ou Button) posé
    // sur l'image de recevoir un clic ou un glisser-déposer complet — vérifié par un test
    // avec clic souris simulé (aucune réaction, confirmé par Joe en pratique aussi).
    // Ctrl+molette contourne le problème : c'est un événement écouté directement sur le
    // RichTextBox lui-même (pas sur un enfant), donc pas de conflit de capture souris.
    // Dupliqué dans PlanningPage.xaml.cs (zones de texte) car ce sont deux classes séparées.
    // ============================================================

    private const double InlineImageMinSize = 30;
    private const double InlineImageMaxSize = 600;

    private static FrameworkElement CreateResizableInlineImage(BitmapImage bmp)
    {
        double w = bmp.PixelWidth > 0 ? bmp.PixelWidth : 220;
        double h = bmp.PixelHeight > 0 ? bmp.PixelHeight : 220;
        if (w > 220 || h > 220)
        {
            var scale = 220 / Math.Max(w, h);
            w *= scale; h *= scale;
        }

        var image = new System.Windows.Controls.Image
        {
            Source = bmp,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill,
            ToolTip = "Ctrl + molette pour redimensionner"
        };

        // ✅ Le Grid doit avoir une taille EXPLICITE (identique à l'image) et ne pas
        // s'étirer (HorizontalAlignment/VerticalAlignment = Left/Top) : sinon, inséré dans
        // le flux de texte, il s'étire sur toute la largeur de ligne restante et la
        // hauteur de ligne réservée devient incohérente (image qui semble "coupée").
        var grid = new Grid
        {
            Width = w,
            Height = h,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        grid.Children.Add(image);
        return grid;
    }

    private static bool TryResizeInlineImageUnderCursor(System.Windows.Controls.RichTextBox rtb, System.Windows.Point position, int wheelDelta)
    {
        var hit = VisualTreeHelper.HitTest(rtb, position);
        var image = FindAncestorOrSelf<System.Windows.Controls.Image>(hit?.VisualHit);
        if (image?.Parent is not Grid grid) return false;

        var factor = wheelDelta > 0 ? 1.15 : 0.85;
        var newW = Math.Clamp(image.Width * factor, InlineImageMinSize, InlineImageMaxSize);
        var newH = Math.Clamp(image.Height * factor, InlineImageMinSize, InlineImageMaxSize);
        image.Width = grid.Width = newW;
        image.Height = grid.Height = newH;
        return true;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T match) return match;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static void RewireResizableInlineImages(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
            RewireResizableInlineImagesInBlock(block);
    }

    private static void RewireResizableInlineImagesInBlock(Block block)
    {
        if (block is Paragraph p)
        {
            foreach (var inline in p.Inlines)
                RewireResizableInlineImagesInInline(inline);
        }
        else if (block is Section sec)
        {
            foreach (var b in sec.Blocks)
                RewireResizableInlineImagesInBlock(b);
        }
    }

    private static void RewireResizableInlineImagesInInline(Inline inline)
    {
        if (inline is InlineUIContainer iuc)
        {
            if (iuc.Child is Grid grid)
            {
                var image = grid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (image != null)
                {
                    var stale = grid.Children.OfType<FrameworkElement>().Where(c => !ReferenceEquals(c, image)).ToList();
                    foreach (var s in stale) grid.Children.Remove(s);
                    image.ToolTip = "Ctrl + molette pour redimensionner";
                }
            }
            // ✅ Images insérées AVANT ce mécanisme (image seule, pas encore enveloppée dans
            // le Grid redimensionnable) : mise à niveau automatique vers le nouveau format.
            else if (iuc.Child is System.Windows.Controls.Image oldImage && oldImage.Source is BitmapImage oldBmp)
            {
                iuc.Child = CreateResizableInlineImage(oldBmp);
            }
        }
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                RewireResizableInlineImagesInInline(child);
        }
    }

    // ✅ Compression à l'insertion (26.07.2026, demande de Joe) : une photo de téléphone non
    // retouchée peut faire plusieurs Mo pour un affichage qui ne dépassera jamais ~220px de
    // large dans le texte -> réduit à 1600px de long côté max (largement suffisant à l'écran
    // et à l'impression) avant stockage. Dupliqué dans PlanningPage.xaml.cs (zones de texte).
    private const int RichTextImageMaxDimension = 1600;

    private static string CopyImageIntoAppStorage(string sourcePath)
    {
        var pid = Db.GetCurrentProjectId();
        var pidStr = (pid.HasValue && pid.Value > 0) ? pid.Value.ToString(CultureInfo.InvariantCulture) : "0";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iziregi", "Planning", "Images", pidStr);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var destPath = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");

        try
        {
            var decoder = BitmapDecoder.Create(new Uri(sourcePath, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            if (frame.PixelWidth > RichTextImageMaxDimension || frame.PixelHeight > RichTextImageMaxDimension)
            {
                var scale = (double)RichTextImageMaxDimension / Math.Max(frame.PixelWidth, frame.PixelHeight);
                var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));

                BitmapEncoder encoder = ext.ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 85 },
                    ".gif" => new GifBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(scaled));

                using var fs = new FileStream(destPath, FileMode.Create);
                encoder.Save(fs);
                return destPath;
            }
        }
        catch { /* fallback : copie brute ci-dessous */ }

        File.Copy(sourcePath, destPath, overwrite: false);
        return destPath;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CommitResults();
        Close();
    }

    private void CommitResults()
    {
        ResultText = GetPlainTextFromDocument(DescriptifRichTextBox.Document);
        ResultDocumentXaml = SerializeFlowDocumentToXaml(DescriptifRichTextBox.Document);
        ResultIncludeInTaskDetails = IncludeInTaskDetailsCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void IncludeInTaskDetailsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _hasEdited = true;
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

    // ✅ NOUVEAU (20.08.2026, correctif : "si j'ajoute une image dans le descriptif tâche, elle
    // n'apparaît pas dans le pdf") : GeneratePdfButton_Click passait GetPlainTextFromDocument
    // (texte brut) à PdfService.GenerateTaskDescriptionPdf, qui affichait juste du texte QuestPDF
    // -- toute image insérée était donc silencieusement perdue. Ici, on prend plutôt une capture
    // PNG fidèle du Descriptif (mise en forme + images comprises), rendue hors-écran à partir
    // d'une COPIE du FlowDocument (round-trip Xaml, même mécanisme que ResultDocumentXaml) car un
    // FlowDocument ne peut être hébergé que par un seul RichTextBox à la fois -- utiliser
    // directement DescriptifRichTextBox.Document le détacherait de l'éditeur visible.
    private byte[] CaptureDescriptifSnapshotPng()
    {
        const double snapshotWidth = 640;

        var snapshotDoc = DeserializeFlowDocumentFromXaml(SerializeFlowDocumentToXaml(DescriptifRichTextBox.Document))
            ?? new FlowDocument();

        var snapshotRtb = new System.Windows.Controls.RichTextBox
        {
            Document = snapshotDoc,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(12),
            FontSize = 13,
            Width = snapshotWidth,
        };

        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        snapshotRtb.Resources.Add(typeof(Paragraph), paragraphStyle);

        snapshotRtb.Measure(new System.Windows.Size(snapshotWidth, double.PositiveInfinity));
        var height = Math.Max(1, snapshotRtb.DesiredSize.Height);
        snapshotRtb.Arrange(new Rect(0, 0, snapshotWidth, height));
        snapshotRtb.UpdateLayout();

        var bmp = new RenderTargetBitmap((int)Math.Ceiling(snapshotWidth), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(snapshotRtb);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ✅ Se déclenche pour TOUTE fermeture (Annuler, croix en haut à droite, Alt+F4) —
    // pas seulement le bouton Annuler. On ne prévient PAS si "Enregistrer" a déjà été
    // cliqué (DialogResult == true) ni si le texte n'a en fait pas changé. Même formulation
    // et mêmes boutons (Oui/Non/Annuler) que WorkOrderWindow_Closing (26.07.2026, demande
    // de Joe : cohérence avec le reste de l'app).
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true && HasUnsavedChanges && !_discardConfirmed)
        {
            var result = WpfMessageBox.Show(
                this,
                "Voulez-vous enregistrer les modifications avant de fermer?",
                "Iziregi",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
                CommitResults();

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
                GetPlainTextFromDocument(DescriptifRichTextBox.Document),
                CaptureDescriptifSnapshotPng());

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
