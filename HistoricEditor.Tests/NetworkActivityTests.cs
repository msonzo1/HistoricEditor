using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class NetworkActivityTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Collector : IProgress<NetworkEvent>
    {
        public List<NetworkEvent> Items { get; } = [];
        public void Report(NetworkEvent value) => Items.Add(value);
    }

    [Fact]
    public void Idle_ShowsOnlyElapsedTime()
        => Assert.Equal("Tempo trascorso 1:05", new NetworkActivityTracker(Start).Describe(Start.AddSeconds(65)));

    [Fact]
    public void ShortRequest_IsInProgress()
    {
        var tracker = new NetworkActivityTracker(Start);
        tracker.Apply(new NetworkEvent(1, NetworkPhase.Requesting, "it.wikipedia.org", Start));
        Assert.Equal("Richiesta a Wikipedia (it) in corso · tempo trascorso 0:01", tracker.Describe(Start.AddSeconds(1)));
    }

    [Fact]
    public void SlowRequest_ShowsHowLongItHasBeenWaiting()
    {
        var tracker = new NetworkActivityTracker(Start);
        tracker.Apply(new NetworkEvent(1, NetworkPhase.Requesting, "www.wikidata.org", Start.AddSeconds(2)));
        tracker.Apply(new NetworkEvent(2, NetworkPhase.Requesting, "de.wikipedia.org", Start.AddSeconds(5)));
        Assert.Equal(
            "In attesa della risposta di Wikidata da 0:12 (2 richieste in corso) · tempo trascorso 0:14",
            tracker.Describe(Start.AddSeconds(14)));
    }

    [Fact]
    public void RetryWait_TakesPrecedenceAndCountsDown()
    {
        var tracker = new NetworkActivityTracker(Start);
        tracker.Apply(new NetworkEvent(1, NetworkPhase.Requesting, "it.wikipedia.org", Start));
        tracker.Apply(new NetworkEvent(2, NetworkPhase.RetryWaiting, "www.wikidata.org", Start, TimeSpan.FromSeconds(10), Attempt: 0, MaxAttempts: 4, StatusCode: 429));
        Assert.Equal(
            "Wikidata: troppe richieste (429), nuovo tentativo 2 di 4 tra 7 s · tempo trascorso 0:03",
            tracker.Describe(Start.AddSeconds(3)));
        Assert.Equal(
            "Wikidata: troppe richieste (429), nuovo tentativo 2 di 4… · tempo trascorso 0:11",
            tracker.Describe(Start.AddSeconds(11)));
    }

    [Fact]
    public void Unavailable_IsDescribed()
    {
        var tracker = new NetworkActivityTracker(Start);
        tracker.Apply(new NetworkEvent(1, NetworkPhase.RetryWaiting, "it.wikipedia.org", Start, TimeSpan.FromSeconds(4), Attempt: 1, MaxAttempts: 4, StatusCode: 503));
        Assert.StartsWith("Wikipedia (it): servizio momentaneamente non disponibile (503), nuovo tentativo 3 di 4 tra 4 s", tracker.Describe(Start));
    }

    [Fact]
    public void Queued_And_Completed()
    {
        var tracker = new NetworkActivityTracker(Start);
        tracker.Apply(new NetworkEvent(1, NetworkPhase.Queued, "www.wikidata.org", Start));
        Assert.StartsWith("In coda per Wikidata", tracker.Describe(Start));
        tracker.Apply(new NetworkEvent(1, NetworkPhase.Completed, "www.wikidata.org", Start));
        Assert.Equal("Tempo trascorso 0:00", tracker.Describe(Start));
    }

    [Theory]
    [InlineData("api.wikitree.com", "WikiTree")]
    [InlineData("query.wikidata.org", "Wikidata (SPARQL)")]
    [InlineData("fr.wikipedia.org", "Wikipedia (fr)")]
    [InlineData("api.artic.edu", "Art Institute of Chicago")]
    [InlineData("www.artic.edu", "Art Institute of Chicago")]
    [InlineData("collectionapi.metmuseum.org", "Metropolitan Museum")]
    [InlineData("openaccess-api.clevelandart.org", "Cleveland Museum of Art")]
    [InlineData("api.europeana.eu", "Europeana")]
    [InlineData("upload.wikimedia.org", "Wikimedia Commons")]
    [InlineData("example.org", "example.org")]
    [InlineData("notartic.edu", "notartic.edu")]
    public void SiteNames(string host, string expected) => Assert.Equal(expected, NetworkActivityTracker.SiteName(host));

    [Fact]
    public void FormatDuration_UsesHoursWhenNeeded()
        => Assert.Equal("1:02:03", NetworkActivityTracker.FormatDuration(new TimeSpan(1, 2, 3)));

    [Fact]
    public async Task Observe_FollowsTheAsyncFlowAndIsRestored()
    {
        var outer = new Collector();
        var inner = new Collector();
        using (NetworkActivity.Observe(outer))
        {
            using (NetworkActivity.Observe(inner))
            {
                await Task.Run(() => NetworkActivity.Report(new NetworkEvent(1, NetworkPhase.Requesting, "x", Start)));
            }

            NetworkActivity.Report(new NetworkEvent(2, NetworkPhase.Requesting, "x", Start));
        }

        NetworkActivity.Report(new NetworkEvent(3, NetworkPhase.Requesting, "x", Start));
        Assert.Equal([1L], inner.Items.Select(i => i.RequestId));
        Assert.Equal([2L], outer.Items.Select(i => i.RequestId));
    }
}
