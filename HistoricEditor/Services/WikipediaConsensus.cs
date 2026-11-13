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

    // Date discordanti tra le fonti della scheda (ipotesi e date scartate), una riga per nascita e morte.
    public IReadOnlyList<string> DateNotes { get; init; } = [];
}

// Esito del confronto per una singola edizione linguistica, con il collegamento alla pagina usata.
public sealed record ConsensusEdition(string LanguageName, string Title, string Url, bool Agrees, string Birth, string Death, IReadOnlyList<string> Differences)
{
    // Riga con i dati di Wikidata, mostrata in fondo all'elenco delle edizioni.
    public bool IsWikidata { get; init; }

    public string DifferencesText => Differences.Count == 0 ? "Concorda con la maggioranza." : "Diverso: " + string.Join(", ", Differences) + ".";
}

// Luogo Wikidata con cui riconoscere il testo letto dalle introduzioni: nomi del luogo e del comune che lo contiene.
// ParentIds: Q-id delle suddivisioni che lo contengono (es. Roma per il Teatro di Pompeo).
public sealed record LeadPlaceCandidate(string WikidataId, IReadOnlyCollection<string> Names, IReadOnlyCollection<string> ParentNames)
{
    public IReadOnlyCollection<string> ParentIds { get; init; } = [];
}

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

        // «Suburra, Rome»: la prima parte non è un luogo noto, ma una delle successive sì.
        foreach (var segment in text.Split(',', '(', ')').Skip(1).Select(Normalize).Where(s => s.Length > 0))
        {
            var match = list.FirstOrDefault(c => c.Names.Select(Normalize).Contains(segment));
            if (match is not null)
            {
                return match.WikidataId;
            }
        }

        return Normalize(text.Split(',')[0]);
    }

    // Un luogo contenuto in quello indicato da un'altra edizione non è un luogo diverso: «Theatre of Pompey, Rome»
    // e «Roma» indicano entrambi Roma, uno con più dettaglio. Ogni chiave passa al luogo più vicino che la contiene
    // tra quelli nominati dalle altre edizioni.
    public static IReadOnlyList<string> GeneralizeKeys(IReadOnlyList<string> keys, IEnumerable<LeadPlaceCandidate> candidates)
    {
        var parents = candidates
            .GroupBy(c => c.WikidataId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(c => c.ParentIds).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var present = keys.Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);

        string Ancestor(string key)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { key };
            var current = key;
            for (var step = 0; step < 8 && parents.TryGetValue(current, out var ids); step++)
            {
                // Il primo contenitore (nell'ordine delle suddivisioni) nominato da un'altra edizione; poi si risale da quello.
                var named = ids.FirstOrDefault(id => id != key && present.Contains(id));
                if (named is not null)
                {
                    current = named;
                    if (!visited.Add(current))
                    {
                        break;
                    }

                    continue;
                }

                break;
            }

            return current;
        }

        return [.. keys.Select(k => k.Length == 0 ? k : Ancestor(k))];
    }

    // Le edizioni di Wikipedia decidono la maggioranza; la riga di Wikidata (se salvata) si confronta con la maggioranza,
    // compare in fondo all'elenco e, se diversa, rende il LED almeno arancione.
    // Le date della scheda (con quelle scartate all'import) aggiungono al confronto le date discordanti tra le fonti:
    // più ipotesi nel campo o date indicate da una sola fonte rendono il LED almeno arancione.
    public static WikipediaConsensusResult Evaluate(
        IReadOnlyList<WikipediaLeadRecord>? allLeads,
        IReadOnlyList<HistoricalDateEvidence>? birthEvidence = null,
        IReadOnlyList<HistoricalDateEvidence>? deathEvidence = null)
    {
        var leads = allLeads?.Where(l => !l.IsWikidata).ToList() ?? [];
        var wikidata = allLeads?.FirstOrDefault(l => l.IsWikidata);
        if (leads.Count == 0)
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
        var birthDay = DayMajority(leads, majorities.GetValueOrDefault("anno di nascita"), l => (l.BirthYear, l.BirthMonth, l.BirthDay, l.BirthDayAlt));
        var deathDay = DayMajority(leads, majorities.GetValueOrDefault("anno di morte"), l => (l.DeathYear, l.DeathMonth, l.DeathDay, l.DeathDayAlt));
        if (majorities.Count == 0)
        {
            return new WikipediaConsensusResult(ConsensusLevel.Hidden, 0, string.Empty, string.Empty);
        }

        var lines = new List<string>
        {
            "Informazioni ottenute da: " + string.Join(", ", leads.Select(l => $"Wikipedia in {l.LanguageName}")) + (wikidata is null ? "." : ", Wikidata."),
            string.Empty,
        };
        var differing = 0;
        List<string>? wikidataDifferences = null;
        var editions = new List<ConsensusEdition>();
        foreach (var lead in wikidata is null ? leads : [.. leads, wikidata])
        {
            var differences = fields
                .Where(f => majorities.TryGetValue(f.Label, out var majority) && f.Value(lead) is { Length: > 0 } v && v != majority)
                .Select(f => f.Label)
                .ToList();
            if (DiffersFromDay(birthDay, majorities.GetValueOrDefault("anno di nascita"), lead.BirthYear, lead.BirthMonth, lead.BirthDay, lead.BirthDayAlt))
            {
                differences.Insert(0, "giorno di nascita");
            }

            if (DiffersFromDay(deathDay, majorities.GetValueOrDefault("anno di morte"), lead.DeathYear, lead.DeathMonth, lead.DeathDay, lead.DeathDayAlt))
            {
                differences.Add("giorno di morte");
            }

            if (lead.IsWikidata)
            {
                wikidataDifferences = differences;
            }
            else if (differences.Count > 0)
            {
                differing++;
            }

            var birth = Describe(lead.BirthYear, lead.BirthMonth, lead.BirthDay, lead.BirthDayAlt, lead.BirthPlace);
            var death = Describe(lead.DeathYear, lead.DeathMonth, lead.DeathDay, lead.DeathDayAlt, lead.DeathPlace);
            editions.Add(new ConsensusEdition(lead.LanguageName, lead.Title, LeadUrl(lead), differences.Count == 0, birth, death, differences) { IsWikidata = lead.IsWikidata });
            var source = lead.IsWikidata ? "Wikidata" : lead.LanguageName;
            var line = $"{(differences.Count > 0 ? "✗" : "✓")} {source}: nascita {birth}{Separator}morte {death}";
            if (differences.Count > 0)
            {
                line += $" (diverso: {string.Join(", ", differences)})";
            }

            lines.Add(line);
        }

        var wikidataDiffers = wikidataDifferences is { Count: > 0 };
        var level = differing switch
        {
            0 => wikidataDiffers ? ConsensusLevel.Orange : ConsensusLevel.Green,
            <= 2 => ConsensusLevel.Orange,
            _ => ConsensusLevel.Red,
        };
        var summary = differing == 0
            ? "Le edizioni di Wikipedia concordano su date e luoghi di nascita e morte."
            : differing == 1
                ? "1 edizione di Wikipedia ha date o luoghi diversi dalla maggioranza."
                : $"{differing} edizioni di Wikipedia hanno date o luoghi diversi dalla maggioranza.";
        if (wikidataDiffers)
        {
            summary += $" Wikidata è diverso dalla maggioranza: {string.Join(", ", wikidataDifferences!)}.";
        }

        var dateNotes = new[] { DateDiscrepancy.Describe("Nascita", birthEvidence ?? []), DateDiscrepancy.Describe("Morte", deathEvidence ?? []) }
            .Where(n => n.Length > 0)
            .ToList();
        if (dateNotes.Count > 0)
        {
            summary += " Le fonti indicano date diverse: vedi le date discordanti.";
            if (level == ConsensusLevel.Green)
            {
                level = ConsensusLevel.Orange;
            }

            lines.Add(string.Empty);
            lines.Add("Date discordanti tra le fonti:");
            lines.AddRange(dateNotes);
        }

        lines.Insert(0, summary);
        return new WikipediaConsensusResult(level, differing, summary, string.Join(Environment.NewLine, lines)) { Editions = editions, DateNotes = dateNotes };
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
        Func<WikipediaLeadRecord, (int? Year, int? Month, int? Day, int? Alt)> select)
    {
        if (majorityYear is null)
        {
            return (null, null);
        }

        var dates = leads.Select(select)
            .Where(d => d.Year?.ToString(CultureInfo.InvariantCulture) == majorityYear && d.Month is not null)
            .ToList();
        var month = Majority(dates.Select(d => d.Month!.Value).ToList());
        // «le 8 ou 9 août»: l'edizione vota per entrambi i giorni.
        var day = month is null ? null : Majority(dates.Where(d => d.Month == month && d.Day is not null)
            .SelectMany(d => d.Alt is int alt ? new[] { d.Day!.Value, alt } : [d.Day!.Value]).ToList());
        return (month, day);
    }

    private static int? Majority(List<int> values)
        => values.Count < 2
            ? null
            : values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => values.IndexOf(g.Key)).First().Key;

    // Diverso solo se l'anno coincide con la maggioranza (altrimenti è già segnalato l'anno) e mese o giorno differiscono.
    // Con due giorni («8 ou 9 août») è diverso solo se nessuno dei due è quello di maggioranza.
    private static bool DiffersFromDay((int? Month, int? Day) majority, string? majorityYear, int? year, int? month, int? day, int? alt = null)
        => majority.Month is int majorityMonth
            && year?.ToString(CultureInfo.InvariantCulture) == majorityYear
            && month is int m
            && (m != majorityMonth || (majority.Day is int majorityDay && day is int d && d != majorityDay && alt != majorityDay));

    private static string Describe(int? year, int? month, int? day, int? alt, string place)
    {
        var dateText = year is int y ? HistoricalYear.Format(y) : "—";
        if (year is not null && month is >= 1 and <= 12)
        {
            var days = day is int dd ? alt is int other ? $"{Math.Min(dd, other)} o {Math.Max(dd, other)} " : $"{dd} " : string.Empty;
            dateText = $"{days}{ItalianMonths[month.Value - 1]} {dateText}";
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
