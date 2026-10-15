using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class LocalizedNameBuilderTests
{
    private static readonly (string Code, string Name)[] Languages = [("it", "italiano"), ("en", "inglese"), ("de", "tedesco")];

    [Fact]
    public void Build_SplitsNameAndSuffixPerLanguage()
    {
        var labels = new Dictionary<string, string> { ["it"] = "Carlo V d'Asburgo", ["en"] = "Charles V" };
        var mainTitle = new Dictionary<string, string> { ["it"] = "Imperatore del Sacro Romano Impero", ["en"] = "Holy Roman Emperor" };

        var result = LocalizedNameBuilder.Build(Languages, labels, [], mainTitle);

        Assert.Equal(2, result.Count);
        Assert.Equal(("Carlo V", "d'Asburgo", "Imperatore del Sacro Romano Impero"), (result[0].Nome, result[0].Suffisso, result[0].TitoloPrincipale));
        Assert.Equal(("en", "Charles V", "", "Holy Roman Emperor"), (result[1].Lingua, result[1].Nome, result[1].Suffisso, result[1].TitoloPrincipale));
    }

    [Fact]
    public void Build_UsesNicknameOfMatchingLanguage()
    {
        var labels = new Dictionary<string, string> { ["it"] = "Filippo il Bello", ["en"] = "Philip the Fair" };
        (string, string)[] nicknames = [("il Bello", "it"), ("the Fair", "en"), ("der Schöne", "de")];

        var result = LocalizedNameBuilder.Build(Languages, labels, nicknames, new Dictionary<string, string>());

        Assert.Equal(("Filippo", "il Bello"), (result[0].Nome, result[0].Soprannome));
        Assert.Equal(("Philip", "the Fair"), (result[1].Nome, result[1].Soprannome));
        Assert.Equal(("de", "", "der Schöne"), (result[2].Lingua, result[2].Nome, result[2].Soprannome));
    }

    [Fact]
    public void Build_SkipsLanguagesWithoutData()
    {
        var result = LocalizedNameBuilder.Build(Languages, new Dictionary<string, string> { ["it"] = "Dante" }, [], new Dictionary<string, string>());

        Assert.Single(result);
    }
}
