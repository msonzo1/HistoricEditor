using System.Text.RegularExpressions;

namespace HistoricEditor;

// Ricava un anno di riferimento da una data in testo libero ("c. 1540", "12 marzo 1550", "44 a.C.").
public static partial class HistoricalYear
{
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = YearPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups["year"].Value);
        return match.Groups["bc"].Success ? -year : year;
    }

    // Le date senza anno riconoscibile vanno in fondo, ordinate per testo.
    public static IOrderedEnumerable<T> OrderByYear<T>(IEnumerable<T> items, Func<T, string?> date)
        => items
            .OrderBy(item => Parse(date(item)) is null)
            .ThenBy(item => Parse(date(item)) ?? 0)
            .ThenBy(item => date(item), StringComparer.CurrentCultureIgnoreCase);

    // Preferisce un numero di 3-4 cifre (anno) ai giorni del mese; altrimenti accetta 1-2 cifre seguite da a.C./d.C.
    [GeneratedRegex(@"(?<!\d)(?<year>\d{3,4})(?!\d)(?:\s*(?<bc>a\.?\s*C\.?))?|(?<!\d)(?<year>\d{1,2})(?!\d)\s*(?:(?<bc>a\.?\s*C\.?)|d\.?\s*C\.?)", RegexOptions.IgnoreCase)]
    private static partial Regex YearPattern();
}
