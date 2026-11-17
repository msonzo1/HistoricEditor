using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Voce italiana di un luogo storico (2026-11-17): modello dell'infobox, nome originale senza segni di quantità, periodo.
public class HistoricPlaceArticleTests
{
    [Fact]
    public void AncientCity_ReadsTemplateLatinAndPeriod()
    {
        const string wikitext = """
            {{Città antica
            |Nome = Anzio
            |Nome originale = ''Antĭum''
            |Fondazione = [[IX secolo a.C.]]
            |Fine = [[VI secolo d.C.]]  
            |Causa = [[Invasioni barbariche]]
            }}
            '''Anzio''' (in [[Lingua latina|latino]] '''''Antĭum''''') era una città
            """;
        var article = WikipediaImportService.ParseHistoricPlaceArticle(wikitext);
        Assert.Equal(("Città antica", "Antium", "IX secolo a.C. – VI secolo d.C."), (article.Template, article.OriginalName, article.Period));
    }

    [Fact]
    public void ArchaeologicalSite_HasNoCityTemplate()
    {
        const string wikitext = """
            {{nd}}
            {{S|siti archeologici dell'Egitto}}
            {{Sito archeologico
            |Nome = Pelusio
            |Fondazione = 
            }}
            """;
        var article = WikipediaImportService.ParseHistoricPlaceArticle(wikitext);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (article.Template, article.OriginalName, article.Period));
    }

    [Theory]
    [InlineData("|Fondazione = [[753 a.C.]]\n", "dal 753 a.C.")]
    [InlineData("|Fine = [[476]]\n", "fino al 476")]
    public void Period_WithOnlyOneEnd(string parameter, string expected)
        => Assert.Equal(expected, WikipediaImportService.ParseHistoricPlaceArticle("{{Città antica\n" + parameter + "}}").Period);
}
