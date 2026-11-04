using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Parente presente su WikiTree ma senza scheda in archivio. WikidataId è vuoto se WikiTree non è collegato a Wikidata.
public sealed record WikiTreeRelative(WikiTreeProfile Profile, string Ruolo, string WikidataId);

// Completa le parentele con i dati WikiTree: collega le schede già in archivio e crea quelle scelte dall'utente.
public static class WikiTreeLinker
{
    // Id numerico WikiTree → Id della scheda, per il personaggio e i suoi parenti già in archivio.
    // Il confronto avviene sul profilo WikiTree registrato e, in mancanza, sul Q-id Wikidata.
    public static Dictionary<int, int> ResolveArchive(PeopleStore store, int personId, WikiTreeFamily family, IReadOnlyDictionary<string, string> wikidataIds)
    {
        var profiles = family.Relatives.ToList();
        var byKey = store.FindPeopleByWikiTreeIds(profiles.Select(p => p.Key));
        var byQid = store.FindPeopleByWikidataIds(profiles.Select(p => wikidataIds.GetValueOrDefault(p.Key, string.Empty)));
        var result = new Dictionary<int, int> { [family.Person.Id] = personId };
        foreach (var profile in profiles)
        {
            if (byKey.TryGetValue(profile.Key, out var id)
                || (wikidataIds.TryGetValue(profile.Key, out var qid) && byQid.TryGetValue(qid, out id)))
            {
                if (id != personId)
                {
                    result.TryAdd(profile.Id, id);
                }
            }
        }

        return result;
    }

    // Collega genitori, coniugi e figli già in archivio e, per i figli, anche l'altro genitore indicato da WikiTree.
    // Non sostituisce genitori già indicati né duplica unioni. Restituisce il numero di legami aggiunti.
    public static int Link(PeopleStore store, int personId, WikiTreeFamily family, IReadOnlyDictionary<string, string> wikidataIds)
    {
        var archive = ResolveArchive(store, personId, family, wikidataIds);
        var profiles = family.Relatives.Append(family.Person).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        foreach (var (wikiTreeId, archiveId) in archive)
        {
            Try(() => store.SetWikiTreeIdIfMissing(archiveId, profiles[wikiTreeId].Key));
        }

        var linked = 0;
        linked += LinkParents(store, personId, family.Person, archive);
        foreach (var spouse in family.Spouses)
        {
            if (archive.TryGetValue(spouse.Id, out var spouseId) && !store.HasUnion(personId, spouseId))
            {
                linked += Try(() => store.SaveUnion(new Unione { Persona1Id = personId, Persona2Id = spouseId }));
            }
        }

        foreach (var child in family.Children)
        {
            if (archive.TryGetValue(child.Id, out var childId))
            {
                linked += LinkParents(store, childId, child, archive);
            }
        }

        return linked;
    }

    private static int LinkParents(PeopleStore store, int childId, WikiTreeProfile child, IReadOnlyDictionary<int, int> archive)
    {
        var (motherId, fatherId) = store.GetParents(childId);
        var linked = 0;
        if (fatherId is null && child.FatherId is int f && archive.TryGetValue(f, out var father) && father != childId)
        {
            linked += Try(() => store.SetParent(childId, father, mother: false));
        }

        if (motherId is null && child.MotherId is int m && archive.TryGetValue(m, out var mother) && mother != childId)
        {
            linked += Try(() => store.SetParent(childId, mother, mother: true));
        }

        return linked;
    }

    private static int Try(Action action)
    {
        try
        {
            action();
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return 0;
        }
    }

    // Parenti WikiTree senza scheda, esclusi quelli già proposti da Wikidata (stesso Q-id).
    public static IReadOnlyList<WikiTreeRelative> MissingRelatives(
        PeopleStore store,
        int personId,
        WikiTreeFamily family,
        IReadOnlyDictionary<string, string> wikidataIds,
        IEnumerable<string> proposedWikidataIds)
    {
        var archive = ResolveArchive(store, personId, family, wikidataIds);
        var proposed = proposedWikidataIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var childNames = family.Children.ToLookup(c => c.MotherId ?? 0);
        var childNamesByFather = family.Children.ToLookup(c => c.FatherId ?? 0);
        var candidates = family.Parents.Select(p => (p, IsFemale(p) ? "madre" : IsMale(p) ? "padre" : "genitore"))
            .Concat(family.Spouses.Select(p => (p, "coniuge")))
            .Concat(family.Children.Select(p => (p, "figlio o figlia")))
            .Concat(family.OtherParents.Select(p => (p, OtherParentRole(p, IsFemale(p) ? childNames[p.Id] : childNamesByFather[p.Id]))));
        return
        [
            .. candidates
                .Where(c => c.p.Id != family.Person.Id && !archive.ContainsKey(c.p.Id))
                .DistinctBy(c => c.p.Id)
                .Select(c => new WikiTreeRelative(c.p, c.Item2, wikidataIds.GetValueOrDefault(c.p.Key, string.Empty)))
                .Where(r => r.WikidataId.Length == 0 || !proposed.Contains(r.WikidataId)),
        ];
    }

    private static string OtherParentRole(WikiTreeProfile parent, IEnumerable<WikiTreeProfile> children)
    {
        var names = children.Select(c => c.GivenName.Length > 0 ? c.GivenName : c.Key).ToList();
        var role = IsFemale(parent) ? "madre" : IsMale(parent) ? "padre" : "genitore";
        return names.Count == 0 ? $"{role} di un figlio" : $"{role} di {string.Join(", ", names)}";
    }

    private static bool IsFemale(WikiTreeProfile p) => string.Equals(p.Gender, "Female", StringComparison.OrdinalIgnoreCase);

    private static bool IsMale(WikiTreeProfile p) => string.Equals(p.Gender, "Male", StringComparison.OrdinalIgnoreCase);

    // Scheda costruita dai soli dati WikiTree: nome come scritto su WikiTree, date in testo, luoghi da scegliere a mano.
    public static HistoricalPerson BuildPerson(WikiTreeProfile profile, string wikidataId)
    {
        var citation = new SourceCitation { Title = "WikiTree", Url = profile.Url, RetrievedAt = DateTime.Today.ToString("yyyy-MM-dd") };
        var birth = WikipediaImportService.FormatWikiTreeDate(profile.BirthDate, profile.BirthStatus);
        var death = WikipediaImportService.FormatWikiTreeDate(profile.DeathDate, profile.DeathStatus);
        return new HistoricalPerson
        {
            Name = profile.GivenName.Length > 0 ? profile.GivenName : profile.Key,
            Surname = profile.LastNameAtBirth,
            Suffix = profile.Suffix,
            Sesso = IsMale(profile) ? Sessi.Maschio : IsFemale(profile) ? Sessi.Femmina : string.Empty,
            Birth = birth,
            Death = death,
            BirthStile = HistoricalCalendar.StileComune,
            DeathStile = HistoricalCalendar.StileComune,
            WikidataId = wikidataId,
            WikiTreeId = profile.Key,
            BirthDateEvidence = DateEvidenceBuilder.Build(birth, string.Empty, null, citation),
            DeathDateEvidence = DateEvidenceBuilder.Build(death, string.Empty, null, citation),
            BirthPlaces = PlaceRecords(profile.BirthLocation, citation),
            DeathPlaces = PlaceRecords(profile.DeathLocation, citation),
        };
    }

    // Il luogo WikiTree è solo testo: resta tra i dati importati e va scelto a mano nella scheda.
    private static List<HistoricalPlaceRecord> PlaceRecords(string location, SourceCitation citation)
        => location.Trim().Length == 0
            ? []
            : [new HistoricalPlaceRecord { CurrentName = location.Trim(), Sources = [new SourceCitation { Title = citation.Title, Url = citation.Url, RetrievedAt = citation.RetrievedAt }] }];

    // Salva la scheda; restituisce la scheda e il numero di luoghi rimasti da scegliere.
    public static (HistoricalPerson Person, int UnresolvedPlaces) Create(PeopleStore store, WikiTreeProfile profile, string wikidataId)
    {
        var person = BuildPerson(profile, wikidataId);
        store.Add(person);
        return (person, (profile.BirthLocation.Length > 0 ? 1 : 0) + (profile.DeathLocation.Length > 0 ? 1 : 0));
    }
}
