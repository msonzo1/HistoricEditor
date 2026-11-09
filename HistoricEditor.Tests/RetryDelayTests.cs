using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class RetryDelayTests
{
    [Fact]
    public void UsesServerDelta()
        => Assert.Equal(TimeSpan.FromSeconds(7), HttpRetry.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)), 0));

    [Fact]
    public void CapsAtThirtySeconds()
        => Assert.Equal(TimeSpan.FromSeconds(30), HttpRetry.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5)), 0));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    public void WithoutHeader_GrowsPerAttempt(int attempt, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), HttpRetry.RetryDelay(null, attempt));

    [Fact]
    public void OptionalFetch_TimeoutIsIgnoredButUserCancellationPropagates()
    {
        // Il timeout di HttpClient arriva come TaskCanceledException senza che l'utente abbia annullato.
        Assert.True(WikipediaImportService.IsOptionalFetchFailure(new TaskCanceledException(), CancellationToken.None));
        Assert.True(WikipediaImportService.IsOptionalFetchFailure(new KeyNotFoundException(), CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False(WikipediaImportService.IsOptionalFetchFailure(new TaskCanceledException(), cancelled.Token));
    }

    private sealed class Collector : IProgress<NetworkEvent>
    {
        public List<NetworkEvent> Items { get; } = [];
        public void Report(NetworkEvent value) => Items.Add(value);
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string body = "")
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body) };
        // Attesa minima (1 s) per tenere veloce il test.
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    [Fact]
    public async Task RetriesOn429_ReportsPhasesAndReadsTheSuccessfulResponse()
    {
        var responses = new Queue<HttpResponseMessage>([Response(HttpStatusCode.TooManyRequests), Response(HttpStatusCode.OK, "ok")]);
        var collector = new Collector();
        string result;
        using (NetworkActivity.Observe(collector))
        {
            result = await HttpRetry.GetAsync(
                "https://api.artic.edu/x",
                (report, _) =>
                {
                    report(NetworkPhase.Requesting);
                    return Task.FromResult(responses.Dequeue());
                },
                (response, token) => response.Content.ReadAsStringAsync(token),
                CancellationToken.None);
        }

        Assert.Equal("ok", result);
        Assert.Equal(
            [NetworkPhase.Requesting, NetworkPhase.RetryWaiting, NetworkPhase.Requesting, NetworkPhase.Completed],
            collector.Items.Select(i => i.Phase));
        var retry = collector.Items[1];
        Assert.Equal((429, 0, HttpRetry.MaximumRetries + 1, "api.artic.edu"), (retry.StatusCode, retry.Attempt, retry.MaxAttempts, retry.Host));
        Assert.Single(collector.Items.Select(i => i.RequestId).Distinct());
    }

    [Fact]
    public async Task OtherErrors_AreNotRetriedAndStillComplete()
    {
        var calls = 0;
        var collector = new Collector();
        using (NetworkActivity.Observe(collector))
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => HttpRetry.GetAsync(
                "https://example.org/x",
                (report, _) =>
                {
                    calls++;
                    report(NetworkPhase.Requesting);
                    return Task.FromResult(Response(HttpStatusCode.NotFound));
                },
                (response, token) => response.Content.ReadAsStringAsync(token),
                CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        }

        Assert.Equal(1, calls);
        Assert.Equal(NetworkPhase.Completed, collector.Items[^1].Phase);
    }
}
