using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Formato standard delle date (2026-11-10): GG/MM/AAAA con l'anno a quattro cifre, «a.C.» solo prima di Cristo.
// I test passano il formato esplicitamente per non dipendere dall'impostazione globale.
public class DateFormatsTests
{
    [Theory]
    [InlineData(79, 6, 23, DateFormats.Slash, "23/06/0079")]
    [InlineData(79, 6, 23, DateFormats.Dot, "23.06.0079")]
    [InlineData(79, 6, 23, DateFormats.Dash, "23-06-0079")]
    [InlineData(1525, 2, 10, DateFormats.Slash, "10/02/1525")]
    [InlineData(-44, 3, 15, DateFormats.Slash, "15/03/44 a.C.")]
    [InlineData(79, 6, null, DateFormats.Slash, "06/0079")]
    [InlineData(79, null, null, DateFormats.Slash, "0079")]
    [InlineData(-44, null, null, DateFormats.Dot, "44 a.C.")]
    public void Format_UsesFourDigitYearsAndBeforeChristOnly(int year, int? month, int? day, string format, string expected)
        => Assert.Equal(expected, DateFormats.Format(year, month, day, format));

    [Theory]
    [InlineData("17/11/9 d.C.", "17/11/0009")]
    [InlineData("23/06/79 d.C.; 04/06/79 d.C.", "23/06/0079; 04/06/0079")]
    [InlineData("10 febbraio 1525", "10/02/1525")]
    [InlineData("1° marzo 1550", "01/03/1550")]
    [InlineData("marzo 1550", "03/1550")]
    [InlineData("79 d.C.", "0079")]
    [InlineData("23.06.0079", "23/06/0079")]
    [InlineData("15/03/44 a.C.", "15/03/44 a.C.")]
    [InlineData("1525", "1525")]
    public void Normalize_RewritesRecognizedDates(string text, string expected)
        => Assert.Equal(expected, DateFormats.Normalize(text, DateFormats.Slash));

    // Il testo libero e le date ambigue restano come sono.
    [Theory]
    [InlineData("c. 1540")]
    [InlineData("XV secolo")]
    [InlineData("1503-1519")]
    [InlineData("23/06/79")]
    [InlineData("anni 1450")]
    [InlineData("")]
    public void Normalize_LeavesFreeTextUnchanged(string text)
        => Assert.Equal(text, DateFormats.Normalize(text, DateFormats.Slash));

    [Fact]
    public void Normalize_UsesTheChosenSeparator()
        => Assert.Equal("17-11-0009; 44 a.C.", DateFormats.Normalize("17/11/9 d.C.; 44 a.C.", DateFormats.Dash));

    [Theory]
    [InlineData("23-06-0079", 79, 6, 23)]
    [InlineData("23.06.0079", 79, 6, 23)]
    [InlineData("06-0079", 79, 6, null)]
    [InlineData("15-03-44 a.C.", -44, 3, 15)]
    public void CalendarParser_ReadsAllFormats(string text, int year, int? month, int? day)
    {
        Assert.True(HistoricalCalendar.TryParse(text, out var date));
        Assert.Equal(new CalendarDate(year, month, day), date);
        Assert.Equal(year, HistoricalYear.Parse(text));
    }

    [Theory]
    [InlineData("25.12.0800", 800, 12, 25)]
    [InlineData("25-12-0800", 800, 12, 25)]
    [InlineData("25/12/800", 800, 12, 25)]
    public void TitleDates_ReadAllSeparators(string text, int year, int month, int day)
        => Assert.Equal(new TitleDate(year, month, day), TitleDateConsensus.Parse(text));

    [Theory]
    [InlineData("", "23/06/0079", "23/06/0079")]
    [InlineData("1525", "10/02/1525", "10/02/1525")]
    [InlineData("79 d.C.; 80 d.C.", "23/06/0079", "23/06/0079; 80 d.C.")]
    public void Picker_ReplacesOnlyTheFirstHypothesis(string text, string date, string expected)
        => Assert.Equal(expected, DatePickerButton.ReplaceFirstHypothesis(text, date));

    // Una data importata con il formato precedente conserva le sue fonti quando la scheda la mostra nel formato nuovo.
    [Fact]
    public void Evidence_MatchesDatesWrittenInTheOldFormat()
    {
        var imported = new HistoricalDateEvidence
        {
            Value = DateFormats.Normalize("17/11/9 d.C."),
            Sources = [new SourceCitation { Title = "Wikipedia in italiano", Url = "https://it.wikipedia.org/wiki/Vespasiano" }],
        };
        var old = new HistoricalDateEvidence { Value = "17/11/9 d.C.", Sources = imported.Sources };

        var evidence = Assert.Single(DateEvidenceBuilder.Build(DateFormats.Format(9, 11, 17), string.Empty, [old], null));
        Assert.Equal(DateFormats.Format(9, 11, 17), evidence.Value);
        Assert.Equal("Wikipedia in italiano", Assert.Single(evidence.Sources).Title);
    }
}
