using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Partecipante proposto da Wikidata: va abbinato a una scheda dell'archivio tramite il Q-id.
public sealed record WikipediaEventParticipant(string WikidataId, string Nome, string Ruolo);

// Dati di un evento letti da Wikipedia in italiano e da Wikidata, da verificare prima del salvataggio.
public sealed class WikipediaEventImport
{
    public string Titolo { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public string DataQualificatore { get; init; } = DataEvento.Esatta;
    public string Data1 { get; init; } = string.Empty;
    public string Data2 { get; init; } = string.Empty;
    // "giuliano" se Wikidata registra le date nel calendario giuliano.
    public string Calendario { get; init; } = string.Empty;
    public string Descrizione { get; init; } = string.Empty;
    public string WikidataId { get; init; } = string.Empty;
    public string ArticleUrl { get; init; } = string.Empty;

    // Vero se la voce è una pagina di disambiguazione (es. «Battaglia di Pavia»): va scelta una voce più specifica.
    public bool Disambiguazione { get; init; }

    public List<WikipediaEventParticipant> Partecipanti { get; init; } = [];

    // Luogo da Wikidata (P276, in mancanza P131), con la gerarchia amministrativa da confermare come per le schede.
    public HistoricalPlaceRecord? Luogo { get; init; }
}

// Import di eventi (battaglie, trattati, paci…) da Wikipedia e Wikidata.
public sealed partial class WikipediaImportService
{
    public const int EventImportSteps = 5;

    private const string HumanQid = "Q5";
    private const string CircaQid = "Q5727902";

    // Parole delle etichette italiane del tipo Wikidata (P31) e tipo di evento proposto; l'ordine conta («trattato di pace» → Pace).
    private static readonly (string Parola, string Tipo)[] EventTypeKeywords =
    [
        ("battaglia", "Battaglia"), ("assedio", "Assedio"), ("pace", "Pace"), ("armistizio", "Pace"),
        ("trattato", "Trattato"), ("accordo", "Trattato"), ("alleanza", "Alleanza"), ("lega", "Alleanza"),
        ("guerra", "Guerra"), ("conflitto", "Guerra"), ("concilio", "Concilio"), ("sinodo", "Concilio"),
        ("congiura", "Congiura"), ("colpo di stato", "Congiura"), ("cospirazione", "Congiura"),
        ("ribellione", "Rivolta"), ("rivolta", "Rivolta"), ("insurrezione", "Rivolta"), ("rivoluzione", "Rivolta"),
        ("incoronazione", "Incoronazione"), ("abdicazione", "Abdicazione"), ("elezione", "Elezione"), ("conclave", "Elezione"),
        ("spedizione", "Esplorazione"), ("esplorazione", "Esplorazione"), ("scoperta", "Scoperta"), ("invenzione", "Invenzione"),
        ("fondazione", "Fondazione"), ("viaggio", "Viaggio"),
        ("riunione", "Incontro"), ("incontro", "Incontro"), ("conferenza", "Incontro"), ("congresso", "Incontro"), ("convegno", "Incontro"),
    ];

    public async Task<WikipediaEventImport> ImportEventAsync(string articleTitle, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var reporter = new ImportProgressReporter(progress, importSteps: EventImportSteps);
        void Step(int step, string text) => reporter.Step(step, text);

        Step(1, "Lettura della pagina Wikipedia…");
        var pageUrl = $"{WikipediaApi}?action=query&prop=pageprops%7Cextracts&exintro=1&explaintext=1&ppprop=wikibase_item%7Cdisambiguation&titles={Uri.EscapeDataString(articleTitle)}&redirects=1&format=json&utf8=1";
        string title, wikidataId, extract;
        bool disambiguation;
        using (var pageDocument = await GetJsonAsync(pageUrl, cancellationToken))
        {
            var page = ApiProperty(ApiProperty(pageDocument.RootElement, "query"), "pages").EnumerateObject().First().Value;
            title = GetString(page, "title", articleTitle);
            var pageProps = page.GetPropertyOrDefault("pageprops");
            wikidataId = GetString(pageProps, "wikibase_item");
            disambiguation = pageProps.ValueKind == JsonValueKind.Object && pageProps.TryGetProperty("disambiguation", out _);
            extract = GetString(page, "extract");
        }

        var articleUrl = $"https://it.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
        if (disambiguation || !IsEntityId(wikidataId))
        {
            return new WikipediaEventImport
            {
                Titolo = title,
                Descrizione = disambiguation ? string.Empty : SummarizeExtract(extract),
                ArticleUrl = articleUrl,
                Disambiguazione = disambiguation,
            };
        }

        Step(2, "Lettura delle affermazioni Wikidata…");
        var entities = await ReadEntitiesAsync([wikidataId], cancellationToken);
        if (!entities.TryGetValue(wikidataId, out var entity))
        {
            return new WikipediaEventImport { Titolo = title, Descrizione = SummarizeExtract(extract), ArticleUrl = articleUrl };
        }

        return await BuildEventAsync(title, wikidataId, entity, SummarizeExtract(extract), articleUrl, reporter, cancellationToken);
    }

    // Evento indicato da Wikidata su una scheda: dalla voce italiana se esiste, altrimenti dalla sola Wikidata.
    // Restituisce null per i concetti generici («incoronazione», sottoclassi) e per gli elementi senza alcuna data.
    public async Task<WikipediaEventImport?> ImportEventByIdAsync(string wikidataId, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsEntityId(wikidataId))
        {
            return null;
        }

        string italianTitle;
        using (var document = await GetJsonAsync(
            $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(wikidataId)}&props=claims%7Csitelinks&sitefilter=itwiki&format=json",
            cancellationToken))
        {
            var entity = ApiProperty(document.RootElement, "entities").GetPropertyOrDefault(wikidataId);
            if (entity.ValueKind != JsonValueKind.Object || entity.TryGetProperty("missing", out _) || !IsDatedEvent(entity.GetPropertyOrDefault("claims")))
            {
                return null;
            }

            italianTitle = ReadItalianSitelinkTitle(document.RootElement, wikidataId);
        }

        if (italianTitle.Length > 0)
        {
            var import = await ImportEventAsync(italianTitle, progress, cancellationToken);
            return import.Disambiguazione ? null : import;
        }

        var reporter = new ImportProgressReporter(progress, importSteps: EventImportSteps);
        reporter.Step(2, "Lettura delle affermazioni Wikidata…");
        var entities = await ReadEntitiesAsync([wikidataId], cancellationToken);
        if (!entities.TryGetValue(wikidataId, out var full))
        {
            return null;
        }

        var labels = ReadLabels(full);
        var label = labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", string.Empty));
        return label.Length == 0 ? null : await BuildEventAsync(label, wikidataId, full, string.Empty, string.Empty, reporter, cancellationToken);
    }

    // Un evento vero e proprio ha almeno una data e non è una classe di eventi (P279 «sottoclasse di»).
    internal static bool IsDatedEvent(JsonElement claims)
        => !Statements(claims, "P279").Any() && ReadEventDates(claims) is var (_, data1, data2) && (data1.Length > 0 || data2.Length > 0);

    private async Task<WikipediaEventImport> BuildEventAsync(string title, string wikidataId, JsonElement entity, string description, string articleUrl, ImportProgressReporter reporter, CancellationToken cancellationToken)
    {
        void Step(int step, string text) => reporter.Step(step, text);
        var claims = entity.GetPropertyOrDefault("claims");
        var (qualifier, data1, data2) = ReadEventDates(claims);
        var typeIds = ClaimEntityIds(claims, "P31");
        var typeLabels = await ReadEntityLabelsAsync(typeIds, cancellationToken, italianOnly: true);
        var tipo = MapEventType(typeIds.Where(typeLabels.ContainsKey).Select(id => typeLabels[id]));

        Step(3, "Ricerca dei partecipanti…");
        var candidates = ReadParticipantClaims(claims);
        // Persone che indicano l'evento come «partecipò a» (P1344) o «evento significativo» (P793).
        foreach (var id in await SearchLinkedItemsAsync(wikidataId, cancellationToken))
        {
            candidates.Add((id, "partecipante", string.Empty));
        }

        Step(4, "Lettura dei nomi dei partecipanti…");
        var participants = await ResolveParticipantsAsync(candidates, cancellationToken);

        Step(5, "Lettura del luogo…");
        var location = await ReadEventPlaceAsync(claims, reporter, cancellationToken);

        return new WikipediaEventImport
        {
            Titolo = title,
            Tipo = tipo,
            DataQualificatore = qualifier,
            Data1 = data1,
            Data2 = data2,
            Calendario = ReadEventCalendar(claims),
            Descrizione = description,
            WikidataId = wikidataId,
            ArticleUrl = articleUrl,
            Partecipanti = participants,
            Luogo = location,
        };
    }

    // Luogo dell'evento (P276), altrimenti l'unità amministrativa (P131); si usa il primo con una gerarchia leggibile.
    private static async Task<HistoricalPlaceRecord?> ReadEventPlaceAsync(JsonElement claims, ImportProgressReporter reporter, CancellationToken cancellationToken)
    {
        var statements = ReadEntityStatements(claims, "P276");
        if (statements.Count == 0)
        {
            statements = ReadEntityStatements(claims, "P131");
        }

        if (statements.Count == 0)
        {
            return null;
        }

        var placeEntities = await ReadPlaceHierarchyAsync(statements.Select(s => s.EntityId).Distinct(StringComparer.Ordinal), reporter, cancellationToken);
        var places = BuildPlaces(statements, placeEntities);
        await ReadInfoboxDivisionsAsync(places, placeEntities, cancellationToken);
        await ReadHistoricalNamesAsync(places, placeEntities, cancellationToken);
        await AssignPlaceTypesAsync(places, cancellationToken);
        return places.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.CurrentName));
    }

    private static async Task<List<string>> SearchLinkedItemsAsync(string wikidataId, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"haswbstatement:P1344={wikidataId}|P793={wikidataId}");
        using var document = await GetJsonAsync($"{WikidataApi}?action=query&list=search&srsearch={query}&srnamespace=0&srlimit=50&format=json", cancellationToken);
        return [.. document.RootElement.GetPropertyOrDefault("query").GetPropertyOrDefault("search") is { ValueKind: JsonValueKind.Array } results
            ? results.EnumerateArray().Select(r => GetString(r, "title")).Where(IsEntityId)
            : []];
    }

    // Tiene solo gli esseri umani (Q5): stati ed eserciti servono solo a descrivere il ruolo dei comandanti.
    private static async Task<List<WikipediaEventParticipant>> ResolveParticipantsAsync(List<(string Id, string Ruolo, string Parte)> candidates, CancellationToken cancellationToken)
    {
        var ids = candidates.SelectMany(c => new[] { c.Id, c.Parte }).Where(IsEntityId).Distinct(StringComparer.Ordinal).ToList();
        var entities = await ReadEntitiesAsync(ids, cancellationToken);
        string Label(string id) => entities.TryGetValue(id, out var e) && ReadLabels(e) is var l && l.Count > 0
            ? l.GetValueOrDefault("it", l.GetValueOrDefault("en", l.Values.First()))
            : id;

        var result = new List<WikipediaEventParticipant>();
        foreach (var group in candidates.GroupBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!entities.TryGetValue(group.Key, out var entity)
                || !ClaimEntityIds(entity.GetPropertyOrDefault("claims"), "P31").Contains(HumanQid))
            {
                continue;
            }

            // Ruolo più specifico tra quelli trovati (es. «comandante» prevale su «partecipante»).
            var best = group.OrderBy(c => c.Ruolo == "partecipante" ? 1 : 0).First();
            var ruolo = best.Parte.Length > 0 ? $"{best.Ruolo} ({Label(best.Parte)})" : best.Ruolo;
            result.Add(new WikipediaEventParticipant(group.Key, Label(group.Key), ruolo));
        }

        return [.. result.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)];
    }

    // P710 partecipante (con i comandanti nel qualificatore P4791), P1891 firmatario, P4791/P1344 diretti.
    internal static List<(string Id, string Ruolo, string Parte)> ReadParticipantClaims(JsonElement claims)
    {
        var result = new List<(string, string, string)>();
        foreach (var statement in Statements(claims, "P710"))
        {
            var participant = SnakEntityId(statement.GetPropertyOrDefault("mainsnak"));
            if (participant.Length > 0)
            {
                result.Add((participant, "partecipante", string.Empty));
            }

            foreach (var commander in QualifierEntityIds(statement, "P4791"))
            {
                result.Add((commander, "comandante", participant));
            }
        }

        foreach (var commander in ClaimEntityIds(claims, "P4791"))
        {
            result.Add((commander, "comandante", string.Empty));
        }

        foreach (var statement in Statements(claims, "P1891"))
        {
            var signatory = SnakEntityId(statement.GetPropertyOrDefault("mainsnak"));
            if (signatory.Length > 0)
            {
                result.Add((signatory, "firmatario", string.Empty));
            }
        }

        return result;
    }

    // Data puntuale (P585), altrimenti inizio/fine (P580/P582), altrimenti estremi incerti (P1319/P1326).
    internal static (string Qualificatore, string Data1, string Data2) ReadEventDates(JsonElement claims)
    {
        if (Statements(claims, "P585").FirstOrDefault() is { ValueKind: JsonValueKind.Object } point
            && ReadTime(point.GetPropertyOrDefault("mainsnak")) is { } pointTime)
        {
            var earliest = QualifierTime(point, "P1319");
            var latest = QualifierTime(point, "P1326");
            if (earliest.Length > 0 || latest.Length > 0)
            {
                return (DataEvento.Tra, earliest, latest);
            }

            return (QualifierEntityIds(point, "P1480").Contains(CircaQid) ? DataEvento.Circa : DataEvento.Esatta, pointTime, string.Empty);
        }

        var start = Statements(claims, "P580").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        var end = Statements(claims, "P582").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        if (start.Length > 0 || end.Length > 0)
        {
            return start == end ? (DataEvento.Esatta, start, string.Empty) : (DataEvento.DaA, start, end);
        }

        var first = Statements(claims, "P1319").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        var last = Statements(claims, "P1326").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        return first.Length > 0 || last.Length > 0 ? (DataEvento.Tra, first, last) : (DataEvento.Esatta, string.Empty, string.Empty);
    }

    // "giuliano" se una delle date dell'evento precisa al mese o al giorno è registrata da Wikidata nel calendario giuliano.
    internal static string ReadEventCalendar(JsonElement claims)
        => new[] { "P585", "P580", "P582", "P1319", "P1326" }
            .SelectMany(property => Statements(claims, property))
            .Select(s => s.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"))
            .Any(value => IsJulianCalendar(value) && value.GetPropertyOrDefault("precision") is { ValueKind: JsonValueKind.Number } p && p.GetInt32() >= 10)
                ? HistoricalCalendar.Giuliano
                : string.Empty;

    // Tipo di evento proposto dalle etichette italiane dei tipi Wikidata; in mancanza la prima etichetta con l'iniziale maiuscola.
    internal static string MapEventType(IEnumerable<string> italianLabels)
    {
        var labels = italianLabels.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        foreach (var label in labels)
        {
            var lower = label.ToLowerInvariant();
            foreach (var (parola, tipo) in EventTypeKeywords)
            {
                if (Regex.IsMatch(lower, $@"\b{Regex.Escape(parola)}\b", RegexOptions.CultureInvariant))
                {
                    return tipo;
                }
            }
        }

        var first = labels.FirstOrDefault(l => !string.Equals(l, "evento", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return first.Length == 0 ? string.Empty : char.ToUpperInvariant(first[0]) + first[1..];
    }

    // Il primo paragrafo dell'introduzione, troncato a fine frase oltre i 600 caratteri.
    internal static string SummarizeExtract(string extract)
    {
        var paragraph = (extract ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        const int limit = 600;
        if (paragraph.Length <= limit)
        {
            return paragraph;
        }

        var cut = paragraph.LastIndexOf(". ", limit, StringComparison.Ordinal);
        return cut > 0 ? paragraph[..(cut + 1)] : paragraph[..limit].TrimEnd() + "…";
    }

    private static IEnumerable<JsonElement> Statements(JsonElement claims, string propertyId)
        => claims.ValueKind == JsonValueKind.Object && claims.TryGetProperty(propertyId, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(s => GetString(s, "rank") != "deprecated")
            : [];

    private static List<string> ClaimEntityIds(JsonElement claims, string propertyId)
        => [.. Statements(claims, propertyId).Select(s => SnakEntityId(s.GetPropertyOrDefault("mainsnak"))).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)];

    private static IEnumerable<JsonElement> QualifierSnaks(JsonElement statement, string propertyId)
        => statement.GetPropertyOrDefault("qualifiers") is { ValueKind: JsonValueKind.Object } qualifiers
           && qualifiers.TryGetProperty(propertyId, out var snaks) && snaks.ValueKind == JsonValueKind.Array
            ? snaks.EnumerateArray()
            : [];

    private static List<string> QualifierEntityIds(JsonElement statement, string propertyId)
        => [.. QualifierSnaks(statement, propertyId).Select(SnakEntityId).Where(id => id.Length > 0)];

    private static string QualifierTime(JsonElement statement, string propertyId)
        => QualifierSnaks(statement, propertyId).Select(ReadTime).FirstOrDefault(t => t is not null) ?? string.Empty;

    private static string SnakEntityId(JsonElement snak)
    {
        var id = GetString(snak.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id");
        return IsEntityId(id) ? id : string.Empty;
    }

    private static string? ReadTime(JsonElement snak)
    {
        var value = snak.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
        var time = GetString(value, "time");
        if (time.Length == 0)
        {
            return null;
        }

        var precision = value.GetPropertyOrDefault("precision") is { ValueKind: JsonValueKind.Number } p ? p.GetInt32() : 9;
        return FormatDate(time, precision);
    }
}
