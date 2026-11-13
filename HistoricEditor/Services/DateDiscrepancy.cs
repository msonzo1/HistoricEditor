namespace HistoricEditor;

// Date discordanti tra le fonti: più ipotesi tenute nel campo («07/08/0117; 08/08/0117») o date scartate perché
// indicate da una sola fonte (Cesare: 102 a.C.). Si mostrano nella sezione Fonti, accanto al LED di concordanza.
public static class DateDiscrepancy
{
    // Date tenute nel campo (non scartate), senza ripetizioni.
    public static List<HistoricalDateEvidence> Kept(IEnumerable<HistoricalDateEvidence>? evidence)
        => [.. (evidence ?? []).Where(e => !e.Scartata && e.Value.Trim().Length > 0).DistinctBy(e => DateFormats.Normalize(e.Value), StringComparer.OrdinalIgnoreCase)];

    public static List<HistoricalDateEvidence> Discarded(IEnumerable<HistoricalDateEvidence>? evidence)
        => [.. (evidence ?? []).Where(e => e.Scartata && e.Value.Trim().Length > 0).DistinctBy(e => DateFormats.Normalize(e.Value), StringComparer.OrdinalIgnoreCase)];

    public static bool Has(IReadOnlyList<HistoricalDateEvidence> evidence)
        => Kept(evidence).Count > 1 || Discarded(evidence).Count > 0;

    public static bool HasAny(IReadOnlyList<HistoricalDateEvidence> birth, IReadOnlyList<HistoricalDateEvidence> death)
        => Has(birth) || Has(death);

    // «Morte: 08/08/0117 (14 fonti) o 09/08/0117 (6 fonti); scartata 11/08/0117 (Wikipedia in spagnolo)».
    // Vuoto se le fonti concordano su una sola data.
    public static string Describe(string label, IReadOnlyList<HistoricalDateEvidence> evidence)
    {
        if (!Has(evidence))
        {
            return string.Empty;
        }

        var kept = Kept(evidence);
        var discarded = Discarded(evidence);
        var text = $"{label}: {string.Join(" o ", kept.Select(e => $"{DateFormats.Normalize(e.Value)} ({SourceCountText(e)})"))}";
        if (discarded.Count > 0)
        {
            text += $"; {(discarded.Count == 1 ? "scartata" : "scartate")} perché indicat{(discarded.Count == 1 ? "a" : "e")} da una sola fonte: "
                + string.Join(", ", discarded.Select(e => $"{DateFormats.Normalize(e.Value)} ({SourceNames(e)})"));
        }

        return text + ".";
    }

    public static string DescribeAll(IReadOnlyList<HistoricalDateEvidence> birth, IReadOnlyList<HistoricalDateEvidence> death)
        => string.Join(Environment.NewLine, new[] { Describe("Nascita", birth), Describe("Morte", death) }.Where(block => block.Length > 0));

    private static string SourceCountText(HistoricalDateEvidence evidence)
    {
        var count = evidence.Sources.Count;
        return count switch
        {
            0 => "fonte non indicata",
            1 => $"1 fonte: {SourceNames(evidence)}",
            _ => $"{count} fonti",
        };
    }

    private static string SourceNames(HistoricalDateEvidence evidence)
    {
        var names = evidence.Sources
            .Select(s => s.Title.Trim().Length > 0 ? s.Title.Trim() : s.Url.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? "Wikidata, senza riferimenti" : string.Join(", ", names);
    }
}
