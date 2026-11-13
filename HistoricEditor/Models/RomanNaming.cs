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

// Tipi delle parti del nome romano, nell'ordine in cui si propongono.
public static class TipiParteNome
{
    public const string Praenomen = "Praenomen";
    public const string Nomen = "Nomen";
    public const string Cognomen = "Cognomen";
    public const string Agnomen = "Agnomen";
    // Imperator, Caesar, Augustus: titoli che fanno parte del nome (praenomen imperatoris, titolo dinastico, titolo imperiale).
    public const string Titolo = "Titolo";

    public static IReadOnlyList<string> All { get; } = [Praenomen, Nomen, Cognomen, Agnomen, Titolo];
}

// Una parte del nome romano: tipo, forma latina e forma italiana. Nel modulo del nome, scrivendo il latino, l'italiano
// vuoto (o ancora uguale a quello proposto) si compone da solo con le regole dell'italiano («Aurelius» → «Aurelio»).
public sealed class RomanNamePart : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Tipo
    {
        get => field ?? TipiParteNome.Cognomen;
        set { field = value; Changed(nameof(Tipo)); }
    } = TipiParteNome.Cognomen;

    public string Latino
    {
        get => field ?? string.Empty;
        set
        {
            var proposed = RomanNaming.ItalianForm(field);
            var follow = AutoItalian && (Italiano.Trim().Length == 0 || string.Equals(Italiano.Trim(), proposed, StringComparison.Ordinal));
            field = value;
            Changed(nameof(Latino));
            if (follow)
            {
                Italiano = RomanNaming.ItalianForm(value);
            }
        }
    } = string.Empty;

    public string Italiano
    {
        get => field ?? string.Empty;
        set { field = value; Changed(nameof(Italiano)); }
    } = string.Empty;

    // Solo nel modulo del nome: altrove (lettura dal database, import) l'italiano non si compone da solo.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AutoItalian { get; set; }

    public bool IsEmpty() => Latino.Trim().Length == 0 && Italiano.Trim().Length == 0;

    public RomanNamePart Clone() => new() { Tipo = Tipo, Latino = Latino, Italiano = Italiano };

    private void Changed(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

// Nome portato in un periodo della vita (alla nascita, dopo un'adozione, all'assunzione del potere…). Le parti sono righe
// ordinate (Parti: tipo, latino, italiano), perché un nome può avere più nomina o cognomina e titoli in posizioni diverse
// («Caesar Marcus Aurelius Antoninus Augustus»). I campi singoli (Praenomen, Nomen…, Prenome, Gentilizio…) ne sono il
// riassunto per tipo; nei nomi salvati prima delle righe sono la fonte da cui si ricavano.
// Uno dei nomi è il principale: il suo riassunto è anche nei campi della persona, usati negli elenchi.
public sealed class RomanNameRecord
{
    // Testo libero dei nomi salvati prima del 2026-11-13 (solo il nome latino): alla lettura passa in NomeLatino.
    public string Nome { get; set; } = string.Empty;
    public string Dal { get; set; } = string.Empty;
    public string Al { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public bool Principale { get; set; }

    public List<RomanNamePart> Parti { get => field ??= []; set; } = [];

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

    public RomanNameRecord Clone()
    {
        var clone = (RomanNameRecord)MemberwiseClone();
        clone.Parti = [.. Parti.Select(p => p.Clone())];
        return clone;
    }

    public bool IsEmpty() => new[] { Nome, Dal, Al, Motivo }.All(string.IsNullOrWhiteSpace) && !HasParts;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasParts => Parti.Any(p => !p.IsEmpty())
        || RomanNaming.HasRomanParts(Prenome, Gentilizio, CognomeRomano, Agnome, Praenomen, Nomen, Cognomen, Agnomen, NomeLatino);

    // Righe ricavate dai campi singoli (nomi salvati prima delle righe, campi della persona, import): una riga per parola,
    // con l'italiano accanto se ha lo stesso numero di parole («Aelius Aurelius» / «Elio Aurelio»), altrimenti nella prima.
    public void PartsFromFields()
    {
        var parts = new List<RomanNamePart>();
        void Add(string tipo, string latin, string italian)
        {
            var latinWords = latin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var italianWords = italian.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (latinWords.Length == italianWords.Length || latinWords.Length == 0 || italianWords.Length == 0)
            {
                var count = Math.Max(latinWords.Length, italianWords.Length);
                for (var i = 0; i < count; i++)
                {
                    parts.Add(new RomanNamePart { Tipo = tipo, Latino = i < latinWords.Length ? latinWords[i] : string.Empty, Italiano = i < italianWords.Length ? italianWords[i] : string.Empty });
                }

                return;
            }

            for (var i = 0; i < latinWords.Length; i++)
            {
                parts.Add(new RomanNamePart { Tipo = tipo, Latino = latinWords[i], Italiano = i == 0 ? italian.Trim() : string.Empty });
            }
        }

        Add(TipiParteNome.Praenomen, Praenomen, Prenome);
        Add(TipiParteNome.Nomen, Nomen, Gentilizio);
        Add(TipiParteNome.Cognomen, Cognomen, CognomeRomano);
        Add(TipiParteNome.Agnomen, Agnomen, Agnome);
        Parti = parts;
    }

    // Campi singoli dal riassunto delle righe (senza righe restano quelli che ci sono).
    public void FieldsFromParts()
    {
        if (Parti.Count == 0)
        {
            return;
        }

        string Join(string tipo, Func<RomanNamePart, string> value)
            => string.Join(' ', Parti.Where(p => p.Tipo == tipo).Select(value).Select(v => v.Trim()).Where(v => v.Length > 0));
        Praenomen = Join(TipiParteNome.Praenomen, p => p.Latino);
        Nomen = Join(TipiParteNome.Nomen, p => p.Latino);
        Cognomen = Join(TipiParteNome.Cognomen, p => p.Latino);
        Agnomen = Join(TipiParteNome.Agnomen, p => p.Latino);
        Prenome = Join(TipiParteNome.Praenomen, p => p.Italiano);
        Gentilizio = Join(TipiParteNome.Nomen, p => p.Italiano);
        CognomeRomano = Join(TipiParteNome.Cognomen, p => p.Italiano);
        Agnome = Join(TipiParteNome.Agnomen, p => p.Italiano);
    }

    // Righe da mostrare: quelle salvate o, se mancano, quelle ricavate dai campi singoli.
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<RomanNamePart> OrderedParts
    {
        get
        {
            if (Parti.Count > 0)
            {
                return Parti;
            }

            var copy = Clone();
            copy.PartsFromFields();
            return copy.Parti;
        }
    }

    // Nome latino composto dalle righe, nell'ordine scritto (titoli compresi).
    [System.Text.Json.Serialization.JsonIgnore]
    public string ComposedLatin => string.Join(' ', OrderedParts.Select(p => p.Latino.Trim()).Where(v => v.Length > 0));

    [System.Text.Json.Serialization.JsonIgnore]
    public string ItalianName => string.Join(' ', OrderedParts.Select(p => p.Italiano.Trim()).Where(v => v.Length > 0));

    [System.Text.Json.Serialization.JsonIgnore]
    public string LatinName => NomeLatino.Trim().Length > 0 ? NomeLatino.Trim() : ComposedLatin;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Epigraphic => RomanNaming.Epigraphic(OrderedParts, Filiazione, Tribu);

    // Nome negli elenchi: in italiano, altrimenti in latino.
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => ItalianName.Length > 0 ? ItalianName : LatinName.Length > 0 ? LatinName : "(nome non indicato)";

    // Nome latino sotto quello italiano, se diverso.
    [System.Text.Json.Serialization.JsonIgnore]
    public string LatinSubtitle => ItalianName.Length > 0 && !string.Equals(LatinName, ItalianName, StringComparison.OrdinalIgnoreCase) ? LatinName : string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Marker => Principale ? "★" : string.Empty;

    // «dal 138 al 161», «dal 161», «fino al 138».
    [System.Text.Json.Serialization.JsonIgnore]
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

    // Della persona: dal nome principale (con i titoli e nell'ordine scritto), se c'è.
    public static string Epigraphic(HistoricalPerson person)
        => NamesOf(person).FirstOrDefault(n => n.Principale) is { } principal
            ? principal.Epigraphic
            : Epigraphic(person.Praenomen, person.Nomen, person.Filiazione, person.Tribu, person.Cognomen, person.Agnomen);

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
        ["Imperator"] = "Imperatore",
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

    // Valori ripuliti per il salvataggio: spazi tolti, righe vuote scartate, un solo nome principale. I nomi senza righe
    // (salvati prima) le ricavano dai campi singoli; i campi singoli sono poi il riassunto delle righe.
    public static List<RomanNameRecord> Normalize(IEnumerable<RomanNameRecord> names)
    {
        var result = new List<RomanNameRecord>();
        foreach (var n in names)
        {
            var name = new RomanNameRecord
            {
                Dal = n.Dal.Trim(), Al = n.Al.Trim(), Motivo = n.Motivo.Trim(), Principale = n.Principale,
                Prenome = n.Prenome.Trim(), Gentilizio = n.Gentilizio.Trim(), CognomeRomano = n.CognomeRomano.Trim(), Agnome = n.Agnome.Trim(),
                Praenomen = n.Praenomen.Trim(), Nomen = n.Nomen.Trim(), Cognomen = n.Cognomen.Trim(), Agnomen = n.Agnomen.Trim(),
                Filiazione = n.Filiazione.Trim(), Tribu = n.Tribu.Trim(),
                NomeLatino = n.NomeLatino.Trim().Length > 0 ? n.NomeLatino.Trim() : n.Nome.Trim(),
                Parti = [.. n.Parti.Where(p => !p.IsEmpty()).Select(p => new RomanNamePart { Tipo = p.Tipo, Latino = p.Latino.Trim(), Italiano = p.Italiano.Trim() })],
            };
            if (name.Parti.Count == 0)
            {
                name.PartsFromFields();
            }

            name.FieldsFromParts();
            if (!name.IsEmpty())
            {
                result.Add(name);
            }
        }

        var principal = result.FirstOrDefault(n => n.Principale) ?? result.LastOrDefault();
        foreach (var name in result)
        {
            name.Principale = ReferenceEquals(name, principal);
        }

        return result;
    }

    // Nome latino da salvare nella persona per il nome principale: quello libero o, se le righe hanno titoli o un ordine
    // diverso da praenomen-nomen-cognomen-agnomen («Caesar Marcus Aurelius Antoninus Augustus»), quello composto dalle righe.
    private static string PersonLatin(RomanNameRecord principal)
    {
        if (principal.NomeLatino.Trim().Length > 0)
        {
            return principal.NomeLatino.Trim();
        }

        var composed = principal.ComposedLatin;
        return string.Equals(composed, Join(principal.Praenomen, principal.Nomen, principal.Cognomen, principal.Agnomen), StringComparison.Ordinal)
            ? string.Empty
            : composed;
    }

    // Il principale salvato corrisponde ancora ai campi della persona (che l'import può aver aggiornato)?
    private static bool MatchesPerson(RomanNameRecord principal, HistoricalPerson person)
        => principal.Praenomen == person.Praenomen.Trim() && principal.Nomen == person.Nomen.Trim()
            && principal.Cognomen == person.Cognomen.Trim() && principal.Agnomen == person.Agnomen.Trim()
            && principal.Prenome == person.Prenome.Trim() && principal.Gentilizio == person.Gentilizio.Trim()
            && principal.CognomeRomano == person.CognomeRomano.Trim() && principal.Agnome == person.Agnome.Trim()
            && principal.Filiazione == person.Filiazione.Trim() && principal.Tribu == person.Tribu.Trim()
            && PersonLatin(principal) == person.NomeLatino.Trim();

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
        RomanNameRecord? principal = principalIndex >= 0 && principalIndex < names.Count ? names[principalIndex] : null;
        principal ??= names.FirstOrDefault(n => n.Parti.Count == 0
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
        if (!MatchesPerson(principal, person))
        {
            CopyParts(person, principal);
        }

        return HasRomanParts(person) || names.Count > 1 || !principal.IsEmpty() ? names : [];
    }

    // Salva nella persona i nomi e copia il riassunto del principale nei campi della persona.
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
        person.NomeLatino = PersonLatin(principal);
        // Un solo nome senza periodo né motivo, con le parti nell'ordine consueto e senza titoli, è già tutto nei campi
        // della persona.
        var plain = list.Count == 1 && list[0].Dal.Length == 0 && list[0].Al.Length == 0 && list[0].Motivo.Length == 0
            && list[0].Parti.All(p => p.Tipo != TipiParteNome.Titolo) && PlainOrder(list[0]);
        person.NomiNelTempo = plain ? [] : list;
    }

    // Righe nell'ordine praenomen, nomen, cognomen, agnomen (quello che si ricava dai campi singoli).
    private static bool PlainOrder(RomanNameRecord name)
    {
        var order = name.Parti.Select(p => Array.IndexOf(new[] { TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Cognomen, TipiParteNome.Agnomen }, p.Tipo)).ToList();
        return order.Zip(order.Skip(1)).All(pair => pair.First <= pair.Second);
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
        if (person.NomeLatino.Trim().Length > 0 || HasRomanParts(person.Praenomen, person.Nomen, person.Cognomen, person.Agnomen))
        {
            name.NomeLatino = person.NomeLatino.Trim();
        }

        name.PartsFromFields();
        // Un nome latino che è solo la composizione delle righe non è un nome libero.
        if (string.Equals(name.NomeLatino, name.ComposedLatin, StringComparison.Ordinal))
        {
            name.NomeLatino = string.Empty;
        }
    }

    // Forma epigrafica dalle righe, nell'ordine scritto: praenomen e titoli abbreviati, filiazione e tribù dopo l'ultimo
    // nomen («IMP. CAES. M. Aurelius M. f. Antoninus AVG.»). Vuota se mancano praenomen e nomen.
    public static string Epigraphic(IEnumerable<RomanNamePart> parts, string? filiazione, string? tribu)
    {
        var list = parts.Where(p => p.Latino.Trim().Length > 0).ToList();
        if (!list.Any(p => p.Tipo is TipiParteNome.Praenomen or TipiParteNome.Nomen))
        {
            return string.Empty;
        }

        var lastNomen = list.FindLastIndex(p => p.Tipo == TipiParteNome.Nomen);
        if (lastNomen < 0)
        {
            lastNomen = list.FindLastIndex(p => p.Tipo == TipiParteNome.Praenomen);
        }

        var words = new List<string?>();
        for (var i = 0; i < list.Count; i++)
        {
            var part = list[i];
            words.Add(part.Tipo switch
            {
                TipiParteNome.Praenomen => AbbreviatePraenomen(part.Latino),
                TipiParteNome.Titolo => TitleAbbreviations.GetValueOrDefault(part.Latino.Trim(), part.Latino.Trim()),
                _ => part.Latino.Trim(),
            });
            if (i == lastNomen)
            {
                words.Add(filiazione);
                words.Add(AbbreviateTribe(tribu));
            }
        }

        return Join([.. words]);
    }

    private static readonly Dictionary<string, string> TitleAbbreviations = new(Comparer)
    {
        ["Imperator"] = "IMP.",
        ["Caesar"] = "CAES.",
        ["Augustus"] = "AVG.",
        ["Augusta"] = "AVG.",
    };

    private static string Join(params string?[] parts)
        => string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
