using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public sealed partial class WikipediaImportService
{
    private const string WikipediaApi = "https://it.wikipedia.org/w/api.php";
    private const string WikidataApi = "https://www.wikidata.org/w/api.php";
    private const int MaximumPlaceHierarchyDepth = 6;
    private const int MaximumRateLimitRetries = 3;
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

    internal sealed record LeadDate(int? BirthYear, int? DeathYear, string BirthPlace, string DeathPlace, string Title, string Url)
    {
        public int? BirthMonth { get; init; }
        public int? BirthDay { get; init; }
        public int? DeathMonth { get; init; }
        public int? DeathDay { get; init; }
    }

    public async Task<IReadOnlyList<WikipediaSearchResult>> SearchAsync(string searchText, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Interrogazione di Wikipedia in corso…");
        var url = $"{WikipediaApi}?action=query&list=search&srsearch={Uri.EscapeDataString(searchText)}&srlimit=8&format=json&utf8=1";
        using var document = await GetJsonAsync(url, cancellationToken);
        var results = new List<WikipediaSearchResult>();

        foreach (var item in ApiProperty(ApiProperty(document.RootElement, "query"), "search").EnumerateArray())
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

    // Reimporta una scheda già salvata: la pagina italiana si ricava dal sitelink Wikidata, in mancanza dal titolo salvato.
    public async Task<WikipediaImportResult?> ReimportAsync(string wikidataId, string fallbackTitle, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var reporter = new ImportProgressReporter(progress, offset: 1);
        var title = string.Empty;
        if (IsEntityId(wikidataId))
        {
            progress?.Report(new ImportProgress(1, reporter.TotalSteps, "Ricerca della pagina tramite Wikidata…"));
            using var document = await GetJsonAsync(
                $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(wikidataId)}&props=sitelinks&sitefilter=itwiki&format=json",
                cancellationToken);
            title = ReadItalianSitelinkTitle(document.RootElement, wikidataId);
        }

        if (title.Length == 0)
        {
            title = fallbackTitle.Trim();
        }

        if (title.Length == 0)
        {
            return null;
        }

        return await ImportCoreAsync(new WikipediaSearchResult { Title = title }, reporter, cancellationToken);
    }

    internal static string ReadItalianSitelinkTitle(JsonElement root, string wikidataId)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("entities", out var entities)
            || entities.ValueKind != JsonValueKind.Object
            || !entities.TryGetProperty(wikidataId, out var entity))
        {
            return string.Empty;
        }

        var sitelinks = entity.GetPropertyOrDefault("sitelinks");
        return sitelinks.ValueKind == JsonValueKind.Object && sitelinks.TryGetProperty("itwiki", out var itwiki)
            ? GetString(itwiki, "title").Trim()
            : string.Empty;
    }

    public Task<WikipediaImportResult> ImportAsync(WikipediaSearchResult result, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
        => ImportCoreAsync(result, new ImportProgressReporter(progress), cancellationToken);

    private async Task<WikipediaImportResult> ImportCoreAsync(WikipediaSearchResult result, ImportProgressReporter progress, CancellationToken cancellationToken)
    {
        progress.Step(1, "Lettura della pagina Wikipedia…");
        var pageUrl = $"{WikipediaApi}?action=query&prop=pageprops%7Cextracts&exintro=1&explaintext=1&ppprop=wikibase_item&titles={Uri.EscapeDataString(result.Title)}&redirects=1&format=json&utf8=1";
        using var pageDocument = await GetJsonAsync(pageUrl, cancellationToken);
        var page = ApiProperty(ApiProperty(pageDocument.RootElement, "query"), "pages").EnumerateObject().First().Value;
        var title = GetString(page, "title", result.Title);
        var articleUrl = $"https://it.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
        var wikidataId = GetString(page.GetPropertyOrDefault("pageprops"), "wikibase_item");
        var extract = GetString(page, "extract");

        if (string.IsNullOrWhiteSpace(wikidataId))
        {
            progress.Step(ImportProgressReporter.ImportSteps, "Pagina senza collegamento a Wikidata.");
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

        progress.Step(2, "Lettura delle affermazioni Wikidata…");
        using var entityDocument = await GetJsonAsync(
            $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(wikidataId)}&props=labels%7Cclaims%7Csitelinks&languages={LanguagesParam}&format=json",
            cancellationToken);
        // Un elemento unito a un altro viene restituito con il nuovo Q-id: si usa l'unica entità ricevuta.
        var entities = ApiProperty(entityDocument.RootElement, "entities");
        var entity = entities.TryGetProperty(wikidataId, out var direct)
            ? direct
            : entities.EnumerateObject().Select(e => e.Value).FirstOrDefault(e => !e.TryGetProperty("missing", out _));
        if (entity.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Elemento Wikidata {wikidataId} non trovato.");
        }

        if (GetString(entity, "id") is { Length: > 0 } resolvedId)
        {
            wikidataId = resolvedId;
        }
        var labels = ReadLabels(entity);
        var claims = entity.GetPropertyOrDefault("claims");

        progress.Step(3, "Lettura delle altre edizioni linguistiche…");
        var leadDates = await ReadLanguageLeadDatesAsync(entity.GetPropertyOrDefault("sitelinks"), progress, cancellationToken);

        var birthDates = BuildCrossLanguageDates(ReadDateEvidence(claims, "P569"), leadDates, birth: true);
        var deathDates = BuildCrossLanguageDates(ReadDateEvidence(claims, "P570"), leadDates, birth: false);
        var birthPlaceStatements = ReadEntityStatements(claims, "P19");
        var deathPlaceStatements = ReadEntityStatements(claims, "P20");
        var burialPlaceStatements = ReadEntityStatements(claims, "P119");
        var placeIds = birthPlaceStatements.Concat(deathPlaceStatements).Concat(burialPlaceStatements).Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        progress.Step(4, "Analisi della gerarchia dei luoghi…");
        var placeEntities = await ReadPlaceHierarchyAsync(placeIds, progress, cancellationToken);
        var birthPlaces = BuildPlaces(birthPlaceStatements, placeEntities);
        var deathPlaces = BuildPlaces(deathPlaceStatements, placeEntities);
        var burialPlaces = BuildPlaces(burialPlaceStatements, placeEntities);
        progress.Step(5, "Lettura delle suddivisioni e dei tipi di luogo…");
        await ReadInfoboxDivisionsAsync(birthPlaces.Concat(deathPlaces).Concat(burialPlaces), placeEntities, cancellationToken);
        await ReadHistoricalNamesAsync(birthPlaces.Concat(deathPlaces).Concat(burialPlaces), placeEntities, cancellationToken);
        progress.Sub(0.5, "tipi di luogo");
        await AssignPlaceTypesAsync([.. birthPlaces, .. deathPlaces, .. burialPlaces], cancellationToken);
        progress.Step(6, "Lettura dei nomi dei luoghi nelle altre lingue…");
        var leadPlaceNames = leadDates.Values.Any(l => l.BirthPlace.Length > 0 || l.DeathPlace.Length > 0)
            ? await ReadPlaceNamesAsync(
                birthPlaces.Concat(deathPlaces).SelectMany(p => ContainingDivisions(p).Select(d => d.WikidataId).Prepend(p.WikidataId)),
                cancellationToken)
            : [];
        var wikipediaLeads = BuildWikipediaLeads(
            leadDates,
            PlaceCandidates(birthPlaces, leadPlaceNames),
            PlaceCandidates(deathPlaces, leadPlaceNames));
        var familyIds = ReadEntityStatements(claims, "P53").Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        var surnameIds = ReadEntityIds(claims, "P734").Distinct(StringComparer.Ordinal).ToArray();
        progress.Step(7, "Lettura di famiglie e titoli…");
        var rawTitles = ReadTitleStatements(claims, "P39").Concat(ReadTitleStatements(claims, "P97")).ToList();
        var sexId = ReadEntityIds(claims, "P21").FirstOrDefault();

        // Prima richiesta: famiglie, titoli e cause di fine con etichette e affermazioni insieme.
        var related = await ReadEntitiesAsync(
            familyIds.Concat(surnameIds).Concat(rawTitles.SelectMany(t => new[] { t.EntityId, t.EndCauseId })),
            cancellationToken);
        familyIds = [.. familyIds.Where(id => !related.TryGetValue(id, out var family)
            || family.GetPropertyOrDefault("claims") is var familyClaims
            && !IsNotAFamily(ReadEntityIds(familyClaims, "P31"), ReadEntityIds(familyClaims, "P36").Any()))];
        var familyParents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in familyIds)
        {
            if (related.TryGetValue(id, out var family))
            {
                var familyClaims = family.GetPropertyOrDefault("claims");
                var parent = ReadEntityIds(familyClaims, "P361").Concat(ReadEntityIds(familyClaims, "P279")).FirstOrDefault(p => p != id);
                if (parent is not null)
                {
                    familyParents[id] = parent;
                }
            }
        }

        var territoryOf = new Dictionary<string, (string English, string TerritoryId)>(StringComparer.Ordinal);
        foreach (var t in rawTitles)
        {
            if (related.TryGetValue(t.EntityId, out var titleEntity)
                && ReadLabels(titleEntity) is var titleEntityLabels
                && !titleEntityLabels.ContainsKey("it")
                && titleEntityLabels.TryGetValue("en", out var english))
            {
                var titleClaims = titleEntity.GetPropertyOrDefault("claims");
                var territoryId = ReadEntityIds(titleClaims, "P1001").Concat(ReadEntityIds(titleClaims, "P17")).FirstOrDefault();
                if (territoryId is not null)
                {
                    territoryOf[t.EntityId] = (english, territoryId);
                }
            }
        }

        // Seconda richiesta: solo le etichette mancanti (dinastie madri, territori dei titoli, predecessori e successori).
        var extraLabels = await ReadEntityLabelsAsync(
            familyParents.Values.Concat(territoryOf.Values.Select(v => v.TerritoryId))
                .Concat(rawTitles.SelectMany(t => new[] { t.ReplacesId, t.ReplacedById }).Where(IsEntityId))
                .Where(id => !related.ContainsKey(id)),
            cancellationToken);
        string LabelOf(string id)
            => related.TryGetValue(id, out var e) && ReadLabels(e) is var l && (l.TryGetValue("it", out var v) || l.TryGetValue("en", out v))
                ? v
                : extraLabels.GetValueOrDefault(id, id);

        var familyLabels = familyIds.ToDictionary(id => id, LabelOf, StringComparer.Ordinal);
        var parentLabels = familyParents.Values.Distinct(StringComparer.Ordinal).ToDictionary(id => id, LabelOf, StringComparer.Ordinal);
        var dynasties = familyIds.Select(id =>
        {
            var parentId = familyParents.GetValueOrDefault(id, string.Empty);
            return new ImportedDynasty(
                familyLabels.GetValueOrDefault(id, id),
                id,
                parentId.Length == 0 ? string.Empty : familyLabels.GetValueOrDefault(parentId, parentLabels.GetValueOrDefault(parentId, parentId)),
                parentId);
        }).ToList();

        var titleLabels = rawTitles.SelectMany(t => new[] { t.EntityId, t.EndCauseId }).Where(IsEntityId).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, LabelOf, StringComparer.Ordinal);
        var sesso = sexId == "Q6581072" ? Sessi.Femmina : Sessi.Maschio;
        foreach (var (id, (english, territoryId)) in territoryOf)
        {
            if (TitleTranslator.Translate(english, LabelOf(territoryId), sesso) is { } translated)
            {
                titleLabels[id] = translated;
            }
        }

        var titleSuperclasses = rawTitles.Select(t => t.EntityId).Where(related.ContainsKey).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => ReadEntityIds(related[id].GetPropertyOrDefault("claims"), "P279").ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var statementCounts = new Dictionary<(string, string), int>();
        var titles = TitleMerger.Merge(rawTitles.Select(t =>
        {
            var key = (t.EntityId, t.PropertyId);
            var number = statementCounts[key] = statementCounts.GetValueOrDefault(key) + 1;
            return new PersonTitle
            {
                Titolo = TitleRanking.Capitalize(titleLabels.GetValueOrDefault(t.EntityId, t.EntityId)),
                Numerazione = t.Ordinal,
                Inizio = t.Start,
                Fine = t.End,
                CausaFine = t.EndCauseId.Length > 0 ? titleLabels.GetValueOrDefault(t.EndCauseId, t.EndCauseId) : string.Empty,
                WikidataId = t.EntityId,
                Predecessore = IsEntityId(t.ReplacesId) ? LabelOf(t.ReplacesId) : string.Empty,
                PredecessoreWikidataId = IsEntityId(t.ReplacesId) ? t.ReplacesId : string.Empty,
                Successore = IsEntityId(t.ReplacedById) ? LabelOf(t.ReplacedById) : string.Empty,
                SuccessoreWikidataId = IsEntityId(t.ReplacedById) ? t.ReplacedById : string.Empty,
                FontiDate =
                [
                    new TitleDateSource
                    {
                        Fonte = $"Wikidata · {(t.PropertyId == "P97" ? "titolo nobiliare" : "carica ricoperta")} ({t.PropertyId}){(number > 1 ? $" n. {number}" : string.Empty)}",
                        Inizio = t.Start,
                        Fine = t.End,
                    },
                ],
            };
        }), titleSuperclasses);
        var mainTitle = TitleRanking.ChooseMain(titles, rawTitles.Where(t => t.Preferred).Select(t => t.EntityId).ToHashSet(StringComparer.Ordinal));
        var nicknames = ReadMonolingualTexts(claims, "P1449");
        var parsedName = PersonNameParser.Split(labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", title)), nicknames);
        var nickname = parsedName.Nickname.Length > 0 ? parsedName.Nickname : nicknames.FirstOrDefault() ?? string.Empty;
        // Il cognome (P734) serve solo a chi non appartiene a una vera dinastia: una "famiglia" generica
        // (es. "Mussolini family") non conta come dinastia e, se il cognome viene riconosciuto, viene scartata.
        var plainFamilyIds = familyIds
            .Where(id => related.TryGetValue(id, out var family) && IsPlainFamily(ReadEntityIds(family.GetPropertyOrDefault("claims"), "P31")))
            .ToHashSet(StringComparer.Ordinal);
        var surnameLabels = familyIds.All(plainFamilyIds.Contains)
            ? surnameIds.Where(related.ContainsKey).SelectMany(id => ReadAllLabels(related[id])).Distinct().ToList()
            : [];
        var (givenName, surname) = PersonNameParser.SplitSurname(parsedName.Name, surnameLabels);
        var keptFamilyIds = surname.Length > 0 ? familyIds.Where(id => !plainFamilyIds.Contains(id)).ToArray() : familyIds;
        var mainTitleLabels = mainTitle is not null && related.TryGetValue(mainTitle.WikidataId, out var mainTitleEntity)
            ? ReadLabels(mainTitleEntity)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var localizedNames = LocalizedNameBuilder.Build(
            Languages.Select(l => (l.Code, l.Name)),
            labels,
            ReadMonolingualTextsWithLanguage(claims, "P1449"),
            mainTitleLabels,
            surnameLabels);

        progress.Step(8, "Composizione della scheda…");
        return new WikipediaImportResult
        {
            // Le parentele restano valide anche se concluse (un matrimonio ha la data di fine P582).
            FatherIds = ReadRelativeIds(claims, "P22"),
            MotherIds = ReadRelativeIds(claims, "P25"),
            SpouseIds = ReadRelativeIds(claims, "P26"),
            ChildIds = ReadRelativeIds(claims, "P40"),
            Relations = ReadRelations(claims),
            EventIds = ReadEventIds(claims),
            Sesso = sexId switch
            {
                "Q6581097" => Sessi.Maschio,
                "Q6581072" => Sessi.Femmina,
                _ => string.Empty,
            },
            Name = givenName,
            Surname = surname,
            Suffix = parsedName.Suffix,
            Nickname = nickname,
            MainTitle = mainTitle?.Titolo ?? string.Empty,
            Titles = titles,
            LocalizedNames = localizedNames,
            Family = string.Join("; ", keptFamilyIds.Select(id => familyLabels.GetValueOrDefault(id, id))),
            Dynasties = [.. dynasties.Where(d => keptFamilyIds.Contains(d.WikidataId))],
            ArticleTitle = title,
            ArticleUrl = articleUrl,
            WikidataId = wikidataId,
            WikiTreeId = ReadWikiTreeId(claims),
            Extract = extract,
            BirthDates = birthDates,
            DeathDates = deathDates,
            BirthPlaces = birthPlaces,
            DeathPlaces = deathPlaces,
            BurialPlaces = burialPlaces,
            WikipediaLeads = wikipediaLeads,
        };
    }

    private static async Task<Dictionary<string, JsonElement>> ReadPlaceHierarchyAsync(IEnumerable<string> rootIds, ImportProgressReporter progress, CancellationToken cancellationToken)
    {
        var entities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var pending = rootIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).ToList();

        for (var depth = 0; depth < MaximumPlaceHierarchyDepth && pending.Count > 0; depth++)
        {
            progress.Sub((double)depth / MaximumPlaceHierarchyDepth, $"livello {depth + 1} di massimo {MaximumPlaceHierarchyDepth}, {entities.Count} luoghi letti");
            var remaining = pending.Where(id => !entities.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
            var batch = remaining.Take(40).ToArray();
            pending = remaining.Skip(40).ToList();
            if (batch.Length == 0)
            {
                break;
            }

            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Cclaims%7Csitelinks&sitefilter=itwiki&languages=it%7Cen&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            var responseEntities = ApiProperty(document.RootElement, "entities");

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
                TypeIds = [.. ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P31")],
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

                    foreach (var division in place.AdministrativeDivisions)
                    {
                        if (entities.TryGetValue(division.WikidataId, out var divisionEntity))
                        {
                            division.TypeIds = [.. ReadEntityIds(divisionEntity.GetPropertyOrDefault("claims"), "P31")];
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
                catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
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
                Calendario = IsJulianCalendar(value) ? HistoricalCalendar.Giuliano : string.Empty,
                Qualifiers = qualifiers,
                Sources = ReadSources(statement),
            });
        }

        return evidence
            .OrderByDescending(item => item.Rank == "preferred")
            .ThenByDescending(item => item.Sources.Count)
            .ToList();
    }

    internal static List<EntityStatement> ReadEntityStatements(JsonElement claims, string propertyId, bool keepEnded = false)
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
            if (rank == "deprecated" || (!keepEnded && HasEndTime(statement)))
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
        // Per le parentele (keepEnded) servono tutti i valori: più coniugi o figli sono tutti validi.
        var chosen = !keepEnded && collected.Any(item => item.Preferred)
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

    internal static List<string> ReadRelativeIds(JsonElement claims, string propertyId) =>
        [.. ReadEntityStatements(claims, propertyId, keepEnded: true).Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal)];

    // P1066 «allievo di», P802 «studente/allievo», P737 «influenzato da».
    internal static List<ImportedRelation> ReadRelations(JsonElement claims) =>
    [
        .. ReadRelativeIds(claims, "P1066").Select(id => new ImportedRelation(id, "Maestro e allievo", Attivo: false)),
        .. ReadRelativeIds(claims, "P802").Select(id => new ImportedRelation(id, "Maestro e allievo", Attivo: true)),
        .. ReadRelativeIds(claims, "P737").Select(id => new ImportedRelation(id, "Influenza", Attivo: false)),
    ];

    // P793 «evento significativo», P1344 «partecipò a», P607 «conflitto».
    internal static List<string> ReadEventIds(JsonElement claims) =>
        [.. new[] { "P793", "P1344", "P607" }.SelectMany(p => ReadRelativeIds(claims, p)).Distinct(StringComparer.Ordinal)];

    // Nome, descrizione e voce italiana delle persone indicate, per proporne l'import.
    public async Task<IReadOnlyList<WikidataPersonSummary>> ReadPeopleSummariesAsync(IEnumerable<string> wikidataIds, CancellationToken cancellationToken = default)
    {
        var ids = wikidataIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).ToList();
        var found = new Dictionary<string, WikidataPersonSummary>(StringComparer.Ordinal);
        foreach (var batch in ids.Chunk(50))
        {
            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Cdescriptions%7Csitelinks&sitefilter=itwiki&languages=it%7Cen&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            foreach (var item in ApiProperty(document.RootElement, "entities").EnumerateObject())
            {
                if (!item.Value.TryGetProperty("missing", out _))
                {
                    found[item.Name] = ReadPersonSummary(item.Name, item.Value);
                }
            }
        }

        return [.. ids.Where(found.ContainsKey).Select(id => found[id])];
    }

    internal static WikidataPersonSummary ReadPersonSummary(string wikidataId, JsonElement entity)
    {
        var labels = ReadLabels(entity);
        var descriptions = entity.GetPropertyOrDefault("descriptions");
        string Description(string language) => GetString(descriptions.GetPropertyOrDefault(language), "value");
        var description = Description("it");
        var sitelinks = entity.GetPropertyOrDefault("sitelinks");
        var title = sitelinks.ValueKind == JsonValueKind.Object && sitelinks.TryGetProperty("itwiki", out var itwiki)
            ? GetString(itwiki, "title").Trim()
            : string.Empty;
        return new WikidataPersonSummary(
            wikidataId,
            labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", wikidataId)),
            description.Length > 0 ? description : Description("en"),
            title);
    }

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

    private sealed record TitleStatement(string EntityId, string Ordinal, string Start, string End, string EndCauseId, bool Preferred, string PropertyId = "", string ReplacesId = "", string ReplacedById = "");

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
                yield return new TitleStatement(id, string.Empty, string.Empty, string.Empty, string.Empty, rank == "preferred", propertyId);
                continue;
            }

            yield return new TitleStatement(
                id,
                ReadSnakValue(qualifiers, "P1545"),
                ReadQualifierDate(qualifiers, "P580"),
                ReadQualifierDate(qualifiers, "P582"),
                ReadSnakValue(qualifiers, "P1534"),
                rank == "preferred",
                propertyId,
                ReadSnakValue(qualifiers, "P1365"),
                ReadSnakValue(qualifiers, "P1366"));
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

    // Legge in un'unica richiesta etichette (tutte le lingue) e affermazioni delle entità indicate.
    private static async Task<Dictionary<string, JsonElement>> ReadEntitiesAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var batch in entityIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Cclaims&languages={LanguagesParam}%7Cmul&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            foreach (var item in ApiProperty(document.RootElement, "entities").EnumerateObject())
            {
                if (!item.Value.TryGetProperty("missing", out _))
                {
                    result[item.Name] = item.Value.Clone();
                }
            }
        }

        return result;
    }
    private static async Task<Dictionary<string, string>> ReadEntityLabelsAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken, bool italianOnly = false)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        // L'API accetta al massimo 50 entità per richiesta.
        foreach (var batch in entityIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels&languages={(italianOnly ? "it" : "it%7Cen")}&format=json";
            using var document = await GetJsonAsync(url, cancellationToken);
            foreach (var item in ApiProperty(document.RootElement, "entities").EnumerateObject())
            {
                var read = ReadLabels(item.Value);
                if (italianOnly)
                {
                    if (read.TryGetValue("it", out var italian))
                    {
                        labels[item.Name] = italian;
                    }

                    continue;
                }

                labels[item.Name] = read.GetValueOrDefault("it", read.GetValueOrDefault("en", item.Name));
            }
        }

        return labels;
    }

    // Tipo proposto per ciascun luogo se diventerà un luogo specifico (frazione, monastero…):
    // prima i tipi noti, poi l'etichetta italiana del tipo Wikidata.
    private static async Task AssignPlaceTypesAsync(List<HistoricalPlaceRecord> places, CancellationToken cancellationToken)
    {
        var unknownTypes = places
            .Where(p => SpecificPlaceTypes.Describe(p.TypeIds).Length == 0)
            .SelectMany(p => p.TypeIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (unknownTypes.Count > 0)
        {
            try
            {
                labels = await ReadEntityLabelsAsync(unknownTypes, cancellationToken, italianOnly: true);
            }
            catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
            {
                // Senza etichette resta il solo riconoscimento dei tipi noti.
            }
        }

        foreach (var place in places)
        {
            place.TypeLabel = SpecificPlaceTypes.Describe(place.TypeIds, labels);
        }
    }


    // Tutte le etichette lette, compresa quella multilingue ("mul") usata spesso per i cognomi.
    /// <summary>Vero se l'entità è soltanto una famiglia generica (Q8436), non una dinastia o casata nobiliare.</summary>
    public static bool IsPlainFamily(IEnumerable<string> classIds)
    {
        var classes = classIds.ToList();
        return classes.Count > 0 && classes.All(id => id == "Q8436");
    }

    // Stati e periodi storici registrati per errore come "famiglia" (P53), es. "Spagna degli Asburgo" (Q600093).
    private static readonly HashSet<string> NonFamilyClasses = new(StringComparer.Ordinal)
    {
        "Q3024240",  // stato storico
        "Q6256",     // paese
        "Q7275",     // stato
        "Q3624078",  // stato sovrano
        "Q1250464",  // reame
        "Q417175",   // regno
        "Q48349",    // impero
        "Q11514315", // periodo storico
        "Q1156073",  // periodo della storia di un paese
    };

    public static bool IsNotAFamily(IEnumerable<string> classIds, bool hasCapital)
        => hasCapital || classIds.Any(NonFamilyClasses.Contains);

    private static IEnumerable<string> ReadAllLabels(JsonElement entity)
    {
        var values = entity.GetPropertyOrDefault("labels");
        return values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().Select(l => GetString(l.Value, "value")).Where(v => v.Length > 0).ToList()
            : [];
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

    // Nomi (etichette e alias in tutte le lingue importate) dei luoghi, per riconoscerli nelle introduzioni.
    private static async Task<Dictionary<string, List<string>>> ReadPlaceNamesAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            foreach (var batch in entityIds.Where(IsEntityId).Distinct(StringComparer.Ordinal).Chunk(50))
            {
                var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Caliases&languages={LanguagesParam}&format=json";
                using var document = await GetJsonAsync(url, cancellationToken);
                foreach (var item in ApiProperty(document.RootElement, "entities").EnumerateObject())
                {
                    var names = new List<string>();
                    if (item.Value.GetPropertyOrDefault("labels") is { ValueKind: JsonValueKind.Object } labels)
                    {
                        names.AddRange(labels.EnumerateObject().Select(l => GetString(l.Value, "value")));
                    }

                    if (item.Value.GetPropertyOrDefault("aliases") is { ValueKind: JsonValueKind.Object } aliases)
                    {
                        names.AddRange(aliases.EnumerateObject()
                            .Where(a => a.Value.ValueKind == JsonValueKind.Array)
                            .SelectMany(a => a.Value.EnumerateArray().Select(v => GetString(v, "value"))));
                    }

                    result[item.Name] = names.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                }
            }
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Senza i nomi multilingue il confronto usa le sole etichette italiane e inglesi.
        }

        return result;
    }

    internal static List<LeadPlaceCandidate> PlaceCandidates(IEnumerable<HistoricalPlaceRecord> places, IReadOnlyDictionary<string, List<string>> extraNames)
        => places
            .Where(p => p.WikidataId.Length > 0)
            .Select(p => new LeadPlaceCandidate(
                p.WikidataId,
                p.Names.Select(n => n.Name).Append(p.CurrentName).Concat(extraNames.GetValueOrDefault(p.WikidataId) ?? []).Where(n => n.Length > 0).Distinct().ToList(),
                ContainingDivisions(p)
                    .SelectMany(d => d.Names.Select(n => n.Name).Append(d.Name).Concat(extraNames.GetValueOrDefault(d.WikidataId) ?? []))
                    .Where(n => n.Length > 0)
                    .Distinct()
                    .ToList()))
            .ToList();

    // Suddivisioni amministrative (P131) che contengono il luogo, esclusi i Paesi: troppo generici per il confronto.
    private static IEnumerable<PlaceDivision> ContainingDivisions(HistoricalPlaceRecord place)
        => place.AdministrativeDivisions.Where(d => d.WikidataId.Length > 0 && d.Relation.Contains("P131", StringComparison.Ordinal));

    // Una voce per ogni edizione letta, nell'ordine delle lingue (italiano per primo).
    internal static List<WikipediaLeadRecord> BuildWikipediaLeads(
        IReadOnlyDictionary<string, LeadDate> leadDates,
        IReadOnlyList<LeadPlaceCandidate> birthCandidates,
        IReadOnlyList<LeadPlaceCandidate> deathCandidates)
    {
        var leads = new List<WikipediaLeadRecord>();
        foreach (var (code, name, _, _) in Languages)
        {
            if (!leadDates.TryGetValue(code, out var lead))
            {
                continue;
            }

            leads.Add(new WikipediaLeadRecord
            {
                Language = code,
                LanguageName = name,
                Title = lead.Title,
                Url = lead.Url,
                BirthYear = lead.BirthYear,
                BirthMonth = lead.BirthMonth,
                BirthDay = lead.BirthDay,
                DeathYear = lead.DeathYear,
                DeathMonth = lead.DeathMonth,
                DeathDay = lead.DeathDay,
                BirthPlace = lead.BirthPlace,
                BirthPlaceKey = WikipediaConsensus.PlaceKey(lead.BirthPlace, birthCandidates.Concat(deathCandidates)),
                DeathPlace = lead.DeathPlace,
                DeathPlaceKey = WikipediaConsensus.PlaceKey(lead.DeathPlace, deathCandidates.Concat(birthCandidates)),
            });
        }

        return leads;
    }

    // Scarica l'introduzione di ogni edizione linguistica e ne estrae gli anni di nascita/morte.
    private static async Task<Dictionary<string, LeadDate>> ReadLanguageLeadDatesAsync(JsonElement sitelinks, ImportProgressReporter progress, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LeadDate>(StringComparer.Ordinal);
        if (sitelinks.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        // Le edizioni sono indipendenti: si scaricano in parallelo.
        var titles = new List<(string Code, string Title)>();
        foreach (var (code, _, _, sitelink) in Languages)
        {
            if (sitelinks.TryGetProperty(sitelink, out var link) && GetString(link, "title") is { Length: > 0 } title)
            {
                titles.Add((code, title));
            }
        }

        var completed = 0;
        var progressLock = new object();
        progress.Sub(0, $"0 di {titles.Count} edizioni lette");
        var tasks = titles.Select(async item =>
        {
            var read = await ReadLeadDateAsync(item.Code, item.Title, cancellationToken);
            // Incremento e notifica insieme: le notifiche non possono arrivare fuori ordine.
            lock (progressLock)
            {
                completed++;
                progress.Sub((double)completed / titles.Count, $"{completed} di {titles.Count} edizioni lette");
            }

            return read;
        }).ToList();

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
            var info = ExtractLead(GetString(page, "extract"));
            if (info.BirthPlace.Length == 0 || info.DeathPlace.Length == 0)
            {
                // L'introduzione inglese di solito omette i luoghi («Charles V (24 February 1500 – 21 September 1558)»):
                // si leggono dall'infobox a destra.
                var (infoboxBirth, infoboxDeath) = await ReadInfoboxPlacesAsync(code, title, cancellationToken);
                info = info with
                {
                    BirthPlace = info.BirthPlace.Length > 0 ? info.BirthPlace : infoboxBirth,
                    DeathPlace = info.DeathPlace.Length > 0 ? info.DeathPlace : infoboxDeath,
                };
            }

            var articleUrl = $"https://{code}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
            return (code, new LeadDate(info.BirthYear, info.DeathYear, info.BirthPlace, info.DeathPlace, title, articleUrl)
            {
                BirthMonth = info.BirthMonth,
                BirthDay = info.BirthDay,
                DeathMonth = info.DeathMonth,
                DeathDay = info.DeathDay,
            });
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Edizione non leggibile: si prosegue con le altre.
            return (code, null);
        }
    }

    private static async Task<(string Birth, string Death)> ReadInfoboxPlacesAsync(string code, string title, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://{code}.wikipedia.org/w/api.php?action=parse&prop=wikitext&section=0&redirects=1&page={Uri.EscapeDataString(title)}&format=json&formatversion=2";
            using var document = await GetJsonAsync(url, cancellationToken);
            var wikitext = document.RootElement.GetProperty("parse").GetProperty("wikitext").GetString();
            return ExtractInfoboxPlaces(wikitext);
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            return (string.Empty, string.Empty);
        }
    }

    // Parametri dell'infobox con il luogo di nascita/morte nelle lingue importate.
    private static readonly string[] InfoboxBirthPlaceParams =
        ["birth_place", "luogo di nascita", "LuogoNascita", "lieu de naissance", "lugar de nacimiento", "geboorteplaats", "Geburtsort", "GEBURTSORT"];

    private static readonly string[] InfoboxDeathPlaceParams =
        ["death_place", "luogo di morte", "LuogoMorte", "lieu de décès", "lieu de mort", "lugar de defunción", "lugar de fallecimiento", "lugar de muerte",
         "overlijdensplaats", "sterfplaats", "Sterbeort", "STERBEORT"];

    // Luoghi di nascita/morte dai parametri dell'infobox, ripuliti dal markup:
    // «| birth_place = [[Prinsenhof, Ghent]], Flanders» → «Prinsenhof, Ghent, Flanders».
    internal static (string Birth, string Death) ExtractInfoboxPlaces(string? wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
        {
            return (string.Empty, string.Empty);
        }

        var text = Regex.Replace(wikitext, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        return (InfoboxParam(text, InfoboxBirthPlaceParams), InfoboxParam(text, InfoboxDeathPlaceParams));
    }

    private static string InfoboxParam(string wikitext, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var match = Regex.Match(wikitext, $@"^\s*\|\s*{Regex.Escape(name)}\s*=(?<value>[^\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && CleanWikitext(match.Groups["value"].Value) is { Length: >= 2 and <= 120 } place)
            {
                return place;
            }
        }

        return string.Empty;
    }

    private static string CleanWikitext(string value)
    {
        var text = Regex.Replace(value, @"<ref[^>/]*/>|<ref[^>]*>.*?</ref>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", ", ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        // Template annidati: si tolgono dall'interno verso l'esterno.
        string previous;
        do
        {
            previous = text;
            text = Regex.Replace(text, @"\{\{[^{}]*\}\}", string.Empty);
        }
        while (text != previous);
        text = Regex.Replace(text, @"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", "$1");
        text = Regex.Replace(text, @"\[https?://\S+\s*([^\]]*)\]", "$1");
        text = text.Replace("'''", string.Empty).Replace("''", string.Empty).Replace("&nbsp;", " ");
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\s*,(\s*,)+", ",");
        text = Regex.Replace(text, @"\(\s*\)", string.Empty);
        return text.Trim().Trim(',', ';', ' ');
    }

    // Errore di una richiesta accessoria (rete, timeout, risposta inattesa): si ignora senza interrompere l'import.
    // L'annullamento chiesto dall'utente invece si propaga.
    internal static bool IsOptionalFetchFailure(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    // Marcatori di epoca nelle lingue importate (it, en, de, fr, es, nl).
    private static readonly Regex BeforeChristMarker = new(
        @"\b(a\.\s?C\.?|a\.?\s?Cr\.?|BCE?|B\.C\.(E\.)?|v\.\s?Chr\.?|av\.\s?J\.?-C\.?|a\.\s?de\s?C\.?|v\.\s?C\.?)(?=$|[\s,;)\]–—-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AfterChristMarker = new(
        @"\b(d\.\s?C\.?|AD|CE|n\.\s?Chr\.?|apr\.\s?J\.?-C\.?|n\.\s?C\.?)(?=$|[\s,;)\]–—-])",
        RegexOptions.CultureInvariant);

    private static readonly Regex LeadYear = new(
        @"(?<!\d)(?<year>\d{3,4})(?!\d)|(?<!\d)(?<year>\d{1,2})(?=\s*(a\.\s?C|a\.?\s?Cr|BC|B\.C|v\.\s?Chr|av\.\s?J|a\.\s?de\s?C|v\.\s?C|d\.\s?C|AD\b|n\.\s?Chr))",
        RegexOptions.CultureInvariant);

    internal sealed record LeadInfo(int? BirthYear, int? DeathYear, string BirthPlace, string DeathPlace)
    {
        public static readonly LeadInfo Empty = new(null, null, string.Empty, string.Empty);

        public int? BirthMonth { get; init; }
        public int? BirthDay { get; init; }
        public int? DeathMonth { get; init; }
        public int? DeathDay { get; init; }
    }

    // Estrae gli anni di nascita/morte dalla prima parentesi dell'introduzione che contiene un anno
    // (es. «(1769–1821)», «(100 – 44 a.C.)»), ignorando quelle con la sola pronuncia o il nome originale.
    // Gli anni avanti Cristo sono restituiti negativi.
    internal static (int? Birth, int? Death) ExtractLeadYears(string? extract)
    {
        var lead = ExtractLead(extract);
        return (lead.BirthYear, lead.DeathYear);
    }

    // Come ExtractLeadYears, ma legge anche i luoghi: «(Ajaccio, 15 agosto 1769 – Longwood, …)»,
    // «(* 21. März 1685 in Eisenach; † …)» o, in francese, «né le … à Vinci et mort le … à Amboise».
    internal static LeadInfo ExtractLead(string? extract)
    {
        if (string.IsNullOrWhiteSpace(extract))
        {
            return LeadInfo.Empty;
        }

        var lead = extract.Length > 1200 ? extract[..1200] : extract;
        var birth = FrenchBirthSentence.Match(lead);
        var death = FrenchDeathSentence.Match(lead);

        // Una parentesi successiva alla frase «né le …» parla d'altro (es. «(menée … en octobre 1483)»).
        foreach (Match match in Regex.Matches(lead, @"\(([^()]{0,240})\)"))
        {
            if (birth.Success && match.Index > birth.Index)
            {
                break;
            }

            var info = ParseLeadParts(Regex.Split(match.Groups[1].Value, @"\s*[–—;]\s*|\s+-\s+|(?<=\d)-(?=\S)"));
            if (info.BirthYear is not null || info.DeathYear is not null)
            {
                return info;
            }
        }
        var parts = new List<string>();
        if (birth.Success)
        {
            parts.Add(birth.Groups["seg"].Value);
        }

        if (death.Success && (!birth.Success || death.Index > birth.Index))
        {
            parts.Add(death.Groups["seg"].Value);
        }

        return birth.Success ? ParseLeadParts(parts) : LeadInfo.Empty;
    }

    private static readonly Regex FrenchBirthSentence = new(
        @"\bnée?\s+(?<seg>(?:le|en|vers|à)\b.{0,160}?)(?=\s+et\s+morte?\b|\s*,\s*(?:et\s+)?morte?\b|\s*,\s*(?:est|était|fut|a\s+été)\b|\s*[;.]\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex FrenchDeathSentence = new(
        @"\bmorte?\s+(?<seg>(?:le|en|vers|à)\b.{0,160}?)(?=\s*,\s|\s*;|$)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // Luogo dopo la data, introdotto da una preposizione: «in Eisenach», «à Amboise», «en Roma», «te Delft».
    // Le virgole restano nel luogo: in «im Prinzenhof, Gent, Burgundische Niederlande» la città viene dopo l'edificio.
    private static readonly Regex PlaceAfterDate = new(
        @"(?:^|\s)(?:in|im|bei|à|a|en|te|at|near)\s+(?<place>\p{Lu}[^;()\[\]\d.]*)",
        RegexOptions.CultureInvariant);

    // «ebenda», «ibidem», «ivi»: la morte è avvenuta nello stesso luogo della nascita.
    private static readonly Regex SamePlaceMarker = new(@"\b(ebenda|ibidem|ibid|ivi|ídem|aldaar|ibídem)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static LeadInfo ParseLeadParts(IEnumerable<string> rawParts)
    {
        var parts = rawParts
            .Select(text => (Text: text, Parsed: ParseLeadPart(text)))
            .Where(part => part.Parsed.Year is not null)
            .Take(2)
            .ToList();
        if (parts.Count == 0)
        {
            return LeadInfo.Empty;
        }

        var birth = parts[0].Parsed;
        var birthPlace = LeadPlace(parts[0].Text, birth.End);
        var (birthMonth, birthDay) = ParseDayMonth(parts[0].Text[..birth.Start]);
        if (parts.Count == 1)
        {
            return new LeadInfo(Signed(birth.Year!.Value, birth.BeforeChrist), null, birthPlace, string.Empty) { BirthMonth = birthMonth, BirthDay = birthDay };
        }

        var death = parts[1].Parsed;
        var deathPlace = LeadPlace(parts[1].Text, death.End);
        if (deathPlace.Length == 0 && SamePlaceMarker.IsMatch(parts[1].Text))
        {
            deathPlace = birthPlace;
        }

        // «100 – 44 a.C.»: il marcatore compare solo alla fine ma vale anche per la nascita,
        // purché non sia successiva alla morte (in quel caso la nascita è d.C.).
        var birthBc = birth.BeforeChrist || (death.BeforeChrist && !birth.AfterChrist && birth.Year >= death.Year);
        var (deathMonth, deathDay) = ParseDayMonth(parts[1].Text[..death.Start]);
        return new LeadInfo(Signed(birth.Year!.Value, birthBc), Signed(death.Year!.Value, death.BeforeChrist), birthPlace, deathPlace)
        {
            BirthMonth = birthMonth,
            BirthDay = birthDay,
            DeathMonth = deathMonth,
            DeathDay = deathDay,
        };
    }

    // Il luogo precede la data («Ajaccio, 15 agosto 1769») oppure la segue con una preposizione («1685 in Eisenach»).
    private static string LeadPlace(string part, int yearEnd)
    {
        var text = part.Trim().TrimStart('*', '†', '✝', '+', '~', ' ', '\u00a0');
        var comma = text.IndexOf(',');
        if (comma > 0)
        {
            var head = text[..comma].Trim();
            if (head.Length is >= 2 and <= 60 && char.IsUpper(head[0]) && !head.Any(char.IsDigit) && !head.Contains(':'))
            {
                return head;
            }
        }

        var after = PlaceAfterDate.Match(part[Math.Min(yearEnd, part.Length)..]);
        if (after.Success)
        {
            var place = after.Groups["place"].Value.Trim().TrimEnd(',').Trim();
            if (place.Length > 80 && place.IndexOf(',') is var cut and > 0)
            {
                place = place[..cut].Trim();
            }

            return place.Length is >= 2 and <= 80 ? place : string.Empty;
        }

        return string.Empty;
    }

    private static (int? Year, bool BeforeChrist, bool AfterChrist, int Start, int End) ParseLeadPart(string part)
    {
        var yearMatch = LeadYear.Match(part);
        if (!yearMatch.Success || !int.TryParse(yearMatch.Groups["year"].Value, out var year) || year is <= 0 or > 2100)
        {
            return (null, false, false, 0, 0);
        }

        var end = yearMatch.Index + yearMatch.Length;
        var tail = part[end..];
        var beforeChrist = BeforeChristMarker.IsMatch(tail);
        var afterChrist = AfterChristMarker.IsMatch(tail);
        return (year, beforeChrist, afterChrist, yearMatch.Index, end);
    }

    // Nomi (e abbreviazioni) dei mesi nelle lingue importate, già normalizzati senza diacritici.
    private static readonly Dictionary<string, int> MonthNames = BuildMonthNames(
        ["gennaio", "january", "januar", "janner", "janvier", "enero", "januari", "jan", "janv", "gen", "ene"],
        ["febbraio", "february", "februar", "fevrier", "febrero", "februari", "feb", "febr", "fevr"],
        ["marzo", "march", "marz", "maerz", "mars", "maart", "mar", "mrt"],
        ["aprile", "april", "avril", "abril", "apr", "avr"],
        ["maggio", "may", "mai", "mayo", "mei", "mag"],
        ["giugno", "june", "juni", "juin", "junio", "jun", "giu"],
        ["luglio", "july", "juli", "juillet", "julio", "jul", "lug", "juil"],
        ["agosto", "august", "aout", "augustus", "aug", "ago"],
        ["settembre", "september", "septembre", "septiembre", "setiembre", "sep", "sept", "set"],
        ["ottobre", "october", "oktober", "octobre", "octubre", "oct", "okt", "ott"],
        ["novembre", "november", "noviembre", "nov"],
        ["dicembre", "december", "dezember", "decembre", "diciembre", "dec", "dez", "dic"]);

    private static Dictionary<string, int> BuildMonthNames(params string[][] months)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < months.Length; i++)
        {
            foreach (var name in months[i])
            {
                result[name] = i + 1;
            }
        }

        return result;
    }

    // «15 agosto», «15. August», «15 de agosto de», «1er janvier», «1º gennaio».
    private static readonly Regex DayBeforeMonth = new(
        @"(?<!\d)(?<day>\d{1,2})(?:\.|º|°|ª|er|e|st|nd|rd|th)?\s*(?:de\s+)?(?<month>\p{L}+)\.?\s*(?:de|del|of)?\s*$",
        RegexOptions.CultureInvariant);

    // «August 15,» (inglese).
    private static readonly Regex MonthBeforeDay = new(
        @"(?<!\p{L})(?<month>\p{L}+)\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s*$",
        RegexOptions.CultureInvariant);

    // Solo il mese: «agosto 1769», «August 1769».
    private static readonly Regex MonthOnly = new(
        @"(?<!\p{L})(?<month>\p{L}+)\.?\s*(?:de|del|of)?\s*$",
        RegexOptions.CultureInvariant);

    // Mese e giorno nel testo che precede l'anno; null se non indicati.
    internal static (int? Month, int? Day) ParseDayMonth(string textBeforeYear)
    {
        var text = textBeforeYear ?? string.Empty;
        foreach (var regex in new[] { DayBeforeMonth, MonthBeforeDay })
        {
            var match = regex.Match(text);
            if (match.Success
                && MonthNames.TryGetValue(WikipediaConsensus.Normalize(match.Groups["month"].Value), out var month)
                && int.TryParse(match.Groups["day"].Value, out var day) && day is >= 1 and <= 31)
            {
                return (month, day);
            }
        }

        var monthOnly = MonthOnly.Match(text);
        return monthOnly.Success && MonthNames.TryGetValue(WikipediaConsensus.Normalize(monthOnly.Groups["month"].Value), out var onlyMonth)
            ? (onlyMonth, null)
            : (null, null);
    }

    private static int Signed(int year, bool beforeChrist) => beforeChrist ? -year : year;

    // Combina i valori di data di Wikidata con gli anni letti dalle varie Wikipedia, raggruppando per anno.
    private static List<HistoricalDateEvidence> BuildCrossLanguageDates(
        List<HistoricalDateEvidence> wikidataEvidence,
        Dictionary<string, LeadDate> leadDates,
        bool birth)
    {
        var groups = new Dictionary<int, HistoricalDateEvidence>();
        var order = new List<int>();

        void EnsureGroup(int year, string value, string precision, string rawValue, Dictionary<string, List<string>>? qualifiers, string rank, IEnumerable<SourceCitation> sources, string calendar = "")
        {
            if (!groups.TryGetValue(year, out var evidence))
            {
                evidence = new HistoricalDateEvidence { Value = value, RawValue = rawValue, Precision = precision, Rank = rank, Calendario = calendar };
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
                evidence.Calendario = calendar;
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

            EnsureGroup(year, e.Value, e.Precision.Length > 0 ? e.Precision : "anno", e.RawValue, e.Qualifiers, e.Rank, e.Sources, e.Calendario);
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
            var sign = y < 0 ? "-" : "+";
            var value = FormatYear(Math.Abs(y), y < 0);
            EnsureGroup(y, value, "anno", $"{sign}{Math.Abs(y):0000}-00-00T00:00:00Z", null, string.Empty, [citation]);
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

    // Data Wikidata in testo leggibile e riconoscibile da HistoricalYear: «15/03/44 a.C.», «anni 1450», «XV secolo».
    internal static string FormatDate(string rawValue, int precision)
    {
        var match = Regex.Match(rawValue, @"^(?<sign>[+-])(?<year>\d+)-(?<month>\d{2})-(?<day>\d{2})");
        if (!match.Success || !int.TryParse(match.Groups["year"].Value, out var year) || year == 0)
        {
            return rawValue;
        }

        var beforeChrist = match.Groups["sign"].Value == "-";
        var era = beforeChrist ? " a.C." : string.Empty;
        return precision switch
        {
            11 => $"{match.Groups["day"].Value}/{match.Groups["month"].Value}/{FormatYear(year, beforeChrist)}",
            10 => $"{match.Groups["month"].Value}/{FormatYear(year, beforeChrist)}",
            8 => $"anni {year / 10 * 10}{era}",
            7 => $"{RomanNumeral((year - 1) / 100 + 1)} secolo{era}",
            6 => $"{RomanNumeral((year - 1) / 1000 + 1)} millennio{era}",
            _ => FormatYear(year, beforeChrist),
        };
    }

    // Senza zeri iniziali; gli anni d.C. a una o due cifre sono marcati per non confonderli con giorni o mesi.
    internal static string FormatYear(int year, bool beforeChrist) => beforeChrist
        ? $"{year} a.C."
        : year < 100 ? $"{year} d.C." : year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static string RomanNumeral(int value)
    {
        var numerals = new (int Value, string Symbol)[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var builder = new System.Text.StringBuilder();
        foreach (var (number, symbol) in numerals)
        {
            for (; value >= number; value -= number)
            {
                builder.Append(symbol);
            }
        }

        return builder.ToString();
    }

    // Wikidata scrive le date nel calendario indicato da calendarmodel: Q1985786 è il giuliano prolettico.
    internal static bool IsJulianCalendar(JsonElement timeValue)
        => GetString(timeValue, "calendarmodel").EndsWith("/Q1985786", StringComparison.Ordinal);

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

     private static readonly SemaphoreSlim WikidataGate = new(1, 1);
     private static DateTime lastWikidataRequest = DateTime.MinValue;
     private static readonly TimeSpan WikidataInterval = TimeSpan.FromMilliseconds(250);

     private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
     {
          var isWikidata = url.StartsWith(WikidataApi, StringComparison.Ordinal);
          for (var attempt = 0; ; attempt++)
          {
                HttpResponseMessage response;
                if (isWikidata)
                {
                     // Richieste a Wikidata una alla volta, distanziate di un breve intervallo.
                     await WikidataGate.WaitAsync(cancellationToken);
                     try
                     {
                          var elapsed = DateTime.UtcNow - lastWikidataRequest;
                          if (elapsed < WikidataInterval)
                          {
                                await Task.Delay(WikidataInterval - elapsed, cancellationToken);
                          }

                          response = await Client.GetAsync(url, cancellationToken);
                          lastWikidataRequest = DateTime.UtcNow;
                     }
                     finally
                     {
                          WikidataGate.Release();
                     }
                }
                else
                {
                     response = await Client.GetAsync(url, cancellationToken);
                }

                using (response)
                {
                     if ((response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                          && attempt < MaximumRateLimitRetries)
                     {
                          await Task.Delay(RetryDelay(response.Headers.RetryAfter, attempt), cancellationToken);
                          continue;
                     }

                     response.EnsureSuccessStatusCode();
                     return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                }
          }
     }

     // Le API MediaWiki rispondono 200 anche in caso di errore ({"error":{...}}): il messaggio va mostrato chiaramente.
    internal static JsonElement ApiProperty(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
        {
            return value;
        }

        var info = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? GetString(error, "info")
            : string.Empty;
        throw new InvalidOperationException(info.Length > 0
            ? $"Wikipedia ha restituito un errore: {info}"
            : "Risposta di Wikipedia non valida o incompleta.");
    }

    // Attesa indicata dal server (secondi o data), altrimenti crescente a ogni tentativo; massimo 30 secondi.
     internal static TimeSpan RetryDelay(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter, int attempt)
     {
          var wait = retryAfter?.Delta
                ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2 << attempt));
          if (wait < TimeSpan.FromSeconds(1))
          {
                wait = TimeSpan.FromSeconds(1);
          }

          return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
     }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // Wikimedia strozza (429) gli User-Agent senza contatto.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AlberiStorici/1.0 (github.com/msonzo1/HistoricEditor)");
        return client;
    }

    internal sealed record EntityStatement(string EntityId, List<SourceCitation> Sources);
}

internal static class JsonElementExtensions
{
    public static JsonElement GetPropertyOrDefault(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) ? value : default;
}
