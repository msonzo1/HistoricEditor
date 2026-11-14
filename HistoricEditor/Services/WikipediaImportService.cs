using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public sealed partial class WikipediaImportService
{
    private const string WikipediaApi = "https://it.wikipedia.org/w/api.php";
    private const string WikidataApi = "https://www.wikidata.org/w/api.php";
    private const int MaximumPlaceHierarchyDepth = 6;
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
        public int? BirthDayAlt { get; init; }
        public int? DeathMonth { get; init; }
        public int? DeathDay { get; init; }
        public int? DeathDayAlt { get; init; }

        // Periodi di regno dell'infobox, per completare le date dei titoli.
        public IReadOnlyList<InfoboxReign> Reigns { get; init; } = [];

        // Introduzione della voce, dove cercare il nome completo se manca altrove.
        public string Extract { get; init; } = string.Empty;

        // Luoghi scritti nell'infobox, anche quando l'introduzione li indica: per i luoghi antichi l'infobox italiana
        // riporta spesso il nome latino («Vicus Phalacrinae»).
        public string InfoboxBirthPlace { get; init; } = string.Empty;
        public string InfoboxDeathPlace { get; init; } = string.Empty;
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

    // Le lingue senza etichetta Wikidata prendono il titolo della voce di Wikipedia in quella lingua, senza la parte
    // tra parentesi: Cesare non ha un'etichetta olandese, ma la voce si intitola «Gaius Julius Caesar».
    internal static Dictionary<string, string> WithSitelinkTitles(Dictionary<string, string> labels, JsonElement sitelinks)
    {
        foreach (var (code, _, _, sitelink) in Languages)
        {
            if (!labels.ContainsKey(code)
                && GetString(sitelinks.GetPropertyOrDefault(sitelink), "title") is { Length: > 0 } title
                && Regex.Replace(title, @"\s*\([^()]*\)\s*$", string.Empty).Trim() is { Length: > 0 } name)
            {
                labels[code] = name;
            }
        }

        return labels;
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

    // Famiglie senza etichetta italiana su Wikidata: il nome è il titolo della voce di Wikipedia in italiano, senza la
    // parte tra parentesi, invece dell'etichetta inglese («Nerva–Antonine dynasty» per Traiano). Una richiesta, solo se serve.
    private static async Task UseItalianArticleTitlesAsync(Dictionary<string, string> familyLabels, IReadOnlyDictionary<string, JsonElement> related, CancellationToken cancellationToken)
    {
        var missing = familyLabels.Keys
            .Where(id => !(related.TryGetValue(id, out var e) && ReadLabels(e).ContainsKey("it")))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        try
        {
            using var document = await GetJsonAsync(
                $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', missing))}&props=sitelinks&sitefilter=itwiki&format=json",
                cancellationToken);
            foreach (var id in missing)
            {
                var title = Regex.Replace(ReadItalianSitelinkTitle(document.RootElement, id), @"\s*\([^()]*\)\s*$", string.Empty).Trim();
                if (title.Length > 0)
                {
                    familyLabels[id] = title;
                }
            }
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Resta l'etichetta in un'altra lingua.
        }
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
            $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(wikidataId)}&props=labels%7Caliases%7Cclaims%7Csitelinks&languages={LanguagesParam}&format=json",
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
        var labels = WithSitelinkTitles(ReadLabels(entity), entity.GetPropertyOrDefault("sitelinks"));
        var claims = entity.GetPropertyOrDefault("claims");

        progress.Step(3, "Lettura delle altre edizioni linguistiche…");
        var (leadDates, unreadEditions) = await ReadLanguageLeadDatesAsync(entity.GetPropertyOrDefault("sitelinks"), progress, cancellationToken);

        var wikidataBirthDates = ReadDateEvidence(claims, "P569");
        var wikidataDeathDates = ReadDateEvidence(claims, "P570");
        var (birthDates, discardedBirthDates) = BuildCrossLanguageDates(wikidataBirthDates, leadDates, birth: true);
        var (deathDates, discardedDeathDates) = BuildCrossLanguageDates(wikidataDeathDates, leadDates, birth: false);
        var birthPlaceStatements = ReadEntityStatements(claims, "P19");
        var deathPlaceStatements = ReadEntityStatements(claims, "P20");
        var burialPlaceStatements = ReadEntityStatements(claims, "P119");
        var placeIds = birthPlaceStatements.Concat(deathPlaceStatements).Concat(burialPlaceStatements).Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        progress.Step(4, "Analisi della gerarchia dei luoghi…");
        var placeEntities = await ReadPlaceHierarchyAsync(placeIds, progress, cancellationToken);
        var birthPlaces = BuildPlaces(birthPlaceStatements, placeEntities);
        var deathPlaces = BuildPlaces(deathPlaceStatements, placeEntities);
        var burialPlaces = BuildPlaces(burialPlaceStatements, placeEntities);

        // Luoghi senza comune su Wikidata (località antiche o scomparse): il comune attuale più vicino, dalle coordinate.
        var nearbyMunicipalities = await FindNearbyMunicipalitiesAsync([.. birthPlaces, .. deathPlaces, .. burialPlaces], cancellationToken);
        if (nearbyMunicipalities.Count > 0)
        {
            foreach (var (id, municipality) in await ReadPlaceHierarchyAsync(nearbyMunicipalities.Values, progress, cancellationToken))
            {
                placeEntities.TryAdd(id, municipality);
            }

            birthPlaces = BuildPlaces(birthPlaceStatements, placeEntities, nearbyMunicipalities);
            deathPlaces = BuildPlaces(deathPlaceStatements, placeEntities, nearbyMunicipalities);
            burialPlaces = BuildPlaces(burialPlaceStatements, placeEntities, nearbyMunicipalities);
        }
        progress.Step(5, "Lettura delle suddivisioni e dei tipi di luogo…");
        await ReadInfoboxDivisionsAsync(birthPlaces.Concat(deathPlaces).Concat(burialPlaces), placeEntities, cancellationToken);
        await ReadHistoricalNamesAsync(birthPlaces.Concat(deathPlaces).Concat(burialPlaces), placeEntities, cancellationToken);
        progress.Sub(0.5, "tipi di luogo");
        var typeLabels = await AssignPlaceTypesAsync([.. birthPlaces, .. deathPlaces, .. burialPlaces], cancellationToken);
        progress.Sub(0.8, "luoghi antichi");
        await AssignHistoricPlaceNamesAsync(birthPlaces, deathPlaces, burialPlaces, placeEntities, leadDates, typeLabels, cancellationToken);
        progress.Step(6, "Lettura dei nomi dei luoghi nelle altre lingue…");
        var leadPlaceNames = leadDates.Values.Any(l => l.BirthPlace.Length > 0 || l.DeathPlace.Length > 0)
            ? await ReadPlaceNamesAsync(
                birthPlaces.Concat(deathPlaces).SelectMany(p => ContainingDivisions(p).Select(d => d.WikidataId).Prepend(p.WikidataId)),
                cancellationToken)
            : [];
        var wikipediaLeads = BuildWikipediaLeads(
            leadDates,
            PlaceCandidates(birthPlaces, leadPlaceNames),
            PlaceCandidates(deathPlaces, leadPlaceNames),
            BuildWikidataLead(wikidataId, wikidataBirthDates, wikidataDeathDates, birthPlaces, deathPlaces, birthDates.FirstOrDefault(), deathDates.FirstOrDefault()));
        var familyIds = ReadEntityStatements(claims, "P53").Select(statement => statement.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        var surnameIds = ReadEntityIds(claims, "P734").Distinct(StringComparer.Ordinal).ToArray();

        // Onomastica romana: praenomen, nomen gentilicium, cognomina, agnomina e gens.
        var romanPartIds = ReadRomanNameParts(claims);
        var (gensIds, adoptiveGensIds) = ReadGensIds(claims);
        var endedNomenIds = ReadEndedRomanPartIds(claims, "P2359");
        // Tutte le parti abbandonate, per riconoscere il tipo delle parole dei nomi precedenti (vedi RomanNamesOverTime).
        var endedPartIds = RomanNameProperties.ToDictionary(p => p, p => ReadEndedRomanPartIds(claims, p), StringComparer.Ordinal);
        progress.Step(7, "Lettura di famiglie e titoli…");
        var rawTitles = ReadTitleStatements(claims, "P39").Concat(ReadTitleStatements(claims, "P97")).ToList();
        var sexId = ReadEntityIds(claims, "P21").FirstOrDefault();

        // Prima richiesta: famiglie, nomi romani, titoli e cause di fine con etichette e affermazioni insieme.
        var related = await ReadEntitiesAsync(
            familyIds.Concat(surnameIds).Concat(gensIds).Concat(romanPartIds.Values.SelectMany(ids => ids)).Concat(endedPartIds.Values.SelectMany(ids => ids))
                .Concat(rawTitles.SelectMany(t => new[] { t.EntityId, t.EndCauseId })),
            cancellationToken);
        // Senza gens di nascita su Wikidata (Marco Aurelio ha «valore sconosciuto») la si ricava dal nomen portato alla
        // nascita, cioè quello abbandonato se c'è, senza contare la gens d'adozione; se non si trova resta vuota (la gens
        // d'adozione ha un campo proprio).
        if (gensIds.Length == 0)
        {
            gensIds = [.. GensOfNomina(endedNomenIds.Length > 0 ? endedNomenIds : romanPartIds["P2359"], related).Except(adoptiveGensIds, StringComparer.Ordinal)];
        }

        foreach (var (id, gens) in await ReadEntitiesAsync(gensIds.Concat(adoptiveGensIds).Where(id => !related.ContainsKey(id)), cancellationToken))
        {
            related[id] = gens;
        }

        adoptiveGensIds = [.. adoptiveGensIds.Except(gensIds, StringComparer.Ordinal)];
        var allGensIds = gensIds.Concat(adoptiveGensIds).ToArray();
        familyIds = [.. familyIds.Where(id => !related.TryGetValue(id, out var family)
            || family.GetPropertyOrDefault("claims") is var familyClaims
            && !IsNotAFamily(ReadEntityIds(familyClaims, "P31"), ReadEntityIds(familyClaims, "P36").Any()))];
        // Dinastia superiore: il primo P361 (poi P279) che non sia una pagina di servizio di Wikipedia; la «Dinastia flavia»
        // indica come «parte di» la «list of Roman dynasties», che non è una dinastia.
        var parentCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in familyIds.Concat(allGensIds).Distinct(StringComparer.Ordinal))
        {
            if (related.TryGetValue(id, out var family))
            {
                var familyClaims = family.GetPropertyOrDefault("claims");
                var candidates = ReadEntityIds(familyClaims, "P361").Concat(ReadEntityIds(familyClaims, "P279"))
                    .Where(p => p != id).Distinct(StringComparer.Ordinal).ToList();
                if (candidates.Count > 0)
                {
                    parentCandidates[id] = candidates;
                }
            }
        }

        foreach (var (id, parent) in await ReadEntitiesAsync(parentCandidates.Values.SelectMany(c => c).Where(p => !related.ContainsKey(p)), cancellationToken))
        {
            related[id] = parent;
        }

        var familyParents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, candidates) in parentCandidates)
        {
            var parent = candidates.FirstOrDefault(p => !related.TryGetValue(p, out var e) || !IsWikimediaPage(ReadEntityIds(e.GetPropertyOrDefault("claims"), "P31")));
            if (parent is not null)
            {
                familyParents[id] = parent;
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

        var familyLabels = familyIds.Concat(allGensIds).Distinct(StringComparer.Ordinal).ToDictionary(id => id, LabelOf, StringComparer.Ordinal);
        await UseItalianArticleTitlesAsync(familyLabels, related, cancellationToken);
        var parentLabels = familyParents.Values.Distinct(StringComparer.Ordinal).ToDictionary(id => id, LabelOf, StringComparer.Ordinal);
        var dynasties = familyIds.Concat(allGensIds).Distinct(StringComparer.Ordinal).Select(id =>
        {
            var parentId = familyParents.GetValueOrDefault(id, string.Empty);
            return new ImportedDynasty(
                familyLabels.GetValueOrDefault(id, id),
                id,
                parentId.Length == 0 ? string.Empty : familyLabels.GetValueOrDefault(parentId, parentLabels.GetValueOrDefault(parentId, parentId)),
                parentId,
                Gens: allGensIds.Contains(id));
        }).ToList();
        dynasties = MarkGensLine(dynasties);

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
        progress.Sub(0.5, "periodi di regno dalle infobox");
        await AddInfoboxReignSourcesAsync(titles, leadDates, cancellationToken);
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

        string RomanPart(string propertyId) => string.Join(' ', romanPartIds[propertyId]
            .Select(id => related.TryGetValue(id, out var e) ? LatinLabel(e) : string.Empty)
            .Where(v => v.Length > 0));
        var femaleSex = sexId == "Q6581072";
        var praenomen = RomanPart("P2358");
        var nomen = femaleSex ? RomanNaming.FeminineNomen(RomanPart("P2359")) : RomanPart("P2359");
        var cognomen = RomanPart("P2365");
        var agnomen = RomanPart("P2366");
        var latinName = ImportedLatinName(ReadMonolingualTextsWithLanguage(claims, "P1559"), RomanNaming.LatinName(praenomen, nomen, cognomen, agnomen, null));

        // Le stesse parti in ogni lingua: «Gaio Giulio Cesare», «Gaius Julius Caesar»…
        string LocalPart(string propertyId, string language) => string.Join(' ', romanPartIds[propertyId]
            .Select(id => related.TryGetValue(id, out var e) ? GetString(e.GetPropertyOrDefault("labels").GetPropertyOrDefault(language), "value") : string.Empty)
            .Where(v => v.Length > 0));
        RomanParts LocalParts(string language)
        {
            var parts = LocalRomanParts(
                PersonNameParser.Split(labels.GetValueOrDefault(language, string.Empty), nicknames).Name,
                ReadAliases(entity, language),
                new RomanParts(praenomen, RomanPart("P2359"), cognomen, agnomen),
                new RomanParts(LocalPart("P2358", language), LocalPart("P2359", language), LocalPart("P2365", language), LocalPart("P2366", language)),
                leadDates.GetValueOrDefault(language)?.Extract);
            return femaleSex ? parts with { Gentilizio = RomanNaming.FeminineNomen(parts.Gentilizio) } : parts;
        }
        var italianParts = ItalianRomanParts(LocalParts("it"), new RomanParts(praenomen, nomen, cognomen, agnomen));
        // Nomi nel tempo dall'introduzione italiana; il tipo delle parole viene dalle parti di Wikidata (attuali e abbandonate).
        var knownRomanTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (property, tipo) in new[] { ("P2358", TipiParteNome.Praenomen), ("P2359", TipiParteNome.Nomen), ("P2365", TipiParteNome.Cognomen), ("P2366", TipiParteNome.Agnomen) })
        {
            foreach (var id in romanPartIds[property].Concat(endedPartIds[property]))
            {
                if (related.TryGetValue(id, out var part) && LatinLabel(part) is { Length: > 0 } label)
                {
                    foreach (var word in label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        knownRomanTypes.TryAdd(word, tipo);
                    }
                }
            }
        }

        var mainTitleStart = mainTitle is null ? null : HistoricalYear.Parse(mainTitle.Inizio);
        var romanNamesOverTime = RomanNamesOverTime(
            leadDates.GetValueOrDefault("it")?.Extract,
            knownRomanTypes,
            HistoricalYear.Parse(birthDates.FirstOrDefault()?.Value),
            mainTitleStart,
            mainTitle is not null && Regex.IsMatch(mainTitle.Titolo, @"^(Imperator[ei]|Imperatrice|Re|Regina|Faraone)\b"));
        localizedNames = AddRomanParts(localizedNames, Languages.Where(l => l.Code != "it").Select(l => (l.Code, l.Name, LocalParts(l.Code))));
        // La gens e i suoi rami nel campo Gens, le dinastie nel campo Famiglia (vedi MarkGensLine).
        var gensLine = dynasties.Where(d => d.Gens).Select(d => d.WikidataId).ToHashSet(StringComparer.Ordinal);
        var gensNames = gensIds.Concat(keptFamilyIds.Where(gensLine.Contains)).Distinct(StringComparer.Ordinal).Select(id => familyLabels.GetValueOrDefault(id, id));
        var adoptiveGensNames = adoptiveGensIds.Select(id => familyLabels.GetValueOrDefault(id, id));
        var familyNames = keptFamilyIds.Where(id => !gensLine.Contains(id)).Distinct(StringComparer.Ordinal).Select(id => familyLabels.GetValueOrDefault(id, id));

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
            Praenomen = praenomen,
            Nomen = nomen,
            Cognomen = cognomen,
            Agnomen = agnomen,
            NomeLatino = latinName,
            Prenome = italianParts.Prenome,
            Gentilizio = italianParts.Gentilizio,
            CognomeRomano = italianParts.Cognome,
            Agnome = italianParts.Agnome,
            NomiNelTempo = romanNamesOverTime,
            Family = string.Join("; ", familyNames),
            Gens = string.Join("; ", gensNames),
            GensAdozione = string.Join("; ", adoptiveGensNames),
            Dynasties = [.. dynasties.Where(d => d.Gens || keptFamilyIds.Contains(d.WikidataId))],
            ArticleTitle = title,
            ArticleUrl = articleUrl,
            WikidataId = wikidataId,
            WikiTreeId = ReadWikiTreeId(claims),
            Extract = extract,
            BirthDates = birthDates,
            DeathDates = deathDates,
            DiscardedBirthDates = discardedBirthDates,
            DiscardedDeathDates = discardedDeathDates,
            BirthPlaces = birthPlaces,
            DeathPlaces = deathPlaces,
            BurialPlaces = burialPlaces,
            WikipediaLeads = wikipediaLeads,
            UnreadEditions = unreadEditions,
        };
    }

    // Gens e dinastia sono separate (2026-11-12, da Traiano): la gens e i suoi rami (Ulpii Traiani, ramo della gens
    // Ulpia) vanno nel campo Gens e sono registrati come gens; le altre famiglie (Dinastia Nerva-Antonina) nel campo
    // Famiglia, come dinastie, con la dinastia superiore indicata da Wikidata. Una dinastia non si appende più al ramo
    // della gens (regola del 2026-11-11, tolta): una dinastia di più gens (la Nerva-Antonina comprende Cocceii, Ulpii,
    // Aelii…) sarebbe finita sotto la gens del primo personaggio importato.
    internal static List<ImportedDynasty> MarkGensLine(List<ImportedDynasty> dynasties)
    {
        var parentOf = dynasties.Where(d => d.ParentWikidataId.Length > 0)
            .GroupBy(d => d.WikidataId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ParentWikidataId, StringComparer.Ordinal);
        bool InGensLine(ImportedDynasty dynasty)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var id = dynasty.WikidataId; id.Length > 0 && visited.Add(id); id = parentOf.GetValueOrDefault(id, string.Empty))
            {
                if (dynasties.Any(d => d.Gens && d.WikidataId == id))
                {
                    return true;
                }
            }

            return false;
        }

        return [.. dynasties.Select(d => !d.Gens && d.WikidataId.Length > 0 && InGensLine(d) ? d with { Gens = true } : d)];
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
        IReadOnlyDictionary<string, JsonElement> entities,
        IReadOnlyDictionary<string, string>? nearbyMunicipalities = null)
    {
        var places = new List<HistoricalPlaceRecord>();
        var labels = entities.ToDictionary(pair => pair.Key, pair => ReadLabels(pair.Value), StringComparer.Ordinal);
        var coordinates = entities.ToDictionary(pair => pair.Key, pair => ReadCoordinate(pair.Value.GetPropertyOrDefault("claims")), StringComparer.Ordinal);

        foreach (var statement in statements)
        {
            // Una pagina di servizio (Vespasiano: sepoltura «Inglaterra», una disambigua) non è un luogo.
            if (!entities.TryGetValue(statement.EntityId, out var entity)
                || IsWikimediaPage(ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P31")))
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
                if (currentId == statement.EntityId && nearbyMunicipalities?.GetValueOrDefault(currentId) is { } municipalityId)
                {
                    AddDivision(place, municipalityId, currentId, PlaceDivision.NearbyMunicipalityRelation, labels, coordinates);
                    pending.Enqueue(municipalityId);
                }

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
            // Una data deprecata è segnalata come errata su Wikidata (Tito: 40 e 41 accanto al 39): non è un'alternativa.
            if (GetString(statement, "rank") == "deprecated"
                || rawValue.Length == 0 || !value.TryGetProperty("precision", out var precisionElement) || !precisionElement.TryGetInt32(out var precision))
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
            // Il latino serve per le parti del nome romano (praenomen, nomen, cognomen).
            var url = $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', batch))}&props=labels%7Cclaims&languages={LanguagesParam}%7Cla%7Cmul&format=json";
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
    // Restituisce le etichette italiane lette per i tipi non riconosciuti (servono anche per i luoghi storici).
    private static async Task<Dictionary<string, string>> AssignPlaceTypesAsync(List<HistoricalPlaceRecord> places, CancellationToken cancellationToken)
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

        return labels;
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

    // Pagine di servizio di Wikipedia registrate come elementi Wikidata: elenchi, disambigue, categorie, template.
    private static readonly HashSet<string> WikimediaPageClasses = new(StringComparer.Ordinal)
    {
        "Q13406463", // pagina elenco di Wikimedia
        "Q4167410",  // pagina di disambiguazione
        "Q4167836",  // categoria di Wikimedia
        "Q11266439", // template di Wikimedia
    };

    internal static bool IsWikimediaPage(IEnumerable<string> classIds) => classIds.Any(WikimediaPageClasses.Contains);

    private static IEnumerable<string> ReadAllLabels(JsonElement entity)
    {
        var values = entity.GetPropertyOrDefault("labels");
        return values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().Select(l => GetString(l.Value, "value")).Where(v => v.Length > 0).ToList()
            : [];
    }

    // Praenomen, nomen gentilicium, cognomen e agnomen.
    private static readonly string[] RomanNameProperties = ["P2358", "P2359", "P2365", "P2366"];

    // Elementi di tutte le parti del nome romano. Un agnomen si aggiunge a un cognomen e non lo sostituisce: se il
    // cognomen manca (o era solo contestato) e c'è un agnomen, l'agnomen diventa il cognomen. Vespasiano ha su Wikidata
    // il cognomen «Sabinus» contestato e «Vespasianus» come agnomen, ma è «Titus Flavius Vespasianus» con
    // Vespasianus cognomen, come il figlio Tito.
    internal static Dictionary<string, string[]> ReadRomanNameParts(JsonElement claims)
    {
        var parts = RomanNameProperties.ToDictionary(p => p, p => ReadRomanPartIds(claims, p), StringComparer.Ordinal);
        if (parts["P2365"].Length == 0 && parts["P2366"].Length > 0)
        {
            parts["P2365"] = parts["P2366"];
            parts["P2366"] = [];
        }
        return parts;
    }

    // Elementi di una parte del nome romano, senza quelli deprecati o contestati (P5102 «natura dell'affermazione»
    // = Q18912752 «contestato»): Vespasiano ha il cognomen «Sabinus» contestato, che non fa parte del suo nome.
    // Le parti abbandonate (con «causa della fine» P1534 o data di fine P582) appartengono a un nome precedente e non
    // entrano nel nome: Marco Aurelio, nato Marcus Annius Catilius Severus, ha come nomen attuali Aelius e Aurelius.
    // Se tutte le parti sono abbandonate si tengono tutte. L'ordine è quello del qualificatore «numero nella serie» (P1545).
    internal static string[] ReadRomanPartIds(JsonElement claims, string propertyId)
    {
        var parts = ReadRomanPartStatements(claims, propertyId);
        var current = parts.Where(p => !p.Ended).ToList();
        return [.. (current.Count > 0 ? current : parts).Select(p => p.Id).Distinct(StringComparer.Ordinal)];
    }

    // Parti abbandonate di una parte del nome romano (vedi ReadRomanPartIds).
    internal static string[] ReadEndedRomanPartIds(JsonElement claims, string propertyId)
        => [.. ReadRomanPartStatements(claims, propertyId).Where(p => p.Ended).Select(p => p.Id).Distinct(StringComparer.Ordinal)];

    private static List<(string Id, bool Ended)> ReadRomanPartStatements(JsonElement claims, string propertyId)
    {
        if (claims.ValueKind != JsonValueKind.Object || !claims.TryGetProperty(propertyId, out var statements) || statements.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        static bool Disputed(JsonElement statement)
            => statement.GetPropertyOrDefault("qualifiers").GetPropertyOrDefault("P5102") is { ValueKind: JsonValueKind.Array } natures
                && natures.EnumerateArray().Any(n => GetString(n.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id") == "Q18912752");
        static bool Ended(JsonElement statement)
            => statement.GetPropertyOrDefault("qualifiers") is { ValueKind: JsonValueKind.Object } qualifiers
                && (qualifiers.TryGetProperty("P1534", out _) || qualifiers.TryGetProperty("P582", out _));
        static int Ordinal(JsonElement statement)
            => statement.GetPropertyOrDefault("qualifiers").GetPropertyOrDefault("P1545") is { ValueKind: JsonValueKind.Array } ordinals
                && ordinals.GetArrayLength() > 0
                && int.TryParse(GetString(ordinals[0].GetPropertyOrDefault("datavalue"), "value"), out var n)
                ? n
                : int.MaxValue;

        return [.. statements.EnumerateArray()
            .Where(s => GetString(s, "rank") != "deprecated" && !Disputed(s))
            .Select((s, index) => (Id: GetString(s.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id"), Ended: Ended(s), Order: Ordinal(s), Index: index))
            .Where(p => IsEntityId(p.Id))
            .OrderBy(p => p.Order).ThenBy(p => p.Index)
            .Select(p => (p.Id, p.Ended))];
    }

    // Gens (P5025): quella di nascita e quella acquisita con l'adozione (qualificatore «causa» P828 = adozione).
    // Marco Aurelio è nato nella gens Annia ed è entrato per adozione nella gens Aurelia: conta la gens di nascita.
    private static readonly HashSet<string> AdoptionCauses = new(StringComparer.Ordinal) { "Q5658243", "Q180472", "Q121334179" };

    internal static (string[] Birth, string[] Adoptive) ReadGensIds(JsonElement claims)
    {
        if (claims.ValueKind != JsonValueKind.Object || !claims.TryGetProperty("P5025", out var statements) || statements.ValueKind != JsonValueKind.Array)
        {
            return ([], []);
        }

        var birth = new List<string>();
        var adoptive = new List<string>();
        foreach (var statement in statements.EnumerateArray().Where(s => GetString(s, "rank") != "deprecated"))
        {
            var id = GetString(statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id");
            if (!IsEntityId(id))
            {
                continue;
            }

            var adopted = statement.GetPropertyOrDefault("qualifiers").GetPropertyOrDefault("P828") is { ValueKind: JsonValueKind.Array } causes
                && causes.EnumerateArray().Any(c => AdoptionCauses.Contains(GetString(c.GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id")));
            (adopted ? adoptive : birth).Add(id);
        }

        return ([.. birth.Distinct(StringComparer.Ordinal)], [.. adoptive.Distinct(StringComparer.Ordinal)]);
    }

    // Gens indicate dagli elementi del nomen (P1535 «usato da»): «Annius» → gens Annia.
    internal static string[] GensOfNomina(IEnumerable<string> nomenIds, IReadOnlyDictionary<string, JsonElement> related)
        => [.. nomenIds
            .SelectMany(id => related.TryGetValue(id, out var nomen) ? ReadEntityIds(nomen.GetPropertyOrDefault("claims"), "P1535") : [])
            .Distinct(StringComparer.Ordinal)];

    // Forma latina del nome di un elemento (praenomen, nomen…): latino, poi etichetta multilingue, inglese, italiano.
    private static string LatinLabel(JsonElement entity)
    {
        var values = entity.GetPropertyOrDefault("labels");
        return new[] { "la", "mul", "en", "it" }
            .Select(language => GetString(values.GetPropertyOrDefault(language), "value"))
            .FirstOrDefault(label => label.Length > 0) ?? string.Empty;
    }

    // Parti italiane rimaste in latino perché il nome italiano non si trova scritto (né nel nome, né negli alias, né
    // nell'introduzione) o vuote: si adattano con le regole dell'italiano (Marcus Aelius → Marco Elio, vedi RomanNaming.ItalianForm).
    internal static RomanParts ItalianRomanParts(RomanParts found, RomanParts latin)
    {
        // Una parte vuota (elemento senza etichetta italiana) si ricava dal latino.
        static string Adapt(string part, string latinPart)
            => part.Trim().Length == 0 || string.Equals(part.Trim(), latinPart.Trim(), StringComparison.OrdinalIgnoreCase) ? RomanNaming.ItalianForm(latinPart) : part;
        return new RomanParts(
            Adapt(found.Prenome, latin.Prenome),
            Adapt(found.Gentilizio, latin.Gentilizio),
            Adapt(found.Cognome, latin.Cognome),
            Adapt(found.Agnome, latin.Agnome));
    }

    // Parti del nome romano in una lingua: prenome, nome gentilizio, cognome, agnome.
    internal sealed record RomanParts(string Prenome, string Gentilizio, string Cognome, string Agnome)
    {
        public bool IsEmpty => new[] { Prenome, Gentilizio, Cognome, Agnome }.All(string.IsNullOrWhiteSpace);
    }

    // Parti del nome romano in una lingua. Su Wikidata le etichette dei singoli elementi restano spesso in latino
    // anche nelle altre lingue (in italiano «Gaius», «Iulius», «Caesar»): se il nome della persona in quella lingua
    // ha tante parole quante le parti latine («Gaio Giulio Cesare»), le parti si leggono da lì, nello stesso ordine;
    // altrimenti («Julius Caesar», «Augusto») restano le etichette degli elementi.
    // Se il nome principale è breve («Tito») si provano gli alias Wikidata nella stessa lingua («Tito Flavio Vespasiano»):
    // tra quelli con il numero giusto di parole vince chi somiglia di più alle parti latine o alle etichette
    // (in inglese «Titus Flavius Vespasianus» e non «Titus Caesar Vespasianus»); un alias senza alcuna parola
    // somigliante non vale. Il nome principale, se ha il numero giusto di parole, vale sempre e vince a parità.
    internal static RomanParts LocalRomanParts(string localName, RomanParts latin, RomanParts labels)
        => LocalRomanParts(localName, [], latin, labels);

    // In mancanza di nome o alias adatti, il nome completo si cerca nell'introduzione di Wikipedia in quella lingua
    // (Vespasiano: «Nato Tito Flavio Vespasiano (…)»): una sequenza di parole maiuscole in cui ogni parte somiglia
    // a quella latina. Dal testo libero vale solo una corrispondenza completa.
    internal static RomanParts LocalRomanParts(string localName, IEnumerable<string> aliases, RomanParts latin, RomanParts labels, string? introduction = null)
    {
        static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string[] Parts(RomanParts p) => [p.Prenome, p.Gentilizio, p.Cognome, p.Agnome];
        var counts = Parts(latin).Select(p => Words(p).Length).ToArray();
        var total = counts.Sum();
        if (total == 0)
        {
            return labels;
        }

        string[] Split(string name)
        {
            var words = Words(name);
            var position = 0;
            return [.. counts.Select(count =>
            {
                var part = string.Join(' ', words[position..(position + count)]);
                position += count;
                return part;
            })];
        }

        var references = new[] { Parts(latin), Parts(labels) };
        int Score(string[] parts) => parts
            .Select((part, i) => references.Any(r => SimilarRomanPart(part, r[i])) ? 1 : 0)
            .Sum();

        var best = Words(localName ?? string.Empty).Length == total ? Split(localName!) : null;
        var bestScore = best is null ? 0 : Score(best);
        foreach (var alias in aliases.Where(a => Words(a ?? string.Empty).Length == total))
        {
            var parts = Split(alias);
            var score = Score(parts);
            if (score > bestScore)
            {
                (best, bestScore) = (parts, score);
            }
        }

        // Le etichette già tradotte (in spagnolo «Gayo», «Julio», «César») valgono più del testo libero, che spesso
        // cita il nome latino («en latín: Gaius Iulius Caesar»): l'introduzione serve solo se sono rimaste in latino.
        var required = counts.Count(c => c > 0);
        var labelsAreLatin = Parts(labels).Zip(Parts(latin)).All(p => p.First.Length == 0 || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
        if (bestScore < required && labelsAreLatin)
        {
            foreach (var candidate in IntroductionNames(introduction, total))
            {
                var parts = Split(candidate);
                if (Score(parts) == required)
                {
                    (best, bestScore) = (parts, required);
                    break;
                }
            }
        }

        return best is null ? labels : new RomanParts(best[0], best[1], best[2], best[3]);
    }

    // Sequenze di «words» parole consecutive con l'iniziale maiuscola nell'inizio dell'introduzione.
    internal static IEnumerable<string> IntroductionNames(string? introduction, int words)
    {
        if (string.IsNullOrWhiteSpace(introduction) || words <= 0)
        {
            yield break;
        }

        var text = introduction.Length > 1500 ? introduction[..1500] : introduction;
        foreach (Match run in Regex.Matches(text, @"\p{Lu}\p{L}+(?:[  ]+\p{Lu}\p{L}+)*", RegexOptions.CultureInvariant))
        {
            var runWords = run.Value.Split([' ', ' '], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + words <= runWords.Length; i++)
            {
                yield return string.Join(' ', runWords[i..(i + words)]);
            }
        }
    }

    // Due parti del nome si somigliano se iniziano con le stesse tre lettere, senza accenti, con J = I
    // (Tito/Titus, Flavio/Flavius, Julius/Iulius) e con gli adattamenti italiani più comuni del latino:
    // Gi- iniziale = I- (Giulio/Iulius), ae/oe = e (Cesare/Caesar), ct = tt (Ottaviano/Octavianus),
    // ph = f, x = ss (Massimo/Maximus).
    internal static bool SimilarRomanPart(string a, string b)
    {
        static string Fold(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c == 'j' ? 'i' : c);
                }
            }

            var folded = Regex.Replace(builder.ToString(), "^gi(?=[aeiou])", "i");
            return folded.Replace("ae", "e").Replace("oe", "e").Replace("ct", "tt").Replace("ph", "f").Replace("x", "ss");
        }

        var (x, y) = (Fold(a), Fold(b));
        return x.Length >= 3 && y.Length >= 3 && string.CompareOrdinal(x, 0, y, 0, 3) == 0;
    }

    // Alias Wikidata di un elemento in una lingua.
    private static List<string> ReadAliases(JsonElement entity, string language)
        => entity.GetPropertyOrDefault("aliases").GetPropertyOrDefault(language) is { ValueKind: JsonValueKind.Array } values
            ? [.. values.EnumerateArray().Select(v => GetString(v, "value")).Where(v => v.Length > 0)]
            : [];

    // Aggiunge ai nomi delle altre lingue le parti del nome romano; una lingua senza altri valori viene creata.
    internal static List<LocalizedPersonName> AddRomanParts(List<LocalizedPersonName> names, IEnumerable<(string Code, string Name, RomanParts Parts)> parts)
    {
        foreach (var (code, name, part) in parts.Where(p => !p.Parts.IsEmpty))
        {
            var entry = names.FirstOrDefault(n => n.Lingua == code);
            if (entry is null)
            {
                entry = new LocalizedPersonName { Lingua = code, LinguaNome = name };
                names.Add(entry);
            }

            entry.Prenome = part.Prenome;
            entry.Gentilizio = part.Gentilizio;
            entry.CognomeRomano = part.Cognome;
            entry.Agnome = part.Agnome;
        }

        // Nell'ordine delle lingue d'importazione.
        return [.. names.OrderBy(n => Array.FindIndex(Languages, l => l.Code == n.Lingua))];
    }

    // Nome in latino (P1559 con lingua «la»), tenuto solo se diverso da quello composto dalle parti.
    internal static string ImportedLatinName(IEnumerable<(string Text, string Language)> nativeNames, string composed)
    {
        var latin = nativeNames.FirstOrDefault(n => n.Language == "la").Text?.Trim() ?? string.Empty;
        static string Normalize(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return latin.Length == 0 || string.Equals(Normalize(latin), Normalize(composed), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : latin;
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
                    .ToList())
            {
                ParentIds = [.. ContainingDivisions(p).Select(d => d.WikidataId).Distinct(StringComparer.Ordinal)],
            })
            .ToList();

    // Suddivisioni amministrative (P131) che contengono il luogo, esclusi i Paesi: troppo generici per il confronto.
    private static IEnumerable<PlaceDivision> ContainingDivisions(HistoricalPlaceRecord place)
        => place.AdministrativeDivisions.Where(d => d.WikidataId.Length > 0 && d.Relation.Contains("P131", StringComparison.Ordinal));

    // Una voce per ogni edizione letta, nell'ordine delle lingue (italiano per primo).
    internal static List<WikipediaLeadRecord> BuildWikipediaLeads(
        IReadOnlyDictionary<string, LeadDate> leadDates,
        IReadOnlyList<LeadPlaceCandidate> birthCandidates,
        IReadOnlyList<LeadPlaceCandidate> deathCandidates,
        WikipediaLeadRecord? wikidata = null)
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
                BirthDayAlt = lead.BirthDayAlt,
                DeathYear = lead.DeathYear,
                DeathMonth = lead.DeathMonth,
                DeathDay = lead.DeathDay,
                DeathDayAlt = lead.DeathDayAlt,
                BirthPlace = lead.BirthPlace,
                BirthPlaceKey = WikipediaConsensus.PlaceKey(lead.BirthPlace, birthCandidates.Concat(deathCandidates)),
                DeathPlace = lead.DeathPlace,
                DeathPlaceKey = WikipediaConsensus.PlaceKey(lead.DeathPlace, deathCandidates.Concat(birthCandidates)),
            });
        }

        // Wikidata in fondo: si confronta con la maggioranza delle edizioni ma non la determina.
        if (wikidata is not null && (wikidata.BirthYear is not null || wikidata.DeathYear is not null || wikidata.BirthPlaceKey.Length > 0 || wikidata.DeathPlaceKey.Length > 0))
        {
            leads.Add(wikidata);
        }

        // «Theatre of Pompey, Rome» e «Roma»: il luogo più preciso conta come quello che lo contiene.
        var all = birthCandidates.Concat(deathCandidates).ToList();
        var birthKeys = WikipediaConsensus.GeneralizeKeys([.. leads.Select(l => l.BirthPlaceKey)], all);
        var deathKeys = WikipediaConsensus.GeneralizeKeys([.. leads.Select(l => l.DeathPlaceKey)], all);
        for (var i = 0; i < leads.Count; i++)
        {
            leads[i].BirthPlaceKey = birthKeys[i];
            leads[i].DeathPlaceKey = deathKeys[i];
        }

        return leads;
    }

    // Riga «Wikidata» del confronto tra le edizioni: la data uguale a quella scelta per la scheda, se Wikidata la indica
    // (Traiano ha tre date di morte preferite: 7, 8 e 9 agosto), altrimenti la prima (le preferite prima); il primo luogo
    // di nascita e morte.
    internal static WikipediaLeadRecord BuildWikidataLead(
        string wikidataId,
        IReadOnlyList<HistoricalDateEvidence> births,
        IReadOnlyList<HistoricalDateEvidence> deaths,
        IReadOnlyList<HistoricalPlaceRecord> birthPlaces,
        IReadOnlyList<HistoricalPlaceRecord> deathPlaces,
        HistoricalDateEvidence? chosenBirth = null,
        HistoricalDateEvidence? chosenDeath = null)
    {
        (int? Year, int? Month, int? Day) First(IReadOnlyList<HistoricalDateEvidence> dates, HistoricalDateEvidence? chosen)
        {
            var first = dates.OrderByDescending(d => chosen is not null && d.RawValue == chosen.RawValue && d.Precision == chosen.Precision)
                .ThenByDescending(d => d.Rank == "preferred")
                .FirstOrDefault(d => ParseWikidataYear(d.RawValue) is not null);
            if (first is null)
            {
                return (null, null, null);
            }

            var (month, day) = WikidataMonthDay(first.RawValue, first.Precision);
            return (ParseWikidataYear(first.RawValue), month, day);
        }

        var (birthYear, birthMonth, birthDay) = First(births, chosenBirth);
        var (deathYear, deathMonth, deathDay) = First(deaths, chosenDeath);
        var birthPlace = birthPlaces.FirstOrDefault();
        var deathPlace = deathPlaces.FirstOrDefault();
        return new WikipediaLeadRecord
        {
            Language = WikipediaLeadRecord.WikidataLanguage,
            LanguageName = "Wikidata",
            Title = wikidataId,
            Url = wikidataId.Length > 0 ? $"https://www.wikidata.org/wiki/{Uri.EscapeDataString(wikidataId)}" : string.Empty,
            BirthYear = birthYear,
            BirthMonth = birthMonth,
            BirthDay = birthDay,
            DeathYear = deathYear,
            DeathMonth = deathMonth,
            DeathDay = deathDay,
            BirthPlace = birthPlace?.CurrentName ?? string.Empty,
            BirthPlaceKey = birthPlace?.WikidataId ?? string.Empty,
            DeathPlace = deathPlace?.CurrentName ?? string.Empty,
            DeathPlaceKey = deathPlace?.WikidataId ?? string.Empty,
        };
    }

    // Edizioni lette contemporaneamente: con tutte insieme (due richieste ciascuna) Wikimedia risponde «troppe richieste».
    private const int ParallelEditions = 2;

    // Pausa prima di riprovare le edizioni non lette.
    private static readonly TimeSpan EditionRetryPause = TimeSpan.FromSeconds(3);

    // Scarica l'introduzione di ogni edizione linguistica e ne estrae gli anni di nascita/morte.
    // Le edizioni non lette vengono riprovate una alla volta alla fine; quelle che non si leggono nemmeno così finiscono in Unread
    // (nomi delle lingue), da segnalare all'utente invece di ignorarle in silenzio.
    private static async Task<(Dictionary<string, LeadDate> Leads, List<string> Unread)> ReadLanguageLeadDatesAsync(JsonElement sitelinks, ImportProgressReporter progress, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LeadDate>(StringComparer.Ordinal);
        if (sitelinks.ValueKind != JsonValueKind.Object)
        {
            return (result, []);
        }

        // Le edizioni sono indipendenti: si scaricano in parallelo, poche alla volta.
        using var gate = new SemaphoreSlim(ParallelEditions);
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
            (string Code, LeadDate? Lead) read;
            await gate.WaitAsync(cancellationToken);
            try
            {
                read = await ReadLeadDateAsync(item.Code, item.Title, cancellationToken);
            }
            finally
            {
                gate.Release();
            }

            // Incremento e notifica insieme: le notifiche non possono arrivare fuori ordine.
            lock (progressLock)
            {
                completed++;
                progress.Sub((double)completed / titles.Count, $"{completed} di {titles.Count} edizioni lette");
            }

            return read;
        }).ToList();

        var failed = new List<(string Code, string Title)>();
        foreach (var (code, lead) in await Task.WhenAll(tasks))
        {
            if (lead is not null)
            {
                result[code] = lead;
            }
            else
            {
                failed.Add(titles.First(t => t.Code == code));
            }
        }

        var unread = new List<string>();
        foreach (var (code, title) in failed)
        {
            progress.Sub(1, $"nuovo tentativo: Wikipedia in {LanguageName(code)}");
            await Task.Delay(EditionRetryPause, cancellationToken);
            var (_, lead) = await ReadLeadDateAsync(code, title, cancellationToken);
            if (lead is not null)
            {
                result[code] = lead;
            }
            else
            {
                unread.Add(LanguageName(code));
            }
        }

        return (result, unread);
    }

    private static string LanguageName(string code) => Languages.FirstOrDefault(l => l.Code == code).Name ?? code;

    private static async Task<(string Code, LeadDate? Lead)> ReadLeadDateAsync(string code, string title, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://{code}.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&redirects=1&titles={Uri.EscapeDataString(title)}&format=json&utf8=1";
            using var document = await GetJsonAsync(url, cancellationToken);
            var page = document.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().FirstOrDefault().Value;
            var extract = GetString(page, "extract");
            var info = ExtractLead(extract);
            // L'infobox serve per i periodi di regno e, quando l'introduzione li omette
            // («Charles V (24 February 1500 – 21 September 1558)»), per i luoghi di nascita e morte.
            var wikitext = await ReadInfoboxWikitextAsync(code, title, cancellationToken);
            var (infoboxBirth, infoboxDeath) = ExtractInfoboxPlaces(wikitext);
            if (info.BirthPlace.Length == 0 || info.DeathPlace.Length == 0)
            {
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
                BirthDayAlt = info.BirthDayAlt,
                DeathMonth = info.DeathMonth,
                DeathDay = info.DeathDay,
                DeathDayAlt = info.DeathDayAlt,
                Reigns = InfoboxReignParser.Parse(wikitext),
                Extract = extract,
                InfoboxBirthPlace = infoboxBirth,
                InfoboxDeathPlace = infoboxDeath,
            });
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Edizione non leggibile: si prosegue con le altre.
            return (code, null);
        }
    }

    // Testo della sezione iniziale della voce, dove si trova l'infobox; vuoto se non leggibile.
    private static async Task<string> ReadInfoboxWikitextAsync(string code, string title, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://{code}.wikipedia.org/w/api.php?action=parse&prop=wikitext&section=0&redirects=1&page={Uri.EscapeDataString(title)}&format=json&formatversion=2";
            using var document = await GetJsonAsync(url, cancellationToken);
            return document.RootElement.GetProperty("parse").GetProperty("wikitext").GetString() ?? string.Empty;
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            return string.Empty;
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
            if (match.Success && CleanWikitext(UntilNextParam(match.Groups["value"].Value)) is { Length: >= 2 and <= 120 } place)
            {
                return place;
            }
        }

        return string.Empty;
    }

    // Il valore finisce al primo «|» fuori da link e template: un parametro può seguirne un altro sulla stessa riga
    // («|lieu de naissance=Falacrine Italie| date de décès = …», infobox francese di Vespasiano).
    internal static string UntilNextParam(string value)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var pair = i + 1 < value.Length ? value.Substring(i, 2) : string.Empty;
            if (pair is "[[" or "{{")
            {
                depth++;
                i++;
            }
            else if (pair is "]]" or "}}")
            {
                depth = Math.Max(0, depth - 1);
                i++;
            }
            else if (value[i] == '|' && depth == 0)
            {
                return value[..i];
            }
        }

        return value;
    }

    internal static string CleanWikitext(string value)
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
        public int? BirthDayAlt { get; init; }
        public int? DeathMonth { get; init; }
        public int? DeathDay { get; init; }
        public int? DeathDayAlt { get; init; }
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
        // Una parentesi con un solo anno (es. «na 42 v.Chr.» nel nome latino) cede il posto alla successiva con nascita e morte,
        // purché nella stessa frase: «(Latijn: …; na 42 v.Chr. …) (Rome, 12 of 13 juli 100 v.Chr. – …)».
        LeadInfo? single = null;
        var singleEnd = 0;
        foreach (var (index, end, content) in TopLevelParentheses(lead))
        {
            if (birth.Success && index > birth.Index)
            {
                break;
            }

            if (single is not null && Regex.IsMatch(lead[singleEnd..index], @"[.!?]\s"))
            {
                break;
            }

            var info = ParseLeadParts(Regex.Split(content, @"\s*[–—;]\s*|\s+-\s+|(?<=\d)-(?=\S)|(?<=\.)-(?=\s?\d)"));
            if (info.BirthYear is not null && info.DeathYear is not null)
            {
                return info;
            }

            if (single is null && (info.BirthYear is not null || info.DeathYear is not null))
            {
                single = info;
                singleEnd = end;
            }
        }

        if (single is not null)
        {
            return single;
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

    // Parentesi di primo livello con il loro contenuto; quelle annidate restano nel testo senza i segni di parentesi:
    // «Idus van maart (15 maart) 44 v.Chr.» → «Idus van maart 15 maart 44 v.Chr.». Le parentesi lunghe (pronuncia,
    // nome latino e greco prima delle date, come in «Gaio Giulio Cesare») sono ammesse fino a 700 caratteri.
    internal static IEnumerable<(int Start, int End, string Content)> TopLevelParentheses(string text)
    {
        var depth = 0;
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                if (depth++ == 0)
                {
                    start = i;
                }
            }
            else if (text[i] == ')' && depth > 0 && --depth == 0)
            {
                var content = text[(start + 1)..i];
                if (content.Length <= 700)
                {
                    yield return (start, i + 1, Regex.Replace(content, @"\s*[()]\s*", " ").Trim());
                }
            }
        }
    }

    // Il punto di «av. J.-C.» non chiude la frase. Il nome di nascita può stare in mezzo:
    // «né sous le nom de Marcus Ulpius Traianus le 18 septembre 53 à Italica».
    private static readonly Regex FrenchBirthSentence = new(
        @"\bnée?\s+(?:sous\s+le\s+nom\s+(?:de\s+|d['’])[^,;()]{1,80}?\s+)?(?<seg>(?:le|en|vers|à)\b.{0,160}?)(?=\s+et\s+(?:\p{L}+\s+)?morte?\b|\s*,\s*(?:et\s+)?morte?\b|\s*,\s*(?:est|était|fut|a\s+été)\b|\s*;\s|(?<!\b(?:av|apr|env|J\.-C|J))\.\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // «mort le …», ma anche «mort assassiné le …».
    private static readonly Regex FrenchDeathSentence = new(
        @"\bmorte?\s+(?:(?!(?:le|en|vers|à)\b)\p{L}+\s+){0,2}(?<seg>(?:le|en|vers|à)\b.{0,160}?)(?=\s*,\s|\s*;|$)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // Luogo dopo la data, introdotto da una preposizione: «in Eisenach», «à Amboise», «en Roma», «te Delft».
    // Le virgole restano nel luogo: in «im Prinzenhof, Gent, Burgundische Niederlande» la città viene dopo l'edificio.
    // Anche un luogo incerto: «53, vielleicht Italica, in Hispania Baetica oder in Rom» → «Italica, …».
    private static readonly Regex PlaceAfterDate = new(
        @"(?:^|\s)(?:in|im|bei|à|a|en|te|at|near|vielleicht|wahrscheinlich|vermutlich|wohl|probablement|probably|possibly|forse|probabilmente)\s+(?<place>\p{Lu}[^;()\[\]\d.]*)",
        RegexOptions.CultureInvariant);

    // «ebenda», «ibidem», «ivi»: la morte è avvenuta nello stesso luogo della nascita.
    private static readonly Regex SamePlaceMarker = new(@"\b(ebenda|ibidem|ibid|ivi|ídem|aldaar|ibídem|même\s+(?:ville|lieu)|same\s+(?:city|place))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        // «(* 18. September 53, …; † 8. August 117 in Selinus)»: se la nascita non si legge, la parte con «†» resta la morte
        // (prima diventava la nascita: Traiano risultava nato nel 117).
        if (IsDeathPart(parts[0].Text))
        {
            var only = parts[0].Parsed;
            var (onlyMonth, onlyDay) = ParseDayMonth(parts[0].Text[..only.Start]);
            return new LeadInfo(null, Signed(only.Year!.Value, only.BeforeChrist), string.Empty, LeadPlace(parts[0].Text, only.End))
            {
                DeathMonth = onlyMonth,
                DeathDay = onlyDay,
                DeathDayAlt = AlternativeDay(parts[0].Text[..only.Start], onlyDay),
            };
        }

        var birth = parts[0].Parsed;
        var birthPlace = LeadPlace(parts[0].Text, birth.End);
        var (birthMonth, birthDay) = ParseDayMonth(parts[0].Text[..birth.Start]);
        var birthDayAlt = AlternativeDay(parts[0].Text[..birth.Start], birthDay);
        if (parts.Count == 1)
        {
            return new LeadInfo(Signed(birth.Year!.Value, birth.BeforeChrist), null, birthPlace, string.Empty) { BirthMonth = birthMonth, BirthDay = birthDay, BirthDayAlt = birthDayAlt };
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
            BirthDayAlt = birthDayAlt,
            DeathMonth = deathMonth,
            DeathDay = deathDay,
            DeathDayAlt = AlternativeDay(parts[1].Text[..death.Start], deathDay),
        };
    }

    private static bool IsDeathPart(string part) => part.TrimStart(' ', ' ').StartsWith('†') || part.TrimStart(' ', ' ').StartsWith('✝');

    // «8 ou 9 août», «12 of 13 juli», «7 o 8 agosto», «7 or 8 August», «7. oder 8. August»: l'altro giorno indicato,
    // oltre a quello accanto al mese (letto da ParseDayMonth). null se il testo indica un solo giorno.
    private static readonly Regex AlternativeDayPattern = new(
        @"(?<!\d)(?<alt>\d{1,2})(?:\.|º|°|er)?\s+(?:o|ou|or|oder|of|y|e|u)\s+(?<day>\d{1,2})(?:\.|º|°|ª|er|st|nd|rd|th)?\s*(?:de\s+)?\p{L}+\.?\s*(?:de|del|of)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static int? AlternativeDay(string textBeforeYear, int? day)
    {
        var text = (textBeforeYear ?? string.Empty).TrimEnd(' ', ' ', '±', '~', '≈');
        var match = AlternativeDayPattern.Match(text);
        return day is int d && match.Success
            && int.TryParse(match.Groups["day"].Value, out var parsed) && parsed == d
            && int.TryParse(match.Groups["alt"].Value, out var alt) && alt is >= 1 and <= 31 && alt != d
            ? alt
            : null;
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

    // Anno di una o due cifre senza «a.C.»/«d.C.»: vale solo subito dopo un mese («30 dicembre 39», «December AD 39»).
    // Può essere seguito da una virgola e da altro testo («18. September 53, vielleicht Italica»), non da un'altra cifra.
    private static readonly Regex ShortYear = new(@"(?<!\d)(?<year>\d{1,2})(?![\d.:/]|,\d)", RegexOptions.CultureInvariant);

    private static Match ShortYearAfterMonth(string part)
    {
        for (var match = ShortYear.Match(part); match.Success; match = match.NextMatch())
        {
            if (ParseDayMonth(part[..match.Index]).Month is not null)
            {
                return match;
            }
        }

        return Match.Empty;
    }

    private static (int? Year, bool BeforeChrist, bool AfterChrist, int Start, int End) ParseLeadPart(string part)
    {
        var yearMatch = LeadYear.Match(part);
        if (!yearMatch.Success)
        {
            yearMatch = ShortYearAfterMonth(part);
        }

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
        // «13 juli ± 100»: il segno di data approssimata tra mese e anno non conta.
        var text = (textBeforeYear ?? string.Empty).TrimEnd(' ', ' ', '±', '~', '≈');
        // «30 December AD 39»: anche «AD» tra mese e anno non conta.
        text = Regex.Replace(text, @"\s+AD\s*$", string.Empty, RegexOptions.CultureInvariant);
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

    // Combina le date di Wikidata con quelle lette dalle varie Wikipedia. Ogni data candidata (il giorno, o il mese, o
    // l'anno se nessuna fonte è più precisa) conta le sue fonti: ogni edizione di Wikipedia vale una fonte, ogni data
    // Wikidata tante fonti quanti i suoi riferimenti (almeno una). Vince la data con più fonti (a parità quella letta da
    // più edizioni, poi Wikidata); una data che solo Wikipedia indica deve essere letta da almeno due edizioni. Le altre
    // date indicate da almeno due fonti restano come ipotesi («morto il 7 o l'8 agosto»: Traiano, 8, 9 e 7 agosto 117);
    // quelle con una sola fonte sono scartate: non entrano nel campo, ma restano tra le fonti come date discordanti
    // (Cesare: 102 a.C. da un solo riferimento Wikidata, contro il 100 a.C. di tutte le altre fonti). Un'edizione che
    // indica due giorni («le 8 ou 9 août») vale per entrambi; le fonti meno precise (solo l'anno o solo il mese)
    // sostengono le date compatibili e ne diventano fonti, ma non decidono tra un giorno e l'altro.
    private static (List<HistoricalDateEvidence> Kept, List<HistoricalDateEvidence> Discarded) BuildCrossLanguageDates(
        List<HistoricalDateEvidence> wikidataEvidence,
        Dictionary<string, LeadDate> leadDates,
        bool birth)
    {
        var readings = new List<DateReading>();
        foreach (var e in wikidataEvidence)
        {
            if (ParseWikidataYear(e.RawValue) is not int year)
            {
                continue;
            }

            var (month, day) = WikidataMonthDay(e.RawValue, e.Precision);
            readings.Add(new DateReading(year, month, day, null, e, null));
        }

        foreach (var (code, name, _, _) in Languages)
        {
            if (leadDates.TryGetValue(code, out var lead) && (birth ? lead.BirthYear : lead.DeathYear) is int year)
            {
                var month = birth ? lead.BirthMonth : lead.DeathMonth;
                var day = month is null ? null : birth ? lead.BirthDay : lead.DeathDay;
                var alt = day is null ? null : birth ? lead.BirthDayAlt : lead.DeathDayAlt;
                readings.Add(new DateReading(year, month, day, alt, null, new SourceCitation { Title = $"Wikipedia in {name}", Url = lead.Url }));
            }
        }

        var years = new List<(List<Candidate> Kept, List<Candidate> Discarded, int Score)>();
        foreach (var year in readings.Select(r => r.Year).Distinct())
        {
            var inYear = readings.Where(r => r.Year == year).ToList();

            // Date candidate: i giorni indicati (anche il secondo giorno di «8 ou 9»), poi i mesi senza giorni indicati.
            var keys = inYear.Where(r => r.Month is not null && r.Day is not null).Select(r => (r.Month, r.Day))
                .Concat(inYear.Where(r => r.Month is not null && r.AltDay is not null).Select(r => (r.Month, Day: r.AltDay)))
                .Distinct()
                .ToList();
            keys.AddRange(inYear.Where(r => r.Month is not null && keys.All(k => k.Month != r.Month)).Select(r => (r.Month, Day: (int?)null)).Distinct());
            var candidates = keys
                .Select(k =>
                {
                    var exact = inYear.Where(r => r.Month == k.Month && (k.Day is null ? r.Day is null : r.Day == k.Day || r.AltDay == k.Day)).ToList();
                    return new Candidate(year, k.Month, k.Day, exact, inYear.IndexOf(exact[0]));
                })
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Exact.Count)
                .ThenByDescending(c => c.Exact.Any(r => r.Wikidata is not null))
                .ThenBy(c => c.First)
                .ToList();

            var winner = candidates.FirstOrDefault(c => c.Exact.Any(r => r.Wikidata is not null) || c.Exact.Count >= 2);
            List<Candidate> kept;
            if (winner is not null)
            {
                kept = [winner, .. candidates.Where(c => c != winner && c.Score >= 2)];
            }
            else
            {
                // Nessun giorno (o mese) abbastanza confermato: l'anno, con il valore Wikidata più preciso se c'è.
                var representative = inYear.Where(r => r.Wikidata is not null).Select(r => r.Wikidata!).OrderByDescending(e => PrecisionRank(e.Precision)).FirstOrDefault();
                var (month, day) = representative is null ? (null, null) : WikidataMonthDay(representative.RawValue, representative.Precision);
                var exact = inYear.Where(r => r.Month == month && r.Day == day).ToList();
                kept = [new Candidate(year, month, day, exact, 0) { Representative = representative }];
            }

            var discarded = candidates.Where(c => !kept.Any(k => k.Month == c.Month && k.Day == c.Day)).ToList();
            foreach (var candidate in kept)
            {
                candidate.Compatible = [.. inYear.Where(r => (r.Month is null || candidate.Month is null || r.Month == candidate.Month)
                    && (r.Day is null || candidate.Day is null || r.Day == candidate.Day || r.AltDay == candidate.Day))];
            }

            years.Add((kept, discarded, SourceCount(kept[0].Compatible)));
        }

        // Tra un anno e l'altro vince quello con più fonti; un anno diverso resta solo se lo indicano almeno due fonti.
        var ordered = years.OrderByDescending(y => y.Score).ThenByDescending(y => y.Kept[0].Compatible.Count).ToList();
        var keptEvidence = new List<HistoricalDateEvidence>();
        var discardedEvidence = new List<HistoricalDateEvidence>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (kept, discarded, score) = ordered[i];
            if (i > 0 && score < 2)
            {
                discarded = [.. kept, .. discarded];
                kept = [];
            }

            var wikidataOfYear = readings.Where(r => r.Year == ordered[i].Kept[0].Year && r.Wikidata is not null).Select(r => r.Wikidata!).ToList();
            keptEvidence.AddRange(kept.Select(c => ToEvidence(c, c.Compatible, wikidataOfYear)));
            discardedEvidence.AddRange(discarded.Select(c =>
            {
                var evidence = ToEvidence(c, c.Exact, wikidataOfYear);
                evidence.Scartata = true;
                return evidence;
            }));
        }

        return (keptEvidence, discardedEvidence);
    }

    private sealed record DateReading(int Year, int? Month, int? Day, int? AltDay, HistoricalDateEvidence? Wikidata, SourceCitation? Wikipedia)
    {
        // Ogni edizione di Wikipedia è una fonte; una data Wikidata vale quanti sono i suoi riferimenti (almeno uno).
        public int SourceCount => Wikidata is null ? 1 : Math.Max(1, Wikidata.Sources.Count);
    }

    // Data candidata con le letture che la indicano esattamente; Compatible: anche quelle meno precise che la sostengono.
    private sealed record Candidate(int Year, int? Month, int? Day, List<DateReading> Exact, int First)
    {
        public int Score => SourceCount(Exact);

        public List<DateReading> Compatible { get; set; } = Exact;

        // Data Wikidata da usare quando nessun giorno è abbastanza confermato (la più precisa dell'anno).
        public HistoricalDateEvidence? Representative { get; init; }
    }

    private static int SourceCount(IEnumerable<DateReading> readings) => readings.Sum(r => r.SourceCount);

    // La data Wikidata che indica proprio questa data (la preferita, con più riferimenti), altrimenti una data costruita
    // dalle edizioni. Le fonti sono quelle delle letture indicate.
    private static HistoricalDateEvidence ToEvidence(Candidate candidate, IEnumerable<DateReading> sourceReadings, IReadOnlyList<HistoricalDateEvidence> wikidataOfYear)
    {
        var chosen = candidate.Representative ?? candidate.Exact
            .Where(r => r.Wikidata is not null && r.Day == candidate.Day)
            .Select(r => r.Wikidata!)
            .OrderByDescending(e => e.Rank == "preferred")
            .ThenByDescending(e => e.Sources.Count)
            .FirstOrDefault();
        var year = candidate.Year;
        var sign = year < 0 ? "-" : "+";
        var evidence = chosen is not null
            ? CopyEvidence(chosen)
            : candidate.Month is int month
                ? new HistoricalDateEvidence
                {
                    Value = DateFormats.Format(year, month, candidate.Day),
                    RawValue = $"{sign}{Math.Abs(year):0000}-{month:00}-{candidate.Day ?? 0:00}T00:00:00Z",
                    Precision = candidate.Day is null ? "mese" : "giorno",
                    Rank = wikidataOfYear.Select(e => e.Rank).FirstOrDefault() ?? string.Empty,
                    Calendario = wikidataOfYear.Select(e => e.Calendario).FirstOrDefault(c => c.Length > 0) ?? string.Empty,
                }
                : new HistoricalDateEvidence
                {
                    Value = FormatYear(Math.Abs(year), year < 0),
                    RawValue = $"{sign}{Math.Abs(year):0000}-00-00T00:00:00Z",
                    Precision = "anno",
                };
        if (evidence.Precision.Length == 0)
        {
            evidence.Precision = "anno";
        }

        foreach (var reading in sourceReadings)
        {
            foreach (var source in reading.Wikidata?.Sources ?? new List<SourceCitation> { reading.Wikipedia! })
            {
                if (!evidence.Sources.Any(s => s.Title == source.Title && s.Url == source.Url && s.WikidataId == source.WikidataId))
                {
                    evidence.Sources.Add(source);
                }
            }
        }

        return evidence;
    }

    // Mese e giorno di una data Wikidata, secondo la precisione («giorno», «mese»); altrimenti nessuno dei due.
    private static (int? Month, int? Day) WikidataMonthDay(string rawValue, string precision)
    {
        var match = Regex.Match(rawValue, @"^[+-]\d+-(?<m>\d{2})-(?<d>\d{2})");
        if (!match.Success || precision is not ("giorno" or "mese"))
        {
            return (null, null);
        }

        var month = int.Parse(match.Groups["m"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return month is < 1 or > 12 ? (null, null) : (month, precision == "giorno" && day is >= 1 and <= 31 ? day : null);
    }

    // Copia senza fonti: le fonti si aggiungono poi, solo quelle compatibili con la data scelta.
    private static HistoricalDateEvidence CopyEvidence(HistoricalDateEvidence source) => new()
    {
        Value = source.Value,
        RawValue = source.RawValue,
        Precision = source.Precision,
        Rank = source.Rank,
        Calendario = source.Calendario,
        Qualifiers = source.Qualifiers,
    };

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

    // Data Wikidata nel formato delle date in uso («23/06/0079», «15/03/44 a.C.»), «anni 1450», «XV secolo».
    internal static string FormatDate(string rawValue, int precision)
    {
        var match = Regex.Match(rawValue, @"^(?<sign>[+-])(?<year>\d+)-(?<month>\d{2})-(?<day>\d{2})");
        if (!match.Success || !int.TryParse(match.Groups["year"].Value, out var year) || year == 0)
        {
            return rawValue;
        }

        var beforeChrist = match.Groups["sign"].Value == "-";
        var era = beforeChrist ? " a.C." : string.Empty;
        var signed = beforeChrist ? -year : year;
        var month = int.Parse(match.Groups["month"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["day"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return precision switch
        {
            11 => DateFormats.Format(signed, month, day),
            10 => DateFormats.Format(signed, month),
            8 => $"anni {year / 10 * 10}{era}",
            7 => $"{RomanNumeral((year - 1) / 100 + 1)} secolo{era}",
            6 => $"{RomanNumeral((year - 1) / 1000 + 1)} millennio{era}",
            _ => DateFormats.FormatYear(signed),
        };
    }

    // Anno nel formato delle date: quattro cifre dopo Cristo («0079»), «44 a.C.» prima.
    internal static string FormatYear(int year, bool beforeChrist) => DateFormats.FormatYear(beforeChrist ? -year : year);

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

     // Solo per i test di import: risposte registrate su file al posto della rete (vedi ImportFixtures nei test).
     // AsyncLocal: vale solo per l'import avviato dal test, non per gli altri test in parallelo.
     internal interface IRecordedResponses
     {
          // Risposta registrata per l'URL, oppure null se va scaricata (e poi passata a Save).
          string? Find(string url);
          void Save(string url, string json);
     }

     internal static readonly AsyncLocal<IRecordedResponses?> RecordedResponses = new();

     // Nuovi tentativi e segnalazione delle fasi (coda, richiesta, attesa) in HttpRetry.
     private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
     {
          var recorded = RecordedResponses.Value;
          if (recorded?.Find(url) is { } saved)
          {
                return JsonDocument.Parse(saved);
          }

          static async Task<string> Read(HttpResponseMessage response, CancellationToken token)
                => await response.Content.ReadAsStringAsync(token);

          var json = await (url.StartsWith(WikidataApi, StringComparison.Ordinal)
                ? HttpRetry.GetAsync(url, (report, token) => SendToWikidataAsync(url, report, token), Read, cancellationToken)
                : HttpRetry.GetAsync(Client, url, Read, cancellationToken));
          var document = JsonDocument.Parse(json);
          recorded?.Save(url, json);
          return document;
     }

     // Richieste a Wikidata una alla volta, distanziate di un breve intervallo.
     private static async Task<HttpResponseMessage> SendToWikidataAsync(string url, Action<NetworkPhase> report, CancellationToken cancellationToken)
     {
          report(NetworkPhase.Queued);
          await WikidataGate.WaitAsync(cancellationToken);
          try
          {
                var elapsed = DateTime.UtcNow - lastWikidataRequest;
                if (elapsed < WikidataInterval)
                {
                     await Task.Delay(WikidataInterval - elapsed, cancellationToken);
                }

                report(NetworkPhase.Requesting);
                var response = await Client.GetAsync(url, cancellationToken);
                lastWikidataRequest = DateTime.UtcNow;
                return response;
          }
          finally
          {
                WikidataGate.Release();
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
