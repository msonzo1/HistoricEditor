using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Luogo storico o luogo specifico? (regola concordata il 2026-11-12, vedi DECISIONI.md, «Luoghi»)
//  - LUOGO STORICO = luogo che non esiste più: una località antica o scomparsa (Falacrine, Aquae Cutiliae, Italica,
//    Selinunte in Cilicia) o un edificio distrutto (Teatro di Pompeo, «ex edificio» su Wikidata). Sta dentro il luogo
//    attuale che lo contiene: di solito il comune, ma anche una provincia o una regione se il comune non si conosce.
//  - LUOGO SPECIFICO = luogo che esiste ancora, anche solo come rovine: un edificio in rovina (Tempio del Divo Giulio)
//    diventa un luogo specifico di tipo «Sito archeologico», in rovina; i resti di una località antica (Terme di
//    Cotilia, Sito archeologico di Italica) diventano un luogo specifico nel luogo attuale che li contiene, con la nota
//    «Resti di …» (2026-11-16: prima stavano dentro il luogo storico). Una città antica «sostituita da» (P1366) una città
//    di oggi non ha resti visibili: stanno sotto la città moderna (Antiochia di Siria sotto Antiochia).
// I nomi si cercano su Wikidata (etichette e alias in italiano e latino) e nelle voci di Wikipedia della persona
// (introduzione e infobox in italiano), perché spesso Wikidata non li ha: Falacrine ha solo etichette straniere.
public sealed partial class WikipediaImportService
{
    // Stati dell'antica Roma: un luogo che li indica come Paese (P17) faceva parte del mondo romano.
    private static readonly HashSet<string> RomanStates = new(StringComparer.Ordinal)
    {
        "Q1747689", // antica Roma
        "Q2277", // Impero romano
        "Q17167", // Repubblica romana
        "Q201038", // Regno di Roma
        "Q42834", // Impero romano d'Occidente
    };

    // Città antiche: hanno un nome latino anche quando Wikidata indica come Paese solo lo Stato di oggi (Antium, «Anzio»
    // in italiano, ha come Paese l'Italia e l'etichetta latina «Antium», 2026-11-17).
    private static readonly HashSet<string> AncientCityTypes = new(StringComparer.Ordinal)
    {
        "Q15661340", // città antica
        "Q2202509", // città romana
    };

    // Insediamenti e siti (non edifici: un teatro o una villa restano luoghi specifici).
    private static readonly HashSet<string> SettlementTypes = new(StringComparer.Ordinal)
    {
        "Q532", // villaggio
        "Q486972", // insediamento umano
        "Q3957", // città (town)
        "Q515", // città
        "Q5084", // borgo
        "Q839954", // sito archeologico
        "Q15661340", // città antica
        "Q350895", // villaggio abbandonato
        "Q74047", // città fantasma
        "Q125866553", // sito archeologico egizio (Pelusio)
    };

    // Regioni e province che non esistono più (2026-11-14, da Pompeo): luoghi storici dentro lo Stato di oggi indicato da
    // Wikidata (Regio V Picenum → Italia), con il nome italiano del tipo su Wikidata («Regioni dell'Italia augustea»,
    // 2026-11-15): il tipo si corregge poi nella sezione Luoghi. Regione dell'Italia augustea, provincia romana, regione storica.
    internal static readonly HashSet<string> HistoricRegionTypes = new(StringComparer.Ordinal) { "Q15066925", "Q182547", "Q1620908" };

    // Tipi che bastano da soli a dire che il luogo non esiste più come luogo abitato.
    private static readonly HashSet<string> AncientTypes = new(StringComparer.Ordinal) { "Q839954", "Q15661340", "Q350895", "Q74047", "Q125866553" };

    // Resti ancora visibili: sito archeologico, sito archeologico romano, rovine, rovine d'epoca romana.
    private static readonly HashSet<string> RuinsTypes = new(StringComparer.Ordinal) { "Q839954", "Q21752084", "Q109607", "Q133444874", "Q125866553" };

    // Edifici che non esistono più: «ex edificio o struttura», chiesa distrutta, sinagoga demolita, ex ponte.
    private static readonly HashSet<string> DestroyedTypes = new(StringComparer.Ordinal) { "Q19860854", "Q57644089", "Q96378286", "Q11486300" };

    // Stato di conservazione (P5816): «demolito o distrutto» = scomparso; «in rovina» = esiste come rovine;
    // «in rovina» o «in buono stato» per una località antica = ne restano i resti.
    private static readonly HashSet<string> DestroyedStates = new(StringComparer.Ordinal) { "Q56556915" };
    private const string RuinousState = "Q56557159";
    private static readonly HashSet<string> RemainsStates = new(StringComparer.Ordinal) { RuinousState, "Q56557591" };

    // Tipo dato ai resti ancora visibili di un luogo antico o di un edificio in rovina.
    internal const string ArchaeologicalSiteType = "Sito archeologico";

    internal enum PlaceSurvival
    {
        // Esiste ancora (o non si sa): luogo amministrativo o luogo specifico come prima.
        Existing,

        // Località antica o scomparsa: luogo storico.
        AncientSettlement,

        // Edificio che non esiste più: luogo storico con il proprio nome.
        DestroyedBuilding,

        // Edificio o sito in rovina: luogo specifico di tipo «Sito archeologico», in rovina.
        Ruins,
    }

    // Tutti i tipi (P31) non deprecati, anche quelli con rango normale accanto a un preferito: Italica è «sito
    // archeologico» (preferito) ma anche «rovine» e «città antica».
    private static HashSet<string> AllTypes(JsonElement claims, string propertyId = "P31")
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (claims.ValueKind == JsonValueKind.Object && claims.TryGetProperty(propertyId, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var statement in values.EnumerateArray())
            {
                if (GetString(statement, "rank") == "deprecated")
                {
                    continue;
                }

                var id = GetString(statement.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"), "id");
                if (IsEntityId(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    // Classifica un luogo importato. Le unità amministrative attuali (comuni, province…) esistono sempre.
    internal static PlaceSurvival ClassifySurvival(JsonElement entity, string country)
    {
        if (IsAncientSettlement(entity, country))
        {
            return PlaceSurvival.AncientSettlement;
        }

        var claims = entity.GetPropertyOrDefault("claims");
        var types = AllTypes(claims);
        if (types.Count == 0 || (GeoSchemas.LevelTypes(country) is { } levels && levels.Values.Any(set => set.Overlaps(types))))
        {
            return PlaceSurvival.Existing;
        }

        var states = AllTypes(claims, "P5816");
        if (types.Overlaps(DestroyedTypes) || states.Overlaps(DestroyedStates))
        {
            return PlaceSurvival.DestroyedBuilding;
        }

        // Solo lo stato «in rovina» rende un edificio un sito archeologico: il Mausoleo di Augusto (restaurato) o la
        // Colonna Traiana (intatta) sono anche «siti archeologici» su Wikidata, ma restano mausoleo e colonna.
        return states.Contains(RuinousState) ? PlaceSurvival.Ruins : PlaceSurvival.Existing;
    }

    // Insediamento antico: un villaggio o un sito (tutti i tipi Wikidata sono di insediamento o di sito: il Mausoleo di
    // Augusto è anche sito archeologico ma resta un edificio) che non è un'unità amministrativa attuale (Roma indica anche l'antica
    // Roma tra i Paesi, ma è un comune) e che appartiene a uno Stato romano, ha un tipo «antico» o è stato abbandonato (P576).
    internal static bool IsAncientSettlement(JsonElement entity, string country)
    {
        var claims = entity.GetPropertyOrDefault("claims");
        var types = ReadEntityIds(claims, "P31").ToHashSet(StringComparer.Ordinal);
        if (types.Overlaps(HistoricRegionTypes) && !(GeoSchemas.LevelTypes(country) is { } regionLevels && regionLevels.Values.Any(set => set.Overlaps(types))))
        {
            // Regione o provincia antica (Regio V Picenum): non esiste più come unità. Una regione di oggi che Wikidata
            // indica anche come «regione storica» resta un'unità amministrativa.
            return true;
        }

        if (types.Count == 0 || !types.IsSubsetOf(SettlementTypes)
            || (GeoSchemas.LevelTypes(country) is { } levels && levels.Values.Any(set => set.Overlaps(types))))
        {
            return false;
        }

        return types.Overlaps(AncientTypes)
            || ReadEntityIds(claims, "P17").Any(RomanStates.Contains)
            || claims.TryGetProperty("P576", out _);
    }

    // Regione o provincia antica (Regio V Picenum).
    internal static bool IsHistoricRegion(JsonElement entity)
        => ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P31").Any(HistoricRegionTypes.Contains);

    internal static bool IsRomanPlace(JsonElement entity)
        => ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P17").Any(RomanStates.Contains)
            || ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P31").Any(AncientCityTypes.Contains);

    // Una località antica di cui restano le rovine (sito archeologico, rovine) ha un luogo specifico per i resti. Non se
    // Wikidata la indica «sostituita da» (P1366) un altro luogo: Antiochia di Siria è un «sito archeologico», ma la città
    // antica sta sotto quella moderna e non c'è un sito da visitare (2026-11-16).
    internal static bool HasVisibleRemains(JsonElement entity)
    {
        var claims = entity.GetPropertyOrDefault("claims");
        return (AllTypes(claims).Overlaps(RuinsTypes) || AllTypes(claims, "P5816").Overlaps(RemainsStates))
            && AllTypes(claims, "P1366").Count == 0;
    }

    // Decide luogo storico / luogo specifico e dà i nomi italiano e latino ai luoghi storici; una richiesta per le
    // etichette e gli alias in italiano e latino, solo se ci sono luoghi storici.
    private static async Task AssignHistoricPlaceNamesAsync(
        IReadOnlyList<HistoricalPlaceRecord> birthPlaces,
        IReadOnlyList<HistoricalPlaceRecord> deathPlaces,
        IReadOnlyList<HistoricalPlaceRecord> burialPlaces,
        IReadOnlyDictionary<string, JsonElement> entities,
        IReadOnlyDictionary<string, LeadDate> leadDates,
        IReadOnlyDictionary<string, string> typeLabels,
        CancellationToken cancellationToken)
    {
        var all = birthPlaces.Concat(deathPlaces).Concat(burialPlaces).ToList();
        var survival = all
            .Where(p => entities.ContainsKey(p.WikidataId))
            .DistinctBy(p => p.WikidataId, StringComparer.Ordinal)
            .ToDictionary(p => p.WikidataId, p => ClassifySurvival(entities[p.WikidataId], p.CurrentCountry), StringComparer.Ordinal);

        // Edifici in rovina: luoghi specifici di tipo «Sito archeologico», in rovina (Tempio del Divo Giulio).
        foreach (var place in all.Where(p => survival.GetValueOrDefault(p.WikidataId) == PlaceSurvival.Ruins))
        {
            place.TypeLabel = ArchaeologicalSiteType;
            place.SpecificCondition = CondizioniLuogo.InRovina;
        }

        var historic = survival.Where(s => s.Value is PlaceSurvival.AncientSettlement or PlaceSurvival.DestroyedBuilding).Select(s => s.Key).ToList();
        if (historic.Count == 0)
        {
            return;
        }

        var names = new Dictionary<string, (Dictionary<string, string> Labels, Dictionary<string, List<string>> Aliases)>(StringComparer.Ordinal);
        try
        {
            using var document = await GetJsonAsync(
                $"{WikidataApi}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join('|', historic))}&props=labels%7Caliases&languages=it%7Cla%7Cen%7Cde%7Cfr%7Ces%7Cnl&format=json",
                cancellationToken);
            foreach (var id in historic)
            {
                if (ApiProperty(document.RootElement, "entities").TryGetProperty(id, out var entity))
                {
                    // L'etichetta latina è chiesta apposta: ReadLabels legge solo le lingue delle edizioni, e il latino
                    // restava fuori (Antium, 2026-11-17).
                    var labels = ReadLabels(entity);
                    if (GetString(entity.GetPropertyOrDefault("labels").GetPropertyOrDefault("la"), "value") is { Length: > 0 } latinLabel)
                    {
                        labels["la"] = latinLabel;
                    }

                    names[id] = (labels, ReadAliases(entity));
                }
            }
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Senza etichette restano i testi delle voci di Wikipedia e il nome già letto.
        }

        var italianLead = leadDates.GetValueOrDefault("it");
        foreach (var (places, role) in new[] { (birthPlaces, 0), (deathPlaces, 1), (burialPlaces, 2) })
        {
            for (var i = 0; i < places.Count; i++)
            {
                var place = places[i];
                if (!historic.Contains(place.WikidataId))
                {
                    continue;
                }

                var entity = entities[place.WikidataId];
                var (labels, aliases) = names.TryGetValue(place.WikidataId, out var found) ? found : (new Dictionary<string, string>(), new Dictionary<string, List<string>>());
                var roman = IsRomanPlace(entity);
                if (survival[place.WikidataId] == PlaceSurvival.DestroyedBuilding)
                {
                    // Edificio scomparso: luogo storico con il suo nome («Teatro di Pompeo») e il tipo di edificio
                    // («Teatro romano»), senza i tipi generici (sito archeologico, ex edificio).
                    var italianName = labels.GetValueOrDefault("it", string.Empty).Trim();
                    place.HistoricName = italianName.Length > 0 ? italianName : place.CurrentName.Trim();
                    var latin = roman ? labels.GetValueOrDefault("la", string.Empty).Trim() : string.Empty;
                    place.LatinName = string.Equals(latin, place.HistoricName, StringComparison.OrdinalIgnoreCase) ? string.Empty : latin;
                    var buildingType = SpecificPlaceTypes.Describe(place.TypeIds.Where(t => !RuinsTypes.Contains(t) && !DestroyedTypes.Contains(t)), typeLabels);
                    if (buildingType.Length > 0)
                    {
                        place.TypeLabel = buildingType;
                    }

                    continue;
                }

                // I testi della voce italiana valgono solo per il primo luogo del ruolo (quello che nominano).
                var introText = i == 0 && italianLead is not null ? role switch { 0 => italianLead.BirthPlace, 1 => italianLead.DeathPlace, _ => string.Empty } : string.Empty;
                var infoboxText = i == 0 && italianLead is not null ? role switch { 0 => italianLead.InfoboxBirthPlace, 1 => italianLead.InfoboxDeathPlace, _ => string.Empty } : string.Empty;
                var chosen = ChooseHistoricNames(labels, aliases, MainPart(introText), MainPart(infoboxText), roman, place.CurrentName);
                (place.HistoricName, place.LatinName) = (chosen.Italian, chosen.Latin);
                if (IsHistoricRegion(entity))
                {
                    // Una regione non ha resti né un comune: solo il suo tipo, con il nome italiano di Wikidata.
                    place.TypeLabel = SpecificPlaceTypes.Describe(place.TypeIds.Where(HistoricRegionTypes.Contains), typeLabels);
                    continue;
                }

                // Resti ancora visibili: un luogo specifico (in rovina) nel luogo attuale che contiene il luogo storico. Il
                // nome è quello dell'edificio se un nome italiano lo indica («Terme di Cotilia»), altrimenti «Sito
                // archeologico di …».
                if (chosen.Building.Length > 0 || HasVisibleRemains(entity))
                {
                    place.HistoricBuildingName = chosen.Building.Length > 0 ? chosen.Building : $"{ArchaeologicalSiteType} di {place.HistoricName}";
                    place.HistoricBuildingType = ArchaeologicalSiteType;
                    place.SpecificCondition = CondizioniLuogo.InRovina;
                }
            }
        }
    }

    // Edifici che danno il nome a una località antica e ne restano come rovine: tipo del luogo specifico.
    private static readonly Dictionary<string, string> BuildingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Terme"] = "Terme",
        ["Villa"] = "Villa",
        ["Tempio"] = "Tempio",
        ["Santuario"] = "Santuario",
        ["Teatro"] = "Teatro",
        ["Anfiteatro"] = "Anfiteatro",
        ["Mausoleo"] = "Mausoleo",
        ["Basilica"] = "Basilica",
        ["Foro"] = "Foro",
        ["Necropoli"] = "Necropoli",
        ["Rovine"] = "Rovine",
        ["Scavi"] = "Scavi",
    };

    // «Terme di Cotilia» → località «Cotilia», edificio «Terme di Cotilia» di tipo «Terme». Il nome della località è
    // l'alias italiano uguale al resto del nome, se c'è (per conservarne la grafia), altrimenti il resto stesso.
    // null se il nome non comincia con un edificio seguito da «di», «del», «della», «dei», «degli», «delle» o «d'».
    internal static (string Settlement, string Building, string BuildingType)? SplitBuildingName(string italian, IEnumerable<string> italianAliases)
    {
        var match = BuildingNamePattern().Match(italian.Trim());
        if (!match.Success || !BuildingWords.TryGetValue(match.Groups["word"].Value, out var type))
        {
            return null;
        }

        var rest = match.Groups["rest"].Value.Trim();
        var settlement = italianAliases.Select(a => a.Trim()).FirstOrDefault(a => string.Equals(a, rest, StringComparison.OrdinalIgnoreCase)) ?? rest;
        return (settlement, italian.Trim(), type);
    }

    [GeneratedRegex(@"^(?<word>\p{L}+)\s+(?:di|del|della|dei|degli|delle|d['’])\s*(?<rest>\p{L}.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex BuildingNamePattern();

    // Nome del luogo storico: l'etichetta italiana (anche se è in latino: «Aquae Cutiliae», come la chiama la voce
    // italiana), il nome usato nell'introduzione della voce italiana del personaggio, gli alias italiani; un nome che
    // comincia con un edificio («Terme di Cotilia») non è il nome della località ma dei suoi resti, e diventa Building.
    // Se ci sono solo nomi di edifici, la località prende il resto del nome («Cotilia»). Nome latino (solo mondo
    // romano): l'etichetta latina, il luogo scritto nell'infobox italiana se ha forma latina («Vicus Phalacrinae»),
    // un'etichetta in un'altra lingua con forma latina e simile al nome italiano; vuoto se uguale al nome italiano.
    internal static (string Italian, string Latin, string Building) ChooseHistoricNames(
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, List<string>> aliases,
        string introText,
        string infoboxText,
        bool roman,
        string fallback)
    {
        var latin = string.Empty;
        if (roman)
        {
            latin = labels.GetValueOrDefault("la", string.Empty).Trim();
            if (latin.Length == 0 && LooksLatin(infoboxText))
            {
                latin = infoboxText;
            }
        }

        var italianAliases = aliases.GetValueOrDefault("it") ?? [];
        var italianCandidates = new[] { labels.GetValueOrDefault("it", string.Empty), introText }
            .Concat(italianAliases)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        var building = italianCandidates.FirstOrDefault(t => SplitBuildingName(t, italianAliases) is not null) ?? string.Empty;
        var italian = italianCandidates.FirstOrDefault(t => SplitBuildingName(t, italianAliases) is null)
            ?? (building.Length > 0 ? SplitBuildingName(building, italianAliases)!.Value.Settlement : null)
            ?? fallback.Trim();

        if (roman && latin.Length == 0)
        {
            latin = new[] { "de", "en", "es", "fr", "nl" }
                .SelectMany(code => new[] { labels.GetValueOrDefault(code, string.Empty) }.Concat(aliases.GetValueOrDefault(code) ?? []))
                .Select(t => t.Trim())
                .FirstOrDefault(t => LooksLatin(t) && SameStem(t, italian) && !string.Equals(t, italian, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        return (italian, string.Equals(latin, italian, StringComparison.OrdinalIgnoreCase) ? string.Empty : latin, building);
    }

    // Forma latina: ogni parola finisce come un nome latino di luogo («Vicus Phalacrinae», «Aquae Cutiliae», «Forum Iulii»).
    internal static bool LooksLatin(string? text)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 && words.All(w => LatinWord().IsMatch(w));
    }

    // Stesse prime tre lettere, senza accenti e con «ph» = «f» (Falacrinae / Phalacrinae / Falacrine).
    private static bool SameStem(string a, string b)
    {
        static string Stem(string text)
        {
            var normalized = WikipediaConsensus.Normalize(text).Replace("ph", "f", StringComparison.Ordinal);
            return normalized.Length >= 3 ? normalized[..3] : normalized;
        }

        return Stem(a).Length == 3 && Stem(a) == Stem(b);
    }

    // Parte principale del luogo scritto nella voce: «Falacrinae, Italia» → «Falacrinae», «Cotilia (Rieti)» → «Cotilia».
    private static string MainPart(string? text)
        => (text ?? string.Empty).Split(',', '(', ';')[0].Trim().TrimEnd('.', ':');

    private static Dictionary<string, List<string>> ReadAliases(JsonElement entity)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (entity.GetPropertyOrDefault("aliases") is { ValueKind: JsonValueKind.Object } aliases)
        {
            foreach (var language in aliases.EnumerateObject())
            {
                result[language.Name] = [.. language.Value.EnumerateArray().Select(a => GetString(a, "value")).Where(v => v.Length > 0)];
            }
        }

        return result;
    }

    [GeneratedRegex(@"^\p{Lu}\p{L}*(?:ae|um|us|is|orum|arum|ium|ii)$|^(?:ad|in|apud)$")]
    private static partial Regex LatinWord();
}
