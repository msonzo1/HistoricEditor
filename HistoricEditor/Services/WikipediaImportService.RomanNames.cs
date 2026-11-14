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

    // Filiazione nel latino («Augusti filius», «Divi Nervae filius») e nell'italiano («figlio dell'Augusto», «figlio del divo»).
    [GeneratedRegex(@"\b(?<fil>(?:Divi\s+)?\p{Lu}\p{L}+\s+(?:filius|filia|f\.))(?=\s|$)")]
    private static partial Regex LatinFiliationPattern();

    // Titoli trionfali presi per le vittorie (agnomina): Germanicus, Dacicus, Parthicus Maximus…
    private static readonly HashSet<string> TriumphalAgnomina = new(StringComparer.Ordinal)
    {
        "Germanicus", "Dacicus", "Parthicus", "Armeniacus", "Medicus", "Sarmaticus", "Britannicus", "Arabicus", "Adiabenicus",
    };

    // Campo «nome completo» dell'infobox italiana: «| nome completo = ''Titus Flavius Vespasianus'' (alla nascita)<br />…».
    [GeneratedRegex(@"^\s*\|\s*nome\s+completo\s*=(?<value>[^\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex InfoboxFullNamePattern();

    // Ogni nome in corsivo e il testo che lo segue fino al nome successivo (il momento: «(alla nascita)», «dopo l'adozione;»).
    [GeneratedRegex(@"''(?<la>[^']+?)''(?<rest>.*?)(?=''|$)", RegexOptions.Singleline)]
    private static partial Regex InfoboxNamePattern();

    internal static string InfoboxFullNameValue(string? wikitext)
        => string.IsNullOrWhiteSpace(wikitext) ? string.Empty
            : InfoboxFullNamePattern().Match(Regex.Replace(wikitext, @"<!--.*?-->", string.Empty, RegexOptions.Singleline)) is { Success: true } match
                ? match.Groups["value"].Value.Trim()
                : string.Empty;

    internal sealed record InfoboxRomanName(string Latin, string Moment);

    // Nomi in latino del campo «nome completo», ciascuno con il suo momento ripulito dal markup.
    internal static List<InfoboxRomanName> InfoboxRomanNames(string? value)
    {
        var result = new List<InfoboxRomanName>();
        foreach (Match match in InfoboxNamePattern().Matches(value ?? string.Empty))
        {
            var latin = Regex.Replace(match.Groups["la"].Value, @"\s+", " ").Trim();
            var moment = Regex.Replace(match.Groups["rest"].Value, @"<[^>]*>|\[\[(?:[^|\]]*\|)?|\]\]", " ");
            moment = Regex.Replace(moment.Replace('(', ' ').Replace(')', ' ').Replace(';', ' ').Replace(',', ' '), @"\s+", " ").Trim();
            if (latin.Split(' ').Length >= 2 && latin.All(c => char.IsLetter(c) || c is ' ' or '.'))
            {
                result.Add(new InfoboxRomanName(latin, moment));
            }
        }

        return result;
    }

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

    // Gens di nascita dal nomen d'origine di un nome nel tempo (Octavianus → gens Ottavia); vuota se non c'è.
    internal static string GensOfOriginNomen(IEnumerable<RomanNameRecord> names)
        => names.SelectMany(n => n.Parti).FirstOrDefault(p => p.Tipo == TipiParteNome.NomenOrigine) is { } part
            ? RomanNaming.GensOfOriginNomen(part.Latino)
            : string.Empty;

    // Righe di un nome: ogni parola latina con il suo tipo (quelli noti da Wikidata, poi le regole) e la parola italiana
    // nella stessa posizione, se le parole sono tante quante; altrimenti l'italiano proposto dal latino.
    internal static RomanNameRecord BuildIntroRomanName(string italian, string latin, IReadOnlyDictionary<string, string> knownTypes, IReadOnlyDictionary<string, string>? italianOf = null)
    {
        var name = new RomanNameRecord();
        // La filiazione resta una riga nel punto in cui è scritta (2026-11-14): «Imperator Caesar Divi filius Augustus».
        // Le parole del nome si leggono senza, per allineare il latino all'italiano.
        var filiation = LatinFiliationPattern().Match(latin);
        RomanNamePart? filiationPart = null;
        var filiationIndex = 0;
        if (filiation.Success)
        {
            var italianFiliation = ItalianFiliationPattern().Match(italian);
            filiationPart = new RomanNamePart
            {
                Tipo = TipiParteNome.Filiazione,
                Latino = filiation.Groups["fil"].Value.Trim(),
                Italiano = italianFiliation.Success ? italianFiliation.Value.Trim() : string.Empty,
            };
            filiationIndex = latin[..filiation.Index].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
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
            else if (knownTypes.TryGetValue(word, out var known) && known != TipiParteNome.Agnomen)
            {
                tipo = known;
            }
            else if (RomanNaming.OriginNomen(word) is { Length: > 0 } originNomen
                && knownTypes.GetValueOrDefault(originNomen) == TipiParteNome.Nomen)
            {
                // Aggettivo in -ianus da un nomen della persona (Octavius → Octavianus): nomen d'origine di un adottato,
                // anche se Wikidata lo indica come agnomen.
                tipo = TipiParteNome.NomenOrigine;
            }
            else if (known is not null)
            {
                tipo = known;
            }
            else if (TriumphalAgnomina.Contains(word) || (word == "Maximus" && TriumphalAgnomina.Contains(previous)))
            {
                tipo = TipiParteNome.Agnomen;
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
            var italianWord = italianWords.Length == latinWords.Length ? italianWords[i]
                : italianOf?.GetValueOrDefault(word) ?? RomanNaming.ItalianForm(word);
            name.Parti.Add(new RomanNamePart { Tipo = tipo, Latino = word, Italiano = italianWord });
        }

        if (filiationPart is not null)
        {
            name.Parti.Insert(Math.Min(filiationIndex, name.Parti.Count), filiationPart);
        }

        name.FieldsFromParts();
        return name;
    }

    // Nomi nel tempo dalle due fonti della voce italiana, unite: il campo «nome completo» dell'infobox e l'introduzione.
    // Un nome presente in entrambe si prende una volta sola (confronto senza filiazione e senza «Imperator» iniziale, che
    // l'introduzione spesso omette: vale la forma più completa, quella dell'infobox); uno presente in una sola si aggiunge.
    // Ordine: quelli dell'infobox come sono scritti; dall'introduzione il nome alla nascita va dopo gli altri nomi alla
    // nascita (o in testa), i cambi di nome e il nome iniziale prima dell'ultimo. L'ultimo è il principale. Vuoto se in
    // tutto c'è un solo nome: allora il nome resta quello di Wikidata.
    internal static List<RomanNameRecord> RomanNamesOverTime(
        string? introduction, IReadOnlyDictionary<string, string> knownTypes, int? birthYear, int? principalFrom, bool ruler, string? infoboxFullName = null)
    {
        static string Year(int? year) => year is not int y ? string.Empty : y < 0 ? $"{-y} a.C." : y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Key(string latin)
        {
            var text = Regex.Replace(LatinFiliationPattern().Replace(latin, string.Empty), @"\s+", " ").Trim();
            text = Regex.Replace(text, @"^Imperator\s+", string.Empty, RegexOptions.IgnoreCase);
            return text.ToLowerInvariant();
        }

        var found = IntroductionRomanNames(introduction);

        // Italiano parola per parola dalle coppie italiano/latino dell'introduzione.
        var italianOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in found)
        {
            var latinWords = LatinFiliationPattern().Replace(pair.Latin, string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var italianWords = ItalianFiliationPattern().Replace(pair.Italian, string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (latinWords.Length == italianWords.Length)
            {
                foreach (var (latin, italian) in latinWords.Zip(italianWords))
                {
                    italianOf.TryAdd(latin, italian);
                }
            }
        }

        var names = new List<(string Key, RomanNameRecord Name)>();
        foreach (var (latin, moment) in InfoboxRomanNames(infoboxFullName))
        {
            if (names.Any(n => n.Key == Key(latin)))
            {
                continue;
            }

            var name = BuildIntroRomanName(string.Empty, latin, knownTypes, italianOf);
            if (Regex.IsMatch(moment, @"\bnascita\b", RegexOptions.IgnoreCase))
            {
                (name.Motivo, name.Dal) = ("Nome alla nascita", Year(birthYear));
            }
            else if (Regex.IsMatch(moment, @"ascesa|principato|potere imperiale|acclamazione", RegexOptions.IgnoreCase))
            {
                (name.Motivo, name.Dal) = ("Assunzione del potere", Year(principalFrom));
            }
            else if (Regex.IsMatch(moment, @"\badozione\b", RegexOptions.IgnoreCase))
            {
                // Adozione per testamento (Augusto, adottato nel testamento di Cesare).
                name.Motivo = Regex.IsMatch(moment, @"testament", RegexOptions.IgnoreCase) ? "Adozione testamentaria" : "Adozione";
            }
            else if (moment.Length > 0)
            {
                name.Motivo = char.ToUpper(moment[0]) + moment[1..];
            }

            names.Add((Key(latin), name));
        }

        var fromInfobox = names.Count;

        // Dall'introduzione: prima i nomi alla nascita, poi i cambi di nome nell'ordine del testo, infine il nome iniziale.
        var fromIntroduction = found.Where(n => n.Kind == IntroNameKind.Birth)
            .Concat(found.Where(n => n.Kind == IntroNameKind.Change))
            .Concat(found.Where(n => n.Kind == IntroNameKind.Lead))
            .ToList();
        for (var j = 0; j < fromIntroduction.Count; j++)
        {
            var pair = fromIntroduction[j];
            var key = Key(pair.Latin);
            if (names.Any(n => n.Key == key))
            {
                continue;
            }

            var name = BuildIntroRomanName(pair.Italian, pair.Latin, knownTypes, italianOf);
            int index;
            switch (pair.Kind)
            {
                case IntroNameKind.Birth:
                    (name.Motivo, name.Dal) = ("Nome alla nascita", Year(birthYear));
                    index = names.FindLastIndex(n => n.Name.Motivo == "Nome alla nascita") + 1;
                    break;
                case IntroNameKind.Change:
                    name.Motivo = "Cambio di nome";
                    // Prima del nome che lo segue nel testo, se è già nell'elenco (Marco Annio Vero prima del nome dell'adozione);
                    // altrimenti prima del principale dell'infobox, o in coda senza infobox.
                    var following = fromIntroduction.Skip(j + 1).Select(n => Key(n.Latin)).ToHashSet();
                    index = names.FindIndex(n => following.Contains(n.Key));
                    if (index < 0)
                    {
                        index = fromInfobox > 0 ? names.Count - 1 : names.Count;
                    }
                    break;
                default:
                    // Il nome iniziale è il principale solo se l'infobox non ne ha uno più completo.
                    (name.Motivo, name.Dal) = ruler ? ("Assunzione del potere", Year(principalFrom)) : (string.Empty, string.Empty);
                    index = names.Any(n => n.Name.Motivo == "Assunzione del potere") ? names.Count - 1 : names.Count;
                    break;
            }

            names.Insert(Math.Clamp(index, 0, names.Count), (key, name));
        }

        if (names.Count < 2)
        {
            return [];
        }

        var result = names.Select(n => n.Name).ToList();
        var principal = result[^1];
        principal.Principale = true;
        if (ruler && principal.Motivo.Length == 0)
        {
            (principal.Motivo, principal.Dal) = ("Assunzione del potere", Year(principalFrom));
        }

        return result;
    }
}
