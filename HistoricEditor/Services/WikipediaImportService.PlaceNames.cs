using System.Text.Json;

namespace HistoricEditor;

// Nomi storici dei luoghi da Wikidata: nomi ufficiali con periodo (P1448 con P580/P582)
// e luoghi che il luogo ha sostituito (P1365, es. Costantinopoli → Istanbul) con fondazione P571 e fine P576.
public sealed partial class WikipediaImportService
{
    private const int MaximumPredecessorDepth = 3;

    // Lingue preferite per il nome di un periodo; poi qualunque nome in alfabeto latino, infine gli altri.
    private static readonly string[] HistoricNameLanguages = ["it", "la", "en", "es", "fr", "de", "pt", "ca", "nl"];

    internal static List<NomeStorico> BuildHistoricalNames(JsonElement placeEntity, IReadOnlyDictionary<string, JsonElement> predecessors)
    {
        var labels = ReadLabels(placeEntity);
        var currentName = labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", string.Empty));
        var placeId = GetString(placeEntity, "id");
        var names = new List<NomeStorico>();

        // Solo i nomi ufficiali con una fine: quelli ancora in uso non sono storici.
        var official = Statements(placeEntity.GetPropertyOrDefault("claims"), "P1448")
            .Select(s =>
            {
                var value = s.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value");
                return (Text: GetString(value, "text").Trim(), Language: GetString(value, "language"),
                    Start: QualifierYear(s, "P580"), End: QualifierYear(s, "P582"));
            })
            .Where(n => n.Text.Length > 0 && n.End is not null);
        foreach (var period in official.GroupBy(n => (n.Start, n.End)))
        {
            var best = period.OrderBy(n => LanguageRank(n.Language, n.Text)).First();
            names.Add(new NomeStorico
            {
                Nome = best.Text,
                Dal = best.Start,
                Al = best.End,
                Lingua = best.Language,
                Fonte = $"Wikidata {placeId} (nome ufficiale)",
            });
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { placeId };
        var level = ClaimEntityIds(placeEntity.GetPropertyOrDefault("claims"), "P1365");
        for (var depth = 0; depth < MaximumPredecessorDepth && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var id in level.Where(visited.Add))
            {
                if (!predecessors.TryGetValue(id, out var entity))
                {
                    continue;
                }

                var claims = entity.GetPropertyOrDefault("claims");
                var predecessorLabels = ReadLabels(entity);
                var name = predecessorLabels.GetValueOrDefault("it", predecessorLabels.GetValueOrDefault("en", string.Empty));
                var start = Statements(claims, "P571").Select(s => ReadYear(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(y => y is not null);
                var end = Statements(claims, "P576").Select(s => ReadYear(s.GetPropertyOrDefault("mainsnak"))).FirstOrDefault(y => y is not null);
                if (name.Length > 0 && (start is not null || end is not null))
                {
                    names.Add(new NomeStorico
                    {
                        Nome = name,
                        Dal = start,
                        Al = end,
                        Lingua = predecessorLabels.ContainsKey("it") ? "it" : "en",
                        Fonte = $"Wikidata {id}",
                    });
                }

                next.AddRange(ClaimEntityIds(claims, "P1365"));
            }

            level = next;
        }

        return HistoricalPlaceNames.Merge([], names, currentName);
    }

    // Id dei luoghi sostituiti (P1365) da leggere, fino a MaximumPredecessorDepth livelli.
    private static async Task<Dictionary<string, JsonElement>> ReadPredecessorsAsync(IEnumerable<JsonElement> places, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var pending = places.SelectMany(p => ClaimEntityIds(p.GetPropertyOrDefault("claims"), "P1365")).Distinct(StringComparer.Ordinal).ToList();
        for (var depth = 0; depth < MaximumPredecessorDepth && pending.Count > 0; depth++)
        {
            var read = await ReadEntitiesAsync(pending.Where(id => !result.ContainsKey(id)), cancellationToken);
            pending = [];
            foreach (var (id, entity) in read)
            {
                result[id] = entity;
                pending.AddRange(ClaimEntityIds(entity.GetPropertyOrDefault("claims"), "P1365").Where(p => !result.ContainsKey(p)));
            }
        }

        return result;
    }

    // Completa i luoghi importati con i loro nomi storici; senza rete restano senza.
    private static async Task ReadHistoricalNamesAsync(IEnumerable<HistoricalPlaceRecord> places, IReadOnlyDictionary<string, JsonElement> entities, CancellationToken cancellationToken)
    {
        var withEntity = places
            .Where(p => entities.ContainsKey(p.WikidataId))
            .Select(p => (Place: p, Entity: entities[p.WikidataId]))
            .ToList();
        if (withEntity.Count == 0)
        {
            return;
        }

        Dictionary<string, JsonElement> predecessors;
        try
        {
            predecessors = await ReadPredecessorsAsync(withEntity.Select(p => p.Entity), cancellationToken);
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            predecessors = [];
        }

        foreach (var (place, entity) in withEntity)
        {
            place.HistoricalNames = BuildHistoricalNames(entity, predecessors);
        }
    }

    public sealed record PlaceNamesResult(string WikidataId, string Label, string Description, IReadOnlyList<NomeStorico> Names);

    // Nomi storici di un luogo dell'archivio: dall'elemento Wikidata se noto, altrimenti cercando il nome (prima corrispondenza).
    public async Task<PlaceNamesResult?> ReadPlaceHistoricalNamesAsync(string wikidataId, string name, CancellationToken cancellationToken = default)
    {
        var id = (wikidataId ?? string.Empty).Trim().ToUpperInvariant();
        var description = string.Empty;
        if (!IsEntityId(id))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            using var search = await GetJsonAsync(
                $"{WikidataApi}?action=wbsearchentities&search={Uri.EscapeDataString(name.Trim())}&language=it&uselang=it&type=item&limit=1&format=json",
                cancellationToken);
            var first = search.RootElement.GetPropertyOrDefault("search") is { ValueKind: JsonValueKind.Array } results
                ? results.EnumerateArray().FirstOrDefault()
                : default;
            id = first.ValueKind == JsonValueKind.Object ? GetString(first, "id") : string.Empty;
            description = first.ValueKind == JsonValueKind.Object ? GetString(first, "description") : string.Empty;
            if (!IsEntityId(id))
            {
                return null;
            }
        }

        var entities = await ReadEntitiesAsync([id], cancellationToken);
        if (!entities.TryGetValue(id, out var entity))
        {
            return null;
        }

        var predecessors = await ReadPredecessorsAsync([entity], cancellationToken);
        var labels = ReadLabels(entity);
        return new PlaceNamesResult(id, labels.GetValueOrDefault("it", labels.GetValueOrDefault("en", id)), description,
            BuildHistoricalNames(entity, predecessors));
    }

    private static int? QualifierYear(JsonElement statement, string propertyId)
        => QualifierSnaks(statement, propertyId).Select(ReadYear).FirstOrDefault(y => y is not null);

    private static int LanguageRank(string language, string text)
    {
        var index = Array.IndexOf(HistoricNameLanguages, language);
        if (index >= 0)
        {
            return index;
        }

        return text.All(c => c < 0x250 || !char.IsLetter(c)) ? 50 : 100;
    }
}
