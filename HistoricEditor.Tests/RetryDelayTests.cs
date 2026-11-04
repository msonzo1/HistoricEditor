using System.Net.Http.Headers;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class RetryDelayTests
{
    [Fact]
    public void UsesServerDelta()
        => Assert.Equal(TimeSpan.FromSeconds(7), WikipediaImportService.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)), 0));

    [Fact]
    public void CapsAtThirtySeconds()
        => Assert.Equal(TimeSpan.FromSeconds(30), WikipediaImportService.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5)), 0));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    public void WithoutHeader_GrowsPerAttempt(int attempt, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), WikipediaImportService.RetryDelay(null, attempt));

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
}
