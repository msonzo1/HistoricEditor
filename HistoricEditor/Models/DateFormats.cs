using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public sealed record DateFormatOption(string Codice, string Etichetta, char Separatore)
{
    public override string ToString() => Etichetta;
}

// Formato delle date scritte dall'app (import, calendario, convertitore) e di quelle riconosciute nei campi.
// L'anno ha sempre quattro cifre («23/06/0079», «06/0079», «0079»); «a.C.» si scrive solo prima di Cristo
// («15/03/44 a.C.»), mai «d.C.». Il formato si sceglie nel menu Visualizza (GG/MM/AAAA in modo predefinito).
public static partial class DateFormats
{
    public const string Slash = "GG/MM/AAAA";
    public const string Dot = "GG.MM.AAAA";
    public const string Dash = "GG-MM-AAAA";

    public static IReadOnlyList<DateFormatOption> All { get; } =
    [
        new(Slash, "GG/MM/AAAA (23/06/0079)", '/'),
        new(Dot, "GG.MM.AAAA (23.06.0079)", '.'),
        new(Dash, "GG-MM-AAAA (23-06-0079)", '-'),
    ];

    private static DateFormatOption current = All[0];

    // Formato in uso; un codice sconosciuto (impostazioni vecchie o rovinate) torna al predefinito.
    public static string Current
    {
        get => current.Codice;
        set => current = Option(value);
    }

    public static DateFormatOption Option(string? codice) => All.FirstOrDefault(o => o.Codice == codice) ?? All[0];

    public static string FormatYear(int year) => year < 0
        ? $"{(-year).ToString(CultureInfo.InvariantCulture)} a.C."
        : year.ToString("0000", CultureInfo.InvariantCulture);

    public static string Format(CalendarDate date) => Format(date.Year, date.Month, date.Day);

    // Giorno e mese sempre con due cifre; senza mese il giorno non si scrive.
    public static string Format(int year, int? month = null, int? day = null, string? format = null)
    {
        var separator = (format is null ? current : Option(format)).Separatore;
        var yearText = FormatYear(year);
        if (month is not int m || m is < 1 or > 12)
        {
            return yearText;
        }

        var monthText = m.ToString("00", CultureInfo.InvariantCulture);
        return day is int d and >= 1 and <= 31
            ? $"{d.ToString("00", CultureInfo.InvariantCulture)}{separator}{monthText}{separator}{yearText}"
            : $"{monthText}{separator}{yearText}";
    }

    // Riscrive nel formato in uso ogni ipotesi (separate da «;») che sia proprio una data: «17/11/9 d.C.» → «17/11/0009»,
    // «10 febbraio 1525» → «10/02/1525», «44 a.C.» resta così; le date incerte vanno nella forma compatta di UncertainDate.
    // Il testo libero non riconosciuto non cambia. Se nessuna ipotesi cambia si restituisce il testo originale.
    public static string Normalize(string? text, string? format = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        // Le date incerte riconosciute (tra, prima, dopo, circa, secolo, più date) si riscrivono nella forma compatta:
        // «1503-1519» → «tra 1503 e 1519», «circa 1540» → «c. 1540».
        if (UncertainDate.Parse(text) is { } uncertain)
        {
            var compact = uncertain.Compact(format);
            return string.Equals(compact, text.Trim(), StringComparison.Ordinal) ? text : compact;
        }

        var pieces = text.Split(';');
        var changed = false;
        for (var i = 0; i < pieces.Length; i++)
        {
            var trimmed = pieces[i].Trim();
            if (TryParseStrict(trimmed, out var date) && Format(date.Year, date.Month, date.Day, format) is var formatted && formatted != trimmed)
            {
                pieces[i] = formatted;
                changed = true;
            }
            else
            {
                pieces[i] = trimmed;
            }
        }

        return changed ? string.Join("; ", pieces.Where(p => p.Length > 0)) : text;
    }

    // Forma leggibile per elenchi e schede: «17 novembre 9», «12 o 13 luglio 100 a.C.», «tra il 1503 e il 1519»;
    // il testo non riconosciuto resta com'è (nel formato in uso se è una data).
    public static string Readable(string? text)
        => UncertainDate.Parse(text) is { } uncertain ? uncertain.Readable() : Normalize(text);

    // Vero solo se tutto il testo è una data: giorno/mese/anno, mese/anno o anno (con «/», «.» o «-»), oppure
    // «10 febbraio 1525», «marzo 1550». Un anno di una o due cifre vale solo con «a.C.»/«d.C.» o dopo il nome del mese,
    // per non scambiare «23/06/79» (forse 1979) per un anno del I secolo.
    public static bool TryParseStrict(string? text, out CalendarDate date)
    {
        date = default;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var numeric = NumericPattern().Match(value);
        if (numeric.Success)
        {
            var yearGroup = numeric.Groups["y"].Value;
            if (yearGroup.Length < 3 && !numeric.Groups["era"].Success)
            {
                return false;
            }

            return Build(numeric, int.Parse(numeric.Groups["m"].Success ? numeric.Groups["m"].Value : "0", CultureInfo.InvariantCulture), out date);
        }

        var textual = TextPattern().Match(value);
        if (textual.Success)
        {
            var monthIndex = Array.FindIndex(HistoricalCalendar.MonthNames, m => m.StartsWith(textual.Groups["mn"].Value[..3], StringComparison.OrdinalIgnoreCase)) + 1;
            return monthIndex > 0 && Build(textual, monthIndex, out date);
        }

        return false;
    }

    private static bool Build(Match match, int month, out CalendarDate date)
    {
        date = default;
        if (!int.TryParse(match.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year == 0)
        {
            return false;
        }

        int? day = match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : null;
        int? monthValue = month == 0 ? null : month;
        if (monthValue is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        date = new CalendarDate(match.Groups["bc"].Success ? -year : year, monthValue, day);
        return true;
    }

    private const string Era = @"(?:\s*(?<era>(?<bc>a\.?\s*C\.?)|d\.?\s*C\.?)(?!\p{L}))?";

    // Stesso separatore tra giorno, mese e anno; anche solo mese e anno, o solo l'anno.
    [GeneratedRegex(@"^(?:(?:(?<d>\d{1,2})\s*(?<s>[/.\-])\s*)?(?<m>\d{1,2})\s*(?(s)\k<s>|[/.\-])\s*)?(?<y>\d{1,4})" + Era + @"\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericPattern();

    [GeneratedRegex(@"^(?:(?<d>\d{1,2})\s*[°º]?\s+)?(?<mn>genn(?:aio)?|gen|febb(?:raio)?|feb|marzo|mar|aprile|apr|maggio|mag|giugno|giu|luglio|lug|agosto|ago|settembre|sett?|ottobre|ott|novembre|nov|dicembre|dic)\.?\s+(?<y>\d{1,4})(?!\d)(?:\s*(?<era>(?<bc>a\.?\s*C\.?)|d\.?\s*C\.?)(?!\p{L}))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TextPattern();
}
