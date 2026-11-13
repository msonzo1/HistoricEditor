using Xunit;

namespace HistoricEditor.Tests;

public class DateEvidenceBuilderTests
{
    private static readonly HistoricalDateEvidence Imported = new()
    {
        Value = "1452",
        Sources = [new SourceCitation { Title = "Wikipedia in italiano", Url = "https://it.wikipedia.org/wiki/Leonardo" }],
    };

    // Le date scartate all'import (una sola fonte) restano tra le fonti come discordanti; scritte nel campo tornano normali.
    [Fact]
    public void DiscardedDates_AreKept_UntilWrittenInTheField()
    {
        var discarded = new HistoricalDateEvidence { Value = "102 a.C.", Scartata = true, Sources = [new SourceCitation { Title = "Fonte Wikidata Q1420342" }] };
        var kept = new HistoricalDateEvidence { Value = "13/07/100 a.C." };

        var evidence = DateEvidenceBuilder.Build("13/07/100 a.C.", string.Empty, [kept, discarded], null);
        Assert.Equal([("13/07/100 a.C.", false), ("102 a.C.", true)], evidence.Select(e => (e.Value, e.Scartata)));
        Assert.Equal("13/07/100 a.C.", ImportedPersonCreator.FormatDates(evidence));
        Assert.Contains("scartata", DateDiscrepancy.Describe("Nascita", evidence));

        var written = DateEvidenceBuilder.Build("13/07/100 a.C.; 102 a.C.", string.Empty, [kept, discarded], null);
        Assert.All(written, e => Assert.False(e.Scartata));
        Assert.False(DateDiscrepancy.Discarded(written).Count > 0);
    }

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
