# Iziregi — Pivot Web App : dossier de reprise (21.08.2026)

Document préparé pour reprendre ce chantier dans une prochaine session (celle-ci devenait
lente). Contient tout le contexte de la discussion stratégique du 21.08.2026 — à lire
AVANT de commencer le moindre code sur ce sujet.

## Contexte / pourquoi ce pivot

- Joe indique que l'adaptabilité multiplateforme était prévue dès le départ d'Iziregi,
  mais le choix technique initial (WPF) a été fait sans en mesurer les conséquences —
  WPF est Windows uniquement, aucune voie de portage simple vers Mac.
- Besoin actuel exprimé : support Mac, Linux, mobile, en plus de PC — sortir des
  problèmes de compatibilité liés à la plateforme.
- Vision à terme (peut attendre) : offre "Entreprise" — une société fait tourner sa
  propre instance d'Iziregi sur son infrastructure. Le serveur actuel (ASP.NET Core/
  Blazor Server + PostgreSQL, déjà multi-tenant) s'en rapproche déjà pas mal.
- Contrainte non négociable de Joe : garder l'app WPF actuelle fonctionnelle pendant
  toute la transition — c'est son business en production, avec des clients payants et
  des essais gratuits en cours. Pas de coupure, pas de gros-bang.

## Options évaluées pendant la discussion (et pourquoi elles ont été écartées)

- **Electron** — écarté. Ne réutilise aucun code C# existant (nécessiterait une
  réécriture JS/TS complète de toute façon), et ne fait PAS de mobile (il aurait fallu
  un 2e stack séparé type React Native/Flutter en plus). Peu cohérent avec l'objectif
  d'unifier sur une seule base de code.
- **.NET MAUI Blazor Hybrid** — sérieusement envisagé un moment (réutilise le Blazor
  déjà en place côté serveur, reste en C#, vrai natif Windows/Mac/iOS/Android via des
  composants Razor partagés). Limite : pas de Linux desktop natif (il faudrait passer
  par le navigateur pour Linux). Écarté finalement car Joe juge l'app assez simple
  fonctionnellement pour être une web app pure, sans repasser par du natif du tout.
- **Rewrite total immédiat, en une fois** — écarté comme MÉTHODE (pas comme
  destination). Trop risqué pour un produit en production avec des clients réels ;
  volume de règles métier accumulées (rabais en cascade, mise en page PDF exacte,
  signature électronique, sync serveur...) qui ne sont écrites nulle part ailleurs que
  dans le code C# actuel — un rewrite big-bang risque d'en perdre ou d'en mal
  réinterpréter une partie.

## Direction retenue

### Stack technique recommandée

**TypeScript unifié (front + back), via Next.js, PostgreSQL conservé tel quel.**

Pourquoi :
- Un seul langage, un seul repo, au lieu de la séparation actuelle C# serveur / C#
  client WPF — plus simple à maintenir seul pour Joe, et écosystème le plus large/le
  mieux documenté (donc le mieux couvert par l'assistance IA sur la durée).
- PostgreSQL est un bon choix déjà en place, aucune raison d'en changer.
- Écosystème mature pour tout ce qui est aujourd'hui fait "à la main" en WPF :
  éditeur de texte riche (Tiptap ou Lexical), tableaux (TanStack Table), glisser-
  déposer (dnd-kit), signature électronique (signature_pad), génération PDF (à
  trancher : react-pdf, ou rendu HTML→PDF côté serveur type Puppeteer, ou lib PDF
  serveur Node).
- Se package facilement en conteneur Docker → sert aussi l'objectif "Entreprise
  self-host" à terme (plus simple à héberger pour une équipe IT tierce qu'un stack
  ASP.NET/Blazor Server).

### Méthode : incrémentale, page par page — PAS un rewrite big-bang

- Le client WPF + `Iziregi.Server` actuels restent en production, sans interruption,
  pendant toute la transition.
- La nouvelle web app est un **nouveau client** qui réutilise l'API déjà exposée par
  `Iziregi.Server` (préfixe `/internal/...`) et la **même base PostgreSQL** — pas
  besoin de réécrire le serveur pour démarrer ce chantier.
- Un tenant/client donné peut basculer sur le web dès que "sa" page est prête côté
  web ; WPF et web coexistent tant que nécessaire, aucune bascule forcée.
- Une fois que la web app couvre 100% des fonctionnalités du client WPF, on pourra
  arrêter de maintenir ce dernier — sans qu'il y ait jamais eu de coupure de service.

### Premier chantier obligatoire, avant toute page fonctionnelle : l'authentification web

Le modèle actuel (une clé API unique par tenant, stockée chiffrée via DPAPI sur le
poste Windows) ne convient PAS à un navigateur — on ne peut pas y cacher un secret de
la même façon qu'un programme installé. Il faut un vrai système de connexion par
utilisateur (email/mot de passe, ou lien magique), pas juste une clé partagée par
tenant. C'est la toute première brique à construire, avant la première page métier.

### Par quelle page commencer

- **PAS Planification** — de loin la page la plus complexe (canvas de plan, éditeur
  de texte riche, glisser-déposer d'images, export PDF multi-sections). À garder pour
  la fin, une fois le mécanisme de bout en bout validé sur des pages plus simples.
- Recommandé pour valider le mécanisme (auth + lecture/écriture + déploiement) :
  **Tableau de bord** ou **liste des Bons** (bons de régie) — simples, autonomes.

## Repères sur l'architecture existante (pour mémoire — se référer aux CLAUDE.md des
deux dépôts pour le détail à jour, ceci n'est qu'un résumé figé au 21.08.2026)

- **`Iziregi.Server`** (`C:\Users\HP\source\repos\Iziregi.Server`) : ASP.NET Core /
  Blazor Server (.NET 8), PostgreSQL via Npgsql, hébergé chez Infomaniak (Suisse) sur
  VPS Ubuntu, `https://iziregi.com`. Multi-tenant par clé API (table `tenants`),
  panneau admin `/admin` (protégé par `IZIREGI_ADMIN_PASSWORD` — liste/crée/renouvelle/
  révoque les tenants), demandes d'essai gratuit 7 jours (`trial_requests`, approuvées
  manuellement par Joe), installateur client téléchargeable publiquement en GET sur
  `/internal/download/installer`. GitHub `09h00/Iziregi.Server`, branche `main`.
- **`Iziregi.Test`** (`C:\Users\HP\source\repos\Iziregi.Test`) : client WPF .NET 8,
  SQLite local (`%MyDocuments%\Iziregi\Data\iziregi.db`), synchro par polling toutes
  les 60s avec le serveur, licence par machine dérivée du `MachineGuid` Windows
  (plafond configurable par tenant), mise à jour automatique silencieuse (Inno Setup).
  GitHub `09h00/Iziregi.Test`, branche `main` (branche `master` obsolète, à ignorer).
- **Positionnement éthique déjà établi**, à conserver dans la nouvelle version :
  hébergement 100% suisse (Infomaniak, ISO 27001, Swiss Hosting/Swiss Made Software),
  conformité nLPD, aucune revente de données, export complet des données possible à
  tout moment dans un format ouvert (CSV).

## Contraintes de collaboration à respecter dès la reprise

- Communication en français, tutoiement (Joe).
- Toujours préciser AVANT une commande si elle est PowerShell (ce PC Windows) ou
  SSH/Ubuntu (serveur de production) — instruction permanente de Joe.
- Ne jamais coller de code ou de diffs dans le texte du chat — décrire les
  changements en prose, laisser les fichiers parler pour eux-mêmes.
- Un message de Joe commençant par "question" = répondre uniquement, aucune action.
- Pour `Iziregi.Server` actuel : déployer automatiquement après un changement de code
  serveur, sans attendre que Joe le demande. (Pour le nouveau projet web, l'équivalent
  — déploiement auto ou non — reste à définir avec Joe une fois le repo créé.)

## Prochaine étape concrète à l'ouverture de la prochaine session

1. Reconfirmer avec Joe qu'il valide cette direction (Next.js + TypeScript +
   PostgreSQL existant, approche incrémentale page par page, auth d'abord).
2. Décider où vit le nouveau code — recommandation : un nouveau dépôt séparé (ex.
   `Iziregi.Web`), plutôt que de le mêler aux deux dépôts existants.
3. Construire la brique d'authentification web (connexion par utilisateur).
4. Construire la première page fonctionnelle (Tableau de bord ou Bons), branchée sur
   l'API existante de `Iziregi.Server`.
