using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Date incerte (2026-11-14): giorno, mese, anno, secolo, una tra più date, tra due date, prima, dopo, circa.
public class UncertainDateTests
{
    // Forma compatta nei campi e forma leggibile in elenchi e schede.
    [Theory]
    [InlineData("17/11/0009", "17/11/0009", "17 novembre 9")]
    [InlineData("11/0009", "11/0009", "novembre 9")]
    [InlineData("0009", "0009", "9")]
    [InlineData("44 a.C.", "44 a.C.", "44 a.C.")]
    [InlineData("XV secolo", "XV secolo", "XV secolo")]
    [InlineData("sec. V a.C.", "V secolo a.C.", "V secolo a.C.")]
    [InlineData("12/07/100 a.C.; 13/07/100 a.C.", "12/07/100 a.C.; 13/07/100 a.C.", "12 o 13 luglio 100 a.C.")]
    [InlineData("1540 o 1541", "1540; 1541", "1540 o 1541")]
    [InlineData("tra 1503 e 1519", "tra 1503 e 1519", "tra il 1503 e il 1519")]
    [InlineData("1503-1519", "tra 1503 e 1519", "tra il 1503 e il 1519")]
    [InlineData("100–90 a.C.", "tra 100 a.C. e 90 a.C.", "tra il 100 a.C. e il 90 a.C.")]
    [InlineData("tra l'8 marzo 1500 e l'11 aprile 1500", "tra 08/03/1500 e 11/04/1500", "tra l'8 marzo 1500 e l'11 aprile 1500")]
    [InlineData("prima del 1500", "prima del 1500", "prima del 1500")]
    [InlineData("ante 850", "prima dell'0850", "prima dell'850")]
    [InlineData("dopo il 1500", "dopo il 1500", "dopo il 1500")]
    [InlineData("c. 1540", "c. 1540", "circa 1540")]
    [InlineData("circa XV secolo", "c. XV secolo", "circa XV secolo")]
    public void Parse_GivesCompactAndReadableForms(string text, string compact, string readable)
    {
        var date = UncertainDate.Parse(text);
        Assert.NotNull(date);
        Assert.Equal(compact, date!.Compact(DateFormats.Slash));
        Assert.Equal(readable, date.Readable());
    }

    [Theory]
    [InlineData("")]
    [InlineData("anni 1450")]
    [InlineData("23/06/79")]
    [InlineData("forse a Roma")]
    public void Parse_IgnoresUnrecognizedText(string text) => Assert.Null(UncertainDate.Parse(text));

    // Intervallo per i filtri e anno per ordinare.
    [Theory]
    [InlineData("1540", 1540, 1540, 1540)]
    [InlineData("XV secolo", 1401, 1500, 1450)]
    [InlineData("V secolo a.C.", -500, -401, -450)]
    [InlineData("tra 1503 e 1519", 1503, 1519, 1511)]
    [InlineData("1540; 1542", 1540, 1542, 1540)]
    public void Range_AndSortYear(string text, int first, int last, int sort)
    {
        var date = UncertainDate.Parse(text)!;
        Assert.Equal((first, last, sort), (date.FirstYear!.Value, date.LastYear!.Value, date.SortYear));
        Assert.Equal(sort, HistoricalYear.Parse(text));
    }

    [Fact]
    public void BeforeAndAfter_AreOpenRanges()
    {
        var before = UncertainDate.Parse("prima del 1500")!;
        Assert.Equal((null, 1500), (before.FirstYear, before.LastYear));
        var after = UncertainDate.Parse("dopo il 1500")!;
        Assert.Equal((1500, null), (after.FirstYear, after.LastYear));
    }

    // Filtro della mappa sul periodo: una data incerta vale se il suo intervallo si sovrappone al periodo.
    [Fact]
    public void MapFilter_UsesWholeRange()
    {
        MapPoint Point(string data, int? from, int? to) => new(MapPointKind.Nascita, 0, 0, "x", "y", data, HistoricalYear.Parse(data), 1, null, [1], "", "", false)
        {
            HasRange = true,
            AnnoDa = from,
            AnnoA = to,
        };

        var points = new[] { Point("tra 1503 e 1519", 1503, 1519), Point("prima del 1500", null, 1500), Point("XV secolo", 1401, 1500) };
        var filter = new MapFilter { Nascite = true, DaAnno = 1515, AAnno = 1530 };
        Assert.Equal(["tra 1503 e 1519"], HistoricalMap.Filter(points, filter, new Dictionary<int, string>()).Select(p => p.Data));

        filter = new MapFilter { Nascite = true, AAnno = 1450 };
        Assert.Equal(["prima del 1500", "XV secolo"], HistoricalMap.Filter(points, filter, new Dictionary<int, string>()).Select(p => p.Data));
    }
}
