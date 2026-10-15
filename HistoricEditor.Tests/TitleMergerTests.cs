using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class TitleMergerTests
{
    private static readonly Dictionary<string, HashSet<string>> NoSuperclasses = new(StringComparer.Ordinal);

    [Fact]
    public void Merge_CombinesSameEntityFromDifferentProperties()
    {
        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "re di Napoli", WikidataId = "Q21096118", Inizio = "1554" },
            new PersonTitle { Titolo = "re di Napoli", WikidataId = "Q21096118", Fine = "1598" },
        ], NoSuperclasses);

        var title = Assert.Single(titles);
        Assert.Equal("1554", title.Inizio);
        Assert.Equal("1598", title.Fine);
    }

    [Fact]
    public void Merge_KeepsSameTitleWithDifferentPeriods()
    {
        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "re", WikidataId = "Q1", Inizio = "1500", Fine = "1510" },
            new PersonTitle { Titolo = "re", WikidataId = "Q1", Inizio = "1520", Fine = "1530" },
        ], NoSuperclasses);

        Assert.Equal(2, titles.Count);
    }

    [Fact]
    public void Merge_DropsGenericSuperclassTitle()
    {
        var superclasses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Q111671302"] = ["Q58631963"],
        };

        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Monarca d'Aragona", WikidataId = "Q58631963", Inizio = "1556" },
            new PersonTitle { Titolo = "King of Aragon", WikidataId = "Q111671302" },
        ], superclasses);

        var title = Assert.Single(titles);
        Assert.Equal("Q111671302", title.WikidataId);
        Assert.Equal("1556", title.Inizio);
    }

    [Fact]
    public void Merge_CombinesLabelsDifferingOnlyByCaseAndAccents()
    {
        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Re di León", WikidataId = "Q1" },
            new PersonTitle { Titolo = "re di Leon", WikidataId = "Q2" },
        ], NoSuperclasses);

        Assert.Single(titles);
    }
}
