using System.IO;
using System.Text.Json;
using HistoricEditor;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HistoricEditor.Tests;

public class DuplicateTests
{
    private static void WithStore(Action<PeopleStore> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-dup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            test(store);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindsCertainDuplicatesByWikidataOrArticle()
        => WithStore(store =>
        {
            var enrico = new HistoricalPerson { Name = "Enrico VII", WikidataId = "Q129857", WikipediaTitle = "Enrico VII d'Inghilterra", Birth = "1457" };
            store.Add(enrico);

            var byQid = Assert.Single(store.FindPossibleDuplicates(new HistoricalPerson { Name = "Henry VII", WikidataId = "q129857" }));
            Assert.True(byQid.Certa);
            Assert.Equal(enrico.Id, byQid.Id);

            var byTitle = Assert.Single(store.FindPossibleDuplicates(new HistoricalPerson { WikipediaTitle = "Enrico VII d'Inghilterra" }));
            Assert.True(byTitle.Certa);

            // La scheda stessa non è un doppione di sé.
            Assert.Empty(store.FindPossibleDuplicates(enrico, enrico.Id));
        });

    [Fact]
    public void FindsProbableDuplicatesByNameWithCompatibleYears()
        => WithStore(store =>
        {
            store.Add(new HistoricalPerson { Name = "Carlo", Suffix = "V", Birth = "24 febbraio 1500", Death = "1558" });

            var match = Assert.Single(store.FindPossibleDuplicates(new HistoricalPerson { Name = "Càrlo V", Birth = "1500" }));
            Assert.False(match.Certa);
            Assert.Single(store.FindPossibleDuplicates(new HistoricalPerson { Name = "Carlo V" }));
            // Omonimo vissuto in un'altra epoca.
            Assert.Empty(store.FindPossibleDuplicates(new HistoricalPerson { Name = "Carlo V", Birth = "1338" }));
            Assert.Empty(store.FindPossibleDuplicates(new HistoricalPerson { Name = "Carlo VI" }));
        });

    [Fact]
    public void RejectsCertainDuplicatesOnAddAndUpdate()
        => WithStore(store =>
        {
            var carlo = new HistoricalPerson { Name = "Carlo V", WikidataId = "Q32500", WikipediaTitle = "Carlo V d'Asburgo", WikiTreeId = "Habsburg-105" };
            store.Add(carlo);

            Assert.Throws<InvalidOperationException>(() => store.Add(new HistoricalPerson { Name = "Carlo V", WikidataId = "q32500" }));
            Assert.Throws<InvalidOperationException>(() => store.Add(new HistoricalPerson { Name = "Carlo", WikipediaTitle = "Carlo V d'Asburgo" }));
            Assert.Throws<InvalidOperationException>(() => store.Add(new HistoricalPerson { Name = "Karl", WikiTreeId = "Habsburg-105" }));

            var altro = new HistoricalPerson { Name = "Filippo II" };
            store.Add(altro);
            altro.WikidataId = "Q32500";
            Assert.Throws<InvalidOperationException>(() => store.Update(altro));

            // La scheda stessa si può salvare di nuovo, e gli omonimi senza identificativi restano ammessi.
            store.Update(carlo);
            store.Add(new HistoricalPerson { Name = "Carlo V" });
            Assert.Equal(3, store.LoadAll().Count);
        });

    [Fact]
    public void FindPeopleByWikidataIds_IgnoresCase()
        => WithStore(store =>
        {
            var francesco = new HistoricalPerson { Name = "Francesco I", WikidataId = "Q129857" };
            store.Add(francesco);
            store.Add(new HistoricalPerson { Name = "Senza Q-id" });

            var found = store.FindPeopleByWikidataIds(["q129857", "Q1"]);
            Assert.Equal(francesco.Id, Assert.Single(found).Value);
            Assert.True(found.ContainsKey("Q129857"));
        });
}

public class EventImportMappingTests
{
    private static JsonElement Claims(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Time(string time, int precision) =>
        "{\"mainsnak\":{\"datavalue\":{\"value\":{\"time\":\"" + time + "\",\"precision\":" + precision + "}}}}";

    [Fact]
    public void ApiErrorPayload_BecomesReadableError()
    {
        var root = Claims("{\"error\":{\"code\":\"maxlag\",\"info\":\"Server sovraccarico\"}}");
        var ex = Assert.Throws<InvalidOperationException>(() => WikipediaImportService.ApiProperty(root, "query"));
        Assert.Contains("Server sovraccarico", ex.Message);
        Assert.Equal(JsonValueKind.Array, WikipediaImportService.ApiProperty(Claims("{\"query\":[]}"), "query").ValueKind);
    }

    [Fact]
    public void ReadsPointInTime()
    {
        var claims = Claims($$"""{"P585":[{{Time("+1525-02-24T00:00:00Z", 11)}}]}""");
        Assert.Equal((DataEvento.Esatta, "24/02/1525", ""), WikipediaImportService.ReadEventDates(claims));
    }

    [Fact]
    public void ReadsStartAndEndAsRange()
    {
        var claims = Claims($$"""{"P580":[{{Time("+1618-05-23T00:00:00Z", 11)}}],"P582":[{{Time("+1648-00-00T00:00:00Z", 9)}}]}""");
        Assert.Equal((DataEvento.DaA, "23/05/1618", "1648"), WikipediaImportService.ReadEventDates(claims));
    }

    [Fact]
    public void ReadsCircaAndUncertainBounds()
    {
        var circa = Claims("""{"P585":[{"mainsnak":{"datavalue":{"value":{"time":"-0753-00-00T00:00:00Z","precision":9}}},"qualifiers":{"P1480":[{"datavalue":{"value":{"id":"Q5727902"}}}]}}]}""");
        Assert.Equal((DataEvento.Circa, "753 a.C.", ""), WikipediaImportService.ReadEventDates(circa));

        var bounds = Claims($$"""{"P1319":[{{Time("+1450-00-00T00:00:00Z", 9)}}],"P1326":[{{Time("+1455-00-00T00:00:00Z", 9)}}]}""");
        Assert.Equal((DataEvento.Tra, "1450", "1455"), WikipediaImportService.ReadEventDates(bounds));
        Assert.Equal((DataEvento.Esatta, "", ""), WikipediaImportService.ReadEventDates(Claims("{}")));
    }

    [Fact]
    public void ReadsParticipantsCommandersAndSignatories()
    {
        var claims = Claims("""
            {"P710":[{"mainsnak":{"datavalue":{"value":{"id":"Q70972"}}},"qualifiers":{"P4791":[{"datavalue":{"value":{"id":"Q129857"}}}]}}],
             "P1891":[{"mainsnak":{"datavalue":{"value":{"id":"Q42"}}}},{"rank":"deprecated","mainsnak":{"datavalue":{"value":{"id":"Q43"}}}}]}
            """);
        var result = WikipediaImportService.ReadParticipantClaims(claims);
        Assert.Contains(("Q70972", "partecipante", ""), result);
        Assert.Contains(("Q129857", "comandante", "Q70972"), result);
        Assert.Contains(("Q42", "firmatario", ""), result);
        Assert.DoesNotContain(result, r => r.Id == "Q43");
    }

    [Theory]
    [InlineData("battaglia", "Battaglia")]
    [InlineData("trattato di pace", "Pace")]
    [InlineData("trattato internazionale", "Trattato")]
    [InlineData("colpo di Stato", "Congiura")]
    [InlineData("guerra di successione", "Guerra")]
    [InlineData("eclissi solare", "Eclissi solare")]
    [InlineData("", "")]
    public void MapsWikidataTypeToEventType(string label, string expected)
        => Assert.Equal(expected, WikipediaImportService.MapEventType([label]));

    [Fact]
    public void SummarizesFirstParagraphAtSentenceEnd()
    {
        Assert.Equal("Primo paragrafo.", WikipediaImportService.SummarizeExtract("Primo paragrafo.\nSecondo."));
        var longText = string.Concat(Enumerable.Repeat("Frase di prova abbastanza lunga. ", 40));
        var summary = WikipediaImportService.SummarizeExtract(longText);
        Assert.True(summary.Length <= 600);
        Assert.EndsWith(".", summary);
    }
}
