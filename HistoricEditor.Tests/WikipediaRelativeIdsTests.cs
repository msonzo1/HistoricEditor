using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class WikipediaRelativeIdsTests
{
    [Fact]
    public void ReadRelativeIds_KeepsEndedMarriages_AndAllSpouses()
    {
        // Maria Emanuela d'Aviz: matrimonio con Filippo II concluso (P582) alla sua morte.
        const string json = """
            {"P26":[
              {"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q34417"}}},"qualifiers":{"P582":[{}]}},
              {"rank":"preferred","mainsnak":{"datavalue":{"value":{"id":"Q1"}}}},
              {"rank":"deprecated","mainsnak":{"datavalue":{"value":{"id":"Q2"}}}}
            ]}
            """;
        using var document = JsonDocument.Parse(json);

        var ids = WikipediaImportService.ReadRelativeIds(document.RootElement, "P26");

        Assert.Equal(new[] { "Q34417", "Q1" }, ids);
    }

    [Theory]
    [InlineData(new[] { "Q8436" }, true)]                // "Mussolini family": famiglia generica
    [InlineData(new[] { "Q8436", "Q13417114" }, false)]  // anche famiglia nobile: resta dinastia
    [InlineData(new[] { "Q164950" }, false)]             // dinastia
    [InlineData(new string[0], false)]
    public void IsPlainFamily_DistinguishesGenericFamiliesFromDynasties(string[] classes, bool expected)
        => Assert.Equal(expected, WikipediaImportService.IsPlainFamily(classes));

    [Theory]
    [InlineData(new[] { "Q1156073", "Q3024240" }, false, true)] // "Spagna degli Asburgo": stato storico
    [InlineData(new[] { "Q164950" }, true, true)]                // con capitale: è uno stato
    [InlineData(new[] { "Q164950" }, false, false)]              // dinastia
    [InlineData(new[] { "Q8436" }, false, false)]                // famiglia
    public void IsNotAFamily_DiscardsStatesAndPeriods(string[] classes, bool hasCapital, bool expected)
        => Assert.Equal(expected, WikipediaImportService.IsNotAFamily(classes, hasCapital));
}
