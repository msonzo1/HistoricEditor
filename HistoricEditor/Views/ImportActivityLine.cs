using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HistoricEditor;

// Riga sotto la barra di avanzamento: cosa sta facendo la rete (richiesta in corso, coda, attesa e nuovo tentativo)
// e il tempo trascorso, aggiornata ogni secondo. Va creata sul thread dell'interfaccia e chiusa con Dispose
// nello stesso metodo asincrono che avvia l'operazione (l'ascolto segue il suo flusso asincrono).
internal sealed class ImportActivityLine : IDisposable
{
    private readonly TextBlock target;
    private readonly NetworkActivityTracker tracker = new(DateTime.UtcNow);
    private readonly DispatcherTimer timer;
    private readonly IDisposable subscription;
    private bool disposed;

    private ImportActivityLine(TextBlock target)
    {
        this.target = target;
        subscription = NetworkActivity.Observe(new Progress<NetworkEvent>(update =>
        {
            if (!disposed)
            {
                tracker.Apply(update);
                Refresh();
            }
        }));
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), target.Dispatcher);
        target.Visibility = Visibility.Visible;
        Refresh();
        timer.Start();
    }

    public static ImportActivityLine Start(TextBlock target) => new(target);

    private void Refresh()
    {
        if (!disposed)
        {
            target.Text = tracker.Describe(DateTime.UtcNow);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        subscription.Dispose();
        target.Text = string.Empty;
        target.Visibility = Visibility.Collapsed;
    }
}
