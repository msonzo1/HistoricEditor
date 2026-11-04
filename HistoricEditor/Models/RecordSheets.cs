using System.Globalization;

namespace HistoricEditor;

// Persona collegata a un'opera, un evento o una dinastia nelle schede di lettura e nelle sezioni.
public sealed record SheetPersonLink(int Id, string Nome, string Dettaglio)
{
    public string Etichetta => Dettaglio.Length > 0 ? $"{Nome} — {Dettaglio}" : Nome;
}

// Riga di fonte: gli indirizzi web diventano collegamenti, il resto resta testo.
public sealed record SheetSource(string Testo, string? Url)
{
    public bool IsLink => Url is not null;
}

// Voce dei filtri a tendina delle sezioni: la prima («Tutti…») ha Id e Valore nulli.
public sealed record ChoiceOption(string Etichetta, int? Id = null, string? Valore = null)
{
    public override string ToString() => Etichetta;
}

// Testi comuni delle schede di lettura e dei filtri delle sezioni.
public static class RecordSheet
{
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("it-IT").CompareInfo;

    public static IReadOnlyList<SheetField> Fields(params (string Label, string? Value)[] values) =>
    [
        .. values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => new SheetField(v.Label, v.Value!.Trim())),
    ];

    // Vero se la ricerca è vuota o compare in uno dei testi (senza distinguere maiuscole e accenti).
    public static bool Matches(string? query, params string?[] texts)
    {
        var q = (query ?? string.Empty).Trim();
        return q.Length == 0 || texts.Any(t => !string.IsNullOrEmpty(t)
            && Compare.IndexOf(t, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    // Una fonte per riga, senza righe vuote né doppioni.
    public static IReadOnlyList<SheetSource> Sources(string? fonti)
    {
        var result = new List<SheetSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (fonti ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (!seen.Add(line))
            {
                continue;
            }

            var isLink = Uri.TryCreate(line, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            result.Add(new SheetSource(line, isLink ? uri!.AbsoluteUri : null));
        }

        return result;
    }

    public static string WikidataUrl(string? wikidataId)
        => (wikidataId ?? string.Empty).Trim() is { Length: > 0 } id ? $"https://www.wikidata.org/wiki/{id}" : string.Empty;
}

public static class ArtworkSheet
{
    public static IReadOnlyList<SheetField> MainFields(Opera opera, string collocazione) => RecordSheet.Fields(
        ("Tipo", opera.Tipo),
        ("Data", opera.Data),
        ("Tecnica", opera.Tecnica),
        ("Dimensioni", opera.Dimensioni),
        ("Collocazione", collocazione),
        ("Wikidata", opera.WikidataId));

    // Persone collegate nell'ordine dei ruoli (autori, bottega, attribuzioni, committenti, soggetti), poi per nome.
    public static IReadOnlyList<SheetPersonLink> People(Opera opera, Func<int, string> nameOf)
    {
        var order = OperaRuoli.Tutti.Select(r => r.Codice).ToList();
        return
        [
            .. opera.Persone
                .Select(p => (p.PersonaId, p.Ruolo, Nome: nameOf(p.PersonaId)))
                .OrderBy(p => order.IndexOf(p.Ruolo) is var i && i < 0 ? int.MaxValue : i)
                .ThenBy(p => p.Nome, StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), true))
                .Select(p => new SheetPersonLink(p.PersonaId, p.Nome, OperaRuoli.Info(p.Ruolo).Etichetta)),
        ];
    }

    public static string Artists(Opera opera, Func<int, string> nameOf)
        => OperaRuoli.DescribeArtists(opera.Persone.Select(p => (p.Ruolo, nameOf(p.PersonaId))));

    public static string Subtitle(Opera opera, Func<int, string> nameOf)
        => string.Join(" · ", new[] { Artists(opera, nameOf), opera.Data.Trim(), opera.Tipo.Trim() }.Where(s => s.Length > 0));

    // Altre opere degli stessi artisti (autore, bottega, attribuzione), esclusa quella mostrata.
    public static IReadOnlyList<Opera> SameArtists(Opera opera, IEnumerable<Opera> all)
    {
        var artists = opera.Persone.Where(p => OperaRuoli.IsArtista(p.Ruolo)).Select(p => p.PersonaId).ToHashSet();
        return artists.Count == 0
            ? []
            : [.. all.Where(o => o.Id != opera.Id && o.Persone.Any(p => OperaRuoli.IsArtista(p.Ruolo) && artists.Contains(p.PersonaId)))
                .OrderBy(o => HistoricalYear.Parse(o.Data) ?? int.MaxValue)
                .ThenBy(o => o.Titolo, StringComparer.CurrentCultureIgnoreCase)];
    }
}

public static class EventSheet
{
    public static IReadOnlyList<SheetField> MainFields(Evento evento, string luogo) => RecordSheet.Fields(
        ("Tipo", evento.Tipo),
        ("Data", evento.DataTesto),
        ("Nota sul calendario", HistoricalCalendar.Describe(evento.Data1, evento.Calendario, evento.Stile)),
        ("Luogo", luogo),
        ("Wikidata", evento.WikidataId));

    public static IReadOnlyList<SheetPersonLink> Participants(Evento evento, Func<int, string> nameOf) =>
    [
        .. evento.Partecipanti
            .Select(p => new SheetPersonLink(p.PersonaId, nameOf(p.PersonaId), p.Ruolo.Trim()))
            .OrderBy(p => p.Nome, StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), true)),
    ];

    // Eventi con almeno un partecipante tra le persone indicate (es. i membri di una dinastia).
    public static bool InvolvesAny(Evento evento, IReadOnlySet<int> personIds)
        => evento.Partecipanti.Any(p => personIds.Contains(p.PersonaId));

    // Righe dell'elenco eventi, nello stesso ordine ricevuto (cronologico se arrivano da GetEvents).
    public static IReadOnlyList<EventRow> Rows(IEnumerable<Evento> events, Func<int, string> nameOf, Func<Evento, string> placeOf) =>
    [
        .. events.Select(e => new EventRow(
            e.Id,
            e.DataTesto,
            e.Titolo.Trim(),
            e.Tipo.Trim(),
            placeOf(e),
            string.Join(", ", Participants(e, nameOf).Select(p => p.Nome)),
            RichNotes.ToPlainText(e.Descrizione).Trim(),
            e.Partecipanti.Select(p => p.PersonaId).ToHashSet())),
    ];

    // Filtri della sezione Eventi: testo libero, tipo (vuoto = tutti) e persone coinvolte (null = tutte).
    public static IReadOnlyList<EventRow> Filter(IEnumerable<EventRow> rows, string? query, string? tipo, IReadOnlySet<int>? people) =>
    [
        .. rows.Where(r => string.IsNullOrWhiteSpace(tipo) || string.Equals(r.Tipo, tipo.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .Where(r => people is null || r.PersonIds.Overlaps(people))
            .Where(r => RecordSheet.Matches(query, r.Titolo, r.Tipo, r.Data, r.Luogo, r.Partecipanti, r.Descrizione)),
    ];
}

// Riga della sezione Eventi della finestra principale.
public sealed record EventRow(int Id, string Data, string Titolo, string Tipo, string Luogo, string Partecipanti, string Descrizione, IReadOnlySet<int> PersonIds)
{
    public string Etichetta => string.Join(", ", new[] { Titolo, Data, Tipo, Luogo }.Where(s => s.Length > 0))
        + (Partecipanti.Length > 0 ? $". Partecipanti: {Partecipanti}" : string.Empty);
}

// Riquadro della galleria delle opere nella finestra principale.
public sealed record ArtworkRow(int Id, string Titolo, string Artisti, string Data, string Tipo, string Immagine, string Testo, IReadOnlySet<int> ArtistIds)
{
    public string Dettaglio => string.Join(" · ", new[] { Data, Tipo }.Where(s => s.Length > 0));

    public string Etichetta => string.Join(", ", new[] { Titolo, Artisti, Data, Tipo }.Where(s => s.Length > 0));
}

// Persona che compare come autore, bottega o attribuzione in almeno un'opera.
public sealed record ArtistOption(int Id, string Nome, int Opere)
{
    public string Etichetta => $"{Nome} ({Opere})";

    public override string ToString() => Etichetta;
}

public static class ArtworkGallery
{
    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), true);

    // Opere in ordine di titolo, con artisti, data e testo cercabile (note, tecnica e persone collegate).
    public static IReadOnlyList<ArtworkRow> Rows(IEnumerable<Opera> works, Func<int, string> nameOf) =>
    [
        .. works.Select(o => new ArtworkRow(
                o.Id,
                o.Titolo.Trim(),
                ArtworkSheet.Artists(o, nameOf),
                o.Data.Trim(),
                o.Tipo.Trim(),
                o.Immagine,
                string.Join("\n", o.Persone.Select(p => nameOf(p.PersonaId)).Append(o.Tecnica).Append(RichNotes.ToPlainText(o.Note))),
                o.Persone.Where(p => OperaRuoli.IsArtista(p.Ruolo)).Select(p => p.PersonaId).ToHashSet()))
            .OrderBy(r => r.Titolo, Comparer),
    ];

    // Filtri della sezione Opere: testo libero, tipo (vuoto = tutti) e artista (null = tutti).
    public static IReadOnlyList<ArtworkRow> Filter(IEnumerable<ArtworkRow> rows, string? query, string? tipo, int? artistId) =>
    [
        .. rows.Where(r => string.IsNullOrWhiteSpace(tipo) || string.Equals(r.Tipo, tipo.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .Where(r => artistId is not int a || r.ArtistIds.Contains(a))
            .Where(r => RecordSheet.Matches(query, r.Titolo, r.Artisti, r.Data, r.Tipo, r.Testo)),
    ];

    // Artisti dell'archivio con il numero delle loro opere, in ordine di nome.
    public static IReadOnlyList<ArtistOption> Artists(IEnumerable<Opera> works, Func<int, string> nameOf) =>
    [
        .. works.SelectMany(o => o.Persone.Where(p => OperaRuoli.IsArtista(p.Ruolo)).Select(p => (p.PersonaId, o.Id)).Distinct())
            .GroupBy(x => x.PersonaId)
            .Select(g => new ArtistOption(g.Key, nameOf(g.Key), g.Count()))
            .OrderBy(a => a.Nome, Comparer),
    ];
}

public sealed record DynastyMemberRow(int Id, string Nome, string Anni, string Ramo)
{
    public string Etichetta => string.Join(", ", new[] { Nome, Anni, Ramo.Length > 0 ? $"ramo {Ramo}" : string.Empty }.Where(s => s.Length > 0));
}

public static class DynastySheet
{
    // «Asburgo › Asburgo di Spagna»: la dinastia preceduta dai suoi superiori.
    public static string Path(Dinastia dynasty, IReadOnlyList<Dinastia> all)
    {
        var parts = new List<string> { dynasty.Nome.Trim() };
        var visited = new HashSet<int> { dynasty.Id };
        var current = dynasty;
        while (current.ParentId is int p && visited.Add(p) && all.FirstOrDefault(d => d.Id == p) is { } parent)
        {
            parts.Insert(0, parent.Nome.Trim());
            current = parent;
        }

        return string.Join(" › ", parts);
    }

    // Membri della dinastia e dei suoi rami, in ordine di nascita (senza anno in fondo).
    public static IReadOnlyList<HistoricalPerson> Members(int dynastyId, IReadOnlyList<Dinastia> dynasties, IEnumerable<HistoricalPerson> people)
    {
        var list = people.ToList();
        var ids = HistoricalTimeline.FamilyMembers(list, HistoricalMap.FamiliesOfDynasty(dynastyId, dynasties));
        return
        [
            .. list.Where(p => ids.Contains(p.Id))
                .OrderBy(p => HistoricalYear.Parse(p.Birth) ?? HistoricalYear.Parse(p.Death) ?? int.MaxValue)
                .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    // Righe dell'elenco dei membri: nome, anni e ramo.
    public static IReadOnlyList<DynastyMemberRow> MemberRows(Dinastia dynasty, IReadOnlyList<Dinastia> dynasties, IEnumerable<HistoricalPerson> people) =>
    [
        .. Members(dynasty.Id, dynasties, people).Select(p => new DynastyMemberRow(
            p.Id,
            p.DisplayName,
            string.IsNullOrWhiteSpace(p.Birth) && string.IsNullOrWhiteSpace(p.Death)
                ? string.Empty
                : $"{(string.IsNullOrWhiteSpace(p.Birth) ? "?" : p.Birth.Trim())} – {(string.IsNullOrWhiteSpace(p.Death) ? "?" : p.Death.Trim())}",
            Branch(p, dynasty))),
    ];

    // Ramo a cui appartiene la persona tra quelli della dinastia (vuoto se è nella dinastia principale).
    public static string Branch(HistoricalPerson person, Dinastia dynasty)
    {
        var names = PeopleGrouping.FamilyNames(person.Family);
        return names.Any(n => PeopleGrouping.FamilyComparer.Equals(n, dynasty.Nome.Trim()))
            ? string.Empty
            : string.Join(", ", names);
    }
}
