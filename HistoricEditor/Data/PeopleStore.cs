using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public sealed record PlaceDescription(string Classic, string Specific);

public sealed class PeopleStore : IDisposable
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

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        // VACUUM INTO produce una copia coerente anche con la connessione aperta.
        context.Database.ExecuteSql($"VACUUM INTO {destination}");
    }

    // Sostituisce l'archivio in uso; restituisce il percorso della copia di sicurezza di quello precedente.
    public string ImportDatabase(string source)
    {
        if (IsCurrentDatabase(source))
        {
            throw new InvalidOperationException("Il file selezionato è già l’archivio in uso.");
        }

        var backup = Path.Combine(baseDir, $"historic-{DateTime.Now:yyyyMMdd-HHmmss}.bak.db");
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

    public void Add(HistoricalPerson person)
    {
        context.People.Add(person);
        SaveOrRevert();
    }

    // Aggiorna i dati anagrafici di una scheda esistente senza toccare i legami familiari.
    public void Update(HistoricalPerson person)
    {
        var existing = context.People.Find(person.Id)
            ?? throw new InvalidOperationException("Scheda non trovata.");

        existing.Name = person.Name;
        existing.Suffix = person.Suffix;
        existing.Family = person.Family;
        existing.Sesso = person.Sesso;
        existing.Nickname = person.Nickname;
        existing.MainTitle = person.MainTitle;
        existing.Titles = person.Titles.Select(t => t.Clone()).ToList();
        existing.LocalizedNames = person.LocalizedNames.Select(n => n.Clone()).ToList();
        existing.Birth = person.Birth;
        existing.Death = person.Death;
        existing.BirthPlace = person.BirthPlace;
        existing.DeathPlace = person.DeathPlace;
        existing.BurialPlace = person.BurialPlace;
        existing.WikipediaTitle = person.WikipediaTitle;
        existing.WikipediaUrl = person.WikipediaUrl;
        existing.WikipediaExtract = person.WikipediaExtract;
        existing.WikidataId = person.WikidataId;
        existing.BirthDateEvidence = person.BirthDateEvidence;
        existing.DeathDateEvidence = person.DeathDateEvidence;
        existing.BirthPlaces = person.BirthPlaces;
        existing.DeathPlaces = person.DeathPlaces;
        existing.BurialPlaces = person.BurialPlaces;
        existing.BirthLuogoId = person.BirthLuogoId;
        existing.BirthLuogoSpecificoId = person.BirthLuogoSpecificoId;
        existing.DeathLuogoId = person.DeathLuogoId;
        existing.DeathLuogoSpecificoId = person.DeathLuogoSpecificoId;
        existing.BurialLuogoId = person.BurialLuogoId;
        existing.BurialLuogoSpecificoId = person.BurialLuogoSpecificoId;

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
        context.People.Remove(person);
        SaveOrRevert();
    }

    public IReadOnlyList<Dinastia> GetDynasties() => [.. context.Dinastie.AsNoTracking().OrderBy(d => d.Nome)];

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
            var oldName = entity.Nome;
            foreach (var person in context.People.Where(p => p.Family != null && p.Family.Length > 0).AsEnumerable()
                .Where(p => string.Equals(p.Family.Trim(), oldName, StringComparison.CurrentCultureIgnoreCase)))
            {
                person.Family = nome;
            }
        }

        entity.Nome = nome;
        entity.ParentId = data.ParentId;
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim();
        entity.Note = data.Note ?? string.Empty;
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

        context.Dinastie.Remove(entity);
        SaveOrRevert();
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
        => GeographyResolver.EnsureChain(context, levels, latitudine, longitudine)?.Id;

    public IReadOnlyList<Luogo> GetAllPlaces() => [.. context.Luoghi.AsNoTracking()];

    public bool PlaceExists(int id) => context.Luoghi.Any(l => l.Id == id);

    public int? FindPlace(int? parentId, string tipo, string nome)
    {
        tipo = tipo.Trim();
        nome = nome.Trim();
        return context.Luoghi
            .Where(l => l.ParentId == parentId && l.Tipo == tipo && l.Nome == nome)
            .Select(l => (int?)l.Id)
            .FirstOrDefault();
    }

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
        SaveOrRevert();
    }

    public void DeletePlace(int id)
    {
        var luogo = context.Luoghi.Find(id);
        if (luogo is null)
        {
            return;
        }

        context.Luoghi.Remove(luogo);
        SaveOrRevert();
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

        context.LuoghiSpecifici.Remove(specifico);
        SaveOrRevert();
    }

    // Riusa il luogo specifico già importato (stesso Wikidata o stesso nome nello stesso luogo).
    public int EnsureSpecificPlace(string nome, int luogoId, string wikidataId, double? latitudine = null, double? longitudine = null)
    {
        nome = nome.Trim();
        var existing = (wikidataId.Length > 0 ? context.LuoghiSpecifici.FirstOrDefault(s => s.WikidataId == wikidataId) : null)
            ?? context.LuoghiSpecifici.FirstOrDefault(s => s.LuogoId == luogoId && s.Nome == nome);
        if (existing is not null)
        {
            // Coordinate (P625) solo se mancanti, per non sovrascrivere correzioni manuali.
            existing.Latitudine ??= latitudine;
            existing.Longitudine ??= longitudine;
            SaveOrRevert();
            return existing.Id;
        }

        var created = new LuogoSpecifico { Nome = nome, LuogoId = luogoId, WikidataId = wikidataId, Latitudine = latitudine, Longitudine = longitudine };
        context.LuoghiSpecifici.Add(created);
        SaveOrRevert();
        return created.Id;
    }

    // In caso di errore annulla le modifiche pendenti, altrimenti resterebbero nel contesto e bloccherebbero i salvataggi successivi.
    private void SaveOrRevert()
    {
        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException)
        {
            RevertPendingChanges();
            throw;
        }
    }

    private void RevertPendingChanges()
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else
            {
                entry.Reload();
            }
        }
    }

    public IReadOnlyList<PersonSummary> GetPersonSummaries()
        => [.. context.People.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.Suffix, p.Birth, p.Death, p.Sesso })
            .AsEnumerable()
            .Select(p => Summarize(p.Id, p.Name, p.Suffix, p.Birth, p.Death, p.Sesso))
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
            .Select(p => new { p.Id, p.Name, p.Suffix, p.Birth, p.Death, p.Sesso, p.MotherId, p.FatherId })
            .ToList();

        // I figli sono raggruppati per altro genitore, cioè per unione.
        var others = new List<PersonSummary>();
        foreach (var child in HistoricalYear.OrderByYear(children, c => c.Birth))
        {
            var summary = Summarize(child.Id, child.Name, child.Suffix, child.Birth, child.Death, child.Sesso);
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
        if (sesso == Sessi.Maschio && context.People.Any(p => p.MotherId == personId))
        {
            throw new InvalidOperationException("È indicata come madre di qualcuno: rimuovi prima quel legame.");
        }

        if (sesso == Sessi.Femmina && context.People.Any(p => p.FatherId == personId))
        {
            throw new InvalidOperationException("È indicato come padre di qualcuno: rimuovi prima quel legame.");
        }

        person.Sesso = sesso;
        SaveOrRevert();
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
            .Select(x => new { x.Id, x.Name, x.Suffix, x.Birth, x.Death, x.Sesso })
            .FirstOrDefault();
        return p is null ? null : Summarize(p.Id, p.Name, p.Suffix, p.Birth, p.Death, p.Sesso);
    }

    private static PersonSummary Summarize(int id, string name, string suffix, string birth, string death, string sesso)
    {
        var nome = string.IsNullOrWhiteSpace(suffix) ? name : $"{name} {suffix}";
        var anni = string.IsNullOrWhiteSpace(birth) && string.IsNullOrWhiteSpace(death)
            ? string.Empty
            : $"{(string.IsNullOrWhiteSpace(birth) ? "?" : birth)} – {(string.IsNullOrWhiteSpace(death) ? "?" : death)}";
        return new PersonSummary(id, nome, anni, sesso);
    }

    public void ExportJson(IEnumerable<HistoricalPerson> people)
    {
        var backup = new ArchiveBackup
        {
            People = [.. people],
            Unions = [.. context.Unioni.AsNoTracking()],
            Dynasties = [.. context.Dinastie.AsNoTracking()],
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
                return;
            }

            // Le unioni riferiscono le persone: EF ordina gli inserimenti nello stesso SaveChanges.
            context.People.AddRange(backup.People);
            context.Unioni.AddRange(backup.Unions);
            context.Dinastie.AddRange(backup.Dynasties);
            context.SaveChanges();
        }
        catch (JsonException)
        {
            // Backup JSON illeggibile: si parte da un database vuoto.
        }
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
        public List<Dinastia> Dynasties { get; set; } = [];
    }

    public void Dispose() => context.Dispose();
}
