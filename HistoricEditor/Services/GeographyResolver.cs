namespace HistoricEditor;

public sealed class DerivedPlace
{
    public DerivedPlace(List<GeoLevel> levels, bool confident, string? specificName)
    {
        Levels = levels;
        Confident = confident;
        SpecificName = specificName;
    }

    public List<GeoLevel> Levels { get; }

    // true solo se la catena mappa in modo pulito sullo schema e può essere inserita senza conferma.
    public bool Confident { get; }

    // Valorizzato quando il luogo importato non è un'unità amministrativa (monastero, reggia…).
    public string? SpecificName { get; }
}

public static class GeographyResolver
{
    // Deriva i livelli secondo lo schema del Paese ricostruendo la catena amministrativa P131.
    public static DerivedPlace DeriveLevels(AppDbContext context, HistoricalPlaceRecord place)
    {
        var country = place.CurrentCountry?.Trim() ?? string.Empty;
        var placeName = place.CurrentName?.Trim() ?? string.Empty;
        var schema = GeoSchemas.For(country);
        var ancestors = BuildAncestors(place);
        var leafIndex = schema.Length - 1;
        var countryNames = CountryNames(place, country);
        var (countryLat, countryLon) = CountryCoordinate(place, country);

        if (!GeoSchemas.IsKnown(country))
        {
            var last = ancestors.Count > 0 ? ancestors[^1] : null;
            List<GeoLevel> proposal =
            [
                new(schema[0], country) { Nomi = countryNames, Latitudine = countryLat, Longitudine = countryLon },
                new(schema[1], last?.Name ?? string.Empty) { Nomi = last?.Names ?? [], Latitudine = last?.Latitudine, Longitudine = last?.Longitudine },
                new(schema[leafIndex], placeName) { Nomi = place.Names, Latitudine = place.Latitudine, Longitudine = place.Longitudine },
            ];
            return new DerivedPlace(proposal, false, null);
        }

        var levels = new List<GeoLevel> { new(schema[0], country) { Nomi = countryNames, Latitudine = countryLat, Longitudine = countryLon } };

        if (GeoSchemas.UsesInfoboxDivisions(country) && place.InfoboxDivisions.Count >= leafIndex - 1)
        {
            for (var i = 1; i < leafIndex; i++)
            {
                var name = place.InfoboxDivisions[i - 1];
                levels.Add(string.Equals(name, InfoboxDivisionParser.Absent, StringComparison.OrdinalIgnoreCase)
                    ? new GeoLevel(schema[i], string.Empty) { Assente = true }
                    : new GeoLevel(schema[i], name));
            }

            levels.Add(new GeoLevel(schema[leafIndex], placeName) { Nomi = place.Names, Latitudine = place.Latitudine, Longitudine = place.Longitudine });
            return new DerivedPlace(levels, IsComplete(levels), null);
        }

        var next = 0;
        var k = 1;
        while (k < leafIndex)
        {
            var ancestor = next < ancestors.Count ? ancestors[next++] : null;
            levels.Add(new GeoLevel(schema[k], ancestor?.Name ?? string.Empty) { Nomi = ancestor?.Names ?? [], Latitudine = ancestor?.Latitudine, Longitudine = ancestor?.Longitudine });
            k++;

            if (ancestor is { Name.Length: > 0 } && k < leafIndex && SuggestsNoLowerLevel(context, country, schema[k - 1], ancestor.Name))
            {
                levels.Add(new GeoLevel(schema[k], string.Empty) { Assente = true });
                k++;
            }
        }

        var remaining = ancestors.Count - next;
        if (remaining == 0)
        {
            levels.Add(new GeoLevel(schema[leafIndex], placeName) { Nomi = place.Names, Latitudine = place.Latitudine, Longitudine = place.Longitudine });
            return new DerivedPlace(levels, IsComplete(levels), null);
        }

        // Un livello in più: il luogo è contenuto nel suo genitore diretto, che diventa la foglia.
        var directParent = ancestors[^1];
        levels.Add(new GeoLevel(schema[leafIndex], directParent.Name) { Nomi = directParent.Names, Latitudine = directParent.Latitudine, Longitudine = directParent.Longitudine });
        return new DerivedPlace(levels, remaining == 1 && IsComplete(levels), placeName);
    }

    public static bool SuggestsNoLowerLevel(AppDbContext context, string country, string tipo, string nome)
        => GeoSchemas.HintsNoLowerLevel(country, tipo, nome)
            || context.Luoghi.Any(l => l.Tipo == tipo && l.Nome == nome && l.SenzaLivelloInferiore);

    // Ricostruisce i nomi degli antenati amministrativi (P131) dal comune fino sotto lo Stato, ordinati dall'alto al basso.
    private sealed record Ancestor(string Name, List<PlaceName> Names, double? Latitudine, double? Longitudine);

    private static List<Ancestor> BuildAncestors(HistoricalPlaceRecord place)
    {
        var parentOf = new Dictionary<string, PlaceDivision>(StringComparer.Ordinal);
        foreach (var division in place.AdministrativeDivisions)
        {
            if (!division.Relation.StartsWith("Unità amministrativa", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(division.SubjectWikidataId) && !parentOf.ContainsKey(division.SubjectWikidataId))
            {
                parentOf[division.SubjectWikidataId] = division;
            }
        }

        var countryIds = place.AdministrativeDivisions
            .Where(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.WikidataId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var lowToHigh = new List<Ancestor>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = place.WikidataId;

        while (!string.IsNullOrEmpty(current) && visited.Add(current) && parentOf.TryGetValue(current, out var parent))
        {
            var parentId = parent.WikidataId;
            if (string.IsNullOrEmpty(parentId) || countryIds.Contains(parentId))
            {
                break;
            }

            var name = parent.Name?.Trim() ?? string.Empty;
            if (name.Length > 0)
            {
                lowToHigh.Add(new Ancestor(name, parent.Names, parent.Latitudine, parent.Longitudine));
            }

            current = parentId;
        }

        lowToHigh.Reverse();
        return lowToHigh;
    }

    // Nomi multilingua del Paese dalla divisione "Paese" corrispondente.
    private static List<PlaceName> CountryNames(HistoricalPlaceRecord place, string country)
        => place.AdministrativeDivisions
               .Where(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase))
               .FirstOrDefault(d => string.Equals(d.Name?.Trim(), country, StringComparison.OrdinalIgnoreCase))?.Names
           ?? place.AdministrativeDivisions
               .FirstOrDefault(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase))?.Names
           ?? [];

    // Coordinate del Paese dalla divisione "Paese" corrispondente.
    private static (double? Latitudine, double? Longitudine) CountryCoordinate(HistoricalPlaceRecord place, string country)
    {
        var division = place.AdministrativeDivisions
                .Where(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(d => string.Equals(d.Name?.Trim(), country, StringComparison.OrdinalIgnoreCase))
            ?? place.AdministrativeDivisions
                .FirstOrDefault(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase));
        return (division?.Latitudine, division?.Longitudine);
    }

    // Copia i nomi multilingua distinti per lingua, scartando quelli vuoti.
    private static List<PlaceName> CloneNames(IEnumerable<PlaceName> names)
        => names
            .Where(n => n.Name.Trim().Length > 0)
            .GroupBy(n => n.Language, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PlaceName { Language = g.Key, Name = g.First().Name.Trim() })
            .ToList();

    public static bool IsComplete(IReadOnlyList<GeoLevel> levels)
        => levels.Count > 0
            && !levels[0].Assente
            && !levels[^1].Assente
            && levels.All(l => l.Assente || !string.IsNullOrWhiteSpace(l.Nome));

    // Crea i nodi mancanti dall'alto verso il basso e restituisce la foglia; null se la catena è incompleta.
    public static Luogo? EnsureChain(AppDbContext context, IReadOnlyList<GeoLevel> levels, double? latitudine = null, double? longitudine = null)
    {
        if (!IsComplete(levels))
        {
            return null;
        }

        int? parentId = null;
        var nodes = new Luogo?[levels.Count];

        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i].Assente)
            {
                continue;
            }

            var tipo = levels[i].Tipo.Trim();
            var nome = levels[i].Nome.Trim();
            var pid = parentId;

            var existing = pid is null
                ? context.Luoghi.FirstOrDefault(l => l.ParentId == null && l.Tipo == tipo && l.Nome == nome)
                : context.Luoghi.FirstOrDefault(l => l.ParentId == pid && l.Tipo == tipo && l.Nome == nome);

            if (existing is null)
            {
                existing = new Luogo { Nome = nome, Tipo = tipo, ParentId = pid };
                context.Luoghi.Add(existing);
                context.SaveChanges();
            }

            // Imposta i nomi multilingua solo se mancanti: riassegnare la collezione owned-JSON
            // di un nodo già tracciato con nomi provoca un conflitto di tracking in EF (crash al re-import).
            if (existing.NomiAltreLingue.Count == 0)
            {
                var otherNames = CloneNames(levels[i].Nomi);
                if (otherNames.Count > 0)
                {
                    existing.NomiAltreLingue = otherNames;
                }
            }

            // Coordinate (P625) dal livello importato, solo se il nodo non ne ha già.
            existing.Latitudine ??= levels[i].Latitudine;
            existing.Longitudine ??= levels[i].Longitudine;

            nodes[i] = existing;
            parentId = existing.Id;
        }

        // Il flag segue l'ultima catena confermata, così l'utente può correggerlo.
        for (var i = 0; i < levels.Count - 1; i++)
        {
            if (nodes[i] is { } node)
            {
                node.SenzaLivelloInferiore = levels[i + 1].Assente;
            }
        }

        var leaf = nodes[^1]!;
        if (latitudine.HasValue || longitudine.HasValue)
        {
            leaf.Latitudine = latitudine ?? leaf.Latitudine;
            leaf.Longitudine = longitudine ?? leaf.Longitudine;
        }

        context.SaveChanges();
        return leaf;
    }
}
