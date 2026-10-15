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
    }

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
    }

    private static void LogError(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlberiStorici",
                "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:o}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Registrazione non riuscita: si prosegue comunque mostrando l'errore.
        }
    }
}
