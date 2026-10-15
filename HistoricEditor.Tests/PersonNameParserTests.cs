namespace HistoricEditor.Tests;

using HistoricEditor;
using Xunit;

public class PersonNameParserTests
{
    [Theory]
    [InlineData("Carlo V d'Asburgo", "Carlo V", "d'Asburgo")]
    [InlineData("Luigi XIV di Borbone", "Luigi XIV", "di Borbone")]
    [InlineData("Carlo V", "Carlo V", "")]
    [InlineData("Dante Alighieri", "Dante Alighieri", "")]
    public void Split_SeparatesSuffixAfterRomanNumeral(string label, string name, string suffix)
    {
        var result = PersonNameParser.Split(label);

        Assert.Equal(name, result.Name);
        Assert.Equal(suffix, result.Suffix);
        Assert.Equal(string.Empty, result.Nickname);
    }

    [Fact]
    public void Split_ExtractsNickname()
    {
        var result = PersonNameParser.Split("Filippo il Bello", ["il Bello"]);

        Assert.Equal("Filippo", result.Name);
        Assert.Equal(string.Empty, result.Suffix);
        Assert.Equal("il Bello", result.Nickname);
    }

    [Fact]
    public void Split_ExtractsNicknameAndSuffix()
    {
        var result = PersonNameParser.Split("Filippo IV di Francia il Bello", ["il Bello"]);

        Assert.Equal("Filippo IV", result.Name);
        Assert.Equal("di Francia", result.Suffix);
        Assert.Equal("il Bello", result.Nickname);
    }
}
