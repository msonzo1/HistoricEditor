using System.Globalization;
using System.Text;

namespace HistoricEditor;

public enum ConsensusLevel
{
    Hidden,
    Green,
    Orange,
    Red,
}

public sealed record WikipediaConsensusResult(ConsensusLevel Level, int DifferingCount, string Summary, string Details)
{
    public IReadOnlyList<ConsensusEdition> Editions { get; init; } = [];
}

// Esito del confronto per una singola edizione linguistica, con il collegamento alla pagina usata.
public sealed record ConsensusEdition(string LanguageName, string Title, string Url, bool Agrees, string Birth, string Death, IReadOnlyList<string> Differences)
{
    public string DifferencesText => Differences.Count == 0 ? "Concorda con la maggioranza." : "Diverso: " + string.Join(", ", Differences) + ".";
}

// Luogo Wikidata con cui riconoscere il testo letto dalle introduzioni: nomi del luogo e del comune che lo contiene.
public sealed record LeadPlaceCandidate(string WikidataId, IReadOnlyCollection<string> Names, IReadOnlyCollection<string> ParentNames);

// Confronta anni e luoghi di nascita/morte letti dalle introduzioni delle varie edizioni di Wikipedia.
public static class WikipediaConsensus
{
    private const string Separator = "\u00a0·\u00a0";

    // Chiave confrontabile di un luogo: il Q-id se il testo nomina un luogo Wikidata noto, altrimenti il testo normalizzato.
    public static string PlaceKey(string? text, IEnumerable<LeadPlaceCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Normalize(text);
        var padded = $" {normalized} ";
        var list = candidates.ToList();
        foreach (var candidate in list)
        {
            if (candidate.Names.Select(Normalize).Any(name => name.Length > 0 && padded.Contains($" {name} ", StringComparison.Ordinal)))
            {
                return candidate.WikidataId;
            }
        }

        // «Vinci» per la frazione Anchiano, «Santa Elena» per Longwood: il testo comincia proprio con una suddivisione che contiene il luogo.
        // Conta solo l'inizio della parte principale: in «Firenze, Toscana» o «Firenze, vicino a Vinci» il resto non rende Firenze uguale a Vinci.
        var main = Normalize(text.Split(',', '(')[0]);
        foreach (var candidate in list)
        {
            if (main.Length > 0 && candidate.ParentNames.Select(Normalize).Any(name => name.Length > 0 && (main == name || main.StartsWith(name + " ", StringComparison.Ordinal))))
            {
                return candidate.WikidataId;
            }
        }

        return Normalize(text.Split(',')[0]);
    }

    public static WikipediaConsensusResult Evaluate(IReadOnlyList<WikipediaLeadRecord>? leads)
    {
        if (leads is null || leads.Count == 0)
        {
            return new WikipediaConsensusResult(ConsensusLevel.Hidden, 0, string.Empty, string.Empty);
        }

        var fields = new (string Label, Func<WikipediaLeadRecord, string> Value)[]
        {
            ("anno di nascita", l => l.BirthYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            ("luogo di nascita", l => l.BirthPlaceKey),
            ("anno di morte", l => l.DeathYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            ("luogo di morte", l => l.DeathPlaceKey),
        };

        // Valore di maggioranza per ogni campo presente in almeno due edizioni; a parità vince l'edizione elencata prima.
        var majorities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, value) in fields)
        {
            var values = leads.Select(value).Where(v => v.Length > 0).ToList();
            if (values.Count < 2)
            {
                continue;
            }

            majorities[label] = values
                .GroupBy(v => v, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => values.IndexOf(g.Key))
                .First().Key;
        }

        // Giorno e mese si confrontano tra le edizioni che indicano l'anno di maggioranza.
        var birthDay = DayMajority(leads, majorities.GetValueOrDefault("anno di nascita"), l => (l.BirthYear, l.BirthMonth, l.BirthDay));
        var deathDay = DayMajority(leads, majorities.GetValueOrDefault("anno di morte"), l => (l.DeathYear, l.DeathMonth, l.DeathDay));
        if (majorities.Count == 0)
        {
            return new WikipediaConsensusResult(ConsensusLevel.Hidden, 0, string.Empty, string.Empty);
        }

        var lines = new List<string>
        {
            "Informazioni ottenute da: " + string.Join(", ", leads.Select(l => $"Wikipedia in {l.LanguageName}")) + ".",
            string.Empty,
        };
        var differing = 0;
        var editions = new List<ConsensusEdition>();
        foreach (var lead in leads)
        {
            var differences = fields
                .Where(f => majorities.TryGetValue(f.Label, out var majority) && f.Value(lead) is { Length: > 0 } v && v != majority)
                .Select(f => f.Label)
                .ToList();
            if (DiffersFromDay(birthDay, majorities.GetValueOrDefault("anno di nascita"), lead.BirthYear, lead.BirthMonth, lead.BirthDay))
            {
                differences.Insert(0, "giorno di nascita");
            }

            if (DiffersFromDay(deathDay, majorities.GetValueOrDefault("anno di morte"), lead.DeathYear, lead.DeathMonth, lead.DeathDay))
            {
                differences.Add("giorno di morte");
            }
            if (differences.Count > 0)
            {
                differing++;
            }

            var birth = Describe(lead.BirthYear, lead.BirthMonth, lead.BirthDay, lead.BirthPlace);
            var death = Describe(lead.DeathYear, lead.DeathMonth, lead.DeathDay, lead.DeathPlace);
            editions.Add(new ConsensusEdition(lead.LanguageName, lead.Title, LeadUrl(lead), differences.Count == 0, birth, death, differences));
            var line = $"{(differences.Count > 0 ? "✗" : "✓")} {lead.LanguageName}: nascita {birth}{Separator}morte {death}";
            if (differences.Count > 0)
            {
                line += $" (diverso: {string.Join(", ", differences)})";
            }

            lines.Add(line);
        }

        var level = differing switch
        {
            0 => ConsensusLevel.Green,
            <= 2 => ConsensusLevel.Orange,
            _ => ConsensusLevel.Red,
        };
        var summary = differing == 0
            ? "Le edizioni di Wikipedia concordano su date e luoghi di nascita e morte."
            : differing == 1
                ? "1 edizione di Wikipedia ha date o luoghi diversi dalla maggioranza."
                : $"{differing} edizioni di Wikipedia hanno date o luoghi diversi dalla maggioranza.";
        lines.Insert(0, summary);
        return new WikipediaConsensusResult(level, differing, summary, string.Join(Environment.NewLine, lines)) { Editions = editions };
    }

    // Indirizzo salvato o, per i dati più vecchi che non lo hanno, ricostruito da lingua e titolo.
    internal static string LeadUrl(WikipediaLeadRecord lead)
    {
        if (Uri.TryCreate(lead.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri.AbsoluteUri;
        }

        return lead.Language.Length > 0 && lead.Title.Length > 0
            ? $"https://{Uri.EscapeDataString(lead.Language)}.wikipedia.org/wiki/{Uri.EscapeDataString(lead.Title.Replace(' ', '_'))}"
            : string.Empty;
    }

    private static readonly string[] ItalianMonths =
        ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];

    // Mese (e giorno) di maggioranza tra le edizioni con l'anno di maggioranza; serve che almeno due lo indichino.
    private static (int? Month, int? Day) DayMajority(
        IReadOnlyList<WikipediaLeadRecord> leads,
        string? majorityYear,
        Func<WikipediaLeadRecord, (int? Year, int? Month, int? Day)> select)
    {
        if (majorityYear is null)
        {
            return (null, null);
        }

        var dates = leads.Select(select)
            .Where(d => d.Year?.ToString(CultureInfo.InvariantCulture) == majorityYear && d.Month is not null)
            .ToList();
        var month = Majority(dates.Select(d => d.Month!.Value).ToList());
        var day = month is null ? null : Majority(dates.Where(d => d.Month == month && d.Day is not null).Select(d => d.Day!.Value).ToList());
        return (month, day);
    }

    private static int? Majority(List<int> values)
        => values.Count < 2
            ? null
            : values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => values.IndexOf(g.Key)).First().Key;

    // Diverso solo se l'anno coincide con la maggioranza (altrimenti è già segnalato l'anno) e mese o giorno differiscono.
    private static bool DiffersFromDay((int? Month, int? Day) majority, string? majorityYear, int? year, int? month, int? day)
        => majority.Month is int majorityMonth
            && year?.ToString(CultureInfo.InvariantCulture) == majorityYear
            && month is int m
            && (m != majorityMonth || (majority.Day is int majorityDay && day is int d && d != majorityDay));

    private static string Describe(int? year, int? month, int? day, string place)
    {
        var dateText = year is int y ? WikipediaImportService.FormatYear(Math.Abs(y), y < 0) : "—";
        if (year is not null && month is >= 1 and <= 12)
        {
            dateText = $"{(day is int dd ? $"{dd} " : string.Empty)}{ItalianMonths[month.Value - 1]} {dateText}";
        }

        return place.Length > 0 ? $"{dateText}, {place}" : $"{dateText}, luogo non indicato";
    }

    // Minuscolo, senza diacritici, solo lettere e cifre separate da uno spazio.
    internal static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space && builder.Length > 0)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
