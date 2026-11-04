using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class PersonSheetTests
{
    [Fact]
    public void Sources_KeepsOnlyWebLinksWithoutDuplicates()
    {
        var person = new HistoricalPerson
        {
            WikipediaTitle = "Carlo V d'Asburgo",
            WikipediaUrl = "https://it.wikipedia.org/wiki/Carlo_V_d%27Asburgo",
            WikidataId = "Q12096",
            BirthDateEvidence = [new HistoricalDateEvidence { Sources = [new SourceCitation { Title = "Treccani", Url = "https://www.treccani.it/carlo" }, new SourceCitation { Url = "libro di famiglia" }] }],
            DeathDateEvidence = [new HistoricalDateEvidence { Sources = [new SourceCitation { Url = "https://www.treccani.it/carlo" }] }],
        };

        var links = PersonSheet.Sources(person);

        Assert.Equal(["Wikipedia: Carlo V d'Asburgo", "Wikidata: Q12096", "Data di nascita: Treccani"], links.Select(l => l.Etichetta));
        Assert.Equal("https://www.wikidata.org/wiki/Q12096", links[1].Url);
    }

    [Fact]
    public void DynastyPath_DescribesBranchesAndIgnoresUnknownFamilies()
    {
        IReadOnlyList<Dinastia> dynasties =
        [
            new Dinastia { Id = 1, Nome = "Asburgo" },
            new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 },
        ];

        Assert.Equal("Ramo di: Asburgo", PersonSheet.DynastyPath("Asburgo di Spagna", dynasties));
        Assert.Equal("Dinastia principale", PersonSheet.DynastyPath("Asburgo", dynasties));
        Assert.Equal(string.Empty, PersonSheet.DynastyPath("Valois", dynasties));
    }
    [Fact]
    public void MainFields_SkipsEmptyValuesAndDescribesSex()
    {
        var person = new HistoricalPerson { Name = "Carlo", Suffix = "V", Family = "Asburgo", Sesso = Sessi.Maschio, Nickname = "  " };

        var fields = PersonSheet.MainFields(person);

        Assert.Equal(["Nome", "Suffisso", "Famiglia", "Sesso"], fields.Select(f => f.Etichetta));
        Assert.Equal("Maschio", fields[^1].Valore);
    }

    [Theory]
    [InlineData("24 febbraio 1500", "", "", "24 febbraio 1500")]
    [InlineData("24 febbraio 1500", HistoricalCalendar.Giuliano, "", "24 febbraio 1500 (calendario giuliano)")]
    [InlineData("10 febbraio 1525", HistoricalCalendar.Giuliano, HistoricalCalendar.StileFiorentino, "10 febbraio 1525 (calendario giuliano, stile Fiorentino (25 marzo))")]
    [InlineData("  ", HistoricalCalendar.Giuliano, "", "")]
    public void DescribeDate_AddsCalendarAndStyle(string date, string calendar, string style, string expected)
        => Assert.Equal(expected, PersonSheet.DescribeDate(date, calendar, style));

    [Fact]
    public void DescribeHistoricalContext_UsesFirstNonEmptyContext()
    {
        var places = new[]
        {
            new HistoricalPlaceRecord { HistoricalContexts = [new HistoricalPlaceContext()] },
            new HistoricalPlaceRecord
            {
                HistoricalContexts = [new HistoricalPlaceContext { NameAtTime = "Gand", PoliticalEntity = "Paesi Bassi asburgici", Period = "1500" }],
            },
        };

        Assert.Equal("Gand, Paesi Bassi asburgici (1500)", PersonSheet.DescribeHistoricalContext(places));
        Assert.Equal(string.Empty, PersonSheet.DescribeHistoricalContext([]));
    }

    [Fact]
    public void LocalizedNames_JoinsNameSuffixNicknameAndTitle()
    {
        var names = new[]
        {
            new LocalizedPersonName { Lingua = "en", LinguaNome = "inglese", Nome = "Charles", Suffisso = "V", TitoloPrincipale = "Holy Roman Emperor" },
            new LocalizedPersonName { Lingua = "de" },
        };

        var field = Assert.Single(PersonSheet.LocalizedNames(names));
        Assert.Equal("Inglese", field.Etichetta);
        Assert.Equal("Charles V · Holy Roman Emperor", field.Valore);
    }

    [Fact]
    public void Titles_DescribePeriodCausesAndSuccession()
    {
        var titles = new[]
        {
            new PersonTitle
            {
                Titolo = "Re di Spagna", Numerazione = "I", Inizio = "1516", CausaInizio = "Morte del predecessore",
                Fine = "1556", CausaFine = "Abdicazione", Predecessore = "Ferdinando II", Successore = "Filippo II",
            },
            new PersonTitle { Titolo = "Duca di Borgogna", Fine = "1555" },
            new PersonTitle(),
        };

        var sheet = PersonSheet.Titles(titles);

        Assert.Equal(2, sheet.Count);
        Assert.Equal("Re di Spagna (I)", sheet[0].Nome);
        Assert.Equal("dal 1516 (Morte del predecessore) al 1556 (Abdicazione) · Predecessore: Ferdinando II · Successore: Filippo II", sheet[0].Dettagli);
        Assert.Equal("fino al 1555", sheet[1].Dettagli);
    }
}
