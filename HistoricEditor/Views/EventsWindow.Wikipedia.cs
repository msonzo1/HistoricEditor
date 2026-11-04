using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Ricerca e import di un evento da Wikipedia in italiano (con date, tipo e partecipanti da Wikidata).
public partial class EventsWindow
{
    private readonly WikipediaImportService wikipediaService = new();
    private CancellationTokenSource? wikiCancellation;
    private bool wikiBusy;

    private void SetWikiBusy(bool busy)
    {
        wikiBusy = busy;
        WikiBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WikiSearchBox.IsEnabled = !busy;
        WikiSearchButton.IsEnabled = !busy;
        WikiResults.IsEnabled = !busy;
        WikiImportButton.IsEnabled = !busy && WikiResults.SelectedItem is WikipediaSearchResult;
        if (!busy)
        {
            WikiStage.Text = string.Empty;
            WikiProgress.IsIndeterminate = true;
            WikiProgress.Value = 0;
            AutomationProperties.SetHelpText(WikiProgress, string.Empty);
        }
    }

    private void WikiCancel_Click(object sender, RoutedEventArgs e) => wikiCancellation?.Cancel();

    private void WikiSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            WikiSearch_Click(sender, e);
        }
    }

    private void WikiResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => WikiImportButton.IsEnabled = !wikiBusy && WikiResults.SelectedItem is WikipediaSearchResult;

    private void WikiResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WikiResults.SelectedItem is WikipediaSearchResult)
        {
            WikiImport_Click(sender, e);
        }
    }

    private void WikiResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && WikiResults.SelectedItem is WikipediaSearchResult)
        {
            e.Handled = true;
            WikiImport_Click(sender, e);
        }
    }

    private async void WikiSearch_Click(object sender, RoutedEventArgs e)
    {
        var query = WikiSearchBox.Text.Trim();
        if (wikiBusy)
        {
            return;
        }

        if (query.Length == 0)
        {
            Feedback.Info(WikiStatus, "Scrivi il nome dell’evento da cercare.");
            WikiSearchBox.Focus();
            return;
        }

        await RunWikiAsync("Ricerca", async token =>
        {
            var results = await wikipediaService.SearchAsync(query, new Progress<string>(s => WikiStage.Text = s), token);
            WikiResults.ItemsSource = results;
            WikiResults.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (results.Count == 0)
            {
                Feedback.Info(WikiStatus, "Nessuna voce trovata.");
                return;
            }

            WikiResults.SelectedIndex = 0;
            Feedback.Success(WikiStatus, $"Trovate {results.Count} voci: scegli quella corretta e premi «Importa».");
        });
        if (WikiResults.Visibility == Visibility.Visible)
        {
            WikiResults.Focus();
        }
    }

    private async void WikiImport_Click(object sender, RoutedEventArgs e)
    {
        if (wikiBusy || WikiResults.SelectedItem is not WikipediaSearchResult chosen)
        {
            return;
        }

        WikipediaEventImport? import = null;
        await RunWikiAsync("Importazione", async token =>
        {
            var progress = new Progress<ImportProgress>(update =>
            {
                WikiProgress.IsIndeterminate = false;
                WikiProgress.Value = update.Percent;
                WikiStage.Text = update.Text;
                AutomationProperties.SetHelpText(WikiProgress, $"{update.Percent:0}% completato");
            });
            import = await wikipediaService.ImportEventAsync(chosen.Title, progress, token);
        });

        if (import is not null)
        {
            ApplyWikiImport(import);
        }
    }

    // Esegue una richiesta a Wikipedia con indicatore di avanzamento, annullamento e messaggi d'errore comuni.
    private async Task RunWikiAsync(string operation, Func<CancellationToken, Task> action)
    {
        SetWikiBusy(true);
        Feedback.Info(WikiStatus, $"{operation} in corso su Wikipedia…");
        using var cancellation = new CancellationTokenSource();
        wikiCancellation = cancellation;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(WikiStatus, cancellation.IsCancellationRequested ? $"{operation} annullata." : "La richiesta a Wikipedia è scaduta. Riprova.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
        {
            Feedback.Error(WikiStatus, "Wikipedia ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.");
        }
        catch (HttpRequestException)
        {
            Feedback.Error(WikiStatus, "Wikipedia non è raggiungibile. Controlla la connessione e riprova.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Error(WikiStatus, "Risposta di Wikipedia non riconosciuta. Riprova più tardi.");
        }
        finally
        {
            wikiCancellation = null;
            if (IsLoaded)
            {
                SetWikiBusy(false);
            }
        }
    }

    private void ApplyWikiImport(WikipediaEventImport import)
    {
        if (import.Disambiguazione)
        {
            Feedback.Info(WikiStatus, $"«{import.Titolo}» è una pagina di disambiguazione: scegli o cerca una voce più specifica (es. con l’anno tra parentesi).");
            WikiResults.Focus();
            return;
        }

        // Evento già registrato con lo stesso elemento Wikidata o lo stesso titolo.
        var existing = items.FirstOrDefault(i =>
            (import.WikidataId.Length > 0 && string.Equals(i.Evento.WikidataId.Trim(), import.WikidataId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(i.Evento.Titolo.Trim(), import.Titolo, StringComparison.CurrentCultureIgnoreCase));
        var target = currentId;
        if (existing is not null && existing.Evento.Id != currentId)
        {
            var answer = MessageBox.Show(this,
                $"L’evento «{existing.Evento.Titolo}» è già registrato.{Environment.NewLine}{Environment.NewLine}Sì: aggiorna l’evento esistente con i dati importati.{Environment.NewLine}No: crea comunque un nuovo evento.{Environment.NewLine}Annulla: non importare.",
                "Evento già presente", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
            {
                Feedback.Info(WikiStatus, "Importazione annullata.");
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SearchBox.Text = string.Empty;
                PersonFilter.IsChecked = false;
                ApplyFilter(existing.Evento.Id);
                target = existing.Evento.Id;
            }
            else
            {
                target = 0;
            }
        }
        else if (currentId != 0 && existing is null)
        {
            var answer = MessageBox.Show(this,
                $"È aperto l’evento «{TitleBox.Text}».{Environment.NewLine}{Environment.NewLine}Sì: aggiornalo con i dati importati.{Environment.NewLine}No: crea un nuovo evento.{Environment.NewLine}Annulla: non importare.",
                "Importa evento", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Cancel)
            {
                Feedback.Info(WikiStatus, "Importazione annullata.");
                return;
            }

            target = answer == MessageBoxResult.Yes ? currentId : 0;
        }

        if (target == 0 && currentId != 0)
        {
            EventList.SelectedItem = null;
            ShowEvent(null);
        }

        var unmatched = FillFromImport(import);
        EditorMessage.Text = string.Empty;
        var placeNote = ApplyImportedPlace(import.Luogo);
        var message = target == 0
            ? "Dati importati in un nuovo evento: controllali e premi «Salva»."
            : "Dati importati nell’evento: controllali e premi «Salva».";
        message += placeNote;
        if (unmatched.Count > 0)
        {
            var (added, summary) = OfferMissingParticipants(import.Titolo, unmatched);
            if (summary.Length > 0)
            {
                message += $" {summary}";
            }

            unmatched = [.. unmatched.Where(p => !added.Contains(p.WikidataId))];
        }

        if (unmatched.Count > 0)
        {
            var names = unmatched.Select(p => p.Nome).ToList();
            message += $" Partecipanti non presenti in archivio: {string.Join(", ", names.Take(8))}{(names.Count > 8 ? "…" : string.Empty)}.";
        }

        Feedback.Success(WikiStatus, message);
        TitleBox.Focus();
    }

    // Propone di creare le schede dei partecipanti mancanti e li aggiunge all'evento; restituisce i Q-id aggiunti.
    private (HashSet<string> Added, string Summary) OfferMissingParticipants(string eventTitle, IReadOnlyList<WikipediaEventParticipant> unmatched)
    {
        var missing = unmatched.Select(p => new MissingRelative(p.WikidataId, p.Ruolo)).ToList();
        var window = new ImportPeopleWindow(store, wikipediaService,
            $"{(missing.Count == 1 ? "Un partecipante" : $"{missing.Count} partecipanti")} di «{eventTitle}» non {(missing.Count == 1 ? "ha" : "hanno")} una scheda in archivio. "
                + "Scegli per chi creare una scheda completa: verrà aggiunto ai partecipanti dell’evento.",
            missing)
        {
            Owner = this,
            Title = "Partecipanti mancanti",
        };
        window.ShowDialog();
        var added = new HashSet<string>(StringComparer.Ordinal);
        if (window.PeopleIds.Count == 0)
        {
            return (added, window.Summary);
        }

        PeopleChanged = true;
        LoadPeople();
        foreach (var participant in unmatched)
        {
            if (!window.PeopleIds.TryGetValue(participant.WikidataId, out var id))
            {
                continue;
            }

            added.Add(participant.WikidataId);
            if (participants.All(p => p.PersonaId != id))
            {
                participants.Add(new ParticipantRow(id, PersonLabel(id), participant.Ruolo));
            }
        }

        return (added, window.Summary);
    }

    // Crea (o riusa) il luogo importato e lo imposta nel modulo; restituisce una nota per il messaggio finale.
    private string ApplyImportedPlace(HistoricalPlaceRecord? place)
    {
        if (place is null || string.IsNullOrWhiteSpace(place.CurrentName))
        {
            return string.Empty;
        }

        try
        {
            var (luogoId, specificoId) = ImportedPlaceResolver.Resolve(store, place, "Luogo dell’evento", this);
            if (luogoId is null && specificoId is null)
            {
                return $" Luogo «{place.CurrentName}» non impostato: sceglilo manualmente.";
            }

            SetPlace(luogoId, specificoId);
            return string.Empty;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            return $" Impossibile salvare il luogo «{place.CurrentName}»: sceglilo manualmente.";
        }
    }

    // Compila il modulo; i partecipanti si aggiungono a quelli già presenti. Restituisce quelli non trovati in archivio.
    private List<WikipediaEventParticipant> FillFromImport(WikipediaEventImport import)
    {
        loading = true;
        TitleBox.Text = import.Titolo;
        if (import.Tipo.Length > 0)
        {
            TypeBox.Text = import.Tipo;
        }

        if (import.Data1.Length > 0 || import.Data2.Length > 0)
        {
            QualifierBox.SelectedItem = DataEvento.Info(import.DataQualificatore);
            Date1Box.Text = import.Data1;
            Date2Box.Text = import.Data2;
            CalendarBox.SelectedItem = HistoricalCalendar.CalendarInfo(import.Calendario);
            StyleBox.SelectedItem = HistoricalCalendar.StyleInfo(HistoricalCalendar.StileComune);
        }

        if (import.Descrizione.Length > 0)
        {
            DescriptionBox.Text = import.Descrizione;
        }

        if (import.WikidataId.Length > 0)
        {
            WikidataBox.Text = import.WikidataId;
        }

        loading = false;
        UpdateDateFields();

        var matches = store.FindPeopleByWikidataIds(import.Partecipanti.Select(p => p.WikidataId));
        var unmatched = new List<WikipediaEventParticipant>();
        foreach (var participant in import.Partecipanti)
        {
            if (!matches.TryGetValue(participant.WikidataId, out var personId))
            {
                unmatched.Add(participant);
                continue;
            }

            var index = participants.ToList().FindIndex(p => p.PersonaId == personId);
            if (index < 0)
            {
                participants.Add(new ParticipantRow(personId, PersonLabel(personId), participant.Ruolo));
            }
            else if (participants[index].Ruolo.Trim().Length == 0)
            {
                participants[index] = new ParticipantRow(personId, participants[index].Nome, participant.Ruolo);
            }
        }

        return unmatched;
    }
}
