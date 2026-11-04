namespace HistoricEditor;

// Unisce le date scritte nella scheda con quelle importate e con le fonti indicate nella finestra «Fonti…».
public static class DateEvidenceBuilder
{
    public static List<string> SplitUserValues(string value) =>
        value.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Le date importate ancora presenti conservano le proprie fonti. Le fonti indicate dall'utente che non
    // appartengono a nessuna data importata valgono per tutte le date nuove (non c'è un abbinamento per posizione);
    // se non ci sono date nuove si aggiungono alla prima data, così non vanno perse.
    public static List<HistoricalDateEvidence> Build(
        string dateText,
        string sourceText,
        IEnumerable<HistoricalDateEvidence>? importedEvidence,
        SourceCitation? inputCitation)
    {
        var values = SplitUserValues(dateText);
        var evidence = importedEvidence?
            .Where(item => values.Contains(item.Value, StringComparer.OrdinalIgnoreCase))
            .Select(Clone)
            .ToList() ?? [];
        var knownUrls = evidence.SelectMany(e => e.Sources).Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extraSources = SplitUserValues(sourceText).Where(url => !knownUrls.Contains(url)).ToList();
        List<SourceCitation> ExtraCitations() => extraSources.Select(url => new SourceCitation { Title = "Fonte della data", Url = url }).ToList();

        var added = false;
        foreach (var value in values)
        {
            if (evidence.Any(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var sources = ExtraCitations();
            if (sources.Count == 0 && inputCitation is not null)
            {
                sources.Add(Clone(inputCitation));
            }

            evidence.Add(new HistoricalDateEvidence
            {
                Value = value,
                RawValue = value,
                Precision = "testo libero",
                Sources = sources,
            });
            added = true;
        }

        if (!added && evidence.Count > 0)
        {
            evidence[0].Sources.AddRange(ExtraCitations());
        }

        return evidence;
    }

    private static HistoricalDateEvidence Clone(HistoricalDateEvidence source) => new()
    {
        Value = source.Value,
        RawValue = source.RawValue,
        Precision = source.Precision,
        Rank = source.Rank,
        Calendario = source.Calendario,
        Qualifiers = source.Qualifiers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal),
        Sources = source.Sources.Select(Clone).ToList(),
    };

    private static SourceCitation Clone(SourceCitation source) => new()
    {
        Title = source.Title,
        Url = source.Url,
        RetrievedAt = source.RetrievedAt,
        WikidataId = source.WikidataId,
    };
}
