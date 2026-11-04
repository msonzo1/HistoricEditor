using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Data di un titolo letta dal testo libero: anno sempre presente, mese e giorno solo se noti.
public readonly record struct TitleDate(int Year, int? Month, int? Day)
{
    public int Precision => Day is not null ? 3 : Month is not null ? 2 : 1;

    // Stessa data alla precisione comune: «800» e «25/12/800» concordano.
    public bool AgreesWith(TitleDate other)
        => Year == other.Year
            && (Month is null || other.Month is null || Month == other.Month)
            && (Day is null || other.Day is null || Day == other.Day);
}

// Confronta le date di inizio/fine di un titolo riportate dalle varie fonti.
public static partial class TitleDateConsensus
{
    public static TitleDate? Parse(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        var match = DatePattern().Match(value);
        if (!match.Success || !int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        if (match.Groups["bc"].Success)
        {
            year = -year;
        }

        int? month = match.Groups["month"].Success ? int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture) : null;
        int? day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture) : null;
        return month is < 1 or > 12 || day is < 1 or > 31 ? null : new TitleDate(year, month, day);
    }

    public static WikipediaConsensusResult Evaluate(IReadOnlyList<TitleDateSource>? sources)
    {
        var list = (sources ?? []).Where(s => Parse(s.Inizio) is not null || Parse(s.Fine) is not null).ToList();
        if (list.Count < 2)
        {
            return new WikipediaConsensusResult(ConsensusLevel.Hidden, 0, string.Empty, string.Empty);
        }

        var start = Compare(list.Select(s => Parse(s.Inizio)).ToList());
        var end = Compare(list.Select(s => Parse(s.Fine)).ToList());
        var level = start.Level > end.Level ? start.Level : end.Level;
        var differing = Enumerable.Range(0, list.Count).Count(i => start.Differs[i] || end.Differs[i]);
        var summary = level switch
        {
            ConsensusLevel.Green => "Le fonti concordano sulle date del titolo.",
            ConsensusLevel.Orange => "Le fonti indicano lo stesso anno ma giorno o mese diversi.",
            _ => "Le fonti indicano anni diversi per il titolo.",
        };

        var details = new StringBuilder(summary);
        for (var i = 0; i < list.Count; i++)
        {
            var notes = new List<string>();
            if (start.Differs[i]) notes.Add("inizio");
            if (end.Differs[i]) notes.Add("fine");
            details.Append('\n')
                .Append(notes.Count > 0 ? "✗ " : "✓ ")
                .Append(list[i].Fonte.Length > 0 ? list[i].Fonte : "Fonte senza nome")
                .Append(": ")
                .Append(Show(list[i].Inizio))
                .Append(" → ")
                .Append(Show(list[i].Fine));
            if (notes.Count > 0)
            {
                details.Append(" (diverso: ").Append(string.Join(", ", notes)).Append(')');
            }
        }

        return new WikipediaConsensusResult(level, differing, summary, details.ToString());
    }

    // Riferimento: l'anno più citato e, a parità, la data più precisa tra quelle di quell'anno.
    private static (ConsensusLevel Level, bool[] Differs) Compare(IReadOnlyList<TitleDate?> dates)
    {
        var differs = new bool[dates.Count];
        var known = dates.Where(d => d is not null).Select(d => d!.Value).ToList();
        if (known.Count < 2)
        {
            return (ConsensusLevel.Green, differs);
        }

        var majorityYear = known.GroupBy(d => d.Year).OrderByDescending(g => g.Count()).First().Key;
        var reference = known.Where(d => d.Year == majorityYear).OrderByDescending(d => d.Precision).First();
        var level = ConsensusLevel.Green;
        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] is not { } date || date.AgreesWith(reference))
            {
                continue;
            }

            differs[i] = true;
            var severity = date.Year == reference.Year ? ConsensusLevel.Orange : ConsensusLevel.Red;
            if (severity > level)
            {
                level = severity;
            }
        }

        return (level, differs);
    }

    private static string Show(string text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();

    // «25/12/800», «12/800», «800», «44 a.C.», «50 d.C.».
    [GeneratedRegex(@"^(?:(?:(?<day>\d{1,2})/)?(?<month>\d{1,2})/)?(?<year>\d{1,4})(?:\s*(?<bc>a\.\s*C\.)|\s*d\.\s*C\.)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();
}
