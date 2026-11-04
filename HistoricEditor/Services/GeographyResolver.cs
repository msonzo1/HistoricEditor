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
        var ancestors = BuildAncestors(place, GeoSchemas.LevelTypes(country));
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

        if (GeoSchemas.LevelTypes(country) is { } levelTypes
            && TryDeriveByTypes(context, place, country, schema, ancestors, levelTypes, levels) is { } typed)
        {
            return typed;
        }

        if (GeoSchemas.HasFlexibleDepth(country))
        {
            var slots = leafIndex - 1;
            if (ancestors.Count == 0)
            {
                // Città che è anche suddivisione di primo livello (es. Vienna).
                levels.Add(new GeoLevel(schema[1], placeName) { Nomi = place.Names, Latitudine = place.Latitudine, Longitudine = place.Longitudine });
                for (var i = 2; i < leafIndex; i++)
                {
                    levels.Add(new GeoLevel(schema[i], string.Empty) { Assente = true });
                }
            }
            else
            {
                // I primi livelli dall'alto, l'ultimo slot è sempre il genitore diretto; quelli intermedi in più sono scartati.
                var picked = ancestors.Count > slots
                    ? [.. ancestors.Take(slots - 1), ancestors[^1]]
                    : ancestors;
                for (var i = 0; i < slots; i++)
                {
                    var tipo = schema[i + 1];
                    levels.Add(i < picked.Count
                        ? new GeoLevel(tipo, picked[i].Name) { Nomi = picked[i].Names, Latitudine = picked[i].Latitudine, Longitudine = picked[i].Longitudine }
                        : new GeoLevel(tipo, string.Empty) { Assente = true });
                }
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
    private sealed record Ancestor(string Name, List<PlaceName> Names, double? Latitudine, double? Longitudine, List<string> TypeIds, string? WikidataId = null);

    // Assegna ogni livello all'antenato P131 più vicino con un tipo (P31) previsto per quel livello.
    // Restituisce null se il comune non è riconoscibile dai tipi: si ripiega sulla mappatura per posizione.
    private static DerivedPlace? TryDeriveByTypes(
        AppDbContext context,
        HistoricalPlaceRecord place,
        string country,
        string[] schema,
        List<Ancestor> ancestors,
        IReadOnlyDictionary<string, HashSet<string>> levelTypes,
        List<GeoLevel> levels)
    {
        var leafIndex = schema.Length - 1;
        bool Matches(IEnumerable<string> types, int level)
            => levelTypes.TryGetValue(schema[level], out var expected) && types.Any(expected.Contains);

        var self = new Ancestor(place.CurrentName?.Trim() ?? string.Empty, place.Names, place.Latitudine, place.Longitudine, place.TypeIds, place.WikidataId);
        Ancestor leaf;
        string? specificName = null;
        var leafPosition = ancestors.Count;
        if (Matches(place.TypeIds, leafIndex))
        {
            leaf = self;
        }
        else
        {
            leafPosition = ancestors.FindLastIndex(a => Matches(a.TypeIds, leafIndex));
            if (leafPosition < 0)
            {
                return null;
            }

            leaf = ancestors[leafPosition];
            // Città contenuta nel comune omonimo (es. Amsterdam): non è un luogo specifico.
            specificName = string.Equals(leaf.Name, self.Name, StringComparison.OrdinalIgnoreCase) ? null : self.Name;
        }

        // I livelli si assegnano dal basso: ognuno all'antenato più vicino sopra quello del livello inferiore
        // (es. distretto Cheshire West and Chester, poi contea Cheshire), oppure alla stessa entità se copre
        // più livelli (Berlino, Parigi, City of Bristol).
        var matched = new Ancestor?[leafIndex];
        var lowerPosition = leafPosition;
        var lower = leaf;
        for (var i = leafIndex - 1; i >= 1; i--)
        {
            var position = lowerPosition - 1;
            while (position >= 0 && !Matches(ancestors[position].TypeIds, i))
            {
                position--;
            }

            if (position >= 0)
            {
                matched[i] = ancestors[position];
                lowerPosition = position;
                lower = ancestors[position];
            }
            else if (Matches(lower.TypeIds, i))
            {
                matched[i] = lower;
            }
        }

        var confident = specificName is null || leafPosition == ancestors.Count - 1;
        for (var i = 1; i < leafIndex; i++)
        {
            if (matched[i] is { } match)
            {
                levels.Add(new GeoLevel(schema[i], match.Name) { Nomi = match.Names, Latitudine = match.Latitudine, Longitudine = match.Longitudine });
                continue;
            }

            // Livello fuori dalla catena P131, dedotto dall'entità del livello inferiore (es. regioni della Norvegia).
            var below = i + 1 < leafIndex ? matched[i + 1] : leaf;
            if (GeoSchemas.ImpliedLevel(country, schema[i], below?.WikidataId) is { } implied)
            {
                levels.Add(new GeoLevel(schema[i], implied));
                continue;
            }

            levels.Add(new GeoLevel(schema[i], string.Empty) { Assente = true });
            var previous = levels[^2];
            var upper = i > 1 ? matched[i - 1] : null;
            if (previous.Assente || previous.Nome.Length == 0
                || !(SuggestsNoLowerLevel(context, country, previous.Tipo, previous.Nome)
                    || (upper is not null && GeoSchemas.TypesHintNoLowerLevel(country, upper.TypeIds))))
            {
                confident = false;
            }
        }

        levels.Add(new GeoLevel(schema[leafIndex], leaf.Name) { Nomi = leaf.Names, Latitudine = leaf.Latitudine, Longitudine = leaf.Longitudine });
        return new DerivedPlace(levels, confident && IsComplete(levels), specificName);
    }

    private static List<Ancestor> BuildAncestors(HistoricalPlaceRecord place, IReadOnlyDictionary<string, HashSet<string>>? levelTypes)
    {
        var countryIds = place.AdministrativeDivisions
            .Where(d => d.Relation.StartsWith("Paese", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.WikidataId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        // Con più P131 per lo stesso luogo (es. provincia di Anversa → Belgio e Regione fiamminga) si preferisce
        // il genitore con un tipo previsto dallo schema, poi uno diverso dallo Stato, infine il primo.
        int Rank(PlaceDivision division)
            => levelTypes is not null && levelTypes.Values.Any(types => division.TypeIds.Any(types.Contains)) ? 2
                : countryIds.Contains(division.WikidataId) ? 0
                : 1;

        var parentOf = new Dictionary<string, PlaceDivision>(StringComparer.Ordinal);
        foreach (var division in place.AdministrativeDivisions)
        {
            if (!division.Relation.StartsWith("Unità amministrativa", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(division.SubjectWikidataId))
            {
                continue;
            }

            if (!parentOf.TryGetValue(division.SubjectWikidataId, out var chosen) || Rank(division) > Rank(chosen))
            {
                parentOf[division.SubjectWikidataId] = division;
            }
        }

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
            if (name.Length > 0 && !GeoSchemas.IsTransparentDivision(parentId))
            {
                lowToHigh.Add(new Ancestor(name, parent.Names, parent.Latitudine, parent.Longitudine, parent.TypeIds, parentId));
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
    // Tutta la catena è salvata in un'unica transazione: un errore non lascia livelli parziali.
    public static Luogo? EnsureChain(AppDbContext context, IReadOnlyList<GeoLevel> levels, double? latitudine = null, double? longitudine = null)
    {
        if (!IsComplete(levels))
        {
            return null;
        }

        using var transaction = context.Database.CurrentTransaction is null ? context.Database.BeginTransaction() : null;
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

            var existing = FindChild(context, pid, tipo, nome);

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
        transaction?.Commit();
        return leaf;
    }

    // Figlio con tipo e nome indicati, senza distinguere maiuscole e minuscole («Milano» = «milano»);
    // a parità preferisce la scrittura identica.
    public static Luogo? FindChild(AppDbContext context, int? parentId, string tipo, string nome)
    {
        var candidates = parentId is null
            ? context.Luoghi.Where(l => l.ParentId == null).ToList()
            : context.Luoghi.Where(l => l.ParentId == parentId).ToList();
        return candidates
            .Where(l => string.Equals(l.Tipo, tipo, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(l.Nome, nome, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(l => l.Nome == nome && l.Tipo == tipo)
            .FirstOrDefault();
    }
}
