using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Gentes romane e loro rami (2026-11-15): entità separate dalle dinastie, collegate alle schede per identificativo.
public sealed partial class PeopleStore
{
    private static readonly StringComparer GensComparer = PeopleGrouping.FamilyComparer;

    // Gentes con i loro rami, in ordine di nome.
    public IReadOnlyList<GensRomana> GetGentes()
        => [.. context.Gentes.AsNoTrackingWithIdentityResolution().Include(g => g.Rami).AsEnumerable()
            .OrderBy(g => g.Etichetta, GensComparer)];

    public GensRomana? GetGens(int id)
        => context.Gentes.AsNoTrackingWithIdentityResolution().Include(g => g.Rami).FirstOrDefault(g => g.Id == id);

    public RamoGens? GetRamo(int id) => context.RamiGens.AsNoTracking().FirstOrDefault(r => r.Id == id);

    // Schede con la gens (o un suo ramo), di nascita o d'adozione.
    public int CountPeopleUsingGens(int gensId)
        => context.People.Count(p => p.GensId == gensId || p.GensAdozioneId == gensId
            || (p.RamoNascita != null && p.RamoNascita.GensId == gensId) || (p.RamoAdottivo != null && p.RamoAdottivo.GensId == gensId));

    public int CountPeopleUsingRamo(int ramoId) => context.People.Count(p => p.RamoGensId == ramoId || p.RamoGensAdozioneId == ramoId);

    public int SaveGens(GensRomana data)
    {
        var nome = DynastyNames.Capitalize(data.Nome);
        if (nome.Length == 0)
        {
            throw new InvalidOperationException("Indicare il nome latino della gens (es. «Iulia»).");
        }

        if (context.Gentes.Any(g => g.Id != data.Id && g.Nome == nome))
        {
            throw new InvalidOperationException($"Esiste già la Gens {nome}.");
        }

        var entity = data.Id == 0 ? null : context.Gentes.Find(data.Id);
        if (entity is null)
        {
            entity = new GensRomana();
            context.Gentes.Add(entity);
        }

        entity.Nome = nome;
        entity.Nomen = DynastyNames.Capitalize(data.Nomen);
        entity.Plurale = DynastyNames.Capitalize(data.Plurale);
        entity.NomeItaliano = DynastyNames.Capitalize(data.NomeItaliano);
        entity.PluraleItaliano = DynastyNames.Capitalize(data.PluraleItaliano);
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim();
        entity.Note = data.Note ?? string.Empty;
        SaveOrRevert();
        return entity.Id;
    }

    public int SaveRamo(RamoGens data)
    {
        var plurale = DynastyNames.Capitalize(data.Plurale);
        if (plurale.Length == 0)
        {
            throw new InvalidOperationException("Indicare il nome plurale del ramo (es. «Caesares»).");
        }

        if (!context.Gentes.Any(g => g.Id == data.GensId))
        {
            throw new InvalidOperationException("Indicare la gens del ramo.");
        }

        if (context.RamiGens.Any(r => r.Id != data.Id && r.GensId == data.GensId && r.Plurale == plurale))
        {
            throw new InvalidOperationException($"La gens ha già il ramo «{plurale}».");
        }

        var entity = data.Id == 0 ? null : context.RamiGens.Find(data.Id);
        if (entity is null)
        {
            entity = new RamoGens();
            context.RamiGens.Add(entity);
        }

        // Spostando il ramo in un'altra gens, le schede che lo usano seguono la nuova gens.
        if (entity.Id != 0 && entity.GensId != data.GensId)
        {
            foreach (var person in context.People.Where(p => p.RamoGensId == entity.Id))
            {
                person.GensId = data.GensId;
            }

            foreach (var person in context.People.Where(p => p.RamoGensAdozioneId == entity.Id))
            {
                person.GensAdozioneId = data.GensId;
            }
        }

        entity.GensId = data.GensId;
        entity.Cognomen = DynastyNames.Capitalize(data.Cognomen);
        entity.Plurale = plurale;
        entity.NomeItaliano = DynastyNames.Capitalize(data.NomeItaliano);
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim();
        entity.Note = data.Note ?? string.Empty;
        SaveOrRevert();
        return entity.Id;
    }

    // Eliminando una gens si eliminano i suoi rami; le schede restano senza gens.
    public void DeleteGens(int id)
    {
        if (context.Gentes.Find(id) is not { } entity)
        {
            return;
        }

        var ramoIds = context.RamiGens.Where(r => r.GensId == id).Select(r => r.Id).ToList();
        foreach (var person in context.People.Where(p => p.GensId == id || p.GensAdozioneId == id
            || (p.RamoGensId != null && ramoIds.Contains(p.RamoGensId.Value)) || (p.RamoGensAdozioneId != null && ramoIds.Contains(p.RamoGensAdozioneId.Value))))
        {
            if (person.GensId == id || (person.RamoGensId is int r && ramoIds.Contains(r)))
            {
                (person.GensId, person.RamoGensId) = (null, null);
            }

            if (person.GensAdozioneId == id || (person.RamoGensAdozioneId is int a && ramoIds.Contains(a)))
            {
                (person.GensAdozioneId, person.RamoGensAdozioneId) = (null, null);
            }
        }

        context.RamiGens.RemoveRange(context.RamiGens.Where(r => r.GensId == id));
        context.Gentes.Remove(entity);
        SaveOrRevert();
    }

    // Eliminando un ramo le schede restano nella sua gens.
    public void DeleteRamo(int id)
    {
        if (context.RamiGens.Find(id) is not { } entity)
        {
            return;
        }

        foreach (var person in context.People.Where(p => p.RamoGensId == id))
        {
            person.RamoGensId = null;
        }

        foreach (var person in context.People.Where(p => p.RamoGensAdozioneId == id))
        {
            person.RamoGensAdozioneId = null;
        }

        context.RamiGens.Remove(entity);
        SaveOrRevert();
    }

    // Gens ed eventuale ramo già nell'archivio per una gens importata: prima per codice Wikidata, poi per nome (la gens) o
    // per gens e plurale (il ramo). Senza scrivere.
    public (GensRomana? Gens, RamoGens? Ramo) FindImportedGens(ImportedGens imported)
    {
        var gentes = GetGentes();
        var gens = (imported.WikidataId.Length > 0 ? gentes.FirstOrDefault(g => g.WikidataId == imported.WikidataId) : null)
            ?? gentes.FirstOrDefault(g => GensComparer.Equals(g.Nome.Trim(), imported.Nome.Trim()));
        if (gens is null || imported.Ramo is not { } branch)
        {
            return (gens, null);
        }

        var rami = gentes.SelectMany(g => g.Rami).ToList();
        var ramo = (branch.WikidataId.Length > 0 ? rami.FirstOrDefault(r => r.WikidataId == branch.WikidataId) : null)
            ?? gens.Rami.FirstOrDefault(r => branch.Plurale.Trim().Length > 0 && GensComparer.Equals(r.Plurale.Trim(), branch.Plurale.Trim()));
        return (gens, ramo);
    }

    // Crea (o completa) la gens e il ramo importati; di quelli esistenti riempie solo i campi vuoti, per non perdere le
    // correzioni fatte a mano.
    public (int GensId, int? RamoId) EnsureImportedGens(ImportedGens imported)
    {
        var (found, foundRamo) = FindImportedGens(imported);
        var gens = found is null ? null : context.Gentes.Find(found.Id);
        if (gens is null)
        {
            gens = new GensRomana { Nome = DynastyNames.Capitalize(imported.Nome) };
            context.Gentes.Add(gens);
        }

        static void Fill(Func<string> get, Action<string> set, string value)
        {
            if (get().Trim().Length == 0 && value.Trim().Length > 0)
            {
                set(DynastyNames.Capitalize(value));
            }
        }

        Fill(() => gens.Nomen, v => gens.Nomen = v, imported.Nomen);
        Fill(() => gens.Plurale, v => gens.Plurale = v, imported.Plurale);
        Fill(() => gens.NomeItaliano, v => gens.NomeItaliano = v, imported.NomeItaliano);
        Fill(() => gens.PluraleItaliano, v => gens.PluraleItaliano = v, imported.PluraleItaliano);
        if (gens.WikidataId.Trim().Length == 0)
        {
            gens.WikidataId = imported.WikidataId.Trim();
        }

        SaveOrRevert();
        if (imported.Ramo is not { } branch || (branch.Plurale.Trim().Length == 0 && branch.Cognomen.Trim().Length == 0))
        {
            return (gens.Id, null);
        }

        var ramo = foundRamo is null ? null : context.RamiGens.Find(foundRamo.Id);
        if (ramo is null)
        {
            ramo = new RamoGens { GensId = gens.Id, Plurale = DynastyNames.Capitalize(branch.Plurale) };
            context.RamiGens.Add(ramo);
        }

        Fill(() => ramo.Plurale, v => ramo.Plurale = v, branch.Plurale);
        Fill(() => ramo.Cognomen, v => ramo.Cognomen = v, branch.Cognomen);
        Fill(() => ramo.NomeItaliano, v => ramo.NomeItaliano = v, branch.NomeItaliano);
        if (ramo.WikidataId.Trim().Length == 0)
        {
            ramo.WikidataId = branch.WikidataId.Trim();
        }

        SaveOrRevert();
        return (gens.Id, ramo.Id);
    }

    // Pulizia delle dinastie (2026-11-16), a ogni caricamento e senza effetti se non c'è niente da fare:
    //  - le gens salvate come dinastie prima della separazione (tipo «gens») escono dalle Dinastie con tutti i loro rami;
    //    ciascuna diventa una gens (e i rami rami) se non c'è già tra le gentes, ritrovata per Wikidata o per nome.
    //    Un ramo a cui è collegata una scheda come dinastia resta tra le dinastie, come dinastia principale;
    //  - le dinastie scritte prima della regola della maiuscola («dinastia Giulio-Claudia») la prendono, anche nel campo
    //    Famiglia delle schede collegate.
    private void CleanUpDynasties()
    {
        var all = context.Dinastie.ToList();
        var byId = all.ToDictionary(d => d.Id);
        var gensRowIds = all.Where(d => d.Tipo == TipiDinastia.Gens).Select(d => d.Id).ToHashSet();
        var changed = false;
        var files = new List<string>();
        if (gensRowIds.Count > 0)
        {
            // La gens più esterna sopra una dinastia (o la dinastia stessa), risalendo i superiori.
            Dinastia? OuterGens(Dinastia row)
            {
                Dinastia? found = null;
                var visited = new HashSet<int>();
                for (var current = row; visited.Add(current.Id);)
                {
                    if (gensRowIds.Contains(current.Id))
                    {
                        found = current;
                    }

                    if (current.ParentId is not int p || !byId.TryGetValue(p, out var parent))
                    {
                        break;
                    }

                    current = parent;
                }

                return found;
            }

            var linked = context.PersoneDinastie.Select(l => l.DinastiaId).Distinct().ToHashSet();
            var removed = new List<Dinastia>();
            var gensOf = new Dictionary<int, GensRomana?>();
            GensRomana? GensFor(Dinastia outer)
            {
                if (!gensOf.TryGetValue(outer.Id, out var gens))
                {
                    gens = EnsureLegacyGens(outer);
                    gensOf[outer.Id] = gens;
                }

                return gens;
            }

            // Prima le gens, poi i rami (che ne completano il plurale).
            foreach (var row in all.OrderBy(d => gensRowIds.Contains(d.Id) && d.ParentId is null ? 0 : 1))
            {
                if (OuterGens(row) is not { } outer)
                {
                    continue;
                }

                if (!gensRowIds.Contains(row.Id) && linked.Contains(row.Id))
                {
                    continue;
                }

                removed.Add(row);
                if (GensFor(outer) is { } gens && row != outer)
                {
                    EnsureLegacyRamo(gens, row);
                }
            }

            var removedIds = removed.Select(d => d.Id).ToHashSet();
            foreach (var keep in all.Where(d => !removedIds.Contains(d.Id) && d.ParentId is int p && removedIds.Contains(p)))
            {
                keep.ParentId = null;
            }

            // Le schede perdono i nomi delle gens nel campo Famiglia: ora stanno nei campi della gens.
            var links = context.PersoneDinastie.Where(l => removedIds.Contains(l.DinastiaId)).ToList();
            var names = removed.Select(d => d.Nome.Trim()).ToHashSet(PeopleGrouping.FamilyComparer);
            var personIds = links.Select(l => l.PersonaId).ToHashSet();
            foreach (var person in context.People.Where(p => personIds.Contains(p.Id)))
            {
                person.Family = PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(person.Family).Where(n => !names.Contains(n)));
            }

            context.PersoneDinastie.RemoveRange(links);
            files.AddRange(RemoveAttachmentRows(AmbitiAllegato.Dinastia, removedIds));
            context.Dinastie.RemoveRange(removed);
            all = [.. all.Where(d => !removedIds.Contains(d.Id))];
            changed = true;
        }

        foreach (var dynasty in all)
        {
            var nome = DynastyNames.Capitalize(dynasty.Nome);
            if (!string.Equals(nome, dynasty.Nome, StringComparison.Ordinal))
            {
                var oldName = dynasty.Nome.Trim();
                var personIds = context.PersoneDinastie.Where(l => l.DinastiaId == dynasty.Id).Select(l => l.PersonaId).ToList();
                foreach (var person in context.People.Where(p => personIds.Contains(p.Id)))
                {
                    person.Family = RenameFamily(person.Family, oldName, nome);
                }

                dynasty.Nome = nome;
                changed = true;
            }

            var latino = DynastyNames.Capitalize(dynasty.NomeLatino);
            if (!string.Equals(latino, dynasty.NomeLatino, StringComparison.Ordinal))
            {
                dynasty.NomeLatino = latino;
                changed = true;
            }
        }

        if (changed)
        {
            SaveOrRevert();
            DeleteAttachmentFiles(files);
        }
        else
        {
            context.ChangeTracker.Clear();
        }
    }

    private static string WithoutGensWord(string text)
        => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"^gens\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    // Gens corrispondente a una vecchia dinastia di tipo gens: ritrovata per Wikidata o per nome latino, altrimenti creata.
    private GensRomana? EnsureLegacyGens(Dinastia row)
    {
        var latin = WithoutGensWord(row.NomeLatino.Trim().Length > 0 ? row.NomeLatino : row.Nome);
        var italian = WithoutGensWord(row.Nome);
        if (latin.Length == 0)
        {
            return null;
        }

        var wikidata = row.WikidataId.Trim();
        var gentes = context.Gentes.ToList();
        var gens = (wikidata.Length > 0 ? gentes.FirstOrDefault(g => g.WikidataId == wikidata) : null)
            ?? gentes.FirstOrDefault(g => GensComparer.Equals(g.Nome.Trim(), latin));
        if (gens is null)
        {
            gens = new GensRomana
            {
                Nome = DynastyNames.Capitalize(latin),
                NomeItaliano = GensComparer.Equals(italian, latin) ? string.Empty : DynastyNames.Capitalize(italian),
                WikidataId = wikidata,
                Note = row.Note,
            };
            context.Gentes.Add(gens);
        }

        return gens;
    }

    // Ramo corrispondente a una vecchia dinastia ramo di una gens: «Iulii Caesares» dà il plurale «Caesares» (e quello della
    // gens, se manca); ritrovato per Wikidata o per plurale nella stessa gens, altrimenti creato.
    private void EnsureLegacyRamo(GensRomana gens, Dinastia row)
    {
        var latin = (row.NomeLatino.Trim().Length > 0 ? row.NomeLatino : row.Nome).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var italian = row.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (latin.Length == 0)
        {
            return;
        }

        if (latin.Length >= 2 && gens.Plurale.Trim().Length == 0)
        {
            gens.Plurale = DynastyNames.Capitalize(latin[0]);
        }

        var plural = string.Join(' ', latin.Length >= 2 ? latin[1..] : latin);
        var italianPlural = string.Join(' ', italian.Length >= 2 ? italian[1..] : italian);
        var wikidata = row.WikidataId.Trim();
        // Anche i rami appena aggiunti e non ancora salvati (Local), per non crearne due uguali.
        var rami = context.RamiGens.ToList().Concat(context.RamiGens.Local).Distinct().ToList();
        var exists = (wikidata.Length > 0 && rami.Any(r => r.WikidataId == wikidata))
            || rami.Any(r => (ReferenceEquals(r.Gens, gens) || (gens.Id != 0 && r.GensId == gens.Id)) && GensComparer.Equals(r.Plurale.Trim(), plural));
        if (exists)
        {
            return;
        }

        context.RamiGens.Add(new RamoGens
        {
            Gens = gens,
            Plurale = DynastyNames.Capitalize(plural),
            NomeItaliano = GensComparer.Equals(italianPlural, plural) ? string.Empty : DynastyNames.Capitalize(italianPlural),
            WikidataId = wikidata,
            Note = row.Note,
        });
    }

    // Gens salvate come dinastie nei backup precedenti al 2026-11-15: diventano gentes e rami (il ramo è la dinastia che ha
    // una gens come superiore), e il testo della gens nelle schede diventa il collegamento. Le forme mancanti restano da
    // completare. Un ramo «Iulii Caesares» dà il plurale della gens («Iulii») e quello del ramo («Caesares»), come sono
    // scritti.
    private static void ConvertBackupGens(ArchiveBackup backup)
    {
        var rows = backup.Dynasties.Where(d => d.Tipo == TipiDinastia.Gens).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        static string WithoutGens(string text)
            => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"^gens\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var byId = backup.Dynasties.ToDictionary(d => d.Id);
        var rowIds = rows.Select(r => r.Id).ToHashSet();
        var nextGens = backup.Gentes.Select(g => g.Id).DefaultIfEmpty(0).Max() + 1;
        var nextRamo = backup.RamiGens.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
        var gensOf = new Dictionary<int, GensRomana>();
        var ramoOf = new Dictionary<int, RamoGens>();
        foreach (var row in rows.Where(r => r.ParentId is not int p || !rowIds.Contains(p)))
        {
            var latin = WithoutGens(row.NomeLatino.Trim().Length > 0 ? row.NomeLatino : row.Nome);
            var italian = WithoutGens(row.Nome);
            var gens = new GensRomana
            {
                Id = nextGens++,
                Nome = DynastyNames.Capitalize(latin),
                NomeItaliano = string.Equals(italian, latin, StringComparison.OrdinalIgnoreCase) ? string.Empty : DynastyNames.Capitalize(italian),
                WikidataId = row.WikidataId,
                Note = row.Note,
            };
            backup.Gentes.Add(gens);
            gensOf[row.Id] = gens;
        }

        GensRomana? RootGens(Dinastia row)
        {
            var visited = new HashSet<int>();
            for (var current = row; visited.Add(current.Id);)
            {
                if (gensOf.TryGetValue(current.Id, out var gens))
                {
                    return gens;
                }

                if (current.ParentId is not int p || !byId.TryGetValue(p, out var parent))
                {
                    return null;
                }

                current = parent;
            }

            return null;
        }

        foreach (var row in rows.Where(r => !gensOf.ContainsKey(r.Id)))
        {
            if (RootGens(row) is not { } gens)
            {
                continue;
            }

            var latin = (row.NomeLatino.Trim().Length > 0 ? row.NomeLatino : row.Nome).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var italian = row.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (latin.Length >= 2 && gens.Plurale.Length == 0)
            {
                gens.Plurale = latin[0];
            }

            if (italian.Length >= 2 && gens.PluraleItaliano.Length == 0 && !string.Equals(italian[0], latin[0], StringComparison.OrdinalIgnoreCase))
            {
                gens.PluraleItaliano = italian[0];
            }

            var plural = string.Join(' ', latin.Length >= 2 ? latin[1..] : latin);
            var italianPlural = string.Join(' ', italian.Length >= 2 ? italian[1..] : italian);
            var ramo = new RamoGens
            {
                Id = nextRamo++,
                GensId = gens.Id,
                Plurale = plural,
                NomeItaliano = string.Equals(italianPlural, plural, StringComparison.OrdinalIgnoreCase) ? string.Empty : italianPlural,
                WikidataId = row.WikidataId,
                Note = row.Note,
            };
            backup.RamiGens.Add(ramo);
            ramoOf[row.Id] = ramo;
        }

        (int? Gens, int? Ramo) Link(string? text)
        {
            var found = PeopleGrouping.FamilyNames(text).Select(n => rows.FirstOrDefault(r => GensComparer.Equals(r.Nome.Trim(), n))).OfType<Dinastia>().ToList();
            if (found.Select(r => ramoOf.GetValueOrDefault(r.Id)).OfType<RamoGens>().FirstOrDefault() is { } ramo)
            {
                return (ramo.GensId, ramo.Id);
            }

            return (found.Select(r => gensOf.GetValueOrDefault(r.Id)).OfType<GensRomana>().FirstOrDefault()?.Id, null);
        }

        foreach (var person in backup.People)
        {
            if (person.GensId is null && person.GensTestoBackup is { Length: > 0 } birth)
            {
                (person.GensId, person.RamoGensId) = Link(birth);
            }

            if (person.GensAdozioneId is null && person.GensAdozioneTestoBackup is { Length: > 0 } adoptive)
            {
                (person.GensAdozioneId, person.RamoGensAdozioneId) = Link(adoptive);
            }
        }

        // Le gens non sono più dinastie: una dinastia che ne era ramo diventa principale.
        backup.Dynasties.RemoveAll(d => rowIds.Contains(d.Id));
        foreach (var dynasty in backup.Dynasties.Where(d => d.ParentId is int p && rowIds.Contains(p)))
        {
            dynasty.ParentId = null;
        }
    }
}
