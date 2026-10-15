using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public sealed class WikipediaImportService
{
    private const string WikipediaApi = "https://it.wikipedia.org/w/api.php";
    private const string WikidataApi = "https://www.wikidata.org/w/api.php";
    private const int MaximumPlaceHierarchyDepth = 6;
    private const int MaximumRateLimitRetries = 2;
    private static readonly HttpClient Client = CreateClient();

    // Edizioni linguistiche lette oltre a quella italiana (codice, nome, Q-id del progetto, chiave sitelink).
    private static readonly (string Code, string Name, string Qid, string Sitelink)[] Languages =
    [
        ("it", "italiano", "Q11920", "itwiki"),
        ("en", "inglese", "Q328", "enwiki"),
        ("de", "tedesco", "Q48183", "dewiki"),
        ("fr", "francese", "Q8447", "frwiki"),
        ("es", "spagnolo", "Q8449", "eswiki"),
        ("nl", "olandese", "Q10000", "nlwiki"),
    ];

    private const string LanguagesParam = "it%7Cen%7Cde%7Cfr%7Ces%7Cnl";

    private sealed record LeadDate(int? BirthYear, int? DeathYear, string Title, string Url);

    public async Task<IReadOnlyList<WikipediaSearchResult>> SearchAsync(string searchText, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Interrogazione di Wikipedia in corso…");
        var url = $"{WikipediaApi}?action=query&list=search&srsearch={Uri.EscapeDataString(searchText)}&srlimit=8&format=json&utf8=1";
        using var document = await GetJsonAsync(url, cancellationToken);
        var results = new List<WikipediaSearchResult>();

        foreach (var item in document.RootElement.GetProperty("query").GetProperty("search").EnumerateArray())
        {
            results.Add(new WikipediaSearchResult
            {
                Title = GetString(item, "title"),
                PageId = item.GetProperty("pageid").GetInt32(),
                Snippet = CleanSnippet(GetString(item, "snippet")),
            });
        }

        progress?.Report($"Trovate {results.Count} pagine.");
        return results;
    }

    public async Task<WikipediaImportResult> ImportAsync(WikipediaSearchResult result, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Lettura della pagina Wikipedia…");
        var pageUrl = $"{WikipediaApi}?action=query&prop=pageprops%7Cextracts&exintro=1&explaintext=1&ppprop=wikibase_item&titles={Uri.EscapeDataString(result.Title)}&redirects=1&format=json&utf8=1";
        using var pageDocument = await GetJsonAsync(pageUrl, cancellationToken);
        var page = pageDocument.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().First().Value;
        var title = GetString(page, "title", result.Title);
        var articleUrl = $"https://it.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
        var wikidataId = GetString(page.GetPropertyOrDefault("pageprops"), "wikibase_item");
        var extract = GetString(page, "extract");

        if (string.IsNullOrWhiteSpace(wikidataId))
        {
            progress?.Report("Pagina senza collegamento a Wikidata.");
            var parsedTitle = PersonNameParser.Split(title);
            return new WikipediaImportResult
            {
                Name = parsedTitle.Name,
                Suffix = parsedTitle.Suffix,
                ArticleTitle = title,
                ArticleUrl = articleUrl,
                Extract = extract,
            };
        }

        progress?.Report("Lettura delle affermazioni Wikidata…");
        using var entityDocument = await GetJsonAsync(
            $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(wikidataId)}&props=labels%7Cclaims%7Csitelinks&languages={LanguagesParam}&format=json",
            cancellationToken);
        var entity = entityDocument.RootElement.GetProperty("entities").GetProperty(wikidataId);
        var labels = ReadLabels(entity);
        var claims = entity.GetPropertyOrDefault("claims");

        progress?.Report("Lettura delle altre edizioni linguistiche…");
        var leadDates = await ReadLanguageLeadDatesAsync(entity.GetPropertyOrDefault("sitelinks"), cancellationToken);

        var birthDates = BuildCrossLanguageDates(ReadDateEvidence(claims, "P569"), leadDates, birth: true);
        var deathDates = BuildCrossLanguageDates(ReadDateEvidence(claims, "P570"), leadDates, birth: false);
        var birthPlaceStatements = ReadEntityStatements(claims, "P19");
        var deathPlaceStatements = ReadEntityStatements(claims, "P20");
        var burialPlaceStatements = ReadEntityStatements(claims, "P119");
        var placeIds = birthPlaceStatements.Concat(deathPlaceStatements).Concat(burialPlaceStatements).Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        progress?.Report("Analisi della gerarchia dei luoghi…");
        var placeEntities = await ReadPlaceHierarchyAsync(placeIds, cancellationToken);
        var birthPlaces = BuildPlaces(birthPlaceStatements, placeEntities);
        var deathPlaces = BuildPlaces(deathPlaceStatements, placeEntities);
        var burialPlaces = BuildPlaces(burialPlaceStatements, placeEntities);
        await ReadInfoboxDivisionsAsync(birthPlaces.Concat(deathPlaces).Concat(burialPlaces), placeEntities, cancellationToken);
        var familyIds = ReadEntityStatements(claims, "P53").Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        progress?.Report("Lettura delle famiglie…");
        var familyLabels = await ReadEntityLabelsAsync(familyIds, cancellationToken);
        var familyParents = await ReadFamilyParentsAsync(familyIds, cancellationToken);
        var parentLabels = await ReadEntityLabelsAsync(familyParents.Values.Where(id => !familyLabels.ContainsKey(id)), cancellationToken);
        var dynasties = familyIds.Select(id =>
        {
            var parentId = familyParents.GetValueOrDefault(id, string.Empty);
            return new ImportedDynasty(
                familyLabels.GetValueOrDefault(id, id),
                id,
                parentId.Length == 0 ? string.Empty : familyLabels.GetValueOrDefault(parentId, parentLabels.GetValueOrDefault(parentId, parentId)),
                parentId);
        }).ToList();

        progress?.Report("Lettura dei titoli…");
        var rawTitles = ReadTitleStatements(claims, "P39").Concat(ReadTitleStatements(claims, "P97")).ToList();
        var titleLabelIds = rawTitles.SelectMany(t => new[] { t.EntityId, t.EndCauseId }).Where(IsEntityId).Distinct(StringComparer.Ordinal).ToArray();
        var titleLabels = await ReadEntityLabelsAsync(titleLabelIds, cancellationToken);
        var titleSuperclasses = await ReadSuperclassesAsync(rawTitles.Select(t => t.EntityId), cancellationToken);
        var titles = TitleMerger.Merge(rawTitles.Select(t => new PersonTitle
        {
            Titolo = titleLabels.GetValueOrDefault(t.EntityId, t.EntityId),
            Numerazione = t.Ordinal,
            Inizio = t.Start,
            Fine = t.End,
            CausaFine = t.EndCauseId.Length > 0 ? titleLabels.GetValueOrDefault(t.EndCauseId, t.EndCauseId) : string.Empty,
            WikidataId = t.EntityId,
        }), titleSuperclasses);
        var mainTitle = rawTitles.FirstOrDefault(t => t.Preferred) ?? rawTitles.FirstOrDefault();
        var nicknames = ReadMonolingualTexts(claims, "P1449");
        var parsedName = PersonNameParser.Split(labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", title)), nicknames);
        var nickname = parsedName.Nickname.Length > 0 ? parsedName.Nickname : nicknames.FirstOrDefault() ?? string.Empty;
        var mainTitleLabels = mainTitle is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await ReadAllLabelsAsync(mainTitle.EntityId, cancellationToken);
        var localizedNames = LocalizedNameBuilder.Build(
            Languages.Select(l => (l.Code, l.Name)),
            labels,
            ReadMonolingualTextsWithLanguage(claims, "P1449"),
            mainTitleLabels);

        progress?.Report("Composizione della scheda…");
        var sexId = ReadEntityIds(claims, "P21").FirstOrDefault();
        return new WikipediaImportResult
        {
            FatherIds = ReadEntityIds(claims, "P22").ToList(),
            MotherIds = ReadEntityIds(claims, "P25").ToList(),
            SpouseIds = ReadEntityIds(claims, "P26").ToList(),
            ChildIds = ReadEntityIds(claims, "P40").ToList(),
            Sesso = sexId switch
            {
                "Q6581097" => Sessi.Maschio,
                "Q6581072" => Sessi.Femmina,
                _ => string.Empty,
            },
            Name = parsedName.Name,
            Suffix = parsedName.Suffix,
            Nickname = nickname,
            MainTitle = mainTitle is null ? string.Empty : titleLabels.GetValueOrDefault(mainTitle.EntityId, mainTitle.EntityId),
            Titles = titles,
            LocalizedNames = localizedNames,
            Family = string.Join("; ", familyIds.Select(id => familyLabels.GetValueOrDefault(id, id))),
            Dynasties = dynasties,
            ArticleTitle = title,
            ArticleUrl = articleUrl,
            WikidataId = wikidataId,
            Extract = extract,
            BirthDates = birthDates,
            DeathDates = deathDates,
            BirthPlaces = birthPlaces,
            DeathPlaces = deathPlaces,
            BurialPlaces = burialPlaces,
        };
    }

    private static async Task<Dictionary<string, JsonElement>> ReadPlaceHierarchyAsync(IEnumerable<string> rootIds, CancellationToken cancellationToken)
    {
        var entities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var pending = rootIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).ToList();

        for (var depth = 0; depth < MaximumPlaceHierarchyDepth && pending.Count > 0; depth++)
        {
            var remaining = pending.Where(id => !entities.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
            var batch = remaining.Take(40).ToArray();
            pending = remaining.Skip(40).ToList();
            if (batch.Length == 0)
            {
                break;
            }

            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Cclaims%7Csitelinks&sitefilter=itwiki&languages=it%7Cen&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            var responseEntities = document.RootElement.GetProperty("entities");

            foreach (var id in batch)
            {
                if (!responseEntities.TryGetProperty(id, out var entity) || entity.TryGetProperty("missing", out _))
                {
                    continue;
                }

                entities[id] = entity.Clone();
                var claims = entity.GetPropertyOrDefault("claims");
                pending.AddRange(ReadEntityIds(claims, "P131"));
                pending.AddRange(ReadEntityIds(claims, "P17"));
            }
        }

        return entities;
    }

    internal static List<HistoricalPlaceRecord> BuildPlaces(
        IEnumerable<EntityStatement> statements,
        IReadOnlyDictionary<string, JsonElement> entities)
    {
        var places = new List<HistoricalPlaceRecord>();
        var labels = entities.ToDictionary(pair => pair.Key, pair => ReadLabels(pair.Value), StringComparer.Ordinal);
        var coordinates = entities.ToDictionary(pair => pair.Key, pair => ReadCoordinate(pair.Value.GetPropertyOrDefault("claims")), StringComparer.Ordinal);

        foreach (var statement in statements)
        {
            if (!entities.TryGetValue(statement.EntityId, out var entity))
            {
                continue;
            }

            var entityLabels = labels[statement.EntityId];
            var (placeLat, placeLon) = coordinates[statement.EntityId];
            var place = new HistoricalPlaceRecord
            {
                WikidataId = statement.EntityId,
                CurrentName = entityLabels.GetValueOrDefault("it", entityLabels.GetValueOrDefault("en", statement.EntityId)),
                Names = ToPlaceNames(entityLabels),
                Latitudine = placeLat,
                Longitudine = placeLon,
                Sources = statement.Sources,
            };
            var pending = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var countryIsHistoric = false;
            pending.Enqueue(statement.EntityId);

            while (pending.Count > 0)
            {
                var currentId = pending.Dequeue();
                if (!visited.Add(currentId) || !entities.TryGetValue(currentId, out var currentEntity))
                {
                    continue;
                }

                var currentClaims = currentEntity.GetPropertyOrDefault("claims");
                foreach (var parentId in ReadEntityIds(currentClaims, "P131"))
                {
                    AddDivision(place, parentId, currentId, "Unità amministrativa (P131)", labels, coordinates);
                    pending.Enqueue(parentId);
                }

                foreach (var countryId in ReadEntityIds(currentClaims, "P17"))
                {
                    AddDivision(place, countryId, currentId, "Paese (P17)", labels, coordinates);
                    // Un Paese storico (con data di dissoluzione P576) non sostituisce quello attuale.
                    var dissolved = IsDissolved(countryId, entities);
                    if (place.CurrentCountry.Length == 0 || (countryIsHistoric && !dissolved))
                    {
                        place.CurrentCountry = GetEntityLabel(countryId, labels);
                        countryIsHistoric = dissolved;
                    }
                    else if (currentId == statement.EntityId && !dissolved)
                    {
                        place.CurrentCountry = GetEntityLabel(countryId, labels);
                        countryIsHistoric = false;
                    }

                    pending.Enqueue(countryId);
                }
            }

            places.Add(place);
        }

        return places;
    }

    // Per i Paesi che seguono l'infobox di it.wikipedia legge le divisioni come appaiono sulla pagina del comune.
    private static async Task ReadInfoboxDivisionsAsync(IEnumerable<HistoricalPlaceRecord> places, IReadOnlyDictionary<string, JsonElement> entities, CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var place in places)
        {
            if (!GeoSchemas.UsesInfoboxDivisions(place.CurrentCountry) || !entities.TryGetValue(place.WikidataId, out var entity))
            {
                continue;
            }

            var title = GetString(entity.GetPropertyOrDefault("sitelinks").GetPropertyOrDefault("itwiki"), "title");
            if (title.Length == 0)
            {
                continue;
            }

            if (!cache.TryGetValue(title, out var grades))
            {
                var url = $"{WikipediaApi}?action=parse&page={Uri.EscapeDataString(title)}&prop=wikitext&section=0&redirects=1&format=json&formatversion=2";
                try
                {
                    using var document = await GetJsonAsync(url, cancellationToken);
                    var wikitext = GetString(document.RootElement.GetPropertyOrDefault("parse"), "wikitext");
                    grades = InfoboxDivisionParser.Parse(wikitext)?.Grades ?? [];
                }
                catch (HttpRequestException)
                {
                    grades = [];
                }

                cache[title] = grades;
            }

            place.InfoboxDivisions = [.. grades];
        }
    }

    internal static bool IsDissolved(string entityId, IReadOnlyDictionary<string, JsonElement> entities)
        => entities.TryGetValue(entityId, out var entity)
            && entity.GetPropertyOrDefault("claims").ValueKind == JsonValueKind.Object
            && entity.GetPropertyOrDefault("claims").TryGetProperty("P576", out var dissolution)
            && dissolution.GetArrayLength() > 0;

    private static void AddDivision(
        HistoricalPlaceRecord place,
        string entityId,
        string parentId,
        string relation,
        IReadOnlyDictionary<string, Dictionary<string, string>> labels,
        IReadOnlyDictionary<string, (double? Latitude, double? Longitude)> coordinates)
    {
        if (place.AdministrativeDivisions.Any(division => division.WikidataId == entityId && division.SubjectWikidataId == parentId && division.Relation == relation))
        {
            return;
        }

        var (latitude, longitude) = coordinates.GetValueOrDefault(entityId);
        place.AdministrativeDivisions.Add(new PlaceDivision
        {
            Relation = relation,
            Name = GetEntityLabel(entityId, labels),
            Names = labels.TryGetValue(entityId, out var divisionLabels) ? ToPlaceNames(divisionLabels) : [],
            Latitudine = latitude,
            Longitudine = longitude,
            WikidataId = entityId,
            SubjectWikidataId = parentId,
            SourceUrl = $"https://www.wikidata.org/wiki/{entityId}",
        });
    }

    private static List<PlaceName> ToPlaceNames(IReadOnlyDictionary<string, string> labels)
        => labels.Select(pair => new PlaceName { Language = pair.Key, Name = pair.Value }).ToList();

    // Legge le coordinate geografiche (P625) dalla prima affermazione non deprecata.
    private static (double? Latitude, double? Longitude) ReadCoordinate(JsonElement claims)
    {
        if (claims.ValueKind != JsonValueKind.Object || !claims.TryGetProperty("P625", out var statements) || statements.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var statement in statements.EnumerateArray())
        {
            if (GetString(statement, "rank") == "deprecated")
            {
                continue;
            }

            var value = statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latitude)
                && value.TryGetProperty("longitude", out var lon) && lon.TryGetDouble(out var longitude))
            {
                return (latitude, longitude);
            }
        }

        return (null, null);
    }

    private static List<HistoricalDateEvidence> ReadDateEvidence(JsonElement claims, string propertyId)
    {
        var evidence = new List<HistoricalDateEvidence>();
        if (!claims.TryGetProperty(propertyId, out var statements) || statements.ValueKind != JsonValueKind.Array)
        {
            return evidence;
        }

        foreach (var statement in statements.EnumerateArray())
        {
            var value = statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
            var rawValue = GetString(value, "time");
            if (rawValue.Length == 0 || !value.TryGetProperty("precision", out var precisionElement) || !precisionElement.TryGetInt32(out var precision))
            {
                continue;
            }

            var qualifiers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var qualifierObject = statement.GetPropertyOrDefault("qualifiers");
            if (qualifierObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var qualifier in qualifierObject.EnumerateObject())
                {
                    var values = qualifier.Value.EnumerateArray()
                        .Select(item => GetString(item.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "time"))
                        .Where(item => item.Length > 0)
                        .ToList();
                    if (values.Count > 0)
                    {
                        qualifiers[qualifier.Name] = values;
                    }
                }
            }

            evidence.Add(new HistoricalDateEvidence
            {
                Value = FormatDate(rawValue, precision),
                RawValue = rawValue,
                Precision = PrecisionName(precision),
                Rank = GetString(statement, "rank"),
                Qualifiers = qualifiers,
                Sources = ReadSources(statement),
            });
        }

        return evidence
            .OrderByDescending(item => item.Rank == "preferred")
            .ThenByDescending(item => item.Sources.Count)
            .ToList();
    }

    private static List<EntityStatement> ReadEntityStatements(JsonElement claims, string propertyId)
    {
        var statements = new List<EntityStatement>();
        if (!claims.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return statements;
        }

        var collected = new List<(string Id, List<SourceCitation> Sources, bool Preferred)>();
        foreach (var statement in values.EnumerateArray())
        {
            var rank = GetString(statement, "rank");
            if (rank == "deprecated" || HasEndTime(statement))
            {
                continue;
            }

            var value = statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
            var id = GetString(value, "id");
            if (IsEntityId(id))
            {
                collected.Add((id, ReadSources(statement), rank == "preferred"));
            }
        }

        // Se esistono valori con rank "preferred" si usano solo quelli (valore attuale/autorevole).
        var chosen = collected.Any(item => item.Preferred)
            ? collected.Where(item => item.Preferred)
            : collected;

        foreach (var item in chosen)
        {
            statements.Add(new EntityStatement(item.Id, item.Sources));
        }

        return statements;
    }

    // Un valore con qualificatore "fine" (P582) è storico: non rappresenta la gerarchia attuale.
    private static bool HasEndTime(JsonElement statement)
    {
        var qualifiers = statement.GetPropertyOrDefault("qualifiers");
        return qualifiers.ValueKind == JsonValueKind.Object && qualifiers.TryGetProperty("P582", out _);
    }

    private static IEnumerable<string> ReadEntityIds(JsonElement claims, string propertyId) =>
        ReadEntityStatements(claims, propertyId).Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal);

    private static List<SourceCitation> ReadSources(JsonElement statement)
    {
        var sources = new List<SourceCitation>();
        var references = statement.GetPropertyOrDefault("references");
        if (references.ValueKind != JsonValueKind.Array)
        {
            return sources;
        }

        foreach (var reference in references.EnumerateArray())
        {
            var snaks = reference.GetPropertyOrDefault("snaks");
            var url = ReadSnakValue(snaks, "P854");
            var statedIn = ReadSnakValue(snaks, "P248");
            var retrievedAt = ReadSnakValue(snaks, "P813");
            var importedFromLanguage = QidToLanguageName(ReadSnakValue(snaks, "P143"));
            var title = importedFromLanguage.Length > 0
                ? $"Importato da Wikipedia in {importedFromLanguage}"
                : statedIn.Length > 0 ? $"Fonte Wikidata {statedIn}" : "Riferimento Wikidata";
            sources.Add(new SourceCitation
            {
                Title = title,
                Url = url.Length > 0 ? url : statedIn.Length > 0 ? $"https://www.wikidata.org/wiki/{statedIn}" : string.Empty,
                RetrievedAt = retrievedAt,
                WikidataId = statedIn,
            });
        }

        return sources;
    }

    private sealed record TitleStatement(string EntityId, string Ordinal, string Start, string End, string EndCauseId, bool Preferred);

    // A differenza di ReadEntityStatements include anche i titoli conclusi (P582): servono per lo storico.
    private static IEnumerable<TitleStatement> ReadTitleStatements(JsonElement claims, string propertyId)
    {
        if (!claims.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var statement in values.EnumerateArray())
        {
            var rank = GetString(statement, "rank");
            var id = GetString(statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id");
            if (rank == "deprecated" || !IsEntityId(id))
            {
                continue;
            }

            var qualifiers = statement.GetPropertyOrDefault("qualifiers");
            if (qualifiers.ValueKind != JsonValueKind.Object)
            {
                yield return new TitleStatement(id, string.Empty, string.Empty, string.Empty, string.Empty, rank == "preferred");
                continue;
            }

            yield return new TitleStatement(
                id,
                ReadSnakValue(qualifiers, "P1545"),
                ReadQualifierDate(qualifiers, "P580"),
                ReadQualifierDate(qualifiers, "P582"),
                ReadSnakValue(qualifiers, "P1534"),
                rank == "preferred");
        }
    }

    private static string ReadQualifierDate(JsonElement qualifiers, string propertyId)
    {
        if (!qualifiers.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var value = values.EnumerateArray().FirstOrDefault().GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
        var raw = GetString(value, "time");
        return raw.Length > 0 && value.TryGetProperty("precision", out var p) && p.TryGetInt32(out var precision)
            ? FormatDate(raw, precision)
            : string.Empty;
    }

    private static List<(string Text, string Language)> ReadMonolingualTextsWithLanguage(JsonElement claims, string propertyId)
    {
        if (!claims.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values.EnumerateArray()
            .Where(statement => GetString(statement, "rank") != "deprecated")
            .Select(statement => statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"))
            .Select(value => (Text: GetString(value, "text"), Language: GetString(value, "language")))
            .Where(item => item.Text.Length > 0)
            .ToList();
    }

    private static async Task<Dictionary<string, string>> ReadAllLabelsAsync(string entityId, CancellationToken cancellationToken)
    {
        if (!IsEntityId(entityId))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(entityId)}&props=labels&languages={LanguagesParam}&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        return ReadLabels(document.RootElement.GetProperty("entities").GetProperty(entityId));
    }

    private static List<string> ReadMonolingualTexts(JsonElement claims, string propertyId)
    {
        if (!claims.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values.EnumerateArray()
            .Where(statement => GetString(statement, "rank") != "deprecated")
            .Select(statement => statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"))
            .Select(value => (Text: GetString(value, "text"), Language: GetString(value, "language")))
            .Where(item => item.Text.Length > 0)
            .OrderByDescending(item => item.Language == "it")
            .Select(item => item.Text)
            .ToList();
    }

    private static string ReadSnakValue(JsonElement snaks, string propertyId)
    {
        if (!snaks.TryGetProperty(propertyId, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var value = values.EnumerateArray().FirstOrDefault().GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Object => GetString(value, "id", GetString(value, "time")),
            _ => string.Empty,
        };
    }

    // Per ogni famiglia legge la dinastia di cui è ramo: "parte di" (P361), in mancanza "sottoclasse di" (P279).
    private static async Task<Dictionary<string, string>> ReadFamilyParentsAsync(IReadOnlyCollection<string> familyIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ids = familyIds.Where(IsEntityId).Take(50).ToArray();
        if (ids.Length == 0)
        {
            return result;
        }

        var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', ids))}&props=claims&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        foreach (var item in document.RootElement.GetProperty("entities").EnumerateObject())
        {
            var claims = item.Value.GetPropertyOrDefault("claims");
            var parent = ReadEntityIds(claims, "P361").Concat(ReadEntityIds(claims, "P279"))
                .FirstOrDefault(id => id != item.Name);
            if (parent is not null)
            {
                result[item.Name] = parent;
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> ReadEntityLabelsAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken)
    {
        var ids = entityIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', ids.Take(50)))}&props=labels&languages=it%7Cen&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        return document.RootElement.GetProperty("entities").EnumerateObject()
            .ToDictionary(item => item.Name, item => ReadLabels(item.Value).GetValueOrDefault("it", ReadLabels(item.Value).GetValueOrDefault("en", item.Name)), StringComparer.Ordinal);
    }

    // Superclassi dirette (P279) di ogni titolo: servono a scartare i titoli generici (es. «monarca»).
    private static async Task<Dictionary<string, HashSet<string>>> ReadSuperclassesAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var batch in entityIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=claims&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            foreach (var item in document.RootElement.GetProperty("entities").EnumerateObject())
            {
                result[item.Name] = ReadEntityIds(item.Value.GetPropertyOrDefault("claims"), "P279").ToHashSet(StringComparer.Ordinal);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadLabels(JsonElement entity)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = entity.GetPropertyOrDefault("labels");
        foreach (var language in new[] { "it", "en", "de", "fr", "es", "nl" })
        {
            var label = GetString(values.GetPropertyOrDefault(language), "value");
            if (label.Length > 0)
            {
                labels[language] = label;
            }
        }

        return labels;
    }

    // Scarica l'introduzione di ogni edizione linguistica e ne estrae gli anni di nascita/morte.
    private static async Task<Dictionary<string, LeadDate>> ReadLanguageLeadDatesAsync(JsonElement sitelinks, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LeadDate>(StringComparer.Ordinal);
        if (sitelinks.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        // Le edizioni sono indipendenti: si scaricano in parallelo.
        var tasks = new List<Task<(string Code, LeadDate? Lead)>>();
        foreach (var (code, _, _, sitelink) in Languages)
        {
            if (sitelinks.TryGetProperty(sitelink, out var link) && GetString(link, "title") is { Length: > 0 } title)
            {
                tasks.Add(ReadLeadDateAsync(code, title, cancellationToken));
            }
        }

        foreach (var (code, lead) in await Task.WhenAll(tasks))
        {
            if (lead is not null)
            {
                result[code] = lead;
            }
        }

        return result;
    }

    private static async Task<(string Code, LeadDate? Lead)> ReadLeadDateAsync(string code, string title, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://{code}.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&redirects=1&titles={Uri.EscapeDataString(title)}&format=json&utf8=1";
            using var document = await GetJsonAsync(url, cancellationToken);
            var page = document.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().FirstOrDefault().Value;
            var (birth, death) = ExtractLeadYears(GetString(page, "extract"));
            var articleUrl = $"https://{code}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
            return (code, new LeadDate(birth, death, title, articleUrl));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // Edizione non leggibile: si prosegue con le altre.
            return (code, null);
        }
    }

    // Prende gli anni dalla prima coppia di parentesi dell'introduzione (es. «(1769–1821)»).
    private static (int? Birth, int? Death) ExtractLeadYears(string extract)
    {
        if (string.IsNullOrWhiteSpace(extract))
        {
            return (null, null);
        }

        var match = Regex.Match(extract, @"\(([^()]{0,240})\)");
        if (!match.Success)
        {
            return (null, null);
        }

        var years = Regex.Matches(match.Groups[1].Value, @"\b(\d{3,4})\b")
            .Select(m => int.TryParse(m.Groups[1].Value, out var y) ? y : 0)
            .Where(y => y is > 0 and <= 2100)
            .ToList();
        if (years.Count == 0)
        {
            return (null, null);
        }

        int? birth = years[0];
        var second = years.FirstOrDefault(y => y != years[0]);
        return (birth, second > 0 ? second : null);
    }

    // Combina i valori di data di Wikidata con gli anni letti dalle varie Wikipedia, raggruppando per anno.
    private static List<HistoricalDateEvidence> BuildCrossLanguageDates(
        List<HistoricalDateEvidence> wikidataEvidence,
        Dictionary<string, LeadDate> leadDates,
        bool birth)
    {
        var groups = new Dictionary<int, HistoricalDateEvidence>();
        var order = new List<int>();

        void EnsureGroup(int year, string value, string precision, string rawValue, Dictionary<string, List<string>>? qualifiers, string rank, IEnumerable<SourceCitation> sources)
        {
            if (!groups.TryGetValue(year, out var evidence))
            {
                evidence = new HistoricalDateEvidence { Value = value, RawValue = rawValue, Precision = precision, Rank = rank };
                if (qualifiers is not null)
                {
                    evidence.Qualifiers = qualifiers;
                }

                groups[year] = evidence;
                order.Add(year);
            }
            else if (PrecisionRank(precision) > PrecisionRank(evidence.Precision))
            {
                // Preferisce il valore più preciso (es. la data completa di Wikidata) come rappresentante del gruppo.
                evidence.Value = value;
                evidence.RawValue = rawValue;
                evidence.Precision = precision;
            }

            foreach (var source in sources)
            {
                if (!evidence.Sources.Any(s => s.Title == source.Title && s.Url == source.Url))
                {
                    evidence.Sources.Add(source);
                }
            }
        }

        foreach (var e in wikidataEvidence)
        {
            if (ParseWikidataYear(e.RawValue) is not int year)
            {
                continue;
            }

            EnsureGroup(year, e.Value, e.Precision.Length > 0 ? e.Precision : "anno", e.RawValue, e.Qualifiers, e.Rank, e.Sources);
        }

        foreach (var (code, name, _, _) in Languages)
        {
            if (!leadDates.TryGetValue(code, out var lead))
            {
                continue;
            }

            var year = birth ? lead.BirthYear : lead.DeathYear;
            if (year is not int y)
            {
                continue;
            }

            var citation = new SourceCitation { Title = $"Wikipedia in {name}", Url = lead.Url };
            EnsureGroup(y, y.ToString(), "anno", $"+{y:0000}-00-00T00:00:00Z", null, string.Empty, [citation]);
        }

        return order
            .Select(year => groups[year])
            .OrderByDescending(ev => ev.Rank == "preferred")
            .ThenByDescending(ev => ev.Sources.Count)
            .ToList();
    }

    private static int PrecisionRank(string precision) => precision switch
    {
        "giorno" => 5,
        "mese" => 4,
        "anno" => 3,
        "decennio" => 2,
        "secolo" => 1,
        _ => 3,
    };

    private static int? ParseWikidataYear(string rawValue)
    {
        var match = Regex.Match(rawValue, @"^(?<sign>[+-])(?<year>\d+)-");
        if (!match.Success || !int.TryParse(match.Groups["year"].Value, out var year))
        {
            return null;
        }

        return match.Groups["sign"].Value == "-" ? -year : year;
    }

    private static string QidToLanguageName(string qid)
    {
        if (qid.Length == 0)
        {
            return string.Empty;
        }

        foreach (var (_, name, entityId, _) in Languages)
        {
            if (entityId == qid)
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static string GetEntityLabel(string id, IReadOnlyDictionary<string, Dictionary<string, string>> labels) =>
        labels.TryGetValue(id, out var languageLabels)
            ? languageLabels.GetValueOrDefault("it", languageLabels.GetValueOrDefault("en", id))
            : id;

    private static string FormatDate(string rawValue, int precision)
    {
        var match = Regex.Match(rawValue, @"^(?<sign>[+-])(?<year>\d+)-(?<month>\d{2})-(?<day>\d{2})");
        if (!match.Success)
        {
            return rawValue;
        }

        var year = match.Groups["year"].Value;
        if (match.Groups["sign"].Value == "-")
        {
            year = $"-{year}";
        }

        return precision switch
        {
            11 => $"{match.Groups["day"].Value}/{match.Groups["month"].Value}/{year}",
            10 => $"{match.Groups["month"].Value}/{year}",
            9 => year,
            _ => year,
        };
    }

    private static string PrecisionName(int precision) => precision switch
    {
        7 => "secolo",
        8 => "decennio",
        9 => "anno",
        10 => "mese",
        11 => "giorno",
        _ => $"precisione Wikidata {precision}",
    };

    private static bool IsEntityId(string id) => Regex.IsMatch(id, @"^Q\d+$", RegexOptions.CultureInvariant);

    private static string CleanSnippet(string snippet) => WebUtility.HtmlDecode(Regex.Replace(snippet, "<.*?>", " ")).Trim();

    private static string GetString(JsonElement element, string propertyName, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaximumRateLimitRetries)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(wait > TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : wait, cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // Wikimedia strozza (429) gli User-Agent senza contatto.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AlberiStorici/1.0 (maurosonzogni@hotmail.it)");
        return client;
    }

    internal sealed record EntityStatement(string EntityId, List<SourceCitation> Sources);
}

internal static class JsonElementExtensions
{
    public static JsonElement GetPropertyOrDefault(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) ? value : default;
}
