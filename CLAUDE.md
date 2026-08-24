# Iziregi.Test — mémo projet pour Claude Code

Client de bureau Windows (WPF, .NET 8) pour la gestion de "bons de régie" (BDR) par un
architecte. Utilisé conjointement avec le backend `Iziregi.Server` (dépôt séparé,
`C:\Users\HP\source\repos\Iziregi.Server\`).

## Instruction permanente de Joe (à respecter dans TOUTE réponse contenant une commande)

Toujours préciser AVANT une commande à copier-coller si c'est **PowerShell Windows**
(sur ce PC) ou **SSH/Ubuntu** (sur le serveur de production iziregi.com). Ne jamais
donner une commande sans cette précision. Communication en français, tutoiement.

## Où est le code

- `Iziregi.Test\Iziregi.Test\` : code C#/XAML de l'app (le `.csproj` est ici, pas à la racine).
- `Iziregi.Test\Installer\` : installateur Inno Setup (`Iziregi.iss`).
- Deux branches existent sur GitHub (`https://github.com/09h00/Iziregi.Test.git`) :
  `main` (à jour, celle-ci) et `master` (obsolète, ancienne version d'avril — ne pas
  s'y fier si jamais consultée).
- Le dépôt GitHub est une sauvegarde manuelle, pas synchronisée en continu — le code sur
  ce PC est la source de vérité.

## Architecture

- **Stockage local** : SQLite (`%MyDocuments%\Iziregi\Data\iziregi.db`, via Dapper),
  schéma en `Data\Db.cs`, évolution par ajout de colonnes (`TryAddColumn`) plutôt que
  vraies migrations — pas de retour en arrière possible sur le schéma.
- **Modèle métier** : `WorkOrder` (bon de régie) rattaché à un `Project`, pipeline en 5
  étapes (création → envoyé entreprise → devis reçu → envoyé signataire → validé),
  numérotation `01-D{ProjectId}` (ex. `01-D1`) — anciens bons créés avant un renommage
  silencieux gardent le tag `P` (`01-P1`), `MainWindow.TryParseWorkOrderRef` gère les deux.
  Devis (heures, taux, déplacement, forfait, rabais, TVA), lignes de matériel, signature
  électronique.
- **Config poste** : `%LOCALAPPDATA%\Iziregi\iziregi-config.json` (URL serveur + clé API),
  clé API chiffrée via DPAPI Windows (liée au compte Windows courant) — jamais en clair.
- **Synchro serveur** : polling toutes les 60s (`MainWindow.StartServerSyncTimer`) sur
  `/internal/submissions/since`, applique les réponses entreprise/signataire en local
  (popup son + flash barre des tâches). Curseur de synchro persisté (`LastServerSyncUtc`).
- **Lignes de devis (Libellé/Matériel, `WorkOrderLines` en local)** : poussées vers le
  serveur depuis le 22.08.2026 (`WorkOrderWindow.PublishWorkOrderToServerAsync`, champ
  `lines` du payload `/internal/workorders/upsert`) — avant cette date elles ne quittaient
  jamais ce PC. Ajouté pour que ces lignes soient visibles/modifiables depuis le nouveau
  client web `Iziregi.Web` (voir son `CLAUDE.md`). Table serveur : `work_order_lines`, à
  ne pas confondre avec `work_order_quote_lines` (contre-devis de l'entreprise via `/bdr`).
- **Fallback manuel hors-ligne** : export `.iziregi-package` (zip manifeste JSON) et
  import `.iziregi-reponse` via sélecteur de fichier OU dossier `INBOX` surveillé
  (`FileSystemWatcher`, voir `MainWindow.StartInboxWatcher`).
- **Mise à jour auto** : ping serveur (`/internal/ping`) compare version installée vs
  serveur ; si plus récente, bandeau non bloquant (PAS de popup modale — voir piège
  ci-dessous), téléchargement avec fenêtre de progression, lancement silencieux de
  l'installateur Inno Setup (`/SILENT /SUPPRESSMSGBOXES /NORESTART`), relance auto avec
  `--updated`.
- **Licence par machine** : identifiant dérivé du `MachineGuid` Windows (registre),
  plafond configurable côté serveur (`tenants.max_machines`).

## Pages / fenêtres principales

- `MainWindow` : orchestrateur (navigation, sync, sélection projet).
- Pages (UserControls) : Dashboard, Accounting (comptabilité + export CSV/PDF), Archives,
  Trash, Lists (listes de référence par projet : lieux, étages, entreprises, demandeurs,
  réserves), Planning (éditeur de plan visuel : stickers, zones de texte riche, export PDF).
- Fenêtres : `WorkOrderWindow` (édition d'un bon, 3 modes : Architecte/EntrepriseDevis/
  Signataire), `ProjectsWindow`, `ArchitectIdentityWindow`, `ChooseProjectWindow`,
  `ConfigSetupWindow`.
- Services : `PdfService`, `ExportService` (package zip), `PackageImportService`,
  `IziregiConfigService` (config chiffrée DPAPI).

## Pièges connus (coûteux en temps si oubliés)

1. **Boîte de dialogue modale "Oui/Non" au démarrage** : se refermait automatiquement sur
   "No" sans action utilisateur (cause exacte jamais identifiée). Ne JAMAIS réintroduire
   de `MessageBox.Show` modale pour la confirmation de mise à jour au démarrage — utiliser
   le bandeau non bloquant (`UpdateBanner`) déclenché uniquement par un vrai clic utilisateur.
2. **"Problème du bootstrap"** pour tester un correctif du mécanisme de mise à jour
   lui-même : le code qui s'exécute au moment du clic "Mettre à jour" est celui de la
   version ACTUELLEMENT installée, pas celui qu'on vient de publier. Pour tester un
   correctif du mécanisme lui-même : publier la version corrigée → l'installer
   MANUELLEMENT une fois (contourne le mécanisme à tester) → publier une version
   suivante comme cible → alors seulement tester le clic "Mettre à jour" depuis la
   version corrigée déjà installée.
3. Éviter de dire "parfait" ou qu'un correctif est définitif avant confirmation réelle
   par un test de Joe.
4. **Joe teste parfois l'exe installé (`%LOCALAPPDATA%\Iziregi\Iziregi.Test.exe`), pas le
   build de dev** (`Iziregi.Test\bin\Debug\net8.0-windows\Iziregi.Test.exe`) : les deux ont
   le même nom de processus (`Iziregi.Test.exe`), donc `tasklist` ne suffit pas à distinguer
   lequel tourne — utiliser `Get-Process -Id <pid> | Select Path` pour vérifier. Si un
   correctif semble "ne pas s'appliquer" après rebuild, vérifier ce point avant de chercher
   ailleurs. Solution pendant une session de dev itérative : tuer l'instance installée et
   relancer explicitement celle de `bin\Debug`.
5. **Commentaires XAML** : `<!-- ... -->` ne tolère pas `--` à l'intérieur du texte (erreur
   de compilation MC3000, "An XML comment cannot contain '--'"). Piège récurrent avec les
   tirets doubles utilisés comme ponctuation en français — préférer une virgule ou un point.
6. **Ambiguïté de types WPF vs WinForms/System.Drawing** : ce projet référence les deux
   (voir piège `Button` déjà noté pour `MainWindow.xaml.cs`), donc tout type dont le nom
   existe dans les deux univers déclenche une erreur CS0104 "référence ambiguë" — rencontré
   pour `Point` (`System.Windows.Point` vs `System.Drawing.Point`) et `Panel`
   (`System.Windows.Controls.Panel` vs `System.Windows.Forms.Panel`) dans
   `PlanningPage.xaml.cs`. Réflexe dès qu'une erreur CS0104 apparaît : qualifier
   explicitement en `System.Windows.X` plutôt que de chercher une autre cause.

## Export complet des données (17.07.2026)

`ConfigSetupWindow` (accessible via le bouton "Paramètres" du menu de navigation) propose
maintenant "Exporter toutes mes données…", qui appelle `ExportService.ExportAllData` :
génère un zip contenant un CSV par table de la base SQLite locale (introspection dynamique
via `sqlite_master`/`PRAGMA table_info`, donc toujours à jour même si de nouvelles colonnes
sont ajoutées plus tard sans qu'il faille penser à modifier ce fichier), plus un
`LISEZ-MOI.txt`. Objectif : donner une contrepartie concrète à la promesse de portabilité
des données que Joe compte inclure dans ses futures CGV — le client peut récupérer
l'intégralité de son historique en format ouvert (lisible par un tableur), même sans
Iziregi installé. Ne contient aucune donnée sensible (la clé API est stockée séparément,
chiffrée via DPAPI, hors de cette base SQLite).

## État (08.07.2026)

Le mécanisme de mise à jour auto a été réparé cette session-là (bandeau non bloquant,
installation silencieuse, relance auto) et confirmé fonctionnel par Joe en v1.0.27
(message d'attente 2,5s + splash "Iziregi" au lieu de "Mise à jour" — OK).

Un ancien fichier de handoff existe (`iziregi-handoff. 6.07.md` sur le Bureau de Joe,
dossier `txt`) mais Joe l'a signalé comme obsolète le 08.07.2026 — ne pas s'y fier
comme source de vérité, ce CLAUDE.md le remplace.

## Modernisation du look (13.07.2026, appliquée et compilée — vérification visuelle par Joe en attente)

Refonte visuelle demandée par Joe ("ça fait un peu vieux") sur TOUTE l'app en une fois,
appliquée sur : Dashboard, Archives, Corbeille, Listes, Comptabilité, Planning (4
sections), ProjectsWindow, ArchitectIdentityWindow, WorkOrderWindow.

- `App.xaml` : nouvelle ressource partagée `CardShadowEffect` (DropShadowEffect léger,
  Opacity 0.10) + 3 géométries d'icônes vectorielles (`IconEditGeometry`,
  `IconArchiveGeometry`, `IconTrashGeometry`) dessinées en segments droits (M/L/Z
  uniquement). Préparé dans une session sans SDK .NET (patch fourni par Joe), puis
  appliqué et compilé avec succès dans une session suivante.
- Bordures de grilles/cartes/toolbars : noir plein (`#111827` / `Black`) → gris doux
  (`#D1D5DB`), cohérent avec la convention déjà utilisée par ProjectsWindow/
  ArchitectIdentityWindow (`CardStyle`, déjà la page la plus "moderne" de l'app avant
  cette session).
- Effet d'ombre + coin arrondi 12 (au lieu de 8-10) sur toutes les cartes blanches.
- Emojis couleur ✎/📦/🗑 (rendu clip-art multicolore selon Windows) remplacés par des
  icônes vectorielles monochromes qui suivent la couleur du bouton (Dashboard + Archives
  uniquement — les autres glyphes comme ↩ ✖ ⊕ ◀ ▶ sont déjà de simples caractères
  monochromes, pas de vrais emojis, donc laissés tels quels).
- Page Listes : les boutons (Ajouter/Renommer/Supprimer/etc.) utilisaient le chrome
  Windows par défaut (aucun Style) — c'était la page la plus datée de l'app. Stylés
  maintenant avec `SmallButtonStyle` (déjà défini dans ce fichier).
- **Décision volontaire de ne PAS centraliser tous les styles dupliqués dans App.xaml** :
  chaque page garde ses propres styles locaux (juste les valeurs de couleur/rayon ont
  changé), pour éviter le risque d'un refactor de portée large sans pouvoir compiler/
  tester dans ce sandbox. Une vraie centralisation reste une amélioration future possible
  une fois ce lot testé et validé par Joe.
- `MainWindow.xaml` : le `<Menu>`/`<MenuItem>` Windows par défaut (fond lié au thème
  système, apparence "2007", visible en permanence sur toutes les pages) remplacé par des
  boutons pilule (`NavPillButtonStyle`/`NavPillButtonActiveStyle`) — bleu quand actif, gris
  clair au survol. Le bandeau passe en fond blanc avec une fine bordure basse. L'onglet actif
  est mis à jour via `SetActiveNavButton()` (`MainWindow.xaml.cs`), appelé au début de chaque
  `Show*()`. Attention : `Button` est ambigu dans ce fichier (WPF + WinForms référencés) —
  qualifier en `System.Windows.Controls.Button`.

## Page Listes : édition directe en ligne (28-29.07.2026)

Refonte complète du modèle d'édition de `ListsPage`, demandée par Joe pour que les 8 listes
de référence (Concerne/Demandé par/Bâtiment/Étage/Entreprise/Zone de texte planning/Cat./
Urg. + la nouvelle liste Nom, voir plus bas) et les libellés de champs se modifient
directement dans la page, sans fenêtres popup séparées.

- **Modèle** : `EditableListItem` (`ListsPage.xaml.cs`), avec `OriginalName` (`null` = ligne
  créée pendant la session, pas encore en base) et `Name` (éditable). Chaque liste est une
  `ObservableCollection<EditableListItem>` liée UNE SEULE FOIS à son `ListBox.ItemsSource`
  dans le constructeur ; `Reload()`/`ReloadCore()` ne font que vider/remplir cette même
  collection (`PopulateItems`), jamais réassigner `ItemsSource`.
- **Édition d'une ligne** : clic direct dans le `TextBox` de la ligne (bordure bleue au
  focus). Entrée/flèche bas valide et passe à la ligne suivante (crée une ligne vide en bas
  si on est déjà sur la dernière) ; flèche haut remonte. Vider une ligne la supprime, les
  suivantes remontent automatiquement (`ObservableCollection`).
- **Sélection** = soit une ligne précise (`_activeRowItem` non null), soit toute la liste
  (`_activeRowItem == null`, clic dans le vide) — détermine la portée de Supprimer/Copier.
- Boutons Ajouter/Renommer/Définir par défaut **supprimés** : le ComboBox "Sélection par
  défaut" au-dessus de chaque liste (déjà éditable) fait office de "Définir par défaut".
  Supprimer agit sur la ligne ou toute la liste (confirmation) ; "Copier la liste"/Coller
  idem, Coller comble d'abord les lignes vides existantes avant d'en ajouter.
- **Enregistrement différé** : rien n'est écrit en base pendant l'édition. Un seul bouton
  "Enregistrer" global (`SaveAllNow`, bleu — `PrimaryButtonStyle`, **toujours cliquable**,
  décision volontaire suite à un retour de Joe sur le clic en trop que demandait l'ancien
  état désactivé) diffe chaque liste contre son instantané "original" par IDENTITÉ
  (`SaveListChanges`, via `OriginalName`) pour distinguer un renommage d'un supprimer+ajouter,
  puis écrit libellés + 8 listes + 8 valeurs par défaut en une fois. `HasUnsavedChanges`
  (labels + listes) pilote le pop-up de confirmation en quittant la page sans enregistrer
  (`MainWindow.ConfirmLeaveListsPageIfDirty`, 6 points d'appel : Dashboard/Comptabilité/
  Archives/Corbeille/Planning/Identité Société).
- **Doublons** : avertissement + annulation si un nom est dupliqué DANS la même liste, ou si
  un libellé de champ est dupliqué entre les 9 champs — validation en mémoire au moment où le
  champ perd le focus, avant tout enregistrement.
- **Couleurs Entreprise/Cat.** restent en écriture immédiate en base (pas différées comme le
  texte) — les handlers couleur (`PickCompanyColor_Click` etc.) ne font plus `Reload()`
  après un changement de couleur (ça effacerait les éditions de texte en cours dans les
  autres listes) : ils mettent juste à jour `ColorBrush` sur `_activeRowItem` directement.
- **Piège résolu (29.07.2026)** : un `TextBox` de ligne ne remplissait QUE la largeur de son
  texte (pas toute la ligne) → un clic à droite du texte manquait le champ, donnant
  l'impression qu'"on ne peut rien écrire". Fix : `HorizontalAlignment="Stretch"` sur le
  `TextBox` + `ItemContainerStyle` (`StretchListBoxItemStyle`, `HorizontalContentAlignment=
  Stretch`, `Padding=0`) sur chaque `ListBox`. Pour le template avec pastille de couleur
  (Entreprise/Cat.), le `StackPanel` horizontal a dû être remplacé par une `Grid` à 2
  colonnes (Auto + `*`) car un `StackPanel` ne laisse pas un enfant remplir l'espace restant.

### Liste "Nom" remplace le bloc "Délai" (29.07.2026, demande de Joe)

Le champ "Délai" (`DeadlineDatePicker`, section Demande du bon) n'a jamais eu de liste
associée dans la page Listes (juste un libellé). Joe a demandé de récupérer cet espace :
- **Délai** : libellé figé "Délai" en dur dans `WorkOrderWindow.xaml` (plus personnalisable,
  `Db.GetLabelDeadline`/`SetLabelDeadline` retirés). Plus de date pré-remplie par défaut :
  `WorkOrder.DeadlineDate` n'a plus d'initialiseur (`default(DateTime)` = "pas de délai",
  convention déjà utilisée par `PdfService`), le `DatePicker` affiche `null` (vide) tant
  qu'aucune date n'a été choisie au lieu de proposer la date du jour.
- **Nom** (section Validation du bon, `SignatureNameComboBox` — anciennement un `TextBox`
  libre `SignatureNameTextBox`) : devient une ComboBox éditable alimentée par une nouvelle
  liste de référence, table `SignatoryNames` (`ProjectId`, `Name`, sans contrainte UNIQUE
  SQL — comme les autres listes, la détection de doublon est gérée côté application, pas en
  base). CRUD `Db.GetSignatoryNames`/`InsertSignatoryName`/`RenameSignatoryName` (avec
  propagation vers `WorkOrders.SignatureName` existants, comme `RenameEtage`)/
  `DeleteSignatoryName`, libellé `Db.GetLabelSignatoryName`/`SetLabelSignatoryName` (défaut
  "Nom", renommable par Joe via son champ libellé dans la page Listes), valeur par défaut
  `Db.GetDefaultSignatoryName`/`SetDefaultSignatoryName`.
