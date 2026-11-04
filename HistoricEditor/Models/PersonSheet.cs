namespace HistoricEditor;

// Riga «etichetta: valore» della scheda di lettura.
public sealed record SheetField(string Etichetta, string Valore)
{
    public string Descrizione => $"{Etichetta}: {Valore}";
}

// Riga di un titolo nella scheda di lettura; il titolo apre la cronologia della carica.
public sealed record SheetTitle(PersonTitle Titolo, string Nome, string Dettagli)
{
    public bool HasDettagli => Dettagli.Length > 0;
}

// Collegamento web della scheda di lettura.
public sealed record SheetLink(string Etichetta, string Url);

// Testi della scheda di lettura di un personaggio: solo le informazioni presenti, senza campi vuoti.
public static class PersonSheet
{
    public static string DescribeSex(string? sesso) => sesso switch
    {
        Sessi.Maschio => "Maschio",
        Sessi.Femmina => "Femmina",
        _ => string.Empty,
    };

    public static IReadOnlyList<SheetField> MainFields(HistoricalPerson person) => Fields(
        ("Nome", person.Name),
        ("Cognome", person.Surname),
        ("Suffisso", person.Suffix),
        ("Soprannome", person.Nickname),
        ("Titolo principale", person.MainTitle),
        ("Famiglia", person.Family),
        ("Sesso", DescribeSex(person.Sesso)));

    // Data con il calendario e lo stile dell'anno, se indicati: «10 febbraio 1525 (calendario giuliano, stile fiorentino)».
    public static string DescribeDate(string? date, string? calendario, string? stile)
    {
        var text = (date ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var notes = new List<string>();
        if (!string.IsNullOrEmpty(calendario))
        {
            notes.Add($"calendario {HistoricalCalendar.CalendarInfo(calendario).Etichetta.ToLowerInvariant()}");
        }

        if (!string.IsNullOrEmpty(stile))
        {
            notes.Add($"stile {HistoricalCalendar.StyleInfo(stile).Etichetta}");
        }

        return notes.Count == 0 ? text : $"{text} ({string.Join(", ", notes)})";
    }

    // Nome all'epoca, entità politica, unità e periodo del primo contesto storico registrato.
    public static string DescribeHistoricalContext(IEnumerable<HistoricalPlaceRecord> places)
    {
        var context = places.SelectMany(p => p.HistoricalContexts).FirstOrDefault(c =>
            new[] { c.NameAtTime, c.PoliticalEntity, c.AdministrativeUnit, c.Period }.Any(v => !string.IsNullOrWhiteSpace(v)));
        if (context is null)
        {
            return string.Empty;
        }

        var parts = new[] { context.NameAtTime, context.AdministrativeUnit, context.PoliticalEntity }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();
        var text = string.Join(", ", parts);
        var period = context.Period.Trim();
        return period.Length == 0 ? text : text.Length == 0 ? period : $"{text} ({period})";
    }

    public static IReadOnlyList<SheetField> LocalizedNames(IEnumerable<LocalizedPersonName> names) =>
    [
        .. names
            .Where(n => !n.IsEmpty())
            .Select(n => new SheetField(
                Capitalize(n.LinguaNome.Trim().Length > 0 ? n.LinguaNome.Trim() : n.Lingua.Trim()),
                string.Join(" · ", new[]
                {
                    HistoricalPerson.ComposeName(n.Nome, null, n.Suffisso),
                    n.Soprannome.Trim(),
                    n.TitoloPrincipale.Trim(),
                }.Where(v => v.Length > 0)))),
    ];

    public static IReadOnlyList<SheetTitle> Titles(IEnumerable<PersonTitle> titles) =>
    [
        .. titles
            .Where(t => !t.IsEmpty())
            .Select(t => new SheetTitle(t, TitleName(t), TitleDetails(t))),
    ];

    public static string TitleName(PersonTitle title)
    {
        var name = title.Titolo.Trim().Length > 0 ? title.Titolo.Trim() : "Titolo senza nome";
        return title.Numerazione.Trim().Length > 0 ? $"{name} ({title.Numerazione.Trim()})" : name;
    }

    // «dal 1516 (Morte del predecessore) al 1556 (Abdicazione) · Predecessore: … · Successore: …»
    public static string TitleDetails(PersonTitle title)
    {
        static string WithCause(string date, string cause)
            => (date.Trim(), cause.Trim()) switch
            {
                ("", "") => string.Empty,
                (var d, "") => d,
                ("", var c) => c,
                (var d, var c) => $"{d} ({c})",
            };

        var start = WithCause(title.Inizio, title.CausaInizio);
        var end = WithCause(title.Fine, title.CausaFine);
        var period = (start, end) switch
        {
            ("", "") => string.Empty,
            (var a, "") => $"dal {a}",
            ("", var b) => $"fino al {b}",
            (var a, var b) => $"dal {a} al {b}",
        };

        var parts = new List<string>();
        if (period.Length > 0)
        {
            parts.Add(period);
        }

        if (title.Predecessore.Trim().Length > 0)
        {
            parts.Add($"Predecessore: {title.Predecessore.Trim()}");
        }

        if (title.Successore.Trim().Length > 0)
        {
            parts.Add($"Successore: {title.Successore.Trim()}");
        }

        return string.Join(" · ", parts);
    }

    // Collegamenti web della scheda (Wikipedia, Wikidata, WikiTree, fonti delle date), senza doppioni.
    public static IReadOnlyList<SheetLink> Sources(HistoricalPerson person)
    {
        var links = new List<SheetLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string label, string? url)
        {
            var text = (url ?? string.Empty).Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && seen.Add(uri.AbsoluteUri))
            {
                links.Add(new SheetLink(label, uri.AbsoluteUri));
            }
        }

        var wikipedia = person.WikipediaTitle.Trim();
        Add(wikipedia.Length > 0 ? $"Wikipedia: {wikipedia}" : "Wikipedia", person.WikipediaUrl);
        if (person.WikidataId.Trim().Length > 0)
        {
            Add($"Wikidata: {person.WikidataId.Trim()}", $"https://www.wikidata.org/wiki/{person.WikidataId.Trim()}");
        }

        if (person.WikiTreeId.Trim().Length > 0)
        {
            Add($"WikiTree: {person.WikiTreeId.Trim()}", WikiTreeLinks.ProfileUrl(person.WikiTreeId.Trim()));
        }

        foreach (var (label, evidence) in new[] { ("nascita", person.BirthDateEvidence), ("morte", person.DeathDateEvidence) })
        {
            foreach (var source in evidence.SelectMany(e => e.Sources))
            {
                var title = source.Title.Trim().Length > 0 ? source.Title.Trim() : source.Url.Trim();
                Add($"Data di {label}: {title}", source.Url);
            }
        }

        return links;
    }

    // «Ramo di: A › B», «Dinastia principale» o vuoto se la famiglia non è tra le dinastie registrate.
    public static string DynastyPath(string? family, IReadOnlyList<Dinastia> dynasties)
    {
        string Describe(string name)
        {
            var dynasty = dynasties.FirstOrDefault(d => PeopleGrouping.FamilyComparer.Equals(d.Nome.Trim(), name));
            if (dynasty is null)
            {
                return string.Empty;
            }

            var parents = new List<string>();
            var visited = new HashSet<int>();
            while (dynasty?.ParentId is int p && visited.Add(p) && dynasties.FirstOrDefault(d => d.Id == p) is { } parent)
            {
                parents.Insert(0, parent.Nome);
                dynasty = parent;
            }

            return parents.Count > 0 ? $"Ramo di: {string.Join(" › ", parents)}" : "Dinastia principale";
        }

        var names = PeopleGrouping.FamilyNames(family ?? string.Empty);
        if (names.Count == 1)
        {
            return Describe(names[0]);
        }

        return string.Join(" · ", names
            .Select(n => (Name: n, Path: Describe(n)))
            .Where(x => x.Path.Length > 0)
            .Select(x => $"{x.Name} – {x.Path}"));
    }

    private static IReadOnlyList<SheetField> Fields(params (string Label, string? Value)[] values) =>
    [
        .. values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => new SheetField(v.Label, v.Value!.Trim())),
    ];

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
