using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Importazione da Wikipedia nella prima parte del modulo, disponibile solo per le schede nuove.
public partial class PersonEditorWindow
{
    private readonly WikipediaImportService wikipediaService = new();
    private WikipediaSearchResult? chosenResult;
    private bool isBusy;
    private CancellationTokenSource? wikipediaCancellation;

    private void UpdateWikipediaAccess()
    {
        WikipediaPanel.Visibility = editingPerson is null ? Visibility.Visible : Visibility.Collapsed;
        RefreshWikipediaButton.Visibility = editingPerson is { } person && (person.WikidataId.Length > 0 || person.WikipediaTitle.Length > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        LoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            LoadingStage.Text = string.Empty;
            ResetProgress(LoadingBar);
            ResetProgress(RefreshBar);
        }

        WikipediaSearchInput.IsEnabled = !busy;
        SearchWikipediaButton.IsEnabled = !busy;
        WikipediaResults.IsEnabled = !busy;
        ImportWikipediaButton.IsEnabled = !busy && chosenResult is not null;
        RefreshWikipediaButton.IsEnabled = !busy;
    }

    private void CancelWikipedia_Click(object sender, RoutedEventArgs e)
    {
        wikipediaCancellation?.Cancel();
        Feedback.Info(WikipediaStatus, "Operazione annullata.");
    }

    // Wikimedia risponde 429 (troppe richieste) o 403 quando blocca il traffico.
    private static bool IsRateLimited(HttpRequestException ex)
        => ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;

    private IProgress<string> CreateProgress() => new Progress<string>(stage => LoadingStage.Text = stage);

    private static IProgress<ImportProgress> CreateImportProgress(ProgressBar bar, TextBlock stage) => new Progress<ImportProgress>(update =>
    {
        bar.IsIndeterminate = false;
        bar.Value = update.Percent;
        stage.Text = update.Text;
        AutomationProperties.SetHelpText(bar, $"{update.Percent:0}% completato");
    });

    private static void ResetProgress(ProgressBar bar)
    {
        bar.IsIndeterminate = true;
        bar.Value = 0;
        AutomationProperties.SetHelpText(bar, string.Empty);
    }

    private async void SearchWikipedia_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        var query = WikipediaSearchInput.Text.Trim();
        if (query.Length == 0)
        {
            Feedback.Info(WikipediaStatus, "Scrivi il nome da cercare su Wikipedia.");
            WikipediaSearchInput.Focus();
            return;
        }

        chosenResult = null;
        SetBusy(true);
        Feedback.Info(WikipediaStatus, "Ricerca in corso su Wikipedia in italiano…");
        using var cancellation = new CancellationTokenSource();
        wikipediaCancellation = cancellation;
        try
        {
            var results = await wikipediaService.SearchAsync(query, CreateProgress(), cancellation.Token);
            WikipediaResults.ItemsSource = results;
            WikipediaResults.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            WikipediaResults.SelectedIndex = -1;
            Feedback.Success(WikipediaStatus, results.Count == 0 ? "Nessuna pagina trovata." : $"Trovate {results.Count} pagine. Fai clic su quella corretta.");
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(WikipediaStatus, cancellation.IsCancellationRequested
                ? "Ricerca annullata."
                : "La richiesta a Wikipedia è scaduta. Riprova.");
        }
        catch (HttpRequestException ex) when (IsRateLimited(ex))
        {
            Feedback.Error(WikipediaStatus, "Wikipedia ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.");
        }
        catch (HttpRequestException)
        {
            Feedback.Error(WikipediaStatus, "Wikipedia non è raggiungibile. Controlla la connessione e riprova.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Error(WikipediaStatus, "Risposta di Wikipedia non riconosciuta. Riprova più tardi.");
        }
        finally
        {
            wikipediaCancellation = null;
            SetBusy(false);
        }
    }

    private async void ImportWikipedia_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || editingPerson is not null)
        {
            return;
        }

        if (chosenResult is not { } selectedResult)
        {
            Feedback.Info(WikipediaStatus, "Scegli prima una pagina Wikipedia.");
            return;
        }

        SetBusy(true);
        Feedback.Info(WikipediaStatus, "Lettura della pagina e delle affermazioni Wikidata…");
        using var cancellation = new CancellationTokenSource();
        wikipediaCancellation = cancellation;
        WikipediaImportResult result;
        try
        {
            result = await wikipediaService.ImportAsync(selectedResult, CreateImportProgress(LoadingBar, LoadingStage), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(WikipediaStatus, cancellation.IsCancellationRequested
                ? "Importazione annullata."
                : "La lettura della pagina è scaduta. Riprova.");
            return;
        }
        catch (HttpRequestException ex) when (IsRateLimited(ex))
        {
            Feedback.Error(WikipediaStatus, "Wikipedia ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.");
            return;
        }
        catch (HttpRequestException)
        {
            Feedback.Error(WikipediaStatus, "Non è stato possibile leggere i dati. Controlla la connessione e riprova.");
            return;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Error(WikipediaStatus, "La risposta ricevuta non contiene dati leggibili.");
            return;
        }
        finally
        {
            wikipediaCancellation = null;
            SetBusy(false);
        }

        if (!ConfirmImportNotDuplicate(result))
        {
            return;
        }

        ApplyImport(result);
        Feedback.Success(WikipediaStatus, result.WikidataId.Length == 0
            ? "Pagina senza collegamento a Wikidata: completa i dati nella scheda."
            : "Dati importati: controllali prima di salvare.");
    }

    private void ApplyImport(WikipediaImportResult import)
    {
        pendingImport = import;
        relativesLinked = false;
        HeaderText.Text = string.IsNullOrWhiteSpace(import.Name) ? "Nuova scheda" : HistoricalPerson.ComposeName(import.Name, import.Surname, import.Suffix);
        HeaderSubtitle.Text = import.WikidataId.Length == 0
            ? "Importata da Wikipedia (senza collegamento a Wikidata): verifica i dati."
            : "Importata da Wikipedia: verifica i dati prima di salvare.";
        PrefillFromImport(import);
        UpdateDateNotice();
        savedSnapshot = null;
        UndoImportButton.Visibility = Visibility.Visible;
    }

    // Scarta l'import e riporta la scheda allo stato vuoto.
    private void UndoImport_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || pendingImport is null || editingPerson is not null)
        {
            return;
        }

        pendingImport = null;
        relativesLinked = false;
        HeaderText.Text = "Nuova scheda";
        HeaderSubtitle.Text = string.Empty;
        PrefillFromImport(new WikipediaImportResult());
        sourceUrl = string.Empty;
        UpdateSourcesSummary();
        UpdateDateNotice();
        savedSnapshot = FormSnapshot();
        UndoImportButton.Visibility = Visibility.Collapsed;
        Feedback.Info(WikipediaStatus, "Import annullato: la scheda è stata svuotata.");
    }

    // Reimporta una scheda già salvata dal suo WikidataId: i dati vanno controllati e poi salvati.
    private async void RefreshWikipedia_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || editingPerson is not { } person)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "I dati principali della scheda (nome, date, luoghi, titoli…) verranno sostituiti con quelli letti da Wikipedia e Wikidata. Le parentele già indicate restano invariate. Le modifiche saranno salvate solo premendo «Salva». Continuare?",
            "Aggiorna da Wikipedia",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        RefreshPanel.Visibility = Visibility.Visible;
        Feedback.Info(RefreshStatus, "Lettura della pagina e delle affermazioni Wikidata…");
        using var cancellation = new CancellationTokenSource();
        wikipediaCancellation = cancellation;
        WikipediaImportResult? result;
        try
        {
            result = await wikipediaService.ReimportAsync(
                person.WikidataId,
                person.WikipediaTitle,
                CreateImportProgress(RefreshBar, RefreshStage),
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(RefreshStatus, cancellation.IsCancellationRequested
                ? "Aggiornamento annullato."
                : "La lettura della pagina è scaduta. Riprova.");
            return;
        }
        catch (HttpRequestException ex) when (IsRateLimited(ex))
        {
            Feedback.Error(RefreshStatus, "Wikipedia ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.");
            return;
        }
        catch (HttpRequestException)
        {
            Feedback.Error(RefreshStatus, "Non è stato possibile leggere i dati. Controlla la connessione e riprova.");
            return;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Error(RefreshStatus, "La risposta ricevuta non contiene dati leggibili.");
            return;
        }
        finally
        {
            wikipediaCancellation = null;
            RefreshPanel.Visibility = Visibility.Collapsed;
            RefreshStage.Text = string.Empty;
            SetBusy(false);
        }

        if (result is null)
        {
            Feedback.Error(RefreshStatus, "Nessuna pagina Wikipedia in italiano collegata a questa scheda.");
            return;
        }

        ApplyImport(result);
        HeaderSubtitle.Text = "Aggiornata da Wikipedia: verifica i dati e premi «Salva».";
        Feedback.Success(RefreshStatus, "Dati aggiornati: controllali prima di salvare.");
    }

    private void WikipediaResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ChooseResult();

    private void WikipediaResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ChooseResult();
        }
    }

    // Il personaggio scelto va nella casella di ricerca e l'elenco dei risultati si chiude.
    private void ChooseResult()
    {
        if (isBusy || WikipediaResults.SelectedItem is not WikipediaSearchResult result)
        {
            return;
        }

        chosenResult = result;
        WikipediaSearchInput.Text = result.Title;
        WikipediaResults.Visibility = Visibility.Collapsed;
        ImportWikipediaButton.IsEnabled = true;
        Feedback.Success(WikipediaStatus, $"Scelto «{result.Title}». Premi «Importa».{ExistingArticleNotice(result.Title)}");
        ImportWikipediaButton.Focus();
    }

    private void WikipediaSearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (chosenResult is not null && WikipediaSearchInput.Text != chosenResult.Title)
        {
            chosenResult = null;
            ImportWikipediaButton.IsEnabled = false;
        }
    }

    private void WikipediaSearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SearchWikipedia_Click(sender, e);
        }
    }
}
