using Xunit;

namespace HistoricEditor.Tests;

public class WikipediaConsensusTests
{
    private static readonly LeadPlaceCandidate Anchiano = new("Q1", ["Anchiano"], ["Vinci"]);
    private static readonly LeadPlaceCandidate Amboise = new("Q2", ["Amboise"], []);
    private static readonly LeadPlaceCandidate Longwood = new("Q3", ["Longwood House"], ["Longwood", "Sint-Helena", "Sainte-Hélène"]);

    private static WikipediaLeadRecord Lead(string language, int? birth, int? death, string birthKey = "", string deathKey = "")
        => new() { Language = language, LanguageName = language, BirthYear = birth, DeathYear = death, BirthPlaceKey = birthKey, DeathPlaceKey = deathKey };

    [Theory]
    [InlineData("Anchiano", "Q1")]
    [InlineData("Anchiano bei Vinci", "Q1")]
    [InlineData("Vinci", "Q1")]
    [InlineData("Amboise (Touraine)", "Q2")]
    [InlineData("Longwood sur l’île de Sainte-Hélène", "Q3")]
    [InlineData("Sint-Helena", "Q3")]
    [InlineData("Firenze, Toscana", "firenze")]
    [InlineData("Vinci, Toscana", "Q1")]
    [InlineData("Firenze, vicino a Vinci", "firenze")]
    [InlineData("Schloss Clos Lucé, Amboise, Frankreich", "Q2")]
    [InlineData("", "")]
    public void PlaceKey_RecognisesWikidataPlaceOrNormalisesText(string text, string expected)
    {
        Assert.Equal(expected, WikipediaConsensus.PlaceKey(text, [Anchiano, Amboise, Longwood]));
    }

    [Fact]
    public void AllEditionsAgree_IsGreenAndListsSources()
    {
        var result = WikipediaConsensus.Evaluate([Lead("italiano", 1452, 1519, "Q1", "Q2"), Lead("inglese", 1452, 1519), Lead("tedesco", 1452, 1519, "Q1", "Q2")]);

        Assert.Equal(ConsensusLevel.Green, result.Level);
        Assert.Contains("Wikipedia in italiano, Wikipedia in inglese, Wikipedia in tedesco", result.Details);
    }

    [Fact]
    public void DifferencesAreCountedAgainstTheMajority()
    {
        var orange = WikipediaConsensus.Evaluate([Lead("it", 1452, 1519), Lead("en", 1452, 1519), Lead("de", 1453, 1519), Lead("fr", 1452, 1519, "Q1")]);
        Assert.Equal(ConsensusLevel.Orange, orange.Level);
        Assert.Equal(1, orange.DifferingCount);

        var red = WikipediaConsensus.Evaluate([
            Lead("it", 1452, 1519, "Q1"), Lead("en", 1452, 1519, "Q1"), Lead("de", 1452, 1519, "Q1"),
            Lead("fr", 1453, 1519), Lead("es", 1452, 1520), Lead("nl", 1452, 1519, "vinci"),
        ]);
        Assert.Equal(ConsensusLevel.Red, red.Level);
        Assert.Equal(3, red.DifferingCount);
    }

    [Fact]
    public void Editions_ListEachLeadWithLinkAndDifferences()
    {
        var de = Lead("de", 1453, 1519);
        de.LanguageName = "tedesco";
        de.Title = "Leonardo da Vinci";
        de.Url = "https://de.wikipedia.org/wiki/Leonardo_da_Vinci";
        var fr = Lead("fr", 1452, 1519);
        fr.Title = "Léonard de Vinci";

        var result = WikipediaConsensus.Evaluate([Lead("it", 1452, 1519), Lead("en", 1452, 1519), de, fr]);

        Assert.Equal(4, result.Editions.Count);
        var german = result.Editions[2];
        Assert.False(german.Agrees);
        Assert.Equal("https://de.wikipedia.org/wiki/Leonardo_da_Vinci", german.Url);
        Assert.Equal(["anno di nascita"], german.Differences);
        var french = result.Editions[3];
        Assert.True(french.Agrees);
        Assert.Equal("https://fr.wikipedia.org/wiki/L%C3%A9onard_de_Vinci", french.Url);
        Assert.Equal(string.Empty, result.Editions[0].Url);
    }

    [Fact]
    public void DifferentDayInTheSameYear_IsReported()
    {
        static WikipediaLeadRecord Dated(string language, int? month, int? day, string birthPlace = "Vinci")
            => new() { Language = language, LanguageName = language, BirthYear = 1452, BirthMonth = month, BirthDay = day, BirthPlace = birthPlace, DeathYear = 1519, DeathMonth = 5, DeathDay = 2 };

        var result = WikipediaConsensus.Evaluate([Dated("it", 4, 15), Dated("en", 4, 15), Dated("fr", 4, 14), Dated("de", 4, null), Dated("nl", null, null, "")]);

        Assert.Equal(ConsensusLevel.Orange, result.Level);
        Assert.Equal(1, result.DifferingCount);
        Assert.Contains("✗ fr: nascita 14 aprile 1452, Vinci", result.Details);
        Assert.Contains("(diverso: giorno di nascita)", result.Details);
        Assert.Contains("✓ de: nascita aprile 1452, Vinci", result.Details);
        Assert.Contains("✓ nl: nascita 1452, luogo non indicato", result.Details);
    }

    [Fact]
    public void NothingToCompare_IsHidden()
    {
        Assert.Equal(ConsensusLevel.Hidden, WikipediaConsensus.Evaluate([]).Level);
        Assert.Equal(ConsensusLevel.Hidden, WikipediaConsensus.Evaluate([Lead("it", 1452, 1519, "Q1", "Q2"), Lead("en", null, null)]).Level);
    }
}
