using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class HistoricalYearTests
{
    [Theory]
    [InlineData("1550", 1550)]
    [InlineData("c. 1540", 1540)]
    [InlineData("12 marzo 1550", 1550)]
    [InlineData("1540 circa", 1540)]
    [InlineData("44 a.C.", -44)]
    [InlineData("753 a.C.", -753)]
    [InlineData("79 d.C.", 79)]
    [InlineData("anni 1450", 1450)]
    [InlineData("XV secolo", 1450)]
    [InlineData("V secolo a.C.", -450)]
    [InlineData("1540 a Como", 1540)]
    [InlineData("1512 a Cremona", 1512)]
    [InlineData("44 aC", -44)]
    [InlineData("sec. XV", 1450)]
    [InlineData("secolo XV", 1450)]
    [InlineData("XV sec.", 1450)]
    public void Parse_ExtractsYear(string text, int expected) => Assert.Equal(expected, HistoricalYear.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("sconosciuta")]
    [InlineData("il secolo d'oro")]
    [InlineData(null)]
    public void Parse_WithoutYear_ReturnsNull(string? text) => Assert.Null(HistoricalYear.Parse(text));

    [Fact]
    public void OrderByYear_UsesYearAndPutsUnknownLast()
    {
        string[] dates = ["1550", "", "c. 1540", "1545"];
        Assert.Equal(["c. 1540", "1545", "1550", ""], HistoricalYear.OrderByYear(dates, d => d).ToArray());
    }
}
