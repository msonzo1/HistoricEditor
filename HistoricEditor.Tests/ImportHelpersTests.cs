using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Regole introdotte con l'import di Vespasiano (2026-11-10).
public class ImportHelpersTests
{
    [Theory]
    [InlineData("Falacrine Italie| date de décès = {{date de décès|23|juin|79}} - [[Rome]]", "Falacrine Italie")]
    [InlineData(" [[Gand|Gent]], {{Belgique}}", " [[Gand|Gent]], {{Belgique}}")]
    [InlineData("[[Roma]] {{nowrap|(Lazio)}} | morte = 79", "[[Roma]] {{nowrap|(Lazio)}} ")]
    public void UntilNextParam_StopsAtTopLevelPipe(string value, string expected)
    {
        Assert.Equal(expected, WikipediaImportService.UntilNextParam(value));
    }

    [Fact]
    public void ExtractInfoboxPlaces_HandlesParamsOnTheSameLine()
    {
        const string wikitext = " | date de naissance = {{date|17|novembre|9}}\n |lieu de naissance=Falacrine Italie| date de décès     = {{date de décès|23|juin|79}} (à 69 ans) - [[Rome]]\n |lieu de décès=Italie| père = X";

        Assert.Equal(("Falacrine Italie", "Italie"), WikipediaImportService.ExtractInfoboxPlaces(wikitext));
    }

    [Theory]
    [InlineData("Giulio", "Iulius")]
    [InlineData("Cesare", "Caesar")]
    [InlineData("Ottaviano", "Octavianus")]
    [InlineData("Massimo", "Maximus")]
    [InlineData("Febo", "Phoebus")]
    [InlineData("Tito", "Titus")]
    [InlineData("Julius", "Iulius")]
    public void SimilarRomanPart_AcceptsItalianAdaptations(string local, string latin)
    {
        Assert.True(WikipediaImportService.SimilarRomanPart(local, latin));
    }

    [Theory]
    [InlineData("Cesare", "Vespasianus")]
    [InlineData("Augusto", "Flavius")]
    [InlineData("Gaio", "Iulius")]
    public void SimilarRomanPart_RejectsDifferentNames(string local, string latin)
    {
        Assert.False(WikipediaImportService.SimilarRomanPart(local, latin));
    }

    [Fact]
    public void LocalRomanParts_ReadsFullNameFromIntroduction_WhenLabelsAreLatin()
    {
        const string intro = "Cesare Vespasiano Augusto (in latino Caesar Vespasianus Augustus; Falacrine, 17 novembre 9 – Aquae Cutiliae, 23 giugno 79) è stato un imperatore romano.\nNato Tito Flavio Vespasiano (Titus Flavius Vespasianus) e meglio conosciuto come Vespasiano.";
        var latin = new WikipediaImportService.RomanParts("Titus", "Flavius", "", "Vespasianus");

        var parts = WikipediaImportService.LocalRomanParts("Vespasiano", [], latin, latin, intro);

        Assert.Equal(new WikipediaImportService.RomanParts("Tito", "Flavio", "", "Vespasiano"), parts);
    }

    [Fact]
    public void LocalRomanParts_KeepsTranslatedLabels_OverLatinInIntroduction()
    {
        const string intro = "Cayo o Gayo Julio César (en latín: Gaius Iulius Caesar; 12 o 13 de julio de 100 a. C.-15 de marzo de 44 a. C.) fue un político.";
        var latin = new WikipediaImportService.RomanParts("Gaius", "Iulius", "Caesar", "");
        var labels = new WikipediaImportService.RomanParts("Gayo", "Julio", "César", "");

        Assert.Equal(labels, WikipediaImportService.LocalRomanParts("Julio César", [], latin, labels, intro));
    }

    [Fact]
    public void LocalRomanParts_IgnoresIntroductionWithoutFullMatch()
    {
        const string intro = "Tito Cesare Vespasiano Augusto (Roma, 30 dicembre 39 – Aquae Cutiliae, 13 settembre 81) è stato un imperatore.";
        var latin = new WikipediaImportService.RomanParts("Titus", "Flavius", "Vespasianus", "");

        Assert.Equal(latin, WikipediaImportService.LocalRomanParts("Tito", [], latin, latin, intro));
    }

    [Fact]
    public void ReadRomanPartIds_SkipsDisputedAndDeprecated()
    {
        using var claims = JsonDocument.Parse("""
            {"P2365":[
              {"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q61042185"}}},"qualifiers":{"P5102":[{"datavalue":{"value":{"id":"Q18912752"}}}]}},
              {"rank":"deprecated","mainsnak":{"datavalue":{"value":{"id":"Q2"}}}},
              {"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q3"}}},"qualifiers":{"P5102":[{"datavalue":{"value":{"id":"Q99"}}}]}}
            ]}
            """);

        Assert.Equal(["Q3"], WikipediaImportService.ReadRomanPartIds(claims.RootElement, "P2365"));
        Assert.Empty(WikipediaImportService.ReadRomanPartIds(claims.RootElement, "P2366"));
    }

    [Fact]
    public void ReadRomanNameParts_AgnomenBecomesCognomen_WhenCognomenIsMissing()
    {
        // Vespasiano: cognomen «Sabinus» contestato, «Vespasianus» come agnomen.
        using var claims = JsonDocument.Parse("""
            {"P2365":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q61042185"}}},"qualifiers":{"P5102":[{"datavalue":{"value":{"id":"Q18912752"}}}]}}],
             "P2366":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q107607683"}}}}]}
            """);

        var parts = WikipediaImportService.ReadRomanNameParts(claims.RootElement);

        Assert.Equal(["Q107607683"], parts["P2365"]);
        Assert.Empty(parts["P2366"]);
    }

    [Fact]
    public void ReadRomanNameParts_KeepsAgnomen_WhenCognomenIsPresent()
    {
        // Scipione Africano: cognomen Scipio, agnomen Africanus.
        using var claims = JsonDocument.Parse("""
            {"P2365":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q10"}}}}],
             "P2366":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q11"}}}}]}
            """);

        var parts = WikipediaImportService.ReadRomanNameParts(claims.RootElement);

        Assert.Equal(["Q10"], parts["P2365"]);
        Assert.Equal(["Q11"], parts["P2366"]);
    }

    [Fact]
    public void NearbyMunicipality_QueryAndResult()
    {
        var query = WikipediaImportService.NearbyMunicipalityQuery(42.59361111, 13.17055556, ["Q954172", "Q747074"]);
        Assert.Contains("Point(13.17055556 42.59361111)", query);
        Assert.Contains("VALUES ?type { wd:Q747074 wd:Q954172 }", query);

        using var empty = JsonDocument.Parse("""{"results":{"bindings":[]}}""");
        using var found = JsonDocument.Parse("""{"results":{"bindings":[{"item":{"type":"uri","value":"http://www.wikidata.org/entity/Q224043"},"dist":{"value":"2.7"}}]}}""");
        Assert.Null(WikipediaImportService.ParseNearbyMunicipality(empty.RootElement));
        Assert.Equal("Q224043", WikipediaImportService.ParseNearbyMunicipality(found.RootElement));
    }

    [Fact]
    public void HasMunicipality_LooksAtPlaceAndAdministrativeParents()
    {
        var types = new HashSet<string> { "Q747074" };
        var falacrine = new HistoricalPlaceRecord
        {
            TypeIds = ["Q532"],
            AdministrativeDivisions = [new PlaceDivision { Relation = "Unità amministrativa (P131)", TypeIds = ["Q16110"] }],
        };
        var cutiliae = new HistoricalPlaceRecord
        {
            TypeIds = ["Q839954"],
            AdministrativeDivisions = [new PlaceDivision { Relation = "Unità amministrativa (P131)", TypeIds = ["Q747074"] }],
        };

        Assert.False(WikipediaImportService.HasMunicipality(falacrine, types));
        Assert.True(WikipediaImportService.HasMunicipality(cutiliae, types));
        Assert.True(WikipediaImportService.HasMunicipality(new HistoricalPlaceRecord { TypeIds = ["Q747074"] }, types));
    }
}
