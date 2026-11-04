using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Propone i personaggi indicati da Wikidata che non hanno ancora una scheda (parenti o partecipanti):
// quelli confermati vengono importati per intero e collegati. Facoltativamente importa anche gli eventi.
public partial class ImportPeopleWindow : Window
{
    private readonly PeopleStore store;
    private readonly WikipediaImportService service;
    private readonly IReadOnlyList<MissingRelative> missing;
    private readonly int? mainPersonId;
    private readonly WikipediaImportResult? mainImport;
    private readonly bool importEvents;
    private readonly string wikiTreeId;
    private readonly ObservableCollection<Candidate> candidates = [];
    private CancellationTokenSource? cancellation;
    private bool working;
    private WikiTreeFamily? wikiTreeFamily;
    private IReadOnlyDictionary<string, string> wikiTreeWikidataIds = new Dictionary<string, string>();
    private string wikiTreeNotice = string.Empty;

    public sealed class Candidate : INotifyPropertyChanged
    {
        private bool selected;

        // Vuoto per i parenti presenti solo su WikiTree.
        public required string WikidataId { get; init; }
        public required string Nome { get; init; }
        public required string Ruolo { get; init; }
        public required string Dettaglio { get; init; }
        public bool CanImport { get; init; }

        // Profilo da cui creare la scheda quando manca una voce italiana di Wikipedia.
        public WikiTreeProfile? WikiTree { get; init; }

        public string Key => WikiTree is not null && !UsesWikipedia ? WikiTree.Key : WikidataId;

        public bool UsesWikipedia { get; init; }

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public string AccessibleName => $"{Nome}, {Ruolo}. {Dettaglio}";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Q-id (o identificativo WikiTree, per i parenti senza Wikidata) → Id delle schede create o già presenti.
    public Dictionary<string, int> PeopleIds { get; } = new(StringComparer.Ordinal);

    public string Summary { get; private set; } = string.Empty;

    public ImportPeopleWindow(
        PeopleStore store,
        WikipediaImportService service,
        string intro,
        IReadOnlyList<MissingRelative> missing,
        int? mainPersonId = null,
        WikipediaImportResult? mainImport = null,
        bool importEvents = false,
        string wikiTreeId = "")
    {
        InitializeComponent();
        this.store = store;
        this.service = service;
        this.missing = missing;
        this.mainPersonId = mainPersonId;
        this.mainImport = mainImport;
        this.importEvents = importEvents;
        this.wikiTreeId = mainPersonId is null ? string.Empty : WikiTreeLinks.Normalize(wikiTreeId);
        IntroText.Text = intro;
        CandidatesList.ItemsSource = candidates;
        if (EventsOffered)
        {
            var count = mainImport!.EventIds.Count;
            ImportEventsText.Text = $"Importa anche gli _eventi del personaggio indicati da Wikidata ({(count == 1 ? "1 evento" : $"{count} eventi")}): può richiedere diversi minuti.";
            ImportEventsCheck.IsChecked = SettingsStore.Load().ImportPersonEvents;
            ImportEventsCheck.Visibility = Visibility.Visible;
        }

        Loaded += OnLoaded;
        Closing += (_, _) => cancellation?.Cancel();
    }

    // Gli eventi del personaggio si importano solo su richiesta (opzione ricordata nelle impostazioni).
    private bool EventsOffered => importEvents && mainPersonId is not null && mainImport?.EventIds.Count > 0;

    private bool EventsRequested => EventsOffered && ImportEventsCheck.IsChecked == true;

    private bool CanRun => candidates.Any(c => c.CanImport) || EventsRequested || wikiTreeFamily is not null;

    private void ImportEvents_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var requested = ImportEventsCheck.IsChecked == true;
        SettingsStore.Update(s => s.ImportPersonEvents = requested);
        ImportButton.IsEnabled = !working && CanRun;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (missing.Count == 0 && wikiTreeId.Length == 0)
        {
            if (!EventsOffered)
            {
                DialogResult = false;
                return;
            }

            // Solo eventi: si importano se l'utente lo chiede.
            ImportButton.IsEnabled = CanRun;
            Feedback.Info(StatusText, "Spunta l'opzione per importare gli eventi, oppure Salta.");
            ImportEventsCheck.Focus();
            return;
        }

        SetWorking(true);
        StageText.Text = "Lettura dei nomi da Wikidata…";
        using var cts = new CancellationTokenSource();
        cancellation = cts;
        var loaded = false;
        try
        {
            var list = new List<Candidate>();
            var summaries = missing.Count == 0 ? [] : await service.ReadPeopleSummariesAsync(missing.Select(m => m.WikidataId), cts.Token);
            var byId = summaries.ToDictionary(s => s.WikidataId, StringComparer.Ordinal);
            foreach (var relative in missing)
            {
                var summary = byId.GetValueOrDefault(relative.WikidataId);
                var canImport = summary is { ItalianTitle.Length: > 0 };
                var detail = summary?.Descrizione ?? string.Empty;
                if (!canImport)
                {
                    detail = (detail.Length > 0 ? detail + " · " : string.Empty) + "nessuna voce su Wikipedia in italiano: non importabile";
                }

                list.Add(new Candidate
                {
                    WikidataId = relative.WikidataId,
                    Nome = summary?.Nome ?? relative.WikidataId,
                    Ruolo = $"({relative.Ruolo})",
                    Dettaglio = detail,
                    CanImport = canImport,
                    UsesWikipedia = true,
                });
            }

            if (wikiTreeId.Length > 0)
            {
                StageText.Text = "Lettura dei parenti da WikiTree…";
                await AddWikiTreeCandidatesAsync(list, cts.Token);
            }

            foreach (var candidate in list)
            {
                candidate.Selected = candidate.CanImport;
                candidates.Add(candidate);
            }

            loaded = true;
            var hint = candidates.Any(c => c.CanImport)
                ? "Scegli i personaggi da importare: ciascuno avrà una scheda completa."
                : "Nessuno dei personaggi può essere importato.";
            Feedback.Info(StatusText, wikiTreeNotice.Length > 0 ? $"{wikiTreeNotice} {hint}" : hint);
        }
        catch (OperationCanceledException)
        {
            Feedback.Info(StatusText, "Lettura annullata.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Feedback.Error(StatusText, DescribeError(ex));
        }
        finally
        {
            cancellation = null;
            SetWorking(false);
        }

        if (candidates.Count > 0)
        {
            CandidatesList.Focus();
        }
        else if (EventsOffered)
        {
            // Nessun parente da proporre: gli eventi (e le parentele WikiTree) si completano solo con «Importa».
            ImportEventsCheck.Focus();
        }
        else if (loaded)
        {
            // Nessun parente da proporre: resta il completamento delle parentele tra schede già in archivio con WikiTree.
            await RunAsync([]);
        }
    }

    // Parenti presenti su WikiTree e non in archivio. Chi ha una voce italiana viene importato da Wikipedia,
    // gli altri ricevono una scheda costruita con i dati WikiTree (anche se proposti da Wikidata senza voce italiana).
    // Se WikiTree non risponde restano i soli parenti indicati da Wikidata.
    private async Task AddWikiTreeCandidatesAsync(List<Candidate> list, CancellationToken cancellationToken)
    {
        try
        {
            wikiTreeFamily = await service.ReadWikiTreeFamilyAsync(wikiTreeId, cancellationToken);
            if (wikiTreeFamily is null || mainPersonId is not int mainId)
            {
                wikiTreeFamily = null;
                wikiTreeNotice = "Profilo WikiTree non trovato.";
                return;
            }

            wikiTreeWikidataIds = await service.FindWikidataIdsForWikiTreeAsync(
                wikiTreeFamily.Relatives.Append(wikiTreeFamily.Person).Select(p => p.Key), cancellationToken);
            var extra = WikiTreeLinker.MissingRelatives(store, mainId, wikiTreeFamily, wikiTreeWikidataIds, list.Where(c => c.CanImport).Select(c => c.WikidataId));
            var extraIds = extra.Select(r => r.WikidataId).Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(c => !c.CanImport && extraIds.Contains(c.WikidataId));
            var summaries = extraIds.Count == 0
                ? []
                : await service.ReadPeopleSummariesAsync(extraIds, cancellationToken);
            var byId = summaries.ToDictionary(s => s.WikidataId, StringComparer.OrdinalIgnoreCase);
            foreach (var relative in extra)
            {
                var summary = byId.GetValueOrDefault(relative.WikidataId);
                var profile = relative.Profile;
                var years = DescribeYears(profile);
                if (summary is { ItalianTitle.Length: > 0 })
                {
                    list.Add(new Candidate
                    {
                        WikidataId = relative.WikidataId,
                        Nome = summary.Nome,
                        Ruolo = $"({relative.Ruolo}, da WikiTree)",
                        Dettaglio = summary.Descrizione,
                        CanImport = true,
                        UsesWikipedia = true,
                        WikiTree = profile,
                    });
                    continue;
                }

                list.Add(new Candidate
                {
                    WikidataId = relative.WikidataId,
                    Nome = profile.DisplayName,
                    Ruolo = $"({relative.Ruolo}, da WikiTree)",
                    Dettaglio = (years.Length > 0 ? years + " · " : string.Empty) + "nessuna voce su Wikipedia in italiano: scheda creata con i dati WikiTree",
                    CanImport = true,
                    WikiTree = profile,
                });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            wikiTreeFamily = null;
            wikiTreeNotice = "WikiTree non è raggiungibile: sono proposti solo i parenti indicati da Wikidata.";
        }
    }

    private static string DescribeYears(WikiTreeProfile profile)
    {
        var birth = WikipediaImportService.FormatWikiTreeDate(profile.BirthDate, profile.BirthStatus);
        var death = WikipediaImportService.FormatWikiTreeDate(profile.DeathDate, profile.DeathStatus);
        return birth.Length == 0 && death.Length == 0 ? string.Empty : $"{(birth.Length > 0 ? birth : "?")} – {(death.Length > 0 ? death : "?")}";
    }

    private void SetWorking(bool busy)
    {
        working = busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CandidatesList.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy && CanRun;
        ImportEventsCheck.IsEnabled = !busy;
        SkipButton.IsEnabled = !busy;
        if (!busy)
        {
            WorkProgress.IsIndeterminate = true;
            WorkProgress.Value = 0;
            StageText.Text = string.Empty;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var select = candidates.Any(c => c.CanImport && !c.Selected);
        foreach (var candidate in candidates.Where(c => c.CanImport))
        {
            candidate.Selected = select;
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!working)
        {
            await RunAsync([.. candidates.Where(c => c.CanImport && c.Selected)]);
        }
    }

    // «Salta» chiude senza importare né collegare nulla.
    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (!working)
        {
            DialogResult = false;
        }
    }

    private void CancelWork_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = PeopleIds.Count > 0 || Summary.Length > 0;

    private void ReportProgress(double percent, string text)
    {
        WorkProgress.IsIndeterminate = false;
        WorkProgress.Value = percent;
        StageText.Text = text;
        AutomationProperties.SetHelpText(WorkProgress, $"{percent:0}% completato");
    }

    // Importa i personaggi scelti, li collega (anche con i legami WikiTree) e, se richiesto, importa gli eventi.
    private async Task RunAsync(IReadOnlyList<Candidate> selected)
    {
        if (selected.Count == 0 && wikiTreeFamily is null && !EventsRequested)
        {
            DialogResult = false;
            return;
        }

        SetWorking(true);
        using var cts = new CancellationTokenSource();
        cancellation = cts;
        var created = new List<(int Id, WikipediaImportResult Import)>();
        int fromWikiTree = 0, wikiTreeLinks = 0, skipped = 0, unresolvedPlaces = 0;
        var events = new EventImportSummary(0, 0, 0);
        var error = string.Empty;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var candidate = selected[i];
                ReportProgress(100.0 * i / Math.Max(1, selected.Count), $"Importazione di {candidate.Nome} ({i + 1} di {selected.Count})…");
                if (FindExisting(candidate) is int existing)
                {
                    PeopleIds[candidate.Key] = existing;
                    continue;
                }

                if (!candidate.UsesWikipedia && candidate.WikiTree is { } profile)
                {
                    try
                    {
                        var (person, unresolved) = WikiTreeLinker.Create(store, profile, candidate.WikidataId);
                        PeopleIds[candidate.Key] = person.Id;
                        fromWikiTree++;
                        unresolvedPlaces += unresolved;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                    {
                        skipped++;
                    }

                    continue;
                }

                var qid = candidate.WikidataId;
                var import = await service.ReimportAsync(qid, string.Empty, null, cts.Token);
                if (import is null || (import.WikidataId != qid && store.FindPersonIdByWikidataId(import.WikidataId) is not null))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var (person, unresolved) = ImportedPersonCreator.Create(store, import);
                    PeopleIds[qid] = person.Id;
                    created.Add((person.Id, import));
                    unresolvedPlaces += unresolved;
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    skipped++;
                }
            }

            // Un secondo passaggio collega anche i legami tra le schede appena create.
            if (mainPersonId is int mainId && mainImport is not null)
            {
                ImportedPersonCreator.LinkRelatives(store, mainId, mainImport);
            }

            foreach (var (id, import) in created)
            {
                ImportedPersonCreator.LinkRelatives(store, id, import);
            }

            // WikiTree completa ciò che Wikidata non indica, ad esempio la madre di ciascun figlio.
            if (mainPersonId is int personId && wikiTreeFamily is not null)
            {
                wikiTreeLinks = WikiTreeLinker.Link(store, personId, wikiTreeFamily, wikiTreeWikidataIds);
            }

            if (EventsRequested)
            {
                var people = new List<(int Id, string Name, List<string> EventIds)>();
                if (mainPersonId is int main && mainImport is not null)
                {
                    people.Add((main, HistoricalPerson.ComposeName(mainImport.Name, mainImport.Surname, mainImport.Suffix), mainImport.EventIds));
                }

                people.AddRange(created.Select(c => (c.Id, HistoricalPerson.ComposeName(c.Import.Name, c.Import.Surname, c.Import.Suffix), c.Import.EventIds)));
                for (var i = 0; i < people.Count; i++)
                {
                    var (id, personName, eventIds) = people[i];
                    if (eventIds.Count == 0)
                    {
                        continue;
                    }

                    var index = i;
                    var progress = new Progress<(double Fraction, string Text)>(p => ReportProgress(100.0 * (index + p.Fraction) / people.Count, $"{personName}: {p.Text}"));
                    var summary = await ImportedEventCreator.ImportForPersonAsync(service, store, id, eventIds, progress, cts.Token);
                    events = new EventImportSummary(events.Created + summary.Created, events.Linked + summary.Linked, events.Skipped + summary.Skipped);
                }
            }
        }
        catch (OperationCanceledException)
        {
            error = "Importazione interrotta: le schede già create restano in archivio.";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            error = DescribeError(ex) + " Le schede già create restano in archivio.";
        }
        finally
        {
            cancellation = null;
            SetWorking(false);
        }

        Summary = DescribeResult(created.Count + fromWikiTree, skipped, unresolvedPlaces, events, wikiTreeLinks);
        var message = error.Length > 0 ? $"{error} {Summary}".Trim() : Summary.Length > 0 ? Summary : "Nessuna scheda o evento da aggiungere.";
        if (error.Length > 0)
        {
            Feedback.Error(StatusText, message);
        }
        else
        {
            Feedback.Success(StatusText, message);
        }

        CandidatesList.IsEnabled = false;
        SelectAllButton.Visibility = Visibility.Collapsed;
        ImportButton.Visibility = Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsDefault = true;
        CloseButton.Focus();
    }

    private int? FindExisting(Candidate candidate)
    {
        if (candidate.WikidataId.Length > 0 && store.FindPersonIdByWikidataId(candidate.WikidataId) is int byQid)
        {
            return byQid;
        }

        return candidate.WikiTree is { } profile && store.FindPeopleByWikiTreeIds([profile.Key]).TryGetValue(profile.Key, out var byKey)
            ? byKey
            : null;
    }

    internal static string DescribeResult(int created, int skipped, int unresolvedPlaces, EventImportSummary events, int wikiTreeLinks = 0)
    {
        var parts = new List<string>();
        if (created > 0)
        {
            parts.Add(created == 1 ? "Creata 1 scheda" : $"Create {created} schede");
        }

        if (wikiTreeLinks > 0)
        {
            parts.Add(wikiTreeLinks == 1 ? "1 parentela completata con WikiTree" : $"{wikiTreeLinks} parentele completate con WikiTree");
        }

        if (skipped > 0)
        {
            parts.Add(skipped == 1 ? "1 personaggio non importato" : $"{skipped} personaggi non importati");
        }

        if (unresolvedPlaces > 0)
        {
            parts.Add(unresolvedPlaces == 1 ? "1 luogo da scegliere a mano nelle schede" : $"{unresolvedPlaces} luoghi da scegliere a mano nelle schede");
        }

        if (events.Created > 0)
        {
            parts.Add(events.Created == 1 ? "importato 1 evento" : $"importati {events.Created} eventi");
        }

        if (events.Linked > 0)
        {
            parts.Add(events.Linked == 1 ? "1 partecipazione aggiunta a eventi già registrati" : $"{events.Linked} partecipazioni aggiunte a eventi già registrati");
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts) + ".";
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden }
            => "Wikipedia ha rifiutato la richiesta (troppo traffico). Attendi qualche minuto e riprova.",
        HttpRequestException => "Wikipedia non è raggiungibile. Controlla la connessione e riprova.",
        _ => "Risposta di Wikipedia non riconosciuta. Riprova più tardi.",
    };
}
