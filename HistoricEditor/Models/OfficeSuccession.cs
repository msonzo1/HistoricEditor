using System.Globalization;
using System.Text;

namespace HistoricEditor;

// Un mandato: la carica ricoperta da un personaggio dell'archivio in un periodo.
public sealed record OfficeTenure(int PersonId, string Persona, string PersonWikidataId, PersonTitle Titolo, int? AnnoInizio, int? AnnoFine)
{
    public string Periodo => (Titolo.Inizio.Trim(), Titolo.Fine.Trim()) switch
    {
        ("", "") => "date sconosciute",
        (var a, "") => $"dal {a}",
        ("", var b) => $"fino al {b}",
        (var a, var b) => $"{a} – {b}",
    };
}

// Riga della cronologia: il mandato con predecessore e successore e le eventuali segnalazioni.
public sealed record OfficeRow(int Posizione, OfficeTenure Mandato, string Predecessore, string Successore, IReadOnlyList<string> Note)
{
    public string Titolare => Mandato.Titolo.Numerazione.Trim().Length > 0 ? $"{Mandato.Persona} ({Mandato.Titolo.Numerazione.Trim()})" : Mandato.Persona;
    public string Inizio => Join(Mandato.Titolo.Inizio, Mandato.Titolo.CausaInizio);
    public string Fine => Join(Mandato.Titolo.Fine, Mandato.Titolo.CausaFine);
    public string NoteTesto => string.Join(" · ", Note);

    public string Etichetta => $"{Posizione}. {Titolare}, {Mandato.Periodo}"
        + $". Predecessore: {Predecessore}. Successore: {Successore}" + (Note.Count > 0 ? $". {NoteTesto}" : string.Empty);

    private static string Join(string date, string cause)
        => (date.Trim(), cause.Trim()) switch
        {
            ("", "") => "—",
            (var d, "") => d,
            ("", var c) => c,
            (var d, var c) => $"{d} ({c})",
        };
}

// Persona indicata da Wikidata come predecessore o successore ma senza questa carica in archivio.
public sealed record OfficeMissingHolder(string Nome, string WikidataId, int? PersonId)
{
    public string Etichetta => (WikidataId.Length > 0 ? $"{Nome} ({WikidataId})" : Nome)
        + (PersonId is null ? " · non in archivio" : " · in archivio, senza questa carica");
}

public sealed record OfficeChronology(string Key, string Nome, string WikidataId, IReadOnlyList<OfficeRow> Righe, IReadOnlyList<OfficeMissingHolder> Mancanti)
{
    public int? AnnoInizio => Righe.Select(r => r.Mandato.AnnoInizio ?? r.Mandato.AnnoFine).Where(y => y is not null).Min();
    public int? AnnoFine => Righe.Select(r => r.Mandato.AnnoFine ?? r.Mandato.AnnoInizio).Where(y => y is not null).Max();

    public string Riepilogo
    {
        get
        {
            var span = (AnnoInizio, AnnoFine) switch
            {
                (int a, int b) when a != b => $", {HistoricalYear.Format(a)}–{HistoricalYear.Format(b)}",
                (int a, _) => $", {HistoricalYear.Format(a)}",
                _ => string.Empty,
            };
            return $"{Righe.Count} {(Righe.Count == 1 ? "titolare" : "titolari")}{span}";
        }
    }

    public string Etichetta => $"{Nome} ({Riepilogo})";

    public override string ToString() => Etichetta;
}

// Ricostruisce la successione di ogni carica a partire dai titoli dei personaggi: ordine cronologico, vacanze,
// sovrapposizioni (coreggenze o pretendenti) e discordanze con predecessori/successori indicati da Wikidata.
public static class OfficeSuccession
{
    // Oltre questo numero di anni tra la fine di un mandato e l'inizio del successivo la carica si segnala come vacante.
    public const int GapYears = 1;

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    public static List<OfficeChronology> Build(IEnumerable<HistoricalPerson> people)
    {
        var peopleList = people.ToList();
        var byWikidata = peopleList.Where(p => !string.IsNullOrWhiteSpace(p.WikidataId))
            .GroupBy(p => p.WikidataId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = peopleList.GroupBy(p => Normalize(p.DisplayName)).ToDictionary(g => g.Key, g => g.ToList());

        var entries = peopleList
            .SelectMany(p => p.Titles.Where(t => !string.IsNullOrWhiteSpace(t.Titolo)).Select(t => (Person: p, Title: t)))
            .ToList();

        // Le cariche con codice Wikidata si riconoscono dal codice; i titoli scritti a mano si uniscono a una carica con lo stesso nome.
        var keyByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, title) in entries.Where(e => e.Title.WikidataId.Trim().Length > 0))
        {
            keyByName.TryAdd(Normalize(title.Titolo), title.WikidataId.Trim().ToUpperInvariant());
        }

        string KeyOf(PersonTitle t)
            => t.WikidataId.Trim().Length > 0
                ? t.WikidataId.Trim().ToUpperInvariant()
                : keyByName.GetValueOrDefault(Normalize(t.Titolo), "t:" + Normalize(t.Titolo));

        var result = new List<OfficeChronology>();
        foreach (var group in entries.GroupBy(e => KeyOf(e.Title), StringComparer.Ordinal))
        {
            var tenures = group
                .Select(e => new OfficeTenure(e.Person.Id, e.Person.DisplayName, e.Person.WikidataId.Trim(), e.Title,
                    HistoricalYear.Parse(e.Title.Inizio), HistoricalYear.Parse(e.Title.Fine)))
                .OrderBy(t => t.AnnoInizio ?? t.AnnoFine ?? int.MaxValue)
                .ThenBy(t => t.AnnoFine ?? int.MaxValue)
                .ThenBy(t => t.Persona, Comparer)
                .ToList();
            var holders = tenures.Select(t => t.PersonId).ToHashSet();
            var rows = new List<OfficeRow>();
            var missing = new List<OfficeMissingHolder>();

            void NoteMissing(string nome, string wikidataId)
            {
                if (nome.Trim().Length == 0 && wikidataId.Trim().Length == 0)
                {
                    return;
                }

                var person = Find(nome, wikidataId, byWikidata, byName);
                if (person is not null && holders.Contains(person.Id))
                {
                    return;
                }

                if (!missing.Any(m => (wikidataId.Length > 0 && string.Equals(m.WikidataId, wikidataId, StringComparison.OrdinalIgnoreCase))
                    || Normalize(m.Nome) == Normalize(nome)))
                {
                    missing.Add(new OfficeMissingHolder(nome.Trim().Length > 0 ? nome.Trim() : wikidataId.Trim(), wikidataId.Trim(), person?.Id));
                }
            }

            for (var i = 0; i < tenures.Count; i++)
            {
                var current = tenures[i];
                var previous = i > 0 ? tenures[i - 1] : null;
                var next = i < tenures.Count - 1 ? tenures[i + 1] : null;
                var notes = new List<string>();

                if (previous is not null && previous.PersonId != current.PersonId
                    && previous.AnnoFine is int prevEnd && current.AnnoInizio is int start)
                {
                    if (start - prevEnd > GapYears)
                    {
                        notes.Add($"Carica vacante tra il {HistoricalYear.Format(prevEnd)} e il {HistoricalYear.Format(start)}");
                    }
                    else if (start < prevEnd)
                    {
                        notes.Add($"Periodo sovrapposto a {previous.Persona} ({HistoricalYear.Format(start)}–{HistoricalYear.Format(prevEnd)})");
                    }
                }

                if (current.AnnoInizio is null && current.AnnoFine is null)
                {
                    notes.Add("Date mancanti: posizione incerta");
                }

                var declaredPrev = current.Titolo.Predecessore.Trim();
                var declaredNext = current.Titolo.Successore.Trim();
                if (previous is not null && declaredPrev.Length > 0 && !Matches(declaredPrev, current.Titolo.PredecessoreWikidataId, previous))
                {
                    notes.Add($"Predecessore indicato: {declaredPrev}");
                }

                if (next is not null && declaredNext.Length > 0 && !Matches(declaredNext, current.Titolo.SuccessoreWikidataId, next))
                {
                    notes.Add($"Successore indicato: {declaredNext}");
                }

                NoteMissing(declaredPrev, current.Titolo.PredecessoreWikidataId);
                NoteMissing(declaredNext, current.Titolo.SuccessoreWikidataId);

                rows.Add(new OfficeRow(
                    i + 1,
                    current,
                    previous?.Persona ?? (declaredPrev.Length > 0 ? $"{declaredPrev} (non in cronologia)" : "—"),
                    next?.Persona ?? (declaredNext.Length > 0 ? $"{declaredNext} (non in cronologia)" : "—"),
                    notes));
            }

            var nome = group.GroupBy(e => e.Title.Titolo.Trim(), Comparer)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, Comparer)
                .First().Key;
            var wikidataId = group.Key.StartsWith("t:", StringComparison.Ordinal) ? string.Empty : group.Key;
            result.Add(new OfficeChronology(group.Key, nome, wikidataId, rows, missing));
        }

        return [.. result.OrderBy(o => o.Nome, Comparer).ThenBy(o => o.Key, StringComparer.Ordinal)];
    }

    // Vero se il nome/codice indicato corrisponde al titolare: con entrambi i codici Wikidata si confrontano solo quelli.
    private static bool Matches(string nome, string wikidataId, OfficeTenure tenure)
    {
        if (wikidataId.Trim().Length > 0 && tenure.PersonWikidataId.Length > 0)
        {
            return string.Equals(wikidataId.Trim(), tenure.PersonWikidataId, StringComparison.OrdinalIgnoreCase);
        }

        var a = Normalize(nome);
        var b = Normalize(tenure.Persona);
        return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    private static HistoricalPerson? Find(string nome, string wikidataId, Dictionary<string, HistoricalPerson> byWikidata, Dictionary<string, List<HistoricalPerson>> byName)
    {
        if (wikidataId.Trim().Length > 0 && byWikidata.TryGetValue(wikidataId.Trim(), out var byId))
        {
            return byId;
        }

        return byName.TryGetValue(Normalize(nome), out var list) && list.Count == 1 ? list[0] : null;
    }

    internal static string Normalize(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Trim().Replace('’', '\'').ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(c);
        }

        return builder.ToString().TrimEnd();
    }
}
