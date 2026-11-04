using System.Globalization;

namespace HistoricEditor;

public enum TimelineCategory
{
    Vita,
    Carica,
    Unione,
    Evento,
}

// Una voce della linea del tempo: anno moderno (per ordinare e disegnare) e data come scritta nella scheda.
public sealed record TimelineEntry(
    TimelineCategory Categoria,
    string Tipo,
    int? Anno,
    int? Mese,
    int? Giorno,
    string Data,
    string Descrizione,
    IReadOnlyList<int> PersonIds,
    string Persone,
    int? EventoId = null)
{
    public string AnnoTesto => Anno is int y ? HistoricalYear.Format(y) : "—";

    public string Etichetta
    {
        get
        {
            var text = $"{AnnoTesto}, {Tipo}: {Descrizione}";
            if (Persone.Length > 0)
            {
                text += $" — {Persone}";
            }

            return Data.Length > 0 ? $"{text} (data: {Data})" : text;
        }
    }
}

// Periodo disegnato come barra: la vita di una persona, una carica o un evento che dura più anni.
public sealed record TimelineBar(TimelineCategory Categoria, string Etichetta, int Inizio, int Fine, bool FineAperta = false);

// Corsia della linea del tempo dedicata a una persona.
public sealed record TimelineLane(int PersonId, string Persona, IReadOnlyList<TimelineBar> Barre, IReadOnlyList<TimelineEntry> Voci);

public sealed record TimelineData(IReadOnlyList<TimelineEntry> Voci, IReadOnlyList<TimelineLane> Corsie)
{
    // Primo e ultimo anno noti, considerando voci e barre.
    public (int Min, int Max)? Intervallo
    {
        get
        {
            var years = Voci.Select(v => v.Anno).OfType<int>()
                .Concat(Corsie.SelectMany(c => c.Barre).SelectMany(b => new[] { b.Inizio, b.Fine }))
                .ToList();
            return years.Count == 0 ? null : (years.Min(), years.Max());
        }
    }
}

// Famiglia o dinastia da mostrare nella linea del tempo; DinastiaId è null per le famiglie senza dinastia registrata.
public sealed record TimelineFamilyOption(string Nome, int? DinastiaId)
{
    public override string ToString() => Nome;
}

// Linea del tempo di una persona, dei suoi familiari o di una dinastia: nascite, morti, cariche, unioni ed eventi.
public static class HistoricalTimeline
{
    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    // La persona con genitori, coniugi (unioni registrate e altri genitori dei figli) e figli.
    public static HashSet<int> Relatives(int personId, IEnumerable<HistoricalPerson> people, IEnumerable<Unione> unions)
    {
        var list = people.ToList();
        var ids = new HashSet<int> { personId };
        if (list.FirstOrDefault(p => p.Id == personId) is { } person)
        {
            ids.UnionWith(new[] { person.FatherId, person.MotherId }.OfType<int>());
        }

        foreach (var union in unions)
        {
            if (union.Persona1Id == personId)
            {
                ids.Add(union.Persona2Id);
            }
            else if (union.Persona2Id == personId)
            {
                ids.Add(union.Persona1Id);
            }
        }

        foreach (var child in list.Where(p => p.FatherId == personId || p.MotherId == personId))
        {
            ids.Add(child.Id);
            ids.UnionWith(new[] { child.FatherId, child.MotherId }.OfType<int>());
        }

        ids.IntersectWith(list.Select(p => p.Id));
        return ids;
    }

    // Persone che tra le famiglie indicate nella scheda hanno uno dei nomi dati (una dinastia e i suoi rami).
    public static HashSet<int> FamilyMembers(IEnumerable<HistoricalPerson> people, IEnumerable<string> families)
    {
        var names = new HashSet<string>(families.Select(f => f.Trim()).Where(f => f.Length > 0), Comparer);
        return [.. people.Where(p => PeopleGrouping.FamilyNames(p.Family).Any(names.Contains)).Select(p => p.Id)];
    }

    // Dinastie registrate e famiglie indicate nelle schede, senza doppioni, in ordine alfabetico.
    public static List<TimelineFamilyOption> FamilyOptions(IEnumerable<HistoricalPerson> people, IEnumerable<Dinastia> dynasties)
    {
        var options = dynasties
            .Where(d => d.Nome.Trim().Length > 0)
            .Select(d => new TimelineFamilyOption(d.Nome.Trim(), d.Id))
            .ToList();
        var known = new HashSet<string>(options.Select(o => o.Nome), Comparer);
        foreach (var family in people.SelectMany(p => PeopleGrouping.FamilyNames(p.Family)))
        {
            if (known.Add(family))
            {
                options.Add(new TimelineFamilyOption(family, null));
            }
        }

        return [.. options.OrderBy(o => o.Nome, Comparer)];
    }

    public static TimelineData Build(IEnumerable<HistoricalPerson> people, IEnumerable<Unione> unions, IEnumerable<Evento> events, IReadOnlySet<int> personIds)
    {
        var all = people.ToList();
        var byId = all.Where(p => personIds.Contains(p.Id)).ToDictionary(p => p.Id);
        var entries = new List<TimelineEntry>();
        var bars = byId.Keys.ToDictionary(id => id, _ => new List<TimelineBar>());

        foreach (var person in byId.Values)
        {
            var name = person.DisplayName;
            IReadOnlyList<int> self = [person.Id];
            var birth = Point(person.Birth, person.BirthStile);
            var death = Point(person.Death, person.DeathStile);
            if (person.Birth.Trim().Length > 0)
            {
                entries.Add(new(TimelineCategory.Vita, "Nascita", birth.Year, birth.Month, birth.Day, DateText(person.Birth, person.BirthCalendario, person.BirthStile),
                    WithPlace("Nascita", person.BirthPlace), self, name));
            }

            if (person.Death.Trim().Length > 0)
            {
                entries.Add(new(TimelineCategory.Vita, "Morte", death.Year, death.Month, death.Day, DateText(person.Death, person.DeathCalendario, person.DeathStile),
                    WithPlace("Morte", person.DeathPlace), self, name));
            }

            if (birth.Year is int born && death.Year is int died && died >= born)
            {
                bars[person.Id].Add(new(TimelineCategory.Vita, $"Vita di {name}: {HistoricalYear.Format(born)}–{HistoricalYear.Format(died)}", born, died));
            }

            foreach (var title in person.Titles.Where(t => t.Titolo.Trim().Length > 0))
            {
                var office = title.Titolo.Trim();
                var start = Point(title.Inizio, null);
                var end = Point(title.Fine, null);
                if (title.Inizio.Trim().Length > 0)
                {
                    entries.Add(new(TimelineCategory.Carica, "Inizio carica", start.Year, start.Month, start.Day, title.Inizio.Trim(),
                        WithCause(office, title.CausaInizio), self, name));
                }

                if (title.Fine.Trim().Length > 0)
                {
                    entries.Add(new(TimelineCategory.Carica, "Fine carica", end.Year, end.Month, end.Day, title.Fine.Trim(),
                        WithCause(office, title.CausaFine), self, name));
                }

                var until = end.Year ?? death.Year;
                if (start.Year is int from && until is int to && to >= from)
                {
                    var open = end.Year is null;
                    var period = open ? $"dal {HistoricalYear.Format(from)}, fine non indicata" : $"{HistoricalYear.Format(from)}–{HistoricalYear.Format(to)}";
                    bars[person.Id].Add(new(TimelineCategory.Carica, $"{office} ({name}): {period}", from, to, open));
                }
            }
        }

        foreach (var union in unions)
        {
            var members = new[] { union.Persona1Id, union.Persona2Id }.Where(byId.ContainsKey).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var tipo = union.Tipo.Trim().Length > 0 ? union.Tipo.Trim() : "Unione";
            var names = string.Join(" e ", new[] { union.Persona1Id, union.Persona2Id }.Select(id => NameOf(id, byId, all)).Where(n => n.Length > 0));
            var persone = string.Join(", ", members.Select(id => byId[id].DisplayName));
            if (union.Inizio.Trim().Length > 0)
            {
                var start = Point(union.Inizio, null);
                entries.Add(new(TimelineCategory.Unione, tipo, start.Year, start.Month, start.Day, union.Inizio.Trim(), $"{tipo} tra {names}", members, persone));
            }

            if (union.Fine.Trim().Length > 0)
            {
                var end = Point(union.Fine, null);
                entries.Add(new(TimelineCategory.Unione, $"Fine {tipo.ToLower(CultureInfo.GetCultureInfo("it-IT"))}", end.Year, end.Month, end.Day, union.Fine.Trim(), $"{tipo} tra {names}", members, persone));
            }
        }

        foreach (var evento in events)
        {
            var members = evento.Partecipanti.Where(p => byId.ContainsKey(p.PersonaId)).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var first = Point(evento.Data1.Trim().Length > 0 ? evento.Data1 : evento.Data2, evento.Stile);
            var persone = string.Join(", ", members.Select(m => m.Ruolo.Trim().Length > 0 ? $"{byId[m.PersonaId].DisplayName} ({m.Ruolo.Trim()})" : byId[m.PersonaId].DisplayName));
            var titolo = evento.Titolo.Trim().Length > 0 ? evento.Titolo.Trim() : "Evento senza titolo";
            var tipo = evento.Tipo.Trim().Length > 0 ? evento.Tipo.Trim() : "Evento";
            var ids = members.Select(m => m.PersonaId).Distinct().ToList();
            entries.Add(new(TimelineCategory.Evento, tipo, evento.Anno, first.Month, first.Day, evento.DataTesto, titolo, ids, persone, evento.Id));

            if (evento.DataQualificatore == DataEvento.DaA
                && HistoricalCalendar.ModernYear(evento.Data1, evento.Stile) is int from
                && HistoricalCalendar.ModernYear(evento.Data2, evento.Stile) is int to && to > from)
            {
                foreach (var id in ids)
                {
                    bars[id].Add(new(TimelineCategory.Evento, $"{titolo}: {HistoricalYear.Format(from)}–{HistoricalYear.Format(to)}", from, to));
                }
            }
        }

        var ordered = entries
            .OrderBy(e => e.Anno is null)
            .ThenBy(e => e.Anno ?? 0)
            .ThenBy(e => e.Mese ?? 0)
            .ThenBy(e => e.Giorno ?? 0)
            .ThenBy(e => e.Tipo == "Nascita" ? 0 : e.Tipo == "Morte" ? 2 : 1)
            .ThenBy(e => e.Descrizione, Comparer)
            .ToList();

        var lanes = byId.Values
            .Select(p => new TimelineLane(
                p.Id,
                p.DisplayName,
                [.. bars[p.Id].OrderBy(b => b.Categoria).ThenBy(b => b.Inizio)],
                [.. ordered.Where(e => e.Anno is not null && e.PersonIds.Contains(p.Id))]))
            .Where(l => l.Barre.Count > 0 || l.Voci.Count > 0)
            .OrderBy(l => FirstYear(l) is null)
            .ThenBy(l => FirstYear(l) ?? 0)
            .ThenBy(l => l.Persona, Comparer)
            .ToList();

        return new TimelineData(ordered, lanes);
    }

    // Testo semplice dell'elenco, per copiarlo in un documento.
    public static string ToText(IEnumerable<TimelineEntry> entries)
        => string.Join(Environment.NewLine, entries.Select(e => e.Etichetta));

    private static int? FirstYear(TimelineLane lane)
        => lane.Barre.Select(b => (int?)b.Inizio).Concat(lane.Voci.Select(v => v.Anno)).Min();

    // Anno moderno (con lo stile di inizio dell'anno) e, se presenti, mese e giorno per ordinare le voci dello stesso anno.
    private static (int? Year, int? Month, int? Day) Point(string? text, string? style)
    {
        var year = HistoricalCalendar.ModernYear(text, style);
        if (year is not null && HistoricalCalendar.TryParse(text, out var date))
        {
            var modern = HistoricalCalendar.ToModern(date, style);
            if (modern.Year == year)
            {
                return (year, modern.Month, modern.Day);
            }
        }

        return (year, null, null);
    }

    private static string DateText(string date, string calendar, string style)
        => HistoricalCalendar.Suffix(calendar, style) is { Length: > 0 } suffix ? $"{date.Trim()} ({suffix})" : date.Trim();

    private static string WithPlace(string what, string place)
        => place.Trim().Length > 0 ? $"{what} · {place.Trim()}" : what;

    private static string WithCause(string office, string cause)
        => cause.Trim().Length > 0 ? $"{office} ({cause.Trim().ToLower(CultureInfo.GetCultureInfo("it-IT"))})" : office;

    private static string NameOf(int id, IReadOnlyDictionary<int, HistoricalPerson> inScope, IEnumerable<HistoricalPerson> people)
        => inScope.TryGetValue(id, out var person) ? person.DisplayName : people.FirstOrDefault(p => p.Id == id)?.DisplayName ?? string.Empty;
}
