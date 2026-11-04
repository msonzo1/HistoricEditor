using System.Text.RegularExpressions;

namespace HistoricEditor;

// Ricostruisce in italiano i titoli che su Wikidata hanno solo l'etichetta inglese (es. «Monarch of England»).
public static partial class TitleTranslator
{
    private static readonly Dictionary<string, (string Male, string Female)> Heads = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Monarch"] = ("Re", "Regina"),
        ["King"] = ("Re", "Re"),
        ["Queen"] = ("Regina", "Regina"),
        ["Emperor"] = ("Imperatore", "Imperatore"),
        ["Empress"] = ("Imperatrice", "Imperatrice"),
        ["Lord"] = ("Signore", "Signora"),
        ["Lady"] = ("Signora", "Signora"),
        ["Duke"] = ("Duca", "Duca"),
        ["Duchess"] = ("Duchessa", "Duchessa"),
        ["Grand Duke"] = ("Granduca", "Granduca"),
        ["Prince"] = ("Principe", "Principe"),
        ["Princess"] = ("Principessa", "Principessa"),
        ["Count"] = ("Conte", "Conte"),
        ["Earl"] = ("Conte", "Conte"),
        ["Countess"] = ("Contessa", "Contessa"),
        ["Marquess"] = ("Marchese", "Marchese"),
        ["Marquis"] = ("Marchese", "Marchese"),
        ["Elector"] = ("Principe elettore", "Principe elettore"),
        ["Archduke"] = ("Arciduca", "Arciduca"),
        ["Archduchess"] = ("Arciduchessa", "Arciduchessa"),
        ["Margrave"] = ("Margravio", "Margravio"),
        ["Landgrave"] = ("Langravio", "Langravio"),
        ["Viscount"] = ("Visconte", "Visconte"),
        ["Viscountess"] = ("Viscontessa", "Viscontessa"),
        ["Baron"] = ("Barone", "Barone"),
        ["Baroness"] = ("Baronessa", "Baronessa"),
        ["Tsar"] = ("Zar", "Zarina"),
    };

    public static string? Translate(string englishLabel, string italianTerritory, string sesso)
    {
        var match = EnglishTitle().Match(englishLabel.Trim());
        if (!match.Success || italianTerritory.Trim().Length == 0 || !Heads.TryGetValue(match.Groups[1].Value, out var head))
        {
            return null;
        }

        var name = sesso == Sessi.Femmina ? head.Female : head.Male;
        if (sesso.Length == 0 && match.Groups[1].Value.Equals("Monarch", StringComparison.OrdinalIgnoreCase))
        {
            name = "Monarca";
        }

        return $"{name} {Territory(italianTerritory.Trim())}";
    }

    // «Regno d'Inghilterra» → «d'Inghilterra»; «Francia» → «di Francia».
    private static string Territory(string label)
    {
        // Territorio senza etichetta italiana: «Kingdom of Granada» → «Granada».
        var english = EnglishPolity().Match(label);
        if (english.Success)
        {
            label = english.Groups[1].Value.Trim();
        }

        var polity = Polity().Match(label);
        if (polity.Success)
        {
            return polity.Groups[1].Value;
        }

        return "AEIOUaeiou".Contains(label[0]) ? $"d'{label}" : $"di {label}";
    }

    [GeneratedRegex(@"^(Monarch|King|Queen|Emperor|Empress|Lord|Lady|Grand Duke|Archduchess|Archduke|Duke|Duchess|Prince|Princess|Countess|Count|Earl|Marquess|Marquis|Elector|Margrave|Landgrave|Viscountess|Viscount|Baroness|Baron|Tsar) of .+$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishTitle();

    [GeneratedRegex(@"^(?:Regno|Signoria|Ducato|Granducato|Arciducato|Principato|Contea|Impero|Marchesato|Margraviato|Langraviato|Elettorato|Viscontea|Baronia|Zarato)\s+((?:di|del|della|dello|dei|degli|delle)\s+.+|d'.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Polity();

    [GeneratedRegex(@"^(?:Kingdom|Lordship|Duchy|Grand Duchy|Archduchy|Principality|County|Empire|Margraviate|Landgraviate|Electorate|Tsardom)\s+of\s+(?:the\s+)?(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishPolity();
}
