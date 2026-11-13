namespace HistoricEditor;

using System.Text.Json.Serialization;

public sealed class HistoricalPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // Cognome dei personaggi moderni; facoltativo e indipendente da suffisso e famiglia.
    public string Surname { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    // Dinastie o casate, separate da «;» (es. «Asburgo; Lorena»); per i Romani la dinastia, non la gens.
    public string Family { get; set; } = string.Empty;

    // Gens romana e suoi rami, separati da «;» («gens Ulpia; Ulpii Traiani»), distinta dalla dinastia (Nerva-Antonina).
    public string Gens { get; set; } = string.Empty;
    public string Sesso { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string MainTitle { get; set; } = string.Empty;
    public List<PersonTitle> Titles { get; set; } = [];
    public List<LocalizedPersonName> LocalizedNames { get; set; } = [];

    // Onomastica romana (vedi RomanNaming): vuota = decisa in automatico dall'epoca.
    public string Onomastica { get; set; } = string.Empty;
    public string Praenomen { get; set; } = string.Empty;
    public string Nomen { get; set; } = string.Empty;

    // Uno o più cognomina, separati da spazio («Lentulus Crus»).
    public string Cognomen { get; set; } = string.Empty;
    public string Agnomen { get; set; } = string.Empty;

    // Filiazione abbreviata, es. «C. f.» (Gai filius).
    public string Filiazione { get; set; } = string.Empty;
    public string Tribu { get; set; } = string.Empty;

    // Nome latino scritto a mano, quando non si compone bene dalle parti; ha la precedenza sulla composizione.
    public string NomeLatino { get; set; } = string.Empty;

    // Le stesse parti del nome romano in italiano («Gaio», «Giulio», «Cesare»): sono quelle mostrate nella scheda;
    // le forme latine (Praenomen, Nomen…) stanno nella tendina «Nome in latino». Le altre lingue sono in LocalizedNames.
    public string Prenome { get; set; } = string.Empty;
    public string Gentilizio { get; set; } = string.Empty;
    public string CognomeRomano { get; set; } = string.Empty;
    public string Agnome { get; set; } = string.Empty;
    public List<RomanNameRecord> NomiNelTempo { get; set; } = [];

    // Un solo padre e una sola madre; i figli si ricavano da questi riferimenti.
    public int? FatherId { get; set; }
    public int? MotherId { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Father { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Mother { get; set; }
    public string Birth { get; set; } = string.Empty;
    public string Death { get; set; } = string.Empty;

    // Calendario (giuliano/gregoriano) e stile di inizio dell'anno in cui sono scritte le date: vedi HistoricalCalendar.
    public string BirthCalendario { get; set; } = string.Empty;
    public string BirthStile { get; set; } = string.Empty;
    public string DeathCalendario { get; set; } = string.Empty;
    public string DeathStile { get; set; } = string.Empty;
    public string BirthPlace { get; set; } = string.Empty;
    public string DeathPlace { get; set; } = string.Empty;
    public string BurialPlace { get; set; } = string.Empty;
    public string WikipediaTitle { get; set; } = string.Empty;
    public string WikipediaUrl { get; set; } = string.Empty;
    public string WikipediaExtract { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;

    // Identificativo del profilo WikiTree (es. «Habsburg-105»).
    public string WikiTreeId { get; set; } = string.Empty;

    // Note libere formattate (FlowDocument XAML, vedi RichNotes).
    public string Note { get; set; } = string.Empty;
    public List<HistoricalDateEvidence> BirthDateEvidence { get; set; } = [];
    public List<HistoricalDateEvidence> DeathDateEvidence { get; set; } = [];
    public List<HistoricalPlaceRecord> BirthPlaces { get; set; } = [];
    public List<HistoricalPlaceRecord> DeathPlaces { get; set; } = [];
    public List<HistoricalPlaceRecord> BurialPlaces { get; set; } = [];
    public List<WikipediaLeadRecord> WikipediaLeads { get; set; } = [];

    // Se è indicato un luogo specifico, il luogo amministrativo si ricava da quello.
    public int? BirthLuogoId { get; set; }
    public int? BirthLuogoSpecificoId { get; set; }
    public int? DeathLuogoId { get; set; }
    public int? DeathLuogoSpecificoId { get; set; }
    public int? BurialLuogoId { get; set; }
    public int? BurialLuogoSpecificoId { get; set; }

    [JsonIgnore]
    public Luogo? BirthLuogo { get; set; }

    [JsonIgnore]
    public Luogo? DeathLuogo { get; set; }

    [JsonIgnore]
    public Luogo? BurialLuogo { get; set; }

    [JsonIgnore]
    public LuogoSpecifico? BirthLuogoSpecifico { get; set; }

    [JsonIgnore]
    public LuogoSpecifico? DeathLuogoSpecifico { get; set; }

    [JsonIgnore]
    public LuogoSpecifico? BurialLuogoSpecifico { get; set; }

    public string DisplayName => ComposeName(Name, Surname, Suffix);

    // Ordine «Nome Cognome Suffisso», saltando le parti vuote.
    public static string ComposeName(string? name, string? surname, string? suffix)
        => string.Join(' ', new[] { name, surname, suffix }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    public string LifeSpan => $"{(string.IsNullOrWhiteSpace(Birth) ? "—" : Birth)}  /  {(string.IsNullOrWhiteSpace(Death) ? "—" : Death)}";
}