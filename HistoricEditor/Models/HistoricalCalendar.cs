using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

public sealed record CalendarOption(string Codice, string Etichetta)
{
    public override string ToString() => Etichetta;
}

public sealed record YearStyleOption(string Codice, string Etichetta, string Descrizione)
{
    public override string ToString() => Etichetta;
}

// Primo giorno gregoriano in un paese: il giorno prima era ancora in uso il calendario giuliano.
public sealed record GregorianAdoption(string Paese, int Anno, int Mese, int Giorno, string Note)
{
    public CalendarDate PrimoGiorno => new(Anno, Mese, Giorno);

    public override string ToString() => Paese;
}

// Anno con la numerazione storica (negativo = a.C., senza anno zero); mese e giorno possono mancare.
public readonly record struct CalendarDate(int Year, int? Month = null, int? Day = null)
{
    public bool IsComplete => Month is not null && Day is not null;
}

// Una data riportata in un calendario e uno stile, con l'equivalente nell'altro sistema.
public sealed record CalendarConversion(string Sistema, string Data, string GiornoSettimana)
{
    public string Etichetta => GiornoSettimana.Length > 0 ? $"{Sistema}: {Data}, {GiornoSettimana}" : $"{Sistema}: {Data}";
}

// Calendario giuliano e gregoriano e stili di inizio dell'anno usati in Italia (fiorentino, pisano, veneto…).
public static partial class HistoricalCalendar
{
    public const string NonIndicato = "";
    public const string Gregoriano = "gregoriano";
    public const string Giuliano = "giuliano";

    public const string StileComune = "";
    public const string StileFiorentino = "fiorentino";
    public const string StilePisano = "pisano";
    public const string StileVeneto = "veneto";
    public const string StileNativita = "nativita";
    public const string StileBizantino = "bizantino";

    public static IReadOnlyList<CalendarOption> Calendari { get; } =
    [
        new(NonIndicato, "Non indicato"),
        new(Giuliano, "Giuliano"),
        new(Gregoriano, "Gregoriano"),
    ];

    public static IReadOnlyList<YearStyleOption> Stili { get; } =
    [
        new(StileComune, "Comune (1° gennaio)", "L’anno inizia il 1° gennaio, come oggi (stile della Circoncisione)."),
        new(StileFiorentino, "Fiorentino (25 marzo)", "Stile dell’Incarnazione fiorentino: l’anno iniziava il 25 marzo, quasi tre mesi dopo il nostro; le date dal 1° gennaio al 24 marzo appartengono all’anno moderno successivo. In uso a Firenze fino al 1749."),
        new(StilePisano, "Pisano (25 marzo)", "Stile dell’Incarnazione pisano: l’anno iniziava il 25 marzo, quasi nove mesi prima del nostro; le date dal 25 marzo al 31 dicembre appartengono all’anno moderno precedente. In uso a Pisa fino al 1749."),
        new(StileVeneto, "Veneto (1° marzo)", "More veneto: l’anno iniziava il 1° marzo; gennaio e febbraio appartengono all’anno moderno successivo. In uso nella Repubblica di Venezia fino al 1797."),
        new(StileNativita, "Natività (25 dicembre)", "Stile della Natività: l’anno iniziava il 25 dicembre; le date dal 25 al 31 dicembre appartengono all’anno moderno precedente."),
        new(StileBizantino, "Bizantino (1° settembre)", "Stile bizantino: l’anno iniziava il 1° settembre; le date da settembre a dicembre appartengono all’anno moderno precedente. In uso nell’Italia meridionale."),
    ];

    // Date di passaggio più citate; in molti stati tedeschi e svizzeri il passaggio avvenne in anni diversi.
    public static IReadOnlyList<GregorianAdoption> Adozioni { get; } =
    [
        new("Stati italiani, Spagna, Portogallo, Polonia", 1582, 10, 15, "Al 4 ottobre 1582 seguì il 15 ottobre (bolla Inter gravissimas)."),
        new("Francia", 1582, 12, 20, "Al 9 dicembre 1582 seguì il 20 dicembre."),
        new("Paesi Bassi cattolici", 1583, 1, 1, "Brabante, Zelanda e Stati Generali: al 21 dicembre 1582 seguì il 1° gennaio 1583."),
        new("Baviera e stati cattolici tedeschi", 1583, 10, 16, "Baviera: al 5 ottobre seguì il 16 ottobre 1583; altri stati cattolici tra il 1583 e il 1585."),
        new("Austria e Boemia", 1584, 1, 17, "Al 6 gennaio 1584 seguì il 17 gennaio."),
        new("Ungheria", 1587, 11, 1, "Al 21 ottobre 1587 seguì il 1° novembre."),
        new("Stati protestanti tedeschi, Danimarca, Norvegia", 1700, 3, 1, "Al 18 febbraio 1700 seguì il 1° marzo."),
        new("Svizzera protestante", 1701, 1, 12, "Zurigo, Berna e altri cantoni: al 31 dicembre 1700 seguì il 12 gennaio 1701."),
        new("Gran Bretagna e colonie", 1752, 9, 14, "Al 2 settembre 1752 seguì il 14 settembre; l’anno iniziava il 25 marzo fino al 1751."),
        new("Svezia e Finlandia", 1753, 3, 1, "Al 17 febbraio 1753 seguì il 1° marzo."),
        new("Bulgaria", 1916, 4, 14, "Al 31 marzo 1916 seguì il 14 aprile."),
        new("Russia", 1918, 2, 14, "Al 31 gennaio 1918 seguì il 14 febbraio."),
        new("Serbia e Jugoslavia", 1919, 1, 28, "Al 14 gennaio 1919 seguì il 28 gennaio."),
        new("Romania", 1919, 4, 14, "Al 31 marzo 1919 seguì il 14 aprile."),
        new("Grecia", 1923, 3, 1, "Al 15 febbraio 1923 seguì il 1° marzo."),
    ];

    private static readonly string[] MonthNames =
        ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];

    private static readonly string[] DayNames = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"];

    // Primo giorno del calendario gregoriano secondo la bolla papale.
    public static CalendarDate GregorianReform { get; } = new(1582, 10, 15);

    public static CalendarOption CalendarInfo(string? codice)
        => Calendari.FirstOrDefault(c => c.Codice == (codice ?? string.Empty)) ?? Calendari[0];

    public static YearStyleOption StyleInfo(string? codice)
        => Stili.FirstOrDefault(s => s.Codice == (codice ?? string.Empty)) ?? Stili[0];

    // Legge la prima data di un testo libero: «10 febbraio 1525», «1° marzo 1550», «15/03/44 a.C.», «03/1550», «1525».
    public static bool TryParse(string? text, out CalendarDate date)
    {
        date = default;
        var first = (text ?? string.Empty).Split(';')[0].Trim();
        if (first.Length == 0 || !first.Any(char.IsDigit))
        {
            return false;
        }

        var match = NumericDatePattern().Match(first);
        if (!match.Success)
        {
            match = TextDatePattern().Match(first);
        }

        if (match.Success && int.TryParse(match.Groups["y"].Value, out var year) && year > 0)
        {
            var month = match.Groups["mn"].Success
                ? Array.FindIndex(MonthNames, m => m.StartsWith(match.Groups["mn"].Value[..3], StringComparison.OrdinalIgnoreCase)) + 1
                : int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            int? day = match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : null;
            if (match.Groups["bc"].Success)
            {
                year = -year;
            }

            if (month is < 1 or > 12 || day is < 1 or > 31)
            {
                return false;
            }

            date = new CalendarDate(year, month, day);
            return true;
        }

        if (HistoricalYear.Parse(first) is int onlyYear && !CenturyOrDecadePattern().IsMatch(first))
        {
            date = new CalendarDate(onlyYear);
            return true;
        }

        return false;
    }

    public static bool IsValid(CalendarDate date, string calendar)
        => date.Year != 0
            && (date.Month is null || date.Month is >= 1 and <= 12)
            && (date.Day is null || (date.Month is int m && date.Day >= 1 && date.Day <= DaysInMonth(date.Year, m, calendar != Gregoriano)));

    public static int DaysInMonth(int year, int month, bool julian)
    {
        var astronomical = ToAstronomical(year);
        var leap = julian
            ? Mod(astronomical, 4) == 0
            : Mod(astronomical, 4) == 0 && (Mod(astronomical, 100) != 0 || Mod(astronomical, 400) == 0);
        return month == 2 ? (leap ? 29 : 28) : month is 4 or 6 or 9 or 11 ? 30 : 31;
    }

    // Numero del giorno giuliano: permette di passare da un calendario all'altro e di ricavare il giorno della settimana.
    public static long ToDayNumber(CalendarDate date, string calendar)
    {
        if (!date.IsComplete)
        {
            throw new ArgumentException("Servono giorno e mese.", nameof(date));
        }

        long a = (14 - date.Month!.Value) / 12;
        long y = ToAstronomical(date.Year) + 4800 - a;
        long m = date.Month.Value + 12 * a - 3;
        var days = date.Day!.Value + (153 * m + 2) / 5 + 365 * y + y / 4;
        return calendar == Gregoriano ? days - y / 100 + y / 400 - 32045 : days - 32083;
    }

    public static CalendarDate FromDayNumber(long dayNumber, string calendar)
    {
        long b, c;
        if (calendar == Gregoriano)
        {
            var a = dayNumber + 32044;
            b = (4 * a + 3) / 146097;
            c = a - 146097 * b / 4;
        }
        else
        {
            b = 0;
            c = dayNumber + 32082;
        }

        var d = (4 * c + 3) / 1461;
        var e = c - 1461 * d / 4;
        var m = (5 * e + 2) / 153;
        var day = (int)(e - (153 * m + 2) / 5 + 1);
        var month = (int)(m + 3 - 12 * (m / 10));
        var year = (int)(100 * b + d - 4800 + m / 10);
        return new CalendarDate(FromAstronomical(year), month, day);
    }

    // Converte una data completa tra giuliano e gregoriano; le date senza giorno restano invariate.
    public static CalendarDate Convert(CalendarDate date, string from, string to)
        => from == to || !date.IsComplete ? date : FromDayNumber(ToDayNumber(date, from), to);

    public static string WeekDay(CalendarDate date, string calendar)
        => date.IsComplete ? DayNames[(int)Mod(ToDayNumber(date, calendar) + 1, 7)] : string.Empty;

    // Quanti anni aggiungere all'anno scritto in uno stile per ottenere l'anno moderno; null se il mese non basta a dirlo.
    public static int? StyleShift(CalendarDate date, string? style)
    {
        var (month, day) = (date.Month, date.Day);
        return (style ?? string.Empty) switch
        {
            StileComune => 0,
            _ when month is null => null,
            StileFiorentino => month < 3 ? 1 : month > 3 ? 0 : day is null ? null : day < 25 ? 1 : 0,
            StilePisano => month < 3 ? 0 : month > 3 ? -1 : day is null ? null : day < 25 ? 0 : -1,
            StileVeneto => month < 3 ? 1 : 0,
            StileNativita => month < 12 ? 0 : day is null ? null : day >= 25 ? -1 : 0,
            StileBizantino => month >= 9 ? -1 : 0,
            _ => 0,
        };
    }

    // Data scritta in uno stile → stessa data con l'anno moderno (che inizia il 1° gennaio).
    public static CalendarDate ToModern(CalendarDate date, string? style)
        => StyleShift(date, style) is int shift ? date with { Year = AddYears(date.Year, shift) } : date;

    // Data con l'anno moderno → anno come sarebbe stato scritto nello stile indicato.
    public static CalendarDate FromModern(CalendarDate date, string? style)
        => StyleShift(date, style) is int shift ? date with { Year = AddYears(date.Year, -shift) } : date;

    // Anno moderno per ordinare: tiene conto dello stile; se la data non si riconosce usa HistoricalYear.
    public static int? ModernYear(string? text, string? style)
    {
        if (!string.IsNullOrEmpty(style) && TryParse(text, out var date) && StyleShift(date, style) is int shift)
        {
            return AddYears(date.Year, shift);
        }

        return HistoricalYear.Parse(text);
    }

    public static string Format(CalendarDate date)
    {
        var year = date.Year < 0 ? $"{-date.Year} a.C." : date.Year < 100 ? $"{date.Year} d.C." : date.Year.ToString(CultureInfo.InvariantCulture);
        return date switch
        {
            { Month: int m, Day: int d } => $"{(d == 1 ? "1°" : d.ToString(CultureInfo.InvariantCulture))} {MonthNames[m - 1]} {year}",
            { Month: int m } => $"{MonthNames[m - 1]} {year}",
            _ => year,
        };
    }

    // Indicazione breve da affiancare a una data: «giuliano, stile fiorentino».
    public static string Suffix(string? calendar, string? style)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(calendar))
        {
            parts.Add(CalendarInfo(calendar).Codice);
        }

        if (!string.IsNullOrEmpty(style))
        {
            parts.Add(StyleInfo(style).Codice switch
            {
                StileFiorentino => "stile fiorentino",
                StilePisano => "stile pisano",
                StileVeneto => "more veneto",
                StileNativita => "stile della Natività",
                StileBizantino => "stile bizantino",
                _ => string.Empty,
            });
        }

        return string.Join(", ", parts);
    }

    // Spiega come si legge oggi una data scritta in un calendario e in uno stile; vuoto se non c'è nulla da dire.
    public static string Describe(string? text, string? calendar, string? style)
    {
        calendar ??= NonIndicato;
        style ??= StileComune;
        if ((calendar.Length == 0 && style.Length == 0) || string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (!TryParse(text, out var written))
        {
            return "Data non riconosciuta: per la conversione scrivi ad esempio «10 febbraio 1525».";
        }

        if (!IsValid(written, calendar))
        {
            return $"Il giorno {written.Day} non esiste in {MonthNames[written.Month!.Value - 1]} {Format(new CalendarDate(written.Year))} nel calendario {(calendar == Gregoriano ? "gregoriano" : "giuliano")}.";
        }

        var parts = new List<string>();
        var modern = written;
        if (style.Length > 0)
        {
            if (StyleShift(written, style) is int shift)
            {
                modern = ToModern(written, style);
                parts.Add(shift == 0 ? $"Anno moderno: {Format(new CalendarDate(modern.Year))} (invariato)" : $"Anno moderno: {Format(modern)}");
            }
            else
            {
                parts.Add("Anno moderno incerto: indica giorno e mese");
            }
        }

        if (calendar.Length > 0 && modern.IsComplete)
        {
            var other = calendar == Giuliano ? Gregoriano : Giuliano;
            var converted = Convert(modern, calendar, other);
            var proleptic = calendar == Gregoriano && ToDayNumber(modern, Gregoriano) < ToDayNumber(GregorianReform, Gregoriano)
                ? " (la data gregoriana precede la riforma del 1582)"
                : string.Empty;
            parts.Add($"nel calendario {other}: {Format(converted)}, {WeekDay(converted, other)}{proleptic}");
        }
        else if (calendar.Length > 0 && style.Length == 0)
        {
            parts.Add("per convertire tra giuliano e gregoriano servono giorno e mese");
        }

        var description = string.Join(" · ", parts);
        return description.Length == 0 ? string.Empty : char.ToUpperInvariant(description[0]) + description[1..] + ".";
    }

    // Tutte le letture di una data: nei due calendari e negli stili usati in Italia, con il giorno della settimana.
    public static IReadOnlyList<CalendarConversion> ConvertAll(CalendarDate written, string calendar, string? style)
    {
        var source = calendar == Gregoriano ? Gregoriano : Giuliano;
        var modern = ToModern(written, style);
        var julian = Convert(modern, source, Giuliano);
        var gregorian = Convert(modern, source, Gregoriano);
        var rows = new List<CalendarConversion>
        {
            new("Giuliano, anno dal 1° gennaio", Format(julian), WeekDay(julian, Giuliano)),
            new("Gregoriano, anno dal 1° gennaio", Format(gregorian), WeekDay(gregorian, Gregoriano)),
        };

        // Gli stili si applicano alla data nel calendario d'origine: Firenze, ad esempio, li usò anche dopo il 1582.
        var basis = source == Gregoriano ? gregorian : julian;
        var calendarName = source == Gregoriano ? "Gregoriano" : "Giuliano";
        foreach (var option in Stili.Skip(1))
        {
            var styled = FromModern(basis, option.Codice);
            var text = StyleShift(basis, option.Codice) is null ? $"{Format(styled)} (anno incerto)" : Format(styled);
            rows.Add(new($"{calendarName}, stile {option.Etichetta}", text, WeekDay(basis, source)));
        }

        return rows;
    }

    // Vero se nel paese indicato, in quella data, era ancora in vigore il calendario giuliano.
    public static bool UsesJulian(GregorianAdoption adoption, CalendarDate gregorianDate)
        => gregorianDate.IsComplete
            ? ToDayNumber(gregorianDate, Gregoriano) < ToDayNumber(adoption.PrimoGiorno, Gregoriano)
            : gregorianDate.Year < adoption.Anno;

    private static int AddYears(int year, int delta)
        => delta == 0 ? year : FromAstronomical(ToAstronomical(year) + delta);

    private static int ToAstronomical(int year) => year < 0 ? year + 1 : year;

    private static int FromAstronomical(int year) => year <= 0 ? year - 1 : year;

    private static long Mod(long value, long divisor) => ((value % divisor) + divisor) % divisor;

    private const string Era = @"(?:\s*(?<bc>a\.?\s*C\.?)(?!\p{L})|\s*d\.?\s*C\.?(?!\p{L}))?";

    [GeneratedRegex(@"(?<!\d)(?:(?<d>\d{1,2})[/.])?(?<m>\d{1,2})[/.](?<y>\d{1,4})(?!\d)" + Era, RegexOptions.IgnoreCase)]
    private static partial Regex NumericDatePattern();

    [GeneratedRegex(@"(?:(?<!\d)(?<d>\d{1,2})\s*[°º]?\s+)?\b(?<mn>genn(?:aio)?|gen|febb(?:raio)?|feb|marzo|mar|aprile|apr|maggio|mag|giugno|giu|luglio|lug|agosto|ago|settembre|sett?|ottobre|ott|novembre|nov|dicembre|dic)\.?\s+(?<y>\d{1,4})(?!\d)" + Era, RegexOptions.IgnoreCase)]
    private static partial Regex TextDatePattern();

    [GeneratedRegex(@"\banni\b|\bsec(?:olo|\.)|\bmillennio\b", RegexOptions.IgnoreCase)]
    private static partial Regex CenturyOrDecadePattern();
}
