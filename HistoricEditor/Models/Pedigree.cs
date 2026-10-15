namespace HistoricEditor;

// Casella dell'albero degli antenati: generazione 0 = persona centrale, 1 = genitori, ... 4 = trisnonni.
// Nella generazione g la casella i ha come padre la casella 2i e come madre la 2i+1 della generazione g+1.
public sealed class PedigreeSlot
{
    public required int Generation { get; init; }
    public required int Index { get; init; }
    public PersonSummary? Person { get; init; }

    // Persona di cui questa casella è il genitore; null per la radice o se il figlio non è inserito.
    public int? ChildId { get; init; }
    public bool IsMother => Generation > 0 && Index % 2 == 1;

    public bool IsFilled => Person is not null;
    public bool CanAdd => Person is null && ChildId is not null;
    public bool IsRoot => Generation == 0;

    public string Ruolo => Pedigree.DescribeRole(Generation, Index);
    public string Nome => Person?.Nome ?? string.Empty;
    public string Anni => Person?.Anni ?? string.Empty;
    public string AccessibleName => Person is not null
        ? $"{Ruolo}: {Person.Etichetta}"
        : CanAdd ? $"Aggiungi {Ruolo.ToLowerInvariant()}" : string.Empty;
}

public static class Pedigree
{
    // Persona, genitori, nonni, bisnonni, trisnonni.
    public const int Generations = 5;

    public static IReadOnlyList<IReadOnlyList<PedigreeSlot>> Build(
        int rootId,
        Func<int, PersonSummary?> summary,
        Func<int, (int? MotherId, int? FatherId)> parents,
        int generations = Generations)
    {
        var result = new List<IReadOnlyList<PedigreeSlot>>();
        var current = new List<PedigreeSlot> { new() { Generation = 0, Index = 0, Person = summary(rootId) } };
        result.Add(current);
        for (var g = 1; g < generations; g++)
        {
            var next = new List<PedigreeSlot>(current.Count * 2);
            foreach (var child in current)
            {
                int? fatherId = null, motherId = null;
                if (child.Person is { } person)
                {
                    (motherId, fatherId) = parents(person.Id);
                }

                next.Add(Slot(g, child.Index * 2, fatherId, child.Person?.Id));
                next.Add(Slot(g, child.Index * 2 + 1, motherId, child.Person?.Id));
            }

            result.Add(next);
            current = next;
        }

        return result;

        PedigreeSlot Slot(int generation, int index, int? personId, int? childId) => new()
        {
            Generation = generation,
            Index = index,
            Person = personId is int id ? summary(id) : null,
            ChildId = childId,
        };
    }

    public static string DescribeRole(int generation, int index)
    {
        var mother = index % 2 == 1;
        var baseName = generation switch
        {
            0 => "Persona",
            1 => mother ? "Madre" : "Padre",
            2 => mother ? "Nonna" : "Nonno",
            3 => mother ? "Bisnonna" : "Bisnonno",
            4 => mother ? "Trisnonna" : "Trisnonno",
            _ => mother ? "Antenata" : "Antenato",
        };

        if (generation < 2)
        {
            return baseName;
        }

        // Metà superiore della generazione = ramo del padre.
        var paternal = index < (1 << generation) / 2;
        var side = paternal ? "patern" : "matern";
        return $"{baseName} {side}{(mother ? "a" : "o")}";
    }
}
