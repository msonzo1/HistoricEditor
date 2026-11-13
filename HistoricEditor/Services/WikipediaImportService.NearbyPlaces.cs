using System.Globalization;
using System.Text.Json;

namespace HistoricEditor;

// Comune attuale per i luoghi che su Wikidata non ne indicano uno: località antiche o scomparse (Falacrine, oggi nel
// territorio di Cittareale), siti archeologici, ville romane… Wikidata non ha i confini dei comuni, quindi si cerca il
// comune con il centro più vicino alle coordinate del luogo: di solito è quello che lo contiene, ma vicino a un confine
// può essere il vicino. Per questo il luogo risultante resta da confermare (vedi GeographyResolver).
public sealed partial class WikipediaImportService
{
    // Oltre questa distanza dal centro del comune più vicino non si propone nulla.
    private const int NearbyMunicipalityRadiusKm = 15;

    // Luogo → comune più vicino (Q-id), solo per i luoghi con coordinate, in un Paese con i tipi di comune noti,
    // che non sono comuni e non ne hanno uno tra le suddivisioni amministrative.
    private static async Task<Dictionary<string, string>> FindNearbyMunicipalitiesAsync(IEnumerable<HistoricalPlaceRecord> places, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var place in places.Where(p => !result.ContainsKey(p.WikidataId)))
        {
            if (MunicipalityTypes(place) is not { } types || HasMunicipality(place, types))
            {
                continue;
            }

            try
            {
                var query = NearbyMunicipalityQuery(place.Latitudine!.Value, place.Longitudine!.Value, types);
                using var document = await GetJsonAsync($"{WikidataSparql}?format=json&query={Uri.EscapeDataString(query)}", cancellationToken);
                if (ParseNearbyMunicipality(document.RootElement) is { } municipalityId)
                {
                    result[place.WikidataId] = municipalityId;
                }
            }
            catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
            {
                // Senza risposta il luogo resta da scegliere a mano, come prima.
            }
        }

        return result;
    }

    // Tipi Wikidata del livello più basso dello schema del Paese (in Italia: comune, comune sparso); null se il luogo
    // non ha coordinate o il Paese non ha tipi noti.
    private static HashSet<string>? MunicipalityTypes(HistoricalPlaceRecord place)
        => place.Latitudine is not null && place.Longitudine is not null
            && GeoSchemas.LevelTypes(place.CurrentCountry) is { } levelTypes
            && levelTypes.TryGetValue(GeoSchemas.For(place.CurrentCountry)[^1], out var types)
                ? types
                : null;

    internal static bool HasMunicipality(HistoricalPlaceRecord place, IReadOnlySet<string> municipalityTypes)
        => place.TypeIds.Any(municipalityTypes.Contains)
            || place.AdministrativeDivisions.Any(d => d.Relation.StartsWith("Unità amministrativa", StringComparison.Ordinal)
                && d.TypeIds.Any(municipalityTypes.Contains));

    // Il comune ancora esistente (senza data di soppressione P576) con il centro più vicino, entro il raggio.
    internal static string NearbyMunicipalityQuery(double latitude, double longitude, IEnumerable<string> municipalityTypes)
    {
        var point = string.Create(CultureInfo.InvariantCulture, $"Point({longitude} {latitude})");
        var types = string.Join(' ', municipalityTypes.Order(StringComparer.Ordinal).Select(t => "wd:" + t));
        return "SELECT ?item ?dist WHERE { "
            + "SERVICE wikibase:around { ?item wdt:P625 ?location . "
            + $"bd:serviceParam wikibase:center \"{point}\"^^geo:wktLiteral . "
            + $"bd:serviceParam wikibase:radius \"{NearbyMunicipalityRadiusKm}\" . "
            + "bd:serviceParam wikibase:distance ?dist . } "
            + $"VALUES ?type {{ {types} }} ?item wdt:P31 ?type . "
            + "FILTER NOT EXISTS { ?item wdt:P576 ?end . } "
            + "} ORDER BY ?dist LIMIT 1";
    }

    internal static string? ParseNearbyMunicipality(JsonElement root)
    {
        var bindings = root.GetPropertyOrDefault("results").GetPropertyOrDefault("bindings");
        if (bindings.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var binding in bindings.EnumerateArray())
        {
            var uri = GetString(binding.GetPropertyOrDefault("item"), "value");
            var id = uri[(uri.LastIndexOf('/') + 1)..];
            if (IsEntityId(id))
            {
                return id;
            }
        }

        return null;
    }
}
