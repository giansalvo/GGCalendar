<p align="center"><img src="img/GGCalendar_logo.jpg" alt="GGCalendar logo" width="128"></p>

<p align="center"><a href="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml"><img src="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml/badge.svg" alt="CI status"></a></p>

<h1 align="center">GGCalendar</h1>

<p align="center">Calendar for Windows 11 with Google Calendar sync.</p>

<p align="center">
  <a href="https://github.com/giansalvo/GGCalendar/releases/latest">Download the latest version</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/en/index.html">Website</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/en/privacy.html">Privacy</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/en/terms.html">Terms</a> ·
  <a href="LICENSE">GPL v3 or later licence</a>
</p>

<p align="center">🌐 <a href="README.md">Italiano</a> · English · <a href="README.de.md">Deutsch</a> · <a href="README.es.md">Español</a> · <a href="README.fr.md">Français</a></p>

---

## Features

- Day, week, 6-day, month, year and schedule views; weekends can be hidden
- Create, move and resize events by dragging, also between different calendars
- Summary card when you click an event (edit, delete, email the guests, export, join Meet); double-click to edit it
- Recurring events, with edit or delete of a single occurrence, of the following ones or of the whole series
- Reminders with notification, sound alarm (until you switch it off, 2 minutes at most) and snooze, kept also after a restart
- Two-way sync with one or more Google Calendar accounts, with conflict handling; alarms travel with the event on Google and are shared with [GGCalendar Mobile](https://github.com/giansalvo/GGCalendarMobile) for Android
- Guests, Google Meet links, email reminders (sent by Google)
- Local calendars, import and export of `.ics` files
- Light and dark theme
- Interface in Italian, English, German, Spanish and French: the language is chosen in *Settings* (default: the language of Windows) and changes at once

## Installation

Download `GGCalendar-v<version>-Setup.exe` from the [Releases](https://github.com/giansalvo/GGCalendar/releases/latest) page and run it. The installer speaks the language of Windows when it is one of the five above (English otherwise).

- Requirements: 64-bit Windows 11. The .NET runtime is included, no other components are needed.
- By default it installs only for the current user, without administrator rights.
- The installer is not digitally signed: if Windows SmartScreen shows "Windows protected your PC", choose **More info → Run anyway**.

The app's data is in `%AppData%\GGCalendar` and is not deleted by uninstalling.

## Building from source

Requirements: [.NET SDK 10](https://dotnet.microsoft.com/download) on Windows.

```powershell
git clone https://github.com/giansalvo/GGCalendar.git
cd GGCalendar
dotnet run --project src/GGCalendar.App
dotnet test
```

### Google credentials

The OAuth credentials of the official app are not in the repository. To use sync with a build you compiled yourself you need your own Google Cloud project:

1. In [Google Cloud Console](https://console.cloud.google.com/) enable the **Google Calendar API**.
2. Configure the OAuth consent screen with the scope `https://www.googleapis.com/auth/calendar`.
3. Create an OAuth client ID of type **Desktop app** and download the JSON file.
4. Copy the file into an `OAuth/` folder in the root of the repository, with a name that starts with `client_secret` (the folder is excluded from git): it will be included in the executable when you build.

Alternatively you can enter the client ID and secret in the app, in *Settings → Google accounts → Advanced*.

### Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) and the Google credentials described above.

```powershell
.\installer\build-installer.ps1
```

The script publishes the self-contained app for `win-x64` and creates `website/downloads/GGCalendar-v<version>-Setup.exe`, taking the version from `InformationalVersion` in `src/GGCalendar.App/GGCalendar.App.csproj`.

## Project structure

| Folder | Contents |
|---|---|
| `src/GGCalendar.Core` | Models, recurrences (RRULE), ICS parser and writer, reminder calculation, layout of overlapping events, texts in the five languages (`Localization`) |
| `src/GGCalendar.Data` | SQLite store of calendars and events, queue of changes to send, settings, reminder state |
| `src/GGCalendar.Providers.Google` | OAuth sign-in (PKCE, loopback redirect) and Google Calendar API client |
| `src/GGCalendar.App` | [Avalonia](https://avaloniaui.net/) interface (MVVM), sync, notifications and sounds |
| `tests/GGCalendar.Core.Tests` | xUnit tests |
| `installer` | Inno Setup script and installer build script |
| `website` | Presentation site, privacy policy and terms, in five languages |

## Privacy

GGCalendar has no servers of its own and collects no telemetry. Your data stays on your computer and, if you connect a Google account, travels only between the computer and Google. The details are in the [privacy policy](https://profgusinu.altervista.org/GGCalendar/en/privacy.html); the conditions of use are in the [terms of service](https://profgusinu.altervista.org/GGCalendar/en/terms.html). The Italian texts are the reference ones.

## Licence

Copyright © 2026 Giansalvo Gusinu.

GGCalendar is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License or (at your option) any later version. It is distributed in the hope that it will be useful, but **without any warranty**, without even the implied warranty of merchantability or fitness for a particular purpose. The full text is in the [LICENSE](LICENSE) file.

### Additional terms (GPL v3, section 7)

The name "GGCalendar" and the logo (files `img/GGCalendar.jpg`, `img/GGCalendar_logo.jpg`, `src/GGCalendar.App/Assets/GGCalendar.ico`, `src/GGCalendar.App/Assets/GGCalendar_logo.jpg`, `website/assets/logo.jpg`) identify the author's original app:

- under section 7(e), the licence grants no right to use the name and the logo as trademarks;
- under section 7(c), whoever distributes a modified version must use a different name and logo, so that it is not confused with the original. It is allowed to state that it derives from GGCalendar.
