using System.Windows.Threading;
using HistoricEditor;

namespace HistoricEditor.Tests;

// Le finestre WPF richiedono un thread STA e una sola Application per processo: tutti i test di interfaccia
// girano sullo stesso thread, che resta attivo con il suo Dispatcher.
internal static class WpfTestThread
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(() =>
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // Le risorse "pack://application" vanno cercate nell'assembly dell'app, non nel test host.
                System.Windows.Application.ResourceAssembly ??= typeof(App).Assembly;
                var app = new App();
                app.InitializeComponent();
                ThemeManager.Apply(AppTheme.Light, persist: false);
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                System.Windows.Threading.Dispatcher.Run();
            }
        })
        { IsBackground = true, Name = "WPF test thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return failure is null ? dispatcher! : throw new InvalidOperationException("Avvio di WPF non riuscito nei test.", failure);
    });

    public static void Run(Action action) => Dispatcher.Value.Invoke(action);
}
