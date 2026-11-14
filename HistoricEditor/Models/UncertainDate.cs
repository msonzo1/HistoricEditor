using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public enum UncertainDateKind
{
    // Una data (giorno, mese, anno o secolo).
    Exact,
    // Una tra più date: «12/07/100 a.C.; 13/07/100 a.C.».
    Alternatives,
    // Tra due date: «tra 1503 e 1519».
    Between,
    // Prima di una data: «prima del 1500».
    Before,
    // Dopo una data: «dopo il 1500».
    After,
}

// Un punto della data: giorno/mese/anno (mese e giorno facoltativi) oppure un secolo (negativo = avanti Cristo).
public sealed record DatePoint(int? Year, int? Month = null, int? Day = null, int? Century = null)
{
    public bool IsCentury => Century is not null;

    // Primo e ultimo anno coperti: un anno è sé stesso, un secolo va da …01 a …00 («XV secolo» = 1401–1500).
    public int First => Century is int c ? (c > 0 ? (c - 1) * 100 + 1 : c * 100) : Year!.Value;
    public int Last => Century is int c ? (c > 0 ? c * 100 : (c + 1) * 100 - 1) : Year!.Value;

    // Anno per ordinare: per il secolo il centro (1450), come HistoricalYear.
    public int SortYear => Century is int c ? (c > 0 ? (c - 1) * 100 + 50 : -((-c - 1) * 100 + 50)) : Year!.Value;
}

// Data incerta, scritta come testo nei campi (così resta compatibile con import, backup e dati salvati):
//   giorno «17/11/0009», mese «11/0009», anno «0009», secolo «XV secolo» / «V secolo a.C.»;
//   una tra più date «12/07/100 a.C.; 13/07/100 a.C.»; tra due date «tra 1503 e 1519»;
//   prima/dopo «prima del 1500», «dopo il 1500»; «c. » davanti vale per tutte («c. 1540», «c. XV secolo»).
// Si leggono anche le forme scritte a mano più comuni: «1503-1519», «circa 1540», «ante 1500», «post 1500», «sec. XV».
public sealed partial record UncertainDate(UncertainDateKind Kind, IReadOnlyList<DatePoint> Points, bool Circa = false)
{
    private static readonly string[] MonthNames =
        ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];

    public static UncertainDate? Parse(string? text)
    {
        var value = Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
        if (value.Length == 0)
        {
            return null;
        }

        var circa = false;
        var circaMatch = CircaPattern().Match(value);
        if (circaMatch.Success)
        {
            circa = true;
            value = value[circaMatch.Length..].Trim();
        }

        if (BetweenPattern().Match(value) is { Success: true } between
            && ParsePoint(between.Groups["a"].Value) is { } from && ParsePoint(between.Groups["b"].Value) is { } to)
        {
            return new UncertainDate(UncertainDateKind.Between, [from, to], circa);
        }

        if (YearRangePattern().Match(value) is { Success: true } range
            && ParsePoint(range.Groups["a"].Value + range.Groups["bc"].Value) is { } rangeFrom
            && ParsePoint(range.Groups["b"].Value + range.Groups["bc"].Value) is { } rangeTo)
        {
            return new UncertainDate(UncertainDateKind.Between, [rangeFrom, rangeTo], circa);
        }

        if (BeforePattern().Match(value) is { Success: true } before && ParsePoint(before.Groups["a"].Value) is { } beforePoint)
        {
            return new UncertainDate(UncertainDateKind.Before, [beforePoint], circa);
        }

        if (AfterPattern().Match(value) is { Success: true } after && ParsePoint(after.Groups["a"].Value) is { } afterPoint)
        {
            return new UncertainDate(UncertainDateKind.After, [afterPoint], circa);
        }

        var pieces = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length == 1)
        {
            pieces = Regex.Split(value, @"\s+o\s+");
        }

        var points = new List<DatePoint>();
        foreach (var piece in pieces)
        {
            if (ParsePoint(piece) is not { } point)
            {
                return null;
            }

            points.Add(point);
        }

        return points.Count switch
        {
            0 => null,
            1 => new UncertainDate(UncertainDateKind.Exact, points, circa),
            _ => new UncertainDate(UncertainDateKind.Alternatives, points, circa),
        };
    }

    // Un punto: data (giorno/mese/anno, mese/anno, anno, anche «10 febbraio 1525») o secolo. Un anno di una o due cifre
    // vale solo con «a.C.»/«d.C.» (vedi DateFormats.TryParseStrict).
    public static DatePoint? ParsePoint(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (DateFormats.TryParseStrict(value, out var date))
        {
            return new DatePoint(date.Year, date.Month, date.Day);
        }

        if (CenturyPattern().Match(value) is { Success: true } century && RomanToInt(century.Groups["roman"].Value) is int c and > 0)
        {
            return new DatePoint(null, Century: century.Groups["bc"].Success ? -c : c);
        }

        return null;
    }

    // Primo e ultimo anno possibili (null = aperto: «prima del 1500» non ha inizio), per i filtri sul periodo.
    public int? FirstYear => Kind switch
    {
        UncertainDateKind.Before => null,
        _ => Points.Min(p => p.First),
    };

    public int? LastYear => Kind switch
    {
        UncertainDateKind.After => null,
        _ => Points.Max(p => p.Last),
    };

    // Anno per ordinare: tra due date il centro, tra più date la prima, altrimenti l'anno della data.
    public int SortYear => Kind == UncertainDateKind.Between
        ? (Points[0].SortYear + Points[1].SortYear) / 2
        : Points[0].SortYear;

    // Forma compatta per i campi, nel formato delle date in uso: «c. 1540», «tra 1503 e 1519», «prima del 1500».
    public string Compact(string? format = null)
    {
        string Point(DatePoint p) => p.IsCentury ? CenturyText(p.Century!.Value) : DateFormats.Format(p.Year!.Value, p.Month, p.Day, format);
        var body = Kind switch
        {
            UncertainDateKind.Between => $"tra {Point(Points[0])} e {Point(Points[1])}",
            UncertainDateKind.Before => $"prima {Preposition("del", Points[0])} {Point(Points[0])}".Replace("' ", "'"),
            UncertainDateKind.After => $"dopo {Preposition("il", Points[0])} {Point(Points[0])}".Replace("' ", "'"),
            _ => string.Join("; ", Points.Select(Point)),
        };
        return Circa ? $"c. {body}" : body;
    }

    // Forma leggibile per elenchi e schede: «17 novembre 9», «12 o 13 luglio 100 a.C.», «tra il 1503 e il 1519»,
    // «circa 1540», «prima dell'800», «XV secolo».
    public string Readable()
    {
        var body = Kind switch
        {
            UncertainDateKind.Between => $"tra {Preposition("il", Points[0])} {ReadablePoint(Points[0])} e {Preposition("il", Points[1])} {ReadablePoint(Points[1])}",
            UncertainDateKind.Before => $"prima {Preposition("del", Points[0])} {ReadablePoint(Points[0])}",
            UncertainDateKind.After => $"dopo {Preposition("il", Points[0])} {ReadablePoint(Points[0])}",
            UncertainDateKind.Alternatives => ReadableAlternatives(),
            _ => ReadablePoint(Points[0]),
        };
        body = body.Replace("' ", "'");
        return Circa ? $"circa {body}" : body;
    }

    private string ReadableAlternatives()
    {
        // Stesso mese e anno: «12 o 13 luglio 100 a.C.».
        if (Points.All(p => p.Day is not null && p.Month == Points[0].Month && p.Year == Points[0].Year))
        {
            return $"{string.Join(" o ", Points.Select(p => p.Day!.Value.ToString(CultureInfo.InvariantCulture)))} {MonthNames[Points[0].Month!.Value - 1]} {YearText(Points[0].Year!.Value)}";
        }

        return string.Join(" o ", Points.Select(ReadablePoint));
    }

    public static string ReadablePoint(DatePoint p)
    {
        if (p.IsCentury)
        {
            return CenturyText(p.Century!.Value);
        }

        var year = YearText(p.Year!.Value);
        if (p.Month is not int month)
        {
            return year;
        }

        return p.Day is int day ? $"{day} {MonthNames[month - 1]} {year}" : $"{MonthNames[month - 1]} {year}";
    }

    private static string YearText(int year) => year < 0 ? $"{-year} a.C." : year.ToString(CultureInfo.InvariantCulture);

    private static string CenturyText(int century) => century < 0 ? $"{ToRoman(-century)} secolo a.C." : $"{ToRoman(century)} secolo";

    // Articolo o preposizione articolata davanti al punto: «il 1503» ma «l'800», «l'8 marzo», «l'11 aprile»; «del»/«dell'».
    private static string Preposition(string word, DatePoint point)
    {
        var elided = !point.IsCentury && ((point.Day ?? point.Year) is int n && StartsWithVowelSound(Math.Abs(n), point.Day is not null));
        return (word, elided) switch
        {
            ("il", true) => "l'",
            ("del", true) => "dell'",
            _ => word,
        };
    }

    // In italiano si elide davanti a otto, undici e agli anni che si leggono cominciando con «otto» (ottanta…, ottocento…).
    private static bool StartsWithVowelSound(int n, bool day)
        => n is 8 or 11 || (!day && n is (>= 80 and <= 89) or (>= 800 and <= 899));

    private static int? RomanToInt(string roman)
    {
        var total = 0;
        var previous = 0;
        foreach (var c in roman.ToUpperInvariant().Reverse())
        {
            var value = c switch { 'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, _ => 0 };
            if (value == 0)
            {
                return null;
            }

            total += value < previous ? -value : value;
            previous = Math.Max(previous, value);
        }

        return total;
    }

    public static string ToRoman(int number)
    {
        var result = new System.Text.StringBuilder();
        foreach (var (value, symbol) in new[] { (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
        {
            while (number >= value)
            {
                result.Append(symbol);
                number -= value;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^(?:c\.|ca\.|circa|approx\.?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CircaPattern();

    [GeneratedRegex(@"^(?:tra|fra)\s+(?:(?:il|l')\s*)?(?<a>.+?)\s+e\s+(?:(?:il|l')\s*)?(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BetweenPattern();

    // «1503-1519», «1503–1519», «100–90 a.C.»: solo anni di 3-4 cifre (o con a.C.), per non scambiare «17-11-0009».
    [GeneratedRegex(@"^(?<a>\d{1,4})\s*[–—-]\s*(?<b>\d{1,4})(?<bc>\s*a\.?\s*C\.?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex YearRangePattern();

    [GeneratedRegex(@"^(?:prima\s+(?:del|dell'|della|di)|ante)\s*(?<a>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BeforePattern();

    [GeneratedRegex(@"^(?:dopo\s+(?:il|l'|la|del|dell')?|post)\s*(?<a>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AfterPattern();

    [GeneratedRegex(@"^(?:(?<roman>(?-i:[IVXLC]+))\s+sec(?:olo|\.)?|sec(?:olo|\.)?\s*(?<roman>(?-i:[IVXLC]+)))(?:\s*(?<bc>a\.?\s*C\.?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CenturyPattern();
}
