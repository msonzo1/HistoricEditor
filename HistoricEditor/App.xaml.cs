using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HistoricEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);
        ThemeManager.Initialize();
        base.OnStartup(e);

        // L'archivio si apre prima della finestra: se non è utilizzabile l'app si chiude con un messaggio,
        // invece di restare in esecuzione senza alcuna finestra.
        PeopleStore store;
        try
        {
            store = new PeopleStore();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(
                $"Impossibile aprire l’archivio locale:{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Dettagli in: {ErrorLogPath}",
                "Alberi Storici",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            MainWindow = new MainWindow(store);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            store.Dispose();
            LogError(ex);
            MessageBox.Show(
                $"Impossibile avviare l’applicazione:{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Dettagli in: {ErrorLogPath}",
                "Alberi Storici",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlberiStorici",
        "error.log");

    // Un errore in un gestore eventi non deve chiudere l'applicazione: lo si registra e lo si mostra.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            $"Si è verificato un errore imprevisto:{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}",
            "Errore",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;

        // Senza finestre aperte il processo resterebbe attivo ma invisibile.
        if (!Windows.Cast<Window>().Any(w => w.IsVisible))
        {
            Shutdown(1);
        }
    }

    private static void LogError(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var path = ErrorLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:o}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Registrazione non riuscita: si prosegue comunque mostrando l'errore.
        }
    }
}
