<p align="center"><img src="img/GGCalendar_logo.jpg" alt="GGCalendar-Logo" width="128"></p>

<p align="center"><a href="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml"><img src="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml/badge.svg" alt="CI-Status"></a></p>

<h1 align="center">GGCalendar</h1>

<p align="center">Kalender für Windows 11 mit Synchronisierung mit Google Kalender.</p>

<p align="center">
  <a href="https://github.com/giansalvo/GGCalendar/releases/latest">Neueste Version herunterladen</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/de/index.html">Website</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/de/privacy.html">Datenschutz</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/de/terms.html">Nutzungsbedingungen</a> ·
  <a href="LICENSE">Lizenz GPL v3 oder später</a>
</p>

<p align="center">🌐 <a href="README.md">Italiano</a> · <a href="README.en.md">English</a> · Deutsch · <a href="README.es.md">Español</a> · <a href="README.fr.md">Français</a></p>

---

## Funktionen

- Ansichten Tag, Woche, 6 Tage, Monat, Jahr und Terminübersicht; Wochenenden ausblendbar
- Termine per Ziehen erstellen, verschieben und in der Größe ändern, auch zwischen verschiedenen Kalendern
- Übersichtskarte beim Klick auf einen Termin (bearbeiten, löschen, E-Mail an die Gäste, exportieren, an Meet teilnehmen); Doppelklick zum Bearbeiten
- Wiederkehrende Termine, mit Bearbeiten oder Löschen eines einzelnen Termins, der folgenden oder der ganzen Serie
- Erinnerungen mit Benachrichtigung, Wecker mit Ton (bis Sie ihn ausschalten, höchstens 2 Minuten) und Schlummerfunktion, auch nach einem Neustart erhalten
- Synchronisierung in beide Richtungen mit einem oder mehreren Google-Kalender-Konten, mit Konfliktbehandlung; Wecker reisen mit dem Termin über Google und werden mit [GGCalendar Mobile](https://github.com/giansalvo/GGCalendarMobile) für Android geteilt
- Gäste, Google-Meet-Links, Erinnerungen per E-Mail (von Google gesendet)
- Suche nach einem Text in Titel, Beschreibung, Ort und Gästen, in einem oder mehreren Kalendern, mit Datum und auszuschließenden Wörtern
- Kalender in Google-Konten erstellen und löschen (nur eigene, nie den Hauptkalender)
- Name und Farbe von Kalendern ändern: den Namen für lokale und eigene Google-Kalender, die Farbe für alle (8 Schnellfarben und die 24 von Google)
- Lokale Kalender, Import und Export von `.ics`-Dateien
- Helles und dunkles Design
- Oberfläche in Italienisch, Englisch, Deutsch, Spanisch und Französisch: Die Sprache wird in den *Einstellungen* gewählt (Standard: die Sprache von Windows) und ändert sich sofort

## Installation

Laden Sie `GGCalendar-v<Version>-Setup.exe` von der Seite [Releases](https://github.com/giansalvo/GGCalendar/releases/latest) herunter und führen Sie sie aus. Das Installationsprogramm spricht die Sprache von Windows, wenn sie eine der fünf oben genannten ist (sonst Englisch).

- Voraussetzungen: 64-Bit-Windows 11. Die .NET-Laufzeit ist enthalten, es werden keine weiteren Komponenten benötigt.
- Standardmäßig wird nur für den aktuellen Benutzer installiert, ohne Administratorrechte.
- Das Installationsprogramm ist nicht digital signiert: Wenn Windows SmartScreen „Der Computer wurde durch Windows geschützt“ anzeigt, wählen Sie **Weitere Informationen → Trotzdem ausführen**.

Die Daten der App liegen in `%AppData%\GGCalendar` und werden von der Deinstallation nicht gelöscht.

## Aus dem Quellcode erstellen

Voraussetzungen: [.NET SDK 10](https://dotnet.microsoft.com/download) unter Windows.

```powershell
git clone https://github.com/giansalvo/GGCalendar.git
cd GGCalendar
dotnet run --project src/GGCalendar.App
dotnet test
```

### Google-Zugangsdaten

Die OAuth-Zugangsdaten der offiziellen App befinden sich nicht im Repository. Um die Synchronisierung mit einem selbst erstellten Build zu nutzen, brauchen Sie ein eigenes Google-Cloud-Projekt:

1. Aktivieren Sie in der [Google Cloud Console](https://console.cloud.google.com/) die **Google Calendar API**.
2. Konfigurieren Sie den OAuth-Zustimmungsbildschirm mit dem Bereich `https://www.googleapis.com/auth/calendar`.
3. Erstellen Sie eine OAuth-Client-ID vom Typ **Desktop-App** und laden Sie die JSON-Datei herunter.
4. Kopieren Sie die Datei in einen Ordner `OAuth/` im Stammverzeichnis des Repositorys, mit einem Namen, der mit `client_secret` beginnt (der Ordner ist von git ausgeschlossen): Sie wird beim Erstellen in die ausführbare Datei aufgenommen.

Alternativ können Sie Client-ID und Secret in der App eingeben, unter *Einstellungen → Google-Konten → Erweitert*.

### Das Installationsprogramm erstellen

Erfordert [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) und die oben beschriebenen Google-Zugangsdaten.

```powershell
.\installer\build-installer.ps1
```

Das Skript veröffentlicht die eigenständige App für `win-x64` und erstellt `website/downloads/GGCalendar-v<Version>-Setup.exe`, wobei die Version aus `InformationalVersion` in `src/GGCalendar.App/GGCalendar.App.csproj` übernommen wird.

## Projektstruktur

| Ordner | Inhalt |
|---|---|
| `src/GGCalendar.Core` | Modelle, Wiederholungen (RRULE), ICS-Parser und -Writer, Berechnung der Erinnerungen, Layout überlappender Termine, Texte in den fünf Sprachen (`Localization`) |
| `src/GGCalendar.Data` | SQLite-Speicher für Kalender und Termine, Warteschlange der zu sendenden Änderungen, Einstellungen, Zustand der Erinnerungen |
| `src/GGCalendar.Providers.Google` | OAuth-Anmeldung (PKCE, Loopback-Weiterleitung) und Client der Google-Calendar-API |
| `src/GGCalendar.App` | Oberfläche mit [Avalonia](https://avaloniaui.net/) (MVVM), Synchronisierung, Benachrichtigungen und Töne |
| `tests/GGCalendar.Core.Tests` | xUnit-Tests |
| `installer` | Inno-Setup-Skript und Build-Skript des Installationsprogramms |
| `website` | Präsentationsseite, Datenschutzerklärung und Nutzungsbedingungen, in fünf Sprachen |

## Datenschutz

GGCalendar hat keine eigenen Server und erhebt keine Telemetrie. Ihre Daten bleiben auf Ihrem Computer und reisen, wenn Sie ein Google-Konto verbinden, nur zwischen dem Computer und Google. Die Einzelheiten stehen in der [Datenschutzerklärung](https://profgusinu.altervista.org/GGCalendar/de/privacy.html); die Nutzungsbedingungen in den [Nutzungsbedingungen](https://profgusinu.altervista.org/GGCalendar/de/terms.html). Maßgeblich sind die italienischen Texte.

## Lizenz

Copyright © 2026 Giansalvo Gusinu.

GGCalendar ist freie Software: Sie können sie unter den Bedingungen der GNU General Public License, wie von der Free Software Foundation veröffentlicht, weitergeben und/oder ändern, entweder gemäß Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren Version. Sie wird in der Hoffnung verbreitet, dass sie nützlich ist, aber **ohne jede Gewährleistung**, nicht einmal die stillschweigende Gewährleistung der Marktgängigkeit oder der Eignung für einen bestimmten Zweck. Der vollständige Text steht in der Datei [LICENSE](LICENSE).

### Zusätzliche Bedingungen (GPL v3, Abschnitt 7)

Der Name „GGCalendar“ und das Logo (Dateien `img/GGCalendar.jpg`, `img/GGCalendar_logo.jpg`, `src/GGCalendar.App/Assets/GGCalendar.ico`, `src/GGCalendar.App/Assets/GGCalendar_logo.jpg`, `website/assets/logo.jpg`) kennzeichnen die originale App des Autors:

- nach Abschnitt 7(e) gewährt die Lizenz keine Rechte zur Verwendung von Name und Logo als Marken;
- nach Abschnitt 7(c) muss, wer eine geänderte Version vertreibt, einen anderen Namen und ein anderes Logo verwenden, damit sie nicht mit dem Original verwechselt wird. Es ist erlaubt anzugeben, dass sie von GGCalendar abstammt.
