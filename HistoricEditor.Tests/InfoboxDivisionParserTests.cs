using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class InfoboxDivisionParserTests
{
    [Fact]
    public void Parse_ReadsGrades_AsOnPortoPage()
    {
        const string wikitext = "{{Divisione amministrativa\n|Nome = Porto\n|Stato = PRT\n|Grado amministrativo = 3\n|Divisione amm grado 1 = [[Regione Nord (Portogallo)|Nord]]\n|Divisione amm grado 2 = [[Distretto di Porto|Porto]]<ref>x</ref>\n|Voce divisione amm grado 3 =\n}}";

        var result = InfoboxDivisionParser.Parse(wikitext);

        Assert.NotNull(result);
        Assert.Equal(3, result.AdministrativeGrade);
        Assert.Equal(new[] { "Nord", "Porto" }, result.Grades);
    }

    [Fact]
    public void Parse_KeepsNo_ForMissingDistrict()
    {
        const string wikitext = "{{Divisione amministrativa\n|Divisione amm grado 1 = Madera\n|Divisione amm grado 2 = no\n}}";

        Assert.Equal(new[] { "Madera", "no" }, InfoboxDivisionParser.Parse(wikitext)!.Grades);
    }

    [Fact]
    public void Parse_ReturnsNull_WithoutTemplate()
        => Assert.Null(InfoboxDivisionParser.Parse("Testo senza infobox"));
}
