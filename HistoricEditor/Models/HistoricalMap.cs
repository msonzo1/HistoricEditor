using System.Globalization;

namespace HistoricEditor;

public enum MapPointKind
{
    Nascita,
    Morte,
    Sepoltura,
    Evento,
    Luogo,
}

// Un elemento da mostrare sulla mappa: nascita/morte/sepoltura di una persona, un evento o un luogo dell'archivio.
public sealed record MapPoint(
    MapPointKind Kind,
    double Latitudine,
    double Longitudine,
    string Titolo,
    string Luogo,
    string Data,
    int? Anno,
    int? PersonId,
    int? EventId,
    IReadOnlyList<int> PersoneIds,
    string Famiglia,
    string TipoEvento,
    bool Approssimato)
{
    public string Categoria => Kind switch
    {
        MapPointKind.Nascita => "Nascita",
        MapPointKind.Morte => "Morte",
        MapPointKind.Sepoltura => "Sepoltura",
        MapPointKind.Evento => TipoEvento.Length > 0 ? TipoEvento : "Evento",
        _ => "Luogo",
    };

    public string Dettaglio => string.Join(" · ", new[] { Categoria, Data, Luogo, Approssimato ? "posizione approssimata" : string.Empty }
        .Where(s => s.Length > 0));

    public string Etichetta => $"{Titolo}, {Dettaglio}";
}

// Punti che cadono nella stessa posizione, mostrati come un unico segnaposto.
public sealed record MapLocation(double Latitudine, double Longitudine, string Luogo, IReadOnlyList<MapPoint> Punti)
{
    public MapPointKind KindPrevalente => Punti.GroupBy(p => p.Kind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

    public string Etichetta => Punti.Count == 1
        ? Punti[0].Etichetta
        : $"{(Luogo.Length > 0 ? Luogo : "Posizione")}: {Punti.Count} elementi";
}

public sealed class MapFilter
{
    public bool Nascite { get; set; } = true;
    public bool Morti { get; set; } = true;
    public bool Sepolture { get; set; } = true;
    public bool Eventi { get; set; } = true;
    public bool Luoghi { get; set; } = true;

    public int? DaAnno { get; set; }
    public int? AAnno { get; set; }
    public int? PersonId { get; set; }

    // Nomi di famiglia ammessi (la dinastia scelta con i suoi rami); null = nessun filtro.
    public IReadOnlySet<string>? Famiglie { get; set; }

    public string TipoEvento { get; set; } = string.Empty;

    public bool HasPeriod => DaAnno is not null || AAnno is not null;
}

public static class HistoricalMap
{
    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    public static StringComparer FamilyComparer => Comparer;

    // Coordinate di un luogo: quelle del luogo specifico, altrimenti del luogo amministrativo; in mancanza si
    // risale ai luoghi contenitori (posizione approssimata).
    public static (double Lat, double Lon, bool Approssimato)? ResolveCoordinates(
        int? luogoId,
        int? specificoId,
        IReadOnlyDictionary<int, Luogo> places,
        IReadOnlyDictionary<int, LuogoSpecifico> specifics)
    {
        if (specificoId is int s && specifics.TryGetValue(s, out var specifico))
        {
            if (specifico.Latitudine is double sLat && specifico.Longitudine is double sLon)
            {
                return (sLat, sLon, false);
            }

            luogoId ??= specifico.LuogoId;
            if (ResolveCoordinates(specifico.LuogoId, null, places, specifics) is { } fromPlace)
            {
                return (fromPlace.Lat, fromPlace.Lon, true);
            }

            return null;
        }

        var approximate = false;
        var visited = new HashSet<int>();
        var current = luogoId is int id && places.TryGetValue(id, out var luogo) ? luogo : null;
        while (current is not null && visited.Add(current.Id))
        {
            if (current.Latitudine is double lat && current.Longitudine is double lon)
            {
                return (lat, lon, approximate);
            }

            approximate = true;
            current = current.ParentId is int parent && places.TryGetValue(parent, out var next) ? next : null;
        }

        return null;
    }

    public static List<MapPoint> BuildPoints(
        IEnumerable<HistoricalPerson> people,
        IEnumerable<Evento> events,
        IEnumerable<Luogo> places,
        IEnumerable<LuogoSpecifico> specifics)
    {
        var placesById = places.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var specificsById = specifics.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var peopleList = people.ToList();
        var familyById = peopleList.ToDictionary(p => p.Id, p => (p.Family ?? string.Empty).Trim());
        var points = new List<MapPoint>();

        string Describe(int? luogoId, int? specificoId, string fallback)
        {
            var parts = new List<string>();
            if (specificoId is int s && specificsById.TryGetValue(s, out var specifico))
            {
                parts.Add(specifico.Nome);
                luogoId = specifico.LuogoId;
            }

            if (luogoId is int l && placesById.TryGetValue(l, out var luogo))
            {
                parts.Add(luogo.Nome);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : (fallback ?? string.Empty).Trim();
        }

        foreach (var person in peopleList)
        {
            var family = familyById[person.Id];
            void Add(MapPointKind kind, int? luogoId, int? specificoId, string date, string style, string fallback)
            {
                if (ResolveCoordinates(luogoId, specificoId, placesById, specificsById) is not { } c)
                {
                    return;
                }

                points.Add(new MapPoint(kind, c.Lat, c.Lon, person.DisplayName, Describe(luogoId, specificoId, fallback),
                    (date ?? string.Empty).Trim(), HistoricalCalendar.ModernYear(date, style), person.Id, null, [person.Id], family, string.Empty, c.Approssimato));
            }

            Add(MapPointKind.Nascita, person.BirthLuogoId, person.BirthLuogoSpecificoId, person.Birth, person.BirthStile, person.BirthPlace);
            Add(MapPointKind.Morte, person.DeathLuogoId, person.DeathLuogoSpecificoId, person.Death, person.DeathStile, person.DeathPlace);
            // La sepoltura non ha una data propria: si usa l'anno di morte per il filtro sul periodo.
            Add(MapPointKind.Sepoltura, person.BurialLuogoId, person.BurialLuogoSpecificoId, person.Death, person.DeathStile, person.BurialPlace);
        }

        foreach (var evento in events)
        {
            if (ResolveCoordinates(evento.LuogoId, evento.LuogoSpecificoId, placesById, specificsById) is not { } c)
            {
                continue;
            }

            var ids = evento.Partecipanti.Select(p => p.PersonaId).Distinct().ToList();
            points.Add(new MapPoint(MapPointKind.Evento, c.Lat, c.Lon, evento.Titolo, Describe(evento.LuogoId, evento.LuogoSpecificoId, string.Empty),
                evento.DataTesto, evento.Anno, null, evento.Id, ids, string.Empty, (evento.Tipo ?? string.Empty).Trim(), c.Approssimato));
        }

        foreach (var luogo in placesById.Values.Where(l => l.Latitudine is not null && l.Longitudine is not null))
        {
            points.Add(new MapPoint(MapPointKind.Luogo, luogo.Latitudine!.Value, luogo.Longitudine!.Value, luogo.Nome,
                Describe(luogo.ParentId, null, string.Empty), luogo.Periodo, null, null, null, [], string.Empty, luogo.Tipo, false));
        }

        foreach (var specifico in specificsById.Values.Where(s => s.Latitudine is not null && s.Longitudine is not null))
        {
            points.Add(new MapPoint(MapPointKind.Luogo, specifico.Latitudine!.Value, specifico.Longitudine!.Value, specifico.Nome,
                Describe(specifico.LuogoId, null, string.Empty), string.Empty, null, null, null, [], string.Empty, specifico.Tipo, false));
        }

        return points;
    }

    // familyOfPerson serve a filtrare per dinastia gli eventi, tramite la famiglia dei partecipanti.
    public static List<MapPoint> Filter(IEnumerable<MapPoint> points, MapFilter filter, IReadOnlyDictionary<int, string> familyOfPerson)
    {
        bool InFamilies(int personId)
            => filter.Famiglie is null || (familyOfPerson.TryGetValue(personId, out var f) && PeopleGrouping.FamilyNames(f).Any(filter.Famiglie.Contains));

        bool InPeriod(MapPoint p)
        {
            if (!filter.HasPeriod)
            {
                return true;
            }

            // Con un periodo impostato, gli elementi senza un anno riconoscibile non possono essere collocati.
            return p.Anno is int year && (filter.DaAnno is not int from || year >= from) && (filter.AAnno is not int to || year <= to);
        }

        return [.. points.Where(p =>
        {
            var kindEnabled = p.Kind switch
            {
                MapPointKind.Nascita => filter.Nascite,
                MapPointKind.Morte => filter.Morti,
                MapPointKind.Sepoltura => filter.Sepolture,
                MapPointKind.Evento => filter.Eventi,
                _ => filter.Luoghi,
            };
            if (!kindEnabled)
            {
                return false;
            }

            if (p.Kind == MapPointKind.Luogo)
            {
                // I luoghi dell'archivio non appartengono a persone o periodi: compaiono solo senza filtri su persona e dinastia.
                return filter.PersonId is null && filter.Famiglie is null;
            }

            if (p.Kind == MapPointKind.Evento && filter.TipoEvento.Length > 0 && !Comparer.Equals(p.TipoEvento, filter.TipoEvento))
            {
                return false;
            }

            if (filter.PersonId is int person && !p.PersoneIds.Contains(person))
            {
                return false;
            }

            if (filter.Famiglie is not null && !p.PersoneIds.Any(InFamilies))
            {
                return false;
            }

            return InPeriod(p);
        })];
    }

    // Raggruppa i punti con le stesse coordinate (arrotondate a ~10 m) in un solo segnaposto.
    public static List<MapLocation> GroupByLocation(IEnumerable<MapPoint> points)
        => [.. points
            .GroupBy(p => (Math.Round(p.Latitudine, 4), Math.Round(p.Longitudine, 4)))
            .Select(g =>
            {
                var ordered = g.OrderBy(p => p.Kind == MapPointKind.Luogo ? 1 : 0)
                    .ThenBy(p => p.Anno ?? int.MaxValue)
                    .ThenBy(p => p.Titolo, Comparer)
                    .ToList();
                var luogo = ordered.FirstOrDefault(p => p.Kind != MapPointKind.Luogo)?.Luogo ?? ordered[0].Titolo;
                return new MapLocation(g.First().Latitudine, g.First().Longitudine, luogo, ordered);
            })
            .OrderBy(l => l.Luogo, Comparer)];

    // Nomi di famiglia della dinastia e di tutti i suoi rami.
    public static HashSet<string> FamiliesOfDynasty(int dynastyId, IEnumerable<Dinastia> dynasties)
    {
        var list = dynasties.ToList();
        var result = new HashSet<string>(Comparer);
        var pending = new Queue<int>([dynastyId]);
        var seen = new HashSet<int>();
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id))
            {
                continue;
            }

            foreach (var d in list.Where(d => d.Id == id))
            {
                result.Add(d.Nome.Trim());
            }

            foreach (var child in list.Where(d => d.ParentId == id))
            {
                pending.Enqueue(child.Id);
            }
        }

        return result;
    }
}
