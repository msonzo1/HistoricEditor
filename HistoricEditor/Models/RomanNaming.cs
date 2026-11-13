using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Tipo di scheda per il nome: automatico (deciso dall'epoca), romano (tria nomina e gens) o standard (nome, cognome, famiglia).
public static class Onomastiche
{
    public const string Automatica = "";
    public const string Romana = "romana";
    public const string Standard = "standard";
}

// Nome portato in un periodo della vita (alla nascita, dopo un'adozione, all'assunzione del potere…), con le parti in
// italiano e in latino. Uno dei nomi è il principale: le sue parti sono anche quelle della persona (Prenome, Praenomen…),
// usate negli elenchi, nel nome latino e nella forma epigrafica.
public sealed class RomanNameRecord
{
    // Testo libero dei nomi salvati prima del 2026-11-13 (solo il nome latino): alla lettura passa in NomeLatino.
    public string Nome { get; set; } = string.Empty;
    public string Dal { get; set; } = string.Empty;
    public string Al { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public bool Principale { get; set; }

    public string Prenome { get => field ?? string.Empty; set; } = string.Empty;
    public string Gentilizio { get => field ?? string.Empty; set; } = string.Empty;
    public string CognomeRomano { get => field ?? string.Empty; set; } = string.Empty;
    public string Agnome { get => field ?? string.Empty; set; } = string.Empty;
    public string Praenomen { get => field ?? string.Empty; set; } = string.Empty;
    public string Nomen { get => field ?? string.Empty; set; } = string.Empty;
    public string Cognomen { get => field ?? string.Empty; set; } = string.Empty;
    public string Agnomen { get => field ?? string.Empty; set; } = string.Empty;
    public string Filiazione { get => field ?? string.Empty; set; } = string.Empty;
    public string Tribu { get => field ?? string.Empty; set; } = string.Empty;
    public string NomeLatino { get => field ?? string.Empty; set; } = string.Empty;

    public RomanNameRecord Clone() => (RomanNameRecord)MemberwiseClone();

    public bool IsEmpty() => new[] { Nome, Dal, Al, Motivo }.All(string.IsNullOrWhiteSpace) && !HasParts;

    public bool HasParts => RomanNaming.HasRomanParts(Prenome, Gentilizio, CognomeRomano, Agnome, Praenomen, Nomen, Cognomen, Agnomen, NomeLatino);

    public string ItalianName => string.Join(' ', new[] { Prenome, Gentilizio, CognomeRomano, Agnome }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

    public string LatinName => RomanNaming.LatinName(Praenomen, Nomen, Cognomen, Agnomen, NomeLatino);

    public string Epigraphic => RomanNaming.Epigraphic(Praenomen, Nomen, Filiazione, Tribu, Cognomen, Agnomen);

    // Nome negli elenchi: in italiano, altrimenti in latino.
    public string DisplayName => ItalianName.Length > 0 ? ItalianName : LatinName.Length > 0 ? LatinName : "(nome non indicato)";

    // Nome latino sotto quello italiano, se diverso.
    public string LatinSubtitle => ItalianName.Length > 0 && !string.Equals(LatinName, ItalianName, StringComparison.OrdinalIgnoreCase) ? LatinName : string.Empty;

    public string Marker => Principale ? "★" : string.Empty;

    // «dal 138 al 161», «dal 161», «fino al 138».
    public string Period => (Dal.Trim().Length > 0, Al.Trim().Length > 0) switch
    {
        (true, true) => $"dal {Dal.Trim()} al {Al.Trim()}",
        (true, false) => $"dal {Dal.Trim()}",
        (false, true) => $"fino al {Al.Trim()}",
        _ => string.Empty,
    };

    // Motivi proposti nella scheda; il campo resta libero.
    public static IReadOnlyList<string> Reasons { get; } =
    [
        "Nome alla nascita",
        "Adozione",
        "Adozione testamentaria",
        "Assunzione del potere",
        "Titolo onorifico",
        "Manomissione",
        "Cambio di nome",
    ];
}

// Onomastica romana: riconoscimento dei personaggi romani, nome latino e forma epigrafica abbreviata.
public static class RomanNaming
{
    // Periodo in cui la scheda diventa romana in automatico: dalla fondazione di Roma alla caduta dell'Impero d'Occidente.
    public const int FirstYear = -753;
    public const int LastYear = 476;

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

    // Abbreviazioni canoniche dei praenomina, come compaiono nelle epigrafi.
    private static readonly Dictionary<string, string> PraenomenAbbreviations = new(Comparer)
    {
        ["Appius"] = "Ap.",
        ["Aulus"] = "A.",
        ["Decimus"] = "D.",
        ["Gaius"] = "C.",
        ["Caius"] = "C.",
        ["Gnaeus"] = "Cn.",
        ["Cnaeus"] = "Cn.",
        ["Kaeso"] = "K.",
        ["Caeso"] = "K.",
        ["Lucius"] = "L.",
        ["Mamercus"] = "Mam.",
        ["Manius"] = "M'.",
        ["Marcus"] = "M.",
        ["Numerius"] = "N.",
        ["Opiter"] = "Opet.",
        ["Postumus"] = "Post.",
        ["Publius"] = "P.",
        ["Quintus"] = "Q.",
        ["Servius"] = "Ser.",
        ["Sextus"] = "Sex.",
        ["Spurius"] = "Sp.",
        ["Tiberius"] = "Ti.",
        ["Titus"] = "T.",
        ["Vibius"] = "V.",
    };

    // Praenomina proposti nella scheda (forma estesa).
    public static IReadOnlyList<string> Praenomina { get; } =
    [
        "Appius", "Aulus", "Decimus", "Gaius", "Gnaeus", "Kaeso", "Lucius", "Mamercus", "Manius", "Marcus",
        "Numerius", "Opiter", "Postumus", "Publius", "Quintus", "Servius", "Sextus", "Spurius", "Tiberius", "Titus", "Vibius",
    ];

    // Le 35 tribù romane con l'abbreviazione epigrafica.
    private static readonly (string Name, string Abbreviation)[] TribeTable =
    [
        ("Aemilia", "Aem."), ("Aniensis", "Ani."), ("Arnensis", "Arn."), ("Camilia", "Cam."), ("Claudia", "Cla."),
        ("Clustumina", "Clu."), ("Collina", "Col."), ("Cornelia", "Cor."), ("Esquilina", "Esq."), ("Fabia", "Fab."),
        ("Falerna", "Fal."), ("Galeria", "Gal."), ("Horatia", "Hor."), ("Lemonia", "Lem."), ("Maecia", "Mae."),
        ("Menenia", "Men."), ("Oufentina", "Ouf."), ("Palatina", "Pal."), ("Papiria", "Pap."), ("Pollia", "Pol."),
        ("Pomptina", "Pom."), ("Publilia", "Pub."), ("Pupinia", "Pup."), ("Quirina", "Qui."), ("Romilia", "Rom."),
        ("Sabatina", "Sab."), ("Scaptia", "Sca."), ("Sergia", "Ser."), ("Stellatina", "Ste."), ("Suburana", "Suc."),
        ("Terentina", "Ter."), ("Tromentina", "Tro."), ("Velina", "Vel."), ("Voltinia", "Vol."), ("Voturia", "Vot."),
    ];

    public static IReadOnlyList<string> Tribes { get; } = [.. TribeTable.Select(t => t.Name)];

    // Scheda romana: scelta esplicita, altrimenti se ha già parti del nome romano o se è nato (o morto) tra il 753 a.C. e il 476.
    public static bool IsRoman(string? onomastica, string? birth, string? death, bool hasRomanParts)
        => onomastica switch
        {
            Onomastiche.Romana => true,
            Onomastiche.Standard => false,
            _ => hasRomanParts || IsRomanEra(birth, death),
        };

    public static bool IsRoman(HistoricalPerson person)
        => IsRoman(person.Onomastica, person.Birth, person.Death, HasRomanParts(person));

    public static bool IsRomanEra(string? birth, string? death)
        => (HistoricalYear.Parse(birth) ?? HistoricalYear.Parse(death)) is int year && year >= FirstYear && year <= LastYear;

    public static bool HasRomanParts(HistoricalPerson person)
        => HasRomanParts(
            person.Praenomen, person.Nomen, person.Cognomen, person.Agnomen, person.NomeLatino,
            person.Prenome, person.Gentilizio, person.CognomeRomano, person.Agnome);

    public static bool HasRomanParts(params string?[] parts) => parts.Any(p => !string.IsNullOrWhiteSpace(p));

    // Nome latino completo: quello scritto a mano, altrimenti praenomen, nomen, cognomen e agnomen.
    public static string LatinName(string? praenomen, string? nomen, string? cognomen, string? agnomen, string? free)
        => !string.IsNullOrWhiteSpace(free) ? free.Trim() : Join(praenomen, nomen, cognomen, agnomen);

    public static string LatinName(HistoricalPerson person)
        => LatinName(person.Praenomen, person.Nomen, person.Cognomen, person.Agnomen, person.NomeLatino);

    // Forma delle epigrafi: «C. Iulius C. f. Fab. Caesar». Vuota se mancano praenomen e nomen.
    public static string Epigraphic(string? praenomen, string? nomen, string? filiazione, string? tribu, string? cognomen, string? agnomen)
    {
        if (string.IsNullOrWhiteSpace(praenomen) && string.IsNullOrWhiteSpace(nomen))
        {
            return string.Empty;
        }

        return Join(AbbreviatePraenomen(praenomen), nomen, filiazione, AbbreviateTribe(tribu), cognomen, agnomen);
    }

    public static string Epigraphic(HistoricalPerson person)
        => Epigraphic(person.Praenomen, person.Nomen, person.Filiazione, person.Tribu, person.Cognomen, person.Agnomen);

    // «Gaius» → «C.»; un'abbreviazione già scritta («C.») resta com'è; un praenomen sconosciuto resta per esteso.
    public static string AbbreviatePraenomen(string? praenomen)
    {
        var text = (praenomen ?? string.Empty).Trim();
        return PraenomenAbbreviations.TryGetValue(text, out var abbreviation) ? abbreviation : text;
    }

    public static string AbbreviateTribe(string? tribu)
    {
        var text = (tribu ?? string.Empty).Trim();
        var match = TribeTable.FirstOrDefault(t => Comparer.Equals(t.Name, text));
        return match.Name is null ? text : match.Abbreviation;
    }

    // Filiazione dal praenomen del padre: «Gaius» → «C. f.» (Gai filius). Vuota se il padre non ha praenomen.
    public static string Filiation(string? fatherPraenomen)
    {
        var abbreviation = AbbreviatePraenomen(fatherPraenomen);
        return abbreviation.Length == 0 ? string.Empty : $"{abbreviation} f.";
    }

    // Nomen al femminile, come lo portavano le donne: «Iulius» → «Iulia», «Antonius» → «Antonia»;
    // vale anche per le forme italiane e spagnole: «Giulio» → «Giulia», «Julio» → «Julia».
    public static string FeminineNomen(string? nomen)
    {
        var text = (nomen ?? string.Empty).Trim();
        if (text.Length <= 3)
        {
            return text;
        }

        return text.EndsWith("us", StringComparison.OrdinalIgnoreCase) ? text[..^2] + "a"
            : text.EndsWith('o') ? text[..^1] + "a"
            : text;
    }

    // Forma italiana di un nome latino («Marcus Aelius Aurelius Antoninus» → «Marco Elio Aurelio Antonino»), per le parti
    // del nome romano di cui non si trova la forma italiana scritta da nessuna parte. Regole degli adattamenti italiani:
    // finali -ius → -io, -us → -o, -ar → -are, -o → -one, -is → -e, -ns → -nte, -ex → -ice, -x → -ce; I- davanti a vocale
    // → Gi-, H- iniziale tolta, ae/oe → e, ph → f, th → t, ct/pt → tt, xt → st, x → ss, nst → st, ti + vocale → zi.
    // I nomi che non seguono le regole sono nell'elenco delle eccezioni.
    public static string ItalianForm(string? latin)
        => string.Join(' ', (latin ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ItalianWord));

    private static readonly Dictionary<string, string> ItalianExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gnaeus"] = "Gneo",
        ["Sulla"] = "Silla",
        ["Pompeius"] = "Pompeo",
        ["Pompeia"] = "Pompea",
        ["Constantius"] = "Costanzo",
        ["Iuvenalis"] = "Giovenale",
        ["Vergilius"] = "Virgilio",
        ["Alexander"] = "Alessandro",
        ["Paulus"] = "Paolo",
        ["Paullus"] = "Paolo",
        ["Romulus"] = "Romolo",
        ["Tertius"] = "Terzo",
    };

    private static string ItalianWord(string word)
    {
        if (ItalianExceptions.TryGetValue(word, out var known))
        {
            return known;
        }

        // Solo parole latine scritte con lettere semplici; il resto (numeri, «f.», nomi già italiani con accenti) resta com'è.
        if (word.Length < 2 || !word.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
        {
            return word;
        }

        var w = word.ToLowerInvariant().Replace('j', 'i').Replace('k', 'c').Replace('y', 'i');
        w = Regex.Replace(w, "^h", string.Empty);
        w = Regex.Replace(w, "^i(?=[aeiou])", "gi");
        w = w.Replace("ae", "e").Replace("oe", "e").Replace("ph", "f").Replace("th", "t").Replace("rh", "r");

        // Finali, prima dei gruppi di consonanti (la x finale di «Felix» non diventa «ss»).
        w = w switch
        {
            _ when w.EndsWith("ius") => w[..^3] + "io",
            _ when w.EndsWith("us") || w.EndsWith("um") => w[..^2] + "o",
            _ when w.EndsWith("ar") => w + "e",
            _ when w.EndsWith("o") && w.Length > 2 => w + "ne",
            _ when w.EndsWith("is") => w[..^2] + "e",
            _ when w.EndsWith("ns") => w[..^1] + "te",
            _ when w.EndsWith("ex") => w[..^2] + "ice",
            _ when w.EndsWith("x") => w[..^1] + "ce",
            _ => w,
        };

        w = w.Replace("ct", "tt").Replace("pt", "tt").Replace("xt", "st").Replace("x", "ss").Replace("nst", "st");
        w = Regex.Replace(w, "(?<=[^s])ti(?=[aeiou])", "zi");
        return char.ToUpperInvariant(w[0]) + w[1..];
    }

    // Valori ripuliti per il salvataggio: spazi tolti, righe vuote scartate, un solo nome principale.
    public static List<RomanNameRecord> Normalize(IEnumerable<RomanNameRecord> names)
    {
        var result = names
            .Select(n => new RomanNameRecord
            {
                Dal = n.Dal.Trim(), Al = n.Al.Trim(), Motivo = n.Motivo.Trim(), Principale = n.Principale,
                Prenome = n.Prenome.Trim(), Gentilizio = n.Gentilizio.Trim(), CognomeRomano = n.CognomeRomano.Trim(), Agnome = n.Agnome.Trim(),
                Praenomen = n.Praenomen.Trim(), Nomen = n.Nomen.Trim(), Cognomen = n.Cognomen.Trim(), Agnomen = n.Agnomen.Trim(),
                Filiazione = n.Filiazione.Trim(), Tribu = n.Tribu.Trim(),
                NomeLatino = n.NomeLatino.Trim().Length > 0 ? n.NomeLatino.Trim() : n.Nome.Trim(),
            })
            .Where(n => !n.IsEmpty())
            .ToList();
        var principal = result.FirstOrDefault(n => n.Principale) ?? result.LastOrDefault();
        foreach (var name in result)
        {
            name.Principale = ReferenceEquals(name, principal);
        }

        return result;
    }

    // Nomi della persona per la scheda: quelli salvati, con il principale allineato ai campi della persona (che restano
    // il riferimento: l'import li aggiorna). Senza un principale salvato, i campi della persona diventano il principale:
    // nel nome salvato con lo stesso nome latino (schede con i vecchi «Nomi nel tempo»), altrimenti in un nome nuovo in fondo.
    public static List<RomanNameRecord> NamesOf(HistoricalPerson person)
    {
        var names = Normalize((person.NomiNelTempo ?? []).Select(n => n.Clone()));
        foreach (var name in names)
        {
            name.Principale = false;
        }

        var saved = (person.NomiNelTempo ?? []).Where(n => !n.IsEmpty()).ToList();
        var principalIndex = saved.FindIndex(n => n.Principale);
        var personLatin = LatinName(person);
        RomanNameRecord? principal = principalIndex >= 0 ? names[principalIndex] : null;
        principal ??= names.FirstOrDefault(n => !RomanNaming.HasRomanParts(n.Praenomen, n.Nomen, n.Cognomen, n.Agnomen, n.Prenome, n.Gentilizio, n.CognomeRomano, n.Agnome)
            && personLatin.Length > 0 && string.Equals(n.NomeLatino, personLatin, StringComparison.OrdinalIgnoreCase));
        if (principal is null)
        {
            if (!HasRomanParts(person) && names.Count > 0)
            {
                principal = names[^1];
                principal.Principale = true;
                return names;
            }

            principal = new RomanNameRecord();
            names.Add(principal);
        }

        principal.Principale = true;
        CopyParts(person, principal);
        return HasRomanParts(person) || names.Count > 1 || !principal.IsEmpty() ? names : [];
    }

    // Salva nella persona i nomi e copia le parti del principale nei campi della persona.
    public static void SetNames(HistoricalPerson person, IEnumerable<RomanNameRecord> names)
    {
        var list = Normalize(names);
        var principal = list.FirstOrDefault(n => n.Principale) ?? new RomanNameRecord();
        person.Prenome = principal.Prenome;
        person.Gentilizio = principal.Gentilizio;
        person.CognomeRomano = principal.CognomeRomano;
        person.Agnome = principal.Agnome;
        person.Praenomen = principal.Praenomen;
        person.Nomen = principal.Nomen;
        person.Cognomen = principal.Cognomen;
        person.Agnomen = principal.Agnomen;
        person.Filiazione = principal.Filiazione;
        person.Tribu = principal.Tribu;
        person.NomeLatino = principal.NomeLatino;
        // Un solo nome senza periodo né motivo è già tutto nei campi della persona.
        person.NomiNelTempo = list.Count == 1 && list[0].Dal.Length == 0 && list[0].Al.Length == 0 && list[0].Motivo.Length == 0 ? [] : list;
    }

    private static void CopyParts(HistoricalPerson person, RomanNameRecord name)
    {
        name.Prenome = person.Prenome.Trim();
        name.Gentilizio = person.Gentilizio.Trim();
        name.CognomeRomano = person.CognomeRomano.Trim();
        name.Agnome = person.Agnome.Trim();
        name.Praenomen = person.Praenomen.Trim();
        name.Nomen = person.Nomen.Trim();
        name.Cognomen = person.Cognomen.Trim();
        name.Agnomen = person.Agnomen.Trim();
        name.Filiazione = person.Filiazione.Trim();
        name.Tribu = person.Tribu.Trim();
        // Il nome latino libero della persona; quello del vecchio testo libero resta se la persona non ne ha.
        if (person.NomeLatino.Trim().Length > 0 || RomanNaming.HasRomanParts(person.Praenomen, person.Nomen, person.Cognomen, person.Agnomen))
        {
            name.NomeLatino = person.NomeLatino.Trim();
        }
    }

    private static string Join(params string?[] parts)
        => string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
