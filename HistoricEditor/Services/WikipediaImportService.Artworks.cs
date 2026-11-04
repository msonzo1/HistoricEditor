using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Import di opere d'arte da Wikipedia in italiano, Wikidata e Wikimedia Commons.
public sealed partial class WikipediaImportService
{
    public const int ArtworkImportSteps = 5;

    private const string AnonymousQid = "Q4233718";
    private const string PaintingSurfaceQid = "Q861259";
    private const string FrameQid = "Q1737943";

    // Qualificatori di P5102 «natura dell'affermazione» che indicano un'attribuzione non certa.
    private static readonly HashSet<string> UncertainCreatorQids = new(StringComparer.Ordinal)
    {
        "Q230768", "Q18122778", "Q56644435", "Q18912752",
    };

    private static readonly (string Parola, string Tipo)[] ArtworkTypeKeywords =
    [
        ("pala d'altare", "Pala d'altare"), ("polittico", "Pala d'altare"), ("affresco", "Affresco"), ("mosaico", "Mosaico"),
        ("vetrata", "Vetrata"), ("arazzo", "Arazzo"), ("manoscritto", "Manoscritto miniato"), ("codice miniato", "Manoscritto miniato"),
        ("miniatura", "Manoscritto miniato"), ("incisione", "Incisione"), ("acquaforte", "Incisione"), ("xilografia", "Incisione"),
        ("stampa", "Incisione"), ("disegno", "Disegno"), ("medaglia", "Medaglia"), ("statua", "Scultura"), ("scultura", "Scultura"),
        ("busto", "Scultura"), ("rilievo", "Scultura"), ("bassorilievo", "Scultura"), ("ritratto", "Ritratto"), ("dipinto", "Dipinto"),
        ("pittura", "Dipinto"), ("quadro", "Dipinto"), ("oreficeria", "Oreficeria"), ("gioiello", "Oreficeria"), ("reliquiario", "Oreficeria"),
        ("chiesa", "Architettura"), ("basilica", "Architettura"), ("cattedrale", "Architettura"), ("duomo", "Architettura"),
        ("cappella", "Architettura"), ("palazzo", "Architettura"), ("villa", "Architettura"), ("edificio", "Architettura"),
    ];

    private static readonly Dictionary<string, string> LengthUnits = new(StringComparer.Ordinal)
    {
        ["Q174728"] = "cm", ["Q174789"] = "mm", ["Q11573"] = "m", ["Q218593"] = "in",
    };

    public async Task<ArtworkImport> ImportArtworkAsync(string articleTitle, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var reporter = new ImportProgressReporter(progress, importSteps: ArtworkImportSteps);
        reporter.Step(1, "Lettura della pagina Wikipedia…");
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
        var summary = disambiguation ? string.Empty : SummarizeExtract(extract);
        if (disambiguation || !IsEntityId(wikidataId))
        {
            return new ArtworkImport { Titolo = title, Note = summary, Fonti = [articleUrl], Disambiguazione = disambiguation };
        }

        reporter.Step(2, "Lettura delle affermazioni Wikidata…");
        var entities = await ReadEntitiesAsync([wikidataId], cancellationToken);
        if (!entities.TryGetValue(wikidataId, out var entity))
        {
            return new ArtworkImport { Titolo = title, Note = summary, Fonti = [articleUrl] };
        }

        return await BuildArtworkAsync(title, wikidataId, entity, summary, articleUrl, reporter, cancellationToken);
    }

    private async Task<ArtworkImport> BuildArtworkAsync(string title, string wikidataId, JsonElement entity, string summary, string articleUrl, ImportProgressReporter reporter, CancellationToken cancellationToken)
    {
        var claims = entity.GetPropertyOrDefault("claims");
        var typeIds = ClaimEntityIds(claims, "P31");
        var materials = ReadMaterialClaims(claims);
        var labels = await ReadEntityLabelsAsync(typeIds.Concat(materials.Select(m => m.Id)), cancellationToken, italianOnly: true);
        string Label(string id) => labels.GetValueOrDefault(id, string.Empty);

        reporter.Step(3, "Lettura di autori, committenti e soggetti…");
        var people = await ResolveArtworkPeopleAsync(ReadArtworkPeopleClaims(claims), cancellationToken);

        reporter.Step(4, "Lettura della collocazione…");
        var (place, placeName) = await ReadArtworkPlaceAsync(claims, reporter, cancellationToken);

        reporter.Step(5, "Lettura dell’immagine da Wikimedia Commons…");
        var imageFile = Statements(claims, "P18").Select(s => GetSnakString(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(f => f.Length > 0) ?? string.Empty;
        var sources = new List<string>();
        if (articleUrl.Length > 0)
        {
            sources.Add(articleUrl);
        }

        sources.Add($"https://www.wikidata.org/wiki/{wikidataId}");
        if (imageFile.Length > 0)
        {
            sources.Add($"Immagine: {CommonsFilePage(imageFile)}");
        }

        return new ArtworkImport
        {
            Titolo = title,
            Tipo = MapArtworkType(typeIds.Select(Label).Where(l => l.Length > 0)),
            Data = ReadArtworkDate(claims),
            Tecnica = FormatTechnique(materials.Where(m => !m.Supporto).Select(m => Label(m.Id)), materials.Where(m => m.Supporto).Select(m => Label(m.Id))),
            Dimensioni = ReadDimensions(claims),
            Note = summary,
            WikidataId = wikidataId,
            Fonti = sources,
            Persone = people,
            Luogo = place,
            Collocazione = placeName,
            ImmagineUrl = imageFile.Length > 0 ? CommonsImageUrl(imageFile) : string.Empty,
        };
    }

    // Collocazione: collezione (P195, di solito il museo), altrimenti luogo (P276, es. la chiesa di un affresco).
    private static async Task<(HistoricalPlaceRecord? Place, string Name)> ReadArtworkPlaceAsync(JsonElement claims, ImportProgressReporter reporter, CancellationToken cancellationToken)
    {
        var statements = ReadEntityStatements(claims, "P195");
        if (statements.Count == 0)
        {
            statements = ReadEntityStatements(claims, "P276");
        }

        if (statements.Count == 0)
        {
            return (null, string.Empty);
        }

        var place = await ReadPlaceFromStatementsAsync(statements.Take(1).ToList(), reporter, cancellationToken);
        if (place is not null)
        {
            return (place, place.CurrentName);
        }

        var names = await ReadEntityLabelsAsync([statements[0].EntityId], cancellationToken);
        return (null, names.GetValueOrDefault(statements[0].EntityId, string.Empty));
    }

    // Luogo (es. un museo) a partire dal suo elemento Wikidata, con la gerarchia amministrativa.
    public async Task<HistoricalPlaceRecord?> ReadPlaceByIdAsync(string wikidataId, CancellationToken cancellationToken = default)
        => IsEntityId(wikidataId)
            ? await ReadPlaceFromStatementsAsync([new EntityStatement(wikidataId, [])], new ImportProgressReporter(null, importSteps: 1), cancellationToken)
            : null;

    private static async Task<HistoricalPlaceRecord?> ReadPlaceFromStatementsAsync(List<EntityStatement> statements, ImportProgressReporter reporter, CancellationToken cancellationToken)
    {
        var placeEntities = await ReadPlaceHierarchyAsync(statements.Select(s => s.EntityId).Distinct(StringComparer.Ordinal), reporter, cancellationToken);
        var places = BuildPlaces(statements, placeEntities);
        await ReadInfoboxDivisionsAsync(places, placeEntities, cancellationToken);
        await ReadHistoricalNamesAsync(places, placeEntities, cancellationToken);
        await AssignPlaceTypesAsync(places, cancellationToken);
        return places.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.CurrentName));
    }

    // Persona su Wikidata a partire dal nome dato da un museo; con gli anni si scartano gli omonimi. Vuoto se non trovata.
    public async Task<string> FindHumanAsync(string name, int? birthYear, int? deathYear, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        List<string> ids;
        using (var document = await GetJsonAsync(
            $"{WikidataApi}?action=wbsearchentities&search={Uri.EscapeDataString(name.Trim())}&language=en&uselang=it&type=item&limit=7&format=json",
            cancellationToken))
        {
            ids = document.RootElement.GetPropertyOrDefault("search") is { ValueKind: JsonValueKind.Array } results
                ? [.. results.EnumerateArray().Select(r => GetString(r, "id")).Where(IsEntityId)]
                : [];
        }

        var entities = await ReadEntitiesAsync(ids, cancellationToken);
        return ids.FirstOrDefault(id => entities.TryGetValue(id, out var entity) && IsMatchingHuman(entity.GetPropertyOrDefault("claims"), birthYear, deathYear)) ?? string.Empty;
    }

    // Umano (Q5) i cui anni di nascita e morte, se noti da entrambe le parti, coincidono (con un anno di tolleranza).
    internal static bool IsMatchingHuman(JsonElement claims, int? birthYear, int? deathYear)
    {
        if (!ClaimEntityIds(claims, "P31").Contains(HumanQid))
        {
            return false;
        }

        bool Matches(string property, int? expected)
        {
            if (expected is null)
            {
                return true;
            }

            var years = Statements(claims, property).Select(s => ReadYear(s.GetPropertyOrDefault("mainsnak"))).OfType<int>().ToList();
            return years.Count == 0 || years.Any(y => Math.Abs(y - expected.Value) <= 1);
        }

        return Matches("P569", birthYear) && Matches("P570", deathYear);
    }

    private static int? ReadYear(JsonElement snak)
    {
        var time = GetString(snak.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "time");
        var match = Regex.Match(time, @"^(?<sign>[+-])(?<year>\d+)-");
        return match.Success && int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? match.Groups["sign"].Value == "-" ? -year : year
            : null;
    }

    // Tiene solo gli esseri umani: «anonimo» o un'intera scuola pittorica non diventano schede.
    private static async Task<List<ArtworkImportPerson>> ResolveArtworkPeopleAsync(List<(string Id, string Ruolo)> candidates, CancellationToken cancellationToken)
    {
        var entities = await ReadEntitiesAsync(candidates.Select(c => c.Id), cancellationToken);
        var result = new List<ArtworkImportPerson>();
        foreach (var (id, ruolo) in candidates.DistinctBy(c => (c.Id, c.Ruolo)))
        {
            if (!entities.TryGetValue(id, out var entity) || !ClaimEntityIds(entity.GetPropertyOrDefault("claims"), "P31").Contains(HumanQid))
            {
                continue;
            }

            var labels = ReadLabels(entity);
            var name = labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", labels.Values.FirstOrDefault() ?? id));
            result.Add(new ArtworkImportPerson(id, name, ruolo));
        }

        return result;
    }

    // P170 autore (con «attribuito a» P1773, «bottega di» P1774, «possibile autore» P1779), P88 committente, P180 soggetto.
    internal static List<(string Id, string Ruolo)> ReadArtworkPeopleClaims(JsonElement claims)
    {
        var result = new List<(string, string)>();
        foreach (var statement in BestStatements(claims, "P170"))
        {
            var creator = SnakEntityId(statement.GetPropertyOrDefault("mainsnak"));
            var uncertain = QualifierEntityIds(statement, "P5102").Any(UncertainCreatorQids.Contains);
            if (creator.Length > 0 && creator != AnonymousQid)
            {
                result.Add((creator, uncertain ? OperaRuoli.Attribuita : OperaRuoli.Autore));
            }

            result.AddRange(QualifierEntityIds(statement, "P1773").Select(id => (id, OperaRuoli.Attribuita)));
            result.AddRange(QualifierEntityIds(statement, "P1779").Select(id => (id, OperaRuoli.Attribuita)));
            result.AddRange(QualifierEntityIds(statement, "P1774").Select(id => (id, OperaRuoli.Bottega)));
        }

        result.AddRange(ClaimEntityIds(claims, "P88").Select(id => (id, OperaRuoli.Committente)));
        result.AddRange(ClaimEntityIds(claims, "P180").Select(id => (id, OperaRuoli.Soggetto)));
        return [.. result.Distinct()];
    }

    // Data di creazione (P571) con «circa» e gli estremi incerti; in mancanza inizio e fine (P580/P582).
    internal static string ReadArtworkDate(JsonElement claims)
    {
        if (BestStatements(claims, "P571").FirstOrDefault() is { ValueKind: JsonValueKind.Object } statement)
        {
            var earliest = QualifierTime(statement, "P1319");
            var latest = QualifierTime(statement, "P1326");
            if (earliest.Length == 0 && latest.Length == 0)
            {
                earliest = QualifierTime(statement, "P580");
                latest = QualifierTime(statement, "P582");
            }

            if (earliest.Length > 0 && latest.Length > 0)
            {
                return earliest == latest ? earliest : $"{earliest}-{latest}";
            }

            if (ReadTime(statement.GetPropertyOrDefault("mainsnak")) is { } time)
            {
                return QualifierEntityIds(statement, "P1480").Contains(CircaQid) ? $"circa {time}" : time;
            }

            if (earliest.Length > 0 || latest.Length > 0)
            {
                return earliest.Length > 0 ? $"dopo il {earliest}" : $"prima del {latest}";
            }
        }

        var start = Statements(claims, "P580").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        var end = Statements(claims, "P582").Select(s => ReadTime(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(t => t is not null) ?? string.Empty;
        return start.Length > 0 && end.Length > 0 && start != end ? $"{start}-{end}" : start.Length > 0 ? start : end;
    }

    // Materiali (P186) del rango migliore, separando il supporto pittorico (qualificatore P518) e ignorando la cornice.
    internal static List<(string Id, bool Supporto)> ReadMaterialClaims(JsonElement claims)
        => [.. BestStatements(claims, "P186")
            .Select(s => (Id: SnakEntityId(s.GetPropertyOrDefault("mainsnak")), Parts: QualifierEntityIds(s, "P518")))
            .Where(m => m.Id.Length > 0 && !m.Parts.Contains(FrameQid))
            .Select(m => (m.Id, m.Parts.Contains(PaintingSurfaceQid)))
            .Distinct()];

    // «pittura a olio» + «tavola di pioppo» → «olio su tavola di pioppo».
    internal static string FormatTechnique(IEnumerable<string> materials, IEnumerable<string> supports)
    {
        static string Clean(IEnumerable<string> values) => string.Join(", ", values
            .Select(v => Regex.Replace(v.Trim(), @"^(pittura|colore|colori) a(d)?\s+", string.Empty, RegexOptions.IgnoreCase))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase));

        var material = Clean(materials);
        var support = Clean(supports);
        return material.Length > 0 && support.Length > 0 ? $"{material} su {support}" : material.Length > 0 ? material : support;
    }

    // Altezza × larghezza (× profondità) con l'unità; il diametro come «Ø 30 cm».
    internal static string ReadDimensions(JsonElement claims)
    {
        (string Amount, string Unit)? Read(string property)
        {
            var value = BestStatements(claims, property).Select(s => s.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value")).FirstOrDefault();
            var amount = GetString(value, "amount").TrimStart('+');
            if (amount.Length == 0 || !decimal.TryParse(amount, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            var unitId = GetString(value, "unit");
            unitId = unitId[(unitId.LastIndexOf('/') + 1)..];
            return (number.ToString("0.##", CultureInfo.GetCultureInfo("it-IT")), LengthUnits.GetValueOrDefault(unitId, string.Empty));
        }

        var measures = new[] { Read("P2048"), Read("P2049"), Read("P2610") }.OfType<(string Amount, string Unit)>().ToList();
        string text;
        if (measures.Count == 0)
        {
            text = string.Empty;
        }
        else if (measures.All(m => m.Unit == measures[0].Unit))
        {
            text = $"{string.Join(" × ", measures.Select(m => m.Amount))} {measures[0].Unit}".Trim();
        }
        else
        {
            text = string.Join(" × ", measures.Select(m => $"{m.Amount} {m.Unit}".Trim()));
        }

        if (Read("P2386") is { } diameter)
        {
            var round = $"Ø {diameter.Amount} {diameter.Unit}".Trim();
            text = text.Length > 0 ? $"{text}, {round}" : round;
        }

        return text;
    }

    // Tipo proposto dalle etichette italiane dei tipi Wikidata (P31).
    internal static string MapArtworkType(IEnumerable<string> italianLabels)
    {
        var labels = italianLabels.Select(l => l.Trim().Replace('’', '\'')).Where(l => l.Length > 0).ToList();
        foreach (var (parola, tipo) in ArtworkTypeKeywords)
        {
            if (labels.Any(l => Regex.IsMatch(l.ToLowerInvariant(), $@"\b{Regex.Escape(parola)}\b", RegexOptions.CultureInvariant)))
            {
                return tipo;
            }
        }

        var first = labels.FirstOrDefault(l => !string.Equals(l, "opera d'arte", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return first.Length == 0 ? string.Empty : char.ToUpperInvariant(first[0]) + first[1..];
    }

    // Immagine ridotta da Commons (lato massimo 2000 pixel, come la copia salvata nell'archivio).
    internal static string CommonsImageUrl(string fileName)
        => $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(fileName.Trim().Replace(' ', '_'))}?width=2000";

    // Pagina del file su Commons, con autore e licenza.
    internal static string CommonsFilePage(string fileName)
        => $"https://commons.wikimedia.org/wiki/File:{Uri.EscapeDataString(fileName.Trim().Replace(' ', '_'))}";

    // Affermazioni del rango «preferito» se ce ne sono, altrimenti quelle normali.
    private static List<JsonElement> BestStatements(JsonElement claims, string propertyId)
    {
        var statements = Statements(claims, propertyId).ToList();
        var preferred = statements.Where(s => GetString(s, "rank") == "preferred").ToList();
        return preferred.Count > 0 ? preferred : statements;
    }

    private static string GetSnakString(JsonElement snak)
        => snak.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value") is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;
}
