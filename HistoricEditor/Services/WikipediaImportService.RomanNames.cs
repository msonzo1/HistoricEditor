using System.Text.RegularExpressions;

namespace HistoricEditor;

// Nomi nel tempo dei personaggi romani, dall'introduzione della voce italiana di Wikipedia, che li scrive a coppie
// italiano/latino: «Cesare Marco Aurelio Antonino Augusto (in latino Caesar Marcus Aurelius Antoninus Augustus; …)»,
// «Nato Marco Annio Catilio Severo (Marcus Annius Catilius Severus), divenne Marco Annio Vero (Marcus Annius Verus) …
// assunse quindi il nome di Marco Aurelio Cesare figlio dell'Augusto (Marcus Aurelius Caesar Augusti filius)».
// Wikidata non basta: fonde i nomi di nascita e non ha quello da imperatore.
public sealed partial class WikipediaImportService
{
    internal enum IntroNameKind { Lead, Birth, Change }

    internal sealed record IntroRomanName(string Italian, string Latin, IntroNameKind Kind);

    // Il nome all'inizio della voce, con il latino tra parentesi.
    [GeneratedRegex(@"^\s*(?<it>\p{Lu}[^()\n;]{1,120}?)\s*\(\s*(?:in\s+)?latino:?\s*(?<la>\p{Lu}[^();\n]{1,160}?)\s*[;)]")]
    private static partial Regex LeadRomanNamePattern();

    // «Nato/Nata X (Y)», «divenne X (Y)», «… il nome di X (Y)».
    [GeneratedRegex(@"(?:\b(?<birth>[Nn]at[oa])\s+(?:con\s+il\s+nome\s+di\s+|come\s+)?|\bdivenne\s+|\bil\s+nome\s+di\s+)(?<it>\p{Lu}[^()\n;]{1,120}?)\s*\(\s*(?:in\s+latino:?\s*)?(?<la>\p{Lu}[^();\n]{1,160}?)\s*[;)]")]
    private static partial Regex ChangedRomanNamePattern();

    // Filiazione nel latino («Augusti filius», «Divi filius») e nell'italiano («figlio dell'Augusto», «figlio del divo»).
    [GeneratedRegex(@"\b(?<fil>\p{Lu}\p{L}+\s+(?:filius|filia|f\.))(?=\s|$)")]
    private static partial Regex LatinFiliationPattern();

    [GeneratedRegex(@"\s*\bfigli[oa]\s+(?:di\s+|del\s+|dello\s+|della\s+|dell['’]\s*)?\p{L}+")]
    private static partial Regex ItalianFiliationPattern();

    internal static List<IntroRomanName> IntroductionRomanNames(string? introduction)
    {
        var result = new List<IntroRomanName>();
        if (string.IsNullOrWhiteSpace(introduction))
        {
            return result;
        }

        var text = introduction.Length > 2500 ? introduction[..2500] : introduction;
        var lead = LeadRomanNamePattern().Match(text);
        if (lead.Success && LooksLikeSameName(lead.Groups["it"].Value, lead.Groups["la"].Value))
        {
            result.Add(new IntroRomanName(lead.Groups["it"].Value.Trim(), lead.Groups["la"].Value.Trim(), IntroNameKind.Lead));
        }

        foreach (Match match in ChangedRomanNamePattern().Matches(text))
        {
            if (LooksLikeSameName(match.Groups["it"].Value, match.Groups["la"].Value))
            {
                result.Add(new IntroRomanName(match.Groups["it"].Value.Trim(), match.Groups["la"].Value.Trim(),
                    match.Groups["birth"].Success ? IntroNameKind.Birth : IntroNameKind.Change));
            }
        }

        return result;
    }

    // Il latino tra parentesi è lo stesso nome (non un luogo o una data): almeno due parole latine e la prima parola
    // somigliante a quella italiana (Marco/Marcus, Cesare/Caesar, Tito/Titus).
    private static bool LooksLikeSameName(string italian, string latin)
    {
        var latinWords = latin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var italianWords = italian.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return latinWords.Length >= 2 && italianWords.Length >= 1
            && latinWords.All(w => w.All(c => char.IsLetter(c) || c == '.'))
            && SimilarRomanPart(italianWords[0], latinWords[0]);
    }

    // Righe di un nome: ogni parola latina con il suo tipo (quelli noti da Wikidata, poi le regole) e la parola italiana
    // nella stessa posizione, se le parole sono tante quante; altrimenti l'italiano proposto dal latino.
    internal static RomanNameRecord BuildIntroRomanName(string italian, string latin, IReadOnlyDictionary<string, string> knownTypes)
    {
        var name = new RomanNameRecord();
        var filiation = LatinFiliationPattern().Match(latin);
        if (filiation.Success)
        {
            name.Filiazione = filiation.Groups["fil"].Value.Trim();
            latin = latin.Remove(filiation.Index, filiation.Length);
            italian = ItalianFiliationPattern().Replace(italian, string.Empty);
        }

        var latinWords = latin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var italianWords = italian.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasPraenomen = false;
        var hasCognomen = false;
        for (var i = 0; i < latinWords.Length; i++)
        {
            var word = latinWords[i];
            var previous = i > 0 ? latinWords[i - 1] : string.Empty;
            string tipo;
            if (word is "Caesar" && previous is not ("Iulius" or "Iulia"))
            {
                // Dopo Augusto «Caesar» è un titolo dinastico; resta cognomen solo dopo il nomen Iulius (Gaius Iulius Caesar).
                tipo = TipiParteNome.Titolo;
            }
            else if (knownTypes.TryGetValue(word, out var known))
            {
                tipo = known;
            }
            else if (word is "Imperator" or "Augustus" or "Augusta")
            {
                tipo = TipiParteNome.Titolo;
            }
            else if (!hasPraenomen && RomanNaming.Praenomina.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                tipo = TipiParteNome.Praenomen;
            }
            else if (!hasCognomen && (word.EndsWith("ius", StringComparison.Ordinal) || word.EndsWith("ia", StringComparison.Ordinal)))
            {
                tipo = TipiParteNome.Nomen;
            }
            else
            {
                tipo = TipiParteNome.Cognomen;
            }

            hasPraenomen |= tipo == TipiParteNome.Praenomen;
            hasCognomen |= tipo == TipiParteNome.Cognomen;
            var italianWord = italianWords.Length == latinWords.Length ? italianWords[i] : RomanNaming.ItalianForm(word);
            name.Parti.Add(new RomanNamePart { Tipo = tipo, Latino = word, Italiano = italianWord });
        }

        name.FieldsFromParts();
        return name;
    }

    // Nomi nel tempo dall'introduzione: il nome alla nascita, i cambi di nome nell'ordine del testo e, ultimo e principale,
    // il nome iniziale della voce (per gli imperatori quello da imperatore). Vuoto se l'introduzione non indica altri nomi
    // oltre a quello iniziale: allora il nome resta quello di Wikidata.
    internal static List<RomanNameRecord> RomanNamesOverTime(
        string? introduction, IReadOnlyDictionary<string, string> knownTypes, int? birthYear, int? principalFrom, bool ruler)
    {
        var found = IntroductionRomanNames(introduction);
        var lead = found.FirstOrDefault(n => n.Kind == IntroNameKind.Lead);
        var others = found.Where(n => n.Kind != IntroNameKind.Lead)
            .DistinctBy(n => n.Latin, StringComparer.OrdinalIgnoreCase)
            .Where(n => lead is null || !string.Equals(n.Latin, lead.Latin, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (lead is null || others.Count == 0)
        {
            return [];
        }

        static string Year(int? year) => year is not int y ? string.Empty : y < 0 ? $"{-y} a.C." : y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var names = new List<RomanNameRecord>();
        foreach (var other in others.OrderBy(n => n.Kind == IntroNameKind.Birth ? 0 : 1))
        {
            var name = BuildIntroRomanName(other.Italian, other.Latin, knownTypes);
            name.Motivo = other.Kind == IntroNameKind.Birth ? "Nome alla nascita" : "Cambio di nome";
            name.Dal = other.Kind == IntroNameKind.Birth ? Year(birthYear) : string.Empty;
            names.Add(name);
        }

        var principal = BuildIntroRomanName(lead.Italian, lead.Latin, knownTypes);
        principal.Principale = true;
        principal.Dal = Year(principalFrom);
        principal.Motivo = ruler ? "Assunzione del potere" : string.Empty;
        names.Add(principal);
        return names;
    }
}
