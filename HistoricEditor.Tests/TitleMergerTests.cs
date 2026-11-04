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
    public void Merge_GenericSuperclassFillsEverySpecificTitle()
    {
        var superclasses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Q58622349"] = ["Q58005590"],
            ["Q111671728"] = ["Q58005590"],
        };

        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Monarca di Castiglia e León", WikidataId = "Q58005590", Inizio = "1516", Fine = "26/01/1556" },
            new PersonTitle { Titolo = "Re di Castiglia", WikidataId = "Q58622349" },
            new PersonTitle { Titolo = "Re di León", WikidataId = "Q111671728" },
        ], superclasses);

        Assert.Equal(new[] { "Re di Castiglia", "Re di León" }, titles.Select(t => t.Titolo));
        Assert.All(titles, t => Assert.Equal(("1516", "26/01/1556"), (t.Inizio, t.Fine)));
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

    [Fact]
    public void Merge_CombinesSameTitleWithDifferentPrecision_KeepingMorePreciseDatesAndAllSources()
    {
        // Carlo Magno: «Imperatore» 25/12/800 → 28/01/814 (P39) e 800 → 814 (P97).
        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Imperatore", WikidataId = "Q181765", Inizio = "800", Fine = "814", FontiDate = [new() { Fonte = "P97" }] },
            new PersonTitle { Titolo = "Imperatore", WikidataId = "Q181765", Inizio = "25/12/800", Fine = "28/01/814", FontiDate = [new() { Fonte = "P39" }] },
        ], NoSuperclasses);

        var title = Assert.Single(titles);
        Assert.Equal("25/12/800", title.Inizio);
        Assert.Equal("28/01/814", title.Fine);
        Assert.Equal(new[] { "P97", "P39" }, title.FontiDate.Select(s => s.Fonte));
    }

    [Fact]
    public void Merge_CombinesOverlappingPeriodsWithDifferentDates()
    {
        var titles = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Re di Castiglia", WikidataId = "Q1", Inizio = "1516", Fine = "1556" },
            new PersonTitle { Titolo = "re di Castiglia", WikidataId = "Q2", Inizio = "1519", Fine = "1555" },
        ], NoSuperclasses);

        var title = Assert.Single(titles);
        Assert.Equal("1516", title.Inizio);
    }
}
