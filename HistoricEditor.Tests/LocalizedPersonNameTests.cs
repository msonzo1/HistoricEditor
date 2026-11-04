using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class LocalizedPersonNameTests
{
    [Fact]
    public void MissingLanguages_ExcludesPresentOnes()
    {
        var existing = new[]
        {
            new LocalizedPersonName { Lingua = "en", LinguaNome = "inglese", Nome = "Charles" },
            new LocalizedPersonName { Lingua = "DE", LinguaNome = "tedesco", Nome = "Karl" },
        };

        var missing = LocalizedPersonName.MissingLanguages(existing).Select(l => l.Code);

        Assert.Equal(new[] { "fr", "es", "nl" }, missing);
    }

    [Fact]
    public void MissingLanguages_NeverOffersItalian()
    {
        Assert.DoesNotContain(LocalizedPersonName.MissingLanguages([]), l => l.Code == "it");
    }

    [Fact]
    public void Normalize_TrimsValues_AndDropsEmptyLanguages()
    {
        var names = new[]
        {
            new LocalizedPersonName { Lingua = "en", LinguaNome = "inglese", Nome = "  Charles ", Suffisso = " V " },
            new LocalizedPersonName { Lingua = "fr", LinguaNome = "francese", Nome = "   " },
        };

        var result = LocalizedPersonName.Normalize(names);

        var only = Assert.Single(result);
        Assert.Equal("en", only.Lingua);
        Assert.Equal("Charles", only.Nome);
        Assert.Equal("V", only.Suffisso);
    }

    [Fact]
    public void Normalize_DoesNotModifyOriginals()
    {
        var original = new LocalizedPersonName { Lingua = "en", Nome = " Charles " };

        LocalizedPersonName.Normalize([original]);

        Assert.Equal(" Charles ", original.Nome);
    }
}
