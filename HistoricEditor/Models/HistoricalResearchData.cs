namespace HistoricEditor;

public sealed class HistoricalDateEvidence
{
    public string Value { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
    public string Precision { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    // "giuliano" se Wikidata indica il calendario giuliano (Q1985786), altrimenti vuoto.
    public string Calendario { get; set; } = string.Empty;
    public Dictionary<string, List<string>> Qualifiers { get; set; } = [];
    public List<SourceCitation> Sources { get; set; } = [];
}

public sealed class HistoricalPlaceRecord
{
    public string WikidataId { get; set; } = string.Empty;
    public string CurrentName { get; set; } = string.Empty;
    public string CurrentCountry { get; set; } = string.Empty;
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }
    public List<PlaceName> Names { get; set; } = [];
    // Tipi Wikidata (P31) del luogo.
    public List<string> TypeIds { get; set; } = [];
    // Tipo proposto per il luogo specifico (es. «Frazione», «Monastero»), ricavato da TypeIds all'import.
    public string TypeLabel { get; set; } = string.Empty;
    public List<PlaceDivision> AdministrativeDivisions { get; set; } = [];
    // Divisioni lette dall'infobox della pagina it.wikipedia del comune (grado 1, grado 2…); "no" = livello assente.
    public List<string> InfoboxDivisions { get; set; } = [];
    public List<HistoricalPlaceContext> HistoricalContexts { get; set; } = [];
    public List<SourceCitation> Sources { get; set; } = [];

    // Nomi storici letti da Wikidata all'import: passano al luogo dell'archivio e non sono salvati nella scheda.
    [System.Text.Json.Serialization.JsonIgnore]
    public List<NomeStorico> HistoricalNames { get; set; } = [];
}

public sealed class PlaceDivision
{
    public string Relation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PlaceName> Names { get; set; } = [];
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }
    public string WikidataId { get; set; } = string.Empty;
    public string SubjectWikidataId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    // Tipi Wikidata (P31) della suddivisione.
    public List<string> TypeIds { get; set; } = [];
}

public sealed class HistoricalPlaceContext
{
    public string NameAtTime { get; set; } = string.Empty;
    public string PoliticalEntity { get; set; } = string.Empty;
    public string AdministrativeUnit { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<SourceCitation> Sources { get; set; } = [];
}

public sealed class SourceCitation
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RetrievedAt { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
}

// Anni e luoghi di nascita/morte letti dall'introduzione di un'edizione linguistica di Wikipedia.
// Le chiavi dei luoghi sono il Q-id Wikidata riconosciuto o, in mancanza, il testo normalizzato.
public sealed class WikipediaLeadRecord
{
    public string Language { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }
    public int? DeathYear { get; set; }
    public int? DeathMonth { get; set; }
    public int? DeathDay { get; set; }
    public string BirthPlace { get; set; } = string.Empty;
    public string BirthPlaceKey { get; set; } = string.Empty;
    public string DeathPlace { get; set; } = string.Empty;
    public string DeathPlaceKey { get; set; } = string.Empty;

    public WikipediaLeadRecord Clone() => (WikipediaLeadRecord)MemberwiseClone();
}

public sealed class WikipediaSearchResult
{
    public string Title { get; init; } = string.Empty;
    public int PageId { get; init; }
    public string Snippet { get; init; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Snippet) ? Title : $"{Title} — {Snippet}";
}

// Famiglia letta da Wikidata con l'eventuale dinastia di cui è ramo.
public sealed record ImportedDynasty(string Nome, string WikidataId, string ParentNome, string ParentWikidataId);

public sealed class WikipediaImportResult
{
    public string Name { get; init; } = string.Empty;
    public string Surname { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string MainTitle { get; init; } = string.Empty;
    public List<PersonTitle> Titles { get; init; } = [];
    public List<LocalizedPersonName> LocalizedNames { get; init; } = [];
    public string Family { get; init; } = string.Empty;
    public List<ImportedDynasty> Dynasties { get; init; } = [];
    public string Sesso { get; init; } = string.Empty;
    public string ArticleTitle { get; init; } = string.Empty;
    public string ArticleUrl { get; init; } = string.Empty;
    public string WikidataId { get; init; } = string.Empty;

    // Profilo WikiTree indicato da Wikidata (P2949).
    public string WikiTreeId { get; init; } = string.Empty;
    public string Extract { get; init; } = string.Empty;
    public List<HistoricalDateEvidence> BirthDates { get; init; } = [];
    public List<HistoricalDateEvidence> DeathDates { get; init; } = [];
    public List<HistoricalPlaceRecord> BirthPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> DeathPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> BurialPlaces { get; init; } = [];
    public List<WikipediaLeadRecord> WikipediaLeads { get; init; } = [];

    // Identificativi Wikidata dei parenti (P22 padre, P25 madre, P26 coniugi, P40 figli).
    public List<string> FatherIds { get; init; } = [];
    public List<string> MotherIds { get; init; } = [];
    public List<string> SpouseIds { get; init; } = [];
    public List<string> ChildIds { get; init; } = [];

    // Relazioni non familiari lette da Wikidata (maestri, allievi, influenze).
    public List<ImportedRelation> Relations { get; init; } = [];

    // Eventi del personaggio (P793 evento significativo, P1344 partecipò a, P607 conflitto).
    public List<string> EventIds { get; init; } = [];
}

// Persona Wikidata non ancora in archivio, proposta per un import completo.
public sealed record WikidataPersonSummary(string WikidataId, string Nome, string Descrizione, string ItalianTitle);

// Attivo: il personaggio importato è la parte attiva della relazione (es. il maestro).
public sealed record ImportedRelation(string WikidataId, string Tipo, bool Attivo);
