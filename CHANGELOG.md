# Novità di GGCalendar

## 1.0.3

- La descrizione degli eventi mostra la formattazione di Google Calendar: grassetto (`b`, `strong`), corsivo (`i`, `em`), sottolineato, a capo, paragrafi, titoli (`h1`, `h2`, `h3`), elenchi puntati e numerati e link; una pagina HTML intera mostra solo il suo testo (senza DOCTYPE, `html`, `head`, `body` e commenti). Gli altri tag HTML restano testo come sono scritti. Sola lettura: la modifica resta in testo semplice e Google riceve lo stesso testo di prima.
- Gli indirizzi di siti e le email nella descrizione sono cliccabili. Un clic su un link chiede conferma e mostra l'indirizzo reale prima di aprire il browser; si aprono solo link http, https e mailto.
- La ricerca guarda il testo che vedi: non trova i tag che diventano formattazione né gli indirizzi nascosti nei link, trova invece gli altri tag scritti nel testo.
- La descrizione ha un limite di 8192 caratteri.
- Allegati degli eventi: un evento può avere fino a 25 allegati, che sono link https a file (immagini, documenti, file di Google Drive). Si aggiungono nella finestra dell'evento incollando l'indirizzo (il titolo è facoltativo: se manca vale il nome del file), si vedono nella scheda dell'evento e si aprono dopo la domanda con l'indirizzo vero. Viaggiano con Google Calendar (anche quelli già presenti: al primo avvio gli eventi si scaricano di nuovo, una volta), nell'esportazione `.ics` (righe `ATTACH`) e nell'importazione (solo link https). La ricerca trova il titolo e il nome del file degli allegati.
- Alla prima apertura dopo un'installazione o un aggiornamento compare per 5 secondi una schermata iniziale con le informazioni sull'app.

## 1.0.2

- Interfaccia in cinque lingue (it, en, de, es, fr).
- Ricerca degli eventi con parole chiave.
- Abbonamento a calendari pubblici via URL (sola lettura).
- Secondo fuso orario nelle viste e negli eventi.
- Crea, elimina, rinomina e colora i calendari Google.
- Elenco delle sveglie in attesa, con annullo.
- Coda di sincronizzazione: vedi e annulla le modifiche in attesa.
- Scollega un account con il tasto destro sul suo nome.

## 1.0.1

- Le sveglie si sincronizzano con Google e con GGCalendar Mobile (Android).

## 1.0

- Prima versione per Windows 11: viste giorno, settimana e mese, promemoria,
  sincronizzazione con Google Calendar.
