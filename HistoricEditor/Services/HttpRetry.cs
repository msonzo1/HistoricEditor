using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace HistoricEditor;

// Richieste GET con nuovi tentativi su 429/503 (rispettando Retry-After) e segnalazione delle fasi a NetworkActivity,
// condivise dagli import da Wikipedia/Wikidata e dai musei.
internal static class HttpRetry
{
    public const int MaximumRetries = 3;

    // send invia una richiesta (un tentativo) e chiama report(Requesting) subito prima di inviarla: così chi deve
    // prima attendere il proprio turno (la coda di Wikidata) può segnalare anche la fase Queued.
    // read legge la risposta riuscita; la richiesta resta "in corso" finché read non ha finito.
    public static async Task<T> GetAsync<T>(
        string url,
        Func<Action<NetworkPhase>, CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var requestId = NetworkActivity.NewRequestId();
        var host = new Uri(url).Host;
        void Report(NetworkPhase phase, TimeSpan wait = default, int attempt = 0, int statusCode = 0)
            => NetworkActivity.Report(new NetworkEvent(requestId, phase, host, DateTime.UtcNow, wait, attempt, MaximumRetries + 1, statusCode));

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var response = await send(phase => Report(phase), cancellationToken);
                if ((response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) && attempt < MaximumRetries)
                {
                    var delay = RetryDelay(response.Headers.RetryAfter, attempt);
                    Report(NetworkPhase.RetryWaiting, delay, attempt, (int)response.StatusCode);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await read(response, cancellationToken);
            }
        }
        finally
        {
            Report(NetworkPhase.Completed);
        }
    }

    // Caso comune: nessuna coda, un tentativo è una semplice GET.
    public static Task<T> GetAsync<T>(
        HttpClient client,
        string url,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
        => GetAsync(
            url,
            (report, token) =>
            {
                report(NetworkPhase.Requesting);
                return client.GetAsync(url, completion, token);
            },
            read,
            cancellationToken);

    // Attesa indicata dal server (secondi o data), altrimenti crescente a ogni tentativo; minimo 1, massimo 30 secondi.
    internal static TimeSpan RetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        var wait = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2 << attempt));
        if (wait < TimeSpan.FromSeconds(1))
        {
            wait = TimeSpan.FromSeconds(1);
        }

        return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
    }
}
