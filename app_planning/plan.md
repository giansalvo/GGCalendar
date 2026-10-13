# GGCalendar (desktop): supporto a `ggAlarm`, per le sveglie condivise con GGCalendar Mobile

Piano del 13/10/2026, **da approvare prima di scrivere codice**. Nessun file del progetto è stato modificato per
prepararlo: ho solo letto il codice (sola lettura) e creato questa cartella con questo file.

## 1. Perché

GGCalendar Mobile (Android) distingue tre tipi di promemoria: notifica, **sveglia** ed email. Google Calendar conosce
solo notifica (`popup`) ed email, quindi Android salva le sveglie così: ogni sveglia è un promemoria `popup` normale
più una proprietà privata dell'evento, `extendedProperties.private.ggAlarm`, che elenca i minuti delle notifiche che
sono sveglie.

Il desktop ha lo stesso tipo di promemoria (`ReminderMethod.Alarm`), ma oggi lo tiene solo in locale e lo ritrova dopo
una sincronizzazione grazie a `EventReminder.KeepLocalOptions` (una notifica di Google prende il tipo e il suono del
promemoria locale con gli stessi minuti). Conseguenze di oggi:

- una sveglia impostata su **Android** arriva sul desktop come semplice notifica;
- una sveglia impostata sul **desktop** arriva su Android come semplice notifica, e se l'evento viene sincronizzato da
  un altro computer la sveglia si perde.

Con questo lavoro la sveglia diventa un dato che viaggia su Google, come su Android.

## 2. Formato (deciso e già in uso su Android, da rispettare alla lettera)

- Proprietà: `extendedProperties.private.ggAlarm`, una stringa.
- Contenuto: i minuti, separati da virgola e in ordine crescente, delle notifiche che sono sveglie. Esempio: `"10,30"`.
- **Nessuna sveglia: stringa vuota `""`, mai `null`.** Google rifiuta `null` con un errore 400 ("Required"): è già
  successo su Android.
- La lettura accetta spazi (`"10, 30"`) e ignora i valori che non sono numeri.
- Le sveglie restano promemoria `popup` nel campo `reminders.overrides` (così Google e gli altri client le vedono come
  notifiche). La proprietà dice solo quali di quei popup sono sveglie.
- La proprietà è *privata*: vive nella copia dell'evento di chi la imposta. Un invitato che riceve l'evento non la
  riceve (comportamento già noto e voluto per Android).
- Un evento può avere al massimo un promemoria per ogni numero di minuti tra le sveglie: se sul desktop ci sono una
  notifica e una sveglia con gli stessi minuti, la proprietà non può distinguere i due casi (limite del formato; vince la
  sveglia). Caso raro, da segnalare nel testo dell'interfaccia o ignorare (vedi domande aperte).

## 3. Cosa cambia nel codice

File letti: `Core/Models/CalendarModels.cs`, `Providers.Google/GoogleEventMapper.cs`, `App/Services/GoogleSyncService.cs`
(righe ~170-195 e ~325-340), `tests/GGCalendar.Core.Tests/EventReminderTests.cs` e `GoogleEventMapperTests.cs`.
Sono tutti interventi piccoli e locali.

### 3.1 `GGCalendar.Core` — `CalendarModels.cs` (`EventReminder`)
1. `AlarmProperty` = `"ggAlarm"` (costante, unico posto dove compare il nome).
2. `FormatAlarmMinutes(reminders)` → stringa `"10,30"` o `""`: i minuti dei promemoria `Alarm`, senza doppioni, in ordine.
3. `ParseAlarmMinutes(string?)` → `HashSet<int>` (spazi tollerati, valori non numerici ignorati).
4. `ApplyAlarmMinutes(remote, minutes)`: ogni popup di Google i cui minuti sono nell'insieme diventa `Alarm`
   (consumando un minuto alla volta: due popup con gli stessi minuti diventano una sola sveglia + un popup); le email non
   si toccano.
5. `KeepLocalSounds(remote, local)`: copia il suono (`SoundPath`) dal promemoria locale con stesso tipo e stessi minuti.
   È ciò che resta di `KeepLocalOptions` quando Google ha già detto quali sono le sveglie.
6. `KeepLocalOptions` resta com'è, come **ripiego** per gli eventi che non hanno la proprietà (creati prima, o da
   programmi che non la scrivono).

### 3.2 `GGCalendar.Core` — `CalendarEvent`
- Nuova proprietà `bool AlarmsFromProvider { get; init; }` (`[JsonIgnore]`, mai salvata come i `OccurrenceIndex`):
  vero se Google ha mandato `ggAlarm` per quell'evento, anche vuota. Serve al servizio di sincronizzazione per capire se
  la proprietà è la fonte di verità o se va usato il ripiego.

### 3.3 `GGCalendar.Providers.Google` — `GoogleEventMapper.cs`
- `FromGoogle`: se `extendedProperties.private.ggAlarm` esiste (anche `""`), applica `ApplyAlarmMinutes` ai promemoria
  letti e imposta `AlarmsFromProvider = true`. Vale anche per le singole occorrenze modificate di una serie. Gli eventi
  annullati (tombstone) non cambiano.
- `ToGoogle`: aggiunge
  `["extendedProperties"] = { ["private"] = { ["ggAlarm"] = EventReminder.FormatAlarmMinutes(ev.Reminders) } }`,
  sempre, anche con stringa vuota (così nel PATCH una sveglia tolta sul desktop viene davvero tolta). Il blocco
  `reminders` non cambia: le sveglie escono già come `popup`.

### 3.4 `GGCalendar.App` — `GoogleSyncService.cs` (due punti)
- Riga ~185 (pull): `mapped.AlarmsFromProvider ? KeepLocalSounds : KeepLocalOptions`.
- Riga ~336 (`ResolveConflict`): stessa scelta per la versione di Google tenuta dopo un conflitto.
- Il resto della sincronizzazione (outbox, ETag, 412, `EventContent.Same`) non cambia: il confronto del contenuto non
  guarda i promemoria, quindi cambiare solo una sveglia non crea conflitti finti (da confermare con un test, vedi 4).

### 3.5 Ricarica una tantum dopo l'aggiornamento
Il desktop sincronizza in modo incrementale (`syncToken`): gli eventi che non cambiano su Google non vengono riletti,
quindi le sveglie create su Android *prima* di questo aggiornamento non arriverebbero mai. Soluzione, come già fatto su
Android: al primo avvio della nuova versione si azzerano i `syncToken` (una ricarica completa), una sola volta, tenendo
un'impostazione "versione dello schema di sincronizzazione" in `SettingsStore`. **Da verificare in lettura** (non ancora
guardato): dove stanno i `syncToken` (`CalendarStore`, tabella `sync_state`) e come forzare una sincronizzazione completa
senza perdere le modifiche locali in coda.

### 3.6 Interfaccia e testi
Nessuna schermata nuova: il tipo "Sveglia" esiste già nell'editor dei promemoria. Controllare solo che testi e guida non
dicano che le sveglie "esistono solo in questa app". Eventuale riga nel README (sezione sincronizzazione) e nel sito
(`website/index.html`) sul fatto che le sveglie si condividono con GGCalendar Mobile: **solo se lo vuoi**, a parte.

## 4. Test (xUnit, `tests/GGCalendar.Core.Tests`)
Nuovi, accanto a quelli esistenti (`EventReminderTests`, `GoogleEventMapperTests`):

1. `FormatAlarmMinutes`: ordine, doppioni, nessuna sveglia → `""`, email ignorate.
2. `ParseAlarmMinutes`: `"10,30"`, `"10, 30"`, `""`, `null`, `"10,abc,30"`.
3. `ApplyAlarmMinutes`: popup → sveglia, email intatte, due popup con gli stessi minuti, minuti non presenti.
4. `FromGoogle`: proprietà assente → `AlarmsFromProvider` falso, promemoria invariati; presente e vuota → `true` e tutto
   popup; `"10,30"` → due sveglie; con evento che usa i promemoria predefiniti del calendario; con occorrenza modificata.
5. `ToGoogle`: la proprietà c'è sempre, `""` senza sveglie, `"10,30"` con due sveglie; il PATCH non manda mai `null`.
6. Andata e ritorno desktop → JSON → desktop, e un JSON nel formato di Android → desktop (campione preso dai test
   Android, `GoogleEventMapperTest`), compreso il suono locale che sopravvive.
7. Regressione di `KeepLocalOptions` (resta per gli eventi senza proprietà) e conferma che `EventContent.Same` ignora la
   differenza di sola sveglia.
8. Ricarica una tantum (3.5): se si riesce a isolarla in una funzione pura, un test; altrimenti prova a mano.

Comando: `dotnet test` dalla radice. Prima dei cambiamenti lo lancio una volta per sapere se parto da una base verde.

## 5. Prova a mano con il telefono (solo eventi creati apposta, nel calendario principale di profgusinu@gmail.com)
1. Android → desktop: evento con sveglia a 10 minuti e notifica a 30, sincronizzare il desktop: compare una sveglia a 10
   e una notifica a 30.
2. Desktop → Android: creare un evento con una sveglia, sincronizzare Android: compare nella schermata "Sveglie attive".
3. Togliere la sveglia su un lato e vedere che sparisce anche sull'altro, senza messaggi di conflitto.
4. Evento con email + sveglia + notifica; serie con un'occorrenza modificata; evento di tutto il giorno.
5. Dopo l'aggiornamento: una sveglia creata su Android *prima* compare sul desktop (verifica della ricarica 3.5).
Dopo ogni prova si cancellano gli eventi di prova.

## 6. Git, rilascio e rischi
- Lavoro su un **ramo nuovo** (`ggalarm-android`, da `master`, ora pulito all'ultimo commit `013e5c1`), commit piccoli, in
  inglese come il resto della cronologia; **nessun push** e nessun merge senza il tuo ok.
- Nessuna modifica a OAuth, scope, installer o versione (oggi 1.0.0). Se vuoi pubblicare una versione 1.0.1 con la
  novità, è un passo a parte (installer, release) che decidi tu.
- Rischi: (a) eventi con notifica e sveglia agli stessi minuti (limite del formato); (b) vecchie versioni del desktop
  ignorano la proprietà senza danni, ma se modificano un evento la riscrivono con un valore diverso (nessuno, in pratica,
  perché non la leggono: in un PATCH Google non la tocca se non la mandiamo); (c) la ricarica una tantum deve rispettare
  le modifiche locali in coda.
- Il formato non cambia nulla per chi non usa Android: senza telefono tutto resta come prima.

## 7. Domande aperte (da decidere con te)
1. Notifica e sveglia con gli stessi minuti sul desktop: accettare il limite in silenzio, oppure impedirlo
   nell'editor con un avviso? (Consiglio: accettarlo, è rarissimo.)
2. Vuoi il testo sul sito e nel README ora, o dopo?
3. Pubblicare subito una 1.0.1, o lasciare la novità sul ramo fino a quando serve?

## 8. Ordine di lavoro proposto
1. Base verde (`dotnet test`) e ramo nuovo.
2. `EventReminder`: funzioni 3.1 con i test 1-3.
3. `GoogleEventMapper` (`FromGoogle`, `ToGoogle`) con i test 4-6.
4. `GoogleSyncService` (due punti) con il test 7.
5. Ricarica una tantum (3.5), dopo aver letto `CalendarStore` e `SettingsStore`.
6. Prove a mano con il telefono (sezione 5).
7. Aggiornare questo file con l'esito e proporre il merge.

## 9. Esito (13/10/2026)
Approvato dall'utente il 13/10/2026 ("aggiorna app, sito, README, installer; lascia tutto nella versione 1.0").
Fatto sul ramo `ggalarm-android` (locale, **nessun push**):
- `EventReminder`: `AlarmProperty`, `FormatAlarmMinutes`, `ParseAlarmMinutes`, `ApplyAlarmMinutes`, `KeepLocalSounds`
  (`KeepLocalOptions` resta come ripiego); `CalendarEvent.AlarmsFromProvider`.
- `GoogleEventMapper`: legge `extendedProperties.private.ggAlarm` (anche vuota) e la scrive sempre (`""` senza sveglie).
- `GoogleSyncService`: `KeepLocalReminderOptions` nei due punti (pull e risoluzione dei conflitti).
- Ricarica una tantum: `AppSettings.SyncSchema` (0 → 1); al primo avvio `MainWindow` azzera i `syncToken` e salva
  l'impostazione. Le modifiche locali in coda non si perdono: la sincronizzazione invia prima l'outbox e il pull salta
  gli eventi con modifiche in coda.
- Test: 15 nuovi in `AlarmPropertyTests.cs` (formato, lettura, formato di Android, andata e ritorno, suono locale,
  sveglia tolta, eccezione di una serie, evento di tutto il giorno, evento con promemoria predefiniti, `EventContent.Same`);
  in tutto 71 test, tutti superati (prima 56).
- README, sito (`website/index.html`, due schede) e testo dell'editor dei promemoria: aggiunta la frase sulle sveglie
  condivise con GGCalendar Mobile.
- Installer: ricostruito con `installer/build-installer.ps1` (versione **1.0**, invariata), file
  `website/downloads/GGCalendar-v1.0-Setup.exe` (non tracciato da git).
Domande aperte del punto 7: risposte implicite (limite accettato in silenzio, sito e README ora, versione 1.0 invariata).
**Da fare con il telefono**: le prove a mano della sezione 5, e pubblicare il sito/installer se vuoi (non l'ho fatto).

## 10. Prove desktop + Android insieme (13/10/2026, evento "doze" del calendario principale di profgusinu@gmail.com, indicato dall'utente)
Eseguite con un programma di prova a parte (fuori dal repository) che usa il vero codice di sincronizzazione del desktop su un database
temporaneo (l'elenco degli account è solo letto) e con il Samsung. Stato iniziale su Google (scritto da Android): quattro sveglie, 0, 60, 90 e 120 minuti.
1. **Android → desktop**: il desktop legge le quattro sveglie dalla proprietà `ggAlarm` (prima questo arrivava come notifiche): OK.
2. **Desktop → Android**: dal desktop si impostano sveglia 90, notifica 120, sveglia 30, email 60; dopo la sincronizzazione Android mostra
   "Sveglia · 30 minuti prima", "Email · 1 ora prima", "Sveglia · 90 minuti prima", "Notifica · 2 ore prima": OK. Nessun conflitto.
3. **Sveglia tolta su Android** (30 minuti, dall'editor): il desktop dopo la sincronizzazione vede sveglia 90, notifica 120, email 60: OK.
4. **Ripristino**: dal desktop si rimettono le quattro sveglie originali; Android le mostra tutte come sveglie: OK.
Non provati: serie con occorrenza modificata, evento di tutto il giorno e la ricarica una tantum con un database esistente (coperti solo dai test automatici).
L'evento "doze" è tornato com'era.
