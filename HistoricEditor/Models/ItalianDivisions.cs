namespace HistoricEditor;

// Regioni e province italiane di oggi (2026-11-15), per le tendine della finestra dei livelli e per dare all'import sempre
// lo stesso nome («città metropolitana di Napoli» di Wikidata → «Città metropolitana di Napoli»). Non si scrivono
// nell'archivio all'avvio: un livello entra nell'archivio solo quando un luogo lo usa. Elenco letto da Wikidata e corretto
// a mano: Valle d'Aosta senza province, Matera solo in Basilicata, Sardegna dopo la riforma del 2025 (con l'Ogliastra),
// Friuli-Venezia Giulia e Sicilia lette dalla regione che le contiene. Se un nome cambia, si aggiorna qui; nell'archivio
// il luogo si rinomina dalla sezione Luoghi e resta collegato alla voce dell'elenco tramite il codice Wikidata.
public static class ItalianDivisions
{
    public const string Country = "Italia";
    public const string CountryWikidataId = "Q38";
    public const string RegionType = "Regione";
    public const string ProvinceType = "Provincia";

    public sealed record Division(string Nome, string WikidataId);

    public sealed record Region(string Nome, string WikidataId, IReadOnlyList<Division> Province);

    public static readonly IReadOnlyList<Region> Regions =
    [
        new("Abruzzo", "Q1284",
        [
            new("Provincia dell'Aquila", "Q16189"),
            new("Provincia di Chieti", "Q16160"),
            new("Provincia di Pescara", "Q16242"),
            new("Provincia di Teramo", "Q16284"),
        ]),
        new("Basilicata", "Q1452",
        [
            new("Provincia di Matera", "Q16206"),
            new("Provincia di Potenza", "Q16249"),
        ]),
        new("Calabria", "Q1458",
        [
            new("Città metropolitana di Reggio Calabria", "Q3678586"),
            new("Provincia di Catanzaro", "Q16159"),
            new("Provincia di Cosenza", "Q16162"),
            new("Provincia di Crotone", "Q16165"),
            new("Provincia di Vibo Valentia", "Q16315"),
        ]),
        new("Campania", "Q1438",
        [
            new("Città metropolitana di Napoli", "Q18241891"),
            new("Provincia di Avellino", "Q16120"),
            new("Provincia di Benevento", "Q16134"),
            new("Provincia di Caserta", "Q16153"),
            new("Provincia di Salerno", "Q16271"),
        ]),
        new("Emilia-Romagna", "Q1263",
        [
            new("Città metropolitana di Bologna", "Q18288145"),
            new("Provincia di Ferrara", "Q16168"),
            new("Provincia di Forlì-Cesena", "Q16180"),
            new("Provincia di Modena", "Q16209"),
            new("Provincia di Parma", "Q16228"),
            new("Provincia di Piacenza", "Q16243"),
            new("Provincia di Ravenna", "Q16252"),
            new("Provincia di Reggio Emilia", "Q16254"),
            new("Provincia di Rimini", "Q16268"),
        ]),
        new("Friuli-Venezia Giulia", "Q1250",
        [
            new("Ente di decentramento regionale di Gorizia", "Q104182151"),
            new("Ente di decentramento regionale di Pordenone", "Q104155857"),
            new("Ente di decentramento regionale di Trieste", "Q104182228"),
            new("Ente di decentramento regionale di Udine", "Q104182212"),
        ]),
        new("Lazio", "Q1282",
        [
            new("Città metropolitana di Roma Capitale", "Q18288160"),
            new("Provincia di Frosinone", "Q16181"),
            new("Provincia di Latina", "Q16196"),
            new("Provincia di Rieti", "Q16267"),
            new("Provincia di Viterbo", "Q16318"),
        ]),
        new("Liguria", "Q1256",
        [
            new("Città metropolitana di Genova", "Q18288152"),
            new("Provincia della Spezia", "Q16188"),
            new("Provincia di Imperia", "Q16186"),
            new("Provincia di Savona", "Q16274"),
        ]),
        new("Lombardia", "Q1210",
        [
            new("Città metropolitana di Milano", "Q18288155"),
            new("Provincia di Bergamo", "Q16137"),
            new("Provincia di Brescia", "Q16144"),
            new("Provincia di Como", "Q16161"),
            new("Provincia di Cremona", "Q16163"),
            new("Provincia di Lecco", "Q16199"),
            new("Provincia di Lodi", "Q16201"),
            new("Provincia di Mantova", "Q16204"),
            new("Provincia di Monza e della Brianza", "Q16210"),
            new("Provincia di Pavia", "Q16231"),
            new("Provincia di Sondrio", "Q16282"),
            new("Provincia di Varese", "Q16299"),
        ]),
        new("Marche", "Q1279",
        [
            new("Provincia di Ancona", "Q16114"),
            new("Provincia di Ascoli Piceno", "Q16117"),
            new("Provincia di Fermo", "Q16167"),
            new("Provincia di Macerata", "Q16203"),
            new("Provincia di Pesaro e Urbino", "Q16241"),
        ]),
        new("Molise", "Q1443",
        [
            new("Provincia di Campobasso", "Q16151"),
            new("Provincia di Isernia", "Q16187"),
        ]),
        new("Piemonte", "Q1216",
        [
            new("Città metropolitana di Torino", "Q18288162"),
            new("Provincia del Verbano-Cusio-Ossola", "Q16312"),
            new("Provincia di Alessandria", "Q15097"),
            new("Provincia di Asti", "Q15099"),
            new("Provincia di Biella", "Q15094"),
            new("Provincia di Cuneo", "Q15091"),
            new("Provincia di Novara", "Q16216"),
            new("Provincia di Vercelli", "Q15096"),
        ]),
        new("Puglia", "Q1447",
        [
            new("Città metropolitana di Bari", "Q18241854"),
            new("Provincia di Barletta-Andria-Trani", "Q16124"),
            new("Provincia di Brindisi", "Q16148"),
            new("Provincia di Foggia", "Q15129"),
            new("Provincia di Lecce", "Q16197"),
            new("Provincia di Taranto", "Q16283"),
        ]),
        new("Sardegna", "Q1462",
        [
            new("Città metropolitana di Cagliari", "Q3622022"),
            new("Città metropolitana di Sassari", "Q106997185"),
            new("Provincia del Medio Campidano", "Q16207"),
            new("Provincia del Sulcis Iglesiente", "Q106997093"),
            new("Provincia dell'Ogliastra", "Q16223"),
            new("Provincia della Gallura Nord-Est Sardegna", "Q106996869"),
            new("Provincia di Nuoro", "Q16220"),
            new("Provincia di Oristano", "Q16225"),
        ]),
        new("Sicilia", "Q1460",
        [
            new("Città metropolitana di Catania", "Q20991246"),
            new("Città metropolitana di Messina", "Q20991250"),
            new("Città metropolitana di Palermo", "Q20795016"),
            new("Libero consorzio comunale di Agrigento", "Q26160406"),
            new("Libero consorzio comunale di Caltanissetta", "Q26160410"),
            new("Libero consorzio comunale di Enna", "Q26160408"),
            new("Libero consorzio comunale di Ragusa", "Q26160405"),
            new("Libero consorzio comunale di Siracusa", "Q25510290"),
            new("Libero consorzio comunale di Trapani", "Q26160403"),
        ]),
        new("Toscana", "Q1273",
        [
            new("Città metropolitana di Firenze", "Q18288148"),
            new("Provincia di Arezzo", "Q16115"),
            new("Provincia di Grosseto", "Q16185"),
            new("Provincia di Livorno", "Q16200"),
            new("Provincia di Lucca", "Q16202"),
            new("Provincia di Massa-Carrara", "Q16205"),
            new("Provincia di Pisa", "Q16244"),
            new("Provincia di Pistoia", "Q16245"),
            new("Provincia di Prato", "Q16250"),
            new("Provincia di Siena", "Q16275"),
        ]),
        new("Trentino-Alto Adige", "Q1237",
        [
            new("Provincia autonoma di Bolzano", "Q15124"),
            new("Provincia autonoma di Trento", "Q16289"),
        ]),
        new("Umbria", "Q1280",
        [
            new("Provincia di Perugia", "Q16240"),
            new("Provincia di Terni", "Q16286"),
        ]),
        new("Valle d'Aosta", "Q1222", []),
        new("Veneto", "Q1243",
        [
            new("Città metropolitana di Venezia", "Q3678587"),
            new("Provincia di Belluno", "Q16126"),
            new("Provincia di Padova", "Q16226"),
            new("Provincia di Rovigo", "Q16270"),
            new("Provincia di Treviso", "Q16292"),
            new("Provincia di Verona", "Q16314"),
            new("Provincia di Vicenza", "Q16316"),
        ]),
    ];

    public static IEnumerable<Division> RegionDivisions => Regions.Select(r => new Division(r.Nome, r.WikidataId));

    public static Region? FindRegion(string nome)
        => Regions.FirstOrDefault(r => SameName(r.Nome, nome) || SameName(r.WikidataId, nome));

    // Province della regione indicata (nome dell'elenco o scritto diversamente); vuoto per una regione sconosciuta.
    public static IReadOnlyList<Division> ProvincesOf(string regione) => FindRegion(regione)?.Province ?? [];

    // La voce dell'elenco per un livello italiano, dal codice Wikidata o dal nome, senza badare alle maiuscole.
    public static Division? Find(string tipo, string nome, string? wikidataId = null)
    {
        var candidates = string.Equals(tipo, RegionType, StringComparison.OrdinalIgnoreCase) ? RegionDivisions
            : string.Equals(tipo, ProvinceType, StringComparison.OrdinalIgnoreCase) ? Regions.SelectMany(r => r.Province)
            : [];
        var list = candidates.ToList();
        return (wikidataId is { Length: > 0 } ? list.FirstOrDefault(d => d.WikidataId == wikidataId) : null)
            ?? list.FirstOrDefault(d => SameName(d.Nome, nome))
            ?? list.FirstOrDefault(d => SameName(d.Nome, ShortRegionName(nome)));
    }

    // Livelli di un luogo italiano importato: regione e provincia prendono il nome dell'elenco, riconosciute dal codice
    // Wikidata della suddivisione (se è tra quelle lette) o dal nome.
    public static void Normalize(IReadOnlyList<GeoLevel> levels, IEnumerable<PlaceDivision> divisions)
    {
        if (levels.Count == 0 || !SameName(levels[0].Nome, Country))
        {
            return;
        }

        var ids = divisions
            .Where(d => d.Name.Trim().Length > 0 && d.WikidataId.Length > 0)
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().WikidataId, StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.Skip(1))
        {
            if (!level.Assente && Find(level.Tipo, level.Nome.Trim(), ids.GetValueOrDefault(level.Nome.Trim())) is { } division)
            {
                level.Nome = division.Nome;
            }
        }
    }

    // «Trentino-Alto Adige/Südtirol» e «Valle d'Aosta/Vallée d'Aoste» come scritti da Wikidata.
    private static string ShortRegionName(string nome) => nome.Split('/')[0].Trim();

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
