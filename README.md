# Alberi Storici

Applicazione desktop Windows realizzata in C# e WPF (.NET 10) per raccogliere schede di personaggi storici: nome, suffisso, famiglia, date e luoghi di nascita, morte e sepoltura, legami familiari. Le date sono testo libero, per supportare date storiche approssimative o non standard.

## Funzionalità

- **Schede laterali**: Persone, Luoghi ed Eventi.
- **Persone**: barra comandi (nuovo, apri scheda, elimina, famiglia, coniuge, figlio) e due viste:
  - *Famiglie*: personaggi raggruppati per dinastia, con i rami annidati sotto la dinastia principale (es. Asburgo → Asburgo di Spagna) e date di nascita e morte;
  - *Albero genealogico* del personaggio selezionato, fino ai trisnonni, con coniugi e figli. Le caselle vuote (+) permettono di collegare una persona esistente o di crearne una nuova.
- **Luoghi**: gerarchia dei luoghi, luoghi specifici e nomi nelle altre lingue.
- **Dinastie**: finestra «Dinastie…» (barra comandi Persone o pulsante «Dinastia…» nella scheda) per creare dinastie e rami, indicando di quale dinastia sono ramo, il codice Wikidata e le note. Il campo Famiglia della scheda propone le dinastie registrate; un nome nuovo viene aggiunto al salvataggio. Rinominando una dinastia si aggiornano le schede collegate.
- **Titoli e nomi**: titoli con numerazione e periodo, soprannome, titolo principale; i nomi importati in altre lingue sono consultabili con il selettore «Lingua visualizzata».
- **Eventi**: scheda segnaposto, non ancora implementata.
- **Import da Wikipedia** in italiano, con le affermazioni Wikidata collegate: precisione, fonti e valori alternativi vengono conservati. Vengono importati anche titoli, soprannome, nomi in altre lingue e famiglie (registrate come dinastie, con la dinastia superiore se indicata). Il contesto politico storico si inserisce separatamente e non viene dedotto dal paese attuale.
- Per inserire più ipotesi manuali di data, separale con `;`; gli URL nel campo Fonti, nello stesso ordine, vengono associati alle rispettive date.
- Tema chiaro e scuro (menu Visualizza).
- Messaggi di stato colorati: verde per gli esiti positivi, rosso per gli errori, grigio per le informazioni.
- I figli sono ordinati per anno di nascita, ricavato dal testo libero della data (es. `c. 1540`, `44 a.C.`).

## Dati

L'archivio è un database SQLite locale (Entity Framework Core) in `%LOCALAPPDATA%\AlberiStorici`. A ogni modifica viene aggiornata anche una copia di backup in JSON (`people.json`, con persone, unioni e dinastie) nella stessa cartella. Dal menu File è possibile importare ed esportare l'intero archivio: prima di un'importazione viene creata una copia di sicurezza, ripristinata automaticamente in caso di errore; vengono copiati anche gli eventuali file `-wal`/`-shm`. Se il salvataggio di una scheda non riesce, i dati mostrati non vengono alterati. Il backup JSON viene scritto in modo atomico, così un'interruzione non lascia file troncati.

Gli errori imprevisti vengono mostrati all'utente e registrati in `%LOCALAPPDATA%\AlberiStorici\error.log`.

## Requisiti

- Windows 10 o successivo
- .NET 10 SDK

## Avvio

```powershell
dotnet run --project .\HistoricEditor\HistoricEditor.csproj
```

Per compilare senza avviare la finestra:

```powershell
dotnet build .\HistoricEditor.slnx
```

## Test

```powershell
dotnet test .\HistoricEditor.Tests\HistoricEditor.Tests.csproj
```

## Non ancora implementato

- Gestione degli eventi storici.
- Importazione da Treccani.
- Modifica dei nomi nelle altre lingue e aggiornamento da Wikipedia di una scheda già salvata.

L'elenco completo è in [TODO.md](TODO.md).
