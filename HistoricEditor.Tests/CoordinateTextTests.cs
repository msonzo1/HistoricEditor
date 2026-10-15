using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class CoordinateTextTests
{
    [Fact]
    public void TryParse_AcceptsEmpty_AsNull()
    {
        Assert.True(CoordinateText.TryParse("  ", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryParse_ParsesDotDecimal()
    {
        Assert.True(CoordinateText.TryParse("41.9", out var value));
        Assert.Equal(41.9, value);
    }

    [Fact]
    public void TryParse_ParsesCommaDecimal()
    {
        Assert.True(CoordinateText.TryParse("41,9", out var value));
        Assert.Equal(41.9, value);
    }

    [Fact]
    public void TryParse_RejectsNonNumeric()
    {
        Assert.False(CoordinateText.TryParse("abc", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Format_UsesInvariantCulture()
        => Assert.Equal("41.9", CoordinateText.Format(41.9));

    [Fact]
    public void Format_ReturnsEmpty_ForNull()
        => Assert.Equal(string.Empty, CoordinateText.Format(null));
}
