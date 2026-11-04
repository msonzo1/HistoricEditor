using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Nomi storici dei luoghi (Bisanzio → Costantinopoli → Istanbul) e descrizione col nome dell'epoca.
public sealed partial class PeopleStore
{
    public Luogo? GetPlace(int id) => context.Luoghi.AsNoTracking().FirstOrDefault(l => l.Id == id);

    public void SetPlaceHistoricNames(int id, IEnumerable<NomeStorico> names)
    {
        if (context.Luoghi.Find(id) is not { } luogo)
        {
            return;
        }

        luogo.NomiStorici = [.. names
            .Where(n => n.Nome.Trim().Length > 0)
            .Select(n => new NomeStorico { Nome = n.Nome.Trim(), Dal = n.Dal, Al = n.Al, Lingua = n.Lingua.Trim(), Fonte = n.Fonte.Trim() })
            .OrderBy(n => n.Dal ?? int.MinValue).ThenBy(n => n.Al ?? int.MaxValue)];
        SaveOrRevert();
    }

    // Aggiunge i nomi importati a quelli del luogo e ne ricorda l'elemento Wikidata se mancava.
    public void MergePlaceHistoricNames(int id, IEnumerable<NomeStorico> names, string wikidataId = "")
    {
        if (context.Luoghi.Find(id) is not { } luogo)
        {
            return;
        }

        var incoming = names.ToList();
        if (incoming.Count == 0 && (wikidataId.Length == 0 || luogo.WikidataId.Length > 0))
        {
            context.ChangeTracker.Clear();
            return;
        }

        luogo.NomiStorici = HistoricalPlaceNames.Merge(luogo.NomiStorici, incoming, luogo.Nome);
        if (luogo.WikidataId.Length == 0 && wikidataId.Trim().Length > 0)
        {
            luogo.WikidataId = wikidataId.Trim();
        }

        SaveOrRevert();
    }

    // Come DescribePlace, ma ogni livello che nell'anno aveva un altro nome appare come «Costantinopoli (oggi Istanbul)».
    public string DescribePlaceAt(int? luogoId, int? year)
    {
        var parts = new List<string>();
        var current = luogoId is int id ? context.Luoghi.AsNoTracking().FirstOrDefault(l => l.Id == id) : null;
        while (current is not null && parts.Count < 32)
        {
            parts.Add(HistoricalPlaceNames.Describe(current.Nome, current.NomiStorici, year));
            current = current.ParentId is int parentId ? context.Luoghi.AsNoTracking().FirstOrDefault(l => l.Id == parentId) : null;
        }

        return string.Join(", ", parts);
    }

    public PlaceDescription DescribeRoleAt(int? luogoId, int? specificoId, int? year)
    {
        if (year is null)
        {
            return DescribeRole(luogoId, specificoId);
        }

        if (specificoId is int id && context.LuoghiSpecifici.AsNoTracking().FirstOrDefault(s => s.Id == id) is { } specifico)
        {
            return new PlaceDescription(DescribePlaceAt(specifico.LuogoId, year), specifico.Nome);
        }

        return new PlaceDescription(DescribePlaceAt(luogoId, year), string.Empty);
    }
}
