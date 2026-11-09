namespace HistoricEditor;

// Fase di una richiesta online: in coda (Wikidata accetta una richiesta alla volta), in corso, in attesa del nuovo tentativo, conclusa.
public enum NetworkPhase
{
    Queued,
    Requesting,
    RetryWaiting,
    Completed,
}

// Cambio di fase di una richiesta; At è l'inizio della fase. Attempt parte da 0; Wait è l'attesa prima del nuovo tentativo.
public sealed record NetworkEvent(long RequestId, NetworkPhase Phase, string Host, DateTime At, TimeSpan Wait = default, int Attempt = 0, int MaxAttempts = 0, int StatusCode = 0);

// Canale "ambientale" per le richieste online: chi avvia un import si mette in ascolto e riceve le fasi
// delle richieste fatte nel suo flusso asincrono, senza passare l'osservatore a ogni metodo.
public static class NetworkActivity
{
    private static readonly AsyncLocal<IProgress<NetworkEvent>?> Observer = new();
    private static long lastRequestId;

    public static IDisposable Observe(IProgress<NetworkEvent> target)
    {
        var previous = Observer.Value;
        Observer.Value = target;
        return new Subscription(previous);
    }

    internal static long NewRequestId() => Interlocked.Increment(ref lastRequestId);

    internal static void Report(NetworkEvent value) => Observer.Value?.Report(value);

    private sealed class Subscription(IProgress<NetworkEvent>? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                Observer.Value = previous;
            }
        }
    }
}

// Tiene le richieste attive e descrive in una riga cosa sta succedendo, con il tempo trascorso dall'inizio.
public sealed class NetworkActivityTracker(DateTime started)
{
    // Sotto questa durata una richiesta è "in corso"; oltre, si mostra da quanto si attende la risposta.
    internal static readonly TimeSpan SlowRequest = TimeSpan.FromSeconds(3);

    private readonly Dictionary<long, NetworkEvent> active = [];

    public void Apply(NetworkEvent value)
    {
        if (value.Phase == NetworkPhase.Completed)
        {
            active.Remove(value.RequestId);
        }
        else
        {
            active[value.RequestId] = value;
        }
    }

    public string Describe(DateTime now)
    {
        var elapsed = $"tempo trascorso {FormatDuration(now - started)}";
        var activity = DescribeActivity(now);
        return activity.Length > 0 ? $"{activity} · {elapsed}" : char.ToUpperInvariant(elapsed[0]) + elapsed[1..];
    }

    private string DescribeActivity(DateTime now)
    {
        // Le attese per i limiti del server hanno la precedenza: sono quelle che fermano la barra più a lungo.
        var retry = active.Values
            .Where(e => e.Phase == NetworkPhase.RetryWaiting)
            .OrderByDescending(e => e.At + e.Wait)
            .FirstOrDefault();
        if (retry is not null)
        {
            var remaining = retry.At + retry.Wait - now;
            var reason = retry.StatusCode == 503 ? "servizio momentaneamente non disponibile" : "troppe richieste";
            var next = $"nuovo tentativo {retry.Attempt + 2} di {retry.MaxAttempts}";
            return remaining > TimeSpan.Zero
                ? $"{SiteName(retry.Host)}: {reason} ({retry.StatusCode}), {next} tra {Math.Ceiling(remaining.TotalSeconds):0} s"
                : $"{SiteName(retry.Host)}: {reason} ({retry.StatusCode}), {next}…";
        }

        var requests = active.Values.Where(e => e.Phase == NetworkPhase.Requesting).OrderBy(e => e.At).ToList();
        if (requests.Count > 0)
        {
            var oldest = requests[0];
            var waited = now - oldest.At;
            var text = waited >= SlowRequest
                ? $"In attesa della risposta di {SiteName(oldest.Host)} da {FormatDuration(waited)}"
                : $"Richiesta a {SiteName(oldest.Host)} in corso";
            return requests.Count > 1 ? $"{text} ({requests.Count} richieste in corso)" : text;
        }

        var queued = active.Values.Where(e => e.Phase == NetworkPhase.Queued).OrderBy(e => e.At).FirstOrDefault();
        return queued is null ? string.Empty : $"In coda per {SiteName(queued.Host)} (una richiesta alla volta)";
    }

    // Nome leggibile del sito; i musei sono riconosciuti dal dominio, perché API e immagini stanno su sottodomini diversi.
    internal static string SiteName(string host) => host.ToLowerInvariant() switch
    {
        "www.wikidata.org" => "Wikidata",
        "query.wikidata.org" => "Wikidata (SPARQL)",
        "api.wikitree.com" => "WikiTree",
        var h when h.EndsWith(".wikipedia.org", StringComparison.Ordinal) => $"Wikipedia ({h[..h.IndexOf('.')]})",
        var h when IsDomain(h, "wikimedia.org") => "Wikimedia Commons",
        var h when IsDomain(h, "metmuseum.org") => "Metropolitan Museum",
        var h when IsDomain(h, "artic.edu") => "Art Institute of Chicago",
        var h when IsDomain(h, "clevelandart.org") => "Cleveland Museum of Art",
        var h when IsDomain(h, "europeana.eu") => "Europeana",
        _ => host,
    };

    private static bool IsDomain(string host, string domain)
        => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    // 0:07, 1:05, 1:02:03: un formato unico evita che il testo cambi forma a ogni secondo.
    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
    }
}
