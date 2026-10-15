namespace HistoricEditor;

public sealed class HistoricalDateEvidence
{
    public string Value { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
    public string Precision { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
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
    public List<PlaceDivision> AdministrativeDivisions { get; set; } = [];
    // Divisioni lette dall'infobox della pagina it.wikipedia del comune (grado 1, grado 2…); "no" = livello assente.
    public List<string> InfoboxDivisions { get; set; } = [];
    public List<HistoricalPlaceContext> HistoricalContexts { get; set; } = [];
    public List<SourceCitation> Sources { get; set; } = [];
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
    public string Extract { get; init; } = string.Empty;
    public List<HistoricalDateEvidence> BirthDates { get; init; } = [];
    public List<HistoricalDateEvidence> DeathDates { get; init; } = [];
    public List<HistoricalPlaceRecord> BirthPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> DeathPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> BurialPlaces { get; init; } = [];

    // Identificativi Wikidata dei parenti (P22 padre, P25 madre, P26 coniugi, P40 figli).
    public List<string> FatherIds { get; init; } = [];
    public List<string> MotherIds { get; init; } = [];
    public List<string> SpouseIds { get; init; } = [];
    public List<string> ChildIds { get; init; } = [];
}
