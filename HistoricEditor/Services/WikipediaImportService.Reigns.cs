using System.Text.Json;

namespace HistoricEditor;

// Date dei titoli dalle infobox delle voci Wikipedia: ogni periodo di regno diventa un'ulteriore fonte del titolo
// e completa la data Wikidata quando questa è meno precisa (Carlo V imperatore: «1519» → «28/06/1519»).
public sealed partial class WikipediaImportService
{
    private static async Task AddInfoboxReignSourcesAsync(IReadOnlyList<PersonTitle> titles, IReadOnlyDictionary<string, LeadDate> leads, CancellationToken cancellationToken)
    {
        var editions = Languages.Select(l => l.Code)
            .Where(code => leads.TryGetValue(code, out var lead) && lead.Reigns.Count > 0)
            .ToList();
        if (editions.Count == 0 || !titles.Any(t => t.WikidataId.Length > 0))
        {
            return;
        }

        // Il titolo dell'infobox si riconosce dalla pagina collegata: la sua voce Wikidata è il titolo stesso
        // («Holy Roman Emperor») oppure un elenco che rimanda al titolo con P360 («Imperatori del Sacro Romano Impero»).
        var pageIds = new Dictionary<(string Code, string Link), string>();
        foreach (var code in editions)
        {
            foreach (var (link, id) in await ResolveWikipediaLinksAsync(code, leads[code].Reigns.SelectMany(r => r.Links), cancellationToken))
            {
                pageIds[(code, link)] = id;
            }
        }

        var listSubjects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            foreach (var (id, entity) in await ReadEntitiesAsync(pageIds.Values, cancellationToken))
            {
                listSubjects[id] = ReadEntityIds(entity.GetPropertyOrDefault("claims"), "P360").ToList();
            }
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
        {
            // Senza gli elenchi restano validi i collegamenti diretti al titolo.
        }

        HashSet<string> TitleIds(string code, InfoboxReign reign)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in reign.Links)
            {
                if (pageIds.TryGetValue((code, link), out var id))
                {
                    ids.Add(id);
                    ids.UnionWith(listSubjects.GetValueOrDefault(id, []));
                }
            }

            return ids;
        }

        foreach (var title in titles.Where(t => t.WikidataId.Length > 0))
        {
            var added = new List<TitleDateSource>();
            foreach (var code in editions)
            {
                // Lo stesso titolo può comparire più volte (mandati distinti): vale il primo periodo che si sovrappone.
                var reign = leads[code].Reigns.FirstOrDefault(r => TitleIds(code, r).Contains(title.WikidataId) && Overlaps(title, r));
                if (reign is not null)
                {
                    added.Add(new TitleDateSource { Fonte = $"Wikipedia {code} · infobox", Inizio = reign.Start, Fine = reign.End });
                }
            }

            if (added.Count == 0)
            {
                continue;
            }

            title.FontiDate.AddRange(added);
            title.Inizio = TitleDateConsensus.Refine(title.Inizio, title.FontiDate.Select(s => s.Inizio), InfoboxReignParser.Format);
            title.Fine = TitleDateConsensus.Refine(title.Fine, title.FontiDate.Select(s => s.Fine), InfoboxReignParser.Format);
        }
    }

    // Periodi in anni con gli estremi mancanti aperti, come nell'unione dei titoli.
    private static bool Overlaps(PersonTitle title, InfoboxReign reign)
    {
        static (int Start, int End) Period(string start, string end)
        {
            var a = TitleDateConsensus.Parse(start)?.Year ?? int.MinValue;
            var b = TitleDateConsensus.Parse(end)?.Year ?? int.MaxValue;
            return a <= b ? (a, b) : (b, a);
        }

        var (titleStart, titleEnd) = Period(title.Inizio, title.Fine);
        var (reignStart, reignEnd) = Period(reign.Start, reign.End);
        return titleStart <= reignEnd && reignStart <= titleEnd;
    }

    // Voce Wikidata delle pagine collegate, seguendo normalizzazioni e redirect («Sovrani di Spagna» → Q173053).
    private static async Task<Dictionary<string, string>> ResolveWikipediaLinksAsync(string code, IEnumerable<string> links, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in links.Distinct(StringComparer.Ordinal).Chunk(50))
        {
            try
            {
                var url = $"https://{code}.wikipedia.org/w/api.php?action=query&prop=pageprops&ppprop=wikibase_item&redirects=1&format=json&formatversion=2&titles={Uri.EscapeDataString(string.Join('|', batch))}";
                using var document = await GetJsonAsync(url, cancellationToken);
                var query = document.RootElement.GetProperty("query");
                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kind in new[] { "normalized", "redirects" })
                {
                    if (query.TryGetProperty(kind, out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            renamed[GetString(item, "from")] = GetString(item, "to");
                        }
                    }
                }

                var pageItems = new Dictionary<string, string>(StringComparer.Ordinal);
                if (query.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var page in pages.EnumerateArray())
                    {
                        if (GetString(page.GetPropertyOrDefault("pageprops"), "wikibase_item") is { Length: > 0 } id && IsEntityId(id))
                        {
                            pageItems[GetString(page, "title")] = id;
                        }
                    }
                }

                foreach (var link in batch)
                {
                    var target = link;
                    // Prima la normalizzazione, poi l'eventuale redirect.
                    for (var i = 0; i < 2 && renamed.TryGetValue(target, out var next); i++)
                    {
                        target = next;
                    }

                    if (pageItems.TryGetValue(target, out var id))
                    {
                        result[link] = id;
                    }
                }
            }
            catch (Exception ex) when (IsOptionalFetchFailure(ex, cancellationToken))
            {
                // Edizione non raggiungibile: i suoi regni restano senza abbinamento.
            }
        }

        return result;
    }
}
