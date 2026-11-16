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

    // Data letta da una sola fonte e diversa da quelle scelte (Cesare: 102 a.C. da un solo riferimento Wikidata, contro
    // il 100 a.C. di tutte le altre fonti): non entra nel campo della data, ma resta tra le fonti come data discordante.
    public bool Scartata { get; set; }
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

    // Località antica o scomparsa (Falacrine, Aquae Cutiliae): nome italiano con cui diventa un luogo storico
    // nell'archivio, contenuto nel comune attuale. Vuoto per i luoghi attuali. Come i nomi storici, vale solo all'import.
    [System.Text.Json.Serialization.JsonIgnore]
    public string HistoricName { get; set; } = string.Empty;

    // Nome latino del luogo storico, solo per i luoghi del mondo romano («Vicus Phalacrinae»).
    [System.Text.Json.Serialization.JsonIgnore]
    public string LatinName { get; set; } = string.Empty;

    // Edificio della località antica, ancora visibile, che diventa un luogo specifico dentro il luogo storico
    // («Terme di Cotilia», tipo «Terme», dentro Cotilia). Vuoto se il nome della località non indica un edificio.
    [System.Text.Json.Serialization.JsonIgnore]
    public string HistoricBuildingName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string HistoricBuildingType { get; set; } = string.Empty;

    // Condizione del luogo specifico creato all'import (CondizioniLuogo): «In rovina» per i resti ancora visibili
    // (Tempio del Divo Giulio, Terme di Cotilia, Sito archeologico di Italica). Vuoto = esistente.
    [System.Text.Json.Serialization.JsonIgnore]
    public string SpecificCondition { get; set; } = string.Empty;
}

public sealed class PlaceDivision
{
    // Comune attuale più vicino, trovato dalle coordinate per un luogo che su Wikidata non indica il comune
    // (località scomparse come Falacrine, oggi nel territorio di Cittareale). È una proposta da confermare.
    public const string NearbyMunicipalityRelation = "Unità amministrativa (comune più vicino, dalle coordinate)";


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
    // Lingua della riga con i dati di Wikidata, salvata insieme alle edizioni per mostrarla nel confronto.
    public const string WikidataLanguage = "wikidata";

    public string Language { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWikidata => Language == WikidataLanguage;

    public string LanguageName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

    // Secondo giorno indicato dall'edizione: «mort le 8 ou 9 août 117» → giorno 9, alternativo 8.
    public int? BirthDayAlt { get; set; }
    public int? DeathYear { get; set; }
    public int? DeathMonth { get; set; }
    public int? DeathDay { get; set; }
    public int? DeathDayAlt { get; set; }
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

// Famiglia letta da Wikidata con l'eventuale dinastia di cui è ramo; Gens = gens romana (P5025). NomeLatino e
// ParentNomeLatino: nomi latini (etichetta latina, altrimenti inglese con J → I), vuoti se non si trovano.
public sealed record ImportedDynasty(string Nome, string WikidataId, string ParentNome, string ParentWikidataId, bool Gens = false, string NomeLatino = "", string ParentNomeLatino = "");

// Gens letta da Wikidata (2026-11-15): forme latine come sono scritte (nome «Iulia», nomen «Iulius», plurale «Iulii»),
// italiane se diverse, ed eventuale ramo. Le forme che non si trovano restano vuote, da completare a mano.
public sealed record ImportedGens(string WikidataId, string Nome, string Nomen = "", string Plurale = "", string NomeItaliano = "", string PluraleItaliano = "", ImportedGensBranch? Ramo = null)
{
    // Come gens e ramo dell'archivio, senza salvarli, per le etichette («Gens Giulia › Giulii Cesari»).
    public (GensRomana Gens, RamoGens? Ramo) ToEntities()
    {
        var gens = new GensRomana { Nome = Nome, Nomen = Nomen, Plurale = Plurale, NomeItaliano = NomeItaliano, PluraleItaliano = PluraleItaliano, WikidataId = WikidataId };
        var ramo = Ramo is { } r ? new RamoGens { Gens = gens, Cognomen = r.Cognomen, Plurale = r.Plurale, NomeItaliano = r.NomeItaliano, WikidataId = r.WikidataId } : null;
        return (gens, ramo);
    }

    public string Path
    {
        get
        {
            var (gens, ramo) = ToEntities();
            return GensLabels.Path(gens, ramo);
        }
    }
}

// Ramo di una gens letto da Wikidata: plurale («Caesares») dall'etichetta latina del ramo («Iulii Caesares»), italiano se
// diverso, cognomen singolare se scritto da qualche parte.
public sealed record ImportedGensBranch(string WikidataId, string Plurale, string NomeItaliano = "", string Cognomen = "");

public sealed class WikipediaImportResult
{
    public string Name { get; init; } = string.Empty;
    public string Surname { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string MainTitle { get; init; } = string.Empty;
    public List<PersonTitle> Titles { get; init; } = [];
    public List<LocalizedPersonName> LocalizedNames { get; init; } = [];

    // Nome romano da Wikidata: praenomen (P2358), nomen (P2359), cognomen (P2365), agnomen (P2366)
    // e nome latino (P1559 in latino) solo se diverso da quello composto dalle parti.
    public string Praenomen { get; init; } = string.Empty;
    public string Nomen { get; init; } = string.Empty;
    public string Cognomen { get; init; } = string.Empty;
    public string Agnomen { get; init; } = string.Empty;
    public string NomeLatino { get; init; } = string.Empty;

    // Le stesse parti in italiano, dalle etichette italiane degli stessi elementi Wikidata; le altre lingue sono in LocalizedNames.
    public string Prenome { get; init; } = string.Empty;
    public string Gentilizio { get; init; } = string.Empty;
    public string CognomeRomano { get; init; } = string.Empty;
    public string Agnome { get; init; } = string.Empty;

    // Nomi nel tempo dall'introduzione italiana (nascita, cambi di nome e, principale, il nome iniziale della voce);
    // vuoto se l'introduzione non ne indica: allora il nome è quello delle parti qui sopra.
    public List<RomanNameRecord> NomiNelTempo { get; init; } = [];
    public string Family { get; init; } = string.Empty;

    // Gens di nascita ed eventuale ramo (Gens Ulpia, ramo Traiani), separati dalle dinastie del campo Family.
    public ImportedGens? GensNascita { get; init; }

    // Gens d'adozione (P5025 con causa «adozione»), separata da quella di nascita.
    public ImportedGens? GensAdottiva { get; init; }

    // Etichette ricavate: «Gens Ulpia › Ulpii Traiani».
    public string Gens => GensNascita?.Path ?? string.Empty;

    public string GensAdozione => GensAdottiva?.Path ?? string.Empty;
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

    // Date diverse da quelle scelte e indicate da una sola fonte: non vanno nel campo, ma tra le fonti come discordanti.
    public List<HistoricalDateEvidence> DiscardedBirthDates { get; init; } = [];
    public List<HistoricalDateEvidence> DiscardedDeathDates { get; init; } = [];

    // Tutte le date lette, con quelle scartate (segnate con Scartata), come si salvano nella scheda.
    public IEnumerable<HistoricalDateEvidence> AllBirthEvidence => BirthDates.Concat(DiscardedBirthDates);
    public IEnumerable<HistoricalDateEvidence> AllDeathEvidence => DeathDates.Concat(DiscardedDeathDates);
    public List<HistoricalPlaceRecord> BirthPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> DeathPlaces { get; init; } = [];
    public List<HistoricalPlaceRecord> BurialPlaces { get; init; } = [];
    public List<WikipediaLeadRecord> WikipediaLeads { get; init; } = [];

    // Edizioni di Wikipedia (nomi delle lingue) che non è stato possibile leggere, ad esempio per troppe richieste.
    public List<string> UnreadEditions { get; init; } = [];

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
