using System.Globalization;
using System.Text;

namespace HistoricEditor;

// Unisce i titoli duplicati importati da Wikidata (stesso elemento in P39 e P97, stessa etichetta,
// oppure un titolo generico che è superclasse di uno più specifico dello stesso periodo).
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

            var specific = result.FirstOrDefault(r => !ReferenceEquals(r, generic)
                && superclasses.TryGetValue(r.WikidataId, out var parents)
                && parents.Contains(generic.WikidataId)
                && Compatible(r, generic));
            if (specific is not null)
            {
                Fill(specific, generic);
                result.Remove(generic);
            }
        }

        return result;
    }

    private static bool SameTitle(PersonTitle a, PersonTitle b)
        => (a.WikidataId.Length > 0 && a.WikidataId == b.WikidataId)
            || Normalize(a.Titolo) == Normalize(b.Titolo);

    private static bool Compatible(PersonTitle a, PersonTitle b)
        => Agrees(a.Inizio, b.Inizio) && Agrees(a.Fine, b.Fine) && Agrees(a.Numerazione, b.Numerazione);

    private static bool Agrees(string a, string b)
        => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Fill(PersonTitle target, PersonTitle source)
    {
        if (string.IsNullOrWhiteSpace(target.Numerazione)) target.Numerazione = source.Numerazione;
        if (string.IsNullOrWhiteSpace(target.Inizio)) target.Inizio = source.Inizio;
        if (string.IsNullOrWhiteSpace(target.CausaInizio)) target.CausaInizio = source.CausaInizio;
        if (string.IsNullOrWhiteSpace(target.Fine)) target.Fine = source.Fine;
        if (string.IsNullOrWhiteSpace(target.CausaFine)) target.CausaFine = source.CausaFine;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
