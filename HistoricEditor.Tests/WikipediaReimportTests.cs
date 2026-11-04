using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class WikipediaReimportTests
{
    [Fact]
    public void ReadItalianSitelinkTitle_ReturnsItwikiTitle()
    {
        const string json = """
            {"entities":{"Q1":{"sitelinks":{"itwiki":{"site":"itwiki","title":"Carlo V d'Asburgo"},"enwiki":{"title":"Charles V"}}}}}
            """;
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Carlo V d'Asburgo", WikipediaImportService.ReadItalianSitelinkTitle(document.RootElement, "Q1"));
    }

    [Theory]
    [InlineData("""{"entities":{"Q1":{"sitelinks":{"enwiki":{"title":"Charles V"}}}}}""")]
    [InlineData("""{"entities":{"Q1":{"id":"Q1","missing":""}}}""")]
    [InlineData("""{"entities":{"Q2":{"sitelinks":{"itwiki":{"title":"Altro"}}}}}""")]
    [InlineData("""{"error":{"code":"no-such-entity"}}""")]
    public void ReadItalianSitelinkTitle_ReturnsEmpty_WhenMissing(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(string.Empty, WikipediaImportService.ReadItalianSitelinkTitle(document.RootElement, "Q1"));
    }

    [Theory]
    [InlineData("https://it.wikipedia.org/wiki/Carlo_V", true)]
    [InlineData("  http://example.org/fonte  ", true)]
    [InlineData("Archivio di Stato, busta 12", false)]
    [InlineData("file:///C:/segreto.txt", false)]
    [InlineData("", false)]
    public void SourceLinkUri_AcceptsOnlyWebAddresses(string text, bool expected)
    {
        Assert.Equal(expected, PersonEditorWindow.SourceLinkUri(text) is not null);
    }
}
