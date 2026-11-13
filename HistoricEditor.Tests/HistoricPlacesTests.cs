using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Località antiche come luoghi storici (2026-11-10, da Vespasiano e Tito) e riga di Wikidata nel confronto delle edizioni.
public class HistoricPlacesTests
{
    private static readonly Dictionary<string, List<string>> NoAliases = [];

    [Theory]
    [InlineData("Vicus Phalacrinae", true)]
    [InlineData("Aquae Cutiliae", true)]
    [InlineData("Forum Iulii", true)]
    [InlineData("Falacrine", false)]
    [InlineData("Terme di Cotilia", false)]
    [InlineData("Roma", false)]
    [InlineData("", false)]
    public void LooksLatin_RecognizesLatinPlaceNames(string text, bool expected)
        => Assert.Equal(expected, WikipediaImportService.LooksLatin(text));

    // Falacrine: nessuna etichetta italiana o latina su Wikidata; l'introduzione italiana dice «Falacrine»,
    // l'infobox «Vicus Phalacrinae».
    [Fact]
    public void Names_FromTheItalianArticle_WhenWikidataHasNone()
    {
        var labels = new Dictionary<string, string> { ["en"] = "Falacrine", ["de"] = "Falacrinae" };
        var (italian, latin, _) = WikipediaImportService.ChooseHistoricNames(labels, NoAliases, "Falacrine", "Vicus Phalacrinae", roman: true, "Falacrine");
        Assert.Equal(("Falacrine", "Vicus Phalacrinae"), (italian, latin));
    }

    // Senza infobox il latino viene da un'etichetta straniera con forma latina e simile al nome italiano.
    [Fact]
    public void Latin_FromAForeignLabel_WhenTheInfoboxHasNone()
    {
        var labels = new Dictionary<string, string> { ["en"] = "Falacrine", ["de"] = "Falacrinae" };
        Assert.Equal(("Falacrine", "Falacrinae", ""), WikipediaImportService.ChooseHistoricNames(labels, NoAliases, "Falacrine", string.Empty, roman: true, "Falacrine"));
    }

    // Aquae Cutiliae (2026-11-12): la località si chiama come nell'etichetta italiana, anche se è latina; l'alias
    // «Terme di Cotilia» è il nome dei resti ancora visibili, che diventano un luogo specifico al suo interno.
    [Fact]
    public void Italian_KeepsTheLatinLabel_AndTheBuildingAliasNamesTheRemains()
    {
        var labels = new Dictionary<string, string> { ["it"] = "Aquae Cutiliae", ["la"] = "Aquae Cutiliae" };
        var aliases = new Dictionary<string, List<string>> { ["it"] = ["Terme di Cotilia", "Cotilia"] };
        Assert.Equal(("Aquae Cutiliae", "", "Terme di Cotilia"), WikipediaImportService.ChooseHistoricNames(labels, aliases, "Aquae Cutiliae", "Aquae Cutiliae", roman: true, "Aquae Cutiliae"));
    }

    // Solo nomi di edifici: la località prende il resto del nome.
    [Fact]
    public void Italian_FromTheBuildingName_WhenThereIsNoOther()
    {
        var aliases = new Dictionary<string, List<string>> { ["it"] = ["Terme di Cotilia"] };
        Assert.Equal(("Cotilia", "", "Terme di Cotilia"), WikipediaImportService.ChooseHistoricNames(new Dictionary<string, string>(), aliases, string.Empty, string.Empty, roman: false, "Aquae Cutiliae"));
    }

    // «Terme di Cotilia»: la località è Cotilia (con la grafia dell'alias), le terme un luogo specifico di tipo «Terme».
    [Fact]
    public void BuildingName_SplitsSettlementAndBuilding()
    {
        Assert.Equal(("Cotilia", "Terme di Cotilia", "Terme"),
            WikipediaImportService.SplitBuildingName("Terme di Cotilia", ["Terme di Cotilia", "Cotilia", "Aquae Cutuliae"]));
        Assert.Equal(("Sperlonga", "Villa di Sperlonga", "Villa"), WikipediaImportService.SplitBuildingName("Villa di Sperlonga", []));
    }

    // Un nome senza edificio iniziale resta com'è.
    [Theory]
    [InlineData("Falacrine")]
    [InlineData("Cotilia")]
    [InlineData("Borgo di Mezzo")]
    public void BuildingName_LeavesOtherNames(string name)
        => Assert.Null(WikipediaImportService.SplitBuildingName(name, []));

    // Fuori dal mondo romano non si propone un nome latino.
    [Fact]
    public void Latin_OnlyForRomanPlaces()
    {
        var labels = new Dictionary<string, string> { ["it"] = "Ninive", ["la"] = "Nineve" };
        Assert.Equal(("Ninive", "", ""), WikipediaImportService.ChooseHistoricNames(labels, NoAliases, string.Empty, string.Empty, roman: false, "Ninive"));
    }

    // Il luogo storico sta nel comune attuale e si riusa al secondo import; il nome latino si aggiunge solo se manca.
    [Fact]
    public void Store_EnsureHistoricPlace_ReusesAndKeepsManualLatin()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-places-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var comune = store.EnsurePlace([new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lazio"), new GeoLevel("Provincia", "provincia di Rieti"), new GeoLevel("Comune", "Cittareale")], null, null);
            Assert.NotNull(comune);

            var id = store.EnsureHistoricPlace("Falacrine", "Vicus Phalacrinae", "Villaggio", comune, "Q927670", 42.59, 13.17);
            var luogo = store.GetPlace(id)!;
            Assert.True(luogo.Storico);
            Assert.Equal(comune, luogo.ParentId);
            Assert.Equal("Vicus Phalacrinae", HistoricPlaceLatin.Get(luogo));
            Assert.Equal("Falacrine, Cittareale, provincia di Rieti, Lazio, Italia", store.DescribePlace(id));

            store.SetPlaceLatinName(id, "Falacrinae");
            Assert.Equal(id, store.EnsureHistoricPlace("Falacrine", "Vicus Phalacrinae", "Villaggio", comune, "Q927670", null, null));
            Assert.Equal("Falacrinae", HistoricPlaceLatin.Get(store.GetPlace(id)!));

            store.SetPlaceLatinName(id, " ");
            Assert.Equal(string.Empty, HistoricPlaceLatin.Get(store.GetPlace(id)!));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    // Import precedente: luogo storico «Terme di Cotilia». Al reimport diventa «Cotilia» e le terme un luogo specifico
    // al suo interno; un nome corretto a mano non viene toccato.
    [Fact]
    public void Store_EnsureHistoricPlace_RenamesThePreviousImportName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-places-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var comune = store.EnsurePlace([new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lazio"), new GeoLevel("Provincia", "provincia di Rieti"), new GeoLevel("Comune", "Castel Sant’Angelo")], null, null);
            var id = store.EnsureHistoricPlace("Terme di Cotilia", "Aquae Cutiliae", "Sito archeologico", comune, "Q383071", 42.38, 13.0);

            Assert.Equal(id, store.EnsureHistoricPlace("Cotilia", "Aquae Cutiliae", "Sito archeologico", comune, "Q383071", 42.38, 13.0, formerName: "Terme di Cotilia"));
            Assert.Equal("Cotilia", store.GetPlace(id)!.Nome);
            var terme = store.EnsureSpecificPlace("Terme di Cotilia", id, "Q383071", 42.38, 13.0, "Terme");
            Assert.Equal(terme, store.EnsureSpecificPlace("Terme di Cotilia", id, "Q383071", null, null, "Terme"));

            var manual = store.EnsureHistoricPlace("Fondi antica", string.Empty, string.Empty, comune, "Q9", null, null);
            store.EnsureHistoricPlace("Altro", string.Empty, string.Empty, comune, "Q9", null, null, formerName: "Terme di Fondi");
            Assert.Equal("Fondi antica", store.GetPlace(manual)!.Nome);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    private static WikipediaLeadRecord Lead(string language, int? deathYear, int? deathMonth, int? deathDay)
        => new() { Language = language, LanguageName = language, BirthYear = 9, DeathYear = deathYear, DeathMonth = deathMonth, DeathDay = deathDay };

    // Le edizioni concordano, Wikidata no: il LED diventa arancione e la riga di Wikidata dice cosa cambia,
    // ma non conta tra le edizioni diverse.
    [Fact]
    public void Consensus_ShowsWikidataAndTurnsOrangeWhenItDiffers()
    {
        var wikidata = Lead(WikipediaLeadRecord.WikidataLanguage, 79, 6, 4);
        wikidata.LanguageName = "Wikidata";
        var result = WikipediaConsensus.Evaluate([Lead("it", 79, 6, 23), Lead("de", 79, 6, 23), Lead("fr", 79, 6, 23), wikidata]);

        Assert.Equal(ConsensusLevel.Orange, result.Level);
        Assert.Equal(0, result.DifferingCount);
        var row = Assert.Single(result.Editions, e => e.IsWikidata);
        Assert.Equal(["giorno di morte"], row.Differences);
        Assert.Contains("Wikidata", result.Summary);
    }

    [Fact]
    public void Consensus_OnlyWikidata_IsHidden()
    {
        var wikidata = Lead(WikipediaLeadRecord.WikidataLanguage, 79, 6, 4);
        Assert.Equal(ConsensusLevel.Hidden, WikipediaConsensus.Evaluate([wikidata]).Level);
    }
}
