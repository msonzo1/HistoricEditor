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
            return ParseCentury(text);
        }

        var year = int.Parse(match.Groups["year"].Value);
        return match.Groups["bc"].Success ? -year : year;
    }

    // «XV secolo», «sec. XV», «secolo XV», «XV sec.» → 1450, «V secolo a.C.» → -450: il centro del secolo, usato per ordinare.
    private static int? ParseCentury(string text)
    {
        var match = CenturyPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var century = 0;
        var previous = 0;
        foreach (var c in match.Groups["roman"].Value.ToUpperInvariant().Reverse())
        {
            var value = c switch { 'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, _ => 100 };
            century += value < previous ? -value : value;
            previous = Math.Max(previous, value);
        }

        var middle = (century - 1) * 100 + 50;
        return match.Groups["bc"].Success ? -middle : middle;
    }

    [GeneratedRegex(@"\b(?:(?<roman>(?-i:[IVXLC]+))\s+sec(?:olo\b|\.|\b)|sec(?:olo\b|\.)\s*(?<roman>(?-i:[IVXLC]+))\b)(?:\s*(?<bc>a\.?\s*C\.?)(?!\p{L}))?", RegexOptions.IgnoreCase)]
    private static partial Regex CenturyPattern();

    // Anno leggibile: i valori negativi sono anni avanti Cristo («44 a.C.»).
    public static string Format(int year) => year < 0 ? $"{-year} a.C." : year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Le date senza anno riconoscibile vanno in fondo, ordinate per testo.
    public static IOrderedEnumerable<T> OrderByYear<T>(IEnumerable<T> items, Func<T, string?> date)
        => items
            .OrderBy(item => Parse(date(item)) is null)
            .ThenBy(item => Parse(date(item)) ?? 0)
            .ThenBy(item => date(item), StringComparer.CurrentCultureIgnoreCase);

    // Preferisce un numero di 3-4 cifre (anno) ai giorni del mese; altrimenti accetta 1-2 cifre seguite da a.C./d.C.
    [GeneratedRegex(@"(?<!\d)(?<year>\d{3,4})(?!\d)(?:\s*(?<bc>a\.?\s*C\.?)(?!\p{L}))?|(?<!\d)(?<year>\d{1,2})(?!\d)\s*(?:(?<bc>a\.?\s*C\.?)|d\.?\s*C\.?)(?!\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex YearPattern();
}
