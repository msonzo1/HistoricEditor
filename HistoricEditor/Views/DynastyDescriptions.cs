namespace HistoricEditor;

// Famiglie scritte in un campo, mostrate come percorso nella gerarchia delle dinastie registrate:
// «gens Flavia; Flavii Sabini» → «gens Flavia › Flavii Sabini», «Asburgo di Spagna» → «Casa d'Asburgo › Asburgo di Spagna».
internal static class DynastyDescriptions
{
    // Percorso (le famiglie indipendenti separate da « · ») e nota sulle famiglie non ancora registrate.
    public static (string Path, string Note) Summarize(IReadOnlyList<Dinastia> dynasties, string text, bool gens)
    {
        var names = PeopleGrouping.FamilyNames(text);
        if (names.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        Dinastia? Find(string name) => dynasties.FirstOrDefault(d => PeopleGrouping.FamilyComparer.Equals(d.Nome.Trim(), name));

        // Antenati registrati di ciascuna famiglia, dalla radice.
        List<string> Ancestors(Dinastia dynasty)
        {
            var result = new List<string>();
            var visited = new HashSet<int> { dynasty.Id };
            var current = dynasty;
            while (current.ParentId is int p && visited.Add(p) && dynasties.FirstOrDefault(d => d.Id == p) is { } parent)
            {
                result.Insert(0, parent.Nome.Trim());
                current = parent;
            }

            return result;
        }

        var paths = names.Select(n => Find(n) is { } d ? [.. Ancestors(d), n] : new List<string> { n }).ToList();
        // Una famiglia già compresa nel percorso di un'altra indicata (la gens del ramo) non si ripete.
        var shown = paths
            .Where(p => !paths.Any(other => !ReferenceEquals(other, p) && other.Count > p.Count
                && PeopleGrouping.FamilyComparer.Equals(other[p.Count - 1], p[^1])))
            .Select(p => string.Join(" › ", p))
            .Distinct(PeopleGrouping.FamilyComparer);

        var missing = names.Where(n => Find(n) is null).ToList();
        var note = missing.Count == 0
            ? string.Empty
            : (gens ? (missing.Count == 1 ? "Nuova gens" : "Nuove gens") : (missing.Count == 1 ? "Nuova dinastia" : "Nuove dinastie"))
                + (names.Count == 1 ? ", da registrare al salvataggio" : $", da registrare al salvataggio: {string.Join(", ", missing)}");
        return (string.Join(" · ", shown), note);
    }

    // Suggerimento sotto un campo: il percorso solo se aggiunge una gerarchia, poi la nota sulle famiglie nuove.
    public static string Hint(IReadOnlyList<Dinastia> dynasties, string text, bool gens)
    {
        var (path, note) = Summarize(dynasties, text, gens);
        return string.Join("\n", new[] { path.Contains('›') ? path : string.Empty, note }.Where(s => s.Length > 0));
    }
}
