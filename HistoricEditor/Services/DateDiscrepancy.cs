namespace HistoricEditor;

// Rileva e descrive le date discordanti (più ipotesi provenienti da fonti/lingue diverse).
public static class DateDiscrepancy
{
    public static bool Has(IReadOnlyList<HistoricalDateEvidence> evidence)
        => evidence.Select(e => e.Value)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

    public static bool HasAny(IReadOnlyList<HistoricalDateEvidence> birth, IReadOnlyList<HistoricalDateEvidence> death)
        => Has(birth) || Has(death);

    public static string Describe(string label, IReadOnlyList<HistoricalDateEvidence> evidence)
    {
        var values = evidence.Where(e => e.Value.Length > 0).ToList();
        if (values.Count < 2)
        {
            return string.Empty;
        }

        var lines = values.Select(e =>
        {
            var sources = e.Sources
                .Select(s => s.Title)
                .Where(title => title.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return sources.Count > 0 ? $"   • {e.Value} — {string.Join(", ", sources)}" : $"   • {e.Value}";
        });

        return $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    public static string DescribeAll(IReadOnlyList<HistoricalDateEvidence> birth, IReadOnlyList<HistoricalDateEvidence> death)
    {
        var blocks = new[]
        {
            Describe("Nascita", birth),
            Describe("Morte", death),
        }.Where(block => block.Length > 0);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }
}
