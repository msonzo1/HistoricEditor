using Xunit;

namespace HistoricEditor.Tests;

public class DateEvidenceBuilderTests
{
    private static readonly HistoricalDateEvidence Imported = new()
    {
        Value = "1452",
        Sources = [new SourceCitation { Title = "Wikipedia in italiano", Url = "https://it.wikipedia.org/wiki/Leonardo" }],
    };

    [Fact]
    public void UserSourcesAreNotPairedByPosition()
    {
        // Le fonti mostrate contengono quella della data importata: non va attribuita alla data nuova.
        var evidence = DateEvidenceBuilder.Build("1452; c. 1450", "https://it.wikipedia.org/wiki/Leonardo; https://libro.example", [Imported], null);

        Assert.Equal(["https://it.wikipedia.org/wiki/Leonardo"], evidence[0].Sources.Select(s => s.Url));
        Assert.Equal("c. 1450", evidence[1].Value);
        Assert.Equal(["https://libro.example"], evidence[1].Sources.Select(s => s.Url));
    }

    [Fact]
    public void NewSourceWithoutNewDate_IsKeptOnFirstDate()
    {
        var evidence = DateEvidenceBuilder.Build("1452", "https://libro.example", [Imported], null);

        Assert.Single(evidence);
        Assert.Contains(evidence[0].Sources, s => s.Url == "https://libro.example");
        Assert.Single(Imported.Sources);
    }

    [Fact]
    public void NewDateWithoutSources_UsesInputCitation()
    {
        var evidence = DateEvidenceBuilder.Build("1452", string.Empty, null, new SourceCitation { Url = "https://fonte.example" });

        Assert.Equal(["https://fonte.example"], evidence[0].Sources.Select(s => s.Url));
    }
}
