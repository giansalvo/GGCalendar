<p align="center"><img src="img/GGCalendar_logo.jpg" alt="Logo di GGCalendar" width="128"></p>

<p align="center"><a href="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml"><img src="https://github.com/giansalvo/GGCalendar/actions/workflows/ci.yml/badge.svg" alt="Stato della CI"></a></p>

<h1 align="center">GGCalendar</h1>

<p align="center">Calendario per Windows 11 con sincronizzazione Google Calendar.</p>

<p align="center">
  <a href="https://github.com/giansalvo/GGCalendar/releases/latest">Scarica l'ultima versione</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/">Sito</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/privacy.html">Privacy</a> ·
  <a href="https://profgusinu.altervista.org/GGCalendar/terms.html">Termini</a> ·
  <a href="LICENSE">Licenza GPL v3 o successive</a>
</p>

<p align="center">🌐 Italiano · <a href="README.en.md">English</a> · <a href="README.de.md">Deutsch</a> · <a href="README.es.md">Español</a> · <a href="README.fr.md">Français</a></p>

---

## Funzionalità

- Viste giorno, settimana, 6 giorni, mese, anno e programma; weekend nascondibili
- Creazione, spostamento e ridimensionamento degli eventi con il trascinamento, anche tra calendari diversi
- Scheda riassuntiva al clic su un evento (modifica, elimina, condividi, partecipa a Meet); doppio clic per modificarlo
- Condividi un evento dalla sua scheda: WhatsApp, Telegram, email (titolo come oggetto, testo nel corpo), email agli invitati (solo se ci sono), file `.ics` su disco, appunti e le altre app di Windows (testo e file `.ics`)
- Eventi ricorrenti, con modifica o eliminazione di una sola occorrenza, delle successive o dell'intera serie
- Promemoria con notifica, sveglia sonora (fino a quando la spegni, al massimo 2 minuti) e posticipo, conservati anche dopo un riavvio
- Elenco delle sveglie (icona accanto alla lente): quelle che stanno suonando e quelle in arrivo nei prossimi 60 giorni, con il tempo rimasto; le puoi spegnere, annullare (solo su questo computer) o aprire l'evento. Le sveglie suonano una alla volta e solo con GGCalendar aperto; i promemoria dei calendari nascosti non suonano (le sveglie posticipate sì)
- Sincronizzazione bidirezionale con uno o più account Google Calendar, con gestione dei conflitti; le sveglie viaggiano con l'evento su Google e si condividono con [GGCalendar Mobile](https://github.com/giansalvo/GGCalendarMobile) per Android
- Invitati, link Google Meet, promemoria via email (inviati da Google)
- Ricerca di un testo in titolo, descrizione, posizione e invitati, su uno o più calendari, con date e parole da escludere
- Creazione ed eliminazione dei calendari sugli account Google (solo quelli di cui sei proprietario, non il principale)
- Modifica di nome e colore dei calendari: il nome per quelli locali e per i tuoi su Google, il colore per tutti (8 colori veloci e i 24 di Google)
- Scollegare un account Google dal tasto destro sul suo nome (o dalle impostazioni): una domanda dice cosa viene tolto da questo PC (calendari, eventi, modifiche non ancora inviate); l'accesso viene revocato su Google e quello che hai su Google non cambia
- Barra di stato sempre visibile con l'indicatore della sincronizzazione: un pallino verde quando tutto è inviato a Google, una clessidra con il numero di eventi quando ce ne sono in attesa. Un clic apre l'elenco delle modifiche in attesa, per calendario: puoi riprovare subito o annullarle (l'evento torna com'è su Google)
- Calendari locali, importazione ed esportazione di file `.ics`
- Tema chiaro e scuro
- Interfaccia in italiano, inglese, tedesco, spagnolo e francese: la lingua si sceglie in *Impostazioni* (di default quella di Windows) e cambia subito

## Installazione

Scarica `GGCalendar-v<versione>-Setup.exe` dalla pagina [Releases](https://github.com/giansalvo/GGCalendar/releases/latest) ed eseguilo. L'installer parla la lingua di Windows se è una delle cinque sopra (altrimenti l'inglese).

- Requisiti: Windows 11 a 64 bit. Il runtime .NET è incluso, non servono altri componenti.
- Di default installa solo per l'utente corrente, senza permessi di amministratore.
- L'installer non è firmato digitalmente: se Windows SmartScreen mostra "Windows ha protetto il PC", scegli **Ulteriori informazioni → Esegui comunque**.

I dati dell'app sono in `%AppData%\GGCalendar` e non vengono cancellati dalla disinstallazione.

## Compilare dal codice sorgente

Requisiti: [.NET SDK 10](https://dotnet.microsoft.com/download) su Windows.

```powershell
git clone https://github.com/giansalvo/GGCalendar.git
cd GGCalendar
dotnet run --project src/GGCalendar.App
dotnet test
```

### Credenziali Google

Le credenziali OAuth dell'app ufficiale non sono nel repository. Per usare la sincronizzazione con una build compilata da te serve un tuo progetto Google Cloud:

1. In [Google Cloud Console](https://console.cloud.google.com/) abilita la **Google Calendar API**.
2. Configura la schermata di consenso OAuth con l'ambito `https://www.googleapis.com/auth/calendar`.
3. Crea un ID client OAuth di tipo **App desktop** e scarica il file JSON.
4. Copia il file in una cartella `OAuth/` nella radice del repository, con un nome che inizi per `client_secret` (la cartella è esclusa da git): verrà incluso nell'eseguibile alla compilazione.

In alternativa puoi inserire ID client e secret nell'app, in *Impostazioni → Account Google → Avanzate*.

### Creare l'installer

Richiede [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) e le credenziali Google descritte sopra.

```powershell
.\installer\build-installer.ps1
```

Lo script pubblica l'app self-contained per `win-x64` e crea `website/downloads/GGCalendar-v<versione>-Setup.exe`, prendendo la versione da `InformationalVersion` in `src/GGCalendar.App/GGCalendar.App.csproj`.

## Struttura del progetto

| Cartella | Contenuto |
|---|---|
| `src/GGCalendar.Core` | Modelli, ricorrenze (RRULE), parser e writer ICS, calcolo dei promemoria, layout degli eventi sovrapposti, testi nelle cinque lingue (`Localization`) |
| `src/GGCalendar.Data` | Archivio SQLite di calendari ed eventi, coda delle modifiche da inviare, impostazioni, stato dei promemoria |
| `src/GGCalendar.Providers.Google` | Accesso OAuth (PKCE, redirect su loopback) e client delle Google Calendar API |
| `src/GGCalendar.App` | Interfaccia [Avalonia](https://avaloniaui.net/) (MVVM), sincronizzazione, notifiche e suoni |
| `tests/GGCalendar.Core.Tests` | Test xUnit |
| `installer` | Script Inno Setup e script di build dell'installer |
| `website` | Sito di presentazione, informativa privacy e termini, in cinque lingue (i testi italiani sono quelli di riferimento) |

## Privacy

GGCalendar non ha server propri e non raccoglie telemetria. I dati restano sul tuo computer e, se colleghi un account Google, viaggiano solo tra il computer e Google. I dettagli sono nell'[informativa sulla privacy](https://profgusinu.altervista.org/GGCalendar/privacy.html); le condizioni d'uso nei [termini di servizio](https://profgusinu.altervista.org/GGCalendar/terms.html).

## Licenza

Copyright © 2026 Giansalvo Gusinu.

GGCalendar è software libero: puoi ridistribuirlo e/o modificarlo secondo i termini della GNU General Public License pubblicata dalla Free Software Foundation, versione 3 della licenza o (a tua scelta) qualsiasi versione successiva. È distribuito nella speranza che sia utile, ma **senza alcuna garanzia**, nemmeno quella implicita di commerciabilità o di idoneità a uno scopo particolare. Il testo completo è nel file [LICENSE](LICENSE). I font Inter, Roboto e Plus Jakarta Sans inclusi nell'app hanno licenza SIL Open Font License 1.1: vedi [THIRD-PARTY-FONTS.md](THIRD-PARTY-FONTS.md). I loghi di WhatsApp e Telegram nel menu Condividi sono marchi dei rispettivi proprietari, usati senza modifiche solo per indicare dove si condivide: vedi [THIRD-PARTY-BRANDS.md](THIRD-PARTY-BRANDS.md); GGCalendar non è affiliato né approvato da loro.

### Termini aggiuntivi (GPL v3, sezione 7)

Il nome "GGCalendar" e il logo (file `img/GGCalendar.jpg`, `img/GGCalendar_logo.jpg`, `src/GGCalendar.App/Assets/GGCalendar.ico`, `src/GGCalendar.App/Assets/GGCalendar_logo.jpg`, `website/assets/logo.jpg`) identificano l'app originale dell'autore:

- ai sensi della sezione 7(e), la licenza non concede alcun diritto d'uso del nome e del logo come marchi;
- ai sensi della sezione 7(c), chi distribuisce una versione modificata deve usare un nome e un logo diversi, così che non sia confusa con l'originale. È consentito dichiarare che deriva da GGCalendar.
