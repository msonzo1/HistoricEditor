using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DateDiscrepancyTests
{
    private static HistoricalDateEvidence Evidence(string value, params string[] sourceTitles)
        => new()
        {
            Value = value,
            Sources = sourceTitles.Select(t => new SourceCitation { Title = t }).ToList(),
        };

    [Fact]
    public void Has_IsFalse_ForEmptyList()
        => Assert.False(DateDiscrepancy.Has([]));

    [Fact]
    public void Has_IsFalse_ForSingleValue()
        => Assert.False(DateDiscrepancy.Has([Evidence("1769")]));

    [Fact]
    public void Has_IsFalse_ForRepeatedSameValue()
        => Assert.False(DateDiscrepancy.Has([Evidence("1769"), Evidence("1769")]));

    [Fact]
    public void Has_IsTrue_ForTwoDistinctValues()
        => Assert.True(DateDiscrepancy.Has([Evidence("1769"), Evidence("1768")]));

    [Fact]
    public void Has_IgnoresEmptyValues()
        => Assert.False(DateDiscrepancy.Has([Evidence("1769"), Evidence("")]));

    [Fact]
    public void HasAny_IsTrue_WhenOnlyDeathDiffers()
        => Assert.True(DateDiscrepancy.HasAny([Evidence("1769")], [Evidence("1821"), Evidence("1822")]));

    [Fact]
    public void Describe_ListsValuesWithSources_WhenDiscrepant()
    {
        var text = DateDiscrepancy.Describe("Nascita", [Evidence("1769", "Wikipedia in italiano"), Evidence("1768", "Wikipedia in tedesco")]);
        Assert.Contains("Nascita", text);
        Assert.Contains("1769", text);
        Assert.Contains("1768", text);
        Assert.Contains("Wikipedia in tedesco", text);
    }

    [Fact]
    public void Describe_IsEmpty_WhenNoDiscrepancy()
        => Assert.Equal(string.Empty, DateDiscrepancy.Describe("Nascita", [Evidence("1769")]));

    [Fact]
    public void DescribeAll_CombinesBirthAndDeath()
    {
        var text = DateDiscrepancy.DescribeAll(
            [Evidence("1769"), Evidence("1768")],
            [Evidence("1821"), Evidence("1822")]);
        Assert.Contains("Nascita", text);
        Assert.Contains("Morte", text);
    }
}
