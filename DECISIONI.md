# Scelte di progetto

Questo file raccoglie le decisioni prese durante lo sviluppo di **Alberi Storici**: cosa si è scelto e perché. Serve a non rimettere in discussione scelte già fatte e a capire il motivo di comportamenti che potrebbero sembrare strani.

**Regola:** ogni nuova scelta (di comportamento, di dati, di interfaccia o tecnica) va aggiunta qui, nella sezione giusta, con la data e il motivo. Se una scelta viene cambiata, si aggiorna la voce indicando cosa è cambiato, invece di cancellarla.

Le funzionalità sono descritte nel [README](README.md); il lavoro da fare è in [TODO.md](TODO.md).

---

## Dati e archivio

- **Archivio locale SQLite con Entity Framework Core**, in `%LOCALAPPDATA%\AlberiStorici\historic.db`. Motivo: applicazione per un solo utente, senza server, con migrazioni gestite da EF Core.
- **Backup JSON (`people.json`) riscritto a ogni modifica**, nella stessa cartella, con persone, unioni, relazioni, dinastie, eventi, opere, elenco degli allegati e nomi storici dei luoghi. Motivo: copia leggibile e recuperabile anche se il database si rovina. La scrittura è **atomica** (file temporaneo + sostituzione), così un'interruzione non lascia file troncati.
- **I file (immagini e allegati) non sono nel backup JSON né nell'esportazione dell'archivio**: vanno copiati a parte. Motivo: tenere il backup piccolo. Includerli nell'esportazione è in TODO.
- **Se il backup JSON non si può importare, non viene sovrascritto** finché non ne è stata salvata una copia. Motivo: non perdere dati.
- **Importazione dell'archivio con copia di sicurezza** ripristinata in automatico in caso di errore; vengono copiati anche i file `-wal`/`-shm`. Se fallisce anche il ripristino, si riportano entrambi gli errori con il percorso della copia.
- **Salvataggio non riuscito = nessuna modifica visibile**: il modulo viene compilato su una copia della scheda, e l'originale si aggiorna solo a salvataggio riuscito.
- **Date come testo libero**, per accettare date storiche approssimative o non standard («c. 1540», «44 a.C.»). Per ordinare si ricava un anno di riferimento con `HistoricalYear` (gestisce a.C./d.C.; le date senza anno vanno in fondo).
- **Più ipotesi di data separate da `;`**; gli URL nel campo Fonti, nello stesso ordine, vengono associati alle rispettive date.
- **Errori imprevisti** mostrati all'utente e registrati in `%LOCALAPPDATA%\AlberiStorici\error.log`.

## Impostazioni

- **Preferenze in `settings.json`** (`%LOCALAPPDATA%\AlberiStorici`): tema, modalità lettura/modifica, chiave Europeana, import degli eventi del personaggio. Ogni modifica rilegge il file prima di scriverlo, per non perdere le altre impostazioni; se il file non è leggibile si usano i valori predefiniti senza errori.
- **Modalità di apertura predefinita: editor** (non la scheda di lettura), per mantenere il comportamento storico.
- **Chiave Europeana facoltativa**: vuota = chiave dimostrativa pubblica.

## Persone e dinastie

- **Persone collegate alle dinastie per identificativo** (tabella PersoneDinastie), non per nome: rinominando una dinastia si aggiornano le schede.
- **Più famiglie separate da `;`** («Asburgo; Lorena»): la persona compare sotto ciascuna dinastia, anche in mappa e linea del tempo.
  - *Aggiornamento 2026-11-04*: **se una delle famiglie è un ramo dell'altra, la persona compare solo nel ramo** (es. Carlo V, con «Asburgo di Spagna; Casa d'Asburgo», compare solo sotto Asburgo di Spagna, dentro Casa d'Asburgo). Motivo: aprendo la dinastia la stessa persona compariva due volte, una sotto la dinastia e una nel ramo, e sembrava un doppione. Il conteggio della dinastia la conta comunque una volta. Due dinastie indipendenti («Asburgo; Lorena») continuano a mostrarla in entrambe.
- **Nome nuovo nel campo Famiglia → nuova dinastia registrata al salvataggio.**
- **Controllo doppioni al salvataggio di una nuova scheda**: stesso codice Wikidata, stessa voce Wikipedia, o stesso nome con anni compatibili. Si chiede conferma invece di bloccare.
  - *Cambiato 2026-11-04*: **i doppioni certi sono vietati**: stesso codice Wikidata, stessa voce Wikipedia o stesso profilo WikiTree. Il controllo è nell'archivio (`PeopleStore.Add`/`Update`), quindi vale per il modulo e per ogni import (parenti, partecipanti, WikiTree); nel modulo il salvataggio viene rifiutato e all'import si propone solo di aprire la scheda esistente (tolti «Salva comunque» e «Importa comunque»). **Gli omonimi con anni compatibili richiedono ancora conferma**, perché possono essere persone diverse. Motivo: richiesta esplicita, i duplicati non devono essere permessi.

## Import da Wikipedia / Wikidata

- **Wikipedia in italiano come fonte principale**, con le affermazioni Wikidata collegate; si conservano precisione, fonti e valori alternativi. Le altre edizioni (de/en/fr/es/nl) servono a confrontare le date (LED di concordanza).
- **Il contesto politico storico non viene dedotto dal paese attuale**: si inserisce a parte. Motivo: un paese attuale non è l'entità politica dell'epoca.
- **Luoghi creati solo se la gerarchia è certa**; altrimenti l'utente sceglie nella scheda.
- **Famiglie Wikidata (P53) registrate come dinastie**, con la dinastia superiore da P361 (in mancanza P279; il ripiego su P279 è da verificare, vedi TODO). **Si scartano le «famiglie» che sono Stati o periodi storici** (es. «Spagna degli Asburgo»).
- **Chi non ha famiglia su Wikidata eredita quella del padre** già in archivio, anche se il padre è importato dopo (caso frequente: figli naturali).
- **Parenti, partecipanti, maestri/allievi/influenze collegati solo se già in archivio** (abbinamento per codice Wikidata). Le schede mancanti si creano solo con conferma dell'utente.
- **Eventi importati: esclusi concetti generici ed elementi senza data.** Un evento già registrato (stesso codice Wikidata) riceve solo il nuovo partecipante.
- **User-Agent con contatto** (`AlberiStorici/1.0 (github.com/msonzo1/HistoricEditor)`) e nuovi tentativi sui 429 rispettando `Retry-After`. Motivo: Wikimedia rallenta le richieste senza contatto.
- **Estrazione del luogo dall'introduzione (2026-11-04)**: dopo la preposizione («in», «im», «à», «en»…) le virgole restano nel luogo, perché in testi come «im Prinzenhof, Gent, Burgundische Niederlande» la città viene dopo l'edificio. Se il testo supera 80 caratteri si taglia alla prima virgola.
- **Luogo dall'infobox se l'introduzione non lo riporta (2026-11-04)**: se dall'introduzione manca il luogo di nascita o di morte, si legge la sezione iniziale in wikitext e si prendono i parametri dell'infobox (`birth_place`/`death_place`, «luogo di nascita», «lieu de naissance», «lugar de nacimiento», «geboorteplaats»…), ripuliti da link, template e note. Motivo: l'edizione inglese di solito mette i luoghi solo nell'infobox (es. Carlo V), quindi senza questo dato il confronto dei luoghi fra le edizioni restava incompleto. Il luogo dell'introduzione, se c'è, ha la precedenza. Costa una richiesta in più per ogni edizione a cui manca un luogo.
  - *Cambiato 2026-11-04*: la sezione iniziale si legge **sempre**, perché serve anche per le date dei titoli (voce seguente). Costa una richiesta in più per ogni edizione.
- **Date dei titoli anche dalle infobox di Wikipedia (2026-11-04)**: oltre a Wikidata (P39/P97), i periodi di regno delle infobox it/en/fr/es/nl diventano altre fonti del titolo («Wikipedia it · infobox»…), visibili nel LED di concordanza. La Wikipedia tedesca non ha infobox per i sovrani. Parametri letti: `titolo`/`inizio regno`/`fine regno` (it), `succession`/`reign` e `office`/`term_start`/`term_end` (en), `fonction`/`à partir du fonction`/`jusqu'au fonction` (fr), `título`/`reinado` e `cargo`/`inicio`/`final` (es), `functie`/`regeerperiode` (nl), con il numero del titolo in fondo.
  - **Abbinamento solo tramite Wikidata, mai per somiglianza del testo**: la pagina collegata nel nome del titolo dell'infobox viene risolta nel suo elemento Wikidata. Vale se è il titolo stesso («Holy Roman Emperor») oppure un elenco che lo indica con P360 («Imperatori del Sacro Romano Impero»). Se lo stesso titolo compare più volte, vale il primo periodo che si sovrappone agli anni del titolo. Motivo: i nomi dei titoli cambiano da una lingua all'altra, e un abbinamento sbagliato metterebbe date errate.
  - **La data si completa solo se le fonti concordano**: una data Wikidata meno precisa («1519») prende quella più precisa delle fonti dello stesso anno («28/06/1519»), purché nessuna fonte compatibile indichi un giorno o un mese diverso. Se manca, si riempie solo se tutte le fonti concordano. Le fonti di anni diversi (es. es.wikipedia: 1520) non contano, ma restano visibili nel LED. Motivo: caso di Carlo V imperatore, che su Wikidata aveva solo l'anno, mentre le voci riportano l'elezione del 28 giugno 1519.
- **Completamento dell'import su richiesta (2026-11-04)**:
  - dopo il salvataggio di una scheda importata, **i parenti mancanti non vengono più proposti in automatico**. Nella tab «Parentele e relazioni» compare il pulsante «Importa parenti mancanti…», visibile solo se Wikidata o WikiTree indicano ancora parenti o eventi da importare. Motivo: la finestra automatica interrompeva il lavoro;
  - **l'import degli eventi del personaggio è facoltativo**: casella nella finestra di import, **disattivata in modo predefinito** e ricordata in `settings.json` (`ImportPersonEvents`). Motivo: l'operazione può richiedere diversi minuti;
  - **«Salta» chiude senza importare né collegare nulla** (prima completava comunque le parentele WikiTree e gli eventi);
  - la barra di avanzamento degli eventi procede anche all'interno di ogni personaggio, non solo tra un personaggio e l'altro.

## WikiTree

- **Profilo WikiTree letto da Wikidata (P2949) o inserito a mano.** Le parentele indicate da WikiTree tra schede già in archivio vengono completate; i parenti presenti solo su WikiTree sono proposti tra i parenti mancanti e, senza voce su Wikipedia in italiano, ricevono una scheda con i dati WikiTree.

## Luoghi

- **Gerarchia amministrativa attuale mappata Stato per Stato** dai tipi Wikidata (P31) in `GeoSchemas.LevelTypesByCountry`. Alcuni livelli intermedi vengono saltati di proposito (es. Regierungsbezirk in Germania, comarche in Spagna, distretti turchi), come indicato in TODO.
- **Il luogo senza coordinate usa quelle del luogo che lo contiene** sulla mappa (posizione approssimata).
- **Nomi storici dei luoghi con periodo**: le schede di lettura mostrano il nome dell'epoca, con quello attuale tra parentesi: «Costantinopoli (oggi Istanbul)».
- **Mappa di base OpenStreetMap attuale, non storica.**

## Calendari

- **Ogni data può indicare calendario (giuliano/gregoriano) e stile di inizio anno** (fiorentino, pisano, veneto, Natività, bizantino). **Ordinamenti, mappa e controlli usano l'anno moderno.**

## Opere d'arte e allegati

- **Le immagini si comprimono** e l'originale non viene toccato: copia JPEG con lato massimo 2000 px, qualità 85, orientamento EXIF applicato, trasparenze su fondo bianco, più una miniatura da 480 px. Motivo: archivio leggero e apertura veloce.
- **Le immagini non più usate vengono eliminate** in automatico.
- **Immagini online scaricate solo se di pubblico dominio.**
- **La prima immagine di un personaggio diventa il suo ritratto** (modificabile con «Usa come ritratto»).

## Interfaccia

- **Lingua dell'interfaccia: italiano.**
- **Sezioni laterali** Persone, Dinastie, Cariche, Eventi, Opere, Luoghi (Ctrl+1…6), con un interruttore globale Lettura/Modifica (Ctrl+M).
- **Tema chiaro e scuro solo con risorse del tema**, niente colori fissi; applicato anche a barre di scorrimento, menu contestuali e tooltip.
- **Colori dei messaggi**: verde per gli esiti positivi, rosso per gli errori, grigio per le informazioni.
- **Accessibilità da tastiera**: anello di focus visibile solo con la navigazione da tastiera, menu contestuale anche con il tasto Menu o Maiusc+F10, elenchi accessibili sincronizzati con mappa e linea del tempo.
- **Note formattate** (grassetto, corsivo, sottolineato, elenchi); le note in testo semplice già presenti restano valide e le esportazioni testuali usano il testo senza formattazione.

## Codice

- **C# e WPF su .NET 10**, solo Windows.
- **Commenti e nomi nel dominio in italiano** (es. `Dinastia`, `Evento`, `Opera`).
- **Un solo contesto database (`AppDbContext`) per sessione**, gestito da `PeopleStore`, con `ChangeTracker.Clear()` dopo l'apertura. Contesti a vita più breve sono stati valutati in TODO.
- **Test con xUnit** in `HistoricEditor.Tests`; le regole di parsing dei testi di Wikipedia (anni, luoghi) hanno test con esempi reali in più lingue.
