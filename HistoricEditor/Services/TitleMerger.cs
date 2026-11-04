using System.Globalization;
using System.Text;

namespace HistoricEditor;

// Unisce i titoli duplicati importati da Wikidata (stesso elemento in P39 e P97, stessa etichetta,
// oppure un titolo generico che è superclasse di uno più specifico dello stesso periodo).
// Le date possono differire: restano separati solo i mandati distinti (periodi che non si sovrappongono).
public static class TitleMerger
{
    public static List<PersonTitle> Merge(IEnumerable<PersonTitle> titles, IReadOnlyDictionary<string, HashSet<string>> superclasses)
    {
        var result = new List<PersonTitle>();
        foreach (var title in titles)
        {
            var match = result.FirstOrDefault(r => SameTitle(r, title) && Compatible(r, title));
            if (match is null)
            {
                result.Add(title.Clone());
            }
            else
            {
                Fill(match, title);
            }
        }

        foreach (var generic in result.ToList())
        {
            if (generic.WikidataId.Length == 0)
            {
                continue;
            }

            var specifics = result.Where(r => !ReferenceEquals(r, generic)
                && superclasses.TryGetValue(r.WikidataId, out var parents)
                && parents.Contains(generic.WikidataId)
                && Compatible(r, generic)).ToList();
            // Un titolo generico (es. «Monarca di Castiglia e León») completa tutti i titoli specifici che lo includono.
            foreach (var specific in specifics)
            {
                Fill(specific, generic);
            }

            if (specifics.Count > 0)
            {
                result.Remove(generic);
            }
        }

        return result;
    }

    private static bool SameTitle(PersonTitle a, PersonTitle b)
        => (a.WikidataId.Length > 0 && a.WikidataId == b.WikidataId)
            || Normalize(a.Titolo) == Normalize(b.Titolo);

    private static bool Compatible(PersonTitle a, PersonTitle b)
        => Agrees(a.Numerazione, b.Numerazione) && Overlaps(a, b);

    private static bool Overlaps(PersonTitle a, PersonTitle b)
    {
        var (aStart, aEnd) = Period(a);
        var (bStart, bEnd) = Period(b);
        return aStart <= bEnd && bStart <= aEnd;
    }

    // Periodo in anni; un estremo mancante resta aperto. Per un titolo senza date il periodo è illimitato.
    private static (int Start, int End) Period(PersonTitle title)
    {
        var start = TitleDateConsensus.Parse(title.Inizio)?.Year;
        var end = TitleDateConsensus.Parse(title.Fine)?.Year;
        if (start is not null && end is not null && end < start)
        {
            (start, end) = (end, start);
        }

        return (start ?? int.MinValue, end ?? int.MaxValue);
    }

    private static bool Agrees(string a, string b)
        => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Fill(PersonTitle target, PersonTitle source)
    {
        if (string.IsNullOrWhiteSpace(target.Numerazione)) target.Numerazione = source.Numerazione;
        target.Inizio = MorePrecise(target.Inizio, source.Inizio);
        if (string.IsNullOrWhiteSpace(target.CausaInizio)) target.CausaInizio = source.CausaInizio;
        target.Fine = MorePrecise(target.Fine, source.Fine);
        if (string.IsNullOrWhiteSpace(target.CausaFine)) target.CausaFine = source.CausaFine;
        if (target.WikidataId.Length == 0) target.WikidataId = source.WikidataId;
        if (string.IsNullOrWhiteSpace(target.Predecessore))
        {
            target.Predecessore = source.Predecessore;
            target.PredecessoreWikidataId = source.PredecessoreWikidataId;
        }

        if (string.IsNullOrWhiteSpace(target.Successore))
        {
            target.Successore = source.Successore;
            target.SuccessoreWikidataId = source.SuccessoreWikidataId;
        }
        target.FontiDate.AddRange(source.FontiDate.Select(s => s.Clone()));
    }

    // Tiene la data già presente, salvo che l'altra sia dello stesso anno e più precisa (800 → 25/12/800).
    private static string MorePrecise(string current, string candidate)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return candidate;
        }

        var a = TitleDateConsensus.Parse(current);
        var b = TitleDateConsensus.Parse(candidate);
        return a is { } x && b is { } y && y.Precision > x.Precision && x.AgreesWith(y) ? candidate : current;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Trim().Replace('’', '\'').ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && !(char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] == ' '))
            {
                builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
            }
        }

        return builder.ToString();
    }
}
