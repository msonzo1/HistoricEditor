using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Ricerca e import di un'opera da Wikipedia/Wikidata o dalle collezioni online dei musei.
public partial class ArtworksWindow
{
    private readonly WikipediaImportService wikipediaService = new();
    private CancellationTokenSource? importCancellation;
    private bool importBusy;

    private ArtworkSourceInfo SelectedSource => ImportSourceBox.SelectedItem as ArtworkSourceInfo ?? ArtworkSourceInfo.Tutte[0];

    private void InitializeImport()
    {
        ImportSourceBox.ItemsSource = ArtworkSourceInfo.Tutte;
        ImportSourceBox.SelectedIndex = 0;
        Closed += (_, _) => importCancellation?.Cancel();
    }

    private void ImportExpander_Expanded(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(() => ImportSearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    private void ImportSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        var source = SelectedSource;
        ImportSourceInfo.Text = source.Descrizione;
        EuropeanaKeyPanel.Visibility = source.Fonte == ArtworkSource.Europeana ? Visibility.Visible : Visibility.Collapsed;
        if (source.Fonte == ArtworkSource.Europeana && EuropeanaKeyBox.Text.Length == 0)
        {
            EuropeanaKeyBox.Text = SettingsStore.Load().EuropeanaKey;
        }

        ImportResults.ItemsSource = null;
        ImportResults.Visibility = Visibility.Collapsed;
        ImportButton.IsEnabled = false;
        ImportStatus.Text = string.Empty;
    }

    private void SaveEuropeanaKey_Click(object sender, RoutedEventArgs e)
    {
        var key = EuropeanaKeyBox.Text.Trim();
        SettingsStore.Update(s => s.EuropeanaKey = key);
        Feedback.Success(ImportStatus, key.Length > 0 ? "Chiave Europeana salvata." : "Chiave rimossa: verrà usata quella dimostrativa.");
    }

    private void SetImportBusy(bool busy)
    {
        importBusy = busy;
        ImportBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ImportSourceBox.IsEnabled = !busy;
        ImportSearchBox.IsEnabled = !busy;
        ImportSearchButton.IsEnabled = !busy;
        ImportResults.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy && ImportResults.SelectedItem is ArtworkSearchResult;
        if (!busy)
        {
            ImportStage.Text = string.Empty;
            ImportProgressBar.IsIndeterminate = true;
            ImportProgressBar.Value = 0;
            AutomationProperties.SetHelpText(ImportProgressBar, string.Empty);
        }
    }

    private void ImportCancel_Click(object sender, RoutedEventArgs e) => importCancellation?.Cancel();

    private void ImportSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ImportSearch_Click(sender, e);
        }
    }

    private void ImportResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ImportButton.IsEnabled = !importBusy && ImportResults.SelectedItem is ArtworkSearchResult;

    private void ImportResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ImportResults.SelectedItem is ArtworkSearchResult)
        {
            Import_Click(sender, e);
        }
    }

    private void ImportResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ImportResults.SelectedItem is ArtworkSearchResult)
        {
            e.Handled = true;
            Import_Click(sender, e);
        }
    }

    private MuseumImportService CreateMuseumService() => new(wikipediaService, EuropeanaKeyBox.Text);

    private async void ImportSearch_Click(object sender, RoutedEventArgs e)
    {
        var query = ImportSearchBox.Text.Trim();
        if (importBusy)
        {
            return;
        }

        if (query.Length == 0)
        {
            Feedback.Info(ImportStatus, "Scrivi il titolo o l’artista dell’opera da cercare.");
            ImportSearchBox.Focus();
            return;
        }

        var source = SelectedSource;
        await RunImportAsync("Ricerca", source, async token =>
        {
            IReadOnlyList<ArtworkSearchResult> results = source.Fonte == ArtworkSource.Wikipedia
                ? [.. (await wikipediaService.SearchAsync(query, new Progress<string>(s => ImportStage.Text = s), token))
                    .Select(r => new ArtworkSearchResult(ArtworkSource.Wikipedia, r.Title, r.Title, r.Snippet))]
                : await CreateMuseumService().SearchAsync(source.Fonte, query, token);
            ImportResults.ItemsSource = results;
            ImportResults.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (results.Count == 0)
            {
                Feedback.Info(ImportStatus, "Nessuna opera trovata. Prova con altre parole o con un’altra banca dati.");
                return;
            }

            ImportResults.SelectedIndex = 0;
            Feedback.Success(ImportStatus, $"Trovati {results.Count} risultati: scegli l’opera e premi «Importa».");
        });
        if (ImportResults.Visibility == Visibility.Visible)
        {
            ImportResults.Focus();
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (importBusy || ImportResults.SelectedItem is not ArtworkSearchResult chosen)
        {
            return;
        }

        var source = ArtworkSourceInfo.Tutte.First(s => s.Fonte == chosen.Fonte);
        ArtworkImport? import = null;
        await RunImportAsync("Importazione", source, async token =>
        {
            var progress = new Progress<ImportProgress>(update =>
            {
                ImportProgressBar.IsIndeterminate = false;
                ImportProgressBar.Value = update.Percent;
                ImportStage.Text = update.Text;
                AutomationProperties.SetHelpText(ImportProgressBar, $"{update.Percent:0}% completato");
            });
            import = chosen.Fonte == ArtworkSource.Wikipedia
                ? await wikipediaService.ImportArtworkAsync(chosen.Id, progress, token)
                : await CreateMuseumService().ImportAsync(chosen, progress, token);
        });

        if (import is null || !ApplyImport(import, out var message))
        {
            return;
        }

        if (import.ImmagineUrl.Length > 0 && DownloadImageCheck.IsChecked == true)
        {
            var directory = store.ImagesDirectory;
            var downloaded = false;
            await RunImportAsync("Scaricamento dell’immagine", source, async token =>
            {
                ImportStage.Text = "Scaricamento e compressione dell’immagine…";
                var name = await MuseumImportService.DownloadImageAsync(import.ImmagineUrl, directory, token);
                SetImage(name, $"Immagine scaricata e compressa ({FormatSize(new FileInfo(ArtworkImages.FullPath(directory, name)).Length)}). Salva l’opera per confermare.");
                downloaded = true;
            });
            if (!downloaded)
            {
                message += " Immagine non scaricata.";
            }
        }

        Feedback.Success(ImportStatus, message);
        TitleBox.Focus();
    }

    // Esegue una richiesta online con avanzamento, annullamento e messaggi d'errore comuni.
    private async Task RunImportAsync(string operation, ArtworkSourceInfo source, Func<CancellationToken, Task> action)
    {
        SetImportBusy(true);
        Feedback.Info(ImportStatus, $"{operation} in corso su {source.Nome}…");
        using var cancellation = new CancellationTokenSource();
        importCancellation = cancellation;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(ImportStatus, cancellation.IsCancellationRequested ? $"{operation} annullata." : $"{source.Nome} non ha risposto in tempo. Riprova.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            Feedback.Error(ImportStatus, source.Fonte == ArtworkSource.Europeana && ex.StatusCode == HttpStatusCode.Unauthorized
                ? "Europeana ha rifiutato la chiave: controllala o lascia il campo vuoto."
                : $"{source.Nome} ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.");
        }
        catch (HttpRequestException)
        {
            Feedback.Error(ImportStatus, $"{source.Nome} non è raggiungibile. Controlla la connessione e riprova.");
        }
        catch (InvalidOperationException ex) when (operation.StartsWith("Scaricamento", StringComparison.Ordinal))
        {
            Feedback.Error(ImportStatus, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Feedback.Error(ImportStatus, $"Risposta di {source.Nome} non riconosciuta. Riprova più tardi.");
        }
        finally
        {
            importCancellation = null;
            if (IsLoaded)
            {
                SetImportBusy(false);
            }
        }
    }

    // Compila il modulo con i dati importati; false se l'utente annulla. Il messaggio riassume cosa resta da fare.
    private bool ApplyImport(ArtworkImport import, out string message)
    {
        message = string.Empty;
        if (import.Disambiguazione)
        {
            Feedback.Info(ImportStatus, $"«{import.Titolo}» è una pagina di disambiguazione: scegli o cerca una voce più specifica.");
            ImportResults.Focus();
            return false;
        }

        var existing = items.FirstOrDefault(i =>
            (import.WikidataId.Length > 0 && string.Equals(i.Opera.WikidataId.Trim(), import.WikidataId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(i.Opera.Titolo.Trim(), import.Titolo.Trim(), StringComparison.CurrentCultureIgnoreCase));
        var target = currentId;
        if (existing is not null && existing.Opera.Id != currentId)
        {
            var answer = MessageBox.Show(this,
                $"L’opera «{existing.Opera.Titolo}» è già registrata.{Environment.NewLine}{Environment.NewLine}Sì: aggiorna l’opera esistente con i dati importati.{Environment.NewLine}No: crea comunque una nuova opera.{Environment.NewLine}Annulla: non importare.",
                "Opera già presente", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
            {
                Feedback.Info(ImportStatus, "Importazione annullata.");
                return false;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SearchBox.Text = string.Empty;
                PersonFilter.IsChecked = false;
                ApplyFilter(existing.Opera.Id);
                target = existing.Opera.Id;
            }
            else
            {
                target = 0;
            }
        }
        else if (currentId != 0 && existing is null)
        {
            var answer = MessageBox.Show(this,
                $"È aperta l’opera «{TitleBox.Text}».{Environment.NewLine}{Environment.NewLine}Sì: aggiornala con i dati importati.{Environment.NewLine}No: crea una nuova opera.{Environment.NewLine}Annulla: non importare.",
                "Importa opera", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Cancel)
            {
                Feedback.Info(ImportStatus, "Importazione annullata.");
                return false;
            }

            target = answer == MessageBoxResult.Yes ? currentId : 0;
        }

        if (target == 0)
        {
            // Nuova opera: si riparte da un modulo vuoto, senza l'autore proposto dal filtro sulla persona.
            ArtworkList.SelectedItem = null;
            ShowArtwork(null);
            rows.Clear();
        }

        FillFields(import);
        var unmatched = AddImportedPeople(import.Persone);
        message = target == 0
            ? "Dati importati in una nuova opera: controllali e premi «Salva»."
            : "Dati importati nell’opera: controllali e premi «Salva».";
        message += ApplyImportedPlace(import);

        var withId = unmatched.Where(p => p.WikidataId.Length > 0).ToList();
        if (withId.Count > 0)
        {
            var (added, summary) = OfferMissingPeople(import.Titolo, withId);
            if (summary.Length > 0)
            {
                message += $" {summary}";
            }

            unmatched = [.. unmatched.Where(p => !added.Contains(p.WikidataId))];
        }

        if (unmatched.Count > 0)
        {
            var names = unmatched.Select(p => p.Nome).Distinct().ToList();
            message += $" Persone non presenti in archivio: {string.Join(", ", names.Take(8))}{(names.Count > 8 ? "…" : string.Empty)}.";
        }

        EditorMessage.Text = string.Empty;
        return true;
    }

    private void FillFields(ArtworkImport import)
    {
        TitleBox.Text = import.Titolo;
        static void SetIfAny(TextBox box, string value)
        {
            if (value.Trim().Length > 0)
            {
                box.Text = value.Trim();
            }
        }

        if (import.Tipo.Trim().Length > 0)
        {
            TypeBox.Text = import.Tipo.Trim();
        }

        SetIfAny(DateBox, import.Data);
        SetIfAny(TechniqueBox, import.Tecnica);
        SetIfAny(SizeBox, import.Dimensioni);
        if (import.Note.Trim().Length > 0 && !NotesBox.PlainText.Contains(import.Note.Trim(), StringComparison.Ordinal))
        {
            NotesBox.AppendPlainText(import.Note.Trim());
        }

        SourcesBox.Text = MergeSources(SourcesBox.Text, import.Fonti);
        if (import.WikidataId.Length > 0)
        {
            wikidataId = import.WikidataId;
        }
    }

    // Aggiunge alle fonti esistenti (una per riga) quelle nuove, senza duplicati.
    internal static string MergeSources(string existing, IEnumerable<string> added)
    {
        var lines = existing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var source in added.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            if (!lines.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(source);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    // Abbina le persone alle schede (per Q-id, altrimenti per nome se non ambiguo); restituisce quelle non trovate.
    private List<ArtworkImportPerson> AddImportedPeople(IReadOnlyList<ArtworkImportPerson> imported)
    {
        var matches = store.FindPeopleByWikidataIds(imported.Select(p => p.WikidataId).Where(id => id.Length > 0));
        var unmatched = new List<ArtworkImportPerson>();
        foreach (var person in imported)
        {
            int? personId = matches.TryGetValue(person.WikidataId, out var byId) ? byId : null;
            if (personId is null && person.WikidataId.Length == 0)
            {
                personId = FindUniqueByName(person.Nome);
            }

            if (personId is int id)
            {
                AddRow(id, person.Ruolo);
            }
            else
            {
                unmatched.Add(person);
            }
        }

        return unmatched;
    }

    private int? FindUniqueByName(string name)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var found = people.Values
            .Where(p => compare.Compare(p.Nome.Trim(), name.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0)
            .Select(p => p.Id)
            .Take(2)
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    private void AddRow(int personId, string ruolo)
    {
        if (!rows.Any(r => r.PersonaId == personId && r.Ruolo == ruolo))
        {
            rows.Add(new PersonRow(personId, PersonLabel(personId), ruolo));
        }
    }

    // Propone di creare le schede delle persone mancanti e le collega all'opera; restituisce i Q-id aggiunti.
    private (HashSet<string> Added, string Summary) OfferMissingPeople(string title, IReadOnlyList<ArtworkImportPerson> unmatched)
    {
        var missing = unmatched
            .GroupBy(p => p.WikidataId, StringComparer.Ordinal)
            .Select(g => new MissingRelative(g.Key, string.Join(", ", g.Select(p => OperaRuoli.Info(p.Ruolo).Etichetta.ToLower(CultureInfo.CurrentCulture)))))
            .ToList();
        var window = new ImportPeopleWindow(store, wikipediaService,
            $"{(missing.Count == 1 ? "Una persona collegata" : $"{missing.Count} persone collegate")} a «{title}» non {(missing.Count == 1 ? "ha" : "hanno")} una scheda in archivio. "
                + "Scegli per chi creare una scheda completa da Wikipedia: verrà collegata all’opera con il suo ruolo.",
            missing)
        {
            Owner = this,
            Title = "Artisti e personaggi mancanti",
        };
        window.ShowDialog();
        var added = new HashSet<string>(StringComparer.Ordinal);
        if (window.PeopleIds.Count == 0)
        {
            return (added, window.Summary);
        }

        PeopleChanged = true;
        LoadPeople();
        foreach (var person in unmatched)
        {
            if (window.PeopleIds.TryGetValue(person.WikidataId, out var id))
            {
                added.Add(person.WikidataId);
                AddRow(id, person.Ruolo);
            }
        }

        return (added, window.Summary);
    }

    // Crea (o riusa) la collocazione importata; restituisce una nota per il messaggio finale.
    private string ApplyImportedPlace(ArtworkImport import)
    {
        var place = import.Luogo;
        if (place is null || string.IsNullOrWhiteSpace(place.CurrentName))
        {
            return import.Collocazione.Length > 0 ? $" Collocazione indicata dalla fonte: «{import.Collocazione}»; sceglila con «Scegli luogo»." : string.Empty;
        }

        try
        {
            var (luogoId, specificoId) = ImportedPlaceResolver.Resolve(store, place, "Collocazione dell’opera", this);
            if (luogoId is null && specificoId is null)
            {
                return $" Collocazione «{place.CurrentName}» non impostata: sceglila manualmente.";
            }

            SetPlace(luogoId, specificoId);
            return string.Empty;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            return $" Impossibile salvare la collocazione «{place.CurrentName}»: sceglila manualmente.";
        }
    }
}
