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

    [Theory]
    [InlineData("Benito Mussolini", "Mussolini", "Benito", "Mussolini")]
    [InlineData("Gabriel García Márquez", "García|Márquez", "Gabriel", "García Márquez")]
    [InlineData("Ludwig van Beethoven", "Beethoven", "Ludwig", "van Beethoven")]
    [InlineData("Charles de Gaulle", "de Gaulle", "Charles", "de Gaulle")]
    [InlineData("Mussolini", "Mussolini", "Mussolini", "")]
    [InlineData("Dante Alighieri", "", "Dante Alighieri", "")]
    [InlineData("Papa Giovanni Paolo II", "Wojtyła", "Papa Giovanni Paolo II", "")]
    public void SplitSurname_SeparatesTrailingFamilyName(string label, string surnames, string name, string surname)
    {
        var result = PersonNameParser.SplitSurname(label, surnames.Split('|', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal((name, surname), result);
    }

    [Fact]
    public void DisplayName_PutsSurnameBeforeSuffix()
    {
        Assert.Equal("Benito Mussolini", new HistoricalPerson { Name = "Benito", Surname = "Mussolini" }.DisplayName);
        Assert.Equal("Carlo V d'Asburgo", new HistoricalPerson { Name = "Carlo V", Suffix = "d'Asburgo" }.DisplayName);
        Assert.Equal("Martin Luther King Jr.", new HistoricalPerson { Name = "Martin Luther", Surname = "King", Suffix = "Jr." }.DisplayName);
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
