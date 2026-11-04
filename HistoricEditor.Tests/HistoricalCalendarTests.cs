using System.IO;
using System.Text.Json;
using HistoricEditor;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HistoricEditor.Tests;

public sealed class HistoricalCalendarTests
{
    private const string G = HistoricalCalendar.Gregoriano;
    private const string J = HistoricalCalendar.Giuliano;

    [Fact]
    public void Convert_GregorianReformDays()
    {
        var gregorian = HistoricalCalendar.Convert(new CalendarDate(1582, 10, 4), J, G);
        Assert.Equal(new CalendarDate(1582, 10, 14), gregorian);
        Assert.Equal(new CalendarDate(1582, 10, 4), HistoricalCalendar.Convert(gregorian, G, J));
        Assert.Equal(HistoricalCalendar.ToDayNumber(new CalendarDate(1582, 10, 4), J) + 1,
            HistoricalCalendar.ToDayNumber(new CalendarDate(1582, 10, 15), G));
    }

    [Theory]
    [InlineData(1582, 10, 15, G, "venerdì")]
    [InlineData(1582, 10, 4, J, "giovedì")]
    [InlineData(1525, 2, 24, J, "venerdì")]
    public void WeekDay_IsCorrect(int year, int month, int day, string calendar, string expected)
        => Assert.Equal(expected, HistoricalCalendar.WeekDay(new CalendarDate(year, month, day), calendar));

    [Fact]
    public void Convert_RoundTripsBeforeChrist()
    {
        var date = new CalendarDate(-44, 3, 15);
        var gregorian = HistoricalCalendar.Convert(date, J, G);
        Assert.Equal(new CalendarDate(-44, 3, 13), gregorian);
        Assert.Equal(date, HistoricalCalendar.Convert(gregorian, G, J));
    }

    [Theory]
    [InlineData("10 febbraio 1525", 1525, 2, 10)]
    [InlineData("1° marzo 1550", 1550, 3, 1)]
    [InlineData("15/03/44 a.C.", -44, 3, 15)]
    [InlineData("03/1550", 1550, 3, null)]
    [InlineData("24 feb. 1525", 1525, 2, 24)]
    [InlineData("marzo 1550", 1550, 3, null)]
    [InlineData("1525", 1525, null, null)]
    [InlineData("10 febbraio 1525; 11 febbraio 1525", 1525, 2, 10)]
    public void TryParse_ReadsCommonForms(string text, int year, int? month, int? day)
    {
        Assert.True(HistoricalCalendar.TryParse(text, out var date));
        Assert.Equal(new CalendarDate(year, month, day), date);
    }

    [Theory]
    [InlineData("XV secolo")]
    [InlineData("anni 1520")]
    [InlineData("")]
    [InlineData("31/13/1525")]
    public void TryParse_RejectsNonDates(string text)
        => Assert.False(HistoricalCalendar.TryParse(text, out _));

    [Fact]
    public void IsValid_LeapYearDependsOnCalendar()
    {
        var date = new CalendarDate(1500, 2, 29);
        Assert.True(HistoricalCalendar.IsValid(date, J));
        Assert.False(HistoricalCalendar.IsValid(date, G));
        Assert.True(HistoricalCalendar.IsValid(new CalendarDate(1600, 2, 29), G));
    }

    [Theory]
    [InlineData(HistoricalCalendar.StileFiorentino, 1, 10, 1)]
    [InlineData(HistoricalCalendar.StileFiorentino, 3, 24, 1)]
    [InlineData(HistoricalCalendar.StileFiorentino, 3, 25, 0)]
    [InlineData(HistoricalCalendar.StilePisano, 3, 24, 0)]
    [InlineData(HistoricalCalendar.StilePisano, 6, 1, -1)]
    [InlineData(HistoricalCalendar.StileVeneto, 2, 28, 1)]
    [InlineData(HistoricalCalendar.StileVeneto, 3, 1, 0)]
    [InlineData(HistoricalCalendar.StileNativita, 12, 25, -1)]
    [InlineData(HistoricalCalendar.StileNativita, 12, 24, 0)]
    [InlineData(HistoricalCalendar.StileBizantino, 9, 1, -1)]
    [InlineData(HistoricalCalendar.StileBizantino, 8, 31, 0)]
    public void StyleShift_FollowsYearStart(string style, int month, int day, int expected)
        => Assert.Equal(expected, HistoricalCalendar.StyleShift(new CalendarDate(1500, month, day), style));

    [Fact]
    public void StyleShift_IsNullWhenAmbiguous()
    {
        Assert.Null(HistoricalCalendar.StyleShift(new CalendarDate(1500), HistoricalCalendar.StileFiorentino));
        Assert.Null(HistoricalCalendar.StyleShift(new CalendarDate(1500, 3), HistoricalCalendar.StilePisano));
        Assert.Null(HistoricalCalendar.StyleShift(new CalendarDate(1500, 12), HistoricalCalendar.StileNativita));
        Assert.Equal(0, HistoricalCalendar.StyleShift(new CalendarDate(1500), HistoricalCalendar.StileComune));
    }

    [Fact]
    public void ModernYear_AppliesStyleAndSkipsYearZero()
    {
        Assert.Equal(1525, HistoricalCalendar.ModernYear("24 febbraio 1524", HistoricalCalendar.StileFiorentino));
        Assert.Equal(1524, HistoricalCalendar.ModernYear("24 febbraio 1524", null));
        Assert.Equal(1524, HistoricalCalendar.ModernYear("1524", HistoricalCalendar.StileFiorentino));
        Assert.Equal(1, HistoricalCalendar.ModernYear("10 gennaio 1 a.C.", HistoricalCalendar.StileFiorentino));
    }

    [Fact]
    public void Describe_ExplainsModernDate()
    {
        Assert.Equal(string.Empty, HistoricalCalendar.Describe("24 febbraio 1525", "", ""));
        Assert.Equal("Anno moderno: 24 febbraio 1526 · nel calendario gregoriano: 6 marzo 1526, sabato.",
            HistoricalCalendar.Describe("24 febbraio 1525", J, HistoricalCalendar.StileFiorentino));
        Assert.Contains("non esiste", HistoricalCalendar.Describe("29 febbraio 1500", G, ""));
        Assert.Contains("precede la riforma", HistoricalCalendar.Describe("1 gennaio 1500", G, ""));
        Assert.Contains("Anno moderno incerto", HistoricalCalendar.Describe("1500", "", HistoricalCalendar.StileFiorentino));
    }

    [Fact]
    public void ConvertAll_ListsCalendarsAndStyles()
    {
        var rows = HistoricalCalendar.ConvertAll(new CalendarDate(1525, 2, 24), J, HistoricalCalendar.StileComune);
        Assert.Equal(2 + HistoricalCalendar.Stili.Count - 1, rows.Count);
        Assert.Equal("24 febbraio 1525", rows[0].Data);
        Assert.Equal("6 marzo 1525", rows[1].Data);
        Assert.Equal("24 febbraio 1524", rows.Single(r => r.Sistema.Contains("Fiorentino")).Data);
        Assert.Equal("24 febbraio 1524", rows.Single(r => r.Sistema.Contains("Veneto")).Data);
        Assert.Equal("24 febbraio 1525", rows.Single(r => r.Sistema.Contains("Pisano")).Data);
    }

    [Fact]
    public void UsesJulian_DependsOnCountry()
    {
        var britain = HistoricalCalendar.Adozioni.Single(a => a.Paese.StartsWith("Gran Bretagna"));
        Assert.True(HistoricalCalendar.UsesJulian(britain, new CalendarDate(1700, 6, 1)));
        Assert.True(HistoricalCalendar.UsesJulian(britain, new CalendarDate(1752, 9, 13)));
        Assert.False(HistoricalCalendar.UsesJulian(britain, new CalendarDate(1752, 9, 14)));
        Assert.False(HistoricalCalendar.UsesJulian(HistoricalCalendar.Adozioni[0], new CalendarDate(1600)));
    }

    [Fact]
    public void Evento_UsesStyleForYearAndText()
    {
        var evento = new Evento
        {
            Data1 = "24 febbraio 1524",
            Calendario = J,
            Stile = HistoricalCalendar.StileFiorentino,
        };
        Assert.Equal(1525, evento.Anno);
        Assert.Equal("24 febbraio 1524 (giuliano, stile fiorentino)", evento.DataTesto);
        Assert.Null(DataEvento.Validate(DataEvento.DaA, "1 aprile 1524", "10 marzo 1524", HistoricalCalendar.StileFiorentino));
        Assert.NotNull(DataEvento.Validate(DataEvento.DaA, "10 marzo 1525", "1 aprile 1525", HistoricalCalendar.StilePisano));
        Assert.Null(DataEvento.Validate(DataEvento.DaA, "10 marzo 1525", "1 aprile 1525"));
    }

    [Fact]
    public void Store_PersistsCalendarsAndSortsByModernYear()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-calendar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using (var store = new PeopleStore(dir))
            {
                store.SaveEvent(new Evento { Titolo = "Dopo", Data1 = "1 maggio 1524" });
                store.SaveEvent(new Evento { Titolo = "Fiorentino", Data1 = "24 febbraio 1524", Calendario = J, Stile = HistoricalCalendar.StileFiorentino });
                store.SaveEvent(new Evento { Titolo = "Prima", Data1 = "1 aprile 1523", Calendario = "sconosciuto" });

                var id = store.CreatePerson("Francesco I", Sessi.Maschio);
                var person = store.GetPerson(id)!;
                person.BirthCalendario = J;
                person.BirthStile = HistoricalCalendar.StileVeneto;
                person.DeathCalendario = G;
                store.Update(person);
            }

            using var reopened = new PeopleStore(dir);
            var events = reopened.GetEvents();
            Assert.Equal(["Prima", "Dopo", "Fiorentino"], events.Select(e => e.Titolo));
            Assert.Equal((J, HistoricalCalendar.StileFiorentino), (events[2].Calendario, events[2].Stile));
            Assert.Equal(string.Empty, events[0].Calendario);

            var saved = Assert.Single(reopened.LoadAll());
            Assert.Equal((J, HistoricalCalendar.StileVeneto, G, string.Empty),
                (saved.BirthCalendario, saved.BirthStile, saved.DeathCalendario, saved.DeathStile));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Wikidata_DetectsJulianCalendar()
    {
        const string julian = "http://www.wikidata.org/entity/Q1985786";
        const string gregorian = "http://www.wikidata.org/entity/Q1985727";
        static string Claim(string property, string model, int precision)
            => $$"""
                "{{property}}": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "time": "+1525-02-24T00:00:00Z", "precision": {{precision}}, "calendarmodel": "{{model}}" } } } }]
                """;

        using var julianDay = JsonDocument.Parse($"{{{Claim("P585", julian, 11)}}}");
        using var julianYear = JsonDocument.Parse($"{{{Claim("P585", julian, 9)}}}");
        using var gregorianDay = JsonDocument.Parse($"{{{Claim("P580", gregorian, 11)}}}");

        Assert.Equal(J, WikipediaImportService.ReadEventCalendar(julianDay.RootElement));
        Assert.Equal(string.Empty, WikipediaImportService.ReadEventCalendar(julianYear.RootElement));
        Assert.Equal(string.Empty, WikipediaImportService.ReadEventCalendar(gregorianDay.RootElement));
    }

    [Fact]
    public void ImportedCalendar_RequiresPreciseJulianEvidence()
    {
        Assert.Equal(J, PersonEditorWindow.ImportedCalendar([new HistoricalDateEvidence { Precision = "giorno", Calendario = J }]));
        Assert.Equal(string.Empty, PersonEditorWindow.ImportedCalendar([new HistoricalDateEvidence { Precision = "anno", Calendario = J }]));
        Assert.Equal(string.Empty, PersonEditorWindow.ImportedCalendar([new HistoricalDateEvidence { Precision = "giorno" }]));
    }

    [Fact]
    public void ConverterWindow_ShowsConversions()
    {
        WpfTestThread.Run(() =>
        {
            var window = new CalendarConverterWindow("24 febbraio 1525", J, HistoricalCalendar.StileFiorentino);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CalendarConversion>>(window.ResultGrid.ItemsSource);
            Assert.Equal("24 febbraio 1526", rows[0].Data);
            Assert.True(window.CopyButton.IsEnabled);

            window.DateBox.Text = "XV secolo";
            window.UpdateResults();
            Assert.Null(window.ResultGrid.ItemsSource);
            Assert.Contains("non riconosciuta", window.MessageText.Text);
            window.Close();
        });
    }
}
