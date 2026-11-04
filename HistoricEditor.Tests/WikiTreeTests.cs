using System.IO;
using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class WikiTreeTests
{
    private static WikiTreeProfile Profile(int id, string key, string first, string gender, int? father = null, int? mother = null,
        string birth = "", string birthPlace = "")
        => new(id, key, first, string.Empty, key.Split('-')[0], string.Empty, gender, birth, string.Empty, string.Empty, string.Empty,
            birthPlace, string.Empty, father, mother);

    [Theory]
    [InlineData("1500-02-24", "", "24/02/1500")]
    [InlineData("1527-05-00", "certain", "05/1527")]
    [InlineData("1527-00-00", "", "1527")]
    [InlineData("1547-00-00", "guess", "circa 1547")]
    [InlineData("1600-00-00", "before", "prima del 1600")]
    [InlineData("0000-00-00", "", "")]
    [InlineData("", "", "")]
    public void FormatWikiTreeDate_ConvertsPartialDatesAndQualifiers(string date, string status, string expected)
        => Assert.Equal(expected, WikipediaImportService.FormatWikiTreeDate(date, status));

    [Theory]
    [InlineData("Habsburg-105", "Habsburg-105", true)]
    [InlineData("  https://www.wikitree.com/wiki/Habsburg-105#Biography ", "Habsburg-105", true)]
    [InlineData("https://www.wikitree.com/wiki/Van%20Dyck-12", "Van_Dyck-12", true)]
    [InlineData("Habsburg", "Habsburg", false)]
    [InlineData("", "", false)]
    public void Links_NormalizeAndValidateProfileKeys(string text, string key, bool valid)
    {
        Assert.Equal(key, WikiTreeLinks.Normalize(text));
        Assert.Equal(valid, WikiTreeLinks.IsValid(WikiTreeLinks.Normalize(text)));
    }

    [Fact]
    public void ReadWikiTreeId_ReadsP2949()
    {
        using var claims = JsonDocument.Parse("""{"P2949":[{"rank":"normal","mainsnak":{"datavalue":{"value":"Habsburg-105"}}}]}""");
        using var empty = JsonDocument.Parse("{}");

        Assert.Equal("Habsburg-105", WikipediaImportService.ReadWikiTreeId(claims.RootElement));
        Assert.Equal(string.Empty, WikipediaImportService.ReadWikiTreeId(empty.RootElement));
    }

    [Fact]
    public void Parsers_ReadRelativesPeopleAndSparql()
    {
        using var relatives = JsonDocument.Parse("""
            [{"items":[{"key":"Habsburg-105","person":{"Id":100,"Name":"Habsburg-105","FirstName":"Karl","LastNameAtBirth":"von Habsburg",
              "Gender":"Male","BirthDate":"1500-02-24","DataStatus":{"BirthDate":"certain"},"BirthLocation":"Gent","Father":"1","Mother":2,
              "Parents":{"1":{"Id":1,"Name":"Habsburg-1","FirstName":"Philipp","Gender":"Male"},"2":{"Id":2,"Name":"Trastámara-2","FirstName":"Juana","Gender":"Female"}},
              "Spouses":{"3":{"Id":3,"Name":"Aviz-15","FirstName":"Isabel","Gender":"Female"}},
              "Children":{"4":{"Id":4,"Name":"Habsburg-200","FirstName":"Juan","Gender":"Male","Father":100,"Mother":5},"x":{"Name":""}}}}],"status":0}]
            """);
        using var people = JsonDocument.Parse("""[{"people":{"5":{"Id":5,"Name":"Blomberg-218","FirstName":"Bárbara","Gender":"Female"}}}]""");
        using var sparql = JsonDocument.Parse("""
            {"results":{"bindings":[{"item":{"value":"http://www.wikidata.org/entity/Q32500"},"wt":{"value":"Habsburg-105"}},
              {"item":{"value":"http://www.wikidata.org/entity/xyz"},"wt":{"value":"Bad-1"}}]}}
            """);

        var family = WikipediaImportService.ParseWikiTreeRelatives(relatives.RootElement);

        Assert.NotNull(family);
        Assert.Equal(("Habsburg-105", "Karl", 1, 2), (family.Person.Key, family.Person.GivenName, family.Person.FatherId, family.Person.MotherId));
        Assert.Equal(("1500-02-24", "certain", "Gent"), (family.Person.BirthDate, family.Person.BirthStatus, family.Person.BirthLocation));
        Assert.Equal(["Habsburg-1", "Trastámara-2"], family.Parents.Select(p => p.Key));
        Assert.Equal("Aviz-15", Assert.Single(family.Spouses).Key);
        Assert.Equal(5, Assert.Single(family.Children).MotherId);
        Assert.Equal("Blomberg-218", Assert.Single(WikipediaImportService.ParseWikiTreePeople(people.RootElement)).Key);
        Assert.Equal([("Habsburg-105", "Q32500")], WikipediaImportService.ParseWikiTreeSparql(sparql.RootElement));
        Assert.Null(WikipediaImportService.ParseWikiTreeRelatives(JsonDocument.Parse("""[{"items":[],"status":0}]""").RootElement));
    }

    [Fact]
    public void BuildPerson_KeepsWikiTreeDataAndPlacesAsText()
    {
        var person = WikiTreeLinker.BuildPerson(Profile(5, "Blomberg-218", "Bárbara", "Female", birth: "1527-00-00", birthPlace: "Regensburg"), "Q233070");

        Assert.Equal(("Bárbara", "Blomberg", Sessi.Femmina), (person.Name, person.Surname, person.Sesso));
        Assert.Equal(("1527", string.Empty), (person.Birth, person.Death));
        Assert.Equal(("Blomberg-218", "Q233070"), (person.WikiTreeId, person.WikidataId));
        var place = Assert.Single(person.BirthPlaces);
        Assert.Equal("Regensburg", place.CurrentName);
        Assert.Equal("https://www.wikitree.com/wiki/Blomberg-218", Assert.Single(place.Sources).Url);
        Assert.Contains(person.BirthDateEvidence, e => e.Value == "1527");
    }

    [Fact]
    public void Link_CompletesParentsAndUnions_WithoutReplacingExistingOnes_AndListsMissingRelatives()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            int Add(string name, string sesso, string wikiTree = "", string qid = "")
            {
                var person = new HistoricalPerson { Name = name, Sesso = sesso, WikiTreeId = wikiTree, WikidataId = qid };
                store.Add(person);
                return person.Id;
            }

            var carlo = Add("Carlo V", Sessi.Maschio);
            var isabella = Add("Isabella d'Aviz", Sessi.Femmina, wikiTree: "Aviz-15");
            var filippo = Add("Filippo II", Sessi.Maschio, qid: "Q34417");
            var giovanni = Add("Giovanni d'Austria", Sessi.Maschio, wikiTree: "Habsburg-200");
            var altroPadre = Add("Altro padre", Sessi.Maschio);
            store.SetParent(giovanni, altroPadre, mother: false);

            var family = new WikiTreeFamily(
                Profile(100, "Habsburg-105", "Karl", "Male", father: 1, mother: 2),
                [Profile(1, "Habsburg-1", "Philipp", "Male"), Profile(2, "Trastámara-2", "Juana", "Female")],
                [Profile(3, "Aviz-15", "Isabel", "Female")],
                [Profile(4, "Habsburg-150", "Felipe", "Male", father: 100, mother: 3), Profile(6, "Habsburg-200", "Juan", "Male", father: 100, mother: 5)],
                [Profile(5, "Blomberg-218", "Bárbara", "Female")]);
            var qids = new Dictionary<string, string> { ["Habsburg-150"] = "Q34417", ["Habsburg-1"] = "Q157098" };

            Assert.Equal(3, WikiTreeLinker.Link(store, carlo, family, qids));

            Assert.True(store.HasUnion(carlo, isabella));
            Assert.Equal((isabella, carlo), store.GetParents(filippo));
            Assert.Equal(((int?)null, altroPadre), store.GetParents(giovanni));
            Assert.Equal("Habsburg-150", store.GetPerson(filippo)!.WikiTreeId);
            Assert.Equal(0, WikiTreeLinker.Link(store, carlo, family, qids));

            var missing = WikiTreeLinker.MissingRelatives(store, carlo, family, qids, proposedWikidataIds: ["Q157098"]);
            Assert.Equal(
                [("Trastámara-2", "madre", ""), ("Blomberg-218", "madre di Juan", "")],
                missing.Select(m => (m.Profile.Key, m.Ruolo, m.WikidataId)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
