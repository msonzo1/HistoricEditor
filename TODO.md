# TODO

## Personaggi
- [x] Modificare una scheda esistente (editor a tab «Modifica scheda»).
- [x] Eliminare una scheda.
- [x] Gestire la famiglia anche durante l'inserimento di un nuovo personaggio (tab «Parentele» nell'editor, dopo il primo salvataggio).
- [x] Nomi dei familiari cliccabili nella scheda biografica, per passare alla scheda del parente.
- [x] Titoli (con numerazione, periodo e cause di inizio/fine), soprannome e titolo principale; separazione automatica di nome e suffisso (es. «Carlo V»).
- [x] Nomi in altre lingue consultabili dalla scheda (selettore «Lingua visualizzata»).
- [x] Modificare i nomi nelle altre lingue (selettore «Lingua visualizzata»: modifica, aggiunta e rimozione di una lingua).
- [x] Pulsante «Aggiorna da Wikipedia» per reimportare una scheda già salvata tramite il suo WikidataId.
- [x] Scheda di lettura («Visualizza» nella barra e nel menu contestuale): tutte le informazioni in una pagina senza campi modificabili; parenti, relazioni, eventi, fonti e titoli cliccabili (il titolo apre la cronologia della carica); pulsante «Modifica…».

## Dinastie
- [x] Dinastie con rami (es. Asburgo → Asburgo di Spagna): finestra «Dinastie…», vista Famiglie annidata, conteggi cumulativi, backup JSON.
- [x] Campo Famiglia della scheda come scelta tra le dinastie (o nome nuovo, registrato al salvataggio), con pulsante «Dinastia…» e indicazione del ramo.
- [x] Import: famiglie da Wikidata (P53) registrate come dinastie, con dinastia superiore da P361 (in mancanza P279).
- [x] Collegare la scheda alla dinastia tramite identificativo: tabella PersoneDinastie (migrazione DynastyLinks, popolata dai nomi esistenti); la rinomina segue il collegamento.
- [x] Persone con più famiglie («Asburgo; Lorena»): compaiono sotto ciascuna dinastia (anche in mappa e linea del tempo).
- [ ] Verificare il ripiego su P279 («sottoclasse di»): può collegare rami non corretti.
- [ ] Periodo, capostipite e luogo d'origine della dinastia.

## Import da Wikipedia / Wikidata
- [x] Leggere anche le Wikipedia tedesca/inglese/francese/spagnola/olandese: date discordanti tra lingue/fonti segnalate nella scheda (avviso + dettagli) e salvate.
- [x] Nomi dei luoghi multilingua (it/en/de/fr/es/nl) per ogni livello, salvati sul luogo e consultabili dalla scheda.
- [~] Importare padre (P22), madre (P25), coniugi (P26) e figli (P40): oggi vengono collegati solo se già in archivio (match per WikidataId). Manca la creazione automatica delle schede mancanti.
- [x] Importare le coordinate dei luoghi (P625) per compilare latitudine/longitudine.
- [x] WikiTree come fonte aggiuntiva: profilo collegato alla scheda, parentele completate tra schede in archivio, parenti mancanti proposti (anche senza voce italiana).
- [ ] WikiTree: confrontare anche le sue date con quelle delle Wikipedia (LED di concordanza).
- [x] User-Agent con contatto (email) nelle richieste a Wikipedia/Wikidata; ritenta su 429 rispettando Retry-After. Valutare se spostare il contatto in impostazioni.
- [ ] Riconoscere i luoghi specifici anche tramite il tipo Wikidata (P31): oggi un monastero/castello agganciato direttamente a una provincia viene scambiato per un comune.
- [ ] Ricavare automaticamente il tipo del luogo specifico (monastero, reggia, chiesa…) da Wikidata.
- [x] Migliorare l'estrazione degli anni dall'introduzione di Wikipedia (salta le parentesi senza anni come la pronuncia, gestisce a.C./BC/v. Chr./av. J.-C. con anni negativi).
- [x] Rendere cliccabile l'URL della fonte Wikipedia (oggi è solo testo).
- [x] Avanzamento a passi (con percentuale) durante l'import da Wikipedia.
- [x] LED di concordanza cliccabile: elenco delle edizioni confrontate con i collegamenti alle pagine.

## Luoghi
- [ ] Cambiare il luogo contenitore di un luogo storico dopo la creazione.
- [ ] Modificare un luogo specifico direttamente dalla finestra di scelta (oggi solo da "Gestisci luoghi").
- [x] Ampliare l'elenco delle suddivisioni senza livello inferiore (comunità autonome spagnole uniprovinciali, Bruxelles, Valle d'Aosta, Parigi/Lione, Azzorre/Madera, cantoni svizzeri senza distretti, Republika Srpska/Brčko).
- [x] Livelli dedotti da tabella quando mancano in P131 (regioni di Norvegia e terre di Svezia).
- [ ] Lavorare sui contesti storici dei luoghi di nascita/morte/sepoltura (nome all'epoca, entità politica, unità, periodo).

### Mappatura Stato per Stato dei tipi Wikidata (P31) delle suddivisioni (`GeoSchemas.LevelTypesByCountry`)
- [x] Italia (regione, provincia/città metropolitana/libero consorzio, comune).
- [x] Francia metropolitana e dipartimenti d'oltremare (regione, dipartimento, arrondissement, comune).
- [x] Germania (Land, circondario/città extracircondariale, comune; Regierungsbezirk saltato).
- [ ] Francia d'oltremare (Polinesia, Nuova Caledonia, Saint-Pierre-et-Miquelon…).
- [x] Spagna (comunità autonoma/città autonoma, provincia, comune; comarche saltate; Madrid, Ceuta e Melilla senza provincia).
- [x] Portogallo (livelli dall'infobox; tipi Wikidata come ripiego: regione autonoma, distretto, comune. Le regioni continentali non sono in P131).
- [x] Belgio (regione, provincia, arrondissement, comune; Bruxelles senza provincia; Comunità saltate).
- [x] Paesi Bassi (provincia, comune; città contenuta nel comune omonimo).
- [ ] Paesi Bassi caraibici (Bonaire, Sint Eustatius, Saba: comuni speciali).
- [x] Austria (Land, Distretto/città statutaria, Comune; Vienna e città statutarie occupano più livelli).
- [x] Regno Unito (nazione costitutiva, contea cerimoniale/non metropolitana/metropolitana o council area/principal area/distretto NI, distretto/borough/unitary authority, città/town/villaggio; Scozia, Galles e Irlanda del Nord senza distretti; senza tipi resta la profondità variabile).
- [ ] Regno Unito: luoghi come Londra che si estendono su più distretti vengono proposti con il distretto assente da confermare.
- [x] Svizzera (Cantone, Distretto, Comune; cantoni senza distretti riconosciuti per nome)
- [x] Liechtenstein (Comune)
- [x] Lussemburgo (Cantone, Comune)
- [x] Monaco (Comune, per posizione)
- [x] Andorra (Parrocchia, Comune)
- [x] San Marino (Castello come Comune)
- [x] Città del Vaticano (per posizione)
- [x] Malta (Regione, Comune)
- [x] Irlanda (schema nuovo: provincia storica, contea, città/town/villaggio).
- [x] Islanda (Regione, Municipalità, Comune)
- [x] Danimarca (Regione, Municipalità, Comune)
- [x] Norvegia (Regione dedotta dalla contea, Contea, Municipalità, Comune)
- [x] Svezia (Regione/terra dedotta dalla contea, Contea, Municipalità, Comune)
- [x] Finlandia (Regione, Comune; subregioni saltate)
- [x] Estonia (Contea, Municipalità, Comune)
- [x] Lettonia (Municipalità/città statale, Comune; parrocchie saltate)
- [x] Lituania (Contea, Municipalità, Comune)
- [ ] Lituania: comuni urbani (Vilnius, Kaunas…) che su Wikidata non hanno la contea in P131
- [x] Polonia (Voivodato, Distretto/città con diritti di distretto, Gmina, Comune)
- [x] Repubblica Ceca (Regione, Distretto, Comune; Praga su più livelli)
- [x] Slovacchia (Regione, Distretto, Comune)
- [ ] Slovacchia: Bratislava e Košice hanno il distretto assente nella catena P131
- [x] Ungheria (Provincia, Distretto, Comune)
- [ ] Ungheria: Budapest senza provincia
- [x] Slovenia (Municipalità, Comune)
- [x] Croazia (Contea, Comune)
- [ ] Croazia: Zagabria (città e contea insieme) senza contea distinta
- [x] Bosnia ed Erzegovina (Entità, Cantone, Comune; Republika Srpska e Brčko senza cantoni)
- [x] Serbia (Distretto, Comune; Voivodina e municipalità urbane saltate)
- [x] Montenegro (Municipalità, Comune)
- [x] Kosovo (Distretto, Municipalità, Comune)
- [x] Albania (Prefettura, Municipalità, Comune)
- [x] Macedonia del Nord (Municipalità, Comune)
- [x] Grecia (Periferia, Unità periferica, Municipalità, Comune; amministrazioni decentrate saltate)
- [ ] Grecia: unità periferica di Salonicco assente nella catena
- [x] Bulgaria (Provincia, Municipalità, Comune)
- [x] Romania (Distretto, Comune)
- [x] Moldavia (Distretto, Comune)
- [ ] Moldavia: municipi come Bălți senza distretto
- [x] Ucraina (Oblast, Distretto, Comunità/hromada, Comune)
- [x] Bielorussia (Regione, Comune; distretti saltati)
- [x] Russia (Soggetto federale, Comune; città federali su più livelli)
- [x] Cipro (Distretto, Comune)
- [x] Turchia (Provincia, Comune; distretti/ilçe saltati)

## Eventi
- [x] Entità Evento (titolo, tipo, data con qualificatore: esatta, circa, prima, dopo, da… a…, tra… e…) con più partecipanti e ruolo facoltativo; finestra «Eventi…», filtro per personaggio, backup JSON.
- [x] Luogo dell'evento collegato all'albero dei luoghi (campo nella finestra Eventi, import P276/P131 da Wikidata, backup JSON).
- [ ] Fonti dell'evento.
- [x] Eventi nella scheda della persona (tab «Altri eventi»: elenco, nuovo evento, apertura).
- [x] Menu contestuale sui personaggi (elenco e albero genealogico).
- [x] Cronologia nella scheda laterale Eventi.
- [x] Import di un evento da Wikipedia/Wikidata: tipo da P31, date da P585/P580/P582/P1319/P1326, partecipanti da P710 (+P4791 comandanti), P1891, P1344/P793 inversi.
- [x] Import automatico degli eventi di una persona (P793, P1344, P607) durante l'import del personaggio (esclusi concetti generici ed elementi senza data; gli altri partecipanti collegati solo se già in archivio).
- [x] Creare le schede dei partecipanti mancanti durante l'import di un evento (e dei parenti mancanti durante l'import di un personaggio), con elenco di conferma e import completo.
- [x] Controllo doppioni dei personaggi (Wikidata, voce Wikipedia, nome e anni).
- [ ] Gerarchia di eventi (es. battaglia parte di una guerra, P361).

## Strumenti per lo storico (roadmap)
- [ ] Fonti e citazioni per ogni dato (bibliografia, pagina, link, archivio) con grado di affidabilità; collegabili a schede, eventi, date e luoghi.
- [x] Cariche e titoli con periodo (re, duca, vescovo…) e cronologia dei successori/predecessori per ogni carica.
- [x] Linea del tempo di una persona o di una dinastia, con nascita, morte, cariche ed eventi.
- [x] Mappa di luoghi ed eventi (Mapsui + OpenStreetMap): nascite, morti, sepolture, eventi e tutti i luoghi con coordinate; filtri per periodo, personaggio, dinastia e tipo di evento; elenco accessibile sincronizzato con la mappa.
- [ ] Albero genealogico grafico esteso (discendenti, più generazioni) con esportazione PDF/immagine e GEDCOM (import ed export).
- [x] Calendario giuliano/gregoriano: indicare il calendario di una data e convertire tra i due (stile veneto, stile fiorentino, ab Incarnatione…).
- [x] Nomi storici dei luoghi che cambiano nel tempo (es. Costantinopoli → Istanbul) con periodo di validità: elenco modificabile nella sezione Luoghi, lettura da Wikidata (P1448, P1365), nome dell'epoca nelle schede di lettura.
- [x] Relazioni non familiari tra personaggi: alleanze, rivalità, amicizia, mecenatismo, tutela, reggenza, maestro/allievo, influenza o tipo libero, con periodo e note; sezione «Altre relazioni» nella tab «Parentele e relazioni», backup JSON, import da Wikidata (P1066, P802, P737) se l'altro personaggio è già in archivio.
- [ ] Ricerca avanzata con filtri per secolo, tipo di evento, luogo, dinastia e titolo.
- [x] Allegati: ritratti, documenti, immagini (compresse) per personaggi, eventi, opere, luoghi e dinastie; ritratto nella scheda di lettura; note formattate (grassetto, corsivo, elenchi).

## Opere d'arte
- [x] Gestore di opere d'arte (finestra «Opere d'arte», scheda laterale Opere): titolo, tipo, data libera, tecnica, dimensioni, collocazione, note, fonti; persone collegate con ruolo (autore, bottega, attribuita, committente, soggetto raffigurato). Sezione «Opere d'arte» nella scheda di lettura.
- [x] Immagini dei dipinti: **sì, si comprimono**. Copia JPEG (lato max 2000 px, qualità 85) + miniatura 480 px in `%LOCALAPPDATA%\AlberiStorici\Immagini`; l'originale non viene toccato; pulizia automatica delle immagini non usate.
- [ ] Includere la cartella `Immagini` nell'esportazione/importazione dell'archivio (ora va copiata a parte).
- [x] Import opere da Wikipedia/Wikidata (immagine da Wikimedia Commons), Metropolitan Museum, Art Institute of Chicago, Cleveland Museum of Art ed Europeana (chiave facoltativa, predefinita la chiave pubblica di prova); gli artisti mancanti vengono proposti per l'import.
- [ ] Europeana: la collocazione è solo testo (fornitore · Paese), senza gerarchia dei luoghi.
- [ ] Import da ICCD / Catalogo generale dei Beni Culturali (ArCo, SPARQL) per le opere italiane.
- [ ] Rijksmuseum e Smithsonian (richiedono una chiave API personale).
- [x] Modalità lettura / modifica per doppio clic e Invio sui personaggi (scelta ricordata in `settings.json`).

## Archivio
- [ ] Importazione di un archivio vuoto: all'avvio vengono ricaricate le schede da `people.json`; decidere se disattivare questo comportamento.
- [ ] Unire (merge) un archivio importato con quello esistente, invece di sostituirlo.
- [ ] Backup `people.json`: ora include le unioni; resta da valutare un export incrementale invece della riscrittura completa a ogni salvataggio.
- [x] Import archivio: se fallisce anche il ripristino della copia di sicurezza vengono riportati entrambi gli errori con il percorso della copia.
- [x] Import archivio: copiare anche gli eventuali file `-wal`/`-shm` dell'archivio sorgente.
- [x] Backup `people.json` scritto in modo atomico (file temporaneo + sostituzione).

## Robustezza e dati
- [x] Contesto database chiuso alla chiusura della finestra principale.
- [x] Import/Export archivio: gestiti anche gli errori di salvataggio del database.
- [x] Errori imprevisti (interfaccia, task in background, dominio applicativo) registrati in `error.log`.
- [x] Gestione errori già presente nei salvataggi di persone, unioni, luoghi, tema e nelle chiamate Wikipedia (client HTTP condiviso con timeout e nuovi tentativi su 429).
- [x] Entity Framework Core aggiornato a 10.0.13.
- [x] Contesto database (`AppDbContext`) unico per tutta la sessione: accumula entità in memoria e può restituire dati non aggiornati; valutare contesti a vita più breve.
- [x] Salvataggio non riuscito: `FillPersonFromForm` modifica comunque l'oggetto mostrato nell'elenco, che resta con dati non salvati; annullare le modifiche in caso di errore. Risolto: il modulo viene compilato su una copia e la scheda originale si aggiorna solo a salvataggio riuscito.
- [x] Ordinamento dei figli basato sul testo libero della data di nascita: "c. 1540" finisce dopo "1550"; estrarre un anno di riferimento per l'ordinamento. Risolto con `HistoricalYear` (gestisce anche a.C./d.C.; le date senza anno vanno in fondo).

## Interfaccia
- [x] Schede laterali Persone / Luoghi / Eventi: persone raggruppate per famiglia con date di nascita e morte; gestione luoghi integrata nella scheda Luoghi.
- [x] Scheda Persone: barra comandi (nuovo, apri, elimina, famiglia, coniuge, figlio, ricerca Wikipedia) al posto del pannello dettagli; vista Albero genealogico fino ai trisnonni con caselle + per aggiungere persone esistenti o nuove.
- [x] Sostituita la vecchia "A" nella colonna scura con il logo dell'app.
- [x] Scheda personaggio a tab nascoste (navigazione solo con Avanti/Indietro) e interfaccia modernizzata.
- [x] Tema chiaro/scuro impostabile da «Visualizza → Tema» (persistito in settings.json).
- [x] Controlli dei luoghi (campo luogo, albero dei luoghi) e finestra Famiglia adattati al tema chiaro/scuro: niente più colori fissi, solo risorse del tema.
- [x] Virtualizzare l'elenco delle persone (VirtualizingStackPanel su gruppi e righe, scorrimento a pixel limitato alla lista).
- [x] Applicare il tema scuro anche a barre di scorrimento, menu contestuali e tooltip.
- [x] Aggiungere un indicatore di focus da tastiera ben visibile sui pulsanti (anello d'accento solo con navigazione da tastiera).
- [x] Distinguere i messaggi di successo da quelli di errore: oggi usano lo stesso colore rosso-arancio. Risolto: verde per gli esiti positivi, rosso per gli errori, grigio per le informazioni.
- [x] Interfaccia unificata a sezioni: Persone, Dinastie, Cariche, Eventi, Opere, Luoghi (Ctrl+1…6), ognuna consultabile e con il proprio editor; interruttore globale Lettura/Modifica nell'intestazione (Ctrl+M).
- [x] Schede di sola lettura per opere d'arte ed eventi; dal pittore si passa alle sue opere (scheda di lettura, galleria filtrata per artista, pulsante «Opere d'arte…» nell'editor).
- [x] Eventi filtrabili per tipo, dinastia e persona; dalla dinastia si aprono i suoi eventi e la linea del tempo.
- [x] Import Wikidata: scartate le «famiglie» che sono Stati o periodi storici; chi non ha famiglia eredita quella del padre (es. figli naturali di Carlo V).
