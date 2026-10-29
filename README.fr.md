<p align="center"><img src="img/GGCalendar_logo.jpg" alt="Logo de GGCalendar" width="128"></p>

<p align="center"><a href="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml"><img src="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml/badge.svg" alt="État de la CI"></a></p>

<h1 align="center">GGCalendar</h1>

<p align="center">Agenda pour Windows 11 avec synchronisation Google Agenda.</p>

<p align="center">
  <a href="https://github.com/giansalvo/GGCalendar/releases/latest">Télécharger la dernière version</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/fr/index.html">Site</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/fr/privacy.html">Confidentialité</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/fr/terms.html">Conditions</a> ·
  <a href="LICENSE">Licence GPL v3 ou ultérieure</a>
</p>

<p align="center">🌐 <a href="README.md">Italiano</a> · <a href="README.en.md">English</a> · <a href="README.de.md">Deutsch</a> · <a href="README.es.md">Español</a> · Français</p>

---

## Fonctionnalités

- Vues jour, semaine, 6 jours, mois, année et programme ; week-ends masquables
- Création, déplacement et redimensionnement des événements par glisser-déposer, y compris entre agendas différents
- Fiche récapitulative au clic sur un événement (modifier, supprimer, partager, rejoindre Meet) ; double-clic pour le modifier
- Partager un événement depuis sa fiche : WhatsApp, Telegram, e-mail (titre en objet, texte dans le corps), e-mail aux invités (seulement s'il y en a), fichier `.ics` sur le disque, presse-papiers et les autres applications de Windows (texte et fichier `.ics`)
- Événements récurrents, avec modification ou suppression d'une seule occurrence, des suivantes ou de toute la série
- Rappels avec notification, alarme sonore (jusqu'à ce que vous l'arrêtiez, 2 minutes au maximum) et report, conservés aussi après un redémarrage
- Liste des alarmes (icône à côté de la loupe) : celles qui sonnent et celles à venir dans les 60 prochains jours, avec le temps restant ; vous pouvez les arrêter, les annuler (sur cet ordinateur seulement) ou ouvrir l'événement. Les alarmes sonnent une à la fois et seulement quand GGCalendar est ouvert ; les rappels des agendas masqués ne sonnent pas (les alarmes reportées, si)
- Synchronisation dans les deux sens avec un ou plusieurs comptes Google Agenda, avec gestion des conflits ; les alarmes voyagent avec l'événement sur Google et sont partagées avec [GGCalendar Mobile](https://github.com/giansalvo/GGCalendarMobile) pour Android
- Invités, liens Google Meet, rappels par e-mail (envoyés par Google)
- Recherche d'un texte dans le titre, la description, le lieu et les invités, dans un ou plusieurs agendas, avec des dates et des mots à exclure
- Création et suppression d'agendas sur les comptes Google (seulement ceux dont vous êtes propriétaire, jamais l'agenda principal)
- Modification du nom et de la couleur des agendas : le nom pour les agendas locaux et les vôtres sur Google, la couleur pour tous (8 couleurs rapides et les 24 de Google)
- Déconnecter un compte Google par un clic droit sur son nom (ou depuis les paramètres) : une question indique ce qui est retiré de cet ordinateur (agendas, événements, modifications non encore envoyées) ; l'accès est révoqué sur Google et ce que vous avez sur Google ne change pas
- Barre d'état toujours visible avec l'indicateur de synchronisation : un point vert quand tout est envoyé à Google, un sablier avec le nombre d'événements quand certains attendent. Un clic ouvre la liste des modifications en attente, par agenda : vous pouvez réessayer tout de suite ou les annuler (l'événement revient tel qu'il est sur Google)
- Agendas locaux, importation et exportation de fichiers `.ics`
- Thème clair et sombre
- Interface en italien, anglais, allemand, espagnol et français : la langue se choisit dans les *Paramètres* (par défaut, celle de Windows) et change immédiatement

## Installation

Téléchargez `GGCalendar-v<version>-Setup.exe` depuis la page [Releases](https://github.com/giansalvo/GGCalendar/releases/latest) et exécutez-le. Le programme d'installation parle la langue de Windows si c'est l'une des cinq ci-dessus (anglais sinon).

- Prérequis : Windows 11 64 bits. Le runtime .NET est inclus, aucun autre composant n'est nécessaire.
- Par défaut, il s'installe uniquement pour l'utilisateur courant, sans droits d'administrateur.
- Le programme d'installation n'est pas signé numériquement : si Windows SmartScreen affiche « Windows a protégé votre ordinateur », choisissez **Informations complémentaires → Exécuter quand même**.

Les données de l'application sont dans `%AppData%\GGCalendar` et ne sont pas supprimées par la désinstallation.

## Compiler à partir du code source

Prérequis : [.NET SDK 10](https://dotnet.microsoft.com/download) sous Windows.

```powershell
git clone https://github.com/giansalvo/GGCalendar.git
cd GGCalendar
dotnet run --project src/GGCalendar.App
dotnet test
```

### Identifiants Google

Les identifiants OAuth de l'application officielle ne sont pas dans le dépôt. Pour utiliser la synchronisation avec une version compilée par vous, il faut votre propre projet Google Cloud :

1. Dans la [Google Cloud Console](https://console.cloud.google.com/), activez la **Google Calendar API**.
2. Configurez l'écran de consentement OAuth avec le champ d'application `https://www.googleapis.com/auth/calendar`.
3. Créez un ID client OAuth de type **Application de bureau** et téléchargez le fichier JSON.
4. Copiez le fichier dans un dossier `OAuth/` à la racine du dépôt, avec un nom commençant par `client_secret` (le dossier est exclu de git) : il sera inclus dans l'exécutable à la compilation.

Vous pouvez aussi saisir l'ID client et le secret dans l'application, dans *Paramètres → Comptes Google → Avancé*.

### Créer le programme d'installation

Nécessite [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) et les identifiants Google décrits ci-dessus.

```powershell
.\installer\build-installer.ps1
```

Le script publie l'application autonome pour `win-x64` et crée `website/downloads/GGCalendar-v<version>-Setup.exe`, en prenant la version dans `InformationalVersion` de `src/GGCalendar.App/GGCalendar.App.csproj`.

## Structure du projet

| Dossier | Contenu |
|---|---|
| `src/GGCalendar.Core` | Modèles, récurrences (RRULE), analyseur et écrivain ICS, calcul des rappels, disposition des événements qui se chevauchent, textes dans les cinq langues (`Localization`) |
| `src/GGCalendar.Data` | Stockage SQLite des agendas et des événements, file des modifications à envoyer, paramètres, état des rappels |
| `src/GGCalendar.Providers.Google` | Connexion OAuth (PKCE, redirection en loopback) et client des API Google Agenda |
| `src/GGCalendar.App` | Interface [Avalonia](https://avaloniaui.net/) (MVVM), synchronisation, notifications et sons |
| `tests/GGCalendar.Core.Tests` | Tests xUnit |
| `installer` | Script Inno Setup et script de construction du programme d'installation |
| `website` | Site de présentation, politique de confidentialité et conditions, en cinq langues |

## Confidentialité

GGCalendar n'a pas de serveurs propres et ne collecte pas de télémétrie. Vos données restent sur votre ordinateur et, si vous connectez un compte Google, circulent uniquement entre l'ordinateur et Google. Les détails sont dans la [politique de confidentialité](https://profgusinu.altervista.org/GGCalendar/fr/privacy.html) ; les conditions d'utilisation dans les [conditions d'utilisation](https://profgusinu.altervista.org/GGCalendar/fr/terms.html). Les textes italiens font foi.

## Licence

Copyright © 2026 Giansalvo Gusinu.

GGCalendar est un logiciel libre : vous pouvez le redistribuer et/ou le modifier selon les termes de la GNU General Public License publiée par la Free Software Foundation, soit la version 3 de la licence, soit (à votre choix) toute version ultérieure. Il est distribué dans l'espoir qu'il sera utile, mais **sans aucune garantie**, pas même la garantie implicite de qualité marchande ou d'adéquation à un usage particulier. Le texte complet se trouve dans le fichier [LICENSE](LICENSE).

### Termes additionnels (GPL v3, section 7)

Le nom « GGCalendar » et le logo (fichiers `img/GGCalendar.jpg`, `img/GGCalendar_logo.jpg`, `src/GGCalendar.App/Assets/GGCalendar.ico`, `src/GGCalendar.App/Assets/GGCalendar_logo.jpg`, `website/assets/logo.jpg`) identifient l'application originale de l'auteur :

- conformément à la section 7(e), la licence ne confère aucun droit d'utilisation du nom et du logo comme marques ;
- conformément à la section 7(c), celui qui distribue une version modifiée doit utiliser un nom et un logo différents, afin qu'elle ne soit pas confondue avec l'original. Il est permis d'indiquer qu'elle dérive de GGCalendar.
