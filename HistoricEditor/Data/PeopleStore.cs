using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public sealed record PlaceDescription(string Classic, string Specific)
{
    // Testo mostrato nell'elenco e salvato nella scheda: "Luogo specifico (Comune, ..., Stato)".
    public string DisplayText => Specific.Length == 0 ? Classic
        : Classic.Length == 0 ? Specific
        : $"{Specific} ({Classic})";
}

public sealed partial class PeopleStore : IDisposable
{
    private static readonly string DefaultBaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlberiStorici");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string baseDir;
    private readonly string dbPath;
    private readonly string jsonBackupPath;

    private AppDbContext context;

    private bool jsonImportAttempted;

    // Vero se il backup non importato non è stato messo al sicuro: in tal caso people.json non va sovrascritto.
    private bool jsonBackupLocked;

    // Copia del backup JSON che non è stato possibile importare, conservata prima che venga sovrascritto.
    public string? UnimportedBackupPath { get; private set; }

    public PeopleStore() : this(DefaultBaseDir)
    {
    }

    // Costruttore con cartella personalizzabile, usato dai test.
    internal PeopleStore(string baseDirectory)
    {
        baseDir = baseDirectory;
        dbPath = Path.Combine(baseDir, "historic.db");
        jsonBackupPath = Path.Combine(baseDir, "people.json");
        Directory.CreateDirectory(baseDir);
        context = Open();
    }

    private AppDbContext Open()
    {
        var created = new AppDbContext(dbPath);
        try
        {
            created.Database.Migrate();
            GeographySeeder.Seed(created);
            created.ChangeTracker.Clear();
            return created;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    public static bool IsArchive(string path)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('People', 'Luoghi');";
            return Convert.ToInt32(command.ExecuteScalar()) == 2;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public void ExportDatabase(string destination)
    {
        if (IsCurrentDatabase(destination))
        {
            throw new InvalidOperationException("Non è possibile esportare sopra l’archivio in uso.");
        }

        // Si esporta su un file temporaneo: se l'operazione fallisce, il file di destinazione esistente resta intatto.
        var temp = destination + ".tmp";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        try
        {
            // VACUUM INTO produce una copia coerente anche con la connessione aperta.
            context.Database.ExecuteSql($"VACUUM INTO {temp}");
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // Sostituisce l'archivio in uso; restituisce il percorso della copia di sicurezza di quello precedente.
    public string ImportDatabase(string source)
    {
        if (IsCurrentDatabase(source))
        {
            throw new InvalidOperationException("Il file selezionato è già l’archivio in uso.");
        }

        var backup = Path.Combine(baseDir, $"historic-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak.db");
        ExportDatabase(backup);
        CloseDatabase();
        try
        {
            CopyArchive(source, dbPath);
            context = Open();
        }
        catch (Exception importError)
        {
            try
            {
                CloseDatabase();
                CopyArchive(backup, dbPath);
                context = Open();
            }
            catch (Exception restoreError)
            {
                // Senza un contesto aperto ogni operazione successiva fallirebbe con ObjectDisposedException.
                try
                {
                    context = Open();
                }
                catch (Exception)
                {
                }

                throw new AggregateException($"Importazione non riuscita e ripristino fallito. La copia di sicurezza è in {backup}.", importError, restoreError);
            }

            throw;
        }

        return backup;
    }

    // Copia l'archivio insieme ai file -wal/-shm: le ultime modifiche possono trovarsi ancora nel journal.
    private static void CopyArchive(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(source + suffix))
            {
                File.Copy(source + suffix, destination + suffix, overwrite: true);
            }
            else
            {
                File.Delete(destination + suffix);
            }
        }
    }

    private void CloseDatabase()
    {
        context.Dispose();
        // Il pool di Microsoft.Data.Sqlite tiene il file aperto anche dopo il Dispose.
        SqliteConnection.ClearAllPools();
    }

    private bool IsCurrentDatabase(string path)
        => string.Equals(Path.GetFullPath(path), Path.GetFullPath(dbPath), StringComparison.OrdinalIgnoreCase);

    public List<HistoricalPerson> LoadAll()
    {
        // L'import dal backup JSON avviene una sola volta per sessione: così, dopo aver
        // eliminato tutte le schede, l'archivio non viene ripopolato dal file.
        if (!jsonImportAttempted)
        {
            jsonImportAttempted = true;
            if (!context.People.Any() && File.Exists(jsonBackupPath))
            {
                ImportFromJson();
            }
        }

        return [.. context.People.AsNoTracking()];
    }

    public HistoricalPerson? GetPerson(int id) => context.People.AsNoTracking().FirstOrDefault(p => p.Id == id);

    public void Add(HistoricalPerson person)
    {
        context.People.Add(person);
        LinkDynasties(person);
        SaveOrRevert();
    }

    // Aggiorna i dati anagrafici di una scheda esistente senza toccare i legami familiari.
    public void Update(HistoricalPerson person)
    {
        var existing = context.People.Find(person.Id)
            ?? throw new InvalidOperationException("Scheda non trovata.");
        if (existing.Sesso != person.Sesso)
        {
            EnsureSexCompatibleWithParentRole(person.Id, person.Sesso);
        }

        existing.Name = person.Name;
        existing.Surname = person.Surname;
        existing.Suffix = person.Suffix;
        existing.Family = person.Family;
        existing.Sesso = person.Sesso;
        existing.Nickname = person.Nickname;
        existing.MainTitle = person.MainTitle;
        existing.Titles = person.Titles.Select(t => t.Clone()).ToList();
        existing.LocalizedNames = person.LocalizedNames.Select(n => n.Clone()).ToList();
        existing.Birth = person.Birth;
        existing.Death = person.Death;
        existing.BirthCalendario = person.BirthCalendario;
        existing.BirthStile = person.BirthStile;
        existing.DeathCalendario = person.DeathCalendario;
        existing.DeathStile = person.DeathStile;
        existing.BirthPlace = person.BirthPlace;
        existing.DeathPlace = person.DeathPlace;
        existing.BurialPlace = person.BurialPlace;
        existing.WikipediaTitle = person.WikipediaTitle;
        existing.WikipediaUrl = person.WikipediaUrl;
        existing.WikipediaExtract = person.WikipediaExtract;
        existing.WikidataId = person.WikidataId;
        existing.WikiTreeId = person.WikiTreeId;
        existing.Note = person.Note;
        existing.BirthDateEvidence = person.BirthDateEvidence;
        existing.DeathDateEvidence = person.DeathDateEvidence;
        existing.BirthPlaces = person.BirthPlaces;
        existing.DeathPlaces = person.DeathPlaces;
        existing.BurialPlaces = person.BurialPlaces;
        existing.WikipediaLeads = person.WikipediaLeads;
        existing.BirthLuogoId = person.BirthLuogoId;
        existing.BirthLuogoSpecificoId = person.BirthLuogoSpecificoId;
        existing.DeathLuogoId = person.DeathLuogoId;
        existing.DeathLuogoSpecificoId = person.DeathLuogoSpecificoId;
        existing.BurialLuogoId = person.BurialLuogoId;
        existing.BurialLuogoSpecificoId = person.BurialLuogoSpecificoId;
        LinkDynasties(existing);

        SaveOrRevert();
    }

    // Elimina una scheda: scollega i figli e rimuove le unioni collegate.
    public void Delete(int id)
    {
        var person = context.People.Find(id);
        if (person is null)
        {
            return;
        }

        foreach (var child in context.People.Where(p => p.MotherId == id || p.FatherId == id).ToList())
        {
            if (child.MotherId == id)
            {
                child.MotherId = null;
            }

            if (child.FatherId == id)
            {
                child.FatherId = null;
            }
        }

        context.Unioni.RemoveRange(context.Unioni.Where(u => u.Persona1Id == id || u.Persona2Id == id));
        context.Relazioni.RemoveRange(context.Relazioni.Where(r => r.Persona1Id == id || r.Persona2Id == id));
        context.PersoneDinastie.RemoveRange(context.PersoneDinastie.Where(l => l.PersonaId == id));
        context.Set<EventoPartecipante>().RemoveRange(context.Set<EventoPartecipante>().Where(p => p.PersonaId == id));
        context.Set<OperaPersona>().RemoveRange(context.Set<OperaPersona>().Where(p => p.PersonaId == id));
        var files = RemoveAttachmentRows(AmbitiAllegato.Persona, [id]);
        context.People.Remove(person);
        SaveOrRevert();
        DeleteAttachmentFiles(files);
    }

    public IReadOnlyList<Dinastia> GetDynasties() => [.. context.Dinastie.AsNoTracking().OrderBy(d => d.Nome)];

    public IReadOnlyList<PersonaDinastia> GetDynastyLinks() => [.. context.PersoneDinastie.AsNoTracking()];

    // Id delle dinastie a cui è collegata la scheda.
    public IReadOnlyList<int> GetPersonDynastyIds(int personId)
        => [.. context.PersoneDinastie.AsNoTracking().Where(l => l.PersonaId == personId).Select(l => l.DinastiaId)];

    // Allinea i collegamenti della scheda (tracciata) alle dinastie registrate indicate nel campo «Famiglia».
    private void LinkDynasties(HistoricalPerson person)
    {
        var names = new HashSet<string>(PeopleGrouping.FamilyNames(person.Family), PeopleGrouping.FamilyComparer);
        var wanted = names.Count == 0 ? [] : context.Dinastie.AsNoTracking().AsEnumerable()
            .Where(d => names.Contains(d.Nome.Trim()))
            .Select(d => d.Id)
            .ToHashSet();
        var current = person.Id == 0 ? [] : context.PersoneDinastie.Where(l => l.PersonaId == person.Id).ToList();
        context.PersoneDinastie.RemoveRange(current.Where(l => !wanted.Contains(l.DinastiaId)));
        foreach (var id in wanted.Where(id => current.All(l => l.DinastiaId != id)))
        {
            context.PersoneDinastie.Add(new PersonaDinastia { Persona = person, DinastiaId = id });
        }
    }

    // Collega alla dinastia (tracciata) le schede che la indicano per nome e non sono ancora collegate.
    private void LinkPeopleByName(Dinastia dynasty)
    {
        var name = dynasty.Nome.Trim();
        var linked = dynasty.Id == 0 ? [] : context.PersoneDinastie.Where(l => l.DinastiaId == dynasty.Id).Select(l => l.PersonaId).ToHashSet();
        foreach (var person in context.People.Where(p => p.Family != null && p.Family.Length > 0).AsEnumerable()
            .Where(p => !linked.Contains(p.Id) && PeopleGrouping.FamilyNames(p.Family).Contains(name, PeopleGrouping.FamilyComparer)))
        {
            context.PersoneDinastie.Add(new PersonaDinastia { PersonaId = person.Id, Dinastia = dynasty });
        }
    }

    // Ricostruisce tutti i collegamenti dai nomi delle famiglie (es. dopo il ripristino di un backup).
    private void RebuildDynastyLinks()
    {
        var byName = context.Dinastie.AsNoTracking().AsEnumerable()
            .GroupBy(d => d.Nome.Trim(), PeopleGrouping.FamilyComparer)
            .ToDictionary(g => g.Key, g => g.First().Id, PeopleGrouping.FamilyComparer);
        context.PersoneDinastie.RemoveRange(context.PersoneDinastie);
        foreach (var person in context.People.AsNoTracking().Where(p => p.Family != null && p.Family.Length > 0).AsEnumerable())
        {
            foreach (var name in PeopleGrouping.FamilyNames(person.Family))
            {
                if (byName.TryGetValue(name, out var id))
                {
                    context.PersoneDinastie.Add(new PersonaDinastia { PersonaId = person.Id, DinastiaId = id });
                }
            }
        }

        context.SaveChanges();
    }

    private static string RenameFamily(string? family, string oldName, string newName)
        => PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(family)
            .Select(n => PeopleGrouping.FamilyComparer.Equals(n, oldName) ? newName : n)
            .Distinct(PeopleGrouping.FamilyComparer));

    // Crea o aggiorna una dinastia; rinominandola aggiorna anche la famiglia delle schede collegate.
    public int SaveDynasty(Dinastia data)
    {
        var nome = (data.Nome ?? string.Empty).Trim();
        if (nome.Length == 0)
        {
            throw new InvalidOperationException("Il nome della dinastia è obbligatorio.");
        }

        if (context.Dinastie.Any(d => d.Id != data.Id && d.Nome == nome))
        {
            throw new InvalidOperationException($"Esiste già una dinastia chiamata «{nome}».");
        }

        if (data.Id != 0 && IsDynastyDescendant(data.ParentId, data.Id))
        {
            throw new InvalidOperationException("Una dinastia non può essere ramo di sé stessa o di un proprio ramo.");
        }

        var entity = data.Id == 0 ? null : context.Dinastie.Find(data.Id);
        if (entity is null)
        {
            entity = new Dinastia();
            context.Dinastie.Add(entity);
        }
        else if (!string.Equals(entity.Nome, nome, StringComparison.Ordinal))
        {
            // Le schede collegate per identificativo seguono il nuovo nome, anche se hanno più dinastie.
            var oldName = entity.Nome.Trim();
            var linkedIds = context.PersoneDinastie.Where(l => l.DinastiaId == entity.Id).Select(l => l.PersonaId).ToList();
            foreach (var person in context.People.Where(p => linkedIds.Contains(p.Id)))
            {
                person.Family = RenameFamily(person.Family, oldName, nome);
            }
        }

        entity.Nome = nome;
        entity.ParentId = data.ParentId;
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim();
        entity.Note = data.Note ?? string.Empty;
        LinkPeopleByName(entity);
        SaveOrRevert();
        return entity.Id;
    }

    public void DeleteDynasty(int id)
    {
        if (context.Dinastie.Find(id) is not { } entity)
        {
            return;
        }

        foreach (var branch in context.Dinastie.Where(d => d.ParentId == id))
        {
            branch.ParentId = entity.ParentId;
        }

        context.PersoneDinastie.RemoveRange(context.PersoneDinastie.Where(l => l.DinastiaId == id));
        var files = RemoveAttachmentRows(AmbitiAllegato.Dinastia, [id]);
        context.Dinastie.Remove(entity);
        SaveOrRevert();
        DeleteAttachmentFiles(files);
    }

    // Trova (per Wikidata o per nome) o crea la dinastia; collega il ramo superiore solo se non già indicato.
    public int EnsureDynasty(string nome, string wikidataId, int? parentId)
    {
        nome = (nome ?? string.Empty).Trim();
        wikidataId = (wikidataId ?? string.Empty).Trim();
        var entity = (wikidataId.Length > 0 ? context.Dinastie.FirstOrDefault(d => d.WikidataId == wikidataId) : null)
            ?? context.Dinastie.FirstOrDefault(d => d.Nome == nome);
        if (entity is null)
        {
            entity = new Dinastia { Nome = nome, WikidataId = wikidataId, ParentId = parentId };
            context.Dinastie.Add(entity);
            LinkPeopleByName(entity);
            SaveOrRevert();
            return entity.Id;
        }

        var changed = false;
        if (entity.WikidataId.Length == 0 && wikidataId.Length > 0)
        {
            entity.WikidataId = wikidataId;
            changed = true;
        }

        if (entity.ParentId is null && parentId is int p && !IsDynastyDescendant(p, entity.Id))
        {
            entity.ParentId = p;
            changed = true;
        }

        if (changed)
        {
            SaveOrRevert();
        }

        return entity.Id;
    }

    // Vero se candidateId coincide con dynastyId o ne discende (risalendo i rami superiori).
    private bool IsDynastyDescendant(int? candidateId, int dynastyId)
    {
        var visited = new HashSet<int>();
        while (candidateId is int id && visited.Add(id))
        {
            if (id == dynastyId)
            {
                return true;
            }

            candidateId = context.Dinastie.Where(d => d.Id == id).Select(d => d.ParentId).FirstOrDefault();
        }

        return false;
    }

    // Eventi con i partecipanti, in ordine cronologico per anno moderno (quelli senza anno riconoscibile in fondo).
    public IReadOnlyList<Evento> GetEvents()
        => [.. context.Eventi.AsNoTracking().Include(e => e.Partecipanti).AsEnumerable()
            .OrderBy(e => e.Anno is null)
            .ThenBy(e => e.Anno ?? 0)
            .ThenBy(e => e.Data1.Length > 0 ? e.Data1 : e.Data2, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Titolo, StringComparer.CurrentCultureIgnoreCase)];

    public Evento? GetEvent(int id)
        => context.Eventi.AsNoTracking().Include(e => e.Partecipanti).FirstOrDefault(e => e.Id == id);

    public IReadOnlyList<Evento> GetEventsForPerson(int personId)
        => [.. GetEvents().Where(e => e.Partecipanti.Any(p => p.PersonaId == personId))];

    public IReadOnlyList<string> GetEventTypes()
        => [.. context.Eventi.AsNoTracking().Select(e => e.Tipo).Distinct().AsEnumerable()
            .Concat(DataEvento.TipiSuggeriti)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)];

    // Crea o aggiorna un evento sostituendo l'elenco dei partecipanti.
    public int SaveEvent(Evento data)
    {
        var titolo = (data.Titolo ?? string.Empty).Trim();
        if (titolo.Length == 0)
        {
            throw new InvalidOperationException("Il titolo dell’evento è obbligatorio.");
        }

        var qualificatore = DataEvento.Info(data.DataQualificatore).Codice;
        var data1 = (data.Data1 ?? string.Empty).Trim();
        var data2 = DataEvento.IsIntervallo(qualificatore) ? (data.Data2 ?? string.Empty).Trim() : string.Empty;
        if (DataEvento.Validate(qualificatore, data1, data2, data.Stile) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var partecipanti = data.Partecipanti
            .GroupBy(p => p.PersonaId)
            .Select(g => new EventoPartecipante { PersonaId = g.Key, Ruolo = (g.First().Ruolo ?? string.Empty).Trim() })
            .ToList();
        var ids = partecipanti.Select(p => p.PersonaId).ToList();
        if (context.People.Count(p => ids.Contains(p.Id)) != ids.Count)
        {
            throw new InvalidOperationException("Uno dei partecipanti non è più in archivio.");
        }

        var entity = data.Id == 0
            ? new Evento()
            : context.Eventi.Include(e => e.Partecipanti).FirstOrDefault(e => e.Id == data.Id)
                ?? throw new InvalidOperationException("Evento non trovato.");
        entity.Titolo = titolo;
        entity.Tipo = (data.Tipo ?? string.Empty).Trim();
        entity.DataQualificatore = qualificatore;
        entity.Data1 = data1;
        entity.Data2 = data2;
        entity.Calendario = HistoricalCalendar.CalendarInfo(data.Calendario).Codice;
        entity.Stile = HistoricalCalendar.StyleInfo(data.Stile).Codice;
        entity.Descrizione = data.Descrizione ?? string.Empty;
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim();
        (entity.LuogoId, entity.LuogoSpecificoId) = ResolveEventPlace(data.LuogoId, data.LuogoSpecificoId);

        entity.Partecipanti.RemoveAll(p => !ids.Contains(p.PersonaId));
        foreach (var partecipante in partecipanti)
        {
            var existing = entity.Partecipanti.FirstOrDefault(p => p.PersonaId == partecipante.PersonaId);
            if (existing is null)
            {
                entity.Partecipanti.Add(partecipante);
            }
            else
            {
                existing.Ruolo = partecipante.Ruolo;
            }
        }

        if (data.Id == 0)
        {
            context.Eventi.Add(entity);
        }

        SaveOrRevert();
        return entity.Id;
    }

    // Il luogo specifico determina quello amministrativo; riferimenti non più in archivio vengono rifiutati.
    private (int? LuogoId, int? SpecificoId) ResolveEventPlace(int? luogoId, int? specificoId, string missingMessage = "Il luogo dell’evento non è più in archivio.")
    {
        if (specificoId is int s)
        {
            var specifico = context.LuoghiSpecifici.AsNoTracking().FirstOrDefault(x => x.Id == s)
                ?? throw new InvalidOperationException(missingMessage);
            return (specifico.LuogoId, s);
        }

        if (luogoId is int l && !context.Luoghi.Any(x => x.Id == l))
        {
            throw new InvalidOperationException(missingMessage);
        }

        return (luogoId, null);
    }

    public void DeleteEvent(int id)
    {
        if (context.Eventi.Find(id) is not { } entity)
        {
            return;
        }

        var files = RemoveAttachmentRows(AmbitiAllegato.Evento, [id]);
        context.Eventi.Remove(entity);
        SaveOrRevert();
        DeleteAttachmentFiles(files);
    }

    public int? FindEventIdByWikidataId(string wikidataId)
    {
        var id = (wikidataId ?? string.Empty).Trim();
        return id.Length == 0
            ? null
            : context.Eventi.Where(e => e.WikidataId == id).Select(e => (int?)e.Id).FirstOrDefault();
    }

    // Aggiunge un partecipante a un evento esistente; false se c'era già.
    public bool AddEventParticipant(int eventId, int personId, string ruolo)
    {
        var entity = context.Eventi.Include(e => e.Partecipanti).FirstOrDefault(e => e.Id == eventId)
            ?? throw new InvalidOperationException("Evento non trovato.");
        if (!context.People.Any(p => p.Id == personId))
        {
            throw new InvalidOperationException("Il partecipante non è più in archivio.");
        }

        if (entity.Partecipanti.Any(p => p.PersonaId == personId))
        {
            return false;
        }

        entity.Partecipanti.Add(new EventoPartecipante { PersonaId = personId, Ruolo = (ruolo ?? string.Empty).Trim() });
        SaveOrRevert();
        return true;
    }

    // Cerca una scheda già in archivio tramite l'identificativo Wikidata, per collegare i parenti importati.
    public int? FindPersonIdByWikidataId(string wikidataId)
    {
        if (string.IsNullOrWhiteSpace(wikidataId))
        {
            return null;
        }

        return context.People
            .Where(p => p.WikidataId == wikidataId)
            .Select(p => (int?)p.Id)
            .FirstOrDefault();
    }

    // Nomi dei luoghi della catena nelle altre lingue (per l'avviso e il dettaglio nella scheda).
    public string DescribePlaceLanguages(int? luogoId, int? specificoId)
    {
        var startId = specificoId is int sid
            ? context.LuoghiSpecifici.AsNoTracking().Where(s => s.Id == sid).Select(s => (int?)s.LuogoId).FirstOrDefault()
            : luogoId;
        if (startId is not int id)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        var current = context.Luoghi.AsNoTracking().FirstOrDefault(l => l.Id == id);
        for (var guard = 0; current is not null && guard < 32; guard++)
        {
            var others = current.NomiAltreLingue
                .Where(n => n.Name.Length > 0 && !string.Equals(n.Name, current.Nome, StringComparison.OrdinalIgnoreCase))
                .Select(n => $"{LanguageLabel(n.Language)}: {n.Name}")
                .ToList();
            if (others.Count > 0)
            {
                lines.Add($"{current.Tipo} {current.Nome} — {string.Join(", ", others)}");
            }

            current = current.ParentId is int pid ? context.Luoghi.AsNoTracking().FirstOrDefault(l => l.Id == pid) : null;
        }

        return string.Join(Environment.NewLine, lines);
    }

    public bool HasOtherLanguageNames(int? luogoId, int? specificoId)
        => DescribePlaceLanguages(luogoId, specificoId).Length > 0;

    private static string LanguageLabel(string code) => code switch
    {
        "it" => "Italiano",
        "en" => "Inglese",
        "de" => "Tedesco",
        "fr" => "Francese",
        "es" => "Spagnolo",
        "nl" => "Olandese",
        _ => code,
    };

    public DerivedPlace DerivePlace(HistoricalPlaceRecord place) => GeographyResolver.DeriveLevels(context, place);

    public bool SuggestsNoLowerLevel(string country, string tipo, string nome)
        => GeographyResolver.SuggestsNoLowerLevel(context, country, tipo, nome);

    // Crea/riusa la catena di luoghi dall'editor e restituisce l'id della foglia.
    public int? EnsurePlace(IReadOnlyList<GeoLevel> levels, double? latitudine, double? longitudine)
    {
        try
        {
            var id = GeographyResolver.EnsureChain(context, levels, latitudine, longitudine)?.Id;
            context.ChangeTracker.Clear();
            return id;
        }
        catch (Exception)
        {
            RevertPendingChanges();
            throw;
        }
    }

    public IReadOnlyList<Luogo> GetAllPlaces() => [.. context.Luoghi.AsNoTracking()];

    public bool PlaceExists(int id) => context.Luoghi.Any(l => l.Id == id);

    public int? FindPlace(int? parentId, string tipo, string nome)
        => GeographyResolver.FindChild(context, parentId, tipo.Trim(), nome.Trim())?.Id;

    // Livelli della catena fino al luogo indicato, per proporre un nuovo luogo accanto o sotto di esso.
    public List<GeoLevel> GetChainLevels(int luogoId)
    {
        var path = new List<Luogo>();
        for (var current = context.Luoghi.Find(luogoId); current is not null && path.Count < 32; current = current.ParentId is int parentId ? context.Luoghi.Find(parentId) : null)
        {
            path.Insert(0, current);
        }

        if (path.Count == 0)
        {
            return [];
        }

        var schema = GeoSchemas.For(path[0].Nome);
        var levels = new List<GeoLevel>();
        Luogo? previous = null;
        for (var i = 0; i < schema.Length; i++)
        {
            var tipo = schema[i];
            var node = path.FirstOrDefault(l => string.Equals(l.Tipo, tipo, StringComparison.OrdinalIgnoreCase));
            if (node is not null && i < schema.Length - 1)
            {
                levels.Add(new GeoLevel(tipo, node.Nome));
                previous = node;
            }
            else if (node is null && previous?.SenzaLivelloInferiore == true && !levels[^1].Assente)
            {
                levels.Add(new GeoLevel(tipo, string.Empty) { Assente = true });
            }
            else
            {
                levels.Add(new GeoLevel(tipo, string.Empty));
            }
        }

        return levels;
    }

    public IReadOnlyList<string> GetHistoricPlaceTypes()
        => [.. context.Luoghi.Where(l => l.Storico && l.Tipo != "").Select(l => l.Tipo).Distinct().OrderBy(t => t)];

    public int AddHistoricPlace(string nome, string tipo, int? parentId, string periodo, double? latitudine, double? longitudine, string note)
    {
        var luogo = new Luogo
        {
            Nome = nome.Trim(),
            Tipo = tipo.Trim(),
            ParentId = parentId,
            Storico = true,
            Periodo = periodo.Trim(),
            Latitudine = latitudine,
            Longitudine = longitudine,
            Note = note.Trim(),
        };
        context.Luoghi.Add(luogo);
        SaveOrRevert();
        return luogo.Id;
    }

    // Formato "Comune, Provincia, Regione, Stato".
    public string DescribePlace(int? luogoId)
    {
        var parts = new List<string>();
        var current = luogoId is int id ? context.Luoghi.Find(id) : null;
        while (current is not null && parts.Count < 32)
        {
            parts.Add(current.Nome);
            current = current.ParentId is int parentId ? context.Luoghi.Find(parentId) : null;
        }

        return string.Join(", ", parts);
    }

    public PlaceDescription DescribeRole(int? luogoId, int? specificoId)
    {
        if (specificoId is int id && context.LuoghiSpecifici.Find(id) is { } specifico)
        {
            return new PlaceDescription(DescribePlace(specifico.LuogoId), specifico.Nome);
        }

        return new PlaceDescription(DescribePlace(luogoId), string.Empty);
    }

    public void UpdatePlace(int id, string nome, double? latitudine, double? longitudine, bool storico, string periodo, string note, bool senzaLivelloInferiore)
    {
        var luogo = context.Luoghi.Find(id);
        if (luogo is null)
        {
            return;
        }

        luogo.Nome = nome.Trim();
        luogo.Latitudine = latitudine;
        luogo.Longitudine = longitudine;
        luogo.Storico = storico;
        luogo.Periodo = periodo.Trim();
        luogo.Note = note.Trim();
        luogo.SenzaLivelloInferiore = senzaLivelloInferiore;
        SyncPlaceTexts();
        SaveOrRevert();
    }

    public void DeletePlace(int id)
    {
        var luogo = context.Luoghi.Find(id);
        if (luogo is null)
        {
            return;
        }

        // Il luogo viene eliminato con tutto ciò che contiene: le schede collegate perdono anche il testo.
        var parents = context.Luoghi.AsNoTracking().Select(l => new { l.Id, l.ParentId }).ToList();
        var removed = new HashSet<int> { id };
        for (var added = true; added;)
        {
            added = false;
            foreach (var place in parents.Where(p => p.ParentId is int parent && removed.Contains(parent)))
            {
                added |= removed.Add(place.Id);
            }
        }

        var removedSpecifics = context.LuoghiSpecifici.AsNoTracking().Where(s => removed.Contains(s.LuogoId)).Select(s => s.Id).ToHashSet();
        SyncPlaceTexts(removed, removedSpecifics);
        var files = RemoveAttachmentRows(AmbitiAllegato.Luogo, removed);
        context.Luoghi.Remove(luogo);
        SaveOrRevert();
        DeleteAttachmentFiles(files);
    }

    // Le schede conservano il testo del luogo (usato nell'elenco e nel backup): va riallineato quando i luoghi
    // collegati vengono rinominati, spostati o eliminati. Le schede senza luogo collegato mantengono il testo libero.
    private void SyncPlaceTexts(IReadOnlySet<int>? removedPlaces = null, IReadOnlySet<int>? removedSpecifics = null)
    {
        removedPlaces ??= new HashSet<int>();
        removedSpecifics ??= new HashSet<int>();
        var linked = context.People.Where(p =>
            p.BirthLuogoId != null || p.BirthLuogoSpecificoId != null
            || p.DeathLuogoId != null || p.DeathLuogoSpecificoId != null
            || p.BurialLuogoId != null || p.BurialLuogoSpecificoId != null).ToList();
        foreach (var person in linked)
        {
            person.BirthPlace = Describe(person.BirthLuogoId, person.BirthLuogoSpecificoId, person.BirthPlace);
            person.DeathPlace = Describe(person.DeathLuogoId, person.DeathLuogoSpecificoId, person.DeathPlace);
            person.BurialPlace = Describe(person.BurialLuogoId, person.BurialLuogoSpecificoId, person.BurialPlace);
        }

        string Describe(int? luogoId, int? specificoId, string current)
        {
            if ((luogoId is int l && removedPlaces.Contains(l)) || (specificoId is int s && removedSpecifics.Contains(s)))
            {
                return string.Empty;
            }

            return luogoId is null && specificoId is null ? current : DescribeRole(luogoId, specificoId).DisplayText;
        }
    }

    public IReadOnlyList<LuogoSpecifico> GetSpecificPlaces() => [.. context.LuoghiSpecifici.AsNoTracking()];

    public LuogoSpecifico? GetSpecificPlace(int id) => context.LuoghiSpecifici.AsNoTracking().FirstOrDefault(s => s.Id == id);

    public bool SpecificPlaceExists(int id) => context.LuoghiSpecifici.Any(s => s.Id == id);

    public IReadOnlyList<string> GetSpecificPlaceTypes()
        => [.. context.LuoghiSpecifici.Select(s => s.Tipo).Where(t => t != "").Distinct().OrderBy(t => t)];

    public int CountPeopleUsingSpecific(int id)
        => context.People.Count(p => p.BirthLuogoSpecificoId == id || p.DeathLuogoSpecificoId == id || p.BurialLuogoSpecificoId == id);

    public int SaveSpecificPlace(LuogoSpecifico data)
    {
        var entity = data.Id == 0
            ? new LuogoSpecifico()
            : context.LuoghiSpecifici.Find(data.Id) ?? throw new InvalidOperationException("Luogo specifico non trovato.");

        entity.Nome = data.Nome.Trim();
        entity.Tipo = data.Tipo.Trim();
        entity.LuogoId = data.LuogoId;
        entity.Indirizzo = data.Indirizzo.Trim();
        entity.Condizione = data.Condizione;
        entity.DataDistruzione = data.DataDistruzione.Trim();
        entity.Latitudine = data.Latitudine;
        entity.Longitudine = data.Longitudine;
        entity.Note = data.Note.Trim();
        entity.WikidataId = data.WikidataId.Trim();

        if (data.Id == 0)
        {
            context.LuoghiSpecifici.Add(entity);
        }
        else
        {
            SyncPlaceTexts();
        }

        SaveOrRevert();
        return entity.Id;
    }

    public void DeleteSpecificPlace(int id)
    {
        var specifico = context.LuoghiSpecifici.Find(id);
        if (specifico is null)
        {
            return;
        }

        SyncPlaceTexts(removedSpecifics: new HashSet<int> { id });
        context.LuoghiSpecifici.Remove(specifico);
        SaveOrRevert();
    }

    // Riusa il luogo specifico già importato (stesso Wikidata o stesso nome nello stesso luogo).
    public int EnsureSpecificPlace(string nome, int luogoId, string wikidataId, double? latitudine = null, double? longitudine = null, string tipo = "")
    {
        nome = nome.Trim();
        tipo = (tipo ?? string.Empty).Trim();
        var existing = (wikidataId.Length > 0 ? context.LuoghiSpecifici.FirstOrDefault(s => s.WikidataId == wikidataId) : null)
            ?? context.LuoghiSpecifici.FirstOrDefault(s => s.LuogoId == luogoId && s.Nome == nome);
        if (existing is not null)
        {
            // Coordinate (P625) e tipo solo se mancanti, per non sovrascrivere correzioni manuali.
            existing.Latitudine ??= latitudine;
            existing.Longitudine ??= longitudine;
            if (string.IsNullOrWhiteSpace(existing.Tipo))
            {
                existing.Tipo = tipo;
            }

            SaveOrRevert();
            return existing.Id;
        }

        var created = new LuogoSpecifico { Nome = nome, Tipo = tipo, LuogoId = luogoId, WikidataId = wikidataId, Latitudine = latitudine, Longitudine = longitudine };
        context.LuoghiSpecifici.Add(created);
        SaveOrRevert();
        return created.Id;
    }

    // In caso di errore annulla le modifiche pendenti, altrimenti resterebbero nel contesto e bloccherebbero i salvataggi successivi.
    // Dopo il salvataggio il contesto viene svuotato: resta a vita lunga ma non accumula entità né restituisce copie superate.
    private void SaveOrRevert()
    {
        try
        {
            context.SaveChanges();
            context.ChangeTracker.Clear();
        }
        catch (Exception)
        {
            RevertPendingChanges();
            throw;
        }
    }

    private void RevertPendingChanges() => context.ChangeTracker.Clear();

    public IReadOnlyList<PersonSummary> GetPersonSummaries()
        => [.. context.People.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso })
            .AsEnumerable()
            .Select(p => Summarize(p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso))
            .OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)];

    public string GetSex(int personId)
        => context.People.Where(p => p.Id == personId).Select(p => p.Sesso).FirstOrDefault() ?? string.Empty;

    public (int? MotherId, int? FatherId) GetParents(int personId)
    {
        var parents = context.People.Where(p => p.Id == personId).Select(p => new { p.MotherId, p.FatherId }).FirstOrDefault();
        return (parents?.MotherId, parents?.FatherId);
    }

    public FamilyView GetFamily(int personId)
    {
        var (motherId, fatherId) = GetParents(personId);
        var views = context.Unioni.AsNoTracking()
            .Where(u => u.Persona1Id == personId || u.Persona2Id == personId)
            .OrderBy(u => u.Id)
            .AsEnumerable()
            .Select(u => new UnionView
            {
                UnionId = u.Id,
                Coniuge = Summary(u.Persona1Id == personId ? u.Persona2Id : u.Persona1Id),
                Tipo = u.Tipo,
                Inizio = u.Inizio,
                Fine = u.Fine,
            })
            .ToList();

        var children = context.People.AsNoTracking()
            .Where(p => p.MotherId == personId || p.FatherId == personId)
            .Select(p => new { p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso, p.MotherId, p.FatherId })
            .ToList();

        // I figli sono raggruppati per altro genitore, cioè per unione.
        var others = new List<PersonSummary>();
        foreach (var child in HistoricalYear.OrderByYear(children, c => c.Birth))
        {
            var summary = Summarize(child.Id, child.Name, child.Surname, child.Suffix, child.Birth, child.Death, child.Sesso);
            var otherParent = child.MotherId == personId ? child.FatherId : child.MotherId;
            if (otherParent is not int otherId)
            {
                others.Add(summary);
                continue;
            }

            var view = views.FirstOrDefault(v => v.Coniuge?.Id == otherId);
            if (view is null)
            {
                view = new UnionView { Coniuge = Summary(otherId), Tipo = "Unione non registrata" };
                views.Add(view);
            }

            view.Figli.Add(summary);
        }

        return new FamilyView(Summary(fatherId), Summary(motherId), views, others);
    }

    // Schede già presenti che potrebbero rappresentare la stessa persona.
    // Certe: stesso Q-id Wikidata o stessa voce Wikipedia. Probabili: stesso nome completo
    // (senza accenti né punteggiatura) e anni di nascita e morte compatibili.
    public List<PersonDuplicate> FindPossibleDuplicates(HistoricalPerson candidate, int excludeId = 0)
    {
        var wikidata = (candidate.WikidataId ?? string.Empty).Trim();
        var title = (candidate.WikipediaTitle ?? string.Empty).Trim();
        var name = WikipediaConsensus.Normalize(HistoricalPerson.ComposeName(candidate.Name ?? string.Empty, candidate.Surname ?? string.Empty, candidate.Suffix ?? string.Empty));
        if (wikidata.Length == 0 && title.Length == 0 && name.Length == 0)
        {
            return [];
        }

        var birth = HistoricalYear.Parse(candidate.Birth);
        var death = HistoricalYear.Parse(candidate.Death);
        var result = new List<PersonDuplicate>();
        foreach (var p in context.People.AsNoTracking()
                     .Where(p => p.Id != excludeId)
                     .Select(p => new { p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso, p.WikidataId, p.WikipediaTitle }))
        {
            var summary = Summarize(p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso);
            if (wikidata.Length > 0 && string.Equals(wikidata, (p.WikidataId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PersonDuplicate(p.Id, summary.Etichetta, true, $"stesso elemento Wikidata ({wikidata})"));
            }
            else if (title.Length > 0 && string.Equals(title, (p.WikipediaTitle ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PersonDuplicate(p.Id, summary.Etichetta, true, $"stessa voce Wikipedia («{title}»)"));
            }
            else if (name.Length > 0
                     && name == WikipediaConsensus.Normalize(summary.Nome)
                     && YearsCompatible(birth, HistoricalYear.Parse(p.Birth))
                     && YearsCompatible(death, HistoricalYear.Parse(p.Death)))
            {
                result.Add(new PersonDuplicate(p.Id, summary.Etichetta, false, "stesso nome e date compatibili"));
            }
        }

        return [.. result.OrderByDescending(d => d.Certa).ThenBy(d => d.Etichetta, StringComparer.CurrentCultureIgnoreCase)];

        static bool YearsCompatible(int? a, int? b) => a is null || b is null || a == b;
    }

    // Schede collegate agli elementi Wikidata indicati (Q-id → Id della scheda).
    public Dictionary<string, int> FindPeopleByWikidataIds(IEnumerable<string> wikidataIds)
    {
        var wanted = wikidataIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return result;
        }

        foreach (var p in context.People.AsNoTracking().Where(p => p.WikidataId != "").Select(p => new { p.Id, p.WikidataId }).OrderBy(p => p.Id))
        {
            var id = p.WikidataId.Trim().ToUpperInvariant();
            if (wanted.Contains(id))
            {
                result.TryAdd(id, p.Id);
            }
        }

        return result;
    }

    // Schede collegate ai profili WikiTree indicati (identificativo WikiTree → Id della scheda).
    public Dictionary<string, int> FindPeopleByWikiTreeIds(IEnumerable<string> wikiTreeIds)
    {
        var wanted = wikiTreeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return result;
        }

        foreach (var p in context.People.AsNoTracking().Where(p => p.WikiTreeId != "").Select(p => new { p.Id, p.WikiTreeId }).OrderBy(p => p.Id))
        {
            var id = p.WikiTreeId.Trim();
            if (wanted.Contains(id))
            {
                result.TryAdd(id, p.Id);
            }
        }

        return result;
    }

    // Una scheda senza famiglia eredita quella del padre (es. figli naturali senza famiglia su Wikidata).
    public bool InheritFatherFamilyIfMissing(int personId)
    {
        var person = context.People.Find(personId);
        if (person is null || !string.IsNullOrWhiteSpace(person.Family) || person.FatherId is not int fatherId
            || context.People.AsNoTracking().Where(p => p.Id == fatherId).Select(p => p.Family).FirstOrDefault() is not { } family
            || string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        person.Family = family.Trim();
        LinkDynasties(person);
        SaveOrRevert();
        return true;
    }

    // Registra il profilo WikiTree di una scheda che non ne ha ancora uno.
    public void SetWikiTreeIdIfMissing(int personId, string wikiTreeId)
    {
        var person = context.People.Find(personId);
        if (person is null || person.WikiTreeId.Length > 0 || string.IsNullOrWhiteSpace(wikiTreeId))
        {
            return;
        }

        person.WikiTreeId = wikiTreeId.Trim();
        SaveOrRevert();
    }

    public int CreatePerson(string nome, string sesso)
    {
        var person = new HistoricalPerson { Name = nome.Trim(), Sesso = sesso };
        context.People.Add(person);
        SaveOrRevert();
        return person.Id;
    }

    public void SetSex(int personId, string sesso)
    {
        var person = context.People.Find(personId) ?? throw new InvalidOperationException("Scheda non trovata.");
        EnsureSexCompatibleWithParentRole(personId, sesso);
        person.Sesso = sesso;
        SaveOrRevert();
    }

    // Il sesso non può contraddire il ruolo di padre o madre già assegnato alla persona.
    private void EnsureSexCompatibleWithParentRole(int personId, string? sesso)
    {
        if (sesso == Sessi.Maschio && context.People.Any(p => p.MotherId == personId))
        {
            throw new InvalidOperationException("È indicata come madre di qualcuno: rimuovi prima quel legame.");
        }

        if (sesso == Sessi.Femmina && context.People.Any(p => p.FatherId == personId))
        {
            throw new InvalidOperationException("È indicato come padre di qualcuno: rimuovi prima quel legame.");
        }
    }

    public void SetParent(int childId, int? parentId, bool mother)
        => Apply(() => ApplyParent(childId, parentId, mother));

    // Collega un figlio a una persona e, se indicato, all'altro genitore (il coniuge dell'unione).
    public void AssignChild(int parentId, int childId, int? otherParentId)
    {
        var sesso = GetSex(parentId);
        if (sesso is not (Sessi.Maschio or Sessi.Femmina))
        {
            throw new InvalidOperationException("Indica prima il sesso della persona, per sapere se è padre o madre.");
        }

        Apply(() =>
        {
            ApplyParent(childId, parentId, mother: sesso == Sessi.Femmina);
            if (otherParentId is int otherId)
            {
                ApplyParent(childId, otherId, mother: sesso == Sessi.Maschio);
            }
        });
    }

    public void RemoveChild(int parentId, int childId)
    {
        var child = context.People.Find(childId);
        if (child is null)
        {
            return;
        }

        if (child.MotherId == parentId)
        {
            child.MotherId = null;
        }

        if (child.FatherId == parentId)
        {
            child.FatherId = null;
        }

        SaveOrRevert();
    }

    public Unione? GetUnion(int id) => context.Unioni.AsNoTracking().FirstOrDefault(u => u.Id == id);

    public IReadOnlyList<Unione> GetUnions() => [.. context.Unioni.AsNoTracking()];

    public bool HasUnion(int personId, int otherId) => context.Unioni.AsNoTracking().Any(u =>
        (u.Persona1Id == personId && u.Persona2Id == otherId) || (u.Persona1Id == otherId && u.Persona2Id == personId));

    // Vero se collegare il figlio sostituirebbe un padre o una madre diversi già registrati.
    public bool WouldReplaceParent(int parentId, int childId, int? otherParentId)
    {
        var sesso = GetSex(parentId);
        if (sesso is not (Sessi.Maschio or Sessi.Femmina))
        {
            return false;
        }

        var (motherId, fatherId) = GetParents(childId);
        var ownSlot = sesso == Sessi.Femmina ? motherId : fatherId;
        var otherSlot = sesso == Sessi.Femmina ? fatherId : motherId;
        return (ownSlot is int own && own != parentId)
            || (otherParentId is int other && otherSlot is int existing && existing != other);
    }

    public int SaveUnion(Unione data)
    {
        if (data.Persona1Id == data.Persona2Id)
        {
            throw new InvalidOperationException("Una persona non può essere coniuge di sé stessa.");
        }

        var entity = data.Id == 0
            ? new Unione()
            : context.Unioni.Find(data.Id) ?? throw new InvalidOperationException("Unione non trovata.");
        entity.Persona1Id = data.Persona1Id;
        entity.Persona2Id = data.Persona2Id;
        entity.Tipo = data.Tipo.Trim();
        entity.Inizio = data.Inizio.Trim();
        entity.Fine = data.Fine.Trim();
        entity.Note = data.Note.Trim();
        if (data.Id == 0)
        {
            context.Unioni.Add(entity);
        }

        SaveOrRevert();
        return entity.Id;
    }

    public void DeleteUnion(int id)
    {
        var unione = context.Unioni.Find(id);
        if (unione is null)
        {
            return;
        }

        context.Unioni.Remove(unione);
        SaveOrRevert();
    }

    public Relazione? GetRelation(int id) => context.Relazioni.AsNoTracking().FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<Relazione> GetRelations() => [.. context.Relazioni.AsNoTracking()];

    // Relazioni non familiari della persona, lette dal suo punto di vista («Maestro di…», «Allieva di…»).
    public IReadOnlyList<RelationView> GetRelationsForPerson(int personId)
    {
        var sesso = GetSex(personId);
        return [.. context.Relazioni.AsNoTracking()
            .Where(r => r.Persona1Id == personId || r.Persona2Id == personId)
            .OrderBy(r => r.Id)
            .AsEnumerable()
            .Select(r =>
            {
                var attivo = r.Persona1Id == personId;
                return Summary(attivo ? r.Persona2Id : r.Persona1Id) is { } altro
                    ? new RelationView(r.Id, altro, Relazioni.Etichetta(r.Tipo, attivo, sesso), r.Inizio, r.Fine, r.Note)
                    : null;
            })
            .OfType<RelationView>()
            .OrderBy(v => v.Etichetta, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(v => v.Altro.Nome, StringComparer.CurrentCultureIgnoreCase)];
    }

    // Vero se esiste già la stessa relazione tra le due persone (per i tipi simmetrici in qualunque verso).
    public bool HasRelation(int persona1Id, int persona2Id, string tipo)
    {
        var nome = Relazioni.Find(tipo)?.Nome ?? tipo.Trim();
        var simmetrica = Relazioni.Find(tipo)?.Simmetrica ?? true;
        return context.Relazioni.AsNoTracking()
            .Where(r => (r.Persona1Id == persona1Id && r.Persona2Id == persona2Id) || (r.Persona1Id == persona2Id && r.Persona2Id == persona1Id))
            .AsEnumerable()
            .Any(r => string.Equals(r.Tipo, nome, StringComparison.CurrentCultureIgnoreCase) && (simmetrica || r.Persona1Id == persona1Id));
    }

    public int SaveRelation(Relazione data)
    {
        if (data.Persona1Id == data.Persona2Id)
        {
            throw new InvalidOperationException("Una persona non può avere una relazione con sé stessa.");
        }

        var tipo = Relazioni.Find(data.Tipo)?.Nome ?? data.Tipo.Trim();
        if (tipo.Length == 0)
        {
            throw new InvalidOperationException("Indica il tipo di relazione.");
        }

        var entity = data.Id == 0
            ? new Relazione()
            : context.Relazioni.Find(data.Id) ?? throw new InvalidOperationException("Relazione non trovata.");
        entity.Persona1Id = data.Persona1Id;
        entity.Persona2Id = data.Persona2Id;
        entity.Tipo = tipo;
        entity.Inizio = data.Inizio.Trim();
        entity.Fine = data.Fine.Trim();
        entity.Note = data.Note.Trim();
        if (data.Id == 0)
        {
            context.Relazioni.Add(entity);
        }

        SaveOrRevert();
        return entity.Id;
    }

    public void DeleteRelation(int id)
    {
        if (context.Relazioni.Find(id) is not { } relazione)
        {
            return;
        }

        context.Relazioni.Remove(relazione);
        SaveOrRevert();
    }

    private void Apply(Action changes)
    {
        try
        {
            changes();
            SaveOrRevert();
        }
        catch (InvalidOperationException)
        {
            RevertPendingChanges();
            throw;
        }
    }

    private void ApplyParent(int childId, int? parentId, bool mother)
    {
        var child = context.People.Find(childId) ?? throw new InvalidOperationException("Scheda non trovata.");
        if (parentId is int id)
        {
            if (id == childId)
            {
                throw new InvalidOperationException("Una persona non può essere genitore di sé stessa.");
            }

            if (IsDescendant(id, childId))
            {
                throw new InvalidOperationException("La persona scelta è un discendente: si creerebbe un ciclo nell’albero.");
            }

            var parent = context.People.Find(id) ?? throw new InvalidOperationException("Scheda non trovata.");
            var expected = mother ? Sessi.Femmina : Sessi.Maschio;
            if (parent.Sesso.Length > 0 && parent.Sesso != expected)
            {
                throw new InvalidOperationException(mother
                    ? $"{parent.Name} risulta di sesso maschile e non può essere indicato come madre."
                    : $"{parent.Name} risulta di sesso femminile e non può essere indicata come padre.");
            }

            parent.Sesso = expected;
        }

        if (mother)
        {
            child.MotherId = parentId;
        }
        else
        {
            child.FatherId = parentId;
        }
    }

    private bool IsDescendant(int candidateId, int ancestorId)
    {
        var pending = new Queue<int>([ancestorId]);
        var seen = new HashSet<int> { ancestorId };
        while (pending.TryDequeue(out var current))
        {
            var childIds = context.People
                .Where(p => p.MotherId == current || p.FatherId == current)
                .Select(p => p.Id)
                .ToList();
            foreach (var childId in childIds)
            {
                if (childId == candidateId)
                {
                    return true;
                }

                if (seen.Add(childId))
                {
                    pending.Enqueue(childId);
                }
            }
        }

        return false;
    }

    private PersonSummary? Summary(int? personId)
    {
        if (personId is not int id)
        {
            return null;
        }

        var p = context.People.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Name, x.Surname, x.Suffix, x.Birth, x.Death, x.Sesso })
            .FirstOrDefault();
        return p is null ? null : Summarize(p.Id, p.Name, p.Surname, p.Suffix, p.Birth, p.Death, p.Sesso);
    }

    private static PersonSummary Summarize(int id, string name, string surname, string suffix, string birth, string death, string sesso)
    {
        var nome = HistoricalPerson.ComposeName(name, surname, suffix);
        var anni = string.IsNullOrWhiteSpace(birth) && string.IsNullOrWhiteSpace(death)
            ? string.Empty
            : $"{(string.IsNullOrWhiteSpace(birth) ? "?" : birth)} – {(string.IsNullOrWhiteSpace(death) ? "?" : death)}";
        return new PersonSummary(id, nome, anni, sesso);
    }

    public void ExportJson(IEnumerable<HistoricalPerson> people)
    {
        if (jsonBackupLocked)
        {
            throw new IOException("Il backup JSON non importato non è stato salvato altrove: non viene sovrascritto.");
        }

        var backup = new ArchiveBackup
        {
            People = [.. people],
            Unions = [.. context.Unioni.AsNoTracking()],
            Relations = [.. context.Relazioni.AsNoTracking()],
            Dynasties = [.. context.Dinastie.AsNoTracking()],
            Events = [.. context.Eventi.AsNoTracking().Include(e => e.Partecipanti)],
            Artworks = [.. context.Opere.AsNoTracking().Include(o => o.Persone)],
            Places = [.. context.Luoghi.AsNoTracking()],
            SpecificPlaces = [.. context.LuoghiSpecifici.AsNoTracking()],
            Attachments = [.. context.Allegati.AsNoTracking()],
        };
        // Scrittura su file temporaneo e sostituzione: un'interruzione non lascia un backup troncato.
        var temp = jsonBackupPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backup, JsonOptions));
        File.Move(temp, jsonBackupPath, overwrite: true);
    }

    private void ImportFromJson()
    {
        try
        {
            var backup = DeserializeBackup(File.ReadAllText(jsonBackupPath));
            if (backup.People.Count == 0)
            {
                // Senza persone si ripristinano comunque luoghi, dinastie ed eventi, ma solo su un archivio
                // davvero vuoto: altrimenti a ogni avvio verrebbero duplicati.
                var nothingToRestore = backup.Places.Count == 0 && backup.SpecificPlaces.Count == 0
                    && backup.Dynasties.Count == 0 && backup.Events.Count == 0 && backup.Artworks.Count == 0;
                if (nothingToRestore || context.Dinastie.Any() || context.Eventi.Any() || context.LuoghiSpecifici.Any() || context.Opere.Any())
                {
                    return;
                }

                backup.Unions.Clear();
                backup.Relations.Clear();
                foreach (var evento in backup.Events)
                {
                    evento.Partecipanti.Clear();
                }

                foreach (var opera in backup.Artworks)
                {
                    opera.Persone.Clear();
                }
            }

            using var transaction = context.Database.BeginTransaction();

            // I luoghi vengono ricreati (o riusati se già presenti) con id nuovi: i riferimenti delle schede vanno rimappati.
            var placeIds = RestorePlaces(backup.Places);
            var specificIds = RestoreSpecificPlaces(backup.SpecificPlaces, placeIds);
            foreach (var person in backup.People)
            {
                person.BirthLuogoId = Remap(placeIds, person.BirthLuogoId);
                person.DeathLuogoId = Remap(placeIds, person.DeathLuogoId);
                person.BurialLuogoId = Remap(placeIds, person.BurialLuogoId);
                person.BirthLuogoSpecificoId = Remap(specificIds, person.BirthLuogoSpecificoId);
                person.DeathLuogoSpecificoId = Remap(specificIds, person.DeathLuogoSpecificoId);
                person.BurialLuogoSpecificoId = Remap(specificIds, person.BurialLuogoSpecificoId);
            }

            foreach (var evento in backup.Events)
            {
                evento.LuogoId = Remap(placeIds, evento.LuogoId);
                evento.LuogoSpecificoId = Remap(specificIds, evento.LuogoSpecificoId);
            }

            foreach (var opera in backup.Artworks)
            {
                opera.LuogoId = Remap(placeIds, opera.LuogoId);
                opera.LuogoSpecificoId = Remap(specificIds, opera.LuogoSpecificoId);
            }

            // Gli allegati dei luoghi seguono i nuovi id; quelli di persone non ripristinate vengono scartati.
            var attachments = new List<Allegato>();
            foreach (var allegato in backup.Attachments.Where(a => AmbitiAllegato.IsValid(a.Ambito)))
            {
                if (allegato.Ambito == AmbitiAllegato.Luogo)
                {
                    if (Remap(placeIds, allegato.SchedaId) is not int luogoId)
                    {
                        continue;
                    }

                    allegato.SchedaId = luogoId;
                }
                else if (allegato.Ambito == AmbitiAllegato.Persona && !backup.People.Any(p => p.Id == allegato.SchedaId))
                {
                    continue;
                }

                allegato.Id = 0;
                attachments.Add(allegato);
            }

            // Le unioni riferiscono le persone: EF ordina gli inserimenti nello stesso SaveChanges.
            context.People.AddRange(backup.People);
            context.Unioni.AddRange(backup.Unions);
            context.Relazioni.AddRange(backup.Relations);
            context.Dinastie.AddRange(backup.Dynasties);
            context.Eventi.AddRange(backup.Events);
            context.Opere.AddRange(backup.Artworks);
            context.Allegati.AddRange(attachments);
            context.SaveChanges();
            context.ChangeTracker.Clear();
            // I collegamenti scheda–dinastia non sono nel backup: si ricavano dai nomi delle famiglie.
            RebuildDynastyLinks();
            transaction.Commit();
            context.ChangeTracker.Clear();
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            // Le entità non salvate non devono restare nel contesto e finire nel salvataggio successivo.
            context.ChangeTracker.Clear();
            PreserveUnimportedBackup();
            throw;
        }
        catch (JsonException)
        {
            // Backup JSON illeggibile: si parte da un database vuoto, conservandone una copia.
            PreserveUnimportedBackup();
        }
    }

    // Il primo salvataggio successivo riscriverebbe people.json con un archivio vuoto: il file originale viene copiato a parte.
    private void PreserveUnimportedBackup()
    {
        try
        {
            var target = Path.Combine(baseDir, $"people-non-importato-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            for (var i = 2; File.Exists(target); i++)
            {
                target = Path.Combine(baseDir, $"people-non-importato-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.json");
            }

            File.Copy(jsonBackupPath, target);
            UnimportedBackupPath = target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            jsonBackupLocked = true;
        }
    }

    // Un riferimento non presente nel backup (es. backup senza luoghi) viene scollegato invece di far fallire l'import.
    private static int? Remap(Dictionary<int, int> ids, int? id)
        => id is int value && ids.TryGetValue(value, out var mapped) ? mapped : null;

    // Ricrea i luoghi dal genitore verso i figli, riusando quelli già presenti (es. Italia e regioni create all'avvio).
    // Restituisce la corrispondenza tra id del backup e id nell'archivio.
    private Dictionary<int, int> RestorePlaces(List<Luogo> places)
    {
        var byId = places.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        var map = new Dictionary<int, int>();
        var inProgress = new HashSet<int>();

        int Ensure(Luogo luogo)
        {
            if (map.TryGetValue(luogo.Id, out var known))
            {
                return known;
            }

            inProgress.Add(luogo.Id);
            int? parentId = luogo.ParentId is int p && byId.TryGetValue(p, out var parent) && !inProgress.Contains(p)
                ? Ensure(parent)
                : null;
            inProgress.Remove(luogo.Id);

            var tipo = luogo.Tipo.Trim();
            var nome = luogo.Nome.Trim();
            var existing = context.Luoghi
                .Where(l => l.ParentId == parentId && l.Tipo == tipo && l.Nome == nome)
                .Select(l => (int?)l.Id)
                .FirstOrDefault();
            if (existing is not int id)
            {
                var created = new Luogo
                {
                    Nome = nome,
                    Tipo = tipo,
                    ParentId = parentId,
                    Latitudine = luogo.Latitudine,
                    Longitudine = luogo.Longitudine,
                    NomiAltreLingue = luogo.NomiAltreLingue.Select(n => new PlaceName { Language = n.Language, Name = n.Name }).ToList(),
                    SenzaLivelloInferiore = luogo.SenzaLivelloInferiore,
                    Storico = luogo.Storico,
                    Periodo = luogo.Periodo,
                    Note = luogo.Note,
                    NomiStorici = luogo.NomiStorici.Select(n => n.Clone()).ToList(),
                    WikidataId = luogo.WikidataId ?? string.Empty,
                };
                context.Luoghi.Add(created);
                context.SaveChanges();
                id = created.Id;
            }

            map[luogo.Id] = id;
            return id;
        }

        foreach (var luogo in byId.Values)
        {
            Ensure(luogo);
        }

        return map;
    }

    private Dictionary<int, int> RestoreSpecificPlaces(List<LuogoSpecifico> specifics, Dictionary<int, int> placeIds)
    {
        var map = new Dictionary<int, int>();
        foreach (var specifico in specifics)
        {
            if (map.ContainsKey(specifico.Id) || !placeIds.TryGetValue(specifico.LuogoId, out var luogoId))
            {
                continue;
            }

            var nome = specifico.Nome.Trim();
            var existing = context.LuoghiSpecifici
                .Where(s => s.LuogoId == luogoId && s.Nome == nome)
                .Select(s => (int?)s.Id)
                .FirstOrDefault();
            if (existing is not int id)
            {
                var created = new LuogoSpecifico
                {
                    Nome = nome,
                    Tipo = specifico.Tipo,
                    LuogoId = luogoId,
                    Indirizzo = specifico.Indirizzo,
                    Condizione = specifico.Condizione,
                    DataDistruzione = specifico.DataDistruzione,
                    Latitudine = specifico.Latitudine,
                    Longitudine = specifico.Longitudine,
                    Note = specifico.Note,
                    WikidataId = specifico.WikidataId,
                };
                context.LuoghiSpecifici.Add(created);
                context.SaveChanges();
                id = created.Id;
            }

            map[specifico.Id] = id;
        }

        return map;
    }

    // Compatibile sia col formato attuale (oggetto con persone e unioni) sia col vecchio (solo elenco persone).
    private static ArchiveBackup DeserializeBackup(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return new ArchiveBackup { People = JsonSerializer.Deserialize<List<HistoricalPerson>>(json) ?? [] };
        }

        return JsonSerializer.Deserialize<ArchiveBackup>(json) ?? new ArchiveBackup();
    }

    private sealed class ArchiveBackup
    {
        public List<HistoricalPerson> People { get; set; } = [];
        public List<Unione> Unions { get; set; } = [];
        public List<Relazione> Relations { get; set; } = [];
        public List<Dinastia> Dynasties { get; set; } = [];
        public List<Evento> Events { get; set; } = [];
        public List<Opera> Artworks { get; set; } = [];
        public List<Luogo> Places { get; set; } = [];
        public List<LuogoSpecifico> SpecificPlaces { get; set; } = [];
        public List<Allegato> Attachments { get; set; } = [];
    }

    public void Dispose() => context.Dispose();
}
