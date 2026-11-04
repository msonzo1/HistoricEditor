using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Parente letto da Wikidata e non ancora in archivio, con il legame rispetto al personaggio importato.
public sealed record MissingRelative(string WikidataId, string Ruolo);

public sealed record ImportLinkResult(int Relatives, int Relations);

// Creazione e collegamento delle schede importate senza passare dal modulo (parenti e partecipanti mancanti).
public static class ImportedPersonCreator
{
    // Stessi dati che il modulo compila da un import, con i luoghi creati solo se la gerarchia è certa.
    public static HistoricalPerson BuildPerson(WikipediaImportResult import)
    {
        var citation = import.ArticleUrl.Trim().Length == 0 ? null : new SourceCitation { Title = "Fonte inserita", Url = import.ArticleUrl.Trim() };
        var birth = FormatDates(import.BirthDates);
        var death = FormatDates(import.DeathDates);
        return new HistoricalPerson
        {
            Name = import.Name.Trim(),
            Surname = import.Surname.Trim(),
            Suffix = import.Suffix.Trim(),
            Family = import.Family.Trim(),
            Sesso = import.Sesso,
            Nickname = import.Nickname.Trim(),
            MainTitle = import.MainTitle.Trim(),
            Titles = [.. import.Titles.Where(t => !t.IsEmpty()).Select(t => t.Clone())],
            LocalizedNames = LocalizedPersonName.Normalize(import.LocalizedNames),
            Birth = birth,
            Death = death,
            BirthCalendario = ImportedCalendar(import.BirthDates),
            BirthStile = HistoricalCalendar.StileComune,
            DeathCalendario = ImportedCalendar(import.DeathDates),
            DeathStile = HistoricalCalendar.StileComune,
            WikipediaTitle = import.ArticleTitle,
            WikipediaUrl = import.ArticleUrl,
            WikipediaExtract = import.Extract,
            WikidataId = import.WikidataId,
            WikiTreeId = import.WikiTreeId,
            BirthDateEvidence = DateEvidenceBuilder.Build(birth, string.Empty, import.BirthDates, citation),
            DeathDateEvidence = DateEvidenceBuilder.Build(death, string.Empty, import.DeathDates, citation),
            BirthPlaces = [.. import.BirthPlaces],
            DeathPlaces = [.. import.DeathPlaces],
            BurialPlaces = [.. import.BurialPlaces],
            WikipediaLeads = [.. import.WikipediaLeads.Select(l => l.Clone())],
        };
    }

    internal static string FormatDates(IEnumerable<HistoricalDateEvidence> evidence) =>
        string.Join("; ", evidence.Select(item => item.Value).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    // Le date Wikidata al giorno o al mese registrate nel calendario giuliano fanno proporre quel calendario.
    internal static string ImportedCalendar(IEnumerable<HistoricalDateEvidence> evidence)
        => evidence.Any(e => e.Calendario == HistoricalCalendar.Giuliano && e.Precision is "giorno" or "mese")
            ? HistoricalCalendar.Giuliano
            : HistoricalCalendar.NonIndicato;

    // Salva la scheda, registra le dinastie e collega parenti e relazioni già in archivio.
    // Restituisce la scheda e il numero di luoghi rimasti da scegliere a mano.
    public static (HistoricalPerson Person, int UnresolvedPlaces) Create(PeopleStore store, WikipediaImportResult import)
    {
        var person = BuildPerson(import);
        var unresolved = 0;
        (person.BirthLuogoId, person.BirthLuogoSpecificoId) = ResolvePlace(store, import.BirthPlaces, ref unresolved);
        (person.DeathLuogoId, person.DeathLuogoSpecificoId) = ResolvePlace(store, import.DeathPlaces, ref unresolved);
        (person.BurialLuogoId, person.BurialLuogoSpecificoId) = ResolvePlace(store, import.BurialPlaces, ref unresolved);
        person.BirthPlace = PlaceText(store, person.BirthLuogoId, person.BirthLuogoSpecificoId);
        person.DeathPlace = PlaceText(store, person.DeathLuogoId, person.DeathLuogoSpecificoId);
        person.BurialPlace = PlaceText(store, person.BurialLuogoId, person.BurialLuogoSpecificoId);

        store.Add(person);
        RegisterDynasties(store, person.Family, import);
        LinkRelatives(store, person.Id, import);
        return (person, unresolved);
    }

    private static (int?, int?) ResolvePlace(PeopleStore store, IEnumerable<HistoricalPlaceRecord> records, ref int unresolved)
    {
        if (records.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.CurrentName)) is not { } place)
        {
            return (null, null);
        }

        try
        {
            var result = ImportedPlaceResolver.ResolveIfConfident(store, place);
            if (result is (null, null))
            {
                unresolved++;
            }

            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            unresolved++;
            return (null, null);
        }
    }

    private static string PlaceText(PeopleStore store, int? luogoId, int? specificoId)
        => luogoId is null && specificoId is null ? string.Empty : store.DescribeRole(luogoId, specificoId).DisplayText;

    // Ogni famiglia scritta diventa una dinastia; quelle importate si collegano alla dinastia di cui sono ramo.
    // Restituisce false se la registrazione non è riuscita (la scheda resta comunque salvata).
    public static bool RegisterDynasties(PeopleStore store, string family, WikipediaImportResult? import)
    {
        try
        {
            foreach (var name in PeopleGrouping.FamilyNames(family))
            {
                store.EnsureDynasty(name, string.Empty, null);
            }

            foreach (var dynasty in import?.Dynasties.Where(d => d.Nome.Trim().Length > 0) ?? [])
            {
                int? parentId = dynasty.ParentNome.Trim().Length > 0
                    ? store.EnsureDynasty(dynasty.ParentNome, dynasty.ParentWikidataId, null)
                    : null;
                store.EnsureDynasty(dynasty.Nome, dynasty.WikidataId, parentId);
            }

            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            return false;
        }
    }

    // Collega i parenti letti da Wikidata solo se già presenti in archivio (match per WikidataId).
    // I genitori già indicati e le unioni esistenti non vengono toccati.
    public static ImportLinkResult LinkRelatives(PeopleStore store, int personId, WikipediaImportResult import)
    {
        var (currentMother, currentFather) = store.GetParents(personId);
        var linked = 0;
        if (currentFather is null)
        {
            foreach (var fatherId in import.FatherIds)
            {
                linked += TryLink(store, personId, fatherId, id => store.SetParent(personId, id, mother: false));
            }
        }

        if (currentMother is null)
        {
            foreach (var motherId in import.MotherIds)
            {
                linked += TryLink(store, personId, motherId, id => store.SetParent(personId, id, mother: true));
            }
        }

        foreach (var spouseId in import.SpouseIds)
        {
            if (store.FindPersonIdByWikidataId(spouseId) is int existing && store.HasUnion(personId, existing))
            {
                continue;
            }

            linked += TryLink(store, personId, spouseId, id => store.SaveUnion(new Unione { Persona1Id = personId, Persona2Id = id }));
        }

        var ownSex = store.GetSex(personId);
        if (ownSex is Sessi.Maschio or Sessi.Femmina)
        {
            foreach (var childId in import.ChildIds)
            {
                if (store.FindPersonIdByWikidataId(childId) is int child)
                {
                    var (childMother, childFather) = store.GetParents(child);
                    if ((ownSex == Sessi.Femmina ? childMother : childFather) is not null)
                    {
                        continue;
                    }
                }

                linked += TryLink(store, personId, childId, id => store.AssignChild(personId, id, null));
            }

            if (ownSex == Sessi.Maschio)
            {
                foreach (var childId in import.ChildIds)
                {
                    if (store.FindPersonIdByWikidataId(childId) is int child)
                    {
                        InheritFamily(store, child);
                    }
                }
            }
        }

        InheritFamily(store, personId);
        return new ImportLinkResult(linked, LinkRelations(store, personId, import.Relations));
    }

    private static void InheritFamily(PeopleStore store, int personId)
    {
        try
        {
            store.InheritFatherFamilyIfMissing(personId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
        }
    }

    // Relazioni lette da Wikidata (maestri, allievi, influenze): collegate solo se l'altro personaggio è già in archivio.
    public static int LinkRelations(PeopleStore store, int personId, IEnumerable<ImportedRelation> relations)
    {
        var linked = 0;
        foreach (var relation in relations)
        {
            if (store.FindPersonIdByWikidataId(relation.WikidataId) is not int otherId || otherId == personId)
            {
                continue;
            }

            var (persona1, persona2) = relation.Attivo ? (personId, otherId) : (otherId, personId);
            if (store.HasRelation(persona1, persona2, relation.Tipo))
            {
                continue;
            }

            try
            {
                store.SaveRelation(new Relazione { Persona1Id = persona1, Persona2Id = persona2, Tipo = relation.Tipo, Note = "Importata da Wikidata." });
                linked++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
            {
            }
        }

        return linked;
    }

    // Restituisce 1 solo se il parente era in archivio ed è stato collegato.
    private static int TryLink(PeopleStore store, int personId, string wikidataId, Action<int> link)
    {
        if (store.FindPersonIdByWikidataId(wikidataId) is not int id || id == personId)
        {
            return 0;
        }

        try
        {
            link(id);
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return 0;
        }
    }

    // Parenti indicati da Wikidata che non hanno ancora una scheda (padre, madre, coniugi, figli), senza ripetizioni.
    public static IReadOnlyList<MissingRelative> MissingRelatives(PeopleStore store, WikipediaImportResult import)
    {
        var candidates = import.FatherIds.Select(id => new MissingRelative(id, "padre"))
            .Concat(import.MotherIds.Select(id => new MissingRelative(id, "madre")))
            .Concat(import.SpouseIds.Select(id => new MissingRelative(id, "coniuge")))
            .Concat(import.ChildIds.Select(id => new MissingRelative(id, "figlio o figlia")))
            .Where(r => r.WikidataId.Length > 0 && !string.Equals(r.WikidataId, import.WikidataId, StringComparison.Ordinal))
            .DistinctBy(r => r.WikidataId, StringComparer.Ordinal)
            .ToList();
        var existing = store.FindPeopleByWikidataIds(candidates.Select(c => c.WikidataId));
        return [.. candidates.Where(c => !existing.ContainsKey(c.WikidataId))];
    }
}
