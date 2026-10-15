# TODO

## Personaggi
- [x] Modificare una scheda esistente (editor a tab «Modifica scheda»).
- [x] Eliminare una scheda.
- [x] Gestire la famiglia anche durante l'inserimento di un nuovo personaggio (tab «Parentele» nell'editor, dopo il primo salvataggio).
- [ ] Nomi dei familiari cliccabili nella scheda biografica, per passare alla scheda del parente.
- [x] Titoli (con numerazione, periodo e cause di inizio/fine), soprannome e titolo principale; separazione automatica di nome e suffisso (es. «Carlo V»).
- [x] Nomi in altre lingue consultabili dalla scheda (selettore «Lingua visualizzata»).
- [ ] Modificare i nomi nelle altre lingue (oggi sono di sola consultazione, arrivano solo dall'import).
- [ ] Pulsante «Aggiorna da Wikipedia» per reimportare una scheda già salvata tramite il suo WikidataId.

## Dinastie
- [x] Dinastie con rami (es. Asburgo → Asburgo di Spagna): finestra «Dinastie…», vista Famiglie annidata, conteggi cumulativi, backup JSON.
- [x] Campo Famiglia della scheda come scelta tra le dinastie (o nome nuovo, registrato al salvataggio), con pulsante «Dinastia…» e indicazione del ramo.
- [x] Import: famiglie da Wikidata (P53) registrate come dinastie, con dinastia superiore da P361 (in mancanza P279).
- [ ] Collegare la scheda alla dinastia tramite identificativo (oggi il collegamento è per nome; richiede una migrazione).
- [ ] Persone con più famiglie («Asburgo; Lorena»): oggi finiscono in un gruppo a sé invece che sotto ciascuna dinastia.
- [ ] Verificare il ripiego su P279 («sottoclasse di»): può collegare rami non corretti.
- [ ] Periodo, capostipite e luogo d'origine della dinastia.

## Import da Wikipedia / Wikidata
- [x] Leggere anche le Wikipedia tedesca/inglese/francese/spagnola/olandese: date discordanti tra lingue/fonti segnalate nella scheda (avviso + dettagli) e salvate.
- [x] Nomi dei luoghi multilingua (it/en/de/fr/es/nl) per ogni livello, salvati sul luogo e consultabili dalla scheda.
- [~] Importare padre (P22), madre (P25), coniugi (P26) e figli (P40): oggi vengono collegati solo se già in archivio (match per WikidataId). Manca la creazione automatica delle schede mancanti.
- [x] Importare le coordinate dei luoghi (P625) per compilare latitudine/longitudine.
- [x] User-Agent con contatto (email) nelle richieste a Wikipedia/Wikidata; ritenta su 429 rispettando Retry-After. Valutare se spostare il contatto in impostazioni.
- [ ] Riconoscere i luoghi specifici anche tramite il tipo Wikidata (P31): oggi un monastero/castello agganciato direttamente a una provincia viene scambiato per un comune.
- [ ] Ricavare automaticamente il tipo del luogo specifico (monastero, reggia, chiesa…) da Wikidata.
- [ ] Migliorare l'estrazione degli anni dall'introduzione di Wikipedia: oggi prende la prima parentesi (che può contenere la pronuncia) e non gestisce gli anni a.C., generando false "date discordanti" per i personaggi antichi.
- [ ] Rendere cliccabile l'URL della fonte Wikipedia (oggi è solo testo).

## Luoghi
- [ ] Cambiare il luogo contenitore di un luogo storico dopo la creazione.
- [ ] Modificare un luogo specifico direttamente dalla finestra di scelta (oggi solo da "Gestisci luoghi").
- [ ] Ampliare l'elenco delle suddivisioni senza livello inferiore (oggi: comunità autonome spagnole uniprovinciali e Bruxelles).
- [ ] Ampliare gli schemi amministrativi ad altri Paesi (oltre a Italia, Francia, Germania, Spagna, Belgio, Paesi Bassi, Regno Unito).
- [ ] Lavorare sui contesti storici dei luoghi di nascita/morte/sepoltura (nome all'epoca, entità politica, unità, periodo).

## Eventi
- [ ] Modellare l'entità "Eventi" (collegata a persone, luoghi e fonti).
- [ ] Riempire la scheda laterale Eventi (per ora segnaposto).

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
- [ ] Contesto database (`AppDbContext`) unico per tutta la sessione: accumula entità in memoria e può restituire dati non aggiornati; valutare contesti a vita più breve.
- [x] Salvataggio non riuscito: `FillPersonFromForm` modifica comunque l'oggetto mostrato nell'elenco, che resta con dati non salvati; annullare le modifiche in caso di errore. Risolto: il modulo viene compilato su una copia e la scheda originale si aggiorna solo a salvataggio riuscito.
- [x] Ordinamento dei figli basato sul testo libero della data di nascita: "c. 1540" finisce dopo "1550"; estrarre un anno di riferimento per l'ordinamento. Risolto con `HistoricalYear` (gestisce anche a.C./d.C.; le date senza anno vanno in fondo).

## Interfaccia
- [x] Schede laterali Persone / Luoghi / Eventi: persone raggruppate per famiglia con date di nascita e morte; gestione luoghi integrata nella scheda Luoghi.
- [x] Scheda Persone: barra comandi (nuovo, apri, elimina, famiglia, coniuge, figlio, ricerca Wikipedia) al posto del pannello dettagli; vista Albero genealogico fino ai trisnonni con caselle + per aggiungere persone esistenti o nuove.
- [x] Sostituita la vecchia "A" nella colonna scura con il logo dell'app.
- [x] Scheda personaggio a tab nascoste (navigazione solo con Avanti/Indietro) e interfaccia modernizzata.
- [x] Tema chiaro/scuro impostabile da «Visualizza → Tema» (persistito in settings.json).
- [x] Controlli dei luoghi (campo luogo, albero dei luoghi) e finestra Famiglia adattati al tema chiaro/scuro: niente più colori fissi, solo risorse del tema.
- [ ] Virtualizzare l'elenco delle persone: oggi non è virtualizzato (lento con molte schede) e scorre tutta la pagina invece della sola lista.
- [ ] Applicare il tema scuro anche a barre di scorrimento, menu contestuali e tooltip.
- [ ] Aggiungere un indicatore di focus da tastiera ben visibile sui pulsanti.
- [x] Distinguere i messaggi di successo da quelli di errore: oggi usano lo stesso colore rosso-arancio. Risolto: verde per gli esiti positivi, rosso per gli errori, grigio per le informazioni.
