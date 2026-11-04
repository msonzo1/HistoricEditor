using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Un periodo di regno o di carica letto dall'infobox: pagine collegate nel nome del titolo e date già normalizzate
// nel formato dei titoli («28/06/1519», «06/1519», «1519»).
internal sealed record InfoboxReign(IReadOnlyList<string> Links, string Start, string End);

// Legge i periodi di regno dalle infobox delle edizioni importate (de.wikipedia non usa infobox per i sovrani).
internal static partial class InfoboxReignParser
{
    // Parametri per template: nome del titolo, periodo in un solo campo oppure inizio e fine separati.
    // Il numero del titolo (vuoto, 1, 2…) segue il nome: «titolo2», «inizio regno2», «à partir du fonction2».
    private static readonly (string Title, string? Range, string? Start, string? End)[] Schemes =
    [
        ("titolo", null, "inizio regno", "fine regno"),                       // it: Sovrano
        ("succession", "reign", null, null),                                  // en: Infobox royalty
        ("office", null, "term_start", "term_end"),                           // en: Infobox officeholder
        ("fonction", null, "à partir du fonction", "jusqu'au fonction"),      // fr: Infobox Biographie2
        ("título", "reinado", null, null),                                    // es: Ficha de noble
        ("cargo", null, "inicio", "final"),                                   // es: Ficha de persona
        ("functie", "regeerperiode", null, null),                             // nl: Infobox heerser
    ];

    private static readonly Dictionary<string, int> Months = BuildMonths();

    public static List<InfoboxReign> Parse(string? wikitext)
    {
        var result = new List<InfoboxReign>();
        if (string.IsNullOrWhiteSpace(wikitext))
        {
            return result;
        }

        var parameters = ReadParameters(wikitext);
        foreach (var (titleName, range, startName, endName) in Schemes)
        {
            var pattern = new Regex($@"^{Regex.Escape(titleName)}\s*(?<n>\d*)$", RegexOptions.CultureInvariant);
            foreach (var (name, value) in parameters)
            {
                var match = pattern.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                var n = match.Groups["n"].Value;
                string start, end;
                if (range is not null)
                {
                    (start, end) = ParseRange(Lookup(parameters, range, n));
                }
                else
                {
                    start = FirstDate(Lookup(parameters, startName!, n)) ?? string.Empty;
                    end = FirstDate(Lookup(parameters, endName!, n)) ?? string.Empty;
                }

                var links = ReadLinks(value);
                if ((start.Length > 0 || end.Length > 0) && links.Count > 0)
                {
                    result.Add(new InfoboxReign(links, start, end));
                }
            }
        }

        return result;
    }

    // «28 June 1519 –<br/>3 August 1556/24 February 1558» → inizio e fine: la prima data e quella successiva.
    internal static (string Start, string End) ParseRange(string? text)
    {
        var dates = Dates(text).Take(2).ToList();
        return (dates.ElementAtOrDefault(0) ?? string.Empty, dates.ElementAtOrDefault(1) ?? string.Empty);
    }

    internal static string? FirstDate(string? text) => Dates(text).FirstOrDefault();

    private static IEnumerable<string> Dates(string? text)
    {
        var clean = CleanValue(text ?? string.Empty);
        foreach (Match match in DatePattern().Matches(clean))
        {
            if (ToDate(match) is { } date)
            {
                yield return Format(date);
            }
        }
    }

    private static TitleDate? ToDate(Match match)
    {
        if (!int.TryParse(match.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1 or > 2100)
        {
            return null;
        }

        int? month = match.Groups["m"].Success && Months.TryGetValue(match.Groups["m"].Value.ToLowerInvariant(), out var named) ? named
            : match.Groups["mn"].Success ? int.Parse(match.Groups["mn"].Value, CultureInfo.InvariantCulture)
            : null;
        int? day = match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : null;
        if (month is < 1 or > 12 || day is < 1 or > 31 || (day is not null && month is null))
        {
            return null;
        }

        return new TitleDate(match.Groups["bc"].Success ? -year : year, month, day);
    }

    internal static string Format(TitleDate date)
    {
        var year = WikipediaImportService.FormatYear(Math.Abs(date.Year), date.Year < 0);
        return date switch
        {
            { Month: { } m, Day: { } d } => $"{d:00}/{m:00}/{year}",
            { Month: { } m } => $"{m:00}/{year}",
            _ => year,
        };
    }

    // Template che contengono la data vengono sostituiti dal loro testo, gli altri (note, età…) eliminati.
    private static string CleanValue(string value)
    {
        var text = Regex.Replace(value, @"<ref[^>/]*/>|<ref[^>]*>.*?</ref>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase).Replace("&nbsp;", " ").Replace("&ndash;", "–");
        string previous;
        do
        {
            previous = text;
            text = InnerTemplate().Replace(text, ExpandTemplate);
        }
        while (text != previous);
        // Un template non chiuso (nota che prosegue sulle righe successive) si scarta fino alla fine.
        text = Regex.Replace(text, @"\{\{.*", string.Empty, RegexOptions.Singleline);
        return WikipediaImportService.CleanWikitext(text);
    }

    private static string ExpandTemplate(Match match)
    {
        var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
        var args = match.Groups["args"].Success
            ? match.Groups["args"].Value.Split('|').Select(a => a.Trim()).Where(a => !a.Contains('=')).ToArray()
            : [];
        return name switch
        {
            // es {{fecha|14|03|1516}}
            "fecha" when args.Length >= 3 => $"{args[0]}/{args[1]}/{args[2]}",
            // fr {{date|24|février|1530}} o {{date|28 juin 1519}}
            "date" when args.Length >= 3 => $"{args[0]} {args[1]} {args[2]}",
            "date" or "nowrap" or "nobr" or "small" or "lang" when args.Length >= 1 => args[^1],
            // fr {{1er}} janvier
            "1er" => "1",
            _ => string.Empty,
        };
    }

    // Parametri di primo livello dell'infobox («| nome = valore»), anche su più righe; vale la prima occorrenza.
    private static List<(string Name, string Value)> ReadParameters(string wikitext)
    {
        var text = Regex.Replace(wikitext, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var result = new List<(string Name, string Value)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;
        var value = new System.Text.StringBuilder();
        void Flush()
        {
            if (current is not null && seen.Add(current))
            {
                result.Add((current, value.ToString()));
            }
        }

        foreach (var line in text.Split('\n'))
        {
            var match = ParameterLine().Match(line);
            if (match.Success)
            {
                Flush();
                current = Regex.Replace(match.Groups["name"].Value.Trim().ToLowerInvariant(), @"\s+", " ");
                value.Clear().Append(match.Groups["value"].Value);
            }
            else if (current is not null)
            {
                value.Append('\n').Append(line);
            }
        }

        Flush();
        return result;
    }

    private static string Lookup(List<(string Name, string Value)> parameters, string name, string n)
    {
        foreach (var key in new[] { name + n, $"{name} {n}".TrimEnd() })
        {
            var found = parameters.FirstOrDefault(p => p.Name == key);
            if (found.Name is not null)
            {
                return found.Value;
            }
        }

        return string.Empty;
    }

    private static readonly HashSet<string> SkippedNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "image", "immagine", "datei", "bild", "fichier", "archivo", "imagen", "bestand", "afbeelding",
        "category", "categoria", "kategorie", "catégorie", "categoría", "categorie",
    };

    // Destinazioni dei collegamenti nel nome del titolo, senza immagini e categorie né l'eventuale sezione.
    private static List<string> ReadLinks(string value)
    {
        var links = new List<string>();
        foreach (Match match in WikiLink().Matches(value))
        {
            var target = match.Groups["t"].Value.Replace('_', ' ').Trim();
            var colon = target.IndexOf(':');
            if (target.Length == 0 || (colon > 0 && SkippedNamespaces.Contains(target[..colon].Trim())))
            {
                continue;
            }

            if (!links.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                links.Add(target);
            }
        }

        return links;
    }

    private static Dictionary<string, int> BuildMonths()
    {
        var months = new Dictionary<string, int>(StringComparer.Ordinal);
        string[][] names =
        [
            ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"],
            ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"],
            ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"],
            ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
            ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
            ["januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december"],
        ];
        foreach (var language in names)
        {
            for (var i = 0; i < 12; i++)
            {
                months.TryAdd(language[i], i + 1);
            }
        }

        months.TryAdd("sept", 9);
        months.TryAdd("setiembre", 9);
        return months;
    }

    private const string MonthNames =
        "gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre"
        + "|january|february|march|april|may|june|july|august|september|october|november|december"
        + "|janvier|février|mars|avril|mai|juin|juillet|août|septembre|octobre|décembre"
        + "|enero|febrero|abril|mayo|junio|julio|septiembre|setiembre|octubre|diciembre"
        + "|januari|februari|maart|mei|juni|juli|augustus|oktober"
        + "|jan|feb|mar|apr|jun|jul|aug|sept|sep|oct|nov|dec";

    // Giorno mese anno («28 giugno 1519», «28 de junio de 1519», «1er janvier 1500»), mese giorno, anno («June 28, 1519»),
    // gg/mm/aaaa, mm/aaaa, mese anno, anno; con l'eventuale indicazione avanti Cristo.
    [GeneratedRegex(
        @"(?<![\p{L}\d])(?:"
        + @"(?<d>\d{1,2})(?:er|º|°)?\s+(?:de\s+)?(?<m>" + MonthNames + @")\.?,?\s+(?:de\s+)?(?<y>\d{1,4})"
        + @"|(?<m>" + MonthNames + @")\.?\s+(?<d>\d{1,2}),?\s+(?<y>\d{1,4})"
        + @"|(?<d>\d{1,2})/(?<mn>\d{1,2})/(?<y>\d{1,4})"
        + @"|(?<mn>\d{1,2})/(?<y>\d{3,4})"
        + @"|(?<m>" + MonthNames + @")\.?\s+(?:de\s+)?(?<y>\d{3,4})"
        + @"|(?<y>\d{3,4})"
        + @"|(?<y>\d{1,2})(?=\s*(?:a\.\s?C|BCE?\b|B\.C|av\.\s?J|a\.\s?de\s?C|v\.\s?Chr))"
        + @")(?!\d)(?:\s*(?<bc>a\.\s?C\.|BCE?\b|B\.C\.|av\.\s?J\.?-C\.|a\.\s?de\s?C\.|v\.\s?Chr\.))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\{\{\s*(?<name>[^|{}]+?)\s*(?:\|(?<args>[^{}]*))?\}\}")]
    private static partial Regex InnerTemplate();

    [GeneratedRegex(@"^\s*\|\s*(?<name>[^=|{}\[\]\n]+?)\s*=(?<value>.*)$")]
    private static partial Regex ParameterLine();

    [GeneratedRegex(@"\[\[(?<t>[^|\]#\[]+)(?:#[^|\]]*)?(?:\|[^\]]*)?\]\]")]
    private static partial Regex WikiLink();
}
