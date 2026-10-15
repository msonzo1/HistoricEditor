using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class PeopleGroupingTests
{
    private static HistoricalPerson Person(int id, string name, string family, string birth = "", string death = "")
        => new() { Id = id, Name = name, Family = family, Birth = birth, Death = death };

    [Fact]
    public void Build_GroupsByFamilyIgnoringCaseAndSpaces()
    {
        var groups = PeopleGrouping.Build(
        [
            Person(1, "Rodolfo", "Asburgo"),
            Person(2, "Massimiliano", " asburgo "),
            Person(3, "Cosimo", "Medici"),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Asburgo", groups[0].Famiglia);
        Assert.Equal(2, groups[0].Persone.Count);
        Assert.Equal("2 PERSONE", groups[0].Conteggio);
        Assert.Equal("1 PERSONA", groups[1].Conteggio);
    }

    [Fact]
    public void Build_SortsGroupsAndPeople_WithoutFamilyLast()
    {
        var groups = PeopleGrouping.Build(
        [
            Person(1, "Zeno", ""),
            Person(2, "Lorenzo", "Medici"),
            Person(3, "Carlo", "Asburgo"),
            Person(4, "Anna", "Medici"),
        ]);

        Assert.Equal(["Asburgo", "Medici", PeopleGrouping.NoFamily], groups.Select(g => g.Famiglia));
        Assert.Equal([4, 2], groups[1].Persone.Select(p => p.Person.Id));
    }

    [Fact]
    public void Build_NestsBranchesUnderDynasty()
    {
        var groups = PeopleGrouping.Build(
        [
            Person(1, "Rodolfo", "Asburgo"),
            Person(2, "Filippo", "Asburgo di Spagna"),
            Person(3, "Carlo", "asburgo di spagna"),
            Person(4, "Cosimo", "Medici"),
        ],
        [
            new Dinastia { Id = 1, Nome = "Asburgo" },
            new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 },
            new Dinastia { Id = 3, Nome = "Asburgo-Lorena", ParentId = 1 },
        ]);

        Assert.Equal(["Asburgo", "Medici"], groups.Select(g => g.Famiglia));
        var asburgo = groups[0];
        var ramo = Assert.Single(asburgo.Rami);
        Assert.Equal("Asburgo di Spagna", ramo.Famiglia);
        Assert.Equal(2, ramo.Persone.Count);
        Assert.Equal("3 PERSONE", asburgo.Conteggio);
        Assert.IsType<PersonGroup>(asburgo.Elementi[0]);
        Assert.Equal(["Asburgo", "Asburgo di Spagna"], asburgo.SelfAndDescendants().Select(g => g.Famiglia));
    }

    [Fact]
    public void Build_ShowsDynastyWithOnlyBranchMembers()
    {
        var groups = PeopleGrouping.Build(
            [Person(1, "Filippo", "Asburgo di Spagna")],
            [new Dinastia { Id = 1, Nome = "Asburgo" }, new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 }]);

        var asburgo = Assert.Single(groups);
        Assert.Empty(asburgo.Persone);
        Assert.Equal("1 PERSONA", asburgo.Conteggio);
    }

    [Fact]
    public void Build_IgnoresParentCycles()
    {
        var groups = PeopleGrouping.Build(
            [Person(1, "Anna", "A"), Person(2, "Bruno", "B")],
            [new Dinastia { Id = 1, Nome = "A", ParentId = 2 }, new Dinastia { Id = 2, Nome = "B", ParentId = 1 }]);

        Assert.Equal(["A", "B"], groups.Select(g => g.Famiglia));
    }

    [Theory]
    [InlineData("1500", "1558", "n. 1500 · m. 1558")]
    [InlineData("1500", "", "n. 1500")]
    [InlineData("", "c. 1558", "m. c. 1558")]
    [InlineData("", " ", "Date non indicate")]
    public void DescribeDates_FormatsAvailableDates(string birth, string death, string expected)
        => Assert.Equal(expected, PeopleGrouping.DescribeDates(birth, death));

    [Theory]
    [InlineData("M", "Maschio")]
    [InlineData("f", "Femmina")]
    [InlineData("", "")]
    [InlineData("X", "")]
    public void DescribeSex_ReturnsItalianLabel(string sex, string expected)
        => Assert.Equal(expected, PeopleGrouping.DescribeSex(sex));

    [Fact]
    public void PersonEntry_ExposesColumnValues()
    {
        var entry = new PersonEntry(new HistoricalPerson
        {
            Name = "Cosimo",
            Sesso = "M",
            Birth = "1389",
            BirthPlace = "Firenze",
            Death = "1464",
            DeathPlace = "Careggi",
        });

        Assert.Equal("Maschio", entry.Sesso);
        Assert.Equal("1389", entry.Nascita);
        Assert.Equal("Firenze", entry.LuogoNascita);
        Assert.Equal("1464", entry.Morte);
        Assert.Equal("Careggi", entry.LuogoMorte);
    }
}
