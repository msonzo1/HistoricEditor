namespace HistoricEditor;

// Tipo di un luogo specifico (frazione, monastero, castello…) ricavato dai tipi Wikidata (P31).
public static class SpecificPlaceTypes
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["Q18413256"] = "Frazione",
        ["Q5084"] = "Borgo",
        ["Q532"] = "Villaggio",
        ["Q3257686"] = "Località",
        ["Q123705"] = "Quartiere",
        ["Q44613"] = "Monastero",
        ["Q160742"] = "Abbazia",
        ["Q23413"] = "Castello",
        ["Q751876"] = "Castello",
        ["Q57821"] = "Fortezza",
        ["Q1785071"] = "Forte",
        ["Q16560"] = "Palazzo",
        ["Q3950"] = "Villa",
        ["Q3947"] = "Casa",
        ["Q16970"] = "Chiesa",
        ["Q1088552"] = "Chiesa",
        ["Q108325"] = "Cappella",
        ["Q163687"] = "Basilica",
        ["Q120560"] = "Basilica",
        ["Q2977"] = "Cattedrale",
        ["Q44539"] = "Tempio",
        ["Q39614"] = "Cimitero",
        ["Q16917"] = "Ospedale",
        ["Q40357"] = "Prigione",
        ["Q24354"] = "Teatro",
        ["Q33506"] = "Museo",
        ["Q3918"] = "Università",
        ["Q174782"] = "Piazza",
        ["Q22698"] = "Parco",
        ["Q839954"] = "Sito archeologico",
        ["Q23442"] = "Isola",
        ["Q8502"] = "Monte",
    };

    // Il primo tipo noto vince; altrimenti si usa l'etichetta italiana del primo tipo che ne ha una.
    public static string Describe(IEnumerable<string>? typeIds, IReadOnlyDictionary<string, string>? italianLabels = null)
    {
        var ids = (typeIds ?? []).ToList();
        foreach (var id in ids)
        {
            if (Known.TryGetValue(id, out var known))
            {
                return known;
            }
        }

        foreach (var id in ids)
        {
            if (italianLabels is not null && italianLabels.TryGetValue(id, out var label) && label.Trim().Length > 0)
            {
                var text = label.Trim();
                return char.ToUpper(text[0], System.Globalization.CultureInfo.GetCultureInfo("it-IT")) + text[1..];
            }
        }

        return string.Empty;
    }

    public static bool IsKnown(string typeId) => Known.ContainsKey(typeId);
}
