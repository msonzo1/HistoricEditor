namespace HistoricEditor;

using System.Text.RegularExpressions;

// Divide un nome importato (es. "Carlo V d'Asburgo", "Filippo il Bello") in nome, suffisso e soprannome.
internal static partial class PersonNameParser
{
    [GeneratedRegex(@"^(M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3}))$")]
    private static partial Regex RomanNumeral();

    public static (string Name, string Suffix, string Nickname) Split(string label, IEnumerable<string>? nicknames = null)
    {
        var text = (label ?? string.Empty).Trim();
        var nickname = string.Empty;

        foreach (var candidate in (nicknames ?? []).Select(n => n?.Trim() ?? string.Empty).Where(n => n.Length > 0))
        {
            if (text.Length > candidate.Length && text.EndsWith(" " + candidate, StringComparison.CurrentCultureIgnoreCase))
            {
                nickname = text[^candidate.Length..];
                text = text[..^candidate.Length].TrimEnd(' ', ',');
                break;
            }
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Length > 0 && RomanNumeral().IsMatch(tokens[i]))
            {
                return (string.Join(' ', tokens[..(i + 1)]), string.Join(' ', tokens[(i + 1)..]), nickname);
            }
        }

        return (text, string.Empty, nickname);
    }

    private static readonly HashSet<string> SurnameParticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "da", "de", "de'", "dei", "degli", "del", "della", "dello", "dalla", "di", "d'",
        "van", "von", "der", "den", "ten", "ter", "du", "des", "le", "la", "dos", "das", "y",
    };

    // Separa il cognome (P734) in coda al nome, es. "Benito Mussolini" → ("Benito", "Mussolini");
    // i cognomi composti e le particelle ("Ludwig van Beethoven") restano nel cognome.
    public static (string Name, string Surname) SplitSurname(string name, IEnumerable<string>? surnames)
    {
        var text = (name ?? string.Empty).Trim();
        var surnameTokens = new HashSet<string>(
            (surnames ?? []).SelectMany(s => (s ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.CurrentCultureIgnoreCase);
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (surnameTokens.Count == 0 || tokens.Length < 2)
        {
            return (text, string.Empty);
        }

        var start = tokens.Length;
        while (start > 1 && surnameTokens.Contains(tokens[start - 1]))
        {
            start--;
        }

        if (start == tokens.Length || tokens[start..].All(SurnameParticles.Contains))
        {
            return (text, string.Empty);
        }

        while (start > 1 && SurnameParticles.Contains(tokens[start - 1]))
        {
            start--;
        }

        return (string.Join(' ', tokens[..start]), string.Join(' ', tokens[start..]));
    }
}
