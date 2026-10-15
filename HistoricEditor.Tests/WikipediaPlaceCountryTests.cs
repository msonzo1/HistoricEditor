using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class WikipediaPlaceCountryTests
{
    private static JsonElement Entity(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Label(string language, string value)
        => "\"labels\":{\"" + language + "\":{\"language\":\"" + language + "\",\"value\":\"" + value + "\"}}";

    private static string Claim(string property, string targetId)
        => "\"" + property + "\":[{\"rank\":\"normal\",\"mainsnak\":{\"snaktype\":\"value\",\"datavalue\":{\"value\":{\"entity-type\":\"item\",\"id\":\"" + targetId + "\"}}}}]";

    private const string Dissolution = "\"P576\":[{\"rank\":\"normal\",\"mainsnak\":{\"snaktype\":\"value\",\"datavalue\":{\"value\":{\"time\":\"+1715-01-01T00:00:00Z\"}}}}]";

    [Fact]
    public void BuildPlaces_PrefersCurrentCountryOverDissolvedState()
    {
        var entities = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["Q120227728"] = Entity("{" + Label("en", "Cuarto bajo del Rey") + ",\"claims\":{" + Claim("P131", "Q2807") + "," + Claim("P17", "Q217196") + "}}"),
            ["Q2807"] = Entity("{" + Label("it", "Madrid") + ",\"claims\":{" + Claim("P17", "Q29") + "}}"),
            ["Q217196"] = Entity("{" + Label("it", "Regno di Castiglia") + ",\"claims\":{" + Dissolution + "}}"),
            ["Q29"] = Entity("{" + Label("it", "Spagna") + ",\"claims\":{}}"),
        };

        var places = WikipediaImportService.BuildPlaces([new WikipediaImportService.EntityStatement("Q120227728", [])], entities);

        Assert.Equal("Spagna", Assert.Single(places).CurrentCountry);
    }
}
