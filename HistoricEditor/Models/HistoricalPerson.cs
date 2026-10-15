namespace HistoricEditor;

using System.Text.Json.Serialization;

public sealed class HistoricalPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Sesso { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string MainTitle { get; set; } = string.Empty;
    public List<PersonTitle> Titles { get; set; } = [];
    public List<LocalizedPersonName> LocalizedNames { get; set; } = [];

    // Un solo padre e una sola madre; i figli si ricavano da questi riferimenti.
    public int? FatherId { get; set; }
    public int? MotherId { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Father { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Mother { get; set; }
    public string Birth { get; set; } = string.Empty;
    public string Death { get; set; } = string.Empty;
    public string BirthPlace { get; set; } = string.Empty;
    public string DeathPlace { get; set; } = string.Empty;
    public string BurialPlace { get; set; } = string.Empty;
    public string WikipediaTitle { get; set; } = string.Empty;
    public string WikipediaUrl { get; set; } = string.Empty;
    public string WikipediaExtract { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
    public List<HistoricalDateEvidence> BirthDateEvidence { get; set; } = [];
    public List<HistoricalDateEvidence> DeathDateEvidence { get; set; } = [];
    public List<HistoricalPlaceRecord> BirthPlaces { get; set; } = [];
    public List<HistoricalPlaceRecord> DeathPlaces { get; set; } = [];
    public List<HistoricalPlaceRecord> BurialPlaces { get; set; } = [];

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

    public string DisplayName => string.IsNullOrWhiteSpace(Suffix) ? Name : $"{Name} {Suffix}";

    public string LifeSpan => $"{(string.IsNullOrWhiteSpace(Birth) ? "—" : Birth)}  /  {(string.IsNullOrWhiteSpace(Death) ? "—" : Death)}";
}