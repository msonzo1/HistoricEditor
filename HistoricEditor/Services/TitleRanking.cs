using System.Globalization;

namespace HistoricEditor;

// Sceglie il titolo principale: il rango più alto (imperatore, re, duca…), poi quello preferito su Wikidata, poi l'ordine.
public static class TitleRanking
{
    private static readonly (int Rank, string[] Heads)[] Ranks =
    [
        (0, ["imperatore", "imperatrice", "papa", "zar", "zarina"]),
        (1, ["re", "regina", "sovrano", "sovrana", "monarca", "sultano", "faraone"]),
        (2, ["arciduca", "arciduchessa", "granduca", "granduchessa", "principe elettore"]),
        (3, ["duca", "duchessa", "principe", "principessa", "doge"]),
        (4, ["marchese", "marchesa", "margravio", "langravio", "conte", "contessa"]),
        (5, ["signore", "signora", "visconte", "viscontessa", "barone", "baronessa"]),
    ];

    public static PersonTitle? ChooseMain(IReadOnlyList<PersonTitle> titles, IReadOnlySet<string> preferredIds)
        => titles
            .Select((title, index) => (title, index))
            .OrderBy(x => Rank(x.title.Titolo))
            .ThenBy(x => preferredIds.Contains(x.title.WikidataId) ? 0 : 1)
            .ThenBy(x => x.index)
            .Select(x => x.title)
            .FirstOrDefault();

    public static int Rank(string title)
    {
        var text = title.Trim().ToLower(CultureInfo.GetCultureInfo("it-IT"));
        foreach (var (rank, heads) in Ranks)
        {
            if (heads.Any(h => text == h || text.StartsWith(h + " ", StringComparison.Ordinal)))
            {
                return rank;
            }
        }

        return Ranks.Length;
    }

    public static string Capitalize(string title)
    {
        var text = title.Trim();
        return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("it-IT")) + text[1..];
    }
}
